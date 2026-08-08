using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LexWolf.Services;

public record UpdateInfo(string Version, string DownloadUrl, string Notes);

public class UpdateChecker
{
    private readonly string _backendUrl;
    private readonly HttpClient _http;

    public UpdateChecker(string backendUrl, HttpClient http)
    {
        _backendUrl = backendUrl.TrimEnd('/');
        _http = http;
    }

    public static Version CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var json = await _http.GetStringAsync($"{_backendUrl}/client/version");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var versionString = root.GetProperty("version").GetString() ?? "0.0.0";

            if (!Version.TryParse(versionString, out var latestVersion))
                return null;

            if (latestVersion <= CurrentVersion)
                return null;

            return new UpdateInfo(
                versionString,
                root.GetProperty("download_url").GetString() ?? "",
                root.TryGetProperty("notes", out var notes) ? notes.GetString() ?? "" : "");
        }
        catch
        {
            // Update-Check ist best-effort — Backend nicht erreichbar oder Endpoint fehlt
            // soll den Programmstart nicht beeinträchtigen.
            return null;
        }
    }

    /// <summary>
    /// Lädt den Installer in einen Temp-Pfad herunter — für den Silent-Self-Update
    /// (Installer wird danach mit /S ausgeführt statt dem Nutzer zum manuellen
    /// Ausführen im Browser-Download-Ordner überlassen zu werden).
    /// </summary>
    public async Task<string> DownloadInstallerAsync(string downloadUrl)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"LexWolf-Update-{Guid.NewGuid():N}.exe");
        using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using (var fs = File.Create(tempPath))
        {
            await response.Content.CopyToAsync(fs);
        }
        return tempPath;
    }
}
