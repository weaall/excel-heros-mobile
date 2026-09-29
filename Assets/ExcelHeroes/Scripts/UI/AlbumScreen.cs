using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 사원_앨범 — the illustration gallery, and the only place the card art is seen at its own size.
    ///
    /// The art is the reason to pull, so it gets a viewer rather than a thumbnail: tap a card and it
    /// fills the screen, and the three outfits (기본 / 캐주얼 / 정장) switch in place. Everything else
    /// about a hero lives in the roster; this sheet is only for looking.
    /// </summary>
    public class AlbumScreen : IScreen
    {
        public string Cell => "A1";
        public string Formula => "=COUNTA(사원_사진!B:B)";

        readonly AppRoot _app;
        VisualElement _root;
        Pages<HeroDef> _pages;
        Label _count;
        bool _ownedOnly;

        public AlbumScreen(AppRoot app) => _app = app;

        public IEnumerable<RibbonItem> Ribbon()
        {
            yield return new RibbonItem("▩", _ownedOnly ? "전체 보기" : "보유만",
                () => { _ownedOnly = !_ownedOnly; _app.Rebuild(); });
            yield return new RibbonItem("★", "대표 지정", ChooseLead);
        }

        public VisualElement Build()
        {
            _root = UiKit.Div("album");

            var head = UiKit.Div("sheet-head", _root);
            _count = UiKit.Text("", "sheet-head__stat", head);
            UiKit.Text("일러스트를 누르면 원본 크기로 열립니다", "muted", head);

            // Six illustrations across, two down.
            _pages = new Pages<HeroDef>(_root, "album__grid", 12).Empty("아직 사원이 없습니다");

            Refresh();
            return _root;
        }

        public void Refresh()
        {
            if (_pages == null) return;

            var p = Game.Player;
            _count.text = $"<color=#00D2FF>COLLECTION</color>   <color=#FFFFFF>{p.owned.Count}</color> <color=#7E95B3>/ {GameData.Heroes.Count}</color>";

            _pages.Fill(GameData.Heroes.Where(h => !_ownedOnly || p.Owns(h.id)).ToList(), (def, grid) =>
            {
                var owned = p.Owns(def.id);
                var cell = UiKit.Div(owned ? "album__cell" : "album__cell album__cell--locked", grid);
                var art = UiKit.Div("album__art", cell);
                UiKit.SetArt(art, GameData.CardArt(def.id));
                if (!owned)
                {
                    // the roster's locked look, so a missing picture reads the same everywhere
                    var scrim = UiKit.Div("album__scrim", cell);
                    scrim.style.backgroundColor = new Color(0.06f, 0.1f, 0.19f, 0.62f);   // Linear-space alpha, see UiKit.Card
                }
                // the name on a soft fade rather than a solid slab (ui_score 13-Album #1), the grade as
                // a small slanted tag in the corner
                var fade = UiKit.Div("album__fade", cell); fade.pickingMode = PickingMode.Ignore;
                ModalFrame.Painted(fade, (ctx, r) => UiPaint.Fill(ctx, UiPaint.RoundRect(r, 0f), UiPaint.Vertical(UiPaint.C(14, 24, 44, 0f), UiPaint.C(10, 18, 36, 1f), r.yMin, r.yMin + r.height * 0.7f)));
                UiKit.Text(owned ? def.name : "미보유", "album__name", cell);
                var gcol = GameData.Grade(def.grade)?.Color ?? Color.gray;
                var gtag = UiKit.Div("album__grade", cell); gtag.pickingMode = PickingMode.Ignore;
                ModalFrame.Painted(gtag, (ctx, r) => UiPaint.Fill(ctx, UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.6f, 2f), owned ? gcol : UiPaint.C(70, 84, 110)));
                UiKit.Text(def.grade, "album__grade-text", gtag).pickingMode = PickingMode.Ignore;
                if (!owned) return;
                var id = def.id;
                cell.RegisterCallback<ClickEvent>(_ => Open(id));
            });
        }

        /// <summary>
        /// The full-size viewer. It draws the source illustration uncropped on a dimmed backdrop
        /// rather than scaling up the grid thumbnail, which is the difference between a zoom and a
        /// blurry crop — the grid cell is square and the art is 2:3.
        /// </summary>
        void Open(string heroId)
        {
            var def = GameData.Hero(heroId);
            var skins = GameData.SkinsOf(heroId);
            var index = 0;

            var panel = UiKit.Div("viewer");
            var art = UiKit.Div("viewer__art", panel);
            ArtMotion.Breathe(art);
            var caption = UiKit.Div("viewer__caption", panel);
            UiKit.Text(def.name, "viewer__name", caption);
            UiKit.Text($"{def.nick} · {def.dept}", "viewer__nick", caption);

            // One illustration per character now, so the outfit tabs only appear if a hero
            // actually has more than one — a row containing a single "기본" button is furniture.
            var buttons = new List<Button>();
            if (skins.Count > 1)
            {
                var tabs = UiKit.Div("viewer__skins", panel);
                for (var i = 0; i < skins.Count; i++)
                {
                    var slot = i;
                    var label = skins[i] switch { "casual" => "캐주얼", "formal" => "정장", _ => "기본" };
                    buttons.Add(UiKit.Btn(label, "viewer__skin", () => { index = slot; Apply(); }, tabs));
                }
            }

            UiKit.Btn("닫기", "btn btn--ghost viewer__close", _app.CloseOverlay, panel);

            Apply();
            _app.OpenOverlay(panel);

            void Apply()
            {
                UiKit.SetArt(art, GameData.CardArt(heroId, index < skins.Count ? skins[index] : null));
                for (var i = 0; i < buttons.Count; i++)
                    buttons[i].EnableInClassList("viewer__skin--active", i == index);
            }
        }

        /// <summary>The lead card is who greets you; picking one is the only edit this sheet makes.</summary>
        void ChooseLead()
        {
            var p = Game.Player;
            var panel = UiKit.Div("picker");
            UiKit.Text("대표 사원", "section-title", panel);
            var list = UiKit.Scroll("picker__list", panel);

            foreach (var o in p.owned)
            {
                var def = GameData.Hero(o.id);
                if (def == null) continue;
                var id = o.id;
                var row = UiKit.Div("picker__row", list);
                UiKit.SetArt(UiKit.Div("picker__face", row), GameData.CardArt(id));
                UiKit.Text(def.name, "picker__name", row);
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    p.leadHeroId = id;
                    Game.Touch();
                    _app.CloseOverlay();
                });
            }

            UiKit.Btn("닫기", "btn", _app.CloseOverlay, panel);
            _app.OpenOverlay(panel);
        }
    }
}
