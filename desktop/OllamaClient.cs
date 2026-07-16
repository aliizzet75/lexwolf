using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LexWolf;

public class OllamaNotReachableException : Exception
{
    public OllamaNotReachableException(string message, Exception? inner = null)
        : base(message, inner) { }
}

public class OllamaClient
{
    public const string DefaultUrl = "http://localhost:11434";

    private readonly string _baseUrl;
    private readonly HttpClient _http;

    public OllamaClient(string? baseUrl = null)
    {
        _baseUrl = (baseUrl ?? Environment.GetEnvironmentVariable("OLLAMA_URL") ?? DefaultUrl)
                   .TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<string> GenerateAsync(string model, string prompt)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            model,
            prompt,
            stream = false
        });

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(
                $"{_baseUrl}/api/generate",
                new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
        }
        catch (Exception ex)
        {
            throw new OllamaNotReachableException(
                $"Ollama nicht erreichbar unter {_baseUrl}", ex);
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("response").GetString() ?? string.Empty;
    }

    public async Task<string> AnonymizeAsync(string text)
        => await GenerateAsync("mistral:7b",
            $"Anonymisiere den folgenden Text vollständig. Ersetze alle personenbezogenen Daten " +
            $"(Namen, Adressen, Telefonnummern, E-Mail-Adressen, Geburtsdaten) durch [ANONYMISIERT]:\n\n{text}");

    public async Task<string> ClassifyAsync(string text)
        => await GenerateAsync("mistral:7b",
            $"Klassifiziere das folgende Rechtsdokument in eine Kategorie " +
            $"(z.B. Vertrag, Klage, Bescheid, Urteil). Antworte nur mit dem Kategorienamen:\n\n{text}");
}
