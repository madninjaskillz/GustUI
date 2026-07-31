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
                    spriteBatch.DrawString(font, r.Text, Vector2.Zero, r.color);
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
