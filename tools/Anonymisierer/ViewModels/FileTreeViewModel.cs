using System;
using System.Collections.ObjectModel;
using System.IO;
using Anonymisierer.Models;
using Anonymisierer.Services;

namespace Anonymisierer.ViewModels
{
    // Node für TreeView - Mandant oder Datei
    public class FileNode : NotifyBase
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public bool IsFolder { get; set; }
        public string Icon => IsFolder ? "📁" : "📄";
        public ObservableCollection<FileNode> Children { get; } = new();

        // Konstruktor für Ordner
        public FileNode(string name, string path, bool isFolder = true)
        {
            Name = name;
            Path = path;
            IsFolder = isFolder;
        }

        // Konstruktor für Datei
        public FileNode(string path)
        {
            var fileInfo = new FileInfo(path);
            Name = fileInfo.Name;
            Path = path;
            Size = fileInfo.Length;
            IsFolder = false;
        }

        // Hilfsmethode zum Hinzufügen eines Kindes
        public void AddChild(FileNode child)
        {
            Children.Add(child);
        }
    }

    // ViewModel für den Dateibaum
    public class FileTreeViewModel : NotifyBase
    {
        private ObservableCollection<FileNode> _rootNodes = new();
        private FileNode? _selectedNode;

        public ObservableCollection<FileNode> RootNodes
        {
            get => _rootNodes;
            set { _rootNodes = value; OnPropertyChanged(); }
        }

        public FileNode? SelectedNode
        {
            get => _selectedNode;
            set { _selectedNode = value; OnPropertyChanged(); }
        }

        // Scant ein Verzeichnis und erstellt die TreeView-Struktur
        public void ScanDirectory(string directoryPath)
        {
            RootNodes.Clear();

            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return;

            // Alle Mandanten-Ordner in der Wurzel holen
            var mandantDirs = Directory.GetDirectories(directoryPath);
            foreach (var mandantDir in mandantDirs)
            {
                var mandantName = System.IO.Path.GetFileName(mandantDir);
                var mandantNode = new FileNode(mandantName, mandantDir, true);

                // Alle Dateien im Mandanten-Ordner hinzufügen
                var files = Directory.GetFiles(mandantDir, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var extension = System.IO.Path.GetExtension(file).ToLowerInvariant();
                    if (new[] { ".docx", ".pdf", ".txt", ".eml" }.Contains(extension))
                    {
                        mandantNode.AddChild(new FileNode(file));
                    }
                }

                RootNodes.Add(mandantNode);
            }
        }

        // Lädt den Inhalt einer Datei für die Vorschau
        public string LoadFilePreview(FileNode node)
        {
            if (node == null || node.IsFolder)
                return "Keine Datei ausgewählt";

            try
            {
                return UnifiedFileReader.ReadFile(node.Path);
            }
            catch (Exception ex)
            {
                return $"Fehler beim Laden: {ex.Message}";
            }
        }
    }

    // Basisklasse für INotifyPropertyChanged
    public abstract class NotifyBase : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}
