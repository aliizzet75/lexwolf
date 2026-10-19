using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// Entwurf-Block in KI-Antworten (ChatEntwurfParser) und Entwurfskarte im Chat
    /// (ChatHtmlRenderer.WrapEntwurfKarte).
    /// </summary>
    public class ChatEntwurfTests
    {
        [Fact]
        public void Zerlege_TrenntBlockVomChatText()
        {
            var antwort = "Gerne, hier der Entwurf.\n\n" +
                          "<<<ENTWURF art=\"brief\" titel=\"Stellungnahme an RA Dr. Klein\" an=\"RA Dr. Klein\" betreff=\"Unterhalt\" ueberarbeitung=\"nein\">>>\n" +
                          "Sehr geehrter Herr Kollege,\n\nwir nehmen Stellung …\n\nMit freundlichen Grüßen\n" +
                          "<<<ENDE>>>\n\nBitte das Aktenzeichen ergänzen.";

            var (rest, e) = ChatEntwurfParser.Zerlege(antwort);

            Assert.NotNull(e);
            Assert.Equal("brief", e!.Art);
            Assert.Equal("Stellungnahme an RA Dr. Klein", e.Titel);
            Assert.Equal("RA Dr. Klein", e.An);
            Assert.False(e.Ueberarbeitung);
            Assert.StartsWith("Sehr geehrter Herr Kollege,", e.Text);
            Assert.EndsWith("Mit freundlichen Grüßen", e.Text);
            Assert.Equal("Gerne, hier der Entwurf.\n\nBitte das Aktenzeichen ergänzen.", rest);
        }

        [Fact]
        public void Zerlege_EmailMitUeberarbeitung_OhneEnde()
        {
            var antwort = "<<<ENTWURF art=\"email\" titel=\"Termin\" an=\"m@x.de\" betreff=\"Termin am Freitag\" ueberarbeitung=\"ja\">>>\nHallo Frau Müller,\nkürzer gefasst …";

            var (rest, e) = ChatEntwurfParser.Zerlege(antwort);

            Assert.True(e!.IstEmail);
            Assert.True(e.Ueberarbeitung);
            Assert.Equal("Termin am Freitag", e.AlsEmail().Betreff);
            Assert.Equal("m@x.de", e.AlsEmail().An);
            Assert.EndsWith("kürzer gefasst …", e.Text);
            Assert.Equal("", rest);
        }

        [Fact]
        public void Zerlege_EmailOhneAttribute_LiestKopfzeilenImBlock()
        {
            var antwort = "<<<ENTWURF art=\"email\">>>\nAn: m@x.de\nBetreff: Unterlagen\n\nBitte senden Sie …\n<<<ENDE>>>";

            var (_, e) = ChatEntwurfParser.Zerlege(antwort);

            Assert.Equal("m@x.de", e!.An);
            Assert.Equal("Unterlagen", e.Betreff);
            Assert.Equal("Unterlagen", e.Titel);
            Assert.Equal("Bitte senden Sie …", e.Text);
        }

        [Fact]
        public void Zerlege_OhneBlock_FrageBleibtChatText()
        {
            var (rest, e) = ChatEntwurfParser.Zerlege("Die Frist beträgt einen Monat.");

            Assert.Null(e);
            Assert.Equal("Die Frist beträgt einen Monat.", rest);
        }

        [Fact]
        public void Zerlege_OhneBlock_StichwortErkennungWirdGanzeAntwort()
        {
            var (rest, e) = ChatEntwurfParser.Zerlege("An: m@x.de\nBetreff: Termin\n\nSehr geehrte Frau Müller,", "erstelle_email");

            Assert.Equal("", rest);
            Assert.Equal("Termin", e!.Betreff);
            Assert.StartsWith("Sehr geehrte Frau Müller,", e.Text);
        }

        [Fact]
        public void Zerlege_EntferntTesthinweisAusEntwurf()
        {
            var antwort = "<<<ENTWURF art=\"brief\" titel=\"X\">>>\nText\n<<<ENDE>>>\n\n---\n⚠️ Testmodus: lokal erzeugt.";

            var (rest, e) = ChatEntwurfParser.Zerlege(antwort);

            Assert.Equal("Text", e!.Text);
            Assert.DoesNotContain("Testmodus", rest);
        }

        [Fact]
        public void Zerlege_ZitatpruefungBleibtImChatUndNichtImEntwurf()
        {
            var antwort = "<<<ENTWURF art=\"brief\" titel=\"X\">>>\nGemäß § 1361 BGB …\n<<<ENDE>>>" +
                          "\n\n---\n🔎 Zitatprüfung:\n- ✓ § 1361 BGB — in der Datenbank gefunden" +
                          "\n\n---\n⚠️ Testmodus: lokal erzeugt.";

            var (rest, e) = ChatEntwurfParser.Zerlege(antwort);

            Assert.Equal("Gemäß § 1361 BGB …", e!.Text);
            Assert.StartsWith("🔎 Zitatprüfung:", rest);
            Assert.DoesNotContain("Testmodus", rest);
        }

        [Fact]
        public void Zerlege_ZitatpruefungBeiStichwortErkennungNichtInDerEmail()
        {
            var antwort = "An: m@x.de\nBetreff: Termin\n\nSehr geehrte Frau Müller,\nnach § 1361 BGB …" +
                          "\n\n---\n🔎 Zitatprüfung:\n- ✓ § 1361 BGB — in der Datenbank gefunden";

            var (rest, e) = ChatEntwurfParser.Zerlege(antwort, "erstelle_email");

            Assert.DoesNotContain("Zitatprüfung", e!.Text);
            Assert.StartsWith("🔎 Zitatprüfung:", rest);
        }

        [Fact]
        public void AlsKlartext_OhneMarkierungen()
        {
            var antwort = "Hier der Entwurf.\n<<<ENTWURF art=\"email\" an=\"m@x.de\" betreff=\"Termin\">>>\nHallo,\n<<<ENDE>>>";

            var text = ChatEntwurfParser.AlsKlartext(antwort);

            Assert.DoesNotContain("<<<", text);
            Assert.Equal("Hier der Entwurf.\n\nAn: m@x.de\nBetreff: Termin\n\nHallo,", text);
        }

        [Fact]
        public void Karte_ZeigtArtAktionenUndMandantAuswahl()
        {
            var e = new ChatEntwurf("brief", "Stellungnahme <Klein>", "RA Klein", "", "Sehr geehrter Herr Kollege,\nText");
            var html = ChatHtmlRenderer.WrapEntwurfKarte(new EntwurfKarteAnsicht(
                3, e, "Noch keinem Mandanten zugeordnet.", Aktiv: true, ArtUmschaltbar: true, AblegenMoeglich: false,
                OeffnenLabel: "📄 In Word öffnen", MandantAuswahl: new[] { "Schmidt, Anna" }));

            Assert.Contains("id='ek-3'", html);
            Assert.Contains("Stellungnahme &lt;Klein&gt;", html);
            Assert.Contains("data-action='art' data-value='email' data-msg='3'", html);
            Assert.Contains("data-action='oeffnen' data-msg='3'", html);
            Assert.Contains("data-action='zuordnen' data-msg='3' data-select='ek-mandant-3'", html);
            Assert.Contains("<option value='Schmidt, Anna'>", html);
            Assert.DoesNotContain("data-action='uebernehmen'", html);
        }

        [Fact]
        public void Karte_Inaktiv_OhneAktionen()
        {
            var e = new ChatEntwurf("email", "Termin", "m@x.de", "Termin", "Hallo");
            var html = ChatHtmlRenderer.WrapEntwurfKarte(new EntwurfKarteAnsicht(
                1, e, "Durch eine neuere Fassung ersetzt.", Aktiv: false, ArtUmschaltbar: false, AblegenMoeglich: false, OeffnenLabel: null));

            Assert.Contains("entwurf-karte inaktiv", html);
            Assert.DoesNotContain("data-action", html);
        }
    }
}
