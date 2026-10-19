using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LexWolf.Database;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>Was nach dem Dialog passieren soll: Unterlagen ablegen, Akte öffnen und ggf. den KI-Auftrag in den Chat geben.</summary>
public sealed record NeuesMandatErgebnis(string MandantId, string Name, string? KiAuftrag,
    LexWolf.Controls.DokumentAuswahl? Unterlagen = null);

/// <summary>
/// Neues Mandat: legt Mandantenordner und Akte an, prüft vorher auf Interessenkollision,
/// erfasst eine laufende Frist, legt Vollmacht und Aktenvermerk ab und gibt der KI den
/// Auftrag für Ersteinschätzung, Fragen, Unterlagenliste und Mandatsbestätigung.
/// </summary>
public partial class NeuesMandatDialog : Window
{
    private sealed record FristWahl(string Anzeige, Fristart? Art)
    {
        public override string ToString() => Anzeige;
    }

    private readonly LocalDb _db;
    private readonly AppSettings _settings;
    private readonly List<(string Id, string Name)> _mandanten;
    private readonly IReadOnlyList<AktenBeteiligter> _beteiligte;
    private List<Kollision> _kollisionen = new();
    private string? _bestehenderMandant;
    private FristErgebnis? _frist;

    public NeuesMandatErgebnis? Ergebnis { get; private set; }

    public NeuesMandatDialog(LocalDb db, AkteService akte, AppSettings settings, IEnumerable<string>? unterlagen = null)
    {
        InitializeComponent();
        FensterGroesse.AnSchriftAnpassen(this);
        _db = db;
        _settings = settings;
        // Einmal laden — die Prüfung läuft dann bei jedem Tastendruck nur im Speicher.
        _mandanten = db.GetMandanten();
        _beteiligte = akte.Beteiligte();

        RechtsgebietBox.ItemsSource = MandatAufnahme.Rechtsgebiete;
        RechtsgebietBox.SelectedIndex = 0;
        var arten = new List<FristWahl> { new("Vom Gericht, der Gegenseite oder Behörde gesetzte Frist", null) };
        arten.AddRange(FristRechner.Fristarten.Select(f => new FristWahl($"{f.Name} — {f.Dauer} ({f.Rechtsgrundlage})", f)));
        FristartBox.ItemsSource = arten;
        FristartBox.SelectedIndex = 0;

        Unterlagen.PostfachAnbieten(db);
        if (unterlagen is not null) Unterlagen.Hinzufuegen(unterlagen);
        Loaded += (_, _) => NameBox.Focus();
        Pruefen();
    }

    private MandatDaten Daten() => new(
        NameBox.Text.Trim(), TelefonBox.Text.Trim(), EmailBox.Text.Trim(), AnschriftBox.Text.Trim(),
        GegnerBox.Text.Trim(), GegnervertreterBox.Text.Trim(),
        RechtsgebietBox.SelectedItem as string ?? "", AnliegenBox.Text.Trim());

    private void OnEingabeGeaendert(object sender, RoutedEventArgs e) => Pruefen();

    /// <summary>Doppelter Mandant, Kollisionsprüfung und ob „Anlegen“ möglich ist.</summary>
    private void Pruefen()
    {
        if (!IsInitialized) return;
        var d = Daten();

        _bestehenderMandant = MandatAufnahme.BestehenderMandant(d.Name, _mandanten);
        BestehendHinweis.Visibility = _bestehenderMandant is null ? Visibility.Collapsed : Visibility.Visible;
        BestehendText.Text = _bestehenderMandant is null ? "" : $"„{_bestehenderMandant}“ ist schon Mandant. Neues Mandat trotzdem anlegen oder die bestehende Akte öffnen?";

        _kollisionen = d.Name.Length < 3 ? new() : MandatAufnahme.PruefeKollision(d, _mandanten, _beteiligte);
        KollisionWarnung.Visibility = _kollisionen.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        KollisionText.Text = string.Join("\n", _kollisionen.Select(k => $"• {k.Beschreibung} — Akte „{k.AndereAkte}“"));
        KollisionOk.Visibility = _kollisionen.Count == 0 && d.Name.Length >= 3 ? Visibility.Visible : Visibility.Collapsed;
        KollisionOkText.Text = d.Gegner.Length > 0
            ? $"✓ Keine Kollision: weder „{d.Name}“ noch „{d.Gegner}“ stehen in einer anderen Akte auf der Gegenseite."
            : $"✓ „{d.Name}“ ist in keiner anderen Akte Gegner. Gegner eintragen, um auch ihn zu prüfen.";

        var ordner = MandatAufnahme.OrdnerName(d.Name);
        var ordnerGibtEs = ordner.Length > 0 && Directory.Exists(Path.Combine(_settings.DokumentePfad, ordner));
        var kollisionOffen = _kollisionen.Count > 0 && KollisionGeprueftBox.IsChecked != true;
        AnlegenBtn.IsEnabled = ordner.Length > 0 && !ordnerGibtEs && !kollisionOffen;
        StatusText.Text = ordner.Length == 0 ? "Bitte den Namen des Mandanten eingeben."
            : ordnerGibtEs ? $"Einen Ordner „{ordner}“ gibt es schon — bitte den Namen unterscheidbar machen (z. B. mit Gegenstand)."
            : kollisionOffen ? "Bitte die mögliche Kollision prüfen und bestätigen."
            : $"Ordner „{ordner}“ wird angelegt.";
    }

    private void OnFristTextGeaendert(object sender, TextChangedEventArgs e) => FristBerechnen();
    private void OnFristGeaendert(object sender, RoutedEventArgs e) => FristBerechnen();

    private void FristBerechnen()
    {
        if (!IsInitialized) return;
        FristPanel.Visibility = FristBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        var art = (FristartBox.SelectedItem as FristWahl)?.Art;
        FristDatumLabel.Text = art is null ? "Frist läuft ab am" : $"{art.Ereignis} am";
        var datum = DatumEingabe.Parse(FristDatumBox.Text);
        _frist = FristBox.IsChecked != true || datum is null ? null
            : art is null ? FristRechner.GesetzteFrist(datum.Value, _settings.Bundesland)
            : FristRechner.Berechne(datum.Value, art, _settings.Bundesland);
        FristErgebnisKarte.Visibility = _frist is null ? Visibility.Collapsed : Visibility.Visible;
        if (_frist is null) return;
        FristEndeText.Text = $"Fristende: {DatumEingabe.Lang(_frist.Ende)}" + (art?.Notfrist == true ? " — NOTFRIST" : "") +
                             $"  ·  {DatumEingabe.Abstand(_frist.Ende)}";
        FristWegText.Text = _frist.Rechenweg + (art?.Hinweis is { } h ? $"\n{h}" : "");
    }

    private string? FristBeschreibung()
    {
        if (_frist is null) return null;
        var art = (FristartBox.SelectedItem as FristWahl)?.Art;
        var name = art?.Name ?? "Gesetzte Frist";
        return $"{name} bis {DatumEingabe.Lang(_frist.Ende)}" + (art?.Notfrist == true ? " (Notfrist)" : "");
    }

    private void OnBestehendeOeffnen(object sender, RoutedEventArgs e)
    {
        var m = _mandanten.FirstOrDefault(x => x.Name == _bestehenderMandant);
        if (m.Id is null) return;
        Ergebnis = new NeuesMandatErgebnis(m.Id, m.Name, null);
        DialogResult = true;
    }

    private void OnAnlegen(object sender, RoutedEventArgs e)
    {
        var d = Daten();
        var ordnerName = MandatAufnahme.OrdnerName(d.Name);
        if (!Directory.Exists(_settings.DokumentePfad))
        {
            MessageBox.Show(this, $"Das Aktenverzeichnis „{_settings.DokumentePfad}“ gibt es nicht. Bitte in den Einstellungen festlegen.",
                "Neues Mandat", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var jetzt = DateTime.Now;
            var ordner = Path.Combine(_settings.DokumentePfad, ordnerName);
            Directory.CreateDirectory(ordner);
            var id = DokumentScanner.MandantIdFuer(ordnerName);
            _db.UpsertMandant(id, ordnerName, ordnerName);
            var notiz = string.Join(": ", new[] { d.Rechtsgebiet, d.Anliegen }.Where(s => s.Length > 0));
            _db.UpsertAktenStamm(new AktenStamm(id, "", "", d.Gegner, d.Gegnervertreter, notiz));
            _db.SetMeta(MandatAufnahme.MetaSchluessel(id), MandatAufnahme.SpeicherJson(d, jetzt));

            if (_frist is not null)
            {
                var art = (FristartBox.SelectedItem as FristWahl)?.Art;
                _db.UpsertFrist(new FristEintrag(
                    Guid.NewGuid().ToString("N"), id, art?.Name ?? "Gesetzte Frist", art?.Id ?? "gesetzt",
                    art?.Rechtsgrundlage ?? "gesetzte Frist", DatumEingabe.Parse(FristDatumBox.Text) ?? jetzt.Date,
                    _frist.Ende, _frist.Vorfrist, art?.Notfrist ?? false, "offen", null, _frist.Rechenweg,
                    "Bei der Mandatsaufnahme erfasst.", false));
            }

            var frist = FristBeschreibung();
            if (VermerkBox.IsChecked == true)
                DokumentExport.SchreibeDocx(DokumentExport.FreierPfad(ordner, $"LexWolf_Mandatsaufnahme_{jetzt:yyyy-MM-dd}.docx"),
                    MandatAufnahme.Aktenvermerk(d, jetzt, _kollisionen, KollisionGeprueftBox.IsChecked == true, frist));
            if (VollmachtBox.IsChecked == true)
                DokumentExport.SchreibeDocx(DokumentExport.FreierPfad(ordner, $"LexWolf_Vollmacht_{jetzt:yyyy-MM-dd}.docx"),
                    MandatAufnahme.Vollmacht(d, _settings.Briefkopf));

            var unterlagen = Unterlagen.Dateien.Select(System.IO.Path.GetFileName)
                .Concat(Unterlagen.PostfachMails.Select(p => System.IO.Path.GetFileNameWithoutExtension(p.Pfad)))
                .OfType<string>().ToList();
            var ki = KiBox.IsChecked == true || BestaetigungBox.IsChecked == true
                ? MandatAufnahme.KiAuftrag(d, frist, KiBox.IsChecked == true, BestaetigungBox.IsChecked == true, unterlagen)
                : null;
            Ergebnis = new NeuesMandatErgebnis(id, ordnerName, ki, Unterlagen.Anzahl > 0 ? Unterlagen : null);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Das Mandat konnte nicht angelegt werden: {ex.Message}", "Neues Mandat",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
