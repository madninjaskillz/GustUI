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
        /// Draws the pointer. Called last in the frame, after the tree and the
        /// debug overlay, because a cursor is on top of everything by
        /// definition — including the things that draw on top of everything.
        /// </summary>
        internal static void Draw(DrawManager draw)
        {
            string wanted = thisFrame ?? DefaultCursor;
            thisFrame = null;

            if (!Ready || draw == null)
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
