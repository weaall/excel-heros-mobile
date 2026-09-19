using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// Finds text that does not fit and elements that fall off the screen.
    ///
    /// Clipping is the one class of bug that passes every logic check and every compile, and that
    /// nobody sees until they look at the screen — a label whose box is narrower than its own text
    /// just quietly loses the end of the sentence. This walks the built tree and measures, so the
    /// screenshot pass reports it in words instead of leaving it to be spotted in a picture.
    /// </summary>
    public static class LayoutAudit
    {
        public struct Problem
        {
            public string Where;
            public string What;
            public override string ToString() => $"{Where}: {What}";
        }

        public static List<Problem> Run(VisualElement root)
        {
            var problems = new List<Problem>();
            var screen = root.worldBound;
            Walk(root, root, screen, problems);
            return problems;
        }

        static void Walk(VisualElement e, VisualElement root, Rect screen, List<Problem> problems)
        {
            // An element with no layout yet cannot be judged; it is measured on a later frame.
            if (e.resolvedStyle.display == DisplayStyle.None) return;

            var box = e.worldBound;
            if (box.width > 1f && box.height > 1f)
            {
                // Off the screen entirely, or hanging past its right/bottom edge by more than a
                // rounding error. Scroll views legitimately overflow, so their contents are skipped.
                if (!InsideAScrollView(e) && !InsideAClip(e))
                {
                    if (box.xMax > screen.xMax + 1f)
                        problems.Add(new Problem { Where = Describe(e), What = $"{box.xMax - screen.xMax:F0}px past the right edge" });
                    if (box.yMax > screen.yMax + 1f)
                        problems.Add(new Problem { Where = Describe(e), What = $"{box.yMax - screen.yMax:F0}px below the bottom edge" });
                }

            }

            if (e is Label label && !string.IsNullOrEmpty(label.text))
            {
                var wraps = label.resolvedStyle.whiteSpace == WhiteSpace.Normal;
                var size = label.MeasureTextSize(label.text, 0, VisualElement.MeasureMode.Undefined,
                                                 0, VisualElement.MeasureMode.Undefined);
                var content = label.contentRect;
                if (content.width > 1f && !wraps && size.x > content.width + 1.5f)
                    problems.Add(new Problem
                    {
                        Where = Describe(label),
                        What = $"text \"{Trim(label.text)}\" needs {size.x:F0}px, has {content.width:F0}px",
                    });
                if (content.height > 1f && size.y > content.height + 1.5f)
                    problems.Add(new Problem
                    {
                        Where = Describe(label),
                        What = $"text \"{Trim(label.text)}\" needs {size.y:F0}px of height, has {content.height:F0}px",
                    });
            }

            foreach (var child in e.Children()) Walk(child, root, screen, problems);
        }

        /// <summary>
        /// Some elements are meant to overrun their parent and be cropped by it — a cut-in's
        /// diagonal sweep is drawn oversized on purpose so its edges never show. USS exposes no
        /// resolved `overflow` to read, so the containers that clip on purpose say so with a
        /// class, and everything inside one is exempt. Without this the audit cries wolf on
        /// decoration, and an audit that reports things that are fine stops being read.
        /// </summary>
        // KNOWN BLIND SPOT: clipping INSIDE a box.
        //
        // Everything here measures against the screen's edges, so an element cut in half by the
        // card it sits in reads as a clean run. That is not hypothetical — every 읽기 button on
        // 메신저 was cut by four pixels while this reported zero problems.
        //
        // Detecting it needs to know which ancestors hide their overflow, and UI Toolkit does not
        // expose that: `resolvedStyle` has no `overflow`, `OverflowInternal` is internal and
        // `computedStyle` is not public. Marking every clipping container with a class would work
        // and would drift out of date the first time someone adds one. So: a fixed-size box with
        // content in it still needs a human to look at the capture once.
        static bool InsideAClip(VisualElement e)
        {
            // The cropping box itself, not just what is inside it. A full-bleed stage is laid out
            // to fill the frame exactly, and a flex-grown element measured against a 2400px design
            // resolution on a 1080px screen lands a pixel or three either side of the edge through
            // rounding alone. Reporting the crop container for that is noise; its CONTENTS are what
            // the check is for, and they are exempt for a different reason — they are meant to run
            // off the edge and be cut.
            //
            // The tolerance is deliberately not widened for everything instead: a real defect this
            // session was a button cut by four pixels, and a four-pixel blanket would have hidden it.
            if (e.ClassListContains("clips")) return true;

            for (var p = e.parent; p != null; p = p.parent)
                if (p.ClassListContains("clips")) return true;
            return false;
        }

        static bool InsideAScrollView(VisualElement e)
        {
            for (var p = e.parent; p != null; p = p.parent)
                if (p is ScrollView) return true;
            return false;
        }

        static string Trim(string s) => s.Length <= 24 ? s : s.Substring(0, 24) + "…";

        static string Describe(VisualElement e)
        {
            var sb = new StringBuilder(e.GetType().Name);
            if (!string.IsNullOrEmpty(e.name)) sb.Append('#').Append(e.name);
            foreach (var c in e.GetClasses()) sb.Append('.').Append(c);
            return sb.ToString();
        }
    }
}
