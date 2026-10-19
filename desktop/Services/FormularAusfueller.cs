using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LexWolf.Database;

namespace LexWolf.Services;

/// <summary>
/// Liest den Fragebogen aus den Dokumenten der Akte aus (Backend /akte/formular-ausfuellen).
/// Merkt sich, welcher Dokumentenstand schon ausgelesen ist — neu gelesen wird nur, wenn
/// Dokumente dazugekommen sind oder sich geändert haben (oder auf ausdrücklichen Wunsch).
/// </summary>
public sealed class FormularAusfueller
{
    private const string StandSchluessel = "formular_ki_stand:";
    private readonly LocalDb _db;
    private readonly string _backendUrl;

    public FormularAusfueller(LocalDb db, string backendUrl)
    {
        _db = db;
        _backendUrl = backendUrl;
    }

    private List<(string Titel, string Pfad, string Text)> Dokumente(string mandantId) =>
        _db.GetDokumenteForMandant(mandantId, maxDokumente: 60)
            .Where(d => d.Text.Trim().Length > 0
                        // Eigene Dateien (Fragebogen-Vorlage, Entwürfe) enthalten unsere eigenen Werte.
                        && !Path.GetFileName(d.Pfad).StartsWith("LexWolf_", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string Stand(List<(string Titel, string Pfad, string Text)> dokumente) =>
        Formularwerte.Stand(dokumente.Select(d => (d.Pfad, d.Text.Length)));

    public bool HatDokumente(string mandantId) => Dokumente(mandantId).Count > 0;

    public bool IstAktuell(string mandantId)
    {
        var dok = Dokumente(mandantId);
        return dok.Count == 0 || _db.GetMeta(StandSchluessel + mandantId) == Stand(dok);
    }

    /// <summary>Werte aus der Akte; null, wenn es nichts zu lesen gibt.</summary>
    public async Task<Dictionary<string, (string Wert, string Quelle)>?> ErmittelnAsync(
        string mandantId, string mandantName, CancellationToken ct = default)
    {
        var dok = Dokumente(mandantId);
        if (dok.Count == 0) return null;
        var stand = Stand(dok);
        var payload = JsonSerializer.Serialize(new
        {
            mandant = mandantName,
            gegner = _db.GetAktenStamm(mandantId).Gegner,
            dokumente = dok.Select(d => new { titel = d.Titel, text = d.Text }),
        });
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(6) };
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await http.PostAsync($"{_backendUrl}/akte/formular-ausfuellen", content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var ergebnis = Formularwerte.AusKiAntwort(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), FragebogenFelder.Typen);
        _db.SetMeta(StandSchluessel + mandantId, stand);
        return ergebnis;
    }

    public Formularwerte LadeFragebogen(string mandantId) =>
        FragebogenFelder.Laden(_db.GetFormularFelder(mandantId, FragebogenFelder.Formular), _db.GetFragebogen(mandantId));

    /// <summary>Felder samt Herkunft speichern und den Fragebogen (für Rechner und Schriftsatz) mitführen.</summary>
    public void Speichern(string mandantId, Formularwerte werte)
    {
        _db.SaveFormularFelder(mandantId, FragebogenFelder.Formular, werte.Felder);
        _db.SaveFragebogen(mandantId, FragebogenFelder.ZuFragebogen(werte).ToJson());
    }
}
