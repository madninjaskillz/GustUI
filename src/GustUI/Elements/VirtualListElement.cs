using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace GustUI.Elements
{
    /// <summary>
    /// A scrolling list that only builds the rows you can see.
    ///
    /// A <see cref="VerticalScrollElement"/> with one element tree per item
    /// costs a tree per item whether it is on screen or not: building a folder
    /// of 2,000 files built 10,000 elements and froze the frame for most of a
    /// second, and every one of them was walked on every frame after that
    /// (ezmuze #450). This keeps a POOL of rows the size of the viewport plus
    /// <see cref="Overscan"/> on each side, positions each at its item's place
    /// in the content, and when scrolling moves an item out of view hands its
    /// row to the item coming in, through the host's bind callback. The
    /// scrollbar still sees the whole list, because a spacer as tall as every
    /// row put together sits in the content.
    ///
    /// ROWS ARE A FIXED PITCH (<see cref="RowHeight"/>). That is what makes
    /// "which items are in view" arithmetic rather than a measurement
    /// (<see cref="VirtualListWindow"/>). Anything of another height the list
    /// needs above its rows — a back row, a banner, a grid of cards — is added
    /// as an ordinary child with <see cref="AddChild"/> and reserved with
    /// <see cref="HeaderHeight"/>; the rows start below it.
    ///
    /// A row is created once and BOUND many times, so it must not capture the
    /// item it was first built for: handlers on it look the item up with
    /// <see cref="IndexOfRow"/> when they fire. Children of the row can be
    /// rebuilt inside the bind — a handful per row in view is cheap, which is
    /// the point.
    ///
    /// Rows that leave the window are taken out of the tree, not hidden, so
    /// <c>/tree</c> and hit testing only ever see the rows in the window.
    /// </summary>
    public class VirtualListElement : VerticalScrollElement
    {
        private readonly Func<Element> createRow;
        private readonly Action<Element, int> bindRow;
        private readonly Action<Element, int> parkRow;
        private readonly RowRecycler<Element> recycler;

        /// <summary>Stands in for every row: as tall as the whole list, so the
        /// content (and the scrollbar that reads it) is.</summary>
        private readonly FilledRectangleElement spacer;

        private int count;
        private float headerHeight;
        private bool rebindAll;

        /// <summary>A scroll asked for before the list had a size to scroll in
        /// (a row selected in the same frame the listing arrived); applied on
        /// the next update, once it has.</summary>
        private (int Index, bool Centre)? pendingReveal;

        /// <param name="rowHeight">The row PITCH: every row is placed this far
        /// below the last. Paint a row shorter than it for a gap.</param>
        /// <param name="createRow">Makes an empty row. Attach handlers here;
        /// they find their item through <see cref="IndexOfRow"/>.</param>
        /// <param name="bindRow">Shows item <c>index</c> in a row. Called when
        /// the row comes into the window, and again by <see cref="Rebind()"/>.
        /// The row is already positioned; size it and fill it.</param>
        /// <param name="parkRow">Optional: a row leaving the window, with the
        /// index it was showing — for a host that tracks something per row.</param>
        public VirtualListElement(float rowHeight, Func<Element> createRow, Action<Element, int> bindRow,
            Action<Element, int> parkRow = null)
        {
            if (rowHeight <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(rowHeight), "A virtual list needs a positive row height.");
            }

            RowHeight = rowHeight;
            this.createRow = createRow ?? throw new ArgumentNullException(nameof(createRow));
            this.bindRow = bindRow ?? throw new ArgumentNullException(nameof(bindRow));
            this.parkRow = parkRow;

            recycler = new RowRecycler<Element>(createRow, BindInPlace, ParkRow);

            spacer = new FilledRectangleElement(0, 0, 1, 0, new TVFillSolidColor(Color.Transparent));
            AddChild(spacer, "virtual-spacer");
        }

        /// <summary>The row pitch.</summary>
        public float RowHeight { get; }

        /// <summary>Rows built beyond each edge of the viewport, so a wheel
        /// notch shows rows that already exist.</summary>
        public int Overscan { get; set; } = 3;

        /// <summary>What a row is called in the tree while it shows item
        /// <c>index</c> (<c>/tree</c>, <c>/click {"name":..}</c>). Defaults to
        /// <c>row-&lt;index&gt;</c>.</summary>
        public Func<int, string> RowName { get; set; }

        /// <summary>How many items the list has.</summary>
        public int Count => count;

        /// <summary>Content above row 0 — header children the host added
        /// itself. Row <c>i</c> sits at <c>HeaderHeight + i * RowHeight</c>.</summary>
        public float HeaderHeight
        {
            get => headerHeight;
            set
            {
                if (headerHeight != value)
                {
                    headerHeight = Math.Max(0f, value);
                    rebindAll = true;
                }
            }
        }

        /// <summary>First item in the window (built, not necessarily on screen).</summary>
        public int FirstRealised => recycler.First;

        /// <summary>One past the last item in the window.</summary>
        public int EndRealised => recycler.End;

        /// <summary>Rows ever built — stays near the viewport's worth however
        /// long the list is.</summary>
        public int RowsCreated => recycler.Created;

        /// <summary>Rows bound so far.</summary>
        public int RowBinds => recycler.Binds;

        /// <summary>
        /// Says how many items there are. Every row in the window is bound
        /// again, since what an index means may have changed; the scroll
        /// position is kept, clamped to the new length. A new listing usually
        /// wants <see cref="ClearChildren"/> first, which also scrolls to the
        /// top.
        /// </summary>
        public void SetCount(int newCount)
        {
            count = Math.Max(0, newCount);
            rebindAll = true;
            Reconcile();
        }

        /// <summary>The items changed in place (a selection, a waveform that
        /// arrived) but not how many there are: every row in the window is
        /// bound again, now. The scroll does not move.</summary>
        public void Rebind()
        {
            rebindAll = true;
            Reconcile();
        }

        /// <summary>Binds one item's row again, if it is in the window.</summary>
        public void Rebind(int index) => recycler.Rebind(index);

        /// <summary>The row showing <paramref name="index"/>, or null when it is
        /// not in the window.</summary>
        public Element RowAt(int index) => recycler.RowAt(index);

        /// <summary>The item <paramref name="row"/> is showing, or -1 when it
        /// is not a row of this list or is not in use. What a row's handlers
        /// call to find out what they were clicked on.</summary>
        public int IndexOfRow(Element row) => recycler.IndexOf(row);

        /// <summary>Every row in the window, with its item, in item order.</summary>
        public IEnumerable<(int Index, Element Row)> RealisedRows() => recycler.Live();

        /// <summary>
        /// Scrolls so item <paramref name="index"/> is in view: the least
        /// movement that shows it whole, or with <paramref name="centre"/> set,
        /// the middle of the viewport. Safe to call in the same frame the
        /// count was set — it waits for the list's size if it has none yet.
        /// </summary>
        public void ScrollToIndex(int index, bool centre = false)
        {
            pendingReveal = (index, centre);
            ApplyPendingReveal();
        }

        /// <summary>Removes the rows AND every header child, and scrolls to the
        /// top: a new listing. <see cref="Count"/> goes to 0 and
        /// <see cref="HeaderHeight"/> to 0 with them.</summary>
        public override void ClearChildren()
        {
            base.ClearChildren();

            // The live rows went with the rest of the content; the free ones
            // were already out of the tree. Neither is kept: a row made for
            // the old listing may carry state from it.
            recycler.Forget();
            count = 0;
            headerHeight = 0f;
            pendingReveal = null;
            rebindAll = false;
            spacer.Set<SizeTrait>(new TVVector(1, 0));
            AddChild(spacer, "virtual-spacer");
        }

        public override void Update(Element parent = null)
        {
            Reconcile();
            base.Update(parent);
        }

        /// <summary>
        /// Brings the window up to date with the scroll, the size and the
        /// count: the spacer to the list's full height, a waiting scroll-to
        /// applied, rows outside the window parked and rows inside it bound.
        /// Run every update, BEFORE the children update, so a row that just
        /// came into view is laid out in the same frame it is drawn.
        /// </summary>
        internal void Reconcile()
        {
            float contentHeight = VirtualListWindow.ContentHeight(count, RowHeight, headerHeight);
            if (spacer.GetSize().Y != contentHeight)
            {
                spacer.Set<SizeTrait>(new TVVector(1, contentHeight));
            }

            SyncScrollExtent();
            ApplyPendingReveal();

            float viewport = this.GetSize().Y;
            (int first, int end) = VirtualListWindow.Range(ScrollPosition, viewport, RowHeight, headerHeight, count, Overscan);

            if (rebindAll)
            {
                rebindAll = false;

                // Park everything that no longer exists first, then bind what
                // is left in place: an index past the new end must not be
                // bound to an item that is not there.
                recycler.Realise(first, end);
                recycler.RebindAll();
                return;
            }

            recycler.Realise(first, end);
        }

        private void ApplyPendingReveal()
        {
            if (pendingReveal == null)
            {
                return;
            }

            (int index, bool centre) = pendingReveal.Value;

            float viewport = this.GetSize().Y;
            if (viewport <= 0f)
            {
                return;
            }

            pendingReveal = null;
            if (index < 0 || index >= count)
            {
                return;
            }

            ScrollPosition = centre
                ? VirtualListWindow.Centre(index, viewport, RowHeight, headerHeight, count)
                : VirtualListWindow.Reveal(index, ScrollPosition, viewport, RowHeight, headerHeight, count);
        }

        private void BindInPlace(Element row, int index)
        {
            row.Set<PositionTrait>(new TVVector(0, VirtualListWindow.RowTop(index, RowHeight, headerHeight)));
            string name = RowName?.Invoke(index) ?? "row-" + index;
            if (row.Parent == null)
            {
                AddChild(row, name);
            }
            else if (row.ElementName != name)
            {
                row.ElementName = name;
                ContentChildren.Rename(row, name);
            }

            bindRow(row, index);
        }

        private void ParkRow(Element row, int index)
        {
            parkRow?.Invoke(row, index);
            row.Kill();
        }
    }
}
