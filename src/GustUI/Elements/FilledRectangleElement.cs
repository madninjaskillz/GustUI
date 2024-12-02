using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GustUI.Elements;

[ElementTraits(typeof(BackgroundFillTrait))]
public class FilledRectangleElement : RectangleElement
{
    public FilledRectangleElement() { }
    public FilledRectangleElement(int left, int top, int width, int height, TVFill fill, int border = 0, Color? borderColor = null)
    {
        Set<PositionTrait>(new TVVector(left, top));
        Set<SizeTrait>(new TVVector(width, height));

        Set<BackgroundFillTrait>(fill);

        if (border > 0)
        {
            Set<BorderSizeTrait>(new TVInt(border));
            if (borderColor.HasValue)
            {
                Set<BorderColorTrait>(new TVColor(borderColor.Value));
            }
        }
    }
    public override void Draw(SpriteBatch spriteBatch)
    {
        BackgroundFillTrait fill = ElementTrait<BackgroundFillTrait>();
        Ensure.NotNull(fill, nameof(fill));

        TVVector actualPosition = this.GetActualPosition();
        TVVector size = this.ElementTrait<SizeTrait>().Value();
        Rectangle rect = new Rectangle(actualPosition.X.AsInt(), actualPosition.Y.AsInt(), size.X.AsInt(), size.Y.AsInt());
        Ensure.NotNull(rect, nameof(rect));
        
        switch (fill.Value())
        {
            case TVFillSolidColor solidColor:
                {
                    spriteBatch.DrawFilledRectangle(rect, solidColor.Color);
                    break;
                }
            case TVFillImage image:
                {
                    spriteBatch.Draw(image.Texture, rect, Color.White);
                    break;
                }
            case TVFillSimpleGradient image:
                {
                    spriteBatch.Draw(image.Texture, rect, Color.White);
                    break;
                }
        }
        
        base.Draw(spriteBatch);
    }
}