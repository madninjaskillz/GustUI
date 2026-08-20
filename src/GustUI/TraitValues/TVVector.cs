using GustUI.Extensions;
using Microsoft.Xna.Framework;
using System;

namespace GustUI.TraitValues;

public class TVVector : TraitValue
{
    // init-only (not set): PositionTrait/SizeTrait's change event
    // (Trait<T>.Set -> ValueChangedEventHandler) only fires when Set()
    // replaces the stored instance — TVVector being a mutable reference
    // type meant `var p = trait.Value(); p.X = ...;` silently mutated the
    // trait's own stored object in place, bypassing Set() and the change
    // event entirely (found 2026-08-20 designing a Draw-time visibility
    // cache keyed on that event). init blocks that pattern at compile
    // time: every call site must go through Set(new TVVector(...)) to
    // change a position/size, which is what makes the event reliable.
    public float X { get; init; }
    public float Y { get; init; }
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

    public TVVector(Vector2 vector2)
    {
        this.X = vector2.X;
        this.Y = vector2.Y; 
    }

    public override string ToString()
    {
        return $"x: {X}, Y:{Y}";
    }

    // Value equality makes Trait.Set a no-op (no stored-instance swap, no
    // changed-event) when an equal vector is assigned — several per-frame
    // callers Set a freshly allocated equal value every tick.
    public override bool Equals(object obj) => obj is TVVector other && other.X == X && other.Y == Y;

    public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
    public static TVVector operator +(TVVector a, TVVector b) => new TVVector(a.X + b.X, a.Y + b.Y);

    public static TVVector operator -(TVVector a, TVVector b) => new TVVector(a.X - b.X, a.Y - b.Y);

    public Vector2 AsXna => new Vector2(X, Y);

    public Rectangle Rectangle(TVVector size) => new Rectangle(this.X.AsInt(), this.Y.AsInt(), size.X.AsInt(), size.Y.AsInt());
}


