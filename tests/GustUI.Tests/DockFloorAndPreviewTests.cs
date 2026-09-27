using System.Collections.Generic;
using GustUI.Elements;
using GustUI.Managers;
using Microsoft.Xna.Framework;

namespace GustUI.Tests
{
    /// <summary>
    /// ezmuze #390: a side dock that docked after a bottom dock got less than its
    /// minimum height, because only fillers set a floor on a dock's size.
    /// ezmuze #391: the bottom dock preview covered the status bar and any bottom
    /// docks, and a side preview was drawn at the window's unclamped width.
    /// </summary>
    public class DockFloorAndPreviewTests
    {
        // The report's window: 1600x900 at 150%, a 24 px status bar.
        private static readonly Vector2 Window = new Vector2(1052f, 562.6667f);
        private const float StatusBar = 24f;
        private const float SequencerMinHeight = 260f;
        private const float Explorer = 330f;

        [Fact]
        public void OnlyDocksThatDockedLaterSetAFloor()
        {
            // Docked at 1 and 5; asked for by a dock that docked at 3.
            var docks = new List<(long, float)> { (1, 500f), (5, 284f) };

            Assert.Equal(284f, DockLayout.MaxFloorDockedAfter(docks, 3));
            Assert.Equal(0f, DockLayout.MaxFloorDockedAfter(docks, 9));
            Assert.Equal(500f, DockLayout.MaxFloorDockedAfter(docks, 0));
        }

        [Fact]
        public void ABottomDockLeavesALaterSideDockItsMinimumHeight()
        {
            // The explorer docked first; the sequencer then docks left, so its
            // floor (min height + status bar) caps the explorer.
            float floor = SequencerMinHeight + StatusBar;
            float explorer = DockLayout.Clamp(Explorer, Window.Y, floor, 0f);
            var sequencer = DockLayout.DockRect(DockSide.Left, Window, 526f, 0f, 0f, explorer, StatusBar);

            Assert.Equal(SequencerMinHeight, sequencer.Size.Y, 2);

            // Without the floor the explorer took its 50% cap: the 257 px of #390.
            float uncapped = DockLayout.Clamp(Explorer, Window.Y, 0f, 0f);
            var squeezed = DockLayout.DockRect(DockSide.Left, Window, 526f, 0f, 0f, uncapped, StatusBar);
            Assert.True(squeezed.Size.Y < SequencerMinHeight);
        }

        [Fact]
        public void ABottomPreviewSitsAboveTheStatusBarAndTheBottomDocks()
        {
            // Nothing docked yet: above the status bar, not over it.
            var alone = DockLayout.PreviewRect(DockSide.Bottom, Window, 200f, 0f, 0f, 0f, 0f, StatusBar);
            Assert.Equal(Window.Y - StatusBar - 200f, alone.Position.Y, 2);
            Assert.Equal(Window.Y - StatusBar, alone.Position.Y + alone.Size.Y, 2);

            // A 150 px bottom dock already there: the new one sits on top of it.
            var stacked = DockLayout.PreviewRect(DockSide.Bottom, Window, 100f, 150f, 0f, 0f, 0f, StatusBar);
            Assert.Equal(Window.Y - StatusBar - 150f, stacked.Position.Y + stacked.Size.Y, 2);
        }

        [Fact]
        public void ABottomPreviewFitsBetweenTheSideDocks()
        {
            var preview = DockLayout.PreviewRect(DockSide.Bottom, Window, 200f, 0f, 0f, 300f, 100f, StatusBar);

            Assert.Equal(300f, preview.Position.X);
            Assert.Equal(Window.X - 400f, preview.Size.X, 2);
        }

        [Fact]
        public void ASidePreviewShowsTheBottomDockGivingWayToItsMinimum()
        {
            // The explorer at its 50% cap (281.33) leaves 257.33; the sequencer
            // needs 260, and once docked the explorer gives way to it.
            float explorer = Window.Y / 2f;
            var preview = DockLayout.PreviewRect(DockSide.Left, Window, 526f, 0f, 0f, 0f, explorer, StatusBar, SequencerMinHeight);
            Assert.Equal(SequencerMinHeight, preview.Size.Y, 2);

            // Room enough already: unchanged.
            var roomy = DockLayout.PreviewRect(DockSide.Left, Window, 526f, 0f, 0f, 0f, 100f, StatusBar, SequencerMinHeight);
            Assert.Equal(Window.Y - 100f - StatusBar, roomy.Size.Y, 2);
        }

        [Fact]
        public void APreviewIsDrawnAtTheSizeTheDockWillHave()
        {
            // A full-width window held at the left edge docks at half the window.
            var left = DockLayout.PreviewRect(DockSide.Left, Window, Window.X, 0f, 0f, 0f, 0f, StatusBar);
            Assert.Equal(Window.X / 2f, left.Size.X, 2);

            // ...and inboard of a panel already docked left, stopping above the
            // bottom dock and the status bar.
            var inboard = DockLayout.PreviewRect(DockSide.Left, Window, 200f, 250f, 0f, 0f, 120f, StatusBar);
            Assert.Equal(250f, inboard.Position.X);
            Assert.Equal(Window.Y - 120f - StatusBar, inboard.Size.Y, 2);
        }
    }
}
