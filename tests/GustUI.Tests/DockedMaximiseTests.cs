using GustUI.Elements;

namespace GustUI.Tests
{
    /// <summary>
    /// A docked window does not offer maximise (ezmuze #366). The dock owns a
    /// docked window's geometry, so the glyph could only ever do nothing there;
    /// it hides, as the pin does, on the title bar and on the active tab alike.
    /// </summary>
    public class DockedMaximiseTests
    {
        private static readonly DockSide[] Docks = { DockSide.Left, DockSide.Right, DockSide.Top, DockSide.Bottom };

        [Fact]
        public void AFloatingWindowOffersMaximise()
        {
            Assert.True(ModalWindowElement.OffersMaximise(DockSide.None));
        }

        [Fact]
        public void ADockedWindowOffersNoMaximiseOnAnySide()
        {
            foreach (DockSide side in Docks)
            {
                Assert.False(ModalWindowElement.OffersMaximise(side));
            }
        }

        [Fact]
        public void OnlyTheActiveTabOfAFloatingWindowDrawsMaximise()
        {
            Assert.True(ModalWindowElement.TabShowsMaximise(isActive: true, DockSide.None));
            Assert.False(ModalWindowElement.TabShowsMaximise(isActive: false, DockSide.None));

            foreach (DockSide side in Docks)
            {
                Assert.False(ModalWindowElement.TabShowsMaximise(isActive: true, side));
                Assert.False(ModalWindowElement.TabShowsMaximise(isActive: false, side));
            }
        }

        [Fact]
        public void AHiddenMaximiseGivesItsSquareBackToTheTitleBar()
        {
            float bar = ModalTitleBarElement.BarHeight;
            Assert.Equal(2 * bar, ModalTitleBarElement.CloseAndSizeWidthFor(closable: true, maximiseShowing: true));
            Assert.Equal(bar, ModalTitleBarElement.CloseAndSizeWidthFor(closable: true, maximiseShowing: false));

            // The sequencer has no close: docked, nothing is left at the right.
            Assert.Equal(bar, ModalTitleBarElement.CloseAndSizeWidthFor(closable: false, maximiseShowing: true));
            Assert.Equal(0f, ModalTitleBarElement.CloseAndSizeWidthFor(closable: false, maximiseShowing: false));
        }
    }
}
