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
using System.Text;
using System.Threading.Tasks;

namespace GustUI.Managers
{
    public class FontManager
    {
        GraphicsDevice graphicsDevice;
        VirtualContent content;
        public FontManager(GraphicsDevice graphicsDevice, VirtualContent content)
        {
            this.graphicsDevice = graphicsDevice;
            this.content = content; 
        }

        private readonly Dictionary<string, SpriteFont> FontCache = new();

        public SpriteFont LoadFont(string path, float size)
        {
            var key = $"{path}_{size}";

            if (FontCache.TryGetValue(key, out var cachedFont))
            {
                return cachedFont;
            }

            var bake = TtfFontBaker.Bake(content.ReadAllBytes(path), size / GustConstants.FontScale, 1024, 1024, new[] { CharacterRange.BasicLatin, new CharacterRange((char)Enum.GetValues(typeof(UIFont.Symbol)).Cast<UIFont.Symbol>().Min(), (char)Enum.GetValues(typeof(UIFont.Symbol)).Cast<UIFont.Symbol>().Max()) });

            var font = bake.CreateSpriteFont(graphicsDevice);

            FontCache.Add(key, font);

            return font;
        }
    }
}
