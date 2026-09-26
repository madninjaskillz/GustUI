using GustUI.Elements;
using GustUI.Traits;
using GustUI.TraitValues;

namespace GustUI.Tests
{
    /// <summary>
    /// "Drawn on top" and "has the keyboard" are separate (ezmuze, 2026-09-26):
    /// a window can be RAISED without being ACTIVATED. A channel-header click
    /// in the sequencer retargets the floating Stack; the Stack must come up
    /// over the sequencer the click raised, while the sequencer, which was
    /// clicked, keeps the keys and the lit title bar.
    /// </summary>
    public class RaiseWithoutActivatingTests
    {
        private static Element Parent()
        {
            var parent = new Element();
            parent.AddTrait<ChildrenTrait>().Set(new TVElements());
            return parent;
        }

        private static Element Child(Element parent, string name)
        {
            var child = new Element { Depth = ModalWindowElement.WindowDepth };
            parent.AddChild(child, name);
            return child;
        }

        [Fact]
        public void ARaisedWindowDrawsOnTopButTheClickedOneStaysActive()
        {
            Element root = Parent();
            Element stack = Child(root, "stack");
            Element sequencer = Child(root, "sequencer");

            // The click lands on the sequencer: it comes forward and is active.
            sequencer.MarkBroughtForward();
            Assert.Same(sequencer, root.Children.Items[^1]);
            Assert.Same(sequencer, ModalWindowElement.ActiveWindow(root.Children.Items));

            // The Stack it retargeted is raised without activating.
            stack.MarkRaised();
            Assert.Same(stack, root.Children.Items[^1]);
            Assert.Same(stack, ModalWindowElement.FrontWindow(root.Children.Items));
            Assert.Same(sequencer, ModalWindowElement.ActiveWindow(root.Children.Items));
            Assert.True(ModalWindowElement.IsActiveAmong(sequencer, root.Children.Items));
            Assert.False(ModalWindowElement.IsActiveAmong(stack, root.Children.Items));
        }

        [Fact]
        public void ClickingTheRaisedWindowActivatesIt()
        {
            Element root = Parent();
            Element stack = Child(root, "stack");
            Element sequencer = Child(root, "sequencer");

            sequencer.MarkBroughtForward();
            stack.MarkRaised();
            stack.MarkBroughtForward();

            Assert.Same(stack, root.Children.Items[^1]);
            Assert.Same(stack, ModalWindowElement.ActiveWindow(root.Children.Items));
        }

        [Fact]
        public void ClickingTheOtherWindowAfterARaiseBringsItBackOver()
        {
            Element root = Parent();
            Element stack = Child(root, "stack");
            Element sequencer = Child(root, "sequencer");

            stack.MarkRaised();
            sequencer.MarkBroughtForward();

            Assert.Same(sequencer, root.Children.Items[^1]);
            Assert.Same(sequencer, ModalWindowElement.FrontWindow(root.Children.Items));
        }
    }
}
