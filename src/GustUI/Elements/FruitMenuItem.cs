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
    [ElementTraits(typeof(OnClickTrait))]
    public class FruitMenuItem : FilledRectangleElement
    {
        TextElement iconElement;
        TextElement textElement;
        public FruitMenuItem(string text, string icon, Action<ClickEventArgs> action, bool more, int width)
        {
            Set<SizeTrait>(new TVVector(width, 40));
            Set<BackgroundFillTrait>(new TVSmartFill { States= Resources.StaticResources.Theme.FruitMenuItemStates });
            Set<OnClickTrait>(new TVEvent((x)=>action((ClickEventArgs)x)));

            if (icon != null)
            {
                iconElement = this.AddChildElement<TextElement>();
                iconElement.Set<PositionTrait>(new TVVector(10, 5));
                iconElement.Set<SizeTrait>(new TVVector(32, 32));
                iconElement.Set<FontTrait>(Resources.StaticResources.Theme.SymbolFont);
                iconElement.Set<ForegroundColorTrait>(new TVColor(Color.Black));
                iconElement.Set<TextTrait>(new TVText(icon));
            }

            textElement = this.AddChildElement<TextElement>();
            textElement.Set<PositionTrait>(new TVVector(50, 6));
            textElement.Set<SizeTrait>(new TVVector(width - 70, 50));
            textElement.Set<FontTrait>(Resources.StaticResources.Theme.UiFont);
            textElement.Set<ForegroundColorTrait>(new TVColor(Color.Black));
            textElement.Set<TextTrait>(new TVText(text));
            
        }
    }
}
