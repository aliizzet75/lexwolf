using System;
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
        private string _originalText = string.Empty;
        private string _anonymizedText = string.Empty;
        private readonly FlowDocument _document = new();

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
