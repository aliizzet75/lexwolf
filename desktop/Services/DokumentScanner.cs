using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LexWolf.Database;

namespace LexWolf.Services
{
    public class DokumentScanner : IDisposable
    {
        private readonly string _basePath;
        private readonly LocalDb _db;
        private readonly List<FileSystemWatcher> _watchers = new();

        /// <summary>Wird aufgerufen wenn zur Laufzeit ein neuer Mandanten-Ordner
        /// (Top-Level-Verzeichnis unter basePath) auftaucht — UI kann damit das
        /// Mandanten-Dropdown neu laden ohne Neustart.</summary>
        public Action? OnNeuerMandant;

        /// <summary>Nach jedem erfolgreichen (Neu-)Einlesen einer Datei: (mandantId, pfad).
        /// Der AkteService entscheidet, ob sie als neuer Eingang ausgewertet wird.</summary>
        public Action<string, string>? OnDateiIndexiert;

        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".docx", ".pdf", ".txt", ".eml"
        };

        private const int ChunkSize    = 500;
        private const int ChunkOverlap = 100;

        public DokumentScanner(string basePath, LocalDb db)
        {
            _basePath = basePath;
            _db = db;
        }

        // -------------------------------------------------------------------------
        // Vollständiger Scan beim Start
        // -------------------------------------------------------------------------

        public void ScanAll()
        {
            if (!Directory.Exists(_basePath))
                return;

            foreach (var mandantDir in Directory.EnumerateDirectories(_basePath))
            {
                var mandantName   = Path.GetFileName(mandantDir);
                var mandantNummer = mandantName; // Ordnername = Name/Nummer
                var mandantId     = ToId(mandantName);

                _db.UpsertMandant(mandantId, mandantName, mandantNummer);
                IndexDirectory(mandantId, mandantDir);
            }
        }

        private void IndexDirectory(string mandantId, string dir)
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (SupportedExtensions.Contains(Path.GetExtension(file)))
                    IndexFile(mandantId, file);
            }
        }

        // -------------------------------------------------------------------------
        // Einzelne Datei indexieren
        // -------------------------------------------------------------------------

        private void IndexFile(string mandantId, string filePath)
        {
            try
            {
                var text      = ReadText(filePath);
                var geaendert = File.GetLastWriteTimeUtc(filePath).ToString("o");
                var titel     = Path.GetFileNameWithoutExtension(filePath);
                var chunks    = Chunk(text);

                _db.DeleteDokumentChunks(filePath);
                for (int i = 0; i < chunks.Count; i++)
                {
                    var chunkId = ToId($"{filePath}_{i}");
                    _db.UpsertDokumentChunk(chunkId, mandantId, filePath, titel, chunks[i], geaendert);
                }
                OnDateiIndexiert?.Invoke(mandantId, filePath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DokumentScanner] Fehler bei {filePath}: {ex.Message}");
            }
        }

        // -------------------------------------------------------------------------
        // Text aus Datei lesen (.txt / .eml → direkt; .docx / .pdf → Rohtext)
        // -------------------------------------------------------------------------

        /// <summary>Eine gerade abgelegte Datei sofort einlesen, ohne auf den Ordner-Wächter zu warten
        /// (z. B. damit die KI sie bei der nächsten Frage schon kennt).</summary>
        public void DateiEinlesen(string mandantId, string pfad)
        {
            if (File.Exists(pfad) && SupportedExtensions.Contains(Path.GetExtension(pfad)))
                IndexFile(mandantId, pfad);
        }

        public static bool IstLesbar(string pfad) => SupportedExtensions.Contains(Path.GetExtension(pfad));

        /// <summary>Volltext einer Datei (wie beim Indexieren, inkl. OCR-Fallback).</summary>
        public static string LeseText(string path) => ReadText(path);

        public static string MandantIdFuer(string mandantOrdnerName) => ToId(mandantOrdnerName);

        private static string ReadText(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".txt")
                return File.ReadAllText(path, Encoding.UTF8);

            // MIME dekodieren (Base64, quoted-printable, HTML) statt Rohtext
            if (ext == ".eml")
                return MailText.LeseEml(path);

            if (ext == ".docx")
                return ReadDocx(path);

            if (ext == ".pdf")
                return ReadPdf(path);

            return string.Empty;
        }

        // Minimal .docx-Reader: zieht Text aus word/document.xml (keine externe Lib nötig)
        private static string ReadDocx(string path)
        {
            try
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(path);
                var entry = zip.GetEntry("word/document.xml");
                if (entry == null) return string.Empty;

                using var stream = entry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var xml = reader.ReadToEnd();

                // Einfaches Tag-Stripping
                var sb = new StringBuilder();
                bool inside = false;
                foreach (char c in xml)
                {
                    if (c == '<') { inside = true; continue; }
                    if (c == '>') { inside = false; sb.Append(' '); continue; }
                    if (!inside) sb.Append(c);
                }
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        // PdfPig-basierter Reader. Der bisherige Ansatz (rohe Bytes nach
        // "(...)"-Mustern durchsuchen) fand nur unkomprimierte PDF-Content-Streams —
        // praktisch alle modernen PDFs komprimieren diese (Flate/zlib), wodurch
        // fast nie Text extrahiert wurde. PdfPig dekomprimiert und parst korrekt.
        // Bei rein gescannten Dokumenten ohne Textebene (z.B. abfotografierte
        // beglaubigte Abschriften) bleibt der Text trotzdem leer — das würde OCR
        // brauchen, was PdfPig nicht macht.
        private static string ReadPdf(string path)
        {
            try
            {
                var text = PdfTextExtractor.ExtractText(path);
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
                // Kein Fehler, aber leerer Text -> PdfPig konnte öffnen, es gibt
                // schlicht keine Textebene (typisch für gescannte/abfotografierte
                // Dokumente). Weiter unten per OCR versuchen statt hier aufzugeben.
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DokumentScanner] PdfPig-Lesefehler bei {path}: {ex}");
                // Auch bei einem harten PdfPig-Fehler noch OCR versuchen — manche
                // Dateien lassen sich zwar nicht textparsen, aber rastern.
            }

            try
            {
                var ocrText = OcrService.TryReadPdfViaOcrAsync(path).GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(ocrText))
                    return ocrText!;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DokumentScanner] OCR-Fallback fehlgeschlagen bei {path}: {ex}");
            }

            return $"[{Path.GetFileName(path)}: Weder Textebene noch OCR-Ergebnis gefunden — " +
                   "PDF evtl. beschädigt, leer, oder kein passendes OCR-Sprachpaket auf diesem System installiert.]";
        }

        // -------------------------------------------------------------------------
        // Chunking: 500 Zeichen, 100 Zeichen Überlappung
        // -------------------------------------------------------------------------

        private static List<string> Chunk(string text)
        {
            var chunks = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                chunks.Add(string.Empty);
                return chunks;
            }

            int pos = 0;
            while (pos < text.Length)
            {
                var end = Math.Min(pos + ChunkSize, text.Length);
                chunks.Add(text[pos..end]);
                pos += ChunkSize - ChunkOverlap;
                if (pos >= text.Length) break;
            }
            return chunks;
        }

        // -------------------------------------------------------------------------
        // FileSystemWatcher: automatische Nachindexierung
        // -------------------------------------------------------------------------

        public void StartWatching()
        {
            if (!Directory.Exists(_basePath))
                return;

            foreach (var mandantDir in Directory.EnumerateDirectories(_basePath))
            {
                AttachWatcher(mandantDir);
            }

            // Watcher für neue Mandanten-Ordner auf oberster Ebene
            var rootWatcher = new FileSystemWatcher(_basePath)
            {
                NotifyFilter      = NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            rootWatcher.Created += (_, e) =>
            {
                if (Directory.Exists(e.FullPath))
                {
                    var name = Path.GetFileName(e.FullPath);
                    var id   = ToId(name);
                    _db.UpsertMandant(id, name, name);
                    AttachWatcher(e.FullPath);
                    OnNeuerMandant?.Invoke();
                }
            };
            _watchers.Add(rootWatcher);
        }

        private void AttachWatcher(string mandantDir)
        {
            var mandantName = Path.GetFileName(mandantDir);
            var mandantId   = ToId(mandantName);

            var w = new FileSystemWatcher(mandantDir)
            {
                NotifyFilter          = NotifyFilters.LastWrite | NotifyFilters.FileName,
                IncludeSubdirectories = true,
                EnableRaisingEvents   = true
            };

            FileSystemEventHandler handler = (_, e) =>
            {
                if (SupportedExtensions.Contains(Path.GetExtension(e.FullPath)))
                    IndexFile(mandantId, e.FullPath);
            };

            w.Created += handler;
            w.Changed += handler;
            // Gelöschte/verschobene Dateien aus dem Index nehmen — sonst stünden sie
            // weiter im KI-Kontext (z.B. ein in die Akte übernommener Entwurf doppelt).
            // Verschieben zwischen Unterordnern meldet Windows als Deleted + Created.
            w.Deleted += (_, e) => EntferneAusIndex(e.FullPath);
            w.Renamed += (_, e) =>
            {
                EntferneAusIndex(e.OldFullPath);
                handler(this, e);
            };
            _watchers.Add(w);
        }

        // -------------------------------------------------------------------------
        // Hilfsmethoden
        // -------------------------------------------------------------------------

        private void EntferneAusIndex(string filePath)
        {
            try
            {
                _db.DeleteDokumentChunks(filePath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DokumentScanner] Entfernen aus Index fehlgeschlagen für {filePath}: {ex.Message}");
            }
        }

        private static string ToId(string s) =>
            Convert.ToHexString(System.Security.Cryptography.MD5.HashData(
                Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

        public void Dispose()
        {
            foreach (var w in _watchers)
                w.Dispose();
        }
    }
}
