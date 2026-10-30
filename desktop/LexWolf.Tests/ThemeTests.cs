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

    [Fact]
    public void Vollstaendige_Palette_liefert_fuer_alle_Farben_Werte()
    {
        var dunkel = Theme.Aus(dunkel: true);
        var hell = Theme.Aus(dunkel: false);

        foreach (var farbe in new[]
        {
            dunkel.Bg, dunkel.Panel, dunkel.Raised, dunkel.Border, dunkel.Text,
            dunkel.TextStrong, dunkel.Muted, dunkel.Red, dunkel.Orange, dunkel.Green, dunkel.Blue,
            dunkel.HerkunftLexWolf, dunkel.HerkunftLexWolfHintergrund, dunkel.HerkunftLexWolfText,
            dunkel.HerkunftAnwalt, dunkel.HerkunftAnwaltHintergrund, dunkel.HerkunftAnwaltText,
            hell.Bg, hell.Panel, hell.Raised, hell.Border, hell.Text,
            hell.TextStrong, hell.Muted, hell.Red, hell.Orange, hell.Green, hell.Blue,
            hell.HerkunftLexWolf, hell.HerkunftLexWolfHintergrund, hell.HerkunftLexWolfText,
            hell.HerkunftAnwalt, hell.HerkunftAnwaltHintergrund, hell.HerkunftAnwaltText
        })
        {
            Assert.StartsWith("#", farbe);
            Assert.Equal(7, farbe.Length);
        }
    }

    [Fact]
    public void Dunkel_und_Hell_unterscheiden_sich_fuer_alle_Palette_Farben()
    {
        var dunkel = Theme.Aus(dunkel: true);
        var hell = Theme.Aus(dunkel: false);

        Assert.NotEqual(dunkel.Bg, hell.Bg);
        Assert.NotEqual(dunkel.Panel, hell.Panel);
        Assert.NotEqual(dunkel.Raised, hell.Raised);
        Assert.NotEqual(dunkel.Border, hell.Border);
        Assert.NotEqual(dunkel.Text, hell.Text);
        Assert.NotEqual(dunkel.TextStrong, hell.TextStrong);
        Assert.NotEqual(dunkel.Muted, hell.Muted);
        Assert.NotEqual(dunkel.Red, hell.Red);
        Assert.NotEqual(dunkel.Orange, hell.Orange);
        Assert.NotEqual(dunkel.Green, hell.Green);
        Assert.NotEqual(dunkel.Blue, hell.Blue);

        Assert.NotEqual(dunkel.HerkunftLexWolfHintergrund, hell.HerkunftLexWolfHintergrund);
        Assert.NotEqual(dunkel.HerkunftAnwaltHintergrund, hell.HerkunftAnwaltHintergrund);
    }

    [Fact]
    public void Hellmodus_hat_dunklen_Text_auf_hellem_Hintergrund()
    {
        var hell = Theme.Aus(dunkel: false);
        Assert.True(Helligkeit(hell.Bg) > Helligkeit(hell.Text), "Hintergrund muss heller als Text sein");
        Assert.NotEqual(hell.Text, hell.Muted);
        Assert.NotEqual(hell.Bg, hell.Muted);
    }

    private static double Helligkeit(string hex)
    {
        var r = int.Parse(hex.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
        var g = int.Parse(hex.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
        var b = int.Parse(hex.Substring(5, 2), System.Globalization.NumberStyles.HexNumber);
        return (r * 299 + g * 587 + b * 114) / 1000.0;
    }
}
