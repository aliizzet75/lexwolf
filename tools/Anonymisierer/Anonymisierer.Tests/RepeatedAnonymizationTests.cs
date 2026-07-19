using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Anonymisierer.Tests
{
    // Regressionstest fuer Task #202: Alias-Verkettung bei erneuter Anonymisierung.
    // Realer Fund beim Review von .lexwolf_mapping.json am 2026-07-19:
    // Erkol -> Dr. Watson -> Prof. Moriarty -> Verleihnix -> Robin
    public class RepeatedAnonymizationTests
    {
        // Index von "Dr. Watson" im internen PersonPool (Anonymisierer.Core/Anonymizer.cs).
        // Per Reflection gesetzt, um den real beobachteten Kettenstart deterministisch zu
        // reproduzieren, statt vom Zufall der Verarbeitungsreihenfolge abzuhaengen.
        private const int DrWatsonPoolIndex = 34;

        private static void SetPersonIdx(int value)
        {
            typeof(Anonymizer)
                .GetField("_personIdx", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, value);
        }

        [Fact]
        public void Erneute_Anonymisierung_Erzeugt_Keine_Alias_Kette_Erkol_Watson_Moriarty()
        {
            var tempFolder = Path.Combine(Path.GetTempPath(), "AnonymizerRepeatTest_" + Guid.NewGuid());
            Directory.CreateDirectory(tempFolder);
            try
            {
                Anonymizer.SetMappingFile(tempFolder);
                SetPersonIdx(DrWatsonPoolIndex); // erzwingt Alias "[Dr. Watson]", wie im real beobachteten Fall

                var original = "Sehr geehrter Herr Erkol,\n\nvielen Dank fuer Ihre Anfrage.\n\nMit freundlichen Gruessen";

                var anon1 = Anonymizer.AnonymizeText(original, out _);
                Assert.Contains("[Dr. Watson]", anon1);

                // Zweiter/dritter Durchlauf auf dem BEREITS anonymisierten Text (z.B. erneutes
                // Verarbeiten desselben Dokuments) darf keine weiteren Aliase erzeugen.
                var anon2 = Anonymizer.AnonymizeText(anon1, out _);
                var anon3 = Anonymizer.AnonymizeText(anon2, out _);

                Assert.Equal(anon1, anon2);
                Assert.Equal(anon2, anon3);

                // Die konkret berichtete Kette darf an keiner Stelle entstehen.
                Assert.DoesNotContain("Moriarty", anon3);
                Assert.DoesNotContain("Verleihnix", anon3);
                Assert.DoesNotContain("Robin", anon3);
                Assert.DoesNotContain("[[", anon3);

                // Kein Mapping-Eintrag darf einen bereits erzeugten Alias als "Original" fuehren
                // (Symptom einer Kette: ein Alias wird selbst als neue PII erkannt und bekommt
                // wiederum einen eigenen Alias).
                var reverseMapping = Anonymizer.GetReverseMapping();
                Assert.DoesNotContain(reverseMapping.Values, orig => orig.Contains('['));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Platzhalter_Codes_Werden_Bei_Erneuter_Anonymisierung_Nicht_Als_Neue_Pii_Erkannt()
        {
            var tempFolder = Path.Combine(Path.GetTempPath(), "AnonymizerRepeatTest_" + Guid.NewGuid());
            Directory.CreateDirectory(tempFolder);
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                var original = "Steuernummer: 12345/67890\nWohnort: 70173 Stuttgart";
                var anon1 = Anonymizer.AnonymizeText(original, out _);
                Assert.Contains("STEUER-", anon1);
                Assert.Contains("ORT-", anon1);

                // Erneuter Durchlauf ueber den bereits anonymisierten Text: die Platzhalter-Codes
                // selbst duerfen nicht als frische PII erkannt und erneut ersetzt werden.
                var anon2 = Anonymizer.AnonymizeText(anon1, out var entities2);

                Assert.Equal(anon1, anon2);
                Assert.DoesNotContain(entities2, e => e.Text.Contains('['));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }
    }
}
