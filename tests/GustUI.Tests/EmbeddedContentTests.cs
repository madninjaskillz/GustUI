using System.IO;

namespace GustUI.Tests
{
    /// <summary>
    /// GustUI's default fonts and theme images are assembly resources (they
    /// were base64 string literals until 2026-10-08, ~13 MB resident for the
    /// life of the process). Every name the app and theme ask for has to be
    /// there, whole, under the name it was asked for.
    /// </summary>
    public class EmbeddedContentTests
    {
        public static TheoryData<string, int> Files => new()
        {
            { "segmdl2.ttf", 283820 },
            { "SegoeIcons.ttf", 451168 },
            { "segoeuisl.ttf", 869992 },
            { "segoeuib.ttf", 947092 },
            { "seguisym.ttf", 2514056 },
            { "Comfortaa-Bold.ttf", 20676 },
            { "Comfortaa-Light.ttf", 20740 },
            { "tl.png", 1545 }, { "t.png", 421 }, { "tr.png", 1645 },
            { "l.png", 276 }, { "r.png", 275 },
            { "bl.png", 1559 }, { "b.png", 419 }, { "br.png", 1605 },
            { "modifier_alt.png", 580 }, { "modifier_ctrl.png", 602 }, { "modifier_shift.png", 708 },
        };

        [Theory]
        [MemberData(nameof(Files))]
        public void EveryFileIsThereWholeUnderItsOwnName(string name, int length)
        {
            byte[] bytes = new UUContent().ReadAllBytes(name);

            Assert.Equal(length, bytes.Length);
            Assert.Contains(name, UUContent.Names);
            if (name.EndsWith(".ttf"))
            {
                // sfnt version 1.0: a TrueType font, not text or a truncation.
                Assert.Equal(new byte[] { 0, 1, 0, 0 }, bytes[..4]);
            }
            else
            {
                Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, bytes[..4]);
            }
        }

        [Fact]
        public void NothingElseIsEmbeddedUnderThePrefix()
        {
            Assert.Equal(18, UUContent.Names.Count);
        }

        [Fact]
        public void AStreamIsSeekableSoTextureLoadersCanUseIt()
        {
            using Stream stream = new UUContent().OpenStream("b.png");
            Assert.True(stream.CanSeek);
            Assert.Equal(419, stream.Length);
        }

        [Fact]
        public void AMissingNameSaysWhichOne()
        {
            var ex = Assert.Throws<FileNotFoundException>(() => new UUContent().OpenStream("nope.ttf"));
            Assert.Contains("nope.ttf", ex.Message);
        }
    }
}
