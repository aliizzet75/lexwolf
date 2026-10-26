using System;
using System.IO;

namespace LexWolf.Services
{
    /// <summary>Validierung für das Umbenennen von Dateien/Ordnern im Dateibaum —
    /// bewusst ohne WPF-Abhängigkeit, damit sie sich ohne Windows-Zielplattform testen lässt.</summary>
    public static class DateiVerwaltung
    {
        private static readonly char[] UngueltigeZeichen = Path.GetInvalidFileNameChars();

        /// <summary>Prüft einen vorgeschlagenen neuen Namen für <paramref name="aktuellerPfad"/>
        /// innerhalb von <paramref name="ordner"/>. Gibt null zurück, wenn der Name gültig ist
        /// (inkl. „unverändert“), sonst eine Fehlermeldung für die Anzeige im Dialog.</summary>
        public static string? PruefeNeuenNamen(string neuerName, string ordner, string aktuellerPfad)
        {
            var name = (neuerName ?? "").Trim();
            if (name.Length == 0) return "Der Name darf nicht leer sein.";
            if (name is "." or "..") return "Ungültiger Name.";
            if (name.IndexOfAny(UngueltigeZeichen) >= 0) return "Der Name enthält ungültige Zeichen.";

            var ziel = Path.Combine(ordner, name);
            if (NameUnveraendert(ziel, aktuellerPfad)) return null;

            // Bewusst über die Verzeichniseinträge statt File.Exists/Directory.Exists:
            // Windows ist case-insensitive, aber das soll nicht von der jeweiligen
            // Dateisystem-Semantik der Laufzeitumgebung abhängen.
            if (Directory.Exists(ordner))
            {
                foreach (var eintrag in Directory.EnumerateFileSystemEntries(ordner))
                {
                    if (string.Equals(Path.GetFileName(eintrag), name, StringComparison.OrdinalIgnoreCase))
                        return $"„{name}“ existiert in diesem Ordner bereits.";
                }
            }

            return null;
        }

        /// <summary>True, wenn der neue Zielpfad auf denselben Eintrag zeigt wie der aktuelle
        /// (Windows-Dateisystem ist case-insensitive — „Foo.docx“ -> „foo.docx“ ist kein Konflikt).</summary>
        public static bool NameUnveraendert(string zielPfad, string aktuellerPfad) =>
            string.Equals(Path.GetFullPath(zielPfad), Path.GetFullPath(aktuellerPfad), StringComparison.OrdinalIgnoreCase);
    }
}
