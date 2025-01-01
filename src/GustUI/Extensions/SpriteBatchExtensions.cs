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
                            color * BorderFade,
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
