using System;
using System.Collections.Generic;
using GustUI.Extensions;
using Microsoft.Xna.Framework;
using Xunit;

namespace GustUI.Tests
{
    /// <summary>
    /// A sequencer block's outline: straight edges, two-step corners, a notch
    /// per seam, all quads. These pin the shape by rasterising the rectangles
    /// into a pixel set, and the cost by counting them — the reason the arcs
    /// went was a ~72-vertex pinch per seam, so a notch must stay two quads.
    /// </summary>
    public class LoopOutlineGeometryTests
    {
        private static Rectangle[] Build(Rectangle r, int t, params float[] seams)
        {
            var output = new Rectangle[LoopOutlineGeometry.MaxRects(seams.Length)];
            int n = LoopOutlineGeometry.Build(r, t, seams, output);
            return output.AsSpan(0, n).ToArray();
        }

        private static HashSet<(int X, int Y)> Pixels(Rectangle[] rects)
        {
            var set = new HashSet<(int, int)>();
            foreach (Rectangle r in rects)
            {
                for (int x = r.Left; x < r.Right; x++)
                {
                    for (int y = r.Top; y < r.Bottom; y++)
                    {
                        Assert.True(set.Add((x, y)), $"pixel {x},{y} drawn twice (overdraw at a join)");
                    }
                }
            }

            return set;
        }

        [Fact]
        public void A_plain_block_is_eight_quads_and_its_corners_step_twice()
        {
            Rectangle[] rects = Build(new Rectangle(10, 20, 100, 40), 1);
            Assert.Equal(8, rects.Length);

            var px = Pixels(rects);

            // Top-left corner: the corner pixel and its two neighbours along
            // the edges are empty, the diagonal one is drawn.
            Assert.DoesNotContain((10, 20), px);
            Assert.DoesNotContain((11, 20), px);
            Assert.DoesNotContain((10, 21), px);
            Assert.Contains((11, 21), px);
            Assert.Contains((12, 20), px);
            Assert.Contains((10, 22), px);

            // Bottom-right mirrors it.
            Assert.DoesNotContain((109, 59), px);
            Assert.Contains((108, 58), px);
            Assert.Contains((107, 59), px);
            Assert.Contains((109, 57), px);

            // Nothing inside the box.
            Assert.DoesNotContain((50, 40), px);
        }

        [Fact]
        public void A_seam_is_two_short_ticks_and_nothing_across_the_middle()
        {
            Rectangle[] rects = Build(new Rectangle(0, 0, 100, 40), 1, 50f);
            Assert.Equal(10, rects.Length);

            var px = Pixels(rects);
            Assert.Contains((50, 1), px);
            Assert.Contains((50, LoopOutlineGeometry.MaxNotchLength), px);
            Assert.DoesNotContain((50, LoopOutlineGeometry.MaxNotchLength + 1), px);
            Assert.DoesNotContain((50, 20), px);
            Assert.Contains((50, 38), px);
            Assert.DoesNotContain((50, 38 - LoopOutlineGeometry.MaxNotchLength), px);
        }

        [Fact]
        public void Each_seam_costs_two_quads()
        {
            float[] seams = { 20f, 40f, 60f, 80f, 100f, 120f };
            Rectangle[] rects = Build(new Rectangle(0, 0, 140, 40), 1, seams);
            Assert.Equal(8 + (2 * seams.Length), rects.Length);
        }

        [Fact]
        public void A_seam_that_would_collide_with_a_corner_is_dropped()
        {
            Rectangle[] rects = Build(new Rectangle(0, 0, 100, 40), 1, 1f, 2f, 97f, 99f, 120f);
            Assert.Equal(8, rects.Length);
        }

        [Fact]
        public void A_short_block_gets_shorter_notches()
        {
            Rectangle[] rects = Build(new Rectangle(0, 0, 100, 10), 1, 50f);
            var px = Pixels(rects);

            // Inner height 8: a quarter of it, 2px, from each edge.
            Assert.Contains((50, 2), px);
            Assert.DoesNotContain((50, 3), px);
            Assert.Contains((50, 7), px);
            Assert.DoesNotContain((50, 6), px);
        }

        [Fact]
        public void Steps_fall_back_with_size_and_scale_with_thickness()
        {
            Assert.Equal(2, LoopOutlineGeometry.CornerSteps(12, 12, 2));
            Assert.Equal(1, LoopOutlineGeometry.CornerSteps(11, 40, 2));
            Assert.Equal(0, LoopOutlineGeometry.CornerSteps(5, 40, 2));

            // A 2px stroke steps off 2px at a time.
            var px = Pixels(Build(new Rectangle(0, 0, 60, 30), 2));
            Assert.DoesNotContain((3, 0), px);
            Assert.Contains((4, 0), px);
            Assert.Contains((2, 2), px);
            Assert.Contains((3, 3), px);
            Assert.DoesNotContain((1, 3), px);
        }

        [Fact]
        public void A_tiny_block_is_still_drawn_without_overdraw()
        {
            Assert.NotEmpty(Pixels(Build(new Rectangle(0, 0, 4, 4), 1)));
            Assert.Single(Build(new Rectangle(0, 0, 2, 30), 1));
            Assert.Empty(Build(new Rectangle(0, 0, 0, 30), 1));
        }
    }
}
