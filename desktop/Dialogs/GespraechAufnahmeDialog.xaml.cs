using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace LexWolf.Dialogs;

/// <summary>Zeigt während einer laufenden Gesprächsaufnahme (Task #127) die verstrichene
/// Zeit und einen großen Stop-Knopf. Rührt den Recorder selbst nicht an — der Aufrufer
/// (MainWindow) startet die Aufnahme, bevor dieser Dialog geöffnet wird, und beendet sie
/// erst, nachdem <see cref="Window.ShowDialog"/> zurückkehrt (per Stop-Knopf oder
/// Schließen des Fensters — beides beendet die Aufnahme, nie wird sie stillschweigend
/// weiterlaufen gelassen).</summary>
public partial class GespraechAufnahmeDialog : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = System.TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _stopwatch = new();

    public GespraechAufnahmeDialog()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => ZeitText.Text = _stopwatch.Elapsed.ToString(@"mm\:ss");
        Loaded += (_, _) =>
        {
            _stopwatch.Start();
            _timer.Start();
        };
        Closing += (_, _) =>
        {
            _timer.Stop();
            DialogResult ??= true;
        };
    }

    private void OnStop(object sender, RoutedEventArgs e) => DialogResult = true;
}
