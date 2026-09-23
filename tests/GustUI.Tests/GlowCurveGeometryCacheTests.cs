using System.Collections.Generic;
using GustUI.Elements;
using Microsoft.Xna.Framework;

namespace GustUI.Tests
{
    /// <summary>
    /// GlowCurveElement builds its quads once and redraws the cached arrays
    /// until something they were built from changes. These pin both halves:
    /// that an unchanged curve is NOT rebuilt (the point of the cache), and
    /// that every input which changes the picture IS.
    /// </summary>
    public class GlowCurveGeometryCacheTests
    {
        private static readonly Vector2 Origin = new Vector2(100, 50);
        private static readonly Vector2 Size = new Vector2(200, 80);

        private static List<Vector2> Wave(float lift = 0f)
        {
            var points = new List<Vector2>();
            for (int i = 0; i <= 40; i++)
            {
                points.Add(new Vector2(i * 5f, 40f + lift + (float)System.Math.Sin(i * 0.3) * 30f));
            }

            return points;
        }

        private static GlowCurveElement Curve(bool glow = true, bool fill = true)
        {
            var curve = new GlowCurveElement { Points = Wave(), Glow = glow, ShowFill = fill };
            Assert.True(curve.EnsureGeometry(Origin, Size));
            Assert.Equal(1, curve.GeometryBuilds);
            return curve;
        }

        [Fact]
        public void An_unchanged_curve_is_built_once()
        {
            GlowCurveElement curve = Curve();
            for (int frame = 0; frame < 100; frame++)
            {
                Assert.True(curve.EnsureGeometry(Origin, Size));
            }

            Assert.Equal(1, curve.GeometryBuilds);
        }

        [Fact]
        public void A_new_list_with_the_same_points_is_not_a_rebuild()
        {
            // Callers refresh by assigning a fresh list whether or not
            // anything moved (a forced panel refresh on every knob drag).
            GlowCurveElement curve = Curve();
            curve.Points = Wave();
            curve.EnsureGeometry(Origin, Size);
            curve.Points = Wave();
            curve.EnsureGeometry(Origin, Size);
            Assert.Equal(1, curve.GeometryBuilds);
        }

        [Fact]
        public void A_new_list_with_different_points_rebuilds()
        {
            GlowCurveElement curve = Curve();
            curve.Points = Wave(lift: 3f);
            curve.EnsureGeometry(Origin, Size);
            Assert.Equal(2, curve.GeometryBuilds);
        }

        [Fact]
        public void An_in_place_edit_needs_InvalidateGeometry()
        {
            GlowCurveElement curve = Curve();
            curve.Points[3] = new Vector2(15f, 0f);
            curve.EnsureGeometry(Origin, Size);
            Assert.Equal(1, curve.GeometryBuilds); // invisible, as documented

            curve.InvalidateGeometry();
            curve.EnsureGeometry(Origin, Size);
            Assert.Equal(2, curve.GeometryBuilds);
        }

        [Fact]
        public void An_in_place_append_is_seen_by_its_count()
        {
            GlowCurveElement curve = Curve();
            curve.Points.Add(new Vector2(205f, 40f));
            curve.EnsureGeometry(Origin, Size);
            Assert.Equal(2, curve.GeometryBuilds);
        }

        [Fact]
        public void Every_styling_input_rebuilds()
        {
            GlowCurveElement curve = Curve();
            int builds = 1;

            void Changed(System.Action change)
            {
                change();
                curve.EnsureGeometry(Origin, Size);
                Assert.Equal(++builds, curve.GeometryBuilds);
                curve.EnsureGeometry(Origin, Size);
                Assert.Equal(builds, curve.GeometryBuilds);
            }

            Changed(() => curve.LineColor = Color.Red);
            Changed(() => curve.Thickness = 3);
            Changed(() => curve.ShowFill = false);
            Changed(() => curve.ShowFill = true);
            Changed(() => curve.FillColor = Color.Blue);
            Changed(() => curve.FillTopAlpha = 0.3f);
            Changed(() => curve.FillBottomAlpha = 0.1f);
            Changed(() => curve.FadeFillAcrossElement = false);
            Changed(() => curve.Baseline = 60f);
            Changed(() => curve.Glow = false);
            Changed(() => curve.GlowColor = Color.White);
            Changed(() => curve.FillColorAt = x => Color.Green);
            Changed(() => curve.LineColorAt = x => Color.Green);
        }

        [Fact]
        public void A_resize_rebuilds()
        {
            GlowCurveElement curve = Curve();
            curve.EnsureGeometry(Origin, Size + new Vector2(10, 0));
            Assert.Equal(2, curve.GeometryBuilds);
        }

        [Fact]
        public void Moving_whole_pixels_reuses_the_geometry_then_settles_once()
        {
            GlowCurveElement curve = Curve();

            // A scroll: a new whole-pixel origin every frame is drawn from the
            // cache through the shader's offset...
            for (int step = 1; step <= 20; step++)
            {
                curve.EnsureGeometry(Origin + new Vector2(0, -step), Size);
            }

            Assert.Equal(1, curve.GeometryBuilds);

            // ...and the first frame it holds still it is rebuilt where it
            // came to rest, so the resting picture is bit-exact.
            Vector2 rest = Origin + new Vector2(0, -20);
            curve.EnsureGeometry(rest, Size);
            Assert.Equal(2, curve.GeometryBuilds);

            curve.EnsureGeometry(rest, Size);
            curve.EnsureGeometry(rest, Size);
            Assert.Equal(2, curve.GeometryBuilds);
        }

        [Fact]
        public void A_fractional_move_rebuilds()
        {
            GlowCurveElement curve = Curve();
            curve.EnsureGeometry(Origin + new Vector2(0.5f, 0), Size);
            Assert.Equal(2, curve.GeometryBuilds);
        }

        [Fact]
        public void A_move_across_zero_rebuilds()
        {
            // (int) truncates toward zero, so shifting geometry built at a
            // positive origin to a negative one would land a pixel out.
            GlowCurveElement curve = Curve();
            curve.EnsureGeometry(new Vector2(-150, Origin.Y), Size);
            Assert.Equal(2, curve.GeometryBuilds);
        }

        [Fact]
        public void Glow_draws_fill_glow_and_line_as_three_passes_in_order()
        {
            Assert.Equal(3, Curve(glow: true, fill: true).CachedPassCount);
            Assert.Equal(2, Curve(glow: true, fill: false).CachedPassCount);
        }

        [Fact]
        public void Without_glow_fill_and_line_share_one_pass()
        {
            Assert.Equal(1, Curve(glow: false, fill: true).CachedPassCount);
            Assert.Equal(1, Curve(glow: false, fill: false).CachedPassCount);
        }

        [Fact]
        public void A_curve_too_big_to_cache_falls_back_to_drawing_directly()
        {
            // A fill wider than 16-bit indices can address in one pass.
            var curve = new GlowCurveElement
            {
                Points = new List<Vector2> { new Vector2(0, 10), new Vector2(9000, 10) },
                Glow = false,
            };

            Assert.False(curve.EnsureGeometry(Vector2.Zero, new Vector2(9000, 40)));
            Assert.Equal(0, curve.CachedPassCount);
        }
    }
}
