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
using static GustUI.Managers.FontManager;
using static System.Net.Mime.MediaTypeNames;

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
        private KeyedSpriteFont font = null;
        private RasterizerState rasterizerState = new RasterizerState() { MultiSampleAntiAlias = false, ScissorTestEnable = true };
        private BlendState blendState = null;
        private SamplerState samplerState = null;

        /// <summary>
        /// Uniform scale applied to every draw via <see cref="Begin"/>'s
        /// SpriteBatch transform matrix (1 = off, the default). Pairs with
        /// WindowElement.DevicePixelRatio: that keeps GustUI's own layout/
        /// hit-testing in the ORIGINAL logical space (unaware anything HiDPI
        /// is happening); this magnifies the composited output to fill
        /// whatever larger physical backbuffer the host set up for crisp
        /// rendering on a scaled display. Set both to the SAME value.
        /// </summary>
        public float RenderScale { get; set; } = 1f;

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

            FrameProfiler.Begin(FrameProfiler.Bucket.DrawFontCache);
            Resources.StaticResources.FontManager.ManageCaches();
            FrameProfiler.End(FrameProfiler.Bucket.DrawFontCache);

            SetRenderTarget(null);
            Clear(Color.Transparent);
            Begin();
            FrameProfiler.Begin(FrameProfiler.Bucket.DrawRoot);
            Elements.Element.BeginPositionCache();
            Resources.StaticResources.RootWindow.Draw();
            Elements.Element.EndPositionCache();
            FrameProfiler.End(FrameProfiler.Bucket.DrawRoot);
            FrameProfiler.Begin(FrameProfiler.Bucket.DrawDebug);
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
                    fps += "\r\n" + Resources.StaticResources.FontManager.CacheInfo;
                    if (Resources.StaticResources.DebugMode == DebugMode.Outlines)
                    {
                        Resources.StaticResources.RootWindow.DebugWrite(0, 160);
                    }
                }
                if (font == null)
                {
                    font = Resources.StaticResources.FontManager.LoadFont(Resources.StaticResources.Theme.UiFontSmall.Family, Resources.StaticResources.Theme.UiFontSmall.Size);
                }
                Vector2 ps = new Vector2(0, 60);
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

                    DrawString(font, Log.log.ToArray()[i - 1].ToString(), new Vector2(5, 0) + new Vector2(0, bottom), Color.Black * 0.5f);
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

            FrameProfiler.End(FrameProfiler.Bucket.DrawDebug);
            End();

            
        }

        internal void DrawString(KeyedSpriteFont font, string text, Vector2 position, Color white)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            FrameProfiler.CountString();
            var cache = Resources.StaticResources.FontManager.GetCachedText(font.Key, text, white);
            if (cache == null)
            {
                spriteBatch.DrawString(font.SpriteFont, text, position, white);
            }
            else
            {

                spriteBatch.Draw(cache, position, white);

            }
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


        public void Begin(SpriteSortMode mode = SpriteSortMode.Deferred)
        {
            IsInBatch = true;
            FrameProfiler.CountFlush();
            // Z MUST stay 1. Matrix.CreateScale(float) scales all THREE axes,
            // and SpriteBatch's transform is a real 3D matrix applied to real
            // 3D vertices: a sprite's layerDepth is its vertex Z, and the
            // orthographic projection SpriteEffect folds in leaves Z alone
            // (M33=1, M43=0, M44=1), so clip-space z == layerDepth * M33 with
            // w == 1. Clipping keeps only -w <= z <= w, i.e. layerDepth 1.0 —
            // which SpriteBatchExtensions.DrawString passes for EVERY string —
            // sits exactly ON the far plane. Scaling Z by 1.5 pushed it to 1.5,
            // outside the frustum, and the GPU discarded every glyph quad:
            // all text vanished from the first frame, silently, with no GL
            // error, while rectangles (drawn at the default layerDepth 0)
            // were untouched. Scaling only X/Y is what "scale the 2D output"
            // actually means here.
            Matrix? transform = RenderScale != 1f ? Matrix.CreateScale(RenderScale, RenderScale, 1f) : null;
            spriteBatch.Begin(mode, blendState, samplerState, null, rasterizerState, null, transform);
        }

        public void End()
        {
            spriteBatch.End();
            IsInBatch = false;
        }

        internal void DrawString(KeyedSpriteFont font, string text, Vector2 vector2, Color color, int v1, Vector2 zero, float fontScale, SpriteEffects none, float v2)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            FrameProfiler.CountString();
            var cache = Resources.StaticResources.FontManager.GetCachedText(font.Key, text, color);
            if (cache == null)
            {
                spriteBatch.DrawString(font.SpriteFont, text, vector2, color, v1, zero, fontScale, none, v2);
            }
            else
            {
                spriteBatch.Draw(cache, vector2,null, color, 0, Vector2.Zero, fontScale, none, 0);
            }
        }

        internal void Draw(Texture2D pixel, Rectangle rectangle, object value, Color color, float angle, Vector2 vector2, SpriteEffects none, int v)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            FrameProfiler.CountSprite();
            spriteBatch.Draw(pixel, rectangle, null, color, angle, vector2, none, v);
        }

        internal void Draw(Texture2D pixel, Vector2 position, Color color)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            FrameProfiler.CountSprite();
            spriteBatch.Draw(pixel, position, color);
        }

        internal void Draw(Texture2D pixel, Rectangle rectangle, Color color)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            FrameProfiler.CountSprite();
            spriteBatch.Draw(pixel, rectangle, color);
        }

        internal void Draw(Texture2D texture, Rectangle rectangle, Rectangle? source, Color color)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            FrameProfiler.CountSprite();
            spriteBatch.Draw(texture, rectangle, source, color);
        }

        // Public clipping API: nested scissors intersect with the enclosing one.
        private readonly Stack<Rectangle> scissorStack = new Stack<Rectangle>();

        public void PushScissor(Rectangle rect)
        {
            // Callers (Element.Draw's ClipChildren path) build this rect from
            // an element's own logical position/size — the SAME space
            // RenderScale's Begin() transform already magnifies draws FROM.
            // Scissor rects are raw physical-pixel device coordinates and
            // are NOT affected by that transform (scissor testing happens
            // post-transform, at the rasterizer), so without this scale here
            // too, a magnified element's own content routinely exceeds its
            // un-magnified clip rect and gets scissored away entirely —
            // text is especially prone to this since it's almost always
            // inside a tightly-fit clipped label/panel.
            Rectangle scaled = RenderScale != 1f
                ? new Rectangle(
                    (int)(rect.X * RenderScale), (int)(rect.Y * RenderScale),
                    (int)(rect.Width * RenderScale), (int)(rect.Height * RenderScale))
                : rect;
            Rectangle clipped = scissorStack.Count > 0 ? Rectangle.Intersect(scissorStack.Peek(), scaled) : scaled;
            scissorStack.Push(clipped);
            SetScissor(clipped);
        }

        public void PopScissor()
        {
            if (scissorStack.Count > 0)
            {
                scissorStack.Pop();
            }

            SetScissor(scissorStack.Count > 0 ? scissorStack.Peek() : (Rectangle?)null);
        }

        public void SetScissor(Rectangle? rect)
        {
            if (rect.HasValue)
            {
                End();
                Begin();
                Resources.StaticResources.GraphicsDevice.ScissorRectangle = rect.Value;
            }
            else
            {
                End();

                Resources.StaticResources.GraphicsDevice.ScissorRectangle = new Rectangle(
                    0, 0,
                    (int)(Resources.StaticResources.RootWindow.GetSize().X * RenderScale),
                    (int)(Resources.StaticResources.RootWindow.GetSize().Y * RenderScale));
                Begin();
            }
        }
    }
}
