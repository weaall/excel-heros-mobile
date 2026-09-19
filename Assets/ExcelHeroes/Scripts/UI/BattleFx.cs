using System.Collections.Generic;
using ExcelHeroes.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// The hit effects, ported from the web build's Renderer#drawEffects.
    ///
    /// This is the piece the port was missing, and the reason a fight here read as two sprites
    /// swapping numbers while the same fight on the web reads as a brawl. On every landed hit the
    /// web draws an arc where the blade passed, a burst of radial lines at the contact point, a
    /// puff for a ranged hit, a spinning star on a crit, an expanding ground ring for anything
    /// heavy, and a shower of sparks when something dies. None of that existed here: there was a
    /// CSS class that nudged the sprite 22px and a floating number.
    ///
    /// UI Toolkit can draw all of it. `generateVisualContent` hands out a Painter2D with the same
    /// vocabulary the canvas version uses — arcs, lines, fills — so this is a port rather than a
    /// reinvention. One element owns the whole list and repaints itself, so the cost is a single
    /// draw call over the field instead of a VisualElement per spark.
    /// </summary>
    public class BattleFx
    {
        class Effect
        {
            public FxKind Kind;
            public float X, Y, Life, T, Radius, Angle;
            public bool Big;
            public Color Color;
        }

        readonly List<Effect> _live = new();
        readonly VisualElement _root;

        /// <summary>Field space to element space. The screen sets these every frame.</summary>
        public float ScaleX = 1f, ScaleY = 1f;

        /// <summary>
        /// How much bigger to draw an effect than the source does.
        ///
        /// The first cut of this used the web build's raw pixel sizes — a 24px slash radius — on an
        /// element that is 1010 units wide showing an 832-unit field, and then that was displayed on
        /// a 486px-wide phone. The arcs came out about ten screen pixels across and were invisible
        /// in every capture. The sprites are scaled up to fill the taller field, so the effects have
        /// to be scaled with them or they are not drawing on the same picture.
        /// </summary>
        float Size => Mathf.Max(1f, ScaleY) * 1.6f;

        public BattleFx(VisualElement parent)
        {
            _root = new VisualElement { pickingMode = PickingMode.Ignore };
            _root.AddToClassList("fx-layer");
            _root.generateVisualContent += Paint;
            parent.Add(_root);
        }

        /// <summary>Lifetimes, from the source. Everything is a fifth of a second or so.</summary>
        static float LifeOf(FxKind kind) => kind switch
        {
            FxKind.Slash => 0.18f,
            FxKind.Puff => 0.25f,
            FxKind.Ring => 0.5f,
            FxKind.Sparkle => 0.45f,
            FxKind.Impact => 0.22f,
            FxKind.Crit => 0.35f,
            FxKind.Muzzle => 0.12f,
            _ => 0.25f,
        };

        public void Add(FxKind kind, float x, float y, Color color, float big)
        {
            _live.Add(new Effect
            {
                Kind = kind,
                X = x, Y = y, Color = color, Big = big > 0f,
                Life = LifeOf(kind),
                Radius = kind == FxKind.Ring ? Mathf.Max(40f, big) : 0f,
                // A slash tilts differently every swing, so a run of them never looks stamped.
                Angle = kind == FxKind.Slash ? Random.value * 0.8f - 0.4f : 0f,
            });
        }

        public void Clear() => _live.Clear();

        public void Tick(float dt)
        {
            if (_live.Count == 0) return;
            for (var i = _live.Count - 1; i >= 0; i--)
            {
                _live[i].T += dt;
                if (_live[i].T >= _live[i].Life) _live.RemoveAt(i);
            }
            _root.MarkDirtyRepaint();
        }

        void Paint(MeshGenerationContext ctx)
        {
            if (_live.Count == 0) return;
            var p = ctx.painter2D;

            foreach (var f in _live)
            {
                var k = Mathf.Clamp01(f.T / f.Life);
                var x = f.X * ScaleX;
                var y = f.Y * ScaleY;
                var fade = 1f - k;

                switch (f.Kind)
                {
                    case FxKind.Slash:
                    {
                        // Two arcs sweeping open, the outer one fainter: the shape a blade leaves.
                        var r = (f.Big ? 34f : 24f) * Size;

                        // A white core under the coloured arc. A single stroke in the grade colour
                        // vanishes against a dark street; the blown-out centre is what makes a
                        // swing read as a swing rather than as a coloured smudge.
                        p.strokeColor = new Color(1f, 1f, 1f, fade * 0.85f);
                        p.lineWidth = (f.Big ? 9f : 6f) * Size;
                        p.lineCap = LineCap.Round;
                        Arc(p, x, y, r, -1.1f + k * 0.6f, 0.9f + k * 0.8f, f.Angle);

                        p.strokeColor = new Color(f.Color.r, f.Color.g, f.Color.b, fade);
                        p.lineWidth = (f.Big ? 6f : 4f) * Size;
                        p.lineCap = LineCap.Round;
                        Arc(p, x, y, r, -1.2f + k * 0.6f, 1.0f + k * 0.8f, f.Angle);

                        p.strokeColor = new Color(f.Color.r, f.Color.g, f.Color.b, fade * 0.5f);
                        p.lineWidth = 2f * Size;
                        Arc(p, x, y, r + 6f * Size, -0.9f + k * 0.6f, 0.8f + k * 0.8f, f.Angle);
                        break;
                    }

                    case FxKind.Impact:
                    {
                        // Radial lines flying out of the contact point.
                        var n = f.Big ? 8 : 6;
                        var len = (f.Big ? 18f : 11f) * (0.4f + k) * Size;
                        var r0 = (3f + k * 6f) * Size;
                        p.strokeColor = new Color(f.Color.r, f.Color.g, f.Color.b, fade);
                        p.lineWidth = (f.Big ? 3f : 2f) * Size;
                        p.lineCap = LineCap.Round;
                        p.BeginPath();
                        for (var i = 0; i < n; i++)
                        {
                            var a = i / (float)n * Mathf.PI * 2f + 0.3f;
                            p.MoveTo(new Vector2(x + Mathf.Cos(a) * r0, y + Mathf.Sin(a) * r0));
                            p.LineTo(new Vector2(x + Mathf.Cos(a) * (r0 + len), y + Mathf.Sin(a) * (r0 + len)));
                        }
                        p.Stroke();
                        break;
                    }

                    case FxKind.Puff:
                    {
                        p.fillColor = new Color(f.Color.r, f.Color.g, f.Color.b, fade * 0.7f);
                        for (var i = 0; i < 4; i++)
                        {
                            var a = i * 1.57f + 0.4f;
                            var rr = (6f + k * 14f) * Size;
                            Disc(p, x + Mathf.Cos(a) * rr, y + Mathf.Sin(a) * rr, Mathf.Max(0.5f, 5f - k * 3f) * Size);
                        }
                        break;
                    }

                    case FxKind.Ring:
                    {
                        // Flattened, because it is a shockwave running along the ground.
                        var r = Mathf.Max(1f, 10f + (f.Radius - 10f) * Mathf.Sqrt(k)) * Size;
                        p.strokeColor = new Color(f.Color.r, f.Color.g, f.Color.b, fade * 0.8f);
                        p.lineWidth = (3f + fade * 3f) * Size;
                        Ellipse(p, x, y, r, r * 0.6f);
                        break;
                    }

                    case FxKind.Sparkle:
                    {
                        const int n = 8;
                        p.fillColor = new Color(f.Color.r, f.Color.g, f.Color.b, fade);
                        for (var i = 0; i < n; i++)
                        {
                            var a = i / (float)n * Mathf.PI * 2f + i;
                            var rr = (12f + k * 26f) * Size;
                            var sx = x + Mathf.Cos(a) * rr;
                            var sy = y - (20f + k * 40f) * Size + Mathf.Sin(a) * rr * 0.5f;
                            Box(p, sx - 2f * Size, sy - Size, 4f * Size, 2f * Size);
                            Box(p, sx - Size, sy - 2f * Size, 2f * Size, 4f * Size);
                        }
                        break;
                    }

                    case FxKind.Crit:
                    {
                        // A sixteen-point star that grows, spins and lifts.
                        var big = Mathf.Max(1f, 16f + k * 10f) * Size;
                        var small = big * 0.45f;
                        var spin = k * 0.6f;
                        var cy = y - k * 10f * Size;

                        p.fillColor = new Color(f.Color.r, f.Color.g, f.Color.b, fade);
                        p.BeginPath();
                        for (var i = 0; i < 16; i++)
                        {
                            var a = i / 16f * Mathf.PI * 2f + spin;
                            var rr = i % 2 == 1 ? small : big;
                            var pt = new Vector2(x + Mathf.Cos(a) * rr, cy + Mathf.Sin(a) * rr);
                            if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
                        }
                        p.ClosePath();
                        p.Fill();

                        p.fillColor = new Color(1f, 1f, 1f, fade);
                        Disc(p, x, cy, small * 0.5f);
                        break;
                    }

                    case FxKind.Muzzle:
                    {
                        p.fillColor = new Color(f.Color.r, f.Color.g, f.Color.b, fade);
                        Box(p, x, y - 3f * Size, (10f + k * 8f) * Size, 6f * Size);
                        p.fillColor = new Color(1f, 1f, 1f, fade);
                        Box(p, x + 2f * Size, y - Size, 6f * Size, 2f * Size);
                        break;
                    }
                }
            }
        }

        // --- painter helpers: Painter2D has no rotated arc, ellipse or rectangle ---------------

        static void Arc(Painter2D p, float x, float y, float r, float from, float to, float rotate)
        {
            const int steps = 14;
            p.BeginPath();
            for (var i = 0; i <= steps; i++)
            {
                var a = Mathf.Lerp(from, to, i / (float)steps) + rotate;
                var pt = new Vector2(x + Mathf.Cos(a) * r, y + Mathf.Sin(a) * r);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.Stroke();
        }

        static void Ellipse(Painter2D p, float x, float y, float rx, float ry)
        {
            const int steps = 24;
            p.BeginPath();
            for (var i = 0; i <= steps; i++)
            {
                var a = i / (float)steps * Mathf.PI * 2f;
                var pt = new Vector2(x + Mathf.Cos(a) * rx, y + Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.Stroke();
        }

        static void Disc(Painter2D p, float x, float y, float r)
        {
            const int steps = 12;
            p.BeginPath();
            for (var i = 0; i < steps; i++)
            {
                var a = i / (float)steps * Mathf.PI * 2f;
                var pt = new Vector2(x + Mathf.Cos(a) * r, y + Mathf.Sin(a) * r);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath();
            p.Fill();
        }

        static void Box(Painter2D p, float x, float y, float w, float h)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.ClosePath();
            p.Fill();
        }
    }
}
