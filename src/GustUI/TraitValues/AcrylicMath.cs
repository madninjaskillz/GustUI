#nullable enable

using System;

namespace GustUI.TraitValues
{
    /// <summary>
    /// The decisions behind <see cref="AcrylicLayer"/> that are not GPU calls:
    /// when the glass needs blurring again, and what the grain tile holds.
    /// Plain C# with no graphics types, so it is tested without a device
    /// (ezmuze's AcrylicMathTests link this file in).
    /// </summary>
    public static class AcrylicMath
    {
        /// <summary>The grain tile's side, in texels.</summary>
        public const int GrainSize = 128;

        /// <summary>The grain's fixed seed: every run, every head, the same
        /// tile, so the material never shimmers between launches.</summary>
        public const int GrainSeed = 0x5eed;

        /// <summary>
        /// Whether the glass must be blurred again: only when the background's
        /// picture has moved on since the last blur, or the glass has no
        /// usable target at the right size. A frame on which neither is true
        /// costs nothing but the surfaces' own quads.
        /// </summary>
        public static bool ShouldReblur(int blurredVersion, int pictureVersion, bool targetMissingOrResized)
            => targetMissingOrResized || blurredVersion != pictureVersion;

        /// <summary>
        /// Fills <paramref name="rgba"/> (<see cref="GrainSize"/> squared
        /// texels, four bytes each, straight alpha) with the grain: each
        /// texel pure white or pure black at a random alpha, from
        /// <paramref name="seed"/>. Drawn at a low strength over the glass,
        /// the white and black specks lighten and darken it in equal measure,
        /// so the grain adds texture without shifting the average brightness.
        /// </summary>
        public static void FillGrain(Span<byte> rgba, int seed = GrainSeed)
        {
            if (rgba.Length < GrainSize * GrainSize * 4)
            {
                throw new ArgumentException("too small for the grain tile", nameof(rgba));
            }

            var random = new Random(seed);
            for (int i = 0; i < GrainSize * GrainSize; i++)
            {
                byte alpha = (byte)random.Next(0, 256);
                byte level = random.Next(2) == 0 ? (byte)255 : (byte)0;
                rgba[i * 4] = level;
                rgba[(i * 4) + 1] = level;
                rgba[(i * 4) + 2] = level;
                rgba[(i * 4) + 3] = alpha;
            }
        }
    }
}
