using GustUI.Attributes;
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
    // Hot trait reference (resolved once; the trait set never shrinks).
    private readonly BackgroundFillTrait backgroundFillTrait;

    public FilledRectangleElement()
    {
        backgroundFillTrait = ElementTrait<BackgroundFillTrait>();
    }
    public FilledRectangleElement(int left, int top, int width, int height, TVFill fill, int border = 0, Color? borderColor = null)
        : this()
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
        BackgroundFillTrait fill = backgroundFillTrait;
        Ensure.NotNull(fill, nameof(fill));

        Vector2 actualPosition = this.GetActualXnaPosition();
        TVVector size = CachedSizeTrait.Value();
        Rectangle rect = new Rectangle(actualPosition.X.AsInt(), actualPosition.Y.AsInt(), size.X.AsInt(), size.Y.AsInt());

        var fillType = fill.Value();

        if (fillType is TVSmartFill smartFill)
        {
            // TVSmartFill.Resolve eases the hover/press crossfade itself
            // (design-guide.md §5) when all three states are gradients —
            // falls back to this same instant switch otherwise.
            fillType = smartFill.Resolve(Resources.StaticResources.InputManager.GetElementState(this));
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
                Resources.StaticResources.DrawManager.DrawFilledRectangle(rect, solidColor.ResolvedColor * solidColor.Opacity);
                break;
            case TVFillRoundedColor rounded:
                Color? roundedBorder = rounded.ResolvedBorderColor;
                if (roundedBorder.HasValue && rounded.BorderSize > 0)
                {
                    Resources.StaticResources.DrawManager.DrawRoundedBorder(rect,
                        roundedBorder.Value * rounded.Opacity,
                        rounded.ResolvedColor * rounded.Opacity,
                        rounded.Radius, rounded.BorderSize);
                }
                else
                {
                    Resources.StaticResources.DrawManager.DrawRoundedRectangle(rect,
                        rounded.ResolvedColor * rounded.Opacity, rounded.Radius);
                }

                break;
            case TVFillImage image:
                Resources.StaticResources.DrawManager.Draw(image.Texture, rect, image.Tint * image.Opacity);
                break;
            case TVFillSimpleGradient gradient:
                Resources.StaticResources.DrawManager.DrawFilledRectangleGradient(
                    rect, gradient.PrimaryColor * gradient.Opacity, gradient.SecondaryColor * gradient.Opacity, gradient.Direction);
                break;
            case TVVideoFill video:
                // Tagged (not just inline) because there's no occlusion
                // culling anywhere in this Draw() call chain — an Element
                // draws unconditionally every frame regardless of whether
                // something fully opaque is on top of it, so the decorative
                // WindowElement background video keeps decoding and drawing
                // for the life of the session, sequencer open or not. Split
                // into two tags (found 2026-08-16) to separate the actual
                // question: is the cost in GetTexture() (frame decode +
                // texture upload) or in the draw call itself?
                Texture2D videoTexture;
                using (Managers.Telemetry.Scope("Draw.VideoBackground.GetTexture"))
                {
                    videoTexture = video.GetTexture();
                }

                if (videoTexture != null)
                {
                    using (Managers.Telemetry.Scope("Draw.VideoBackground.Blit"))
                    {
                        Resources.StaticResources.DrawManager.Draw(videoTexture, rect, Color.White * video.Opacity);
                    }
                }

                break;
        }

        
        base.Draw();
    }
}