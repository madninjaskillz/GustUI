using System;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using Microsoft.Xna.Framework;

namespace GustUI.Elements;

/// <summary>
/// A stroked chevron drawn as geometry, centred in its box and turned to
/// <see cref="Angle"/> — 0 points right ("&gt;"), Pi points left ("&lt;").
///
/// It exists because a chevron that TURNS cannot be a glyph. The symbol font
/// has one glyph per direction, so a glyph chevron can only snap between
/// them; this one can be anywhere in between, which is what a disclosure
/// control that rotates as its panel moves needs. Colour comes from
/// <see cref="ForegroundColorTrait"/>, so a live (Func) colour follows the
/// theme.
///
/// It declares no pointer traits: placed inside a button, the button keeps
/// the hover, press and click.
/// </summary>
[ElementTraits(
    typeof(PositionTrait),
    typeof(SizeTrait),
    typeof(ForegroundColorTrait))]
public class ChevronElement : Element
{
    /// <summary>Direction the chevron points, in radians. 0 is right.</summary>
    public float Angle { get; set; }

    /// <summary>Stroke width in logical pixels.</summary>
    public float Thickness { get; set; } = 2f;

    /// <summary>Chevron height as a fraction of the box's smaller side.</summary>
    public float Scale { get; set; } = 0.55f;

    public override void Draw()
    {
        Vector2 at = this.GetActualXnaPosition();
        Vector2 box = CachedSizeTrait.Value().AsXna;
        Color colour = ElementTrait<ForegroundColorTrait>().Value().AsXna;
        Resources.StaticResources.DrawManager.DrawChevron(
            at + box / 2f, Math.Min(box.X, box.Y) * Scale, Angle, colour, Thickness);
        base.Draw();
    }
}
