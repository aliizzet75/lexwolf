using System;
using System.IO;

namespace LexWolf.Services
{
    // PdfReader für .pdf mit sicherer Fallback-Implementierung
    public class PdfReader : IFileReader
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
            extension?.Equals(".pdf", StringComparison.OrdinalIgnoreCase) == true;
    }
}
