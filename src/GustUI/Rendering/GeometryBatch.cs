using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using GustUI.Managers;
using System;
using System.Collections.Generic;

namespace GustUI.Rendering
{
    /// <summary>
    /// Frame-scoped vertex/index accumulator backing DrawManager's geometry
    /// rendering path (DrawManager.UseGeometryBackend). Callers append
    /// shapes throughout the frame (Element.Draw's tree walk, same call
    /// sites as today's SpriteBatchExtensions); Flush() uploads everything
    /// once via persistent DynamicVertexBuffer/DynamicIndexBuffer and issues
    /// one DrawIndexedPrimitives call per texture/blend-mode SEGMENT — clip
    /// rects are baked per-vertex (GeometryVertex.ClipRect) and tested via
    /// clip() in GeometryBatch.fx, so changing scissor mid-frame does NOT
    /// start a new segment the way changing texture or blend mode still
    /// must (both are real GPU pipeline state with no Reach-safe way
    /// around them). This is the structural fix for the measured cost:
    /// today, PushScissor/PopScissor force a full SpriteBatch End()+Begin()
    /// pair via SetScissor, scaling with visible clipped-element count
    /// (SequencerView's per-automation-lane and per-run-label ClipChildren
    /// pool sites); here, clip is just data on a vertex.
    ///
    /// A segment also closes early, even with no texture/blend change,
    /// whenever appending would push its vertex count past the 16-bit
    /// index ceiling (65535) — the same hard limit DrawManager.DrawTriangles
    /// already documents; Reach's index buffers are 16-bit only.
    ///
    /// ONE STREAM, STRICT APPEND ORDER (2026-08-30). What is drawn is
    /// exactly what the tree walk asked for, in the order it asked, and
    /// Element.Draw has already sorted that walk by Depth. This class does
    /// not reorder anything.
    ///
    /// It used to (2026-08-20 to 2026-08-30): three parallel streams,
    /// nonText/text/overlay, flushed in that order. The reason was real —
    /// a text draw is a hard shader boundary (SDF glyphs need SdfText.fx,
    /// everything else GeometryBatch.fx, so a glyph ALWAYS closes the open
    /// segment), tree order interleaves text with non-text constantly, and
    /// the sequencer was fragmenting into ~2000 segments a frame at ~4300
    /// elements. Sorting the segment list could not fix it, since a segment
    /// is an index range into a buffer already laid out in append order;
    /// only separate accumulators make same-key content physically
    /// contiguous.
    ///
    /// The cost was that the frame no longer drew in the order it was
    /// written. That is harmless for ordinary tiled content — a label sits
    /// on its own container's fill, grid siblings do not overlap — and
    /// wrong for anything that must cover ANOTHER element's text, which is
    /// most of what a UI does when it puts something on top of something
    /// else. Depth stopped deciding that, and Element.IsOverlay appeared to
    /// opt a subtree back into strict order. Thirteen call sites ended up
    /// setting it, four of them modals, which is the tell: a modal should
    /// not have to know how the renderer batches in order to cover what is
    /// beneath it. Text went on showing through solid panels regardless.
    ///
    /// So it was measured rather than argued about. Maximised sequencer,
    /// 1232 elements: three streams gave 80 segments and 1.69 ms of draw
    /// time; one stream gave 257 segments and 1.57 ms. Triple the draw
    /// calls, no cost that shows up in a frame — 257 DrawIndexedPrimitives
    /// is simply not a lot. The split was defending a number that had
    /// stopped mattering, and charging correctness for it.
    ///
    /// If segment count ever does start mattering, the fix is to remove the
    /// shader boundary rather than reorder around it: give glyphs and shapes
    /// one shader and one atlas so they share segments outright. That is a
    /// real project (per-vertex text params, multi-atlas binding) and this
    /// note is here so nobody reaches for the reordering again first.
    /// </summary>
    public class GeometryBatch
    {
        private const int MaxVerticesPerSegment = 65535;

        /// <summary>
        /// Vertices one accumulator may hold before <see cref="WantsFlush"/>
        /// asks the caller to flush.
        ///
        /// Exists because scissor changes stopped forcing a flush
        /// (DrawManager.SetScissor, 2026-08-30). They were, accidentally, the
        /// thing that kept these arrays small: a clipped container every few
        /// elements meant the buffers never grew far. Without that, a
        /// maximised timeline accumulates the whole frame into one pair of
        /// arrays, EnsureCapacity doubles them, and FlushAccumulator throws
        /// away and rebuilds the GPU buffers every time the CPU side grows --
        /// multi-MB large-object allocations and mid-frame
        /// DynamicVertexBuffer churn. That is the shape of the native access
        /// violation that made the two earlier attempts at this
        /// (2026-08-19, 2026-08-20) unsafe.
        ///
        /// 32768 vertices is ~1.4 MB per accumulator at 44 bytes a vertex --
        /// under the large-object heap threshold's spirit, comfortably more
        /// than a normal frame needs, and still far fewer flushes than one
        /// per scissor change.
        /// </summary>
        private const int MaxVerticesBeforeFlush = 32768;

        /// <summary>
        /// SDF-text-only per-segment uniforms (Phase 7: SDF glyphs share
        /// this same accumulator/vertex-buffer, just flushed through
        /// SdfText.fx instead of GeometryBatch.fx for segments where this is
        /// set) — Smoothing/BorderWidth/BorderColor are per-DRAW-CALL
        /// uniforms in the shader, not per-vertex, so unlike flat shapes
        /// (which only need a texture+blend match to share a segment),
        /// glyphs also need a MATCHING TextParams to batch together. In
        /// practice this means same-size, same-border-state text (the
        /// overwhelming common case — most UI text uses 2-3 distinct sizes)
        /// batches into one segment; a font-size or border change starts a
        /// new one, same granularity a texture change already forces.
        /// </summary>
        private struct TextParams : IEquatable<TextParams>
        {
            public float Smoothing;
            public float BorderWidth;
            public Color BorderColor;

            public bool Equals(TextParams other) =>
                Smoothing == other.Smoothing && BorderWidth == other.BorderWidth && BorderColor == other.BorderColor;
        }

        private struct Segment
        {
            public Texture2D Texture;
            public BlendState Blend;
            public int IndexStart;
            public int IndexCount;
            public int VertexStart;
            public bool IsText;
            public TextParams TextParams;
        }

        /// <summary>
        /// One independent append/segment stream — everything GeometryBatch
        /// used to do with its own top-level fields, now instantiated twice
        /// (see this class's own doc comment for why). Vertex/index storage
        /// and GPU buffers are each accumulator's own; nothing is shared.
        /// </summary>
        private sealed class Accumulator
        {
            public GeometryVertex[] Vertices = new GeometryVertex[4096];
            public short[] Indices = new short[6144];
            public int VertexCount;
            public int IndexCount;
            public readonly List<Segment> Segments = new List<Segment>();

            public Texture2D CurrentTexture;
            public BlendState CurrentBlend;
            public bool CurrentIsText;
            public TextParams CurrentTextParams;
            public int CurrentSegmentVertexStart;
            public int CurrentSegmentIndexStart;
            public bool HasOpenSegment;

            public DynamicVertexBuffer VertexBuffer;
            public DynamicIndexBuffer IndexBuffer;

            public bool IsEmpty => IndexCount == 0;

            public void BeginFrame()
            {
                VertexCount = 0;
                IndexCount = 0;
                Segments.Clear();
                HasOpenSegment = false;
                CurrentTexture = null;
                CurrentBlend = null;
            }

            public void EnsureCapacity(int addVertices, int addIndices)
            {
                if (VertexCount + addVertices > Vertices.Length)
                {
                    int newSize = Vertices.Length * 2;
                    while (newSize < VertexCount + addVertices)
                    {
                        newSize *= 2;
                    }
                    Array.Resize(ref Vertices, newSize);
                }

                if (IndexCount + addIndices > Indices.Length)
                {
                    int newSize = Indices.Length * 2;
                    while (newSize < IndexCount + addIndices)
                    {
                        newSize *= 2;
                    }
                    Array.Resize(ref Indices, newSize);
                }
            }

            public void BeginSegmentIfNeeded(Texture2D texture, BlendState blend, int addVertices)
            {
                BeginSegmentIfNeeded(texture, blend, addVertices, false, default);
            }

            public void BeginSegmentIfNeeded(Texture2D texture, BlendState blend, int addVertices, bool isText, TextParams textParams)
            {
                bool stateChanged = !HasOpenSegment || texture != CurrentTexture || blend != CurrentBlend
                    || isText != CurrentIsText || (isText && !textParams.Equals(CurrentTextParams));
                bool wouldOverflow = HasOpenSegment && (VertexCount - CurrentSegmentVertexStart + addVertices > MaxVerticesPerSegment);

                if (stateChanged || wouldOverflow)
                {
                    CloseSegment();
                    CurrentTexture = texture;
                    CurrentBlend = blend;
                    CurrentIsText = isText;
                    CurrentTextParams = textParams;
                    CurrentSegmentVertexStart = VertexCount;
                    CurrentSegmentIndexStart = IndexCount;
                    HasOpenSegment = true;
                }
            }

            // See the original (pre-split) CloseSegment's doc comment,
            // preserved on GeometryBatch's own Flush below, for why
            // VertexStart/segment-relative indices exist at all.
            public void CloseSegment()
            {
                if (HasOpenSegment && IndexCount > CurrentSegmentIndexStart)
                {
                    Segments.Add(new Segment
                    {
                        Texture = CurrentTexture,
                        Blend = CurrentBlend,
                        IndexStart = CurrentSegmentIndexStart,
                        IndexCount = IndexCount - CurrentSegmentIndexStart,
                        VertexStart = CurrentSegmentVertexStart,
                        IsText = CurrentIsText,
                        TextParams = CurrentTextParams,
                    });
                }

                HasOpenSegment = false;
            }
        }

        private readonly GraphicsDevice device;
        // ONE stream, in strict append order (2026-08-30). Append order is
        // tree draw order, which Element.Draw has already sorted by Depth --
        // so what is drawn is exactly what the app asked for, and Depth means
        // what it says.
        //
        // There were three (nonText/text/overlay), because a text draw is a
        // hard shader boundary and tree order interleaves text with non-text
        // constantly, so one stream fragmented into a segment every couple of
        // elements. Routing them apart made each stream's content contiguous
        // and cut the segment count hard.
        //
        // It also silently reordered the frame: all non-text, then all text,
        // then the overlay stream. That is fine until something has to sit on
        // top of ANOTHER element's text, at which point Depth stopped meaning
        // anything and the only way out was Element.IsOverlay, a flag opting a
        // subtree into the last stream. Thirteen places ended up setting it --
        // four modals among them, which is the tell: a modal should not need
        // to know how the renderer batches in order to cover what is under it.
        // Their own comments recorded the damage ("neither depth nor parenting
        // was ever going to be", "non-text always draws before text now, Depth
        // or not") and text was still showing through solid panels.
        //
        // Measured before removing it, maximised sequencer, 1232 elements:
        // three streams 80 segments and 1.69 ms of draw; one stream 257
        // segments and 1.57 ms. Triple the draw calls and no cost that shows
        // up in a frame -- 257 is simply not a lot for a GPU. The split was
        // optimising a number that had stopped mattering, and paying for it in
        // correctness.
        private readonly Accumulator stream = new Accumulator();

        public GeometryBatch(GraphicsDevice device)
        {
            this.device = device;
        }

        public bool IsEmpty => stream.IsEmpty;

        /// <summary>
        /// True once any stream has accumulated more than
        /// <see cref="MaxVerticesBeforeFlush"/> vertices. Advisory: the batch
        /// cannot flush itself (it has no effects), so DrawManager checks this
        /// and calls Flush. Bounds a frame's buffer growth now that scissor
        /// changes no longer do it as a side effect.
        /// </summary>
        public bool WantsFlush => stream.VertexCount > MaxVerticesBeforeFlush;

        // Segment count (one DrawIndexedPrimitives call each in Flush) is
        // NOT the same as flush count (FrameProfiler's "flushes") — many
        // segments can close within one flush purely from texture/blend
        // churn in draw ORDER (e.g. text interleaved with non-text content
        // per element), independent of how many real PushScissor-driven
        // flushes happened. Added 2026-08-20: found ~2000 segments/frame at
        // ~4300 visible elements on ezmuze-studio's sequencer — roughly one
        // segment per two elements, meaning draw order rarely groups two
        // same-texture elements consecutively (the motivation for the
        // two-accumulator split above). Kept as a standing stat (cheap: one
        // int increment per Flush) since it's the only visible signal for
        // this specific cost, which "flushes" alone hides. Accumulated
        // across every Flush() within a frame (each one clears segment
        // lists) and reset in BeginFrame, so this is a true per-frame total.
        public static int SegmentsThisFrame;

        /// <summary>Call once at the start of each frame, before any Append* calls.</summary>
        public void BeginFrame()
        {
            stream.BeginFrame();
            SegmentsThisFrame = 0;
        }

        private static void UVRect(Texture2D texture, Rectangle srcRect, out float u0, out float v0, out float u1, out float v1)
        {
            u0 = (srcRect.X + 0.5f) / texture.Width;
            v0 = (srcRect.Y + 0.5f) / texture.Height;
            u1 = (srcRect.X + srcRect.Width - 0.5f) / texture.Width;
            v1 = (srcRect.Y + srcRect.Height - 0.5f) / texture.Height;
        }

        private static void AppendQuadIndices(Accumulator acc, int vBase)
        {
            acc.Indices[acc.IndexCount++] = (short)(vBase + 0);
            acc.Indices[acc.IndexCount++] = (short)(vBase + 1);
            acc.Indices[acc.IndexCount++] = (short)(vBase + 2);
            acc.Indices[acc.IndexCount++] = (short)(vBase + 0);
            acc.Indices[acc.IndexCount++] = (short)(vBase + 2);
            acc.Indices[acc.IndexCount++] = (short)(vBase + 3);
        }

        /// <summary>
        /// A 0..1 alpha multiplier applied to EVERY colour appended, so a whole
        /// subtree can be faded without every element knowing how to fade
        /// itself. Pushed and popped around a subtree by
        /// <see cref="Elements.Element.Opacity"/>; every draw path in the
        /// library funnels through the Append methods below, which is what
        /// makes one field here enough.
        ///
        /// ALPHA only — see <see cref="Fade"/>.
        /// </summary>
        public float Opacity { get; set; } = 1f;

        /// <summary>
        /// Scales a colour's ALPHA by <see cref="Opacity"/> and leaves its
        /// channels alone.
        ///
        /// Not <c>color * Opacity</c>, which is the obvious spelling and the
        /// wrong one: multiplying a Color scales the channels as well, so a
        /// half-faded white becomes a translucent mid-grey and everything
        /// "fading out" visibly darkens on the way. Against straight alpha
        /// blending only the alpha may move.
        /// </summary>
        private Color Fade(Color color) =>
            Opacity >= 1f ? color : new Color(color.R, color.G, color.B, (byte)(color.A * Opacity));

        /// <summary>
        /// Appends an axis-aligned textured quad — the common case.
        /// DrawFilledRectangle/DrawRectangle/DrawRoundedRectangle's corner
        /// blits all reduce to this.
        /// </summary>
        public void AppendQuad(Texture2D texture, Rectangle destRect, Rectangle srcRect, Color color, Vector4 clipRect, BlendState blend)
        {
            if (destRect.Width == 0 || destRect.Height == 0)
            {
                return;
            }

            color = Fade(color);

            Accumulator acc = stream;
            acc.BeginSegmentIfNeeded(texture, blend, 4);
            acc.EnsureCapacity(4, 6);

            UVRect(texture, srcRect, out float u0, out float v0, out float u1, out float v1);

            // Segment-relative — see CloseSegment's doc for why.
            int vBase = acc.VertexCount - acc.CurrentSegmentVertexStart;
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(new Vector2(destRect.Left, destRect.Top), color, new Vector2(u0, v0), clipRect);
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(new Vector2(destRect.Right, destRect.Top), color, new Vector2(u1, v0), clipRect);
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(new Vector2(destRect.Right, destRect.Bottom), color, new Vector2(u1, v1), clipRect);
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(new Vector2(destRect.Left, destRect.Bottom), color, new Vector2(u0, v1), clipRect);

            AppendQuadIndices(acc, vBase);
        }

        /// <summary>
        /// Appends an axis-aligned quad with a PER-CORNER color instead of
        /// one flat color — the vertex-color equivalent of what
        /// TVFillSimpleGradient used to bake into a throwaway 256x1
        /// Texture2D. Callers pass the shared white texel as texture/srcRect
        /// (same convention as AppendQuad), so a gradient costs no extra
        /// texture upload and no extra segment over an ordinary flat-color
        /// quad — the GPU already interpolates vertex color across the quad
        /// for free.
        /// </summary>
        public void AppendQuadGradient(Texture2D texture, Rectangle destRect, Rectangle srcRect, Color colorTopLeft, Color colorTopRight, Color colorBottomRight, Color colorBottomLeft, Vector4 clipRect, BlendState blend)
        {
            colorTopLeft = Fade(colorTopLeft);
            colorTopRight = Fade(colorTopRight);
            colorBottomRight = Fade(colorBottomRight);
            colorBottomLeft = Fade(colorBottomLeft);
            if (destRect.Width == 0 || destRect.Height == 0)
            {
                return;
            }

            Accumulator acc = stream;
            acc.BeginSegmentIfNeeded(texture, blend, 4);
            acc.EnsureCapacity(4, 6);

            UVRect(texture, srcRect, out float u0, out float v0, out float u1, out float v1);

            // Segment-relative — see CloseSegment's doc for why.
            int vBase = acc.VertexCount - acc.CurrentSegmentVertexStart;
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(new Vector2(destRect.Left, destRect.Top), colorTopLeft, new Vector2(u0, v0), clipRect);
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(new Vector2(destRect.Right, destRect.Top), colorTopRight, new Vector2(u1, v0), clipRect);
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(new Vector2(destRect.Right, destRect.Bottom), colorBottomRight, new Vector2(u1, v1), clipRect);
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(new Vector2(destRect.Left, destRect.Bottom), colorBottomLeft, new Vector2(u0, v1), clipRect);

            AppendQuadIndices(acc, vBase);
        }

        /// <summary>
        /// Appends a quad rotated by <paramref name="angle"/> radians around
        /// <paramref name="origin"/> (in the unrotated rect's own local
        /// space, top-left = 0,0) — the DrawLine/DrawThickLine rotated-rect
        /// idiom SpriteBatch.Draw(texture, rect, src, color, angle, origin,
        /// effects, depth) implements today.
        /// </summary>
        public void AppendRotatedQuad(Texture2D texture, Rectangle destRect, Rectangle srcRect, Color color, float angle, Vector2 origin, Vector4 clipRect, BlendState blend)
        {
            color = Fade(color);
            if (destRect.Width == 0 || destRect.Height == 0)
            {
                return;
            }

            Accumulator acc = stream;
            acc.BeginSegmentIfNeeded(texture, blend, 4);
            acc.EnsureCapacity(4, 6);

            UVRect(texture, srcRect, out float u0, out float v0, out float u1, out float v1);

            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);
            Vector2 pos = new Vector2(destRect.X, destRect.Y);

            Vector2 Corner(float lx, float ly)
            {
                float dx = lx - origin.X;
                float dy = ly - origin.Y;
                return new Vector2(pos.X + (dx * cos) - (dy * sin), pos.Y + (dx * sin) + (dy * cos));
            }

            // Segment-relative — see CloseSegment's doc for why.
            int vBase = acc.VertexCount - acc.CurrentSegmentVertexStart;
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(Corner(0, 0), color, new Vector2(u0, v0), clipRect);
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(Corner(destRect.Width, 0), color, new Vector2(u1, v0), clipRect);
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(Corner(destRect.Width, destRect.Height), color, new Vector2(u1, v1), clipRect);
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(Corner(0, destRect.Height), color, new Vector2(u0, v1), clipRect);

            AppendQuadIndices(acc, vBase);
        }

        /// <summary>
        /// Appends one SDF glyph quad — DrawManager.DrawSdfString's former
        /// per-glyph spriteBatch.Draw call, ported into this accumulator.
        /// Unlike AppendQuad/AppendRotatedQuad's srcRect handling, this uses
        /// the PLAIN (non-half-texel-inset) UV mapping — matching exactly
        /// what SpriteBatch.Draw's own internal texCoord computation already
        /// did for glyphs (confirmed by reading KNI's SpriteBatch.cs source:
        /// srcRect.X * texture.TexelWidth, no offset) — SDF glyphs are NOT
        /// atlas-packed by TextureAtlas's tight shelf packer (they come from
        /// SdfFont's own StbTrueTypeSharp-baked atlas, already spaced by
        /// SdfFontBaker.Padding to avoid bleed), so the half-texel inset
        /// UVRect uses for flat shapes doesn't apply here and would clip
        /// valid distance-field data at each glyph's own edge.
        /// destSize is the glyph's on-screen size (srcRect scaled by the
        /// caller's glyphScale) — passed explicitly rather than derived from
        /// srcRect, mirroring DrawSdfString's original position+scale draw.
        /// Routes to the text accumulator (or overlay, if currently inside
        /// one — see SelectAccumulator) — the only Append* method that ever
        /// picks text over nonText.
        /// </summary>
        public void AppendGlyphQuad(Texture2D atlas, Vector2 destPos, Vector2 destSize, Rectangle srcRect, Color color, float smoothing, float borderWidth, Color borderColor, Vector4 clipRect)
            => AppendGlyphQuad(atlas, destPos, destSize, srcRect, color, smoothing, borderWidth, borderColor, clipRect, 0f, Vector2.Zero);

        /// <summary>
        /// A glyph quad, optionally ROTATED about a pivot.
        ///
        /// The four corners are rotated here rather than the string being
        /// re-laid-out at an angle: a caller lays a line out along +X as it
        /// always has and hands the same pivot to every glyph of it, so the
        /// line comes out rotated as a whole with its spacing and kerning
        /// untouched. The clip rectangle stays axis-aligned, because it is a
        /// scissor and always was — a rotated label needs a box big enough to
        /// hold it, which is the caller's business.
        /// </summary>
        public void AppendGlyphQuad(Texture2D atlas, Vector2 destPos, Vector2 destSize, Rectangle srcRect, Color color, float smoothing, float borderWidth, Color borderColor, Vector4 clipRect, float rotation, Vector2 pivot)
        {
            if (destSize.X <= 0f || destSize.Y <= 0f)
            {
                return;
            }

            color = Fade(color);
            borderColor = Fade(borderColor);

            Accumulator acc = stream;
            var textParams = new TextParams { Smoothing = smoothing, BorderWidth = borderWidth, BorderColor = borderColor };
            acc.BeginSegmentIfNeeded(atlas, null, 4, true, textParams);
            acc.EnsureCapacity(4, 6);

            float u0 = (float)srcRect.X / atlas.Width;
            float v0 = (float)srcRect.Y / atlas.Height;
            float u1 = (float)(srcRect.X + srcRect.Width) / atlas.Width;
            float v1 = (float)(srcRect.Y + srcRect.Height) / atlas.Height;

            Vector2 topLeft = destPos;
            var topRight = new Vector2(destPos.X + destSize.X, destPos.Y);
            var bottomRight = new Vector2(destPos.X + destSize.X, destPos.Y + destSize.Y);
            var bottomLeft = new Vector2(destPos.X, destPos.Y + destSize.Y);

            if (rotation != 0f)
            {
                float sin = (float)System.Math.Sin(rotation);
                float cos = (float)System.Math.Cos(rotation);
                topLeft = Rotate(topLeft, pivot, sin, cos);
                topRight = Rotate(topRight, pivot, sin, cos);
                bottomRight = Rotate(bottomRight, pivot, sin, cos);
                bottomLeft = Rotate(bottomLeft, pivot, sin, cos);
            }

            // Segment-relative — see CloseSegment's doc for why.
            int vBase = acc.VertexCount - acc.CurrentSegmentVertexStart;
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(topLeft, color, new Vector2(u0, v0), clipRect);
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(topRight, color, new Vector2(u1, v0), clipRect);
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(bottomRight, color, new Vector2(u1, v1), clipRect);
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(bottomLeft, color, new Vector2(u0, v1), clipRect);

            AppendQuadIndices(acc, vBase);
        }

        private static Vector2 Rotate(Vector2 point, Vector2 pivot, float sin, float cos)
        {
            float dx = point.X - pivot.X;
            float dy = point.Y - pivot.Y;
            return new Vector2(
                pivot.X + (dx * cos) - (dy * sin),
                pivot.Y + (dx * sin) + (dy * cos));
        }

        /// <summary>
        /// Appends caller-supplied indexed triangle geometry (WaveformData's
        /// real vertex/index buffers, DrawManager.DrawTriangles' compat
        /// overload) sharing one texture/blend across all of it. Each
        /// vertex's ClipRect is stamped with the current scissor here,
        /// overwriting whatever the caller passed — callers building raw
        /// geometry don't track clip state themselves.
        /// </summary>
        public void AppendTriangles(Texture2D texture, GeometryVertex[] verts, short[] idx, int primitiveCount, Vector4 clipRect, BlendState blend)
        {
            if (primitiveCount <= 0 || verts == null || verts.Length == 0)
            {
                return;
            }

            int addVertices = verts.Length;
            int addIndices = primitiveCount * 3;

            Accumulator acc = stream;
            acc.BeginSegmentIfNeeded(texture, blend, addVertices);
            acc.EnsureCapacity(addVertices, addIndices);

            // Segment-relative — see CloseSegment's doc for why.
            int vBase = acc.VertexCount - acc.CurrentSegmentVertexStart;
            for (int i = 0; i < addVertices; i++)
            {
                GeometryVertex v = verts[i];
                v.ClipRect = clipRect;

                // Faded on the way into the accumulator rather than in place:
                // verts is the CALLER's array and is often a reused scratch
                // buffer, so dimming it would dim the next frame too.
                v.Color = Fade(v.Color);
                acc.Vertices[acc.VertexCount++] = v;
            }

            for (int i = 0; i < addIndices; i++)
            {
                acc.Indices[acc.IndexCount++] = (short)(vBase + idx[i]);
            }
        }

        /// <summary>
        /// The <see cref="VertexPositionColor"/> form, for
        /// DrawManager.DrawTriangles' callers (WaveformElement's live
        /// triangulation, PianoRollElement's MPE bend curves) — geometry
        /// built by code that predates <see cref="GeometryVertex"/> and has
        /// no UV or clip rect of its own to give.
        ///
        /// Converted WHILE copying into this batch's own vertex array rather
        /// than through an intermediate GeometryVertex[], for the same reason
        /// <see cref="AppendCachedTriangles"/> translates in place: this runs
        /// per visible waveform block per frame, and the intermediate would be
        /// a fresh allocation each time for data that only ever needed to
        /// exist in this buffer.
        ///
        /// <paramref name="uv"/> is stamped on every vertex — the caller's
        /// geometry is flat-coloured, so it samples the atlas's reserved white
        /// texel.
        /// </summary>
        public void AppendTriangles(Texture2D texture, VertexPositionColor[] verts, short[] idx, int primitiveCount, Vector2 uv, Vector4 clipRect, BlendState blend)
        {
            if (primitiveCount <= 0 || verts == null || verts.Length == 0)
            {
                return;
            }

            int addVertices = verts.Length;
            int addIndices = primitiveCount * 3;

            Accumulator acc = stream;
            acc.BeginSegmentIfNeeded(texture, blend, addVertices);
            acc.EnsureCapacity(addVertices, addIndices);

            int vBase = acc.VertexCount - acc.CurrentSegmentVertexStart;
            for (int i = 0; i < addVertices; i++)
            {
                VertexPositionColor v = verts[i];

                // Z is dropped, not carried: GeometryVertex.Position.Z is
                // always 0 by contract (see its own doc for the layerDepth
                // bug that reintroduces), and this geometry is flat anyway.
                acc.Vertices[acc.VertexCount++] = new GeometryVertex(
                    new Vector2(v.Position.X, v.Position.Y), Fade(v.Color), uv, clipRect);
            }

            for (int i = 0; i < addIndices; i++)
            {
                acc.Indices[acc.IndexCount++] = (short)(vBase + idx[i]);
            }
        }

        /// <summary>
        /// Same shape as <see cref="AppendTriangles"/>, for one specific
        /// caller (WaveformData's cached-array "Geometry (Baked)" mode,
        /// DrawManager.DrawCachedTriangles): <paramref name="localVerts"/>
        /// is LOCAL-space (relative to (0,0)) and gets translated by
        /// <paramref name="offset"/> and multiplied by <paramref name="tint"/>
        /// WHILE copying into this batch's own vertex array, instead of the
        /// caller building a separate translated array first — that array
        /// would otherwise be a fresh per-call allocation (this runs once
        /// per visible waveform block, every frame), pure GC churn for data
        /// that only ever needed to exist inside this buffer anyway. UV is
        /// stamped fresh here too (not read from <paramref name="localVerts"/>)
        /// since a long-lived cache can outlive an atlas grow/rebuild that
        /// relocates the white region this samples.
        /// </summary>
        public void AppendCachedTriangles(Texture2D texture, GeometryVertex[] localVerts, short[] idx, int primitiveCount, Vector2 offset, Color tint, Vector2 uv, Vector4 clipRect, BlendState blend)
        {
            tint = Fade(tint);
            if (primitiveCount <= 0 || localVerts == null || localVerts.Length == 0)
            {
                return;
            }

            int addVertices = localVerts.Length;
            int addIndices = primitiveCount * 3;

            Accumulator acc = stream;
            acc.BeginSegmentIfNeeded(texture, blend, addVertices);
            acc.EnsureCapacity(addVertices, addIndices);

            Vector4 tintVec = tint.ToVector4();
            // Segment-relative — see CloseSegment's doc for why.
            int vBase = acc.VertexCount - acc.CurrentSegmentVertexStart;
            for (int i = 0; i < addVertices; i++)
            {
                GeometryVertex src = localVerts[i];
                acc.Vertices[acc.VertexCount++] = new GeometryVertex(
                    new Vector2(src.Position.X + offset.X, src.Position.Y + offset.Y),
                    new Color(src.Color.ToVector4() * tintVec),
                    uv,
                    clipRect);
            }

            for (int i = 0; i < addIndices; i++)
            {
                acc.Indices[acc.IndexCount++] = (short)(vBase + idx[i]);
            }
        }

        /// <summary>
        /// Uploads whatever geometry has accumulated SINCE THE LAST FLUSH
        /// (not necessarily the whole frame) and issues one
        /// DrawIndexedPrimitives call per texture/blend segment, then resets
        /// the accumulator so it's ready to accept more Append* calls
        /// immediately — safe to call more than once per frame.
        ///
        /// This is called from DrawManager.End() (in addition to the final
        /// call DrawLoop makes), the same synchronization points that force
        /// a SpriteBatch flush today (scissor change, BeginAdditive,
        /// DrawTriangles, DrawSdfString) — during the staged migration
        /// (some primitives still on SpriteBatch, some already on
        /// GeometryBatch) this keeps geometry and sprite content
        /// interleaved in roughly tree-draw order at each such boundary,
        /// rather than all geometry content silently rendering after (or
        /// before) all sprite content for the whole frame regardless of
        /// which was actually drawn first. Once SpriteBatch is fully
        /// retired (migration's final phase) End() is called only once
        /// per frame anyway, so this degrades to "flush once," at zero
        /// extra cost.
        ///
        /// Draws the NON-TEXT accumulator's segments, then the TEXT
        /// accumulator's — see this class's own doc comment for why that
        /// fixed order is safe (a label sits on top of its own container)
        /// without being a fully general reordering.
        ///
        /// Takes TWO effects (Phase 7: SDF text shares this accumulator) —
        /// <paramref name="flatEffect"/> (GeometryBatch.fx) for ordinary
        /// shape segments, <paramref name="textEffect"/> (SdfText.fx) for
        /// segments appended via AppendGlyphQuad. The caller (DrawManager.
        /// FlushGeometryBatch) sets MatrixTransform/RenderScale on BOTH
        /// before calling this (frame-constant, cheap to duplicate); THIS
        /// method sets textEffect's Smoothing/BorderWidth/BorderColor
        /// PER TEXT SEGMENT as it iterates, since — unlike MatrixTransform —
        /// those genuinely vary segment-to-segment (different font sizes/
        /// border states force new segments in the first place, exactly so
        /// each one can carry its own values here).
        /// </summary>
        public void Flush(Effect flatEffect, Effect textEffect)
        {
            stream.CloseSegment();

            if (stream.IndexCount == 0)
            {
                return;
            }

            using (Managers.Telemetry.Scope("Draw.GeometryFlush.Submit"))
            {
                FlushAccumulator(stream, flatEffect, textEffect);
            }

            FrameProfiler.CountFlush();
        }

        private void FlushAccumulator(Accumulator acc, Effect flatEffect, Effect textEffect)
        {
            if (acc.IndexCount == 0)
            {
                return;
            }

            if (acc.VertexBuffer == null || acc.VertexBuffer.VertexCount < acc.Vertices.Length)
            {
                acc.VertexBuffer?.Dispose();
                acc.VertexBuffer = new DynamicVertexBuffer(device, GeometryVertex.VertexDeclaration, acc.Vertices.Length, BufferUsage.WriteOnly);
            }

            if (acc.IndexBuffer == null || acc.IndexBuffer.IndexCount < acc.Indices.Length)
            {
                acc.IndexBuffer?.Dispose();
                acc.IndexBuffer = new DynamicIndexBuffer(device, IndexElementSize.SixteenBits, acc.Indices.Length, BufferUsage.WriteOnly);
            }

            using (Managers.Telemetry.Scope("Draw.GeometryFlush.SetData"))
            {
                acc.VertexBuffer.SetData(acc.Vertices, 0, acc.VertexCount, SetDataOptions.Discard);
                acc.IndexBuffer.SetData(acc.Indices, 0, acc.IndexCount, SetDataOptions.Discard);
            }

            device.SetVertexBuffer(acc.VertexBuffer);
            device.Indices = acc.IndexBuffer;
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = RasterizerState.CullNone;

            foreach (Segment segment in acc.Segments)
            {
                device.BlendState = segment.Blend ?? BlendState.AlphaBlend;
                Effect effect = segment.IsText ? textEffect : flatEffect;

                if (segment.IsText)
                {
                    effect.Parameters["Smoothing"].SetValue(segment.TextParams.Smoothing);
                    effect.Parameters["BorderWidth"].SetValue(segment.TextParams.BorderWidth);
                    effect.Parameters["BorderColor"].SetValue(segment.TextParams.BorderColor.ToVector4());
                }

                foreach (EffectPass pass in effect.CurrentTechnique.Passes)
                {
                    // Texture/sampler MUST be (re-)bound AFTER Apply(), not
                    // before — see the long GOTCHA comment in GeometryBatch.fx.
                    // On this KNI DesktopGL target, EffectPass.Apply() resets
                    // texture/sampler bindings for an Effect with its own
                    // declared `sampler` object; binding before Apply() runs
                    // with zero errors but every fragment samples fully
                    // transparent, silently vanishing every shape.
                    pass.Apply();
                    device.Textures[0] = segment.Texture;
                    device.SamplerStates[0] = SamplerState.LinearClamp;
                    device.DrawIndexedPrimitives(PrimitiveType.TriangleList, segment.VertexStart, segment.IndexStart, segment.IndexCount / 3);
                }
            }

            SegmentsThisFrame += acc.Segments.Count;
            Managers.FrameProfiler.CountSegments(acc.Segments.Count);

            // Reset so the next Append* call after this mid-frame flush
            // starts a clean batch rather than accumulating on top of
            // geometry that's already been drawn.
            acc.VertexCount = 0;
            acc.IndexCount = 0;
            acc.Segments.Clear();
            acc.HasOpenSegment = false;
        }
    }
}
