using System;
using System.Collections.Generic;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
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
        /// established), sampling a FIXED per-element-height gradient
        /// texture at each column's ABSOLUTE row range rather than
        /// stretching a small texture per column — a column re-stretching
        /// the same 0..1 gradient over its own (varying) height would make a
        /// short, quiet column look just as saturated near its own bottom as
        /// a tall, loud column does near ITS bottom, which reads as visually
        /// inconsistent once both are on screen together. Sampling fixed
        /// absolute rows instead means opacity at a given panel height is
        /// the same no matter which column reads it.</summary>
        private void DrawFill(Managers.DrawManager manager, Vector2 origin, Vector2 size)
        {
            int width = (int)size.X;
            int height = (int)size.Y;
            if (width < 1 || height < 2)
            {
                return;
            }

            float baselineY = Math.Clamp(Baseline ?? size.Y, 0f, size.Y);
            Texture2D gradient = GetFillGradientTexture(height);

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
                var source = new Rectangle(0, y0, 1, fillHeight);
                manager.Draw(gradient, dest, source, FillColor);
            }
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
                manager.DrawThickLine(previous, next, color, thickness);
                previous = next;
            }
        }

        private void DrawLiveMarker(Managers.DrawManager manager, Vector2 center)
        {
            if (Glow)
            {
                int haloDiameter = LiveMarkerDiameter * 2;
                var haloRect = new Rectangle(
                    (int)(center.X - haloDiameter / 2f), (int)(center.Y - haloDiameter / 2f), haloDiameter, haloDiameter);
                manager.BeginAdditive();
                manager.Draw(GetLiveDotTexture(haloDiameter), haloRect, LiveMarkerColor * 0.35f);
                manager.EndAdditive();
            }

            int diameter = LiveMarkerDiameter;
            var dotRect = new Rectangle(
                (int)(center.X - diameter / 2f), (int)(center.Y - diameter / 2f), diameter, diameter);
            manager.Draw(GetLiveDotTexture(diameter), dotRect, LiveMarkerColor);
        }

        private static readonly Dictionary<int, Texture2D> FillGradientCache = new Dictionary<int, Texture2D>();

        /// <summary>1px-wide, element-height-tall alpha ramp (opaque top,
        /// transparent bottom), baked once per height — the fixed texture
        /// <see cref="DrawFill"/> slices per column. White so the caller's
        /// tint color fully controls hue, same as every other baked-mask
        /// texture in this library (KnobElement's disc/ring, the rounded-rect
        /// corner atlas).</summary>
        private static Texture2D GetFillGradientTexture(int height)
        {
            if (FillGradientCache.TryGetValue(height, out Texture2D cached))
            {
                return cached;
            }

            var data = new Color[height];
            for (int y = 0; y < height; y++)
            {
                float alpha = height <= 1 ? 0.55f : MathHelper.Lerp(0.55f, 0f, y / (float)(height - 1));
                data[y] = Color.White * alpha;
            }

            var texture = new Texture2D(Resources.StaticResources.GraphicsDevice, 1, height);
            texture.SetData(data);
            FillGradientCache[height] = texture;
            return texture;
        }

        private static readonly Dictionary<int, Texture2D> LiveDotCache = new Dictionary<int, Texture2D>();

        /// <summary>Antialiased filled disc, baked once per diameter — same
        /// technique as <see cref="KnobElement"/>'s LiveValue rim marker
        /// (kept as an independent copy here rather than a shared helper:
        /// KnobElement's is private and the two libraries' baking idioms are
        /// meant to be copy-and-adapt per element, not centralized).</summary>
        private static Texture2D GetLiveDotTexture(int diameter)
        {
            if (LiveDotCache.TryGetValue(diameter, out Texture2D cached))
            {
                return cached;
            }

            var data = new Color[diameter * diameter];
            float r = diameter / 2f;
            for (int y = 0; y < diameter; y++)
            {
                for (int x = 0; x < diameter; x++)
                {
                    float dx = x - r + 0.5f;
                    float dy = y - r + 0.5f;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    float alpha = MathHelper.Clamp(r - dist, 0f, 1f);
                    data[y * diameter + x] = alpha <= 0f ? Color.Transparent : Color.White * alpha;
                }
            }

            var texture = new Texture2D(Resources.StaticResources.GraphicsDevice, diameter, diameter);
            texture.SetData(data);
            LiveDotCache[diameter] = texture;
            return texture;
        }
    }
}
