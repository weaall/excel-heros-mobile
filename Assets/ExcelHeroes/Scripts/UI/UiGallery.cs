using System;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// Every plate kind and the modal, laid out like the reference sheet so a capture of this and
    /// the sheet can be put side by side. Only the screenshot driver opens it.
    ///
    /// A button only ever looked "different in kind" from the reference when it was seen beside
    /// the reference. Screens show two or three plates at a time, of one or two kinds; this shows
    /// all of them at once, in the sheet's own order.
    /// </summary>
    public static class UiGallery
    {
        /// <summary>
        /// Every grade's sheet side by side, each on a different trait, star count and level,
        /// plus an awakened one — the whole ladder on one page, as BackSheet describes it.
        /// </summary>
        public static VisualElement BuildSheets(System.Action close)
        {
            var root = UiKit.Div("kit");
            var col = UiKit.Div("kit__col", root);
            UiKit.Text("(7) 등 뒤 시트 — 등급 D → S, 특성·★·레벨별", "kit__cap", col);
            var row = UiKit.Div("kit__sheets", col);
            var samples = new (string grade, string trait, int star, float fill, bool awake)[]
            {
                ("D", "swift", 1, 0.6f, false), ("C", "crit", 2, 0.5f, false), ("B", "lifesteal", 3, 0.7f, false),
                ("A", "rally", 4, 0.8f, false), ("S", "splash", 5, 1f, true),
            };
            // every hero's sheet, small, to check that each reads as its own (the mark + colour)
            if (System.Environment.GetEnvironmentVariable("SHEETS_ALL") == "1")
            {
                row.AddToClassList("kit__sheets--all");
                foreach (var def in Data.GameData.Heroes)
                {
                    var o = new Core.OwnedHero(def.id) { star = 3 };
                    o.level = UnityEngine.Mathf.RoundToInt(Core.StatMath.LevelCap(o) * 0.5f);
                    var cell = UiKit.Div("kit__sheet-cell kit__sheet-cell--mini", row);
                    BackSheet.Add(cell, def, o, "backsheet kit__sheet");
                    UiKit.Text(def.name, "kit__sheet-label", cell);
                }
                return root;
            }
            foreach (var (grade, trait, star, fill, awake) in samples)
            {
                var def = Data.GameData.Heroes.Find(h => h.grade == grade && h.trait == trait) ?? Data.GameData.Heroes.Find(h => h.grade == grade);
                var o = new Core.OwnedHero(def.id) { star = star, awakened = awake, skillLv = 2 };
                o.level = UnityEngine.Mathf.RoundToInt(Core.StatMath.LevelCap(o) * fill);
                var cell = UiKit.Div("kit__sheet-cell", row);
                BackSheet.Add(cell, def, o, "backsheet kit__sheet");
                UiKit.Text($"{grade} · {Data.GameData.Trait(def.trait)?.name ?? def.trait} · ★{star}", "kit__sheet-label", cell);
            }
            return root;
        }

        public static VisualElement Build(Action close)
        {
            var root = UiKit.Div("kit");

            var left = UiKit.Div("kit__col", root);
            UiKit.Text("(1) 기본 버튼", "kit__cap", left);
            var grid = UiKit.Div("kit__grid", left);
            UiKit.Btn("확인", "btn btn--primary kit__btn", null, grid);
            UiKit.Btn("취소", "btn btn--ghost kit__btn", null, grid);
            UiKit.Btn("업무 시작", "btn btn--glow kit__btn", null, grid);
            UiKit.Btn("출근!", "btn kit__btn", null, grid);
            UiKit.Btn("▲  ▼", "btn btn--glass kit__btn", null, grid);
            UiKit.Btn("보상 정보", "btn kit__btn", null, grid);
            var off = UiKit.Btn("잠김", "btn kit__btn", null, grid);
            off.SetEnabled(false);
            UiKit.Btn("모집", "btn btn--gold kit__btn", null, grid);
            UiKit.Btn("방출", "btn btn--danger kit__btn", null, grid);
            UiKit.Btn("전체 선택", "btn btn--chip kit__btn", null, grid);
            UiKit.Text("(2) 탭 · 아이콘 버튼", "kit__cap", left);
            var tabs = UiKit.Div("kit__grid", left);
            UiKit.Btn("영웅", "btn btn--pill-on kit__btn", null, tabs);
            UiKit.Btn("장비", "btn btn--pill kit__btn", null, tabs);
            foreach (var ic in new[] { "mail", "notice", "settings" })
            {
                var rb = UiKit.Btn("", "btn btn--round kit__round", null, tabs);
                var a = Data.GameData.Icon(ic); if (a != null) UiKit.SetArt(UiKit.Div("kit__round-icon", rb), a);
            }

            var right = UiKit.Div("kit__col", root);
            UiKit.Text("(5) 모달", "kit__cap", right);
            var body = UiKit.Modal("1장 완료!", close, out var panel, "kit__modal");
            right.Add(panel);
            UiKit.Text("수고하셨습니다!", "modal__lead", body);
            UiKit.Text("이번 Phase의 성과가 정산되었습니다.", "modal__sub", body);
            var inset = UiKit.Div("modal__inset", body);
            ModalFrame.Inset(inset);
            UiKit.RewardTile(Icons.Gem, "x3", "icon", inset);
            UiKit.RewardTile(Icons.Gold, "+1,200", "icon rtile__glyph--gold", inset);
            UiKit.RewardTile(Icons.Star, "x3", "icon", inset, UiPaint.C(190, 120, 255, 0.45f));
            UiKit.Ribbon("획득 보상", inset);
            var acts = UiKit.Div("modal__acts", body);
            UiKit.Btn("다음 Phase", "btn btn--primary", close, acts);
            UiKit.Btn("다시 하기", "btn", close, acts);
            return root;
        }
    }
}
