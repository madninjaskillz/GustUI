using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GustUI.Elements;

[ElementTraits(typeof(BackgroundColorTrait))]
public class FilledRectangleElement : RectangleElement
{
    public FilledRectangleElement() { }
    public FilledRectangleElement(int left, int top, int width, int height, Color background, int border = 0, Color? borderColor = null)
    {
        Set<PositionTrait>(new TVVector(left,top));
        Set<SizeTrait>(new TVVector(width,height));

        Set<BackgroundColorTrait, TVColor>(new TVColor(background));

        if (border > 0)
        {
            Set<BorderSizeTrait>(new TVInt(border));
            if (borderColor.HasValue)
            {
                Set<BorderColorTrait>(new TVColor(borderColor.Value));
            }
        }
    }
    public override void Draw(SpriteBatch spriteBatch, Element parent)
    {
        TVVector actualPosition = this.GetActualPosition(parent);

        TVVector size = this.ElementTrait<SizeTrait>().Value();

        Rectangle rect = new Rectangle(actualPosition.X.AsInt(), actualPosition.Y.AsInt(), size.X.AsInt(), size.Y.AsInt());
        Color color = this.ElementTrait<BackgroundColorTrait>().Value().AsXna;

        spriteBatch.DrawFilledRectangle(rect, color);
        base.Draw(spriteBatch, parent);
    }
}

