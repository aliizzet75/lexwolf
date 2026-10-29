using System.Windows.Media;
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

    /// <summary>Setzt die Theme-Kernfarben der Oberfläche; alles mit DynamicResource passt sich sofort an.</summary>
    public static void SetzeTheme(bool dunkel)
    {
        if (Current is null) return;
        var t = LexWolf.Services.Theme.Aus(dunkel);
        Current.Resources["LwFensterHintergrund"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(t.Hintergrund));
        Current.Resources["LwVordergrund"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(t.Vordergrund));
        Current.Resources["LwOberflaeche"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(t.Oberflaeche));
        Current.Resources["LwAkzent"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(t.Akzent));
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
