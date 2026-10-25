using System;
using System.IO;
using System.Linq;
using LexWolf.Database;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// Task #282: Konfidenz-Badge neben jeder Chat-Antwort (grün/gelb/rot nach
    /// confidence_score) und die Datengrundlage für die Statistik-Seite
    /// (Durchschnitt der letzten 20 Antworten).
    /// </summary>
    public class KonfidenzBadgeTests
    {
        [Theory]
        [InlineData(0.95, "green")]
        [InlineData(0.81, "green")]
        [InlineData(0.8, "yellow")]
        [InlineData(0.65, "yellow")]
        [InlineData(0.5, "yellow")]
        [InlineData(0.49, "red")]
        [InlineData(0.1, "red")]
        public void ConfidenceColor_OrdnetScoreDerRichtigenAmpelfarbeZu(double score, string erwarteteFarbe)
        {
            Assert.Equal(erwarteteFarbe, ChatHtmlRenderer.ConfidenceColor(score));
        }

        [Fact]
        public void WrapAiBubble_MitConfidence_EnthaeltBadgeMitRichtigerFarbeUndProzent()
        {
            var html = ChatHtmlRenderer.WrapAiBubble("<p class='chat-paragraph'>Antwort</p>", 0.92);

            Assert.Contains("confidence-badge", html);
            Assert.Contains("confidence-green", html);
            Assert.Contains("92%", html);
        }

        [Fact]
        public void WrapAiBubble_MitNiedrigerConfidence_IstRot()
        {
            var html = ChatHtmlRenderer.WrapAiBubble("<p class='chat-paragraph'>Antwort</p>", 0.2);

            Assert.Contains("confidence-red", html);
            Assert.Contains("20%", html);
        }

        [Fact]
        public void WrapAiBubble_OhneConfidence_ZeigtKeinBadge()
        {
            var html = ChatHtmlRenderer.WrapAiBubble("<p class='chat-paragraph'>Antwort</p>");

            Assert.DoesNotContain("confidence-badge", html);
        }
    }

    /// <summary>Persistenz des confidence_score in der chat_history-Tabelle (LocalDb) und
    /// die Aggregation für die Statistik-Seite. Jede Testinstanz bekommt eine eigene
    /// temporäre SQLite-Datei (gleiches Muster wie LocalDbGespraechTests).</summary>
    public class LocalDbKonfidenzTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly LocalDb _db;

        public LocalDbKonfidenzTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"lexwolf_test_{Guid.NewGuid():N}.db");
            _db = new LocalDb(_dbPath);
        }

        public void Dispose()
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }

        [Fact]
        public void AddChatMessage_SpeichertConfidenceScoreFuerAssistant()
        {
            _db.AddChatMessage("mandant-1", "user", "Frage ohne Score");
            _db.AddChatMessage("mandant-1", "assistant", "Antwort mit Score", 0.77);

            var werte = _db.GetLetzteKonfidenzWerte(20);

            Assert.Single(werte);
            Assert.Equal(0.77, werte[0].Score, 3);
        }

        [Fact]
        public void GetLetzteKonfidenzWerte_IgnoriertNachrichtenOhneScore()
        {
            _db.AddChatMessage("mandant-1", "assistant", "Ohne Score (z.B. Backend-Fehler)");
            _db.AddChatMessage("mandant-1", "assistant", "Mit Score", 0.6);

            var werte = _db.GetLetzteKonfidenzWerte(20);

            Assert.Single(werte);
            Assert.Equal(0.6, werte[0].Score, 3);
        }

        [Fact]
        public void GetLetzteKonfidenzWerte_BegrenztAufDieLetztenNUndIstChronologischAufsteigend()
        {
            for (int i = 0; i < 25; i++)
            {
                _db.AddChatMessage("mandant-1", "assistant", $"Antwort {i}", i / 100.0 + 0.5);
                System.Threading.Thread.Sleep(2); // garantiert unterschiedliche Timestamps
            }

            var werte = _db.GetLetzteKonfidenzWerte(20);

            Assert.Equal(20, werte.Count);
            // älteste der zurückgegebenen 20 ist Antwort Nr. 5 (0-indiziert) -> Score 0.55
            Assert.Equal(0.55, werte[0].Score, 3);
            // neueste ist Antwort Nr. 24 -> Score 0.74
            Assert.Equal(0.74, werte[^1].Score, 3);
            for (int i = 1; i < werte.Count; i++)
                Assert.True(werte[i].Timestamp >= werte[i - 1].Timestamp);
        }

        [Fact]
        public void GetLetzteKonfidenzWerte_IstGlobalUeberAlleMandantenHinweg()
        {
            _db.AddChatMessage("mandant-a", "assistant", "A", 0.9);
            _db.AddChatMessage("mandant-b", "assistant", "B", 0.3);

            var werte = _db.GetLetzteKonfidenzWerte(20);

            Assert.Equal(2, werte.Count);
        }

        [Fact]
        public void DurchschnittDerLetzten20Antworten_WirdKorrektBerechnet()
        {
            _db.AddChatMessage("m", "assistant", "1", 1.0);
            _db.AddChatMessage("m", "assistant", "2", 0.5);
            _db.AddChatMessage("m", "assistant", "3", 0.0);

            var werte = _db.GetLetzteKonfidenzWerte(20);
            var durchschnitt = werte.Average(w => w.Score);

            Assert.Equal(0.5, durchschnitt, 3);
        }
    }
}
