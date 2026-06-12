using System;
using System.IO;

namespace Anonymisierer.Services
{
    // IFileReader-Schnittstelle für alle Dateitypen
    public interface IFileReader
    {
        string ReadFile(string filePath);
        bool CanHandle(string extension);
    }

    // DokumentReader für .docx mit DocumentFormat.OpenXml
    public class DocxReader : IFileReader
    {
        public string ReadFile(string filePath)
        {
            if (!File.Exists(filePath))
                return string.Empty;

            try
            {
                using (var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(filePath, false))
                {
                    var body = package.MainDocumentPart?.Document?.Body;
                    if (body == null)
                        return string.Empty;

                    var text = new System.Text.StringBuilder();
                    foreach (var paragraph in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
                    {
                        foreach (var run in paragraph.Descendants<DocumentFormat.OpenXml.Wordprocessing.Run>())
                        {
                            foreach (var textElement in run.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>())
                            {
                                text.Append(textElement.Text);
                            }
                        }
                        text.AppendLine();
                    }

                    return text.ToString();
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        public bool CanHandle(string extension) =>
            extension?.Equals(".docx", StringComparison.OrdinalIgnoreCase) == true;
    }

    // PdfReader für .pdf mit iText7
    public class PdfReader : IFileReader
    {
        public string ReadFile(string filePath)
        {
            if (!File.Exists(filePath))
                return string.Empty;

            try
            {
                var sb = new System.Text.StringBuilder();
                using var reader = new iText.Kernel.Pdf.PdfReader(filePath);
                using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);
                for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                {
                    sb.Append(iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(i)));
                    sb.AppendLine();
                }
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        public bool CanHandle(string extension) =>
            extension?.Equals(".pdf", StringComparison.OrdinalIgnoreCase) == true;
    }

    // TextReader für .txt
    public class TxtReader : IFileReader
    {
        public string ReadFile(string filePath)
        {
            if (!File.Exists(filePath))
                return string.Empty;

            try
            {
                return File.ReadAllText(filePath);
            }
            catch
            {
                return string.Empty;
            }
        }

        public bool CanHandle(string extension) =>
            extension?.Equals(".txt", StringComparison.OrdinalIgnoreCase) == true;
    }

    // EmlReader für .eml
    public class EmlReader : IFileReader
    {
        public string ReadFile(string filePath)
        {
            if (!File.Exists(filePath))
                return string.Empty;

            try
            {
                return File.ReadAllText(filePath);
            }
            catch
            {
                return string.Empty;
            }
        }

        public bool CanHandle(string extension) =>
            extension?.Equals(".eml", StringComparison.OrdinalIgnoreCase) == true;
    }

    // UnifiedFileReader mit Factory-Methode für alle Dateitypen
    public static class UnifiedFileReader
    {
        private static readonly List<IFileReader> _readers = new()
        {
            new DocxReader(),
            new PdfReader(),
            new TxtReader(),
            new EmlReader()
        };

        // Liest eine Datei basierend auf der Extension
        public static string ReadFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return string.Empty;

            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            foreach (var reader in _readers)
            {
                if (reader.CanHandle(ext))
                {
                    return reader.ReadFile(filePath);
                }
            }

            return "Dateityp nicht unterstützt";
        }

        // Liest eine Datei und gibt den Typ zurück
        public static (string content, string type) ReadFileWithType(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return (ReadFile(filePath), ext.Substring(1).ToUpperInvariant());
        }
    }
}
