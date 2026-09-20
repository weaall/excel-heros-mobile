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
            // The plate is drawn, not styled: the reference's buttons are parallelograms and USS
            // has no skew. See SkewPlate for why this is a mesh rather than a 9-sliced picture.
            var kind = SkewPlate.KindFor(classes);
            if (kind.HasValue) SkewPlate.Apply(b, kind.Value);
            parent?.Add(b);
            return b;
        }

        /// <summary>
        /// Changes a button's label. A plated button keeps its text in a child, so `b.text = x`
        /// sets a string nothing draws — this is the one way that works for both kinds.
        /// </summary>
        public static void SetBtnText(Button b, string text) => SkewPlate.SetText(b, text);

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

            // A card you do not own is DARKENED, by a scrim laid over the art.
            //
            // It used to be `opacity: 0.55` on the art, which composites against whatever is
            // behind — and behind was a white card, so every unowned card bleached TOWARDS WHITE
            // and two thirds of the grid read as blank rectangles. Putting a dark colour on the
            // card and keeping the opacity did not fix it either; measured on a capture the art
            // came back at 207 grey when the arithmetic said 143, so the blend is not the simple
            // lerp it looks like. A scrim needs no theory about compositing: it is a dark rectangle
            // on top of the picture, and it darkens.
            if (owned == null) Div("card__scrim", art);

            // Badges sit on the art, in the corners, as they do in the source.
            var badge = Text(def.grade, "card__grade", art);
            badge.style.backgroundColor = grade?.Color ?? Color.gray;
            Text(RoleName(def.role), "card__role", art);

            if (owned != null)
            {
                var stars = Text(Stars(owned.star), "card__stars", art);
                stars.style.color = grade?.Color ?? Color.white;
                Text($"Lv {owned.level}", "card__level", art);

                // 즐겨찾기 has to be legible from the grid. Its only effect is that 레벨 회수
                // skips this card, and a sweep you cannot predict from the screen you press it
                // on is a sweep nobody presses twice. "보존" rather than a star, because ★ on a
                // card already means 승급.
                if (Game.Player != null && Game.Player.favorites.Contains(def.id))
                    Text("보존", "card__keep", art);
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

        /// <summary>
        /// A stat as a half-width cell, for a two-column grid. Four facts in two lines rather than
        /// four is the difference between a panel that fits its column and one that clips.
        /// </summary>
        public static VisualElement StatCell(string key, string value, VisualElement parent = null)
        {
            var cell = Div("statcell", parent);
            Text(key, "statcell__key", cell);
            Text(value, "statcell__val", cell);
            return cell;
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
