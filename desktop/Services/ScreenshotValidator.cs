using System;
using System.IO;
using System.Linq;

namespace LexWolf.Services
{
    /// <summary>
    /// Plattformunabhängige Validierungslogik für Screenshot-Anhänge im
    /// Feature-Wunsch-/Bug-Meldungs-Dialog. Wird sowohl vom WPF-Dialog als auch
    /// von den Unit-Tests verwendet.
    /// </summary>
    public static class ScreenshotValidator
    {
        public const long DefaultMaxSize = 10 * 1024 * 1024; // 10 MB

        public static readonly string[] AllowedImageTypes = { "image/png", "image/jpeg", "image/gif", "image/webp" };

        /// <summary>
        /// Liefert den MIME-Type für eine Dateiendung oder null, wenn die Endung
        /// nicht zu einem erlaubten Bildformat gehört.
        /// </summary>
        public static string? GetMimeTypeForFileName(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => null,
            };
        }

        /// <summary>
        /// Prüft Dateityp und -größe eines potenziellen Screenshot-Anhangs.
        /// Gibt bei Erfolg den MIME-Type zurück; bei Fehler eine aussagekräftige
        /// Fehlermeldung.
        /// </summary>
        public static (bool Ok, string? MimeType, string? ErrorMessage) Validate(byte[]? bytes, string fileName, long maxSize)
        {
            if (bytes is null || bytes.Length == 0)
                return (false, null, "Das Bild ist leer.");

            if (bytes.Length > maxSize)
                return (false, null, $"Das Bild darf maximal {maxSize / 1024 / 1024} MB groß sein.");

            var mime = GetMimeTypeForFileName(fileName);
            if (mime is null || !AllowedImageTypes.Contains(mime))
                return (false, null, "Bitte ein Bild im Format PNG, JPG, GIF oder WebP verwenden.");

            return (true, mime, null);
        }

        /// <summary>
        /// Convenience-Überladung mit dem Default-Limit von 10 MB.
        /// </summary>
        public static (bool Ok, string? MimeType, string? ErrorMessage) Validate(byte[]? bytes, string fileName)
            => Validate(bytes, fileName, DefaultMaxSize);
    }
}
