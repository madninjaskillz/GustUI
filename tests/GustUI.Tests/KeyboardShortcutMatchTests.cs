using System.Collections.Generic;
using GustUI.Elements;
using GustUI.Managers;
using GustUI.Models;
using Microsoft.Xna.Framework.Input;
using M = GustUI.Managers.InputManager.KeyboardModifiers;

namespace GustUI.Tests
{
    /// <summary>
    /// A keyboard shortcut matches only its EXACT modifier set (ezmuze #280).
    /// The matcher used to check that a hook's own modifiers were held and
    /// never that nothing else was, so the sequencer's bare S (split) fired on
    /// Ctrl+S, and Ctrl+X fired alongside Ctrl+Shift+X.
    /// </summary>
    public class KeyboardShortcutMatchTests
    {
        private static InputManager.KeyboardShortcut Shortcut(Keys key, params M[] modifiers)
            => new InputManager.KeyboardShortcut(key, modifiers);

        private static KeyboardState Held(params Keys[] keys) => new KeyboardState(keys);

        [Fact]
        public void BareKeyMatchesWithNothingElseHeld()
            => Assert.True(Shortcut(Keys.S).IsHeldIn(Held(Keys.S)));

        [Theory]
        [InlineData(Keys.LeftControl)]
        [InlineData(Keys.RightControl)]
        [InlineData(Keys.LeftShift)]
        [InlineData(Keys.RightShift)]
        [InlineData(Keys.LeftAlt)]
        [InlineData(Keys.RightAlt)]
        public void BareKeyRejectsAnyExtraModifier(Keys modifier)
            => Assert.False(Shortcut(Keys.S).IsHeldIn(Held(modifier, Keys.S)));

        [Theory]
        [InlineData(Keys.LeftControl)]
        [InlineData(Keys.RightControl)]
        public void EitherControlKeyCountsAsCtrl(Keys control)
            => Assert.True(Shortcut(Keys.S, M.ctrl).IsHeldIn(Held(control, Keys.S)));

        [Fact]
        public void RightShiftCountsAsShift()
            => Assert.True(Shortcut(Keys.X, M.ctrl, M.shift).IsHeldIn(Held(Keys.RightControl, Keys.RightShift, Keys.X)));

        [Fact]
        public void CtrlShortcutNeedsCtrl()
            => Assert.False(Shortcut(Keys.S, M.ctrl).IsHeldIn(Held(Keys.S)));

        [Fact]
        public void CtrlXDoesNotMatchCtrlShiftX()
        {
            KeyboardState chord = Held(Keys.LeftControl, Keys.LeftShift, Keys.X);
            Assert.False(Shortcut(Keys.X, M.ctrl).IsHeldIn(chord));
            Assert.True(Shortcut(Keys.X, M.ctrl, M.shift).IsHeldIn(chord));
        }

        [Fact]
        public void AltBlocksACtrlShortcut()
            => Assert.False(Shortcut(Keys.S, M.ctrl).IsHeldIn(Held(Keys.LeftControl, Keys.LeftAlt, Keys.S)));

        [Fact]
        public void AltShortcutMatchesAlt()
            => Assert.True(Shortcut(Keys.Left, M.alt).IsHeldIn(Held(Keys.RightAlt, Keys.Left)));

        [Fact]
        public void AHookOnAModifierKeyIsNotBlockedByPressingIt()
        {
            Assert.True(Shortcut(Keys.LeftShift).IsHeldIn(Held(Keys.LeftShift)));
            Assert.True(Shortcut(Keys.RightControl).IsHeldIn(Held(Keys.RightControl)));
            // ...but the OTHER modifiers still count as extra.
            Assert.False(Shortcut(Keys.LeftShift).IsHeldIn(Held(Keys.LeftControl, Keys.LeftShift)));
        }

        [Fact]
        public void AnyModifierHeldSeesBothSides()
        {
            Assert.False(InputManager.AnyModifierHeld(Held(Keys.Enter)));
            Assert.True(InputManager.AnyModifierHeld(Held(Keys.RightAlt, Keys.Enter)));
            Assert.True(InputManager.AnyModifierHeld(Held(Keys.RightShift, Keys.Escape)));
        }

        // ---- menu-bar shortcut lookup --------------------------------------

        private static List<MenuItemModel> Menu(out MenuItemModel save, out MenuItemModel saveAs, out MenuItemModel disabled)
        {
            save = new MenuItemModel { Text = "Save", Shortcut = Shortcut(Keys.S, M.ctrl), Action = _ => { } };
            saveAs = new MenuItemModel { Text = "Save as", Shortcut = Shortcut(Keys.S, M.ctrl, M.shift), Action = _ => { } };
            disabled = new MenuItemModel { Text = "Export", Shortcut = Shortcut(Keys.E, M.ctrl), Action = _ => { }, Enabled = false };
            return new List<MenuItemModel>
            {
                new MenuItemModel
                {
                    Text = "File",
                    SubItems = new List<MenuItemModel>
                    {
                        new MenuItemModel { Text = "Label only", Shortcut = Shortcut(Keys.O, M.ctrl) },
                        new MenuItemModel { Text = "More", SubItems = new List<MenuItemModel> { save, saveAs } },
                        disabled,
                    },
                },
            };
        }

        [Fact]
        public void MenuLookupFindsANestedItemByExactChord()
        {
            List<MenuItemModel> menu = Menu(out MenuItemModel save, out MenuItemModel saveAs, out _);
            Assert.Same(save, MenuBarElement.FindShortcut(menu, Keys.S, Held(Keys.LeftControl, Keys.S)));
            Assert.Same(saveAs, MenuBarElement.FindShortcut(menu, Keys.S, Held(Keys.RightControl, Keys.RightShift, Keys.S)));
        }

        [Fact]
        public void MenuLookupIgnoresTheWrongChordDisabledItemsAndLabels()
        {
            List<MenuItemModel> menu = Menu(out _, out _, out _);
            Assert.Null(MenuBarElement.FindShortcut(menu, Keys.S, Held(Keys.S)));
            Assert.Null(MenuBarElement.FindShortcut(menu, Keys.S, Held(Keys.LeftAlt, Keys.LeftControl, Keys.S)));
            Assert.Null(MenuBarElement.FindShortcut(menu, Keys.E, Held(Keys.LeftControl, Keys.E)));
            Assert.Null(MenuBarElement.FindShortcut(menu, Keys.O, Held(Keys.LeftControl, Keys.O)));
        }
    }
}
