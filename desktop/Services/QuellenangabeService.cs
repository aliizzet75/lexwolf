using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LexWolf.Services;

/// <summary>Gesetzestext-Chunk, wie ihn /legal-db/chunks/{id} bzw.
/// /legal-db/chunks/by-reference liefert (Task #80: Inline-Quellenangaben).</summary>
public sealed record GesetzesChunk(
    int Id,
    string Text,
    string Title,
    string? Court,
    string? CaseNumber,
    string? Date,
    string? LegalField,
    string? Tags,
    string? CreatedAt,
    string? ValidFrom)
{
    /// <summary>Fundstelle für die Anzeige im Popup: bei Urteilen Gericht + Aktenzeichen,
    /// sonst Gesetzeskürzel (aus den Tags) + Paragraphentitel.</summary>
    public string Fundstelle =>
        !string.IsNullOrWhiteSpace(Court)
            ? string.IsNullOrWhiteSpace(CaseNumber) ? Court! : $"{Court}, {CaseNumber}"
            : !string.IsNullOrWhiteSpace(Tags)
                ? $"{Tags.ToUpperInvariant()} — {Title}"
                : Title;

    /// <summary>Anzeige-Datum: echtes Datum (Urteile) > Gültig-ab (Gesetzesfassung) >
    /// Import-Zeitpunkt als letzter Fallback, damit das Popup nie ganz ohne Datum bleibt.</summary>
    public string DatumAnzeige
    {
        get
        {
            if (DateTime.TryParse(Date, out var datum)) return datum.ToString("dd.MM.yyyy");
            if (DateTime.TryParse(ValidFrom, out var gueltig)) return $"gültig ab {gueltig:dd.MM.yyyy}";
            if (DateTime.TryParse(CreatedAt, out var importiert)) return $"importiert am {importiert:dd.MM.yyyy}";
            return "unbekannt";
        }
    }
}

/// <summary>
/// Lädt Gesetzestext-Chunks für Task #80 (klickbare Inline-Quellenangaben) vom Backend.
/// Löst erst die Paragraphen-Referenz (z.B. "§ 23 KSchG") über /legal-db/chunks/by-reference
/// auf den passenden Chunk auf, lädt ihn danach regulär über /legal-db/chunks/{id} — so zeigt
/// das Popup denselben Weg, den auch ein direkter Aufruf per Chunk-ID nähme.
/// </summary>
public class QuellenangabeService
{
    private readonly string _backendUrl;
    private readonly HttpClient _http;

    public QuellenangabeService(string backendUrl, HttpClient http)
    {
        _backendUrl = backendUrl.TrimEnd('/');
        _http = http;
    }

    public async Task<GesetzesChunk?> LadeChunkFuerReferenzAsync(string referenz)
    {
        if (string.IsNullOrWhiteSpace(referenz)) return null;
        var url = $"{_backendUrl}/legal-db/chunks/by-reference?ref={Uri.EscapeDataString(referenz)}";
        var response = await _http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync();
        return ParseChunk(json);
    }

    public async Task<GesetzesChunk?> LadeChunkAsync(int chunkId)
    {
        var response = await _http.GetAsync($"{_backendUrl}/legal-db/chunks/{chunkId}");
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync();
        return ParseChunk(json);
    }

    private static GesetzesChunk ParseChunk(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string? S(string name) => root.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;

        return new GesetzesChunk(
            Id: root.GetProperty("id").GetInt32(),
            Text: S("text") ?? "",
            Title: S("title") ?? "",
            Court: S("court"),
            CaseNumber: S("case_number"),
            Date: S("date"),
            LegalField: S("legal_field"),
            Tags: S("tags"),
            CreatedAt: S("created_at"),
            ValidFrom: S("valid_from"));
    }
}
