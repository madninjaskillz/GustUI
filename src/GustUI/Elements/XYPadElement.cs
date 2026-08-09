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
/// A 2D draggable pad: ValueX/ValueY each 0..1, position mapped directly
/// from the pointer's location within the element's own bounds (unlike
/// <see cref="KnobElement"/>'s relative vertical-drag law, a pad is
/// naturally absolute — "put the handle where I clicked" is the whole
/// point). Self-contained pointer capture/drag state, same idiom
/// <see cref="KnobElement"/>/<see cref="SliderElement"/> already use, so a
/// pad is a first-class reusable primitive rather than a caller hand-rolling
/// mouse traits over a plain rectangle.
/// </summary>
[ElementTraits(typeof(PositionTrait), typeof(SizeTrait), typeof(OnMousePress), typeof(OnMouseButtonHeldDown), typeof(OnMouseRelease))]
public class XYPadElement : Element
{
    private static readonly Dictionary<int, Texture2D> HandleCache = new Dictionary<int, Texture2D>();

    public Color PadColor { get; set; } = new Color(38, 38, 47);

    public Color BorderColor { get; set; } = new Color(90, 90, 104);

    public Color GridColor { get; set; } = new Color(60, 60, 72);

    public Color HandleColor { get; set; } = new Color(232, 232, 232);

    /// <summary>Live-automated position for X/Y, same "second independent
    /// marker, never fought over by drag" contract as <see cref="KnobElement.LiveValue"/>.
    /// Null on either axis draws no live marker at all (an XY pad's two axes
    /// are more often driven by two INDEPENDENT sources than a knob's one).</summary>
    public Vector2? LiveValue { get; set; }

    public Color LiveColor { get; set; } = new Color(255, 196, 64);

    /// <summary>Handle diameter as a fraction of the pad's shorter side.</summary>
    public float HandleSizeFraction { get; set; } = 0.14f;

    public Action<float, float> OnValueChanged;

    /// <summary>Raised when a drag gesture ends, with the final (x, y) — the
    /// undo-coalescing commit hook, <see cref="KnobElement.OnDragCompleted"/>'s
    /// two-axis sibling.</summary>
    public Action<float, float> OnDragCompleted;

    private float valueX;
    private float valueY;

    public float ValueX
    {
        get => valueX;
        set => SetValue(MathHelper.Clamp(value, 0f, 1f), valueY);
    }

    public float ValueY
    {
        get => valueY;
        set => SetValue(valueX, MathHelper.Clamp(value, 0f, 1f));
    }

    /// <summary>Sets both axes at once without raising <see cref="OnValueChanged"/>
    /// twice — the drag handler's own path, and the setter a host uses to
    /// sync the pad from a model value without echoing a spurious edit.</summary>
    public void SetValueSilent(float x, float y)
    {
        valueX = MathHelper.Clamp(x, 0f, 1f);
        valueY = MathHelper.Clamp(y, 0f, 1f);
    }

    private void SetValue(float x, float y)
    {
        x = MathHelper.Clamp(x, 0f, 1f);
        y = MathHelper.Clamp(y, 0f, 1f);
        if (x != valueX || y != valueY)
        {
            valueX = x;
            valueY = y;
            OnValueChanged?.Invoke(x, y);
        }
    }

    public XYPadElement()
    {
        ElementTrait<OnMousePress>().Set(new TVEvent<ClickEventArgs>(args =>
        {
            DragTo(args.MouseState.X, args.MouseState.Y);
            CapturePointer();
        }));

        ElementTrait<OnMouseButtonHeldDown>().Set(new TVEvent<ClickEventArgs>(args =>
        {
            DragTo(args.MouseState.X, args.MouseState.Y);
        }));

        ElementTrait<OnMouseRelease>().Set(new TVEvent<ClickEventArgs>(_ =>
        {
            OnDragCompleted?.Invoke(valueX, valueY);
        }));
    }

    private void DragTo(float screenX, float screenY)
    {
        Vector2 pos = this.GetActualXnaPosition();
        Vector2 size = this.GetSize().AsXna;
        if (size.X <= 0f || size.Y <= 0f)
        {
            return;
        }

        // Y inverted: pad "up" (screen-top) reads as the larger value, the
        // universal XY-pad/fader convention (up = more).
        float x = (screenX - pos.X) / size.X;
        float y = 1f - (screenY - pos.Y) / size.Y;
        SetValue(x, y);
    }

    public override void Draw()
    {
        var manager = Resources.StaticResources.DrawManager;
        Vector2 pos = this.GetActualXnaPosition();
        Vector2 size = this.GetSize().AsXna;

        var bounds = new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);
        manager.DrawRoundedBorder(bounds, BorderColor, PadColor, Math.Max(2, (int)(Math.Min(size.X, size.Y) * 0.06f)), 1);

        // Centre crosshair, a light static reference so a handle sitting at
        // (0.5, 0.5) doesn't read as "nothing set yet".
        var midV = new Vector2(pos.X + size.X / 2f, pos.Y);
        var midVEnd = new Vector2(pos.X + size.X / 2f, pos.Y + size.Y);
        manager.DrawLine(midV, midVEnd, GridColor);
        var midH = new Vector2(pos.X, pos.Y + size.Y / 2f);
        var midHEnd = new Vector2(pos.X + size.X, pos.Y + size.Y / 2f);
        manager.DrawLine(midH, midHEnd, GridColor);

        int handleDiameter = Math.Max(6, (int)(Math.Min(size.X, size.Y) * HandleSizeFraction));
        Vector2 handleCentre = new Vector2(pos.X + valueX * size.X, pos.Y + (1f - valueY) * size.Y);
        DrawHandle(manager, handleCentre, handleDiameter, HandleColor);

        if (LiveValue.HasValue)
        {
            Vector2 liveCentre = new Vector2(
                pos.X + MathHelper.Clamp(LiveValue.Value.X, 0f, 1f) * size.X,
                pos.Y + (1f - MathHelper.Clamp(LiveValue.Value.Y, 0f, 1f)) * size.Y);
            DrawHandle(manager, liveCentre, Math.Max(4, handleDiameter / 2), LiveColor);
        }

        base.Draw();
    }

    private void DrawHandle(DrawManager manager, Vector2 centre, int diameter, Color color)
    {
        Texture2D handle = GetHandleTexture(diameter);
        var dest = new Rectangle((int)(centre.X - diameter / 2f), (int)(centre.Y - diameter / 2f), diameter, diameter);
        manager.Draw(handle, dest, color);
    }

    /// <summary>Antialiased filled disc, baked once per diameter — the same
    /// SDF-style bake <see cref="KnobElement.GetLiveDotTexture"/> uses.</summary>
    private static Texture2D GetHandleTexture(int diameter)
    {
        if (HandleCache.TryGetValue(diameter, out Texture2D cached))
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
        HandleCache[diameter] = texture;
        return texture;
    }
}
