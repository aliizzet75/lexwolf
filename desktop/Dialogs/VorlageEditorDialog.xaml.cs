using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>
/// Editor-Dialog für Vorlagen-Dateien (Task #306) — analog zum Entwurf-Editor in
/// SchriftsatzDialog. Erreichbar über "✏️ Bearbeiten" im Dateibaum-Kontextmenü oder per
/// Doppelklick auf eine Vorlagen-Datei (siehe MainWindow.VorlageBearbeiten).
/// </summary>
public partial class VorlageEditorDialog : Window
{
    private readonly string _pfad;

    public VorlageEditorDialog(string pfad)
    {
        InitializeComponent();
        FensterGroesse.AnSchriftAnpassen(this);
        _pfad = pfad;
        UntertitelText.Text = Path.GetFileName(pfad);

        try
        {
            InhaltBox.Text = VorlagenEditor.LeseText(pfad);
        }
        catch (Exception ex)
        {
            InhaltBox.IsEnabled = false;
            SpeichernBtn.IsEnabled = false;
            StatusText.Text = $"Datei konnte nicht gelesen werden: {ex.Message}";
        }
    }

    private void OnInhaltGeaendert(object sender, TextChangedEventArgs e) => StatusText.Text = "";

    private void OnSpeichern(object sender, RoutedEventArgs e)
    {
        try
        {
            VorlagenEditor.SchreibeText(_pfad, InhaltBox.Text);
            StatusText.Text = $"Gespeichert: {Path.GetFileName(_pfad)}";
        }
        catch (IOException ex)
        {
            MessageBox.Show(this,
                $"„{Path.GetFileName(_pfad)}“ konnte nicht gespeichert werden — ist die Datei noch in einem anderen Programm geöffnet?\n\n{ex.Message}",
                "Speichern", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(this, $"„{Path.GetFileName(_pfad)}“ konnte nicht gespeichert werden.\n\n{ex.Message}",
                "Speichern", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnSchliessen(object sender, RoutedEventArgs e) => Close();
}
