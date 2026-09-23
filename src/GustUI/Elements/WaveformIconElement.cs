using System;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using Microsoft.Xna.Framework;

namespace GustUI.Elements;

/// <summary>
/// A small audio-waveform mark drawn as geometry: a row of round-capped
/// vertical strokes of varying height, centred in its box.
///
/// It exists because the symbol font has no waveform. Segoe MDL2 has notes,
/// speakers, equalisers and charts, and none of them says "waveform", which
/// is the one thing a waveform setting needs its icon to say. It is drawn to
/// sit beside those glyphs: the ink fills the same share of the box a glyph
/// fills of its em square, and the stroke is a glyph's stroke weight at that
/// size. Colour comes from <see cref="ForegroundColorTrait"/>, so a live
/// (Func) colour follows the theme.
///
/// It declares no pointer traits: placed inside a row or a button, that
/// keeps the hover, press and click.
/// </summary>
[ElementTraits(
    typeof(PositionTrait),
    typeof(SizeTrait),
    typeof(ForegroundColorTrait))]
public class WaveformIconElement : Element
{
    /// <summary>Stroke heights as fractions of the ink height, left to right.
    /// Loud in the middle and tapering at both ends, so it reads as a sound
    /// rather than as a bar chart.</summary>
    private static readonly float[] Heights = { 0.28f, 0.62f, 0.4f, 1f, 0.7f, 0.45f, 0.22f };

    /// <summary>Share of the box the ink spans, matching how much of its em
    /// square a Segoe MDL2 glyph fills.</summary>
    public float Scale { get; set; } = 0.8f;

    public override void Draw()
    {
        Vector2 at = this.GetActualXnaPosition();
        Vector2 box = CachedSizeTrait.Value().AsXna;
        float side = Math.Min(box.X, box.Y);
        if (side >= 4f)
        {
            Color colour = ElementTrait<ForegroundColorTrait>().Value().AsXna;
            var manager = Resources.StaticResources.DrawManager;

            // A glyph's stroke is about a sixteenth of its em; a touch
            // heavier here, since separate strokes have no joins to carry
            // weight the way a glyph's outline does.
            float thickness = Math.Max(1.25f, side / 13f);
            float ink = side * Scale;
            float span = ink - thickness;
            float step = span / (Heights.Length - 1);
            Vector2 centre = at + (box / 2f);
            float left = centre.X - (span / 2f);

            for (int i = 0; i < Heights.Length; i++)
            {
                float half = Math.Max(0f, ((ink * Heights[i]) - thickness) / 2f);
                float x = left + (i * step);
                manager.DrawRoundCapLine(
                    new Vector2(x, centre.Y - half), new Vector2(x, centre.Y + half), colour, thickness);
            }
        }

        base.Draw();
    }
}
