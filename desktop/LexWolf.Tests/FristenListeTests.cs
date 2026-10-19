using System;
using System.Collections.Generic;
using LexWolf.Database;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests;

public class FristenListeTests
{
    private static readonly DateTime Heute = new(2026, 10, 19);
    private static readonly Dictionary<string, string> Mandanten = new() { ["m1"] = "Schmidt <Anna>" };

    private static FristEintrag Frist(string bezeichnung, DateTime ende, string status = "offen", bool notfrist = false) =>
        new(Guid.NewGuid().ToString("N"), "m1", bezeichnung, null, "§ 517 ZPO", ende.AddMonths(-1), ende,
            ende.AddDays(-7), notfrist, status, null, "", "", false);

    [Fact]
    public void Enthaelt_nur_offene_Fristen_im_Zeitraum_und_Ueberfaelliges()
    {
        var html = FristenListe.BaueHtml(new[]
        {
            Frist("Berufung", Heute.AddDays(3), notfrist: true),
            Frist("Überfällig", Heute.AddDays(-1)),
            Frist("Viel später", Heute.AddDays(60)),
            Frist("Schon erledigt", Heute.AddDays(2), status: "erledigt"),
        }, Mandanten, Heute);

        Assert.Contains("Berufung", html);
        Assert.Contains("NOTFRIST", html);
        Assert.Contains("Überfällig", html);
        Assert.Contains("überfällig</span>", html);
        Assert.DoesNotContain("Viel später", html);
        Assert.DoesNotContain("Schon erledigt", html);
    }

    [Fact]
    public void Ungepruefte_Vorschlaege_werden_als_Warnung_genannt_und_Namen_escaped()
    {
        var html = FristenListe.BaueHtml(new[]
        {
            Frist("Vorschlag", Heute.AddDays(2), status: "vorschlag"),
            Frist("Berufung", Heute.AddDays(2)),
        }, Mandanten, Heute);

        Assert.Contains("1 Fristvorschlag ist noch nicht geprüft", html);
        Assert.Contains("Schmidt &lt;Anna&gt;", html);
    }
}
