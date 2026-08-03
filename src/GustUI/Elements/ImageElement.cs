using System;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GustUI.Elements;

/// <summary>How an <see cref="ImageElement"/> maps its texture onto its box.</summary>
public enum ImageScaleMode
{
    /// <summary>Fill the box exactly, distorting the aspect ratio if needed —
    /// what a <see cref="TVFillImage"/> background has always done.</summary>
    Stretch = 0,

    /// <summary>Letterbox: the whole image fits inside the box, centred, with
    /// its aspect preserved. Never magnifies past 1:1.</summary>
    Fit = 1,

    /// <summary>Crop: the box is covered completely, aspect preserved,
    /// overflow trimmed equally on both sides.</summary>
    Cover = 2,

    /// <summary>Draw at native pixel size, centred (no scaling at all).</summary>
    Center = 3,
}

/// <summary>
/// A first-class BITMAP element: give it a <see cref="Texture2D"/> and a box
/// and it draws the picture.
///
/// Before this, GustUI could only show an arbitrary image by putting a
/// <see cref="TVFillImage"/> in a <see cref="FilledRectangleElement"/>'s
/// <see cref="BackgroundFillTrait"/> — which works, but always STRETCHES to
/// the element rect, is a background rather than content, and makes the
/// caller reach through the fill-trait indirection to say "show this
/// picture". Custom-skinned UIs (a module author's own art on a synth panel)
/// want the picture as the thing itself, with a real aspect policy
/// (<see cref="ScaleMode"/>) and cropping handled here rather than by the
/// caller pre-scaling textures.
///
/// A null <see cref="Texture"/> draws nothing at all (no placeholder box), so
/// an element whose art has gone missing degrades to empty space instead of
/// a broken-image blot — the contract a skin system needs.
/// </summary>
[ElementTraits(
    typeof(PositionTrait),
    typeof(SizeTrait),
    typeof(ChildrenTrait),
    typeof(OnMousePress),
    typeof(OnMouseRelease),
    typeof(OnMouseButtonHeldDown))]
public class ImageElement : Element
{
    public ImageElement()
    {
    }

    public ImageElement(Texture2D texture, ImageScaleMode scaleMode = ImageScaleMode.Stretch)
    {
        Texture = texture;
        ScaleMode = scaleMode;
    }

    public ImageElement(Texture2D texture, int left, int top, int width, int height,
        ImageScaleMode scaleMode = ImageScaleMode.Stretch)
        : this(texture, scaleMode)
    {
        Set<PositionTrait>(new TVVector(left, top));
        Set<SizeTrait>(new TVVector(width, height));
    }

    /// <summary>The picture. Null draws nothing (see the class doc).</summary>
    public Texture2D Texture { get; set; }

    public ImageScaleMode ScaleMode { get; set; } = ImageScaleMode.Stretch;

    /// <summary>Multiplied into every texel — white leaves the image
    /// untouched; a colour turns a grayscale/alpha mask into a themed graphic
    /// (the tint trick <see cref="TVFillImage"/> already uses for waveforms).</summary>
    public Color Tint { get; set; } = Color.White;

    public float Opacity { get; set; } = 1f;

    public override void Draw()
    {
        Texture2D texture = Texture;
        if (texture != null && Opacity > 0f)
        {
            Vector2 position = this.GetActualXnaPosition();
            Vector2 size = this.GetSize().AsXna;
            if (size.X >= 1f && size.Y >= 1f)
            {
                var box = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);
                Resources.StaticResources.DrawManager.Draw(texture, Destination(texture, box), Source(texture, box),
                    Tint * Opacity);
            }
        }

        base.Draw();
    }

    /// <summary>Where the (possibly cropped) texture lands on screen.</summary>
    private Rectangle Destination(Texture2D texture, Rectangle box)
    {
        switch (ScaleMode)
        {
            case ImageScaleMode.Fit:
            {
                float scale = Math.Min(1f, Math.Min(box.Width / (float)texture.Width, box.Height / (float)texture.Height));
                int w = Math.Max(1, (int)(texture.Width * scale));
                int h = Math.Max(1, (int)(texture.Height * scale));
                return new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
            }

            case ImageScaleMode.Center:
            {
                int w = Math.Min(box.Width, texture.Width);
                int h = Math.Min(box.Height, texture.Height);
                return new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
            }

            // Cover crops in SOURCE space (below) and fills the box exactly,
            // as does Stretch.
            default:
                return box;
        }
    }

    /// <summary>The sub-rect of the texture to sample — only Cover/Center
    /// crop; the rest read the whole image.</summary>
    private Rectangle? Source(Texture2D texture, Rectangle box)
    {
        switch (ScaleMode)
        {
            case ImageScaleMode.Cover:
            {
                float scale = Math.Max(box.Width / (float)texture.Width, box.Height / (float)texture.Height);
                int w = Math.Max(1, Math.Min(texture.Width, (int)(box.Width / scale)));
                int h = Math.Max(1, Math.Min(texture.Height, (int)(box.Height / scale)));
                return new Rectangle((texture.Width - w) / 2, (texture.Height - h) / 2, w, h);
            }

            case ImageScaleMode.Center:
            {
                int w = Math.Min(box.Width, texture.Width);
                int h = Math.Min(box.Height, texture.Height);
                return new Rectangle((texture.Width - w) / 2, (texture.Height - h) / 2, w, h);
            }

            default:
                return null;
        }
    }
}
