using GustUI.Elements;
using GustUI.Traits;
using GustUI.TraitValues;
using Band = GustUI.Elements.ModalWindowElement.StackingBand;

namespace GustUI.Tests
{
    /// <summary>
    /// The window you click comes to the front (ezmuze #301). Windows in one
    /// band all clamp to one depth, so the tie between them is broken by which
    /// was brought forward last; a window that fills the available space (the
    /// sequencer) sits in a band below every floating view however often it is
    /// clicked; and a dialog stays above both.
    /// </summary>
    public class WindowStackingTests
    {
        // Pool max as it is in the app: tiers far above the modal band (status
        // bar, popups) are always present.
        private const int AppPoolMax = 100000;

        private static Element Parent()
        {
            var parent = new Element();
            parent.AddTrait<ChildrenTrait>().Set(new TVElements());
            return parent;
        }

        private static Element Child(Element parent, string name, int depth)
        {
            var child = new Element { Depth = depth };
            parent.AddChild(child, name);
            return child;
        }

        private static int Raise(Band band, int? pool = AppPoolMax, bool floatAbove = false, int? ceiling = null)
            => Element.FrontDepth(pool, ModalWindowElement.BandFloor(band),
                ModalWindowElement.BandCeiling(band, floatAbove, ceiling));

        [Fact]
        public void TheWindowBroughtForwardLastDrawsOnTopOfItsTie()
        {
            Element root = Parent();
            Element first = Child(root, "first", ModalWindowElement.ViewWindowDepth);
            Element second = Child(root, "second", ModalWindowElement.ViewWindowDepth);

            // Insertion order alone: the later one is on top.
            Assert.Same(second, root.Children.Items[^1]);

            // Clicking the one behind raises it, though its depth is unchanged.
            first.MarkBroughtForward();
            Assert.Same(first, root.Children.Items[^1]);

            second.MarkBroughtForward();
            Assert.Same(second, root.Children.Items[^1]);
        }

        [Fact]
        public void DepthStillOutranksBeingBroughtForward()
        {
            Element root = Parent();
            Element dialog = Child(root, "dialog", ModalWindowElement.DialogWindowDepth);
            Element panel = Child(root, "panel", ModalWindowElement.ViewWindowDepth);

            panel.MarkBroughtForward();

            Assert.Same(dialog, root.Children.Items[^1]);
        }

        [Fact]
        public void AWindowThatFillsTheSpaceStaysBehindFloatingWindows()
        {
            Assert.True(Raise(Band.Background) < Raise(Band.View));

            // However low or high the pool, each band lands on its own depth:
            // clicking the sequencer cannot lift it past a view, and a view
            // never sinks below it.
            foreach (int? pool in new int?[] { null, 0, 1, 50000, 59997, 59999, 1000000 })
            {
                Assert.Equal(ModalWindowElement.BackgroundWindowDepth, Raise(Band.Background, pool));
                Assert.Equal(ModalWindowElement.ViewWindowDepth, Raise(Band.View, pool));
                Assert.Equal(ModalWindowElement.DialogWindowDepth, Raise(Band.Dialog, pool));
            }
        }

        [Fact]
        public void TheSequencerClickedLastIsStillDrawnBehindAPanel()
        {
            Element root = Parent();
            Element sequencer = Child(root, "sequencer", Raise(Band.Background));
            Element panel = Child(root, "panel", Raise(Band.View));

            sequencer.MarkBroughtForward();

            Assert.Same(panel, root.Children.Items[^1]);
        }

        [Fact]
        public void ADialogStaysAboveViewsClickedAfterIt()
        {
            Element root = Parent();
            Element panel = Child(root, "panel", Raise(Band.View));
            Element dialog = Child(root, "file-browser", Raise(Band.Dialog));

            // The panel behind the dialog is clicked.
            panel.Depth = Raise(Band.View);
            panel.MarkBroughtForward();

            Assert.Same(dialog, root.Children.Items[^1]);
        }

        [Fact]
        public void AFloatAboveDialogStaysAboveEveryBand()
        {
            int floatAbove = Raise(Band.Dialog, floatAbove: true);
            Assert.True(floatAbove >= ModalWindowElement.ModalDepth);
            Assert.True(floatAbove > Raise(Band.Dialog));

            // And a view clicked after it opened stays under it.
            Assert.True(Raise(Band.View, pool: floatAbove) < floatAbove);
        }

        [Fact]
        public void AnExplicitCeilingWinsOverTheFloor()
        {
            Assert.Equal(42, Raise(Band.View, pool: 1000000, ceiling: 42));
        }
    }
}
