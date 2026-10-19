using System;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// Schreiben als Word-Dokument / E-Mail-Entwurf aus KI-Antworten
    /// (LexWolf.Services.DokumentExport).
    /// </summary>
    public class DokumentExportTests
    {
        private const string TestHinweis =
            "\n\n---\n⚠️ Testmodus: Diese Antwort wurde lokal über Claude Code auf dem " +
            "Entwicklungsrechner erzeugt, nicht über die produktive KI-Anbindung.";

        [Fact]
        public void ParseEmail_LiestAnUndBetreffUndEntferntTesthinweis()
        {
            var antwort = "An: m.mueller@kanzlei-beispiel.de\nBetreff: Unterhalt Dilara\n\nSehr geehrte Frau Müller,\n\nanbei …\n\nMit freundlichen Grüßen" + TestHinweis;

            var e = DokumentExport.ParseEmail(antwort);

            Assert.Equal("m.mueller@kanzlei-beispiel.de", e.An);
            Assert.Equal("Unterhalt Dilara", e.Betreff);
            Assert.StartsWith("Sehr geehrte Frau Müller,", e.Text);
            Assert.EndsWith("Mit freundlichen Grüßen", e.Text);
            Assert.DoesNotContain("Testmodus", e.Text);
        }

        [Fact]
        public void ParseEmail_Versteht_FetteKopfzeilen_UndEntferntMarkdown()
        {
            var antwort = "**An:** Frau Müller <m@x.de>\n**Betreff:** Termin\n\n## Hinweis\nBitte **bis Freitag** antworten.";

            var e = DokumentExport.ParseEmail(antwort);

            Assert.Equal("Frau Müller <m@x.de>", e.An);
            Assert.Equal("Termin", e.Betreff);
            Assert.Equal("Hinweis\nBitte bis Freitag antworten.", e.Text);
        }

        [Fact]
        public void ParseEmail_OhneKopfzeilen_IstAllesText()
        {
            var e = DokumentExport.ParseEmail("Sehr geehrte Damen und Herren,\nText.");

            Assert.Equal("", e.An);
            Assert.Equal("", e.Betreff);
            Assert.Equal("Sehr geehrte Damen und Herren,\nText.", e.Text);
        }

        [Fact]
        public void EmailAdressen_NurEchteAdressen()
        {
            Assert.Empty(DokumentExport.EmailAdressen("Gegnerische Kanzlei"));
            Assert.Equal(new[] { "a@b.de", "c.d@e-f.com" },
                DokumentExport.EmailAdressen("Herr A <a@b.de>, c.d@e-f.com; a@b.de"));
        }

        [Fact]
        public void Mailto_KodiertBetreffUndText_ZeilenumbruecheAlsCrLf()
        {
            var uri = DokumentExport.BaueMailtoUri(new EmailEntwurf("x@y.de", "Ä & B?", "Zeile 1\nZeile 2"));

            Assert.Equal("mailto:x@y.de?subject=%C3%84%20%26%20B%3F&body=Zeile%201%0D%0AZeile%202", uri);
        }

        [Theory]
        [InlineData(true, true, 10, EmailWeg.Mailto)]
        [InlineData(true, false, 10, EmailWeg.Mailto)]
        [InlineData(true, true, 5000, EmailWeg.MailtoTextInZwischenablage)]   // mailto vor .eml (neues Outlook: .eml nur lesbar)
        [InlineData(false, true, 5000, EmailWeg.Eml)]
        [InlineData(false, true, 10, EmailWeg.Eml)]
        [InlineData(false, false, 10, EmailWeg.NurZwischenablage)]
        public void WaehleEmailWeg_RichtetSichNachEingerichtetenProgrammen(bool mailto, bool eml, int textLaenge, EmailWeg erwartet)
        {
            var entwurf = new EmailEntwurf("x@y.de", "Betreff", new string('a', textLaenge));

            Assert.Equal(erwartet, DokumentExport.WaehleEmailWeg(entwurf, mailto, eml));
        }

        [Fact]
        public void Mailto_OhneText_NurEmpfaengerUndBetreff()
        {
            Assert.Equal("mailto:x@y.de?subject=Hallo",
                DokumentExport.BaueMailtoUri(new EmailEntwurf("x@y.de", "Hallo", "langer Text"), mitText: false));
        }

        [Fact]
        public void Zwischenablage_EnthaeltBetreffUndText()
        {
            Assert.Equal("Betreff: Termin\r\n\r\nZeile 1\r\nZeile 2",
                DokumentExport.AlsZwischenablageText(new EmailEntwurf("", "Termin", "Zeile 1\nZeile 2")));
            Assert.Equal("Nur Text", DokumentExport.AlsZwischenablageText(new EmailEntwurf("", "", "Nur Text")));
        }

        [Fact]
        public void SchreibeText_OhneMarkdownUndTesthinweis()
        {
            var pfad = Path.Combine(Path.GetTempPath(), $"lexwolf-test-{Guid.NewGuid():N}.txt");
            try
            {
                DokumentExport.SchreibeText(pfad, "# Antrag\nBetrag **349 €**" + TestHinweis);

                Assert.Equal("Antrag\r\nBetrag 349 €", File.ReadAllText(pfad, Encoding.UTF8));
                Assert.Equal("Schreiben_2026-10-12_220509.txt",
                    DokumentExport.DateinameFuerSchreiben(null, new DateTime(2026, 10, 12, 22, 5, 9), ".txt"));
            }
            finally
            {
                File.Delete(pfad);
            }
        }

        [Fact]
        public void NameNachUebernahme_MachtAusEmailEntwurfEineEmail()
        {
            Assert.Equal("E-Mail_2026-10-12_220509.eml", DokumentExport.NameNachUebernahme("E-Mail-Entwurf_2026-10-12_220509.eml"));
            Assert.Equal("Schreiben_2026-10-12_220509.docx", DokumentExport.NameNachUebernahme("Schreiben_2026-10-12_220509.docx"));
        }

        [Fact]
        public void FreierPfad_HaengtNummerAnWennDateiSchonExistiert()
        {
            var ordner = Path.Combine(Path.GetTempPath(), $"lexwolf-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(ordner);
            try
            {
                Assert.Equal(Path.Combine(ordner, "Schreiben.docx"), DokumentExport.FreierPfad(ordner, "Schreiben.docx"));
                File.WriteAllText(Path.Combine(ordner, "Schreiben.docx"), "");
                File.WriteAllText(Path.Combine(ordner, "Schreiben (2).docx"), "");
                Assert.Equal(Path.Combine(ordner, "Schreiben (3).docx"), DokumentExport.FreierPfad(ordner, "Schreiben.docx"));
            }
            finally
            {
                Directory.Delete(ordner, true);
            }
        }

        [Fact]
        public void SchreibeEmlNach_AktualisiertDenselbenEntwurf()
        {
            var pfad = Path.Combine(Path.GetTempPath(), $"lexwolf-test-{Guid.NewGuid():N}.eml");
            try
            {
                DokumentExport.SchreibeEmlNach(new EmailEntwurf("x@y.de", "Alt", "Erste Fassung"), pfad);
                DokumentExport.SchreibeEmlNach(new EmailEntwurf("x@y.de", "Neu", "Zweite Fassung"), pfad);

                var eml = File.ReadAllText(pfad);
                Assert.Contains($"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes("Neu"))}?=", eml);
                Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes("Alt")), eml);
            }
            finally
            {
                File.Delete(pfad);
            }
        }

        [Fact]
        public void Eml_IstEntwurfMitUtf8BetreffUndText()
        {
            var eml = DokumentExport.BaueEml(new EmailEntwurf("Frau X <x@y.de>", "Grüße", "Schöne Grüße\nIhr Anwalt"));

            Assert.Contains("X-Unsent: 1\r\n", eml);
            Assert.Contains("To: x@y.de\r\n", eml);
            Assert.Contains($"Subject: =?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes("Grüße"))}?=", eml);
            var body = eml.Split("\r\n\r\n", 2)[1].Replace("\r\n", "");
            Assert.Equal("Schöne Grüße\r\nIhr Anwalt", Encoding.UTF8.GetString(Convert.FromBase64String(body)));
        }

        [Fact]
        public void Dateiname_EnthaeltMandantOhneUngueltigeZeichen()
        {
            var name = DokumentExport.DateinameFuerSchreiben("Erkol, Ali / Akte?", new DateTime(2026, 10, 12, 22, 5, 9));

            Assert.Equal("Schreiben_Erkol,_Ali___Akte__2026-10-12_220509.docx", name);
            Assert.Equal("Schreiben_2026-10-12_220509.docx", DokumentExport.DateinameFuerSchreiben(null, new DateTime(2026, 10, 12, 22, 5, 9)));
        }

        [Fact]
        public void SchreibeDocx_UebernimmtTextFettUndAufzaehlung_OhneTesthinweis()
        {
            var pfad = Path.Combine(Path.GetTempPath(), $"lexwolf-test-{Guid.NewGuid():N}.docx");
            try
            {
                DokumentExport.SchreibeDocx(pfad,
                    "# Herabsetzungsschreiben\n\nSehr geehrte Frau Erkol,\nder Betrag von **349 €** bleibt.\n- Punkt eins" + TestHinweis);

                using var doc = WordprocessingDocument.Open(pfad, false);
                var absaetze = doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>().ToList();
                var texte = absaetze.Select(p => string.Concat(p.Descendants<Text>().Select(t => t.Text))).ToList();

                Assert.Equal(new[] { "Herabsetzungsschreiben", "", "Sehr geehrte Frau Erkol,", "der Betrag von 349 € bleibt.", "• Punkt eins" }, texte);
                var fett = absaetze[3].Elements<Run>().Where(r => r.RunProperties?.Bold is not null)
                    .Select(r => r.InnerText).ToList();
                Assert.Equal(new[] { "349 €" }, fett);
                Assert.NotNull(absaetze[0].Elements<Run>().Single().RunProperties?.Bold);
                // Schema-gültig, sonst meldet Word beim Öffnen "unlesbaren Inhalt".
                Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc));
            }
            finally
            {
                File.Delete(pfad);
            }
        }
    }
}
