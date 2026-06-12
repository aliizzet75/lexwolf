using System;
using System.Collections.ObjectModel;

namespace LexWolf.Models
{
    // Node für TreeView - Mandant oder Datei
    public class FileTreeNode : System.ComponentModel.INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public bool IsFolder { get; set; }
        public string Icon => IsFolder ? "📁" : "📄";
        public ObservableCollection<FileTreeNode> Children { get; } = new();

        // Konstruktor für Ordner
        public FileTreeNode(string name, string path, bool isFolder = true)
        {
            Name = name;
            Path = path;
            IsFolder = isFolder;
        }

        // Konstruktor für Datei
        public FileTreeNode(string path)
        {
            var fileInfo = new System.IO.FileInfo(path);
            Name = fileInfo.Name;
            Path = path;
            Size = fileInfo.Length;
            IsFolder = false;
        }

        // Hilfsmethode zum Hinzufügen eines Kindes
        public void AddChild(FileTreeNode child)
        {
            Children.Add(child);
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}
