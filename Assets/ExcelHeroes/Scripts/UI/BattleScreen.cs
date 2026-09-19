using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 출근 — the line auto-battle. The party holds formation on the left, errors walk in from the
    /// right, and basic attacks resolve themselves. The one thing the player does is fire EX skills
    /// from the bar along the bottom as they charge, which is where the tension lives: spend the
    /// heal now or hold it for the boss wave.
    ///
    /// The screen is a thin renderer over BattleSim — it owns no combat rules, only positions,
    /// health bars and floating numbers.
    /// </summary>
    public class BattleScreen : IScreen
    {
        public string Cell => "A1";
        public string Formula => "=SUMIFS(Sheet2!D:D,Sheet2!A:A,\"Q3\",Sheet2!B:B,\">0\")";

        readonly AppRoot _app;
        VisualElement _root, _stage, _exBar, _resultView, _upgradeBar;
        BattleFx _fx;
        Label _comboLabel;
        Label _waveLabel;
        Button _autoButton;

        // How long the result card stays up before the next run starts on its own. The fight is
        // an idle loop; stopping it on a modal until someone taps 다시 is what made it a menu.
        const float RestartDelay = 2.6f;
        float _restartIn;

        /// <summary>
        /// 히트스톱 — the simulation is held still for a few frames when something big lands.
        ///
        /// It is the oldest trick in action games and the one that does the most for how hard a hit
        /// feels: the eye reads the pause as weight. It lives here rather than in BattleSim on
        /// purpose — the headless bench must keep measuring real fight length, and a freeze that
        /// only the player sees cannot move the balance numbers.
        /// </summary>
        float _hitStop;
        bool _resultApplied;

        // Ash and office paper drifting across the field, ported from drawAsh in cityBackdrop.js.
        // A still battlefield reads as a screenshot; this is what makes it read as weather.
        readonly System.Collections.Generic.List<VisualElement> _motes = new();
        float _moteT;

        ScrollView _log;
        readonly System.Collections.Generic.List<string> _logLines = new();
        BattleSim _sim;

        readonly Dictionary<Combatant, VisualElement> _views = new();
        readonly Dictionary<string, (VisualElement button, VisualElement charge)> _exButtons = new();
        // The y is carried here rather than read back from resolvedStyle: on the frame a floater
        // is created its resolved top is still 0, so reading it sent every damage number to the top
        // of the field and it drifted up from there. The numbers were landing nowhere near the
        // thing that had been hit, which is most of why the hits did not read.
        readonly List<(VisualElement el, float life, float y)> _floaters = new();

        public BattleScreen(AppRoot app) { _app = app; }

        // 홈 리본: the two things a player reaches for mid-run, in Excel's words for them.
        public IEnumerable<RibbonItem> Ribbon()
        {
            yield return new RibbonItem("▶", "선택 영역 재계산", Start, "다시 출근");
            yield return new RibbonItem("Σ", "자동 합계", () => _app.Show(AppRoot.Sheet.Roster), "강화하러");
            yield return RibbonItem.Sep;
            yield return new RibbonItem("▤", "선택 영역", () => _app.Show(AppRoot.Sheet.Roster), "편성");
        }

        public VisualElement Build()
        {
            _root = UiKit.Div("battle");

            var hud = UiKit.Div("battle__hud", _root);
            _waveLabel = UiKit.Text("", "battle__wave", hud);
            // One toggle, called what it is. The ×1–3 speed control went with it: the run now keeps
            // going while you are on another sheet, so the reason to fast-forward was gone.
            _autoButton = UiKit.Btn("AUTO", "auto-toggle", ToggleAuto, hud);

            _stage = UiKit.Div("battle__stage", _root);

            // 콤보 — the count sits over the field, because it is about what is happening there.
            _comboLabel = UiKit.Text("", "combo", _stage);

            // 재계산 예산 — the shared pool EX skills are paid from. Segmented rather than smooth so
            // you can read "two more" at a glance without doing arithmetic mid-fight.
            var costRow = UiKit.Div("cost-row", _root);
            _costValue = UiKit.Text("0", "cost-row__value", costRow);
            var track = UiKit.Div("cost-row__track", costRow);
            for (var i = 0; i < (int)BattleSim.MaxCost; i++)
                _costCells.Add(UiKit.Div("cost-cell", track));

            _exBar = UiKit.Div("ex-bar", _root);

            // 사무실 개선 — the gold sink, under the EX bar rather than on a sheet of its own,
            // because it is meant to be spent from without leaving the fight that earns it.
            _upgradeBar = UiKit.Div("upgrades", _root);

            _log = UiKit.Scroll("battle-log", _root);

            // Attach to the run already in progress instead of restarting it: opening 인사 to spend
            // gold and coming back should not throw away the wave the party was on.
            if (_sim == null || Game.Player.PartyCount() == 0) NewRun(); else AttachViews();
            SyncAutoButton();
            BuildUpgrades();
            return _root;
        }

        /// <summary>
        /// Rebuilds the visible half of the screen against whatever the simulation is currently
        /// doing. Called both when a run starts and when the player comes back to this sheet.
        /// </summary>
        void AttachViews()
        {
            if (_stage == null || _sim == null) return;

            _views.Clear();
            _exButtons.Clear();
            _floaters.Clear();
            _shotViews.Clear();
            _motes.Clear();
            _stage.Clear();
            _comboLabel = UiKit.Text("", "combo", _stage);
            // The effects layer goes in first so the fighters draw over it — a spark belongs
            // behind the thing it came off, not painted across its face.
            _fx = new BattleFx(_stage);
            _exBar.Clear();
            _resultView = null;

            if (_backdropStage != _sim.Stage || _backdrop == null)
            {
                _backdropStage = _sim.Stage;
                _backdrop = CityBackdrop.Build(_backdropStage);
            }
            _stage.style.backgroundImage = new StyleBackground(_backdrop);

            foreach (var h in _sim.Heroes) AddFighterView(h);
            foreach (var m in _sim.Monsters) AddFighterView(m);
            foreach (var h in _sim.Heroes) AddExButton(h);
        }

        Label _costValue;
        readonly System.Collections.Generic.List<VisualElement> _costCells = new();

        void ToggleAuto()
        {
            Game.Player.autoSkill = !Game.Player.autoSkill;
            if (_sim != null) _sim.AutoSkill = Game.Player.autoSkill;
            Game.Touch();
            SyncAutoButton();
        }

        void SyncAutoButton()
        {
            if (_autoButton == null) return;
            _autoButton.EnableInClassList("auto-toggle--on", Game.Player.autoSkill);
        }

        /// <summary>
        /// Starts a run. The simulation can exist without any of the screen — that is the point:
        /// AppRoot ticks this sheet whether or not it is the one on display, so the party keeps
        /// clearing waves while the player is off spending the gold.
        /// </summary>
        bool NewRun()
        {
            if (Game.Player == null || Game.Player.PartyCount() == 0)
            {
                ShowEmptyParty();
                return false;
            }

            // The previous run's result dialog is on the shared overlay; the new run owns the
            // screen, so it closes it rather than leaving a stale scoreboard over the field.
            if (_resultView != null && _resultView.panel != null) _app.CloseOverlay();

            _hitStop = 0f;
            _sim = new BattleSim(Game.Player, Game.Player.stage) { AutoSkill = Game.Player.autoSkill };
            _resultApplied = false;
            _restartIn = 0f;
            AttachViews();
            return true;
        }

        void ShowEmptyParty()
        {
            if (_root == null) return;
            _root.Clear();
            _stage = null;

            var empty = UiKit.Div("battle-empty", _root);
            UiKit.Text("편성된 사원이 없습니다", "battle-empty__title", empty);
            UiKit.Text("먼저 동료를 모집하고, 인사 시트에서 파티를 짜 주세요.", "battle-empty__line", empty);
            UiKit.Btn("모집하러 가기", "btn btn--primary battle-empty__go",
                () => _app.Show(AppRoot.Sheet.Gacha), empty);
        }

        /// <summary>Kept for the 리본's 다시 출근 — it just forces the next run to start now.</summary>
        void Start() => NewRun();

        void AddFighterView(Combatant c)
        {
            // Attaching to a run in progress builds a view for every combatant, and the Spawn
            // events that announced those same combatants are still queued — draining them would
            // build a second view and leave the first orphaned in the corner of the field at 0,0,
            // never laid out again. Which is exactly what the screenshot caught: a monster parked
            // in the sky at the left edge with its health bar under it.
            if (_views.ContainsKey(c)) return;

            // 보스는 3배, 일반 몬스터는 2배 (CLAUDE.md 픽셀 아트 규칙). A boss drawn at wave-enemy
            // size is just a monster with a long health bar; the size is how the fight announces it.
            var classes = "fighter " + (c.side == Side.Hero ? "fighter--hero" : "fighter--monster");
            if (c.boss != null) classes += " fighter--boss";
            else if (c.elite) classes += " fighter--elite";

            var el = UiKit.Div(classes, _stage);
            var bodyEl = UiKit.Div("fighter__body", el);

            if (c.side == Side.Hero)
            {
                // A chibi if one exists, otherwise the card art in a circle. The fallback keeps the
                // fight readable while the sprite set is still being filled in.
                var sprite = GameData.BattleSprite(c.heroId);
                if (sprite != null)
                {
                    bodyEl.AddToClassList("fighter__body--chibi");
                    UiKit.SetArt(bodyEl, sprite);
                }
                else UiKit.SetArt(bodyEl, GameData.CardArt(c.heroId));
            }
            else
            {
                // Monsters were never given any art at all — the element was an empty box. The
                // strips come out of the tileset already facing left, so nothing is mirrored here.
                var mon = GameData.MonsterSprite(c.boss != null ? "boss" : c.typeId);
                if (mon != null)
                {
                    bodyEl.AddToClassList("fighter__body--creature");
                    UiKit.SetArt(bodyEl, mon);
                }
            }
            var bar = UiKit.Div("fighter__hpbar", el);
            UiKit.Div("fighter__hpfill", bar);
            UiKit.Text(c.name, "fighter__name", el);
            _views[c] = el;
        }

        void AddExButton(Combatant h)
        {
            if (h.skillCooldown <= 0f)
            {
                // Below ★2 the skill is still locked; show the slot so the upgrade path is visible.
                var locked = UiKit.Div("ex-button ex-button--spent", _exBar);
                UiKit.SetArt(UiKit.Div("ex-button__art", locked), GameData.CardArt(h.heroId));
                UiKit.Text($"★{GameData.Balance.skillUnlockStar} 해금", "ex-button__label", locked);
                return;
            }

            var def = GameData.Hero(h.heroId);
            var btn = UiKit.Div("ex-button", _exBar);
            UiKit.SetArt(UiKit.Div("ex-button__art", btn), GameData.CardArt(h.heroId));
            var charge = UiKit.Div("ex-button__charge", btn);
            UiKit.Text(BattleSim.CostOf(h).ToString(), "ex-button__cost", btn);
            UiKit.Text(def?.skillName ?? "스킬", "ex-button__label", btn);
            btn.RegisterCallback<ClickEvent>(_ => _sim?.FireSkill(h));
            _exButtons[h.heroId] = (btn, charge);
        }

        /// <summary>Gold changed somewhere else, so the prices on the upgrade strip did too.</summary>
        public void Refresh()
        {
            SyncAutoButton();
            UpdateUpgrades();
        }

        /// <summary>
        /// Driven by AppRoot.Update every frame, on whatever sheet the player is looking at. The
        /// simulation always runs; only the drawing is conditional on this sheet being mounted.
        /// </summary>
        public void Tick(float dt)
        {
            if (Game.Player == null) return;

            if (_sim == null)
            {
                // Nothing to run yet — either the game has just booted, or the party was empty last
                // time we looked. Retry once a second rather than every frame.
                _restartIn -= dt;
                if (_restartIn > 0f) return;
                _restartIn = 1f;
                if (!NewRun()) return;
            }

            if (_hitStop > 0f) _hitStop -= dt;
            else if (!_sim.Finished) _sim.Tick(dt);

            var visible = _root != null && _root.panel != null;
            if (!visible)
            {
                // Off-screen the events still have to be consumed or the queue grows without bound,
                // but nothing is drawn for them.
                _sim.Events.Clear();
                Finish(dt);
                return;
            }

            DrainEvents();
            _fx?.Tick(dt);
            LayoutFighters(dt);
            ApplyShake();
            UpdateCombo();
            UpdateExButtons();
            UpdateFloaters(dt);
            UpdateUpgrades();

            _waveLabel.text = _sim.Finished
                ? (_sim.Won ? "업무 완료" : _sim.TimedOut ? "시간 초과" : "업무 실패")
                : _sim.Enraged
                    ? $"Phase {_sim.Stage} · 웨이브 {_sim.Wave}/{_sim.WaveCount} · 야근 ×{_sim.EnrageMultiplier:F1}"
                    : $"Phase {_sim.Stage} · 웨이브 {_sim.Wave}/{_sim.WaveCount}";
            _waveLabel.EnableInClassList("battle__wave--enraged", !_sim.Finished && _sim.Enraged);

            if (_sim.Finished && _resultView == null) ShowResult();
            Finish(dt);
        }

        /// <summary>
        /// Banks the run's rewards once, then counts down to the next one. Auto-restarting is what
        /// makes this an idle game rather than a level select: a finished fight that waits for a tap
        /// stops earning, and the whole economy below it assumes the fight never stops.
        /// </summary>
        void Finish(float dt)
        {
            if (!_sim.Finished) return;
            if (!_resultApplied) { ApplyResult(); _restartIn = RestartDelay; }

            _restartIn -= dt;
            if (_restartIn <= 0f) NewRun();
        }

        void DrainEvents()
        {
            while (_sim.Events.Count > 0)
            {
                var e = _sim.Events.Dequeue();
                switch (e.kind)
                {
                    case EventKind.Spawn:
                        AddFighterView(e.actor);
                        break;
                    case EventKind.Fx:
                        _fx?.Add(e.fx, e.x, e.y, e.color, e.radius);
                        break;
                    case EventKind.Damage:
                        Float(e.target, e.amount.ToString("N0"), e.crit ? "floater floater--crit" : "floater");
                        // Only the party's own hits get a sound; every monster swing too would be mud.
                        if (e.actor != null && e.actor.side == Side.Hero) AudioService.Play("hit", 0.22f);
                        Pulse(e.actor, "fighter__body--swing", 110);
                        Pulse(e.target, "fighter__body--hurt", 90);
                        // The white flash is what actually sells a hit landing — the eye reads a
                        // one-frame blowout as contact long before it reads a number appearing.
                        Pulse(e.target, "fighter__body--flash", 70);
                        Knock(e.target, e.actor);
                        if (e.crit) _hitStop = Mathf.Max(_hitStop, 0.07f);
                        break;
                    case EventKind.Heal:
                        Float(e.target, "+" + e.amount.ToString("N0"), "floater floater--heal");
                        AudioService.Play("heal", 0.4f);
                        break;
                    case EventKind.Skill:
                        Float(e.actor, e.text, "floater floater--skill");
                        AudioService.Play("skill", 0.55f);
                        // A boss shouting its move gets the floater; a hero firing EX gets the screen.
                        if (e.actor != null && e.actor.side == Side.Hero) PlayCutIn(e.actor, e.text);
                        break;
                    case EventKind.Death:
                        // A kill is the beat worth stopping for; a monster popping mid-stride is
                        // the moment the whole exchange was building to.
                        if (e.target != null && e.target.side == Side.Monster)
                            _hitStop = Mathf.Max(_hitStop, e.target.boss != null ? 0.18f : 0.09f);
                        if (_views.TryGetValue(e.target, out var dead)) dead.AddToClassList("fighter--dead");
                        if (e.target != null && e.target.side == Side.Monster) Log($"{e.target.name} 처리 완료");
                        else if (e.target != null) Log($"{e.target.name} 이탈");
                        break;
                    case EventKind.WaveClear:
                        Log($"웨이브 {_sim.Wave} 정리 — 다음 구간");
                        break;
                    case EventKind.Victory:
                        Log("전 구간 처리 완료");
                        break;
                    case EventKind.Defeat:
                        Log(_sim.TimedOut ? "시간 초과 — 미처리 건 남음" : "처리 실패");
                        break;
                }
            }
        }

        /// <summary>
        /// The battle log, written into the sheet's own rows under the field — which is where the
        /// web build puts it, and what the empty space below the battle was there for. It is also
        /// the part of the screen that carries the disguise: in 위장 모드 these lines become
        /// recalculation progress instead.
        /// </summary>
        void Log(string line)
        {
            if (_log == null) return;
            _logLines.Add(line);
            if (_logLines.Count > 40) _logLines.RemoveAt(0);

            _log.Clear();
            for (var i = 0; i < _logLines.Count; i++)
            {
                var row = UiKit.Div("log-row", _log);
                UiKit.Text((i + 1).ToString(), "log-row__n", row);
                UiKit.Text(_app.Stealth ? StealthLabels.LogLine(i) : _logLines[i], "log-row__text", row);
            }
            _log.scrollOffset = new Vector2(0, float.MaxValue);
        }

        // Kept in step with .fighter / .fighter__body in App.uss: a 16x28 sprite at 4x, on rows tall
        // enough to clear it.
        const float FighterWidth = 72f;

        // The party stands on one line, as in the web build, with a six-pixel stagger so a
        // five-stack still reads as five people. Rows were readable but they turned a side-view
        // brawl into a spreadsheet of duels.
        const float GroundFraction = CityBackdrop.GroundY / (float)CityBackdrop.CanvasH;
        const float StaggerY = 9f;

        Texture2D _backdrop;
        int _backdropStage = -1;

        readonly System.Collections.Generic.Dictionary<Shot, VisualElement> _shotViews = new();

        /// <summary>
        /// Draws whatever is in the air. A ranged hero's attack is a thrown office supply and a
        /// monster's is something dropped on the line; without them a fight between two archers is
        /// two figures standing still while numbers appear.
        /// </summary>
        void LayoutShots(float width, float height)
        {
            foreach (var shot in _sim.Shots)
            {
                if (!_shotViews.TryGetValue(shot, out var el))
                {
                    el = UiKit.Div("shot shot--" + shot.Kind, _stage);
                    _shotViews[shot] = el;
                }

                var k = shot.Progress;
                var x = Mathf.Lerp(shot.From.x, shot.To.x, k);
                el.style.left = Mathf.Clamp01(x / BattleSim.FieldW) * (width - FighterWidth) + FighterWidth * 0.5f;
                // A thrown thing arcs; a slash does not travel at all.
                var arc = shot.Kind == "slash" ? 0f : Mathf.Sin(k * Mathf.PI) * 30f;
                el.style.top = height * GroundFraction - 46f - arc;
            }

            foreach (var pair in _shotViews.ToList())
            {
                if (_sim.Shots.Contains(pair.Key)) continue;
                pair.Value.RemoveFromHierarchy();
                _shotViews.Remove(pair.Key);
            }
        }

        const int MoteCount = 26;

        void LayoutMotes(float width, float height, float dt)
        {
            if (_motes.Count == 0)
                for (var i = 0; i < MoteCount; i++)
                    _motes.Add(UiKit.Div(i % 5 == 0 ? "mote mote--paper" : "mote", _stage));

            _moteT += dt;
            for (var i = 0; i < _motes.Count; i++)
            {
                // Straight from the source: each speck has its own speed from its index, and the
                // whole field wraps, so nothing ever needs spawning or destroying.
                // Wrapped inside the field rather than around it: a mote that starts 20px left of
                // the edge ends 20px past the right one, which the layout audit reads as content
                // hanging off the screen — and on a phone that is usually a real bug, so the
                // decoration should not be the thing that cries wolf.
                var x = (i * 137f + _moteT * (8f + i % 5 * 3f)) % Mathf.Max(1f, width - 12f);
                var y = (i * 71f + _moteT * (14f + i % 3 * 6f)) % Mathf.Max(1f, height - 10f);
                _motes[i].style.left = x;
                _motes[i].style.top = y;
            }
        }

        void LayoutFighters(float dt = 0f)
        {
            var width = _stage.resolvedStyle.width;
            var height = _stage.resolvedStyle.height;
            if (width <= 0f || height <= 0f) return;

            if (_fx != null)
            {
                _fx.ScaleX = width / BattleSim.FieldW;
                _fx.ScaleY = height / CityBackdrop.CanvasH;
            }

            LayoutShots(width, height);
            LayoutMotes(width, height, dt);

            foreach (var (c, el) in _views.ToList())
            {
                if (c.side == Side.Monster && !_sim.Monsters.Contains(c) && !c.Alive)
                {
                    el.RemoveFromHierarchy();
                    _views.Remove(c);
                    continue;
                }

                // The lunge is drawn, not simulated: out during the wind-up, back on the recovery.
                var drawX = c.x;
                if (c.dashT > 0f && c.dashTo != 0f)
                {
                    var k = 1f - c.dashT / 0.45f;
                    var reach = k < 0.3f ? Mathf.Sin(k / 0.3f * Mathf.PI * 0.5f)
                              : k < 0.55f ? 1f
                              : Mathf.Max(0f, 1f - (k - 0.55f) / 0.35f);
                    drawX = Mathf.Lerp(c.homeX, c.dashTo, reach);
                }

                // Clamped by the element's own width, not by the hero width constant. A boss is
                // 176px wide and an office monster's body 84, so a fighter standing at the right
                // edge of the field hung off the right edge of the screen by up to a third of
                // itself — which the field's overflow:hidden then cropped mid-sprite.
                if (_knock.TryGetValue(c, out var knock) && Mathf.Abs(knock) > 0.2f)
                {
                    drawX += knock;
                    _knock[c] = Mathf.MoveTowards(knock, 0f, dt * 60f);
                }

                var t = Mathf.Clamp01(drawX / BattleSim.FieldW);
                var w = el.resolvedStyle.width;
                if (float.IsNaN(w) || w <= 1f) w = FighterWidth;
                el.style.left = Mathf.Clamp(t * (width - FighterWidth), 0f, Mathf.Max(0f, width - w));

                // One fighter per worksheet row. The first version staggered them by 34px, which is
                // a fifth of a sprite's height, so a five-hero party read as one smear — and once
                // the monsters closed in, nine sprites shared the same spot. Rows also mean the
                // side-view line stays a line: x is still distance, y is only identity.
                var lane = Mathf.Max(0, c.side == Side.Hero ? _sim.Heroes.IndexOf(c) : _sim.Monsters.IndexOf(c));
                // The sprite is anchored by its feet, so subtract its height to sit ON the ground.
                var feet = height * GroundFraction + (lane % 2 == 0 ? -StaggerY : StaggerY);
                el.style.top = feet - (c.boss != null ? 105f : 70f);

                var fill = el.Q(className: "fighter__hpfill");
                if (fill != null) fill.style.width = Length.Percent(c.maxHp <= 0 ? 0 : 100f * c.hp / c.maxHp);
                el.EnableInClassList("fighter--dead", !c.Alive);
            }
        }

        void UpdateExButtons()
        {
            foreach (var h in _sim.Heroes)
            {
                if (!_exButtons.TryGetValue(h.heroId, out var pair)) continue;
                pair.charge.style.height = Length.Percent(h.SkillCharge * 100f);
                var affordable = _sim.CanAfford(h);
                pair.button.EnableInClassList("ex-button--ready", h.SkillReady && affordable);
                // Charged but unaffordable is its own state: the card is waiting on budget, not on
                // its own cooldown, and dimming it the same as a dead slot hides that difference.
                pair.button.EnableInClassList("ex-button--poor", h.SkillReady && !affordable);
                pair.button.EnableInClassList("ex-button--spent", !h.Alive);
            }

            var cost = _sim.Cost;
            _costValue.text = ((int)cost).ToString();
            for (var i = 0; i < _costCells.Count; i++)
            {
                _costCells[i].EnableInClassList("cost-cell--full", cost >= i + 1);
                // The partly-charged cell shows the fraction, so the bar moves continuously rather
                // than jumping once a second.
                _costCells[i].EnableInClassList("cost-cell--part", cost > i && cost < i + 1);
            }
        }

        /// <summary>
        /// Flicks a class on for a moment. USS owns the movement; this only decides when. Used for
        /// the attacker's lunge and the target's recoil, which together are what turn two circles
        /// exchanging numbers into something that reads as a hit landing.
        /// </summary>
        /// <summary>
        /// The whole field jolts, as in the web build — `em.shake` translated onto the canvas
        /// before anything is drawn. Random each frame so it reads as a jolt and not as a slide.
        /// </summary>
        void ApplyShake()
        {
            if (_stage == null) return;
            var s = _sim.Shake;
            if (s <= 0.01f)
            {
                _stage.style.translate = new StyleTranslate(new Translate(0, 0));
                return;
            }
            _stage.style.translate = new StyleTranslate(new Translate(
                (Random.value - 0.5f) * s, (Random.value - 0.5f) * s));
        }

        void UpdateCombo()
        {
            if (_comboLabel == null) return;
            var on = _sim.Combo >= 5;
            _comboLabel.text = on ? $"{_sim.Combo} COMBO" : "";
            _comboLabel.EnableInClassList("combo--on", on);
        }

        // How far a hit shoves its target, and how long the shove lasts.
        const float KnockDistance = 9f;
        readonly System.Collections.Generic.Dictionary<Combatant, float> _knock = new();

        /// <summary>
        /// Pushes the target away from whoever hit it, for a moment. Three pixels of recoil is the
        /// difference between an attack that connects and two sprites overlapping.
        /// </summary>
        void Knock(Combatant target, Combatant from)
        {
            if (target == null || from == null) return;
            _knock[target] = from.side == Side.Hero ? KnockDistance : -KnockDistance;
        }

        void Pulse(Combatant who, string cls, long ms)
        {
            if (who == null || !_views.TryGetValue(who, out var el)) return;
            var body = el.Q(className: "fighter__body");
            if (body == null) return;
            body.AddToClassList(cls);
            body.schedule.Execute(() => body.RemoveFromClassList(cls)).ExecuteLater(ms);
        }

        /// <summary>
        /// The EX cut-in. The hero's own illustration sweeps in at full size with the skill name and
        /// the line they shout — the one moment during play where the card art is the whole screen.
        /// It is deliberately short: at roughly a second it punctuates a fight rather than pausing it.
        /// </summary>
        // At most one cut-in in this window. Five heroes on auto fire an EX every couple of
        // seconds between them, so without a gate the band was up more often than it was down and
        // the thing meant to punctuate the fight became the fight.
        const float CutInGap = 7f;
        float _cutInAt = -99f;

        void PlayCutIn(Combatant hero, string skillName)
        {
            var def = GameData.Hero(hero.heroId);
            if (def == null || _root == null) return;

            // Only the rare cards get one, and only if the last has had time to clear. A D-grade
            // basic skill announcing itself with a portrait is what made them all feel cheap.
            var rank = GameData.GradeRank(def.grade);
            if (rank < 3 || Time.time - _cutInAt < CutInGap) return;
            _cutInAt = Time.time;

            // A second cut-in landing on top of the first reads as a glitch, so the last one wins.
            _cutIn?.RemoveFromHierarchy();

            // Inside the battlefield, not over the whole sheet. An EX skill is something that
            // happens on the field; taking the app's full height for it covered the chrome, the
            // upgrade strip and the log, none of which the skill has anything to do with.
            var view = UiKit.Div("cutin clips", _stage ?? _root);
            _cutIn = view;

            var sweep = UiKit.Div("cutin__sweep", view);
            var art = UiKit.Div("cutin__art", view);
            UiKit.SetArt(art, GameData.CardArt(def.id));

            var plate = UiKit.Div("cutin__plate", view);
            UiKit.Text(skillName ?? def.skillName, "cutin__skill", plate);
            if (!string.IsNullOrEmpty(def.ult)) UiKit.Text($"“{def.ult}”", "cutin__line", plate);

            view.schedule.Execute(() =>
            {
                sweep.AddToClassList("cutin__sweep--in");
                art.AddToClassList("cutin__art--in");
                plate.AddToClassList("cutin__plate--in");
            }).ExecuteLater(16);

            view.schedule.Execute(() =>
            {
                art.RemoveFromClassList("cutin__art--in");
                plate.RemoveFromClassList("cutin__plate--in");
                sweep.RemoveFromClassList("cutin__sweep--in");
            }).ExecuteLater(880);

            view.schedule.Execute(() =>
            {
                view.RemoveFromHierarchy();
                if (_cutIn == view) _cutIn = null;
            }).ExecuteLater(1250);
        }

        VisualElement _cutIn;

        void Float(Combatant at, string text, string classes)
        {
            if (at == null || !_views.TryGetValue(at, out var anchor)) return;
            var el = UiKit.Text(text, classes, _stage);
            var top = anchor.resolvedStyle.top;
            if (float.IsNaN(top) || top <= 0f) top = _stage.resolvedStyle.height * GroundFraction - 90f;
            el.style.left = anchor.style.left;
            el.style.top = top;
            _floaters.Add((el, 0.9f, top));
        }

        void UpdateFloaters(float dt)
        {
            for (var i = _floaters.Count - 1; i >= 0; i--)
            {
                var (el, life, y) = _floaters[i];
                life -= dt;
                if (life <= 0f) { el.RemoveFromHierarchy(); _floaters.RemoveAt(i); continue; }

                // A quick pop upward that slows, rather than a constant crawl — the first tenth of
                // a second is where a damage number does its work.
                var k = 1f - life / 0.9f;
                y -= 130f * dt * (1f - k * 0.7f);
                el.style.top = y;
                el.style.opacity = Mathf.Clamp01(life / 0.45f);
                _floaters[i] = (el, life, y);
            }
        }

        /// <summary>
        /// Banks what the run earned. Separate from the card that reports it, because the run also
        /// finishes while the player is on another sheet — the gold has to land either way.
        /// </summary>
        void ApplyResult()
        {
            _resultApplied = true;

            // Counted whether or not the run was won: the player still put those errors down.
            QuestService.Note(Game.Player, "kills", _sim.Kills);
            QuestService.Note(Game.Player, "elite", _sim.EliteKills);
            QuestService.Note(Game.Player, "chests", _sim.ChestsOpened);

            // Gold and dropped gems are per kill, so a failed run still pays for what it cleared.
            Game.Player.gold += _sim.GoldEarned;
            Game.Player.gems += _sim.GemsDropped;

            if (_sim.Won)
            {
                Game.Player.gems += 5 + _sim.GemBonus;   // 행운의 셀 holders pay out here
                if (_sim.Stage >= Game.Player.stage) Game.Player.stage++;
                AffectionService.AwardBattle(Game.Player, _sim.Kills, clearedBoss: true);
                QuestService.Note(Game.Player, "clears");
                QuestService.Note(Game.Player, "boss");
            }

            Game.Touch();
        }

        /// <summary>
        /// The run's result, as the same Excel dialog the rest of the game interrupts with. It was
        /// a panel dropped into the battle layout, so it pushed the field and the EX bar around as
        /// it appeared and read as part of the sheet rather than as a thing that had happened.
        ///
        /// It closes itself when the next run starts, so nothing has to be tapped.
        /// </summary>
        void ShowResult()
        {
            AudioService.Play(_sim.Won ? "victory" : "defeat");

            var dialog = UiKit.Div("xl-dialog result-dialog");
            _resultView = dialog;

            var caption = UiKit.Div("xl-dialog__caption", dialog);
            UiKit.Text("계산 결과", "xl-dialog__caption-title", caption);
            UiKit.Div("spacer", caption);
            UiKit.Btn("✕", "xl-dialog__close", _app.CloseOverlay, caption);

            var body = UiKit.Div("xl-dialog__body", dialog);

            var head = UiKit.Div("xl-dialog__head", body);
            var icon = UiKit.Text(_sim.Won ? "✓" : "!", "xl-dialog__icon", head);
            if (!_sim.Won) icon.style.backgroundColor = new Color(0.77f, 0.25f, 0.18f);
            var headText = UiKit.Div("xl-dialog__head-text", head);
            UiKit.Text(_sim.Won ? "업무 완료" : _sim.TimedOut ? "시간 초과" : "업무 실패",
                "xl-dialog__title", headText);
            UiKit.Text(_sim.Won ? $"다음 구간은 Phase {Game.Player.stage} 입니다."
                                : "이 구간을 한 번 더 돌립니다.", "xl-dialog__subtitle", headText);

            // The payout as worksheet rows, because that is what the rest of the disguise looks like.
            var sheet = UiKit.Div("xl-dialog__sheet", body);
            var gems = _sim.GemsDropped + (_sim.Won ? 5 + _sim.GemBonus : 0);
            Row(sheet, 1, "처리 건수", $"{_sim.Kills:N0}");
            Row(sheet, 2, "골드", $"+{_sim.GoldEarned:N0}", accent: true);
            Row(sheet, 3, "보석", $"+{gems:N0}", accent: true);

            UiKit.Text("잠시 후 자동으로 다시 시작합니다.", "xl-dialog__note", body);

            var foot = UiKit.Div("xl-dialog__foot", dialog);
            UiKit.Btn("편성 보기", "xl-btn", () =>
            {
                _app.CloseOverlay();
                _app.Show(AppRoot.Sheet.Roster);
            }, foot);
            UiKit.Btn("계속", "xl-btn xl-btn--default", _app.CloseOverlay, foot);

            _app.OpenOverlay(dialog);
        }

        static void Row(VisualElement sheet, int n, string label, string value, bool accent = false)
        {
            var row = UiKit.Div("xl-row" + (accent ? " xl-row--grant" : ""), sheet);
            UiKit.Text(n.ToString(), "xl-row__n", row);
            UiKit.Text(label, "xl-row__cell", row);
            UiKit.Text(value, "xl-row__value", row);
        }

        // ---------------------------------------------------------------- 사무실 개선

        readonly System.Collections.Generic.Dictionary<string, (Button Button, Label Level, Label Cost)> _upgradeRows = new();

        /// <summary>
        /// Four permanent, party-wide upgrades bought straight out of the gold the auto-battle is
        /// earning. This is the idle half of the loop: levelling one card at a time in a modal is a
        /// decision, and there is nothing to decide while you are watching a fight run itself.
        /// </summary>
        void BuildUpgrades()
        {
            if (_upgradeBar == null) return;
            _upgradeBar.Clear();
            _upgradeRows.Clear();

            foreach (var def in TeamUpgrades.All)
            {
                var id = def.id;
                var cell = new Button(() =>
                {
                    if (!TeamUpgrades.Buy(Game.Player, id)) return;
                    AudioService.Play("tap", 0.5f);
                    Game.Touch();
                    // The bonuses are baked into the combatants at spawn, so they apply from the
                    // next run. Reporting that is honest and it is also why the price is low.
                    UpdateUpgrades();
                });
                cell.AddToClassList("upgrade");
                _upgradeBar.Add(cell);

                UiKit.Text(def.name, "upgrade__name", cell);
                var level = UiKit.Text("", "upgrade__level", cell);
                var cost = UiKit.Text("", "upgrade__cost", cell);
                _upgradeRows[id] = (cell, level, cost);
            }

            UpdateUpgrades();
        }

        void UpdateUpgrades()
        {
            if (_upgradeRows.Count == 0) return;
            var p = Game.Player;

            foreach (var def in TeamUpgrades.All)
            {
                if (!_upgradeRows.TryGetValue(def.id, out var row)) continue;
                var lv = TeamUpgrades.Level(p, def.id);
                var maxed = TeamUpgrades.AtMax(p, def.id);

                // The bonus, not the level, is what the player is actually buying — so that is the
                // big number, with the level as the small one beside it.
                var bonus = TeamUpgrades.Bonus(p, def.id);
                row.Level.text = def.unit == "pct"
                    ? $"+{bonus * 100f:0.#}%p · Lv{lv}"
                    : $"+{bonus * 100f:0}% · Lv{lv}";
                row.Cost.text = maxed ? "MAX" : $"₩{TeamUpgrades.Cost(p, def.id):N0}";
                row.Button.EnableInClassList("upgrade--ready", TeamUpgrades.CanBuy(p, def.id));
                row.Button.SetEnabled(!maxed);
            }
        }
    }
}
