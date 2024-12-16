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
        public TVFont SymbolFont = new() { Family = "segmdl2.ttf", Size = 24, Border = 0 };
        public TVFont AltSymbolFont = new() { Family = "SegoeIcons.ttf", Size = 24, Border = 0 };

        public Icons Icons = new Icons();

        public TVFillImage NineGridTopLeft = new TVFillImage { Texture = Resources.StaticResources.Content.Load<Texture2D>("9grid/tl"), Opacity = NineGridOpacity };
        public TVFillImage NineGridTop = new TVFillImage { Texture = Resources.StaticResources.Content.Load<Texture2D>("9grid/t"), Opacity = NineGridOpacity };
        public TVFillImage NineGridTopRight = new TVFillImage { Texture = Resources.StaticResources.Content.Load<Texture2D>("9grid/tr"), Opacity = NineGridOpacity };
        public TVFillImage NineGridLeft = new TVFillImage { Texture = Resources.StaticResources.Content.Load<Texture2D>("9grid/l"), Opacity = NineGridOpacity };
        public TVFillImage NineGridRight = new TVFillImage { Texture = Resources.StaticResources.Content.Load<Texture2D>("9grid/r"), Opacity = NineGridOpacity };
        public TVFillImage NineGridBottomLeft = new TVFillImage { Texture = Resources.StaticResources.Content.Load<Texture2D>("9grid/bl"), Opacity = NineGridOpacity };
        public TVFillImage NineGridBottom = new TVFillImage { Texture = Resources.StaticResources.Content.Load<Texture2D>("9grid/b"), Opacity = NineGridOpacity };
        public TVFillImage NineGridBottomRight = new TVFillImage { Texture = Resources.StaticResources.Content.Load<Texture2D>("9grid/br"), Opacity = NineGridOpacity };

        public TVFill PositiveButtonFill = new TVFillSimpleGradient(Color.DarkGreen, Color.DarkGreen * 0.5f, Direction.Vertically);
        public TVFill NegativeButtonFill = new TVFillSimpleGradient(Color.DarkRed, Color.DarkRed * 0.5f, Direction.Vertically);
        public TVFill NeutralButtonFill = new TVFillSimpleGradient(Color.Gray, Color.Gray * 0.5f, Direction.Vertically);

        public ButtonStates FruitMenuItemStates = new ButtonStates
        {
            NormalFill = new TVFillSolidColor(Color.White),
            HoveredFill = new TVFillSimpleGradient(Color.LightGray, Color.DarkGray, Direction.Vertically),
            PressedFill = new TVFillSimpleGradient(Color.DarkGray, Color.Black, Direction.Vertically)
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
    }
}
