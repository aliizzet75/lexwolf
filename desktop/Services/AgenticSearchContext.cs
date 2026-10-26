using System.Collections.Generic;
using System.Linq;
using System.Text;
using LexWolf.Models;

namespace LexWolf.Services;

/// <summary>
/// T#309: baut aus den Live-Suchschritten des ReAct-SSE-Streams
/// (GET /api/search/stream, siehe ReActStreamClient) einen kompakten
/// Textblock, der als zusätzlicher mandant_context an /chat geht — damit
/// die im Chat sichtbare Agentic-Suche auch tatsächlich die Antwort und
/// deren Quellenangaben beeinflusst, statt nur eine Anzeige zu sein.
/// Bewusst als reine, netzwerkfreie Funktion (testbar ohne Backend).
/// </summary>
public static class AgenticSearchContext
{
    /// <summary>
    /// Nimmt nur "suchergebnis"-Schritte mit Status != verworfen — verworfene
    /// Treffer lagen unter der Konfidenzschwelle (CONFIDENCE_THRESHOLD, siehe
    /// react_engine.py) und sollen die Antwort nicht mitprägen, genau wie sie
    /// im Live-Panel optisch als verworfen markiert sind.
    /// Gibt null zurück, wenn nichts Verwertbares gefunden wurde, damit der
    /// Aufrufer dem /chat-Request keinen leeren Kontext-Abschnitt anhängt.
    /// </summary>
    public static string? BuildKontext(IReadOnlyList<ReActStepEvent> events, string modus)
    {
        var gefunden = events
            .Where(e => e.Step == "suchergebnis" && !e.IstVerworfen && !string.IsNullOrWhiteSpace(e.Text))
            .ToList();
        if (gefunden.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine($"Ergebnisse der Agentic-Suche (Modus: {modus}) — als Grundlage für die Antwort, bitte bei Zitaten referenzieren:");
        foreach (var e in gefunden)
        {
            var quelle = string.IsNullOrWhiteSpace(e.Quelle) ? "Quelle" : e.Quelle;
            sb.AppendLine($"- [{quelle}] {e.Text}");
        }
        return sb.ToString().TrimEnd();
    }
}
