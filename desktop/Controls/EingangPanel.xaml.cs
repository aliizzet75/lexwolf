using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LexWolf.Database;
using LexWolf.Services;

namespace LexWolf.Controls;

public sealed class EingangZeile
{
    public required EingangEintrag Eintrag { get; init; }
    public required DokumentAnalyse Analyse { get; init; }
    public required string Mandant { get; init; }

    public string Titel => Eintrag.Titel;
    public string Icon => Analyse.Dokumenttyp.ToLowerInvariant() switch
    {
        var t when t.Contains("urteil") || t.Contains("beschluss") => "⚖",
        var t when t.Contains("ladung") || t.Contains("verfügung") => "🏛",
        var t when t.Contains("bescheid") || t.Contains("mahn") => "📜",
        var t when t.Contains("e-mail") || t.Contains("brief") => "✉",
        var t when t.Contains("schriftsatz") => "📑",
        _ => "📄",
    };
    public string Meta
    {
        get
        {
            var teile = new List<string> { Mandant };
            if (Analyse.Dokumenttyp.Length > 0) teile.Add(Analyse.Dokumenttyp);
            if (Analyse.Absender.Length > 0) teile.Add(Analyse.Absender);
            teile.Add($"eingegangen {DatumEingabe.Format(Eintrag.Eingang.ToLocalTime())}");
            return string.Join(" · ", teile);
        }
    }
    public string Zusammenfassung => Analyse.Zusammenfassung;
    public string Anliegen => Analyse.Anliegen.Length > 0 ? $"Verlangt: {Analyse.Anliegen}" : "";
    public string NaechsterSchritt => Analyse.NaechsterSchritt.Length > 0 ? $"➜ {Analyse.NaechsterSchritt}" : "";
    public string FristenText => Analyse.Fristen.Count == 0 ? ""
        : $"⏰ {string.Join(", ", Analyse.Fristen.Select(f => f.Bezeichnung))} — als Vorschlag in der Fristenliste";
    public Visibility AnliegenSichtbar => Anliegen.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SchrittSichtbar => NaechsterSchritt.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FristenSichtbar => FristenText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EiligSichtbar => Analyse.Eilig && !Eintrag.Gelesen ? Visibility.Visible : Visibility.Collapsed;
    public Brush Rahmen => Analyse.Eilig && !Eintrag.Gelesen ? new SolidColorBrush(Color.FromRgb(0xf8, 0x51, 0x49))
                                                             : new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3d));
    public double Deckkraft => Eintrag.Gelesen ? 0.6 : 1.0;
    public string GelesenText => Eintrag.Gelesen ? "↺ Wieder offen" : "✓ Erledigt";
}

// ToString = Anzeigetext: die dunkle ComboBox-Vorlage zeigt im geschlossenen Zustand ToString().
public sealed record MandantWahl(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Per Postfach abgerufene Mail, die keinem Mandanten sicher zugeordnet werden konnte.</summary>
public sealed class UnzugeordnetZeile
{
    public required string Pfad { get; init; }
    public required string Absender { get; init; }
    public required string Betreff { get; init; }
    public required string Meta { get; init; }
    public required string Grund { get; init; }
    public required List<MandantWahl> Mandanten { get; init; }
    public MandantWahl? Gewaehlt { get; set; }
}

/// <summary>Posteingang: von LexWolf ausgewertete neue Dokumente aller Mandanten.</summary>
public partial class EingangPanel : UserControl
{
    private LocalDb? _db;
    private PostfachService? _postfach;
    private Func<AppSettings>? _settings;
    private string? _aktiverMandant;

    /// <summary>"Antwort entwerfen": (mandantId, Eingang) — MainWindow wechselt den Mandanten und füllt den Chat.</summary>
    public event Action<string, EingangZeile>? AntwortAngefordert;

    /// <summary>Anzahl offener (unerledigter) Eingänge — für das Badge am Reiter.</summary>
    public event Action<int>? OffeneGeaendert;

    public EingangPanel() => InitializeComponent();

    public void Init(LocalDb db, PostfachService? postfach = null, Func<AppSettings>? settings = null)
    {
        _db = db;
        _postfach = postfach;
        _settings = settings;
        if (postfach is not null)
            postfach.Abgerufen += (_, _, _) => Dispatcher.BeginInvoke(Aktualisieren);
        Aktualisieren();
    }

    private void AktualisierePostfachText()
    {
        var s = _settings?.Invoke();
        if (_postfach is null || s is null) { PostfachText.Text = ""; return; }
        AbrufenBtn.Visibility = s.PostfachAktiv && s.PostfachServer.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!s.PostfachAktiv || s.PostfachServer.Length == 0)
        {
            PostfachText.Text = "Kein E-Mail-Postfach verbunden — über „✉ Postfach“ einrichten.";
            PostfachText.Foreground = (Brush)FindResource("LwMuted");
            return;
        }
        if (_postfach.LetzterFehler is { } fehler)
        {
            PostfachText.Text = $"⚠ Postfach {s.PostfachBenutzer}: {fehler}";
            PostfachText.Foreground = (Brush)FindResource("LwRed");
            return;
        }
        var zuletzt = _postfach.LetzterAbruf is { } d ? $"zuletzt abgerufen {d:HH:mm} Uhr" : "wird gleich abgerufen";
        PostfachText.Text = $"✉ {s.PostfachBenutzer} — {zuletzt}";
        PostfachText.Foreground = (Brush)FindResource("LwMuted");
    }

    private void AktualisiereUnzugeordnet()
    {
        if (_db is null) return;
        var mandanten = _db.GetMandanten().Select(m => new MandantWahl(m.Id, m.Name)).ToList();
        var zeilen = _db.GetUnzugeordnet()
            .Where(u => System.IO.File.Exists(u.Pfad))
            .Select(u => new UnzugeordnetZeile
            {
                Pfad = u.Pfad,
                Absender = u.Absender,
                Betreff = u.Betreff.Length > 0 ? u.Betreff : "(ohne Betreff)",
                Meta = $"{(u.AbsenderName.Length > 0 ? $"{u.AbsenderName} <{u.Absender}>" : u.Absender)} · {u.Datum:dd.MM.yyyy HH:mm}",
                Grund = $"Nicht zugeordnet: {u.Grund}",
                Mandanten = mandanten,
            }).ToList();
        UnzugeordnetListe.ItemsSource = zeilen;
        UnzugeordnetTitel.Text = $"E-Mails ohne Mandant ({zeilen.Count}) — bitte zuordnen";
        UnzugeordnetTitel.Visibility = zeilen.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetAktiverMandant(string? mandantId)
    {
        _aktiverMandant = mandantId;
        if (NurMandantBox.IsChecked == true) Aktualisieren();
    }

    public void SetAuswertung(int anzahl)
    {
        AuswertungText.Text = anzahl == 1 ? "⏳ 1 Dokument wird ausgewertet …" : $"⏳ {anzahl} Dokumente werden ausgewertet …";
        AuswertungText.Visibility = anzahl > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Aktualisieren()
    {
        if (_db is null) return;
        var namen = _db.GetMandanten().ToDictionary(m => m.Id, m => m.Name);
        var filter = NurMandantBox.IsChecked == true ? _aktiverMandant : null;
        var alle = _db.GetEingaenge(filter, nurUngelesen: AuchErledigteBox.IsChecked != true);

        var zeilen = new List<EingangZeile>();
        foreach (var e in alle)
        {
            DokumentAnalyse a;
            try { a = DokumentAnalyse.Parse(e.Json); } catch { continue; }
            zeilen.Add(new EingangZeile { Eintrag = e, Analyse = a, Mandant = namen.GetValueOrDefault(e.MandantId, "") });
        }
        // Eilige zuerst, dann neueste
        Liste.ItemsSource = zeilen.OrderByDescending(z => z.Analyse.Eilig && !z.Eintrag.Gelesen)
                                  .ThenBy(z => z.Eintrag.Gelesen)
                                  .ThenByDescending(z => z.Eintrag.Eingang).ToList();
        AktualisiereUnzugeordnet();
        AktualisierePostfachText();
        LeerText.Visibility = zeilen.Count == 0 && UnzugeordnetListe.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var offene = _db.GetEingaenge(nurUngelesen: true);
        var seitGestern = offene.Count(e => e.Eingang.ToLocalTime() >= DateTime.Today.AddDays(-1));
        var eilig = offene.Count(e => { try { return DokumentAnalyse.Parse(e.Json).Eilig; } catch { return false; } });
        UeberblickText.Text = offene.Count == 0
            ? "Alles erledigt ✓"
            : $"{offene.Count} offene Eingänge" + (seitGestern > 0 ? $" · {seitGestern} seit gestern" : "") +
              (eilig > 0 ? $" · ⚠ {eilig} eilig" : "");
        OffeneGeaendert?.Invoke(offene.Count);
    }

    private static EingangZeile? ZeileAus(object sender) => (sender as FrameworkElement)?.Tag as EingangZeile;

    private void OnAntwort(object sender, RoutedEventArgs e)
    {
        if (ZeileAus(sender) is { } z) AntwortAngefordert?.Invoke(z.Eintrag.MandantId, z);
    }

    private void OnOeffnen(object sender, RoutedEventArgs e)
    {
        if (ZeileAus(sender) is { } z) DokumentExport.OeffneMitStandardprogramm(z.Eintrag.Pfad);
    }

    private void OnGelesen(object sender, RoutedEventArgs e)
    {
        if (ZeileAus(sender) is not { } z || _db is null) return;
        _db.SetEingangGelesen(z.Eintrag.Pfad, !z.Eintrag.Gelesen);
        Aktualisieren();
    }

    /// <summary>Entfernt einen falsch zugeordneten Eingang wieder aus dieser Akte (z.B. eine
    /// E-Mail, die versehentlich diesem Mandanten zugeordnet wurde). Löscht nur die
    /// Zuordnung/Auswertung, nicht die zugrunde liegende Datei — und korrigiert damit
    /// automatisch auch die Zeitleiste im Aktenstamm, die daraus abgeleitet wird.</summary>
    private void OnEntfernen(object sender, RoutedEventArgs e)
    {
        if (ZeileAus(sender) is not { } z || _db is null) return;
        if (System.Windows.MessageBox.Show(
                Window.GetWindow(this),
                $"„{z.Titel}“ wirklich aus dieser Akte entfernen?\n\nDie Datei selbst bleibt erhalten, nur die Zuordnung/Auswertung wird gelöscht.",
                "Eingang entfernen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _db.DeleteAnalyse(z.Eintrag.Pfad);
        Aktualisieren();
    }

    private void OnFilterGeaendert(object sender, RoutedEventArgs e) => Aktualisieren();

    private void OnPostfach(object sender, RoutedEventArgs e)
    {
        if (_postfach is null || _settings is null) return;
        if (new Dialogs.PostfachDialog(_settings()) { Owner = Window.GetWindow(this) }.ShowDialog() == true)
        {
            _postfach.Starten(); // ruft gleich ab und dann im eingestellten Abstand
            AktualisierePostfachText();
        }
    }

    private async void OnAbrufen(object sender, RoutedEventArgs e)
    {
        if (_postfach is null) return;
        AbrufenBtn.IsEnabled = false;
        PostfachText.Text = "⏳ Postfach wird abgerufen …";
        await _postfach.AbrufenAsync();
        AbrufenBtn.IsEnabled = true;
    }

    private static UnzugeordnetZeile? UnzugeordnetAus(object sender) => (sender as FrameworkElement)?.Tag as UnzugeordnetZeile;

    private void OnZuordnen(object sender, RoutedEventArgs e)
    {
        if (UnzugeordnetAus(sender) is not { } z || _postfach is null) return;
        if (z.Gewaehlt is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Bitte zuerst einen Mandanten auswählen.", "Zuordnen",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            _postfach.Zuordnen(z.Pfad, z.Gewaehlt.Id, z.Gewaehlt.Name, z.Absender);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Zuordnen fehlgeschlagen: {ex.Message}", "Zuordnen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Aktualisieren();
    }

    private void OnUnzugeordnetOeffnen(object sender, RoutedEventArgs e)
    {
        if (UnzugeordnetAus(sender) is { } z) DokumentExport.OeffneMitStandardprogramm(z.Pfad);
    }

    private void OnVerwerfen(object sender, RoutedEventArgs e)
    {
        if (UnzugeordnetAus(sender) is not { } z || _postfach is null) return;
        try { _postfach.Verwerfen(z.Pfad); } catch { }
        Aktualisieren();
    }
}
