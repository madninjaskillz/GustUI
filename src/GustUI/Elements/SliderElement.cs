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
/// A horizontal slider: value 0..1 along the track. Pressing anywhere jumps
/// the thumb to the pointer and starts a drag; dragging uses pointer capture,
/// so fast drags that leave the element keep working (same input model as
/// <see cref="KnobElement"/>). The thumb is an antialiased disc baked once per
/// diameter.
/// </summary>
[ElementTraits(typeof(PositionTrait), typeof(SizeTrait), typeof(OnMousePress), typeof(OnMouseButtonHeldDown), typeof(OnMouseRelease))]
public class SliderElement : Element
{
    private static readonly Dictionary<int, Texture2D> ThumbCache = new Dictionary<int, Texture2D>();

    public Color TrackColor { get; set; } = new Color(52, 52, 63);
    public Color FillColor { get; set; } = new Color(110, 145, 235);
    public Color ThumbColor { get; set; } = new Color(232, 232, 236);
    public int TrackThickness { get; set; } = 4;
    public int ThumbDiameter { get; set; } = 14;

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
            int fillWidth = (int)(thumbCenterX - trackLeft);
            if (fillWidth > 0)
            {
                manager.DrawFilledRectangle(new Rectangle(trackLeft, trackTop, fillWidth, TrackThickness), FillColor);
            }

            int d = Math.Min(ThumbDiameter, (int)size.Y);
            if (d >= 4)
            {
                var dest = new Rectangle((int)(thumbCenterX - d / 2f), (int)(centerY - d / 2f), d, d);
                manager.Draw(GetThumbTexture(d), dest, ThumbColor);
            }
        }

        base.Draw();
    }

    /// <summary>Antialiased filled disc, baked once per diameter, tinted by ThumbColor.</summary>
    private static Texture2D GetThumbTexture(int diameter)
    {
        if (ThumbCache.TryGetValue(diameter, out Texture2D cached))
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
        ThumbCache[diameter] = texture;
        return texture;
    }
}
