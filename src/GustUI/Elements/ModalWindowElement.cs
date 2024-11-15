﻿using GustUI.Attributes;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace GustUI.Elements
{
    [ElementTraits(typeof(TitleTextTrait), typeof(BodyTextTrait), typeof(FontTrait), typeof(ForegroundColorTrait), typeof(OnClickTrait))]
    public class ModalWindowElement : FilledRectangleElement
    {
        private TextElement textElement = new TextElement();
        private List<BasicButtonElement> buttons = new List<BasicButtonElement>();
        public ModalWindowElement()
        {
            Sync(textElement);

            Setup();
        }
        public ModalWindowElement(TVFont font, string title, string body, Color foreground, TVFill background, List<BasicButtonElement> buttons = null, TVVector position = null, TVVector size = null)
        {
            Sync(textElement);

            Set<FontTrait>(font);
            Set<ForegroundColorTrait>(new TVColor(foreground));
            Set<BackgroundFillTrait>(background);
            Set<TitleTextTrait>(new TVText(title));
            Set<BodyTextTrait>(new TVText(body));
            Set<PositionTrait>(position ?? new TVVector(0, 0));
            Set<SizeTrait>(size ?? new TVVector(0, 0));

            if (buttons != null)
            {
                this.buttons.AddRange(buttons);
            }

            foreach (var button in this.buttons)
            {
                Sync(button);
            }   

            Setup();
        }

        private void Setup()
        {
            //textElement.Set<HorizontalAlignmentTrait, TVHorizontalAlignment>(new TVHorizontalAlignment { Alignment = HorizontalAlignment.Center });
            //textElement.Set<VerticalAlignmentTrait, TVVerticalAlignment>(new TVVerticalAlignment { Alignment = VerticalAlignment.Center });

            //Set<BorderSizeTrait, TVInt>(new TVInt(2));
            //ElementTrait<ChildrenTrait>().Value().Items.Add(textElement);

        }

    }
}
