using System;
using System.Collections.Generic;
using GustUI.Elements;
using Microsoft.Xna.Framework;

namespace GustUI.Tests
{
    /// <summary>
    /// The straight-wire router (ezmuze studio #311): forward wires turn
    /// twice, backward wires loop round, every wire arrives on a straight run
    /// at least a stub long, corners fit their runs, and lanes spread wires
    /// that would share a column while a fan-out shares one trunk.
    /// </summary>
    public class OrthogonalWireRouteTests
    {
        private const float Stub = 24f;
        private const float Radius = 8f;

        private static List<Vector2> Route(Vector2 from, Vector2 to, float? lane = null,
            WireNodeSpan? fromSpan = null, WireNodeSpan? toSpan = null)
            => OrthogonalWireRoute.Corners(from, to, Stub, Radius, lane, fromSpan, toSpan);

        private static void AssertAxisAligned(List<Vector2> points)
        {
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 a = points[i - 1];
                Vector2 b = points[i];
                Assert.True(Math.Abs(a.X - b.X) < 0.01f || Math.Abs(a.Y - b.Y) < 0.01f,
                    $"segment {i} ({a} -> {b}) is diagonal");
            }
        }

        private static void AssertArrivesHorizontally(List<Vector2> points, Vector2 to)
        {
            Vector2 last = points[points.Count - 1];
            Vector2 before = points[points.Count - 2];
            Assert.Equal(to, last);
            Assert.Equal(to.Y, before.Y, 3);
            Assert.True(to.X - before.X >= Stub - 0.01f, $"final run {to.X - before.X} is shorter than a stub");
        }

        [Fact]
        public void LevelPortsAreOneStraightLine()
        {
            List<Vector2> points = Route(new Vector2(0, 50), new Vector2(200, 50));
            Assert.Equal(new[] { new Vector2(0, 50), new Vector2(200, 50) }, points);
        }

        [Fact]
        public void ForwardWireTurnsTwiceHalfwayAcross()
        {
            List<Vector2> points = Route(new Vector2(0, 0), new Vector2(200, 100));
            Assert.Equal(new[]
            {
                new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100), new Vector2(200, 100),
            }, points);
        }

        [Fact]
        public void ForwardWireUsesItsLane()
        {
            List<Vector2> points = Route(new Vector2(0, 0), new Vector2(200, 100), lane: 60);
            Assert.Equal(60f, points[1].X);
            Assert.Equal(60f, points[2].X);
        }

        [Fact]
        public void ALaneNeverEatsTheInputStub()
        {
            List<Vector2> points = Route(new Vector2(0, 0), new Vector2(200, 100), lane: 199);
            AssertArrivesHorizontally(points, new Vector2(200, 100));
        }

        [Fact]
        public void BackwardWireLoopsThroughTheGapBetweenItsNodes()
        {
            // Source node spans 0..40 (port at 20), target node spans 100..140
            // (port at 120), target to the LEFT.
            var from = new Vector2(300, 20);
            var to = new Vector2(100, 120);
            List<Vector2> points = Route(from, to, fromSpan: new WireNodeSpan(0, 40), toSpan: new WireNodeSpan(100, 140));

            Assert.Equal(6, points.Count);
            AssertAxisAligned(points);
            AssertArrivesHorizontally(points, to);
            Assert.Equal(from.X + Stub, points[1].X);
            Assert.Equal(70f, points[2].Y); // middle of the 40..100 gap
            Assert.Equal(to.X - Stub, points[3].X);
        }

        [Fact]
        public void BackwardWireGoesUnderNodesThatOverlap()
        {
            var from = new Vector2(300, 20);
            var to = new Vector2(100, 30);
            List<Vector2> points = Route(from, to, fromSpan: new WireNodeSpan(0, 60), toSpan: new WireNodeSpan(10, 90));

            AssertAxisAligned(points);
            AssertArrivesHorizontally(points, to);
            Assert.Equal(90f + Stub, points[2].Y);
            Assert.Equal(90f + Stub, points[3].Y);
        }

        [Fact]
        public void AComponentWiredToItselfLoopsUnderItself()
        {
            // Output on the node's right edge, input on its left edge.
            var span = new WireNodeSpan(0, 80);
            var from = new Vector2(150, 30);
            var to = new Vector2(50, 50);
            List<Vector2> points = Route(from, to, fromSpan: span, toSpan: span);

            AssertAxisAligned(points);
            AssertArrivesHorizontally(points, to);
            Assert.All(points.GetRange(1, points.Count - 2), p => Assert.False(
                p.X > 50 && p.X < 150 && p.Y > 0 && p.Y < 80, $"{p} is inside the node"));
        }

        [Fact]
        public void TooCloseToTurnIsRoutedBackward()
        {
            List<Vector2> points = Route(new Vector2(100, 0), new Vector2(110, 100));
            Assert.Equal(6, points.Count);
            AssertArrivesHorizontally(points, new Vector2(110, 100));
        }

        [Fact]
        public void RoundedRouteStaysOnTheCornersAndKeepsItsEnds()
        {
            List<Vector2> corners = Route(new Vector2(0, 0), new Vector2(200, 100));
            var rounded = new List<Vector2>();
            OrthogonalWireRoute.Round(corners, Radius, rounded);

            Assert.Equal(corners[0], rounded[0]);
            Assert.Equal(corners[corners.Count - 1], rounded[rounded.Count - 1]);
            Assert.True(rounded.Count > corners.Count);

            // Every arc point is within the radius of its sharp corner.
            foreach (Vector2 p in rounded)
            {
                float nearest = float.MaxValue;
                for (int i = 0; i < corners.Count - 1; i++)
                {
                    nearest = Math.Min(nearest, DistanceToSegment(p, corners[i], corners[i + 1]));
                }

                Assert.True(nearest <= Radius * 0.35f, $"{p} strays {nearest} from the route");
            }
        }

        [Fact]
        public void CornersShrinkToFitAShortStep()
        {
            // A 6px step: two corners on a 6px vertical run get 3px each.
            List<Vector2> corners = Route(new Vector2(0, 0), new Vector2(200, 6));
            var rounded = new List<Vector2>();
            OrthogonalWireRoute.Round(corners, Radius, rounded);

            foreach (Vector2 p in rounded)
            {
                Assert.InRange(p.Y, -0.01f, 6.01f);
            }

            // The first arc starts 3px before the first corner, not 8.
            Assert.Contains(rounded, p => Math.Abs(p.X - 97f) < 0.01f && Math.Abs(p.Y) < 0.01f);
        }

        [Fact]
        public void AFanOutSharesOneTrunkHalfwayToItsNearestTarget()
        {
            var source = new Vector2(0, 50);
            var wires = new[]
            {
                new LaneWire { From = source, To = new Vector2(100, 0), SourceKey = "a" },
                new LaneWire { From = source, To = new Vector2(300, 200), SourceKey = "a" },
            };

            float?[] lanes = OrthogonalWireRoute.AssignLanes(wires, Stub, Radius, 6f);
            Assert.Equal(50f, lanes[0]);
            Assert.Equal(50f, lanes[1]);
        }

        [Fact]
        public void WiresSharingAColumnAreSpreadApartInSourceOrder()
        {
            var wires = new[]
            {
                new LaneWire { From = new Vector2(0, 100), To = new Vector2(200, 0), SourceKey = "low" },
                new LaneWire { From = new Vector2(0, 0), To = new Vector2(200, 100), SourceKey = "high" },
            };

            float?[] lanes = OrthogonalWireRoute.AssignLanes(wires, Stub, Radius, 6f);
            Assert.Equal(103f, lanes[0].Value, 3);
            Assert.Equal(97f, lanes[1].Value, 3);
        }

        [Fact]
        public void RunsThatDoNotOverlapVerticallyKeepTheirColumn()
        {
            var wires = new[]
            {
                new LaneWire { From = new Vector2(0, 0), To = new Vector2(200, 20), SourceKey = "a" },
                new LaneWire { From = new Vector2(0, 300), To = new Vector2(200, 320), SourceKey = "b" },
            };

            float?[] lanes = OrthogonalWireRoute.AssignLanes(wires, Stub, Radius, 6f);
            Assert.Equal(100f, lanes[0]);
            Assert.Equal(100f, lanes[1]);
        }

        [Fact]
        public void ABackwardOnlySourceGetsNoLane()
        {
            var wires = new[]
            {
                new LaneWire { From = new Vector2(300, 0), To = new Vector2(100, 100), SourceKey = "a" },
            };

            Assert.Null(OrthogonalWireRoute.AssignLanes(wires, Stub, Radius, 6f)[0]);
        }

        [Fact]
        public void ABackwardWireJoinsItsSourcesTrunk()
        {
            var source = new Vector2(100, 50);
            var wires = new[]
            {
                new LaneWire { From = source, To = new Vector2(300, 0), SourceKey = "a" },
                new LaneWire { From = source, To = new Vector2(0, 200), SourceKey = "a" },
            };

            float?[] lanes = OrthogonalWireRoute.AssignLanes(wires, Stub, Radius, 6f);
            Assert.Equal(200f, lanes[1]);

            List<Vector2> back = Route(source, new Vector2(0, 200), lanes[1]);
            Assert.Equal(200f, back[1].X);
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
