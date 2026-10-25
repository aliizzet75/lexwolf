using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// Task #80: GesetzesChunk bildet die Anzeige für das Quellen-Popup (Fundstelle,
    /// Datum) aus den Rohdaten von /legal-db/chunks/{id} bzw. .../by-reference ab.
    /// </summary>
    public class QuellenangabeServiceTests
    {
        [Fact]
        public void Fundstelle_Fuer_Gesetzesnorm_Nutzt_Kuerzel_Und_Titel()
        {
            var chunk = new GesetzesChunk(127095, "Text", "§ 23 Geltungsbereich (1)", "", "", null, "Arbeitsrecht", "kschg", "2026-08-09T04:00:18", null);
            Assert.Equal("KSCHG — § 23 Geltungsbereich (1)", chunk.Fundstelle);
        }

        [Fact]
        public void Fundstelle_Fuer_Urteil_Nutzt_Gericht_Und_Aktenzeichen()
        {
            var chunk = new GesetzesChunk(1, "Text", "Kündigungsschutz", "BAG", "2 AZR 123/20", "2024-03-12", "Arbeitsrecht", null, "2026-01-01T00:00:00", null);
            Assert.Equal("BAG, 2 AZR 123/20", chunk.Fundstelle);
        }

        [Fact]
        public void Datum_Bevorzugt_Echtes_Datum_Vor_Gueltig_Ab_Vor_Importdatum()
        {
            var mitDatum = new GesetzesChunk(1, "T", "T", null, null, "2024-03-12", null, null, "2026-01-01T00:00:00", "2020-01-01");
            Assert.Equal("12.03.2024", mitDatum.DatumAnzeige);

            var nurGueltigAb = new GesetzesChunk(1, "T", "T", null, null, null, null, null, "2026-01-01T00:00:00", "2020-01-01");
            Assert.Equal("gültig ab 01.01.2020", nurGueltigAb.DatumAnzeige);

            var nurImport = new GesetzesChunk(1, "T", "T", null, null, null, null, null, "2026-01-01T00:00:00", null);
            Assert.Equal("importiert am 01.01.2026", nurImport.DatumAnzeige);
        }
    }
}
