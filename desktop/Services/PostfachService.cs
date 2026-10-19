using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LexWolf.Database;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace LexWolf.Services;

/// <summary>
/// Ruft das Kanzlei-Postfach per IMAP ab (nur lesend — in der Mailbox wird nichts
/// gelöscht oder als gelesen markiert), ordnet neue Mails einem Mandanten zu
/// (<see cref="MailZuordnung"/>) und legt sie samt Anhängen im Mandantenordner unter
/// "E-Mails" ab. Von dort übernimmt der normale Weg: DokumentScanner → AkteService →
/// Posteingang, Fristvorschläge. Nicht zuordenbare Mails warten in einem eigenen
/// Ordner, bis der Anwalt sie zuordnet; der Absender wird dabei für später gelernt.
/// </summary>
public sealed class PostfachService
{
    private const string MetaUidValidity = "imap_uidvalidity";
    private const string MetaLetzteUid = "imap_letzte_uid";
    private const string MetaLetzterAbruf = "imap_letzter_abruf";
    private const int ErsterAbrufTage = 7;

    private readonly LocalDb _db;
    private readonly Func<AppSettings> _settings;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private CancellationTokenSource? _cts;

    public static string UnzugeordnetOrdner => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LexWolf", "Posteingang");

    /// <summary>Nach jedem Abruf: (zugeordnet, nicht zugeordnet, Fehlermeldung oder null). Beliebiger Thread.</summary>
    public event Action<int, int, string?>? Abgerufen;

    public string? LetzterFehler { get; private set; }

    public DateTime? LetzterAbruf =>
        DateTime.TryParse(_db.GetMeta(MetaLetzterAbruf), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;

    public PostfachService(LocalDb db, Func<AppSettings> settings)
    {
        _db = db;
        _settings = settings;
    }

    // ------------------------------------------------------------ Passwort (DPAPI)

    public static string Schuetzen(string passwort) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(passwort), null, DataProtectionScope.CurrentUser));

    public static string Entschuetzen(string geschuetzt)
    {
        if (string.IsNullOrEmpty(geschuetzt)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(geschuetzt), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }

    // ------------------------------------------------------------ Abruf

    /// <summary>Startet (bzw. startet neu) den regelmäßigen Abruf.</summary>
    public void Starten()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var s = _settings();
                if (s.PostfachAktiv && s.PostfachServer.Length > 0)
                    await AbrufenAsync(token).ConfigureAwait(false);
                try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, s.PostfachAbrufMinuten)), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    public static async Task<string?> TestenAsync(string server, int port, bool ssl, string benutzer, string passwort, string ordner)
    {
        try
        {
            using var client = new ImapClient { Timeout = 20000 };
            await client.ConnectAsync(server, port, ssl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable);
            await client.AuthenticateAsync(benutzer, passwort);
            var folder = await client.GetFolderAsync(ordner);
            await folder.OpenAsync(FolderAccess.ReadOnly);
            await client.DisconnectAsync(true);
            return null;
        }
        catch (Exception ex)
        {
            return ex switch
            {
                AuthenticationException => "Anmeldung abgelehnt — Benutzername oder Passwort falsch (bei Microsoft 365/Gmail ggf. App-Passwort nötig).",
                FolderNotFoundException => $"Ordner „{ordner}“ nicht gefunden.",
                System.Net.Sockets.SocketException => "Server nicht erreichbar — Adresse und Port prüfen.",
                _ => ex.Message,
            };
        }
    }

    public async Task AbrufenAsync(CancellationToken ct = default)
    {
        if (!await _lock.WaitAsync(0, ct).ConfigureAwait(false)) return; // läuft schon
        int zugeordnet = 0, offen = 0;
        try
        {
            var s = _settings();
            using var client = new ImapClient { Timeout = 60000 };
            await client.ConnectAsync(s.PostfachServer, s.PostfachPort,
                s.PostfachSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, ct).ConfigureAwait(false);
            await client.AuthenticateAsync(s.PostfachBenutzer, Entschuetzen(s.PostfachPasswortGeschuetzt), ct).ConfigureAwait(false);
            var folder = await client.GetFolderAsync(s.PostfachOrdner, ct).ConfigureAwait(false);
            await folder.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);

            // UIDs gelten nur innerhalb derselben UIDVALIDITY — ändert sie sich, neu ab Datum.
            var validity = folder.UidValidity.ToString();
            uint.TryParse(_db.GetMeta(MetaLetzteUid), out var letzteUid);
            if (_db.GetMeta(MetaUidValidity) != validity) letzteUid = 0;

            SearchQuery query = letzteUid > 0
                ? SearchQuery.Uids(new UniqueIdRange(new UniqueId(letzteUid + 1), UniqueId.MaxValue))
                : SearchQuery.DeliveredAfter(DateTime.Today.AddDays(-ErsterAbrufTage));
            var uids = (await folder.SearchAsync(query, ct).ConfigureAwait(false))
                .Where(u => u.Id > letzteUid).OrderBy(u => u.Id).ToList();

            var mandanten = Kandidaten();
            var bekannt = _db.GetMailAbsender();
            foreach (var uid in uids)
            {
                ct.ThrowIfCancellationRequested();
                var m = await folder.GetMessageAsync(uid, ct).ConfigureAwait(false);
                if (Ablegen(m, mandanten, bekannt)) zugeordnet++; else offen++;
                _db.SetMeta(MetaUidValidity, validity);
                _db.SetMeta(MetaLetzteUid, uid.Id.ToString());
            }
            await client.DisconnectAsync(true, ct).ConfigureAwait(false);
            LetzterFehler = null;
            _db.SetMeta(MetaLetzterAbruf, DateTime.Now.ToString("o"));
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            LetzterFehler = ex is AuthenticationException ? "Anmeldung am Postfach abgelehnt" : ex.Message;
        }
        finally
        {
            _lock.Release();
        }
        Abgerufen?.Invoke(zugeordnet, offen, LetzterFehler);
    }

    private System.Collections.Generic.List<MandantKandidat> Kandidaten()
    {
        var staemme = _db.GetAlleAktenStaemme().ToDictionary(x => x.MandantId);
        return _db.GetMandanten().Select(m =>
        {
            staemme.TryGetValue(m.Id, out var st);
            return new MandantKandidat(m.Id, m.Name, st?.Aktenzeichen ?? "", st?.Gegner ?? "", st?.Gegnervertreter ?? "");
        }).ToList();
    }

    /// <summary>true = einem Mandanten zugeordnet und dort abgelegt.</summary>
    private bool Ablegen(MimeMessage m, System.Collections.Generic.List<MandantKandidat> mandanten,
        System.Collections.Generic.Dictionary<string, string> bekannt)
    {
        var absender = m.From.Mailboxes.FirstOrDefault();
        var z = MailZuordnung.Finde(absender?.Address ?? "", absender?.Name ?? "", m.Subject ?? "",
            MailText.Koerper(m), mandanten, bekannt);

        if (z.MandantId is { } id && mandanten.FirstOrDefault(x => x.Id == id) is { } mandant)
        {
            MailText.Speichern(m, MandantOrdner(mandant.Name));
            return true;
        }

        var pfad = MailText.Speichern(m, UnzugeordnetOrdner);
        _db.AddUnzugeordnet(pfad, absender?.Address ?? "", absender?.Name ?? "", m.Subject ?? "", m.Date.LocalDateTime, z.Grund);
        return false;
    }

    private string MandantOrdner(string mandantName) =>
        Path.Combine(_settings().DokumentePfad, mandantName, "E-Mails");

    // ------------------------------------------------------------ Manuelle Zuordnung

    /// <summary>Verschiebt Mail + Anhänge in die Akte und merkt sich den Absender.</summary>
    public void Zuordnen(string emlPfad, string mandantId, string mandantName, string absenderAdresse)
    {
        var ziel = MandantOrdner(mandantName);
        Directory.CreateDirectory(ziel);
        var basis = Path.GetFileNameWithoutExtension(emlPfad);
        // Anhänge zuerst (gleicher Dateianfang), damit sie beim Einlesen der Mail schon da sind.
        foreach (var datei in Directory.EnumerateFiles(Path.GetDirectoryName(emlPfad)!, basis + " - *"))
            File.Move(datei, DokumentExport.FreierPfad(ziel, Path.GetFileName(datei)));
        if (File.Exists(emlPfad))
            File.Move(emlPfad, DokumentExport.FreierPfad(ziel, Path.GetFileName(emlPfad)));
        _db.DeleteUnzugeordnet(emlPfad);
        if (absenderAdresse.Length > 0) _db.SetMailAbsender(absenderAdresse, mandantId);
    }

    /// <summary>Keine Kanzleipost (Newsletter …): lokale Kopie verwerfen, die Mail bleibt im Postfach.</summary>
    public void Verwerfen(string emlPfad)
    {
        var basis = Path.GetFileNameWithoutExtension(emlPfad);
        var ordner = Path.GetDirectoryName(emlPfad)!;
        if (Directory.Exists(ordner))
            foreach (var datei in Directory.EnumerateFiles(ordner, basis + " - *"))
                File.Delete(datei);
        if (File.Exists(emlPfad)) File.Delete(emlPfad);
        _db.DeleteUnzugeordnet(emlPfad);
    }
}
