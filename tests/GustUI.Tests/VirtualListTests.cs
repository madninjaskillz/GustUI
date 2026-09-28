using GustUI.Elements;
using GustUI.Extensions;
using GustUI.Traits;
using GustUI.TraitValues;
using System.Collections.Generic;
using System.Linq;

namespace GustUI.Tests
{
    /// <summary>
    /// ezmuze #450: the windowed list the file browser and the wave browser
    /// are built on. The arithmetic (which rows are in view, where to scroll
    /// to show one), the pool (a scroll rebinds only the rows that came into
    /// view, and never grows past the window) and the element that puts the
    /// two together.
    /// </summary>
    public class VirtualListTests
    {
        // ------------------------------------------------------ the window

        [Fact]
        public void AtTheTopTheWindowIsTheRowsInViewPlusOverscanBelow()
        {
            // 24px rows in a 100px viewport: rows 0-4 are in view (row 4 is
            // cut off at 96..120), and three more below.
            Assert.Equal((0, 8), VirtualListWindow.Range(0f, 100f, 24f, 0f, 2000, 3));
        }

        [Fact]
        public void MidListTheWindowHasOverscanOnBothSides()
        {
            // Scrolled 1000px: row 41 (984..1008) is the first in view, row 45
            // (1080..1104) the last.
            Assert.Equal((38, 49), VirtualListWindow.Range(1000f, 100f, 24f, 0f, 2000, 3));
        }

        [Fact]
        public void AtTheEndTheWindowStopsAtTheLastRow()
        {
            float max = VirtualListWindow.MaxScroll(2000, 24f, 0f, 100f);
            (int first, int end) = VirtualListWindow.Range(max, 100f, 24f, 0f, 2000, 3);
            Assert.Equal(2000, end);
            Assert.Equal(1995 - 3, first);
        }

        [Fact]
        public void ARowThatOnlyTouchesTheBottomEdgeIsNotInTheWindow()
        {
            // 96px viewport of 24px rows: exactly four rows, the fifth starts
            // at the edge.
            Assert.Equal((0, 4), VirtualListWindow.Range(0f, 96f, 24f, 0f, 100, 0));
        }

        [Fact]
        public void AHeaderPushesTheRowsDown()
        {
            // A 60px header: in a 100px viewport at the top, only rows 0 and 1
            // (60..84, 84..108) are in view.
            Assert.Equal((0, 2), VirtualListWindow.Range(0f, 100f, 24f, 60f, 100, 0));
            Assert.Equal(60f + (10 * 24f), VirtualListWindow.RowTop(10, 24f, 60f));
            Assert.Equal(60f + (100 * 24f), VirtualListWindow.ContentHeight(100, 24f, 60f));
        }

        [Fact]
        public void NothingToShowMeansAnEmptyWindow()
        {
            Assert.Equal((0, 0), VirtualListWindow.Range(0f, 100f, 24f, 0f, 0, 3));
            Assert.Equal((0, 0), VirtualListWindow.Range(0f, 0f, 24f, 0f, 50, 3));
            Assert.Equal((0, 0), VirtualListWindow.Range(0f, 100f, 0f, 0f, 50, 3));
        }

        [Fact]
        public void AShortListIsAllInTheWindow()
        {
            Assert.Equal((0, 3), VirtualListWindow.Range(0f, 500f, 24f, 0f, 3, 3));
        }

        [Fact]
        public void IndexAtFindsTheRowUnderAContentY()
        {
            Assert.Equal(-1, VirtualListWindow.IndexAt(10f, 24f, 30f, 100));
            Assert.Equal(0, VirtualListWindow.IndexAt(30f, 24f, 30f, 100));
            Assert.Equal(1, VirtualListWindow.IndexAt(54f, 24f, 30f, 100));
            Assert.Equal(-1, VirtualListWindow.IndexAt(30f + (100 * 24f), 24f, 30f, 100));
        }

        // ------------------------------------------------ scrolling to one

        [Fact]
        public void RevealLeavesARowAlreadyInViewWhereItIs()
        {
            Assert.Equal(240f, VirtualListWindow.Reveal(11, 240f, 100f, 24f, 0f, 2000));
        }

        [Fact]
        public void RevealBringsARowAboveToTheTop()
        {
            Assert.Equal(5 * 24f, VirtualListWindow.Reveal(5, 240f, 100f, 24f, 0f, 2000));
        }

        [Fact]
        public void RevealBringsARowBelowToTheBottom()
        {
            // Row 20 is 480..504: its bottom goes to the viewport's bottom.
            Assert.Equal(504f - 100f, VirtualListWindow.Reveal(20, 0f, 100f, 24f, 0f, 2000));
        }

        [Fact]
        public void CentrePutsARowInTheMiddleAndClampsAtTheEnds()
        {
            Assert.Equal((100 * 24f) + 12f - 50f, VirtualListWindow.Centre(100, 100f, 24f, 0f, 2000));
            Assert.Equal(0f, VirtualListWindow.Centre(1, 100f, 24f, 0f, 2000));
            Assert.Equal(VirtualListWindow.MaxScroll(2000, 24f, 0f, 100f), VirtualListWindow.Centre(1999, 100f, 24f, 0f, 2000));
        }

        [Fact]
        public void AnIndexOutsideTheListDoesNotScroll()
        {
            Assert.Equal(0f, VirtualListWindow.Centre(-1, 100f, 24f, 0f, 20));
            Assert.Equal(48f, VirtualListWindow.Reveal(50, 48f, 100f, 24f, 0f, 20));
        }

        // ------------------------------------------------------ the pool

        private sealed class Row
        {
            public int Shows = -1;
        }

        private static RowRecycler<Row> Pool(List<string> log) =>
            new RowRecycler<Row>(
                () => new Row(),
                (row, index) => { row.Shows = index; log.Add("bind " + index); },
                (row, index) => log.Add("park " + index));

        [Fact]
        public void TheFirstWindowCreatesOneRowPerIndex()
        {
            var log = new List<string>();
            RowRecycler<Row> pool = Pool(log);

            pool.Realise(0, 8);

            Assert.Equal(8, pool.Created);
            Assert.Equal(8, pool.LiveCount);
            Assert.Equal(Enumerable.Range(0, 8).Select(i => "bind " + i), log);
        }

        [Fact]
        public void ScrollingOneRowRebindsOnlyTheRowThatCameIntoView()
        {
            var log = new List<string>();
            RowRecycler<Row> pool = Pool(log);
            pool.Realise(0, 8);
            Row leaving = pool.RowAt(0);
            log.Clear();

            pool.Realise(1, 9);

            Assert.Equal(new[] { "park 0", "bind 8" }, log);
            Assert.Same(leaving, pool.RowAt(8));
            Assert.Equal(8, pool.Created);
            Assert.Equal(-1, pool.IndexOf(new Row()));
            Assert.Equal(8, pool.IndexOf(leaving));
        }

        [Fact]
        public void TheSameWindowDoesNothing()
        {
            var log = new List<string>();
            RowRecycler<Row> pool = Pool(log);
            pool.Realise(10, 20);
            log.Clear();

            pool.Realise(10, 20);

            Assert.Empty(log);
        }

        [Fact]
        public void AJumpAcrossTheWholeListReusesEveryRowAndCreatesNone()
        {
            var log = new List<string>();
            RowRecycler<Row> pool = Pool(log);
            pool.Realise(0, 10);

            pool.Realise(1500, 1510);

            Assert.Equal(10, pool.Created);
            Assert.Equal(10, pool.LiveCount);
            Assert.Equal(0, pool.FreeCount);
            Assert.All(pool.Live(), live => Assert.Equal(live.Index, live.Row.Shows));
        }

        [Fact]
        public void ScrollingTheWholeListNeverGrowsThePoolPastTheWindow()
        {
            var log = new List<string>();
            RowRecycler<Row> pool = Pool(log);
            const int Count = 2000;

            for (float scroll = 0; scroll <= VirtualListWindow.MaxScroll(Count, 24f, 0f, 450f); scroll += 37f)
            {
                (int first, int end) = VirtualListWindow.Range(scroll, 450f, 24f, 0f, Count, 3);
                pool.Realise(first, end);
            }

            // 450 / 24 is 18.75, so at most 20 rows touch the viewport, plus
            // three on each side.
            Assert.True(pool.Created <= 26, "created " + pool.Created);
            Assert.All(pool.Live(), live => Assert.Equal(live.Index, live.Row.Shows));
        }

        [Fact]
        public void ShrinkingTheWindowParksTheRestAndKeepsThemForLater()
        {
            var log = new List<string>();
            RowRecycler<Row> pool = Pool(log);
            pool.Realise(0, 10);

            pool.Realise(0, 4);
            Assert.Equal(4, pool.LiveCount);
            Assert.Equal(6, pool.FreeCount);

            pool.Realise(0, 10);
            Assert.Equal(10, pool.Created);
        }

        [Fact]
        public void RebindAllBindsTheLiveRowsAgainInOrder()
        {
            var log = new List<string>();
            RowRecycler<Row> pool = Pool(log);
            pool.Realise(5, 8);
            log.Clear();

            pool.RebindAll();

            Assert.Equal(new[] { "bind 5", "bind 6", "bind 7" }, log);
            Assert.True(pool.Rebind(6));
            Assert.False(pool.Rebind(40));
        }

        [Fact]
        public void ForgetStartsAgainFromNothing()
        {
            var log = new List<string>();
            RowRecycler<Row> pool = Pool(log);
            pool.Realise(0, 5);
            log.Clear();

            pool.Forget();
            pool.Realise(0, 5);

            Assert.DoesNotContain(log, line => line.StartsWith("park"));
            Assert.Equal(10, pool.Created);
        }

        // ------------------------------------------------------ the element

        private static VirtualListElement List(int count, float height, List<int> bound, out List<Element> made)
        {
            var created = new List<Element>();
            var list = new VirtualListElement(24f,
                () =>
                {
                    var row = new FilledRectangleElement(0, 0, 100, 22, new TVFillSolidColor(Microsoft.Xna.Framework.Color.Transparent));
                    created.Add(row);
                    return row;
                },
                (row, index) => bound.Add(index));
            list.Set<SizeTrait>(new TVVector(200, height));
            list.SetCount(count);
            made = created;
            return list;
        }

        [Fact]
        public void TheElementOnlyPutsTheWindowInTheTree()
        {
            var bound = new List<int>();
            VirtualListElement list = List(2000, 100f, bound, out List<Element> made);

            // The spacer plus rows 0..7 — not 2,000 rows.
            Assert.Equal(1 + 8, list.ContentChildren.Count);
            Assert.Equal(8, made.Count);
            Assert.Equal(0f, list.RowAt(3).GetRelativePosition().Y - (3 * 24f));
            Assert.Equal(3, list.IndexOfRow(list.RowAt(3)));
            Assert.Equal("row-3", list.RowAt(3).ElementName);
        }

        [Fact]
        public void TheElementsContentIsAsTallAsEveryRow()
        {
            var bound = new List<int>();
            VirtualListElement list = List(2000, 100f, bound, out _);
            list.HeaderHeight = 40f;
            list.Reconcile();

            Assert.Equal(40f + (2000 * 24f), list.ContentChildren.Items.Max(c => c.GetRelativePosition().Y + c.GetSize().Y));
            Assert.Equal(40f, list.RowAt(0).GetRelativePosition().Y);
        }

        [Fact]
        public void ScrollingTheElementMovesRowsOutOfTheTreeAndReusesThem()
        {
            var bound = new List<int>();
            VirtualListElement list = List(2000, 100f, bound, out List<Element> made);
            Element first = list.RowAt(0);

            list.ScrollToIndex(1000, centre: true);
            list.Reconcile();

            Assert.Null(list.RowAt(0));
            Assert.NotEqual(0, list.IndexOfRow(first));
            Assert.Equal(8 + 3, made.Count);
            Assert.NotNull(list.RowAt(1000));
            Assert.InRange(list.ScrollPosition, (1000 * 24f) - 100f, 1000 * 24f);
            Assert.Equal(1 + 11, list.ContentChildren.Count);
        }

        [Fact]
        public void ScrollToIndexWaitsForTheListToHaveASize()
        {
            var bound = new List<int>();
            VirtualListElement list = List(2000, 0f, bound, out _);

            list.ScrollToIndex(500, centre: true);
            Assert.Equal(0f, list.ScrollPosition);

            list.Set<SizeTrait>(new TVVector(200, 100));
            list.Reconcile();

            Assert.Equal(VirtualListWindow.Centre(500, 100f, 24f, 0f, 2000), list.ScrollPosition);
            Assert.NotNull(list.RowAt(500));
        }

        [Fact]
        public void RebindBindsTheWindowAgainWithoutMovingIt()
        {
            var bound = new List<int>();
            VirtualListElement list = List(2000, 100f, bound, out _);
            list.ScrollToIndex(300);
            list.Reconcile();
            float scroll = list.ScrollPosition;
            bound.Clear();

            list.Rebind();

            Assert.Equal(scroll, list.ScrollPosition);
            Assert.Equal(Enumerable.Range(list.FirstRealised, list.EndRealised - list.FirstRealised), bound);
        }

        [Fact]
        public void ClearChildrenEmptiesTheListAndScrollsToTheTop()
        {
            var bound = new List<int>();
            VirtualListElement list = List(2000, 100f, bound, out _);
            list.ScrollToIndex(300);
            list.Reconcile();

            list.ClearChildren();
            list.Reconcile();

            Assert.Equal(0, list.Count);
            Assert.Equal(0f, list.ScrollPosition);
            Assert.Equal(1, list.ContentChildren.Count);
        }
    }
}
