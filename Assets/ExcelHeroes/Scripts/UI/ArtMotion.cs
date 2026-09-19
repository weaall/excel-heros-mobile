using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// Makes a card illustration breathe, without touching the illustration.
    ///
    /// The ask was one design per character plus a little motion. The first attempt at that
    /// generated extra frames — breathe, blink, talk, smile — and every frame came back with a
    /// slightly different face, so the card visibly changed person as it animated. The art is fixed;
    /// what moves is the camera on it.
    ///
    /// Two slow sine waves, deliberately at different periods so they never line up into an obvious
    /// loop: a 5.5s swell that scales by 1.5%, and a 7.3s drift of a few pixels. Anchored low, so
    /// the swell reads as breathing rather than as zooming — the feet stay put and the shoulders
    /// rise. Subtle enough that you notice it stop, not start.
    /// </summary>
    public static class ArtMotion
    {
        const float SwellPeriod = 5.5f;
        const float DriftPeriod = 7.3f;
        const float SwellAmount = 0.015f;
        const float DriftPixels = 6f;

        /// <summary>
        /// Starts the motion. It rides the panel's scheduler, so it stops on its own when the
        /// element leaves the tree — no coroutine to remember to cancel when a sheet is rebuilt.
        /// </summary>
        public static void Breathe(VisualElement art, float phase = 0f)
        {
            if (art == null) return;

            art.style.transformOrigin = new StyleTransformOrigin(
                new TransformOrigin(Length.Percent(50), Length.Percent(90)));

            var t = phase;
            art.schedule.Execute(() =>
            {
                t += Time.deltaTime;
                var swell = 1f + Mathf.Sin(t / SwellPeriod * Mathf.PI * 2f) * SwellAmount;
                var drift = Mathf.Sin(t / DriftPeriod * Mathf.PI * 2f) * DriftPixels;
                art.style.scale = new StyleScale(new Scale(new Vector2(swell, swell)));
                art.style.translate = new StyleTranslate(new Translate(0, drift));
            }).Every(33);   // 30fps is plenty for something this slow, and costs a third of 60
        }
    }
}
