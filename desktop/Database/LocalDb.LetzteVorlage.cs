using System;
using Microsoft.Data.Sqlite;

namespace LexWolf.Database
{
    // Zuletzt gewählte Vorlage pro Schriftsatz-Typ (Task #304): ersetzt die bisherige
    // Heuristik in MainWindow.GetLatestTemplateContext (jüngste LastWriteTimeUtc-Datei im
    // Vorlagen-Ordner = "zuletzt geändert") durch eine echte Auswahl-Speicherung. Wird nur
    // geschrieben, wenn der Anwalt im Schriftsatz-Dialog explizit eine Vorlagen-Datei
    // auswählt — nicht automatisch bei jedem Dateizugriff.
    public partial class LocalDb
    {
        private void InitializeLetzteVorlage(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS letzte_vorlage_pro_typ (
                    schriftsatz_typ TEXT PRIMARY KEY,
                    vorlage_pfad    TEXT,
                    gewaehlt_am     TEXT
                );
            ";
            cmd.ExecuteNonQuery();
        }

        /// <summary>Speichert, welche Vorlagen-Datei der Anwalt für einen Schriftsatz-Typ
        /// ausgewählt hat. Aufrufer: die explizite "Vorlage wählen"-Auswahl im Schriftsatz-Dialog —
        /// nicht ein bloßer Lesezugriff auf die Datei.</summary>
        public void UpsertLetzteVorlage(string schriftsatzTyp, string vorlagePfad)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO letzte_vorlage_pro_typ (schriftsatz_typ, vorlage_pfad, gewaehlt_am)
                VALUES ($typ, $pfad, $gewaehlt_am)
                ON CONFLICT(schriftsatz_typ) DO UPDATE SET vorlage_pfad=$pfad, gewaehlt_am=$gewaehlt_am;
            ";
            cmd.Parameters.AddWithValue("$typ", schriftsatzTyp);
            cmd.Parameters.AddWithValue("$pfad", vorlagePfad);
            cmd.Parameters.AddWithValue("$gewaehlt_am", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        /// <summary>Zuletzt für diesen Schriftsatz-Typ gewählte Vorlagen-Datei, oder null
        /// wenn für diesen Typ noch keine Auswahl gespeichert wurde.</summary>
        public (string VorlagePfad, DateTime GewaehltAm)? GetLetzteVorlage(string schriftsatzTyp)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT vorlage_pfad, gewaehlt_am FROM letzte_vorlage_pro_typ
                WHERE schriftsatz_typ = $typ;
            ";
            cmd.Parameters.AddWithValue("$typ", schriftsatzTyp);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            return (
                reader.GetString(0),
                DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind)
            );
        }

        /// <summary>Die über alle Schriftsatz-Typen hinweg zuletzt bewusst gewählte Vorlage —
        /// für den allgemeinen Chat-Kontext (MainWindow.GetLatestTemplateContext), der anders
        /// als der Schriftsatz-Dialog keinen konkreten Schriftsatz-Typ kennt.</summary>
        public (string SchriftsatzTyp, string VorlagePfad, DateTime GewaehltAm)? GetLetzteVorlageGlobal()
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT schriftsatz_typ, vorlage_pfad, gewaehlt_am FROM letzte_vorlage_pro_typ
                ORDER BY gewaehlt_am DESC
                LIMIT 1;
            ";
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            return (
                reader.GetString(0),
                reader.GetString(1),
                DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind)
            );
        }
    }
}
