using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace GustUI.Elements;

/// <summary>
/// The pure maths behind <see cref="TooltipElement"/>: how wide a label may
/// grow before it wraps, which lines it shows, and where it sits so that it
/// stays on screen. No fonts and no drawing - text is measured through a
/// function - so every rule here is unit-testable.
///
/// Why it exists (ezmuze, 2026-10-02): the tooltip measured each line and drew
/// it at whatever width it came to. A store tile's tooltip carries the
/// module's whole description, several hundred characters on one line, and it
/// ran straight across a 4K screen and off the far edge. A tooltip wraps at a
/// reading measure, never takes more than a fraction of the window, and never
/// leaves the window.
/// </summary>
public static class TooltipLayout
{
    /// <summary>The widest a tooltip box may be, in logical px (design-guide
    /// §9: ~400). At the 16px tooltip font that is a 55-60 character line, a
    /// normal reading measure.</summary>
    public const float MaxBoxWidth = 400f;

    /// <summary>The widest a tooltip box may be as a share of the window, so
    /// a small window still gets a label rather than a banner.</summary>
    public const float MaxWindowWidthFraction = 0.4f;

    /// <summary>The tallest a tooltip box may be as a share of the window.
    /// Anything longer is cut at its last whole line with "...".</summary>
    public const float MaxWindowHeightFraction = 0.5f;

    /// <summary>The narrowest wrap the window fraction may force, so a tiny
    /// window does not stack a description a word or two per line.</summary>
    public const float MinBoxWidth = 160f;

    /// <summary>Cursor-to-label offset when the label sits below-right of the
    /// pointer (where it goes whenever it fits).</summary>
    public static readonly Vector2 CursorOffset = new Vector2(14, 20);

    /// <summary>The gap kept between the pointer and a label flipped to the
    /// pointer's left or above it.</summary>
    public const float FlipGap = 4f;

    /// <summary>
    /// The widest a box may be in a window <paramref name="windowWidth"/>
    /// wide: <see cref="MaxBoxWidth"/>, or <see cref="MaxWindowWidthFraction"/>
    /// of the window if that is less, but never under
    /// <see cref="MinBoxWidth"/> (nor wider than the window itself).
    /// </summary>
    public static float MaxBoxWidthFor(float windowWidth)
    {
        float width = Math.Min(MaxBoxWidth, windowWidth * MaxWindowWidthFraction);
        width = Math.Max(width, MinBoxWidth);
        return Math.Max(1f, Math.Min(width, windowWidth));
    }

    /// <summary>
    /// The lines a tooltip shows. Every line break in <paramref name="text"/>
    /// is kept; a line that fits <paramref name="wrapWidth"/> is left exactly
    /// as written (so a short tooltip is untouched), and a longer one is
    /// word-wrapped with <see cref="TextElement"/>'s wrapper - the same
    /// greedy wrap body text uses, which also breaks a word wider than the
    /// box. When there are more than <paramref name="maxLines"/>, the last
    /// shown line ends in "...".
    /// </summary>
    public static List<string> Lines(string text, Func<string, float> measureWidth, float wrapWidth, int maxLines)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return lines;
        }

        foreach (string hard in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (measureWidth(hard) <= wrapWidth)
            {
                lines.Add(hard);
                continue;
            }

            lines.AddRange(TextElement.WrapText(hard, measureWidth, wrapWidth).Split('\n'));
        }

        maxLines = Math.Max(1, maxLines);
        if (lines.Count > maxLines)
        {
            lines.RemoveRange(maxLines, lines.Count - maxLines);
            string last = lines[maxLines - 1].TrimEnd();

            // Always visibly cut, even when the kept line would fit as it is:
            // trim it until it and the dots fit together. Three ASCII dots,
            // as TextElement.Ellipsise uses - the SDF fonts have no U+2026.
            int keep = last.Length;
            while (keep > 0 && measureWidth(last.Substring(0, keep).TrimEnd() + "...") > wrapWidth)
            {
                keep--;
            }

            lines[maxLines - 1] = last.Substring(0, keep).TrimEnd() + "...";
        }

        return lines;
    }

    /// <summary>How many lines fit in <see cref="MaxWindowHeightFraction"/> of
    /// a window <paramref name="windowHeight"/> tall, padding included. At
    /// least one.</summary>
    public static int MaxLinesFor(float windowHeight, float lineHeight, float padY)
    {
        if (lineHeight <= 0f)
        {
            return 1;
        }

        float room = (windowHeight * MaxWindowHeightFraction) - (padY * 2f);
        return Math.Max(1, (int)Math.Floor(room / lineHeight));
    }

    /// <summary>
    /// Where a <paramref name="boxSize"/> label goes for a pointer at
    /// <paramref name="cursor"/>. Below-right of it when that fits; flipped to
    /// the pointer's left when it would cross the right edge, and above it
    /// when it would cross the bottom; then clamped into the window, so it is
    /// always fully on screen (top-left wins when the box is bigger than the
    /// window).
    /// </summary>
    public static Vector2 Place(Vector2 cursor, Vector2 boxSize, Vector2 windowSize)
    {
        float x = cursor.X + CursorOffset.X;
        if (x + boxSize.X > windowSize.X)
        {
            x = cursor.X - FlipGap - boxSize.X;
        }

        float y = cursor.Y + CursorOffset.Y;
        if (y + boxSize.Y > windowSize.Y)
        {
            y = cursor.Y - FlipGap - boxSize.Y;
        }

        x = Math.Max(0f, Math.Min(x, windowSize.X - boxSize.X));
        y = Math.Max(0f, Math.Min(y, windowSize.Y - boxSize.Y));
        return new Vector2(x, y);
    }
}
