﻿using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static GustUI.Managers.InputManager;

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
                Set<BorderFillTrait>(new TVBorderColorFill(borderColor.Value));
            }
        }
    }
    public override void Draw()
    {
        BackgroundFillTrait fill = ElementTrait<BackgroundFillTrait>();
        Ensure.NotNull(fill, nameof(fill));

        TVVector actualPosition = this.GetActualPosition();
        TVVector size = this.ElementTrait<SizeTrait>().Value();
        Rectangle rect = new Rectangle(actualPosition.X.AsInt(), actualPosition.Y.AsInt(), size.X.AsInt(), size.Y.AsInt());
        Ensure.NotNull(rect, nameof(rect));

        var fillType = fill.Value();

        if (fillType is TVSmartFill smartFill)
        {
            switch (Resources.StaticResources.InputManager.GetElementState(this))
            {
                case ElementState.Hovered:
                    fillType = smartFill.States.HoveredFill;
                    break;
                case ElementState.Pressed:
                    fillType = smartFill.States.PressedFill;
                    break;
                default:
                    fillType = smartFill.States.NormalFill;
                    break;
            }
        }

        if (fillType is TVBlurFill blurFill)
        {
            var rt = Resources.StaticResources.DrawManager.GetBlurredTargetClone(blurFill.Ratio);
            if (rt != null)
            {
                Resources.StaticResources.DrawManager.Draw(rt, rect, rect, Color.White * blurFill.Opacity);
            }
            fillType = blurFill.OverlayFill;
        }

        switch (fillType)
        {
            case TVFillSolidColor solidColor:
                Resources.StaticResources.DrawManager.DrawFilledRectangle(rect, solidColor.Color * solidColor.Opacity);
                break;
            case TVFillImage image:
                Resources.StaticResources.DrawManager.Draw(image.Texture, rect, Color.White * image.Opacity);
                break;
            case TVFillSimpleGradient image:
                Resources.StaticResources.DrawManager.Draw(image.Texture, rect, Color.White * image.Opacity);
                break;
            case TVVideoFill video:
                var texture = video.GetTexture();
                if (texture != null)
                {
                    Resources.StaticResources.DrawManager.Draw(texture, rect, Color.White * video.Opacity);
                }
                break;
        }

        
        base.Draw();
    }
}