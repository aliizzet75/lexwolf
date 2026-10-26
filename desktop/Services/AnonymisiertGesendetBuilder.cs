using System.Collections.Generic;
using System.Linq;

namespace LexWolf.Services;

/// <summary>
/// T#311: baut den Anzeige-Text für den Live-Panel-Abschnitt "Anonymisiert gesendet"
/// (Chat und Schriftsatz) — aus genau den Textteilen, die AnonymizerEngine.AnonymizeText
/// tatsächlich an den Server schickt (Task #308), nicht aus der Original-Eingabe. Reine,
/// UI- und netzwerkfreie Funktion (testbar ohne WebView2/WPF, siehe RechercheQueryBuilder
/// für dasselbe Muster).
/// </summary>
public static class AnonymisiertGesendetBuilder
{
    /// <summary>Für MainWindow.PostChatAsync: die anonymisierte Nachricht dieser Runde plus
    /// den anonymisierten Mandant-Kontext (Akte-Dokumente, Mandantname etc.), falls mitgeschickt.</summary>
    public static string BuildFuerChat(string anonymisierteNachricht, string? anonymisierterKontext)
    {
        var teile = new List<string>();
        if (anonymisierteNachricht.Length > 0) teile.Add(anonymisierteNachricht);
        if (!string.IsNullOrWhiteSpace(anonymisierterKontext))
            teile.Add("--- Kontext (anonymisiert) ---\n" + anonymisierterKontext);
        return string.Join("\n\n", teile);
    }

    /// <summary>Für SchriftsatzDialog.OnErstellen: die anonymisierte Anweisung plus die
    /// anonymisierten Texte der ausgewählten Akte-Dokumente, falls welche einbezogen wurden.</summary>
    public static string BuildFuerSchriftsatz(string anonymisierteAnweisung, IReadOnlyList<(string Titel, string Text)> anonymisierteDokumente)
    {
        var teile = new List<string>();
        if (anonymisierteAnweisung.Length > 0) teile.Add(anonymisierteAnweisung);
        if (anonymisierteDokumente.Count > 0)
            teile.Add("--- Dokumente (anonymisiert) ---\n" +
                string.Join("\n\n", anonymisierteDokumente.Select(d => $"[{d.Titel}]\n{d.Text}")));
        return string.Join("\n\n", teile);
    }
}
