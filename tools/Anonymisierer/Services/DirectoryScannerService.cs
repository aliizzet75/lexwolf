using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Anonymisierer.Models;

namespace Anonymisierer.Services
{
    // Rekursive Dateiscanner-Service
    public class DirectoryScannerService
    {
        // Unterstützte Dateiendungen
        private static readonly string[] SupportedExtensions = { ".docx", ".pdf", ".txt", ".eml" };

        // Scannet ein Verzeichnis rekursiv und sammelt Dateieinträge
        public static async Task<ObservableCollection<FileEntry>> ScanDirectoryAsync(string directoryPath, Action<double> onProgress)
        {
            var files = new ObservableCollection<FileEntry>();

            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return files;
            }

            try
            {
                // Erstmal alle Dateien in der aktuellen Ebene holen
                var currentFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly);
                var totalFiles = currentFiles.Length;
                var scanned = 0;

                foreach (var filePath in currentFiles)
                {
                    var extension = Path.GetExtension(filePath).ToLowerInvariant();
                    if (SupportedExtensions.Contains(extension))
                    {
                        var fileInfo = new FileInfo(filePath);
                        var entry = new FileEntry
                        {
                            Path = filePath,
                            Size = fileInfo.Length,
                            MandantFolder = GetFirstLevelDirectory(directoryPath, filePath),
                            TypeName = extension.Substring(1).ToUpperInvariant() // z.B. "DOCX"
                        };
                        files.Add(entry);
                    }

                    scanned++;
                    onProgress?.Invoke((double)scanned / totalFiles * 100);
                }

                // Jetzt alle Unterverzeichnisse scannen
                var subDirectories = Directory.GetDirectories(directoryPath);
                foreach (var subDir in subDirectories)
                {
                    var subFiles = await ScanDirectoryAsync(subDir, onProgress);
                    foreach (var file in subFiles)
                    {
                        files.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                // Fehler logging (z.B. Berechtigungsprobleme)
                System.Diagnostics.Debug.WriteLine($"Scan-Fehler: {ex.Message}");
            }

            return files;
        }

        // Ermittelt den Mandanten-Ordner (1. Ebene unter dem Wurzelverzeichnis)
        private static string GetFirstLevelDirectory(string rootPath, string filePath)
        {
            var rootInfo = new DirectoryInfo(rootPath);
            var fileInfo = new FileInfo(filePath);

            // Pfad relativ zum Root holen
            var relativePath = Path.GetRelativePath(rootPath, filePath);

            // Ersten Ordner in der Relativpfad-Struktur extrahieren
            var parts = relativePath.Split(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 1)
            {
                return parts[0];
            }

            return string.Empty;
        }
    }
}
