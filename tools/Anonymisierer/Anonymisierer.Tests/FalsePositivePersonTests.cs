using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Anonymisierer.Tests
{
    // Regressionstest fuer Task #204: False-Positive Person-Erkennung bei gewoehnlichen
    // deutschen Woertern und silbengetrennten Zeilenumbruch-Fragmenten.
    // Realer Fund beim Mapping-Review vom 2026-07-19:
    //   Bescheinigt, Mieters, Parken, Wo-, Las-
    public class FalsePositivePersonTests
    {
        private static string NewTempFolder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "AnonymizerFPPersonTest_" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            return folder;
        }

        [Fact]
        public void Gewoehnliche_Deutsche_Woerter_Werden_Nicht_Als_Person_Erkannt()
        {
            var tempFolder = NewTempFolder();
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                var original =
                    "Bescheinigt wurde der Mietvertrag. " +
                    "Mieters Parken ist untersagt.";

                var anonymized = Anonymizer.AnonymizeText(original, out var entities);

                var personEntities = entities.Where(e => e.Type == EntityType.Person).ToList();

                Assert.DoesNotContain(personEntities, e => e.Text.Contains("Bescheinigt"));
                Assert.DoesNotContain(personEntities, e => e.Text.Contains("Mieters"));
                Assert.DoesNotContain(personEntities, e => e.Text.Contains("Parken"));

                Assert.DoesNotContain("[", anonymized);
                Assert.Equal(original, anonymized);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Zeilenumbruch_Silbentrennungen_Werden_Nicht_Als_Person_Erkannt()
        {
            var tempFolder = NewTempFolder();
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                // "Wo-" und "Las-" sind Fragmente, die in echten Dokumenten durch
                // Silbentrennung am Zeilenende entstehen (z.B. "Wohnung", "Lastschrift").
                var original =
                    "Die Wo-\n" +
                    "nung ist gemietet.\n\n" +
                    "Die Las-\n" +
                    "tschrift wurde eingezogen.";

                var anonymized = Anonymizer.AnonymizeText(original, out var entities);

                var personEntities = entities.Where(e => e.Type == EntityType.Person).ToList();
                Assert.DoesNotContain(personEntities, e => e.Text.Contains("Wo-"));
                Assert.DoesNotContain(personEntities, e => e.Text.Contains("Las-"));

                // Fragmente duerfen als Teil der Original-Woerter "Wohnung"/"Lastschrift"
                // weiterhin im Text vorkommen — sie duerfen nur nicht selbst als Personen
                // erkannt und durch Alias-Klammern ersetzt worden sein.
                Assert.DoesNotContain("[", anonymized);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Echte_Personennamen_Werden_Weiterhin_Als_Person_Erkannt()
        {
            var tempFolder = NewTempFolder();
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                var original = "Sehr geehrter Herr Max Mustermann, vielen Dank fuer Ihr Schreiben.";
                var anonymized = Anonymizer.AnonymizeText(original, out var entities);

                Assert.Contains(entities, e => e.Type == EntityType.Person && e.Text.Contains("Mustermann"));
                Assert.DoesNotContain("Max Mustermann", anonymized);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }
    }
}
