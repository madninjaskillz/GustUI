using System;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Rendering;
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
/// Renders as triangle geometry (zoom-responsive via the data's mip chain),
/// never a baked Texture2D — so there is no 4096-texel size cap and no
/// rasterize-then-stretch blur at any zoom. <see cref="WaveformRenderMode.GeometryBaked"/>
/// (default) triangulates once per size and caches; <see cref="WaveformRenderMode.Geometry"/>
/// re-triangulates every frame; <see cref="WaveformRenderMode.Columns"/> draws
/// per-pixel rects. The former BakedTexture mode was removed (2026-08-30): a
/// long clip's low-mip texture could exceed the GL HiDef profile's 4096 limit
/// and crash the app, and geometry is crisper at comparable cost.
/// </summary>
[ElementTraits(typeof(PositionTrait), typeof(SizeTrait))]
public class WaveformElement : Element
{
    /// <summary>Waveform to draw; null draws nothing.</summary>
    public WaveformData Data { get; set; }

    /// <summary>Draw color (premultiplied alpha, e.g. <c>color * 0.6f</c>).</summary>
    public Color Tint { get; set; } = Color.White;

    /// <summary>Optional 1px horizontal zero-line under the waveform.</summary>
    public bool ShowCenterLine { get; set; }

    public Color CenterLineColor { get; set; } = Color.White * 0.3f;

    public WaveformRenderMode RenderMode { get; set; } = WaveformRenderMode.GeometryBaked;

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
    /// How much of <see cref="Data"/> the LAST tile covers, 0..1 (1 = a whole
    /// tile like all the others, the unchanged default). Everything before it
    /// is always a full tile.
    ///
    /// This is what makes a tile count of "3.25" expressible: the host sets
    /// <see cref="TileCount"/> 4 and this to 0.25, and the final tile draws
    /// the leading quarter of the data in a quarter-width slot instead of the
    /// whole thing squeezed into it. Without it a clip whose length isn't an
    /// exact multiple of its tile's shows the entire source crammed into a
    /// fraction of the width — a picture of audio that plays several times
    /// too fast, rather than of the beginning of the audio (ezmuze-studio bug
    /// board #38: trimming a clip made its waveform unrecognisable). The
    /// caller supplies no source OFFSET because its counterpart doesn't need
    /// one — a partial tile is always a leading window.
    /// </summary>
    public float LastTileFraction { get; set; } = 1f;

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
        using (Managers.Telemetry.Scope("Draw.Waveform"))
        {
            DrawWaveform();
        }
    }

    private void DrawWaveform()
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
                    manager.DrawFilledRectangle(new Rectangle((int)pos.X, (int)pos.Y + height / 2, totalWidth, 1), CenterLineColor);
                }

                int tiles = Math.Max(1, TileCount);
                float lastFraction = MathHelper.Clamp(LastTileFraction, 0f, 1f);

                // Tile widths are proportional to the SPAN, not the count: a
                // 3.25-tile block gives three full tiles and a quarter-width
                // one, so a full tile is the same width whether or not the
                // block ends mid-tile. Dividing by the count instead would
                // stretch every full tile to cover the shortfall.
                float span = Math.Max(0.0001f, tiles - 1 + lastFraction);
                int tileWidth = Math.Max(1, (int)(totalWidth / span));

                // One level serves every tile, partial included: the partial
                // draws fraction × columns across fraction × tileWidth, so
                // its columns-per-pixel is the full tile's and the mip that
                // suits one suits the other.
                int level = data.SelectLevel(tileWidth);

                int ghostLevel = GhostData != null ? GhostData.SelectLevel(Math.Max(1, GhostWidthPx)) : 0;
                int drawnWidth = 0;
                for (int t = 0; t < tiles; t++)
                {
                    // The last tile absorbs integer-division rounding so the
                    // tiles exactly cover totalWidth with no gap/overhang.
                    bool last = t == tiles - 1;
                    int thisWidth = last ? totalWidth - drawnWidth : tileWidth;
                    float fraction = last ? lastFraction : 1f;
                    var rect = new Rectangle((int)pos.X + drawnWidth, (int)pos.Y, thisWidth, height);
                    drawnWidth += thisWidth;

                    if (thisWidth <= 0)
                    {
                        continue;
                    }

                    if (RenderMode == WaveformRenderMode.Geometry)
                    {
                        DrawGeometry(manager, data, level, rect, Tint, fraction);
                    }
                    else if (RenderMode == WaveformRenderMode.Columns)
                    {
                        DrawColumns(manager, data.LevelData(level), rect, Tint, fraction);
                    }
                    else
                    {
                        DrawGeometryBaked(manager, data, level, rect, Tint, fraction);
                    }

                    if (GhostData != null && t < tiles - 1)
                    {
                        int ghostWidth = Math.Min(GhostWidthPx, totalWidth - drawnWidth);
                        if (ghostWidth > 0)
                        {
                            var ghostRect = new Rectangle((int)pos.X + drawnWidth, (int)pos.Y, ghostWidth, height);
                            if (RenderMode == WaveformRenderMode.Geometry)
                            {
                                DrawGeometry(manager, GhostData, ghostLevel, ghostRect, GhostTint, 1f);
                            }
                            else if (RenderMode == WaveformRenderMode.Columns)
                            {
                                DrawColumns(manager, GhostData.LevelData(ghostLevel), ghostRect, GhostTint, 1f);
                            }
                            else
                            {
                                DrawGeometryBaked(manager, GhostData, ghostLevel, ghostRect, GhostTint, 1f);
                            }
                        }
                    }
                }
            }
        }

        base.Draw();
    }

    private static void DrawGeometry(
        Managers.DrawManager manager, WaveformData data, int level, Rectangle rect, Color tint, float sourceFraction)
    {
        (VertexPositionColor[] vertices, short[] indices, int primitiveCount) = data.BuildGeometry(level, rect, tint, sourceFraction);
        manager.DrawTriangles(vertices, indices, primitiveCount);
    }

    private static void DrawGeometryBaked(
        Managers.DrawManager manager, WaveformData data, int level, Rectangle rect, Color tint, float sourceFraction)
    {
        GeometryVertex[] vertices;
        short[] indices;
        int primitiveCount;
        using (Managers.Telemetry.Scope("Draw.Waveform.Lookup"))
        {
            (vertices, indices, primitiveCount) =
                data.GetGeometryVertices(level, rect.Width, rect.Height, sourceFraction);
        }

        using (Managers.Telemetry.Scope("Draw.Waveform.Append"))
        {
            manager.DrawCachedTriangles(vertices, indices, primitiveCount, new Vector2(rect.X, rect.Y), tint);
        }
    }

    private void DrawColumns(
        Managers.DrawManager manager, float[] minMax, Rectangle rect, Color tint, float sourceFraction)
    {
        // Same leading-window rule as BuildGeometry's, in the per-pixel path.
        int columns = Math.Clamp((int)Math.Round(minMax.Length / 2 * sourceFraction), 1, minMax.Length / 2);
        int height = rect.Height;

        for (int x = 0; x < rect.Width; x++)
        {
            int c = (int)((long)x * columns / rect.Width);
            float top = (1f - MathHelper.Clamp(minMax[c * 2 + 1], -1f, 1f)) * 0.5f * height;
            float bottom = (1f - MathHelper.Clamp(minMax[c * 2], -1f, 1f)) * 0.5f * height;

            int y0 = (int)top;
            int y1 = Math.Max(y0 + 1, (int)Math.Ceiling(bottom)); // ≥1px so silence stays visible
            manager.DrawFilledRectangle(new Rectangle(rect.X + x, rect.Y + y0, 1, Math.Min(y1, height) - y0), tint);
        }
    }
}

public enum WaveformRenderMode
{
    /// <summary>The default and standard mode. Same triangle geometry as
    /// <see cref="Geometry"/>, but the TRIANGULATION
    /// (WaveformData.GetGeometryVertices) runs ONCE per distinct size and the
    /// resulting vertex/index arrays are cached and reused every later frame —
    /// DrawManager.DrawCachedTriangles just translates + tints them into the
    /// shared geometry batch, no re-triangulation, no Texture2D/RenderTarget2D
    /// and so no 4096-texel size limit. Resolution-independent (no
    /// rasterize-then-stretch blur) at low draw-time cost as long as the
    /// caller's own width/height doesn't change.</summary>
    GeometryBaked,

    /// <summary>Real triangle geometry (WaveformData.BuildGeometry),
    /// interleaved with the sprite batch via DrawManager.DrawTriangles —
    /// re-triangulated EVERY frame. Same output as <see cref="GeometryBaked"/>;
    /// prefer that one for anything drawn more than a couple of frames at the
    /// same size (i.e. basically everything). Kept for data that changes every
    /// frame and for comparison.</summary>
    Geometry,

    /// <summary>Per-pixel column rects every frame (no textures). Kept for
    /// comparison/measurement only.</summary>
    Columns,
}
