﻿using GustUI.Attributes;
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

namespace GustUI.Elements
{
    [ElementTraits(typeof(TextTrait), typeof(FontTrait), typeof(ForegroundColorTrait), typeof(OnClickTrait))]
    public class BasicButtonElement : FilledRectangleElement
    {
        private TextElement textElement;
        public BasicButtonElement()
        {
            textElement = this.AddChildElement<TextElement>();
            Sync(textElement);

            Setup();
        }

        public BasicButtonElement(string text, Color foreground, TVFill background, TVVector position = null, TVVector size = null, TVEvent onClick = null) : this(Resources.StaticResources.Theme.UiFont, text, foreground, background, position, size, onClick)
        {
            
        }

        public BasicButtonElement(TVFont font, string text, Color foreground, TVFill background, TVVector position = null, TVVector size = null, TVEvent onClick = null)
        {
            textElement = this.AddChildElement<TextElement>();
            Sync(textElement);

            Set<FontTrait>(font);
            Set<ForegroundColorTrait>(new TVColor(foreground));
            Set<BackgroundFillTrait>(background);
            Set<TextTrait>(new TVText(text));
            Set<PositionTrait>(position ?? new TVVector(0, 0));
            Set<SizeTrait>(size ?? new TVVector(0, 0));
            Set<OnClickTrait>(onClick);
            
            Setup();
        }

        private void Setup()
        {
            textElement.Set<HorizontalAlignmentTrait, TVHorizontalAlignment>(new TVHorizontalAlignment { Alignment = HorizontalAlignment.Center });
            textElement.Set<VerticalAlignmentTrait, TVVerticalAlignment>(new TVVerticalAlignment { Alignment = VerticalAlignment.Center });

            Set<BorderSizeTrait, TVInt>(new TVInt(2));
            this.AddChild(textElement, $"button Text: "+this.ElementTrait<TextTrait>().Value().Text);
        }


    }
}
