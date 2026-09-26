using System;
using System.Collections.Generic;
using GustUI.Attributes;
using GustUI.Extensions;
using GustUI.Traits;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GustUI.Elements
{
    /// <summary>One note in a <see cref="MiniPianoRollElement"/>'s display
    /// list — pattern-local beats, no selection (a read-only thumbnail, not
    /// an editing surface; see <see cref="PianoRollElement"/> for that).</summary>
    public struct MiniRollNote
    {
        public float Pitch;
        public double StartBeats;
        public double LengthBeats;
        public float Velocity;

        /// <summary>
        /// Semitone offsets sampled UNIFORMLY across the note — the same
        /// samples the piano roll draws from (<see cref="PianoRollNoteView.BendOffsets"/>);
        /// null = a straight note. When present the note draws BENT, as the
        /// shared <see cref="Rendering.BendBand"/> at this roll's note
        /// height (ezmuze #377: an MPE study whose notes all start on C4 drew
        /// as one flat bar while the piano roll fanned out to the chords).
        /// The host owns and caches the array; this element only reads it.
        /// </summary>
        public float[] BendOffsets;
    }

    /// <summary>
    /// A tiny "Reaper-style" per-channel MIDI-item preview for a sequencer
    /// block face: the alternate a channel's pattern blocks can show instead
    /// of <see cref="WaveformElement"/>'s rendered waveform. Immediate-mode
    /// in <see cref="Draw"/> (the WaveformElement/PianoRollElement idiom) —
    /// a pure DISPLAY surface, no mouse traits. The host supplies the
    /// display list and the mapping domain, exactly like PianoRollElement:
    ///   x = StartBeats/LengthBeats over [0, BeatsVisible] (one tile);
    ///   y = Pitch linearly normalized over [MinPitch, MaxPitch] onto
    ///       [0, height], INVERTED (higher pitch draws smaller y — higher on
    ///       screen). The host computes MinPitch/MaxPitch ONCE across every
    ///       pattern placed on a channel (Reaper's own per-track MIDI-item
    ///       scaling) rather than per block, so every block on that channel
    ///       reads on the same vertical scale — this element does not know
    ///       or care where the range came from, it just normalizes into it.
    /// Tiles its <see cref="Notes"/> side-by-side <see cref="TileCount"/>
    /// times, mirroring WaveformElement's Repeat-clip tiling of one cached
    /// image — the SAME note list drawn N times, not N elements.
    /// </summary>
    [ElementTraits(typeof(PositionTrait), typeof(SizeTrait))]
    public class MiniPianoRollElement : Element
    {
        /// <summary>Notes to draw, in ONE tile's beat-local coordinates
        /// (host-owned; replaced/edited after model changes).</summary>
        public List<MiniRollNote> Notes { get; } = new List<MiniRollNote>();

        /// <summary>Horizontal domain of ONE tile, in beats — the pattern's
        /// own natural length (the same domain the waveform face's single
        /// cached image represents).</summary>
        public double BeatsVisible { get; set; } = 4;

        /// <summary>MIDI pitch mapped to the bottom of the surface.</summary>
        public float MinPitch { get; set; }

        /// <summary>MIDI pitch mapped to the top of the surface.</summary>
        public float MaxPitch { get; set; } = 127f;

        /// <summary>Repeats <see cref="Notes"/> side-by-side this many times
        /// across the element's width (≥1; the WaveformElement Repeat-clip
        /// tiling convention).</summary>
        public int TileCount { get; set; } = 1;

        /// <summary>How much of one tile's beat domain the LAST tile covers,
        /// 0..1 (1 = a whole tile, the unchanged default) — WaveformElement's
        /// <c>LastTileFraction</c> in the note domain, and set from the same
        /// host-side number so a block's notes and its waveform never
        /// disagree about where the clip ends. Notes past the fraction are
        /// simply not drawn: a clip cut short does not play them.</summary>
        public float LastTileFraction { get; set; } = 1f;

        /// <summary>Opaque background fill; Transparent (default) draws no
        /// background — the host's own block face shows through, matching
        /// the waveform overlay style rather than the baked-texture style.</summary>
        public Color BackColor { get; set; } = Color.Transparent;

        public Color NoteColor { get; set; } = new Color(230, 235, 250);

        /// <summary>
        /// A second set of notes drawn OVER <see cref="Notes"/> in
        /// <see cref="OverlayNoteColor"/> — notes that belong to this roll's
        /// bar but came from somewhere else, so they read as a layer rather
        /// than as more of the same part.
        ///
        /// Its case is a grouped channel whose own pattern plays alongside its
        /// children's (ezmuze studio's channel hierarchy). Two lists rather than
        /// a colour per note: the distinction is between SOURCES, not between
        /// individual notes, and a per-note colour would invite it to become
        /// decoration.
        /// </summary>
        public List<MiniRollNote> OverlayNotes { get; } = new List<MiniRollNote>();

        public Color OverlayNoteColor { get; set; } = new Color(150, 190, 240);

        /// <summary>Minimum drawn note width/height in px so a dense pattern
        /// (many short notes) or a wide pitch range (many semitones per
        /// pixel) still reads as note ticks instead of vanishing — the
        /// WaveformData "silence still reads as a waveform" idiom.</summary>
        private const int MinNotePx = 2;

        /// <summary>Steps across ONE bent note in one tile, at most (one per
        /// ~2px below it). Higher than the piano roll's 512 because a tile is
        /// baked once, not rebuilt every frame, and a zoomed-in clip can be
        /// thousands of pixels wide.</summary>
        private const int MaxBendSamples = 2048;

        /// <summary>Records bent notes into <see cref="Rendering.BendGeometry"/>.</summary>
        private readonly Rendering.BendGeometryBatch bendBatch = new Rendering.BendGeometryBatch();

        /// <summary>
        /// The bent notes of one tile, baked in TILE-LOCAL coordinates and
        /// redrawn every frame at the tile's offset (#377). Every full tile of
        /// a Repeat clip is the same picture, so there are two slots: the
        /// full tile, and the last one when it is cut short. A slot is
        /// rebuilt only when its key (the notes, their bends, the colours
        /// and the tile's pixel size and pitch mapping) changes, so panning
        /// and scrolling draw from the cache and only a zoom (a new width)
        /// or an edit pays for the curve again.
        /// </summary>
        private readonly BakedTile[] bakedTiles = { new BakedTile(), new BakedTile() };

        private sealed class BakedTile
        {
            public int Key;
            public Rendering.BendGeometry Geometry;
        }

        public override void Draw()
        {
            using (Managers.Telemetry.Scope("Draw.MiniPianoRoll"))
            {
                DrawRoll();
            }
        }

        private void DrawRoll()
        {
            Vector2 pos = this.GetActualXnaPosition();
            Vector2 size = this.GetSize().AsXna;
            int totalWidth = (int)size.X;
            int height = (int)size.Y;

            if (totalWidth >= 1 && height >= 2 && (Notes.Count > 0 || OverlayNotes.Count > 0) && BeatsVisible > 0)
            {
                var manager = Resources.StaticResources.DrawManager;

                if (BackColor.A > 0)
                {
                    manager.DrawFilledRectangle(new Rectangle((int)pos.X, (int)pos.Y, totalWidth, height), BackColor);
                }

                int tiles = Math.Max(1, TileCount);
                float lastFraction = MathHelper.Clamp(LastTileFraction, 0f, 1f);

                // Widths proportional to the SPAN, not the count — see
                // WaveformElement.DrawWaveform, which this mirrors exactly so
                // the two views of a block line up bar for bar.
                float span = Math.Max(0.0001f, tiles - 1 + lastFraction);
                int tileWidth = Math.Max(1, (int)(totalWidth / span));
                float range = MaxPitch - MinPitch;

                // A semitone-ish sliver: dense/wide-range patterns still read
                // as a texture of ticks rather than one indistinct band.
                int noteHeight = Math.Max(MinNotePx, height / 24);

                // Tiles the viewport does not show are skipped outright: a
                // long Repeat clip is mostly off screen.
                Vector4 clip = manager.LogicalClipBounds;
                float visibleLeft = Math.Max(pos.X, clip.X);
                float visibleRight = Math.Min(pos.X + totalWidth, clip.Z);
                bool anyBent = HasBend(Notes) || HasBend(OverlayNotes);

                int drawnWidth = 0;
                for (int t = 0; t < tiles; t++)
                {
                    // The last tile absorbs integer-division rounding, same
                    // as WaveformElement's tiling loop.
                    bool last = t == tiles - 1;
                    int thisWidth = last ? totalWidth - drawnWidth : tileWidth;
                    float fraction = last ? lastFraction : 1f;
                    int tileX = (int)pos.X + drawnWidth;
                    drawnWidth += thisWidth;

                    if (thisWidth <= 0)
                    {
                        continue;
                    }

                    if (tileX > visibleRight || tileX + thisWidth < visibleLeft)
                    {
                        continue; // this tile is wholly off screen
                    }

                    DrawNotes(manager, Notes, NoteColor, tileX, thisWidth, pos, height, noteHeight, range, fraction);
                    DrawNotes(manager, OverlayNotes, OverlayNoteColor, tileX, thisWidth, pos, height, noteHeight, range, fraction);

                    if (anyBent)
                    {
                        // The bent layer draws over the flat one, own notes
                        // then overlay, as the piano roll batches its bends.
                        BakedTile slot = bakedTiles[last && fraction < 1f ? 1 : 0];
                        int key = BakeKey(thisWidth, height, noteHeight, fraction);
                        if (slot.Geometry == null || slot.Key != key)
                        {
                            slot.Geometry = BakeTile(thisWidth, height, noteHeight, range, fraction);
                            slot.Key = key;
                        }

                        slot.Geometry.Draw(manager, new Vector2(tileX, pos.Y));
                    }
                }
            }

            base.Draw();
        }

        private static bool HasBend(List<MiniRollNote> notes)
        {
            foreach (MiniRollNote note in notes)
            {
                if (note.BendOffsets != null && note.BendOffsets.Length >= 2)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Everything a baked tile's picture depends on, hashed. The
        /// bend arrays count by IDENTITY: the host hands over a new array
        /// whenever a bend or a note's length changes, and the same one
        /// otherwise.</summary>
        private int BakeKey(int width, int height, int noteHeight, float fraction)
        {
            var hash = new HashCode();
            hash.Add(width);
            hash.Add(height);
            hash.Add(noteHeight);
            hash.Add(fraction);
            hash.Add(MinPitch);
            hash.Add(MaxPitch);
            hash.Add(BeatsVisible);
            hash.Add(NoteColor);
            hash.Add(OverlayNoteColor);
            AddBentNotes(ref hash, Notes);
            hash.Add(-1);
            AddBentNotes(ref hash, OverlayNotes);
            return hash.ToHashCode();
        }

        private static void AddBentNotes(ref HashCode hash, List<MiniRollNote> notes)
        {
            foreach (MiniRollNote note in notes)
            {
                if (note.BendOffsets == null || note.BendOffsets.Length < 2)
                {
                    continue;
                }

                hash.Add(note.Pitch);
                hash.Add(note.StartBeats);
                hash.Add(note.LengthBeats);
                hash.Add(note.Velocity);
                hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(note.BendOffsets));
            }
        }

        private Rendering.BendGeometry BakeTile(int width, int height, int noteHeight, float range, float fraction)
        {
            bendBatch.BeginBake();
            AppendBentNotes(Notes, NoteColor, width, height, noteHeight, range, fraction);
            AppendBentNotes(OverlayNotes, OverlayNoteColor, width, height, noteHeight, range, fraction);
            return bendBatch.EndBake();
        }

        private void AppendBentNotes(List<MiniRollNote> notes, Color color,
            int width, int height, int noteHeight, float range, float sourceFraction)
        {
            double visible = BeatsVisible * Math.Max(0.0001f, sourceFraction);
            foreach (MiniRollNote note in notes)
            {
                if (note.BendOffsets == null || note.BendOffsets.Length < 2)
                {
                    continue;
                }

                float startT = (float)(note.StartBeats / visible);
                float endT = (float)((note.StartBeats + note.LengthBeats) / visible);
                if (endT <= 0f || startT >= 1f)
                {
                    continue; // wholly outside this tile
                }

                float alpha = 0.4f + 0.6f * MathHelper.Clamp(note.Velocity, 0f, 1f);
                AppendBentNote(note, color * alpha, startT, endT, width, height, noteHeight, range);
            }
        }

        private void DrawNotes(Managers.DrawManager manager, List<MiniRollNote> notes, Color color,
            int tileX, int thisWidth, Vector2 pos, int height, int noteHeight, float range, float sourceFraction)
        {
            // A partial tile shows only the LEADING sourceFraction of the
            // tile's beat domain, spread across its (proportionally narrower)
            // width — so the visible domain shrinks and the pixels-per-beat
            // stays put. Notes beyond it fall out through the same
            // "wholly outside this tile" test that already existed.
            double visible = BeatsVisible * Math.Max(0.0001f, sourceFraction);

            foreach (MiniRollNote note in notes)
            {
                float startT = (float)(note.StartBeats / visible);
                float endT = (float)((note.StartBeats + note.LengthBeats) / visible);
                if (endT <= 0f || startT >= 1f)
                {
                    continue; // wholly outside this tile
                }

                if (note.BendOffsets != null && note.BendOffsets.Length >= 2)
                {
                    continue; // drawn from the baked tile (BakeTile)
                }

                float alpha = 0.4f + 0.6f * MathHelper.Clamp(note.Velocity, 0f, 1f);

                startT = MathHelper.Clamp(startT, 0f, 1f);
                endT = MathHelper.Clamp(endT, 0f, 1f);

                int left = tileX + (int)(startT * thisWidth);
                int right = tileX + (int)(endT * thisWidth);
                int noteWidth = Math.Max(MinNotePx, right - left);

                float pitchT = range > 0f ? (note.Pitch - MinPitch) / range : 0.5f;
                pitchT = MathHelper.Clamp(pitchT, 0f, 1f);
                int centerY = (int)pos.Y + (int)((1f - pitchT) * height);
                int top = MathHelper.Clamp(
                    centerY - noteHeight / 2,
                    (int)pos.Y,
                    (int)pos.Y + Math.Max(0, height - noteHeight));

                manager.DrawFilledRectangle(new Rectangle(left, top, noteWidth, noteHeight), color * alpha);
            }
        }

        /// <summary>
        /// One bent note as the shared <see cref="Rendering.BendBand"/>:
        /// borderless, the SAME height as a straight note here, centred on the
        /// sounding pitch exactly as a straight note's rectangle is centred on
        /// its pitch — so a bent note and a flat one at the same pitch
        /// coincide, and a pattern mixing them reads at one weight.
        /// </summary>
        private void AppendBentNote(MiniRollNote note, Color color, float startT, float endT,
            int width, int height, int noteHeight, float range)
        {
            // Tile-local: x from 0 at the tile's left edge, y from 0 at the
            // element's top.
            float leftF = startT * width;
            float rightF = endT * width;
            float xStart = Math.Max(leftF, 0f);
            float xEnd = Math.Min(rightF, width);
            if (xEnd - xStart < 1f)
            {
                return;
            }

            // centre(p) = (1 - (p - MinPitch) / range) * height; the band's
            // top sits half a note above it. A zero range (every sounding
            // pitch equal) centres everything, as DrawNotes does.
            float perSemitone = range > 0f ? -height / range : 0f;
            float centreAtZero = range > 0f ? height * (1f + MinPitch / range) : height * 0.5f;
            float topAtZero = centreAtZero - noteHeight / 2;

            Rendering.BendBand.Append(bendBatch, note.BendOffsets, range > 0f ? note.Pitch : 0f,
                leftF, rightF, xStart, xEnd, topAtZero, perSemitone, noteHeight,
                color, Color.Transparent, 0f, height, MaxBendSamples);
        }
    }
}
