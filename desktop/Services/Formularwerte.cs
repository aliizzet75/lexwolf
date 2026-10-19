using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LexWolf.Services;

/// <summary>Ein Formularfeld: Wert von LexWolf (aus den Akten-Dokumenten, mit Fundstelle) und
/// Eingabe des Anwalts. Anwalt = null: keine Eingabe; "" = bewusst geleert.</summary>
public sealed class FeldWert
{
    public string? Ki { get; set; }
    public string? KiQuelle { get; set; }
    public string? Anwalt { get; set; }
}

public enum Herkunft { Leer, LexWolf, Anwalt }

public enum FeldTyp { Betrag, Ganzzahl, Datum, JaNein, Text, Kinder }

/// <summary>
/// Werte eines Formulars (Fragebogen, Rechner) mit Herkunft. Angezeigt wird die Eingabe des
/// Anwalts, sonst der Wert von LexWolf. Neue Werte von LexWolf überschreiben nie, was der Anwalt
/// eingegeben hat. Alle Werte liegen in einer festen Schreibweise vor (Beträge "2450.5",
/// Datum "2026-10-14", Ja/Nein "true"/"false"), damit „gleich“ auch gleich ist.
/// </summary>
public sealed class Formularwerte
{
    public Dictionary<string, FeldWert> Felder { get; }

    public Formularwerte(Dictionary<string, FeldWert>? felder = null) =>
        Felder = felder ?? new Dictionary<string, FeldWert>(StringComparer.OrdinalIgnoreCase);

    private FeldWert Feld(string key)
    {
        if (!Felder.TryGetValue(key, out var f)) Felder[key] = f = new FeldWert();
        return f;
    }

    public string? Wert(string key) =>
        !Felder.TryGetValue(key, out var f) ? null
        : f.Anwalt is not null ? (f.Anwalt.Length == 0 ? null : f.Anwalt)
        : f.Ki;

    public Herkunft HerkunftVon(string key) =>
        !Felder.TryGetValue(key, out var f) ? Herkunft.Leer
        : f.Anwalt is not null ? (f.Anwalt.Length == 0 ? Herkunft.Leer : Herkunft.Anwalt)
        : f.Ki is not null ? Herkunft.LexWolf : Herkunft.Leer;

    public FeldWert? Info(string key) => Felder.TryGetValue(key, out var f) ? f : null;

    /// <summary>Eingabe des Anwalts (bereits in fester Schreibweise). Gleich dem LexWolf-Wert = keine eigene Eingabe.</summary>
    public void Eingabe(string key, string? wert)
    {
        var f = Feld(key);
        if (string.IsNullOrEmpty(wert)) f.Anwalt = f.Ki is not null ? "" : null;
        else f.Anwalt = wert == f.Ki ? null : wert;
    }

    /// <summary>Feld wieder mit dem Wert von LexWolf füllen.</summary>
    public bool LexWolfAusfuellen(string key)
    {
        if (!Felder.TryGetValue(key, out var f) || f.Ki is null || f.Anwalt is null) return false;
        f.Anwalt = null;
        return true;
    }

    /// <summary>Alle leeren Felder mit LexWolf-Werten füllen; Eingaben des Anwalts bleiben.</summary>
    public int LeereAusfuellen() => Felder.Keys.ToList().Count(k => HerkunftVon(k) == Herkunft.Leer && LexWolfAusfuellen(k));

    /// <summary>Neue Werte von LexWolf übernehmen. Gibt zurück, wie viele Felder jetzt einen
    /// sichtbaren LexWolf-Wert haben, der vorher fehlte oder anders war.</summary>
    public int KiUebernehmen(IReadOnlyDictionary<string, (string Wert, string Quelle)> neu)
    {
        var n = 0;
        foreach (var (key, (wert, quelle)) in neu)
        {
            var f = Feld(key);
            var vorher = Wert(key);
            f.Ki = wert;
            f.KiQuelle = quelle;
            if (f.Anwalt == wert) f.Anwalt = null;
            if (HerkunftVon(key) == Herkunft.LexWolf && vorher != wert) n++;
        }
        return n;
    }

    // ------------------------------------------------------------ Schreibweise

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Eingabe aus einem Feld in feste Schreibweise; null = leer/ungültig.</summary>
    public static string? AusEingabe(FeldTyp typ, string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return null;
        switch (typ)
        {
            case FeldTyp.Betrag:
                return Betrag(t) is { } d ? d.ToString("0.##", CultureInfo.InvariantCulture) : null;
            case FeldTyp.Ganzzahl:
                return int.TryParse(t, out var i) ? i.ToString(CultureInfo.InvariantCulture) : null;
            case FeldTyp.Datum:
                return DatumEingabe.Parse(t) is { } dt ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
            case FeldTyp.JaNein:
                return t.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true"
                    : t.Equals("false", StringComparison.OrdinalIgnoreCase) ? "false" : null;
            default:
                return t;
        }
    }

    /// <summary>Betrag in deutscher Schreibweise: "4.250" = 4250 (Tausenderpunkt), "4.250,50", "4250,5";
    /// ein Punkt mit nicht genau drei Ziffern dahinter gilt als Dezimalpunkt ("4.5" = 4,5).</summary>
    public static decimal? Betrag(string? text)
    {
        var t = (text ?? "").Replace("€", "").Replace(" ", "").Replace(" ", "").Trim();
        if (t.Length == 0) return null;
        if (t.Contains(','))
            t = t.Replace(".", "").Replace(",", ".");
        else if (t.Count(c => c == '.') > 1 || System.Text.RegularExpressions.Regex.IsMatch(t, @"^-?\d{1,3}\.\d{3}$"))
            t = t.Replace(".", "");
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>Feste Schreibweise für die Anzeige im Feld ("2450.5" → "2.450,5").</summary>
    public static string ZurAnzeige(FeldTyp typ, string? wert)
    {
        if (string.IsNullOrEmpty(wert)) return "";
        return typ switch
        {
            FeldTyp.Betrag when decimal.TryParse(wert, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d.ToString("#,##0.##", De),
            FeldTyp.Datum when DateTime.TryParseExact(wert, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) => DatumEingabe.Format(dt),
            _ => wert,
        };
    }

    /// <summary>Wert aus der KI-Antwort (JSON) in feste Schreibweise.</summary>
    public static string? AusJson(FeldTyp typ, JsonElement v) => typ switch
    {
        FeldTyp.Betrag or FeldTyp.Ganzzahl when v.ValueKind == JsonValueKind.Number =>
            AusEingabe(typ, typ == FeldTyp.Ganzzahl ? ((int)v.GetDecimal()).ToString(CultureInfo.InvariantCulture) : v.GetDecimal().ToString(CultureInfo.InvariantCulture)),
        FeldTyp.JaNein when v.ValueKind is JsonValueKind.True or JsonValueKind.False => v.GetBoolean() ? "true" : "false",
        FeldTyp.Kinder when v.ValueKind == JsonValueKind.Array => KinderAusJson(v),
        _ when v.ValueKind == JsonValueKind.String => typ == FeldTyp.Datum
            ? (DokumentAnalyse.ParseDatum(v.GetString()!) is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null)
            : AusEingabe(typ, v.GetString()),
        _ => null,
    };

    private static string? KinderAusJson(JsonElement arr)
    {
        var kinder = new List<FragebogenKind>();
        foreach (var k in arr.EnumerateArray())
        {
            var name = k.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : "";
            var geb = k.TryGetProperty("geburtsdatum", out var g) && g.ValueKind == JsonValueKind.String ? DokumentAnalyse.ParseDatum(g.GetString()!) : null;
            if (name.Length == 0 && geb is null) continue;
            var bei = !(k.TryGetProperty("lebt_bei_mandant", out var lb) && lb.ValueKind == JsonValueKind.False);
            kinder.Add(new FragebogenKind { Name = name, Geburtsdatum = geb, LebtBeiMandant = bei });
        }
        return kinder.Count == 0 ? null : KinderText(kinder);
    }

    public static string KinderText(IEnumerable<FragebogenKind> kinder) => JsonSerializer.Serialize(
        kinder.Select(k => new { name = k.Name, geburtsdatum = k.Geburtsdatum?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), lebt_bei_mandant = k.LebtBeiMandant }));

    public static List<FragebogenKind> KinderAus(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<FragebogenKind>();
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Array ? ParseKinder(doc.RootElement) : new List<FragebogenKind>();
        }
        catch (JsonException) { return new List<FragebogenKind>(); }
    }

    private static List<FragebogenKind> ParseKinder(JsonElement arr) => arr.EnumerateArray().Where(k => k.ValueKind == JsonValueKind.Object).Select(k => new FragebogenKind
    {
        Name = k.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "",
        Geburtsdatum = k.TryGetProperty("geburtsdatum", out var g) && g.ValueKind == JsonValueKind.String ? DokumentAnalyse.ParseDatum(g.GetString()!) : null,
        LebtBeiMandant = !(k.TryGetProperty("lebt_bei_mandant", out var lb) && lb.ValueKind == JsonValueKind.False),
    }).ToList();

    /// <summary>Antwort von /akte/formular-ausfuellen: {"felder": {"netto_mandant": {"wert": 2450, "quelle": "…", "zitat": "…"}}}.
    /// Die Quelle für den Tooltip ist „Dokument — „Zitat““.</summary>
    public static Dictionary<string, (string Wert, string Quelle)> AusKiAntwort(string json, IReadOnlyDictionary<string, FeldTyp> typen)
    {
        var ergebnis = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("felder", out var felder) || felder.ValueKind != JsonValueKind.Object) return ergebnis;
        foreach (var p in felder.EnumerateObject())
        {
            if (!typen.TryGetValue(p.Name, out var typ) || p.Value.ValueKind != JsonValueKind.Object) continue;
            if (!p.Value.TryGetProperty("wert", out var w) || AusJson(typ, w) is not { } wert) continue;
            var dok = p.Value.TryGetProperty("quelle", out var q) && q.ValueKind == JsonValueKind.String ? q.GetString() ?? "" : "";
            var zitat = p.Value.TryGetProperty("zitat", out var z) && z.ValueKind == JsonValueKind.String ? z.GetString() ?? "" : "";
            ergebnis[p.Name] = (wert, zitat.Length > 0 ? $"{dok} — „{zitat}“" : dok);
        }
        return ergebnis;
    }

    /// <summary>Flaches JSON von /akte/fragebogen-lesen ({"netto_mandant": 2450, …}), Quelle = Dateiname.</summary>
    public static Dictionary<string, (string Wert, string Quelle)> AusFlacherAntwort(string json, IReadOnlyDictionary<string, FeldTyp> typen, string quelle)
    {
        var ergebnis = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        foreach (var p in doc.RootElement.EnumerateObject())
            if (typen.TryGetValue(p.Name, out var typ) && AusJson(typ, p.Value) is { } wert)
                ergebnis[p.Name] = (wert, quelle);
        return ergebnis;
    }

    /// <summary>Kennzeichen der Akten-Dokumente: ändert es sich, lohnt sich neues Auslesen.</summary>
    public static string Stand(IEnumerable<(string Pfad, int Laenge)> dokumente)
    {
        var s = string.Join("|", dokumente.OrderBy(d => d.Pfad, StringComparer.OrdinalIgnoreCase).Select(d => $"{d.Pfad}:{d.Laenge}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..16];
    }
}

/// <summary>Die Felder des Mandanten-Fragebogens und die Umrechnung von/zu <see cref="Fragebogen"/>.</summary>
public static class FragebogenFelder
{
    public const string Formular = "fragebogen";

    public static readonly IReadOnlyDictionary<string, FeldTyp> Typen = new Dictionary<string, FeldTyp>(StringComparer.OrdinalIgnoreCase)
    {
        ["mandant_ist_unterhaltspflichtig"] = FeldTyp.JaNein,
        ["netto_mandant"] = FeldTyp.Betrag, ["netto_gegenseite"] = FeldTyp.Betrag,
        ["mandant_erwerbstaetig"] = FeldTyp.JaNein, ["gegenseite_erwerbstaetig"] = FeldTyp.JaNein,
        ["abzuege_mandant"] = FeldTyp.Betrag, ["abzuege_gegenseite"] = FeldTyp.Betrag,
        ["kinder"] = FeldTyp.Kinder,
        ["heiratsdatum"] = FeldTyp.Datum, ["trennungsdatum"] = FeldTyp.Datum, ["zustellung_scheidungsantrag"] = FeldTyp.Datum,
        ["anfangsvermoegen_mandant"] = FeldTyp.Betrag, ["privilegiert_mandant"] = FeldTyp.Betrag, ["endvermoegen_mandant"] = FeldTyp.Betrag,
        ["anfangsvermoegen_gegenseite"] = FeldTyp.Betrag, ["privilegiert_gegenseite"] = FeldTyp.Betrag, ["endvermoegen_gegenseite"] = FeldTyp.Betrag,
        ["anzahl_rentenanrechte"] = FeldTyp.Ganzzahl,
        ["wohnsituation"] = FeldTyp.Text, ["bemerkungen"] = FeldTyp.Text,
    };

    private static string? B(decimal? d) => d?.ToString("0.##", CultureInfo.InvariantCulture);
    private static string? D(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string J(bool b) => b ? "true" : "false";
    private static string? T(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Werte eines Fragebogens als Felder (feste Schreibweise).</summary>
    public static Dictionary<string, string?> Aus(Fragebogen f) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["mandant_ist_unterhaltspflichtig"] = J(f.MandantIstUnterhaltspflichtig),
        ["netto_mandant"] = B(f.NettoMandant), ["netto_gegenseite"] = B(f.NettoGegenseite),
        ["mandant_erwerbstaetig"] = J(f.MandantErwerbstaetig), ["gegenseite_erwerbstaetig"] = J(f.GegenseiteErwerbstaetig),
        ["abzuege_mandant"] = B(f.AbzuegeMandant), ["abzuege_gegenseite"] = B(f.AbzuegeGegenseite),
        ["kinder"] = f.Kinder.Count == 0 ? null : Formularwerte.KinderText(f.Kinder),
        ["heiratsdatum"] = D(f.Heiratsdatum), ["trennungsdatum"] = D(f.Trennungsdatum), ["zustellung_scheidungsantrag"] = D(f.ZustellungScheidungsantrag),
        ["anfangsvermoegen_mandant"] = B(f.AnfangsvermoegenMandant), ["privilegiert_mandant"] = B(f.PrivilegiertMandant), ["endvermoegen_mandant"] = B(f.EndvermoegenMandant),
        ["anfangsvermoegen_gegenseite"] = B(f.AnfangsvermoegenGegenseite), ["privilegiert_gegenseite"] = B(f.PrivilegiertGegenseite), ["endvermoegen_gegenseite"] = B(f.EndvermoegenGegenseite),
        ["anzahl_rentenanrechte"] = f.AnzahlRentenanrechte?.ToString(CultureInfo.InvariantCulture),
        ["wohnsituation"] = T(f.Wohnsituation), ["bemerkungen"] = T(f.Bemerkungen),
    };

    /// <summary>Fragebogen aus den angezeigten Werten (Anwalt vor LexWolf).</summary>
    public static Fragebogen ZuFragebogen(Formularwerte w)
    {
        decimal? Z(string k) => decimal.TryParse(w.Wert(k), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
        DateTime? Dt(string k) => DateTime.TryParseExact(w.Wert(k), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        bool Jn(string k, bool standard) => w.Wert(k) is { } s ? s == "true" : standard;
        return new Fragebogen
        {
            MandantIstUnterhaltspflichtig = Jn("mandant_ist_unterhaltspflichtig", false),
            NettoMandant = Z("netto_mandant"), NettoGegenseite = Z("netto_gegenseite"),
            MandantErwerbstaetig = Jn("mandant_erwerbstaetig", true), GegenseiteErwerbstaetig = Jn("gegenseite_erwerbstaetig", true),
            AbzuegeMandant = Z("abzuege_mandant"), AbzuegeGegenseite = Z("abzuege_gegenseite"),
            Kinder = Formularwerte.KinderAus(w.Wert("kinder")),
            Heiratsdatum = Dt("heiratsdatum"), Trennungsdatum = Dt("trennungsdatum"), ZustellungScheidungsantrag = Dt("zustellung_scheidungsantrag"),
            AnfangsvermoegenMandant = Z("anfangsvermoegen_mandant"), PrivilegiertMandant = Z("privilegiert_mandant"), EndvermoegenMandant = Z("endvermoegen_mandant"),
            AnfangsvermoegenGegenseite = Z("anfangsvermoegen_gegenseite"), PrivilegiertGegenseite = Z("privilegiert_gegenseite"), EndvermoegenGegenseite = Z("endvermoegen_gegenseite"),
            AnzahlRentenanrechte = int.TryParse(w.Wert("anzahl_rentenanrechte"), out var a) ? a : null,
            Wohnsituation = w.Wert("wohnsituation") ?? "", Bemerkungen = w.Wert("bemerkungen") ?? "",
        };
    }

    /// <summary>Gespeicherte Felder laden; gibt es noch keine, gilt ein alter Fragebogen als Eingabe des Anwalts.</summary>
    public static Formularwerte Laden(Dictionary<string, FeldWert> gespeichert, string? fragebogenJson)
    {
        if (gespeichert.Count > 0 || string.IsNullOrWhiteSpace(fragebogenJson)) return new Formularwerte(gespeichert);
        var w = new Formularwerte();
        foreach (var (k, v) in Aus(Fragebogen.FromJson(fragebogenJson)))
            if (v is not null) w.Felder[k] = new FeldWert { Anwalt = v };
        return w;
    }
}
