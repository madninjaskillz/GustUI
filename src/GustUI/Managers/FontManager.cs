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
        internal struct FontCacheKey
        {
            internal string FontKey;
            internal string Text;
            internal Color color;
        }

        internal class FontCacheValue
        {
            internal Texture2D Texture2D;
            internal DateTime LastUsed;
        }

        public Texture2D GetCachedText(string fontKey, string text, Color color)
        {
            FontCacheKey key = new FontCacheKey
            {
                FontKey = fontKey,
                Text = text,
                color = color
            };

            if (FontWriteCache.ContainsKey(key))
            {
                FontWriteCache[key].LastUsed = DateTime.Now;
                return FontWriteCache[key].Texture2D;
            }

            if (FontRequestCount.ContainsKey(key))
            {
                FontRequestCount[key]++;
            }
            else
            {
                FontRequestCount.Add(key, 0);
            }

            return null;
        }

        internal string CacheInfo => $"Fonts Cached: {FontCache.Count}, TRC: {FontRequestCount.Count}, TIC: {FontWriteCache.Count}";

        private DateTime lastClean = DateTime.Now;
        internal void ManageCaches()
        {
            var expired = FontWriteCache.Where(x => DateTime.Now - x.Value.LastUsed > TimeSpan.FromSeconds(10));
            foreach (var e in expired)
            {
                FontWriteCache.Remove(e.Key);
                FontRequestCount.Remove(e.Key);
            }

            if (DateTime.Now - lastClean > TimeSpan.FromSeconds(10))
            {
                lastClean = DateTime.Now;
                var lowrRequests = FontRequestCount.Where(x => x.Value < 50).Select(x => x.Key).ToList();
                foreach (var r in lowrRequests)
                {
                    FontRequestCount.Remove(r);
                }
            }

            var required = FontRequestCount.Where(x => x.Value > 50 && !FontWriteCache.ContainsKey(x.Key));

            foreach (var r in required)
            {
                var font = LoadFont(r.Key.FontKey);
                if (font != null)
                {
                    var size = font.MeasureString(r.Key.Text);
                    RenderTarget2D rt = new RenderTarget2D(graphicsDevice, (int)(size.X) + 2, (int)(size.Y) + 2);
                    graphicsDevice.SetRenderTarget(rt);
                    graphicsDevice.Clear(Color.Transparent);
                    spriteBatch.Begin(SpriteSortMode.Deferred);
                    spriteBatch.DrawString(font, r.Key.Text, Vector2.Zero, r.Key.color);
                    spriteBatch.End();
                    FontWriteCache.Add(r.Key, new FontCacheValue
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
        public KeyedSpriteFont LoadFont(string path, float size)
        {
            var key = $"{path}_{size}";

            if (FontCache.TryGetValue(key, out var cachedFont))
            {
                return cachedFont;
            }

            var bake = TtfFontBaker.Bake(content.ReadAllBytes(path), size / GustConstants.FontScale, 1024, 1024, new[] { CharacterRange.BasicLatin, new CharacterRange((char)Enum.GetValues(typeof(UIFont.Symbol)).Cast<UIFont.Symbol>().Min(), (char)Enum.GetValues(typeof(UIFont.Symbol)).Cast<UIFont.Symbol>().Max()) });

            var font = bake.CreateSpriteFont(graphicsDevice);

            FontCache.Add(key, new KeyedSpriteFont { SpriteFont = font, Key = key });

            return new KeyedSpriteFont { SpriteFont = font, Key = key };
        }

        public class KeyedSpriteFont
        {
            public SpriteFont SpriteFont { get; set; }
            public string Key { get; set; }

            internal Vector2 MeasureString(string consoleText)
            {
                return SpriteFont.MeasureString(consoleText);
            }
        }
    }
}
