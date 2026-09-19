using System;
using System.Collections.Generic;
using ExcelHeroes.Core;
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
        public enum Sheet { Home, Roster, Gacha, Quests, Story, Album, Codex, Chart }

        public VisualElement Overlay { get; private set; }

        UIDocument _doc;
        VisualElement _content, _ribbon, _gutter;
        Button _ribbonToggle;
        Label _nameBox, _formula, _gems, _gold, _dailyBadge, _status;
        Button _stealth;
        readonly Dictionary<Sheet, IScreen> _screens = new();
        readonly Dictionary<Sheet, Button> _tabs = new();
        readonly Dictionary<Sheet, string> _tabNames = new();
        Sheet _sheet = Sheet.Home;
        IScreen _current;
        HeroDetail _detail;
        StealthSheet _stealthSheet;

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
            _ribbon = root.Q<VisualElement>("ribbon");
            Overlay = root.Q<VisualElement>("overlay");
            _nameBox = root.Q<Label>("nameBox");
            _formula = root.Q<Label>("formula");
            _gems = root.Q<Label>("gemValue");
            _gold = root.Q<Label>("goldValue");
            _status = root.Q<Label>("statusText");
            _dailyBadge = root.Q<Label>("dailyBadge");

            _screens[Sheet.Home] = new BattleScreen(this);
            _screens[Sheet.Roster] = new RosterScreen(this);
            _screens[Sheet.Gacha] = new GachaScreen(this);
            _screens[Sheet.Quests] = new DailyScreen(this);
            _screens[Sheet.Story] = new StoryScreen(this);
            _screens[Sheet.Album] = new AlbumScreen(this);
            _screens[Sheet.Codex] = new CodexScreen(this);
            _screens[Sheet.Chart] = new ChartScreen(this);
            _detail = new HeroDetail(this);
            _stealthSheet = new StealthSheet(this);

            // Four tabs along the bottom, because four short words is what fits on a phone without
            // the strip scrolling. The other four sheets live behind the sheet-list button, which
            // is where Excel for Android keeps sheets that do not fit either.
            Bind("tabHome", Sheet.Home);
            Bind("tabRoster", Sheet.Roster);
            Bind("tabGacha", Sheet.Gacha);
            Bind("tabQuests", Sheet.Quests);

            var sheetList = root.Q<Button>("sheetListBtn");
            if (sheetList != null) sheetList.clicked += OpenSheetList;

            // The chrome's glyphs come from the Material Symbols font rather than from PNGs I
            // drew or from whatever Unity's default font happened to have — several of the old
            // ones rendered as an empty box, which is how the disguise button went missing.
            Glyph("backBtn", Icons.Back);
            Glyph("searchBtn", Icons.Search);
            Glyph("undoBtn", Icons.Undo);
            Glyph("overflowBtn", Icons.Overflow);
            Glyph("sheetListBtn", Icons.Sheets);
            Glyph("addSheetBtn", Icons.Add);
            Glyph("formulaExpand", Icons.Expand);

            // Keeps the chrome clear of the notch and the gesture bar.
            (gameObject.GetComponent<SafeArea>() ?? gameObject.AddComponent<SafeArea>()).Bind(root);

            _stealth = root.Q<Button>("stealthToggle");
            if (_stealth != null) _stealth.clicked += ToggleStealth;

            // On a phone the ribbon is summoned, not resident: 홈 ▲ slides it up over the grid.
            _gutter = root.Q<VisualElement>("rowGutter");

            // Excel's tab strip slides without showing a scrollbar. The USS selector for the
            // built-in scroller does not survive the ScrollView rebuilding its parts, so it is set
            // on the control itself.
            var tabScroll = root.Q<ScrollView>("sheetTabsScroll");
            if (tabScroll != null)
            {
                tabScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                tabScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            }
            _ribbonToggle = root.Q<Button>("ribbonToggle");
            if (_ribbonToggle != null) _ribbonToggle.clicked += ToggleRibbon;

            Game.Changed += OnGameChanged;
            ApplyStealth();
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
            if (Onboarding.Needed(Game.Player)) new Onboarding(this).Show();
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
            if (_dailyBadge == null) return;
            var n = QuestService.ReadyCount(Game.Player);
            _dailyBadge.text = n.ToString();
            _dailyBadge.EnableInClassList("hidden", n == 0);
        }

        public void Show(Sheet sheet)
        {
            _sheet = sheet;
            _current = Stealth ? (IScreen)_stealthSheet : _screens[sheet];
            _content.Clear();
            _content.Add(_current.Build());
            _nameBox.text = _current.Cell;
            _formula.text = _current.Formula;
            BuildRibbon();
            BuildGutter();
            UpdateStatus();

            foreach (var pair in _tabs)
                pair.Value.EnableInClassList("sheet-tab--active", pair.Key == sheet);
        }

        /// <summary>
        /// Row numbers down the left edge. They are decoration, but they are the decoration that
        /// does the most work: with the ribbon gone, the column letters and these numbers are what
        /// someone glancing over your shoulder actually reads.
        /// </summary>
        void BuildGutter()
        {
            if (_gutter == null) return;
            var height = _gutter.resolvedStyle.height;
            if (float.IsNaN(height) || height <= 1f)
            {
                // First build of the session: the gutter has no measured height yet, so wait for
                // the layout pass and come back.
                _gutter.RegisterCallback<GeometryChangedEvent>(OnGutterMeasured);
                return;
            }
            FillGutter(height);
        }

        void OnGutterMeasured(GeometryChangedEvent e)
        {
            _gutter.UnregisterCallback<GeometryChangedEvent>(OnGutterMeasured);
            FillGutter(e.newRect.height);
        }

        void FillGutter(float height)
        {
            const float rowHeight = 96f;
            _gutter.Clear();
            var rows = Mathf.Max(1, Mathf.FloorToInt(height / rowHeight));
            for (var i = 1; i <= rows; i++) UiKit.Text(i.ToString(), "ws-row-num", _gutter);
        }

        void ToggleRibbon()
        {
            var open = _ribbon.ClassListContains("hidden");
            _ribbon.EnableInClassList("hidden", !open);
            _ribbonToggle.text = open ? "홈 ▼" : "홈 ▲";
        }

        void BuildRibbon()
        {
            _ribbon.Clear();
            foreach (var item in _current.Ribbon())
            {
                if (item.Separator) { UiKit.Div("rb-sep", _ribbon); continue; }
                var captured = item;
                var b = new Button(() => { AudioService.Play("tap", 0.5f); captured.Click?.Invoke(); });
                b.AddToClassList("rb-btn");
                if (!string.IsNullOrEmpty(item.Icon)) UiKit.Text(item.Icon, "rb-btn__icon", b);
                UiKit.Text(item.Label, null, b);
                if (item.Sub != null) UiKit.Text(item.Sub, "rb-btn__sub", b);
                _ribbon.Add(b);
            }
        }

        /// <summary>Locked while a reveal is playing so a sheet change cannot strand the overlay.</summary>
        public void SetNavEnabled(bool enabled)
        {
            foreach (var pair in _tabs) pair.Value.SetEnabled(enabled);
        }

        public void SetStatus(string text) { if (_status != null) _status.text = text; }

        public void Rebuild() => Show(_sheet);

        /// <summary>
        /// The sheet list. Excel for Android puts every sheet behind this button; here it is also
        /// where the four sheets that did not fit on the tab strip live, so nothing is unreachable.
        /// </summary>
        static readonly (Sheet Sheet, string Name)[] MoreSheets =
        {
            (Sheet.Home, "메인_전투"), (Sheet.Roster, "인사_명단"),
            (Sheet.Gacha, "데이터_가져오기"), (Sheet.Quests, "일일_업무"),
            (Sheet.Story, "사내_메신저"), (Sheet.Album, "사원_앨범"),
            (Sheet.Codex, "오류_도감"), (Sheet.Chart, "통계_차트"),
        };

        void OpenSheetList()
        {
            AudioService.Play("tap", 0.5f);
            var pane = UiKit.Div("sheet-list");
            UiKit.Text("시트", "sheet-list__title", pane);

            foreach (var (sheet, name) in MoreSheets)
            {
                var target = sheet;
                var row = UiKit.Btn(Stealth ? StealthLabels.Tab(target) : name, "sheet-list__row",
                    () => { CloseOverlay(); Show(target); }, pane);
                row.EnableInClassList("sheet-list__row--active", target == _sheet);
            }

            UiKit.Btn("닫기", "btn btn--ghost", CloseOverlay, pane);
            OpenOverlay(pane);
        }

        public void OpenDetail(string heroId)
        {
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

        // ------------------------------------------------------------------- stealth

        /// <summary>
        /// The desktop build hid the game behind Esc. A phone has no Esc, and the point of the
        /// disguise is that you can reach it without looking, so it is a fixed title-bar button.
        /// </summary>
        void ToggleStealth() => SetStealth(!Stealth);

        public void SetStealth(bool on)
        {
            Game.Player.stealth = on;
            Game.Touch();
            ApplyStealth();
            Show(_sheet);   // sheets carry their own vocabulary, so rebuild rather than relabel
        }

        void ApplyStealth()
        {
            var on = Stealth;
            _doc.rootVisualElement.EnableInClassList("stealth", on);
            // An eye, open or shut. The disguise is literally "do not look at this".
            if (_stealth != null) _stealth.text = on ? Icons.BossKeyOn : Icons.BossKey;

            foreach (var pair in _tabs)
                pair.Value.text = on ? StealthLabels.Tab(pair.Key) : _tabNames[pair.Key];

            // 위장 중에는 통화를 라벨까지 감춘다. 숫자만 바꾸면 ◈ 와 ₩ 아이콘이 남고,
            // 스프레드시트 상태 표시줄에 그런 기호가 있을 이유가 없다.
            var root = _doc.rootVisualElement;
            foreach (var name in new[] { "gemChip", "goldChip" })
                root.Q<VisualElement>(name)?.EnableInClassList("hidden", on);
        }

        public bool Stealth => Game.Player != null && Game.Player.stealth;
    }
}
