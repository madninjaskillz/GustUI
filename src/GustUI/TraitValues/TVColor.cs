using Microsoft.Xna.Framework;

namespace GustUI.TraitValues;

public class TVColor : TraitValue
{
    public TVColor() : this(Color.White)
    {
    }

    public TVColor(byte red, byte green, byte blue, byte alpha) : this(new Color(red, green, blue, alpha)) { }

    public TVColor(Color color)
    {
        AsXna = color;
    }

    public byte Red { get => AsXna.R; set => AsXna = new Color(value, AsXna.G, AsXna.B, AsXna.A); }
    public byte Green { get => AsXna.G; set => AsXna = new Color(AsXna.R,value, AsXna.B, AsXna.A); }
    public byte Blue { get => AsXna.B; set => AsXna = new Color(AsXna.R, AsXna.G, value, AsXna.A); }
    public byte Alpha { get => AsXna.A; set => AsXna = new Color(AsXna.R, AsXna.G, AsXna.B, value); }

    public Color AsXna { get; set; }

}


