using System;
using System.Collections.Generic;
using System.IO;
using LexWolf.Database;
using LexWolf.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LexWolf.Tests;

public sealed class MandantLoeschenTests : IDisposable
{
    private readonly string _pfad = Path.Combine(Path.GetTempPath(), $"lexwolf-loeschen-{Guid.NewGuid():N}.db");
    private readonly LocalDb _db;

    public MandantLoeschenTests() => _db = new LocalDb(_pfad);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_pfad); } catch { }
    }

    private void Befuellen(string id)
    {
        _db.UpsertMandant(id, $"Mandant {id}", id);
        _db.UpsertDokumentChunk($"{id}-d1", id, $@"C:\Akten\{id}\Urteil.pdf", "Urteil", "Text", DateTime.UtcNow.ToString("o"));
        _db.AddChatMessage(id, "user", "Hallo");
        _db.InsertNotiz($"{id}-n1", id, "Telefonat", "Notiz");
        _db.UpsertMandantZusammenfassung(id, "Zusammenfassung", "hash");
        _db.UpsertFrist(new FristEintrag($"{id}-f1", id, "Berufung", "berufung", "§ 517 ZPO", DateTime.Today, DateTime.Today.AddDays(30),
            DateTime.Today.AddDays(23), true, "offen", null, "", "", false));
        _db.UpsertAnalyse($@"C:\Akten\{id}\Urteil.pdf", id, "Urteil", "{}", DateTime.UtcNow);
        _db.UpsertAktenStamm(new AktenStamm(id, "AG Köln", "1 F 1/26", "Gegner", "", ""));
        _db.SaveFragebogen(id, "{}");
        _db.SetMailAbsender($"gegner-{id}@example.de", id);
        _db.SaveFormularFelder(id, "fragebogen", new Dictionary<string, FeldWert> { ["netto_mandant"] = new() { Ki = "1" } });
        _db.SetMeta(MandatAufnahme.MetaSchluessel(id), "{}");
    }

    [Fact]
    public void Umfang_zeigt_was_geloescht_wuerde()
    {
        Befuellen("m1");
        var u = _db.GetMandantUmfang("m1");
        Assert.Equal(1, u.Dokumente);
        Assert.Equal(1, u.FristenOffen);
        Assert.Equal(1, u.ChatNachrichten);
        Assert.Equal(1, u.Notizen);
    }

    [Fact]
    public void Loeschen_entfernt_alles_zum_Mandanten_und_nichts_von_anderen()
    {
        Befuellen("m1");
        Befuellen("m2");

        _db.DeleteMandantKomplett("m1");

        Assert.Equal(new LocalDb.MandantUmfang(0, 0, 0, 0, 0), _db.GetMandantUmfang("m1"));
        Assert.DoesNotContain(_db.GetMandanten(), m => m.Id == "m1");
        Assert.Null(_db.GetFragebogen("m1"));
        Assert.Empty(_db.GetFormularFelder("m1", "fragebogen"));
        Assert.Null(_db.GetMeta(MandatAufnahme.MetaSchluessel("m1")));

        // Keine Tabelle mit mandant_id darf noch Zeilen von m1 haben
        using var conn = new SqliteConnection($"Data Source={_pfad}");
        conn.Open();
        using var tabellen = conn.CreateCommand();
        tabellen.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
        var namen = new List<string>();
        using (var r = tabellen.ExecuteReader()) while (r.Read()) namen.Add(r.GetString(0));
        foreach (var t in namen)
        {
            using var spalten = conn.CreateCommand();
            spalten.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{t}') WHERE name = 'mandant_id';";
            if (Convert.ToInt32(spalten.ExecuteScalar()) == 0) continue;
            using var zaehlen = conn.CreateCommand();
            zaehlen.CommandText = $"SELECT COUNT(*) FROM {t} WHERE mandant_id = 'm1';";
            Assert.True(Convert.ToInt32(zaehlen.ExecuteScalar()) == 0, $"Tabelle {t} enthält noch Daten des gelöschten Mandanten");
        }

        // Der andere Mandant ist unberührt
        Assert.Equal(1, _db.GetMandantUmfang("m2").Dokumente);
        Assert.Contains(_db.GetMandanten(), m => m.Id == "m2");
    }
}
