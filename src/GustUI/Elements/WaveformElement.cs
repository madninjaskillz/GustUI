using System;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GustUI.Elements;

/// <summary>
/// Renders <see cref="WaveformData"/> (min/max columns) inside its bounds —
/// immediate-mode in <see cref="Draw"/>, the KnobElement pattern for dense
/// custom surfaces. Designed for pooled use: <see cref="Data"/>, <see cref="Tint"/>
/// and size are cheap to rebind every frame, and a null <see cref="Data"/>
/// draws nothing (the host shows its flat background until data arrives).
///
/// Two render modes (both zoom-responsive via the data's mip chain):
///  - <see cref="WaveformRenderMode.BakedTexture"/> (default): one stretched
///    sprite per element from a texture baked once per (data, mip level) and
///    tinted at draw time — constant per-frame cost regardless of width.
///  - <see cref="WaveformRenderMode.Columns"/>: one 1px rect per horizontal
///    pixel, no textures — kept for comparison/measurement and for data that
///    changes every frame (live meters) where baking would thrash.
/// Measured on ezmuze's 597-block timeline (interpreter WASM): columns mode
/// more than doubled worst-case frame time vs baked (≈4 800 extra sprite
/// draws at max zoom-out), so BakedTexture is the default. For hundreds of
/// solid tiles the even faster shape is skipping the element entirely and
/// using the solid-background bake as a tinted TVFillImage on the tile rect
/// (see <see cref="WaveformData.FromMinMax"/>), which draws one batchable
/// sprite per tile.
/// </summary>
[ElementTraits(typeof(PositionTrait), typeof(SizeTrait))]
public class WaveformElement : Element
{
    private static Texture2D pixel;

    /// <summary>Waveform to draw; null draws nothing.</summary>
    public WaveformData Data { get; set; }

    /// <summary>Draw color (premultiplied alpha, e.g. <c>color * 0.6f</c>).</summary>
    public Color Tint { get; set; } = Color.White;

    /// <summary>Optional 1px horizontal zero-line under the waveform.</summary>
    public bool ShowCenterLine { get; set; }

    public Color CenterLineColor { get; set; } = Color.White * 0.3f;

    public WaveformRenderMode RenderMode { get; set; } = WaveformRenderMode.BakedTexture;

    public override void Draw()
    {
        WaveformData data = Data;
        if (data != null)
        {
            Vector2 pos = this.GetActualXnaPosition();
            Vector2 size = this.GetSize().AsXna;
            var rect = new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);

            if (rect.Width >= 1 && rect.Height >= 2)
            {
                var manager = Resources.StaticResources.DrawManager;
                if (ShowCenterLine)
                {
                    manager.Draw(Pixel(), new Rectangle(rect.X, rect.Y + rect.Height / 2, rect.Width, 1), CenterLineColor);
                }

                int level = data.SelectLevel(rect.Width);
                if (RenderMode == WaveformRenderMode.BakedTexture)
                {
                    manager.Draw(data.GetTexture(level), rect, Tint);
                }
                else
                {
                    DrawColumns(manager, data.LevelData(level), rect);
                }
            }
        }

        base.Draw();
    }

    private void DrawColumns(Managers.DrawManager manager, float[] minMax, Rectangle rect)
    {
        Texture2D px = Pixel();
        int columns = minMax.Length / 2;
        int height = rect.Height;

        for (int x = 0; x < rect.Width; x++)
        {
            int c = (int)((long)x * columns / rect.Width);
            float top = (1f - MathHelper.Clamp(minMax[c * 2 + 1], -1f, 1f)) * 0.5f * height;
            float bottom = (1f - MathHelper.Clamp(minMax[c * 2], -1f, 1f)) * 0.5f * height;

            int y0 = (int)top;
            int y1 = Math.Max(y0 + 1, (int)Math.Ceiling(bottom)); // ≥1px so silence stays visible
            manager.Draw(px, new Rectangle(rect.X + x, rect.Y + y0, 1, Math.Min(y1, height) - y0), Tint);
        }
    }

    private static Texture2D Pixel()
    {
        if (pixel == null)
        {
            pixel = new Texture2D(Resources.StaticResources.GraphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        return pixel;
    }
}

public enum WaveformRenderMode
{
    /// <summary>Texture baked once per (data, mip level), stretched + tinted per draw.</summary>
    BakedTexture,

    /// <summary>Per-pixel column rects every frame (no textures).</summary>
    Columns,
}
