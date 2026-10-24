using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests;

/// <summary>
/// Akzeptanztest Task #127 (Whisper lokal, WPF/.NET-Stack). In diesem Testlauf (Linux,
/// kein Mikrofon/kein winmm.dll) kann die Mikrofonaufnahme selbst nicht geprüft werden —
/// StartAufnahme() ruft NAudios WaveInEvent.DeviceCount ab, was hier zuverlässig mit einer
/// Plattform-Exception fehlschlägt (erwartbar: die App läuft produktiv nur unter Windows,
/// TargetFramework net10.0-windows10.0.19041.0). Getestet wird deshalb genau der Teil, den
/// StoppAufnahmeAsync nach dem Stoppen ausführt und der die DoD-Punkte trägt: eine echte
/// WAV-Datei mit echter deutscher Sprache wird mit einem echten, lokal abgelegten
/// Whisper.net-Modell transkribiert (kein Server-Roundtrip, Sprache fest "de") und die
/// WAV-Datei wird danach gelöscht.
/// </summary>
public class GespraechsRecorderTests
{
    // ggml-tiny statt ggml-small/-medium (von der Aufgabe empfohlen) — nur damit der Test
    // in angemessener Zeit/Größe läuft (77 MB statt 466 MB/1,5 GB); Lade- und
    // Transkriptionscode in GespraechsRecorder ist für alle ggml-Modelle identisch.
    private const string ModellUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin";

    private static string ModellCachePfad => Path.Combine(Path.GetTempPath(), "lexwolf-test-whisper-cache", "ggml-tiny.bin");

    private static async Task<string> ModellSicherstellenAsync()
    {
        var pfad = ModellCachePfad;
        if (File.Exists(pfad)) return pfad;
        Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var bytes = await http.GetByteArrayAsync(ModellUrl);
        var teil = pfad + ".part";
        await File.WriteAllBytesAsync(teil, bytes);
        File.Move(teil, pfad, overwrite: true);
        return pfad;
    }

    [Fact]
    public async Task TranskribiereAsync_erkennt_deutschen_Text_komplett_lokal_und_loescht_wav_danach()
    {
        var modellPfad = await ModellSicherstellenAsync();

        var quelle = Path.Combine(AppContext.BaseDirectory, "test_de.wav");
        Assert.True(File.Exists(quelle), $"Test-Fixture fehlt: {quelle}");

        // Simuliert die temporäre Aufnahme-Datei, so wie StartAufnahme() sie anlegen würde.
        var wavPfad = Path.Combine(Path.GetTempPath(), $"test-gespraech-{Guid.NewGuid():N}.wav");
        File.Copy(quelle, wavPfad);
        Assert.True(File.Exists(wavPfad));

        using var recorder = new GespraechsRecorder();
        var text = await recorder.TranskribiereAsync(modellPfad, wavPfad);

        // DoD: WAV-Datei nach Transkription gelöscht — Audio wird nicht dauerhaft gespeichert.
        Assert.False(File.Exists(wavPfad));

        // DoD: Deutsch-Spracherkennung — die eingesprochene WAV enthält
        // "Guten Tag, hier ist ein Test für die Spracherkennung auf Deutsch."
        Assert.Contains("Test", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Deutsch", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TranskribiereAsync_loescht_wav_auch_wenn_Modell_fehlt()
    {
        var wavPfad = Path.Combine(Path.GetTempPath(), $"test-gespraech-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(wavPfad, new byte[] { 0x52, 0x49, 0x46, 0x46 }); // Inhalt irrelevant für diesen Test

        using var recorder = new GespraechsRecorder();
        var fehlendesModell = Path.Combine(Path.GetTempPath(), $"existiert-nicht-{Guid.NewGuid():N}.bin");

        await Assert.ThrowsAsync<FileNotFoundException>(() => recorder.TranskribiereAsync(fehlendesModell, wavPfad));

        Assert.False(File.Exists(wavPfad));
    }

    [Fact]
    public void StartAufnahme_ohne_Mikrofon_wirft_statt_stillschweigend_zu_scheitern()
    {
        // In diesem Testumfeld (kein Audiogerät) muss der Start zuverlässig fehlschlagen,
        // nicht z.B. eine leere/stumme Aufnahme erzeugen.
        using var recorder = new GespraechsRecorder();
        Assert.ThrowsAny<Exception>(() => recorder.StartAufnahme());
        Assert.False(recorder.IsAufnahme);
    }
}
