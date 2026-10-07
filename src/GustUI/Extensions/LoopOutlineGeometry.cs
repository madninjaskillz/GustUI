using Microsoft.Xna.Framework;
using System;

namespace GustUI.Extensions
{
    /// <summary>
    /// The shape of a sequencer block's outline, as a handful of axis-aligned
    /// rectangles (ezmuze bug board #212; ezmuze design-guide.md, "A block's
    /// corners are two steps, and a repeat is a notch", 2026-10-07).
    ///
    /// FOUR STRAIGHT EDGES WITH THE CORNERS STEPPED OFF. Each edge stops two
    /// strokes short of the corner, and one stroke-square sits on the
    /// diagonal between them: a 45-degree step that reads as a softened
    /// corner. It replaced four antialiased quarter-ring arcs per block,
    /// which cost trig and ~64 vertices for a 4px radius the eye barely
    /// resolved. A block too small for two steps gets one (the corner square
    /// simply left out), and one too small for that gets a plain box.
    ///
    /// A SEAM IS A NOTCH. Where the pattern starts again, a short tick hangs
    /// down from the top edge and another stands up from the bottom one, and
    /// nothing crosses the middle of the block — a full-height rule would
    /// compete with the content (a kick transient is also a bright vertical
    /// stroke) and with the bar grid. It replaced a "pinch" of four corner
    /// arcs per seam, ~72 vertices and sixteen sin/cos pairs each; a notch is
    /// two quads. A seam too close to either end to clear the corner step is
    /// dropped.
    ///
    /// Pure arithmetic so it can be tested without a graphics device; the
    /// draw call is <see cref="ShapeDrawExtensions.DrawLoopOutline"/>.
    /// </summary>
    public static class LoopOutlineGeometry
    {
        /// <summary>Seams read per block; more are ignored.</summary>
        public const int MaxSeams = 64;

        /// <summary>The longest a notch gets, in logical pixels.</summary>
        public const int MaxNotchLength = 4;

        /// <summary>Upper bound on what <see cref="Build"/> writes for
        /// <paramref name="seamCount"/> seams: four edges, four corner steps,
        /// two notches a seam.</summary>
        public static int MaxRects(int seamCount) => 8 + (2 * Math.Max(0, seamCount));

        /// <summary>How many strokes each corner is stepped off: 2 when the
        /// block is at least six strokes each way, 1 at three, else 0.</summary>
        public static int CornerSteps(int width, int height, int thickness)
        {
            int t = Math.Max(1, thickness);
            int least = Math.Min(width, height);
            return least >= t * 6 ? 2 : least >= t * 3 ? 1 : 0;
        }

        /// <summary>
        /// Writes the outline's rectangles into <paramref name="output"/>
        /// (at least <see cref="MaxRects"/> long) and returns how many.
        /// <paramref name="seams"/> are x offsets from the rectangle's left
        /// edge, ascending.
        /// </summary>
        public static int Build(Rectangle rect, int thickness, ReadOnlySpan<float> seams, Span<Rectangle> output)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return 0;
            }

            int t = Math.Max(1, thickness);
            int n = 0;
            int steps = CornerSteps(rect.Width, rect.Height, t);
            if (steps == 0)
            {
                // Too small to shape: a plain box (or a solid block when the
                // strokes meet in the middle).
                if (rect.Width <= t * 2 || rect.Height <= t * 2)
                {
                    output[n++] = rect;
                    return n;
                }

                output[n++] = new Rectangle(rect.Left, rect.Top, rect.Width, t);
                output[n++] = new Rectangle(rect.Left, rect.Bottom - t, rect.Width, t);
                output[n++] = new Rectangle(rect.Left, rect.Top + t, t, rect.Height - (t * 2));
                output[n++] = new Rectangle(rect.Right - t, rect.Top + t, t, rect.Height - (t * 2));
                return n;
            }

            int inset = steps * t;
            int top = rect.Top;
            int bottom = rect.Bottom - t;

            // Top and bottom edges, the two sides, each stopping `steps`
            // strokes short of the corner.
            output[n++] = new Rectangle(rect.Left + inset, top, rect.Width - (inset * 2), t);
            output[n++] = new Rectangle(rect.Left + inset, bottom, rect.Width - (inset * 2), t);
            output[n++] = new Rectangle(rect.Left, top + inset, t, rect.Height - (inset * 2));
            output[n++] = new Rectangle(rect.Right - t, top + inset, t, rect.Height - (inset * 2));

            // The diagonal stroke-square that turns two missing strokes into
            // a step rather than a hole.
            if (steps == 2)
            {
                output[n++] = new Rectangle(rect.Left + t, top + t, t, t);
                output[n++] = new Rectangle(rect.Right - (t * 2), top + t, t, t);
                output[n++] = new Rectangle(rect.Left + t, bottom - t, t, t);
                output[n++] = new Rectangle(rect.Right - (t * 2), bottom - t, t, t);
            }

            int inner = rect.Height - (t * 2);
            int notch = Math.Min(MaxNotchLength, inner / 4);
            if (notch < 1)
            {
                return n;
            }

            // A notch must clear the corner step on both sides, with a stroke
            // of edge between them.
            int minX = rect.Left + inset + t;
            int maxX = rect.Right - inset - (t * 2);
            int count = Math.Min(seams.Length, MaxSeams);
            for (int i = 0; i < count; i++)
            {
                int x = (int)Math.Round(rect.Left + seams[i]);
                if (x < minX || x > maxX)
                {
                    continue;
                }

                output[n++] = new Rectangle(x, top + t, t, notch);
                output[n++] = new Rectangle(x, bottom - notch, t, notch);
            }

            return n;
        }
    }
}
