using System.Collections;
using System.Linq;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 내 자리 — the screen the game opens on. One card stands there full-bleed, breathing and
    /// blinking, and says something to you; tapping them says something else. Everything else on the
    /// screen is small.
    ///
    /// This is the room the player comes back to, and it only works if the character feels present:
    /// the greeting changes as 호감도 rises, and at Lv5 they stop talking like an employee and start
    /// talking like someone who knows you.
    /// </summary>
    public class HomeScreen : IScreen
    {
        public string Title => "내 자리";

        readonly AppRoot _app;
        VisualElement _root;
        Label _speech;
        Coroutine _motion;
        OwnedHero _lead;

        public HomeScreen(AppRoot app) { _app = app; }

        public VisualElement Build()
        {
            _root = UiKit.Div("home");
            Refresh();
            return _root;
        }

        public void Refresh()
        {
            if (_root == null) return;
            if (_motion != null) { _app.StopCoroutine(_motion); _motion = null; }
            _root.Clear();

            var p = Game.Player;
            _lead = ResolveLead(p);

            if (_lead == null)
            {
                var empty = UiKit.Div("panel", _root);
                UiKit.Text("아직 아무도 없습니다", "section-title", empty);
                UiKit.Text("사원을 모집하면 여기서 기다립니다.", "muted", empty);
                UiKit.Btn("모집하러 가기", "btn btn--primary", () => _app.Show(AppRoot.Tab.Gacha), empty);
                return;
            }

            var def = GameData.Hero(_lead.id);

            // The card fills the screen; the UI sits on top of it rather than beside it.
            var art = UiKit.Div("home__art", _root);
            UiKit.SetArt(art, GameData.CardArt(def.id));

            var motion = UiKit.Div("home__art-motion", _root);
            var frames = GameData.CardMotion(def.id);
            if (frames.Count > 0) _motion = _app.StartCoroutine(Breathe(motion, frames));

            UiKit.Div("home__scrim", _root);

            var bubble = UiKit.Div("home__bubble", _root);
            UiKit.Text($"{def.name} · {def.nick}", "home__who", bubble);
            _speech = UiKit.Text(AffectionService.Greeting(_lead), "home__speech", bubble);
            bubble.RegisterCallback<ClickEvent>(_ => Poke(def));

            var bond = UiKit.Div("home__bond", bubble);
            UiKit.Text($"호감도 {_lead.affection}", "home__bond-level", bond);
            var track = UiKit.Div("home__bond-track", bond);
            var need = AffectionService.XpForNext(_lead.affection);
            var fill = UiKit.Div("home__bond-fill", track);
            fill.style.width = Length.Percent(need <= 0 ? 100f : Mathf.Clamp01(_lead.affectionXp / (float)need) * 100f);

            var strip = UiKit.Div("home__strip", _root);
            Stat(strip, "Phase", p.stage.ToString());
            Stat(strip, "도감", $"{p.owned.Count}/{GameData.Heroes.Count}");
            Stat(strip, "모집 포인트", p.sparkPoints.ToString("N0"));

            var actions = UiKit.Div("home__actions", _root);
            UiKit.Btn("출근하기", "btn btn--primary", () => _app.Show(AppRoot.Tab.Battle), actions);
            UiKit.Btn("모집", "btn", () => _app.Show(AppRoot.Tab.Gacha), actions);
            UiKit.Btn("대표 바꾸기", "btn btn--ghost", () => _app.Show(AppRoot.Tab.Party), actions);
        }

        static void Stat(VisualElement parent, string key, string value)
        {
            var cell = UiKit.Div("home__stat", parent);
            UiKit.Text(value, "home__stat-value", cell);
            UiKit.Text(key, "home__stat-key", cell);
        }

        /// <summary>
        /// Tapping the card cycles what they have to say — the entrance line, their ultimate shout,
        /// and once you know them well enough, the office secret and the private message.
        /// </summary>
        int _pokeIndex;
        void Poke(HeroDef def)
        {
            var extra = GameData.Affection(def.id);
            var lines = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(def.line)) lines.Add(def.line);
            if (!string.IsNullOrEmpty(def.ult)) lines.Add(def.ult);
            if (AffectionService.SecretUnlocked(_lead) && !string.IsNullOrEmpty(extra?.secret)) lines.Add(extra.secret);
            if (AffectionService.LineUnlocked(_lead) && !string.IsNullOrEmpty(extra?.line2)) lines.Add(extra.line2);
            if (lines.Count == 0) return;

            _pokeIndex = (_pokeIndex + 1) % lines.Count;
            _speech.text = lines[_pokeIndex];
        }

        /// <summary>Picks the card that greets the player: their chosen lead, else the strongest.</summary>
        static OwnedHero ResolveLead(PlayerState p)
        {
            var chosen = string.IsNullOrEmpty(p.leadHeroId) ? null : p.Find(p.leadHeroId);
            return chosen ?? p.PartyMembers().OrderByDescending(StatMath.Power).FirstOrDefault()
                          ?? p.owned.OrderByDescending(StatMath.Power).FirstOrDefault();
        }

        static IEnumerator Breathe(VisualElement layer, System.Collections.Generic.List<Sprite> frames)
        {
            var i = 0;
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(1.8f, 3.4f));
                UiKit.SetArt(layer, frames[i % frames.Count]);
                i++;
                layer.AddToClassList("home__art-motion--on");
                yield return new WaitForSeconds(1.2f);
                layer.RemoveFromClassList("home__art-motion--on");
            }
        }
    }
}
