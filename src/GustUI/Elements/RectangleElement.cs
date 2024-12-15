using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace GustUI.Elements;

[ElementTraits(typeof(PositionTrait), typeof(SizeTrait), typeof(ChildrenTrait), typeof(BorderColorTrait), typeof(BorderSizeTrait), typeof(OnMousePress), typeof(OnMouseRelease))]
public class RectangleElement : Element
{
    public override void Draw(SpriteBatch spriteBatch)
    {
        TVVector actualPosition = this.GetActualPosition();

        TVVector size = ElementTrait<SizeTrait>().Value();

        int borderSize = ElementTrait<BorderSizeTrait>().Value().Int;
        if (borderSize > 0)
        {
            spriteBatch.DrawRectangle(actualPosition.Rectangle(size), ElementTrait<BorderColorTrait>().Value().AsXna, borderSize);
        }

        base.Draw(spriteBatch);
    }
}

