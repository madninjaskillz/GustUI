using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Elements.InputElements
{
    /// <summary>
    /// A single-line text field. Click to focus (focus visuals via the
    /// focused/unfocused traits); while focused the InputManager routes
    /// newly pressed keys here and suppresses keyboard shortcut hooks.
    /// Basic typing: letters (shift = caps), digits, space, '-'/'_', '.',
    /// backspace. <see cref="OnTextChanged"/> fires per edit;
    /// <see cref="OnSubmit"/> fires on Enter.
    /// </summary>
    [ElementTraits(typeof(TextTrait), typeof(OnFocused), typeof(OnUnfocused), typeof(OnMousePress))]
    public class TextFieldElement : FilledRectangleElement
    {
        internal override bool CanBeInputFocused => true;
        private TextElement textElement;
        private string text = "";

        public Action<string> OnTextChanged;

        /// <summary>Raised when Enter is pressed while focused.</summary>
        public Action<string> OnSubmit;

        public int MaxLength { get; set; } = 60;

        public string Text
        {
            get => text;
            set
            {
                text = value ?? "";
                textElement.Set<TextTrait>(new TVText(text));
            }
        }

        public TextFieldElement()
        {
            var theme = Resources.StaticResources.Theme;

            // A field's text stops at the field's edge. Without this a value
            // longer than the box simply carried on drawing over whatever was
            // beside it — a file name running straight through the Save button
            // next to it, which reads as two broken controls rather than one
            // long value.
            ClipChildren = true;

            textElement = new TextElement { WordWrap = false };
            textElement.Set<ForegroundColorTrait>(new TVColor(theme.BodyText));
            textElement.Set<PositionTrait>(new TVVector(6, 4));
            textElement.Set<FontTrait>(theme.UiFont);
            textElement.Set<HorizontalAlignmentTrait>(new TVHorizontalAlignment() { Alignment = HorizontalAlignment.Left });
            textElement.Set<TextTrait>(new TVText(""));

            // Themed idle state (design-guide.md §1) — was hardcoded
            // Color.Gray/Color.Black regardless of app theme, which read as a
            // stray light-mode control inside an otherwise dark app.
            this.Set<BorderFillTrait>(new TVBorderColorFill(theme.SurfaceBorder));
            this.Set<BorderSizeTrait>(new TVInt(1));
            this.Set<BackgroundFillTrait>(new TVFillSolidColor(theme.SurfaceRaised));

            this.Set<OnFocused>(new TVEvent<TVEventArgs>(x => OnFocusedHandler(x)));
            this.Set<OnUnfocused>(new TVEvent<TVEventArgs>(x => OnUnfocusedHandler(x)));
            AddChild(textElement, "text");
        }

        /// <summary>Child text size follows the field.</summary>
        /// <summary>
        /// The face the field draws its text in. Defaults to the theme's BODY
        /// font, which is right for a dialog and too big for a dense form: an
        /// inspector row gave its value the body face beside a small-face
        /// label, so the value both outsized its own caption and overflowed the
        /// row it was given, clipped along the bottom of every glyph.
        /// </summary>
        public TVFont Font
        {
            get => textElement.ElementTrait<FontTrait>().Value();
            set => textElement.Set<FontTrait>(value);
        }

        public void FitText()
        {
            TVVector size = this.GetSize();
            textElement.Set<SizeTrait>(new TVVector(Math.Max(10, size.X - 12), Math.Max(10, size.Y - 6)));
        }

        internal override void HandleKeyInput(Keys key, bool shift, bool control)
        {
            if (control)
            {
                HandleShortcut(key);
                return;
            }

            if (key == Keys.Back)
            {
                if (text.Length > 0)
                {
                    Text = text.Substring(0, text.Length - 1);
                    OnTextChanged?.Invoke(text);
                }

                return;
            }

            if (key == Keys.Enter)
            {
                OnSubmit?.Invoke(text);
                return;
            }

            char? c = CharFor(key, shift);
            if (c.HasValue)
            {
                Insert(c.Value.ToString());
            }
        }

        /// <summary>
        /// Ctrl+V / Ctrl+C / Ctrl+X / Ctrl+A.
        ///
        /// Paste is the one that matters: this field is where API keys, OAuth
        /// client secrets and authorisation codes get entered, and none of
        /// those are things anyone types by hand. Without it those flows are
        /// effectively unusable (reported 2026-08-23).
        ///
        /// There is no selection model here yet, so copy and cut act on the
        /// WHOLE field and select-all is a no-op that at least doesn't type an
        /// "a" — better than a shortcut that silently corrupts the value.
        /// </summary>
        private void HandleShortcut(Keys key)
        {
            switch (key)
            {
                case Keys.V:
                    Insert(Managers.ClipboardBridge.GetText());
                    break;

                case Keys.C:
                    Managers.ClipboardBridge.SetText(text);
                    break;

                case Keys.X:
                    Managers.ClipboardBridge.SetText(text);
                    if (text.Length > 0)
                    {
                        Text = "";
                        OnTextChanged?.Invoke(text);
                    }

                    break;
            }
        }

        /// <summary>Appends, honouring <see cref="MaxLength"/> and stripping
        /// anything that can't live on one line — pasted text routinely arrives
        /// with a trailing newline, and a redirect URL copied out of a browser
        /// can arrive with leading whitespace.</summary>
        public void Insert(string incoming)
        {
            if (string.IsNullOrEmpty(incoming))
            {
                return;
            }

            var cleaned = new StringBuilder(incoming.Length);
            foreach (char c in incoming)
            {
                if (c == '\n' || c == '\r' || c == '\t')
                {
                    continue;
                }

                if (!char.IsControl(c))
                {
                    cleaned.Append(c);
                }
            }

            if (cleaned.Length == 0)
            {
                return;
            }

            int room = MaxLength - text.Length;
            if (room <= 0)
            {
                return;
            }

            string addition = cleaned.Length <= room ? cleaned.ToString() : cleaned.ToString(0, room);
            Text = text + addition;
            OnTextChanged?.Invoke(text);
        }

        /// <summary>
        /// The character a key produces. Covers the full printable ASCII range
        /// a US layout can reach, not just letters and digits — the previous
        /// set (a-z, 0-9, space, -, _, .) could not type a URL, a path, or most
        /// API keys, so a field that couldn't be pasted into couldn't be filled
        /// in at all.
        ///
        /// This is a US-layout map. A proper fix is the platform's own
        /// character input (KNI exposes a TextInput event on desktop), which
        /// would also bring dead keys and IME along; that is a bigger change
        /// than this one, and paste covers the case that actually hurts.
        /// </summary>
        private static char? CharFor(Keys key, bool shift)
        {
            if (key >= Keys.A && key <= Keys.Z)
            {
                char c = (char)('a' + (key - Keys.A));
                return shift ? char.ToUpperInvariant(c) : c;
            }

            if (key >= Keys.D0 && key <= Keys.D9)
            {
                int digit = key - Keys.D0;
                if (!shift)
                {
                    return (char)('0' + digit);
                }

                return ")!@#$%^&*("[digit];
            }

            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
            {
                return (char)('0' + (key - Keys.NumPad0));
            }

            return key switch
            {
                Keys.Space => ' ',
                Keys.OemMinus => shift ? '_' : '-',
                Keys.OemPlus => shift ? '+' : '=',
                Keys.OemPeriod => shift ? '>' : '.',
                Keys.OemComma => shift ? '<' : ',',
                Keys.OemQuestion => shift ? '?' : '/',
                Keys.OemSemicolon => shift ? ':' : ';',
                Keys.OemQuotes => shift ? '"' : '\'',
                Keys.OemTilde => shift ? '~' : '`',
                Keys.OemOpenBrackets => shift ? '{' : '[',
                Keys.OemCloseBrackets => shift ? '}' : ']',
                Keys.OemPipe or Keys.OemBackslash => shift ? '|' : '\\',
                Keys.Decimal => '.',
                Keys.Add => '+',
                Keys.Subtract => '-',
                Keys.Multiply => '*',
                Keys.Divide => '/',
                _ => (char?)null,
            };
        }

        // design-guide.md §6: a focused element gets a distinct visible
        // indicator (2px AccentSelection outline) separate from hover/press —
        // the field's background also lifts to SurfaceHeader so focus reads
        // even for someone not sensitive to the border color alone.
        private void OnFocusedHandler(TVEventArgs x)
        {
            var theme = Resources.StaticResources.Theme;
            this.Set<BackgroundFillTrait>(new TVFillSolidColor(theme.SurfaceHeader));
            this.Set<BorderFillTrait>(new TVBorderColorFill(theme.AccentSelection));
            this.Set<BorderSizeTrait>(new TVInt(2));
        }
        private void OnUnfocusedHandler(TVEventArgs x)
        {
            var theme = Resources.StaticResources.Theme;
            this.Set<BackgroundFillTrait>(new TVFillSolidColor(theme.SurfaceRaised));
            this.Set<BorderFillTrait>(new TVBorderColorFill(theme.SurfaceBorder));
            this.Set<BorderSizeTrait>(new TVInt(1));
        }
    }
}
