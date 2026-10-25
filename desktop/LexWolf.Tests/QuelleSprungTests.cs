using System.Collections.Generic;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// Task #297: Klick auf eine Quelle im Chat springt nicht nur zum Dokumentanfang,
    /// sondern zur exakten Passage (chunk_id aus T#296). Testet die beiden reinen
    /// Bausteine ohne WPF-Fenster: die Positionssuche (ChatHtmlRenderer.
    /// FindeZitatPosition) und das Rendering der klickbaren Quellen-Links
    /// (ChatHtmlRenderer.WrapAiBubble).
    /// </summary>
    public class QuelleSprungTests
    {
        [Fact]
        public void FindeZitatPosition_FindetVollesZitatUndMarkiertUmgebendenSatz()
        {
            var text = "Einleitung. § 433 BGB regelt die vertragstypischen Pflichten beim Kaufvertrag. Weiterer Satz.";

            var (start, length) = ChatHtmlRenderer.FindeZitatPosition(text, "§ 433 BGB");

            Assert.True(start > 0, "Treffer darf nicht am Dokumentanfang liegen (Kernanforderung #297).");
            var passage = text.Substring(start, length);
            Assert.Contains("§ 433 BGB", passage);
            Assert.DoesNotContain("Einleitung", passage);
        }

        [Fact]
        public void FindeZitatPosition_FindetNurParagraphNummerWennVollesZitatImChunktextFehlt()
        {
            // Gesetzestexte nennen die Abkürzung (BGB) oft nicht direkt neben der Norm.
            var text = "Vorspann.\n§ 433 Vertragstypische Pflichten beim Kaufvertrag.\nFolgetext.";

            var (start, length) = ChatHtmlRenderer.FindeZitatPosition(text, "§ 433 BGB");

            Assert.True(start > 0);
            var passage = text.Substring(start, length);
            Assert.Contains("§ 433", passage);
        }

        [Fact]
        public void FindeZitatPosition_GibtNullZurueckWennZitatNichtImTextVorkommt()
        {
            var (start, length) = ChatHtmlRenderer.FindeZitatPosition("Ein Text ohne jeden Bezug.", "§ 999 ZPO");

            Assert.Equal(0, start);
            Assert.Equal(0, length);
        }

        [Fact]
        public void FindeZitatPosition_LeererTextOderLeeresZitatGibtNullZurueck()
        {
            Assert.Equal((0, 0), ChatHtmlRenderer.FindeZitatPosition("", "§ 1 BGB"));
            Assert.Equal((0, 0), ChatHtmlRenderer.FindeZitatPosition("Text", ""));
        }

        [Fact]
        public void WrapAiBubble_MitQuellen_EnthaeltKlickbarenQuellenLinkMitChunkId()
        {
            var quellen = new List<(string Zitat, int ChunkId)> { ("§ 433 BGB", 42) };

            var html = ChatHtmlRenderer.WrapAiBubble("<p class='chat-paragraph'>Antwort</p>", quellen: quellen);

            Assert.Contains("quelle-link", html);
            Assert.Contains("data-action='quelle'", html);
            Assert.Contains("data-msg='42'", html);
            // "§" wird von WebUtility.HtmlEncode als numerische Entity kodiert (&#167;) —
            // hier reicht der Nachweis, dass die Zitat-Nummer im Link-Text steht.
            Assert.Contains("433 BGB", html);
        }

        [Fact]
        public void WrapAiBubble_OhneQuellen_ZeigtKeineQuellenListe()
        {
            var html = ChatHtmlRenderer.WrapAiBubble("<p class='chat-paragraph'>Antwort</p>");

            Assert.DoesNotContain("quellen-liste", html);
        }
    }
}
