using System.Windows;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>Task #297: zeigt den Volltext einer zitierten Quelle (legal_chunks-Eintrag,
/// geladen über chunk_id aus quellen_check/T#296) und springt direkt zur Stelle, an
/// der das Zitat im Text vorkommt, statt nur den Anfang des Chunks zu zeigen.</summary>
public partial class QuelleDialog : Window
{
    private readonly string _text;
    private readonly string _zitat;

    public QuelleDialog(string titel, string text, string zitat)
    {
        InitializeComponent();
        _text = text;
        _zitat = zitat;
        TitelText.Text = titel;
        TextBox.Text = text;
        Loaded += (_, _) => SpringeZurStelle();
    }

    private void SpringeZurStelle()
    {
        var (start, length) = ChatHtmlRenderer.FindeZitatPosition(_text, _zitat);
        if (length <= 0) return;

        TextBox.Focus();
        TextBox.Select(start, length);
        var zeile = TextBox.GetLineIndexFromCharacterIndex(start);
        if (zeile >= 0) TextBox.ScrollToLine(zeile);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
