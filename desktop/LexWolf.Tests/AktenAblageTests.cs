using System;
using System.IO;
using System.Linq;
using System.Text;
using LexWolf.Services;
using MimeKit;
using OpenMcdf;
using Xunit;

namespace LexWolf.Tests;

public sealed class AktenAblageTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "lexwolf-ablage-" + Guid.NewGuid().ToString("N"));
    private string Akte => Path.Combine(_tmp, "Neumann, Eva");

    public AktenAblageTests() => Directory.CreateDirectory(Path.Combine(_tmp, "quelle"));

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    private string Quelle(string name, string inhalt = "Inhalt")
    {
        var p = Path.Combine(_tmp, "quelle", name);
        File.WriteAllText(p, inhalt);
        return p;
    }

    [Fact]
    public void Datei_wird_kopiert_Original_bleibt_und_Name_kollidiert_nicht()
    {
        var q = Quelle("Urteil.pdf");
        var erste = AktenAblage.Ablegen(q, Akte).Single();
        var zweite = AktenAblage.Ablegen(q, Akte).Single();

        Assert.True(File.Exists(q));
        Assert.Equal(Path.Combine(Akte, "Urteil.pdf"), erste);
        Assert.NotEqual(erste, zweite);
        Assert.True(File.GetLastWriteTime(erste) > DateTime.Now.AddMinutes(-1));
    }

    [Fact]
    public void Datei_die_schon_in_der_Akte_liegt_wird_nicht_nochmal_kopiert()
    {
        Directory.CreateDirectory(Akte);
        var p = Path.Combine(Akte, "Vertrag.pdf");
        File.WriteAllText(p, "x");
        Assert.Equal(p, AktenAblage.Ablegen(p, Akte).Single());
        Assert.Single(Directory.GetFiles(Akte));
    }

    [Fact]
    public void Eml_landet_mit_Datum_unter_EMails_und_Anhang_als_eigene_Datei()
    {
        var mail = new MimeMessage();
        mail.From.Add(new MailboxAddress("AG Köln", "poststelle@ag-koeln.nrw.de"));
        mail.Subject = "Beschluss vom 30.09.2026";
        mail.Date = new DateTimeOffset(2026, 10, 14, 9, 31, 0, TimeSpan.FromHours(2));
        var body = new BodyBuilder { TextBody = "Anbei der Beschluss." };
        body.Attachments.Add("Beschluss.pdf", Encoding.UTF8.GetBytes("%PDF-1.4"));
        mail.Body = body.ToMessageBody();
        var q = Path.Combine(_tmp, "quelle", "irgendwas.eml");
        mail.WriteTo(q);

        var abgelegt = AktenAblage.Ablegen(q, Akte);

        Assert.Equal(2, abgelegt.Count);
        Assert.All(abgelegt, p => Assert.StartsWith(Path.Combine(Akte, AktenAblage.EmailOrdner), p));
        Assert.Contains(abgelegt, p => p.EndsWith("2026-10-14_0931_Beschluss vom 30.09.2026.eml"));
        Assert.Contains(abgelegt, p => p.EndsWith("Beschluss.pdf"));
    }

    [Theory]
    [InlineData("a.PDF", true)]
    [InlineData("a.msg", true)]
    [InlineData("foto.jpg", false)]
    public void Auswertbar_nach_Dateityp(string name, bool erwartet) =>
        Assert.Equal(erwartet, AktenAblage.IstAuswertbar(name));

    [Fact]
    public void Outlook_msg_wird_mit_Absender_Datum_Text_und_Anhang_gelesen()
    {
        var m = MsgKonverter.Laden(BeispielMsg());

        Assert.Equal("Unterhalt Lena und Paul", m.Subject);
        Assert.Equal("t.weber@example.de", m.From.Mailboxes.Single().Address);
        Assert.Equal("info@kanzlei-wolf.de", m.To.Mailboxes.Single().Address);
        Assert.Equal(new DateTime(2026, 10, 12, 8, 15, 0), m.Date.UtcDateTime);
        Assert.Contains("anbei die Abrechnungen", m.TextBody);
        Assert.Equal("Gehalt_09-2026.pdf", MailText.Anhaenge(m).Single().FileName);
    }

    [Fact]
    public void Aus_Outlook_gezogene_Mail_landet_als_eml_mit_Anhang_in_der_Akte()
    {
        var q = Path.Combine(_tmp, "quelle", "Unterhalt Lena und Paul.msg");
        File.WriteAllBytes(q, BeispielMsg().ToArray());

        var abgelegt = AktenAblage.Ablegen(q, Akte, MsgKonverter.Laden);

        Assert.Equal(2, abgelegt.Count);
        Assert.Contains(abgelegt, p => p.EndsWith(".eml") && Path.GetFileName(p).StartsWith("2026-10-12_"));
        Assert.Contains(abgelegt, p => p.EndsWith("Gehalt_09-2026.pdf"));
        Assert.Contains("anbei die Abrechnungen", MailText.LeseEml(abgelegt.Single(p => p.EndsWith(".eml"))));
    }

    private static MemoryStream BeispielMsg()
    {
        var ms = new MemoryStream();
        using (var root = RootStorage.Create(ms, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
        {
            void Text(Storage s, string id, string wert) => Schreibe(s, $"__substg1.0_{id}001F", Encoding.Unicode.GetBytes(wert));
            Text(root, "0037", "Unterhalt Lena und Paul");
            Text(root, "0C1A", "Thomas Weber");
            Text(root, "5D01", "t.weber@example.de");
            Text(root, "1000", "Hallo Frau Neumann, anbei die Abrechnungen.");
            // Kopf 32 Byte, dann PR_CLIENT_SUBMIT_TIME (0x0039, PT_SYSTIME)
            var props = new byte[32 + 16];
            BitConverter.GetBytes((ushort)0x0040).CopyTo(props, 32);
            BitConverter.GetBytes((ushort)0x0039).CopyTo(props, 34);
            BitConverter.GetBytes(new DateTime(2026, 10, 12, 8, 15, 0, DateTimeKind.Utc).ToFileTimeUtc()).CopyTo(props, 40);
            Schreibe(root, MsgKonverter.Eigenschaften, props);

            var empfaenger = root.CreateStorage("__recip_version1.0_#00000000");
            Text(empfaenger, "3001", "Kanzlei Wolf");
            Text(empfaenger, "39FE", "info@kanzlei-wolf.de");

            var anhang = root.CreateStorage("__attach_version1.0_#00000000");
            Text(anhang, "3707", "Gehalt_09-2026.pdf");
            Schreibe(anhang, "__substg1.0_37010102", Encoding.ASCII.GetBytes("%PDF-1.4 Abrechnung"));
        }
        ms.Position = 0;
        return ms;
    }

    private static void Schreibe(Storage s, string name, byte[] daten)
    {
        using var stream = s.CreateStream(name);
        stream.Write(daten, 0, daten.Length);
    }
}
