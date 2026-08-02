using System;
using System.Collections.Generic;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GustUI.Elements
{
    /// <summary>
    /// A Windows-8/Metro-style boolean toggle switch: a pill-shaped track with
    /// a sliding circular thumb (off = neutral track, thumb left; on = accent
    /// track, thumb right). Interaction follows <see cref="KnobElement"/>'s
    /// capture-drag idiom: press-and-drag the thumb across the track slides
    /// it live and commits whichever side it's nearer to on release; a plain
    /// click (press+release with negligible movement) just flips the value —
    /// so both "flick it like a switch" and "tap it like a checkbox" work.
    /// Sized freely (a small instance reads as a checkbox, a wider one as the
    /// classic Win8 switch) — one element serves both jobs rather than having
    /// a separate checkbox primitive for the same on/off state.
    /// </summary>
    [ElementTraits(typeof(PositionTrait), typeof(SizeTrait), typeof(OnMousePress), typeof(OnMouseButtonHeldDown), typeof(OnMouseRelease))]
    public class ToggleSwitchElement : Element
    {
        /// <summary>Movement (press to current mouse position, Manhattan
        /// distance) beyond which a gesture counts as a drag rather than a
        /// click — below this, release always flips the value regardless of
        /// where the thumb visually ended up.</summary>
        public float ClickThresholdPixels { get; set; } = 4f;

        public Color OnColor { get; set; } = new Color(90, 170, 250);
        public Color OffColor { get; set; } = new Color(70, 70, 84);
        public Color ThumbColor { get; set; } = Color.White;

        public Action<bool> OnValueChanged;

        /// <summary>Raised once per completed gesture with the final value —
        /// the "commit" hook (undo-coalescing breaks, etc.), mirroring
        /// <see cref="KnobElement.OnDragCompleted"/>.</summary>
        public Action<bool> OnDragCompleted;

        private bool value;
        public bool Value
        {
            get => value;
            set
            {
                if (value != this.value)
                {
                    this.value = value;
                    OnValueChanged?.Invoke(value);
                }
            }
        }

        private bool pressActive;
        private float pressX;
        private float pressY;
        private float maxDelta;
        private float liveT; // 0 = off/left, 1 = on/right — live thumb position while dragging

        public ToggleSwitchElement()
        {
            ElementTrait<OnMousePress>().Set(new TVEvent<ClickEventArgs>(args =>
            {
                pressActive = true;
                pressX = args.MouseState.X;
                pressY = args.MouseState.Y;
                maxDelta = 0f;
                liveT = value ? 1f : 0f;
                CapturePointer();
            }));

            ElementTrait<OnMouseButtonHeldDown>().Set(new TVEvent<ClickEventArgs>(args =>
            {
                if (!pressActive)
                {
                    return;
                }

                float dx = args.MouseState.X - pressX;
                float dy = args.MouseState.Y - pressY;
                maxDelta = Math.Max(maxDelta, Math.Abs(dx) + Math.Abs(dy));

                Vector2 size = this.GetSize().AsXna;
                float localX = args.MouseState.X - this.GetActualXnaPosition().X;
                liveT = MathHelper.Clamp(localX / Math.Max(1f, size.X), 0f, 1f);
            }));

            ElementTrait<OnMouseRelease>().Set(new TVEvent<ClickEventArgs>(_ =>
            {
                if (pressActive)
                {
                    bool dragged = maxDelta > ClickThresholdPixels;
                    Value = dragged ? liveT >= 0.5f : !value;
                }

                pressActive = false;
                OnDragCompleted?.Invoke(value);
            }));
        }

        public override void Draw()
        {
            var manager = Resources.StaticResources.DrawManager;
            Vector2 pos = this.GetActualXnaPosition();
            Vector2 size = this.GetSize().AsXna;

            int w = (int)size.X;
            int h = (int)size.Y;
            if (w >= 6 && h >= 4)
            {
                float t = pressActive && maxDelta > ClickThresholdPixels ? liveT : (value ? 1f : 0f);
                Color trackColor = Color.Lerp(OffColor, OnColor, t);

                var dest = new Rectangle((int)pos.X, (int)pos.Y, w, h);
                manager.Draw(GetPillTexture(w, h), dest, trackColor);

                int diameter = Math.Max(2, h - 4);
                float travel = Math.Max(0, w - diameter - 4);
                float thumbX = pos.X + 2 + t * travel;
                float thumbY = pos.Y + (h - diameter) / 2f;
                var thumbDest = new Rectangle((int)thumbX, (int)thumbY, diameter, diameter);
                manager.Draw(GetThumbTexture(diameter), thumbDest, ThumbColor);
            }

            base.Draw();
        }

        private static readonly Dictionary<(int, int), Texture2D> PillCache = new Dictionary<(int, int), Texture2D>();
        private static readonly Dictionary<int, Texture2D> ThumbCache = new Dictionary<int, Texture2D>();

        /// <summary>Antialiased rounded-rect (radius = height/2, a true pill
        /// for the track), baked once per (width, height).</summary>
        private static Texture2D GetPillTexture(int w, int h)
        {
            if (PillCache.TryGetValue((w, h), out Texture2D cached))
            {
                return cached;
            }

            var data = new Color[w * h];
            float r = h / 2f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Distance to the nearest point of the pill's medial
                    // segment [r, r+w-2r] at height r — i.e. a capsule SDF.
                    float cx = MathHelper.Clamp(x + 0.5f, r, w - r);
                    float dx = x + 0.5f - cx;
                    float dy = y + 0.5f - r;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    float alpha = MathHelper.Clamp(r - dist, 0f, 1f);
                    data[y * w + x] = Color.White * alpha;
                }
            }

            var texture = new Texture2D(Resources.StaticResources.GraphicsDevice, w, h);
            texture.SetData(data);
            PillCache[(w, h)] = texture;
            return texture;
        }

        /// <summary>Antialiased filled disc — the sliding thumb, baked once per diameter.</summary>
        private static Texture2D GetThumbTexture(int diameter)
        {
            if (ThumbCache.TryGetValue(diameter, out Texture2D cached))
            {
                return cached;
            }

            var data = new Color[diameter * diameter];
            float r = diameter / 2f;
            for (int y = 0; y < diameter; y++)
            {
                for (int x = 0; x < diameter; x++)
                {
                    float dx = x - r + 0.5f;
                    float dy = y - r + 0.5f;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    float alpha = MathHelper.Clamp(r - dist, 0f, 1f);
                    data[y * diameter + x] = Color.White * alpha;
                }
            }

            var texture = new Texture2D(Resources.StaticResources.GraphicsDevice, diameter, diameter);
            texture.SetData(data);
            ThumbCache[diameter] = texture;
            return texture;
        }
    }
}
