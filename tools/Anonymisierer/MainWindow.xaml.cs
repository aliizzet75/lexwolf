using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Anonymisierer.Models;
using Anonymisierer.Services;
using Anonymisierer.ViewModels;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using TreeView = System.Windows.Controls.TreeView;
using TextBox = System.Windows.Controls.TextBox;
using RichTextBox = System.Windows.Controls.RichTextBox;
using Color = System.Windows.Media.Color;

namespace Anonymisierer
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<FileEntry> _files = new();
        private string _scanRootPath = string.Empty;
        private FileTreeViewModel _fileTreeViewModel = new();
        private List<Entity> _lastEntities = new();
        private string _lastOriginalText = string.Empty;
        private bool _isScrollSyncing;

        private static readonly Regex _placeholderRx = new(@"\[[^\]]+\]", RegexOptions.Compiled);
        private static readonly SolidColorBrush _hlBg  = new(Color.FromRgb(0x3d, 0x2e, 0x00));
        private static readonly SolidColorBrush _hlFg  = new(Color.FromRgb(0xff, 0xc9, 0x47));
        private static readonly SolidColorBrush _defFg = new(Color.FromRgb(0xc9, 0xd1, 0xd9));
        private static readonly System.Windows.Media.FontFamily _monoFont = new("Consolas");

        public MainWindow()
        {
            InitializeComponent();
        }

        // ── Accessoren ────────────────────────────────────────────────────────

        private System.Windows.Controls.ProgressBar? GetProgressBar() =>
            this.FindName("ProgressBar") as System.Windows.Controls.ProgressBar;

        private TextBlock? GetStatusLabel() =>
            this.FindName("StatusLabel") as TextBlock;

        private TextBox? GetOriginalBox() =>
            this.FindName("OriginalBox") as TextBox;

        private RichTextBox? GetAnonymizedRich() =>
            this.FindName("AnonymizedRich") as RichTextBox;

        private ScrollViewer? GetOriginalScroll() =>
            this.FindName("OriginalScroll") as ScrollViewer;

        private ScrollViewer? GetAnonymizedScroll() =>
            this.FindName("AnonymizedScroll") as ScrollViewer;

        private TextBlock? GetEntityCountLabel() =>
            this.FindName("EntityCountLabel") as TextBlock;

        // ── Progress / Status ─────────────────────────────────────────────────

        private void ShowProgress()
        {
            var pb = GetProgressBar();
            if (pb != null) { pb.IsIndeterminate = false; pb.Value = 0; pb.Visibility = Visibility.Visible; }
        }

        private void ShowSpinner(string text)
        {
            var pb = GetProgressBar();
            var lbl = GetStatusLabel();
            if (pb != null) { pb.IsIndeterminate = true; pb.Visibility = Visibility.Visible; }
            if (lbl != null) lbl.Text = text;
        }

        private void ShowStatus(string text)
        {
            var pb = GetProgressBar();
            var lbl = GetStatusLabel();
            if (pb != null) { pb.IsIndeterminate = false; pb.Visibility = Visibility.Collapsed; }
            if (lbl != null) lbl.Text = text;
        }

        // ── Ordner scannen ────────────────────────────────────────────────────

        private async void OnAddFolderClicked(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Verzeichnis zum Scannen auswählen",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            _scanRootPath = folderDialog.SelectedPath;

            var folderLabel = this.FindName("FolderPathLabel") as System.Windows.Controls.TextBlock;
            if (folderLabel != null)
            {
                folderLabel.Text = _scanRootPath;
                folderLabel.Visibility = Visibility.Visible;
            }

            ShowProgress();

            await ScanDirectoryAsync(_scanRootPath, progress =>
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var pb = GetProgressBar();
                    if (pb != null) pb.Value = progress;
                }));

            Application.Current.Dispatcher.Invoke(() =>
            {
                _fileTreeViewModel.ScanDirectory(_scanRootPath);
                var fileTree = this.FindName("FileTree") as TreeView;
                if (fileTree != null) fileTree.ItemsSource = _fileTreeViewModel.RootNodes;
                ShowStatus($"{_files.Count} Datei(en) gefunden");
            });
        }

        private async Task ScanDirectoryAsync(string directoryPath, Action<double> onProgress)
        {
            _files.Clear();
            var result = await DirectoryScannerService.ScanDirectoryAsync(directoryPath, onProgress);
            _files = result;
        }

        // ── Dateibaum Auswahl → automatisch anonymisieren ─────────────────────

        private async void OnFileTreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if ((sender as TreeView)?.SelectedItem is not FileNode node) return;
            if (node.IsFolder || string.IsNullOrEmpty(node.Path)) return;

            var fileName = System.IO.Path.GetFileName(node.Path);
            ShowSpinner($"Lade und anonymisiere {fileName}...");

            var originalBox  = GetOriginalBox();
            var anonymizedRich = GetAnonymizedRich();
            var countLabel   = GetEntityCountLabel();
            if (originalBox != null) originalBox.Text = "";
            if (anonymizedRich != null) anonymizedRich.Document = new FlowDocument();
            if (countLabel != null) countLabel.Text = "";

            try
            {
                string content = "";
                string anonymized = "";
                List<Entity> entities = new();

                await Task.Run(() =>
                {
                    content   = _fileTreeViewModel.LoadFilePreview(node);
                    anonymized = Anonymizer.AnonymizeText(content, out entities);
                });

                _lastOriginalText = content;
                _lastEntities     = entities;

                if (originalBox != null)    originalBox.Text = content;
                if (anonymizedRich != null) anonymizedRich.Document = BuildAnonymizedDoc(anonymized);
                if (countLabel != null)     countLabel.Text = $"{entities.Count} Entität(en) ersetzt";

                ShowStatus($"{fileName} — {entities.Count} Entität(en) anonymisiert");
            }
            catch (Exception ex)
            {
                ShowStatus($"Fehler: {ex.Message}");
            }
        }

        // ── Scroll-Synchronisierung ───────────────────────────────────────────

        private void OnOriginalScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isScrollSyncing || e.VerticalChange == 0) return;
            _isScrollSyncing = true;
            GetAnonymizedScroll()?.ScrollToVerticalOffset(e.VerticalOffset);
            _isScrollSyncing = false;
        }

        private void OnAnonymizedScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isScrollSyncing || e.VerticalChange == 0) return;
            _isScrollSyncing = true;
            GetOriginalScroll()?.ScrollToVerticalOffset(e.VerticalOffset);
            _isScrollSyncing = false;
        }

        // ── FlowDocument mit Farb-Highlighting ────────────────────────────────

        private static FlowDocument BuildAnonymizedDoc(string text)
        {
            var doc = new FlowDocument
            {
                FontFamily  = (System.Windows.Media.FontFamily)_monoFont,
                FontSize    = 12,
                Foreground  = _defFg,
                PagePadding = new Thickness(0)
            };

            var para = new Paragraph { Margin = new Thickness(14), Padding = new Thickness(0) };

            bool firstLine = true;
            foreach (var rawLine in text.Split('\n'))
            {
                if (!firstLine) para.Inlines.Add(new LineBreak());
                firstLine = false;

                var line = rawLine.TrimEnd('\r');
                var lastIdx = 0;

                foreach (Match m in _placeholderRx.Matches(line))
                {
                    if (m.Index > lastIdx)
                        para.Inlines.Add(new Run(line[lastIdx..m.Index]));

                    para.Inlines.Add(new Run(m.Value)
                    {
                        Background = _hlBg,
                        Foreground = _hlFg,
                        FontWeight = FontWeights.SemiBold
                    });
                    lastIdx = m.Index + m.Length;
                }

                if (lastIdx < line.Length)
                    para.Inlines.Add(new Run(line[lastIdx..]));
            }

            doc.Blocks.Add(para);
            return doc;
        }

        // ── Details ───────────────────────────────────────────────────────────

        private void OnDetailsClicked(object sender, RoutedEventArgs e)
        {
            if (_lastEntities.Count == 0)
            {
                MessageBox.Show("Noch keine Anonymisierung durchgeführt.",
                    "Details", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var lines = string.Join("\n", _lastEntities.ConvertAll(
                ent => $"{ent.AnonymizedText,-24} ←  {ent.Text}  [{ent.Type}]"));

            var dlg = new Window
            {
                Title = $"Erkannte Entitäten ({_lastEntities.Count})",
                Width = 650, Height = 450,
                Owner = this,
                Background = System.Windows.Media.Brushes.White,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var tb = new TextBox
            {
                Text = lines,
                IsReadOnly = true,
                AcceptsReturn = true,
                FontFamily = (System.Windows.Media.FontFamily)_monoFont,
                FontSize = 12,
                Margin = new Thickness(12),
                BorderThickness = new Thickness(0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            dlg.Content = tb;
            dlg.ShowDialog();
        }

        // ── Exportieren ───────────────────────────────────────────────────────

        private async void OnExportClicked(object sender, RoutedEventArgs e)
        {
            if (_files.Count == 0 || string.IsNullOrEmpty(_scanRootPath))
            {
                MessageBox.Show("Bitte zuerst einen Ordner scannen.", "Kein Inhalt",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var outputDir = ExportService.GetOutputDirectory(_scanRootPath);
            ShowProgress();
            Cursor = System.Windows.Input.Cursors.Wait;

            int success = 0, failed = 0;
            var snapshot = _files.ToList();
            double total = snapshot.Count;

            await Task.Run(() =>
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var file = snapshot[i];
                    try
                    {
                        var content    = UnifiedFileReader.ReadFile(file.Path);
                        var anonymized = Anonymizer.AnonymizeText(content, out _);
                        var destPath   = ExportService.GetDestPath(file.Path, outputDir, _scanRootPath);
                        ExportService.WriteFile(file.Path, destPath, anonymized, new List<Entity>());
                        success++;
                    }
                    catch { failed++; }

                    double progress = ((i + 1) / total) * 100;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var pb = GetProgressBar();
                        if (pb != null) pb.Value = progress;
                    });
                }
            });

            Cursor = System.Windows.Input.Cursors.Arrow;
            ShowStatus($"Export: {success} OK" + (failed > 0 ? $", {failed} Fehler" : ""));

            MessageBox.Show(
                $"Export abgeschlossen!\n\n{success} Datei(en) → {outputDir}" +
                (failed > 0 ? $"\n{failed} konnten nicht exportiert werden." : ""),
                "Export", MessageBoxButton.OK,
                failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
    }
}
