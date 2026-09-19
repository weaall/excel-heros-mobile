using System;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// Small builders shared by the screens. Layout and colour live in App.uss; the only inline
    /// styling here is for values USS cannot know at author time — a card's artwork and its rarity
    /// colour, which come from the data files.
    /// </summary>
    public static class UiKit
    {
        public static VisualElement Div(string classes = null, VisualElement parent = null)
        {
            var e = new VisualElement();
            AddClasses(e, classes);
            parent?.Add(e);
            return e;
        }

        public static Label Text(string text, string classes = null, VisualElement parent = null)
        {
            var l = new Label(text);
            AddClasses(l, classes);
            parent?.Add(l);
            return l;
        }

        /// <summary>
        /// Every button in the game is built here, which is why the click sound lives here too.
        /// Screens used to play it themselves, so whether a button made a noise depended on whether
        /// whoever wrote that screen remembered — most did not, and the only things that clicked
        /// were the sheet tabs.
        /// </summary>
        public static Button Btn(string text, string classes, Action onClick, VisualElement parent = null)
        {
            var b = new Button(() => { AudioService.Play("tap", 0.5f); onClick?.Invoke(); }) { text = text };
            AddClasses(b, classes);
            parent?.Add(b);
            return b;
        }

        public static ScrollView Scroll(string classes = null, VisualElement parent = null)
        {
            var s = new ScrollView(ScrollViewMode.Vertical);
            AddClasses(s, classes);
            s.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            parent?.Add(s);
            return s;
        }

        public static void AddClasses(VisualElement e, string classes)
        {
            if (string.IsNullOrEmpty(classes)) return;
            foreach (var c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) e.AddToClassList(c);
        }

        public static void SetArt(VisualElement e, Sprite sprite)
        {
            if (sprite != null) e.style.backgroundImage = new StyleBackground(sprite);
        }

        public static string Stars(int star) => new string('★', Math.Clamp(star, 0, 5)).PadRight(5, '☆');

        public static string RoleName(string roleId) => GameData.Role(roleId)?.name ?? roleId;

        /// <summary>
        /// A roster/party card. `owned` may be null, which renders the silhouette used for cards the
        /// player has not pulled yet.
        /// </summary>
        /// <summary>
        /// A roster card, arranged the way Blue Archive arranges its own.
        ///
        /// Checked against a real screenshot of its battle HUD, where the EX skill cards are the
        /// same object in miniature: a bust crop of the portrait, a number badge in the top corner,
        /// and nothing written across the picture itself. What this build had instead was the whole
        /// 2:3 illustration squashed into a square — so every card was a torso, the faces were cut
        /// off at the top, and the metadata sat in rows of text underneath, which is why three of
        /// them filled the screen.
        ///
        /// So: the crop is anchored to the top, because the face is the part that identifies a
        /// card; the grade and role become corner badges over the art; and the stars go on a plate
        /// at the foot of the portrait rather than on a line of their own.
        /// </summary>
        public static VisualElement Card(HeroDef def, OwnedHero owned, Action onClick = null, string extraClasses = null)
        {
            var grade = GameData.Grade(def.grade);
            var card = Div("card " + (owned == null ? "card--locked " : "") + (extraClasses ?? ""));
            card.style.borderTopColor = card.style.borderBottomColor =
                card.style.borderLeftColor = card.style.borderRightColor = grade?.Color ?? Color.gray;

            var art = Div("card__art", card);
            // An owned hero wears what they have equipped; a locked one has nothing equipped
            // and falls straight through to the base art.
            SetArt(art, owned == null ? GameData.CardArt(def.id) : GameData.WornCardArt(def.id));

            // Badges sit on the art, in the corners, as they do in the source.
            var badge = Text(def.grade, "card__grade", art);
            badge.style.backgroundColor = grade?.Color ?? Color.gray;
            Text(RoleName(def.role), "card__role", art);

            if (owned != null)
            {
                var stars = Text(Stars(owned.star), "card__stars", art);
                stars.style.color = grade?.Color ?? Color.white;
                Text($"Lv {owned.level}", "card__level", art);
            }

            var plate = Div("card__plate", card);
            Text(def.name, "card__name", plate);
            if (owned == null) Text("미보유", "card__sub muted", plate);
            else
            {
                var need = GachaService.PromoteCost(owned);
                Text(need > 0 ? $"승급 {owned.copies}/{need}" : "최대 ★", "card__sub", plate);
            }

            if (onClick != null) card.RegisterCallback<ClickEvent>(_ => onClick());
            return card;
        }

        public static VisualElement StatRow(string key, string value, VisualElement parent = null)
        {
            var row = Div("stat-row", parent);
            Text(key, "stat-row__key", row);
            Text(value, "stat-row__value", row);
            return row;
        }
    }
}
