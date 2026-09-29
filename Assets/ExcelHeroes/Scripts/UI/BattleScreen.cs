using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using ExcelHeroes.World;
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
        VisualElement _root, _stage, _resultView, _upgradeBar, _backdropView;
        VisualElement _exBar, _costBar, _costFill;
        Label _costLabel;
        readonly Dictionary<Combatant, (Button Button, VisualElement Charge, Label CostLabel)> _exButtons = new();
        Button _overtimeButton;
        Label _forecastLabel;
        BattleFx _fx;
        Label _comboLabel;
        Label _waveLabel;
        Button _autoButton;
        Button _speedButton;
        int _speedMultiplier = 1;

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

        // The 3D field (World/BattleWorld): the office set and the SD cast, rendered into a texture
        // that becomes this stage's background. The 2D fighter views stay, stripped to their HP
        // bars, and ride the projected head of each figure so every number still lands on it.
        BattleWorld _world;
        RenderTexture _worldTex;

        readonly Dictionary<Combatant, VisualElement> _views = new();
        // The y is carried here rather than read back from resolvedStyle: on the frame a floater
        // is created its resolved top is still 0, so reading it sent every damage number to the top
        // of the field and it drifted up from there. The numbers were landing nowhere near the
        // thing that had been hit, which is most of why the hits did not read.
        readonly List<(VisualElement el, float life, float y)> _floaters = new();

        public BattleScreen(AppRoot app) { _app = app; Current = this; }

        /// <summary>For the screenshot driver.</summary>
        public static BattleScreen Current { get; private set; }

        /// <summary>For the screenshot driver: an EX cut-in for the strongest hero on the field, guards off.</summary>
        /// <summary>Screenshot driver only: the boss on the field now, part-way down (see BattleSim.DebugBossNow).</summary>
        /// Held until a run is live — a call that lands on a finished run would otherwise do nothing.
        public void DebugAutoSkill(bool on) { if (_sim != null) _sim.AutoSkill = on; }
        public void DebugBoss(float hpFrac) { _pendingBoss = hpFrac; Debug.Log($"[shots] boss requested (sim {(_sim == null ? "none" : _sim.Finished ? "finished" : "live")})"); }
        float _pendingBoss;
        VisualElement _resultPopup;
        VisualElement _armorChip;
        Label _armorText;
        VisualElement _kills;
        int _hudWave = -1, _hudEnrage = -1, _hudAlive = -1, _hudKills = -1, _hudTotal = -1, _hudTime = -1;
        float _hudForecastAt;
        int _hudCost = -1, _hudCostStep = -1, _hudBossN = -1, _hudBossStep = -1;
        readonly List<Combatant> _viewKeys = new();
        readonly List<Shot> _shotKeys = new();
        readonly Dictionary<VisualElement, VisualElement> _hpFill = new();
        Label _costWords;     // the result overlay on screen, if any (NewRun takes it down)
        public string DebugState() => _sim == null ? "no sim" : $"P{_sim.Stage} wave {_sim.Wave}/{_sim.WaveCount} t {_sim.Elapsed:F1} finished {_sim.Finished} boss {(_sim.Monsters.FirstOrDefault(m => m.boss != null) is { } b ? $"{b.hp}/{b.maxHp}" : "-")} bar {(_bossBar == null ? "null" : _bossBar.ClassListContains("hidden") ? "hidden" : "shown")}";

        public string DebugSkill(int i)
        {
            if (_sim == null || _sim.Finished || i >= _sim.Heroes.Count) return null;
            var h = _sim.Heroes[i];
            return _sim.DebugFire(i) ? GameData.Hero(h.heroId)?.skillType : null;
        }

        public void DebugCutIn()
        {
            var h = _sim?.Heroes.OrderByDescending(x => GameData.GradeRank(GameData.Hero(x.heroId)?.grade)).FirstOrDefault();
            if (h == null) return;
            _cutInAt = -999f;
            PlayCutIn(h, null);
        }

        // 홈 리본: the two things a player reaches for mid-run, in Excel's words for them.
        public IEnumerable<RibbonItem> Ribbon()
        {
            yield return new RibbonItem(Icons.Refresh, "선택 영역 재계산", Start, "다시 출근");
            yield return new RibbonItem(Icons.Upgrade, "자동 합계", () => _app.Show(AppRoot.Sheet.Roster), "강화하러");
            yield return RibbonItem.Sep;
            yield return new RibbonItem(Icons.Roster, "선택 영역", () => _app.Show(AppRoot.Sheet.Roster), "편성");
        }

        public VisualElement Build()
        {
            _root = UiKit.Div("battle");

            // The field takes the whole frame and the rest floats on it.
            //
            // Held sideways it used to share the height with a log box: the fight got the top
            // half and an empty white panel got the bottom, which is the layout of a tool, not
            // of a game. A landscape game gives the picture everything and puts the readouts
            // over it.
            // `clips`: the audit's word for "this box crops its children and that is the point".
            // Everything walks in from off-screen here, so without it the audit reports a dozen
            // fighters past the right edge every single run and stops being worth reading.
            _stage = UiKit.Div("battle__stage clips", _root);

            // The backdrop is a child rather than this box's own background image, so it can be
            // taller than the box and hang off the top. Held sideways the sheet is far wider than
            // the street is tall, so the whole 832-unit field fits across and the sky is what gets
            // cut — which is what a side-view game does with a wide frame.
            _backdropView = UiKit.Div("battle__backdrop", _stage);

            // 콤보 — the count sits over the field, because it is about what is happening there.
            _comboLabel = UiKit.Text("", "combo", _stage);

            // 사무실 개선 — the gold sink, under the field rather than on a sheet of its own,
            // because it is meant to be spent from without leaving the fight that earns it.
            // Added after the field so they draw over it: UI Toolkit has no z-index, and the
            // order things are built in is the only thing that decides what is on top.
            // The reference's battle HUD, top right: a dark translucent pill with what is left of
            // the fight (enemies, time), then square glass buttons — speed, AUTO, menu. The run's
            // standing settings (진행 · 안전 · 자동 강화 · 야근) and the office upgrades moved off
            // the field into the menu, which is where the reference keeps everything that is not
            // about the next three seconds.
            var hud = UiKit.Div("battle__hud bhud", _root);
            var pill = UiKit.Div("bhud__pill", hud);
            ModalFrame.Painted(pill, (ctx, r) =>
            {
                var poly = UiPaint.RoundRect(r, r.height * 0.5f, 8);
                UiPaint.Fill(ctx, poly, UiPaint.C(18, 28, 50, 0.66f));
                UiPaint.Stroke(ctx, poly, UiPaint.C(255, 255, 255, 0.22f), 2f);
            });
            _waveLabel = UiKit.Text("", "battle__wave bhud__wave", pill);
            // this Phase's error armour as its own coloured chip (업무 상성), not text run into the wave
            _armorChip = UiKit.Div("bhud__armor", pill);
            ModalFrame.Painted(_armorChip, (ctx, r) =>
            {
                var col = _armorChip.userData is Color c0 ? c0 : Color.gray;
                UiPaint.Fill(ctx, UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.6f, 3f), col);
            });
            _armorText = UiKit.Text("", "bhud__armor-text", _armorChip);
            ModalFrame.Painted(UiKit.Div("bhud__icon", pill), DrawEnemyIcon);
            _enemyLabel = UiKit.Text("", "bhud__num", pill);
            ModalFrame.Painted(UiKit.Div("bhud__icon", pill), DrawClockIcon);
            _timeLabel = UiKit.Text("", "bhud__num bhud__time", pill);

            _speedButton = Square(hud, DrawSpeedIcon, ToggleSpeed, out _speedLabel);
            _autoButton = Square(hud, null, ToggleAuto, out var autoLabel);
            autoLabel.text = "AUTO";
            _autoButton.AddToClassList("bhud__auto");
            Square(hud, DrawMenuIcon, ToggleMenu, out var menuLabel);
            menuLabel.text = "메뉴";

            // Built before the menu so the menu opens over them (build order is the only z-order).
            _bossBar = UiKit.Div("bboss hidden", _root);
            ModalFrame.Painted(_bossBar, DrawBossBar);
            var bossPlate = UiKit.Div("bboss__plate", _bossBar);
            _bossLevel = UiKit.Text("", "bboss__lv", bossPlate);
            _bossName = UiKit.Text("", "bboss__name", bossPlate);
            _bossCount = UiKit.Text("", "bboss__count", _bossBar);

            var kills = _kills = UiKit.Div("bkill", _root);
            // target_3: "격파: 12 / 50" on a glass plate, a navy sword before it
            ModalFrame.Painted(kills, (ctx, r) => Chrome.DrawGlassTile(ctx, r, false));
            UiKit.Text("", "icon bkill__glyph", kills);   // swords
            _killLabel = UiKit.Text("", "bkill__num", kills);

            _menu = UiKit.Div("bmenu hidden", _root);
            ModalFrame.Painted(_menu, (ctx, r) =>
            {
                var poly = UiPaint.RoundRect(r, 16f, 6);
                UiPaint.Shadow(ctx, poly, new Vector2(0f, 6f), UiPaint.C(0, 0, 0, 0.3f), 14f);
                UiPaint.Fill(ctx, poly, UiPaint.C(255, 255, 255, 0.96f));
                var head = UiPaint.RoundRect(Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, r.yMin + 64f), 16f, 6);
                UiPaint.Fill(ctx, head, UiPaint.Vertical(UiPaint.C(40, 62, 108), UiPaint.C(26, 42, 78), r.yMin, r.yMin + 64f));
            });
            UiKit.Text("업무 설정", "bmenu__title", _menu);
            _forecastLabel = UiKit.Text("", "forecast bmenu__forecast", _menu);
            var toggles = UiKit.Div("bmenu__row", _menu);

            // 진행 — whether a win moves the party on, and whether 승산 gets a say.
            _advanceButton = UiKit.Btn("자동 진행", "auto-toggle", ToggleAdvance, toggles);
            _safeButton = UiKit.Btn("안전 진행", "auto-toggle", ToggleSafe, toggles);
            _upgradeButton = UiKit.Btn("자동 강화", "auto-toggle", ToggleAutoUpgrade, toggles);
            // 야근 — the one fight in this game a player chooses to start.
            _overtimeButton = UiKit.Btn("야근", "auto-toggle overtime-btn", StartOvertime, toggles);
            foreach (var t in toggles.Query<Button>(className: "auto-toggle").ToList()) SkewPlate.Apply(t, SkewPlate.Kind.Light);
            // the fight has no top band (as in the reference), so the way back to the lobby is here
            var leave = UiKit.Btn("로비로 나가기", "btn bmenu__leave", () => { AudioService.Play("back", 0.55f); _app.Show(AppRoot.Sheet.Home); }, _menu);
            SkewPlate.Apply(leave, SkewPlate.Kind.Navy);
            UiKit.Text("사무실 개선", "bmenu__sub", _menu);
            _upgradeBar = UiKit.Div("upgrades bmenu__upgrades", _menu);

            _costBar = UiKit.Div("ex-cost-bar", _root);
            ModalFrame.Painted(_costBar, DrawCost);
            _costFill = UiKit.Div("ex-cost-fill", _costBar);
            _costFill.AddToClassList("hidden");
            var disc = UiKit.Div("ex-cost-disc", _costBar);
            UiKit.Text("COST", "ex-cost-disc__word", disc);
            _costLabel = UiKit.Text("0", "ex-cost-text", disc);
            _costWords = UiKit.Text("", "ex-cost-words", _costBar);   // "코스트 07/10" at the gauge's end (target_3)

            _exBar = UiKit.Div("ex-bar", _root);


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
            _floaters.Clear();
            _bubbles.Clear();
            CloseBrace();
            _shotViews.Clear();
            _motes.Clear();
            _stage.Clear();

            // Rebuilt here, not kept from Build(): clearing the stage detaches it, and the field
            // then drew an empty sky over a live reference to an element that was no longer in the
            // tree. The street has been missing since the first run for exactly this reason.
            _backdropView = UiKit.Div("battle__backdrop", _stage);

            _comboLabel = UiKit.Text("", "combo", _stage);
            // The effects layer goes in first so the fighters draw over it — a spark belongs
            // behind the thing it came off, not painted across its face.
            _fx = new BattleFx(_stage);
            _resultView = null;

            if (_backdropStage != _sim.Stage || _backdrop == null)
            {
                _backdropStage = _sim.Stage;
                _backdrop = CityBackdrop.Build(_backdropStage);
            }
            if (_backdropView != null) _backdropView.style.backgroundImage = new StyleBackground(_backdrop);

            _world = BattleWorld.Instance;
            foreach (var h in _sim.Heroes) AddFighterView(h);
            foreach (var m in _sim.Monsters) AddFighterView(m);

            _world.Begin(_sim);
            _worldTex = null;
            _stage.AddToClassList("battle__stage--3d");
            _root?.AddToClassList("battle--3d");

            BuildExBar();
        }

        /// <summary>
        /// 야근 모드 — sixty seconds at a difficulty three stages past anything cleared, scored on
        /// kills. It replaces the running fight and puts it back afterwards, so the idle run loses
        /// only the minute the player spent watching this one.
        /// </summary>
        void StartOvertime()
        {
            if (OvertimeService.Start(Game.Player) == null) return;
            AudioService.Play("boss", 0.7f);

            _hitStop = 0f;
            _restartIn = 0f;
            _resultApplied = true;   // an overtime run never advances the stage
            // Waves are set high enough that the boss wave cannot arrive inside a minute: this is
            // a survival run, and a boss script in the middle of it is a different fight.
            _sim = new BattleSim(Game.Player, OvertimeService.StageFor(Game.Player),
                                 999, GameData.Balance.overtimeCount)
            {
                AutoSkill = Game.Player.autoSkill,
                Overtime = true,
            };
            AttachViews();
            SyncOvertimeButton();
            Log($"야근 모드 시작 — {GameData.Balance.overtimeDuration:0}초");
        }

        void EndOvertime()
        {
            var report = OvertimeService.End(Game.Player);
            SyncOvertimeButton();
            if (report == null) return;

            AudioService.Play("victory", 0.7f);
            Game.Touch();

            var pane = UiKit.Div("onboard__card idle");
            UiKit.Text("야근 종료", "onboard__title", pane);
            UiKit.Text($"처치 {report.Value.Kills} · 엘리트 {report.Value.Elites} · 최고 기록 {report.Value.Best}",
                "muted", pane);

            var value = UiKit.Div("power-readout", pane);
            UiKit.Text($"◈{report.Value.Gems}", "power-readout__value", value);
            UiKit.Text($"강화 카드 +{report.Value.Cards}", "power-readout__label", value);

            UiKit.Btn("확인", "btn btn--primary", () =>
            {
                _app.CloseOverlay();
                // Back to the idle run the minute interrupted.
                _resultApplied = false;
                NewRun();
            }, pane);
            _app.OpenOverlay(pane);
        }

        /// <summary>While a run is on, the wave counter carries its clock instead.</summary>
        void UpdateOvertimeLabel()
        {
            var run = OvertimeService.Active;
            if (run == null || _waveLabel == null) return;
            _waveLabel.text = $"야근 {Mathf.CeilToInt(Mathf.Max(0f, run.Left))}초 · 처치 {run.Kills}";
        }

        void SyncOvertimeButton()
        {
            if (_overtimeButton == null) return;
            var can = OvertimeService.CanStart(Game.Player);
            var blocked = OvertimeService.Blocked(Game.Player);
            UiKit.SetBtnText(_overtimeButton, can ? "야근" : blocked);
            _overtimeButton.SetEnabled(can);
            _overtimeButton.EnableInClassList("auto-toggle--on", OvertimeService.Active != null);
        }

        void ToggleAuto()
        {
            Game.Player.autoSkill = !Game.Player.autoSkill;
            if (_sim != null) _sim.AutoSkill = Game.Player.autoSkill;
            Game.Touch();
            SyncAutoButton();
        }

        void ToggleSpeed()
        {
            _speedMultiplier = _speedMultiplier switch
            {
                1 => 2,
                2 => 3,
                _ => 1,
            };
            if (_speedLabel != null) _speedLabel.text = $"속도 x{_speedMultiplier}";
            SetOn(_speedButton, _speedMultiplier > 1);
            AudioService.Play("tap", 0.5f);
        }

        Button _advanceButton, _safeButton, _upgradeButton;
        Label _speedLabel, _enemyLabel, _timeLabel;
        VisualElement _menu;

        // 보스 HP 바 — Blue Archive's boss HUD, top centre (layout from tools/gen_hudref_gemini.py
        // mock-ups of this very screen): a navy plate with the level and name, a long slanted bar
        // cut into BossBars layers with the next layer's colour under the current one, a pale trail
        // that waits a beat after a hit and then drains, and "xN" for the layers still to go.
        const int BossBars = 10;
        VisualElement _bossBar;
        Label _bossLevel, _bossName, _bossCount;
        Combatant _boss;
        float _bossFrac = 1f, _bossTrail = 1f, _bossTrailHold;
        // 처리 — the kill counter, top left: monsters put down this run over all it will field.
        Label _killLabel;

        void ToggleMenu()
        {
            if (_menu == null) return;
            var open = _menu.ClassListContains("hidden");
            _menu.EnableInClassList("hidden", !open);
            AudioService.Play(open ? "tap" : "back", 0.5f);
            if (open) { SyncAutoButton(); SyncOvertimeButton(); UpdateUpgrades(); }
        }

        /// <summary>A square glass button, the reference's battle HUD shape: painted body, icon, label.</summary>
        static Button Square(VisualElement parent, System.Action<MeshGenerationContext, Rect> icon, System.Action click, out Label label)
        {
            var b = UiKit.Btn("", "bhud__sq", click, parent);
            var bg = UiKit.Div("bhud__sq-bg", b);
            bg.pickingMode = PickingMode.Ignore;
            ModalFrame.Painted(bg, (ctx, r) =>
            {
                // one family (ui_critique 07-BattleHud #3): the same navy glass square for all three;
                // "on" is a lit bar along the foot and a lit label — AUTO amber, speed cyan — not
                // a whole yellow block beside two grey ones
                var on = b.ClassListContains("bhud__sq--on");
                var accent = b.ClassListContains("bhud__auto") ? UiPaint.C(255, 196, 0) : UiPaint.C(0, 229, 255);
                var poly = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.45f, 5f);   // the HUD's slant language
                UiPaint.Fill(ctx, poly, UiPaint.Vertical(UiPaint.C(26, 40, 66, on ? 0.92f : 0.8f), UiPaint.C(14, 24, 44, on ? 0.92f : 0.8f), r.yMin, r.yMax));
                UiPaint.Stroke(ctx, poly, on ? WithAlpha(accent, 0.85f) : UiPaint.C(255, 255, 255, 0.28f), 1.5f);
                // AUTO on is the one lit key: the whole face amber (the convention players read at a glance)
                if (on && b.ClassListContains("bhud__auto")) UiPaint.Fill(ctx, poly, UiPaint.Vertical(UiPaint.C(255, 214, 64), UiPaint.C(255, 180, 20), r.yMin, r.yMax));
                if (on) UiPaint.Fill(ctx, UiPaint.Clip(poly, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin - 4f, r.yMax - 6f, r.xMax + 4f, r.yMax + 4f), 0f)), accent, 0f);
            });
            if (icon != null)
            {
                var ic = UiKit.Div("bhud__sq-icon", b);
                ic.pickingMode = PickingMode.Ignore;
                ModalFrame.Painted(ic, icon);
            }
            label = UiKit.Text("", "bhud__sq-label", b);
            label.pickingMode = PickingMode.Ignore;
            return b;
        }

        static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        static void SetOn(Button b, bool on)
        {
            if (b == null) return;
            b.EnableInClassList("bhud__sq--on", on);
            b.Q(className: "bhud__sq-bg")?.MarkDirtyRepaint();
        }

        static void DrawSpeedIcon(MeshGenerationContext ctx, Rect r)
        {
            var c = UiPaint.C(255, 255, 255);
            var h = r.height * 0.5f; var y = r.center.y; var x = r.center.x - h * 0.55f;
            for (var i = 0; i < 2; i++)
            {
                var x0 = x + i * h * 0.55f;
                UiPaint.Fill(ctx, new List<Vector2> { new(x0, y - h * 0.5f), new(x0 + h * 0.55f, y), new(x0, y + h * 0.5f) }, c);
            }
        }

        static void DrawMenuIcon(MeshGenerationContext ctx, Rect r)
        {
            var c = UiPaint.C(255, 255, 255);
            var w = r.width * 0.46f; var x0 = r.center.x - w * 0.5f;
            for (var i = -1; i <= 1; i++)
                UiPaint.Fill(ctx, UiPaint.RoundRect(new Rect(x0, r.center.y + i * r.height * 0.18f - 2.5f, w, 5f), 2.5f), c);
        }

        static void DrawEnemyIcon(MeshGenerationContext ctx, Rect r)
        {
            var c = r.center; var rad = r.height * 0.42f;
            UiPaint.Fill(ctx, UiPaint.Ellipse(c, rad, rad * 0.92f), UiPaint.C(240, 90, 90));
            UiPaint.Fill(ctx, UiPaint.Ellipse(c + new Vector2(-rad * 0.35f, -rad * 0.1f), rad * 0.22f, rad * 0.26f), UiPaint.C(255, 255, 255));
            UiPaint.Fill(ctx, UiPaint.Ellipse(c + new Vector2(rad * 0.35f, -rad * 0.1f), rad * 0.22f, rad * 0.26f), UiPaint.C(255, 255, 255));
        }

        /// <summary>Tracks the living boss: shows the bar, and drives the fill, the trail and the layer count.</summary>
        void UpdateBoss(float dt)
        {
            if (_bossBar == null) return;
            var boss = _sim.Finished ? null : _sim.Monsters.FirstOrDefault(m => m.boss != null && m.Alive);
            _bossBar.EnableInClassList("hidden", boss == null);
            // the combo line sits where the bar goes; it steps down while a boss is up
            _comboLabel?.EnableInClassList("combo--low", boss != null);
            if (boss == null) { _boss = null; return; }
            var frac = Mathf.Clamp01(boss.hp / (float)Mathf.Max(1, boss.maxHp));
            if (boss != _boss)
            {
                _boss = boss;
                _bossLevel.text = "보스";
                _bossName.text = boss.name;
                _bossFrac = _bossTrail = frac; _bossTrailHold = 0f;
            }
            // a hit that opens a fresh chunk: the trail waits a beat, then drains — faster the
            // further behind it is, so a steady stream of hits cannot hold it up for ever
            if (frac < _bossFrac - 1e-5f && _bossTrail <= _bossFrac + 1e-5f) _bossTrailHold = 0.35f;
            _bossFrac = frac;
            if (_bossTrailHold > 0f) _bossTrailHold -= dt;
            else _bossTrail = Mathf.MoveTowards(_bossTrail, frac, dt * Mathf.Max(0.05f, (_bossTrail - frac) * 3f));
            _bossTrail = Mathf.Max(_bossTrail, frac);
            var n = Mathf.Max(1, Mathf.CeilToInt(frac * BossBars - 1e-4f));
            if (n != _hudBossN) { _hudBossN = n; _bossCount.text = $"x{n}"; }
            var bstep = Mathf.RoundToInt(frac * 600f) * 1000 + Mathf.RoundToInt(_bossTrail * 600f);
            if (bstep != _hudBossStep) { _hudBossStep = bstep; _bossBar.MarkDirtyRepaint(); }
        }

        /// <summary>
        /// The boss bar: the plate is drawn behind its labels (top left), the bar under it with
        /// the layer badge at its right end. Within the current layer: the next layer's dark red
        /// (or an empty well on the last one), the pale trail, then the live red fill.
        /// </summary>
        void DrawBossBar(MeshGenerationContext ctx, Rect r)
        {
            // target_3's layout: a big diamond at the left carrying xN, a navy name plate right of
            // it, the bar under the plate — the layers in cyan (not the red it was: the reference
            // mock reads cooler and the red fought the damage numbers)
            const float plateH = 48f, barH = 40f, dia = 124f;
            var x0 = r.xMin + dia * 0.62f;
            var plate = Rect.MinMaxRect(x0, r.yMin, Mathf.Min(r.xMax, x0 + 640f), r.yMin + plateH);
            var ps = UiPaint.SkewRect(plate, SkewPlate.SlantFor(plateH), 4f, 3);
            UiPaint.Shadow(ctx, ps, new Vector2(0f, 3f), UiPaint.C(6, 12, 30, 0.3f), 6f);
            UiPaint.Fill(ctx, ps, UiPaint.Vertical(UiPaint.C(40, 60, 104), UiPaint.C(22, 36, 68), plate.yMin, plate.yMax));
            var edge = UiPaint.Clip(ps, UiPaint.RoundRect(Rect.MinMaxRect(plate.xMin - 40f, plate.yMax - 4f, plate.xMax + 40f, plate.yMax), 0f));
            UiPaint.Fill(ctx, edge, UiPaint.C(64, 214, 255), 0f);

            var bar = Rect.MinMaxRect(x0 + 10f, r.yMin + plateH + 8f, r.xMax, r.yMin + plateH + 8f + barH);
            var outer = UiPaint.SkewRect(bar, SkewPlate.SlantFor(barH) * 0.8f, 4f, 3);
            UiPaint.Shadow(ctx, outer, new Vector2(0f, 3f), UiPaint.C(6, 12, 30, 0.35f), 8f);
            UiPaint.Fill(ctx, outer, UiPaint.C(255, 255, 255, 0.95f));
            var well = UiPaint.Offset(outer, -3f);
            var left = _bossFrac * BossBars;
            var n = Mathf.Max(1, Mathf.CeilToInt(left - 1e-4f));
            var inLayer = Mathf.Clamp01(left - (n - 1));
            var trail = Mathf.Clamp(_bossTrail * BossBars - (n - 1), inLayer, 1f);
            UiPaint.Fill(ctx, well, n > 1 ? UiPaint.C(18, 78, 120) : UiPaint.C(26, 32, 52));
            Rect Upto(float t) => Rect.MinMaxRect(bar.xMin - 20f, bar.yMin - 4f, Mathf.Lerp(bar.xMin + 3f, bar.xMax - 3f, t), bar.yMax + 4f);
            if (trail > inLayer + 1e-4f) UiPaint.Fill(ctx, UiPaint.Clip(well, UiPaint.RoundRect(Upto(trail), 0f)), UiPaint.C(255, 228, 120));
            if (inLayer > 0f)
            {
                var fill = UiPaint.Clip(well, UiPaint.RoundRect(Upto(inLayer), 0f));
                UiPaint.Fill(ctx, fill, UiPaint.Vertical(UiPaint.C(120, 236, 255), UiPaint.C(26, 170, 236), bar.yMin, bar.yMax));
                var gloss = UiPaint.Clip(fill, UiPaint.RoundRect(Rect.MinMaxRect(bar.xMin - 20f, bar.yMin, bar.xMax, bar.yMin + barH * 0.34f), 0f));
                UiPaint.Fill(ctx, gloss, UiPaint.C(255, 255, 255, 0.28f));
            }
            // the layer ticks: one hairline per remaining layer along the bar's foot
            for (var i = 1; i < BossBars; i++)
            {
                var tx = Mathf.Lerp(bar.xMin + 3f, bar.xMax - 3f, i / (float)BossBars);
                UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(tx - 1f, bar.yMax - 9f, tx + 1f, bar.yMax - 3f), 0f), UiPaint.C(255, 255, 255, 0.55f));
            }

            // the diamond: gold rim, cyan-to-navy face, over the left end
            var c = new Vector2(r.xMin + dia * 0.5f, r.yMin + (plateH + 8f + barH) * 0.5f + 6f);
            var d = new List<Vector2> { c + new Vector2(0f, -dia * 0.5f), c + new Vector2(dia * 0.5f, 0f), c + new Vector2(0f, dia * 0.5f), c + new Vector2(-dia * 0.5f, 0f) };
            UiPaint.Shadow(ctx, d, new Vector2(0f, 4f), UiPaint.C(6, 12, 30, 0.4f), 8f);
            UiPaint.Fill(ctx, d, UiPaint.Vertical(UiPaint.C(255, 232, 140), UiPaint.C(210, 150, 40), c.y - dia * 0.5f, c.y + dia * 0.5f));
            UiPaint.Fill(ctx, UiPaint.Offset(d, -6f), UiPaint.Vertical(UiPaint.C(72, 214, 255), UiPaint.C(22, 60, 118), c.y - dia * 0.5f, c.y + dia * 0.5f));
            UiPaint.Fill(ctx, UiPaint.Offset(d, -18f), UiPaint.C(255, 255, 255, 0.10f));
        }

        /// <summary>A clipboard with a tick: the office's word for "handled".</summary>
        static void DrawClipboardIcon(MeshGenerationContext ctx, Rect r)
        {
            var c = r.center; var h = r.height * 0.9f; var w = h * 0.76f;
            var board = new Rect(c.x - w * 0.5f, c.y - h * 0.44f, w, h * 0.9f);
            UiPaint.Fill(ctx, UiPaint.RoundRect(board, 4f), UiPaint.C(196, 150, 104));
            UiPaint.Fill(ctx, UiPaint.RoundRect(new Rect(board.xMin + 3f, board.yMin + 5f, w - 6f, board.height - 8f), 2f), UiPaint.C(255, 255, 255));
            UiPaint.Fill(ctx, UiPaint.RoundRect(new Rect(c.x - w * 0.26f, board.yMin - 3f, w * 0.52f, 8f), 3f), UiPaint.C(90, 200, 255));
            // the tick: two thick strokes as one polygon
            var t = 3.2f; var a = new Vector2(c.x - w * 0.24f, c.y + h * 0.04f); var m = new Vector2(c.x - w * 0.06f, c.y + h * 0.2f); var b = new Vector2(c.x + w * 0.26f, c.y - h * 0.14f);
            UiPaint.Fill(ctx, new List<Vector2> { a + new Vector2(0f, -t), m + new Vector2(0f, -t * 1.4f), b + new Vector2(0f, -t), b + new Vector2(0f, t), m + new Vector2(0f, t * 1.4f), a + new Vector2(0f, t) }, UiPaint.C(40, 170, 90));
        }

        static void DrawClockIcon(MeshGenerationContext ctx, Rect r)
        {
            var c = r.center; var rad = r.height * 0.42f;
            UiPaint.Fill(ctx, UiPaint.Ellipse(c, rad, rad), UiPaint.C(90, 200, 255));
            UiPaint.Fill(ctx, UiPaint.Ellipse(c, rad * 0.72f, rad * 0.72f), UiPaint.C(18, 28, 50));
            UiPaint.Fill(ctx, UiPaint.RoundRect(new Rect(c.x - 1.5f, c.y - rad * 0.55f, 3f, rad * 0.58f), 1.5f), UiPaint.C(255, 255, 255));
            UiPaint.Fill(ctx, UiPaint.RoundRect(new Rect(c.x - 1.5f, c.y - 1.5f, rad * 0.45f, 3f), 1.5f), UiPaint.C(255, 255, 255));
        }

        /// <summary>The last 자동 진행 reason logged, so 파밍 does not repeat itself every clear.</summary>
        string _lastHeld;

        void ToggleAdvance()
        {
            var p = Game.Player;
            p.autoAdvance = !p.autoAdvance;
            Log(p.autoAdvance
                ? "자동 진행 켜짐 — 처리를 마치면 다음 구간으로 넘어갑니다"
                : $"자동 진행 꺼짐 — Phase {p.stage}에서 계속 처리합니다");
            Game.Touch();
            SyncAutoButton();
        }

        void ToggleSafe()
        {
            var p = Game.Player;
            p.safeAdvance = !p.safeAdvance;
            var min = Mathf.RoundToInt(GameData.Balance.safeAdvanceMin * 100);
            Log(p.safeAdvance
                ? $"안전 진행 켜짐 — 승산 {min}% 미만이면 넘어가지 않습니다"
                : "안전 진행 꺼짐 — 승산과 무관하게 넘어갑니다");
            Game.Touch();
            SyncAutoButton();
        }

        void ToggleAutoUpgrade()
        {
            var p = Game.Player;
            p.autoUpgrade = !p.autoUpgrade;
            Log(p.autoUpgrade
                ? "자동 강화 켜짐 — 가장 싼 편성 카드부터 골드를 씁니다"
                : "자동 강화 꺼짐");
            Game.Touch();
            SyncAutoButton();
        }

        void SyncAutoButton()
        {
            var p = Game.Player;
            if (p == null) return;
            SetOn(_autoButton, p.autoSkill);
            _advanceButton?.EnableInClassList("auto-toggle--on", p.autoAdvance);
            _upgradeButton?.EnableInClassList("auto-toggle--on", p.autoUpgrade);

            if (_speedLabel != null) _speedLabel.text = $"속도 x{_speedMultiplier}";
            SetOn(_speedButton, _speedMultiplier > 1);

            if (_safeButton == null) return;
            _safeButton.EnableInClassList("auto-toggle--on", p.safeAdvance);
            // 안전 진행 only has anything to gate while 자동 진행 is on. Left enabled it is a
            // switch that changes nothing, which reads as a bug rather than as a dependency.
            _safeButton.SetEnabled(p.autoAdvance);
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

            // The last run's result comes down with it. The auto-restart (Finish, 2.6 s) used to
            // leave it up, and its own auto-confirm (3.2 s, 자동 진행) then found it still there and
            // restarted AGAIN — the new fight was covered for 0.6 s and then thrown away. With the
            // popup gone, that timer's parent check stops it.
            _resultPopup?.RemoveFromHierarchy();
            _resultPopup = null;
            _root?.RemoveFromClassList("battle--result");
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

            if (_world != null) classes += " fighter--3d";
            var el = UiKit.Div(classes, _stage);
            var bodyEl = UiKit.Div("fighter__body", el);

            if (c.side == Side.Hero)
            {
                // A chibi if one exists, otherwise the card art in a circle. The fallback keeps the
                // fight readable while the sprite set is still being filled in.
                var sprite = GameData.WornSprite(c.heroId);
                if (sprite != null)
                {
                    bodyEl.AddToClassList("fighter__body--chibi");
                    UiKit.SetArt(bodyEl, sprite);
                }
                else UiKit.SetArt(bodyEl, GameData.WornCardArt(c.heroId));
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
            // The contact shadow. A sprite without one is a sticker on the picture; with one it
            // is standing on the floor, and on a plane that recedes that is most of the effect.
            UiKit.Div("fighter__shadow", el);

            var bar = UiKit.Div("fighter__hpbar", el);
            // a pale "damage taken" bar under the fill that drains after it (ui_score 07-BattleHud #1:
            // the reference's bars show the chunk a hit took before it empties), and a gloss on top
            UiKit.Div("fighter__hplag", bar);
            UiKit.Div("fighter__hpfill", bar);
            UiKit.Div("fighter__hpgloss", bar).pickingMode = PickingMode.Ignore;
            // an error's armour as a small coloured diamond at the head of its bar (업무 상성)
            if (c.side == Side.Monster && c.armorType >= 0)
            {
                var armor = UiKit.Div("fighter__armor", el);
                armor.pickingMode = PickingMode.Ignore;
                var col = Affinity.ColorOf(c.armorType);
                ModalFrame.Painted(armor, (ctx, r) =>
                {
                    var d = new List<Vector2> { new(r.center.x, r.yMin), new(r.xMax, r.center.y), new(r.center.x, r.yMax), new(r.xMin, r.center.y) };
                    UiPaint.Fill(ctx, d, Color.white);
                    var i = new List<Vector2> { new(r.center.x, r.yMin + 3f), new(r.xMax - 3f, r.center.y), new(r.center.x, r.yMax - 3f), new(r.xMin + 3f, r.center.y) };
                    UiPaint.Fill(ctx, i, col);
                });
            }

            // Heroes carry a second, blue bar under the first: the charge on their skill, which
            // fires on its own the moment it is full.
            if (c.side == Side.Hero && c.skillCooldown > 0f)
                UiKit.Div("fighter__skillfill", UiKit.Div("fighter__skillbar", el));
            UiKit.Text(c.name, "fighter__name", el);
            _views[c] = el;
            _depthDirty = true;
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
            // 야근's clock runs on the screen's tick rather than the sim's, because the run is
            // sixty seconds of real time and not a number of waves.
            if (OvertimeService.Active != null)
            {
                UpdateOvertimeLabel();
                if (OvertimeService.Tick(dt)) { EndOvertime(); return; }
            }

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

            if (_pendingBoss > 0f && !_sim.Finished && _sim.Wave >= 1)
            {
                _sim.DebugBossNow(_pendingBoss); _pendingBoss = 0f;
                Debug.Log($"[shots] boss on: wave {_sim.Wave}/{_sim.WaveCount}, boss alive {_sim.Monsters.Any(m => m.boss != null && m.Alive)}");
            }
            if (_hitStop > 0f) _hitStop -= dt;
            else if (!_sim.Finished) _sim.Tick(dt * _speedMultiplier);

            // 자동 강화 runs off the screen's tick, not the sim's, so gold keeps being spent while
            // the player is on another sheet — which is the whole point of a setting that spends
            // for them. It levels the party the run is already using, so the next wave feels it.
            if (AutoPlayService.Tick(Game.Player, dt) > 0)
            {
                _sim?.RefreshHeroStats();
                Game.Touch();
            }

            var visible = _root != null && _root.panel != null;
            if (!visible)
            {
                // Off-screen the events still have to be consumed or the queue grows without bound,
                // but nothing is drawn for them.
                _sim.Events.Clear();
                _lag.Clear();
                _world?.SetVisible(false);
                Finish(dt);
                return;
            }

            DrainEvents();
            _fx?.Tick(dt);
            LayoutFighters(dt);
            ApplyShake();
            UpdateCombo();
            UpdateBoss(dt);
            UpdateSkillGauges();
            UpdateExBar();
            UpdateFloaters(dt);
            UpdateBubbles(dt);
            UpdateBrace(dt);
            UpdateUpgrades();

            // The HUD's words are rebuilt only when what they say changes: formatted every frame they
            // were most of the fight's managed garbage (PerfProbe).
            var waveKey = (_sim.Finished ? 1 : 0) | (_sim.Won ? 2 : 0) | (_sim.TimedOut ? 4 : 0) | (_sim.Enraged ? 8 : 0)
                        | (_sim.Wave << 4) | (_sim.WaveCount << 10) | (_sim.Stage << 16);
            var enrage = Mathf.RoundToInt(_sim.EnrageMultiplier * 10f);
            if (waveKey != _hudWave || enrage != _hudEnrage)
            {
                _hudWave = waveKey; _hudEnrage = enrage;
                _waveLabel.text = _sim.Finished
                    ? (_sim.Won ? "업무 완료" : _sim.TimedOut ? "시간 초과" : "업무 실패")
                    : _sim.Enraged
                        ? $"P{_sim.Stage} · {_sim.Wave}/{_sim.WaveCount} · 야근 ×{_sim.EnrageMultiplier:F1}"
                        : $"P{_sim.Stage} · {_sim.Wave}/{_sim.WaveCount}";
                if (_armorChip != null)
                {
                    var arm = Affinity.ArmorOfStage(_sim.Stage);
                    _armorChip.userData = Affinity.ColorOf(arm);
                    _armorText.text = Affinity.ArmorShortName(arm);
                    _armorChip.MarkDirtyRepaint();
                }
            }
            if (_enemyLabel != null)
            {
                var alive = 0; foreach (var m in _sim.Monsters) if (m.Alive) alive++;
                if (alive != _hudAlive) { _hudAlive = alive; _enemyLabel.text = alive.ToString(); }
            }
            _kills?.EnableInClassList("hidden", _sim.Finished);   // the result screen has its own title there
            if (_killLabel != null && (_sim.Kills != _hudKills || _sim.EnemyTotal != _hudTotal))
            {
                _hudKills = _sim.Kills; _hudTotal = _sim.EnemyTotal;
                _killLabel.text = _sim.EnemyTotal > 0 ? $"격파: {_sim.Kills} / {_sim.EnemyTotal}" : $"격파: {_sim.Kills}";
            }
            if (_timeLabel != null)
            {
                var left = (int)Mathf.Max(0f, BattleSim.TimeLimit - _sim.Elapsed);
                if (left != _hudTime) { _hudTime = left; _timeLabel.text = $"{left / 60:00}:{left % 60:00}"; }
            }
            if (OvertimeService.Active != null) { UpdateOvertimeLabel(); return; }
            _waveLabel.EnableInClassList("battle__wave--enraged", !_sim.Finished && _sim.Enraged);

            // 승산 — the ETA rather than the odds. A stage's time grows continuously and that
            // growth is the wall; the win chance reads 유리 almost always and says nothing.
            // (the forecast sums the party each call: once a second is plenty, and only while the menu shows it)
            if (_forecastLabel != null && !_menu.ClassListContains("hidden") && Time.unscaledTime >= _hudForecastAt)
            {
                _hudForecastAt = Time.unscaledTime + 1f;
                var f = ForecastService.For(Game.Player, _sim.Stage);
                _forecastLabel.text = $"예상 {ForecastService.Eta(f.Eta)}";
            }

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

        // No lag: the sim already lands a blow when its Shot arrives (BattleSim.Land), and the swing
        // starts when the Shot is fired (BattleWorld.SyncShots), so the damage event IS the contact
        // frame. (A 0.1 s lag here drew the flinch after the projectile had gone, and restarted the
        // monsters' 0.5 s swings.) The queue stays for effects that want to trail an event.
        const float ImpactLag = 0f;
        readonly Queue<(float due, BattleSim sim, BattleEvent e)> _lag = new();

        void DrainEvents()
        {
            var now = Time.unscaledTime;
            while (_sim.Events.Count > 0)
            {
                var ev = _sim.Events.Dequeue();
                // a skill's hits are queued before its Skill event: mark the caster first
                if (ev.kind == EventKind.Skill) _world?.MarkSkill(ev.actor, ev.text);
                _lag.Enqueue((now + ImpactLag, _sim, ev));
            }
            while (_lag.Count > 0 && _lag.Peek().due <= now)
            {
                var (_, sim, e) = _lag.Dequeue();
                if (sim != _sim) continue;   // left over from the run before
                _world?.OnEvent(e);
                switch (e.kind)
                {
                    case EventKind.Spawn:
                        AddFighterView(e.actor);
                        break;
                    case EventKind.Fx:
                        _fx?.Add(e.fx, e.x, e.y, e.color, e.radius);
                        break;
                    case EventKind.Damage:
                        // the reference target (tools/out/design/target_3): a crit is gold and says so
                        {
                            // 업무 상성 says itself over the number: 효과적 (amber, bigger) / 저항 (grey, smaller)
                            var num = e.amount.ToString("N0");
                            var tag = e.affinity > 0 ? "<size=46%>효과적</size>\n" : e.affinity < 0 ? "<size=46%>저항</size>\n" : "";
                            var cls = e.crit ? "floater floater--crit"
                                : e.target != null && e.target.side == Side.Hero ? "floater floater--taken"
                                : e.affinity > 0 ? "floater floater--weak" : e.affinity < 0 ? "floater floater--resist" : "floater";
                            Float(e.target, e.crit ? tag + "CRITICAL\n" + num : tag + num, cls);
                        }
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
                    case EventKind.Warn:
                        // The boss has announced its next move, which is the moment the web opens
                        // the formula. It is the only window the player has.
                        Float(e.actor, $"⚠ {e.text}", "floater floater--skill");
                        OpenBrace(e.text);
                        break;
                    case EventKind.Braced:
                        Float(e.actor, "검산 완료", "floater floater--heal");
                        AudioService.Play("block", 0.6f);
                        break;
                    case EventKind.Death:
                        // A kill is the beat worth stopping for; a monster popping mid-stride is
                        // the moment the whole exchange was building to.
                        if (e.target != null && e.target.side == Side.Monster)
                        {
                            _hitStop = Mathf.Max(_hitStop, e.target.boss != null ? 0.18f : 0.09f);
                            OvertimeService.Note(e.target.elite);
                        }
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
            _world?.EndSkillBatch();
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
            // Four lines, because it sits over the fight now rather than in a box of its own —
            // enough to catch what just happened, not enough to cover the party.
            if (_logLines.Count > 4) _logLines.RemoveAt(0);

            _log.Clear();
            for (var i = 0; i < _logLines.Count; i++)
            {
                var row = UiKit.Div("log-row", _log);
                UiKit.Text((i + 1).ToString(), "log-row__n", row);
                UiKit.Text(_logLines[i], "log-row__text", row);
            }
            _log.scrollOffset = new Vector2(0, float.MaxValue);
        }

        // Kept in step with .fighter / .fighter__body in App.uss: a 16x28 sprite at 4x, on rows tall
        // enough to clear it.
        const float FighterWidth = 190f;

        // The camera does not show the whole 832-unit field any more.
        //
        // The party stands around x=200-400 and monsters are fought at roughly 450-650, so a third
        // of the field was permanently empty road and every fighter was drawn small to fit space
        // nothing happened in. Showing this window instead is about a 1.3x zoom, which is enough
        // for the sprites to read at arm's length without cropping anyone out of frame.
        // One scale for both axes, taken from the width, so the whole street spans the sheet and
        // nothing is stretched. The ground line is pinned near the bottom of the box, so the party
        // stands on the road however tall the sheet happens to be.
        // Up from 0.84: the gold strip is anchored along the bottom now, and at the old line
        // the party stood behind it.
        const float GroundAnchor = 0.56f;

        // The window on the field, in the simulation's own units.
        //
        // The party stands around x=200-400 and monsters are fought at roughly 450-650, so showing
        // all 832 units spent a quarter of the screen on empty road at each end and drew everyone
        // small to make room for it. Framing 100-740 is a 1.3x zoom onto the part where the fight
        // actually is, and still leaves a monster room to walk in from the right.
        const float CamX0 = 100f, CamX1 = 740f;

        float _scale = 1f, _groundY, _camX;

        // The party stands on one line, as in the web build, with a six-pixel stagger so a
        // five-stack still reads as five people. Rows were readable but they turned a side-view
        // brawl into a spreadsheet of duels.
        const float GroundFraction = CityBackdrop.GroundY / (float)CityBackdrop.CanvasH;

        // The party used to stand on a single line with a nine-pixel stagger, which is a side-view
        // brawler: distance was x and nothing else, and every fighter was the same size wherever
        // they stood. The reference reads as three dimensions because its floor recedes — the
        // figures at the back are higher up the frame, smaller, and closer to the centre.
        //
        // So: three depth rows across a plane 64 canvas units deep. Discrete rows rather than a
        // continuous z, because the sprites are pixel art and a continuous scale would resample
        // them at a different ratio every frame.
        static readonly float[] DepthRows = { 0f, 0.5f, 1f };

        /// <summary>How deep the standing plane is, in the backdrop's own units.</summary>
        const float DepthSpread = 42f;

        /// <summary>Sprite scale at the back of the plane; the front row is drawn at 1.</summary>
        const float DepthScaleFar = 0.74f;

        /// <summary>How much the back row pulls in towards the centre of the frame.</summary>
        const float DepthConverge = 0.13f;

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
                el.style.left = _camX + x * _scale;
                // A thrown thing arcs; a slash does not travel at all.
                var arc = shot.Kind == "slash" ? 0f : Mathf.Sin(k * Mathf.PI) * 30f;
                el.style.top = _groundY - 46f - arc;
            }

            _shotKeys.Clear(); _shotKeys.AddRange(_shotViews.Keys);
            foreach (var key in _shotKeys)
            {
                if (_sim.Shots.Contains(key)) continue;
                _shotViews[key].RemoveFromHierarchy();
                _shotViews.Remove(key);
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

            _scale = width / (CamX1 - CamX0);
            _camX = -CamX0 * _scale;
            _groundY = height * GroundAnchor;

            if (_world != null)
            {
                Layout3D(width, height, dt);
                return;
            }

            if (_backdropView != null)
            {
                // The street is drawn at field scale and slid under the camera, so the road, the
                // buildings and the sprites all move together when the window changes.
                _backdropView.style.width = CityBackdrop.CanvasW * _scale;
                _backdropView.style.height = CityBackdrop.CanvasH * _scale;
                _backdropView.style.left = _camX;
                // Line the drawn road up with where the sprites actually stand.
                _backdropView.style.top = _groundY - CityBackdrop.GroundY * _scale;
            }

            if (_fx != null)
            {
                // The effects ride the same mapping as the fighters, or a slash lands somewhere
                // the sprite is not.
                _fx.ScaleX = _scale;
                _fx.ScaleY = _scale;
                _fx.OffsetX = _camX;
                _fx.OffsetY = _groundY - CityBackdrop.GroundY * _scale;
            }

            LayoutShots(width, height);
            LayoutMotes(width, height, dt);

            _viewKeys.Clear(); _viewKeys.AddRange(_views.Keys);
            foreach (var c in _viewKeys)
            {
                var el = _views[c];
                if (c.side == Side.Monster && !_sim.Monsters.Contains(c) && !c.Alive)
                {
                    el.RemoveFromHierarchy();
                    _views.Remove(c);
                    _depthDirty = true;
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

                // Which row of the plane this one stands on. It is the lane index, so a
                // fighter never changes depth mid-fight — a sprite that walked towards the camera
                // while it fought would be a different animation problem entirely.
                var lane = Mathf.Max(0, c.side == Side.Hero ? _sim.Heroes.IndexOf(c) : _sim.Monsters.IndexOf(c));
                var z = DepthRows[lane % DepthRows.Length];

                // Near the front: full size, low in the frame, out at the edges. At the back:
                // smaller, higher, pulled towards the middle.
                var depth = Mathf.Lerp(DepthScaleFar, 1f, z);
                el.style.scale = new Scale(new Vector2(depth, depth));

                var centre = width * 0.5f;
                var px = _camX + drawX * _scale;
                px = centre + (px - centre) * Mathf.Lerp(1f - DepthConverge, 1f, z);

                var w = el.resolvedStyle.width;
                if (float.IsNaN(w) || w <= 1f) w = FighterWidth;
                el.style.left = Mathf.Clamp(px - w * 0.5f, -w * 0.5f, Mathf.Max(0f, width - w * 0.5f));

                // The sprite is anchored by its feet, so subtract its height to sit ON the ground.
                var feet = _groundY + (z - 0.5f) * DepthSpread * _scale;
                el.style.top = feet - (c.boss != null ? 138f : 92f);

                SetHp(el, c.maxHp <= 0 ? 0f : (float)c.hp / c.maxHp);
                el.EnableInClassList("fighter--dead", !c.Alive);
            }

            SortByDepth();
        }

        readonly Dictionary<VisualElement, (VisualElement lag, float shown, float hold)> _hpLag = new();

        void SetHp(VisualElement el, float frac)
        {
            if (!_hpFill.TryGetValue(el, out var fill)) _hpFill[el] = fill = el.Q(className: "fighter__hpfill");
            if (fill != null) fill.style.width = Length.Percent(frac * 100f);
            if (!_hpLag.TryGetValue(el, out var st)) st = (el.Q(className: "fighter__hplag"), frac, 0f);
            if (st.lag == null) return;
            var dt = Time.unscaledDeltaTime;
            if (frac < st.shown - 0.001f)
            {
                // hold the chunk a beat, then drain it
                if (st.hold <= 0f) st.hold = 0.35f;
                st.hold -= dt;
                if (st.hold <= 0f) st.shown = Mathf.Max(frac, st.shown - dt * 0.9f);
            }
            else { st.shown = frac; st.hold = 0f; }
            st.lag.style.width = Length.Percent(st.shown * 100f);
            _hpLag[el] = st;
        }

        /// <summary>The drawn lane x of a fighter: its sim x, with a melee lunge played out.</summary>
        float DrawX(Combatant c)
        {
            var drawX = c.x;
            if (c.dashT > 0f && c.dashTo != 0f)
            {
                var k = 1f - c.dashT / 0.45f;
                var reach = k < 0.3f ? Mathf.Sin(k / 0.3f * Mathf.PI * 0.5f)
                          : k < 0.55f ? 1f
                          : Mathf.Max(0f, 1f - (k - 0.55f) / 0.35f);
                drawX = Mathf.Lerp(c.homeX, c.dashTo, reach);
            }
            return drawX;
        }

        /// <summary>
        /// The 3D field. The render target follows the stage's size in real pixels; the fighter
        /// views are only HP bars now, pinned over each figure's head.
        /// </summary>
        void Layout3D(float width, float height, float dt)
        {
            var panelW = _stage.panel?.visualTree?.worldBound.width ?? 0f;
            var px = panelW > 1f ? Screen.width / panelW : 1f;
            // a little supersampling: the texture is filtered down onto the panel
            const float ss = 1.25f;
            _world.Resize(Mathf.RoundToInt(width * px * ss), Mathf.RoundToInt(height * px * ss));
            _world.SetVisible(true);
            if (_worldTex != _world.Texture)
            {
                _worldTex = _world.Texture;
                _stage.style.backgroundImage = Background.FromRenderTexture(_worldTex);
            }
            if (_backdropView != null) _backdropView.style.display = DisplayStyle.None;

            _world.Sync(dt, DrawX, _sim.Shake);

            if (_fx != null)
            {
                // the 2D sparks ride a linear fit of the projection along the lane
                var a = _world.ProjectSim(0f, BattleSim.GroundY);
                var b = _world.ProjectSim(BattleSim.FieldW, BattleSim.GroundY);
                var c = _world.ProjectSim(0f, BattleSim.GroundY - 64f);
                _fx.ScaleX = (b.x - a.x) * width / BattleSim.FieldW;
                _fx.ScaleY = (a.y - c.y) * height / 64f;
                _fx.OffsetX = a.x * width;
                _fx.OffsetY = a.y * height - BattleSim.GroundY * _fx.ScaleY;
            }

            _viewKeys.Clear(); _viewKeys.AddRange(_views.Keys);
            foreach (var c in _viewKeys)
            {
                var el = _views[c];
                if (c.side == Side.Monster && !_sim.Monsters.Contains(c) && !c.Alive)
                {
                    el.RemoveFromHierarchy();
                    _views.Remove(c);
                    continue;
                }
                if (!_world.Head(c, out var head)) continue;
                var w = el.resolvedStyle.width;
                if (float.IsNaN(w) || w <= 1f) w = 120f;
                el.style.scale = new Scale(Vector2.one);
                el.style.left = head.x * width - w * 0.5f;
                el.style.top = head.y * height - 26f;

                SetHp(el, c.maxHp <= 0 ? 0f : (float)c.hp / c.maxHp);
                el.EnableInClassList("fighter--dead", !c.Alive);
            }
        }

        /// <summary>
        /// Front row last, so a figure at the front of the plane overlaps one behind it. Without
        /// this the whole illusion collapses the moment two fighters share a column: a small,
        /// high, distant sprite drawn over a large near one reads as a floating doll.
        ///
        /// Only when the cast changes — a monster died, a wave spawned. Reordering the tree every
        /// frame for ten elements that have not moved is work for nothing.
        /// </summary>
        void SortByDepth()
        {
            if (!_depthDirty) return;
            _depthDirty = false;

            foreach (var pair in _views.OrderBy(p =>
                     DepthRows[Mathf.Max(0, (p.Key.side == Side.Hero
                         ? _sim.Heroes.IndexOf(p.Key) : _sim.Monsters.IndexOf(p.Key))) % DepthRows.Length]))
                pair.Value.BringToFront();
        }

        bool _depthDirty = true;

        /// <summary>
        /// The blue gauge under each hero's health, filling towards their next skill.
        ///
        /// It replaces the row of EX cards and the shared budget bar underneath the field. Those
        /// were a Blue Archive hand of cards grafted onto a fight nobody plays: every skill here
        /// fires itself, so a card you cannot press is a card that only tells you the game is
        /// thinking. On the fighter, the same information is where the player is already looking.
        /// </summary>
        void UpdateSkillGauges()
        {
            foreach (var (c, el) in _views)
            {
                if (c.side != Side.Hero) continue;
                var fill = el.Q(className: "fighter__skillfill");
                if (fill == null) continue;
                fill.style.width = Length.Percent(c.SkillCharge * 100f);
                fill.EnableInClassList("fighter__skillfill--ready", c.SkillReady);
            }
        }

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
            var on = _sim.Combo >= 5 && !_sim.Finished;
            _comboLabel.text = on ? "COMBO\nx" + _sim.Combo : "";
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

            // The band: a slant across the field in the character's own colour, fading to navy,
            // with speed lines running along it — the reference's EX band, not a grey strip.
            var accent = BackSheet.For(def, Game.Player?.Find(hero.heroId)).Accent;
            var sweep = UiKit.Div("cutin__sweep", view);
            ModalFrame.Painted(sweep, (ctx, r) =>
            {
                var slant = r.height * 0.35f;
                var band = new System.Collections.Generic.List<Vector2>
                    { new(r.xMin + slant, r.yMin), new(r.xMax, r.yMin), new(r.xMax - slant, r.yMax), new(r.xMin, r.yMax) };
                UiPaint.Fill(ctx, band, UiPaint.Horizontal(UiPaint.WithAlpha(Color.Lerp(accent, Color.white, 0.15f), 0.95f),
                                                          UiPaint.C(20, 32, 64, 0.92f), r.xMin, r.xMax), 1f);
                for (var i = 0; i < 14; i++)
                {
                    var y = r.yMin + r.height * ((i * 37) % 100) / 100f;
                    var x = r.xMin + r.width * ((i * 53) % 100) / 100f;
                    var len = r.width * (0.12f + (i % 4) * 0.05f);
                    var line = new System.Collections.Generic.List<Vector2>
                        { new(x, y), new(x + len, y), new(x + len - 6f, y + 3f), new(x - 6f, y + 3f) };
                    UiPaint.Fill(ctx, UiPaint.Clip(line, band), UiPaint.C(255, 255, 255, 0.35f), 0f);
                }
                // thin white edges top and bottom
                UiPaint.Fill(ctx, UiPaint.Clip(new System.Collections.Generic.List<Vector2>
                    { new(r.xMin, r.yMin), new(r.xMax, r.yMin), new(r.xMax, r.yMin + 5f), new(r.xMin, r.yMin + 5f) }, band), UiPaint.C(255, 255, 255, 0.9f), 0f);
                UiPaint.Fill(ctx, UiPaint.Clip(new System.Collections.Generic.List<Vector2>
                    { new(r.xMin, r.yMax - 5f), new(r.xMax, r.yMax - 5f), new(r.xMax, r.yMax), new(r.xMin, r.yMax) }, band), UiPaint.C(255, 255, 255, 0.9f), 0f);
            });
            // manga focus lines over the whole field, from the edges toward the band (the Gemini action
            // mock-up, tools/out/design/mock_action_0)
            var focus = UiKit.Div("cutin__focus", view);
            focus.pickingMode = PickingMode.Ignore;
            ModalFrame.Painted(focus, (ctx, r) =>
            {
                var c = r.center; var R = Mathf.Max(r.width, r.height);
                for (var i = 0; i < 46; i++)
                {
                    var a = i / 46f * Mathf.PI * 2f + ((i * 37) % 11) * 0.013f;
                    var inner = R * (0.36f + ((i * 53) % 17) / 100f);
                    var wdt = 0.012f + ((i * 29) % 7) * 0.003f;
                    var p0 = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * inner;
                    var p1 = c + new Vector2(Mathf.Cos(a - wdt), Mathf.Sin(a - wdt)) * R;
                    var p2 = c + new Vector2(Mathf.Cos(a + wdt), Mathf.Sin(a + wdt)) * R;
                    UiPaint.Fill(ctx, new System.Collections.Generic.List<Vector2> { p0, p1, p2 }, UiPaint.C(20, 24, 40, 0.34f), 0f);
                }
            });
            // floating spreadsheet windows: the Excel motif as the skill's energy
            var sheets = UiKit.Div("cutin__sheets", view);
            sheets.pickingMode = PickingMode.Ignore;
            foreach (var (cls, formula, chart) in new[] { ("cutin__win cutin__win--a", "=SUM(A1:B10)", false), ("cutin__win cutin__win--b", "=VLOOKUP(\"EX\",A:F,3)", true) })
            {
                var win = UiKit.Div(cls, sheets);
                win.pickingMode = PickingMode.Ignore;
                var green = UiPaint.C(33, 163, 102);
                ModalFrame.Painted(win, (ctx, r) =>
                {
                    var box = UiPaint.RoundRect(r, 8f);
                    UiPaint.Ring(ctx, box, UiPaint.C(120, 255, 190, 0.55f), UiPaint.C(120, 255, 190, 0f), 14f);
                    UiPaint.Fill(ctx, box, UiPaint.C(236, 252, 244, 0.9f));
                    var title = UiPaint.Clip(box, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, r.yMin + r.height * 0.13f), 0f));
                    UiPaint.Fill(ctx, title, green, 0f);
                    var fy0 = r.yMin + r.height * 0.13f; var fy1 = fy0 + r.height * 0.12f;
                    UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin + 6f, fy0 + 4f, r.xMax - 6f, fy1 - 2f), 3f), Color.white);
                    // the grid: column heads, row heads, cells
                    var gx0 = r.xMin + r.width * 0.07f; var gy0 = fy1 + 4f; var cols = 6; var rows = 6;
                    var cw = (r.xMax - 6f - gx0) / cols; var rh = (r.yMax - 6f - gy0) / (rows + 1);
                    UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin + 6f, gy0, r.xMax - 6f, gy0 + rh), 0f), UiPaint.C(214, 236, 224));
                    UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin + 6f, gy0, gx0, r.yMax - 6f), 0f), UiPaint.C(214, 236, 224));
                    for (var i = 0; i <= cols; i++) UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(gx0 + i * cw - 0.6f, gy0, gx0 + i * cw + 0.6f, r.yMax - 6f), 0f), UiPaint.C(150, 190, 170, 0.8f));
                    for (var j = 0; j <= rows + 1; j++) UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin + 6f, gy0 + j * rh - 0.6f, r.xMax - 6f, gy0 + j * rh + 0.6f), 0f), UiPaint.C(150, 190, 170, 0.8f));
                    if (chart)
                    {
                        // a small bar chart over the cells
                        var bx = gx0 + cw * 0.5f; var by = r.yMax - 10f;
                        float[] hs = { 0.35f, 0.6f, 0.45f, 0.8f, 0.62f };
                        for (var i = 0; i < hs.Length; i++)
                        {
                            var x = bx + i * cw * 1.05f; var top = by - (r.yMax - gy0 - rh * 1.5f) * hs[i];
                            UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(x, top, x + cw * 0.6f, by), 2f), i % 2 == 0 ? UiPaint.C(70, 150, 230) : UiPaint.C(240, 140, 70));
                        }
                    }
                    UiPaint.Stroke(ctx, box, UiPaint.C(33, 163, 102, 0.9f), 2f);
                });
                var fx = UiKit.Text("fx  " + formula, "cutin__formula", win);
                fx.pickingMode = PickingMode.Ignore;
            }
            var art = UiKit.Div("cutin__art", view);
            UiKit.SetPortrait(art, def.id, UiKit.Crop.Cut, false, new Color(0f, 0f, 0f, 0f));
            var flash = UiKit.Div("cutin__flash", view);
            flash.pickingMode = PickingMode.Ignore;
            view.schedule.Execute(() => flash.AddToClassList("cutin__flash--out")).ExecuteLater(40);

            var plate = UiKit.Div("cutin__plate", view);
            var namePlate = UiKit.Div("cutin__nameplate", plate);
            ModalFrame.Painted(namePlate, (ctx, r) =>
                UiPaint.Fill(ctx, UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height), 6f), UiPaint.C(20, 32, 64, 0.95f)));
            UiKit.Text("EX", "cutin__ex", namePlate);
            UiKit.Text(skillName ?? def.skillName, "cutin__skill", namePlate);
            // the English sub-title under the name, as BA sets its EX names
            UiKit.Text("EX-SKILL · " + def.id.Replace('_', ' ').ToUpperInvariant(), "cutin__sub", plate);
            if (!string.IsNullOrEmpty(def.ult)) UiKit.Text($"“{def.ult}”", "cutin__line", plate);

            view.schedule.Execute(() =>
            {
                sweep.AddToClassList("cutin__sweep--in");
                art.AddToClassList("cutin__art--in");
                plate.AddToClassList("cutin__plate--in");
                sheets.AddToClassList("cutin__sheets--in");
                focus.AddToClassList("cutin__focus--in");
            }).ExecuteLater(16);

            view.schedule.Execute(() =>
            {
                art.RemoveFromClassList("cutin__art--in");
                plate.RemoveFromClassList("cutin__plate--in");
                sweep.RemoveFromClassList("cutin__sweep--in");
                sheets.RemoveFromClassList("cutin__sheets--in");
                focus.RemoveFromClassList("cutin__focus--in");
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
            if (float.IsNaN(top) || top <= 0f) top = _groundY - 90f;
            // numbers on one target within half a second step up and sideways instead of printing
            // over each other (a burst of hits read as one smudge)
            var now = Time.unscaledTime;
            var n = _floatStack.TryGetValue(at, out var st) && now - st.t < 0.5f ? st.n + 1 : 0;
            _floatStack[at] = (n, now);
            var k = n % 4;
            top -= k * 58f;
            var left = anchor.style.left;
            if (k > 0 && left.keyword == StyleKeyword.Undefined && left.value.unit == LengthUnit.Pixel)
                el.style.left = left.value.value + (k % 2 == 1 ? 34f : -34f);
            else el.style.left = left;
            el.style.top = top;
            _floaters.Add((el, 0.9f, top));
        }
        readonly Dictionary<Combatant, (int n, float t)> _floatStack = new();

        /// <summary>
        /// 캐릭터 대사 — one party member says something when a Phase falls, ported from the web
        /// build's `sayLine`.
        ///
        /// It is the only place the cast speaks during a fight. Every one of them has a line
        /// written for them in PROFILES, and this build was already exporting it and showing it
        /// in two places nobody looks at often — the 호감도 panel and the EX cut-in. The web
        /// says it where it lands: over the hero's head, the moment the boss goes down.
        ///
        /// A speaker who is dead says nothing, which is why the living are filtered first rather
        /// than picked from and checked.
        /// </summary>
        void SayLine()
        {
            if (_sim == null) return;

            var alive = _sim.Heroes.Where(h => h.hp > 0 && _views.ContainsKey(h)).ToList();
            if (alive.Count == 0) return;

            var speaker = alive[Random.Range(0, alive.Count)];
            var owned = Game.Player.Find(speaker.heroId);
            if (owned == null) return;

            // The 호감도 line replaces the stock one about a third of the time once it is
            // unlocked — always, and it stops being a reward for knowing them; never, and the
            // thing 호감도 buys is a panel nobody opens.
            var text = AffectionService.LineUnlocked(owned) && Random.value < 0.3f
                     ? AffectionService.Greeting(owned)
                     : GameData.Hero(speaker.heroId)?.line;
            if (string.IsNullOrEmpty(text)) return;

            Bubble(speaker, text);
        }

        readonly List<(VisualElement el, float life)> _bubbles = new();

        void Bubble(Combatant at, string text)
        {
            if (at == null || !_views.TryGetValue(at, out var anchor)) return;

            // one line, as wide as it needs (lines are short; a wrapped two-line bubble overflowed its box)
            var el = UiKit.Text(text, "saybubble", _stage);
            // an absolute label sizes to nothing here, so give it the width its longest line needs
            var longest = 0; foreach (var line in (text ?? "").Split('\n')) longest = Mathf.Max(longest, line.Length);
            el.style.width = Mathf.Clamp(longest * 25f + 60f, 180f, 900f);
            // the tail: a small rotated square under the bubble, pointing at the head
            UiKit.Div("saybubble__tail", el).pickingMode = PickingMode.Ignore;
            var top = anchor.resolvedStyle.top;
            if (float.IsNaN(top) || top <= 0f) top = _groundY - 90f;
            el.style.left = anchor.style.left;
            // anchored by its FOOT: a two-line line grows upward instead of running down over
            // the head (the r7 result capture had the second line printed through the first)
            el.style.top = top - 18f;
            el.style.translate = new Translate(Length.Percent(-20), Length.Percent(-100));
            _bubbles.Add((el, 3.2f));
        }

        void UpdateBubbles(float dt)
        {
            for (var i = _bubbles.Count - 1; i >= 0; i--)
            {
                var (el, life) = _bubbles[i];
                life -= dt;
                if (life <= 0f) { el.RemoveFromHierarchy(); _bubbles.RemoveAt(i); continue; }
                // Held at full opacity and faded only at the end: a line that starts dissolving
                // immediately is one nobody finishes reading.
                el.style.opacity = Mathf.Clamp01(life / 0.6f);
                _bubbles[i] = (el, life);
            }
        }

        // ---------------------------------------------------------------- 괄호 수식

        readonly BraceService _brace = new();
        VisualElement _bracePane;
        TextField _braceField;
        Label _braceClock;

        /// <summary>
        /// Poses the sum, over the field, for as long as the balance allows.
        ///
        /// Nothing opens while the player is on another sheet: the fight runs off-screen and a
        /// question nobody can see is one they are guaranteed to fail, which would turn a bonus
        /// into a penalty for leaving the battle screen.
        /// </summary>
        void OpenBrace(string move)
        {
            if (_root == null || _root.panel == null) return;
            if (_sim == null || _sim.Braced) return;

            _brace.Open_(move, _sim.Braced);
            if (!_brace.Open) return;

            CloseBrace();
            _bracePane = UiKit.Div("brace", _stage);
            UiKit.Text($"검산 · {move}", "brace__title", _bracePane);

            var row = UiKit.Div("brace__row", _bracePane);
            UiKit.Text($"= {_brace.A} + {_brace.B}", "brace__sum", row);

            _braceField = new TextField { maxLength = 4 };
            _braceField.AddToClassList("brace__field");
            _braceField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter) SubmitBrace();
            });
            row.Add(_braceField);
            UiKit.Btn("제출", "btn btn--primary brace__submit", SubmitBrace, row);

            _braceClock = UiKit.Text("", "brace__clock", _bracePane);
            _braceField.Focus();
        }

        void SubmitBrace()
        {
            if (!_brace.Open) { CloseBrace(); return; }
            var ok = _brace.Submit(_braceField?.value);
            if (ok)
            {
                _sim?.MarkBraced();
                AudioService.Play("upgrade", 0.6f);
                Log($"검산 완료 — 다음 특수 공격 피해 {Mathf.RoundToInt(GameData.Balance.braceReduce * 100)}% 감소");
            }
            else AudioService.Play("tap", 0.4f);
            CloseBrace();
        }

        void CloseBrace()
        {
            _bracePane?.RemoveFromHierarchy();
            _bracePane = null;
            _braceField = null;
            _braceClock = null;
        }

        void UpdateBrace(float dt)
        {
            if (!_brace.Open) { if (_bracePane != null) CloseBrace(); return; }
            if (_brace.Tick(dt)) { CloseBrace(); return; }
            if (_braceClock != null) _braceClock.text = $"{_brace.Left:0.0}초";
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
                // the punch: lands big and snaps down to size in the first 0.12 s, with a small overshoot
                var sc = k < 0.13f ? Mathf.Lerp(1.9f, 0.92f, k / 0.13f) : k < 0.22f ? Mathf.Lerp(0.92f, 1f, (k - 0.13f) / 0.09f) : 1f;
                el.style.scale = new Scale(new Vector3(sc, sc, 1f));
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

                // 비품 — the one growth axis that comes out of playing rather than out of a
                // currency. The first clear of a stage is the boss's first clear too, which is why
                // the deepest stage reached is checked before it is advanced.
                var firstClear = _sim.Stage > Game.Player.maxCleared;
                var drop = EquipService.Drop(Game.Player, _sim.Stage, boss: true, firstBoss: firstClear);
                if (drop != null) Log($"비품 획득 — {EquipService.Label(drop)}");

                // The Phase only moves when 자동 진행 says so. maxCleared is recorded either
                // way — what was beaten was beaten, and every gate in the game that reads "how
                // far have you got" reads this one, so parking on a Phase must never look like
                // losing ground.
                if (_sim.Stage > Game.Player.maxCleared) Game.Player.maxCleared = _sim.Stage;

                var held = AutoPlayService.HeldBack(Game.Player, _sim.Stage);
                if (held.Length == 0)
                {
                    if (_sim.Stage >= Game.Player.stage) Game.Player.stage++;
                }
                else if (held != _lastHeld)
                {
                    // Said once per reason rather than after every clear: on 파밍 this fires every
                    // fifteen seconds forever, and a log that repeats itself is a log nobody reads.
                    Log(held);
                    _lastHeld = held;
                }
                if (held.Length == 0) _lastHeld = null;
                AffectionService.AwardBattle(Game.Player, _sim.Kills, clearedBoss: true);

                // The cast speaks when a Phase falls — the web's `if (boss || first) sayLine()`.
                // Every fight here ends on a boss, so this fires on every clear.
                if (_root != null && _root.panel != null) SayLine();
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
        void BuildExBar()
        {
            if (_exBar == null) return;
            _exBar.Clear();
            _exButtons.Clear();

            if (_sim == null) return;

            foreach (var h in _sim.Heroes)
            {
                var combatant = h; // capture for closure
                var btn = new Button(() =>
                {
                    if (_sim != null && _sim.FireSkill(combatant))
                    {
                        Game.Touch();
                    }
                });
                UiKit.AddClasses(btn, "ex-button");
                _exBar.Add(btn);

                var art = UiKit.Div("ex-button__art", btn);
                UiKit.SetPortrait(art, combatant.heroId, UiKit.Crop.Face);
                UiKit.Div("ex-button__dim", btn).pickingMode = PickingMode.Ignore;
                // the ready glow (the Gemini HUD mock-up, tools/out/design/mock_hud_0): a pulsing cyan
                // aura and a light sweep across the card while it can be cast
                var glow = UiKit.Div("ex-button__glow", btn);
                glow.pickingMode = PickingMode.Ignore;
                var gt0 = Time.realtimeSinceStartup;
                ModalFrame.Painted(glow, (ctx, r) =>
                {
                    if (!btn.ClassListContains("ex-button--ready")) return;
                    var t = Time.realtimeSinceStartup - gt0;
                    var k = 0.6f + 0.4f * Mathf.Sin(t * 4f);
                    var box = UiPaint.RoundRect(r, 10f);
                    UiPaint.Ring(ctx, box, UiPaint.C(120, 236, 255, 0.85f * k), UiPaint.C(120, 236, 255, 0f), 16f);
                    UiPaint.Stroke(ctx, box, UiPaint.C(170, 246, 255, 0.95f), 3f);
                    // the sweep: a slanted band of light crossing every 1.6 s
                    var ph = Mathf.Repeat(t / 1.6f, 1f) * 1.8f - 0.4f;
                    var x = r.xMin + r.width * ph; var w2 = r.width * 0.16f; var sl = r.height * 0.35f;
                    var band = new List<Vector2> { new(x + sl, r.yMin), new(x + sl + w2, r.yMin), new(x + w2, r.yMax), new(x, r.yMax) };
                    UiPaint.Fill(ctx, UiPaint.Clip(band, box), UiPaint.C(255, 255, 255, 0.28f), 1f);
                });
                glow.schedule.Execute(() => glow.MarkDirtyRepaint()).Every(40);

                var charge = UiKit.Div("ex-button__charge", btn);
                var cost = BattleSim.CostOf(combatant);
                var def = GameData.Hero(combatant.heroId);
                UiKit.CardFrame(Color.Lerp(GameData.Grade(def?.grade)?.Color ?? Color.white, Color.white, 0.4f), btn, 12f);   // a softer rim (ui_gate 07-BattleHud)
                var badge = UiKit.Div("ex-button__cost", btn);
                // a hexagon with a gold rim, COST over the number (target_3)
                ModalFrame.Painted(badge, (ctx, r) =>
                {
                    var hex = new List<Vector2>();
                    for (var h = 0; h < 6; h++) { var an = Mathf.PI / 6f + h * Mathf.PI / 3f; hex.Add(r.center + new Vector2(Mathf.Cos(an) * r.width * 0.5f, Mathf.Sin(an) * r.height * 0.5f)); }
                    UiPaint.Shadow(ctx, hex, new Vector2(0f, 2f), UiPaint.C(0, 0, 0, 0.35f), 4f);
                    UiPaint.Fill(ctx, hex, UiPaint.Vertical(UiPaint.C(255, 230, 130), UiPaint.C(214, 160, 40), r.yMin, r.yMax));
                    UiPaint.Fill(ctx, UiPaint.Offset(hex, -3.5f), UiPaint.Vertical(UiPaint.C(46, 70, 118), UiPaint.C(22, 36, 70), r.yMin, r.yMax));
                });
                UiKit.Text("COST", "ex-button__costword", badge).pickingMode = PickingMode.Ignore;
                var label = UiKit.Text(cost.ToString(), "ex-button__label", badge);
                // 업무 상성 on the card: the attack colour as a strip along the foot, ▲ / ▼ against
                // this Phase's errors in a tag at the top right — which EX to spend first
                var atk = combatant.atkType >= 0 ? combatant.atkType : Affinity.AtkOf(combatant.heroId);
                var verdict = Affinity.Verdict(atk, Affinity.ArmorOfStage(_sim.Stage));
                var strip = UiKit.Div("ex-button__atk", btn); strip.pickingMode = PickingMode.Ignore;
                strip.style.backgroundColor = Affinity.ColorOf(atk);
                if (verdict != 0)
                {
                    var vt = UiKit.Text(verdict > 0 ? "▲" : "▼", "ex-button__verdict" + (verdict > 0 ? " ex-button__verdict--up" : ""), btn);
                    vt.pickingMode = PickingMode.Ignore;
                }
                Juice.Press(btn);

                _exButtons[combatant] = (btn, charge, label);
            }
        }

        /// <summary>
        /// The reference's cost gauge: ten slanted cells in a navy frame, lit cyan up to the
        /// current cost with the next cell filling, and the whole number in a disc on the left.
        /// </summary>
        void DrawCost(MeshGenerationContext ctx, Rect r)
        {
            var cost = _sim != null ? Mathf.Clamp(_sim.Cost, 0f, BattleSim.MaxCost) : 0f;
            var bar = Rect.MinMaxRect(r.xMin + r.height * 1.1f, r.yMin + r.height * 0.25f, r.xMax, r.yMax - r.height * 0.18f);
            var slant = SkewPlate.SlantFor(bar.height);
            var frame = UiPaint.SkewRect(bar, slant, 4f);
            UiPaint.Shadow(ctx, frame, new Vector2(0f, 3f), UiPaint.C(0, 0, 0, 0.35f), 6f);
            UiPaint.Fill(ctx, frame, UiPaint.C(20, 32, 60, 0.92f));
            var cells = (int)BattleSim.MaxCost;
            var inner = new Rect(bar.xMin + 6f, bar.yMin + 5f, bar.width - 12f, bar.height - 10f);
            var cw = inner.width / cells;
            for (var i = 0; i < cells; i++)
            {
                var c = new Rect(inner.xMin + i * cw + 2f, inner.yMin, cw - 4f, inner.height);
                var poly = UiPaint.SkewRect(c, slant * (inner.height / bar.height), 2f, 2);
                UiPaint.Fill(ctx, poly, UiPaint.C(60, 80, 120, 0.8f));
                var lit = Mathf.Clamp01(cost - i);
                if (lit >= 1f)
                    UiPaint.Fill(ctx, poly, UiPaint.Vertical(UiPaint.C(120, 236, 255), UiPaint.C(30, 190, 245), c.yMin, c.yMax));
                else if (lit > 0f)
                {
                    var part = UiPaint.Clip(poly, UiPaint.RoundRect(Rect.MinMaxRect(c.xMin - 10f, c.yMin - 2f, c.xMin + (c.width + 10f) * lit, c.yMax + 2f), 0f));
                    UiPaint.Fill(ctx, part, UiPaint.C(90, 200, 240, 0.8f), 0f);
                }
            }
            var disc = UiPaint.Ellipse(new Vector2(r.xMin + r.height * 0.5f, r.center.y), r.height * 0.5f, r.height * 0.5f);
            UiPaint.Shadow(ctx, disc, new Vector2(0f, 3f), UiPaint.C(0, 0, 0, 0.35f), 6f);
            UiPaint.Fill(ctx, disc, Color.white);
            UiPaint.Fill(ctx, UiPaint.Ellipse(new Vector2(r.xMin + r.height * 0.5f, r.center.y), r.height * 0.5f - 4f, r.height * 0.5f - 4f),
                         UiPaint.Vertical(UiPaint.C(46, 70, 118), UiPaint.C(24, 38, 72), r.yMin, r.yMax));
        }

        void UpdateExBar()
        {
            if (_sim == null) return;

            // Update Cost Bar
            if (_costFill != null)
            {
                var costPercent = (Mathf.Clamp(_sim.Cost, 0f, BattleSim.MaxCost) / BattleSim.MaxCost) * 100f;
                _costFill.style.width = Length.Percent(costPercent);
            }
            var whole = Mathf.FloorToInt(_sim.Cost);
            if (_costLabel != null && whole != _hudCost)
            {
                _hudCost = whole;
                _costLabel.text = whole.ToString();
                if (_costWords != null) _costWords.text = $"COST  {whole:00}<size=70%> / {(int)BattleSim.MaxCost}</size>";
            }
            // the painted gauge again only when its fill has moved a visible step (a repaint rebuilds its polygons)
            var step = Mathf.RoundToInt(_sim.Cost / BattleSim.MaxCost * 240f);
            if (step != _hudCostStep) { _hudCostStep = step; _costBar?.MarkDirtyRepaint(); }

            // Update EX Buttons
            foreach (var pair in _exButtons)
            {
                var h = pair.Key;
                var (btn, charge, label) = pair.Value;

                if (h == null || btn == null) continue;

                // 1. Skill cooldown/charge vertical fill
                if (charge != null)
                {
                    var chargePercent = h.SkillReady ? 0f : (1f - h.SkillCharge) * 100f;
                    charge.style.height = Length.Percent(chargePercent);
                }

                // 2. Can afford and is ready?
                var readyAndAffordable = h.SkillReady && _sim.CanAfford(h);
                btn.EnableInClassList("ex-button--ready", readyAndAffordable);
                btn.EnableInClassList("ex-button--spent", !readyAndAffordable);
                // cost not there yet: the card dims and its cost turns red (ui_score 07-BattleHud #1)
                btn.EnableInClassList("ex-button--poor", !_sim.CanAfford(h));
                btn.SetEnabled(h.Alive); // cannot cast if dead
            }
        }

        /// <summary>
        /// The run is over. There is nothing to report and nobody to report it to: this is an auto
        /// battle that restarts itself, so a dialog is a thing to dismiss between two fights the
        /// player did not start either. The payout already landed in the counters along the bottom;
        /// the log gets one line and the next run begins.
        /// </summary>
        void ShowResult()
        {
            if (_sim != null && _sim.Won) _world?.Celebrate(true);
            AudioService.Play(_sim.Won ? "victory" : "defeat");
            _resultView = _root;   // marks the result as reported for this run

            var gems = _sim.GemsDropped + (_sim.Won ? 5 + _sim.GemBonus : 0);
            Log(_sim.Won
                ? $"전 구간 처리 완료 — 골드 +{_sim.GoldEarned:N0} · 보석 +{gems}"
                : $"처리 실패 — 골드 +{_sim.GoldEarned:N0} · 보석 +{gems}");

            // The reference game's result is not a window. The fight stays on screen, a large
            // yellow italic title lands top-left, the run's numbers sit in a navy plate
            // top-right, the squad that fought is a strip of small cards bottom-left, and the way
            // on is one cyan plate bottom-right.
            var popup = UiKit.Div("bresult", _root);
            _resultPopup = popup;
            // the HUD pill, the EX cards and the cost bar make way for the result (the reference
            // shows only the title, the numbers and the squad)
            _root.AddToClassList("battle--result");
            void Close()
            {
                _root.RemoveFromClassList("battle--result");
                popup.RemoveFromHierarchy();
                _restartIn = 0f;
                NewRun();
            }

            // the title on a slanted band that fades out to the right, a cyan hairline under it and
            // the phase in small caps (ui_score 07-Fight1 #1) — a result, not a word on the street
            var band = UiKit.Div("bresult__band" + (_sim.Won ? "" : " bresult__band--lose"), popup);
            band.pickingMode = PickingMode.Ignore;
            var won = _sim.Won;
            ModalFrame.Painted(band, (ctx, r) =>
            {
                var poly = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.6f, 0f);
                var c0 = won ? UiPaint.C(16, 30, 60, 0.9f) : UiPaint.C(40, 20, 30, 0.9f);
                UiPaint.Fill(ctx, poly, UiPaint.Horizontal(c0, UiPaint.C(16, 30, 60, 0f), r.xMin + r.width * 0.45f, r.xMax));
                UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin, r.yMax - 4f, r.xMin + r.width * 0.7f, r.yMax), 0f),
                             UiPaint.Horizontal(won ? UiPaint.C(0, 229, 255) : UiPaint.C(255, 90, 110), UiPaint.C(0, 229, 255, 0f), r.xMin, r.xMin + r.width * 0.7f));
            });
            var title = UiKit.Text(_sim.Won ? "Battle Complete" : _sim.TimedOut ? "Time Over" : "Battle Failed",
                                   "bresult__title" + (_sim.Won ? "" : " bresult__title--lose"), band);
            title.pickingMode = PickingMode.Ignore;
            UiKit.Text($"PHASE {_sim.Stage}  ·  {(_sim.Won ? "CLEAR" : "FAILED")}", "bresult__kicker", band).pickingMode = PickingMode.Ignore;

            var info = UiKit.Div("bresult__info", popup);
            ModalFrame.Painted(info, (ctx, r) =>
            {
                var poly = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.5f, 8f);
                UiPaint.Shadow(ctx, poly, new Vector2(0f, 4f), UiPaint.C(0, 0, 0, 0.3f), 10f);
                UiPaint.Fill(ctx, poly, UiPaint.Horizontal(UiPaint.C(20, 34, 64, 0.9f), UiPaint.C(30, 52, 92, 0.82f), r.xMin, r.xMax));
                UiPaint.Stroke(ctx, poly, UiPaint.C(0, 210, 255, 0.55f), 1.5f);
                UiPaint.Fill(ctx, UiPaint.Clip(poly, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin, r.yMax - 3f, r.xMax, r.yMax + 2f), 0f)), UiPaint.C(0, 210, 255, 0.8f), 0f);
            });
            var line = UiKit.Div("bresult__line", info);
            UiKit.Text($"Phase {_sim.Stage}", "bresult__phase", line);
            var secs = Mathf.FloorToInt(_sim.Elapsed);
            UiKit.Text($"소요 시간  {secs / 60:00}:{secs % 60:00}", "bresult__time", line);
            var gainRow = UiKit.Div("bresult__gains", info);
            Gain(gainRow, "gold", Icons.Gold, $"+{_sim.GoldEarned:N0}");
            Gain(gainRow, "gem", Icons.Gem, $"+{gems:N0}");

            var squad = UiKit.Div("bresult__squad", popup);
            UiKit.Text("STRIKER", "bresult__squad-label", squad);
            var strip = UiKit.Div("bresult__strip", squad);
            // who did the work: each card with its share of the damage, the top one marked MVP
            var dealt = _sim.Heroes.ToDictionary(h => h.heroId, h => h.dealt);
            var total = System.Math.Max(1L, dealt.Values.Sum());
            var mvp = dealt.Count > 0 ? dealt.OrderByDescending(kv => kv.Value).First().Key : null;
            foreach (var id in Game.Player.party)
            {
                var def = GameData.Hero(id);
                if (def == null) continue;
                var cell = UiKit.Div("bresult__cell", strip);
                var mini = UiKit.Div("bresult__mini", cell);
                var share = dealt.TryGetValue(id, out var dv) ? dv / (float)total : 0f;
                var meter = UiKit.Div("bresult__meter", cell);
                UiKit.Div("bresult__meter-fill", meter).style.width = Length.Percent(share * 100f);
                UiKit.Text($"{share:P0}", "bresult__share", cell);
                if (id == mvp && dv > 0) UiKit.Text("MVP", "bresult__mvp", cell).pickingMode = PickingMode.Ignore;
                var standing = GameData.StandingArt(id);
                var sprite = standing ?? GameData.WornCardArt(id);
                var gradeColor = GameData.Grade(def.grade)?.Color ?? Color.gray;
                ModalFrame.Painted(mini, (ctx, r) =>
                {
                    var poly = UiPaint.RoundRect(r, 8f, 4);
                    UiPaint.Shadow(ctx, poly, new Vector2(0f, 3f), UiPaint.C(0, 0, 0, 0.3f), 6f);
                    UiPaint.Fill(ctx, poly, Color.white);
                    if (standing != null)
                    {
                        UiPaint.Fill(ctx, UiPaint.Offset(poly, -3f), UiPaint.C(226, 238, 250));
                        UiKit.PaintPortrait(ctx, UiPaint.Offset(poly, -3f), standing, id, r, UiKit.Crop.Face);
                    }
                    else UiPaint.Image(ctx, UiPaint.Offset(poly, -3f), sprite, r, 0.08f);
                    UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin + 3f, r.yMax - 8f, r.xMax - 3f, r.yMax - 3f), 0f), gradeColor, 0f);
                });
            }

            var acts = UiKit.Div("bresult__acts", popup);
            UiKit.Btn("확인", "btn bresult__btn", Close, acts);
            var confirmBtn = UiKit.Btn(_sim.Won ? "다음 Phase" : "다시 도전", "btn btn--primary bresult__btn", Close, acts);
            Juice.PressAll(popup);

            // If auto-advance is ON, we automatically auto-confirm after 3.2 seconds so it doesn't block idle loop
            if (Game.Player.autoAdvance)
            {
                confirmBtn.schedule.Execute(() =>
                {
                    if (popup.parent != null) Close();
                }).ExecuteLater(3200);
            }
        }

        static void Gain(VisualElement parent, string icon, string glyph, string text)
        {
            var g = UiKit.Div("bresult__gain", parent);
            var sprite = GameData.Icon(icon);
            if (sprite != null) UiKit.SetArt(UiKit.Div("bresult__gain-icon", g), sprite);
            else UiKit.Text(glyph, "icon bresult__gain-glyph", g);
            UiKit.Text(text, "bresult__gain-text", g);
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
