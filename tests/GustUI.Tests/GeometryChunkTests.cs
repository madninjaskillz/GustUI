using System;
using System.Collections.Generic;
using GustUI.Rendering;
using Xunit;

namespace GustUI.Tests
{
    /// <summary>
    /// GeometryBatch uploads a frame's segments in chunks (2026-10-07) so its GPU
    /// buffers stay ~3 MB instead of growing to the whole frame (20 MB, and six
    /// renamed copies of it in a D3D11 driver). These hold the chunking rule:
    /// every segment lands in exactly one chunk, in order, contiguous; a chunk
    /// never passes the caps unless one segment alone does; and a lone oversized
    /// segment still gets a chunk of its own.
    /// </summary>
    public class GeometryChunkTests
    {
        private static readonly Func<List<(int V, int I, int N)>, int, (int VertexStart, int IndexStart, int IndexCount)> Span =
            static (list, i) => (list[i].V, list[i].I, list[i].N);

        /// <summary>Segments laid end to end, as the accumulator appends them.</summary>
        private static (List<(int V, int I, int N)> Segments, int VertexCount) Layout(params (int Vertices, int Indices)[] sizes)
        {
            var list = new List<(int V, int I, int N)>();
            int v = 0, i = 0;
            foreach ((int vertices, int indices) in sizes)
            {
                list.Add((v, i, indices));
                v += vertices;
                i += indices;
            }

            return (list, v);
        }

        private static List<GeometryBatch.GeometryChunk> Chunks(List<(int V, int I, int N)> segments, int vertexCount)
        {
            var chunks = new List<GeometryBatch.GeometryChunk>();
            int at = 0;
            while (at < segments.Count)
            {
                GeometryBatch.GeometryChunk chunk = GeometryBatch.NextChunk(segments.Count, at, vertexCount, segments, Span);
                chunks.Add(chunk);
                at = chunk.End;
            }

            return chunks;
        }

        [Fact]
        public void ASmallFrameIsOneChunk()
        {
            var (segments, count) = Layout((4, 6), (100, 150), (3, 3));
            var chunk = Assert.Single(Chunks(segments, count));
            Assert.Equal(0, chunk.First);
            Assert.Equal(3, chunk.End);
            Assert.Equal(107, chunk.VertexCount);
            Assert.Equal(159, chunk.IndexCount);
        }

        [Fact]
        public void ABigFrameSplitsInOrderWithinTheCaps()
        {
            var sizes = new List<(int, int)>();
            var rng = new Random(3);
            for (int n = 0; n < 400; n++)
            {
                int vertices = rng.Next(1, 40000);
                sizes.Add((vertices, vertices * 3 / 2));
            }

            var (segments, count) = Layout(sizes.ToArray());
            List<GeometryBatch.GeometryChunk> chunks = Chunks(segments, count);

            Assert.True(chunks.Count > 1);
            int expectFirst = 0, expectVertex = 0, expectIndex = 0;
            foreach (GeometryBatch.GeometryChunk chunk in chunks)
            {
                // In order, nothing skipped or repeated, ranges contiguous.
                Assert.Equal(expectFirst, chunk.First);
                Assert.True(chunk.End > chunk.First);
                Assert.Equal(expectVertex, chunk.VertexStart);
                Assert.Equal(expectIndex, chunk.IndexStart);
                Assert.True(chunk.VertexCount <= GeometryBatch.ChunkVertices);
                Assert.True(chunk.IndexCount <= GeometryBatch.ChunkIndices);
                expectFirst = chunk.End;
                expectVertex += chunk.VertexCount;
                expectIndex += chunk.IndexCount;
            }

            Assert.Equal(segments.Count, expectFirst);
            Assert.Equal(count, expectVertex);
        }

        [Fact]
        public void AnOversizedSegmentGetsAChunkOfItsOwn()
        {
            var (segments, count) = Layout((10, 15), (65535, GeometryBatch.ChunkIndices + 9), (10, 15));
            List<GeometryBatch.GeometryChunk> chunks = Chunks(segments, count);
            Assert.Equal(3, chunks.Count);
            Assert.Equal(1, chunks[1].First);
            Assert.Equal(2, chunks[1].End);
            Assert.Equal(GeometryBatch.ChunkIndices + 9, chunks[1].IndexCount);
        }
    }
}
