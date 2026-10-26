using System;
using System.IO;
using System.Linq;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests;

public sealed class VorlagenOrdnerTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "lexwolf-vorlagen-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    [Fact]
    public void Legt_Vorlagenordner_und_Unterordner_pro_Schriftsatz_Art_an()
    {
        var vorlagenOrdner = VorlagenOrdner.SicherstellenUnterordner(_tmp);

        Assert.Equal(Path.Combine(_tmp, "Vorlagen"), vorlagenOrdner);
        Assert.True(Directory.Exists(vorlagenOrdner));
        foreach (var art in VorlagenOrdner.Arten)
        {
            var erwartet = Path.Combine(vorlagenOrdner, VorlagenOrdner.OrdnerName(art));
            Assert.True(Directory.Exists(erwartet), $"Unterordner fehlt: {erwartet}");
        }
    }

    [Fact]
    public void Bestehende_Root_Vorlagen_bleiben_unangetastet()
    {
        var vorlagenOrdner = Path.Combine(_tmp, "Vorlagen");
        Directory.CreateDirectory(vorlagenOrdner);
        var bestehendeDatei = Path.Combine(vorlagenOrdner, "Mein Muster.docx");
        File.WriteAllText(bestehendeDatei, "Inhalt");

        VorlagenOrdner.SicherstellenUnterordner(_tmp);

        Assert.True(File.Exists(bestehendeDatei));
        Assert.Equal("Inhalt", File.ReadAllText(bestehendeDatei));
        // Datei liegt weiterhin direkt im Wurzelordner, nicht in einem Unterordner verschoben.
        Assert.Contains(bestehendeDatei, Directory.GetFiles(vorlagenOrdner, "*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void Mehrfacher_Aufruf_ist_unschaedlich_und_erzeugt_keine_Duplikate()
    {
        VorlagenOrdner.SicherstellenUnterordner(_tmp);
        VorlagenOrdner.SicherstellenUnterordner(_tmp);

        var vorlagenOrdner = Path.Combine(_tmp, "Vorlagen");
        var unterordner = Directory.GetDirectories(vorlagenOrdner).Select(Path.GetFileName).ToList();
        Assert.Equal(VorlagenOrdner.Arten.Length, unterordner.Distinct().Count());
    }

    [Fact]
    public void Schraegstrich_in_Schriftsatz_Art_wird_zu_gueltigem_Ordnernamen()
    {
        var name = VorlagenOrdner.OrdnerName("Gutachten / Erfolgsaussichten");
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
        Assert.True(name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }

    // Task #307: "Als Vorlage speichern" an der Entwurfskarte.

    [Fact]
    public void VorlagenDateiPfad_Liegt_Im_Typ_Unterordner()
    {
        var pfad = VorlagenOrdner.VorlagenDateiPfad(_tmp, "Klageschrift", "Mein Muster");

        Assert.Equal(
            Path.Combine(_tmp, "Vorlagen", "Klageschrift", "Mein Muster.txt"),
            pfad);
        // Typ-Unterordner wird bei Bedarf angelegt, damit direkt geschrieben werden kann.
        Assert.True(Directory.Exists(Path.Combine(_tmp, "Vorlagen", "Klageschrift")));
    }

    [Fact]
    public void PruefeVorlagenName_Leerer_Name_Ist_Ungueltig()
    {
        var fehler = VorlagenOrdner.PruefeVorlagenName("   ", _tmp, "Klageschrift");
        Assert.NotNull(fehler);
    }

    [Fact]
    public void PruefeVorlagenName_Neuer_Name_Ist_Gueltig()
    {
        var fehler = VorlagenOrdner.PruefeVorlagenName("Neue Vorlage", _tmp, "Klageschrift");
        Assert.Null(fehler);
    }

    [Fact]
    public void PruefeVorlagenName_Erkennt_Kollision_Im_Typ_Unterordner()
    {
        var pfad = VorlagenOrdner.VorlagenDateiPfad(_tmp, "Klageschrift", "Bestehend");
        File.WriteAllText(pfad, "Inhalt");

        var fehler = VorlagenOrdner.PruefeVorlagenName("Bestehend", _tmp, "Klageschrift");
        Assert.NotNull(fehler);
    }

    [Fact]
    public void PruefeVorlagenName_Gleicher_Name_In_Anderem_Typ_Ist_Keine_Kollision()
    {
        var pfad = VorlagenOrdner.VorlagenDateiPfad(_tmp, "Klageschrift", "Bestehend");
        File.WriteAllText(pfad, "Inhalt");

        var fehler = VorlagenOrdner.PruefeVorlagenName("Bestehend", _tmp, "Berufungsbegründung");
        Assert.Null(fehler);
    }

    [Fact]
    public void AlsVorlageSpeichern_End_To_End_Schreibt_Datei_Im_Typ_Ordner_Und_Setzt_Letzte_Vorlage()
    {
        // Simuliert MainWindow.KarteAlsVorlageSpeichern (Task #307): Entwurfstext in
        // Vorlagen/<Typ>/<Name>.txt schreiben und sofort als "zuletzt gewählt" setzen.
        var dbPath = Path.Combine(Path.GetTempPath(), $"lexwolf_vorlage_e2e_{Guid.NewGuid():N}.db");
        try
        {
            var db = new LexWolf.Database.LocalDb(dbPath);
            var entwurfText = "Sehr geehrte Damen und Herren,\n\nhiermit ...";

            var pfad = VorlagenOrdner.VorlagenDateiPfad(_tmp, "Klageschrift", "Standardmuster");
            File.WriteAllText(pfad, entwurfText);
            db.UpsertLetzteVorlage("Klageschrift", pfad);

            Assert.True(File.Exists(pfad));
            Assert.Equal(Path.Combine(_tmp, "Vorlagen", "Klageschrift", "Standardmuster.txt"), pfad);
            Assert.Equal(entwurfText, File.ReadAllText(pfad));

            var letzte = db.GetLetzteVorlage("Klageschrift");
            Assert.NotNull(letzte);
            Assert.Equal(pfad, letzte!.Value.VorlagePfad);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
