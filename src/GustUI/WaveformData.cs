using System;
using System.Collections.Generic;
using GustUI.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GustUI
{
    /// <summary>
    /// Immutable waveform column data for <see cref="Elements.WaveformElement"/>.
    ///
    /// Consumers pass a plain float array of interleaved (min, max) pairs, one
    /// pair per column, values −1..1 — GustUI stays free of any audio-library
    /// types. From that base resolution a mip chain is precomputed (each level
    /// halves the column count, min-of-mins / max-of-maxes, down to 8 columns)
    /// so a narrow draw samples a level close to its pixel width instead of
    /// skipping columns and turning the waveform into noise.
    ///
    /// Also owns the lazily baked per-level textures used by the baked-texture
    /// render mode — the KnobElement pattern: bake alpha-only art once, tint
    /// with the draw color every frame.
    /// </summary>
    public sealed class WaveformData
    {
        /// <summary>Default baked texture height in texels; drawn stretched
        /// to the element rect. A cheap approximation when the display
        /// height is close to this — <see cref="EnsureMinimumTextureHeight"/>
        /// raises it (and drops the stale bake) for callers that display
        /// this data much taller, so the stretch ratio — and with it the
        /// fixed-fraction "always show ≥1 texel of silence" floor below —
        /// stays close to what a native bake at that height would show.</summary>
        public const int DefaultTextureHeight = 64;

        /// <summary>Legacy "Pronounced Peaks" (Options > Display, checked by
        /// default): gamma-boosts quiet content so it doesn't read as a
        /// near-flat line, without artificially loudening true silence
        /// (0 stays 0). Off = raw linear min/max, no boost.</summary>
        public static bool PronouncedPeaks { get; set; } = true;

        /// <summary>Legacy "Peak Bright Waveform" (Options > Display,
        /// UNCHECKED by default — most songs read better fully bright):
        /// dims each column toward the block background in proportion to
        /// how quiet it is, so loud hits visually pop against quieter
        /// stretches. Off (default) = always full brightness.</summary>
        public static bool PeakBrightWaveform { get; set; }

        private const int MinLevelColumns = 8;

        private readonly List<float[]> levels = new List<float[]>();
        private Texture2D[] textures;
        private bool solidBackground;
        private float waveShade;
        private int textureHeight = DefaultTextureHeight;
        private int pendingTextureHeight = DefaultTextureHeight;

        private WaveformData()
        {
        }

        /// <summary>
        /// Builds waveform data (including the mip chain) from interleaved
        /// (min, max) pairs at the base resolution.
        ///
        /// Texture style: with <paramref name="solidBackground"/> false the
        /// bake is an overlay — transparent background, white waveform —
        /// tinted freely at draw time. With true, the bake is a complete
        /// tile face: opaque white background with the waveform darkened to
        /// <paramref name="waveShade"/> — so ONE tinted draw renders both a
        /// solid-colored tile and its waveform (tint × white = tile color,
        /// tint × shade = darker wave). That collapses a filled rect + wave
        /// overlay into a single sprite, which batches with its neighbours —
        /// the fast path for hundreds of timeline blocks (see TVFillImage.Tint).
        /// </summary>
        public static WaveformData FromMinMax(float[] interleavedMinMax, bool solidBackground = false, float waveShade = 0.4f)
        {
            Ensure.NotNull(interleavedMinMax, nameof(interleavedMinMax));

            var data = new WaveformData
            {
                solidBackground = solidBackground,
                waveShade = waveShade,
            };
            data.levels.Add(interleavedMinMax);

            float[] current = interleavedMinMax;
            while (current.Length / 2 > MinLevelColumns)
            {
                int columns = current.Length / 2;
                int halfColumns = (columns + 1) / 2;
                var half = new float[halfColumns * 2];
                for (int c = 0; c < halfColumns; c++)
                {
                    int a = c * 2 * 2;                              // first source pair
                    int b = Math.Min(a + 2, (columns - 1) * 2);     // second source pair (clamped for odd counts)
                    half[c * 2] = Math.Min(current[a], current[b]);
                    half[c * 2 + 1] = Math.Max(current[a + 1], current[b + 1]);
                }

                data.levels.Add(half);
                current = half;
            }

            return data;
        }

        /// <summary>
        /// Whether this data bakes SOLID tile faces (opaque white background,
        /// darkened wave — tint with the tile color) or transparent OVERLAYS
        /// (white wave only — tint with a contrasting wave color). Callers
        /// that draw a texture over an already-filled tile must pick their
        /// tint by this: tinting an overlay bake with the tile's own color
        /// renders the waveform invisible (found on ezmuze's multi-tile
        /// Repeat clips under the geometry render modes).
        /// </summary>
        public bool SolidBackground => solidBackground;

        public int LevelCount => levels.Count;

        public int BaseColumns => levels[0].Length / 2;

        public float[] LevelData(int level) => levels[level];

        public int LevelColumns(int level) => levels[level].Length / 2;

        /// <summary>
        /// The smallest mip level that still has at least
        /// <paramref name="desiredColumns"/> columns (so minification per draw
        /// stays under 2×), falling back to the smallest level available.
        /// </summary>
        public int SelectLevel(int desiredColumns)
        {
            int level = 0;
            while (level + 1 < levels.Count && LevelColumns(level + 1) >= desiredColumns)
            {
                level++;
            }

            return level;
        }

        /// <summary>
        /// The baked texture for a level: one texel column per data column,
        /// antialiased (premultiplied alpha, matching the batch blend state),
        /// tinted at draw time. Style per <see cref="FromMinMax"/>. Baked on
        /// first use per level. Public so consumers can also use it directly
        /// as an image fill (TVFillImage) on an existing rectangle element.
        /// </summary>
        // ---- visual-fidelity pass (2026-08): legacy ezmuze3's waveform draw
        // (StorageEngine.cs RequestWaveFormData) applied two perceptual
        // touches this baker didn't: "PronouncedPeaks" (a gamma curve, 1 -
        // (1-avg)^5, that fills out quiet passages so they don't read as a
        // near-flat line) and "VolumeBasedBrightness" (louder columns drawn
        // brighter/more prominent than quiet ones). Both are PURE PRESENTATION
        // — legacy applied them to the same underlying peak data, not a
        // different analysis — so they're reproduced here entirely inside the
        // texture bake, touching neither the stored min/max data nor the mip
        // chain above. ProminenceExponent < 1 is the equivalent gamma bend
        // (chosen milder than legacy's ^5 — that read as over-saturated at
        // this renderer's antialiased-coverage fill vs. legacy's raw
        // triangle-strip edges); loudness shading below is the brightness cue.
        private const float ProminenceExponent = 0.62f;

        private static float Prominent(float v)
        {
            float clamped = MathHelper.Clamp(v, -1f, 1f);
            if (!PronouncedPeaks)
            {
                return clamped;
            }

            return Math.Sign(clamped) * MathF.Pow(Math.Abs(clamped), ProminenceExponent);
        }

        public Texture2D GetTexture(int level)
        {
            if (textures == null)
            {
                textures = new Texture2D[levels.Count];
            }

            if (textures[level] != null)
            {
                return textures[level];
            }

            float[] minMax = levels[level];
            int columns = minMax.Length / 2;
            int height = textureHeight;
            var pixels = new Color[columns * height];

            for (int c = 0; c < columns; c++)
            {
                float maxV = Prominent(minMax[c * 2 + 1]);
                float minV = Prominent(minMax[c * 2]);

                // Map min/max (−1..1) to a vertical band in texel space;
                // y grows downward, so max maps to the top edge.
                float top = (1f - maxV) * 0.5f * height;
                float bottom = (1f - minV) * 0.5f * height;

                // Silence still reads as a waveform: at least a 1-texel band.
                if (bottom - top < 1f)
                {
                    float mid = (top + bottom) * 0.5f;
                    top = mid - 0.5f;
                    bottom = mid + 0.5f;
                }

                // Loudness cue (legacy VolumeBasedBrightness): this column's
                // OWN band height, 0..1 of the full available half-height —
                // a quiet column shades toward background, a loud one hits
                // full prominence. Measured post-boost (matches what's about
                // to be drawn), pre floor-clamp (a silent column stays flat 0).
                float loudness = MathHelper.Clamp((maxV - minV) * 0.5f, 0f, 1f);

                if (solidBackground)
                {
                    // Opaque face: background white, waveform band darkened —
                    // shade eases from a near-background tint at silence to
                    // the full requested waveShade at full loudness.
                    float shade = MathHelper.Lerp(0.8f, waveShade, loudness);
                    for (int y = 0; y < height; y++)
                    {
                        float coverage = MathHelper.Clamp(Math.Min(y + 1f, bottom) - Math.Max(y, top), 0f, 1f);
                        float v = 1f - coverage * (1f - shade);
                        pixels[y * columns + c] = new Color(v, v, v, 1f);
                    }

                    continue;
                }

                float brightness = PeakBrightWaveform ? MathHelper.Lerp(0.5f, 1f, loudness) : 1f;
                int yStart = Math.Max(0, (int)Math.Floor(top));
                int yEnd = Math.Min(height - 1, (int)Math.Ceiling(bottom) - 1);
                for (int y = yStart; y <= yEnd; y++)
                {
                    // Coverage of texel row [y, y+1) by the band [top, bottom).
                    float coverage = MathHelper.Clamp(Math.Min(y + 1f, bottom) - Math.Max(y, top), 0f, 1f);
                    if (coverage > 0f)
                    {
                        pixels[y * columns + c] = Color.White * (coverage * brightness);
                    }
                }
            }

            var texture = new Texture2D(Resources.StaticResources.GraphicsDevice, columns, height);
            texture.SetData(pixels);
            textures[level] = texture;
            return texture;
        }

        /// <summary>
        /// Tessellates this waveform's min/max envelope into indexed
        /// triangle geometry for <see cref="Managers.DrawManager.DrawTriangles"/>
        /// — the resolution-independent alternative to <see cref="GetTexture"/>'s
        /// bake-to-bitmap-then-stretch path. Same Pronounced Peaks/Peak
        /// Bright Waveform treatment as GetTexture (per-vertex instead of
        /// per-texel), but there is no bake step, no fixed texture
        /// resolution, and so no upscale blur at any zoom — the shape is
        /// exact float geometry regenerated at the requested column count
        /// every call (cheap: N columns of arithmetic, no texture alloc).
        /// solidBackground data isn't supported here (geometry has no
        /// "tile face" concept — draw the block's own fill separately).
        /// </summary>
        public (VertexPositionColor[] Vertices, short[] Indices, int PrimitiveCount) BuildGeometry(int level, Rectangle rect, Color tint)
        {
            float[] minMax = levels[level];
            int columns = minMax.Length / 2;
            if (columns < 1 || rect.Width <= 0 || rect.Height <= 0)
            {
                return (Array.Empty<VertexPositionColor>(), Array.Empty<short>(), 0);
            }

            // short indices (DrawManager.DrawTriangles) cap this at 32767
            // vertices — a block would need >16383 columns to hit that,
            // wildly past any practical zoom level's column count.
            columns = Math.Min(columns, short.MaxValue / 2);

            var vertices = new VertexPositionColor[columns * 2];
            var indices = new short[Math.Max(0, columns - 1) * 6];
            float colWidth = rect.Width / (float)columns;

            for (int c = 0; c < columns; c++)
            {
                float maxV = Prominent(minMax[c * 2 + 1]);
                float minV = Prominent(minMax[c * 2]);

                float top = rect.Y + (1f - maxV) * 0.5f * rect.Height;
                float bottom = rect.Y + (1f - minV) * 0.5f * rect.Height;

                // Silence still reads as a waveform: at least a 1px band —
                // GetTexture's same rule, in float pixels instead of texels.
                if (bottom - top < 1f)
                {
                    float mid = (top + bottom) * 0.5f;
                    top = mid - 0.5f;
                    bottom = mid + 0.5f;
                }

                float loudness = MathHelper.Clamp((maxV - minV) * 0.5f, 0f, 1f);
                float brightness = PeakBrightWaveform ? MathHelper.Lerp(0.5f, 1f, loudness) : 1f;
                Color vColor = tint * brightness;

                float x = rect.X + c * colWidth;
                vertices[c * 2] = new VertexPositionColor(new Vector3(x, top, 0f), vColor);
                vertices[c * 2 + 1] = new VertexPositionColor(new Vector3(x, bottom, 0f), vColor);

                if (c < columns - 1)
                {
                    // Triangle-strip-to-list for this column-to-column
                    // segment: (top_c, bottom_c, top_c+1), (bottom_c,
                    // bottom_c+1, top_c+1) — CW winding after the Y-down
                    // ortho projection (DrawManager.DrawTriangles sets
                    // CullMode.None regardless, so winding is belt-and-
                    // braces here, not load-bearing).
                    short vi = (short)(c * 2);
                    int ii = c * 6;
                    indices[ii + 0] = vi;
                    indices[ii + 1] = (short)(vi + 1);
                    indices[ii + 2] = (short)(vi + 2);
                    indices[ii + 3] = (short)(vi + 1);
                    indices[ii + 4] = (short)(vi + 3);
                    indices[ii + 5] = (short)(vi + 2);
                }
            }

            return (vertices, indices, Math.Max(0, columns - 1) * 2);
        }

        private GeometryVertex[] geometryVertsCache;
        private short[] geometryIndicesCache;
        private int geometryPrimitiveCountCache;
        private int geometryVertsLevel = -1;
        private int geometryVertsWidth = -1;
        private int geometryVertsHeight = -1;

        /// <summary>
        /// Triangulated-once-then-cached alternative to <see cref="GetTexture"/>:
        /// runs this waveform's geometry (<see cref="BuildGeometry"/>) through
        /// the triangulation math ONCE per (level, width, height) and caches
        /// the resulting vertex/index arrays (in LOCAL space, origin at
        /// (0,0)) — reused on every later call with the same key via
        /// <see cref="Managers.DrawManager.DrawCachedTriangles"/>, which
        /// translates + tints them into the shared geometry batch. Gets
        /// GetTexture's "pay the expensive part once" property and
        /// BuildGeometry/DrawTriangles's crispness (real geometry, no
        /// bake-resolution-vs-blur tradeoff) with neither's downside: no
        /// texture/RenderTarget2D at all (2026-08-19 — this used to render
        /// to an offscreen RenderTarget2D via BakeTrianglesToTexture, which
        /// meant SetRenderTarget-timing hazards, VRAM churn, and a queued-
        /// to-next-frame bake to dodge those hazards; the triangulation
        /// math was always the actual cost, not the texture, so caching
        /// just the arrays is strictly simpler and needs none of that —
        /// this can compute synchronously, right here, on a cache miss).
        /// Baked WHITE (color already carries per-column loudness
        /// brightness) — callers still supply their own tint at draw time,
        /// applied by DrawCachedTriangles. Scrolling and clipping never
        /// touch this cache — only a genuine (level, width, height) change
        /// (zoom, row resize) does, exactly the input this method's own
        /// cache key already is.
        /// </summary>
        public (GeometryVertex[] Vertices, short[] Indices, int PrimitiveCount) GetGeometryVertices(int level, int width, int height)
        {
            bool hit = geometryVertsCache != null && geometryVertsLevel == level && geometryVertsWidth == width && geometryVertsHeight == height;
            if (!hit)
            {
                (VertexPositionColor[] raw, short[] indices, int primitiveCount) = BuildGeometry(level, new Rectangle(0, 0, width, height), Color.White);
                var verts = new GeometryVertex[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                {
                    // UV/ClipRect are placeholders — DrawCachedTriangles
                    // overwrites both (UV from the atlas's current white
                    // region, ClipRect from the current scissor) every time
                    // it translates this cache into the batch.
                    verts[i] = new GeometryVertex(new Vector2(raw[i].Position.X, raw[i].Position.Y), raw[i].Color, Vector2.Zero, Vector4.Zero);
                }

                geometryVertsCache = verts;
                geometryIndicesCache = indices;
                geometryPrimitiveCountCache = primitiveCount;
                geometryVertsLevel = level;
                geometryVertsWidth = width;
                geometryVertsHeight = height;
            }

            return (geometryVertsCache, geometryIndicesCache, geometryPrimitiveCountCache);
        }

        /// <summary>
        /// Records that some caller wants this data displayed at least
        /// <paramref name="height"/> texels tall — cheap, no rebake yet
        /// (safe to call every frame, e.g. while a display height is being
        /// dragged live). Call <see cref="FlushPendingTextureHeight"/> once
        /// the caller settles to actually apply it.
        /// </summary>
        public void EnsureMinimumTextureHeight(int height)
        {
            if (height > pendingTextureHeight)
            {
                pendingTextureHeight = height;
            }
        }

        /// <summary>
        /// Applies the tallest height requested via
        /// <see cref="EnsureMinimumTextureHeight"/> since the last flush,
        /// dropping cached textures so they rebake at that height on next
        /// <see cref="GetTexture"/> — the (deliberately rare, one-shot) real
        /// cost of an accurate bake. Never shrinks: a larger bake still
        /// downscales fine for callers displaying this data smaller.
        /// </summary>
        public void FlushPendingTextureHeight()
        {
            if (pendingTextureHeight > textureHeight)
            {
                textureHeight = pendingTextureHeight;
                textures = null;
            }
        }
    }
}
