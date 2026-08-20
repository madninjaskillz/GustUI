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
    /// THREE PARALLEL STREAMS (2026-08-20): appends route into one of
    /// nonText/text/overlay instead of one shared stream in strict draw
    /// order. Found profiling ezmuze-studio's sequencer: ~2000 segments/
    /// frame at ~4300 visible elements, roughly one per two elements,
    /// because tree-walk draw order constantly interleaves text (a hard
    /// shader boundary — SDF glyphs need SdfText.fx, everything else needs
    /// GeometryBatch.fx, so a text draw ALWAYS closes whatever segment was
    /// open) with non-text content. Sorting the SEGMENT LIST alone can't
    /// fix this: a segment is just an index range into a buffer already
    /// laid out in original append order, so two same-key segments
    /// separated by a different-key one aren't physically adjacent —
    /// merging them into one DrawIndexedPrimitives call needs the
    /// underlying vertex data to actually BE contiguous. Routing appends
    /// into separate accumulators achieves that for free: each stream's
    /// own content lands contiguously regardless of how it was interleaved
    /// in tree order, so within-stream segment count drops to genuine
    /// texture/blend/border-state changes only.
    ///
    /// Flush() draws nonText, then text, then overlay. "nonText before
    /// text" is safe for ordinary tiled content (a label sits on top of
    /// its own container's fill, siblings in a grid don't overlap each
    /// other) but is NOT a general reordering guarantee — anything that
    /// must render on top of OTHER text breaks it (found via
    /// SequencerView's own Depth constants: dragDropGhost/dragDropBadge/
    /// playhead are the only elements placed above DepthRunLabel, i.e. the
    /// only ones actually relying on rendering over other elements' text).
    /// Those opt into `overlay` via Element.IsOverlay, which Element.Draw()
    /// turns into PushOverlay/PopOverlay around that element's ENTIRE
    /// subtree (see PushOverlay's own doc) — so this is not "GeometryBatch
    /// decides what's safe to reorder," it's "the app declares what needs
    /// strict order, GeometryBatch keeps that separate from what doesn't."
    /// </summary>
    public class GeometryBatch
    {
        private const int MaxVerticesPerSegment = 65535;

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
        private readonly Accumulator nonText = new Accumulator();
        private readonly Accumulator text = new Accumulator();

        // Third stream for content that must NOT be subject to the
        // "all non-text, then all text" reordering above — anything whose
        // Depth places it above DepthRunLabel in SequencerView's own
        // layering (dragDropGhost, dragDropBadge, playhead: the only
        // elements there that need to render on top of OTHER text, not
        // just their own). Draws last, in original append order, exactly
        // like the single accumulator this class used to be — no reorder
        // risk, just kept separate so the other two streams stay free to
        // batch. Toggled by Element.Draw() (see Element.IsOverlay) via
        // PushOverlay/PopOverlay around one child's whole subtree, so
        // everything a flagged element draws — including its own children,
        // e.g. dragDropBadge's label — lands here together, preserving
        // their OWN relative order (rect under its own text) correctly.
        private readonly Accumulator overlay = new Accumulator();
        private int overlayDepth;

        public GeometryBatch(GraphicsDevice device)
        {
            this.device = device;
        }

        public bool IsEmpty => nonText.IsEmpty && text.IsEmpty && overlay.IsEmpty;

        public void PushOverlay() => overlayDepth++;

        public void PopOverlay() => overlayDepth--;

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
            nonText.BeginFrame();
            text.BeginFrame();
            overlay.BeginFrame();
            overlayDepth = 0;
            SegmentsThisFrame = 0;
        }

        private Accumulator SelectAccumulator(bool isText) => overlayDepth > 0 ? overlay : (isText ? text : nonText);

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

            Accumulator acc = SelectAccumulator(false);
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
            if (destRect.Width == 0 || destRect.Height == 0)
            {
                return;
            }

            Accumulator acc = SelectAccumulator(false);
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
            if (destRect.Width == 0 || destRect.Height == 0)
            {
                return;
            }

            Accumulator acc = SelectAccumulator(false);
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
        {
            if (destSize.X <= 0f || destSize.Y <= 0f)
            {
                return;
            }

            Accumulator acc = SelectAccumulator(true);
            var textParams = new TextParams { Smoothing = smoothing, BorderWidth = borderWidth, BorderColor = borderColor };
            acc.BeginSegmentIfNeeded(atlas, null, 4, true, textParams);
            acc.EnsureCapacity(4, 6);

            float u0 = (float)srcRect.X / atlas.Width;
            float v0 = (float)srcRect.Y / atlas.Height;
            float u1 = (float)(srcRect.X + srcRect.Width) / atlas.Width;
            float v1 = (float)(srcRect.Y + srcRect.Height) / atlas.Height;

            // Segment-relative — see CloseSegment's doc for why.
            int vBase = acc.VertexCount - acc.CurrentSegmentVertexStart;
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(destPos, color, new Vector2(u0, v0), clipRect);
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(new Vector2(destPos.X + destSize.X, destPos.Y), color, new Vector2(u1, v0), clipRect);
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(new Vector2(destPos.X + destSize.X, destPos.Y + destSize.Y), color, new Vector2(u1, v1), clipRect);
            acc.Vertices[acc.VertexCount++] = new GeometryVertex(new Vector2(destPos.X, destPos.Y + destSize.Y), color, new Vector2(u0, v1), clipRect);

            AppendQuadIndices(acc, vBase);
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

            Accumulator acc = SelectAccumulator(false);
            acc.BeginSegmentIfNeeded(texture, blend, addVertices);
            acc.EnsureCapacity(addVertices, addIndices);

            // Segment-relative — see CloseSegment's doc for why.
            int vBase = acc.VertexCount - acc.CurrentSegmentVertexStart;
            for (int i = 0; i < addVertices; i++)
            {
                GeometryVertex v = verts[i];
                v.ClipRect = clipRect;
                acc.Vertices[acc.VertexCount++] = v;
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
            if (primitiveCount <= 0 || localVerts == null || localVerts.Length == 0)
            {
                return;
            }

            int addVertices = localVerts.Length;
            int addIndices = primitiveCount * 3;

            Accumulator acc = SelectAccumulator(false);
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
            nonText.CloseSegment();
            text.CloseSegment();
            overlay.CloseSegment();

            if (nonText.IndexCount == 0 && text.IndexCount == 0 && overlay.IndexCount == 0)
            {
                return;
            }

            using (Managers.Telemetry.Scope("Draw.GeometryFlush.Submit"))
            {
                FlushAccumulator(nonText, flatEffect, textEffect);
                FlushAccumulator(text, flatEffect, textEffect);
                FlushAccumulator(overlay, flatEffect, textEffect);
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
