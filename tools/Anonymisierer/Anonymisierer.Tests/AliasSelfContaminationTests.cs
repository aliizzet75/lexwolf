using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Anonymisierer.Tests
{
    // Regressionstest fuer Task #208: Bereits erzeugte Alias-/Platzhalterwerte
    // (Adress-Aliase, Stadt-Aliase, Steuernummer-Codes, UST-Codes, ORT-Codes)
    // duerfen nicht erneut als Type:1 Person erkannt werden, wenn sie spaeter im
    // selben Batch (mit oder ohne eckige Klammern) noch einmal auftauchen.
    public class AliasSelfContaminationTests
    {
        private static string NewTempFolder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "AnonymizerSelfContaminationTest_" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            return folder;
        }

        [Fact]
        public void Bereits_Erzeugte_Adress_Alias_Fragmente_Werden_Nicht_Als_Person_Erkannt()
        {
            var tempFolder = NewTempFolder();
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                // Dokument 1 erzeugt mit 7 deutschen Adressen nacheinander alle gewuenschten
                // Pool-Aliase (AdressPool wird sequentiell durchlaufen).
                var doc1 =
                    "Hauptstraße 1, Bahnhofstraße 2, Berliner Straße 3, Musterweg 4, " +
                    "Dorfplatz 5, Waldallee 6 und Ringstraße 7. " +
                    "70173 Stuttgart, 70437 Stuttgart und 70435 Stuttgart.";
                var anon1 = Anonymizer.AnonymizeText(doc1, out var entities1);

                var adressAliases = entities1
                    .Where(e => e.Type == EntityType.Adresse)
                    .Select(e => e.AnonymizedText.Trim('[', ']'))
                    .ToList();
                Assert.NotEmpty(adressAliases);

                // Dokument 2 wiederholt bewusst die Alias-Fragmente in Anrede-Kontext.
                var doc2 = "Herr Baker Street, Frau Downing Street, Frau Auenland-Weg, " +
                           "Herr Moria-Pfad, Frau Rivendell-Allee und Frau Coruscant-Boulevard " +
                           "sind eingeladen. Auch Entenhausen soll kommen.";
                var anon2 = Anonymizer.AnonymizeText(doc2, out var entities2);

                var forbiddenPersons = new[]
                {
                    "Baker Street", "Downing Street", "Auenland-Weg", "Moria-Pfad",
                    "Rivendell-Allee", "Coruscant-Boulevard", "Entenhausen", "Street"
                };

                foreach (var forbidden in forbiddenPersons)
                {
                    Assert.DoesNotContain(entities2, e =>
                        e.Type == EntityType.Person && e.Text.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
                }

                // Die Alias-Fragmente duerfen nicht durch Personen-Aliase ersetzt worden sein
                // (keine neuen Klammern im anonymisierten Text an diesen Stellen).
                Assert.DoesNotContain("[Baker Street", anon2);
                Assert.DoesNotContain("[Downing Street", anon2);
                Assert.DoesNotContain("[Auenland-Weg", anon2);
                Assert.DoesNotContain("[Moria-Pfad", anon2);
                Assert.DoesNotContain("[Rivendell-Allee", anon2);
                Assert.DoesNotContain("[Coruscant-Boulevard", anon2);
                Assert.DoesNotContain("[Entenhausen", anon2);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Bereits_Erzeugte_Platzhalter_Codes_Werden_Nicht_Als_Person_Erkannt()
        {
            var tempFolder = NewTempFolder();
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                // Dokument 1 erzeugt Platzhalter-Codes.
                var doc1 = "Steuernummer 93060/15826 und USt-IdNr DE296426579.";
                var anon1 = Anonymizer.AnonymizeText(doc1, out var entities1);

                var placeholderAliases = entities1
                    .Where(e => e.Type == EntityType.Aktenzeichen)
                    .Select(e => e.AnonymizedText.Trim('[', ']'))
                    .ToList();
                Assert.True(placeholderAliases.Any(a => a.StartsWith("STEUER-", StringComparison.OrdinalIgnoreCase)),
                    $"Kein STEUER-Alias erzeugt. Gefunden: {string.Join(", ", placeholderAliases)}");

                // Dokument 2 wiederholt die Platzhalter-Codes.
                var doc2 = "Herr STEUER-1, Frau STEUER-2 und Frau ORT-3 sind geladen.";
                var anon2 = Anonymizer.AnonymizeText(doc2, out var entities2);

                Assert.DoesNotContain(entities2, e =>
                    e.Type == EntityType.Person &&
                    (e.Text.Contains("STEUER-1", StringComparison.OrdinalIgnoreCase) ||
                     e.Text.Contains("STEUER-2", StringComparison.OrdinalIgnoreCase) ||
                     e.Text.Contains("ORT-3", StringComparison.OrdinalIgnoreCase)));

                // Die Platzhalter-Codes wurden nicht als Personen-Aliase ersetzt und bleiben
                // (ggf. ohne eckige Klammern, falls sie nackt wiederholt wurden) im Text.
                Assert.Contains("STEUER-1", anon2);
                Assert.Contains("STEUER-2", anon2);
                Assert.Contains("ORT-3", anon2);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }
    }
}
