using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using LexWolf.Database;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>Sachstandsmitteilung an den Mandanten aus Eingängen, Fristen und Notizen der Akte.
/// Der Anwalt wählt per Haken, welche Punkte hineinkommen.</summary>
public partial class SachstandDialog : Window
{
    public sealed class Punkt
    {
        public required string Datum { get; init; }
        public required string Titel { get; init; }
        public string Text { get; init; } = "";
        /// <summary>So geht der Punkt an die KI.</summary>
        public required string FuerKi { get; init; }
        public bool Ausgewaehlt { get; set; } = true;
        public Visibility TextSichtbar => Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private readonly LocalDb _db;
    private readonly string _backendUrl;
    private readonly string _mandantId;
    private readonly string _mandantName;
    private readonly string _dokumentePfad;
    private readonly List<Punkt> _entwicklungen;
    private readonly List<Punkt> _fristen;
    private readonly List<Punkt> _notizen;
    private CancellationTokenSource? _cts;

    public SachstandDialog(LocalDb db, string backendUrl, string mandantId, string mandantName, string dokumentePfad)
    {
        InitializeComponent();
        FensterGroesse.AnSchriftAnpassen(this);
        _db = db;
        _backendUrl = backendUrl;
        _mandantId = mandantId;
        _mandantName = mandantName;
        _dokumentePfad = dokumentePfad;
        UntertitelText.Text = mandantName;

        _entwicklungen = db.GetEingaenge(mandantId, limit: 12)
            .Select(e =>
            {
                try
                {
                    var a = DokumentAnalyse.Parse(e.Json);
                    var datum = DatumEingabe.Format(a.Datum ?? e.Eingang.ToLocalTime());
                    var typ = a.Dokumenttyp.Length > 0 ? a.Dokumenttyp : e.Titel;
                    return new Punkt
                    {
                        Datum = datum, Titel = typ, Text = a.Zusammenfassung,
                        FuerKi = $"{datum}: {a.Dokumenttyp} „{e.Titel}“ — {a.Zusammenfassung}",
                    };
                }
                catch { return null; }
            })
            .Where(x => x is not null).Cast<Punkt>().ToList();
        // Wiedervorlagen sind interne Erinnerungen — gehören nicht in die Mitteilung an den Mandanten.
        _fristen = db.GetFristen(mandantId).Where(f => f.Status == "offen" && f.FristartId != "wiedervorlage")
            .OrderBy(f => f.Ende)
            .Select(f => new Punkt
            {
                Datum = DatumEingabe.Format(f.Ende), Titel = f.Bezeichnung,
                FuerKi = $"{f.Bezeichnung}: bis {DatumEingabe.Lang(f.Ende)}",
            }).ToList();
        // Notizen sind intern — nur auf ausdrücklichen Wunsch in die Mitteilung.
        _notizen = db.GetNotizen(mandantId).Take(8)
            .Select(n => new Punkt
            {
                Datum = DatumEingabe.Format(n.Geaendert.ToLocalTime()), Titel = n.TitelKurz, Text = n.Text,
                FuerKi = $"{n.TitelKurz}: {n.Text}", Ausgewaehlt = false,
            }).ToList();

        EntwicklungenListe.ItemsSource = _entwicklungen;
        FristenListe.ItemsSource = _fristen;
        NotizenListe.ItemsSource = _notizen;
        EntwicklungenLeer.Visibility = _entwicklungen.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FristenLeer.Visibility = _fristen.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NotizenLeer.Visibility = _notizen.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ZaehlerAktualisieren();
        Closed += (_, _) => _cts?.Cancel();
    }

    private void ZaehlerAktualisieren()
    {
        static string Zahl(List<Punkt> l) => l.Count == 0 ? "0" : $"{l.Count(p => p.Ausgewaehlt)} von {l.Count}";
        EntwicklungenZahl.Text = Zahl(_entwicklungen);
        FristenZahl.Text = Zahl(_fristen);
        NotizenZahl.Text = Zahl(_notizen);
        var gewaehlt = _entwicklungen.Concat(_fristen).Concat(_notizen).Count(p => p.Ausgewaehlt);
        if (MitteilungBox.Text.Length == 0)
            StatusText.Text = gewaehlt == 0 && HinweisBox.Text.Trim().Length == 0
                ? "Nichts ausgewählt — Punkte anhaken oder unter „Zusätzlich erwähnen“ etwas eintragen."
                : $"{gewaehlt} Punkt(e) ausgewählt.";
    }

    private void OnAuswahlGeaendert(object sender, RoutedEventArgs e) => ZaehlerAktualisieren();

    private void OnMitteilungGeaendert(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        LeerHinweis.Visibility = MitteilungBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private static List<string> Gewaehlt(List<Punkt> l) => l.Where(p => p.Ausgewaehlt).Select(p => p.FuerKi).ToList();

    private async void OnErstellen(object sender, RoutedEventArgs e)
    {
        var stamm = _db.GetAktenStamm(_mandantId);
        var payload = JsonSerializer.Serialize(new
        {
            mandant = _mandantName,
            anrede = AnredeBox.Text.Trim(),
            stammdaten = string.Join("; ", new[] { stamm.Gericht, stamm.Aktenzeichen.Length > 0 ? $"Az. {stamm.Aktenzeichen}" : "",
                stamm.Gegner.Length > 0 ? $"Gegenseite: {stamm.Gegner}" : "" }.Where(s => s.Length > 0)),
            entwicklungen = Gewaehlt(_entwicklungen),
            fristen = Gewaehlt(_fristen),
            notizen = Gewaehlt(_notizen),
            hinweis = HinweisBox.Text.Trim(),
        });

        ErstellenBtn.IsEnabled = false;
        StatusText.Text = "⏳ LexWolf schreibt den Sachstand …";
        _cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await http.PostAsync($"{_backendUrl}/akte/sachstand", content, _cts.Token);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_cts.Token));
            // Reiner Text: die Mitteilung geht als E-Mail an Laien — keine Sternchen/Rauten.
            MitteilungBox.Text = DokumentExport.OhneMarkdown(doc.RootElement.GetProperty("text").GetString() ?? "");
            StatusText.Text = "✓ Fertig. Bitte lesen und bei Bedarf anpassen.";
            KopierenBtn.IsEnabled = EmailBtn.IsEnabled = WordBtn.IsEnabled = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !_cts.IsCancellationRequested)
        {
            StatusText.Text = $"Fehlgeschlagen: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Abgebrochen.";
        }
        finally
        {
            ErstellenBtn.IsEnabled = true;
        }
    }

    private void OnKopieren(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(DokumentExport.OhneTestHinweis(MitteilungBox.Text));

    private void OnEmail(object sender, RoutedEventArgs e)
    {
        try
        {
            var entwurf = DokumentExport.ParseEmail(MitteilungBox.Text);
            if (entwurf.An.Length == 0)
                entwurf = entwurf with { An = MandatAufnahme.EmailAus(_db.GetMeta(MandatAufnahme.MetaSchluessel(_mandantId))) };
            var pfad = Path.Combine(Path.GetTempPath(), $"LexWolf_Sachstand_{DateTime.Now:yyyyMMdd_HHmmss}.eml");
            DokumentExport.SchreibeEmlNach(entwurf, pfad);
            DokumentExport.OeffneMitStandardprogramm(pfad);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"E-Mail konnte nicht geöffnet werden: {ex.Message}";
        }
    }

    private void OnWord(object sender, RoutedEventArgs e)
    {
        try
        {
            var ordner = Path.Combine(_dokumentePfad, _mandantName);
            Directory.CreateDirectory(ordner);
            var pfad = DokumentExport.FreierPfad(ordner, $"LexWolf_Sachstand_{DateTime.Now:yyyy-MM-dd}.docx");
            DokumentExport.SchreibeDocx(pfad, MitteilungBox.Text);
            StatusText.Text = $"Gespeichert: {Path.GetFileName(pfad)}";
            DokumentExport.OeffneMitStandardprogramm(pfad);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Speichern fehlgeschlagen: {ex.Message}";
        }
    }

    private void OnSchliessen(object sender, RoutedEventArgs e) => Close();
}
