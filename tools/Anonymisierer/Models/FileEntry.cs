using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace Anonymisierer.Models
{
    // Modell für einen erkannten Dateieintrag
    public class FileEntry
    {
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public string MandantFolder { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;

        // Hilfseigenschaft für Typ-Erkennung
        public string Extension => System.IO.Path.GetExtension(Path).ToLowerInvariant();
    }
}
