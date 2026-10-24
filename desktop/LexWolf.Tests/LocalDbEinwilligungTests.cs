using System;
using System.IO;
using LexWolf.Database;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>Task #128 (DSGVO-Einwilligungs-Flow): die reine LocalDb-Persistenz der
    /// Einwilligung ist hier testbar; der WPF-Dialog selbst (GespraechEinwilligungDialog)
    /// läuft nur unter Windows und wird — wie die übrigen Dialoge in Dialogs/ — nicht
    /// per Unit-Test, sondern manuell/visuell geprüft.</summary>
    public class LocalDbEinwilligungTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly LocalDb _db;

        public LocalDbEinwilligungTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"lexwolf_einwilligung_test_{Guid.NewGuid():N}.db");
            _db = new LocalDb(_dbPath);
        }

        public void Dispose()
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }

        [Fact]
        public void InsertEinwilligung_SpeichertBestaetigungMitZeitstempelUndText()
        {
            var vorher = DateTime.UtcNow;

            _db.InsertEinwilligung("mandant-1", "Das Gespräch wird aufgezeichnet. Einverstanden?", true);

            var liste = _db.GetEinwilligungen("mandant-1");

            Assert.Single(liste);
            Assert.Equal("mandant-1", liste[0].MandantId);
            Assert.True(liste[0].Bestaetigt);
            Assert.Equal("Das Gespräch wird aufgezeichnet. Einverstanden?", liste[0].Text);
            Assert.InRange(liste[0].Zeitstempel, vorher.AddSeconds(-2), DateTime.UtcNow.AddSeconds(2));
        }

        [Fact]
        public void InsertEinwilligung_KannAblehnungSpeichern()
        {
            _db.InsertEinwilligung("mandant-2", "Text", false);

            var liste = _db.GetEinwilligungen("mandant-2");

            Assert.Single(liste);
            Assert.False(liste[0].Bestaetigt);
        }

        [Fact]
        public void GetEinwilligungen_FiltertNachMandantUndSortiertAbsteigend()
        {
            _db.InsertEinwilligung("mandant-a", "Text 1", true);
            System.Threading.Thread.Sleep(20);
            _db.InsertEinwilligung("mandant-a", "Text 2", true);
            _db.InsertEinwilligung("mandant-b", "Andere", true);

            var liste = _db.GetEinwilligungen("mandant-a");

            Assert.Equal(2, liste.Count);
            Assert.Equal("Text 2", liste[0].Text);
            Assert.Equal("Text 1", liste[1].Text);
        }

        [Fact]
        public void GetEinwilligungen_OhneEintraegeLiefertLeereListe()
        {
            var liste = _db.GetEinwilligungen("kein-mandant");

            Assert.Empty(liste);
        }
    }
}
