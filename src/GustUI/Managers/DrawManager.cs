using GustUI.Extensions;
using GustUI.Rendering;
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
        private SdfFont debugFont = null;
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

        private GeometryBatch geometryBatch;
        private TextureAtlas geometryAtlas;
        private Effect geometryBatchEffect;

        /// <summary>
        /// The frame-scoped accumulator backing every DrawManager primitive
        /// draw (flat shapes AND SDF text, unconditionally — see the
        /// GustUI geometry-renderer migration plan; Phase 8 completed the
        /// migration off SpriteBatch entirely for primitives). Lazily
        /// created on first use.
        ///
        /// IMPORTANT — do not reintroduce a SpriteBatch fallback for flat
        /// shapes: SpriteBatch's SpriteSortMode.Deferred queues draws and
        /// only actually commits them at the next real End()/Begin() sync
        /// point, while GeometryBatch content (including all text) commits
        /// IMMEDIATELY via DrawIndexedPrimitives during the same tree walk.
        /// Mixing the two backends for ordinary shapes reintroduces a real,
        /// silent bug: an ordinary shape (e.g. a panel's own background
        /// fill) queued on SpriteBatch BEFORE some later-drawn geometry
        /// text stays uncommitted until the next sync point, then paints
        /// OVER the already-rasterized text the instant it finally
        /// flushes — no exception, no obviously-wrong per-draw state.
        /// Found 2026-08-12: EVERY piece of text inside EVERY modal
        /// rendered invisible — even forced to output solid opaque white
        /// by a diagnostic pixel shader — while non-modal text worked
        /// fine; a raw GetBackBufferData read confirmed the glyph pixels
        /// were correctly white immediately after their own
        /// DrawIndexedPrimitives call and only turned into the panel's
        /// background color after DrawManager.End()'s final SpriteBatch
        /// commit, isolating it to exactly this deferred-vs-immediate
        /// ordering gap. This is why the migration's staged
        /// DrawManager.UseGeometryBackend opt-in flag was removed in
        /// Phase 8 rather than kept as a toggle — false was never actually
        /// safe once SDF text (Phase 7) went geometry-only.
        /// </summary>
        public GeometryBatch GeometryBatch => geometryBatch ?? (geometryBatch = new GeometryBatch(Resources.StaticResources.GraphicsDevice));

        /// <summary>Shared atlas for baked alpha-mask shapes (rounded-rect corners, knob dial/ring, etc.) migrated onto the geometry backend — see TextureAtlas's own doc comment.</summary>
        public TextureAtlas GeometryAtlas => geometryAtlas ?? (geometryAtlas = new TextureAtlas(Resources.StaticResources.GraphicsDevice));

        private Effect GetGeometryBatchEffect()
        {
            if (geometryBatchEffect == null)
            {
                geometryBatchEffect = Resources.StaticResources.Content.Load<Effect>("GeometryBatch");
            }

            return geometryBatchEffect;
        }

        private Effect GetSdfBatchEffect()
        {
            if (sdfEffect == null)
            {
                sdfEffect = Resources.StaticResources.Content.Load<Effect>("SdfText");
            }

            return sdfEffect;
        }

        /// <summary>
        /// Uploads and draws whatever's accumulated in <see cref="GeometryBatch"/>
        /// since the last flush, resetting it for further appends — see
        /// GeometryBatch.Flush's own doc comment for why this is called
        /// from multiple places per frame (every DrawManager.End(), not
        /// just once at frame end). Cheap when empty (GeometryBatch.Flush's
        /// own early-return).
        /// </summary>
        private void FlushGeometryBatch()
        {
            if (geometryBatch == null || geometryBatch.IsEmpty)
            {
                return;
            }

            Effect flatEffect = GetGeometryBatchEffect();
            Effect textEffect = GetSdfBatchEffect();
            Viewport viewport = Resources.StaticResources.GraphicsDevice.Viewport;
            Matrix scale = RenderScale != 1f ? Matrix.CreateScale(RenderScale, RenderScale, 1f) : Matrix.Identity;
            Matrix projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, 1);
            Matrix matrixTransform = scale * projection;

            flatEffect.Parameters["MatrixTransform"].SetValue(matrixTransform);
            // Separate scalar (not just folded into MatrixTransform): the
            // pixel shader can't read SV_Position (Reach forbids it — see
            // GeometryBatch.fx), so it re-derives a physical-pixel-space
            // position from the raw vertex position via this same factor,
            // matching PushScissor's own ClipRect scaling exactly.
            flatEffect.Parameters["RenderScale"].SetValue(RenderScale);

            textEffect.Parameters["MatrixTransform"].SetValue(matrixTransform);
            // Smoothing/BorderWidth/BorderColor are NOT set here — they vary
            // per text segment (that's exactly why a font-size/border change
            // forces a new segment) and GeometryBatch.Flush sets them itself
            // right before drawing each such segment.

            geometryBatch.Flush(flatEffect, textEffect);
        }

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

            GeometryBatch.BeginFrame();

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
            float debugFontSize = Resources.StaticResources.Theme.UiFontSmall.Size;

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
                if (debugFont == null)
                {
                    debugFont = Resources.StaticResources.FontManager.LoadSdfFont(Resources.StaticResources.Theme.UiFontSmall.Family);
                }
                Vector2 ps = new Vector2(0, 60);
                DrawSdfString(debugFont, fps, ps + new Vector2(1, 1), debugFontSize, Color.Black);
                DrawSdfString(debugFont, fps, ps + new Vector2(3, 1), debugFontSize, Color.Black);
                DrawSdfString(debugFont, fps, ps + new Vector2(1, 3), debugFontSize, Color.Black);
                DrawSdfString(debugFont, fps, ps + new Vector2(3, 3), debugFontSize, Color.Black);
                DrawSdfString(debugFont, fps, ps + new Vector2(2, 2), debugFontSize, Color.White);


                SpriteBatchExtensions.DrawFilledRectangle(this, new Rectangle(0, 0, (int)Resources.StaticResources.RootWindow.GetSize().X, bottom), Color.Blue * 0.8f);

                string consoleText = "CMD:>";
                var ctHeight = (int)debugFont.MeasureString(consoleText, debugFontSize).Y;
                var ctBorder = 2;
                SpriteBatchExtensions.DrawFilledRectangle(this, new Rectangle(ctBorder, bottom - ctHeight - (ctBorder * 2), (int)Resources.StaticResources.RootWindow.GetSize().X - (ctBorder * 2), ctHeight + ctBorder), Color.Black * 0.8f);
                bottom = bottom - ctHeight - (ctBorder * 2);

                DrawSdfString(debugFont, consoleText, new Vector2(5, 0) + new Vector2(0, bottom), debugFontSize, Color.Black);
                DrawSdfString(debugFont, consoleText, new Vector2(5, 0) + new Vector2(2, bottom), debugFontSize, Color.Black);
                DrawSdfString(debugFont, consoleText, new Vector2(5, 0) + new Vector2(0, bottom + 2), debugFontSize, Color.Black);
                DrawSdfString(debugFont, consoleText, new Vector2(5, 0) + new Vector2(2, bottom + 2), debugFontSize, Color.Black);
                DrawSdfString(debugFont, consoleText, new Vector2(5, 0) + new Vector2(1, bottom + 1), debugFontSize, Color.Yellow);

            }



            if (debugBottom > 1)
            {

                for (int i = Log.log.Count; i > 0; i--)
                {
                    var height = ((int)debugFont.MeasureString(Log.log.ToArray()[i - 1].ToString(), debugFontSize).Y) + 4;
                    bottom = bottom - height;

                    DrawSdfString(debugFont, Log.log.ToArray()[i - 1].ToString(), new Vector2(5, 0) + new Vector2(0, bottom), debugFontSize, Color.Black * 0.5f);
                    DrawSdfString(debugFont, Log.log.ToArray()[i - 1].ToString(), new Vector2(5, 0) + new Vector2(2, bottom), debugFontSize, Color.Black * 0.5f);
                    DrawSdfString(debugFont, Log.log.ToArray()[i - 1].ToString(), new Vector2(5, 0) + new Vector2(0, bottom + 2), debugFontSize, Color.Black * 0.5f);
                    DrawSdfString(debugFont, Log.log.ToArray()[i - 1].ToString(), new Vector2(5, 0) + new Vector2(2, bottom + 2), debugFontSize, Color.Black * 0.5f);
                    DrawSdfString(debugFont, Log.log.ToArray()[i - 1].ToString(), new Vector2(5, 0) + new Vector2(1, bottom + 1), debugFontSize, Color.White);

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
            BeginSprite(mode);
        }

        public void End()
        {
            // Geometry flushes BEFORE the sprite batch commits, at every
            // GENERAL sync point (BeginAdditive/EndAdditive, DrawTriangles,
            // end of frame) — see FlushGeometryBatch/GeometryBatch.Flush's
            // own comments. NOT called from SetScissor (Phase 3:
            // PushScissor/PopScissor's clip is per-vertex data, not a
            // GPU-state sync point, so geometry can accumulate freely
            // across scissor changes) — see SetScissor's own comment.
            FlushGeometryBatch();
            EndSprite();
        }

        /// <summary>
        /// The actual SpriteBatch End()/Begin() pair, WITHOUT the geometry
        /// flush — used by SetScissor (Phase 3: scissor changes are pure
        /// GPU/SpriteBatch state now, geometry doesn't need to sync with
        /// them since clip travels per-vertex) so that pushing/popping a
        /// clip region no longer forces a geometry-batch flush the way it
        /// did through Phase 1-2 (when SetScissor called the general
        /// Begin()/End() pair). Public Begin()/End() still call these too,
        /// via the wrappers above — this split exists so ONE call site
        /// (SetScissor) can skip the geometry half while every other
        /// caller keeps it.
        /// </summary>
        private void BeginSprite(SpriteSortMode mode = SpriteSortMode.Deferred)
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

        private void EndSprite()
        {
            spriteBatch.End();
            IsInBatch = false;
        }


        /// <summary>
        /// Rotated-quad draw (KnobElement's pointer, DrawLine/DrawThickLine's
        /// rotated-rect idiom). <paramref name="vector2"/> (origin) is passed
        /// straight through to GeometryBatch.AppendRotatedQuad's own
        /// dest-local-pixel-space convention, UNCHANGED — matching exactly
        /// how DrawLine/DrawThickLine's own geometry branch already does it
        /// (SpriteBatchExtensions.cs), which is screenshot-verified correct.
        /// The `value` parameter has always been dead (never read) —
        /// untouched, not this change's concern.
        /// </summary>
        internal void Draw(Texture2D pixel, Rectangle rectangle, object value, Color color, float angle, Vector2 vector2, SpriteEffects none, int v)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            Rectangle src = new Rectangle(0, 0, pixel.Width, pixel.Height);
            GeometryBatch.AppendRotatedQuad(pixel, rectangle, src, color, angle, vector2, GetClipRectForGeometry(), null);
        }

        internal void Draw(Texture2D pixel, Rectangle rectangle, Color color)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            Rectangle src = new Rectangle(0, 0, pixel.Width, pixel.Height);
            GeometryBatch.AppendQuad(pixel, rectangle, src, color, GetClipRectForGeometry(), null);
        }

        internal void Draw(Texture2D texture, Rectangle rectangle, Rectangle? source, Color color)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            Rectangle src = source ?? new Rectangle(0, 0, texture.Width, texture.Height);
            GeometryBatch.AppendQuad(texture, rectangle, src, color, GetClipRectForGeometry(), null);
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
        /// The clip rect geometry-backend Append* calls should stamp onto
        /// their vertices right now — the top of scissorStack (already
        /// RenderScale-scaled into physical-pixel space by PushScissor) if
        /// any ClipChildren ancestor is active, else the full viewport (no
        /// clip). This runs REDUNDANTLY alongside SetScissor's real
        /// GraphicsDevice.ScissorRectangle update (which is still required
        /// for whatever content remains on SpriteBatch — text is
        /// ubiquitous inside these exact regions — and still forces a
        /// flush via the general End()/Begin() pair; see SetScissor's own
        /// comment for why decoupling them was tried and reverted, having
        /// measured no flush-count benefit at this stage of the migration)
        /// — that's intentional groundwork: geometry itself ignores
        /// GraphicsDevice.ScissorRectangle entirely (RasterizerState.CullNone
        /// in GeometryBatch.Flush leaves ScissorTestEnable off) and relies
        /// solely on this per-vertex data, so once SpriteBatch no longer
        /// needs the real scissor rect here (Phase 7+), SetScissor's
        /// GPU-state work can be dropped for the geometry-only case without
        /// touching this method at all.
        /// </summary>
        internal Vector4 GetClipRectForGeometry()
        {
            if (scissorStack.Count > 0)
            {
                Rectangle r = scissorStack.Peek();
                return new Vector4(r.Left, r.Top, r.Right, r.Bottom);
            }

            Viewport viewport = Resources.StaticResources.GraphicsDevice.Viewport;
            return new Vector4(0, 0, viewport.Width, viewport.Height);
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

        /// <summary>Border-free overload — see the full overload below.</summary>
        public void DrawSdfString(SdfFont sdfFont, string text, Vector2 position, float pixelSize, Color color)
        {
            DrawSdfString(sdfFont, text, position, pixelSize, color, 0, null);
        }

        /// <summary>
        /// Draws an SDF-baked string — crisp at ANY pixelSize/RenderScale
        /// from a single per-family bake, unlike the retired bitmap path's
        /// per-(family,size,RenderScale) rebake. Phase 7 of the geometry-
        /// renderer migration: appends glyph quads into GeometryBatch
        /// (GeometryBatch.AppendGlyphQuad) instead of driving its own
        /// SpriteBatch batch — so, unlike this method's pre-Phase-7 form,
        /// it does NOT cost a flush per call; many strings of the same
        /// (font size, RenderScale, border state) batch into one segment,
        /// flushed at the same sync points every other geometry content
        /// uses (see DrawManager.End()/FlushGeometryBatch).
        ///
        /// position is in the same logical pixel space as every other Draw
        /// call on this class (RenderScale folds in via GeometryBatch's own
        /// transform) — NOT pre-multiplied by RenderScale.
        ///
        /// <paramref name="borderSize"/> follows the same convention the
        /// retired bitmap DrawString's outline did (0 = none; N = an N-pixel
        /// stroke); <paramref name="borderColor"/> defaults to a dimmed
        /// version of <paramref name="color"/> (matching
        /// SpriteBatchExtensions.DrawString's BorderFade=0.1 default) when
        /// null. Converted into the SDF's normalized distance-field units
        /// via the SAME per-draw texel-to-screen-pixel scale factor
        /// Smoothing's own conversion uses (see SdfText.fx's own comment for
        /// why unbordered draws — BorderColor.a forced to 0 below — are
        /// mathematically UNAFFECTED by this being the same shader that now
        /// also supports outlines, not just visually close).
        /// </summary>
        public void DrawSdfString(SdfFont sdfFont, string text, Vector2 position, float pixelSize, Color color, int borderSize, Color? borderColor)
        {
            if (string.IsNullOrEmpty(text) || sdfFont == null || pixelSize <= 0)
            {
                return;
            }

            // AA band width, in normalized (0..1) distance-fraction units —
            // sized so the transition spans ~1 REAL screen pixel regardless
            // of how much the atlas is being magnified/minified for this
            // particular draw — see SdfText.fx's header for why this can't
            // just be a shader constant (no derivatives at the Reach/9.1
            // feature level, so there's no ddx/ddy to derive it on-GPU).
            // The literal "1 physical pixel" band that formula alone produces
            // reads visibly THINNER/softer than the retired bitmap path at
            // typical UI text sizes (found 2026-08-12, direct SDF-vs-bitmap
            // screenshot comparison at UiFontSmall=16 over EmSize=64's heavy
            // ~4x minification): a full-pixel-wide linear ramp swallows most
            // of a thin stroke's own width before any of it reaches full
            // opacity. The 0.35 factor is an empirically-tuned correction,
            // not a re-derivation — chosen by comparing crops against the
            // bitmap reference until stroke weight matched, then re-checked
            // at UiFontLarge=32 (near-native, minimal minification) to
            // confirm it doesn't introduce visible aliasing there either.
            float finalScale = (pixelSize / sdfFont.EmSize) * RenderScale;
            float pixelToNormalized = 0.5f * SdfFontBaker.OnEdgeValue / (255f * SdfFontBaker.Padding * finalScale);
            float smoothing = MathHelper.Clamp(pixelToNormalized * 0.35f, 0.005f, 0.25f);

            // Border width uses the SAME base pixel-to-normalized-distance
            // conversion Smoothing derives from, but WITHOUT that 0.35
            // correction — 0.35 was tuned specifically to match the retired
            // bitmap path's ANTIALIASING weight, not a literal stroke width,
            // and a border's whole point is to BE the literal pixel width
            // the caller asked for.
            float borderWidth = borderSize > 0 ? borderSize * pixelToNormalized : 0f;
            // BorderFade = 0.1, matching SpriteBatchExtensions.DrawString's
            // own default outline dimming — alpha forced to exactly 0 when
            // there's no border so the shader's degenerate-case identity
            // holds (see SdfText.fx's own comment).
            Color effectiveBorderColor = borderSize > 0 ? (borderColor ?? color * 0.1f) : Color.Transparent;

            float glyphScale = pixelSize / sdfFont.EmSize;
            float cursorX = position.X;
            Vector4 clipRect = GetClipRectForGeometry();
            foreach (char c in text)
            {
                if (!sdfFont.TryGetGlyph(c, out SpriteFontPlus.SdfGlyphInfo g))
                {
                    continue;
                }

                if (g.Width > 0 && g.Height > 0)
                {
                    // Float position, NOT an integer Rectangle (found
                    // 2026-08-12: (int)Math.Round() on each glyph's
                    // dest.X/dest.Y independently, before the RenderScale
                    // transform multiplies it out, produced up to half a
                    // pixel of jitter per glyph at 1x — up to 2 physical
                    // pixels at 400% DPI, each glyph's remainder rounding
                    // independently so different letters visibly drifted
                    // relative to each other. SDF doesn't need pixel-
                    // snapping to look crisp — that's the technique's whole
                    // point — so this keeps every glyph in continuous float
                    // space through to the GPU's own final rasterization,
                    // same as GeometryBatch's other Append* calls.
                    Vector2 destPos = new Vector2(cursorX + g.XOffset * glyphScale, position.Y + g.YOffset * glyphScale);
                    Vector2 destSize = new Vector2(g.Width * glyphScale, g.Height * glyphScale);
                    Rectangle src = new Rectangle(g.X, g.Y, g.Width, g.Height);
                    GeometryBatch.AppendGlyphQuad(sdfFont.Atlas, destPos, destSize, src, color, smoothing, borderWidth, effectiveBorderColor, clipRect);
                }

                cursorX += g.XAdvance * glyphScale;
            }
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
            // Deliberately the GENERAL End()/Begin() pair (flushes geometry
            // too), NOT the sprite-only BeginSprite/EndSprite split defined
            // above — measured via FrameProfiler on a busy scenario
            // (--scenario deepzoom) that decoupling geometry from scissor
            // flushes does NOT reduce total flush count at this stage of
            // the migration (368 decoupled vs 370 coupled, on ~296 baseline
            // sprite-only flushes) — worse than that, it ADDS flushes
            // relative to baseline either way. Root cause: SetScissor's own
            // real GraphicsDevice.ScissorRectangle update is still fully
            // required for whatever remains on SpriteBatch (chiefly text —
            // ubiquitous inside these exact ClipChildren regions, e.g.
            // every timeline run label), so decoupling geometry from it
            // doesn't remove any of the ORIGINAL scissor-flush cost — it
            // only splits what used to be one combined sprite flush
            // (background rect + its own label, both sprite-batched) into
            // two (one geometry DrawIndexedPrimitives + one sprite flush),
            // since they're now on different backends. The real elimination
            // of scissor-driven flush cost is deferred until enough of what
            // still draws inside these regions (text — Phase 7, baked
            // shapes — Phase 4/5) is ALSO on the geometry backend that
            // SpriteBatch stops needing real GPU scissor here at all —
            // revisit decoupling then, when it can be measured to actually
            // help instead of just adding complexity for no gain.
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
