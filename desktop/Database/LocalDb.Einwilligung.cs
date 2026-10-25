using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace LexWolf.Database
{
    /// <summary>Eine DSGVO-Einwilligung zur Gesprächsaufzeichnung (Task #128).</summary>
    public sealed record EinwilligungEintrag(
        string Id, string MandantId, DateTime Zeitstempel, string Text, bool Bestaetigt);

    // Einwilligungen zur Gesprächsaufzeichnung (DSGVO, Task #128).
    public partial class LocalDb
    {
        private void InitializeEinwilligung(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS einwilligung (
                    id          TEXT PRIMARY KEY,
                    mandant_id  TEXT,
                    zeitstempel TEXT,
                    text        TEXT,
                    bestaetigt  INTEGER
                );
            ";
            cmd.ExecuteNonQuery();
        }

        /// <summary>Speichert die Entscheidung eines Mandanten zur Gesprächsaufzeichnung mit
        /// Zeitstempel — Beleg für die DSGVO-Pflicht, dass der Mandant aktiv zugestimmt hat,
        /// bevor aufgezeichnet wird.</summary>
        public void InsertEinwilligung(string mandantId, string text, bool bestaetigt)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO einwilligung (id, mandant_id, zeitstempel, text, bestaetigt)
                VALUES ($id, $mandant_id, $zeitstempel, $text, $bestaetigt);
            ";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("$mandant_id", mandantId);
            cmd.Parameters.AddWithValue("$zeitstempel", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$text", text);
            cmd.Parameters.AddWithValue("$bestaetigt", bestaetigt ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        public List<EinwilligungEintrag> GetEinwilligungen(string mandantId)
        {
            var result = new List<EinwilligungEintrag>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, mandant_id, zeitstempel, text, bestaetigt
                FROM einwilligung
                WHERE mandant_id = $mandantId
                ORDER BY zeitstempel DESC;
            ";
            cmd.Parameters.AddWithValue("$mandantId", mandantId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new EinwilligungEintrag(
                    reader.GetString(0),
                    reader.GetString(1),
                    DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    reader.IsDBNull(3) ? "" : reader.GetString(3),
                    reader.GetInt64(4) != 0
                ));
            }
            return result;
        }
    }
}
