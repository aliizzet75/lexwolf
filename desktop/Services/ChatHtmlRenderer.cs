using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LexWolf.Services;

/// <summary>Was die Entwurfskarte anzeigt (siehe ChatHtmlRenderer.WrapEntwurfKarte).</summary>
/// <param name="Aktiv">false = nur Anzeige (Historie, durch neuere Fassung ersetzt).</param>
/// <param name="MandantAuswahl">Nicht leer = noch keinem Mandanten zugeordnet; Auswahl anzeigen.</param>
/// <param name="OeffnenLabel">z.B. "📄 In Word öffnen"; null = kein Öffnen-Button.</param>
/// <param name="Confidence">Task #301: confidence_score der /chat-Antwort, aus der dieser
/// Entwurf entstand (0..1); null (z.B. nach Neustart aus der Historie) zeigt kein Badge.</param>
/// <param name="Quellen">Task #301: geprüfte Zitate mit chunk_id (T#296), analog zu
/// WrapAiBubble — als klickbare Links, die zur exakten Stelle im Quellentext springen.</param>
/// <param name="BelegeAnsehenMoeglich">Task #321: true, wenn dieser Entwurf aus einem
/// Schriftsatz stammt und eine Belegprüfung anzeigbar ist.</param>
/// <param name="BelegePanelIndex">Task #321: Index in MainWindow._recherchePanels bzw.
/// -1, wenn kein separates Recherche-Panel vorhanden ist.</param>
public sealed record EntwurfKarteAnsicht(
    int Id,
    ChatEntwurf Entwurf,
    string Status,
    bool Aktiv,
    bool ArtUmschaltbar,
    bool AblegenMoeglich,
    string? OeffnenLabel,
    IReadOnlyList<string>? MandantAuswahl = null,
    double? Confidence = null,
    IReadOnlyList<(string Zitat, int ChunkId)>? Quellen = null,
    bool BelegeAnsehenMoeglich = false,
    int BelegePanelIndex = -1);

/// <summary>
/// Konvertiert KI-Antwort-Text (Markdown-ähnliche Struktur) in sauberes,
/// an das LexWolf-Dark-Theme angepasstes HTML für das WebView2-Chat-Panel.
/// </summary>
public static class ChatHtmlRenderer
{
    public static string Render(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var sb = new StringBuilder();
        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        bool inCodeBlock = false;
        var codeBuffer = new StringBuilder();
        var listBuffer = new StringBuilder();
        bool inList = false;
        char? listType = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine;

            // Code-Block: ``` or ~~~
            if (Regex.IsMatch(line.Trim(), "^(```|~~~)"))
            {
                if (inCodeBlock)
                {
                    sb.AppendLine($"<pre class='code-block'><code>{WebUtility.HtmlEncode(codeBuffer.ToString())}</code></pre>");
                    codeBuffer.Clear();
                    inCodeBlock = false;
                    continue;
                }

                CloseOpenList(sb, listBuffer, ref inList, ref listType);
                inCodeBlock = true;
                continue;
            }

            if (inCodeBlock)
            {
                codeBuffer.AppendLine(line);
                continue;
            }

            // Inline-Code: `text`
            line = Regex.Replace(line, "`([^`]+)`", m => $"<code class='inline-code'>{WebUtility.HtmlEncode(m.Groups[1].Value)}</code>");

            // Links: [text](url)
            line = Regex.Replace(line, @"\[(.+?)\]\((https?://[^\s)]+)\)", m =>
                $"<a class='chat-link' href='{WebUtility.HtmlEncode(m.Groups[2].Value)}' target='_blank'>{WebUtility.HtmlEncode(m.Groups[1].Value)}</a>");

            // Listen erkennen (-, *, + oder 1.)
            var orderedMatch = Regex.Match(line, "^(\\s*)(\\d+)\\.\\s+(.*)$");
            var unorderedMatch = Regex.Match(line, "^(\\s*)[-*+]\\s+(.*)$");

            if (orderedMatch.Success || unorderedMatch.Success)
            {
                var indentMatch = orderedMatch.Success ? orderedMatch : unorderedMatch;
                var currentType = orderedMatch.Success ? 'o' : 'u';

                if (!inList || listType != currentType)
                {
                    CloseOpenList(sb, listBuffer, ref inList, ref listType);
                    inList = true;
                    listType = currentType;
                }

                var content = orderedMatch.Success ? orderedMatch.Groups[3].Value : unorderedMatch.Groups[2].Value;
                listBuffer.AppendLine($"<li>{ApplySemanticHighlighting(RenderInlineFormatting(content))}</li>");
                continue;
            }
            else
            {
                CloseOpenList(sb, listBuffer, ref inList, ref listType);
            }

            // Überschriften
            var headingMatch = Regex.Match(line.Trim(), "^(#{1,6})\\s+(.*)$");
            if (headingMatch.Success)
            {
                var level = headingMatch.Groups[1].Length;
                var content = headingMatch.Groups[2].Value;
                sb.AppendLine($"<h{level} class='chat-heading'>{ApplySemanticHighlighting(RenderInlineFormatting(content))}</h{level}>");
                continue;
            }

            // Horizontal rule
            if (Regex.IsMatch(line.Trim(), "^(-{3,}|\\*{3,}|_{3,})$"))
            {
                sb.AppendLine("<hr class='chat-hr'/>");
                continue;
            }

            // Leere Zeile
            if (string.IsNullOrWhiteSpace(line))
            {
                sb.AppendLine("<br/>");
                continue;
            }

            // Normaler Absatz
            sb.AppendLine($"<p class='chat-paragraph'>{ApplySemanticHighlighting(RenderInlineFormatting(line))}</p>");
        }

        if (inCodeBlock)
        {
            sb.AppendLine($"<pre class='code-block'><code>{WebUtility.HtmlEncode(codeBuffer.ToString())}</code></pre>");
        }

        CloseOpenList(sb, listBuffer, ref inList, ref listType);

        return sb.ToString();
    }

    private static void CloseOpenList(StringBuilder sb, StringBuilder listBuffer, ref bool inList, ref char? listType)
    {
        if (!inList || listBuffer.Length == 0) return;

        var tag = listType == 'o' ? "ol" : "ul";
        sb.AppendLine($"<{tag} class='chat-list'>");
        sb.Append(listBuffer.ToString());
        sb.AppendLine($"</{tag}>");

        listBuffer.Clear();
        inList = false;
        listType = null;
    }

    private static string RenderInlineFormatting(string line)
    {
        // Fett: **text** oder __text__
        var result = Regex.Replace(line, "\\*\\*(.+?)\\*\\*|__(.+?)__", m =>
            $"<strong>{WebUtility.HtmlEncode(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)}</strong>");

        // Kursiv: *text* oder _text_
        result = Regex.Replace(result, "\\*(.+?)\\*|_(.+?)_", m =>
            $"<em>{WebUtility.HtmlEncode(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)}</em>");

        // Durchgestrichen: ~~text~~
        result = Regex.Replace(result, "~~(.+?)~~", m =>
            $"<del>{WebUtility.HtmlEncode(m.Groups[1].Value)}</del>");

        return result;
    }

    /// <summary>
    /// Wendet semantische Hervorhebungen für Fristen, Paragraphen und
    /// Handlungsempfehlungen auf gerenderten HTML-Text an.
    /// </summary>
    private static string ApplySemanticHighlighting(string html)
    {
        // 1) Fristen (rot, unterstrichen): Datumsangaben, Wochen-/Monatsfristen, Tagesangaben.
        html = Regex.Replace(html,
            @"(?i)(\d{1,2}\.\s*\d{1,2}\.\s*\d{2,4}|\d{1,2}\.\s*(Januar|Februar|März|April|Mai|Juni|Juli|August|September|Oktober|November|Dezember)\s*\d{2,4}|\b\d+\s*(Wochen|Monate|Tage|Tagen|Woche|Monat)\b|\b(Frist|Fristen|Deadline)\b)",
            m => $"<span class='highlight-frist'>{m.Value}</span>");

        // 2) Paragraphen (blau): §/Art./Abs. Referenzen inkl. Gesetzesabkürzungen.
        // Task #80: Treffer mit konkreter Nummer (§ 23 KSchG, Art. 3 GG, ...) sind
        // klickbar -> OnChatWebMessage löst sie über /legal-db/chunks/by-reference auf
        // und zeigt den Gesetzestext im Popup. Bloße Gesetzeskürzel ohne Nummer (z.B.
        // "nach dem BGB") bleiben reine Hervorhebung, da es dafür keinen eindeutigen Chunk gibt.
        // T#300: Abs./Satz/Nr. müssen alle drei in der Wiederholungsgruppe stehen, sonst
        // bricht der Treffer bei zusammengesetzten Normen wie "§ 573 Abs. 2 Nr. 2 BGB" (die
        // Eigenbedarfs-Norm) nach "Nr" ab — data-value trug dann nur "§ 573 Abs. 2 Nr" statt
        // der vollen Referenz, wodurch find_chunk_by_reference (backend) aus dem letzten Wort
        // "nr" statt "bgb" das Gesetz erraten wollte und nie etwas fand (HTTP 404 im Popup).
        html = Regex.Replace(html,
            @"(?i)(§\s*\d+(\s*(Abs\.\s*\d+|Satz\s*\d+|Nr\.\s*\d+))*\s*[A-Za-z]*\b|Art\.\s*\d+(\s*(Abs\.\s*\d+|Satz\s*\d+|Nr\.\s*\d+))*\s*[A-Za-z]*\b|\b(BGB|GG|KSchG|AGBG|AGB|AktG|GmbHG|HGB|StGB|StPO|ZPO|VwGO|BVerfG|BAG|BGH|BSG|LAG|OLG|LG|AG|EU-DSGVO|DSGVO|KWG|InsO|AO|BAföG|AsylbLG|SGB\s*(I{1,3}|IV|V|VI|VII|VIII|IX|X|XI|XII|1|2|3|4|5|6|7|8|9|10|11|12)))\b",
            m =>
            {
                if (!Regex.IsMatch(m.Value, @"\d"))
                    return $"<span class='highlight-paragraph'>{m.Value}</span>";
                var referenz = WebUtility.HtmlEncode(m.Value.Trim());
                return $"<a href='#' class='highlight-paragraph quelle-link' data-action='quelle' data-value='{referenz}'>{m.Value}</a>";
            });

        // 3) Handlungsempfehlungen (fett/orange): Modalverben und handlungsleitende Verben.
        html = Regex.Replace(html,
            @"(?i)\b(muss|sollten|sollt|sollte|soll|müssten|müsste|müsst|muss|empfehlen|empfahl|empfiehlt|empfehle|dringend|unbedingt|sofort|zunächst|anschließend|danach|abschließend|bitte\s+\w+|wenden\s+Sie\s+sich\s+an|vereinbaren\s+Sie\s+einen|reichen\s+Sie\s+ein|beantragen\s+Sie|legen\s+Sie\s+vor)\b",
            m => $"<span class='highlight-empfehlung'>{m.Value}</span>");

        return html;
    }

    /// <summary>Task #282: Ampel-Farbe (grün/gelb/rot) für einen confidence_score aus der
    /// /chat-Antwort des Backends. Gleiche Schwellen wie desktop/confidence_coloring.py
    /// (ConfidenceColorizer.score_to_color), damit Satz-Ebene und Antwort-Badge konsistent
    /// einfärben: &gt;0.8 grün, &gt;=0.5 gelb, sonst rot.</summary>
    public static string ConfidenceColor(double score) =>
        score > 0.8 ? "green" : score >= 0.5 ? "yellow" : "red";

    private static string ConfidenceBadgeHtml(double confidence)
    {
        var color = ConfidenceColor(confidence);
        var pct = (int)Math.Round(confidence * 100);
        return $"<span class='confidence-badge confidence-{color}' title='Konfidenz: {pct}%'>{pct}%</span>";
    }

    /// <summary>
    /// Baut eine vollständige Chat-Bubble aus einem KI-Text. Entwürfe (Schreiben,
    /// E-Mails) stehen nicht hier, sondern in einer eigenen Karte (WrapEntwurfKarte).
    /// <paramref name="confidence"/> ist der confidence_score der /chat-Antwort (0..1);
    /// null (z.B. historische Nachrichten ohne gespeicherten Score) zeigt kein Badge.
    /// <paramref name="quellen"/> (Task #297): geprüfte Zitate mit chunk_id (T#296) aus
    /// quellen_check der /chat-Antwort — als klickbare Links, die in MainWindow per
    /// OnChatWebMessage zur exakten Stelle im Quellentext springen.
    /// </summary>
    public static string WrapAiBubble(string html, double? confidence = null,
        IReadOnlyList<(string Zitat, int ChunkId)>? quellen = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class='ai-message'>");
        sb.AppendLine("  <div class='ai-bubble'>");
        if (confidence.HasValue)
            sb.AppendLine(ConfidenceBadgeHtml(confidence.Value));
        sb.AppendLine(html);
        if (quellen is { Count: > 0 })
            sb.AppendLine(QuellenListeHtml(quellen));
        sb.AppendLine("  </div>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    /// <summary>Klickbare Quellen-Links unter einer Chat-Antwort (Task #297). Nutzt die
    /// bestehende data-action-Bridge (siehe GetChatBaseHtml-Skript -> OnChatWebMessage):
    /// data-msg trägt hier die chunk_id, data-value das Zitat (für die Positionssuche
    /// im Quellentext per ChatHtmlRenderer.FindeZitatPosition).</summary>
    private static string QuellenListeHtml(IReadOnlyList<(string Zitat, int ChunkId)> quellen)
    {
        static string H(string s) => WebUtility.HtmlEncode(s);
        var sb = new StringBuilder();
        sb.Append("<div class='quellen-liste'>");
        foreach (var (zitat, chunkId) in quellen)
            sb.Append($"<button class='quelle-link' data-action='quelle' data-msg='{chunkId}' data-value='{H(zitat)}'>🔗 {H(zitat)} — zur Stelle</button>");
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>Position (Start, Länge) der zum Zitat gehörenden Passage in einem
    /// Quellen-Chunk-Text (Task #297, baut auf chunk_id aus T#296 auf). Sucht zunächst
    /// das volle Zitat ("§ 433 BGB"), dann nur §/Art.-Nummer (der Gesetzestext selbst
    /// nennt die Abkürzung oft nicht direkt daneben), erweitert den Treffer auf die
    /// umgebende Satzgrenze (., Zeilenumbruch) und markiert damit mehr als nur die
    /// Zitat-Zeichenfolge. (0, 0) wenn nichts gefunden wird -> Aufrufer zeigt dann den
    /// Dokumentanfang, was dem Verhalten vor #297 entspricht.</summary>
    public static (int Start, int Length) FindeZitatPosition(string text, string zitat)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(zitat)) return (0, 0);

        var idx = text.IndexOf(zitat, StringComparison.OrdinalIgnoreCase);
        var gesucht = zitat;
        if (idx < 0)
        {
            var m = Regex.Match(zitat, @"^(§|Art\.)\s*\d+");
            if (m.Success)
            {
                gesucht = m.Value;
                idx = text.IndexOf(gesucht, StringComparison.OrdinalIgnoreCase);
            }
        }
        if (idx < 0) return (0, 0);

        var satzStart = idx;
        while (satzStart > 0 && text[satzStart - 1] != '.' && text[satzStart - 1] != '\n') satzStart--;
        while (satzStart < text.Length && char.IsWhiteSpace(text[satzStart])) satzStart++;

        var satzEnde = idx + gesucht.Length;
        while (satzEnde < text.Length && text[satzEnde] != '.' && text[satzEnde] != '\n') satzEnde++;
        if (satzEnde < text.Length) satzEnde++; // Satzzeichen mit einschließen

        return (satzStart, Math.Max(satzEnde - satzStart, gesucht.Length));
    }

    public static string WrapUserBubble(string html)
    {
        return $"<div class='user-message'><div class='user-bubble'>{html}</div></div>";
    }

    /// <summary>T#309: eine Zeile im Live-Suche-Panel — Pendant zu
    /// ReActDialog.AddStepRow/BuildRow, nur als HTML statt WPF-Controls, damit
    /// der Live-Denkprozess der Agentic-Suche inline im Chat erscheint statt nur
    /// im separaten Fenster.</summary>
    public static string SearchStepRowHtml(string step, string? quelle, string text, double? confidence, string? status)
    {
        static string H(string s) => WebUtility.HtmlEncode(s);
        var verworfen = string.Equals(status, "verworfen", System.StringComparison.OrdinalIgnoreCase);
        var (icon, cls) = step switch
        {
            "suche_start" => ("🔎", "info"),
            "bewertung" => ("📊", "info"),
            "fertig" => ("✅", "done"),
            "suchergebnis" when verworfen => ("✗", "verworfen"),
            "suchergebnis" => ("✓", "gefunden"),
            "fehler" => ("⚠️", "fehler"),
            _ => ("•", "info"),
        };

        var konfidenzHtml = confidence.HasValue
            ? $"<span class='such-konfidenz'>{(int)System.Math.Round(confidence.Value * 100)}%</span>"
            : "";

        var sb = new StringBuilder();
        sb.Append($"<div class='such-schritt such-{cls}'>");
        sb.Append("<div class='such-kopf'>");
        sb.Append($"<span class='such-icon'>{icon}</span>");
        sb.Append($"<span class='such-quelle'>{H(quelle ?? step)}</span>");
        sb.Append(konfidenzHtml);
        sb.Append("</div>");
        if (!string.IsNullOrWhiteSpace(text))
            sb.Append($"<div class='such-text'>{H(text)}</div>");
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>T#320: kompakter, klickbarer Link im Chat, der ein separates WPF-Fenster
    /// mit den vollständigen Recherche-Informationen öffnet. Ersetzt den früheren
    /// aufklappbaren &lt;details class='such-panel'&gt;-Block im Chat-Fließtext.
    /// <paramref name="panelIndex"/> identifiziert den Eintrag in MainWindow._recherchePanels,
    /// damit der Klick-Handler die aktuellen Daten (Suchschritte + anonymisiert gesendet)
    /// an den Dialog übergeben kann.
    /// <paramref name="anonymisiertGesendet"/> (Task #311): der tatsächlich anonymisiert an
    /// den Server geschickte Text — wird im Dialog angezeigt, nicht mehr direkt im Chat.</summary>
    public static string WrapSearchPanel(string stepsHtml, bool laufend, string modus, int gefundenCount,
        string? anonymisiertGesendet = null, int panelIndex = -1)
    {
        var modusLabel = modus == "tief" ? "tief" : "schnell";
        string label;
        if (laufend)
            label = $"🔎 Recherche läuft … (Modus: {modusLabel})";
        else if (gefundenCount > 0)
            label = $"🔎 Recherche ansehen ({gefundenCount} Quelle(n))";
        else
            label = $"🔎 Recherche ansehen (keine Quellen)";

        var tooltip = laufend
            ? "Recherche läuft noch — Klick öffnet den bisherigen Stand."
            : "Klick öffnet Suchschritte und anonymisiert gesendeten Text.";

        var button = $"<button class='chat-action-button such-panel-button' " +
                     $"data-action='recherche' data-msg='{panelIndex}' title='{WebUtility.HtmlEncode(tooltip)}'>" +
                     $"{WebUtility.HtmlEncode(label)}</button>";

        // T#320: die eigentlichen Suchschritte und der anonymisierte Text stehen
        // NICHT mehr im Chat, sondern werden im Dialog per PanelIndex nachgeschlagen.
        return $"<div class='such-panel-compact'>{button}</div>";
    }

    /// <summary>T#320: baut den Inhalt des Recherche-Ergebnis-Dialogs als HTML.
    /// Wird von RechercheErgebnisDialog verwendet, um die bisherigen &lt;details&gt;-Inhalte
    /// formatiert und gut lesbar anzuzeigen.</summary>
    public static string BuildRechercheDialogHtml(string stepsHtml, bool laufend, string modus, int gefundenCount,
        string? anonymisiertGesendet)
    {
        var modusLabel = modus == "tief" ? "tief" : "schnell";
        string status;
        if (laufend)
            status = $"🔎 Live-Suche läuft … (Modus: {modusLabel})";
        else if (gefundenCount > 0)
            status = $"✅ Suche abgeschlossen (Modus: {modusLabel}) — {gefundenCount} Quelle(n) gefunden";
        else
            status = $"✅ Suche abgeschlossen (Modus: {modusLabel}) — keine Quellen gefunden";

        var anonymHtml = string.IsNullOrWhiteSpace(anonymisiertGesendet)
            ? ""
            : "<div class='such-anonym'><div class='such-anonym-titel'>🔒 Anonymisiert gesendet:</div>" +
              $"<div class='such-anonym-text'>{WebUtility.HtmlEncode(anonymisiertGesendet)}</div></div>";

        return $"<div class='recherche-dialog'><div class='recherche-status'>{WebUtility.HtmlEncode(status)}</div>" +
               $"<div class='such-liste'>{stepsHtml}</div>{anonymHtml}</div>";
    }

    public static string WrapSystemMessage(string text)
    {
        return $"<div class='system-message'>{WebUtility.HtmlEncode(text)}</div>";
    }

    /// <summary>Systemnachricht, die beim Mandantenwechsel eingeblendet wird,
    /// mit direkt klickbaren Aktionen für Zusammenfassung und Timeline.</summary>
    public static string WrapMandantGeladenHinweis(string mandantName)
    {
        static string H(string s) => WebUtility.HtmlEncode(s);
        var text = $"👤 Mandant {H(mandantName)} geladen.";
        return $"<div class='system-message'>{text}<br>" +
               $"<button class='chat-action-button primary' data-action='mandant-zusammenfassung'>📊 Zusammenfassung</button>" +
               $"<button class='chat-action-button' data-action='mandant-timeline'>🕐 Timeline</button></div>";
    }

    /// <summary>Entwurfskarte im Chat: Titel, Art (Brief/E-Mail umschaltbar), Empfänger,
    /// eingeklappter Text, Ablageort und Aktionen. Klicks gehen per data-action an
    /// MainWindow.OnChatWebMessage. Ohne Aktionen (Historie, ersetzte Fassung) bleibt
    /// nur die Anzeige.</summary>
    public static string WrapEntwurfKarte(EntwurfKarteAnsicht k)
    {
        static string H(string s) => WebUtility.HtmlEncode(s);
        var sb = new StringBuilder();
        sb.Append($"<div id='ek-{k.Id}' class='entwurf-karte{(k.Aktiv ? "" : " inaktiv")}'>");

        if (k.Confidence.HasValue)
            sb.Append(ConfidenceBadgeHtml(k.Confidence.Value));

        sb.Append("<div class='ek-kopf'>");
        sb.Append($"<span class='ek-titel'>{(k.Entwurf.IstEmail ? "✉️" : "📄")} {H(k.Entwurf.Titel)}</span>");
        if (k.ArtUmschaltbar)
        {
            sb.Append("<span class='ek-art'>");
            sb.Append($"<button class='ek-art-btn{(k.Entwurf.IstEmail ? "" : " an")}' data-action='art' data-value='brief' data-msg='{k.Id}'>Brief</button>");
            sb.Append($"<button class='ek-art-btn{(k.Entwurf.IstEmail ? " an" : "")}' data-action='art' data-value='email' data-msg='{k.Id}'>E-Mail</button>");
            sb.Append("</span>");
        }
        else
        {
            sb.Append($"<span class='ek-art-fest'>{(k.Entwurf.IstEmail ? "E-Mail" : "Brief")}</span>");
        }
        sb.Append("</div>");

        var meta = new List<string>();
        if (k.Entwurf.An != "") meta.Add($"An: {H(k.Entwurf.An)}");
        if (k.Entwurf.IstEmail && k.Entwurf.Betreff != "") meta.Add($"Betreff: {H(k.Entwurf.Betreff)}");
        if (meta.Count > 0) sb.Append($"<div class='ek-meta'>{string.Join(" · ", meta)}</div>");

        var text = DokumentExport.OhneMarkdown(k.Entwurf.Text).Replace("\r\n", "\n").Trim();
        var vorschau = string.Join(" ", text.Split('\n').Where(z => z.Trim() != "").Take(3).Select(z => z.Trim()));
        if (vorschau.Length > 220) vorschau = vorschau[..220].TrimEnd() + " …";
        sb.Append($"<details class='ek-text'><summary>{H(vorschau)}</summary><div class='ek-volltext'>{H(text)}</div></details>");

        if (k.Status != "") sb.Append($"<div class='ek-status'>{H(k.Status)}</div>");

        if (k.Quellen is { Count: > 0 } quellen)
            sb.Append(QuellenListeHtml(quellen));

        if (k.MandantAuswahl is { Count: > 0 } auswahl)
        {
            sb.Append("<div class='ek-aktionen'>");
            sb.Append($"<select class='ek-select' id='ek-mandant-{k.Id}'><option value=''>Mandant wählen …</option>");
            foreach (var name in auswahl) sb.Append($"<option value='{H(name)}'>{H(name)}</option>");
            sb.Append("</select>");
            sb.Append($"<button class='chat-action-button primary' data-action='zuordnen' data-msg='{k.Id}' data-select='ek-mandant-{k.Id}'>In Entwürfe ablegen</button>");
            sb.Append("</div>");
        }

        if (k.Aktiv)
        {
            sb.Append("<div class='ek-aktionen'>");
            if (k.OeffnenLabel is not null)
                sb.Append($"<button class='chat-action-button primary' data-action='oeffnen' data-msg='{k.Id}'>{H(k.OeffnenLabel)}</button>");
            if (k.BelegeAnsehenMoeglich)
                sb.Append($"<button class='chat-action-button' data-action='belege' data-msg='{k.Id}'>📋 Belege ansehen</button>");
            if (k.AblegenMoeglich)
                sb.Append($"<button class='chat-action-button' data-action='uebernehmen' data-msg='{k.Id}'>📁 In Akte ablegen</button>");
            sb.Append($"<button class='chat-action-button' data-action='kopieren' data-msg='{k.Id}'>Text kopieren</button>");
            sb.Append($"<button class='chat-action-button' data-action='vorlage' data-msg='{k.Id}'>💾 Als Vorlage speichern</button>");
            sb.Append("</div>");
        }

        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>
    /// Liefert das gemeinsame CSS für alle Chat-Nachrichten (Dark Theme).
    /// </summary>
    private static string _outputFontFamily = "'Segoe UI', system-ui, sans-serif";
    private static double _outputFontSize = 13;
    private static bool _dunkel = true;

    public static void SetOutputFont(string fontFamily, double fontSize)
    {
        _outputFontFamily = $"'{fontFamily}', system-ui, sans-serif";
        _outputFontSize = fontSize;
    }

    public static void SetTheme(bool dunkel)
    {
        _dunkel = dunkel;
    }

    private static string HexToRgb(string hex)
    {
        if (hex.Length == 7 && hex[0] == '#')
        {
            var r = int.Parse(hex.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
            var g = int.Parse(hex.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
            var b = int.Parse(hex.Substring(5, 2), System.Globalization.NumberStyles.HexNumber);
            return $"{r}, {g}, {b}";
        }
        return "0, 0, 0";
    }

    public static string GetChatCss()
    {
        var t = Theme.Aus(_dunkel);
        return $@"
        <style>
            :root {{
                --lw-bg: {t.Bg};
                --lw-panel: {t.Panel};
                --lw-raised: {t.Raised};
                --lw-border: {t.Border};
                --lw-text: {t.Text};
                --lw-text-strong: {t.TextStrong};
                --lw-muted: {t.Muted};
                --lw-accent: {t.Akzent};
                --lw-red: {t.Red};
                --lw-orange: {t.Orange};
                --lw-green: {t.Green};
                --lw-blue: {t.Blue};
                --lw-primary: {t.Primary};
                --lw-green-rgb: {HexToRgb(t.Green)};
                --lw-orange-rgb: {HexToRgb(t.Orange)};
                --lw-red-rgb: {HexToRgb(t.Red)};
                --lw-accent-rgb: {HexToRgb(t.Akzent)};
            }}

            body {{ margin: 0; padding: 12px; font-family: {_outputFontFamily}; background: var(--lw-bg); color: var(--lw-text); line-height: 1.5; }}

            .user-message {{ text-align: right; margin: 8px 0 12px 60px; }}
            .user-bubble {{ display: inline-block; background: var(--lw-primary); color: var(--lw-text-strong); border-radius: 16px 4px 16px 16px; padding: 10px 14px; max-width: 85%; text-align: left; font-family: {_outputFontFamily}; font-size: {_outputFontSize}px; }}

            .ai-message {{ text-align: left; margin: 8px 0 12px 8px; }}
            .ai-bubble {{ display: inline-block; background: var(--lw-raised); color: var(--lw-text); border-radius: 4px 16px 16px 16px; padding: 12px 16px; max-width: 85%; font-family: {_outputFontFamily}; font-size: {_outputFontSize}px; }}

            .system-message {{ display: inline-block; text-align: center; background: var(--lw-raised); color: var(--lw-muted); font-size: {Math.Max(10, _outputFontSize - 1)}px; border-radius: 12px; padding: 6px 14px; margin: 8px auto 12px auto; font-family: {_outputFontFamily}; }}

            .chat-paragraph {{ margin: 0 0 10px 0; font-family: {_outputFontFamily}; font-size: {_outputFontSize}px; }}
            .chat-paragraph:last-child {{ margin-bottom: 0; }}

            .chat-list {{ margin: 8px 0 10px 16px; padding-left: 16px; font-family: {_outputFontFamily}; font-size: {_outputFontSize}px; }}
            .chat-list li {{ margin-bottom: 4px; }}

            .code-block {{ background: var(--lw-panel); border: 1px solid var(--lw-border); border-radius: 6px; padding: 10px 12px; margin: 10px 0; overflow-x: auto; font-family: Consolas, 'Courier New', monospace; font-size: {Math.Max(10, _outputFontSize - 1)}px; }}
            .code-block code {{ color: var(--lw-text-strong); background: transparent; padding: 0; white-space: pre; }}

            .inline-code {{ background: var(--lw-panel); color: var(--lw-text-strong); padding: 2px 5px; border-radius: 4px; font-family: Consolas, 'Courier New', monospace; font-size: {Math.Max(10, _outputFontSize - 1)}px; }}

            .chat-link {{ color: var(--lw-blue); text-decoration: none; }}
            .chat-link:hover {{ text-decoration: underline; }}

            .chat-heading {{ color: var(--lw-accent); margin: 14px 0 8px 0; font-weight: 600; font-family: {_outputFontFamily}; font-size: {Math.Max(12, _outputFontSize + 2)}px; }}
            .chat-hr {{ border: 0; border-top: 1px solid var(--lw-border); margin: 14px 0; }}

            .chat-action-button {{ display: inline-block; margin-top: 8px; padding: 8px 14px; border: 1px solid var(--lw-border); border-radius: 6px; background: var(--lw-raised); color: var(--lw-text); font-size: {Math.Max(10, _outputFontSize - 1)}px; cursor: pointer; }}
            .chat-action-button.primary {{ color: var(--lw-accent); }}
            .chat-action-button:hover {{ background: var(--lw-border); }}

            .quellen-liste {{ margin-top: 10px; padding-top: 8px; border-top: 1px solid var(--lw-border); display: flex; flex-wrap: wrap; gap: 6px; }}
            .quelle-link {{ border: 1px solid var(--lw-border); border-radius: 6px; background: transparent; color: var(--lw-blue); padding: 4px 9px; font-size: {Math.Max(10, _outputFontSize - 2)}px; cursor: pointer; }}
            .quelle-link:hover {{ background: var(--lw-raised); text-decoration: underline; }}

            .entwurf-karte {{ background: var(--lw-panel); border: 1px solid var(--lw-border); border-left: 3px solid var(--lw-accent); border-radius: 8px; padding: 12px 14px; margin: 8px 60px 12px 8px; font-family: {_outputFontFamily}; font-size: {_outputFontSize}px; }}
            .entwurf-karte.inaktiv {{ border-left-color: var(--lw-border); opacity: 0.75; }}
            .ek-kopf {{ display: flex; align-items: center; justify-content: space-between; gap: 10px; }}
            .ek-titel {{ color: var(--lw-text-strong); font-weight: 600; }}
            .ek-art {{ display: inline-flex; border: 1px solid var(--lw-border); border-radius: 6px; overflow: hidden; flex-shrink: 0; }}
            .ek-art-btn {{ background: transparent; color: var(--lw-muted); border: 0; padding: 3px 10px; cursor: pointer; font-size: {Math.Max(10, _outputFontSize - 2)}px; }}
            .ek-art-btn.an {{ background: var(--lw-border); color: var(--lw-text-strong); }}
            .ek-art-fest {{ color: var(--lw-muted); font-size: {Math.Max(10, _outputFontSize - 2)}px; }}
            .ek-meta {{ color: var(--lw-muted); font-size: {Math.Max(10, _outputFontSize - 1)}px; margin-top: 4px; }}
            .ek-text {{ margin-top: 8px; }}
            .ek-text summary {{ color: var(--lw-muted); cursor: pointer; font-size: {Math.Max(10, _outputFontSize - 1)}px; }}
            .ek-volltext {{ white-space: pre-wrap; margin-top: 8px; padding: 10px 12px; background: var(--lw-bg); border-radius: 6px; color: var(--lw-text); }}
            .ek-status {{ color: var(--lw-green); font-size: {Math.Max(10, _outputFontSize - 1)}px; margin-top: 8px; }}
            .entwurf-karte.inaktiv .ek-status {{ color: var(--lw-muted); }}
            .ek-aktionen {{ display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin-top: 4px; }}
            .ek-aktionen .chat-action-button {{ margin-top: 4px; }}
            .ek-select {{ margin-top: 4px; padding: 7px 8px; background: var(--lw-bg); color: var(--lw-text); border: 1px solid var(--lw-border); border-radius: 6px; font-size: {Math.Max(10, _outputFontSize - 1)}px; }}

            .highlight-frist {{ color: var(--lw-red); font-weight: 700; text-decoration: underline; }}
            .highlight-paragraph {{ color: var(--lw-blue); font-weight: 600; }}
            a.highlight-paragraph.quelle-link {{ text-decoration: none; cursor: pointer; border-bottom: 1px dotted var(--lw-blue); }}
            a.highlight-paragraph.quelle-link:hover {{ color: var(--lw-blue); border-bottom-style: solid; }}
            .highlight-empfehlung {{ color: var(--lw-accent); font-weight: 700; }}

            .confidence-badge {{ display: inline-block; float: right; margin: 0 0 8px 10px; padding: 2px 9px;
                                  border-radius: 10px; font-size: {Math.Max(10, _outputFontSize - 2)}px;
                                  font-weight: 700; font-family: {_outputFontFamily}; }}
            .confidence-badge.confidence-green {{ background: rgba(var(--lw-green-rgb), 0.15); color: var(--lw-green); }}
            .confidence-badge.confidence-yellow {{ background: rgba(var(--lw-orange-rgb), 0.15); color: var(--lw-accent); }}
            .confidence-badge.confidence-red {{ background: rgba(var(--lw-red-rgb), 0.15); color: var(--lw-red); }}

            .such-panel {{ margin: 8px 8px 12px 8px; border: 1px solid var(--lw-border); border-radius: 8px; background: var(--lw-panel); }}
            .such-panel summary {{ cursor: pointer; padding: 10px 14px; color: var(--lw-text); font-size: {_outputFontSize}px; font-weight: 600; list-style: none; }}
            .such-panel-compact {{ margin: 8px 8px 12px 8px; display: inline-block; }}
            .such-panel-button {{ display: inline-flex; align-items: center; gap: 6px; padding: 8px 14px; border: 1px solid var(--lw-border); border-radius: 6px; background: var(--lw-raised); color: var(--lw-text); font-size: {_outputFontSize}px; cursor: pointer; }}
            .such-panel-button:hover {{ background: var(--lw-border); border-color: var(--lw-blue); color: var(--lw-text-strong); }}
            .recherche-dialog {{ padding: 14px; }}
            .recherche-status {{ padding: 10px 14px; margin-bottom: 12px; border: 1px solid var(--lw-border); border-radius: 8px; background: var(--lw-panel); color: var(--lw-text); font-weight: 600; font-size: {_outputFontSize}px; }}
            .such-leer {{ color: var(--lw-muted); font-size: {_outputFontSize}px; padding: 8px 2px; }}
            .such-liste {{ display: flex; flex-direction: column; gap: 8px; }}
            .such-schritt {{ border: 1px solid var(--lw-border); border-radius: 6px; padding: 8px 11px; background: var(--lw-bg); }}
            .such-kopf {{ display: flex; align-items: center; gap: 6px; }}
            .such-icon {{ font-size: {Math.Max(13, _outputFontSize + 1)}px; }}
            .such-quelle {{ color: var(--lw-text); font-weight: 600; font-size: {_outputFontSize}px; }}
            .such-konfidenz {{ margin-left: auto; color: var(--lw-muted); font-size: {Math.Max(10, _outputFontSize - 1)}px; }}
            .such-text {{ margin-top: 4px; color: var(--lw-muted); font-size: {Math.Max(11, _outputFontSize - 1)}px; line-height: 1.5; }}
            .such-schritt.such-gefunden .such-quelle {{ color: var(--lw-green); }}
            .such-schritt.such-verworfen .such-quelle {{ color: var(--lw-red); }}
            .such-schritt.such-fehler .such-quelle {{ color: var(--lw-red); }}
            .such-schritt.such-done .such-quelle {{ color: var(--lw-green); }}

            .such-anonym {{ margin: 4px 0 0 0; padding: 10px 14px 12px 14px; border: 1px solid var(--lw-border); border-radius: 8px; background: rgba(var(--lw-accent-rgb), 0.06); }}
            .such-anonym-titel {{ color: var(--lw-accent); font-weight: 700; font-size: {_outputFontSize}px; margin-bottom: 6px; }}
            .such-anonym-text {{ white-space: pre-wrap; color: var(--lw-text); font-size: {Math.Max(11, _outputFontSize - 1)}px; line-height: 1.5; max-height: 280px; overflow-y: auto; background: var(--lw-bg); border: 1px solid var(--lw-accent); border-radius: 6px; padding: 10px; font-family: Consolas, 'Courier New', monospace; }}
        </style>";
    }
}
