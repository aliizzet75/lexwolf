using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LexWolf.Database;

namespace LexWolf.Services;

/// <summary>
/// Erzeugt eine aggregierte KI-Zusammenfassung aus Chat-Zusammenfassungen,
/// Notizen und Dokumenten eines Mandanten. Cacht Ergebnis lokal in der DB
/// zusammen mit einem Content-Hash der Quellen. Bei neuen Inhalten wird
/// automatisch neu generiert; bei unveränderten Quellen wird der gecachte
/// Text sofort zurückgegeben.
/// </summary>
public class MandantZusammenfassungService
{
    private readonly LocalDb _db;
    private readonly HttpClient _http;
    private readonly string _backendUrl;

    public MandantZusammenfassungService(LocalDb db, HttpClient http, string backendUrl)
    {
        _db = db;
        _http = http;
        _backendUrl = backendUrl;
    }

    /// <summary>
    /// Liefert die aktuelle Zusammenfassung für einen Mandanten. Wenn ein Cache-
    /// Eintrag existiert und der Hash der Quellen noch passt, wird dieser sofort
    /// zurückgegeben; sonst wird neu generiert und gecacht.
    /// </summary>
    public async Task<string> HoleOderErzeugeZusammenfassungAsync(string mandantId, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(mandantId)) return "";

        var (chat, notizen, dokumente) = await LadeQuellenAsync(mandantId, token).ConfigureAwait(false);
        var quellenHash = BerechneQuellenHash(chat, notizen, dokumente);
        var gecacht = await Task.Run(() => _db.GetMandantZusammenfassung(mandantId), token).ConfigureAwait(false);

        if (gecacht.HasValue && gecacht.Value.QuellenHash == quellenHash && !string.IsNullOrWhiteSpace(gecacht.Value.Text))
            return gecacht.Value.Text!;

        var kontext = BaueKontext(chat, notizen, dokumente);
        if (string.IsNullOrWhiteSpace(kontext))
        {
            var leerText = "Noch keine Chats, Notizen oder Dokumente für diesen Mandanten vorhanden.";
            await Task.Run(() => _db.UpsertMandantZusammenfassung(mandantId, leerText, quellenHash), token).ConfigureAwait(false);
            return leerText;
        }

        var zusammenfassung = await GeneriereZusammenfassungAsync(kontext, token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(zusammenfassung))
            zusammenfassung = "Zusammenfassung konnte nicht erzeugt werden.";

        await Task.Run(() => _db.UpsertMandantZusammenfassung(mandantId, zusammenfassung, quellenHash), token).ConfigureAwait(false);
        return zusammenfassung;
    }

    private async Task<(
        List<(string Id, string MandantId, DateTime SitzungStart, DateTime SitzungEnde, string Zusammenfassung, DateTime Erstellt)> Chat,
        List<(string Id, string MandantId, string TitelKurz, string Text, DateTime Erstellt, DateTime Geaendert)> Notizen,
        List<(string Titel, string Pfad, string Text)> Dokumente)
    > LadeQuellenAsync(string mandantId, CancellationToken token)
    {
        var chatTask = Task.Run(() => _db.GetChatZusammenfassungen(mandantId), token);
        var notizenTask = Task.Run(() => _db.GetNotizen(mandantId), token);
        var dokumenteTask = Task.Run(() => _db.GetDokumenteForMandant(mandantId), token);

        await Task.WhenAll(chatTask, notizenTask, dokumenteTask).ConfigureAwait(false);
        return (await chatTask.ConfigureAwait(false), await notizenTask.ConfigureAwait(false), await dokumenteTask.ConfigureAwait(false));
    }

    private static string BerechneQuellenHash(
        List<(string Id, string MandantId, DateTime SitzungStart, DateTime SitzungEnde, string Zusammenfassung, DateTime Erstellt)> chat,
        List<(string Id, string MandantId, string TitelKurz, string Text, DateTime Erstellt, DateTime Geaendert)> notizen,
        List<(string Titel, string Pfad, string Text)> dokumente)
    {
        var sb = new StringBuilder();
        foreach (var c in chat.OrderBy(c => c.SitzungStart))
            sb.AppendLine($"CHAT {c.SitzungEnde:o} {c.Zusammenfassung}");
        foreach (var n in notizen.OrderBy(n => n.Geaendert))
            sb.AppendLine($"NOTIZ {n.Geaendert:o} {n.TitelKurz} {n.Text}");
        foreach (var d in dokumente.OrderBy(d => d.Pfad))
            sb.AppendLine($"DOK {d.Pfad} {d.Titel} {d.Text}");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string BaueKontext(
        List<(string Id, string MandantId, DateTime SitzungStart, DateTime SitzungEnde, string Zusammenfassung, DateTime Erstellt)> chat,
        List<(string Id, string MandantId, string TitelKurz, string Text, DateTime Erstellt, DateTime Geaendert)> notizen,
        List<(string Titel, string Pfad, string Text)> dokumente)
    {
        var sb = new StringBuilder();
        if (chat.Count > 0)
        {
            sb.AppendLine("Chat-Zusammenfassungen:");
            foreach (var item in chat.OrderByDescending(c => c.SitzungEnde))
                sb.AppendLine($"- {item.Zusammenfassung}");
            sb.AppendLine();
        }
        if (notizen.Count > 0)
        {
            sb.AppendLine("Notizen:");
            foreach (var item in notizen.OrderByDescending(n => n.Geaendert))
                sb.AppendLine($"- {item.TitelKurz}: {item.Text}");
            sb.AppendLine();
        }
        if (dokumente.Count > 0)
        {
            sb.AppendLine("Dokumente:");
            foreach (var item in dokumente.Take(10))
            {
                var vorschau = item.Text.Length > 200 ? item.Text[..200] + "..." : item.Text;
                sb.AppendLine($"- {item.Titel}: {vorschau.Replace("\n", " ")}");
            }
        }
        return sb.ToString().Trim();
    }

    private async Task<string> GeneriereZusammenfassungAsync(string kontext, CancellationToken token)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = "Erstelle eine übersichtliche, aber vollständige Zusammenfassung für den Anwalt " +
                                   "aus folgenden Quellen (Chat, Notizen, Dokumente). " +
                                   "Struktur: 1) Aktueller Stand, 2) Offene Punkte, 3) Nächste Schritte.\n\n" + kontext
                    }
                }
            });
            using var httpContent = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync($"{_backendUrl}/chat", httpContent, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MandantZusammenfassungService] LLM-Generierung fehlgeschlagen: {ex.Message}");
            return "";
        }
    }
}
