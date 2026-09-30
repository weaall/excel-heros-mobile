using System;
using System.Collections.Generic;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    public interface IScreen
    {
        /// <summary>What the name box shows while this sheet is open, e.g. "A1".</summary>
        string Cell { get; }
        /// <summary>The formula bar's contents — flavour, but it is the disguise doing the work.</summary>
        string Formula { get; }
        VisualElement Build();
        void Refresh();
        /// <summary>Ribbon buttons for this sheet, left to right. Empty means an empty ribbon.</summary>
        IEnumerable<RibbonItem> Ribbon() => Array.Empty<RibbonItem>();
    }

    public readonly struct RibbonItem
    {
        public readonly string Icon, Label, Sub;
        public readonly Action Click;
        public readonly bool Separator;

        public RibbonItem(string icon, string label, Action click, string sub = null)
        { Icon = icon; Label = label; Click = click; Sub = sub; Separator = false; }

        RibbonItem(bool separator)
        { Icon = Label = Sub = null; Click = null; Separator = separator; }

        public static RibbonItem Sep => new RibbonItem(true);
    }

    /// <summary>
    /// The single MonoBehaviour that runs the game. It mounts AppShell.uxml — an Excel window — and
    /// swaps sheets under the shared ribbon and formula bar.
    ///
    /// The sheet tabs are the navigation, exactly as in the web build. That is not decoration: the
    /// premise is that this survives being looked at over your shoulder at work, so the chrome has
    /// to be Excel's own, down to the tab order and the formula sitting in the bar.
    ///
    /// Screens are rebuilt on entry rather than kept warm — the roster is 55 cards and the whole
    /// tree costs less to construct than it does to keep in sync, and rebuilding removes a whole
    /// class of stale-UI bugs.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class AppRoot : MonoBehaviour
    {
        public enum Sheet { Home, Battle, Roster, Party, Gacha, Quests, Progress, Story, Album, Codex, Chart, Shop }

        public VisualElement Overlay { get; private set; }

        UIDocument _doc;
        VisualElement _content;
        Label _progressBadge;
        Label _gems, _gold, _dailyBadge, _status, _stage, _plateSub, _screenTitle, _screenEn;
        VisualElement _plate, _navBack, _navbar;
        readonly Dictionary<Sheet, IScreen> _screens = new();
        readonly Dictionary<Sheet, Button> _tabs = new();
        readonly Dictionary<Sheet, string> _tabNames = new();
        Sheet _sheet = Sheet.Home;
        IScreen _current;
        HeroDetail _detail;

        void Awake()
        {
            _doc = GetComponent<UIDocument>();
            Game.Boot();
            AudioService.Init(gameObject);
        }

        void OnEnable()
        {
            var root = _doc.rootVisualElement;
            _content = root.Q<VisualElement>("content");
            Overlay = root.Q<VisualElement>("overlay");
            _gems = root.Q<Label>("gemValue");
            _gold = root.Q<Label>("goldValue");
            _status = root.Q<Label>("statusText");
            _dailyBadge = root.Q<Label>("dailyBadge");
            _progressBadge = root.Q<Label>("progressBadge");
            _stage = root.Q<Label>("stageValue");
            _plateSub = root.Q<Label>("plateSub");
            _plate = root.Q<VisualElement>("playerPlate");
            _navBack = root.Q<VisualElement>("navBack");
            _screenTitle = root.Q<Label>("screenTitle");
            // the screen's English sub-label beside its name (ui_critique: the tactical-admin micro type)
            if (_screenTitle?.parent != null)
            {
                _screenEn = new Label { pickingMode = PickingMode.Ignore }; _screenEn.AddToClassList("navback__en");
                _screenTitle.parent.Insert(_screenTitle.parent.IndexOf(_screenTitle) + 1, _screenEn);
            }
            _navbar = root.Q<VisualElement>("navbar");

            _screens[Sheet.Home] = new HomeScreen(this);
            _screens[Sheet.Battle] = new BattleScreen(this);
            _screens[Sheet.Roster] = new RosterScreen(this);
            _screens[Sheet.Party] = new PartyScreen(this);
            _screens[Sheet.Gacha] = new GachaScreen(this);
            _screens[Sheet.Quests] = new DailyScreen(this);
            _screens[Sheet.Progress] = new ProgressScreen(this);
            _screens[Sheet.Story] = new StoryScreen(this);
            _screens[Sheet.Album] = new AlbumScreen(this);
            _screens[Sheet.Codex] = new CodexScreen(this);
            _screens[Sheet.Chart] = new ChartScreen(this);
            _screens[Sheet.Shop] = new ShopScreen(this);
            _detail = new HeroDetail(this);

            // Every screen on the bar, which is how the reference arranges its lobby: 카페,
            // 스케줄, 학생, 편성, 서클, 제조, 상점, 모집, all visible, nothing behind a menu. This
            // build had four down a rail and four behind 더보기 — and a screen behind a menu is a
            // screen players never learn exists.
            Bind("tabHome", Sheet.Home);
            Bind("tabRoster", Sheet.Roster);
            Bind("tabParty", Sheet.Party);
            Bind("tabQuests", Sheet.Quests);
            Bind("tabProgress", Sheet.Progress);
            Bind("tabStory", Sheet.Story);
            Bind("tabAlbum", Sheet.Album);
            Bind("tabCodex", Sheet.Codex);
            Bind("tabChart", Sheet.Chart);
            Bind("tabGacha", Sheet.Gacha);
            _tabNames[Sheet.Battle] = "출근";

            // Glyphs come from the Material Symbols font rather than from PNGs, so an icon that
            // has to be white here and grey there is one character rather than two images.
            Glyph("overflowBtn", Icons.Settings);
            Glyph("homeBtn", Icons.Home);
            Glyph("backBtn", Icons.Back);

            // Back and home both mean 메인: the main screen is the one every other screen is
            // entered from, the way the reference's lobby is.
            // 모집 is the one button in the shell built by UXML rather than UiKit.Btn, so its
            // plate has to be asked for here.
            var cta = root.Q<Button>("tabGacha");
            if (cta != null) SkewPlate.Apply(cta, SkewPlate.Kind.Gold);

            var settings = root.Q<Button>("overflowBtn");
            if (settings != null) settings.clicked += () => OpenSettings();

            var back = root.Q<Button>("backBtn");
            // Back closes a page opened over the screen first (the hero page), then goes home.
            if (back != null) back.clicked += () =>
            {
                AudioService.Play("back", 0.55f);
                if (Overlay != null && Overlay.ClassListContains("overlay--page") && !Overlay.ClassListContains("hidden")) CloseOverlay();
                else Show(Sheet.Home);
            };
            var home = root.Q<Button>("homeBtn");
            if (home != null) home.clicked += () => { AudioService.Play("nav", 0.5f); Show(Sheet.Home); };
            // Illustrated icons (Resources/Art/Icons, tools/gen_icons_gemini.py). The reference's
            // icons are small drawings, and the line glyphs these replace were the furthest thing
            // from them. The glyph stays as the fallback for an icon that has not been generated.
            Art("goldIcon", "gold", Icons.Gold);
            Art("gemIcon", "gem", Icons.Gem);
            Art("tabHomeIcon", "lobby", Icons.Battle);
            Art("tabRosterIcon", "roster", Icons.Roster);
            Art("tabPartyIcon", "party", Icons.Shield);
            Art("tabQuestsIcon", "tasks", Icons.Tasks);
            Art("tabProgressIcon", "review", Icons.Star);
            Art("tabStoryIcon", "messenger", Icons.Story);
            Art("tabAlbumIcon", "album", Icons.Album);
            Art("tabCodexIcon", "codex", Icons.Codex);
            Art("tabChartIcon", "chart", Icons.Chart);
            Art("tabGachaIcon", "recruit", Icons.Gacha);

            // The shell's own look — backdrop, top strip, pills, player plate, bottom strip —
            // painted to the reference screenshots. See Chrome.
            Chrome.Dress(root.Q<VisualElement>("root") ?? root);

            // Touch answers: the press-and-spring on every shell button, a cyan burst wherever
            // the screen is touched. See Juice.
            Juice.PressAll(root);
            Juice.Touches(root.Q<VisualElement>("root") ?? root);

            // Keeps the chrome clear of the notch and the gesture bar.
            (gameObject.GetComponent<SafeArea>() ?? gameObject.AddComponent<SafeArea>()).Bind(root);

            Game.Changed += OnGameChanged;
            Show(Sheet.Home);

            // The opening comes before anything else, and the tips only after it — the web build
            // does the same, and stacking them would put a coach mark on top of a story beat.
            if (PrologueScreen.Needed(Game.Player))
                new PrologueScreen(this, ShowOnboardingIfNeeded).Show();
            else ShowOnboardingIfNeeded();

            void Art(string name, string icon, string glyph)
            {
                var e = root.Q<VisualElement>(name);
                if (e == null) return;
                var sprite = GameData.Icon(icon);
                if (sprite == null) { Glyph(name, glyph); return; }
                if (e is TextElement t) t.text = "";
                e.RemoveFromClassList("icon");
                e.AddToClassList("art-icon");
                e.style.backgroundImage = new StyleBackground(sprite);
            }

            void Glyph(string name, string ch)
            {
                var e = root.Q<VisualElement>(name);
                if (e is Button b) b.text = ch;
                else if (e is Label l) l.text = ch;
            }

            void Bind(string name, Sheet sheet)
            {
                var button = root.Q<Button>(name);
                if (button == null) return;
                _tabNames[sheet] = button.text;
                button.clicked += () => { AudioService.Play("nav", 0.5f); Show(sheet); };
                _tabs[sheet] = button;
            }
        }

        void ShowOnboardingIfNeeded()
        {
            if (Onboarding.Needed(Game.Player)) { new Onboarding(this).Show(); return; }
            ShowIdleIfAny();
        }

        /// <summary>
        /// 백그라운드 정산 — what the run earned while the game was shut. After the tips rather
        /// than before them: on a first launch there is nothing to report anyway, and a returning
        /// player should see this before anything else.
        /// </summary>
        void ShowIdleIfAny()
        {
            var report = Game.TakeIdle();
            if (!report.Worth) return;
            ShowIdle(report);
        }

        /// <summary>The 백그라운드 정산 modal for a report (the capture driver shows one with a made-up absence).</summary>
        public void ShowIdle(IdleService.Report report)
        {

            // a reward modal (the user: the gold read as plain white text, and gems belong here too): the time away,
            // then a tile per reward — the illustrated icon, the amount in its own colour — and one cyan plate
            var body = UiKit.Modal("백그라운드 정산", null, out var pane, "modal--idle");
            UiKit.Text($"{IdleService.Duration(report.Seconds)} 동안 사원들이 대신 일했습니다", "idle__away", body);
            var tiles = UiKit.Div("idle__tiles", body);
            void Tile(string icon, string glyph, string amount, string label, string kind)
            {
                var t = UiKit.Div("idle__tile idle__tile--" + kind, tiles);
                ModalFrame.Painted(t, (ctx, r) =>
                {
                    var box = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.35f, 10f);
                    UiPaint.Shadow(ctx, box, new Vector2(0f, 5f), UiPaint.C(20, 40, 90, 0.18f), 10f);
                    UiPaint.Fill(ctx, box, UiPaint.Vertical(UiPaint.C(255, 255, 255), kind == "gold" ? UiPaint.C(255, 244, 214) : UiPaint.C(224, 242, 255), r.yMin, r.yMax));
                    UiPaint.Stroke(ctx, box, kind == "gold" ? UiPaint.C(240, 190, 70) : UiPaint.C(90, 190, 250), 2.5f);
                });
                var sp = GameData.Icon(icon);
                if (sp != null) UiKit.SetArt(UiKit.Div("idle__icon", t), sp); else UiKit.Text(glyph, "icon idle__glyph", t);
                UiKit.Text(amount, "idle__amount idle__amount--" + kind, t);
                UiKit.Text(label, "idle__label", t);
            }
            Tile("gold", Icons.Gold, $"+{UiKit.Num(report.Gold)}", "골드", "gold");
            if (report.Gems > 0) Tile("gem", Icons.Gem, $"+{report.Gems:N0}", "보석", "gem");
            if (report.Capped)
                UiKit.Text($"정산은 최대 {IdleService.CapSeconds / 3600}시간까지 쌓입니다", "idle__note", body);

            var claim = UiKit.Btn("수령", "idle__claim", () =>
            {
                if (IdleService.Grant(Game.Player, report))
                {
                    AudioService.Play("victory", 0.6f);
                    SetStatus(report.Gems > 0 ? $"백그라운드 정산 · 골드 +{UiKit.Num(report.Gold)} · 보석 +{report.Gems}" : $"백그라운드 정산 · 골드 +{UiKit.Num(report.Gold)}");
                    Game.Touch();
                }
                CloseOverlay();
            }, body);
            SkewPlate.Apply(claim, SkewPlate.Kind.Primary);

            OpenOverlay(pane);
        }

        void OnDisable()
        {
            Game.Changed -= OnGameChanged;
            Game.FlushSave();
        }

        void OnApplicationPause(bool paused) { if (paused) Game.FlushSave(); }

        void Update()
        {
            Game.Tick(Time.deltaTime);
            // 메인 전투는 자동전투라 항상 돌아간다. Ticking only the sheet on screen meant the run
            // froze the moment you opened the roster to spend the gold it was earning, which is
            // backwards for an idle game — the reason to browse is that the fight keeps going.
            if (_screens.TryGetValue(Sheet.Battle, out var bScreen) && bScreen is BattleScreen battle)
                battle.Tick(Time.deltaTime);
        }

        /// <summary>
        /// target_1's player plate: the lead's face in a white-rimmed disc at its left end, the
        /// level over the name. Rebuilt on every visit to the lobby, as the lead can change.
        /// </summary>
        void PlateAvatar()
        {
            if (_plate == null) return;
            var body = _plate.Q<VisualElement>(className: "plate__body");
            var rank = _plate.Q<VisualElement>(className: "plate__rank");
            if (body != null && rank != null && rank.parent != body) body.Insert(0, rank);
            _plate.Q<VisualElement>("plateAvatar")?.RemoveFromHierarchy();
            var ring = new VisualElement { name = "plateAvatar", pickingMode = PickingMode.Ignore };
            ring.AddToClassList("plate__avatar");
            var face = new VisualElement { pickingMode = PickingMode.Ignore };
            face.AddToClassList("plate__face");
            ring.Add(face);
            _plate.Insert(0, ring);
            UiKit.SetPortrait(face, HomeScreen.LeadHeroId(), UiKit.Crop.Face, round: true);
            // the reference's thin cyan bar under the name: progress through the current ten phases
            body?.Q<VisualElement>("plateBar")?.RemoveFromHierarchy();
            if (body != null && Game.Player != null)
            {
                var track = new VisualElement { name = "plateBar", pickingMode = PickingMode.Ignore }; track.AddToClassList("plate__bar");
                var fill = new VisualElement { pickingMode = PickingMode.Ignore }; fill.AddToClassList("plate__bar-fill");
                fill.style.width = Length.Percent(Mathf.Clamp01(((Game.Player.stage - 1) % 10 + 1) / 10f) * 100f);
                track.Add(fill); body.Add(track);
            }
        }

        void OnGameChanged()
        {
            UpdateStatus();
            _current?.Refresh();
            if (_content != null) UiKit.TitleSubs(_content);
        }

        void UpdateStatus()
        {
            if (Game.Player == null) return;
            _gems.text = Game.Player.gems.ToString("N0");
            _gold.text = UiKit.Num(Game.Player.gold);
            if (_stage != null) _stage.text = Game.Player.stage.ToString();
            if (_plateSub != null) _plateSub.text = $"사원 {Game.Player.owned.Count}명";
            if (_dailyBadge == null) return;
            var n = QuestService.ReadyCount(Game.Player);
            _dailyBadge.text = n.ToString();
            _dailyBadge.EnableInClassList("hidden", n == 0);

            if (_progressBadge == null) return;
            var m = ProgressService.ReadyCount(Game.Player);
            _progressBadge.text = m.ToString();
            _progressBadge.EnableInClassList("hidden", m == 0);
        }

        public void Show(Sheet sheet)
        {
            // A panel belongs to the screen that opened it. Changing screens under an open one left
            // it hanging over the new sheet, which is how a capture of 메신저 came back showing the
            // menu that had been used to reach it.
            if (Overlay != null && !Overlay.ClassListContains("hidden"))
            {
                Overlay.Clear();
                Overlay.AddToClassList("hidden");
            }

            var from = _sheet;
            _sheet = sheet;
            _current = _screens[sheet];
            _content.Clear();
            var built = _current.Build();
            _content.Add(built);
            Juice.Enter(built);
            Juice.PressAll(built);
            UiKit.TitleSubs(built);
            UpdateStatus();

            foreach (var pair in _tabs)
            {
                pair.Value.EnableInClassList("navtab--active",
                    pair.Key == sheet && !pair.Value.ClassListContains("navcta"));
                pair.Value.MarkDirtyRepaint();   // the glass tile is painted lit or not
            }

            // The plate on 메인, the back arrow and the screen's name everywhere else — which is
            // the arrangement the reference uses between its lobby and everything inside it.
            var home = sheet == Sheet.Home;
            _plate?.EnableInClassList("hidden", !home);
            if (home) PlateAvatar();
            _navBack?.EnableInClassList("hidden", home);
            Chrome.SetLobby(_doc.rootVisualElement, home);
            // The bottom bar belongs to the lobby only. Inside a screen the reference has none —
            // the top edge's home button is the way out — and the screen gets the height back,
            // which is most of what "made for a phone" means on a 1080-tall landscape display.
            _navbar?.EnableInClassList("hidden", !home);
            // In a fight the reference shows no currencies: the band is the back arrow and the
            // title only, so the field reads (ui_critique round 1, 07-BattleHud #2)
            var battle = sheet == Sheet.Battle;
            // into a fight from elsewhere: the reference's Now Loading, the cast's gag panels for a beat
            if (battle && from != Sheet.Battle && !LoadingScreen.Suppress) LoadingScreen.Show(_doc.rootVisualElement, 1.4f);
            _doc.rootVisualElement.Q<VisualElement>("root")?.EnableInClassList("shell--battle", battle);
            if (_screenTitle != null) _screenTitle.text = TitleOf(sheet);
            if (_screenEn != null) _screenEn.text = EnOf(sheet);
        }

        static string EnOf(Sheet sheet) => sheet switch
        {
            Sheet.Roster => "EMPLOYEES", Sheet.Party => "FORMATION", Sheet.Gacha => "RECRUIT",
            Sheet.Quests => "DAILY TASKS", Sheet.Progress => "REVIEW", Sheet.Story => "MESSENGER",
            Sheet.Album => "ALBUM", Sheet.Codex => "ERROR CODEX", Sheet.Chart => "STATISTICS", Sheet.Shop => "SHOP",
            _ => "",
        };

        static string TitleOf(Sheet sheet) => sheet switch
        {
            Sheet.Home => "로비",
            Sheet.Battle => "출근",
            Sheet.Roster => "사원",
            Sheet.Party => "편성",
            Sheet.Gacha => "모집",
            Sheet.Quests => "일일 업무",
            Sheet.Progress => "검토",
            Sheet.Story => "사내 메신저",
            Sheet.Album => "사원 앨범",
            Sheet.Codex => "오류 도감",
            Sheet.Chart => "통계",
            Sheet.Shop => "상점",
            _ => "로비",
        };

        /// <summary>Locked while a reveal is playing so a sheet change cannot strand the overlay.</summary>
        public void SetNavEnabled(bool enabled)
        {
            foreach (var pair in _tabs) pair.Value.SetEnabled(enabled);
        }

        /// <summary>
        /// The status line, which is hidden when there is nothing to say.
        ///
        /// It used to be a permanent 44px band under the tab bar, empty on every screen that had
        /// not set it - a strip of background with nothing in it, below the navigation, on every
        /// single sheet. USS has no :empty, so the toggle lives here.
        /// </summary>
        public void SetStatus(string text)
        {
            if (_status == null) return;
            // a toast, not a strip (the user: the flat bar that stayed along the bottom looked cheap): a navy slanted
            // pill low in the middle, in for ~2.6 s and faded out
            _status.text = text ?? "";
            var on = !string.IsNullOrEmpty(_status.text);
            _status.EnableInClassList("hidden", !on);
            _status.RemoveFromClassList("statusbar--out");
            _statusHide?.Pause();
            if (!on) return;
            _status.style.color = Color.white;   // inline: the shell's label colour outranked the class
            _status.MarkDirtyRepaint();
            _statusHide = _status.schedule.Execute(() =>
            {
                _status.AddToClassList("statusbar--out");
                _status.schedule.Execute(() => { if (_status.ClassListContains("statusbar--out")) _status.AddToClassList("hidden"); }).StartingIn(400);
            }).StartingIn(2600);
        }
        IVisualElementScheduledItem _statusHide;
        bool _statusPainted;

        public void Rebuild() => Show(_sheet);

        /// <summary>
        /// 설정 — the gear. It opened the ad sheet straight away, which is not what a gear promises
        /// (the user). Now a modal with three tabs: 설정 (sound on / off and its volume, kept in
        /// PlayerPrefs; the battle's 자동 전투 / 자동 진행 / 안전 진행 switches, kept in the save),
        /// 광고 보상 (every offer pays a flat amount — see AdService — and the foot says so, because
        /// "closing the app never pays better" is the point of the design), and 쿠폰·데이터 (보석 코드
        /// and 세이브 이동).
        /// </summary>
        public void OpenSettings(int tab = 0)
        {
            AudioService.Play("tap", 0.5f);
            var body = UiKit.Modal("설정", CloseOverlay, out var panel, "settings-modal");
            var tabs = UiKit.Div("stabs", body);
            var page = UiKit.Div("spage", body);
            string[] names = { "설정", "광고 보상", "쿠폰 · 데이터" };
            for (var i = 0; i < names.Length; i++)
            {
                var ii = i;
                var b = UiKit.Btn(names[i], "stab" + (i == tab ? " stab--on" : ""), () => { CloseOverlay(); OpenSettings(ii); }, tabs);
                SkewPlate.Apply(b, i == tab ? SkewPlate.Kind.Primary : SkewPlate.Kind.Glass);   // one slanted family, the open one lit (ui_gate 22-Settings)
            }
            if (tab == 0) BuildSettingsPage(page);
            else if (tab == 1) BuildAdPage(page);
            else { BuildCodeRow(page); BuildSaveTransfer(page); }
            OpenOverlay(panel);
        }

        /// <summary>A settings row: the name and a line under it on the left, the control on the right.</summary>
        static VisualElement SettingRow(VisualElement parent, string name, string desc)
        {
            var row = UiKit.Div("srow", parent);
            var text = UiKit.Div("srow__text", row);
            UiKit.Text(name, "srow__name", text);
            if (!string.IsNullOrEmpty(desc)) UiKit.Text(desc, "srow__desc", text);
            return row;
        }

        /// <summary>An ON / OFF switch: a slanted pill, lit cyan when on.</summary>
        static void Switch(VisualElement row, bool on, System.Action<bool> set)
        {
            var b = UiKit.Btn(on ? "ON" : "OFF", "stoggle" + (on ? " stoggle--on" : ""), null, row);
            b.clicked += () =>
            {
                on = !on; set(on);
                b.text = on ? "ON" : "OFF";
                b.EnableInClassList("stoggle--on", on);
                b.MarkDirtyRepaint();
            };
            // a switch, not a button (ui_gate 22-Settings: which colour is ON was a guess): a round knob
            // that sits right and lit when on, left and grey when off, the word on the other side
            ModalFrame.Painted(b, (ctx, r) =>
            {
                var lit = b.ClassListContains("stoggle--on");
                var p = UiPaint.RoundRect(r, r.height * 0.5f, 8);
                UiPaint.Fill(ctx, p, lit ? UiPaint.Vertical(UiPaint.C(90, 226, 255), UiPaint.C(0, 170, 236), r.yMin, r.yMax)
                                         : UiPaint.Vertical(UiPaint.C(206, 214, 226), UiPaint.C(188, 198, 214), r.yMin, r.yMax));
                UiPaint.Stroke(ctx, p, lit ? UiPaint.C(0, 150, 220) : UiPaint.C(170, 184, 204), 2f);
                var rad = r.height * 0.5f - 6f;
                var c = new Vector2(lit ? r.xMax - rad - 6f : r.xMin + rad + 6f, r.center.y);
                UiPaint.Shadow(ctx, UiPaint.Ellipse(c, rad, rad), new Vector2(0f, 2f), UiPaint.C(0, 0, 0, 0.25f), 4f);
                UiPaint.Fill(ctx, UiPaint.Ellipse(c, rad, rad), Color.white);
            });
        }

        void BuildSettingsPage(VisualElement page)
        {
            var p = Game.Player;
            UiKit.Text("사운드", "spage__head", page);
            Switch(SettingRow(page, "효과음", "버튼 · 전투 · 모집 소리"), !AudioService.Muted, v => { AudioService.Muted = !v; AudioService.Save(); });
            var vol = SettingRow(page, "음량", null);
            var slider = new Slider(0f, 100f) { value = AudioService.Volume * 100f };
            slider.AddToClassList("sslider");
            var readout = UiKit.Text($"{Mathf.RoundToInt(AudioService.Volume * 100f)}", "sslider__value", null);
            slider.RegisterValueChangedCallback(e => { AudioService.Volume = e.newValue / 100f; readout.text = $"{Mathf.RoundToInt(e.newValue)}"; AudioService.Save(); });
            vol.Add(slider); vol.Add(readout);

            UiKit.Text("전투", "spage__head", page);
            Switch(SettingRow(page, "자동 전투", "EX 스킬을 코스트가 차는 대로 자동 사용"), p.autoSkill, v => { p.autoSkill = v; Game.Touch(); });
            Switch(SettingRow(page, "자동 진행", "클리어하면 다음 Phase로 (끄면 이 Phase 반복 · 파밍)"), p.autoAdvance, v => { p.autoAdvance = v; Game.Touch(); });
            Switch(SettingRow(page, "안전 진행", "승산이 낮으면 다음 Phase로 넘어가지 않음"), p.safeAdvance, v => { p.safeAdvance = v; Game.Touch(); });
        }

        void BuildAdPage(VisualElement page)
        {
            var p = Game.Player;
            var head = UiKit.Div("spage__row", page);
            UiKit.Text("광고 보상", "spage__head", head);
            UiKit.Div("spacer", head);
            UiKit.Text($"오늘 남은 광고 {AdService.LeftToday(p)} / {GameData.Balance.adPerDay}회", "spage__meta", head);
            foreach (var offer in AdService.Offers(p))
            {
                // the value only when the name does not already say it ("보석 +15  보석 +15"), and a reason it cannot be
                // watched in the line under the name, not squeezed into the button (it ran out of the plate)
                var title = string.IsNullOrEmpty(offer.Value) || offer.Def.name.Contains(offer.Value) ? offer.Def.name : $"{offer.Def.name}  {offer.Value}";
                var desc = offer.Can ? $"{offer.Def.desc} · 오늘 {offer.Left} / {offer.Def.perDay}회" : $"{offer.Reason} · 오늘 {offer.Left} / {offer.Def.perDay}회";
                var row = SettingRow(page, title, desc);
                var id = offer.Def.id;
                var watch = UiKit.Btn(offer.Can ? "광고 보기" : "지금은 불가", "arow__claim srow__btn srow__btn--go", () =>
                {
                    var told = AdService.Grant(Game.Player, id);
                    if (told == null) return;
                    AudioService.Play("victory", 0.6f);
                    SetStatus(told);
                    Game.Touch();
                    CloseOverlay();
                    OpenSettings(1);
                }, row);
                watch.SetEnabled(offer.Can);
                if (offer.Can) SkewPlate.Apply(watch, SkewPlate.Kind.Primary);   // the live offer reads as the thing to press (ui_gate 22-Settings1)
            }
            UiKit.Text("광고 1편 = 보상 1개 · 모든 보상은 고정 지급 (방치 배율 없음)", "spage__foot", page);
        }

        // 보석 코드 — a field and a button, beside the save transfer: both are "not playing the game"
        void BuildCodeRow(VisualElement page)
        {
            UiKit.Text("쿠폰", "spage__head", page);
            var codeRow = SettingRow(page, "보석 코드", "받은 코드를 입력하세요");
            var field = new TextField { value = "" };
            field.AddToClassList("code-field");
            field.AddToClassList("srow__field");
            codeRow.Add(field);
            var redeem = UiKit.Btn("등록", "arow__claim srow__btn", () =>
            {
                var r = CodeService.Redeem(Game.Player, field.value);
                if (!r.Ok) { SetStatus(r.Message); return; }
                AudioService.Play("victory", 0.6f);
                SetStatus($"{r.Message} · {CodeService.Paid(r)}");
                Game.Touch();
                CloseOverlay();
                OpenSettings(2);
            }, codeRow);
            SkewPlate.Apply(redeem, SkewPlate.Kind.Primary);   // it had no plate: white text on the pale row, invisible
            UiKit.Text("데이터", "spage__head", page);
        }

        /// <summary>
        /// 세이브 이동 — export to a string, import from one.
        ///
        /// It sits on this sheet because this is where everything that is not playing the game
        /// lives, and it matters more than it looks: cloud sync is blocked on a mobile OAuth
        /// decision, so this is the only way a save reaches another device at all.
        ///
        /// Importing is the one destructive thing in the menu, so it asks first. There is no undo
        /// — the roster, the stage and every card go at once — and the confirm says that rather
        /// than assuming a player who pasted a code meant to overwrite everything.
        /// </summary>
        void BuildSaveTransfer(VisualElement pane)
        {
            var row = UiKit.Div("arow", pane);
            var text = UiKit.Div("arow__text", row);
            UiKit.Text("세이브 이동", "arow__name", text);
            UiKit.Text("코드를 복사해 두면 다른 기기에서 불러올 수 있습니다", "arow__meta", text);

            var field = new TextField { value = "", isReadOnly = false };
            field.AddToClassList("code-field");
            text.Add(field);

            var export = UiKit.Btn("내보내기", "arow__claim srow__btn", () =>
            {
                var code = SaveService.Export(Game.Player);
                if (code.Length == 0) { SetStatus("세이브를 내보내지 못했습니다"); return; }
                field.value = code;
                GUIUtility.systemCopyBuffer = code;
                SetStatus("세이브 코드를 복사했습니다");
            }, row);

            var import = UiKit.Btn("불러오기", "arow__claim srow__btn", () =>
            {
                var result = SaveService.Import(field.value);
                if (!result.Ok) { SetStatus(result.Error); return; }
                ConfirmImport(result.State);
            }, row);
            SkewPlate.Apply(export, SkewPlate.Kind.Primary);
            SkewPlate.Apply(import, SkewPlate.Kind.Navy);
        }

        /// <summary>The one confirm in this menu, because this is the one action that destroys
        /// something and cannot be taken back.</summary>
        void ConfirmImport(PlayerState incoming)
        {
            var pane = UiKit.Div("onboard__card idle");
            UiKit.Text("세이브 불러오기", "onboard__title", pane);
            UiKit.Text($"불러올 세이브 · Phase {incoming.stage} · 사원 {incoming.owned.Count}명", "muted", pane);
            UiKit.Text("이 기기의 진행은 모두 사라집니다. 되돌릴 수 없습니다.", "muted", pane);

            var row = UiKit.Div("party-actions", pane);
            UiKit.Btn("취소", "btn", CloseOverlay, row);
            UiKit.Btn("불러오기", "btn btn--primary", () =>
            {
                SaveService.Apply(incoming);
                AudioService.Play("victory", 0.6f);
                SetStatus("세이브를 불러왔습니다");
                CloseOverlay();
                Rebuild();
            }, row);

            OpenOverlay(pane);
        }

        public void OpenDetail(string heroId, string tab = null)
        {
            if (!string.IsNullOrEmpty(tab)) _detail.ShowTab(tab);
            Overlay.Clear();
            Overlay.RemoveFromClassList("hidden");
            // A page under the top bar, not a modal over everything (see HeroDetail.Build).
            Overlay.AddToClassList("overlay--page");
            Overlay.Add(_detail.Build(heroId, CloseOverlay));
            Juice.PressAll(Overlay);
            UiKit.TitleSubs(Overlay);
        }

        /// <summary>Puts one panel on the dimmed overlay.</summary>
        public void OpenOverlay(VisualElement panel)
        {
            Overlay.RemoveFromClassList("overlay--page");
            Overlay.RemoveFromClassList("overlay--clear");
            Overlay.Clear();
            Overlay.RemoveFromClassList("hidden");
            Overlay.Add(panel);
            Juice.PressAll(Overlay);
            UiKit.TitleSubs(Overlay);
        }

        public void CloseOverlay()
        {
            Overlay.RemoveFromClassList("overlay--page");
            Overlay.RemoveFromClassList("overlay--clear");
            Overlay.Clear();
            Overlay.AddToClassList("hidden");
            _current?.Refresh();
            if (_content != null) UiKit.TitleSubs(_content);
        }

    }
}
