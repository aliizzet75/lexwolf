using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace Anonymisierer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            LoadConfig();

            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        private static void LoadConfig()
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(configPath)) return;
            try
            {
                var json = File.ReadAllText(configPath);
                var doc  = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("NerEndpoint", out var ep) &&
                    ep.GetString() is { Length: > 0 } url)
                {
                    Anonymizer.NerEndpoint = url;
                }
            }
            catch { }
        }
    }
}
