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

namespace GustUI.Elements
{
    [ElementTraits(typeof(TextTrait), typeof(FontTrait), typeof(ForegroundColorTrait))]
    public class BasicButtonElement : FilledRectangleElement
    {
        private TextElement textElement;
        public BasicButtonElement()
        {
            textElement = new TextElement();
            TVFont font = ElementTrait<FontTrait>().Value();

            textElement.Set<HorizontalAlignmentTrait, TVHorizontalAlignment>(new TVHorizontalAlignment { Alignment = HorizontalAlignment.Center });
            textElement.Set<VerticalAlignmentTrait, TVVerticalAlignment>(new TVVerticalAlignment { Alignment = VerticalAlignment.Center });

            Set<BorderSizeTrait, TVInt>(new TVInt(2));
            ElementTrait<ChildrenTrait>().Value().Items.Add(textElement);

            //Sync() is available for all children but this seems overzealous - position etc will filter down

            Sync(textElement);
        }


    }
}
