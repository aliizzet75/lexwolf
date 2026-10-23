using System;
using System.IO;
using LexWolf.Database;
using LexWolf.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LexWolf.Tests;

/// <summary>
/// Task #277: Ein per SchriftsatzDialog erzeugtes Gutachten wird wie jeder andere Schriftsatz
/// in der dokumente-Tabelle der lokalen Mandanten-Akte abgelegt, mit Typ-Feld "Gutachten" —
/// kein neues Datenmodell, nur eine zusätzliche Spalte auf der bestehenden Tabelle.
/// </summary>
public class DokumentTypTests
{
    [Theory]
    [InlineData("LexWolf_Gutachten_2026-10-23.docx")]
    [InlineData(@"C:\Akten\Mustermann\LexWolf_Gutachten_2026-10-23.docx")]
    [InlineData("lexwolf_gutachten_2026-10-23.docx")] // Groß-/Kleinschreibung darf keine Rolle spielen
    [InlineData("LexWolf_Gutachten_2026-10-23 (2).docx")] // zweite Fassung am selben Tag (FreierPfad)
    public void TypAusDateiname_ErkenntGutachten(string dateiname)
    {
        Assert.Equal("Gutachten", DokumentExport.TypAusDateiname(dateiname));
    }

    [Theory]
    [InlineData("LexWolf_Klageerwiderung_2026-10-23.docx")] // anderer Schriftsatz-Typ
    [InlineData("Urteil.pdf")] // gescanntes Dokument aus der Akte, kein Entwurf
    [InlineData("E-Mail-Entwurf_2026-10-23_120000.eml")]
    [InlineData("")]
    public void TypAusDateiname_LiefertLeerenString_FuerAndereDateien(string dateiname)
    {
        Assert.Equal("", DokumentExport.TypAusDateiname(dateiname));
    }
}

/// <summary>Speichern und Lesen des Dokumenttyps über die lokale SQLite-Akte (echte Verbindung,
/// kein Mock) — inklusive Migration einer schon vorhandenen Datenbank ohne "typ"-Spalte.</summary>
public sealed class DokumentTypLocalDbTests : IDisposable
{
    private readonly string _pfad = Path.Combine(Path.GetTempPath(), $"lexwolf-dok-typ-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_pfad); } catch { }
    }

    [Fact]
    public void Gutachten_WirdMitTypGespeichert_UndWiederGelesen()
    {
        var db = new LocalDb(_pfad);
        db.UpsertMandant("m1", "Mandant Eins", "1");
        var pfad = @"C:\Akten\Mandant Eins\LexWolf_Gutachten_2026-10-23.docx";
        var typ = DokumentExport.TypAusDateiname(pfad);

        db.UpsertDokumentChunk("m1-gutachten-0", "m1", pfad, "LexWolf_Gutachten_2026-10-23", "Gutachtentext …", DateTime.UtcNow.ToString("o"), typ);

        Assert.Equal("Gutachten", db.GetDokumentTyp(pfad));
    }

    [Fact]
    public void AndereSchriftsaetze_BleibenOhneTyp()
    {
        var db = new LocalDb(_pfad);
        db.UpsertMandant("m1", "Mandant Eins", "1");
        var pfad = @"C:\Akten\Mandant Eins\LexWolf_Klageerwiderung_2026-10-23.docx";

        db.UpsertDokumentChunk("m1-ke-0", "m1", pfad, "LexWolf_Klageerwiderung_2026-10-23", "Text …", DateTime.UtcNow.ToString("o"),
            DokumentExport.TypAusDateiname(pfad));

        Assert.Equal("", db.GetDokumentTyp(pfad));
    }

    [Fact]
    public void AufrufOhneTypParameter_BleibtAbwaertskompatibel()
    {
        // Bestehende Aufrufer (z. B. der Scan gewöhnlicher Posteingangsdokumente) übergeben
        // weiterhin keinen Typ — das darf nicht brechen (Standardwert "").
        var db = new LocalDb(_pfad);
        db.UpsertMandant("m1", "Mandant Eins", "1");
        var pfad = @"C:\Akten\Mandant Eins\Urteil.pdf";

        db.UpsertDokumentChunk("m1-urteil-0", "m1", pfad, "Urteil", "Text …", DateTime.UtcNow.ToString("o"));

        Assert.Equal("", db.GetDokumentTyp(pfad));
    }

    [Fact]
    public void BestehendeDatenbankOhneTypSpalte_WirdBeimOeffnenMigriert()
    {
        // Simuliert eine Datenbank aus der Zeit vor Task #277: dokumente-Tabelle ohne "typ".
        using (var conn = new SqliteConnection($"Data Source={_pfad};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE dokumente (
                    id           TEXT PRIMARY KEY,
                    mandant_id   TEXT,
                    pfad         TEXT,
                    titel        TEXT,
                    inhalt_chunk TEXT,
                    erstellt     TEXT,
                    geaendert    TEXT
                );
                INSERT INTO dokumente (id, mandant_id, pfad, titel, inhalt_chunk, erstellt, geaendert)
                VALUES ('alt-0', 'm1', 'C:\Akten\Mandant Eins\Altbestand.pdf', 'Altbestand', 'Text', 'x', 'x');
            ";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        // LocalDb-Konstruktor muss die fehlende Spalte per ALTER TABLE nachziehen, ohne die
        // vorhandene Zeile zu verlieren, und ab da normal lesen/schreiben können.
        var db = new LocalDb(_pfad);
        Assert.Equal("", db.GetDokumentTyp(@"C:\Akten\Mandant Eins\Altbestand.pdf"));

        var pfad = @"C:\Akten\Mandant Eins\LexWolf_Gutachten_2026-10-23.docx";
        db.UpsertDokumentChunk("m1-gutachten-0", "m1", pfad, "LexWolf_Gutachten_2026-10-23", "Text", DateTime.UtcNow.ToString("o"), "Gutachten");
        Assert.Equal("Gutachten", db.GetDokumentTyp(pfad));
    }
}
