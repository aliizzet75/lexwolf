using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;

namespace LexWolf;

record ChatMessage(string Role, string Content);

public partial class MainWindow : Window
{
    private const string BackendUrl = "http://212.227.180.66:8000";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private readonly List<ChatMessage> _history = new();

    public MainWindow()
    {
        InitializeComponent();
        _ = CheckConnectionAsync(showConnecting: true);
        _ = PeriodicHealthCheckAsync();
        AppendSystemMessage("Willkommen bei LexWolf. Wie kann ich Ihnen helfen?");
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

    private void OnClearChat(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        ChatPanel.Children.Clear();
        AppendSystemMessage("Chat gelöscht. Wie kann ich Ihnen helfen?");
    }

    // Ctrl+Enter sendet; Enter erzeugt neue Zeile
    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            OnSend(sender, e);
        }
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        var text = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        InputBox.Clear();
        SendBtn.IsEnabled = false;

        // User-Nachricht anzeigen und zur History hinzufügen
        _history.Add(new ChatMessage("user", text));
        AppendUserMessage(text);

        AddReasoning("⏳", "Anfrage wird gesendet...");

        try
        {
            // SSE Stream aufrufen für Live-Streaming des Denkprozesses
            await CallSseStreamAsync(text);

            ReasoningPanel.Children.Clear();
            AddReasoning("✅", "Denkprozess abgeschlossen");
            SetStatus(true, $"Verbunden — {BackendUrl}");
        }
        catch (Exception ex)
        {
            SetStatus(false, "Backend nicht erreichbar");
            ReasoningPanel.Children.Clear();
            AddReasoning("❌", $"Fehler: {ex.Message}");
            AppendAiMessage($"Verbindungsfehler: {ex.Message}", "frage");
        }
        finally
        {
            SendBtn.IsEnabled = true;
        }
    }

    private async Task CallSseStreamAsync(string query)
    {
        AddReasoning("🔍", $"Starte SSE-Stream: {query}");

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BackendUrl}/api/search/stream?q={Uri.EscapeDataString(query)}");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            // SSE format: "data: {{\"step\": \"...\", \"text\": \"...\"}}"
            if (line.StartsWith("data: "))
            {
                var jsonData = line.Substring(6);
                try
                {
                    using var doc = JsonDocument.Parse(jsonData);
                    var root = doc.RootElement;
                    var step = root.TryGetProperty("step", out var stepProp) ? stepProp.GetString() : "";
                    var text = root.TryGetProperty("text", out var textProp) ? textProp.GetString() : "";

                    if (!string.IsNullOrEmpty(step) && !string.IsNullOrEmpty(text))
                    {
                        DisplaySseEvent(step, text);
                    }
                }
                catch
                {
                    // Invalid JSON, skip
                }
            }
        }

        AddReasoning("🏁", "SSE-Stream abgeschlossen");
    }

    private void DisplaySseEvent(string step, string text)
    {
        Dispatcher.Invoke(() =>
        {
            // Step zu Emoji Mapping
            var emoji = step switch
            {
                "suche_start" => "🔍",
                "suchergebnis" => "📚",
                "bewertung" => "📊",
                "antwort_chunk" => "🤖",
                "fertig" => "✅",
                _ => "⚙️"
            };

            AddReasoning(emoji, $"{step}: {text}");
        });
    }

    private void AppendSystemMessage(string text)
    {
        Dispatcher.Invoke(() =>
        {
            var border = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 4, 0, 8),
            };
            border.Child = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            };
            ChatPanel.Children.Add(border);
            ScrollToBottom();
        });
    }

    // User-Nachricht: rechts, blau (wie WhatsApp)
    private void AppendUserMessage(string text)
    {
        Dispatcher.Invoke(() =>
        {
            var bubble = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Color.FromRgb(31, 111, 235)),
                CornerRadius = new CornerRadius(16, 4, 16, 16),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(60, 4, 8, 4),
                MaxWidth = 500,
            };
            bubble.Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
            };
            ChatPanel.Children.Add(bubble);
            ScrollToBottom();
        });
    }

    // AI-Antwort: links, grau (wie WhatsApp)
    private void AppendAiMessage(string text, string suggestedAction)
    {
        Dispatcher.Invoke(() =>
        {
            var container = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(8, 4, 60, 4),
            };

            var bubble = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                CornerRadius = new CornerRadius(4, 16, 16, 16),
                Padding = new Thickness(14, 10, 14, 10),
                MaxWidth = 500,
            };
            bubble.Child = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(201, 209, 217)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
            };
            container.Children.Add(bubble);

            // Aktions-Button wenn AI das vorschlägt
            if (suggestedAction == "erstelle_dokument")
            {
                var btn = new Button
                {
                    Content = "📄 Vorlage erstellen",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Color.FromRgb(31, 111, 235)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 6, 0, 0),
                    FontSize = 12,
                    Cursor = Cursors.Hand,
                };
                btn.Click += async (s, e) => await OnCreateDocument(text);
                container.Children.Add(btn);
            }
            else if (suggestedAction == "berechne_unterhalt")
            {
                container.Children.Add(BuildUnterhaltForm());
            }

            ChatPanel.Children.Add(container);
            ScrollToBottom();
        });
    }

    private StackPanel BuildUnterhaltForm()
    {
        var form = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        var header = new TextBlock
        {
            Text = "Unterhaltsberechnung:",
            Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4),
        };
        form.Children.Add(header);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };

        var einkommenBox = new TextBox
        {
            Width = 120, Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(Color.FromRgb(13, 17, 23)),
            Foreground = new SolidColorBrush(Color.FromRgb(201, 209, 217)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
            BorderThickness = new Thickness(1),
            FontSize = 12,
        };
        einkommenBox.SetValue(FrameworkElement.TagProperty, "einkommen");

        var alterBox = new TextBox
        {
            Width = 80, Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(Color.FromRgb(13, 17, 23)),
            Foreground = new SolidColorBrush(Color.FromRgb(201, 209, 217)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12,
        };

        var calcBtn = new Button
        {
            Content = "Berechnen",
            Background = new SolidColorBrush(Color.FromRgb(31, 111, 235)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12,
            Cursor = Cursors.Hand,
        };

        var resultText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(63, 185, 80)),
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
        };

        calcBtn.Click += (s, e) =>
        {
            if (double.TryParse(einkommenBox.Text, out var einkommen) &&
                int.TryParse(alterBox.Text, out var alter))
            {
                // Düsseldorfer Tabelle (vereinfacht)
                var satz = alter < 6 ? 0.17 : alter < 12 ? 0.19 : alter < 18 ? 0.21 : 0.25;
                var betrag = einkommen * satz;
                resultText.Text = $"Geschätzter Unterhalt: {betrag:C2}/Monat";
            }
            else
            {
                resultText.Text = "Bitte gültige Zahlen eingeben.";
            }
        };

        row.Children.Add(new TextBlock { Text = "Einkommen (€):", Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        row.Children.Add(einkommenBox);
        row.Children.Add(new TextBlock { Text = "Alter:", Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) });
        row.Children.Add(alterBox);
        row.Children.Add(calcBtn);

        form.Children.Add(row);
        form.Children.Add(resultText);
        return form;
    }

    private async Task OnCreateDocument(string context)
    {
        AddReasoning("📄", "Vorlage wird erstellt...");
        try
        {
            var payload = JsonSerializer.Serialize(new { text = $"Erstelle eine Vorlage basierend auf folgendem Kontext: {context}" });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{BackendUrl}/ask", content);
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var output = root.TryGetProperty("output", out var o) ? o.GetString() ?? json : json;

            _history.Add(new ChatMessage("assistant", output));
            AppendAiMessage($"📄 Vorlage:\n\n{output}", "frage");
            ReasoningPanel.Children.Clear();
            AddReasoning("✅", "Vorlage erstellt");
        }
        catch (Exception ex)
        {
            AppendAiMessage($"Fehler beim Erstellen der Vorlage: {ex.Message}", "frage");
        }
    }

    private void ScrollToBottom()
    {
        ChatScrollViewer.ScrollToBottom();
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
