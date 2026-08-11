using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace GustUI.Elements
{
    [ElementTraits(
        typeof(TextTrait), 
        typeof(PositionTrait), 
        typeof(ForegroundColorTrait), 
        typeof(FontTrait), 
        typeof(SizeTrait),
        typeof(VerticalAlignmentTrait), 
        typeof(HorizontalAlignmentTrait))]
    public class TextElement : Element
    {
        /// <summary>
        /// False renders the text as a single unwrapped line (overflow is the
        /// owner's concern — typically a ClipChildren parent). Skips the
        /// per-word measuring pass entirely, so it is also the cheap mode for
        /// pooled labels whose width changes every frame.
        /// </summary>
        public bool WordWrap { get; set; } = true;

        // Hot trait references (resolved once; the trait set never shrinks).
        private readonly FontTrait fontTrait;
        private readonly TextTrait textTrait;
        private readonly ForegroundColorTrait foregroundTrait;

        public TextElement()
        {
            fontTrait = ElementTrait<FontTrait>();
            textTrait = ElementTrait<TextTrait>();
            foregroundTrait = ElementTrait<ForegroundColorTrait>();
        }

        // FontManager.LoadFont builds an interpolated key string per call, so
        // the resolved font is cached against (family, size) here.
        private Managers.FontManager.KeyedSpriteFont cachedFont;
        private string cachedFontFamily;
        private float cachedFontSize = float.MinValue;

        private Managers.FontManager.KeyedSpriteFont GetFont(string family, float size)
        {
            if (cachedFont == null || cachedFontFamily != family || cachedFontSize != size)
            {
                cachedFont = Resources.StaticResources.FontManager.LoadFont(family, size);
                cachedFontFamily = family;
                cachedFontSize = size;
            }

            return cachedFont;
        }

        // Word-wrap result cache: wrapping calls MeasureString per word, which
        // is far too expensive to repeat every Draw for static labels. The
        // cache is invalidated whenever the source text, font or wrap width
        // changes. (Draw rewrites SizeTrait.Y each frame, so only X is keyed.)
        private string wrapCacheResult;
        private string wrapCacheText;
        private string wrapCacheFamily;
        private float wrapCacheFontSize = float.MinValue;
        private float wrapCacheWidth = float.MinValue;
        private string[] wrapCacheLines;
        private Vector2[] wrapCacheLineSizes; // pre-multiplied by FontScale
        private Vector2 wrapCacheTotalSize;   // pre-multiplied by FontScale

        /// <summary>Resolves the per-line measurement function for the
        /// CURRENT render mode/font — SDF (Managers.FontManager.UseSdf,
        /// unless this element has an outline border, which the SDF path
        /// doesn't support yet and silently falls back to bitmap for) or the
        /// classic per-size bitmap atlas. Centralizing this dispatch here
        /// keeps getText/WrapText/CalculatedSize ignorant of which font
        /// representation is actually behind it.</summary>
        private Func<string, Vector2> MakeMeasure(TVFont fontValue)
        {
            if (Managers.FontManager.UseSdf && fontValue.Border <= 0)
            {
                var sdfFont = Resources.StaticResources.FontManager.LoadSdfFont(fontValue.Family);
                float size = fontValue.Size;
                return s => sdfFont.MeasureString(s, size);
            }

            var font = GetFont(fontValue.Family, fontValue.Size);
            return s => font.SpriteFont.MeasureString(s) * font.DrawScale;
        }

        private string getText()
        {
            TVFont fontValue = fontTrait.Value();
            string fontName = fontValue.Family;
            float fontSize = fontValue.Size;
            string text = textTrait.Value().Text;
            float wrapWidth = WordWrap && CachedSizeTrait != null ? CachedSizeTrait.Value().X : float.MinValue;

            if (wrapCacheResult != null &&
                wrapCacheText == text &&
                wrapCacheFamily == fontName &&
                wrapCacheFontSize == fontSize &&
                wrapCacheWidth == wrapWidth)
            {
                return wrapCacheResult;
            }

            Func<string, Vector2> measure = MakeMeasure(fontValue);

            if (!WordWrap)
            {
                string single = text ?? "";
                wrapCacheResult = single;
                wrapCacheText = text;
                wrapCacheFamily = fontName;
                wrapCacheFontSize = fontSize;
                wrapCacheWidth = wrapWidth;
                wrapCacheTotalSize = measure(single);
                wrapCacheLines = new[] { single };
                wrapCacheLineSizes = new[] { wrapCacheTotalSize };
                return single;
            }

            string newText = WrapText(text, s => measure(s).X, wrapWidth);

            wrapCacheResult = newText;
            wrapCacheText = text;
            wrapCacheFamily = fontName;
            wrapCacheFontSize = fontSize;
            wrapCacheWidth = wrapWidth;
            wrapCacheTotalSize = measure(newText);
            wrapCacheLines = newText.Split('\n');
            wrapCacheLineSizes = new Vector2[wrapCacheLines.Length];
            for (int i = 0; i < wrapCacheLines.Length; i++)
            {
                // An EMPTY line (a deliberate blank line between paragraphs)
                // measures (0,0), which would collapse the gap it exists to
                // create — measure a space instead so it advances one line.
                string line = wrapCacheLines[i];
                wrapCacheLineSizes[i] = measure(line.Length == 0 ? " " : line);
            }

            return newText;
        }

        /// <summary>
        /// Greedy word wrap: breaks a paragraph onto as many words as fit
        /// <paramref name="wrapWidth"/>, and honours explicit newlines in the
        /// source as HARD breaks (so multi-paragraph body text lays out the
        /// way it was written).
        ///
        /// The measurement is per CURRENT LINE, not per accumulated string.
        /// That distinction is the whole fix: the previous implementation
        /// measured <c>everythingSoFar + word</c> every iteration and compared
        /// it against the wrap width, so as soon as the text contained a
        /// newline — inserted by an earlier break, or written by the caller —
        /// the comparison was against a string spanning several lines and
        /// every subsequent word "overflowed", collapsing the rest of the
        /// paragraph into one word per line. It also depended on
        /// <c>MeasureString</c> reporting a multi-line string's width as its
        /// widest line, which is not something this wrap should rely on either
        /// way.
        /// </summary>
        private static string WrapText(string text, Func<string, float> measureWidth, float wrapWidth)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? "";
            }

            if (wrapWidth <= 0f)
            {
                return text;
            }

            var wrapped = new StringBuilder(text.Length + 16);
            string[] paragraphs = text.Replace("\r", "").Split('\n');
            for (int p = 0; p < paragraphs.Length; p++)
            {
                if (p > 0)
                {
                    wrapped.Append('\n');
                }

                string line = "";
                foreach (string word in paragraphs[p].Split(' '))
                {
                    if (word.Length == 0)
                    {
                        continue; // collapse runs of spaces rather than wrapping on them
                    }

                    string candidate = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0
                        && measureWidth(candidate) > wrapWidth)
                    {
                        wrapped.Append(line).Append('\n');
                        line = word; // a single word wider than the box still gets its own line
                    }
                    else
                    {
                        line = candidate;
                    }
                }

                wrapped.Append(line);
            }

            return wrapped.ToString();
        }
        public override void Draw()
        {
            TVFont fontValue = fontTrait.Value();
            if (fontValue.Family != null)
            {
                string text = getText();
                int border = fontValue.Border;
                Color foreground = foregroundTrait.Value().AsXna;

                // Border/outline text (SequencerView's legacy block-label
                // style) stays on the bitmap path — SpriteBatchExtensions.
                // DrawString's border is N offset copies of the glyph atlas,
                // a technique the SDF path doesn't implement yet.
                bool useSdf = Managers.FontManager.UseSdf && border <= 0;
                Managers.FontManager.KeyedSpriteFont font = useSdf ? null : GetFont(fontValue.Family, fontValue.Size);
                Managers.SdfFont sdfFont = useSdf ? Resources.StaticResources.FontManager.LoadSdfFont(fontValue.Family) : null;
                bool fontReady = useSdf ? Ensure.NotNull(sdfFont, nameof(sdfFont)) : Ensure.NotNull(font, nameof(font));

                if (fontReady &&
                Ensure.NotNull(text, nameof(text)))
                {

                    // Measured sizes and split lines come from the wrap cache
                    // (populated by getText above) - no re-measuring per frame.
                    Vector2 thisSize = wrapCacheTotalSize;
                    Vector2 actualPosition = this.GetActualXnaPosition(thisSize, true);
                    var lines = wrapCacheLines;
                    var p = actualPosition;
                    var pr = actualPosition;
                    var thisGetSize = this.GetSize();
                    bool aligned = CachedSizeTrait != null && HasTrait<HorizontalAlignmentTrait>();
                    HorizontalAlignment? horizAlign = aligned && HasTrait<VerticalAlignmentTrait>()
                        ? ElementTrait<HorizontalAlignmentTrait>().Value().Alignment
                        : (HorizontalAlignment?)null;
                    var ySize = 0;
                    for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                    {
                        var line = lines[lineIndex];
                        var lineSize = wrapCacheLineSizes[lineIndex];
                        Vector2 offsetVector = Vector2.Zero;

                        switch (horizAlign)
                        {
                            case HorizontalAlignment.Center:
                                var cent = thisGetSize.X / 2f;
                                var halfLineSize = lineSize.X / 2f;
                                var offset = cent - halfLineSize;
                                offsetVector = new Vector2(offset, 0);
                                break;

                            case HorizontalAlignment.Right:
                                offsetVector = new Vector2(thisGetSize.X - lineSize.X, 0);
                                break;
                        }

                        if (useSdf)
                        {
                            Resources.StaticResources.DrawManager.DrawSdfString(
                                sdfFont, line, p + offsetVector, fontValue.Size, foreground);
                        }
                        else
                        {
                            Resources.StaticResources.DrawManager.DrawString(
                            font,
                            line,
                                p+offsetVector,
                                foreground,
                                border,
                                fontValue.BorderColor);
                        }
                        p.Y += lineSize.Y;
                        p.X = pr.X;
                        ySize = ySize+  (int)lineSize.Y;
                    }

                    // Only rewrite the size when the measured height actually
                    // changed: Set fires trait-changed events and invalidates
                    // downstream consumers on every call.
                    TVVector currentSize = CachedSizeTrait.Value();
                    if ((int)currentSize.Y != ySize)
                    {
                        this.Set<SizeTrait>(new TVVector(currentSize.X, ySize));
                    }
                }
            }


            base.Draw();
        }

        public Vector2 CalculatedSize()
        {
            TVFont fontValue = fontTrait.Value();
            string text = getText();
            Vector2 size = MakeMeasure(fontValue)(text);

            return new Vector2(ElementTrait<SizeTrait>().Value().X, size.Y);
        }
    }
}
