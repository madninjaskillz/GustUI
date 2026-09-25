using System.Linq;
using GustUI.Elements;

namespace GustUI.Tests
{
    /// <summary>
    /// Window tabs are sized to their captions, capped, and shrink widest-first
    /// when space runs short, never below the floor (ezmuze #341).
    /// </summary>
    public class TabWidthTests
    {
        private const float Gap = 4f;
        private const float Min = 80f;
        private const float Max = 240f;

        [Fact]
        public void TabsThatFitKeepTheirOwnWidths()
        {
            float[] widths = ModalWindowElement.FitTabWidths(new[] { 120f, 150f }, 1000f, Gap, Min, Max);
            Assert.Equal(new[] { 120f, 150f }, widths);
        }

        [Fact]
        public void ALongCaptionIsCappedAndAShortOneFloored()
        {
            float[] widths = ModalWindowElement.FitTabWidths(new[] { 500f, 20f }, 2000f, Gap, Min, Max);
            Assert.Equal(new[] { Max, Min }, widths);
        }

        [Fact]
        public void WhenShortTheWidestGiveWayFirst()
        {
            float[] widths = ModalWindowElement.FitTabWidths(new[] { 100f, 240f, 240f }, 404f, Gap, Min, Max);

            Assert.Equal(100f, widths[0]);
            Assert.Equal(widths[1], widths[2]);
            Assert.Equal(404f, widths.Sum() + (Gap * 2), 3);
        }

        [Fact]
        public void NoTabGoesBelowTheFloor()
        {
            float[] widths = ModalWindowElement.FitTabWidths(new[] { 200f, 200f, 200f }, 100f, Gap, Min, Max);
            Assert.All(widths, w => Assert.Equal(Min, w));
        }

        [Fact]
        public void AWiderCaptionNeverGetsANarrowerTab()
        {
            float[] widths = ModalWindowElement.FitTabWidths(new[] { 90f, 130f, 220f, 180f }, 500f, Gap, Min, Max);
            Assert.True(widths[0] <= widths[1]);
            Assert.True(widths[1] <= widths[3]);
            Assert.True(widths[3] <= widths[2]);
        }
    }
}
