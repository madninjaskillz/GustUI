using GustUI.Elements;
using GustUI.Traits;
using GustUI.TraitValues;
using Pin = GustUI.Elements.ModalWindowElement.WindowPin;

namespace GustUI.Tests
{
    /// <summary>
    /// Every window shares one stack (ezmuze #301): the window clicked or
    /// opened last is on top, whatever kind of window it is, the sequencer
    /// included. Windows clamp to one depth per pin group, so within a group
    /// the tie is broken by which was brought forward last. A pin splits the
    /// stack into back-pinned &lt; normal &lt; front-pinned.
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

        private static Element Child(Element parent, string name, Pin pin = Pin.Normal)
        {
            var child = new Element { Depth = Raise(pin) };
            parent.AddChild(child, name);
            return child;
        }

        private static int Raise(Pin pin = Pin.Normal, int? pool = AppPoolMax, int? ceiling = null)
            => Element.FrontDepth(pool, ModalWindowElement.StackFloor(pin),
                ModalWindowElement.StackCeiling(pin, ceiling));

        /// <summary>Brings a window forward the way a click does.</summary>
        private static void Click(Element window, Pin pin = Pin.Normal)
        {
            window.Depth = Raise(pin);
            window.MarkBroughtForward();
        }

        [Fact]
        public void TheWindowBroughtForwardLastDrawsOnTopOfItsTie()
        {
            Element root = Parent();
            Element first = Child(root, "first");
            Element second = Child(root, "second");

            // Insertion order alone: the later one is on top.
            Assert.Same(second, root.Children.Items[^1]);

            // Clicking the one behind raises it, though its depth is unchanged.
            Click(first);
            Assert.Same(first, root.Children.Items[^1]);

            Click(second);
            Assert.Same(second, root.Children.Items[^1]);
        }

        [Fact]
        public void EachPinGroupLandsOnItsOwnDepthWhateverThePool()
        {
            foreach (int? pool in new int?[] { null, 0, 1, 50000, 59997, 59999, 1000000 })
            {
                Assert.Equal(ModalWindowElement.BackPinnedWindowDepth, Raise(Pin.Back, pool));
                Assert.Equal(ModalWindowElement.WindowDepth, Raise(Pin.Normal, pool));
                Assert.Equal(ModalWindowElement.FrontPinnedWindowDepth, Raise(Pin.Front, pool));
            }

            Assert.True(Raise(Pin.Back) < Raise(Pin.Normal));
            Assert.True(Raise(Pin.Normal) < Raise(Pin.Front));
            Assert.True(Raise(Pin.Front) < ModalWindowElement.ModalDepth);
        }

        [Fact]
        public void TheSequencerClickedComesAboveAFloatingPanel()
        {
            // The user's rule: treat the sequencer like everything else.
            Element root = Parent();
            Element sequencer = Child(root, "sequencer");
            Element panel = Child(root, "panel");

            Click(sequencer);

            Assert.Same(sequencer, root.Children.Items[^1]);
        }

        [Fact]
        public void APanelClickedBehindADialogComesAboveIt()
        {
            Element root = Parent();
            Element panel = Child(root, "panel");
            Element fileBrowser = Child(root, "file-browser");

            Click(panel);
            Assert.Same(panel, root.Children.Items[^1]);

            Click(fileBrowser);
            Assert.Same(fileBrowser, root.Children.Items[^1]);
        }

        [Fact]
        public void AFrontPinnedPanelStaysAboveTheSequencerWhenItIsClicked()
        {
            Element root = Parent();
            Element panel = Child(root, "panel", Pin.Front);
            Element sequencer = Child(root, "sequencer");

            Click(sequencer);

            Assert.Same(panel, root.Children.Items[^1]);
        }

        [Fact]
        public void ABackPinnedWindowClickedStaysBelowANormalOne()
        {
            Element root = Parent();
            Element normal = Child(root, "normal");
            Element pinnedBack = Child(root, "pinned-back", Pin.Back);

            Click(pinnedBack, Pin.Back);

            Assert.Same(normal, root.Children.Items[^1]);
            Assert.Same(pinnedBack, root.Children.Items[0]);
        }

        [Fact]
        public void UnpinningRestoresClickOrder()
        {
            Element root = Parent();
            Element pinned = Child(root, "pinned", Pin.Front);
            Element other = Child(root, "other");
            Click(other);
            Assert.Same(pinned, root.Children.Items[^1]);

            // Unpinned: the window keeps its place in the order until clicked...
            pinned.Depth = Raise(Pin.Normal);
            Click(other);
            Assert.Same(other, root.Children.Items[^1]);

            // ...and a click raises it like any other.
            Click(pinned);
            Assert.Same(pinned, root.Children.Items[^1]);
        }

        [Fact]
        public void PopupsAndTooltipsStayAboveEveryWindowEvenFrontPinned()
        {
            const int popupDepth = 500000;
            const int tooltipDepth = 1000000;
            Element root = Parent();
            Element popup = new Element { Depth = popupDepth };
            root.AddChild(popup, "popup");
            Element tooltip = new Element { Depth = tooltipDepth };
            root.AddChild(tooltip, "tooltip");
            Element panel = Child(root, "panel", Pin.Front);

            Click(panel, Pin.Front);

            Assert.Same(tooltip, root.Children.Items[^1]);
            Assert.Same(popup, root.Children.Items[^2]);
        }

        [Fact]
        public void AnExplicitCeilingWinsOverTheFloor()
        {
            Assert.Equal(42, Raise(pool: 1000000, ceiling: 42));
        }

        // Two questions, two answers (ezmuze #358, closed as intended). FRONT
        // is the window drawn on top, pins included: dialog keys and presses
        // go there. ACTIVE is the window clicked or activated last: the lit
        // title bar, the OS title and the keyboard follow it, pins or not --
        // as an always-on-top window on Windows stays on top but dims when
        // you click the window under it.

        [Fact]
        public void AFrontPinnedWindowStaysInFrontButDimsWhenAWindowUnderItIsClicked()
        {
            Element root = Parent();
            Element pinned = Child(root, "pinned", Pin.Front);
            Element sequencer = Child(root, "sequencer");
            Click(pinned, Pin.Front);

            Click(sequencer);

            Assert.Same(pinned, ModalWindowElement.FrontWindow(root.Children.Items));
            Assert.Same(sequencer, ModalWindowElement.ActiveWindow(root.Children.Items));
            Assert.True(ModalWindowElement.IsActiveAmong(sequencer, root.Children.Items));
            Assert.False(ModalWindowElement.IsActiveAmong(pinned, root.Children.Items));

            // Clicking the pinned window again makes it active once more.
            Click(pinned, Pin.Front);
            Assert.Same(pinned, ModalWindowElement.ActiveWindow(root.Children.Items));
            Assert.True(ModalWindowElement.IsActiveAmong(pinned, root.Children.Items));
        }

        [Fact]
        public void WithinAPinGroupTheWindowClickedLastIsBothFrontAndActive()
        {
            Element root = Parent();
            Element first = Child(root, "first");
            Element second = Child(root, "second");

            Click(first);

            Assert.Same(first, ModalWindowElement.FrontWindow(root.Children.Items));
            Assert.True(ModalWindowElement.IsActiveAmong(first, root.Children.Items));
            Assert.False(ModalWindowElement.IsActiveAmong(second, root.Children.Items));
        }

        [Fact]
        public void FrontIsAlwaysTheWindowDrawnLastAndActiveTheOneClickedLast()
        {
            Element root = Parent();
            Element back = Child(root, "back", Pin.Back);
            Element normal = Child(root, "normal");
            Element front = Child(root, "front", Pin.Front);

            foreach (Element clicked in new[] { back, normal, front, normal, back })
            {
                Click(clicked, clicked == back ? Pin.Back : clicked == front ? Pin.Front : Pin.Normal);

                Assert.Same(root.Children.Items[^1], ModalWindowElement.FrontWindow(root.Children.Items));
                Assert.Same(clicked, ModalWindowElement.ActiveWindow(root.Children.Items));
                Assert.True(ModalWindowElement.IsActiveAmong(clicked, root.Children.Items));
            }
        }
    }
}
