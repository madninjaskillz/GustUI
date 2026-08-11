using GustUI.Managers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static GustUI.Managers.FontManager;

namespace GustUI.Extensions
{
    internal static class SpriteBatchExtensions
    {
        private const float BorderFade = 0.1f;

        public static void DrawString(this DrawManager spriteBatch, KeyedSpriteFont font, string text, Vector2 position, Color color, int borderSize)
        {
            DrawString(spriteBatch, font, text, position, color, borderSize, null);
        }

        public static void DrawString(this DrawManager spriteBatch, KeyedSpriteFont font, string text, Vector2 position, Color color, int borderSize, Color? borderColor)
        {
            // font.DrawScale, not bare FontScale — the glyph atlas was
            // baked (RenderScale-dependent) supersample times bigger than
            // the final on-screen size (FontManager.LoadFont), so this
            // scale-down exactly cancels that back out to the same net
            // size while sampling from a higher-detail source bake.
            float drawScale = font.DrawScale;

            Color outline = borderColor ?? color * BorderFade;
            for (var x = -borderSize; x <= borderSize; x++)
            {
                for (var y = -borderSize; y <= borderSize; y++)
                {
                    if (x != 0 || y != 0)
                    {
                        spriteBatch.DrawString(
                            font,
                            text,
                            position + new Vector2(x, y),
                            outline,
                            0,
                            Vector2.Zero,
                            drawScale,
                            SpriteEffects.None,
                            1f);
                    }
                }
            }

            spriteBatch.DrawString(font, text, position, color, 0, Vector2.Zero, drawScale, SpriteEffects.None, 1f);

        }

        public static void DrawLine(this DrawManager spriteBatch, Vector2 start, Vector2 end, Color color)
        {
            Vector2 edge = end - start;
            float angle = (float)Math.Atan2(edge.Y, edge.X);

            spriteBatch.Draw(Resources.StaticResources.Pixel,
                new Rectangle((int)start.X, (int)start.Y, (int)edge.Length(), 1),
                null,
                color,
                angle,
                new Vector2(0, 0),
                SpriteEffects.None,
                0);

        }

        /// <summary>DrawLine with a pixel thickness (rotated filled rect).</summary>
        public static void DrawThickLine(this DrawManager spriteBatch, Vector2 start, Vector2 end, Color color, int thickness)
        {
            Vector2 edge = end - start;
            float angle = (float)Math.Atan2(edge.Y, edge.X);

            spriteBatch.Draw(Resources.StaticResources.Pixel,
                new Rectangle((int)start.X, (int)start.Y, (int)edge.Length() + 1, thickness),
                null,
                color,
                angle,
                new Vector2(0, thickness / 2f),
                SpriteEffects.None,
                0);
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
            for (int i = 0; i < borderSize; i++)
            {
                spriteBatch.Draw(Resources.StaticResources.Pixel, new Rectangle(rectangle.Left, rectangle.Top + i, rectangle.Width, 1), color);
                spriteBatch.Draw(Resources.StaticResources.Pixel, new Rectangle(rectangle.Left, rectangle.Bottom - i, rectangle.Width, 1), color);

                spriteBatch.Draw(Resources.StaticResources.Pixel, new Rectangle(rectangle.Left + i, rectangle.Top, 1, rectangle.Height), color);
                spriteBatch.Draw(Resources.StaticResources.Pixel, new Rectangle(rectangle.Right - i, rectangle.Top, 1, rectangle.Height), color);
            }
        }

        public static void DrawFilledRectangle(this DrawManager spriteBatch, Rectangle rectangle, Color color)
        {
            spriteBatch.Draw(Resources.StaticResources.Pixel, rectangle, color);
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

            Texture2D corners = GetCornerDisc(radius);
            int d = radius * 2;

            // Four corner quadrants, sampled out of the one baked disc.
            spriteBatch.Draw(corners, new Rectangle(rectangle.Left, rectangle.Top, radius, radius),
                new Rectangle(0, 0, radius, radius), color);
            spriteBatch.Draw(corners, new Rectangle(rectangle.Right - radius, rectangle.Top, radius, radius),
                new Rectangle(radius, 0, radius, radius), color);
            spriteBatch.Draw(corners, new Rectangle(rectangle.Left, rectangle.Bottom - radius, radius, radius),
                new Rectangle(0, radius, radius, radius), color);
            spriteBatch.Draw(corners, new Rectangle(rectangle.Right - radius, rectangle.Bottom - radius, radius, radius),
                new Rectangle(radius, radius, radius, radius), color);

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

        private static readonly Dictionary<int, Texture2D> CornerDiscCache = new Dictionary<int, Texture2D>();

        /// <summary>Antialiased filled disc of diameter 2r — the corner atlas
        /// for <see cref="DrawRoundedRectangle"/>. Same alpha-mask/SetData
        /// idiom KnobElement and ToggleSwitchElement bake their shapes with.</summary>
        private static Texture2D GetCornerDisc(int radius)
        {
            if (CornerDiscCache.TryGetValue(radius, out Texture2D cached))
            {
                return cached;
            }

            int d = radius * 2;
            var data = new Color[d * d];
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

            var texture = new Texture2D(Resources.StaticResources.GraphicsDevice, d, d);
            texture.SetData(data);
            CornerDiscCache[radius] = texture;
            return texture;
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
