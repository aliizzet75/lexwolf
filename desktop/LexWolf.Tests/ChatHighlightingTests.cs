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

        /// <summary>Task #80: eine konkrete Paragraphen-Referenz (mit Nummer) wird als
        /// klickbarer Link gerendert, nicht nur als eingefärbter Text — sonst bliebe der
        /// Klick wirkungslos. Der generische Klick-Handler in GetChatBaseHtml greift über
        /// data-action/data-value, siehe MainWindow.OnChatWebMessage ("quelle").</summary>
        [Fact]
        public void Paragraph_Referenz_Ist_Klickbarer_Link()
        {
            var html = ChatHtmlRenderer.Render("Gemäß § 23 KSchG gilt eine Ausnahme.");
            Assert.Contains("<a href='#' class='highlight-paragraph quelle-link' data-action='quelle' data-value='&#167; 23 KSchG'>§ 23 KSchG</a>", html);
        }

        /// <summary>Ein bloßes Gesetzeskürzel ohne Paragraphennummer lässt sich nicht
        /// eindeutig auf einen Chunk auflösen -> bleibt reine Hervorhebung ohne Link.</summary>
        [Fact]
        public void Blosses_Gesetzeskuerzel_Ohne_Nummer_Ist_Nicht_Klickbar()
        {
            var html = ChatHtmlRenderer.Render("Das regelt grundsätzlich das BGB.");
            Assert.Contains("<span class='highlight-paragraph'>BGB</span>", html);
            Assert.DoesNotContain("data-action='quelle'", html);
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
