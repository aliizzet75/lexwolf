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
}
