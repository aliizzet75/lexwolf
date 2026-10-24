using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;
using Whisper.net;

namespace LexWolf.Services;

/// <summary>
/// Nimmt ein Mandantengespräch über das Mikrofon auf (NAudio) und transkribiert es
/// danach komplett lokal mit Whisper.net (whisper.cpp-Binding, Modell z.B. ggml-small
/// oder ggml-medium) — das Audio verlässt nie den Rechner und geht nicht an einen
/// Server. Die WAV-Datei liegt nur für die Dauer der Aufnahme als temporäre Datei vor
/// und wird unmittelbar nach der Transkription gelöscht, egal ob diese gelingt.
/// Bewusst ohne Abhängigkeit von AppSettings (WPF-Typ) — der Modellpfad wird vom
/// Aufrufer übergeben, z.B. <c>DiktatService.WhisperPath(settings)</c>, damit die
/// Transkriptionslogik auch außerhalb von WPF testbar ist.
/// </summary>
public sealed class GespraechsRecorder : IDisposable
{
    private const int SampleRate = 16000;

    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _wavPfad;
    private TaskCompletionSource? _gestoppt;
    private WhisperFactory? _whisper;
    private string? _whisperPfad;

    public bool IsAufnahme => _waveIn is not null;

    /// <summary>Startet die Mikrofonaufnahme in eine temporäre WAV-Datei.</summary>
    public void StartAufnahme()
    {
        if (IsAufnahme) throw new InvalidOperationException("Aufnahme läuft bereits.");
        if (WaveInEvent.DeviceCount == 0) throw new InvalidOperationException("Kein Mikrofon gefunden.");

        _wavPfad = Path.Combine(Path.GetTempPath(), $"lexwolf-gespraech-{Guid.NewGuid():N}.wav");
        _waveIn = new WaveInEvent { WaveFormat = new WaveFormat(SampleRate, 16, 1), BufferMilliseconds = 100 };
        _writer = new WaveFileWriter(_wavPfad, _waveIn.WaveFormat);
        _gestoppt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _waveIn.DataAvailable += OnAudio;
        _waveIn.RecordingStopped += (_, _) => _gestoppt?.TrySetResult();
        _waveIn.StartRecording();
    }

    private void OnAudio(object? sender, WaveInEventArgs e) => _writer?.Write(e.Buffer, 0, e.BytesRecorded);

    /// <summary>
    /// Beendet die Mikrofonaufnahme, transkribiert sie lokal mit Whisper (Deutsch) und
    /// liefert den Transkript-Text. Die temporäre WAV-Datei wird danach immer gelöscht
    /// (auch wenn die Transkription fehlschlägt) — das Audio wird nicht dauerhaft gespeichert.
    /// </summary>
    /// <param name="whisperModellPfad">Pfad zur ggml-Modelldatei (klein/medium), z.B. DiktatService.WhisperPath(settings).</param>
    public async Task<string> StoppAufnahmeAsync(string whisperModellPfad)
    {
        if (_waveIn is null || _writer is null || _wavPfad is null || _gestoppt is null)
            throw new InvalidOperationException("Keine Aufnahme aktiv.");

        _waveIn.StopRecording();
        await _gestoppt.Task.ConfigureAwait(false);
        _waveIn.Dispose();
        _waveIn = null;
        _writer.Dispose(); // schreibt die WAV-Header-Längen
        _writer = null;

        var wavPfad = _wavPfad;
        _wavPfad = null;
        _gestoppt = null;

        return await TranskribiereAsync(whisperModellPfad, wavPfad).ConfigureAwait(false);
    }

    /// <summary>
    /// Transkribiert eine WAV-Datei lokal mit Whisper (Sprache fest "de") und löscht sie
    /// danach — unabhängig von Erfolg oder Fehler. Eigenständig aufrufbar (auch testbar
    /// ohne Mikrofon), <see cref="StoppAufnahmeAsync"/> nutzt dieselbe Methode.
    /// </summary>
    public async Task<string> TranskribiereAsync(string whisperModellPfad, string wavPfad)
    {
        try
        {
            if (!File.Exists(whisperModellPfad))
                throw new FileNotFoundException("Whisper-Modell nicht gefunden.", whisperModellPfad);

            if (_whisper is null || _whisperPfad != whisperModellPfad)
            {
                _whisper?.Dispose();
                _whisper = await Task.Run(() => WhisperFactory.FromPath(whisperModellPfad)).ConfigureAwait(false);
                _whisperPfad = whisperModellPfad;
            }

            await using var processor = _whisper.CreateBuilder()
                .WithLanguage("de")
                .WithThreads(Environment.ProcessorCount)
                .Build();

            await using var audio = File.OpenRead(wavPfad);
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(audio).ConfigureAwait(false))
                text.Append(segment.Text);
            return text.ToString().Trim();
        }
        finally
        {
            // Audio verlässt nie den PC und wird auch nicht dauerhaft gespeichert.
            if (File.Exists(wavPfad)) File.Delete(wavPfad);
        }
    }

    public void Dispose()
    {
        _waveIn?.Dispose();
        _writer?.Dispose();
        _whisper?.Dispose();
        if (_wavPfad is not null && File.Exists(_wavPfad)) File.Delete(_wavPfad);
    }
}

/// <summary>Strukturierter Inhalt eines gespeicherten Mandantengesprächs (Task #130) — so
/// liegt es als JSON in LocalDb.InsertGespraech/GetGespraeche (Spalte strukturiert_json).
/// Tolerant beim Parsen (fehlende Felder = leer), analog zu DokumentAnalyse.Parse.</summary>
public sealed record GespraechStruktur(string Zusammenfassung, string Transkript)
{
    public static GespraechStruktur Parse(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var r = doc.RootElement;
            string S(string key) => r.TryGetProperty(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
                ? (v.GetString() ?? "").Trim() : "";
            return new GespraechStruktur(S("zusammenfassung"), S("transkript"));
        }
        catch (System.Text.Json.JsonException)
        {
            // Rohtext ohne JSON-Hülle (z.B. direkt das Whisper-Transkript) — als Transkript behandeln.
            return new GespraechStruktur("", json);
        }
    }

    /// <summary>Kurztext für Historie/Kontext: die Zusammenfassung, sonst (falls keine
    /// erzeugt wurde) die ersten 300 Zeichen des Rohtranskripts.</summary>
    public string AlsKurztext()
    {
        if (!string.IsNullOrWhiteSpace(Zusammenfassung)) return Zusammenfassung;
        if (Transkript.Length > 300) return Transkript[..300] + "...";
        return Transkript;
    }
}
