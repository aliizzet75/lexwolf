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

    // RtfReader für .rtf (via WinForms RichTextBox)
    public class RtfReader : IFileReader
    {
        public string ReadFile(string filePath)
        {
            if (!File.Exists(filePath)) return string.Empty;
            try
            {
                string result = string.Empty;
                var thread = new System.Threading.Thread(() =>
                {
                    using var rtb = new System.Windows.Forms.RichTextBox();
                    rtb.LoadFile(filePath, System.Windows.Forms.RichTextBoxStreamType.RichText);
                    result = rtb.Text;
                });
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.Start();
                thread.Join();
                return result;
            }
            catch
            {
                return string.Empty;
            }
        }

        public bool CanHandle(string extension) =>
            extension?.Equals(".rtf", StringComparison.OrdinalIgnoreCase) == true;
    }

    // OdtReader für .odt (OpenDocument Text via ZIP + XML)
    public class OdtReader : IFileReader
    {
        public string ReadFile(string filePath)
        {
            if (!File.Exists(filePath)) return string.Empty;
            try
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(filePath);
                var entry = zip.GetEntry("content.xml");
                if (entry == null) return string.Empty;
                using var stream = entry.Open();
                var doc = System.Xml.Linq.XDocument.Load(stream);
                var sb = new System.Text.StringBuilder();
                // Alle text:p, text:h und text:span Knoten durchlaufen
                foreach (var el in doc.Descendants())
                {
                    var local = el.Name.LocalName;
                    if (local == "p" || local == "h" || local == "span")
                    {
                        var text = el.Value?.Trim();
                        if (!string.IsNullOrEmpty(text))
                        {
                            sb.AppendLine(text);
                        }
                    }
                }
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        public bool CanHandle(string extension) =>
            extension?.Equals(".odt", StringComparison.OrdinalIgnoreCase) == true;
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
            new EmlReader(),
            new RtfReader(),
            new OdtReader()
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
