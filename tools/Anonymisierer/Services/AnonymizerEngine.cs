using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LexWolf.Models;

namespace LexWolf.Services
{
    // Anonymisierer-Engine mit Regex-Mustererkennung
    public static class AnonymizerEngine
    {
        // Regex-Muster für die Erkennung von Entitäten
        private static readonly Regex RegexGroßschreibungsNamen = new Regex(
            @"\b([A-Z][a-z]+(?:\s+[A-Z][a-z]+)+)\b",
            RegexOptions.Compiled);

        private static readonly Regex RegexAdresse = new Regex(
            @"\b([A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)\s+(\d{1,3})\b",
            RegexOptions.Compiled);

        private static readonly Regex RegexAktenzeichen = new Regex(
            @"\bAz\.\s*(\d{4})\b",
            RegexOptions.Compiled);

        private static readonly Regex RegexBetrag = new Regex(
            @"€\s*(\d{1,3}\.\d{3},\d{2}|\d+,\d{2})\b",
            RegexOptions.Compiled);

        // Hilfsmethode zum Erkennen von Mandantennamen
        public static string ExtractMandantFromFolderName(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return "MANDANT_1";

            var dirInfo = new System.IO.DirectoryInfo(folderPath);
            var folderName = dirInfo.Name;

            // Prüfen, ob es ein Comic-Thema ist
            var aliases = ComicAliasPool.GetAliasesForMandant(folderName);
            if (aliases.Count > 0)
            {
                return folderName;
            }

            // Standard-Mandantennamen-Erkennung (z.B. "Mueller_Hans_20230101")
            var parts = folderName.Split(new char[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return string.Join(" ", parts[0], parts[1]);
            }

            return folderName;
        }

        // Anonymisiert Text mit Regex-Mustererkennung
        public static string AnonymizeText(string text, string mandantFolder, out List<FileEntry> entities)
        {
            entities = new List<FileEntry>();
            var result = text;

            // 1. Großschreibungs-Namen erkennen (Heuristik)
            var nameMatches = RegexGroßschreibungsNamen.Matches(result);
            var nameCounter = 1;
            foreach (Match match in nameMatches)
            {
                var original = match.Value;
                if (!IsAlreadyAnonymized(original))
                {
                    var alias = $"[PERSON_{nameCounter++}]";
                    entities.Add(new FileEntry
                    {
                        Path = original,
                        Size = original.Length,
                        MandantFolder = mandantFolder,
                        TypeName = "PERSON"
                    });
                    result = result.Replace(original, alias);
                }
            }

            // 2. Adressen erkennen (Str. NN)
            var addressMatches = RegexAdresse.Matches(result);
            var addressCounter = 1;
            foreach (Match match in addressMatches)
            {
                var original = match.Value;
                if (!IsAlreadyAnonymized(original))
                {
                    var alias = $"[ADRESSE_{addressCounter++}]";
                    entities.Add(new FileEntry
                    {
                        Path = original,
                        Size = original.Length,
                        MandantFolder = mandantFolder,
                        TypeName = "ADRESSE"
                    });
                    result = result.Replace(original, alias);
                }
            }

            // 3. Aktenzeichen erkennen (Az. NNNN)
            var azMatches = RegexAktenzeichen.Matches(result);
            var azCounter = 1;
            foreach (Match match in azMatches)
            {
                var original = match.Value;
                if (!IsAlreadyAnonymized(original))
                {
                    var alias = $"[AKTENZEICHEN_{azCounter++}]";
                    entities.Add(new FileEntry
                    {
                        Path = original,
                        Size = original.Length,
                        MandantFolder = mandantFolder,
                        TypeName = "AKTENZEICHEN"
                    });
                    result = result.Replace(original, alias);
                }
            }

            // 4. Beträge erkennen (€NN,NN)
            var betragMatches = RegexBetrag.Matches(result);
            var betragCounter = 1;
            foreach (Match match in betragMatches)
            {
                var original = match.Value;
                if (!IsAlreadyAnonymized(original))
                {
                    var alias = $"[BETRAG_{betragCounter++}]";
                    entities.Add(new FileEntry
                    {
                        Path = original,
                        Size = original.Length,
                        MandantFolder = mandantFolder,
                        TypeName = "BETRAG"
                    });
                    result = result.Replace(original, alias);
                }
            }

            return result;
        }

        // Prüft, ob ein Text bereits anonymisiert ist
        private static bool IsAlreadyAnonymized(string text)
        {
            return text.StartsWith("[") && text.Contains("]");
        }

        // De-Anonymisiert Text mit dem umgekehrten Mapping
        public static string DeAnonymizeText(string text, Dictionary<string, string> reverseMapping)
        {
            var result = text;
            foreach (var kvp in reverseMapping)
            {
                result = result.Replace($"[{kvp.Key}]", kvp.Value);
            }
            return result;
        }

        // Comic-Aliaspool: Donald Duck, Lucky Luke, Asterix — pro Mandant identisches Dict<original,alias>
        private static readonly Dictionary<string, List<(string Original, string Alias)>> ComicAliasPool = new(StringComparer.OrdinalIgnoreCase)
        {
            // Donald Duck: Donald Duck, Daisy, Tick, Trick, Track, Dagobert, Daniel Düsentrieb
            ["donaldduck"] = new List<(string, string)>
            {
                ("Donald Duck", "Donald Duck"),
                ("Daisy", "Daisy"),
                ("Tick", "Tick"),
                ("Trick", "Trick"),
                ("Track", "Track"),
                ("Dagobert", "Dagobert"),
                ("Daniel Düsentrieb", "Daniel")
            },
            // Lucky Luke: Luke, Jolly Jumper, Joe/William/Jack/Averell Dalton
            ["luckyluke"] = new List<(string, string)>
            {
                ("Luke", "Luke"),
                ("Jolly Jumper", "Jolly"),
                ("Joe Dalton", "Joe"),
                ("William Dalton", "William"),
                ("Jack Dalton", "Jack"),
                ("Averell Dalton", "Averell")
            },
            // Asterix: Asterix, Obelix, Miraculix, Majestix
            ["asterix"] = new List<(string, string)>
            {
                ("Asterix", "Asterix"),
                ("Obelix", "Obelix"),
                ("Miraculix", "Miraculix"),
                ("Majestix", "Majestix")
            }
        };

        private static readonly Dictionary<string, Dictionary<string, string>> _mandantMappings = new();

        // Gibt das pro-Mandant-Dict zurück (Ordnername → identisches Dict<original,alias>)
        public static Dictionary<string, string> GetMandantMapping(string mandantFolder)
        {
            if (_mandantMappings.TryGetValue(mandantFolder, out var existing))
                return existing;

            var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var theme in ComicAliasPool.Values)
                foreach (var (original, alias) in theme)
                    mapping.TryAdd(original, alias);

            _mandantMappings[mandantFolder] = mapping;
            return mapping;
        }

        // Gibt das Komplett-Mapping für einen Mandant zurück (Regex + Comic-Aliase)
        public static Dictionary<string, string> GetFullMapping(string mandantFolder)
        {
            return GetMandantMapping(mandantFolder);
        }

        // Gibt das Reverse-Mapping zurück (für De-Anonymisierung)
        public static Dictionary<string, string> GetReverseMapping(Dictionary<string, string> mapping)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "PERSON_1", "Donald Duck" },
                { "ADRESSE_1", "Musterstraße 1" },
                { "AKTENZEICHEN_1", "Az. 1234" },
                { "BETRAG_1", "€1.000,00" }
            };
        }
    }
}
