using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LexWolf.Controls;
using LexWolf.Services;
using Microsoft.Win32;

namespace LexWolf.Dialogs;

public partial class SettingsDialog : Window
{
    /// <summary>Ein Modell aus dem Katalog von GET /api/llm-config (T#313), mit
    /// Kostenpunkten 1-10 zur Orientierung beim Testen.</summary>
    private sealed record KiModellEintrag(string Id, string Label, int Kosten)
    {
        public string Anzeige => $"{Label} (Kosten: {Kosten}/10)";
    }

    public AppSettings Settings { get; }

    private readonly string _backendUrl;
    private List<KiModellEintrag> _claudeKatalog = new();
    private List<KiModellEintrag> _ollamaKatalog = new();
    private bool _kiKonfigLaedt;
    private bool _kiApiKeyVorhanden;
    private bool _kiApiKeyLoeschen;
    private bool _laden;

    public SettingsDialog(AppSettings current, string backendUrl)
    {
        InitializeComponent();
        _laden = true;
        _backendUrl = backendUrl;
        Settings = new AppSettings
        {
            DokumentePfad = current.DokumentePfad,
            Briefkopf = current.Briefkopf,
            Briefende = current.Briefende,
            KanzleiOrt = current.KanzleiOrt,
            KanzleiKontodaten = current.KanzleiKontodaten,
            FeedbackOptIn = current.FeedbackOptIn,
            AbTestOptIn = current.AbTestOptIn,
            ChatInputFont = current.ChatInputFont,
            ChatInputFontSize = current.ChatInputFontSize,
            ChatOutputFont = current.ChatOutputFont,
            ChatOutputFontSize = current.ChatOutputFontSize,
            Bundesland = current.Bundesland,
            DunkelModus = current.DunkelModus,
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
        KanzleiOrtBox.Text = Settings.KanzleiOrt;
        KanzleiKontodatenBox.Text = Settings.KanzleiKontodaten;
        BundeslandBox.ItemsSource = FristRechner.Bundeslaender.Select(kv => new Land(kv.Key, kv.Value)).ToList();
        BundeslandBox.SelectedValue = Settings.Bundesland;
        FeedbackOptInBox.IsChecked = Settings.FeedbackOptIn;
        AbTestOptInBox.IsChecked = Settings.AbTestOptIn;
        VoskGrossBox.IsChecked = Settings.VoskGrossBeimStart;
        DunkelModusBox.IsChecked = Settings.DunkelModus;
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

        _laden = false;
        _ = LadeKiModellKonfigurationAsync();
    }

    // ── KI-Modell (Test, T#313) ─────────────────────────────────────────────

    private async Task LadeKiModellKonfigurationAsync()
    {
        try
        {
            using var http = new HttpClient();
            var antwort = await http.GetStringAsync($"{_backendUrl}/api/llm-config");
            using var doc = JsonDocument.Parse(antwort);
            var root = doc.RootElement;

            _claudeKatalog = LiesKatalog(root.GetProperty("kataloge").GetProperty("claude_code"));
            _ollamaKatalog = LiesKatalog(root.GetProperty("kataloge").GetProperty("ollama"));

            var config = root.GetProperty("config");
            _kiKonfigLaedt = true;
            try
            {
                var modus = config.GetProperty("modus").GetString() ?? "localhost";
                SelectByTag(KiModusBox, modus);
                SetzeModusSichtbarkeit(modus);

                var anbieter = config.GetProperty("ein_modell_anbieter").GetString() ?? "ollama";
                SelectByTag(KiAnbieterBox, anbieter);
                KiEinModellModellBox.ItemsSource = anbieter == "claude_code" ? _claudeKatalog : _ollamaKatalog;
                SelectById(KiEinModellModellBox, config.GetProperty("ein_modell").GetString() ?? "");

                KiHybridClaudeBox.ItemsSource = _claudeKatalog;
                SelectById(KiHybridClaudeBox, config.GetProperty("hybrid_claude_code_modell").GetString() ?? "");
                KiHybridOllamaBox.ItemsSource = _ollamaKatalog;
                SelectById(KiHybridOllamaBox, config.GetProperty("hybrid_ollama_modell").GetString() ?? "");

                _kiApiKeyVorhanden = config.GetProperty("hat_claude_api_key").GetBoolean();
                AktualisiereApiKeyStatus();
            }
            finally
            {
                _kiKonfigLaedt = false;
            }
        }
        catch (Exception ex)
        {
            KiStatusLabel.Text = $"KI-Modell-Konfiguration konnte nicht geladen werden: {ex.Message}";
            KiStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xf8, 0x51, 0x49));
        }
    }

    private static List<KiModellEintrag> LiesKatalog(JsonElement array) =>
        array.EnumerateArray()
            .Select(m => new KiModellEintrag(
                m.GetProperty("id").GetString() ?? "",
                m.GetProperty("label").GetString() ?? "",
                m.GetProperty("kosten").GetInt32()))
            .ToList();

    private static void SelectByTag(ComboBox box, string tag)
    {
        foreach (var obj in box.Items)
            if (obj is ComboBoxItem item && item.Tag as string == tag)
            {
                box.SelectedItem = item;
                return;
            }
        box.SelectedIndex = box.Items.Count > 0 ? 0 : -1;
    }

    private static void SelectById(ComboBox box, string id)
    {
        if (box.ItemsSource is IEnumerable<KiModellEintrag> eintraege)
        {
            box.SelectedItem = eintraege.FirstOrDefault(e => e.Id == id);
            if (box.SelectedItem is null && box.Items.Count > 0)
                box.SelectedIndex = 0;
        }
    }

    private void SetzeModusSichtbarkeit(string modus)
    {
        KiEinModellPanel.Visibility = modus == "ein_modell" ? Visibility.Visible : Visibility.Collapsed;
        KiHybridPanel.Visibility = modus == "hybrid" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AktualisiereApiKeyStatus()
    {
        KiApiKeyStatus.Text = _kiApiKeyVorhanden
            ? "Ein Key ist hinterlegt (geht direkt über die Anthropic-API). Feld leer lassen, um ihn zu behalten."
            : "Kein Key hinterlegt — läuft über den lokalen Claude-Code-Proxy.";
    }

    private void OnKiModusChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_kiKonfigLaedt) return;
        if (KiModusBox.SelectedItem is ComboBoxItem item && item.Tag is string modus)
            SetzeModusSichtbarkeit(modus);
    }

    private void OnKiAnbieterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_kiKonfigLaedt) return;
        if (KiAnbieterBox.SelectedItem is ComboBoxItem item && item.Tag as string == "claude_code")
            KiEinModellModellBox.ItemsSource = _claudeKatalog;
        else
            KiEinModellModellBox.ItemsSource = _ollamaKatalog;
        if (KiEinModellModellBox.Items.Count > 0)
            KiEinModellModellBox.SelectedIndex = 0;
    }

    private void OnKiApiKeyEntfernen(object sender, RoutedEventArgs e)
    {
        KiApiKeyBox.Password = "";
        _kiApiKeyLoeschen = true;
        _kiApiKeyVorhanden = false;
        AktualisiereApiKeyStatus();
    }

    private async void OnKiUebernehmen(object sender, RoutedEventArgs e)
    {
        KiUebernehmenBtn.IsEnabled = false;
        KiStatusLabel.Text = "Wird übernommen …";
        KiStatusLabel.Foreground = (SolidColorBrush)System.Windows.Application.Current.Resources["LwMuted"];
        try
        {
            var modus = (KiModusBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "localhost";
            var anbieter = (KiAnbieterBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "ollama";
            var payload = JsonSerializer.Serialize(new
            {
                modus,
                ein_modell_anbieter = anbieter,
                ein_modell = (KiEinModellModellBox.SelectedItem as KiModellEintrag)?.Id ?? "",
                hybrid_claude_code_modell = (KiHybridClaudeBox.SelectedItem as KiModellEintrag)?.Id ?? "",
                hybrid_ollama_modell = (KiHybridOllamaBox.SelectedItem as KiModellEintrag)?.Id ?? "",
                claude_api_key = KiApiKeyBox.Password,
                claude_api_key_loeschen = _kiApiKeyLoeschen,
            });

            using var http = new HttpClient();
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await http.PostAsync($"{_backendUrl}/api/llm-config", content);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            _kiApiKeyVorhanden = doc.RootElement.GetProperty("config").GetProperty("hat_claude_api_key").GetBoolean();
            _kiApiKeyLoeschen = false;
            KiApiKeyBox.Password = "";
            AktualisiereApiKeyStatus();

            KiStatusLabel.Text = "✓ Übernommen — wirkt sofort, auch ohne das Fenster zu schließen.";
            KiStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x3f, 0xb9, 0x50));
        }
        catch (Exception ex)
        {
            KiStatusLabel.Text = $"Fehlgeschlagen: {ex.Message}";
            KiStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xf8, 0x51, 0x49));
        }
        finally
        {
            KiUebernehmenBtn.IsEnabled = true;
        }
    }

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not ListBoxItem item) return;
        PanelAllgemein.Visibility = item.Tag as string == "allgemein" ? Visibility.Visible : Visibility.Collapsed;
        PanelDarstellung.Visibility = item.Tag as string == "darstellung" ? Visibility.Visible : Visibility.Collapsed;
        PanelSprache.Visibility = item.Tag as string == "sprache" ? Visibility.Visible : Visibility.Collapsed;
        PanelKiModell.Visibility = item.Tag as string == "ki_modell" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDunkelModusChanged(object sender, RoutedEventArgs e)
    {
        if (_laden || Settings is null) return;
        Settings.DunkelModus = DunkelModusBox.IsChecked == true;
        App.SetzeTheme(Settings.DunkelModus);
        Settings.Save();
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
        Settings.KanzleiOrt = KanzleiOrtBox.Text.Trim();
        Settings.KanzleiKontodaten = KanzleiKontodatenBox.Text.Trim();
        Settings.Bundesland = BundeslandBox.SelectedValue as string ?? Settings.Bundesland;
        Settings.FeedbackOptIn = FeedbackOptInBox.IsChecked == true;
        Settings.AbTestOptIn = AbTestOptInBox.IsChecked == true;
        Settings.VoskGrossBeimStart = VoskGrossBox.IsChecked == true;
        Settings.ChatInputFont = InputFontCombo.SelectedItem as string ?? "Segoe UI";
        Settings.ChatInputFontSize = InputFontSizeSlider.Value;
        Settings.ChatOutputFont = OutputFontCombo.SelectedItem as string ?? "Segoe UI";
        Settings.ChatOutputFontSize = OutputFontSizeSlider.Value;
        Settings.DunkelModus = DunkelModusBox.IsChecked == true;
        App.SetzeTheme(Settings.DunkelModus);
        Settings.Save();
        StatusLabel.Text       = "Gespeichert";
        StatusLabel.Visibility = Visibility.Visible;
        DialogResult           = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
