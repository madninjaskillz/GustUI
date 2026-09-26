using System;
using System.Collections.Generic;
using GustUI.Elements;
using Microsoft.Xna.Framework;

namespace GustUI.Tests
{
    /// <summary>
    /// Selecting a wire anywhere along it (ezmuze studio #314): the hit test
    /// follows the path the layer DRAWS, straight or curved, and the topmost
    /// wire wins where two cross.
    /// </summary>
    public class NodeWireHitTestTests
    {
        private const float Stub = 24f;
        private const float Radius = 8f;

        private static NodeWireView Wire(Vector2 from, Vector2 to)
            => new NodeWireView { Id = Guid.NewGuid(), From = from, To = to };

        [Fact]
        public void AStraightWireIsHitOnItsVerticalRun()
        {
            // Forward, turning at x = 150 on its way from y 0 to y 200.
            NodeWireView wire = Wire(new Vector2(0, 0), new Vector2(300, 200));
            Guid? hit = NodeWireLayerElement.HitTest(new[] { wire }, WireRouting.Orthogonal, Stub, Radius, new Vector2(153, 100), 6f);
            Assert.Equal(wire.Id, hit);
        }

        [Fact]
        public void AStraightWireIsMissedWhereACurveWouldHaveBeen()
        {
            // Halfway between the ends is on the Bezier but nowhere near the
            // straight route's runs (the vertical is at x 150, the midpoint of
            // the chord at 150,100 IS on it - so probe off it).
            NodeWireView wire = Wire(new Vector2(0, 0), new Vector2(300, 200));
            Guid? hit = NodeWireLayerElement.HitTest(new[] { wire }, WireRouting.Orthogonal, Stub, Radius, new Vector2(100, 100), 6f);
            Assert.Null(hit);
        }

        [Fact]
        public void TheLanesTheRouterPickedAreWhatIsHit()
        {
            NodeWireView wire = Wire(new Vector2(0, 0), new Vector2(300, 200));
            wire.LaneX = 60f;
            Assert.Equal(wire.Id, NodeWireLayerElement.HitTest(new[] { wire }, WireRouting.Orthogonal, Stub, Radius, new Vector2(60, 100), 4f));
            Assert.Null(NodeWireLayerElement.HitTest(new[] { wire }, WireRouting.Orthogonal, Stub, Radius, new Vector2(150, 100), 4f));
        }

        [Fact]
        public void ACurvedWireIsHitAtItsMiddle()
        {
            // The Bezier's control points are symmetric about its middle, so
            // t = 0.5 is the chord's midpoint.
            NodeWireView wire = Wire(new Vector2(0, 0), new Vector2(300, 200));
            Assert.Equal(wire.Id, NodeWireLayerElement.HitTest(new[] { wire }, WireRouting.Curved, Stub, Radius, new Vector2(150, 100), 3f));
            Assert.Null(NodeWireLayerElement.HitTest(new[] { wire }, WireRouting.Curved, Stub, Radius, new Vector2(150, 40), 3f));
        }

        [Fact]
        public void EmptySpaceHitsNothing()
        {
            NodeWireView wire = Wire(new Vector2(0, 0), new Vector2(300, 0));
            Assert.Null(NodeWireLayerElement.HitTest(new[] { wire }, WireRouting.Orthogonal, Stub, Radius, new Vector2(150, 20), 6f));
            Assert.Null(NodeWireLayerElement.HitTest(new NodeWireView[0], WireRouting.Orthogonal, Stub, Radius, new Vector2(0, 0), 6f));
        }

        [Fact]
        public void WhereTwoWiresCrossTheTopmostWins()
        {
            // Both straight across y = 50: the later one is drawn on top.
            NodeWireView under = Wire(new Vector2(0, 50), new Vector2(300, 50));
            NodeWireView over = Wire(new Vector2(0, 50), new Vector2(300, 50));
            var wires = new List<NodeWireView> { under, over };
            Assert.Equal(over.Id, NodeWireLayerElement.HitTest(wires, WireRouting.Orthogonal, Stub, Radius, new Vector2(150, 51), 6f));
        }
    }
}
