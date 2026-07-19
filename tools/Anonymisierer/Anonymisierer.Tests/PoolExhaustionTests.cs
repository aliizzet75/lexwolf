using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Anonymisierer.Tests
{
    // Regressionstest fuer Task #205: Betrags-/Datum-Pool erschoepfen darf nicht zu
    // Alias-Kollisionen fuehren, wenn mehr Werte im Dokument vorkommen als im festen
    // Pool vorhanden sind.
    public class PoolExhaustionTests
    {
        private static string NewTempFolder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "AnonymizerPoolExhaustionTest_" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            return folder;
        }

        [Fact]
        public void Mehr_Betraege_Als_Poolgroesse_Erhalten_Eindeutige_Aliase()
        {
            var tempFolder = NewTempFolder();
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                // Der _fakeBetraege-Pool hat 8 Eintraege; wir verwenden 15 verschiedene Betraege.
                var amounts = new[]
                {
                    "1.000,00", "2.000,00", "3.000,00", "4.000,00",
                    "5.000,00", "6.000,00", "7.000,00", "8.000,00",
                    "9.000,00", "10.000,00", "11.000,00", "12.000,00",
                    "13.000,00", "14.000,00", "15.000,00"
                };
                var original = string.Join(" und ", amounts);
                var anonymized = Anonymizer.AnonymizeText(original, out var entities);

                var betragEntities = entities.Where(e => e.Type == EntityType.Betrag).ToList();
                Assert.Equal(amounts.Length, betragEntities.Count);

                var aliases = betragEntities.Select(e => e.AnonymizedText).Distinct().ToList();
                Assert.Equal(amounts.Length, aliases.Count);

                foreach (var amount in amounts)
                    Assert.DoesNotContain(amount, anonymized);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Mehr_Daten_Als_Poolgroesse_Erhalten_Eindeutige_Aliase()
        {
            var tempFolder = NewTempFolder();
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                // Der _fakeDaten-Pool hat 8 Eintraege; wir verwenden 15 verschiedene Daten.
                var dates = new[]
                {
                    "01.01.2021", "02.01.2021", "03.01.2021", "04.01.2021",
                    "05.01.2021", "06.01.2021", "07.01.2021", "08.01.2021",
                    "09.01.2021", "10.01.2021", "11.01.2021", "12.01.2021",
                    "13.01.2021", "14.01.2021", "15.01.2021"
                };
                var original = "Daten: " + string.Join(", ", dates);
                var anonymized = Anonymizer.AnonymizeText(original, out var entities);

                var datumEntities = entities.Where(e => e.Type == EntityType.Datum).ToList();
                Assert.Equal(dates.Length, datumEntities.Count);

                var aliases = datumEntities.Select(e => e.AnonymizedText).Distinct().ToList();
                Assert.Equal(dates.Length, aliases.Count);

                foreach (var date in dates)
                    Assert.DoesNotContain(date, anonymized);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Mehr_Adressen_Als_Poolgroesse_Erhalten_Eindeutige_Aliase()
        {
            var tempFolder = NewTempFolder();
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                // AdressPool hat 18 Eintraege; wir verwenden 25.
                var streets = Enumerable.Range(1, 25)
                    .Select(i => $"Teststraße {i}")
                    .ToArray();
                var original = string.Join("\n", streets);
                var anonymized = Anonymizer.AnonymizeText(original, out var entities);

                var addressEntities = entities.Where(e => e.Type == EntityType.Adresse).ToList();
                Assert.Equal(streets.Length, addressEntities.Count);

                var aliases = addressEntities.Select(e => e.AnonymizedText).Distinct().ToList();
                Assert.Equal(streets.Length, aliases.Count);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }
    }
}
