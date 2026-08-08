using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace LexWolf.Database
{
    public class LocalDb
    {
        private readonly string _dbPath;

        public LocalDb(string dbPath = null)
        {
            _dbPath = dbPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LexWolf", "lexwolf_local.db");

            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
            Initialize();
        }

        public SqliteConnection GetConnection()
        {
            var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            return conn;
        }

        private void Initialize()
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS mandanten (
                    id      TEXT PRIMARY KEY,
                    name    TEXT,
                    nummer  TEXT,
                    erstellt TEXT
                );

                CREATE TABLE IF NOT EXISTS dokumente (
                    id           TEXT PRIMARY KEY,
                    mandant_id   TEXT,
                    pfad         TEXT,
                    titel        TEXT,
                    inhalt_chunk TEXT,
                    erstellt     TEXT,
                    geaendert    TEXT
                );

                CREATE TABLE IF NOT EXISTS chat_history (
                    id         TEXT PRIMARY KEY,
                    mandant_id TEXT,
                    role       TEXT,
                    content    TEXT,
                    timestamp  TEXT
                );
            ";
            cmd.ExecuteNonQuery();
        }

        // --- Mandanten ---

        public void UpsertMandant(string id, string name, string nummer)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO mandanten (id, name, nummer, erstellt)
                VALUES ($id, $name, $nummer, $erstellt)
                ON CONFLICT(id) DO UPDATE SET name=$name, nummer=$nummer;
            ";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$nummer", nummer);
            cmd.Parameters.AddWithValue("$erstellt", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        public System.Collections.Generic.List<(string Id, string Name)> GetMandanten()
        {
            var result = new System.Collections.Generic.List<(string, string)>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name FROM mandanten ORDER BY name COLLATE NOCASE;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add((reader.GetString(0), reader.GetString(1)));
            return result;
        }

        // --- Dokumente ---

        public void UpsertDokumentChunk(string id, string mandantId, string pfad,
                                        string titel, string inhaltsChunk, string geaendert)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO dokumente (id, mandant_id, pfad, titel, inhalt_chunk, erstellt, geaendert)
                VALUES ($id, $mandant_id, $pfad, $titel, $chunk, $erstellt, $geaendert)
                ON CONFLICT(id) DO UPDATE SET inhalt_chunk=$chunk, geaendert=$geaendert;
            ";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$mandant_id", mandantId);
            cmd.Parameters.AddWithValue("$pfad", pfad);
            cmd.Parameters.AddWithValue("$titel", titel);
            cmd.Parameters.AddWithValue("$chunk", inhaltsChunk);
            cmd.Parameters.AddWithValue("$erstellt", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$geaendert", geaendert);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Alle gescannten Dokumente eines Mandanten, gruppiert nach Datei
        /// (mehrere Chunks pro Datei werden zusammengefügt, gedeckelt pro Datei und
        /// Gesamtanzahl damit der Chat-Kontext nicht ausufert). Wird für den lokalen
        /// Mandanten-Kontext im Chat gebraucht — ohne das sieht das LLM nur Name/ID,
        /// nicht was tatsächlich im Mandantenordner liegt.</summary>
        public System.Collections.Generic.List<(string Titel, string Text)> GetDokumenteForMandant(
            string mandantId, int maxChunksProDokument = 4, int maxDokumente = 12)
        {
            var byPfad = new System.Collections.Generic.Dictionary<string, (string Titel, System.Text.StringBuilder Text, int Count)>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT titel, pfad, inhalt_chunk FROM dokumente
                WHERE mandant_id = $mandantId
                ORDER BY pfad, id;
            ";
            cmd.Parameters.AddWithValue("$mandantId", mandantId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var titel = reader.GetString(0);
                var pfad  = reader.GetString(1);
                var chunk = reader.IsDBNull(2) ? "" : reader.GetString(2);
                if (!byPfad.TryGetValue(pfad, out var entry))
                    entry = (titel, new System.Text.StringBuilder(), 0);
                if (entry.Count < maxChunksProDokument)
                {
                    if (entry.Text.Length > 0) entry.Text.Append(' ');
                    entry.Text.Append(chunk);
                    entry = (entry.Titel, entry.Text, entry.Count + 1);
                }
                byPfad[pfad] = entry;
            }

            var result = new System.Collections.Generic.List<(string, string)>();
            foreach (var (titel, text, _) in byPfad.Values)
            {
                result.Add((titel, text.ToString().Trim()));
                if (result.Count >= maxDokumente) break;
            }
            return result;
        }

        // --- Chat History ---

        public void AddChatMessage(string mandantId, string role, string content)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO chat_history (id, mandant_id, role, content, timestamp)
                VALUES ($id, $mandant_id, $role, $content, $timestamp);
            ";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$mandant_id", mandantId);
            cmd.Parameters.AddWithValue("$role", role);
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }
    }
}
