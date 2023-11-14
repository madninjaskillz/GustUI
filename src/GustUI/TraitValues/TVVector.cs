using GustUI.Extensions;
using Microsoft.Xna.Framework;
using System;

namespace GustUI.TraitValues;

public class TVVector : TraitValue
{
    public float X { get; set; }    
    public float Y { get; set; }
    public TVVector()
    {
        X = 0f;
        Y = 0f;
    }
    public TVVector(float x, float y)
    {
        this.X = x;
        this.Y = y;
    }

    public override string ToString()
    {
        return $"x: {X}, Y:{Y}";
    }
    public static TVVector operator +(TVVector a, TVVector b) => new TVVector(a.X + b.X, a.Y + b.Y);

    public static TVVector operator -(TVVector a, TVVector b) => new TVVector(a.X - b.X, a.Y - b.Y);

    public Vector2 AsXna => new Vector2(X, Y);

    public Rectangle Rectangle(TVVector size) => new Rectangle(this.X.AsInt(), this.Y.AsInt(), size.X.AsInt(), size.Y.AsInt());
}


