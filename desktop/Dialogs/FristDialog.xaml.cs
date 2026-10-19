using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LexWolf.Database;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>Frist anlegen oder bearbeiten — mit Live-Berechnung und Rechenweg.
/// Ein bestätigter Vorschlag wird hier zur "offenen" Frist.</summary>
public partial class FristDialog : Window
{
    // ToString = Anzeigetext: die dunkle ComboBox-Vorlage zeigt im geschlossenen Zustand ToString().
    private sealed record ArtOption(string Anzeige, string Modus, Fristart? Art)
    {
        public override string ToString() => Anzeige;
    }
    public sealed record MandantOption(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private readonly LocalDb _db;
    private readonly AppSettings _settings;
    private readonly FristEintrag? _bestehend;
    private FristErgebnis? _ergebnis;
    private string _letzteAutoBezeichnung = "";
    private bool _vorfristManuell;

    public FristDialog(LocalDb db, AppSettings settings, FristEintrag? bestehend = null, string? mandantId = null)
    {
        InitializeComponent();
        _db = db;
        _settings = settings;
        _bestehend = bestehend;

        var mandanten = db.GetMandanten().Select(m => new MandantOption(m.Id, m.Name)).ToList();
        MandantBox.ItemsSource = mandanten;
        MandantBox.SelectedItem = mandanten.FirstOrDefault(m => m.Id == (bestehend?.MandantId ?? mandantId));

        var arten = new List<ArtOption>
        {
            new("Vom Gericht / der Gegenseite gesetzte Frist (Datum)", "gesetzt", null),
            new("Wiedervorlage (Datum)", "wiedervorlage", null),
            new("Eigene Frist (Dauer ab Ereignis)", "frei", null),
        };
        arten.AddRange(FristRechner.Fristarten.Select(f =>
            new ArtOption($"{f.Name} — {f.Dauer}, {f.Rechtsgrundlage}", "gesetzlich", f)));
        ArtBox.ItemsSource = arten;

        VorfristBox.TextChanged += (_, _) => { if (VorfristBox.IsKeyboardFocusWithin) _vorfristManuell = true; };

        if (bestehend is not null)
        {
            HeaderText.Text = bestehend.Status == "vorschlag" ? "Fristvorschlag prüfen und übernehmen" : "Frist bearbeiten";
            ArtBox.SelectedItem = arten.FirstOrDefault(a => a.Art?.Id == bestehend.FristartId)
                                  ?? arten.First(a => a.Modus == (bestehend.FristartId is "wiedervorlage" or "frei" ? bestehend.FristartId : "gesetzt"));
            BezeichnungBox.Text = bestehend.Bezeichnung;
            _letzteAutoBezeichnung = "";
            EreignisBox.Text = DatumEingabe.Format(bestehend.Ereignis);
            DatumBox.Text = DatumEingabe.Format(bestehend.Ende);
            VorfristBox.Text = DatumEingabe.Format(bestehend.Vorfrist);
            NotfristBox.IsChecked = bestehend.Notfrist;
            NotizBox.Text = bestehend.Notiz;
            _vorfristManuell = true;
            if (bestehend.EreignisAngenommen)
                FehlerText.Text = "Zustelldatum wurde angenommen (Datei-Eingang) — bitte mit Umschlag/beA-Eingang abgleichen.";
        }
        else
        {
            ArtBox.SelectedIndex = 0;
            EreignisBox.Text = DatumEingabe.Format(DateTime.Today);
        }
        Aktualisieren();
    }

    private void OnEingabeGeaendert(object sender, RoutedEventArgs e)
    {
        if (IsLoaded || _bestehend is null) Aktualisieren();
    }

    private void Aktualisieren()
    {
        if (ArtBox.SelectedItem is not ArtOption art || RechenwegText is null) return;

        var modus = art.Modus;
        EreignisPanel.Visibility = modus is "gesetzt" or "wiedervorlage" ? Visibility.Collapsed : Visibility.Visible;
        DatumPanel.Visibility = modus is "gesetzt" or "wiedervorlage" ? Visibility.Visible : Visibility.Collapsed;
        DauerPanel.Visibility = modus == "frei" ? Visibility.Visible : Visibility.Collapsed;
        // Ohne Ereignisfeld das Datumsfeld nach links rücken statt eine Lücke zu lassen.
        Grid.SetColumn(DatumPanel, EreignisPanel.Visibility == Visibility.Visible ? 2 : 0);
        EreignisLabel.Text = art.Art is not null ? $"{art.Art.Ereignis} am" : "Ereignis (z. B. Zugang) am";
        ArtHinweis.Text = art.Art?.Hinweis ?? (modus == "wiedervorlage" ? "Interne Erinnerung ohne Rechtsfolge." : "");

        var auto = art.Art?.Name ?? (modus == "wiedervorlage" ? "Wiedervorlage" : modus == "gesetzt" ? "Stellungnahmefrist" : "Frist");
        if (BezeichnungBox.Text.Length == 0 || BezeichnungBox.Text == _letzteAutoBezeichnung)
        {
            BezeichnungBox.Text = auto;
            _letzteAutoBezeichnung = auto;
        }
        if (_bestehend is null || IsLoaded) NotfristBox.IsChecked = art.Art?.Notfrist ?? NotfristBox.IsChecked;

        _ergebnis = null;
        var land = _settings.Bundesland;
        switch (modus)
        {
            case "gesetzt":
            case "wiedervorlage":
                if (DatumEingabe.Parse(DatumBox.Text) is { } datum)
                    _ergebnis = modus == "gesetzt"
                        ? FristRechner.GesetzteFrist(datum, land)
                        : new FristErgebnis(datum, datum, datum, $"Wiedervorlage am {DatumEingabe.Lang(datum)}.");
                break;
            case "frei":
                if (DatumEingabe.Parse(EreignisBox.Text) is { } ev && int.TryParse(AnzahlBox.Text, out var n) && n > 0)
                {
                    var einheit = (FristEinheit)Math.Max(0, EinheitBox.SelectedIndex);
                    _ergebnis = FristRechner.Berechne(ev, n, einheit, land, einheit == FristEinheit.Monate ? 7 : 3);
                }
                break;
            default:
                if (DatumEingabe.Parse(EreignisBox.Text) is { } e2)
                    _ergebnis = FristRechner.Berechne(e2, art.Art!, land);
                break;
        }

        if (_ergebnis is null)
        {
            EndeText.Text = "Fristende: — (Datum eingeben, z. B. 14.10.2026)";
            RechenwegText.Text = "";
            return;
        }
        EndeText.Text = $"Fristende: {DatumEingabe.Lang(_ergebnis.Ende)}  ({DatumEingabe.Abstand(_ergebnis.Ende)})";
        RechenwegText.Text = _ergebnis.Rechenweg +
            $"\nFeiertage: {FristRechner.Bundeslaender.GetValueOrDefault(land, land)} (Einstellung in der Fristenliste).";
        if (!_vorfristManuell || VorfristBox.Text.Length == 0)
            VorfristBox.Text = DatumEingabe.Format(_ergebnis.Vorfrist);
    }

    private void OnSpeichern(object sender, RoutedEventArgs e)
    {
        Aktualisieren();
        if (MandantBox.SelectedItem is not MandantOption mandant) { FehlerText.Text = "Bitte einen Mandanten wählen."; return; }
        if (_ergebnis is null) { FehlerText.Text = "Bitte ein gültiges Datum eingeben."; return; }
        if (BezeichnungBox.Text.Trim().Length == 0) { FehlerText.Text = "Bitte eine Bezeichnung eingeben."; return; }
        var vorfrist = DatumEingabe.Parse(VorfristBox.Text) ?? _ergebnis.Vorfrist;
        if (vorfrist > _ergebnis.Ende) { FehlerText.Text = "Die Vorfrist liegt nach dem Fristende."; return; }

        var art = (ArtOption)ArtBox.SelectedItem;
        var fristartId = art.Art?.Id ?? art.Modus;
        var ereignis = art.Modus is "gesetzt" or "wiedervorlage" ? (_bestehend?.Ereignis ?? DateTime.Today) : DatumEingabe.Parse(EreignisBox.Text)!.Value;

        _db.UpsertFrist(new FristEintrag(
            _bestehend?.Id ?? Guid.NewGuid().ToString("N"),
            mandant.Id,
            BezeichnungBox.Text.Trim(),
            fristartId,
            art.Art?.Rechtsgrundlage ?? (art.Modus == "gesetzt" ? "gesetzte Frist" : ""),
            ereignis,
            _ergebnis.Ende,
            vorfrist,
            NotfristBox.IsChecked == true,
            "offen", // Speichern = vom Anwalt geprüft und bestätigt
            _bestehend?.QuellePfad,
            _ergebnis.Rechenweg,
            NotizBox.Text.Trim(),
            false)); // mit dem Speichern hat der Anwalt das Ereignisdatum geprüft
        DialogResult = true;
    }
}
