using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LexWolf.Services
{
    // Comic-Aliaspool pro Mandant
    public static class ComicAliasPool
    {
        // Vordefinierte Alias-Pools pro Comic-Thema
        private static readonly Dictionary<string, Dictionary<string, string>> _comicAliases = new()
        {
            {
                "donaldduck",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Donald Duck", "DONALD" },
                    { "Daisy", "DAISY" },
                    { "Tick", "TICK" },
                    { "Trick", "TRICK" },
                    { "Track", "TRACK" },
                    { "Dagobert", "DAGOBERT" },
                    { "Daniel Düsentrieb", "DANIEL" }
                }
            },
            {
                "lucky Luke",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Luke", "LUKE" },
                    { "Jolly Jumper", "JOLLY" },
                    { "Joe", "JOE" },
                    { "William", "WILLIAM" },
                    { "Jack", "JACK" },
                    { "Averell Dalton", "AVERELL" }
                }
            },
            {
                "asterix",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Asterix", "ASTERIX" },
                    { "Obelix", "OBELEX" },
                    { "Miraculix", "MIRACULIX" },
                    { "Majestix", "MAJESTIX" }
                }
            }
        };

        // Gibt den Alias-Pool für einen Mandantennamen zurück
        public static Dictionary<string, string> GetAliasesForMandant(string mandantName)
        {
            if (string.IsNullOrWhiteSpace(mandantName))
                return new Dictionary<string, string>();

            // Prüfen, ob der Mandantenname ein Comic-Thema enthält
            foreach (var topic in _comicAliases.Keys)
            {
                if (mandantName.Contains(topic, StringComparison.OrdinalIgnoreCase))
                {
                    return _comicAliases[topic];
                }
            }

            // Standard-Aliaspool (wenn kein Comic-Thema erkannt)
            return new Dictionary<string, string>();
        }

        // Gibt alle verfügbaren Comic-Themen zurück
        public static List<string> GetAvailableThemes()
        {
            return new List<string>(_comicAliases.Keys);
        }
    }
}
