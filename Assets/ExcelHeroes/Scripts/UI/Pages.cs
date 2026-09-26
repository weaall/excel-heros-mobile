using System;
using System.Collections.Generic;
using ExcelHeroes.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// A list that turns pages instead of scrolling.
    ///
    /// Held sideways there is no scrolling anywhere in this build. A scroll on a landscape screen
    /// is a list read through a slot: the content is wider than it is tall, so a vertical scroll
    /// shows six rows at a time out of a frame that could hold sixteen side by side, and the bar
    /// itself is a desktop affordance sitting in a place a thumb never rests.
    ///
    /// So every list that outgrows its frame becomes a fixed grid plus two arrows. A page holds a
    /// constant number of cells, which means an item is always in the same place on the screen —
    /// something a scroll position can never promise — and the count tells you how much is left,
    /// which a scrollbar only implies.
    /// </summary>
    public class Pages<T>
    {
        readonly VisualElement _body;
        readonly Button _prev, _next;
        readonly Label _label;
        readonly int _perPage;

        int _page;
        IReadOnlyList<T> _items = Array.Empty<T>();
        Action<T, VisualElement> _build;
        string _emptyText = "";

        /// <param name="perPage">Cells one page holds — decided by the USS that sizes them.</param>
        public Pages(VisualElement parent, string bodyClasses, int perPage)
        {
            _perPage = Mathf.Max(1, perPage);
            _body = UiKit.Div(bodyClasses, parent);

            var pager = UiKit.Div("pager", parent);
            _prev = UiKit.Btn("", "pager__btn", () => Turn(-1), pager);
            _label = UiKit.Text("", "pager__label", pager);
            _next = UiKit.Btn("", "pager__btn", () => Turn(1), pager);
            Round(_prev, -1);
            Round(_next, 1);
        }

        /// <summary>
        /// The reference's page arrows: a round white button with a soft shadow and a navy
        /// chevron, faded when there is nowhere to go.
        /// </summary>
        static void Round(Button b, int dir)
        {
            ModalFrame.Painted(b, (ctx, r) =>
            {
                var on = b.enabledInHierarchy;
                var d = Mathf.Min(r.width, r.height);
                var c = r.center;
                var disc = UiPaint.Ellipse(c, d * 0.5f, d * 0.5f);
                UiPaint.Shadow(ctx, disc, new Vector2(0f, 3f), UiPaint.C(20, 40, 80, on ? 0.2f : 0.08f), 6f);
                UiPaint.Fill(ctx, disc, UiPaint.Vertical(UiPaint.C(255, 255, 255, on ? 1f : 0.6f), UiPaint.C(234, 242, 249, on ? 1f : 0.6f), r.yMin, r.yMax));
                UiPaint.Stroke(ctx, disc, UiPaint.C(176, 200, 226, on ? 1f : 0.5f), 1.5f);
                var s = d * 0.16f;
                var ink = on ? UiPaint.C(36, 52, 84) : UiPaint.C(150, 162, 180);
                var chevron = new List<Vector2>
                {
                    c + new Vector2(-dir * s * 0.6f, -s), c + new Vector2(-dir * s * 0.6f + dir * s * 0.45f, -s),
                    c + new Vector2(dir * s * 0.75f, 0f),
                    c + new Vector2(-dir * s * 0.6f + dir * s * 0.45f, s), c + new Vector2(-dir * s * 0.6f, s),
                    c + new Vector2(dir * s * 0.3f, 0f),
                };
                UiPaint.Fill(ctx, chevron, ink);
            });
            Juice.Press(b);
        }

        /// <summary>What the pager says when the list is empty — the page count has nothing to say.</summary>
        public Pages<T> Empty(string text) { _emptyText = text; return this; }

        public VisualElement Body => _body;

        void Turn(int by)
        {
            _page += by;
            AudioService.Play("nav", 0.4f);
            Render();
        }

        /// <summary>Replaces the contents. The page is kept where it can be, so a refresh that
        /// changes one row does not throw the reader back to the front.</summary>
        public void Fill(IReadOnlyList<T> items, Action<T, VisualElement> build)
        {
            _items = items ?? Array.Empty<T>();
            _build = build;
            Render();
        }

        void Render()
        {
            var pages = Mathf.Max(1, Mathf.CeilToInt(_items.Count / (float)_perPage));
            _page = Mathf.Clamp(_page, 0, pages - 1);

            _body.Clear();
            for (var i = _page * _perPage; i < Mathf.Min(_items.Count, (_page + 1) * _perPage); i++)
                _build(_items[i], _body);

            _label.text = _items.Count == 0 ? _emptyText : $"{_page + 1} / {pages}";
            _prev.SetEnabled(_page > 0);
            _next.SetEnabled(_page < pages - 1);
            // One page of content needs no page turning, and two dead arrows under every short
            // list is furniture pretending to be a control.
            _prev.EnableInClassList("hidden", pages <= 1);
            _next.EnableInClassList("hidden", pages <= 1);
        }
    }
}
