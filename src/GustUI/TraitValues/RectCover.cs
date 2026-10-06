#nullable enable

using System;

namespace GustUI.TraitValues
{
    /// <summary>
    /// Whether a set of axis-aligned rectangles covers a target rectangle
    /// completely. Plain integer geometry with no graphics types, so it is
    /// tested without a device (ezmuze's RectCoverTests link this file in).
    ///
    /// For <see cref="TVSpriteSheetFill"/>: a background under a stack of
    /// opaque windows that leave no gap anywhere is invisible, and need not
    /// be drawn, or redrawn, at all.
    ///
    /// EXACT, not sampled: the rectangles' edges cut the target into a grid of
    /// cells, and the target is covered when every cell's centre is inside
    /// one of them. A one-pixel gap between two windows is found. Allocates
    /// nothing; a few windows make a grid of a few dozen cells.
    /// </summary>
    public static class RectCover
    {
        /// <summary>The most rectangles considered. More than this answers
        /// "not covered", which only ever costs drawing something hidden.</summary>
        public const int MaxRects = 16;

        /// <summary>
        /// True when the rectangles in <paramref name="rects"/> (x, y, width,
        /// height, four ints each; empty ones ignored) leave no part of the
        /// target uncovered. An empty target is covered.
        /// </summary>
        public static bool Covers(int x, int y, int width, int height, ReadOnlySpan<int> rects)
        {
            if (width <= 0 || height <= 0)
            {
                return true;
            }

            int count = Math.Min(rects.Length / 4, MaxRects);
            if (rects.Length / 4 > MaxRects)
            {
                return false;
            }

            int right = x + width;
            int bottom = y + height;

            // Cut lines: the target's edges plus every rectangle edge inside it.
            Span<int> xs = stackalloc int[(2 * MaxRects) + 2];
            Span<int> ys = stackalloc int[(2 * MaxRects) + 2];
            int nx = 0;
            int ny = 0;
            xs[nx++] = x;
            xs[nx++] = right;
            ys[ny++] = y;
            ys[ny++] = bottom;
            for (int i = 0; i < count; i++)
            {
                int rx = rects[i * 4];
                int ry = rects[(i * 4) + 1];
                int rw = rects[(i * 4) + 2];
                int rh = rects[(i * 4) + 3];
                if (rw <= 0 || rh <= 0)
                {
                    continue;
                }

                if (rx > x && rx < right) xs[nx++] = rx;
                if (rx + rw > x && rx + rw < right) xs[nx++] = rx + rw;
                if (ry > y && ry < bottom) ys[ny++] = ry;
                if (ry + rh > y && ry + rh < bottom) ys[ny++] = ry + rh;
            }

            Span<int> xcut = xs.Slice(0, nx);
            Span<int> ycut = ys.Slice(0, ny);
            xcut.Sort();
            ycut.Sort();

            for (int xi = 0; xi + 1 < nx; xi++)
            {
                if (xcut[xi] == xcut[xi + 1])
                {
                    continue;
                }

                // Centre of the cell, doubled to stay in integers.
                int cx2 = xcut[xi] + xcut[xi + 1];
                for (int yi = 0; yi + 1 < ny; yi++)
                {
                    if (ycut[yi] == ycut[yi + 1])
                    {
                        continue;
                    }

                    int cy2 = ycut[yi] + ycut[yi + 1];
                    bool inside = false;
                    for (int i = 0; i < count && !inside; i++)
                    {
                        int rx = rects[i * 4];
                        int ry = rects[(i * 4) + 1];
                        int rw = rects[(i * 4) + 2];
                        int rh = rects[(i * 4) + 3];
                        inside = rw > 0 && rh > 0
                            && cx2 > 2 * rx && cx2 < 2 * (rx + rw)
                            && cy2 > 2 * ry && cy2 < 2 * (ry + rh);
                    }

                    if (!inside)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
