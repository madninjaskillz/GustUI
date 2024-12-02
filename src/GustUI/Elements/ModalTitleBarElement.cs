using GustUI.Attributes;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Elements
{
    [ElementTraits(typeof(FontTrait), typeof(OnExitTrait))]
    public class ModalTitleBarElement : FilledRectangleElement
    {
        private TextElement textElement;
        private BasicButtonElement closeButton;

        public ModalTitleBarElement()
        {
            textElement = CreateElement<TextElement>();
            closeButton = CreateElement<BasicButtonElement>();
            Sync(textElement);
            Sync(closeButton);
            Setup();
        }

        public ModalTitleBarElement(string title, TVVector position = null, TVVector size = null)
        {
            textElement = CreateElement<TextElement>();
            closeButton = CreateElement<BasicButtonElement>();
            Sync(textElement);
            Sync(closeButton);
            //Sync();
            textElement.Set<TextTrait>(new TVText(title));


            Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.Green, Color.DarkGreen, Direction.Vertically));
            Set<BorderSizeTrait>(new TVInt(0));
            Set<BorderColorTrait>(new TVColor(Color.Gray));
            Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            Set<PositionTrait>(position ?? new TVVector(0, 0));
            Set<SizeTrait>(size ?? new TVVector(0, 0));

            closeButton.Set<SizeTrait>(new TVVector(size.Y, size.Y));
            closeButton.Set<TextTrait>(new TVText("X"));
            closeButton.Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            closeButton.Set<BackgroundFillTrait>(new TVFillSimpleGradient(Color.Red, Color.DarkRed, Direction.Vertically));
            closeButton.Set<ForegroundColorTrait>(new TVColor(Color.White));
            closeButton.Set<PositionTrait>(new TVVector(size.X - size.Y, 0));

            Setup();
        }

        private void Setup()
        {
            textElement.Set<HorizontalAlignmentTrait, TVHorizontalAlignment>(new TVHorizontalAlignment { Alignment = HorizontalAlignment.Center });
            textElement.Set<VerticalAlignmentTrait, TVVerticalAlignment>(new TVVerticalAlignment { Alignment = VerticalAlignment.Center });

            Set<BorderSizeTrait, TVInt>(new TVInt(0));


            this.AddChild(closeButton, "closeButton");
            this.AddChild(textElement, "titleText");
        }
    }
}
