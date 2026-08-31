using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace GustUI.Elements
{
    [ElementTraits(typeof(TextTrait), typeof(FontTrait), typeof(ForegroundColorTrait), typeof(OnMouseButtonHeldDown), typeof(OnEnterTrait), typeof(OnExitTrait))]
    public class BasicButtonElement : FilledRectangleElement
    {
        /// <summary>Disabled controls stop responding to hover/press/click and
        /// render desaturated (design-guide.md §6: a gray wash over whatever
        /// fill is currently set, rather than a per-fill-type grayscale
        /// conversion — robust regardless of which TVFill subtype a button
        /// uses). Default true (unchanged behavior for every existing
        /// button).</summary>
        public bool Enabled { get; set; } = true;

        private static readonly Color DisabledWash = new Color(128, 128, 128, 140);

        private ButtonStates buttonState;
        private TextElement textElement;

        /// <summary>The label, for subclasses that need to lay it out — see
        /// <see cref="SplitButtonElement"/>, which narrows it so the centred
        /// text doesn't drift under the chevron segment. Its size is SYNCED
        /// from this button's, so anything set here is overwritten on the next
        /// resize; re-apply rather than assuming it sticks.</summary>
        protected TextElement Label => textElement;
        public BasicButtonElement()
        {
            textElement = this.AddChildElement<TextElement>();
            Sync(textElement);

            Setup();
        }

        public BasicButtonElement(string text, Color foreground, ButtonStates buttonStates, TVVector position = null, TVVector size = null, TVEvent<ClickEventArgs> onClick = null)
        {
            textElement = this.AddChildElement<TextElement>();
            Sync(textElement);

            Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            Set<ForegroundColorTrait>(new TVColor(foreground));
            Set<BackgroundFillTrait>(buttonStates.NormalFill);
            Set<TextTrait>(new TVText(text));
            Set<PositionTrait>(position ?? new TVVector(0, 0));
            Set<SizeTrait>(size ?? new TVVector(0, 0));

            this.buttonState = buttonStates;
            Set<OnEnterTrait>(new TVEvent<ClickEventArgs>((x) => { if (Enabled) Set<BackgroundFillTrait>(buttonState.HoveredFill); }));
            Set<OnExitTrait>(new TVEvent<ClickEventArgs>((x) => Set<BackgroundFillTrait>(buttonState.NormalFill)));
            Set<OnMousePress>(new TVEvent<ClickEventArgs>((x) => { if (Enabled) Set<BackgroundFillTrait>(buttonState.PressedFill); }));
            Set<OnMouseRelease>(new TVEvent<ClickEventArgs>((x) =>
            {
                Set<BackgroundFillTrait>(buttonState.NormalFill);
                if (Enabled) { onClick?.TriggerAction?.Invoke(x); }
            }));

            Setup();
        }

        public BasicButtonElement(string text, Color foreground, TVFill background, TVVector position = null, TVVector size = null, TVEvent<ClickEventArgs> onClick = null, TVFill hoverFill = null, TVFill clickFill = null) : this(Resources.StaticResources.Theme.UiFont, text, foreground, background, position, size, onClick, hoverFill, clickFill)
        {

        }

        /// <summary>
        /// Same, with a LIVE label colour — pass <c>() =&gt; Theme.BodyText</c>
        /// and the label follows a palette change instead of freezing at the
        /// value it was built with.
        ///
        /// A label on the neutral fill has to do this: neutral is a dark grey
        /// in the dark palette and a near-white in the light one, so a fixed
        /// colour is unreadable in one of them whichever you pick (ezmuze bug
        /// board #66). Positive and negative fills stay dark enough in both
        /// that a fixed white is still correct there.
        /// </summary>
        public BasicButtonElement(string text, Func<Color> foreground, TVFill background, TVVector position = null, TVVector size = null, TVEvent<ClickEventArgs> onClick = null, TVFill hoverFill = null, TVFill clickFill = null)
            : this(Resources.StaticResources.Theme.UiFont, text, Color.White, background, position, size, onClick, hoverFill, clickFill)
        {
            Set<ForegroundColorTrait>(new TVColor(foreground));
        }

        /// <summary>Live-colour form of the ButtonStates constructor.</summary>
        public BasicButtonElement(string text, Func<Color> foreground, ButtonStates buttonStates, TVVector position = null, TVVector size = null, TVEvent<ClickEventArgs> onClick = null)
            : this(text, Color.White, buttonStates, position, size, onClick)
        {
            Set<ForegroundColorTrait>(new TVColor(foreground));
        }

        public BasicButtonElement(TVFont font, string text, Color foreground, TVFill background, TVVector position = null, TVVector size = null, TVEvent<ClickEventArgs> onClick = null, TVFill hoverFill = null, TVFill clickFill = null)
        {
            textElement = this.AddChildElement<TextElement>();
            Sync(textElement);

            Set<FontTrait>(font);
            Set<ForegroundColorTrait>(new TVColor(foreground));
            Set<BackgroundFillTrait>(background);
            Set<TextTrait>(new TVText(text));
            Set<PositionTrait>(position ?? new TVVector(0, 0));
            Set<SizeTrait>(size ?? new TVVector(0, 0));
            Set<OnMouseRelease>(onClick);

            buttonState = new ButtonStates
            {
                NormalFill = background,
                HoveredFill = hoverFill ?? background,
                PressedFill = clickFill ?? background
            };

            Setup();
        }

        private void Setup()
        {
            textElement.Set<HorizontalAlignmentTrait, TVHorizontalAlignment>(new TVHorizontalAlignment { Alignment = HorizontalAlignment.Center });
            textElement.Set<VerticalAlignmentTrait, TVVerticalAlignment>(new TVVerticalAlignment { Alignment = VerticalAlignment.Center });

            Set<BorderSizeTrait, TVInt>(new TVInt(2));
            this.AddChild(textElement, $"button Text: " + this.ElementTrait<TextTrait>().Value().Text);
        }

        public override void Draw()
        {
            base.Draw();

            // Disabled = desaturate-toward-gray (design-guide.md §6): a
            // translucent gray wash over whatever's already drawn, robust
            // regardless of the concrete TVFill type in use.
            if (!Enabled)
            {
                Vector2 pos = this.GetActualXnaPosition();
                Vector2 size = this.GetSize().AsXna;
                Resources.StaticResources.DrawManager.DrawFilledRectangle(
                    new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y), DisabledWash);
            }
        }


    }
}
