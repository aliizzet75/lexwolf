using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LexWolf.Services;

/// <summary>
/// Vorlagen liegen unter [Dokumente]/Vorlagen/. Zusätzlich gibt es dort einen Unterordner je
/// bekannter Schriftsatz-Art (siehe SchriftsatzDialog.Arten), damit Anwält:innen ihre Muster
/// passend zum Schriftsatz-Typ ablegen können. Bestehende Dateien direkt im Vorlagen-Wurzelordner
/// werden nicht angerührt oder verschoben — sie bleiben als übergeordneter Fallback erhalten.
/// </summary>
public static class VorlagenOrdner
{
    public const string Ordnername = "Vorlagen";

    /// <summary>Die bekannten Schriftsatz-Arten, wie sie im Schriftsatz-Dialog zur Auswahl stehen
    /// und als SchriftsatzRequest.art ans Backend gehen (backend/api/akte.py).</summary>
    public static readonly string[] Arten =
    {
        "Klageerwiderung", "Klageschrift", "Antragsschrift (Familiensache)", "Antragserwiderung (Familiensache)",
        "Stellungnahme an das Gericht", "Berufungsbegründung", "Beschwerdebegründung",
        "Außergerichtliches Schreiben an die Gegenseite", "Gutachten / Erfolgsaussichten",
        "Freier Entwurf (siehe Anweisung)",
    };

    /// <summary>Legt [dokumentePfad]/Vorlagen/ sowie je bekannter Schriftsatz-Art einen Unterordner an.
    /// Migriert keine Dateien — bestehende Vorlagen im Wurzelordner bleiben unverändert. Mehrfacher
    /// Aufruf (z.B. bei jedem Start) ist unschädlich, da Directory.CreateDirectory idempotent ist.</summary>
    public static string SicherstellenUnterordner(string dokumentePfad, IEnumerable<string>? arten = null)
    {
        var vorlagenOrdner = Path.Combine(dokumentePfad, Ordnername);
        Directory.CreateDirectory(vorlagenOrdner);
        foreach (var art in arten ?? Arten)
        {
            var name = OrdnerName(art);
            if (name.Length == 0) continue;
            Directory.CreateDirectory(Path.Combine(vorlagenOrdner, name));
        }
        return vorlagenOrdner;
    }

    /// <summary>Macht aus einer Schriftsatz-Art einen gültigen Ordnernamen, z.B.
    /// "Gutachten / Erfolgsaussichten" -> "Gutachten - Erfolgsaussichten".</summary>
    public static string OrdnerName(string art)
    {
        var ersetzt = art.Replace('/', '-').Replace('\\', '-');
        var ungueltig = Path.GetInvalidFileNameChars();
        return new string(ersetzt.Where(c => !ungueltig.Contains(c)).ToArray()).Trim();
    }
}
