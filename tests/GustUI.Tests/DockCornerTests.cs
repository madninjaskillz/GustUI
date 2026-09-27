using System.Collections.Generic;
using GustUI.Elements;
using GustUI.Managers;
using Microsoft.Xna.Framework;

namespace GustUI.Tests
{
    /// <summary>
    /// ezmuze #384: with the pattern explorer docked along the bottom, docking
    /// the sequencer to the left left a block of bare backdrop below one and
    /// beside the other. A side dock stopped at the bottom docks and a bottom
    /// dock started at the side docks, so their shared corner was nobody's. The
    /// dock that was there first now keeps the corner.
    /// </summary>
    public class DockCornerTests
    {
        // The report's layout: 2560 x 1369.33 UI px, a 24 px status bar, the
        // explorer 330 deep along the bottom, the sequencer 1126 wide on the left.
        private static readonly Vector2 Window = new Vector2(2560, 1369.33f);
        private const float StatusBar = 24f;
        private const float Explorer = 330f;
        private const float Sequencer = 1126f;

        private static (Vector2 Position, Vector2 Size) Left(float bottomBefore)
            => DockLayout.DockRect(DockSide.Left, Window, Sequencer, 0f, 0f, bottomBefore, StatusBar);

        private static (Vector2 Position, Vector2 Size) Bottom(float leftBefore)
            => DockLayout.DockRect(DockSide.Bottom, Window, Explorer, 0f, leftBefore, 0f, StatusBar);

        private static float Area((Vector2 Position, Vector2 Size) r) => r.Size.X * r.Size.Y;

        private static float Overlap((Vector2 Position, Vector2 Size) a, (Vector2 Position, Vector2 Size) b)
        {
            float w = System.Math.Min(a.Position.X + a.Size.X, b.Position.X + b.Size.X) - System.Math.Max(a.Position.X, b.Position.X);
            float h = System.Math.Min(a.Position.Y + a.Size.Y, b.Position.Y + b.Size.Y) - System.Math.Max(a.Position.Y, b.Position.Y);
            return System.Math.Max(0f, w) * System.Math.Max(0f, h);
        }

        /// <summary>The free space beside the docks: every inset, whoever owns
        /// the corners.</summary>
        private static float FreeArea() => (Window.X - Sequencer) * (Window.Y - StatusBar - Explorer);

        [Fact]
        public void ADockDockedSecondStopsAtTheOneThatWasThereFirst()
        {
            // The report: the explorer first, then the sequencer to the left.
            var explorer = Bottom(leftBefore: 0f);
            var sequencer = Left(bottomBefore: Explorer);

            // The explorer keeps its full width under the sequencer.
            Assert.Equal(0f, explorer.Position.X);
            Assert.Equal(Window.X, explorer.Size.X);
            Assert.Equal(Window.Y - StatusBar - Explorer, sequencer.Size.Y, 2);
        }

        [Fact]
        public void TheDocksAndTheFreeSpaceTileTheWindowWithNoGap()
        {
            float whole = Window.X * (Window.Y - StatusBar);

            // Either order: whichever docked first owns the corner.
            foreach (bool explorerFirst in new[] { true, false })
            {
                var explorer = Bottom(leftBefore: explorerFirst ? 0f : Sequencer);
                var sequencer = Left(bottomBefore: explorerFirst ? Explorer : 0f);

                Assert.Equal(0f, Overlap(explorer, sequencer));
                Assert.InRange(whole - Area(explorer) - Area(sequencer) - FreeArea(), -2f, 2f);
            }
        }

        [Fact]
        public void TheOldRuleLeftTheCornerToNobody()
        {
            // Both stopping at the other: the 1126 x 330 block of #384.
            var explorer = Bottom(leftBefore: Sequencer);
            var sequencer = Left(bottomBefore: Explorer);
            float whole = Window.X * (Window.Y - StatusBar);

            Assert.InRange(whole - Area(explorer) - Area(sequencer) - FreeArea() - Sequencer * Explorer, -2f, 2f);
        }

        [Fact]
        public void OnlyTheDocksThatDockedEarlierCount()
        {
            // A bottom stack of two, docked at 1 and 5; a side dock docked at 3
            // stops at the first and spans the second.
            var bottoms = new List<(long, float)> { (1, 330f), (5, 200f) };

            Assert.Equal(330f, DockLayout.SumDockedBefore(bottoms, 3));
            Assert.Equal(0f, DockLayout.SumDockedBefore(bottoms, 0));
            Assert.Equal(530f, DockLayout.SumDockedBefore(bottoms, 9));
        }

        [Fact]
        public void ATopOrBottomDockStartsAtItsLeftInsetAndSitsAboveTheStatusBar()
        {
            var bottom = DockLayout.DockRect(DockSide.Bottom, Window, Explorer, 50f, 300f, 200f, StatusBar);
            Assert.Equal(new Vector2(300f, Window.Y - StatusBar - Explorer - 50f), bottom.Position);
            Assert.Equal(new Vector2(Window.X - 500f, Explorer), bottom.Size);

            var top = DockLayout.DockRect(DockSide.Top, Window, 120f, 40f, 300f, 0f, StatusBar);
            Assert.Equal(new Vector2(300f, 40f), top.Position);
        }

        [Fact]
        public void ARightDockHangsFromItsTopInset()
        {
            var right = DockLayout.DockRect(DockSide.Right, Window, 600f, 100f, 80f, Explorer, StatusBar);

            Assert.Equal(new Vector2(Window.X - 700f, 80f), right.Position);
            Assert.Equal(new Vector2(600f, Window.Y - 80f - Explorer - StatusBar), right.Size);
        }
    }
}
