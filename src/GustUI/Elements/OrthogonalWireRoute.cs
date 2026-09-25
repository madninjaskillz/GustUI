using System;
using System.Collections.Generic;
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

    /// <summary>One wire as <see cref="OrthogonalWireRoute.AssignLanes"/>
    /// sees it: its two ends, and which output it comes from. Wires with
    /// equal <see cref="SourceKey"/>s share a trunk.</summary>
    public struct LaneWire
    {
        public Vector2 From;
        public Vector2 To;
        public object SourceKey;
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
        /// <see cref="AssignLanes"/>. Null means the default.</param>
        public static List<Vector2> Corners(Vector2 from, Vector2 to, float stub, float radius,
            float? laneX = null, WireNodeSpan? fromSpan = null, WireNodeSpan? toSpan = null)
        {
            var points = new List<Vector2>(6);
            Corners(points, from, to, stub, radius, laneX, fromSpan, toSpan);
            return points;
        }

        /// <summary>As <see cref="Corners(Vector2, Vector2, float, float, float?, WireNodeSpan?, WireNodeSpan?)"/>,
        /// into a caller's list (cleared first) — the per-frame path.</summary>
        public static void Corners(List<Vector2> points, Vector2 from, Vector2 to, float stub, float radius,
            float? laneX = null, WireNodeSpan? fromSpan = null, WireNodeSpan? toSpan = null)
        {
            points.Clear();
            points.Add(from);

            if (IsForward(from, to, stub, radius))
            {
                float x = ClampForward(laneX ?? (from.X + to.X) * 0.5f, from, to, stub, radius);
                Add(points, new Vector2(x, from.Y));
                Add(points, new Vector2(x, to.Y));
                Add(points, to);
                return;
            }

            float outX = laneX.HasValue && laneX.Value >= from.X + radius ? laneX.Value : from.X + stub;
            float inX = to.X - stub;
            float y = ReturnChannel(from, to, stub, radius, fromSpan, toSpan);

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
        /// for the nodes.
        /// </summary>
        private static float ReturnChannel(Vector2 from, Vector2 to, float stub, float radius,
            WireNodeSpan? fromSpan, WireNodeSpan? toSpan)
        {
            WireNodeSpan a = fromSpan ?? new WireNodeSpan(from.Y, from.Y);
            WireNodeSpan b = toSpan ?? new WireNodeSpan(to.Y, to.Y);

            // A gap has to fit two corners to be worth threading.
            float minGap = radius * 2f;
            if (b.Top - a.Bottom >= minGap)
            {
                return (a.Bottom + b.Top) * 0.5f;
            }

            if (a.Top - b.Bottom >= minGap)
            {
                return (b.Bottom + a.Top) * 0.5f;
            }

            return Math.Max(a.Bottom, b.Bottom) + stub;
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
        /// Picks a vertical run for every wire that has one, so wires that
        /// would share the same column run side by side instead of on top of
        /// each other.
        ///
        /// TRUNKS FIRST: every wire from the same output (equal
        /// <see cref="LaneWire.SourceKey"/>) shares one vertical run, placed
        /// halfway to the NEAREST of its targets — a fan-out reads as one
        /// cable that branches, the way a workflow graph draws it, rather
        /// than as parallel wires. Then trunks whose runs would land within
        /// <paramref name="spacing"/> of each other AND overlap vertically are
        /// spread <paramref name="spacing"/> apart around their shared centre,
        /// in order of source height. Each lane stays inside the range its
        /// own wires can turn in.
        ///
        /// Returns one entry per wire, in order: the lane X, or null for a
        /// wire whose source has no forward wire (it takes the default
        /// backward route).
        /// </summary>
        public static float?[] AssignLanes(IReadOnlyList<LaneWire> wires, float stub, float radius, float spacing)
        {
            var lanes = new float?[wires.Count];
            var trunks = new List<Trunk>();
            var trunkOf = new Dictionary<object, Trunk>();

            for (int i = 0; i < wires.Count; i++)
            {
                LaneWire wire = wires[i];
                object key = wire.SourceKey ?? (object)i;
                if (!trunkOf.TryGetValue(key, out Trunk trunk))
                {
                    trunk = new Trunk { From = wire.From, Min = float.MinValue, Max = float.MaxValue, Top = wire.From.Y, Bottom = wire.From.Y };
                    trunkOf[key] = trunk;
                    trunks.Add(trunk);
                }

                trunk.Members.Add(i);
                if (!IsForward(wire.From, wire.To, stub, radius))
                {
                    continue;
                }

                trunk.Forward = true;
                trunk.Min = Math.Max(trunk.Min, wire.From.X + radius);
                trunk.Max = Math.Min(trunk.Max, wire.To.X - stub);
                trunk.Nearest = Math.Min(trunk.Nearest, wire.To.X);
                trunk.Top = Math.Min(trunk.Top, wire.To.Y);
                trunk.Bottom = Math.Max(trunk.Bottom, wire.To.Y);
            }

            var forward = new List<Trunk>();
            foreach (Trunk trunk in trunks)
            {
                if (trunk.Forward)
                {
                    trunk.X = Math.Clamp((trunk.From.X + trunk.Nearest) * 0.5f, trunk.Min, Math.Max(trunk.Min, trunk.Max));
                    forward.Add(trunk);
                }
            }

            // Cluster: a trunk joins the first cluster it would collide with.
            forward.Sort((a, b) => a.X.CompareTo(b.X));
            var clusters = new List<List<Trunk>>();
            foreach (Trunk trunk in forward)
            {
                List<Trunk> home = null;
                foreach (List<Trunk> cluster in clusters)
                {
                    float centre = Centre(cluster);
                    if (Math.Abs(centre - trunk.X) >= spacing)
                    {
                        continue;
                    }

                    foreach (Trunk other in cluster)
                    {
                        if (trunk.Top <= other.Bottom + spacing && other.Top <= trunk.Bottom + spacing)
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
                    home = new List<Trunk>();
                    clusters.Add(home);
                }

                home.Add(trunk);
            }

            foreach (List<Trunk> cluster in clusters)
            {
                if (cluster.Count < 2)
                {
                    continue;
                }

                float centre = Centre(cluster);
                cluster.Sort((a, b) => a.From.Y.CompareTo(b.From.Y));
                for (int i = 0; i < cluster.Count; i++)
                {
                    Trunk trunk = cluster[i];
                    float x = centre + (i - (cluster.Count - 1) * 0.5f) * spacing;
                    trunk.X = Math.Clamp(x, trunk.Min, Math.Max(trunk.Min, trunk.Max));
                }
            }

            foreach (Trunk trunk in trunks)
            {
                if (!trunk.Forward)
                {
                    continue;
                }

                foreach (int member in trunk.Members)
                {
                    lanes[member] = trunk.X;
                }
            }

            return lanes;
        }

        private static float Centre(List<Trunk> cluster)
        {
            float sum = 0f;
            foreach (Trunk trunk in cluster)
            {
                sum += trunk.X;
            }

            return sum / cluster.Count;
        }

        private sealed class Trunk
        {
            public Vector2 From;
            public bool Forward;
            public float Min;
            public float Max;
            public float Nearest = float.MaxValue;
            public float Top;
            public float Bottom;
            public float X;
            public readonly List<int> Members = new List<int>();
        }
    }
}
