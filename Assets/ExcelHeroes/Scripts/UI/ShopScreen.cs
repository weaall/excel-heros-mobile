using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 상점, as the Gemini shop mock-up (tools/out/design/mock_shop_0): the shelves down the left as
    /// slanted tabs, the goods in a three-across grid of white glass cards — the item on a tinted
    /// tile with its quantity, the name, "남은 수량 n/m", a slanted price plate with the currency's
    /// icon — a SOLD OUT band over a card bought out today, the reset timer at the top right.
    /// </summary>
    public class ShopScreen : IScreen
    {
        public string Cell => "B2";
        public string Formula => "=VLOOKUP(\"비품\",상점!A:D,4,FALSE)";

        readonly AppRoot _app;
        VisualElement _root, _grid, _tabs;
        Label _reset;
        string _tab = ShopService.Tabs[0];

        public ShopScreen(AppRoot app) => _app = app;

        public VisualElement Build()
        {
            _root = UiKit.Div("shop");
            _tabs = UiKit.Div("shop__tabs", _root);
            var panel = UiKit.Div("shop__panel", _root);
            ModalFrame.Painted(panel, (ctx, r) =>
            {
                var box = UiPaint.RoundRect(r, 14f, 6);
                UiPaint.Shadow(ctx, box, new Vector2(0f, 6f), UiPaint.C(10, 30, 60, 0.18f), 14f);
                UiPaint.Fill(ctx, box, UiPaint.C(255, 255, 255, 0.72f));
                UiPaint.Stroke(ctx, box, UiPaint.C(255, 255, 255, 0.95f), 2f);
            });
            var head = UiKit.Div("shop__head", panel);
            UiKit.Div("spacer", head);
            var chip = UiKit.Div("shop__reset", head);
            ModalFrame.Painted(chip, (ctx, r) => UiPaint.Fill(ctx, UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.5f, 4f), UiPaint.C(24, 40, 74)));
            _reset = UiKit.Text("", "shop__reset-text", chip);
            _grid = UiKit.Div("shop__grid", panel);
            _root.schedule.Execute(() => { if (_reset != null) _reset.text = ResetText(); }).Every(1000);
            Refresh();
            return _root;
        }

        // the label muted, the time itself in cyan (ui_critique r6 16-Shop #3)
        static string ResetText() => $"<color=#9AB0C4>초기화까지</color>  <color=#2CE0F8>{ShopService.ResetIn()}</color>";

        public void Refresh()
        {
            if (_grid == null) return;
            var p = Game.Player;
            _reset.text = ResetText();
            _tabs.Clear();
            foreach (var t in ShopService.Tabs)
            {
                var on = t == _tab;
                // docked tabs (ui_critique 16-Shop #2): flush against the panel, the open one navy with a
                // cyan bar at its outer edge, the rest white with a hairline — no floating pills
                var tab = UiKit.Btn("", on ? "shop__tab shop__tab--on" : "shop__tab", () => { _tab = t; Refresh(); }, _tabs);
                ModalFrame.Painted(tab, (ctx, r) =>
                {
                    var box = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.6f, 4f);
                    if (on)
                    {
                        UiPaint.Shadow(ctx, box, new Vector2(0f, 4f), UiPaint.C(10, 30, 60, 0.22f), 8f);
                        UiPaint.Fill(ctx, box, UiPaint.C(30, 43, 62));
                        UiPaint.Fill(ctx, UiPaint.Clip(box, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin - 30f, r.yMin, r.xMin + 22f, r.yMax), 0f)), UiPaint.C(0, 229, 255), 0f);
                    }
                    else
                    {
                        UiPaint.Fill(ctx, box, UiPaint.C(255, 255, 255, 0.92f));
                        UiPaint.Stroke(ctx, box, UiPaint.C(216, 226, 236), 1.5f);
                    }
                });
                UiKit.Text(t, "shop__tab-label", tab).pickingMode = PickingMode.Ignore;
                var en = t switch { "일반 상점" => "NORMAL", "보석 상점" => "GEM SHOP", "교환소" => "EXCHANGE", "무료 보급" => "FREE SUPPLY", _ => "" };
                UiKit.Text(en, "shop__tab-en", tab).pickingMode = PickingMode.Ignore;
                if (t == "무료 보급" && ShopService.HasFree(p)) UiKit.Div("shop__dot", tab).pickingMode = PickingMode.Ignore;
            }
            _grid.Clear();
            foreach (var g in ShopService.Goods.Where(x => x.Tab == _tab)) Card(g, p);
        }

        void Card(ShopService.Good g, PlayerState p)
        {
            var left = ShopService.Left(p, g);
            var soldOut = left <= 0;
            var card = UiKit.Div("scard", _grid);
            var tint = g.Grade switch { "A" => UiPaint.C(196, 150, 250), "B" => UiPaint.C(140, 190, 250), "S" => UiPaint.C(255, 214, 110), _ => g.Icon == "gold" ? UiPaint.C(255, 216, 110) : g.Icon == "gem" ? UiPaint.C(130, 220, 250) : UiPaint.C(170, 220, 190) };
            ModalFrame.Painted(card, (ctx, r) =>
            {
                var box = UiPaint.RoundRect(r, 10f, 6);
                UiPaint.Shadow(ctx, box, new Vector2(0f, 4f), UiPaint.C(10, 30, 60, 0.18f), 8f);
                UiPaint.Fill(ctx, box, UiPaint.Vertical(UiPaint.C(255, 255, 255), UiPaint.C(238, 244, 250), r.yMin, r.yMax));
                // the item in a square slot of its own, the grade only in the slot's rim and a thin
                // band on its top (ui_critique r5 16-Shop #1 — the half-card colour wash read as a web card)
                var sz = r.height - 36f;
                var slot = Rect.MinMaxRect(r.xMin + 18f, r.yMin + 18f, r.xMin + 18f + sz, r.yMax - 18f);
                var sbox = UiPaint.RoundRect(slot, 10f, 5);
                UiPaint.Fill(ctx, sbox, UiPaint.Vertical(Color.Lerp(tint, Color.white, 0.82f), Color.Lerp(tint, Color.white, 0.6f), slot.yMin, slot.yMax));
                UiPaint.Fill(ctx, UiPaint.Clip(sbox, UiPaint.RoundRect(Rect.MinMaxRect(slot.xMin, slot.yMin, slot.xMax, slot.yMin + 8f), 0f)), tint, 0f);
                UiPaint.Stroke(ctx, sbox, Color.Lerp(tint, UiPaint.C(40, 70, 110), 0.25f), 2f);
                UiPaint.Stroke(ctx, box, UiPaint.C(200, 214, 232), 1.5f);
            });
            var icon = UiKit.Div("scard__icon", card);
            var art = GameData.Icon(g.Icon);
            if (art != null) UiKit.SetArt(icon, art);
            UiKit.Text(g.Amount(p), "scard__qty", card).pickingMode = PickingMode.Ignore;
            var body = UiKit.Div("scard__body", card);
            UiKit.Text(g.Name, "scard__name", body);
            UiKit.Text($"남은 수량 {left}/{g.Limit}", "scard__left" + (left == 0 ? " scard__left--zero" : ""), body);
            var price = UiKit.Btn("", g.Pay == ShopService.Pay.Free ? "btn btn--primary scard__buy" : "btn btn--primary scard__buy", () =>
            {
                var got = ShopService.Buy(Game.Player, g);
                if (got == null) { _app.SetStatus(g.Pay == ShopService.Pay.Gems ? "보석이 부족합니다" : "재화가 부족합니다"); return; }
                AudioService.Play("bond", 0.8f);
                _app.SetStatus($"{g.Name} 구매 · {got}");
                Game.Touch();
                Refresh();
            }, body);
            var row = UiKit.Div("scard__price", price); row.pickingMode = PickingMode.Ignore;
            var cur = g.Pay switch { ShopService.Pay.Gold => "gold", ShopService.Pay.Gems => "gem", _ => null };
            if (cur != null && GameData.Icon(cur) != null) UiKit.SetArt(UiKit.Div("scard__cur", row), GameData.Icon(cur));
            UiKit.Text(g.Pay switch { ShopService.Pay.Free => "무료", ShopService.Pay.Points => $"포인트 {g.Price}", _ => g.Price.ToString("N0") }, "scard__price-text", row);
            // short of money the price stays a live white plate with the figure in red (BA's way —
            // a tap says why), only a sold-out good greys its button out
            if (!soldOut && !ShopService.CanBuy(p, g))
            {
                price.AddToClassList("scard__buy--short");
                SkewPlate.Apply(price, SkewPlate.Kind.Light);
            }
            price.SetEnabled(!soldOut);
            if (soldOut)
            {
                card.AddToClassList("scard--sold");
                var band = UiKit.Div("scard__sold", card); band.pickingMode = PickingMode.Ignore;
                ModalFrame.Painted(band, (ctx, r) => UiPaint.Fill(ctx, UiPaint.RoundRect(r, 0f), UiPaint.Horizontal(UiPaint.C(20, 30, 50, 0f), UiPaint.C(20, 30, 50, 0.85f), r.xMin, r.center.x)));
                UiKit.Text("SOLD OUT", "scard__sold-text", band).pickingMode = PickingMode.Ignore;
            }
        }
    }

    /// <summary>우편함 and 공지 as the one modal each, opened from the lobby.</summary>
    public static class InboxPanels
    {
        public static void OpenMail(AppRoot app, System.Action changed)
        {
            var p = Game.Player;
            MailService.Seed(p);
            var body = UiKit.Modal("우편함", app.CloseOverlay, out var panel, "modal--wide");
            var list = UiKit.Div("mail__list", body);
            void Build()
            {
                list.Clear();
                var any = false;
                foreach (var m in p.mail.OrderBy(x => x.claimed).ThenByDescending(x => x.day))
                {
                    any = true;
                    var row = UiKit.Div("mrow" + (m.claimed ? " mrow--done" : ""), list);
                    var ic = UiKit.Div("mrow__icon", row);
                    var mailArt = GameData.Icon("mail"); if (mailArt != null) UiKit.SetArt(ic, mailArt);
                    var text = UiKit.Div("mrow__text", row);
                    UiKit.Text(m.title, "mrow__title", text);
                    UiKit.Text($"{m.from} · {m.day}", "mrow__meta", text);
                    UiKit.Text(m.body, "mrow__body", text);
                    var gifts = UiKit.Div("mrow__gifts", row);
                    void Gift(string icon, string amount) { var g = UiKit.Div("rchip", gifts); var a = GameData.Icon(icon); if (a != null) UiKit.SetArt(UiKit.Div("rchip__icon", g), a); UiKit.Text(amount, "rchip__amount", g); }
                    if (m.gems > 0) Gift("gem", m.gems.ToString("N0"));
                    if (m.gold > 0) Gift("gold", m.gold.ToString("N0"));
                    if (!string.IsNullOrEmpty(m.itemSlot)) Gift("eq_" + m.itemSlot, $"{m.itemGrade}급 ×1");
                    if (m.claimed) UiKit.Text("수령 완료", "mrow__done", row);
                    else
                        UiKit.Btn("받기", "btn btn--primary mrow__take", () =>
                        {
                            var got = MailService.Claim(p, m);
                            AudioService.Play("bond");
                            app.SetStatus($"{m.title} · {got}");
                            Game.Touch(); changed?.Invoke(); Build();
                        }, row);
                }
                if (!any) UiKit.Text("받은 우편이 없습니다", "mail__empty", list);
            }
            Build();
            var foot = UiKit.Div("mail__foot", body);
            UiKit.Text("수령한 우편은 7일 뒤 정리됩니다", "mail__hint", foot);
            UiKit.Div("spacer", foot);
            UiKit.Btn("모두 받기", "btn btn--primary mail__all", () =>
            {
                var (n, line) = MailService.ClaimAll(p);
                if (n == 0) { app.SetStatus("받을 우편이 없습니다"); return; }
                AudioService.Play("bond");
                app.SetStatus($"우편 {n}통 수령 · {line}");
                Game.Touch(); changed?.Invoke(); Build();
            }, foot);
            app.OpenOverlay(panel);
        }

        static Color TagColour(string tag) => tag switch { "이벤트" => UiPaint.C(255, 92, 150), "업데이트" => UiPaint.C(46, 150, 246), "신규" => UiPaint.C(250, 170, 40), _ => UiPaint.C(90, 104, 130) };
        static string TagWord(string tag) => tag switch { "이벤트" => "EVENT", "업데이트" => "UPDATE", "신규" => "NEW", _ => "NOTICE" };

        /// <summary>
        /// 공지 as the reference's UPDATE INFO board (the BA cross-check, 24-Notice 4.5: "rigid boxy
        /// list vs asymmetrical overlapping slanted banners, plain type, characters only inside one
        /// banner"): a full page on the menu scene, the latest date as a big outlined headline with two
        /// SD chibis standing on the banner under it, and every notice as an illustrated slanted banner
        /// — the newest large on the left, two stacked on the right, the rest in a row on a glass shelf —
        /// each with its tag as a ribbon over its top edge. A banner opens the article as a card.
        /// </summary>
        public static void OpenNotice(AppRoot app, System.Action changed)
        {
            var p = Game.Player;
            var all = NoticeService.All.OrderByDescending(x => x.date).ToList();
            var page = UiKit.Div("nboard");
            Chrome.PaintScene(page);

            var head = UiKit.Div("nboard__head", page);
            var back = new Button(() => { AudioService.Play("back", 0.55f); app.CloseOverlay(); }) { text = "" };
            back.AddToClassList("nboard__back");
            ModalFrame.Painted(back, (ctx, r) =>
            {
                var c = r.center; var rad = Mathf.Min(r.width, r.height) * 0.5f;
                UiPaint.Fill(ctx, UiPaint.Ellipse(c, rad, rad), UiPaint.C(255, 255, 255));
                UiPaint.Fill(ctx, UiPaint.Ellipse(c, rad - 5f, rad - 5f), UiPaint.C(30, 48, 84));
                var k = rad * 0.36f; var t = rad * 0.09f;
                UiPaint.Fill(ctx, new List<Vector2> { new(c.x - k, c.y - t), new(c.x + k, c.y - t), new(c.x + k, c.y + t), new(c.x - k, c.y + t) }, Color.white);
                foreach (var sgn in new[] { 1f, -1f })
                {
                    var a0 = new Vector2(c.x - k, c.y); var a1 = new Vector2(c.x - k * 0.1f, c.y + sgn * k * 0.9f);
                    var n = new Vector2(-(a1 - a0).y, (a1 - a0).x).normalized * t;
                    UiPaint.Fill(ctx, new List<Vector2> { a0 - n, a1 - n, a1 + n, a0 + n }, Color.white);
                }
            });
            Juice.Press(back);
            head.Add(back);
            UiKit.Text("공지", "nboard__ko", head);
            UiKit.Text("NOTICE", "nboard__en", head);

            var top = all.FirstOrDefault();
            var md = top != null && System.DateTime.TryParse(top.date, out var d) ? $"{d.Month}.{d.Day:00}" : "";
            var title = UiKit.Text($"{md} UPDATE INFO", "nboard__title", page);
            title.pickingMode = PickingMode.Ignore;

            var grid = UiKit.Div("nboard__grid", page);
            // the glass shelf under the bottom row (the reference's frosted panel behind its small banners)
            var shelf = UiKit.Div("nboard__shelf", grid); shelf.pickingMode = PickingMode.Ignore;
            ModalFrame.Painted(shelf, (ctx, r) =>
            {
                var q = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.35f, 14f);
                UiPaint.Fill(ctx, q, UiPaint.Vertical(UiPaint.C(255, 255, 255, 0.62f), UiPaint.C(236, 246, 255, 0.4f), r.yMin, r.yMax), 0f);
                UiPaint.Stroke(ctx, q, UiPaint.C(255, 255, 255, 0.9f), 2f);
            });

            VisualElement card = null;
            void Article(NoticeService.Notice n)
            {
                NoticeService.Read(p, n); Game.Touch(); changed?.Invoke();
                card?.RemoveFromHierarchy();
                card = UiKit.Div("nboard__dim", page);
                card.RegisterCallback<ClickEvent>(e => { if (e.target == card) { card.RemoveFromHierarchy(); card = null; Refresh(); } });
                var view = UiKit.Div("notice__view nboard__card", card);
                var col = TagColour(n.tag);
                var art = Resources.Load<Sprite>("Art/Notice/" + n.id);
                var banner = UiKit.Div("notice__banner" + (art != null ? " notice__banner--art" : ""), view);
                ModalFrame.Painted(banner, (ctx, r) =>
                {
                    var box = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.25f, 8f);
                    if (art != null)
                    {
                        UiPaint.Shadow(ctx, box, new Vector2(0f, 4f), UiPaint.C(0, 20, 50, 0.22f), 10f);
                        UiPaint.Image(ctx, box, art, r, 0.4f);
                        UiPaint.Fill(ctx, box, UiPaint.Horizontal(UiPaint.WithAlpha(col, 0.88f), UiPaint.WithAlpha(col, 0f), r.xMin, r.xMin + r.width * 0.5f), 0f);
                        UiPaint.Stroke(ctx, box, UiPaint.C(255, 255, 255, 0.9f), 2f);
                        return;
                    }
                    UiPaint.Fill(ctx, box, UiPaint.Horizontal(col, Color.Lerp(col, Color.white, 0.55f), r.xMin, r.xMax));
                });
                UiKit.Text(TagWord(n.tag), "notice__banner-en", banner).pickingMode = PickingMode.Ignore;
                UiKit.Text("EXCEL HEROES  ·  사내 공지", "notice__banner-sub", banner).pickingMode = PickingMode.Ignore;
                var tagRow = UiKit.Div("notice__tagrow", view);
                Tag(tagRow, n.tag);
                UiKit.Text(n.date, "notice__date", tagRow);
                UiKit.Text(n.title, "notice__title", view);
                UiKit.Div("notice__rule", view);
                var sc = new ScrollView(ScrollViewMode.Vertical); sc.AddToClassList("notice__scroll"); view.Add(sc);
                UiKit.Text(n.body, "notice__body", sc);
                var close = new Button(() => { AudioService.Play("tap", 0.5f); card.RemoveFromHierarchy(); card = null; Refresh(); }) { text = "닫기" };
                close.AddToClassList("nboard__close");
                SkewPlate.Apply(close, SkewPlate.Kind.Navy);
                view.Add(close);
                Juice.PressAll(card);
            }

            // slot boxes in the grid (2200 x 800 design px): big left, two stacked right, three on the shelf
            var slots = new[] { new Rect(0, 0, 1080, 440), new Rect(1120, 0, 1080, 205), new Rect(1120, 235, 1080, 205),
                                new Rect(30, 500, 690, 280), new Rect(755, 500, 690, 280), new Rect(1480, 500, 690, 280) };
            var tiles = new List<VisualElement>();
            void Refresh() { foreach (var t in tiles) t.Q(className: "nbanner__new")?.EnableInClassList("hidden", !(t.userData is NoticeService.Notice nn) || p.readNotices.Contains(nn.id)); }
            for (var i = 0; i < all.Count && i < slots.Length; i++)
            {
                var n = all[i]; var s = slots[i]; var big = i == 0; var side = i is 1 or 2;
                var tile = UiKit.Div("nbanner" + (big ? " nbanner--big" : side ? " nbanner--side" : " nbanner--small"), grid);
                tile.userData = n; tiles.Add(tile);
                tile.style.left = s.x; tile.style.top = s.y; tile.style.width = s.width; tile.style.height = s.height;
                var col = TagColour(n.tag);
                var art = Resources.Load<Sprite>("Art/Notice/" + n.id);
                ModalFrame.Painted(tile, (ctx, r) =>
                {
                    var sl = SkewPlate.SlantFor(r.height) * 0.42f;
                    var outer = UiPaint.SkewRect(r, sl, 12f);
                    UiPaint.Shadow(ctx, outer, new Vector2(0f, 8f), UiPaint.C(20, 40, 90, 0.28f), 16f);
                    UiPaint.Fill(ctx, outer, Color.white);
                    var ir = new Rect(r.x + 6f, r.y + 6f, r.width - 12f, r.height - 12f);
                    var inner = UiPaint.SkewRect(ir, sl * ir.height / r.height, 9f);
                    if (art != null) UiPaint.Image(ctx, inner, art, ir, 0.35f);
                    else UiPaint.Fill(ctx, inner, UiPaint.Horizontal(col, Color.Lerp(col, Color.white, 0.6f), ir.xMin, ir.xMax), 0f);
                    // a white wash where the words sit: from the left on the side banners (the reference's
                    // 드럼통 게), from the bottom on the others
                    if (side) UiPaint.Fill(ctx, inner, UiPaint.Horizontal(UiPaint.C(255, 255, 255, 0.96f), UiPaint.C(255, 255, 255, 0f), ir.xMin, ir.xMin + ir.width * 0.62f), 0f);
                    else UiPaint.Fill(ctx, inner, UiPaint.Vertical(UiPaint.C(10, 24, 60, 0f), UiPaint.C(10, 24, 60, 0.62f), ir.yMin + ir.height * 0.45f, ir.yMax), 0f);
                    // a thin tag-coloured edge along the bottom
                    UiPaint.Fill(ctx, UiPaint.Clip(new List<Vector2> { new(ir.xMin - sl, ir.yMax - 7f), new(ir.xMax + sl, ir.yMax - 7f), new(ir.xMax + sl, ir.yMax), new(ir.xMin - sl, ir.yMax) }, inner), col, 0f);
                });
                // the tag ribbon over the top edge, right
                var rib = UiKit.Div("nbanner__ribbon", tile); rib.pickingMode = PickingMode.Ignore;
                ModalFrame.Painted(rib, (ctx, r) =>
                {
                    var q = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height), 3f);
                    UiPaint.Shadow(ctx, q, new Vector2(0f, 3f), UiPaint.C(0, 0, 0, 0.2f), 6f);
                    UiPaint.Fill(ctx, q, UiPaint.Vertical(Color.Lerp(col, Color.white, 0.18f), col, r.yMin, r.yMax), 0f);
                });
                UiKit.Text(big || n.tag == "이벤트" ? TagWord(n.tag) : n.tag, "nbanner__ribbon-text", rib);
                var words = UiKit.Div("nbanner__words", tile); words.pickingMode = PickingMode.Ignore;
                UiKit.Text(n.title.Replace("[이벤트] ", ""), "nbanner__title", words).pickingMode = PickingMode.Ignore;
                var date = UiKit.Div("nbanner__date", words); date.pickingMode = PickingMode.Ignore;
                ModalFrame.Painted(date, (ctx, r) => UiPaint.Fill(ctx, UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.5f, r.height * 0.3f), side ? UiPaint.C(30, 70, 140) : UiPaint.C(30, 48, 84, 0.9f)));
                UiKit.Text(n.date.Replace("-", ".").Substring(5) + (n.tag == "이벤트" ? " ~ 10.13" : " 업데이트"), "nbanner__date-text", date).pickingMode = PickingMode.Ignore;
                UiKit.Div("nbanner__new", tile).pickingMode = PickingMode.Ignore;
                tile.RegisterCallback<ClickEvent>(_ => { AudioService.Play("tap", 0.5f); Article(n); });
                Juice.Press(tile);
            }
            // two SD chibis standing on the upper right banner, under the headline (the reference's
            // characters peeking over its top banners)
            var ids = p.party != null ? p.party.Where(x => !string.IsNullOrEmpty(x)).Take(2).ToList() : new List<string>();
            foreach (var fb in new[] { GameData.MainId, "hr_jung", "barista" }) if (ids.Count < 2 && !ids.Contains(fb)) ids.Add(fb);
            for (var k = 0; k < ids.Count && k < 2; k++)
            {
                var sd = GameData.SdArt(ids[k]);
                if (sd == null) continue;
                var chib = UiKit.Div("nboard__chibi nboard__chibi--" + k, grid); chib.pickingMode = PickingMode.Ignore;
                chib.style.backgroundImage = new StyleBackground(sd);
            }
            Refresh();
            app.OpenOverlay(page);
        }

        static void Tag(VisualElement parent, string tag)
        {
            var col = TagColour(tag);
            var chip = UiKit.Div("ntag", parent);
            ModalFrame.Painted(chip, (ctx, r) => UiPaint.Fill(ctx, UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.6f, 3f), col));
            UiKit.Text(tag, "ntag__text", chip).pickingMode = PickingMode.Ignore;
        }
    }
}
