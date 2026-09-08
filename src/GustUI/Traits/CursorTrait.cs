using GustUI.TraitValues;

namespace GustUI.Traits
{
    /// <summary>
    /// The mouse pointer this element wants while it is hovered — a cursor
    /// name from whatever atlas the app registered with
    /// <see cref="Managers.CursorManager"/> (ezmuze bug board #224).
    ///
    /// DECLARATIVE, because the alternative does not scale. The only way to
    /// change the pointer used to be an imperative
    /// <see cref="Managers.CursorManager.Use"/> from inside a per-frame hover
    /// handler, so every affordance that wanted its own pointer cost a
    /// handler, and an app carrying two hundred cursors wired up three of
    /// them. A resize handle knows at construction that it is a resize
    /// handle; saying so once is the whole feature.
    ///
    /// <see cref="Managers.InputManager"/> reads this from the hover pass it
    /// already runs, and the FRONT-MOST hovered element wins — the same
    /// last-drawn-wins rule the pointer follows everywhere else.
    ///
    /// STILL IMPERATIVE FOR REGIONS. An element with several pointers inside
    /// one rectangle — a sequencer clip, whose four edge zones are 5px strips
    /// of a block, not elements — keeps calling <c>CursorManager.Use</c> from
    /// its own hit-testing. The trait is for the common case, where the thing
    /// with the pointer is the thing with the bounds; it is not a replacement
    /// for knowing where inside yourself the pointer is.
    ///
    /// An empty or unregistered name falls back to the default pointer, so a
    /// typo costs a plain arrow rather than an invisible one.
    /// </summary>
    public class CursorTrait : Trait<TVText>
    {
    }
}
