using System;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Elements.InputElements
{
    /// <summary>
    /// A small text box dropped over a control so a value can be TYPED into it
    /// (ezmuze #499): double-click a knob, type "3.2k", press Enter.
    ///
    /// The box knows nothing about units. The host hands it the text to start
    /// from and a commit function that reads what was typed; the commit says
    /// whether the text meant anything. Enter with text that does not keeps
    /// the box open with a warning border, so it can be corrected rather than
    /// retyped. Escape, or a click anywhere else, closes it and changes
    /// nothing — a value half-typed is discarded, never committed
    /// (design-guide.md, "Escape means the cancel button").
    ///
    /// It floats on the root window at popup depth, over whatever panel the
    /// control is in, and there is only ever one: opening another closes the
    /// first.
    /// </summary>
    public static class InlineValueEntry
    {
        /// <summary>Height of the box, whatever the control's size.</summary>
        public const int Height = 22;

        /// <summary>Narrowest the box gets: room for "12.5 kHz" and a caret
        /// on a knob a few pixels across.</summary>
        public const int MinWidth = 72;

        private static TextFieldElement open;
        private static Action openClosed;

        /// <summary>True while an entry box is showing.</summary>
        public static bool IsOpen => open != null;

        /// <summary>The box currently open, or null. For tests and automation.</summary>
        public static TextFieldElement Current => open;

        /// <summary>
        /// Opens the box centred on <paramref name="anchor"/>.
        /// </summary>
        /// <param name="anchor">The control being typed into; the box is laid
        /// over it.</param>
        /// <param name="text">The text to start from — the control's own
        /// readout, all selected so typing replaces it.</param>
        /// <param name="commit">Reads the typed text and applies it. True
        /// closes the box; false keeps it open, marked, for another go.</param>
        /// <param name="closed">Raised once when the box goes, however it
        /// went.</param>
        public static TextFieldElement Open(Element anchor, string text, Func<string, bool> commit, Action closed = null)
        {
            Close();

            WindowElement root = Resources.StaticResources.RootWindow;
            if (anchor == null || root == null)
            {
                return null;
            }

            Vector2 at = anchor.GetActualXnaPosition();
            Vector2 size = anchor.GetSize().AsXna;
            int width = (int)Math.Max(MinWidth, size.X);
            float x = at.X + (size.X - width) / 2f;
            float y = at.Y + (size.Y - Height) / 2f;

            // Kept on screen: a knob at the window's edge still gets a box
            // that can be read.
            Vector2 rootSize = root.GetSize().AsXna;
            if (rootSize.X > 0)
            {
                x = MathHelper.Clamp(x, 0, Math.Max(0, rootSize.X - width));
                y = MathHelper.Clamp(y, 0, Math.Max(0, rootSize.Y - Height));
            }

            var field = new TextFieldElement { MaxLength = 32, Text = text ?? "" };
            field.Set<PositionTrait>(new TVVector(x, y));
            field.Set<SizeTrait>(new TVVector(width, Height));
            field.Font = Resources.StaticResources.Theme.UiFontSmall;
            field.FitText();
            field.Depth = FruitPopupMenu.PopupDepth;

            field.OnSubmit = typed =>
            {
                if (open != field)
                {
                    return;
                }

                bool accepted;
                try
                {
                    accepted = commit == null || commit(typed);
                }
                catch (Exception)
                {
                    accepted = false;
                }

                if (accepted)
                {
                    Close();
                    return;
                }

                // Not a value: say so on the box itself and leave the text
                // there to be fixed.
                field.BorderColour = Resources.StaticResources.Theme.AccentWarning;
                field.BorderFocusedColour = Resources.StaticResources.Theme.AccentWarning;
                field.SelectAll();
            };

            field.OnTextChanged = _ =>
            {
                field.BorderColour = null;
                field.BorderFocusedColour = null;
            };

            // Escape and clicking away are both "never mind".
            field.OnCancel = _ => Close();
            field.OnBlur = _ => Close();

            open = field;
            openClosed = closed;
            root.AddChild(field, "inline-value-entry");
            Resources.StaticResources.InputManager?.SetFocus(field);
            field.SelectAll();
            return field;
        }

        /// <summary>Closes the box without committing. Safe when none is
        /// open.</summary>
        public static void Close()
        {
            TextFieldElement field = open;
            if (field == null)
            {
                return;
            }

            // Cleared first: losing focus below calls back in here.
            open = null;
            Action closed = openClosed;
            openClosed = null;

            var input = Resources.StaticResources.InputManager;
            if (input?.CurrentlyFocused == field)
            {
                input.ClearFocus();
            }

            field.Kill();
            closed?.Invoke();
        }
    }
}
