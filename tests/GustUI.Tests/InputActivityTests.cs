using GustUI.Managers;
using Microsoft.Xna.Framework.Input;

namespace GustUI.Tests
{
    /// <summary>
    /// InputManager.TimeSinceInput (2026-10-08): work that must never land
    /// mid-gesture (ezmuze's settle-time garbage collection) waits for it. A
    /// pointer that moved, a wheel that turned, and anything HELD — a drag
    /// paused in place, a key down — are all somebody using the window.
    /// </summary>
    public class InputActivityTests
    {
        private static MouseState Mouse(int x = 10, int y = 10, int wheel = 0, bool left = false, bool right = false)
            => new MouseState(x, y, wheel,
                left ? ButtonState.Pressed : ButtonState.Released,
                ButtonState.Released,
                right ? ButtonState.Pressed : ButtonState.Released,
                ButtonState.Released, ButtonState.Released);

        private static readonly KeyboardState NoKeys = default;

        [Fact]
        public void NothingChangedAndNothingHeldIsIdle()
            => Assert.False(InputManager.IsActivity(Mouse(), Mouse(), NoKeys, NoKeys));

        [Fact]
        public void MovingThePointerIsUse()
            => Assert.True(InputManager.IsActivity(Mouse(), Mouse(x: 11), NoKeys, NoKeys));

        [Fact]
        public void TurningTheWheelIsUse()
            => Assert.True(InputManager.IsActivity(Mouse(), Mouse(wheel: 120), NoKeys, NoKeys));

        [Fact]
        public void ADragHeldStillIsStillUse()
            => Assert.True(InputManager.IsActivity(Mouse(left: true), Mouse(left: true), NoKeys, NoKeys));

        [Fact]
        public void TheFrameAButtonComesUpIsUse()
            => Assert.True(InputManager.IsActivity(Mouse(right: true), Mouse(), NoKeys, NoKeys));

        [Fact]
        public void AHeldKeyIsUse()
        {
            var shift = new KeyboardState(Keys.LeftShift);
            Assert.True(InputManager.IsActivity(Mouse(), Mouse(), shift, shift));
        }

        [Fact]
        public void ANewInputManagerHasNotBeenIdle()
            => Assert.True(new InputManager().TimeSinceInput < System.TimeSpan.FromSeconds(1));
    }
}
