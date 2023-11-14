using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
    }

    public class TVFillImage : TVFill
    {
        
        public Tiling Tiling { get; set; }
    }

    public class TVFillSolidColor : TVFill
    {
        public Color Color { get; set; }
        public TVFillSolidColor() { }
        public TVFillSolidColor(Color color) {
            Color = color;
        }
    }

    public class TVFillSimpleGradient : TVFill
    {
        public Color PrimaryColor { get; }
        public Color SecondaryColor { get; }
        public Direction Direction { get; }

        public TVFillSimpleGradient(GraphicsDevice graphicsDevice, Color primary, Color secondary, Direction direction)
        {
            int w = 1;
            int h = 1;
            if (direction == Direction.Horizontally)
            {
                w = 256;
            }
            else
            {
                h = 256;
            }

            Texture2D result = new Texture2D(graphicsDevice, w, h);
            Color[] c = new Color[256];

            Color col = primary;
            for (int i = 0; i < 256; i++)
            {
                c[i] = col;
                col=Color.Lerp(primary, secondary, i/255f);
            }

            result.SetData(c);
            this.Texture = result;
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
