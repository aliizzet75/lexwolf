using System;
using System.IO;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// Regressionstest gegen den Byte-Scan-Bug: der alte PDF-Reader durchsuchte
    /// rohe Bytes nach unkomprimierten "(...)"-Textmustern und fand bei praktisch
    /// allen modernen (komprimierten) PDFs keinen Text. Diese Tests laufen gegen
    /// echte, im Repo eingecheckte Mandanten-Testdokumente (desktop/test/) und
    /// verifizieren, dass tatsächlich lesbarer Inhalt herauskommt.
    /// </summary>
    public class PdfTextExtractorTests
    {
        private static string TestDir =>
            Path.Combine(AppContext.BaseDirectory);

        [Fact]
        public void Liest_Unterhaltsberechnung_MitErwartetenBetraegen()
        {
            var path = Path.Combine(TestDir, "Unterhaltsberechnung Özgür.pdf");
            Assert.True(File.Exists(path), $"Testdatei fehlt: {path}");

            var text = PdfTextExtractor.ExtractText(path);

            Assert.False(string.IsNullOrWhiteSpace(text), "Extrahierter Text ist leer.");
            Assert.Contains("Unterhaltsberechnung", text);
            // Konkreter Wert aus dem Dokument — stellt sicher, dass nicht nur
            // irgendein Text, sondern der TATSÄCHLICHE Inhalt extrahiert wird.
            Assert.Contains("6.091,00", text);
        }

        [Fact]
        public void Liest_GerichtsBeschluss_MitAktenzeichenUndInhalt()
        {
            var path = Path.Combine(TestDir,
                "BES_10_03_2025_Begl_Abschr__u_12_03_202_a_00_2fb64dc2797c9b98e0636fac7f0a44b8.pdf");
            Assert.True(File.Exists(path), $"Testdatei fehlt: {path}");

            var text = PdfTextExtractor.ExtractText(path);

            Assert.False(string.IsNullOrWhiteSpace(text), "Extrahierter Text ist leer.");
            Assert.Contains("2 F 158/24", text);           // Aktenzeichen
            Assert.Contains("Familiensache", text);
            Assert.Contains("Versorgungsausgleich", text);
        }
    }
}
