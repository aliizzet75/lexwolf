using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace LexWolf.Services;

public sealed record ErkannteFrist(
    string Bezeichnung,
    string? FristartId,
    DateTime? EreignisDatum,
    DateTime? FristDatum,
    int? Anzahl,
    FristEinheit? Einheit,
    string? Zitat);

public sealed record AktenEreignis(DateTime Datum, string Beschreibung);

/// <summary>KI-Auswertung eines Dokuments (Backend /akte/analyse) — Grundlage für
/// Posteingang, Fristvorschläge und Aktenstamm.</summary>
public sealed record DokumentAnalyse(
    string Dokumenttyp,
    string Absender,
    DateTime? Datum,
    string Zusammenfassung,
    string Anliegen,
    string NaechsterSchritt,
    bool Eilig,
    IReadOnlyList<ErkannteFrist> Fristen,
    IReadOnlyList<(string Rolle, string Name)> Beteiligte,
    string Gericht,
    string Aktenzeichen,
    IReadOnlyList<AktenEreignis> Ereignisse)
{
    /// <summary>Liest die JSON-Antwort des Backends tolerant (fehlende Felder = leer).</summary>
    public static DokumentAnalyse Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;

        string S(JsonElement e, string key) =>
            e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";
        int? I(JsonElement e, string key) =>
            e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

        var fristen = new List<ErkannteFrist>();
        if (r.TryGetProperty("fristen", out var fa) && fa.ValueKind == JsonValueKind.Array)
            foreach (var f in fa.EnumerateArray())
            {
                FristEinheit? einheit = S(f, "einheit").ToLowerInvariant() switch
                {
                    "tage" or "tag" => FristEinheit.Tage,
                    "wochen" or "woche" => FristEinheit.Wochen,
                    "monate" or "monat" => FristEinheit.Monate,
                    _ => null,
                };
                fristen.Add(new ErkannteFrist(S(f, "bezeichnung"), NullWennLeer(S(f, "fristart_id")),
                    ParseDatum(S(f, "ereignis_datum")), ParseDatum(S(f, "frist_datum")),
                    I(f, "anzahl"), einheit, NullWennLeer(S(f, "zitat"))));
            }

        var beteiligte = new List<(string, string)>();
        if (r.TryGetProperty("beteiligte", out var ba) && ba.ValueKind == JsonValueKind.Array)
            foreach (var b in ba.EnumerateArray())
                if (S(b, "name").Length > 0)
                    beteiligte.Add((S(b, "rolle"), S(b, "name")));

        var ereignisse = new List<AktenEreignis>();
        if (r.TryGetProperty("ereignisse", out var ea) && ea.ValueKind == JsonValueKind.Array)
            foreach (var e in ea.EnumerateArray())
                if (ParseDatum(S(e, "datum")) is { } d && S(e, "beschreibung").Length > 0)
                    ereignisse.Add(new AktenEreignis(d, S(e, "beschreibung")));

        return new DokumentAnalyse(
            S(r, "dokumenttyp"), S(r, "absender"), ParseDatum(S(r, "datum")),
            S(r, "zusammenfassung"), S(r, "anliegen"), S(r, "naechster_schritt"),
            r.TryGetProperty("eilig", out var ei) && ei.ValueKind == JsonValueKind.True,
            fristen, beteiligte, S(r, "gericht"), S(r, "aktenzeichen"), ereignisse);
    }

    public static DateTime? ParseDatum(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        string[] formate = { "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-ddTHH:mm:ss" };
        return DateTime.TryParseExact(s.Trim(), formate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.Date : null;
    }

    private static string? NullWennLeer(string s) => s.Length == 0 ? null : s;
}

/// <summary>Aus einer erkannten Frist berechneter Vorschlag (noch vom Anwalt zu bestätigen).</summary>
public sealed record FristVorschlag(
    string Bezeichnung,
    string? FristartId,
    DateTime Ereignis,
    bool EreignisAngenommen,
    FristErgebnis Ergebnis,
    string Rechtsgrundlage,
    bool Notfrist);

public static class FristVorschlaege
{
    /// <summary>Rechnet erkannte Fristen in Vorschläge um. Fehlt das Zustelldatum,
    /// wird das Eingangsdatum der Datei angenommen und als "bitte prüfen" markiert —
    /// das tatsächliche Zustelldatum steht oft nur auf dem Umschlag bzw. im beA-Eingang.</summary>
    public static List<FristVorschlag> Erzeuge(IEnumerable<ErkannteFrist> fristen, DateTime eingangsdatum, string bundesland)
    {
        var liste = new List<FristVorschlag>();
        foreach (var f in fristen)
        {
            var art = FristRechner.FindeFristart(f.FristartId);
            var ereignis = f.EreignisDatum ?? eingangsdatum.Date;
            var angenommen = f.EreignisDatum is null;
            var name = string.IsNullOrWhiteSpace(f.Bezeichnung) ? art?.Name ?? "Frist" : f.Bezeichnung;

            if (f.FristDatum is { } datum)
            {
                liste.Add(new FristVorschlag(name, f.FristartId ?? "gesetzt", ereignis, false,
                    FristRechner.GesetzteFrist(datum, bundesland), "gesetzte Frist", false));
            }
            else if (art is not null)
            {
                liste.Add(new FristVorschlag(name, art.Id, ereignis, angenommen,
                    FristRechner.Berechne(ereignis, art, bundesland), art.Rechtsgrundlage, art.Notfrist));
            }
            else if (f.Anzahl is > 0 && f.Einheit is { } einheit)
            {
                liste.Add(new FristVorschlag(name, "frei", ereignis, angenommen,
                    FristRechner.Berechne(ereignis, f.Anzahl.Value, einheit, bundesland, einheit == FristEinheit.Monate ? 7 : 3),
                    "", false));
            }
        }
        return liste;
    }
}
