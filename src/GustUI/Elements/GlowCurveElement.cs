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

                if (EnsureGeometry(origin, size, manager.RenderScale))
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
        // same builders the immediate path calls (StrokeGeometry.Write for
        // every stroke, WriteFill for the area under it),
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
        // pixel-grid decisions the build makes (StrokeGeometry snapping a flat
        // run onto the grid, the fill's whole-pixel columns) move by the same
        // whole number, so a fractional move rebuilds instead, and so - kept
        // from when the build truncated - does one that crosses zero. And it is only NEARLY exact (float rounding
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
            public float RenderScale;

            public bool Equals(GeometryKey other) =>
                Size == other.Size && LineColor == other.LineColor && Thickness == other.Thickness
                && ShowFill == other.ShowFill && FillColor == other.FillColor
                && ReferenceEquals(FillColorAt, other.FillColorAt) && ReferenceEquals(LineColorAt, other.LineColorAt)
                && FillTopAlpha == other.FillTopAlpha && FillBottomAlpha == other.FillBottomAlpha
                && FadeFillAcrossElement == other.FadeFillAcrossElement && Baseline == other.Baseline
                && Glow == other.Glow && GlowColor == other.GlowColor && Version == other.Version
                && RenderScale == other.RenderScale;
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

        private GeometryKey CurrentKey(Vector2 size, float renderScale) => new GeometryKey
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
            RenderScale = renderScale,
        };

        /// <summary>
        /// Makes the cached geometry current for a draw at
        /// <paramref name="origin"/>, rebuilding it only if something it was
        /// built from changed. False means draw the immediate way (the curve is
        /// too big to cache). Internal so the tests can drive it without a
        /// graphics device.
        /// </summary>
        internal bool EnsureGeometry(Vector2 origin, Vector2 size, float renderScale = 1f)
        {
            if (!hasGeometry || !IsCurrent(origin, size, renderScale))
            {
                BuildGeometry(origin, size, renderScale);
            }

            lastOrigin = origin;
            return geometryCacheable;
        }

        private bool IsCurrent(Vector2 origin, Vector2 size, float renderScale)
        {
            if (!builtKey.Equals(CurrentKey(size, renderScale)) || Points.Count != builtPointCount)
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

        private void BuildGeometry(Vector2 origin, Vector2 size, float renderScale)
        {
            GeometryBuilds++;
            hasGeometry = true;
            builtKey = CurrentKey(size, renderScale);
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
                int width = (int)size.X;
                Reserve(vertexCount + FillVertices(width), indexCount + FillIndices(width));
                WriteFill(origin, size, renderScale, 0, width, Vector2.Zero, GeometryBatch.NoClip,
                    scratchVertices, vertexCount, scratchIndices, indexCount, vertexCount, out int fv, out int fi);
                vertexCount += fv;
                indexCount += fi;
            }

            if (Glow)
            {
                FinishPass(ref vertexCount, ref indexCount, additive: false);
                Color glow = GlowColor ?? LineColor;
                WritePolyline(origin, glow * 0.12f, Thickness + 6, renderScale, ref vertexCount, ref indexCount);
                WritePolyline(origin, glow * 0.22f, Thickness + 3, renderScale, ref vertexCount, ref indexCount);
                FinishPass(ref vertexCount, ref indexCount, additive: true);
            }

            WritePolyline(origin, LineColor, Thickness, renderScale, ref vertexCount, ref indexCount);
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

        private static void Reserve(int vertices, int indices)
        {
            if (scratchVertices == null || scratchVertices.Length < vertices)
            {
                Array.Resize(ref scratchVertices, Math.Max(256, Math.Max(vertices, (scratchVertices?.Length ?? 0) * 2)));
            }

            if (scratchIndices == null || scratchIndices.Length < indices)
            {
                Array.Resize(ref scratchIndices, Math.Max(384, Math.Max(indices, (scratchIndices?.Length ?? 0) * 2)));
            }
        }

        // ---- the fill -------------------------------------------------------
        //
        // A strip, not a rect per column (2026-10-05): three vertices at every
        // whole-pixel X — just above the curve (transparent), just below it
        // (the fill's top colour) and the baseline (its bottom colour) — so the
        // fill's top edge follows the curve at float precision with a
        // one-physical-pixel soft edge, instead of stepping a whole pixel at a
        // time under the line. Same per-column tint and alpha rules as before.

        private static int FillVertices(int columns) => (columns + 1) * 3;

        private static int FillIndices(int columns) => columns * 12;

        /// <summary>Writes the fill for columns <paramref name="from"/> through
        /// <paramref name="to"/> (inclusive edges, element-relative X) — the one
        /// builder both the cached and the immediate path use.</summary>
        private void WriteFill(Vector2 origin, Vector2 size, float renderScale, int from, int to, Vector2 uv, Vector4 clip,
            GeometryVertex[] verts, int vAt, short[] indices, int iAt, int indexBase, out int vertexCount, out int indexCount)
        {
            vertexCount = 0;
            indexCount = 0;
            int height = (int)size.Y;
            if (to <= from || height < 2)
            {
                return;
            }

            float half = 0.5f / Math.Max(0.01f, renderScale);
            float baselineY = Math.Clamp(Baseline ?? size.Y, 0f, size.Y);
            int cursor = 1;
            int v = vAt;
            for (int x = from; x <= to; x++)
            {
                float y = Math.Clamp(SampleY(Points, x, ref cursor), 0f, baselineY);
                float outer = Math.Max(0f, y - half);
                float inner = Math.Min(baselineY, y + half);

                Color tint = FillColorAt?.Invoke(x) ?? FillColor;
                float topAlpha = FadeFillAcrossElement ? FillAlphaAt(inner, height) : FillTopAlpha;
                float bottomAlpha = FadeFillAcrossElement ? FillAlphaAt(baselineY, height) : FillBottomAlpha;
                if (baselineY - y <= 0.0001f)
                {
                    topAlpha = bottomAlpha = 0f;
                }

                float px = origin.X + x;
                verts[v++] = new GeometryVertex(new Vector2(px, origin.Y + outer), Color.Transparent, uv, clip);
                verts[v++] = new GeometryVertex(new Vector2(px, origin.Y + inner), tint * topAlpha, uv, clip);
                verts[v++] = new GeometryVertex(new Vector2(px, origin.Y + baselineY), tint * bottomAlpha, uv, clip);
            }

            int ii = iAt;
            for (int c = 0; c < to - from; c++)
            {
                int a = indexBase + (c * 3);
                int b = a + 3;
                for (int row = 0; row < 2; row++)
                {
                    indices[ii++] = (short)(a + row);
                    indices[ii++] = (short)(b + row);
                    indices[ii++] = (short)(b + row + 1);
                    indices[ii++] = (short)(a + row);
                    indices[ii++] = (short)(b + row + 1);
                    indices[ii++] = (short)(a + row + 1);
                }
            }

            vertexCount = v - vAt;
            indexCount = ii - iAt;
        }

        // ---- the line -------------------------------------------------------

        [ThreadStatic] private static Vector2[] strokePoints;
        [ThreadStatic] private static Color[] strokeColors;

        /// <summary>The curve's points at <paramref name="origin"/> and their
        /// colours (one, or one per point with <see cref="LineColorAt"/>), in
        /// the per-thread scratch both paths stroke from.</summary>
        private int StrokeInputs(Vector2 origin, Color color, out Vector2[] points, out Color[] colors)
        {
            int n = Points.Count;
            if (strokePoints == null || strokePoints.Length < n)
            {
                strokePoints = new Vector2[Math.Max(64, n * 2)];
                strokeColors = new Color[Math.Max(64, n * 2)];
            }

            points = strokePoints;
            colors = strokeColors;
            for (int i = 0; i < n; i++)
            {
                points[i] = origin + Points[i];
                if (LineColorAt != null)
                {
                    colors[i] = LineColorAt(Points[i].X) * (color.A / 255f);
                }
            }

            if (LineColorAt == null)
            {
                colors[0] = color;
                return 1;
            }

            return n;
        }

        /// <summary>The stroke, written into the scratch buffers for the
        /// cache — the same <see cref="StrokeGeometry"/> the immediate
        /// <see cref="DrawPolyline"/> goes through.</summary>
        private void WritePolyline(Vector2 origin, Color color, int thickness, float renderScale, ref int vertexCount, ref int indexCount)
        {
            int colorCount = StrokeInputs(origin, color, out Vector2[] points, out Color[] colors);
            int n = Points.Count;
            Reserve(vertexCount + StrokeGeometry.MaxVertices(n), indexCount + StrokeGeometry.MaxIndices(n));
            StrokeGeometry.Write(
                new ReadOnlySpan<Vector2>(points, 0, n), new ReadOnlySpan<Color>(colors, 0, colorCount),
                thickness, renderScale, Vector2.Zero, GeometryBatch.NoClip,
                scratchVertices, vertexCount, scratchIndices, indexCount, vertexCount,
                out int written, out int writtenIndices);
            vertexCount += written;
            indexCount += writtenIndices;
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

        /// <summary>The area under the curve, drawn the immediate way (a curve
        /// too big to cache) — <see cref="WriteFill"/> in pieces small enough
        /// for 16-bit indices, each appended as it is built.</summary>
        private void DrawFill(Managers.DrawManager manager, Vector2 origin, Vector2 size)
        {
            int width = (int)size.X;
            if (width < 1 || size.Y < 2)
            {
                return;
            }

            AtlasRegion white = manager.GeometryAtlas.WhiteRegion;
            var uv = new Vector2(
                (white.Pixels.X + 0.5f) / white.Texture.Width,
                (white.Pixels.Y + 0.5f) / white.Texture.Height);
            Vector4 clip = manager.GetClipRectForGeometry();

            const int Chunk = 4096;
            for (int from = 0; from < width; from += Chunk)
            {
                int to = Math.Min(width, from + Chunk);
                Reserve(FillVertices(to - from), FillIndices(to - from));
                WriteFill(origin, size, manager.RenderScale, from, to, uv, clip,
                    scratchVertices, 0, scratchIndices, 0, 0, out int vertexCount, out int indexCount);
                manager.GeometryBatch.AppendTriangles(white.Texture, scratchVertices, vertexCount, scratchIndices,
                    indexCount / 3, clip, manager.CurrentBlend);
            }
        }

        /// <summary>Fixed opaque-top/transparent-bottom alpha curve over the
        /// element's absolute row range, evaluated at a float row.</summary>
        private float FillAlphaAt(float y, int height)
        {
            return height <= 1
                ? FillTopAlpha
                : MathHelper.Lerp(FillTopAlpha, FillBottomAlpha,
                    MathHelper.Clamp(y, 0, height - 1) / (height - 1));
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
            // Coloured per POINT when a caller asks (LineColorAt), and the GPU
            // blends between them, so the tint is a true gradient along the line.
            int colorCount = StrokeInputs(origin, color, out Vector2[] points, out Color[] colors);
            manager.DrawStroke(
                new ReadOnlySpan<Vector2>(points, 0, Points.Count),
                new ReadOnlySpan<Color>(colors, 0, colorCount),
                thickness);
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
