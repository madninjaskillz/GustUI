using GustUI.Extensions;
using GustUI.Traits;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SpriteFontPlus;
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
        // CullMode.None: SpriteBatch's own quads are unaffected by cull mode
        // either way, but DrawTriangles' custom geometry (DrawManager.cs)
        // very much is — leaving the default CullCounterClockwiseFace would
        // silently drop every triangle whose winding doesn't happen to
        // match after the 2D Y-down orthographic projection, i.e. an
        // invisible shape with no error. A 2D UI has no back-face concept
        // worth culling for anyway.
        private RasterizerState rasterizerState = new RasterizerState() { MultiSampleAntiAlias = false, ScissorTestEnable = true, CullMode = CullMode.None };
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

        private readonly List<Action> pendingBakes = new List<Action>();

        /// <summary>
        /// Defers a render-target bake (anything that calls SetRenderTarget,
        /// e.g. BakeTrianglesToTexture) to the next safe point — right here
        /// in DrawLoop, BEFORE SetRenderTarget(null)/Clear sets up the
        /// backbuffer for the frame. Queuing a bake instead of running it
        /// immediately, mid-scene, is not an optimization: GraphicsDevice
        /// discards a render target's contents when you switch away from it
        /// (the default RenderTargetUsage.DiscardContents), and that
        /// includes the BACKBUFFER — a bake triggered mid-traversal (e.g. a
        /// waveform block's lazy cache miss, discovered while drawing this
        /// frame's UI to the backbuffer) steals the render target out from
        /// under the frame already in progress and corrupts it (observed as
        /// a solid/garbage-colored flash — "purple screen" — for that
        /// frame). Queuing means: this frame, the caller keeps drawing
        /// whatever it already had (stale texture or nothing); the bake
        /// itself runs at the very start of the NEXT frame, before the
        /// backbuffer has been touched at all, so there is nothing to
        /// corrupt. One frame of staleness, never a corrupted frame.
        /// </summary>
        public void QueuePendingBake(Action bake)
        {
            pendingBakes.Add(bake);
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

            if (pendingBakes.Count > 0)
            {
                // Snapshot + clear BEFORE running: a bake can itself queue
                // another one (e.g. a still-settling resize drag re-misses
                // the moment its own bake lands), which must wait for the
                // NEXT frame, not re-enter this same pass.
                var bakes = pendingBakes.ToArray();
                pendingBakes.Clear();
                foreach (Action bake in bakes)
                {
                    bake();
                }
            }

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

        /// <summary>
        /// Switches the active SpriteBatch to additive blending — same
        /// End()/Begin()-to-change-GPU-state-mid-frame shape as
        /// <see cref="SetScissor"/> (blend state, like blend mode or the
        /// scissor rect, can only change between batches, not within one).
        /// For a true glow/bloom stroke: draw the same line 2-3× with
        /// increasing thickness and decreasing alpha inside a
        /// BeginAdditive()/<see cref="EndAdditive"/> pair so overlapping
        /// passes brighten instead of just alpha-composing over each other.
        /// Caller must already be inside a Begin()/End() batch (mirrors
        /// SetScissor's assumption) and must pair this with EndAdditive
        /// before any non-additive drawing resumes.
        /// </summary>
        public void BeginAdditive()
        {
            End();
            blendState = BlendState.Additive;
            Begin();
        }

        /// <summary>Restores normal alpha blending after <see cref="BeginAdditive"/>.</summary>
        public void EndAdditive()
        {
            End();
            blendState = null;
            Begin();
        }

        private BasicEffect geometryEffect;

        /// <summary>
        /// Draws indexed triangle geometry (real vertices, not a baked
        /// texture) interleaved with the sprite batch — the SetScissor/
        /// BeginAdditive idiom (End, change GPU state, draw, Begin), since
        /// SpriteBatch itself has no vertex-geometry primitive. Vertex
        /// positions are in the SAME logical pixel space every other Draw
        /// call on this class uses (GetActualXnaPosition()'s space):
        /// RenderScale is folded into the projection the same way Begin()'s
        /// transform folds it in for sprites, so geometry stays pixel-
        /// aligned with whatever sprite-drawn UI surrounds it. Blend/
        /// rasterizer (scissor) state carries over from the just-closed
        /// batch, matching what a sprite drawn at this exact point in the
        /// element tree would have seen.
        ///
        /// Caller must already be inside a Begin()/End() batch. Every call
        /// costs one batch flush (End+Begin) — cheap for a handful of
        /// calls/frame (this pairs with the existing BeginAdditive/
        /// SetScissor idiom's own cost), but callers drawing MANY small
        /// shapes (e.g. one call per timeline block) should batch them into
        /// as few DrawTriangles calls as the geometry allows rather than
        /// calling this once per shape.
        /// </summary>
        // short indices, not int: KNI's default Reach graphics profile
        // throws NotSupportedException on 32-bit index buffers. A waveform
        // block's column count never remotely approaches 65535 (16-bit's
        // ceiling), so this costs nothing.
        public void DrawTriangles(VertexPositionColor[] vertices, short[] indices, int primitiveCount)
        {
            if (primitiveCount <= 0)
            {
                return;
            }

            End();

            GraphicsDevice device = Resources.StaticResources.GraphicsDevice;

            if (geometryEffect == null)
            {
                geometryEffect = new BasicEffect(device) { VertexColorEnabled = true, World = Matrix.Identity };
            }

            Viewport viewport = device.Viewport;
            geometryEffect.View = RenderScale != 1f ? Matrix.CreateScale(RenderScale, RenderScale, 1f) : Matrix.Identity;
            geometryEffect.Projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, 1);

            device.BlendState = blendState ?? BlendState.AlphaBlend;
            device.RasterizerState = rasterizerState;
            device.DepthStencilState = DepthStencilState.None;

            foreach (EffectPass pass in geometryEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length, indices, 0, primitiveCount);
            }

            Begin();
        }

        private Effect sdfEffect;

        /// <summary>
        /// Draws an SDF-baked string (font-rendering plan Phase 1) — crisp at
        /// ANY pixelSize/RenderScale from a single per-family bake, unlike
        /// the bitmap path's per-(family,size,RenderScale) rebake. Same End/
        /// draw/Begin interleave idiom as <see cref="DrawTriangles"/> (a
        /// custom Effect needs its own batch), for the same reason: costs one
        /// batch flush per call, so callers drawing many short strings
        /// (a whole label-heavy screen) pay one flush per DrawSdfString
        /// call, not per glyph — batch flush cost, not glyph count, is what
        /// scales here.
        ///
        /// position is in the same logical pixel space as every other Draw
        /// call on this class (RenderScale folds in via the transform, same
        /// as DrawTriangles) — NOT pre-multiplied by RenderScale.
        ///
        /// Caller must already be inside a Begin()/End() batch (same
        /// precondition as DrawTriangles) — this leaves the batch open again
        /// on return, it does not need to be reopened by the caller.
        /// </summary>
        public void DrawSdfString(SdfFont sdfFont, string text, Vector2 position, float pixelSize, Color color)
        {
            if (string.IsNullOrEmpty(text) || sdfFont == null || pixelSize <= 0)
            {
                return;
            }

            End();

            if (sdfEffect == null)
            {
                sdfEffect = Resources.StaticResources.Content.Load<Effect>("SdfText");
            }

            GraphicsDevice device = Resources.StaticResources.GraphicsDevice;
            Viewport viewport = device.Viewport;

            // A custom Effect passed to SpriteBatch.Begin does NOT get its
            // transform auto-fed the way the built-in SpriteEffect does (only
            // updates SpriteBatch's own internal _spriteEffect) — must be set
            // explicitly every call. Same lesson as the DrawTriangles/
            // BasicEffect View+Projection split above, just as one combined
            // matrix (this effect's cbuffer, like stock SpriteEffect.fx,
            // only exposes a single MatrixTransform).
            Matrix scaleMatrix = RenderScale != 1f ? Matrix.CreateScale(RenderScale, RenderScale, 1f) : Matrix.Identity;
            Matrix projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, 1);
            sdfEffect.Parameters["MatrixTransform"].SetValue(scaleMatrix * projection);

            // AA band width, in normalized (0..1) distance-fraction units,
            // sized so the transition spans ~1 REAL screen pixel regardless
            // of how much the atlas is being magnified/minified for this
            // particular draw — see SdfText.fx's header for why this can't
            // just be a shader constant (no derivatives at the Reach/9.1
            // feature level, so there's no ddx/ddy to derive it on-GPU).
            float finalScale = (pixelSize / sdfFont.EmSize) * RenderScale;
            float smoothing = MathHelper.Clamp(
                0.5f * SdfFontBaker.OnEdgeValue / (255f * SdfFontBaker.Padding * finalScale),
                0.005f, 0.25f);
            sdfEffect.Parameters["Smoothing"].SetValue(smoothing);

            spriteBatch.Begin(SpriteSortMode.Deferred, blendState, samplerState, null, rasterizerState, sdfEffect);

            float glyphScale = pixelSize / sdfFont.EmSize;
            float cursorX = position.X;
            foreach (char c in text)
            {
                if (!sdfFont.TryGetGlyph(c, out SpriteFontPlus.SdfGlyphInfo g))
                {
                    continue;
                }

                if (g.Width > 0 && g.Height > 0)
                {
                    Rectangle dest = new Rectangle(
                        (int)Math.Round(cursorX + g.XOffset * glyphScale),
                        (int)Math.Round(position.Y + g.YOffset * glyphScale),
                        Math.Max(1, (int)Math.Round(g.Width * glyphScale)),
                        Math.Max(1, (int)Math.Round(g.Height * glyphScale)));
                    Rectangle src = new Rectangle(g.X, g.Y, g.Width, g.Height);
                    spriteBatch.Draw(sdfFont.Atlas, dest, src, color);
                }

                cursorX += g.XAdvance * glyphScale;
            }

            spriteBatch.End();
            Begin();
        }

        /// <summary>
        /// Renders indexed triangle geometry to a FRESH offscreen texture
        /// at EXACTLY (width, height) — the "bake once, blit every frame
        /// after" upgrade to <see cref="DrawTriangles"/>'s "pay the flush
        /// cost every single frame" one. After this call the shape is a
        /// normal Texture2D: draw it with the ordinary Draw(texture, rect,
        /// tint) sprite path (batches, tints, everything the existing
        /// baked-texture render mode already does) for as long as the
        /// caller's own cache says the size hasn't changed — panning,
        /// scrolling, and clipping never need a fresh bake, only a real
        /// (width, height) change does. Vertex positions must already be
        /// in the TARGET's own local space (0,0 top-left to width, height
        /// bottom-right), not screen position and not RenderScale-adjusted
        /// — this bakes a SIZE, not a placement; RenderScale (if any)
        /// still applies naturally when the resulting texture is later
        /// drawn through the normal sprite path.
        ///
        /// Mirrors <see cref="GetTargetClone"/>'s render-target save/
        /// restore shape (bypasses the tracked <c>currentTarget</c> field
        /// on purpose — this is a nested, transient target swap, not a
        /// frame-level one) rather than <see cref="DrawTriangles"/>'s
        /// "caller must already be mid-batch" assumption, since a bake is
        /// naturally a rarer, cache-driven call that shouldn't have to
        /// happen only from inside the middle of the main draw pass.
        /// </summary>
        public Texture2D BakeTrianglesToTexture(VertexPositionColor[] vertices, short[] indices, int primitiveCount, int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            bool wasInBatch = IsInBatch;
            if (wasInBatch)
            {
                End();
            }

            GraphicsDevice device = Resources.StaticResources.GraphicsDevice;
            var preTarget = currentTarget;
            var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
            device.SetRenderTarget(target);
            device.Clear(Color.Transparent);

            if (primitiveCount > 0)
            {
                if (geometryEffect == null)
                {
                    geometryEffect = new BasicEffect(device) { VertexColorEnabled = true, World = Matrix.Identity };
                }

                geometryEffect.View = Matrix.Identity;
                geometryEffect.Projection = Matrix.CreateOrthographicOffCenter(0, width, height, 0, 0, 1);

                device.BlendState = BlendState.AlphaBlend;
                device.RasterizerState = rasterizerState;
                device.DepthStencilState = DepthStencilState.None;

                foreach (EffectPass pass in geometryEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length, indices, 0, primitiveCount);
                }
            }

            device.SetRenderTarget(preTarget);

            if (wasInBatch)
            {
                Begin();
            }

            return target;
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
