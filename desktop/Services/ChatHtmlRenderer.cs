using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LexWolf.Services;

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
        html = Regex.Replace(html,
            @"(?i)(§\s*\d+(\s*(Abs\.\s*\d+|Satz\s*\d+))*\s*[A-Za-z]*\b|Art\.\s*\d+(\s*(Abs\.\s*\d+|Satz\s*\d+))*\s*[A-Za-z]*\b|\b(BGB|GG|KSchG|AGBG|AGB|AktG|GmbHG|HGB|StGB|StPO|ZPO|VwGO|BVerfG|BAG|BGH|BSG|LAG|OLG|LG|AG|EU-DSGVO|DSGVO|KWG|InsO|AO|BAföG|AsylbLG|SGB\s*(I{1,3}|IV|V|VI|VII|VIII|IX|X|XI|XII|1|2|3|4|5|6|7|8|9|10|11|12)))\b",
            m => $"<span class='highlight-paragraph'>{m.Value}</span>");

        // 3) Handlungsempfehlungen (fett/orange): Modalverben und handlungsleitende Verben.
        html = Regex.Replace(html,
            @"(?i)\b(muss|sollten|sollt|sollte|soll|müssten|müsste|müsst|muss|empfehlen|empfahl|empfiehlt|empfehle|dringend|unbedingt|sofort|zunächst|anschließend|danach|abschließend|bitte\s+\w+|wenden\s+Sie\s+sich\s+an|vereinbaren\s+Sie\s+einen|reichen\s+Sie\s+ein|beantragen\s+Sie|legen\s+Sie\s+vor)\b",
            m => $"<span class='highlight-empfehlung'>{m.Value}</span>");

        return html;
    }

    /// <summary>
    /// Baut eine vollständige Chat-Bubble aus einem KI-Text und dem System-CSS.
    /// </summary>
    public static string WrapAiBubble(string html, string suggestedAction)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class='ai-message'>");
        sb.AppendLine("  <div class='ai-bubble'>");
        sb.AppendLine(html);
        sb.AppendLine("  </div>");

        if (suggestedAction == "erstelle_dokument")
        {
            sb.AppendLine("  <button class='chat-action-button' data-action='erstelle_dokument'>📄 Vorlage erstellen</button>");
        }
        else if (suggestedAction == "berechne_unterhalt")
        {
            sb.AppendLine("  <button class='chat-action-button primary' data-action='berechne_unterhalt'>⚖ Unterhalt berechnen</button>");
        }

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

            .highlight-frist {{ color: #ff7b72; font-weight: 700; text-decoration: underline; }}
            .highlight-paragraph {{ color: #79c0ff; font-weight: 600; }}
            .highlight-empfehlung {{ color: #e8a838; font-weight: 700; }}
        </style>";
    }
}
