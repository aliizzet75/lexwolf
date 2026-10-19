using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LexWolf.Database;
using LexWolf.Dialogs;
using LexWolf.Services;

namespace LexWolf.Controls;

public sealed record FristGruppe(string Titel, Brush Farbe);

// ToString = Anzeigetext: die dunkle ComboBox-Vorlage zeigt im geschlossenen Zustand ToString().
public sealed record Land(string Key, string Value)
{
    public override string ToString() => Value;
}

public sealed class FristZeile
{
    public required FristEintrag Eintrag { get; init; }
    public required string Mandant { get; init; }
    public required Brush Farbe { get; init; }
    public string Bezeichnung => Eintrag.Bezeichnung;
    public string EndeText => $"bis {DatumEingabe.Lang(Eintrag.Ende)} · ";
    public string Abstand => DatumEingabe.Abstand(Eintrag.Ende);
    public string VorfristText => Eintrag.Vorfrist < Eintrag.Ende ? $"Vorfrist {DatumEingabe.Lang(Eintrag.Vorfrist)}" : "";
    public string Hinweis => Eintrag.Status == "vorschlag"
        ? (Eintrag.EreignisAngenommen
            ? $"Vorschlag von LexWolf — Zustellung am {DatumEingabe.Format(Eintrag.Ereignis)} angenommen (Datei-Eingang), bitte prüfen."
            : "Vorschlag von LexWolf — bitte prüfen und übernehmen.")
        : "";
    public Visibility HinweisSichtbar => Hinweis.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NotfristSichtbar => Eintrag.Notfrist ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VorschlagSichtbar => Eintrag.Status == "vorschlag" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OffenSichtbar => Eintrag.Status == "offen" ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Fristen und Wiedervorlagen aller Mandanten, nach Dringlichkeit gruppiert.</summary>
public partial class FristenPanel : UserControl
{
    private static readonly Brush Rot = Brush(0xf8, 0x51, 0x49);
    private static readonly Brush Orange = Brush(0xd2, 0x99, 0x22);
    private static readonly Brush Blau = Brush(0x58, 0xa6, 0xff);
    private static readonly Brush Grau = Brush(0x8b, 0x94, 0x9e);

    private LocalDb? _db;
    private Func<AppSettings>? _settings;
    private string? _aktiverMandant;
    private bool _init;

    /// <summary>Klick auf eine Frist: zu diesem Mandanten wechseln.</summary>
    public event Action<string>? MandantGewaehlt;

    /// <summary>Anzahl dringender Fristen (überfällig/heute/morgen) — für das Badge am Reiter.</summary>
    public event Action<int>? DringendGeaendert;

    public FristenPanel() => InitializeComponent();

    private static Brush Brush(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    public void Init(LocalDb db, Func<AppSettings> settings)
    {
        _db = db;
        _settings = settings;
        BundeslandBox.ItemsSource = FristRechner.Bundeslaender.Select(kv => new Land(kv.Key, kv.Value)).ToList();
        BundeslandBox.SelectedValue = settings().Bundesland;
        _init = true;
        Aktualisieren();
    }

    public void SetAktiverMandant(string? mandantId)
    {
        _aktiverMandant = mandantId;
        if (NurMandantBox.IsChecked == true) Aktualisieren();
    }

    public void Aktualisieren()
    {
        if (_db is null) return;
        var namen = _db.GetMandanten().ToDictionary(m => m.Id, m => m.Name);
        var mandantFilter = NurMandantBox.IsChecked == true ? _aktiverMandant : null;
        var fristen = _db.GetFristen(mandantFilter);
        var heute = DateTime.Today;
        var wochenEnde = heute.AddDays(7);

        FristZeile Zeile(FristEintrag f, Brush farbe) => new()
        {
            Eintrag = f, Mandant = namen.GetValueOrDefault(f.MandantId, "(unbekannter Mandant)"), Farbe = farbe,
        };

        var items = new List<object>();
        void Gruppe(string titel, Brush farbe, IEnumerable<FristEintrag> liste, Func<FristEintrag, Brush> zeilenFarbe)
        {
            var l = liste.ToList();
            if (l.Count == 0) return;
            items.Add(new FristGruppe($"{titel} ({l.Count})", farbe));
            items.AddRange(l.Select(f => Zeile(f, zeilenFarbe(f))));
        }

        var vorschlaege = fristen.Where(f => f.Status == "vorschlag").ToList();
        var offen = fristen.Where(f => f.Status == "offen").ToList();
        Gruppe("Neue Vorschläge — bitte prüfen", Blau, vorschlaege, f => f.Ende <= heute.AddDays(1) ? Rot : Blau);
        Gruppe("Überfällig", Rot, offen.Where(f => f.Ende < heute), _ => Rot);
        Gruppe("Heute und morgen", Rot, offen.Where(f => f.Ende >= heute && f.Ende <= heute.AddDays(1)), _ => Rot);
        Gruppe("Diese Woche", Orange, offen.Where(f => f.Ende > heute.AddDays(1) && f.Ende <= wochenEnde), _ => Orange);
        Gruppe("Vorfrist erreicht", Orange, offen.Where(f => f.Ende > wochenEnde && f.Vorfrist <= heute), _ => Orange);
        Gruppe("Später", Grau, offen.Where(f => f.Ende > wochenEnde && f.Vorfrist > heute), _ => Grau);

        Liste.ItemsSource = items;
        LeerText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var dringend = offen.Count(f => f.Ende <= heute.AddDays(1));
        var woche = offen.Count(f => f.Ende > heute.AddDays(1) && f.Ende <= wochenEnde);
        var teile = new List<string>();
        if (dringend > 0) teile.Add($"⚠ {dringend} dringend");
        if (woche > 0) teile.Add($"{woche} diese Woche");
        if (vorschlaege.Count > 0) teile.Add(vorschlaege.Count == 1 ? "1 Vorschlag" : $"{vorschlaege.Count} Vorschläge");
        ZusammenfassungText.Text = teile.Count > 0 ? string.Join(" · ", teile) : $"{offen.Count} offene Fristen";
        DringendGeaendert?.Invoke(dringend + vorschlaege.Count(f => f.Ende <= heute.AddDays(1)));
    }

    private static FristZeile? ZeileAus(object sender) => (sender as FrameworkElement)?.Tag as FristZeile;

    private void OnZeileClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindeButton(d)) return;
        if (ZeileAus(sender) is { } z) MandantGewaehlt?.Invoke(z.Eintrag.MandantId);
    }

    private static bool FindeButton(DependencyObject d)
    {
        while (d is not null and not Border { Tag: FristZeile })
        {
            if (d is Button) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private void OnNeu(object sender, RoutedEventArgs e) => OeffneDialog(null);

    private void OnBearbeiten(object sender, RoutedEventArgs e)
    {
        if (ZeileAus(sender) is { } z) OeffneDialog(z.Eintrag);
    }

    private void OeffneDialog(FristEintrag? f)
    {
        if (_db is null || _settings is null) return;
        var dlg = new FristDialog(_db, _settings(), f, _aktiverMandant) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) Aktualisieren();
    }

    private void OnUebernehmen(object sender, RoutedEventArgs e)
    {
        if (ZeileAus(sender) is not { } z || _db is null) return;
        if (z.Eintrag.EreignisAngenommen)
        {
            // Zustelldatum nur geschätzt — erst prüfen lassen statt blind übernehmen.
            OeffneDialog(z.Eintrag);
            return;
        }
        _db.UpsertFrist(z.Eintrag with { Status = "offen" });
        Aktualisieren();
    }

    private void OnVerwerfen(object sender, RoutedEventArgs e)
    {
        if (ZeileAus(sender) is not { } z || _db is null) return;
        _db.SetFristStatus(z.Eintrag.Id, "verworfen");
        Aktualisieren();
    }

    private void OnErledigt(object sender, RoutedEventArgs e)
    {
        if (ZeileAus(sender) is not { } z || _db is null) return;
        var antwort = MessageBox.Show(Window.GetWindow(this),
            $"„{z.Bezeichnung}“ ({z.Mandant}) als erledigt markieren?", "Frist erledigt",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (antwort != MessageBoxResult.Yes) return;
        _db.SetFristStatus(z.Eintrag.Id, "erledigt");
        Aktualisieren();
    }

    private void OnKalender(object sender, RoutedEventArgs e)
    {
        if (ZeileAus(sender) is not { } z) return;
        try
        {
            var pfad = Path.Combine(Path.GetTempPath(), $"LexWolf-Frist-{z.Eintrag.Id[..8]}.ics");
            File.WriteAllText(pfad, AkteService.BaueIcs(z.Eintrag, z.Mandant), new UTF8Encoding(false));
            DokumentExport.OeffneMitStandardprogramm(pfad);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Kalenderdatei konnte nicht geöffnet werden: {ex.Message}",
                "Kalender", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnDrucken(object sender, RoutedEventArgs e)
    {
        if (_db is null) return;
        try
        {
            var namen = _db.GetMandanten().ToDictionary(m => m.Id, m => m.Name);
            var pfad = Path.Combine(Path.GetTempPath(), $"LexWolf-Fristenliste-{DateTime.Today:yyyy-MM-dd}.html");
            File.WriteAllText(pfad, FristenListe.BaueHtml(_db.GetFristen(), namen, DateTime.Today), new UTF8Encoding(false));
            DokumentExport.OeffneMitStandardprogramm(pfad);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Fristenliste konnte nicht erstellt werden: {ex.Message}",
                "Fristenliste", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnFilterGeaendert(object sender, RoutedEventArgs e) => Aktualisieren();

    private void OnBundeslandGeaendert(object sender, SelectionChangedEventArgs e)
    {
        if (!_init || _settings is null || BundeslandBox.SelectedValue is not string land) return;
        var s = _settings();
        if (s.Bundesland == land) return;
        s.Bundesland = land;
        try { s.Save(); } catch { }
    }
}
