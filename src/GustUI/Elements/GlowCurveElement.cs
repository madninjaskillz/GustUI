using System;
using System.Collections.Generic;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Managers;
using GustUI.Rendering;
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
        /// than 2 points draws nothing.
        ///
        /// The drawn geometry is CACHED (see "cached geometry" below): assign
        /// a new list to change the curve, or call
        /// <see cref="InvalidateGeometry"/> after editing this one in place.</summary>
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

                if (EnsureGeometry(origin, size))
                {
                    DrawCached(manager, origin - builtOrigin);
                }
                else
                {
                    if (ShowFill)
                    {
                        DrawFill(manager, origin, size);
                    }

                    DrawGlowLine(manager, origin);
                }

                if (LiveMarker.HasValue)
                {
                    DrawLiveMarker(manager, origin + LiveMarker.Value);
                }
            }

            base.Draw();
        }

        // ---- cached geometry ------------------------------------------------
        //
        // WHY (2026-09-23). A curve is re-tessellated from scratch every frame
        // otherwise: one rotated quad per segment per stroke (three strokes
        // with Glow on) plus one gradient quad per fill column, each costing an
        // Atan2, a square root, a sine and a cosine. Bifrost's panel has three
        // wavetable displays of eleven curves each, and an idle panel spent
        // most of its frame redrawing pictures that had not changed.
        //
        // So the quads are built ONCE into local arrays and handed to
        // DrawManager.DrawCachedTriangles every frame, which is an Array.Copy
        // into the batch — the path WaveformElement's cached geometry already
        // takes. They are rebuilt only when something they were built from
        // changes, which is checked cheaply, per frame, without looking at the
        // points: a new Points LIST (compared against the snapshot only then,
        // so a caller that rebuilds an identical list costs one compare rather
        // than a rebuild), a different Count, any styling field, the size, or
        // InvalidateGeometry(). A caller that edits Points IN PLACE must call
        // InvalidateGeometry() — there is no way to see that cheaply.
        //
        // PIXEL-IDENTICAL, not approximately so. The vertices come from the
        // same helpers the immediate path calls (ShapeDrawExtensions.
        // ThickLineQuad, GeometryBatch.WriteRotatedQuad/WriteQuadGradient),
        // built in ABSOLUTE coordinates at the origin they were built for, so
        // at rest the offset handed to the shader is exactly zero and every
        // vertex is the float the immediate path would have produced. Colours
        // are unfaded (the draw's tint carries Element opacity) and clip rect
        // is NoClip (the draw's uniform carries the real one) — the contract
        // AppendCachedTriangles documents.
        //
        // MOVING does not invalidate — scrolling a panel or relaying it out
        // shifts the whole curve by a whole number of pixels, and the shader
        // adds the offset. That is only equivalent to rebuilding while the
        // (int) truncations the immediate path makes (DrawThickLine's segment
        // start, the fill's column origin) move by the same whole number, i.e.
        // while nothing crosses zero, so a fractional move or one that crosses
        // zero rebuilds instead. And it is only NEARLY exact (float rounding
        // differs at the edges of a few pixels), so the first frame the curve
        // holds still after moving, it rebuilds where it came to rest: exact
        // at rest, cheap in motion. See IsCurrent.
        //
        // PAINT ORDER is unchanged: each pass is its own cached draw, closing
        // the open segment before it and opening a fresh one after, so it lands
        // in the batch exactly where the immediate draws did — fill, glow
        // (additive), core line. With Glow off the fill and the line share one
        // draw: triangles within a draw blend in index order, fill first.

        private struct GeometryKey : IEquatable<GeometryKey>
        {
            public Vector2 Size;
            public Color LineColor;
            public int Thickness;
            public bool ShowFill;
            public Color FillColor;
            public Func<float, Color> FillColorAt;
            public Func<float, Color> LineColorAt;
            public float FillTopAlpha;
            public float FillBottomAlpha;
            public bool FadeFillAcrossElement;
            public float? Baseline;
            public bool Glow;
            public Color? GlowColor;
            public int Version;

            public bool Equals(GeometryKey other) =>
                Size == other.Size && LineColor == other.LineColor && Thickness == other.Thickness
                && ShowFill == other.ShowFill && FillColor == other.FillColor
                && ReferenceEquals(FillColorAt, other.FillColorAt) && ReferenceEquals(LineColorAt, other.LineColorAt)
                && FillTopAlpha == other.FillTopAlpha && FillBottomAlpha == other.FillBottomAlpha
                && FadeFillAcrossElement == other.FadeFillAcrossElement && Baseline == other.Baseline
                && Glow == other.Glow && GlowColor == other.GlowColor && Version == other.Version;
        }

        private struct CachedPass
        {
            public GeometryVertex[] Vertices;
            public short[] Indices;
            public int Primitives;
            public bool Additive;
        }

        /// <summary>Largest vertex count one cached pass may hold: its indices
        /// are 16-bit and segment-relative. A curve past it (a fill thousands of
        /// columns wide) is drawn the immediate way instead.</summary>
        private const int MaxCachedVertices = short.MaxValue;

        private readonly CachedPass[] passes = new CachedPass[3];
        private int passCount;
        private bool hasGeometry;
        private bool geometryCacheable;
        private GeometryKey builtKey;
        private List<Vector2> builtFrom;
        private Vector2[] builtPoints = Array.Empty<Vector2>();
        private int builtPointCount;
        private Vector2 builtOrigin;
        private Vector2 lastOrigin;
        private Vector2 builtMin;
        private int version;

        [ThreadStatic] private static GeometryVertex[] scratchVertices;
        [ThreadStatic] private static short[] scratchIndices;

        /// <summary>How many times this curve's geometry has been built —
        /// a diagnostic, and what the tests read to tell a cache hit from a
        /// rebuild.</summary>
        public int GeometryBuilds { get; private set; }

        /// <summary>
        /// Throws away the cached geometry, so the next draw rebuilds it. Call
        /// after changing <see cref="Points"/> IN PLACE (assigning a new list
        /// needs nothing), or when <see cref="FillColorAt"/>/<see cref="LineColorAt"/>
        /// would now answer differently for the same X.
        /// </summary>
        public void InvalidateGeometry() => version++;

        private GeometryKey CurrentKey(Vector2 size) => new GeometryKey
        {
            Size = size,
            LineColor = LineColor,
            Thickness = Thickness,
            ShowFill = ShowFill,
            FillColor = FillColor,
            FillColorAt = FillColorAt,
            LineColorAt = LineColorAt,
            FillTopAlpha = FillTopAlpha,
            FillBottomAlpha = FillBottomAlpha,
            FadeFillAcrossElement = FadeFillAcrossElement,
            Baseline = Baseline,
            Glow = Glow,
            GlowColor = GlowColor,
            Version = version,
        };

        /// <summary>
        /// Makes the cached geometry current for a draw at
        /// <paramref name="origin"/>, rebuilding it only if something it was
        /// built from changed. False means draw the immediate way (the curve is
        /// too big to cache). Internal so the tests can drive it without a
        /// graphics device.
        /// </summary>
        internal bool EnsureGeometry(Vector2 origin, Vector2 size)
        {
            if (!hasGeometry || !IsCurrent(origin, size))
            {
                BuildGeometry(origin, size);
            }

            lastOrigin = origin;
            return geometryCacheable;
        }

        private bool IsCurrent(Vector2 origin, Vector2 size)
        {
            if (!builtKey.Equals(CurrentKey(size)) || Points.Count != builtPointCount)
            {
                return false;
            }

            if (!ReferenceEquals(Points, builtFrom))
            {
                // A new list. Most callers hand over a fresh one on every
                // refresh, identical or not, so compare it — once, here —
                // rather than rebuild.
                for (int i = 0; i < builtPointCount; i++)
                {
                    if (Points[i] != builtPoints[i])
                    {
                        return false;
                    }
                }

                builtFrom = Points;
            }

            if (origin == builtOrigin)
            {
                return true;
            }

            // Drawn somewhere other than where it was built. While it is
            // MOVING (scrolling, a panel sliding in) the shader's offset is
            // good enough -- but not bit-exact: (built + point) + offset rounds
            // differently from (built + offset) + point, and on a 4K screen
            // that flipped five edge pixels out of four million. So the first
            // frame it holds still, rebuild where it now is.
            if (origin == lastOrigin)
            {
                return false;
            }

            return OffsetReusable(builtOrigin.X, origin.X, builtMin.X)
                && OffsetReusable(builtOrigin.Y, origin.Y, builtMin.Y);
        }

        /// <summary>Whether geometry built at <paramref name="from"/> can be
        /// drawn at <paramref name="to"/> by adding the difference: a whole
        /// number of pixels, and no truncation in the build crossing zero at
        /// either end (<paramref name="min"/> is the most negative point
        /// coordinate, or 0).</summary>
        private static bool OffsetReusable(float from, float to, float min)
        {
            if (from == to)
            {
                return true;
            }

            float delta = to - from;
            return delta == MathF.Round(delta) && from + min >= 0f && to + min >= 0f;
        }

        private void BuildGeometry(Vector2 origin, Vector2 size)
        {
            GeometryBuilds++;
            hasGeometry = true;
            builtKey = CurrentKey(size);
            builtOrigin = origin;
            builtFrom = Points;
            builtPointCount = Points.Count;
            if (builtPoints.Length < builtPointCount)
            {
                builtPoints = new Vector2[builtPointCount];
            }

            Points.CopyTo(builtPoints);

            float minX = 0f, minY = 0f;
            for (int i = 0; i < builtPointCount; i++)
            {
                minX = Math.Min(minX, builtPoints[i].X);
                minY = Math.Min(minY, builtPoints[i].Y);
            }

            builtMin = new Vector2(minX, minY);

            passCount = 0;
            geometryCacheable = true;
            if (builtPointCount < 2)
            {
                return;
            }

            int vertexCount = 0, indexCount = 0;
            if (ShowFill)
            {
                WriteFill(origin, size, ref vertexCount, ref indexCount);
            }

            if (Glow)
            {
                FinishPass(ref vertexCount, ref indexCount, additive: false);
                Color glow = GlowColor ?? LineColor;
                WritePolyline(origin, glow * 0.12f, Thickness + 6, ref vertexCount, ref indexCount);
                WritePolyline(origin, glow * 0.22f, Thickness + 3, ref vertexCount, ref indexCount);
                FinishPass(ref vertexCount, ref indexCount, additive: true);
            }

            WritePolyline(origin, LineColor, Thickness, ref vertexCount, ref indexCount);
            FinishPass(ref vertexCount, ref indexCount, additive: false);
        }

        private void FinishPass(ref int vertexCount, ref int indexCount, bool additive)
        {
            if (vertexCount > MaxCachedVertices)
            {
                geometryCacheable = false;
            }

            if (geometryCacheable && indexCount > 0)
            {
                // DrawCachedTriangles takes the whole array, so each pass is
                // sized exactly — but a curve that moves (a modulated scan
                // position) usually rebuilds to the same size, so keep the
                // arrays when they still fit exactly.
                ref CachedPass pass = ref passes[passCount++];
                if (pass.Vertices == null || pass.Vertices.Length != vertexCount)
                {
                    pass.Vertices = new GeometryVertex[vertexCount];
                }

                if (pass.Indices == null || pass.Indices.Length != indexCount)
                {
                    pass.Indices = new short[indexCount];
                }

                Array.Copy(scratchVertices, pass.Vertices, vertexCount);
                Array.Copy(scratchIndices, pass.Indices, indexCount);
                pass.Primitives = indexCount / 3;
                pass.Additive = additive;
            }

            vertexCount = 0;
            indexCount = 0;
        }

        private static void ReserveQuad(int vertexCount, int indexCount)
        {
            if (scratchVertices == null || scratchVertices.Length < vertexCount + 4)
            {
                Array.Resize(ref scratchVertices, Math.Max(256, Math.Max(vertexCount + 4, (scratchVertices?.Length ?? 0) * 2)));
            }

            if (scratchIndices == null || scratchIndices.Length < indexCount + 6)
            {
                Array.Resize(ref scratchIndices, Math.Max(384, Math.Max(indexCount + 6, (scratchIndices?.Length ?? 0) * 2)));
            }
        }

        /// <summary><see cref="DrawFill"/>, written into the scratch buffers
        /// instead of the batch. Keep the two in step.</summary>
        private void WriteFill(Vector2 origin, Vector2 size, ref int vertexCount, ref int indexCount)
        {
            int width = (int)size.X;
            int height = (int)size.Y;
            if (width < 1 || height < 2)
            {
                return;
            }

            float baselineY = Math.Clamp(Baseline ?? size.Y, 0f, size.Y);
            int cursor = 1;
            for (int x = 0; x < width; x++)
            {
                float y = Math.Clamp(SampleY(Points, x, ref cursor), 0f, baselineY);
                int y0 = (int)y;
                int fillHeight = (int)baselineY - y0;
                if (fillHeight <= 0)
                {
                    continue;
                }

                var dest = new Rectangle((int)origin.X + x, (int)origin.Y + y0, 1, fillHeight);
                Color tint = FillColorAt?.Invoke(x) ?? FillColor;
                float topAlpha = FadeFillAcrossElement ? FillAlphaAt(y0, height) : FillTopAlpha;
                float bottomAlpha = FadeFillAcrossElement
                    ? FillAlphaAt(y0 + fillHeight, height)
                    : FillBottomAlpha;

                Color top = tint * topAlpha;
                Color bottom = tint * bottomAlpha;
                ReserveQuad(vertexCount, indexCount);
                GeometryBatch.WriteQuadGradient(scratchVertices, vertexCount, dest, top, top, bottom, bottom, 0f, 0f, 0f, 0f, GeometryBatch.NoClip);
                GeometryBatch.WriteQuadIndices(scratchIndices, indexCount, vertexCount);
                vertexCount += 4;
                indexCount += 6;
            }
        }

        /// <summary><see cref="DrawPolyline"/>, written into the scratch
        /// buffers instead of the batch. Keep the two in step.</summary>
        private void WritePolyline(Vector2 origin, Color color, int thickness, ref int vertexCount, ref int indexCount)
        {
            var pivot = new Vector2(0, thickness / 2f);
            Vector2 previous = origin + Points[0];
            for (int i = 1; i < Points.Count; i++)
            {
                Vector2 next = origin + Points[i];
                Color segment = LineColorAt != null
                    ? LineColorAt((Points[i - 1].X + Points[i].X) * 0.5f) * (color.A / 255f)
                    : color;

                ShapeDrawExtensions.ThickLineQuad(previous, next, thickness, out Rectangle rect, out float angle);
                previous = next;

                // AppendRotatedQuad draws nothing for an empty rect.
                if (rect.Width == 0 || rect.Height == 0)
                {
                    continue;
                }

                ReserveQuad(vertexCount, indexCount);
                GeometryBatch.WriteRotatedQuad(scratchVertices, vertexCount, rect, segment, angle, pivot, 0f, 0f, 0f, 0f, GeometryBatch.NoClip);
                GeometryBatch.WriteQuadIndices(scratchIndices, indexCount, vertexCount);
                vertexCount += 4;
                indexCount += 6;
            }
        }

        private void DrawCached(Managers.DrawManager manager, Vector2 offset)
        {
            for (int i = 0; i < passCount; i++)
            {
                ref CachedPass pass = ref passes[i];
                if (pass.Additive)
                {
                    manager.BeginAdditive();
                }

                manager.DrawCachedTriangles(pass.Vertices, pass.Indices, pass.Primitives, offset, Color.White);

                if (pass.Additive)
                {
                    manager.EndAdditive();
                }
            }
        }

        /// <summary>The number of cached draws this curve makes (0-3).
        /// Tests only.</summary>
        internal int CachedPassCount => passCount;

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

            // One cursor walked left to right with the columns, not a search
            // from the start per column: the columns only ever move right and
            // the points are non-decreasing in X, so every column resumes
            // where the last one stopped. A search per column was
            // width x points — a 400px wavetable frame of 192 points is 38,000
            // steps a frame, and Bifrost draws three of them plus envelopes.
            int cursor = 1;
            for (int x = 0; x < width; x++)
            {
                float y = Math.Clamp(SampleY(Points, x, ref cursor), 0f, baselineY);
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
        /// outside the curve's own X range.
        ///
        /// <paramref name="cursor"/> is where the previous, smaller-X call
        /// found its segment (start it at 1). Answers are identical to a search
        /// from the start — the segment before the cursor ends left of any X a
        /// later call can ask for.</summary>
        private static float SampleY(List<Vector2> points, float x, ref int cursor)
        {
            if (x <= points[0].X)
            {
                return points[0].Y;
            }

            for (int i = Math.Max(1, cursor); i < points.Count; i++)
            {
                cursor = i;
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
