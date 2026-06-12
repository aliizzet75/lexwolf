using System;
using System.IO;

namespace LexWolf.Services
{
    // PdfReader für .pdf mit PdfPig (read-only)
    public class PdfReader : IFileReader
    {
        public string ReadFile(string filePath)
        {
            if (!File.Exists(filePath))
                return string.Empty;

            try
            {
                var sb = new System.Text.StringBuilder();
                using (var pdfDocument = PdfPig.PdfDocument.Open(filePath))
                {
                    foreach (var page in pdfDocument.Pages)
                    {
                        sb.Append(page.Text);
                        sb.AppendLine();
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
            extension?.Equals(".pdf", StringComparison.OrdinalIgnoreCase) == true;
    }
}
