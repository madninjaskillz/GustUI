using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI.TraitValues
{
    public class TVBorderFill : TraitValue
    {
    }

    public class TVBorderColorFill : TVBorderFill
    {
        public Color Color { get; set; }
        public TVBorderColorFill() { }
        public TVBorderColorFill(Color color)
        {
            Color = color;
        }

        public TVBorderColorFill(TVColor color)
        {
            Color = color.AsXna;
        }
    }

    public class TVBorder9Grid : TVBorderFill
    {
        public float Opacity { get; set; } = 0.25f;
        public int NineGridSize { get; set; } = 32;
        public bool TopLeft { get; set; } = true;
        public bool TopCenter { get; set; } = true;
        public bool TopRight { get; set; } = true;
        public bool MiddleLeft { get; set; }= true;
        public bool MiddleRight { get; set; } = true;
        public bool BottomLeft { get; set; }= true;
        public bool BottomCenter { get; set; } = true;
        public bool BottomRight { get; set; }= true;
    }
}
