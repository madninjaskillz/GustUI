using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace GustUI.Elements;

[ElementTraits(
    typeof(PositionTrait),
    typeof(SizeTrait),
    typeof(ChildrenTrait),
    typeof(BorderFillTrait), 
    typeof(BorderSizeTrait),
    typeof(OnMouseButtonHeldDown),
    typeof(OnMousePress), 
    typeof(OnMouseRelease), 
    typeof(OnExitTrait))]
public class RectangleElement : Element
{
    public override void Draw()
    {
        TVVector actualPosition = this.GetActualPosition();

        TVVector size = ElementTrait<SizeTrait>().Value();

        int borderSize = ElementTrait<BorderSizeTrait>().Value().Int;

        if (ElementTrait<BorderFillTrait>().Value() is TVBorderColorFill borderColorFill)
        {
            if (borderSize > 0)
            {
                Resources.StaticResources.DrawManager.DrawRectangle(actualPosition.Rectangle(size), borderColorFill.Color, borderSize);
            }
        }

        if (ElementTrait<BorderFillTrait>().Value() is TVBorder9Grid nineGrid)
        {
            Color color = Color.White * nineGrid.Opacity;
            if (nineGrid.TopLeft)
            {
                Resources.StaticResources.DrawManager.Draw(Resources.StaticResources.Theme.NineGridTopLeft.Texture, new Rectangle(actualPosition.X.AsInt() - (int)nineGrid.NineGridSize, actualPosition.Y.AsInt() - (int)nineGrid.NineGridSize, (int)nineGrid.NineGridSize, (int)nineGrid.NineGridSize), null, color);
            }


            if (nineGrid.TopCenter)
            {
                Resources.StaticResources.DrawManager.Draw(Resources.StaticResources.Theme.NineGridTop.Texture, new Rectangle(actualPosition.X.AsInt(), actualPosition.Y.AsInt() - (int)nineGrid.NineGridSize, (int)this.GetSize().X, (int)nineGrid.NineGridSize), null, color);
            }

            if (nineGrid.TopRight)
            {
                Resources.StaticResources.DrawManager.Draw(Resources.StaticResources.Theme.NineGridTopRight.Texture, new Rectangle(actualPosition.X.AsInt() + (int)this.GetSize().X, actualPosition.Y.AsInt() - (int)nineGrid.NineGridSize, (int)nineGrid.NineGridSize, (int)nineGrid.NineGridSize), null, color);
            }


            if (nineGrid.MiddleLeft)
            {
                Resources.StaticResources.DrawManager.Draw(Resources.StaticResources.Theme.NineGridLeft.Texture, new Rectangle(actualPosition.X.AsInt() - (int)nineGrid.NineGridSize, actualPosition.Y.AsInt(), (int)nineGrid.NineGridSize, (int)this.GetSize().Y), null, color);
            }

            if (nineGrid.MiddleRight)
            {
                Resources.StaticResources.DrawManager.Draw(Resources.StaticResources.Theme.NineGridRight.Texture, new Rectangle(actualPosition.X.AsInt() + (int)this.GetSize().X, actualPosition.Y.AsInt(), (int)nineGrid.NineGridSize, (int)this.GetSize().Y), null, color);
            }


            if (nineGrid.BottomLeft)
            {
                Resources.StaticResources.DrawManager.Draw(Resources.StaticResources.Theme.NineGridBottomLeft.Texture, new Rectangle(actualPosition.X.AsInt() - (int)nineGrid.NineGridSize, actualPosition.Y.AsInt() + (int)this.GetSize().Y, (int)nineGrid.NineGridSize, (int)nineGrid.NineGridSize), null, color);
            }


            if (nineGrid.BottomCenter)
            {
                Resources.StaticResources.DrawManager.Draw(Resources.StaticResources.Theme.NineGridBottom.Texture, new Rectangle(actualPosition.X.AsInt(), actualPosition.Y.AsInt() + (int)this.GetSize().Y, (int)this.GetSize().X, (int)nineGrid.NineGridSize), null, color);
            }

            if (nineGrid.BottomRight)
            {
                Resources.StaticResources.DrawManager.Draw(Resources.StaticResources.Theme.NineGridBottomRight.Texture, new Rectangle(actualPosition.X.AsInt() + (int)this.GetSize().X, actualPosition.Y.AsInt() + (int)this.GetSize().Y, (int)nineGrid.NineGridSize, (int)nineGrid.NineGridSize), null, color);
            }

        }

        base.Draw();
    }
}

