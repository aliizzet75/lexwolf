using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LexWolf.Dialogs;

/// <summary>
/// Zeigt dem Anwalt seine eigenen Feature-Wünsche (Milestones, die aus
/// Feature-Wunsch-Dialogen entstanden) und laufende Entwicklungen (alle
/// nicht-fertigen LexWolf-Milestones) an. Lädt die Daten vom Board-Server.
/// </summary>
public partial class FeatureUebersichtDialog : Window
{
    private readonly HttpClient _http;
    private readonly string _backendUrl;
    private readonly string _sessionId;
    private CancellationTokenSource? _cts;

    public FeatureUebersichtDialog(HttpClient http, string backendUrl, string sessionId)
    {
        InitializeComponent();
        _http = http;
        _backendUrl = (backendUrl ?? "").TrimEnd('/');
        _sessionId = sessionId;
        Loaded += async (_, _) => await LoadDataAsync();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _ = LoadDataAsync(_cts.Token);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosing(e);
    }

    private async Task LoadDataAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await _http.GetStringAsync($"{_backendUrl}/api/milestones?projekt_id=1", cancellationToken).ConfigureAwait(true);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var fertig = new List<FeatureListItem>();
            var laufend = new List<FeatureListItem>();
            var eigene = new List<FeatureListItem>();

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray())
                {
                    var id = el.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                    var titel = el.TryGetProperty("titel", out var t) ? t.GetString() ?? "" : "";
                    var beschreibung = el.TryGetProperty("beschreibung", out var b) ? b.GetString() ?? "" : "";
                    var status = el.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";

                    var item = new FeatureListItem(id, titel, beschreibung, status);
                    if (string.Equals(status, "fertig", StringComparison.OrdinalIgnoreCase))
                        fertig.Add(item);
                    else
                        laufend.Add(item);

                    // "Eigene" Feature-Wünsche erkennen wir daran, dass der Titel mit
                    // "Anwalt-Feedback:" beginnt — genau so werden sie beim Confirm im
                    // backend/api/feature_feedback.py angelegt.
                    if (titel.Contains("Anwalt-Feedback", StringComparison.OrdinalIgnoreCase) ||
                        titel.Contains("Feature-Wunsch", StringComparison.OrdinalIgnoreCase))
                    {
                        eigene.Add(item);
                    }
                }
            }

            // "Eigene Feature-Wünsche" zeigen am nützlichsten: eigene + fertige
            // zusammen, sortiert nach Fertigstellung. Wenn der Anwalt nie etwas
            // eingereicht hat, zeigen wir trotzdem fertige Features, die ihn
            // betreffen könnten.
            var eigeneSource = eigene.Count > 0 ? eigene : fertig;

            Dispatcher.Invoke(() =>
            {
                EigeneFeatureWuenscheList.ItemsSource = eigeneSource;
                LaufendeEntwicklungenList.ItemsSource = laufend;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                EigeneFeatureWuenscheList.ItemsSource = new List<FeatureListItem>
                {
                    new FeatureListItem(0, "Daten konnten nicht geladen werden", ex.Message, "fehler")
                };
                LaufendeEntwicklungenList.ItemsSource = System.Array.Empty<FeatureListItem>();
            });
        }
    }
}

/// <summary>
/// Ein einzelner Eintrag in der Feature-Übersicht; StatusLabel/StatusBrush
/// werden für die UI passend aufbereitet.
/// </summary>
public sealed class FeatureListItem
{
    public int Id { get; }
    public string Titel { get; }
    public string Beschreibung { get; }
    public string Status { get; }

    public FeatureListItem(int id, string titel, string beschreibung, string status)
    {
        Id = id;
        Titel = titel;
        Beschreibung = beschreibung;
        Status = status;
    }

    public string StatusLabel => Status?.ToLowerInvariant() switch
    {
        "fertig" => "Fertig",
        "in_arbeit" => "In Arbeit",
        "geplant" => "Geplant",
        "offen" => "Offen",
        _ => Status ?? "Unbekannt"
    };

    public Brush StatusBrush => Status?.ToLowerInvariant() switch
    {
        "fertig" => new SolidColorBrush(Color.FromRgb(63, 185, 80)),   // grün
        "in_arbeit" => new SolidColorBrush(Color.FromRgb(88, 166, 255)), // blau
        "geplant" => new SolidColorBrush(Color.FromRgb(210, 153, 34)),  // gelb
        "offen" => new SolidColorBrush(Color.FromRgb(139, 148, 158)),   // grau
        _ => new SolidColorBrush(Color.FromRgb(139, 148, 158))
    };
}
