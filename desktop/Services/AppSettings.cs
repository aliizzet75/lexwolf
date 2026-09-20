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
