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
        /// <summary>Global text-rendering strategy toggle (app Preferences >
        /// Waveform's sibling — a future "Text" section): off (default) is
        /// the classic per-(family,size,RenderScale) bitmap atlas path; on
        /// switches TextElement/TooltipElement to the SDF path (DrawManager.
        /// DrawSdfString) — crisp at any size/zoom from a single per-family
        /// bake. Same "GustUI exposes a plain static toggle, the app's
        /// UserPreferences owns persistence and pushes into it" convention
        /// as WaveformData.PronouncedPeaks/PeakBrightWaveform.</summary>
        public static bool UseSdf { get; set; }

        GraphicsDevice graphicsDevice;
        SpriteBatch spriteBatch;
        IContentManager content;
        public FontManager(GraphicsDevice graphicsDevice, IContentManager content)
        {
            this.graphicsDevice = graphicsDevice;
            this.content = content;
            this.spriteBatch = new SpriteBatch(this.graphicsDevice);
        }

        private readonly Dictionary<string, KeyedSpriteFont> FontCache = new();
        private readonly Dictionary<FontCacheKey, FontCacheValue> FontWriteCache = new Dictionary<FontCacheKey, FontCacheValue>();
        private readonly Dictionary<FontCacheKey, int> FontRequestCount = new Dictionary<FontCacheKey, int>();
        private readonly Dictionary<FontCacheKey, DateTime> FontLastUsed = new Dictionary<FontCacheKey, DateTime>();
        // IEquatable + explicit hash: without them Dictionary falls back to
        // reflection-based ValueType equality with boxing on every lookup —
        // measured at ~0.15 ms per DrawString under interpreted WASM, which
        // made text the single largest draw cost.
        internal struct FontCacheKey : IEquatable<FontCacheKey>
        {
            internal string FontKey;
            internal string Text;
            internal Color color;

            public bool Equals(FontCacheKey other)
            {
                return color.PackedValue == other.color.PackedValue
                    && Text == other.Text
                    && FontKey == other.FontKey;
            }

            public override bool Equals(object obj) => obj is FontCacheKey other && Equals(other);

            public override int GetHashCode()
            {
                int h = Text != null ? Text.GetHashCode() : 0;
                h = (h * 397) ^ (FontKey != null ? FontKey.GetHashCode() : 0);
                h = (h * 397) ^ (int)color.PackedValue;
                return h;
            }
        }

        internal class FontCacheValue
        {
            internal Texture2D Texture2D;
            internal DateTime LastUsed;
        }

        // Coarse per-frame clock: LastUsed feeds a 10s expiry, so refreshing it
        // from a value sampled once per ManageCaches call (instead of
        // DateTime.Now per DrawString) is plenty.
        private DateTime frameNow = DateTime.Now;

        public Texture2D GetCachedText(string fontKey, string text, Color color)
        {
            FontCacheKey key = new FontCacheKey
            {
                FontKey = fontKey,
                Text = text,
                color = color
            };

            if (FontWriteCache.TryGetValue(key, out FontCacheValue cached))
            {
                cached.LastUsed = frameNow;
                return cached.Texture2D;
            }

            if (FontRequestCount.TryGetValue(key, out int count))
            {
                FontRequestCount[key] = count + 1;
            }
            else
            {
                FontRequestCount.Add(key, 0);
            }

            return null;
        }

        internal string CacheInfo => $"Fonts Cached: {FontCache.Count}, TRC: {FontRequestCount.Count}, TIC: {FontWriteCache.Count}";

        private DateTime lastClean = DateTime.Now;
        private DateTime lastManage = DateTime.MinValue;
        private readonly List<FontCacheKey> scratchKeys = new List<FontCacheKey>();

        internal void ManageCaches()
        {
            frameNow = DateTime.Now;

            // Expiry/bake bookkeeping only needs to run a few times a second,
            // not per frame (the per-frame LINQ over both caches showed up at
            // ~1.4 ms/frame in the interpreter).
            if (frameNow - lastManage < TimeSpan.FromMilliseconds(250))
            {
                return;
            }

            lastManage = frameNow;

            scratchKeys.Clear();
            foreach (var x in FontWriteCache)
            {
                if (frameNow - x.Value.LastUsed > TimeSpan.FromSeconds(10))
                {
                    scratchKeys.Add(x.Key);
                }
            }

            foreach (var e in scratchKeys)
            {
                FontWriteCache.Remove(e);
                FontRequestCount.Remove(e);
            }

            if (frameNow - lastClean > TimeSpan.FromSeconds(10))
            {
                lastClean = frameNow;
                scratchKeys.Clear();
                foreach (var x in FontRequestCount)
                {
                    if (x.Value < 50)
                    {
                        scratchKeys.Add(x.Key);
                    }
                }

                foreach (var r in scratchKeys)
                {
                    FontRequestCount.Remove(r);
                }
            }

            scratchKeys.Clear();
            foreach (var x in FontRequestCount)
            {
                if (x.Value > 50 && !FontWriteCache.ContainsKey(x.Key))
                {
                    scratchKeys.Add(x.Key);
                }
            }

            foreach (var r in scratchKeys)
            {
                var font = LoadFont(r.FontKey);
                if (font != null)
                {
                    var size = font.MeasureString(r.Text);
                    RenderTarget2D rt = new RenderTarget2D(graphicsDevice, (int)(size.X) + 2, (int)(size.Y) + 2);
                    graphicsDevice.SetRenderTarget(rt);
                    graphicsDevice.Clear(Color.Transparent);
                    spriteBatch.Begin(SpriteSortMode.Deferred);
                    // Baked in WHITE, never r.color: DrawString's cache-hit path
                    // (below, and DrawManager.DrawString) draws this texture
                    // TINTED by the caller's color every frame. Baking the
                    // color in here as well meant a promoted label's color got
                    // squared per channel the instant it crossed the 50-draw
                    // threshold (e.g. a 0.78 gray channel -> 0.78^2 ~= 0.61) -
                    // visibly dimmer, and fast enough at a high frame rate to
                    // read as "text loads, then dims a fraction of a second
                    // later". The texture is a colorless alpha mask; color is
                    // applied exactly once, at draw time, same as the
                    // un-cached SpriteFont.DrawString path above it.
                    spriteBatch.DrawString(font, r.Text, Vector2.Zero, Color.White);
                    spriteBatch.End();
                    FontWriteCache.Add(r, new FontCacheValue
                    {
                        LastUsed = DateTime.Now,
                        Texture2D = rt
                    });
                }
            }
        }
        private SpriteFont LoadFont(string key)
        {
            if (FontCache.TryGetValue(key, out var cachedFont))
            {
                return cachedFont.SpriteFont;
            }
            return null;
        }
        /// <summary>
        /// On-screen size of <paramref name="text"/> in the given font — the
        /// same scaled measurement <see cref="Elements.TextElement"/> renders
        /// with, for consumers that lay labels out themselves (fit/clip/align
        /// decisions before binding a pooled element).
        /// </summary>
        public Vector2 MeasureText(TraitValues.TVFont font, string text)
        {
            if (font == null || font.Family == null || string.IsNullOrEmpty(text))
            {
                return Vector2.Zero;
            }

            if (UseSdf)
            {
                return LoadSdfFont(font.Family).MeasureString(text, font.Size);
            }

            var keyedFont = LoadFont(font.Family, font.Size);
            return keyedFont.SpriteFont.MeasureString(text) * keyedFont.DrawScale;
        }

        /// <summary>Current DrawManager.RenderScale, rounded to 2 decimals so
        /// float jitter can't spawn a fresh bake+cache-entry every frame —
        /// real DPI values are discrete steps (1, 1.25, 1.5, 2, ...), never
        /// a continuously drifting number. Defaults to 1 if DrawManager
        /// isn't up yet (defensive only: LoadFont is never actually called
        /// that early in practice, since it's driven by Draw()).</summary>
        private static float CurrentBakeRenderScale()
        {
            float scale = Resources.StaticResources?.DrawManager != null
                ? Math.Max(1f, Resources.StaticResources.DrawManager.RenderScale)
                : 1f;
            return (float)Math.Round(scale, 2);
        }

        public KeyedSpriteFont LoadFont(string path, float size)
        {
            float renderScale = CurrentBakeRenderScale();
            var key = $"{path}_{size}_{renderScale}";

            if (FontCache.TryGetValue(key, out var cachedFont))
            {
                return cachedFont;
            }

            // Supersample scales with RenderScale (see GustConstants.
            // FontBakeSupersample's doc) so the oversampling margin that
            // keeps text crisp doesn't shrink away on HiDPI displays.
            float supersample = GustConstants.FontBakeSupersample * renderScale;
            float drawScale = GustConstants.FontScale / supersample;
            var bake = TtfFontBaker.Bake(content.ReadAllBytes(path), size / GustConstants.FontScale * supersample, 2048, 2048, new[] { CharacterRange.BasicLatin, new CharacterRange((char)Enum.GetValues(typeof(UIFont.Symbol)).Cast<UIFont.Symbol>().Min(), (char)Enum.GetValues(typeof(UIFont.Symbol)).Cast<UIFont.Symbol>().Max()) });

            var font = bake.CreateSpriteFont(graphicsDevice);

            var keyed = new KeyedSpriteFont { SpriteFont = font, Key = key, DrawScale = drawScale };
            FontCache.Add(key, keyed);

            return keyed;
        }

        public class KeyedSpriteFont
        {
            public SpriteFont SpriteFont { get; set; }
            public string Key { get; set; }

            /// <summary>Sprite draw-time scale that cancels this specific
            /// bake's supersample back out to the intended on-screen size
            /// (RenderScale-dependent — see LoadFont — so it varies per
            /// bake, unlike the old fixed GustConstants.EffectiveFontScale
            /// constant it replaces).</summary>
            public float DrawScale { get; set; }

            internal Vector2 MeasureString(string consoleText)
            {
                return SpriteFont.MeasureString(consoleText);
            }
        }

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

        /// <summary>SDF counterpart to <see cref="LoadFont(string, float)"/> —
        /// see <see cref="SdfBakeEmSize"/> for why this takes no size
        /// parameter at all. Character coverage is BasicLatin plus whatever
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

        /// <summary>SDF counterpart to <see cref="MeasureText"/>.</summary>
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
    /// atlas + glyph table — the SDF counterpart to
    /// <see cref="FontManager.KeyedSpriteFont"/>. Holds no notion of a target
    /// pixel size (unlike KeyedSpriteFont, which IS baked at one) since every
    /// glyph rect/metric here is resolution-independent until a caller scales
    /// it by (targetPixelSize / EmSize) — see <see cref="MeasureString"/> and
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
        /// for drop-in parity with the bitmap path's MeasureString callers.</summary>
        public Vector2 MeasureString(string text, float targetPixelSize)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new Vector2(0, targetPixelSize);
            }

            float scale = targetPixelSize / EmSize;
            float width = 0f;
            foreach (char c in text)
            {
                width += TryGetGlyph(c, out var g) ? g.XAdvance * scale : 0f;
            }

            return new Vector2(width, targetPixelSize);
        }
    }
}
