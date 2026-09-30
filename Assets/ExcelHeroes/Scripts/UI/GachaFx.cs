using System;
using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// The pull's set pieces, in Blue Archive's order (the user's round of 2026-09-30): one tell for
    /// the whole batch, then ten cards dealt face down and turned over one after another — a good
    /// one charges up in its colour before it turns, and an A or S stops the deal for its own
    /// entrance: the whole illustration, the name, what the member is (role, trait, department,
    /// EX), and the line. SKIP turns every card at once. Any card of the result, or a pick-up
    /// figure on the banner, opens the member's card (<see cref="Info"/>): the illustration, the SD
    /// card and the profile.
    /// </summary>
    public static class GachaFx
    {
        // ---------------------------------------------------------------- shared

        public static (Color top, Color bot) Tint(string grade) => grade switch
        {
            "S" => (UiPaint.C(255, 226, 120), UiPaint.C(244, 170, 40)),
            "A" => (UiPaint.C(206, 160, 250), UiPaint.C(128, 78, 206)),
            "B" => (UiPaint.C(140, 190, 250), UiPaint.C(60, 112, 204)),
            _ => (UiPaint.C(176, 188, 204), UiPaint.C(104, 118, 142)),
        };

        public static bool Big(string grade) => grade is "S" or "A";

        /// <summary>A code tween on the element's own scheduler: f(0..1) every frame for dur seconds (unscaled).</summary>
        public static void Tween(VisualElement e, float dur, Action<float> f, Action done = null)
        {
            var t0 = Time.realtimeSinceStartup;
            f(0f);
            IVisualElementScheduledItem it = null;
            it = e.schedule.Execute(() =>
            {
                var k = Mathf.Clamp01((Time.realtimeSinceStartup - t0) / Mathf.Max(0.001f, dur));
                f(k);
                if (k >= 1f) { it?.Pause(); done?.Invoke(); }
            }).Every(16);
        }

        static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t) * (1f - t); }
        static float Back(float t) { t = Mathf.Clamp01(t); const float c = 1.7f; return 1f + (c + 1f) * Mathf.Pow(t - 1f, 3f) + c * Mathf.Pow(t - 1f, 2f); }

        static List<Vector2> Star4(Vector2 c, float r)
        {
            var pts = new List<Vector2>();
            for (var i = 0; i < 8; i++)
            {
                var a = i / 8f * Mathf.PI * 2f - Mathf.PI / 2f;
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? r : r * 0.32f));
            }
            return pts;
        }

        /// <summary>Light rays turning slowly about a point, in the grade's colour (S and A only).</summary>
        static void Rays(MeshGenerationContext ctx, Vector2 c, float len, Color col, float t, int n = 16)
        {
            for (var i = 0; i < n; i++)
            {
                var a0 = (i / (float)n) * Mathf.PI * 2f + t * 0.25f;
                var a1 = a0 + Mathf.PI / n * 0.55f;
                UiPaint.Fill(ctx, new List<Vector2> { c, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * len, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * len },
                             UiPaint.WithAlpha(col, 0.16f + 0.06f * Mathf.Sin(t * 2f + i)), 0f);
            }
        }

        static VisualElement Chip(VisualElement parent, string key, string value, Color accent)
        {
            var chip = UiKit.Div("gx-chip", parent);
            ModalFrame.Painted(chip, (ctx, r) =>
            {
                var p = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.5f, 3f);
                UiPaint.Fill(ctx, p, UiPaint.C(255, 255, 255, 0.94f));
                UiPaint.Stroke(ctx, p, UiPaint.C(24, 36, 60, 0.14f), 1.5f);   // it also sits on the white of the member's card
                UiPaint.Fill(ctx, UiPaint.Clip(p, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin - 20f, r.yMin, r.xMin + 8f, r.yMax), 0f)), accent);
            });
            UiKit.Text(key, "gx-chip__key", chip).pickingMode = PickingMode.Ignore;
            UiKit.Text(value, "gx-chip__value", chip).pickingMode = PickingMode.Ignore;
            return chip;
        }

        /// <summary>role · trait · department · EX, as chips.</summary>
        static void Profile(VisualElement parent, HeroDef h)
        {
            var row = UiKit.Div("gx-chips", parent);
            var div = GameData.Division(h.division);
            Chip(row, "역할", UiKit.RoleName(h.role), UiPaint.C(46, 135, 246));
            var trait = GameData.Trait(h.trait);
            if (trait != null) Chip(row, "특성", trait.name, UiPaint.C(0, 190, 170));
            if (div != null) Chip(row, "부서", div.name, div.Color);
        }

        // ---------------------------------------------------------------- the entrance

        /// <summary>
        /// A member's entrance: the whole standing illustration sliding in on the right over light
        /// rays of her grade, a navy band on the left with the grade, the name, the profile chips and
        /// the EX, and her line in a speech plate. Tap anywhere to go on.
        /// </summary>
        public static VisualElement Intro(PullResult r)
        {
            var h = r.hero; var (top, bot) = Tint(r.grade); var big = Big(r.grade);
            var view = UiKit.Div("gx-intro");
            var t0 = Time.realtimeSinceStartup;
            var bg = UiKit.Div("gx-intro__bg", view); bg.pickingMode = PickingMode.Ignore;
            ModalFrame.Painted(bg, (ctx, rr) =>
            {
                var t = Time.realtimeSinceStartup - t0;
                UiPaint.Fill(ctx, UiPaint.RoundRect(rr, 0f), UiPaint.Vertical(Color.Lerp(top, Color.white, 0.8f), Color.Lerp(bot, Color.white, 0.55f), rr.yMin, rr.yMax));
                var fc = new Vector2(rr.xMin + rr.width * 0.68f, rr.yMin + rr.height * 0.42f);
                if (big) Rays(ctx, fc, rr.width * 0.75f, Color.white, t, r.grade == "S" ? 20 : 14);
                UiPaint.Fill(ctx, UiPaint.Ellipse(fc, rr.width * 0.26f, rr.height * 0.5f), UiPaint.C(255, 255, 255, 0.45f), rr.height * 0.3f);
                // the navy band down the left, its edge on the slant, the grade's stripe along it
                var x0 = rr.xMin + rr.width * 0.47f; var x1 = rr.xMin + rr.width * 0.37f;
                UiPaint.Fill(ctx, new List<Vector2> { new(rr.xMin, rr.yMin), new(x0, rr.yMin), new(x1, rr.yMax), new(rr.xMin, rr.yMax) },
                             UiPaint.Vertical(UiPaint.C(22, 36, 70, 0.94f), UiPaint.C(12, 20, 44, 0.96f), rr.yMin, rr.yMax), 0f);
                UiPaint.Fill(ctx, new List<Vector2> { new(x0, rr.yMin), new(x0 + 22f, rr.yMin), new(x1 + 22f, rr.yMax), new(x1, rr.yMax) }, bot, 0f);
                UiPaint.Fill(ctx, new List<Vector2> { new(x0 + 34f, rr.yMin), new(x0 + 40f, rr.yMin), new(x1 + 40f, rr.yMax), new(x1 + 34f, rr.yMax) }, UiPaint.WithAlpha(top, 0.8f), 0f);
                if (r.grade == "S")
                    for (var i = 0; i < 14; i++)
                    {
                        var a = i * 2.39f + t * 0.3f; var k = 0.5f + 0.5f * Mathf.Sin(t * 3f + i * 1.3f);
                        var c = fc + new Vector2(Mathf.Cos(a) * rr.width * (0.12f + (i % 5) * 0.05f), Mathf.Sin(a) * rr.height * (0.2f + (i % 4) * 0.08f));
                        UiPaint.Fill(ctx, Star4(c, 10f + 14f * k), UiPaint.C(255, 246, 200, 0.35f + 0.6f * k));
                    }
            });
            if (big) bg.schedule.Execute(() => bg.MarkDirtyRepaint()).Every(33);

            // the figure: the whole standing illustration (a card in a frame for those drawn as cards only)
            var standing = GameData.StandingArt(h.id);
            var fig = UiKit.Div(standing != null ? "gx-intro__fig" : "gx-intro__fig gx-intro__fig--card", view);
            fig.pickingMode = PickingMode.Ignore;
            UiKit.SetArt(fig, standing ?? GameData.CardArt(h.id));
            Tween(fig, 0.42f, k => { var e = EaseOut(k); fig.style.translate = new Translate(220f * (1f - e), 0f); fig.style.opacity = e; });

            var info = UiKit.Div("gx-intro__info", view); info.pickingMode = PickingMode.Ignore;
            var tags = UiKit.Div("gx-intro__tags", info);
            if (r.isPickup) UiKit.Text("PICK UP", "gx-tag gx-tag--pick", tags);
            if (r.isNew) UiKit.Text("NEW", "gx-tag gx-tag--new", tags);
            else if (r.promoted) UiKit.Text($"승급 ★{r.starAfter}", "gx-tag", tags);
            else UiKit.Text($"중복 · 승급 조각 {r.copiesAfter}", "gx-tag gx-tag--dup", tags);
            if (!string.IsNullOrEmpty(r.pityReason)) UiKit.Text(r.pityReason, "gx-tag", tags);

            var gradeRow = UiKit.Div("gx-intro__graderow", info);
            var plate = UiKit.Div("gx-intro__grade", gradeRow);
            ModalFrame.Painted(plate, (ctx, rr) =>
            {
                var p = UiPaint.SkewRect(rr, SkewPlate.SlantFor(rr.height) * 0.7f, 4f);
                UiPaint.Fill(ctx, p, UiPaint.Vertical(Color.Lerp(top, Color.white, 0.15f), bot, rr.yMin, rr.yMax));
                UiPaint.Stroke(ctx, p, UiPaint.C(255, 255, 255, 0.9f), 2f);
            });
            UiKit.Text(r.grade, "gx-intro__grade-text", plate).pickingMode = PickingMode.Ignore;
            var stars = UiKit.Text(new string('★', Mathf.Clamp(r.starAfter, 1, 5)), "gx-intro__stars", gradeRow);
            stars.style.color = top;
            UiKit.Text(GameData.Grade(r.grade)?.label ?? "", "gx-intro__gradelabel", gradeRow);

            UiKit.Text(h.name, "gx-intro__name", info);
            UiKit.Text($"{h.nick} · {h.dept}", "gx-intro__nick", info);
            Profile(info, h);
            var ex = UiKit.Div("gx-intro__ex", info);
            UiKit.Text("EX", "gx-intro__ex-tag", ex);
            UiKit.Text(h.skillName, "gx-intro__ex-name", ex);
            var exd = UiKit.Text(GameData.SkillText(h), "gx-intro__ex-desc", info);
            exd.style.whiteSpace = WhiteSpace.Normal;
            Tween(info, 0.36f, k => { var e = EaseOut(k); info.style.translate = new Translate(-120f * (1f - e), 0f); info.style.opacity = e; });

            if (!string.IsNullOrEmpty(h.line))
            {
                var say = UiKit.Div("gx-intro__say", view); say.pickingMode = PickingMode.Ignore;
                ModalFrame.Painted(say, (ctx, rr) =>
                {
                    var p = UiPaint.SkewRect(rr, SkewPlate.SlantFor(rr.height) * 0.35f, 6f);
                    UiPaint.Shadow(ctx, p, new Vector2(0f, 4f), UiPaint.C(0, 20, 50, 0.25f), 10f);
                    UiPaint.Fill(ctx, p, UiPaint.C(255, 255, 255, 0.96f));
                    UiPaint.Fill(ctx, UiPaint.Clip(p, UiPaint.RoundRect(Rect.MinMaxRect(rr.xMin - 30f, rr.yMin, rr.xMin + 10f, rr.yMax), 0f)), bot);
                });
                UiKit.Text(h.name, "gx-intro__say-who", say);
                var line = UiKit.Text($"“{h.line}”", "gx-intro__say-line", say); line.style.whiteSpace = WhiteSpace.Normal;
                say.style.opacity = 0f;
                say.schedule.Execute(() => Tween(say, 0.3f, k => { say.style.opacity = k; say.style.translate = new Translate(0f, 30f * (1f - EaseOut(k))); })).ExecuteLater(260);
            }
            UiKit.Text("TAP TO CONTINUE", "gx-intro__hint", view).pickingMode = PickingMode.Ignore;

            // an S lands with a white flash over everything
            if (r.grade == "S")
            {
                var flash = UiKit.Div("gx-flash", view); flash.pickingMode = PickingMode.Ignore;
                Tween(flash, 0.5f, k => flash.style.opacity = 1f - EaseOut(k), () => flash.RemoveFromHierarchy());
            }
            return view;
        }

        // ---------------------------------------------------------------- the deal

        /// <summary>One card of the result: face down until <see cref="Turn"/>.</summary>
        public class Card
        {
            public PullResult R; public VisualElement Root, Face, BackEl; public bool Open; public bool Charging;
        }

        /// <summary>
        /// The ten, five across and two down, each a tall parallelogram leaning hard (the reference's
        /// slant, not a row of upright strips), face down at first. The grade on a plate at the top
        /// RIGHT, the stars and the name on the foot band (two lines for a long title).
        /// </summary>
        public static VisualElement Grid(List<PullResult> results, List<Card> cards, bool open)
        {
            var grid = UiKit.Div("gx-grid");
            foreach (var r in results)
            {
                var c = new Card { R = r };
                c.Root = UiKit.Div("gx-card", grid);
                c.Face = UiKit.Div("gx-card__face", c.Root); c.Face.pickingMode = PickingMode.Ignore;
                BuildFace(c.Face, r);
                c.BackEl = UiKit.Div("gx-card__back", c.Root); c.BackEl.pickingMode = PickingMode.Ignore;
                var cc = c;
                ModalFrame.Painted(c.BackEl, (ctx, rect) =>
                {
                    var slant = rect.height * 0.2f;
                    var outer = UiPaint.SkewRect(rect, slant, 6f);
                    var (tp, bt) = Tint(cc.R.grade);
                    if (cc.Charging) UiPaint.Ring(ctx, outer, UiPaint.WithAlpha(bt, 0.85f), UiPaint.WithAlpha(bt, 0f), 30f);
                    UiPaint.Shadow(ctx, outer, new Vector2(0f, 5f), UiPaint.C(20, 40, 80, 0.3f), 10f);
                    UiPaint.Fill(ctx, outer, UiPaint.Vertical(UiPaint.C(38, 62, 110), UiPaint.C(18, 30, 62), rect.yMin, rect.yMax));
                    // a spreadsheet's grid on the back, and the company mark
                    var inner = UiPaint.Offset(outer, -8f);
                    for (var i = 1; i < 8; i++)
                    {
                        var y = rect.yMin + rect.height * i / 8f;
                        UiPaint.Fill(ctx, UiPaint.Clip(inner, UiPaint.RoundRect(Rect.MinMaxRect(rect.xMin - 60f, y - 0.8f, rect.xMax + 60f, y + 0.8f), 0f)), UiPaint.C(120, 170, 240, 0.22f), 0f);
                    }
                    for (var i = 1; i < 4; i++)
                    {
                        var x = rect.xMin + rect.width * i / 4f;
                        UiPaint.Fill(ctx, UiPaint.Clip(inner, new List<Vector2> { new(x + slant * 0.5f - 0.8f, rect.yMin), new(x + slant * 0.5f + 0.8f, rect.yMin), new(x - slant * 0.5f + 0.8f, rect.yMax), new(x - slant * 0.5f - 0.8f, rect.yMax) }), UiPaint.C(120, 170, 240, 0.22f), 0f);
                    }
                    UiPaint.Stroke(ctx, inner, UiPaint.C(120, 190, 255, 0.55f), 2f);
                    UiPaint.Fill(ctx, Star4(rect.center, rect.width * 0.16f), cc.Charging ? UiPaint.WithAlpha(tp, 0.95f) : UiPaint.C(160, 205, 255, 0.7f));
                });
                UiKit.Text("EXCEL HEROES", "gx-card__backmark", c.BackEl).pickingMode = PickingMode.Ignore;
                SetOpen(c, open);
                cards.Add(c);
            }
            return grid;
        }

        static void SetOpen(Card c, bool open)
        {
            c.Open = open;
            c.Face.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            c.BackEl.style.display = open ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>Turns a card over (a squash through its edge and back, the face popping a little past full).</summary>
        public static void Turn(Card c, bool instant = false)
        {
            if (c.Open) return;
            if (instant) { SetOpen(c, true); c.Root.style.scale = new Scale(Vector3.one); return; }
            Tween(c.Root, 0.09f, k => c.Root.style.scale = new Scale(new Vector3(1f - k, 1f + 0.05f * k, 1f)), () =>
            {
                SetOpen(c, true);
                Tween(c.Root, 0.16f, k => { var e = Back(k); c.Root.style.scale = new Scale(new Vector3(Mathf.Max(0.01f, e), 1f + 0.05f * (1f - k), 1f)); });
                if (Big(c.R.grade)) Burst(c.Root, c.R.grade);
            });
        }

        /// <summary>The back lights up in the grade's colour and shivers before a good card turns.</summary>
        public static void Charge(Card c, float dur)
        {
            c.Charging = true;
            Tween(c.BackEl, dur, k =>
            {
                c.Root.style.translate = new Translate(Mathf.Sin(k * 60f) * 5f * k, 0f);
                c.BackEl.MarkDirtyRepaint();
            }, () => c.Root.style.translate = new Translate(0f, 0f));
        }

        static void Burst(VisualElement host, string grade)
        {
            var (top, _) = Tint(grade);
            var fx = UiKit.Div("gx-burst", host); fx.pickingMode = PickingMode.Ignore;
            var t0 = Time.realtimeSinceStartup;
            ModalFrame.Painted(fx, (ctx, rr) =>
            {
                var k = Mathf.Clamp01((Time.realtimeSinceStartup - t0) / 0.6f);
                var e = EaseOut(k);
                UiPaint.Fill(ctx, UiPaint.Ellipse(rr.center, rr.width * 0.3f * (0.4f + e), rr.height * 0.3f * (0.4f + e)), UiPaint.C(255, 255, 255, 0.7f * (1f - k)), rr.width * 0.2f);
                for (var i = 0; i < 10; i++)
                {
                    var a = i / 10f * Mathf.PI * 2f;
                    var c = rr.center + new Vector2(Mathf.Cos(a) * rr.width * 0.5f * e, Mathf.Sin(a) * rr.height * 0.45f * e);
                    UiPaint.Fill(ctx, Star4(c, 16f * (1f - k) + 4f), UiPaint.WithAlpha(top, 1f - k));
                }
            });
            fx.schedule.Execute(() => fx.MarkDirtyRepaint()).Every(16);
            fx.schedule.Execute(() => fx.RemoveFromHierarchy()).ExecuteLater(650);
        }

        static void BuildFace(VisualElement face, PullResult r)
        {
            var (top, bot) = Tint(r.grade); var isS = r.grade == "S"; var isA = r.grade == "A";
            var sprite = GameData.StandingArt(r.hero.id);
            var cardArt = sprite == null ? GameData.CardArt(r.hero.id) : null;
            ModalFrame.Painted(face, (ctx, rect) =>
            {
                var slant = rect.height * 0.2f;
                var outer = UiPaint.SkewRect(rect, slant, 6f);
                if (isS) UiPaint.Ring(ctx, outer, UiPaint.C(255, 196, 40, 0.8f), UiPaint.C(255, 196, 40, 0f), 30f);
                else if (isA) UiPaint.Ring(ctx, outer, UiPaint.C(176, 96, 250, 0.65f), UiPaint.C(176, 96, 250, 0f), 22f);
                UiPaint.Shadow(ctx, outer, new Vector2(0f, 5f), UiPaint.C(20, 40, 80, 0.3f), 10f);
                UiPaint.Fill(ctx, outer, Color.white);
                var inner = UiPaint.Offset(outer, -4f);
                var bandTop = rect.yMax - rect.height * 0.27f;
                var art = UiPaint.Clip(inner, UiPaint.RoundRect(Rect.MinMaxRect(rect.xMin - 80f, rect.yMin - 50f, rect.xMax + 80f, bandTop), 0f));
                // the portrait a little wider than the card, so the lean does not crop the face
                var dest = Rect.MinMaxRect(rect.xMin - slant * 0.35f, rect.yMin + rect.height * 0.02f, rect.xMax + slant * 0.35f, bandTop);
                UiPaint.Fill(ctx, art, UiPaint.Vertical(Color.Lerp(top, Color.white, 0.55f), Color.Lerp(bot, Color.white, 0.25f), rect.yMin, bandTop));
                if (sprite != null) UiKit.PaintPortrait(ctx, art, sprite, r.hero.id, dest, UiKit.Crop.Bust);
                else UiPaint.Image(ctx, art, cardArt, dest, 0.1f);
                UiPaint.Fill(ctx, art, UiPaint.Vertical(UiPaint.WithAlpha(bot, 0f), UiPaint.WithAlpha(bot, 0.45f), rect.yMin + rect.height * 0.5f, bandTop), 0f);
                var band = UiPaint.Clip(inner, UiPaint.RoundRect(Rect.MinMaxRect(rect.xMin - 80f, bandTop, rect.xMax + 80f, rect.yMax + 50f), 0f));
                UiPaint.Fill(ctx, band, UiPaint.Vertical(Color.Lerp(bot, UiPaint.C(20, 30, 60), 0.35f), Color.Lerp(bot, UiPaint.C(14, 20, 44), 0.6f), bandTop, rect.yMax), 0f);
                UiPaint.Fill(ctx, UiPaint.Clip(inner, UiPaint.RoundRect(Rect.MinMaxRect(rect.xMin - 80f, bandTop - 1.5f, rect.xMax + 80f, bandTop + 1.5f), 0f)), UiPaint.WithAlpha(top, 0.95f), 0f);
                UiPaint.Stroke(ctx, outer, UiPaint.WithAlpha(bot, 0.9f), 2.5f);
            });
            // the grade on a plate at the card's top right
            var badge = UiKit.Div("gx-card__grade", face); badge.pickingMode = PickingMode.Ignore;
            ModalFrame.Painted(badge, (ctx, rr) =>
            {
                var p = UiPaint.SkewRect(rr, SkewPlate.SlantFor(rr.height), 3f);
                UiPaint.Shadow(ctx, p, new Vector2(0f, 2f), UiPaint.C(0, 0, 0, 0.3f), 3f);
                UiPaint.Fill(ctx, p, UiPaint.Vertical(Color.Lerp(top, Color.white, 0.1f), bot, rr.yMin, rr.yMax));
                UiPaint.Stroke(ctx, p, UiPaint.C(255, 255, 255, 0.9f), 1.5f);
            });
            UiKit.Text(r.grade, "gx-card__grade-text", badge).pickingMode = PickingMode.Ignore;
            if (r.isNew) UiKit.Text("NEW", "gx-card__new", face).pickingMode = PickingMode.Ignore;
            var stars = UiKit.Text(new string('★', Mathf.Clamp(r.starAfter, 1, 5)), "gx-card__stars", face); stars.pickingMode = PickingMode.Ignore;
            var name = UiKit.Text(r.hero.name, "gx-card__name", face); name.pickingMode = PickingMode.Ignore;
            if (r.hero.name.Length >= 7) name.AddToClassList("gx-card__name--long");
            if (isS)
            {
                var fx = UiKit.Div("gx-card__sfx", face); fx.pickingMode = PickingMode.Ignore;
                var t0 = Time.realtimeSinceStartup;
                ModalFrame.Painted(fx, (ctx, rr) =>
                {
                    var t = Time.realtimeSinceStartup - t0;
                    for (var i = 0; i < 8; i++)
                    {
                        var a = i / 8f * Mathf.PI * 2f + t * 0.6f;
                        var c = rr.center + new Vector2(Mathf.Cos(a) * rr.width * 0.56f, Mathf.Sin(a) * rr.height * 0.54f);
                        var k = 0.5f + 0.5f * Mathf.Sin(t * 5f + i * 1.7f);
                        UiPaint.Fill(ctx, Star4(c, 8f + 10f * k), UiPaint.C(255, 244, 200, 0.4f + 0.6f * k));
                    }
                });
                fx.schedule.Execute(() => fx.MarkDirtyRepaint()).Every(33);
            }
        }

        // ---------------------------------------------------------------- the member's card

        /// <summary>
        /// The member's card, over whatever is open: the illustration, an SD card beside it, and the
        /// profile — grade, name, rank and department, role / trait / department chips, the EX and the
        /// trait written out, the line and the bio. Opened from a result card or a pick-up figure.
        /// </summary>
        public static VisualElement Info(string heroId, Action onClose)
        {
            var h = GameData.Hero(heroId);
            var view = UiKit.Div("gx-info");
            if (h == null) return view;
            var owned = Game.Player?.Find(heroId);
            var (top, bot) = Tint(h.grade);
            view.RegisterCallback<ClickEvent>(e => { if (e.target == view) onClose(); });
            var panel = UiKit.Div("gx-info__panel", view);
            panel.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            ModalFrame.Painted(panel, (ctx, r) =>
            {
                var p = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.12f, 10f);
                UiPaint.Shadow(ctx, p, new Vector2(0f, 10f), UiPaint.C(0, 20, 50, 0.35f), 24f);
                UiPaint.Fill(ctx, p, UiPaint.Vertical(UiPaint.C(255, 255, 255), UiPaint.C(238, 245, 252), r.yMin, r.yMax));
                UiPaint.Fill(ctx, UiPaint.Clip(p, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin - 40f, r.yMin - 10f, r.xMax + 40f, r.yMin + 14f), 0f)), UiPaint.Horizontal(bot, top, r.xMin, r.xMax));
            });

            // the illustration
            var art = UiKit.Div("gx-info__art", panel);
            var card = GameData.CardArt(heroId);
            ModalFrame.Painted(art, (ctx, r) =>
            {
                var p = UiPaint.SkewRect(r, r.height * 0.1f, 8f);
                UiPaint.Shadow(ctx, p, new Vector2(0f, 4f), UiPaint.C(0, 20, 50, 0.18f), 8f);
                UiPaint.Fill(ctx, p, Color.white);
                var inner = UiPaint.Offset(p, -5f);
                if (card != null) UiPaint.Image(ctx, inner, card, Rect.MinMaxRect(r.xMin - r.height * 0.06f, r.yMin, r.xMax + r.height * 0.06f, r.yMax), 0.05f);
                UiPaint.Stroke(ctx, p, UiPaint.WithAlpha(bot, 0.55f), 1.5f);
            });

            // the SD card
            var sdCard = UiKit.Div("gx-info__sd", panel);
            var sd = GameData.SdPoseArt(heroId);   // the persona pose
            ModalFrame.Painted(sdCard, (ctx, r) =>
            {
                var p = UiPaint.SkewRect(r, r.height * 0.1f, 8f);
                UiPaint.Shadow(ctx, p, new Vector2(0f, 6f), UiPaint.C(0, 20, 50, 0.3f), 12f);
                UiPaint.Fill(ctx, p, UiPaint.Vertical(Color.Lerp(top, Color.white, 0.85f), Color.Lerp(bot, Color.white, 0.6f), r.yMin, r.yMax));   // light: the SD reads over it
                var inner = UiPaint.Offset(p, -5f);
                for (var i = 0; i < 6; i++)
                {
                    var x = r.xMin + r.width * (0.05f + i * 0.2f);
                    UiPaint.Fill(ctx, UiPaint.Clip(inner, new List<Vector2> { new(x, r.yMin), new(x + 14f, r.yMin), new(x + 14f - r.height * 0.3f, r.yMax), new(x - r.height * 0.3f, r.yMax) }), UiPaint.C(255, 255, 255, 0.18f), 0f);
                }
                UiPaint.Fill(ctx, UiPaint.Ellipse(new Vector2(r.center.x, r.yMax - r.height * 0.2f), r.width * 0.32f, r.height * 0.04f), UiPaint.C(0, 30, 70, 0.22f), 8f);
                if (sd != null)
                {
                    var tr = sd.textureRect; var hgt = r.height * 0.76f; var wid = hgt * tr.width / tr.height;
                    var dest = new Rect(r.center.x - wid * 0.5f, r.yMax - r.height * 0.18f - hgt, wid, hgt);
                    UiPaint.Image(ctx, inner, sd, dest, 0f);
                }
                var band = UiPaint.Clip(inner, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin - 60f, r.yMax - r.height * 0.14f, r.xMax + 60f, r.yMax + 20f), 0f));
                UiPaint.Fill(ctx, band, UiPaint.C(18, 28, 56, 0.9f), 0f);
                UiPaint.Stroke(ctx, p, UiPaint.WithAlpha(bot, 0.55f), 1.5f);
            });
            UiKit.Text("SD", "gx-info__sd-tag", sdCard).pickingMode = PickingMode.Ignore;
            UiKit.Text(h.name, "gx-info__sd-name", sdCard).pickingMode = PickingMode.Ignore;

            // the profile
            var col = UiKit.Div("gx-info__col", panel);
            var gradeRow = UiKit.Div("gx-intro__graderow", col);
            var plate = UiKit.Div("gx-intro__grade gx-intro__grade--sm", gradeRow);
            ModalFrame.Painted(plate, (ctx, rr) =>
            {
                var p = UiPaint.SkewRect(rr, SkewPlate.SlantFor(rr.height) * 0.7f, 4f);
                UiPaint.Fill(ctx, p, UiPaint.Vertical(Color.Lerp(top, Color.white, 0.15f), bot, rr.yMin, rr.yMax));
            });
            UiKit.Text(h.grade, "gx-intro__grade-text", plate).pickingMode = PickingMode.Ignore;
            var stars = UiKit.Text(new string('★', Mathf.Clamp(owned?.star ?? 1, 1, 5)), "gx-intro__stars gx-intro__stars--sm", gradeRow);
            stars.style.color = bot;
            UiKit.Text(owned != null ? $"보유 · Lv.{owned.level}" : "미보유", "gx-info__own", gradeRow);
            UiKit.Text(h.name, "gx-info__name", col);
            UiKit.Text($"{h.nick} · {h.dept}", "gx-info__nick", col);
            Profile(col, h);
            UiKit.Text($"EX · {h.skillName}", "gx-info__head", col);
            var sk = UiKit.Text(GameData.SkillText(h), "gx-info__body", col); sk.style.whiteSpace = WhiteSpace.Normal;
            var trait = GameData.Trait(h.trait);
            if (trait != null)
            {
                UiKit.Text($"특성 · {trait.name}", "gx-info__head", col);
                var td = UiKit.Text(trait.desc, "gx-info__body", col); td.style.whiteSpace = WhiteSpace.Normal;
            }
            if (!string.IsNullOrEmpty(h.line)) { var l = UiKit.Text($"“{h.line}”", "gx-info__line", col); l.style.whiteSpace = WhiteSpace.Normal; }
            if (!string.IsNullOrEmpty(h.bio)) { var b = UiKit.Text(h.bio, "gx-info__bio", col); b.style.whiteSpace = WhiteSpace.Normal; }

            var close = UiKit.Btn("✕", "gx-info__close", onClose, panel);
            panel.style.scale = new Scale(new Vector3(0.94f, 0.94f, 1f)); panel.style.opacity = 0f;
            Tween(panel, 0.2f, k => { var e = EaseOut(k); panel.style.scale = new Scale(new Vector3(0.94f + 0.06f * e, 0.94f + 0.06f * e, 1f)); panel.style.opacity = e; });
            return view;
        }
    }
}
