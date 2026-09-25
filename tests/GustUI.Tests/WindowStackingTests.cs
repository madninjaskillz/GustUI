using GustUI.Elements;
using GustUI.Traits;
using GustUI.TraitValues;

namespace GustUI.Tests
{
    /// <summary>
    /// Every window shares one stack (ezmuze #301): the window clicked or
    /// opened last is on top, whatever kind of window it is. Windows all clamp
    /// to one depth, so the tie is broken by which was brought forward last.
    /// The one exception is the app's backdrop (the sequencer), which stays
    /// behind every other window however often it is clicked.
    /// </summary>
    public class WindowStackingTests
    {
        // Pool max as it is in the app: tiers far above the windows (status
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

        private static int Raise(bool backdrop = false, int? pool = AppPoolMax, int? ceiling = null)
            => Element.FrontDepth(pool, ModalWindowElement.StackFloor(backdrop),
                ModalWindowElement.StackCeiling(backdrop, ceiling));

        /// <summary>Brings a window forward the way a click does.</summary>
        private static void Click(Element window, bool backdrop = false)
        {
            window.Depth = Raise(backdrop);
            window.MarkBroughtForward();
        }

        [Fact]
        public void TheWindowBroughtForwardLastDrawsOnTopOfItsTie()
        {
            Element root = Parent();
            Element first = Child(root, "first", Raise());
            Element second = Child(root, "second", Raise());

            // Insertion order alone: the later one is on top.
            Assert.Same(second, root.Children.Items[^1]);

            // Clicking the one behind raises it, though its depth is unchanged.
            Click(first);
            Assert.Same(first, root.Children.Items[^1]);

            Click(second);
            Assert.Same(second, root.Children.Items[^1]);
        }

        [Fact]
        public void EveryWindowLandsOnTheSameDepthWhateverThePool()
        {
            foreach (int? pool in new int?[] { null, 0, 1, 50000, 59998, 59999, 1000000 })
            {
                Assert.Equal(ModalWindowElement.WindowDepth, Raise(pool: pool));
                Assert.Equal(ModalWindowElement.BackdropWindowDepth, Raise(backdrop: true, pool: pool));
            }
        }

        [Fact]
        public void APanelClickedBehindADialogComesAboveIt()
        {
            // The user's rule: if I click it, it's on top. A dialog is a
            // window like any other.
            Element root = Parent();
            Element panel = Child(root, "panel", Raise());
            Element fileBrowser = Child(root, "file-browser", Raise());
            Assert.Same(fileBrowser, root.Children.Items[^1]);

            Click(panel);
            Assert.Same(panel, root.Children.Items[^1]);

            // And reopening (re-activating) the dialog brings it back.
            Click(fileBrowser);
            Assert.Same(fileBrowser, root.Children.Items[^1]);
        }

        [Fact]
        public void TheBackdropClickedLastIsStillDrawnBehindEveryWindow()
        {
            Element root = Parent();
            Element sequencer = Child(root, "sequencer", Raise(backdrop: true));
            Element panel = Child(root, "panel", Raise());
            Element maximised = Child(root, "piano-roll", Raise());

            Click(sequencer, backdrop: true);

            Assert.Same(sequencer, root.Children.Items[0]);
            Assert.True(Raise(backdrop: true) < Raise());
        }

        [Fact]
        public void AMaximisedWindowClickedComesToTheTop()
        {
            Element root = Parent();
            Element maximised = Child(root, "piano-roll", Raise());
            Element panel = Child(root, "panel", Raise());

            Click(maximised);

            Assert.Same(maximised, root.Children.Items[^1]);
        }

        [Fact]
        public void PopupsAndTooltipsStayAboveEveryWindow()
        {
            const int popupDepth = 500000;
            const int tooltipDepth = 1000000;
            Element root = Parent();
            Element popup = Child(root, "popup", popupDepth);
            Element tooltip = Child(root, "tooltip", tooltipDepth);
            Element panel = Child(root, "panel", Raise());

            Click(panel);

            Assert.Same(tooltip, root.Children.Items[^1]);
            Assert.Same(popup, root.Children.Items[^2]);
        }

        [Fact]
        public void AnExplicitCeilingWinsOverTheFloor()
        {
            Assert.Equal(42, Raise(pool: 1000000, ceiling: 42));
        }
    }
}
