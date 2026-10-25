using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LexWolf.Database;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>
/// Zeigt nach einem Mandantengespräch die vom Backend erzeugten Vorschläge für die
/// nächsten Schritte (Task #131, Pilot-Scope: Fristennotiz + E-Mail-Entwurf) und lässt
/// den Anwalt je Vorschlag mit Ja / Nein / Geändert entscheiden. Jede Entscheidung wird
/// über <see cref="VorschlaegeService.SendeEntscheidungAsync"/> an das Backend gemeldet
/// (fließt in die Lernschleife ein) — UND bei "Ja"/"Geändert" wird zusätzlich ein echtes
/// Artefakt erzeugt (Fristen-Vorschlag in der Akte bzw. E-Mail-Entwurf-Datei), nicht nur
/// ein Lernsignal ohne sichtbares Ergebnis.
/// </summary>
public partial class VorschlaegeDialog : Window
{
    private readonly HttpClient _http;
    private readonly string _backendUrl;
    private readonly string _accountId;
    private readonly string _dokumentePfad;
    private readonly string _mandantName;
    private readonly LocalDb? _db;
    private readonly Func<AppSettings>? _settings;
    private readonly string _mandantId;

    public VorschlaegeDialog(IReadOnlyList<Vorschlag> vorschlaege, HttpClient http, string backendUrl,
        string accountId = "default", string dokumentePfad = "", string mandantName = "",
        LocalDb? db = null, Func<AppSettings>? settings = null, string mandantId = "")
    {
        InitializeComponent();
        _http = http;
        _backendUrl = backendUrl;
        _accountId = accountId;
        _dokumentePfad = dokumentePfad;
        _mandantName = mandantName;
        _db = db;
        _settings = settings;
        _mandantId = mandantId;

        foreach (var vorschlag in vorschlaege)
            VorschlaegePanel.Children.Add(ErzeugeKarte(vorschlag));
    }

    /// <summary>Baut die Karte für einen einzelnen Vorschlag: Titel, editierbarer Text
    /// (für "Geändert") und die drei Entscheidungs-Knöpfe Ja/Nein/Geändert übernehmen.</summary>
    private Border ErzeugeKarte(Vorschlag vorschlag)
    {
        var textBox = new TextBox
        {
            Text = vorschlag.Text,
            Style = (Style)FindResource("LwEditor"),
            MinHeight = 70,
            MaxHeight = 160,
        };

        var statusText = new TextBlock
        {
            Style = (Style)FindResource("LwHinweis"),
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        void ZeigeStatus(string text, bool erfolg)
        {
            statusText.Text = text;
            statusText.Foreground = erfolg
                ? new SolidColorBrush(Color.FromRgb(0x3f, 0xb9, 0x50))
                : new SolidColorBrush(Color.FromRgb(0xf8, 0x51, 0x49));
            statusText.Visibility = Visibility.Visible;
        }

        var jaBtn = new Button { Content = "✓ Ja", Style = (Style)FindResource("LwPrimaryButton"), Margin = new Thickness(0, 0, 8, 0) };
        var neinBtn = new Button { Content = "✕ Nein", Style = (Style)FindResource("LwGefahrButton"), Margin = new Thickness(0, 0, 8, 0) };
        var geaendertBtn = new Button { Content = "💾 Geändert übernehmen", Style = (Style)FindResource("LwSmallButton"), Margin = new Thickness(0, 0, 8, 0) };
        var wordBtn = new Button { Content = "📄 Als Word speichern", Style = (Style)FindResource("LwSmallButton") };

        wordBtn.Click += (_, _) =>
        {
            try
            {
                var pfad = DokumentExport.FreierPfad(ZielOrdner(), DokumentExport.DateinameFuerSchreiben(vorschlag.Titel, DateTime.Now));
                DokumentExport.SchreibeDocx(pfad, textBox.Text);
                DokumentExport.OeffneMitStandardprogramm(pfad);
                ZeigeStatus($"Gespeichert: {Path.GetFileName(pfad)}", erfolg: true);
            }
            catch (Exception ex)
            {
                ZeigeStatus($"Speichern fehlgeschlagen: {ex.Message}", erfolg: false);
            }
        };

        async void Entscheiden(string entscheidung)
        {
            jaBtn.IsEnabled = false;
            neinBtn.IsEnabled = false;
            geaendertBtn.IsEnabled = false;
            textBox.IsEnabled = false;

            var geaenderterText = entscheidung == "geaendert" ? textBox.Text : null;
            var erfolg = await VorschlaegeService.SendeEntscheidungAsync(
                _http, _backendUrl, vorschlag.Typ, vorschlag.Text, entscheidung, geaenderterText, _accountId)
                .ConfigureAwait(true);

            string? artefaktFehler = null;
            if (entscheidung is "ja" or "geaendert")
                artefaktFehler = ErzeugeArtefakt(vorschlag, textBox.Text);

            ZeigeStatus(
                artefaktFehler ?? (erfolg
                    ? "Übernommen — fließt in die Lernschleife ein."
                    : "Übernommen, aber Entscheidung konnte nicht an den Server gemeldet werden."),
                erfolg: artefaktFehler is null);
        }

        jaBtn.Click += (_, _) => Entscheiden("ja");
        neinBtn.Click += (_, _) => Entscheiden("nein");
        geaendertBtn.Click += (_, _) => Entscheiden("geaendert");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(jaBtn);
        buttons.Children.Add(neinBtn);
        buttons.Children.Add(geaendertBtn);
        buttons.Children.Add(wordBtn);

        var inhalt = new StackPanel();
        inhalt.Children.Add(new TextBlock
        {
            Text = vorschlag.Titel,
            Style = (Style)FindResource("LwAbschnitt"),
            Margin = new Thickness(0, 0, 0, 8),
        });
        inhalt.Children.Add(textBox);
        inhalt.Children.Add(buttons);
        inhalt.Children.Add(statusText);

        return new Border { Style = (Style)FindResource("VorschlagKarte"), Child = inhalt };
    }

    private string ZielOrdner()
    {
        if (string.IsNullOrWhiteSpace(_dokumentePfad)) return Path.GetTempPath();
        var ordner = Path.Combine(_dokumentePfad, _mandantName);
        Directory.CreateDirectory(ordner);
        return ordner;
    }

    /// <summary>Erzeugt bei "Ja"/"Geändert" das eigentliche, sichtbare Ergebnis in der Akte —
    /// nicht nur das Lernsignal an den Server. "fristennotiz" -> Fristen-Vorschlag (Status
    /// "vorschlag", der Anwalt bestätigt ihn wie jeden anderen KI-Fristvorschlag im
    /// Fristen-Panel — Datum kommt aus der KI-Analyse, die eigentliche Berechnung macht wie
    /// immer FristRechner, nie die KI direkt). "email_entwurf" -> .eml-Datei in der Akte,
    /// sofort geöffnet. Gibt eine Fehlermeldung zurück, oder null bei Erfolg.</summary>
    private string? ErzeugeArtefakt(Vorschlag vorschlag, string text)
    {
        try
        {
            switch (vorschlag.Typ)
            {
                case "fristennotiz":
                    return ErzeugeFristenvorschlag(vorschlag, text);
                case "email_entwurf":
                    return ErzeugeEmailEntwurf(vorschlag, text);
                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            return $"Übernommen, aber Artefakt konnte nicht erzeugt werden: {ex.Message}";
        }
    }

    private string? ErzeugeFristenvorschlag(Vorschlag vorschlag, string text)
    {
        if (_db is null || _settings is null || string.IsNullOrWhiteSpace(_mandantId))
            return "Übernommen (kein Akte-Zugriff — Fristvorschlag konnte nicht angelegt werden).";

        string? datumRoh = vorschlag.Metadata.ValueKind == JsonValueKind.Object &&
                           vorschlag.Metadata.TryGetProperty("datum", out var d) ? d.GetString() : null;

        if (string.IsNullOrWhiteSpace(datumRoh) ||
            !DateTime.TryParse(datumRoh, System.Globalization.CultureInfo.GetCultureInfo("de-DE"),
                System.Globalization.DateTimeStyles.None, out var datum))
        {
            return "Übernommen — Datum aus dem Gespräch war nicht eindeutig, bitte Frist manuell im Fristen-Panel anlegen.";
        }

        var ergebnis = FristRechner.GesetzteFrist(datum, _settings().Bundesland);
        _db.UpsertFrist(new FristEintrag(
            Guid.NewGuid().ToString("N"), _mandantId, text, null, "",
            DateTime.Now, ergebnis.Ende, ergebnis.Vorfrist, false, "vorschlag", null,
            ergebnis.Rechenweg, "Aus Gesprächs-Vorschlag übernommen — bitte prüfen.", false));

        return null;
    }

    private string? ErzeugeEmailEntwurf(Vorschlag vorschlag, string text)
    {
        var entwurf = new EmailEntwurf("", vorschlag.Titel.TrimEnd('?'), text.Replace("[Mandant]", _mandantName));
        var pfad = DokumentExport.SchreibeEml(entwurf, ZielOrdner());
        DokumentExport.OeffneMitStandardprogramm(pfad);
        return null;
    }

    private void OnFertig(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <summary>
    /// Holt nach Gesprächsende die Vorschläge vom Backend (POST /conversation/suggestions,
    /// Ausgabe von ConversationAssistant.generate_suggestions) und zeigt sie modal an, falls
    /// es welche gibt. Fehler beim Abrufen werden geschluckt — das Gespräch wurde bereits
    /// gespeichert, fehlende Vorschläge dürfen diesen Fluss nicht blockieren.
    /// </summary>
    public static async Task ZeigeNachGespraechAsync(
        Window owner, HttpClient http, string backendUrl, JsonElement strukturiert,
        LocalDb? db = null, Func<AppSettings>? settings = null, string mandantId = "",
        IReadOnlyList<string>? mandantHistory = null, string accountId = "default",
        string dokumentePfad = "", string mandantName = "")
    {
        List<Vorschlag> vorschlaege;
        try
        {
            vorschlaege = await VorschlaegeService.HoleVorschlaegeAsync(http, backendUrl, strukturiert, mandantHistory)
                .ConfigureAwait(true);
        }
        catch (Exception)
        {
            return;
        }

        if (vorschlaege.Count == 0) return;

        var dialog = new VorschlaegeDialog(vorschlaege, http, backendUrl, accountId, dokumentePfad, mandantName,
            db, settings, mandantId) { Owner = owner };
        dialog.ShowDialog();
    }
}
