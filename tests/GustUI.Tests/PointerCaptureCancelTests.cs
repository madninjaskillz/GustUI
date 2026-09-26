using GustUI.Elements;
using GustUI.Managers;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework.Input;

namespace GustUI.Tests
{
    /// <summary>
    /// ezmuze #374: a drag already under way when a waiting dialog opens is
    /// cancelled. Every capture ends; an element that opted in to
    /// OnPointerCaptureCancelled reverts, and one that did not gets its
    /// ordinary release at the pointer's last position before the cancel. The
    /// button then reads as up until it is really let go.
    /// </summary>
    public class PointerCaptureCancelTests
    {
        private static MouseState Mouse(int x, int y, bool left = false, bool middle = false, bool right = false)
            => new MouseState(x, y, 0,
                left ? ButtonState.Pressed : ButtonState.Released,
                middle ? ButtonState.Pressed : ButtonState.Released,
                right ? ButtonState.Pressed : ButtonState.Released,
                ButtonState.Released, ButtonState.Released);

        private static Element Captured(out System.Collections.Generic.List<string> heard, bool optIn)
        {
            var log = new System.Collections.Generic.List<string>();
            var element = new Element();
            element.AddTrait<OnMouseRelease>().Set(new TVEvent<ClickEventArgs>(a => log.Add("release@" + a.GlobalMousePosition.X)));
            element.AddTrait<OnMiddleMouseRelease>().Set(new TVEvent<ClickEventArgs>(a => log.Add("middle-release")));
            if (optIn)
            {
                element.AddTrait<OnPointerCaptureCancelled>().Set(new TVEvent<ClickEventArgs>(a => log.Add("cancelled@" + a.GlobalMousePosition.X)));
            }

            heard = log;
            return element;
        }

        [Fact]
        public void AnOptedInElementIsToldItWasCancelledAndNotReleased()
        {
            var input = new InputManager();
            Element dragged = Captured(out var heard, optIn: true);
            input.SeedPreviousMouseState(Mouse(120, 40, left: true));
            input.CapturePointer(dragged);

            Assert.Equal(1, input.CancelPointerCaptures());

            Assert.Null(input.CapturedPointerElement);
            Assert.Equal(new[] { "cancelled@120" }, heard);
        }

        [Fact]
        public void AnElementThatDidNotOptInGetsItsReleaseWhereThePointerLastWas()
        {
            var input = new InputManager();
            Element dragged = Captured(out var heard, optIn: false);
            input.SeedPreviousMouseState(Mouse(300, 40, left: true));
            input.CapturePointer(dragged);

            input.CancelPointerCaptures();

            Assert.Equal(new[] { "release@300" }, heard);
        }

        [Fact]
        public void EveryCaptureSlotIsCancelled()
        {
            var input = new InputManager();
            Element left = Captured(out var leftHeard, optIn: true);
            Element middle = Captured(out var middleHeard, optIn: false);
            Element right = Captured(out var rightHeard, optIn: true);
            input.SeedPreviousMouseState(Mouse(10, 10, left: true, middle: true, right: true));
            input.CapturePointer(left);
            input.CaptureMiddlePointer(middle);
            input.CaptureRightPointer(right);

            Assert.Equal(3, input.CancelPointerCaptures());

            Assert.Null(input.CapturedPointerElement);
            Assert.Null(input.CapturedMiddleElement);
            Assert.Null(input.CapturedRightElement);
            Assert.Equal(new[] { "cancelled@10" }, leftHeard);
            Assert.Equal(new[] { "middle-release" }, middleHeard);
            Assert.Equal(new[] { "cancelled@10" }, rightHeard);
        }

        [Fact]
        public void TheHeldButtonsReadAsUpUntilTheyAreLetGo()
        {
            var input = new InputManager();
            input.SeedPreviousMouseState(Mouse(10, 10, left: true, right: true));
            input.CapturePointer(Captured(out _, optIn: true));
            input.CaptureRightPointer(Captured(out _, optIn: true));

            input.CancelPointerCaptures();

            Assert.Equal((true, false, true), input.MaskedButtons);
        }

        [Fact]
        public void NothingCapturedIsANoOp()
        {
            var input = new InputManager();
            input.SeedPreviousMouseState(Mouse(10, 10, left: true));

            Assert.Equal(0, input.CancelPointerCaptures());
            Assert.Equal((false, false, false), input.MaskedButtons);
        }
    }
}
