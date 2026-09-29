using GustUI.Managers;
using Microsoft.Xna.Framework.Input;

namespace GustUI.Tests
{
    /// <summary>
    /// ezmuze #563: while the window is inactive the pointer is frozen where it
    /// was last processed. That position is already in the tree's divided
    /// space, so it must not be divided by MouseScale again; it was, every
    /// frame, and at 150 % scaling the frozen pointer slid into the corner and
    /// hovered whatever sat at (0,0).
    /// </summary>
    public class InactivePointerTests
    {
        private static MouseState At(int x, int y, bool left = false)
            => new MouseState(x, y, 0,
                left ? ButtonState.Pressed : ButtonState.Released,
                ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released);

        [Fact]
        public void AnInactiveWindowsPointerStaysPutFrameAfterFrame()
        {
            MouseState pointer = At(900, 600);
            for (int frame = 0; frame < 120; frame++)
            {
                pointer = InputManager.PointerForFrame(null, At(3000, 2000), realInputReaches: false, pointer, 0, 1.5f);
            }

            Assert.Equal(900, pointer.X);
            Assert.Equal(600, pointer.Y);
        }

        [Fact]
        public void TheFrozenPointerHasItsButtonsUp()
        {
            MouseState frozen = InputManager.PointerForFrame(null, At(10, 10, left: true), realInputReaches: false, At(40, 50, left: true), 7, 1.5f);

            Assert.Equal(ButtonState.Released, frozen.LeftButton);
            Assert.Equal(7, frozen.ScrollWheelValue);
            Assert.Equal(40, frozen.X);
        }

        [Fact]
        public void RealInputIsStillDividedIntoTheTreesSpace()
        {
            MouseState real = InputManager.PointerForFrame(null, At(900, 600), realInputReaches: true, At(0, 0), 0, 1.5f);

            Assert.Equal(600, real.X);
            Assert.Equal(400, real.Y);
        }

        [Fact]
        public void SyntheticInputIsUsedAsGiven()
        {
            MouseState synthetic = InputManager.PointerForFrame(At(123, 45), At(900, 600), realInputReaches: false, At(0, 0), 0, 1.5f);

            Assert.Equal(123, synthetic.X);
            Assert.Equal(45, synthetic.Y);
        }
    }
}
