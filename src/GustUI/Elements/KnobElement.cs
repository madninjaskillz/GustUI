using System;
using System.Collections.Generic;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GustUI.Elements;

/// <summary>
/// A rotary knob: value 0..1 over a 270° sweep (7:30 → 4:30). Vertical drag
/// adjusts the value (DragRangePixels for the full sweep); dragging uses
/// pointer capture, so fast drags that leave the element keep working.
/// </summary>
[ElementTraits(typeof(PositionTrait), typeof(SizeTrait), typeof(OnMousePress), typeof(OnMouseButtonHeldDown), typeof(OnMouseRelease))]
public class KnobElement : Element
{
    private static readonly Dictionary<int, Texture2D> DialCache = new Dictionary<int, Texture2D>();
    private static Texture2D pixel;

    public float DragRangePixels { get; set; } = 150f;
    public Color RingColor { get; set; } = new Color(180, 180, 190);
    public Color FaceColor { get; set; } = new Color(38, 38, 47);
    public Color PointerColor { get; set; } = new Color(232, 232, 232);

    /// <summary>
    /// Normalized 0..1 LIVE automated position, distinct from <see cref="Value"/>
    /// (the editable base) — null draws nothing extra. A consumer with a running
    /// automation lane polls the engine every frame and sets this; dragging the
    /// knob always edits <see cref="Value"/> regardless of what this shows (the
    /// documented "drag edits the base" rule), so the two never fight. Rendered
    /// as a small bright dot riding the rim at the live angle plus a warmed ring
    /// tint, kept visually separate from the base pointer so a viewer can tell
    /// "this is moving because automation" from "this is my droppable knob".
    /// </summary>
    public float? LiveValue { get; set; }

    /// <summary>Accent color for the <see cref="LiveValue"/> rim marker / ring tint.</summary>
    public Color LiveColor { get; set; } = new Color(255, 196, 64);

    /// <summary>
    /// CUSTOM SKIN: an author-supplied bitmap drawn in place of the baked
    /// vector face, square, centred, at the knob's diameter (so a round PNG
    /// with alpha reads as a real knob cap). The pointer and the
    /// <see cref="LiveValue"/> rim dot still draw ON TOP, so a skinned knob
    /// keeps every affordance an unskinned one has and stays readable even
    /// with plain artwork behind it.
    ///
    /// Deliberately a STILL image rather than a rotating filmstrip: a
    /// filmstrip would need a frame-count convention, per-frame source-rect
    /// slicing and art authored for it, and the vector pointer already
    /// communicates position perfectly well over a static cap. Null = the
    /// baked <see cref="FaceColor"/> disc, i.e. exactly today's look.
    /// </summary>
    public Texture2D FaceTexture { get; set; }

    /// <summary>Draws the rim ring. Skins usually bring their own bezel, so a
    /// custom <see cref="FaceTexture"/> defaults this off via
    /// <see cref="ShowRingWithSkin"/> rather than double-drawing one.</summary>
    public bool ShowRing { get; set; } = true;

    /// <summary>Whether the rim ring still draws when a
    /// <see cref="FaceTexture"/> is set.</summary>
    public bool ShowRingWithSkin { get; set; }

    /// <summary>Pointer length as a fraction of the diameter (a skin with a
    /// small dished cap wants a shorter pointer than the default disc).</summary>
    public float PointerLength { get; set; } = 0.38f;

    public Action<float> OnValueChanged;

    /// <summary>Raised when a drag gesture ends (mouse release), with the final
    /// value — the hook for "commit" semantics like closing an undo-coalescing
    /// stream, which per-tick <see cref="OnValueChanged"/> cannot signal.</summary>
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

    private float dragStartY;
    private float dragStartValue;

    public KnobElement()
    {
        ElementTrait<OnMousePress>().Set(new TVEvent<ClickEventArgs>(args =>
        {
            dragStartY = args.MouseState.Y;
            dragStartValue = value;
            CapturePointer();
        }));

        ElementTrait<OnMouseButtonHeldDown>().Set(new TVEvent<ClickEventArgs>(args =>
        {
            Value = dragStartValue + (dragStartY - args.MouseState.Y) / DragRangePixels;
        }));

        ElementTrait<OnMouseRelease>().Set(new TVEvent<ClickEventArgs>(args =>
        {
            OnDragCompleted?.Invoke(value);
        }));
    }

    public override void Draw()
    {
        var manager = Resources.StaticResources.DrawManager;
        Vector2 pos = this.GetActualXnaPosition();
        Vector2 size = this.GetSize().AsXna;

        int diameter = (int)Math.Min(size.X, size.Y);
        if (diameter >= 8)
        {
            Texture2D dial = GetDialTexture(diameter);
            var center = new Vector2(pos.X + size.X / 2f, pos.Y + size.Y / 2f);
            var dest = new Rectangle(
                (int)(center.X - diameter / 2f), (int)(center.Y - diameter / 2f), diameter, diameter);

            if (FaceTexture != null)
            {
                // Custom skin: the author's bitmap IS the face (drawn
                // untinted so its own colours survive).
                manager.Draw(FaceTexture, dest, Color.White);
            }
            else
            {
                manager.Draw(dial, dest, FaceColor);
            }

            // A running lane warms the ring toward LiveColor — a passive,
            // always-visible cue that this control is automated right now,
            // ahead of the eye even catching the moving rim dot.
            if (ShowRing && (FaceTexture == null || ShowRingWithSkin))
            {
                Color ringColor = LiveValue.HasValue ? Color.Lerp(RingColor, LiveColor, 0.35f) : RingColor;
                manager.Draw(GetRingTexture(diameter), dest, ringColor);
            }

            // Pointer: thin rect rotated about its top-center, angle 0 = 6
            // o'clock; value sweeps 45°..315° clockwise (7:30 → 4:30). This
            // ALWAYS tracks Value (the editable base) — never the live
            // automated position — so dragging stays predictable while a
            // lane runs on top of it.
            float angle = MathHelper.ToRadians(45f + value * 270f);
            int length = (int)(diameter * PointerLength);
            var pointerRect = new Rectangle((int)center.X, (int)center.Y, 3, length);
            manager.Draw(Pixel(), pointerRect, null, PointerColor, angle, new Vector2(0.5f, 0f), SpriteEffects.None, 0);

            if (LiveValue.HasValue)
            {
                // Live marker: a small bright dot riding the OUTER rim at the
                // live angle (same 45°..315° mapping as the pointer) — a
                // second, independent mark so base position and live
                // automated position read as two distinct things on the same
                // face rather than one pointer fighting itself.
                float liveAngle = MathHelper.ToRadians(45f + MathHelper.Clamp(LiveValue.Value, 0f, 1f) * 270f);
                var dir = new Vector2(-(float)Math.Sin(liveAngle), (float)Math.Cos(liveAngle));
                float dotDiameterF = Math.Max(4f, diameter * 0.16f);
                float rim = diameter / 2f - dotDiameterF / 2f - 1f;
                Vector2 dotCenter = center + dir * rim;
                int dotDiameter = (int)dotDiameterF;
                var dotRect = new Rectangle(
                    (int)(dotCenter.X - dotDiameter / 2f), (int)(dotCenter.Y - dotDiameter / 2f), dotDiameter, dotDiameter);
                manager.Draw(GetLiveDotTexture(dotDiameter), dotRect, LiveColor);
            }
        }

        base.Draw();
    }

    private Texture2D Pixel()
    {
        if (pixel == null)
        {
            pixel = new Texture2D(Resources.StaticResources.GraphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        return pixel;
    }

    private static readonly Dictionary<int, Texture2D> RingCache = new Dictionary<int, Texture2D>();

    /// <summary>Antialiased 2px ring annulus, baked once per diameter, tinted by RingColor.</summary>
    private static Texture2D GetRingTexture(int diameter)
    {
        if (RingCache.TryGetValue(diameter, out Texture2D cached))
        {
            return cached;
        }

        var data = new Color[diameter * diameter];
        float r = diameter / 2f;
        float ringInner = r - 2.5f;

        for (int y = 0; y < diameter; y++)
        {
            for (int x = 0; x < diameter; x++)
            {
                float dx = x - r + 0.5f;
                float dy = y - r + 0.5f;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                float outer = MathHelper.Clamp(r - dist, 0f, 1f);
                float inner = MathHelper.Clamp(dist - ringInner + 1f, 0f, 1f);
                data[y * diameter + x] = Color.White * (outer * inner);
            }
        }

        var texture = new Texture2D(Resources.StaticResources.GraphicsDevice, diameter, diameter);
        texture.SetData(data);
        RingCache[diameter] = texture;
        return texture;
    }

    private static readonly Dictionary<int, Texture2D> LiveDotCache = new Dictionary<int, Texture2D>();

    /// <summary>Antialiased filled disc — the <see cref="LiveValue"/> rim marker — baked once per diameter.</summary>
    private static Texture2D GetLiveDotTexture(int diameter)
    {
        if (LiveDotCache.TryGetValue(diameter, out Texture2D cached))
        {
            return cached;
        }

        var data = new Color[diameter * diameter];
        float r = diameter / 2f;
        for (int y = 0; y < diameter; y++)
        {
            for (int x = 0; x < diameter; x++)
            {
                float dx = x - r + 0.5f;
                float dy = y - r + 0.5f;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                float alpha = MathHelper.Clamp(r - dist, 0f, 1f);
                data[y * diameter + x] = alpha <= 0f ? Color.Transparent : Color.White * alpha;
            }
        }

        var texture = new Texture2D(Resources.StaticResources.GraphicsDevice, diameter, diameter);
        texture.SetData(data);
        LiveDotCache[diameter] = texture;
        return texture;
    }

    /// <summary>Antialiased filled disc (face only; ring drawn separately), baked once per diameter.</summary>
    private static Texture2D GetDialTexture(int diameter)
    {
        if (DialCache.TryGetValue(diameter, out Texture2D cached))
        {
            return cached;
        }

        var data = new Color[diameter * diameter];
        float r = diameter / 2f;
        float ringInner = r - 2.5f;

        for (int y = 0; y < diameter; y++)
        {
            for (int x = 0; x < diameter; x++)
            {
                float dx = x - r + 0.5f;
                float dy = y - r + 0.5f;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                float alpha = MathHelper.Clamp(r - dist, 0f, 1f);
                if (alpha <= 0f)
                {
                    data[y * diameter + x] = Color.Transparent;
                    continue;
                }

                // Face is opaque white (tinted by FaceColor at draw time); the
                // ring band is transparent here and drawn from RingTexture so
                // RingColor tints it independently.
                bool ring = dist >= ringInner;
                data[y * diameter + x] = ring ? Color.Transparent : Color.White * alpha;
            }
        }

        var texture = new Texture2D(Resources.StaticResources.GraphicsDevice, diameter, diameter);
        texture.SetData(data);
        DialCache[diameter] = texture;
        return texture;
    }
}
