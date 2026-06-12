using System;
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

            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
    }
}
