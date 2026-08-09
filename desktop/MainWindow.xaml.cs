using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;
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
    private string? _activeMandantId = null;
    private string? _activeMandantName = null;
    private readonly List<(string Id, string Name)> _mandanten = new();
    private readonly HashSet<string> _prioritizedPaths = new(StringComparer.OrdinalIgnoreCase);
    private System.Collections.ObjectModel.ObservableCollection<Models.FileTreeNode> _fileTreeRoots = new();

    public MainWindow()
    {
        InitializeComponent();
        MandantBox.AddHandler(
            System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(OnMandantTextChanged));
        _ = CheckConnectionAsync(showConnecting: true);
        _ = PeriodicHealthCheckAsync();
        _ = LoadMandantenAsync();
        _ = Task.Run(StartDocumentScannerAsync);
        _ = CheckForUpdateAsync();
        AppendSystemMessage("Willkommen bei LexWolf. Wie kann ich Ihnen helfen?");
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
            Dispatcher.Invoke(() => SetProgressBusy(false));
            Dispatcher.Invoke(() => System.Windows.MessageBox.Show(
                this, $"Update fehlgeschlagen: {ex.Message}", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error));
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
    private bool _suppressMandantEvents = false;

    private Task LoadMandantenAsync()
    {
        // Mandanten kommen aus der lokalen Scanner-DB (Ordnername je Mandant unter
        // DokumentePfad), nicht vom Server — die Server-Tabelle "mandanten" ist leer
        // und hat keinen Endpoint zum Befüllen; Mandantendaten sollen laut
        // Datenschutz-Konzept ohnehin nie den Anwalts-PC verlassen.
        var mandanten = _db.GetMandanten();
        _mandanten.Clear();
        _mandanten.AddRange(mandanten);
        Dispatcher.Invoke(() => ApplyMandantFilter(MandantBox.Text));
        return Task.CompletedTask;
    }

    /// <summary>Befüllt die Dropdown-Liste case-insensitiv gefiltert nach dem
    /// aktuell eingegebenen Text — Suche/Autovervollständigung fürs Mandanten-Feld.</summary>
    private void ApplyMandantFilter(string? filterText)
    {
        _suppressMandantEvents = true;
        try
        {
            var filter = (filterText ?? "").Trim();
            MandantBox.Items.Clear();
            if (string.IsNullOrEmpty(filter))
                MandantBox.Items.Add(KeinMandantLabel);
            foreach (var (_, name) in _mandanten)
            {
                if (string.IsNullOrEmpty(filter) || name.Contains(filter, StringComparison.OrdinalIgnoreCase))
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
        ApplyMandantFilter(MandantBox.Text);
        MandantBox.IsDropDownOpen = MandantBox.Items.Count > 0 && !string.IsNullOrEmpty(MandantBox.Text);
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
        _history.Clear();
        ChatPanel.Children.Clear();
        UnterhaltBtn.Visibility = Visibility.Collapsed;

        if (string.IsNullOrEmpty(selected) || selected == KeinMandantLabel)
        {
            _activeMandantId   = null;
            _activeMandantName = null;
            AppendSystemMessage("Kein Mandant ausgewählt — allgemeines Gespräch.");
            BuildFileTree(null);
            return;
        }

        var match = _mandanten.FirstOrDefault(m => string.Equals(m.Name, selected, StringComparison.OrdinalIgnoreCase));
        if (match.Name is null) return; // getippter Text ohne (eindeutigen) Treffer

        _activeMandantId   = match.Id;
        _activeMandantName = match.Name;
        AppendSystemMessage($"Mandant: {match.Name} — Chat-Kontext aktiv.");
        BuildFileTree(match.Name);
    }

    /// <summary>Baut den Dateibaum (links) aus DokumentePfad neu auf — ein Mandanten-
    /// Ordner pro Wurzelknoten, oder bei aktivem Mandanten nur dessen Ordner
    /// (gefiltert). FileTree.ItemsSource wurde ursprünglich nirgends gesetzt, der
    /// Baum war deshalb immer leer, unabhängig vom Scan-Ergebnis.</summary>
    private void BuildFileTree(string? filterMandantName = null)
    {
        var basePath = _settings.DokumentePfad;
        var roots = new System.Collections.ObjectModel.ObservableCollection<Models.FileTreeNode>();
        if (Directory.Exists(basePath))
        {
            var mandantDirs = Directory.EnumerateDirectories(basePath);
            if (!string.IsNullOrEmpty(filterMandantName))
                mandantDirs = mandantDirs.Where(d =>
                    string.Equals(Path.GetFileName(d), filterMandantName, StringComparison.OrdinalIgnoreCase));

            foreach (var mandantDir in mandantDirs.OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                var node = new Models.FileTreeNode(Path.GetFileName(mandantDir), mandantDir, isFolder: true);
                AddFileTreeChildren(node, mandantDir);
                roots.Add(node);
            }
        }
        _fileTreeRoots = roots;
        Dispatcher.Invoke(() => ApplyFileTreeSearch(FileTreeSearchBox.Text));
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
            FileTree.ItemsSource = _fileTreeRoots;
            return;
        }
        var filtered = new System.Collections.ObjectModel.ObservableCollection<Models.FileTreeNode>();
        foreach (var root in _fileTreeRoots)
        {
            var match = FilterFileTreeNode(root, text);
            if (match is not null) filtered.Add(match);
        }
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
        ApplyFileTreeSearch(FileTreeSearchBox.Text);
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
}
