using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LexWolf.Services
{
    /// <summary>
    /// Extrahiert aus lokalen Anwalt-Korrekturen ausschließlich statistische,
    /// nicht personenbezogene Stil-Metriken, bevor sie an den Server gesendet werden.
    ///
    /// Datenschutzprinzip:
    /// - Keine Original- oder korrigierte Rohtexte verlassen den Rechner.
    /// - Keine Eigennamen (Mandanten, Richter, Gegner, Orte, …).
    /// - Keine §-Referenzen oder Aktenzeichen.
    /// - Keine Geldbeträge, Daten oder andere Mandantendetails.
    /// - Nur aggregierte Verteilungen und Häufigkeiten.
    ///
    /// Optionaler differentieller Datenschutz: kleines Rauschen (Laplace)
    /// wird auf die Häufigkeiten addiert, falls epsilon > 0 übergeben wird.
    /// </summary>
    public static class StyleMetricsExtractor
    {
        private static readonly Regex WordPattern = new Regex(
            @"[A-Za-zÄÖÜäöüß0-9_\\-]+", RegexOptions.Compiled);

        private static readonly Regex SentenceDelimiterPattern = new Regex(
            @"[.!?]+", RegexOptions.Compiled);

        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "der", "die", "das", "den", "dem", "des", "ein", "eine", "einer", "eines",
            "einem", "einen", "und", "oder", "aber", "sondern", "mit", "zu", "zur",
            "zum", "bei", "von", "vom", "für", "in", "im", "an", "am", "auf", "aus",
            "als", "wie", "dass", "es", "sie", "er", "man", "wird", "werden", "wurde",
            "worden", "sein", "sind", "ist", "war", "waren", "haben", "hat", "hatte",
            "kann", "könnte", "soll", "sollte", "muss", "darf", "dürfte", "nicht",
            "kein", "keine", "ja", "nein",
        };

        public class MetricsPayload
        {
            public Dictionary<string, int> satzlaengen_verteilung { get; set; }
                = new Dictionary<string, int>();

            public Dictionary<string, int> wortklassen_haeufigkeiten { get; set; }
                = new Dictionary<string, int>();

            public Dictionary<string, int> korrektur_kategorien { get; set; }
                = new Dictionary<string, int>();
        }

        /// <summary>
        /// Erzeugt aus einer Liste korrigierter Text-Fragmente und deren
        /// Korrektur-Kategorien einen DSGVO-konformen Metrik-Payload.
        /// </summary>
        /// <param name="korrigierteTexte">Lokale korrigierte Textfragmente (bleiben lokal).</param>
        /// <param name="kategorien">Zugehörige Korrektur-Kategorien.</param>
        /// <param name="epsilon">Differential-Privacy-Epsilon; 0 = kein Rauschen.</param>
        public static MetricsPayload Extract(
            IReadOnlyList<string> korrigierteTexte,
            IReadOnlyList<string> kategorien,
            double epsilon = 0.0)
        {
            var payload = new MetricsPayload();

            // 1. Satzlängen-Verteilung (Bucketed, keine Originaltexte)
            var sentenceLengths = new List<int>();
            foreach (var text in korrigierteTexte ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                var sentences = SentenceDelimiterPattern.Split(text)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim());
                foreach (var sentence in sentences)
                {
                    var wordCount = CountWords(sentence);
                    if (wordCount > 0) sentenceLengths.Add(wordCount);
                }
            }

            foreach (var length in sentenceLengths)
            {
                var bucket = GetBucketLabel(length);
                payload.satzlaengen_verteilung[bucket] =
                    payload.satzlaengen_verteilung.GetValueOrDefault(bucket) + 1;
            }

            // 2. Wortklassen-Häufigkeiten (abstrahiert, keine Einzelwörter)
            var wordClassCounts = new Dictionary<string, int>();
            foreach (var text in korrigierteTexte ?? Enumerable.Empty<string>())
            {
                foreach (var token in Tokenize(text))
                {
                    var wordClass = ClassifyWord(token);
                    wordClassCounts[wordClass] = wordClassCounts.GetValueOrDefault(wordClass) + 1;
                }
            }
            payload.wortklassen_haeufigkeiten = wordClassCounts;

            // 3. Korrektur-Kategorien (vom Client lokal klassifiziert)
            foreach (var category in kategorien ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(category)) continue;
                var normalized = category.ToLowerInvariant().Trim();
                if (string.IsNullOrEmpty(normalized)) continue;
                payload.korrektur_kategorien[normalized] =
                    payload.korrektur_kategorien.GetValueOrDefault(normalized) + 1;
            }

            if (epsilon > 0)
            {
                ApplyLaplaceNoise(payload, epsilon);
            }

            return payload;
        }

        private static int CountWords(string sentence)
        {
            return WordPattern.Matches(sentence).Count;
        }

        private static IEnumerable<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Enumerable.Empty<string>();
            return WordPattern.Matches(text)
                .Cast<Match>()
                .Select(m => m.Value.ToLowerInvariant())
                .Where(t => !StopWords.Contains(t));
        }

        private static string GetBucketLabel(int wordCount)
        {
            return wordCount switch
            {
                <= 5 => "0-5",
                <= 10 => "6-10",
                <= 15 => "11-15",
                <= 25 => "16-25",
                <= 40 => "26-40",
                _ => "40+",
            };
        }

        private static string ClassifyWord(string token)
        {
            if (string.IsNullOrEmpty(token)) return "other";
            if (char.IsUpper(token[0])) return "noun_or_name"; // anonymisiert, keine Namen
            if (token.EndsWith("en") || token.EndsWith("ern") || token.EndsWith("eln")) return "verb_infinitive";
            if (token.EndsWith("ung") || token.EndsWith("heit") || token.EndsWith("keit")
                || token.EndsWith("schaft") || token.EndsWith("tion") || token.EndsWith("ität")) return "noun_derivative";
            if (token.EndsWith("lich") || token.EndsWith("isch") || token.EndsWith("ig")
                || token.EndsWith("bar") || token.EndsWith("sam")) return "adjective";
            if (token.EndsWith("er") || token.EndsWith("en") || token.EndsWith("sten")
                || token.EndsWith("ste")) return "adjective_or_adverb";
            return "other";
        }

        private static void ApplyLaplaceNoise(MetricsPayload payload, double epsilon)
        {
            var rnd = new Random();
            AddNoise(payload.satzlaengen_verteilung, epsilon, rnd);
            AddNoise(payload.wortklassen_haeufigkeiten, epsilon, rnd);
            AddNoise(payload.korrektur_kategorien, epsilon, rnd);
        }

        private static void AddNoise(Dictionary<string, int> counts, double epsilon, Random rnd)
        {
            if (epsilon <= 0 || counts == null) return;
            var scale = 1.0 / epsilon;
            foreach (var key in counts.Keys.ToList())
            {
                var noise = LaplaceNoise(rnd, scale);
                var value = counts[key] + noise;
                counts[key] = value < 0 ? 0 : value;
            }
        }

        private static int LaplaceNoise(Random rnd, double scale)
        {
            var u = rnd.NextDouble() - 0.5;
            var noise = -scale * Math.Sign(u) * Math.Log(1 - 2 * Math.Abs(u));
            return (int)Math.Round(noise);
        }
    }
}
