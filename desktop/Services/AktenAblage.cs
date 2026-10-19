using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MimeKit;

namespace LexWolf.Services;

/// <summary>
/// Legt Dateien und E-Mails in den Ordner eines Mandanten. Dateien werden kopiert (das
/// Original bleibt, wo es ist). E-Mails (.eml, aus Outlook .msg) landen wie beim Postfach
/// unter „E-Mails“ mit Datum im Namen, ihre Anhänge als eigene Dateien daneben — so wird
/// z. B. ein angehängter Beschluss selbst ausgewertet.
/// </summary>
public static class AktenAblage
{
    public const string EmailOrdner = "E-Mails";

    public static bool IstEmail(string pfad) =>
        Path.GetExtension(pfad).ToLowerInvariant() is ".eml" or ".msg";

    /// <summary>Kann LexWolf den Inhalt lesen und auswerten? Sonst wird die Datei nur abgelegt.</summary>
    public static bool IstAuswertbar(string pfad) =>
        Path.GetExtension(pfad).ToLowerInvariant() is ".pdf" or ".docx" or ".txt" or ".eml" or ".msg";

    /// <summary>Liegt die Datei schon im Ordner dieses Mandanten? Dann nichts kopieren.</summary>
    public static bool LiegtInAkte(string pfad, string mandantOrdner)
    {
        var datei = Path.GetFullPath(pfad);
        var ordner = Path.GetFullPath(mandantOrdner).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return datei.StartsWith(ordner, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Legt eine Datei bzw. E-Mail in die Akte und gibt alle neu entstandenen Dateien zurück.</summary>
    /// <param name="msgLaden">Wandelt eine Outlook-.msg in eine MIME-Nachricht (im Hauptprogramm MsgKonverter).</param>
    public static List<string> Ablegen(string quelle, string mandantOrdner, Func<string, MimeMessage>? msgLaden = null)
    {
        if (LiegtInAkte(quelle, mandantOrdner)) return new List<string> { quelle };

        var endung = Path.GetExtension(quelle).ToLowerInvariant();
        if (endung == ".eml" || (endung == ".msg" && msgLaden is not null))
        {
            var mail = endung == ".eml" ? MimeMessage.Load(quelle) : msgLaden!(quelle);
            var (eml, anhaenge) = MailText.SpeichernMitAnhaengen(mail, Path.Combine(mandantOrdner, EmailOrdner));
            return anhaenge.Append(eml).ToList();
        }

        Directory.CreateDirectory(mandantOrdner);
        var ziel = DokumentExport.FreierPfad(mandantOrdner, Path.GetFileName(quelle));
        File.Copy(quelle, ziel);
        // Datum der Kopie = Eingang in der Akte; daran orientieren sich Fristvorschläge,
        // wenn das Dokument selbst kein Zustelldatum nennt.
        File.SetLastWriteTime(ziel, DateTime.Now);
        return new List<string> { ziel };
    }
}
