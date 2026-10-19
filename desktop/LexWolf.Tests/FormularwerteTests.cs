using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LexWolf.Database;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests;

public class FormularwerteTests
{
    private static Dictionary<string, (string, string)> Ki(params (string Key, string Wert)[] werte) =>
        werte.ToDictionary(w => w.Key, w => (w.Wert, "Gehaltsabrechnung.pdf"), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void LexWolf_fuellt_Anwalt_ueberschreibt_und_LexWolf_ueberschreibt_den_Anwalt_nie()
    {
        var w = new Formularwerte();
        w.KiUebernehmen(Ki(("netto_mandant", "2450")));
        Assert.Equal("2450", w.Wert("netto_mandant"));
        Assert.Equal(Herkunft.LexWolf, w.HerkunftVon("netto_mandant"));

        w.Eingabe("netto_mandant", "2500");
        Assert.Equal(Herkunft.Anwalt, w.HerkunftVon("netto_mandant"));

        w.KiUebernehmen(Ki(("netto_mandant", "2600")));
        Assert.Equal("2500", w.Wert("netto_mandant"));
        Assert.Equal("2600", w.Info("netto_mandant")!.Ki);
    }

    [Fact]
    public void Geleertes_Feld_bleibt_leer_bis_LexWolf_ausfuellen()
    {
        var w = new Formularwerte();
        w.KiUebernehmen(Ki(("netto_mandant", "2450"), ("heiratsdatum", "2012-06-14")));
        w.Eingabe("netto_mandant", null);
        w.KiUebernehmen(Ki(("netto_mandant", "2450")));
        Assert.Null(w.Wert("netto_mandant"));
        Assert.Equal(Herkunft.Leer, w.HerkunftVon("netto_mandant"));

        Assert.Equal(1, w.LeereAusfuellen());
        Assert.Equal("2450", w.Wert("netto_mandant"));
        Assert.Equal(Herkunft.LexWolf, w.HerkunftVon("netto_mandant"));
    }

    [Fact]
    public void Eingabe_gleich_dem_LexWolf_Wert_bleibt_LexWolf()
    {
        var w = new Formularwerte();
        w.KiUebernehmen(Ki(("netto_mandant", "2450")));
        w.Eingabe("netto_mandant", Formularwerte.AusEingabe(FeldTyp.Betrag, "2.450,00 €"));
        Assert.Equal(Herkunft.LexWolf, w.HerkunftVon("netto_mandant"));
    }

    [Theory]
    [InlineData("4.250", 4250)]
    [InlineData("4.250,50", 4250.5)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("4250,5", 4250.5)]
    [InlineData("4.25", 4.25)]
    [InlineData("4.5", 4.5)]
    [InlineData("2450 €", 2450)]
    public void Betrag_versteht_deutschen_Tausenderpunkt(string eingabe, double erwartet) =>
        Assert.Equal((decimal)erwartet, Formularwerte.Betrag(eingabe));

    [Fact]
    public void Angezeigter_Betrag_wird_wieder_gleich_gelesen()
    {
        foreach (var wert in new[] { "4250", "2450.5", "1234567", "0.5" })
            Assert.Equal(wert, Formularwerte.AusEingabe(FeldTyp.Betrag, Formularwerte.ZurAnzeige(FeldTyp.Betrag, wert)));
    }

    [Theory]
    [InlineData(FeldTyp.Betrag, "2.450,50 €", "2450.5")]
    [InlineData(FeldTyp.Betrag, "2450", "2450")]
    [InlineData(FeldTyp.Betrag, "abc", null)]
    [InlineData(FeldTyp.Datum, "14.06.2012", "2012-06-14")]
    [InlineData(FeldTyp.Ganzzahl, "3", "3")]
    public void Schreibweise_ist_fest(FeldTyp typ, string eingabe, string? erwartet) =>
        Assert.Equal(erwartet, Formularwerte.AusEingabe(typ, eingabe));

    [Fact]
    public void Anzeige_deutsch()
    {
        Assert.Equal("2.450,5", Formularwerte.ZurAnzeige(FeldTyp.Betrag, "2450.5"));
        Assert.Equal("14.06.2012", Formularwerte.ZurAnzeige(FeldTyp.Datum, "2012-06-14"));
    }

    [Fact]
    public void KI_Antwort_mit_Fundstelle_wird_gelesen_und_unbekannte_Felder_ignoriert()
    {
        const string json = """
            {"felder": {
              "netto_mandant": {"wert": 2450.5, "quelle": "Gehaltsabrechnung 09-2026", "zitat": "Auszahlung 2.450,50"},
              "heiratsdatum": {"wert": "2012-06-14", "quelle": "Heiratsurkunde"},
              "kinder": {"wert": [{"name": "Lena", "geburtsdatum": "2015-05-03", "lebt_bei_mandant": true}], "quelle": "Mail"},
              "mandant_erwerbstaetig": {"wert": false, "quelle": "Mail"},
              "lieblingsfarbe": {"wert": "blau"}
            }}
            """;
        var r = Formularwerte.AusKiAntwort(json, FragebogenFelder.Typen);

        Assert.Equal(4, r.Count);
        Assert.Equal("2450.5", r["netto_mandant"].Wert);
        Assert.Equal("Gehaltsabrechnung 09-2026 — „Auszahlung 2.450,50“", r["netto_mandant"].Quelle);
        Assert.Equal("false", r["mandant_erwerbstaetig"].Wert);
        Assert.Equal("Lena", Formularwerte.KinderAus(r["kinder"].Wert).Single().Name);
    }

    [Fact]
    public void Fragebogen_hin_und_zurueck()
    {
        var f = new Fragebogen { NettoMandant = 2450.5m, Heiratsdatum = new DateTime(2012, 6, 14), MandantErwerbstaetig = false };
        f.Kinder.Add(new FragebogenKind { Name = "Lena", Geburtsdatum = new DateTime(2015, 5, 3) });

        var w = FragebogenFelder.Laden(new Dictionary<string, FeldWert>(), f.ToJson());
        var zurueck = FragebogenFelder.ZuFragebogen(w);

        Assert.Equal(Herkunft.Anwalt, w.HerkunftVon("netto_mandant"));
        Assert.Equal(2450.5m, zurueck.NettoMandant);
        Assert.Equal(new DateTime(2012, 6, 14), zurueck.Heiratsdatum);
        Assert.False(zurueck.MandantErwerbstaetig);
        Assert.Equal("Lena", zurueck.Kinder.Single().Name);
    }

    [Fact]
    public void Stand_aendert_sich_mit_den_Dokumenten()
    {
        var a = Formularwerte.Stand(new[] { ("a.pdf", 10), ("b.pdf", 20) });
        Assert.Equal(a, Formularwerte.Stand(new[] { ("b.pdf", 20), ("a.pdf", 10) }));
        Assert.NotEqual(a, Formularwerte.Stand(new[] { ("a.pdf", 10), ("b.pdf", 21) }));
    }

    [Fact]
    public void Datenbank_speichert_LexWolf_und_Anwalt_getrennt()
    {
        var pfad = Path.Combine(Path.GetTempPath(), $"lexwolf-formular-{Guid.NewGuid():N}.db");
        try
        {
            var db = new LocalDb(pfad);
            var w = new Formularwerte();
            w.KiUebernehmen(Ki(("netto_mandant", "2450"), ("netto_gegenseite", "4100")));
            w.Eingabe("netto_mandant", "2500");
            w.Eingabe("netto_gegenseite", null);
            db.SaveFormularFelder("m1", FragebogenFelder.Formular, w.Felder);

            var geladen = new Formularwerte(db.GetFormularFelder("m1", FragebogenFelder.Formular));
            Assert.Equal("2450", geladen.Info("netto_mandant")!.Ki);
            Assert.Equal("2500", geladen.Wert("netto_mandant"));
            Assert.Equal("Gehaltsabrechnung.pdf", geladen.Info("netto_mandant")!.KiQuelle);
            Assert.Equal(Herkunft.Leer, geladen.HerkunftVon("netto_gegenseite"));
            Assert.Empty(db.GetFormularFelder("m2", FragebogenFelder.Formular));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(pfad); } catch { }
        }
    }
}
