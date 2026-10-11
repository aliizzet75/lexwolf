using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LexWolf.Database;

namespace LexWolf.Services;

/// <summary>
/// Repräsentiert den vom Server gemeldeten Fertigstellungsstatus eines Features.
/// </summary>
public record FeatureStatus(
    int Id,
    string Titel,
    string Beschreibung,
    string Status,
    DateTimeOffset? FertiggestelltAm);

/// <summary>
/// Periodischer Hintergrund-Service für LexWolf: fragt alle konfigurierbaren
/// Intervalle (Default 30 Minuten) den Server nach fertiggestellten Milestones
/// ab und zeigt im Client einen Popup/Toast mit Neustart- und Erinnern-Option.
/// </summary>
public sealed class FeatureStatusChecker : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _backendUrl;
    private readonly FeatureNotificationStore _store;
    private readonly TimeSpan _interval;
    private readonly Func<Window?> _ownerFactory;
    private readonly Action<string> _log;

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _disposed;

    public FeatureStatusChecker(
        HttpClient http,
        string backendUrl,
        LocalDb db,
        Func<Window?> ownerFactory,
        TimeSpan? interval = null,
        Action<string>? log = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _backendUrl = (backendUrl ?? "").TrimEnd('/');
        _ownerFactory = ownerFactory ?? throw new ArgumentNullException(nameof(ownerFactory));
        _interval = interval ?? TimeSpan.FromMinutes(30);
        _log = log ?? (_ => { });
        _store = new FeatureNotificationStore(db ?? throw new ArgumentNullException(nameof(db)), _log);
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FeatureStatusChecker));
            if (_loop is not null) return;

            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => LoopAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _cts?.Cancel();
        }
    }

    /// <summary>
    /// Sofortiger einmaliger Check (z. B. beim App-Start), ohne die Schleife zu stören.
    /// </summary>
    public async Task CheckOnceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var fertige = await FetchFertigeFeaturesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var feature in fertige)
                await ShowNotificationAsync(feature, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log($"[FeatureStatusChecker] Sofort-Check fehlgeschlagen: {ex.Message}");
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _cts?.Dispose();
        }
        _loop?.Wait(TimeSpan.FromSeconds(5));
    }

    private readonly object _lock = new();

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        // Erster Check direkt nach Start, dann periodisch.
        await CheckOnceAsync(cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await CheckOnceAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<FeatureStatus>> FetchFertigeFeaturesAsync(CancellationToken cancellationToken)
    {
        var list = new List<FeatureStatus>();
        try
        {
            var response = await _http.GetAsync(
                $"{_backendUrl}/api/milestones?projekt_id=1&status=fertig",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray())
                {
                    var id = el.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                    if (id == 0) continue;
                    var titel = el.TryGetProperty("titel", out var t) ? t.GetString() ?? "" : "";
                    var beschreibung = el.TryGetProperty("beschreibung", out var b) ? b.GetString() ?? "" : "";
                    var status = el.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";

                    DateTimeOffset? fertiggestelltAm = null;
                    if (el.TryGetProperty("fertiggestellt_am", out var fa) &&
                        fa.ValueKind == JsonValueKind.String &&
                        DateTimeOffset.TryParse(fa.GetString(), out var dto))
                    {
                        fertiggestelltAm = dto;
                    }

                    if (string.Equals(status, "fertig", StringComparison.OrdinalIgnoreCase) &&
                        !_store.IsAlreadySeen(id))
                    {
                        list.Add(new FeatureStatus(id, titel, beschreibung, status, fertiggestelltAm));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log($"[FeatureStatusChecker] Abruf der Feature-Status fehlgeschlagen: {ex.Message}");
        }
        return list;
    }

    private async Task ShowNotificationAsync(FeatureStatus feature, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<string?>();
        var owner = _ownerFactory();
        _ = (owner?.Dispatcher ?? Dispatcher.CurrentDispatcher).InvokeAsync(() =>
        {
            try
            {
                var result = MessageBox.Show(
                    owner,
                    $"🎉 Feature fertiggestellt: {feature.Titel}\n\n" +
                    $"{feature.Beschreibung}\n\n" +
                    "Möchten Sie LexWolf jetzt neu starten, um das Feature zu nutzen?\n\n" +
                    "• Ja = Client jetzt neu starten\n" +
                    "• Nein = Später erinnern\n" +
                    "• Abbrechen = Später erinnern",
                    "Feature verfügbar",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Information,
                    MessageBoxResult.No);

                tcs.TrySetResult(result switch
                {
                    MessageBoxResult.Yes => "restart",
                    MessageBoxResult.No => "later",
                    MessageBoxResult.Cancel => "later",
                    _ => null
                });
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, DispatcherPriority.Normal);

        var reaction = await tcs.Task.ConfigureAwait(false);
        if (reaction is null) return;

        _store.MarkAsSeen(feature.Id, reaction);

        if (reaction == "restart")
        {
            _ = (owner?.Dispatcher ?? Dispatcher.CurrentDispatcher).InvokeAsync(() =>
            {
                try
                {
                    RelaunchAndShutdown();
                }
                catch (Exception ex)
                {
                    _log($"[FeatureStatusChecker] Neustart konnte nicht ausgelöst werden: {ex.Message}");
                }
            });
        }
    }

    /// <summary>
    /// Startet eine neue Instanz der aktuell laufenden .exe und beendet danach die
    /// laufende Instanz. Nur Shutdown() aufzurufen (wie zuvor) schließt den Client,
    /// startet ihn aber NICHT neu — der Anwalt müsste manuell neu öffnen.
    /// </summary>
    private static void RelaunchAndShutdown()
    {
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(exePath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath)
            {
                UseShellExecute = true
            });
        }

        System.Windows.Application.Current.Shutdown();
    }
}
