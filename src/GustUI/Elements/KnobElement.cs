using System;
using System.Collections.Generic;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Managers;
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

    /// <summary>
    /// Where the value ACTUALLY is, 0..1, when something is modulating it — a
    /// ghost of the pointer, riding the modulation.
    ///
    /// The pointer always shows the base position, which is the right thing
    /// for dragging and the wrong thing for reading: a knob under an LFO sits
    /// still while the sound moves, and the arc says only how far the movement
    /// can go. This is the mark that says where it got to.
    ///
    /// Distinct from <see cref="LiveValue"/>, which is an automation LANE
    /// moving the base itself. A control can have both, and they mean
    /// different things — hence a second mark rather than a shared one.
    /// </summary>
    public float? ModValue { get; set; }

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

    /// <summary>
    /// How the FACE paints -- flat disc, soft plastic, or studio hardware.
    ///
    /// Only the face and its rim change. The pointer, the modulation arc, the
    /// ghost pointer and the live-automation dot are affordances rather than
    /// decoration, and they draw identically over every skin: a themed panel
    /// must not quietly cost you the ability to see that a knob is automated.
    ///
    /// Ignored when <see cref="FaceTexture"/> is set -- a bitmap cap IS the
    /// face, and shading it a second time would fight the artwork.
    /// </summary>
    public ControlSkin Skin { get; set; } = ControlSkin.Flat;

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
        // The three "shared deliberately app-wide" accents (design-guide.md
        // §1.2/§7) — read live Theme at construction so a knob built after a
        // light/dark switch picks up the current palette. RingColor/FaceColor/
        // PointerColor stay pure per-instance defaults (no ambient theme
        // concept, by design — every call site hand-picks those).
        LiveColor = Resources.StaticResources.Theme.AccentLiveAutomation;
        ModColor = Resources.StaticResources.Theme.AccentModPositive;
        ModNegativeColor = Resources.StaticResources.Theme.AccentModNegative;

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
            var center = new Vector2(pos.X + size.X / 2f, pos.Y + size.Y / 2f);
            var dest = new Rectangle(
                (int)(center.X - diameter / 2f), (int)(center.Y - diameter / 2f), diameter, diameter);
            float outerRadius = diameter / 2f;
            float ringInner = outerRadius - 2.5f;

            if (FaceTexture != null)
            {
                // Custom skin: the author's bitmap IS the face (drawn
                // untinted so its own colours survive).
                manager.Draw(FaceTexture, dest, Color.White);

                // A running lane warms the ring toward LiveColor — a passive,
                // always-visible cue that this control is automated right now,
                // ahead of the eye even catching the moving rim dot.
                if (ShowRing && ShowRingWithSkin)
                {
                    manager.DrawRing(center, ringInner, outerRadius, LiveWarmed(RingColor));
                }
            }
            else
            {
                switch (Skin)
                {
                    case ControlSkin.Soft:
                        DrawSoftFace(manager, center, outerRadius);
                        break;
                    case ControlSkin.Hardware:
                        DrawHardwareFace(manager, center, outerRadius, diameter);
                        break;
                    case ControlSkin.Amp:
                        DrawAmpFace(manager, center, outerRadius, diameter);
                        break;
                    case ControlSkin.Neon:
                        DrawNeonFace(manager, center, outerRadius);
                        break;
                    case ControlSkin.Pixel:
                        DrawPixelFace(manager, center, outerRadius);
                        break;
                    default:
                        DrawFlatFace(manager, center, outerRadius, ringInner);
                        break;
                }
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
                    manager.DrawFilledRectangle(
                        new Rectangle((int)(p.X - thickness / 2f), (int)(p.Y - thickness / 2f), thickness, thickness),
                        arcColor);
                }
            }

            // Ghost pointer: where the modulation has actually put the value.
            //
            // UNDER the real pointer and dimmer than it, because the pointer
            // is still what you grab and the ghost is what you read. Drawn
            // only when it has separated from the pointer far enough to be a
            // second mark rather than a fattening of the first.
            if (ModValue.HasValue && Math.Abs(ModValue.Value - value) > 0.004f)
            {
                float ghostAngle = MathHelper.ToRadians(
                    45f + MathHelper.Clamp(ModValue.Value, 0f, 1f) * SweepDegrees);

                var ghostRect = new Rectangle((int)center.X, (int)center.Y, 3, (int)(diameter * PointerLength));
                manager.DrawRotatedFilledRectangle(ghostRect,
                    (ModDepth ?? 0f) >= 0f ? ModColor : ModNegativeColor,
                    ghostAngle, new Vector2(0.5f, 0f));
            }

            // Pointer: thin rect rotated about its top-center, angle 0 = 6
            // o'clock; value sweeps 45°..315° clockwise (7:30 → 4:30). This
            // ALWAYS tracks Value (the editable base) — never the live
            // automated position — so dragging stays predictable while a
            // lane runs on top of it.
            float angle = MathHelper.ToRadians(45f + value * SweepDegrees);
            int length = (int)(diameter * PointerLength);

            // Skins vary the pointer's WEIGHT but never whether it is there.
            // Amp panels print a fat white line on the cap; pixel art cannot
            // have a 3px line at all, since everything else on that panel is
            // built from blocks several pixels across.
            int pointerWidth = Skin switch
            {
                ControlSkin.Amp => Math.Max(3, (int)(diameter * 0.09f)),
                ControlSkin.Pixel => Math.Max(3, (int)(diameter * 0.14f)),
                _ => 3,
            };

            var pointerRect = new Rectangle((int)center.X, (int)center.Y, pointerWidth, length);
            manager.DrawRotatedFilledRectangle(pointerRect, PointerColor, angle, new Vector2(0.5f, 0f));

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
                manager.DrawFilledCircle(dotCenter, dotDiameterF / 2f, LiveColor);
            }
        }

        base.Draw();
    }

    /// <summary>The ring colour, warmed toward <see cref="LiveColor"/> while a
    /// lane is driving this control.</summary>
    private Color LiveWarmed(Color ringColor)
    {
        return LiveValue.HasValue ? Color.Lerp(ringColor, LiveColor, 0.35f) : ringColor;
    }

    /// <summary>
    /// <see cref="ControlSkin.Flat"/>: a disc and a thin rim, which is what
    /// every knob drew before skins existed.
    ///
    /// The face fills only to <paramref name="ringInner"/> and the band is
    /// drawn over it, so that edge is antialiased in its own right rather than
    /// relying on the ring's inner AA to hide a hard cut.
    /// </summary>
    private void DrawFlatFace(DrawManager manager, Vector2 center, float outerRadius, float ringInner)
    {
        manager.DrawFilledCircle(center, ringInner, FaceColor);

        if (ShowRing)
        {
            manager.DrawRing(center, ringInner, outerRadius, LiveWarmed(RingColor));
        }
    }

    /// <summary>
    /// <see cref="ControlSkin.Soft"/>: the cap is the same material as the
    /// panel, lifted off it by a shadow low-right and a highlight high-left,
    /// with the value read as an accent arc around the rim.
    ///
    /// The arc is what replaces the flat skin's plain ring, and it is drawn in
    /// <see cref="PointerColor"/> rather than <see cref="RingColor"/> because
    /// it is now carrying the value: a soft knob whose arc was the dim rim
    /// colour would be a control with no reading at a glance.
    /// </summary>
    private void DrawSoftFace(DrawManager manager, Vector2 center, float outerRadius)
    {
        float capRadius = outerRadius * 0.72f;

        // The offset has to be a real fraction of the cap, not a pixel or two:
        // a shadow that peeps out on every side is a HALO, and a halo reads as
        // a metal bezel rather than as a raised surface. Offsetting it far
        // enough that the lit side is genuinely clear of it is the whole
        // difference between the two.
        float offset = Math.Max(2f, outerRadius * 0.20f);

        // Shadow then highlight, both under the cap. Light from the top-left,
        // which is the convention every soft-UI reference uses and the reason
        // these controls read as raised rather than as printed discs. The
        // highlight is the stronger of the two because these panels are pale
        // and a white lift has less to work with there than a black one.
        manager.DrawSoftShadowCircle(center + new Vector2(offset, offset), capRadius,
            Color.Black * 0.22f, offset * 1.4f);
        manager.DrawSoftShadowCircle(center - new Vector2(offset, offset), capRadius,
            Color.White * 0.85f, offset * 1.4f);

        // The cap itself, very slightly dished: lighter where the light is, so
        // it is not a flat sticker sitting between two blurs.
        manager.DrawRadialShadedCircle(center, capRadius,
            Lighten(FaceColor, 0.10f), Darken(FaceColor, 0.06f));

        if (ShowRing)
        {
            float arcOuter = outerRadius;
            float arcInner = outerRadius - Math.Max(2f, outerRadius * 0.14f);

            // Full sweep dim, travelled sweep lit -- the same 45..315 degrees
            // clockwise-from-6-o'clock convention the pointer uses, converted
            // to DrawRingArc's 0-at-three-o'clock frame.
            manager.DrawRingArc(center, arcInner, arcOuter, LiveWarmed(RingColor) * 0.45f,
                ArcStartRadians, MathHelper.ToRadians(SweepDegrees));

            if (value > 0.001f)
            {
                manager.DrawRingArc(center, arcInner, arcOuter, LiveWarmed(PointerColor),
                    ArcStartRadians, MathHelper.ToRadians(value * SweepDegrees));
            }
        }
    }

    /// <summary>
    /// <see cref="ControlSkin.Hardware"/>: a machined cap in a metal bezel,
    /// ringed by ticks with the travelled ones lit.
    ///
    /// The lit ticks are the value read here, and they are deliberately
    /// COARSE -- a dozen or so discrete steps, like the LED collar on a piece
    /// of rack gear. The pointer still draws over the top, so the exact value
    /// is not left to a count of dots.
    /// </summary>
    private void DrawHardwareFace(DrawManager manager, Vector2 center, float outerRadius, int diameter)
    {
        float tickRadius = outerRadius * 0.90f;
        float bezelOuter = outerRadius * 0.78f;
        float capRadius = bezelOuter - Math.Max(1.5f, outerRadius * 0.07f);

        // Bezel: a bright ring with a darker disc just inside it, which is the
        // cheapest thing that reads as a turned metal edge rather than a
        // painted circle.
        manager.DrawRing(center, bezelOuter - Math.Max(1f, outerRadius * 0.06f), bezelOuter,
            LiveWarmed(RingColor));
        manager.DrawFilledCircle(center, capRadius, Darken(FaceColor, 0.45f));

        // The cap, shaded from a little light at the centre out to near-black
        // at the edge.
        manager.DrawRadialShadedCircle(center, capRadius * 0.94f,
            Lighten(FaceColor, 0.22f), Darken(FaceColor, 0.35f));

        // Tick collar. Count follows SIZE so a small knob does not turn into a
        // solid ring of overlapping dots.
        int ticks = Math.Max(9, Math.Min(21, diameter / 6));
        float dotRadius = Math.Max(0.9f, outerRadius * 0.055f);
        Color lit = LiveWarmed(PointerColor);
        Color unlit = RingColor * 0.35f;

        for (int i = 0; i < ticks; i++)
        {
            float t = i / (float)(ticks - 1);
            float rad = MathHelper.ToRadians(45f + t * SweepDegrees);
            var dir = new Vector2(-(float)Math.Sin(rad), (float)Math.Cos(rad));
            manager.DrawFilledCircle(center + dir * tickRadius, dotRadius, t <= value + 0.0001f ? lit : unlit);
        }
    }

    /// <summary>
    /// <see cref="ControlSkin.Amp"/>: a knurled black cap with printed tick
    /// marks around it, read like a volume control on an amplifier.
    ///
    /// The ticks are PRINTED, not lit — they are the same colour all the way
    /// round, and the pointer is what you read against them. That is the whole
    /// difference from <see cref="DrawHardwareFace"/>, and it is why this skin
    /// survives on a panel whose accent is barely there.
    ///
    /// The knurl is a run of short radial spokes around the cap's edge. Real
    /// knurling is far finer than this at knob sizes, and drawing it finer just
    /// produces a grey ring: a dozen visible teeth read as "milled edge" where
    /// forty read as "smudge".
    /// </summary>
    private void DrawAmpFace(DrawManager manager, Vector2 center, float outerRadius, int diameter)
    {
        float tickRadius = outerRadius * 0.94f;
        float capRadius = outerRadius * 0.68f;

        // Printed scale. Longer marks at the two ends, because an amp panel
        // calls out its extremes and the eye uses them to find the middle.
        int ticks = Math.Max(9, Math.Min(15, diameter / 8));
        Color printed = RingColor;

        for (int i = 0; i < ticks; i++)
        {
            float t = i / (float)(ticks - 1);
            float rad = MathHelper.ToRadians(45f + t * SweepDegrees);
            var dir = new Vector2(-(float)Math.Sin(rad), (float)Math.Cos(rad));
            bool end = i == 0 || i == ticks - 1;
            float length = outerRadius * (end ? 0.22f : 0.14f);
            manager.DrawThickLine(center + dir * (tickRadius - length), center + dir * tickRadius,
                printed, Math.Max(1, (int)(outerRadius * 0.07f)));
        }

        // Knurled edge, then the cap face over the top of its inner half.
        int teeth = Math.Max(10, Math.Min(24, diameter / 5));
        Color knurl = Darken(FaceColor, 0.25f);
        for (int i = 0; i < teeth; i++)
        {
            float rad = i / (float)teeth * MathHelper.TwoPi;
            var dir = new Vector2((float)Math.Cos(rad), (float)Math.Sin(rad));
            manager.DrawThickLine(center + dir * (capRadius * 0.86f), center + dir * capRadius,
                knurl, Math.Max(1, (int)(outerRadius * 0.06f)));
        }

        manager.DrawFilledCircle(center, capRadius * 0.88f, Darken(FaceColor, 0.55f));
        manager.DrawRadialShadedCircle(center, capRadius * 0.84f,
            Lighten(FaceColor, 0.10f), Darken(FaceColor, 0.30f));

        // The coloured insert. On a black cap this is the only colour on the
        // control, and it is what separates one knob from the next on a panel
        // of otherwise identical black ones.
        if (LiveValue.HasValue || PointerColor != Color.White)
        {
            manager.DrawFilledCircle(center, capRadius * 0.46f, LiveWarmed(PointerColor));
        }
    }

    /// <summary>
    /// <see cref="ControlSkin.Neon"/>: an outlined ring, dark inside, with the
    /// travelled part glowing.
    ///
    /// The glow is three arcs of falling alpha over the same band rather than
    /// anything blurred — there is no blur to be had without a shader, and at
    /// knob sizes a three-step bloom is indistinguishable from one.
    /// </summary>
    private void DrawNeonFace(DrawManager manager, Vector2 center, float outerRadius)
    {
        float ringOuter = outerRadius * 0.94f;
        float ringInner = ringOuter - Math.Max(1.5f, outerRadius * 0.13f);

        manager.DrawFilledCircle(center, ringInner, FaceColor);
        manager.DrawRing(center, ringInner, ringOuter, RingColor * 0.5f);

        if (!ShowRing)
        {
            return;
        }

        float sweep = MathHelper.ToRadians(value * SweepDegrees);
        if (sweep <= 0.0001f)
        {
            return;
        }

        Color glow = LiveWarmed(PointerColor);
        for (int i = 2; i >= 0; i--)
        {
            float spread = i * Math.Max(1f, outerRadius * 0.09f);
            manager.DrawRingArc(center, ringInner - spread, ringOuter + spread,
                glow * (i == 0 ? 1f : 0.16f / i), ArcStartRadians, sweep);
        }
    }

    /// <summary>
    /// <see cref="ControlSkin.Pixel"/>: a square cap with a two-step bevel,
    /// drawn only from hard-edged rectangles.
    ///
    /// The bevel is light on the top and left, dark on the bottom and right,
    /// which is the entire vocabulary of a raised pixel-art button and reads at
    /// sizes where a gradient would not.
    /// </summary>
    private void DrawPixelFace(DrawManager manager, Vector2 center, float outerRadius)
    {
        int step = Math.Max(1, (int)(outerRadius * 0.16f));
        int side = (int)(outerRadius * 1.7f);
        int left = (int)(center.X - side / 2f);
        int top = (int)(center.Y - side / 2f);

        // Outline, then the shadowed base it sits proud of, then the face.
        manager.DrawFilledRectangle(new Rectangle(left - step, top - step, side + step * 2, side + step * 2),
            Darken(FaceColor, 0.75f));
        manager.DrawFilledRectangle(new Rectangle(left, top, side, side), Darken(FaceColor, 0.35f));
        manager.DrawFilledRectangle(new Rectangle(left, top, side - step, side - step), FaceColor);
        manager.DrawFilledRectangle(new Rectangle(left, top, side - step * 2, step), Lighten(FaceColor, 0.35f));
        manager.DrawFilledRectangle(new Rectangle(left, top, step, side - step * 2), Lighten(FaceColor, 0.35f));

        if (!ShowRing)
        {
            return;
        }

        // Value as a row of blocks along the bottom of the CAP, because a
        // pixel panel has no arcs on it anywhere else.
        //
        // Inside the cap rather than under it: the element's box is only as
        // tall as the knob, and a row hung below the face lands in the caption
        // that every control draws beneath itself.
        int cells = 6;
        int inset = step;
        int barWidth = side - step - inset * 2;
        int cell = Math.Max(1, barWidth / cells);
        int barTop = top + side - step * 2 - inset;
        for (int i = 0; i < cells; i++)
        {
            manager.DrawFilledRectangle(
                new Rectangle(left + inset + i * cell, barTop, Math.Max(1, cell - 1), step),
                i / (float)cells < value ? LiveWarmed(PointerColor) : Darken(FaceColor, 0.45f));
        }
    }

    /// <summary>Start of the value sweep in <c>DrawRingArc</c>'s frame (0 at
    /// three o'clock, clockwise): the knob's own 45 degrees from 6
    /// o'clock.</summary>
    private static float ArcStartRadians => MathHelper.ToRadians(45f + 90f);

    private static Color Lighten(Color c, float amount)
    {
        return Color.Lerp(c, Color.White, amount);
    }

    private static Color Darken(Color c, float amount)
    {
        return Color.Lerp(c, Color.Black, amount);
    }
}
