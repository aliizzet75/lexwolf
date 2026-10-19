using System;
using System.IO;

namespace LexWolf.Services
{
    // EmlReader für .eml mit StreamReader
    public class EmlReader : IFileReader
    {
        public string ReadFile(string filePath)
        {
            if (!File.Exists(filePath))
                return string.Empty;

            return MailText.LeseEml(filePath);
        }

        public bool CanHandle(string extension) => 
            extension?.Equals(".eml", StringComparison.OrdinalIgnoreCase) == true;
    }
}
