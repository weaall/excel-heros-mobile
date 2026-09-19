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
        VisualElement _content;
        Label _gems, _gold, _dailyBadge, _status;
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

            _screens[Sheet.Home] = new BattleScreen(this);
            _screens[Sheet.Roster] = new RosterScreen(this);
            _screens[Sheet.Gacha] = new GachaScreen(this);
            _screens[Sheet.Quests] = new DailyScreen(this);
            _screens[Sheet.Story] = new StoryScreen(this);
            _screens[Sheet.Album] = new AlbumScreen(this);
            _screens[Sheet.Codex] = new CodexScreen(this);
            _screens[Sheet.Chart] = new ChartScreen(this);
            _detail = new HeroDetail(this);

            // Four tabs along the bottom, because four short words is what fits on a phone without
            // the strip scrolling. The other four sheets live behind the sheet-list button, which
            // is where Excel for Android keeps sheets that do not fit either.
            Bind("tabHome", Sheet.Home);
            Bind("tabRoster", Sheet.Roster);
            Bind("tabGacha", Sheet.Gacha);
            Bind("tabQuests", Sheet.Quests);

            var sheetList = root.Q<Button>("sheetListBtn");
            if (sheetList != null) sheetList.clicked += OpenSheetList;

            // Glyphs come from the Material Symbols font rather than from PNGs, so an icon that
            // has to be white here and grey there is one character rather than two images.
            Glyph("overflowBtn", Icons.Overflow);
            Glyph("goldIcon", Icons.Gold);
            Glyph("gemIcon", Icons.Gem);
            Glyph("tabHomeIcon", Icons.Battle);
            Glyph("tabRosterIcon", Icons.Roster);
            Glyph("tabGachaIcon", Icons.Gacha);
            Glyph("tabQuestsIcon", Icons.Tasks);
            Glyph("moreIcon", Icons.Sheets);

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
            _current = _screens[sheet];
            _content.Clear();
            _content.Add(_current.Build());
            UpdateStatus();

            foreach (var pair in _tabs)
                pair.Value.EnableInClassList("rail__tab--active", pair.Key == sheet);
        }

        /// <summary>Locked while a reveal is playing so a sheet change cannot strand the overlay.</summary>
        public void SetNavEnabled(bool enabled)
        {
            foreach (var pair in _tabs) pair.Value.SetEnabled(enabled);
        }

        public void SetStatus(string text) { if (_status != null) _status.text = text; }

        public void Rebuild() => Show(_sheet);

        /// <summary>
        /// The screens the rail has no room for. Four tabs is what fits down the side at a size a
        /// thumb can find without looking; the rest live behind 더보기.
        /// </summary>
        static readonly (Sheet Sheet, string Name)[] MoreSheets =
        {
            (Sheet.Home, "메인 전투"), (Sheet.Roster, "인사 명단"),
            (Sheet.Gacha, "모집"), (Sheet.Quests, "일일 업무"),
            (Sheet.Story, "사내 메신저"), (Sheet.Album, "사원 앨범"),
            (Sheet.Codex, "오류 도감"), (Sheet.Chart, "통계"),
        };

        void OpenSheetList()
        {
            AudioService.Play("tap", 0.5f);
            var pane = UiKit.Div("sheet-list");
            UiKit.Text("전체 메뉴", "sheet-list__title", pane);

            foreach (var (sheet, name) in MoreSheets)
            {
                var target = sheet;
                var row = UiKit.Btn(name, "sheet-list__row",
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

    }
}
