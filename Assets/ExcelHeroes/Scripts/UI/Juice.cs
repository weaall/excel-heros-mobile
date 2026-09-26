using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// How the reference game answers a touch, in three pieces (docs/design/BA_REFERENCE.md §5):
    ///
    ///   PRESS  — the thing under the finger sinks to 0.94, and on release overshoots to 1.04
    ///            before settling. A plain shrink-and-restore reads as a switch; the overshoot is
    ///            what makes it feel like a soft physical button.
    ///   TOUCH  — wherever the screen is touched, a small cyan ring and a few triangle shards
    ///            spread and fade. It is on every touch, not only on buttons, which is why tapping
    ///            empty space in the reference still feels answered.
    ///   ENTER  — a new screen slides in from the right a short way while fading up.
    ///
    /// All three are USS transitions on transform and opacity, so they cost nothing per frame
    /// beyond what the panel already does.
    /// </summary>
    public static class Juice
    {
        /// <summary>Gives `el` the press-and-spring. Safe to call more than once.</summary>
        public static void Press(VisualElement el)
        {
            if (el == null || el.ClassListContains("juice")) return;
            el.AddToClassList("juice");
            el.RegisterCallback<PointerDownEvent>(_ =>
            {
                el.AddToClassList("juice--down");
                el.RemoveFromClassList("juice--up");
            }, TrickleDown.TrickleDown);
            void Release()
            {
                if (!el.ClassListContains("juice--down")) return;
                el.RemoveFromClassList("juice--down");
                el.AddToClassList("juice--up");
                el.schedule.Execute(() => el.RemoveFromClassList("juice--up")).ExecuteLater(110);
            }
            el.RegisterCallback<PointerUpEvent>(_ => Release());
            el.RegisterCallback<PointerLeaveEvent>(_ => Release());
        }

        /// <summary>Every button under `root` that does not have the press yet gets it.</summary>
        public static void PressAll(VisualElement root)
        {
            root?.Query<Button>().ForEach(Press);
        }

        /// <summary>Puts the touch effect on a layer covering `root`.</summary>
        public static void Touches(VisualElement root)
        {
            if (root == null) return;
            var layer = new VisualElement { pickingMode = PickingMode.Ignore, name = "touchLayer" };
            layer.AddToClassList("touch-layer");
            root.Add(layer);

            // Trickle-down on the root sees every touch before anything handles it, including
            // touches that land on nothing.
            root.RegisterCallback<PointerDownEvent>(e =>
            {
                layer.BringToFront();
                Burst(layer, layer.WorldToLocal(e.position));
            }, TrickleDown.TrickleDown);
        }

        static void Burst(VisualElement layer, Vector2 at)
        {
            var fx = new VisualElement { pickingMode = PickingMode.Ignore };
            fx.AddToClassList("touch-fx");
            fx.style.left = at.x - 60f;
            fx.style.top = at.y - 60f;
            var seed = (int)(at.x * 7 + at.y * 13);
            ModalFrame.Painted(fx, (ctx, r) =>
            {
                var c = r.center;
                // A ring: the gap between two ellipses, as two fills (outer cyan, inner clear
                // would need a concave shape, so the ring is a stroke on the circle).
                UiPaint.Stroke(ctx, UiPaint.Ellipse(c, r.width * 0.42f, r.height * 0.42f, 36), UiPaint.C(90, 220, 255, 0.9f), 4f);
                var rng = new System.Random(seed);
                for (var i = 0; i < 5; i++)
                {
                    var a = (float)(rng.NextDouble() * Mathf.PI * 2);
                    var d = r.width * (0.3f + 0.2f * (float)rng.NextDouble());
                    var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                    var s = 7f + 6f * (float)rng.NextDouble();
                    var rot = (float)(rng.NextDouble() * Mathf.PI * 2);
                    var tri = new List<Vector2>
                    {
                        p + new Vector2(Mathf.Cos(rot), Mathf.Sin(rot)) * s,
                        p + new Vector2(Mathf.Cos(rot + 2.1f), Mathf.Sin(rot + 2.1f)) * s,
                        p + new Vector2(Mathf.Cos(rot + 4.2f), Mathf.Sin(rot + 4.2f)) * s,
                    };
                    // Keep the winding clockwise on screen for the fill's anti-alias fringe.
                    if ((tri[1].x - tri[0].x) * (tri[2].y - tri[0].y) - (tri[1].y - tri[0].y) * (tri[2].x - tri[0].x) < 0f)
                        (tri[1], tri[2]) = (tri[2], tri[1]);
                    UiPaint.Fill(ctx, tri, i % 2 == 0 ? UiPaint.C(90, 220, 255, 0.9f) : UiPaint.C(255, 255, 255, 0.95f));
                }
            });
            layer.Add(fx);
            // Next frame: grow and fade, then remove.
            fx.schedule.Execute(() => fx.AddToClassList("touch-fx--go")).ExecuteLater(16);
            fx.schedule.Execute(() => fx.RemoveFromHierarchy()).ExecuteLater(420);
        }

        /// <summary>Slides a freshly built screen in.</summary>
        public static void Enter(VisualElement screen)
        {
            if (screen == null) return;
            screen.AddToClassList("screen-enter");
            screen.schedule.Execute(() => screen.AddToClassList("screen-enter--go")).ExecuteLater(16);
            screen.schedule.Execute(() =>
            {
                screen.RemoveFromClassList("screen-enter");
                screen.RemoveFromClassList("screen-enter--go");
            }).ExecuteLater(320);
        }
    }
}
