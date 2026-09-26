using System.Collections.Generic;
using GustUI.Elements;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Tests
{
    /// <summary>
    /// A drag merges only into a title bar you can see (ezmuze #357): the
    /// topmost window under the pointer decides, and a window buried behind
    /// another is never a target.
    /// </summary>
    public class MergeTargetTests
    {
        private const float TitleBar = 30f;

        private readonly Dictionary<Element, (Vector2, Vector2)> rects = new();
        private readonly HashSet<Element> tabable = new();
        private readonly Element root;

        public MergeTargetTests()
        {
            root = new Element();
            root.AddTrait<ChildrenTrait>().Set(new TVElements());
        }

        private Element Window(string name, float x, float y, float w, float h, bool acceptsTabs = true)
        {
            var window = new Element { Depth = ModalWindowElement.WindowDepth };
            root.AddChild(window, name);
            rects[window] = (new Vector2(x, y), new Vector2(w, h));
            if (acceptsTabs)
            {
                tabable.Add(window);
            }

            return window;
        }

        private Element TargetAt(Element self, float x, float y)
            => ModalWindowElement.MergeTargetAt(root.Children.Items, self, new Vector2(x, y),
                w => rects.ContainsKey(w), w => tabable.Contains(w), w => rects[w], TitleBar);

        [Fact]
        public void AVisibleTitleBarUnderThePointerIsTheTarget()
        {
            Window("sequencer", 0, 0, 2560, 1345);
            Element panel = Window("panel", 820, 385, 920, 598);
            Element dragged = Window("dragged", 1300, 390, 400, 300);

            Assert.Same(panel, TargetAt(dragged, 1000, 400));
        }

        [Fact]
        public void AWindowBuriedBehindTheSequencerIsNeverATarget()
        {
            Window("panel", 820, 385, 920, 598);
            Element sequencer = Window("sequencer", 0, 0, 2560, 1345);
            sequencer.MarkBroughtForward();
            Element dragged = Window("dragged", 1300, 390, 400, 300);
            dragged.MarkBroughtForward();

            // The panel's title bar is under the pointer, but the sequencer
            // covers it there, and the pointer is not on the sequencer's bar.
            Assert.Null(TargetAt(dragged, 1000, 400));
        }

        [Fact]
        public void TheTopmostOfTwoOverlappingTitleBarsWins()
        {
            Element lower = Window("lower", 800, 400, 600, 400);
            Element upper = Window("upper", 1000, 400, 600, 400);
            Element dragged = Window("dragged", 0, 0, 100, 100);

            Assert.Same(upper, TargetAt(dragged, 1100, 410));

            lower.MarkBroughtForward();
            Assert.Same(lower, TargetAt(dragged, 1100, 410));
        }

        [Fact]
        public void TheDraggedWindowItselfIsPassedOver()
        {
            Element panel = Window("panel", 820, 385, 920, 598);
            Element dragged = Window("dragged", 800, 380, 920, 598);

            Assert.Same(panel, TargetAt(dragged, 1000, 400));
        }

        [Fact]
        public void AWindowThatTakesNoTabsBlocksWhatIsBehindIt()
        {
            Window("panel", 820, 385, 920, 598);
            Window("dialog", 900, 390, 400, 300, acceptsTabs: false);
            Element dragged = Window("dragged", 0, 0, 100, 100);

            Assert.Null(TargetAt(dragged, 1000, 400));
        }

        [Fact]
        public void BelowTheTitleBarIsNoTarget()
        {
            Window("panel", 820, 385, 920, 598);
            Element dragged = Window("dragged", 0, 0, 100, 100);

            Assert.Null(TargetAt(dragged, 1000, 385 + TitleBar + 5));
        }

        [Fact]
        public void AHiddenWindowIsPassedOver()
        {
            Element panel = Window("panel", 820, 385, 920, 598);
            Element hidden = Window("hidden", 820, 385, 920, 598);
            hidden.Visible = false;
            Element dragged = Window("dragged", 0, 0, 100, 100);

            Assert.Same(panel, TargetAt(dragged, 1000, 400));
        }
    }
}
