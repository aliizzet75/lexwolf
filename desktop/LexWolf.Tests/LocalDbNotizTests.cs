using System;
using System.IO;
using System.Linq;
using LexWolf.Database;
using Xunit;

namespace LexWolf.Tests
{
    public class LocalDbNotizTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly LocalDb _db;

        public LocalDbNotizTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"lexwolf_notiz_test_{Guid.NewGuid():N}.db");
            _db = new LocalDb(_dbPath);
        }

        public void Dispose()
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }

        [Fact]
        public void InsertNotiz_SchreibtEintragKorrekt()
        {
            _db.InsertNotiz("notiz-1", "mandant-1", "Erste Notiz", "Inhalt der ersten Notiz");

            var liste = _db.GetNotizen("mandant-1");

            Assert.Single(liste);
            Assert.Equal("notiz-1", liste[0].Id);
            Assert.Equal("mandant-1", liste[0].MandantId);
            Assert.Equal("Erste Notiz", liste[0].TitelKurz);
            Assert.Equal("Inhalt der ersten Notiz", liste[0].Text);
        }

        [Fact]
        public void UpdateNotiz_AktualisiertTitelUndText()
        {
            _db.InsertNotiz("notiz-2", "mandant-1", "Alt", "Alter Text");

            _db.UpdateNotiz("notiz-2", "Neu", "Neuer Text");

            var liste = _db.GetNotizen("mandant-1");
            Assert.Single(liste);
            Assert.Equal("Neu", liste[0].TitelKurz);
            Assert.Equal("Neuer Text", liste[0].Text);
            Assert.True(liste[0].Geaendert >= liste[0].Erstellt);
        }

        [Fact]
        public void GetNotizen_LiefertAbsteigendNachErstellt()
        {
            _db.InsertNotiz("notiz-a", "mandant-2", "A", "Text A");
            System.Threading.Thread.Sleep(50);
            _db.InsertNotiz("notiz-b", "mandant-2", "B", "Text B");
            System.Threading.Thread.Sleep(50);
            _db.InsertNotiz("notiz-c", "mandant-2", "C", "Text C");

            var liste = _db.GetNotizen("mandant-2");

            Assert.Equal(3, liste.Count);
            Assert.Equal("notiz-c", liste[0].Id);
            Assert.Equal("notiz-b", liste[1].Id);
            Assert.Equal("notiz-a", liste[2].Id);
        }

        [Fact]
        public void GetNotizen_FiltertNachMandant()
        {
            _db.InsertNotiz("notiz-x", "mandant-a", "X", "Text X");
            _db.InsertNotiz("notiz-y", "mandant-b", "Y", "Text Y");

            var liste = _db.GetNotizen("mandant-a");

            Assert.Single(liste);
            Assert.Equal("notiz-x", liste[0].Id);
        }
    }
}
