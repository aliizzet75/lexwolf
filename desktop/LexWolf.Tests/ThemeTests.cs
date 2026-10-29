using LexWolf.Services;
using System.Drawing;
using Xunit;

namespace LexWolf.Tests;

public class ThemeTests
{
    [Fact]
    public void Dunkel_liefert_bisherige_Werte()
    {
        var t = Theme.Aus(dunkel: true);
        Assert.Equal(ColorTranslator.FromHtml("#0d1117"), t.Hintergrund);
        Assert.Equal(ColorTranslator.FromHtml("#c9d1d9"), t.Vordergrund);
        Assert.Equal(ColorTranslator.FromHtml("#161b22"), t.Oberflaeche);
        Assert.Equal(ColorTranslator.FromHtml("#e8a838"), t.Akzent);
    }

    [Fact]
    public void Hell_liefert_andere_gueltige_Werte()
    {
        var t = Theme.Aus(dunkel: false);
        Assert.NotEqual(t.Hintergrund, Theme.Aus(dunkel: true).Hintergrund);
        Assert.NotEqual(t.Vordergrund, Theme.Aus(dunkel: true).Vordergrund);
        Assert.NotEqual(t.Oberflaeche, Theme.Aus(dunkel: true).Oberflaeche);
        Assert.NotEqual(t.Akzent, Theme.Aus(dunkel: true).Akzent);

        // Lesbarer Kontrast: Hintergrund und Vordergrund unterscheiden sich deutlich.
        Assert.True(t.Hintergrund.R != t.Vordergrund.R || t.Hintergrund.G != t.Vordergrund.G || t.Hintergrund.B != t.Vordergrund.B);
        Assert.True(t.Oberflaeche.R != t.Vordergrund.R || t.Oberflaeche.G != t.Vordergrund.G || t.Oberflaeche.B != t.Vordergrund.B);
    }
}
