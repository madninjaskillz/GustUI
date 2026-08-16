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

            manager.DrawFilledRectangle(new Rectangle(trackLeft, trackTop, trackWidth, TrackThickness), TrackColor);

            if (Bipolar)
            {
                float centerX = pos.X + ThumbDiameter / 2f + 0.5f * (size.X - ThumbDiameter);
                int fillLeft = (int)Math.Min(centerX, thumbCenterX);
                int fillWidth = (int)Math.Abs(thumbCenterX - centerX);
                if (fillWidth > 0)
                {
                    manager.DrawFilledRectangle(new Rectangle(fillLeft, trackTop, fillWidth, TrackThickness), FillColor);
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
                    manager.DrawFilledRectangle(new Rectangle(trackLeft, trackTop, fillWidth, TrackThickness), FillColor);
                }
            }

            int d = Math.Min(ThumbDiameter, (int)size.Y);
            if (d >= 4)
            {
                manager.DrawFilledCircle(new Vector2(thumbCenterX, centerY), d / 2f, ThumbColor);
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
}
