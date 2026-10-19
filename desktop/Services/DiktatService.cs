using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Utils;
using NAudio.Wave;
using Vosk;
using Whisper.net;

namespace LexWolf.Services;

/// <summary>
/// Spracheingabe für den Chat, komplett lokal — kein Ton verlässt den Rechner.
/// Zwei Stufen:
/// 1. Live: Vosk erkennt schon während der Aufnahme (Text erscheint sofort,
///    über <see cref="LiveTextChanged"/>), aufbereitet von <see cref="DiktatText"/>.
/// 2. Nachkorrektur: nach dem Stoppen schreibt Whisper die ganze Aufnahme
///    noch einmal sauber (Satzzeichen, Groß-/Kleinschreibung, Fachbegriffe) —
///    das dauert auf schwachen Rechnern einige Sekunden, läuft aber im
///    Hintergrund und ersetzt den Live-Text erst, wenn es fertig ist.
/// Modelle werden einmal geladen und wiederverwendet.
/// </summary>
public sealed class DiktatService : IDisposable
{
    // Begriffe, die Whisper ohne Vorgabe gern falsch schreibt. Der Prompt
    // wirkt wie "bisheriger Text" und zieht die Erkennung zu dieser Schreibweise.
    private const string FachPrompt =
        "Juristisches Diktat einer Anwaltskanzlei: § 242 BGB, ZPO, FamFG, StGB, " +
        "Zugewinnausgleich, Versorgungsausgleich, nachehelicher Unterhalt, Trennungsunterhalt, " +
        "Mandant, Gegenseite, Schriftsatz, Klageerwiderung.";

    private const string WhisperDownloadUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";
    private const string VoskDownloadUrl = "https://alphacephei.com/vosk/models/";
    private const int SampleRate = 16000;

    private readonly SemaphoreSlim _whisperLock = new(1, 1);
    private WhisperFactory? _whisper;
    private string? _whisperPath;
    private Model? _vosk;
    private string? _voskPath;

    private WaveInEvent? _waveIn;
    private MemoryStream? _buffer;
    private WaveFileWriter? _writer;
    private VoskRecognizer? _recognizer;
    private readonly List<string> _saetze = new();
    private string _partial = "";
    private TaskCompletionSource? _stopped;

    /// <summary>Bisher erkannter Text (fertige Sätze + laufender Teil). Kommt vom Audio-Thread.</summary>
    public event Action<string>? LiveTextChanged;

    public bool IsRecording => _waveIn is not null;

    // ------------------------------------------------------------ Großes Live-Modell

    private Model? _voskGross;

    /// <summary>Fortschritt beim Laden des großen Modells: (0..1, geschätzte Restzeit oder null). Beliebiger Thread.</summary>
    public event Action<double, TimeSpan?>? GrossLadeFortschritt;

    /// <summary>Großes Modell fertig (Fehler null) oder fehlgeschlagen; mit gemessener Ladezeit. Beliebiger Thread.</summary>
    public event Action<string?, TimeSpan>? GrossGeladen;

    public bool GrossBereit => _voskGross is not null;

    public static string VoskGrossPath(AppSettings s) => Path.Combine(s.VoskOrdner, s.VoskModellGross);
    public static bool VoskGrossExists(AppSettings s) =>
        !string.IsNullOrWhiteSpace(s.VoskModellGross) && Directory.Exists(VoskGrossPath(s));

    // Gemessen: 3,2 GB (vosk-model-de-0.21) in 5:08 min auf i5-7200U — davon nur ~1,5 min
    // Lesen, der Rest ist Aufbereitung. Für die erste Schätzung, bevor eine eigene Messung da ist.
    private const double SekundenProGb = 96;

    /// <summary>
    /// Lädt das große Modell im Hintergrund. Vosk meldet keinen Fortschritt, und die
    /// gelesenen Bytes taugen nicht als Maß (die Aufbereitung danach dauert länger als
    /// das Lesen) — geschätzt wird über die Zeit: Ladezeit des letzten Starts bzw. beim
    /// ersten Mal aus der Modellgröße. Höchstens 97 %, bis das Modell wirklich bereit ist;
    /// dauert es länger, meldet die Restzeit null ("länger als sonst").
    /// </summary>
    public Task GrossVorladenAsync(AppSettings settings)
    {
        if (_voskGross is not null || !VoskGrossExists(settings)) return Task.CompletedTask;
        var pfad = VoskGrossPath(settings);
        var erwartet = settings.VoskGrossLadezeitSek;
        return Task.Run(() =>
        {
            if (erwartet <= 0)
            {
                var gb = Directory.EnumerateFiles(pfad, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1e9;
                erwartet = Math.Max(20, gb * SekundenProGb);
            }
            var start = DateTime.UtcNow;
            using var timer = new Timer(_ =>
            {
                var verstrichen = (DateTime.UtcNow - start).TotalSeconds;
                var p = Math.Clamp(verstrichen / erwartet, 0, 0.97);
                TimeSpan? rest = verstrichen < erwartet ? TimeSpan.FromSeconds(Math.Max(5, erwartet - verstrichen)) : null;
                GrossLadeFortschritt?.Invoke(p, rest);
            }, null, 0, 1000);
            try
            {
                Vosk.Vosk.SetLogLevel(-1);
                var model = new Model(pfad);
                timer.Change(Timeout.Infinite, Timeout.Infinite);
                _voskGross = model;
                GrossLadeFortschritt?.Invoke(1, TimeSpan.Zero);
                GrossGeladen?.Invoke(null, DateTime.UtcNow - start);
            }
            catch (Exception ex)
            {
                timer.Change(Timeout.Infinite, Timeout.Infinite);
                GrossGeladen?.Invoke(ex.Message, DateTime.UtcNow - start);
            }
        });
    }

    public static string WhisperPath(AppSettings s) => Path.Combine(s.WhisperOrdner, s.WhisperModell);
    public static string VoskPath(AppSettings s) => Path.Combine(s.VoskOrdner, s.VoskModell);
    public static bool WhisperExists(AppSettings s) => File.Exists(WhisperPath(s));
    public static bool VoskExists(AppSettings s) => Directory.Exists(VoskPath(s));

    /// <summary>Lädt das Live-Modell (beim ersten Mal ein paar Sekunden) und startet die Aufnahme.</summary>
    public async Task StartRecordingAsync(AppSettings settings)
    {
        if (IsRecording) return;
        if (WaveInEvent.DeviceCount == 0)
            throw new InvalidOperationException("Kein Mikrofon gefunden.");

        // Großes Modell, sobald es im Hintergrund fertig geladen ist — sonst das kleine.
        var model = _voskGross;
        if (model is null)
        {
            var path = VoskPath(settings);
            if (_vosk is null || _voskPath != path)
            {
                Vosk.Vosk.SetLogLevel(-1);
                _vosk?.Dispose();
                _vosk = await Task.Run(() => new Model(path)).ConfigureAwait(true);
                _voskPath = path;
            }
            model = _vosk;
        }

        _recognizer = new VoskRecognizer(model, SampleRate);
        _saetze.Clear();
        _partial = "";
        _buffer = new MemoryStream();
        _waveIn = new WaveInEvent { WaveFormat = new WaveFormat(SampleRate, 16, 1), BufferMilliseconds = 100 };
        _writer = new WaveFileWriter(new IgnoreDisposeStream(_buffer), _waveIn.WaveFormat);
        _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _waveIn.DataAvailable += OnAudio;
        _waveIn.RecordingStopped += (_, _) => _stopped?.TrySetResult();
        _waveIn.StartRecording();
    }

    private void OnAudio(object? sender, WaveInEventArgs e)
    {
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        var rec = _recognizer;
        if (rec is null) return;
        lock (rec)
        {
            if (rec.AcceptWaveform(e.Buffer, e.BytesRecorded))
            {
                AddSatz(rec.Result());
                _partial = "";
            }
            else
            {
                var p = ReadJson(rec.PartialResult(), "partial");
                if (p == _partial) return;
                _partial = p;
            }
        }
        LiveTextChanged?.Invoke(CurrentText());
    }

    private void AddSatz(string json)
    {
        var text = ReadJson(json, "text");
        if (text.Length > 0) _saetze.Add(DiktatText.Satz(text));
    }

    private string CurrentText()
    {
        var teile = new List<string>(_saetze);
        if (_partial.Length > 0) teile.Add(DiktatText.Aufbereiten(_partial));
        return string.Join(' ', teile);
    }

    private static string ReadJson(string json, string key)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(key, out var v) ? (v.GetString() ?? "").Trim() : "";
    }

    /// <summary>Beendet die Aufnahme; liefert den fertigen Live-Text und die Aufnahme für die Nachkorrektur.</summary>
    public async Task<(string LiveText, MemoryStream? Audio)> StopRecordingAsync()
    {
        if (_waveIn is null || _buffer is null || _writer is null || _stopped is null || _recognizer is null)
            return ("", null);

        _waveIn.StopRecording();
        await _stopped.Task.ConfigureAwait(false);
        _waveIn.Dispose();
        _waveIn = null;
        _writer.Dispose(); // schreibt die WAV-Header-Längen
        _writer = null;

        string text;
        lock (_recognizer)
        {
            AddSatz(_recognizer.FinalResult());
            _partial = "";
            text = CurrentText();
        }
        _recognizer.Dispose();
        _recognizer = null;

        var audio = _buffer;
        _buffer = null;
        // Unter einer halben Sekunde ist es ein versehentlicher Klick.
        if (audio.Length < 44 + SampleRate)
        {
            audio.Dispose();
            return (text, null);
        }
        audio.Position = 0;
        return (text, audio);
    }

    /// <summary>Whisper-Nachkorrektur der ganzen Aufnahme ("" bei Stille). Gibt den Stream frei.</summary>
    public async Task<string> TranscribeAsync(AppSettings settings, MemoryStream audio, string? zusatzPrompt = null)
    {
        try
        {
            var factory = await GetWhisperAsync(WhisperPath(settings)).ConfigureAwait(false);
            var prompt = string.IsNullOrWhiteSpace(zusatzPrompt) ? FachPrompt : $"{FachPrompt} {zusatzPrompt}";
            await using var processor = factory.CreateBuilder()
                .WithLanguage("de")
                .WithThreads(Environment.ProcessorCount)
                .WithPrompt(prompt)
                .Build();

            var text = new System.Text.StringBuilder();
            await foreach (var segment in processor.ProcessAsync(audio).ConfigureAwait(false))
                text.Append(segment.Text);
            return text.ToString().Trim();
        }
        finally
        {
            audio.Dispose();
        }
    }

    /// <summary>Lädt Whisper im Hintergrund vor, damit die Nachkorrektur nicht auch noch laden muss.</summary>
    public Task PreloadWhisperAsync(AppSettings settings) =>
        WhisperExists(settings) ? GetWhisperAsync(WhisperPath(settings)) : Task.CompletedTask;

    private async Task<WhisperFactory> GetWhisperAsync(string path)
    {
        await _whisperLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_whisper is null || _whisperPath != path)
            {
                _whisper?.Dispose();
                _whisper = await Task.Run(() => WhisperFactory.FromPath(path)).ConfigureAwait(false);
                _whisperPath = path;
            }
            return _whisper;
        }
        finally
        {
            _whisperLock.Release();
        }
    }

    /// <summary>Lädt fehlende Modelle herunter (Vosk als ZIP, Whisper als Datei).</summary>
    public static async Task DownloadModelsAsync(AppSettings settings, bool whisper,
        IProgress<double>? progress, CancellationToken ct = default)
    {
        // Eigener Client: der gemeinsame der App bricht nach 120 s ab.
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var gesamt = whisper && !WhisperExists(settings) ? 2.0 : 1.0;

        if (!VoskExists(settings))
        {
            Directory.CreateDirectory(settings.VoskOrdner);
            var zip = VoskPath(settings) + ".zip";
            await DownloadAsync(http, VoskDownloadUrl + settings.VoskModell + ".zip", zip,
                new Progress<double>(p => progress?.Report(p / gesamt)), ct).ConfigureAwait(false);
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, settings.VoskOrdner, overwriteFiles: true), ct)
                .ConfigureAwait(false);
            File.Delete(zip);
        }

        if (whisper && !WhisperExists(settings))
        {
            Directory.CreateDirectory(settings.WhisperOrdner);
            await DownloadAsync(http, WhisperDownloadUrl + settings.WhisperModell, WhisperPath(settings),
                new Progress<double>(p => progress?.Report((gesamt - 1 + p) / gesamt)), ct).ConfigureAwait(false);
        }
    }

    // Erst .part, dann umbenannt — ein abgebrochener Download bleibt nicht als "fertig" liegen.
    private static async Task DownloadAsync(HttpClient http, string url, string target,
        IProgress<double> progress, CancellationToken ct)
    {
        var part = target + ".part";
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? -1;

        await using (var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var dst = File.Create(part))
        {
            var buf = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total > 0) progress.Report((double)done / total);
            }
        }
        File.Move(part, target, overwrite: true);
    }

    public void Dispose()
    {
        _waveIn?.Dispose();
        _writer?.Dispose();
        _buffer?.Dispose();
        _recognizer?.Dispose();
        _vosk?.Dispose();
        _voskGross?.Dispose();
        _whisper?.Dispose();
    }
}
