using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using Microsoft.Xna.Framework.Media;
using nkast.Wasm.Dom;
using SpriteFontPlus;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Managers
{
    public class FontManager
    {
        GraphicsDevice graphicsDevice;
        IContentManager content;
        public FontManager(GraphicsDevice graphicsDevice, IContentManager content)
        {
            this.graphicsDevice = graphicsDevice;
            this.content = content;
        }

        /// <summary>SDF font atlases loaded so far, keyed by family — a debug-overlay stat, not load-bearing anywhere.</summary>
        internal string CacheInfo => $"SDF fonts cached: {SdfFontCache.Count}";

        private readonly Dictionary<string, SdfFont> SdfFontCache = new();

        /// <summary>Reference EM size the SDF atlas is baked at — unlike the
        /// bitmap path this has NO relationship to any on-screen text size or
        /// RenderScale: the distance field is resolution-independent, so one
        /// bake per FAMILY (not per family+size+RenderScale) serves every
        /// caller at every size and every DPI. Large enough that even the
        /// thinnest strokes still rasterize as a clean, well-separated
        /// distance field before the SdfFontBaker.Padding border clips it.</summary>
        private const float SdfBakeEmSize = 64f;

        /// <summary>Codepoints StbTrueTypeSharp's stbtt_GetGlyphSDF crashes
        /// on: a genuine, UNCATCHABLE CLR stack overflow inside its bezier-
        /// flattening ray-intersection/cubic-solve code, triggered by a
        /// specific degenerate curve shape in each of these two glyphs
        /// (confirmed for segmdl2.ttf; World also crashes SegoeIcons.ttf's
        /// own glyph at the same codepoint — Emoji2 wasn't re-checked there
        /// since the app never requests it through that font anyway).
        /// Found by isolating each of the app's ~196 actually-used
        /// UIFont.Symbol codepoints in its own OS process one at a time (a
        /// managed try/catch cannot survive a real stack overflow, so this
        /// could only be done OUTSIDE the running app — see the session
        /// that added this comment for the throwaway repro tool). Neither
        /// icon is used anywhere in the app today; excluding just these two
        /// from the SDF bake — instead of dropping the ENTIRE icon range,
        /// the original, much blunter fix — is a silent, harmless gap: they
        /// simply won't render in SDF mode (TryGetGlyph returns false, same
        /// as any other missing glyph) until this stb bug is fixed upstream
        /// or independently worked around (e.g. a from-scratch SDF
        /// generator, or patching StbTrueTypeSharp's bezier subdivision to
        /// cap recursion depth instead of relying on convergence).</summary>
        private static readonly HashSet<int> SdfUnsafeSymbolCodepoints = new HashSet<int>
        {
            (int)UIFont.Symbol.World,
            (int)UIFont.Symbol.Emoji2,
        };

        /// <summary>Which UIFont.Symbol codepoints to include in a given
        /// font's SDF bake — NOT a blanket "every enum value, every font"
        /// sweep. Two reasons: (1) most fonts never render an icon
        /// codepoint at all (segoeuisl.ttf/segoeuib.ttf are plain text
        /// fonts — BasicLatin alone covers everything they're ever asked to
        /// draw); (2) stbtt_GetGlyphSDF's crash risk (see
        /// SdfUnsafeSymbolCodepoints) is PER-GLYPH, i.e. per (font,
        /// codepoint) pair, not just per codepoint — segmdl2.ttf's full
        /// icon set was exhaustively verified safe (minus the two excluded
        /// there), but SegoeIcons.ttf was NOT: it has its OWN, DIFFERENT
        /// crashers (Setting/Favorite/People confirmed, likely more
        /// unscanned — its glyph OUTLINES for the "same" codepoints are
        /// unrelated data, so segmdl2.ttf's clean bill of health says
        /// nothing about it). Rather than exhaustively re-verify a whole
        /// second ~2200-codepoint space for two icons, SegoeIcons.ttf gets
        /// only the exact two codepoints the app ever actually asks it to
        /// render (Theme.Icons.CloseIcon/MaximizeIcon) — individually
        /// confirmed safe. Any font not listed here gets none (BasicLatin
        /// only) — the original all-icons-excluded fix's fallback.</summary>
        private static IEnumerable<CharacterRange> IconRangesFor(string path)
        {
            if (path == "segmdl2.ttf")
            {
                return Enum.GetValues(typeof(UIFont.Symbol))
                    .Cast<UIFont.Symbol>()
                    .Where(s => !SdfUnsafeSymbolCodepoints.Contains((int)s))
                    .Select(s => new CharacterRange((char)s));
            }

            if (path == "SegoeIcons.ttf")
            {
                return new[]
                {
                    new CharacterRange((char)UIFont.Symbol.Cancel),
                    new CharacterRange((char)UIFont.Symbol.FullScreen),
                };
            }

            return Array.Empty<CharacterRange>();
        }

        /// <summary>Loads (and caches, one bake per family) the SDF atlas for
        /// <paramref name="path"/> — see <see cref="SdfBakeEmSize"/> for why
        /// this takes no size parameter at all. Character coverage is
        /// BasicLatin plus whatever
        /// <see cref="IconRangesFor"/> says this specific font needs — see
        /// its doc for why that's per-font rather than a blanket sweep.</summary>
        public SdfFont LoadSdfFont(string path)
        {
            if (SdfFontCache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var bake = SdfFontBaker.Bake(content.ReadAllBytes(path), SdfBakeEmSize, 2048, 2048,
                new[] { CharacterRange.BasicLatin }.Concat(IconRangesFor(path)));

            var texture = bake.CreateTexture(graphicsDevice);
            var font = new SdfFont(texture, bake.Glyphs, bake.EmSize, path);
            SdfFontCache.Add(path, font);
            return font;
        }

        /// <summary>On-screen size of <paramref name="text"/> in the given
        /// font — the same scaled measurement <see cref="Elements.TextElement"/>
        /// renders with, for consumers that lay labels out themselves
        /// (fit/clip/align decisions before binding a pooled element).</summary>
        public Vector2 MeasureSdfText(TraitValues.TVFont font, string text)
        {
            if (font == null || font.Family == null || string.IsNullOrEmpty(text))
            {
                return Vector2.Zero;
            }

            return LoadSdfFont(font.Family).MeasureString(text, font.Size);
        }
    }

    /// <summary>Draw-ready wrapper around an <see cref="SdfFontBakerResult"/>'s
    /// atlas + glyph table. Holds no notion of a target pixel size — unlike
    /// the retired bitmap path's per-size bake, every glyph rect/metric here
    /// is resolution-independent until a caller scales it by
    /// (targetPixelSize / EmSize) — see <see cref="MeasureString"/> and
    /// DrawManager.DrawSdfString.</summary>
    public class SdfFont
    {
        public Texture2D Atlas { get; }
        public Dictionary<int, SpriteFontPlus.SdfGlyphInfo> Glyphs { get; }
        public float EmSize { get; }
        public string Key { get; }

        public SdfFont(Texture2D atlas, Dictionary<int, SpriteFontPlus.SdfGlyphInfo> glyphs, float emSize, string key)
        {
            Atlas = atlas;
            Glyphs = glyphs;
            EmSize = emSize;
            Key = key;
        }

        public bool TryGetGlyph(char c, out SpriteFontPlus.SdfGlyphInfo glyph) => Glyphs.TryGetValue(c, out glyph);

        /// <summary>Same "line height == the requested pixel size" convention
        /// TtfFontBakerResult.CreateSpriteFont's LineSpacing constructor arg
        /// already uses (not a real ascent+descent+lineGap metric) — kept
        /// for drop-in parity with the bitmap path's MeasureString callers,
        /// which (via MonoGame's own SpriteFont.MeasureString) DOES split on
        /// embedded '\n' and multiply height by line count — this one didn't
        /// (found 2026-08-12: TextElement.Draw()'s own per-line loop measures
        /// each wrapped line separately via getText()'s wrapCacheLineSizes,
        /// so on-screen wrapping always rendered correctly; only
        /// TextElement.CalculatedSize() — which hands the WHOLE wrapped,
        /// already-'\n'-joined string to this method in one call — silently
        /// got back a single line's height back for an N-line block. Callers
        /// that reserve layout space from CalculatedSize() (PreferencesModal's
        /// row-height sizing) under-reserved for any hint wrapping to more
        /// than one line, and the next row would overlap it — invisible until
        /// a hint actually wrapped far enough for the shortfall to clear the
        /// row gap, e.g. wider text at a taller font size.</summary>
        public Vector2 MeasureString(string text, float targetPixelSize)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new Vector2(0, targetPixelSize);
            }

            float scale = targetPixelSize / EmSize;
            string[] lines = text.Split('\n');
            float maxWidth = 0f;
            foreach (string line in lines)
            {
                float width = 0f;
                foreach (char c in line)
                {
                    width += TryGetGlyph(c, out var g) ? g.XAdvance * scale : 0f;
                }

                if (width > maxWidth)
                {
                    maxWidth = width;
                }
            }

            return new Vector2(maxWidth, targetPixelSize * lines.Length);
        }
    }
}
