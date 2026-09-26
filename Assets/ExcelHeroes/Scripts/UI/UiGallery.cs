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
