using GustUI.Managers;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace GustUI.Tests
{
    /// <summary>
    /// A shortcut's key is printed as the key's legend, not the enum's name
    /// (ezmuze #637, #641): the Markers menu read "OemCloseBrackets" and the
    /// module editor's help "Ctrl+D0".
    /// </summary>
    public class KeyNamesTests
    {
        [Theory]
        [InlineData(Keys.OemCloseBrackets, "]")]
        [InlineData(Keys.OemOpenBrackets, "[")]
        [InlineData(Keys.D0, "0")]
        [InlineData(Keys.D9, "9")]
        [InlineData(Keys.NumPad3, "Num 3")]
        [InlineData(Keys.Escape, "Esc")]
        [InlineData(Keys.Back, "Backspace")]
        [InlineData(Keys.OemMinus, "-")]
        [InlineData(Keys.OemPlus, "+")]
        [InlineData(Keys.OemComma, ",")]
        [InlineData(Keys.OemPeriod, ".")]
        [InlineData(Keys.PageDown, "Page Down")]
        [InlineData(Keys.M, "M")]
        [InlineData(Keys.F11, "F11")]
        public void PrintsTheLegend(Keys key, string expected)
            => Assert.Equal(expected, KeyNames.Display(key));

        [Fact]
        public void NoOemOrDigitEnumNameEverReachesTheScreen()
        {
            foreach (Keys key in System.Enum.GetValues<Keys>())
            {
                string name = KeyNames.Display(key);
                bool isDigitEnum = name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]);
                Assert.False(isDigitEnum, key + " printed as " + name);
                if (key is >= Keys.OemSemicolon and <= Keys.OemBackslash)
                {
                    Assert.DoesNotContain("Oem", name);
                }
            }
        }
    }
}
