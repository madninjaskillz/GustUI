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
            // Now genuinely needed (2026-08-13, SdfText.fx clip-rect fix):
            // ScreenPos re-derives physical-pixel position the same way
            // flatEffect's RenderScale above does, for the SAME reason (the
            // pixel shader can't read SV_Position under Reach) — SdfText.fx
            // previously had no RenderScale parameter at all since its pixel
            // shader never used a screen-space position for anything.
            textEffect.Parameters["RenderScale"].SetValue(RenderScale);
            // Smoothing/BorderWidth/BorderColor are NOT set here — they vary
            // per text segment (that's exactly why a font-size/border change
            // forces a new segment) and GeometryBatch.Flush sets them itself
            // right before drawing each such segment.

            geometryBatch.Flush(flatEffect, textEffect);
        }

        public DrawManager()
        {
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
            DrawTelemetryOverlay();
            End();
        }

        /// <summary>
        /// The Telemetry HUD (design-guide.md-adjacent debug tooling, not a
        /// normal app view): a 10-second rolling line graph of total
        /// per-frame instrumented CPU time, top-right, plus a live table of
        /// the top tags by total self-time — <see cref="Managers.Telemetry"/>'s
        /// own doc comment covers the tag/exclusive-time model this reads
        /// from. Hand-drawn directly here (not a GustUI Element) for the
        /// same reason the FPS/console debug block above is: it needs to
        /// render on top of literally everything, every frame, regardless
        /// of which view/modal is currently in the element tree.
        /// </summary>
        private void DrawTelemetryOverlay()
        {
            if (!Telemetry.OverlayVisible)
            {
                return;
            }

            if (debugFont == null)
            {
                debugFont = Resources.StaticResources.FontManager.LoadSdfFont(Resources.StaticResources.Theme.UiFontSmall.Family);
            }

            const int width = 340;
            const int graphHeight = 90;
            const int rowHeight = 16;
            const int topN = 6;
            const int padding = 8;
            const float fontSize = 13f;

            List<Telemetry.TagStats> stats = Telemetry.GetAllStats();
            stats.Sort((a, b) => b.TotalMs.CompareTo(a.TotalMs));
            int rows = Math.Min(topN, stats.Count);
            int height = padding * 3 + 16 + graphHeight + Math.Max(rows, 1) * rowHeight;

            float windowWidth = Resources.StaticResources.RootWindow.GetSize().X;
            var origin = new Vector2(windowWidth - width - 8, 48);
            var panelRect = new Rectangle((int)origin.X, (int)origin.Y, width, height);

            SpriteBatchExtensions.DrawFilledRectangle(this, panelRect, new Color(10, 10, 14) * 0.88f);
            SpriteBatchExtensions.DrawRectangle(this, panelRect, new Color(70, 70, 84), 1);
            DrawSdfString(debugFont, "CPU telemetry — last 10s", origin + new Vector2(padding, padding - 2), fontSize, Color.White);

            var graphRect = new Rectangle((int)origin.X + padding, (int)origin.Y + padding + 16, width - padding * 2, graphHeight);
            SpriteBatchExtensions.DrawFilledRectangle(this, graphRect, new Color(4, 4, 6) * 0.7f);

            var samples = new List<(double TimeSeconds, float TotalMs)>(Telemetry.RecentSamples(10.0));
            if (samples.Count > 1)
            {
                float maxMs = 4f; // floor so a near-idle graph isn't all noise
                foreach ((double _, float ms) in samples)
                {
                    if (ms > maxMs)
                    {
                        maxMs = ms;
                    }
                }

                double newestT = samples[0].TimeSeconds;
                Vector2 previous = default;
                bool havePrevious = false;

                // Samples come back newest-first; walk oldest-first so the
                // line draws left (10s ago) to right (now).
                for (int i = samples.Count - 1; i >= 0; i--)
                {
                    (double t, float ms) = samples[i];
                    float age = (float)(newestT - t);
                    float x = graphRect.Right - MathHelper.Clamp(age / 10f, 0f, 1f) * graphRect.Width;
                    float y = graphRect.Bottom - MathHelper.Clamp(ms / maxMs, 0f, 1f) * graphRect.Height;
                    var point = new Vector2(x, y);
                    if (havePrevious)
                    {
                        SpriteBatchExtensions.DrawLine(this, previous, point, Resources.StaticResources.Theme.AccentSelection);
                    }

                    previous = point;
                    havePrevious = true;
                }

                DrawSdfString(debugFont, $"{maxMs:0}ms", new Vector2(graphRect.Right - 38, graphRect.Top + 2), 11f, Color.White * 0.6f);
            }

            float rowY = origin.Y + padding + 16 + graphHeight + 6;
            if (rows == 0)
            {
                DrawSdfString(debugFont, "(no tagged sections yet)", new Vector2(origin.X + padding, rowY), fontSize, Color.White * 0.6f);
            }
            else
            {
                for (int i = 0; i < rows; i++)
                {
                    Telemetry.TagStats s = stats[i];
                    string line = $"{s.Tag}  {s.TotalMs:0.0}ms  x{s.Calls}  ~{s.AvgMs:0.000}ms";
                    DrawSdfString(debugFont, line, new Vector2(origin.X + padding, rowY + i * rowHeight), fontSize, Color.White * 0.9f);
                }
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
        /// Sets the GPU state a SpriteBatch.Begin() call used to set as a
        /// side effect — directly on GraphicsDevice now, not through
        /// SpriteBatch (SpriteBatch removal slice 1, 2026-08-20). Safe
        /// because nothing has called spriteBatch.Draw()/DrawString() since
        /// Phase 8 (see GeometryBatch's own doc comment) — every actual
        /// draw goes through GeometryBatch (which sets its own BlendState/
        /// RasterizerState/SamplerState per segment in Flush(), ignoring
        /// these fields entirely) or the legacy DrawTriangles/
        /// DrawFullScreenEffect raw-draw paths (which likewise set their
        /// own state explicitly, right before drawing, not relying on this
        /// having run first). This method's ONLY remaining real effect is
        /// keeping device.RasterizerState (ScissorTestEnable=true) and
        /// BlendState/SamplerState current between those explicit sets —
        /// still called from the exact same sync points (SetScissor,
        /// BeginAdditive/EndAdditive, End()/Begin()) so flush-count
        /// telemetry stays comparable.
        /// </summary>
        private void BeginSprite(SpriteSortMode mode = SpriteSortMode.Deferred)
        {
            IsInBatch = true;
            FrameProfiler.CountFlush();
            GraphicsDevice device = Resources.StaticResources.GraphicsDevice;
            device.BlendState = blendState ?? BlendState.AlphaBlend;
            device.SamplerStates[0] = samplerState ?? SamplerState.LinearClamp;
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = rasterizerState;
        }

        private void EndSprite()
        {
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

        // A single "big triangle" covering the whole clip space (-1,-1) to
        // (3,-1) to (-1,3) — the standard fullscreen-pass trick: it covers
        // every pixel of whatever viewport is bound (same as a quad would)
        // with one fewer vertex and no diagonal seam, at the cost of some
        // wasted rasterization outside the visible area that the GPU clips
        // for free. UVs run 0..1 across the visible region the same way a
        // quad's would (the extra corner UVs run to 2/-1, simply never
        // sampled since they land outside the clipped/visible triangle).
        private static readonly VertexPositionTexture[] fullScreenTriangle =
        {
            new VertexPositionTexture(new Vector3(-1, 1, 0), new Vector2(0, 0)),
            new VertexPositionTexture(new Vector3(3, 1, 0), new Vector2(2, 0)),
            new VertexPositionTexture(new Vector3(-1, -3, 0), new Vector2(0, 2)),
        };

        /// <summary>
        /// Draws <paramref name="effect"/>'s current technique over one
        /// fullscreen triangle — the primitive a custom multi-uniform shader
        /// effect (arbitrary iTime/resolution/texture-array parameters, unlike
        /// GeometryBatch's fixed flat/text effects) needs to cover a target
        /// with. Same End()/raw-draw/Begin() shape as <see cref="DrawTriangles"/>:
        /// caller must already be inside a Begin()/End() batch, and sets the
        /// effect's own scalar/vector parameters BEFORE calling this — this
        /// method only manages the render target, one optional input
        /// texture, and issues the draw; it knows nothing about what a
        /// specific effect's parameters mean.
        ///
        /// <paramref name="inputTexture"/> (optional — many fullscreen
        /// effects have none) is bound to texture slot 0 AFTER
        /// <c>pass.Apply()</c>, not before — the same GOTCHA
        /// <see cref="Rendering.GeometryBatch.Flush"/> already documents on
        /// this KNI DesktopGL target: Apply() resets texture/sampler
        /// bindings for an effect with its own declared <c>sampler</c>
        /// object, so binding earlier silently samples fully transparent.
        ///
        /// <paramref name="target"/> is set as the render target for the
        /// duration of this call and the PREVIOUS target is restored
        /// afterward — null draws to whatever was already bound (typically
        /// the backbuffer during normal DrawLoop traversal), a real
        /// RenderTarget2D redirects there and back (e.g. an intermediate
        /// pass another effect will sample from as a texture next). No
        /// explicit Clear: the triangle covers every pixel of the bound
        /// viewport, so the pixel shader's own output already replaces the
        /// target's entire prior contents.
        /// </summary>
        public void DrawFullScreenEffect(Effect effect, RenderTarget2D target, Texture2D inputTexture = null, Texture2D inputTexture2 = null)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            End();

            GraphicsDevice device = Resources.StaticResources.GraphicsDevice;
            RenderTarget2D previousTarget = currentTarget;
            SetRenderTarget(target);
            device.BlendState = BlendState.Opaque;
            device.RasterizerState = RasterizerState.CullNone;
            device.DepthStencilState = DepthStencilState.None;

            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                if (inputTexture != null)
                {
                    device.Textures[0] = inputTexture;
                    device.SamplerStates[0] = SamplerState.LinearClamp;
                }
                if (inputTexture2 != null)
                {
                    device.Textures[1] = inputTexture2;
                    device.SamplerStates[1] = SamplerState.LinearClamp;
                }

                device.DrawUserPrimitives(PrimitiveType.TriangleList, fullScreenTriangle, 0, 1);
            }

            SetRenderTarget(previousTarget);
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
            // sized so the transition spans a fixed PHYSICAL pixel count
            // regardless of how much the atlas is being magnified/minified
            // for this particular draw — see SdfText.fx's header for why
            // this can't just be a shader constant (no derivatives at the
            // Reach/9.1 feature level, so there's no ddx/ddy to derive it
            // on-GPU). Working through the unit conversion (normalized
            // distance-fraction -> bake texels via OnEdgeValue/Padding ->
            // physical pixels via finalScale) shows every scale-dependent
            // term cancels exactly: the full ramp's PHYSICAL pixel width
            // reduces to precisely AaBandPhysicalPixels below, independent
            // of pixelSize/EmSize/RenderScale. That constant was 1.0 when
            // this formula was first written ("spans ~1 REAL screen pixel"),
            // then cut to 0.35 (2026-08-12) after a side-by-side against the
            // since-retired bitmap path found a 1px band left thin strokes
            // never reaching full opacity ("washed out"). 0.35 physical
            // pixels is sub-pixel, though — confirmed by direct pixel
            // sampling of a rendered title in ezmuze-studio (0/255 ->
            // 255/255 with ZERO intermediate value, at UiFontLarge=32 on a
            // real 150%-DPI display): the ramp is narrower than a single
            // pixel's sampling footprint, so it's essentially always missed
            // — hard-edged, aliased text, not "soft but sharpened" text.
            // Currently back at the original 1.0 — the 2026-08-12 "washed
            // out" call was re-examined and preferred full-pixel softness
            // over that risk; revisit toward 0.6-0.8 if thin strokes read
            // grey/faint at small sizes instead.
            const float AaBandPhysicalPixels = 1.0f;
            float finalScale = (pixelSize / sdfFont.EmSize) * RenderScale;
            float pixelToNormalized = 0.5f * SdfFontBaker.OnEdgeValue / (255f * SdfFontBaker.Padding * finalScale);
            float smoothing = MathHelper.Clamp(pixelToNormalized * AaBandPhysicalPixels, 0.005f, 0.25f);

            // Border width uses the SAME base pixel-to-normalized-distance
            // conversion Smoothing derives from, but WITHOUT the
            // AaBandPhysicalPixels correction — that constant tunes
            // ANTIALIASING weight, not a literal stroke width, and a
            // border's whole point is to BE the literal pixel width the
            // caller asked for.
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
        /// Appends previously-triangulated, LOCAL-space geometry (vertex
        /// positions relative to origin (0,0) — see
        /// <see cref="WaveformData.GetGeometryVertices"/>, the caller this
        /// exists for) into the shared <see cref="GeometryBatch"/> at
        /// <paramref name="offset"/>, tinted by <paramref name="tint"/>.
        /// Replaces the old bake-to-RenderTarget2D path (BakeTrianglesToTexture,
        /// removed 2026-08-19): the expensive part was never the texture,
        /// it was re-running the triangulation math every frame — caching
        /// the vertex/index ARRAYS gets the same "pay once, reuse many
        /// frames" win without ever touching SetRenderTarget, so none of
        /// the render-target-timing hazards that method's own doc comment
        /// (and the QueuePendingBake machinery built around it) had to
        /// guard against exist here at all. Costs no flush either — this
        /// is exactly as batchable as any other GeometryBatch Append* call,
        /// sharing a segment with surrounding flat-color content whenever
        /// blend state matches. The translate/tint/UV-stamp work happens
        /// directly inside GeometryBatch.AppendCachedTriangles as it copies
        /// into the batch's OWN vertex array — no intermediate array here,
        /// this would otherwise be a fresh allocation every visible
        /// waveform block, every frame.
        /// </summary>
        public void DrawCachedTriangles(GeometryVertex[] localVerts, short[] indices, int primitiveCount, Vector2 offset, Color tint)
        {
            AtlasRegion white = GeometryAtlas.WhiteRegion;
            // Same half-texel-inset collapse-to-center trick GeometryBatch's
            // own UVRect uses for the reserved 1x1 white region — always
            // the exact texel center, so any triangle samples pure opaque
            // white regardless of size.
            Vector2 uv = new Vector2(
                (white.Pixels.X + 0.5f) / white.Texture.Width,
                (white.Pixels.Y + 0.5f) / white.Texture.Height);

            GeometryBatch.AppendCachedTriangles(white.Texture, localVerts, indices, primitiveCount, offset, tint, uv, GetClipRectForGeometry(), null);
        }

        public void SetScissor(Rectangle? rect)
        {
            // Deliberately the GENERAL End()/Begin() pair (flushes geometry
            // too), NOT the sprite-only BeginSprite/EndSprite split defined
            // above.
            //
            // 2026-08-20: reattempted the sprite-only split after fixing
            // GeometryBatch.CloseSegment's absolute-vBase/baseVertex-always-0
            // bug (the corruption the 2026-08-19 attempt below hit) — this
            // time it didn't corrupt, but crashed hard: a native access
            // violation (0xc0000005, unknown faulting module — not a managed
            // exception, nothing in Console) after a frame with a 1.16s
            // spike and heavy GC during a fast horizontal-scrollbar sweep
            // over a maximized, GeometryBaked-mode timeline. Letting
            // geometry accumulate across scissor changes means the
            // vertex/index arrays can grow far larger before a flush than
            // they ever did coupled — consistent with something in the
            // Array.Resize / DynamicVertexBuffer-upload path (GeometryBatch.
            // EnsureCapacity/Flush) not tolerating a much bigger single
            // upload, though that's not yet root-caused. Reverted again;
            // don't retry without root-causing THIS failure first (get a
            // native crash dump / repro the buffer-growth path in
            // isolation) — it's a worse failure mode than the visual
            // corruption that blocked the first attempt.
            //
            // 2026-08-19: dropping the End()/Begin() pair here corrupted the
            // Sequencer view's rendering — root-caused 2026-08-20 as
            // GeometryBatch.CloseSegment's indexing bug (see its own doc).
            // Also measured via FrameProfiler on a busy scenario
            // (--scenario deepzoom) that decoupling did NOT reduce total
            // flush count at that stage of the migration (368 decoupled vs
            // 370 coupled, ~296 baseline sprite-only flushes) — text was
            // still on SpriteBatch then and needed a real GPU scissor rect
            // here regardless, so decoupling geometry from it didn't remove
            // any of the original scissor-flush cost. Text has since moved
            // onto the geometry backend (SDF glyphs, Phase 7), so that
            // particular blocker no longer applies — but the crash above
            // means this still isn't safe to flip.
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
