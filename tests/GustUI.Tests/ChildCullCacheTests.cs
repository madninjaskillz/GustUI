using System.Collections.Generic;
using GustUI.Elements;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Tests
{
    /// <summary>
    /// ezmuze #652: the child-visibility cull cache was keyed on the clip and
    /// the children, not on where the element itself was. A list row that had
    /// been half outside the viewport kept the half it could see then -- its
    /// name and waveform, without its buttons or detail line -- after being
    /// scrolled fully into view, because moving the row marks its PARENT's
    /// cache dirty and not its own.
    /// </summary>
    public class ChildCullCacheTests
    {
        private static Element Box(float x, float y, float w, float h)
        {
            var element = new Element();
            element.AddTrait<ChildrenTrait>().Set(new TVElements());
            element.AddTrait<PositionTrait>().Set(new TVVector(x, y));
            element.AddTrait<SizeTrait>().Set(new TVVector(w, h));
            return element;
        }

        [Fact]
        public void ARowScrolledIntoViewSeesAllItsChildren()
        {
            // A viewport 0..100 high, and a row 54 high whose top is at 80:
            // its top child (y 4) is inside, its lower one (y 30) is not.
            Element viewport = Box(0, 0, 400, 100);
            Element row = Box(0, 80, 400, 54);
            viewport.AddChild(row, "row");
            Element top = Box(10, 4, 50, 20);
            Element lower = Box(10, 30, 50, 20);
            row.AddChild(top, "top");
            row.AddChild(lower, "lower");

            var clip = new Rectangle(0, 0, 400, 100);
            List<Element> before = row.GetVisibleChildren(row.Children.Items, clip, row.Children.Version);
            Assert.Equal(new[] { top }, before);

            // Scrolled up by 60: the whole row is now inside the viewport. The
            // clip and the row's children are exactly as they were.
            row.ElementTrait<PositionTrait>().Set(new TVVector(0, 20));

            List<Element> after = row.GetVisibleChildren(row.Children.Items, clip, row.Children.Version);
            Assert.Equal(new[] { top, lower }, after);
        }

        [Fact]
        public void NothingMovedIsStillACacheHit()
        {
            Element viewport = Box(0, 0, 400, 100);
            Element row = Box(0, 10, 400, 54);
            viewport.AddChild(row, "row");
            row.AddChild(Box(10, 4, 50, 20), "child");

            var clip = new Rectangle(0, 0, 400, 100);
            List<Element> first = row.GetVisibleChildren(row.Children.Items, clip, row.Children.Version);
            List<Element> second = row.GetVisibleChildren(row.Children.Items, clip, row.Children.Version);

            Assert.Same(first, second);
            Assert.Single(second);
        }
    }
}
