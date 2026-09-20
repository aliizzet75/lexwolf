using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using LexWolf.Services;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace LexWolf.Controls
{
    public partial class DiffView : WpfUserControl
    {
        private static readonly System.Text.RegularExpressions.Regex ParagraphPattern =
            new(@"§\s?\d+[a-z]?", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex SentencePattern =
            new(@"[.!?]+", System.Text.RegularExpressions.RegexOptions.Compiled);
        private string _originalText = string.Empty;
        private string _anonymizedText = string.Empty;
        private readonly FlowDocument _document = new();
        private readonly FeedbackSender _feedbackSender = new("http://localhost:8000");

        public DiffView()
        {
            InitializeComponent();
        }

        public string OriginalText
        {
            get => _originalText;
            set { _originalText = value; UpdateDiff(); }
        }

        public string AnonymizedText
        {
            get => _anonymizedText;
            set { _anonymizedText = value; UpdateDiff(); }
        }

        private void OnComputeDiff(object sender, RoutedEventArgs e)
        {
            UpdateDiff();
        }

        private void UpdateDiff()
        {
            _document.Blocks.Clear();
            var para = new Paragraph();
            _document.Blocks.Add(para);

            var model = DiffService.BuildDiffViewModel(_originalText, _anonymizedText);
            foreach (var line in model.Lines)
            {
                var run = new Run(line.Text);
                run.Foreground = line.IsChanged ? Brushes.Red : Brushes.Gray;
                para.Inlines.Add(run);
                para.Inlines.Add(new LineBreak());
            }

            // T#89: Sende nur aggregierte Stil-Metriken fuer die geaenderten Zeilen.
            // Es werden keine Original-/Anonymized-Texte, keine Namen und keine
            // Paragraphen uebertragen - nur Satzlaengen, Wortklassen und
            // Korrektur-Kategorien.
            _ = SendDiffMetricsAsync(model);
        }

        private async Task SendDiffMetricsAsync(DiffViewModel model)
        {
            try
            {
                // T#93: Opt-in pruefen — keine Daten senden, wenn Feedback deaktiviert ist.
                var settings = AppSettings.Load();
                if (!settings.FeedbackOptIn)
                {
                    Console.WriteLine("[DiffView] Stil-Feedback deaktiviert — keine Metriken gesendet.");
                    return;
                }

                var changedFragments = model.Lines
                    .Where(l => l.IsChanged)
                    .Select(l => l.NewText)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();

                if (changedFragments.Count == 0) return;

                var categories = changedFragments
                    .Select(_ => CategorizeChange(_originalText, _anonymizedText))
                    .ToList();

                var success = await _feedbackSender.SendMetricsAsync(changedFragments, categories);
                if (success)
                {
                    // Fire-and-forget; im echten UI koennte man eine kurze Statusmeldung
                    // im Diff-Fenster anzeigen. Hier wird absichtlich keine MessageBox
                    // gezeigt, um den Workflow nicht zu stoeren.
                    Console.WriteLine("[DiffView] Anonymisierte Stil-Metriken gesendet.");
                }
                else
                {
                    Console.WriteLine("[DiffView] Server hat Feedback-Metriken abgelehnt.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DiffView] Fehler beim Senden von Feedback-Metriken: {ex.Message}");
            }
        }

        private static string CategorizeChange(string original, string anonymized)
        {
            if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(anonymized))
                return "formulierung";

            // Paragraphen-Aenderungen
            var originalParagraphs = ParagraphPattern.Matches(original).Count;
            var anonymizedParagraphs = ParagraphPattern.Matches(anonymized).Count;
            if (originalParagraphs != anonymizedParagraphs)
                return "paragraph";

            // Laengenaenderungen
            var originalWords = original.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            var anonymizedWords = anonymized.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (Math.Abs(anonymizedWords - originalWords) / (double)Math.Max(originalWords, 1) > 0.2)
                return "laenge";

            // Satzstruktur
            var originalSentences = SentencePattern.Split(original).Count(s => !string.IsNullOrWhiteSpace(s));
            var anonymizedSentences = SentencePattern.Split(anonymized).Count(s => !string.IsNullOrWhiteSpace(s));
            if (originalSentences != anonymizedSentences)
                return "satzstruktur";

            return "formulierung";
        }

        public static DiffView Create(string original, string anonymized)
        {
            var view = new DiffView();
            view._originalText = original;
            view._anonymizedText = anonymized;
            view.UpdateDiff();
            return view;
        }
    }
}
