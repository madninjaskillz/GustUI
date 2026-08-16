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
    }
}
