using System;
using System.Text;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.Model;

namespace LexWolf.Services
{
    // Diff-Service mit DiffPlex für Text-Vergleiche
    public static class DiffService
    {
        private static readonly Differ _differ = new Differ();

        // Berechnet den Diff zwischen zwei Texten
        public static DiffResult DiffText(string original, string anonymized)
        {
            if (string.IsNullOrEmpty(original))
                original = string.Empty;
            if (string.IsNullOrEmpty(anonymized))
                anonymized = string.Empty;

            var diff = _differ.Diff(original, anonymized);
            return diff;
        }

        // Erstellt einen Inline-Diff mit Farben (simuliert)
        public static string BuildColoredDiff(string original, string anonymized)
        {
            var diffResult = DiffText(original, anonymized);
            
            var result = new StringBuilder();
            var lines = diffResult.Lines;

            foreach (var line in lines)
            {
                switch (line.Type)
                {
                    case ChangeType.Inserted:
                        // Grün für anonymisiert (hinzugefügt)
                        result.AppendLine($"[GRÜN]{line.Text}");
                        break;
                    case ChangeType.Deleted:
                        // Rot für original (gelöscht)
                        result.AppendLine($"[ROT]{line.Text}");
                        break;
                    case ChangeType.Replace:
                        // Rot für original, grün für anonymisiert
                        result.AppendLine($"[ROT]{line.OldText}");
                        result.AppendLine($"[GRÜN]{line.NewText}");
                        break;
                    case ChangeType.Unchanged:
                        // Grau für unverändert
                        result.AppendLine($"[GRAU]{line.Text}");
                        break;
                }
            }

            return result.ToString();
        }

        // Erstellt ein einfaches Diff-Modell für UI-Anzeige
        public static DiffViewModel BuildDiffViewModel(string original, string anonymized)
        {
            var diffResult = DiffText(original, anonymized);
            
            var lines = new System.Collections.Generic.List<DiffLine>();
            foreach (var line in diffResult.Lines)
            {
                lines.Add(new DiffLine
                {
                    Type = line.Type,
                    Text = line.Text,
                    IsChanged = line.Type != ChangeType.Unchanged
                });
            }

            return new DiffViewModel
            {
                OriginalText = original,
                AnonymizedText = anonymized,
                Lines = lines,
                TotalLines = lines.Count,
                ChangedLines = lines.Count(l => l.IsChanged)
            };
        }
    }

    // Diff-View-Model für Binding
    public class DiffViewModel
    {
        public string OriginalText { get; set; } = string.Empty;
        public string AnonymizedText { get; set; } = string.Empty;
        public System.Collections.Generic.List<DiffLine> Lines { get; set; } = new();
        public int TotalLines { get; set; }
        public int ChangedLines { get; set; }
    }

    // Diff-Line für UI-Darstellung
    public class DiffLine
    {
        public ChangeType Type { get; set; }
        public string Text { get; set; } = string.Empty;
        public string OldText { get; set; } = string.Empty;
        public string NewText { get; set; } = string.Empty;
        public bool IsChanged { get; set; }
    }
}
