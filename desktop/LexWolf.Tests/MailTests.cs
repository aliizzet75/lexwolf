using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LexWolf.Services;
using MimeKit;
using Xunit;

namespace LexWolf.Tests;

public class MailTextTests
{
    private static MimeMessage Mail(string betreff, string? text, string? html, bool mitPdf = false)
    {
        var m = new MimeMessage();
        m.From.Add(new MailboxAddress("Rechtsanwalt Dr. Klein", "klein@kanzlei-klein.de"));
        m.To.Add(new MailboxAddress("Kanzlei", "info@lexwolf.de"));
        m.Subject = betreff;
        m.Date = new DateTimeOffset(2026, 10, 14, 9, 31, 0, TimeSpan.FromHours(2));
        var b = new BodyBuilder { TextBody = text, HtmlBody = html };
        if (mitPdf) b.Attachments.Add("Schriftsatz.pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 });
        m.Body = b.ToMessageBody();
        return m;
    }

    [Fact]
    public void Html_Mail_wird_lesbarer_Text()
    {
        var m = Mail("Unterhalt Schmidt ./. Weber", null,
            "<html><head><style>p{color:red}</style></head><body><p>Sehr geehrte Kollegin,</p><p>anbei unser Schriftsatz &amp; die Anlagen.</p></body></html>");
        var t = MailText.AlsText(m);
        Assert.Contains("Betreff: Unterhalt Schmidt ./. Weber", t);
        Assert.Contains("anbei unser Schriftsatz & die Anlagen.", t);
        Assert.DoesNotContain("<p>", t);
        Assert.DoesNotContain("color:red", t);
    }

    [Fact]
    public void Base64_kodierte_Mail_wird_dekodiert()
    {
        var m = Mail("Test", "Die Frist läuft am 28.10.2026 ab.", null);
        foreach (var part in m.BodyParts.OfType<TextPart>()) part.ContentTransferEncoding = ContentEncoding.Base64;
        var pfad = Path.Combine(Path.GetTempPath(), $"lw_mail_{Guid.NewGuid():N}.eml");
        m.WriteTo(pfad);
        try
        {
            Assert.DoesNotContain("läuft", File.ReadAllText(pfad)); // roh nur Base64
            Assert.Contains("Die Frist läuft am 28.10.2026 ab.", MailText.LeseEml(pfad));
        }
        finally { File.Delete(pfad); }
    }

    [Fact]
    public void Speichern_legt_Mail_und_Anhang_ab()
    {
        var ordner = Path.Combine(Path.GetTempPath(), $"lw_mail_{Guid.NewGuid():N}");
        try
        {
            var pfad = MailText.Speichern(Mail("AW: Beschluss vom 30.09.2026", "Text", null, mitPdf: true), ordner);
            Assert.EndsWith("2026-10-14_0931_Beschluss vom 30.09.2026.eml", pfad);
            Assert.True(File.Exists(Path.Combine(ordner, "2026-10-14_0931_Beschluss vom 30.09.2026 - Schriftsatz.pdf")));
        }
        finally { Directory.Delete(ordner, true); }
    }
}

public class MailZuordnungTests
{
    private static readonly MandantKandidat[] Mandanten =
    {
        new("m1", "Schmidt, Anna", "312 F 45/26", "Tom Weber", "Dr. Klein"),
        new("m2", "Weber, Tom", "", "", ""),
        new("m3", "Müller Bau GmbH", "9 O 123/26", "", ""),
    };

    private static readonly Dictionary<string, string> Keine = new();

    [Fact]
    public void Aktenzeichen_im_Betreff_gewinnt()
    {
        var z = MailZuordnung.Finde("poststelle@ag-koeln.nrw.de", "AG Köln", "Ihr Zeichen / 312F 45/26", "", Mandanten, Keine);
        Assert.Equal("m1", z.MandantId);
        Assert.Contains("Aktenzeichen", z.Grund);
    }

    [Fact]
    public void Mandant_schreibt_selbst()
    {
        var z = MailZuordnung.Finde("anna.schmidt@web.de", "Anna Schmidt", "Frage", "Hallo, wie ist der Stand?", Mandanten, Keine);
        Assert.Equal("m1", z.MandantId);
    }

    [Fact]
    public void Bekannter_Absender_wird_wiedererkannt()
    {
        var bekannt = new Dictionary<string, string> { ["info@kanzlei-klein.de"] = "m1" };
        var z = MailZuordnung.Finde("Info@Kanzlei-Klein.de", "Kanzlei Klein", "Neuer Schriftsatz", "anbei", Mandanten, bekannt);
        Assert.Equal("m1", z.MandantId);
    }

    [Fact]
    public void Mehrdeutig_wird_nicht_geraten()
    {
        // Beide Namen im Text: Schmidt (m1) und Weber (m2) gleich stark
        var z = MailZuordnung.Finde("x@y.de", "X", "Termin", "Betrifft Anna Schmidt und Tom Weber", Mandanten, Keine);
        Assert.Null(z.MandantId);
        Assert.Contains("mehrere", z.Grund);
    }

    [Fact]
    public void Ohne_Hinweis_bleibt_unzugeordnet()
    {
        var z = MailZuordnung.Finde("newsletter@shop.de", "Shop", "Angebot der Woche", "20 % Rabatt", Mandanten, Keine);
        Assert.Null(z.MandantId);
    }
}
