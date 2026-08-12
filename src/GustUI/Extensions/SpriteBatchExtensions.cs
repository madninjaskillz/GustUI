using GustUI.Managers;
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
    internal static class SpriteBatchExtensions
    {
        public static void DrawLine(this DrawManager spriteBatch, Vector2 start, Vector2 end, Color color)
        {
            Vector2 edge = end - start;
            float angle = (float)Math.Atan2(edge.Y, edge.X);
            var rect = new Rectangle((int)start.X, (int)start.Y, (int)edge.Length(), 1);

            AtlasRegion white = spriteBatch.GeometryAtlas.WhiteRegion;
            spriteBatch.GeometryBatch.AppendRotatedQuad(white.Texture, rect, white.Pixels, color, angle, new Vector2(0, 0), spriteBatch.GetClipRectForGeometry(), null);
        }

        /// <summary>DrawLine with a pixel thickness (rotated filled rect).</summary>
        public static void DrawThickLine(this DrawManager spriteBatch, Vector2 start, Vector2 end, Color color, int thickness)
        {
            Vector2 edge = end - start;
            float angle = (float)Math.Atan2(edge.Y, edge.X);
            var rect = new Rectangle((int)start.X, (int)start.Y, (int)edge.Length() + 1, thickness);

            AtlasRegion white = spriteBatch.GeometryAtlas.WhiteRegion;
            spriteBatch.GeometryBatch.AppendRotatedQuad(white.Texture, rect, white.Pixels, color, angle, new Vector2(0, thickness / 2f), spriteBatch.GetClipRectForGeometry(), null);
        }

        /// <summary>
        /// Cubic Bézier as a sampled polyline of thick segments — the house
        /// "sampled geometry IS the curve" idiom (no curve primitive exists in
        /// the sprite batch and none is needed).
        /// </summary>
        public static void DrawCubicBezier(this DrawManager spriteBatch, Vector2 p0, Vector2 c0, Vector2 c1, Vector2 p1,
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
                spriteBatch.DrawThickLine(previous, point, color, thickness);
                previous = point;
            }
        }

        public static void DrawRectangle(this DrawManager spriteBatch, Rectangle rectangle, Color color, int borderSize = 1)
        {
            // Delegates to DrawFilledRectangle (not a direct Pixel draw)
            // specifically so border strokes automatically pick up the
            // geometry-backend routing below without duplicating it here.
            for (int i = 0; i < borderSize; i++)
            {
                spriteBatch.DrawFilledRectangle(new Rectangle(rectangle.Left, rectangle.Top + i, rectangle.Width, 1), color);
                spriteBatch.DrawFilledRectangle(new Rectangle(rectangle.Left, rectangle.Bottom - i, rectangle.Width, 1), color);

                spriteBatch.DrawFilledRectangle(new Rectangle(rectangle.Left + i, rectangle.Top, 1, rectangle.Height), color);
                spriteBatch.DrawFilledRectangle(new Rectangle(rectangle.Right - i, rectangle.Top, 1, rectangle.Height), color);
            }
        }

        public static void DrawFilledRectangle(this DrawManager spriteBatch, Rectangle rectangle, Color color)
        {
            AtlasRegion white = spriteBatch.GeometryAtlas.WhiteRegion;
            spriteBatch.GeometryBatch.AppendQuad(white.Texture, rectangle, white.Pixels, color, spriteBatch.GetClipRectForGeometry(), null);
        }

        /// <summary>
        /// A ROUNDED filled rectangle — the missing primitive between
        /// <see cref="DrawFilledRectangle"/> (sharp) and the per-element
        /// baked SDF masks (KnobElement's disc, ToggleSwitchElement's pill,
        /// each hard-wired to one shape at one size).
        ///
        /// Baked per RADIUS only, never per rect size: one antialiased
        /// quarter-disc atlas of side 2r is cached and blitted into the four
        /// corners, and the interior is five plain pixel-stretch rects. So a
        /// resizing panel — a UI-editor element being dragged by its handle,
        /// a panel tracking a window resize — costs no new textures per
        /// frame, which a naive "bake the whole rounded rect" cache does.
        /// </summary>
        public static void DrawRoundedRectangle(this DrawManager spriteBatch, Rectangle rectangle, Color color, int radius)
        {
            if (rectangle.Width <= 0 || rectangle.Height <= 0)
            {
                return;
            }

            radius = Math.Min(radius, Math.Min(rectangle.Width, rectangle.Height) / 2);
            if (radius <= 0)
            {
                spriteBatch.DrawFilledRectangle(rectangle, color);
                return;
            }

            AtlasRegion corners = GetCornerDisc(spriteBatch, radius);
            Rectangle atlasRect = corners.Pixels;
            int d = radius * 2;

            // Four corner quadrants, sampled out of the one baked disc —
            // offsets now relative to atlasRect's own packed position
            // (TextureAtlas.GetOrBake), not (0,0) of a standalone texture.
            spriteBatch.Draw(corners.Texture, new Rectangle(rectangle.Left, rectangle.Top, radius, radius),
                new Rectangle(atlasRect.X, atlasRect.Y, radius, radius), color);
            spriteBatch.Draw(corners.Texture, new Rectangle(rectangle.Right - radius, rectangle.Top, radius, radius),
                new Rectangle(atlasRect.X + radius, atlasRect.Y, radius, radius), color);
            spriteBatch.Draw(corners.Texture, new Rectangle(rectangle.Left, rectangle.Bottom - radius, radius, radius),
                new Rectangle(atlasRect.X, atlasRect.Y + radius, radius, radius), color);
            spriteBatch.Draw(corners.Texture, new Rectangle(rectangle.Right - radius, rectangle.Bottom - radius, radius, radius),
                new Rectangle(atlasRect.X + radius, atlasRect.Y + radius, radius, radius), color);

            // Interior: top strip, bottom strip, and the full-width middle.
            spriteBatch.DrawFilledRectangle(
                new Rectangle(rectangle.Left + radius, rectangle.Top, rectangle.Width - d, radius), color);
            spriteBatch.DrawFilledRectangle(
                new Rectangle(rectangle.Left + radius, rectangle.Bottom - radius, rectangle.Width - d, radius), color);
            spriteBatch.DrawFilledRectangle(
                new Rectangle(rectangle.Left, rectangle.Top + radius, rectangle.Width, rectangle.Height - d), color);
        }

        /// <summary>Rounded-rect OUTLINE, drawn as a rounded fill with a
        /// smaller rounded fill punched out of it — needs the caller's
        /// backing colour, since the sprite batch has no stencil.</summary>
        public static void DrawRoundedBorder(this DrawManager spriteBatch, Rectangle rectangle, Color borderColor,
            Color interiorColor, int radius, int thickness = 1)
        {
            spriteBatch.DrawRoundedRectangle(rectangle, borderColor, radius);
            var inner = new Rectangle(
                rectangle.X + thickness, rectangle.Y + thickness,
                Math.Max(0, rectangle.Width - thickness * 2), Math.Max(0, rectangle.Height - thickness * 2));
            spriteBatch.DrawRoundedRectangle(inner, interiorColor, Math.Max(0, radius - thickness));
        }

        /// <summary>Antialiased filled disc of diameter 2r — the corner atlas
        /// for <see cref="DrawRoundedRectangle"/>. Baked into the shared
        /// TextureAtlas (Phase 4 of the geometry-renderer migration) instead
        /// of its own standalone Texture2D, so a panel's rounded corners can
        /// share a GeometryBatch segment with everything else atlas-packed
        /// (knob dial/ring, toggle pill, etc.) rather than forcing a
        /// texture-swap boundary.</summary>
        private static AtlasRegion GetCornerDisc(DrawManager spriteBatch, int radius)
        {
            int d = radius * 2;
            return spriteBatch.GeometryAtlas.GetOrBake($"corner{radius}", d, d, data =>
            {
                float r = radius;
                for (int y = 0; y < d; y++)
                {
                    for (int x = 0; x < d; x++)
                    {
                        float dx = x - r + 0.5f;
                        float dy = y - r + 0.5f;
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                        float alpha = MathHelper.Clamp(r - dist, 0f, 1f);
                        data[y * d + x] = alpha <= 0f ? Color.Transparent : Color.White * alpha;
                    }
                }
            });
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
