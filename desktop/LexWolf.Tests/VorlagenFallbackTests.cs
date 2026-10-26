using System;
using System.IO;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests;

/// <summary>Task #305: Lade-Fallback-Kette für die automatisch vorgeschlagene Vorlage beim
/// Erstellen eines Schriftsatzes vom Typ X: 1) zuletzt gewählte Vorlage (falls Datei noch
/// existiert) -> 2) erste Datei (alphabetisch) im Typ-Unterordner -> 3) erste Datei
/// (alphabetisch) im übergeordneten Vorlagen-Wurzelordner -> 4) kein Vorlagen-Kontext.</summary>
public sealed class VorlagenFallbackTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "lexwolf-vorlagen-fallback-" + Guid.NewGuid().ToString("N"));
    private const string Typ = "Klageschrift";

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    private string VorlagenOrdnerPfad => Path.Combine(_tmp, VorlagenOrdner.Ordnername);
    private string TypOrdnerPfad => Path.Combine(VorlagenOrdnerPfad, VorlagenOrdner.OrdnerName(Typ));

    [Fact]
    public void Stufe1_ZuletztGewaehlteVorlageWirdGenommenWennDateiExistiert()
    {
        Directory.CreateDirectory(TypOrdnerPfad);
        var gewaehlteDatei = Path.Combine(_tmp, "irgendwo", "meine-vorlage.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(gewaehlteDatei)!);
        File.WriteAllText(gewaehlteDatei, "Inhalt der gewählten Vorlage");
        // Köder im Typ-Ordner, der NICHT genommen werden darf, solange Stufe 1 greift.
        File.WriteAllText(Path.Combine(TypOrdnerPfad, "a-koeder.docx"), "Köder");

        var pfad = VorlagenOrdner.FindeVorlagenPfad(_tmp, Typ, gewaehlteDatei);

        Assert.Equal(gewaehlteDatei, pfad);
    }

    [Fact]
    public void Stufe2_ErsteDateiImTypOrdnerWennGewaehlteVorlageFehlt()
    {
        Directory.CreateDirectory(TypOrdnerPfad);
        File.WriteAllText(Path.Combine(TypOrdnerPfad, "zweite.docx"), "B");
        File.WriteAllText(Path.Combine(TypOrdnerPfad, "erste.docx"), "A");
        var nichtMehrVorhandenePfad = Path.Combine(_tmp, "geloescht.docx"); // existiert nicht

        var pfad = VorlagenOrdner.FindeVorlagenPfad(_tmp, Typ, nichtMehrVorhandenePfad);

        Assert.Equal(Path.Combine(TypOrdnerPfad, "erste.docx"), pfad);
    }

    [Fact]
    public void Stufe2_GreiftAuchOhneJemalsGewaehlteVorlage()
    {
        Directory.CreateDirectory(TypOrdnerPfad);
        File.WriteAllText(Path.Combine(TypOrdnerPfad, "muster.docx"), "Inhalt");

        var pfad = VorlagenOrdner.FindeVorlagenPfad(_tmp, Typ, letzteVorlagePfad: null);

        Assert.Equal(Path.Combine(TypOrdnerPfad, "muster.docx"), pfad);
    }

    [Fact]
    public void Stufe3_ErsteDateiImVorlagenWurzelordnerWennTypOrdnerLeerOderFehlt()
    {
        Directory.CreateDirectory(VorlagenOrdnerPfad);
        File.WriteAllText(Path.Combine(VorlagenOrdnerPfad, "zweite-root.docx"), "B");
        File.WriteAllText(Path.Combine(VorlagenOrdnerPfad, "erste-root.docx"), "A");
        // Typ-Unterordner existiert gar nicht (z.B. ältere Installation vor Task #303).

        var pfad = VorlagenOrdner.FindeVorlagenPfad(_tmp, Typ, letzteVorlagePfad: null);

        Assert.Equal(Path.Combine(VorlagenOrdnerPfad, "erste-root.docx"), pfad);
    }

    [Fact]
    public void Stufe3_IgnoriertDateienInAnderenTypUnterordnern()
    {
        Directory.CreateDirectory(TypOrdnerPfad); // eigener Typ-Ordner bleibt leer
        var andererTypOrdner = Path.Combine(VorlagenOrdnerPfad, VorlagenOrdner.OrdnerName("Berufungsbegründung"));
        Directory.CreateDirectory(andererTypOrdner);
        File.WriteAllText(Path.Combine(andererTypOrdner, "fremde-vorlage.docx"), "Fremd");
        File.WriteAllText(Path.Combine(VorlagenOrdnerPfad, "eigene-root-vorlage.docx"), "Eigen");

        var pfad = VorlagenOrdner.FindeVorlagenPfad(_tmp, Typ, letzteVorlagePfad: null);

        Assert.Equal(Path.Combine(VorlagenOrdnerPfad, "eigene-root-vorlage.docx"), pfad);
    }

    [Fact]
    public void Stufe4_KeinVorlagenKontextWennAllesLeerIst()
    {
        VorlagenOrdner.SicherstellenUnterordner(_tmp, new[] { Typ }); // leere Ordnerstruktur, wie bei frischer Installation

        var pfad = VorlagenOrdner.FindeVorlagenPfad(_tmp, Typ, letzteVorlagePfad: null);

        Assert.Null(pfad);
    }

    [Fact]
    public void Stufe4_KeinVorlagenKontextWennVorlagenOrdnerKomplettFehlt()
    {
        var pfad = VorlagenOrdner.FindeVorlagenPfad(_tmp, Typ, letzteVorlagePfad: null);

        Assert.Null(pfad);
    }
}
