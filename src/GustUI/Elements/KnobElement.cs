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
    /// Signed −1..1 MODULATION DEPTH assigned to this knob, or null when
    /// nothing modulates it. Drawn as a separate ARC layer swept from the base
    /// pointer's angle by the depth, OUTSIDE the value ring — the span the
    /// value will actually travel over once its modulation source runs.
    ///
    /// Deliberately a second, distinct visual layer rather than a reuse of the
    /// <see cref="LiveValue"/> ring tint: those two say completely different
    /// things ("an automation lane is driving this RIGHT NOW" versus "a
    /// modulation source is assigned to this, and here is how far it can push
    /// it"), and a knob can be in both states at once. Collapsing them into
    /// one ring would make the two indistinguishable exactly when both matter.
    ///
    /// The arc is drawn from the pointer, so it also communicates HEADROOM:
    /// a knob near the top of its range with a large positive depth visibly
    /// runs out of arc, which is the modulation clipping you would otherwise
    /// only hear.
    /// </summary>
    public float? ModDepth { get; set; }

    /// <summary>Arc colour for a POSITIVE <see cref="ModDepth"/>.</summary>
    public Color ModColor { get; set; } = new Color(96, 224, 160);

    /// <summary>Arc colour for a NEGATIVE <see cref="ModDepth"/> — polarity is
    /// worth reading at a glance and arc direction alone is easy to misread on
    /// a small knob.</summary>
    public Color ModNegativeColor { get; set; } = new Color(255, 120, 168);

    /// <summary>
    /// Raised while the user drags the modulation arc itself, with the new
    /// signed depth. Only reachable when <see cref="ModDepth"/> is non-null:
    /// a knob with no modulation assigned behaves exactly as it always did,
    /// so this costs existing callers nothing.
    /// </summary>
    public Action<float> OnModDepthChanged;

    /// <summary>Raised when a modulation-arc drag ends (the commit hook, the
    /// arc's counterpart to <see cref="OnDragCompleted"/>).</summary>
    public Action<float> OnModDepthDragCompleted;

    /// <summary>Fraction of the radius beyond which a press grabs the
    /// MODULATION ARC instead of the knob value. 0.78 keeps the whole dished
    /// centre — where people actually aim for a value drag — on the value
    /// gesture, and gives the arc the outer rim it is drawn on.</summary>
    public float ModRingInnerFraction { get; set; } = 0.78f;

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
    private bool draggingModArc;
    private float dragStartAngle;
    private float dragStartDepth;

    /// <summary>The knob's sweep in degrees (7:30 → 4:30) — the constant the
    /// pointer, the live marker and the modulation arc all share.</summary>
    private const float SweepDegrees = 270f;

    public KnobElement()
    {
        ElementTrait<OnMousePress>().Set(new TVEvent<ClickEventArgs>(args =>
        {
            // A press out on the rim of a MODULATED knob grabs the arc; a
            // press anywhere else — and every press on an unmodulated knob —
            // is the ordinary value drag, unchanged.
            draggingModArc = ModDepth.HasValue && PressIsOnModRing(args.MouseState.X, args.MouseState.Y);
            if (draggingModArc)
            {
                dragStartAngle = AngleAt(args.MouseState.X, args.MouseState.Y);
                dragStartDepth = ModDepth.Value;
            }
            else
            {
                dragStartY = args.MouseState.Y;
                dragStartValue = value;
            }

            CapturePointer();
        }));

        ElementTrait<OnMouseButtonHeldDown>().Set(new TVEvent<ClickEventArgs>(args =>
        {
            if (draggingModArc)
            {
                // RADIAL drag: the pointer's angle around the knob centre maps
                // onto depth over the same 270° the value uses, so "drag the
                // arc further round" reads as "more depth" with no hidden
                // pixels-per-unit constant. Wrapping is folded to the shorter
                // way round so a drag across the 12 o'clock seam does not
                // snap depth to the far end.
                float delta = WrapDegrees(AngleAt(args.MouseState.X, args.MouseState.Y) - dragStartAngle);
                float depth = MathHelper.Clamp(dragStartDepth + delta / SweepDegrees, -1f, 1f);
                if (depth != ModDepth)
                {
                    ModDepth = depth;
                    OnModDepthChanged?.Invoke(depth);
                }

                return;
            }

            Value = dragStartValue + (dragStartY - args.MouseState.Y) / DragRangePixels;
        }));

        ElementTrait<OnMouseRelease>().Set(new TVEvent<ClickEventArgs>(args =>
        {
            if (draggingModArc)
            {
                draggingModArc = false;
                OnModDepthDragCompleted?.Invoke(ModDepth ?? 0f);
                return;
            }

            OnDragCompleted?.Invoke(value);
        }));
    }

    private Vector2 Centre()
    {
        Vector2 pos = this.GetActualXnaPosition();
        Vector2 size = this.GetSize().AsXna;
        return new Vector2(pos.X + size.X / 2f, pos.Y + size.Y / 2f);
    }

    private float Radius()
    {
        Vector2 size = this.GetSize().AsXna;
        return Math.Min(size.X, size.Y) / 2f;
    }

    private bool PressIsOnModRing(float x, float y)
    {
        Vector2 centre = Centre();
        float radius = Radius();
        if (radius <= 0f)
        {
            return false;
        }

        float dx = x - centre.X;
        float dy = y - centre.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy) >= radius * ModRingInnerFraction;
    }

    /// <summary>Pointer angle in the knob's own convention: degrees clockwise
    /// from 6 o'clock, matching the 45°..315° the pointer sweeps.</summary>
    private float AngleAt(float x, float y)
    {
        Vector2 centre = Centre();
        return MathHelper.ToDegrees((float)Math.Atan2(centre.X - x, y - centre.Y));
    }

    private static float WrapDegrees(float degrees)
    {
        while (degrees > 180f)
        {
            degrees -= 360f;
        }

        while (degrees < -180f)
        {
            degrees += 360f;
        }

        return degrees;
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

            // Modulation arc, UNDER the pointer so the pointer stays the
            // primary read. Drawn as a run of small quads along the rim rather
            // than as a baked annulus texture: the arc's start angle and sweep
            // both change continuously (with the knob's value and with the
            // depth), so a texture cache keyed on them would thrash, and a
            // couple of dozen quads is cheaper than one texture upload.
            if (ModDepth.HasValue && Math.Abs(ModDepth.Value) > 0.001f)
            {
                float depth = MathHelper.Clamp(ModDepth.Value, -1f, 1f);
                float fromValue = value;
                float toValue = MathHelper.Clamp(value + depth, 0f, 1f);
                float startDeg = 45f + fromValue * SweepDegrees;
                float endDeg = 45f + toValue * SweepDegrees;
                Color arcColor = depth >= 0f ? ModColor : ModNegativeColor;

                float arcRadius = diameter / 2f - Math.Max(1.5f, diameter * 0.045f);
                int steps = Math.Max(3, (int)(Math.Abs(endDeg - startDeg) / 4f) + 2);
                int thickness = Math.Max(2, (int)(diameter * 0.07f));
                for (int s = 0; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    float deg = startDeg + (endDeg - startDeg) * t;
                    float rad = MathHelper.ToRadians(deg);
                    var dir = new Vector2(-(float)Math.Sin(rad), (float)Math.Cos(rad));
                    Vector2 p = center + dir * arcRadius;
                    manager.Draw(
                        Pixel(),
                        new Rectangle((int)(p.X - thickness / 2f), (int)(p.Y - thickness / 2f), thickness, thickness),
                        arcColor);
                }
            }

            // Pointer: thin rect rotated about its top-center, angle 0 = 6
            // o'clock; value sweeps 45°..315° clockwise (7:30 → 4:30). This
            // ALWAYS tracks Value (the editable base) — never the live
            // automated position — so dragging stays predictable while a
            // lane runs on top of it.
            float angle = MathHelper.ToRadians(45f + value * SweepDegrees);
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
