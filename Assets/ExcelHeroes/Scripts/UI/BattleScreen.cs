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
        public string Title => "출근";

        readonly AppRoot _app;
        VisualElement _root, _stage, _exBar, _resultView;
        Label _waveLabel;
        Button _autoButton;
        BattleSim _sim;

        readonly Dictionary<Combatant, VisualElement> _views = new();
        readonly Dictionary<string, (VisualElement button, VisualElement charge)> _exButtons = new();
        readonly List<(VisualElement el, float life)> _floaters = new();

        public BattleScreen(AppRoot app) { _app = app; }

        public VisualElement Build()
        {
            _root = UiKit.Div("battle");

            var hud = UiKit.Div("battle__hud", _root);
            _waveLabel = UiKit.Text("", "battle__wave", hud);
            _autoButton = UiKit.Btn("자동 스킬 OFF", "btn btn--ghost", ToggleAuto, hud);

            _stage = UiKit.Div("battle__stage", _root);
            _exBar = UiKit.Div("ex-bar", _root);

            Start();
            return _root;
        }

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
                UiKit.Btn("모집하러 가기", "btn btn--primary", () => _app.Show(AppRoot.Tab.Gacha), empty);
                return;
            }

            _sim = new BattleSim(Game.Player, Game.Player.stage);
            _autoButton.text = "자동 스킬 OFF";

            foreach (var h in _sim.Heroes) AddFighterView(h);
            foreach (var h in _sim.Heroes) AddExButton(h);
        }

        void AddFighterView(Combatant c)
        {
            var el = UiKit.Div("fighter " + (c.side == Side.Hero ? "fighter--hero" : "fighter--monster"), _stage);
            var bodyEl = UiKit.Div("fighter__body", el);
            if (c.side == Side.Hero) UiKit.SetArt(bodyEl, GameData.CardArt(c.heroId));
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
                ? (_sim.Won ? "업무 완료" : "업무 실패")
                : $"Phase {_sim.Stage} · 웨이브 {_sim.Wave}/{_sim.WaveCount}";

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
                        break;
                    case EventKind.Heal:
                        Float(e.target, "+" + e.amount.ToString("N0"), "floater floater--heal");
                        break;
                    case EventKind.Skill:
                        Float(e.actor, e.text, "floater floater--skill");
                        break;
                    case EventKind.Death:
                        if (_views.TryGetValue(e.target, out var dead)) dead.AddToClassList("fighter--dead");
                        break;
                }
            }
        }

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
                el.style.left = t * (width - 120f);
                // Slight vertical stagger so a five-stack never hides itself behind one silhouette.
                var lane = c.side == Side.Hero ? _sim.Heroes.IndexOf(c) : _sim.Monsters.IndexOf(c);
                el.style.top = height * 0.42f + Mathf.Max(0, lane) * 34f;

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
                pair.button.EnableInClassList("ex-button--ready", h.SkillReady);
                pair.button.EnableInClassList("ex-button--spent", !h.Alive);
            }
        }

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
            _resultView = UiKit.Div("result", _root);
            var title = UiKit.Text(_sim.Won ? "업무 완료" : "업무 실패",
                "result__title " + (_sim.Won ? "result__title--win" : "result__title--lose"), _resultView);

            if (_sim.Won)
            {
                Game.Player.gold += _sim.GoldEarned;
                Game.Player.gems += 5;
                if (_sim.Stage >= Game.Player.stage) Game.Player.stage++;
                UiKit.Text($"골드 +{_sim.GoldEarned:N0} · 보석 +5", null, _resultView);
                UiKit.Text($"다음 구간: Phase {Game.Player.stage}", "muted", _resultView);
                Game.Touch();
            }
            else
            {
                UiKit.Text("파티를 보강하고 다시 도전해 보세요.", "muted", _resultView);
            }

            UiKit.Btn("다시", "btn btn--primary", Start, _resultView);
            UiKit.Btn("모집하러 가기", "btn", () => _app.Show(AppRoot.Tab.Gacha), _resultView);
        }
    }
}
