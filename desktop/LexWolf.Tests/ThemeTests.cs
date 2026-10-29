using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests;

public class ThemeTests
{
    [Fact]
    public void Dunkel_liefert_die_bisherigen_Farben_unveraendert()
    {
        var t = Theme.Aus(dunkel: true);
        Assert.Equal("#0d1117", t.Hintergrund);
        Assert.Equal("#c9d1d9", t.Vordergrund);
        Assert.Equal("#161b22", t.Oberflaeche);
        Assert.Equal("#e8a838", t.Akzent);
    }

    [Fact]
    public void Hell_liefert_andere_gueltige_Farben()
    {
        var dunkel = Theme.Aus(dunkel: true);
        var hell = Theme.Aus(dunkel: false);
        Assert.NotEqual(dunkel.Hintergrund, hell.Hintergrund);
        Assert.NotEqual(dunkel.Vordergrund, hell.Vordergrund);
        Assert.NotEqual(dunkel.Oberflaeche, hell.Oberflaeche);
        Assert.NotEqual(dunkel.Akzent, hell.Akzent);
        foreach (var farbe in new[] { hell.Hintergrund, hell.Vordergrund, hell.Oberflaeche, hell.Akzent })
        {
            Assert.StartsWith("#", farbe);
            Assert.Equal(7, farbe.Length);
        }
    }
}
