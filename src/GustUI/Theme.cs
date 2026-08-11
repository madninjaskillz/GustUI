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
    public class Theme
    {
        private const float NineGridOpacity = 0.25f;
        public TVFont UiFont = new() { Family = "segoeuisl.ttf", Size = 24, Border = 0 };
        public TVFont UiFontLarge = new() { Family = "segoeuisl.ttf", Size = 48, Border = 0 };
        public TVFont UiFontSmall = new() { Family = "segoeuisl.ttf", Size = 12, Border = 0 };

        /// <summary>Segoe UI Bold (segoeuib.ttf) — the app's ONLY other font
        /// weight (everything else is Semilight): titles/headers that need
        /// to visually anchor a surface, used sparingly so weight actually
        /// signals hierarchy instead of becoming noise.</summary>
        public TVFont UiFontBold = new() { Family = "segoeuib.ttf", Size = 24, Border = 0 };
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

        // Muted, dark-DAW-appropriate button palette (Reaper/Ableton lean on
        // desaturated neutrals with a small accent, not stoplight primaries)
        // — replaces the old Color.Green/Red/Gray "toolbar icon" defaults.
        private static readonly Color PositiveBase = new Color(58, 108, 74);
        private static readonly Color PositiveHover = new Color(74, 138, 94);
        private static readonly Color PositivePress = new Color(96, 168, 116);
        private static readonly Color NegativeBase = new Color(120, 58, 58);
        private static readonly Color NegativeHover = new Color(158, 72, 72);
        private static readonly Color NegativePress = new Color(196, 92, 92);
        private static readonly Color NeutralBase = new Color(52, 52, 60);
        private static readonly Color NeutralHover = new Color(66, 66, 76);
        private static readonly Color NeutralPress = new Color(80, 80, 92);

        public TVFill PositiveButtonFill = new TVFillSimpleGradient(PositiveBase, PositiveBase * 0.75f, Direction.Vertically);
        public TVFill NegativeButtonFill = new TVFillSimpleGradient(NegativeBase, NegativeBase * 0.75f, Direction.Vertically);
        public TVFill NeutralButtonFill = new TVFillSimpleGradient(NeutralBase, NeutralBase * 0.75f, Direction.Vertically);

        public ButtonStates FruitMenuItemStates = new ButtonStates
        {
            NormalFill = new TVFillSolidColor(Color.Transparent),
            HoveredFill = new TVFillSimpleGradient(new Color(44, 44, 52), new Color(38, 38, 45), Direction.Vertically),
            PressedFill = new TVFillSimpleGradient(new Color(52, 52, 62), new Color(44, 44, 52), Direction.Vertically)
        };

        public ButtonStates PositiveButtonStates = new ButtonStates
        {
            NormalFill = new TVFillSimpleGradient(PositiveBase, PositiveBase * 0.75f, Direction.Vertically),
            HoveredFill = new TVFillSimpleGradient(PositiveHover, PositiveHover * 0.75f, Direction.Vertically),
            PressedFill = new TVFillSimpleGradient(PositivePress, PositivePress * 0.75f, Direction.Vertically)
        };

        public ButtonStates NegativeButtonStates = new ButtonStates
        {
            NormalFill = new TVFillSimpleGradient(NegativeBase, NegativeBase * 0.75f, Direction.Vertically),
            HoveredFill = new TVFillSimpleGradient(NegativeHover, NegativeHover * 0.75f, Direction.Vertically),
            PressedFill = new TVFillSimpleGradient(NegativePress, NegativePress * 0.75f, Direction.Vertically)
        };

        public ButtonStates NeutralButtonStates = new ButtonStates
        {
            NormalFill = new TVFillSimpleGradient(NeutralBase, NeutralBase * 0.75f, Direction.Vertically),
            HoveredFill = new TVFillSimpleGradient(NeutralHover, NeutralHover * 0.75f, Direction.Vertically),
            PressedFill = new TVFillSimpleGradient(NeutralPress, NeutralPress * 0.75f, Direction.Vertically)
        };
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
