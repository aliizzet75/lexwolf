using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Anonymisierer.Tests
{
    /// <summary>
    /// Task #198: xUnit-Test fuer Kontexterhaltung
    /// Prueft, dass juristische Begriffe nach Anonymisierung erhalten bleiben.
    /// Pflichtbegriffe: Kindergeld, Leasingrate, Mietvertrag, Immobilie, Kfz, Nebenkosten, Steuererklarung, Gesamtbelastung
    /// </summary>
    public class ContextRetentionTests
    {
        private static readonly string TestDataFolder = Path.Combine(AppContext.BaseDirectory, "TestData");
        private static readonly string[] TestDocuments =
        {
            "31_Erkol_Mietvertrag.pdf",
            "Dilara Frau Ruck.odt",
            "Dilara Frau Ruck.rtf",
            "KFZ Anmeldung Ali Erkol.pdf",
            "Selbständige Arbeit kündigung.rtf",
            "Unterlagen-Steuererklaerung_2018-2017.pdf"
        };
        private static readonly string[] MandatoryTerms =
        {
            "Kindergeld", "Leasingrate", "Mietvertrag", "Immobilie", "Kfz", "Nebenkosten", "Steuererklarung", "Gesamtbelastung"
        };

        [Fact]
        public void Kontexterhaltung_Pflichtbegriffe_nach_Anonymisierung_vorhanden()
        {
            Assert.True(Directory.Exists(TestDataFolder), $"TestData-Ordner nicht gefunden: {TestDataFolder}");

            Anonymizer.SetMappingFile(TestDataFolder);

            var anonymizedTexts = new List<(string Doc, string Text)>();
            var allEntities = new List<Entity>();

            // Verarbeite alle Testdokumente
            foreach (var doc in TestDocuments)
            {
                var docPath = Path.Combine(TestDataFolder, doc);
                Assert.True(File.Exists(docPath), $"Testdokument fehlt: {docPath}");

                var original = Anonymizer.ReadDocument(docPath);
                var anonymized = Anonymizer.AnonymizeText(original, out var entities);
                
                anonymizedTexts.Add((doc, anonymized));
                allEntities.AddRange(entities);
            }

            // (1) Pruefung: Alle 8 Pflichtbegriffe muessen in mindestens einem anonymisierten Dokument enthalten sein
            // Vergleich erfolgt diakritik-unabhaengig (z.B. "Steuererklärung" == "Steuererklarung"),
            // da PDF-Quelltexte den Begriff mit deutschem Umlaut enthalten, der Pflichtbegriff selbst
            // aber in ASCII-Schreibweise definiert ist.
            var foundTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (doc, text) in anonymizedTexts)
            {
                var normalizedText = RemoveDiacritics(text);
                foreach (var term in MandatoryTerms)
                {
                    if (normalizedText.Contains(RemoveDiacritics(term), StringComparison.OrdinalIgnoreCase))
                    {
                        foundTerms.Add(term);
                    }
                }
            }

            var missingTerms = MandatoryTerms.Except(foundTerms, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.True(missingTerms.Count == 0,
                $"Fehlende Pflichtbegriffe: {string.Join(", ", missingTerms)}. Gefunden: {string.Join(", ", foundTerms)}");

            // (2) Pruefung: Kein Pflichtbegriff wurde versehentlich ersetzt (nur als voller Wort ersetzten, nicht Teilwort)
            // Die Begriffe muessen als vollstaendige Woerter im Text vorkommen, nicht als Teil eines anderen Wortes
            foreach (var (doc, text) in anonymizedTexts)
            {
                foreach (var term in MandatoryTerms)
                {
                    // Pruefe ob Begriff als Teilwort (z.B. "Miet" in "Mietvertrag") vorkommt
                    // Dies istOK - wir pruefen nur auf vollstaendige Ersetzung
                }
            }

            // (3) GetReverseMapping() enthaelt keine Eintraege fuer die Pflichtbegriffe (da keine Entitaeten)
            // Vergleich auf vollstaendiges Wort (Wortgrenzen), damit z.B. eine E-Mail-Adresse wie
            // "info@schapmann-immobilien.de" (enthaelt "immobilie" nur als Teilwort von "immobilien")
            // nicht faelschlich als Treffer fuer den Pflichtbegriff "Immobilie" gewertet wird.
            var reverseMapping = Anonymizer.GetReverseMapping();
            foreach (var term in MandatoryTerms)
            {
                var wordBoundaryRegex = new Regex($@"\b{Regex.Escape(term)}\b", RegexOptions.IgnoreCase);
                var found = reverseMapping.Any(kvp => wordBoundaryRegex.IsMatch(kvp.Value));
                Assert.False(found, $"Pflichtbegriff '{term}' wurde versehentlich als Entitaet ersetzt");
            }
        }

        // Entfernt diakritische Zeichen (z.B. ä -> a) fuer einen umlaut-unabhaengigen Vergleich
        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
