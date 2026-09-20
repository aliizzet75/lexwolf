using System.Windows;

namespace LexWolf.Dialogs;

/// <summary>
/// Zeigt die aggregierte KI-Zusammenfassung eines Mandanten an.
/// </summary>
public partial class ZusammenfassungDialog : Window
{
    public ZusammenfassungDialog(string text, string? mandantName)
    {
        InitializeComponent();
        HeaderText.Text = mandantName is null
            ? "Zusammenfassung"
            : $"Zusammenfassung — {mandantName}";
        ZusammenfassungText.Text = text;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
