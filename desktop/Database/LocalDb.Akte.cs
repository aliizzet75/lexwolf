using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LexWolf.Database
{
    /// <summary>Eine Frist oder Wiedervorlage. Status: "vorschlag" (von LexWolf erkannt,
    /// noch nicht bestätigt), "offen" (bestätigt), "erledigt", "verworfen".</summary>
    public sealed record FristEintrag(
        string Id, string MandantId, string Bezeichnung, string? FristartId, string Rechtsgrundlage,
        DateTime Ereignis, DateTime Ende, DateTime Vorfrist, bool Notfrist, string Status,
        string? QuellePfad, string Rechenweg, string Notiz, bool EreignisAngenommen);

    public sealed record EingangEintrag(
        string Pfad, string MandantId, string Titel, string Json, DateTime Eingang, bool Gelesen);

    public sealed record AktenStamm(
        string MandantId, string Gericht, string Aktenzeichen, string Gegner, string Gegnervertreter, string Notiz);

    // Fristen, Posteingang (Dokument-Auswertungen), Aktenstamm, Fragebogen.
    public partial class LocalDb
    {
        private void InitializeAkte(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS fristen (
                    id                  TEXT PRIMARY KEY,
                    mandant_id          TEXT,
                    bezeichnung         TEXT,
                    fristart_id         TEXT,
                    rechtsgrundlage     TEXT,
                    ereignis            TEXT,
                    ende                TEXT,
                    vorfrist            TEXT,
                    notfrist            INTEGER,
                    status              TEXT,
                    quelle_pfad         TEXT,
                    rechenweg           TEXT,
                    notiz               TEXT,
                    ereignis_angenommen INTEGER,
                    erstellt            TEXT,
                    geaendert           TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_fristen_ende ON fristen(status, ende);

                CREATE TABLE IF NOT EXISTS dokument_analyse (
                    pfad       TEXT PRIMARY KEY,
                    mandant_id TEXT,
                    titel      TEXT,
                    json       TEXT,
                    eingang    TEXT,
                    gelesen    INTEGER,
                    erstellt   TEXT
                );

                CREATE TABLE IF NOT EXISTS akte_stamm (
                    mandant_id      TEXT PRIMARY KEY,
                    gericht         TEXT,
                    aktenzeichen    TEXT,
                    gegner          TEXT,
                    gegnervertreter TEXT,
                    notiz           TEXT,
                    geaendert       TEXT
                );

                CREATE TABLE IF NOT EXISTS fragebogen (
                    mandant_id TEXT PRIMARY KEY,
                    json       TEXT,
                    geaendert  TEXT
                );

                CREATE TABLE IF NOT EXISTS mail_absender (
                    adresse    TEXT PRIMARY KEY,
                    mandant_id TEXT
                );

                CREATE TABLE IF NOT EXISTS postfach_unzugeordnet (
                    pfad           TEXT PRIMARY KEY,
                    absender       TEXT,
                    absender_name  TEXT,
                    betreff        TEXT,
                    datum          TEXT,
                    grund          TEXT
                );

                -- Formularfelder (Fragebogen, Rechner): Wert von LexWolf (aus der Akte) und Eingabe
                -- des Anwalts getrennt. anwalt_wert NULL = keine Eingabe, '' = bewusst geleert.
                CREATE TABLE IF NOT EXISTS formular_feld (
                    mandant_id  TEXT,
                    formular    TEXT,
                    feld        TEXT,
                    ki_wert     TEXT,
                    ki_quelle   TEXT,
                    anwalt_wert TEXT,
                    geaendert   TEXT,
                    PRIMARY KEY (mandant_id, formular, feld)
                );

                CREATE TABLE IF NOT EXISTS app_meta (
                    key   TEXT PRIMARY KEY,
                    value TEXT
                );
            ";
            cmd.ExecuteNonQuery();
        }

        private static string D(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        private static DateTime PD(string s) => DateTime.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        // --- Meta ---

        public string? GetMeta(string key)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM app_meta WHERE key = $k;";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        }

        public void SetMeta(string key, string value)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO app_meta (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v;";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }

        // --- Fristen ---

        public void UpsertFrist(FristEintrag f)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            var jetzt = DateTime.UtcNow.ToString("o");
            cmd.CommandText = @"
                INSERT INTO fristen (id, mandant_id, bezeichnung, fristart_id, rechtsgrundlage, ereignis, ende, vorfrist,
                                     notfrist, status, quelle_pfad, rechenweg, notiz, ereignis_angenommen, erstellt, geaendert)
                VALUES ($id, $m, $b, $fa, $rg, $er, $en, $vf, $nf, $st, $qp, $rw, $no, $ea, $jetzt, $jetzt)
                ON CONFLICT(id) DO UPDATE SET bezeichnung=$b, fristart_id=$fa, rechtsgrundlage=$rg, ereignis=$er, ende=$en,
                    vorfrist=$vf, notfrist=$nf, status=$st, rechenweg=$rw, notiz=$no, ereignis_angenommen=$ea, geaendert=$jetzt;
            ";
            cmd.Parameters.AddWithValue("$id", f.Id);
            cmd.Parameters.AddWithValue("$m", f.MandantId);
            cmd.Parameters.AddWithValue("$b", f.Bezeichnung);
            cmd.Parameters.AddWithValue("$fa", (object?)f.FristartId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rg", f.Rechtsgrundlage);
            cmd.Parameters.AddWithValue("$er", D(f.Ereignis));
            cmd.Parameters.AddWithValue("$en", D(f.Ende));
            cmd.Parameters.AddWithValue("$vf", D(f.Vorfrist));
            cmd.Parameters.AddWithValue("$nf", f.Notfrist ? 1 : 0);
            cmd.Parameters.AddWithValue("$st", f.Status);
            cmd.Parameters.AddWithValue("$qp", (object?)f.QuellePfad ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rw", f.Rechenweg);
            cmd.Parameters.AddWithValue("$no", f.Notiz);
            cmd.Parameters.AddWithValue("$ea", f.EreignisAngenommen ? 1 : 0);
            cmd.Parameters.AddWithValue("$jetzt", jetzt);
            cmd.ExecuteNonQuery();
        }

        public void SetFristStatus(string id, string status)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE fristen SET status = $s, geaendert = $g WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$s", status);
            cmd.Parameters.AddWithValue("$g", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        /// <summary>Fristen mit Status "vorschlag"/"offen" (ohne mandantId: alle Mandanten).</summary>
        public List<FristEintrag> GetFristen(string? mandantId = null, bool auchErledigte = false)
        {
            var result = new List<FristEintrag>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            var where = auchErledigte ? "status <> 'verworfen'" : "status IN ('vorschlag', 'offen')";
            if (mandantId is not null) where += " AND mandant_id = $m";
            cmd.CommandText = $@"
                SELECT id, mandant_id, bezeichnung, fristart_id, rechtsgrundlage, ereignis, ende, vorfrist, notfrist,
                       status, quelle_pfad, rechenweg, notiz, ereignis_angenommen
                FROM fristen WHERE {where} ORDER BY ende, bezeichnung;";
            if (mandantId is not null) cmd.Parameters.AddWithValue("$m", mandantId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                result.Add(new FristEintrag(
                    r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? "" : r.GetString(4), PD(r.GetString(5)), PD(r.GetString(6)), PD(r.GetString(7)),
                    r.GetInt32(8) == 1, r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10),
                    r.IsDBNull(11) ? "" : r.GetString(11), r.IsDBNull(12) ? "" : r.GetString(12), r.GetInt32(13) == 1));
            return result;
        }

        /// <summary>Gibt es zu dieser Quelle schon eine Frist mit dieser Bezeichnung (egal welcher Status)?
        /// Verhindert, dass eine verworfene Vorschlag-Frist bei jeder Neu-Auswertung wiederkommt.</summary>
        public bool FristExistiert(string quellePfad, string bezeichnung)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM fristen WHERE quelle_pfad = $p AND bezeichnung = $b LIMIT 1;";
            cmd.Parameters.AddWithValue("$p", quellePfad);
            cmd.Parameters.AddWithValue("$b", bezeichnung);
            return cmd.ExecuteScalar() is not null;
        }

        // --- Posteingang / Dokument-Auswertungen ---

        public bool HatAnalyse(string pfad)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM dokument_analyse WHERE pfad = $p;";
            cmd.Parameters.AddWithValue("$p", pfad);
            return cmd.ExecuteScalar() is not null;
        }

        public void UpsertAnalyse(string pfad, string mandantId, string titel, string json, DateTime eingang)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO dokument_analyse (pfad, mandant_id, titel, json, eingang, gelesen, erstellt)
                VALUES ($p, $m, $t, $j, $e, 0, $jetzt)
                ON CONFLICT(pfad) DO UPDATE SET json = $j, titel = $t;";
            cmd.Parameters.AddWithValue("$p", pfad);
            cmd.Parameters.AddWithValue("$m", mandantId);
            cmd.Parameters.AddWithValue("$t", titel);
            cmd.Parameters.AddWithValue("$j", json);
            cmd.Parameters.AddWithValue("$e", eingang.ToString("o"));
            cmd.Parameters.AddWithValue("$jetzt", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        public void SetEingangGelesen(string pfad, bool gelesen = true)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE dokument_analyse SET gelesen = $g WHERE pfad = $p;";
            cmd.Parameters.AddWithValue("$p", pfad);
            cmd.Parameters.AddWithValue("$g", gelesen ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        public void DeleteAnalyse(string pfad)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM dokument_analyse WHERE pfad = $p;";
            cmd.Parameters.AddWithValue("$p", pfad);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Auswertungen, neueste zuerst; optional nur eines Mandanten bzw. nur ungelesene.</summary>
        public List<EingangEintrag> GetEingaenge(string? mandantId = null, bool nurUngelesen = false, int limit = 200)
        {
            var result = new List<EingangEintrag>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            var where = new List<string>();
            if (mandantId is not null) where.Add("mandant_id = $m");
            if (nurUngelesen) where.Add("gelesen = 0");
            cmd.CommandText = "SELECT pfad, mandant_id, titel, json, eingang, gelesen FROM dokument_analyse" +
                              (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "") +
                              " ORDER BY eingang DESC LIMIT $limit;";
            if (mandantId is not null) cmd.Parameters.AddWithValue("$m", mandantId);
            cmd.Parameters.AddWithValue("$limit", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                result.Add(new EingangEintrag(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                    DateTime.Parse(r.GetString(4), null, DateTimeStyles.RoundtripKind), r.GetInt32(5) == 1));
            return result;
        }

        // --- Aktenstamm ---

        public AktenStamm GetAktenStamm(string mandantId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT gericht, aktenzeichen, gegner, gegnervertreter, notiz FROM akte_stamm WHERE mandant_id = $m;";
            cmd.Parameters.AddWithValue("$m", mandantId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return new AktenStamm(mandantId, "", "", "", "", "");
            string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
            return new AktenStamm(mandantId, S(0), S(1), S(2), S(3), S(4));
        }

        public void UpsertAktenStamm(AktenStamm s)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO akte_stamm (mandant_id, gericht, aktenzeichen, gegner, gegnervertreter, notiz, geaendert)
                VALUES ($m, $g, $az, $ge, $gv, $n, $jetzt)
                ON CONFLICT(mandant_id) DO UPDATE SET gericht=$g, aktenzeichen=$az, gegner=$ge, gegnervertreter=$gv,
                    notiz=$n, geaendert=$jetzt;";
            cmd.Parameters.AddWithValue("$m", s.MandantId);
            cmd.Parameters.AddWithValue("$g", s.Gericht);
            cmd.Parameters.AddWithValue("$az", s.Aktenzeichen);
            cmd.Parameters.AddWithValue("$ge", s.Gegner);
            cmd.Parameters.AddWithValue("$gv", s.Gegnervertreter);
            cmd.Parameters.AddWithValue("$n", s.Notiz);
            cmd.Parameters.AddWithValue("$jetzt", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        public List<AktenStamm> GetAlleAktenStaemme()
        {
            var result = new List<AktenStamm>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT mandant_id, gericht, aktenzeichen, gegner, gegnervertreter, notiz FROM akte_stamm;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
                result.Add(new AktenStamm(S(0), S(1), S(2), S(3), S(4), S(5)));
            }
            return result;
        }

        // --- Postfach ---

        public Dictionary<string, string> GetMailAbsender()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT adresse, mandant_id FROM mail_absender;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) result[r.GetString(0)] = r.GetString(1);
            return result;
        }

        public void SetMailAbsender(string adresse, string mandantId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO mail_absender (adresse, mandant_id) VALUES ($a, $m) ON CONFLICT(adresse) DO UPDATE SET mandant_id = $m;";
            cmd.Parameters.AddWithValue("$a", adresse.Trim().ToLowerInvariant());
            cmd.Parameters.AddWithValue("$m", mandantId);
            cmd.ExecuteNonQuery();
        }

        public void AddUnzugeordnet(string pfad, string absender, string absenderName, string betreff, DateTime datum, string grund)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO postfach_unzugeordnet (pfad, absender, absender_name, betreff, datum, grund)
                VALUES ($p, $a, $n, $b, $d, $g) ON CONFLICT(pfad) DO NOTHING;";
            cmd.Parameters.AddWithValue("$p", pfad);
            cmd.Parameters.AddWithValue("$a", absender);
            cmd.Parameters.AddWithValue("$n", absenderName);
            cmd.Parameters.AddWithValue("$b", betreff);
            cmd.Parameters.AddWithValue("$d", datum.ToString("o"));
            cmd.Parameters.AddWithValue("$g", grund);
            cmd.ExecuteNonQuery();
        }

        public List<(string Pfad, string Absender, string AbsenderName, string Betreff, DateTime Datum, string Grund)> GetUnzugeordnet()
        {
            var result = new List<(string, string, string, string, DateTime, string)>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pfad, absender, absender_name, betreff, datum, grund FROM postfach_unzugeordnet ORDER BY datum DESC;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                result.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                    DateTime.Parse(r.GetString(4), null, DateTimeStyles.RoundtripKind), r.GetString(5)));
            return result;
        }

        public void DeleteUnzugeordnet(string pfad)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM postfach_unzugeordnet WHERE pfad = $p;";
            cmd.Parameters.AddWithValue("$p", pfad);
            cmd.ExecuteNonQuery();
        }

        // --- Fragebogen ---

        // --- Mandant löschen ---

        // Alle Tabellen mit Mandantenbezug. Neue Tabellen mit mandant_id hier eintragen
        // (ein Test prüft, dass nach dem Löschen nichts übrig bleibt).
        private static readonly string[] MandantTabellen =
        {
            "dokumente", "chat_history", "chat_zusammenfassungen", "notizen", "mandant_zusammenfassung",
            "fristen", "dokument_analyse", "akte_stamm", "fragebogen", "mail_absender", "formular_feld",
        };
        private static readonly string[] MandantMetaPraefixe = { "mandat_aufnahme:", "formular_ki_stand:" };

        public sealed record MandantUmfang(int Dokumente, int FristenOffen, int Fristen, int ChatNachrichten, int Notizen);

        /// <summary>Was zu einem Mandanten gespeichert ist — für die Sicherheitsabfrage vor dem Löschen.</summary>
        public MandantUmfang GetMandantUmfang(string mandantId)
        {
            using var conn = GetConnection();
            int Zahl(string sql)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("$m", mandantId);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            return new MandantUmfang(
                Zahl("SELECT COUNT(DISTINCT pfad) FROM dokumente WHERE mandant_id = $m;"),
                Zahl("SELECT COUNT(*) FROM fristen WHERE mandant_id = $m AND status IN ('offen', 'vorschlag');"),
                Zahl("SELECT COUNT(*) FROM fristen WHERE mandant_id = $m;"),
                Zahl("SELECT COUNT(*) FROM chat_history WHERE mandant_id = $m;"),
                Zahl("SELECT COUNT(*) FROM notizen WHERE mandant_id = $m;"));
        }

        /// <summary>Löscht den Mandanten und alles, was LexWolf zu ihm gespeichert hat
        /// (Fristen, Chat, Notizen, Auswertungen, Fragebogen …) in einem Schritt.</summary>
        public void DeleteMandantKomplett(string mandantId)
        {
            using var conn = GetConnection();
            using var tx = conn.BeginTransaction();
            void Ausfuehren(string sql, string wert)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("$m", wert);
                cmd.ExecuteNonQuery();
            }
            foreach (var t in MandantTabellen)
                Ausfuehren($"DELETE FROM {t} WHERE mandant_id = $m;", mandantId);
            foreach (var p in MandantMetaPraefixe)
                Ausfuehren("DELETE FROM app_meta WHERE key = $m;", p + mandantId);
            Ausfuehren("DELETE FROM mandanten WHERE id = $m;", mandantId);
            tx.Commit();
        }

        // --- Formularfelder ---

        public Dictionary<string, Services.FeldWert> GetFormularFelder(string mandantId, string formular)
        {
            var result = new Dictionary<string, Services.FeldWert>(StringComparer.OrdinalIgnoreCase);
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT feld, ki_wert, ki_quelle, anwalt_wert FROM formular_feld WHERE mandant_id = $m AND formular = $f;";
            cmd.Parameters.AddWithValue("$m", mandantId);
            cmd.Parameters.AddWithValue("$f", formular);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                result[r.GetString(0)] = new Services.FeldWert
                {
                    Ki = r.IsDBNull(1) ? null : r.GetString(1),
                    KiQuelle = r.IsDBNull(2) ? null : r.GetString(2),
                    Anwalt = r.IsDBNull(3) ? null : r.GetString(3),
                };
            return result;
        }

        public void SaveFormularFelder(string mandantId, string formular, IReadOnlyDictionary<string, Services.FeldWert> felder)
        {
            using var conn = GetConnection();
            using var tx = conn.BeginTransaction();
            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM formular_feld WHERE mandant_id = $m AND formular = $f;";
                del.Parameters.AddWithValue("$m", mandantId);
                del.Parameters.AddWithValue("$f", formular);
                del.ExecuteNonQuery();
            }
            var jetzt = DateTime.UtcNow.ToString("o");
            foreach (var (feld, w) in felder)
            {
                if (w.Ki is null && w.Anwalt is null) continue;
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO formular_feld (mandant_id, formular, feld, ki_wert, ki_quelle, anwalt_wert, geaendert)
                                    VALUES ($m, $f, $feld, $ki, $q, $a, $g);";
                cmd.Parameters.AddWithValue("$m", mandantId);
                cmd.Parameters.AddWithValue("$f", formular);
                cmd.Parameters.AddWithValue("$feld", feld);
                cmd.Parameters.AddWithValue("$ki", (object?)w.Ki ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$q", (object?)w.KiQuelle ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$a", (object?)w.Anwalt ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$g", jetzt);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        public string? GetFragebogen(string mandantId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT json FROM fragebogen WHERE mandant_id = $m;";
            cmd.Parameters.AddWithValue("$m", mandantId);
            return cmd.ExecuteScalar() as string;
        }

        public void SaveFragebogen(string mandantId, string json)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO fragebogen (mandant_id, json, geaendert) VALUES ($m, $j, $g)
                ON CONFLICT(mandant_id) DO UPDATE SET json = $j, geaendert = $g;";
            cmd.Parameters.AddWithValue("$m", mandantId);
            cmd.Parameters.AddWithValue("$j", json);
            cmd.Parameters.AddWithValue("$g", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }
    }
}
