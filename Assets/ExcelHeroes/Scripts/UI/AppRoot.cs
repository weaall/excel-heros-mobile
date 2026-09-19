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

            Bind("tabHome", Sheet.Home);
            Bind("tabRoster", Sheet.Roster);
            Bind("tabGacha", Sheet.Gacha);
            Bind("tabQuests", Sheet.Quests);
            Bind("tabStory", Sheet.Story);
            Bind("tabAlbum", Sheet.Album);
            Bind("tabCodex", Sheet.Codex);
            Bind("tabChart", Sheet.Chart);

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

            if (Onboarding.Needed(Game.Player)) new Onboarding(this).Show();

            void Bind(string name, Sheet sheet)
            {
                var button = root.Q<Button>(name);
                if (button == null) return;
                _tabNames[sheet] = button.text;
                button.clicked += () => { AudioService.Play("nav", 0.5f); Show(sheet); };
                _tabs[sheet] = button;
            }
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
            if (_current is BattleScreen battle) battle.Tick(Time.deltaTime);
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
                var b = new Button(() => captured.Click?.Invoke());
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
            if (_stealth != null)
            {
                _stealth.EnableInClassList("icon--bosskey", !on);
                _stealth.EnableInClassList("icon--bosskey-on", on);
            }

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
