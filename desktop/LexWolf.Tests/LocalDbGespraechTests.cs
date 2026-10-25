using System;
using System.IO;
using LexWolf.Database;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// Task #130: Mandanten-Gedächtnis um Gespräch-Einträge erweitern — keine neue
    /// separate DB, sondern dieselbe LocalDb-Struktur wie Chat-Historie/Notizen (M16).
    /// Jede Testinstanz bekommt eine eigene temporäre SQLite-Datei.
    /// </summary>
    public class LocalDbGespraechTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly LocalDb _db;

        public LocalDbGespraechTests()
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
        public void InsertGespraech_SchreibtEintragInDerselbenLocalDb()
        {
            var datum = new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);
            var json = "{\"zusammenfassung\":\"Besprochen: Scheidungsfolgen, Unterhalt.\",\"transkript\":\"...\"}";

            _db.InsertGespraech("mandant-1", datum, json);

            var liste = _db.GetGespraeche("mandant-1");

            Assert.Single(liste);
            Assert.Equal("mandant-1", liste[0].MandantId);
            Assert.Equal(datum, liste[0].Datum);
            Assert.Equal(json, liste[0].StrukturiertJson);
        }

        [Fact]
        public void GetGespraeche_FiltertNachMandantUndSortiertChronologisch()
        {
            var basis = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

            _db.InsertGespraech("mandant-a", basis.AddDays(2), "{\"zusammenfassung\":\"Drittes Gespräch\"}");
            _db.InsertGespraech("mandant-a", basis, "{\"zusammenfassung\":\"Erstes Gespräch\"}");
            _db.InsertGespraech("mandant-a", basis.AddDays(1), "{\"zusammenfassung\":\"Zweites Gespräch\"}");
            _db.InsertGespraech("mandant-b", basis, "{\"zusammenfassung\":\"Für B\"}");

            var liste = _db.GetGespraeche("mandant-a");

            Assert.Equal(3, liste.Count);
            Assert.Equal("Erstes Gespräch", GespraechStruktur.Parse(liste[0].StrukturiertJson).Zusammenfassung);
            Assert.Equal("Zweites Gespräch", GespraechStruktur.Parse(liste[1].StrukturiertJson).Zusammenfassung);
            Assert.Equal("Drittes Gespräch", GespraechStruktur.Parse(liste[2].StrukturiertJson).Zusammenfassung);
        }

        [Fact]
        public void GetGespraeche_LeerWennKeineGespraecheFuerMandant()
        {
            Assert.Empty(_db.GetGespraeche("unbekannt"));
        }

        [Fact]
        public void GespraechStruktur_Parse_LiestZusammenfassungUndTranskript()
        {
            var g = GespraechStruktur.Parse("{\"zusammenfassung\":\"Kurzfassung\",\"transkript\":\"Volltext...\"}");
            Assert.Equal("Kurzfassung", g.Zusammenfassung);
            Assert.Equal("Volltext...", g.Transkript);
            Assert.Equal("Kurzfassung", g.AlsKurztext());
        }

        [Fact]
        public void GespraechStruktur_Parse_FaelltAufTranskriptZurueckWennKeineZusammenfassung()
        {
            var g = GespraechStruktur.Parse("{\"transkript\":\"Nur Rohtext ohne Zusammenfassung.\"}");
            Assert.Equal("", g.Zusammenfassung);
            Assert.Equal("Nur Rohtext ohne Zusammenfassung.", g.AlsKurztext());
        }

        [Fact]
        public void GespraechStruktur_Parse_ToleriertNichtJsonAlsRohtranskript()
        {
            var g = GespraechStruktur.Parse("Das war einfach ein Rohtranskript ohne JSON-Huelle.");
            Assert.Equal("", g.Zusammenfassung);
            Assert.Equal("Das war einfach ein Rohtranskript ohne JSON-Huelle.", g.Transkript);
        }

        [Fact]
        public void Gespraech_WirdBeimLoeschenDesMandantenMitentfernt()
        {
            _db.UpsertMandant("m1", "Mandant M1", "1");
            _db.InsertGespraech("m1", DateTime.UtcNow, "{\"zusammenfassung\":\"Letztes Gespräch\"}");
            Assert.Single(_db.GetGespraeche("m1"));

            _db.DeleteMandantKomplett("m1");

            Assert.Empty(_db.GetGespraeche("m1"));
        }
    }
}
