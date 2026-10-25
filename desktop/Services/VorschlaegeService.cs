using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LexWolf.Services;

/// <summary>Ein einzelner Post-Gespräch-Vorschlag (Task #131, Pilot-Scope: Fristennotiz
/// und E-Mail-Entwurf), wie er vom Backend (POST /conversation/suggestions) geliefert wird.
/// Typ ist "fristennotiz" oder "email_entwurf" (siehe ConversationAssistant.VORSCHLAG_TYP_*);
/// Metadata enthält je Typ unterschiedliche Felder (z.B. "datum" bei Fristennotiz).</summary>
public sealed record Vorschlag(string Typ, string Titel, string Text, JsonElement Metadata);

/// <summary>
/// Ruft nach einem Mandantengespräch über das Backend Vorschläge für die nächsten
/// Schritte ab (Fristennotiz, E-Mail-Entwurf, ...) und meldet die Anwalt-Entscheidung
/// (Ja/Nein/Geändert) zurück an den Server. Dort fließt die Entscheidung in die
/// bestehende Lernschleife (style_learner/feedback_service) ein — dieser Dienst
/// speichert selbst nichts lokal.
/// </summary>
public static class VorschlaegeService
{
    /// <summary>Strukturiert ein (bereits lokal transkribiertes) Gespräch über
    /// POST /conversation/analyze. Gibt das rohe JSON-Element zurück, damit es
    /// unverändert an /conversation/suggestions weitergegeben werden kann.</summary>
    public static async Task<JsonElement> AnalysiereTranskriptAsync(
        HttpClient http, string backendUrl, string transkript)
    {
        var payload = JsonSerializer.Serialize(new { transcript = transkript });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await http.PostAsync($"{backendUrl}/conversation/analyze", content).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    /// <summary>Liest die "themen"-Liste aus einem /conversation/analyze-Ergebnis
    /// (für eine kurze Zusammenfassung, z.B. beim Speichern via LocalDb.InsertGespraech).</summary>
    public static List<string> LeseThemen(JsonElement strukturiert)
    {
        if (strukturiert.ValueKind == JsonValueKind.Object &&
            strukturiert.TryGetProperty("themen", out var themen) &&
            themen.ValueKind == JsonValueKind.Array)
        {
            return themen.EnumerateArray()
                .Select(t => t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "")
                .Where(t => t.Length > 0)
                .ToList();
        }
        return new List<string>();
    }

    /// <summary>Erzeugt aus dem strukturierten Gesprächsergebnis konkrete Vorschläge
    /// über POST /conversation/suggestions (siehe ConversationAssistant.generate_suggestions).</summary>
    public static async Task<List<Vorschlag>> HoleVorschlaegeAsync(
        HttpClient http, string backendUrl, JsonElement strukturiert, IReadOnlyList<string>? mandantHistory = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            strukturiert,
            mandant_history = mandantHistory ?? new List<string>(),
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await http.PostAsync($"{backendUrl}/conversation/suggestions", content).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        var ergebnis = new List<Vorschlag>();
        if (!doc.RootElement.TryGetProperty("vorschlaege", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return ergebnis;

        foreach (var item in arr.EnumerateArray())
        {
            string S(string key) => item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                ? (v.GetString() ?? "") : "";
            // .Clone(): das umschliessende JsonDocument wird am Ende dieser Methode disposed,
            // die Metadata muss den Dialog aber ueberleben.
            var metadata = item.TryGetProperty("metadata", out var m) ? m.Clone() : default;
            ergebnis.Add(new Vorschlag(S("typ"), S("titel"), S("text"), metadata));
        }
        return ergebnis;
    }

    /// <summary>Meldet die Anwalt-Entscheidung (Ja/Nein/Geändert) zu einem Vorschlag an
    /// POST /conversation/suggestions/decision. Der Server speist sie dort in die
    /// bestehende Lernschleife ein (style_learner immer, feedback_service zusätzlich
    /// bei "geaendert"). Gibt false zurück, wenn der Server nicht erreichbar ist oder
    /// die Anfrage ablehnt — der Dialog-Fluss läuft trotzdem weiter.</summary>
    public static async Task<bool> SendeEntscheidungAsync(
        HttpClient http, string backendUrl, string vorschlagTyp, string vorschlagText,
        string entscheidung, string? geaenderterText = null, string accountId = "default")
    {
        var payload = JsonSerializer.Serialize(new
        {
            vorschlag_typ = vorschlagTyp,
            vorschlag_text = vorschlagText,
            entscheidung,
            geaenderter_text = geaenderterText,
            account_id = accountId,
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        try
        {
            var response = await http.PostAsync($"{backendUrl}/conversation/suggestions/decision", content).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (System.Exception)
        {
            return false;
        }
    }
}
