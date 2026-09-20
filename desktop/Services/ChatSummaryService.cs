using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LexWolf.Database;

namespace LexWolf.Services;

/// <summary>
/// Erzeugt im Hintergrund eine KI-Zusammenfassung des Chat-Verlaufs eines
/// Mandanten (seit der letzten Zusammenfassung) und speichert sie über
/// InsertChatZusammenfassung(). Wird bei Mandant-Wechsel bzw. wenn die App in
/// den Hintergrund geht ausgelöst. Läuft komplett async über await, blockiert den
/// aufrufenden Thread nie, und verschluckt/loggt Fehler statt die Anwendung zu
/// blockieren — ein fehlgeschlagener Hintergrund-Job darf den Anwalt nicht bei
/// der Arbeit stören.
/// </summary>
public class ChatSummaryService
{
    private readonly LocalDb _db;
    private readonly HttpClient _http;
    private readonly string _backendUrl;

    public ChatSummaryService(LocalDb db, HttpClient http, string backendUrl)
    {
        _db = db;
        _http = http;
        _backendUrl = backendUrl;
    }

    public async Task SummarizeSessionAsync(string? mandantId)
    {
        if (string.IsNullOrEmpty(mandantId)) return;

        try
        {
            var seit    = await Task.Run(() => _db.GetLetzteZusammenfassungEnde(mandantId)).ConfigureAwait(false);
            var verlauf = await Task.Run(() => _db.GetChatHistorySeit(mandantId, seit)).ConfigureAwait(false);
            if (verlauf.Count == 0) return;

            var sitzungStart = verlauf.Min(m => m.Timestamp);
            var sitzungEnde  = verlauf.Max(m => m.Timestamp);

            var transkript = string.Join("\n", verlauf.Select(m => $"{m.Role}: {m.Content}"));
            var zusammenfassung = await GenerateZusammenfassungAsync(transkript).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(zusammenfassung)) return;

            await Task.Run(() =>
                _db.InsertChatZusammenfassung(mandantId, sitzungStart, sitzungEnde, zusammenfassung)
            ).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ChatSummaryService] Zusammenfassung für Mandant {mandantId} fehlgeschlagen: {ex.Message}");
        }
    }

    private async Task<string> GenerateZusammenfassungAsync(string transkript)
    {
        var payload = JsonSerializer.Serialize(new
        {
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = "Fasse den folgenden Chat-Verlauf in wenigen Sätzen sachlich für die Mandantenakte zusammen:\n\n" + transkript
                }
            }
        });
        using var httpContent = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync($"{_backendUrl}/chat", httpContent).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
    }
}
