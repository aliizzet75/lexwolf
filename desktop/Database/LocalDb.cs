using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace LexWolf.Database
{
    public partial class LocalDb
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
            // Ohne Pooling: jede Methode öffnet ohnehin ihre eigene kurze Verbindung,
            // und der Pool hielte die Datei unter Windows sonst über Dispose hinaus
            // gesperrt (Tests konnten ihre Temp-Datenbank nicht löschen).
            var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
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
                    typ          TEXT,
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

                -- Mandanten-Gespräche (Task #130): Erweiterung der bestehenden M16-Struktur
                -- um Gesprächsprotokolle (z.B. via GespraechsRecorder transkribiert), statt
                -- einer eigenen separaten Datenbank. Gleiche LocalDb-Verbindung und gleiche
                -- Mandanten-Zuordnung (mandant_id) wie chat_history/notizen.
                CREATE TABLE IF NOT EXISTS gespraeche (
                    id                TEXT PRIMARY KEY,
                    mandant_id        TEXT,
                    datum             TEXT,
                    strukturiert_json TEXT,
                    erstellt          TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_gespraeche_mandant ON gespraeche(mandant_id, datum);
            ";
            cmd.ExecuteNonQuery();
            // Bestehende Datenbanken (vor "typ"-Spalte) per ALTER TABLE nachziehen —
            // CREATE TABLE IF NOT EXISTS fasst eine schon vorhandene Tabelle nicht an.
            MigriereSpalteFallsFehlt(conn, "dokumente", "typ", "TEXT");
            InitializeAkte(conn);
            InitializeEinwilligung(conn);
        }

        private static void MigriereSpalteFallsFehlt(SqliteConnection conn, string tabelle, string spalte, string spaltenTyp)
        {
            var vorhanden = false;
            using (var pragma = conn.CreateCommand())
            {
                pragma.CommandText = $"PRAGMA table_info({tabelle});";
                using var r = pragma.ExecuteReader();
                while (r.Read())
                {
                    if (string.Equals(r.GetString(1), spalte, StringComparison.OrdinalIgnoreCase))
                    {
                        vorhanden = true;
                        break;
                    }
                }
            }
            if (vorhanden) return;
            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE {tabelle} ADD COLUMN {spalte} {spaltenTyp};";
            alter.ExecuteNonQuery();
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

        /// <summary>Holt Mandanten projiziert für Liste/Dropdown, optional gefiltert.
        /// Nur id + name werden geladen; keine weiteren Mandantendetails, damit
        /// Dropdown und linke Liste bei >=200 Mandanten schlank bleiben.</summary>
        public System.Collections.Generic.List<(string Id, string Name)> SearchMandanten(string? q)
        {
            var result = new System.Collections.Generic.List<(string, string)>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            var filter = (q ?? "").Trim();
            if (!string.IsNullOrEmpty(filter))
            {
                cmd.CommandText = "SELECT id, name FROM mandanten WHERE name LIKE $q ESCAPE '\\' ORDER BY name COLLATE NOCASE;";
                cmd.Parameters.AddWithValue("$q", "%" + EscapeLike(filter) + "%");
            }
            else
            {
                cmd.CommandText = "SELECT id, name FROM mandanten ORDER BY name COLLATE NOCASE;";
            }
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add((reader.GetString(0), reader.GetString(1)));
            return result;
        }

        private static string EscapeLike(string s)
        {
            return s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }

        // --- Dokumente ---

        /// <param name="typ">Dokumenttyp, z. B. "Gutachten" für einen per SchriftsatzDialog erzeugten
        /// Gutachten-Entwurf (siehe DokumentExport.TypAusDateiname); "" wenn kein bekannter Typ.</param>
        public void UpsertDokumentChunk(string id, string mandantId, string pfad,
                                        string titel, string inhaltsChunk, string geaendert, string typ = "")
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO dokumente (id, mandant_id, pfad, titel, inhalt_chunk, typ, erstellt, geaendert)
                VALUES ($id, $mandant_id, $pfad, $titel, $chunk, $typ, $erstellt, $geaendert)
                ON CONFLICT(id) DO UPDATE SET inhalt_chunk=$chunk, typ=$typ, geaendert=$geaendert;
            ";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$mandant_id", mandantId);
            cmd.Parameters.AddWithValue("$pfad", pfad);
            cmd.Parameters.AddWithValue("$titel", titel);
            cmd.Parameters.AddWithValue("$chunk", inhaltsChunk);
            cmd.Parameters.AddWithValue("$typ", typ);
            cmd.Parameters.AddWithValue("$erstellt", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$geaendert", geaendert);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Dokumenttyp, wie beim Einlesen in UpsertDokumentChunk gesetzt (z. B. "Gutachten");
        /// "" wenn das Dokument unbekannt ist oder keinen Typ hat.</summary>
        public string GetDokumentTyp(string pfad)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT typ FROM dokumente WHERE pfad = $pfad LIMIT 1;";
            cmd.Parameters.AddWithValue("$pfad", pfad);
            return cmd.ExecuteScalar() as string ?? "";
        }

        /// <summary>Entfernt alle Chunks einer Datei — beim Löschen/Verschieben und vor
        /// dem Neu-Einlesen (sonst blieben bei kürzerem neuen Inhalt die hinteren
        /// Chunks der alten Fassung stehen und landeten weiter im KI-Kontext).</summary>
        public void DeleteDokumentChunks(string pfad)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM dokumente WHERE pfad = $pfad;";
            cmd.Parameters.AddWithValue("$pfad", pfad);
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

        // --- Gespräche (Task #130) ---

        /// <summary>Speichert ein Mandantengespräch (z.B. Whisper-Transkript aus
        /// GespraechsRecorder) strukturiert als JSON — mandantenbezogen, in derselben
        /// LocalDb wie Chat-Historie und Notizen. <paramref name="strukturiertJson"/>
        /// liegt in der Verantwortung des Aufrufers (z.B. {"zusammenfassung": "...",
        /// "transkript": "..."}).</summary>
        public void InsertGespraech(string mandantId, DateTime datum, string strukturiertJson)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO gespraeche (id, mandant_id, datum, strukturiert_json, erstellt)
                VALUES ($id, $mandant_id, $datum, $json, $erstellt);
            ";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$mandant_id", mandantId);
            cmd.Parameters.AddWithValue("$datum", datum.ToString("o"));
            cmd.Parameters.AddWithValue("$json", strukturiertJson);
            cmd.Parameters.AddWithValue("$erstellt", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        /// <summary>Gespräche eines Mandanten, älteste zuerst (wie GetChatZusammenfassungen) —
        /// wird von der bestehenden Historien-/Kontextabfrage (MandantAnalyseService,
        /// MandantZusammenfassungService) zusammen mit Chat-Zusammenfassungen und Notizen
        /// gelesen, damit Fragen wie "Was haben wir beim letzten Gespräch besprochen?" über
        /// denselben Mandanten-Kontext beantwortet werden können.</summary>
        public System.Collections.Generic.List<(string Id, string MandantId, DateTime Datum, string StrukturiertJson, DateTime Erstellt)> GetGespraeche(string mandantId)
        {
            var result = new System.Collections.Generic.List<(string, string, DateTime, string, DateTime)>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, mandant_id, datum, strukturiert_json, erstellt
                FROM gespraeche
                WHERE mandant_id = $mandantId
                ORDER BY datum ASC;
            ";
            cmd.Parameters.AddWithValue("$mandantId", mandantId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    reader.IsDBNull(3) ? "" : reader.GetString(3),
                    DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind)
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

        /// <summary>Löscht den gesamten lokalen Feature-Wunsch-Verlauf des aktuellen
        /// Anwalts. Affectiert ausschließlich die eigene Datenbank — keine Daten
        /// anderer Nutzer.</summary>
        public void ClearFeatureWunschHistory()
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM feature_wunsch_history;";
            cmd.ExecuteNonQuery();
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
