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
                            GustConstants.FontScale,
                            SpriteEffects.None,
                            1f);
                    }
                }
            }

            spriteBatch.DrawString(font, text, position, color, 0, Vector2.Zero, GustConstants.FontScale, SpriteEffects.None, 1f);

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



        public static void SaveTextureData(this RenderTarget2D texture, string filename)
        {
            using (var stream = File.OpenWrite(filename))
            {
                texture.SaveAsPng(stream, texture.Width, texture.Height);
            }
        }
    }
}
