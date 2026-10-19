using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LexWolf.Services;

/// <summary>Angaben aus dem Dialog „Neues Mandat“.</summary>
public sealed record MandatDaten(
    string Name,
    string Telefon = "",
    string Email = "",
    string Anschrift = "",
    string Gegner = "",
    string Gegnervertreter = "",
    string Rechtsgebiet = "",
    string Anliegen = "")
{
    public bool IstFamilienrecht => Rechtsgebiet.StartsWith("Familienrecht", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Fachliche Logik der Mandatsaufnahme: Ordnername, Kollisionsprüfung vor dem Anlegen,
/// Aktenvermerk, Vollmacht und der Auftrag an die KI für die ersten Schritte.
/// Ohne WPF, damit testbar.
/// </summary>
public static class MandatAufnahme
{
    public static readonly IReadOnlyList<string> Rechtsgebiete = new[]
    {
        "Familienrecht – Scheidung",
        "Familienrecht – Unterhalt",
        "Familienrecht – Sorge- und Umgangsrecht",
        "Familienrecht – Zugewinn / Vermögen",
        "Familienrecht – Sonstiges",
        "Arbeitsrecht",
        "Mietrecht",
        "Verkehrsrecht",
        "Allgemeines Zivilrecht",
        "Erbrecht",
        "Strafrecht",
        "Sozialrecht",
        "Verwaltungsrecht",
        "Sonstiges",
    };

    private const string Ungueltig = "\\/:*?\"<>|";
    private const string MetaPraefix = "mandat_aufnahme:";

    public static string MetaSchluessel(string mandantId) => MetaPraefix + mandantId;

    /// <summary>Ordnername aus dem Mandantennamen: unzulässige Zeichen raus, Leerraum glätten.</summary>
    public static string OrdnerName(string name)
    {
        var s = string.Concat(name.Trim().Select(c => Ungueltig.Contains(c) || char.IsControl(c) ? ' ' : c));
        s = string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return s.TrimEnd('.', ' ');
    }

    /// <summary>Gibt es den Mandanten (vermutlich) schon? Dann eher bestehende Akte öffnen.</summary>
    public static string? BestehenderMandant(string name, IEnumerable<(string Id, string Name)> mandanten) =>
        string.IsNullOrWhiteSpace(name) ? null
            : mandanten.FirstOrDefault(m => Kollisionspruefung.GleicherName(name, m.Name)).Name;

    /// <summary>Kollisionsprüfung für ein Mandat, das es noch nicht gibt.</summary>
    public static List<Kollision> PruefeKollision(MandatDaten d,
        IEnumerable<(string Id, string Name)> mandanten, IReadOnlyList<AktenBeteiligter> beteiligte)
    {
        const string neu = "\u0000neu";
        var alle = beteiligte.ToList();
        if (d.Gegner.Trim().Length > 0) alle.Add(new AktenBeteiligter(neu, d.Name, "Gegner", d.Gegner.Trim()));
        if (d.Gegnervertreter.Trim().Length > 0)
            alle.Add(new AktenBeteiligter(neu, d.Name, "Gegnervertreter", d.Gegnervertreter.Trim()));
        // Gegnervertreter ist ein Kollege, keine Partei — nur Gegner gegen Mandanten prüfen.
        return Kollisionspruefung.Pruefe(neu, d.Name.Trim(), mandanten, alle)
            .Where(k => !k.Beschreibung.Contains("(Gegnervertreter)", StringComparison.Ordinal))
            .ToList();
    }

    public static string SpeicherJson(MandatDaten d, DateTime angelegt) => JsonSerializer.Serialize(new
    {
        d.Telefon, d.Email, d.Anschrift, d.Rechtsgebiet, d.Anliegen, angelegt = angelegt.ToString("o"),
    });

    /// <summary>E-Mail-Adresse aus der gespeicherten Mandatsaufnahme (für Sachstand, Mandatsbestätigung).</summary>
    public static string EmailAus(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("Email", out var e) ? e.GetString() ?? "" : "";
        }
        catch (JsonException) { return ""; }
    }

    /// <summary>Aktenvermerk über die Mandatsaufnahme — dokumentiert auch die Kollisionsprüfung.</summary>
    public static string Aktenvermerk(MandatDaten d, DateTime zeitpunkt, IReadOnlyList<Kollision> kollisionen,
        bool kollisionBestaetigt, string? frist)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Aktenvermerk: Mandatsaufnahme {d.Name}");
        sb.AppendLine($"Angelegt am {zeitpunkt:dd.MM.yyyy} um {zeitpunkt:HH:mm} Uhr.");
        sb.AppendLine();
        sb.AppendLine("## Mandant");
        sb.AppendLine(d.Name);
        if (d.Anschrift.Trim().Length > 0) sb.AppendLine(d.Anschrift.Trim());
        if (d.Telefon.Trim().Length > 0) sb.AppendLine($"Telefon: {d.Telefon.Trim()}");
        if (d.Email.Trim().Length > 0) sb.AppendLine($"E-Mail: {d.Email.Trim()}");
        sb.AppendLine();
        sb.AppendLine("## Gegenseite");
        sb.AppendLine(d.Gegner.Trim().Length > 0 ? d.Gegner.Trim() : "noch nicht bekannt");
        if (d.Gegnervertreter.Trim().Length > 0) sb.AppendLine($"Vertreten durch: {d.Gegnervertreter.Trim()}");
        sb.AppendLine();
        sb.AppendLine("## Gegenstand");
        if (d.Rechtsgebiet.Length > 0) sb.AppendLine($"Rechtsgebiet: {d.Rechtsgebiet}");
        sb.AppendLine(d.Anliegen.Trim().Length > 0 ? d.Anliegen.Trim() : "—");
        if (frist is not null)
        {
            sb.AppendLine();
            sb.AppendLine("## Laufende Frist");
            sb.AppendLine(frist);
        }
        sb.AppendLine();
        sb.AppendLine("## Kollisionsprüfung (§ 43a Abs. 4 BRAO, § 3 BORA)");
        if (kollisionen.Count == 0)
        {
            sb.AppendLine($"Am {zeitpunkt:dd.MM.yyyy} gegen den Akten- und Beteiligtenbestand in LexWolf geprüft: keine Treffer.");
        }
        else
        {
            sb.AppendLine($"Am {zeitpunkt:dd.MM.yyyy} geprüft — mögliche Kollisionen:");
            foreach (var k in kollisionen) sb.AppendLine($"- {k.Beschreibung} (Akte „{k.AndereAkte}“)");
            if (kollisionBestaetigt)
                sb.AppendLine("Die Treffer wurden geprüft; nach Angabe des Bearbeiters liegt keine Interessenkollision vor.");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Vollmacht zum Ausdrucken und Unterschreiben.</summary>
    public static string Vollmacht(MandatDaten d, string kanzlei)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Vollmacht");
        sb.AppendLine();
        sb.AppendLine("Hiermit erteile ich");
        sb.AppendLine(kanzlei.Trim().Length > 0 ? kanzlei.Trim() : "[Kanzlei / Rechtsanwältin / Rechtsanwalt]");
        sb.AppendLine();
        sb.AppendLine("in Sachen");
        sb.AppendLine($"**{d.Name}**{(d.Gegner.Trim().Length > 0 ? $"  ./.  **{d.Gegner.Trim()}**" : "")}");
        sb.AppendLine($"wegen {(d.Rechtsgebiet.Length > 0 ? d.Rechtsgebiet : "[Gegenstand]")}");
        sb.AppendLine();
        sb.AppendLine("Vollmacht");
        sb.AppendLine();
        sb.AppendLine("- zur außergerichtlichen und gerichtlichen Vertretung in allen Instanzen, einschließlich der Einlegung und Rücknahme von Rechtsmitteln;");
        sb.AppendLine("- zur Abgabe und Entgegennahme von Willenserklärungen im Zusammenhang mit dieser Angelegenheit;");
        sb.AppendLine("- zum Abschluss von Vergleichen, zum Verzicht und zur Anerkennung;");
        sb.AppendLine("- zur Entgegennahme von Zustellungen, Geld, Wertsachen und Urkunden, insbesondere der von der Gegenseite, der Justizkasse oder anderen Stellen zu erstattenden Beträge;");
        sb.AppendLine("- zur Bestellung von Vertretern und zur Erteilung von Untervollmacht;");
        sb.AppendLine("- zur Akteneinsicht.");
        if (d.IstFamilienrecht)
            sb.AppendLine("- in Familiensachen auch zur Stellung und Rücknahme von Scheidungsanträgen und zum Abschluss von Vereinbarungen über Scheidungsfolgen (besondere Vollmacht nach § 114 Abs. 5 FamFG).");
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("______________________________          ______________________________");
        sb.AppendLine("Ort, Datum                                         Unterschrift");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Auftrag an die KI im Chat: Ersteinschätzung, Fragen, Unterlagen, Risiken,
    /// nächste Schritte und eine Mandatsbestätigung als E-Mail-Entwurf.</summary>
    public static string KiAuftrag(MandatDaten d, string? frist, bool einschaetzung = true, bool bestaetigung = true,
        IReadOnlyList<string>? unterlagen = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Neues Mandat: {d.Name}");
        if (d.Rechtsgebiet.Length > 0) sb.AppendLine($"Rechtsgebiet: {d.Rechtsgebiet}");
        sb.AppendLine($"Gegenseite: {(d.Gegner.Trim().Length > 0 ? d.Gegner.Trim() : "noch unbekannt")}" +
                      (d.Gegnervertreter.Trim().Length > 0 ? $", vertreten durch {d.Gegnervertreter.Trim()}" : ""));
        sb.AppendLine($"Anliegen: {(d.Anliegen.Trim().Length > 0 ? d.Anliegen.Trim() : "noch nicht beschrieben")}");
        if (frist is not null) sb.AppendLine($"Laufende Frist: {frist}");
        if (unterlagen is { Count: > 0 })
            sb.AppendLine($"Mitgebrachte Unterlagen (liegen in der Akte, bitte berücksichtigen): {string.Join(", ", unterlagen)}");
        sb.AppendLine();
        if (einschaetzung)
        {
            sb.AppendLine("Bitte bereite mich auf das Mandat vor, kurz und in dieser Gliederung:");
            sb.AppendLine("1. Ersteinschätzung: Worum geht es rechtlich, welche Anspruchsgrundlagen, wo liegen die Knackpunkte (höchstens 6 Sätze).");
            sb.AppendLine("2. Fragen, die ich im Erstgespräch noch klären muss.");
            sb.AppendLine("3. Unterlagen, die der Mandant beibringen soll.");
            sb.AppendLine("4. Fristen und Risiken, auf die ich achten muss (nur benennen, kein Fristende ausrechnen).");
            sb.AppendLine("5. Die nächsten drei Schritte — jeweils mit einem Satz, was LexWolf dafür entwerfen kann.");
        }
        if (bestaetigung)
        {
            sb.Append(einschaetzung ? "Entwirf außerdem" : "Entwirf");
            sb.Append(" eine kurze Mandatsbestätigung an den Mandanten als E-Mail");
            sb.Append(d.Email.Trim().Length > 0 ? $" an {d.Email.Trim()}" : "");
            sb.AppendLine(": Dank für das Vertrauen, kurz was wir als Nächstes tun, welche Unterlagen wir brauchen, " +
                          "Bitte um Rücksendung der unterschriebenen Vollmacht und ein Satz zu unseren Datenschutzhinweisen.");
        }
        return sb.ToString().TrimEnd();
    }
}
