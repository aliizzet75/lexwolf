using System.Windows;
using System.Windows.Controls;
using LexWolf.Services;
using Microsoft.Win32;

namespace LexWolf.Dialogs;

public partial class SettingsDialog : Window
{
    public AppSettings Settings { get; }

    public SettingsDialog(AppSettings current)
    {
        InitializeComponent();
        Settings = new AppSettings
        {
            DokumentePfad = current.DokumentePfad,
            Briefkopf = current.Briefkopf,
            Briefende = current.Briefende,
        };
        DokumentePfadBox.Text = Settings.DokumentePfad;
        BriefkopfBox.Text = Settings.Briefkopf;
        BriefendeBox.Text = Settings.Briefende;
        NavList.SelectedIndex = 0;
    }

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        // Placeholder for future panels — all currently visible, just one panel
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title            = "Dokumente-Verzeichnis wählen",
            InitialDirectory = DokumentePfadBox.Text,
        };
        if (dlg.ShowDialog(this) == true)
            DokumentePfadBox.Text = dlg.FolderName;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Settings.DokumentePfad = DokumentePfadBox.Text.Trim();
        Settings.Briefkopf = BriefkopfBox.Text.Trim();
        Settings.Briefende = BriefendeBox.Text.Trim();
        Settings.Save();
        StatusLabel.Text       = "Gespeichert";
        StatusLabel.Visibility = Visibility.Visible;
        DialogResult           = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
