using GustUI.Managers;

namespace GustUI.Tests
{
    /// <summary>
    /// ezmuze #379: a docked panel is drawn at its clamped size, but it still
    /// WANTS its own size. The clamp used to be written back as the panel's
    /// natural size, so the pattern explorer opened at 118 px in a 1600x900
    /// window and only ever shrank from there.
    /// </summary>
    public class DockPreferredSizeTests
    {
        // 1600x900 at 150%: 562.67 UI px tall. The sequencer's floor is its
        // MinSize.Y plus the 24 px status bar.
        private const float Axis = 562.67f;

        [Fact]
        public void TheOldSequencerFloorLeftTheExplorer118Px()
        {
            Assert.Equal(118.67f, DockLayout.Clamp(330f, Axis, 420f + 24f, 0f), 2);
        }

        [Fact]
        public void TheLowerSequencerFloorLeavesItMostOfItsHeight()
        {
            Assert.Equal(278.67f, DockLayout.Clamp(330f, Axis, 260f + 24f, 0f), 2);
        }

        [Fact]
        public void AClampWrittenBackIsNotANewPreference()
        {
            // LayoutDocked wrote the clamped 65 into a panel that wanted 330.
            var note = (Written: 65f, Preferred: 330f);

            Assert.Equal(330f, DockLayout.PreferredSize(65f, note));
        }

        [Fact]
        public void TheWindowGrowingBackGivesThePanelItsHeightBack()
        {
            // Squeezed: the window shrinks to 760 UI px tall's worth of room.
            float wanted = 330f;
            float squeezed = DockLayout.Clamp(wanted, 450f, 284f, 0f);
            Assert.True(squeezed < wanted);

            // Next frame the SizeTrait holds the squeeze; the panel still wants 330.
            float natural = DockLayout.PreferredSize(squeezed, (squeezed, wanted));
            float grown = DockLayout.Clamp(natural, 1000f, 284f, 0f);

            Assert.Equal(330f, grown);
        }

        [Fact]
        public void AnythingElseSizingThePanelIsANewPreference()
        {
            // The panel's own reflow asked for 200: that is what it wants now.
            Assert.Equal(200f, DockLayout.PreferredSize(200f, (Written: 65f, Preferred: 330f)));
        }

        [Fact]
        public void APanelNeverDockedWantsItsOwnSize()
        {
            Assert.Equal(330f, DockLayout.PreferredSize(330f, null));
        }
    }
}
