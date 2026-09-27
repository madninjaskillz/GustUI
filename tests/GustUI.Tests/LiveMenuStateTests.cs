using System.Collections.Generic;
using GustUI.Elements;
using GustUI.Managers;
using GustUI.Models;
using Microsoft.Xna.Framework.Input;
using M = GustUI.Managers.InputManager.KeyboardModifiers;

namespace GustUI.Tests
{
    /// <summary>
    /// A menu item can say whether it is enabled, and what it is called, at
    /// the moment it is asked rather than when it was built (ezmuze #411). A
    /// menu-bar menu is built once with its view, so a fixed Enabled went
    /// stale: Edit > Copy stayed grey after a selection was made.
    /// </summary>
    public class LiveMenuStateTests
    {
        [Fact]
        public void EnabledWhenIsAskedEveryTime()
        {
            bool possible = false;
            var item = new MenuItemModel { Text = "Copy", EnabledWhen = () => possible };

            Assert.False(item.Enabled);
            possible = true;
            Assert.True(item.Enabled);
        }

        [Fact]
        public void EnabledWhenOverridesAFixedEnabled()
        {
            var item = new MenuItemModel { Text = "Copy", Enabled = false, EnabledWhen = () => true };
            Assert.True(item.Enabled);

            item.EnabledWhen = null;
            Assert.False(item.Enabled);   // the fixed value is still there underneath
        }

        [Fact]
        public void WithoutAPredicateEnabledIsTheFixedValue()
        {
            Assert.True(new MenuItemModel { Text = "Copy" }.Enabled);
            Assert.False(new MenuItemModel { Text = "Copy", Enabled = false }.Enabled);
        }

        [Fact]
        public void TextWhenIsAskedEveryTime()
        {
            int count = 1;
            var item = new MenuItemModel
            {
                Text = "fixed",
                TextWhen = () => count > 1 ? "Convert (" + count + " clips)" : "Convert",
            };

            Assert.Equal("Convert", item.Text);
            count = 3;
            Assert.Equal("Convert (3 clips)", item.Text);
        }

        [Fact]
        public void ANullLiveLabelReadsAsEmptyRatherThanNull()
        {
            var item = new MenuItemModel { TextWhen = () => null };
            Assert.Equal("", item.Text);
        }

        [Fact]
        public void TheMenuBarRunsALiveItemOnlyWhileItIsEnabled()
        {
            bool possible = false;
            var glue = new MenuItemModel
            {
                Text = "Glue",
                Shortcut = new InputManager.KeyboardShortcut(Keys.G, M.ctrl),
                Action = _ => { },
                EnabledWhen = () => possible,
            };
            var menu = new List<MenuItemModel>
            {
                new MenuItemModel { Text = "Edit", SubItems = new List<MenuItemModel> { glue } },
            };
            var chord = new KeyboardState(Keys.LeftControl, Keys.G);

            Assert.Null(MenuBarElement.FindShortcut(menu, Keys.G, chord));
            possible = true;
            Assert.Same(glue, MenuBarElement.FindShortcut(menu, Keys.G, chord));
        }

        [Fact]
        public void ALiveDisabledSectionHidesItsChildren()
        {
            bool open = true;
            var save = new MenuItemModel
            {
                Text = "Save",
                Shortcut = new InputManager.KeyboardShortcut(Keys.S, M.ctrl),
                Action = _ => { },
            };
            var menu = new List<MenuItemModel>
            {
                new MenuItemModel { Text = "File", EnabledWhen = () => open, SubItems = new List<MenuItemModel> { save } },
            };
            var chord = new KeyboardState(Keys.LeftControl, Keys.S);

            Assert.Same(save, MenuBarElement.FindShortcut(menu, Keys.S, chord));
            open = false;
            Assert.Null(MenuBarElement.FindShortcut(menu, Keys.S, chord));
        }
    }
}
