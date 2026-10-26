using System.Collections.Generic;
using LexWolf.Models;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// T#309: Chat an Agentic-RAG-Suche anschließen. AgenticSearchContext baut aus
    /// den Live-Suchschritten (ReActStreamClient/ReActStepEvent) den Kontext-Block,
    /// der zusätzlich an /chat geht — DoD "Suchergebnisse beeinflussen nachweislich
    /// die /chat-Antwort" hängt direkt an dieser Funktion: nur was hier im Kontext
    /// landet, kann die Antwort/Zitate beeinflussen.
    /// </summary>
    public class AgenticSearchContextTests
    {
        [Fact]
        public void BuildKontext_MitGefundenenQuellen_EnthaeltQuelleUndText()
        {
            var events = new List<ReActStepEvent>
            {
                new() { Step = "suche_start", Text = "Suche nach: Kündigung Mietvertrag" },
                new() { Step = "suchergebnis", Text = "§ 573c BGB regelt die Kündigungsfrist.",
                        Quelle = "Gesetzestext: Zivilrecht", Confidence = 0.91, Status = "gefunden" },
                new() { Step = "bewertung", Text = "vollstaendig=True", Confidence = 0.8 },
                new() { Step = "fertig", Text = "Suche abgeschlossen" },
            };

            var kontext = AgenticSearchContext.BuildKontext(events, "schnell");

            Assert.NotNull(kontext);
            Assert.Contains("§ 573c BGB", kontext);
            Assert.Contains("Gesetzestext: Zivilrecht", kontext);
            Assert.Contains("schnell", kontext);
        }

        [Fact]
        public void BuildKontext_VerworfeneTreffer_WerdenNichtAufgenommen()
        {
            var events = new List<ReActStepEvent>
            {
                new() { Step = "suchergebnis", Text = "irrelevanter Treffer",
                        Quelle = "Datenbank", Confidence = 0.1, Status = "verworfen" },
            };

            var kontext = AgenticSearchContext.BuildKontext(events, "tief");

            Assert.Null(kontext);
        }

        [Fact]
        public void BuildKontext_OhneSuchergebnisse_LiefertNull()
        {
            var events = new List<ReActStepEvent>
            {
                new() { Step = "suche_start", Text = "Suche nach: X" },
                new() { Step = "fertig", Text = "Suche abgeschlossen" },
            };

            Assert.Null(AgenticSearchContext.BuildKontext(events, "schnell"));
        }

        [Fact]
        public void BuildKontext_MehrereQuellen_AlleInReihenfolgeEnthalten()
        {
            var events = new List<ReActStepEvent>
            {
                new() { Step = "suchergebnis", Text = "Erster Treffer", Quelle = "Quelle A",
                        Confidence = 0.9, Status = "gefunden" },
                new() { Step = "suchergebnis", Text = "Zweiter Treffer", Quelle = "Quelle B",
                        Confidence = 0.75, Status = "gefunden" },
            };

            var kontext = AgenticSearchContext.BuildKontext(events, "tief");

            Assert.NotNull(kontext);
            var posA = kontext!.IndexOf("Erster Treffer", System.StringComparison.Ordinal);
            var posB = kontext.IndexOf("Zweiter Treffer", System.StringComparison.Ordinal);
            Assert.True(posA >= 0 && posB > posA);
        }
    }
}
