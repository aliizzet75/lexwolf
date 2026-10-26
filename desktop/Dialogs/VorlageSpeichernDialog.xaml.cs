using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>Name und Schriftsatz-Art für eine neue Vorlage (Task #307) — der Anwalt legt den
/// aktuellen Entwurfstext einer Entwurfskarte als wiederverwendbares Muster für einen
/// Schriftsatz-Typ ab. Schreibt die Datei nicht selbst (Aufrufer: MainWindow.
/// KarteAlsVorlageSpeichern); liefert nur den geprüften Typ und Dateinamen.</summary>
public partial class VorlageSpeichernDialog : Window
{
    private readonly string _dokumentePfad;

    public string GewaehlterTyp { get; private set; } = "";
    public string Name { get; private set; } = "";

    public VorlageSpeichernDialog(string dokumentePfad, string vorgeschlagenerTyp, string vorgeschlagenerName)
    {
        InitializeComponent();
        FensterGroesse.AnSchriftAnpassen(this);
        _dokumentePfad = dokumentePfad;

        ArtBox.ItemsSource = VorlagenOrdner.Arten;
        ArtBox.SelectedItem = VorlagenOrdner.Arten.Contains(vorgeschlagenerTyp) ? vorgeschlagenerTyp : VorlagenOrdner.Arten[0];
        NameBox.Text = vorgeschlagenerName;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
        Pruefen();
    }

    private void OnArtGeaendert(object sender, SelectionChangedEventArgs e) => Pruefen();
    private void OnNameGeaendert(object sender, TextChangedEventArgs e) => Pruefen();

    private void Pruefen()
    {
        if (ArtBox.SelectedItem is not string art) { SpeichernBtn.IsEnabled = false; return; }
        var fehler = VorlagenOrdner.PruefeVorlagenName(NameBox.Text, _dokumentePfad, art);
        FehlerText.Text = fehler ?? "";
        FehlerText.Visibility = fehler is null ? Visibility.Collapsed : Visibility.Visible;
        SpeichernBtn.IsEnabled = fehler is null;
    }

    private void OnSpeichern(object sender, RoutedEventArgs e)
    {
        if (ArtBox.SelectedItem is not string art) return;
        GewaehlterTyp = art;
        Name = NameBox.Text.Trim();
        DialogResult = true;
    }
}
