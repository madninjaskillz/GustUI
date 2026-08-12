using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace GustUI.Managers
{
    /// <summary>A packed region inside a TextureAtlas: pixel rect + the atlas texture itself.</summary>
    public struct AtlasRegion
    {
        public Texture2D Texture;
        public Rectangle Pixels;

        public AtlasRegion(Texture2D texture, Rectangle pixels)
        {
            Texture = texture;
            Pixels = pixels;
        }
    }

    /// <summary>
    /// Shared texture atlas for the geometry renderer's small, CPU-rasterized
    /// alpha-mask shapes — rounded-rect corner discs, knob dial/ring/live-dot,
    /// glow-curve gradient/live-dot, toggle pill, and the single reserved
    /// white texel every flat-color primitive samples. Before this, each of
    /// those was its own standalone Texture2D (three independently-duplicated
    /// "static Dictionary&lt;int,Texture2D&gt;, bake once, tint at draw time"
    /// caches, per GlowCurveElement's own doc comment on the duplication being
    /// "meant to be copy-and-adapt, not centralized") — meaning SpriteBatch's
    /// batching broke on every texture swap between them. Packing them into
    /// one atlas lets a panel's background + border + rounded corners + knobs
    /// + toggle pills land in a single GeometryBatch segment.
    ///
    /// Uses a simple shelf packer (rows of a fixed height, packed
    /// left-to-right, a new shelf started when a row runs out of width) —
    /// fine for this atlas's actual content (small, roughly-similar-height
    /// baked shapes, not arbitrary large images; those stay standalone, see
    /// the class-level migration plan). Grows through a fixed tier list
    /// (512 -> 1024 -> 2048) by rebuilding into a bigger texture and
    /// re-uploading every previously-packed region's pixels (rare — happens
    /// only the first time a session's shape variety exceeds the current
    /// tier, not per frame).
    /// </summary>
    public class TextureAtlas
    {
        private static readonly int[] SizeTiers = { 512, 1024, 2048 };

        private readonly GraphicsDevice device;
        private readonly Dictionary<string, AtlasRegion> regions = new Dictionary<string, AtlasRegion>();
        private readonly Dictionary<string, Color[]> pixelsByKey = new Dictionary<string, Color[]>();

        private Texture2D texture;
        private int tierIndex;
        private int shelfY;
        private int shelfHeight;
        private int cursorX;

        private const int Padding = 1;
        private const string WhiteKey = "__white__";

        public TextureAtlas(GraphicsDevice device)
        {
            this.device = device;
            GrowTo(0);
            Reserve(WhiteKey, 1, 1, pixels => pixels[0] = Color.White);
        }

        /// <summary>The atlas's own reserved fully-opaque white texel — the shared home for what Resources.Pixel and the per-element private "pixel" fields (WaveformElement, KnobElement) each currently duplicate.</summary>
        public AtlasRegion WhiteRegion => regions[WhiteKey];

        public Texture2D Texture => texture;

        /// <summary>
        /// Returns the packed region for <paramref name="key"/>, baking it
        /// via <paramref name="rasterize"/> (writes into a row-major
        /// width*height Color[] buffer) on first request and caching
        /// thereafter — same "bake once, reuse forever" contract the
        /// per-element caches (KnobElement.GetDialTexture etc.) had, just
        /// centralized and atlas-packed instead of each owning a standalone
        /// Texture2D.
        /// </summary>
        public AtlasRegion GetOrBake(string key, int width, int height, Action<Color[]> rasterize)
        {
            if (regions.TryGetValue(key, out AtlasRegion existing))
            {
                return existing;
            }

            return Reserve(key, width, height, rasterize);
        }

        private AtlasRegion Reserve(string key, int width, int height, Action<Color[]> rasterize)
        {
            var pixels = new Color[width * height];
            rasterize(pixels);

            Rectangle rect = Pack(width, height);
            texture.SetData(0, rect, pixels, 0, pixels.Length);

            pixelsByKey[key] = pixels;
            var region = new AtlasRegion(texture, rect);
            regions[key] = region;
            return region;
        }

        private Rectangle Pack(int width, int height)
        {
            int padded_w = width + (Padding * 2);
            int padded_h = height + (Padding * 2);

            if (cursorX + padded_w > texture.Width)
            {
                cursorX = 0;
                shelfY += shelfHeight;
                shelfHeight = 0;
            }

            if (shelfY + padded_h > texture.Height || padded_w > texture.Width)
            {
                Grow();
                return Pack(width, height);
            }

            var rect = new Rectangle(cursorX + Padding, shelfY + Padding, width, height);
            cursorX += padded_w;
            shelfHeight = Math.Max(shelfHeight, padded_h);
            return rect;
        }

        private void Grow()
        {
            if (tierIndex + 1 >= SizeTiers.Length)
            {
                throw new InvalidOperationException($"TextureAtlas exhausted its largest tier ({SizeTiers[tierIndex]}px) — too many distinct baked shapes for a shelf packer at this size.");
            }

            GrowTo(tierIndex + 1);

            // Re-pack every previously-baked region into the new, bigger
            // texture from scratch (fresh shelf cursor) — simplest correct
            // approach for a rare, non-per-frame event; no attempt to
            // preserve old regions' exact rects since every AtlasRegion the
            // caller is holding gets refreshed on its next GetOrBake call
            // anyway (this method returns void, callers always re-fetch).
            var toRepack = new List<(string key, int w, int h, Color[] pixels)>();
            foreach (var kvp in regions)
            {
                var r = kvp.Value.Pixels;
                toRepack.Add((kvp.Key, r.Width, r.Height, pixelsByKey[kvp.Key]));
            }

            regions.Clear();
            cursorX = 0;
            shelfY = 0;
            shelfHeight = 0;

            foreach (var (key, w, h, pixels) in toRepack)
            {
                Rectangle rect = Pack(w, h);
                texture.SetData(0, rect, pixels, 0, pixels.Length);
                regions[key] = new AtlasRegion(texture, rect);
            }
        }

        private void GrowTo(int tier)
        {
            tierIndex = tier;
            int size = SizeTiers[tierIndex];
            texture?.Dispose();
            texture = new Texture2D(device, size, size);

            // Fully-transparent backing so unpacked atlas area never bleeds
            // stray opaque texels into a neighboring region's bilinear
            // sample at a packed rect's edge.
            var clear = new Color[size * size];
            texture.SetData(clear);

            cursorX = 0;
            shelfY = 0;
            shelfHeight = 0;
        }
    }
}
