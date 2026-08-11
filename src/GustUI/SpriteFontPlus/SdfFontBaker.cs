using StbTrueTypeSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SpriteFontPlus
{
    /// <summary>One glyph's atlas rect plus its EM-relative placement, all in
    /// pixels AT THE BAKE'S REFERENCE SIZE (<see cref="SdfFontBakerResult.EmSize"/>)
    /// — unlike <see cref="GlyphInfo"/>'s bitmap-atlas counterpart, an SDF
    /// glyph is baked ONCE PER FONT FAMILY, not once per (family, size): the
    /// distance field itself is resolution-independent, so a caller scales
    /// XOffset/YOffset/XAdvance/the atlas rect by (targetPixelSize / EmSize)
    /// at draw time instead of re-baking.</summary>
    public struct SdfGlyphInfo
    {
        public int X, Y, Width, Height;
        public float XOffset, YOffset;
        public float XAdvance;
    }

    public class SdfFontBakerResult
    {
        public Dictionary<int, SdfGlyphInfo> Glyphs { get; }
        public float EmSize { get; }
        public byte[] Pixels { get; }
        public int Width { get; }
        public int Height { get; }

        public SdfFontBakerResult(Dictionary<int, SdfGlyphInfo> glyphs, float emSize, byte[] pixels, int width, int height)
        {
            Glyphs = glyphs;
            EmSize = emSize;
            Pixels = pixels;
            Width = width;
            Height = height;
        }

        /// <summary>Single-channel distance value replicated across RGBA
        /// (same "bake once, tint/threshold at draw time" convention as
        /// TtfFontBakerResult.CreateSpriteFont's coverage bitmap) — Alpha8
        /// would halve this texture's memory, but Color keeps the SDF atlas
        /// on the exact same GraphicsDevice.Textures[0]/SamplerState path
        /// every other GustUI texture already uses, with zero format-support
        /// risk under Reach on either platform (Alpha8 support is spottier).</summary>
        public Texture2D CreateTexture(GraphicsDevice graphicsDevice)
        {
            var rgb = new Color[Width * Height];
            for (int i = 0; i < Pixels.Length; ++i)
            {
                byte b = Pixels[i];
                rgb[i] = new Color(b, b, b, b);
            }

            var texture = new Texture2D(graphicsDevice, Width, Height);
            texture.SetData(rgb);
            return texture;
        }
    }

    /// <summary>Signed-distance-field counterpart to <see cref="TtfFontBaker"/>:
    /// same stb_truetype dependency, same CharacterRange-driven codepoint
    /// sweep (so callers baking "the same characters" get the same glyph
    /// coverage either way), but rasterizes each glyph as an SDF
    /// (stbtt_GetGlyphSDF) instead of plain coverage (stbtt_PackFontRange).
    /// Own shelf packer instead of stb's pack context — GetGlyphSDF hands
    /// back one already-sized bitmap per glyph rather than filling a shared
    /// target the way PackFontRange does, so there's nothing for stb's own
    /// packer to do here.</summary>
    public static class SdfFontBaker
    {
        /// <summary>Border, in pixels, baked around every glyph's shape so
        /// the distance field has room to represent both outward AND inward
        /// distance before clamping — too little padding clips the outer
        /// glow/AA falloff at large draw sizes.</summary>
        public const int Padding = 6;

        /// <summary>The distance-byte value that represents the glyph's
        /// exact edge (stb_truetype convention: 0 = far outside, 255 = far
        /// inside, this = the boundary) — the draw-time shader thresholds
        /// around this same value (as a 0..1 fraction) to decide fill.</summary>
        public const byte OnEdgeValue = 128;

        /// <summary>How many distance-bytes one pixel of ACTUAL outward/inward
        /// distance covers, at the bake's reference EM size — combined with
        /// <see cref="Padding"/>, this is stb_truetype's own recommended
        /// pairing (pixel_dist_scale = onedge_value / padding) so the full
        /// padding border maps to exactly the 0..255 byte range.</summary>
        public const float PixelDistScale = (float)OnEdgeValue / Padding;

        public unsafe static SdfFontBakerResult Bake(byte[] ttf, float emSize, int bitmapWidth, int bitmapHeight,
            IEnumerable<CharacterRange> characterRanges)
        {
            if (ttf == null || ttf.Length == 0)
            {
                throw new ArgumentNullException(nameof(ttf));
            }

            if (emSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(emSize));
            }

            if (characterRanges == null || !characterRanges.Any())
            {
                throw new ArgumentException("characterRanges must have at least one value.");
            }

            var glyphs = new Dictionary<int, SdfGlyphInfo>();
            var pixels = new byte[bitmapWidth * bitmapHeight];

            // stbtt_fontinfo.data is a raw pointer set by stbtt_InitFont
            // straight into ttf's own memory — every later native call that
            // takes fontInfo (FindGlyphIndex, GetGlyphHMetrics, GetGlyphSDF)
            // dereferences it right back through that same pointer, so ttf
            // must stay PINNED for the whole bake, not just the InitFont
            // call. An earlier version of this method only pinned it for
            // InitFont: nothing crashed (the GC doesn't compact THAT
            // aggressively on every run), but whenever it DID relocate the
            // array mid-bake, every subsequent glyph read through a stale
            // pointer — corrupted-looking but structurally plausible glyph
            // shapes, which is exactly why this reads as "wrong characters"
            // rather than a crash or garbage pixels.
            StbTrueType.stbtt_fontinfo fontInfo = new StbTrueType.stbtt_fontinfo();
            fixed (byte* ttfPtr = ttf)
            {
                if (StbTrueType.stbtt_InitFont(fontInfo, ttfPtr, 0) == 0)
                {
                    throw new Exception("Failed to init font.");
                }

                float scale = StbTrueType.stbtt_ScaleForPixelHeight(fontInfo, emSize);

                // stbtt_GetGlyphSDF's own yoff is BASELINE-relative (glyphs
                // extend upward from 0, so yoff is typically negative) — but
                // every caller (TextElement etc.) positions text the same
                // way the bitmap path expects: position.Y is the TOP of the
                // line. TtfFontBaker gets this for free from stb's own
                // PackFontRange bookkeeping (see its "yOff += ascent *
                // scaleFactor" line); this baker has to do the equivalent
                // rebase itself, once, here.
                int ascent, descent, lineGap;
                StbTrueType.stbtt_GetFontVMetrics(fontInfo, &ascent, &descent, &lineGap);
                float ascentPixels = ascent * scale;

                // Shelf packer: left-to-right within a shelf, wrap to a new
                // shelf (height = the tallest glyph placed on it) when a row
                // fills — plenty for a one-time bake of a bounded character
                // set, and far simpler than a bin packer for a problem this
                // small.
                int penX = 0, penY = 0, shelfHeight = 0;

                foreach (CharacterRange range in characterRanges)
                {
                    if (range.Start > range.End)
                    {
                        continue;
                    }

                    for (int codepoint = range.Start; codepoint <= range.End; codepoint++)
                    {
                        int glyphIndex = StbTrueType.stbtt_FindGlyphIndex(fontInfo, codepoint);
                        if (glyphIndex == 0)
                        {
                            continue; // no glyph for this codepoint in this font — leave it out of the atlas entirely
                        }

                        // advance/metrics are recorded for EVERY valid glyph
                        // index, including zero-area ones (space, and anything
                        // stb hands back a null SDF bitmap for) — skipping
                        // those here (an earlier version of this method did)
                        // drops the glyph's XAdvance along with its (correctly
                        // nonexistent) pixels, which collapses every space in
                        // any drawn string: "a b" measures/advances as "ab".
                        int advanceWidth, leftSideBearing;
                        StbTrueType.stbtt_GetGlyphHMetrics(fontInfo, glyphIndex, &advanceWidth, &leftSideBearing);

                        int w = 0, h = 0, xoff = 0, yoff = 0;
                        byte* sdf = StbTrueType.stbtt_GetGlyphSDF(fontInfo, scale, glyphIndex, Padding, OnEdgeValue, PixelDistScale, &w, &h, &xoff, &yoff);

                        // Captured BEFORE penX/penY advance below — the
                        // dictionary entry must record where THIS glyph was
                        // actually blitted, not wherever the pen ends up
                        // after (the bug this replaced: recording the
                        // POST-advance pen position meant every glyph's
                        // rect pointed at the NEXT glyph's slot instead of
                        // its own, so drawing codepoint N always sampled
                        // whatever N+1 baked to).
                        int glyphX = penX, glyphY = penY;

                        if (sdf != null && w > 0 && h > 0)
                        {
                            if (penX + w > bitmapWidth)
                            {
                                penX = 0;
                                penY += shelfHeight + 1;
                                shelfHeight = 0;
                                glyphX = penX;
                                glyphY = penY;
                            }

                            if (penY + h > bitmapHeight)
                            {
                                // Atlas budget exhausted: drop this glyph's
                                // PIXELS (not its advance — see above) rather
                                // than corrupt earlier ones — same silent-cap
                                // tradeoff TtfFontBaker's fixed 2048x2048
                                // accepts today (see GustConstants.
                                // FontBakeSupersample's sibling doc for the
                                // equivalent bitmap-path budget).
                                StbTrueType.stbtt_FreeSDF(sdf, null);
                                w = 0;
                                h = 0;
                            }
                            else
                            {
                                for (int y = 0; y < h; y++)
                                {
                                    for (int x = 0; x < w; x++)
                                    {
                                        pixels[(penY + y) * bitmapWidth + (penX + x)] = sdf[y * w + x];
                                    }
                                }

                                StbTrueType.stbtt_FreeSDF(sdf, null);
                                penX += w + 1;
                                shelfHeight = Math.Max(shelfHeight, h);
                            }
                        }

                        glyphs[codepoint] = new SdfGlyphInfo
                        {
                            X = glyphX,
                            Y = glyphY,
                            Width = w,
                            Height = h,
                            XOffset = xoff,
                            YOffset = yoff + ascentPixels,
                            XAdvance = advanceWidth * scale,
                        };
                    }
                }
            }

            return new SdfFontBakerResult(glyphs, emSize, pixels, bitmapWidth, bitmapHeight);
        }
    }
}
