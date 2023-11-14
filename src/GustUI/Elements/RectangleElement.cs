using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GustUI.Elements;

[ElementTraits(typeof(PositionTrait), typeof(SizeTrait), typeof(ChildrenTrait), typeof(BorderColorTrait), typeof(BorderSizeTrait))]
public class RectangleElement : Element
{
    public override void Draw(SpriteBatch spriteBatch, Element parent)
    {
        TVVector actualPosition = this.GetActualPosition(parent);

        TVVector size = ElementTrait<SizeTrait>().Value();

        int borderSize = ElementTrait<BorderSizeTrait>().Value().Int;
        if (borderSize > 0)
        {
            spriteBatch.DrawRectangle(actualPosition.Rectangle(size), ElementTrait<BorderColorTrait>().Value().AsXna, borderSize);
        }

        base.Draw(spriteBatch, parent);
    }
}

