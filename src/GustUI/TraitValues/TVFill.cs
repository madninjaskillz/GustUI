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
        /// <summary>
        /// Draw as though the pointer were on it.
        ///
        /// For a row a KEYBOARD is on: arrowing down a menu has to look like
        /// hovering down it, and it has to look like it in the same colours,
        /// with the same crossfade, and — because ink is read off
        /// <see cref="HighlightWeight"/> — with the same guarantee that the
        /// label stays legible over the highlight. A second highlight
        /// mechanism beside this one is two things to keep in step, and the
        /// first thing to drift is the one nobody is looking at.
        /// </summary>
        public bool ForceHovered { get; set; }

        public TVFill Resolve(Managers.InputManager.ElementState state)
        {
            if (ForceHovered && state == Managers.InputManager.ElementState.Normal)
            {
                state = Managers.InputManager.ElementState.Hovered;
            }

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
            Color primary = BlendColor(normalG.ResolvedPrimary, hoverG.ResolvedPrimary, pressG.ResolvedPrimary, sum);
            Color secondary = BlendColor(normalG.ResolvedSecondary, hoverG.ResolvedSecondary, pressG.ResolvedSecondary, sum);
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
    /// DIAGONAL STRIPES — the universal "this stretch is not right", drawn
    /// over whatever is already there.
    ///
    /// A flat tint cannot do this job: the sequencer already spends flat
    /// washes on selection and on the playhead, and a third one reads as a
    /// fourth kind of selection rather than as a problem. Stripes read as
    /// hazard tape at any size and survive being drawn over a busy waveform,
    /// which is exactly where this lands.
    ///
    /// Clipped to the element's own rectangle, so a stripe that runs off the
    /// end is cut rather than spilling into the row above — that is what makes
    /// this usable as an overlay rather than something needing its own
    /// scissored container.
    /// </summary>
    public class TVFillHatch : TVFill
    {
        /// <summary>The stripes.</summary>
        public Color Color { get; set; }

        /// <summary>Optional flat wash UNDER them. Null draws stripes
        /// alone, which is what an overlay usually wants.</summary>
        public Color? Background { get; set; }

        /// <summary>Gap between stripe starts along the top edge, in pixels.
        /// The perpendicular gap is this times cos 45°.</summary>
        public float Spacing { get; set; } = 10f;

        public float Thickness { get; set; } = 3f;

        /// <summary>Stripe angle in radians. The default leans the way a
        /// forward slash does.</summary>
        public float Angle { get; set; } = -Microsoft.Xna.Framework.MathHelper.PiOver4;

        private readonly Func<Color> colorFunc;

        public Color ResolvedColor => colorFunc != null ? colorFunc() : Color;

        public TVFillHatch() { }

        public TVFillHatch(Color color, float spacing = 10f, float thickness = 3f)
        {
            Color = color;
            Spacing = spacing;
            Thickness = thickness;
        }

        public TVFillHatch(Func<Color> colorFunc, float spacing = 10f, float thickness = 3f)
        {
            this.colorFunc = colorFunc;
            Spacing = spacing;
            Thickness = thickness;
        }
    }

    /// <summary>
    /// A block OUTLINE with a notch at each repeat boundary — how a sequencer
    /// block says where its pattern starts over (bug board #212).
    ///
    /// It draws no interior at all, only the line, so it goes over whatever
    /// face the element already has (a flat colour, a baked waveform, a mini
    /// piano roll) without knowing anything about it.
    ///
    /// Straight edges with each corner stepped off two strokes, and at each
    /// seam a short tick down from the top edge and up from the bottom, with
    /// nothing across the middle. All of it axis-aligned quads — see
    /// <see cref="Extensions.LoopOutlineGeometry"/> for the shape and for why
    /// the arcs it used to draw went.
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

        public int Thickness { get; set; } = 1;

        /// <summary>Where the pattern starts again, in this element's own x.
        /// Ascending; entries at or outside the edges are ignored.</summary>
        public float[] Seams { get; set; }

        /// <summary>How many of <see cref="Seams"/> to read.</summary>
        public int SeamCount { get; set; }

        public TVFillLoopOutline() { }

        public TVFillLoopOutline(Color color)
        {
            Color = color;
        }

        public TVFillLoopOutline(Func<Color> colorFunc)
        {
            this.colorFunc = colorFunc;
        }

        /// <summary>The corners stopped being arcs on 2026-10-07, so there is
        /// no radius; kept so a caller built against the old signature still
        /// gets the right thickness rather than its radius.</summary>
        [Obsolete("The outline has no corner radius any more; use TVFillLoopOutline(color) { Thickness = n }.")]
        public TVFillLoopOutline(Color color, int radius, int thickness = 1)
        {
            Color = color;
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
            renderBlur = RenderBlur;
        }

        private bool stopped;

        /// <summary>
        /// How soft to draw the video: the number of horizontal+vertical
        /// Gaussian pairs (DrawManager's backdrop blur) run over it at a
        /// quarter of the size it is drawn at. 0, the default, draws it
        /// sharp. Each pair is two small passes, run only while the fill is
        /// being drawn; the blur lands one frame behind, like any pre-pass,
        /// so the first frame after the fill appears draws nothing.
        ///
        /// For a small video stretched over a big area, which is what this is
        /// for: blurring AFTER the stretch hides the stretched texel grid, so
        /// a 256x144 clip can stand in for a 720p one behind a window.
        /// </summary>
        public int Blur { get; set; }

        private readonly Action renderBlur;
        private RenderTarget2D blurTarget;
        private RenderTarget2D blurScratch;
        private Texture2D blurred;
        private bool blurQueued;
        private int blurWidth;
        private int blurHeight;

        /// <summary>
        /// The blurred frame to draw over an area of <paramref name="width"/> x
        /// <paramref name="height"/> device pixels, and a request to render the
        /// next one before the coming frame. Null until the first blurred
        /// frame exists, and once stopped. Called from Draw(); allocates
        /// nothing after the first call at a given size.
        /// </summary>
        public Texture2D GetBlurredTexture(int width, int height)
        {
            if (stopped)
            {
                return null;
            }

            blurWidth = Math.Max(1, width / 4);
            blurHeight = Math.Max(1, height / 4);
            if (!blurQueued)
            {
                blurQueued = true;
                Resources.StaticResources.DrawManager.QueuePrePass(renderBlur);
            }

            return blurred;
        }

        private void RenderBlur()
        {
            blurQueued = false;
            Texture2D frame;
            using (Managers.Telemetry.Scope("Draw.VideoBackground.GetTexture"))
            {
                frame = GetTexture();
            }

            if (frame == null)
            {
                return;
            }

            using (Managers.Telemetry.Scope("Draw.VideoBackground.Blur"))
            {
                blurred = Resources.StaticResources.DrawManager.RenderBlurred(frame,
                    ref blurTarget, ref blurScratch, blurWidth, blurHeight, Blur);
            }
        }

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

            // Nothing draws the blur after this, so its targets go now.
            blurred = null;
            blurTarget?.Dispose();
            blurTarget = null;
            blurScratch?.Dispose();
            blurScratch = null;
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
    /// A looping background drawn from SPRITE SHEETS instead of a video: the
    /// frames of a short clip baked offline into a grid of small cells on one
    /// or more textures, crossfaded by time and drawn blurred over the fill's
    /// whole area. It needs no codec, so it runs on every platform KNI does,
    /// and it is cheap enough to sit behind a whole app:
    ///
    /// * The picture is composed and blurred into a target a quarter of the
    ///   drawn size (<see cref="Managers.DrawManager.RenderCrossfadeBlurred"/>:
    ///   two cell draws then <see cref="Blur"/> blur pairs, in the frame's
    ///   pre-pass) and that target is drawn stretched, one quad.
    /// * That target is only redrawn when <see cref="SpriteSheetTimeline.Due"/>
    ///   says so: at most <see cref="SpriteSheetTimeline.MaxUpdatesPerSecond"/>
    ///   times a second whatever the app's frame rate, never when the picture
    ///   would not change, and at once when the size does. Every other frame
    ///   costs the one stretched quad.
    /// * Nothing at all happens while the fill is not drawn (out of the tree,
    ///   or the window minimised to nothing) or is hidden under opaque children
    ///   that leave no gap (<see cref="Covered"/>), and nothing is allocated
    ///   per frame once the targets exist.
    ///
    /// <see cref="Underlay"/> is drawn beneath until the first picture exists
    /// and while it fades in over <see cref="FadeInSeconds"/>.
    /// </summary>
    public class TVSpriteSheetFill : TVFill
    {
        private readonly Texture2D[] sheets;
        private readonly Func<double> clock;
        private readonly Action render;
        private RenderTarget2D target;
        private RenderTarget2D scratch;
        private Texture2D picture;
        private bool queued;
        private bool released;
        private bool sheetsReady;
        private int width;
        private int height;
        private double firstPictureAt = -1;

        /// <summary>Real time for the fade-in, apart from the loop's clock,
        /// which a caller may hold still.</summary>
        private readonly System.Diagnostics.Stopwatch fadeClock = System.Diagnostics.Stopwatch.StartNew();

        public SpriteSheetTimeline Timeline { get; }

        /// <summary>The last blurred picture (covering the whole area the
        /// fill was last asked for), or null. For an <see cref="AcrylicLayer"/>
        /// that blurs it again behind a window.</summary>
        public Texture2D Picture => picture;

        /// <summary>Moves on every redraw of <see cref="Picture"/>, so a
        /// consumer can tell whether it has changed without comparing pixels.</summary>
        public int PictureVersion { get; private set; }

        /// <summary>Blur pairs run at a quarter of the drawn size, as for
        /// <see cref="TVVideoFill.Blur"/>. 0 draws the cells sharp.</summary>
        public int Blur { get; set; }

        /// <summary>Drawn beneath until the first picture has faded in. Null
        /// draws nothing there.</summary>
        public TVFill Underlay { get; set; }

        public float FadeInSeconds { get; set; } = 0.6f;

        /// <summary>
        /// Asked before each redraw (so at most
        /// <see cref="SpriteSheetTimeline.MaxUpdatesPerSecond"/> times a
        /// second): true skips it and keeps the last picture. For a host that
        /// knows its window is minimised when the window itself does not say
        /// so - on DirectX a minimised window lays itself out at nothing and
        /// the timeline stops by itself, but SDL (DesktopGL) keeps reporting
        /// the old size while the app goes on drawing to nobody.
        /// </summary>
        public Func<bool> Paused { get; set; }

        /// <summary>
        /// Set by the element drawing this fill: true while its own opaque
        /// children hide all of it, when it draws and redraws nothing
        /// (FilledRectangleElement.IsCoveredByOpaqueChildren). Read it to
        /// tell whether the background is costing anything right now.
        /// </summary>
        public bool Covered { get; set; }

        /// <param name="sheets">The sheets, in frame order, as many as
        /// <paramref name="timeline"/> needs. Slots may be null when the fill
        /// is made and filled in later (a download): nothing is drawn but the
        /// <see cref="Underlay"/> until every one is there.</param>
        /// <param name="clock">Where the loop is, in seconds on any monotonic
        /// clock; a Stopwatch when null. A clock held still shows one moment
        /// (for screenshots).</param>
        public TVSpriteSheetFill(Texture2D[] sheets, SpriteSheetTimeline timeline, Func<double> clock = null)
        {
            if (sheets == null || sheets.Length < timeline.SheetCount)
            {
                throw new ArgumentException($"the timeline needs {timeline.SheetCount} sheet(s)", nameof(sheets));
            }

            this.sheets = sheets;
            Timeline = timeline;
            if (clock == null)
            {
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                clock = () => watch.Elapsed.TotalSeconds;
            }

            this.clock = clock;
            render = Render;
        }

        /// <summary>How opaque to draw the picture right now: 0 until it
        /// exists, then rising to 1 over <see cref="FadeInSeconds"/>.</summary>
        public float PictureOpacity
        {
            get
            {
                if (picture == null || firstPictureAt < 0)
                {
                    return 0f;
                }

                if (FadeInSeconds <= 0f)
                {
                    return 1f;
                }

                return (float)Math.Min(1.0, (fadeClock.Elapsed.TotalSeconds - firstPictureAt) / FadeInSeconds);
            }
        }

        /// <summary>
        /// The picture to draw over <paramref name="deviceWidth"/> x
        /// <paramref name="deviceHeight"/> device pixels, and, when the
        /// timeline says it is due, a request to redraw it before the next
        /// frame. Null until the first one exists. Called from Draw().
        /// </summary>
        public Texture2D GetTexture(int deviceWidth, int deviceHeight)
        {
            released = false;
            if (!sheetsReady)
            {
                for (int i = 0; i < Timeline.SheetCount; i++)
                {
                    if (sheets[i] == null)
                    {
                        return null;
                    }
                }

                sheetsReady = true;
            }

            if (target != null && (target.IsDisposed || target.IsContentLost))
            {
                Timeline.Invalidate();
            }

            // A quarter of the drawn size, as the backdrop blur does. Under 4
            // device pixels (a minimised window lays itself out at nothing)
            // that is 0, and the timeline never asks for a redraw.
            int w = deviceWidth / 4;
            int h = deviceHeight / 4;
            if (!queued && Timeline.Due(clock(), w, h, out _))
            {
                if (Paused != null && Paused())
                {
                    // Not drawn, so not recorded: due again on the next frame,
                    // and drawn on the first one after it stops being paused.
                    Timeline.Invalidate();
                    return picture;
                }

                width = w;
                height = h;
                queued = true;
                Resources.StaticResources.DrawManager.QueuePrePass(render);
            }

            return picture;
        }

        private void Render()
        {
            queued = false;
            if (released)
            {
                // Asked for, then put away before the frame it was for: making
                // the targets now would hold them for as long as it stays away.
                return;
            }

            SpriteSheetTimeline.FramePair pair = Timeline.LastPair;
            (int fromSheet, int fromX, int fromY) = Timeline.CellOf(pair.From);
            (int toSheet, int toX, int toY) = Timeline.CellOf(pair.To);
            using (Managers.Telemetry.Scope("Draw.SpriteSheetBackground.Render"))
            {
                picture = Resources.StaticResources.DrawManager.RenderCrossfadeBlurred(
                    sheets[fromSheet], new Rectangle(fromX, fromY, Timeline.CellWidth, Timeline.CellHeight),
                    sheets[toSheet], new Rectangle(toX, toY, Timeline.CellWidth, Timeline.CellHeight),
                    pair.Weight, ref target, ref scratch, width, height, Blur);
            }

            if (picture != null)
            {
                PictureVersion++;
            }

            if (picture != null && firstPictureAt < 0)
            {
                firstPictureAt = fadeClock.Elapsed.TotalSeconds;
            }
        }

        /// <summary>Drops the render targets (the sheets belong to whoever
        /// loaded them). The fill still works afterwards: the next draw makes
        /// new targets, without a second fade-in.</summary>
        public void ReleaseTargets()
        {
            released = true;
            picture = null;
            target?.Dispose();
            target = null;
            scratch?.Dispose();
            scratch = null;
            Timeline.Invalidate();
        }
    }

    /// <summary>
    /// The frosted glass behind <see cref="TVAcrylicFill"/>s: a
    /// <see cref="TVSpriteSheetFill"/>'s already-blurred picture, blurred
    /// AGAIN at half its size (so an eighth of the window's), and kept.
    ///
    /// Cheap by construction. It reblurs only when the background's picture
    /// has changed (<see cref="TVSpriteSheetFill.PictureVersion"/>, at most
    /// 15 times a second) or the window's size has; every other frame each
    /// acrylic surface is one textured quad and one tint. One layer serves
    /// any number of surfaces, which simply draw their own part of it.
    ///
    /// It keeps the background moving even while that background is hidden
    /// under opaque windows - the glass shows it, so it must stay alive.
    /// </summary>
    public sealed class AcrylicLayer
    {
        private readonly Action render;
        private RenderTarget2D target;
        private RenderTarget2D scratch;
        private Texture2D blurred;
        private bool queued;
        private int doneVersion = -1;
        private int lastFrameAsked = -1;

        public AcrylicLayer(TVSpriteSheetFill source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            render = Render;
        }

        public TVSpriteSheetFill Source { get; }

        /// <summary>Blur pairs run over the background's picture at
        /// 1/<see cref="Downsample"/> of its size, on top of the background's own.</summary>
        public int Blur { get; set; } = 2;

        /// <summary>How much smaller than the background's picture (itself a
        /// quarter of the window) the glass is blurred at. Each step down
        /// doubles the reach of every blur pair and quarters its cost.</summary>
        public int Downsample { get; set; } = 2;

        private static Texture2D grain;

        /// <summary>
        /// The acrylic GRAIN tile (<see cref="AcrylicMath.FillGrain"/>): 128x128 black and white specks at
        /// random low alpha, made once (fixed seed, so every run is the same)
        /// and shared by every acrylic surface. Drawn tiled over the glass at
        /// <see cref="TVAcrylicFill.Grain"/>; it never moves, which is what
        /// makes it read as the material rather than as noise on the picture.
        /// </summary>
        public static Texture2D GrainTile
        {
            get
            {
                if (grain == null || grain.IsDisposed)
                {
                    int size = AcrylicMath.GrainSize;
                    var texels = new byte[size * size * 4];
                    AcrylicMath.FillGrain(texels);
                    grain = new Texture2D(Resources.StaticResources.GraphicsDevice, size, size, false, SurfaceFormat.Color);
                    grain.SetData(texels);
                }

                return grain;
            }
        }

        /// <summary>The glass, covering the whole root window; null until the
        /// background has a picture. Called by every surface every frame;
        /// does its scheduling once a frame however many ask.</summary>
        public Texture2D GetTexture()
        {
            int frame = Resources.StaticResources.DrawManager.FrameNumber;
            if (frame != lastFrameAsked)
            {
                lastFrameAsked = frame;

                // Keep the background animating, covered or not: the size is
                // the root's, which is what the root's own fill asks with.
                float scale = Resources.StaticResources.DrawManager.RenderScale;
                Vector2 root = Extensions.ElementExtensions.GetSize(Resources.StaticResources.RootWindow).AsXna;
                Source.GetTexture((int)(root.X * scale), (int)(root.Y * scale));

                if (!queued)
                {
                    queued = true;
                    Resources.StaticResources.DrawManager.QueuePrePass(render);
                }
            }

            return blurred;
        }

        private void Render()
        {
            queued = false;
            Texture2D picture = Source.Picture;
            if (picture == null || picture.IsDisposed)
            {
                return;
            }

            int down = Math.Max(1, Downsample);
            int w = Math.Max(1, picture.Width / down);
            int h = Math.Max(1, picture.Height / down);
            bool sizeChanged = target == null || target.IsDisposed || target.IsContentLost
                || target.Width != w || target.Height != h;
            if (!AcrylicMath.ShouldReblur(doneVersion, Source.PictureVersion, sizeChanged))
            {
                return;
            }

            using (Managers.Telemetry.Scope("Draw.Acrylic.Blur"))
            {
                blurred = Resources.StaticResources.DrawManager.RenderBlurred(picture, ref target, ref scratch, w, h,
                    Math.Max(1, Blur), keep: true);
            }

            doneVersion = Source.PictureVersion;
        }
    }

    /// <summary>
    /// ACRYLIC: a surface that shows the app's moving background through it,
    /// blurred further and darkened, like frosted glass (Windows' acrylic
    /// material). Draws its own part of an <see cref="AcrylicLayer"/> - the
    /// piece of the glass under this element's rectangle, so it stays right
    /// as the element moves or resizes - multiplied down by
    /// <see cref="Darken"/>, with <see cref="Tint"/> laid over at
    /// <see cref="TintAmount"/>. Text and controls on top draw as usual.
    ///
    /// <see cref="Fallback"/> draws until the glass exists (the background's
    /// sheets still loading), so the surface is never see-through to nothing.
    /// </summary>
    public class TVAcrylicFill : TVFill
    {
        public TVAcrylicFill(AcrylicLayer layer)
        {
            Layer = layer ?? throw new ArgumentNullException(nameof(layer));
        }

        public AcrylicLayer Layer { get; }

        /// <summary>0 leaves the glass as bright as the background; 1 is black.
        /// Read every frame, so it can follow the theme.</summary>
        public Func<float> Darken { get; set; } = () => 0.4f;

        /// <summary>Colour laid over the glass, read every frame (a theme
        /// token). Transparent draws nothing.</summary>
        public Func<Color> Tint { get; set; } = () => Color.Transparent;

        /// <summary>How strongly <see cref="Tint"/> is laid over, 0..1.</summary>
        public Func<float> TintAmount { get; set; } = () => 0f;

        /// <summary>Drawn instead until the glass exists. Null draws nothing.</summary>
        public TVFill Fallback { get; set; }

        /// <summary>How strongly the grain (<see cref="AcrylicLayer.GrainTile"/>)
        /// is laid over the glass, 0..1. 0 draws none.</summary>
        public float Grain { get; set; }
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

        /// <summary>Optional live-computed alternatives to the two colours —
        /// set via the <c>Func&lt;Color&gt;</c> constructor, the same escape
        /// hatch <see cref="TVFillSolidColor"/> and
        /// <see cref="TVFillRoundedColor"/> already had and this one did not.
        ///
        /// A gradient built from theme tokens with the plain constructor bakes
        /// whatever the palette said at construction, so it keeps the old
        /// colours through a light/dark switch while everything around it
        /// changes — which is exactly what Theme.SetMode's summary promises
        /// will NOT happen (ezmuze #231). Readers take
        /// <see cref="ResolvedPrimary"/>/<see cref="ResolvedSecondary"/>, which
        /// are evaluated fresh every frame.</summary>
        private readonly Func<Color> primaryFunc;

        private readonly Func<Color> secondaryFunc;

        public Color ResolvedPrimary => primaryFunc != null ? primaryFunc() : PrimaryColor;

        public Color ResolvedSecondary => secondaryFunc != null ? secondaryFunc() : SecondaryColor;

        public TVFillSimpleGradient(Color primary, Color secondary, Direction direction)
        {
            PrimaryColor = primary;
            SecondaryColor = secondary;
            Direction = direction;
        }

        public TVFillSimpleGradient(Func<Color> primary, Func<Color> secondary, Direction direction)
        {
            primaryFunc = primary;
            secondaryFunc = secondary;
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
