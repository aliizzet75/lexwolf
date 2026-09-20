using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// Regressionstest fuer Task #223: semantische Hervorhebung von Fristen,
    /// Paragraphen und Handlungsempfehlungen in KI-Antworten (ChatHtmlRenderer).
    /// </summary>
    public class ChatHighlightingTests
    {
        [Fact]
        public void Erkennt_Frist_Und_Hebt_Sie_Hervor()
        {
            var html = ChatHtmlRenderer.Render("Die Frist endet am 15.03.2026, bitte beachten Sie das.");
            Assert.Contains("highlight-frist", html);
            Assert.Contains("15.03.2026", html);
        }

        [Fact]
        public void Erkennt_Paragraph_Und_Hebt_Ihn_Hervor()
        {
            var html = ChatHtmlRenderer.Render("Gemäß § 1601 BGB besteht ein Unterhaltsanspruch.");
            Assert.Contains("highlight-paragraph", html);
            Assert.Contains("§ 1601 BGB", html);
        }

        [Fact]
        public void Erkennt_Empfehlung_Und_Hebt_Sie_Hervor()
        {
            var html = ChatHtmlRenderer.Render("Sie sollten fristgerecht Widerspruch einlegen.");
            Assert.Contains("highlight-empfehlung", html);
        }

        [Fact]
        public void Keine_Falsch_Positiven_Bei_Einfachem_Fliesstext()
        {
            var html = ChatHtmlRenderer.Render("Das Wetter heute ist sonnig und die Katze schläft auf dem Sofa.");
            Assert.DoesNotContain("highlight-frist", html);
            Assert.DoesNotContain("highlight-paragraph", html);
            Assert.DoesNotContain("highlight-empfehlung", html);
        }
    }
}
