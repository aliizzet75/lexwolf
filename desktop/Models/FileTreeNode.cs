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

        private bool _isPrioritized;
        public bool IsPrioritized
        {
            get => _isPrioritized;
            set
            {
                if (_isPrioritized == value) return;
                _isPrioritized = value;
                OnPropertyChanged(nameof(IsPrioritized));
                OnPropertyChanged(nameof(Icon));
            }
        }

        public string Icon => IsPrioritized ? "⭐" : (IsFolder ? "📁" : "📄");

        /// <summary>Oberster Ordner = Mandant (Kontextmenü „Mandant löschen“).</summary>
        public bool IsMandantOrdner { get; set; }
        public System.Windows.Visibility MandantMenuSichtbar =>
            IsMandantOrdner ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); }
        }

        private bool _isAktiv;
        /// <summary>Der oben ausgewählte Mandant — im Baum hervorgehoben.</summary>
        public bool IsAktiv
        {
            get => _isAktiv;
            set { if (_isAktiv == value) return; _isAktiv = value; OnPropertyChanged(nameof(IsAktiv)); }
        }
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
