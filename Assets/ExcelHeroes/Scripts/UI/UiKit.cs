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

        public static Button Btn(string text, string classes, Action onClick, VisualElement parent = null)
        {
            var b = new Button(() => onClick?.Invoke()) { text = text };
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
        public static VisualElement Card(HeroDef def, OwnedHero owned, Action onClick = null, string extraClasses = null)
        {
            var grade = GameData.Grade(def.grade);
            var card = Div("card " + (owned == null ? "card--locked " : "") + (extraClasses ?? ""));
            card.style.borderTopColor = card.style.borderBottomColor =
                card.style.borderLeftColor = card.style.borderRightColor = grade?.Color ?? Color.gray;

            var art = Div("card__art", card);
            SetArt(art, GameData.CardArt(def.id));

            var top = Div("card__top", card);
            var badge = Text(def.grade, "card__grade", top);
            badge.style.backgroundColor = grade?.Color ?? Color.gray;
            Text(RoleName(def.role), "card__role", top);

            var plate = Div("card__plate", card);
            Text(def.name, "card__name", plate);
            if (owned != null)
            {
                Text(Stars(owned.star), "card__stars", plate);
                var need = GachaService.PromoteCost(owned);
                if (need > 0) Text($"승급 {owned.copies}/{need}", "card__copies", plate);
            }
            else
            {
                Text("미보유", "card__stars muted", plate);
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
