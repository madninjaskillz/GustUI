using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Elements
{
    [ElementTraits(
        typeof(TextTrait), 
        typeof(PositionTrait), 
        typeof(ForegroundColorTrait), 
        typeof(FontTrait), 
        typeof(VerticalAlignmentTrait), 
        typeof(HorizontalAlignmentTrait))]
    public class TextElement : Element
    {
        public override void Draw(SpriteBatch spriteBatch, Element parent)
        {
            if (this.ElementTrait<FontTrait>().Value().Family != null)
            {

                string fontName = this.ElementTrait<FontTrait>().Value().Family;
                float fontSize = this.ElementTrait<FontTrait>().Value().Size;
                string text = this.ElementTrait<TextTrait>().Value().Text;
                int border = this.ElementTrait<FontTrait>().Value().Border;
                Color foreground = this.ElementTrait<ForegroundColorTrait>().Value().AsXna;
                var font = Resources.StaticResources.FontManager.LoadFont(fontName, fontSize);


                Vector2 thisSize = font.MeasureString(text) * GustConstants.FontScale;

                TVVector actualPosition = this.GetActualPosition(parent, thisSize);



                spriteBatch.DrawString(
                    font,
                    text,
                    actualPosition.AsXna,
                    foreground,
                    border);

            }

            base.Draw(spriteBatch, parent);
        }
    }
}
