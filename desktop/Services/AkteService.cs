using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LexWolf.Database;

namespace LexWolf.Services;

/// <summary>
/// Posteingang und Akten-Auswertung: neue Dokumente im Mandantenordner werden
/// im Hintergrund nacheinander per KI ausgewertet (Backend /akte/analyse ->
/// ask_llm). Aus jeder Auswertung entstehen ein Eingang, Frist-Vorschläge
/// (vom Anwalt zu bestätigen) und Daten für den Aktenstamm.
///
/// Damit beim ersten Start nicht die gesamte bestehende Ablage durch die KI
/// geht, gilt nur als "neu", was nach dem ersten Start von LexWolf mit dieser
/// Funktion geändert wurde; ältere Akten lassen sich per "Akte auswerten" holen.
/// </summary>
public sealed class AkteService
{
    private const string BaselineKey = "eingang_baseline_utc";
    // Von LexWolf selbst erzeugte Dateien sind kein Eingang.
    private static readonly string[] EigeneDateien = { DokumentExport.EmlEntwurfPraefix, "LexWolf_" };

    private readonly LocalDb _db;
    private readonly HttpClient _http;
    private readonly string _backendUrl;
    private readonly Func<AppSettings> _settings;
    private readonly Channel<(string MandantId, string Pfad)> _queue = Channel.CreateUnbounded<(string, string)>();
    private readonly HashSet<string> _inQueue = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTime _baseline;
    private int _offen;

    /// <summary>Eingänge, Fristen oder Aktenstamm haben sich geändert (beliebiger Thread).</summary>
    public event Action? Geaendert;

    /// <summary>Anzahl Dokumente in Auswertung (beliebiger Thread).</summary>
    public event Action<int>? WarteschlangeGeaendert;

    public int InAuswertung => Volatile.Read(ref _offen);

    public AkteService(LocalDb db, HttpClient http, string backendUrl, Func<AppSettings> settings)
    {
        _db = db;
        _http = http;
        _backendUrl = backendUrl;
        _settings = settings;

        var gespeichert = _db.GetMeta(BaselineKey);
        if (gespeichert is null || !DateTime.TryParse(gespeichert, null, System.Globalization.DateTimeStyles.RoundtripKind, out _baseline))
        {
            _baseline = DateTime.UtcNow;
            _db.SetMeta(BaselineKey, _baseline.ToString("o"));
        }
        _ = Task.Run(VerarbeiteAsync);
    }

    /// <summary>Vom DokumentScanner nach dem Einlesen einer Datei aufgerufen.</summary>
    public void DateiIndexiert(string mandantId, string pfad)
    {
        if (!_settings().EingangAutoAuswertung || IstEigeneDatei(pfad)) return;
        try
        {
            if (File.GetLastWriteTimeUtc(pfad) <= _baseline || _db.HatAnalyse(pfad)) return;
        }
        catch { return; }
        Einreihen(mandantId, pfad);
    }

    /// <summary>Alle noch nicht ausgewerteten Dokumente eines Mandanten auswerten (Aktenstamm-Dialog).</summary>
    public int AkteAuswerten(string mandantId)
    {
        var n = 0;
        foreach (var (_, pfad, _) in _db.GetDokumenteForMandant(mandantId, maxDokumente: int.MaxValue))
            if (!IstEigeneDatei(pfad) && !_db.HatAnalyse(pfad) && Einreihen(mandantId, pfad))
                n++;
        return n;
    }

    /// <summary>Vom Anwalt ausdrücklich in die Akte gelegtes Dokument auswerten — unabhängig vom
    /// Dateidatum und von der Einstellung für automatische Auswertung.</summary>
    public bool Auswerten(string mandantId, string pfad) =>
        File.Exists(pfad) && !IstEigeneDatei(pfad) && DokumentScanner.IstLesbar(pfad)
        && !_db.HatAnalyse(pfad) && Einreihen(mandantId, pfad);

    public int NichtAusgewertet(string mandantId) =>
        _db.GetDokumenteForMandant(mandantId, maxDokumente: int.MaxValue)
            .Count(d => !IstEigeneDatei(d.Pfad) && !_db.HatAnalyse(d.Pfad));

    private static bool IstEigeneDatei(string pfad)
    {
        var name = Path.GetFileName(pfad);
        return EigeneDateien.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private bool Einreihen(string mandantId, string pfad)
    {
        lock (_inQueue)
            if (!_inQueue.Add(pfad)) return false;
        WarteschlangeGeaendert?.Invoke(Interlocked.Increment(ref _offen));
        _queue.Writer.TryWrite((mandantId, pfad));
        return true;
    }

    private async Task VerarbeiteAsync()
    {
        await foreach (var (mandantId, pfad) in _queue.Reader.ReadAllAsync())
        {
            try
            {
                await AuswertenAsync(mandantId, pfad).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AkteService] Auswertung {pfad} fehlgeschlagen: {ex.Message}");
            }
            finally
            {
                lock (_inQueue) _inQueue.Remove(pfad);
                WarteschlangeGeaendert?.Invoke(Interlocked.Decrement(ref _offen));
            }
        }
    }

    private async Task AuswertenAsync(string mandantId, string pfad)
    {
        if (!File.Exists(pfad)) return;
        var text = await Task.Run(() => DokumentScanner.LeseText(pfad)).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text)) return;

        var mandantName = MandantName(mandantId);
        // Vom Postfach abgelegte Mails/Anhänge tragen das Mail-Datum im Namen
        // ("2026-10-14_0931_…") — das ist der Eingang, nicht der Zeitpunkt des Abrufs.
        var eingang = DatumAusDateiname(pfad) ?? File.GetLastWriteTime(pfad);
        var payload = JsonSerializer.Serialize(new
        {
            titel = Path.GetFileName(pfad),
            text,
            eingangsdatum = eingang.ToString("yyyy-MM-dd"),
            mandant = mandantName,
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        // Die KI braucht pro Dokument teils über eine Minute — eigener langer Timeout.
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var response = await http.PostAsync($"{_backendUrl}/akte/analyse", content, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        var analyse = DokumentAnalyse.Parse(json);

        _db.UpsertAnalyse(pfad, mandantId, Path.GetFileNameWithoutExtension(pfad), json, eingang.ToUniversalTime());
        LegeFristvorschlaegeAn(mandantId, pfad, analyse, eingang);
        ErgaenzeAktenStamm(mandantId, analyse);
        Geaendert?.Invoke();
    }

    private void LegeFristvorschlaegeAn(string mandantId, string pfad, DokumentAnalyse a, DateTime eingang)
    {
        foreach (var v in FristVorschlaege.Erzeuge(a.Fristen, eingang, _settings().Bundesland))
        {
            if (_db.FristExistiert(pfad, v.Bezeichnung)) continue;
            var zitat = a.Fristen.FirstOrDefault(f => f.Bezeichnung == v.Bezeichnung)?.Zitat;
            _db.UpsertFrist(new FristEintrag(
                Guid.NewGuid().ToString("N"), mandantId, v.Bezeichnung, v.FristartId, v.Rechtsgrundlage,
                v.Ereignis, v.Ergebnis.Ende, v.Ergebnis.Vorfrist, v.Notfrist, "vorschlag", pfad,
                v.Ergebnis.Rechenweg, zitat is null ? "" : $"Aus dem Dokument: „{zitat}“", v.EreignisAngenommen));
        }
    }

    /// <summary>Füllt nur leere Felder — was der Anwalt selbst eingetragen hat, bleibt.</summary>
    private void ErgaenzeAktenStamm(string mandantId, DokumentAnalyse a)
    {
        var s = _db.GetAktenStamm(mandantId);
        string Erster(params string[] rollen) => a.Beteiligte
            .FirstOrDefault(b => rollen.Any(r => b.Rolle.Contains(r, StringComparison.OrdinalIgnoreCase))).Name ?? "";
        var neu = s with
        {
            Gericht = s.Gericht.Length > 0 ? s.Gericht : a.Gericht,
            Aktenzeichen = s.Aktenzeichen.Length > 0 ? s.Aktenzeichen : a.Aktenzeichen,
            Gegner = s.Gegner.Length > 0 ? s.Gegner : Erster("Gegner"),
            Gegnervertreter = s.Gegnervertreter.Length > 0 ? s.Gegnervertreter : Erster("Gegnervertreter"),
        };
        // "Gegnervertreter" enthält "Gegner" — nicht als Gegner selbst übernehmen
        if (neu.Gegner == neu.Gegnervertreter && neu.Gegner.Length > 0 && s.Gegner.Length == 0)
            neu = neu with { Gegner = a.Beteiligte.FirstOrDefault(b => b.Rolle.Equals("Gegner", StringComparison.OrdinalIgnoreCase)).Name ?? "" };
        if (neu != s) _db.UpsertAktenStamm(neu);
    }

    public static DateTime? DatumAusDateiname(string pfad)
    {
        var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(pfad), @"^(\d{4}-\d{2}-\d{2})_(\d{4})_");
        return m.Success && DateTime.TryParseExact(m.Groups[1].Value + m.Groups[2].Value, "yyyy-MM-ddHHmm",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
    }

    private string MandantName(string mandantId) =>
        _db.GetMandanten().FirstOrDefault(m => m.Id == mandantId).Name ?? "";

    // ------------------------------------------------------------ Aktenstamm, Beteiligte, Kollision

    /// <summary>Alle Beteiligten einer bzw. aller Akten: Aktenstamm (manuell) + Dokument-Auswertungen.</summary>
    public List<AktenBeteiligter> Beteiligte(string? nurMandantId = null)
    {
        var namen = _db.GetMandanten().ToDictionary(m => m.Id, m => m.Name);
        var liste = new List<AktenBeteiligter>();
        foreach (var s in _db.GetAlleAktenStaemme().Where(s => nurMandantId is null || s.MandantId == nurMandantId))
        {
            namen.TryGetValue(s.MandantId, out var mn);
            if (s.Gegner.Length > 0) liste.Add(new AktenBeteiligter(s.MandantId, mn ?? "", "Gegner", s.Gegner));
            if (s.Gegnervertreter.Length > 0) liste.Add(new AktenBeteiligter(s.MandantId, mn ?? "", "Gegnervertreter", s.Gegnervertreter));
        }
        foreach (var e in _db.GetEingaenge(nurMandantId, limit: 100000))
        {
            namen.TryGetValue(e.MandantId, out var mn);
            DokumentAnalyse a;
            try { a = DokumentAnalyse.Parse(e.Json); } catch { continue; }
            foreach (var (rolle, name) in a.Beteiligte)
                liste.Add(new AktenBeteiligter(e.MandantId, mn ?? "", rolle, name));
        }
        return liste
            .GroupBy(b => (b.MandantId, Kollisionspruefung.Normalisiere(b.Name), b.Rolle.ToLowerInvariant()))
            .Select(g => g.First())
            .ToList();
    }

    public List<Kollision> Kollisionen(string mandantId)
    {
        var mandanten = _db.GetMandanten();
        var name = mandanten.FirstOrDefault(m => m.Id == mandantId).Name ?? "";
        return Kollisionspruefung.Pruefe(mandantId, name, mandanten, Beteiligte());
    }

    public List<AktenEreignis> Zeitleiste(string mandantId)
    {
        var liste = new List<AktenEreignis>();
        foreach (var e in _db.GetEingaenge(mandantId, limit: 100000))
        {
            try { liste.AddRange(DokumentAnalyse.Parse(e.Json).Ereignisse); } catch { }
        }
        var ausgeblendet = _db.GetAusgeblendeteZeitleisteEintraege(mandantId);
        return liste
            .GroupBy(x => (x.Datum, x.Beschreibung.Trim().ToLowerInvariant()))
            .Select(g => g.First())
            .Where(x => !ausgeblendet.Contains((x.Datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.Beschreibung.Trim().ToLowerInvariant())))
            .OrderBy(x => x.Datum)
            .ToList();
    }

    // ------------------------------------------------------------ Kalender-Export

    /// <summary>iCalendar-Datei mit Fristende und Vorfrist als ganztägige Termine (öffnet sich in Outlook).</summary>
    public static string BaueIcs(FristEintrag f, string mandantName)
    {
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//LexWolf//Fristen//DE\r\nMETHOD:PUBLISH\r\n");
        void Termin(string uid, DateTime tag, string titel, int erinnerungTage)
        {
            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append($"UID:{uid}@lexwolf\r\n");
            sb.Append($"DTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}\r\n");
            sb.Append($"DTSTART;VALUE=DATE:{tag:yyyyMMdd}\r\n");
            sb.Append($"DTEND;VALUE=DATE:{tag.AddDays(1):yyyyMMdd}\r\n");
            sb.Append($"SUMMARY:{Esc(titel)}\r\n");
            sb.Append($"DESCRIPTION:{Esc($"{f.Bezeichnung} ({f.Rechtsgrundlage})\nMandant: {mandantName}\n\n{f.Rechenweg}")}\r\n");
            sb.Append("TRANSP:TRANSPARENT\r\n");
            sb.Append($"BEGIN:VALARM\r\nACTION:DISPLAY\r\nDESCRIPTION:{Esc(titel)}\r\nTRIGGER:-P{erinnerungTage}D\r\nEND:VALARM\r\n");
            sb.Append("END:VEVENT\r\n");
        }
        var prefix = f.Notfrist ? "NOTFRIST" : "Frist";
        Termin(f.Id + "-ende", f.Ende, $"⚠ {prefix}: {f.Bezeichnung} — {mandantName}", 1);
        if (f.Vorfrist < f.Ende)
            Termin(f.Id + "-vorfrist", f.Vorfrist, $"Vorfrist: {f.Bezeichnung} — {mandantName} (Ende {f.Ende:dd.MM.yyyy})", 0);
        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }
}
