using System;
using System.Collections.Generic;
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
        /// <summary>Baked texture height in texels; drawn stretched to the element rect.</summary>
        public const int TextureHeight = 64;

        private const int MinLevelColumns = 8;

        private readonly List<float[]> levels = new List<float[]>();
        private Texture2D[] textures;
        private bool solidBackground;
        private float waveShade;

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
            int height = TextureHeight;
            var pixels = new Color[columns * height];

            for (int c = 0; c < columns; c++)
            {
                // Map min/max (−1..1) to a vertical band in texel space;
                // y grows downward, so max maps to the top edge.
                float top = (1f - MathHelper.Clamp(minMax[c * 2 + 1], -1f, 1f)) * 0.5f * height;
                float bottom = (1f - MathHelper.Clamp(minMax[c * 2], -1f, 1f)) * 0.5f * height;

                // Silence still reads as a waveform: at least a 1-texel band.
                if (bottom - top < 1f)
                {
                    float mid = (top + bottom) * 0.5f;
                    top = mid - 0.5f;
                    bottom = mid + 0.5f;
                }

                if (solidBackground)
                {
                    // Opaque face: background white, waveform band darkened.
                    for (int y = 0; y < height; y++)
                    {
                        float coverage = MathHelper.Clamp(Math.Min(y + 1f, bottom) - Math.Max(y, top), 0f, 1f);
                        float v = 1f - coverage * (1f - waveShade);
                        pixels[y * columns + c] = new Color(v, v, v, 1f);
                    }

                    continue;
                }

                int yStart = Math.Max(0, (int)Math.Floor(top));
                int yEnd = Math.Min(height - 1, (int)Math.Ceiling(bottom) - 1);
                for (int y = yStart; y <= yEnd; y++)
                {
                    // Coverage of texel row [y, y+1) by the band [top, bottom).
                    float coverage = MathHelper.Clamp(Math.Min(y + 1f, bottom) - Math.Max(y, top), 0f, 1f);
                    if (coverage > 0f)
                    {
                        pixels[y * columns + c] = Color.White * coverage;
                    }
                }
            }

            var texture = new Texture2D(Resources.StaticResources.GraphicsDevice, columns, height);
            texture.SetData(pixels);
            textures[level] = texture;
            return texture;
        }
    }
}
