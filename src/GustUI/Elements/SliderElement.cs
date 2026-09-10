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
/// A slider: value 0..1 along the track. Pressing anywhere jumps the thumb to
/// the pointer and starts a drag; dragging uses pointer capture, so fast drags
/// that leave the element keep working (same input model as
/// <see cref="KnobElement"/>). The thumb is an antialiased disc baked once per
/// diameter.
///
/// HORIZONTAL BY DEFAULT, <see cref="Vertical"/> to stand it up. A vertical
/// slider reads BOTTOM = 0, TOP = 1, which is the way every fader ever built
/// reads and the opposite of screen coordinates — so a panel of them is a
/// mixer or a synth's front face rather than a column of progress bars.
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

    /// <summary>Stand the slider up: the track runs down the middle of the
    /// element, 0 at the bottom and 1 at the top. Everything else — skins,
    /// bipolar mode, the live marker — behaves the same, rotated.</summary>
    public bool Vertical
    {
        get => vertical;
        set
        {
            vertical = value;

            // The pointer that says which way it drags (#224) is part of the
            // orientation, not something a caller should have to remember.
            ElementTrait<CursorTrait>().Set(new TVText(
                value ? Managers.StandardCursors.ResizeVertical : Managers.StandardCursors.ResizeHorizontal));
        }
    }

    private bool vertical;

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
        // Dragged along its one axis (#224).
        AddTrait<CursorTrait>().Set(new TVText(Managers.StandardCursors.ResizeHorizontal));

        ElementTrait<OnMousePress>().Set(new TVEvent<ClickEventArgs>(args =>
        {
            CapturePointer();
            Value = ValueAt(args.MouseState.X, args.MouseState.Y);
        }));

        ElementTrait<OnMouseButtonHeldDown>().Set(new TVEvent<ClickEventArgs>(args =>
        {
            Value = ValueAt(args.MouseState.X, args.MouseState.Y);
        }));

        ElementTrait<OnMouseRelease>().Set(new TVEvent<ClickEventArgs>(args =>
        {
            OnDragCompleted?.Invoke(value);
        }));
    }

    private float ValueAt(float screenX, float screenY)
    {
        Vector2 pos = this.GetActualXnaPosition();
        Vector2 size = this.GetSize().AsXna;
        if (vertical)
        {
            float spanY = size.Y - ThumbDiameter;
            if (spanY <= 0f)
            {
                return 0f;
            }

            // INVERTED: the top of the element is 1. Screen y grows downward
            // and a fader does not.
            return 1f - ((screenY - pos.Y - (ThumbDiameter / 2f)) / spanY); // Value clamps
        }

        float span = size.X - ThumbDiameter;
        if (span <= 0f)
        {
            return 0f;
        }

        return (screenX - pos.X - (ThumbDiameter / 2f)) / span; // Value clamps
    }

    public override void Draw()
    {
        var manager = Resources.StaticResources.DrawManager;
        Vector2 pos = this.GetActualXnaPosition();
        Vector2 size = this.GetSize().AsXna;

        // ALONG is the axis the value travels; ACROSS is the other one. Every
        // number below is written once in those terms and turned back into a
        // rectangle at the point of drawing, which is why the skins did not
        // have to learn about orientation.
        float along = vertical ? size.Y : size.X;
        float across = vertical ? size.X : size.Y;
        if (along <= ThumbDiameter || across < TrackThickness)
        {
            base.Draw();
            return;
        }

        float centreAcross = (vertical ? pos.X : pos.Y) + (across / 2f);
        float trackStart = (vertical ? pos.Y : pos.X) + (ThumbDiameter / 2f);
        float trackLength = along - ThumbDiameter;

        // The thumb's position along the axis, and the point 0 fills from.
        // Vertical counts DOWN from the far end, so a full fader is a full
        // track and an empty one is an empty track.
        float thumbAlong = vertical
            ? trackStart + ((1f - value) * trackLength)
            : trackStart + (value * trackLength);
        float zeroAlong = vertical ? trackStart + trackLength : trackStart;
        float centreValueAlong = trackStart + (vertical ? 1f - 0.5f : 0.5f) * trackLength;

        Rectangle Band(float from, float to)
        {
            float lo = Math.Min(from, to);
            float length = Math.Abs(to - from);
            int thin = (int)(centreAcross - (TrackThickness / 2f));
            return vertical
                ? new Rectangle(thin, (int)lo, TrackThickness, (int)length)
                : new Rectangle((int)lo, thin, (int)length, TrackThickness);
        }

        Rectangle trackRect = Band(trackStart, trackStart + trackLength);
        Vector2 thumbCentre = vertical
            ? new Vector2(centreAcross, thumbAlong)
            : new Vector2(thumbAlong, centreAcross);

        switch (Skin)
        {
            case ControlSkin.Soft:
                // A groove pressed INTO the panel: the shadow sits inside the
                // leading edge, which is what inverts the raised look the
                // thumb has and makes the pair read as track-and-slider.
                manager.DrawFilledCapsule(Inflate(trackRect, 0, 1), Color.Black * 0.16f);
                manager.DrawFilledCapsule(trackRect, TrackColor);
                break;

            case ControlSkin.Hardware:
                // Inset channel with a lip along its far edge, and a milled
                // scale beside it -- the tick row is the thing that reads as
                // a piece of gear rather than a progress bar.
                manager.DrawFilledRectangle(trackRect, Color.Lerp(TrackColor, Color.Black, 0.45f));
                manager.DrawFilledRectangle(FarLip(trackRect), Color.Lerp(TrackColor, Color.White, 0.25f));
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
                manager.DrawFilledCapsule(trackRect, TrackColor);
                break;

            case ControlSkin.Modern:
                manager.DrawFilledCapsule(trackRect, Color.Lerp(TrackColor, Color.Black, 0.25f));
                break;

            case ControlSkin.Pixel:
                manager.DrawFilledRectangle(Inflate(trackRect, 1, 1), Color.Lerp(TrackColor, Color.Black, 0.6f));
                manager.DrawFilledRectangle(trackRect, TrackColor);
                break;

            default:
                manager.DrawFilledRectangle(trackRect, TrackColor);
                break;
        }

        if (Bipolar)
        {
            if (Math.Abs(thumbAlong - centreValueAlong) >= 1f)
            {
                DrawFill(manager, Band(centreValueAlong, thumbAlong));
            }

            // Centre tick, drawn OVER the fill so it stays visible however
            // far the thumb has travelled.
            int tickLong = TrackThickness + 6;
            manager.DrawFilledRectangle(
                vertical
                    ? new Rectangle((int)(centreAcross - (tickLong / 2f)), (int)centreValueAlong - 1, tickLong, 2)
                    : new Rectangle((int)centreValueAlong - 1, (int)(centreAcross - (tickLong / 2f)), 2, tickLong),
                CenterMarkColor);
        }
        else if (Math.Abs(thumbAlong - zeroAlong) >= 1f)
        {
            DrawFill(manager, Band(zeroAlong, thumbAlong));
        }

        int d = Math.Min(ThumbDiameter, (int)across);
        if (d >= 4)
        {
            DrawThumb(manager, thumbCentre, d / 2f);
        }

        if (LiveValue.HasValue)
        {
            // The live marker rides BESIDE the track (never the draggable
            // thumb itself) so base position and live automated position read
            // as two independent marks.
            float live = MathHelper.Clamp(LiveValue.Value, 0f, 1f);
            float liveAlong = trackStart + ((vertical ? 1f - live : live) * trackLength);
            float ld = Math.Max(4f, d * 0.7f);
            float liveAcross = centreAcross - (TrackThickness / 2f) - (ld * 0.5f) - 1f;
            manager.DrawFilledCircle(
                vertical ? new Vector2(liveAcross, liveAlong) : new Vector2(liveAlong, liveAcross),
                ld / 2f, LiveColor);
        }

        base.Draw();
    }

    /// <summary>Grow a band by <paramref name="acrossBy"/> across its own
    /// axis and <paramref name="alongBy"/> along it, whichever way it
    /// runs.</summary>
    private Rectangle Inflate(Rectangle band, int acrossBy, int alongBy)
        => vertical
            ? new Rectangle(band.X - acrossBy, band.Y - alongBy, band.Width + (acrossBy * 2), band.Height + (alongBy * 2))
            : new Rectangle(band.X - alongBy, band.Y - acrossBy, band.Width + (alongBy * 2), band.Height + (acrossBy * 2));

    /// <summary>The one-pixel lip along a band's far edge — the bottom of a
    /// horizontal channel, the right of a vertical one.</summary>
    private Rectangle FarLip(Rectangle band)
        => vertical
            ? new Rectangle(band.Right - 1, band.Y, 1, band.Height)
            : new Rectangle(band.X, band.Bottom - 1, band.Width, 1);

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
                    manager.DrawFilledCapsule(Inflate(rect, i, 0), FillColor * (i == 0 ? 1f : 0.18f / i));
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

            case ControlSkin.Modern:
                manager.DrawFilledCircle(centre, radius, Color.Lerp(ThumbColor, Color.Black, 0.5f));
                manager.DrawRadialShadedCircle(centre, radius * 0.82f,
                    Color.Lerp(ThumbColor, Color.White, 0.15f), ThumbColor);
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

    /// <summary>The milled scale beside a <see cref="ControlSkin.Hardware"/>
    /// track: evenly spaced ticks, spaced by SIZE rather than by a fixed count
    /// so a short slider does not become a solid bar.</summary>
    private void DrawHardwareScale(DrawManager manager, Rectangle track)
    {
        int length = vertical ? track.Height : track.Width;
        int ticks = Math.Max(5, Math.Min(33, length / 12));
        int reach = Math.Max(2, TrackThickness);
        Color tick = Color.Lerp(TrackColor, Color.White, 0.35f);

        for (int i = 0; i < ticks; i++)
        {
            int at = (int)(i / (float)(ticks - 1) * (length - 1));
            manager.DrawFilledRectangle(
                vertical
                    ? new Rectangle(track.X - reach - 2, track.Y + at, reach, 1)
                    : new Rectangle(track.X + at, track.Y - reach - 2, 1, reach),
                tick);
        }
    }
}
