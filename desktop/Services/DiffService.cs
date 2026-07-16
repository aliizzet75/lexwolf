using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LexWolf.Services
{
    // Einfacher, stabiler Diff-Service ohne externe Diff-API-Abhängigkeit
    public static class DiffService
    {
        public static DiffViewModel BuildDiffViewModel(string original, string anonymized)
        {
            var originalLines = (original ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            var anonymizedLines = (anonymized ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            var lines = new List<DiffLine>();

            var max = Math.Max(originalLines.Length, anonymizedLines.Length);
            for (var i = 0; i < max; i++)
            {
                var oldLine = i < originalLines.Length ? originalLines[i] : string.Empty;
                var newLine = i < anonymizedLines.Length ? anonymizedLines[i] : string.Empty;
                var isChanged = !string.Equals(oldLine, newLine, StringComparison.Ordinal);

                lines.Add(new DiffLine
                {
                    Type = isChanged ? DiffChangeType.Replace : DiffChangeType.Unchanged,
                    Text = newLine,
                    OldText = oldLine,
                    NewText = newLine,
                    IsChanged = isChanged
                });
            }

            return new DiffViewModel
            {
                OriginalText = original ?? string.Empty,
                AnonymizedText = anonymized ?? string.Empty,
                Lines = lines,
                TotalLines = lines.Count,
                ChangedLines = lines.Count(l => l.IsChanged)
            };
        }

        public static string BuildColoredDiff(string original, string anonymized)
        {
            var result = new StringBuilder();
            var model = BuildDiffViewModel(original, anonymized);

            foreach (var line in model.Lines)
            {
                var prefix = line.IsChanged ? "[GEÄNDERT]" : "[GRAU]";
                result.AppendLine($"{prefix}{line.Text}");
            }

            return result.ToString();
        }
    }

    public class DiffViewModel
    {
        public string OriginalText { get; set; } = string.Empty;
        public string AnonymizedText { get; set; } = string.Empty;
        public List<DiffLine> Lines { get; set; } = new();
        public int TotalLines { get; set; }
        public int ChangedLines { get; set; }
    }

    public class DiffLine
    {
        public DiffChangeType Type { get; set; }
        public string Text { get; set; } = string.Empty;
        public string OldText { get; set; } = string.Empty;
        public string NewText { get; set; } = string.Empty;
        public bool IsChanged { get; set; }
    }

    public enum DiffChangeType
    {
        Unchanged,
        Replace
    }
}
