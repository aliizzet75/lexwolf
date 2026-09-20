using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LexWolf.Database;

namespace LexWolf.Services;

/// <summary>
/// Analyse-Status pro Mandant: Idle = noch nicht gescannt, Scanning = läuft,
/// Ready = Hintergrund-Scan abgeschlossen.
/// </summary>
public enum AnalyseStatus
{
    Idle,
    Scanning,
    Ready
}

/// <summary>
/// Verwaltet einen abbrechbaren Hintergrund-Scan pro Mandant. Beim Mandanten-
/// Wechsel wird für den neuen Mandanten ein Scan gestartet, der Chat-
/// Zusammenfassungen, Notizen und indizierte Dokumente in einer internen
/// Zusammenfassung kondensiert. Ein Wechsel zu einem anderen Mandanten bricht
/// den laufenden Scan ab bzw. ignoriert dessen Ergebnis.
/// </summary>
public class MandantAnalyseService
{
    private readonly LocalDb _db;
    private readonly HttpClient _http;
    private readonly string _backendUrl;

    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private string? _activeMandantId;

    /// <summary>
    /// Aktueller Analyse-Status des zuletzt angestoßenen Mandanten.
    /// </summary>
    public AnalyseStatus Status { get; private set; } = AnalyseStatus.Idle;

    /// <summary>
    /// Wird aufgerufen, wenn sich der Analyse-Status ändert.
    /// </summary>
    public event EventHandler<AnalyseStatus>? StatusChanged;

    public MandantAnalyseService(LocalDb db, HttpClient http, string backendUrl)
    {
        _db = db;
        _http = http;
        _backendUrl = backendUrl;
    }

    /// <summary>
    /// Startet den Hintergrund-Scan für den angegebenen Mandanten. Bricht einen
    /// bereits laufenden Scan für einen anderen Mandanten sauber ab.
    /// </summary>
    public void StarteScan(string mandantId)
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _activeMandantId = mandantId;
            SetStatus(AnalyseStatus.Scanning);
        }

        _ = ScanAsync(mandantId, _cts.Token);
    }

    /// <summary>
    /// Bricht den aktuellen Scan ab, z. B. beim Wechsel zu einem anderen Mandanten
    /// oder beim Beenden der Anwendung.
    /// </summary>
    public void Abbrechen()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _activeMandantId = null;
            SetStatus(AnalyseStatus.Idle);
        }
    }

    private async Task ScanAsync(string mandantId, CancellationToken token)
    {
        try
        {
            // Chat-Zusammenfassungen, Notizen und Dokumente parallel sammeln.
            // Der bestehende Dokument-Watcher/Index (DokumentScanner) füllt die
            // Tabelle "dokumente" — wir lesen daraus, anstatt selbst Dateien zu
            // scannen, damit keine Duplikate entstehen und der Scan schnell bleibt.
            var chatTask = Task.Run(() => _db.GetChatZusammenfassungen(mandantId), token);
            var notizenTask = Task.Run(() => _db.GetNotizen(mandantId), token);
            var dokumenteTask = Task.Run(() => _db.GetDokumenteForMandant(mandantId), token);

            await Task.WhenAll(chatTask, notizenTask, dokumenteTask).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();

            var chat = await chatTask.ConfigureAwait(false);
            var notizen = await notizenTask.ConfigureAwait(false);
            var dokumente = await dokumenteTask.ConfigureAwait(false);

            var kontext = BaueKontext(chat, notizen, dokumente);
            if (!string.IsNullOrWhiteSpace(kontext))
            {
                // Hintergrund-Analyse via LLM; Fehler werden abgefangen, damit die
                // App stabil bleibt.
                _ = await AnalysiereKontextAsync(kontext, token).ConfigureAwait(false);
            }

            lock (_lock)
            {
                if (_activeMandantId == mandantId && !token.IsCancellationRequested)
                    SetStatus(AnalyseStatus.Ready);
            }
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[MandantAnalyseService] Scan für Mandant {mandantId} abgebrochen.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[MandantAnalyseService] Scan für Mandant {mandantId} fehlgeschlagen: {ex.Message}");
        }
    }

    private static string BaueKontext(
        List<(string Id, string MandantId, DateTime SitzungStart, DateTime SitzungEnde,
              string Zusammenfassung, DateTime Erstellt)> chat,
        List<(string Id, string MandantId, string TitelKurz, string Text,
              DateTime Erstellt, DateTime Geaendert)> notizen,
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

    private async Task<string> AnalysiereKontextAsync(string kontext, CancellationToken token)
    {
        var payload = JsonSerializer.Serialize(new
        {
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = "Analysiere den folgenden Mandanten-Kontext aus Chat, Notizen und Dokumenten " +
                               "und gib eine sehr kurze Stichwort-Zusammenfassung zurück (max. 5 Stichpunkte):\n\n" + kontext
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

    private void SetStatus(AnalyseStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }
}
