using System.Collections.Generic;
using GustUI.Elements;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Tests
{
    /// <summary>
    /// The frequency-response editor (ezmuze studio #497): its axes, the
    /// scale it chooses, and what a press, a drag, the wheel and a
    /// double-click do to a node — driven without a pointer or a device.
    /// </summary>
    public class FrequencyResponseTests
    {
        private const float W = 400f;
        private const float H = 200f;

        private static FrequencyResponseElement Editor(params ResponseNode[] nodes)
        {
            var editor = new FrequencyResponseElement { CanAddNodes = true };
            editor.Set<SizeTrait>(new TVVector(W, H));
            editor.Nodes.AddRange(nodes);
            return editor;
        }

        private static Vector2 At(FrequencyResponseElement e, float hz, float db)
            => new Vector2(e.XOf(hz, W), e.YOf(db, H));

        [Fact]
        public void TheFrequencyAxisIsLogarithmic()
        {
            // Every decade takes the same width: 20-200, 200-2k, 2k-20k.
            float a = FrequencyResponseMath.PositionOf(200, 20, 20000);
            float b = FrequencyResponseMath.PositionOf(2000, 20, 20000);
            Assert.Equal(1f / 3f, a, 4);
            Assert.Equal(2f / 3f, b, 4);
            Assert.Equal(1000f, FrequencyResponseMath.FrequencyAt(FrequencyResponseMath.PositionOf(1000, 20, 20000), 20, 20000), 1);
        }

        [Fact]
        public void ZeroDbIsTheMiddleAndTheScaleIsInsetFromTheEdges()
        {
            Assert.Equal(H / 2f, FrequencyResponseMath.YOf(0, 12, H, 10), 3);
            Assert.Equal(10f, FrequencyResponseMath.YOf(12, 12, H, 10), 3);
            Assert.Equal(H - 10f, FrequencyResponseMath.YOf(-12, 12, H, 10), 3);
            Assert.Equal(6f, FrequencyResponseMath.DbAt(FrequencyResponseMath.YOf(6, 12, H, 10), 12, H, 10), 3);
        }

        [Fact]
        public void TheScaleIsTheSmallestThatHoldsEveryGain()
        {
            float[] ranges = { 12, 18, 24, 30 };
            var nodes = new List<ResponseNode> { new() { Db = 3 }, new() { Db = -12 } };
            Assert.Equal(12f, FrequencyResponseMath.ChooseRange(ranges, nodes));

            nodes.Add(new ResponseNode { Db = 20 });
            Assert.Equal(24f, FrequencyResponseMath.ChooseRange(ranges, nodes));

            // An off band and a band with no gain do not stretch the scale.
            nodes[2].Enabled = false;
            nodes.Add(new ResponseNode { Db = 29, MovesY = false });
            Assert.Equal(12f, FrequencyResponseMath.ChooseRange(ranges, nodes));
        }

        [Fact]
        public void AFittedCurveStretchesTheScaleForItsPeak_NotForItsCut()
        {
            float[] ranges = { 12, 18, 24, 30 };
            var nodes = new List<ResponseNode> { new() { Db = 3 } };
            Assert.Equal(12f, FrequencyResponseMath.ChooseRange(ranges, nodes, new[] { 0f, -60f, -120f }));
            Assert.Equal(24f, FrequencyResponseMath.ChooseRange(ranges, nodes, new[] { 0f, -21f, 0f }));
            Assert.Equal(12f, FrequencyResponseMath.ChooseRange(ranges, nodes, new[] { 0f, -3f, -21f, -29f, -60f }));
            Assert.Equal(18f, FrequencyResponseMath.ChooseRange(ranges, nodes, new[] { 0f, 16f, -60f }));
            Assert.Equal(12f, FrequencyResponseMath.ChooseRange(ranges, nodes));
        }

        [Fact]
        public void DraggingANodeMovesItsFrequencyAndGain()
        {
            var node = new ResponseNode { Hz = 1000, Db = 0 };
            FrequencyResponseElement e = Editor(node);
            (int Index, float Hz, float Db, float Q) edited = (-1, 0, 0, 0);
            int completed = -1;
            e.NodeEdited = (i, hz, db, q) => edited = (i, hz, db, q);
            e.NodeEditCompleted = i => completed = i;

            Assert.True(e.PressAt(At(e, 1000, 0), 1, false));
            e.DragTo(At(e, 4000, 6), false);
            Assert.True(e.ReleaseDrag());

            Assert.Equal(0, edited.Index);
            Assert.Equal(4000f, edited.Hz, 0);
            Assert.Equal(6f, edited.Db, 2);
            Assert.Equal(1f, edited.Q);
            Assert.Equal(0, completed);
            Assert.Equal(0, e.SelectedIndex);
        }

        [Fact]
        public void ACutsVerticalDragIsItsQAndAltDragIsAnyNodesQ()
        {
            var cut = new ResponseNode { Hz = 100, MovesY = false, Q = 1 };
            FrequencyResponseElement e = Editor(cut);
            e.PressAt(At(e, 100, 0), 1, false);
            e.DragTo(At(e, 100, 0) - new Vector2(0, 60), false);
            e.ReleaseDrag();
            Assert.Equal(2f, cut.Q, 3);
            Assert.Equal(100f, cut.Hz, 1);

            var bell = new ResponseNode { Hz = 1000, Db = 3, Q = 1 };
            e = Editor(bell);
            e.PressAt(At(e, 1000, 3), 1, true);
            e.DragTo(At(e, 1000, 3) + new Vector2(80, 60), false);
            e.ReleaseDrag();
            Assert.Equal(0.5f, bell.Q, 3);
            Assert.Equal(3f, bell.Db);
            Assert.Equal(1000f, bell.Hz);
        }

        [Fact]
        public void AFixedFrequencyNodeOnlyMovesUpAndDown()
        {
            var mid = new ResponseNode { Hz = 700, Db = 0, MovesX = false };
            FrequencyResponseElement e = Editor(mid);
            e.PressAt(At(e, 700, 0), 1, false);
            e.DragTo(At(e, 3000, -6), false);
            e.ReleaseDrag();
            Assert.Equal(700f, mid.Hz);
            Assert.Equal(-6f, mid.Db, 2);
        }

        [Fact]
        public void TheWheelOverANodeTurnsItsQ()
        {
            var bell = new ResponseNode { Hz = 1000, Db = 3, Q = 1 };
            FrequencyResponseElement e = Editor(bell);
            e.HoverAt(At(e, 1000, 3));
            for (int i = 0; i < 8; i++)
            {
                e.Wheel(1);
            }

            Assert.Equal(2f, bell.Q, 3);
        }

        [Fact]
        public void DoubleClickResetsAndPressingEmptySpaceAdds()
        {
            var bell = new ResponseNode { Hz = 1000, Db = 3 };
            FrequencyResponseElement e = Editor(bell);
            int reset = -1;
            e.NodeReset = i => reset = i;
            Assert.False(e.PressAt(At(e, 1000, 3), 2, false));
            Assert.Equal(0, reset);

            var added = new ResponseNode { Enabled = false };
            e.Nodes.Add(added);
            (float Hz, float Db) at = (0, 0);
            e.AddNode = (hz, db) =>
            {
                at = (hz, db);
                added.Enabled = true;
                added.Hz = hz;
                added.Db = db;
                return 1;
            };

            Assert.True(e.PressAt(At(e, 150, -4), 1, false));
            Assert.Equal(150f, at.Hz, 0);
            Assert.Equal(-4f, at.Db, 2);
            Assert.Equal(1, e.SelectedIndex);

            // ...and the same press drags the new node.
            e.DragTo(At(e, 300, -4), false);
            Assert.Equal(300f, added.Hz, 0);
        }

        [Fact]
        public void AReadOnlyEditorIgnoresEveryGesture()
        {
            var bell = new ResponseNode { Hz = 1000, Db = 3, Q = 1 };
            FrequencyResponseElement e = Editor(bell);
            e.ReadOnly = true;
            Assert.False(e.PressAt(At(e, 1000, 3), 1, false));
            e.DragTo(At(e, 2000, 6), false);
            e.HoverAt(At(e, 1000, 3));
            e.Wheel(4);
            Assert.Equal(1000f, bell.Hz);
            Assert.Equal(3f, bell.Db);
            Assert.Equal(1f, bell.Q);
        }
    }
}
