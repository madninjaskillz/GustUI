using System;
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
    /// <summary>As the <see cref="Color"/> overload, but the border colour is
    /// read every frame. For a border taken from the theme: a baked one keeps
    /// the palette it was built under across a light/dark switch (#231).</summary>
    public FilledRectangleElement(int left, int top, int width, int height, TVFill fill, int border, Func<Color> borderColor)
        : this()
    {
        Set<PositionTrait>(new TVVector(left, top));
        Set<SizeTrait>(new TVVector(width, height));
        Set<BackgroundFillTrait>(fill);

        if (border > 0)
        {
            Set<BorderSizeTrait>(new TVInt(border));
            if (borderColor != null)
            {
                Set<BorderFillTrait>(new TVBorderColorFill(borderColor));
            }
        }
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
        using (Managers.Telemetry.Scope("Draw.FilledRect"))
        {
            DrawFill();
        }

        base.Draw();
    }

    private void DrawFill()
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

        DrawFillValue(fillType, rect, this);
    }

    /// <summary>The children's rectangles for <see cref="IsCoveredByOpaqueChildren"/>,
    /// reused every frame.</summary>
    private int[] coverRects;

    /// <summary>
    /// Whether this element's own children hide every pixel of it: visible,
    /// fully opaque, solid-filled children whose rectangles together leave no
    /// gap (<see cref="RectCover"/>, exact). For a fill too costly to draw
    /// for nobody - a <see cref="TVSpriteSheetFill"/> behind a maximised
    /// window. Allocates nothing after the first call.
    /// </summary>
    public bool IsCoveredByOpaqueChildren()
    {
        TVElements children = Children;
        if (children == null || children.Count == 0 || CachedSizeTrait == null)
        {
            return false;
        }

        coverRects ??= new int[RectCover.MaxRects * 4];
        int n = 0;
        foreach (Element child in children.Items)
        {
            if (!child.Visible || child.Opacity < 1f || child.CachedSizeTrait == null
                || !(child is FilledRectangleElement filled) || filled.backgroundFillTrait == null
                || !(filled.backgroundFillTrait.Value() is TVFillSolidColor solid)
                || solid.Opacity < 1f || solid.ResolvedColor.A < 255)
            {
                continue;
            }

            if (n == RectCover.MaxRects)
            {
                return false;
            }

            Vector2 at = child.GetActualXnaPosition();
            TVVector size = child.CachedSizeTrait.Value();
            coverRects[n * 4] = (int)Math.Round(at.X);
            coverRects[(n * 4) + 1] = (int)Math.Round(at.Y);
            coverRects[(n * 4) + 2] = (int)Math.Round(at.X + size.X) - coverRects[n * 4];
            coverRects[(n * 4) + 3] = (int)Math.Round(at.Y + size.Y) - coverRects[(n * 4) + 1];
            n++;
        }

        if (n == 0)
        {
            return false;
        }

        Vector2 self = this.GetActualXnaPosition();
        TVVector own = CachedSizeTrait.Value();
        int sx = (int)Math.Round(self.X);
        int sy = (int)Math.Round(self.Y);
        return RectCover.Covers(sx, sy, (int)Math.Round(self.X + own.X) - sx, (int)Math.Round(self.Y + own.Y) - sy,
            new ReadOnlySpan<int>(coverRects, 0, n * 4));
    }

    private static void DrawFillValue(TVFill fillType, Rectangle rect, FilledRectangleElement owner)
    {
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
            case TVFillHatch hatch:
                if (hatch.Background.HasValue)
                {
                    Resources.StaticResources.DrawManager.DrawFilledRectangle(
                        rect, hatch.Background.Value * hatch.Opacity);
                }

                Extensions.ShapeDrawExtensions.DrawHatch(Resources.StaticResources.DrawManager, rect,
                    hatch.ResolvedColor * hatch.Opacity, hatch.Spacing, hatch.Thickness, hatch.Angle);
                break;
            case TVFillLoopOutline loop:
                Resources.StaticResources.DrawManager.DrawLoopOutline(rect,
                    loop.ResolvedColor * loop.Opacity, loop.Radius, loop.Thickness,
                    loop.Seams == null ? System.ReadOnlySpan<float>.Empty
                        : new System.ReadOnlySpan<float>(loop.Seams, 0, System.Math.Min(loop.SeamCount, loop.Seams.Length)));
                break;
            case TVFillImage image:
                Resources.StaticResources.DrawManager.Draw(image.Texture, rect, image.Tint * image.Opacity);
                break;
            case TVFillSimpleGradient gradient:
                Resources.StaticResources.DrawManager.DrawFilledRectangleGradient(
                    rect, gradient.ResolvedPrimary * gradient.Opacity, gradient.ResolvedSecondary * gradient.Opacity, gradient.Direction);
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
                if (video.Blur > 0)
                {
                    // Decoded and blurred in the pre-pass (tagged there): this
                    // asks for the next frame and draws the last. Sized in
                    // device pixels, which is what the blur radius is in.
                    float scale = Resources.StaticResources.DrawManager.RenderScale;
                    videoTexture = video.GetBlurredTexture((int)(rect.Width * scale), (int)(rect.Height * scale));
                }
                else
                {
                    using (Managers.Telemetry.Scope("Draw.VideoBackground.GetTexture"))
                    {
                        videoTexture = video.GetTexture();
                    }
                }

                if (videoTexture != null)
                {
                    using (Managers.Telemetry.Scope("Draw.VideoBackground.Blit"))
                    {
                        Resources.StaticResources.DrawManager.Draw(videoTexture, rect, Color.White * video.Opacity);
                    }
                }

                break;
            case TVSpriteSheetFill sheet:
            {
                // Under opaque windows that leave no gap there is nothing to
                // see: no redraw, no stretched quad, no underlay.
                if (owner != null && owner.IsCoveredByOpaqueChildren())
                {
                    sheet.Covered = true;
                    break;
                }

                sheet.Covered = false;
                // Sized in device pixels, which is what the blur radius is in.
                float sheetScale = Resources.StaticResources.DrawManager.RenderScale;
                Texture2D picture = sheet.GetTexture((int)(rect.Width * sheetScale), (int)(rect.Height * sheetScale));
                float shown = sheet.PictureOpacity;
                if (shown < 1f && sheet.Underlay != null && !(sheet.Underlay is TVSpriteSheetFill))
                {
                    DrawFillValue(sheet.Underlay, rect, null);
                }

                if (picture != null && shown > 0f)
                {
                    using (Managers.Telemetry.Scope("Draw.SpriteSheetBackground.Blit"))
                    {
                        Resources.StaticResources.DrawManager.Draw(picture, rect, Color.White * (sheet.Opacity * shown));
                    }
                }

                break;
            }
        }
    }
}