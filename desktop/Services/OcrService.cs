using System;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace LexWolf.Services
{
    /// <summary>
    /// OCR-Fallback für gescannte/abfotografierte PDFs ohne Textebene, über die in
    /// Windows eingebauten WinRT-APIs (kein Tesseract o.ä. nötig — keine zusätzlichen
    /// Binärdateien/Sprachdaten zu bündeln). Setzt voraus, dass ein OCR-Sprachpaket
    /// für die Zielsprache auf dem System installiert ist (unter deutschem Windows
    /// i.d.R. der Fall, aber nicht garantiert).
    /// </summary>
    public static class OcrService
    {
        private const int MaxPages = 20; // Sicherheitsdeckel gegen sehr lange Scans

        public static async Task<string?> TryReadPdfViaOcrAsync(string path)
        {
            var engine = OcrEngine.TryCreateFromLanguage(new Language("de"))
                         ?? OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null)
                return null; // kein passendes OCR-Sprachpaket auf diesem System installiert

            var file = await StorageFile.GetFileFromPathAsync(path);
            var pdf = await PdfDocument.LoadFromFileAsync(file);

            var sb = new StringBuilder();
            var pageCount = Math.Min(pdf.PageCount, MaxPages);
            for (uint i = 0; i < pageCount; i++)
            {
                using var page = pdf.GetPage(i);
                using var stream = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream);
                stream.Seek(0);

                var decoder = await BitmapDecoder.CreateAsync(stream);
                using var bitmap = await decoder.GetSoftwareBitmapAsync();
                using var converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

                var result = await engine.RecognizeAsync(converted);
                sb.AppendLine(result.Text);
            }
            return sb.ToString();
        }
    }
}
