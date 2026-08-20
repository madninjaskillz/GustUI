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
    internal static class ShapeDrawExtensions
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
        public static void DrawCubicBezier(this DrawManager manager, Vector2 p0, Vector2 c0, Vector2 c1, Vector2 p1,
            Color color, int thickness = 2, int segments = 24)
        {
            Vector2 previous = p0;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                float u = 1f - t;
                Vector2 point =
                    u * u * u * p0
                    + 3f * u * u * t * c0
                    + 3f * u * t * t * c1
                    + t * t * t * p1;
                manager.DrawThickLine(previous, point, color, thickness);
                previous = point;
            }
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

            int segmentsPerCorner = Math.Max(3, ArcSegments(r, manager.RenderScale) / 4);
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

        public static void SaveTextureData(this RenderTarget2D texture, string filename)
        {
            using (var stream = File.OpenWrite(filename))
            {
                texture.SaveAsPng(stream, texture.Width, texture.Height);
            }
        }
    }
}
