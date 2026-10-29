using System;
using System.Threading.Tasks;
using System.Windows;
using LexWolf.Services;
using Microsoft.Web.WebView2.Wpf;

namespace LexWolf.Dialogs;

/// <summary>Task #320: separates WPF-Fenster, das die Live-Suchschritte und den
/// anonymisiert gesendeten Text anzeigt — Inhalte, die früher direkt im Chat als
/// aufklappbares &lt;details class='such-panel'&gt; standen.
/// </summary>
public partial class RechercheErgebnisDialog : Window
{
    private readonly string _stepsHtml;
    private readonly bool _laufend;
    private readonly string _modus;
    private readonly int _gefundenCount;
    private readonly string? _anonymisiertGesendet;

    public RechercheErgebnisDialog(string stepsHtml, bool laufend, string modus, int gefundenCount,
        string? anonymisiertGesendet)
    {
        InitializeComponent();
        _stepsHtml = stepsHtml;
        _laufend = laufend;
        _modus = modus;
        _gefundenCount = gefundenCount;
        _anonymisiertGesendet = anonymisiertGesendet;

        var modusLabel = modus == "tief" ? "tief" : "schnell";
        if (laufend)
            StatusText.Text = $"🔎 Live-Suche läuft … (Modus: {modusLabel})";
        else if (gefundenCount > 0)
            StatusText.Text = $"✅ Suche abgeschlossen (Modus: {modusLabel}) — {gefundenCount} Quelle(n) gefunden";
        else
            StatusText.Text = $"✅ Suche abgeschlossen (Modus: {modusLabel}) — keine Quellen gefunden";

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            await WebView.EnsureCoreWebView2Async();
            var html = BuildHtml();
            WebView.NavigateToString(html);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RechercheErgebnisDialog] {ex.Message}");
        }
    }

    private string BuildHtml()
    {
        var content = ChatHtmlRenderer.BuildRechercheDialogHtml(
            _stepsHtml, _laufend, _modus, _gefundenCount, _anonymisiertGesendet);

        return $"<!DOCTYPE html>\n<html>\n<head>\n<meta charset='utf-8'>\n{ChatHtmlRenderer.GetChatCss()}\n</head>\n<body>\n{content}\n</body>\n</html>";
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
