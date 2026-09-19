using System.Collections.Generic;
using ExcelHeroes.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    public interface IScreen
    {
        string Title { get; }
        VisualElement Build();
        void Refresh();
    }

    /// <summary>
    /// The single MonoBehaviour that runs the game: loads the database and save, mounts AppShell.uxml
    /// into the UIDocument, and swaps screens under the shared top bar and bottom nav.
    ///
    /// Screens are rebuilt on entry rather than kept warm — the roster is 55 cards and the whole tree
    /// costs less to construct than it does to keep in sync, and rebuilding removes a whole class of
    /// stale-UI bugs.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class AppRoot : MonoBehaviour
    {
        public enum Tab { Home, Gacha, Roster, Party, Battle, Daily, Story }

        public VisualElement Overlay { get; private set; }

        UIDocument _doc;
        VisualElement _content, _navbar;
        Label _title, _gems, _gold, _dailyBadge;
        readonly Dictionary<Tab, IScreen> _screens = new();
        readonly Dictionary<Tab, Button> _navButtons = new();
        Tab _tab = Tab.Gacha;
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
            _navbar = root.Q<VisualElement>("navbar");
            Overlay = root.Q<VisualElement>("overlay");
            _title = root.Q<Label>("screenTitle");
            _gems = root.Q<Label>("gemValue");
            _gold = root.Q<Label>("goldValue");

            _screens[Tab.Home] = new HomeScreen(this);
            _screens[Tab.Gacha] = new GachaScreen(this);
            _screens[Tab.Roster] = new RosterScreen(this);
            _screens[Tab.Party] = new PartyScreen(this);
            _screens[Tab.Battle] = new BattleScreen(this);
            _screens[Tab.Daily] = new DailyScreen(this);
            _screens[Tab.Story] = new StoryScreen(this);
            _detail = new HeroDetail(this);

            Bind("navHome", Tab.Home);
            Bind("navGacha", Tab.Gacha);
            Bind("navRoster", Tab.Roster);
            Bind("navParty", Tab.Party);
            Bind("navBattle", Tab.Battle);
            Bind("navDaily", Tab.Daily);
            Bind("navStory", Tab.Story);
            _dailyBadge = root.Q<Label>("dailyBadge");

            Game.Changed += OnGameChanged;
            Show(Tab.Home);

            // A brand new player lands on an empty home screen, which reads as a broken app rather
            // than a game waiting to start. Give them the premise and point at the banner.
            if (Onboarding.Needed(Game.Player)) new Onboarding(this).Show();

            void Bind(string name, Tab tab)
            {
                var button = root.Q<Button>(name);
                if (button == null) return;
                button.clicked += () => { AudioService.Play("nav", 0.5f); Show(tab); };
                _navButtons[tab] = button;
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
            UpdateCurrencies();
            _current?.Refresh();
        }

        void UpdateCurrencies()
        {
            if (Game.Player == null) return;
            _gems.text = Game.Player.gems.ToString("N0");
            _gold.text = Game.Player.gold.ToString("N0");
            UpdateDailyBadge();
        }

        /// <summary>
        /// The count of rewards waiting on the daily tab. Without it the whole economy is invisible
        /// — a player has no way to know they are leaving gems on the table.
        /// </summary>
        void UpdateDailyBadge()
        {
            if (_dailyBadge == null || Game.Player == null) return;
            var n = Core.QuestService.ReadyCount(Game.Player);
            _dailyBadge.text = n.ToString();
            _dailyBadge.EnableInClassList("hidden", n == 0);
        }

        public void Show(Tab tab)
        {
            _tab = tab;
            _current = _screens[tab];
            _content.Clear();
            _content.Add(_current.Build());
            _title.text = _current.Title;
            UpdateCurrencies();

            foreach (var (key, button) in _navButtons)
                button.EnableInClassList("nav-button--active", key == tab);
        }

        /// <summary>Locked while a reveal is playing so a tab change cannot strand the overlay.</summary>
        public void SetNavEnabled(bool enabled) => _navbar.SetEnabled(enabled);

        public void OpenDetail(string heroId)
        {
            Overlay.Clear();
            Overlay.RemoveFromClassList("hidden");
            Overlay.Add(_detail.Build(heroId, () =>
            {
                Overlay.Clear();
                Overlay.AddToClassList("hidden");
                _current?.Refresh();
            }));
        }
    }
}
