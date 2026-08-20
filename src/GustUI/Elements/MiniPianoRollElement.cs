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
    /// list — pattern-local beats, no bend/selection (a read-only thumbnail,
    /// not an editing surface; see <see cref="PianoRollElement"/> for
    /// that).</summary>
    public struct MiniRollNote
    {
        public float Pitch;
        public double StartBeats;
        public double LengthBeats;
        public float Velocity;
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

        /// <summary>Opaque background fill; Transparent (default) draws no
        /// background — the host's own block face shows through, matching
        /// the waveform overlay style rather than the baked-texture style.</summary>
        public Color BackColor { get; set; } = Color.Transparent;

        public Color NoteColor { get; set; } = new Color(230, 235, 250);

        /// <summary>Minimum drawn note width/height in px so a dense pattern
        /// (many short notes) or a wide pitch range (many semitones per
        /// pixel) still reads as note ticks instead of vanishing — the
        /// WaveformData "silence still reads as a waveform" idiom.</summary>
        private const int MinNotePx = 2;

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

            if (totalWidth >= 1 && height >= 2 && Notes.Count > 0 && BeatsVisible > 0)
            {
                var manager = Resources.StaticResources.DrawManager;

                if (BackColor.A > 0)
                {
                    manager.DrawFilledRectangle(new Rectangle((int)pos.X, (int)pos.Y, totalWidth, height), BackColor);
                }

                int tiles = Math.Max(1, TileCount);
                int tileWidth = Math.Max(1, totalWidth / tiles);
                float range = MaxPitch - MinPitch;

                // A semitone-ish sliver: dense/wide-range patterns still read
                // as a texture of ticks rather than one indistinct band.
                int noteHeight = Math.Max(MinNotePx, height / 24);

                int drawnWidth = 0;
                for (int t = 0; t < tiles; t++)
                {
                    // The last tile absorbs integer-division rounding, same
                    // as WaveformElement's tiling loop.
                    int thisWidth = t == tiles - 1 ? totalWidth - drawnWidth : tileWidth;
                    int tileX = (int)pos.X + drawnWidth;
                    drawnWidth += thisWidth;

                    if (thisWidth <= 0)
                    {
                        continue;
                    }

                    foreach (MiniRollNote note in Notes)
                    {
                        float startT = (float)(note.StartBeats / BeatsVisible);
                        float endT = (float)((note.StartBeats + note.LengthBeats) / BeatsVisible);
                        if (endT <= 0f || startT >= 1f)
                        {
                            continue; // wholly outside this tile
                        }

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

                        float alpha = 0.4f + 0.6f * MathHelper.Clamp(note.Velocity, 0f, 1f);
                        manager.DrawFilledRectangle(new Rectangle(left, top, noteWidth, noteHeight), NoteColor * alpha);
                    }
                }
            }

            base.Draw();
        }
    }
}
