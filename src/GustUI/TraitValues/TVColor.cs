using System;
using Microsoft.Xna.Framework;

namespace GustUI.TraitValues;

public class TVColor : TraitValue
{
    /// <summary>
    /// Set when this colour is LIVE — re-read on every access rather than
    /// captured once. Null for an ordinary fixed colour.
    ///
    /// <see cref="TVFillSolidColor"/> has had this since the theme work, which
    /// is why backgrounds follow a theme switch. Text colours go through this
    /// class and did not, so every label captured whatever the palette said at
    /// the moment its element was built and kept it forever: switching to Light
    /// left the menu bar, the channel names and every open dialog painted in
    /// dark-mode text on light surfaces (ezmuze bug board #66).
    /// </summary>
    private readonly Func<Color> colorFunc;

    private Color stored;

    public TVColor() : this(Color.White)
    {
    }

    public TVColor(byte red, byte green, byte blue, byte alpha) : this(new Color(red, green, blue, alpha)) { }

    public TVColor(Color color)
    {
        stored = color;
    }

    /// <summary>
    /// A colour that is re-read every time it is used — pass
    /// <c>() =&gt; Theme.BodyText</c> rather than <c>Theme.BodyText</c> and the
    /// element follows the palette instead of freezing at the value it was
    /// built with.
    /// </summary>
    public TVColor(Func<Color> color)
    {
        colorFunc = color;
    }

    public byte Red { get => AsXna.R; set => AsXna = new Color(value, AsXna.G, AsXna.B, AsXna.A); }
    public byte Green { get => AsXna.G; set => AsXna = new Color(AsXna.R,value, AsXna.B, AsXna.A); }
    public byte Blue { get => AsXna.B; set => AsXna = new Color(AsXna.R, AsXna.G, value, AsXna.A); }
    public byte Alpha { get => AsXna.A; set => AsXna = new Color(AsXna.R, AsXna.G, AsXna.B, value); }

    /// <summary>The colour now. A plain field read for a fixed colour — this is
    /// touched once per drawn glyph, so the delegate is only ever invoked for
    /// the colours that actually asked to be live.</summary>
    public Color AsXna
    {
        get => colorFunc == null ? stored : colorFunc();

        // Assigning to a LIVE colour does nothing, deliberately: the function is
        // the source of truth and silently overwriting it with a stale constant
        // is how this bug comes back. Set a new TVColor instead.
        set => stored = value;
    }

    /// <summary>True when this colour re-reads itself. Used by
    /// <see cref="Equals"/>, which must not let the no-op optimisation below
    /// discard a live colour.</summary>
    public bool IsLive => colorFunc != null;

    // Value equality: assigning an equal color through Trait.Set is a no-op.
    //
    // A LIVE colour is never equal to anything, even another live one that
    // happens to agree right now. Without that, installing `() => BodyText`
    // over a captured BodyText would compare equal — the two ARE the same
    // colour at that instant — and the assignment would be skipped, leaving the
    // element frozen and the fix silently doing nothing. Comparing what a
    // colour is worth today cannot answer whether it will change tomorrow.
    public override bool Equals(object obj) =>
        obj is TVColor other && !other.IsLive && !IsLive && other.AsXna == AsXna;

    public override int GetHashCode() => (int)AsXna.PackedValue;
}
