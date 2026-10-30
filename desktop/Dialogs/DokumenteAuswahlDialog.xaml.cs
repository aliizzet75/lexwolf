using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LexWolf.Database;
using LexWolf.Models;

namespace LexWolf.Dialogs;

/// <summary>
/// T#323: Einfacher Dialog zur direkten Auswahl von Dokumenten für die aktuelle Chat-Frage.
/// Die ausgewählten Dokumente werden zusätzlich zur automatischen Kontext-Bildung und zu den
/// per Rechtsklick priorisierten Pfaden an die KI übergeben.
/// </summary>
public partial class DokumenteAuswahlDialog : Window
{
    public sealed class DokumentAuswahl
    {
        public required string Titel { get; init; }
        public required string Text { get; init; }
        public required string Pfad { get; init; }
        public bool Ausgewaehlt { get; set; } = true;
        public bool IstVorlage { get; init; }
    }

    public IReadOnlyList<DokumentAuswahl> AusgewaehlteDokumente => _dokumente.Where(d => d.Ausgewaehlt).ToList();

    private readonly List<DokumentAuswahl> _dokumente;

    public DokumenteAuswahlDialog(LocalDb db, string mandantId, string mandantName, string? vorlagenPfad = null)
    {
        InitializeComponent();
        Title = $"Dokumente für {mandantName}";

        _dokumente = db.GetDokumenteForMandant(mandantId, maxDokumente: 60)
            .Where(d => d.Text.Length > 0 && !Path.GetFileName(d.Pfad).StartsWith("LexWolf_", StringComparison.OrdinalIgnoreCase))
            .Select(d => new DokumentAuswahl { Titel = d.Titel, Text = d.Text, Pfad = d.Pfad })
            .ToList();

        // T#323: Falls Vorlagen zur Auswahl stehen, diese mit anbieten. Der Pfad wurde
        // bereits vom Aufrufer mittels VorlagenOrdner.FindeVorlagenPfad aufgelöst.
        if (!string.IsNullOrEmpty(vorlagenPfad) && File.Exists(vorlagenPfad))
        {
            _dokumente.Insert(0, new DokumentAuswahl
            {
                Titel = $"Vorlage: {Path.GetFileNameWithoutExtension(vorlagenPfad)}",
                Text = File.ReadAllText(vorlagenPfad),
                Pfad = vorlagenPfad,
                IstVorlage = true,
            });
        }

        DokumenteListe.ItemsSource = _dokumente;
        LeerText.Visibility = _dokumente.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AktualisiereZaehler();
    }

    private void AktualisiereZaehler() =>
        ZaehlerText.Text = $"{_dokumente.Count(d => d.Ausgewaehlt)} von {_dokumente.Count} Dokumenten";

    private void OnDokumentGeaendert(object sender, RoutedEventArgs e) => AktualisiereZaehler();

    private void OnDokumentOeffnen(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is not DokumentAuswahl d) return;
        try { Services.DokumentExport.OeffneMitStandardprogramm(d.Pfad); }
        catch (Exception ex) { Debug.WriteLine($"[DokumenteAuswahlDialog] Öffnen fehlgeschlagen: {ex.Message}"); }
    }

    private void OnAlleUmschalten(object sender, RoutedEventArgs e)
    {
        var ziel = !_dokumente.All(d => d.Ausgewaehlt);
        foreach (var d in _dokumente) d.Ausgewaehlt = ziel;
        DokumenteListe.Items.Refresh();
        AktualisiereZaehler();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
