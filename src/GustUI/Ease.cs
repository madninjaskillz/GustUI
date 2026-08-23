using System;
using Microsoft.Xna.Framework;

namespace GustUI
{
    /// <summary>
    /// Shared easing math for discrete state-change transitions
    /// (design-guide.md §5, locked 2026-08-13, ezmuze-studio's normative UI
    /// spec) — a frame-rate-independent exponential ease toward a target,
    /// reused everywhere a control snaps between discrete visual states
    /// (<see cref="Elements.ToggleSwitchElement"/>'s flip, button hover/
    /// press, ezmuze-studio's mute/solo buttons) instead of each call site
    /// duplicating the same formula.
    /// </summary>
    public static class Ease
    {
        /// <summary>
        /// Eases <paramref name="current"/> toward <paramref name="target"/>,
        /// reaching ~98% of the way there after <paramref name="durationSeconds"/>
        /// has elapsed, independent of frame rate — always eases from
        /// wherever <paramref name="current"/> ACTUALLY is, so re-triggering
        /// mid-transition (a fast double-click, a rapid hover-in-hover-out)
        /// reverses smoothly instead of snapping to a stale start point, and
        /// needs no separate start-time bookkeeping the way a fixed-duration
        /// tween would.
        /// </summary>
        public static float Toward(float current, float target, float dt, float durationSeconds = 0.15f)
        {
            if (dt <= 0f)
            {
                return current;
            }

            float k = 1f - (float)Math.Pow(0.02, dt / durationSeconds);
            return MathHelper.Lerp(current, target, MathHelper.Clamp(k, 0f, 1f));
        }

        // ---- timeline curves (2026-08-23) ----
        //
        // Toward() above is for a control easing to WHEREVER its target moved
        // to; it has no beginning and no end. A timed animation is the other
        // shape entirely - it starts at a known moment, runs for a known
        // length, and its curve is a function of how far through it is. These
        // are that: pure 0..1 -> 0..1 curves, plus the clock arithmetic that
        // turns "seconds since the animation started" into a phase.

        /// <summary>Decelerating: fast off the mark, settling into the end.
        /// The default for something ARRIVING (design-guide.md §5).</summary>
        public static float OutCubic(float t) => 1f - (float)Math.Pow(1f - Clamp01(t), 3);

        /// <summary>Accelerating: the mirror of <see cref="OutCubic"/>, for
        /// something LEAVING.</summary>
        public static float InCubic(float t)
        {
            t = Clamp01(t);
            return t * t * t;
        }

        /// <summary>Slow at both ends, quick through the middle - a move that
        /// begins and ends at rest.</summary>
        public static float InOutCubic(float t)
        {
            t = Clamp01(t);
            return t < 0.5f
                ? 4f * t * t * t
                : 1f - (float)Math.Pow(-2f * t + 2f, 3) / 2f;
        }

        /// <summary>Overshoots past 1 and comes back - a little bounce of
        /// arrival. <paramref name="overshoot"/> 1.70158 is the conventional
        /// "10% past" figure.</summary>
        public static float OutBack(float t, float overshoot = 1.70158f)
        {
            t = Clamp01(t) - 1f;
            return 1f + (overshoot + 1f) * t * t * t + overshoot * t * t;
        }

        /// <summary>
        /// How far through a timed phase <paramref name="now"/> is: 0 before
        /// it starts, 1 after it ends, linear between.
        ///
        /// This is what lets an animation be written as a list of overlapping
        /// phases against ONE clock - "the fill runs from 0.35s to 0.7s" - so
        /// each part's timing is stated where it is used and can be read off
        /// without tracing state through a frame counter.
        /// </summary>
        public static float Phase(float now, float startSeconds, float endSeconds)
        {
            if (endSeconds <= startSeconds)
            {
                return now >= endSeconds ? 1f : 0f;
            }

            return Clamp01((now - startSeconds) / (endSeconds - startSeconds));
        }

        private static float Clamp01(float t) => t < 0f ? 0f : t > 1f ? 1f : t;
    }
}
