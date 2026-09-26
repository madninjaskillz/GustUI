using GustUI.Elements;

namespace GustUI.Tests
{
    /// <summary>
    /// A menu-bar dropdown is as wide as its widest row, between a 300 px
    /// floor and a 640 px cap, and no wider than the room left on screen
    /// (ezmuze #351).
    /// </summary>
    public class MenuWidthTests
    {
        private const int Floor = FruitPopupMenu.MinFitWidth;
        private const int Cap = FruitPopupMenu.MaxFitWidth;

        [Fact]
        public void RowsThatFitKeepTheOldWidth()
        {
            Assert.Equal(300, Floor);
            Assert.Equal(Floor, FruitPopupMenu.FitWidth(new[] { 120f, 250f, 0f }, Floor, Cap, 2000f));
        }

        [Fact]
        public void ALongRowWidensTheMenu()
        {
            Assert.Equal(452, FruitPopupMenu.FitWidth(new[] { 200f, 451.2f }, Floor, Cap, 2000f));
        }

        [Fact]
        public void TheCapHolds()
        {
            Assert.Equal(Cap, FruitPopupMenu.FitWidth(new[] { 1400f }, Floor, Cap, 5000f));
        }

        [Fact]
        public void TheRoomLeftOnScreenLimitsIt()
        {
            Assert.Equal(400, FruitPopupMenu.FitWidth(new[] { 600f }, Floor, Cap, 400f));
        }

        [Fact]
        public void TooLittleRoomNeverTakesItBelowTheFloor()
        {
            Assert.Equal(Floor, FruitPopupMenu.FitWidth(new[] { 600f }, Floor, Cap, 120f));
            Assert.Equal(Floor, FruitPopupMenu.FitWidth(new float[0], Floor, Cap, 120f));
        }
    }
}
