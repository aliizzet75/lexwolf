using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>Zeigt während einer laufenden Gesprächsaufnahme (Task #127) die verstrichene
/// Zeit und einen großen Stop-Knopf, und bleibt nach dem Stop sichtbar ("bitte warten")
/// bis Transkription (lokal, Whisper) UND Strukturierung (Backend, T#129) fertig sind —
/// schließt sich also nicht einfach kommentarlos, während im Hintergrund noch gearbeitet
/// wird. Schließen per X verhält sich wie der Stop-Knopf (die Aufnahme läuft nie unbemerkt
/// im Hintergrund weiter).</summary>
public partial class GespraechAufnahmeDialog : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _stopwatch = new();
    private readonly GespraechsRecorder _recorder;
    private readonly string _whisperModellPfad;
    private readonly HttpClient _http;
    private readonly string _backendUrl;
    private bool _wirdBeendet;

    /// <summary>Das Transkript nach dem Schließen, oder null bei Fehler/kein Text erkannt.</summary>
    public string? Transkript { get; private set; }

    /// <summary>Ergebnis von POST /conversation/analyze, oder null wenn Transkript leer
    /// war oder die Strukturierung fehlgeschlagen ist.</summary>
    public JsonElement? Strukturiert { get; private set; }

    /// <summary>Fehlermeldung aus Transkription oder Strukturierung, sonst null.</summary>
    public string? Fehler { get; private set; }

    public GespraechAufnahmeDialog(GespraechsRecorder recorder, string whisperModellPfad, HttpClient http, string backendUrl)
    {
        InitializeComponent();
        _recorder = recorder;
        _whisperModellPfad = whisperModellPfad;
        _http = http;
        _backendUrl = backendUrl;

        _timer.Tick += (_, _) => ZeitText.Text = _stopwatch.Elapsed.ToString(@"mm\:ss");
        Loaded += (_, _) =>
        {
            _stopwatch.Start();
            _timer.Start();
        };
        Closing += OnClosing;
    }

    private async void OnStop(object sender, RoutedEventArgs e) => await BeendenAsync();

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_wirdBeendet) return; // Transkription/Strukturierung bereits fertig — normal schließen lassen
        e.Cancel = true; // erst fertig verarbeiten, dann wirklich schließen
        await BeendenAsync();
    }

    private async Task BeendenAsync()
    {
        if (_wirdBeendet) return;
        _wirdBeendet = true;
        _timer.Stop();

        StopBtn.Visibility = Visibility.Collapsed;
        HinweisText.Visibility = Visibility.Collapsed;
        WartePanel.Visibility = Visibility.Visible;
        WarteText.Text = "Wird transkribiert — bitte warten...";

        try
        {
            Transkript = await _recorder.StoppAufnahmeAsync(_whisperModellPfad);
            if (!string.IsNullOrWhiteSpace(Transkript))
            {
                WarteText.Text = "Gespräch wird strukturiert — bitte warten...";
                Strukturiert = await VorschlaegeService.AnalysiereTranskriptAsync(_http, _backendUrl, Transkript);
            }
        }
        catch (Exception ex)
        {
            Fehler = ex.Message;
        }

        Close();
    }
}
