using System.Collections.Generic;
using GustUI.Elements;
using GustUI.Managers;
using GustUI.Traits;
using GustUI.TraitValues;
using Microsoft.Xna.Framework;

namespace GustUI.Tests
{
    /// <summary>
    /// ezmuze #393: a docked window had two drag targets on its inner edge, its
    /// own 6 px resize strip under the 10 px dock splitter.
    /// ezmuze #394: every button held its label twice, and a title bar's pin and
    /// maximise were held under their type name because AddChildElement ignored
    /// the name it was given.
    /// ezmuze #395: a filling window stopped holding room on the docks the
    /// moment it was picked up, so a bottom dock grew under it.
    /// </summary>
    public class EdgeOwnerRoomAndChildrenTests
    {
        private static Element Parent()
        {
            var parent = new Element();
            parent.AddTrait<ChildrenTrait>().Set(new TVElements());
            return parent;
        }

        [Fact]
        public void AChildAddedTwiceIsHeldOnceUnderItsLatestName()
        {
            // BasicButtonElement's pattern: the constructor adds the label,
            // then Setup adds it again under "button Text: ...".
            Element button = Parent();
            Element label = button.AddChildElement<Element>();

            button.AddChild(label, "button Text: OK");

            Assert.Single(button.Children.Items);
            Assert.Equal(1, button.Children.Count);
            Assert.Same(label, button.Children.Get("button Text: OK"));
        }

        [Fact]
        public void ReAddingAChildMovesItAfterItsSiblings()
        {
            Element bar = Parent();
            var first = new Element();
            var second = new Element();
            bar.AddChild(first, "first");
            bar.AddChild(second, "second");

            bar.AddChild(first, "first again");

            Assert.Equal(new[] { second, first }, bar.Children.Items);
        }

        [Fact]
        public void RemovingAChildThatWasAddedTwiceLeavesNothingBehind()
        {
            // With the child held twice, Remove took one copy out and a killed
            // child stayed on screen.
            Element bar = Parent();
            var child = new Element();
            bar.AddChild(child, "a");
            bar.AddChild(child, "b");

            child.Kill();

            Assert.Empty(bar.Children.Items);
            Assert.False(bar.Children.Contains(child));
        }

        [Fact]
        public void AddChildElementUsesTheNameItIsGiven()
        {
            Element bar = Parent();

            Element pin = bar.AddChildElement<Element>("pinButton");

            Assert.Equal("pinButton", pin.ElementName);
            Assert.Same(pin, bar.Children.Get("pinButton"));
        }

        [Fact]
        public void ADockedWindowHasNoResizeHandles()
        {
            // The dock splitter owns the one edge that moves.
            var rects = ResizeHandlesElement.HandleRects(new Vector2(1052f, 281.33f), docked: true);

            Assert.Equal(8, rects.Length);
            Assert.All(rects, r => Assert.Equal(Vector2.Zero, r.Size));
        }

        [Fact]
        public void AFloatingWindowKeepsItsEdgesAndCorners()
        {
            var rects = ResizeHandlesElement.HandleRects(new Vector2(600f, 400f), docked: false);

            Assert.Equal(8, rects.Length);
            Assert.All(rects, r => Assert.True(r.Size.X > 0f && r.Size.Y > 0f));
        }

        [Fact]
        public void APickedUpFillerStillHoldsItsRoomButADockedOneDoesNot()
        {
            // The report: a sequencer (260 min + 24 status bar) picked up off
            // the free space. It floats, so its floor still caps the explorer.
            var floating = new List<(bool, float)> { (false, 284f) };
            Assert.Equal(284f, DockLayout.MaxRoomFloor(floating));

            float window = 562.6667f;
            float explorer = DockLayout.Clamp(330f, window, DockLayout.MaxRoomFloor(floating), 0f);
            float explorerTop = window - 24f - explorer;
            Assert.Equal(260f, explorerTop, 2);

            // Docked, it sets no floor here (its own side must not be capped
            // by its own minimum).
            var docked = new List<(bool, float)> { (true, 284f), (false, 200f) };
            Assert.Equal(200f, DockLayout.MaxRoomFloor(docked));
        }
        [Fact]
        public void OppositeDocksShareTheAxisWithoutOverlapping()
        {
            // The report: the explorer docked along the bottom first, then the
            // sequencer (min 260) dragged to the top. The explorer is held back
            // by the later dock's minimum plus the status bar ...
            float window = 562.6667f;
            float statusBar = 24f;
            float explorer = DockLayout.Clamp(330f, window, 260f + statusBar, 0f);

            // ... and the sequencer fits in what the explorer leaves.
            float sequencer = DockLayout.Clamp(260f, window, statusBar, explorer);

            Assert.Equal(260f, sequencer, 2);
            Assert.True(sequencer + explorer <= window - statusBar + 0.01f);

            // Before, the explorer took its 50% cap and the two overlapped.
            float uncapped = DockLayout.Clamp(330f, window, 0f, 0f);
            Assert.True(260f + uncapped > window - statusBar);
        }

        [Fact]
        public void ATopPreviewShowsTheBottomDockGivingWayToIt()
        {
            var window = new Vector2(1052f, 562.6667f);
            float explorer = window.Y / 2f;

            var preview = DockLayout.PreviewRect(DockSide.Top, window, 260f, 0f, 0f, 0f, 0f, 24f,
                minAlong: 0f, opposite: explorer, minAcross: 260f);

            Assert.Equal(0f, preview.Position.Y);
            Assert.Equal(260f, preview.Size.Y, 2);
        }
    }
}
