using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using LexWolf.Services;

namespace LexWolf.Controls
{
    /// <summary>
    /// Interaktionslogik für DiffView.xaml
    /// </summary>
    public partial class DiffView : UserControl
    {
        private string _originalText = string.Empty;
        private string _anonymizedText = string.Empty;

        public DiffView()
        {
            InitializeComponent();
        }

        // Setzt den Originaltext
        public string OriginalText
        {
            get => _originalText;
            set { _originalText = value; UpdateDiff(); }
        }

        // Setzt den anonymisierten Text
        public string AnonymizedText
        {
            get => _anonymizedText;
            set { _anonymizedText = value; UpdateDiff(); }
        }

        // Diff berechnen und anzeigen
        private void OnComputeDiff(object sender, RoutedEventArgs e)
        {
            UpdateDiff();
        }

        // Diff aktualisieren mit InlineDiffBuilder
        private void UpdateDiff()
        {
            if (string.IsNullOrEmpty(_originalText) || string.IsNullOrEmpty(_anonymizedText))
                return;

            var diffModel = InlineDiffBuilder.Diff(_originalText, _anonymizedText);

            DiffDocument.Blocks.Clear();
            var para = new Paragraph();
            DiffDocument.Blocks.Add(para);

            foreach (var line in diffModel.Lines)
            {
                var run = new Run(line.Text);
                switch (line.Type)
                {
                    case ChangeType.Deleted:
                        run.Foreground = Brushes.Red;
                        break;
                    case ChangeType.Inserted:
                        run.Foreground = Brushes.Green;
                        break;
                    default:
                        run.Foreground = Brushes.Gray;
                        break;
                }
                para.Inlines.Add(run);
                para.Inlines.Add(new LineBreak());
            }
        }

        // Statische Methode zum Erstellen einer DiffView
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
