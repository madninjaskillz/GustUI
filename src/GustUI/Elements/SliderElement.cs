using System;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Managers;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GustUI.Elements;

/// <summary>
/// A horizontal slider: value 0..1 along the track. Pressing anywhere jumps
/// the thumb to the pointer and starts a drag; dragging uses pointer capture,
/// so fast drags that leave the element keep working (same input model as
/// <see cref="KnobElement"/>). The thumb is an antialiased disc baked once per
/// diameter.
/// </summary>
[ElementTraits(typeof(PositionTrait), typeof(SizeTrait), typeof(OnMousePress), typeof(OnMouseButtonHeldDown), typeof(OnMouseRelease))]
public class SliderElement : Element
{
    public Color TrackColor { get; set; } = new Color(52, 52, 63);
    public Color FillColor { get; set; } = new Color(110, 145, 235);
    public Color ThumbColor { get; set; } = new Color(232, 232, 236);
    /// <summary>
    /// How the TRACK and THUMB paint. The fill, the centre tick and the
    /// live-automation marker are affordances and draw the same over every
    /// skin -- see <see cref="ControlSkin"/>.
    /// </summary>
    public ControlSkin Skin { get; set; } = ControlSkin.Flat;

    public int TrackThickness { get; set; } = 4;
    public int ThumbDiameter { get; set; } = 14;

    /// <summary>Bipolar mode (e.g. pan/balance): the fill draws from the
    /// CENTER (0.5) to the thumb instead of from the left edge, and a
    /// small tick mark always shows where center is — so the control reads
    /// as "how far from center, which direction" instead of the default
    /// "how far from zero" a volume-style slider means.</summary>
    public bool Bipolar { get; set; }

    /// <summary>Center tick mark color (<see cref="Bipolar"/> mode only).</summary>
    public Color CenterMarkColor { get; set; } = new Color(150, 150, 165);

    /// <summary>Normalized 0..1 LIVE automated position (see
    /// <see cref="KnobElement.LiveValue"/> for the full rationale) — null draws
    /// nothing extra. Rendered as a small marker riding ABOVE the track at the
    /// live position, independent of the draggable <see cref="Value"/> thumb.</summary>
    public float? LiveValue { get; set; }

    /// <summary>Accent color for the <see cref="LiveValue"/> marker.</summary>
    public Color LiveColor { get; set; } = new Color(255, 196, 64);

    public Action<float> OnValueChanged;

    /// <summary>Raised when a drag gesture ends (mouse release), with the final
    /// value — the "commit" hook (see <see cref="KnobElement.OnDragCompleted"/>).</summary>
    public Action<float> OnDragCompleted;

    private float value;
    public float Value
    {
        get => value;
        set
        {
            float clamped = MathHelper.Clamp(value, 0f, 1f);
            if (clamped != this.value)
            {
                this.value = clamped;
                OnValueChanged?.Invoke(clamped);
            }
        }
    }

    public SliderElement()
    {
        ElementTrait<OnMousePress>().Set(new TVEvent<ClickEventArgs>(args =>
        {
            CapturePointer();
            Value = ValueAt(args.MouseState.X);
        }));

        ElementTrait<OnMouseButtonHeldDown>().Set(new TVEvent<ClickEventArgs>(args =>
        {
            Value = ValueAt(args.MouseState.X);
        }));

        ElementTrait<OnMouseRelease>().Set(new TVEvent<ClickEventArgs>(args =>
        {
            OnDragCompleted?.Invoke(value);
        }));
    }

    private float ValueAt(float screenX)
    {
        float left = this.GetActualXnaPosition().X;
        float span = this.GetSize().X - ThumbDiameter;
        if (span <= 0f)
        {
            return 0f;
        }

        return (screenX - left - ThumbDiameter / 2f) / span; // Value clamps
    }

    public override void Draw()
    {
        var manager = Resources.StaticResources.DrawManager;
        Vector2 pos = this.GetActualXnaPosition();
        Vector2 size = this.GetSize().AsXna;

        if (size.X > ThumbDiameter && size.Y >= TrackThickness)
        {
            float centerY = pos.Y + size.Y / 2f;
            int trackLeft = (int)(pos.X + ThumbDiameter / 2f);
            int trackWidth = (int)(size.X - ThumbDiameter);
            int trackTop = (int)(centerY - TrackThickness / 2f);
            float thumbCenterX = pos.X + ThumbDiameter / 2f + value * (size.X - ThumbDiameter);

            var trackRect = new Rectangle(trackLeft, trackTop, trackWidth, TrackThickness);
            switch (Skin)
            {
                case ControlSkin.Soft:
                    // A groove pressed INTO the panel: the shadow sits inside
                    // the top edge, which is what inverts the raised look the
                    // thumb has and makes the pair read as track-and-slider.
                    manager.DrawFilledCapsule(new Rectangle(
                        trackRect.X, trackRect.Y - 1, trackRect.Width, trackRect.Height + 2),
                        Color.Black * 0.16f);
                    manager.DrawFilledCapsule(trackRect, TrackColor);
                    break;
                case ControlSkin.Hardware:
                    // Inset channel with a lip along the bottom, and a milled
                    // scale above it -- the tick row is the thing that reads
                    // as a piece of gear rather than a progress bar.
                    manager.DrawFilledRectangle(trackRect, Color.Lerp(TrackColor, Color.Black, 0.45f));
                    manager.DrawFilledRectangle(new Rectangle(
                        trackRect.X, trackRect.Bottom - 1, trackRect.Width, 1),
                        Color.Lerp(TrackColor, Color.White, 0.25f));
                    DrawHardwareScale(manager, trackRect);
                    break;

                case ControlSkin.Amp:
                    // A printed scale and a plain slot. Like the Amp knob, what
                    // carries the value is the THUMB read against marks that do
                    // not change, so the track stays one colour end to end.
                    manager.DrawFilledRectangle(trackRect, Color.Lerp(TrackColor, Color.Black, 0.35f));
                    DrawHardwareScale(manager, trackRect);
                    break;

                case ControlSkin.Neon:
                    // An unlit channel with a thin outline; the fill below does
                    // the glowing.
                    manager.DrawFilledCapsule(trackRect, TrackColor);
                    break;

                case ControlSkin.Pixel:
                    // Chunky recessed trough: dark base, darker top edge, hard
                    // corners. No capsule anywhere on a pixel panel.
                    manager.DrawFilledRectangle(new Rectangle(
                        trackRect.X - 1, trackRect.Y - 1, trackRect.Width + 2, trackRect.Height + 2),
                        Color.Lerp(TrackColor, Color.Black, 0.6f));
                    manager.DrawFilledRectangle(trackRect, TrackColor);
                    break;
                default:
                    manager.DrawFilledRectangle(trackRect, TrackColor);
                    break;
            }

            if (Bipolar)
            {
                float centerX = pos.X + ThumbDiameter / 2f + 0.5f * (size.X - ThumbDiameter);
                int fillLeft = (int)Math.Min(centerX, thumbCenterX);
                int fillWidth = (int)Math.Abs(thumbCenterX - centerX);
                if (fillWidth > 0)
                {
                    DrawFill(manager, new Rectangle(fillLeft, trackTop, fillWidth, TrackThickness));
                }

                // Center tick mark, drawn OVER the fill so it stays visible
                // no matter how far the thumb has traveled.
                int tickHeight = TrackThickness + 6;
                manager.DrawFilledRectangle(new Rectangle((int)centerX - 1, (int)(centerY - tickHeight / 2f), 2, tickHeight), CenterMarkColor);
            }
            else
            {
                int fillWidth = (int)(thumbCenterX - trackLeft);
                if (fillWidth > 0)
                {
                    DrawFill(manager, new Rectangle(trackLeft, trackTop, fillWidth, TrackThickness));
                }
            }

            int d = Math.Min(ThumbDiameter, (int)size.Y);
            if (d >= 4)
            {
                DrawThumb(manager, new Vector2(thumbCenterX, centerY), d / 2f);
            }

            if (LiveValue.HasValue)
            {
                // Live marker rides ABOVE the track (never the draggable
                // thumb itself) so base position and live automated position
                // read as two independent marks.
                float liveX = pos.X + ThumbDiameter / 2f + MathHelper.Clamp(LiveValue.Value, 0f, 1f) * (size.X - ThumbDiameter);
                float ld = Math.Max(4f, d * 0.7f);
                float liveY = trackTop - ld * 0.5f - 1f;
                manager.DrawFilledCircle(new Vector2(liveX, liveY + ld / 2f), ld / 2f, LiveColor);
            }
        }

        base.Draw();
    }

    /// <summary>The travelled part of the track. Rounded under
    /// <see cref="ControlSkin.Soft"/> so it matches the groove it sits in;
    /// square elsewhere.</summary>
    private void DrawFill(DrawManager manager, Rectangle rect)
    {
        switch (Skin)
        {
            case ControlSkin.Soft:
                manager.DrawFilledCapsule(rect, FillColor);
                break;

            case ControlSkin.Neon:
                // Bloom: the same bar three times, growing and fading, which is
                // as close to a glow as geometry gets without a shader.
                for (int i = 2; i >= 0; i--)
                {
                    manager.DrawFilledCapsule(
                        new Rectangle(rect.X, rect.Y - i, rect.Width, rect.Height + i * 2),
                        FillColor * (i == 0 ? 1f : 0.18f / i));
                }

                break;

            case ControlSkin.Amp:
                // Nothing. An amp's slot is not a progress bar -- the cap is
                // read against the printed scale, and filling the track behind
                // it would be a second, competing reading of the same value.
                break;

            default:
                manager.DrawFilledRectangle(rect, FillColor);
                break;
        }
    }

    /// <summary>The grab handle, per skin: a plain disc, a soft raised cap, or
    /// a milled metal one.</summary>
    private void DrawThumb(DrawManager manager, Vector2 centre, float radius)
    {
        switch (Skin)
        {
            case ControlSkin.Soft:
                float offset = Math.Max(1f, radius * 0.28f);
                manager.DrawSoftShadowCircle(centre + new Vector2(offset, offset), radius,
                    Color.Black * 0.28f, offset * 1.5f);
                manager.DrawRadialShadedCircle(centre, radius,
                    Color.Lerp(ThumbColor, Color.White, 0.20f),
                    Color.Lerp(ThumbColor, Color.Black, 0.10f));
                break;

            case ControlSkin.Hardware:
                // Dark collar, bright cap: the same two-tone trick the knob's
                // bezel uses, at slider scale.
                manager.DrawFilledCircle(centre, radius, Color.Lerp(ThumbColor, Color.Black, 0.55f));
                manager.DrawRadialShadedCircle(centre, radius * 0.78f,
                    Color.Lerp(ThumbColor, Color.White, 0.30f),
                    Color.Lerp(ThumbColor, Color.Black, 0.18f));
                break;

            case ControlSkin.Amp:
                // A fader cap, not a bead: a tall block with a light line
                // across its middle, which is the thing you line up with the
                // scale.
                int capW = (int)Math.Max(3f, radius * 1.1f);
                int capH = (int)Math.Max(6f, radius * 2.4f);
                var cap = new Rectangle((int)(centre.X - capW / 2f), (int)(centre.Y - capH / 2f), capW, capH);
                manager.DrawFilledRectangle(cap, Color.Lerp(ThumbColor, Color.Black, 0.55f));
                manager.DrawFilledRectangle(
                    new Rectangle(cap.X + 1, cap.Y + 1, Math.Max(1, cap.Width - 2), Math.Max(1, cap.Height - 2)),
                    Color.Lerp(ThumbColor, Color.Black, 0.25f));
                manager.DrawFilledRectangle(
                    new Rectangle(cap.X, (int)centre.Y - 1, cap.Width, 2), ThumbColor);
                break;

            case ControlSkin.Neon:
                manager.DrawFilledCircle(centre, radius, ThumbColor * 0.25f);
                manager.DrawFilledCircle(centre, radius * 0.62f, ThumbColor);
                break;

            case ControlSkin.Pixel:
                int side = (int)Math.Max(4f, radius * 1.8f);
                var block = new Rectangle((int)(centre.X - side / 2f), (int)(centre.Y - side / 2f), side, side);
                manager.DrawFilledRectangle(block, Color.Lerp(ThumbColor, Color.Black, 0.7f));
                manager.DrawFilledRectangle(
                    new Rectangle(block.X, block.Y, block.Width - 2, block.Height - 2), ThumbColor);
                break;

            default:
                manager.DrawFilledCircle(centre, radius, ThumbColor);
                break;
        }
    }

    /// <summary>The milled scale above a <see cref="ControlSkin.Hardware"/>
    /// track: evenly spaced ticks, spaced by SIZE rather than by a fixed count
    /// so a short slider does not become a solid bar.</summary>
    private void DrawHardwareScale(DrawManager manager, Rectangle track)
    {
        int ticks = Math.Max(5, Math.Min(33, track.Width / 12));
        int height = Math.Max(2, TrackThickness);
        Color tick = Color.Lerp(TrackColor, Color.White, 0.35f);

        for (int i = 0; i < ticks; i++)
        {
            int x = track.X + (int)(i / (float)(ticks - 1) * (track.Width - 1));
            manager.DrawFilledRectangle(new Rectangle(x, track.Y - height - 2, 1, height), tick);
        }
    }
}
