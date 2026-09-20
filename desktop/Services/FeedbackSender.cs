using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LexWolf.Services;

namespace LexWolf.Services
{
    /// <summary>
    /// Sendet anonymisierte Stil-Metriken vom Desktop-Client an das Backend.
    /// Verwendet StyleMetricsExtractor, sodass NUR aggregierte Metriken das
    /// Gerät verlassen (keine Original-/Korrigiert-Texte, keine Namen, keine
    /// §-Referenzen, keine Geldbeträge).
    /// </summary>
    public class FeedbackSender
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public FeedbackSender(string baseUrl = "http://localhost:8000")
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Baut aus lokalen Korrektur-Daten den DSGVO-konformen Metrik-Payload
        /// und sendet ihn an POST /api/feedback/metrics.
        /// </summary>
        public async Task<bool> SendMetricsAsync(
            IReadOnlyList<string> correctedFragments,
            IReadOnlyList<string> categories,
            double epsilon = 0.0)
        {
            var metrics = StyleMetricsExtractor.Extract(correctedFragments, categories, epsilon);
            var payload = new
            {
                metrics.satzlaengen_verteilung,
                metrics.wortklassen_haeufigkeiten,
                metrics.korrektur_kategorien,
                zeitstempel = DateTime.UtcNow.ToString("O")
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/api/feedback/metrics", content);
            return response.IsSuccessStatusCode;
        }
    }
}
