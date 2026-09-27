using GustUI.Elements;
using GustUI.Traits;
using GustUI.TraitValues;

namespace GustUI.Tests
{
    /// <summary>
    /// ezmuze #401: while the sequencer filled the free space, its maximise
    /// flagged it maximised with nothing to show, and restore started a 70%
    /// transition the fill layout overwrote every frame. That transition then
    /// finished on the next title-bar drag, shrinking the window under the
    /// pointer. Filling now counts as maximised: the glyph offers restore, and
    /// the press floats the window at 70% of the space.
    /// </summary>
    public class FillingMaximiseTests
    {
        [Fact]
        public void AFillingWindowShowsTheRestoreGlyph()
        {
            Assert.True(ModalWindowElement.ShowsRestoreGlyph(maximised: false, filling: true));
            Assert.True(ModalWindowElement.ShowsRestoreGlyph(maximised: true, filling: false));
            Assert.True(ModalWindowElement.ShowsRestoreGlyph(maximised: true, filling: true));
            Assert.False(ModalWindowElement.ShowsRestoreGlyph(maximised: false, filling: false));
        }

        [Fact]
        public void PressingItOnAFillingWindowRestoresFromTheFill()
        {
            Assert.Equal(ModalWindowElement.MaximisePress.RestoreFromFilling,
                ModalWindowElement.PressOfMaximise(DockSide.None, maximised: false, filling: true));

            // The sequencer rebuilt maximised is both; it is still a restore.
            Assert.Equal(ModalWindowElement.MaximisePress.RestoreFromFilling,
                ModalWindowElement.PressOfMaximise(DockSide.None, maximised: true, filling: true));
        }

        [Fact]
        public void AFloatingWindowMaximisesAndRestoresAsBefore()
        {
            // Including the ex-filler once it floats: an ordinary maximise, so
            // restore comes back to where it floated.
            Assert.Equal(ModalWindowElement.MaximisePress.Maximise,
                ModalWindowElement.PressOfMaximise(DockSide.None, maximised: false, filling: false));
            Assert.Equal(ModalWindowElement.MaximisePress.Restore,
                ModalWindowElement.PressOfMaximise(DockSide.None, maximised: true, filling: false));
        }

        [Fact]
        public void ADockedWindowStillIgnoresThePress()
        {
            foreach (DockSide side in new[] { DockSide.Left, DockSide.Right, DockSide.Top, DockSide.Bottom })
            {
                Assert.Equal(ModalWindowElement.MaximisePress.Nothing,
                    ModalWindowElement.PressOfMaximise(side, maximised: false, filling: true));
                Assert.Equal(ModalWindowElement.MaximisePress.Nothing,
                    ModalWindowElement.PressOfMaximise(side, maximised: true, filling: false));
            }
        }

        [Fact]
        public void APendingRestoreCanBeDropped()
        {
            // What the fill layout does every frame: a transition that cannot
            // arrive while the window fills is dropped, not left to finish on
            // the next drag.
            var element = new Element();
            element.AddTrait<PositionTrait>().Set(new TVVector(0, 0));
            element.AddTrait<SizeTrait>().Set(new TVVector(1052, 260));
            element.isFullScreen = true;

            element.ToggleFullScreen();
            Assert.True(element.SizeTransitionPending);

            element.CancelSizeTransition();
            Assert.False(element.SizeTransitionPending);
        }
    }
}
