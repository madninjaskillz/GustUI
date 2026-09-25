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

            WireLanes[] lanes = OrthogonalWireRoute.AssignRoutes(wires, Stub, Radius, 6f);
            Assert.Equal(50f, lanes[0].X);
            Assert.Equal(50f, lanes[1].X);
        }

        [Fact]
        public void WiresSharingAColumnAreSpreadApartInSourceOrder()
        {
            var wires = new[]
            {
                new LaneWire { From = new Vector2(0, 100), To = new Vector2(200, 0), SourceKey = "low" },
                new LaneWire { From = new Vector2(0, 0), To = new Vector2(200, 100), SourceKey = "high" },
            };

            WireLanes[] lanes = OrthogonalWireRoute.AssignRoutes(wires, Stub, Radius, 6f);
            Assert.Equal(103f, lanes[0].X.Value, 3);
            Assert.Equal(97f, lanes[1].X.Value, 3);
        }

        [Fact]
        public void RunsThatDoNotOverlapVerticallyKeepTheirColumn()
        {
            var wires = new[]
            {
                new LaneWire { From = new Vector2(0, 0), To = new Vector2(200, 20), SourceKey = "a" },
                new LaneWire { From = new Vector2(0, 300), To = new Vector2(200, 320), SourceKey = "b" },
            };

            WireLanes[] lanes = OrthogonalWireRoute.AssignRoutes(wires, Stub, Radius, 6f);
            Assert.Equal(100f, lanes[0].X);
            Assert.Equal(100f, lanes[1].X);
        }

        [Fact]
        public void ALoneBackwardWireGetsItsDefaultRoute()
        {
            var from = new Vector2(300, 0);
            var to = new Vector2(100, 100);
            var wires = new[] { new LaneWire { From = from, To = to, SourceKey = "a" } };

            WireLanes lanes = OrthogonalWireRoute.AssignRoutes(wires, Stub, Radius, 6f)[0];
            Assert.Equal(from.X + Stub, lanes.X);
            Assert.Equal(50f, lanes.ReturnY);
            Assert.Equal(to.X - Stub, lanes.InX);
            Assert.Equal(Route(from, to), RouteWith(wires[0], lanes));
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

            WireLanes[] lanes = OrthogonalWireRoute.AssignRoutes(wires, Stub, Radius, 6f);
            Assert.Equal(200f, lanes[1].X);

            List<Vector2> back = Route(source, new Vector2(0, 200), lanes[1].X);
            Assert.Equal(200f, back[1].X);
        }

        // ------------------------------------------------ #321: pairs keep one gap

        /// <summary>The port row pitch the pairs below are spaced at, passed
        /// as the lane spacing — the whole point is that the two agree.</summary>
        private const float Pitch = 22f;

        private static List<Vector2> RouteWith(LaneWire wire, WireLanes lanes)
            => OrthogonalWireRoute.Corners(wire.From, wire.To, Stub, Radius, lanes, wire.FromSpan, wire.ToSpan);

        private static (List<Vector2> Upper, List<Vector2> Lower) RoutePair(LaneWire upper, LaneWire lower)
        {
            WireLanes[] lanes = OrthogonalWireRoute.AssignRoutes(new[] { upper, lower }, Stub, Radius, Pitch);
            return (RouteWith(upper, lanes[0]), RouteWith(lower, lanes[1]));
        }

        /// <summary>Two routes that run PARALLEL: the same turns, and every
        /// pair of matching runs exactly <paramref name="gap"/> apart — on
        /// the horizontals and the verticals alike.</summary>
        private static void AssertOneGap(List<Vector2> a, List<Vector2> b, float gap)
        {
            Assert.Equal(a.Count, b.Count);
            for (int i = 1; i < a.Count; i++)
            {
                bool vertical = Math.Abs(a[i - 1].X - a[i].X) < 0.01f;
                Assert.Equal(vertical, Math.Abs(b[i - 1].X - b[i].X) < 0.01f);
                float apart = vertical ? Math.Abs(a[i].X - b[i].X) : Math.Abs(a[i].Y - b[i].Y);
                Assert.True(Math.Abs(apart - gap) < 0.01f, $"run {i} is {apart} apart, not {gap}");
            }
        }

        /// <summary>No run of one route touches any run of the other.</summary>
        private static void AssertNoCrossing(List<Vector2> a, List<Vector2> b)
        {
            for (int i = 1; i < a.Count; i++)
            {
                for (int j = 1; j < b.Count; j++)
                {
                    Assert.False(Touch(a[i - 1], a[i], b[j - 1], b[j]),
                        $"run {a[i - 1]}->{a[i]} meets run {b[j - 1]}->{b[j]}");
                }
            }
        }

        private static bool Touch(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
        {
            const float e = 0.01f;
            return Math.Max(a0.X, a1.X) >= Math.Min(b0.X, b1.X) - e && Math.Max(b0.X, b1.X) >= Math.Min(a0.X, a1.X) - e
                && Math.Max(a0.Y, a1.Y) >= Math.Min(b0.Y, b1.Y) - e && Math.Max(b0.Y, b1.Y) >= Math.Min(a0.Y, a1.Y) - e;
        }

        [Fact]
        public void AForwardPairGoingDownKeepsOneGapAndNests()
        {
            var l = new LaneWire { From = new Vector2(0, 0), To = new Vector2(200, 100), SourceKey = "L" };
            var r = new LaneWire { From = new Vector2(0, Pitch), To = new Vector2(200, 100 + Pitch), SourceKey = "R" };
            (List<Vector2> upper, List<Vector2> lower) = RoutePair(l, r);

            AssertOneGap(upper, lower, Pitch);
            AssertNoCrossing(upper, lower);

            // Turning down, the upper wire is on the OUTSIDE: to the right.
            Assert.True(upper[1].X > lower[1].X);
        }

        [Fact]
        public void AForwardPairGoingUpKeepsOneGapAndNests()
        {
            var l = new LaneWire { From = new Vector2(0, 200), To = new Vector2(200, 50), SourceKey = "L" };
            var r = new LaneWire { From = new Vector2(0, 200 + Pitch), To = new Vector2(200, 50 + Pitch), SourceKey = "R" };
            (List<Vector2> upper, List<Vector2> lower) = RoutePair(l, r);

            AssertOneGap(upper, lower, Pitch);
            AssertNoCrossing(upper, lower);

            // Turning up, the upper wire is on the INSIDE: to the left.
            Assert.True(upper[1].X < lower[1].X);
        }

        [Fact]
        public void PairsTurningOppositeWaysInOneColumnEachStayNested()
        {
            // One pair runs down, the other up, through the same column.
            var wires = new[]
            {
                new LaneWire { From = new Vector2(0, 0), To = new Vector2(200, 300), SourceKey = "downL" },
                new LaneWire { From = new Vector2(0, Pitch), To = new Vector2(200, 300 + Pitch), SourceKey = "downR" },
                new LaneWire { From = new Vector2(0, 400), To = new Vector2(200, 100), SourceKey = "upL" },
                new LaneWire { From = new Vector2(0, 400 + Pitch), To = new Vector2(200, 100 + Pitch), SourceKey = "upR" },
            };

            WireLanes[] lanes = OrthogonalWireRoute.AssignRoutes(wires, Stub, Radius, Pitch);
            var routes = new List<Vector2>[4];
            for (int i = 0; i < 4; i++)
            {
                routes[i] = RouteWith(wires[i], lanes[i]);
            }

            AssertOneGap(routes[0], routes[1], Pitch);
            AssertNoCrossing(routes[0], routes[1]);
            AssertOneGap(routes[2], routes[3], Pitch);
            AssertNoCrossing(routes[2], routes[3]);

            // All four in one column, one pitch apart, none on another.
            var xs = new List<float>();
            foreach (WireLanes lane in lanes)
            {
                xs.Add(lane.X.Value);
            }

            xs.Sort();
            for (int i = 1; i < xs.Count; i++)
            {
                Assert.Equal(Pitch, xs[i] - xs[i - 1], 3);
            }
        }

        [Fact]
        public void ABackwardPairGoingDownKeepsOneGapWithoutCrossing()
        {
            // Split 3 High L/R into Split 4 below and to the left (the
            // owner's screenshot): a gap between the nodes to run back through.
            var fromSpan = new WireNodeSpan(0, 60);
            var toSpan = new WireNodeSpan(180, 260);
            var l = new LaneWire { From = new Vector2(300, 20), To = new Vector2(100, 200), SourceKey = "L", FromSpan = fromSpan, ToSpan = toSpan };
            var r = new LaneWire { From = new Vector2(300, 20 + Pitch), To = new Vector2(100, 200 + Pitch), SourceKey = "R", FromSpan = fromSpan, ToSpan = toSpan };
            (List<Vector2> upper, List<Vector2> lower) = RoutePair(l, r);

            Assert.Equal(6, upper.Count);
            AssertOneGap(upper, lower, Pitch);
            AssertNoCrossing(upper, lower);
            AssertArrivesHorizontally(upper, l.To);
            AssertArrivesHorizontally(lower, r.To);

            // Nested: out on the right, back along the bottom, in on the right.
            Assert.True(upper[1].X > lower[1].X);
            Assert.True(upper[2].Y > lower[2].Y);
            Assert.True(upper[3].X > lower[3].X);

            // The return runs stay in the gap, centred on it.
            Assert.Equal(120f, (upper[2].Y + lower[2].Y) * 0.5f, 3);
        }

        [Fact]
        public void ABackwardPairGoingUpKeepsOneGapWithoutCrossing()
        {
            var fromSpan = new WireNodeSpan(200, 280);
            var toSpan = new WireNodeSpan(0, 60);
            var l = new LaneWire { From = new Vector2(300, 220), To = new Vector2(100, 20), SourceKey = "L", FromSpan = fromSpan, ToSpan = toSpan };
            var r = new LaneWire { From = new Vector2(300, 220 + Pitch), To = new Vector2(100, 20 + Pitch), SourceKey = "R", FromSpan = fromSpan, ToSpan = toSpan };
            (List<Vector2> upper, List<Vector2> lower) = RoutePair(l, r);

            AssertOneGap(upper, lower, Pitch);
            AssertNoCrossing(upper, lower);
            AssertArrivesHorizontally(upper, l.To);
            AssertArrivesHorizontally(lower, r.To);
        }

        [Fact]
        public void ABackwardPairUnderOverlappingNodesKeepsOneGapWithoutCrossing()
        {
            // Side by side, so the pair loops under both, stacked downward.
            var fromSpan = new WireNodeSpan(0, 100);
            var toSpan = new WireNodeSpan(10, 120);
            var l = new LaneWire { From = new Vector2(300, 40), To = new Vector2(100, 50), SourceKey = "L", FromSpan = fromSpan, ToSpan = toSpan };
            var r = new LaneWire { From = new Vector2(300, 40 + Pitch), To = new Vector2(100, 50 + Pitch), SourceKey = "R", FromSpan = fromSpan, ToSpan = toSpan };
            (List<Vector2> upper, List<Vector2> lower) = RoutePair(l, r);

            AssertOneGap(upper, lower, Pitch);
            AssertNoCrossing(upper, lower);
            Assert.True(Math.Min(upper[2].Y, lower[2].Y) >= 120f + Stub - 0.01f, "a return run went into a node");
        }

        [Fact]
        public void AReturnChannelTooNarrowForThePitchSqueezesRatherThanHitANode()
        {
            // A 40px gap: two corners' room leaves 24px, three wires fit 12 apart.
            var fromSpan = new WireNodeSpan(0, 60);
            var toSpan = new WireNodeSpan(100, 200);
            var wires = new LaneWire[3];
            for (int i = 0; i < 3; i++)
            {
                wires[i] = new LaneWire { From = new Vector2(300, 10 + i * Pitch), To = new Vector2(100, 120 + i * Pitch), SourceKey = i, FromSpan = fromSpan, ToSpan = toSpan };
            }

            WireLanes[] lanes = OrthogonalWireRoute.AssignRoutes(wires, Stub, Radius, Pitch);
            foreach (WireLanes lane in lanes)
            {
                Assert.InRange(lane.ReturnY.Value, 60f + Radius - 0.01f, 100f - Radius + 0.01f);
            }
        }

        [Fact]
        public void ABackwardFanOutSharesOneTrunkThroughout()
        {
            // One output into a node's In L and In R, to the left: one cable
            // out, one back, branching only at the end.
            var source = new Vector2(300, 20);
            var fromSpan = new WireNodeSpan(0, 60);
            var toSpan = new WireNodeSpan(180, 260);
            var wires = new[]
            {
                new LaneWire { From = source, To = new Vector2(100, 200), SourceKey = "a", FromSpan = fromSpan, ToSpan = toSpan },
                new LaneWire { From = source, To = new Vector2(100, 200 + Pitch), SourceKey = "a", FromSpan = fromSpan, ToSpan = toSpan },
            };

            WireLanes[] lanes = OrthogonalWireRoute.AssignRoutes(wires, Stub, Radius, Pitch);
            Assert.Equal(lanes[0].X, lanes[1].X);
            Assert.Equal(lanes[0].ReturnY, lanes[1].ReturnY);
            Assert.Equal(lanes[0].InX, lanes[1].InX);
        }

        [Fact]
        public void AForwardFanOutStillSharesOneTrunkBesideAPair()
        {
            // A fan-out and a pair in one column: the fan-out's two wires
            // share a lane, and the pair's lanes sit a pitch either side.
            var fan = new Vector2(0, 50);
            var wires = new[]
            {
                new LaneWire { From = fan, To = new Vector2(200, 0), SourceKey = "fan" },
                new LaneWire { From = fan, To = new Vector2(200, 150), SourceKey = "fan" },
                new LaneWire { From = new Vector2(0, 80), To = new Vector2(200, 180), SourceKey = "L" },
                new LaneWire { From = new Vector2(0, 80 + Pitch), To = new Vector2(200, 180 + Pitch), SourceKey = "R" },
            };

            WireLanes[] lanes = OrthogonalWireRoute.AssignRoutes(wires, Stub, Radius, Pitch);
            Assert.Equal(lanes[0].X, lanes[1].X);
            Assert.Equal(Pitch, lanes[2].X.Value - lanes[3].X.Value, 3);
            Assert.NotEqual(lanes[0].X, lanes[2].X);
            Assert.NotEqual(lanes[0].X, lanes[3].X);
            AssertOneGap(RouteWith(wires[2], lanes[2]), RouteWith(wires[3], lanes[3]), Pitch);
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
