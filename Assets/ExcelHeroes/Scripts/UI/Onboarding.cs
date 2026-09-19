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
    /// </summary>
    public class Onboarding
    {
        readonly AppRoot _app;

        public Onboarding(AppRoot app) { _app = app; }

        /// <summary>True until the player owns anyone — the honest test for "has this game started".</summary>
        public static bool Needed(PlayerState p) => p.owned.Count == 0;

        public void Show()
        {
            var overlay = _app.Overlay;
            overlay.Clear();
            overlay.RemoveFromClassList("hidden");

            var view = UiKit.Div("onboard");
            UiKit.Div("onboard__bg", view);

            var card = UiKit.Div("onboard__card", view);
            UiKit.Text("입사를 환영합니다", "onboard__title", card);

            // The premise, in the voice the rest of the game is written in.
            UiKit.Text("스프레드시트 괴물이 서울을 덮쳤습니다.", "onboard__line", card);
            UiKit.Text("당신은 오늘 입사한 신입 김인턴입니다.", "onboard__line", card);
            UiKit.Text("정규직 전환까지, 동료를 모으고 오류를 처리하세요.", "onboard__line", card);

            var tip = UiKit.Div("onboard__tip", card);
            UiKit.Text($"보석 {Game.Player.gems:N0}개가 지급되었습니다", "onboard__tip-title", tip);
            UiKit.Text($"10회 모집이 {GameData.Balance.gachaTenCost}개입니다. 먼저 동료부터 뽑으세요.",
                "muted", tip);

            UiKit.Btn("모집하러 가기", "btn btn--primary", () =>
            {
                AudioService.Play("nav");
                Close();
                _app.Show(AppRoot.Sheet.Gacha);
            }, card);

            UiKit.Btn("둘러보기", "btn btn--ghost", Close, card);

            overlay.Add(view);
        }

        void Close()
        {
            _app.Overlay.Clear();
            _app.Overlay.AddToClassList("hidden");
        }
    }
}
