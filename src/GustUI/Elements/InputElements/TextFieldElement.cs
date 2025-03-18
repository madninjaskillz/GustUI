using GustUI.Attributes;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Elements.InputElements
{
    [ElementTraits(typeof(TextTrait), typeof(OnFocused), typeof(OnUnfocused))]
    public class TextFieldElement : FilledRectangleElement
    {
        internal override bool CanBeInputFocused => true; 
        private TextElement textElement;

        public TextFieldElement()
        {
            textElement = this.AddChildElement<TextElement>("text");
            textElement.Set<ForegroundColorTrait>(new TVColor(Color.Black));
            textElement.Set<PositionTrait>(new TVVector(0, 0));
            textElement.Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            textElement.Set<ForegroundColorTrait>(new TVColor(Color.Black));
            textElement.Set<HorizontalAlignmentTrait>(new TVHorizontalAlignment() { Alignment = HorizontalAlignment.Left });
            textElement.Set<TextTrait>(new TVText("Test"));

            this.Set<BorderFillTrait>(new TVBorderColorFill(Color.Black*0.5f));
            this.Set<BorderSizeTrait>(new TVInt(1));
            this.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Gray * 0.35f));

            this.Set<OnFocused>(new TVEvent<TVEventArgs>(x => OnFocused(x)));
            this.Set<OnUnfocused>(new TVEvent<TVEventArgs>(x => OnUnfocused(x)));
            AddChild(textElement, "text");
        }

        private void OnFocused(TVEventArgs x)
        {
            this.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.White));
            this.Set<BorderFillTrait>(new TVBorderColorFill(Color.Gray));
        }
        private void OnUnfocused(TVEventArgs x)
        {
            this.Set<BackgroundFillTrait>(new TVFillSolidColor(Color.Gray * 0.35f));
            this.Set<BorderFillTrait>(new TVBorderColorFill(Color.Black*0.5f));
        }
    }
}
