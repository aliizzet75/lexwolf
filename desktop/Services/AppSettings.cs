using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
namespace LexWolf.Services;

public class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LexWolf", "settings.json");

    public string DokumentePfad { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "LexWolf", "Mandanten");

    public string Briefkopf { get; set; } = "";

    public string Briefende { get; set; } = "";

    public bool FeedbackOptIn { get; set; } = false;

    public bool AbTestOptIn { get; set; } = false;

    public string ChatInputFont { get; set; } = "Segoe UI";
    public double ChatInputFontSize { get; set; } = 13;
    public string ChatOutputFont { get; set; } = "Segoe UI";
    public double ChatOutputFontSize { get; set; } = 13;

    // Eigener Musterschriftsatz als Stilvorlage für Schriftsatz-Entwürfe (leer = keine)
    public string StilvorlagePfad { get; set; } = "";

    // Feiertage für die Fristberechnung (Kürzel, siehe FristRechner.Bundeslaender)
    public string Bundesland { get; set; } = "NW";
    // Neue Dokumente automatisch per KI auswerten (Posteingang, Fristvorschläge)
    public bool EingangAutoAuswertung { get; set; } = true;

    // E-Mail-Postfach (PostfachService, IMAP). Passwort per DPAPI nur für diesen
    // Windows-Benutzer verschlüsselt (PostfachService.Schuetzen/Entschuetzen).
    public bool PostfachAktiv { get; set; }
    public string PostfachServer { get; set; } = "";
    public int PostfachPort { get; set; } = 993;
    public bool PostfachSsl { get; set; } = true;
    public string PostfachBenutzer { get; set; } = "";
    public string PostfachPasswortGeschuetzt { get; set; } = "";
    public string PostfachOrdner { get; set; } = "INBOX";
    public int PostfachAbrufMinuten { get; set; } = 5;

    // Spracheingabe (DiktatService): Vosk für den Live-Text, Whisper für die
    // Nachkorrektur nach dem Stoppen (abschaltbar, z.B. auf sehr langsamen Rechnern).
    public string VoskOrdner { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LexWolf", "Vosk");
    public string VoskModell { get; set; } = "vosk-model-small-de-0.15";
    // Großes Live-Modell (deutlich bessere Erkennung, ~3 GB, lädt einige Minuten):
    // nur wenn in den Einstellungen eingeschaltet beim Start im Hintergrund geladen
    // (braucht viel Arbeitsspeicher/CPU); bis dahin bzw. sonst diktiert das kleine.
    // Neuer Name statt VoskGrossVorladen (war standardmäßig an): so ist es auch bei
    // bestehenden settings.json zunächst aus.
    public string VoskModellGross { get; set; } = "vosk-model-de-0.21";
    public bool VoskGrossBeimStart { get; set; }
    // Gemessene Ladezeit des letzten Starts — macht die Fortschrittsanzeige genauer (0 = unbekannt)
    public double VoskGrossLadezeitSek { get; set; }
    public bool DiktatNachkorrektur { get; set; } = true;
    public string WhisperOrdner { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LexWolf", "Whisper");
    public string WhisperModell { get; set; } = "ggml-small-q5_1.bin";

    [JsonIgnore]
    public static IReadOnlyList<string> AvailableFontFamilies { get; } = Fonts.SystemFontFamilies
        .Select(ff => ff.FamilyNames.Values.FirstOrDefault())
        .Where(name => !string.IsNullOrEmpty(name))
        .Cast<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    public void SanitizeFonts()
    {
        if (string.IsNullOrWhiteSpace(ChatInputFont) ||
            !AvailableFontFamilies.Contains(ChatInputFont, StringComparer.OrdinalIgnoreCase))
        {
            ChatInputFont = AvailableFontFamilies.FirstOrDefault() ?? "Segoe UI";
        }

        if (string.IsNullOrWhiteSpace(ChatOutputFont) ||
            !AvailableFontFamilies.Contains(ChatOutputFont, StringComparer.OrdinalIgnoreCase))
        {
            ChatOutputFont = AvailableFontFamilies.FirstOrDefault() ?? "Segoe UI";
        }

        if (double.IsNaN(ChatInputFontSize) || ChatInputFontSize < 8 || ChatInputFontSize > 32)
            ChatInputFontSize = 13;
        if (double.IsNaN(ChatOutputFontSize) || ChatOutputFontSize < 8 || ChatOutputFontSize > 32)
            ChatOutputFontSize = 13;
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is not null)
                {
                    settings.SanitizeFonts();
                    return settings;
                }
            }
        }
        catch { }
        var defaults = new AppSettings();
        defaults.SanitizeFonts();
        return defaults;
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
