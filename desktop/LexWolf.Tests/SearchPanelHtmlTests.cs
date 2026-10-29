using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// T#320: Live-Suchschritte werden nicht mehr als aufklappbares Panel im Chat
    /// angezeigt, sondern als kompakter Link, der ein separates WPF-Fenster öffnet.
    /// Diese Tests prüfen die reine HTML-Erzeugung in ChatHtmlRenderer — ohne
    /// WebView2/Netzwerk.
    /// </summary>
    public class SearchPanelHtmlTests
    {
        [Fact]
        public void SearchStepRowHtml_GefundenerTreffer_ZeigtQuelleUndKonfidenz()
        {
            var html = ChatHtmlRenderer.SearchStepRowHtml(
                "suchergebnis", "Gesetzestext: Zivilrecht", "§ 573c BGB", 0.87, "gefunden");

            Assert.Contains("such-gefunden", html);
            Assert.Contains("Gesetzestext: Zivilrecht", html);
            Assert.Contains("573c BGB", html);
            Assert.Contains("87%", html);
        }

        [Fact]
        public void SearchStepRowHtml_VerworfenerTreffer_MarkiertAlsVerworfen()
        {
            var html = ChatHtmlRenderer.SearchStepRowHtml(
                "suchergebnis", "Datenbank", "irrelevant", 0.1, "verworfen");

            Assert.Contains("such-verworfen", html);
        }

        [Fact]
        public void WrapSearchPanel_Laufend_ZeigtKompaktenButton()
        {
            var html = ChatHtmlRenderer.WrapSearchPanel("<div>Schritt</div>", laufend: true, modus: "tief", gefundenCount: 0, panelIndex: 3);

            Assert.DoesNotContain("<details", html);
            Assert.DoesNotContain("<div>Schritt</div>", html);
            Assert.Contains("data-action='recherche'", html);
            Assert.Contains("data-msg='3'", html);
            Assert.Contains("Recherche l&#228;uft", html);
            Assert.Contains("Modus: tief", html);
        }

        [Fact]
        public void WrapSearchPanel_Fertig_ZeigtQuellenAnzahl()
        {
            var html = ChatHtmlRenderer.WrapSearchPanel("<div>Schritt</div>", laufend: false, modus: "schnell", gefundenCount: 3, panelIndex: 7);

            Assert.DoesNotContain("<details", html);
            Assert.Contains("data-action='recherche'", html);
            Assert.Contains("data-msg='7'", html);
            Assert.Contains("Recherche ansehen (3 Quelle(n))", html);
        }

        [Fact]
        public void WrapSearchPanel_FertigOhneQuellen_ZeigtKeineQuellen()
        {
            var html = ChatHtmlRenderer.WrapSearchPanel("<div>Schritt</div>", laufend: false, modus: "schnell", gefundenCount: 0, panelIndex: 2);

            Assert.Contains("Recherche ansehen (keine Quellen)", html);
        }

        [Fact]
        public void BuildRechercheDialogHtml_MitAnonymisiertGesendet_ZeigtEigenenAbschnitt()
        {
            var html = ChatHtmlRenderer.BuildRechercheDialogHtml(
                "<div class='such-schritt'>Schritt</div>", laufend: false, modus: "schnell",
                gefundenCount: 1, anonymisiertGesendet: "Für [PERSON_1] prüfen.");

            Assert.Contains("recherche-dialog", html);
            Assert.Contains("such-liste", html);
            Assert.Contains("such-anonym", html);
            Assert.Contains("Anonymisiert gesendet", html);
            Assert.Contains("[PERSON_1]", html);
        }

        [Fact]
        public void BuildRechercheDialogHtml_OhneAnonymisiertGesendet_ZeigtKeinenAbschnitt()
        {
            var html = ChatHtmlRenderer.BuildRechercheDialogHtml(
                "", laufend: true, modus: "schnell", gefundenCount: 0, anonymisiertGesendet: null);

            Assert.DoesNotContain("such-anonym", html);
            Assert.DoesNotContain("Anonymisiert gesendet", html);
        }

        [Fact]
        public void BuildRechercheDialogHtml_AnonymisiertGesendet_EscapedHtml()
        {
            var html = ChatHtmlRenderer.BuildRechercheDialogHtml(
                "", laufend: false, modus: "schnell", gefundenCount: 0,
                anonymisiertGesendet: "<script>alert(1)</script>");

            Assert.DoesNotContain("<script>", html);
            Assert.Contains("&lt;script&gt;", html);
        }

        [Fact]
        public void BuildRechercheDialogHtml_Laufend_ZeigtLaufendenStatus()
        {
            var html = ChatHtmlRenderer.BuildRechercheDialogHtml(
                "", laufend: true, modus: "tief", gefundenCount: 0, anonymisiertGesendet: null);

            Assert.Contains("Live-Suche l&#228;uft", html);
            Assert.Contains("Modus: tief", html);
        }
    }
}
