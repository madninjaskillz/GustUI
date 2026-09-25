using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace GustUI.Elements
{
    /// <summary>
    /// The vertical extent of the node a wire leaves or enters, in the same
    /// space as the wire's endpoints. Only a BACKWARD wire uses it: to know
    /// whether there is a gap between the two nodes to run back through, or
    /// whether it has to go underneath both.
    /// </summary>
    public struct WireNodeSpan
    {
        public float Top;
        public float Bottom;

        public WireNodeSpan(float top, float bottom)
        {
            Top = Math.Min(top, bottom);
            Bottom = Math.Max(top, bottom);
        }
    }

    /// <summary>One wire as <see cref="OrthogonalWireRoute.AssignRoutes"/>
    /// sees it: its two ends, which output it comes from, and the nodes at
    /// each end. Wires with equal <see cref="SourceKey"/>s share a trunk.</summary>
    public struct LaneWire
    {
        public Vector2 From;
        public Vector2 To;
        public object SourceKey;

        /// <summary>The nodes' vertical extents, as the router's Corners
        /// takes them — a backward wire's return channel depends on them.</summary>
        public WireNodeSpan? FromSpan;

        /// <inheritdoc cref="FromSpan"/>
        public WireNodeSpan? ToSpan;
    }

    /// <summary>
    /// The runs of one wire's route that <see cref="OrthogonalWireRoute.AssignRoutes"/>
    /// chose, each null for "the default". A forward wire only has
    /// <see cref="X"/>; a backward wire uses all three.
    /// </summary>
    public struct WireLanes
    {
        /// <summary>The X of a forward wire's vertical run, or of a backward
        /// wire's FIRST vertical (out of the output).</summary>
        public float? X;

        /// <summary>A backward wire's return run: the Y it runs back left
        /// along.</summary>
        public float? ReturnY;

        /// <summary>A backward wire's LAST vertical: the X it comes down (or
        /// up) to its input at.</summary>
        public float? InX;
    }

    /// <summary>
    /// Straight wires with rounded corners — the "metro map" alternative to
    /// <see cref="NodeWireLayerElement"/>'s Béziers (ezmuze studio #311).
    ///
    /// Pure geometry, no drawing, so every rule below is unit-tested:
    ///
    /// A FORWARD wire (input to the right of the output, with room for the
    /// input's stub) leaves horizontally, turns once to run vertically, and
    /// turns again into the input: from → (x, from.Y) → (x, to.Y) → to. The
    /// vertical run sits halfway across the gap unless a lane says otherwise.
    ///
    /// A BACKWARD wire (input to the LEFT, or too close to turn) cannot do
    /// that without doubling back through its own nodes, so it steps out to
    /// the right, runs vertically to a horizontal return channel, runs back
    /// left past the input, and comes in from the left like every other wire.
    /// The return channel is the middle of the gap between the two nodes when
    /// there is one, and UNDER both of them when they overlap vertically (a
    /// component wired to itself always does).
    ///
    /// Both kinds always ARRIVE on a horizontal run at least one stub long,
    /// which is what keeps the scaler affordance
    /// (<see cref="NodeWireLayerElement.ScalerAnchor"/>, 16px before the
    /// input) sitting on the wire rather than on a corner.
    /// </summary>
    public static class OrthogonalWireRoute
    {
        /// <summary>
        /// The route's corner points, unrounded: an axis-aligned polyline
        /// from <paramref name="from"/> (an output) to <paramref name="to"/>
        /// (an input). Consecutive duplicates and straight-through points are
        /// dropped, so every interior point is a real turn.
        /// </summary>
        /// <param name="stub">The shortest straight run out of an output and
        /// into an input.</param>
        /// <param name="radius">The corner radius — a forward wire needs this
        /// much room after the output before it can turn.</param>
        /// <param name="laneX">Where the vertical run goes (a forward wire)
        /// or where the wire first turns (a backward one), from
        /// <see cref="AssignRoutes"/>. Null means the default.</param>
        public static List<Vector2> Corners(Vector2 from, Vector2 to, float stub, float radius,
            float? laneX = null, WireNodeSpan? fromSpan = null, WireNodeSpan? toSpan = null)
        {
            var points = new List<Vector2>(6);
            Corners(points, from, to, stub, radius, new WireLanes { X = laneX }, fromSpan, toSpan);
            return points;
        }

        /// <summary>As <see cref="Corners(Vector2, Vector2, float, float, float?, WireNodeSpan?, WireNodeSpan?)"/>,
        /// with every lane <see cref="AssignRoutes"/> picked.</summary>
        public static List<Vector2> Corners(Vector2 from, Vector2 to, float stub, float radius,
            WireLanes lanes, WireNodeSpan? fromSpan = null, WireNodeSpan? toSpan = null)
        {
            var points = new List<Vector2>(6);
            Corners(points, from, to, stub, radius, lanes, fromSpan, toSpan);
            return points;
        }

        /// <summary>As <see cref="Corners(Vector2, Vector2, float, float, WireLanes, WireNodeSpan?, WireNodeSpan?)"/>,
        /// into a caller's list (cleared first) — the per-frame path.</summary>
        public static void Corners(List<Vector2> points, Vector2 from, Vector2 to, float stub, float radius,
            WireLanes lanes, WireNodeSpan? fromSpan = null, WireNodeSpan? toSpan = null)
        {
            points.Clear();
            points.Add(from);

            if (IsForward(from, to, stub, radius))
            {
                float x = ClampForward(lanes.X ?? (from.X + to.X) * 0.5f, from, to, stub, radius);
                Add(points, new Vector2(x, from.Y));
                Add(points, new Vector2(x, to.Y));
                Add(points, to);
                return;
            }

            float outX = OutX(from, stub, radius, lanes.X);
            float inX = InX(to, stub, lanes.InX);
            float y = lanes.ReturnY ?? ReturnChannel(from, to, stub, radius, fromSpan, toSpan, out _, out _, out _);

            Add(points, new Vector2(outX, from.Y));
            Add(points, new Vector2(outX, y));
            Add(points, new Vector2(inX, y));
            Add(points, new Vector2(inX, to.Y));
            Add(points, to);
        }

        /// <summary>Whether a wire can go straight across (one vertical run)
        /// rather than having to loop back: there has to be room for a corner
        /// after the output and a whole stub before the input.</summary>
        public static bool IsForward(Vector2 from, Vector2 to, float stub, float radius)
            => to.X - from.X >= stub + radius;

        /// <summary>A forward wire's vertical run, held where it still leaves
        /// a corner's room after the output and a stub before the input.</summary>
        private static float ClampForward(float x, Vector2 from, Vector2 to, float stub, float radius)
        {
            float min = from.X + radius;
            float max = to.X - stub;
            return max < min ? min : Math.Clamp(x, min, max);
        }

        /// <summary>
        /// The height a backward wire runs back along: through the middle of
        /// the gap between the two nodes if there is one, otherwise a stub
        /// below the lower of the two. Without node spans the ports stand in
        /// for the nodes. <paramref name="min"/>..<paramref name="max"/> is
        /// where lanes may spread to (a corner's room inside a gap), and
        /// <paramref name="under"/> says the channel is below both nodes, so
        /// lanes stack downward from it rather than around it.
        /// </summary>
        private static float ReturnChannel(Vector2 from, Vector2 to, float stub, float radius,
            WireNodeSpan? fromSpan, WireNodeSpan? toSpan, out float min, out float max, out bool under)
        {
            WireNodeSpan a = fromSpan ?? new WireNodeSpan(from.Y, from.Y);
            WireNodeSpan b = toSpan ?? new WireNodeSpan(to.Y, to.Y);
            under = false;

            // A gap has to fit two corners to be worth threading.
            float minGap = radius * 2f;
            if (b.Top - a.Bottom >= minGap)
            {
                min = a.Bottom + radius;
                max = b.Top - radius;
                return (a.Bottom + b.Top) * 0.5f;
            }

            if (a.Top - b.Bottom >= minGap)
            {
                min = b.Bottom + radius;
                max = a.Top - radius;
                return (b.Bottom + a.Top) * 0.5f;
            }

            under = true;
            float y = Math.Max(a.Bottom, b.Bottom) + stub;
            min = y;
            max = float.MaxValue;
            return y;
        }

        private static void Add(List<Vector2> points, Vector2 point)
        {
            Vector2 last = points[points.Count - 1];
            if (Vector2.DistanceSquared(last, point) < 0.0001f)
            {
                return;
            }

            // Straight through the previous point: it is not a corner.
            if (points.Count >= 2)
            {
                Vector2 before = points[points.Count - 2];
                bool sameX = Math.Abs(before.X - last.X) < 0.01f && Math.Abs(last.X - point.X) < 0.01f;
                bool sameY = Math.Abs(before.Y - last.Y) < 0.01f && Math.Abs(last.Y - point.Y) < 0.01f;
                bool onward = Vector2.Dot(last - before, point - last) > 0f;
                if ((sameX || sameY) && onward)
                {
                    points[points.Count - 1] = point;
                    return;
                }
            }

            points.Add(point);
        }

        /// <summary>
        /// The polyline to DRAW: <paramref name="corners"/> with every right
        /// angle replaced by a sampled quarter circle.
        ///
        /// The radius shrinks, per corner, to half of either neighbouring run,
        /// so two corners sharing a short run never overlap — a small step
        /// between two nearly level ports becomes a smooth S rather than a
        /// knot. A corner that is not a right angle is left sharp.
        /// </summary>
        public static void Round(List<Vector2> corners, float radius, List<Vector2> into)
        {
            into.Clear();
            if (corners.Count == 0)
            {
                return;
            }

            into.Add(corners[0]);
            for (int i = 1; i < corners.Count - 1; i++)
            {
                Vector2 p0 = corners[i - 1];
                Vector2 c = corners[i];
                Vector2 p1 = corners[i + 1];

                float inLength = Vector2.Distance(p0, c);
                float outLength = Vector2.Distance(c, p1);
                float r = Math.Min(radius, Math.Min(inLength, outLength) * 0.5f);

                Vector2 dIn = inLength > 0f ? (c - p0) / inLength : Vector2.Zero;
                Vector2 dOut = outLength > 0f ? (p1 - c) / outLength : Vector2.Zero;
                bool rightAngle = Math.Abs(Vector2.Dot(dIn, dOut)) < 0.01f;

                if (r < 0.5f || !rightAngle)
                {
                    into.Add(c);
                    continue;
                }

                // The arc runs from r before the corner to r after it; its
                // centre is the square's far corner.
                Vector2 start = c - dIn * r;
                Vector2 centre = start + dOut * r;
                int steps = Math.Clamp((int)(r / 2f), 3, 8);
                for (int k = 0; k <= steps; k++)
                {
                    float angle = k / (float)steps * MathHelper.PiOver2;
                    into.Add(centre - dOut * (r * (float)Math.Cos(angle)) + dIn * (r * (float)Math.Sin(angle)));
                }
            }

            if (corners.Count > 1)
            {
                into.Add(corners[corners.Count - 1]);
            }
        }

        /// <summary>
        /// Picks every run a wire's route has a choice about, so wires that
        /// would share a column (or a return channel) run side by side, one
        /// port row apart, instead of on top of each other or across each
        /// other (ezmuze studio #311, #321).
        ///
        /// <b>Spacing.</b> <paramref name="spacing"/> is the gap between
        /// parallel runs everywhere — pass the pitch of the port rows (at the
        /// zoom) and a stereo pair keeps ONE gap along its whole length: the
        /// ports set it on the horizontal runs, the lanes keep it on the
        /// vertical ones and on a backward wire's return run.
        ///
        /// <b>Nesting.</b> Parallel wires never cross at a corner, because
        /// each keeps the SAME SIDE of its bundle all the way along. A pair
        /// leaves its outputs heading right with the upper wire on its
        /// left-hand side (screen space, y down); after turning DOWN that puts
        /// the upper wire on the RIGHT, after turning UP on the LEFT, and on a
        /// backward wire's return run (heading left) at the BOTTOM. Applied
        /// per run, that is: the wire on the INSIDE of the turn into a run
        /// takes the lane nearest the corner — it turns first.
        /// <list type="bullet">
        /// <item>A vertical entered from the LEFT (a forward wire, or a
        /// backward wire's way out): going down, the LOWER entry is leftmost;
        /// going up, the UPPER entry is.</item>
        /// <item>A vertical entered from the RIGHT (a backward wire's way in):
        /// mirrored — going down, the lower entry is RIGHTMOST.</item>
        /// <item>A return run entered from above: the rightmost vertical
        /// takes the lowest lane; entered from below, the highest.</item>
        /// </list>
        /// Each run is ordered by where its wires come in from, so a pair that
        /// is nested at its outputs stays nested to its inputs.
        ///
        /// <b>Trunks.</b> Every wire from the same output (equal
        /// <see cref="LaneWire.SourceKey"/>) shares its first vertical —
        /// placed halfway to the NEAREST forward target — and backward wires
        /// from it that share a return channel also share that return run and
        /// the vertical to their inputs: a fan-out reads as one cable that
        /// branches, not as parallel wires.
        ///
        /// Lanes stay inside the range their own wires can turn in (a group
        /// slides as a whole to fit, so its gap stays even); a return channel
        /// between two nodes squeezes its spacing rather than run through a
        /// node, and one under both nodes stacks downward.
        ///
        /// Returns one entry per wire, in order.
        /// </summary>
        public static WireLanes[] AssignRoutes(IReadOnlyList<LaneWire> wires, float stub, float radius, float spacing)
        {
            var lanes = new WireLanes[wires.Count];
            var trunks = new List<Trunk>();
            var trunkOf = new Dictionary<object, Trunk>();
            var forwardWire = new bool[wires.Count];

            // ---- 1. Forward trunks: one vertical per output.
            for (int i = 0; i < wires.Count; i++)
            {
                LaneWire wire = wires[i];
                object key = wire.SourceKey ?? (object)i;
                if (!trunkOf.TryGetValue(key, out Trunk trunk))
                {
                    trunk = new Trunk { Key = key, From = wire.From, Min = float.MinValue, Max = float.MaxValue, Top = wire.From.Y, Bottom = wire.From.Y };
                    trunkOf[key] = trunk;
                    trunks.Add(trunk);
                }

                trunk.Members.Add(i);
                if (!IsForward(wire.From, wire.To, stub, radius))
                {
                    continue;
                }

                forwardWire[i] = true;
                trunk.Forward = true;
                trunk.Min = Math.Max(trunk.Min, wire.From.X + radius);
                trunk.Max = Math.Min(trunk.Max, wire.To.X - stub);
                trunk.Nearest = Math.Min(trunk.Nearest, wire.To.X);
                trunk.Top = Math.Min(trunk.Top, wire.To.Y);
                trunk.Bottom = Math.Max(trunk.Bottom, wire.To.Y);
                trunk.Travel += wire.To.Y - wire.From.Y;
            }

            var forward = new List<Run>();
            var forwardRunOf = new Dictionary<Trunk, Run>();
            foreach (Trunk trunk in trunks)
            {
                if (!trunk.Forward)
                {
                    continue;
                }

                float max = Math.Max(trunk.Min, trunk.Max);
                var run = new Run
                {
                    Position = Math.Clamp((trunk.From.X + trunk.Nearest) * 0.5f, trunk.Min, max),
                    Min = trunk.Min,
                    Max = max,
                    Start = trunk.Top,
                    End = trunk.Bottom,
                    Order = EnteredFromLeft(trunk.Travel, trunk.From.Y),
                    Tie = trunk.Members[0],
                };
                forward.Add(run);
                forwardRunOf[trunk] = run;
            }

            foreach (List<Run> cluster in Cluster(forward, spacing))
            {
                SpreadCentred(cluster, spacing);
            }

            foreach (KeyValuePair<Trunk, Run> pair in forwardRunOf)
            {
                foreach (int member in pair.Key.Members)
                {
                    lanes[member].X = pair.Value.Position;
                }
            }

            // ---- 2. Backward wires: out, back along a channel, and in.
            var back = new List<int>();
            var channels = new Dictionary<int, Channel>();
            for (int i = 0; i < wires.Count; i++)
            {
                if (forwardWire[i])
                {
                    continue;
                }

                LaneWire wire = wires[i];
                back.Add(i);
                float y = ReturnChannel(wire.From, wire.To, stub, radius, wire.FromSpan, wire.ToSpan,
                    out float min, out float max, out bool under);
                channels[i] = new Channel { Y = y, Min = min, Max = max, Under = under };
            }

            if (back.Count == 0)
            {
                return lanes;
            }

            // 2a. The way out, for an output with no forward trunk to borrow:
            // one vertical per output, stacked rightward from a stub past it.
            var outRuns = new List<Run>();
            var outRunOf = new Dictionary<Trunk, Run>();
            foreach (int i in back)
            {
                LaneWire wire = wires[i];
                Trunk trunk = trunkOf[wire.SourceKey ?? (object)i];
                if (trunk.Forward)
                {
                    continue;
                }

                if (!outRunOf.TryGetValue(trunk, out Run run))
                {
                    run = new Run
                    {
                        Position = wire.From.X + stub,
                        Min = wire.From.X + stub,
                        Max = float.MaxValue,
                        Start = wire.From.Y,
                        End = wire.From.Y,
                        Tie = i,
                    };
                    outRunOf[trunk] = run;
                    outRuns.Add(run);
                }

                float y = channels[i].Y;
                run.Start = Math.Min(run.Start, y);
                run.End = Math.Max(run.End, y);
                run.Travel += y - wire.From.Y;
            }

            foreach (KeyValuePair<Trunk, Run> pair in outRunOf)
            {
                pair.Value.Order = EnteredFromLeft(pair.Value.Travel, pair.Key.From.Y);
            }

            foreach (List<Run> cluster in Cluster(outRuns, spacing))
            {
                SpreadFrom(cluster, spacing, cluster.Max(r => r.Min), +1f);
            }

            foreach (int i in back)
            {
                Trunk trunk = trunkOf[wires[i].SourceKey ?? (object)i];
                if (outRunOf.TryGetValue(trunk, out Run run))
                {
                    lanes[i].X = run.Position;
                }
            }

            // 2b. The return run: one per output and channel, spread across
            // the channel — squeezed to fit a gap between two nodes, stacked
            // downward under them.
            var returnRuns = new List<Run>();
            var returnRunOf = new Dictionary<(object, float), Run>();
            var returnOf = new Dictionary<int, Run>();
            foreach (int i in back)
            {
                LaneWire wire = wires[i];
                Channel channel = channels[i];
                float outX = OutX(wire.From, stub, radius, lanes[i].X);
                float inX = InX(wire.To, stub, null);
                var key = (wire.SourceKey ?? (object)i, channel.Y);
                if (!returnRunOf.TryGetValue(key, out Run run))
                {
                    run = new Run
                    {
                        Position = channel.Y,
                        Min = channel.Min,
                        Max = channel.Max,
                        Under = channel.Under,
                        Start = inX,
                        End = outX,
                        // Entered from above, the rightmost vertical is the
                        // outer one, so the lowest; from below, the highest.
                        Order = channel.Y >= wire.From.Y ? outX : -outX,
                        Tie = i,
                    };
                    returnRunOf[key] = run;
                    returnRuns.Add(run);
                }

                run.Start = Math.Min(run.Start, inX);
                returnOf[i] = run;
            }

            foreach (List<Run> cluster in Cluster(returnRuns, spacing))
            {
                if (cluster.Exists(r => r.Under))
                {
                    SpreadFrom(cluster, spacing, cluster.Max(r => r.Position), +1f);
                    continue;
                }

                float min = cluster.Max(r => r.Min);
                float max = cluster.Min(r => r.Max);
                float fit = cluster.Count > 1 && max > min ? (max - min) / (cluster.Count - 1) : spacing;
                SpreadCentred(cluster, Math.Min(spacing, fit));
            }

            foreach (int i in back)
            {
                lanes[i].ReturnY = returnOf[i].Position;
            }

            // 2c. The way in: one vertical per return run (so a fan-out that
            // came back together branches at the end), stacked leftward from
            // a stub before the input.
            var inRuns = new List<Run>();
            var inRunOf = new Dictionary<Run, Run>();
            var inOf = new Dictionary<int, Run>();
            foreach (int i in back)
            {
                LaneWire wire = wires[i];
                Run ret = returnOf[i];
                float inX = InX(wire.To, stub, null);
                if (!inRunOf.TryGetValue(ret, out Run run))
                {
                    run = new Run
                    {
                        Min = float.MinValue,
                        Max = inX,
                        Start = ret.Position,
                        End = ret.Position,
                        Tie = i,
                    };
                    inRunOf[ret] = run;
                    inRuns.Add(run);
                }

                run.Max = Math.Min(run.Max, inX);
                run.Position = run.Max;
                run.Start = Math.Min(run.Start, wire.To.Y);
                run.End = Math.Max(run.End, wire.To.Y);
                run.Travel += wire.To.Y - ret.Position;
                inOf[i] = run;
            }

            foreach (KeyValuePair<Run, Run> pair in inRunOf)
            {
                // Entered from the right: going down the lower entry is the
                // inner wire, which is now the RIGHTMOST lane; going up the
                // upper entry is. Order ascends rightward.
                float y = pair.Key.Position;
                pair.Value.Order = pair.Value.Travel >= 0f ? y : -y;
            }

            foreach (List<Run> cluster in Cluster(inRuns, spacing))
            {
                SpreadFrom(cluster, spacing, cluster.Min(r => r.Max), -1f);
            }

            foreach (int i in back)
            {
                lanes[i].InX = inOf[i].Position;
            }

            return lanes;
        }

        /// <summary>The first-vertical lanes only (<see cref="WireLanes.X"/>)
        /// of <see cref="AssignRoutes"/> — the shape this had before
        /// backward wires got lanes (#321). A backward wire routed with only
        /// this X still collapses onto its neighbours on the way back; use
        /// <see cref="AssignRoutes"/>.</summary>
        public static float?[] AssignLanes(IReadOnlyList<LaneWire> wires, float stub, float radius, float spacing)
        {
            WireLanes[] routes = AssignRoutes(wires, stub, radius, spacing);
            var lanes = new float?[routes.Length];
            for (int i = 0; i < routes.Length; i++)
            {
                lanes[i] = routes[i].X;
            }

            return lanes;
        }

        /// <summary>The sort key of a vertical entered from the left, with
        /// its lanes in ascending X: going down the lower entry comes first
        /// (it is on the inside of the turn), going up the upper one does.</summary>
        private static float EnteredFromLeft(float travel, float entryY)
            => travel >= 0f ? -entryY : entryY;

        /// <summary>Where a backward wire's first vertical goes: its lane if
        /// that leaves room for a corner after the output, else a stub out.</summary>
        private static float OutX(Vector2 from, float stub, float radius, float? laneX)
            => laneX.HasValue && laneX.Value >= from.X + radius ? laneX.Value : from.X + stub;

        /// <summary>Where a backward wire's last vertical goes: its lane if
        /// that still leaves a whole stub before the input, else a stub
        /// before it.</summary>
        private static float InX(Vector2 to, float stub, float? laneX)
            => laneX.HasValue && laneX.Value <= to.X - stub + 0.01f ? laneX.Value : to.X - stub;

        /// <summary>
        /// Groups runs that would collide — closer than
        /// <paramref name="spacing"/> across the run and overlapping (give or
        /// take a spacing) along it — then sorts each group by its nesting
        /// order.
        /// </summary>
        private static List<List<Run>> Cluster(List<Run> runs, float spacing)
        {
            runs.Sort((a, b) => a.Position != b.Position ? a.Position.CompareTo(b.Position) : a.Tie.CompareTo(b.Tie));
            var clusters = new List<List<Run>>();
            foreach (Run run in runs)
            {
                List<Run> home = null;
                foreach (List<Run> cluster in clusters)
                {
                    foreach (Run other in cluster)
                    {
                        if (Math.Abs(other.Position - run.Position) < spacing - 0.01f
                            && run.Start <= other.End + spacing && other.Start <= run.End + spacing)
                        {
                            home = cluster;
                            break;
                        }
                    }

                    if (home != null)
                    {
                        break;
                    }
                }

                if (home == null)
                {
                    home = new List<Run>();
                    clusters.Add(home);
                }

                home.Add(run);
            }

            foreach (List<Run> cluster in clusters)
            {
                cluster.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Tie.CompareTo(b.Tie));
            }

            return clusters;
        }

        /// <summary>Spreads a sorted group <paramref name="spacing"/> apart
        /// around its shared centre, sliding the whole group (not one lane)
        /// to keep every lane inside its own range where that is possible.</summary>
        private static void SpreadCentred(List<Run> cluster, float spacing)
        {
            if (cluster.Count < 2)
            {
                return;
            }

            float centre = cluster.Average(r => r.Position);
            float lo = float.MinValue;
            float hi = float.MaxValue;
            for (int i = 0; i < cluster.Count; i++)
            {
                float offset = (i - (cluster.Count - 1) * 0.5f) * spacing;
                lo = Math.Max(lo, cluster[i].Min - offset);
                hi = Math.Min(hi, cluster[i].Max - offset);
            }

            if (lo <= hi)
            {
                centre = Math.Clamp(centre, lo, hi);
            }

            for (int i = 0; i < cluster.Count; i++)
            {
                Run run = cluster[i];
                float at = centre + (i - (cluster.Count - 1) * 0.5f) * spacing;
                run.Position = Math.Clamp(at, run.Min, Math.Max(run.Min, run.Max));
            }
        }

        /// <summary>Stacks a sorted group <paramref name="spacing"/> apart
        /// from <paramref name="origin"/>: rightward/downward for a positive
        /// <paramref name="direction"/>, leftward/upward for a negative one.
        /// Positions always ascend in the group's order, so stacking
        /// backward puts the LAST in order at the origin.</summary>
        private static void SpreadFrom(List<Run> cluster, float spacing, float origin, float direction)
        {
            int n = cluster.Count;
            for (int i = 0; i < n; i++)
            {
                int step = direction > 0f ? i : n - 1 - i;
                cluster[i].Position = origin + (direction * step * spacing);
            }
        }

        /// <summary>One run a lane is picked for: a vertical (Position is
        /// its X, Start..End its Y extent) or a return run (Position is its
        /// Y, Start..End its X extent).</summary>
        private sealed class Run
        {
            public float Position;
            public float Min;
            public float Max;
            public float Start;
            public float End;
            public float Travel;
            public float Order;
            public int Tie;
            public bool Under;
        }

        private struct Channel
        {
            public float Y;
            public float Min;
            public float Max;
            public bool Under;
        }

        private sealed class Trunk
        {
            public object Key;
            public Vector2 From;
            public bool Forward;
            public float Min;
            public float Max;
            public float Nearest = float.MaxValue;
            public float Top;
            public float Bottom;
            public float Travel;
            public readonly List<int> Members = new List<int>();
        }
    }
}
