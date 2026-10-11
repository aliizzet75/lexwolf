using System;
using System.Text;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// Task #238: automatisierte Tests für die Client-Upload-Logik (Dateityp-
    /// und Größenvalidierung des Screenshot-Anhangs). Die reine Validierung ist
    /// in ScreenshotValidator ausgelagert, damit sie plattformunabhängig läuft.
    /// </summary>
    public class ScreenshotValidatorTests
    {
        private static byte[] PngBytes =>
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        [Fact]
        public void GueltigesPng_WirdAkzeptiert()
        {
            var (ok, mime, error) = ScreenshotValidator.Validate(PngBytes, "bug.png");
            Assert.True(ok);
            Assert.Equal("image/png", mime);
            Assert.Null(error);
        }

        [Theory]
        [InlineData("bug.jpg", "image/jpeg")]
        [InlineData("bug.jpeg", "image/jpeg")]
        [InlineData("bug.gif", "image/gif")]
        [InlineData("bug.webp", "image/webp")]
        [InlineData("bug.PNG", "image/png")]
        public void ErlaubteEndungen_WerdenAkzeptiert(string fileName, string expectedMime)
        {
            var bytes = Encoding.UTF8.GetBytes("dummy image bytes");
            var (ok, mime, error) = ScreenshotValidator.Validate(bytes, fileName);
            Assert.True(ok);
            Assert.Equal(expectedMime, mime);
            Assert.Null(error);
        }

        [Theory]
        [InlineData("bad.exe")]
        [InlineData("bad.pdf")]
        [InlineData("bad.txt")]
        [InlineData("bad")]
        public void UnzulaessigerDateityp_WirdAbgelehnt(string fileName)
        {
            var bytes = Encoding.UTF8.GetBytes("not an image");
            var (ok, mime, error) = ScreenshotValidator.Validate(bytes, fileName);
            Assert.False(ok);
            Assert.Null(mime);
            Assert.Contains("PNG, JPG, GIF oder WebP", error);
        }

        [Fact]
        public void LeeresBild_WirdAbgelehnt()
        {
            var (ok, mime, error) = ScreenshotValidator.Validate(Array.Empty<byte>(), "empty.png");
            Assert.False(ok);
            Assert.Null(mime);
            Assert.Equal("Das Bild ist leer.", error);
        }

        [Fact]
        public void NullBild_WirdAbgelehnt()
        {
            var (ok, mime, error) = ScreenshotValidator.Validate(null, "null.png");
            Assert.False(ok);
            Assert.Null(mime);
            Assert.Equal("Das Bild ist leer.", error);
        }

        [Fact]
        public void UebergrossesBild_WirdAbgelehnt()
        {
            var bytes = new byte[11 * 1024 * 1024]; // 11 MB
            var (ok, mime, error) = ScreenshotValidator.Validate(bytes, "huge.png", maxSize: 10 * 1024 * 1024);
            Assert.False(ok);
            Assert.Null(mime);
            Assert.Contains("10 MB", error);
        }

        [Fact]
        public void GrenzwertigesBild_WirdAkzeptiert()
        {
            var bytes = new byte[10 * 1024 * 1024]; // exakt 10 MB
            var (ok, mime, error) = ScreenshotValidator.Validate(bytes, "max.png", maxSize: 10 * 1024 * 1024);
            Assert.True(ok);
            Assert.Equal("image/png", mime);
            Assert.Null(error);
        }

        [Fact]
        public void DefaultLimit_Ist10MB()
        {
            Assert.Equal(10L * 1024 * 1024, ScreenshotValidator.DefaultMaxSize);
        }
    }
}
