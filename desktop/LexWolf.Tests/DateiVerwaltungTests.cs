using System;
using System.IO;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// Task #302: Umbenennen im Dateibaum — Validierung gegen das reale Dateisystem
    /// (temporäres Verzeichnis), kein Mock.
    /// </summary>
    public class DateiVerwaltungTests : IDisposable
    {
        private readonly string _ordner;

        public DateiVerwaltungTests()
        {
            _ordner = Path.Combine(Path.GetTempPath(), "LexWolfTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_ordner);
        }

        public void Dispose()
        {
            try { Directory.Delete(_ordner, recursive: true); } catch { }
        }

        private string Datei(string name)
        {
            var pfad = Path.Combine(_ordner, name);
            File.WriteAllText(pfad, "test");
            return pfad;
        }

        [Fact]
        public void GueltigerNeuerName_WirdAkzeptiert()
        {
            var pfad = Datei("original.docx");
            var fehler = DateiVerwaltung.PruefeNeuenNamen("umbenannt.docx", _ordner, pfad);
            Assert.Null(fehler);
        }

        [Fact]
        public void DoppelterName_WirdAbgelehnt()
        {
            var pfad = Datei("original.docx");
            Datei("vorhanden.docx");
            var fehler = DateiVerwaltung.PruefeNeuenNamen("vorhanden.docx", _ordner, pfad);
            Assert.NotNull(fehler);
            Assert.Contains("existiert", fehler);
        }

        [Fact]
        public void DoppelterName_IstCaseInsensitiv()
        {
            var pfad = Datei("original.docx");
            Datei("Vorhanden.docx");
            var fehler = DateiVerwaltung.PruefeNeuenNamen("VORHANDEN.docx", _ordner, pfad);
            Assert.NotNull(fehler);
        }

        [Fact]
        public void DoppelterOrdnerName_WirdAbgelehnt()
        {
            var pfad = Datei("original.docx");
            Directory.CreateDirectory(Path.Combine(_ordner, "Unterordner"));
            var fehler = DateiVerwaltung.PruefeNeuenNamen("Unterordner", _ordner, pfad);
            Assert.NotNull(fehler);
        }

        [Fact]
        public void UnveraenderterName_WirdAkzeptiert()
        {
            var pfad = Datei("original.docx");
            var fehler = DateiVerwaltung.PruefeNeuenNamen("original.docx", _ordner, pfad);
            Assert.Null(fehler);
        }

        [Fact]
        public void NurGrossschreibungGeaendert_WirdAkzeptiert()
        {
            // Eigener Pfad per Case-Only-Rename -> kein Konflikt mit sich selbst.
            var pfad = Datei("original.docx");
            var fehler = DateiVerwaltung.PruefeNeuenNamen("Original.docx", _ordner, pfad);
            Assert.Null(fehler);
        }

        [Fact]
        public void LeererName_WirdAbgelehnt()
        {
            var pfad = Datei("original.docx");
            var fehler = DateiVerwaltung.PruefeNeuenNamen("   ", _ordner, pfad);
            Assert.NotNull(fehler);
        }

        [Theory]
        [InlineData("a/b.docx")]
        [InlineData("a\0b.docx")]
        public void UngueltigeZeichen_WerdenAbgelehnt(string name)
        {
            var pfad = Datei("original.docx");
            var fehler = DateiVerwaltung.PruefeNeuenNamen(name, _ordner, pfad);
            Assert.NotNull(fehler);
        }

        [Fact]
        public void PunktPunkt_WirdAbgelehnt()
        {
            var pfad = Datei("original.docx");
            var fehler = DateiVerwaltung.PruefeNeuenNamen("..", _ordner, pfad);
            Assert.NotNull(fehler);
        }
    }
}
