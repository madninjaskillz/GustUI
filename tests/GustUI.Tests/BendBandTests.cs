using System;
using GustUI.Rendering;
using Microsoft.Xna.Framework;
using Xunit;

namespace GustUI.Tests
{
    /// <summary>
    /// BendBand is the one shape every surface draws a bend with (the piano
    /// roll and, since ezmuze #377, a sequencer clip's mini-roll). These pin
    /// the part that keeps the clip cheap enough to draw every frame: a
    /// straight stretch of a curve is ONE quad, however many samples it spans,
    /// while a real curve keeps its steps.
    /// </summary>
    public class BendBandTests
    {
        // A borderless band is three quads per drawn step (skirt, body,
        // skirt), four vertices each.
        private const int VerticesPerStep = 12;

        private static int BakedVertices(float[] offsets, float width = 400f)
        {
            var batch = new BendGeometryBatch();
            batch.BeginBake();
            BendBand.Append(batch, offsets, 60f, 0f, width, 0f, width,
                100f, -1f, 2f, Color.White, Color.Transparent, 0f, 200f);
            return batch.EndBake().VertexCount;
        }

        [Fact]
        public void AHeldNote_IsOneStep()
        {
            Assert.Equal(VerticesPerStep, BakedVertices(new float[129]));
        }

        [Fact]
        public void AStraightGlideThenAHold_IsTwoSteps()
        {
            var offsets = new float[129];
            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] = i < 64 ? i * 0.5f : 32f;
            }

            Assert.Equal(2 * VerticesPerStep, BakedVertices(offsets));
        }

        [Fact]
        public void ACurve_KeepsItsSteps()
        {
            var offsets = new float[129];
            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] = 12f * (float)Math.Sin(i * 0.1);
            }

            // 200 steps across 400px; nearly all of a sine bends enough to keep.
            Assert.True(BakedVertices(offsets) > 150 * VerticesPerStep);
        }

        [Fact]
        public void SampleOffsets_LerpsAcrossTheNote()
        {
            float[] offsets = { 0f, 10f, 0f };
            Assert.Equal(0f, BendBand.SampleOffsets(offsets, 0f));
            Assert.Equal(5f, BendBand.SampleOffsets(offsets, 0.25f));
            Assert.Equal(10f, BendBand.SampleOffsets(offsets, 0.5f));
            Assert.Equal(0f, BendBand.SampleOffsets(offsets, 1.5f));
            Assert.Equal(0f, BendBand.SampleOffsets(null, 0.5f));
        }
    }
}
