using GustUI.Managers;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI
{
    public enum ThemeMode
    {
        Dark,
        Light
    }

    /// <summary>
    /// One full set of app-wide surface/accent color values. ezmuze-studio's
    /// design-guide.md §1 is normative — <see cref="Theme.DarkPalette"/> and
    /// <see cref="Theme.LightPalette"/> are the two shipped instances. Accent
    /// tokens keep the SAME hue meaning across both palettes (blue is always
    /// "selection", amber is always "live automation") — only brightness/
    /// saturation is retuned per theme for legibility, never the hue itself.
    /// </summary>
    public class Palette
    {
        public Color SurfaceBackdrop;
        public Color SurfacePanel;
        public Color SurfaceHeader;
        public Color SurfaceRaised;
        public Color SurfaceBorder;
        public Color BodyText;

        public Color MenuBarFillTop;
        public Color MenuBarFillBottom;

        public Color AccentSelection;
        public Color AccentLiveAutomation;
        public Color AccentModPositive;
        public Color AccentModNegative;
        public Color AccentPlayhead;
        public Color AccentMuteOn;
        public Color AccentMuteOff;
        public Color AccentWarning;
        public Color AccentVolume;
        public Color AccentPan;

        public Color PositiveBase, PositiveHover, PositivePress;
        public Color NegativeBase, NegativeHover, NegativePress;
        public Color NeutralBase, NeutralHover, NeutralPress;

        /// <summary>Device-chain category color tabs (design-guide.md §8.3) —
        /// shared across both themes, same hues as Cerebrum's own per-section
        /// accent language so the two systems reinforce each other.</summary>
        public Color CategorySource = new Color(53, 199, 232);
        public Color CategoryEffects = new Color(95, 224, 160);
        public Color CategoryModulation = new Color(176, 124, 255);
        public Color CategoryUtility = new Color(110, 140, 168);
        public Color CategoryComposite = new Color(255, 111, 156);

        /// <summary>Soft drop-shadow tint for floating surfaces (modals,
        /// popovers, tooltips, the Stack panel) — design-guide.md §6.2. Needs
        /// less opacity in light mode to read the same way against a bright
        /// backdrop than in dark mode.</summary>
        public Color ElevationShadow;
    }

    public class Theme
    {
        private const float NineGridOpacity = 0.25f;

        // ---- Typography scale (design-guide.md §2, revised 2026-08-12 — the
        // 14/18/24/30/38 scale from the previous day's revision still ran too
        // small at the bottom and too large at the top; narrowed toward the
        // 24 anchor to 16/20/24/28/32, an even 4-step arithmetic progression).
        // Field NAMES are kept stable (UiFontSmall/UiFont/UiFontLarge) so the
        // many existing call sites across GustUI/Ezmuze don't need a rename
        // sweep; only the SIZES change.
        public TVFont UiFontSmall = new() { Family = "segoeuisl.ttf", Size = 16, Border = 0 };      // caption
        public TVFont UiFontSecondary = new() { Family = "segoeuisl.ttf", Size = 20, Border = 0 };  // secondary/list-row/tooltip
        public TVFont UiFont = new() { Family = "segoeuisl.ttf", Size = 24, Border = 0 };            // body (unchanged anchor size)
        public TVFont UiFontSubtitle = new() { Family = "segoeuisl.ttf", Size = 28, Border = 0 };    // panel/modal sub-titles
        public TVFont UiFontLarge = new() { Family = "segoeuisl.ttf", Size = 32, Border = 0 };       // top-level section headers only

        /// <summary>Segoe UI Bold (segoeuib.ttf) — the app's ONLY other font
        /// weight (everything else is Semilight): titles/headers that need
        /// to visually anchor a surface, used sparingly so weight actually
        /// signals hierarchy instead of becoming noise. No italic variant
        /// exists or is planned — design-guide.md §2 standardizes on plain
        /// (non-italic) text everywhere.</summary>
        public TVFont UiFontBold = new() { Family = "segoeuib.ttf", Size = 24, Border = 0 };
        /// <summary>
        /// The wordmark face (Comfortaa, SIL OFL — see THIRD-PARTY/). A brand
        /// font, NOT a UI font: it exists so a logo can be SET rather than
        /// shipped as pixels, which makes it resolution-independent through the
        /// SDF path and recolourable per theme. Don't reach for it for ordinary
        /// interface text — design-guide.md §2's type ramp is Segoe, and mixing
        /// a display face into the ramp muddies the hierarchy it encodes.
        ///
        /// Both weights are Basic-Latin subsets of the upstream variable font,
        /// 20 KB each; see THIRD-PARTY/README.md for what was changed and why.
        /// </summary>
        public TVFont WordmarkFont = new() { Family = "Comfortaa-Bold.ttf", Size = 48, Border = 0 };

        public TVFont WordmarkLightFont = new() { Family = "Comfortaa-Light.ttf", Size = 48, Border = 0 };

        public TVFont SymbolFont = new() { Family = "segmdl2.ttf", Size = 24, Border = 0 };
        public TVFont AltSymbolFont = new() { Family = "SegoeIcons.ttf", Size = 24, Border = 0 };

        public Icons Icons = new Icons();

        public TVFillImage MenuLogo = null;


        public Dictionary<InputManager.KeyboardModifiers, TVFillImage> KBModifiers = new Dictionary<InputManager.KeyboardModifiers, TVFillImage>
        {
            { InputManager.KeyboardModifiers.ctrl, new TVFillImage { Texture = Resources.StaticResources.UUContent.GetTexture("modifier_ctrl.png") } },
                { InputManager.KeyboardModifiers.shift, new TVFillImage { Texture = Resources.StaticResources.UUContent.GetTexture("modifier_shift.png") } },
                { InputManager.KeyboardModifiers.alt, new TVFillImage { Texture = Resources.StaticResources.UUContent.GetTexture("modifier_alt.png") } }
        };

        public TVFillImage NineGridTopLeft = new TVFillImage     { Texture = Resources.StaticResources.UUContent.GetTexture("tl.png"), Opacity = NineGridOpacity };
        public TVFillImage NineGridTop = new TVFillImage         { Texture = Resources.StaticResources.UUContent.GetTexture("t.png"), Opacity = NineGridOpacity };
        public TVFillImage NineGridTopRight = new TVFillImage    { Texture = Resources.StaticResources.UUContent.GetTexture("tr.png"), Opacity = NineGridOpacity };
        public TVFillImage NineGridLeft = new TVFillImage        { Texture = Resources.StaticResources.UUContent.GetTexture("l.png"), Opacity = NineGridOpacity };
        public TVFillImage NineGridRight = new TVFillImage       { Texture = Resources.StaticResources.UUContent.GetTexture("r.png"), Opacity = NineGridOpacity };
        public TVFillImage NineGridBottomLeft = new TVFillImage  { Texture = Resources.StaticResources.UUContent.GetTexture("bl.png"), Opacity = NineGridOpacity };
        public TVFillImage NineGridBottom = new TVFillImage      { Texture = Resources.StaticResources.UUContent.GetTexture("b.png"), Opacity = NineGridOpacity };
        public TVFillImage NineGridBottomRight = new TVFillImage { Texture = Resources.StaticResources.UUContent.GetTexture("br.png"), Opacity = NineGridOpacity };

        // ---- Palettes (design-guide.md §1) ----
        public static readonly Palette DarkPalette = new Palette
        {
            SurfaceBackdrop = new Color(13, 13, 17),
            SurfacePanel = new Color(22, 22, 28),
            SurfaceHeader = new Color(30, 30, 38),
            SurfaceRaised = new Color(40, 40, 51),
            SurfaceBorder = new Color(60, 60, 74),
            BodyText = new Color(224, 224, 230),

            MenuBarFillTop = new Color(214, 221, 232, 235),
            MenuBarFillBottom = new Color(198, 206, 220, 235),

            AccentSelection = new Color(150, 200, 255),
            AccentLiveAutomation = new Color(255, 196, 64),
            AccentModPositive = new Color(96, 224, 160),
            AccentModNegative = new Color(255, 120, 168),
            AccentPlayhead = new Color(255, 90, 90),
            AccentMuteOn = new Color(196, 68, 68),
            AccentMuteOff = new Color(88, 34, 34),
            AccentWarning = new Color(230, 140, 50),
            AccentVolume = new Color(96, 224, 160),
            AccentPan = new Color(224, 199, 64),

            PositiveBase = new Color(58, 108, 74), PositiveHover = new Color(74, 138, 94), PositivePress = new Color(96, 168, 116),
            NegativeBase = new Color(120, 58, 58), NegativeHover = new Color(158, 72, 72), NegativePress = new Color(196, 92, 92),
            NeutralBase = new Color(52, 52, 60), NeutralHover = new Color(66, 66, 76), NeutralPress = new Color(80, 80, 92),

            ElevationShadow = new Color(0, 0, 0, 90),
        };

        public static readonly Palette LightPalette = new Palette
        {
            SurfaceBackdrop = new Color(246, 247, 249),
            SurfacePanel = new Color(255, 255, 255),
            SurfaceHeader = new Color(237, 238, 241),
            SurfaceRaised = new Color(255, 255, 255),
            SurfaceBorder = new Color(210, 212, 218),
            BodyText = new Color(28, 28, 32),

            MenuBarFillTop = new Color(255, 255, 255, 235),
            MenuBarFillBottom = new Color(240, 242, 246, 235),

            AccentSelection = new Color(40, 110, 210),
            AccentLiveAutomation = new Color(196, 140, 20),
            AccentModPositive = new Color(30, 150, 95),
            AccentModNegative = new Color(210, 60, 110),
            AccentPlayhead = new Color(215, 45, 45),
            AccentMuteOn = new Color(196, 68, 68),
            AccentMuteOff = new Color(220, 190, 190),
            AccentWarning = new Color(190, 105, 20),
            AccentVolume = new Color(30, 150, 95),
            AccentPan = new Color(170, 140, 10),

            PositiveBase = new Color(70, 130, 90), PositiveHover = new Color(88, 158, 110), PositivePress = new Color(108, 182, 134),
            NegativeBase = new Color(160, 72, 72), NegativeHover = new Color(184, 90, 90), NegativePress = new Color(206, 112, 112),
            NeutralBase = new Color(224, 225, 230), NeutralHover = new Color(210, 212, 218), NeutralPress = new Color(198, 200, 208),

            ElevationShadow = new Color(0, 0, 0, 46),
        };

        public ThemeMode Mode { get; private set; } = ThemeMode.Dark;

        // ---- Live (current-theme) surface & accent tokens — design-guide.md
        // §1 is normative. These are mutable and re-populated by ApplyPalette
        // whenever the theme switches, so every call site that reads e.g.
        // Resources.StaticResources.Theme.SurfaceBackdrop always sees the
        // CURRENT theme without needing to know light/dark exists. One-off
        // tuning (a module's own per-section accents, a specific knob's
        // hand-picked face color) is still fine to keep local to its view.
        public Color SurfaceBackdrop, SurfacePanel, SurfaceHeader, SurfaceRaised, SurfaceBorder, BodyText;
        public Color MenuBarFillTop, MenuBarFillBottom;
        public Color AccentSelection, AccentLiveAutomation, AccentModPositive, AccentModNegative, AccentPlayhead, AccentMuteOn, AccentMuteOff, AccentWarning, AccentVolume, AccentPan;
        public Color CategorySource, CategoryEffects, CategoryModulation, CategoryUtility, CategoryComposite;
        public Color ElevationShadow;

        public TVFill PositiveButtonFill;
        public TVFill NegativeButtonFill;
        public TVFill NeutralButtonFill;

        public ButtonStates FruitMenuItemStates;
        public ButtonStates PositiveButtonStates;
        public ButtonStates NegativeButtonStates;
        public ButtonStates NeutralButtonStates;

        public Theme()
        {
            ApplyPalette(DarkPalette, ThemeMode.Dark);
        }

        /// <summary>Switches the live theme — every already-constructed
        /// element reading Theme's color fields picks up the new values on
        /// its next Draw, no rebuild required. design-guide.md §1.4: follows
        /// the OS setting by default, with a manual Preferences override.</summary>
        public void SetMode(ThemeMode mode)
        {
            ApplyPalette(mode == ThemeMode.Dark ? DarkPalette : LightPalette, mode);
        }

        private void ApplyPalette(Palette p, ThemeMode mode)
        {
            Mode = mode;

            SurfaceBackdrop = p.SurfaceBackdrop;
            SurfacePanel = p.SurfacePanel;
            SurfaceHeader = p.SurfaceHeader;
            SurfaceRaised = p.SurfaceRaised;
            SurfaceBorder = p.SurfaceBorder;
            BodyText = p.BodyText;

            MenuBarFillTop = p.MenuBarFillTop;
            MenuBarFillBottom = p.MenuBarFillBottom;

            AccentSelection = p.AccentSelection;
            AccentLiveAutomation = p.AccentLiveAutomation;
            AccentModPositive = p.AccentModPositive;
            AccentModNegative = p.AccentModNegative;
            AccentPlayhead = p.AccentPlayhead;
            AccentMuteOn = p.AccentMuteOn;
            AccentMuteOff = p.AccentMuteOff;
            AccentWarning = p.AccentWarning;
            AccentVolume = p.AccentVolume;
            AccentPan = p.AccentPan;

            CategorySource = p.CategorySource;
            CategoryEffects = p.CategoryEffects;
            CategoryModulation = p.CategoryModulation;
            CategoryUtility = p.CategoryUtility;
            CategoryComposite = p.CategoryComposite;

            ElevationShadow = p.ElevationShadow;

            PositiveButtonFill = new TVFillSimpleGradient(p.PositiveBase, p.PositiveBase * 0.75f, Direction.Vertically);
            NegativeButtonFill = new TVFillSimpleGradient(p.NegativeBase, p.NegativeBase * 0.75f, Direction.Vertically);
            NeutralButtonFill = new TVFillSimpleGradient(p.NeutralBase, p.NeutralBase * 0.75f, Direction.Vertically);

            FruitMenuItemStates = new ButtonStates
            {
                NormalFill = new TVFillSolidColor(Color.Transparent),
                HoveredFill = new TVFillSimpleGradient(mode == ThemeMode.Dark ? new Color(44, 44, 52) : new Color(224, 227, 233), mode == ThemeMode.Dark ? new Color(38, 38, 45) : new Color(210, 214, 222), Direction.Vertically),
                PressedFill = new TVFillSimpleGradient(mode == ThemeMode.Dark ? new Color(52, 52, 62) : new Color(208, 212, 222), mode == ThemeMode.Dark ? new Color(44, 44, 52) : new Color(194, 199, 210), Direction.Vertically)
            };

            PositiveButtonStates = new ButtonStates
            {
                NormalFill = new TVFillSimpleGradient(p.PositiveBase, p.PositiveBase * 0.75f, Direction.Vertically),
                HoveredFill = new TVFillSimpleGradient(p.PositiveHover, p.PositiveHover * 0.75f, Direction.Vertically),
                PressedFill = new TVFillSimpleGradient(p.PositivePress, p.PositivePress * 0.75f, Direction.Vertically)
            };

            NegativeButtonStates = new ButtonStates
            {
                NormalFill = new TVFillSimpleGradient(p.NegativeBase, p.NegativeBase * 0.75f, Direction.Vertically),
                HoveredFill = new TVFillSimpleGradient(p.NegativeHover, p.NegativeHover * 0.75f, Direction.Vertically),
                PressedFill = new TVFillSimpleGradient(p.NegativePress, p.NegativePress * 0.75f, Direction.Vertically)
            };

            NeutralButtonStates = new ButtonStates
            {
                NormalFill = new TVFillSimpleGradient(p.NeutralBase, p.NeutralBase * 0.75f, Direction.Vertically),
                HoveredFill = new TVFillSimpleGradient(p.NeutralHover, p.NeutralHover * 0.75f, Direction.Vertically),
                PressedFill = new TVFillSimpleGradient(p.NeutralPress, p.NeutralPress * 0.75f, Direction.Vertically)
            };
        }

        /// <summary>Standard corner radius for interactive chips (toolbar
        /// buttons, toggle pills, entries, popovers). Hard-edged-by-default
        /// policy (design-guide.md §4) — 0 means square, kept as a named token
        /// rather than a bare literal so a future deliberate change is one
        /// edit instead of a repo-wide find/replace.</summary>
        public const int ChipRadius = 0;

        /// <summary>Opacity for every full-screen view's own base background
        /// fill (the sequencer's and every <see cref="FullScreenModalElement"/>
        /// editor's) — multiply the fill color by this instead of using it at
        /// full opacity, e.g. <c>new TVFillSolidColor(color * Theme.
        /// ModalBackgroundAlpha)</c>. 2026-08-16: the decorative
        /// WindowElement background video's own draw cost dropped ~89% (see
        /// docs/video-playback-optimization.md), making it cheap enough to
        /// leave running behind every view instead of only the welcome
        /// screen — this is the shared "how much should subtly show
        /// through" knob, not a per-view judgment call.</summary>
        public const float ModalBackgroundAlpha = 0.93f;
    }

    public class ButtonStates
    {
        public TVFill NormalFill { get; set; }
        public TVFill HoveredFill { get; set; }
        public TVFill PressedFill { get; set; }
    }

    public class Icons
    {
        public String CloseIcon = Char.ConvertFromUtf32((int)UIFont.Symbol.Cancel);
        public String MinimizeIcon = Char.ConvertFromUtf32((int)UIFont.Symbol.BackToWindow);
        public String MaximizeIcon = Char.ConvertFromUtf32((int)UIFont.Symbol.FullScreen);
    }
}
