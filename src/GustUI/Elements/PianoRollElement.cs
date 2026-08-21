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

        /// <summary>The bend's actual vertices (note-local beats +
        /// semitone offsets), for the BEND-edit mode's handles
        /// (<see cref="PianoRollElement.BendEditMode"/>); null when the
        /// note has no bend.</summary>
        public List<PianoRollBendPointView> BendPoints;
    }

    /// <summary>One bend vertex as the roll draws it (a display view of the
    /// host's own bend model, like <see cref="PianoRollNoteView"/> itself).</summary>
    public class PianoRollBendPointView
    {
        public double Beats;
        public float Semitones;
        public float Curvature;
    }

    /// <summary>
    /// The piano-roll editing surface: semitone rows (piano-key shading),
    /// beat/bar grid, note blocks — bent notes draw as curved geometry
    /// following their pitch offsets — plus a playhead line, a bend handle
    /// on selected notes, and an optional piano-keyboard gutter down the
    /// left edge whose keys align 1:1 with the rows (<see cref="KeyboardWidth"/>).
    /// Immediate-mode in Draw() (the KnobElement pattern; measured guidance
    /// in WaveformElement's header applies: per-column rects only where a
    /// note actually bends).
    ///
    /// The element is a DUMB SURFACE: the host sets the display lists and
    /// mapping domain (FirstBeat / PixelsPerBeat / TopPitch / RowHeight),
    /// wires the mouse traits and runs the interaction state machine itself —
    /// the same division of labor as the sequencer's body panel.
    ///
    /// Mapping (mirrored from the host's pure view math):
    ///   x = KeyboardWidth + (beats − FirstBeat) × PixelsPerBeat
    ///   y = (TopPitch − pitch) × RowHeight.
    /// PixelsPerBeat ≤ 0 falls back to fitting <see cref="BeatsVisible"/>
    /// into the grid width — the pre-zoom behavior, so a host that never
    /// sets the viewport fields still gets the whole pattern.
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

        /// <summary>Total pattern extent in beats (the editable domain; the
        /// area past it is drawn dimmed so a zoomed/panned view still shows
        /// where the pattern ends).</summary>
        public double BeatsVisible = 16;

        public int BeatsPerBar = 4;

        /// <summary>Drawn grid subdivision in beats (also the host's snap).</summary>
        public double GridBeats = 0.25;

        /// <summary>Pitch whose row TOP sits at y = 0 (vertical scroll).</summary>
        public int TopPitch = 96;

        /// <summary>Row height in pixels (vertical zoom).</summary>
        public float RowHeight = 14f;

        /// <summary>First beat visible at the grid's left edge (horizontal scroll).</summary>
        public double FirstBeat;

        /// <summary>Horizontal zoom. ≤ 0 = fit <see cref="BeatsVisible"/> to
        /// the grid width (legacy behavior).</summary>
        public float PixelsPerBeat;

        /// <summary>Width of the piano-keyboard gutter on the left edge; 0
        /// draws no keyboard (legacy layout).</summary>
        public float KeyboardWidth;

        /// <summary>Playhead position in beats; negative hides it.</summary>
        public double PlayheadBeats = -1;

        /// <summary>Pitches whose gutter keys draw PRESSED (audition
        /// feedback — QWERTY musical typing, mouse note-hits). Null/empty =
        /// none. Host-owned, rebuilt per frame like the note list.</summary>
        public HashSet<int> HighlightedPitches;

        /// <summary>BEND-edit mode (the toolbar's Notes/Bend toggle): every
        /// note draws its bend vertices as grabbable squares and each
        /// segment's midpoint as a curvature diamond; the host runs the
        /// editing state machine against the same geometry
        /// (<see cref="BendVertexHitSize"/>).</summary>
        public bool BendEditMode;

        /// <summary>Hit/draw size of a bend vertex handle (square) and the
        /// segment-midpoint curvature handle (diamond).</summary>
        public const float BendVertexHitSize = 7f;

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
        public Color PatternEndDimColor = new Color(0, 0, 0) * 0.45f;
        public Color KeyWhiteColor = new Color(214, 214, 220);
        public Color KeyBlackColor = new Color(28, 28, 34);
        public Color KeyPressedColor = new Color(110, 145, 235);
        public Color KeyLineColor = new Color(120, 120, 128);
        public Color KeyLabelColor = new Color(60, 60, 70);
        public Color KeyEdgeColor = new Color(70, 70, 84);

        private Managers.SdfFont keyFont;

        /// <summary>The horizontal scale actually in effect (the fit
        /// fallback resolved against the current width).</summary>
        public float EffectivePixelsPerBeat(float width)
        {
            if (PixelsPerBeat > 0f)
            {
                return PixelsPerBeat;
            }

            float gridWidth = Math.Max(1f, width - KeyboardWidth);
            return BeatsVisible <= 0 ? 1f : (float)(gridWidth / BeatsVisible);
        }

        public float XForBeat(double beats, float width)
            => KeyboardWidth + (float)((beats - FirstBeat) * EffectivePixelsPerBeat(width));

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
            int gutter = (int)KeyboardWidth;
            int gridX = x0 + gutter;
            int gridW = width - gutter;
            float ppb = EffectivePixelsPerBeat(width);
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
                manager.DrawFilledRectangle(new Rectangle(gridX, top, gridW, rowH), black ? BlackRowColor : WhiteRowColor);
                bool octave = ((pitch % 12) + 12) % 12 == 0; // line under B/C boundary
                manager.DrawFilledRectangle(new Rectangle(gridX, top + rowH - 1, gridW, 1), octave ? OctaveLineColor : RowLineColor);
            }

            // ---- vertical grid (subdivision / beat / bar) ----
            if (GridBeats > 0)
            {
                double lastBeat = Math.Min(BeatsVisible, FirstBeat + gridW / (double)ppb);
                float pxPerGrid = (float)(GridBeats * ppb);
                bool drawSubdivisions = pxPerGrid >= 5f;
                long firstLine = Math.Max(1, (long)Math.Ceiling(FirstBeat / GridBeats - 1e-9));
                for (long i = firstLine; i * GridBeats <= lastBeat + 1e-9; i++)
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

                    if (x < gridX)
                    {
                        continue;
                    }

                    Color c = isBar ? BarLineColor : isBeat ? BeatLineColor : GridLineColor;
                    manager.DrawFilledRectangle(new Rectangle(x, y0, 1, height), c);
                }
            }

            // ---- the void past the pattern's end, dimmed ----
            int endX = x0 + (int)XForBeat(BeatsVisible, width);
            if (endX < x0 + width)
            {
                int dimX = Math.Max(gridX, endX);
                manager.DrawFilledRectangle(new Rectangle(dimX, y0, x0 + width - dimX, height), PatternEndDimColor);
            }

            // ---- notes ----
            foreach (PianoRollNoteView note in Notes)
            {
                DrawNote(manager, note, x0, y0, width, height, gridX, 1f);
            }

            if (GhostNote != null)
            {
                DrawNote(manager, GhostNote, x0, y0, width, height, gridX, 0.55f);
            }

            // ---- playhead ----
            if (PlayheadBeats >= 0 && PlayheadBeats <= BeatsVisible)
            {
                int x = x0 + (int)XForBeat(PlayheadBeats, width);
                if (x >= gridX && x < x0 + width)
                {
                    manager.DrawFilledRectangle(new Rectangle(x, y0, 2, height), PlayheadColor);
                }
            }

            // ---- piano keyboard gutter (over everything grid-side) ----
            if (gutter > 0)
            {
                DrawKeyboard(manager, x0, y0, gutter, height);
            }

            base.Draw();
        }

        /// <summary>The keys column: one key per row, aligned with the grid's
        /// rows — white keys light, black keys inset dark over the left of
        /// the gutter (reads as a real keyboard edge-on). C rows are always
        /// labeled ("C4"); every white row gains its own label once rows are
        /// tall enough to carry the text.</summary>
        private void DrawKeyboard(Managers.DrawManager manager, int x0, int y0, int gutter, int height)
        {
            manager.DrawFilledRectangle(new Rectangle(x0, y0, gutter, height), KeyBlackColor);

            keyFont ??= Resources.StaticResources.FontManager.LoadSdfFont(Resources.StaticResources.Theme.UiFontSmall.Family);
            float labelSize = MathHelper.Clamp(RowHeight - 3f, 8f, 12f);
            bool labelAllWhites = RowHeight >= 15f;
            int blackKeyWidth = (int)(gutter * 0.58f);

            int lastRow = (int)(height / RowHeight) + 1;
            for (int r = 0; r <= lastRow; r++)
            {
                int pitch = TopPitch - r;
                int top = y0 + (int)(r * RowHeight);
                int rowH = (int)((r + 1) * RowHeight) - (int)(r * RowHeight);
                if (top >= y0 + height)
                {
                    break;
                }

                rowH = Math.Min(rowH, y0 + height - top);
                if (pitch < 0 || pitch > 127)
                {
                    continue; // beyond the MIDI range: bare gutter
                }

                bool pressed = HighlightedPitches != null && HighlightedPitches.Contains(pitch);
                if (IsBlackKey(pitch))
                {
                    // The white keys behind flow through; the black key sits
                    // over the left portion, its row line only under itself.
                    manager.DrawFilledRectangle(new Rectangle(x0 + blackKeyWidth, top, gutter - blackKeyWidth, rowH), KeyWhiteColor);
                    manager.DrawFilledRectangle(new Rectangle(x0, top, blackKeyWidth, rowH - 1), pressed ? KeyPressedColor : KeyBlackColor);
                }
                else
                {
                    manager.DrawFilledRectangle(new Rectangle(x0, top, gutter, rowH), pressed ? KeyPressedColor : KeyWhiteColor);

                    // A physical white-key boundary exists only at E/F and
                    // B/C — where no black key separates the rows.
                    int pc = ((pitch % 12) + 12) % 12;
                    if (pc == 0 || pc == 5)
                    {
                        manager.DrawFilledRectangle(new Rectangle(x0, top + rowH - 1, gutter, 1), KeyLineColor);
                    }

                    bool isC = pc == 0;
                    if ((isC || labelAllWhites) && rowH >= 8)
                    {
                        string label = NoteName(pitch);
                        Vector2 measured = keyFont.MeasureString(label, labelSize);
                        manager.DrawSdfString(keyFont, label,
                            new Vector2(x0 + gutter - measured.X - 3, top + (rowH - measured.Y) * 0.5f),
                            labelSize, isC ? KeyLabelColor : KeyLabelColor * 0.7f);
                    }
                }
            }

            // Gutter/grid divider.
            manager.DrawFilledRectangle(new Rectangle(x0 + gutter - 1, y0, 1, height), KeyEdgeColor);
        }

        /// <summary>"C4" style note name (60 = C4, this codebase's middle-C convention).</summary>
        public static string NoteName(int pitch)
        {
            string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            int pc = ((pitch % 12) + 12) % 12;
            return names[pc] + (pitch / 12 - 1);
        }

        private void DrawNote(Managers.DrawManager manager, PianoRollNoteView note,
            int x0, int y0, int width, int height, int gridX, float alpha)
        {
            int left = x0 + (int)XForBeat(note.StartBeats, width);
            int right = x0 + (int)XForBeat(note.StartBeats + note.LengthBeats, width);
            if (right <= gridX || left >= x0 + width)
            {
                return; // fully off-view (panned/zoomed away)
            }

            int noteW = Math.Max(3, right - left);
            if (left < gridX)
            {
                // Clip at the keyboard edge; keep the true right edge.
                noteW = Math.Max(1, right - gridX);
                left = gridX;
            }

            noteW = Math.Min(noteW, x0 + width - left);
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

            // Bend-edit handles (mode-gated): vertex squares at each bend
            // point, a curvature diamond at each segment's midpoint — the
            // curve-editor idiom (drag a point to move it, drag a diamond
            // to bow the segment). Drawn for every note so the whole
            // pattern's pitch shapes are editable at a glance.
            if (BendEditMode && alpha >= 1f)
            {
                DrawBendHandles(manager, note, x0, y0, width);
            }
        }

        private void DrawBendHandles(Managers.DrawManager manager, PianoRollNoteView note, int x0, int y0, int width)
        {
            List<PianoRollBendPointView> points = note.BendPoints;
            int hs = (int)BendVertexHitSize;
            if (points == null || points.Count == 0)
            {
                return;
            }

            for (int i = 0; i < points.Count; i++)
            {
                PianoRollBendPointView v = points[i];
                int vx = x0 + (int)XForBeat(note.StartBeats + v.Beats, width);
                int vy = y0 + (int)(YTopForPitch(note.Pitch + v.Semitones) + RowHeight * 0.5f);
                var handle = new Rectangle(vx - hs / 2, vy - hs / 2, hs, hs);
                manager.DrawFilledRectangle(handle, BendHandleColor);
                manager.DrawRectangle(handle, NoteBorderColor);

                if (i + 1 < points.Count && note.LengthBeats > 0)
                {
                    // The segment's curvature diamond, ON the curve at the
                    // segment's midpoint (SampleOffsets reads the same
                    // sampled curve the note body renders from).
                    PianoRollBendPointView b2 = points[i + 1];
                    double midBeat = (v.Beats + b2.Beats) * 0.5;
                    float t = (float)(midBeat / note.LengthBeats);
                    float offset = SampleOffsets(note.BendOffsets, t);
                    int mx = x0 + (int)XForBeat(note.StartBeats + midBeat, width);
                    int my = y0 + (int)(YTopForPitch(note.Pitch + offset) + RowHeight * 0.5f);
                    int ds = hs - 2;
                    var diamond = new Rectangle(mx - ds / 2, my - ds / 2, ds, ds);
                    manager.DrawFilledRectangle(diamond, BendHandleColor * 0.55f);
                    manager.DrawRectangle(diamond, NoteBorderColor);
                }
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
