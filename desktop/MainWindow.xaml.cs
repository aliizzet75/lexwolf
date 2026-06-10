using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using LexWolf.Database;
using LexWolf.Services;
using LexWolf.Dialogs;

namespace LexWolf;

record ChatMessage(string Role, string Content);

public partial class MainWindow : Window
{
    private const string BackendUrl = "http://212.227.180.66:8000";

    private AppSettings _settings = AppSettings.Load();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private readonly List<ChatMessage> _history = new();
    private readonly LocalDb _db = new();
    private DokumentScanner? _scanner;
    private int? _activeMandantId = null;
    private string? _activeMandantName = null;
    private readonly List<(int Id, string Name)> _mandanten = new();

    public MainWindow()
    {
        InitializeComponent();
        _ = CheckConnectionAsync(showConnecting: true);
        _ = PeriodicHealthCheckAsync();
        _ = LoadMandantenAsync();
        _ = Task.Run(StartDocumentScannerAsync);
        AppendSystemMessage("Willkommen bei LexWolf. Wie kann ich Ihnen helfen?");
    }

    private Task StartDocumentScannerAsync()
    {
        var path = _settings.DokumentePfad;
        try
        {
            _scanner?.Dispose();
            _scanner = null;
            Directory.CreateDirectory(path);
            _scanner = new DokumentScanner(path, _db);
            _scanner.ScanAll();
            _scanner.StartWatching();
            Dispatcher.Invoke(() => AddReasoning("📂", $"Dokumente indexiert: {path}"));
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => AddReasoning("⚠️", $"Scanner-Fehler: {ex.Message}"));
        }
        return Task.CompletedTask;
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsDialog(_settings) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            var oldPath = _settings.DokumentePfad;
            _settings   = dlg.Settings;
            if (_settings.DokumentePfad != oldPath)
            {
                AddReasoning("🔄", $"Neues Verzeichnis: {_settings.DokumentePfad}");
                _ = Task.Run(StartDocumentScannerAsync);
            }
        }
    }

    // ── Verbindung ────────────────────────────────────────────────────────────

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
        await LoadMandantenAsync();
    }

    // ── Mandanten ─────────────────────────────────────────────────────────────

    private async Task LoadMandantenAsync()
    {
        try
        {
            var json = await _http.GetStringAsync($"{BackendUrl}/mandant/search");
            using var doc = JsonDocument.Parse(json);
            _mandanten.Clear();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var id   = item.GetProperty("id").GetInt32();
                var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "?" : "?";
                _mandanten.Add((id, name));
                _db.UpsertMandant(id.ToString(), name, "");
            }
            Dispatcher.Invoke(() =>
            {
                var prev = MandantBox.SelectedIndex;
                MandantBox.Items.Clear();
                MandantBox.Items.Add("— kein Mandant —");
                foreach (var (_, name) in _mandanten)
                    MandantBox.Items.Add(name);
                MandantBox.SelectedIndex = prev >= 0 ? Math.Min(prev, MandantBox.Items.Count - 1) : 0;
            });
        }
        catch { /* Backend noch nicht erreichbar */ }
    }

    private void OnMandantChanged(object sender, SelectionChangedEventArgs e)
    {
        var idx = MandantBox.SelectedIndex - 1;
        _history.Clear();
        ChatPanel.Children.Clear();
        UnterhaltBtn.Visibility = Visibility.Collapsed;

        if (idx < 0 || idx >= _mandanten.Count)
        {
            _activeMandantId   = null;
            _activeMandantName = null;
            AppendSystemMessage("Kein Mandant ausgewählt — allgemeines Gespräch.");
        }
        else
        {
            var (id, name) = _mandanten[idx];
            _activeMandantId   = id;
            _activeMandantName = name;
            AppendSystemMessage($"Mandant: {name} — Chat-Kontext aktiv.");
        }
    }

    // ── Chat ──────────────────────────────────────────────────────────────────

    private void OnClearChat(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        ChatPanel.Children.Clear();
        UnterhaltBtn.Visibility = Visibility.Collapsed;
        AppendSystemMessage(_activeMandantName is not null
            ? $"Chat gelöscht — Mandant: {_activeMandantName}"
            : "Chat gelöscht. Wie kann ich Ihnen helfen?");
    }

    private void OnUnterhaltBtnClick(object sender, RoutedEventArgs e)
    {
        var container = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin              = new Thickness(8, 4, 60, 4),
        };
        container.Children.Add(new TextBlock
        {
            Text        = "Unterhaltsrechner:",
            Foreground  = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
            FontSize    = 12,
            Margin      = new Thickness(0, 0, 0, 6),
        });
        container.Children.Add(BuildUnterhaltForm());
        ChatPanel.Children.Add(container);
        ScrollToBottom();
        UnterhaltBtn.Visibility = Visibility.Collapsed;
    }

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

        _history.Add(new ChatMessage("user", text));
        AppendUserMessage(text);
        _db.AddChatMessage(_activeMandantId?.ToString() ?? "global", "user", text);

        ReasoningPanel.Children.Clear();
        AddReasoning("⏳", "Anfrage wird verarbeitet...");

        try
        {
            var (content, suggestedAction) = await PostChatAsync();

            _history.Add(new ChatMessage("assistant", content));
            _db.AddChatMessage(_activeMandantId?.ToString() ?? "global", "assistant", content);
            AppendAiMessage(content, suggestedAction);

            Dispatcher.Invoke(() =>
                UnterhaltBtn.Visibility = suggestedAction == "berechne_unterhalt"
                    ? Visibility.Visible
                    : Visibility.Collapsed);

            ReasoningPanel.Children.Clear();
            AddReasoning("✅", "Fertig");
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

    private async Task<(string content, string suggestedAction)> PostChatAsync()
    {
        AddReasoning("🔍", "Suche in Rechtsdatenbank...");

        string? mandantContext = _activeMandantName is not null
            ? $"Mandant: {_activeMandantName} (ID: {_activeMandantId})"
            : null;

        var payload = JsonSerializer.Serialize(new
        {
            messages       = _history.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            mandant_context = mandantContext,
        });
        var httpContent = new StringContent(payload, Encoding.UTF8, "application/json");

        AddReasoning("🤖", "Generiere juristische Antwort...");
        var response = await _http.PostAsync($"{BackendUrl}/chat", httpContent);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body);
        var root            = doc.RootElement;
        var content         = root.TryGetProperty("content",          out var c)  ? c.GetString()  ?? body  : body;
        var suggestedAction = root.TryGetProperty("suggested_action", out var sa) ? sa.GetString() ?? "frage" : "frage";

        if (root.TryGetProperty("intent", out var ip))
            AddReasoning("🎯", $"Intent: {ip.GetString()}");

        return (content, suggestedAction);
    }

    // ── UI Rendering ──────────────────────────────────────────────────────────

    private void AppendSystemMessage(string text)
    {
        Dispatcher.Invoke(() =>
        {
            var border = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Background          = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                CornerRadius        = new CornerRadius(8),
                Padding             = new Thickness(12, 6, 12, 6),
                Margin              = new Thickness(0, 4, 0, 8),
            };
            border.Child = new TextBlock
            {
                Text        = text,
                Foreground  = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                FontSize    = 11,
                TextWrapping = TextWrapping.Wrap,
            };
            ChatPanel.Children.Add(border);
            ScrollToBottom();
        });
    }

    private void AppendUserMessage(string text)
    {
        Dispatcher.Invoke(() =>
        {
            var bubble = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Background          = new SolidColorBrush(Color.FromRgb(31, 111, 235)),
                CornerRadius        = new CornerRadius(16, 4, 16, 16),
                Padding             = new Thickness(14, 10, 14, 10),
                Margin              = new Thickness(60, 4, 8, 4),
                MaxWidth            = 500,
            };
            bubble.Child = new TextBlock
            {
                Text        = text,
                Foreground  = Brushes.White,
                FontSize    = 13,
                TextWrapping = TextWrapping.Wrap,
            };
            ChatPanel.Children.Add(bubble);
            ScrollToBottom();
        });
    }

    private void AppendAiMessage(string text, string suggestedAction)
    {
        Dispatcher.Invoke(() =>
        {
            var container = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin              = new Thickness(8, 4, 60, 4),
            };

            var bubble = new Border
            {
                Background   = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                CornerRadius = new CornerRadius(4, 16, 16, 16),
                Padding      = new Thickness(14, 10, 14, 10),
                MaxWidth     = 500,
            };
            bubble.Child = new TextBlock
            {
                Text        = text,
                Foreground  = new SolidColorBrush(Color.FromRgb(201, 209, 217)),
                FontSize    = 13,
                TextWrapping = TextWrapping.Wrap,
            };
            container.Children.Add(bubble);

            if (suggestedAction == "erstelle_dokument")
            {
                var btn = new Button
                {
                    Content             = "📄 Vorlage erstellen",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background          = new SolidColorBrush(Color.FromRgb(31, 111, 235)),
                    Foreground          = Brushes.White,
                    BorderThickness     = new Thickness(0),
                    Padding             = new Thickness(12, 6, 12, 6),
                    Margin              = new Thickness(0, 6, 0, 0),
                    FontSize            = 12,
                    Cursor              = Cursors.Hand,
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

        form.Children.Add(new TextBlock
        {
            Text        = "Unterhaltsberechnung:",
            Foreground  = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
            FontSize    = 11,
            Margin      = new Thickness(0, 0, 0, 4),
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };

        var einkommenBox = new TextBox
        {
            Width           = 120,
            Padding         = new Thickness(8, 4, 8, 4),
            Background      = new SolidColorBrush(Color.FromRgb(13, 17, 23)),
            Foreground      = new SolidColorBrush(Color.FromRgb(201, 209, 217)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
            BorderThickness = new Thickness(1),
            FontSize        = 12,
        };

        var alterBox = new TextBox
        {
            Width           = 80,
            Padding         = new Thickness(8, 4, 8, 4),
            Background      = new SolidColorBrush(Color.FromRgb(13, 17, 23)),
            Foreground      = new SolidColorBrush(Color.FromRgb(201, 209, 217)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
            BorderThickness = new Thickness(1),
            Margin          = new Thickness(6, 0, 0, 0),
            FontSize        = 12,
        };

        var calcBtn = new Button
        {
            Content         = "Berechnen",
            Background      = new SolidColorBrush(Color.FromRgb(31, 111, 235)),
            Foreground      = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding         = new Thickness(10, 4, 10, 4),
            Margin          = new Thickness(6, 0, 0, 0),
            FontSize        = 12,
            Cursor          = Cursors.Hand,
        };

        var resultText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(63, 185, 80)),
            FontSize   = 12,
            Margin     = new Thickness(0, 4, 0, 0),
        };

        calcBtn.Click += (s, e) =>
        {
            if (double.TryParse(einkommenBox.Text, out var einkommen) &&
                int.TryParse(alterBox.Text, out var alter))
            {
                var satz   = alter < 6 ? 0.17 : alter < 12 ? 0.19 : alter < 18 ? 0.21 : 0.25;
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
            var root   = doc.RootElement;
            var output = root.TryGetProperty("output", out var o) ? o.GetString() ?? json : json;

            _history.Add(new ChatMessage("assistant", output));
            _db.AddChatMessage(_activeMandantId?.ToString() ?? "global", "assistant", output);
            AppendAiMessage($"📄 Vorlage:\n\n{output}", "frage");
            ReasoningPanel.Children.Clear();
            AddReasoning("✅", "Vorlage erstellt");
        }
        catch (Exception ex)
        {
            AppendAiMessage($"Fehler beim Erstellen der Vorlage: {ex.Message}", "frage");
        }
    }

    private void ScrollToBottom() => ChatScrollViewer.ScrollToBottom();

    private void AddReasoning(string emoji, string text)
    {
        Dispatcher.Invoke(() =>
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            row.Children.Add(new TextBlock
            {
                Text              = emoji,
                FontSize          = 16,
                Margin            = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
            });
            row.Children.Add(new TextBlock
            {
                Text              = text,
                Foreground        = new SolidColorBrush(Color.FromRgb(201, 209, 217)),
                FontSize          = 12,
                TextWrapping      = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top,
            });
            ReasoningPanel.Children.Add(row);
        });
    }
}
