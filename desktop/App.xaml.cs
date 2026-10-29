using Microsoft.Web.WebView2.Core;
using WpfApplication = System.Windows.Application;

namespace LexWolf;

public partial class App : WpfApplication
{
    /// <summary>Setzt die Schriftstufen der ganzen Oberfläche; alles mit DynamicResource passt sich sofort an.</summary>
    public static void SetzeSchriftgroesse(double grundgroesse)
    {
        if (Current is null) return;
        var s = LexWolf.Services.Schriftgroessen.Aus(grundgroesse);
        Current.Resources["LwSchriftKlein"] = s.Klein;
        Current.Resources["LwSchriftNormal"] = s.Normal;
        Current.Resources["LwSchriftGross"] = s.Gross;
        Current.Resources["LwSchriftTitel"] = s.Titel;
    }

    /// <summary>Setzt die vier Hauptfarben der Oberfläche; alles mit DynamicResource passt sich sofort an.</summary>
    public static void SetzeTheme(bool dunkel)
    {
        if (Current is null) return;
        var t = LexWolf.Services.Theme.Aus(dunkel);
        Current.Resources["LwFensterHintergrund"] = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(t.Hintergrund.R, t.Hintergrund.G, t.Hintergrund.B));
        Current.Resources["LwVordergrund"] = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(t.Vordergrund.R, t.Vordergrund.G, t.Vordergrund.B));
        Current.Resources["LwOberflaeche"] = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(t.Oberflaeche.R, t.Oberflaeche.G, t.Oberflaeche.B));
        Current.Resources["LwAkzent"] = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(t.Akzent.R, t.Akzent.G, t.Akzent.B));
    }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        // Ohne diesen Handler beendet JEDE nicht abgefangene Ausnahme auf dem UI-Thread
        // sofort die komplette App (WPF-Standardverhalten) — z.B. beobachtet beim
        // Umbenennen einer Datei, wenn der Datei-Scanner gleichzeitig auf die lokale DB
        // zugreift. Zeigt den Fehler stattdessen an und lässt LexWolf weiterlaufen.
        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(
                $"Unerwarteter Fehler — LexWolf läuft weiter, bitte die letzte Aktion prüfen:\n\n{args.Exception.Message}",
                "Unerwarteter Fehler", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            System.Diagnostics.Debug.WriteLine($"[App] WebView2 Runtime verfügbar: {version}");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            var result = System.Windows.MessageBox.Show(
                "LexWolf benötigt die Microsoft Edge WebView2 Runtime, die auf diesem PC nicht installiert ist.\n\n" +
                "Möchten Sie den WebView2-Installer jetzt herunterladen?",
                "WebView2 Runtime fehlt",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Information);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://go.microsoft.com/fwlink/p/?LinkId=2124703",
                    UseShellExecute = true
                });
            }

            Shutdown();
            return;
        }

        base.OnStartup(e);
    }
}
