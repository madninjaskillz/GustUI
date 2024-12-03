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
        public TVFont UiFont = new TVFont() { Family = "segoeuisl.ttf", Size = 32, Border = 0 };
        public TVFont SymbolFont = new TVFont() { Family = "segmdl2.ttf", Size = 32, Border = 0 };
        public TVFont AltSymbolFont = new TVFont() { Family = "SegoeIcons.ttf", Size = 32, Border = 0 };

        public Icons Icons = new Icons();

        public TVFill PositiveButtonFill = new TVFillSimpleGradient(Color.DarkGreen, Color.DarkGreen * 0.5f, Direction.Vertically);
        public TVFill NegativeButtonFill = new TVFillSimpleGradient(Color.DarkRed, Color.DarkRed * 0.5f, Direction.Vertically);
        public TVFill NeutralButtonFill = new TVFillSimpleGradient(Color.Gray, Color.Gray * 0.5f, Direction.Vertically);

    }

    public class Icons
    {
        public String CloseIcon = Char.ConvertFromUtf32((int)UIFont.Symbol.Cancel);
    }
}
