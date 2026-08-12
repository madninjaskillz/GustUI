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
            public bool IsText;
            public TextParams TextParams;
        }

        private readonly GraphicsDevice device;
        private GeometryVertex[] vertices = new GeometryVertex[4096];
        private short[] indices = new short[6144];
        private int vertexCount;
        private int indexCount;
        private readonly List<Segment> segments = new List<Segment>();

        private Texture2D currentTexture;
        private BlendState currentBlend;
        private bool currentIsText;
        private TextParams currentTextParams;
        private int currentSegmentVertexStart;
        private int currentSegmentIndexStart;
        private bool hasOpenSegment;

        private DynamicVertexBuffer vertexBuffer;
        private DynamicIndexBuffer indexBuffer;

        public GeometryBatch(GraphicsDevice device)
        {
            this.device = device;
        }

        public bool IsEmpty => indexCount == 0;

        /// <summary>Call once at the start of each frame, before any Append* calls.</summary>
        public void BeginFrame()
        {
            vertexCount = 0;
            indexCount = 0;
            segments.Clear();
            hasOpenSegment = false;
            currentTexture = null;
            currentBlend = null;
        }

        private void EnsureCapacity(int addVertices, int addIndices)
        {
            if (vertexCount + addVertices > vertices.Length)
            {
                int newSize = vertices.Length * 2;
                while (newSize < vertexCount + addVertices)
                {
                    newSize *= 2;
                }
                Array.Resize(ref vertices, newSize);
            }

            if (indexCount + addIndices > indices.Length)
            {
                int newSize = indices.Length * 2;
                while (newSize < indexCount + addIndices)
                {
                    newSize *= 2;
                }
                Array.Resize(ref indices, newSize);
            }
        }

        private void BeginSegmentIfNeeded(Texture2D texture, BlendState blend, int addVertices)
        {
            BeginSegmentIfNeeded(texture, blend, addVertices, false, default);
        }

        private void BeginSegmentIfNeeded(Texture2D texture, BlendState blend, int addVertices, bool isText, TextParams textParams)
        {
            bool stateChanged = !hasOpenSegment || texture != currentTexture || blend != currentBlend
                || isText != currentIsText || (isText && !textParams.Equals(currentTextParams));
            bool wouldOverflow = hasOpenSegment && (vertexCount - currentSegmentVertexStart + addVertices > MaxVerticesPerSegment);

            if (stateChanged || wouldOverflow)
            {
                CloseSegment();
                currentTexture = texture;
                currentBlend = blend;
                currentIsText = isText;
                currentTextParams = textParams;
                currentSegmentVertexStart = vertexCount;
                currentSegmentIndexStart = indexCount;
                hasOpenSegment = true;
            }
        }

        private void CloseSegment()
        {
            if (hasOpenSegment && indexCount > currentSegmentIndexStart)
            {
                segments.Add(new Segment
                {
                    Texture = currentTexture,
                    Blend = currentBlend,
                    IndexStart = currentSegmentIndexStart,
                    IndexCount = indexCount - currentSegmentIndexStart,
                    IsText = currentIsText,
                    TextParams = currentTextParams,
                });
            }

            hasOpenSegment = false;
        }

        /// <summary>
        /// Maps srcRect to UV, inset by HALF A TEXEL on every edge — not
        /// the naive texel-boundary mapping. With LinearClamp filtering
        /// (used for every atlas sample, so shapes bigger than the white
        /// cell can still magnify smoothly), sampling exactly AT a texel
        /// edge lands precisely on the 50/50 tie point between that texel
        /// and its neighbor; for an atlas-packed region, the neighbor past
        /// the padding is a DIFFERENT (often fully transparent) shape, so
        /// every quad corner would blend 50% with transparent padding —
        /// and since the shader outputs PREMULTIPLIED color
        /// (rgb*alpha, alpha), a low sampled alpha drives rgb toward black
        /// too, not just toward transparent. Found exactly this way: the
        /// white 1x1 region rendered as a black/near-invisible checkerboard
        /// instead of solid color, traced to this exact edge-sampling tie.
        /// Insetting by 0.5 texel keeps every sample point strictly inside
        /// the packed pixels — for the reserved 1x1 white region this
        /// collapses u0==u1 (always the exact texel center, so ANY quad
        /// size samples pure opaque white); for larger baked shapes
        /// (Phase 4) it costs half a texel of the outermost antialiasing
        /// gradient, a standard atlas-packing tradeoff.
        /// </summary>
        private static void UVRect(Texture2D texture, Rectangle srcRect, out float u0, out float v0, out float u1, out float v1)
        {
            u0 = (srcRect.X + 0.5f) / texture.Width;
            v0 = (srcRect.Y + 0.5f) / texture.Height;
            u1 = (srcRect.X + srcRect.Width - 0.5f) / texture.Width;
            v1 = (srcRect.Y + srcRect.Height - 0.5f) / texture.Height;
        }

        private void AppendQuadIndices(int vBase)
        {
            indices[indexCount++] = (short)(vBase + 0);
            indices[indexCount++] = (short)(vBase + 1);
            indices[indexCount++] = (short)(vBase + 2);
            indices[indexCount++] = (short)(vBase + 0);
            indices[indexCount++] = (short)(vBase + 2);
            indices[indexCount++] = (short)(vBase + 3);
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

            BeginSegmentIfNeeded(texture, blend, 4);
            EnsureCapacity(4, 6);

            UVRect(texture, srcRect, out float u0, out float v0, out float u1, out float v1);

            int vBase = vertexCount;
            vertices[vertexCount++] = new GeometryVertex(new Vector2(destRect.Left, destRect.Top), color, new Vector2(u0, v0), clipRect);
            vertices[vertexCount++] = new GeometryVertex(new Vector2(destRect.Right, destRect.Top), color, new Vector2(u1, v0), clipRect);
            vertices[vertexCount++] = new GeometryVertex(new Vector2(destRect.Right, destRect.Bottom), color, new Vector2(u1, v1), clipRect);
            vertices[vertexCount++] = new GeometryVertex(new Vector2(destRect.Left, destRect.Bottom), color, new Vector2(u0, v1), clipRect);

            AppendQuadIndices(vBase);
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

            BeginSegmentIfNeeded(texture, blend, 4);
            EnsureCapacity(4, 6);

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

            int vBase = vertexCount;
            vertices[vertexCount++] = new GeometryVertex(Corner(0, 0), color, new Vector2(u0, v0), clipRect);
            vertices[vertexCount++] = new GeometryVertex(Corner(destRect.Width, 0), color, new Vector2(u1, v0), clipRect);
            vertices[vertexCount++] = new GeometryVertex(Corner(destRect.Width, destRect.Height), color, new Vector2(u1, v1), clipRect);
            vertices[vertexCount++] = new GeometryVertex(Corner(0, destRect.Height), color, new Vector2(u0, v1), clipRect);

            AppendQuadIndices(vBase);
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
        /// </summary>
        public void AppendGlyphQuad(Texture2D atlas, Vector2 destPos, Vector2 destSize, Rectangle srcRect, Color color, float smoothing, float borderWidth, Color borderColor, Vector4 clipRect)
        {
            if (destSize.X <= 0f || destSize.Y <= 0f)
            {
                return;
            }

            var textParams = new TextParams { Smoothing = smoothing, BorderWidth = borderWidth, BorderColor = borderColor };
            BeginSegmentIfNeeded(atlas, null, 4, true, textParams);
            EnsureCapacity(4, 6);

            float u0 = (float)srcRect.X / atlas.Width;
            float v0 = (float)srcRect.Y / atlas.Height;
            float u1 = (float)(srcRect.X + srcRect.Width) / atlas.Width;
            float v1 = (float)(srcRect.Y + srcRect.Height) / atlas.Height;

            int vBase = vertexCount;
            vertices[vertexCount++] = new GeometryVertex(destPos, color, new Vector2(u0, v0), clipRect);
            vertices[vertexCount++] = new GeometryVertex(new Vector2(destPos.X + destSize.X, destPos.Y), color, new Vector2(u1, v0), clipRect);
            vertices[vertexCount++] = new GeometryVertex(new Vector2(destPos.X + destSize.X, destPos.Y + destSize.Y), color, new Vector2(u1, v1), clipRect);
            vertices[vertexCount++] = new GeometryVertex(new Vector2(destPos.X, destPos.Y + destSize.Y), color, new Vector2(u0, v1), clipRect);

            AppendQuadIndices(vBase);
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

            BeginSegmentIfNeeded(texture, blend, addVertices);
            EnsureCapacity(addVertices, addIndices);

            int vBase = vertexCount;
            for (int i = 0; i < addVertices; i++)
            {
                GeometryVertex v = verts[i];
                v.ClipRect = clipRect;
                vertices[vertexCount++] = v;
            }

            for (int i = 0; i < addIndices; i++)
            {
                indices[indexCount++] = (short)(vBase + idx[i]);
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
            CloseSegment();

            if (indexCount == 0)
            {
                return;
            }

            if (vertexBuffer == null || vertexBuffer.VertexCount < vertices.Length)
            {
                vertexBuffer?.Dispose();
                vertexBuffer = new DynamicVertexBuffer(device, GeometryVertex.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            }

            if (indexBuffer == null || indexBuffer.IndexCount < indices.Length)
            {
                indexBuffer?.Dispose();
                indexBuffer = new DynamicIndexBuffer(device, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
            }

            vertexBuffer.SetData(vertices, 0, vertexCount, SetDataOptions.Discard);
            indexBuffer.SetData(indices, 0, indexCount, SetDataOptions.Discard);

            device.SetVertexBuffer(vertexBuffer);
            device.Indices = indexBuffer;
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = RasterizerState.CullNone;

            foreach (Segment segment in segments)
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
                    device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, segment.IndexStart, segment.IndexCount / 3);
                }
            }

            FrameProfiler.CountFlush();

            // Reset so the next Append* call after this mid-frame flush
            // starts a clean batch rather than accumulating on top of
            // geometry that's already been drawn.
            vertexCount = 0;
            indexCount = 0;
            segments.Clear();
            hasOpenSegment = false;
        }
    }
}
