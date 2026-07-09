using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Anonymisierer.Tests
{
    public class CrossDocumentConsistencyTests
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
        private static readonly string[] NamesThatMustNotLeak =
        {
            "Erkol", "Ali Izzet", "Dilara", "Maysa", "Ruck"
        };

        [Fact]
        public void CrossDocument_Konsistenz_Aliase_Fuer_Erkol()
        {
            Assert.True(Directory.Exists(TestDataFolder), $"TestData-Ordner nicht gefunden: {TestDataFolder}");
            Anonymizer.SetMappingFile(TestDataFolder);

            var anonymizedTexts = new List<(string Doc, string Text)>();
            var allEntities = new List<Entity>();

            foreach (var doc in TestDocuments)
            {
                var docPath = Path.Combine(TestDataFolder, doc);
                Assert.True(File.Exists(docPath), $"Testdokument fehlt: {docPath}");
                var original = Anonymizer.ReadDocument(docPath);
                var anonymized = Anonymizer.AnonymizeText(original, out var entities);
                anonymizedTexts.Add((doc, anonymized));
                allEntities.AddRange(entities);
            }

            // Debug: Show all Person entities
            var personEntities = allEntities.Where(e => e.Type == EntityType.Person).ToList();
            Console.WriteLine($"Person entities ({personEntities.Count}):");
            foreach (var e in personEntities)
            {
                Console.WriteLine($"  '{e.Text}' -> {e.AnonymizedText}");
            }

            // Debug: Show all Person-Entitaeten containing 'erkol' (case-insensitive)
            // Nur Personen zaehlen - andere Entitaetstypen (z.B. eine E-Mail-Adresse wie
            // "a.erkol@gmx.de", die zufaellig die Zeichenfolge "erkol" enthaelt) duerfen die
            // Alias-Konsistenzpruefung fuer die Person "Erkol" nicht verfaelschen.
            var erkolEntities = personEntities
                .Where(e => e.Text.Contains("erkol", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Console.WriteLine($"Entities containing 'erkol': {erkolEntities.Count}");
            foreach (var e in erkolEntities)
            {
                Console.WriteLine($"  '{e.Text}' ({e.Type}) -> {e.AnonymizedText}");
            }

            // Debug: Show anonymized text for 31_Erkol_Mietvertrag.pdf
            var erkolDoc = anonymizedTexts.FirstOrDefault(t => t.Doc == "31_Erkol_Mietvertrag.pdf");
            if (!string.IsNullOrEmpty(erkolDoc.Text))
            {
                Console.WriteLine($"31_Erkol_Mietvertrag.pdf anonymized text: {erkolDoc.Text}");
            }

            // (1) Erkol bekommt in jedem Dokument denselben Alias.
            var erkolAliases = erkolEntities
                .Select(e => e.AnonymizedText)
                .Distinct()
                .ToList();
            Assert.True(erkolAliases.Count > 0, $"Keine 'Erkol'-Entitaeten gefunden. Gesamte Entities: {allEntities.Count}");
            Assert.True(erkolAliases.Count == 1,
                $"'Erkol' hat unterschiedliche Aliase erhalten: {string.Join(", ", erkolAliases)}");

            // (2) Die Klartext-Namen duerfen in keinem anonymisierten Dokument mehr vorkommen.
            foreach (var (doc, text) in anonymizedTexts)
            {
                foreach (var name in NamesThatMustNotLeak)
                {
                    if (text.Contains(name, StringComparison.Ordinal))
                    {
                        Console.WriteLine($"DEBUG: {doc} contains {name}");
                    }
                    Assert.False(text.Contains(name, StringComparison.Ordinal),
                        $"Klartext-Name '{name}' ist im anonymisierten Dokument '{doc}' noch enthalten.");
                }
            }

            // (3) GetReverseMapping() enthaelt genau einen Alias-Eintrag fuer die Person Erkol.
            // Gefiltert auf den unter (1) ermittelten Person-Alias, damit andere Entitaetstypen
            // (z.B. die E-Mail-Adresse "a.erkol@gmx.de", die zufaellig "erkol" enthaelt) die
            // Pruefung nicht verfaelschen.
            var reverseMapping = Anonymizer.GetReverseMapping();
            var erkolReverseEntries = reverseMapping
                .Where(kvp => erkolAliases.Contains(kvp.Key) && kvp.Value.Contains("erkol", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.True(erkolReverseEntries.Count == 1,
                $"Erwartet genau einen Reverse-Mapping-Eintrag fuer Erkol, gefunden: {erkolReverseEntries.Count}");
        }
    }
}
