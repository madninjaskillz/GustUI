using Microsoft.Xna.Framework.Graphics;
using SpriteFontPlus;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Managers
{
    public class FontManager
    {
        GraphicsDevice graphicsDevice;
        public FontManager(GraphicsDevice graphicsDevice)
        {
            this.graphicsDevice = graphicsDevice;
        }

        private readonly Dictionary<string, SpriteFont> FontCache = new();

        public SpriteFont LoadFont(string path, float size)
        {
            var key = $"{path}_{size}";

            if (FontCache.TryGetValue(key, out var cachedFont))
            {
                return cachedFont;
            }

            var font = TtfFontBaker.Bake(File.ReadAllBytes(path), size / GustConstants.FontScale, 1024, 1024, new[] { CharacterRange.BasicLatin }).CreateSpriteFont(graphicsDevice);

            FontCache.Add(key, font);

            return font;
        }
    }
}
