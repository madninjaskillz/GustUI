using System;
using System.Collections.Generic;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GustUI.Elements
{
    /// <summary>One note on a <see cref="PianoRollElement"/> (a display view —
    /// the host owns the real model and syncs this list after every edit).</summary>
    public class PianoRollNoteView
    {
        public Guid Id;

        /// <summary>MIDI-style pitch as a float (fractional = between rows).</summary>
        public float Pitch;

        public double StartBeats;
        public double LengthBeats;
        public float Velocity = 1f;
        public bool Selected;

        /// <summary>
        /// Semitone offsets sampled UNIFORMLY across the note (the host
        /// samples its bend curve); null/empty = straight note. When present
        /// the note block renders BENT: its body follows Pitch + offset.
        /// </summary>
        public float[] BendOffsets;
    }

    /// <summary>
    /// The piano-roll editing surface: semitone rows (piano-key shading),
    /// beat/bar grid, note blocks — bent notes draw as curved geometry
    /// following their pitch offsets — plus a playhead line and a bend
    /// handle on selected notes. Immediate-mode in Draw() (the KnobElement
    /// pattern; measured guidance in WaveformElement's header applies:
    /// per-column rects only where a note actually bends).
    ///
    /// The element is a DUMB SURFACE: the host sets the display lists and
    /// mapping domain (BeatsVisible / TopPitch / RowHeight), wires the mouse
    /// traits and runs the interaction state machine itself — the same
    /// division of labor as the sequencer's body panel.
    ///
    /// Mapping (mirrored from the host's pure view math):
    ///   x = beats / BeatsVisible × width;  y = (TopPitch − pitch) × RowHeight.
    /// </summary>
    [ElementTraits(typeof(PositionTrait), typeof(SizeTrait),
                   typeof(OnMousePress), typeof(OnMouseButtonHeldDown), typeof(OnMouseRelease))]
    public class PianoRollElement : Element
    {
        /// <summary>Notes to draw (host-owned; replaced/edited after model changes).</summary>
        public List<PianoRollNoteView> Notes = new List<PianoRollNoteView>();

        /// <summary>An uncommitted note being created by a drag, drawn like a
        /// note but slightly translucent. Null when idle.</summary>
        public PianoRollNoteView GhostNote;

        /// <summary>Horizontal domain: the whole pattern width in beats.</summary>
        public double BeatsVisible = 16;

        public int BeatsPerBar = 4;

        /// <summary>Drawn grid subdivision in beats (also the host's snap).</summary>
        public double GridBeats = 0.25;

        /// <summary>Pitch whose row TOP sits at y = 0 (vertical scroll).</summary>
        public int TopPitch = 96;

        public float RowHeight = 14f;

        /// <summary>Playhead position in beats; negative hides it.</summary>
        public double PlayheadBeats = -1;

        /// <summary>Radius of the bend-handle grip drawn on selected notes.</summary>
        public const float BendHandleSize = 8f;

        // House-style hard-coded dark defaults, host-overridable.
        public Color BackColor = new Color(16, 16, 21);
        public Color WhiteRowColor = new Color(26, 26, 33);
        public Color BlackRowColor = new Color(20, 20, 26);
        public Color RowLineColor = new Color(34, 34, 42);
        public Color OctaveLineColor = new Color(52, 52, 64);
        public Color GridLineColor = new Color(30, 30, 38);
        public Color BeatLineColor = new Color(42, 42, 52);
        public Color BarLineColor = new Color(64, 64, 78);
        public Color NoteColor = new Color(110, 145, 235);
        public Color NoteSelectedColor = new Color(170, 195, 255);
        public Color NoteBorderColor = new Color(16, 20, 40);
        public Color BendHandleColor = new Color(255, 210, 120);
        public Color PlayheadColor = new Color(255, 90, 90);

        public float XForBeat(double beats, float width)
            => BeatsVisible <= 0 ? 0f : (float)(beats / BeatsVisible * width);

        public float YTopForPitch(double pitch)
            => (float)((TopPitch - pitch) * RowHeight);

        public override void Draw()
        {
            var manager = Resources.StaticResources.DrawManager;
            Vector2 pos = this.GetActualXnaPosition();
            Vector2 size = this.GetSize().AsXna;
            int width = (int)size.X;
            int height = (int)size.Y;
            if (width < 8 || height < 8)
            {
                base.Draw();
                return;
            }

            int x0 = (int)pos.X;
            int y0 = (int)pos.Y;
            manager.DrawFilledRectangle(new Rectangle(x0, y0, width, height), BackColor);

            // ---- semitone rows (piano shading + row/octave lines) ----
            int firstRow = 0;
            int lastRow = (int)(height / RowHeight) + 1;
            for (int r = firstRow; r <= lastRow; r++)
            {
                int pitch = TopPitch - r;
                int top = y0 + (int)(r * RowHeight);
                int rowH = (int)((r + 1) * RowHeight) - (int)(r * RowHeight);
                if (top >= y0 + height)
                {
                    break;
                }

                rowH = Math.Min(rowH, y0 + height - top);
                bool black = IsBlackKey(pitch);
                manager.DrawFilledRectangle(new Rectangle(x0, top, width, rowH), black ? BlackRowColor : WhiteRowColor);
                bool octave = ((pitch % 12) + 12) % 12 == 0; // line under B/C boundary
                manager.DrawFilledRectangle(new Rectangle(x0, top + rowH - 1, width, 1), octave ? OctaveLineColor : RowLineColor);
            }

            // ---- vertical grid (subdivision / beat / bar) ----
            if (GridBeats > 0 && BeatsVisible > 0)
            {
                int lines = (int)(BeatsVisible / GridBeats);
                float pxPerGrid = (float)(GridBeats / BeatsVisible * width);
                bool drawSubdivisions = pxPerGrid >= 5f;
                for (int i = 1; i <= lines; i++)
                {
                    double beat = i * GridBeats;
                    bool isBeat = Math.Abs(beat - Math.Round(beat)) < 1e-9;
                    bool isBar = isBeat && ((int)Math.Round(beat)) % BeatsPerBar == 0;
                    if (!isBeat && !drawSubdivisions)
                    {
                        continue;
                    }

                    int x = x0 + (int)XForBeat(beat, width);
                    if (x >= x0 + width)
                    {
                        break;
                    }

                    Color c = isBar ? BarLineColor : isBeat ? BeatLineColor : GridLineColor;
                    manager.DrawFilledRectangle(new Rectangle(x, y0, 1, height), c);
                }
            }

            // ---- notes ----
            foreach (PianoRollNoteView note in Notes)
            {
                DrawNote(manager, note, x0, y0, width, height, 1f);
            }

            if (GhostNote != null)
            {
                DrawNote(manager, GhostNote, x0, y0, width, height, 0.55f);
            }

            // ---- playhead ----
            if (PlayheadBeats >= 0 && PlayheadBeats <= BeatsVisible)
            {
                int x = x0 + (int)XForBeat(PlayheadBeats, width);
                manager.DrawFilledRectangle(new Rectangle(x, y0, 2, height), PlayheadColor);
            }

            base.Draw();
        }

        private void DrawNote(Managers.DrawManager manager, PianoRollNoteView note,
            int x0, int y0, int width, int height, float alpha)
        {
            int left = x0 + (int)XForBeat(note.StartBeats, width);
            int right = x0 + (int)XForBeat(note.StartBeats + note.LengthBeats, width);
            int noteW = Math.Max(3, right - left);
            int rowH = Math.Max(3, (int)RowHeight - 1);
            Color body = (note.Selected ? NoteSelectedColor : NoteColor) * alpha;
            Color border = NoteBorderColor * alpha;

            bool bent = note.BendOffsets != null && note.BendOffsets.Length >= 2;
            if (!bent)
            {
                int top = y0 + (int)YTopForPitch(note.Pitch);
                if (top + rowH < y0 || top > y0 + height)
                {
                    return;
                }

                var rect = new Rectangle(left, top + 1, noteW, rowH);
                manager.DrawFilledRectangle(rect, body);
                manager.DrawRectangle(rect, border);
                DrawVelocityShade(manager, rect, note.Velocity, alpha);
            }
            else
            {
                // Curved geometry: the block is a strip of short column rects
                // following Pitch + offset(t) — the sampled bend IS the shape.
                const int columnStep = 3;
                float[] offsets = note.BendOffsets;
                int columns = Math.Max(1, noteW / columnStep);
                for (int cIx = 0; cIx <= columns; cIx++)
                {
                    float t = cIx / (float)columns;
                    float offset = SampleOffsets(offsets, t);
                    int cx = left + (int)(t * noteW);
                    int cw = Math.Min(columnStep, left + noteW - cx);
                    if (cw <= 0)
                    {
                        continue;
                    }

                    int top = y0 + (int)YTopForPitch(note.Pitch + offset);
                    if (top + rowH < y0 || top > y0 + height)
                    {
                        continue;
                    }

                    manager.DrawFilledRectangle(new Rectangle(cx, top + 1, cw, rowH), body);

                    // Thin top/bottom edge lines keep the curved block crisp.
                    manager.DrawFilledRectangle(new Rectangle(cx, top + 1, cw, 1), border);
                    manager.DrawFilledRectangle(new Rectangle(cx, top + rowH, cw, 1), border);
                }

                // End caps.
                int startTop = y0 + (int)YTopForPitch(note.Pitch + SampleOffsets(offsets, 0f));
                int endTop = y0 + (int)YTopForPitch(note.Pitch + SampleOffsets(offsets, 1f));
                manager.DrawFilledRectangle(new Rectangle(left, startTop + 1, 1, rowH), border);
                manager.DrawFilledRectangle(new Rectangle(left + noteW - 1, endTop + 1, 1, rowH), border);
            }

            // Bend handle on the selected note's tail (bend is opt-in: the
            // handle is the affordance; the host hit-tests the same spot).
            if (note.Selected && alpha >= 1f)
            {
                float endOffset = bent ? SampleOffsets(note.BendOffsets, 1f) : 0f;
                int hx = left + noteW - 1;
                int hy = y0 + (int)(YTopForPitch(note.Pitch + endOffset) + RowHeight * 0.5f);
                int hs = (int)BendHandleSize;
                var handle = new Rectangle(hx - hs / 2, hy - hs / 2, hs, hs);
                manager.DrawFilledRectangle(handle, BendHandleColor);
                manager.DrawRectangle(handle, NoteBorderColor);
            }
        }

        private static void DrawVelocityShade(Managers.DrawManager manager, Rectangle rect, float velocity, float alpha)
        {
            // Quieter notes dim: a translucent dark overlay scaled by 1−velocity.
            float dim = 1f - MathHelper.Clamp(velocity, 0f, 1f);
            if (dim > 0.05f)
            {
                manager.DrawFilledRectangle(rect, new Color(0, 0, 0) * (0.5f * dim * alpha));
            }
        }

        /// <summary>Linear interpolation over the host-sampled bend offsets.</summary>
        public static float SampleOffsets(float[] offsets, float t)
        {
            if (offsets == null || offsets.Length == 0)
            {
                return 0f;
            }

            if (offsets.Length == 1)
            {
                return offsets[0];
            }

            float f = MathHelper.Clamp(t, 0f, 1f) * (offsets.Length - 1);
            int i = (int)f;
            if (i >= offsets.Length - 1)
            {
                return offsets[offsets.Length - 1];
            }

            return offsets[i] + (offsets[i + 1] - offsets[i]) * (f - i);
        }

        private static bool IsBlackKey(int pitch)
        {
            int pc = ((pitch % 12) + 12) % 12;
            return pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10;
        }
    }
}
