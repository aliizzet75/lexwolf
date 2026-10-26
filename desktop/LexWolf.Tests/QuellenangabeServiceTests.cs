using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>T#300: fängt die letzte Anfrage ab und antwortet mit einem fest
    /// einprogrammierten Status/Body — ohne echten Server, siehe VorschlaegeServiceTests.
    /// Simuliert hier gezielt das 404-Verhalten von /legal-db/chunks/{id} für eine
    /// ungültige chunk_id.</summary>
    file sealed class FakeHandler : HttpMessageHandler
    {
        public string? LetzteUrl { get; private set; }
        private readonly string _antwortJson;
        private readonly HttpStatusCode _status;

        public FakeHandler(string antwortJson, HttpStatusCode status = HttpStatusCode.OK)
        {
            _antwortJson = antwortJson;
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LetzteUrl = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_antwortJson, Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>
    /// Task #80: GesetzesChunk bildet die Anzeige für das Quellen-Popup (Fundstelle,
    /// Datum) aus den Rohdaten von /legal-db/chunks/{id} bzw. .../by-reference ab.
    /// </summary>
    public class QuellenangabeServiceTests
    {
        /// <summary>T#300-Regression: Inline-Zitate (ChatHtmlRenderer.ApplySemanticHighlighting)
        /// tragen kein data-msg und kommen in MainWindow.QuelleOeffnenAsync daher mit chunkId
        /// &lt;= 0 an. Direktes Laden per /legal-db/chunks/{id} liefert dafür immer 404 —
        /// LadeChunkAsync muss das als "kein Treffer" (null) behandeln, nicht werfen, damit der
        /// Aufrufer auf die Referenz-Auflösung zurückfallen kann.</summary>
        [Fact]
        public async Task LadeChunkAsync_Bei_404_Gibt_Null_Zurueck()
        {
            var handler = new FakeHandler("""{"detail":"Chunk not found"}""", HttpStatusCode.NotFound);
            using var http = new HttpClient(handler);
            var service = new QuellenangabeService("http://backend.test", http);

            var chunk = await service.LadeChunkAsync(-1);

            Assert.Null(chunk);
        }

        /// <summary>T#300-Regression: der Fallback für Inline-Zitate löst die zusammengesetzte
        /// Norm ("§ 573 Abs. 2 Nr. 2 BGB", die Eigenbedarfs-Norm) über /legal-db/chunks/by-reference
        /// auf — mit der vollen Referenz, nicht nur der Paragraphen-Nummer.</summary>
        [Fact]
        public async Task LadeChunkFuerReferenzAsync_Loest_Zusammengesetzte_Norm_Ueber_By_Reference_Auf()
        {
            var antwort = """
            {"id": 106410, "text": "§ 573 Ordentliche Kündigung des Vermieters ...", "title": "§ 573 Ordentliche Kündigung des Vermieters", "court": "", "case_number": "", "date": null, "legal_field": "Zivilrecht", "tags": "bgb", "created_at": "2026-06-05T05:16:58", "valid_from": null}
            """;
            var handler = new FakeHandler(antwort);
            using var http = new HttpClient(handler);
            var service = new QuellenangabeService("http://backend.test", http);

            var chunk = await service.LadeChunkFuerReferenzAsync("§ 573 Abs. 2 Nr. 2 BGB");

            Assert.NotNull(chunk);
            Assert.Equal(106410, chunk!.Id);
            Assert.StartsWith("http://backend.test/legal-db/chunks/by-reference?ref=", handler.LetzteUrl);
            Assert.Contains("573", handler.LetzteUrl);
        }

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
