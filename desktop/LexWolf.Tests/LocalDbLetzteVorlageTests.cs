using System;
using System.IO;
using LexWolf.Database;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>Task #304: "zuletzt gewählte Vorlage pro Schriftsatz-Typ" ersetzt die
    /// bisherige Heuristik "Datei mit jüngster LastWriteTimeUtc" durch eine echte
    /// Auswahl-Speicherung. Hier wird nur die LocalDb-Persistenz getestet; die explizite
    /// Auswahl im Schriftsatz-Dialog (WPF, nur unter Windows lauffähig) wird wie die
    /// übrigen Dialoge manuell/visuell geprüft.</summary>
    public class LocalDbLetzteVorlageTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly LocalDb _db;

        public LocalDbLetzteVorlageTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"lexwolf_letztevorlage_test_{Guid.NewGuid():N}.db");
            _db = new LocalDb(_dbPath);
        }

        public void Dispose()
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }

        [Fact]
        public void GetLetzteVorlage_OhneAuswahlLiefertNull()
        {
            Assert.Null(_db.GetLetzteVorlage("Klageschrift"));
        }

        [Fact]
        public void UpsertLetzteVorlage_SpeichertPfadUndZeitstempel()
        {
            var vorher = DateTime.UtcNow;

            _db.UpsertLetzteVorlage("Klageschrift", @"C:\Vorlagen\Klageschrift\muster.docx");

            var gewaehlt = _db.GetLetzteVorlage("Klageschrift");
            Assert.NotNull(gewaehlt);
            Assert.Equal(@"C:\Vorlagen\Klageschrift\muster.docx", gewaehlt!.Value.VorlagePfad);
            Assert.InRange(gewaehlt.Value.GewaehltAm, vorher.AddSeconds(-2), DateTime.UtcNow.AddSeconds(2));
        }

        [Fact]
        public void UpsertLetzteVorlage_ErsetztFruehereAuswahlDesselbenTyps()
        {
            _db.UpsertLetzteVorlage("Klageschrift", "alt.docx");
            _db.UpsertLetzteVorlage("Klageschrift", "neu.docx");

            var gewaehlt = _db.GetLetzteVorlage("Klageschrift");
            Assert.Equal("neu.docx", gewaehlt!.Value.VorlagePfad);
        }

        [Fact]
        public void UpsertLetzteVorlage_IstProSchriftsatzTypGetrennt()
        {
            _db.UpsertLetzteVorlage("Klageschrift", "klage.docx");
            _db.UpsertLetzteVorlage("Berufungsbegründung", "berufung.docx");

            Assert.Equal("klage.docx", _db.GetLetzteVorlage("Klageschrift")!.Value.VorlagePfad);
            Assert.Equal("berufung.docx", _db.GetLetzteVorlage("Berufungsbegründung")!.Value.VorlagePfad);
        }

        [Fact]
        public void GetLetzteVorlageGlobal_OhneAuswahlLiefertNull()
        {
            Assert.Null(_db.GetLetzteVorlageGlobal());
        }

        [Fact]
        public void GetLetzteVorlageGlobal_LiefertZeitlichZuletztGewaehlteUeberAlleTypen()
        {
            _db.UpsertLetzteVorlage("Klageschrift", "klage.docx");
            System.Threading.Thread.Sleep(20);
            _db.UpsertLetzteVorlage("Berufungsbegründung", "berufung.docx");

            var global = _db.GetLetzteVorlageGlobal();
            Assert.NotNull(global);
            Assert.Equal("Berufungsbegründung", global!.Value.SchriftsatzTyp);
            Assert.Equal("berufung.docx", global.Value.VorlagePfad);
        }

        [Fact]
        public void GetLetzteVorlageGlobal_AktualisiertSichBeiErneuterAuswahlDesAelterenTyps()
        {
            _db.UpsertLetzteVorlage("Klageschrift", "klage-v1.docx");
            System.Threading.Thread.Sleep(20);
            _db.UpsertLetzteVorlage("Berufungsbegründung", "berufung.docx");
            System.Threading.Thread.Sleep(20);
            _db.UpsertLetzteVorlage("Klageschrift", "klage-v2.docx");

            var global = _db.GetLetzteVorlageGlobal();
            Assert.Equal("Klageschrift", global!.Value.SchriftsatzTyp);
            Assert.Equal("klage-v2.docx", global.Value.VorlagePfad);
        }
    }
}
