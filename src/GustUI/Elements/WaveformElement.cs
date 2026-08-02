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

    /// <summary>
    /// Repeats <see cref="Data"/> side-by-side this many times across the
    /// element's width (≥1; 1 = the original single stretch, unchanged
    /// behavior). Added for ezmuze-studio's pattern render-cache
    /// rearchitecture: a Repeat clip spanning N pattern-lengths wants N REAL
    /// copies of one cached pattern image (so every repeat's own transients
    /// show) rather than a longer render stretched across the whole width —
    /// this draws the SAME baked texture (or column data) per tile, so it's
    /// still one bounded set of batchable sprite draws, not N elements.
    /// </summary>
    public int TileCount { get; set; } = 1;

    /// <summary>
    /// Optional decay-tail "ghost" waveform (ezmuze-studio pattern
    /// render-cache rearchitecture, item 5): drawn at reduced opacity
    /// (<see cref="GhostTint"/>) immediately after each INTERNAL tile
    /// boundary (a real tail bleeds into the next repetition) — bounded to
    /// <see cref="GhostWidthPx"/> so it never dominates. Null draws no
    /// ghost. Only internal boundaries (between tile N and N+1) are drawn
    /// here, since they are guaranteed to stay within this element's own
    /// bounds; the ghost AFTER the final tile (the clip's own end, which
    /// needs to draw past this element's right edge into whatever comes
    /// next) is deliberately the host's own responsibility — a separate
    /// element the host can give an elevated Depth, so it always draws
    /// above a neighbor rather than depending on scene draw order.
    /// </summary>
    public WaveformData GhostData { get; set; }

    public Color GhostTint { get; set; } = Color.White * 0.5f;

    public int GhostWidthPx { get; set; } = 32;

    public override void Draw()
    {
        WaveformData data = Data;
        if (data != null)
        {
            Vector2 pos = this.GetActualXnaPosition();
            Vector2 size = this.GetSize().AsXna;
            int totalWidth = (int)size.X;
            int height = (int)size.Y;

            if (totalWidth >= 1 && height >= 2)
            {
                var manager = Resources.StaticResources.DrawManager;
                if (ShowCenterLine)
                {
                    manager.Draw(Pixel(), new Rectangle((int)pos.X, (int)pos.Y + height / 2, totalWidth, 1), CenterLineColor);
                }

                int tiles = Math.Max(1, TileCount);
                int tileWidth = Math.Max(1, totalWidth / tiles);
                int level = data.SelectLevel(tileWidth);
                int ghostLevel = GhostData != null ? GhostData.SelectLevel(Math.Max(1, GhostWidthPx)) : 0;
                int drawnWidth = 0;
                for (int t = 0; t < tiles; t++)
                {
                    // The last tile absorbs integer-division rounding so the
                    // tiles exactly cover totalWidth with no gap/overhang.
                    int thisWidth = t == tiles - 1 ? totalWidth - drawnWidth : tileWidth;
                    var rect = new Rectangle((int)pos.X + drawnWidth, (int)pos.Y, thisWidth, height);
                    drawnWidth += thisWidth;

                    if (RenderMode == WaveformRenderMode.BakedTexture)
                    {
                        manager.Draw(data.GetTexture(level), rect, Tint);
                    }
                    else
                    {
                        DrawColumns(manager, data.LevelData(level), rect, Tint);
                    }

                    if (GhostData != null && t < tiles - 1)
                    {
                        int ghostWidth = Math.Min(GhostWidthPx, totalWidth - drawnWidth);
                        if (ghostWidth > 0)
                        {
                            var ghostRect = new Rectangle((int)pos.X + drawnWidth, (int)pos.Y, ghostWidth, height);
                            if (RenderMode == WaveformRenderMode.BakedTexture)
                            {
                                manager.Draw(GhostData.GetTexture(ghostLevel), ghostRect, GhostTint);
                            }
                            else
                            {
                                DrawColumns(manager, GhostData.LevelData(ghostLevel), ghostRect, GhostTint);
                            }
                        }
                    }
                }
            }
        }

        base.Draw();
    }

    private void DrawColumns(Managers.DrawManager manager, float[] minMax, Rectangle rect, Color tint)
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
            manager.Draw(px, new Rectangle(rect.X + x, rect.Y + y0, 1, Math.Min(y1, height) - y0), tint);
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
