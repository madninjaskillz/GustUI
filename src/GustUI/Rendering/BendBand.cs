using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GustUI.Rendering
{
    /// <summary>
    /// Accumulates bent-note geometry for one element's frame and draws it in
    /// ONE <see cref="Managers.DrawManager.DrawTriangles(VertexPositionColor[], int, short[], int)"/>
    /// call (more only when a frame outgrows a short index — see
    /// <see cref="MaxVertices"/>). Shared by <see cref="Elements.PianoRollElement"/>
    /// and <see cref="Elements.MiniPianoRollElement"/>, so the editor and the
    /// sequencer clip draw a bend with the same geometry (ezmuze #377).
    ///
    /// The buffers are reused frame to frame, so a steady frame allocates
    /// nothing; they grow once to the largest frame seen.
    /// </summary>
    public sealed class BendGeometryBatch
    {
        /// <summary>
        /// Vertex ceiling for ONE flush. Indices are shorts, so a batch cannot
        /// address more than 32,768 vertices — past that the base index wraps
        /// and every quad after it indexes back into earlier geometry, which
        /// silently swallows whole notes (the piano roll's 2026-08-23 bug). A
        /// multiple of 4 so a flush never splits a quad.
        /// </summary>
        public const int MaxVertices = 32764;

        private VertexPositionColor[] vertices = new VertexPositionColor[1024];
        private short[] indices = new short[1536];
        private int vertexCount;
        private int indexCount;
        private Managers.DrawManager manager;

        /// <summary>Non-null while baking (<see cref="BeginBake"/>): flushes
        /// land here as reusable chunks instead of on screen.</summary>
        private List<BendGeometry.Chunk> baked;

        /// <summary>Vertices waiting for the next flush.</summary>
        public int PendingVertices => vertexCount;

        /// <summary>Starts a frame's batch against <paramref name="drawManager"/>
        /// — the overflow flush needs it as well as the final one. Discards
        /// anything a previous frame left unflushed.</summary>
        public void Begin(Managers.DrawManager drawManager)
        {
            manager = drawManager;
            baked = null;
            vertexCount = 0;
            indexCount = 0;
        }

        /// <summary>
        /// Starts recording instead of drawing: everything appended until
        /// <see cref="EndBake"/> becomes a <see cref="BendGeometry"/> that
        /// can be drawn again, anywhere, for the cost of a copy. Build it in
        /// LOCAL coordinates (origin 0,0) and give the offset at draw time.
        /// </summary>
        public void BeginBake()
        {
            manager = null;
            baked = new List<BendGeometry.Chunk>();
            vertexCount = 0;
            indexCount = 0;
        }

        /// <summary>Finishes a <see cref="BeginBake"/> recording.</summary>
        public BendGeometry EndBake()
        {
            Flush();
            var geometry = new BendGeometry(baked ?? new List<BendGeometry.Chunk>());
            baked = null;
            return geometry;
        }

        /// <summary>One quad between a top edge (x1,y1t)→(x2,y2t) and a bottom
        /// edge (x1,y1b)→(x2,y2b), with separate top/bottom colors (equal for
        /// solid bands, one transparent for a feathered skirt). Y is clamped to
        /// [yMin, yMax] — the surfaces clip manually, with no scissor.</summary>
        public void AddQuad(float x1, float y1t, float x2, float y2t, float y1b, float y2b,
            Color cTop, Color cBot, float yMin, float yMax)
        {
            Reserve(4, 6);
            int b = vertexCount;
            vertices[b] = new VertexPositionColor(new Vector3(x1, Math.Clamp(y1t, yMin, yMax), 0f), cTop);
            vertices[b + 1] = new VertexPositionColor(new Vector3(x2, Math.Clamp(y2t, yMin, yMax), 0f), cTop);
            vertices[b + 2] = new VertexPositionColor(new Vector3(x1, Math.Clamp(y1b, yMin, yMax), 0f), cBot);
            vertices[b + 3] = new VertexPositionColor(new Vector3(x2, Math.Clamp(y2b, yMin, yMax), 0f), cBot);
            AddIndices(b, 0, 1, 2, 1, 3, 2);
        }

        /// <summary>An axis-rotated diamond (the actual diamond shape, not a
        /// square posing as one) centred on (cx, cy).</summary>
        public void AddDiamond(float cx, float cy, float r, Color c)
        {
            Reserve(4, 6);
            int b = vertexCount;
            vertices[b] = new VertexPositionColor(new Vector3(cx, cy - r, 0f), c);
            vertices[b + 1] = new VertexPositionColor(new Vector3(cx + r, cy, 0f), c);
            vertices[b + 2] = new VertexPositionColor(new Vector3(cx, cy + r, 0f), c);
            vertices[b + 3] = new VertexPositionColor(new Vector3(cx - r, cy, 0f), c);
            AddIndices(b, 0, 1, 3, 1, 2, 3);
        }

        /// <summary>Draws everything accumulated, then empties the batch.</summary>
        public void Flush()
        {
            if (indexCount > 0 && baked != null)
            {
                var chunkVertices = new GeometryVertex[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    Vector3 p = vertices[i].Position;
                    // UV is a placeholder and ClipRect MUST be NoClip: the
                    // cached path takes both from uniforms, and the shader
                    // intersects the per-vertex rect with the real one, so a
                    // zero rect here would clip everything away
                    // (WaveformData.GetGeometryVertices has the story).
                    chunkVertices[i] = new GeometryVertex(
                        new Vector2(p.X, p.Y), vertices[i].Color, Vector2.Zero, GeometryBatch.NoClip);
                }

                var chunkIndices = new short[indexCount];
                Array.Copy(indices, chunkIndices, indexCount);
                baked.Add(new BendGeometry.Chunk(chunkVertices, chunkIndices));
            }
            else if (indexCount > 0 && manager != null)
            {
                manager.DrawTriangles(vertices, vertexCount, indices, indexCount / 3);
            }

            vertexCount = 0;
            indexCount = 0;
        }

        private void Reserve(int moreVertices, int moreIndices)
        {
            if (vertexCount + moreVertices > MaxVertices)
            {
                Flush();
            }

            if (vertexCount + moreVertices > vertices.Length)
            {
                Array.Resize(ref vertices, Math.Min(MaxVertices, vertices.Length * 2));
            }

            if (indexCount + moreIndices > indices.Length)
            {
                Array.Resize(ref indices, indices.Length * 2);
            }
        }

        private void AddIndices(int b, int i0, int i1, int i2, int i3, int i4, int i5)
        {
            indices[indexCount] = (short)(b + i0);
            indices[indexCount + 1] = (short)(b + i1);
            indices[indexCount + 2] = (short)(b + i2);
            indices[indexCount + 3] = (short)(b + i3);
            indices[indexCount + 4] = (short)(b + i4);
            indices[indexCount + 5] = (short)(b + i5);
            indexCount += 6;
            vertexCount += 4;
        }
    }

    /// <summary>
    /// Bend geometry recorded once (<see cref="BendGeometryBatch.BeginBake"/>)
    /// and drawn every frame after for the cost of a copy — the
    /// <see cref="Managers.DrawManager.DrawCachedTriangles"/> path the
    /// waveform faces use. What a sequencer clip needs: its bends change
    /// only when the song or the zoom does, and it is drawn every frame.
    /// </summary>
    public sealed class BendGeometry
    {
        internal readonly struct Chunk
        {
            public readonly GeometryVertex[] Vertices;
            public readonly short[] Indices;

            public Chunk(GeometryVertex[] vertices, short[] indices)
            {
                Vertices = vertices;
                Indices = indices;
            }
        }

        private readonly List<Chunk> chunks;

        internal BendGeometry(List<Chunk> chunks)
        {
            this.chunks = chunks;
        }

        /// <summary>Vertices recorded, across every chunk.</summary>
        public int VertexCount
        {
            get
            {
                int n = 0;
                foreach (Chunk c in chunks)
                {
                    n += c.Vertices.Length;
                }

                return n;
            }
        }

        /// <summary>Draws the recording with its origin at <paramref name="offset"/>.</summary>
        public void Draw(Managers.DrawManager manager, Vector2 offset)
        {
            foreach (Chunk c in chunks)
            {
                manager.DrawCachedTriangles(c.Vertices, c.Indices, c.Indices.Length / 3, offset, Color.White);
            }
        }
    }

    /// <summary>
    /// A bent note's body: a band of constant thickness that follows
    /// <c>pitch + offset(t)</c> across the note, as float-precision quads
    /// (no integer column snapping) with 1px alpha-feathered skirts along
    /// both edges — geometric antialiasing, since the render targets run
    /// without MSAA and a hard 1px edge on a slope would staircase.
    ///
    /// ONE shape for every surface that draws a bend (ezmuze #377): the piano
    /// roll draws it with a border and end caps at row height, the sequencer
    /// clip's mini-roll draws it borderless at its own note height. Anything
    /// that changes how a bend LOOKS changes here, for both.
    /// </summary>
    public static class BendBand
    {
        /// <summary>How far (px) the curve may leave the line its current
        /// quad started on before a step is kept as a vertex of the drawn
        /// polyline.</summary>
        public const float SlopeTolerance = 0.02f;

        // Scratch for one note's steps. Draw runs on the game thread only.
        [ThreadStatic] private static float[] xs;
        [ThreadStatic] private static float[] tops;

        /// <summary>Linear interpolation over host-sampled bend offsets, which
        /// are spaced uniformly across the note; <paramref name="t"/> is the
        /// note-relative position 0..1.</summary>
        public static float SampleOffsets(float[] offsets, float t)
        {
            if (offsets == null || offsets.Length == 0)
            {
                return 0f;
            }

            if (offsets.Length == 1)
            {
                return offsets[0];
            }

            float f = MathHelper.Clamp(t, 0f, 1f) * (offsets.Length - 1);
            int i = (int)f;
            if (i >= offsets.Length - 1)
            {
                return offsets[offsets.Length - 1];
            }

            return offsets[i] + (offsets[i + 1] - offsets[i]) * (f - i);
        }

        /// <summary>
        /// Appends one bent note to <paramref name="batch"/>.
        /// </summary>
        /// <param name="offsets">The bend, sampled uniformly across the note (semitones).</param>
        /// <param name="pitch">The note's base pitch.</param>
        /// <param name="leftF">The note's TRUE left edge, x — may be off the surface.</param>
        /// <param name="rightF">The note's TRUE right edge, x.</param>
        /// <param name="xStart">Where drawing starts: the left edge clipped to the surface.</param>
        /// <param name="xEnd">Where drawing ends: the right edge clipped to the surface.</param>
        /// <param name="topAtPitchZero">The band's TOP edge, y, for a sounding pitch of 0.</param>
        /// <param name="topPerSemitone">How far the top edge moves per semitone (negative: up is higher).</param>
        /// <param name="thickness">The band's height in px, borders included.</param>
        /// <param name="body">Fill colour.</param>
        /// <param name="border">1px border colour on both edges and the caps; transparent draws none.</param>
        /// <param name="yMin">Top of the surface (geometry is clamped to it).</param>
        /// <param name="yMax">Bottom of the surface.</param>
        /// <param name="maxSamples">Ceiling on steps across the note (one step per ~2px below it).</param>
        public static void Append(BendGeometryBatch batch, float[] offsets, float pitch,
            float leftF, float rightF, float xStart, float xEnd,
            float topAtPitchZero, float topPerSemitone, float thickness,
            Color body, Color border, float yMin, float yMax, int maxSamples = 512)
        {
            float span = rightF - leftF;
            if (xEnd - xStart < 1f || span <= 0f)
            {
                return;
            }

            xs ??= new float[64];
            tops ??= new float[64];

            bool bordered = border.A > 0;
            Color edge = bordered ? border : body;
            Color fade = edge * 0f; // premultiplied: zero = fully transparent
            int samples = Math.Clamp((int)((xEnd - xStart) / 2f), 1, Math.Max(1, maxSamples));

            // The curve at every step, then only the steps where it BENDS.
            // A bend is mostly held notes and straight glides, and a run of
            // equal slopes is one quad, not one per step: the "Too many
            // chords" clip went from ~14,000 quads a frame to a few hundred.
            // A step is dropped only while the curve stays within
            // SlopeTolerance px of the straight line the current quad started
            // on, so the drawn polyline never strays more than twice that
            // (a twenty-fifth of a pixel) from the sampled one — however long
            // a gentle curve runs, the error cannot accumulate.
            if (xs.Length < samples + 1)
            {
                xs = new float[samples + 1];
                tops = new float[samples + 1];
            }

            for (int i = 0; i <= samples; i++)
            {
                float x = i == 0 ? xStart : xStart + (xEnd - xStart) * i / samples;
                xs[i] = x;
                tops[i] = topAtPitchZero + topPerSemitone * (pitch + SampleOffsets(offsets, (x - leftF) / span));
            }

            float prevX = xs[0];
            float prevTop = tops[0];
            float firstTop = prevTop;
            int runStart = 0;
            float runSlope = tops[1] - tops[0];
            for (int i = 1; i <= samples; i++)
            {
                if (i < samples)
                {
                    float onLine = tops[runStart] + runSlope * (i + 1 - runStart);
                    if (Math.Abs(tops[i + 1] - onLine) <= SlopeTolerance)
                    {
                        continue; // still on this quad's line: extend it
                    }
                }

                float x = xs[i];
                float top = tops[i];
                float b1 = prevTop + thickness;
                float b2 = top + thickness;
                batch.AddQuad(prevX, prevTop - 1f, x, top - 1f, prevTop, top, fade, edge, yMin, yMax);
                if (bordered)
                {
                    batch.AddQuad(prevX, prevTop, x, top, prevTop + 1f, top + 1f, border, border, yMin, yMax);
                    batch.AddQuad(prevX, prevTop + 1f, x, top + 1f, b1 - 1f, b2 - 1f, body, body, yMin, yMax);
                    batch.AddQuad(prevX, b1 - 1f, x, b2 - 1f, b1, b2, border, border, yMin, yMax);
                }
                else
                {
                    batch.AddQuad(prevX, prevTop, x, top, b1, b2, body, body, yMin, yMax);
                }

                batch.AddQuad(prevX, b1, x, b2, b1 + 1f, b2 + 1f, edge, fade, yMin, yMax);

                prevX = x;
                prevTop = top;
                runStart = i;
                if (i < samples)
                {
                    runSlope = tops[i + 1] - tops[i];
                }
            }

            if (!bordered)
            {
                return;
            }

            // End caps, only at TRUE note ends — a clipped edge gets none.
            if (leftF >= xStart)
            {
                batch.AddQuad(leftF, firstTop, leftF + 1f, firstTop, firstTop + thickness, firstTop + thickness,
                    border, border, yMin, yMax);
            }

            if (rightF <= xEnd)
            {
                batch.AddQuad(rightF - 1f, prevTop, rightF, prevTop, prevTop + thickness, prevTop + thickness,
                    border, border, yMin, yMax);
            }
        }
    }
}
