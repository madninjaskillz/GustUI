namespace GustUI.Elements
{
    /// <summary>
    /// How a value control PAINTS ITSELF — the knob's cap, the slider's track,
    /// the switch's pill. Not what it does: every skin keeps every affordance
    /// (pointer, modulation arc, ghost pointer, live marker, drag behaviour)
    /// and only the surface underneath them changes.
    ///
    /// Why an enum on the element rather than a bag of colours: the difference
    /// between a flat disc and a machined aluminium cap is not a palette, it is
    /// a different set of SHAPES — a shadow, a bezel, a radial shade, a ring of
    /// lit ticks. Those cannot be expressed as "FaceColor but darker", and the
    /// alternative to naming them is the caller assembling a knob out of
    /// primitives, which is how two panels end up with two subtly different
    /// knobs.
    ///
    /// Deliberately SMALL and closed. Three skins cover what a module panel
    /// actually asks for (the app's own look, soft plastic, studio hardware),
    /// and each one has to be drawn three times over — knob, slider, switch —
    /// so a fourth is real work rather than a line of config. Bitmap art is
    /// still the escape hatch for anything beyond them
    /// (<see cref="KnobElement.FaceTexture"/>).
    /// </summary>
    public enum ControlSkin
    {
        /// <summary>The app's own look: a flat disc, a thin track, a plain
        /// pill. The default, and what every control drew before skins
        /// existed.</summary>
        Flat = 0,

        /// <summary>
        /// Soft plastic, in the "neomorphic" idiom: the control is the same
        /// material as the panel behind it, raised by a light shadow on one
        /// side and a highlight on the other, with the value read as an accent
        /// arc or fill rather than a hard mark.
        ///
        /// Needs a MID-TONE panel behind it to work — the shadow and the
        /// highlight are what separate the shape from its background, and on
        /// black there is nothing to shade against.
        /// </summary>
        Soft = 1,

        /// <summary>
        /// Studio hardware: a dark machined cap with a metal bezel, a ring of
        /// ticks with the travelled ones lit, an inset track with a milled
        /// thumb. The rack-gear look, and the one that reads best on a dark
        /// panel.
        /// </summary>
        Hardware = 2,
    }
}
