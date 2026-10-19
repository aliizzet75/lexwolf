using System.Runtime.InteropServices;
using System.Text;

namespace LexWolf.Services;

/// <summary>
/// Fragt zur Laufzeit bei Windows nach, welches Programm für eine Dateiendung
/// (".docx", ".eml") oder ein Protokoll ("mailto") eingerichtet ist — so kann
/// LexWolf vorher entscheiden, wie es ein Schreiben bzw. eine E-Mail öffnet, und
/// das Programm beim Namen nennen (z.B. "Microsoft Word", "WordPad", "Outlook").
/// </summary>
public static class StandardProgramme
{
    private const uint AssocfInitIgnoreUnknown = 0x400; // kein "Öffnen mit"-Ersatz für Unbekanntes
    private const int AssocstrFriendlyAppName = 4;      // funktioniert auch für Store-Apps (neues Outlook)

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint AssocQueryString(uint flags, int str, string assoc, string? extra,
                                                StringBuilder? output, ref uint length);

    /// <summary>Anzeigename des zuständigen Programms, oder null, wenn keins eingerichtet ist.</summary>
    public static string? Fuer(string endungOderProtokoll)
    {
        uint laenge = 0;
        AssocQueryString(AssocfInitIgnoreUnknown, AssocstrFriendlyAppName, endungOderProtokoll, null, null, ref laenge);
        if (laenge == 0) return null;

        var name = new StringBuilder((int)laenge);
        var hr = AssocQueryString(AssocfInitIgnoreUnknown, AssocstrFriendlyAppName, endungOderProtokoll, null, name, ref laenge);
        return hr == 0 && name.Length > 0 ? name.ToString() : null;
    }
}
