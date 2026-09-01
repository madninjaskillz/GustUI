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
        private BlendState blendState = null;

        /// <summary>Offscreen renders queued for the top of the next frame —
        /// see <see cref="QueuePrePass"/>.</summary>
        private readonly List<Action> prePass = new List<Action>();

        /// <summary>
        /// Uniform scale applied to every draw (1 = off, the default) —
        /// folded into GeometryBatch's own MatrixTransform/RenderScale
        /// effect parameters (see <see cref="FlushGeometryBatch"/>) and
        /// every geometry append, not a SpriteBatch transform
        /// (removed 2026-08-20 — nothing reads one anymore). Pairs with
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

        /// <summary>
        /// Which compiled effect the geometry backend loads, and which the SDF
        /// text backend loads.
        ///
        /// Settable because the choice is per-HEAD and GustUI is a library
        /// shared by all of them, so it cannot be a #if here. ezmuze studio's
        /// GL/web heads point these at hand-written GLSL twins
        /// (GeometryBatchGL/SdfTextGL) to keep MojoShader out of the pipeline;
        /// the DirectX head leaves them alone and loads the HLSL, which it
        /// needs anyway and which an Xbox build would need too.
        ///
        /// Set BEFORE the first draw — these are read once and the effect is
        /// then cached for the app's lifetime.
        /// </summary>
        public static string GeometryEffectAsset { get; set; } = "GeometryBatch";

        public static string SdfEffectAsset { get; set; } = "SdfText";

        private Effect GetGeometryBatchEffect()
        {
            if (geometryBatchEffect == null)
            {
                geometryBatchEffect = Resources.StaticResources.Content.Load<Effect>(GeometryEffectAsset);
            }

            return geometryBatchEffect;
        }

        private Effect GetSdfBatchEffect()
        {
            if (sdfEffect == null)
            {
                sdfEffect = Resources.StaticResources.Content.Load<Effect>(SdfEffectAsset);
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

        float debugBottom = 0;

        public void DrawLoop(GameTime gameTime)
        {
            var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            _frameCounter.Update(deltaTime);

            GeometryBatch.BeginFrame();

            SetRenderTarget(null);
            Begin();

            // Before the tree, so the batch is empty and the render-target
            // swaps inside these cost no flush. Ahead of Clear as well, so
            // whatever the swaps leave on the backbuffer is wiped rather than
            // relied on to survive.
            RunPrePass();

            Clear(Color.Transparent);
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


                ShapeDrawExtensions.DrawFilledRectangle(this, new Rectangle(0, 0, (int)Resources.StaticResources.RootWindow.GetSize().X, bottom), Color.Blue * 0.8f);

                string consoleText = "CMD:>";
                var ctHeight = (int)debugFont.MeasureString(consoleText, debugFontSize).Y;
                var ctBorder = 2;
                ShapeDrawExtensions.DrawFilledRectangle(this, new Rectangle(ctBorder, bottom - ctHeight - (ctBorder * 2), (int)Resources.StaticResources.RootWindow.GetSize().X - (ctBorder * 2), ctHeight + ctBorder), Color.Black * 0.8f);
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

            ShapeDrawExtensions.DrawFilledRectangle(this, panelRect, new Color(10, 10, 14) * 0.88f);
            ShapeDrawExtensions.DrawRectangle(this, panelRect, new Color(70, 70, 84), 1);
            DrawSdfString(debugFont, "CPU telemetry — last 10s", origin + new Vector2(padding, padding - 2), fontSize, Color.White);

            var graphRect = new Rectangle((int)origin.X + padding, (int)origin.Y + padding + 16, width - padding * 2, graphHeight);
            ShapeDrawExtensions.DrawFilledRectangle(this, graphRect, new Color(4, 4, 6) * 0.7f);

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
                        ShapeDrawExtensions.DrawLine(this, previous, point, Resources.StaticResources.Theme.AccentSelection);
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



        public void Begin()
        {
            BeginSprite();
        }

        /// <summary>
        /// Asks for <paramref name="render"/> to run at the top of the NEXT
        /// frame, before anything has been drawn into the batch.
        ///
        /// For elements that render into their own RenderTarget2D and then
        /// draw that texture like any other image (GustUI has no such element
        /// itself; ezmuze studio's now-playing visualiser is the case this
        /// exists for). Done inline from Draw(), the render-target swap forces
        /// the batch to commit whatever the tree has drawn so far, so a single
        /// small offscreen effect costs a full flush in the middle of the
        /// frame. Run before the tree walk there is nothing to commit.
        ///
        /// Queued per frame from the element's own Draw() rather than
        /// registered once, which is what makes visibility work: an element
        /// that is not drawn does not ask, so nothing renders a texture for a
        /// panel that is off screen or gone. The cost is that the texture is
        /// one frame old -- invisible on anything animating, and the reason
        /// this is a queue rather than a callback.
        /// </summary>
        public void QueuePrePass(Action render)
        {
            if (render != null)
            {
                prePass.Add(render);
            }
        }

        private void RunPrePass()
        {
            if (prePass.Count == 0)
            {
                return;
            }

            for (int i = 0; i < prePass.Count; i++)
            {
                try
                {
                    prePass[i]();
                }
                catch (Exception ex)
                {
                    // One bad offscreen effect must not take the frame with
                    // it -- the whole UI is drawn after this.
                    Console.WriteLine("[draw] a pre-pass render failed: " + ex.Message);
                }
            }

            prePass.Clear();
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
        private void BeginSprite()
        {
            // Just a lifetime flag now (2026-08-30).
            //
            // It used to call FrameProfiler.CountFlush() and set four pieces
            // of GraphicsDevice state, both of which stopped meaning anything
            // when SpriteBatch went away. The count made "flushes" the sum of
            // batch-begins AND geometry flushes, which reads as a draw-call
            // measure and is not one. The state is set again, per segment, by
            // GeometryBatch.Flush (blend from the segment, CullNone,
            // DepthStencilState.None) and explicitly by the raw
            // DrawFullScreenEffect path; nothing draws outside those two.
            //
            // ScissorTestEnable went with it. It was true here and false in
            // every actual draw -- Flush uses RasterizerState.CullNone -- so
            // scissor testing has been off for a while. Clipping is the
            // per-vertex ClipRect, which is why SetScissor no longer writes
            // GraphicsDevice.ScissorRectangle either.
            IsInBatch = true;
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
        /// (ShapeDrawExtensions.cs), which is screenshot-verified correct.
        /// The `value` parameter has always been dead (never read) —
        /// untouched, not this change's concern.
        /// </summary>
        internal void Draw(Texture2D pixel, Rectangle rectangle, object value, Color color, float angle, Vector2 vector2, SpriteEffects none, int v)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            Rectangle src = new Rectangle(0, 0, pixel.Width, pixel.Height);
            GeometryBatch.AppendRotatedQuad(pixel, rectangle, src, color, angle, vector2, GetClipRectForGeometry(), CurrentBlend);
        }

        internal void Draw(Texture2D pixel, Rectangle rectangle, Color color)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            Rectangle src = new Rectangle(0, 0, pixel.Width, pixel.Height);
            GeometryBatch.AppendQuad(pixel, rectangle, src, color, GetClipRectForGeometry(), CurrentBlend);
        }

        internal void Draw(Texture2D texture, Rectangle rectangle, Rectangle? source, Color color)
        {
            Ensure.IsTrue(IsInBatch, "IsInBatch");
            Rectangle src = source ?? new Rectangle(0, 0, texture.Width, texture.Height);
            GeometryBatch.AppendQuad(texture, rectangle, src, color, GetClipRectForGeometry(), CurrentBlend);
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
        /// <see cref="GetClipRectForGeometry()"/> further narrowed by
        /// <paramref name="explicitClip"/> (logical pixel space — the same
        /// convention <see cref="PushScissor"/> takes, scaled by
        /// RenderScale the same way here), when provided — see
        /// <see cref="Elements.TextElement.ClipRectOverride"/>'s doc for
        /// why this exists as an alternative to a real ClipChildren
        /// container. Null falls straight through to the ambient clip,
        /// unchanged.
        /// </summary>
        internal Vector4 GetClipRectForGeometry(Rectangle? explicitClip)
        {
            Vector4 ambient = GetClipRectForGeometry();
            if (!explicitClip.HasValue)
            {
                return ambient;
            }

            Rectangle rect = explicitClip.Value;
            Rectangle scaled = RenderScale != 1f
                ? new Rectangle(
                    (int)(rect.X * RenderScale), (int)(rect.Y * RenderScale),
                    (int)(rect.Width * RenderScale), (int)(rect.Height * RenderScale))
                : rect;

            return new Vector4(
                Math.Max(ambient.X, scaled.Left),
                Math.Max(ambient.Y, scaled.Top),
                Math.Min(ambient.Z, scaled.Right),
                Math.Min(ambient.W, scaled.Bottom));
        }

        /// <summary>
        /// The blend state <see cref="BeginAdditive"/>/<see cref="EndAdditive"/>
        /// last set (null = default alpha blend) — every ShapeDrawExtensions/
        /// DrawManager call into GeometryBatch.Append* must pass this instead
        /// of a hardcoded null, or BeginAdditive silently does nothing for
        /// that shape (found 2026-08-20: GlowCurveElement's glow strokes had
        /// been alpha-blending instead of brightening since Phase 8 moved
        /// shape drawing onto GeometryBatch, which resolves each segment's
        /// blend from what's passed in per-call, not from this field —
        /// unlike the old SpriteBatch.Begin() path, which read this field
        /// directly).
        /// </summary>
        internal BlendState CurrentBlend => blendState;

        /// <summary>
        /// Switches to additive blending for whatever's drawn until
        /// <see cref="EndAdditive"/> — same End()/Begin()-to-change-GPU-
        /// state-mid-frame shape as <see cref="SetScissor"/> (blend state,
        /// like blend mode or the scissor rect, can only change between
        /// batches, not within one). For a true glow/bloom stroke: draw the
        /// same line 2-3× with increasing thickness and decreasing alpha
        /// inside a BeginAdditive()/<see cref="EndAdditive"/> pair so
        /// overlapping passes brighten instead of just alpha-composing over
        /// each other. Only takes effect for shapes drawn through
        /// GeometryBatch if the caller threads <see cref="CurrentBlend"/>
        /// through to the Append* call (see its own doc) — this method only
        /// changes what that field HOLDS, same as it always did.
        /// Caller must already be inside a Begin()/End() batch (mirrors
        /// SetScissor's assumption) and must pair this with EndAdditive
        /// before any non-additive drawing resumes.
        /// </summary>
        public void BeginAdditive()
        {
            // No End()/Begin() (2026-08-30). Blend state is a SEGMENT KEY in
            // GeometryBatch -- every Append* passes CurrentBlend, and
            // BeginSegmentIfNeeded opens a new segment when it changes -- so
            // setting the field is the entire job. Additive geometry lands in
            // its own segment and draws in append order, which is the order it
            // was asked for.
            //
            // The flush was the SpriteBatch-era idiom for changing GPU state,
            // kept after the backend could express blend per segment. It cost
            // two flushes per glow (one here, one in EndAdditive) for nothing.
            blendState = BlendState.Additive;
        }

        /// <summary>Restores normal alpha blending after <see cref="BeginAdditive"/>.</summary>
        public void EndAdditive()
        {
            blendState = null;
        }


        /// <summary>
        /// Draws indexed triangle geometry (real vertices, not a baked
        /// texture). Vertex positions are in the SAME logical pixel space
        /// every other Draw call on this class uses
        /// (GetActualXnaPosition()'s space): RenderScale is folded into
        /// GeometryBatch's MatrixTransform exactly as it is for every other
        /// append, so geometry stays pixel-aligned with the UI around it.
        ///
        /// COSTS NO FLUSH (2026-08-30). This used to be a raw
        /// DrawUserIndexedPrimitives through its own BasicEffect, wrapped in
        /// the End/change-GPU-state/draw/Begin idiom SetScissor and
        /// BeginAdditive still use — one full batch flush per call, and its
        /// own doc told callers to merge shapes because of it. That was
        /// written when SpriteBatch had no vertex-geometry primitive;
        /// GeometryBatch is nothing BUT vertex geometry, and
        /// AppendTriangles' own doc already named this method as the caller
        /// it was waiting for.
        ///
        /// It matters more than "one flush": the callers are
        /// WaveformElement (once per visible waveform, every frame, since
        /// waveforms went geometry-only) and PianoRollElement's bend curves
        /// — both per-element, so flush count scaled with how much of the
        /// song was on screen.
        ///
        /// Two behaviour changes fall out, both corrections. Clipping is now
        /// the per-vertex ClipRect the rest of the batch uses rather than
        /// the device scissor rect, and Element.Opacity now applies (the raw
        /// path ignored it, so geometry inside a fading element stayed at
        /// full strength while everything around it faded).
        ///
        /// Caller must already be inside a Begin()/End() batch.
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

            AtlasRegion white = GeometryAtlas.WhiteRegion;

            // The same half-texel collapse-to-centre DrawCachedTriangles
            // uses: always the exact texel centre, so a triangle of any size
            // samples pure opaque white.
            Vector2 uv = new Vector2(
                (white.Pixels.X + 0.5f) / white.Texture.Width,
                (white.Pixels.Y + 0.5f) / white.Texture.Height);

            GeometryBatch.AppendTriangles(
                white.Texture, vertices, indices, primitiveCount, uv, GetClipRectForGeometry(), CurrentBlend);
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

            // finally, not straight-line code: the End()/Begin() and the
            // render-target swap above are a matched pair, and a driver-level
            // throw out of Apply()/DrawUserPrimitives (a shader the device
            // won't accept, a lost device) would otherwise leave the batch
            // ended and an intermediate RenderTarget2D still bound — so every
            // subsequent draw call this frame AND the next trips
            // Ensure.IsTrue(IsInBatch) or paints into the wrong target. A
            // caller that catches the exception and carries on (e.g. ezmuze
            // studio's vis screen, which drops a failing effect and keeps
            // going) then has a usable device instead of a corrupted one.
            try
            {
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
            }
            finally
            {
                SetRenderTarget(previousTarget);
                Begin();
            }
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
        /// ShapeDrawExtensions.DrawString's BorderFade=0.1 default) when
        /// null. Converted into the SDF's normalized distance-field units
        /// via the SAME per-draw texel-to-screen-pixel scale factor
        /// Smoothing's own conversion uses (see SdfText.fx's own comment for
        /// why unbordered draws — BorderColor.a forced to 0 below — are
        /// mathematically UNAFFECTED by this being the same shader that now
        /// also supports outlines, not just visually close).
        /// </summary>
        public void DrawSdfString(SdfFont sdfFont, string text, Vector2 position, float pixelSize, Color color, int borderSize, Color? borderColor)
        {
            DrawSdfString(sdfFont, text, position, pixelSize, color, borderSize, borderColor, null);
        }

        /// <summary>
        /// Same as the overload above, plus an optional
        /// <paramref name="clipRectOverride"/> (logical pixel space, same
        /// convention as <see cref="Elements.TextElement.ClipRectOverride"/>
        /// — see that property's doc for why this exists): intersected with
        /// whatever ambient ClipChildren clip is already active, entirely
        /// in per-vertex data — no GPU scissor change, no flush, unlike
        /// wrapping the caller in its own ClipChildren container.
        /// </summary>
        public void DrawSdfString(SdfFont sdfFont, string text, Vector2 position, float pixelSize, Color color, int borderSize, Color? borderColor, Rectangle? clipRectOverride)
            => DrawSdfString(sdfFont, text, position, pixelSize, color, borderSize, borderColor, clipRectOverride, 0f);

        /// <summary>
        /// Same again, ROTATED about <paramref name="position"/>.
        ///
        /// The line is laid out along +X exactly as it always is and every
        /// glyph of it is then rotated about the string's own origin, so
        /// spacing and kerning are untouched and only the result is turned.
        /// −π/2 gives a label reading bottom-to-top, which is what a narrow
        /// vertical strip down the left of a panel wants.
        /// </summary>
        public void DrawSdfString(SdfFont sdfFont, string text, Vector2 position, float pixelSize, Color color, int borderSize, Color? borderColor, Rectangle? clipRectOverride, float rotation)
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
            // BorderFade = 0.1, matching ShapeDrawExtensions.DrawString's
            // own default outline dimming — alpha forced to exactly 0 when
            // there's no border so the shader's degenerate-case identity
            // holds (see SdfText.fx's own comment).
            Color effectiveBorderColor = borderSize > 0 ? (borderColor ?? color * 0.1f) : Color.Transparent;

            float glyphScale = pixelSize / sdfFont.EmSize;
            float cursorX = position.X;
            Vector4 clipRect = GetClipRectForGeometry(clipRectOverride);
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
                    GeometryBatch.AppendGlyphQuad(sdfFont.Atlas, destPos, destSize, src, color, smoothing, borderWidth, effectiveBorderColor, clipRect, rotation, position);
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

            GeometryBatch.AppendCachedTriangles(white.Texture, localVerts, indices, primitiveCount, offset, tint, uv, GetClipRectForGeometry(), CurrentBlend);
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
            // 2026-08-30: the End()/Begin() pair is GONE, third attempt, this
            // time with the failure above root-caused rather than retried.
            //
            // The crash was never really about scissor. Those flushes were
            // accidentally the thing keeping GeometryBatch's arrays small --
            // a clipped container every few elements meant they never grew.
            // Remove them and a maximised timeline accumulates a whole frame
            // into one pair of arrays; EnsureCapacity doubles them, and
            // FlushAccumulator disposes and rebuilds the GPU buffers every
            // time the CPU side grows, because it sizes them from
            // Vertices.Length. Multi-MB LOH allocations plus mid-frame
            // DynamicVertexBuffer churn -- which is exactly the reported
            // signature: a 1.16s spike and heavy GC, then a native access
            // violation in no managed frame.
            //
            // GeometryBatch.MaxVerticesBeforeFlush now bounds that growth
            // directly, which is what the coupling was doing by accident.
            //
            // Nothing needs the GPU scissor rect any more either: geometry
            // clips per-vertex (GeometryBatch.fx reads ClipRect; Flush leaves
            // ScissorTestEnable off), and text became geometry in Phase 7.
            // The rect is still set because the raw DrawFullScreenEffect path
            // and anything else reading device state should see something
            // sane, but setting it no longer costs a flush.
            // No GraphicsDevice.ScissorRectangle write either (2026-08-30).
            // Scissor testing is off in every draw -- GeometryBatch.Flush uses
            // RasterizerState.CullNone, and BeginSprite stopped setting a
            // ScissorTestEnable rasterizer state -- so the rect was read by
            // nothing. It was pure device chatter on every clipped container,
            // and there are a lot of those.
            //
            // What clips is GeometryVertex.ClipRect, stamped by every Append*
            // from GetClipRectForGeometry() and tested in the fragment
            // shader. scissorStack is still the source of truth for that; it
            // just no longer has a GPU-side shadow.

            // The growth ceiling the flush-coupling used to provide for free.
            if (geometryBatch != null && geometryBatch.WantsFlush)
            {
                FlushGeometryBatch();
            }
        }
    }
}
