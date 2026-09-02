using System;
using System.Collections.Generic;
using System.Diagnostics;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Managers;
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

        /// <summary>
        /// How the pill and thumb paint. The slide animation, the drag and the
        /// click behaviour are identical across skins -- see
        /// <see cref="ControlSkin"/>.
        /// </summary>
        public ControlSkin Skin { get; set; } = ControlSkin.Flat;

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

        // ---- flip animation (design-guide.md §5: ~100-150ms chrome easing,
        // same idiom ModalWindowElement's open/close transition uses) — a
        // plain click or a drag-release snap now SLIDES the thumb and LERPS
        // the track color instead of jumping instantly. Frame-rate-
        // independent exponential ease toward the target rather than a
        // fixed-duration tween: it always eases from wherever the thumb
        // CURRENTLY is, so re-toggling mid-animation (a fast double-click)
        // reverses smoothly instead of snapping to a stale start point —
        // no separate from/start-time bookkeeping needed. Suspended entirely
        // while a drag is actively tracking the pointer (t = liveT then,
        // unchanged) — easing a drag would read as laggy/rubber-banded,
        // exactly the opposite of what a direct-manipulation drag should
        // feel like.
        private readonly Stopwatch animClock = Stopwatch.StartNew();
        private double lastDrawSeconds = -1;
        private float displayedT;
        private bool displayedTInitialized;
        private const float TransitionSeconds = 0.15f;

        public ToggleSwitchElement()
        {
            // Default on/off colors track the live theme (design-guide.md §1)
            // — call sites that want a specific accent (e.g. Stack's
            // ControlAccent) still override these after construction.
            OnColor = Resources.StaticResources.Theme.AccentSelection;
            OffColor = Resources.StaticResources.Theme.SurfaceBorder;

            ElementTrait<OnMousePress>().Set(new TVEvent<ClickEventArgs>(args =>
            {
                pressActive = true;
                pressX = args.MouseState.X;
                pressY = args.MouseState.Y;
                maxDelta = 0f;
                // Grab wherever the thumb is VISUALLY right now (mid-flip-
                // animation, most of the time) rather than snapping it to
                // the discrete committed value first.
                liveT = displayedTInitialized ? displayedT : (value ? 1f : 0f);
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

            double now = animClock.Elapsed.TotalSeconds;
            float dt = lastDrawSeconds < 0 ? 0f : (float)Math.Min(now - lastDrawSeconds, 0.25);
            lastDrawSeconds = now;

            if (!displayedTInitialized)
            {
                displayedT = value ? 1f : 0f;
                displayedTInitialized = true;
            }

            float t;
            if (pressActive && maxDelta > ClickThresholdPixels)
            {
                // Actively dragging: follow the pointer 1:1, no ease — keep
                // displayedT in sync so a release resumes easing from here,
                // not from a stale pre-drag point.
                t = liveT;
                displayedT = liveT;
            }
            else
            {
                displayedT = Ease.Toward(displayedT, value ? 1f : 0f, dt, TransitionSeconds);
                t = displayedT;
            }

            int w = (int)size.X;
            int h = (int)size.Y;
            if (w >= 6 && h >= 4)
            {
                Color trackColor = Color.Lerp(OffColor, OnColor, t);

                var dest = new Rectangle((int)pos.X, (int)pos.Y, w, h);

                int diameter = Math.Max(2, h - 4);
                float travel = Math.Max(0, w - diameter - 4);
                float thumbX = pos.X + 2 + t * travel;
                float thumbY = pos.Y + (h - diameter) / 2f;
                Vector2 thumbCenter = new Vector2(thumbX + diameter / 2f, thumbY + diameter / 2f);
                float thumbRadius = diameter / 2f;

                switch (Skin)
                {
                    case ControlSkin.Soft:
                        // Recessed pill, raised thumb -- the same inversion the
                        // soft slider uses, so a panel mixing the two reads as
                        // one material rather than two.
                        manager.DrawFilledCapsule(new Rectangle(
                            dest.X, dest.Y - 1, dest.Width, dest.Height + 2), Color.Black * 0.16f);
                        manager.DrawFilledCapsule(dest, trackColor);
                        manager.DrawSoftShadowCircle(
                            thumbCenter + new Vector2(thumbRadius * 0.25f, thumbRadius * 0.25f),
                            thumbRadius, Color.Black * 0.30f, thumbRadius * 0.45f);
                        manager.DrawRadialShadedCircle(thumbCenter, thumbRadius,
                            Color.Lerp(ThumbColor, Color.White, 0.22f),
                            Color.Lerp(ThumbColor, Color.Black, 0.10f));
                        break;

                    case ControlSkin.Hardware:
                        // A rocker in a dark surround, with the lit side of the
                        // pill glowing rather than the whole track changing
                        // colour: on a rack panel the LED is the state, and the
                        // switch body stays the same dark plastic either way.
                        manager.DrawFilledCapsule(dest, Color.Lerp(OffColor, Color.Black, 0.5f));
                        int litWidth = Math.Max(2, (int)(dest.Width * (0.25f + 0.55f * t)));
                        manager.DrawFilledCapsule(
                            new Rectangle(dest.X + 2, dest.Y + 2, litWidth, Math.Max(1, dest.Height - 4)),
                            Color.Lerp(OffColor, OnColor, t));
                        manager.DrawFilledCircle(thumbCenter, thumbRadius,
                            Color.Lerp(ThumbColor, Color.Black, 0.55f));
                        manager.DrawRadialShadedCircle(thumbCenter, thumbRadius * 0.80f,
                            Color.Lerp(ThumbColor, Color.White, 0.28f),
                            Color.Lerp(ThumbColor, Color.Black, 0.20f));
                        break;

                    case ControlSkin.Amp:
                        // A rocker: a plain dark surround with a hard block
                        // that sits on one side or the other. No colour change
                        // on the body, because an amp's switch is read by where
                        // the rocker is, not by what shade it went.
                        manager.DrawFilledRectangle(dest, Color.Lerp(OffColor, Color.Black, 0.35f));
                        manager.DrawFilledRectangle(
                            new Rectangle((int)(thumbCenter.X - thumbRadius), dest.Y + 1,
                                (int)(thumbRadius * 2), Math.Max(1, dest.Height - 2)),
                            Color.Lerp(ThumbColor, Color.Black, t > 0.5f ? 0.1f : 0.45f));
                        break;

                    case ControlSkin.Neon:
                        // Outline pill, unlit; the lit half glows.
                        manager.DrawFilledCapsule(dest, OffColor);
                        for (int i = 2; i >= 0; i--)
                        {
                            manager.DrawFilledCapsule(
                                new Rectangle(dest.X + 2 - i, dest.Y + 2 - i,
                                    Math.Max(2, (int)((dest.Width - 4) * (0.3f + 0.7f * t)) + i * 2),
                                    Math.Max(1, dest.Height - 4 + i * 2)),
                                Color.Lerp(OffColor, OnColor, t) * (i == 0 ? 1f : 0.2f / i));
                        }

                        manager.DrawFilledCircle(thumbCenter, thumbRadius * 0.7f, ThumbColor);
                        break;

                    case ControlSkin.Pixel:
                        manager.DrawFilledRectangle(
                            new Rectangle(dest.X - 1, dest.Y - 1, dest.Width + 2, dest.Height + 2),
                            Color.Lerp(OffColor, Color.Black, 0.6f));
                        manager.DrawFilledRectangle(dest, trackColor);
                        int side = Math.Max(3, dest.Height - 4);
                        var block = new Rectangle(
                            (int)(thumbCenter.X - side / 2f), (int)(thumbCenter.Y - side / 2f), side, side);
                        manager.DrawFilledRectangle(block, Color.Lerp(ThumbColor, Color.Black, 0.65f));
                        manager.DrawFilledRectangle(
                            new Rectangle(block.X, block.Y, block.Width - 2, block.Height - 2), ThumbColor);
                        break;

                    default:
                        manager.DrawFilledCapsule(dest, trackColor);
                        manager.DrawFilledCircle(thumbCenter, thumbRadius, ThumbColor);
                        break;
                }
            }

            base.Draw();
        }
    }
}
