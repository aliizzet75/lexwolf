using Microsoft.Web.WebView2.Core;
using WpfApplication = System.Windows.Application;

namespace LexWolf;

public partial class App : WpfApplication
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
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
