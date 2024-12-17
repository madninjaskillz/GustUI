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

        public TVFill PositiveButtonFill = new TVFillSimpleGradient(Color.DarkGreen, Color.DarkGreen * 0.5f, Direction.Vertically);
        public TVFill NegativeButtonFill = new TVFillSimpleGradient(Color.DarkRed, Color.DarkRed * 0.5f, Direction.Vertically);
        public TVFill NeutralButtonFill = new TVFillSimpleGradient(Color.Gray, Color.Gray * 0.5f, Direction.Vertically);

        public ButtonStates FruitMenuItemStates = new ButtonStates
        {
            NormalFill = new TVFillSolidColor(Color.Transparent),
            HoveredFill = new TVFillSimpleGradient(Color.DarkGray * 0.5f, Color.DarkGray * 0.8f, Direction.Vertically),
            PressedFill = new TVFillSimpleGradient(Color.DarkGray * 0.7f, Color.DarkGray * 0.9f, Direction.Vertically)
        };

        public ButtonStates PositiveButtonStates = new ButtonStates
        {
            NormalFill = new TVFillSimpleGradient(Color.DarkGreen, Color.DarkGreen * 0.5f, Direction.Vertically),
            HoveredFill = new TVFillSimpleGradient(Color.Green, Color.Green * 0.5f, Direction.Vertically),
            PressedFill = new TVFillSimpleGradient(Color.LightGreen, Color.LightGreen * 0.5f, Direction.Vertically)
        };

        public ButtonStates NegativeButtonStates = new ButtonStates
        {
            NormalFill = new TVFillSimpleGradient(Color.DarkRed, Color.DarkRed * 0.5f, Direction.Vertically),
            HoveredFill = new TVFillSimpleGradient(Color.Red, Color.Red * 0.5f, Direction.Vertically),
            PressedFill = new TVFillSimpleGradient(Color.LightCoral, Color.LightCoral * 0.5f, Direction.Vertically)
        };

        public ButtonStates NeutralButtonStates = new ButtonStates
        {
            NormalFill = new TVFillSimpleGradient(Color.Gray, Color.Gray * 0.5f, Direction.Vertically),
            HoveredFill = new TVFillSimpleGradient(Color.Silver, Color.Silver * 0.5f, Direction.Vertically),
            PressedFill = new TVFillSimpleGradient(Color.LightGray, Color.LightGray * 0.5f, Direction.Vertically)
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
