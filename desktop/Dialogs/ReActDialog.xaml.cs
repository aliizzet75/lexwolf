using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LexWolf.Models;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>
/// Live-Denkprozess-Fenster (T#295): zeigt den ReAct-Suchverlauf aus
/// GET /api/search/stream in Echtzeit — welche Quelle durchsucht wird,
/// was gefunden/verworfen wurde und mit welcher Konfidenz pro Schritt.
/// Ersetzt die bisherige Black-Box-Suche durch einen nachvollziehbaren
/// Suchweg (Antwort auf die fehlende Marken-Autorität, siehe Task-Notiz).
/// </summary>
public partial class ReActDialog : Window
{
    // Eigener HttpClient mit unbegrenztem Timeout statt der geteilten
    // MainWindow-Instanz (120s) — der Tief-Modus durchläuft bis zu 5 Runden
    // plus Reranking/Gegensuche über die LLM-Schnittstelle und kann länger
    // dauern als ein normaler Request/Response-Call.
    private readonly HttpClient _streamHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly ReActStreamClient _client;
    private CancellationTokenSource? _cts;

    public ReActDialog(string backendUrl)
    {
        InitializeComponent();
        _client = new ReActStreamClient(_streamHttp, backendUrl);
    }

    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = RunSearchAsync();
        }
    }

    private void OnSearchClick(object sender, RoutedEventArgs e) => _ = RunSearchAsync();

    private string SelectedModus
    {
        get
        {
            var item = ModusBox.SelectedItem as ComboBoxItem;
            return item?.Tag?.ToString() ?? "schnell";
        }
    }

    private async Task RunSearchAsync()
    {
        var query = QueryBox.Text.Trim();
        if (query.Length == 0) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        StepsList.Items.Clear();
        AnswerText.Text = "";
        SetBusy(true);

        try
        {
            await foreach (var evt in _client.StreamAsync(query, SelectedModus, ct).ConfigureAwait(true))
            {
                if (evt.Step == "antwort_chunk")
                    AnswerText.Text += evt.Text;
                else
                    AddStepRow(evt);
                StepsScroll.ScrollToEnd();
            }
        }
        catch (OperationCanceledException)
        {
            // Neue Suche hat diese abgelöst — kein Fehlerzustand
        }
        catch (ReActStreamNotReachableException ex)
        {
            AddErrorRow(ex.Message);
        }
        catch (Exception ex)
        {
            AddErrorRow($"Fehler: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void AddErrorRow(string message)
    {
        StepsList.Items.Add(BuildRow("⚠️", "#f85149", "Fehler", message, null, null));
    }

    private void AddStepRow(ReActStepEvent evt)
    {
        var (icon, color) = evt.Step switch
        {
            "suche_start" => ("🔎", "#8b949e"),
            "bewertung" => ("📊", "#8b949e"),
            "fertig" => ("✅", "#3fb950"),
            "suchergebnis" when evt.IstVerworfen => ("✗", "#f85149"),
            "suchergebnis" => ("✓", "#3fb950"),
            _ => ("•", "#8b949e"),
        };

        var quelle = evt.Quelle ?? evt.Step;
        StepsList.Items.Add(BuildRow(icon, color, quelle, evt.Text, evt.Confidence, evt.Status));
    }

    private UIElement BuildRow(string icon, string colorHex, string quelle, string text, double? confidence, string? status)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorHex);
        var brush = new SolidColorBrush(color);

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = icon,
            Foreground = brush,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 6, 0),
        });
        header.Children.Add(new TextBlock
        {
            Text = quelle,
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
        });

        if (confidence.HasValue)
        {
            header.Children.Add(new TextBlock
            {
                Text = $"  Konfidenz: {confidence.Value * 100:F0}%",
                Foreground = brush,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            });
        }

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8b, 0x94, 0x9e)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(19, 2, 0, 0),
        });

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1b, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3d)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 6),
            Child = body,
        };
    }

    private void SetBusy(bool busy)
    {
        SearchBtn.IsEnabled = !busy;
        QueryBox.IsEnabled = !busy;
        ModusBox.IsEnabled = !busy;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _streamHttp.Dispose();
        base.OnClosed(e);
    }
}
