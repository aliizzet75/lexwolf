using System.Text;

namespace LexWolf.Services
{
    /// <summary>
    /// Reiner PdfPig-Textextraktor ohne Windows/WinRT-Abhängigkeit — bewusst von
    /// DokumentScanner getrennt, damit er plattformunabhängig (auch unter Linux/CI)
    /// gegen echte PDF-Dateien getestet werden kann. Wirft bei Problemen, statt
    /// leise leeren Text zurückzugeben — Fehlerbehandlung/Fallback (OCR) liegt beim
    /// Aufrufer (DokumentScanner.ReadPdf).
    /// </summary>
    public static class PdfTextExtractor
    {
        public static string ExtractText(string path)
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(path);
            var sb = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                sb.AppendLine(page.Text);
            }
            return sb.ToString();
        }
    }
}
