using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LexWolf;

public partial class MainWindow : Window
{
    private const string BackendUrl = "http://212.227.180.66:8000";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public MainWindow()
    {
        InitializeComponent();
        _ = CheckConnectionAsync(showConnecting: true);
        _ = PeriodicHealthCheckAsync();
    }

    private async Task PeriodicHealthCheckAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            await CheckConnectionAsync(showConnecting: false);
        }
    }

    private async Task CheckConnectionAsync(bool showConnecting = false)
    {
        if (showConnecting) SetStatus(null, "Verbinde...");
        try
        {
            await _http.GetStringAsync($"{BackendUrl}/health");
            SetStatus(true, $"Verbunden — {BackendUrl}");
        }
        catch
        {
            SetStatus(false, "Backend nicht erreichbar");
        }
    }

    private void SetStatus(bool? online, string message)
    {
        Dispatcher.Invoke(() =>
        {
            StatusDot.Fill = online switch
            {
                true  => new SolidColorBrush(Color.FromRgb(63, 185, 80)),
                false => new SolidColorBrush(Color.FromRgb(248, 81, 73)),
                null  => new SolidColorBrush(Color.FromRgb(100, 110, 120)),
            };
            StatusText.Text = message;
        });
    }

    private async void OnReconnect(object sender, RoutedEventArgs e)
    {
        await CheckConnectionAsync(showConnecting: true);
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) OnSend(sender, e);
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        var text = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        SendBtn.IsEnabled = false;
        ReasoningPanel.Children.Clear();
        ResultBox.Text = "Anfrage wird verarbeitet...";
        MetaText.Text = "";

        AddReasoning("⏳", "Anfrage gesendet...");

        try
        {
            var payload = JsonSerializer.Serialize(new { text });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{BackendUrl}/ask", content);
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            ReasoningPanel.Children.Clear();
            if (root.TryGetProperty("reasoning", out var reasoning))
            {
                foreach (var step in reasoning.EnumerateArray())
                {
                    var emoji = step.TryGetProperty("emoji", out var em) ? em.GetString() ?? "•" : "•";
                    var stepText = step.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                    AddReasoning(emoji, stepText);
                }
            }

            var output = root.TryGetProperty("output", out var o) ? o.GetString() ?? "" : json;
            ResultBox.Text = output;

            var intent = root.TryGetProperty("intent", out var i) ? i.GetString() ?? "" : "";
            var ms = root.TryGetProperty("duration_ms", out var d) ? d.GetInt32() : 0;
            MetaText.Text = $"{intent}  •  {ms} ms";

            SetStatus(true, $"Verbunden — {BackendUrl}");
        }
        catch (Exception ex)
        {
            SetStatus(false, "Backend nicht erreichbar");
            ReasoningPanel.Children.Clear();
            AddReasoning("❌", $"Fehler: {ex.Message}");
            ResultBox.Text = $"Verbindungsfehler:\n{ex.Message}";
        }
        finally
        {
            SendBtn.IsEnabled = true;
        }
    }

    private void AddReasoning(string emoji, string text)
    {
        Dispatcher.Invoke(() =>
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            row.Children.Add(new TextBlock
            {
                Text = emoji,
                FontSize = 16,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Top
            });
            row.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(201, 209, 217)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top
            });
            ReasoningPanel.Children.Add(row);
        });
    }
}
