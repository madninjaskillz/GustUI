using System.Collections.Generic;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using Microsoft.Xna.Framework;

namespace GustUI.Elements
{
    /// <summary>
    /// Draws a polyline through a display list of element-relative points —
    /// the "sequencer automation lane" primitive (a straight-segment sibling
    /// of <see cref="NodeWireLayerElement"/>'s Béziers; no polyline primitive
    /// existed in the sprite batch before this). Deliberately carries NO
    /// mouse traits, same rule as <see cref="NodeWireLayerElement"/>: the
    /// host places its own small hit elements (e.g. point dots) over the
    /// affordances, this element only draws the connecting geometry.
    ///
    /// Two uses, same element: a per-channel OVERLAY curve (thin, low
    /// opacity, no dots — view only) and an EXPANDED lane's connecting ramp
    /// lines between its editable point dots (thicker, full opacity).
    /// </summary>
    [ElementTraits(typeof(PositionTrait), typeof(SizeTrait))]
    public class AutomationCurveElement : Element
    {
        /// <summary>Element-relative polyline vertices, in draw order. Fewer
        /// than 2 points draws nothing.</summary>
        public List<Vector2> Points = new List<Vector2>();

        public Color LineColor = new Color(120, 220, 160);

        public int Thickness = 1;

        [System.ThreadStatic] private static Vector2[] scratch;

        public override void Draw()
        {
            if (Points.Count >= 2)
            {
                var manager = Resources.StaticResources.DrawManager;
                Vector2 origin = this.GetActualXnaPosition();

                // One joined, antialiased stroke (StrokeGeometry) rather than a
                // quad per segment: no notch or overlap at the points, and a
                // flat run stays on the pixel grid.
                if (scratch == null || scratch.Length < Points.Count)
                {
                    scratch = new Vector2[System.Math.Max(64, Points.Count * 2)];
                }

                for (int i = 0; i < Points.Count; i++)
                {
                    scratch[i] = origin + Points[i];
                }

                System.Span<Color> color = stackalloc Color[] { LineColor };
                manager.DrawStroke(new System.ReadOnlySpan<Vector2>(scratch, 0, Points.Count), color, Thickness);
            }

            base.Draw();
        }
    }
}
