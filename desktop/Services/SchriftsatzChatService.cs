using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LexWolf.Database;
using LexWolf.Dialogs;
using LexWolf.Models;

namespace LexWolf.Services;

/// <summary>
/// T#321: führt denselben Backend-Weg wie SchriftsatzDialog.OnErstellen aus,
/// aber ohne das SchriftsatzDialog-Fenster zu öffnen — damit der Anwalt aus dem
/// Chat heraus direkt einen Schriftsatz mit Agentic-RAG-Recherche, Dokumenten-
/// auswahl und Belegprüfung erzeugen kann. Reine Service-Schicht, UI-frei.
/// </summary>
public sealed class SchriftsatzChatService : IDisposable
{
    public sealed class Ergebnis
    {
        public required string Text { get; init; }
        public required string Art { get; init; }
        public required string Anweisung { get; init; }
        public IReadOnlyList<Beleg> Belege { get; init; } = Array.Empty<Beleg>();
        public IReadOnlyList<ReActStepEvent> SuchEvents { get; init; } = Array.Empty<ReActStepEvent>();
        public string? AnonymisiertGesendet { get; init; }
        public string SuchModus { get; init; } = "schnell";
    }

    public sealed class Beleg
    {
        public required string Zitat { get; init; }
        public required string Status { get; init; }
        public string Hinweis { get; init; } = "";
        public string Symbol => Status switch
        {
            "geprüft" => "✓",
            "Dokument existiert nicht" => "✗",
            _ => "?"
        };
        public bool IstGeprueft => Status == "geprüft";
    }

    public sealed class DokumentAuswahl
    {
        public required string Titel { get; init; }
        public required string Text { get; init; }
        public required string Pfad { get; init; }
        public bool Ausgewaehlt { get; set; } = true;
    }

    private readonly LocalDb _db;
    private readonly string _backendUrl;
    private readonly string _mandantId;
    private readonly string _mandantName;
    private readonly AppSettings _settings;
    private readonly ReActStreamClient _reactClient;
    private readonly HttpClient _schriftsatzHttp = new() { Timeout = Timeout.InfiniteTimeSpan };

    public SchriftsatzChatService(LocalDb db, string backendUrl, string mandantId, string mandantName, AppSettings settings)
    {
        _db = db;
        _backendUrl = backendUrl;
        _mandantId = mandantId;
        _mandantName = mandantName;
        _settings = settings;
        _reactClient = new ReActStreamClient(_schriftsatzHttp, backendUrl);
    }

    /// <summary>
    /// Erzeugt einen Schriftsatz der gegebenen Art zur Anweisung. Läuft die
    /// Rechtsrecherche vor dem Entwurf, sofern RechercheQueryBuilder einen Query
    /// liefert, und ruft anschließend POST /akte/schriftsatz auf.
    /// <paramref name="fortschritt"/> (Task #321): Callback, um Live-Suchschritte
    /// an den Aufrufer (MainWindow) zu melden, damit der Fortschritt im Chat-Panel
    /// sichtbar wird.
    /// </summary>
    public async Task<Ergebnis> ErstelleSchriftsatzAsync(string art, string anweisung, string suchModus, CancellationToken ct,
        IProgress<ReActStepEvent>? fortschritt = null)
    {
        var stamm = _db.GetAktenStamm(_mandantId);
        var stammText = string.Join("; ", new[]
        {
            stamm.Gericht.Length > 0 ? $"Gericht: {stamm.Gericht}" : "",
            stamm.Aktenzeichen.Length > 0 ? $"Az.: {stamm.Aktenzeichen}" : "",
            stamm.Gegner.Length > 0 ? $"Gegner: {stamm.Gegner}" : "",
            stamm.Gegnervertreter.Length > 0 ? $"Gegnervertreter: {stamm.Gegnervertreter}" : "",
        }.Where(s => s.Length > 0));

        var aliasToOriginal = new Dictionary<string, string>();
        var anweisungAnonym = AnonymizerEngine.AnonymizeText(anweisung.Trim(), aliasToOriginal);

        string? rechercheKontext = null;
        var events = new List<ReActStepEvent>();
        var query = RechercheQueryBuilder.Build(art, anweisungAnonym);
        if (query.Length > 0)
        {
            await foreach (var evt in _reactClient.StreamAsync(query, suchModus, ct).ConfigureAwait(false))
            {
                if (evt.Step == "antwort_chunk") continue;
                events.Add(evt);
                fortschritt?.Report(evt);
            }
            rechercheKontext = AgenticSearchContext.BuildKontext(events, suchModus);
        }

        var dokumente = LadeDokumente();
        var dokumenteAnonym = dokumente
            .Where(d => d.Ausgewaehlt)
            .Select(d => (Titel: d.Titel, Text: AnonymizerEngine.AnonymizeText(d.Text, aliasToOriginal)))
            .ToList();

        var kanzleiTeile = new[]
        {
            _settings.Briefkopf.Length > 0 ? $"Briefkopf: {_settings.Briefkopf}" : "",
            _settings.KanzleiKontodaten.Length > 0 ? $"Kontodaten: {_settings.KanzleiKontodaten}" : "",
            _settings.Briefende.Length > 0 ? $"Briefende: {_settings.Briefende}" : "",
        }.Where(s => s.Length > 0);

        var payload = JsonSerializer.Serialize(new
        {
            art,
            anweisung = anweisungAnonym,
            mandant = _mandantName,
            stammdaten = stammText,
            dokumente = dokumenteAnonym.Select(d => new { titel = d.Titel, text = d.Text }),
            stil_vorlage = LadeStilvorlage(art),
            recherche_kontext = rechercheKontext ?? "",
            kanzlei_stammdaten = string.Join("\n", kanzleiTeile),
            ort = _settings.KanzleiOrt,
        });

        var anonymisiertGesendet = AnonymisiertGesendetBuilder.BuildFuerSchriftsatz(anweisungAnonym, dokumenteAnonym);

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await http.PostAsync($"{_backendUrl}/akte/schriftsatz", content, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        var rohEntwurf = doc.RootElement.GetProperty("text").GetString() ?? "";
        var entwurf = AnonymizerEngine.DeAnonymizeText(rohEntwurf, aliasToOriginal);
        var belege = doc.RootElement.TryGetProperty("belege", out var b) && b.ValueKind == JsonValueKind.Array
            ? b.EnumerateArray().Select(x => new Beleg
            {
                Zitat = AnonymizerEngine.DeAnonymizeText(x.GetProperty("zitat").GetString() ?? "", aliasToOriginal),
                Status = x.GetProperty("status").GetString() ?? "",
                Hinweis = x.TryGetProperty("hinweis", out var h)
                    ? AnonymizerEngine.DeAnonymizeText(h.GetString() ?? "", aliasToOriginal)
                    : ""
            }).ToList()
            : new List<Beleg>();

        return new Ergebnis
        {
            Text = entwurf,
            Art = art,
            Anweisung = anweisung.Trim(),
            Belege = belege,
            SuchEvents = events,
            AnonymisiertGesendet = anonymisiertGesendet,
            SuchModus = suchModus,
        };
    }

    private List<DokumentAuswahl> LadeDokumente()
    {
        return _db.GetDokumenteForMandant(_mandantId, maxDokumente: 60)
            .Where(d => d.Text.Length > 0 && !Path.GetFileName(d.Pfad).StartsWith("LexWolf_", StringComparison.OrdinalIgnoreCase))
            .Select(d => new DokumentAuswahl { Titel = d.Titel, Text = d.Text, Pfad = d.Pfad, Ausgewaehlt = true })
            .ToList();
    }

    private string LadeStilvorlage(string art)
    {
        var pfad = _db.GetLetzteVorlage(art)?.VorlagePfad ?? "";
        if (pfad.Length == 0 || !File.Exists(pfad)) return "";
        try { return DokumentScanner.LeseText(pfad); } catch { return ""; }
    }

    public void Dispose()
    {
        _schriftsatzHttp.Dispose();
    }
}
