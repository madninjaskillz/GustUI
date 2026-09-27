using GustUI.Elements;
using GustUI.Traits;
using GustUI.TraitValues;

namespace GustUI.Tests
{
    /// <summary>
    /// ezmuze #383: a maximised window being dragged when a waiting dialog
    /// opened came back at its restored size, not maximised. Its title bar held
    /// the drag bar twice, so one press ran the title-bar press handler twice:
    /// the first pulled the window out of maximised, and the second recorded
    /// that un-maximised state as where the drag began.
    /// </summary>
    public class MaximisedDragCancelTests
    {
        private static Element Parent()
        {
            var parent = new Element();
            parent.AddTrait<ChildrenTrait>().Set(new TVElements());
            return parent;
        }

        [Fact]
        public void AChildAddedAgainUnderItsFinalNameIsHeldOnce()
        {
            // The title bar's pattern: created through AddChildElement, then
            // given its final name in Setup.
            Element bar = Parent();
            Element drag = bar.AddChildElement<Element>("drag bar");

            bar.AddOrMoveChild(drag, "titleText");

            Assert.Single(bar.Children.Items);
            Assert.Equal("titleText", drag.ElementName);
            Assert.Same(drag, bar.Children.Get("titleText"));
        }

        [Fact]
        public void MovingAChildPutsItLastAmongItsSiblings()
        {
            // The second add used to decide the draw and hit order, so the
            // move keeps that: the child ends up after everything added since.
            Element bar = Parent();
            Element drag = bar.AddChildElement<Element>("drag bar");
            Element close = bar.AddChildElement<Element>("close button");

            bar.AddOrMoveChild(drag, "titleText");

            Assert.Equal(new[] { close, drag }, bar.Children.Items);
        }

        [Fact]
        public void AChildNotYetHeldIsSimplyAdded()
        {
            Element bar = Parent();
            var drag = new Element();

            bar.AddOrMoveChild(drag, "titleText");

            Assert.Single(bar.Children.Items);
            Assert.Same(bar, drag.Parent);
        }

        [Fact]
        public void TheFirstPressOfADragRecordsWhereItBegan()
        {
            Assert.True(ModalWindowElement.StartsTitleDrag(beingDragged: false, originRecorded: false));

            // A stale origin from a drag that ended without a release through
            // the title bar does not block the next one.
            Assert.True(ModalWindowElement.StartsTitleDrag(beingDragged: false, originRecorded: true));
        }

        [Fact]
        public void ASecondCallForTheSamePressLeavesTheOriginAlone()
        {
            // By the second call the first has restored the window, so what it
            // would record is "not maximised".
            Assert.False(ModalWindowElement.StartsTitleDrag(beingDragged: true, originRecorded: true));
        }
    }
}
