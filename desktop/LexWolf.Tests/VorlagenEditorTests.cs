using System;
using System.IO;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests;

public sealed class VorlagenEditorTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "lexwolf-vorlage-editor-" + Guid.NewGuid().ToString("N"));

    public VorlagenEditorTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    [Theory]
    [InlineData(".docx")]
    [InlineData(".txt")]
    public void Bearbeitbare_Endungen_werden_erkannt(string endung) =>
        Assert.True(VorlagenEditor.IstBearbeitbar(Path.Combine(_tmp, "Vorlage" + endung)));

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".doc")]
    [InlineData("")]
    public void Andere_Endungen_sind_nicht_bearbeitbar(string endung) =>
        Assert.False(VorlagenEditor.IstBearbeitbar(Path.Combine(_tmp, "Vorlage" + endung)));

    [Fact]
    public void Txt_Datei_wird_gelesen_und_nach_dem_Speichern_unveraendert_zurueckgelesen()
    {
        var pfad = Path.Combine(_tmp, "Vorlage.txt");
        File.WriteAllText(pfad, "Ursprünglicher Inhalt");

        Assert.Equal("Ursprünglicher Inhalt", VorlagenEditor.LeseText(pfad));

        VorlagenEditor.SchreibeText(pfad, "Geänderter Inhalt\nZweite Zeile");

        Assert.Equal("Geänderter Inhalt\nZweite Zeile", VorlagenEditor.LeseText(pfad).Replace("\r\n", "\n"));
    }

    [Fact]
    public void Docx_Datei_wird_geschrieben_und_mit_Absaetzen_zurueckgelesen()
    {
        var pfad = Path.Combine(_tmp, "Vorlage.docx");

        VorlagenEditor.SchreibeText(pfad, "Sehr geehrte Damen und Herren,\n\nim Namen meines Mandanten ...");

        Assert.True(File.Exists(pfad));
        var gelesen = VorlagenEditor.LeseText(pfad);
        Assert.Contains("Sehr geehrte Damen und Herren,", gelesen);
        Assert.Contains("im Namen meines Mandanten ...", gelesen);
        // Jede Zeile wurde als eigener Absatz geschrieben -> beim Zurücklesen wieder als
        // eigene Zeile erkennbar (Reihenfolge bleibt erhalten).
        var zeilen = gelesen.Split('\n');
        Assert.Equal("Sehr geehrte Damen und Herren,", zeilen[0]);
        Assert.Equal("im Namen meines Mandanten ...", zeilen[2]);
    }

    [Fact]
    public void Docx_Datei_kann_mehrfach_ueberschrieben_werden()
    {
        var pfad = Path.Combine(_tmp, "Vorlage.docx");

        VorlagenEditor.SchreibeText(pfad, "Erster Stand");
        VorlagenEditor.SchreibeText(pfad, "Zweiter Stand, überarbeitet");

        var gelesen = VorlagenEditor.LeseText(pfad);
        Assert.Contains("Zweiter Stand, überarbeitet", gelesen);
        Assert.DoesNotContain("Erster Stand", gelesen);
    }
}
