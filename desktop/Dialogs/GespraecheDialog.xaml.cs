using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LexWolf.Database;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>Zeigt alle aufgezeichneten Gespräche eines Mandanten vollständig (Zusammenfassung
/// UND komplettes Transkript, nicht nur die auf 300 Zeichen gekürzte Kurzfassung, die sonst
/// nur für den internen Chat-Kontext gedacht ist — siehe GespraechStruktur.AlsKurztext) und
/// erlaubt, einzelne Gespräche zu löschen (z.B. Fehlaufnahmen oder Tests).</summary>
public partial class GespraecheDialog : Window
{
    private readonly LocalDb _db;
    private readonly string _mandantId;

    public GespraecheDialog(LocalDb db, string mandantId, string mandantName)
    {
        InitializeComponent();
        _db = db;
        _mandantId = mandantId;
        UntertitelText.Text = mandantName;
        Laden();
    }

    private void Laden()
    {
        GespraechePanel.Children.Clear();
        var gespraeche = _db.GetGespraeche(_mandantId).OrderByDescending(g => g.Datum).ToList();
        LeerText.Visibility = gespraeche.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var g in gespraeche)
            GespraechePanel.Children.Add(ErzeugeKarte(g.Id, g.Datum, GespraechStruktur.Parse(g.StrukturiertJson)));
    }

    private Border ErzeugeKarte(string id, DateTime datum, GespraechStruktur struktur)
    {
        var loeschenBtn = new Button { Content = "🗑 Löschen", Style = (Style)FindResource("LwGefahrButton") };
        loeschenBtn.Click += (_, _) =>
        {
            if (System.Windows.MessageBox.Show(this, $"Gespräch vom {datum:dd.MM.yyyy HH:mm} endgültig löschen?",
                    "Gespräch löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            _db.DeleteGespraech(id);
            Laden();
        };

        var kopf = new DockPanel();
        kopf.Children.Add(new TextBlock
        {
            Text = $"📅 {datum:dd.MM.yyyy HH:mm}",
            Style = (Style)FindResource("LwAbschnitt"),
        });
        DockPanel.SetDock(loeschenBtn, Dock.Right);
        kopf.Children.Add(loeschenBtn);

        var inhalt = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (!string.IsNullOrWhiteSpace(struktur.Zusammenfassung))
        {
            inhalt.Children.Add(new TextBlock { Text = "Zusammenfassung", Style = (Style)FindResource("LwLabel") });
            inhalt.Children.Add(new TextBox
            {
                Text = struktur.Zusammenfassung,
                Style = (Style)FindResource("LwEditor"),
                IsReadOnly = true,
                MinHeight = 60,
                Margin = new Thickness(0, 0, 0, 10),
            });
        }
        inhalt.Children.Add(new TextBlock { Text = "Transkript (vollständig)", Style = (Style)FindResource("LwLabel") });
        inhalt.Children.Add(new TextBox
        {
            Text = struktur.Transkript,
            Style = (Style)FindResource("LwEditor"),
            IsReadOnly = true,
            MinHeight = 160,
        });

        var wrapper = new StackPanel();
        wrapper.Children.Add(kopf);
        wrapper.Children.Add(inhalt);
        return new Border { Style = (Style)FindResource("GespraechKarte"), Child = wrapper };
    }

    private void OnSchliessen(object sender, RoutedEventArgs e) => Close();
}
