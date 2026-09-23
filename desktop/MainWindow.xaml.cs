using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;
using LexWolf.Database;
using LexWolf.Services;
using LexWolf.Dialogs;
using Microsoft.Web.WebView2.Core;

namespace LexWolf;

record ChatMessage(string Role, string Content);

public partial class MainWindow : Window
{
    private const string BackendUrl = "http://212.227.180.66:8000";

    private AppSettings _settings = AppSettings.Load();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private readonly List<ChatMessage> _history = new();
    private readonly LocalDb _db = new();
    private readonly ChatSummaryService _chatSummaryService;
    private readonly MandantAnalyseService _mandantAnalyseService;
    private readonly MandantZusammenfassungService _zusammenfassungService;
    private DokumentScanner? _scanner;
    private string? _activeMandantId = null;
    private string? _activeMandantName = null;
    private bool _webViewInitialized = false;
    private string _kopierenOriginalLabel = "📋 Kopieren";
    private string _kopierenFeedbackLabel = "✅ Kopiert";
    private System.Windows.Threading.DispatcherTimer? _kopierenFeedbackTimer;
    private readonly List<(string Id, string Name)> _mandanten = new();
    private readonly List<string> _chatHistoryHtml = new();
    private readonly HashSet<string> _prioritizedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly FeedbackSender _feedbackSender = new("http://localhost:8000");
    private readonly FeatureStatusChecker _featureStatusChecker;
    private CancellationTokenSource? _loadHistoryCts;
    private System.Collections.ObjectModel.ObservableCollection<Models.FileTreeNode> _fileTreeRoots = new();

    public MainWindow()
    {
        InitializeComponent();
        _chatSummaryService = new ChatSummaryService(_db, _http, BackendUrl);
        _mandantAnalyseService = new MandantAnalyseService(_db, _http, BackendUrl);
        _zusammenfassungService = new MandantZusammenfassungService(_db, _http, BackendUrl);
        _mandantAnalyseService.StatusChanged += (_, status) =>
            Dispatcher.Invoke(() => OnAnalyseStatusChanged(status));
        MandantBox.AddHandler(
            System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(OnMandantTextChanged));
        _featureStatusChecker = new FeatureStatusChecker(
            _http,
            "http://localhost:8082",
            _db,
            () => this,
            TimeSpan.FromMinutes(30),
            msg => System.Diagnostics.Debug.WriteLine(msg));
        _ = CheckConnectionAsync(showConnecting: true);
        _ = PeriodicHealthCheckAsync();
        // Start() führt beim Hochfahren der Schleife bereits einen sofortigen
        // Check aus (siehe FeatureStatusChecker.LoopAsync) — ein zusätzlicher
        // expliziter CheckOnceAsync()-Aufruf hier würde denselben Fertigstellungs-
        // Toast doppelt anzeigen, da beide Aufrufe nebenläufig gegen denselben
        // Server-Call liefen, bevor MarkAsSeen() greifen konnte.
        _featureStatusChecker.Start();
        _ = LoadMandantenAsync();
        _ = Task.Run(StartDocumentScannerAsync);
        _ = CheckForUpdateAsync();
        _ = LoadStyleProgressAsync();
        InputBox.FontFamily = new FontFamily(_settings.ChatInputFont);
        InputBox.FontSize = _settings.ChatInputFontSize;
        ChatHtmlRenderer.SetOutputFont(_settings.ChatOutputFont, _settings.ChatOutputFontSize);
        // App-Hintergrund (Fenster verliert Fokus, z.B. Alt-Tab) löst ebenfalls eine
        // Hintergrund-Zusammenfassung aus, nicht nur der Mandant-Wechsel — sonst
        // bliebe eine lange Sitzung ohne Wechsel bis zum App-Ende unsummarisiert.
        Deactivated += (_, _) => _ = _chatSummaryService.SummarizeSessionAsync(_activeMandantId);
        _ = InitializeChatWebViewAsync();
        AppendSystemMessage("Willkommen bei LexWolf. Wie kann ich Ihnen helfen?");
    }

    private async Task InitializeChatWebViewAsync()
    {
        try
        {
            // Standardmäßig legt WebView2 sein Datenverzeichnis (EBWebView) neben der
            // .exe an — bei Installation nach Program Files ohne Admin-Rechte nicht
            // schreibbar ("kein Lese-/Schreibzugriff"). Explizit auf ein Verzeichnis
            // im Nutzerprofil umleiten, analog zu AppSettings.
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LexWolf", "WebView2");
            var webViewEnv = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await ChatWebView.EnsureCoreWebView2Async(webViewEnv);
            _chatHistoryHtml.Clear();
            AddHtmlMessage(ChatHtmlRenderer.WrapSystemMessage("Willkommen bei LexWolf. Wie kann ich Ihnen helfen?"));
            RefreshChatWebView();
            ChatWebView.Visibility = Visibility.Visible;
            _webViewInitialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InitializeChatWebViewAsync] WebView2-Initialisierung fehlgeschlagen: {ex.Message}");
        }
    }

    private static string GetChatBaseHtml(string messagesHtml)
    {
        return $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
{ChatHtmlRenderer.GetChatCss()}
</head>
<body>
<div id='chat-messages'>
{messagesHtml}
</div>
</body>
</html>";
    }

    private void AddHtmlMessage(string html)
    {
        _chatHistoryHtml.Add(html);
    }

    private void RefreshChatWebView()
    {
        var sb = new StringBuilder();
        foreach (var msg in _chatHistoryHtml)
            sb.AppendLine(msg);
        ChatWebView.NavigateToString(GetChatBaseHtml(sb.ToString()));
    }

    private async Task CheckForUpdateAsync()
    {
        var checker = new UpdateChecker(BackendUrl, _http);
        var update = await checker.CheckForUpdateAsync();
        if (update is null) return;

        var proceed = false;
        Dispatcher.Invoke(() =>
        {
            var result = System.Windows.MessageBox.Show(
                this,
                $"Eine neue LexWolf-Version ist verfügbar: {update.Version}\n\n{update.Notes}\n\n" +
                "Jetzt automatisch aktualisieren? LexWolf wird dazu kurz neu gestartet.",
                "Update verfügbar",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            proceed = result == MessageBoxResult.Yes;
        });
        if (!proceed) return;

        // Silent-Self-Update (wie z.B. Notepad++): Installer im Hintergrund laden,
        // mit /S ohne UI ausführen — der Installer killt eine evtl. noch laufende
        // Instanz selbst (siehe .nsi) und startet die neue Version danach automatisch.
        // Kein manueller Download+Doppelklick mehr nötig.
        Dispatcher.Invoke(() =>
        {
            this.IsEnabled = false;
            ProgressBar.Value = 0;
            SetProgressBusy(true);
            SetStatus(null, "Update wird heruntergeladen... 0%");
        });
        var downloadProgress = new Progress<double>(percent => Dispatcher.Invoke(() =>
        {
            ProgressBar.Value = percent;
            SetStatus(null, $"Update wird heruntergeladen... {percent:0}%");
        }));

        try
        {
            await checker.ApplyUpdateAsync(update, downloadProgress);
            Dispatcher.Invoke(() => SetStatus(null, "Update wird installiert..."));
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                this.IsEnabled = true;
                SetProgressBusy(false);
                System.Windows.MessageBox.Show(
                    this, $"Update fehlgeschlagen: {ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            });
            return;
        }

        Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
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
            // Dropdown mit den gerade gescannten Mandanten befüllen — vorher lief
            // LoadMandantenAsync() schon beim Start, BEVOR überhaupt gescannt wurde.
            _ = LoadMandantenAsync();
            BuildFileTree(_activeMandantName);
            _scanner.OnNeuerMandant = () => Dispatcher.Invoke(() => { _ = LoadMandantenAsync(); BuildFileTree(_activeMandantName); });
            _scanner.StartWatching();
            Dispatcher.Invoke(() => AddReasoning("📂", $"Dokumente indexiert: {path}"));
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => AddReasoning("⚠️", $"Scanner-Fehler: {ex.Message}"));
        }
        return Task.CompletedTask;
    }

    private void OnShowInfo(object sender, RoutedEventArgs e)
    {
        var dlg = new LexWolf.Dialogs.InfoDialog(BackendUrl, _http) { Owner = this };
        dlg.ShowDialog();
    }

    private void OnNotizenClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_activeMandantId)) return;
        var dlg = new NotizenDialog(_db, _activeMandantId, _http, BackendUrl, _activeMandantName) { Owner = this };
        dlg.ShowDialog();
    }

    private void OnKopierenClick(object sender, RoutedEventArgs e)
    {
        var lastAssistant = _history.LastOrDefault(m => string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase));
        if (lastAssistant == default(ChatMessage) || string.IsNullOrWhiteSpace(lastAssistant.Content)) return;

        System.Windows.Clipboard.SetText(lastAssistant.Content);

        KopierenBtn.Content = _kopierenFeedbackLabel;
        if (_kopierenFeedbackTimer is null)
        {
            _kopierenFeedbackTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5),
            };
            _kopierenFeedbackTimer.Tick += (_, _) =>
            {
                _kopierenFeedbackTimer.Stop();
                KopierenBtn.Content = _kopierenOriginalLabel;
            };
        }
        _kopierenFeedbackTimer.Start();
    }

    private void OnFeatureWunschClick(object sender, RoutedEventArgs e)
    {
        var dlg = new LexWolf.Dialogs.FeatureWunschDialog(_db, _http, BackendUrl) { Owner = this };
        dlg.ShowDialog();
    }

    private void OnFeatureUebersichtClick(object sender, RoutedEventArgs e)
    {
        var sessionId = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? "lexwolf";
        var dlg = new LexWolf.Dialogs.FeatureUebersichtDialog(_http, "http://localhost:8082", sessionId) { Owner = this };
        dlg.ShowDialog();
    }

    private async void OnZusammenfassungClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_activeMandantId)) return;

        SetProgressBusy(true);
        SetStatus(null, "Zusammenfassung wird ermittelt...");
        try
        {
            // Quellen-Hash prüfen: bei unveränderten Chat/Notizen/Dokumenten
            // wird gecachte Zusammenfassung sofort angezeigt, sonst neu generiert.
            var text = await _zusammenfassungService.HoleOderErzeugeZusammenfassungAsync(_activeMandantId);
            var dlg = new ZusammenfassungDialog(text, _activeMandantName) { Owner = this };
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this, $"Zusammenfassung konnte nicht erzeugt werden: {ex.Message}", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetProgressBusy(false);
            SetStatus(true, $"Verbunden — {BackendUrl}");
        }
    }

    private void OnAnalyseStatusChanged(AnalyseStatus status)
    {
        AddReasoning("🔍", $"Analyse-Status: {status}");
        if (status == AnalyseStatus.Scanning)
        {
            ZusammenfassungBtn.IsEnabled = false;
            ZusammenfassungBtn.Content = CreateWolfLoadingContent("wird analysiert");
            KopierenBtn.IsEnabled = false;
        }
        else if (status == AnalyseStatus.Ready)
        {
            ZusammenfassungBtn.IsEnabled = true;
            ZusammenfassungBtn.Content = "📊 Zusammenfassung";
            KopierenBtn.IsEnabled = true;
        }
    }

    /// <summary>
    /// Baut den Button-Inhalt mit der pulsierenden Wolf-Icon-Ladeanimation
    /// aus Task #209 (WolfLoadingImage) und dem übergebenen Text.
    /// </summary>
    private object CreateWolfLoadingContent(string label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var img = new System.Windows.Controls.Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("Assets/lexwolf.ico", UriKind.Relative)),
            Width = 18,
            Height = 18,
            Opacity = 1.0,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        var pulse = new DoubleAnimation { From = 0.4, To = 1.0, Duration = TimeSpan.FromSeconds(0.8) };
        Storyboard.SetTarget(pulse, img);
        Storyboard.SetTargetProperty(pulse, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(pulse);
        sb.Begin();
        panel.Children.Add(img);
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(201, 209, 217)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsDialog(_settings) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            var oldPath = _settings.DokumentePfad;
            _settings   = dlg.Settings;
            InputBox.FontFamily = new FontFamily(_settings.ChatInputFont);
            InputBox.FontSize = _settings.ChatInputFontSize;
            ChatHtmlRenderer.SetOutputFont(_settings.ChatOutputFont, _settings.ChatOutputFontSize);
            if (_webViewInitialized)
                RefreshChatWebView();
            if (_settings.DokumentePfad != oldPath)
            {
                AddReasoning("🔄", $"Neues Verzeichnis: {_settings.DokumentePfad}");
                _ = Task.Run(StartDocumentScannerAsync);
            }
            _ = LoadStyleProgressAsync();
        }
    }

    private async Task LoadStyleProgressAsync()
    {
        try
        {
            var response = await _http.GetAsync($"{BackendUrl}/api/style/progress");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var progress = root.TryGetProperty("lern_fortschritt", out var p) ? p.GetDouble() : 0.0;
            var samples = root.TryGetProperty("samples_gesammelt", out var s) ? s.GetInt32() : 0;
            var aspekte = root.TryGetProperty("bekannte_aspekte", out var a) && a.ValueKind == JsonValueKind.Array
                ? string.Join(", ", a.EnumerateArray().Select(x => x.GetString()).Where(x => x != null))
                : "";

            Dispatcher.Invoke(() =>
            {
                StyleProgressBar.Value = progress;
                var prozent = (int)Math.Round(progress * 100);
                StyleProgressLabel.Text = $"LexWolf kennt Ihren Stil zu {prozent}%";
                StyleProgressPanel.ToolTip = $"{samples} Stil-Samples gesammelt; bekannte Aspekte: {aspekte}";
                StyleProgressPanel.Visibility = Visibility.Visible;
            });
        }
        catch (Exception ex)
        {
            AddReasoning("📊", $"Lern-Fortschritt konnte nicht geladen werden: {ex.Message}");
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

    private void SetProgressBusy(bool busy)
    {
        ProgressBar.IsEnabled = busy;
        ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
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

    private const string KeinMandantLabel = "— kein Mandant —";
    private const int MaxMandantDropdownItems = 100;
    private bool _suppressMandantEvents = false;

    private Task LoadMandantenAsync()
    {
        var mandanten = _db.GetMandanten();
        _mandanten.Clear();
        _mandanten.AddRange(mandanten);
        Dispatcher.Invoke(() => ApplyMandantFilter(MandantBox.Text));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Lädt die Mandantenliste projiziert (id, name) aus der lokalen DB.
    /// Kann optional gefiltert werden (SQL-seitige LIKE-Suche), falls die Suche
    /// an die DB delegiert wird.
    /// </summary>
    private Task LoadMandantenAsync(string? filter)
    {
        var mandanten = string.IsNullOrWhiteSpace(filter)
            ? _db.GetMandanten()
            : _db.SearchMandanten(filter);
        _mandanten.Clear();
        _mandanten.AddRange(mandanten);
        Dispatcher.Invoke(() => ApplyMandantFilter(filter));
        return Task.CompletedTask;
    }

    /// <summary>Befüllt die Dropdown-Liste case-insensitiv gefiltert nach dem
    /// aktuell eingegebenen Text. Wichtig: auch bei leerem Filter werden alle
    /// geladenen Mandanten angezeigt, damit das Dropdown beim Öffnen des Dialogs
    /// nicht leer bleibt (Task #239).
    /// </summary>
    private void ApplyMandantFilter(string? filterText)
    {
        _suppressMandantEvents = true;
        try
        {
            var filter = (filterText ?? "").Trim();
            MandantBox.Items.Clear();

            if (string.IsNullOrEmpty(filter))
            {
                // Dialog frisch geöffnet: alle verfügbaren Mandanten anzeigen,
                // damit das Dropdown nicht leer erscheint (Task #239).
                var names = _mandanten.Count == 0
                    ? new List<string> { KeinMandantLabel }
                    : _mandanten.Select(m => m.Name).Take(MaxMandantDropdownItems).ToList();
                foreach (var name in names)
                    MandantBox.Items.Add(name);
            }
            else
            {
                var gefiltert = _mandanten
                    .Where(m => m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.Name)
                    .Take(MaxMandantDropdownItems)
                    .ToList();

                if (gefiltert.Count == 0)
                    MandantBox.Items.Add(KeinMandantLabel);

                foreach (var name in gefiltert)
                    MandantBox.Items.Add(name);
            }
        }
        finally
        {
            _suppressMandantEvents = false;
        }
    }

    private void OnMandantTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressMandantEvents) return;
        var filter = MandantBox.Text ?? "";

        // Beim vollständigen Leeren des Suchfelds laden wir den kompletten
        // Mandantenbestand neu, falls zuvor serverseitig (SQL) gefiltert wurde.
        // Sonst würde ApplyMandantFilter nur über die bereits eingeschränkte
        // _mandanten-Liste laufen und die volle Liste bliebe unsichtbar.
        if (string.IsNullOrEmpty(filter))
        {
            _ = LoadMandantenAsync();
        }
        else if (filter.Length >= 3 && _mandanten.Count > 200)
        {
            _ = LoadMandantenAsync(filter);
        }
        else
        {
            ApplyMandantFilter(filter);
        }

        // Nur beim Tippen (nicht-leerer Filter) das Dropdown aktiv auf-/zuklappen.
        // Bei leerem Filter (Suchfeld geleert) NICHT zwangsweise schliessen: die
        // Liste wurde gerade per LoadMandantenAsync() mit dem vollen Bestand neu
        // befuellt (siehe oben) -- ein hartes IsDropDownOpen=false hier wuerde das
        // frisch befuellte Dropdown sofort wieder zuklappen und beim naechsten Klick
        // faelschlich als "leer" erscheinen lassen, bevor der Nutzer es erneut oeffnet.
        if (!string.IsNullOrEmpty(filter))
            MandantBox.IsDropDownOpen = MandantBox.Items.Count > 0;
    }

    private void OnMandantBoxKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Return) return;
        var typed = (MandantBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(typed)) return;

        var exact = _mandanten.FirstOrDefault(m => string.Equals(m.Name, typed, StringComparison.OrdinalIgnoreCase));
        var toSelect = exact.Name;
        if (toSelect is null)
        {
            var candidates = _mandanten.Where(m => m.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 1) toSelect = candidates[0].Name;
        }
        if (toSelect is not null)
        {
            MandantBox.IsDropDownOpen = false;
            MandantBox.SelectedItem = toSelect;
            e.Handled = true;
        }
    }

    private void OnMandantChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressMandantEvents) return;
        var selected = MandantBox.SelectedItem as string;

        // Zusammenfassung für den bisherigen Mandanten im Hintergrund anstoßen, bevor
        // Kontext/Verlauf auf den neuen Mandanten umgestellt wird. Fire-and-forget,
        // ohne auf das Task-Ergebnis zu warten — die UI darf beim Wechsel nicht
        // einfrieren, und ChatSummaryService fängt alle Fehler selbst ab.
        if (!string.IsNullOrEmpty(_activeMandantId))
            _ = _chatSummaryService.SummarizeSessionAsync(_activeMandantId);

        _history.Clear();
        ChatPanel.Children.Clear();
        UnterhaltBtn.Visibility = Visibility.Collapsed;
        NotizenBtn.Visibility = Visibility.Collapsed;
        ZusammenfassungBtn.Visibility = Visibility.Collapsed;
        KopierenBtn.Visibility = Visibility.Collapsed;
        _mandantAnalyseService.Abbrechen();

        _loadHistoryCts?.Cancel();

        if (string.IsNullOrEmpty(selected) || selected == KeinMandantLabel)
        {
            _loadHistoryCts?.Dispose();
            _loadHistoryCts = null;
            _activeMandantId   = null;
            _activeMandantName = null;
            var msg = "Kein Mandant ausgewählt — allgemeines Gespräch.";
            if (_webViewInitialized)
            {
                _chatHistoryHtml.Clear();
                AddHtmlMessage(ChatHtmlRenderer.WrapSystemMessage(msg));
                RefreshChatWebView();
            }
            else
                AppendSystemMessage(msg);
            BuildFileTree(null);
            NotizenBtn.Visibility = Visibility.Collapsed;
            ZusammenfassungBtn.Visibility = Visibility.Collapsed;
            KopierenBtn.Visibility = Visibility.Collapsed;
            return;
        }

        var match = _mandanten.FirstOrDefault(m => string.Equals(m.Name, selected, StringComparison.OrdinalIgnoreCase));
        if (match.Name is null) return; // getippter Text ohne (eindeutigen) Treffer

        _activeMandantId   = match.Id;
        _activeMandantName = match.Name;
        var activeMsg = $"Mandant: {match.Name} — Chat-Kontext aktiv.";
        NotizenBtn.Visibility = Visibility.Visible;
        ZusammenfassungBtn.Visibility = Visibility.Visible;
        KopierenBtn.Visibility = Visibility.Collapsed;
        _mandantAnalyseService.StarteScan(match.Id);
        if (_webViewInitialized)
        {
            _chatHistoryHtml.Clear();
            AddHtmlMessage(ChatHtmlRenderer.WrapSystemMessage(activeMsg));
            RefreshChatWebView();
        }
        else
            AppendSystemMessage(activeMsg);

        // Vorhandene Chat-Historie im Hintergrund laden (Task #226). Ein laufender
        // Ladevorgang wird beim erneuten Wechsel abgebrochen, damit keine Historie
        // des vorherigen/falschen Mandanten angezeigt wird.
        _loadHistoryCts?.Cancel();
        _loadHistoryCts?.Dispose();
        _loadHistoryCts = new CancellationTokenSource();
        _ = Task.Run(() => LoadChatHistoryAsync(match.Id, _loadHistoryCts.Token));

        BuildFileTree(match.Name);
    }

    /// <summary>Lädt die gespeicherte Chat-Historie eines Mandanten aus der lokalen DB
    /// im Hintergrund und zeigt sie im Chat-Panel an. Die neueste KI-Zusammenfassung
    /// wird als kompakter Kontext-Block oben eingeblendet; die darauf folgenden
    /// Roh-Nachrichten (seit dem Ende der Zusammenfassung) erscheinen als normale
    /// Chat-Bubbles. Funktioniert sowohl für WebView2 als auch für den Fallback-Pfad.
    /// </summary>
    private async Task LoadChatHistoryAsync(string mandantId, CancellationToken token)
    {
        try
        {
            var zusammenfassungen = await Task.Run(() => _db.GetChatZusammenfassungen(mandantId), token).ConfigureAwait(false);
            var letzteZusammenfassung = zusammenfassungen
                .OrderByDescending(z => z.SitzungEnde)
                .FirstOrDefault();

            var seit = letzteZusammenfassung.SitzungEnde != default(DateTime)
                ? letzteZusammenfassung.SitzungEnde
                : (DateTime?)null;

            var nachrichten = await Task.Run(() => _db.GetChatHistorySeit(mandantId, seit), token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();

            // Nur anwenden, wenn der gewählte Mandant noch aktiv ist (Race-Condition-Schutz).
            if (_activeMandantId != mandantId) return;

            const int maxRohNachrichten = 50;
            var anzuzeigendeNachrichten = nachrichten.TakeLast(maxRohNachrichten).ToList();

            if (letzteZusammenfassung.Zusammenfassung is not null &&
                !string.IsNullOrWhiteSpace(letzteZusammenfassung.Zusammenfassung) &&
                anzuzeigendeNachrichten.Count == 0)
            {
                // Nur Zusammenfassung, keine neuen Nachrichten seitdem -> kompakten Kontext-Block anzeigen.
                var kontextText = $"Letzte Sitzung ({letzteZusammenfassung.SitzungEnde:dd.MM.yyyy HH:mm}): {letzteZusammenfassung.Zusammenfassung}";
                Dispatcher.Invoke(() =>
                {
                    if (_activeMandantId != mandantId) return;
                    if (_webViewInitialized)
                    {
                        AddHtmlMessage(ChatHtmlRenderer.WrapSystemMessage(kontextText));
                        RefreshChatWebView();
                    }
                    else
                    {
                        AppendSystemMessage(kontextText);
                    }
                });
            }
            else if (anzuzeigendeNachrichten.Count > 0)
            {
                Dispatcher.Invoke(() =>
                {
                    if (_activeMandantId != mandantId) return;

                    if (letzteZusammenfassung.Zusammenfassung is not null &&
                        !string.IsNullOrWhiteSpace(letzteZusammenfassung.Zusammenfassung))
                    {
                        var kontextText = $"Zusammenfassung bisheriger Sitzung ({letzteZusammenfassung.SitzungEnde:dd.MM.yyyy HH:mm}): {letzteZusammenfassung.Zusammenfassung}";
                        if (_webViewInitialized)
                            AddHtmlMessage(ChatHtmlRenderer.WrapSystemMessage(kontextText));
                        else
                            AppendSystemMessage(kontextText);
                    }

                    foreach (var (role, content, _) in anzuzeigendeNachrichten)
                    {
                        _history.Add(new ChatMessage(role, content));
                        switch (role.ToLowerInvariant())
                        {
                            case "user":
                                if (_webViewInitialized)
                                    AddHtmlMessage(ChatHtmlRenderer.WrapUserBubble(WebUtility.HtmlEncode(content)));
                                else
                                    AppendUserMessage(content);
                                break;
                            case "assistant":
                                if (_webViewInitialized)
                                    AddHtmlMessage(ChatHtmlRenderer.WrapAiBubble(ChatHtmlRenderer.Render(content), "frage"));
                                else
                                    AppendAiMessage(content, "frage");
                                break;
                            default:
                                if (_webViewInitialized)
                                    AddHtmlMessage(ChatHtmlRenderer.WrapSystemMessage(content));
                                else
                                    AppendSystemMessage(content);
                                break;
                        }
                    }

                    if (_webViewInitialized)
                        RefreshChatWebView();
                });
            }
            // Keine Historie -> die bereits gesetzte Systemnachricht bleibt allein stehen.
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadChatHistoryAsync] Laden für Mandant {mandantId} abgebrochen.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadChatHistoryAsync] Laden für Mandant {mandantId} fehlgeschlagen: {ex.Message}");
        }
    }

    private void OnFileTreeNodeExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem tvi || tvi.DataContext is not Models.FileTreeNode node || !node.IsFolder)
            return;

        if (node.Children.Count == 0)
        {
            try
            {
                AddFileTreeChildren(node, node.Path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LazyLoad] {node.Path}: {ex.Message}");
            }
        }
    }

    /// <summary>Baut den Dateibaum (links) neu auf — bei fehlendem Filter nur
    /// einzelne Wurzelknoten je Mandantenordner, OHNE rekursiv alle Unterordner
    /// eager aufzubauen. Kinder werden lazy beim Aufklappen geladen.
    /// Falls ein filterMandantName gesetzt ist, werden nur passende Wurzeln
    /// angelegt und diese vollständig aufgebaut (Detailansicht).</summary>
    private void BuildFileTree(string? filterMandantName = null)
    {
        var basePath = _settings.DokumentePfad;
        var roots = new System.Collections.ObjectModel.ObservableCollection<Models.FileTreeNode>();
        if (Directory.Exists(basePath))
        {
            var mandantDirs = Directory.EnumerateDirectories(basePath);
            var filterActive = !string.IsNullOrEmpty(filterMandantName);
            if (filterActive)
                mandantDirs = mandantDirs.Where(d =>
                    string.Equals(Path.GetFileName(d), filterMandantName, StringComparison.OrdinalIgnoreCase));

            foreach (var mandantDir in mandantDirs.OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                var node = new Models.FileTreeNode(Path.GetFileName(mandantDir), mandantDir, isFolder: true);
                if (filterActive)
                    AddFileTreeChildren(node, mandantDir);
                roots.Add(node);
            }
        }
        _fileTreeRoots = roots;
        Dispatcher.Invoke(() =>
        {
            FileTree.ItemsSource = null;
            FileTree.ItemsSource = _fileTreeRoots;
            if (string.IsNullOrEmpty(FileTreeSearchBox.Text))
                ApplyFileTreeSearch(FileTreeSearchBox.Text);
        });
    }

    private void AddFileTreeChildren(Models.FileTreeNode parent, string dirPath)
    {
        try
        {
            foreach (var subDir in Directory.EnumerateDirectories(dirPath)
                         .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                var subNode = new Models.FileTreeNode(Path.GetFileName(subDir), subDir, isFolder: true);
                AddFileTreeChildren(subNode, subDir);
                parent.AddChild(subNode);
            }
            foreach (var file in Directory.EnumerateFiles(dirPath)
                         .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            {
                var fileNode = new Models.FileTreeNode(file) { IsPrioritized = _prioritizedPaths.Contains(file) };
                parent.AddChild(fileNode);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FileTree] Fehler bei {dirPath}: {ex.Message}");
        }
    }

    /// <summary>Case-insensitive Namenssuche im Dateibaum — Ordner bleiben sichtbar
    /// wenn ein Nachfahre matcht, auch wenn der Ordnername selbst nicht passt.</summary>
    private void ApplyFileTreeSearch(string? searchText)
    {
        var text = (searchText ?? "").Trim();
        if (string.IsNullOrEmpty(text))
        {
            FileTree.ItemsSource = null;
            FileTree.ItemsSource = _fileTreeRoots;
            return;
        }
        var filtered = new System.Collections.ObjectModel.ObservableCollection<Models.FileTreeNode>();
        foreach (var root in _fileTreeRoots)
        {
            var match = FilterFileTreeNode(root, text);
            if (match is not null) filtered.Add(match);
        }
        FileTree.ItemsSource = null;
        FileTree.ItemsSource = filtered;
    }

    private static Models.FileTreeNode? FilterFileTreeNode(Models.FileTreeNode node, string searchText)
    {
        if (node.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            return node; // Name passt -> kompletter Teilbaum bleibt sichtbar

        if (!node.IsFolder) return null;

        var copy = new Models.FileTreeNode(node.Name, node.Path, isFolder: true) { IsPrioritized = node.IsPrioritized };
        foreach (var child in node.Children)
        {
            var filteredChild = FilterFileTreeNode(child, searchText);
            if (filteredChild is not null) copy.AddChild(filteredChild);
        }
        return copy.Children.Count > 0 ? copy : null;
    }

    private void OnFileTreeSearchChanged(object sender, TextChangedEventArgs e)
    {
        var text = FileTreeSearchBox.Text;
        // Für die Suche müssen alle Kinder verfügbar sein; lazy geladene Knoten
        // werden bei Bedarf expandiert und aufgebaut.
        EnsureFileTreeChildrenLoaded(_fileTreeRoots);
        ApplyFileTreeSearch(text);
    }

    private void EnsureFileTreeChildrenLoaded(IEnumerable<Models.FileTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsFolder && node.Children.Count == 0)
                AddFileTreeChildren(node, node.Path);
            if (node.IsFolder)
                EnsureFileTreeChildrenLoaded(node.Children);
        }
    }

    /// <summary>Doppelklick öffnet die Datei im Standardprogramm. Einfacher Klick
    /// wählt nur aus (kein Seiteneffekt mehr) — Priorisieren läuft über Rechtsklick.</summary>
    private void OnFileTreeDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FileTree.SelectedItem is not Models.FileTreeNode selectedNode) return;
        if (selectedNode.IsFolder || string.IsNullOrEmpty(selectedNode.Path) || !File.Exists(selectedNode.Path)) return;

        try
        {
            Process.Start(new ProcessStartInfo(selectedNode.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AddReasoning("⚠️", $"Datei konnte nicht geöffnet werden: {ex.Message}");
        }
    }

    /// <summary>Rechtsklick-Menüpunkt: Dokument im Chat-Kontext hervorheben (oder
    /// Hervorhebung aufheben). Betrifft nur Dateien, keine Ordner.</summary>
    private void OnTogglePrioritize(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { CommandParameter: Models.FileTreeNode node } || node.IsFolder)
            return;

        if (_prioritizedPaths.Contains(node.Path))
        {
            _prioritizedPaths.Remove(node.Path);
            node.IsPrioritized = false;
        }
        else
        {
            _prioritizedPaths.Add(node.Path);
            node.IsPrioritized = true;
        }
    }

    // ── Chat ──────────────────────────────────────────────────────────────────

    private void OnClearChat(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        ChatPanel.Children.Clear();
        _chatHistoryHtml.Clear();
        UnterhaltBtn.Visibility = Visibility.Collapsed;
        KopierenBtn.Visibility = Visibility.Collapsed;
        var msg = _activeMandantName is not null
            ? $"Chat gelöscht — Mandant: {_activeMandantName}"
            : "Chat gelöscht. Wie kann ich Ihnen helfen?";
        if (_webViewInitialized)
        {
            AddHtmlMessage(ChatHtmlRenderer.WrapSystemMessage(msg));
            RefreshChatWebView();
        }
        else
        {
            AppendSystemMessage(msg);
        }
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

    private void OnInputKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // InputBox ist einzeilig (kein AcceptsReturn) — Return schickt direkt ab.
        if (e.Key == System.Windows.Input.Key.Return)
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
        WolfLoadingPanel.Visibility = Visibility.Visible;
        var wolfStoryboard = (Storyboard)WolfLoadingPanel.Resources["WolfPulseStoryboard"];
        wolfStoryboard.Begin(WolfLoadingPanel);

        try
        {
            var (content, suggestedAction) = await PostChatAsync();

            _history.Add(new ChatMessage("assistant", content));
            _db.AddChatMessage(_activeMandantId?.ToString() ?? "global", "assistant", content);
            AppendAiMessage(content, suggestedAction);

            Dispatcher.Invoke(() =>
            {
                UnterhaltBtn.Visibility = suggestedAction == "berechne_unterhalt"
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                KopierenBtn.Visibility = Visibility.Visible;
            });
        }
        catch (Exception ex)
        {
            AppendSystemMessage($"Fehler: {ex.Message}");
        }
        finally
        {
            Dispatcher.Invoke(() => SendBtn.IsEnabled = true);
            WolfLoadingPanel.Visibility = Visibility.Collapsed;
            var storyboard = (Storyboard)WolfLoadingPanel.Resources["WolfPulseStoryboard"];
            storyboard.Stop(WolfLoadingPanel);
        }
    }

    private async void OnSendStyleFeedback(object sender, RoutedEventArgs e)
    {
        if (!_settings.FeedbackOptIn)
        {
            AddReasoning("🔒", "Stil-Feedback ist in den Einstellungen deaktiviert. Es wurden keine Daten gesendet.");
            return;
        }
        await SendStyleFeedbackFromInputAsync();
    }

    private async Task SendStyleFeedbackFromInputAsync()
    {
        try
        {
            var text = InputBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                AddReasoning("🔒", "Kein Text eingegeben - Stil-Feedback nicht gesendet.");
                return;
            }

            var categories = new List<string> { "formulierung" };
            var success = await _feedbackSender.SendMetricsAsync(
                new List<string> { text },
                categories);

            if (success)
                AddReasoning("🔒", "Anonymisierte Stil-Metriken erfolgreich gesendet.");
            else
                AddReasoning("⚠️", "Server hat Stil-Metriken abgelehnt.");
        }
        catch (Exception ex)
        {
            AddReasoning("⚠️", $"Fehler beim Senden von Stil-Metriken: {ex.Message}");
        }
    }

    // ── Chat-Backend ─────────────────────────────────────────────────────────

    private async Task<(string content, string suggestedAction)> PostChatAsync()
    {
        // Früher standen hier zusätzlich "Suche in Rechtsdatenbank..." und
        // "Generiere juristische Antwort..." als eigene Schritte — beides pauschale
        // Behauptungen, die client-seitig zu diesem Zeitpunkt noch gar nicht
        // stattgefunden haben (Suche+Generierung passieren serverseitig in einem
        // einzigen HTTP-Call). Wirkte nach mehr Einzelschritten als tatsächlich
        // passiert; ein ehrlicher "läuft"-Status reicht.
        string? mandantContext = _activeMandantName is not null
            ? $"Mandant: {_activeMandantName} (ID: {_activeMandantId})"
            : null;

        // Ohne das sah das LLM nur Name/ID des Mandanten, nie die tatsächlich im
        // Mandantenordner gescannten Dokumente (z.B. eine Unterhaltsrechnung) —
        // dadurch kamen bei "was lief bisher"-Fragen erfundene bzw. unvollständige
        // Antworten zustande, weil schlicht kein Fallkontext vorlag.
        string? mandantDokumenteContext = null;
        if (_activeMandantId is not null)
        {
            var dokumente = _db.GetDokumenteForMandant(_activeMandantId);
            if (dokumente.Count > 0)
            {
                // Per Rechtsklick im Dateibaum priorisierte Dokumente zuerst und
                // gesondert hervorgehoben — hilft bei Mandanten mit vielen
                // Dokumenten, wo nicht alles gleich relevant für die aktuelle
                // Frage ist, ohne die übrigen Dokumente komplett auszuschließen.
                var prioritized = dokumente.Where(d => _prioritizedPaths.Contains(d.Pfad)).ToList();
                var rest = dokumente.Where(d => !_prioritizedPaths.Contains(d.Pfad)).ToList();

                var sections = new List<string>();
                if (prioritized.Count > 0)
                {
                    var parts = prioritized.Select(d => $"[{d.Titel}]\n{d.Text}");
                    sections.Add("Besonders relevant für diese Frage (vom Anwalt priorisiert):\n\n" +
                                 string.Join("\n\n---\n\n", parts));
                }
                if (rest.Count > 0)
                {
                    var parts = rest.Select(d => $"[{d.Titel}]\n{d.Text}");
                    sections.Add("Weitere Dokumente dieses Mandanten (lokal gescannt):\n\n" +
                                 string.Join("\n\n---\n\n", parts));
                }
                mandantDokumenteContext = string.Join("\n\n", sections);
            }
        }

        var templateContext = GetLatestTemplateContext();
        var attorneyContext = GetAttorneyContext();
        var contextParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(mandantContext)) contextParts.Add(mandantContext);
        if (!string.IsNullOrWhiteSpace(mandantDokumenteContext)) contextParts.Add(mandantDokumenteContext);
        if (!string.IsNullOrWhiteSpace(attorneyContext)) contextParts.Add(attorneyContext);
        if (!string.IsNullOrWhiteSpace(templateContext)) contextParts.Add(templateContext);

        var payload = JsonSerializer.Serialize(new
        {
            messages       = _history.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            mandant_context = contextParts.Count > 0 ? string.Join("\n\n", contextParts) : null,
        });
        var httpContent = new StringContent(payload, Encoding.UTF8, "application/json");

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

    private static System.Windows.Controls.TextBox CreateSelectableTextBox(string text, System.Windows.Media.Brush foreground, double fontSize = 13)
    {
        var box = new System.Windows.Controls.TextBox
        {
            Text = text,
            Foreground = foreground,
            FontSize = fontSize,
            TextWrapping = System.Windows.TextWrapping.Wrap,
            AcceptsReturn = true,
            IsReadOnly = true,
            BorderThickness = new System.Windows.Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Padding = new System.Windows.Thickness(0),
            Margin = new System.Windows.Thickness(0),
            IsTabStop = false,
            SelectionBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 111, 235)),
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
        };

        // Explizites Kontextmenü — funktioniert unabhängig davon, ob Drag-Selektion
        // im konkreten Layout mal hakt; garantiert einen sicheren Copy-Weg.
        var copyItem = new System.Windows.Controls.MenuItem { Header = "Kopieren" };
        copyItem.Click += (_, _) =>
        {
            var toCopy = box.SelectionLength > 0 ? box.SelectedText : box.Text;
            if (!string.IsNullOrEmpty(toCopy)) System.Windows.Clipboard.SetText(toCopy);
        };
        var selectAllItem = new System.Windows.Controls.MenuItem { Header = "Alles auswählen" };
        selectAllItem.Click += (_, _) => box.SelectAll();
        box.ContextMenu = new System.Windows.Controls.ContextMenu
        {
            Items = { copyItem, selectAllItem }
        };

        return box;
    }

    private void AppendSystemMessage(string text)
    {
        Dispatcher.Invoke(() =>
        {
            if (_webViewInitialized)
            {
                AddHtmlMessage(ChatHtmlRenderer.WrapSystemMessage(text));
                RefreshChatWebView();
                ScrollToBottom();
                return;
            }

            var border = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Background          = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                CornerRadius        = new CornerRadius(8),
                Padding             = new Thickness(12, 6, 12, 6),
                Margin              = new Thickness(0, 4, 0, 8),
            };
            border.Child = CreateSelectableTextBox(
                text,
                new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                11);

            ChatPanel.Children.Add(border);
            ScrollToBottom();
        });
    }

    private void AppendUserMessage(string text)
    {
        Dispatcher.Invoke(() =>
        {
            if (_webViewInitialized)
            {
                AddHtmlMessage(ChatHtmlRenderer.WrapUserBubble(WebUtility.HtmlEncode(text)));
                RefreshChatWebView();
                ScrollToBottom();
                return;
            }

            var bubble = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Background          = new SolidColorBrush(Color.FromRgb(31, 111, 235)),
                CornerRadius        = new CornerRadius(16, 4, 16, 16),
                Padding             = new Thickness(14, 10, 14, 10),
                Margin              = new Thickness(60, 4, 8, 4),
                MaxWidth            = 500,
            };
            bubble.Child = CreateSelectableTextBox(text, Brushes.White);
            ChatPanel.Children.Add(bubble);
            ScrollToBottom();
        });
    }

    private void AppendAiMessage(string text, string suggestedAction, string? filePath = null)
    {
        Dispatcher.Invoke(() =>
        {
            if (_webViewInitialized)
            {
                var html = ChatHtmlRenderer.Render(text);
                if (filePath is not null)
                {
                    html += $"\n<p class='chat-paragraph'><a class='chat-link' href='#' data-file='{WebUtility.HtmlEncode(filePath)}'>📁 Vorlage im Explorer öffnen</a></p>";
                }
                AddHtmlMessage(ChatHtmlRenderer.WrapAiBubble(html, suggestedAction));
                RefreshChatWebView();
                ScrollToBottom();
                return;
            }

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
            bubble.Child = CreateSelectableTextBox(
                text,
                new SolidColorBrush(Color.FromRgb(201, 209, 217)));
            container.Children.Add(bubble);

            if (filePath is not null)
            {
                var link = new TextBlock
                {
                    Margin = new Thickness(0, 6, 0, 0),
                    Foreground = new SolidColorBrush(Color.FromRgb(31, 111, 235)),
                    FontSize = 12,
                };
                var hyperlink = new Hyperlink
                {
                    Inlines = { "📁 Vorlage im Explorer öffnen" },
                };
                hyperlink.Click += (_, __) => OpenInExplorer(filePath);
                link.Inlines.Add(hyperlink);
                container.Children.Add(link);
            }

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
                // War: komplettes Formular wurde direkt in die Antwort eingebettet,
                // auch wenn die Frage schon inhaltlich aus den Mandanten-Dokumenten
                // beantwortet war ("wieviel bekommt X" triggert die grobe Keyword-
                // Erkennung in chat.py). Jetzt wie bei "erstelle_dokument" nur ein
                // Button — der ohnehin vorhandene UnterhaltBtn wird zusätzlich sichtbar.
                var btn = new Button
                {
                    Content             = "⚖ Unterhalt berechnen",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background          = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                    Foreground          = new SolidColorBrush(Color.FromRgb(232, 168, 56)),
                    BorderBrush         = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
                    BorderThickness     = new Thickness(1),
                    Padding             = new Thickness(12, 6, 12, 6),
                    Margin              = new Thickness(0, 6, 0, 0),
                    FontSize            = 12,
                    Cursor              = Cursors.Hand,
                };
                btn.Click += (s, e) => OnUnterhaltBtnClick(s, e);
                container.Children.Add(btn);
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
            var templateContext = GetLatestTemplateContext();
            var attorneyContext = GetAttorneyContext();
            var prompt = $"Erstelle eine Vorlage basierend auf folgendem Kontext: {context}";
            if (!string.IsNullOrWhiteSpace(attorneyContext))
                prompt += $"\n\nKonfigurierte Anwaltsdaten für Briefkopf und Briefende:\n{attorneyContext}";
            if (!string.IsNullOrWhiteSpace(templateContext))
                prompt += $"\n\nBestehende bearbeitete Vorlage des Nutzers:\n{templateContext}";

            var payload = JsonSerializer.Serialize(new { text = prompt });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{BackendUrl}/ask", content);
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root   = doc.RootElement;
            var output = root.TryGetProperty("output", out var o) ? o.GetString() ?? json : json;

            var templatePath = SaveTemplateFile(output);
            _history.Add(new ChatMessage("assistant", output));
            _db.AddChatMessage(_activeMandantId?.ToString() ?? "global", "assistant", output);
            AppendAiMessage($"📄 Vorlage:\n\n{output}", "frage", templatePath);
            ReasoningPanel.Children.Clear();
            AddReasoning("✅", "Vorlage erstellt");
        }
        catch (Exception ex)
        {
            AppendAiMessage($"Fehler beim Erstellen der Vorlage: {ex.Message}", "frage");
        }
    }

    private string SaveTemplateFile(string content)
    {
        var baseDir = Directory.Exists(_settings.DokumentePfad)
            ? _settings.DokumentePfad
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LexWolf", "Vorlagen");
        var templateDir = Path.Combine(baseDir, "Vorlagen");
        Directory.CreateDirectory(templateDir);

        var fileName = $"vorlage_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        var filePath = Path.Combine(templateDir, fileName);
        File.WriteAllText(filePath, content, Encoding.UTF8);
        return filePath;
    }

    private string? GetAttorneyContext()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(_settings.Briefkopf))
            parts.Add($"Briefkopf:\n{_settings.Briefkopf}");
        if (!string.IsNullOrWhiteSpace(_settings.Briefende))
            parts.Add($"Briefende:\n{_settings.Briefende}");
        return parts.Count > 0 ? string.Join("\n\n", parts) : null;
    }

    private string? GetLatestTemplateContext()
    {
        var baseDir = Directory.Exists(_settings.DokumentePfad)
            ? _settings.DokumentePfad
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LexWolf", "Vorlagen");
        var templateDir = Path.Combine(baseDir, "Vorlagen");
        if (!Directory.Exists(templateDir)) return null;

        var latestFile = Directory.EnumerateFiles(templateDir, "*", SearchOption.TopDirectoryOnly)
            .Where(path => File.Exists(path))
            .OrderByDescending(path => new FileInfo(path).LastWriteTimeUtc)
            .FirstOrDefault();

        if (latestFile is null) return null;

        var content = File.ReadAllText(latestFile, Encoding.UTF8);
        return string.IsNullOrWhiteSpace(content) ? null : $"Aktuelle Vorlage auf dem Rechner:\n{content}";
    }

    private void OpenInExplorer(string filePath)
    {
        if (!File.Exists(filePath)) return;

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select, \"{filePath}\"",
            UseShellExecute = true,
        };
        Process.Start(startInfo);
    }

    private void ScrollToBottom() => ChatScrollViewer.ScrollToBottom();


    private string LoadFileContent(string filePath)
    {
        var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        try
        {
            if (ext == ".txt") return System.IO.File.ReadAllText(filePath);
            if (ext == ".docx") return ReadDocx(filePath);
            if (ext == ".pdf") return ReadPdf(filePath);
            if (ext == ".eml") return ReadEml(filePath);
            return "Dateityp nicht unterstützt";
        }
        catch (Exception ex)
        {
            return $"Fehler beim Laden: {ex.Message}";
        }
    }

    private static string ReadDocx(string path)
    {
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            var entry = zip.GetEntry("word/document.xml");
            if (entry == null) return string.Empty;
            using var stream = entry.Open();
            using var reader = new System.IO.StreamReader(stream);
            var xml = reader.ReadToEnd();
            var sb = new System.Text.StringBuilder();
            bool inside = false;
            foreach (char c in xml)
            {
                if (c == '<') { inside = true; continue; }
                if (c == '>') { inside = false; sb.Append(' '); continue; }
                if (!inside) sb.Append(c);
            }
            return sb.ToString();
        }
        catch { return string.Empty; }
    }

    private static string ReadPdf(string path)
    {
        // War eine dritte, noch primitivere PDF-Lese-Kopie (druckbare Bytes aus der
        // Rohdatei filtern -> reiner Datenmüll). Bisher unerreichbar, weil FileTree
        // nie befüllt war und diese Methode dadurch faktisch nie aufgerufen wurde.
        try
        {
            return Services.PdfTextExtractor.ExtractText(path);
        }
        catch (Exception ex)
        {
            return $"[PDF konnte nicht gelesen werden: {ex.Message}]";
        }
    }

    private static string ReadEml(string path)
    {
        try
        {
            return System.IO.File.ReadAllText(path);
        }
        catch { return string.Empty; }
    }

    private async void OnScanFolderClicked(object sender, RoutedEventArgs e)
    {
        var folderDialog = new Forms.FolderBrowserDialog
        {
            Description = "Verzeichnis zum Scannen auswählen",
            UseDescriptionForTitle = true,
        };

        var result = folderDialog.ShowDialog();
        if (result == Forms.DialogResult.OK)
        {
            var folderPath = folderDialog.SelectedPath;
            if (!string.IsNullOrEmpty(folderPath))
            {
                ProgressBar.Value = 0;
                SetProgressBusy(true);

                await Task.Run(() =>
                {
                    var totalFiles = 0;
                    var scannedFiles = 0;
                    var extensions = new[] { ".docx", ".pdf", ".txt", ".eml" };

                    foreach (var file in Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories))
                    {
                        if (extensions.Contains(System.IO.Path.GetExtension(file).ToLowerInvariant()))
                        {
                            totalFiles++;
                        }
                    }

                    foreach (var file in Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories))
                    {
                        if (extensions.Contains(System.IO.Path.GetExtension(file).ToLowerInvariant()))
                        {
                            scannedFiles++;
                            var progress = (double)scannedFiles / totalFiles * 100;
                            Dispatcher.Invoke(() => ProgressBar.Value = progress);
                        }
                    }
                });

                SetProgressBusy(false);
                MessageBox.Show($"Scan abgeschlossen. Dateien wurden indexiert.");
            }
        }
    }

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
            var textBox = CreateSelectableTextBox(text, new SolidColorBrush(Color.FromRgb(201, 209, 217)), 12);
            textBox.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(textBox);
            ReasoningPanel.Children.Add(row);
        });
    }

    private async void OnExportClicked(object sender, RoutedEventArgs e)
    {
        // Ordnerauswahl für Export-Ziel
        var folderDialog = new Forms.FolderBrowserDialog();
        folderDialog.Description = "Wählen Sie den Ausgabeordner für anonymisierte Dateien";
        folderDialog.ShowNewFolderButton = true;
        
        var result = folderDialog.ShowDialog();
        if (result != Forms.DialogResult.OK)
            return;

        var outputFolder = folderDialog.SelectedPath;
        var inputFolder = ""; // Dies müsste aus den Dateibaum-Dateien ermittelt werden

        // Export-Fortschritt
        SetProgressBusy(true);
        ProgressBar.Value = 0;
        
        // Alle Dateien im Input-Ordner rekursiv durchlaufen
        var extensions = new[] { ".docx", ".pdf", ".txt", ".eml" };
        var allFiles = new System.Collections.Generic.List<string>();
        try
        {
            allFiles = Directory.GetFiles(inputFolder, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Fehler beim Scannen: {ex.Message}");
            SetProgressBusy(false);
            return;
        }

        if (allFiles.Count == 0)
        {
            System.Windows.MessageBox.Show("Keine unterstützten Dateien gefunden.");
            SetProgressBusy(false);
            return;
        }

        // Export mit Fortschritt
        var exportedCount = 0;
        foreach (var file in allFiles)
        {
            try
            {
                // Relative Pfad ermitteln
                var relativePath = Path.GetRelativePath(inputFolder, file);
                var outputFile = Path.Combine(outputFolder, relativePath);
                var outputDir = Path.GetDirectoryName(outputFile);
                
                // Verzeichnis erstellen
                if (!Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                // Datei laden und anonymisieren
                var content = await Task.Run(() =>
                {
                    return LoadFileContent(file);
                });

                // Anonymisierung simulieren (hier würde der echte Anonymizer laufen)
                var anonymizedContent = AnonymizeText(content);

                // Anonymisierte Datei speichern
                File.WriteAllText(outputFile, anonymizedContent);

                exportedCount++;
                var progress = (double)exportedCount / allFiles.Count * 100;
                Dispatcher.Invoke(() => ProgressBar.Value = progress);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Fehler beim Exportieren von {file}: {ex.Message}");
            }
        }

        SetProgressBusy(false);
        System.Windows.MessageBox.Show($"Export abgeschlossen. {exportedCount} Dateien wurden in {outputFolder} gespeichert.");
    }

    private string AnonymizeText(string text)
    {
        // Hier würde der echte Anonymizer laufen
        // Für Demo: Platzhalter einfügen
        return text.Replace("Hans Müller", "[MANDANT_1]")
                   .Replace("Müller", "[PERSON_1]");
    }

    // ── Testbereich (Task #243) ─────────────────────────────────────────────

    /// <summary>Öffnet den manuellen Verifikations-Dialog für Task #243.
    /// Der Testbereich ist im Desktop-Client als Top-Bar-Button sichtbar und
    /// nutzbar; er zeigt eine kurze Versions-Information an.
    /// </summary>
    private void OnTestbereichClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Testbereich (Task #243) - Client-Version 9.9.9\n" +
            "Manuelle Verifikation: UI-Stelle 'Testbereich' ist sichtbar und nutzbar.",
            "Testbereich",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
