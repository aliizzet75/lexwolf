using System;
using System.IO;
using System.Linq;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests;

public class FristRechnerTests
{
    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    public void Ostersonntag_stimmt(int jahr, int monat, int tag) =>
        Assert.Equal(new DateTime(jahr, monat, tag), FristRechner.Ostersonntag(jahr));

    [Fact]
    public void Monatsfrist_endet_am_Samstag_und_verschiebt_auf_Montag()
    {
        // Zustellung Mi 14.10.2026 + 1 Monat = Sa 14.11.2026 -> Mo 16.11.2026
        var art = FristRechner.FindeFristart("berufung")!;
        var r = FristRechner.Berechne(new DateTime(2026, 10, 14), art, "NW");
        Assert.Equal(new DateTime(2026, 11, 14), r.EndeOhneVerschiebung);
        Assert.Equal(new DateTime(2026, 11, 16), r.Ende);
        Assert.Contains("Samstag", r.Rechenweg);
    }

    [Fact]
    public void Monatsfrist_ab_31_endet_am_Monatsletzten()
    {
        // 31.01.2026 + 1 Monat -> 28.02.2026 (§ 188 Abs. 3 BGB), Samstag -> Mo 02.03.2026
        var r = FristRechner.Berechne(new DateTime(2026, 1, 31), 1, FristEinheit.Monate, "NW", 7);
        Assert.Equal(new DateTime(2026, 2, 28), r.EndeOhneVerschiebung);
        Assert.Equal(new DateTime(2026, 3, 2), r.Ende);
        Assert.Contains("§ 188 Abs. 3 BGB", r.Rechenweg);
    }

    [Fact]
    public void Wochenfrist_ueber_Weihnachten()
    {
        // Do 11.12.2025 + 2 Wochen = Do 25.12. (Feiertag), 26.12. Feiertag, Wochenende -> Mo 29.12.2025
        var art = FristRechner.FindeFristart("einspruch_vu")!;
        var r = FristRechner.Berechne(new DateTime(2025, 12, 11), art, "BY");
        Assert.Equal(new DateTime(2025, 12, 29), r.Ende);
        Assert.Contains("1. Weihnachtstag", r.Rechenweg);
    }

    [Theory]
    [InlineData("NW", 31)]  // Reformationstag kein Feiertag in NRW
    [InlineData("NI", 3)]   // in Niedersachsen schon -> Montag 03.11.
    public void Reformationstag_haengt_vom_Bundesland_ab(string land, int erwarteterTag)
    {
        var r = FristRechner.Berechne(new DateTime(2025, 10, 17), 2, FristEinheit.Wochen, land, 3);
        Assert.Equal(erwarteterTag, r.Ende.Day);
    }

    [Fact]
    public void Fronleichnam_nur_in_bestimmten_Laendern()
    {
        var fronleichnam = new DateTime(2026, 6, 4);
        Assert.Equal("Fronleichnam", FristRechner.FeiertagName(fronleichnam, "NW"));
        Assert.Null(FristRechner.FeiertagName(fronleichnam, "BE"));
    }

    [Fact]
    public void Vorfrist_liegt_auf_einem_Werktag_vor_dem_Ende()
    {
        var art = FristRechner.FindeFristart("berufungsbegruendung")!;
        var r = FristRechner.Berechne(new DateTime(2026, 10, 14), art, "NW");
        Assert.True(r.Vorfrist < r.Ende);
        Assert.True(FristRechner.IstWerktag(r.Vorfrist, "NW"));
    }

    [Fact]
    public void Gesetzte_Frist_am_Sonntag_laeuft_am_Montag_ab()
    {
        var r = FristRechner.GesetzteFrist(new DateTime(2026, 10, 18), "NW");
        Assert.Equal(new DateTime(2026, 10, 19), r.Ende);
    }

    [Fact]
    public void Einspruch_gegen_Versaeumnisurteil_im_Arbeitsrecht_nur_eine_Woche()
    {
        // Zustellung Mo 12.10.2026 + 1 Woche = Mo 19.10.2026 (§ 59 ArbGG), Notfrist
        var art = FristRechner.FindeFristart("einspruch_vu_arbg")!;
        var r = FristRechner.Berechne(new DateTime(2026, 10, 12), art, "NW");
        Assert.Equal(new DateTime(2026, 10, 19), r.Ende);
        Assert.True(art.Notfrist);
    }

    [Fact]
    public void Fristart_Ids_sind_eindeutig()
    {
        var ids = FristRechner.Fristarten.Select(f => f.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Jede_Fristart_ist_auch_der_KI_im_Backend_bekannt()
    {
        // Die KI schlägt nur Fristarten aus backend/api/akte.py (FRISTARTEN) vor —
        // eine Fristart, die nur hier steht, würde nie automatisch erkannt.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "backend", "api", "akte.py")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var backend = File.ReadAllText(Path.Combine(dir!.FullName, "backend", "api", "akte.py"));
        var fehlend = FristRechner.Fristarten.Where(f => !backend.Contains($"\"{f.Id}\":")).Select(f => f.Id).ToList();
        Assert.Empty(fehlend);
    }
}
