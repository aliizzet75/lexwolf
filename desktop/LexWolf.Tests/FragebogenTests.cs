using System;
using System.Linq;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests;

public class FragebogenTests
{
    [Fact]
    public void Uebernahme_setzt_nur_gefundene_Angaben()
    {
        var f = new Fragebogen { NettoGegenseite = 3000m, Bemerkungen = "aus Gespräch" };
        var n = f.UebernehmeAus("""
            {"netto_mandant": 1450.5, "mandant_erwerbstaetig": false, "netto_gegenseite": null,
             "heiratsdatum": "2012-06-30", "kinder": [{"name": "Lea", "geburtsdatum": "2016-04-02", "lebt_bei_mandant": true}],
             "bemerkungen": "Haus gehört beiden je zur Hälfte"}
            """);
        Assert.Equal(5, n);
        Assert.Equal(1450.5m, f.NettoMandant);
        Assert.False(f.MandantErwerbstaetig);
        Assert.Equal(3000m, f.NettoGegenseite); // null überschreibt nicht
        Assert.Equal(new DateTime(2012, 6, 30), f.Heiratsdatum);
        Assert.Equal("Lea", f.Kinder.Single().Name);
        Assert.Contains("aus Gespräch", f.Bemerkungen);
        Assert.Contains("je zur Hälfte", f.Bemerkungen);
    }

    [Fact]
    public void Vorhandenes_Kind_wird_ergaenzt_nicht_doppelt_angelegt()
    {
        var f = new Fragebogen();
        f.Kinder.Add(new FragebogenKind { Name = "Ben" });
        f.UebernehmeAus("""{"kinder": [{"name": "ben", "geburtsdatum": "2020-11-20", "lebt_bei_mandant": false}]}""");
        var ben = f.Kinder.Single();
        Assert.Equal(new DateTime(2020, 11, 20), ben.Geburtsdatum);
        Assert.False(ben.LebtBeiMandant);
    }
}
