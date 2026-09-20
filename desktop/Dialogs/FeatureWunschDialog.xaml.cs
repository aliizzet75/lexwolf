using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LexWolf.Dialogs;

public partial class FeatureWunschDialog : Window
{
    private readonly HttpClient _http;
    private readonly string _backendUrl;
    private readonly List<(string Role, string Content)> _messages = new();
    private JsonNode? _pendingSummary;
    private bool _busy;

    public FeatureWunschDialog(HttpClient http, string backendUrl)
    {
        InitializeComponent();
        _http = http;
        _backendUrl = backendUrl;
        AddBubble("assistant", "Was wünschst du dir für LexWolf? Beschreib es kurz, ich frage bei Bedarf nach.");
        InputBox.Focus();
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            e.Handled = true;
            _ = SendAsync();
        }
    }

    private void OnSend(object sender, RoutedEventArgs e) => _ = SendAsync();

    private async Task SendAsync()
    {
        if (_busy) return;
        var text = InputBox.Text.Trim();
        if (text.Length == 0) return;

        InputBox.Text = string.Empty;
        AddBubble("user", text);
        _messages.Add(("user", text));
        SetBusy(true);

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                messages = _messages.ConvertAll(m => new { role = m.Role, content = m.Content })
            });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{_backendUrl}/feature-feedback/chat", content).ConfigureAwait(true);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var status = root.GetProperty("status").GetString() ?? "clarifying";
            var reply = root.TryGetProperty("reply", out var r) ? r.GetString() ?? "" : "";

            AddBubble("assistant", reply);
            _messages.Add(("assistant", reply));

            if (status == "ready" && root.TryGetProperty("summary", out var summaryEl) && summaryEl.ValueKind == JsonValueKind.Object)
            {
                _pendingSummary = JsonNode.Parse(summaryEl.GetRawText());
                ShowSummary(summaryEl);
            }
            else
            {
                HideSummary();
            }
        }
        catch (Exception ex)
        {
            AddBubble("assistant", $"⚠️ Fehler bei der Verbindung: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowSummary(JsonElement summary)
    {
        var titel = summary.TryGetProperty("titel", out var t) ? t.GetString() : "";
        var beschreibung = summary.TryGetProperty("beschreibung", out var b) ? b.GetString() : "";
        SummaryText.Text = string.IsNullOrWhiteSpace(titel) ? beschreibung : $"{titel}\n\n{beschreibung}";
        SummaryPanel.Visibility = Visibility.Visible;
        ConfirmSummaryBtn.IsEnabled = true;
        RejectSummaryBtn.IsEnabled = true;
    }

    private void HideSummary()
    {
        SummaryPanel.Visibility = Visibility.Collapsed;
        _pendingSummary = null;
    }

    private void OnRejectSummary(object sender, RoutedEventArgs e)
    {
        HideSummary();
        AddBubble("assistant", "Alles klar, was soll stattdessen anders sein?");
        InputBox.Focus();
    }

    private async void OnConfirmSummary(object sender, RoutedEventArgs e)
    {
        if (_pendingSummary is null || _busy) return;

        // Doppel-Submit-Schutz: sofort deaktivieren, bevor der Request raus geht.
        ConfirmSummaryBtn.IsEnabled = false;
        RejectSummaryBtn.IsEnabled = false;
        SetBusy(true);

        try
        {
            var payload = new JsonObject { ["summary"] = _pendingSummary.DeepClone() };
            using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{_backendUrl}/feature-feedback/confirm", content).ConfigureAwait(true);
            response.EnsureSuccessStatusCode();

            HideSummary();
            AddBubble("assistant", "✅ Danke! Wird jetzt umgesetzt — du bekommst das Feature automatisch beim nächsten Neustart von LexWolf.");
            InputBox.IsEnabled = false;
            SendBtn.IsEnabled = false;
        }
        catch (Exception ex)
        {
            AddBubble("assistant", $"⚠️ Konnte nicht übermittelt werden: {ex.Message}");
            ConfirmSummaryBtn.IsEnabled = true;
            RejectSummaryBtn.IsEnabled = true;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        InputBox.IsEnabled = !busy;
        SendBtn.IsEnabled = !busy;
    }

    private void AddBubble(string role, string text)
    {
        var isUser = role == "user";
        var bubble = new Border
        {
            Background = isUser ? new SolidColorBrush(Color.FromRgb(0x1f, 0x6f, 0xeb))
                                 : new SolidColorBrush(Color.FromRgb(0x16, 0x1b, 0x22)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(isUser ? 60 : 0, 5, isUser ? 0 : 60, 5),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
            },
        };
        ChatPanel.Children.Add(bubble);
        ChatScroll.ScrollToEnd();
    }
}
