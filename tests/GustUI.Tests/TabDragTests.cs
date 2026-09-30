using GustUI.Elements;
using Microsoft.Xna.Framework;
using Xunit;

namespace GustUI.Tests
{
    /// <summary>
    /// A tab dragged sideways reorders, dragged 2x its height up or down tears
    /// off into its own window, and the window it arrives in lands under the
    /// pointer, holding its title bar (ezmuze #597).
    /// </summary>
    public class TabDragTests
    {
        private const float Bar = ModalTitleBarElement.BarHeight;

        [Fact]
        public void AStillPressIsJustAClick()
            => Assert.Equal(ModalWindowElement.TabDragIntent.None,
                ModalWindowElement.ClassifyTabDrag(new Vector2(3, 3), false, Bar, 2));

        [Fact]
        public void SidewaysReorders()
            => Assert.Equal(ModalWindowElement.TabDragIntent.Reorder,
                ModalWindowElement.ClassifyTabDrag(new Vector2(-8, 10), false, Bar, 2));

        [Fact]
        public void AReorderStaysOneWhenThePointerComesBack()
            => Assert.Equal(ModalWindowElement.TabDragIntent.Reorder,
                ModalWindowElement.ClassifyTabDrag(new Vector2(1, 0), true, Bar, 2));

        [Fact]
        public void TwoTabHeightsUpOrDownTearsOff()
        {
            Assert.Equal(ModalWindowElement.TabDragIntent.TearOff,
                ModalWindowElement.ClassifyTabDrag(new Vector2(0, 2 * Bar), false, Bar, 2));
            Assert.Equal(ModalWindowElement.TabDragIntent.TearOff,
                ModalWindowElement.ClassifyTabDrag(new Vector2(40, -2 * Bar), true, Bar, 3));
            Assert.NotEqual(ModalWindowElement.TabDragIntent.TearOff,
                ModalWindowElement.ClassifyTabDrag(new Vector2(0, (2 * Bar) - 1), false, Bar, 2));
        }

        [Fact]
        public void ALoneTabHasNoWindowToLeave()
            => Assert.NotEqual(ModalWindowElement.TabDragIntent.TearOff,
                ModalWindowElement.ClassifyTabDrag(new Vector2(0, 5 * Bar), false, Bar, 1));

        [Fact]
        public void ATabMovesOneSlotAsItPassesANeighboursMiddle()
        {
            var widths = new[] { 100f, 100f, 100f };

            // The first tab (centre 50) dragged: short of the second's centre
            // (154) it stays, past it it takes that slot, past the third's
            // (258) it goes last.
            Assert.Equal(0, ModalWindowElement.ReorderTarget(widths, 4f, 0, 150f));
            Assert.Equal(1, ModalWindowElement.ReorderTarget(widths, 4f, 0, 160f));
            Assert.Equal(2, ModalWindowElement.ReorderTarget(widths, 4f, 0, 300f));

            // And the last one back to the front.
            Assert.Equal(0, ModalWindowElement.ReorderTarget(widths, 4f, 2, 10f));
        }

        [Fact]
        public void AReorderedTabSitsStillInItsNewSlot()
        {
            // A wide tab dragged past a narrow one, then laid out again in its
            // new slot with the pointer where it was: it stays put.
            Assert.Equal(1, ModalWindowElement.ReorderTarget(new[] { 200f, 50f }, 0f, 0, 226f));
            Assert.Equal(1, ModalWindowElement.ReorderTarget(new[] { 50f, 200f }, 0f, 1, 226f));
        }

        [Fact]
        public void ATornOffTabIsHeldByItsTitleBarWhereItWasGrabbed()
        {
            (Vector2 position, Vector2 size) = ModalWindowElement.PlaceTornOff(
                new Vector2(500, 300), grabX: 150, new Vector2(800, 600), null, maximisedOrDocked: false);

            Assert.Equal(new Vector2(800, 600), size);
            Assert.Equal(new Vector2(350, 300 - (Bar / 2)), position);
        }

        [Fact]
        public void AGrabNearTheButtonsIsKeptOffThem()
        {
            // Grabbed 780px along an 800px window: the pointer would sit on
            // the new window's close. It is kept on the title bar instead.
            (Vector2 position, Vector2 _) = ModalWindowElement.PlaceTornOff(
                new Vector2(1000, 300), grabX: 780, new Vector2(800, 600), null, maximisedOrDocked: false);

            float along = 1000 - position.X;
            Assert.True(along <= 800 - (Bar * 4));
        }

        [Fact]
        public void AMaximisedOrDockedWindowsTabTakesItsRestoreSize()
        {
            (Vector2 _, Vector2 restored) = ModalWindowElement.PlaceTornOff(
                new Vector2(500, 20), 100, new Vector2(2000, 1200), new Vector2(900, 700), maximisedOrDocked: true);
            Assert.Equal(new Vector2(900, 700), restored);

            (Vector2 _, Vector2 fallback) = ModalWindowElement.PlaceTornOff(
                new Vector2(500, 20), 100, new Vector2(2000, 300), null, maximisedOrDocked: true);
            Assert.Equal(new Vector2(1400, 210), fallback);
        }

        [Fact]
        public void ATornOffTabIsNeverAsBigAsTheScreenItLeft()
        {
            // Restoring to nearly the whole screen: the new window still has
            // room to be dragged.
            (Vector2 _, Vector2 size) = ModalWindowElement.PlaceTornOff(
                new Vector2(500, 20), 100, new Vector2(1600, 1000), new Vector2(1580, 990), maximisedOrDocked: true);
            Assert.Equal(new Vector2(1120, 700), size);
        }
    }
}
