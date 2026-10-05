using System;
using GustUI.Rendering;
using Microsoft.Xna.Framework;

namespace GustUI.Tests
{
    /// <summary>
    /// StrokeGeometry is the one antialiased stroke every GustUI line goes
    /// through. These pin the parts that are easy to break without seeing it:
    /// the soft edge straddles the true edge, flat runs land on the pixel grid
    /// while diagonals do not move, and a hairline fades rather than fattens.
    /// </summary>
    public class StrokeGeometryTests
    {
        private static (GeometryVertex[] Verts, int VertexCount, int IndexCount) Stroke(
            Vector2[] points, float thickness, float scale = 1f, Color? color = null)
        {
            var verts = new GeometryVertex[StrokeGeometry.MaxVertices(points.Length)];
            var indices = new short[StrokeGeometry.MaxIndices(points.Length)];
            StrokeGeometry.Write(points, new[] { color ?? Color.White }, thickness, scale, Vector2.Zero,
                GeometryBatch.NoClip, verts, 0, indices, 0, 0, out int vc, out int ic);
            return (verts, vc, ic);
        }

        /// <summary>The four Ys of one column, sorted, rounded to 1/1000.</summary>
        private static float[] Rows(GeometryVertex[] v, int column)
        {
            var ys = new float[4];
            for (int i = 0; i < 4; i++)
            {
                ys[i] = MathF.Round(v[(column * 4) + i].Position.Y * 1000f) / 1000f;
            }

            Array.Sort(ys);
            return ys;
        }

        [Fact]
        public void Two_points_make_two_columns_and_two_soft_caps()
        {
            var (_, vc, ic) = Stroke(new[] { new Vector2(0, 0), new Vector2(10, 10) }, 2f);
            Assert.Equal(16, vc);
            Assert.Equal(3 * 18, ic);
        }

        [Fact]
        public void A_repeated_point_is_dropped_rather_than_given_no_direction()
        {
            var (_, vc, _) = Stroke(new[] { new Vector2(0, 0), new Vector2(0, 0), new Vector2(10, 3) }, 2f);
            Assert.Equal(16, vc);
        }

        [Fact]
        public void A_flat_one_pixel_line_is_centred_on_a_pixel_row()
        {
            // y = 10 is a pixel EDGE; a 1px line there would be two half rows.
            var (v, _, _) = Stroke(new[] { new Vector2(0, 10), new Vector2(20, 10) }, 1f);

            // Column 1 is the first real point (column 0 is the cap).
            Assert.Equal(new[] { 9.5f, 10.5f, 10.5f, 11.5f }, Rows(v, 1));
        }

        [Fact]
        public void A_flat_two_pixel_line_has_its_edges_on_pixel_edges()
        {
            var (v, _, _) = Stroke(new[] { new Vector2(0, 10.4f), new Vector2(20, 10.4f) }, 2f);

            // Centre snapped to 10, so the edges sit at 9 and 11: opaque from
            // 9.5 to 10.5, transparent at 8.5 and 11.5.
            Assert.Equal(new[] { 8.5f, 9.5f, 10.5f, 11.5f }, Rows(v, 1));
        }

        [Fact]
        public void A_diagonal_is_left_where_it_is()
        {
            var (v, _, _) = Stroke(new[] { new Vector2(0.3f, 0.3f), new Vector2(10.3f, 10.3f) }, 2f);
            Vector2 centre = (new Vector2(v[5].Position.X, v[5].Position.Y) + new Vector2(v[6].Position.X, v[6].Position.Y)) / 2f;
            Assert.Equal(0.3f, centre.X, 3);
            Assert.Equal(0.3f, centre.Y, 3);
        }

        [Fact]
        public void The_soft_band_straddles_the_true_edge_at_any_scale()
        {
            // At 200% one physical pixel is half a logical one.
            var (v, _, _) = Stroke(new[] { new Vector2(0, 0.3f), new Vector2(20, 0.3f) }, 3f, scale: 2f);
            float centre = (v[5].Position.Y + v[6].Position.Y) / 2f;
            float inner = Math.Abs(v[5].Position.Y - centre);
            float outer = Math.Abs(v[4].Position.Y - centre);
            Assert.Equal(1.5f - 0.25f, inner, 3);
            Assert.Equal(1.5f + 0.25f, outer, 3);
        }

        [Fact]
        public void A_hairline_fades_instead_of_fattening()
        {
            var (v, _, _) = Stroke(new[] { new Vector2(0, 0), new Vector2(7, 9) }, 0.5f);
            Assert.InRange(v[5].Color.A, 120, 135);
            Assert.Equal(0, v[4].Color.A);
        }

        [Fact]
        public void Ends_are_soft()
        {
            var (v, vc, _) = Stroke(new[] { new Vector2(0, 0), new Vector2(10, 3) }, 2f);
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(0, v[i].Color.A);
                Assert.Equal(0, v[vc - 1 - i].Color.A);
            }
        }
    }
}
