using GustUI.Managers;
using GustUI.Rendering;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Extensions
{
    /// <summary>
    /// Shape primitives on top of <see cref="DrawManager"/> — antialiased
    /// circles, capsules, rings, thick lines, curves, and the monotone-region
    /// fill the ezmuze mark is drawn from.
    ///
    /// PUBLIC (2026-08-23, was internal): a host app that draws its own element
    /// needs the same primitives GustUI's built-in elements use, and the
    /// alternative was either duplicating the feathered-geometry maths outside
    /// the framework or putting app artwork inside it. Every type in these
    /// signatures is already public.
    /// </summary>
    public static class ShapeDrawExtensions
    {
        public static void DrawLine(this DrawManager manager, Vector2 start, Vector2 end, Color color)
        {
            Vector2 edge = end - start;
            float angle = (float)Math.Atan2(edge.Y, edge.X);
            var rect = new Rectangle((int)start.X, (int)start.Y, (int)edge.Length(), 1);

            AtlasRegion white = manager.GeometryAtlas.WhiteRegion;
            manager.GeometryBatch.AppendRotatedQuad(white.Texture, rect, white.Pixels, color, angle, new Vector2(0, 0), manager.GetClipRectForGeometry(), manager.CurrentBlend);
        }

        /// <summary>Rotated filled rectangle around an arbitrary DEST-LOCAL
        /// origin — the KnobElement pointer/needle idiom (DrawThickLine's
        /// rotated-rect append, generalized past a line's implied
        /// thickness/length to any rect+origin), using the shared white
        /// atlas texel instead of a private per-element pixel texture.</summary>
        public static void DrawRotatedFilledRectangle(this DrawManager manager, Rectangle rectangle, Color color, float angle, Vector2 origin)
        {
            AtlasRegion white = manager.GeometryAtlas.WhiteRegion;
            manager.GeometryBatch.AppendRotatedQuad(white.Texture, rectangle, white.Pixels, color, angle, origin, manager.GetClipRectForGeometry(), manager.CurrentBlend);
        }

        /// <summary>
        /// Filled circle, drawn as REAL vector geometry (a triangle fan plus
        /// a feathered edge strip) instead of a rasterized-once atlas
        /// bitmap — the direct fix for TextureAtlas-baked discs (KnobElement's
        /// dial/ring/live-dot, ToggleSwitchElement's thumb, SliderElement/
        /// XYPadElement's handle) going soft on any display where RenderScale
        /// != 1: those bakes are rasterized at LOGICAL pixel resolution and
        /// then bilinearly stretched to PHYSICAL pixel size by the GPU, same
        /// as any other texture. Real geometry has no such mismatch — vertex
        /// positions are exact floats in the SAME logical space every other
        /// Draw call uses, magnified losslessly by RenderScale's own matrix
        /// transform, so this is crisp at ANY DPI/zoom with no bake, no
        /// cache, no per-size texture at all.
        /// </summary>
        public static void DrawFilledCircle(this DrawManager manager, Vector2 center, float radius, Color color)
        {
            if (radius <= 0.01f)
            {
                return;
            }

            int segments = ArcSegments(radius, manager.RenderScale);
            var points = new Vector2[segments];
            var normals = new Vector2[segments];
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * MathHelper.TwoPi;
                var dir = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
                normals[i] = dir;
                points[i] = center + dir * radius;
            }

            AppendFeatheredFill(manager, points, normals, center, color);
        }

        /// <summary>
        /// Filled capsule/stadium (a rect with fully-rounded left/right ends,
        /// radius = rect.Height / 2) — ToggleSwitchElement's track shape,
        /// same real-geometry treatment as <see cref="DrawFilledCircle"/>.
        /// </summary>
        public static void DrawFilledCapsule(this DrawManager manager, Rectangle rect, Color color)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            float radius = Math.Min(rect.Height / 2f, rect.Width / 2f);
            if (radius <= 0.01f)
            {
                return;
            }

            Vector2 leftCenter = new Vector2(rect.Left + radius, rect.Top + radius);
            Vector2 rightCenter = new Vector2(rect.Right - radius, rect.Top + radius);

            // Two half-circle arcs (right cap: -90°..90°, left cap:
            // 90°..270°), traced consecutively around the perimeter — the
            // straight top/bottom edges need no extra points: a fan
            // triangulation from the centroid already draws a straight edge
            // between any two non-adjacent-angle boundary points.
            int capSegments = ArcSegments(radius, manager.RenderScale);
            int total = (capSegments + 1) * 2;
            var points = new Vector2[total];
            var normals = new Vector2[total];

            int vi = 0;
            for (int i = 0; i <= capSegments; i++)
            {
                float a = MathHelper.ToRadians(-90f + i * (180f / capSegments));
                var dir = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
                normals[vi] = dir;
                points[vi] = rightCenter + dir * radius;
                vi++;
            }

            for (int i = 0; i <= capSegments; i++)
            {
                float a = MathHelper.ToRadians(90f + i * (180f / capSegments));
                var dir = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
                normals[vi] = dir;
                points[vi] = leftCenter + dir * radius;
                vi++;
            }

            Vector2 centroid = new Vector2(rect.Left + rect.Width / 2f, rect.Top + rect.Height / 2f);
            AppendFeatheredFill(manager, points, normals, centroid, color);
        }

        /// <summary>
        /// Filled annulus (ring band between two radii) — KnobElement's rim
        /// ring, same real-geometry treatment as <see cref="DrawFilledCircle"/>:
        /// four concentric vertex rings (feather-in, inner solid edge, outer
        /// solid edge, feather-out) instead of a baked atlas annulus.
        /// </summary>
        public static void DrawRing(this DrawManager manager, Vector2 center, float innerRadius, float outerRadius, Color color)
        {
            if (outerRadius <= 0.01f || outerRadius <= innerRadius)
            {
                return;
            }

            innerRadius = Math.Max(0f, innerRadius);
            int segments = ArcSegments(outerRadius, manager.RenderScale);
            float feather = 1f / Math.Max(0.01f, manager.RenderScale);
            float half = feather * 0.5f;

            float r0 = Math.Max(0f, innerRadius - half);
            float r1 = innerRadius + half;
            float r2 = outerRadius - half;
            float r3 = outerRadius + half;
            if (r1 > r2)
            {
                // Band thinner than the feather itself: collapse the solid
                // middle to a point rather than let the two feather bands
                // cross and invert.
                r1 = r2 = (innerRadius + outerRadius) * 0.5f;
            }

            AtlasRegion white = manager.GeometryAtlas.WhiteRegion;
            float u = (white.Pixels.X + 0.5f) / white.Texture.Width;
            float v = (white.Pixels.Y + 0.5f) / white.Texture.Height;
            var uv = new Vector2(u, v);
            Vector4 clip = manager.GetClipRectForGeometry();
            Color transparent = color * 0f;

            var verts = new GeometryVertex[segments * 4];
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * MathHelper.TwoPi;
                var dir = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
                verts[i] = new GeometryVertex(center + dir * r0, transparent, uv, clip);
                verts[segments + i] = new GeometryVertex(center + dir * r1, color, uv, clip);
                verts[segments * 2 + i] = new GeometryVertex(center + dir * r2, color, uv, clip);
                verts[segments * 3 + i] = new GeometryVertex(center + dir * r3, transparent, uv, clip);
            }

            var idx = new short[segments * 18];
            int ii = 0;
            for (int i = 0; i < segments; i++)
            {
                int ni = (i + 1) % segments;
                AppendBandQuad(idx, ref ii, i, ni, 0, segments);
                AppendBandQuad(idx, ref ii, i, ni, segments, segments * 2);
                AppendBandQuad(idx, ref ii, i, ni, segments * 2, segments * 3);
            }

            manager.GeometryBatch.AppendTriangles(white.Texture, verts, idx, ii / 3, clip, manager.CurrentBlend);
        }

        /// <summary>
        /// A slice of a ring: same band as <see cref="DrawRing"/> but only from
        /// <paramref name="startAngle"/> through <paramref name="sweepAngle"/>
        /// radians (0 = three o'clock, positive = clockwise on screen).
        ///
        /// Exists so a ring can DRAW ITSELF IN — an arc whose sweep animates
        /// from nothing to a full turn is the entrance the ezmuze mark uses.
        /// Unlike the closed ring this does not wrap, so the band has two open
        /// ends; they are left square, which is invisible at the sweep speeds
        /// this is for.
        /// </summary>
        public static void DrawRingArc(this DrawManager manager, Vector2 center, float innerRadius,
            float outerRadius, Color color, float startAngle, float sweepAngle)
        {
            if (outerRadius <= 0.01f || outerRadius <= innerRadius || sweepAngle <= 0.0001f)
            {
                return;
            }

            sweepAngle = Math.Min(sweepAngle, MathHelper.TwoPi);
            innerRadius = Math.Max(0f, innerRadius);

            // Segment count follows the SWEPT length, not the whole circle, so
            // a short arc doesn't pay for a full circle's worth of triangles
            // and a long one is still smooth.
            int segments = Math.Max(2, (int)Math.Ceiling(
                ArcSegments(outerRadius, manager.RenderScale) * (sweepAngle / MathHelper.TwoPi)));

            float feather = 1f / Math.Max(0.01f, manager.RenderScale);
            float half = feather * 0.5f;

            float r0 = Math.Max(0f, innerRadius - half);
            float r1 = innerRadius + half;
            float r2 = outerRadius - half;
            float r3 = outerRadius + half;
            if (r1 > r2)
            {
                r1 = r2 = (innerRadius + outerRadius) * 0.5f;
            }

            AtlasRegion white = manager.GeometryAtlas.WhiteRegion;
            var uv = new Vector2(
                (white.Pixels.X + 0.5f) / white.Texture.Width,
                (white.Pixels.Y + 0.5f) / white.Texture.Height);
            Vector4 clip = manager.GetClipRectForGeometry();
            Color transparent = color * 0f;

            int ring = segments + 1;
            var verts = new GeometryVertex[ring * 4];
            for (int i = 0; i <= segments; i++)
            {
                float a = startAngle + sweepAngle * (i / (float)segments);
                var dir = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
                verts[i] = new GeometryVertex(center + dir * r0, transparent, uv, clip);
                verts[ring + i] = new GeometryVertex(center + dir * r1, color, uv, clip);
                verts[ring * 2 + i] = new GeometryVertex(center + dir * r2, color, uv, clip);
                verts[ring * 3 + i] = new GeometryVertex(center + dir * r3, transparent, uv, clip);
            }

            var idx = new short[segments * 18];
            int ii = 0;
            for (int i = 0; i < segments; i++)
            {
                AppendBandQuad(idx, ref ii, i, i + 1, 0, ring);
                AppendBandQuad(idx, ref ii, i, i + 1, ring, ring * 2);
                AppendBandQuad(idx, ref ii, i, i + 1, ring * 2, ring * 3);
            }

            manager.GeometryBatch.AppendTriangles(white.Texture, verts, idx, ii / 3, clip, manager.CurrentBlend);
        }

        /// <summary>
        /// Fills the region between two polylines that span the same range and
        /// never double back — an <b>x-monotone</b> region, one boundary above
        /// the other.
        ///
        /// This is the shape a centroid fan cannot draw. <see cref="DrawFilledCircle"/>
        /// and friends triangulate by fanning from the middle, which is only
        /// valid for a CONVEX outline; the ezmuze mark's two halves are a disc
        /// cut by an S-shaped wave, and an S is not convex. A general polygon
        /// triangulator (ear clipping) would cover it, but nothing here needs
        /// one: both halves are bounded above and below by a function of x, and
        /// that special case triangulates as a plain strip.
        ///
        /// <paramref name="upperNormals"/>/<paramref name="lowerNormals"/> point
        /// OUT of the region and drive the antialiased edge — the caller knows
        /// what each boundary is (a circle's arc, a wave) and can give a true
        /// normal, which a general routine could only approximate.
        ///
        /// Both arrays must be the same length and ordered the same way along
        /// the sweep. Fewer than two points draws nothing.
        /// </summary>
        public static void DrawMonotoneRegion(this DrawManager manager,
            Vector2[] upper, Vector2[] lower, Vector2[] upperNormals, Vector2[] lowerNormals, Color color) =>
            manager.DrawMonotoneRegion(upper, lower, upperNormals, lowerNormals, color, color, Vector2.Zero, Vector2.Zero);

        /// <summary>
        /// <see cref="DrawMonotoneRegion(DrawManager, Vector2[], Vector2[], Vector2[], Vector2[], Color)"/>
        /// with a LINEAR GRADIENT: each vertex takes its colour from where it
        /// falls along the axis <paramref name="gradientFrom"/> →
        /// <paramref name="gradientTo"/>, clamped at both ends.
        ///
        /// Evaluated per vertex rather than per region, which is the only thing
        /// that makes it usable here: the two halves of the ezmuze mark meet
        /// along a wave, so a gradient run "top of this shape to bottom of this
        /// shape" would restart at every column and band the seam. Anchoring it
        /// to a fixed axis in the same space as the geometry means the two
        /// halves sample one continuous ramp.
        ///
        /// A zero-length axis collapses to a flat fill of
        /// <paramref name="gradientFrom"/>'s colour.
        /// </summary>
        public static void DrawMonotoneRegion(this DrawManager manager,
            Vector2[] upper, Vector2[] lower, Vector2[] upperNormals, Vector2[] lowerNormals,
            Color from, Color to, Vector2 gradientFrom, Vector2 gradientTo)
        {
            int n = upper.Length;
            if (n < 2 || lower.Length != n || upperNormals.Length != n || lowerNormals.Length != n)
            {
                return;
            }

            float feather = 1f / Math.Max(0.01f, manager.RenderScale);
            float half = feather * 0.5f;

            AtlasRegion white = manager.GeometryAtlas.WhiteRegion;
            var uv = new Vector2(
                (white.Pixels.X + 0.5f) / white.Texture.Width,
                (white.Pixels.Y + 0.5f) / white.Texture.Height);
            Vector4 clip = manager.GetClipRectForGeometry();

            // Four rings: the two boundaries pushed a half-feather OUT (fully
            // transparent) and a half-feather IN (fully opaque). The opaque
            // pair is the region's real interior; each transparent pair makes
            // one soft edge.
            Vector2 axis = gradientTo - gradientFrom;
            float axisLengthSquared = axis.LengthSquared();

            Color At(Vector2 point)
            {
                if (axisLengthSquared <= 0.0001f)
                {
                    return from;
                }

                float t = Vector2.Dot(point - gradientFrom, axis) / axisLengthSquared;
                return Color.Lerp(from, to, MathHelper.Clamp(t, 0f, 1f));
            }

            var verts = new GeometryVertex[n * 4];
            for (int i = 0; i < n; i++)
            {
                // How deep the region is at this column, measured along the
                // interior direction rather than as a plain difference, so a
                // caller whose boundaries are not stacked vertically still gets
                // the right answer.
                float depth = Vector2.Dot(lower[i] - upper[i], -upperNormals[i]);

                Vector2 outerUpper, innerUpper, innerLower, outerLower;

                if (depth <= 0f)
                {
                    // EMPTY column - the two boundaries have met or crossed, so
                    // there is nothing here to draw. Every vertex collapses to
                    // one point and the quads come out zero-area.
                    //
                    // This case has to be handled explicitly, because the two
                    // boundaries generally carry DIFFERENT normals: even when
                    // they sit on exactly the same point, insetting each along
                    // its own normal separates them again, and the band between
                    // is filled at full opacity. In the ezmuze mark, where the
                    // unfilled part of each half collapses onto the disc's arc,
                    // that painted a fully opaque one-pixel line along the whole
                    // arc - a complete thin circle drawn around a disc that had
                    // not formed yet (2026-08-23).
                    outerUpper = innerUpper = innerLower = outerLower = upper[i];
                }
                else if (depth < feather)
                {
                    // Thinner than the feather itself: collapse the solid middle
                    // to a line rather than let the two feather bands cross and
                    // invert, exactly as DrawRing does for a narrow band. What
                    // is left is a correctly faint hairline.
                    outerUpper = upper[i] + upperNormals[i] * half;
                    outerLower = lower[i] + lowerNormals[i] * half;
                    innerUpper = innerLower = (upper[i] + lower[i]) * 0.5f;
                }
                else
                {
                    outerUpper = upper[i] + upperNormals[i] * half;
                    innerUpper = upper[i] - upperNormals[i] * half;
                    innerLower = lower[i] - lowerNormals[i] * half;
                    outerLower = lower[i] + lowerNormals[i] * half;
                }

                verts[i] = new GeometryVertex(outerUpper, At(outerUpper) * 0f, uv, clip);
                verts[n + i] = new GeometryVertex(innerUpper, At(innerUpper), uv, clip);
                verts[n * 2 + i] = new GeometryVertex(innerLower, At(innerLower), uv, clip);
                verts[n * 3 + i] = new GeometryVertex(outerLower, At(outerLower) * 0f, uv, clip);
            }

            var idx = new short[(n - 1) * 18];
            int ii = 0;
            for (int i = 0; i < n - 1; i++)
            {
                AppendBandQuad(idx, ref ii, i, i + 1, 0, n);
                AppendBandQuad(idx, ref ii, i, i + 1, n, n * 2);
                AppendBandQuad(idx, ref ii, i, i + 1, n * 2, n * 3);
            }

            manager.GeometryBatch.AppendTriangles(white.Texture, verts, idx, ii / 3, clip, manager.CurrentBlend);
        }

        /// <summary>Two triangles spanning one segment of a band between an
        /// inner and outer concentric vertex ring (<see cref="DrawRing"/>).</summary>
        private static void AppendBandQuad(short[] idx, ref int ii, int i, int ni, int innerBase, int outerBase)
        {
            int a = innerBase + i;
            int b = innerBase + ni;
            int c = outerBase + i;
            int d = outerBase + ni;
            idx[ii++] = (short)a; idx[ii++] = (short)c; idx[ii++] = (short)d;
            idx[ii++] = (short)a; idx[ii++] = (short)d; idx[ii++] = (short)b;
        }

        /// <summary>Arc segment count for a curve of the given radius: enough
        /// that faceting stays sub-pixel at the shape's actual PHYSICAL
        /// on-screen size (radius × RenderScale), never so many that a tiny
        /// control wastes vertices on curvature nobody can see.</summary>
        private static int ArcSegments(float radius, float renderScale)
        {
            return MathHelper.Clamp((int)(radius * renderScale * 0.9f), 10, 64);
        }

        /// <summary>
        /// Fills a convex polygon (fan from <paramref name="centroid"/>) and
        /// wraps it in a feathered edge strip — the real-geometry
        /// antialiasing trick this file's other shapes get for free from
        /// GeometryBatch's baked atlas alpha ramps: the TRUE boundary
        /// (<paramref name="points"/>) sits exactly on the alpha=1→0
        /// crossing, offset inward by half a feather width for the opaque
        /// ring and outward by half for the transparent ring, so the soft
        /// edge straddles the actual shape boundary instead of eating into
        /// it. Feather width is ~1 PHYSICAL pixel (1 / RenderScale in
        /// logical units) regardless of zoom — the same "constant physical
        /// AA band" idea DrawSdfString's Smoothing already uses, just via
        /// vertex-alpha geometry instead of a distance-field shader (no new
        /// shader needed: GeometryBatch.fx already premultiplies color×alpha
        /// per vertex).
        /// </summary>
        private static void AppendFeatheredFill(DrawManager manager, Vector2[] points, Vector2[] normals, Vector2 centroid, Color color)
        {
            int n = points.Length;
            if (n < 3)
            {
                return;
            }

            float feather = 1f / Math.Max(0.01f, manager.RenderScale);

            AtlasRegion white = manager.GeometryAtlas.WhiteRegion;
            float u = (white.Pixels.X + 0.5f) / white.Texture.Width;
            float v = (white.Pixels.Y + 0.5f) / white.Texture.Height;
            var uv = new Vector2(u, v);
            Vector4 clip = manager.GetClipRectForGeometry();
            Color transparent = color * 0f;

            var verts = new GeometryVertex[1 + n * 2];
            verts[0] = new GeometryVertex(centroid, color, uv, clip);
            for (int i = 0; i < n; i++)
            {
                Vector2 inner = points[i] - normals[i] * (feather * 0.5f);
                Vector2 outer = points[i] + normals[i] * (feather * 0.5f);
                verts[1 + i] = new GeometryVertex(inner, color, uv, clip);
                verts[1 + n + i] = new GeometryVertex(outer, transparent, uv, clip);
            }

            var idx = new short[n * 9];
            int ii = 0;
            for (int i = 0; i < n; i++)
            {
                int a = 1 + i;
                int b = 1 + (i + 1) % n;
                // Fill fan.
                idx[ii++] = 0; idx[ii++] = (short)a; idx[ii++] = (short)b;

                // Feather strip (inner ring → outer ring).
                int oa = 1 + n + i;
                int ob = 1 + n + (i + 1) % n;
                idx[ii++] = (short)a; idx[ii++] = (short)oa; idx[ii++] = (short)ob;
                idx[ii++] = (short)a; idx[ii++] = (short)ob; idx[ii++] = (short)b;
            }

            manager.GeometryBatch.AppendTriangles(white.Texture, verts, idx, ii / 3, clip, manager.CurrentBlend);
        }

        /// <summary>Linear 2-color gradient fill via per-vertex color on the
        /// shared white atlas texel — replaces TVFillSimpleGradient's old
        /// bake-a-256x1-texture-per-instance approach with zero texture
        /// allocation and no extra GeometryBatch segment (same texture every
        /// flat-color primitive already samples).</summary>
        public static void DrawFilledRectangleGradient(this DrawManager manager, Rectangle rectangle, Color primary, Color secondary, Direction direction)
        {
            AtlasRegion white = manager.GeometryAtlas.WhiteRegion;
            Color topLeft, topRight, bottomLeft, bottomRight;
            if (direction == Direction.Horizontally)
            {
                topLeft = bottomLeft = primary;
                topRight = bottomRight = secondary;
            }
            else
            {
                topLeft = topRight = primary;
                bottomLeft = bottomRight = secondary;
            }

            manager.GeometryBatch.AppendQuadGradient(white.Texture, rectangle, white.Pixels, topLeft, topRight, bottomRight, bottomLeft, manager.GetClipRectForGeometry(), manager.CurrentBlend);
        }

        /// <summary>DrawLine with a pixel thickness (rotated filled rect).</summary>
        public static void DrawThickLine(this DrawManager manager, Vector2 start, Vector2 end, Color color, int thickness)
        {
            Vector2 edge = end - start;
            float angle = (float)Math.Atan2(edge.Y, edge.X);
            var rect = new Rectangle((int)start.X, (int)start.Y, (int)edge.Length() + 1, thickness);

            AtlasRegion white = manager.GeometryAtlas.WhiteRegion;
            manager.GeometryBatch.AppendRotatedQuad(white.Texture, rect, white.Pixels, color, angle, new Vector2(0, thickness / 2f), manager.GetClipRectForGeometry(), manager.CurrentBlend);
        }

        /// <summary>
        /// Cubic Bézier as a sampled polyline of thick segments — the house
        /// "sampled geometry IS the curve" idiom (no curve primitive exists in
        /// the sprite batch and none is needed).
        /// </summary>
        /// <param name="segments">How many straight pieces to fake the curve
        /// with. Leave it at <see cref="AutoSegments"/> — the default — to let
        /// the curve's own size decide; see <see cref="SegmentsFor"/>.</param>
        public static void DrawCubicBezier(this DrawManager manager, Vector2 p0, Vector2 c0, Vector2 c1, Vector2 p1,
            Color color, int thickness = 2, int segments = AutoSegments)
            => manager.DrawCubicBezier(p0, c0, c1, p1, color, color, thickness, segments);

        /// <summary>Pass as <c>segments</c> to size the tessellation from the
        /// curve rather than from a guess. See <see cref="SegmentsFor"/>.</summary>
        public const int AutoSegments = 0;

        /// <summary>
        /// How many straight pieces a curve of this size needs.
        ///
        /// A FIXED COUNT IS WRONG AT BOTH ENDS, which is what the previous
        /// hardcoded 24 was: a wire spanning two thousand pixels got
        /// eighty-pixel chords and looked visibly angular — reported as
        /// exactly that — while a forty-pixel one got twenty-four segments to
        /// draw what two would have covered.
        ///
        /// So the count comes from the CONTROL POLYGON's length, which bounds
        /// the curve's own arc length and costs three subtractions to measure.
        /// One segment per <see cref="PixelsPerSegment"/> of it, clamped: never
        /// so few that a short curve turns into a chevron, never so many that
        /// dragging a long wire around floods the batch.
        /// </summary>
        public static int SegmentsFor(Vector2 p0, Vector2 c0, Vector2 c1, Vector2 p1)
        {
            float polygon = (c0 - p0).Length() + (c1 - c0).Length() + (p1 - c1).Length();
            return (int)MathHelper.Clamp(polygon / PixelsPerSegment, MinSegments, MaxSegments);
        }

        /// <summary>Roughly the chord length a segment covers. Twelve is below
        /// the point at which a corner between two segments is visible against
        /// a 2px stroke at 100% zoom.</summary>
        private const float PixelsPerSegment = 12f;

        private const int MinSegments = 8;

        /// <summary>The ceiling exists for the drag preview: a wire being
        /// dragged across a large canvas is re-tessellated every frame, and
        /// there is nothing to see past this.</summary>
        private const int MaxSegments = 160;

        /// <summary>
        /// A cubic Bézier shading from <paramref name="fromColor"/> at
        /// <paramref name="p0"/> to <paramref name="toColor"/> at
        /// <paramref name="p1"/>.
        ///
        /// BY ARC LENGTH, not by the curve parameter t. Those are not the same
        /// thing and the difference is plainly visible on the S-curves this
        /// was written for: with horizontal tangents and a deep bend, t moves
        /// fast through the middle and slowly at the ends, so a colour lerped
        /// on t puts its halfway point well away from the halfway point of the
        /// LINE. The ask was that the middle of the wire is the middle of the
        /// two colours, and that is a statement about distance.
        ///
        /// So it samples first, accumulates the chord lengths, and colours
        /// each segment by how far along it actually is. One extra array of
        /// points, and the polyline was going to be built anyway.
        /// </summary>
        public static void DrawCubicBezier(this DrawManager manager, Vector2 p0, Vector2 c0, Vector2 c1, Vector2 p1,
            Color fromColor, Color toColor, int thickness = 2, int segments = AutoSegments)
        {
            if (segments <= 0)
            {
                segments = SegmentsFor(p0, c0, c1, p1);
            }

            Vector2[] points = RentBezierPoints(segments + 1);
            float[] lengths = RentBezierLengths(segments + 1);

            points[0] = p0;
            lengths[0] = 0f;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                float u = 1f - t;
                points[i] =
                    u * u * u * p0
                    + 3f * u * u * t * c0
                    + 3f * u * t * t * c1
                    + t * t * t * p1;
                lengths[i] = lengths[i - 1] + (points[i] - points[i - 1]).Length();
            }

            float total = lengths[segments];
            bool graded = fromColor != toColor;

            // A zero-length curve would divide by zero below, and there is
            // nothing to draw anyway.
            if (total <= 0.0001f)
            {
                return;
            }

            for (int i = 1; i <= segments; i++)
            {
                Color color = fromColor;
                if (graded)
                {
                    // The MIDPOINT of the segment, so a segment is the colour
                    // of where it is rather than of where it started — which
                    // matters most at the ends, where the first segment would
                    // otherwise be pure fromColor for its whole length.
                    float at = (lengths[i - 1] + lengths[i]) * 0.5f / total;
                    color = Color.Lerp(fromColor, toColor, at);
                }

                manager.DrawThickLine(points[i - 1], points[i], color, thickness);
            }
        }

        // Two scratch arrays rather than an allocation per curve. The wire
        // layer redraws every wire every frame, so a hundred wires at sixty
        // frames is six thousand arrays a second straight into gen 0. Not
        // thread-safe and does not need to be: drawing is the game thread's.
        [ThreadStatic]
        private static Vector2[] bezierPoints;

        [ThreadStatic]
        private static float[] bezierLengths;

        private static Vector2[] RentBezierPoints(int count)
        {
            if (bezierPoints == null || bezierPoints.Length < count)
            {
                bezierPoints = new Vector2[Math.Max(count, MaxSegments + 1)];
            }

            return bezierPoints;
        }

        private static float[] RentBezierLengths(int count)
        {
            if (bezierLengths == null || bezierLengths.Length < count)
            {
                bezierLengths = new float[Math.Max(count, MaxSegments + 1)];
            }

            return bezierLengths;
        }

        public static void DrawRectangle(this DrawManager manager, Rectangle rectangle, Color color, int borderSize = 1)
        {
            // Delegates to DrawFilledRectangle (not a direct Pixel draw)
            // specifically so border strokes automatically pick up the
            // geometry-backend routing below without duplicating it here.
            for (int i = 0; i < borderSize; i++)
            {
                manager.DrawFilledRectangle(new Rectangle(rectangle.Left, rectangle.Top + i, rectangle.Width, 1), color);
                manager.DrawFilledRectangle(new Rectangle(rectangle.Left, rectangle.Bottom - i, rectangle.Width, 1), color);

                manager.DrawFilledRectangle(new Rectangle(rectangle.Left + i, rectangle.Top, 1, rectangle.Height), color);
                manager.DrawFilledRectangle(new Rectangle(rectangle.Right - i, rectangle.Top, 1, rectangle.Height), color);
            }
        }

        public static void DrawFilledRectangle(this DrawManager manager, Rectangle rectangle, Color color)
        {
            AtlasRegion white = manager.GeometryAtlas.WhiteRegion;
            manager.GeometryBatch.AppendQuad(white.Texture, rectangle, white.Pixels, color, manager.GetClipRectForGeometry(), manager.CurrentBlend);
        }

        /// <summary>
        /// A circle shaded from <paramref name="inner"/> at its centre to
        /// <paramref name="outer"/> at its rim.
        ///
        /// ONE TRIANGLE FAN with per-vertex colour, not a stack of circles.
        /// The stack was tried first and it is worth saying why it failed,
        /// because it looked like the obvious implementation: every
        /// <see cref="DrawFilledCircle"/> carries its own feathered
        /// antialiased edge, so concentric ones spaced closer than that
        /// feather cross-fade into each other and the cap comes out covered in
        /// a moire swirl. Spacing them further apart to avoid it just trades
        /// the swirl for visible banding. There is no gap between the two.
        ///
        /// A fan has neither problem: the GPU interpolates the colour exactly,
        /// with one vertex per rim segment and no overlapping geometry at all.
        /// It is also the cheaper of the two by an order of magnitude.
        ///
        /// The fan's own rim is hard-edged (no feather), so an opaque
        /// <paramref name="outer"/> disc is laid down underneath it at the
        /// full radius to supply the antialiased boundary, and the fan is
        /// drawn a half-pixel inside that.
        /// </summary>
        public static void DrawRadialShadedCircle(this DrawManager manager, Vector2 center,
            float radius, Color inner, Color outer)
        {
            if (radius <= 0.01f)
            {
                return;
            }

            // The AA edge. Also the whole shape when the control is too small
            // for the shading to be worth any triangles.
            manager.DrawFilledCircle(center, radius, outer);

            float fanRadius = radius - 0.5f;
            if (fanRadius <= 1.5f)
            {
                return;
            }

            int segments = ArcSegments(fanRadius, manager.RenderScale);
            var vertices = new VertexPositionColor[segments + 2];
            vertices[0] = new VertexPositionColor(new Vector3(center, 0f), inner);

            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * MathHelper.TwoPi;
                vertices[i + 1] = new VertexPositionColor(
                    new Vector3(
                        center.X + (float)Math.Cos(angle) * fanRadius,
                        center.Y + (float)Math.Sin(angle) * fanRadius,
                        0f),
                    outer);
            }

            var indices = new short[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                indices[i * 3] = 0;
                indices[i * 3 + 1] = (short)(i + 1);
                indices[i * 3 + 2] = (short)(i + 2);
            }

            manager.DrawTriangles(vertices, indices, segments);
        }

        /// <summary>
        /// A soft circular shadow: <paramref name="layers"/> translucent
        /// circles from <paramref name="radius"/> out to
        /// <paramref name="radius"/> + <paramref name="spread"/>, each fainter
        /// than the last, centred on <paramref name="center"/>.
        ///
        /// Draw it BEFORE the thing casting it, offset toward the light's
        /// opposite corner. The alpha ramp is quadratic rather than linear
        /// because a linear one reads as a hard disc with a fuzzy edge instead
        /// of a shadow.
        ///
        /// This is the whole of what makes <see cref="Elements.ControlSkin.Soft"/>
        /// work, and it is why that skin needs a mid-tone panel: a shadow on
        /// black is invisible, so the shape it is separating never separates.
        /// </summary>
        public static void DrawSoftShadowCircle(this DrawManager manager, Vector2 center,
            float radius, Color color, float spread, int layers = 6)
        {
            if (radius <= 0.01f || spread <= 0.01f)
            {
                return;
            }

            layers = Math.Max(1, layers);

            for (int i = layers - 1; i >= 0; i--)
            {
                float t = (i + 1) / (float)layers;          // 1 = outermost
                float falloff = (1f - t) * (1f - t);
                // color * scalar is the fade idiom under the premultiplied
                // AlphaBlend GeometryBatch runs, so a caller that wants a
                // half-strength shadow passes Color.Black * 0.5f and the two
                // scalars compose correctly.
                manager.DrawFilledCircle(center, radius + spread * t, color * falloff);
            }
        }

        /// <summary>
        /// A ROUNDED filled rectangle — real vector geometry (the same
        /// fan-plus-feather technique as <see cref="DrawFilledCircle"/>/
        /// <see cref="DrawFilledCapsule"/>): four quarter-circle corner arcs
        /// closing a convex outline, filled from the rect's own centroid.
        /// No bake, no per-radius atlas cache, crisp at any DPI/zoom — a
        /// resizing panel (a UI-editor element being dragged by its handle, a
        /// panel tracking a window resize) costs nothing extra since there's
        /// no texture to miss-and-rebake in the first place.
        /// </summary>
        public static void DrawRoundedRectangle(this DrawManager manager, Rectangle rectangle, Color color, int radius)
        {
            if (rectangle.Width <= 0 || rectangle.Height <= 0)
            {
                return;
            }

            float r = Math.Min(radius, Math.Min(rectangle.Width, rectangle.Height) / 2f);
            if (r <= 0.01f)
            {
                manager.DrawFilledRectangle(rectangle, color);
                return;
            }

            // Six a corner, not three. ArcSegments is a budget for a whole
            // arc and the /4 shares it between the four corners, which is
            // right for a gently rounded box — but when the radius reaches
            // half the side the four corners ARE the shape, and the old floor
            // of three drew a circle as a twelve-sided polygon with visible
            // flats at any size a person actually looks at (the add-channel
            // and add-device rings, 22px across, were noticeably lumpy).
            // Six gives twenty-four sides, which reads as round. It only
            // binds below about a 27px radius, so nothing larger changes, and
            // the extra triangles on something this small are free.
            int segmentsPerCorner = Math.Max(6, ArcSegments(r, manager.RenderScale) / 4);
            (Vector2[] points, Vector2[] normals) = BuildRoundedRectOutline(rectangle, r, segmentsPerCorner);
            var centroid = new Vector2(rectangle.Left + rectangle.Width / 2f, rectangle.Top + rectangle.Height / 2f);
            AppendFeatheredFill(manager, points, normals, centroid, color);
        }

        /// <summary>
        /// The closed convex boundary of a rounded rect as 4 quarter-circle
        /// arcs (one per corner) — straight edges need no extra points, since
        /// <see cref="AppendFeatheredFill"/>'s fan triangulation already
        /// draws a straight line between any two non-adjacent-angle boundary
        /// points. Arc order (TL 180°→270°, TR 270°→360°, BR 0°→90°,
        /// BL 90°→180°) traces the perimeter in one consistent direction so
        /// consecutive corners' tangent points line up into the straight
        /// edges between them.
        /// </summary>
        private static (Vector2[] points, Vector2[] normals) BuildRoundedRectOutline(Rectangle rect, float radius, int segmentsPerCorner)
        {
            Span<float> startAngle = stackalloc float[] { 180f, 270f, 0f, 90f };
            Span<Vector2> centers = stackalloc Vector2[]
            {
                new Vector2(rect.Left + radius, rect.Top + radius),
                new Vector2(rect.Right - radius, rect.Top + radius),
                new Vector2(rect.Right - radius, rect.Bottom - radius),
                new Vector2(rect.Left + radius, rect.Bottom - radius),
            };

            int perCorner = segmentsPerCorner + 1;
            var points = new Vector2[perCorner * 4];
            var normals = new Vector2[perCorner * 4];
            int vi = 0;
            for (int c = 0; c < 4; c++)
            {
                for (int i = 0; i <= segmentsPerCorner; i++)
                {
                    float a = MathHelper.ToRadians(startAngle[c] + i * (90f / segmentsPerCorner));
                    var dir = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
                    normals[vi] = dir;
                    points[vi] = centers[c] + dir * radius;
                    vi++;
                }
            }

            return (points, normals);
        }

        /// <summary>Rounded-rect OUTLINE, drawn as a rounded fill with a
        /// smaller rounded fill punched out of it — needs the caller's
        /// backing colour, since the sprite batch has no stencil.</summary>
        public static void DrawRoundedBorder(this DrawManager manager, Rectangle rectangle, Color borderColor,
            Color interiorColor, int radius, int thickness = 1)
        {
            manager.DrawRoundedRectangle(rectangle, borderColor, radius);
            var inner = new Rectangle(
                rectangle.X + thickness, rectangle.Y + thickness,
                Math.Max(0, rectangle.Width - thickness * 2), Math.Max(0, rectangle.Height - thickness * 2));
            manager.DrawRoundedRectangle(inner, interiorColor, Math.Max(0, radius - thickness));
        }

        /// <summary>
        /// The outline of a row of butted rounded rectangles, drawn as a LINE
        /// and nothing else (bug board #212) — rounded at the two ends, and at
        /// every seam in <paramref name="seams"/> the two facing corner arcs
        /// with no line between them, so the top and bottom edges each pinch
        /// inwards and the block is never cut in half.
        ///
        /// Stroked rather than punched out of a fill, because what it goes
        /// over is a block face that may be a baked waveform, a mini piano
        /// roll or a live visualiser — anything that painted an interior would
        /// have to know which, and would be wrong the moment that changed.
        /// The cost is that the FACE behind it stays square; at the radius
        /// this is drawn at the eye reads the line, not the corner behind it.
        ///
        /// <paramref name="seams"/> is in rectangle-local x, ascending. A seam
        /// closer to an edge (or to its neighbour) than two radii is dropped:
        /// two arcs that would overlap read as a blob rather than a cusp.
        /// </summary>
        public static void DrawLoopOutline(this DrawManager manager, Rectangle rectangle, Color color,
            int radius, int thickness, ReadOnlySpan<float> seams)
        {
            if (rectangle.Width <= 0 || rectangle.Height <= 0 || color.A == 0)
            {
                return;
            }

            float t = Math.Max(1, thickness);
            float r = Math.Min(radius, Math.Min(rectangle.Width, rectangle.Height) / 2f);
            if (r < t)
            {
                manager.DrawRectangle(rectangle, color, (int)t);
                return;
            }

            // The x of every edge, ends included: [left, seam, seam, ..., right].
            Span<float> edges = stackalloc float[Math.Min(seams.Length, 64) + 2];
            int n = 0;
            edges[n++] = rectangle.Left;
            for (int i = 0; i < seams.Length && n < edges.Length - 1; i++)
            {
                float x = rectangle.Left + seams[i];
                if (x - edges[n - 1] >= r * 2f && rectangle.Right - x >= r * 2f)
                {
                    edges[n++] = x;
                }
            }

            edges[n++] = rectangle.Right;

            float top = rectangle.Top;
            float bottom = rectangle.Bottom - t;
            float quarter = MathHelper.PiOver2;

            for (int i = 0; i < n - 1; i++)
            {
                float x0 = edges[i];
                float x1 = edges[i + 1];
                float straight = Math.Max(0f, (x1 - r) - (x0 + r));

                if (straight > 0f)
                {
                    manager.DrawFilledRectangle(new Rectangle((int)(x0 + r), (int)top, (int)straight, (int)t), color);
                    manager.DrawFilledRectangle(new Rectangle((int)(x0 + r), (int)bottom, (int)straight, (int)t), color);
                }

                // The four corner arcs of THIS pass. At a seam the two
                // neighbouring passes each draw their own, and the pair is the
                // cusp; at the two ends they are the rounded corners.
                var tl = new Vector2(x0 + r, rectangle.Top + r);
                var tr = new Vector2(x1 - r, rectangle.Top + r);
                var br = new Vector2(x1 - r, rectangle.Bottom - r);
                var bl = new Vector2(x0 + r, rectangle.Bottom - r);
                manager.DrawRingArc(tl, r - t, r, color, MathHelper.Pi, quarter);
                manager.DrawRingArc(tr, r - t, r, color, MathHelper.Pi + quarter, quarter);
                manager.DrawRingArc(br, r - t, r, color, 0f, quarter);
                manager.DrawRingArc(bl, r - t, r, color, quarter, quarter);

                // The verticals close the two ENDS only. A seam is where the
                // block does not end, so it does not get one — that is the
                // whole difference between this and a row of separate boxes.
                float side = Math.Max(0f, rectangle.Height - r * 2f);
                if (side > 0f && i == 0)
                {
                    manager.DrawFilledRectangle(new Rectangle((int)x0, (int)(rectangle.Top + r), (int)t, (int)side), color);
                }

                if (side > 0f && i == n - 2)
                {
                    manager.DrawFilledRectangle(new Rectangle((int)(x1 - t), (int)(rectangle.Top + r), (int)t, (int)side), color);
                }
            }
        }

        public static void SaveTextureData(this RenderTarget2D texture, string filename)
        {
            using (var stream = File.OpenWrite(filename))
            {
                texture.SaveAsPng(stream, texture.Width, texture.Height);
            }
        }
    }
}
