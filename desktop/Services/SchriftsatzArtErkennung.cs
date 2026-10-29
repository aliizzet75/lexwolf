using System;
using System.Collections.Generic;
using System.Linq;

namespace LexWolf.Services;

/// <summary>
/// T#321: erkennt anhand einer Chat-Nachricht, ob eine der bekannten Schriftsatz-Arten
/// aus VorlagenOrdner.Arten gemeint ist. Bewusst einfacher lokerer Stichwort-/Teilwort-
/// Abgleich — kein neues KI-Modell nötig. Groß-/Kleinschreibung, Bindestriche und
/// Klammern/Gedankenstriche werden ignoriert, damit Formulierungen wie
/// "Schreib eine Klageerwiderung" oder "Berufungsbegründung zum Fall" erkannt werden.
/// </summary>
public static class SchriftsatzArtErkennung
{
    /// <summary>
    /// Prüft die Nachricht gegen alle bekannten Arten. Gibt den exakten Art-String
    /// aus VorlagenOrdner.Arten zurück, wenn eindeutig erkannt — sonst null.
    /// Mehrere gleich starke Treffer führen zu null (keine willkürliche Wahl).
    /// </summary>
    public static string? ErkenneArt(string nachricht)
    {
        if (string.IsNullOrWhiteSpace(nachricht))
            return null;

        var normalisiert = Normalisiere(nachricht);
        var treffer = new List<(string Art, int Stärke)>();

        foreach (var art in VorlagenOrdner.Arten)
        {
            var stärke = PassendeStärke(normalisiert, art);
            if (stärke > 0 && EnthaeltKernbegriffDerArt(normalisiert, art))
                treffer.Add((art, stärke));
        }

        if (treffer.Count == 0)
            return null;

        var max = treffer.Max(t => t.Stärke);
        var besten = treffer.Where(t => t.Stärke == max).ToList();

        // Nur dann eindeutig, wenn es genau einen besten Treffer gibt.
        if (besten.Count != 1)
            return null;

        // T#321: Sicherstellen, dass das Ergebnis als Schriftsatz-Anfrage gemeint ist.
        // Formulierungen wie "Schreib eine Klageerwiderung" enthalten ein Intent-Wort;
        // "Danke" oder "Guten Morgen" sollen nicht irrtümlich "Gegenseite" erkennen.
        if (!HatErstellungsIntent(normalisiert))
            return null;

        return besten[0].Art;
    }

    /// <summary>
    /// Bewertet, wie gut die Nachricht zu einer bekannten Schriftsatz-Art passt.
    /// Stärke 4 = zusammenhängender Art-Kern (Wörter der Art ohne Leerzeichen) kommt vor.
    /// Stärke 3 = exakte Art-Phrase als Ganzes.
    /// Stärke 2 = ein Wort der Art als ganzes Wort.
    /// Stärke 1 = signifikanter Teilstring der Art.
    /// Stärke 0 = kein Treffer.
    /// </summary>
    private static int PassendeStärke(string normalisierteNachricht, string art)
    {
        var normalisierteArt = Normalisiere(art);
        var wörter = normalisierteArt
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3)
            .ToList();

        var artKern = string.Join("", wörter);
        if (artKern.Length >= 4 && normalisierteNachricht.Contains(artKern, StringComparison.OrdinalIgnoreCase))
            return 4;

        if (normalisierteNachricht.Contains(normalisierteArt, StringComparison.OrdinalIgnoreCase))
            return 3;

        foreach (var wort in wörter)
        {
            if (IstGanzesWortVorhanden(normalisierteNachricht, wort))
                return 2;
        }

        foreach (var wort in wörter)
        {
            if (normalisierteNachricht.Contains(wort, StringComparison.OrdinalIgnoreCase))
                return 1;
        }

        return 0;
    }

    private static bool IstGanzesWortVorhanden(string text, string wort)
    {
        for (int i = 0; i <= text.Length - wort.Length; i++)
        {
            var idx = text.IndexOf(wort, i, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) break;
            i = idx;

            bool vorher = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
            bool nachher = idx + wort.Length >= text.Length || !char.IsLetterOrDigit(text[idx + wort.Length]);
            if (vorher && nachher)
                return true;
        }
        return false;
    }

    /// <summary>
    /// T#321: Verhindert, dass einzelne allgemeine Wörter fälschlicherweise eine
    /// bestimmte Schriftsatz-Art auswählen. Jede erkannte Art muss ihren eigenen
    /// kennzeichnenden Begriff in der Nachricht haben.
    /// </summary>
    private static bool EnthaeltKernbegriffDerArt(string normalisierteNachricht, string art)
    {
        var normArt = Normalisiere(art);
        var wörter = normArt.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        if (wörter.Contains("klageerwiderung"))
            return normalisierteNachricht.Contains("erwiderung", StringComparison.OrdinalIgnoreCase)
                || normalisierteNachricht.Contains("klage", StringComparison.OrdinalIgnoreCase);

        if (wörter.Contains("klageschrift"))
            return normalisierteNachricht.Contains("klage", StringComparison.OrdinalIgnoreCase);

        if (wörter.Contains("antragserwiderung"))
            return normalisierteNachricht.Contains("erwiderung", StringComparison.OrdinalIgnoreCase)
                && (normalisierteNachricht.Contains("antrag", StringComparison.OrdinalIgnoreCase)
                    || normalisierteNachricht.Contains("familie", StringComparison.OrdinalIgnoreCase)
                    || normalisierteNachricht.Contains("familiensache", StringComparison.OrdinalIgnoreCase)
                    || normalisierteNachricht.Contains("sorgerecht", StringComparison.OrdinalIgnoreCase));

        if (wörter.Contains("antragsschrift"))
            return normalisierteNachricht.Contains("antrag", StringComparison.OrdinalIgnoreCase)
                || normalisierteNachricht.Contains("familie", StringComparison.OrdinalIgnoreCase)
                || normalisierteNachricht.Contains("familiensache", StringComparison.OrdinalIgnoreCase);

        if (wörter.Contains("berufungsbegründung"))
            return normalisierteNachricht.Contains("berufung", StringComparison.OrdinalIgnoreCase)
                || normalisierteNachricht.Contains("begründung", StringComparison.OrdinalIgnoreCase);

        if (wörter.Contains("beschwerdebegründung"))
            return normalisierteNachricht.Contains("beschwerde", StringComparison.OrdinalIgnoreCase)
                || normalisierteNachricht.Contains("begründung", StringComparison.OrdinalIgnoreCase);

        if (wörter.Contains("stellungnahme"))
            return normalisierteNachricht.Contains("stellungnahme", StringComparison.OrdinalIgnoreCase)
                || normalisierteNachricht.Contains("gericht", StringComparison.OrdinalIgnoreCase);

        if (wörter.Contains("außergerichtliches") || wörter.Contains("schreiben"))
            return normalisierteNachricht.Contains("schreiben", StringComparison.OrdinalIgnoreCase)
                || normalisierteNachricht.Contains("gegenseite", StringComparison.OrdinalIgnoreCase)
                || normalisierteNachricht.Contains("gegner", StringComparison.OrdinalIgnoreCase)
                || normalisierteNachricht.Contains("außergerichtlich", StringComparison.OrdinalIgnoreCase);

        if (wörter.Contains("gutachten") || wörter.Contains("erfolgsaussichten"))
            return normalisierteNachricht.Contains("gutachten", StringComparison.OrdinalIgnoreCase)
                || normalisierteNachricht.Contains("erfolgsaussichten", StringComparison.OrdinalIgnoreCase)
                || normalisierteNachricht.Contains("einschätzung", StringComparison.OrdinalIgnoreCase);

        if (wörter.Contains("freier") || wörter.Contains("entwurf"))
            return normalisierteNachricht.Contains("freier entwurf", StringComparison.OrdinalIgnoreCase)
                || normalisierteNachricht.Contains("freien entwurf", StringComparison.OrdinalIgnoreCase)
                || normalisierteNachricht.Contains("frei entwurf", StringComparison.OrdinalIgnoreCase);

        return true;
    }

    /// <summary>
    /// T#321: verhindert, dass harmlose Floskeln wie "Danke" oder "Guten Morgen"
    /// fälschlich als Schriftsatz-Anfrage erkannt werden (z.B. weil "Schreiben" in
    /// "Außergerichtliches Schreiben an die Gegenseite" enthalten ist). Mindestens
    /// ein Schriftsatz-Intent-Wort muss in der Nachricht vorkommen.
    /// </summary>
    private static bool HatErstellungsIntent(string normalisiert)
    {
        var intentWoerter = new[]
        {
            "schreib", "erstell", "entwerf", "formulier", "verfass", "bereit",
            "brauche", "benötige", "mache", "machen", "erzeug", "draft", "entwurf",
            "bitte", "können", "kannst", "wird", "soll", "muss", "nehme", "nimm",
            "fertig", "vorbereiten", "vorbereit", "erledig", "mach", "schick",
        };
        return intentWoerter.Any(w => normalisiert.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalisiere(string text)
    {
        var ersetzt = text
            .Replace('(', ' ')
            .Replace(')', ' ')
            .Replace('/', ' ')
            .Replace('\\', ' ')
            .Replace('-', ' ')
            .Replace('–', ' ')
            .Replace('—', ' ')
            .ToLowerInvariant();
        return string.Join(" ", ersetzt
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 0));
    }
}
