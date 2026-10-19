using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using LexWolf.Controls;
using LexWolf.Database;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>Antwort auf einen Eingang direkt entwerfen (Backend /akte/antwort).
/// Bei E-Mails wird an den Absender geantwortet ("AW: Betreff").</summary>
public partial class AntwortDialog : Window
{
    private readonly LocalDb _db;
    private readonly string _backendUrl;
    private readonly EingangZeile _eingang;
    private readonly string _dokumentePfad;
    private string _dokumentText = "";
    private string _antwortAn = "";
    private string _antwortBetreff = "";
    private CancellationTokenSource? _cts;

    /// <summary>Gesetzt, wenn der Entwurf im Chat weiterbearbeitet werden soll.</summary>
    public string? InChatUebernehmen { get; private set; }

    public AntwortDialog(LocalDb db, string backendUrl, EingangZeile eingang, string dokumentePfad)
    {
        InitializeComponent();
        _db = db;
        _backendUrl = backendUrl;
        _eingang = eingang;
        _dokumentePfad = dokumentePfad;
        HeaderText.Text = $"Antwort auf „{eingang.Titel}“ — {eingang.Mandant}";

        var a = eingang.Analyse;
        var istMail = eingang.Eintrag.Pfad.EndsWith(".eml", StringComparison.OrdinalIgnoreCase);
        var gericht = a.Gericht.Length > 0 ? a.Gericht : "Gericht";
        EmpfaengerBox.ItemsSource = new[]
        {
            a.Absender.Length > 0 ? $"Absender: {a.Absender}" : "Absender des Dokuments",
            $"Gericht: {gericht}",
            "Gegenseite / Gegnervertreter",
            $"Mandant: {eingang.Mandant}",
        };
        // Vorauswahl: gerichtliche Dokumente → Gericht, sonst Absender
        var typ = a.Dokumenttyp.ToLowerInvariant();
        EmpfaengerBox.SelectedIndex = !istMail && (typ.Contains("urteil") || typ.Contains("beschluss") || typ.Contains("verfügung") || typ.Contains("ladung")) ? 1 : 0;

        DokumentText.Text = "⏳ wird gelesen …";
        Loaded += async (_, _) => await LadeDokumentAsync(istMail);
        Closed += (_, _) => _cts?.Cancel();
    }

    private async Task LadeDokumentAsync(bool istMail)
    {
        var pfad = _eingang.Eintrag.Pfad;
        try
        {
            _dokumentText = await Task.Run(() => DokumentScanner.LeseText(pfad));
            if (istMail)
            {
                var m = await Task.Run(() => MimeKit.MimeMessage.Load(pfad));
                _antwortAn = (m.ReplyTo.Mailboxes.FirstOrDefault() ?? m.From.Mailboxes.FirstOrDefault())?.Address ?? "";
                var betreff = m.Subject ?? "";
                _antwortBetreff = betreff.StartsWith("AW:", StringComparison.OrdinalIgnoreCase) ? betreff : $"AW: {betreff}";
            }
        }
        catch (Exception ex)
        {
            _dokumentText = "";
            StatusText.Text = $"Dokument konnte nicht gelesen werden: {ex.Message}";
        }
        DokumentText.Text = _dokumentText.Length > 4000 ? _dokumentText[..4000] + " …" : _dokumentText;
        if (_dokumentText.Length == 0) DokumentText.Text = "(kein lesbarer Text)";
    }

    private async void OnErstellen(object sender, RoutedEventArgs e)
    {
        var stamm = _db.GetAktenStamm(_eingang.Eintrag.MandantId);
        var payload = JsonSerializer.Serialize(new
        {
            titel = _eingang.Titel,
            text = _dokumentText.Length > 0 ? _dokumentText : _eingang.Analyse.Zusammenfassung,
            empfaenger = (string)EmpfaengerBox.SelectedItem,
            mandant = _eingang.Mandant,
            stammdaten = string.Join("; ", new[]
            {
                stamm.Gericht, stamm.Aktenzeichen.Length > 0 ? $"Az. {stamm.Aktenzeichen}" : "",
                stamm.Gegner.Length > 0 ? $"Gegner: {stamm.Gegner}" : "",
                stamm.Gegnervertreter.Length > 0 ? $"Gegnervertreter: {stamm.Gegnervertreter}" : "",
            }.Where(s => s.Length > 0)),
            hinweis = HinweisBox.Text.Trim(),
        });

        ErstellenBtn.IsEnabled = false;
        StatusText.Text = "⏳ LexWolf schreibt den Entwurf …";
        _cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await http.PostAsync($"{_backendUrl}/akte/antwort", content, _cts.Token);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_cts.Token));
            var text = DokumentExport.OhneMarkdown(doc.RootElement.GetProperty("text").GetString() ?? "");
            // Bei Mail-Antworten Empfänger und "AW:"-Betreff vorbelegen
            if (_antwortAn.Length > 0 && EmpfaengerBox.SelectedIndex == 0 && !text.StartsWith("An:", StringComparison.OrdinalIgnoreCase))
            {
                var zeilen = text.Replace("\r\n", "\n").Split('\n').ToList();
                if (zeilen.Count > 0 && zeilen[0].StartsWith("Betreff:", StringComparison.OrdinalIgnoreCase) && _antwortBetreff.Length > 0)
                    zeilen[0] = $"Betreff: {_antwortBetreff}";
                zeilen.Insert(0, $"An: {_antwortAn}");
                text = string.Join("\n", zeilen);
            }
            EntwurfBox.Text = text;
            StatusText.Text = "✓ Entwurf fertig — bitte prüfen. [Ergänzung erforderlich] markiert, was noch fehlt.";
            KopierenBtn.IsEnabled = EmailBtn.IsEnabled = WordBtn.IsEnabled = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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

    private void OnChat(object sender, RoutedEventArgs e)
    {
        var a = _eingang.Analyse;
        InChatUebernehmen = EntwurfBox.Text.Trim().Length > 0
            ? $"Überarbeite diesen Antwortentwurf auf „{_eingang.Titel}“:\n\n{DokumentExport.OhneTestHinweis(EntwurfBox.Text)}"
            : $"Entwirf eine Antwort auf das Dokument „{_eingang.Titel}“" +
              (a.Absender.Length > 0 ? $" von {a.Absender}" : "") + "." +
              (a.Anliegen.Length > 0 ? $" Verlangt wird: {a.Anliegen}." : "") +
              (HinweisBox.Text.Trim().Length > 0 ? $" Vorgabe: {HinweisBox.Text.Trim()}" : "");
        Close();
    }

    private void OnKopieren(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(DokumentExport.OhneTestHinweis(EntwurfBox.Text));

    private void OnEmail(object sender, RoutedEventArgs e)
    {
        try
        {
            var entwurf = DokumentExport.ParseEmail(EntwurfBox.Text);
            var pfad = Path.Combine(Path.GetTempPath(), $"LexWolf_Antwort_{DateTime.Now:yyyyMMdd_HHmmss}.eml");
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
            var ordner = Path.Combine(_dokumentePfad, _eingang.Mandant);
            Directory.CreateDirectory(ordner);
            var pfad = DokumentExport.FreierPfad(ordner, $"LexWolf_Antwort_{DateTime.Now:yyyy-MM-dd}.docx");
            DokumentExport.SchreibeDocx(pfad, EntwurfBox.Text);
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
