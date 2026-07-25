using System;
using System.IO;
using Xunit;

namespace Anonymisierer.Tests
{
    public class AnonymizeFileNameTests
    {
        private static string NewTempFolder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "AnonymizerFileNameTest_" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            return folder;
        }

        [Fact]
        public void Unterstrich_Getrennter_Dateiname_Wird_Anonymisiert()
        {
            var tempFolder = NewTempFolder();
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                // Namen zunächst im Mapping registrieren, wie es beim Batch-Export passiert.
                Anonymizer.AnonymizeText("Sehr geehrter Herr Ali Erkol, ...", out _);

                var result = Anonymizer.AnonymizeFileName("Schreiben_Erkol_Ali_2024.pdf");

                Assert.DoesNotContain("Erkol", result, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Ali", result, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Name_Direkt_Vor_Datumsziffern_Wird_Anonymisiert()
        {
            var tempFolder = NewTempFolder();
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                Anonymizer.AnonymizeText("Sehr geehrter Herr Ali Erkol, ...", out _);

                var result = Anonymizer.AnonymizeFileName("Ali_Erkol2024_Akte.pdf");

                Assert.DoesNotContain("Erkol", result, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Ali", result, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Dateiname_Mit_Nachname_Allein_Wird_Anonymisiert()
        {
            var tempFolder = NewTempFolder();
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                // Person wird im Batch anderswo mit vollem Namen erkannt, registriert
                // dabei auch den Nachnamen allein (siehe StorePersonAlias).
                Anonymizer.AnonymizeText("Sehr geehrter Herr Ali Erkol, ...", out _);

                var result = Anonymizer.AnonymizeFileName("31_Erkol_Mietvertrag.pdf");

                Assert.DoesNotContain("Erkol", result, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }
    }
}
