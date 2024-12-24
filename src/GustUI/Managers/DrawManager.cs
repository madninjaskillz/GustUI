using GustUI.Extensions;
using GustUI.Traits;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Managers
{
    public class DrawManager
    {
        private RenderTarget2D currentTarget;
        private RenderTarget2D renderTarget;
        private SpriteBatch spriteBatch;
        private RenderTarget2D renderTargetClone;
        public bool IsInBatch { get; private set; } = false;
        private FrameCounter _frameCounter = new FrameCounter();
        private SpriteFont font = null;
        private RasterizerState rasterizerState = new RasterizerState() { MultiSampleAntiAlias = false, ScissorTestEnable = true };
        private BlendState blendState = null;
        private SamplerState samplerState = null;

        public DrawManager(SpriteBatch spriteBatch)
        {
            this.spriteBatch = spriteBatch;
        }

        private RenderTarget2D GetRT()
        {
            var sz = Resources.StaticResources.RootWindow.ElementTrait<SizeTrait>().Value();
            if (renderTarget == null || renderTarget.Width != sz.X || renderTarget.Height != sz.Y)
            {
                renderTarget = new RenderTarget2D(Resources.StaticResources.GraphicsDevice, (int)sz.X, (int)sz.Y);
            }
            return renderTarget;
        }

        float debugBottom = 0;

        public void DrawLoop(GameTime gameTime)
        {
            var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            _frameCounter.Update(deltaTime);


            SetRenderTarget(null);
            Clear(Color.Transparent);
            Begin();
            Resources.StaticResources.RootWindow.Draw();
            var lerpSpeed = 0.5f;
            if (Resources.StaticResources.DebugMode == DebugMode.Full)
            {
                debugBottom = MathHelper.Lerp(debugBottom, (Resources.StaticResources.RootWindow.GetSize().Y - 50), lerpSpeed);
            }
            else if (Resources.StaticResources.DebugMode == DebugMode.Mini)
            {
                debugBottom = MathHelper.Lerp(debugBottom, 200, lerpSpeed);
            }
            else
            {
                debugBottom = MathHelper.Lerp(debugBottom, 0, lerpSpeed);
            }
            int bottom = (int)debugBottom;

            if (Resources.StaticResources.DebugMode != DebugMode.None)
            {

                var fps = string.Format("FPS: {0}", _frameCounter.AverageFramesPerSecond);
                if (Resources.StaticResources.DebugMode != DebugMode.FPS)
                {
                    fps += "\r\n" + Resources.StaticResources.RootWindow.GetSize().ToString();
                    fps += "\r\n" + Resources.StaticResources.InputManager.FloatedElementCount + " " + Resources.StaticResources.InputManager.FloatedElementName;
                }
                if (font == null)
                {
                    font = Resources.StaticResources.FontManager.LoadFont(Resources.StaticResources.Theme.UiFontSmall.Family, Resources.StaticResources.Theme.UiFontSmall.Size);
                }
                Vector2 ps = new Vector2(0, Resources.StaticResources.RootWindow.GetSize().Y - 70);
                DrawString(font, fps, ps + new Vector2(1, 1), Color.Black);
                DrawString(font, fps, ps + new Vector2(3, 1), Color.Black);
                DrawString(font, fps, ps + new Vector2(1, 3), Color.Black);
                DrawString(font, fps, ps + new Vector2(3, 3), Color.Black);
                DrawString(font, fps, ps + new Vector2(2, 2), Color.White);

                
                SpriteBatchExtensions.DrawFilledRectangle(this, new Rectangle(0, 0, (int)Resources.StaticResources.RootWindow.GetSize().X, bottom), Color.Blue * 0.8f);

                string consoleText = "CMD:>";
                var ctHeight = (int)font.MeasureString(consoleText).Y;
                var ctBorder = 2;
                SpriteBatchExtensions.DrawFilledRectangle(this, new Rectangle(ctBorder, bottom - ctHeight - (ctBorder * 2), (int)Resources.StaticResources.RootWindow.GetSize().X - (ctBorder * 2), ctHeight + ctBorder), Color.Black * 0.8f);
                bottom = bottom - ctHeight - (ctBorder * 2);

                DrawString(font, consoleText, new Vector2(5, 0) + new Vector2(0, bottom), Color.Black);
                DrawString(font, consoleText, new Vector2(5, 0) + new Vector2(2, bottom), Color.Black);
                DrawString(font, consoleText, new Vector2(5, 0) + new Vector2(0, bottom + 2), Color.Black);
                DrawString(font, consoleText, new Vector2(5, 0) + new Vector2(2, bottom + 2), Color.Black);
                DrawString(font, consoleText, new Vector2(5, 0) + new Vector2(1, bottom + 1), Color.Yellow);

            }


            if (debugBottom > 1)
            {

                for (int i = Log.log.Count; i > 0; i--)
                {
                    var height = ((int)font.MeasureString(Log.log.ToArray()[i - 1].ToString()).Y) + 4;
                    bottom = bottom - height;
                
                    DrawString(font, Log.log.ToArray()[i - 1].ToString(), new Vector2(5, 0) + new Vector2(0, bottom), Color.Black*0.5f);
                    DrawString(font, Log.log.ToArray()[i - 1].ToString(), new Vector2(5, 0) + new Vector2(2, bottom), Color.Black * 0.5f);
                    DrawString(font, Log.log.ToArray()[i - 1].ToString(), new Vector2(5, 0) + new Vector2(0, bottom + 2), Color.Black * 0.5f);
                    DrawString(font, Log.log.ToArray()[i - 1].ToString(), new Vector2(5, 0) + new Vector2(2, bottom + 2), Color.Black * 0.5f);
                    DrawString(font, Log.log.ToArray()[i - 1].ToString(), new Vector2(5, 0) + new Vector2(1, bottom + 1), Color.White);

                    if (bottom < 0)
                    {
                        break;
                    }
                }
            }
            if (Resources.StaticResources.DebugMode == DebugMode.Outlines)
            {
                Resources.StaticResources.RootWindow.DebugDraw();
            }
            
            End();
        }

        private void DrawString(SpriteFont font, string fps, Vector2 vector2, Color white)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            spriteBatch.DrawString(font, fps, vector2, white);
        }

        private void Clear(Color color)
        {
            Resources.StaticResources.GraphicsDevice.Clear(color);
        }

        private void SetRenderTarget(RenderTarget2D renderTarget2D)
        {
            Resources.StaticResources.GraphicsDevice.SetRenderTarget(renderTarget2D);
            currentTarget = renderTarget2D;
        }



        public Texture2D GetTargetClone()
        {
            bool wasInBatch = IsInBatch;
            if (wasInBatch)
            {
                End();
            }

            if (renderTargetClone == null || renderTargetClone.Width != renderTarget.Width || renderTargetClone.Height != renderTarget.Height)
            {
                renderTargetClone = new RenderTarget2D(Resources.StaticResources.GraphicsDevice, renderTarget.Width, renderTarget.Height);
            }

            var preTarget = currentTarget;
            Resources.StaticResources.GraphicsDevice.SetRenderTarget(renderTargetClone);
            Resources.StaticResources.GraphicsDevice.Clear(Microsoft.Xna.Framework.Color.Transparent);
            Begin();
            Draw(renderTarget, new Rectangle(0, 0, renderTarget.Width, renderTarget.Height), Microsoft.Xna.Framework.Color.White);
            End();
            Resources.StaticResources.GraphicsDevice.SetRenderTarget(preTarget);

            if (wasInBatch)
            {
                Begin();
            }

            return renderTargetClone;
        }

        public Texture2D GetScaledTargetClone(float ratio)
        {
            bool wasInBatch = IsInBatch;
            if (wasInBatch)
            {
                End();
            }

            var tempTarget = new RenderTarget2D(Resources.StaticResources.GraphicsDevice, (int)(renderTarget.Width * ratio), (int)(renderTarget.Height * ratio));


            var preTarget = currentTarget;
            Resources.StaticResources.GraphicsDevice.SetRenderTarget(tempTarget);
            Resources.StaticResources.GraphicsDevice.Clear(Color.Transparent);
            Begin();
            Draw(renderTarget, new Rectangle(0, 0, (int)(renderTarget.Width * ratio), (int)(renderTarget.Height * ratio)), Microsoft.Xna.Framework.Color.White);
            End();
            Resources.StaticResources.GraphicsDevice.SetRenderTarget(preTarget);

            if (wasInBatch)
            {
                Begin();
            }

            return tempTarget;
        }

        public Texture2D GetBlurredTargetClone(float ratio)
        {
            return null;

            bool wasInBatch = IsInBatch;
            if (wasInBatch)
            {
                End();
            }
            Color[] data = new Color[renderTarget.Width * renderTarget.Height];
            renderTarget.GetData<Color>(data);
            Texture2D texture = new Texture2D(Resources.StaticResources.GraphicsDevice, renderTarget.Width, renderTarget.Height);
            texture.SetData<Color>(data);

            if (wasInBatch)
            {
                Begin();
            }
            return texture;
        }


        public void Begin()
        {
            IsInBatch = true;
            spriteBatch.Begin(SpriteSortMode.Immediate, blendState, samplerState, null, rasterizerState, null, null);
        }

        public void End()
        {
            spriteBatch.End();
            IsInBatch = false;
        }

        internal void DrawString(SpriteFont font, string text, Vector2 vector2, Color color, int v1, Vector2 zero, float fontScale, SpriteEffects none, float v2)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            spriteBatch.DrawString(font, text, vector2, color, v1, zero, fontScale, none, v2);
        }

        internal void Draw(Texture2D pixel, Rectangle rectangle, object value, Color color, float angle, Vector2 vector2, SpriteEffects none, int v)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            spriteBatch.Draw(pixel, rectangle, null, color, angle, vector2, none, v);
        }

        internal void Draw(Texture2D pixel, Vector2 position, Color color)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            spriteBatch.Draw(pixel, position, color);
        }

        internal void Draw(Texture2D pixel, Rectangle rectangle, Color color)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            spriteBatch.Draw(pixel, rectangle, color);
        }

        internal void Draw(Texture2D texture, Rectangle rectangle, Rectangle? source, Color color)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            spriteBatch.Draw(texture, rectangle, source, color);
        }
    }
}
