using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LexWolf.Services;

/// <summary>
/// Tabellenwerte für die Rechner. Ändern sich jährlich (Düsseldorfer Tabelle,
/// Kindergeld, Selbstbehalte) bzw. bei Gesetzesänderungen (RVG). Deshalb in
/// einer JSON-Datei überschreibbar (%APPDATA%\LexWolf\rechenwerte.json), damit
/// die Kanzlei sie ohne neue LexWolf-Version aktualisieren kann; die Rechner
/// zeigen den Stand immer mit an.
/// </summary>
public sealed class Rechenwerte
{
    // Stand geprüft am 18.10.2026 gegen die Düsseldorfer Tabelle ab 01.01.2026
    // (Zahlbeträge laut Veröffentlichung des Jugendamts Memmingen) sowie § 13 RVG,
    // § 34 GKG, § 28 FamGKG auf gesetze-im-internet.de.
    public string DuesseldorferTabelleStand { get; set; } = "Düsseldorfer Tabelle 2026 (ab 01.01.2026)";
    public int DuesseldorferTabelleJahr { get; set; } = 2026;
    /// <summary>Mindestbedarf je Altersstufe: 0–5, 6–11, 12–17, ab 18.</summary>
    public decimal[] Mindestbedarf { get; set; } = { 486m, 558m, 653m, 698m };
    /// <summary>Bedarf volljähriger Kinder mit eigenem Hausstand (Pauschale).</summary>
    public decimal BedarfEigenerHausstand { get; set; } = 990m;
    /// <summary>Obergrenzen des bereinigten Nettoeinkommens je Einkommensgruppe.</summary>
    public decimal[] EinkommensGrenzen { get; set; } =
        { 2100m, 2500m, 2900m, 3300m, 3700m, 4100m, 4500m, 4900m, 5300m, 5700m, 6400m, 7200m, 8200m, 9700m, 11200m };
    public int[] Prozentsaetze { get; set; } = { 100, 105, 110, 115, 120, 128, 136, 144, 152, 160, 168, 176, 184, 192, 200 };
    public decimal Kindergeld { get; set; } = 259m;
    public decimal SelbstbehaltErwerbstaetig { get; set; } = 1450m;
    public decimal SelbstbehaltNichtErwerbstaetig { get; set; } = 1200m;
    public decimal SelbstbehaltAngemessen { get; set; } = 1750m;
    public decimal SelbstbehaltEhegatteErwerbstaetig { get; set; } = 1600m;
    public decimal SelbstbehaltEhegatteNichtErwerbstaetig { get; set; } = 1475m;

    public string RvgStand { get; set; } = "§ 13 RVG ab 01.06.2025";
    public decimal RvgGrundgebuehr { get; set; } = 51.50m;
    /// <summary>Stufen nach § 13 Abs. 1 RVG: bis Wert, je angefangene Schrittweite, Erhöhung.</summary>
    public RvgStufe[] RvgStufen { get; set; } =
    {
        new() { BisWert = 2000m, Schritt = 500m, Betrag = 41.50m },
        new() { BisWert = 10000m, Schritt = 1000m, Betrag = 59.50m },
        new() { BisWert = 25000m, Schritt = 3000m, Betrag = 55.00m },
        new() { BisWert = 50000m, Schritt = 5000m, Betrag = 86.00m },
        new() { BisWert = 200000m, Schritt = 15000m, Betrag = 99.50m },
        new() { BisWert = 500000m, Schritt = 30000m, Betrag = 140.00m },
        new() { BisWert = decimal.MaxValue, Schritt = 50000m, Betrag = 175.00m },
    };
    public decimal Umsatzsteuer { get; set; } = 0.19m;

    // Gerichtsgebühren: § 34 GKG und § 28 FamGKG haben dieselbe Tabelle.
    public string GerichtStand { get; set; } = "§ 34 GKG / § 28 FamGKG (ab 01.06.2025)";
    public decimal GerichtGrundgebuehr { get; set; } = 40m;
    public RvgStufe[] GerichtStufen { get; set; } =
    {
        new() { BisWert = 2000m, Schritt = 500m, Betrag = 21.00m },
        new() { BisWert = 10000m, Schritt = 1000m, Betrag = 22.50m },
        new() { BisWert = 25000m, Schritt = 3000m, Betrag = 30.50m },
        new() { BisWert = 50000m, Schritt = 5000m, Betrag = 40.50m },
        new() { BisWert = 200000m, Schritt = 15000m, Betrag = 140.00m },
        new() { BisWert = 500000m, Schritt = 30000m, Betrag = 210.00m },
        new() { BisWert = decimal.MaxValue, Schritt = 50000m, Betrag = 210.00m },
    };

    public static string Pfad => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LexWolf", "rechenwerte.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Rechenwerte Laden()
    {
        try
        {
            if (File.Exists(Pfad))
                return JsonSerializer.Deserialize<Rechenwerte>(File.ReadAllText(Pfad)) ?? new Rechenwerte();
        }
        catch { }
        return new Rechenwerte();
    }

    /// <summary>Schreibt die Datei mit den aktuellen Werten (zum Bearbeiten), falls sie fehlt.</summary>
    public static string DateiSicherstellen()
    {
        if (!File.Exists(Pfad))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Pfad)!);
            File.WriteAllText(Pfad, JsonSerializer.Serialize(new Rechenwerte(), JsonOpts));
        }
        return Pfad;
    }
}

public sealed class RvgStufe
{
    public decimal BisWert { get; set; }
    public decimal Schritt { get; set; }
    public decimal Betrag { get; set; }
}

public sealed record Kind(string Name, DateTime Geburtsdatum, bool EigenerHausstand = false);

public sealed record KindesunterhaltZeile(string Name, int Alter, int Altersstufe, decimal Tabellenbetrag,
    decimal Kindergeldanteil, decimal Zahlbetrag);

public sealed record UnterhaltErgebnis(
    decimal BereinigtesEinkommen,
    int Einkommensgruppe,
    IReadOnlyList<KindesunterhaltZeile> Kinder,
    decimal SummeKindesunterhalt,
    bool Mangelfall,
    decimal? Ehegattenunterhalt,
    string Rechenweg);

/// <summary>
/// Fest programmierte Kanzlei-Rechner mit nachvollziehbarem Rechenweg
/// (zum Übernehmen in Schriftsätze). Die KI rechnet hier nichts.
/// </summary>
public static class KanzleiRechner
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    public static string Euro(decimal b) => b.ToString("#,##0.00", De) + " €";

    // ------------------------------------------------------------ Unterhalt

    /// <summary>Berufsbedingte Aufwendungen pauschal 5 %, mind. 50 €, höchstens 150 € (DT Anm. A.3).</summary>
    public static decimal BerufsbedingtePauschale(decimal netto) =>
        netto <= 0 ? 0 : Math.Clamp(Math.Round(netto * 0.05m, 2), 50m, 150m);

    public static int Alter(DateTime geburt, DateTime stichtag)
    {
        var alter = stichtag.Year - geburt.Year;
        if (stichtag < geburt.AddYears(alter)) alter--;
        return alter;
    }

    public static int Altersstufe(int alter) => alter switch { <= 5 => 0, <= 11 => 1, <= 17 => 2, _ => 3 };

    /// <summary>Einkommensgruppe 1–15 (0-basiert), ggf. mit Höher-/Herabstufung.</summary>
    public static int Einkommensgruppe(decimal bereinigt, Rechenwerte w, int stufung = 0)
    {
        var g = Array.FindIndex(w.EinkommensGrenzen, grenze => bereinigt <= grenze);
        if (g < 0) g = w.EinkommensGrenzen.Length - 1; // darüber: nach den Umständen des Einzelfalls
        return Math.Clamp(g + stufung, 0, w.EinkommensGrenzen.Length - 1);
    }

    /// <summary>Tabellenbetrag = Mindestbedarf × Prozentsatz, auf volle Euro aufgerundet (§ 1612a Abs. 2 BGB).</summary>
    public static decimal Tabellenbetrag(int gruppe, int altersstufe, Rechenwerte w) =>
        Math.Ceiling(w.Mindestbedarf[altersstufe] * w.Prozentsaetze[gruppe] / 100m);

    public static UnterhaltErgebnis Unterhalt(
        decimal nettoPflichtiger,
        bool pflichtigerErwerbstaetig,
        decimal sonstigeAbzuege,
        IReadOnlyList<Kind> kinder,
        DateTime stichtag,
        Rechenwerte w,
        int stufung = 0,
        decimal? einkommenBerechtigterEhegatte = null,
        bool berechtigterErwerbstaetig = true,
        bool suedlLeitlinien = false)
    {
        var weg = new StringBuilder();
        weg.AppendLine($"Grundlage: {w.DuesseldorferTabelleStand}, Stichtag {stichtag:dd.MM.yyyy}.");
        weg.AppendLine($"Nettoeinkommen Unterhaltspflichtiger: {Euro(nettoPflichtiger)}");
        var pauschale = pflichtigerErwerbstaetig ? BerufsbedingtePauschale(nettoPflichtiger) : 0m;
        if (pauschale > 0) weg.AppendLine($"− berufsbedingte Aufwendungen (5 %, mind. 50 €, max. 150 €): {Euro(pauschale)}");
        if (sonstigeAbzuege > 0) weg.AppendLine($"− sonstige Abzüge (z. B. Schulden, Altersvorsorge): {Euro(sonstigeAbzuege)}");
        var bereinigt = Math.Max(0, nettoPflichtiger - pauschale - sonstigeAbzuege);
        weg.AppendLine($"= bereinigtes Nettoeinkommen: {Euro(bereinigt)}");

        var gruppe = Einkommensgruppe(bereinigt, w, stufung);
        var grenzeVon = gruppe == 0 ? 0 : w.EinkommensGrenzen[gruppe - 1] + 1;
        weg.AppendLine($"Einkommensgruppe {gruppe + 1} ({Euro(grenzeVon)} – {Euro(w.EinkommensGrenzen[gruppe])}, {w.Prozentsaetze[gruppe]} %)" +
                       (stufung != 0 ? $", nach {(stufung > 0 ? "Höher" : "Herab")}stufung um {Math.Abs(stufung)}" : "") + ".");
        if (bereinigt > w.EinkommensGrenzen[^1])
            weg.AppendLine("Hinweis: Einkommen über der letzten Tabellenstufe — Bedarf nach den Umständen des Einzelfalls.");

        var zeilen = new List<KindesunterhaltZeile>();
        foreach (var k in kinder)
        {
            var alter = Alter(k.Geburtsdatum, stichtag);
            var stufe = Altersstufe(alter);
            // Volljährige mit eigenem Hausstand: fester Bedarf statt Tabellenbetrag
            var tab = stufe == 3 && k.EigenerHausstand ? w.BedarfEigenerHausstand : Tabellenbetrag(gruppe, stufe, w);
            // Minderjährige: hälftiges Kindergeld, Volljährige: volles (§ 1612b Abs. 1 BGB)
            var kg = stufe == 3 ? w.Kindergeld : w.Kindergeld / 2;
            zeilen.Add(new KindesunterhaltZeile(k.Name, alter, stufe, tab, kg, Math.Max(0, tab - kg)));
        }

        var stufenNamen = new[] { "0–5 J.", "6–11 J.", "12–17 J.", "ab 18 J." };
        foreach (var (z, k) in zeilen.Zip(kinder))
            weg.AppendLine($"{z.Name} ({z.Alter} J., Altersstufe {stufenNamen[z.Altersstufe]}" +
                           (z.Altersstufe == 3 && k.EigenerHausstand ? ", eigener Hausstand" : "") +
                           $"): {(z.Altersstufe == 3 && k.EigenerHausstand ? "Bedarf" : "Tabellenbetrag")} {Euro(z.Tabellenbetrag)} " +
                           $"− Kindergeld {Euro(z.Kindergeldanteil)} = Zahlbetrag {Euro(z.Zahlbetrag)}");

        var summe = zeilen.Sum(z => z.Zahlbetrag);
        var selbstbehalt = zeilen.Any(z => z.Altersstufe < 3)
            ? (pflichtigerErwerbstaetig ? w.SelbstbehaltErwerbstaetig : w.SelbstbehaltNichtErwerbstaetig)
            : w.SelbstbehaltAngemessen;
        var verfuegbar = bereinigt - selbstbehalt;
        var mangelfall = zeilen.Count > 0 && verfuegbar < summe;
        if (zeilen.Count > 0)
            weg.AppendLine($"Summe Kindesunterhalt: {Euro(summe)}; Selbstbehalt {Euro(selbstbehalt)}, verfügbar {Euro(Math.Max(0, verfuegbar))}.");

        if (mangelfall)
        {
            var quote = summe > 0 ? Math.Max(0, verfuegbar) / summe : 0;
            weg.AppendLine($"Mangelfall: verfügbare Masse reicht nicht — anteilige Kürzung auf {quote:P1}.");
            zeilen = zeilen.Select(z => z with { Zahlbetrag = Math.Floor(z.Zahlbetrag * quote) }).ToList();
            foreach (var z in zeilen)
                weg.AppendLine($"  {z.Name}: gekürzter Zahlbetrag {Euro(z.Zahlbetrag)}");
            summe = zeilen.Sum(z => z.Zahlbetrag);
        }

        decimal? ehegatte = null;
        if (einkommenBerechtigterEhegatte is { } eb)
        {
            var nachKind = bereinigt - summe;
            var ebBereinigt = berechtigterErwerbstaetig ? Math.Max(0, eb - BerufsbedingtePauschale(eb)) : eb;
            var diff = Math.Max(0, nachKind - ebBereinigt);
            decimal roh;
            if (suedlLeitlinien)
            {
                roh = diff * 0.45m;
                weg.AppendLine($"Ehegattenunterhalt (Süddeutsche Leitlinien, 45 %): ({Euro(nachKind)} − {Euro(ebBereinigt)}) × 45 % = {Euro(roh)}");
            }
            else
            {
                roh = diff * 3m / 7m;
                weg.AppendLine($"Ehegattenunterhalt (3/7-Methode): ({Euro(nachKind)} − {Euro(ebBereinigt)}) × 3/7 = {Euro(roh)}");
            }
            var sbE = pflichtigerErwerbstaetig ? w.SelbstbehaltEhegatteErwerbstaetig : w.SelbstbehaltEhegatteNichtErwerbstaetig;
            var maxE = Math.Max(0, nachKind - sbE);
            ehegatte = Math.Floor(Math.Min(roh, maxE));
            if (roh > maxE)
                weg.AppendLine($"Begrenzt durch Ehegattenselbstbehalt {Euro(sbE)} auf {Euro(ehegatte.Value)}.");
            weg.AppendLine($"Ehegattenunterhalt: {Euro(ehegatte.Value)} monatlich.");
        }

        weg.Append("Ohne Gewähr — Einzelfallumstände (Wohnwert, Mehrbedarf, Erwerbsobliegenheit …) sind nicht berücksichtigt.");
        return new UnterhaltErgebnis(bereinigt, gruppe + 1, zeilen, summe, mangelfall, ehegatte, weg.ToString());
    }

    // ------------------------------------------------------------ Zugewinn

    public sealed record ZugewinnSeite(string Name, decimal Anfangsvermoegen, decimal PrivilegierterErwerb, decimal Endvermoegen);

    public static (decimal Ausgleich, string Berechtigter, string Rechenweg) Zugewinn(
        ZugewinnSeite a, ZugewinnSeite b, decimal indexfaktor = 1m)
    {
        var weg = new StringBuilder();
        decimal Gewinn(ZugewinnSeite s)
        {
            var anfang = Math.Max(0, (s.Anfangsvermoegen + s.PrivilegierterErwerb) * indexfaktor);
            var z = Math.Max(0, s.Endvermoegen - anfang);
            weg.AppendLine($"{s.Name}: Endvermögen {Euro(s.Endvermoegen)} − Anfangsvermögen " +
                           $"({Euro(s.Anfangsvermoegen)} + privilegierter Erwerb {Euro(s.PrivilegierterErwerb)})" +
                           (indexfaktor != 1m ? $" × Index {indexfaktor.ToString("0.0000", De)}" : "") +
                           $" = Zugewinn {Euro(z)}");
            return z;
        }
        var za = Gewinn(a);
        var zb = Gewinn(b);
        var ausgleich = Math.Round(Math.Abs(za - zb) / 2, 2);
        var berechtigt = za < zb ? a.Name : za > zb ? b.Name : "—";
        weg.AppendLine(ausgleich > 0
            ? $"Ausgleichsforderung (§ 1378 Abs. 1 BGB): ({Euro(Math.Max(za, zb))} − {Euro(Math.Min(za, zb))}) / 2 = {Euro(ausgleich)} zugunsten {berechtigt}."
            : "Kein Ausgleich: beide Zugewinne sind gleich.");
        weg.Append("Begrenzung auf das bei Beendigung vorhandene Vermögen (§ 1378 Abs. 2 BGB) bitte prüfen.");
        return (ausgleich, berechtigt, weg.ToString());
    }

    // ------------------------------------------------------------ Verfahrenswert (FamGKG)

    public sealed record VerfahrenswertPosten(string Bezeichnung, decimal Wert, string Grundlage);

    public static VerfahrenswertPosten Ehesache(decimal nettoBeide, decimal abzugKinder = 0) =>
        new("Ehesache (Scheidung)", Math.Clamp(3 * Math.Max(0, nettoBeide - abzugKinder), 3000m, 1_000_000m),
            "§ 43 FamGKG: 3 × Nettoeinkommen beider Ehegatten, mind. 3.000 €");

    public static VerfahrenswertPosten Versorgungsausgleich(decimal ehesachenwert, int anrechte) =>
        new("Versorgungsausgleich", Math.Max(1000m, ehesachenwert * 0.10m * Math.Max(1, anrechte)),
            $"§ 50 FamGKG: 10 % des Ehesachenwerts je Anrecht ({Math.Max(1, anrechte)} Anrechte), mind. 1.000 €");

    public static VerfahrenswertPosten Unterhaltssache(decimal monatlich, decimal rueckstand) =>
        new("Unterhalt", 12 * monatlich + rueckstand, "§ 51 FamGKG: 12 × Monatsbetrag + Rückstände bei Einreichung");

    public static VerfahrenswertPosten Kindschaftssache() =>
        new("Sorge-/Umgangsrecht", 4000m, "§ 45 Abs. 1 FamGKG: 4.000 €");

    // ------------------------------------------------------------ RVG

    /// <summary>Volle Gebühr (1,0) nach § 13 Abs. 1 RVG.</summary>
    public static decimal RvgGebuehr(decimal gegenstandswert, Rechenwerte w) =>
        Wertgebuehr(gegenstandswert, w.RvgGrundgebuehr, w.RvgStufen);

    /// <summary>Volle Gerichtsgebühr (1,0) nach § 34 GKG / § 28 FamGKG.</summary>
    public static decimal GerichtsGebuehr(decimal wert, Rechenwerte w) =>
        Wertgebuehr(wert, w.GerichtGrundgebuehr, w.GerichtStufen);

    private static decimal Wertgebuehr(decimal gegenstandswert, decimal grund, IEnumerable<RvgStufe> stufen)
    {
        var gebuehr = grund;
        decimal untergrenze = 500m;
        foreach (var stufe in stufen)
        {
            if (gegenstandswert <= untergrenze) break;
            var obergrenze = Math.Min(gegenstandswert, stufe.BisWert);
            var schritte = Math.Ceiling((obergrenze - untergrenze) / stufe.Schritt);
            gebuehr += schritte * stufe.Betrag;
            untergrenze = stufe.BisWert;
        }
        return gebuehr;
    }

    public sealed record Gebuehrentatbestand(string Bezeichnung, string Vv, decimal Satz);

    public static readonly IReadOnlyList<Gebuehrentatbestand> Tatbestaende = new List<Gebuehrentatbestand>
    {
        new("Geschäftsgebühr (außergerichtlich)", "VV 2300", 1.3m),
        new("Verfahrensgebühr", "VV 3100", 1.3m),
        new("Terminsgebühr", "VV 3104", 1.2m),
        new("Einigungsgebühr (anhängiges Verfahren)", "VV 1003", 1.0m),
        new("Einigungsgebühr (außergerichtlich)", "VV 1000", 1.5m),
        new("Verfahrensgebühr Berufung", "VV 3200", 1.6m),
    };

    /// <summary>Gerichtliches Verfahren mit Gebührensatz laut Kostenverzeichnis
    /// (Anlage 1 GKG bzw. FamGKG), regulär und bei Vergleich/Rücknahme ermäßigt.</summary>
    public sealed record Gerichtsverfahren(string Bezeichnung, string Kv, decimal Satz, string KvErmaessigt, decimal SatzErmaessigt,
        decimal Mindestgebuehr = 15m)
    {
        public override string ToString() => Bezeichnung;
    }

    public static readonly IReadOnlyList<Gerichtsverfahren> Gerichtsverfahrensarten = new List<Gerichtsverfahren>
    {
        new("Zivilprozess, 1. Instanz", "KV 1210 GKG", 3.0m, "KV 1211 GKG", 1.0m),
        new("Berufung (Zivilprozess)", "KV 1220 GKG", 4.0m, "KV 1222 GKG", 2.0m),
        new("Mahnverfahren", "KV 1100 GKG", 0.5m, "KV 1100 GKG", 0.5m, 38m),
        new("Scheidungsverbund (Ehesache mit Folgesachen)", "KV 1110 FamGKG", 2.0m, "KV 1111 FamGKG", 0.5m),
        new("Beschwerde in Ehesachen", "KV 1120 FamGKG", 3.0m, "KV 1121 FamGKG", 0.5m),
        new("Familienstreitsache, 1. Instanz (Unterhalt, Güterrecht)", "KV 1220 FamGKG", 3.0m, "KV 1221 FamGKG", 1.0m),
        new("Kindschaftssache (Sorge, Umgang)", "KV 1310 FamGKG", 0.5m, "KV 1310 FamGKG", 0.5m),
        new("Übrige Familiensache (z. B. Versorgungsausgleich isoliert)", "KV 1320 FamGKG", 2.0m, "KV 1321 FamGKG", 0.5m),
    };

    public static (decimal Betrag, string Rechenweg) Gerichtskosten(decimal wert, Gerichtsverfahren v, bool ermaessigt, Rechenwerte w)
    {
        var voll = GerichtsGebuehr(wert, w);
        var satz = ermaessigt ? v.SatzErmaessigt : v.Satz;
        var kv = ermaessigt ? v.KvErmaessigt : v.Kv;
        var betrag = Math.Max(v.Mindestgebuehr, Math.Round(voll * satz, 2, MidpointRounding.AwayFromZero));
        return (betrag, $"Gerichtskosten {v.Bezeichnung}: 1,0-Gebühr bei Wert {Euro(wert)} = {Euro(voll)} ({w.GerichtStand}); " +
                        $"{satz.ToString("0.0#", De)} ({kv}){(ermaessigt ? ", ermäßigt" : "")} = {Euro(betrag)}");
    }

    public static (decimal Netto, decimal Brutto, string Rechenweg) Rvg(
        decimal gegenstandswert, IEnumerable<Gebuehrentatbestand> gebuehren, Rechenwerte w, bool auslagenpauschale = true)
    {
        var weg = new StringBuilder();
        var voll = RvgGebuehr(gegenstandswert, w);
        weg.AppendLine($"Gegenstandswert {Euro(gegenstandswert)} — 1,0-Gebühr nach § 13 RVG: {Euro(voll)} ({w.RvgStand})");
        decimal summe = 0;
        foreach (var g in gebuehren)
        {
            var betrag = Math.Max(15m, Math.Round(voll * g.Satz, 2)); // Mindestbetrag 15 € (§ 13 Abs. 2 RVG)
            summe += betrag;
            weg.AppendLine($"{g.Satz.ToString("0.0", De)} {g.Bezeichnung} ({g.Vv}): {Euro(betrag)}");
        }
        if (auslagenpauschale && summe > 0)
        {
            var pauschale = Math.Min(20m, Math.Round(summe * 0.2m, 2));
            summe += pauschale;
            weg.AppendLine($"Post- und Telekommunikationspauschale (VV 7002): {Euro(pauschale)}");
        }
        var ust = Math.Round(summe * w.Umsatzsteuer, 2);
        weg.AppendLine($"Zwischensumme netto: {Euro(summe)}");
        weg.AppendLine($"{w.Umsatzsteuer:P0} Umsatzsteuer (VV 7008): {Euro(ust)}");
        weg.Append($"Gesamt: {Euro(summe + ust)}");
        return (summe, summe + ust, weg.ToString());
    }
}
