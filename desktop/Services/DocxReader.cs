using System;
using System.IO;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace LexWolf.Services
{
    // DocxReader für .docx mit DocumentFormat.OpenXml
    public class DocxReader : IFileReader
    {
        public string ReadFile(string filePath)
        {
            if (!File.Exists(filePath))
                return string.Empty;

            try
            {
                using (var package = WordprocessingDocument.Open(filePath, false))
                {
                    var body = package.MainDocumentPart?.Document?.Body;
                    if (body == null)
                        return string.Empty;

                    var text = new System.Text.StringBuilder();
                    foreach (var paragraph in body.Descendants<Paragraph>())
                    {
                        foreach (var run in paragraph.Descendants<Run>())
                        {
                            foreach (var textElement in run.Descendants<Text>())
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
}
