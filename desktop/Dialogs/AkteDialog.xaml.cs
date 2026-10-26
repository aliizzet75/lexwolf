using System;
using System.Linq;
using System.Windows;
using LexWolf.Database;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>Aktenstamm: Stammdaten, Beteiligte, Zeitleiste und Kollisionsprüfung eines Mandanten.</summary>
public partial class AkteDialog : Window
{
    private sealed record ZeitleisteZeile(DateTime Datum, string DatumText, string Beschreibung);

    private readonly LocalDb _db;
    private readonly AkteService _akte;
    private readonly string _mandantId;
    private AktenStamm _gespeichert;

    public AkteDialog(LocalDb db, AkteService akte, string mandantId, string mandantName)
    {
        InitializeComponent();
        FensterGroesse.AnSchriftAnpassen(this);
        _db = db;
        _akte = akte;
        _mandantId = mandantId;
        UntertitelText.Text = mandantName;

        _gespeichert = db.GetAktenStamm(mandantId);
        GerichtBox.Text = _gespeichert.Gericht;
        AzBox.Text = _gespeichert.Aktenzeichen;
        GegnerBox.Text = _gespeichert.Gegner;
        GegnervertreterBox.Text = _gespeichert.Gegnervertreter;
        NotizBox.Text = _gespeichert.Notiz;

        akte.Geaendert += OnAkteGeaendert;
        // Eingaben gehen beim Schließen nicht verloren, auch ohne „Speichern“.
        Closing += (_, _) => SpeichernFallsGeaendert();
        Closed += (_, _) => akte.Geaendert -= OnAkteGeaendert;
        Laden();
    }

    private void OnAkteGeaendert() => Dispatcher.BeginInvoke(Laden);

    /// <summary>Blendet einen einzelnen Zeitleiste-Eintrag dauerhaft aus — z.B. wenn eine
    /// falsch zugeordnete E-Mail ein nicht passendes Ereignis in die Zeitleiste gebracht hat.
    /// Entfernt nur diesen einen Eintrag, nicht das zugrunde liegende Dokument (dafür: Posteingang).</summary>
    private void OnZeitleisteEintragLoeschen(object sender, RoutedEventArgs e)
    {
        if (((System.Windows.Controls.Button)sender).DataContext is not ZeitleisteZeile z) return;
        _db.BlendeZeitleisteEintragAus(_mandantId, z.Datum, z.Beschreibung);
        Laden();
    }

    private void Laden()
    {
        var beteiligte = _akte.Beteiligte(_mandantId).OrderBy(b => b.Rolle).ThenBy(b => b.Name).ToList();
        BeteiligteListe.ItemsSource = beteiligte;
        BeteiligteZahl.Text = beteiligte.Count.ToString();
        BeteiligteLeer.Visibility = beteiligte.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var zeitleiste = _akte.Zeitleiste(_mandantId)
            .Select(e => new ZeitleisteZeile(e.Datum, DatumEingabe.Format(e.Datum), e.Beschreibung)).ToList();
        ZeitleisteListe.ItemsSource = zeitleiste;
        ZeitleisteZahl.Text = zeitleiste.Count.ToString();
        ZeitleisteLeer.Visibility = zeitleiste.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var kollisionen = _akte.Kollisionen(_mandantId);
        KollisionBanner.Visibility = kollisionen.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        KollisionText.Text = string.Join("\n", kollisionen.Select(k => $"• {k.Beschreibung} — Akte „{k.AndereAkte}“"));

        var offen = _akte.NichtAusgewertet(_mandantId);
        var laeuft = _akte.InAuswertung;
        AuswertungText.Text = laeuft > 0
            ? $"⏳ {laeuft} Dokument(e) in Auswertung — die Ansicht aktualisiert sich von selbst."
            : offen > 0 ? $"{offen} Dokument(e) noch nicht ausgewertet." : "✓ Alle Dokumente ausgewertet.";
        AuswertenBtn.IsEnabled = offen > 0;
    }

    private AktenStamm AusFeldern() => new(_mandantId, GerichtBox.Text.Trim(), AzBox.Text.Trim(),
        GegnerBox.Text.Trim(), GegnervertreterBox.Text.Trim(), NotizBox.Text.Trim());

    private bool SpeichernFallsGeaendert()
    {
        var neu = AusFeldern();
        if (neu == _gespeichert) return false;
        _db.UpsertAktenStamm(neu);
        _gespeichert = neu;
        return true;
    }

    private void OnSpeichern(object sender, RoutedEventArgs e)
    {
        SpeichernFallsGeaendert();
        Laden();
        AuswertungText.Text = $"✓ Gespeichert um {DateTime.Now:HH:mm}.";
    }

    private void OnAuswerten(object sender, RoutedEventArgs e)
    {
        var n = _akte.AkteAuswerten(_mandantId);
        AuswertungText.Text = n > 0
            ? $"⏳ {n} Dokument(e) werden ausgewertet. Sie können weiterarbeiten; Fristen erscheinen als Vorschläge."
            : "Nichts zu tun.";
        AuswertenBtn.IsEnabled = false;
    }

    /// <summary>Gesetzt, wenn der Anwalt den Mandanten löschen will — das Hauptfenster fragt dann nach.</summary>
    public bool LoeschenAngefordert { get; private set; }

    private void OnLoeschen(object sender, RoutedEventArgs e)
    {
        LoeschenAngefordert = true;
        Close();
    }

    private void OnSchliessen(object sender, RoutedEventArgs e) => Close();
}
