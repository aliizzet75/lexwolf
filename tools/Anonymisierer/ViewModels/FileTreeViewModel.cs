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

        private static readonly string[] SupportedExtensions = { ".docx", ".pdf", ".txt", ".eml", ".rtf", ".odt" };

        // Scant ein Verzeichnis und erstellt die TreeView-Struktur
        public void ScanDirectory(string directoryPath)
        {
            RootNodes.Clear();

            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return;

            // Dateien direkt im Root-Verzeichnis
            foreach (var file in Directory.GetFiles(directoryPath))
            {
                if (SupportedExtensions.Contains(System.IO.Path.GetExtension(file).ToLowerInvariant()))
                    RootNodes.Add(new FileNode(file));
            }

            // Unterordner als aufklappbare Knoten
            foreach (var subDir in Directory.GetDirectories(directoryPath))
            {
                var folderNode = new FileNode(System.IO.Path.GetFileName(subDir), subDir, true);
                foreach (var file in Directory.GetFiles(subDir, "*.*", SearchOption.AllDirectories))
                {
                    if (SupportedExtensions.Contains(System.IO.Path.GetExtension(file).ToLowerInvariant()))
                        folderNode.AddChild(new FileNode(file));
                }
                RootNodes.Add(folderNode);
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
