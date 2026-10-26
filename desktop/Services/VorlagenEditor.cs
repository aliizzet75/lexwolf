using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace LexWolf.Services;

/// <summary>
/// Liest und schreibt Vorlagen-Dateien für den eingebauten Vorlagen-Editor (Task #306) —
/// bisher waren Vorlagen nur über den Windows-Explorer editierbar. Wie beim bestehenden
/// Schriftsatz-Editor (SchriftsatzDialog.EntwurfBox / DokumentExport.SchreibeDocx) wird eine
/// .docx beim Speichern komplett neu aus dem einfachen Text erzeugt — Formatierung,
/// Kopf-/Fußzeilen oder Bilder der Originaldatei bleiben dabei nicht erhalten.
/// </summary>
public static class VorlagenEditor
{
    /// <summary>Nur diese Endungen lassen sich im eingebauten Editor öffnen — alle anderen
    /// (z. B. .pdf) werden weiterhin per Doppelklick im Standardprogramm geöffnet.</summary>
    public static bool IstBearbeitbar(string pfad) =>
        Path.GetExtension(pfad).ToLowerInvariant() is ".docx" or ".txt";

    public static string LeseText(string pfad) =>
        Path.GetExtension(pfad).ToLowerInvariant() == ".docx" ? LeseDocxText(pfad) : File.ReadAllText(pfad, Encoding.UTF8);

    public static void SchreibeText(string pfad, string text)
    {
        if (Path.GetExtension(pfad).ToLowerInvariant() == ".docx")
        {
            DokumentExport.SchreibeDocx(pfad, text);
            return;
        }
        File.WriteAllText(pfad, text.Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(true));
    }

    /// <summary>Text je Absatz (InnerText verbindet die Runs eines Absatzes bereits ohne
    /// zusätzliche Trenner); Absätze selbst werden mit Zeilenumbruch verbunden.</summary>
    private static string LeseDocxText(string pfad)
    {
        using var doc = WordprocessingDocument.Open(pfad, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;
        return string.Join("\n", body.Elements<Paragraph>().Select(p => p.InnerText));
    }
}
