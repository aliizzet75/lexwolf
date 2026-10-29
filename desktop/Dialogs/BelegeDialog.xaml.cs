using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>Task #321: separates kleines Fenster, das die geprüften Belege eines aus dem
/// Chat erzeugten Schriftsatzes anzeigt — gleiche Logik wie SchriftsatzDialog.BelegeListe,
/// aber als eigenständiges Dialogfenster statt rechter Spalte.</summary>
public partial class BelegeDialog : Window
{
    public BelegeDialog(string art, IReadOnlyList<SchriftsatzChatService.Beleg> belege)
    {
        InitializeComponent();
        UntertitelText.Text = $"{art} — {belege.Count} Zitat(e) im Entwurf.";

        var falsch = belege.Count(x => x.Status == "Dokument existiert nicht");
        var pruefen = belege.Count(x => x.Status != "geprüft" && x.Status != "Dokument existiert nicht");
        var geprueft = belege.Count - falsch - pruefen;

        GeprueftZahl.Text = $"✓ {geprueft} geprüft";
        PruefenZahl.Text = $"? {pruefen} prüfen";
        FalschZahl.Text = $"✗ {falsch} nicht vorhanden";

        if (belege.Count == 0)
        {
            BelegeListe.Children.Add(new TextBlock
            {
                Text = "Keine Zitate im Entwurf.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x8b, 0x94, 0x9e)),
                FontSize = 13,
                Margin = new Thickness(0, 6, 0, 0)
            });
            return;
        }

        foreach (var beleg in belege.OrderBy(x => x.Status == "Dokument existiert nicht" ? 0 : x.Status == "geprüft" ? 2 : 1))
        {
            BelegeListe.Children.Add(BuildBelegRow(beleg));
        }
    }

    private static UIElement BuildBelegRow(SchriftsatzChatService.Beleg beleg)
    {
        var (colorHex, symbol) = beleg.Status switch
        {
            "geprüft" => ("#3fb950", "✓"),
            "Dokument existiert nicht" => ("#f85149", "✗"),
            _ => ("#d29922", "?"),
        };

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = symbol, Foreground = brush, FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 6, 0) });
        header.Children.Add(new TextBlock { Text = beleg.Zitat, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });

        var body = new StackPanel();
        body.Children.Add(header);

        var statusText = beleg.Status switch
        {
            "geprüft" => string.IsNullOrEmpty(beleg.Hinweis) ? "geprüft" : $"geprüft ({beleg.Hinweis.Replace("gefunden: ", "")})",
            "Dokument existiert nicht" => "Dieses Dokument gibt es in der Akte nicht",
            "nicht in Datenbank" => "Gesetz nicht in der Datenbank — selbst prüfen",
            "nicht gefunden" => "nicht in der Datenbank gefunden — bitte prüfen",
            _ => beleg.Status,
        };

        body.Children.Add(new TextBlock
        {
            Text = statusText,
            Foreground = brush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20, 4, 0, 0)
        });

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0d, 0x11, 0x17)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3d)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 7),
            Child = body,
        };
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
