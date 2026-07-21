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

        // Zweite, normalisierte Sicht auf _mapping: fängt Fälle ab, in denen dieselbe reale
        // Entität (z.B. eine Adresse) in unterschiedlichen Dokumentformaten (ODT vs. RTF) mit
        // leicht abweichendem Whitespace extrahiert wird und sonst zwei verschiedene Aliase
        // bekäme. _mapping selbst behält seine Original-String-Keys (wird u.a. von
        // ApplyKnownMappings per Volltext-Contains/Replace durchsucht) — diese Dictionary dient
        // nur als Fallback-Lookup für die Alias-Wiederverwendung.
        private static readonly Dictionary<string, string> _mappingByNormalizedKey = new();

        // Menge aller jemals vergebenen Alias-Werte (z.B. "[Dr. Watson]", "[STEUER-5]", die
        // Fake-IBAN "DE89 ..."). Wird genutzt, um bereits anonymisierten Text vor erneuten
        // Durchläufen zu schützen (siehe ProtectKnownAliases) — verhindert Alias-Verkettung
        // (Task #202: Erkol -> Dr. Watson -> Prof. Moriarty -> Verleihnix -> Robin).
        private static readonly HashSet<string> _knownAliasValues = new();
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

        // Generiert fuer jede Nummer eine eindeutige Datum-Variation, falls der statische
        // _fakeDaten-Pool erschoepft ist (Task #205). Der 9. und jede weitere Datumserkennung
        // erhaelt z.B. "11.03.2001", "11.03.2002" etc. statt erneut "15.06.1985".
        private static string GenerateUniqueDatum(int idx)
        {
            var baseDate = _fakeDaten[idx % _fakeDaten.Length];
            if (idx < _fakeDaten.Length) return baseDate;

            if (DateTime.TryParseExact(baseDate, "dd.MM.yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d))
            {
                var shifted = d.AddDays(idx / _fakeDaten.Length);
                return shifted.ToString("dd.MM.yyyy");
            }
            return $"[{baseDate}-{idx}]";
        }

        // Generiert fuer jede Nummer einen eindeutigen Betrags-Alias, falls der statische
        // _fakeBetraege-Pool erschoepft ist (Task #205). Der 9. und jede weitere
        // Betragserkennung erhaelt z.B. "1.250,01", "875,51" etc. statt erneut "1.250,00".
        private static string GenerateUniqueBetrag(int idx)
        {
            var baseValue = _fakeBetraege[idx % _fakeBetraege.Length];
            if (idx < _fakeBetraege.Length) return baseValue;

            // "1.250,00" -> 125000 Cent, addiere 1 Cent pro Exemplar ueber den Pool hinaus
            var clean = baseValue.Replace(".", "").Replace(",", ".");
            if (decimal.TryParse(clean, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var amount))
            {
                var unique = amount + (idx / _fakeBetraege.Length) * 0.01m;
                // Manuelle DE-Formatierung statt CultureInfo.GetCultureInfo("de-DE"),
                // da die Anonymisierer.Cli mit InvariantGlobalization laeuft (keine ICU-Kulturdaten verfuegbar).
                var invariantStr = unique.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
                return invariantStr.Replace(",", "\0").Replace(".", ",").Replace("\0", ".");
            }
            return $"[{baseValue}-{idx}]";
        }

        // Generiert fuer jede Nummer eine eindeutige Adress-Alias-Variation, falls der statische
        // AdressPool erschoepft ist (Task #205). Der 19. und jede weitere Adress-Erkennung
        // erhaelt z.B. "Privet Drive 4 (Haus 2)", "Baker Street 221b (Haus 2)" etc.
        private static string GenerateUniqueAdresse(int idx)
        {
            var baseAddr = AdressPool[idx % AdressPool.Length];
            if (idx < AdressPool.Length) return $"[{baseAddr}]";
            return $"[{baseAddr} (Haus {idx / AdressPool.Length + 1})]";
        }

        // Generiert fuer jede Nummer eine eindeutige Stadt-Alias-Variation, falls der statische
        // CityPool erschoepft ist (Task #205).
        private static string GenerateUniqueCity(int idx)
        {
            var baseCity = CityPool[idx % CityPool.Length];
            if (idx < CityPool.Length) return $"[{baseCity}]";
            return $"[{baseCity} {idx / CityPool.Length + 1}]";
        }

        // --- STRUKTURELLE FILTERUNG VON FALSE-POSITIVE PERSONEN-ERKENNUNGEN (Task #207) ---
        //
        // Beobachtung aus Task #204/#207: spaCy NER erkennt im juristischen Kontext haefig
        // generische deutsche Substantive/Komposita als PER (Type 1), z.B.:
        //   "Bescheinigt", "Mieters", "Wirtschaftsjahrs", "Waermecontracting",
        //   "Betriebskostenpauschale", "Bruttoarbeitslohn", "Hauptvordruck", ...
        // Eine einfache Stopwortliste (Task #204) bekaempft dieses Symptom nur fuer bekannte
        // Woerter (Whack-a-Mole). Wir ergaenzen deshalb ein strukturelles Kriterium:
        //
        // 1. Suffix-Regel: Das Wort endet auf ein typisches deutsches Substantiv-/Abstraktum-
        //    Suffix, das bei Eigennamen extrem selten vorkommt (z.B. -jahrs, -jahr, -jahre,
        //    -kosten, -pauschale, -angaben, -lohn, -gehalt, -contracting, -geraete, -art,
        //    -druck, -vordruck, -behandlung, -erklaerung, -rechnung, -vertrag).
        // 2. Praefix-Regel: Das Wort beginnt mit einem typischen Fachbegriffs-Praefix, das bei
        //    Personennamen quasi nie auftritt (Wirtschafts-, Waerme-, Betriebs-, Arbeits-,
        //    Brutto-, Haushalts-, Elektro-, Haupt-).
        // 3. Ein Kompositum muss mindestens EINE der beiden Regeln erfuellen (Suffix ODER
        //    Praefix), damit es als generisches Substantiv abgelehnt wird.
        //
        // Echte Personennamen (Erkol, Schapmann, Dilara, Maysa, Samir, Melisa, Ruck) erfuellen
        // diese Muster in der Regel nicht. Ausnahmen wie "Mayer" (Endung -ayer) oder "Pahl"
        // (Endung -ahl) werden absichtlich NICHT getroffen, weil sie die harten Suffixe nicht
        // besitzen und keine Fachbegriffs-Praefixe tragen.
        private static readonly string[] _germanGenericSubstantivSuffixes = new[]
        {
            "jahrs", "jahr", "jahre", "jahres",
            "kosten", "kostens",
            "pauschale", "pauschalen", "pauschals",
            "angaben", "angabe",
            "lohn", "lohns", "gehalt", "gehalts", "verdienst", "einkommen",
            "contracting",
            "geraete", "geraets", "gerät", "geräte",
            "art", "arten",
            "druck", "drucks", "vordruck", "vordrucks",
            "behandlung", "behandlungen",
            "erklaerung", "erklaerungen", "erklärung", "erklärungen",
            "rechnung", "rechnungen",
            "vertrag", "vertrags", "vertraege",
            "abrechnung", "abrechnungen",
            "gemeinschaft", "gemeinschaften",
            "versicherung", "versicherungen",
            "kasse", "kassen",
            "antrag", "antrags", "antraege",
            "bescheid", "bescheide", "bescheids",
            "konto", "kontos", "konten",
            "geld", "geldes",
            "schein", "scheins",
            "zins", "zinsen",
            "miete", "mieten",
            "nebenkosten", "heizkosten",
            "steuer", "steuern",
            "frist", "fristen",
            "lage", "lagen",
            "leistung", "leistungen",
            "vollmacht", "vollmachten",
            "zulage", "zulagen"
        };

        private static readonly string[] _germanGenericTechnicalPrefixes = new[]
        {
            "Wirtschafts", "Waerme", "Betriebs", "Arbeits", "Brutto",
            "Haushalts", "Elektro", "Haupt", "Neben", "Veranlagungs",
            "Steuer", "Miet", "Gehalts", "Lohn", "Sozial",
            "Kranken", "Renten", "Pflege", "Unfall",
            "Rechts", "Verwaltungs", "Versicherungs"
        };

        // Bindestrich-Komposita (z.B. "Wirtschaftsjahrs" oder "abzurechnenden Wirtschaftsjahrs")
        // und grosse Substantive mit Fachbegriffs-Praefix/-Suffix werden als generisches
        // Substantiv erkannt und aus der Personen-Erkennung ausgeschlossen. Das ist ein
        // generalisierendes Kriterium und keine Aufzaehlung einzelner falsch erkannter Woerter.
        private static bool IsGenericGermanSubstantivComposite(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            var clean = text.Trim('[', ']').Trim();
            if (clean.Length < 4) return false;

            // 1. Suffix-Pruefung (am längsten passenden Suffix testen, damit "Wirtschaftsjahrs"
            //    nicht nur als "jahrs", sondern als Ganzes betrachtet wird).
            foreach (var suffix in _germanGenericSubstantivSuffixes)
            {
                if (clean.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // 2. Praefix-Pruefung an Bindestrich- oder zusammengeschriebenem Kompositum.
            foreach (var prefix in _germanGenericTechnicalPrefixes)
            {
                if (clean.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
                var lower = clean.ToLowerInvariant();
                if (lower.Contains("-" + prefix.ToLowerInvariant()) ||
                    lower.Contains(prefix.ToLowerInvariant() + "-"))
                    return true;
            }

            return false;
        }

        // Entfernt eine fuehrende Anrede (z.B. "Frau ", "Herrn ") von einem Kandidatentext.
        // Der spaCy-NER-Pass liefert Anrede + Name haeufig als EINEN zusammenhaengenden
        // PER-Span (z.B. "Frau Auenland-Weg"), waehrend der bekannte Adress-Alias selbst
        // keine Anrede enthaelt (Task #208 Nachbesserung).
        private static readonly System.Text.RegularExpressions.Regex _rxLeadingSalutation =
            new(@"^(?:Herr(?:n|in)?|Frau(?:en)?|Dr\.|Prof\.(?:in)?|Dipl\.[-\w]*\.)\s+",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // Entfernt einen abschliessenden "(Haus N)"-Variationszusatz sowie das letzte
        // Leerzeichen-getrennte Token, falls es eine Ziffer enthaelt (Hausnummer, z.B.
        // "221b", "3", "9¾"). So wird aus einem Adress-Alias wie "Auenland-Weg 3" oder
        // "Baker Street 221b (Haus 2)" der reine Strassenname "Auenland-Weg"/"Baker Street"
        // fuer den Vergleich gewonnen (Task #208 Nachbesserung).
        private static readonly System.Text.RegularExpressions.Regex _rxTrailingHausVariant =
            new(@"\s*\(Haus\s*\d+\)\s*$",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        private static readonly System.Text.RegularExpressions.Regex _rxTrailingNumericToken =
            new(@"\s+\S*\d\S*$", System.Text.RegularExpressions.RegexOptions.Compiled);

        // Prueft, ob 'text' identisch mit einem bereits vergebenen Alias-Wert ist oder
        // ein Teilstring/davon-Teiltoken davon (z.B. "Baker Street" innerhalb von
        // "[Baker Street 221b]", "Entenhausen" innerhalb von "[Entenhausen]", "ORT-4"
        // innerhalb von "[ORT-4]", "STEUER-3" innerhalb von "[STEUER-3]").
        // Damit werden bereits erzeugte Alias-/Platzhalterwerte NICHT erneut als Personen
        // (oder anderer Typ) erkannt, wenn sie (mit oder ohne Klammern) spaeter im selben
        // Batch wieder auftauchen (Task #208: Selbstkontamination).
        private static bool IsKnownAliasOrFragment(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var clean = text.Trim('[', ']').Trim();
            if (clean.Length < 2) return false;

            // Variante ohne fuehrende Anrede, falls der NER-Span Anrede + Name als ein
            // Stueck geliefert hat (z.B. "Frau Auenland-Weg" -> "Auenland-Weg").
            var cleanNoSalutation = _rxLeadingSalutation.Replace(clean, "").Trim();
            if (cleanNoSalutation.Length < 2) cleanNoSalutation = clean;

            foreach (var alias in _knownAliasValues)
            {
                if (string.IsNullOrWhiteSpace(alias)) continue;
                var aliasClean = alias.Trim('[', ']').Trim();
                if (aliasClean.Length < 2) continue;

                // Alias-Kern ohne Hausnummer/Variationszusatz (z.B. "Auenland-Weg 3" ->
                // "Auenland-Weg"), damit ein spaeteres Wiederauftauchen ohne Hausnummer
                // (z.B. in Anrede-Kontext "Frau Auenland-Weg") ebenfalls erkannt wird.
                var aliasCore = _rxTrailingHausVariant.Replace(aliasClean, "");
                aliasCore = _rxTrailingNumericToken.Replace(aliasCore, "").Trim();
                if (aliasCore.Length < 2) aliasCore = aliasClean;

                foreach (var candidate in new[] { clean, cleanNoSalutation })
                {
                    // Exakte Uebereinstimmung (mit/ohne Klammern)
                    if (candidate.Equals(aliasClean, StringComparison.OrdinalIgnoreCase)) return true;

                    // 'candidate' ist Teilstring des Alias (z.B. "Baker Street" in "Baker Street 221b")
                    if (aliasClean.Contains(candidate, StringComparison.OrdinalIgnoreCase)) return true;

                    // Der Alias ist Teilstring von 'candidate' (z.B. "ORT-4" in "Mein ORT-4 Text")
                    if (candidate.Contains(aliasClean, StringComparison.OrdinalIgnoreCase)) return true;

                    // Vergleich gegen den Alias-Kern ohne Hausnummer (z.B. "Auenland-Weg"
                    // in "Frau Auenland-Weg" trotz fehlender Hausnummer "3" im Original-Alias)
                    if (candidate.Equals(aliasCore, StringComparison.OrdinalIgnoreCase)) return true;
                    if (aliasCore.Contains(candidate, StringComparison.OrdinalIgnoreCase)) return true;
                    if (candidate.Contains(aliasCore, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }

            return false;
        }

        // Deutscher Wortschatz als Frequenzliste (ca. 20.000 häufigste Wörter), geladen
        // aus einer Embedded Resource. Dient als negatives Kriterium: Ein von spaCy als PER
        // geliefertes Token, das in dieser Liste steht UND strukturelle Merkmale eines
        // generischen Substantivs trägt (typisches Suffix/Präfix oder Top-100-Häufigkeit),
        // wird als False Positive abgelehnt. Echte Personennamen (Erkol, Schapmann, ...)
        // stehen nicht in der Liste und bleiben erhalten.
        private static readonly HashSet<string> _germanWordList = LoadGermanWordList();

        // Echte Personennamen, die zufällig auch im deutschen Wortschatz vorkommen
        // (z.B. "Samir", "Ruck" in der Frequenzliste), dürfen dadurch nicht ausgeschlossen
        // werden. Die Wortliste greift deshalb nur in Kombination mit einem der harten
        // strukturellen Kriterien (Substantiv-Suffix, Fachbegriffs-Präfix oder
        // Top-100-Häufigkeit).
        private static HashSet<string> LoadGermanWordList()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var assembly = typeof(Anonymizer).Assembly;
                var resourceName = "Anonymisierer.Data.de_words_20k.txt";
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    // Fallback: versuche den Dateinamen ohne expliziten Namespace
                    foreach (var name in assembly.GetManifestResourceNames())
                    {
                        if (name.EndsWith("de_words_20k.txt", StringComparison.OrdinalIgnoreCase))
                        {
                            using var fallback = assembly.GetManifestResourceStream(name);
                            if (fallback != null)
                            {
                                ReadWordList(fallback, set);
                                return set;
                            }
                        }
                    }
                    return set;
                }
                ReadWordList(stream, set);
            }
            catch
            {
                // Bei Problemen mit der Embedded Resource arbeiten wir nur mit der
                // Suffix-/Präfix-Heuristik weiter.
            }
            return set;
        }

        private static void ReadWordList(Stream stream, HashSet<string> set)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;
                // Zeilen enthalten bereits Kleinbuchstaben-Grundformen.
                set.Add(line.Trim());
            }
        }

        // Top-100-Häufigkeitswörter aus der Wortliste. Einzelne, sehr häufige deutsche
        // Wörter (z.B. "will" als Verbform, "Wille" als Substantiv) werden von spaCy
        // gelegentlich fälschlich als Person erkannt. Da sie in den allerhäufigsten
        // deutschen Wörtern vorkommen, ist die Wahrscheinlichkeit hoch, dass es sich um
        // ein generisches Wort handelt, keinen Eigennamen.
        private static readonly HashSet<string> _top100GermanWords = _germanWordList.Take(100).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Kombiniert die alte Stopwortliste (Task #204) mit dem neuen strukturellen Filter
        // und der Alias-Selbstkontaminations-Sperre (Task #208).
        // Fuer den Fall, dass spaCy zukuenftig generische Substantive liefert, die weder in
        // der Liste noch das harte Suffix/Praefix-Muster treffen, greift zusätzlich der
        // Abgleich gegen den deutschen Wortschatz.
        private static bool IsGermanCommonWordOrFragment(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            var clean = text.Trim('[', ']').Trim();
            if (_germanCommonWords.Contains(clean)) return true;

            // Silbentrennungs-Fragmente (ohne nachfolgenden Bindestrich) ablehnen.
            var withoutHyphen = clean.TrimEnd('-');
            if (_germanWordFragmentStoplist.Contains(withoutHyphen)) return true;

            // NEU (Task #207): generische deutsche Substantiv-Komposita ablehnen.
            if (IsGenericGermanSubstantivComposite(clean)) return true;

            // NEU (Task #207): Abgleich gegen den deutschen Wortschatz. Ein Wort, das im
            // Wortschatz steht, wird nur dann abgelehnt, wenn es zusätzlich ein hartes
            // Substantiv-Merkmal trägt (Suffix/Präfix) oder ein sehr häufiges Wort ist.
            // Das schützt zufällig im Wortschatz vorkommende Vornamen wie "Samir" oder
            // Nachnamen wie "Ruck", die weder Suffix/Präfix noch Top-100-Status haben.
            if (LooksLikeGenericGermanWord(clean)) return true;

            return false;
        }

        // Prüft, ob ein Wort im deutschen Wortschatz vorkommt UND zusätzlich ein Signal
        // für ein generisches Substantiv liefert. Die Suffix-/Präfix-Heuristik deckt
        // Fachkomposita wie "Wirtschaftsjahrs" oder "Elektroheizgeräte" ab; die
        // Top-100-Regel fängt einzelne hochfrequente Wörter wie "Will" ab.
        private static bool LooksLikeGenericGermanWord(string clean)
        {
            if (_germanWordList.Count == 0) return false;
            if (!_germanWordList.Contains(clean)) return false;

            // Suffix-/Präfix-Merkmal vorhanden?
            if (IsGenericGermanSubstantivComposite(clean)) return true;

            // Sehr häufiges deutsches Wort (Top 100) → wahrscheinlich kein Eigenname.
            if (_top100GermanWords.Contains(clean)) return true;

            return false;
        }

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

        // Pass 1: Name nach Anrede – "Frau Ruck", "Herrn Ali Izzet Erkol", "Dr. Schapmann".
        // Der Name darf mit bis zu 3 weiteren Token (z.B. Zwischenname + Nachname) erfasst werden.
        private static readonly System.Text.RegularExpressions.Regex _rxPersonSalutation =
            new(@"(?:Herr(?:n|in)?|Frau(?:en)?|Dr\.|Prof\.(?:in)?|Dipl\.[-\w]*\.)\s+([A-ZÄÖÜ][a-zäöüß]{1,20}(?:[^\S\r\n]+(?:[A-ZÄÖÜ]\.[^\S\r\n]*)?[A-ZÄÖÜ][a-zäöüß]{1,20}){0,3})",
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

        // Statische Stopwortliste deutscher Woerter/Wortfragmente, die im Fließtext
        // grossgeschrieben auftauchen koennen, aber keine Personennamen sind. Wird
        // spaCy-NER-Ergebnissen vorgehalten, um False-Positive Type-1-Erkennungen zu
        // vermeiden (Task #204: Bescheinigt, Mieters, Parken, Wo-, Las-).
        private static readonly HashSet<string> _germanCommonWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "Bescheinigt", "Bescheinigung", "Bescheinigungen", "Bestaetigt", "Bestaetigung",
            "Mieter", "Mieters", "Mieterin", "Mietvertrag", "Mietverhaeltnis",
            "Parken", "Geparkt", "Parkplatz", "Parkgebuehr",
            "Wohnung", "Wohnungs", "Wohnort", "Wohnsitz",
            "Lastschrift", "Lastschriften", "Ueberweisung", "Ueberweisungen",
            "Klage", "Klagen", "Klaeger", "Klaegerin", "Beklagter", "Beklagte",
            "Verfahren", "Verfahrens", "Verhandlung", "Verhandlungen",
            "Bescheid", "Bescheide", "Bescheides",
            "Antrag", "Antraege", "Antrags", "Antragsteller", "Antragstellerin",
            "Bescheinigungs", "Vollmacht", "Vollmachten",
            "Anlage", "Anlagen", "Anhang", "Anhaenge",
            "Unterlagen", "Unterlage",
            "Datum", "Daten", "Termin", "Termine",
            "Gebuehr", "Gebuehren", "Kosten", "Kostenvoranschlag",
            "Rechnung", "Rechnungen", "Zahlung", "Zahlungen",
            "Vorgang", "Vorgaenge", "Sache", "Sachverhalt",
            "Begruendung", "Begruendungen", "Entscheidung", "Entscheidungen",
            "Beschluss", "Beschluesse", "Urteil", "Urteile",
            "Anhoerung", "Anhoerungen", "Vereinbarung", "Vereinbarungen",
            "Aufforderung", "Aufforderungen", "Mahnung", "Mahnungen",
            "Widerspruch", "Widersprueche", "Beschwerde", "Beschwerden",
            "Berufung", "Berufungen", "Revision", "Revisionen",
            "Klageschrift", "Klageschriften", "Schriftsatz", "Schriftsaetze",
            "Stellungnahme", "Stellungnahmen", "Erklaerung", "Erklaerungen",
            "Beweis", "Beweise", "Beweisantrag", "Beweisantraege",
            "Gutachten", "Gutachter", "Gutachterin",
            "Termin", "Termine", "Sitzung", "Sitzungen",
            "Kosten", "Kostenfestsetzung", "Kostenentscheidung",
            "Schadensersatz", "Schadensersatzanspruch", "Schadensersatzansprueche",
            "Frist", "Fristen", "Fristsetzung",
            "Verpflichtung", "Verpflichtungen", "Anspruch", "Ansprueche",
            "Verzug", "Verzuges", "Mahngebuehr",
            "Betrag", "Betrags", "Betraege", "Geld",
            "Summe", "Summen", "Restschuld", "Schuld",
            "Kontostand", "Kontoauszug", "Auszug",
            "Einkommen", "Einkommens", "Gehalt", "Gehalts",
            "Miete", "Mieten", "Mietzins", "Mietzinses",
            "Nebenkosten", "Nebenkostenabrechnung",
            "Heizkosten", "Heizkostenabrechnung",
            "Strom", "Gas", "Wasser", "Abwasser",
            "Versicherung", "Versicherungen", "Versicherungs",
            "Beitrag", "Beitraege", "Beitrags",
            "Rueckzahlung", "Rueckzahlungen", "Erstattung", "Erstattungen",
            "Zinsen", "Zins", "Tilgung", "Tilgungen",
            "Darlehen", "Darlehens", "Hypothek", "Hypotheken",
            "Kauf", "Kaufs", "Kaufvertrag", "Kaufvertrages",
            "Verkauf", "Verkaufs", "Verkaeufers", "Verkaefer",
            "Schenkung", "Schenkungs",
            "Erbschein", "Erbscheins", "Erbe", "Erben",
            "Testament", "Testaments", "Vermaechtnis", "Vermaechtnisse",
            "Pflichtteil", "Pflichtteils", "Pflichtteilsanspruch",
            "Erbengemeinschaft", "Erbengemeinschafts",
            "Gesellschaft", "Gesellschafter", "Gmbh", "Gbr",
            "Firma", "Firmen", "Unternehmen",
            "Vermieter", "Vermieters", "Vermieterin",
            "Verkaeufer", "Verkaeufers", "Verkaeuferin",
            "Makler", "Maklers", "Maklerin",
            "Notar", "Notars", "Notarin",
            "Gericht", "Gerichts", "Amtsgericht", "Landgericht", "Oberlandesgericht",
            "Bundesgerichtshof", "Bundesverfassungsgericht", "Bundesverwaltungsgericht",
            "Bundesarbeitsgericht", "Bundessozialgericht",
            "Verwaltung", "Verwaltungs", "Behoerde", "Behoerden",
            "Finanzamt", "Finanzamts",
            "Jobcenter", "Agentur", "Arbeitsagentur",
            "Sozialamt", "Sozialamts", "Rathaus",
            "Polizei", "Staatsanwaltschaft",
            "Anwalt", "Anwalts", "Anwaelte", "Anwaelte",
            "Mandant", "Mandanten", "Mandantin",
            "Richter", "Richters", "Richterin",
            "Rechtspfleger", "Rechtspflegerin",
            "Zeuge", "Zeugen", "Zeugin",
            "Sachverstaendige", "Sachverstaendiger", "Sachverstaendigen",
            "Dokument", "Dokumente", "Dokuments",
            "Schrift", "Schriften", "Schreiben", "Schreibens",
            "Post", "Email", "E-Mail", "Telefax",
            "Anlage", "Anlagen",
            "Vertrag", "Vertrags", "Vertraege",
            "Konto", "Kontos", "Konten",
            "Bank", "Banken",
            "Berater", "Beraters", "Beraterin",
            "Sachbearbeiter", "Sachbearbeiterin", "Sachbearbeitung",
            "Widerspruch", "Widersprueche",
            "Rueckruf", "Rueckfrage", "Rueckfragen",
            "Zulage", "Zulagen", "Verguetung", "Verguetungen",
            "Auftrag", "Auftrags", "Auftraege",
            "Vollstreckung", "Vollstreckungs",
            "Pfaendung", "Pfaendungen",
            "Insolvenz", "Insolvenzverwalter", "Insolvenzverwalters",
            "Mahnbescheid", "Mahnbescheids",
            "Vollstreckungsbescheid", "Vollstreckungsbescheids",
            "Zahlungsaufforderung", "Zahlungsaufforderungen",
            "Leistung", "Leistungen",
            "Anschluss", "Anschlusses",
            "Folge", "Folgen",
            "Verbindlichkeit", "Verbindlichkeiten",
            "Forderung", "Forderungen",
            "Haftung", "Haftungen", "Haftungs",
            "Schulden", "Schuldner", "Schuldnerin",
            "Glaubiger", "Glaubigers", "Glaubigerin",
            "Betreibung", "Betreibungs",
            "Gerichtsvollzieher", "Gerichtsvollziehers", "Gerichtsvollzieherin",
            "Unterhalt", "Unterhalts",
            "Sorge", "Sorgerecht", "Sorgerechts",
            "Umgang", "Umgangs", "Umgangsrecht",
            "Ehe", "Ehegattens", "Ehegatte", "Ehegattin",
            "Scheidung", "Scheidungs",
            "Trennung", "Trennungs",
            "Hausrat", "Hausrats",
            "Versorgungsausgleich",
            "Zugewinn", "Zugewinns",
            "Gueterrecht", "Gueterrechts",
            "Ehevertrag", "Ehevertrags",
            "Erbvertrag", "Erbvertrags",
            "Patient", "Patienten", "Patientin",
            "Behandlung", "Behandlungen",
            "Arzt", "Arztes", "Aerztin",
            "Krankenhaus", "Krankenhauses",
            "Krankenkasse", "Krankenkassen",
            "Rezept", "Rezepte",
            "Unfall", "Unfalls",
            "Schaden", "Schadens", "Schaeden",
            "Hergang", "Hergangs",
            "Unfallbericht", "Unfallberichts",
            "Arbeitsunfaehigkeit", "Arbeitsunfaehigkeits",
            "Krankengeld", "Krankengelds",
            "Lohn", "Lohns", "Lohnfortzahlung",
            "Urlaub", "Urlaubs",
            "Arbeitszeit", "Arbeitszeiten",
            "Ueberstunden", "Ueberstunde",
            "Kuenigung", "Kuenigungs",
            "Abmahnung", "Abmahnungen",
            "Zeugnis", "Zeugnisses", "Zeugnisse",
            "Arbeitszeugnis", "Arbeitszeugnisses",
            "Befristung", "Befristungen",
            "Probezeit", "Probezeit",
            "Ausbildung", "Ausbildungs",
            "Praktikum", "Praktikums",
            "Werk", "Werks",
            "Werkvertrag", "Werkvertrags",
            "Dienstvertrag", "Dienstvertrags",
            "Arbeitsvertrag", "Arbeitsvertrags",
            "Geschaeft", "Geschaefts", "Geschaeftsfuehrer", "Geschaeftsfuehrers",
            "Protokoll", "Protokolls", "Protokolle",
            "Niederschrift", "Niederschriften",
            "Beweismittel", "Beweismittels",
            "Sachlage", "Sachlagen",
            "Rechtslage", "Rechtslagen",
            "Ausgang", "Ausgangs",
            "Ergebnis", "Ergebnisse",
            "Entwurf", "Entwuerfe",
            "Bedarfs",
            "Wohnungsgeber", "Wohnungsgebers",
            "Einzug", "Einzugs",
            "Umzug", "Umzugs",
            "Nachweis", "Nachweise", "Nachweises",
            "Einkommensnachweis", "Einkommensnachweises",
            "Mietkaution", "Mietkaution",
            "Kaution", "Kautions",
            "Schufa", "Schufas",
            "Bonitaet", "Bonitaets",
            "Selbstauskunft", "Selbstauskuenfte",
            "Steuer", "Steuern", "Steuererklaerung", "Steuerbescheid",
            "Einkommensteuer", "Umsatzsteuer", "Gewerbesteuer",
            "Steuernummer", "Steueridentifikationsnummer",
            "Jahressteuer", "Festsetzung", "Festsetzungs",
            "Nachzahlung", "Nachzahlungen", "Erstattung", "Erstattungen",
            "Sozialabgaben",
            "Rentenversicherung", "Krankenversicherung", "Arbeitslosenversicherung",
            "Pflegeversicherung",
            "Renten", "Rente", "Rentenanspruch", "Rentenansprueche",
            "Pension", "Pensionen",
            "Alter", "Alters",
            "Behindert", "Behinderten",
            "Schwerbehindertenausweis",
            "Merkblatt", "Merkmale",
            "Vordruck", "Vordrucke",
            "Formular", "Formulare",
            "Antragsformular", "Antragsformulare",
            "Beilage", "Beilagen",
            "Zusammenfassung", "Zusammenfassungen",
            "Vermerk", "Vermerke",
            "Notiz", "Notizen",
            "Wiedervorlage", "Wiedervorlagen",
            "Aktenzeichen", "Az", "Az.",
            "Geschaeftszeichen",
            "Sachgebiet", "Sachgebiete",
            "Bearbeiter", "Bearbeiters", "Bearbeiterin",
            "Telefon", "Telefons", "Fax", "Mobil",
            "Handy", "Handys",
            "Durchwahl", "Durchwahlen",
            "Zimmer", "Zimmernummer",
            "Hauptstelle", "Zweigstelle", "Filiale",
            "Berlin", "Hamburg", "Muenchen", "Koeln", "Frankfurt", "Stuttgart",
            "Dortmund", "Essen", "Duesseldorf", "Bremen", "Hannover",
            "Dresden", "Leipzig", "Nuernberg", "Duisburg", "Bochum",
            "Wuppertal", "Bielefeld", "Bonn", "Mannheim", "Karlsruhe"
        };

        // Wortfragmente, die typischerweise durch Silbentrennung am Zeilenende entstehen.
        // Werden zusammen mit der Zeilenumbruch-Normalisierung aus dem Text entfernt/
        // zusammengefuehrt, damit sie nicht als eigenstaendige (Personen-)Tokens erkannt
        // werden (Task #204: Wo-, Las-).
        // Die Liste ist bewusst KURZ gehalten: sie enthaelt nur die vom Review gemeldeten
        // Fragmente. Die Zeilenumbruch-Normalisierung selbst verhindert generisch, dass
        // irgendein Wort mit Bindestrich vor Zeilenumbruch als eigenes Token erkannt wird.
        private static readonly HashSet<string> _germanWordFragmentStoplist = new(StringComparer.OrdinalIgnoreCase)
        {
            "Wo", "Las", "Beschein", "Miet", "Park", "Wohn", "Last",
            "Ver", "Klaeg", "Beklagt", "Antrags", "Beweis", "Gutacht",
            "Vollmacht", "Vollstreck", "Zahlungs", "Rueck", "Wider"
        };

        // Fasst Silbentrennungen am Zeilenumbruch zusammen: "Wo-\n\\t\\t...nung" → "Wohnung".
        // Schützt gleichzeitig die Fragmente vor der spaCy-NER-Pipeline, da sie danach nicht
        // mehr als eigene Woerter auftauchen. Der Regex entfernt den Bindestrich und den
        // Zeilenumbruch-Zwischenraum, wenn das naechste sichtbare Zeichen ein Kleinbuchstabe
        // ist (d.h. es handelt sich um die Fortsetzung desselben Wortes).
        private static readonly System.Text.RegularExpressions.Regex _rxHyphenLineBreak =
            new(@"-(\s*\r?\n\s*)(?=[a-zäöüß])",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        public static async Task<string> AnonymizeTextAsync(string text, List<Entity> entities)
        {
            // Schützt bereits vergebene Alias-Werte (Personen-/Adress-Aliase, Platzhalter-Codes
            // wie [STEUER-5]/[ORT-4], Fake-IBANs/E-Mails/Beträge/Daten) vor erneuter Erkennung,
            // bevor irgendeine Regex- oder NER-Erkennung läuft. Ohne diesen Schutz würde ein
            // bereits (teil-)anonymisierter Text bei wiederholter Verarbeitung Alias-Ketten
            // erzeugen (Task #202: Erkol -> Dr. Watson -> Prof. Moriarty -> Verleihnix -> Robin).
            var aliasRestoreMap = new Dictionary<string, string>();
            string result = ProtectKnownAliases(text, aliasRestoreMap);

            // Zeilenumbruch-Silbentrennungen entfernen, BEVOR die NER-Pipeline laeuft,
            // damit Fragmente wie "Wo-" oder "Las-" nicht als Pseudo-Personen erkannt werden
            // (Task #204). Der Bindestrich wird entfernt, damit "Wo-\nnung" zu "Wohnung"
            // wird und spaCy den Originaltext nicht als zwei separate Tokens sieht.
            result = _rxHyphenLineBreak.Replace(result, "");

            result = ReplaceWithAlias(result, _rxSteuernummer, EntityType.Aktenzeichen, () => _fakeSteuernummern[_steuerIdx++ % _fakeSteuernummern.Length], entities);
            result = ReplaceWithAlias(result, _rxUstIdNr,       EntityType.Aktenzeichen, () => _fakeUstIdNr[_ustIdx++ % _fakeUstIdNr.Length],               entities);
            result = ReplaceWithAlias(result, _rxIban,         EntityType.Konto,        () => _fakeIbans[_ibanIdx++ % _fakeIbans.Length],         entities);
            result = ReplaceWithAlias(result, _rxAktenzeichen, EntityType.Aktenzeichen, () => $"[AZ-{_counter++}]",                                       entities);
            result = ReplaceWithAlias(result, _rxTelefon,      EntityType.Telefon,      () => $"[TEL-{_counter++}]",                                      entities);
            result = ReplaceWithAlias(result, _rxEmail,        EntityType.Email,        () => _fakeEmails[_emailIdx++ % _fakeEmails.Length],               entities);
            result = ReplaceWithAlias(result, _rxBetrag,       EntityType.Betrag,       () => GenerateUniqueBetrag(_betragIdx++),          entities);
            result = ReplaceWithAlias(result, _rxDatum,        EntityType.Datum,        () => GenerateUniqueDatum(_datumIdx++),                 entities);
            result = ReplaceWithAlias(result, _rxAdresse,      EntityType.Adresse,      () => GenerateUniqueAdresse(_addrIdx++),           entities);
            result = ReplaceWithAlias(result, _rxPlz,          EntityType.Adresse,      () => $"[ORT-{_counter++}]",                                      entities);
            result = ReplaceStrasseLabelZeile(result, entities);
            result = ReplaceWohnortLabelZeile(result, entities);
            result = await ReplacePersonsAsync(result, entities);

            result = RestoreProtectedAliases(result, aliasRestoreMap);
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

        // Normalisiert einen Original-String für den Fallback-Abgleich in _mappingByNormalizedKey:
        // mehrfacher Whitespace (inkl. Zeilenumbrüche) wird auf ein einzelnes Leerzeichen reduziert,
        // Rand-Whitespace entfernt. Fängt Extraktionsunterschiede zwischen Dokumentformaten ab.
        private static string NormalizeKey(string s) =>
            System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();

        // Sucht einen bereits bekannten Alias für 'original' — erst exakt in _mapping, dann als
        // Fallback über die normalisierte Form in _mappingByNormalizedKey.
        private static bool TryGetKnownAlias(string original, out string alias)
        {
            if (_mapping.TryGetValue(original, out alias!))
                return true;
            if (_mappingByNormalizedKey.TryGetValue(NormalizeKey(original), out alias!))
                return true;
            alias = string.Empty;
            return false;
        }

        // Sucht einen Alias für eine Namens-Variante, die noch nicht exakt/normalisiert bekannt
        // ist, aber vermutlich dieselbe Person wie ein bereits gemapptes Personen-Mapping meint,
        // z.B.:
        //   - "Ali Erkol" vs. bekanntem "Ali Izzet Erkol" (Zwischenname fehlt im Fragment)
        //   - "Ali Izzet" vs. bekanntem "Ali Izzet Erkol" (Nachname fehlt im Fragment komplett,
        //     Task #203: der Nachname-Abgleich allein greift hier nicht, weil "Izzet" im
        //     kürzeren Fragment gar nicht als Nachname vorkommt)
        //   - "Ali" allein vs. bekanntem "Ali Izzet Erkol" (nur Vorname)
        // Über ALLE bekannten Personen-Einträge iterieren und bei mehreren unterschiedlichen
        // Treffern (z.B. "Ali Izzet Erkol" UND ein unabhängiges "Ali Yilmaz") konservativ KEINEN
        // Alias liefern, statt zu raten — echte Mehrdeutigkeit darf nie zu einer Fehlzuordnung führen.
        private static bool TryFindAliasForNameVariant(string name, out string alias)
        {
            var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                alias = string.Empty;
                return false;
            }

            string? foundAlias = null;
            foreach (var kvp in _mapping)
            {
                if (!_entityTypes.TryGetValue(kvp.Key, out var type) || type != EntityType.Person)
                    continue;
                var knownTokens = kvp.Key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (knownTokens.Length < 2) continue; // nur volle Namen, keine Nachname-Solo-Einträge
                if (!IsLikelySamePerson(tokens, knownTokens)) continue;

                if (foundAlias != null && !foundAlias.Equals(kvp.Value, StringComparison.Ordinal))
                {
                    // Mehrdeutig: Fragment passt auf mindestens zwei unterschiedliche bekannte
                    // Personen (z.B. gleicher Vorname bei zwei verschiedenen Nachnamen) -> lieber
                    // keinen Alias wiederverwenden als eine falsche Zusammenführung zu riskieren.
                    alias = string.Empty;
                    return false;
                }
                foundAlias = kvp.Value;
            }

            if (foundAlias != null)
            {
                alias = foundAlias;
                return true;
            }

            alias = string.Empty;
            return false;
        }

        // Prüft, ob zwei Namens-Tokenlisten vermutlich dieselbe Person meinen:
        //  (a) identischer Nachname (letztes Token) UND mindestens ein weiterer gemeinsamer Token
        //      (fängt fehlenden Zwischennamen ab, z.B. "Ali Erkol" vs. "Ali Izzet Erkol")
        //  (b) die kürzere Tokenliste ist ein Präfix der längeren, Token für Token
        //      (fängt ein komplett fehlendes Nachnamen-Fragment ab, z.B. "Ali Izzet" oder "Ali"
        //      allein vs. "Ali Izzet Erkol" — hier kommt der Nachname im kürzeren Fragment gar
        //      nicht vor, weshalb Fall (a) allein nicht greifen würde)
        // Kurze Tokens (<3 Zeichen, z.B. Initialen) werden für den Präfix-Vergleich in (b)
        // bewusst ausgeschlossen, um Fehlzuordnungen über zu generische Fragmente zu vermeiden.
        private static bool IsLikelySamePerson(string[] tokensA, string[] tokensB)
        {
            if (tokensA.Length >= 2 && tokensB.Length >= 2)
            {
                var surnameA = tokensA[^1];
                var surnameB = tokensB[^1];
                if (surnameA.Length >= 4 && surnameA.Equals(surnameB, StringComparison.OrdinalIgnoreCase))
                {
                    var extraMatch = tokensA.Take(tokensA.Length - 1)
                        .Any(t => tokensB.Take(tokensB.Length - 1)
                            .Any(kt => kt.Equals(t, StringComparison.OrdinalIgnoreCase)));
                    if (extraMatch) return true;
                }
            }

            var shorter = tokensA.Length <= tokensB.Length ? tokensA : tokensB;
            var longer  = tokensA.Length <= tokensB.Length ? tokensB : tokensA;
            if (shorter.Length >= 1 && shorter.Length < longer.Length)
            {
                bool allMatch = true;
                for (int i = 0; i < shorter.Length; i++)
                {
                    if (shorter[i].Length < 3 || !shorter[i].Equals(longer[i], StringComparison.OrdinalIgnoreCase))
                    {
                        allMatch = false;
                        break;
                    }
                }
                if (allMatch) return true;
            }

            return false;
        }

        // Registriert ein Mapping in beiden Dictionaries. Immer aufrufen statt _mapping direkt
        // zu befüllen, damit _mappingByNormalizedKey konsistent bleibt.
        private static void RegisterMapping(string original, string alias, EntityType type)
        {
            _mapping[original] = alias;
            _entityTypes[original] = type;
            _mappingByNormalizedKey[NormalizeKey(original)] = alias;
            _knownAliasValues.Add(alias);
        }

        // Ersetzt jedes Vorkommen eines bereits bekannten Alias-Werts im Text durch ein
        // eindeutiges Sentinel-Token, BEVOR irgendeine Erkennungs-Regex oder der NER-Pass
        // läuft. Ohne diesen Schutz matcht z.B. die Anrede-Regex "Dr." auch innerhalb eines
        // bereits erzeugten Alias wie "[Dr. Watson]" erneut und erzeugt eine Alias-Kette
        // (Task #202). Längere Aliase zuerst, damit keine Teil-Überschneidungen entstehen.
        private static string ProtectKnownAliases(string text, Dictionary<string, string> restoreMap)
        {
            if (_knownAliasValues.Count == 0) return text;
            int i = 0;
            foreach (var alias in _knownAliasValues.OrderByDescending(a => a.Length))
            {
                if (string.IsNullOrEmpty(alias) || !text.Contains(alias)) continue;
                var token = $"{i++}";
                restoreMap[token] = alias;
                text = text.Replace(alias, token);
            }
            return text;
        }

        private static string RestoreProtectedAliases(string text, Dictionary<string, string> restoreMap)
        {
            foreach (var kvp in restoreMap)
                text = text.Replace(kvp.Key, kvp.Value);
            return text;
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
                if (TryGetKnownAlias(original, out var existing))
                {
                    RegisterMapping(original, existing, type);
                    if (!entities.Any(e => e.Text == original))
                        entities.Add(new Entity { Text = original, Type = type, AnonymizedText = existing });
                    return existing;
                }
                var alias = makeAlias();
                RegisterMapping(original, alias, type);
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
                if (TryGetKnownAlias(original, out var existing))
                {
                    alias = existing;
                }
                else
                {
                    alias = GenerateUniqueAdresse(_addrIdx++);
                }
                RegisterMapping(original, alias, EntityType.Adresse);
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
                if (TryGetKnownAlias(original, out var existing))
                {
                    alias = existing;
                }
                else
                {
                    alias = GenerateUniqueCity(_cityIdx++);
                }
                RegisterMapping(original, alias, EntityType.Adresse);
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
            // Volltext-Sweep bleibt unangetastet, solange ein "Original" selbst ein bereits
            // vergebener Alias-Wert ist — sonst würde z.B. eine (fehlerhaft) als Original
            // registrierte Alias-Teilzeichenkette erneut in einem anderen Alias gefunden und
            // ersetzt und so eine Alias-Kette fortsetzen (Task #202).
            var relevantEntries = _mapping
                .Where(kvp => _entityTypes.TryGetValue(kvp.Key, out var t) && (t == EntityType.Person || t == EntityType.Adresse))
                .Where(kvp => !_knownAliasValues.Contains(kvp.Key))
                .OrderByDescending(kvp => kvp.Key.Length)
                .ToList();

            // Schützt bereits im Text vorhandene Alias-Werte vor diesem Sweep, damit deren
            // Inhalt (z.B. "Watson" in "[Dr. Watson]") nicht versehentlich als Substring eines
            // anderen bekannten Originals getroffen und ersetzt wird.
            var aliasRestoreMap = new Dictionary<string, string>();
            text = ProtectKnownAliases(text, aliasRestoreMap);

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

            text = RestoreProtectedAliases(text, aliasRestoreMap);
            return text;
        }

        private static async Task<string> ReplacePersonsAsync(string text, List<Entity> entities)
        {
            // Zeilenumbruch-Silbentrennungen entfernen, BEVOR die Anrede-Regex greift,
            // damit Fragmente wie "Wo-" oder "Las-" nicht als Namensbestandteil gesehen
            // werden (Task #204). Der Bindestrich wird entfernt, sodass "Wo-\nnung" zu
            // "Wohnung" wird.
            text = _rxHyphenLineBreak.Replace(text, "");

            // Pass 1: Salutation-basiert (Frau/Herr/Dr./Prof.) — bleibt unverändert, sehr zuverlässig
            text = _rxPersonSalutation.Replace(text, match =>
            {
                var original = match.Groups[1].Value;
                // Task #208: Bereits erzeugte Alias-/Platzhalterwerte (oder deren Teiltoken)
                // duerfen nicht erneut als Person erkannt werden, auch wenn sie in Anrede-
                // Kontext auftauchen (z.B. "Herr Baker Street" nachdem "[Baker Street 221b]"
                // bereits als Adress-Alias vergeben wurde).
                if (IsKnownAliasOrFragment(original)) return match.Value;
                string alias;
                if (TryGetKnownAlias(original, out var existing))
                {
                    alias = existing;
                    if (!entities.Any(e => e.Text == original))
                        entities.Add(new Entity { Text = original, Type = EntityType.Person, AnonymizedText = alias });
                }
                else if (TryFindAliasForNameVariant(original, out var variantAlias))
                {
                    alias = variantAlias;
                    if (!entities.Any(e => e.Text == original))
                        entities.Add(new Entity { Text = original, Type = EntityType.Person, AnonymizedText = alias });
                }
                else
                {
                    alias = $"[{PersonPool[_personIdx++ % PersonPool.Length]}]";
                    entities.Add(new Entity { Text = original, Type = EntityType.Person, AnonymizedText = alias });
                }
                StorePersonAlias(original, alias);
                int prefixLen = match.Groups[1].Index - match.Index;
                return match.Value[..prefixLen] + alias;
            });

            // Pre-Pass: bekannte Personen/Adressen aus Mapping direkt ersetzen (längste zuerst → kein Doppelersatz)
            text = ApplyKnownMappings(text, entities);

            // Pass 2: spaCy NER — nur noch unbekannte Namen werden gefunden
            text = await ReplaceViaSpacyNer(text, entities);

            return text;
        }

        // Ruft POST /ner auf, ersetzt neue PER-Entitäten und speichert Alias + Teilnamen im Mapping.
        // Vor der Alias-Vergabe wird jede Entität gegen eine Stopwortliste deutscher
        // Allerweltswörter und Silbentrennungs-Fragmente geprüft (Task #204), damit
        // spaCy-False-Positive wie "Bescheinigt", "Mieters", "Parken", "Wo-" oder "Las-"
        // nicht mehr als Person anonymisiert werden.
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

            foreach (var ent in nerResult.entities.OrderByDescending(e => e.start))
            {
                var name = ent.text.Contains('\n') ? ent.text[..ent.text.IndexOf('\n')].Trim() : ent.text.Trim();
                if (name.Length < 2) continue;
                if (IsGermanCommonWordOrFragment(name)) continue;
                if (IsKnownAliasOrFragment(name)) continue; // Task #208: Selbstkontamination verhindern
                if (!text.Contains(name)) continue;
                if (TryGetKnownAlias(name, out _)) continue;

                string alias;
                if (TryFindAliasForNameVariant(name, out var variantAlias))
                {
                    alias = variantAlias;
                }
                else
                {
                    alias = $"[{PersonPool[_personIdx++ % PersonPool.Length]}]";
                }
                StorePersonAlias(name, alias);
                entities.Add(new Entity { Text = name, Type = EntityType.Person, AnonymizedText = alias });
                text = text.Replace(name, alias);
            }
            return text;
        }

        // Speichert den vollen Namen UND den letzten Token (Nachname) als separate Mapping-Einträge
        private static void StorePersonAlias(string fullName, string alias)
        {
            RegisterMapping(fullName, alias, EntityType.Person);

            // Letzten Token als Nachname separat mappen (≥4 Zeichen, kein Alias-Muster)
            var tokens = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var lastName = tokens.Length > 0 ? tokens[tokens.Length - 1] : fullName;
            if (lastName.Length >= 4 && !TryGetKnownAlias(lastName, out _))
            {
                RegisterMapping(lastName, alias, EntityType.Person);
            }
        }

        // Prüft, ob der Dateiname offensichtlich zu einer bereits bekannten Person gehört
        // (z.B. "KFZ Anmeldung Ali Erkol.pdf" bei bekanntem Mapping "Ali Izzet Erkol" -> Alias)
        // und ersetzt den Namens-Anteil im Dateinamen durch diesen Alias. Muss erst aufgerufen
        // werden, nachdem das globale Mapping über alle Dokumente eines Batch-Laufs vollständig
        // ist (Cross-Dokument-Sweep), damit z.B. ein gescanntes PDF ohne extrahierbaren Text
        // trotzdem über den Dateinamen anonymisiert werden kann.
        // \b behandelt "_" (und Ziffern) als Wortzeichen, weshalb es KEINE Grenze zwischen
        // Buchstabe und "_" erkennt. Bei diesem Tool sind unterstrich-getrennte Dateinamen
        // (z.B. "Schreiben_Erkol_Ali_2024.pdf") bzw. direkt an einen Namen anschließende
        // Datumsziffern (z.B. "Erkol2024_Akte.pdf") gängig — mit \b würde der Name dort NICHT
        // gefunden. Diese Grenze verwendet stattdessen ausschließlich Buchstaben als
        // Wortzeichen, sodass "_", Ziffern, "-" und Leerzeichen als Trenner zählen.
        private static readonly string _fileNameWordBoundary = "[A-Za-zÄÖÜäöüß]";

        private static System.Text.RegularExpressions.Match MatchFileNameToken(string baseName, string token)
        {
            var pattern = $@"(?<!{_fileNameWordBoundary}){System.Text.RegularExpressions.Regex.Escape(token)}(?!{_fileNameWordBoundary})";
            return System.Text.RegularExpressions.Regex.Match(
                baseName, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        public static string AnonymizeFileName(string fileName)
        {
            var ext = Path.GetExtension(fileName);
            var baseName = Path.GetFileNameWithoutExtension(fileName);

            var personEntries = _mapping
                .Where(kvp => _entityTypes.TryGetValue(kvp.Key, out var t) && t == EntityType.Person)
                .Where(kvp => kvp.Key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2)
                .OrderByDescending(kvp => kvp.Key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length)
                .ThenByDescending(kvp => kvp.Key.Length)
                .ToList();

            foreach (var (fullName, alias) in personEntries)
            {
                var nameTokens = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var surname = nameTokens[^1];
                if (surname.Length < 4) continue;

                // Nachname muss als eigenes Wort im Dateinamen vorkommen.
                var surnameMatch = MatchFileNameToken(baseName, surname);
                if (!surnameMatch.Success) continue;

                // Mindestens ein weiterer Namens-Token (z.B. Vorname) muss ebenfalls vorkommen,
                // damit ein Nachname allein (der zufällig auch ein normales Wort sein könnte)
                // nicht schon als "offensichtlich zugehörig" zählt.
                var otherTokenMatches = nameTokens.Take(nameTokens.Length - 1)
                    .Select(t => MatchFileNameToken(baseName, t))
                    .Where(m => m.Success)
                    .ToList();
                if (otherTokenMatches.Count == 0) continue;

                // Zusammenhängenden Bereich von erstem bis letztem Treffer (Vorname(n) +
                // Nachname) durch den Alias ersetzen; der Rest des Dateinamens (z.B.
                // "KFZ Anmeldung ") bleibt erhalten.
                var allMatches = otherTokenMatches.Append(surnameMatch).OrderBy(m => m.Index).ToList();
                int start = allMatches.First().Index;
                int end = allMatches.Last().Index + allMatches.Last().Length;

                var aliasName = alias.Trim('[', ']');
                var newBaseName = (baseName[..start] + aliasName + baseName[end..]).Trim();
                return newBaseName + ext;
            }

            return fileName;
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
                var mainPart = document.MainDocumentPart;
                if (mainPart == null)
                    throw new InvalidDataException($"Word-Dokument hat keinen MainDocumentPart: {filePath}");
                var body = mainPart.Document.Body;
                if (body == null)
                    throw new InvalidDataException($"Word-Dokument hat keinen Body: {filePath}");
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

        // Ordner-Mapping-Datei setzen und laden (beim Öffnen eines Ordners aufrufen).
        // Wird derselbe Ordner erneut geöffnet (z.B. erneute Verarbeitung desselben Batches),
        // werden bestehende In-Memory-Mappings NICHT verworfen — es wird nur mit dem Inhalt
        // der Datei gemergt (siehe LoadMappingFromFile), damit noch nicht gespeicherte, aber
        // bereits im Speicher vorhandene Zuordnungen nicht verloren gehen bzw. durch einen
        // blinden Reload erneut "frisch" verarbeitet werden (Task #202).
        public static void SetMappingFile(string folderPath)
        {
            var newPath = Path.Combine(folderPath, ".lexwolf_mapping.json");
            if (newPath == _mappingFilePath)
            {
                LoadMappingFromFile();
                return;
            }

            _mappingFilePath = newPath;
            _mapping.Clear();
            _entityTypes.Clear();
            _mappingByNormalizedKey.Clear();
            _knownAliasValues.Clear();
            _personIdx = _addrIdx = _datumIdx = _betragIdx = _ibanIdx = _emailIdx = 0;
            _counter = 1;
            LoadMappingFromFile();
        }

        // Mapping aus JSON-Datei laden und Person-Einträge per NER validieren
        // Falsch-positive Einträge (spaCy erkennt nicht als PER) werden entfernt + geloggt
        // Fallback: Wenn NER nicht erreichbar, werden alle Einträge behalten
        // Merged nur fehlende Einträge in den Speicher, statt bestehende (evtl. bereits im
        // laufenden Batch neu hinzugekommene) Einträge blind zu überschreiben (Task #202).
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
                    if (_mapping.ContainsKey(e.Original)) continue;
                    _mapping[e.Original] = e.Alias;
                    _entityTypes[e.Original] = e.Type;
                    _mappingByNormalizedKey[NormalizeKey(e.Original)] = e.Alias;
                    _knownAliasValues.Add(e.Alias);
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
                        _mappingByNormalizedKey.Remove(NormalizeKey(orig));
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
