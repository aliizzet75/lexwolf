using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MimeKit;
using OpenMcdf;

namespace LexWolf.Services;

/// <summary>
/// Outlook-Nachricht (.msg — entsteht beim Ziehen einer Mail aus Outlook) als MIME-Nachricht,
/// damit sie wie Postfach-Mails als .eml samt Anhängen in der Akte landet. Liest die
/// MAPI-Eigenschaften direkt aus der Verbunddatei ([MS-OXMSG]): Betreff, Absender,
/// Empfänger, Datum, Text, HTML und Anhänge.
/// </summary>
public static class MsgKonverter
{
    private const ushort PtString8 = 0x001E, PtUnicode = 0x001F, PtBinary = 0x0102, PtLong = 0x0003, PtSysTime = 0x0040;
    public const string Eigenschaften = "__properties_version1.0";

    public static MimeMessage Laden(string pfad)
    {
        using var datei = File.OpenRead(pfad);
        return Laden(datei);
    }

    public static MimeMessage Laden(Stream msg)
    {
        using var root = RootStorage.Open(msg, StorageModeFlags.LeaveOpen);
        var m = new MimeMessage();
        var props = FesteEigenschaften(root, kopf: 32);

        var absenderName = Text(root, 0x0C1A) ?? Text(root, 0x0042) ?? "";
        var absender = Text(root, 0x5D01) ?? Text(root, 0x0C1F) ?? Text(root, 0x0065) ?? "";
        m.From.Add(new MailboxAddress(absenderName, Adresse(absender)));

        foreach (var name in Kinder(root, "__recip_version1.0_#"))
        {
            var r = root.OpenStorage(name);
            var mail = Text(r, 0x39FE) ?? Text(r, 0x3003) ?? "";
            if (!mail.Contains('@')) continue;
            var adresse = new MailboxAddress(Text(r, 0x3001) ?? "", mail);
            var typ = FesteEigenschaften(r, kopf: 8).TryGetValue(0x0C15, out var t) ? BitConverter.ToInt32(t, 0) : 1;
            if (typ == 2) m.Cc.Add(adresse); else m.To.Add(adresse);
        }

        m.Subject = Text(root, 0x0037) ?? "";
        var zeit = props.TryGetValue(0x0039, out var gesendet) ? gesendet
            : props.TryGetValue(0x0E06, out var empfangen) ? empfangen : null;
        m.Date = zeit is null ? DateTimeOffset.Now : DateTimeOffset.FromFileTime(BitConverter.ToInt64(zeit, 0));

        var body = new BodyBuilder { TextBody = Text(root, 0x1000) };
        if (Binaer(root, 0x1013) is { Length: > 0 } html)
            body.HtmlBody = Encoding.UTF8.GetString(html);

        foreach (var name in Kinder(root, "__attach_version1.0_#"))
        {
            var a = root.OpenStorage(name);
            var dateiname = Text(a, 0x3707) ?? Text(a, 0x3704) ?? Text(a, 0x3001) ?? "Anhang";
            // Eingebettete Nachrichten (weitergeleitete Mail als Anhang) haben keine Binärdaten
            // und werden übersprungen — ihr Text steht meist ohnehin im zitierten Verlauf.
            if (Binaer(a, 0x3701) is { Length: > 0 } daten)
                body.Attachments.Add(dateiname, daten);
        }
        m.Body = body.ToMessageBody();
        return m;
    }

    private static string Adresse(string s) => s.Contains('@') ? s : "unbekannt@unbekannt";

    private static IEnumerable<string> Kinder(Storage storage, string praefix) =>
        storage.EnumerateEntries()
            .Where(e => e.Type == EntryType.Storage && e.Name.StartsWith(praefix, StringComparison.Ordinal))
            .Select(e => e.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    private static byte[]? Lesen(Storage storage, string name)
    {
        if (!storage.TryOpenStream(name, out var s)) return null;
        using (s)
        {
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }

    private static string? Text(Storage storage, ushort id)
    {
        if (Lesen(storage, $"__substg1.0_{id:X4}{PtUnicode:X4}") is { } u)
            return Encoding.Unicode.GetString(u).TrimEnd('\0');
        if (Lesen(storage, $"__substg1.0_{id:X4}{PtString8:X4}") is { } a)
            return Encoding.Latin1.GetString(a).TrimEnd('\0');
        return null;
    }

    private static byte[]? Binaer(Storage storage, ushort id) => Lesen(storage, $"__substg1.0_{id:X4}{PtBinary:X4}");

    /// <summary>Eigenschaften fester Länge (Zahlen, Zeitpunkte) aus „__properties_version1.0“:
    /// Kopf (32 Byte Nachricht, 8 Byte Empfänger/Anhang), dann je 16 Byte Tag, Flags, Wert.</summary>
    private static Dictionary<ushort, byte[]> FesteEigenschaften(Storage storage, int kopf)
    {
        var ergebnis = new Dictionary<ushort, byte[]>();
        if (Lesen(storage, Eigenschaften) is not { } daten) return ergebnis;
        for (var i = kopf; i + 16 <= daten.Length; i += 16)
        {
            var typ = BitConverter.ToUInt16(daten, i);
            var id = BitConverter.ToUInt16(daten, i + 2);
            if (typ is PtLong or PtSysTime) ergebnis[id] = daten[(i + 8)..(i + 16)];
        }
        return ergebnis;
    }
}
