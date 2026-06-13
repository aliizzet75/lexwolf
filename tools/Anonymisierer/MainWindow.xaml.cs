using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Anonymisierer.Models;
using Anonymisierer.Services;
using Anonymisierer.ViewModels;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using TreeView = System.Windows.Controls.TreeView;
using TextBox = System.Windows.Controls.TextBox;

namespace Anonymisierer
{
    public partial class MainWindow : Window
    {
        //ObservableCollection für die Dateiliste
        private ObservableCollection<FileEntry> _files;
        private string _scanRootPath = string.Empty;
        private FileTreeViewModel _fileTreeViewModel = new();

        public MainWindow()
        {
            InitializeComponent();
            _files = new ObservableCollection<FileEntry>();
            DataContext = this;
        }

        private System.Windows.Controls.ProgressBar? GetProgressBar() =>
            this.FindName("ProgressBar") as System.Windows.Controls.ProgressBar;
        private System.Windows.Controls.TextBlock? GetStatusLabel() =>
            this.FindName("StatusLabel") as System.Windows.Controls.TextBlock;

        private void ShowProgress()
        {
            var pb = GetProgressBar();
            var lbl = GetStatusLabel();
            if (pb != null)  { pb.Value = 0; pb.Visibility = System.Windows.Visibility.Visible; }
            if (lbl != null) { lbl.Visibility = System.Windows.Visibility.Collapsed; }
        }

        private void ShowStatus(string text)
        {
            var pb = GetProgressBar();
            var lbl = GetStatusLabel();
            if (pb != null)  { pb.Visibility = System.Windows.Visibility.Collapsed; }
            if (lbl != null) { lbl.Text = text; lbl.Visibility = System.Windows.Visibility.Visible; }
        }

        //Command für Ordner-Auswahl
        private async void OnAddFolderClicked(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Wählen Sie ein Verzeichnis zum Scannen aus",
                ShowNewFolderButton = false
            };

            var result = folderDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                _scanRootPath = folderDialog.SelectedPath;
                ShowProgress();

                await ScanDirectoryAsync(_scanRootPath, (progress) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var pb = GetProgressBar();
                        if (pb != null) pb.Value = progress;
                    });
                });

                Application.Current.Dispatcher.Invoke(() => ShowStatus("Bereit"));
            }
        }

        //Tree-View Dateiauswahl
        private void OnFileTreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var treeView = sender as TreeView;
            if (treeView?.SelectedItem is FileNode selectedNode)
            {
                if (!selectedNode.IsFolder && !string.IsNullOrEmpty(selectedNode.Path))
                {
                    // Dateivorschau laden
                    var content = _fileTreeViewModel.LoadFilePreview(selectedNode);
                    var previewTextBox = this.FindName("PreviewTextBox") as TextBox;
                    if (previewTextBox != null)
                    {
                        previewTextBox.Text = content;
                    }
                }
            }
        }

        //Rekursive Dateiscanner-Methode
        private async Task ScanDirectoryAsync(string directoryPath, Action<double> onProgress)
        {
            _files.Clear();

            var scanTask = DirectoryScannerService.ScanDirectoryAsync(directoryPath, onProgress);
            _files = await scanTask;

            //UI aktualisieren
            Application.Current.Dispatcher.Invoke(() =>
            {
                var fileList = this.FindName("FileList") as System.Windows.Controls.ListView;
                if (fileList != null)
                {
                    fileList.ItemsSource = _files;
                }
            });
        }

        private List<Entity> _lastEntities = new();
        private string _lastOriginalText = string.Empty;

        private void OnAnonymizeClicked(object sender, RoutedEventArgs e)
        {
            var previewBox = this.FindName("PreviewTextBox") as TextBox;
            if (previewBox == null || string.IsNullOrWhiteSpace(previewBox.Text))
            {
                MessageBox.Show("Bitte zuerst eine Datei auswählen.", "Kein Inhalt", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _lastOriginalText = previewBox.Text;
            var anonymized = Anonymizer.AnonymizeText(_lastOriginalText, out _lastEntities);
            previewBox.Text = anonymized;

            var entitiesPanel = this.FindName("EntitiesPanel") as StackPanel;
            if (entitiesPanel != null)
            {
                entitiesPanel.Children.Clear();
                if (_lastEntities.Count == 0)
                {
                    entitiesPanel.Children.Add(new TextBlock
                    {
                        Text = "Keine Entitäten erkannt.",
                        Foreground = System.Windows.Media.Brushes.Gray,
                        FontStyle = FontStyles.Italic
                    });
                }
                else
                {
                    foreach (var entity in _lastEntities)
                    {
                        entitiesPanel.Children.Add(new TextBlock
                        {
                            Text = $"[{entity.Type}] {entity.Text} → {entity.AnonymizedText}",
                            Margin = new Thickness(0, 2, 0, 2)
                        });
                    }
                }
            }
        }

        private void OnDeAnonymizeClicked(object sender, RoutedEventArgs e)
        {
            var previewBox = this.FindName("PreviewTextBox") as TextBox;
            if (previewBox == null || string.IsNullOrWhiteSpace(_lastOriginalText))
            {
                MessageBox.Show("Kein anonymisierter Text vorhanden.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            previewBox.Text = _lastOriginalText;

            var entitiesPanel = this.FindName("EntitiesPanel") as StackPanel;
            if (entitiesPanel != null)
            {
                entitiesPanel.Children.Clear();
                entitiesPanel.Children.Add(new TextBlock
                {
                    Text = "Text wiederhergestellt.",
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontStyle = FontStyles.Italic
                });
            }
        }

        private void OnHelpClicked(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Workflow:\n" +
                "  1. Ordner hinzufügen → Dateien werden gescannt\n" +
                "  2. Datei im Baum auswählen → Vorschau erscheint\n" +
                "  3. Anonymisieren → Entitäten werden ersetzt\n" +
                "  4. Exportieren → Alle Dateien in *_anonymisiert/\n\n" +
                "De-Anonymisieren stellt die Vorschau wieder her.",
                "Hilfe",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void OnSettingsClicked(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Unterstützte Formate:\n" +
                "  • Word (.docx)  — Formatierung bleibt erhalten\n" +
                "  • PDF (.pdf)    — Export als .txt\n" +
                "  • Text (.txt)   — Direktbearbeitung\n" +
                "  • E-Mail (.eml) — Textinhalt wird anonymisiert\n\n" +
                "Erkannte Entitäten:\n" +
                "  • Personen (großgeschriebene Namen)\n" +
                "  • Adressen (Straße + Hausnummer)\n" +
                "  • Aktenzeichen (Az. NNNN)\n" +
                "  • Geldbeträge (€NN,NN)",
                "Übersicht",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

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
                        var content = UnifiedFileReader.ReadFile(file.Path);
                        var anonymized = Anonymizer.AnonymizeText(content, out var entities);
                        var destPath = ExportService.GetDestPath(file.Path, outputDir, _scanRootPath);
                        ExportService.WriteFile(file.Path, destPath, anonymized, entities);
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

            Application.Current.Dispatcher.Invoke(() => ShowStatus("Fertig!"));

            MessageBox.Show(
                $"Export abgeschlossen!\n\n{success} Datei(en) → {outputDir}" +
                (failed > 0 ? $"\n{failed} konnten nicht exportiert werden." : ""),
                "Export",
                MessageBoxButton.OK,
                failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
    }
}
