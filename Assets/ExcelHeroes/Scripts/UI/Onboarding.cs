using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// First run. The player arrives with 1,000 gems and no cards, and every screen is about cards
    /// they do not have yet — so the app opens on an empty home and reads as broken.
    ///
    /// This is not a tutorial that explains systems. It gives the premise in three lines, then puts
    /// them one tap from the only thing worth doing first: a ten-pull. The systems explain
    /// themselves once there are faces to hang them on.
    ///
    /// It is drawn as an Excel dialog rather than as a game popup, because that is the joke and
    /// because the shape is already familiar: a grey caption bar, a message with an icon beside it,
    /// the content laid out in worksheet rows, and the buttons bottom-right where every dialog in
    /// Office puts them. The previous version was a plain dark card with three centred lines, which
    /// belonged to neither the game nor the disguise.
    /// </summary>
    public class Onboarding
    {
        readonly AppRoot _app;

        public Onboarding(AppRoot app) { _app = app; }

        /// <summary>True until the player owns anyone — the honest test for "has this game started".</summary>
        public static bool Needed(PlayerState p) => p.owned.Count == 0;

        public void Show()
        {
            var dialog = UiKit.Div("xl-dialog");

            // Caption bar. Named like a real one, because a dialog titled "환영합니다!" is the one
            // thing on screen that would give the game away over a shoulder.
            var caption = UiKit.Div("xl-dialog__caption", dialog);
            UiKit.Text("신규 통합 문서 설정", "xl-dialog__caption-title", caption);
            UiKit.Div("spacer", caption);
            UiKit.Btn(Icons.Close, "xl-dialog__close icon", Close, caption);

            var body = UiKit.Div("xl-dialog__body", dialog);

            var head = UiKit.Div("xl-dialog__head", body);
            UiKit.Text("i", "xl-dialog__icon", head);
            var headText = UiKit.Div("xl-dialog__head-text", head);
            UiKit.Text("입사를 환영합니다", "xl-dialog__title", headText);
            UiKit.Text("아래 내용을 확인한 뒤 계속하세요.", "xl-dialog__subtitle", headText);

            // The premise, in worksheet rows. Row numbers down the side are doing the same work
            // here as they do on the sheets behind this dialog.
            var sheet = UiKit.Div("xl-dialog__sheet", body);
            var lines = new[]
            {
                "스프레드시트 괴물이 서울을 덮쳤습니다.",
                "당신은 오늘 입사한 신입 김인턴입니다.",
                "정규직 전환까지, 동료를 모으고 오류를 처리하세요.",
            };
            for (var i = 0; i < lines.Length; i++)
            {
                var row = UiKit.Div("xl-row", sheet);
                UiKit.Text((i + 1).ToString(), "xl-row__n", row);
                var cell = UiKit.Text(lines[i], "xl-row__cell", row);
                cell.style.whiteSpace = WhiteSpace.Normal;
            }

            // The one row that is a number, formatted as a number — right-aligned, accented.
            var grant = UiKit.Div("xl-row xl-row--grant", sheet);
            UiKit.Text("4", "xl-row__n", grant);
            UiKit.Text("입사 지원금", "xl-row__cell", grant);
            UiKit.Text($"{Game.Player.gems:N0}", "xl-row__value", grant);

            UiKit.Text($"10회 모집에 {GameData.Balance.gachaTenCost}개가 듭니다. 먼저 동료부터 뽑으세요.",
                "xl-dialog__note", body);

            var foot = UiKit.Div("xl-dialog__foot", dialog);
            UiKit.Btn("둘러보기", "xl-btn", Close, foot);
            UiKit.Btn("모집하러 가기", "xl-btn xl-btn--default", () =>
            {
                Close();
                _app.Show(AppRoot.Sheet.Gacha);
            }, foot);

            _app.OpenOverlay(dialog);
        }

        void Close()
        {
            _app.Overlay.Clear();
            _app.Overlay.AddToClassList("hidden");
        }
    }
}
