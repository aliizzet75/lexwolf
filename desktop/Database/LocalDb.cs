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

                CREATE TABLE IF NOT EXISTS chat_zusammenfassungen (
                    id              TEXT PRIMARY KEY,
                    mandant_id      TEXT,
                    sitzung_start   TEXT,
                    sitzung_ende    TEXT,
                    zusammenfassung TEXT,
                    erstellt        TEXT
                );

                CREATE TABLE IF NOT EXISTS notizen (
                    id          TEXT PRIMARY KEY,
                    mandant_id  TEXT,
                    titel_kurz  TEXT,
                    text        TEXT,
                    erstellt    TEXT,
                    geaendert   TEXT
                );

                CREATE TABLE IF NOT EXISTS mandant_zusammenfassung (
                    mandant_id  TEXT PRIMARY KEY,
                    text        TEXT,
                    quellen_hash TEXT,
                    erstellt    TEXT
                );

                CREATE TABLE IF NOT EXISTS feature_wunsch_history (
                    id        TEXT PRIMARY KEY,
                    role      TEXT,
                    content   TEXT,
                    timestamp TEXT
                );
            ";
            cmd.ExecuteNonQuery();
        }

        // --- Notizen ---

        public void InsertNotiz(string id, string mandantId, string titelKurz, string text)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            var jetzt = DateTime.UtcNow.ToString("o");
            cmd.CommandText = @"
                INSERT INTO notizen (id, mandant_id, titel_kurz, text, erstellt, geaendert)
                VALUES ($id, $mandant_id, $titel_kurz, $text, $erstellt, $geaendert);
            ";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$mandant_id", mandantId);
            cmd.Parameters.AddWithValue("$titel_kurz", titelKurz);
            cmd.Parameters.AddWithValue("$text", text);
            cmd.Parameters.AddWithValue("$erstellt", jetzt);
            cmd.Parameters.AddWithValue("$geaendert", jetzt);
            cmd.ExecuteNonQuery();
        }

        public void UpdateNotiz(string id, string titelKurz, string text)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE notizen
                SET titel_kurz = $titel_kurz,
                    text = $text,
                    geaendert = $geaendert
                WHERE id = $id;
            ";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$titel_kurz", titelKurz);
            cmd.Parameters.AddWithValue("$text", text);
            cmd.Parameters.AddWithValue("$geaendert", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        public void DeleteNotiz(string id)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM notizen WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        public System.Collections.Generic.List<(string Id, string MandantId, string TitelKurz,
            string Text, DateTime Erstellt, DateTime Geaendert)> GetNotizen(string mandantId)
        {
            var result = new System.Collections.Generic.List<(string, string, string, string, DateTime, DateTime)>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, mandant_id, titel_kurz, text, erstellt, geaendert
                FROM notizen
                WHERE mandant_id = $mandantId
                ORDER BY erstellt DESC;
            ";
            cmd.Parameters.AddWithValue("$mandantId", mandantId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? "" : reader.GetString(3),
                    DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind)
                ));
            }
            return result;
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
        /// (mehrere Chunks pro Datei werden zusammengefügt). Wird für den lokalen
        /// Mandanten-Kontext im Chat gebraucht — ohne das sieht das LLM nur Name/ID,
        /// nicht was tatsächlich im Mandantenordner liegt.
        /// Kein Deckel pro Dokument mehr (war 4 Chunks ≈ 1700 Zeichen) — ein
        /// mehrseitiges Dokument wurde dadurch mitten im Satz abgeschnitten, bevor
        /// die verknüpfende Information (z.B. "Frau: 2.331,87 €" auf Seite 2) das
        /// Modell überhaupt erreichte. Nur die Anzahl Dokumente bleibt gedeckelt,
        /// als Schutz gegen einen sehr großen Mandantenordner.</summary>
        public System.Collections.Generic.List<(string Titel, string Pfad, string Text)> GetDokumenteForMandant(
            string mandantId, int maxChunksProDokument = int.MaxValue, int maxDokumente = 20)
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

            var result = new System.Collections.Generic.List<(string, string, string)>();
            foreach (var (pfad, (titel, text, _)) in byPfad)
            {
                result.Add((titel, pfad, text.ToString().Trim()));
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

        /// <summary>Chat-Nachrichten eines Mandanten seit einem Zeitpunkt (exklusiv), oder
        /// der komplette Verlauf wenn <paramref name="seit"/> null ist. Wird vom
        /// ChatSummaryService genutzt, um nur den Teil des Verlaufs zu summarisieren,
        /// der noch nicht in einer früheren Zusammenfassung erfasst wurde.</summary>
        public System.Collections.Generic.List<(string Role, string Content, DateTime Timestamp)> GetChatHistorySeit(
            string mandantId, DateTime? seit)
        {
            var result = new System.Collections.Generic.List<(string, string, DateTime)>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT role, content, timestamp FROM chat_history
                WHERE mandant_id = $mandantId
                  AND ($seit IS NULL OR timestamp > $seit)
                ORDER BY timestamp ASC;
            ";
            cmd.Parameters.AddWithValue("$mandantId", mandantId);
            cmd.Parameters.AddWithValue("$seit", (object?)seit?.ToString("o") ?? DBNull.Value);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind)
                ));
            }
            return result;
        }

        /// <summary>Ende-Zeitpunkt der jüngsten Zusammenfassung eines Mandanten, oder
        /// null wenn noch keine existiert.</summary>
        public DateTime? GetLetzteZusammenfassungEnde(string mandantId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT MAX(sitzung_ende) FROM chat_zusammenfassungen
                WHERE mandant_id = $mandantId;
            ";
            cmd.Parameters.AddWithValue("$mandantId", mandantId);
            var result = cmd.ExecuteScalar();
            if (result is null || result is DBNull) return null;
            return DateTime.Parse((string)result, null, System.Globalization.DateTimeStyles.RoundtripKind);
        }

        // --- Chat-Zusammenfassungen ---

        public void InsertChatZusammenfassung(string mandantId, DateTime sitzungStart,
                                              DateTime sitzungEnde, string zusammenfassung)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO chat_zusammenfassungen
                    (id, mandant_id, sitzung_start, sitzung_ende, zusammenfassung, erstellt)
                VALUES
                    ($id, $mandant_id, $sitzung_start, $sitzung_ende, $zusammenfassung, $erstellt);
            ";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$mandant_id", mandantId);
            cmd.Parameters.AddWithValue("$sitzung_start", sitzungStart.ToString("o"));
            cmd.Parameters.AddWithValue("$sitzung_ende", sitzungEnde.ToString("o"));
            cmd.Parameters.AddWithValue("$zusammenfassung", zusammenfassung);
            cmd.Parameters.AddWithValue("$erstellt", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        public System.Collections.Generic.List<(string Id, string MandantId, DateTime SitzungStart,
            DateTime SitzungEnde, string Zusammenfassung, DateTime Erstellt)> GetChatZusammenfassungen(string mandantId)
        {
            var result = new System.Collections.Generic.List<(string, string, DateTime, DateTime, string, DateTime)>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, mandant_id, sitzung_start, sitzung_ende, zusammenfassung, erstellt
                FROM chat_zusammenfassungen
                WHERE mandant_id = $mandantId
                ORDER BY sitzung_start ASC;
            ";
            cmd.Parameters.AddWithValue("$mandantId", mandantId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    reader.GetString(4),
                    DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind)
                ));
            }
            return result;
        }

        // --- Mandant-Zusammenfassung (Task #219) ---

        public void UpsertMandantZusammenfassung(string mandantId, string text, string quellenHash)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO mandant_zusammenfassung (mandant_id, text, quellen_hash, erstellt)
                VALUES ($mandant_id, $text, $quellen_hash, $erstellt)
                ON CONFLICT(mandant_id) DO UPDATE SET text=$text, quellen_hash=$quellen_hash, erstellt=$erstellt;
            ";
            cmd.Parameters.AddWithValue("$mandant_id", mandantId);
            cmd.Parameters.AddWithValue("$text", text);
            cmd.Parameters.AddWithValue("$quellen_hash", quellenHash);
            cmd.Parameters.AddWithValue("$erstellt", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        public (string? Text, string? QuellenHash, DateTime? Erstellt)? GetMandantZusammenfassung(string mandantId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT text, quellen_hash, erstellt
                FROM mandant_zusammenfassung
                WHERE mandant_id = $mandantId;
            ";
            cmd.Parameters.AddWithValue("$mandantId", mandantId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            var text = reader.IsDBNull(0) ? null : reader.GetString(0);
            var hash = reader.IsDBNull(1) ? null : reader.GetString(1);
            var erstellt = reader.IsDBNull(2)
                ? (DateTime?)null
                : DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind);
            return (text, hash, erstellt);
        }

        // --- Feature-Wunsch-Verlauf ---

        public void AddFeatureWunschMessage(string role, string content)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO feature_wunsch_history (id, role, content, timestamp)
                VALUES ($id, $role, $content, $timestamp);
            ";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$role", role);
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        /// <summary>Letzte Feature-Wunsch-Nachrichten in chronologischer Reihenfolge
        /// (älteste zuerst). Wird beim Öffnen des Feature-Wunsch-Dialogs geladen,
        /// damit frühere Fragen/Antworten sichtbar bleiben und die KI Kontext hat,
        /// falls der Anwalt sich auf einen älteren Wunsch bezieht.</summary>
        public System.Collections.Generic.List<(string Role, string Content, DateTime Timestamp)> GetFeatureWunschHistory(int limit = 40)
        {
            var result = new System.Collections.Generic.List<(string, string, DateTime)>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT role, content, timestamp FROM (
                    SELECT role, content, timestamp FROM feature_wunsch_history
                    ORDER BY timestamp DESC
                    LIMIT $limit
                )
                ORDER BY timestamp ASC;
            ";
            cmd.Parameters.AddWithValue("$limit", limit);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind)
                ));
            }
            return result;
        }

        public DateTime? GetMaxGeaendert(string mandantId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT MAX(t) FROM (
                    SELECT MAX(geaendert) AS t FROM notizen WHERE mandant_id = $mandantId
                    UNION ALL
                    SELECT MAX(timestamp)  AS t FROM chat_history WHERE mandant_id = $mandantId
                    UNION ALL
                    SELECT MAX(geaendert)  AS t FROM dokumente WHERE mandant_id = $mandantId
                );
            ";
            cmd.Parameters.AddWithValue("$mandantId", mandantId);
            var result = cmd.ExecuteScalar();
            if (result is null || result is DBNull) return null;
            return DateTime.Parse((string)result, null, System.Globalization.DateTimeStyles.RoundtripKind);
        }
    }
}
