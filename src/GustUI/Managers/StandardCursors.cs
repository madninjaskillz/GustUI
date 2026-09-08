namespace GustUI.Managers
{
    /// <summary>
    /// The cursor names GustUI's OWN controls ask for (ezmuze #224).
    ///
    /// GustUI still owns no art — see <see cref="CursorManager"/>. What it
    /// owns is a handful of pointers that belong to the toolkit rather than
    /// to any app: a window resize handle means "resize" in every program
    /// ever written, and a knob means "drag me up and down" whether the app
    /// is a DAW or a spreadsheet. Naming those here lets the controls that
    /// draw them say so once, instead of every app re-wiring the same eight
    /// handles.
    ///
    /// A NAME IS A REQUEST, NOT A REQUIREMENT. An app whose atlas has no cell
    /// called "ResizeHorizontal" gets <see cref="CursorManager.DefaultCursor"/>
    /// there, which is the plain arrow it would have had anyway — so an app
    /// can register three cursors, or none, and nothing here breaks. These
    /// deliberately match the conventional names an OS cursor set uses, so an
    /// atlas drawn without ever reading this file is likely to satisfy them
    /// by accident.
    ///
    /// App-specific pointers do NOT belong here. "Trim the front of a clip"
    /// is not a toolkit idea; that name lives with the app that knows what a
    /// clip is.
    /// </summary>
    public static class StandardCursors
    {
        /// <summary>A vertical edge: dragging changes width.</summary>
        public const string ResizeHorizontal = "ResizeHorizontal";

        /// <summary>A horizontal edge: dragging changes height.</summary>
        public const string ResizeVertical = "ResizeVertical";

        /// <summary>The top-left / bottom-right diagonal.</summary>
        public const string ResizeNorthWestSouthEast = "ResizeNorthWestSouthEast";

        /// <summary>The top-right / bottom-left diagonal.</summary>
        public const string ResizeNorthEastSouthWest = "ResizeNorthEastSouthWest";

        /// <summary>Something that will move as a whole when dragged.</summary>
        public const string Move = "Move";

        /// <summary>A control that responds to a press — a button, a menu
        /// row, a link.</summary>
        public const string PointingHand = "PointingHand";

        /// <summary>Text that can be selected or typed into.</summary>
        public const string TextSelect = "TextSelect";

        /// <summary>A value dragged rather than clicked — a knob, a fader, a
        /// numeric field being scrubbed.</summary>
        public const string Precision = "Precision";

        /// <summary>A drop that will be refused.</summary>
        public const string Forbidden = "Forbidden";
    }
}
