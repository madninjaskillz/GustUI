using System;
using System.Diagnostics;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GustUI.Elements;

/// <summary>
/// A stereo VU meter: two vertical bars (left | right) growing upward from the
/// element's bottom edge, each with a peak-hold tick that decays over time.
/// Immediate-mode in <see cref="Draw"/> (the KnobElement pattern — live data
/// that changes every frame would thrash any baked texture).
///
/// Feed it levels via <see cref="SetLevels"/> (0..1 per side, e.g. max |peak|
/// since the last poll) at any rate; the element does the display dynamics
/// itself, frame-rate independently:
///  - the bar jumps up to a new level instantly and falls at
///    <see cref="FallPerSecond"/> when the level drops (so block-rate peak
///    reports read as continuous motion);
///  - the peak-hold tick jumps to the bar and decays multiplicatively at
///    <see cref="HoldDecayPerFrame"/> per 60fps-frame (the ezmuze3 legacy
///    feel: <c>decay = lerp(0, decay, 0.99)</c> each frame).
/// Bar color runs green → red with level (legacy: <c>Color(v, 1-v, 0)</c>)
/// unless <see cref="UseLevelColor"/> is off, then <see cref="BarColor"/>.
/// </summary>
[ElementTraits(typeof(PositionTrait), typeof(SizeTrait))]
public class VuMeterElement : Element
{
    public Color BackColor { get; set; } = new Color(12, 12, 16);

    /// <summary>Bar color when <see cref="UseLevelColor"/> is false.</summary>
    public Color BarColor { get; set; } = new Color(90, 200, 110);

    /// <summary>Green→red gradient by level (the legacy ezmuze3 look). Default on.</summary>
    public bool UseLevelColor { get; set; } = true;

    /// <summary>Gap between the two bars, px.</summary>
    public int BarGap { get; set; } = 2;

    /// <summary>How fast a falling bar drops, in full-scale units per second.</summary>
    public float FallPerSecond { get; set; } = 2.2f;

    /// <summary>Multiplicative peak-hold decay per 60fps frame (legacy 0.99).</summary>
    public float HoldDecayPerFrame { get; set; } = 0.99f;

    /// <summary>Height of the peak-hold tick, px.</summary>
    public int HoldTickHeight { get; set; } = 2;

    /// <summary>Level (0..1 full-scale) at/above which a side is considered
    /// clipped — design-guide.md §7.2.</summary>
    public float ClipThreshold { get; set; } = 0.999f;

    /// <summary>How long the clip indicator stays lit after the last clip,
    /// seconds — auto-resets rather than latching until clicked (locked
    /// design-guide.md §7.2 decision).</summary>
    public float ClipHoldSeconds { get; set; } = 2.5f;

    /// <summary>Height of the clip indicator segment at the top of a bar, px.</summary>
    public int ClipTickHeight { get; set; } = 3;

    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double lastDrawSeconds;

    private float shownL, shownR;   // displayed bar levels (with fall applied)
    private float holdL, holdR;     // peak-hold tick levels
    private double clipLitUntilL, clipLitUntilR; // clock seconds the clip indicator stays lit until

    /// <summary>
    /// Reports the current input levels (0..1 per side). Call at any rate —
    /// per frame, or whenever fresh peaks arrive; passing 0 lets the bars
    /// fall away (e.g. when the transport stops).
    /// </summary>
    public void SetLevels(float left, float right)
    {
        left = MathHelper.Clamp(left, 0f, 1f);
        right = MathHelper.Clamp(right, 0f, 1f);
        if (left > shownL)
        {
            shownL = left;
        }

        if (right > shownR)
        {
            shownR = right;
        }

        targetL = left;
        targetR = right;
    }

    private float targetL, targetR;

    /// <summary>Snaps everything (bars, holds) to silence.</summary>
    public void Reset()
    {
        shownL = shownR = holdL = holdR = targetL = targetR = 0f;
    }

    public override void Draw()
    {
        // Display dynamics on wall-clock dt so decay feel survives any frame
        // rate (legacy tuned at 60fps).
        double now = clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Min(now - lastDrawSeconds, 0.25);
        lastDrawSeconds = now;

        float fall = FallPerSecond * dt;
        shownL = Math.Max(targetL, shownL - fall);
        shownR = Math.Max(targetR, shownR - fall);

        float holdDecay = (float)Math.Pow(HoldDecayPerFrame, dt * 60.0);
        holdL *= holdDecay;
        holdR *= holdDecay;
        if (shownL > holdL)
        {
            holdL = shownL;
        }

        if (shownR > holdR)
        {
            holdR = shownR;
        }

        // Clip-latch (design-guide.md §7.2): a hit re-arms the hold window;
        // auto-resets ClipHoldSeconds after the LAST clip, not the first.
        if (targetL >= ClipThreshold) { clipLitUntilL = now + ClipHoldSeconds; }
        if (targetR >= ClipThreshold) { clipLitUntilR = now + ClipHoldSeconds; }

        Vector2 pos = this.GetActualXnaPosition();
        Vector2 size = this.GetSize().AsXna;
        int w = (int)size.X;
        int h = (int)size.Y;
        if (w >= 3 && h >= 4)
        {
            var manager = Resources.StaticResources.DrawManager;

            manager.DrawFilledRectangle(new Rectangle((int)pos.X, (int)pos.Y, w, h), BackColor);

            int gap = Math.Min(BarGap, w - 2);
            int barW = (w - gap) / 2;
            int rightX = (int)pos.X + barW + gap;

            DrawBar(manager, (int)pos.X, (int)pos.Y, barW, h, shownL, holdL, now < clipLitUntilL);
            DrawBar(manager, rightX, (int)pos.Y, barW, h, shownR, holdR, now < clipLitUntilR);
        }

        base.Draw();
    }

    private void DrawBar(Managers.DrawManager manager, int x, int top, int barW, int h, float level, float hold, bool clipped)
    {
        int barH = (int)(h * level);
        if (barH > 0)
        {
            manager.DrawFilledRectangle(new Rectangle(x, top + h - barH, barW, barH), LevelColor(level));
        }

        int tickH = Math.Min(HoldTickHeight, h);
        int tickY = top + h - (int)(h * hold) - tickH;
        if (hold > 0.004f)
        {
            manager.DrawFilledRectangle(new Rectangle(x, Math.Max(top, tickY), barW, tickH), LevelColor(hold));
        }

        if (clipped)
        {
            int clipH = Math.Min(ClipTickHeight, h);
            manager.DrawFilledRectangle(new Rectangle(x, top, barW, clipH), Resources.StaticResources.Theme.AccentMuteOn);
        }
    }

    private Color LevelColor(float v)
    {
        return UseLevelColor ? new Color(v, 1f - v, 0f) : BarColor;
    }
}
