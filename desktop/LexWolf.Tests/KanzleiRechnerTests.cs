using System;
using System.Linq;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests;

public class KanzleiRechnerTests
{
    private static readonly Rechenwerte W2025 = new(); // Standardwerte (Stand 2026)

    // Zahlbeträge laut offizieller Düsseldorfer Tabelle ab 01.01.2026 (Tabellenbetrag − ½ bzw. volles Kindergeld)
    [Theory]
    [InlineData(0, 0, 356.50)]   // Gruppe 1, 0–5 J.
    [InlineData(1, 0, 381.50)]   // Gruppe 2, 0–5 J.: 486 × 105 % = 510,30 -> 511 − 129,50
    [InlineData(1, 1, 456.50)]   // Gruppe 2, 6–11 J.
    [InlineData(5, 2, 706.50)]   // Gruppe 6, 12–17 J.
    [InlineData(6, 3, 691.00)]   // Gruppe 7, ab 18 J. (volles Kindergeld)
    [InlineData(14, 3, 1137.00)] // Gruppe 15, ab 18 J.
    public void Zahlbetraege_wie_Duesseldorfer_Tabelle_2026(int gruppe, int stufe, decimal erwartet)
    {
        var kg = stufe == 3 ? W2025.Kindergeld : W2025.Kindergeld / 2;
        Assert.Equal(erwartet, KanzleiRechner.Tabellenbetrag(gruppe, stufe, W2025) - kg);
    }

    [Fact]
    public void Kindesunterhalt_Minderjaehriger_zieht_halbes_Kindergeld_ab()
    {
        var kind = new Kind("Lea", new DateTime(2022, 3, 1));
        var r = KanzleiRechner.Unterhalt(2000m, true, 0m, new[] { kind }, new DateTime(2026, 10, 18), W2025);
        // 2000 − 100 (5 %) = 1900 -> Gruppe 1, 486 − 129,50 = 356,50
        Assert.Equal(1900m, r.BereinigtesEinkommen);
        Assert.Equal(1, r.Einkommensgruppe);
        Assert.Equal(356.50m, r.Kinder.Single().Zahlbetrag);
        Assert.False(r.Mangelfall);
    }

    [Fact]
    public void Volljaehriges_Kind_mit_eigenem_Hausstand_hat_Pauschalbedarf()
    {
        var kind = new Kind("Max", new DateTime(2005, 1, 1), EigenerHausstand: true);
        var r = KanzleiRechner.Unterhalt(6000m, true, 0m, new[] { kind }, new DateTime(2026, 10, 18), W2025);
        Assert.Equal(990m - 259m, r.Kinder.Single().Zahlbetrag);
    }

    // § 34 GKG / § 28 FamGKG: 40 € bis 500 €, +21 € je 500 € bis 2.000 €, +22,50 € je 1.000 € bis 10.000 €
    [Theory]
    [InlineData(500, 40)]
    [InlineData(2000, 103)]
    [InlineData(5000, 170.50)]
    [InlineData(10000, 283)]
    public void Gerichtsgebuehr_nach_Tabelle(decimal wert, decimal erwartet) =>
        Assert.Equal(erwartet, KanzleiRechner.GerichtsGebuehr(wert, W2025));

    [Fact]
    public void Gerichtskosten_Zivilprozess_drei_Gebuehren_und_ermaessigt_eine()
    {
        var zivil = KanzleiRechner.Gerichtsverfahrensarten.First(v => v.Kv == "KV 1210 GKG");
        Assert.Equal(511.50m, KanzleiRechner.Gerichtskosten(5000m, zivil, false, W2025).Betrag);
        Assert.Equal(170.50m, KanzleiRechner.Gerichtskosten(5000m, zivil, true, W2025).Betrag);
    }

    [Fact]
    public void Rvg_Tabelle_2025_wie_Gesetz()
    {
        // § 13 RVG: 51,50 € bis 500 €; 5.000 € -> 51,50 + 3×41,50 + 3×59,50 = 354,50 €
        Assert.Equal(51.50m, KanzleiRechner.RvgGebuehr(500m, W2025));
        Assert.Equal(354.50m, KanzleiRechner.RvgGebuehr(5000m, W2025));
    }

    [Fact]
    public void Mangelfall_kuerzt_anteilig()
    {
        var kinder = new[] { new Kind("A", new DateTime(2015, 1, 1)), new Kind("B", new DateTime(2012, 1, 1)) };
        var r = KanzleiRechner.Unterhalt(1700m, true, 0m, kinder, new DateTime(2026, 10, 18), W2025);
        Assert.True(r.Mangelfall);
        Assert.True(r.SummeKindesunterhalt <= r.BereinigtesEinkommen - W2025.SelbstbehaltErwerbstaetig);
    }

    [Fact]
    public void Ehegattenunterhalt_drei_Siebtel_nach_Kindesunterhalt()
    {
        var r = KanzleiRechner.Unterhalt(4000m, true, 0m, Array.Empty<Kind>(), new DateTime(2026, 10, 18), W2025,
            einkommenBerechtigterEhegatte: 1000m);
        // 4000 − 150 = 3850; 1000 − 50 = 950; (3850 − 950) × 3/7 = 1242,86 -> 1242
        Assert.Equal(1242m, r.Ehegattenunterhalt);
    }

    [Theory]
    [InlineData(500, 45)]
    [InlineData(5000, 303)]
    [InlineData(10000, 558)]
    [InlineData(25000, 788)]
    public void Rvg_Algorithmus_stimmt_mit_Tabelle_2013(decimal wert, decimal erwartet)
    {
        var w2013 = new Rechenwerte
        {
            RvgGrundgebuehr = 45m,
            RvgStufen = new[]
            {
                new RvgStufe { BisWert = 2000m, Schritt = 500m, Betrag = 35m },
                new RvgStufe { BisWert = 10000m, Schritt = 1000m, Betrag = 51m },
                new RvgStufe { BisWert = 25000m, Schritt = 3000m, Betrag = 46m },
                new RvgStufe { BisWert = 50000m, Schritt = 5000m, Betrag = 75m },
                new RvgStufe { BisWert = decimal.MaxValue, Schritt = 15000m, Betrag = 85m },
            },
        };
        Assert.Equal(erwartet, KanzleiRechner.RvgGebuehr(wert, w2013));
    }

    [Fact]
    public void Rvg_mit_Pauschale_und_Umsatzsteuer()
    {
        var gebuehren = KanzleiRechner.Tatbestaende.Where(t => t.Vv is "VV 3100" or "VV 3104");
        var (netto, brutto, weg) = KanzleiRechner.Rvg(5000m, gebuehren, W2025);
        Assert.True(brutto > netto);
        Assert.Contains("VV 7002", weg);
    }

    [Fact]
    public void Zugewinn_haelftige_Differenz()
    {
        var a = new KanzleiRechner.ZugewinnSeite("Ehemann", 10000m, 0m, 110000m);
        var b = new KanzleiRechner.ZugewinnSeite("Ehefrau", 5000m, 0m, 25000m);
        var (ausgleich, berechtigt, _) = KanzleiRechner.Zugewinn(a, b);
        Assert.Equal(40000m, ausgleich); // (100000 − 20000) / 2
        Assert.Equal("Ehefrau", berechtigt);
    }

    [Fact]
    public void Verfahrenswert_Ehesache_mindestens_3000() =>
        Assert.Equal(3000m, KanzleiRechner.Ehesache(500m).Wert);
}

public class KollisionspruefungTests
{
    [Theory]
    [InlineData("Mustermann, Max", "Herr Dr. Max Mustermann")]
    [InlineData("Müller Bau GmbH", "mueller bau")]
    [InlineData("2024-017 Schmidt, Anna", "Anna Schmidt")]
    public void Gleiche_Namen_werden_erkannt(string a, string b) =>
        Assert.True(Kollisionspruefung.GleicherName(a, b));

    [Theory]
    [InlineData("Max Mustermann", "Erika Mustermann")]
    [InlineData("Schmidt", "Schmidtke")]
    public void Verschiedene_Namen_werden_nicht_verwechselt(string a, string b) =>
        Assert.False(Kollisionspruefung.GleicherName(a, b));

    [Fact]
    public void Gegner_der_als_Mandant_gefuehrt_wird_ist_Kollision()
    {
        var mandanten = new[] { ("m1", "Schmidt, Anna"), ("m2", "Weber, Tom") };
        var beteiligte = new[] { new AktenBeteiligter("m1", "Schmidt, Anna", "Gegner", "Tom Weber") };
        var k = Kollisionspruefung.Pruefe("m1", "Schmidt, Anna", mandanten, beteiligte);
        Assert.Single(k);
        Assert.Equal("Weber, Tom", k[0].AndereAkte);
    }

    [Fact]
    public void Mandant_der_anderswo_Gegner_ist_ist_Kollision()
    {
        var mandanten = new[] { ("m1", "Schmidt, Anna"), ("m2", "Weber, Tom") };
        var beteiligte = new[] { new AktenBeteiligter("m2", "Weber, Tom", "Antragsgegnerin", "Anna Schmidt") };
        var k = Kollisionspruefung.Pruefe("m1", "Schmidt, Anna", mandanten, beteiligte);
        Assert.Single(k);
        Assert.Equal("Mandant ist Gegner", k[0].Art);
    }
}

public class AktenAnalyseTests
{
    [Fact]
    public void Parse_liest_Fristen_und_Beteiligte()
    {
        const string json = """
        {"dokumenttyp":"Urteil","absender":"AG Köln","datum":"2026-10-01","zusammenfassung":"Klage abgewiesen",
         "anliegen":"","naechster_schritt":"Berufung prüfen","eilig":true,
         "fristen":[{"bezeichnung":"Berufung","fristart_id":"berufung","ereignis_datum":null,"frist_datum":null}],
         "beteiligte":[{"rolle":"Gegner","name":"Tom Weber"}],"gericht":"AG Köln","aktenzeichen":"12 C 34/26",
         "ereignisse":[{"datum":"2026-10-01","beschreibung":"Urteil verkündet"}]}
        """;
        var a = DokumentAnalyse.Parse(json);
        Assert.Equal("Urteil", a.Dokumenttyp);
        Assert.True(a.Eilig);
        Assert.Single(a.Fristen);
        Assert.Equal("12 C 34/26", a.Aktenzeichen);
        Assert.Equal(("Gegner", "Tom Weber"), a.Beteiligte.Single());
        Assert.Single(a.Ereignisse);
    }

    [Fact]
    public void Fehlendes_Zustelldatum_nimmt_Eingangsdatum_an_und_markiert_es()
    {
        var f = new ErkannteFrist("Berufung", "berufung", null, null, null, null, null);
        var v = FristVorschlaege.Erzeuge(new[] { f }, new DateTime(2026, 10, 14), "NW").Single();
        Assert.True(v.EreignisAngenommen);
        Assert.Equal(new DateTime(2026, 11, 16), v.Ergebnis.Ende);
        Assert.True(v.Notfrist);
    }

    [Fact]
    public void Gesetzte_Frist_mit_Datum()
    {
        var f = new ErkannteFrist("Stellungnahme", "gesetzt", null, new DateTime(2026, 11, 1), null, null, "binnen bis 01.11.2026");
        var v = FristVorschlaege.Erzeuge(new[] { f }, new DateTime(2026, 10, 14), "NW").Single();
        // 01.11.2026 ist Sonntag und Allerheiligen in NRW -> Montag 02.11.
        Assert.Equal(new DateTime(2026, 11, 2), v.Ergebnis.Ende);
    }
}
