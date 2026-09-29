using System.Collections.Generic;
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
                // a round white button with the navy ✕ (the kit's modal)
                var close = new Button(() => { AudioService.Play("tap", 0.5f); onClose(); }) { text = "" };
                close.AddToClassList("modal__close");
                ModalFrame.Painted(close, (ctx, r) =>
                {
                    var c = r.center; var rad = Mathf.Min(r.width, r.height) * 0.5f;   // a plain navy ✕, as the reference
                    var k = rad * 0.38f; var t = rad * 0.11f;
                    foreach (var sgn in new[] { 1f, -1f })
                    {
                        var d = new Vector2(k, sgn * k); var n = new Vector2(-d.y, d.x).normalized * t;
                        UiPaint.Fill(ctx, new System.Collections.Generic.List<Vector2> { c - d - n, c + d - n, c + d + n, c - d + n }, UiPaint.C(30, 43, 69));
                    }
                });
                Juice.Press(close);
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

        public enum Crop { Face, Bust, Cut }

        static (float zoom, float focus) CropOf(Crop c) => c switch
        {
            Crop.Face => (2.9f, 0.145f),
            Crop.Bust => (1.75f, 0.2f),
            _ => (1.35f, 0.26f),
        };

        /// <summary>
        /// The portrait of a hero, cut from the transparent standing art (the new, uniform
        /// illustrations) and painted over `backdrop`. Falls back to the card illustration when
        /// there is no standing art, or when the hero is wearing a skin (skins exist as cards only).
        /// </summary>
        public static void SetPortrait(VisualElement e, string heroId, Crop crop, bool worn = true, Color? backdrop = null, bool round = false)
        {
            var skin = worn ? Core.SkinService.Active(Core.Game.Player, heroId) : null;
            var standing = string.IsNullOrEmpty(skin) ? GameData.StandingArt(heroId) : null;
            if (standing == null)
            {
                SetArt(e, worn ? GameData.WornCardArt(heroId) : GameData.CardArt(heroId));
                return;
            }
            e.AddToClassList("portrait");
            var (zoom, focus) = CropOf(crop);
            var bg = backdrop ?? UiPaint.C(222, 234, 248);
            var head = FaceBox(heroId);
            ModalFrame.Painted(e, (ctx, r) =>
            {
                var poly = round ? UiPaint.Ellipse(r.center, r.width * 0.5f, r.height * 0.5f) : UiPaint.RoundRect(r, 0f, 1);
                UiPaint.Fill(ctx, poly, UiPaint.Vertical(UiPaint.WithAlpha(Color.Lerp(bg, Color.white, 0.55f), bg.a), bg, r.yMin, r.yMax));
                if (head.HasValue)
                {
                    // Cropped on the detected head: its size in the frame and where it sits.
                    var hb = head.Value;
                    var (share, y) = crop switch
                    {
                        Crop.Face => (0.74f, 0.52f),
                        Crop.Bust => (0.4f, 0.3f),
                        _ => (0.5f, 0.42f),
                    };
                    var imgH = r.height * share / Mathf.Max(0.02f, hb.height);
                    UiPaint.ImageAt(ctx, poly, standing, imgH, hb.center, new Vector2(r.center.x, r.yMin + r.height * y));
                }
                else UiPaint.ImageFocus(ctx, poly, standing, r, zoom, focus);
            });
        }

        [Serializable] class FaceRow { public string id; public float x0, y0, x1, y1; }
        [Serializable] class FaceFile { public System.Collections.Generic.List<FaceRow> items = new(); }
        static System.Collections.Generic.Dictionary<string, Rect> _faces;

        /// <summary>The head box on a standing illustration (canvas fractions, y from the top) — tools/face_boxes.py.</summary>
        public static Rect? FaceBox(string heroId)
        {
            if (_faces == null)
            {
                _faces = new System.Collections.Generic.Dictionary<string, Rect>();
                var asset = Resources.Load<TextAsset>("Data/faces");
                if (asset != null)
                    foreach (var f in JsonUtility.FromJson<FaceFile>(asset.text).items)
                        _faces[f.id] = Rect.MinMaxRect(f.x0, f.y0, f.x1, f.y1);
            }
            var key = heroId == GameData.MainId ? "intern" : heroId;
            return key != null && _faces.TryGetValue(key, out var r) ? r : null;
        }

        /// <summary>Paints a hero's standing art into poly, cropped on the detected head.</summary>
        public static void PaintPortrait(MeshGenerationContext ctx, System.Collections.Generic.IList<Vector2> poly, Sprite standing, string heroId, Rect r, Crop crop)
        {
            var head = FaceBox(heroId);
            var (zoom, focus) = CropOf(crop);
            if (!head.HasValue) { UiPaint.ImageFocus(ctx, poly, standing, r, zoom, focus); return; }
            var (share, y) = crop switch { Crop.Face => (0.74f, 0.52f), Crop.Bust => (0.4f, 0.3f), _ => (0.5f, 0.42f) };
            var hb = head.Value;
            UiPaint.ImageAt(ctx, poly, standing, r.height * share / Mathf.Max(0.02f, hb.height), hb.center, new Vector2(r.center.x, r.yMin + r.height * y));
        }

        // The reference labels its panels twice: the Korean title and a small spaced-out English
        // line after it (DAILY TASKS, ATTENDANCE). One table, applied after a screen is built, so
        // no title call site has to know (ui_critique round 9: 10-Quests #3, 11-Progress #1).
        static readonly System.Collections.Generic.Dictionary<string, string> English = new()
        {
            ["일일 업무"] = "DAILY TASKS", ["출근 도장"] = "ATTENDANCE", ["전체 완료 보너스"] = "ALL CLEAR",
            ["대표 사원"] = "LEAD", ["광고 보상"] = "AD REWARD", ["부문 시너지"] = "SYNERGY", ["편성 구성"] = "FORMATION",
            ["대기 인원"] = "STANDBY", ["업적"] = "ACHIEVEMENTS", ["회사 이전"] = "RELOCATION", ["출장"] = "BUSINESS TRIP",
            ["마일스톤"] = "MILESTONES", ["스킬"] = "SKILL", ["특성"] = "TRAIT", ["호감도"] = "AFFECTION",
            ["인사 기록"] = "PROFILE", ["기본 능력치"] = "STATUS", ["비품"] = "EQUIPMENT", ["승진 조건"] = "PROMOTION",
        };

        public static void TitleSubs(VisualElement root)
        {
            if (root == null) return;
            root.Query<Label>().ForEach(l =>
            {
                if (!(l.ClassListContains("section-title") || l.ClassListContains("qs-title__name") || l.ClassListContains("block__title"))) return;
                var t = l.text ?? "";
                if (t.Length == 0 || t.Contains("<size")) return;
                string en = null;
                foreach (var kv in English)
                    if (t == kv.Key || t.StartsWith(kv.Key + " ") || t.EndsWith(" " + kv.Key) || t.Contains("→ " + kv.Key)) { en = kv.Value; break; }
                if (en == null) return;
                l.enableRichText = true;
                l.text = t + $"  <size=55%><color=#94A7BA><b><cspace=0.12em>{en}</cspace></b></color></size>";
            });
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
            SetPortrait(art, def.id, Crop.Bust, owned != null, Color.Lerp(gradeColor, Color.white, 0.72f));

            // A card you do not own is DARKENED by a scrim over the art. Opacity composites
            // against the white card behind and bleaches towards white instead.
            if (owned == null)
            {
                // cold and dark, with a lock: the reference's not-yet-recruited entry. At .58 the
                // art still read as owned at a glance (ui_critique round 1, 07-Roster #1)
                // on the CARD, not inside the art: the portrait arrives as a child of the art after
                // this runs, and a scrim inside it ended up under the picture
                var scrim = Div("card__scrim", card);
                scrim.pickingMode = PickingMode.Ignore;
                // .92 on paper: this project is in Linear colour space and UI Toolkit composites a
                // background alpha far weaker than the number says (HANDOFF, "an unowned card
                // bleached"); at .74 the art still read at a glance as owned
                scrim.style.backgroundColor = new Color(0.06f, 0.1f, 0.19f, 0.93f);
                ModalFrame.Painted(Div("card__lock", card), DrawLock);
            }

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

        /// <summary>A padlock: a rounded body and a shackle, pale on the dark scrim.</summary>
        public static void DrawLock(MeshGenerationContext ctx, Rect r)
        {
            var c = r.center; var w = Mathf.Min(r.width, r.height) * 0.62f;
            var ink = UiPaint.C(206, 220, 236, 0.9f);
            var body = new Rect(c.x - w * 0.5f, c.y - w * 0.05f, w, w * 0.62f);
            // the shackle: an arch of thick strokes above the body
            const int n = 14; var rad = w * 0.3f; var t = w * 0.11f;
            for (var i = 0; i < n; i++)
            {
                float a0 = Mathf.PI * i / n, a1 = Mathf.PI * (i + 1) / n;
                var p0 = new Vector2(c.x + Mathf.Cos(a0) * rad, body.yMin - Mathf.Sin(a0) * rad);
                var p1 = new Vector2(c.x + Mathf.Cos(a1) * rad, body.yMin - Mathf.Sin(a1) * rad);
                var nrm = new Vector2(-(p1 - p0).y, (p1 - p0).x).normalized * t * 0.5f;
                UiPaint.Fill(ctx, new List<Vector2> { p0 + nrm, p1 + nrm, p1 - nrm, p0 - nrm }, ink, 0.6f);
            }
            UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(c.x - rad - t * 0.5f, body.yMin - 2f, c.x - rad + t * 0.5f, body.yMin + 6f), 1f), ink);
            UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(c.x + rad - t * 0.5f, body.yMin - 2f, c.x + rad + t * 0.5f, body.yMin + 6f), 1f), ink);
            UiPaint.Fill(ctx, UiPaint.RoundRect(body, w * 0.1f), ink);
            UiPaint.Fill(ctx, UiPaint.Ellipse(new Vector2(c.x, body.center.y - w * 0.04f), w * 0.07f, w * 0.07f), UiPaint.C(30, 44, 70, 0.9f));
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
