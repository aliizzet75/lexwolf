using System.Collections.Generic;
using System.Linq;

namespace LexWolf.Services;

/// <summary>
/// T#310: baut aus der gewählten Schriftsatz-Art und den Vorgaben der Anwältin/des
/// Anwalts die Suchanfrage für die Agentic-RAG-Recherche (ReActStreamClient), die im
/// SchriftsatzDialog vor der Entwurfs-Erstellung läuft — damit Normen/Rechtsprechung
/// aus der echten Rechtsdatenbank statt nur aus dem Trainingswissen der KI stammen.
/// Reine, UI- und netzwerkfreie Funktion (testbar ohne WPF/Backend).
/// </summary>
public static class RechercheQueryBuilder
{
    private const int MaxLaenge = 300;

    /// <summary>Leer, wenn weder Art noch Anweisung etwas liefern — der Aufrufer
    /// soll dann keine Recherche anstoßen.</summary>
    public static string Build(string art, string anweisung)
    {
        var teile = new List<string>();
        if (!string.IsNullOrWhiteSpace(art)) teile.Add(art.Trim());
        if (!string.IsNullOrWhiteSpace(anweisung)) teile.Add(anweisung.Trim());

        var query = string.Join(" — ", teile.Where(t => t.Length > 0));
        return query.Length > MaxLaenge ? query[..MaxLaenge].TrimEnd() : query;
    }
}
