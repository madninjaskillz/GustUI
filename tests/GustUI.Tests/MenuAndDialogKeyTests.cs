using System.Collections.Generic;
using GustUI.Elements;
using GustUI.Managers;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework.Input;
using Command = GustUI.Elements.FruitPopupMenu.MenuCommand;
using Pin = GustUI.Elements.ModalWindowElement.WindowPin;

namespace GustUI.Tests
{
    /// <summary>
    /// Who a key goes to (ezmuze #330, #335, #350). An open menu takes the
    /// keys it navigates by before anything else sees them, and a dialog key
    /// only ever reaches the window on top: never a dialog hidden behind
    /// another window.
    /// </summary>
    public class MenuAndDialogKeyTests
    {
        // ---- the menu's keys -------------------------------------------------

        [Theory]
        [InlineData(Keys.Escape, Command.Close)]
        [InlineData(Keys.Enter, Command.Activate)]
        [InlineData(Keys.Up, Command.Up)]
        [InlineData(Keys.Down, Command.Down)]
        [InlineData(Keys.Right, Command.EnterSubmenu)]
        [InlineData(Keys.Left, Command.Back)]
        public void AMenuTakesTheKeysItNavigatesBy(Keys key, Command expected)
        {
            Assert.Equal(expected, FruitPopupMenu.CommandFor(key, typingInSearch: false));
        }

        [Theory]
        [InlineData(Keys.Escape, Command.Close)]
        [InlineData(Keys.Enter, Command.Activate)]
        [InlineData(Keys.Up, Command.Up)]
        [InlineData(Keys.Down, Command.Down)]
        [InlineData(Keys.Left, Command.None)]
        [InlineData(Keys.Right, Command.None)]
        public void LeftAndRightBelongToTheSearchFieldWhileTyping(Keys key, Command expected)
        {
            Assert.Equal(expected, FruitPopupMenu.CommandFor(key, typingInSearch: true));
        }

        [Theory]
        [InlineData(Keys.Space)]
        [InlineData(Keys.Delete)]
        [InlineData(Keys.Z)]
        [InlineData(Keys.F1)]
        public void KeysAMenuDoesNotUseAreNotItsToTake(Keys key)
        {
            Assert.Equal(Command.None, FruitPopupMenu.CommandFor(key, typingInSearch: false));
        }

        // ---- InputManager offers new presses to the menu first ---------------

        [Fact]
        public void AKeyTheMenuTakesIsConsumed()
        {
            var offered = new List<Keys>();
            List<Keys> taken = InputManager.OfferNewKeys(
                new KeyboardState(Keys.Escape), new KeyboardState(),
                key => { offered.Add(key); return key == Keys.Escape; });

            Assert.Equal(new[] { Keys.Escape }, offered);
            Assert.Equal(new[] { Keys.Escape }, taken);
        }

        [Fact]
        public void AKeyTheMenuDeclinesGoesOnAsUsual()
        {
            List<Keys> taken = InputManager.OfferNewKeys(
                new KeyboardState(Keys.Space), new KeyboardState(), key => false);

            Assert.Null(taken);
        }

        [Fact]
        public void AKeyStillHeldIsNotOfferedAgain()
        {
            bool offered = false;
            List<Keys> taken = InputManager.OfferNewKeys(
                new KeyboardState(Keys.Escape), new KeyboardState(Keys.Escape),
                key => offered = true);

            Assert.False(offered);
            Assert.Null(taken);
        }

        [Fact]
        public void AChordIsNotMenuNavigation()
        {
            bool offered = false;
            List<Keys> taken = InputManager.OfferNewKeys(
                new KeyboardState(Keys.LeftControl, Keys.Enter), new KeyboardState(Keys.LeftControl),
                key => offered = true);

            Assert.False(offered);
            Assert.Null(taken);
        }

        [Fact]
        public void NoMenuMeansNothingIsConsumed()
        {
            Assert.Null(InputManager.OfferNewKeys(new KeyboardState(Keys.Escape), new KeyboardState(), null));
        }

        // ---- dialog keys go to the front window only -------------------------

        private static Element Root()
        {
            var parent = new Element();
            parent.AddTrait<ChildrenTrait>().Set(new TVElements());
            return parent;
        }

        private static Element Open(Element root, string name, Pin pin = Pin.Normal)
        {
            var window = new Element
            {
                Depth = Element.FrontDepth(100000, ModalWindowElement.StackFloor(pin), ModalWindowElement.StackCeiling(pin, null)),
            };
            root.AddChild(window, name);
            window.MarkBroughtForward();
            return window;
        }

        [Fact]
        public void TheDialogOnTopTakesTheKey()
        {
            Element root = Root();
            Element sequencer = Open(root, "sequencer");
            Element dialog = Open(root, "newSong");

            Assert.Same(dialog, ModalWindowElement.DialogKeyWindow(new[] { sequencer, dialog }, w => w == dialog));
        }

        [Fact]
        public void AWindowOpenedOverADialogShieldsIt()
        {
            // #335: the module editor opened over the New song dialog. The
            // editor is not a dialog, so nobody gets the dialog key and the
            // editor's own Escape runs instead.
            Element root = Root();
            Element dialog = Open(root, "newSong");
            Element editor = Open(root, "module-modal");

            Assert.Null(ModalWindowElement.DialogKeyWindow(new[] { dialog, editor }, w => w == dialog));
        }

        [Fact]
        public void AWindowPinnedToTheFrontShieldsADialogOpenedLater()
        {
            Element root = Root();
            Element pinned = Open(root, "panel", Pin.Front);
            Element dialog = Open(root, "save");

            Assert.Same(pinned, ModalWindowElement.FrontWindow(new[] { pinned, dialog }));
            Assert.Null(ModalWindowElement.DialogKeyWindow(new[] { pinned, dialog }, w => w == dialog));
        }

        [Fact]
        public void ClickingADialogBackToTheFrontGivesItTheKeyAgain()
        {
            Element root = Root();
            Element dialog = Open(root, "newSong");
            Element editor = Open(root, "module-modal");

            dialog.MarkBroughtForward();

            Assert.Same(dialog, ModalWindowElement.DialogKeyWindow(new[] { dialog, editor }, w => w == dialog));
        }

        [Fact]
        public void NoWindowsMeansNoDialog()
        {
            Assert.Null(ModalWindowElement.DialogKeyWindow(new Element[0], w => true));
        }
    }
}
