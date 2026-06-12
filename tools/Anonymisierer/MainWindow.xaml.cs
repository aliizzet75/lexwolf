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

        //Command für Ordner-Auswahl
        private async void OnAddFolderClicked(object sender, RoutedEventArgs e)
        {
            // FolderPicker öffnen (Windows-Standard-Dialog)
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Wählen Sie ein Verzeichnis zum Scannen aus",
                ShowNewFolderButton = false
            };

            var result = folderDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                _scanRootPath = folderDialog.SelectedPath;

                // UI-Status: Scannen starten
                var progressBar = this.FindName("ProgressBar") as System.Windows.Controls.ProgressBar;
                if (progressBar != null)
                {
                    progressBar.Value = 0;
                    progressBar.IsEnabled = true;
                }

                // Scan starten
                await ScanDirectoryAsync(_scanRootPath, (progress) =>
                {
                    if (progressBar != null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            progressBar.Value = progress;
                        });
                    }
                });

                // UI-Status: Scannen beendet
                if (progressBar != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        progressBar.IsEnabled = false;
                        MessageBox.Show($"Scan abgeschlossen: {_files.Count} Dateien gefunden");
                    });
                }
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

        private void OnAnonymizeClicked(object sender, RoutedEventArgs e)
        {
            // TODO: Implement anonymization logic
            MessageBox.Show("Anonymisierung TODO");
        }

        private void OnDeAnonymizeClicked(object sender, RoutedEventArgs e)
        {
            // TODO: Implement de-anonymization logic
            MessageBox.Show("De-Anonymisierung TODO");
        }

        private void OnFileSelected(object sender, RoutedEventArgs e)
        {
            // TODO: Implement file selection logic
            MessageBox.Show("Dateiauswahl TODO");
        }

        private void OnHelpClicked(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Hilfe: Anonymisierungsdetails");
        }

        private void OnSettingsClicked(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Einstellungen TODO");
        }

        private void OnExportClicked(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Exportieren TODO");
        }
    }
}
