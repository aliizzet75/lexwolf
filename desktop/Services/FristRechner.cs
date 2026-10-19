using System;
using System.Collections.Generic;
using System.Linq;

namespace LexWolf.Services;

public enum FristEinheit { Tage, Wochen, Monate }

/// <summary>Eine gesetzliche (oder freie) Fristart mit Dauer und Rechtsgrundlage.</summary>
public sealed record Fristart(
    string Id,
    string Name,
    int Anzahl,
    FristEinheit Einheit,
    string Rechtsgrundlage,
    string Ereignis,
    bool Notfrist,
    int VorfristTage,
    string? Hinweis = null)
{
    public string Dauer => Einheit switch
    {
        FristEinheit.Tage => Anzahl == 1 ? "1 Tag" : $"{Anzahl} Tage",
        FristEinheit.Wochen => Anzahl == 1 ? "1 Woche" : $"{Anzahl} Wochen",
        _ => Anzahl == 1 ? "1 Monat" : $"{Anzahl} Monate",
    };
}

/// <summary>Ergebnis einer Fristberechnung inklusive nachvollziehbarem Rechenweg.</summary>
public sealed record FristErgebnis(DateTime Ende, DateTime Vorfrist, DateTime EndeOhneVerschiebung, string Rechenweg);

/// <summary>
/// Fristberechnung nach §§ 187 Abs. 1, 188 Abs. 2 und 3 BGB, § 222 ZPO (bzw.
/// § 193 BGB): der Ereignistag zählt nicht mit; Wochen- und Monatsfristen
/// enden am Tag mit derselben Benennung bzw. Zahl, fehlt dieser im Monat am
/// Monatsletzten; fällt das Ende auf Samstag, Sonntag oder einen Feiertag am
/// Gerichtsort, läuft die Frist am nächsten Werktag ab.
/// Bewusst fest programmiert — Fristen rechnet nie die KI.
/// </summary>
public static class FristRechner
{
    public static readonly IReadOnlyDictionary<string, string> Bundeslaender = new Dictionary<string, string>
    {
        ["BW"] = "Baden-Württemberg", ["BY"] = "Bayern", ["BE"] = "Berlin", ["BB"] = "Brandenburg",
        ["HB"] = "Bremen", ["HH"] = "Hamburg", ["HE"] = "Hessen", ["MV"] = "Mecklenburg-Vorpommern",
        ["NI"] = "Niedersachsen", ["NW"] = "Nordrhein-Westfalen", ["RP"] = "Rheinland-Pfalz",
        ["SL"] = "Saarland", ["SN"] = "Sachsen", ["ST"] = "Sachsen-Anhalt",
        ["SH"] = "Schleswig-Holstein", ["TH"] = "Thüringen",
    };

    public static readonly IReadOnlyList<Fristart> Fristarten = new List<Fristart>
    {
        new("berufung", "Berufung", 1, FristEinheit.Monate, "§ 517 ZPO", "Zustellung des Urteils", true, 7,
            "Spätestens 5 Monate nach Verkündung."),
        new("berufungsbegruendung", "Berufungsbegründung", 2, FristEinheit.Monate, "§ 520 Abs. 2 ZPO", "Zustellung des Urteils", false, 7,
            "Verlängerung auf Antrag möglich (§ 520 Abs. 2 S. 2, 3 ZPO)."),
        new("revision", "Revision", 1, FristEinheit.Monate, "§ 548 ZPO", "Zustellung des Urteils", true, 7),
        new("revisionsbegruendung", "Revisionsbegründung", 2, FristEinheit.Monate, "§ 551 Abs. 2 ZPO", "Zustellung des Urteils", false, 7),
        new("einspruch_vu", "Einspruch gegen Versäumnisurteil", 2, FristEinheit.Wochen, "§ 339 Abs. 1 ZPO", "Zustellung des Versäumnisurteils", true, 3),
        new("einspruch_vb", "Einspruch gegen Vollstreckungsbescheid", 2, FristEinheit.Wochen, "§§ 700 Abs. 1, 339 Abs. 1 ZPO", "Zustellung des Vollstreckungsbescheids", true, 3),
        new("widerspruch_mb", "Widerspruch gegen Mahnbescheid", 2, FristEinheit.Wochen, "§ 692 Abs. 1 Nr. 3 ZPO", "Zustellung des Mahnbescheids", false, 3,
            "Keine Notfrist — Widerspruch ist bis zum Erlass des Vollstreckungsbescheids möglich."),
        new("sofortige_beschwerde", "Sofortige Beschwerde", 2, FristEinheit.Wochen, "§ 569 Abs. 1 ZPO", "Zustellung der Entscheidung", true, 3),
        new("beschwerde_famfg", "Beschwerde (FamFG)", 1, FristEinheit.Monate, "§ 63 Abs. 1 FamFG", "Schriftliche Bekanntgabe des Beschlusses", false, 7),
        new("beschwerdebegruendung_famfg", "Beschwerdebegründung (Familienstreitsache)", 2, FristEinheit.Monate, "§ 117 Abs. 1 FamFG", "Schriftliche Bekanntgabe des Beschlusses", false, 7),
        new("rechtsbeschwerde_famfg", "Rechtsbeschwerde (FamFG)", 1, FristEinheit.Monate, "§ 71 Abs. 1 FamFG", "Schriftliche Bekanntgabe des Beschlusses", false, 7),
        new("kschg", "Kündigungsschutzklage", 3, FristEinheit.Wochen, "§ 4 S. 1 KSchG", "Zugang der Kündigung", false, 5),
        new("widerspruch_va", "Widerspruch gegen Verwaltungsakt", 1, FristEinheit.Monate, "§ 70 Abs. 1 VwGO", "Bekanntgabe des Verwaltungsakts", false, 7,
            "Bei Postzustellung gilt der Bescheid am 4. Tag nach Aufgabe zur Post als bekanntgegeben (§ 41 Abs. 2 VwVfG)."),
        new("klage_va", "Anfechtungsklage", 1, FristEinheit.Monate, "§ 74 Abs. 1 VwGO", "Zustellung des Widerspruchsbescheids", false, 7),
        new("einspruch_ao", "Einspruch gegen Steuerbescheid", 1, FristEinheit.Monate, "§ 355 Abs. 1 AO", "Bekanntgabe des Bescheids", false, 7,
            "Bei Postversand gilt der Bescheid am 4. Tag nach Aufgabe zur Post als bekanntgegeben (§ 122 Abs. 2 Nr. 1 AO)."),
        new("berufung_stpo", "Berufung (Strafsache)", 1, FristEinheit.Wochen, "§ 314 Abs. 1 StPO", "Verkündung des Urteils", false, 2),
        new("revision_stpo", "Revision (Strafsache)", 1, FristEinheit.Wochen, "§ 341 Abs. 1 StPO", "Verkündung des Urteils", false, 2),
        new("revisionsbegruendung_stpo", "Revisionsbegründung (Strafsache)", 1, FristEinheit.Monate, "§ 345 Abs. 1 StPO", "Zustellung des Urteils", false, 7),
        new("einspruch_strafbefehl", "Einspruch gegen Strafbefehl", 2, FristEinheit.Wochen, "§ 410 Abs. 1 StPO", "Zustellung des Strafbefehls", false, 3),
        new("beschwerde_famfg_eilverfahren", "Beschwerde (FamFG, einstweilige Anordnung)", 2, FristEinheit.Wochen, "§ 63 Abs. 2 Nr. 1 FamFG", "Schriftliche Bekanntgabe des Beschlusses", false, 3),
        new("nzb", "Nichtzulassungsbeschwerde", 1, FristEinheit.Monate, "§ 544 Abs. 3 ZPO", "Zustellung des vollständigen Urteils", true, 7,
            "Spätestens 6 Monate nach Verkündung."),
        new("nzb_begruendung", "Begründung Nichtzulassungsbeschwerde", 2, FristEinheit.Monate, "§ 544 Abs. 4 ZPO", "Zustellung des vollständigen Urteils", false, 7,
            "Spätestens 7 Monate nach Verkündung; Verlängerung auf Antrag möglich."),
        new("tatbestandsberichtigung", "Antrag auf Tatbestandsberichtigung", 2, FristEinheit.Wochen, "§ 320 Abs. 1 ZPO", "Zustellung des Urteils", false, 3,
            "Ausgeschlossen, wenn seit Verkündung 3 Monate vergangen sind (§ 320 Abs. 2 S. 3 ZPO)."),
        new("wiedereinsetzung", "Antrag auf Wiedereinsetzung", 2, FristEinheit.Wochen, "§ 234 Abs. 1 S. 1 ZPO", "Behebung des Hindernisses", false, 3,
            "1 Monat bei versäumter Begründungsfrist (Berufung, Revision, NZB, Rechtsbeschwerde — § 234 Abs. 1 S. 2 ZPO). Nicht verlängerbar; versäumte Handlung mit nachholen (§ 236 Abs. 2 ZPO); spätestens 1 Jahr nach Fristende (§ 234 Abs. 3 ZPO)."),
        new("berufung_arbg", "Berufung (Arbeitsgericht)", 1, FristEinheit.Monate, "§ 66 Abs. 1 ArbGG", "Zustellung des vollständigen Urteils", true, 7,
            "Spätestens 5 Monate nach Verkündung."),
        new("berufungsbegruendung_arbg", "Berufungsbegründung (Arbeitsgericht)", 2, FristEinheit.Monate, "§ 66 Abs. 1 ArbGG", "Zustellung des vollständigen Urteils", false, 7,
            "Nur einmal auf Antrag verlängerbar (§ 66 Abs. 1 S. 5 ArbGG)."),
        new("einspruch_vu_arbg", "Einspruch gegen Versäumnisurteil (Arbeitsgericht)", 1, FristEinheit.Wochen, "§ 59 S. 1 ArbGG", "Zustellung des Versäumnisurteils", true, 2,
            "Achtung: nur 1 Woche — nicht 2 Wochen wie nach § 339 ZPO."),
        new("widerspruch_sgg", "Widerspruch (Sozialrecht)", 1, FristEinheit.Monate, "§ 84 Abs. 1 SGG", "Bekanntgabe des Verwaltungsakts", false, 7,
            "Bei Postversand gilt der Bescheid am 4. Tag nach Aufgabe zur Post als bekanntgegeben (§ 37 Abs. 2 SGB X)."),
        new("klage_sgg", "Klage (Sozialgericht)", 1, FristEinheit.Monate, "§ 87 SGG", "Bekanntgabe des Widerspruchsbescheids", false, 7),
        new("berufung_sgg", "Berufung (Sozialgericht)", 1, FristEinheit.Monate, "§ 151 Abs. 1 SGG", "Zustellung des Urteils", false, 7),
        new("zulassung_berufung_vwgo", "Antrag auf Zulassung der Berufung (VwGO)", 1, FristEinheit.Monate, "§ 124a Abs. 4 S. 1 VwGO", "Zustellung des vollständigen Urteils", false, 7),
        new("zulassung_begruendung_vwgo", "Begründung des Zulassungsantrags (VwGO)", 2, FristEinheit.Monate, "§ 124a Abs. 4 S. 4 VwGO", "Zustellung des vollständigen Urteils", false, 7),
        new("klage_fgo", "Klage (Finanzgericht)", 1, FristEinheit.Monate, "§ 47 Abs. 1 FGO", "Bekanntgabe der Einspruchsentscheidung", false, 7),
    };

    public static Fristart? FindeFristart(string? id) =>
        id is null ? null : Fristarten.FirstOrDefault(f => f.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Fristende ab Ereignistag (Zustellung, Zugang …).</summary>
    public static FristErgebnis Berechne(DateTime ereignis, int anzahl, FristEinheit einheit, string bundesland, int vorfristTage)
    {
        ereignis = ereignis.Date;
        var roh = einheit switch
        {
            FristEinheit.Tage => ereignis.AddDays(anzahl),
            FristEinheit.Wochen => ereignis.AddDays(7 * anzahl),
            // AddMonths liefert bei fehlendem Tag den Monatsletzten — genau § 188 Abs. 3 BGB.
            _ => ereignis.AddMonths(anzahl),
        };

        var weg = new List<string>
        {
            $"Ereignis am {ereignis:dd.MM.yyyy} ({Wochentag(ereignis)}); dieser Tag zählt nicht mit (§ 187 Abs. 1 BGB).",
            einheit switch
            {
                FristEinheit.Tage => $"{anzahl} Tage später: {roh:dd.MM.yyyy}.",
                FristEinheit.Wochen => $"{anzahl} Woche(n) später, gleicher Wochentag (§ 188 Abs. 2 BGB): {roh:dd.MM.yyyy}.",
                _ => roh.Day != ereignis.Day
                    ? $"{anzahl} Monat(e) später; den {ereignis.Day}. gibt es dort nicht, daher Monatsletzter (§ 188 Abs. 3 BGB): {roh:dd.MM.yyyy}."
                    : $"{anzahl} Monat(e) später, gleiche Tageszahl (§ 188 Abs. 2 BGB): {roh:dd.MM.yyyy}.",
            },
        };

        var ende = NaechsterWerktag(roh, bundesland);
        if (ende != roh)
        {
            var grund = roh.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                ? Wochentag(roh)
                : $"Feiertag ({FeiertagName(roh, bundesland)})";
            weg.Add($"{roh:dd.MM.yyyy} ist {grund} — Fristende verschiebt sich auf {Wochentag(ende)}, {ende:dd.MM.yyyy} (§ 222 Abs. 2 ZPO, § 193 BGB).");
        }
        weg.Add($"Fristende: {Wochentag(ende)}, {ende:dd.MM.yyyy}, 24:00 Uhr.");

        var vorfrist = VorigerWerktag(ende.AddDays(-Math.Max(0, vorfristTage)), bundesland);
        if (vorfrist < ereignis) vorfrist = ereignis;
        return new FristErgebnis(ende, vorfrist, roh, string.Join("\n", weg));
    }

    public static FristErgebnis Berechne(DateTime ereignis, Fristart art, string bundesland) =>
        Berechne(ereignis, art.Anzahl, art.Einheit, bundesland, art.VorfristTage);

    /// <summary>Vom Gericht oder der Gegenseite gesetzte Frist mit festem Datum.</summary>
    public static FristErgebnis GesetzteFrist(DateTime datum, string bundesland, int vorfristTage = 3)
    {
        datum = datum.Date;
        var ende = NaechsterWerktag(datum, bundesland);
        var weg = ende == datum
            ? $"Gesetzte Frist bis {Wochentag(datum)}, {datum:dd.MM.yyyy}."
            : $"Gesetzte Frist bis {datum:dd.MM.yyyy}; das ist kein Werktag — Ablauf am {Wochentag(ende)}, {ende:dd.MM.yyyy} (§ 222 Abs. 2 ZPO).";
        return new FristErgebnis(ende, VorigerWerktag(ende.AddDays(-vorfristTage), bundesland), datum, weg);
    }

    public static bool IstWerktag(DateTime tag, string bundesland) =>
        tag.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && FeiertagName(tag, bundesland) is null;

    public static DateTime NaechsterWerktag(DateTime tag, string bundesland)
    {
        tag = tag.Date;
        while (!IstWerktag(tag, bundesland)) tag = tag.AddDays(1);
        return tag;
    }

    private static DateTime VorigerWerktag(DateTime tag, string bundesland)
    {
        tag = tag.Date;
        while (!IstWerktag(tag, bundesland)) tag = tag.AddDays(-1);
        return tag;
    }

    /// <summary>Name des gesetzlichen Feiertags im Bundesland, sonst null.
    /// Nur landesweite Feiertage — örtliche (z.B. Augsburger Friedensfest,
    /// Fronleichnam nur in Teilen Sachsens/Thüringens) sind nicht enthalten.</summary>
    public static string? FeiertagName(DateTime tag, string bundesland)
    {
        tag = tag.Date;
        var j = tag.Year;
        var ostern = Ostersonntag(j);
        var bl = (bundesland ?? "").ToUpperInvariant();

        if (tag == new DateTime(j, 1, 1)) return "Neujahr";
        if (tag == ostern.AddDays(-2)) return "Karfreitag";
        if (tag == ostern.AddDays(1)) return "Ostermontag";
        if (tag == new DateTime(j, 5, 1)) return "Tag der Arbeit";
        if (tag == ostern.AddDays(39)) return "Christi Himmelfahrt";
        if (tag == ostern.AddDays(50)) return "Pfingstmontag";
        if (tag == new DateTime(j, 10, 3)) return "Tag der Deutschen Einheit";
        if (tag == new DateTime(j, 12, 25)) return "1. Weihnachtstag";
        if (tag == new DateTime(j, 12, 26)) return "2. Weihnachtstag";

        if (tag == new DateTime(j, 1, 6) && bl is "BW" or "BY" or "ST") return "Heilige Drei Könige";
        if (tag == new DateTime(j, 3, 8) && (bl == "BE" || (bl == "MV" && j >= 2023))) return "Internationaler Frauentag";
        if (tag == ostern && bl == "BB") return "Ostersonntag";
        if (tag == ostern.AddDays(49) && bl == "BB") return "Pfingstsonntag";
        if (tag == ostern.AddDays(60) && bl is "BW" or "BY" or "HE" or "NW" or "RP" or "SL") return "Fronleichnam";
        if (tag == new DateTime(j, 8, 15) && bl == "SL") return "Mariä Himmelfahrt";
        if (tag == new DateTime(j, 9, 20) && bl == "TH" && j >= 2019) return "Weltkindertag";
        if (tag == new DateTime(j, 10, 31)
            && (bl is "BB" or "MV" or "SN" or "ST" or "TH" || (bl is "HB" or "HH" or "NI" or "SH" && j >= 2018)))
            return "Reformationstag";
        if (tag == new DateTime(j, 11, 1) && bl is "BW" or "BY" or "NW" or "RP" or "SL") return "Allerheiligen";
        if (tag == BussUndBettag(j) && bl == "SN") return "Buß- und Bettag";
        return null;
    }

    /// <summary>Ostersonntag nach der Gaußschen Osterformel (gregorianisch).</summary>
    public static DateTime Ostersonntag(int jahr)
    {
        var a = jahr % 19;
        var b = jahr / 100;
        var c = jahr % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var monat = (h + l - 7 * m + 114) / 31;
        var tag = (h + l - 7 * m + 114) % 31 + 1;
        return new DateTime(jahr, monat, tag);
    }

    // Mittwoch vor dem 23. November
    private static DateTime BussUndBettag(int jahr)
    {
        var d = new DateTime(jahr, 11, 22);
        while (d.DayOfWeek != DayOfWeek.Wednesday) d = d.AddDays(-1);
        return d;
    }

    public static string Wochentag(DateTime d) => d.DayOfWeek switch
    {
        DayOfWeek.Monday => "Montag",
        DayOfWeek.Tuesday => "Dienstag",
        DayOfWeek.Wednesday => "Mittwoch",
        DayOfWeek.Thursday => "Donnerstag",
        DayOfWeek.Friday => "Freitag",
        DayOfWeek.Saturday => "Samstag",
        _ => "Sonntag",
    };
}
