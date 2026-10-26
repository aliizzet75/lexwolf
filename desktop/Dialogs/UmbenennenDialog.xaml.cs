using System;
using System.Windows;
using System.Windows.Controls;

namespace LexWolf.Dialogs;

/// <summary>Kleiner Dialog zum Umbenennen einer Datei oder eines Unterordners im Dateibaum.
/// Verschoben/umbenannt wird vom Aufrufer — dieser Dialog liefert nur den geprüften neuen Namen.</summary>
public partial class UmbenennenDialog : Window
{
    private readonly string _ordner;
    private readonly string _aktuellerName;
    private readonly string _aktuellerPfad;

    /// <summary>Der vom Benutzer bestätigte neue Name (ohne Pfad), gesetzt wenn DialogResult == true.</summary>
    public string NeuerName { get; private set; } = "";

    public UmbenennenDialog(string aktuellerPfad, string aktuellerName, bool isFolder)
    {
        InitializeComponent();
        FensterGroesse.AnSchriftAnpassen(this);

        _aktuellerPfad = aktuellerPfad;
        _aktuellerName = aktuellerName;
        _ordner = System.IO.Path.GetDirectoryName(aktuellerPfad) ?? "";

        TitelText.Text = isFolder ? "✏️ Ordner umbenennen" : "✏️ Datei umbenennen";
        UntertitelText.Text = aktuellerName;
        NameBox.Text = aktuellerName;

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            var punkt = isFolder ? -1 : aktuellerName.LastIndexOf('.');
            NameBox.Select(0, punkt > 0 ? punkt : aktuellerName.Length);
        };
    }

    private void OnNameGeaendert(object sender, TextChangedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var fehler = Services.DateiVerwaltung.PruefeNeuenNamen(name, _ordner, _aktuellerPfad);
        if (fehler is not null)
        {
            FehlerText.Text = fehler;
            FehlerText.Visibility = Visibility.Visible;
            UmbenennenBtn.IsEnabled = false;
            return;
        }

        FehlerText.Visibility = Visibility.Collapsed;
        UmbenennenBtn.IsEnabled = !string.Equals(name, _aktuellerName, StringComparison.Ordinal);
    }

    private void OnUmbenennen(object sender, RoutedEventArgs e)
    {
        NeuerName = NameBox.Text.Trim();
        DialogResult = true;
    }
}
