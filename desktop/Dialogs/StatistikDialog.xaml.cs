using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using LexWolf.Database;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>Ein Balken im Trend-Diagramm: eine Chat-Antwort mit ihrem Konfidenz-Score.</summary>
public sealed class TrendBalken
{
    public double BalkenHoehe { get; init; }
    public Brush Farbe { get; init; } = Brushes.Gray;
    public string Tooltip { get; init; } = "";
}

/// <summary>
/// Task #282: Einfache Statistik-Seite — zeigt die durchschnittliche Konfidenz der
/// letzten 20 Chat-Antworten sowie deren Trend als Balkendiagramm. Datenquelle ist
/// LocalDb.GetLetzteKonfidenzWerte, dieselbe Quelle, aus der auch das Konfidenz-Badge
/// im Chat (ChatHtmlRenderer.WrapAiBubble) seine Farbe ableitet.
/// </summary>
public partial class StatistikDialog : Window
{
    private const double MaxBalkenHoehe = 160;

    public StatistikDialog(LocalDb db)
    {
        InitializeComponent();

        var werte = db.GetLetzteKonfidenzWerte(20);
        if (werte.Count == 0)
        {
            DurchschnittText.Text = "—";
            AnzahlText.Text = "Noch keine Antworten mit Konfidenz-Score vorhanden.";
            return;
        }

        var durchschnitt = werte.Average(w => w.Score);
        var farbeName = ChatHtmlRenderer.ConfidenceColor(durchschnitt);

        DurchschnittText.Text = $"{durchschnitt * 100:F0}%";
        DurchschnittText.Foreground = FarbeFuer(farbeName);
        AnzahlText.Text = $"Basierend auf {werte.Count} Antwort(en).";

        var balken = werte.Select(w => new TrendBalken
        {
            BalkenHoehe = Math.Max(4, w.Score * MaxBalkenHoehe),
            Farbe = FarbeFuer(ChatHtmlRenderer.ConfidenceColor(w.Score)),
            Tooltip = $"{w.Timestamp:dd.MM.yyyy HH:mm} — {w.Score * 100:F0}%",
        }).ToList();

        TrendItems.ItemsSource = balken;
    }

    private static Brush FarbeFuer(string colorName) => colorName switch
    {
        "green" => new SolidColorBrush(Color.FromRgb(0x3f, 0xb9, 0x50)),
        "yellow" => new SolidColorBrush(Color.FromRgb(0xe8, 0xa8, 0x38)),
        _ => new SolidColorBrush(Color.FromRgb(0xff, 0x7b, 0x72)),
    };
}
