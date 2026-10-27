using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LexWolf.Database;
using LexWolf.Models;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>Schriftsatz aus den Dokumenten der Akte entwerfen (Backend /akte/schriftsatz),
/// mit Fundstellen [Dn] und geprüften Zitaten.</summary>
public partial class SchriftsatzDialog : Window
{
    public sealed class DokumentAuswahl
    {
        public required string Titel { get; init; }
        public required string Text { get; init; }
        public required string Pfad { get; init; }
        public bool Ausgewaehlt { get; set; } = true;
    }

    private sealed record Beleg(string Zitat, string Status, string Hinweis = "")
    {
        public string Symbol => Status switch { "geprüft" => "✓", "Dokument existiert nicht" => "✗", _ => "?" };
        public string StatusText => Hinweis.Length > 0 ? Status switch
        {
            "geprüft" => $"geprüft ({Hinweis.Replace("gefunden: ", "")})",
            _ => "nicht in der Datenbank (nur neuere Entscheidungen der Bundesgerichte) — bitte prüfen",
        } : Status switch
        {
            "geprüft" => "geprüft",
            // Die Datenbank ist nicht vollständig — "nicht gefunden" heißt "prüfen", nicht "falsch".
            "nicht gefunden" => "nicht in der Datenbank gefunden — bitte prüfen",
            "nicht in Datenbank" => "Gesetz nicht in der Datenbank — selbst prüfen",
            "Dokument existiert nicht" => "Dieses Dokument gibt es in der Akte nicht",
            _ => Status,
        };
        public Brush Farbe => Status switch
        {
            "geprüft" => new SolidColorBrush(Color.FromRgb(0x3f, 0xb9, 0x50)),
            "Dokument existiert nicht" => new SolidColorBrush(Color.FromRgb(0xf8, 0x51, 0x49)),
            _ => new SolidColorBrush(Color.FromRgb(0xd2, 0x99, 0x22)),
        };
    }

    // Einzige Quelle für die Liste: VorlagenOrdner.Arten (auch für die Vorlagen-Unterordner je Typ).
    private static readonly string[] Arten = VorlagenOrdner.Arten;

    private readonly LocalDb _db;
    private readonly string _backendUrl;
    private readonly string _mandantId;
    private readonly string _mandantName;
    private readonly string _dokumentePfad;
    private readonly List<DokumentAuswahl> _dokumente;
    private CancellationTokenSource? _cts;

    private readonly AppSettings _settings;

    // T#310: eigener HttpClient ohne Timeout für die Rechtsrecherche vor dem Entwurf —
    // der Tief-Modus durchläuft mehrere Runden und kann länger dauern als ein normaler
    // Request (gleicher Grund wie ReActDialog._streamHttp / MainWindow._reactHttp).
    private readonly HttpClient _reactHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly ReActStreamClient _reactClient;

    public SchriftsatzDialog(LocalDb db, string backendUrl, string mandantId, string mandantName, AppSettings settings)
    {
        InitializeComponent();
        FensterGroesse.AnSchriftAnpassen(this);
        _db = db;
        _backendUrl = backendUrl;
        _mandantId = mandantId;
        _mandantName = mandantName;
        _settings = settings;
        _dokumentePfad = settings.DokumentePfad;
        _reactClient = new ReActStreamClient(_reactHttp, backendUrl);
        UntertitelText.Text = mandantName;
        ArtBox.ItemsSource = Arten;
        ArtBox.SelectedIndex = 0;
        ZeigeVorlage();

        _dokumente = db.GetDokumenteForMandant(mandantId, maxDokumente: 60)
            .Where(d => d.Text.Length > 0 && !Path.GetFileName(d.Pfad).StartsWith("LexWolf_", StringComparison.OrdinalIgnoreCase))
            .Select(d => new DokumentAuswahl { Titel = d.Titel, Text = d.Text, Pfad = d.Pfad })
            .ToList();
        DokumenteListe.ItemsSource = _dokumente;
        DokumenteLeer.Visibility = _dokumente.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DokumenteZaehlen();
        StatusText.Text = "Der Entwurf dauert je nach Umfang 1–3 Minuten.";
        Closed += (_, _) => { _cts?.Cancel(); _reactHttp.Dispose(); };
    }

    private void DokumenteZaehlen() =>
        DokumenteZahl.Text = $"{_dokumente.Count(d => d.Ausgewaehlt)} von {_dokumente.Count}";

    private void OnDokumentGeaendert(object sender, RoutedEventArgs e) => DokumenteZaehlen();

    /// <summary>Öffnet das Dokument mit dem Standardprogramm — zur Prüfung, bevor der Anwalt
    /// entscheidet, ob es in den Entwurf einbezogen werden soll.</summary>
    private void OnDokumentOeffnen(object sender, RoutedEventArgs e)
    {
        if (((System.Windows.Controls.Button)sender).DataContext is not DokumentAuswahl d) return;
        try { DokumentExport.OeffneMitStandardprogramm(d.Pfad); }
        catch (Exception ex) { StatusText.Text = $"Konnte Dokument nicht öffnen: {ex.Message}"; }
    }

    private void OnEntwurfGeaendert(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        LeerHinweis.Visibility = EntwurfBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnAlleUmschalten(object sender, RoutedEventArgs e)
    {
        var alle = !_dokumente.All(d => d.Ausgewaehlt);
        foreach (var d in _dokumente) d.Ausgewaehlt = alle;
        DokumenteListe.ItemsSource = null;
        DokumenteListe.ItemsSource = _dokumente;
        DokumenteZaehlen();
    }

    private async void OnErstellen(object sender, RoutedEventArgs e)
    {
        var auswahl = _dokumente.Where(d => d.Ausgewaehlt).ToList();
        var stamm = _db.GetAktenStamm(_mandantId);
        var stammText = string.Join("; ", new[]
        {
            stamm.Gericht.Length > 0 ? $"Gericht: {stamm.Gericht}" : "",
            stamm.Aktenzeichen.Length > 0 ? $"Az.: {stamm.Aktenzeichen}" : "",
            stamm.Gegner.Length > 0 ? $"Gegner: {stamm.Gegner}" : "",
            stamm.Gegnervertreter.Length > 0 ? $"Gegnervertreter: {stamm.Gegnervertreter}" : "",
        }.Where(s => s.Length > 0));
        var art = (string)ArtBox.SelectedItem;

        // Datenschutz (Task #308): Anweisung + Akte-Dokumenteninhalte gehen nur anonymisiert
        // an den Server/die KI. Die Zuordnung (Alias -> Original) gilt nur für diese eine
        // Anfrage und wird genutzt, um den Entwurf unten wieder zurückzuübersetzen, bevor
        // der Anwalt ihn sieht.
        var aliasToOriginal = new Dictionary<string, string>();
        var anweisungAnonym = AnonymizerEngine.AnonymizeText(AnweisungBox.Text.Trim(), aliasToOriginal);

        ErstellenBtn.IsEnabled = false;
        _cts = new CancellationTokenSource(TimeSpan.FromMinutes(8));

        // T#310: vor dem eigentlichen Entwurf Normen/Rechtsprechung zum Streitgegenstand
        // (Art + Anweisung) über die Agentic-RAG-Suche recherchieren — live im Panel
        // oben im mittleren Bereich sichtbar. Wie bei der Chat-Anbindung (T#309) ist das
        // eine Ergänzung vor der Antwort, kein Blocker: schlägt sie fehl, entwirft die KI
        // wie bisher primär aus den Akte-Dokumenten.
        string? rechercheKontext = null;
        var query = RechercheQueryBuilder.Build(art, anweisungAnonym);
        if (query.Length > 0)
        {
            StatusText.Text = "🔎 Rechtsrecherche läuft (Normen/Rechtsprechung zum Streitgegenstand) …";
            try
            {
                rechercheKontext = await RunLiveSearchAsync(query, SelectedSuchModus, _cts.Token);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RunLiveSearchAsync] {ex.Message}");
            }
        }

        // Task #311: vor dem Serialisieren materialisieren (statt lazy in der Payload),
        // damit genau dieser anonymisierte Text auch im Live-Panel angezeigt werden kann —
        // dieselbe Zuordnung (aliasToOriginal) wie bei anweisungAnonym.
        var dokumenteAnonym = auswahl
            .Select(d => (Titel: d.Titel, Text: AnonymizerEngine.AnonymizeText(d.Text, aliasToOriginal)))
            .ToList();

        // T#312: Kanzlei-Stammdaten (Briefkopf/Briefende/Kontodaten aus den Einstellungen) und
        // der Kanzleiort gehen mit, damit der Entwurf Datum/Ort und Briefkopf/-ende selbst
        // befüllt, statt Platzhalter zu lassen — der Anwalt muss das nicht mehr nachtragen.
        var kanzleiTeile = new[]
        {
            _settings.Briefkopf.Length > 0 ? $"Briefkopf: {_settings.Briefkopf}" : "",
            _settings.KanzleiKontodaten.Length > 0 ? $"Kontodaten: {_settings.KanzleiKontodaten}" : "",
            _settings.Briefende.Length > 0 ? $"Briefende: {_settings.Briefende}" : "",
        }.Where(s => s.Length > 0);

        var payload = JsonSerializer.Serialize(new
        {
            art,
            anweisung = anweisungAnonym,
            mandant = _mandantName,
            stammdaten = stammText,
            dokumente = dokumenteAnonym.Select(d => new { titel = d.Titel, text = d.Text }),
            stil_vorlage = StilvorlageText(),
            recherche_kontext = rechercheKontext ?? "",
            kanzlei_stammdaten = string.Join("\n", kanzleiTeile),
            ort = _settings.KanzleiOrt,
        });

        // Task #311: denselben Abschnitt wie im Chat — zeigt im RecherchePanel (demselben
        // Live-Panel wie die Suchschritte), was tatsächlich anonymisiert an den Server geht,
        // nicht die Original-Eingabe aus AnweisungBox/den Akte-Dokumenten.
        ZeigeAnonymisiertGesendet(AnonymisiertGesendetBuilder.BuildFuerSchriftsatz(anweisungAnonym, dokumenteAnonym));

        StatusText.Text = "⏳ LexWolf liest die Akte und entwirft den Schriftsatz …";
        try
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await http.PostAsync($"{_backendUrl}/akte/schriftsatz", content, _cts.Token);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_cts.Token));
            var rohEntwurf = doc.RootElement.GetProperty("text").GetString() ?? "";
            EntwurfBox.Text = AnonymizerEngine.DeAnonymizeText(rohEntwurf, aliasToOriginal);
            var belege = doc.RootElement.TryGetProperty("belege", out var b) && b.ValueKind == JsonValueKind.Array
                ? b.EnumerateArray().Select(x => new Beleg(
                    AnonymizerEngine.DeAnonymizeText(x.GetProperty("zitat").GetString() ?? "", aliasToOriginal),
                    x.GetProperty("status").GetString() ?? "",
                    x.TryGetProperty("hinweis", out var h) ? AnonymizerEngine.DeAnonymizeText(h.GetString() ?? "", aliasToOriginal) : "")).ToList()
                : new List<Beleg>();
            // Fehlerhaftes zuerst, dann zu Prüfendes, Geprüftes zuletzt
            BelegeListe.ItemsSource = belege
                .OrderBy(x => x.Status == "Dokument existiert nicht" ? 0 : x.Status == "geprüft" ? 2 : 1).ToList();
            BelegeLeer.Text = belege.Count == 0 ? "Keine Zitate im Entwurf." : "";
            BelegeLeer.Visibility = belege.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var falsch = belege.Count(x => x.Status == "Dokument existiert nicht");
            var pruefen = belege.Count(x => x.Status != "geprüft" && x.Status != "Dokument existiert nicht");
            GeprueftZahl.Text = (belege.Count - falsch - pruefen).ToString();
            PruefenZahl.Text = pruefen.ToString();
            FalschZahl.Text = falsch.ToString();
            BelegeKacheln.Visibility = belege.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = falsch > 0
                ? $"⚠ Fertig — {falsch} Verweis(e) auf Dokumente, die es nicht gibt. Siehe Belegprüfung rechts."
                : pruefen > 0
                    ? $"✓ Fertig — {pruefen} Zitat(e) bitte prüfen."
                    : "✓ Fertig. Bitte vollständig lesen, bevor er rausgeht.";
            KopierenBtn.IsEnabled = WordBtn.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Abgebrochen.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Entwurf fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            ErstellenBtn.IsEnabled = true;
        }
    }

    // ── Rechtsrecherche vor dem Entwurf (T#310) ─────────────────────────────────

    private string SelectedSuchModus =>
        (SuchModusBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "schnell";

    /// <summary>
    /// Ruft GET /api/search/stream (ReActStreamClient, bisher nur im separaten ReActDialog
    /// bzw. im Chat über T#309 genutzt) für den aus Art+Anweisung gebildeten Streitgegenstand
    /// auf und zeigt die Live-Suchschritte im RecherchePanel, während der Entwurf entsteht.
    /// Gibt den aus den gefundenen Quellen gebauten Kontext zurück (oder null), der anschließend
    /// als recherche_kontext an /akte/schriftsatz geht, damit der Entwurf nachweislich aus der
    /// echten Recherche zitiert statt nur aus dem Trainingswissen der KI.
    /// </summary>
    private async Task<string?> RunLiveSearchAsync(string query, string modus, CancellationToken ct)
    {
        var events = new List<ReActStepEvent>();

        RechercheListe.Items.Clear();
        RechercheStatusText.Text = $"🔎 Recherche läuft … (Modus: {modus})";
        RecherchePanel.Visibility = Visibility.Visible;

        try
        {
            await foreach (var evt in _reactClient.StreamAsync(query, modus, ct).ConfigureAwait(true))
            {
                // antwort_chunk ist die vom ReAct-Engine selbst generierte Freitextantwort —
                // nicht relevant hier, der Entwurf entsteht separat über /akte/schriftsatz
                // mit dem aus den Suchergebnissen gebauten Kontext.
                if (evt.Step == "antwort_chunk")
                    continue;

                events.Add(evt);
                RechercheListe.Items.Add(BuildRechercheRow(evt));
                RechercheScroll.ScrollToEnd();
            }
        }
        catch (ReActStreamNotReachableException ex)
        {
            RechercheListe.Items.Add(BuildRechercheRow(null, ex.Message));
        }

        var gefundenCount = events.Count(ev => ev.Step == "suchergebnis" && !ev.IstVerworfen);
        RechercheStatusText.Text = gefundenCount > 0
            ? $"🔎 Recherche abgeschlossen (Modus: {modus}) — {gefundenCount} Quelle(n) gefunden"
            : $"🔎 Recherche abgeschlossen (Modus: {modus}) — keine Quellen gefunden";

        return AgenticSearchContext.BuildKontext(events, modus);
    }

    /// <summary>Task #311: Abschnitt "Anonymisiert gesendet" im selben RecherchePanel, das die
    /// Suchschritte zeigt — macht das Panel sichtbar, auch wenn (z.B. ohne Art+Anweisung) keine
    /// Recherche gelaufen ist, damit der Anwalt immer sieht, was den PC wirklich verlässt.</summary>
    private void ZeigeAnonymisiertGesendet(string anonymisiertGesendet)
    {
        if (anonymisiertGesendet.Length == 0) return;
        RecherchePanel.Visibility = Visibility.Visible;
        RechercheListe.Items.Add(BuildAnonymisiertRow(anonymisiertGesendet));
        RechercheScroll.ScrollToEnd();
    }

    /// <summary>Pendant zu BuildRechercheRow, nur für den anonymisierten Sende-Text (Task #311) —
    /// eigene Farbe (Amber statt Grün/Rot), damit sie sich klar von den Suchschritten abhebt.</summary>
    private UIElement BuildAnonymisiertRow(string text)
    {
        var amber = new SolidColorBrush(Color.FromRgb(0xe8, 0xa8, 0x38));

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = "🔒", Foreground = amber, FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 6, 0) });
        header.Children.Add(new TextBlock { Text = "Anonymisiert an die KI gesendet:", Foreground = amber, FontSize = 14, FontWeight = FontWeights.Bold });

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(new TextBlock { Text = text, Foreground = System.Windows.Media.Brushes.White, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18, 6, 0, 0) });

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x16, 0x08)),
            BorderBrush = amber,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 10),
            Margin = new Thickness(0, 0, 0, 6),
            Child = body,
        };
    }

    /// <summary>Eine Zeile im RecherchePanel — Pendant zu ReActDialog.BuildRow, nur ohne
    /// eigenes Fenster, damit der Live-Suchweg direkt neben dem entstehenden Entwurf steht.</summary>
    private UIElement BuildRechercheRow(ReActStepEvent? evt, string? fehlerText = null)
    {
        var (icon, colorHex, quelle, text, confidence) = evt switch
        {
            null => ("⚠️", "#f85149", "Live-Suche", fehlerText ?? "nicht erreichbar", (double?)null),
            { Step: "suche_start" } => ("🔎", "#8b949e", evt.Quelle ?? evt.Step, evt.Text, evt.Confidence),
            { Step: "bewertung" } => ("📊", "#8b949e", evt.Quelle ?? evt.Step, evt.Text, evt.Confidence),
            { Step: "fertig" } => ("✅", "#3fb950", evt.Quelle ?? evt.Step, evt.Text, evt.Confidence),
            { Step: "suchergebnis", IstVerworfen: true } => ("✗", "#f85149", evt.Quelle ?? evt.Step, evt.Text, evt.Confidence),
            { Step: "suchergebnis" } => ("✓", "#3fb950", evt.Quelle ?? evt.Step, evt.Text, evt.Confidence),
            _ => ("•", "#8b949e", evt.Quelle ?? evt.Step, evt.Text, evt.Confidence),
        };

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = icon, Foreground = brush, FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 6, 0) });
        header.Children.Add(new TextBlock { Text = quelle, Foreground = System.Windows.Media.Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold });
        if (confidence.HasValue)
            header.Children.Add(new TextBlock { Text = $"  {confidence.Value * 100:F0}%", Foreground = brush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });

        var body = new StackPanel();
        body.Children.Add(header);
        if (!string.IsNullOrWhiteSpace(text))
            body.Children.Add(new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0xa8, 0xb3, 0xbd)), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 3, 0, 0) });

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0d, 0x11, 0x17)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3d)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 7),
            Child = body,
        };
    }

    /// <summary>Beim Wechsel der Schriftsatz-Art die für diese Art zuletzt gewählte
    /// Vorlage anzeigen (Task #304) — jede Art hat ihre eigene gemerkte Vorlage.</summary>
    private void OnArtGeaendert(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => ZeigeVorlage();

    /// <summary>"Vorlage für diese Art" ist die einzige Vorlagen-Auswahl im Dialog — sie wird
    /// sowohl gemerkt (pro Art, siehe LocalDb.UpsertLetzteVorlage) als auch direkt als
    /// Stilvorlage an das Backend gesendet (StilvorlageText). Vorher gab es dafür zwei
    /// getrennte, verwirrend ähnliche Felder ("Vorlage für diese Art" + "Stilvorlage"), wobei
    /// nur die Stilvorlage tatsächlich in den Entwurf einfloss — die Art-Vorlage wirkte sich
    /// nur auf den allgemeinen Chat aus (MainWindow.GetLatestTemplateContext), nicht hier.</summary>
    private void ZeigeVorlage()
    {
        if (ArtBox.SelectedItem is not string art) { VorlageText.Text = "keine"; return; }
        var gewaehlt = _db.GetLetzteVorlage(art);
        var pfad = gewaehlt?.VorlagePfad ?? "";
        VorlageText.Text = pfad.Length == 0 ? "keine — Standardstil"
            : File.Exists(pfad) ? Path.GetFileName(pfad) : $"{Path.GetFileName(pfad)} (nicht gefunden)";
        VorlageText.ToolTip = pfad.Length > 0 ? pfad : null;
    }

    /// <summary>Explizite Auswahl der Vorlagen-Datei für die aktuell gewählte Schriftsatz-Art —
    /// nur dieser bewusste Klick speichert die Auswahl, nicht ein bloßer Lesezugriff auf
    /// die Datei (siehe LocalDb.UpsertLetzteVorlage / MainWindow.GetLatestTemplateContext).</summary>
    private void OnVorlageWaehlen(object sender, RoutedEventArgs e)
    {
        if (ArtBox.SelectedItem is not string art) return;
        var vorlagenOrdner = VorlagenOrdner.SicherstellenUnterordner(_dokumentePfad);
        var artOrdner = Path.Combine(vorlagenOrdner, VorlagenOrdner.OrdnerName(art));
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Vorlage für „{art}“ wählen",
            Filter = "Schriftsätze (*.docx;*.pdf;*.txt)|*.docx;*.pdf;*.txt",
            InitialDirectory = Directory.Exists(artOrdner) ? artOrdner : vorlagenOrdner,
        };
        if (dlg.ShowDialog(this) != true) return;
        _db.UpsertLetzteVorlage(art, dlg.FileName);
        ZeigeVorlage();
    }

    private void OnVorlageEntfernen(object sender, RoutedEventArgs e)
    {
        if (ArtBox.SelectedItem is not string art) return;
        _db.UpsertLetzteVorlage(art, "");
        ZeigeVorlage();
    }

    /// <summary>Inhalt der für die aktuell gewählte Art gemerkten Vorlage — wird dem Backend
    /// als stil_vorlage übergeben (Aufbau, Ton, Formulierungen, keine Inhalte).</summary>
    private string StilvorlageText()
    {
        if (ArtBox.SelectedItem is not string art) return "";
        var pfad = _db.GetLetzteVorlage(art)?.VorlagePfad ?? "";
        if (pfad.Length == 0 || !File.Exists(pfad)) return "";
        try { return DokumentScanner.LeseText(pfad); } catch { return ""; }
    }

    /// <summary>Rechner öffnen (vorbelegt aus dem Fragebogen) und den Rechenweg übernehmen:
    /// in den Entwurf an der Cursorposition — oder, solange es keinen gibt, in die Anweisung.</summary>
    private void OnBerechnung(object sender, RoutedEventArgs e)
    {
        var fragebogen = Fragebogen.FromJson(_db.GetFragebogen(_mandantId));
        var dlg = new RechnerDialog(fragebogen, _mandantName, "📝 In den Schriftsatz übernehmen", _db, _mandantId, _backendUrl) { Owner = this };
        dlg.ShowDialog();
        if (dlg.InChatUebernehmen is not { } berechnung) return;

        if (EntwurfBox.Text.Trim().Length == 0)
        {
            AnweisungBox.Text = (AnweisungBox.Text.TrimEnd() + "\n\nVerwende diese Berechnung (Zahlen genau übernehmen):\n" + berechnung).Trim();
            StatusText.Text = "Berechnung in die Anweisung übernommen — sie fließt in den Entwurf ein.";
            return;
        }
        var pos = EntwurfBox.CaretIndex;
        EntwurfBox.Text = EntwurfBox.Text.Insert(pos, $"\n\n{berechnung}\n\n");
        EntwurfBox.CaretIndex = pos + berechnung.Length + 4;
        EntwurfBox.Focus();
        StatusText.Text = "Berechnung an der Cursorposition eingefügt.";
    }

    private void OnKopieren(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(DokumentExport.OhneFundstellen(DokumentExport.OhneMarkdown(DokumentExport.OhneTestHinweis(EntwurfBox.Text))));
        StatusText.Text = "In die Zwischenablage kopiert.";
    }

    private void OnWord(object sender, RoutedEventArgs e)
    {
        try
        {
            var ordner = Path.Combine(_dokumentePfad, _mandantName);
            Directory.CreateDirectory(ordner);
            var art = ((string)ArtBox.SelectedItem).Split(' ')[0];
            var inhalt = InhaltsKurzform(EntwurfBox.Text);
            var namensteil = inhalt.Length > 0 ? inhalt : DateTime.Now.ToString("yyyy-MM-dd");
            var pfad = DokumentExport.FreierPfad(ordner, $"LexWolf_{art}_{namensteil}.docx");
            DokumentExport.SchreibeDocx(pfad, DokumentExport.OhneFundstellen(EntwurfBox.Text));
            StatusText.Text = $"Gespeichert: {Path.GetFileName(pfad)}";
            DokumentExport.OeffneMitStandardprogramm(pfad);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Speichern fehlgeschlagen: {ex.Message}";
        }
    }

    /// <summary>Kurzer, dateinamentauglicher Auszug aus der "wegen:"-Zeile (Streitgegenstand),
    /// die der Entwurf laut Backend-Prompt (_SCHRIFTSATZ_PROMPT) praktisch immer enthält —
    /// statt nur eines Datums, sonst sind mehrere Entwürfe desselben Typs im Dateinamen nicht
    /// unterscheidbar. Leer, wenn keine brauchbare Zeile gefunden wird (Aufrufer nutzt dann
    /// weiterhin das Datum als Namensteil).</summary>
    private static string InhaltsKurzform(string entwurf)
    {
        var zeile = entwurf.Split('\n')
            .Select(z => z.Trim())
            .FirstOrDefault(z => z.StartsWith("wegen:", StringComparison.OrdinalIgnoreCase));
        if (zeile is null) return "";

        var inhalt = zeile[(zeile.IndexOf(':') + 1)..].Trim().TrimEnd('.', ' ');
        if (inhalt.Length == 0 || inhalt.StartsWith("[Ergänzung erforderlich", StringComparison.OrdinalIgnoreCase))
            return "";

        const string ungueltig = "\\/:*?\"<>|";
        var bereinigt = string.Concat(inhalt.Select(c => ungueltig.Contains(c) || char.IsControl(c) ? ' ' : c));
        bereinigt = string.Join(" ", bereinigt.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return bereinigt.Length > 40 ? bereinigt[..40].TrimEnd() : bereinigt;
    }

    private void OnSchliessen(object sender, RoutedEventArgs e) => Close();
}
