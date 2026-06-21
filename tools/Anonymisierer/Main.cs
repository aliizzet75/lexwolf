using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using MimeKit;

namespace Anonymisierer
{
    // Entitäts-Typen für die Anonymisierung
    public enum EntityType
    {
        Mandant,
        Person,
        Betrag,
        Datum,
        Adresse,
        Aktenzeichen,
        Konto,
        Unternehmen,
        Email,
        Telefon
    }

    // Erkannte Entität
    public class Entity
    {
        public string Text { get; set; } = string.Empty;
        public EntityType Type { get; set; }
        public string AnonymizedText { get; set; } = string.Empty;
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
    }

    // Mandanten-Klasse
    public class Mandant
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Nummer { get; set; } = string.Empty;
        public DateTime Erstellt { get; set; }
        public DateTime? LetzterKontakt { get; set; }
    }

    // Hauptklasse für die Anonymisierungs-Logik
    public static class Anonymizer
    {
        private static readonly Dictionary<string, string> _mapping = new();
        private static readonly Dictionary<string, EntityType> _entityTypes = new();
        private static string _mappingFilePath = string.Empty;
        private static int _counter = 1;
        private static int _personIdx;
        private static int _addrIdx;
        private static int _datumIdx;
        private static int _betragIdx;
        private static int _ibanIdx;
        private static int _emailIdx;

        private static readonly string[] _fakeDaten =
        {
            "15.06.1985", "03.09.1972", "21.11.1990", "08.04.1967",
            "30.01.1983", "17.07.1995", "25.12.1975", "11.03.2000"
        };
        private static readonly string[] _fakeBetraege =
        {
            "1.250,00", "875,50", "3.400,00", "620,00",
            "15.750,00", "490,00", "2.100,00", "1.050,25"
        };
        private static readonly string[] _fakeIbans =
        {
            "DE89 3704 0044 0532 0130 00",
            "DE02 2012 0500 0000 0250 01",
            "DE12 5004 0075 0000 1234 00",
            "DE91 1000 0000 0123 4567 89"
        };
        private static readonly string[] _fakeEmails =
        {
            "info@beispiel-kanzlei.de",
            "max.muster@muster-gmbh.de",
            "kontakt@beispiel-partner.de",
            "service@test-immobilien.de"
        };

        private static readonly string[] PersonPool =
        {
            "Asterix", "Obelix", "Miraculix", "Majestix", "Troubadix", "Verleihnix",
            "Donald Duck", "Dagobert Duck", "Tick", "Trick", "Track", "Daisy Duck",
            "Lucky Luke", "Jolly Jumper", "Calamity Jane", "Billy the Kid",
            "Batman", "Robin", "Superman", "Wonder Woman", "Flash", "Green Lantern",
            "Spider-Man", "Iron Man", "Thor", "Hulk", "Black Widow", "Hawkeye",
            "Luke Skywalker", "Han Solo", "Leia Organa", "Yoda", "Chewbacca",
            "Sherlock Holmes", "Dr. Watson", "Irene Adler", "Prof. Moriarty",
            "Gandalf", "Frodo Beutlin", "Aragorn Elessar", "Legolas", "Gimli",
            "Pippi Langstrumpf", "Indiana Jones", "James Bond", "Ethan Hunt"
        };

        private static readonly string[] AdressPool =
        {
            "Privet Drive 4", "Baker Street 221b", "Downing Street 10",
            "Auenland-Weg 3", "Moria-Pfad 7", "Rivendell-Allee 1",
            "Mos-Eisley-Straße 42", "Tatooine-Ring 99", "Coruscant-Boulevard 1",
            "Batcave-Weg 7", "Wayne-Manor-Allee 1", "Gotham-Platz 13",
            "Hogwarts-Allee 9¾", "Diagon-Gasse 93",
            "Entenhausen-Ufer 3", "Geldspeicher-Ring 1",
            "Schlumpfhausen-Gasse 10", "Smurf-Allee 42"
        };

        // Häufige deutsche Substantive die keine Namen sind
        private static readonly HashSet<string> _stopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "Mieter", "Vermieter", "Partei", "Herr", "Frau", "Januar", "Februar", "März",
            "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November",
            "Dezember", "Mietvertrag", "Vertrag", "Deutschland", "Bundesrepublik",
            "Wohnung", "Zimmer", "Küche", "Keller", "Garage", "Etage", "Stockwerk",
            "Anlage", "Anhang", "Seite", "Abschnitt", "Paragraph", "Absatz",
        };

        // Kombinierte Regex: Gruppe 1 = bereits ersetzte [Token] überspringen,
        // Gruppe 2 = echter Personenname inkl. Initialen wie "Wilhelm R. Schapmann"
        private static readonly System.Text.RegularExpressions.Regex _rxPerson =
            new(@"(\[[^\]]+\])|(\b[A-ZÄÖÜ][a-zäöüß]{1,20}(?:[^\S\r\n]+(?:[A-ZÄÖÜ]\.[^\S\r\n]*)?[A-ZÄÖÜ][a-zäöüß]{1,20}){1,3}\b)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex _rxDatum =
            new(@"\b(\d{1,2}\.\s*\d{1,2}\.\s*\d{4}|\d{1,2}\.\s*(?:Januar|Februar|März|April|Mai|Juni|Juli|August|September|Oktober|November|Dezember)\s+\d{4})\b",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly System.Text.RegularExpressions.Regex _rxAdresse =
            new(@"\b([A-ZÄÖÜ][a-zäöüß]+(?:straße|strasse|str\.|weg|gasse|platz|allee|ring|damm|pfad|ufer)[^\S\r\n]+\d{1,3}\s*[a-z]?)\b",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly System.Text.RegularExpressions.Regex _rxPlz =
            new(@"\b(\d{5})[^\S\r\n]+([A-ZÄÖÜ][a-zäöüß]+)\b",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex _rxTelefon =
            new(@"\b((?:\+49|0049|0)\d[\d\-\/]{6,13})\b",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex _rxIban =
            new(@"\b([A-Z]{2}\d{2}(?:[^\S\r\n]?\d{4}){4}(?:[^\S\r\n]?\d{1,6})?)\b",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex _rxBetrag =
            new(@"(?<!\d)(\d{1,3}(?:\.\d{3})*,\d{2})(?!\d)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex _rxAktenzeichen =
            new(@"\b(Az\.?\s*[\w\d]{1,6}[\s\/\-][\w\d\/\-]+)\b",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly System.Text.RegularExpressions.Regex _rxEmail =
            new(@"\b[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}\b",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        public static string AnonymizeText(string text, out List<Entity> entities)
        {
            entities = new List<Entity>();
            string result = text;

            result = ReplaceWithAlias(result, _rxIban,         EntityType.Konto,        () => _fakeIbans[_ibanIdx++ % _fakeIbans.Length],         entities);
            result = ReplaceWithAlias(result, _rxAktenzeichen, EntityType.Aktenzeichen, () => $"[AZ-{_counter++}]",                                       entities);
            result = ReplaceWithAlias(result, _rxTelefon,      EntityType.Telefon,      () => $"[TEL-{_counter++}]",                                      entities);
            result = ReplaceWithAlias(result, _rxEmail,        EntityType.Email,        () => _fakeEmails[_emailIdx++ % _fakeEmails.Length],               entities);
            result = ReplaceWithAlias(result, _rxBetrag,       EntityType.Betrag,       () => _fakeBetraege[_betragIdx++ % _fakeBetraege.Length],          entities);
            result = ReplaceWithAlias(result, _rxDatum,        EntityType.Datum,        () => _fakeDaten[_datumIdx++ % _fakeDaten.Length],                 entities);
            result = ReplaceWithAlias(result, _rxAdresse,      EntityType.Adresse,      () => $"[{AdressPool[_addrIdx++ % AdressPool.Length]}]",           entities);
            result = ReplaceWithAlias(result, _rxPlz,          EntityType.Adresse,      () => $"[ORT-{_counter++}]",                                      entities);
            result = ReplacePersons(result, entities);

            return result;
        }

        private static string ReplaceWithAlias(
            string text,
            System.Text.RegularExpressions.Regex rx,
            EntityType type,
            Func<string> makeAlias,
            List<Entity> entities)
        {
            return rx.Replace(text, match =>
            {
                var original = match.Value;
                if (_mapping.TryGetValue(original, out var existing))
                {
                    if (!entities.Any(e => e.Text == original))
                        entities.Add(new Entity { Text = original, Type = type, AnonymizedText = existing });
                    return existing;
                }
                var alias = makeAlias();
                _mapping[original] = alias;
                _entityTypes[original] = type;
                entities.Add(new Entity { Text = original, Type = type, AnonymizedText = alias });
                return alias;
            });
        }

        private static string ReplacePersons(string text, List<Entity> entities)
        {
            return _rxPerson.Replace(text, match =>
            {
                // Gruppe 1: bereits ersetzter [Token] → unverändert lassen
                if (!match.Groups[2].Success) return match.Value;

                var original = match.Groups[2].Value;
                if (_stopWords.Contains(original)) return original;
                if (_mapping.TryGetValue(original, out var existing))
                {
                    if (!entities.Any(e => e.Text == original))
                        entities.Add(new Entity { Text = original, Type = EntityType.Person, AnonymizedText = existing });
                    return existing;
                }

                var alias = $"[{PersonPool[_personIdx++ % PersonPool.Length]}]";
                _mapping[original] = alias;
                _entityTypes[original] = EntityType.Person;
                entities.Add(new Entity { Text = original, Type = EntityType.Person, AnonymizedText = alias });
                return alias;
            });
        }

        // De-Anonymisierung
        public static string DeAnonymizeText(string text, Dictionary<string, string> reverseMapping)
        {
            string result = text;

            foreach (var kvp in reverseMapping)
            {
                result = result.Replace($"[{kvp.Key}]", kvp.Value);
            }

            return result;
        }

        // Word-Dokument öffnen und Inhalt extrahieren
        public static string ReadWordDocument(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Datei nicht gefunden: {filePath}", filePath);

            using (var document = WordprocessingDocument.Open(filePath, false))
            {
                var body = document.MainDocumentPart.Document.Body;
                var text = body.InnerText;
                return text;
            }
        }

        // PDF-Dokument öffnen und Inhalt extrahieren (mit iText7)
        public static string ReadPdfDocument(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Datei nicht gefunden: {filePath}", filePath);

            // Verwendung von iText7 (vollständige PDF-Lesung ohne Layout-Zerstörung)
            var sb = new StringBuilder();
            using (var reader = new PdfReader(filePath))
            {
                using (var document = new PdfDocument(reader))
                {
                    for (int page = 1; page <= document.GetNumberOfPages(); page++)
                    {
                        var text = PdfTextExtractor.GetTextFromPage(document.GetPage(page));
                        sb.Append(text);
                        sb.Append("\n");
                    }
                }
            }

            return sb.ToString();
        }

        // EML-Dokument (E-Mail) öffnen und Inhalt extrahieren (mit MimeKit)
        public static string ReadEmlDocument(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Datei nicht gefunden: {filePath}", filePath);

            var sb = new StringBuilder();
            using (var stream = File.OpenRead(filePath))
            {
                var message = MimeMessage.Load(stream);

                // Absender und Empfänger
                sb.Append($"Von: {message.From}");
                sb.Append($"\nAn: {message.To}");
                sb.Append($"\nBetreff: {message.Subject}");
                sb.Append("\n\n");

                // Textinhalt
                if (message.TextBody != null)
                {
                    sb.Append(message.TextBody);
                }
                else if (message.HtmlBody != null)
                {
                    // HTML-Entfernung (vereinfacht)
                    var text = System.Text.RegularExpressions.Regex.Replace(message.HtmlBody, @"<[^>]+>", string.Empty);
                    sb.Append(text);
                }

                // Anhänge auflisten
                if (message.Attachments.Any())
                {
                    sb.Append("\n\nAnhänge: ");
                    foreach (var attachment in message.Attachments)
                    {
                        sb.Append($"{attachment.ContentType.Name}, ");
                    }
                }
            }

            return sb.ToString();
        }

        // Text vergleichen mit DiffPlex
        public static string GenerateDiff(string oldText, string newText)
        {
            var diff = InlineDiffBuilder.Diff(oldText, newText);

            var result = new StringBuilder();
            foreach (var line in diff.Lines)
            {
                switch (line.Type)
                {
                    case ChangeType.Inserted:
                        result.AppendLine($"+ {line.Text}");
                        break;
                    case ChangeType.Deleted:
                        result.AppendLine($"- {line.Text}");
                        break;
                    case ChangeType.Modified:
                        result.AppendLine($"~ {line.Text}");
                        break;
                    case ChangeType.Unchanged:
                        result.AppendLine($"  {line.Text}");
                        break;
                }
            }

            return result.ToString();
        }

        // Mapping zurückgeben (für De-Anonymisierung)
        public static Dictionary<string, string> GetReverseMapping()
        {
            return _mapping.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
        }

        // Ordner-Mapping-Datei setzen und laden (beim Öffnen eines Ordners aufrufen)
        public static void SetMappingFile(string folderPath)
        {
            _mappingFilePath = Path.Combine(folderPath, ".lexwolf_mapping.json");
            _mapping.Clear();
            _entityTypes.Clear();
            _personIdx = _addrIdx = _datumIdx = _betragIdx = _ibanIdx = _emailIdx = 0;
            _counter = 1;
            LoadMappingFromFile();
        }

        // Mapping aus JSON-Datei laden und Zähler wiederherstellen
        private static void LoadMappingFromFile()
        {
            if (!File.Exists(_mappingFilePath)) return;
            try
            {
                var json = File.ReadAllText(_mappingFilePath);
                var data = JsonSerializer.Deserialize<MappingFile>(json);
                if (data?.Entries == null) return;

                foreach (var e in data.Entries)
                {
                    _mapping[e.Original] = e.Alias;
                    _entityTypes[e.Original] = e.Type;
                    switch (e.Type)
                    {
                        case EntityType.Person:       _personIdx++;  break;
                        case EntityType.Betrag:       _betragIdx++;  break;
                        case EntityType.Datum:        _datumIdx++;   break;
                        case EntityType.Adresse:      _addrIdx++;    break;
                        case EntityType.Konto:        _ibanIdx++;    break;
                        case EntityType.Email:        _emailIdx++;   break;
                        case EntityType.Telefon:      _counter++;    break;
                        case EntityType.Aktenzeichen: _counter++;    break;
                    }
                }
            }
            catch { }
        }

        // Mapping in JSON-Datei speichern (nach jeder Anonymisierung aufrufen)
        public static void SaveMapping()
        {
            if (string.IsNullOrEmpty(_mappingFilePath)) return;
            try
            {
                var entries = _mapping.Select(kvp => new MappingEntry
                {
                    Original = kvp.Key,
                    Alias    = kvp.Value,
                    Type     = _entityTypes.TryGetValue(kvp.Key, out var t) ? t : EntityType.Person
                }).ToList();

                var data = new MappingFile { Entries = entries };
                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_mappingFilePath, JsonSerializer.Serialize(data, opts));
            }
            catch { }
        }

        private sealed class MappingEntry
        {
            public string     Original { get; set; } = string.Empty;
            public string     Alias    { get; set; } = string.Empty;
            public EntityType Type     { get; set; }
        }

        private sealed class MappingFile
        {
            public List<MappingEntry>? Entries { get; set; }
        }
    }

    // Hilfsklasse für File-System-Watcher
    public class DocumentScanner
    {
        private readonly FileSystemWatcher _watcher;
        private readonly Action<string> _onFileDetected;

        public DocumentScanner(string path, Action<string> onFileDetected)
        {
            _onFileDetected = onFileDetected;
            _watcher = new FileSystemWatcher(path)
            {
                Filter = "*.*",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = true
            };

            _watcher.Created += OnFileCreated;
            _watcher.Changed += OnFileChanged;
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            _onFileDetected(e.FullPath);
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // Verhindere doppelte Erkennung nach erstem Created-Ereignis
            Task.Delay(500).ContinueWith(_ => _onFileDetected(e.FullPath));
        }

        public void Start() => _watcher.EnableRaisingEvents = true;
        public void Stop() => _watcher.EnableRaisingEvents = false;
    }
}
