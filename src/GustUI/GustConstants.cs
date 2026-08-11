using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI
{
    internal static class GustConstants
    {
        public const float FontScale = 0.6f;

        /// <summary>Extra bake-resolution multiplier for text at RenderScale
        /// 1 (FontManager.LoadFont bakes at size/FontScale*supersample, then
        /// draws at scale FontScale/supersample — net on-screen size
        /// unchanged, but the glyph atlas holds more source detail before
        /// the draw-time minify, which linear-filters down noticeably
        /// crisper than the bare size/FontScale bake did: thin weights like
        /// Segoe UI Semilight lose the most detail at low bake resolutions,
        /// so this is where "text isn't very crisp" traces to).
        ///
        /// FontManager.LoadFont multiplies this by the CURRENT
        /// DrawManager.RenderScale before baking (and folds RenderScale
        /// into the bake's cache key), so a HiDPI display keeps the same
        /// oversampling MARGIN instead of it shrinking as RenderScale
        /// grows — without that, a high enough DPI scale eventually turns
        /// the fixed-resolution bake into a genuine upscale (magnification
        /// > 1), which reads as blurry bitmap-stretched text. See
        /// KeyedSpriteFont.DrawScale for the resulting per-bake draw
        /// scale (this constant alone is no longer sufficient — the actual
        /// draw scale now varies per RenderScale, so it can't stay a single
        /// global constant the way EffectiveFontScale used to be).</summary>
        public const float FontBakeSupersample = 1.5f;
    }
}
