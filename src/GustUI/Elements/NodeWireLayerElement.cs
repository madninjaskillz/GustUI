using System;
using System.Collections.Generic;
using GustUI.Attributes;
using GustUI.Extensions;
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
    }

    /// <summary>
    /// The node-canvas wire layer (module builder): draws a display list of
    /// wires as cubic Béziers with horizontal tangents, an optional live
    /// drag-preview wire, and the scaler affordance square at each wire's
    /// input end. Deliberately carries NO mouse traits, so pointer events
    /// fall through to the node elements above/below it; the host places its
    /// own small hit elements over the affordance squares
    /// (<see cref="ScalerAnchor"/> exposes the shared geometry).
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

        /// <summary>Where a wire's scaler affordance sits: just before the
        /// input end, pulled back along the curve's incoming tangent.</summary>
        public static Vector2 ScalerAnchor(Vector2 from, Vector2 to)
        {
            return new Vector2(to.X - 16f, to.Y);
        }

        private static (Vector2 c0, Vector2 c1) ControlPoints(Vector2 from, Vector2 to)
        {
            float bend = Math.Max(36f, Math.Abs(to.X - from.X) * 0.5f);
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
                (Vector2 c0, Vector2 c1) = ControlPoints(from, to);
                manager.DrawCubicBezier(from, c0, c1, to, wire.Selected ? WireSelectedColor : WireColor, 2);

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
                Vector2 from = origin + PreviewFrom.Value;
                Vector2 to = origin + PreviewTo.Value;
                (Vector2 c0, Vector2 c1) = ControlPoints(from, to);
                manager.DrawCubicBezier(from, c0, c1, to, PreviewColor, 2);
            }

            base.Draw();
        }
    }
}
