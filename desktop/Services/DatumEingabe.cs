using System;
using System.Globalization;

namespace LexWolf.Services;

/// <summary>Tolerante Datumseingabe: "16.11.2026", "16.11.26", "16.11." (laufendes Jahr), "heute".</summary>
public static class DatumEingabe
{
    public static DateTime? Parse(string? text, DateTime? heute = null)
    {
        var t = (text ?? "").Trim().TrimEnd('.');
        var h = (heute ?? DateTime.Today).Date;
        if (t.Length == 0) return null;
        if (t.Equals("heute", StringComparison.OrdinalIgnoreCase)) return h;
        if (t.Equals("morgen", StringComparison.OrdinalIgnoreCase)) return h.AddDays(1);

        var teile = t.Split('.', StringSplitOptions.TrimEntries);
        if (teile.Length is < 2 or > 3) return null;
        if (!int.TryParse(teile[0], out var tag) || !int.TryParse(teile[1], out var monat)) return null;
        var jahr = h.Year;
        if (teile.Length == 3 && teile[2].Length > 0)
        {
            if (!int.TryParse(teile[2], out jahr)) return null;
            if (teile[2].Length == 2) jahr += 2000;
        }
        if (monat is < 1 or > 12 || tag < 1 || tag > DateTime.DaysInMonth(jahr, monat)) return null;
        return new DateTime(jahr, monat, tag);
    }

    public static string Format(DateTime d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string Lang(DateTime d) => $"{FristRechner.Wochentag(d)}, {Format(d)}";

    /// <summary>"heute", "morgen", "in 5 Tagen", "seit 2 Tagen überfällig".</summary>
    public static string Abstand(DateTime ziel, DateTime? heute = null)
    {
        var tage = (ziel.Date - (heute ?? DateTime.Today).Date).Days;
        return tage switch
        {
            0 => "heute",
            1 => "morgen",
            -1 => "seit gestern überfällig",
            < 0 => $"seit {-tage} Tagen überfällig",
            _ => $"in {tage} Tagen",
        };
    }
}
