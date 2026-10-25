using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>
/// Zeigt nach einem Mandantengespräch die vom Backend erzeugten Vorschläge für die
/// nächsten Schritte (Task #131, Pilot-Scope: Fristennotiz + E-Mail-Entwurf) und lässt
/// den Anwalt je Vorschlag mit Ja / Nein / Geändert entscheiden. Jede Entscheidung wird
/// sofort über <see cref="VorschlaegeService.SendeEntscheidungAsync"/> an das Backend
/// gemeldet, das sie in die BESTEHENDE Lernschleife (style_learner/feedback_service)
/// einspeist — dieser Dialog speichert selbst nichts lokal.
/// </summary>
public partial class VorschlaegeDialog : Window
{
    private readonly HttpClient _http;
    private readonly string _backendUrl;
    private readonly string _accountId;
    private readonly string _dokumentePfad;
    private readonly string _mandantName;

    public VorschlaegeDialog(IReadOnlyList<Vorschlag> vorschlaege, HttpClient http, string backendUrl,
        string accountId = "default", string dokumentePfad = "", string mandantName = "")
    {
        InitializeComponent();
        _http = http;
        _backendUrl = backendUrl;
        _accountId = accountId;
        _dokumentePfad = dokumentePfad;
        _mandantName = mandantName;

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

        var jaBtn = new Button { Content = "✓ Ja", Style = (Style)FindResource("LwPrimaryButton"), Margin = new Thickness(0, 0, 8, 0) };
        var neinBtn = new Button { Content = "✕ Nein", Style = (Style)FindResource("LwGefahrButton"), Margin = new Thickness(0, 0, 8, 0) };
        var geaendertBtn = new Button { Content = "💾 Geändert übernehmen", Style = (Style)FindResource("LwSmallButton"), Margin = new Thickness(0, 0, 8, 0) };
        var wordBtn = new Button { Content = "📄 Als Word speichern", Style = (Style)FindResource("LwSmallButton") };

        wordBtn.Click += (_, _) =>
        {
            try
            {
                var ordner = string.IsNullOrWhiteSpace(_dokumentePfad)
                    ? Path.GetTempPath()
                    : Path.Combine(_dokumentePfad, _mandantName);
                Directory.CreateDirectory(ordner);
                var pfad = DokumentExport.FreierPfad(ordner, DokumentExport.DateinameFuerSchreiben(vorschlag.Titel, DateTime.Now));
                DokumentExport.SchreibeDocx(pfad, textBox.Text);
                DokumentExport.OeffneMitStandardprogramm(pfad);
                statusText.Text = $"Gespeichert: {Path.GetFileName(pfad)}";
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(0x3f, 0xb9, 0x50));
                statusText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                statusText.Text = $"Speichern fehlgeschlagen: {ex.Message}";
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(0xf8, 0x51, 0x49));
                statusText.Visibility = Visibility.Visible;
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

            statusText.Text = erfolg
                ? "Übernommen — fließt in die Lernschleife ein."
                : "Konnte nicht an den Server gemeldet werden (Entscheidung lokal verworfen).";
            statusText.Foreground = erfolg
                ? new SolidColorBrush(Color.FromRgb(0x3f, 0xb9, 0x50))
                : new SolidColorBrush(Color.FromRgb(0xf8, 0x51, 0x49));
            statusText.Visibility = Visibility.Visible;
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

    private void OnFertig(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <summary>
    /// Holt nach Gesprächsende die Vorschläge vom Backend (POST /conversation/suggestions,
    /// Ausgabe von ConversationAssistant.generate_suggestions) und zeigt sie modal an, falls
    /// es welche gibt. Fehler beim Abrufen werden geschluckt — das Gespräch wurde bereits
    /// gespeichert, fehlende Vorschläge dürfen diesen Fluss nicht blockieren.
    /// </summary>
    public static async Task ZeigeNachGespraechAsync(
        Window owner, HttpClient http, string backendUrl, JsonElement strukturiert,
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

        var dialog = new VorschlaegeDialog(vorschlaege, http, backendUrl, accountId, dokumentePfad, mandantName) { Owner = owner };
        dialog.ShowDialog();
    }
}
