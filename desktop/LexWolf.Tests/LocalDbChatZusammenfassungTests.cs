using System;
using System.IO;
using LexWolf.Database;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// Jede Testinstanz bekommt eine eigene temporäre SQLite-Datei, damit Tests
    /// parallel laufen können und sich nicht die reale lokale DB im
    /// ApplicationData-Ordner teilen.
    /// </summary>
    public class LocalDbChatZusammenfassungTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly LocalDb _db;

        public LocalDbChatZusammenfassungTests()
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
        public void InsertChatZusammenfassung_SchreibtEintragKorrekt()
        {
            var start = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
            var ende = new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Utc);

            _db.InsertChatZusammenfassung("mandant-1", start, ende, "Beratung zu Unterhaltsfragen.");

            var liste = _db.GetChatZusammenfassungen("mandant-1");

            Assert.Single(liste);
            Assert.Equal("mandant-1", liste[0].MandantId);
            Assert.Equal("Beratung zu Unterhaltsfragen.", liste[0].Zusammenfassung);
            Assert.Equal(start, liste[0].SitzungStart);
            Assert.Equal(ende, liste[0].SitzungEnde);
        }

        [Fact]
        public void GetChatZusammenfassungen_LiefertChronologischSortierteListe()
        {
            var basis = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

            _db.InsertChatZusammenfassung("mandant-2", basis.AddHours(2), basis.AddHours(2.5), "Dritte Sitzung");
            _db.InsertChatZusammenfassung("mandant-2", basis, basis.AddMinutes(30), "Erste Sitzung");
            _db.InsertChatZusammenfassung("mandant-2", basis.AddHours(1), basis.AddHours(1.5), "Zweite Sitzung");

            var liste = _db.GetChatZusammenfassungen("mandant-2");

            Assert.Equal(3, liste.Count);
            Assert.Equal("Erste Sitzung", liste[0].Zusammenfassung);
            Assert.Equal("Zweite Sitzung", liste[1].Zusammenfassung);
            Assert.Equal("Dritte Sitzung", liste[2].Zusammenfassung);
        }

        [Fact]
        public void GetChatZusammenfassungen_FiltertNachMandant()
        {
            var start = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
            var ende = new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Utc);

            _db.InsertChatZusammenfassung("mandant-a", start, ende, "Für A");
            _db.InsertChatZusammenfassung("mandant-b", start, ende, "Für B");

            var liste = _db.GetChatZusammenfassungen("mandant-a");

            Assert.Single(liste);
            Assert.Equal("Für A", liste[0].Zusammenfassung);
        }

        [Fact]
        public void ChatHistory_BleibtUnveraendertErhalten()
        {
            _db.AddChatMessage("mandant-1", "user", "Hallo, ich brauche Hilfe.");

            using var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM chat_history WHERE mandant_id = 'mandant-1';";
            var count = (long)cmd.ExecuteScalar()!;

            Assert.Equal(1, count);
        }
    }
}
