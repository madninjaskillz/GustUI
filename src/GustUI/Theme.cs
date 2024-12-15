using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI
{
    public class Theme
    {
        public TVFont UiFont = new() { Family = "segoeuisl.ttf", Size = 24, Border = 0 };
        public TVFont SymbolFont = new() { Family = "segmdl2.ttf", Size = 32, Border = 0 };
        public TVFont AltSymbolFont = new() { Family = "SegoeIcons.ttf", Size = 32, Border = 0 };

        public Icons Icons = new Icons();

        public TVFill PositiveButtonFill = new TVFillSimpleGradient(Color.DarkGreen, Color.DarkGreen * 0.5f, Direction.Vertically);
        public TVFill NegativeButtonFill = new TVFillSimpleGradient(Color.DarkRed, Color.DarkRed * 0.5f, Direction.Vertically);
        public TVFill NeutralButtonFill = new TVFillSimpleGradient(Color.Gray, Color.Gray * 0.5f, Direction.Vertically);

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
