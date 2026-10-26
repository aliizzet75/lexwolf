using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// T#309: Live-Suchschritte inline im Chat (statt im separaten ReActDialog-
    /// Fenster). Testet die reine HTML-Erzeugung in ChatHtmlRenderer — ohne
    /// WebView2/Netzwerk, da hier nur Markup-Struktur verifiziert wird.
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
        public void WrapSearchPanel_Laufend_IstOffenUndZeigtModus()
        {
            var html = ChatHtmlRenderer.WrapSearchPanel("<div>Schritt</div>", laufend: true, modus: "tief", gefundenCount: 0);

            Assert.Contains("such-panel", html);
            Assert.Contains(" open", html);
            Assert.Contains("tief", html);
            Assert.Contains("<div>Schritt</div>", html);
        }

        [Fact]
        public void WrapSearchPanel_Fertig_IstZugeklapptUndZeigtAnzahl()
        {
            var html = ChatHtmlRenderer.WrapSearchPanel("<div>Schritt</div>", laufend: false, modus: "schnell", gefundenCount: 3);

            Assert.DoesNotContain("<details class='such-panel' open>", html);
            Assert.Contains("3 Quelle(n) gefunden", html);
        }

        // Task #311: Anonymisierten Text im selben Live-Panel wie die Suchschritte zeigen.
        [Fact]
        public void WrapSearchPanel_MitAnonymisiertGesendet_ZeigtEigenenAbschnitt()
        {
            var html = ChatHtmlRenderer.WrapSearchPanel("<div>Schritt</div>", laufend: false, modus: "schnell",
                gefundenCount: 1, anonymisiertGesendet: "Für [PERSON_1] prüfen.");

            Assert.Contains("such-anonym", html);
            Assert.Contains("Anonymisiert gesendet", html);
            Assert.Contains("[PERSON_1]", html);
        }

        [Fact]
        public void WrapSearchPanel_OhneAnonymisiertGesendet_ZeigtKeinenAbschnitt()
        {
            var html = ChatHtmlRenderer.WrapSearchPanel("<div>Schritt</div>", laufend: false, modus: "schnell", gefundenCount: 0);

            Assert.DoesNotContain("such-anonym", html);
        }

        [Fact]
        public void WrapSearchPanel_AnonymisiertGesendet_EscapedHtml()
        {
            var html = ChatHtmlRenderer.WrapSearchPanel("", laufend: false, modus: "schnell",
                gefundenCount: 0, anonymisiertGesendet: "<script>alert(1)</script>");

            Assert.DoesNotContain("<script>", html);
            Assert.Contains("&lt;script&gt;", html);
        }
    }
}
