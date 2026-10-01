using System;
using System.Linq;
using GustUI.Elements;
using Microsoft.Xna.Framework;

namespace GustUI.Tests
{
    /// <summary>
    /// A tooltip wraps at a reading measure, never takes more than a share of
    /// the window, cuts what would be taller than half of it, and always sits
    /// wholly on screen. Found 2026-10-02: a store tile's several-hundred
    /// character description drew as ONE line straight across a 4K screen.
    /// </summary>
    public class TooltipLayoutTests
    {
        // A monospaced stand-in for the font: 8 px a character.
        private static readonly Func<string, float> Measure = s => s.Length * 8f;

        private const string Description =
            "A late-80s mini tone-bank keyboard, rebuilt from scratch: 32 tones named and numbered " +
            "after the original's own list (pianos, organs, reeds, strings, synths, bells), every one " +
            "synthesised, with no samples. Twelve were matched by measurement to recordings of the " +
            "original. Pick a tone on the panel or from the presets; Octave, Release and Vibrato shape it.";

        [Fact]
        public void TheBoxIsCappedAtTheReadingMeasureOnABigWindow()
        {
            Assert.Equal(TooltipLayout.MaxBoxWidth, TooltipLayout.MaxBoxWidthFor(2560f));
            Assert.Equal(TooltipLayout.MaxBoxWidth, TooltipLayout.MaxBoxWidthFor(3840f));
        }

        [Fact]
        public void ANarrowWindowCapsItAtFortyPercent()
        {
            Assert.Equal(320f, TooltipLayout.MaxBoxWidthFor(800f));
        }

        [Fact]
        public void ATinyWindowStillGetsAUsableMeasureButNeverMoreThanTheWindow()
        {
            Assert.Equal(TooltipLayout.MinBoxWidth, TooltipLayout.MaxBoxWidthFor(300f));
            Assert.Equal(100f, TooltipLayout.MaxBoxWidthFor(100f));
        }

        [Fact]
        public void AShortTooltipIsLeftExactlyAsWritten()
        {
            var lines = TooltipLayout.Lines("Play / pause the transport (Space)", Measure, 384f, 40);
            Assert.Equal(new[] { "Play / pause the transport (Space)" }, lines);

            // Even its spacing: only a line that has to wrap is re-flowed.
            lines = TooltipLayout.Lines("Gain  (dB)", Measure, 384f, 40);
            Assert.Equal(new[] { "Gain  (dB)" }, lines);
        }

        [Fact]
        public void ALongLineWrapsWithinTheWidthAndLosesNoWords()
        {
            var lines = TooltipLayout.Lines(Description, Measure, 384f, 40);

            Assert.True(lines.Count > 1);
            Assert.All(lines, l => Assert.True(Measure(l) <= 384f, l));
            Assert.Equal(
                Description.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                lines.SelectMany(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        }

        [Fact]
        public void ExistingLineBreaksAreKept()
        {
            var lines = TooltipLayout.Lines("Reverb - a hall\r\n\nDrag to change", Measure, 384f, 40);
            Assert.Equal(new[] { "Reverb - a hall", "", "Drag to change" }, lines);
        }

        [Fact]
        public void AWrappedParagraphStillBreaksWhereTheTextDid()
        {
            var lines = TooltipLayout.Lines("Name\n" + Description, Measure, 384f, 40);
            Assert.Equal("Name", lines[0]);
            Assert.StartsWith("A late-80s", lines[1]);
        }

        [Fact]
        public void AWordWiderThanTheBoxIsBrokenRatherThanOverflowing()
        {
            string url = "https://example.com/" + new string('x', 80);
            var lines = TooltipLayout.Lines(url, Measure, 384f, 40);
            Assert.True(lines.Count > 1);
            Assert.All(lines, l => Assert.True(Measure(l) <= 384f, l));
            Assert.Equal(url, string.Concat(lines));
        }

        [Fact]
        public void TooManyLinesAreCutWithAnEllipsisThatFits()
        {
            var lines = TooltipLayout.Lines(Description, Measure, 160f, 3);
            Assert.Equal(3, lines.Count);
            Assert.EndsWith("...", lines[2]);
            Assert.True(Measure(lines[2]) <= 160f);
        }

        [Fact]
        public void TheCutIsVisibleEvenWhenTheLastKeptLineIsShort()
        {
            var lines = TooltipLayout.Lines("one\ntwo\nthree", Measure, 384f, 2);
            Assert.Equal(new[] { "one", "two..." }, lines);
        }

        [Fact]
        public void HalfTheWindowsHeightDecidesTheLineCap()
        {
            // 1000 tall: 500 - 2*5 padding = 490, at 20 a line = 24 lines.
            Assert.Equal(24, TooltipLayout.MaxLinesFor(1000f, 20f, 5f));
            Assert.Equal(1, TooltipLayout.MaxLinesFor(10f, 20f, 5f));
        }

        [Fact]
        public void ItSitsBelowRightOfThePointerWhenItFits()
        {
            Vector2 at = TooltipLayout.Place(new Vector2(100, 100), new Vector2(200, 50), new Vector2(1920, 1080));
            Assert.Equal(new Vector2(114, 120), at);
        }

        [Fact]
        public void AtTheRightEdgeItFlipsToThePointersLeft()
        {
            Vector2 at = TooltipLayout.Place(new Vector2(1800, 100), new Vector2(300, 50), new Vector2(1920, 1080));
            Assert.Equal(1800 - TooltipLayout.FlipGap - 300, at.X);
            Assert.Equal(120, at.Y);
        }

        [Fact]
        public void AtTheBottomEdgeItFlipsAboveThePointer()
        {
            Vector2 at = TooltipLayout.Place(new Vector2(100, 1060), new Vector2(200, 50), new Vector2(1920, 1080));
            Assert.Equal(114, at.X);
            Assert.Equal(1060 - TooltipLayout.FlipGap - 50, at.Y);
        }

        [Fact]
        public void WhenNeitherSideFitsItIsClampedOnScreen()
        {
            // Pointer near the left edge, box wider than the room either side.
            Vector2 window = new Vector2(500, 400);
            Vector2 at = TooltipLayout.Place(new Vector2(150, 300), new Vector2(400, 300), window);
            Assert.True(at.X >= 0 && at.X + 400 <= window.X);
            Assert.True(at.Y >= 0 && at.Y + 300 <= window.Y);
        }

        [Fact]
        public void ABoxBiggerThanTheWindowStartsAtItsTopLeft()
        {
            Assert.Equal(Vector2.Zero,
                TooltipLayout.Place(new Vector2(50, 50), new Vector2(900, 900), new Vector2(500, 400)));
        }
    }
}
