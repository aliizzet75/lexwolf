using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LexWolf.Controls;
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
            FeedbackOptIn = current.FeedbackOptIn,
            AbTestOptIn = current.AbTestOptIn,
            ChatInputFont = current.ChatInputFont,
            ChatInputFontSize = current.ChatInputFontSize,
            ChatOutputFont = current.ChatOutputFont,
            ChatOutputFontSize = current.ChatOutputFontSize,
            Bundesland = current.Bundesland,
            StilvorlagePfad = current.StilvorlagePfad,
            PostfachAktiv = current.PostfachAktiv,
            PostfachServer = current.PostfachServer,
            PostfachPort = current.PostfachPort,
            PostfachSsl = current.PostfachSsl,
            PostfachBenutzer = current.PostfachBenutzer,
            PostfachPasswortGeschuetzt = current.PostfachPasswortGeschuetzt,
            PostfachOrdner = current.PostfachOrdner,
            PostfachAbrufMinuten = current.PostfachAbrufMinuten,
            EingangAutoAuswertung = current.EingangAutoAuswertung,
            VoskOrdner = current.VoskOrdner,
            VoskModell = current.VoskModell,
            VoskModellGross = current.VoskModellGross,
            VoskGrossBeimStart = current.VoskGrossBeimStart,
            VoskGrossLadezeitSek = current.VoskGrossLadezeitSek,
            DiktatNachkorrektur = current.DiktatNachkorrektur,
            WhisperOrdner = current.WhisperOrdner,
            WhisperModell = current.WhisperModell,
        };
        DokumentePfadBox.Text = Settings.DokumentePfad;
        BriefkopfBox.Text = Settings.Briefkopf;
        BriefendeBox.Text = Settings.Briefende;
        BundeslandBox.ItemsSource = FristRechner.Bundeslaender.Select(kv => new Land(kv.Key, kv.Value)).ToList();
        BundeslandBox.SelectedValue = Settings.Bundesland;
        FeedbackOptInBox.IsChecked = Settings.FeedbackOptIn;
        AbTestOptInBox.IsChecked = Settings.AbTestOptIn;
        VoskGrossBox.IsChecked = Settings.VoskGrossBeimStart;
        var grossVorhanden = DiktatService.VoskGrossExists(Settings);
        VoskGrossBox.IsEnabled = grossVorhanden;
        VoskGrossStatus.Text = grossVorhanden
            ? $"Modell gefunden: {DiktatService.VoskGrossPath(Settings)}"
            : $"Großes Modell nicht installiert (erwartet unter {DiktatService.VoskGrossPath(Settings)}) — es wird das kleine Modell verwendet.";

        var installedFonts = AppSettings.AvailableFontFamilies;
        InputFontCombo.ItemsSource = installedFonts;
        OutputFontCombo.ItemsSource = installedFonts;
        InputFontCombo.SelectedItem = installedFonts.Contains(Settings.ChatInputFont, StringComparer.OrdinalIgnoreCase) ? Settings.ChatInputFont : installedFonts.FirstOrDefault();
        OutputFontCombo.SelectedItem = installedFonts.Contains(Settings.ChatOutputFont, StringComparer.OrdinalIgnoreCase) ? Settings.ChatOutputFont : installedFonts.FirstOrDefault();
        InputFontCombo.SelectionChanged += OnInputFontChanged;
        OutputFontCombo.SelectionChanged += OnOutputFontChanged;

        InputFontSizeSlider.Value = Settings.ChatInputFontSize;
        OutputFontSizeSlider.Value = Settings.ChatOutputFontSize;
        InputFontSizeLabel.Text = $"Größe: {Settings.ChatInputFontSize:0} pt";
        OutputFontSizeLabel.Text = $"Größe: {Settings.ChatOutputFontSize:0} pt";
        UpdatePreview();

        NavList.SelectedIndex = 0;
    }

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not ListBoxItem item) return;
        PanelAllgemein.Visibility = item.Tag as string == "allgemein" ? Visibility.Visible : Visibility.Collapsed;
        PanelDarstellung.Visibility = item.Tag as string == "darstellung" ? Visibility.Visible : Visibility.Collapsed;
        PanelSprache.Visibility = item.Tag as string == "sprache" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnInputFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InputFontCombo.SelectedItem is string font)
        {
            Settings.ChatInputFont = font;
            UpdatePreview();
        }
    }

    private void OnOutputFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutputFontCombo.SelectedItem is string font)
        {
            Settings.ChatOutputFont = font;
            UpdatePreview();
        }
    }

    private void OnInputFontSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Feuert schon in InitializeComponent (Minimum="8" hebt den Wert von 0 an),
        // bevor Settings und die Labels existieren.
        if (Settings is null) return;
        Settings.ChatInputFontSize = InputFontSizeSlider.Value;
        InputFontSizeLabel.Text = $"Größe: {Settings.ChatInputFontSize:0} pt";
        UpdatePreview();
    }

    private void OnOutputFontSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Settings is null) return;
        Settings.ChatOutputFontSize = OutputFontSizeSlider.Value;
        OutputFontSizeLabel.Text = $"Größe: {Settings.ChatOutputFontSize:0} pt";
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        PreviewText.FontFamily = new FontFamily(Settings.ChatOutputFont);
        PreviewText.FontSize = Settings.ChatOutputFontSize;
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
        Settings.Bundesland = BundeslandBox.SelectedValue as string ?? Settings.Bundesland;
        Settings.FeedbackOptIn = FeedbackOptInBox.IsChecked == true;
        Settings.AbTestOptIn = AbTestOptInBox.IsChecked == true;
        Settings.VoskGrossBeimStart = VoskGrossBox.IsChecked == true;
        Settings.ChatInputFont = InputFontCombo.SelectedItem as string ?? "Segoe UI";
        Settings.ChatInputFontSize = InputFontSizeSlider.Value;
        Settings.ChatOutputFont = OutputFontCombo.SelectedItem as string ?? "Segoe UI";
        Settings.ChatOutputFontSize = OutputFontSizeSlider.Value;
        Settings.Save();
        StatusLabel.Text       = "Gespeichert";
        StatusLabel.Visibility = Visibility.Visible;
        DialogResult           = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
