using System;
using System.Collections.Generic;
using System.IO;

namespace LexWolf.Services
{
    // TextReader für .txt mit StreamReader
    public class PlainTextReader : IFileReader
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

    // UnifiedFileReader mit Factory-Methode für alle Dateitypen
    public static class UnifiedFileReader
    {
        private static readonly List<IFileReader> _readers = new()
        {
            new DocxReader(),
            new PdfReader(),
            new PlainTextReader(),
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
