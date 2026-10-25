using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests;

/// <summary>Fängt die letzte an den HttpClient gesendete Anfrage ab und antwortet mit
/// einem fest einprogrammierten JSON-Body — ohne echten Server (Task #131: Backend läuft
/// nicht im plattformunabhängigen Testlauf von LexWolf.Tests).</summary>
file sealed class FakeHandler : HttpMessageHandler
{
    public string? LetzteUrl { get; private set; }
    public string? LetzterBody { get; private set; }
    private readonly string _antwortJson;
    private readonly HttpStatusCode _status;

    public FakeHandler(string antwortJson, HttpStatusCode status = HttpStatusCode.OK)
    {
        _antwortJson = antwortJson;
        _status = status;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LetzteUrl = request.RequestUri?.ToString();
        LetzterBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(_status)
        {
            Content = new StringContent(_antwortJson, Encoding.UTF8, "application/json"),
        };
    }
}

public class VorschlaegeServiceTests
{
    [Fact]
    public async Task HoleVorschlaegeAsync_ParstTypTitelUndText()
    {
        var antwort = """
        {"vorschlaege": [
            {"typ": "fristennotiz", "titel": "Fristennotiz anlegen?", "text": "Frist: 15.06.2026", "metadata": {}},
            {"typ": "email_entwurf", "titel": "E-Mail-Entwurf an Mandant senden?", "text": "Sehr geehrte/r [Mandant], ...", "metadata": {}}
        ]}
        """;
        var handler = new FakeHandler(antwort);
        using var http = new HttpClient(handler);

        var strukturiert = JsonDocument.Parse("""{"fristen":[],"themen":["Kündigung"]}""").RootElement;
        var vorschlaege = await VorschlaegeService.HoleVorschlaegeAsync(http, "http://backend.test", strukturiert, new List<string> { "Letztes Gespräch" });

        Assert.Equal(2, vorschlaege.Count);
        Assert.Contains(vorschlaege, v => v.Typ == "fristennotiz");
        Assert.Contains(vorschlaege, v => v.Typ == "email_entwurf");
        Assert.Equal("http://backend.test/conversation/suggestions", handler.LetzteUrl);
        Assert.Contains("mandant_history", handler.LetzterBody);
    }

    [Fact]
    public async Task HoleVorschlaegeAsync_LeereListeWennKeinVorschlaegeFeld()
    {
        var handler = new FakeHandler("""{"irrelevant": true}""");
        using var http = new HttpClient(handler);

        var strukturiert = JsonDocument.Parse("{}").RootElement;
        var vorschlaege = await VorschlaegeService.HoleVorschlaegeAsync(http, "http://backend.test", strukturiert);

        Assert.Empty(vorschlaege);
    }

    [Fact]
    public async Task AnalysiereTranskriptAsync_SendetTranscriptUndGibtRohesJsonZurueck()
    {
        var antwort = """{"fristen": [], "aufgaben": [], "themen": ["Mietrecht"], "naechste_schritte": [], "timestamp": "2026-01-01T00:00:00"}""";
        var handler = new FakeHandler(antwort);
        using var http = new HttpClient(handler);

        var strukturiert = await VorschlaegeService.AnalysiereTranskriptAsync(http, "http://backend.test", "Transkript-Text");

        Assert.Equal("http://backend.test/conversation/analyze", handler.LetzteUrl);
        Assert.Contains("Transkript-Text", handler.LetzterBody);
        var themen = VorschlaegeService.LeseThemen(strukturiert);
        Assert.Equal(new List<string> { "Mietrecht" }, themen);
    }

    [Fact]
    public async Task SendeEntscheidungAsync_SendetAlleFelderUndGibtTrueBeiErfolgZurueck()
    {
        var handler = new FakeHandler("""{"status": "gespeichert"}""");
        using var http = new HttpClient(handler);

        var erfolg = await VorschlaegeService.SendeEntscheidungAsync(
            http, "http://backend.test", "fristennotiz", "Frist: 15.06.2026", "geaendert", "Geänderter Text", "anwalt-1");

        Assert.True(erfolg);
        Assert.Equal("http://backend.test/conversation/suggestions/decision", handler.LetzteUrl);
        Assert.Contains("\"entscheidung\":\"geaendert\"", handler.LetzterBody);
        Assert.Contains("\"geaenderter_text\":\"Ge\\u00E4nderter Text\"", handler.LetzterBody);
        Assert.Contains("\"account_id\":\"anwalt-1\"", handler.LetzterBody);
    }

    [Fact]
    public async Task SendeEntscheidungAsync_GibtFalseBeiFehlerStatusZurueck()
    {
        var handler = new FakeHandler("""{"detail": "ungültig"}""", HttpStatusCode.BadRequest);
        using var http = new HttpClient(handler);

        var erfolg = await VorschlaegeService.SendeEntscheidungAsync(
            http, "http://backend.test", "fristennotiz", "x", "ja");

        Assert.False(erfolg);
    }

    [Fact]
    public async Task SendeEntscheidungAsync_GibtFalseBeiNichtErreichbaremServer()
    {
        using var http = new HttpClient(new ThrowingHandler());

        var erfolg = await VorschlaegeService.SendeEntscheidungAsync(
            http, "http://backend.test", "fristennotiz", "x", "ja");

        Assert.False(erfolg);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Server nicht erreichbar");
    }
}
