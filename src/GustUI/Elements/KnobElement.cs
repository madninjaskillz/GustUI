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

    public Action<float> OnValueChanged;

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
    }

    public override void Draw()
    {
        var manager = Resources.StaticResources.DrawManager;
        Vector2 pos = this.GetActualPosition().AsXna;
        Vector2 size = this.GetSize().AsXna;

        int diameter = (int)Math.Min(size.X, size.Y);
        if (diameter >= 8)
        {
            Texture2D dial = GetDialTexture(diameter);
            var center = new Vector2(pos.X + size.X / 2f, pos.Y + size.Y / 2f);
            var dest = new Rectangle(
                (int)(center.X - diameter / 2f), (int)(center.Y - diameter / 2f), diameter, diameter);

            manager.Draw(dial, dest, FaceColor);
            manager.Draw(GetRingTexture(diameter), dest, RingColor);

            // Pointer: thin rect rotated about its top-center, angle 0 = 6
            // o'clock; value sweeps 45°..315° clockwise (7:30 → 4:30).
            float angle = MathHelper.ToRadians(45f + value * 270f);
            int length = (int)(diameter * 0.38f);
            var pointerRect = new Rectangle((int)center.X, (int)center.Y, 3, length);
            manager.Draw(Pixel(), pointerRect, null, PointerColor, angle, new Vector2(0.5f, 0f), SpriteEffects.None, 0);
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
