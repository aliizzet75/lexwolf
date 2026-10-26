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

    /// <summary>Erste Datei (alphabetisch nach Dateiname) direkt in [ordner], oder null wenn
    /// der Ordner nicht existiert oder keine Dateien enthält. Durchsucht bewusst nur
    /// [ordner] selbst (TopDirectoryOnly) — beim Root-Fallback (Stufe 3 der Lade-Fallback-Kette,
    /// Task #305) sollen keine Dateien aus den Typ-Unterordnern anderer Schriftsatz-Arten
    /// einfließen.</summary>
    public static string? ErsteDateiAlphabetisch(string ordner)
    {
        if (!Directory.Exists(ordner)) return null;
        return Directory.GetFiles(ordner, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(f => Path.GetFileName(f), System.StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>Lade-Fallback-Kette (Task #305) für die Vorlage, die beim Erstellen eines
    /// Schriftsatzes vom Typ [schriftsatzTyp] automatisch geladen wird:
    /// 1) [letzteVorlagePfad] (die für diesen Typ zuletzt bewusst gewählte Vorlage,
    ///    siehe LocalDb.GetLetzteVorlage), falls die Datei noch existiert;
    /// 2) sonst die erste Datei (alphabetisch) im Typ-Unterordner Vorlagen/[Typ]/;
    /// 3) sonst die erste Datei (alphabetisch) im übergeordneten Vorlagen-Wurzelordner;
    /// 4) sonst null — kein Vorlagen-Kontext, wie bisher bei leerem Ordner.</summary>
    public static string? FindeVorlagenPfad(string dokumentePfad, string schriftsatzTyp, string? letzteVorlagePfad)
    {
        if (letzteVorlagePfad is not null && File.Exists(letzteVorlagePfad))
            return letzteVorlagePfad;

        var vorlagenOrdner = Path.Combine(dokumentePfad, Ordnername);
        var typOrdner = Path.Combine(vorlagenOrdner, OrdnerName(schriftsatzTyp));
        return ErsteDateiAlphabetisch(typOrdner) ?? ErsteDateiAlphabetisch(vorlagenOrdner);
    }
}
