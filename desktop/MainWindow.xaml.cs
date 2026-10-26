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
    // LEXWOLF_BACKEND_URL / LEXWOLF_BOARD_URL überschreiben die Server nur für
    // Tests auf dem Entwicklungsrechner.
    private static readonly string BackendUrl =
        Environment.GetEnvironmentVariable("LEXWOLF_BACKEND_URL") ?? "http://212.227.180.66:8000";
    private static readonly string BoardUrl =
        Environment.GetEnvironmentVariable("LEXWOLF_BOARD_URL") ?? "http://212.227.180.66:8082";

    private AppSettings _settings = AppSettings.Load();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private readonly List<ChatMessage> _history = new();
    private readonly LocalDb _db = new();
    private readonly ChatSummaryService _chatSummaryService;
    private readonly QuellenangabeService _quellenangabeService;
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
    // Läuft nach jedem Mandantenwechsel; wer direkt danach etwas in den Chat schreibt, wartet darauf.
    private Task? _historieLaden;
    private readonly List<string> _chatHistoryHtml = new();
    /// <summary>Datei eines Entwurfs (.docx/.eml): in &lt;Mandant&gt;\Entwürfe, nach der
    /// Übernahme im Mandantenordner; Lose = ohne Mandant unter Dokumente\LexWolf\Schreiben.</summary>
    private sealed class Entwurf
    {
        public required string Pfad { get; set; }
        // Zeitstempel unseres letzten Schreibens — weicht er ab, wurde die Datei
        // zwischendurch (z.B. in Word) bearbeitet und darf nicht überschrieben werden.
        public DateTime GeschriebenUtc { get; set; }
        public bool Uebernommen { get; set; }
        public bool Lose { get; set; }
        public int Fassung { get; set; } = 1;
        public bool Unveraendert => File.Exists(Pfad) && File.GetLastWriteTimeUtc(Pfad) == GeschriebenUtc;
    }

    /// <summary>Eine Entwurfskarte im Chat (ChatHtmlRenderer.WrapEntwurfKarte).</summary>
    private sealed class EntwurfKarte
    {
        public required int Id { get; init; }
        public required ChatEntwurf Daten { get; set; }
        public string? Mandant { get; set; } // Ordnername unter DokumentePfad
        public Entwurf? Datei { get; set; }
        public bool Ersetzt { get; set; }
        public bool Historie { get; init; }
        public string? Hinweis { get; set; }
        public int HtmlIndex { get; set; } = -1;
        public string Html { get; set; } = "";
        // Task #301: confidence_score + geprüfte Quellen der /chat-Antwort, aus der
        // dieser Entwurf entstand — analog zur Chat-Bubble (WrapAiBubble).
        public double? Confidence { get; set; }
        public IReadOnlyList<(string Zitat, int ChunkId)>? Quellen { get; set; }
    }
    // Karten-ID (data-msg der Buttons) -> Karte. Wird nie geleert, damit IDs auch
    // nach Mandantenwechsel eindeutig bleiben.
    private readonly Dictionary<int, EntwurfKarte> _karten = new();
    private readonly HashSet<string> _prioritizedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly DiktatService _diktat = new();
    private bool _diktatBusy;
    private string _diktatVorher = "";
    private string? _diktatErwartet;
    private CancellationTokenSource? _loadHistoryCts;
    private System.Collections.ObjectModel.ObservableCollection<Models.FileTreeNode> _fileTreeRoots = new();

    public MainWindow()
    {
        InitializeComponent();
        _chatSummaryService = new ChatSummaryService(_db, _http, BackendUrl);
        _quellenangabeService = new QuellenangabeService(BackendUrl, _http);
        _mandantAnalyseService = new MandantAnalyseService(_db, _http, BackendUrl);
        _zusammenfassungService = new MandantZusammenfassungService(_db, _http, BackendUrl);
        _mandantAnalyseService.StatusChanged += (_, status) =>
            Dispatcher.Invoke(() => OnAnalyseStatusChanged(status));
        MandantBox.AddHandler(
            System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(OnMandantTextChanged));
        _ = CheckConnectionAsync(showConnecting: true);
        _ = PeriodicHealthCheckAsync();
        _ = LoadMandantenAsync();
        // Vor dem Scanner: der meldet beim ersten Einlesen neue Dateien an den AkteService.
        InitAkte();
        _ = Task.Run(StartDocumentScannerAsync);
        // Einziger Kanal für "Was ist neu": der Update-Dialog zeigt die gesammelten
        // Changelog-Notizen aller verpassten Versionen. Ein separates Popup pro
        // fertigem Milestone (früherer Feature-Status-Hintergrundcheck) erzeugte eine Kette aus
        // Popup+Neustart, obwohl ein Neustart ohne Update nichts Neues bringt.
        _ = CheckForUpdateAsync();
        _ = LoadStyleProgressAsync();
        InputBox.FontFamily = new FontFamily(_settings.ChatInputFont);
        InputBox.FontSize = _settings.ChatInputFontSize;
        ChatHtmlRenderer.SetOutputFont(_settings.ChatOutputFont, _settings.ChatOutputFontSize);
        App.SetzeSchriftgroesse(_settings.ChatOutputFontSize);
        _diktat.LiveTextChanged += OnDiktatLiveText;
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
            ChatWebView.CoreWebView2.WebMessageReceived += OnChatWebMessage;
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
<script>
// RefreshChatWebView baut die Seite bei jeder Nachricht per NavigateToString neu
// auf -> ohne das hier stünde die Ansicht danach immer ganz oben, und die neueste
// Nachricht wäre erst nach Herunterscrollen sichtbar. 'load' fängt nachträglich
// geladene Bilder/Schriften ab, die die Höhe noch verändern.
function lwScrollToEnd() {{ window.scrollTo(0, document.documentElement.scrollHeight); }}
lwScrollToEnd();
window.addEventListener('load', lwScrollToEnd);
// Aktions-Buttons (data-action, data-msg = Antwort-ID) an den Client melden,
// ausgewertet in OnChatWebMessage. value: data-value bzw. die Auswahl des
// Dropdowns, auf das data-select zeigt (Mandant der Entwurfskarte).
document.addEventListener('click', function (e) {{
  var el = e.target.closest('[data-action]');
  if (!el || !window.chrome || !window.chrome.webview) return;
  e.preventDefault();
  var sel = el.getAttribute('data-select');
  var value = sel ? (document.getElementById(sel) || {{}}).value : el.getAttribute('data-value');
  window.chrome.webview.postMessage({{ action: el.getAttribute('data-action'), msg: el.getAttribute('data-msg'), value: value || '' }});
}});
</script>
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
            // Vorlagen/<Schriftsatz-Art>-Unterordner anlegen, falls noch nicht vorhanden
            // (keine Migration bestehender Root-Vorlagen, siehe VorlagenOrdner).
            VorlagenOrdner.SicherstellenUnterordner(path);
            _scanner = new DokumentScanner(path, _db);
            _scanner.OnDateiIndexiert = (mandantId, pfad) => _akte?.DateiIndexiert(mandantId, pfad);
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

    private void OnStatistikClick(object sender, RoutedEventArgs e)
    {
        var dlg = new StatistikDialog(_db) { Owner = this };
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

        System.Windows.Clipboard.SetText(ChatEntwurfParser.AlsKlartext(lastAssistant.Content));

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

    private void OnReActClick(object sender, RoutedEventArgs e)
    {
        var dlg = new LexWolf.Dialogs.ReActDialog(BackendUrl) { Owner = this };
        dlg.ShowDialog();
    }

    private void OnFeatureUebersichtClick(object sender, RoutedEventArgs e)
    {
        var sessionId = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? "lexwolf";
        var dlg = new LexWolf.Dialogs.FeatureUebersichtDialog(_http, BoardUrl, sessionId) { Owner = this };
        dlg.ShowDialog();
    }

    private async void OnZusammenfassungClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_activeMandantId)) return;

        // Bis die Zusammenfassung da ist: Wolf-Animation über dem Chat und im Button,
        // Button gesperrt (kein doppeltes Anstoßen).
        var buttonInhalt = ZusammenfassungBtn.Content;
        ZusammenfassungBtn.IsEnabled = false;
        ZusammenfassungBtn.Content = CreateWolfLoadingContent("wird erstellt…");
        ZeigeWolfLoading("Zusammenfassung wird erstellt — bitte warten...");
        SetProgressBusy(true);
        SetStatus(null, "Zusammenfassung wird ermittelt...");
        string text;
        try
        {
            // Quellen-Hash prüfen: bei unveränderten Chat/Notizen/Dokumenten
            // wird gecachte Zusammenfassung sofort angezeigt, sonst neu generiert.
            text = await _zusammenfassungService.HoleOderErzeugeZusammenfassungAsync(_activeMandantId);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this, $"Zusammenfassung konnte nicht erzeugt werden: {ex.Message}", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            VerbergeWolfLoading();
            ZusammenfassungBtn.Content = buttonInhalt;
            ZusammenfassungBtn.IsEnabled = true;
            SetProgressBusy(false);
            SetStatus(true, $"Verbunden — {BackendUrl}");
        }

        // Erst nach dem Ausblenden der Animation öffnen — der Dialog ist modal.
        var dlg = new ZusammenfassungDialog(text, _activeMandantName) { Owner = this };
        dlg.ShowDialog();
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
            App.SetzeSchriftgroesse(_settings.ChatOutputFontSize);
            if (_webViewInitialized)
                RefreshChatWebView();
            if (_settings.DokumentePfad != oldPath)
            {
                AddReasoning("🔄", $"Neues Verzeichnis: {_settings.DokumentePfad}");
                _ = Task.Run(StartDocumentScannerAsync);
            }
            _ = LoadStyleProgressAsync();
            StarteGrossesSprachmodell(); // gerade eingeschaltet -> sofort laden, nicht erst beim nächsten Start
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

    private const string KeinMandantLabel = MandantFilter.KeinMandantLabel;
    private const int MaxMandantDropdownItems = MandantFilter.MaxMandantDropdownItems;
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
    /// nicht leer bleibt (Task #268). Delegiert die Filterlogik an den testbaren
    /// MandantFilter-Service, sodass der Akzeptanztest den echten Produktionspfad
    /// prüft.
    /// </summary>
    private void ApplyMandantFilter(string? filterText)
    {
        _suppressMandantEvents = true;
        try
        {
            MandantBox.Items.Clear();
            var names = MandantFilter.ApplyFilter(_mandanten, filterText);
            foreach (var name in names)
                MandantBox.Items.Add(name);
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

    /// <summary>Steht im Feld nur der gewählte Mandant (oder nichts), soll das Aufklappen alle zeigen —
    /// nicht nur die Treffer für diesen Namen. Gefiltert wird nur, während man tippt.</summary>
    private void OnMandantDropDownOpened(object? sender, EventArgs e)
    {
        var text = (MandantBox.Text ?? "").Trim();
        if (text.Length > 0 && !string.Equals(text, _activeMandantName, StringComparison.OrdinalIgnoreCase)) return;
        _mandanten.Clear();
        _mandanten.AddRange(_db.GetMandanten());
        _suppressMandantEvents = true;
        try
        {
            ApplyMandantFilter("");
            if (_activeMandantName is not null && MandantBox.Items.Contains(_activeMandantName))
                MandantBox.SelectedItem = _activeMandantName;
        }
        finally
        {
            _suppressMandantEvents = false;
        }
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
        ClearChatPanel();
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
            AktenMandantGewechselt();
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
        var token = _loadHistoryCts.Token;
        _historieLaden = Task.Run(() => LoadChatHistoryAsync(match.Id, token));

        if (_mandantAusBaum) MarkiereAktivenMandantImBaum(match.Name);
        else BuildFileTree(match.Name);
        AktenMandantGewechselt();
    }

    /// <summary>Nur die Hervorhebung umsetzen, ohne den Baum neu aufzubauen (Auswahl kam aus dem Baum).</summary>
    private void MarkiereAktivenMandantImBaum(string? name)
    {
        foreach (var n in _fileTreeRoots)
            n.IsAktiv = string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase);
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

            // Task #130: Gesprächsprotokolle (z.B. per GespraechsRecorder transkribierte
            // Mandantengespräche) erscheinen chronologisch zusammen mit dem Chatverlauf in
            // der Historie, statt in einer separaten Ansicht — fachlich sind sie wie
            // Chat-Nachrichten Teil des Mandanten-Gedächtnisses.
            var gespraeche = await Task.Run(() => _db.GetGespraeche(mandantId), token).ConfigureAwait(false);
            var gespraechNachrichten = gespraeche
                .Where(g => seit is null || g.Datum > seit)
                .Select(g => (Role: "system", Content: $"🎙️ Gespräch vom {g.Datum:dd.MM.yyyy}: {GespraechStruktur.Parse(g.StrukturiertJson).AlsKurztext()}", Timestamp: g.Datum, ConfidenceScore: (double?)null));
            nachrichten = nachrichten.Concat(gespraechNachrichten).OrderBy(n => n.Timestamp).ToList();

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

                    foreach (var (role, content, _, confidenceScore) in anzuzeigendeNachrichten)
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
                                var (rest, entwurf) = ChatEntwurfParser.Zerlege(content);
                                if (!_webViewInitialized)
                                    AppendAiMessage(ChatEntwurfParser.AlsKlartext(content));
                                else
                                {
                                    if (!string.IsNullOrWhiteSpace(rest))
                                        AddHtmlMessage(ChatHtmlRenderer.WrapAiBubble(ChatHtmlRenderer.Render(rest), confidenceScore));
                                    // Zuordnung zur Datei ist nach Neustart weg -> nur Anzeige + Kopieren.
                                    // confidenceScore kommt aus der gespeicherten chat_history-Zeile (quellen_check
                                    // wird nicht persistiert, daher hier null).
                                    if (entwurf is not null)
                                        EntwurfKarteAnlegen(entwurf, historie: true, mitRefresh: false, confidence: confidenceScore);
                                }
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

    /// <summary>Baut den Dateibaum (links) neu auf: immer alle Mandantenordner als
    /// Wurzelknoten, Kinder werden lazy beim Aufklappen geladen. Der aktive Mandant
    /// ist hervorgehoben und aufgeklappt — die übrigen bleiben sichtbar.</summary>
    private void BuildFileTree(string? aktiverMandantName = null)
    {
        var basePath = _settings.DokumentePfad;
        var roots = new System.Collections.ObjectModel.ObservableCollection<Models.FileTreeNode>();
        Models.FileTreeNode? aktiv = null;
        if (Directory.Exists(basePath))
        {
            foreach (var mandantDir in Directory.EnumerateDirectories(basePath).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                var node = new Models.FileTreeNode(Path.GetFileName(mandantDir), mandantDir, isFolder: true) { IsMandantOrdner = true };
                if (string.Equals(node.Name, aktiverMandantName, StringComparison.OrdinalIgnoreCase))
                {
                    AddFileTreeChildren(node, mandantDir);
                    node.IsAktiv = true;
                    node.IsExpanded = true;
                    aktiv = node;
                }
                roots.Add(node);
            }
        }
        if (aktiv is not null)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                (FileTree.ItemContainerGenerator.ContainerFromItem(aktiv) as TreeViewItem)?.BringIntoView());
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
                var fileNode = new Models.FileTreeNode(file)
                {
                    IsPrioritized = _prioritizedPaths.Contains(file),
                    IsVorlagenDatei = IstVorlagenDatei(file),
                };
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

    // Auswahl im Dateibaum wählt den Mandanten oben mit aus — ohne den Baum neu aufzubauen,
    // sonst springt er unter dem Mauszeiger weg und ein Doppelklick zum Öffnen ginge ins Leere.
    private bool _mandantAusBaum;

    private void OnFileTreeAuswahl(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not Models.FileTreeNode node || string.IsNullOrEmpty(node.Path)) return;
        // Ordner laden ihren Inhalt erst bei Bedarf und haben bis dahin keinen Aufklapp-Pfeil.
        if (node.IsFolder && node.Children.Count == 0)
        {
            AddFileTreeChildren(node, node.Path);
            node.IsExpanded = true;
        }
        var ordner = MandantOrdnerVon(node.Path, _settings.DokumentePfad);
        if (ordner is null) return;
        var id = DokumentScanner.MandantIdFuer(ordner);
        if (id == _activeMandantId) return;
        Dispatcher.BeginInvoke(() =>
        {
            _mandantAusBaum = true;
            try { WaehleMandant(id); }
            finally { _mandantAusBaum = false; }
        });
    }

    /// <summary>Name des Mandantenordners (erste Ebene unter dem Aktenverzeichnis), sonst null.</summary>
    internal static string? MandantOrdnerVon(string pfad, string basis)
    {
        try
        {
            var b = Path.GetFullPath(basis).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var voll = Path.GetFullPath(pfad);
            if (!voll.StartsWith(b, StringComparison.OrdinalIgnoreCase)) return null;
            var ordner = voll[b.Length..].Split(Path.DirectorySeparatorChar)[0];
            return ordner.Length > 0 ? ordner : null;
        }
        catch { return null; }
    }

    /// <summary>Doppelklick öffnet Vorlagen-Dateien im eingebauten Editor (Task #306),
    /// alle anderen Dateien im Standardprogramm. Einfacher Klick wählt den Mandanten mit aus —
    /// Priorisieren läuft über Rechtsklick.</summary>
    private void OnFileTreeDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FileTree.SelectedItem is not Models.FileTreeNode selectedNode) return;
        if (selectedNode.IsFolder || string.IsNullOrEmpty(selectedNode.Path) || !File.Exists(selectedNode.Path)) return;

        if (selectedNode.IsVorlagenDatei)
        {
            VorlageBearbeiten(selectedNode.Path);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(selectedNode.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AddReasoning("⚠️", $"Datei konnte nicht geöffnet werden: {ex.Message}");
        }
    }

    /// <summary>true, wenn pfad im Vorlagen-Wurzelordner (oder einem Typ-Unterordner davon,
    /// siehe VorlagenOrdner) liegt und eine im eingebauten Editor unterstützte Endung hat
    /// (Task #306) — steuert den "Bearbeiten"-Menüpunkt und den Doppelklick im Dateibaum.</summary>
    private bool IstVorlagenDatei(string pfad)
    {
        if (!VorlagenEditor.IstBearbeitbar(pfad)) return false;
        var vorlagenOrdner = Path.Combine(_settings.DokumentePfad, VorlagenOrdner.Ordnername);
        var praefix = Path.GetFullPath(vorlagenOrdner) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(pfad).StartsWith(praefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Rechtsklick-Menüpunkt "📝 Bearbeiten" — nur für Vorlagen-Dateien sichtbar
    /// (siehe FileTreeNode.BearbeitenMenuSichtbar).</summary>
    private void OnVorlageBearbeiten(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { CommandParameter: Models.FileTreeNode node }) return;
        if (node.IsFolder || string.IsNullOrEmpty(node.Path)) return;
        VorlageBearbeiten(node.Path);
    }

    private void VorlageBearbeiten(string pfad) => new VorlageEditorDialog(pfad) { Owner = this }.ShowDialog();

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

    /// <summary>Rechtsklick-Menüpunkt: Datei/Unterordner umbenennen (nicht für den
    /// Mandantenordner selbst — dessen Name hängt an der Mandanten-ID, siehe NormalMenuSichtbar).</summary>
    private void OnDateiUmbenennen(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { CommandParameter: Models.FileTreeNode node }) return;
        if (node.IsMandantOrdner || string.IsNullOrEmpty(node.Path)) return;

        var dlg = new UmbenennenDialog(node.Path, node.Name, node.IsFolder) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var ordner = Path.GetDirectoryName(node.Path)!;
        var zielPfad = Path.Combine(ordner, dlg.NeuerName);
        try
        {
            if (node.IsFolder)
            {
                Directory.Move(node.Path, zielPfad);
            }
            else
            {
                File.Move(node.Path, zielPfad);
                _db.DeleteDokumentChunks(node.Path); // der Scanner liest die Datei unter dem neuen Pfad neu ein
            }

            if (_prioritizedPaths.Remove(node.Path))
                _prioritizedPaths.Add(zielPfad);

            BuildFileTree(_activeMandantName);
            AddReasoning("✏️", $"„{node.Name}“ umbenannt in „{dlg.NeuerName}“.");
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, $"„{node.Name}“ konnte nicht umbenannt werden — ist die Datei noch geöffnet?\n\n{ex.Message}",
                "Umbenennen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(this, $"„{node.Name}“ konnte nicht umbenannt werden.\n\n{ex.Message}",
                "Umbenennen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            // Datei kann bereits umbenannt sein (File.Move/Directory.Move liefen durch) — nur
            // DeleteDokumentChunks/BuildFileTree danach ist fehlgeschlagen (z.B. SqliteException,
            // weil der Datei-Scanner gerade gleichzeitig auf die DB zugreift). Ohne diesen Fang
            // riss das die ganze App ab, statt nur diese eine Aktion fehlschlagen zu lassen.
            MessageBox.Show(this, $"„{node.Name}“ umbenennen schlug fehl.\n\n{ex.Message}",
                "Umbenennen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Rechtsklick-Menüpunkt: Datei/Unterordner nach Sicherheitsabfrage löschen
    /// (Papierkorb, wenn das Laufwerk einen hat — wie bei „Mandant löschen“).</summary>
    private void OnDateiLoeschen(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { CommandParameter: Models.FileTreeNode node }) return;
        if (node.IsMandantOrdner || string.IsNullOrEmpty(node.Path)) return;

        var art = node.IsFolder ? "den Ordner" : "die Datei";
        if (MessageBox.Show(this, $"{art} „{node.Name}“ wirklich löschen?\n\nDiese Aktion kann nicht rückgängig gemacht werden, " +
                                   "falls das Laufwerk keinen Papierkorb hat.", "Löschen",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            var papierkorb = MandantLoeschenDialog.HatPapierkorb(node.Path);
            if (node.IsFolder)
            {
                foreach (var datei in Directory.EnumerateFiles(node.Path, "*", SearchOption.AllDirectories))
                    _db.DeleteDokumentChunks(datei);
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(node.Path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    papierkorb ? Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin
                               : Microsoft.VisualBasic.FileIO.RecycleOption.DeletePermanently);
            }
            else
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(node.Path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    papierkorb ? Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin
                               : Microsoft.VisualBasic.FileIO.RecycleOption.DeletePermanently);
                _db.DeleteDokumentChunks(node.Path);
            }

            _prioritizedPaths.Remove(node.Path);
            BuildFileTree(_activeMandantName);
            AddReasoning("🗑", $"„{node.Name}“ gelöscht" + (papierkorb ? " — liegt im Papierkorb." : " — endgültig (kein Papierkorb)."));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"„{node.Name}“ konnte nicht gelöscht werden — ist noch eine Datei daraus geöffnet?\n\n{ex.Message}",
                "Löschen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Rechtsklick-Menüpunkt: Datei im Standardprogramm bzw. Ordner im Explorer anzeigen.</summary>
    private void OnDateiAnzeigen(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { CommandParameter: Models.FileTreeNode node }) return;
        if (string.IsNullOrEmpty(node.Path)) return;

        try
        {
            Process.Start(new ProcessStartInfo(node.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AddReasoning("⚠️", $"„{node.Name}“ konnte nicht angezeigt werden: {ex.Message}");
        }
    }

    // ── Chat ──────────────────────────────────────────────────────────────────

    /// <summary>Entfernt alle Chat-Elemente des WPF-Fallbacks (und z.B. das
    /// Unterhaltsformular) aus ChatPanel — aber NICHT ChatWebView selbst, die ebenfalls
    /// in ChatPanel liegt. Ein pauschales Children.Clear() hängte die WebView aus dem
    /// Fenster aus: danach landeten alle Antworten unsichtbar in einer abgehängten
    /// WebView und der Chatbereich blieb leer.</summary>
    private void ClearChatPanel()
    {
        for (var i = ChatPanel.Children.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(ChatPanel.Children[i], ChatWebView))
                ChatPanel.Children.RemoveAt(i);
        }
    }

    private void OnClearChat(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        ClearChatPanel();
        _chatHistoryHtml.Clear();
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

    private void OnInputKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Läuft als PreviewKeyDown, weil die TextBox (AcceptsReturn) Enter sonst selbst
        // verarbeitet: Enter schickt ab, Umschalt+Enter fügt eine neue Zeile ein.
        if (e.Key == System.Windows.Input.Key.Return
            && System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Shift)
        {
            e.Handled = true;
            OnSend(sender, e);
        }
        else if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
        {
            if (e.Key is System.Windows.Input.Key.OemPlus or System.Windows.Input.Key.Add)
            {
                e.Handled = true;
                ChangeChatFontSize(+1);
            }
            else if (e.Key is System.Windows.Input.Key.OemMinus or System.Windows.Input.Key.Subtract)
            {
                e.Handled = true;
                ChangeChatFontSize(-1);
            }
        }
    }

    private async void OnDiktatClick(object sender, RoutedEventArgs e)
    {
        if (_diktatBusy) return;

        if (!_diktat.IsRecording)
        {
            var whisperFehlt = _settings.DiktatNachkorrektur && !DiktatService.WhisperExists(_settings);
            if ((!DiktatService.VoskExists(_settings) || whisperFehlt) && !await DownloadDiktatModelAsync())
                return;

            _diktatBusy = true;
            DiktatBtn.Content = "⏳";
            DiktatBtn.ToolTip = "Spracherkennung wird geladen …";
            try
            {
                // Text vor dem Diktat bleibt stehen, der Live-Text wird dahinter angezeigt.
                _diktatVorher = InputBox.Text.TrimEnd();
                _diktatErwartet = null;
                InputBox.IsReadOnly = true;
                await _diktat.StartRecordingAsync(_settings);
            }
            catch (Exception ex)
            {
                InputBox.IsReadOnly = false;
                ResetDiktatButton();
                MessageBox.Show(this, $"Aufnahme nicht möglich: {ex.Message}", "Diktat",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally
            {
                _diktatBusy = false;
            }
            // Whisper lädt, während gesprochen wird — spart Wartezeit bei der Nachkorrektur.
            if (_settings.DiktatNachkorrektur)
                _ = _diktat.PreloadWhisperAsync(_settings);
            DiktatBtn.Content = "⏹";
            DiktatBtn.Background = new SolidColorBrush(Color.FromRgb(0xb6, 0x23, 0x24));
            DiktatBtn.ToolTip = "Aufnahme läuft — klicken zum Beenden";
            return;
        }

        _diktatBusy = true;
        string live;
        System.IO.MemoryStream? audio;
        try
        {
            (live, audio) = await _diktat.StopRecordingAsync();
        }
        finally
        {
            _diktatBusy = false;
            InputBox.IsReadOnly = false;
            ResetDiktatButton();
        }
        InputBox.Text = JoinDiktat(_diktatVorher, live);
        InputBox.CaretIndex = InputBox.Text.Length;
        InputBox.ScrollToEnd();
        InputBox.Focus();

        if (audio is null || !_settings.DiktatNachkorrektur || !DiktatService.WhisperExists(_settings))
        {
            audio?.Dispose();
            return;
        }

        // Nachkorrektur im Hintergrund: ersetzt den Live-Text nur, wenn das Feld
        // seitdem unverändert ist (nicht abgeschickt, nicht weitergetippt).
        var vorher = _diktatVorher;
        var erwartet = InputBox.Text;
        _diktatErwartet = erwartet;
        InputBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0xe8, 0xa8, 0x38));
        InputBox.ToolTip = "Diktat wird nachkorrigiert …";
        try
        {
            var korrigiert = await _diktat.TranscribeAsync(_settings, audio, _activeMandantName);
            if (korrigiert.Length > 0 && InputBox.Text == erwartet && !_diktat.IsRecording)
            {
                InputBox.Text = JoinDiktat(vorher, korrigiert);
                InputBox.CaretIndex = InputBox.Text.Length;
                InputBox.ScrollToEnd();
            }
        }
        catch (Exception ex)
        {
            AddReasoning("⚠️", $"Diktat-Nachkorrektur fehlgeschlagen: {ex.Message}");
        }
        finally
        {
            // Nur zurücksetzen, wenn nicht schon ein neueres Diktat korrigiert wird.
            if (_diktatErwartet == erwartet)
            {
                InputBox.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
                InputBox.ClearValue(FrameworkElement.ToolTipProperty);
                _diktatErwartet = null;
            }
        }
    }

    // Wird vom Audio-Thread aufgerufen.
    private void OnDiktatLiveText(string live) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (!_diktat.IsRecording) return;
            InputBox.Text = JoinDiktat(_diktatVorher, live);
            InputBox.CaretIndex = InputBox.Text.Length;
            InputBox.ScrollToEnd();
        });

    private static string JoinDiktat(string vorher, string diktat) =>
        vorher.Length == 0 ? diktat : diktat.Length == 0 ? vorher : $"{vorher} {diktat}";

    private void ResetDiktatButton()
    {
        DiktatBtn.Content = "🎤";
        DiktatBtn.Background = new SolidColorBrush(Color.FromRgb(0x21, 0x26, 0x2d));
        DiktatBtn.ToolTip = "Diktieren — Klick startet, zweiter Klick beendet die Aufnahme. Die Erkennung läuft lokal, kein Ton verlässt den Rechner.";
    }

    private async Task<bool> DownloadDiktatModelAsync()
    {
        var whisper = _settings.DiktatNachkorrektur;
        var antwort = MessageBox.Show(this,
            "Für die Spracheingabe werden einmalig die Sprachmodelle heruntergeladen " +
            (whisper ? "(rund 230 MB: Live-Erkennung + Nachkorrektur)" : "(rund 45 MB)") +
            $" und unter\n{_settings.VoskOrdner}" + (whisper ? $"\n{_settings.WhisperOrdner}" : "") +
            "\ngespeichert.\n\nDie Erkennung läuft danach komplett auf diesem Rechner. Jetzt herunterladen?",
            "Diktat einrichten", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (antwort != MessageBoxResult.Yes) return false;

        _diktatBusy = true;
        var progress = new Progress<double>(p => DiktatBtn.Content = $"{p:P0}");
        try
        {
            await DiktatService.DownloadModelsAsync(_settings, whisper, progress);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Download fehlgeschlagen: {ex.Message}", "Diktat",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            _diktatBusy = false;
            DiktatBtn.Content = "🎤";
        }
    }

    // Ist das Feld auf MaxHeight angewachsen, das Neueste sichtbar halten —
    // aber nur, wenn am Ende geschrieben wird (nicht beim Korrigieren weiter oben).
    private void OnInputTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (InputBox.CaretIndex == InputBox.Text.Length)
            InputBox.ScrollToEnd();
    }

    private void OnFontLarger(object sender, RoutedEventArgs e) => ChangeChatFontSize(+1);
    private void OnFontSmaller(object sender, RoutedEventArgs e) => ChangeChatFontSize(-1);

    // Ändert Eingabe- und Ausgabeschrift gemeinsam; Grenzen wie in AppSettings.SanitizeFonts.
    private void ChangeChatFontSize(double delta)
    {
        _settings.ChatInputFontSize = Math.Clamp(_settings.ChatInputFontSize + delta, 8, 32);
        _settings.ChatOutputFontSize = Math.Clamp(_settings.ChatOutputFontSize + delta, 8, 32);
        try { _settings.Save(); } catch { /* Größe gilt trotzdem für diese Sitzung */ }
        InputBox.FontSize = _settings.ChatInputFontSize;
        ChatHtmlRenderer.SetOutputFont(_settings.ChatOutputFont, _settings.ChatOutputFontSize);
        App.SetzeSchriftgroesse(_settings.ChatOutputFontSize);
        if (_webViewInitialized)
            RefreshChatWebView();
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        var text = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        InputBox.Clear();
        await SendeAsync(text);
    }

    /// <summary>Nachricht an die KI schicken, Antwort samt Entwurfskarte anzeigen.</summary>
    private async Task SendeAsync(string text)
    {
        SendBtn.IsEnabled = false;

        _history.Add(new ChatMessage("user", text));
        AppendUserMessage(text);
        _db.AddChatMessage(_activeMandantId?.ToString() ?? "global", "user", text);

        ReasoningPanel.Children.Clear();
        ZeigeWolfLoading("Anfrage wird verarbeitet...");

        try
        {
            var (content, suggestedAction, confidence, quellen) = await PostChatAsync();

            _history.Add(new ChatMessage("assistant", content));
            _db.AddChatMessage(_activeMandantId?.ToString() ?? "global", "assistant", content, confidence);
            // Versandfertiger Text -> Entwurfskarte (bei geöffnetem Mandanten sofort in
            // <Mandant>\Entwürfe abgelegt); der übrige Text bleibt eine Chat-Bubble.
            // Geöffnet wird nichts von selbst — das entscheidet der Anwalt in der Karte.
            var (rest, entwurf) = ChatEntwurfParser.Zerlege(content, suggestedAction);
            if (!string.IsNullOrWhiteSpace(rest)) AppendAiMessage(rest, confidence, quellen);
            if (entwurf is not null) Dispatcher.Invoke(() => EntwurfKarteAnlegen(entwurf, confidence: confidence, quellen: quellen));

            Dispatcher.Invoke(() => KopierenBtn.Visibility = Visibility.Visible);
        }
        catch (Exception ex)
        {
            AppendSystemMessage($"Fehler: {ex.Message}");
        }
        finally
        {
            Dispatcher.Invoke(() => SendBtn.IsEnabled = true);
            VerbergeWolfLoading();
        }
    }

    // Chat-Anfrage und Zusammenfassung können gleichzeitig laufen — die Animation
    // verschwindet erst, wenn alle fertig sind.
    private int _wolfLoadingAktiv;

    /// <summary>Pulsierende Wolf-Animation über dem Chat mit "bitte warten"-Text.</summary>
    private void ZeigeWolfLoading(string text)
    {
        WolfLoadingText.Text = text;
        if (_wolfLoadingAktiv++ > 0) return;
        WolfLoadingPanel.Visibility = Visibility.Visible;
        ((Storyboard)WolfLoadingPanel.Resources["WolfPulseStoryboard"]).Begin(WolfLoadingPanel, true);
    }

    private void VerbergeWolfLoading()
    {
        if (_wolfLoadingAktiv == 0 || --_wolfLoadingAktiv > 0) return;
        WolfLoadingPanel.Visibility = Visibility.Collapsed;
        ((Storyboard)WolfLoadingPanel.Resources["WolfPulseStoryboard"]).Stop(WolfLoadingPanel);
    }

    // ── Chat-Backend ─────────────────────────────────────────────────────────

    private async Task<(string content, string suggestedAction, double confidence, List<(string Zitat, int ChunkId)> quellen)> PostChatAsync()
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
        var confidence      = root.TryGetProperty("confidence_score", out var cs) && cs.ValueKind == JsonValueKind.Number
            ? cs.GetDouble()
            : 0.0;

        if (root.TryGetProperty("intent", out var ip))
            AddReasoning("🎯", $"Intent: {ip.GetString()}");

        // Task #297: quellen_check (inkl. chunk_id aus T#296) bisher clientseitig nirgends
        // konsumiert — ohne chunk_id kein Sprung zur exakten Stelle möglich, deshalb nur
        // Einträge mit chunk_id übernehmen.
        var quellen = new List<(string Zitat, int ChunkId)>();
        if (root.TryGetProperty("quellen_check", out var qc) && qc.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in qc.EnumerateArray())
            {
                if (item.TryGetProperty("chunk_id", out var cid) && cid.ValueKind == JsonValueKind.Number &&
                    item.TryGetProperty("zitat", out var z) && z.GetString() is string zitatText)
                {
                    quellen.Add((zitatText, cid.GetInt32()));
                }
            }
        }

        return (content, suggestedAction, confidence, quellen);
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

    private void AppendAiMessage(string text, double? confidence = null,
        IReadOnlyList<(string Zitat, int ChunkId)>? quellen = null)
    {
        Dispatcher.Invoke(() =>
        {
            if (_webViewInitialized)
            {
                AddHtmlMessage(ChatHtmlRenderer.WrapAiBubble(ChatHtmlRenderer.Render(text), confidence, quellen));
                RefreshChatWebView();
                ScrollToBottom();
                return;
            }

            var bubble = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Background          = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                CornerRadius        = new CornerRadius(4, 16, 16, 16),
                Padding             = new Thickness(14, 10, 14, 10),
                Margin              = new Thickness(8, 4, 60, 4),
                MaxWidth            = 500,
            };
            bubble.Child = CreateSelectableTextBox(
                text,
                new SolidColorBrush(Color.FromRgb(201, 209, 217)));
            ChatPanel.Children.Add(bubble);
            ScrollToBottom();
        });
    }

    /// <summary>Beschriftung des Öffnen-Buttons mit dem Programm, das Windows gerade
    /// dafür eingerichtet hat (zur Laufzeit ermittelt).</summary>
    private static string OeffnenLabel(bool istEmail) => istEmail
        ? (StandardProgramme.Fuer("mailto") ?? StandardProgramme.Fuer(".eml")) is string programm
            ? $"✉️ In {programm} öffnen"
            : "✉️ E-Mail-Text kopieren"
        : $"📄 In {StandardProgramme.Fuer(".docx") ?? StandardProgramme.Fuer(".txt") ?? "Editor"} öffnen";

    private static Button CreateAktionsButton(string label, Action onClick)
    {
        var btn = new Button
        {
            Content             = label,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background          = new SolidColorBrush(Color.FromRgb(31, 111, 235)),
            Foreground          = Brushes.White,
            BorderThickness     = new Thickness(0),
            Padding             = new Thickness(12, 6, 12, 6),
            Margin              = new Thickness(0, 6, 0, 0),
            FontSize            = 12,
            Cursor              = Cursors.Hand,
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    /// <summary>Klicks auf Buttons in der Chat-WebView (siehe Skript in
    /// GetChatBaseHtml) — ohne diesen Handler taten die HTML-Buttons nichts.</summary>
    private void OnChatWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var action = root.TryGetProperty("action", out var a) ? a.GetString() : null;
            var value = root.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";

            var id = root.TryGetProperty("msg", out var m) && int.TryParse(m.GetString(), out var i) ? i : -1;

            // Task #297 (ersetzt die ältere Task #80-Variante ZeigeQuellenPopupAsync):
            // "quelle" ist keine Entwurfskarten-Aktion (id ist hier die chunk_id aus
            // quellen_check/T#296, nicht ein Karten-Index) -> vor dem _karten-Lookup abfangen.
            if (action == "quelle")
            {
                _ = QuelleOeffnenAsync(id, value);
                return;
            }

            if (!_karten.TryGetValue(id, out var karte)) return;

            switch (action)
            {
                case "oeffnen": KarteOeffnen(karte); break;
                case "uebernehmen": KarteInAkteAblegen(karte); break;
                case "kopieren": KarteKopieren(karte); break;
                case "art": KarteArtWechseln(karte, value); break;
                case "zuordnen": KarteZuordnen(karte, value); break;
                case "vorlage": KarteAlsVorlageSpeichern(karte); break;
            }
        }
        catch (Exception ex)
        {
            AppendSystemMessage($"Aktion fehlgeschlagen: {ex.Message}");
        }
    }

    /// <summary>Task #297 (ersetzt die ältere Task #80-Variante ZeigeQuellenPopupAsync):
    /// Klick auf einen Quellen-Link (siehe ChatHtmlRenderer.
    /// WrapAiBubble/QuellenListeHtml) lädt den Chunk per chunk_id (T#296) vom Backend
    /// und zeigt ihn im QuelleDialog — der dort direkt zur exakten Passage springt
    /// (ChatHtmlRenderer.FindeZitatPosition), statt nur den Dokumentanfang zu zeigen.
    ///
    /// T#300: Inline-Zitate aus ApplySemanticHighlighting (z.B. "§ 573 Abs. 2 Nr. 2 BGB"
    /// mitten im Fließtext oder in der Zitatprüfung) tragen kein data-msg und damit keine
    /// chunk_id — der Klick-Handler in GetChatBaseHtml liefert dafür msg=null, was hier als
    /// chunkId &lt;= 0 ankommt. Direktes Laden von /legal-db/chunks/{chunkId} schlug dann
    /// immer mit HTTP 404 fehl, obwohl die Norm indexiert war. Fallback: die Referenz selbst
    /// über /legal-db/chunks/by-reference auflösen (QuellenangabeService, bisher ungenutzt).</summary>
    private async Task QuelleOeffnenAsync(int chunkId, string zitat)
    {
        try
        {
            var chunk = chunkId > 0
                ? await _quellenangabeService.LadeChunkAsync(chunkId)
                : await _quellenangabeService.LadeChunkFuerReferenzAsync(zitat);

            if (chunk is null)
            {
                AppendSystemMessage("Quelle konnte nicht geladen werden.");
                return;
            }

            var title = string.IsNullOrWhiteSpace(chunk.Title) ? zitat : chunk.Title;

            Dispatcher.Invoke(() =>
            {
                var dialog = new QuelleDialog(title, chunk.Text, zitat) { Owner = this };
                dialog.Show();
            });
        }
        catch (Exception ex)
        {
            AppendSystemMessage($"Quelle konnte nicht geladen werden: {ex.Message}");
        }
    }

    // ── Entwurfskarten ──────────────────────────────────────────────────────

    /// <summary>Zeigt einen Entwurf als Karte im Chat. Bei geöffnetem Mandanten liegt
    /// er sofort in &lt;Mandant&gt;\Entwürfe; eine Überarbeitung (ueberarbeitung="ja"
    /// im Block) ersetzt die vorige Fassung, statt eine weitere Datei anzulegen —
    /// ein neues, anderes Schreiben überschreibt dagegen nie einen vorhandenen
    /// Entwurf. Ohne Mandant fragt die Karte, wohin.</summary>
    private void EntwurfKarteAnlegen(ChatEntwurf daten, bool historie = false, bool mitRefresh = true,
        double? confidence = null, IReadOnlyList<(string Zitat, int ChunkId)>? quellen = null)
    {
        var karte = new EntwurfKarte
        {
            Id = _karten.Count, Daten = daten, Mandant = _activeMandantName, Historie = historie,
            Confidence = confidence, Quellen = quellen,
        };
        EntwurfKarte? vorgaenger = null;
        if (!historie && daten.Ueberarbeitung)
        {
            vorgaenger = _karten.Values.LastOrDefault(k =>
                !k.Historie && !k.Ersetzt && k.Datei is not { Uebernommen: true } &&
                string.Equals(k.Mandant, karte.Mandant, StringComparison.OrdinalIgnoreCase));
        }
        _karten[karte.Id] = karte;

        if (!historie && karte.Mandant is not null)
            KarteSpeichern(karte, vorgaenger?.Datei);
        if (vorgaenger is not null)
        {
            vorgaenger.Ersetzt = true;
            KarteNeuZeichnen(vorgaenger);
        }
        KarteZeigen(karte, mitRefresh);
    }

    /// <summary>Schreibt den Entwurf nach &lt;Mandant&gt;\Entwürfe. vorige = Datei der
    /// ersetzten Fassung: wird aktualisiert, außer sie wurde zwischendurch bearbeitet
    /// (bleibt erhalten) oder ist gerade geöffnet.</summary>
    private void KarteSpeichern(EntwurfKarte karte, Entwurf? vorige)
    {
        var ordner = EntwuerfeOrdner(karte.Mandant);
        if (ordner is null)
        {
            karte.Hinweis = "Kein Mandantenordner gefunden — bitte das Dokumente-Verzeichnis in den Einstellungen prüfen.";
            return;
        }
        try
        {
            var d = karte.Daten;
            var endung = d.IstEmail ? ".eml" : ".docx";
            karte.Hinweis = null;
            Entwurf? datei = null;
            if (vorige is { Uebernommen: false, Lose: false } && File.Exists(vorige.Pfad))
            {
                if (!vorige.Unveraendert)
                    karte.Hinweis = "Neue Datei, weil die vorige Fassung bearbeitet wurde — sie bleibt unverändert.";
                else if (!vorige.Pfad.EndsWith(endung, StringComparison.OrdinalIgnoreCase))
                    DateiEntfernen(vorige); // Art gewechselt: alte Fassung im anderen Format weg
                else
                {
                    try
                    {
                        SchreibeEntwurf(d, vorige.Pfad);
                        datei = vorige;
                        datei.Fassung++;
                    }
                    catch (IOException)
                    {
                        karte.Hinweis = "Neue Datei, weil die vorige Fassung gerade geöffnet ist.";
                    }
                }
            }

            if (datei is null)
            {
                var name = d.IstEmail
                    ? $"{DokumentExport.EmlEntwurfPraefix}{DateTime.Now:yyyy-MM-dd_HHmmss}.eml"
                    : DokumentExport.DateinameFuerSchreiben(karte.Mandant, DateTime.Now);
                datei = new Entwurf { Pfad = DokumentExport.FreierPfad(ordner, name), Fassung = (vorige?.Fassung ?? 0) + 1 };
                SchreibeEntwurf(d, datei.Pfad);
            }

            datei.GeschriebenUtc = File.GetLastWriteTimeUtc(datei.Pfad);
            karte.Datei = datei;
            BuildFileTree(_activeMandantName);
        }
        catch (Exception ex)
        {
            karte.Hinweis = $"Entwurf konnte nicht gespeichert werden: {ex.Message}";
        }
    }

    private static void SchreibeEntwurf(ChatEntwurf d, string pfad)
    {
        if (d.IstEmail) DokumentExport.SchreibeEmlNach(d.AlsEmail(), pfad);
        else DokumentExport.SchreibeDocx(pfad, d.Text);
    }

    /// <summary>Löscht eine von LexWolf geschriebene, unbearbeitete Entwurfsdatei.</summary>
    private void DateiEntfernen(Entwurf e)
    {
        if (e.Uebernommen || !e.Unveraendert) return;
        try
        {
            File.Delete(e.Pfad);
            _db.DeleteDokumentChunks(e.Pfad);
        }
        catch (IOException) { /* gerade geöffnet — bleibt liegen */ }
    }

    private EntwurfKarteAnsicht KarteAnsicht(EntwurfKarte k)
    {
        var teile = new List<string>();
        if (k.Historie)
            teile.Add("Aus einer früheren Sitzung — die Datei liegt im Ordner „Entwürfe“ bzw. in der Akte des Mandanten.");
        else if (k.Ersetzt)
            teile.Add("Durch eine neuere Fassung ersetzt.");
        else if (k.Datei is { Uebernommen: true } inAkte)
            teile.Add($"📁 In der Akte: {AktenPfad(inAkte.Pfad)}");
        else if (k.Datei is { Lose: false } entwurf)
            teile.Add($"💾 {(entwurf.Fassung > 1 ? $"Fassung {entwurf.Fassung} · " : "")}liegt in {AktenPfad(entwurf.Pfad)}");
        else if (k.Mandant is null)
            teile.Add(k.Datei is { Lose: true } lose
                ? $"Noch keinem Mandanten zugeordnet — geöffnete Fassung: {lose.Pfad}"
                : "Noch keinem Mandanten zugeordnet.");
        if (k.Hinweis is not null && !k.Historie && !k.Ersetzt) teile.Add(k.Hinweis);

        var bearbeitbar = !k.Historie && !k.Ersetzt;
        return new EntwurfKarteAnsicht(
            k.Id,
            k.Daten,
            string.Join(" · ", teile),
            Aktiv: !k.Ersetzt,
            ArtUmschaltbar: bearbeitbar && k.Datei is not { Uebernommen: true },
            AblegenMoeglich: bearbeitbar && k.Datei is { Uebernommen: false, Lose: false },
            OeffnenLabel: k.Historie ? null : OeffnenLabel(k.Daten.IstEmail),
            MandantAuswahl: bearbeitbar && k.Mandant is null
                ? _mandanten.Select(m => m.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList()
                : null,
            Confidence: k.Confidence,
            Quellen: k.Quellen);
    }

    private void KarteZeigen(EntwurfKarte k, bool mitRefresh)
    {
        var ansicht = KarteAnsicht(k);
        if (!_webViewInitialized)
        {
            // Ohne WebView2: Text, Status und Buttons als WPF-Elemente.
            AppendAiMessage(DokumentExport.OhneMarkdown(k.Daten.Text));
            if (ansicht.Status != "") AppendSystemMessage(ansicht.Status);
            if (ansicht.OeffnenLabel is not null)
                ChatPanel.Children.Add(CreateAktionsButton(ansicht.OeffnenLabel, () => KarteOeffnen(k)));
            if (ansicht.AblegenMoeglich)
                ChatPanel.Children.Add(CreateAktionsButton("📁 In Akte ablegen", () => KarteInAkteAblegen(k)));
            if (ansicht.Aktiv)
                ChatPanel.Children.Add(CreateAktionsButton("💾 Als Vorlage speichern", () => KarteAlsVorlageSpeichern(k)));
            ScrollToBottom();
            return;
        }

        k.Html = ChatHtmlRenderer.WrapEntwurfKarte(ansicht);
        k.HtmlIndex = _chatHistoryHtml.Count;
        AddHtmlMessage(k.Html);
        if (mitRefresh)
        {
            RefreshChatWebView();
            ScrollToBottom();
        }
    }

    /// <summary>Aktualisiert nur diese Karte in der WebView — ohne die Seite neu zu
    /// laden, sonst spränge die Ansicht nach jedem Klick ans Chat-Ende.</summary>
    private void KarteNeuZeichnen(EntwurfKarte k)
    {
        if (!_webViewInitialized) return;
        // Nach einem Mandantenwechsel ist der Chat neu aufgebaut, die Karte steht dort nicht mehr.
        if (k.HtmlIndex < 0 || k.HtmlIndex >= _chatHistoryHtml.Count ||
            !ReferenceEquals(_chatHistoryHtml[k.HtmlIndex], k.Html)) return;
        var html = ChatHtmlRenderer.WrapEntwurfKarte(KarteAnsicht(k));
        _chatHistoryHtml[k.HtmlIndex] = k.Html = html;
        _ = ChatWebView.ExecuteScriptAsync(
            $"(function(){{var e=document.getElementById('ek-{k.Id}');if(e)e.outerHTML={JsonSerializer.Serialize(html)};}})();");
    }

    private void KarteOeffnen(EntwurfKarte k)
    {
        try
        {
            var vorhanden = k.Datei is { } d && File.Exists(d.Pfad) ? d.Pfad : null;
            if (k.Daten.IstEmail)
            {
                EmailOeffnen(k.Daten.AlsEmail(), vorhanden?.EndsWith(".eml", StringComparison.OrdinalIgnoreCase) == true ? vorhanden : null);
                return;
            }

            var pfad = vorhanden?.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) == true ? vorhanden : null;
            if (pfad is null)
            {
                // Ohne Mandantenordner: unter Dokumente\LexWolf\Schreiben, bis die Karte
                // zugeordnet wird (dann wandert genau diese Datei in die Entwürfe).
                // Bewusst KEIN Unterordner direkt in DokumentePfad — den würde der
                // DokumentScanner als weiteren Mandanten anlegen.
                var ordner = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LexWolf", "Schreiben");
                Directory.CreateDirectory(ordner);
                pfad = DokumentExport.FreierPfad(ordner, DokumentExport.DateinameFuerSchreiben(k.Mandant, DateTime.Now));
                DokumentExport.SchreibeDocx(pfad, k.Daten.Text);
                k.Datei = new Entwurf { Pfad = pfad, GeschriebenUtc = File.GetLastWriteTimeUtc(pfad), Lose = true };
                KarteNeuZeichnen(k);
            }
            WordOeffnen(pfad, k.Daten.Text);
        }
        catch (Exception ex)
        {
            AppendSystemMessage($"Entwurf konnte nicht geöffnet werden: {ex.Message}");
        }
    }

    /// <summary>Ordnet eine Karte ohne Mandant zu und legt den Entwurf in dessen
    /// Entwürfe-Ordner. Wurde er schon geöffnet (evtl. bearbeitet), wandert genau
    /// diese Datei mit, statt neu geschrieben zu werden.</summary>
    private void KarteZuordnen(EntwurfKarte k, string mandant)
    {
        if (string.IsNullOrWhiteSpace(mandant))
        {
            k.Hinweis = "Bitte zuerst einen Mandanten wählen.";
            KarteNeuZeichnen(k);
            return;
        }
        var ordner = EntwuerfeOrdner(mandant);
        if (ordner is null)
        {
            k.Hinweis = $"Kein Ordner für „{mandant}“ im Dokumente-Verzeichnis gefunden.";
            KarteNeuZeichnen(k);
            return;
        }

        k.Mandant = mandant;
        k.Hinweis = null;
        if (k.Datei is { Lose: true } lose && File.Exists(lose.Pfad))
        {
            try
            {
                // Move behält den Zeitstempel -> eine bearbeitete Datei gilt weiter als bearbeitet.
                var ziel = DokumentExport.FreierPfad(ordner, Path.GetFileName(lose.Pfad));
                File.Move(lose.Pfad, ziel);
                lose.Pfad = ziel;
                lose.Lose = false;
                BuildFileTree(_activeMandantName);
            }
            catch (IOException)
            {
                k.Mandant = null;
                k.Hinweis = "Die geöffnete Fassung ist noch in Word offen — bitte schließen und erneut ablegen.";
            }
        }
        else
        {
            KarteSpeichern(k, null);
        }
        KarteNeuZeichnen(k);
    }

    /// <summary>Brief ↔ E-Mail: schreibt den Entwurf im anderen Format neu; die
    /// unbearbeitete alte Datei verschwindet, eine bearbeitete bleibt erhalten.</summary>
    private void KarteArtWechseln(EntwurfKarte k, string art)
    {
        var neu = art == "email" ? "email" : "brief";
        if (k.Daten.Art == neu || k.Historie || k.Ersetzt || k.Datei is { Uebernommen: true }) return;
        k.Daten = k.Daten with
        {
            Art = neu,
            Betreff = neu == "email" && k.Daten.Betreff == "" ? k.Daten.Titel : k.Daten.Betreff,
        };

        string? hinweis = null;
        if (k.Datei is { } alt)
        {
            if (alt.Unveraendert) DateiEntfernen(alt);
            else if (File.Exists(alt.Pfad)) hinweis = $"Die bearbeitete Fassung bleibt erhalten: {AktenPfad(alt.Pfad)}";
            k.Datei = null;
        }
        if (k.Mandant is not null) KarteSpeichern(k, null);
        if (hinweis is not null) k.Hinweis ??= hinweis;
        BuildFileTree(_activeMandantName);
        KarteNeuZeichnen(k);
    }

    /// <summary>Verschiebt den Entwurf aus &lt;Mandant&gt;\Entwürfe in den
    /// Mandantenordner (offizielle Akte); ein leerer Entwürfe-Ordner wird entfernt.</summary>
    private void KarteInAkteAblegen(EntwurfKarte k)
    {
        var entwurf = k.Datei;
        if (entwurf is null || entwurf.Lose || entwurf.Uebernommen) return;
        try
        {
            var entwuerfeOrdner = Path.GetDirectoryName(entwurf.Pfad)!;
            if (!File.Exists(entwurf.Pfad) || !string.Equals(Path.GetFileName(entwuerfeOrdner), EntwuerfeOrdnerName, StringComparison.OrdinalIgnoreCase))
            {
                k.Hinweis = "Der Entwurf liegt nicht mehr im Entwürfe-Ordner der Akte.";
                KarteNeuZeichnen(k);
                return;
            }

            var mandantOrdner = Path.GetDirectoryName(entwuerfeOrdner)!;
            var ziel = DokumentExport.FreierPfad(mandantOrdner, DokumentExport.NameNachUebernahme(Path.GetFileName(entwurf.Pfad)));
            var alt = entwurf.Pfad;
            File.Move(alt, ziel);
            _db.DeleteDokumentChunks(alt); // der Scanner liest die Datei unter dem neuen Pfad neu ein
            entwurf.Pfad = ziel;
            entwurf.Uebernommen = true;
            k.Hinweis = null;
            if (!Directory.EnumerateFileSystemEntries(entwuerfeOrdner).Any())
                Directory.Delete(entwuerfeOrdner);
            BuildFileTree(_activeMandantName);
        }
        catch (IOException)
        {
            k.Hinweis = "Der Entwurf ist noch geöffnet — bitte im Programm schließen und dann erneut ablegen.";
        }
        catch (Exception ex)
        {
            k.Hinweis = $"Entwurf konnte nicht abgelegt werden: {ex.Message}";
        }
        KarteNeuZeichnen(k);
    }

    private void KarteKopieren(EntwurfKarte k)
    {
        var text = k.Daten.IstEmail
            ? DokumentExport.AlsZwischenablageText(k.Daten.AlsEmail())
            : DokumentExport.OhneMarkdown(k.Daten.Text).Replace("\r\n", "\n").Replace("\n", "\r\n");
        Clipboard.SetText(text);
        AddReasoning("📋", "Entwurfstext in die Zwischenablage kopiert.");
    }

    /// <summary>Task #307: aktuellen Entwurfstext als wiederverwendbare Vorlage für einen
    /// Schriftsatz-Typ ablegen (Vorlagen/&lt;Typ&gt;/&lt;Name&gt;.txt) — und sofort als
    /// „zuletzt gewählt“ für diesen Typ setzen (letzte_vorlage_pro_typ, siehe Task #304),
    /// damit der nächste Schriftsatz dieses Typs sie automatisch vorschlägt (Task #305).</summary>
    private void KarteAlsVorlageSpeichern(EntwurfKarte k)
    {
        var vorschlag = VorlagenOrdner.OrdnerName(k.Daten.Titel);
        if (vorschlag.Length == 0) vorschlag = $"Vorlage {DateTime.Now:yyyy-MM-dd}";

        var dlg = new VorlageSpeichernDialog(_settings.DokumentePfad, VorlagenOrdner.Arten[^1], vorschlag) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var pfad = VorlagenOrdner.VorlagenDateiPfad(_settings.DokumentePfad, dlg.GewaehlterTyp, dlg.Name);
            DokumentExport.SchreibeText(pfad, k.Daten.Text);
            _db.UpsertLetzteVorlage(dlg.GewaehlterTyp, pfad);
            AddReasoning("💾", $"Als Vorlage für „{dlg.GewaehlterTyp}“ gespeichert: {Path.GetFileName(pfad)}");
        }
        catch (Exception ex)
        {
            AppendSystemMessage($"Vorlage konnte nicht gespeichert werden: {ex.Message}");
        }
    }

    private string AktenPfad(string pfad) =>
        Directory.Exists(_settings.DokumentePfad) ? Path.GetRelativePath(_settings.DokumentePfad, pfad) : pfad;

    /// <summary>Öffnet das Schreiben mit dem Programm, das Windows zur Laufzeit für
    /// Word-Dokumente meldet (Word, LibreOffice, WordPad, …). Ohne Programm für
    /// .docx wird eine Textfassung im Editor geöffnet.</summary>
    private void WordOeffnen(string pfad, string text)
    {
        var programm = StandardProgramme.Fuer(".docx");
        if (programm is not null)
        {
            DokumentExport.OeffneMitStandardprogramm(pfad);
            AddReasoning("📄", $"Schreiben in {programm} geöffnet: {pfad}");
            return;
        }

        // Kein Programm für Word-Dokumente: das .docx bleibt als Entwurf liegen,
        // angezeigt wird eine Textfassung.
        var txtOrdner = Path.Combine(Path.GetTempPath(), "LexWolf");
        Directory.CreateDirectory(txtOrdner);
        var txt = Path.Combine(txtOrdner, Path.ChangeExtension(Path.GetFileName(pfad), ".txt"));
        DokumentExport.SchreibeText(txt, text);
        DokumentExport.OeffneMitStandardprogramm(txt);
        AppendSystemMessage($"📄 Kein Programm für Word-Dokumente eingerichtet — Textfassung in " +
                            $"{StandardProgramme.Fuer(".txt") ?? "Editor"} geöffnet. Word-Datei: {pfad}");
    }

    /// <summary>&lt;DokumentePfad&gt;\&lt;Mandant&gt;\Entwürfe (wird bei Bedarf angelegt),
    /// oder null ohne Mandant bzw. Mandantenordner. Als Unterordner des Mandanten
    /// liest der DokumentScanner ihn mit ein (Entwürfe stehen der KI als Kontext zur
    /// Verfügung), ohne ihn als eigenen Mandanten anzulegen.</summary>
    private const string EntwuerfeOrdnerName = "Entwürfe";

    private string? EntwuerfeOrdner(string? mandant)
    {
        if (mandant is null || !Directory.Exists(_settings.DokumentePfad)) return null;
        var mandantOrdner = Path.Combine(_settings.DokumentePfad, mandant);
        if (!Directory.Exists(mandantOrdner)) return null;
        var ordner = Path.Combine(mandantOrdner, EntwuerfeOrdnerName);
        Directory.CreateDirectory(ordner);
        return ordner;
    }

    /// <summary>Öffnet den E-Mail-Entwurf in dem Programm, das Windows zur Laufzeit für
    /// mailto: bzw. .eml meldet. emlPfad = bereits abgelegter .eml-Entwurf.</summary>
    private void EmailOeffnen(EmailEntwurf entwurf, string? emlPfad)
    {
        var mailtoProgramm = StandardProgramme.Fuer("mailto");
        var emlProgramm = StandardProgramme.Fuer(".eml");
        var empfaengerHinweis = DokumentExport.EmailAdressen(entwurf.An).Count == 0
            ? " Bitte die Empfänger-Adresse ergänzen."
            : "";

        switch (DokumentExport.WaehleEmailWeg(entwurf, mailtoProgramm is not null, emlProgramm is not null))
        {
            case EmailWeg.Mailto:
                DokumentExport.OeffneMitStandardprogramm(DokumentExport.BaueMailtoUri(entwurf));
                AppendSystemMessage($"✉️ E-Mail-Entwurf in {mailtoProgramm} geöffnet.{empfaengerHinweis}");
                break;
            case EmailWeg.MailtoTextInZwischenablage:
                Clipboard.SetText(entwurf.Text.Replace("\r\n", "\n").Replace("\n", "\r\n"));
                DokumentExport.OeffneMitStandardprogramm(DokumentExport.BaueMailtoUri(entwurf, mitText: false));
                AppendSystemMessage($"✉️ E-Mail-Entwurf in {mailtoProgramm} geöffnet. Der Text ist für die direkte Übergabe zu lang " +
                                    $"und liegt in der Zwischenablage — im Entwurf mit Strg+V einfügen.{empfaengerHinweis}");
                break;
            case EmailWeg.Eml:
                var eml = emlPfad ?? DokumentExport.SchreibeEml(entwurf, Path.Combine(Path.GetTempPath(), "LexWolf"));
                DokumentExport.OeffneMitStandardprogramm(eml);
                AppendSystemMessage($"✉️ E-Mail-Entwurf in {emlProgramm} geöffnet.{empfaengerHinweis}");
                break;
            default:
                Clipboard.SetText(DokumentExport.AlsZwischenablageText(entwurf));
                AppendSystemMessage("✉️ Auf diesem Rechner ist kein E-Mail-Programm eingerichtet. Betreff und Text " +
                                    "liegen in der Zwischenablage, z.B. zum Einfügen im Webmail.");
                break;
        }
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

    // Task #304/#305: Da der allgemeine Chat hier keinen konkreten Schriftsatz-Typ kennt,
    // wird der Typ aus der über alle Typen hinweg zuletzt bewusst gewählten Vorlage
    // übernommen (GetLetzteVorlageGlobal). Existiert deren Datei nicht mehr (verschoben/
    // gelöscht), greift die Lade-Fallback-Kette aus VorlagenOrdner.FindeVorlagenPfad:
    // erste Datei im Typ-Unterordner -> erste Datei im Vorlagen-Wurzelordner -> kein Kontext.
    // Wurde noch nie bewusst eine Vorlage gewählt (kein Typ bekannt), geht es direkt auf
    // Stufe 3 (Vorlagen-Wurzelordner).
    private string? GetLatestTemplateContext()
    {
        var gewaehlt = _db.GetLetzteVorlageGlobal();
        var vorlagenOrdner = Path.Combine(_settings.DokumentePfad, VorlagenOrdner.Ordnername);
        var pfad = gewaehlt is null
            ? VorlagenOrdner.ErsteDateiAlphabetisch(vorlagenOrdner)
            : VorlagenOrdner.FindeVorlagenPfad(_settings.DokumentePfad, gewaehlt.Value.SchriftsatzTyp, gewaehlt.Value.VorlagePfad);
        if (pfad is null) return null;

        var content = File.ReadAllText(pfad, Encoding.UTF8);
        return string.IsNullOrWhiteSpace(content) ? null : $"Aktuelle Vorlage auf dem Rechner:\n{content}";
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
}
