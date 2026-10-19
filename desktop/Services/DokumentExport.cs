using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace LexWolf.Services;

public record EmailEntwurf(string An, string Betreff, string Text);

/// <summary>Wie ein E-Mail-Entwurf geöffnet wird — abhängig davon, welche Programme
/// zur Laufzeit eingerichtet sind (siehe StandardProgramme).</summary>
public enum EmailWeg
{
    /// <summary>mailto: mit Empfänger, Betreff und Text.</summary>
    Mailto,
    /// <summary>Text zu lang für mailto: — Entwurf nur mit Empfänger/Betreff, Text in die Zwischenablage.</summary>
    MailtoTextInZwischenablage,
    /// <summary>Kein mailto-Programm, aber eins für .eml-Dateien.</summary>
    Eml,
    /// <summary>Gar kein E-Mail-Programm eingerichtet.</summary>
    NurZwischenablage,
}

/// <summary>
/// Übernimmt KI-Antworten in Word-Dokumente bzw. E-Mail-Entwürfe im
/// Standard-Mailprogramm. Bewusst ohne WPF-Abhängigkeit, damit die Logik im
/// plattformunabhängigen Testprojekt (LexWolf.Tests) geprüft werden kann.
/// </summary>
public static class DokumentExport
{
    // Längere mailto:-URLs schneiden ShellExecute bzw. manche Mailprogramme ab —
    // darüber wird stattdessen ein .eml-Entwurf (X-Unsent) geöffnet.
    public const int MailtoMaxLaenge = 1800;

    // Hinweis, den Server (llm_client.TEST_MODE_DISCLAIMER) und lokaler KI-Proxy
    // an jede Antwort hängen — gehört nicht ins Schreiben bzw. in die E-Mail.
    private const string TestHinweisMarker = "\n---\n⚠️ Testmodus";
    // Prüfvermerk des Backends (chat_prompt.format_zitatpruefung) — für den Anwalt, nicht für den Empfänger.
    private const string ZitatpruefungMarker = "\n---\n🔎 Zitatprüfung";

    private static readonly Regex KopfZeile = new(
        @"^\s*\**\s*(An|To|Empfänger|Betreff|Subject)\s*:\s*\**\s*(.*?)\s*\**\s*$",
        RegexOptions.IgnoreCase);
    private static readonly Regex Adresse = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}");
    private static readonly Regex Fett = new(@"\*\*(.+?)\*\*");
    private static readonly Regex Ueberschrift = new(@"^(#{1,6})\s+(.*)$");
    private static readonly Regex Aufzaehlung = new(@"^\s*[-*•]\s+(.*)$");
    private static readonly Regex Steuerzeichen = new(@"[\x00-\x08\x0B\x0C\x0E-\x1F]");

    public static string OhneTestHinweis(string text)
    {
        var normalisiert = text.Replace("\r\n", "\n");
        var idx = normalisiert.LastIndexOf(TestHinweisMarker, StringComparison.Ordinal);
        var ohne = idx >= 0 ? normalisiert[..idx] : normalisiert;
        var pruefung = ohne.LastIndexOf(ZitatpruefungMarker, StringComparison.Ordinal);
        return (pruefung >= 0 ? ohne[..pruefung] : ohne).Trim('\n', ' ');
    }

    /// <summary>Der Zitatprüfungs-Vermerk am Ende einer Chat-Antwort (ohne Trennlinie), sonst "".</summary>
    public static string Zitatpruefung(string text)
    {
        var normalisiert = text.Replace("\r\n", "\n");
        var idx = normalisiert.LastIndexOf(ZitatpruefungMarker, StringComparison.Ordinal);
        if (idx < 0) return "";
        var vermerk = normalisiert[(idx + "\n---\n".Length)..];
        var test = vermerk.IndexOf(TestHinweisMarker, StringComparison.Ordinal);
        return (test >= 0 ? vermerk[..test] : vermerk).Trim('\n', ' ');
    }

    /// <summary>Entfernt Markdown-Auszeichnungen (fett, Überschriften) für reinen Text.</summary>
    public static string OhneMarkdown(string text)
    {
        var zeilen = text.Split('\n').Select(z =>
        {
            var u = Ueberschrift.Match(z);
            return Fett.Replace(u.Success ? u.Groups[2].Value : z, "$1");
        });
        return string.Join("\n", zeilen);
    }

    /// <summary>Liest führende "An:"/"Betreff:"-Zeilen (Format laut System-Prompt in
    /// backend/api/chat_prompt.py); der Rest ist der E-Mail-Text.</summary>
    public static EmailEntwurf ParseEmail(string antwort)
    {
        var zeilen = OhneTestHinweis(antwort).Split('\n');
        string an = "", betreff = "";
        var i = 0;
        while (i < zeilen.Length && string.IsNullOrWhiteSpace(zeilen[i])) i++;
        for (; i < zeilen.Length; i++)
        {
            var m = KopfZeile.Match(zeilen[i]);
            if (!m.Success) break;
            var wert = m.Groups[2].Value.Trim();
            if (m.Groups[1].Value.ToLowerInvariant() is "betreff" or "subject") betreff = wert;
            else an = wert;
        }
        var text = string.Join("\n", zeilen.Skip(i)).Trim('\n', ' ');
        return new EmailEntwurf(an, betreff, OhneMarkdown(text));
    }

    /// <summary>Nur echte Adressen — "An: Gegnerische Kanzlei" ergibt eine leere Liste,
    /// der Empfänger wird dann im Mailprogramm ergänzt.</summary>
    public static IReadOnlyList<string> EmailAdressen(string an) =>
        Adresse.Matches(an).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static string BaueMailtoUri(EmailEntwurf entwurf, bool mitText = true)
    {
        var uri = $"mailto:{string.Join(",", EmailAdressen(entwurf.An))}" +
                  $"?subject={Uri.EscapeDataString(entwurf.Betreff)}";
        if (!mitText) return uri;
        var body = entwurf.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        return uri + $"&body={Uri.EscapeDataString(body)}";
    }

    /// <summary>Wählt anhand der eingerichteten Programme, wie der Entwurf geöffnet wird.
    /// mailto: hat Vorrang vor .eml, weil z.B. das neue Outlook .eml-Dateien nur zum
    /// Lesen, nicht als bearbeitbaren Entwurf öffnet.</summary>
    public static EmailWeg WaehleEmailWeg(EmailEntwurf entwurf, bool mailtoProgramm, bool emlProgramm)
    {
        if (mailtoProgramm)
            return BaueMailtoUri(entwurf).Length <= MailtoMaxLaenge ? EmailWeg.Mailto : EmailWeg.MailtoTextInZwischenablage;
        return emlProgramm ? EmailWeg.Eml : EmailWeg.NurZwischenablage;
    }

    /// <summary>Betreff + Text für die Zwischenablage, wenn kein Mailprogramm da ist.</summary>
    public static string AlsZwischenablageText(EmailEntwurf entwurf) =>
        string.IsNullOrWhiteSpace(entwurf.Betreff) ? entwurf.Text : $"Betreff: {entwurf.Betreff}\r\n\r\n{entwurf.Text.Replace("\r\n", "\n").Replace("\n", "\r\n")}";

    /// <summary>E-Mail-Entwurf als .eml; "X-Unsent: 1" lässt Outlook (und andere
    /// Programme, die das Feld kennen) ihn als bearbeitbaren Entwurf öffnen.</summary>
    public static string BaueEml(EmailEntwurf entwurf)
    {
        var body = Convert.ToBase64String(Encoding.UTF8.GetBytes(entwurf.Text.Replace("\r\n", "\n").Replace("\n", "\r\n")));
        var sb = new StringBuilder();
        sb.Append("X-Unsent: 1\r\n");
        sb.Append($"To: {string.Join(", ", EmailAdressen(entwurf.An))}\r\n");
        sb.Append($"Subject: =?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(entwurf.Betreff))}?=\r\n");
        sb.Append("MIME-Version: 1.0\r\n");
        sb.Append("Content-Type: text/plain; charset=utf-8\r\n");
        sb.Append("Content-Transfer-Encoding: base64\r\n\r\n");
        for (var i = 0; i < body.Length; i += 76)
            sb.Append(body, i, Math.Min(76, body.Length - i)).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Legt den Entwurf als .eml in ordner ab und liefert den Pfad.</summary>
    public static string SchreibeEml(EmailEntwurf entwurf, string ordner)
    {
        Directory.CreateDirectory(ordner);
        var pfad = Path.Combine(ordner, $"{EmlEntwurfPraefix}{DateTime.Now:yyyy-MM-dd_HHmmss}.eml");
        SchreibeEmlNach(entwurf, pfad);
        return pfad;
    }

    /// <summary>Schreibt (bzw. aktualisiert) den .eml-Entwurf an einem festen Pfad.</summary>
    public static void SchreibeEmlNach(EmailEntwurf entwurf, string pfad) =>
        File.WriteAllText(pfad, BaueEml(entwurf), new UTF8Encoding(false));

    public const string EmlEntwurfPraefix = "E-Mail-Entwurf_";

    /// <summary>Dateiname eines Entwurfs, wenn er in die Akte übernommen wird
    /// ("E-Mail-Entwurf_…" wird zu "E-Mail_…"; Schreiben behalten ihren Namen).</summary>
    public static string NameNachUebernahme(string entwurfDateiname) =>
        entwurfDateiname.StartsWith(EmlEntwurfPraefix, StringComparison.OrdinalIgnoreCase)
            ? "E-Mail_" + entwurfDateiname[EmlEntwurfPraefix.Length..]
            : entwurfDateiname;

    /// <summary>Pfad in ordner, der noch nicht existiert: "name.docx", sonst
    /// "name (2).docx", "name (3).docx", …</summary>
    public static string FreierPfad(string ordner, string dateiname)
    {
        var pfad = Path.Combine(ordner, dateiname);
        var basis = Path.GetFileNameWithoutExtension(dateiname);
        var endung = Path.GetExtension(dateiname);
        for (var i = 2; File.Exists(pfad); i++)
            pfad = Path.Combine(ordner, $"{basis} ({i}){endung}");
        return pfad;
    }

    /// <summary>Ausweichformat, wenn kein Programm für .docx eingerichtet ist.</summary>
    public static void SchreibeText(string pfad, string antwort) =>
        File.WriteAllText(pfad, OhneMarkdown(OhneTestHinweis(antwort)).Replace("\n", "\r\n"), new UTF8Encoding(true));

    // Explizit statt Path.GetInvalidFileNameChars(): das liefert außerhalb von
    // Windows nur '/' und '\0' — die Datei landet aber immer auf Windows.
    private const string UngueltigImDateinamen = "\\/:*?\"<>| ";

    public static string DateinameFuerSchreiben(string? mandant, DateTime zeitpunkt, string endung = ".docx")
    {
        var teil = string.IsNullOrWhiteSpace(mandant)
            ? ""
            : "_" + string.Concat(mandant.Trim().Select(c => UngueltigImDateinamen.Contains(c) || char.IsControl(c) ? '_' : c));
        return $"Schreiben{teil}_{zeitpunkt:yyyy-MM-dd_HHmmss}{endung}";
    }

    /// <summary>Schreibt die Antwort als .docx: Überschriften (#) fett und größer,
    /// **fett** als Fettdruck, Aufzählungen mit "•", jede Zeile ein Absatz.</summary>
    public static void SchreibeDocx(string pfad, string antwort)
    {
        using var doc = WordprocessingDocument.Create(pfad, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var body = new Body();
        main.Document = new Document(body);

        foreach (var roh in OhneTestHinweis(antwort).Split('\n'))
        {
            var zeile = Steuerzeichen.Replace(roh.TrimEnd(), "");
            if (zeile.Trim() == "---") zeile = "";

            var absatz = new Paragraph();
            var u = Ueberschrift.Match(zeile);
            var a = Aufzaehlung.Match(zeile);
            if (u.Success)
                absatz.Append(Lauf(Fett.Replace(u.Groups[2].Value, "$1"), fett: true, groesseHalbpunkte: u.Groups[1].Length == 1 ? 28 : 24));
            else
                foreach (var lauf in Laeufe(a.Success ? "• " + a.Groups[1].Value : zeile))
                    absatz.Append(lauf);
            body.Append(absatz);
        }
        main.Document.Save();
    }

    private static IEnumerable<Run> Laeufe(string zeile)
    {
        var pos = 0;
        foreach (Match m in Fett.Matches(zeile))
        {
            if (m.Index > pos) yield return Lauf(zeile[pos..m.Index], fett: false);
            yield return Lauf(m.Groups[1].Value, fett: true);
            pos = m.Index + m.Length;
        }
        if (pos < zeile.Length) yield return Lauf(zeile[pos..], fett: false);
    }

    private static Run Lauf(string text, bool fett, int groesseHalbpunkte = 22)
    {
        // Reihenfolge laut OpenXML-Schema (rFonts, b, sz) — sonst meldet Word ggf.
        // "unlesbaren Inhalt".
        var eigenschaften = new RunProperties();
        eigenschaften.Append(new RunFonts { Ascii = "Arial", HighAnsi = "Arial", ComplexScript = "Arial" });
        if (fett) eigenschaften.Append(new Bold());
        eigenschaften.Append(new FontSize { Val = groesseHalbpunkte.ToString() });
        return new Run(eigenschaften, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    /// <summary>Öffnet eine Datei oder mailto:-URI mit dem dafür eingerichteten Programm;
    /// gibt es für eine Datei doch keins, erscheint der Windows-Dialog "Öffnen mit".</summary>
    public static void OeffneMitStandardprogramm(string pfad)
    {
        try
        {
            Process.Start(new ProcessStartInfo(pfad) { UseShellExecute = true });
        }
        catch (Win32Exception) when (File.Exists(pfad))
        {
            Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {pfad}"));
        }
    }
}
