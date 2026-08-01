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
            textElement = new TextElement { WordWrap = false };
            textElement.Set<ForegroundColorTrait>(new TVColor(Color.Black));
            textElement.Set<PositionTrait>(new TVVector(6, 4));
            textElement.Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            textElement.Set<HorizontalAlignmentTrait>(new TVHorizontalAlignment() { Alignment = HorizontalAlignment.Left });
            textElement.Set<TextTrait>(new TVText(""));

            this.Set<BorderFillTrait>(new TVBorderColorFill(Color.Black * 0.5f));
            this.Set<BorderSizeTrait>(new TVInt(1));
            this.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Gray * 0.35f));

            this.Set<OnFocused>(new TVEvent<TVEventArgs>(x => OnFocusedHandler(x)));
            this.Set<OnUnfocused>(new TVEvent<TVEventArgs>(x => OnUnfocusedHandler(x)));
            AddChild(textElement, "text");
        }

        /// <summary>Child text size follows the field.</summary>
        public void FitText()
        {
            TVVector size = this.GetSize();
            textElement.Set<SizeTrait>(new TVVector(Math.Max(10, size.X - 12), Math.Max(10, size.Y - 6)));
        }

        internal override void HandleKeyInput(Keys key, bool shift)
        {
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
            if (c.HasValue && text.Length < MaxLength)
            {
                Text = text + c.Value;
                OnTextChanged?.Invoke(text);
            }
        }

        private static char? CharFor(Keys key, bool shift)
        {
            if (key >= Keys.A && key <= Keys.Z)
            {
                char c = (char)('a' + (key - Keys.A));
                return shift ? char.ToUpperInvariant(c) : c;
            }

            if (key >= Keys.D0 && key <= Keys.D9)
            {
                return (char)('0' + (key - Keys.D0));
            }

            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
            {
                return (char)('0' + (key - Keys.NumPad0));
            }

            return key switch
            {
                Keys.Space => ' ',
                Keys.OemMinus => shift ? '_' : '-',
                Keys.OemPeriod => '.',
                _ => (char?)null,
            };
        }

        private void OnFocusedHandler(TVEventArgs x)
        {
            this.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.White));
            this.Set<BorderFillTrait>(new TVBorderColorFill(Color.Gray));
        }
        private void OnUnfocusedHandler(TVEventArgs x)
        {
            this.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Gray * 0.35f));
            this.Set<BorderFillTrait>(new TVBorderColorFill(Color.Black * 0.5f));
        }
    }
}
