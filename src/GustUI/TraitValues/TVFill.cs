using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.TraitValues
{
    public class TVFill : TraitValue
    {
        public Texture2D Texture { get; set; }
        public float Opacity { get; set; } = 1f;
    }

    public class TVSmartFill : TVFill
    {
        public ButtonStates States { get; set; }

        // ---- hover/press ease (design-guide.md §5, locked 2026-08-13: the
        // ~150ms discrete-state-transition treatment, previously only
        // implemented for ToggleSwitchElement's flip) — three independent
        // weights (one per state) rather than a single 0..1 scalar, so ANY
        // transition path (Normal->Hovered->Pressed, or a fast click that
        // skips straight Normal->Pressed) blends smoothly with no special-
        // casing. Each TVSmartFill instance is already per-element (every
        // real call site does `new TVSmartFill{States=...}` per button, even
        // though the underlying ButtonStates/fills are often shared theme
        // singletons — see design-guide.md's own §1.1 button spec) so owning
        // this mutable animation state directly on the instance is safe: two
        // buttons sharing the same ButtonStates never share the same
        // TVSmartFill wrapper, so never fight over the same weights.
        private float weightNormal = 1f;
        private float weightHovered;
        private float weightPressed;
        private double lastSeconds = -1;
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>
        /// How long the crossfade takes, in seconds. Defaults to the
        /// design-guide's ~150ms discrete-state transition; menus set it
        /// shorter (2026-09-06) because a menu row is a thing you sweep past
        /// on the way to another one, and a transition tuned for a button you
        /// arrive at reads as lag when six of them light up in a row.
        /// </summary>
        public float FadeSeconds { get; set; } = 0.15f;

        /// <summary>
        /// How far this element is into its highlight, 0 at rest and 1 when
        /// hovered or pressed — the same eased number the fill is drawn with.
        ///
        /// Exists so a LABEL can cross at exactly the rate its background
        /// does. Text that snaps to white over a background still fading in is
        /// worse than either half done alone, and a second easing clock in the
        /// element would be the same value computed twice and drifting.
        /// Updated in <see cref="Resolve"/>, i.e. when the owning element
        /// draws its fill — always before its children draw their text.
        /// </summary>
        public float HighlightWeight => Math.Min(1f, weightHovered + weightPressed);

        /// <summary>
        /// The fill to actually draw for <paramref name="state"/> right now.
        /// Only animates when all three states are <see cref="TVFillSimpleGradient"/>
        /// (every stock button per Theme.cs's Positive/Negative/Neutral
        /// states) — crossfading an arbitrary mix of TVFill subtypes (a
        /// gradient hover state over an image normal state, say) has no
        /// single well-defined blend, so anything outside the common case
        /// falls back to the pre-existing instant snap rather than a wrong
        /// or partial animation.
        /// </summary>
        public TVFill Resolve(Managers.InputManager.ElementState state)
        {
            // Advanced BEFORE the gradients-only test below, so
            // <see cref="HighlightWeight"/> is honest even for a fill that
            // cannot itself crossfade — a caller colouring text off this
            // weight is entitled to a real number rather than a stuck one.
            double now = clock.Elapsed.TotalSeconds;
            float dt = lastSeconds < 0 ? 0f : (float)Math.Min(now - lastSeconds, 0.25);
            lastSeconds = now;

            weightNormal = Ease.Toward(weightNormal, state == Managers.InputManager.ElementState.Normal ? 1f : 0f, dt, FadeSeconds);
            weightHovered = Ease.Toward(weightHovered, state == Managers.InputManager.ElementState.Hovered ? 1f : 0f, dt, FadeSeconds);
            weightPressed = Ease.Toward(weightPressed, state == Managers.InputManager.ElementState.Pressed ? 1f : 0f, dt, FadeSeconds);

            if (!(States.NormalFill is TVFillSimpleGradient normalG
                && States.HoveredFill is TVFillSimpleGradient hoverG
                && States.PressedFill is TVFillSimpleGradient pressG))
            {
                return state switch
                {
                    Managers.InputManager.ElementState.Hovered => States.HoveredFill,
                    Managers.InputManager.ElementState.Pressed => States.PressedFill,
                    _ => States.NormalFill,
                };
            }

            float sum = Math.Max(0.0001f, weightNormal + weightHovered + weightPressed);
            Color primary = BlendColor(normalG.PrimaryColor, hoverG.PrimaryColor, pressG.PrimaryColor, sum);
            Color secondary = BlendColor(normalG.SecondaryColor, hoverG.SecondaryColor, pressG.SecondaryColor, sum);
            return new TVFillSimpleGradient(primary, secondary, normalG.Direction);

            Color BlendColor(Color a, Color b, Color c, float weightSum)
            {
                Vector4 blended = (a.ToVector4() * weightNormal + b.ToVector4() * weightHovered + c.ToVector4() * weightPressed) / weightSum;
                return new Color(blended);
            }
        }
    }

    public class TVFillImage : TVFill
    {

        public Tiling Tiling { get; set; }

        /// <summary>Draw color the texture is multiplied by (default white =
        /// unchanged). Lets one grayscale/alpha texture serve many colored
        /// fills — e.g. waveform block faces tinted per channel.</summary>
        public Color Tint { get; set; } = Color.White;

        public TVFillImage SetOpacity(float opacity)
        {
            Opacity = opacity;
            return this;
        }
    }

    public class TVFillSolidColor : TVFill
    {
        public Color Color { get; set; }

        /// <summary>Optional live-computed alternative to <see cref="Color"/>
        /// — set via the <c>Func&lt;Color&gt;</c> constructor when a fill
        /// needs to track something that changes after construction (e.g.
        /// GustUI.Resources.StaticResources.Theme after a light/dark
        /// switch) without the owning view rebuilding or re-Setting the
        /// trait. Draw() reads <see cref="ResolvedColor"/>, not
        /// <see cref="Color"/>, so this is evaluated fresh every frame.</summary>
        private readonly Func<Color> colorFunc;

        public Color ResolvedColor => colorFunc != null ? colorFunc() : Color;

        public TVFillSolidColor() { }
        public TVFillSolidColor(Color color)
        {
            Color = color;
        }

        public TVFillSolidColor(Func<Color> colorFunc)
        {
            this.colorFunc = colorFunc;
        }
    }

    /// <summary>
    /// A solid fill with ROUNDED corners — the same one-line swap as
    /// <see cref="TVFillSolidColor"/>, drawn through
    /// <c>DrawManager.DrawRoundedRectangle</c> (radius-cached corner atlas,
    /// so a resizing element bakes nothing per frame). Optional
    /// <see cref="BorderColor"/> paints a rounded outline in the same pass,
    /// because a square <see cref="Traits.BorderSizeTrait"/> border around a
    /// rounded fill reads as a mistake.
    /// </summary>
    public class TVFillRoundedColor : TVFill
    {
        public Color Color { get; set; }

        public int Radius { get; set; } = 0;

        /// <summary>Null = no outline.</summary>
        public Color? BorderColor { get; set; }

        public int BorderSize { get; set; } = 1;

        /// <summary>Optional live-computed alternatives to <see cref="Color"/>/
        /// <see cref="BorderColor"/> — see TVFillSolidColor's matching
        /// field for why. Draw() reads <see cref="ResolvedColor"/>/
        /// <see cref="ResolvedBorderColor"/>, evaluated fresh every frame.</summary>
        private readonly Func<Color> colorFunc;
        private readonly Func<Color> borderColorFunc;

        public Color ResolvedColor => colorFunc != null ? colorFunc() : Color;
        public Color? ResolvedBorderColor => borderColorFunc != null ? borderColorFunc() : BorderColor;

        public TVFillRoundedColor() { }

        public TVFillRoundedColor(Color color, int radius = 0)
        {
            Color = color;
            Radius = radius;
        }

        public TVFillRoundedColor(Color color, int radius, Color borderColor, int borderSize = 1)
        {
            Color = color;
            Radius = radius;
            BorderColor = borderColor;
            BorderSize = borderSize;
        }

        public TVFillRoundedColor(Func<Color> colorFunc, int radius, Func<Color> borderColorFunc, int borderSize = 1)
        {
            this.colorFunc = colorFunc;
            Radius = radius;
            this.borderColorFunc = borderColorFunc;
            BorderSize = borderSize;
        }
    }

    /// <summary>
    /// A 1px ROUNDED OUTLINE with a pinch at each repeat boundary — how a
    /// sequencer block says where its pattern starts over (bug board #212).
    ///
    /// It draws no interior at all, only the line, so it goes over whatever
    /// face the element already has (a flat colour, a baked waveform, a mini
    /// piano roll) without knowing anything about it.
    ///
    /// A PINCH, NOT A DIVIDER. The old answer was to darken every second
    /// pass, which reads as stripes rather than as repeats and fights with
    /// the content on the block. Here the outline simply follows the shape a
    /// row of butted rounded rectangles would have: rounded at the two ends,
    /// and where two passes meet, the two corner arcs and nothing between
    /// them — a small cusp on the top edge and another on the bottom, with no
    /// line across the middle. It marks the seam without cutting the block up.
    ///
    /// <see cref="Seams"/> is block-local x, and only the first
    /// <see cref="SeamCount"/> entries are read, so the array can be sized
    /// once and reused every frame.
    /// </summary>
    public class TVFillLoopOutline : TVFill
    {
        /// <summary>The line itself.</summary>
        public Color Color { get; set; }

        /// <summary>Live-computed alternative to <see cref="Color"/>, for a
        /// theme that can change under a pooled element.</summary>
        private readonly Func<Color> colorFunc;

        public Color ResolvedColor => colorFunc != null ? colorFunc() : Color;

        public int Radius { get; set; } = 4;

        public int Thickness { get; set; } = 1;

        /// <summary>Where the pattern starts again, in this element's own x.
        /// Ascending; entries at or outside the edges are ignored.</summary>
        public float[] Seams { get; set; }

        /// <summary>How many of <see cref="Seams"/> to read.</summary>
        public int SeamCount { get; set; }

        public TVFillLoopOutline() { }

        public TVFillLoopOutline(Color color, int radius = 4, int thickness = 1)
        {
            Color = color;
            Radius = radius;
            Thickness = thickness;
        }

        public TVFillLoopOutline(Func<Color> colorFunc, int radius = 4, int thickness = 1)
        {
            this.colorFunc = colorFunc;
            Radius = radius;
            Thickness = thickness;
        }
    }

    public class TVVideoFill : TVFill
    {
        private Video video;
        private VideoPlayer player;
        public TVVideoFill(Video video)
        {
            this.video = video;
            player = new VideoPlayer();
            // Set once, not every GetTexture() call (found 2026-08-16
            // profiling the decorative background video's draw cost
            // alongside the GetTexture()-itself fix in KNI's WMS
            // VideoPlayer): both setters unconditionally make a real COM
            // call down into Media Foundation (SetChannelVolumes(), even
            // when the value hasn't actually changed) - doing that on
            // every one of GetTexture()'s ~140Hz calls, for a value that's
            // always the same, was pure waste.
            player.Volume = 0.0f;
            player.IsMuted = true;
        }

        private bool stopped;

        /// <summary>
        /// Stops playback for good: <see cref="GetTexture"/> hands back nothing
        /// from here on. TERMINAL — this fill cannot be restarted afterwards.
        /// Play() is accepted but the player's state falls straight back to
        /// Stopped and no further frames arrive, and neither pausing instead
        /// nor replacing the VideoPlayer recovers it (WindowsDX / Media
        /// Foundation, KNI — measured 2026-08-24).
        ///
        /// To hide a video TEMPORARILY, stop drawing it — swap the element's
        /// fill for something else — rather than stopping the player. Nothing
        /// calls GetTexture() while the fill is out of the draw tree, and a
        /// player left alone in that state costs nothing measurable (0.05
        /// CPU-seconds over 15s, i.e. inside the noise, measured the same day
        /// against the same app paused).
        /// </summary>
        public void Stop()
        {
            stopped = true;
            try
            {
                player.Stop();
            }
            catch
            {
            }
        }

        public Texture2D GetTexture()
        {
            try
            {
                if (stopped)
                {
                    return null;
                }

                if (player.State == MediaState.Stopped)
                {
                    player.Play(video);
                }

                return player.GetTexture();
            }
            catch (Exception e)
            {

            }

            return null;
        }
    }

    /// <summary>
    /// A linear 2-color gradient fill, drawn via per-vertex color
    /// interpolation on the shared white atlas texel (FilledRectangleElement/
    /// SpriteBatchExtensions.DrawFilledRectangleGradient) — no texture is
    /// baked or owned here; PrimaryColor/SecondaryColor/Direction are plain
    /// data the draw call reads fresh each frame.
    /// </summary>
    public class TVFillSimpleGradient : TVFill
    {
        public Color PrimaryColor { get; }
        public Color SecondaryColor { get; }
        public Direction Direction { get; }

        public TVFillSimpleGradient(Color primary, Color secondary, Direction direction)
        {
            PrimaryColor = primary;
            SecondaryColor = secondary;
            Direction = direction;
        }
    }

    public enum Tiling
    {
        None,
        Repeat,
        Stretch,
        Scale
    }

    public enum Direction
    {
        Horizontally,
        Vertically,
    }

}
