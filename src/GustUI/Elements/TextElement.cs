﻿using GustUI.Attributes;
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

        private string getText()
        {
            string fontName = this.ElementTrait<FontTrait>().Value().Family;
            float fontSize = this.ElementTrait<FontTrait>().Value().Size;
            string text = this.ElementTrait<TextTrait>().Value().Text;
            
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

                    Vector2 thisSize = font.SpriteFont.MeasureString(text) * GustConstants.FontScale;
                    TVVector actualPosition = this.GetActualPosition(thisSize, true);
                    var lines = text.Split('\n');
                    var p = actualPosition.AsXna;
                    var pr = actualPosition.AsXna;
                    var thisGetSize = this.GetSize();
                    var ySize = 0;
                    foreach (var line in lines)
                    {
                        var lineSize = font.SpriteFont.MeasureString(line) * GustConstants.FontScale;
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
