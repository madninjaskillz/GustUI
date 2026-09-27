using GustUI.Elements;
using GustUI.Managers;
using Microsoft.Xna.Framework;

namespace GustUI.Tests
{
    /// <summary>
    /// ezmuze #400: with the explorer docked along the bottom at 1600x900
    /// (150%), the sequencer maximised to the 1052 x 260 free space and
    /// restored to 70% of it, 736 x 182, under its 400 x 260 minimum. Dragged
    /// to the top edge, it then docked at 182, leaving a 78 px gap above the
    /// explorer.
    /// </summary>
    public class RestoreKeepsMinimumTests
    {
        private static readonly Vector2 SequencerMin = new Vector2(400, 260);
        private static readonly Vector2 FreeBesideExplorer = new Vector2(1052, 260);

        [Fact]
        public void TheSeventyPercentRestoreIsHeldAtTheMinimum()
        {
            Vector2 size = ModalWindowElement.FloatingSize(FreeBesideExplorer * 0.7f, SequencerMin, FreeBesideExplorer);

            Assert.Equal(736.4f, size.X, 2);
            Assert.Equal(260f, size.Y, 2);
        }

        [Fact]
        public void ARememberedSizeBiggerThanTheFreeSpaceIsCapped()
        {
            Vector2 size = ModalWindowElement.FloatingSize(new Vector2(1400, 700), SequencerMin, FreeBesideExplorer);

            Assert.Equal(FreeBesideExplorer, size);
        }

        [Fact]
        public void TheMinimumWinsWhenTheFreeSpaceIsSmallerStill()
        {
            // As OffDockSize always has: the minimum is not negotiable.
            Vector2 size = ModalWindowElement.FloatingSize(new Vector2(300, 150), SequencerMin, new Vector2(350, 200));

            Assert.Equal(SequencerMin, size);
        }

        [Fact]
        public void ASizeInsideBothIsLeftAlone()
        {
            Vector2 size = ModalWindowElement.FloatingSize(new Vector2(600, 240), new Vector2(240, 160), FreeBesideExplorer);

            Assert.Equal(new Vector2(600, 240), size);
        }

        [Fact]
        public void OffADockKeepsTheSameRule()
        {
            // One rule for both ways of starting to float.
            Vector2 viaDock = ModalWindowElement.OffDockSize(
                new Vector2(700, 1345), remembered: new Vector2(300, 120), SequencerMin, new Vector2(1860, 1345));

            Assert.Equal(ModalWindowElement.FloatingSize(new Vector2(300, 120), SequencerMin, new Vector2(1860, 1345)), viaDock);
            Assert.Equal(SequencerMin, viaDock);
        }

        [Fact]
        public void ARestoreHeldAtItsMinimumIsMovedBackInsideTheFreeSpace()
        {
            // Remembered at y = 43 when it was 182 tall; at 260 it would hang
            // 43 px over the explorer docked at 260.
            Vector2 position = ModalWindowElement.FloatingPosition(
                new Vector2(186.27f, 43.18f), new Vector2(736.4f, 260f), Vector2.Zero, FreeBesideExplorer);

            Assert.Equal(186.27f, position.X, 2);
            Assert.Equal(0f, position.Y, 2);
        }

        [Fact]
        public void APlaceInsideTheFreeSpaceIsKept()
        {
            Vector2 position = ModalWindowElement.FloatingPosition(
                new Vector2(120f, 80f), new Vector2(600f, 400f), new Vector2(0f, 0f), new Vector2(1052f, 538.67f));

            Assert.Equal(new Vector2(120f, 80f), position);
        }

        [Fact]
        public void AWindowBiggerThanTheFreeSpaceStartsAtItsNearEdge()
        {
            // Its title bar is what must stay reachable.
            Vector2 position = ModalWindowElement.FloatingPosition(
                new Vector2(500f, 300f), new Vector2(400f, 300f), new Vector2(40f, 10f), new Vector2(350f, 200f));

            Assert.Equal(new Vector2(40f, 10f), position);
        }

        [Fact]
        public void ADockIsNeverThinnerThanItsOwnMinimum()
        {
            // The sequencer at 182 docked top beside the explorer (281.33 at
            // its 50% cap, held back by the sequencer's 260 + 24 status bar).
            float window = 562.6667f;
            float statusBar = 24f;
            float explorer = DockLayout.Clamp(330f, window, 260f + statusBar, 0f);

            float sequencer = DockLayout.Clamp(DockLayout.OwnFloor(182f, 260f), window, statusBar, explorer);

            Assert.Equal(260f, sequencer, 2);
            Assert.Equal(window - statusBar, sequencer + explorer, 2);
        }

        [Fact]
        public void TheMinimumStillGivesWayToTheCaps()
        {
            // Half the axis, and the room others need, still win: the minimum
            // holds only where the free space allows.
            Assert.Equal(200f, DockLayout.Clamp(DockLayout.OwnFloor(100f, 260f), 400f, 0f, 0f), 2);
            Assert.Equal(150f, DockLayout.Clamp(DockLayout.OwnFloor(100f, 260f), 600f, 450f, 0f), 2);
        }

        [Fact]
        public void ABiggerDockIsNotCutToItsMinimum()
        {
            Assert.Equal(330f, DockLayout.OwnFloor(330f, 170f));
        }

        [Fact]
        public void TheTopPreviewIsTheMinimumToo()
        {
            // The preview is where it lands: at 260, not the 182 it is floating at.
            var window = new Vector2(1052f, 562.6667f);
            var preview = DockLayout.PreviewRect(DockSide.Top, window, 182f, 0f, 0f, 0f, 0f, 24f,
                minAlong: 400f, opposite: 278.6667f, minAcross: 260f);

            Assert.Equal(0f, preview.Position.Y);
            Assert.Equal(260f, preview.Size.Y, 2);
        }
    }
}
