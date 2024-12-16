using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace GustUI.Elements;

[ElementTraits(typeof(PositionTrait), typeof(SizeTrait), typeof(ChildrenTrait), typeof(BorderFillTrait), typeof(BorderSizeTrait), typeof(OnMousePress), typeof(OnMouseRelease))]
public class RectangleElement : Element
{
    public override void Draw(SpriteBatch spriteBatch)
    {
        TVVector actualPosition = this.GetActualPosition();

        TVVector size = ElementTrait<SizeTrait>().Value();

        int borderSize = ElementTrait<BorderSizeTrait>().Value().Int;

        if (ElementTrait<BorderFillTrait>().Value() is TVBorderColorFill borderColorFill)
        {
            if (borderSize > 0)
            {
                spriteBatch.DrawRectangle(actualPosition.Rectangle(size), borderColorFill.Color, borderSize);
            }
        }

        if (ElementTrait<BorderFillTrait>().Value() is TVBorder9Grid nineGrid)
        {
            Color color = Color.White * nineGrid.Opacity;
            if (nineGrid.TopLeft)
            {
                spriteBatch.Draw(Resources.StaticResources.Theme.NineGridTopLeft.Texture, new Rectangle(actualPosition.X.AsInt() - nineGrid.NineGridSize, actualPosition.Y.AsInt() - nineGrid.NineGridSize, nineGrid.NineGridSize, nineGrid.NineGridSize), null, color);
            }


            if (nineGrid.TopCenter)
            {
                spriteBatch.Draw(Resources.StaticResources.Theme.NineGridTop.Texture, new Rectangle(actualPosition.X.AsInt(), actualPosition.Y.AsInt() - nineGrid.NineGridSize, (int)this.GetSize().X, nineGrid.NineGridSize), null, color);
            }

            if (nineGrid.TopRight)
            {
                spriteBatch.Draw(Resources.StaticResources.Theme.NineGridTopRight.Texture, new Rectangle(actualPosition.X.AsInt() + (int)this.GetSize().X, actualPosition.Y.AsInt() - nineGrid.NineGridSize, nineGrid.NineGridSize, nineGrid.NineGridSize), null, color);
            }


            if (nineGrid.MiddleLeft)
            {
                spriteBatch.Draw(Resources.StaticResources.Theme.NineGridLeft.Texture, new Rectangle(actualPosition.X.AsInt() - nineGrid.NineGridSize, actualPosition.Y.AsInt(), nineGrid.NineGridSize, (int)this.GetSize().Y), null, color);
            }

            if (nineGrid.MiddleRight)
            {
                spriteBatch.Draw(Resources.StaticResources.Theme.NineGridRight.Texture, new Rectangle(actualPosition.X.AsInt() + (int)this.GetSize().X, actualPosition.Y.AsInt(), nineGrid.NineGridSize, (int)this.GetSize().Y), null, color);
            }


            if (nineGrid.BottomLeft)
            {
                spriteBatch.Draw(Resources.StaticResources.Theme.NineGridBottomLeft.Texture, new Rectangle(actualPosition.X.AsInt() - nineGrid.NineGridSize, actualPosition.Y.AsInt() + (int)this.GetSize().Y, nineGrid.NineGridSize, nineGrid.NineGridSize), null, color);
            }


            if (nineGrid.BottomCenter)
            {
                spriteBatch.Draw(Resources.StaticResources.Theme.NineGridBottom.Texture, new Rectangle(actualPosition.X.AsInt(), actualPosition.Y.AsInt() + (int)this.GetSize().Y, (int)this.GetSize().X, nineGrid.NineGridSize), null, color);
            }

            if (nineGrid.BottomRight)
            {
                spriteBatch.Draw(Resources.StaticResources.Theme.NineGridBottomRight.Texture, new Rectangle(actualPosition.X.AsInt() + (int)this.GetSize().X, actualPosition.Y.AsInt() + (int)this.GetSize().Y, nineGrid.NineGridSize, nineGrid.NineGridSize), null, color);
            }

        }
    

        base.Draw(spriteBatch);
}
}

