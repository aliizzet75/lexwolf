using System;
using LexWolf.Database;

namespace LexWolf.Services;

/// <summary>
/// Persistiert, wie ein Anwalt auf eine Feature-Fertigstellungs-Meldung reagiert hat
/// ("restart" oder "later"). Bewusst ohne WPF-Abhängigkeit gehalten (nur LocalDb/SQLite),
/// damit die Logik plattformunabhängig unit-testbar ist (siehe LexWolf.Tests).
/// </summary>
public sealed class FeatureNotificationStore
{
    private readonly LocalDb _db;
    private readonly Action<string> _log;

    private const string NotificationTableSql = @"
        CREATE TABLE IF NOT EXISTS feature_notifications (
            milestone_id INTEGER PRIMARY KEY,
            status       TEXT,
            gesehen      TEXT
        );";

    public FeatureNotificationStore(LocalDb db, Action<string>? log = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _log = log ?? (_ => { });
        EnsureTable();
    }

    private void EnsureTable()
    {
        try
        {
            using var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = NotificationTableSql;
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _log($"[FeatureNotificationStore] Notification-Tabelle konnte nicht angelegt werden: {ex.Message}");
        }
    }

    /// <summary>
    /// Nur als "gesehen" gelten, wenn der Anwalt bereits neu gestartet hat.
    /// "Später erinnern" darf den Toast nicht dauerhaft unterdrücken, damit er
    /// beim nächsten periodischen Check erneut erscheint.
    /// </summary>
    public bool IsAlreadySeen(int milestoneId)
    {
        try
        {
            using var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT status FROM feature_notifications WHERE milestone_id = $id;";
            cmd.Parameters.AddWithValue("$id", milestoneId);
            var status = cmd.ExecuteScalar() as string;
            return string.Equals(status, "restart", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _log($"[FeatureNotificationStore] IsAlreadySeen fehlgeschlagen: {ex.Message}");
            return false;
        }
    }

    public void MarkAsSeen(int milestoneId, string reaction)
    {
        try
        {
            using var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO feature_notifications (milestone_id, status, gesehen)
                VALUES ($id, $status, $gesehen)
                ON CONFLICT(milestone_id) DO UPDATE SET status=$status, gesehen=$gesehen;
            ";
            cmd.Parameters.AddWithValue("$id", milestoneId);
            cmd.Parameters.AddWithValue("$status", reaction);
            cmd.Parameters.AddWithValue("$gesehen", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _log($"[FeatureNotificationStore] MarkAsSeen fehlgeschlagen: {ex.Message}");
        }
    }
}
