using System.Collections.Generic;
using System.Windows;
using LexWolf.Controls;
using LexWolf.Database;

namespace LexWolf.Dialogs;

/// <summary>Dokumente und E-Mails in eine bestehende Akte legen. Die Übernahme selbst macht das
/// Hauptfenster (es kennt Scanner, Auswertung und Postfach).</summary>
public partial class DokumenteDialog : Window
{
    public DokumentAuswahl Dokumente => Auswahl;

    public DokumenteDialog(LocalDb db, string mandantName, IEnumerable<string>? vorbelegt = null)
    {
        InitializeComponent();
        FensterGroesse.AnSchriftAnpassen(this);
        UntertitelText.Text = mandantName;
        Auswahl.PostfachAnbieten(db);
        Auswahl.Geaendert += Aktualisieren;
        if (vorbelegt is not null) Auswahl.Hinzufuegen(vorbelegt);
        Aktualisieren();
    }

    private void Aktualisieren()
    {
        var n = Auswahl.Anzahl;
        UebernehmenBtn.IsEnabled = n > 0;
        UebernehmenBtn.Content = n > 0 ? $"In die Akte übernehmen ({n})" : "In die Akte übernehmen";
        StatusText.Text = n == 0 ? "Noch nichts ausgewählt." : n == 1 ? "1 Dokument ausgewählt." : $"{n} Dokumente ausgewählt.";
    }

    private void OnUebernehmen(object sender, RoutedEventArgs e) => DialogResult = true;
}
