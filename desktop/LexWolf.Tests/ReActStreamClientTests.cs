using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LexWolf.Models;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// T#295: Live-ReAct-Visualisierung im Client. Testet die reine SSE-
    /// Parsing-Logik ohne Netzwerk UND — per expliziter Vorgabe (echte DB
    /// statt Mock) — den vollen Weg über einen echten, für den Test
    /// gestarteten Backend-Prozess gegen die echten Postgres/Neo4j-
    /// Verbindungen (keine SQLite/Mock-Fallbacks).
    /// </summary>
    public class ReActStreamClientParsingTests
    {
        [Fact]
        public void ParseSseLine_MitAllenFeldern_LiefertEvent()
        {
            var line = "data: {\"step\": \"suchergebnis\", \"text\": \"§ 573 BGB\", \"quelle\": \"Gesetzestext: Zivilrecht\", \"confidence\": 0.87, \"status\": \"gefunden\"}";

            var evt = ReActStreamClient.ParseSseLine(line);

            Assert.NotNull(evt);
            Assert.Equal("suchergebnis", evt!.Step);
            Assert.Equal("§ 573 BGB", evt.Text);
            Assert.Equal("Gesetzestext: Zivilrecht", evt.Quelle);
            Assert.Equal(0.87, evt.Confidence);
            Assert.Equal("gefunden", evt.Status);
            Assert.False(evt.IstVerworfen);
        }

        [Fact]
        public void ParseSseLine_StatusVerworfen_SetztIstVerworfen()
        {
            var line = "data: {\"step\": \"suchergebnis\", \"text\": \"irrelevant\", \"confidence\": 0.1, \"status\": \"verworfen\"}";

            var evt = ReActStreamClient.ParseSseLine(line);

            Assert.NotNull(evt);
            Assert.True(evt!.IstVerworfen);
            Assert.Equal("10%", evt.ConfidenceLabel);
        }

        [Fact]
        public void ParseSseLine_OhneDataPrefix_LiefertNull()
        {
            Assert.Null(ReActStreamClient.ParseSseLine(""));
            Assert.Null(ReActStreamClient.ParseSseLine("event: ping"));
        }

        [Fact]
        public void ParseSseLine_UngueltigesJson_LiefertNull()
        {
            Assert.Null(ReActStreamClient.ParseSseLine("data: {nicht valides json"));
        }

        [Fact]
        public void ParseSseLine_OhneOptionaleFelder_LaesstSieNull()
        {
            var line = "data: {\"step\": \"fertig\", \"text\": \"Suche abgeschlossen\"}";

            var evt = ReActStreamClient.ParseSseLine(line);

            Assert.NotNull(evt);
            Assert.Equal("fertig", evt!.Step);
            Assert.Null(evt.Quelle);
            Assert.Null(evt.Confidence);
            Assert.Null(evt.Status);
            Assert.Equal("", evt.ConfidenceLabel);
        }
    }

    /// <summary>
    /// Startet den echten FastAPI-Backend-Prozess (uvicorn main:app) für die
    /// Dauer der Tests und verbindet ihn mit den echten Postgres-/Neo4j-
    /// Instanzen auf localhost — keine Mocks, kein SQLite-Fallback.
    /// </summary>
    public class ReActBackendFixture : IAsyncLifetime
    {
        private Process? _process;
        public string BaseUrl { get; } = "http://127.0.0.1:8098";
        public bool BackendErreichbar { get; private set; }

        public async Task InitializeAsync()
        {
            var backendDir = FindBackendDir();
            if (backendDir == null)
            {
                BackendErreichbar = false;
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = ResolvePython(),
                Arguments = "-m uvicorn main:app --host 127.0.0.1 --port 8098",
                WorkingDirectory = backendDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.Environment["DATABASE_URL"] = "postgresql://postgres:postgres@localhost:5432/lexwolf";
            psi.Environment["NEO4J_URI"] = "bolt://localhost:7687";
            psi.Environment["NEO4J_USER"] = "neo4j";
            psi.Environment["NEO4J_PASSWORD"] = "lexwolf123";
            psi.Environment["OLLAMA_URL"] = "http://localhost:11434";
            psi.Environment["LLM_PROVIDER"] = "ollama";

            try
            {
                _process = Process.Start(psi);
            }
            catch
            {
                BackendErreichbar = false;
                return;
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var resp = await http.GetAsync($"{BaseUrl}/health");
                    if (resp.IsSuccessStatusCode)
                    {
                        BackendErreichbar = true;
                        return;
                    }
                }
                catch
                {
                    // Backend noch nicht oben — weiter pollen
                }
                await Task.Delay(500);
            }
            BackendErreichbar = false;
        }

        public Task DisposeAsync()
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(5000);
                }
            }
            catch
            {
                // best effort beim Aufräumen
            }
            return Task.CompletedTask;
        }

        private static string? FindBackendDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                var candidate = Path.Combine(dir.FullName, "backend", "main.py");
                if (File.Exists(candidate))
                    return Path.Combine(dir.FullName, "backend");
                dir = dir.Parent;
            }
            return null;
        }

        private static string ResolvePython()
        {
            const string venvPython = "/opt/lexwolf-venv/bin/python3";
            return File.Exists(venvPython) ? venvPython : "python3";
        }
    }

    public class ReActStreamClientIntegrationTests : IClassFixture<ReActBackendFixture>
    {
        private readonly ReActBackendFixture _fixture;

        public ReActStreamClientIntegrationTests(ReActBackendFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task StreamAsync_GegenEchteDatenbank_LiefertSuchschritteMitQuelleUndKonfidenz()
        {
            Assert.True(_fixture.BackendErreichbar, "Echtes Backend (uvicorn gegen echte Postgres/Neo4j) konnte nicht gestartet werden — Test schlägt bewusst fehl statt zu überspringen.");

            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var client = new ReActStreamClient(http, _fixture.BaseUrl);

            var events = new List<ReActStepEvent>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await foreach (var evt in client.StreamAsync("Kündigung Mietvertrag", "schnell", cts.Token))
            {
                events.Add(evt);
            }

            Assert.NotEmpty(events);

            var suchergebnisse = events.Where(e => e.Step == "suchergebnis").ToList();
            Assert.NotEmpty(suchergebnisse);

            // DoD T#295: Konfidenz pro Schritt sichtbar
            Assert.All(suchergebnisse, e => Assert.True(e.Confidence.HasValue));

            // DoD T#295: welche Quelle durchsucht wird
            Assert.All(suchergebnisse, e => Assert.False(string.IsNullOrWhiteSpace(e.Quelle)));

            // Jeder Suchschritt ist eindeutig als gefunden oder verworfen markiert
            Assert.All(suchergebnisse, e => Assert.Contains(e.Status, new[] { "gefunden", "verworfen" }));

            Assert.Contains(events, e => e.Step == "fertig");
        }
    }
}
