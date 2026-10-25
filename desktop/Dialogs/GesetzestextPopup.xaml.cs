using System.Windows;
using System.Windows.Input;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>
/// Task #80: Popup zu einer klickbaren Inline-Quellenangabe (z.B. "§ 23 KSchG") —
/// zeigt Gesetzestext, Fundstelle und Datum des aufgelösten Chunks. Schließbar per
/// X (Standard-Fenstertitelleiste) oder Esc (OnPreviewKeyDown).
/// </summary>
public partial class GesetzestextPopup : Window
{
    /// <summary>referenz = der im Chat angeklickte Text (z.B. "§ 23 KSchG"), auch wenn
    /// der Chunk nicht gefunden wurde — dann zeigt das Popup einen Hinweis statt Text.</summary>
    public GesetzestextPopup(string referenz, GesetzesChunk? chunk)
    {
        InitializeComponent();
        Title = $"LexWolf — {referenz}";
        TitelText.Text = referenz;

        if (chunk is null)
        {
            FundstelleText.Text = "Keine Quelle gefunden";
            DatumText.Text = "";
            GesetzestextText.Text = $"Zu „{referenz}“ wurde kein Gesetzestext in der Datenbank gefunden.";
            return;
        }

        FundstelleText.Text = chunk.Fundstelle;
        DatumText.Text = chunk.DatumAnzeige;
        GesetzestextText.Text = chunk.Text;
    }

    private void OnSchliessenClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
