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
        VisualElement _root, _stage, _exBar, _resultView;
        Label _waveLabel;
        Button _autoButton;
        BattleSim _sim;

        readonly Dictionary<Combatant, VisualElement> _views = new();
        readonly Dictionary<string, (VisualElement button, VisualElement charge)> _exButtons = new();
        readonly List<(VisualElement el, float life)> _floaters = new();

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
            _autoButton = UiKit.Btn("자동 스킬 OFF", "btn btn--ghost", ToggleAuto, hud);

            _stage = UiKit.Div("battle__stage", _root);

            // 재계산 예산 — the shared pool EX skills are paid from. Segmented rather than smooth so
            // you can read "two more" at a glance without doing arithmetic mid-fight.
            var costRow = UiKit.Div("cost-row", _root);
            _costValue = UiKit.Text("0", "cost-row__value", costRow);
            var track = UiKit.Div("cost-row__track", costRow);
            for (var i = 0; i < (int)BattleSim.MaxCost; i++)
                _costCells.Add(UiKit.Div("cost-cell", track));

            _exBar = UiKit.Div("ex-bar", _root);

            Start();
            return _root;
        }

        Label _costValue;
        readonly System.Collections.Generic.List<VisualElement> _costCells = new();

        void ToggleAuto()
        {
            if (_sim == null) return;
            _sim.AutoSkill = !_sim.AutoSkill;
            _autoButton.text = _sim.AutoSkill ? "자동 스킬 ON" : "자동 스킬 OFF";
        }

        void Start()
        {
            _views.Clear();
            _exButtons.Clear();
            _floaters.Clear();
            _stage.Clear();
            _exBar.Clear();
            _resultView = null;

            if (Game.Player.PartyCount() == 0)
            {
                var empty = UiKit.Div("panel", _stage);
                UiKit.Text("편성된 사원이 없습니다.", "section-title", empty);
                UiKit.Text("먼저 모집하고 편성 탭에서 파티를 짜 주세요.", "muted", empty);
                UiKit.Btn("모집하러 가기", "btn btn--primary", () => _app.Show(AppRoot.Sheet.Gacha), empty);
                return;
            }

            _sim = new BattleSim(Game.Player, Game.Player.stage);

            // The street the fight happens on, built once per phase. A blank field made the battle
            // read as a debug view; the web build has never fought on one.
            if (_backdropStage != Game.Player.stage || _backdrop == null)
            {
                _backdropStage = Game.Player.stage;
                _backdrop = CityBackdrop.Build(_backdropStage);
            }
            _stage.style.backgroundImage = new StyleBackground(_backdrop);
            _autoButton.text = "자동 스킬 OFF";

            foreach (var h in _sim.Heroes) AddFighterView(h);
            foreach (var h in _sim.Heroes) AddExButton(h);
        }

        void AddFighterView(Combatant c)
        {
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

        public void Refresh() { }

        /// <summary>Driven by AppRoot.Update while this screen is on top.</summary>
        public void Tick(float dt)
        {
            if (_sim == null) return;
            if (!_sim.Finished) _sim.Tick(dt);

            DrainEvents();
            LayoutFighters();
            UpdateExButtons();
            UpdateFloaters(dt);

            _waveLabel.text = _sim.Finished
                ? (_sim.Won ? "업무 완료" : _sim.TimedOut ? "시간 초과" : "업무 실패")
                : _sim.Enraged
                    ? $"Phase {_sim.Stage} · 웨이브 {_sim.Wave}/{_sim.WaveCount} · 야근 ×{_sim.EnrageMultiplier:F1}"
                    : $"Phase {_sim.Stage} · 웨이브 {_sim.Wave}/{_sim.WaveCount}";
            _waveLabel.EnableInClassList("battle__wave--enraged", !_sim.Finished && _sim.Enraged);

            if (_sim.Finished && _resultView == null) ShowResult();
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
                    case EventKind.Damage:
                        Float(e.target, e.amount.ToString("N0"), "floater");
                        // Only the party's own hits get a sound; every monster swing too would be mud.
                        if (e.actor != null && e.actor.side == Side.Hero) AudioService.Play("hit", 0.22f);
                        Pulse(e.actor, "fighter__body--swing", 110);
                        Pulse(e.target, "fighter__body--hurt", 90);
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
                        if (_views.TryGetValue(e.target, out var dead)) dead.AddToClassList("fighter--dead");
                        break;
                }
            }
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

        void LayoutFighters()
        {
            var width = _stage.resolvedStyle.width;
            var height = _stage.resolvedStyle.height;
            if (width <= 0f || height <= 0f) return;

            foreach (var (c, el) in _views.ToList())
            {
                if (c.side == Side.Monster && !_sim.Monsters.Contains(c) && !c.Alive)
                {
                    el.RemoveFromHierarchy();
                    _views.Remove(c);
                    continue;
                }

                var t = Mathf.Clamp01(c.x / BattleSim.LaneCells);
                el.style.left = t * (width - FighterWidth);

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
        void PlayCutIn(Combatant hero, string skillName)
        {
            var def = GameData.Hero(hero.heroId);
            if (def == null || _root == null) return;

            // A second cut-in landing on top of the first reads as a glitch, so the last one wins.
            _cutIn?.RemoveFromHierarchy();

            var view = UiKit.Div("cutin", _root);
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
            el.style.left = anchor.style.left;
            el.style.top = anchor.style.top;
            _floaters.Add((el, 0.9f));
        }

        void UpdateFloaters(float dt)
        {
            for (var i = _floaters.Count - 1; i >= 0; i--)
            {
                var (el, life) = _floaters[i];
                life -= dt;
                if (life <= 0f) { el.RemoveFromHierarchy(); _floaters.RemoveAt(i); continue; }
                el.style.top = el.resolvedStyle.top - 40f * dt;
                el.style.opacity = Mathf.Clamp01(life / 0.9f);
                _floaters[i] = (el, life);
            }
        }

        void ShowResult()
        {
            AudioService.Play(_sim.Won ? "victory" : "defeat");

            // Counted whether or not the run was won: the player still put those errors down.
            QuestService.Note(Game.Player, "kills", _sim.Kills);
            QuestService.Note(Game.Player, "elite", _sim.EliteKills);
            QuestService.Note(Game.Player, "chests", _sim.ChestsOpened);

            _resultView = UiKit.Div("result", _root);
            var title = UiKit.Text(_sim.Won ? "업무 완료" : "업무 실패",
                "result__title " + (_sim.Won ? "result__title--win" : "result__title--lose"), _resultView);

            if (_sim.Won)
            {
                Game.Player.gold += _sim.GoldEarned;
                var gems = 5 + _sim.GemBonus;          // 행운의 셀 holders pay out here
                Game.Player.gems += gems;
                if (_sim.Stage >= Game.Player.stage) Game.Player.stage++;

                // Everyone who fought gets closer to you. Reported so the bond is visibly a reward
                // for fielding a card rather than a hidden counter.
                AffectionService.AwardBattle(Game.Player, _sim.Kills, clearedBoss: true);

                QuestService.Note(Game.Player, "clears");
                QuestService.Note(Game.Player, "boss");

                UiKit.Text($"골드 +{_sim.GoldEarned:N0} · 보석 +{gems}", null, _resultView);
                UiKit.Text("파티 전원 호감도 상승", "muted", _resultView);
                UiKit.Text($"다음 구간: Phase {Game.Player.stage}", "muted", _resultView);
                Game.Touch();
            }
            else
            {
                UiKit.Text("파티를 보강하고 다시 도전해 보세요.", "muted", _resultView);
            }

            UiKit.Btn("다시", "btn btn--primary", Start, _resultView);
            UiKit.Btn("모집하러 가기", "btn", () => _app.Show(AppRoot.Sheet.Gacha), _resultView);
        }
    }
}
