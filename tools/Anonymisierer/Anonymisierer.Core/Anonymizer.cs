using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

    // NER-Response von FastAPI /ner
    internal sealed class NerEntity
    {
        public string text  { get; set; } = string.Empty;
        public string label { get; set; } = string.Empty;
        public int    start { get; set; }
        public int    end   { get; set; }
    }
    internal sealed class NerResponse
    {
        public List<NerEntity> entities { get; set; } = new();
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

        // NER-Endpoint konfigurierbar; Standard ist der lokale LexWolf-Backend-Port
        public static string NerEndpoint { get; set; } = "http://localhost:8000/ner";
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

        // Logging-Callback für Validierungsergebnisse
        private static Action<string>? _logCallback;
        public static void SetLogCallback(Action<string> callback) => _logCallback = callback;

        private static void Log(string message)
        {
            _logCallback?.Invoke(message);
        }

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

        // Pass 1: Name nach Anrede – "Frau Ruck", "Herrn Ali Izzet Erkol", "Dr. Schapmann"
        private static readonly System.Text.RegularExpressions.Regex _rxPersonSalutation =
            new(@"(?:Herr(?:n|in)?|Frau(?:en)?|Dr\.|Prof\.(?:in)?|Dipl\.[-\w]*\.)\s+([A-ZÄÖÜ][a-zäöüß]{1,20}(?:[^\S\r\n]+(?:[A-ZÄÖÜ]\.[^\S\r\n]*)?[A-ZÄÖÜ][a-zäöüß]{1,20}){0,2})",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex _rxDatum =
            new(@"\b(\d{1,2}\.\s*\d{1,2}\.\s*\d{4}|\d{1,2}\.\s*(?:Januar|Februar|März|April|Mai|Juni|Juli|August|September|Oktober|November|Dezember)\s+\d{4})\b",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Erfasst sowohl zusammengeschriebene ("Musterstraße 12") als auch zweiteilige
        // Straßennamen ("Unterländer Straße 29-31"), inkl. Hausnummern-Bereichen ("29-31").
        private static readonly System.Text.RegularExpressions.Regex _rxAdresse =
            new(@"\b([A-ZÄÖÜ][a-zäöüß]+(?:straße|strasse|str\.|weg|gasse|platz|allee|ring|damm|pfad|ufer)|[A-ZÄÖÜ][a-zäöüß]+[^\S\r\n]+(?:straße|strasse|str\.))[^\S\r\n]*\d{1,3}(?:[-\/]\d{1,3})?\s*[a-z]?\b",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly System.Text.RegularExpressions.Regex _rxPlz =
            new(@"\b(\d{5})[^\S\r\n]+([A-ZÄÖÜ][a-zäöüß]+)\b",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // Formular-Layouts (z.B. Steuererklaerung) trennen Feldbezeichnung und Wert oft auf
        // zwei Zeilen ("Straße (derzeitige Adresse)\nWallensteinstr." bzw. "Wohnort\nStuttgart"),
        // wodurch die obigen Adress-/PLZ-Regexe (die Wert-Adjazenz auf derselben Zeile erwarten)
        // keinen Treffer liefern. Diese Regexe sind bewusst eng an die konkreten Feldbezeichnungen
        // gebunden, um keine Falsch-Positive in Fließtext zu erzeugen.
        private static readonly System.Text.RegularExpressions.Regex _rxStrasseLabelZeile =
            new(@"Stra(?:ß|ss)e[^\S\r\n]*\([^)\r\n]*\)[^\S\r\n]*\r?\n[^\S\r\n]*(\S[^\r\n]*?)[^\S\r\n]*(?=\r?\n|$)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex _rxWohnortLabelZeile =
            new(@"\bWohnort[^\S\r\n]*\r?\n[^\S\r\n]*([A-ZÄÖÜ][A-Za-zÄÖÜäöüß\-]{2,40})\b",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly string[] CityPool =
        {
            "Entenhausen", "Gotham City", "Hogsmeade", "Bree", "Mos Eisley",
            "Springfield", "Smallville", "Meereen", "Winterfell", "Auenland"
        };
        private static int _cityIdx;

        private static readonly System.Text.RegularExpressions.Regex _rxTelefon =
            new(@"\b((?:\+49|0049|0)\d[\d\-\/]{6,13})\b",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex _rxIban =
            new(@"\b([A-Z]{2}\d{2}(?:[^\S\r\n]?\d{4}){4}(?:[^\S\r\n]?\d{1,6})?)\b",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex _rxBetrag =
            new(@"(?<!\d)(\d{1,3}(?:\.\d{3})*,\d{2})(?!\d)|\b(\d{1,3}(?:\.\d{3})*)(?=\s*(?:EUR\b|€))",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex _rxSteuernummer =
            new(@"\b\d{2,5}\/\d{4,6}\b",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex _rxUstIdNr =
            new(@"\bDE\d{9}\b",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly string[] _fakeSteuernummern =
        {
            "[STEUER-1]", "[STEUER-2]", "[STEUER-3]", "[STEUER-4]",
            "[STEUER-5]", "[STEUER-6]", "[STEUER-7]", "[STEUER-8]"
        };
        private static readonly string[] _fakeUstIdNr =
        {
            "[UST-1]", "[UST-2]", "[UST-3]", "[UST-4]",
            "[UST-5]", "[UST-6]", "[UST-7]", "[UST-8]"
        };
        private static int _steuerIdx;
        private static int _ustIdx;

        private static readonly System.Text.RegularExpressions.Regex _rxAktenzeichen =
            new(@"\b(Az\.?\s*[\w\d]{1,6}[\s\/\-][\w\d\/\-]+)\b",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly System.Text.RegularExpressions.Regex _rxEmail =
            new(@"\b[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}\b",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        public static async Task<string> AnonymizeTextAsync(string text, List<Entity> entities)
        {
            string result = text;

            result = ReplaceWithAlias(result, _rxSteuernummer, EntityType.Aktenzeichen, () => _fakeSteuernummern[_steuerIdx++ % _fakeSteuernummern.Length], entities);
            result = ReplaceWithAlias(result, _rxUstIdNr,       EntityType.Aktenzeichen, () => _fakeUstIdNr[_ustIdx++ % _fakeUstIdNr.Length],               entities);
            result = ReplaceWithAlias(result, _rxIban,         EntityType.Konto,        () => _fakeIbans[_ibanIdx++ % _fakeIbans.Length],         entities);
            result = ReplaceWithAlias(result, _rxAktenzeichen, EntityType.Aktenzeichen, () => $"[AZ-{_counter++}]",                                       entities);
            result = ReplaceWithAlias(result, _rxTelefon,      EntityType.Telefon,      () => $"[TEL-{_counter++}]",                                      entities);
            result = ReplaceWithAlias(result, _rxEmail,        EntityType.Email,        () => _fakeEmails[_emailIdx++ % _fakeEmails.Length],               entities);
            result = ReplaceWithAlias(result, _rxBetrag,       EntityType.Betrag,       () => _fakeBetraege[_betragIdx++ % _fakeBetraege.Length],          entities);
            result = ReplaceWithAlias(result, _rxDatum,        EntityType.Datum,        () => _fakeDaten[_datumIdx++ % _fakeDaten.Length],                 entities);
            result = ReplaceWithAlias(result, _rxAdresse,      EntityType.Adresse,      () => $"[{AdressPool[_addrIdx++ % AdressPool.Length]}]",           entities);
            result = ReplaceWithAlias(result, _rxPlz,          EntityType.Adresse,      () => $"[ORT-{_counter++}]",                                      entities);
            result = ReplaceStrasseLabelZeile(result, entities);
            result = ReplaceWohnortLabelZeile(result, entities);
            result = await ReplacePersonsAsync(result, entities);

            return result;
        }

        // Synchronous wrapper kept for compatibility (blocks async)
        public static string AnonymizeText(string text, out List<Entity> entities)
        {
            var list = new List<Entity>();
            var result = AnonymizeTextAsync(text, list).GetAwaiter().GetResult();
            entities = list;
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

        // Ersetzt den Wert hinter dem Feld-Label "Straße (...)" auf der Folgezeile
        // (z.B. Formulare der Steuererklaerung, die Label und Wert auf getrennte Zeilen setzen).
        private static string ReplaceStrasseLabelZeile(string text, List<Entity> entities)
        {
            return _rxStrasseLabelZeile.Replace(text, match =>
            {
                var original = match.Groups[1].Value;
                if (original.Length < 2) return match.Value;
                string alias;
                if (_mapping.TryGetValue(original, out var existing))
                {
                    alias = existing;
                }
                else
                {
                    alias = $"[{AdressPool[_addrIdx++ % AdressPool.Length]}]";
                    _mapping[original] = alias;
                    _entityTypes[original] = EntityType.Adresse;
                }
                if (!entities.Any(e => e.Text == original))
                    entities.Add(new Entity { Text = original, Type = EntityType.Adresse, AnonymizedText = alias });
                int prefixLen = match.Groups[1].Index - match.Index;
                return match.Value[..prefixLen] + alias;
            });
        }

        // Ersetzt den Wert hinter dem Feld-Label "Wohnort" auf der Folgezeile.
        private static string ReplaceWohnortLabelZeile(string text, List<Entity> entities)
        {
            return _rxWohnortLabelZeile.Replace(text, match =>
            {
                var original = match.Groups[1].Value;
                string alias;
                if (_mapping.TryGetValue(original, out var existing))
                {
                    alias = existing;
                }
                else
                {
                    alias = $"[{CityPool[_cityIdx++ % CityPool.Length]}]";
                    _mapping[original] = alias;
                    _entityTypes[original] = EntityType.Adresse;
                }
                if (!entities.Any(e => e.Text == original))
                    entities.Add(new Entity { Text = original, Type = EntityType.Adresse, AnonymizedText = alias });
                int prefixLen = match.Groups[1].Index - match.Index;
                return match.Value[..prefixLen] + alias;
            });
        }

        // Wendet alle bereits bekannten Personen- UND Adress-Mappings (aus diesem oder vorherigen
        // Dokumenten desselben Batch-Laufs) per Volltext-Ersetzung an. Faengt Faelle ab, in denen
        // eine Entitaet (z.B. "Stuttgart") in einem Dokument nur ueber ein Formular-Label erkannt
        // wird, in einem anderen Dokument desselben Mandanten aber "nackt" (ohne Label/PLZ-Kontext)
        // auftaucht, z.B. als Absender-/Unterschriftsort oder in Fusszeilen-Boilerplate.
        public static string ApplyKnownMappings(string text, List<Entity> entities)
        {
            var relevantEntries = _mapping
                .Where(kvp => _entityTypes.TryGetValue(kvp.Key, out var t) && (t == EntityType.Person || t == EntityType.Adresse))
                .OrderByDescending(kvp => kvp.Key.Length)
                .ToList();

            foreach (var (orig, alias) in relevantEntries)
            {
                if (!text.Contains(orig)) continue;
                text = text.Replace(orig, alias);
                if (!entities.Any(e => e.Text == orig))
                {
                    var type = _entityTypes.TryGetValue(orig, out var t) ? t : EntityType.Person;
                    entities.Add(new Entity { Text = orig, Type = type, AnonymizedText = alias });
                }
            }
            return text;
        }

        private static async Task<string> ReplacePersonsAsync(string text, List<Entity> entities)
        {
            // Pass 1: Salutation-basiert (Frau/Herr/Dr./Prof.) — bleibt unverändert, sehr zuverlässig
            text = _rxPersonSalutation.Replace(text, match =>
            {
                var original = match.Groups[1].Value;
                string alias;
                if (_mapping.TryGetValue(original, out var existing))
                {
                    alias = existing;
                    if (!entities.Any(e => e.Text == original))
                        entities.Add(new Entity { Text = original, Type = EntityType.Person, AnonymizedText = alias });
                }
                else
                {
                    alias = $"[{PersonPool[_personIdx++ % PersonPool.Length]}]";
                    StorePersonAlias(original, alias);
                    entities.Add(new Entity { Text = original, Type = EntityType.Person, AnonymizedText = alias });
                }
                int prefixLen = match.Groups[1].Index - match.Index;
                return match.Value[..prefixLen] + alias;
            });

            // Pre-Pass: bekannte Personen/Adressen aus Mapping direkt ersetzen (längste zuerst → kein Doppelersatz)
            text = ApplyKnownMappings(text, entities);

            // Pass 2: spaCy NER — nur noch unbekannte Namen werden gefunden
            text = await ReplaceViaSpacyNer(text, entities);

            return text;
        }

        // Ruft POST /ner auf, ersetzt neue PER-Entitäten und speichert Alias + Teilnamen im Mapping
        private static async Task<string> ReplaceViaSpacyNer(string text, List<Entity> entities)
        {
            NerResponse? nerResult = null;
            try
            {
                var payload = JsonSerializer.Serialize(new { text });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync(NerEndpoint, content);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    nerResult = JsonSerializer.Deserialize<NerResponse>(json);
                }
            }
            catch { /* NER nicht verfügbar → nur Salutation-Pass greift */ }

            if (nerResult == null || nerResult.entities.Count == 0) return text;

            // Entitäten von hinten nach vorne ersetzen, damit Offsets gültig bleiben
            // (Text wurde durch Pre-Pass ggf. bereits verändert → neu suchen per Contains)
            foreach (var ent in nerResult.entities.OrderByDescending(e => e.start))
            {
                // Entität auf die erste Zeile begrenzen (spaCy verbindet manchmal Name+Adresse)
                var name = ent.text.Contains('\n') ? ent.text[..ent.text.IndexOf('\n')].Trim() : ent.text.Trim();
                if (name.Length < 2) continue;
                if (!text.Contains(name)) continue;          // bereits durch Pre-Pass ersetzt
                if (_mapping.ContainsKey(name)) continue;    // Race-condition-Schutz

                var alias = $"[{PersonPool[_personIdx++ % PersonPool.Length]}]";
                StorePersonAlias(name, alias);
                entities.Add(new Entity { Text = name, Type = EntityType.Person, AnonymizedText = alias });
                text = text.Replace(name, alias);
            }
            return text;
        }

        // Speichert den vollen Namen UND den letzten Token (Nachname) als separate Mapping-Einträge
        private static void StorePersonAlias(string fullName, string alias)
        {
            _mapping[fullName] = alias;
            _entityTypes[fullName] = EntityType.Person;

            // Letzten Token als Nachname separat mappen (≥4 Zeichen, kein Alias-Muster)
            var lastName = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
            if (lastName.Length >= 4 && !_mapping.ContainsKey(lastName))
            {
                _mapping[lastName] = alias;
                _entityTypes[lastName] = EntityType.Person;
            }
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

        // ODT-Dokument (OpenDocument Text) öffnen und Inhalt extrahieren
        // ODT ist ein ZIP-Archiv mit content.xml; text:p und text:span Knoten als Plaintext extrahieren
        public static string ReadOdtDocument(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Datei nicht gefunden: {filePath}", filePath);

            var sb = new StringBuilder();
            using (var zip = System.IO.Compression.ZipFile.OpenRead(filePath))
            {
                var entry = zip.GetEntry("content.xml");
                if (entry == null)
                    throw new InvalidDataException($"ODT-Datei enthält keine content.xml: {filePath}");

                using var stream = entry.Open();
                var doc = System.Xml.Linq.XDocument.Load(stream);

                // Alle text:p und text:span Knoten durchlaufen
                foreach (var el in doc.Descendants())
                {
                    var local = el.Name.LocalName;
                    if (local == "p" || local == "h" || local == "span")
                    {
                        // Textinhalt extrahieren (inklusive aller Unterelemente)
                        var text = el.Value?.Trim();
                        if (!string.IsNullOrEmpty(text))
                        {
                            sb.AppendLine(text);
                        }
                    }
                }
            }

            return sb.ToString();
        }

        // RTF-Dokument öffnen und in lesbaren Plaintext umwandeln
        // RTF kodiert Nicht-ASCII-Zeichen als Hex-Escapes in Windows-1252 (\'e4 = ä, \'f6 = ö, \'fc = ü, ...);
        // zusätzlich müssen RTF-Steuer-Sequenzen (\par, \b, \fs24, ...) entfernt werden
        public static string ReadRtfDocument(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Datei nicht gefunden: {filePath}", filePath);

            var raw = File.ReadAllBytes(filePath);
            var text = Encoding.Latin1.GetString(raw); // Latin-1 bildet Bytes 1:1 auf Codepoints ab (deckt cp1252-Umlautbereich 0xA0–0xFF ab)

            // Hex-Escapes \'e4, \'f6, \'fc, ... in das jeweilige Zeichen auflösen
            text = Regex.Replace(text, @"\\'([0-9a-fA-F]{2})",
                m => Encoding.Latin1.GetString(new[] { Convert.ToByte(m.Groups[1].Value, 16) }));

            // Absatz-/Zeilenumbrüche in echte Zeilenumbrüche wandeln
            text = text.Replace("\\par", "\n").Replace("\\line", "\n");

            // Ignorierte Gruppen (z.B. Font-/Farbtabellen, \*-Kontrollworte) entfernen
            text = Regex.Replace(text, @"\{\\\*.*?\}", string.Empty, RegexOptions.Singleline);

            // Übrige RTF-Steuer-Sequenzen (\wortNNN) entfernen
            text = Regex.Replace(text, @"\\[a-zA-Z]+-?\d*\s?", " ");

            // Geschweifte Klammern (Gruppen-Grenzen) entfernen
            return Regex.Replace(text, @"[{}]", string.Empty);
        }

        // Liest ein Dokument anhand seiner Dateiendung mit dem passenden Reader
        public static string ReadDocument(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            switch (extension)
            {
                case ".docx":
                    return ReadWordDocument(filePath);
                case ".pdf":
                    return ReadPdfDocument(filePath);
                case ".eml":
                    return ReadEmlDocument(filePath);
                case ".odt":
                    return ReadOdtDocument(filePath);
                case ".rtf":
                    return ReadRtfDocument(filePath);
                case ".txt":
                    return File.ReadAllText(filePath);
                default:
                    throw new NotSupportedException($"Dateityp nicht unterstützt: {extension}");
            }
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
        // StorePersonAlias trägt für Vollname UND Nachname denselben Alias ein, daher ist
        // _mapping nicht eindeutig invertierbar (ToDictionary würde bei doppeltem Alias-Key
        // eine ArgumentException werfen). Bei Kollision gewinnt der längere Original-Wert
        // (Vollname vor Nachname), damit die De-Anonymisierung den vollständigeren Text liefert.
        public static Dictionary<string, string> GetReverseMapping()
        {
            var reverse = new Dictionary<string, string>();
            foreach (var kvp in _mapping.OrderByDescending(kvp => kvp.Key.Length))
            {
                if (!reverse.ContainsKey(kvp.Value))
                    reverse[kvp.Value] = kvp.Key;
            }
            return reverse;
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

        // Mapping aus JSON-Datei laden und Person-Einträge per NER validieren
        // Falsch-positive Einträge (spaCy erkennt nicht als PER) werden entfernt + geloggt
        // Fallback: Wenn NER nicht erreichbar, werden alle Einträge behalten
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

                // Nach dem Laden: Person-Einträge per NER validieren
                var personEntries = _mapping
                    .Where(kvp => _entityTypes.TryGetValue(kvp.Key, out var t) && t == EntityType.Person)
                    .ToList();

                if (personEntries.Count > 0)
                {
                    var allText = string.Join(" ", personEntries.Select(kvp => kvp.Key));

                    NerResponse? nerResult = null;
                    try
                    {
                        var payload = JsonSerializer.Serialize(new { text = allText });
                        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                        using var resp = _http.PostAsync(NerEndpoint, content).GetAwaiter().GetResult();
                        if (resp.IsSuccessStatusCode)
                        {
                            var jsonResp = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            nerResult = JsonSerializer.Deserialize<NerResponse>(jsonResp);
                        }
                    }
                    catch
                    {
                        Log("NER endpoint not reachable — keeping all person entries from mapping");
                        return;
                    }

                    if (nerResult == null || nerResult.entities.Count == 0)
                    {
                        Log("NER returned no entities — keeping all person entries from mapping");
                        return;
                    }

                    var recognizedPersonNames = new HashSet<string>(
                        nerResult.entities
                            .Where(e => e.label == "PER")
                            .Select(e => e.text.Trim())
                            .Where(s => !string.IsNullOrEmpty(s)),
                        StringComparer.OrdinalIgnoreCase);

                    var toRemove = new List<string>();
                    foreach (var (orig, alias) in personEntries)
                    {
                        if (!recognizedPersonNames.Contains(orig))
                        {
                            toRemove.Add(orig);
                        }
                    }

                    foreach (var orig in toRemove)
                    {
                        _mapping.Remove(orig);
                        _entityTypes.Remove(orig);
                        Log($"Removed invalid person entry '{orig}' from mapping — NER did not recognize it as PER");
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
