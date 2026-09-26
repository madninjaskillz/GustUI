using GustUI.Elements;
using Microsoft.Xna.Framework;

namespace GustUI.Tests
{
    /// <summary>
    /// A popped-out tab lands from the window it left, not from the pointer
    /// (ezmuze #346): one title bar down and right of a floating window,
    /// maximised over a maximised one, and floating off the corner of a
    /// docked one.
    /// </summary>
    public class PopOutPlacementTests
    {
        private static readonly Vector2 Offset = new Vector2(ModalWindowElement.PopOutOffset);

        [Fact]
        public void TheOffsetIsOneTitleBar()
        {
            Assert.Equal(ModalTitleBarElement.BarHeight, ModalWindowElement.PopOutOffset);
        }

        [Fact]
        public void AFloatingWindowsTabLandsOffsetFromItAtTheSameSize()
        {
            var placement = ModalWindowElement.PlacePoppedOut(
                new Vector2(820, 385), new Vector2(920, 598), maximised: false, docked: false);

            Assert.Equal(new Vector2(820, 385) + Offset, placement.Position);
            Assert.Equal(new Vector2(920, 598), placement.Size);
            Assert.False(placement.Maximised);
        }

        [Fact]
        public void AMaximisedWindowsTabPopsOutMaximisedOverIt()
        {
            var placement = ModalWindowElement.PlacePoppedOut(
                Vector2.Zero, new Vector2(2560, 1345), maximised: true, docked: false);

            Assert.True(placement.Maximised);
            Assert.Equal(Vector2.Zero, placement.Position);
            Assert.Equal(new Vector2(2560, 1345), placement.Size);
        }

        [Fact]
        public void ADockedWindowsTabFloatsOffTheDockedCorner()
        {
            var placement = ModalWindowElement.PlacePoppedOut(
                new Vector2(0, 1015), new Vector2(2560, 330), maximised: false, docked: true);

            Assert.False(placement.Maximised);
            Assert.Equal(new Vector2(0, 1015) + Offset, placement.Position);
        }
    }
}
