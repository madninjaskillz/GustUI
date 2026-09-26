using System;
using GustUI.Elements;
using Microsoft.Xna.Framework;

namespace GustUI.Tests
{
    /// <summary>
    /// A sequencer dragged off a side dock and maximised stayed where it was,
    /// over the docked explorer (ezmuze #367). Two faults: the maximise
    /// transition stopped as soon as the SIZE arrived, and a window that had
    /// been filling the free space came off a dock at that full size.
    /// </summary>
    public class MaximiseAndUndockGeometryTests
    {
        private static readonly Vector2 MinSize = new Vector2(400, 300);

        // ---- maximise ends on position as well as size ----

        [Fact]
        public void AMaximiseWhoseSizeIsAlreadyThereIsNotDoneUntilThePositionIs()
        {
            // The #367 numbers: 1860x1345 floating at 293,23, one 40% step
            // into a maximise to 0,0 at the same size.
            Vector2 size = new Vector2(1860, 1345);
            Vector2 afterOneStep = Vector2.Lerp(new Vector2(293, 23), Vector2.Zero, Element.TransitionStep);

            Assert.False(Element.TransitionArrived(size, size, afterOneStep, Vector2.Zero, dragging: false));
        }

        [Fact]
        public void ATransitionIsDoneWhenBothAreWithinAPixel()
        {
            Assert.True(Element.TransitionArrived(
                new Vector2(1859.4f, 1344.6f), new Vector2(1860, 1345),
                new Vector2(0.6f, 0.3f), Vector2.Zero, dragging: false));
        }

        [Fact]
        public void ATransitionWhoseSizeIsStillMovingIsNotDone()
        {
            Assert.False(Element.TransitionArrived(
                new Vector2(1500, 1000), new Vector2(1860, 1345),
                Vector2.Zero, Vector2.Zero, dragging: false));
        }

        [Fact]
        public void WhileDraggedThePointerOwnsThePositionSoOnlyTheSizeCounts()
        {
            // Dragging a maximised window off restores it mid-drag: the
            // position follows the pointer, never the restore target.
            Assert.True(Element.TransitionArrived(
                new Vector2(1302, 942), new Vector2(1302, 942),
                new Vector2(900, 40), new Vector2(454, 215), dragging: true));
        }

        [Fact]
        public void ThePositionAllowanceClosesAnyRealGap()
        {
            // Whatever a lerp can still be chasing once the size is there, it
            // gets within a pixel inside the allowance, so running out of it
            // means something else is holding the position.
            float remaining = 5000f * MathF.Pow(1f - Element.TransitionStep, Element.PositionSettleFrameAllowance);
            Assert.True(remaining < 1f);
        }

        // ---- the undock size of a window that was filling the free space ----

        [Fact]
        public void AWindowFillingTheFreeSpaceIsRecognised()
        {
            Vector2 free = new Vector2(1860, 1345.33f);
            Assert.True(ModalWindowElement.WasFillingFreeSpace(new Vector2(1860, 1345.33f), free));
            Assert.True(ModalWindowElement.WasFillingFreeSpace(new Vector2(1859.3f, 1344.8f), free));
            Assert.False(ModalWindowElement.WasFillingFreeSpace(new Vector2(1302, 942), free));
            Assert.False(ModalWindowElement.WasFillingFreeSpace(new Vector2(1860, 900), free));
        }

        [Fact]
        public void AFillerWithNothingRememberedComesOffAtTheShrunkDockRect()
        {
            // The sequencer docked left beside a right-docked explorer: 1280
            // wide, full height. Nothing remembered, so 0.88 of that.
            Vector2 dock = new Vector2(1280, 1345);
            Vector2 size = ModalWindowElement.OffDockSize(dock, remembered: null, MinSize, new Vector2(1860, 1345));

            Assert.Equal(dock * 0.88f, size);
        }

        [Fact]
        public void ARememberedFloatingSizeIsStillRestored()
        {
            Vector2 size = ModalWindowElement.OffDockSize(
                new Vector2(700, 1345), remembered: new Vector2(920, 598), MinSize, new Vector2(1860, 1345));

            Assert.Equal(new Vector2(920, 598), size);
        }

        [Fact]
        public void TheSizeComingOffIsCappedToTheFreeSpace()
        {
            // Remembered from a layout with more room than there is now.
            Vector2 free = new Vector2(1860, 1345);
            Vector2 size = ModalWindowElement.OffDockSize(
                new Vector2(700, 1345), remembered: new Vector2(2400, 1400), MinSize, free);

            Assert.Equal(free, size);
        }

        [Fact]
        public void TheCapNeverTakesAWindowBelowItsMinimum()
        {
            Vector2 size = ModalWindowElement.OffDockSize(
                new Vector2(700, 1345), remembered: new Vector2(920, 598), MinSize, new Vector2(300, 200));

            Assert.Equal(MinSize, size);
        }

        [Fact]
        public void ARestoreToTheSameSizeStillShrinks()
        {
            Vector2 dock = new Vector2(700, 1345);
            Vector2 size = ModalWindowElement.OffDockSize(dock, remembered: dock, MinSize, new Vector2(2560, 1345));

            Assert.Equal(dock * 0.88f, size);
        }
    }
}
