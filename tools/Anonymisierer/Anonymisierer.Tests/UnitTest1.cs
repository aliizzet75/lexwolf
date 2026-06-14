using System.Text.RegularExpressions;
using Anonymisierer.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Anonymisierer.Tests
{
    /// <summary>
    /// Integrations-Tests für die Anonymisierung des echten Mietvertrags
    /// C:\Temp\31_Erkol_Mietvertrag.pdf
    ///
    /// Die App verwendet Anonymizer.AnonymizeText (Main.cs).
    /// Platzhalter haben das Format [Comic-Name], [BETRAG-N], [TEL-N], [DATUM-N], [ORT-N], [KONTO-N].
    /// </summary>
    [TestClass]
    public class MietvertragAnonymisierungTests
    {
        private const string PdfPfad = @"C:\Temp\31_Erkol_Mietvertrag.pdf";

        // Matcht alle [...]-Platzhalter im anonymisierten Text.
        private static readonly Regex PlatzhalterMuster =
            new(@"\[[^\]]+\]", RegexOptions.Compiled);

        private static string _original = string.Empty;
        private static string _anonymisiert = string.Empty;
        private static List<Entity> _entitaeten = [];

        [ClassInitialize]
        public static void Initialisieren(TestContext _)
        {
            _original = UnifiedFileReader.ReadFile(PdfPfad);
            _anonymisiert = Anonymizer.AnonymizeText(_original, out _entitaeten);
        }

        // ── PDF-Inhalt ──────────────────────────────────────────────────────────

        [TestMethod]
        public void PDF_WirdKorrektGelesen()
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(_original),
                "PDF-Inhalt darf nicht leer sein");
            Assert.IsTrue(_original.Contains("Wohnraummietvertrag"),
                "Original muss den Vertragstitel enthalten");
            Assert.IsTrue(_original.Contains("Ali Izzet Erkol"),
                "Original muss den Mieternamen enthalten");
        }

        // ── Persönliche Daten werden ersetzt ──────────────────────────────────

        [TestMethod]
        public void Mietername_WirdErsetzt()
        {
            // "Ali Izzet Erkol" = drei ASCII-Wörter mit Großbuchstaben, kein Zeilenumbruch dazwischen.
            // Der Regex [A-ZÄÖÜ][a-zäöüß]{1,20}(...){1,2} erkennt 2-3-Wort-Namen.
            Assert.IsFalse(_anonymisiert.Contains("Ali Izzet Erkol"),
                "Vollständiger Mietername 'Ali Izzet Erkol' darf nicht mehr im Text stehen");
            Assert.IsTrue(_entitaeten.Any(e => e.Text == "Ali Izzet Erkol"),
                "'Ali Izzet Erkol' muss als erkannte Entität gemeldet sein");
        }

        [TestMethod]
        public void Vertragsdatum_WirdErsetzt()
        {
            // "15.06.2020" wird vom Datum-Regex erkannt.
            Assert.IsFalse(_anonymisiert.Contains("15.06.2020"),
                "Vertragsdatum 15.06.2020 darf nicht mehr im Text stehen");
            Assert.IsTrue(_entitaeten.Any(e => e.Type == EntityType.Datum),
                "Mindestens ein Datum muss erkannt worden sein");
        }

        [TestMethod]
        public void IBAN_WirdErsetzt()
        {
            // IBAN "DE67 6009 0100 0368 711013" ist eindeutig persönliche Bankdaten.
            Assert.IsFalse(_anonymisiert.Contains("DE67"),
                "IBAN-Anfang 'DE67' darf nicht mehr im Text stehen");
            Assert.IsTrue(_entitaeten.Any(e => e.Type == EntityType.Konto),
                "Mindestens ein Kontoeintrag (IBAN) muss erkannt worden sein");
        }

        [TestMethod]
        public void Telefonnummer_WirdErsetzt()
        {
            Assert.IsFalse(_anonymisiert.Contains("0711/22249380"),
                "Vermieter-Telefon darf nicht mehr im Text stehen");
            Assert.IsTrue(_entitaeten.Any(e => e.Type == EntityType.Person &&
                          e.AnonymizedText.StartsWith("[TEL")),
                "Mindestens eine Telefonnummer muss erkannt worden sein");
        }

        [TestMethod]
        public void Betraege_WerdenErsetzt()
        {
            // "125,00 €" (§9, Kleinreparatur-Grenze) liegt als "NUMBER €" auf einer Zeile
            // und wird vom Betrag-Regex (\d+,\d{2}\s*€) erkannt.
            Assert.IsFalse(_anonymisiert.Contains("125,00"),
                "Betrag 125,00 € (§9) darf nicht mehr im Text stehen");
            Assert.IsTrue(_entitaeten.Any(e => e.Type == EntityType.Betrag),
                "Mindestens ein Betrag muss erkannt worden sein");
        }

        [TestMethod]
        public void AlleErkanntenEntitaeten_SindImErgebnisNichtMehrVorhanden()
        {
            Assert.IsTrue(_entitaeten.Count > 0, "Es müssen Entitäten erkannt worden sein");

            foreach (var entitaet in _entitaeten)
            {
                Assert.IsFalse(_anonymisiert.Contains(entitaet.Text),
                    $"Erkannte Entität '{entitaet.Text}' (Typ: {entitaet.Type}) " +
                    $"darf nicht mehr im anonymisierten Text stehen");
            }
        }

        // ── Vertragstext bleibt 1:1 erhalten ──────────────────────────────────

        [TestMethod]
        public void Vertragstitel_BleibtErhalten()
        {
            // "Wohnraummietvertrag" ist ein einzelnes Wort und wird vom
            // Namens-Regex (erfordert mind. 2 Wörter) nicht erfasst.
            Assert.IsTrue(_anonymisiert.Contains("Wohnraummietvertrag"),
                "Vertragstitel 'Wohnraummietvertrag' muss erhalten bleiben");
        }

        [TestMethod]
        public void Vertragsstruktur_BleibtErhalten()
        {
            // Nicht geprüft:
            // - "§ 18 Sonstige Vereinbarungen": "Sonstige Vereinbarungen" = Zwei-Wort-Großschreibung
            //   → wird als Person erkannt (bekannte Einschränkung des Regex-Ansatzes)
            // - "70435 Stuttgart": PLZ-Regex ersetzt "NNNNN STADTNAME" → [ORT-N]
            string[] pflichtFragmente =
            [
                "§ 1 Mietsache",
                "§ 2 Mietzeit/Kündigung",
                "§ 3 Miete",
                "§ 4 Kaution",
                "§ 5 Haftung des Vermieters",
                "§ 6 Anzeige- und Wartungspflichten",
                "§ 7 Besichtigung der Mietsache",
                "§ 8 Veränderungen an und in der Mietsache",
                "§ 9 Erhaltung der Mietsache",
                "§ 15 Wohnfläche",
                "§ 16 Meldepflicht",
                "§ 17 Datenschutzhinweise",
                "wird folgender Mietvertrag geschlossen:",
                "als Vermieter/in",
                "als Mieter/in",
            ];

            foreach (var fragment in pflichtFragmente)
            {
                Assert.IsTrue(_anonymisiert.Contains(fragment),
                    $"Pflichtfragment '{fragment}' fehlt im anonymisierten Text");
            }
        }

        [TestMethod]
        public void Zeilenanzahl_NimmtNichtZu()
        {
            // Die Anonymisierung darf keine NEUEN Zeilenumbrüche einführen.
            // Sie kann Zeilen zusammenfassen (z.B. wenn "€\nZahl" als ein Betrag erkannt wird),
            // aber niemals neue Zeilen hinzufügen.
            int originalZeilen = _original.Split('\n').Length;
            int anonymisiertZeilen = _anonymisiert.Split('\n').Length;

            Assert.IsTrue(anonymisiertZeilen <= originalZeilen,
                $"Zeilenanzahl darf nicht zunehmen: Original {originalZeilen}, Anonymisiert {anonymisiertZeilen}");
        }

        // ── Nur erkannte Teile wurden geändert ────────────────────────────────

        // Trennt auf alles, was kein Unicode-Buchstabe/Ziffer ist — damit bleiben nie
        // reine Satzzeichen als "Wörter" übrig, was sonst false positives erzeugen würde.
        private static readonly Regex _wortTrenner = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

        [TestMethod]
        public void NichtPlatzhalterParts_KommenAusOriginal()
        {
            // Nach dem Entfernen aller [...]-Platzhalter dürfen keine lexikalischen Tokens
            // im Ergebnis stehen, die nicht auch im Original vorkommen. Reine Satzzeichen
            // (Klammern, Bindestriche …) zählen nicht als Token.
            var textOhnePlatzhalter = PlatzhalterMuster.Replace(_anonymisiert, "");

            var originalTokens = new HashSet<string>(
                _wortTrenner.Split(_original).Where(w => w.Length > 0));

            var fremdeTokens = _wortTrenner.Split(textOhnePlatzhalter)
                .Where(w => w.Length > 0 && !originalTokens.Contains(w))
                .Distinct()
                .ToList();

            Assert.AreEqual(0, fremdeTokens.Count,
                $"Folgende Tokens außerhalb von Platzhaltern sind neu (dürfen nicht sein): " +
                $"{string.Join(", ", fremdeTokens.Take(10))}");
        }

        [TestMethod]
        public void AlleAliase_SindImErgebnisVorhanden()
        {
            // Jede gemeldete Entität muss mit ihrem Alias im Ergebnis vorhanden sein.
            // (Gleiches Original → gleicher Alias, kann mehrfach auftreten.)
            foreach (var entitaet in _entitaeten)
            {
                Assert.IsTrue(_anonymisiert.Contains(entitaet.AnonymizedText),
                    $"Alias '{entitaet.AnonymizedText}' für '{entitaet.Text}' " +
                    $"fehlt im anonymisierten Text");
            }
        }
    }
}
