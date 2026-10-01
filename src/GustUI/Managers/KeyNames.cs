using Microsoft.Xna.Framework.Input;

namespace GustUI.Managers
{
    /// <summary>
    /// The name a person reads for a key: "]" rather than "OemCloseBrackets",
    /// "0" rather than "D0", "Esc" rather than "Escape".
    ///
    /// One table for every place a shortcut is printed — a menu row's key
    /// column, a tooltip, a help listing — so they cannot disagree. It used to
    /// be <c>Keys.ToString()</c> in the menu and a handful of special cases in
    /// the app's help screen, and both showed the enum's spelling for anything
    /// they had not thought of (ezmuze #637, #641).
    /// </summary>
    public static class KeyNames
    {
        public static string Display(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9)
            {
                return ((char)('0' + (key - Keys.D0))).ToString();
            }

            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
            {
                return "Num " + (char)('0' + (key - Keys.NumPad0));
            }

            switch (key)
            {
                case Keys.Space: return "Space";
                case Keys.Escape: return "Esc";
                case Keys.Enter: return "Enter";
                case Keys.Delete: return "Delete";
                case Keys.Back: return "Backspace";
                case Keys.Tab: return "Tab";
                case Keys.PageUp: return "Page Up";
                case Keys.PageDown: return "Page Down";
                case Keys.Insert: return "Insert";
                case Keys.Left: return "Left";
                case Keys.Right: return "Right";
                case Keys.Up: return "Up";
                case Keys.Down: return "Down";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.OemComma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.OemPipe: return "\\";
                case Keys.OemBackslash: return "\\";
                case Keys.OemTilde: return "`";

                // Oem8 is the key left of 1 on a UK board, which the app's
                // help has always called by its apostrophe-like legend.
                case Keys.Oem8: return "'";
                case Keys.OemPlus: return "+";
                case Keys.OemMinus: return "-";
                case Keys.Add: return "Num +";
                case Keys.Subtract: return "Num -";
                case Keys.Multiply: return "Num *";
                case Keys.Divide: return "Num /";
                case Keys.Decimal: return "Num .";
                default: return key.ToString();
            }
        }
    }
}
