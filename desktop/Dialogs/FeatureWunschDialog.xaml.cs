using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LexWolf.Database;

namespace LexWolf.Dialogs;

public partial class FeatureWunschDialog : Window
{
    private readonly LocalDb _db;
    private readonly HttpClient _http;
    private readonly string _backendUrl;
    private readonly List<(string Role, string Content)> _messages = new();
    private JsonNode? _pendingSummary;
    private bool _busy;

    public FeatureWunschDialog(LocalDb db, HttpClient http, string backendUrl)
    {
        InitializeComponent();
        _db = db;
        _http = http;
        _backendUrl = backendUrl;

        ClientVersionBox.Text = GetCurrentClientVersion();

        var verlauf = _db.GetFeatureWunschHistory();
        if (verlauf.Count > 0)
        {
            foreach (var (role, content, _) in verlauf)
            {
                AddBubble(role, content);
                _messages.Add((role, content));
            }
        }
        else
        {
            AddBubble("assistant", "Was möchtest du uns mitteilen? Beschreib es kurz, ich frage bei Bedarf nach.");
        }
        InputBox.Focus();
    }

    private string GetCurrentClientVersion()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var info = assembly.GetName().Version;
            var fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion;
            return info?.ToString() ?? fileVersion ?? "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }

    private string SelectedFeedbackTyp
    {
        get
        {
            var selected = FeedbackTypBox.SelectedItem as ComboBoxItem;
            return selected?.Tag?.ToString() ?? "feature";
        }
    }

    private void OnFeedbackTypChanged(object sender, SelectionChangedEventArgs e)
    {
        var isBug = SelectedFeedbackTyp == "bug";
        BugFieldsPanel.Visibility = isBug ? Visibility.Visible : Visibility.Collapsed;
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

    private bool ValidateBugFields()
    {
        if (SelectedFeedbackTyp != "bug")
            return true;

        var repro = ReproStepsBox.Text.Trim();
        var version = ClientVersionBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(repro))
        {
            MessageBox.Show(
                this,
                "Bitte beschreibe die Reproduktionsschritte, damit wir den Fehler nachvollziehen können.",
                "Fehler melden",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ReproStepsBox.Focus();
            return false;
        }
        if (string.IsNullOrWhiteSpace(version))
        {
            MessageBox.Show(
                this,
                "Bitte gib die verwendete Client-Version an.",
                "Fehler melden",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ClientVersionBox.Focus();
            return false;
        }
        return true;
    }

    private async Task SendAsync()
    {
        if (_busy) return;
        if (!ValidateBugFields()) return;

        var text = InputBox.Text.Trim();
        if (text.Length == 0) return;

        var isBug = SelectedFeedbackTyp == "bug";
        var prefix = isBug ? "[BUG] " : "";
        var taggedText = prefix + text;

        InputBox.Text = string.Empty;
        AddBubble("user", taggedText);
        _messages.Add(("user", taggedText));
        _db.AddFeatureWunschMessage("user", taggedText);
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
            _db.AddFeatureWunschMessage("assistant", reply);

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
        if (!ValidateBugFields()) return;

        // Doppel-Submit-Schutz: sofort deaktivieren, bevor der Request raus geht.
        ConfirmSummaryBtn.IsEnabled = false;
        RejectSummaryBtn.IsEnabled = false;
        SetBusy(true);

        try
        {
            var payload = new JsonObject { ["summary"] = _pendingSummary.DeepClone() };
            payload["typ"] = SelectedFeedbackTyp;

            if (SelectedFeedbackTyp == "bug")
            {
                var bug = new JsonObject
                {
                    ["repro_steps"] = ReproStepsBox.Text.Trim(),
                    ["client_version"] = ClientVersionBox.Text.Trim(),
                    ["affected_ui_location"] = (AffectedUiBox.Text ?? "").Trim()
                };
                payload["bug"] = bug;
            }

            using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{_backendUrl}/feature-feedback/confirm", content).ConfigureAwait(true);
            response.EnsureSuccessStatusCode();

            HideSummary();
            var erfolgsText = SelectedFeedbackTyp == "bug"
                ? "✅ Danke! Die Fehlermeldung wurde übermittelt — das Team kümmert sich darum."
                : "✅ Danke! Wird jetzt umgesetzt — du bekommst das Feature automatisch beim nächsten Neustart von LexWolf.";
            AddBubble("assistant", erfolgsText);
            _db.AddFeatureWunschMessage("assistant", erfolgsText);
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
        ClearHistoryBtnInline.IsEnabled = !busy;
        FeedbackTypBox.IsEnabled = !busy;
        ClientVersionBox.IsEnabled = !busy;
        AffectedUiBox.IsEnabled = !busy;
        ReproStepsBox.IsEnabled = !busy;
    }

    private void OnClearFeatureWunschHistory(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Möchten Sie alle bisherigen Einträge wirklich löschen?",
            "Feedback-Verlauf leeren",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.OK)
            return;

        try
        {
            _db.ClearFeatureWunschHistory();
            _messages.Clear();
            ChatPanel.Children.Clear();
            AddBubble("assistant", "Der Verlauf wurde geleert. Was möchtest du als Nächstes mitteilen?");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Der Verlauf konnte nicht geleert werden: {ex.Message}",
                "Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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
