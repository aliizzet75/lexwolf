using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Anonymisierer.Tests
{
    // Regressionstest fuer Task #207: Die Personen-Erkennung darf generische deutsche
    // Substantive/Komposita nicht mehr als Type-1-Person erkennen. Der Filter arbeitet
    // strukturell (Suffix/Praefix + Abgleich gegen deutschen Wortschatz) statt mit einer
    // starren Aufzaehlung einzelner falsch erkannter Woerter.
    public class StructuralFalsePositivePersonTests
    {
        private static string NewTempFolder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "AnonymizerStructuralFPPersonTest_" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            return folder;
        }

        // Hilfsmethode, um die private Filterlogik direkt zu pruefen, ohne NER zu mocken.
        // Der Rueckgabewert true bedeutet: das Token wuerde als generisches deutsches
        // Wort/Substantiv aus der Personenerkennung ausgeschlossen.
        private static bool IsGermanCommonWordOrFragment(string text)
        {
            var method = typeof(Anonymizer).GetMethod("IsGermanCommonWordOrFragment",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (bool)method!.Invoke(null, new object[] { text })!;
        }

        [Fact]
        public void Generische_Substantivkomposita_Werden_Nicht_Als_Person_Erkannt()
        {
            var tempFolder = NewTempFolder();
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                var original =
                    "abzurechnenden Wirtschaftsjahrs, Waermecontracting, Elektroheizgeraete, " +
                    "Betriebskostenpauschale, Schornsteinreinigungskosten, Waehrungsangaben, " +
                    "Bruttoarbeitslohn, Haushaltsnahe, Arztbehandlungskosten, Veranlagungsart, Hauptvordruck";

                var anonymized = Anonymizer.AnonymizeText(original, out var entities);

                var personEntities = entities.Where(e => e.Type == EntityType.Person).ToList();

                Assert.DoesNotContain(personEntities, e => e.Text.Contains("Wirtschaftsjahrs"));
                Assert.DoesNotContain(personEntities, e => e.Text.Contains("Waermecontracting"));
                Assert.DoesNotContain(personEntities, e => e.Text.Contains("Elektroheizgeraete"));
                Assert.DoesNotContain(personEntities, e => e.Text.Contains("Betriebskostenpauschale"));
                Assert.DoesNotContain(personEntities, e => e.Text.Contains("Schornsteinreinigungskosten"));
                Assert.DoesNotContain(personEntities, e => e.Text.Contains("Waehrungsangaben"));
                Assert.DoesNotContain(personEntities, e => e.Text.Contains("Bruttoarbeitslohn"));
                Assert.DoesNotContain(personEntities, e => e.Text.Contains("Haushaltsnahe"));
                Assert.DoesNotContain(personEntities, e => e.Text.Contains("Arztbehandlungskosten"));
                Assert.DoesNotContain(personEntities, e => e.Text.Contains("Veranlagungsart"));
                Assert.DoesNotContain(personEntities, e => e.Text.Contains("Hauptvordruck"));

                Assert.DoesNotContain("[", anonymized);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Hochfrequente_Deutsche_Woerter_Werden_Als_Generisches_Substantiv_Abgelehnt()
        {
            // "Will" kommt in den echten Testdaten als isoliertes Wort vor und wurde von
            // spaCy fälschlich als PER erkannt. Es ist ein sehr häufiges deutsches Wort
            // (Top 100 der Frequenzliste) und wird daher als generisches Wort abgelehnt.
            Assert.True(IsGermanCommonWordOrFragment("Will"));
            Assert.True(IsGermanCommonWordOrFragment("will"));

            // "Wille" / "wille" sind ebenfalls im deutschen Wortschatz, aber nicht in den
            // Top 100; sie werden nur abgelehnt, wenn sie ein Substantiv-Suffix tragen.
            // Da das nicht der Fall ist, bleiben sie erhalten (konservatives Verhalten).
            Assert.False(IsGermanCommonWordOrFragment("Wille"));
        }

        [Fact]
        public void Echte_Personennamen_Werden_Trotz_Strukturellem_Filter_Als_Person_Erkannt()
        {
            var tempFolder = NewTempFolder();
            try
            {
                Anonymizer.SetMappingFile(tempFolder);

                // Einzelne lose Vornamen/Nachnamen ohne Anrede werden nicht immer von der
                // Anrede-Regex erfasst. Der strukturelle Filter darf sie aber auch nicht
                // fälschlich als generisches Substantiv ablehnen, wenn spaCy sie als PER
                // liefert. Wir prüfen deshalb mehrere echte Namen in Anrede-Kontext, der
                // unabhängig von NER funktioniert.
                var original = "Herr Erkol, Frau Schapmann, Frau Dilara, Herr Maysa, Frau Samir, Frau Melisa und Frau Ruck.";
                var anonymized = Anonymizer.AnonymizeText(original, out var entities);

                var personNames = entities.Where(e => e.Type == EntityType.Person).Select(e => e.Text).ToList();

                Assert.Contains("Erkol", personNames);
                Assert.Contains("Schapmann", personNames);
                Assert.Contains("Dilara", personNames);
                Assert.Contains("Maysa", personNames);
                Assert.Contains("Samir", personNames);
                Assert.Contains("Melisa", personNames);
                Assert.Contains("Ruck", personNames);

                Assert.DoesNotContain("Erkol", anonymized);
                Assert.DoesNotContain("Schapmann", anonymized);
                Assert.DoesNotContain("Ruck", anonymized);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Echte_Personennamen_Sind_Nicht_Im_Deutschen_Wortschatz()
        {
            // Kontroll-Test: Die aus den Testdaten bekannten Personennamen dürfen nicht
            // fälschlicherweise im deutschen Wortschatz erkannt werden, sonst wuerde die
            // Wortlisten-Prüfung sie gefährden.
            Assert.False(IsGermanCommonWordOrFragment("Erkol"));
            Assert.False(IsGermanCommonWordOrFragment("Schapmann"));
            Assert.False(IsGermanCommonWordOrFragment("Dilara"));
            Assert.False(IsGermanCommonWordOrFragment("Maysa"));
            Assert.False(IsGermanCommonWordOrFragment("Melisa"));
            Assert.False(IsGermanCommonWordOrFragment("Izzet"));
        }
    }
}
