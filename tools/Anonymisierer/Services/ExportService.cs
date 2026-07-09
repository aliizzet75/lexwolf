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

        public static string GetAnonFilePath(string sourcePath)
        {
            var dir = Path.GetDirectoryName(sourcePath)!;
            var name = Path.GetFileNameWithoutExtension(sourcePath);
            return Path.Combine(dir, name + "_anon.txt");
        }

        public static void WriteFile(string sourcePath, string destPath, string anonymizedText, List<Anonymisierer.Entity> entities)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            if (Path.GetExtension(sourcePath).Equals(".pdf", System.StringComparison.OrdinalIgnoreCase))
            {
                if (entities.Count > 0)
                    AnonymizePdfInPlace(sourcePath, destPath, entities);
                else
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

            // Task #196: Create _anon.txt in the same folder as the original file
            // This enables LexWolf ingestion by providing plaintext versions
            if (!Path.GetExtension(sourcePath).Equals(".pdf", System.StringComparison.OrdinalIgnoreCase))
            {
                var anonFilePath = GetAnonFilePath(sourcePath);
                File.WriteAllText(anonFilePath, anonymizedText, Encoding.UTF8);
            }
        }

        public static void AnonymizePdfInPlace(string sourcePdfPath, string destPdfPath, List<Anonymisierer.Entity> entities)
        {
            var replacements = entities
                .GroupBy(e => e.Text)
                .ToDictionary(g => g.Key, g => g.First().AnonymizedText);

            static string ToPdfOctal(string s)
            {
                var sb = new System.Text.StringBuilder();
                foreach (char c in s)
                    if (c > 127) sb.Append('\\').Append(Convert.ToString(c, 8).PadLeft(3, '0'));
                    else sb.Append(c);
                return sb.ToString();
            }

            bool ReplaceInStream(iText.Kernel.Pdf.PdfStream stream)
            {
                byte[] bytes;
                try { bytes = stream.GetBytes(decoded: true); }
                catch { return false; }

                var latin  = Encoding.Latin1.GetString(bytes);
                bool changed = false;

                foreach (var kvp in replacements)
                {
                    if (latin.Contains(kvp.Key))
                    {
                        latin   = latin.Replace(kvp.Key, kvp.Value);
                        changed = true;
                    }
                    var pdfOrig = ToPdfOctal(kvp.Key);
                    if (pdfOrig != kvp.Key && latin.Contains(pdfOrig))
                    {
                        latin   = latin.Replace(pdfOrig, kvp.Value);
                        changed = true;
                    }
                }

                if (changed) stream.SetData(Encoding.Latin1.GetBytes(latin));
                return changed;
            }

            void ProcessXObjects(iText.Kernel.Pdf.PdfDictionary? resDict)
            {
                var xobj = resDict?.GetAsDictionary(iText.Kernel.Pdf.PdfName.XObject);
                if (xobj == null) return;
                foreach (var key in xobj.KeySet())
                {
                    if (xobj.Get(key) is not iText.Kernel.Pdf.PdfStream stream) continue;
                    ReplaceInStream(stream);
                    ProcessXObjects(stream.GetAsDictionary(iText.Kernel.Pdf.PdfName.Resources));
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPdfPath)!);
            using var reader = new iText.Kernel.Pdf.PdfReader(sourcePdfPath);
            using var writer = new iText.Kernel.Pdf.PdfWriter(destPdfPath);
            using var doc    = new iText.Kernel.Pdf.PdfDocument(reader, writer);
            for (int p = 1; p <= doc.GetNumberOfPages(); p++)
            {
                var page = doc.GetPage(p);
                for (int s = 0; s < page.GetContentStreamCount(); s++)
                    ReplaceInStream(page.GetContentStream(s));
                ProcessXObjects(page.GetPdfObject().GetAsDictionary(iText.Kernel.Pdf.PdfName.Resources));
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
