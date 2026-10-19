using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LexWolf.Services;

public sealed record MandantKandidat(string Id, string Name, string Aktenzeichen, string Gegner, string Gegnervertreter);

public sealed record Zuordnung(string? MandantId, string Grund);

/// <summary>
/// Ordnet eine eingehende E-Mail einem Mandanten zu. Reihenfolge der Stärke:
/// Aktenzeichen im Betreff/Text → bekannter Absender (früher zugeordnet) →
/// Mandantenname → Name von Gegner/Gegnervertreter. Bei Gleichstand zwischen
/// verschiedenen Mandanten wird nicht geraten — die Mail landet dann unter
/// "Nicht zugeordnet" und der Anwalt entscheidet.
/// </summary>
public static class MailZuordnung
{
    public static Zuordnung Finde(string absenderAdresse, string absenderName, string betreff, string text,
        IEnumerable<MandantKandidat> mandanten, IReadOnlyDictionary<string, string> bekannteAbsender)
    {
        var kopf = Kollisionspruefung.Normalisiere($"{absenderName} {betreff}");
        var alles = Kollisionspruefung.Normalisiere($"{absenderName} {betreff} {text}");
        var kopfTokens = Tokens(kopf);
        var allesTokens = Tokens(alles);
        var kompakt = Kompakt($"{betreff} {text}");

        var punkte = new Dictionary<string, (int Punkte, string Grund)>();
        void Merke(string id, int p, string grund)
        {
            if (!punkte.TryGetValue(id, out var bisher) || p > bisher.Punkte) punkte[id] = (p, grund);
        }

        foreach (var m in mandanten)
        {
            if (m.Aktenzeichen.Length >= 5 && kompakt.Contains(Kompakt(m.Aktenzeichen), StringComparison.Ordinal))
                Merke(m.Id, 100, $"Aktenzeichen {m.Aktenzeichen}");

            if (NameEnthalten(m.Name, kopfTokens)) Merke(m.Id, 60, "Mandantenname im Absender/Betreff");
            else if (NameEnthalten(m.Name, allesTokens)) Merke(m.Id, 45, "Mandantenname im Text");

            if (NameEnthalten(m.Gegner, allesTokens)) Merke(m.Id, 30, $"Gegner {m.Gegner}");
            if (NameEnthalten(m.Gegnervertreter, kopfTokens)) Merke(m.Id, 30, $"Gegnervertreter {m.Gegnervertreter}");
        }

        if (!string.IsNullOrEmpty(absenderAdresse)
            && bekannteAbsender.TryGetValue(absenderAdresse.Trim().ToLowerInvariant(), out var bekannt)
            && mandanten.Any(m => m.Id == bekannt))
            Merke(bekannt, 80, $"bekannter Absender {absenderAdresse}");

        if (punkte.Count == 0) return new Zuordnung(null, "kein Hinweis auf einen Mandanten");
        var sortiert = punkte.OrderByDescending(p => p.Value.Punkte).ToList();
        if (sortiert.Count > 1 && sortiert[0].Value.Punkte == sortiert[1].Value.Punkte)
            return new Zuordnung(null, "mehrere Mandanten möglich");
        // Nur ein schwacher Hinweis (Gegner-Name) reicht nicht für eine automatische Ablage.
        if (sortiert[0].Value.Punkte < 45) return new Zuordnung(null, $"nur schwacher Hinweis ({sortiert[0].Value.Grund})");
        return new Zuordnung(sortiert[0].Key, sortiert[0].Value.Grund);
    }

    private static HashSet<string> Tokens(string normalisiert) =>
        normalisiert.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    /// <summary>Alle Namensteile (mind. 2, je mind. 2 Zeichen) kommen vor — "Weber" allein ist zu unsicher.</summary>
    private static bool NameEnthalten(string name, HashSet<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var teile = Kollisionspruefung.Normalisiere(name).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1).ToList();
        return teile.Count >= 2 && teile.All(tokens.Contains);
    }

    /// <summary>Aktenzeichen-Vergleich ohne Leerzeichen/Groß-Klein: "312 F 45/26" = "312F45/26".</summary>
    private static string Kompakt(string s) => Regex.Replace(s.ToLowerInvariant(), @"\s+", "");
}
