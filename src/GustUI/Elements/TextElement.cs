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

        private string getText()
        {
            string fontName = this.ElementTrait<FontTrait>().Value().Family;
            float fontSize = this.ElementTrait<FontTrait>().Value().Size;
            string text = this.ElementTrait<TextTrait>().Value().Text;
            float wrapWidth = this.HasTrait<SizeTrait>() ? this.ElementTrait<SizeTrait>().Value().X : float.MinValue;

            if (wrapCacheResult != null &&
                wrapCacheText == text &&
                wrapCacheFamily == fontName &&
                wrapCacheFontSize == fontSize &&
                wrapCacheWidth == wrapWidth)
            {
                return wrapCacheResult;
            }

            var font = Resources.StaticResources.FontManager.LoadFont(fontName, fontSize);

            var words = text !=null ? text.Split(' ') : Array.Empty<string>();
            string newText = "";
            if (this.HasTrait<SizeTrait>() && words.Length > 0)
            {
                foreach (var word in words)
                {
                    if ((font.SpriteFont.MeasureString(newText + word) * GustConstants.FontScale).X > this.ElementTrait<SizeTrait>().Value().X)
                    {
                        newText += "\n" + word + " ";
                    }
                    else
                    {
                        newText += word;
                        if (word != words.Last())
                        {
                            newText += " ";
                        }
                    }
                }
            }

            wrapCacheResult = newText;
            wrapCacheText = text;
            wrapCacheFamily = fontName;
            wrapCacheFontSize = fontSize;
            wrapCacheWidth = wrapWidth;
            wrapCacheTotalSize = font.SpriteFont.MeasureString(newText) * GustConstants.FontScale;
            wrapCacheLines = newText.Split('\n');
            wrapCacheLineSizes = new Vector2[wrapCacheLines.Length];
            for (int i = 0; i < wrapCacheLines.Length; i++)
            {
                wrapCacheLineSizes[i] = font.SpriteFont.MeasureString(wrapCacheLines[i]) * GustConstants.FontScale;
            }

            return newText;
        }
        public override void Draw()
        {
            if (this.ElementTrait<FontTrait>().Value().Family != null)
            {

                string fontName = this.ElementTrait<FontTrait>().Value().Family;
                float fontSize = this.ElementTrait<FontTrait>().Value().Size;
                string text = getText();
                int border = this.ElementTrait<FontTrait>().Value().Border;
                Color foreground = this.ElementTrait<ForegroundColorTrait>().Value().AsXna;
                Managers.FontManager.KeyedSpriteFont font = Resources.StaticResources.FontManager.LoadFont(fontName, fontSize);

                if (Ensure.NotNull(font, nameof(font)) &&
                Ensure.NotNull(text, nameof(text)) &&
                Ensure.NotNull(foreground, nameof(foreground)) &&
                Ensure.NotNull(border, nameof(border)))
                {

                    // Measured sizes and split lines come from the wrap cache
                    // (populated by getText above) - no re-measuring per frame.
                    Vector2 thisSize = wrapCacheTotalSize;
                    Vector2 actualPosition = this.GetActualXnaPosition(thisSize, true);
                    var lines = wrapCacheLines;
                    var p = actualPosition;
                    var pr = actualPosition;
                    var thisGetSize = this.GetSize();
                    var ySize = 0;
                    for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                    {
                        var line = lines[lineIndex];
                        var lineSize = wrapCacheLineSizes[lineIndex];
                        Vector2 offsetVector = Vector2.Zero;

                        if (HasTrait<SizeTrait>() && HasTrait<HorizontalAlignmentTrait>())
                        {
                           
                           
                            HorizontalAlignment? horizAlign = HasTrait<VerticalAlignmentTrait>() ? ElementTrait<HorizontalAlignmentTrait>().Value().Alignment : null;
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
                        }
                        Resources.StaticResources.DrawManager.DrawString(
                        font,
                        line,
                            p+offsetVector,
                            foreground,
                            border);
                        p.Y += lineSize.Y;
                        p.X = pr.X;
                        ySize = ySize+  (int)lineSize.Y;
                    }
                    this.Set<SizeTrait>(new TVVector(this.GetSize().X, ySize));
                }
            }            

            
            base.Draw();
        }

        public Vector2 CalculatedSize()
        {
            string fontName = this.ElementTrait<FontTrait>().Value().Family;
            float fontSize = this.ElementTrait<FontTrait>().Value().Size;
            string text = getText();
            var font = Resources.StaticResources.FontManager.LoadFont(fontName, fontSize);

            return new Vector2(ElementTrait<SizeTrait>().Value().X, (font.SpriteFont.MeasureString(text) * GustConstants.FontScale).Y);
        }
    }
}
