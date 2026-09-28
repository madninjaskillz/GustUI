using System;
using System.Collections.Generic;

namespace GustUI.Elements
{
    /// <summary>
    /// The arithmetic behind <see cref="VirtualListElement"/>: which rows of a
    /// fixed-pitch list are in view, where a row sits, and where to scroll to
    /// bring one into view. Pure functions of numbers, so the windowing can be
    /// tested without an element tree or a graphics device.
    ///
    /// Every y here is a CONTENT y: 0 is the top of the scrolled content, not
    /// of the viewport. <c>top</c> is the space above row 0 (a header the host
    /// laid out itself), so row <c>i</c> starts at <c>top + i * rowHeight</c>.
    /// </summary>
    public static class VirtualListWindow
    {
        /// <summary>
        /// The rows to materialise for a viewport scrolled to
        /// <paramref name="scroll"/>: every row that overlaps it, plus
        /// <paramref name="overscan"/> more on each side so a wheel notch
        /// shows rows that already exist. <c>End</c> is exclusive. Empty
        /// (0, 0) when there is nothing to show or no viewport yet.
        /// </summary>
        public static (int First, int End) Range(float scroll, float viewport, float rowHeight, float top, int count, int overscan)
        {
            if (count <= 0 || rowHeight <= 0f || viewport <= 0f)
            {
                return (0, 0);
            }

            overscan = Math.Max(0, overscan);
            int first = (int)Math.Floor((scroll - top) / rowHeight) - overscan;

            // The last row whose top is above the viewport's bottom edge; a
            // row that only touches the edge is not in view.
            int end = (int)Math.Ceiling((scroll + viewport - top) / rowHeight) + overscan;

            first = Math.Clamp(first, 0, count);
            end = Math.Clamp(end, first, count);
            return (first, end);
        }

        /// <summary>Content y of row <paramref name="index"/>'s top.</summary>
        public static float RowTop(int index, float rowHeight, float top) => top + (index * rowHeight);

        /// <summary>The height the content needs for <paramref name="count"/> rows.</summary>
        public static float ContentHeight(int count, float rowHeight, float top) => top + (Math.Max(0, count) * rowHeight);

        /// <summary>The largest scroll offset: content less viewport, never negative.</summary>
        public static float MaxScroll(int count, float rowHeight, float top, float viewport) =>
            Math.Max(0f, ContentHeight(count, rowHeight, top) - viewport);

        /// <summary>The row under content y <paramref name="contentY"/>, or -1
        /// when it is in the header or below the last row.</summary>
        public static int IndexAt(float contentY, float rowHeight, float top, int count)
        {
            if (rowHeight <= 0f || contentY < top)
            {
                return -1;
            }

            int index = (int)Math.Floor((contentY - top) / rowHeight);
            return index < count ? index : -1;
        }

        /// <summary>
        /// The least scroll that shows row <paramref name="index"/> whole:
        /// unchanged when it already is, its top at the viewport's top when it
        /// is above, its bottom at the viewport's bottom when it is below. What
        /// a keyboard move through a list wants — the view only moves when it
        /// has to.
        /// </summary>
        public static float Reveal(int index, float scroll, float viewport, float rowHeight, float top, int count)
        {
            float max = MaxScroll(count, rowHeight, top, viewport);
            if (index < 0 || index >= count)
            {
                return Math.Clamp(scroll, 0f, max);
            }

            float rowTop = RowTop(index, rowHeight, top);
            float target = scroll;
            if (rowTop < scroll)
            {
                target = rowTop;
            }
            else if (rowTop + rowHeight > scroll + viewport)
            {
                target = rowTop + rowHeight - viewport;
            }

            return Math.Clamp(target, 0f, max);
        }

        /// <summary>The scroll that puts row <paramref name="index"/> in the
        /// middle of the viewport, as near as the ends of the list allow. What
        /// "show me this one" wants — it lands where the eye already is.</summary>
        public static float Centre(int index, float viewport, float rowHeight, float top, int count)
        {
            float max = MaxScroll(count, rowHeight, top, viewport);
            if (index < 0 || index >= count)
            {
                return 0f;
            }

            float target = RowTop(index, rowHeight, top) + (rowHeight / 2f) - (viewport / 2f);
            return Math.Clamp(target, 0f, max);
        }
    }

    /// <summary>
    /// The pool behind <see cref="VirtualListElement"/>: keeps one row object
    /// per index in a window, hands rows that leave the window back to a free
    /// list, and binds a free (or, only when none is free, a new) row to each
    /// index that enters it. Generic over the row type so the bookkeeping can
    /// be tested with plain objects.
    ///
    /// A row that stays in the window is NOT rebound when the window moves,
    /// which is what keeps a scroll to one bind per row that comes into view
    /// rather than one per row on screen.
    /// </summary>
    public sealed class RowRecycler<TRow> where TRow : class
    {
        private readonly Func<TRow> create;
        private readonly Action<TRow, int> bind;
        private readonly Action<TRow, int> park;

        private readonly Dictionary<int, TRow> live = new();
        private readonly Dictionary<TRow, int> indexOf = new(ReferenceEqualityComparer.Instance);
        private readonly Stack<TRow> free = new();
        private readonly List<int> leaving = new();

        /// <param name="create">Makes a new row. Called only when the free
        /// list is empty.</param>
        /// <param name="bind">Shows item <c>index</c> in the row.</param>
        /// <param name="park">The row has left the window (it was showing
        /// <c>index</c>) and is going on the free list.</param>
        public RowRecycler(Func<TRow> create, Action<TRow, int> bind, Action<TRow, int> park = null)
        {
            this.create = create ?? throw new ArgumentNullException(nameof(create));
            this.bind = bind ?? throw new ArgumentNullException(nameof(bind));
            this.park = park;
        }

        /// <summary>First index in the window.</summary>
        public int First { get; private set; }

        /// <summary>One past the last index in the window.</summary>
        public int End { get; private set; }

        /// <summary>Rows ever made — the pool's high-water mark, which is what
        /// shows the windowing is working (it stays near the rows on screen
        /// however long the list).</summary>
        public int Created { get; private set; }

        /// <summary>Binds done, over the recycler's life.</summary>
        public int Binds { get; private set; }

        public int LiveCount => live.Count;

        public int FreeCount => free.Count;

        /// <summary>
        /// Makes the window [<paramref name="first"/>, <paramref name="end"/>).
        /// Rows outside it are parked first, so the rows entering it reuse
        /// them rather than growing the pool.
        /// </summary>
        public void Realise(int first, int end)
        {
            if (end < first)
            {
                end = first;
            }

            if (first == First && end == End && live.Count == end - first)
            {
                return;
            }

            leaving.Clear();
            foreach (int index in live.Keys)
            {
                if (index < first || index >= end)
                {
                    leaving.Add(index);
                }
            }

            foreach (int index in leaving)
            {
                TRow row = live[index];
                live.Remove(index);
                indexOf.Remove(row);
                park?.Invoke(row, index);
                free.Push(row);
            }

            for (int index = first; index < end; index++)
            {
                if (live.ContainsKey(index))
                {
                    continue;
                }

                TRow row;
                if (free.Count > 0)
                {
                    row = free.Pop();
                }
                else
                {
                    row = create();
                    Created++;
                }

                live[index] = row;
                indexOf[row] = index;
                Bind(row, index);
            }

            First = first;
            End = end;
        }

        /// <summary>Binds every live row again, in index order — the data
        /// behind them changed in place (a selection, a waveform that
        /// arrived), but not how many there are.</summary>
        public void RebindAll()
        {
            for (int index = First; index < End; index++)
            {
                if (live.TryGetValue(index, out TRow row))
                {
                    Bind(row, index);
                }
            }
        }

        /// <summary>Binds one row again, if it is live. False when that index
        /// is not in the window, which is not an error: it will be bound when
        /// it scrolls in.</summary>
        public bool Rebind(int index)
        {
            if (!live.TryGetValue(index, out TRow row))
            {
                return false;
            }

            Bind(row, index);
            return true;
        }

        /// <summary>The live row showing <paramref name="index"/>, or null.</summary>
        public TRow RowAt(int index) => live.TryGetValue(index, out TRow row) ? row : null;

        /// <summary>The index <paramref name="row"/> is showing, or -1 when it
        /// is parked or not one of these rows.</summary>
        public int IndexOf(TRow row) => row != null && indexOf.TryGetValue(row, out int index) ? index : -1;

        /// <summary>Every live row with its index, in index order.</summary>
        public IEnumerable<(int Index, TRow Row)> Live()
        {
            for (int index = First; index < End; index++)
            {
                if (live.TryGetValue(index, out TRow row))
                {
                    yield return (index, row);
                }
            }
        }

        /// <summary>Drops every row, live and free, without parking them — for
        /// when their owner has already thrown them away. The next
        /// <see cref="Realise"/> starts from nothing.</summary>
        public void Forget()
        {
            live.Clear();
            indexOf.Clear();
            free.Clear();
            First = 0;
            End = 0;
        }

        private void Bind(TRow row, int index)
        {
            Binds++;
            bind(row, index);
        }
    }
}
