using System.Collections.Generic;
using System.Windows;

namespace LexWolf.Dialogs;

/// <summary>Was heute wichtig ist (Fristen, Vorschläge, offene Eingänge) — bisher eine
/// dauerhafte Zeile über dem Chat, jetzt ein eigenes Fenster, das der Anwalt schließen
/// kann, damit der Chat den Platz bekommt.</summary>
public partial class TagesueberblickDialog : Window
{
    public bool ZuFristenGewaehlt { get; private set; }

    public TagesueberblickDialog(IEnumerable<string> punkte, bool mitFristenLink)
    {
        InitializeComponent();
        PunkteListe.ItemsSource = new List<string>(punkte);
        ZuFristenBtn.Visibility = mitFristenLink ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnZuFristenClick(object sender, RoutedEventArgs e)
    {
        ZuFristenGewaehlt = true;
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
