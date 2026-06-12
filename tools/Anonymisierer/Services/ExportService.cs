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
            // PDF kann nicht als gültiges PDF zurückgeschrieben werden
            if (Path.GetExtension(sourcePath).Equals(".pdf", System.StringComparison.OrdinalIgnoreCase))
                relPath = Path.ChangeExtension(relPath, ".txt");
            return Path.Combine(outputDir, relPath);
        }

        public static void WriteFile(string sourcePath, string destPath, string anonymizedText, List<Anonymisierer.Entity> entities)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            if (Path.GetExtension(sourcePath).Equals(".docx", System.StringComparison.OrdinalIgnoreCase)
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
