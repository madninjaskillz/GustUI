#nullable enable

using System;

namespace GustUI.TraitValues
{
    /// <summary>
    /// Which cells of a sprite-sheet loop to show at a given moment, and
    /// whether the cached picture of them is due a redraw — everything
    /// <see cref="TVSpriteSheetFill"/> decides that is not a GPU call.
    ///
    /// Plain arithmetic with no graphics types, so it can be tested without a
    /// device (ezmuze's SpriteSheetTimelineTests link this file in directly).
    ///
    /// FRAMES. The loop is <see cref="FrameCount"/> frames at
    /// <see cref="FramesPerSecond"/>, laid out in reading order across one or
    /// more sheets of <see cref="Columns"/> x <see cref="Rows"/> cells. At a
    /// time t the playhead sits between two frames; <see cref="Sample"/> names
    /// both and how far it is from the first to the second, and the fill
    /// crossfades them by that much. The last frame fades into the first, so
    /// the loop has no seam beyond what the footage itself has.
    ///
    /// THE GATE. A crossfade weight changes every frame the app draws, so
    /// "redraw when the picture changes" would mean redraw always. The gate
    /// instead caps redraws at <see cref="MaxUpdatesPerSecond"/>, independent
    /// of the app's frame rate, and skips even those when the picture would
    /// not change by a visible amount (same pair, weight within
    /// <see cref="WeightEpsilon"/>). Between redraws the fill draws its last
    /// picture again, which is one textured quad.
    /// </summary>
    public sealed class SpriteSheetTimeline
    {
        /// <summary>A weight change smaller than this is not worth a redraw:
        /// about a quarter of one 8-bit colour step between the two frames'
        /// most different pixels, and far below that for the soft footage
        /// this is for.</summary>
        public const float WeightEpsilon = 1f / 64f;

        public int FrameCount { get; }
        public double FramesPerSecond { get; }
        public int Columns { get; }
        public int Rows { get; }
        public int CellWidth { get; }
        public int CellHeight { get; }

        /// <summary>The most redraws a second the gate allows. 0 or less
        /// means no cap (every frame that changes the picture).</summary>
        public double MaxUpdatesPerSecond { get; set; } = 15;

        public int CellsPerSheet => Columns * Rows;

        public int SheetCount => (FrameCount + CellsPerSheet - 1) / CellsPerSheet;

        /// <summary>The loop's length in seconds.</summary>
        public double Duration => FrameCount / FramesPerSecond;

        public SpriteSheetTimeline(int frameCount, double framesPerSecond, int columns, int rows, int cellWidth, int cellHeight)
        {
            if (frameCount < 1) throw new ArgumentOutOfRangeException(nameof(frameCount));
            if (!(framesPerSecond > 0)) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
            if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
            if (rows < 1) throw new ArgumentOutOfRangeException(nameof(rows));
            if (cellWidth < 1) throw new ArgumentOutOfRangeException(nameof(cellWidth));
            if (cellHeight < 1) throw new ArgumentOutOfRangeException(nameof(cellHeight));

            FrameCount = frameCount;
            FramesPerSecond = framesPerSecond;
            Columns = columns;
            Rows = rows;
            CellWidth = cellWidth;
            CellHeight = cellHeight;
        }

        /// <summary>Two neighbouring frames and how far the playhead is from
        /// <see cref="From"/> towards <see cref="To"/> (0..1).</summary>
        public readonly struct FramePair : IEquatable<FramePair>
        {
            public readonly int From;
            public readonly int To;
            public readonly float Weight;

            public FramePair(int from, int to, float weight)
            {
                From = from;
                To = to;
                Weight = weight;
            }

            public bool Equals(FramePair other) => From == other.From && To == other.To && Weight == other.Weight;

            public override bool Equals(object? obj) => obj is FramePair other && Equals(other);

            public override int GetHashCode() => (From * 397) ^ To ^ Weight.GetHashCode();

            public override string ToString() => $"{From}->{To} @ {Weight:0.000}";
        }

        /// <summary>The pair of frames to show at <paramref name="seconds"/>
        /// into the loop (any value, negative included; it wraps).</summary>
        public FramePair Sample(double seconds)
        {
            double position = seconds * FramesPerSecond % FrameCount;
            if (position < 0)
            {
                position += FrameCount;
            }

            int from = (int)Math.Floor(position);
            if (from >= FrameCount)
            {
                from = 0;
            }

            float weight = (float)(position - Math.Floor(position));
            return new FramePair(from, (from + 1) % FrameCount, weight);
        }

        /// <summary>Where <paramref name="frame"/> sits: which sheet, and the
        /// top-left pixel of its cell on that sheet.</summary>
        public (int Sheet, int X, int Y) CellOf(int frame)
        {
            frame = ((frame % FrameCount) + FrameCount) % FrameCount;
            int sheet = frame / CellsPerSheet;
            int index = frame % CellsPerSheet;
            return (sheet, index % Columns * CellWidth, index / Columns * CellHeight);
        }

        // ---- the redraw gate ----

        private bool rendered;
        private double lastRenderTime;
        private FramePair lastPair;
        private int lastWidth;
        private int lastHeight;

        /// <summary>How many times <see cref="Due"/> has said yes.</summary>
        public int RenderCount { get; private set; }

        /// <summary>The pair the last redraw showed.</summary>
        public FramePair LastPair => lastPair;

        /// <summary>
        /// Whether the cached picture needs redrawing at <paramref name="now"/>
        /// (seconds, any monotonic clock) for an area of
        /// <paramref name="width"/> x <paramref name="height"/>, and if so the
        /// pair to draw. Saying yes records the redraw, so call it once per
        /// frame and draw when it says so.
        ///
        /// Always no for an empty area (under 1 pixel either way: a minimised
        /// window lays itself out at nothing), and that records nothing.
        /// Otherwise always yes the first time, after <see cref="Invalidate"/>,
        /// and when the size changes; then no until 1/<see cref="MaxUpdatesPerSecond"/>
        /// has passed since the last redraw, and no even then when the picture
        /// would be the same.
        /// </summary>
        public bool Due(double now, int width, int height, out FramePair pair)
        {
            pair = Sample(now);
            if (width < 1 || height < 1)
            {
                return false;
            }

            bool sizeChanged = width != lastWidth || height != lastHeight;
            if (rendered && !sizeChanged)
            {
                if (MaxUpdatesPerSecond > 0 && now - lastRenderTime < 1.0 / MaxUpdatesPerSecond)
                {
                    return false;
                }

                if (pair.From == lastPair.From && pair.To == lastPair.To
                    && Math.Abs(pair.Weight - lastPair.Weight) < WeightEpsilon)
                {
                    return false;
                }
            }

            rendered = true;
            lastRenderTime = now;
            lastPair = pair;
            lastWidth = width;
            lastHeight = height;
            RenderCount++;
            return true;
        }

        /// <summary>Forgets the cached picture, so the next
        /// <see cref="Due"/> says yes (its targets were lost or released).</summary>
        public void Invalidate()
        {
            rendered = false;
        }
    }
}
