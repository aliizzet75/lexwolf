using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests;

public class DiktatTextTests
{
    [Theory]
    [InlineData("einundsechzig", 61)]
    [InlineData("dreihunderteinundsechzig", 361)]
    [InlineData("eintausenddreihunderteinundsechzig", 1361)]
    [InlineData("fünfzehnhundertachtundsiebzig", 1578)]
    [InlineData("fünfhundertsiebzehn", 517)]
    [InlineData("zwölf", 12)]
    [InlineData("zweitausend", 2000)]
    public void ParseZahlwort_erkennt_Zahlen(string wort, int erwartet) =>
        Assert.Equal(erwartet, DiktatText.ParseZahlwort(wort));

    [Theory]
    [InlineData("unterhalt")]
    [InlineData("und")]
    [InlineData("zwanzighundert")]
    public void ParseZahlwort_lehnt_Nicht_Zahlen_ab(string wort) =>
        Assert.Null(DiktatText.ParseZahlwort(wort));

    // Echte Vosk-Ausgaben aus dem Test mit der Windows-Stimme "Hedda".
    [Theory]
    [InlineData("die antrags gegnerin hat nach paragraph ein tausend dreihundert einundsechzig bgb anspruch",
                "Die antrags gegnerin hat nach § 1361 BGB anspruch")]
    [InlineData("bitte die frist nach paragraph fünfhundert siebzehn zpo notieren",
                "Bitte die frist nach § 517 ZPO notieren")]
    [InlineData("nach paragraph fünfzehn achtundsiebzig b bgb", "Nach § 1578b BGB")]
    [InlineData("paragraph zwei absatz drei satz eins", "§ 2 Abs. 3 satz 1")]
    [InlineData("nach b g b", "Nach BGB")]
    public void Aufbereiten_macht_juristischen_Text_lesbar(string roh, string erwartet) =>
        Assert.Equal(erwartet, DiktatText.Aufbereiten(roh));

    [Theory]
    [InlineData("der mandant hat ein auto und zwei kinder", "Der mandant hat ein auto und zwei kinder")]
    [InlineData("eine e-mail an die gegenseite", "Eine E-Mail an die gegenseite")]
    public void Aufbereiten_laesst_kleine_Zahlen_und_Artikel_stehen(string roh, string erwartet) =>
        Assert.Equal(erwartet, DiktatText.Aufbereiten(roh));

    [Fact]
    public void Satz_setzt_Punkt_ans_Ende() =>
        Assert.Equal("Schreib eine kurze E-Mail.", DiktatText.Satz("schreib eine kurze e-mail"));
}
