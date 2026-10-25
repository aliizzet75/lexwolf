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
public sealed record EntwurfKarteAnsicht(
    int Id,
    ChatEntwurf Entwurf,
    string Status,
    bool Aktiv,
    bool ArtUmschaltbar,
    bool AblegenMoeglich,
    string? OeffnenLabel,
    IReadOnlyList<string>? MandantAuswahl = null);

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
        html = Regex.Replace(html,
            @"(?i)(§\s*\d+(\s*(Abs\.\s*\d+|Satz\s*\d+))*\s*[A-Za-z]*\b|Art\.\s*\d+(\s*(Abs\.\s*\d+|Satz\s*\d+))*\s*[A-Za-z]*\b|\b(BGB|GG|KSchG|AGBG|AGB|AktG|GmbHG|HGB|StGB|StPO|ZPO|VwGO|BVerfG|BAG|BGH|BSG|LAG|OLG|LG|AG|EU-DSGVO|DSGVO|KWG|InsO|AO|BAföG|AsylbLG|SGB\s*(I{1,3}|IV|V|VI|VII|VIII|IX|X|XI|XII|1|2|3|4|5|6|7|8|9|10|11|12)))\b",
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
    /// </summary>
    public static string WrapAiBubble(string html, double? confidence = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class='ai-message'>");
        sb.AppendLine("  <div class='ai-bubble'>");
        if (confidence.HasValue)
            sb.AppendLine(ConfidenceBadgeHtml(confidence.Value));
        sb.AppendLine(html);
        sb.AppendLine("  </div>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    public static string WrapUserBubble(string html)
    {
        return $"<div class='user-message'><div class='user-bubble'>{html}</div></div>";
    }

    public static string WrapSystemMessage(string text)
    {
        return $"<div class='system-message'>{WebUtility.HtmlEncode(text)}</div>";
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
            if (k.AblegenMoeglich)
                sb.Append($"<button class='chat-action-button' data-action='uebernehmen' data-msg='{k.Id}'>📁 In Akte ablegen</button>");
            sb.Append($"<button class='chat-action-button' data-action='kopieren' data-msg='{k.Id}'>Text kopieren</button>");
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

    public static void SetOutputFont(string fontFamily, double fontSize)
    {
        _outputFontFamily = $"'{fontFamily}', system-ui, sans-serif";
        _outputFontSize = fontSize;
    }

    public static string GetChatCss()
    {
        return $@"
        <style>
            body {{ margin: 0; padding: 12px; font-family: {_outputFontFamily}; background: #0d1117; color: #c9d1d9; line-height: 1.5; }}

            .user-message {{ text-align: right; margin: 8px 0 12px 60px; }}
            .user-bubble {{ display: inline-block; background: #1f6feb; color: #fff; border-radius: 16px 4px 16px 16px; padding: 10px 14px; max-width: 85%; text-align: left; font-family: {_outputFontFamily}; font-size: {_outputFontSize}px; }}

            .ai-message {{ text-align: left; margin: 8px 0 12px 8px; }}
            .ai-bubble {{ display: inline-block; background: #21262d; color: #c9d1d9; border-radius: 4px 16px 16px 16px; padding: 12px 16px; max-width: 85%; font-family: {_outputFontFamily}; font-size: {_outputFontSize}px; }}

            .system-message {{ display: inline-block; text-align: center; background: #21262d; color: #8b949e; font-size: {Math.Max(10, _outputFontSize - 1)}px; border-radius: 12px; padding: 6px 14px; margin: 8px auto 12px auto; font-family: {_outputFontFamily}; }}

            .chat-paragraph {{ margin: 0 0 10px 0; font-family: {_outputFontFamily}; font-size: {_outputFontSize}px; }}
            .chat-paragraph:last-child {{ margin-bottom: 0; }}

            .chat-list {{ margin: 8px 0 10px 16px; padding-left: 16px; font-family: {_outputFontFamily}; font-size: {_outputFontSize}px; }}
            .chat-list li {{ margin-bottom: 4px; }}

            .code-block {{ background: #161b22; border: 1px solid #30363d; border-radius: 6px; padding: 10px 12px; margin: 10px 0; overflow-x: auto; font-family: Consolas, 'Courier New', monospace; font-size: {Math.Max(10, _outputFontSize - 1)}px; }}
            .code-block code {{ color: #e6edf3; background: transparent; padding: 0; white-space: pre; }}

            .inline-code {{ background: #161b22; color: #e6edf3; padding: 2px 5px; border-radius: 4px; font-family: Consolas, 'Courier New', monospace; font-size: {Math.Max(10, _outputFontSize - 1)}px; }}

            .chat-link {{ color: #58a6ff; text-decoration: none; }}
            .chat-link:hover {{ text-decoration: underline; }}

            .chat-heading {{ color: #e8a838; margin: 14px 0 8px 0; font-weight: 600; font-family: {_outputFontFamily}; font-size: {Math.Max(12, _outputFontSize + 2)}px; }}
            .chat-hr {{ border: 0; border-top: 1px solid #30363d; margin: 14px 0; }}

            .chat-action-button {{ display: inline-block; margin-top: 8px; padding: 8px 14px; border: 1px solid #30363d; border-radius: 6px; background: #21262d; color: #c9d1d9; font-size: {Math.Max(10, _outputFontSize - 1)}px; cursor: pointer; }}
            .chat-action-button.primary {{ color: #e8a838; }}
            .chat-action-button:hover {{ background: #30363d; }}

            .entwurf-karte {{ background: #161b22; border: 1px solid #30363d; border-left: 3px solid #e8a838; border-radius: 8px; padding: 12px 14px; margin: 8px 60px 12px 8px; font-family: {_outputFontFamily}; font-size: {_outputFontSize}px; }}
            .entwurf-karte.inaktiv {{ border-left-color: #30363d; opacity: 0.75; }}
            .ek-kopf {{ display: flex; align-items: center; justify-content: space-between; gap: 10px; }}
            .ek-titel {{ color: #e6edf3; font-weight: 600; }}
            .ek-art {{ display: inline-flex; border: 1px solid #30363d; border-radius: 6px; overflow: hidden; flex-shrink: 0; }}
            .ek-art-btn {{ background: transparent; color: #8b949e; border: 0; padding: 3px 10px; cursor: pointer; font-size: {Math.Max(10, _outputFontSize - 2)}px; }}
            .ek-art-btn.an {{ background: #30363d; color: #e6edf3; }}
            .ek-art-fest {{ color: #8b949e; font-size: {Math.Max(10, _outputFontSize - 2)}px; }}
            .ek-meta {{ color: #8b949e; font-size: {Math.Max(10, _outputFontSize - 1)}px; margin-top: 4px; }}
            .ek-text {{ margin-top: 8px; }}
            .ek-text summary {{ color: #8b949e; cursor: pointer; font-size: {Math.Max(10, _outputFontSize - 1)}px; }}
            .ek-volltext {{ white-space: pre-wrap; margin-top: 8px; padding: 10px 12px; background: #0d1117; border-radius: 6px; color: #c9d1d9; }}
            .ek-status {{ color: #3fb950; font-size: {Math.Max(10, _outputFontSize - 1)}px; margin-top: 8px; }}
            .entwurf-karte.inaktiv .ek-status {{ color: #8b949e; }}
            .ek-aktionen {{ display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin-top: 4px; }}
            .ek-aktionen .chat-action-button {{ margin-top: 4px; }}
            .ek-select {{ margin-top: 4px; padding: 7px 8px; background: #0d1117; color: #c9d1d9; border: 1px solid #30363d; border-radius: 6px; font-size: {Math.Max(10, _outputFontSize - 1)}px; }}

            .highlight-frist {{ color: #ff7b72; font-weight: 700; text-decoration: underline; }}
            .highlight-paragraph {{ color: #79c0ff; font-weight: 600; }}
            a.highlight-paragraph.quelle-link {{ text-decoration: none; cursor: pointer; border-bottom: 1px dotted #79c0ff; }}
            a.highlight-paragraph.quelle-link:hover {{ color: #a5d6ff; border-bottom-style: solid; }}
            .highlight-empfehlung {{ color: #e8a838; font-weight: 700; }}

            .confidence-badge {{ display: inline-block; float: right; margin: 0 0 8px 10px; padding: 2px 9px;
                                  border-radius: 10px; font-size: {Math.Max(10, _outputFontSize - 2)}px;
                                  font-weight: 700; font-family: {_outputFontFamily}; }}
            .confidence-badge.confidence-green {{ background: #2ea04326; color: #3fb950; }}
            .confidence-badge.confidence-yellow {{ background: #d2992226; color: #e8a838; }}
            .confidence-badge.confidence-red {{ background: #f8514926; color: #ff7b72; }}
        </style>";
    }
}
