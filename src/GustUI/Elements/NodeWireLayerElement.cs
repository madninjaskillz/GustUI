using System;
using System.Collections.Generic;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Managers;
using GustUI.Traits;
using Microsoft.Xna.Framework;

namespace GustUI.Elements
{
    /// <summary>One wire on a <see cref="NodeWireLayerElement"/> (a display
    /// view — the host owns the real model and syncs this list after edits).
    /// Endpoints are element-relative; From is the OUTPUT end, To the INPUT
    /// end (where the scaler affordance square draws).</summary>
    public class NodeWireView
    {
        public Guid Id;
        public Vector2 From;
        public Vector2 To;
        public bool Selected;

        /// <summary>True when the wire carries a non-identity scaler — the
        /// affordance square draws filled instead of hollow.</summary>
        public bool HasScaler;

        /// <summary>
        /// The colour of each END, when the host knows what its ports are.
        ///
        /// A wire is drawn as a GRADIENT between them, so it leaves the output
        /// wearing the output's colour and arrives wearing the input's, and
        /// the halfway point of the line is the halfway point of the two. That
        /// is what makes a canvas of thirty wires readable: you can see where
        /// a cable is going from either end of it without following the curve.
        ///
        /// Null on either side falls back to the layer's own
        /// <see cref="NodeWireLayerElement.WireColor"/>, which is what a host
        /// that has no colour scheme gets — unchanged from before this
        /// existed.
        /// </summary>
        public Color? FromColor;

        /// <inheritdoc cref="FromColor"/>
        public Color? ToColor;

        /// <summary>
        /// Draw this wire DASHED. For a cable that carries a pointer rather
        /// than a signal — a handle to a file, say — so it can be told apart
        /// from every other wire on the canvas with the colour switched off.
        /// The host decides which wires qualify; the layer only draws.
        /// </summary>
        public bool Dashed;

        /// <summary>
        /// Straight routing only (<see cref="WireRouting.Orthogonal"/>): the X
        /// of this wire's vertical run, from
        /// <see cref="OrthogonalWireRoute.AssignRoutes"/>, so wires sharing a
        /// column run side by side and a fan-out shares one trunk. Null takes
        /// the default: halfway across.
        /// </summary>
        public float? LaneX;

        /// <summary>Straight routing, a wire that loops BACK: the Y of its
        /// return run, from <see cref="OrthogonalWireRoute.AssignRoutes"/>
        /// (<see cref="WireLanes.ReturnY"/>). Null takes the default channel.</summary>
        public float? ReturnLaneY;

        /// <summary>Straight routing, a wire that loops BACK: the X of its
        /// last vertical, into the input (<see cref="WireLanes.InX"/>). Null
        /// takes the default: a stub before the input.</summary>
        public float? InLaneX;

        /// <summary>Straight routing only: the vertical extent of the node at
        /// each end (element-relative), so a wire that has to loop BACK knows
        /// whether it can pass between its two nodes or must go under both.
        /// Null stands the port in for the node.</summary>
        public WireNodeSpan? FromSpan;

        /// <inheritdoc cref="FromSpan"/>
        public WireNodeSpan? ToSpan;
    }

    /// <summary>How a <see cref="NodeWireLayerElement"/> draws its wires.</summary>
    public enum WireRouting
    {
        /// <summary>Cubic Béziers with horizontal tangents — the default.</summary>
        Curved,

        /// <summary>Straight horizontal and vertical runs joined by rounded
        /// corners (<see cref="OrthogonalWireRoute"/>) — a workflow graph's
        /// wires rather than patch cables.</summary>
        Orthogonal,
    }

    /// <summary>
    /// The node-canvas wire layer (module builder): draws a display list of
    /// wires as cubic Béziers with horizontal tangents (or, with
    /// <see cref="Routing"/> set to Orthogonal, straight runs with rounded
    /// corners), an optional live
    /// drag-preview wire, and the scaler affordance square at each wire's
    /// input end. Deliberately carries NO mouse traits, so pointer events
    /// fall through to the node elements above/below it; the host places its
    /// own small hit elements over the affordance squares
    /// (<see cref="ScalerAnchor"/> exposes the shared geometry), and asks
    /// <see cref="HitTest(Vector2, float)"/> from its own background press to
    /// select a wire anywhere along it.
    /// </summary>
    [ElementTraits(typeof(PositionTrait), typeof(SizeTrait))]
    public class NodeWireLayerElement : Element
    {
        public const float ScalerAffordanceSize = 10f;

        public List<NodeWireView> Wires = new List<NodeWireView>();

        /// <summary>Live drag preview (element-relative); null when idle.</summary>
        public Vector2? PreviewFrom;
        public Vector2? PreviewTo;

        public Color WireColor = new Color(150, 170, 210);
        public Color WireSelectedColor = new Color(255, 210, 120);
        public Color PreviewColor = new Color(120, 220, 160);
        public Color AffordanceFill = new Color(255, 210, 120);
        public Color AffordanceHollow = new Color(90, 100, 125);

        /// <summary>Béziers (the default) or straight runs with rounded
        /// corners. A host flips it; nothing else about a wire changes — its
        /// colours, dashes, selection and scaler square are drawn the same.</summary>
        public WireRouting Routing = WireRouting.Curved;

        /// <summary>Straight routing: the shortest straight run out of an
        /// output and into an input. Keep it past 16px + half the scaler
        /// square, so the square sits on the wire rather than a corner. A
        /// zooming host scales it.</summary>
        public float Stub = 24f;

        /// <summary>Straight routing: the corner radius. Shrinks per corner
        /// to fit a short run.</summary>
        public float CornerRadius = 8f;

        // Scratch lists for the straight route, reused across wires and
        // frames (the layer redraws everything every frame).
        private readonly List<Vector2> routeCorners = new List<Vector2>(8);
        private readonly List<Vector2> routePoints = new List<Vector2>(64);

        /// <summary>Where a wire's scaler affordance sits: just before the
        /// input end, pulled back along the curve's incoming tangent.</summary>
        public static Vector2 ScalerAnchor(Vector2 from, Vector2 to)
        {
            return new Vector2(to.X - 16f, to.Y);
        }

        /// <summary>
        /// The two control points, which give the curve its horizontal
        /// tangents at both ends — a wire leaves a socket going sideways,
        /// which is what makes it read as a cable rather than a line.
        ///
        /// The bend is HALF THE HORIZONTAL SPAN, floored so a near-vertical
        /// wire still bows instead of collapsing to a straight line, and
        /// capped so a wire crossing the whole canvas does not throw its
        /// control points so far out that the curve leaves the visible area
        /// on its way between two points that are both on screen.
        /// </summary>
        private static (Vector2 c0, Vector2 c1) ControlPoints(Vector2 from, Vector2 to)
        {
            float bend = Math.Clamp(Math.Abs(to.X - from.X) * 0.5f, 36f, 320f);
            return (new Vector2(from.X + bend, from.Y), new Vector2(to.X - bend, to.Y));
        }

        public override void Draw()
        {
            var manager = Resources.StaticResources.DrawManager;
            Vector2 origin = this.GetActualXnaPosition();

            foreach (NodeWireView wire in Wires)
            {
                Vector2 from = origin + wire.From;
                Vector2 to = origin + wire.To;

                // SELECTION OVERRIDES THE GRADIENT, on both ends. A selected
                // wire has to be findable at a glance, and a gradient that
                // merely brightened would be competing with thirty other
                // coloured wires rather than standing out from them.
                Color fromColor = wire.Selected ? WireSelectedColor : wire.FromColor ?? WireColor;
                Color toColor = wire.Selected ? WireSelectedColor : wire.ToColor ?? WireColor;
                if (Routing == WireRouting.Orthogonal)
                {
                    var lanes = new WireLanes
                    {
                        X = origin.X + wire.LaneX,
                        ReturnY = origin.Y + wire.ReturnLaneY,
                        InX = origin.X + wire.InLaneX,
                    };
                    BuildRoute(from, to, lanes, Offset(wire.FromSpan, origin.Y), Offset(wire.ToSpan, origin.Y));
                    if (wire.Dashed)
                    {
                        DrawDashedPolyline(manager, routePoints, fromColor, toColor);
                    }
                    else
                    {
                        manager.DrawPolyline(routePoints, fromColor, toColor, 2);
                    }
                }
                else if (wire.Dashed)
                {
                    (Vector2 c0, Vector2 c1) = ControlPoints(from, to);
                    DrawDashedBezier(manager, from, c0, c1, to, fromColor, toColor);
                }
                else
                {
                    (Vector2 c0, Vector2 c1) = ControlPoints(from, to);
                    manager.DrawCubicBezier(from, c0, c1, to, fromColor, toColor, 2);
                }

                // Scaler affordance on the INPUT end.
                Vector2 anchor = origin + ScalerAnchor(wire.From, wire.To);
                int s = (int)ScalerAffordanceSize;
                var rect = new Rectangle((int)(anchor.X - s / 2f), (int)(anchor.Y - s / 2f), s, s);
                if (wire.HasScaler)
                {
                    manager.DrawFilledRectangle(rect, AffordanceFill);
                    manager.DrawRectangle(rect, Color.Black * 0.6f);
                }
                else
                {
                    manager.DrawFilledRectangle(rect, Color.Black * 0.35f);
                    manager.DrawRectangle(rect, wire.Selected ? WireSelectedColor : AffordanceHollow);
                }
            }

            if (PreviewFrom.HasValue && PreviewTo.HasValue)
            {
                // (the preview is never dashed: it does not know yet what
                // it will connect)
                Vector2 from = origin + PreviewFrom.Value;
                Vector2 to = origin + PreviewTo.Value;
                if (Routing == WireRouting.Orthogonal)
                {
                    // Routed like the wire it will become, so the drag shows
                    // what the drop gives. No lane: it has no neighbours yet.
                    BuildRoute(from, to, default, null, null);
                    manager.DrawPolyline(routePoints, PreviewColor, PreviewColor, 2);
                }
                else
                {
                    (Vector2 c0, Vector2 c1) = ControlPoints(from, to);
                    manager.DrawCubicBezier(from, c0, c1, to, PreviewColor, 2);
                }
            }

            base.Draw();
        }

        /// <summary>
        /// The wire under <paramref name="point"/> (element-relative), or
        /// null: the one whose DRAWN path passes within
        /// <paramref name="tolerance"/> of it. Topmost first - the last wire
        /// drawn is the one on top - so a click where two cross picks the
        /// one you can see (ezmuze studio #314).
        ///
        /// The layer still carries no mouse traits: a host asks this from its
        /// own background press, so a press on a node or a port never has to
        /// get past the wires first.
        /// </summary>
        public Guid? HitTest(Vector2 point, float tolerance = 6f)
            => HitTest(Wires, Routing, Stub, CornerRadius, point, tolerance);

        /// <summary>As <see cref="HitTest(Vector2, float)"/>, on any list of
        /// wires - the pure form, which is what the tests drive.</summary>
        public static Guid? HitTest(IReadOnlyList<NodeWireView> wires, WireRouting routing, float stub, float radius,
            Vector2 point, float tolerance)
        {
            if (wires == null)
            {
                return null;
            }

            var corners = new List<Vector2>(8);
            var path = new List<Vector2>(64);
            for (int i = wires.Count - 1; i >= 0; i--)
            {
                NodeWireView wire = wires[i];
                WirePath(wire, routing, stub, radius, corners, path);
                for (int k = 1; k < path.Count; k++)
                {
                    if (DistanceToSegment(point, path[k - 1], path[k]) <= tolerance)
                    {
                        return wire.Id;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// A wire's drawn path as a polyline, element-relative: the rounded
        /// straight route, or the Bézier walked in short steps. Built from
        /// the same geometry <see cref="Draw"/> uses (the router, and
        /// <see cref="ControlPoints"/>), so what a click finds is what is on
        /// screen.
        /// </summary>
        public static void WirePath(NodeWireView wire, WireRouting routing, float stub, float radius,
            List<Vector2> corners, List<Vector2> into)
        {
            into.Clear();
            if (routing == WireRouting.Orthogonal)
            {
                var lanes = new WireLanes { X = wire.LaneX, ReturnY = wire.ReturnLaneY, InX = wire.InLaneX };
                OrthogonalWireRoute.Corners(corners, wire.From, wire.To, stub, radius, lanes, wire.FromSpan, wire.ToSpan);
                OrthogonalWireRoute.Round(corners, radius, into);
                return;
            }

            (Vector2 c0, Vector2 c1) = ControlPoints(wire.From, wire.To);
            float chord = Vector2.Distance(wire.From, wire.To);
            int steps = Math.Max(24, (int)(chord / 6f));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float u = 1f - t;
                into.Add((u * u * u * wire.From) + (3f * u * u * t * c0) + (3f * u * t * t * c1) + (t * t * t * wire.To));
            }
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float length = ab.LengthSquared();
            float t = length > 0f ? Math.Clamp(Vector2.Dot(p - a, ab) / length, 0f, 1f) : 0f;
            return Vector2.Distance(p, a + (ab * t));
        }

        private void BuildRoute(Vector2 from, Vector2 to, WireLanes lanes, WireNodeSpan? fromSpan, WireNodeSpan? toSpan)
        {
            OrthogonalWireRoute.Corners(routeCorners, from, to, Stub, CornerRadius, lanes, fromSpan, toSpan);
            OrthogonalWireRoute.Round(routeCorners, CornerRadius, routePoints);
        }

        private static WireNodeSpan? Offset(WireNodeSpan? span, float y)
            => span.HasValue ? new WireNodeSpan(span.Value.Top + y, span.Value.Bottom + y) : (WireNodeSpan?)null;

        /// <summary>
        /// The dashed stroke along any polyline: every other run of
        /// <see cref="DashLength"/>, split exactly at the dash boundaries
        /// (a straight wire's runs are long, so stepping point to point would
        /// make each dash a whole run). Each dash is coloured by where it is
        /// along the whole line, as the solid gradient is.
        /// </summary>
        private static void DrawDashedPolyline(DrawManager manager, List<Vector2> points, Color fromColor, Color toColor)
        {
            float total = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                total += Vector2.Distance(points[i - 1], points[i]);
            }

            if (total <= 0.0001f)
            {
                return;
            }

            float travelled = 0f;
            float run = 0f;
            bool on = true;
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 a = points[i - 1];
                Vector2 b = points[i];
                float length = Vector2.Distance(a, b);
                float done = 0f;
                while (done < length - 0.0001f)
                {
                    float piece = Math.Min(DashLength - run, length - done);
                    if (on)
                    {
                        Vector2 p = Vector2.Lerp(a, b, done / length);
                        Vector2 q = Vector2.Lerp(a, b, (done + piece) / length);
                        float at = (travelled + done + piece * 0.5f) / total;
                        manager.DrawThickLine(p, q, Color.Lerp(fromColor, toColor, at), 2);
                    }

                    done += piece;
                    run += piece;
                    if (run >= DashLength - 0.0001f)
                    {
                        run = 0f;
                        on = !on;
                    }
                }

                travelled += length;
            }
        }

        /// <summary>Dash length in pixels, and the gap the same, so the
        /// cable reads as a dotted pointer at any zoom rather than a wire
        /// with a flicker in it.</summary>
        public const float DashLength = 7f;

        /// <summary>
        /// The dashed stroke: the curve is walked in short straight steps
        /// and every other run of <see cref="DashLength"/> is drawn. Each
        /// dash takes the gradient colour at its own position, so a dashed
        /// wire still leaves wearing the output's colour and arrives
        /// wearing the input's, the way a solid one does.
        /// </summary>
        private static void DrawDashedBezier(DrawManager manager, Vector2 p0, Vector2 c0, Vector2 c1, Vector2 p1,
            Color fromColor, Color toColor)
        {
            // Step count from the chord, generously: the dash boundaries
            // are what have to look right, and they land between samples.
            float chord = Vector2.Distance(p0, p1);
            int steps = Math.Max(24, (int)(chord / 3f));
            Vector2 previous = p0;
            float run = 0f;
            bool on = true;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                float u = 1f - t;
                Vector2 point =
                    (u * u * u * p0)
                    + (3f * u * u * t * c0)
                    + (3f * u * t * t * c1)
                    + (t * t * t * p1);

                float length = Vector2.Distance(previous, point);
                if (on)
                {
                    manager.DrawThickLine(previous, point, Color.Lerp(fromColor, toColor, t), 2);
                }

                run += length;
                if (run >= DashLength)
                {
                    run = 0f;
                    on = !on;
                }

                previous = point;
            }
        }
    }
}
