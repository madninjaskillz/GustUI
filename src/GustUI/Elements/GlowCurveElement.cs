using System;
using System.Collections.Generic;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Managers;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GustUI.Elements
{
    /// <summary>
    /// <see cref="AutomationCurveElement"/>'s Serum-caliber sibling: the same
    /// element-relative polyline contract (<see cref="Points"/>), but drawn
    /// with a gradient-filled area under the curve and an additive-blended
    /// glow stroke instead of a single flat line, plus an optional live
    /// tracking dot (<see cref="LiveMarker"/>) for "this is animating because
    /// a note is actually playing right now" displays (Cerebrum's ENV tab).
    /// Deliberately a new element rather than extending
    /// <see cref="AutomationCurveElement"/> in place — that element is reused
    /// elsewhere (sequencer automation lanes) with no glow wanted there.
    /// </summary>
    [ElementTraits(typeof(PositionTrait), typeof(SizeTrait))]
    public class GlowCurveElement : Element
    {
        /// <summary>Element-relative polyline vertices, in draw order,
        /// non-decreasing in X (same assumption <see cref="BuildFill"/>'s
        /// column sampling makes — every current caller already produces
        /// left-to-right curves: envelope shape, wavetable frame). Fewer
        /// than 2 points draws nothing.</summary>
        public List<Vector2> Points = new List<Vector2>();

        public Color LineColor = new Color(120, 220, 160);

        public int Thickness = 2;

        /// <summary>Draws a gradient-filled area between the curve and
        /// <see cref="Baseline"/> (default: the element's bottom edge).</summary>
        public bool ShowFill = true;

        /// <summary>Tint for the area fill — the alpha FADE is baked into a
        /// fixed per-element-height gradient texture (<see cref="GetFillGradientTexture"/>),
        /// this just recolors it, same idiom <see cref="WaveformElement"/>'s
        /// <c>Tint</c> uses over its column rects.</summary>
        public Color FillColor = new Color(120, 220, 160);

        /// <summary>
        /// Per-column fill tint, by element-relative X — overrides
        /// <see cref="FillColor"/> when set.
        ///
        /// For a curve whose MEANING changes along its length: a spectrum split
        /// into bands wants each band's stretch of fill in that band's own
        /// colour, so the picture and the controls under it read as the same
        /// thing. The fill is already drawn one column at a time, so asking per
        /// column costs a delegate call and nothing else.
        /// </summary>
        public Func<float, Color>? FillColorAt;

        /// <summary>Per-column line tint, same contract as
        /// <see cref="FillColorAt"/>; null keeps <see cref="LineColor"/>.</summary>
        public Func<float, Color>? LineColorAt;

        /// <summary>
        /// Alpha at the top of the fill and at the bottom of the fade.
        ///
        /// The default fades to nothing over the ELEMENT, which is what an
        /// envelope wants: its curve hugs the top of its box and the fill hangs
        /// beneath. A spectrum's curve sits wherever the music is, so the same
        /// fade starts faint and is gone before the baseline — the fill was
        /// invisible and only the line read. Callers whose curve does not live
        /// near the top should raise <see cref="FillBottomAlpha"/> or set
        /// <see cref="FadeFillAcrossElement"/> false.
        /// </summary>
        public float FillTopAlpha = 0.55f;

        public float FillBottomAlpha = 0f;

        /// <summary>Whether the fade is measured across the whole element
        /// (default, an envelope's shape) or across each column's OWN span from
        /// the curve down to the baseline.</summary>
        public bool FadeFillAcrossElement = true;

        /// <summary>Element-relative Y the fill drops to; null = the
        /// element's own bottom edge (the common case — a baseline knob is
        /// only useful for a curve that doesn't span the full height).</summary>
        public float? Baseline;

        /// <summary>Additive-blended multi-stroke bloom under the crisp core
        /// line (and around <see cref="LiveMarker"/>, if set). False falls
        /// back to a single flat stroke — <see cref="AutomationCurveElement"/>'s
        /// old look — for callers that decide the glow reads wrong for them.</summary>
        public bool Glow = true;

        /// <summary>Glow tint; null reuses <see cref="LineColor"/>.</summary>
        public Color? GlowColor;

        /// <summary>Element-relative position of a live-tracking dot (e.g. an
        /// envelope's current stage position while a note plays); null draws
        /// no dot. The host is responsible for computing this position from
        /// live engine state every frame — this element only draws it.</summary>
        public Vector2? LiveMarker;

        public Color LiveMarkerColor = new Color(255, 196, 64);

        public int LiveMarkerDiameter = 10;

        public override void Draw()
        {
            if (Points.Count >= 2)
            {
                var manager = Resources.StaticResources.DrawManager;
                Vector2 origin = this.GetActualXnaPosition();
                Vector2 size = this.GetSize().AsXna;

                if (ShowFill)
                {
                    DrawFill(manager, origin, size);
                }

                DrawGlowLine(manager, origin);

                if (LiveMarker.HasValue)
                {
                    DrawLiveMarker(manager, origin + LiveMarker.Value);
                }
            }

            base.Draw();
        }

        /// <summary>Column-fill area under the curve (the same "one 1px rect
        /// per x-column" idiom <see cref="WaveformElement"/>.DrawColumns
        /// established), each column a 2-color vertical vertex-color
        /// gradient (<see cref="SpriteBatchExtensions.DrawFilledRectangleGradient"/>)
        /// sampling the SAME fixed absolute-row alpha curve
        /// (<see cref="FillAlphaAt"/>) at its own y0/baseline — real GPU
        /// interpolation, not a texture slice, so a short quiet column
        /// reads the same opacity-at-a-given-panel-height as a tall loud
        /// one, with no bake/DPI mismatch to go soft on.</summary>
        private void DrawFill(Managers.DrawManager manager, Vector2 origin, Vector2 size)
        {
            int width = (int)size.X;
            int height = (int)size.Y;
            if (width < 1 || height < 2)
            {
                return;
            }

            float baselineY = Math.Clamp(Baseline ?? size.Y, 0f, size.Y);

            for (int x = 0; x < width; x++)
            {
                float y = Math.Clamp(SampleY(Points, x), 0f, baselineY);
                int y0 = (int)y;
                int fillHeight = (int)baselineY - y0;
                if (fillHeight <= 0)
                {
                    continue;
                }

                var dest = new Rectangle((int)origin.X + x, (int)origin.Y + y0, 1, fillHeight);
                Color tint = FillColorAt?.Invoke(x) ?? FillColor;

                // Across the element, or across this column's own fill: the
                // second keeps a fill readable wherever the curve happens to
                // sit, which is what a spectrum needs.
                float topAlpha = FadeFillAcrossElement ? FillAlphaAt(y0, height) : FillTopAlpha;
                float bottomAlpha = FadeFillAcrossElement
                    ? FillAlphaAt(y0 + fillHeight, height)
                    : FillBottomAlpha;

                manager.DrawFilledRectangleGradient(dest, tint * topAlpha, tint * bottomAlpha, Direction.Vertically);
            }
        }

        /// <summary>Fixed opaque-top/transparent-bottom alpha curve over the
        /// element's absolute row range — the same fade
        /// <see cref="GetFillGradientTexture"/> used to bake into a texture,
        /// now evaluated directly per column endpoint.</summary>
        private float FillAlphaAt(int y, int height)
        {
            return height <= 1
                ? FillTopAlpha
                : MathHelper.Lerp(FillTopAlpha, FillBottomAlpha,
                    MathHelper.Clamp(y, 0, height - 1) / (float)(height - 1));
        }

        /// <summary>Linear-interpolated curve Y at element-relative X (points
        /// assumed non-decreasing in X); clamps to the nearest endpoint
        /// outside the curve's own X range.</summary>
        private static float SampleY(List<Vector2> points, float x)
        {
            if (x <= points[0].X)
            {
                return points[0].Y;
            }

            for (int i = 1; i < points.Count; i++)
            {
                if (x <= points[i].X)
                {
                    Vector2 a = points[i - 1];
                    Vector2 b = points[i];
                    float t = b.X > a.X ? (x - a.X) / (b.X - a.X) : 0f;
                    return MathHelper.Lerp(a.Y, b.Y, t);
                }
            }

            return points[points.Count - 1].Y;
        }

        private void DrawGlowLine(Managers.DrawManager manager, Vector2 origin)
        {
            if (Glow)
            {
                Color glow = GlowColor ?? LineColor;
                manager.BeginAdditive();
                DrawPolyline(manager, origin, glow * 0.12f, Thickness + 6);
                DrawPolyline(manager, origin, glow * 0.22f, Thickness + 3);
                manager.EndAdditive();
            }

            DrawPolyline(manager, origin, LineColor, Thickness);
        }

        private void DrawPolyline(Managers.DrawManager manager, Vector2 origin, Color color, int thickness)
        {
            Vector2 previous = origin + Points[0];
            for (int i = 1; i < Points.Count; i++)
            {
                Vector2 next = origin + Points[i];

                // Tinted per SEGMENT when a caller asks, sampled at the
                // segment's midpoint — a segment spans a few pixels, so one
                // colour for it is indistinguishable from a gradient and costs
                // one call instead of one per pixel.
                Color segment = LineColorAt != null
                    ? LineColorAt((Points[i - 1].X + Points[i].X) * 0.5f) * (color.A / 255f)
                    : color;

                manager.DrawThickLine(previous, next, segment, thickness);
                previous = next;
            }
        }

        private void DrawLiveMarker(Managers.DrawManager manager, Vector2 center)
        {
            if (Glow)
            {
                int haloDiameter = LiveMarkerDiameter * 2;
                manager.BeginAdditive();
                manager.DrawFilledCircle(center, haloDiameter / 2f, LiveMarkerColor * 0.35f);
                manager.EndAdditive();
            }

            manager.DrawFilledCircle(center, LiveMarkerDiameter / 2f, LiveMarkerColor);
        }
    }
}
