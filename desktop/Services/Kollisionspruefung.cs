using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LexWolf.Services;

/// <summary>Eine Person/Firma in einer Akte (aus Aktenstamm oder Dokument-Auswertung).</summary>
public sealed record AktenBeteiligter(string MandantId, string MandantName, string Rolle, string Name);

public sealed record Kollision(string Art, string Beschreibung, string AndereAkte);

/// <summary>
/// Interessenkollision (§ 43a Abs. 4 BRAO, § 3 BORA): Ist die Gegenseite in
/// einer anderen Akte selbst Mandant — oder der Mandant dort Gegner? Namen
/// werden tolerant verglichen ("Mustermann, Max" = "Herr Dr. Max Mustermann",
/// "Müller GmbH" = "Mueller"), weil Ordnernamen und Schriftsätze sie selten
/// gleich schreiben. Ergebnis ist ein Hinweis zur Prüfung, keine Entscheidung.
/// </summary>
public static class Kollisionspruefung
{
    private static readonly HashSet<string> Fuellwoerter = new(StringComparer.Ordinal)
    {
        "herr", "herrn", "frau", "dr", "prof", "rechtsanwalt", "rechtsanwaeltin", "ra", "rain", "ra in",
        "gmbh", "ag", "kg", "ohg", "gbr", "ug", "haftungsbeschraenkt", "ek", "ev", "co", "mbh", "und", "u",
        "geb", "verw", "dipl", "ing", "kfm", "eheleute", "familie",
    };

    public static string Normalisiere(string name)
    {
        var s = name.ToLowerInvariant()
            .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
        s = Regex.Replace(s, @"[^a-z\s]", " ");
        var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => !Fuellwoerter.Contains(t));
        return string.Join(' ', tokens);
    }

    private static HashSet<string> Tokens(string normalisiert) =>
        normalisiert.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length > 1).ToHashSet();

    /// <summary>Gleiche Person/Firma? Exakt gleich normalisiert, oder alle Namensteile
    /// des kürzeren Namens (mind. 2) kommen im längeren vor.</summary>
    public static bool GleicherName(string a, string b)
    {
        var na = Normalisiere(a);
        var nb = Normalisiere(b);
        if (na.Length == 0 || nb.Length == 0) return false;
        if (na == nb) return true;
        var ta = Tokens(na);
        var tb = Tokens(nb);
        var (klein, gross) = ta.Count <= tb.Count ? (ta, tb) : (tb, ta);
        return klein.Count >= 2 && klein.All(gross.Contains);
    }

    public static bool IstGegenseite(string rolle) =>
        rolle.Contains("gegner", StringComparison.OrdinalIgnoreCase)
        || rolle.Contains("antragsgegner", StringComparison.OrdinalIgnoreCase)
        || rolle.Contains("beklagt", StringComparison.OrdinalIgnoreCase);

    /// <summary>Prüft eine Akte gegen alle anderen.</summary>
    /// <param name="mandantId">Die zu prüfende Akte.</param>
    /// <param name="mandantName">Name des Mandanten dieser Akte.</param>
    /// <param name="alleMandanten">Alle Akten (Id, Name).</param>
    /// <param name="alleBeteiligten">Beteiligte aller Akten.</param>
    public static List<Kollision> Pruefe(string mandantId, string mandantName,
        IEnumerable<(string Id, string Name)> alleMandanten, IReadOnlyList<AktenBeteiligter> alleBeteiligten)
    {
        var treffer = new List<Kollision>();
        var mandanten = alleMandanten.ToList();
        var eigeneGegner = alleBeteiligten.Where(b => b.MandantId == mandantId && IstGegenseite(b.Rolle)).ToList();

        // 1) Gegner dieser Akte ist anderswo Mandant
        foreach (var g in eigeneGegner)
            foreach (var m in mandanten.Where(m => m.Id != mandantId && GleicherName(g.Name, m.Name)))
                treffer.Add(new Kollision("Gegner ist Mandant",
                    $"„{g.Name}“ ({g.Rolle}) ist in einer anderen Akte selbst Mandant.", m.Name));

        // 2) Mandant dieser Akte ist anderswo Gegner
        foreach (var b in alleBeteiligten.Where(b => b.MandantId != mandantId && IstGegenseite(b.Rolle)
                                                     && GleicherName(b.Name, mandantName)))
            treffer.Add(new Kollision("Mandant ist Gegner",
                $"Ihr Mandant „{mandantName}“ ist in einer anderen Akte {b.Rolle}.", b.MandantName));

        return treffer
            .GroupBy(k => (k.Art, k.AndereAkte))
            .Select(g => g.First())
            .ToList();
    }
}
