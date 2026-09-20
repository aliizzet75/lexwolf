using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LexWolf.Database;

namespace LexWolf.Dialogs;

public partial class NotizenDialog : Window
{
    private readonly LocalDb _db;
    private readonly string _mandantId;
    private readonly string? _mandantName;
    private readonly HttpClient _http;
    private readonly string _backendUrl;
    private readonly List<NotizEintrag> _eintraege = new();
    private NotizEintrag? _ausgewaehlterEintrag;
    private string _unpersistedText = string.Empty;
    private bool _isLoadingSelection = false;

    public NotizenDialog(LocalDb db, string mandantId, HttpClient http, string backendUrl, string? mandantName = null)
    {
        InitializeComponent();
        _db = db;
        _mandantId = mandantId;
        _http = http;
        _backendUrl = backendUrl;
        _mandantName = mandantName;
        NavSubtitle.Text = mandantName is not null
            ? $"Mandant: {mandantName}"
            : $"Mandant-ID: {mandantId}";
        LadeNotizen();
        if (NavList.Items.Count > 0)
            NavList.SelectedIndex = 0;
        _ = LadeZusammenfassungAsync();
    }

    private async Task LadeZusammenfassungAsync()
    {
        if (_eintraege.Count == 0)
        {
            KISummaryText.Text = "Noch keine Notizen";
            KISummaryProgress.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var zusammenfassung = await GenerateZusammenfassungAsync();
            KISummaryText.Text = zusammenfassung;
        }
        catch (Exception ex)
        {
            KISummaryText.Text = "Zusammenfassung konnte nicht erstellt werden.";
            System.Diagnostics.Debug.WriteLine($"[NotizenDialog] Zusammenfassung fehlgeschlagen: {ex.Message}");
        }
        finally
        {
            KISummaryProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async Task<string> GenerateZusammenfassungAsync()
    {
        var notizText = string.Join("\n---\n", _eintraege
            .OrderByDescending(n => n.Zeitstempel)
            .Select(n => n.Text));

        var payload = JsonSerializer.Serialize(new
        {
            messages = new[]
            {
                new { role = "user", content = "Fasse die folgenden Mandanten-Notizen in 2-4 kurzen Sätzen zusammen:\n\n" + notizText }
            }
        });

        using var httpContent = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{_backendUrl}/chat", httpContent).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
    }

    private void LadeNotizen()
    {
        _eintraege.Clear();
        foreach (var (id, _, titelKurz, text, erstellt, geaendert) in _db.GetNotizen(_mandantId))
        {
            var zeit = geaendert > erstellt ? geaendert : erstellt;
            _eintraege.Add(new NotizEintrag(id, titelKurz, text, zeit));
        }

        NavList.Items.Clear();
        foreach (var eintrag in _eintraege.OrderByDescending(e => e.Zeitstempel))
            NavList.Items.Add(eintrag);
    }

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ausgewaehlterEintrag is not null && _unpersistedText != NoteEditorBox.Text)
            SpeichereAusgewaehlteNotiz();

        if (NavList.SelectedItem is not NotizEintrag eintrag)
        {
            NoteEditorBox.IsEnabled = false;
            NoteEditorBox.Text = string.Empty;
            _ausgewaehlterEintrag = null;
            DeleteBtn.Visibility = Visibility.Collapsed;
            return;
        }

        _isLoadingSelection = true;
        _ausgewaehlterEintrag = eintrag;
        NoteEditorBox.Text = eintrag.Text;
        _unpersistedText = eintrag.Text;
        NoteEditorBox.IsEnabled = true;
        DeleteBtn.Visibility = Visibility.Visible;
        _isLoadingSelection = false;
    }

    private void OnLoeschen(object sender, RoutedEventArgs e)
    {
        if (_ausgewaehlterEintrag is null) return;

        var titel = string.IsNullOrWhiteSpace(_ausgewaehlterEintrag.TitelKurz)
            ? "diese Notiz"
            : $"\"{_ausgewaehlterEintrag.TitelKurz}\"";

        var result = MessageBox.Show(
            $"Soll {titel} wirklich unwiderruflich gelöscht werden?",
            "Notiz löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes) return;

        _db.DeleteNotiz(_ausgewaehlterEintrag.Id);
        _eintraege.Remove(_ausgewaehlterEintrag);

        var sortiert = _eintraege.OrderByDescending(n => n.Zeitstempel).ToList();
        NavList.Items.Clear();
        foreach (var item in sortiert)
            NavList.Items.Add(item);

        if (sortiert.Count > 0)
        {
            NavList.SelectedIndex = 0;
            _ = LadeZusammenfassungAsync();
        }
        else
        {
            NavList.SelectedItem = null;
            _ausgewaehlterEintrag = null;
            NoteEditorBox.Text = string.Empty;
            NoteEditorBox.IsEnabled = false;
            DeleteBtn.Visibility = Visibility.Collapsed;
            KISummaryText.Text = "Noch keine Notizen";
            KISummaryProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void OnNoteEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingSelection || _ausgewaehlterEintrag is null) return;
        _ausgewaehlterEintrag.TitelKurz = ErstelleTitelKurz(NoteEditorBox.Text);
    }

    private void OnNoteEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if (_ausgewaehlterEintrag is null) return;
        if (_unpersistedText == NoteEditorBox.Text) return;
        SpeichereAusgewaehlteNotiz();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_ausgewaehlterEintrag is null) return;
        SpeichereAusgewaehlteNotiz();
        ZeigeStatus("Gespeichert");
    }

    private void OnNeueNotiz(object sender, RoutedEventArgs e)
    {
        var id = Guid.NewGuid().ToString("N");
        var jetzt = DateTime.UtcNow;
        var leerTitel = "(neue Notiz)";
        _db.InsertNotiz(id, _mandantId, leerTitel, string.Empty);

        var eintrag = new NotizEintrag(id, leerTitel, string.Empty, jetzt);
        _eintraege.Add(eintrag);

        NavList.Items.Clear();
        foreach (var item in _eintraege.OrderByDescending(n => n.Zeitstempel))
            NavList.Items.Add(item);

        NavList.SelectedItem = eintrag;
        NoteEditorBox.Focus();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        if (_ausgewaehlterEintrag is not null && _unpersistedText != NoteEditorBox.Text)
            SpeichereAusgewaehlteNotiz();
        DialogResult = false;
    }

    private void SpeichereAusgewaehlteNotiz()
    {
        if (_ausgewaehlterEintrag is null) return;

        var text = NoteEditorBox.Text;
        var titelKurz = ErstelleTitelKurz(text);
        _db.UpdateNotiz(_ausgewaehlterEintrag.Id, titelKurz, text);
        _ausgewaehlterEintrag.Text = text;
        _ausgewaehlterEintrag.TitelKurz = titelKurz;
        _ausgewaehlterEintrag.Zeitstempel = DateTime.UtcNow;
        _unpersistedText = text;
        AktualisiereNavSortierung();
    }

    private void AktualisiereNavSortierung()
    {
        var sortiert = _eintraege.OrderByDescending(e => e.Zeitstempel).ToList();
        var selected = _ausgewaehlterEintrag;

        NavList.Items.Clear();
        foreach (var eintrag in sortiert)
            NavList.Items.Add(eintrag);

        if (selected is not null)
            NavList.SelectedItem = selected;
    }

    private static string ErstelleTitelKurz(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "(leer)";

        var ersteZeile = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;
        var gekuerzt = ersteZeile.Length > 60 ? ersteZeile[..57] + "..." : ersteZeile;
        return gekuerzt.Trim();
    }

    private void ZeigeStatus(string nachricht)
    {
        StatusLabel.Text = nachricht;
        StatusLabel.Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(async () =>
        {
            await System.Threading.Tasks.Task.Delay(2000);
            StatusLabel.Visibility = Visibility.Collapsed;
        }, DispatcherPriority.Background);
    }

    private sealed class NotizEintrag : INotifyPropertyChanged
    {
        public string Id { get; }
        public string DatumUhrzeit => Zeitstempel.ToLocalTime().ToString("g");

        private string _titelKurz;
        public string TitelKurz
        {
            get => _titelKurz;
            set
            {
                if (_titelKurz == value) return;
                _titelKurz = value;
                OnPropertyChanged();
            }
        }

        public string Text { get; set; }

        private DateTime _zeitstempel;
        public DateTime Zeitstempel
        {
            get => _zeitstempel;
            set
            {
                if (_zeitstempel == value) return;
                _zeitstempel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DatumUhrzeit));
            }
        }

        public NotizEintrag(string id, string titelKurz, string text, DateTime zeitstempel)
        {
            Id = id;
            _titelKurz = titelKurz;
            Text = text;
            _zeitstempel = zeitstempel;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
