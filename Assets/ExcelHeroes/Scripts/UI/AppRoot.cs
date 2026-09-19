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
        public enum Sheet { Home, Roster, Party, Gacha, Quests, Progress, Story, Album, Codex, Chart }

        public VisualElement Overlay { get; private set; }

        UIDocument _doc;
        VisualElement _content;
        Label _progressBadge;
        Label _gems, _gold, _dailyBadge, _status, _stage, _plateSub, _screenTitle;
        VisualElement _plate, _navBack;
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

            _screens[Sheet.Home] = new BattleScreen(this);
            _screens[Sheet.Roster] = new RosterScreen(this);
            _screens[Sheet.Party] = new PartyScreen(this);
            _screens[Sheet.Gacha] = new GachaScreen(this);
            _screens[Sheet.Quests] = new DailyScreen(this);
            _screens[Sheet.Progress] = new ProgressScreen(this);
            _screens[Sheet.Story] = new StoryScreen(this);
            _screens[Sheet.Album] = new AlbumScreen(this);
            _screens[Sheet.Codex] = new CodexScreen(this);
            _screens[Sheet.Chart] = new ChartScreen(this);
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

            // Glyphs come from the Material Symbols font rather than from PNGs, so an icon that
            // has to be white here and grey there is one character rather than two images.
            Glyph("overflowBtn", Icons.Settings);
            Glyph("homeBtn", Icons.Home);
            Glyph("backBtn", Icons.Back);

            // Back and home both mean 메인: the main screen is the one every other screen is
            // entered from, the way the reference's lobby is.
            var settings = root.Q<Button>("overflowBtn");
            if (settings != null) settings.clicked += OpenAdMenu;

            var back = root.Q<Button>("backBtn");
            if (back != null) back.clicked += () => { AudioService.Play("nav", 0.5f); Show(Sheet.Home); };
            var home = root.Q<Button>("homeBtn");
            if (home != null) home.clicked += () => { AudioService.Play("nav", 0.5f); Show(Sheet.Home); };
            Glyph("goldIcon", Icons.Gold);
            Glyph("gemIcon", Icons.Gem);
            Glyph("tabHomeIcon", Icons.Battle);
            Glyph("tabRosterIcon", Icons.Roster);
            Glyph("tabPartyIcon", Icons.Shield);
            Glyph("tabQuestsIcon", Icons.Tasks);
            Glyph("tabProgressIcon", Icons.Star);
            Glyph("tabStoryIcon", Icons.Story);
            Glyph("tabAlbumIcon", Icons.Album);
            Glyph("tabCodexIcon", Icons.Codex);
            Glyph("tabChartIcon", Icons.Chart);
            Glyph("tabGachaIcon", Icons.Gacha);

            // Keeps the chrome clear of the notch and the gesture bar.
            (gameObject.GetComponent<SafeArea>() ?? gameObject.AddComponent<SafeArea>()).Bind(root);

            Game.Changed += OnGameChanged;
            Show(Sheet.Home);

            // The opening comes before anything else, and the tips only after it — the web build
            // does the same, and stacking them would put a coach mark on top of a story beat.
            if (PrologueScreen.Needed(Game.Player))
                new PrologueScreen(this, ShowOnboardingIfNeeded).Show();
            else ShowOnboardingIfNeeded();

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

            var pane = UiKit.Div("onboard__card idle");
            UiKit.Text("백그라운드 정산", "onboard__title", pane);
            UiKit.Text($"{IdleService.Duration(report.Seconds)} 동안 자리를 비웠습니다.", "muted", pane);

            var value = UiKit.Div("power-readout", pane);
            UiKit.Text($"₩{report.Gold:N0}", "power-readout__value", value);
            UiKit.Text("골드", "power-readout__label", value);

            if (report.Capped)
                UiKit.Text($"정산은 최대 {IdleService.CapSeconds / 3600}시간까지 쌓입니다.", "muted", pane);

            UiKit.Btn("수령", "btn btn--primary", () =>
            {
                if (IdleService.Grant(Game.Player, report))
                {
                    AudioService.Play("victory", 0.6f);
                    SetStatus($"백그라운드 정산 · 골드 +{report.Gold:N0}");
                    Game.Touch();
                }
                CloseOverlay();
            }, pane);

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
            if (_screens.TryGetValue(Sheet.Home, out var home) && home is BattleScreen battle)
                battle.Tick(Time.deltaTime);
        }

        void OnGameChanged()
        {
            UpdateStatus();
            _current?.Refresh();
        }

        void UpdateStatus()
        {
            if (Game.Player == null) return;
            _gems.text = Game.Player.gems.ToString("N0");
            _gold.text = Game.Player.gold.ToString("N0");
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

            _sheet = sheet;
            _current = _screens[sheet];
            _content.Clear();
            _content.Add(_current.Build());
            UpdateStatus();

            foreach (var pair in _tabs)
                pair.Value.EnableInClassList("navtab--active",
                    pair.Key == sheet && !pair.Value.ClassListContains("navcta"));

            // The plate on 메인, the back arrow and the screen's name everywhere else — which is
            // the arrangement the reference uses between its lobby and everything inside it.
            var home = sheet == Sheet.Home;
            _plate?.EnableInClassList("hidden", !home);
            _navBack?.EnableInClassList("hidden", home);
            if (_screenTitle != null) _screenTitle.text = TitleOf(sheet);
        }

        static string TitleOf(Sheet sheet) => sheet switch
        {
            Sheet.Roster => "인사 명단",
            Sheet.Party => "편성",
            Sheet.Gacha => "모집",
            Sheet.Quests => "일일 업무",
            Sheet.Progress => "검토",
            Sheet.Story => "사내 메신저",
            Sheet.Album => "사원 앨범",
            Sheet.Codex => "오류 도감",
            Sheet.Chart => "통계",
            _ => "메인 전투",
        };

        /// <summary>Locked while a reveal is playing so a sheet change cannot strand the overlay.</summary>
        public void SetNavEnabled(bool enabled)
        {
            foreach (var pair in _tabs) pair.Value.SetEnabled(enabled);
        }

        public void SetStatus(string text) { if (_status != null) _status.text = text; }

        public void Rebuild() => Show(_sheet);

        /// <summary>
        /// 광고 보상. Every offer pays a flat amount — see AdService. The menu prints that rule at
        /// the foot rather than leaving a player to work out whether closing the app pays better,
        /// because the answer being "no, never" is the point of the whole design.
        /// </summary>
        void OpenAdMenu()
        {
            AudioService.Play("tap", 0.5f);
            var p = Game.Player;
            var pane = UiKit.Div("picker ad-menu");

            var head = UiKit.Div("prog-head", pane);
            UiKit.Text("광고 보상", "section-title", head);
            UiKit.Div("spacer", head);
            UiKit.Text($"오늘 남은 광고 {AdService.LeftToday(p)} / {GameData.Balance.adPerDay}회",
                "muted", head);

            foreach (var offer in AdService.Offers(p))
            {
                var row = UiKit.Div("arow", pane);
                var text = UiKit.Div("arow__text", row);
                UiKit.Text($"{offer.Def.name}　{offer.Value}", "arow__name", text);
                UiKit.Text($"{offer.Def.desc}　·　오늘 {offer.Left} / {offer.Def.perDay}회",
                    "arow__meta", text);

                var id = offer.Def.id;
                var watch = UiKit.Btn(offer.Can ? "광고 보기" : offer.Reason, "arow__claim", () =>
                {
                    var told = AdService.Grant(Game.Player, id);
                    if (told == null) return;
                    AudioService.Play("victory", 0.6f);
                    SetStatus(told);
                    Game.Touch();
                    CloseOverlay();
                    OpenAdMenu();
                }, row);
                watch.SetEnabled(offer.Can);
            }

            UiKit.Text("광고 1편 = 보상 1개 · 모든 보상은 고정 지급 (방치 배율 없음)", "muted", pane);

            // 보석 코드 — a field and a button, on the same sheet as the ads because both are
            // "get something without fighting for it" and a player looks for them in one place.
            var codeRow = UiKit.Div("arow", pane);
            var codeText = UiKit.Div("arow__text", codeRow);
            UiKit.Text("보석 코드", "arow__name", codeText);
            var field = new TextField { value = "" };
            field.AddToClassList("code-field");
            codeText.Add(field);

            UiKit.Btn("등록", "arow__claim", () =>
            {
                var r = CodeService.Redeem(Game.Player, field.value);
                if (!r.Ok) { SetStatus(r.Message); return; }
                AudioService.Play("victory", 0.6f);
                SetStatus($"{r.Message} · {CodeService.Paid(r)}");
                Game.Touch();
                CloseOverlay();
                OpenAdMenu();
            }, codeRow);

            UiKit.Btn("닫기", "btn btn--ghost", CloseOverlay, pane);
            OpenOverlay(pane);
        }

        public void OpenDetail(string heroId, string tab = null)
        {
            if (!string.IsNullOrEmpty(tab)) _detail.ShowTab(tab);
            Overlay.Clear();
            Overlay.RemoveFromClassList("hidden");
            Overlay.Add(_detail.Build(heroId, CloseOverlay));
        }

        /// <summary>Puts one panel on the dimmed overlay.</summary>
        public void OpenOverlay(VisualElement panel)
        {
            Overlay.Clear();
            Overlay.RemoveFromClassList("hidden");
            Overlay.Add(panel);
        }

        public void CloseOverlay()
        {
            Overlay.Clear();
            Overlay.AddToClassList("hidden");
            _current?.Refresh();
        }

    }
}
