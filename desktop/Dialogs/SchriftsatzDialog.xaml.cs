using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using LexWolf.Database;
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
        ZeigeStil();
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
        Closed += (_, _) => _cts?.Cancel();
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

        var payload = JsonSerializer.Serialize(new
        {
            art = (string)ArtBox.SelectedItem,
            anweisung = AnweisungBox.Text.Trim(),
            mandant = _mandantName,
            stammdaten = stammText,
            dokumente = auswahl.Select(d => new { titel = d.Titel, text = d.Text }),
            stil_vorlage = StilvorlageText(),
        });

        ErstellenBtn.IsEnabled = false;
        StatusText.Text = "⏳ LexWolf liest die Akte und entwirft den Schriftsatz …";
        _cts = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        try
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await http.PostAsync($"{_backendUrl}/akte/schriftsatz", content, _cts.Token);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_cts.Token));
            EntwurfBox.Text = doc.RootElement.GetProperty("text").GetString() ?? "";
            var belege = doc.RootElement.TryGetProperty("belege", out var b) && b.ValueKind == JsonValueKind.Array
                ? b.EnumerateArray().Select(x => new Beleg(x.GetProperty("zitat").GetString() ?? "", x.GetProperty("status").GetString() ?? "",
                    x.TryGetProperty("hinweis", out var h) ? h.GetString() ?? "" : "")).ToList()
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

    /// <summary>Beim Wechsel der Schriftsatz-Art die für diese Art zuletzt gewählte
    /// Vorlage anzeigen (Task #304) — jede Art hat ihre eigene gemerkte Vorlage.</summary>
    private void OnArtGeaendert(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => ZeigeVorlage();

    private void ZeigeVorlage()
    {
        if (ArtBox.SelectedItem is not string art) { VorlageText.Text = "keine"; return; }
        var gewaehlt = _db.GetLetzteVorlage(art);
        VorlageText.Text = gewaehlt is null ? "keine"
            : File.Exists(gewaehlt.Value.VorlagePfad) ? Path.GetFileName(gewaehlt.Value.VorlagePfad)
            : $"{Path.GetFileName(gewaehlt.Value.VorlagePfad)} (nicht gefunden)";
        VorlageText.ToolTip = gewaehlt?.VorlagePfad;
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

    private void ZeigeStil()
    {
        var pfad = _settings.StilvorlagePfad;
        StilText.Text = pfad.Length == 0 ? "keine — Standardstil"
            : File.Exists(pfad) ? Path.GetFileName(pfad) : $"{Path.GetFileName(pfad)} (nicht gefunden)";
        StilText.ToolTip = pfad.Length > 0 ? pfad : null;
    }

    private string StilvorlageText()
    {
        var pfad = _settings.StilvorlagePfad;
        if (pfad.Length == 0 || !File.Exists(pfad)) return "";
        try { return DokumentScanner.LeseText(pfad); } catch { return ""; }
    }

    private void OnStilWaehlen(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Musterschriftsatz als Stilvorlage wählen",
            Filter = "Schriftsätze (*.docx;*.pdf;*.txt)|*.docx;*.pdf;*.txt",
            InitialDirectory = Directory.Exists(_dokumentePfad) ? _dokumentePfad : "",
        };
        if (dlg.ShowDialog(this) != true) return;
        _settings.StilvorlagePfad = dlg.FileName;
        try { _settings.Save(); } catch { }
        ZeigeStil();
    }

    private void OnStilEntfernen(object sender, RoutedEventArgs e)
    {
        _settings.StilvorlagePfad = "";
        try { _settings.Save(); } catch { }
        ZeigeStil();
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
