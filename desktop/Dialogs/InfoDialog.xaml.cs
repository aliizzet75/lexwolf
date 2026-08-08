using System;
using System.Net.Http;
using System.Windows;
using LexWolf.Services;

namespace LexWolf.Dialogs;

public partial class InfoDialog : Window
{
    private readonly UpdateChecker _checker;
    private UpdateInfo? _pendingUpdate;

    public InfoDialog(string backendUrl, HttpClient http)
    {
        InitializeComponent();
        _checker = new UpdateChecker(backendUrl, http);
        VersionText.Text = $"Version {UpdateChecker.CurrentVersion}";
    }

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        CheckBtn.IsEnabled = false;
        StatusText.Text = "Prüfe auf Updates...";
        NotesText.Visibility = Visibility.Collapsed;
        UpdateBtn.Visibility = Visibility.Collapsed;
        _pendingUpdate = null;

        var update = await _checker.CheckForUpdateAsync();
        CheckBtn.IsEnabled = true;

        if (update is null)
        {
            StatusText.Text = "Sie haben bereits die aktuelle Version.";
            return;
        }

        _pendingUpdate = update;
        StatusText.Text = $"Neue Version {update.Version} verfügbar.";
        if (!string.IsNullOrWhiteSpace(update.Notes))
        {
            NotesText.Text = update.Notes;
            NotesText.Visibility = Visibility.Visible;
        }
        UpdateBtn.Visibility = Visibility.Visible;
    }

    private async void OnApplyUpdate(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null) return;

        CheckBtn.IsEnabled = false;
        UpdateBtn.IsEnabled = false;
        UpdateProgressBar.Visibility = Visibility.Visible;

        var progress = new Progress<double>(percent =>
        {
            UpdateProgressBar.Value = percent;
            StatusText.Text = $"Update wird heruntergeladen... {percent:0}%";
        });

        try
        {
            await _checker.ApplyUpdateAsync(_pendingUpdate, progress);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this, $"Update fehlgeschlagen: {ex.Message}", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
            CheckBtn.IsEnabled = true;
            UpdateBtn.IsEnabled = true;
            UpdateProgressBar.Visibility = Visibility.Collapsed;
            return;
        }

        StatusText.Text = "Update wird installiert...";
        System.Windows.Application.Current.Shutdown();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
