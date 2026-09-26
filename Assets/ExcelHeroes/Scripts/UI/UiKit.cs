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
        /// The one modal window, as on the reference sheet: a painted frame, a head band with the
        /// title and a ✕, and a body the caller fills. Returns the body; `panel` is what goes on
        /// the overlay.
        ///
        /// Every popup used to build its own white box — four different ones — so none of them
        /// looked like the same object, and none had a head. Build them here and they all do.
        /// </summary>
        public static VisualElement Modal(string title, Action onClose, out VisualElement panel, string classes = null)
        {
            panel = Div("modal " + (classes ?? ""));
            ModalFrame.Frame(panel);

            var head = Div("modal__head", panel);
            ModalFrame.Head(head);
            Text(title, "modal__title", head);
            if (onClose != null)
            {
                var close = new Button(() => { AudioService.Play("tap", 0.5f); onClose(); }) { text = Icons.Close };
                close.AddToClassList("icon");
                close.AddToClassList("modal__close");
                head.Add(close);
            }

            return Div("modal__body", panel);
        }

        /// <summary>The navy section label inside a modal ("REWARDS RECEIVED").</summary>
        public static VisualElement Ribbon(string text, VisualElement parent = null)
        {
            // A Div with a Label child, not a Label: a TextElement paints its own text BEFORE
            // its generateVisualContent, so a painted Label covers its own words.
            var r = Div("ribbon", parent);
            ModalFrame.Ribbon(r);
            Text(text, "ribbon__label", r).pickingMode = PickingMode.Ignore;
            return r;
        }

        /// <summary>A reward tile: a glyph and a count, on a white rounded square.</summary>
        public static VisualElement RewardTile(string glyph, string count, string glyphClasses = null, VisualElement parent = null, Color? glow = null)
        {
            var tile = Div("rtile", parent);
            ModalFrame.Tile(tile, glow);
            Text(glyph, "rtile__glyph " + (glyphClasses ?? ""), tile);
            Text(count, "rtile__count", tile);
            return tile;
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
            var gradeColor = grade?.Color ?? Color.gray;
            var card = Div("card " + (owned == null ? "card--locked " : "") + (extraClasses ?? ""));

            var art = Div("card__art", card);
            // An owned hero wears what they have equipped; a locked one has nothing equipped
            // and falls straight through to the base art.
            SetArt(art, owned == null ? GameData.CardArt(def.id) : GameData.WornCardArt(def.id));

            // A card you do not own is DARKENED by a scrim over the art. Opacity composites
            // against the white card behind and bleaches towards white instead.
            if (owned == null) Div("card__scrim", art);

            GradeBadge(def.grade, gradeColor, "card__grade", card);
            RoleBadge(def.role, "card__role", card);
            if (owned != null)
            {
                // Filled stars only, in yellow, as the sheet draws them — five hollow ☆ on every
                // ★1 card was noise that said nothing.
                if (owned.star > 0) Text(new string('★', Math.Clamp(owned.star, 0, 5)), "card__stars", card);

                // 즐겨찾기 has to be legible from the grid: 레벨 회수 skips this card.
                if (Game.Player != null && Game.Player.favorites.Contains(def.id))
                    Text("보존", "card__keep", card);
            }

            // The navy band across the foot: name and level on one line, the 승급 count under it.
            var plate = Div("card__plate", card);
            var line = Div("card__line", plate);
            Text(def.name, "card__name", line);
            if (owned != null) Text($"Lv.{owned.level}", "card__level", line);
            if (owned == null) Text("미보유", "card__sub muted", plate);
            else
            {
                var need = GachaService.PromoteCost(owned);
                Text(need > 0 ? $"승급 {owned.copies}/{need}" : "최대 ★", "card__sub", plate);
            }

            // The frame goes on LAST so it paints over the art's edge. UI Toolkit has no
            // z-index; build order decides.
            CardFrame(gradeColor, card);

            if (onClick != null) card.RegisterCallback<ClickEvent>(_ => onClick());
            return card;
        }

        /// <summary>The grade as a small slanted plate in the grade's colour.</summary>
        public static VisualElement GradeBadge(string gradeId, Color gradeColor, string classes, VisualElement parent)
        {
            var badge = Div(classes, parent);
            ModalFrame.Painted(badge, (ctx, r) =>
            {
                var poly = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height), 4f);
                UiPaint.Shadow(ctx, poly, new Vector2(0f, 2f), UiPaint.C(10, 20, 40, 0.35f), 4f);
                UiPaint.Fill(ctx, poly, Color.white);
                UiPaint.Fill(ctx, UiPaint.Offset(poly, -2.5f),
                             UiPaint.Vertical(Color.Lerp(gradeColor, Color.white, 0.25f), gradeColor, r.yMin, r.yMax));
            });
            Text(gradeId, "card__grade-label", badge).pickingMode = PickingMode.Ignore;
            return badge;
        }

        /// <summary>
        /// The role as a round blue badge with its glyph — the sheet's element badge, top-right,
        /// the one place a player looks for "what does this one do".
        /// </summary>
        public static VisualElement RoleBadge(string roleId, string classes, VisualElement parent)
        {
            var role = Div(classes, parent);
            ModalFrame.Painted(role, (ctx, r) =>
            {
                var c = r.center;
                var disc = UiPaint.Ellipse(c, r.width * 0.5f, r.height * 0.5f);
                UiPaint.Shadow(ctx, disc, new Vector2(0f, 2f), UiPaint.C(10, 20, 40, 0.3f), 4f);
                UiPaint.Fill(ctx, disc, Color.white);
                UiPaint.Fill(ctx, UiPaint.Ellipse(c, r.width * 0.5f - 3f, r.height * 0.5f - 3f),
                             UiPaint.Vertical(UiPaint.C(82, 180, 245), UiPaint.C(28, 120, 210), r.yMin, r.yMax));
            });
            Text(RoleGlyph(roleId), "icon card__role-glyph", role).pickingMode = PickingMode.Ignore;
            return role;
        }

        /// <summary>
        /// A white border with a thin line of the grade's colour inside it, painted over a
        /// portrait. Add it last, so it lands on top of the art.
        /// </summary>
        public static VisualElement CardFrame(Color gradeColor, VisualElement parent, float radius = 16f)
        {
            var frame = Div("card__frame", parent);
            frame.pickingMode = PickingMode.Ignore;
            ModalFrame.Painted(frame, (ctx, r) =>
            {
                var poly = UiPaint.RoundRect(r, radius, 6);
                UiPaint.Stroke(ctx, poly, Color.white, 5f);
                UiPaint.Stroke(ctx, UiPaint.Offset(poly, -5f), UiPaint.WithAlpha(gradeColor, 0.9f), 2f);
            });
            return frame;
        }

        /// <summary>The role's glyph for its badge.</summary>
        public static string RoleGlyph(string roleId) => roleId switch
        {
            "tank" => Icons.Shield,
            "melee" => Icons.Battle,
            "ranged" => Icons.Bolt,
            "healer" => Icons.Gem,
            _ => Icons.Star,
        };

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
