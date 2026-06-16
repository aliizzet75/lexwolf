using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Anonymisierer.Services
{
    public static class ExportService
    {
        public static string GetOutputDirectory(string scanRootPath)
        {
            return scanRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + "_anonymisiert";
        }

        public static string GetDestPath(string sourcePath, string outputDir, string scanRootPath)
        {
            var relPath = Path.GetRelativePath(scanRootPath, sourcePath);
            return Path.Combine(outputDir, relPath);
        }

        public static void WriteFile(string sourcePath, string destPath, string anonymizedText, List<Anonymisierer.Entity> entities)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            if (Path.GetExtension(sourcePath).Equals(".pdf", System.StringComparison.OrdinalIgnoreCase))
            {
                WritePdf(destPath, anonymizedText);
            }
            else if (Path.GetExtension(sourcePath).Equals(".docx", System.StringComparison.OrdinalIgnoreCase)
                && entities.Count > 0)
            {
                var replacements = entities
                    .GroupBy(e => e.Text)
                    .ToDictionary(g => g.Key, g => g.First().AnonymizedText);
                WriteDocx(sourcePath, destPath, replacements);
            }
            else
            {
                File.WriteAllText(destPath, anonymizedText, Encoding.UTF8);
            }
        }

        public static void WritePdf(string destPath, string text)
        {
            using var writer = new iText.Kernel.Pdf.PdfWriter(destPath);
            var pdfDoc = new iText.Kernel.Pdf.PdfDocument(writer);
            var doc    = new iText.Layout.Document(pdfDoc, iText.Kernel.Geom.PageSize.A4);
            var font   = iText.Kernel.Font.PdfFontFactory.CreateFont(
                iText.IO.Font.Constants.StandardFonts.HELVETICA);
            doc.SetFont(font).SetFontSize(10);
            foreach (var rawLine in text.Split('\n'))
                doc.Add(new iText.Layout.Element.Paragraph(rawLine.TrimEnd('\r')).SetMarginBottom(0));
            doc.Close(); // schließt pdfDoc mit — kein doppelter Dispose
        }

        private static void WriteDocx(string sourcePath, string destPath, Dictionary<string, string> replacements)
        {
            File.Copy(sourcePath, destPath, overwrite: true);
            using var doc = WordprocessingDocument.Open(destPath, isEditable: true);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null) return;

            foreach (var textEl in body.Descendants<Text>())
            {
                foreach (var (original, alias) in replacements)
                {
                    if (textEl.Text.Contains(original))
                        textEl.Text = textEl.Text.Replace(original, alias);
                }
            }
            doc.MainDocumentPart!.Document.Save();
        }
    }
}
