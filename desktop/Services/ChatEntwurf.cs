using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace LexWolf.Services;

/// <summary>Ein versandfertiger Text aus einer KI-Antwort (Schreiben oder E-Mail).
/// Art ist "brief" oder "email"; Ueberarbeitung = neue Fassung des vorigen Entwurfs.</summary>
public sealed record ChatEntwurf(string Art, string Titel, string An, string Betreff, string Text, bool Ueberarbeitung = false)
{
    public bool IstEmail => Art == "email";

    public EmailEntwurf AlsEmail() =>
        new(An, string.IsNullOrWhiteSpace(Betreff) ? Titel : Betreff, DokumentExport.OhneMarkdown(Text));
}

/// <summary>
/// Trennt den Entwurf-Block, den das Backend laut System-Prompt
/// (backend/api/chat_prompt.py) um versandfertige Texte setzt, vom übrigen
/// Chat-Text:
/// <code>
/// &lt;&lt;&lt;ENTWURF art="email" titel="…" an="…" betreff="…" ueberarbeitung="nein"&gt;&gt;&gt;
/// …Text…
/// &lt;&lt;&lt;ENDE&gt;&gt;&gt;
/// </code>
/// Dadurch landet nur der eigentliche Text in Word bzw. der E-Mail — Sätze wie
/// "Gerne, hier ist der Entwurf" bleiben im Chat.
/// </summary>
public static class ChatEntwurfParser
{
    // ENDE darf fehlen (Antwort abgeschnitten) — dann gilt der Rest als Text.
    private static readonly Regex Block = new(
        @"<<<\s*ENTWURF\b(?<attr>[^>]*)>>>\s*(?<text>.*?)\s*(?:<<<\s*ENDE\s*>>>|$)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex Attribut = new(@"(\w+)\s*=\s*""([^""]*)""");

    public static bool HatBlock(string antwort) => Block.IsMatch(antwort);

    /// <summary>Zerlegt die Antwort in Chat-Text und Entwurf. Ohne Block ist der
    /// Entwurf null — außer suggestedAction kommt aus der Stichwort-Erkennung des
    /// Backends (erstelle_dokument/erstelle_email): dann ist die ganze Antwort der
    /// Entwurf (Verhalten vor Einführung des Blocks).</summary>
    public static (string ChatText, ChatEntwurf? Entwurf) Zerlege(string antwort, string suggestedAction = "frage")
    {
        var ohneHinweis = DokumentExport.OhneTestHinweis(antwort);
        // Bleibt im Chat sichtbar, kommt aber nie in den Entwurf.
        var pruefung = DokumentExport.Zitatpruefung(antwort);
        var m = Block.Match(ohneHinweis);
        if (!m.Success)
        {
            return suggestedAction switch
            {
                "erstelle_email" => (pruefung, AusEmailText(ohneHinweis)),
                "erstelle_dokument" => (pruefung, new ChatEntwurf("brief", "Schreiben", "", "", ohneHinweis)),
                _ => (antwort, null),
            };
        }

        var attr = Attribut.Matches(m.Groups["attr"].Value)
            .GroupBy(a => a.Groups[1].Value.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Groups[2].Value.Trim());
        string A(string name) => attr.TryGetValue(name, out var v) ? v : "";

        var art = A("art").ToLowerInvariant() is "email" or "e-mail" or "mail" ? "email" : "brief";
        var text = m.Groups["text"].Value.Trim();
        var entwurf = art == "email" && A("an") == "" && A("betreff") == ""
            ? AusEmailText(text) // Modell hat An/Betreff doch als Kopfzeilen geschrieben
            : new ChatEntwurf(art, "", A("an"), A("betreff"), text);
        if (A("ueberarbeitung").ToLowerInvariant() is "ja" or "true" or "1")
            entwurf = entwurf with { Ueberarbeitung = true };
        if (A("titel") != "") entwurf = entwurf with { Titel = A("titel") };
        else if (entwurf.Titel == "")
            entwurf = entwurf with { Titel = entwurf.Betreff != "" ? entwurf.Betreff : art == "email" ? "E-Mail" : "Schreiben" };

        var rest = (ohneHinweis[..m.Index].TrimEnd() + "\n\n" + ohneHinweis[(m.Index + m.Length)..].TrimStart()).Trim();
        if (pruefung.Length > 0) rest = (rest + "\n\n" + pruefung).Trim();
        return (rest, entwurf);
    }

    /// <summary>Antwort ohne Block-Markierungen, z.B. für "Kopieren" der ganzen Antwort.</summary>
    public static string AlsKlartext(string antwort)
    {
        var (rest, entwurf) = Zerlege(antwort);
        if (entwurf is null) return rest;
        var kopf = entwurf.IstEmail
            ? string.Join("\n", new[] { entwurf.An == "" ? "" : $"An: {entwurf.An}", entwurf.Betreff == "" ? "" : $"Betreff: {entwurf.Betreff}" }.Where(z => z != ""))
            : "";
        return string.Join("\n\n", new[] { rest, kopf, entwurf.Text }.Where(t => !string.IsNullOrWhiteSpace(t)));
    }

    private static ChatEntwurf AusEmailText(string text)
    {
        var e = DokumentExport.ParseEmail(text);
        return new ChatEntwurf("email", string.IsNullOrWhiteSpace(e.Betreff) ? "E-Mail" : e.Betreff, e.An, e.Betreff, e.Text);
    }
}
