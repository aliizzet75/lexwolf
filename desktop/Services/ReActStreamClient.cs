using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LexWolf.Models;

namespace LexWolf.Services;

public class ReActStreamNotReachableException : Exception
{
    public ReActStreamNotReachableException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>
/// Konsumiert den Server-Sent-Events-Stream des ReAct-Suchendpunkts
/// (GET {backend}/api/search/stream?q=...&amp;mode=...) für die Live-
/// Denkprozess-Visualisierung im Client (T#295). Jede SSE-"data:"-Zeile
/// ist ein JSON-Objekt {"step","text",...optional quelle/confidence/status}.
/// </summary>
public class ReActStreamClient
{
    private readonly HttpClient _http;
    private readonly string _backendUrl;

    public ReActStreamClient(HttpClient http, string backendUrl)
    {
        _http = http;
        _backendUrl = backendUrl.TrimEnd('/');
    }

    /// <summary>
    /// Parst eine einzelne SSE-Datenzeile ("data: {json}", ohne Zeilenumbrüche)
    /// in ein ReActStepEvent. Gibt null zurück für Zeilen, die kein SSE-"data:"-
    /// Feld sind, oder bei ungültigem JSON — rein, damit ohne Netzwerk testbar.
    /// </summary>
    public static ReActStepEvent? ParseSseLine(string line)
    {
        if (!line.StartsWith("data:", StringComparison.Ordinal))
            return null;

        var json = line.Substring(5).TrimStart();
        if (json.Length == 0)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string step = root.TryGetProperty("step", out var s) ? s.GetString() ?? "" : "";
            string text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            string? quelle = root.TryGetProperty("quelle", out var q) ? q.GetString() : null;
            string? status = root.TryGetProperty("status", out var st) ? st.GetString() : null;
            double? confidence = root.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number
                ? c.GetDouble()
                : null;

            return new ReActStepEvent
            {
                Step = step,
                Text = text,
                Quelle = quelle,
                Status = status,
                Confidence = confidence,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Streamt live die ReAct-Suchschritte für eine Anfrage. modus ist
    /// "schnell" oder "tief" (siehe api/routes/search.py).
    /// </summary>
    public async IAsyncEnumerable<ReActStepEvent> StreamAsync(
        string query,
        string modus = "schnell",
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var url = $"{_backendUrl}/api/search/stream?q={Uri.EscapeDataString(query)}&mode={Uri.EscapeDataString(modus)}";

        HttpResponseMessage response;
        try
        {
            response = await _http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ReActStreamNotReachableException($"ReAct-Stream nicht erreichbar unter {_backendUrl}", ex);
        }

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            ct.ThrowIfCancellationRequested();
            var evt = ParseSseLine(line);
            if (evt != null)
                yield return evt;
        }
    }
}
