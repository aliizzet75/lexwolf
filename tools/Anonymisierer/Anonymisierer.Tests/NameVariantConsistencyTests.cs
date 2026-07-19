using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Anonymisierer.Tests
{
    // Regressionstest fuer Task #203: TryFindAliasForNameVariant lieferte fuer verschiedene
    // Namensfragmente derselben Person (z.B. "Ali Izzet" vs. "Ali Izzet Erkol") inkonsistente
    // Aliase, weil der Abgleich rein ueber Nachname+Zusatz-Token lief und fehlschlug, sobald
    // der Nachname im kuerzeren Fragment gar nicht vorkam (Vorname-only bzw. Vorname+Zwischenname
    // ohne Nachname). Gefunden beim Mapping-Review vom 2026-07-19.
    //
    // Die Assertions filtern gezielt auf die Namensfragmente, um die die es hier geht, statt auf
    // ALLE Person-Entitaeten des Dokuments zu pruefen: der echte NER-Endpoint (spaCy) markiert
    // gelegentlich unabhaengige, fuer diesen Test irrelevante Woerter (z.B. Grussformeln) als PER
    // -- das ist ein separates NER-Modell-Rauschen und kein Symptom von Task #203.
    public class NameVariantConsistencyTests
    {
        private static string NewTempFolder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "AnonymizerNameVariantTest_" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            return folder;
        }

        [Fact]
        public void Ali_Izzet_Und_Ali_Izzet_Erkol_Erhalten_Denselben_Alias()
        {
            var tempFolder = NewTempFolder();
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                var original =
                    "Sehr geehrter Herr Ali Izzet Erkol,\n\n" +
                    "wir bestaetigen den Eingang Ihres Schreibens.\n\n" +
                    "Herr Ali Izzet wird gebeten, die fehlenden Unterlagen nachzureichen.\n\n" +
                    "Mit freundlichen Gruessen";

                var anonymized = Anonymizer.AnonymizeText(original, out var entities);

                var variantEntities = entities
                    .Where(e => e.Type == EntityType.Person &&
                                (e.Text == "Ali Izzet Erkol" || e.Text == "Ali Izzet"))
                    .ToList();
                Assert.True(variantEntities.Count == 2,
                    $"Erwartet Treffer fuer beide Namensfragmente, gefunden: " +
                    string.Join(", ", variantEntities.Select(e => e.Text)));

                var aliases = variantEntities.Select(e => e.AnonymizedText).Distinct().ToList();
                Assert.True(aliases.Count == 1,
                    $"'Ali Izzet' und 'Ali Izzet Erkol' haben unterschiedliche Aliase erhalten: " +
                    string.Join(", ", variantEntities.Select(e => $"'{e.Text}'->{e.AnonymizedText}")));

                Assert.DoesNotContain("Ali Izzet", anonymized);
                Assert.DoesNotContain("Erkol", anonymized);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Ali_Izzet_Erkol_Zuerst_Dann_Ali_Izzet_Erhalten_Denselben_Alias()
        {
            // Umgekehrte Reihenfolge zum vorigen Test: das laengere Fragment wird zuerst
            // registriert. Der Abgleich darf nicht von der Verarbeitungsreihenfolge abhaengen.
            var tempFolder = NewTempFolder();
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                var original =
                    "Herr Ali Izzet wird um Rueckmeldung gebeten.\n\n" +
                    "Sehr geehrter Herr Ali Izzet Erkol, vielen Dank fuer Ihre Anfrage.\n\n" +
                    "Mit freundlichen Gruessen";

                Anonymizer.AnonymizeText(original, out var entities);

                var variantEntities = entities
                    .Where(e => e.Type == EntityType.Person &&
                                (e.Text == "Ali Izzet Erkol" || e.Text == "Ali Izzet"))
                    .ToList();
                Assert.True(variantEntities.Count == 2,
                    $"Erwartet Treffer fuer beide Namensfragmente, gefunden: " +
                    string.Join(", ", variantEntities.Select(e => e.Text)));

                var aliases = variantEntities.Select(e => e.AnonymizedText).Distinct().ToList();
                Assert.True(aliases.Count == 1,
                    $"Namensfragmente in umgekehrter Reihenfolge haben unterschiedliche Aliase erhalten: " +
                    string.Join(", ", variantEntities.Select(e => $"'{e.Text}'->{e.AnonymizedText}")));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Mehrere_Namensvarianten_Derselben_Person_Erhalten_Konsistent_Denselben_Alias()
        {
            var tempFolder = NewTempFolder();
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                var original =
                    "Sehr geehrter Herr Ali Izzet Erkol,\n\n" +
                    "Herr Ali Erkol hat sich am 15.06.1985 gemeldet.\n\n" +
                    "Herr Ali Izzet wurde ebenfalls informiert.\n\n" +
                    "Mit freundlichen Gruessen";

                Anonymizer.AnonymizeText(original, out var entities);

                var variantEntities = entities
                    .Where(e => e.Type == EntityType.Person &&
                                (e.Text == "Ali Izzet Erkol" || e.Text == "Ali Erkol" || e.Text == "Ali Izzet"))
                    .ToList();
                Assert.True(variantEntities.Count == 3,
                    $"Erwartet Treffer fuer alle 3 Namensvarianten, gefunden: " +
                    string.Join(", ", variantEntities.Select(e => e.Text)));

                var aliases = variantEntities.Select(e => e.AnonymizedText).Distinct().ToList();
                Assert.True(aliases.Count == 1,
                    $"Namensvarianten derselben Person haben unterschiedliche Aliase erhalten: " +
                    string.Join(", ", variantEntities.Select(e => $"'{e.Text}'->{e.AnonymizedText}")));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Zwei_Verschiedene_Personen_Gleichen_Vornamens_Werden_Nicht_Faelschlich_Zusammengefuehrt()
        {
            var tempFolder = NewTempFolder();
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                var original =
                    "Sehr geehrter Herr Ali Izzet Erkol,\n\n" +
                    "wir informieren Sie ueber den Vorgang.\n\n" +
                    "Herr Ali Yilmaz hat ebenfalls Kontakt aufgenommen.\n\n" +
                    "Mit freundlichen Gruessen";

                Anonymizer.AnonymizeText(original, out var entities);

                var personEntities = entities.Where(e => e.Type == EntityType.Person).ToList();
                var erkolEntity = personEntities.FirstOrDefault(e => e.Text.Contains("Erkol"));
                var yilmazEntity = personEntities.FirstOrDefault(e => e.Text.Contains("Yilmaz"));

                Assert.NotNull(erkolEntity);
                Assert.NotNull(yilmazEntity);
                Assert.NotEqual(erkolEntity!.AnonymizedText, yilmazEntity!.AnonymizedText);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }
    }
}
