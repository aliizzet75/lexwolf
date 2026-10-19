using System;
using System.Collections.Generic;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests;

public class MandatAufnahmeTests
{
    private static readonly List<(string Id, string Name)> Mandanten = new()
    {
        ("m1", "Weber, Thomas"),
        ("m2", "Schmidt, Anna"),
    };

    private static readonly List<AktenBeteiligter> Beteiligte = new()
    {
        // In der Akte Weber ist Frau Klein die Gegnerin.
        new("m1", "Weber, Thomas", "Gegner", "Klein, Petra"),
    };

    [Theory]
    [InlineData("Müller, Hans", "Müller, Hans")]
    [InlineData("  Müller /  Hans: ", "Müller Hans")]
    [InlineData("Firma A*B GmbH.", "Firma A B GmbH")]
    public void Ordnername_ohne_unzulaessige_Zeichen(string eingabe, string erwartet) =>
        Assert.Equal(erwartet, MandatAufnahme.OrdnerName(eingabe));

    [Fact]
    public void Bestehender_Mandant_wird_trotz_anderer_Schreibweise_erkannt()
    {
        Assert.Equal("Weber, Thomas", MandatAufnahme.BestehenderMandant("Thomas Weber", Mandanten));
        Assert.Null(MandatAufnahme.BestehenderMandant("Neumann, Eva", Mandanten));
    }

    [Fact]
    public void Gegner_der_schon_Mandant_ist_wird_vor_dem_Anlegen_erkannt()
    {
        var k = MandatAufnahme.PruefeKollision(new MandatDaten("Neumann, Eva", Gegner: "Thomas Weber"), Mandanten, Beteiligte);
        Assert.Single(k);
        Assert.Equal("Weber, Thomas", k[0].AndereAkte);
    }

    [Fact]
    public void Neuer_Mandant_der_anderswo_Gegner_ist_wird_erkannt()
    {
        var k = MandatAufnahme.PruefeKollision(new MandatDaten("Petra Klein"), Mandanten, Beteiligte);
        Assert.Single(k);
        Assert.Contains("Gegner", k[0].Beschreibung);
    }

    [Fact]
    public void Gegnervertreter_ist_keine_Kollision()
    {
        var k = MandatAufnahme.PruefeKollision(
            new MandatDaten("Neumann, Eva", Gegnervertreter: "Schmidt, Anna"), Mandanten, Beteiligte);
        Assert.Empty(k);
    }

    [Fact]
    public void Aktenvermerk_dokumentiert_die_Kollisionspruefung()
    {
        var text = MandatAufnahme.Aktenvermerk(new MandatDaten("Neumann, Eva", Gegner: "Neumann, Jan"),
            new DateTime(2026, 10, 18, 14, 30, 0), new List<Kollision>(), false, null);
        Assert.Contains("Kollisionsprüfung", text);
        Assert.Contains("keine Treffer", text);
        Assert.Contains("18.10.2026", text);
    }

    [Fact]
    public void Vollmacht_in_Familiensachen_mit_besonderer_Vollmacht_fuer_Ehesachen()
    {
        var familie = MandatAufnahme.Vollmacht(new MandatDaten("Neumann, Eva", Rechtsgebiet: "Familienrecht – Scheidung"), "Kanzlei Wolf");
        var arbeit = MandatAufnahme.Vollmacht(new MandatDaten("Neumann, Eva", Rechtsgebiet: "Arbeitsrecht"), "Kanzlei Wolf");
        Assert.Contains("§ 114 Abs. 5 FamFG", familie);
        Assert.DoesNotContain("FamFG", arbeit);
        Assert.Contains("Kanzlei Wolf", arbeit);
    }

    [Fact]
    public void KiAuftrag_enthaelt_Angaben_und_rechnet_keine_Frist_selbst()
    {
        var d = new MandatDaten("Neumann, Eva", Email: "eva@example.de", Gegner: "Neumann, Jan",
            Rechtsgebiet: "Familienrecht – Unterhalt", Anliegen: "Kein Kindesunterhalt seit März.");
        var text = MandatAufnahme.KiAuftrag(d, "Einspruch bis Montag, 16.11.2026");
        Assert.Contains("Kein Kindesunterhalt seit März.", text);
        Assert.Contains("Einspruch bis Montag, 16.11.2026", text);
        Assert.Contains("kein Fristende ausrechnen", text);
        Assert.Contains("an eva@example.de", text);

        var nurBestaetigung = MandatAufnahme.KiAuftrag(d, null, einschaetzung: false);
        Assert.DoesNotContain("Ersteinschätzung", nurBestaetigung);
        Assert.Contains("Mandatsbestätigung", nurBestaetigung);
    }

    [Fact]
    public void Email_wird_aus_der_Mandatsaufnahme_gelesen()
    {
        var json = MandatAufnahme.SpeicherJson(new MandatDaten("Neumann, Eva", Email: "eva@example.de"), DateTime.Now);
        Assert.Equal("eva@example.de", MandatAufnahme.EmailAus(json));
        Assert.Equal("", MandatAufnahme.EmailAus(null));
        Assert.Equal("", MandatAufnahme.EmailAus("kaputt"));
    }
}
