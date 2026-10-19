using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LexWolf.Services;

/// <summary>
/// Bereitet den Rohtext der Live-Erkennung (Vosk: alles klein, ohne
/// Satzzeichen, Zahlen als Wörter) für juristische Diktate auf:
/// "paragraph ein tausend dreihundert einundsechzig bgb" -> "§ 1361 BGB".
/// Bewusst einfach gehalten — die Whisper-Nachkorrektur ersetzt den Text
/// danach ohnehin; das hier soll nur den Live-Text gut lesbar machen.
/// </summary>
public static class DiktatText
{
    private static readonly Dictionary<string, int> Einer = new()
    {
        ["null"] = 0, ["ein"] = 1, ["eins"] = 1, ["eine"] = 1, ["zwei"] = 2, ["zwo"] = 2, ["drei"] = 3,
        ["vier"] = 4, ["fünf"] = 5, ["sechs"] = 6, ["sieben"] = 7, ["acht"] = 8, ["neun"] = 9,
        ["zehn"] = 10, ["elf"] = 11, ["zwölf"] = 12, ["dreizehn"] = 13, ["vierzehn"] = 14,
        ["fünfzehn"] = 15, ["sechzehn"] = 16, ["siebzehn"] = 17, ["achtzehn"] = 18, ["neunzehn"] = 19,
    };

    private static readonly Dictionary<string, int> Zehner = new()
    {
        ["zwanzig"] = 20, ["dreißig"] = 30, ["vierzig"] = 40, ["fünfzig"] = 50,
        ["sechzig"] = 60, ["siebzig"] = 70, ["achtzig"] = 80, ["neunzig"] = 90,
    };

    // Nach diesen Wörtern werden auch kleine Zahlen als Ziffer geschrieben.
    private static readonly HashSet<string> ZiffernAusloeser = new(StringComparer.OrdinalIgnoreCase)
    {
        "§", "§§", "Abs.", "satz", "nummer", "nr", "artikel", "art", "seite", "randnummer", "rn",
    };

    private static readonly Dictionary<string, string> Abkuerzungen = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bgb"] = "BGB", ["zpo"] = "ZPO", ["stgb"] = "StGB", ["stpo"] = "StPO", ["famfg"] = "FamFG",
        ["hgb"] = "HGB", ["gg"] = "GG", ["sgb"] = "SGB", ["vwgo"] = "VwGO", ["vwvfg"] = "VwVfG",
        ["ao"] = "AO", ["estg"] = "EStG", ["ustg"] = "UStG", ["gmbhg"] = "GmbHG", ["aktg"] = "AktG",
        ["inso"] = "InsO", ["rvg"] = "RVG", ["gkg"] = "GKG", ["famgkg"] = "FamGKG", ["arbgg"] = "ArbGG",
        ["kschg"] = "KSchG", ["bgh"] = "BGH", ["bverfg"] = "BVerfG", ["olg"] = "OLG", ["lg"] = "LG",
        ["ag"] = "AG", ["eu"] = "EU", ["dsgvo"] = "DSGVO", ["e-mail"] = "E-Mail",
    };

    /// <summary>Zahlwort (ohne Leerzeichen) -> Wert, oder null.</summary>
    public static int? ParseZahlwort(string wort)
    {
        if (string.IsNullOrEmpty(wort)) return null;
        var w = wort.ToLowerInvariant();

        var t = w.LastIndexOf("tausend", StringComparison.Ordinal);
        if (t >= 0)
        {
            var links = w[..t];
            var rechts = w[(t + "tausend".Length)..];
            int? l = links.Length == 0 ? 1 : ParseZahlwort(links);
            int? r = rechts.Length == 0 ? 0 : ParseZahlwort(rechts);
            return l is null || r is null || r >= 1000 ? null : l * 1000 + r;
        }

        var h = w.LastIndexOf("hundert", StringComparison.Ordinal);
        if (h >= 0)
        {
            var links = w[..h];
            var rechts = w[(h + "hundert".Length)..];
            int? l = links.Length == 0 ? 1 : ParseUnterHundert(links);
            int? r = rechts.Length == 0 ? 0 : ParseUnterHundert(rechts);
            // "fünfzehnhundert" (1500) ist erlaubt, "zwanzighundert" nicht
            return l is null or < 1 or > 19 || r is null ? null : l * 100 + r;
        }

        return ParseUnterHundert(w);
    }

    private static int? ParseUnterHundert(string w)
    {
        if (Einer.TryGetValue(w, out var e)) return e;
        if (Zehner.TryGetValue(w, out var z)) return z;
        var und = w.IndexOf("und", StringComparison.Ordinal);
        if (und > 0 && Einer.TryGetValue(w[..und], out var einer) && einer is >= 1 and <= 9
            && Zehner.TryGetValue(w[(und + 3)..], out var zehner))
            return zehner + einer;
        return null;
    }

    /// <summary>Wandelt einen Vosk-Satz in lesbaren Text um (ohne Satzende).</summary>
    public static string Aufbereiten(string roh)
    {
        var woerter = roh.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var aus = new List<string>();

        for (var i = 0; i < woerter.Count; i++)
        {
            var w = woerter[i];
            var lower = w.ToLowerInvariant();

            if (lower is "paragraph" or "paragraf") { aus.Add("§"); continue; }
            if (lower is "paragraphen" or "paragrafen") { aus.Add("§§"); continue; }
            if (lower == "absatz") { aus.Add("Abs."); continue; }

            // Zahlwort-Folge einsammeln
            var j = i;
            while (j < woerter.Count && IstZahlBaustein(woerter[j])) j++;
            if (j > i)
            {
                var folge = woerter.GetRange(i, j - i);
                var vorher = aus.Count > 0 ? aus[^1] : "";
                var zahl = ZahlAusFolge(folge);
                var alleinArtikel = folge.Count == 1 && lower is "ein" or "eine" or "einen" or "einem" or "einer";
                if (zahl is not null && !alleinArtikel && (zahl >= 13 || ZiffernAusloeser.Contains(vorher)))
                {
                    var text = zahl.Value.ToString();
                    // "§ 1578 b" -> "§ 1578b"
                    if (j < woerter.Count && woerter[j].Length == 1 && woerter[j][0] is >= 'a' and <= 'h'
                        && (vorher is "§" or "§§"))
                    {
                        text += woerter[j];
                        j++;
                    }
                    aus.Add(text);
                    i = j - 1;
                    continue;
                }
            }

            // Buchstabierte Abkürzung "b g b" -> "BGB"
            if (w.Length == 1 && char.IsLetter(w[0]) && i + 1 < woerter.Count && woerter[i + 1].Length == 1
                && char.IsLetter(woerter[i + 1][0]))
            {
                var sb = new StringBuilder();
                while (i < woerter.Count && woerter[i].Length == 1 && char.IsLetter(woerter[i][0]))
                    sb.Append(woerter[i++]);
                i--;
                var buchst = sb.ToString();
                aus.Add(Abkuerzungen.TryGetValue(buchst, out var abk1) ? abk1 : buchst.ToUpperInvariant());
                continue;
            }

            aus.Add(Abkuerzungen.TryGetValue(w, out var abk) ? abk : w);
        }

        var satz = string.Join(' ', aus);
        return satz.Length == 0 ? "" : char.ToUpper(satz[0]) + satz[1..];
    }

    /// <summary>Fertiger Satz: aufbereitet und mit Punkt.</summary>
    public static string Satz(string roh)
    {
        var s = Aufbereiten(roh);
        return s.Length == 0 || ".?!:".Contains(s[^1]) ? s : s + ".";
    }

    private static bool IstZahlBaustein(string w)
    {
        var l = w.ToLowerInvariant();
        return l is "hundert" or "tausend" || ParseZahlwort(l) is not null;
    }

    private static int? ZahlAusFolge(List<string> folge)
    {
        // Am einfachsten: alles zusammen ist ein Zahlwort ("ein tausend dreihundert ...").
        var zusammen = ParseZahlwort(string.Concat(folge));
        if (zusammen is not null) return zusammen;

        // "fünfzehn achtundsiebzig" (so spricht man § 1578) -> 1578
        if (folge.Count == 2 && ParseZahlwort(folge[0]) is >= 10 and <= 99 and var a
            && ParseZahlwort(folge[1]) is >= 1 and <= 99 and var b)
            return a * 100 + b;

        return null;
    }
}
