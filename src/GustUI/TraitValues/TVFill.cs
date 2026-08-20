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

            double now = clock.Elapsed.TotalSeconds;
            float dt = lastSeconds < 0 ? 0f : (float)Math.Min(now - lastSeconds, 0.25);
            lastSeconds = now;

            weightNormal = Ease.Toward(weightNormal, state == Managers.InputManager.ElementState.Normal ? 1f : 0f, dt);
            weightHovered = Ease.Toward(weightHovered, state == Managers.InputManager.ElementState.Hovered ? 1f : 0f, dt);
            weightPressed = Ease.Toward(weightPressed, state == Managers.InputManager.ElementState.Pressed ? 1f : 0f, dt);

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

        /// <summary>Stops playback for good; GetTexture will no longer restart the video.</summary>
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

    public class TVBlurFill : TVFill
    {
        public float Ratio { get; set; }
        public TVBlurFill(float ratio, TVFill overlay)
        {
            Ratio = ratio;
            OverlayFill = overlay;
        }

        public TVFill OverlayFill { get; set; }
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
