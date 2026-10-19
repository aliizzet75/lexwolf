using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MimeKit;

namespace LexWolf.Services;

/// <summary>
/// E-Mails (.eml) als lesbarer Text: Kopfzeilen, dekodierter Text (auch aus HTML,
/// Base64, quoted-printable) und die Namen der Anhänge. Vorher wurde eine .eml roh
/// eingelesen — die KI bekam dann MIME-Zeichensalat statt des Inhalts.
/// </summary>
public static class MailText
{
    public static string LeseEml(string pfad)
    {
        try
        {
            return AlsText(MimeMessage.Load(pfad));
        }
        catch
        {
            try { return File.ReadAllText(pfad); } catch { return ""; }
        }
    }

    public static string AlsText(MimeMessage m)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Von: {m.From}");
        if (m.To.Count > 0) sb.AppendLine($"An: {m.To}");
        if (m.Cc.Count > 0) sb.AppendLine($"Cc: {m.Cc}");
        sb.AppendLine($"Datum: {m.Date.LocalDateTime:dd.MM.yyyy HH:mm}");
        sb.AppendLine($"Betreff: {m.Subject}");
        sb.AppendLine();
        sb.AppendLine(Koerper(m));
        var anhaenge = Anhaenge(m).Select(a => a.FileName).Where(n => !string.IsNullOrEmpty(n)).ToList();
        if (anhaenge.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Anhänge: {string.Join(", ", anhaenge)}");
        }
        return sb.ToString().Trim();
    }

    public static string Koerper(MimeMessage m)
    {
        var text = m.TextBody;
        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(m.HtmlBody))
            text = HtmlZuText(m.HtmlBody);
        return (text ?? "").Trim();
    }

    public static string HtmlZuText(string html)
    {
        var s = Regex.Replace(html, @"<(script|style|head)[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<(br|/p|/div|/li|/tr|/h\d)[^>]*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<li[^>]*>", "• ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", "");
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"[ \t ]+", " ");
        s = Regex.Replace(s, @"\n\s*\n\s*\n+", "\n\n");
        return s.Trim();
    }

    /// <summary>Echte Anhänge (keine eingebetteten Logos/Signaturbilder).</summary>
    public static IEnumerable<MimePart> Anhaenge(MimeMessage m) =>
        m.Attachments.OfType<MimePart>().Where(p =>
            !(p.ContentType.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase)
              && (p.ContentDisposition?.Disposition == ContentDisposition.Inline || !string.IsNullOrEmpty(p.ContentId))));

    /// <summary>"2026-10-14_0931_Beschluss vom 30.09.2026" — sortiert chronologisch im Ordner.</summary>
    public static string Dateibasis(MimeMessage m)
    {
        var betreff = string.IsNullOrWhiteSpace(m.Subject) ? "ohne Betreff" : m.Subject.Trim();
        betreff = Regex.Replace(betreff, @"^((AW|WG|RE|FW|Fwd)\s*:\s*)+", "", RegexOptions.IgnoreCase);
        betreff = Regex.Replace(betreff, @"[\\/:*?""<>|\r\n\t]", "_");
        if (betreff.Length > 60) betreff = betreff[..60].TrimEnd();
        return $"{m.Date.LocalDateTime:yyyy-MM-dd_HHmm}_{betreff}";
    }

    /// <summary>Speichert Mail + Anhänge in den Ordner; gibt den Pfad der .eml zurück.</summary>
    public static string Speichern(MimeMessage m, string ordner) => SpeichernMitAnhaengen(m, ordner).Eml;

    /// <summary>Wie <see cref="Speichern"/>, liefert zusätzlich die Pfade der abgelegten Anhänge.</summary>
    public static (string Eml, List<string> Anhaenge) SpeichernMitAnhaengen(MimeMessage m, string ordner)
    {
        Directory.CreateDirectory(ordner);
        var basis = Dateibasis(m);
        var emlPfad = DokumentExport.FreierPfad(ordner, basis + ".eml");
        var anhaenge = new List<string>();
        // Anhänge zuerst: wenn der Scanner die .eml sieht, liegen ihre PDFs schon daneben.
        foreach (var a in Anhaenge(m))
        {
            var name = string.IsNullOrWhiteSpace(a.FileName) ? "Anhang" : Regex.Replace(a.FileName, @"[\\/:*?""<>|]", "_");
            var pfad = DokumentExport.FreierPfad(ordner, $"{basis} - {name}");
            using (var fs = File.Create(pfad))
                a.Content.DecodeTo(fs);
            anhaenge.Add(pfad);
        }
        m.WriteTo(emlPfad);
        return (emlPfad, anhaenge);
    }
}
