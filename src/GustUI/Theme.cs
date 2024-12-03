using GustUI.TraitValues;
using System;
using System.Collections.Generic;
using System.Drawing;
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
    }

    public class Icons
    {
        public String CloseIcon = Char.ConvertFromUtf32((int)UIFont.Symbol.Cancel);
    }
}
