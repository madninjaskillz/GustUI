using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace GustUI.Managers
{
    /// <summary>
    /// The mouse pointer, drawn by us rather than by the OS.
    ///
    /// WHY DRAW IT. A DAW wants dozens of pointers — trim, stretch, razor,
    /// scrub, fade, pencil — and the system cursor set has about ten, none of
    /// which say "you are on the front edge of a clip and dragging will crop
    /// it". Hiding the real cursor and drawing a quad at its position makes
    /// the pointer just another sprite, so a new one costs an atlas cell
    /// rather than a platform API.
    ///
    /// GUSTUI OWNS THE MECHANISM AND NONE OF THE ART. This class knows how to
    /// draw a rectangle of a texture at the pointer, offset by a hotspot; it
    /// has no opinion about what any cursor looks like or what the names mean.
    /// The app registers an atlas and asks for cursors by name — see
    /// <see cref="Register"/>.
    ///
    /// THE COST, stated plainly: a drawn cursor lags the real pointer by a
    /// frame, where an OS cursor never does. At 90–120 fps that is under ten
    /// milliseconds and reads as normal; on a badly stalled frame it is
    /// visible, and it is the first thing to blame if the pointer ever feels
    /// heavy.
    /// </summary>
    public static class CursorManager
    {
        /// <summary>The cursor drawn when nothing has asked for another one.
        /// A name the app has not registered simply falls back to this, so a
        /// typo costs a wrong pointer rather than an invisible one.</summary>
        public const string DefaultCursor = "Default";

        private static Texture2D atlas;
        private static readonly Dictionary<string, Rectangle> cells =
            new Dictionary<string, Rectangle>(StringComparer.OrdinalIgnoreCase);

        private static Vector2 hotspot;
        private static string thisFrame;

        /// <summary>Whether an atlas has been registered — until it is,
        /// nothing draws and the app should leave the system cursor on.</summary>
        public static bool Ready => atlas != null && cells.Count > 0;

        /// <summary>
        /// The size the art is drawn at to look "normal" — the app's own
        /// atlas is authored above pointer size so it can be scaled DOWN
        /// cleanly, and this is the factor that undoes that.
        ///
        /// Separate from <see cref="Scale"/> so a user preference can be a
        /// plain multiplier of 1 rather than having to know what the art was
        /// drawn at.
        /// </summary>
        public static float AuthoredScale { get; set; } = 0.75f;

        /// <summary>
        /// How big the pointer is actually drawn. Usually
        /// <see cref="AuthoredScale"/> times whatever the user asked for —
        /// some people want a much bigger cursor, and hiding the system one
        /// means the system's own setting no longer reaches us.
        /// </summary>
        public static float Scale { get; set; } = 0.75f;

        /// <summary>Draws a dot at the exact pointer position alongside the
        /// cursor — for checking that the hotspot lands where it should.</summary>
        public static bool DebugHotspot { get; set; }

        /// <summary>
        /// Stop drawing the pointer, and leave the real one to it.
        ///
        /// Set while the window is not focused. An unfocused window has no
        /// business drawing a pointer at all — the OS one is over whatever
        /// the person is actually using — and the mouse state it reports is
        /// not to be trusted: alt-tab away and it reads (0,0), so ours
        /// jumped to the top-left corner and looked for all the world like
        /// something had moved the mouse.
        /// </summary>
        public static bool Suppressed { get; set; }

        /// <summary>
        /// Draw the pointer even while <see cref="Suppressed"/> — for a
        /// screenshot taken from outside the app (ezmuze #224).
        ///
        /// WHY THIS HAS TO EXIST. Suppression is keyed on window focus, and a
        /// tool driving the app over an API is by definition not focusing its
        /// window: the whole point is that it does not need a human at the
        /// keyboard. So the one thing the app draws entirely itself was the
        /// one thing a screenshot could never show, and a change to a cursor
        /// could not be verified the way every other visual change here is.
        /// Setting this makes the pointer appear in a capture of an unfocused
        /// window, which is exactly the frame such a tool grabs.
        ///
        /// It does NOT unhide the system cursor, and it does not touch the
        /// focus rule: the reason for suppression — that an inactive window
        /// reports (0,0) and would fling our pointer into the corner — still
        /// stands, and this is a caller saying it wants the drawn pointer
        /// anyway, wherever it lands. Off by default; a real session never
        /// sets it.
        /// </summary>
        public static bool DrawWhileSuppressed { get; set; }

        /// <summary>
        /// Registers the app's cursor art: one texture, a cell rectangle per
        /// name, and the hotspot shared by all of them — the point in a cell
        /// that sits ON the pointer position.
        ///
        /// One hotspot rather than one per cursor because that is how the art
        /// is drawn: an arrow's tip, a hand's fingertip and a crosshair's
        /// centre are all placed at the same spot in their cell, so the sheet
        /// only has to say it once.
        /// </summary>
        public static void Register(Texture2D texture, IDictionary<string, Rectangle> cursorCells, Vector2 cellHotspot)
        {
            cells.Clear();
            if (texture == null || cursorCells == null)
            {
                atlas = null;
                return;
            }

            foreach (KeyValuePair<string, Rectangle> pair in cursorCells)
            {
                cells[pair.Key] = pair.Value;
            }

            atlas = texture;
            hotspot = cellHotspot;
        }

        /// <summary>
        /// Asks for a cursor FOR THIS FRAME. Nothing sticks: the request is
        /// cleared once the frame is drawn, so an element that stops asking
        /// stops getting it without having to say so.
        ///
        /// Last caller wins. Deliberately not a depth contest — the only
        /// things that ask are doing their own hit-testing, and they know
        /// what is on top far better than a shared tie-breaker would. If two
        /// unrelated callers ever fight over the same pixel, that is the
        /// point to give this a proper resolution rule rather than now.
        /// </summary>
        public static void Use(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                thisFrame = name;
            }
        }

        /// <summary>What will be drawn this frame — the last request, or the
        /// default.</summary>
        public static string Current => thisFrame ?? DefaultCursor;

        /// <summary>
        /// The cursor the last frame actually DREW.
        ///
        /// <see cref="Current"/> is the request being accumulated, and it is
        /// cleared by every <see cref="Draw"/> — so anything asking from
        /// outside the frame (a control API answering "what pointer is on
        /// screen") reads it at the wrong moment and gets the default back.
        /// This is the settled answer, and it is the one worth asserting on.
        /// </summary>
        public static string LastDrawn { get; private set; } = DefaultCursor;

        /// <summary>How many cursors the registered atlas holds — 0 until one
        /// is registered.</summary>
        public static int Count => cells.Count;

        /// <summary>
        /// Draws the pointer. Called last in the frame, after the tree and the
        /// debug overlay, because a cursor is on top of everything by
        /// definition — including the things that draw on top of everything.
        /// </summary>
        internal static void Draw(DrawManager draw)
        {
            string wanted = thisFrame ?? DefaultCursor;
            thisFrame = null;
            LastDrawn = wanted;

            if (!Ready || (Suppressed && !DrawWhileSuppressed) || draw == null)
            {
                return;
            }

            if (!cells.TryGetValue(wanted, out Rectangle cell)
                && !cells.TryGetValue(DefaultCursor, out cell))
            {
                return;
            }

            MouseState mouse = Resources.StaticResources.InputManager.CurrentMouseState;
            float scale = Scale;

            // ROUNDED, not truncated. The offset is the hotspot scaled, and
            // truncating it biases the whole pointer down and right by up to
            // a pixel — small, but it is a pixel of "the tip is not quite
            // where I am pointing", which is the one thing a cursor has to
            // get right.
            if (DebugHotspot)
            {
                // A 3px dot at the EXACT pointer position, drawn by this same
                // call in this same space — so a screenshot compares the art
                // against ground truth instead of against my arithmetic.
                draw.Draw(
                    atlas,
                    new Rectangle(mouse.X - 1, mouse.Y - 1, 3, 3),
                    new Rectangle(cell.X + 20, cell.Y + 20, 1, 1),
                    Color.Red);
            }

            draw.Draw(
                atlas,
                new Rectangle(
                    mouse.X - (int)MathF.Round(hotspot.X * scale),
                    mouse.Y - (int)MathF.Round(hotspot.Y * scale),
                    (int)MathF.Round(cell.Width * scale),
                    (int)MathF.Round(cell.Height * scale)),
                cell,
                Color.White);
        }
    }
}
