using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LexWolf.Database;

namespace LexWolf.Services;

/// <summary>
/// Druckbare Fristenliste (HTML) für die tägliche Fristenkontrolle im Büro:
/// alle offenen Fristen, deren Ende oder Vorfrist in den nächsten Tagen liegt,
/// plus Überfälliges — mit Spalte zum Abzeichnen.
/// </summary>
public static class FristenListe
{
    public static string BaueHtml(IEnumerable<FristEintrag> fristen, IReadOnlyDictionary<string, string> mandanten,
        DateTime heute, int tage = 14)
    {
        heute = heute.Date;
        var bis = heute.AddDays(tage);
        var alle = fristen.ToList();
        var liste = alle
            .Where(f => f.Status == "offen" && (f.Ende <= bis || f.Vorfrist <= bis))
            .OrderBy(f => f.Ende).ThenBy(f => Name(mandanten, f.MandantId))
            .ToList();
        var vorschlaege = alle.Count(f => f.Status == "vorschlag");

        static string H(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"de\"><head><meta charset=\"utf-8\"><title>Fristenliste ")
          .Append(DatumEingabe.Format(heute)).Append("</title><style>")
          .Append("body{font-family:Segoe UI,Arial,sans-serif;font-size:11pt;color:#111;margin:24px}")
          .Append("h1{font-size:16pt;margin:0 0 4px}p{margin:2px 0 12px;color:#444}")
          .Append("table{border-collapse:collapse;width:100%}th,td{border:1px solid #999;padding:5px 7px;text-align:left;vertical-align:top}")
          .Append("th{background:#eee}tr.ueberfaellig td{background:#fde2e1}tr.dringend td{background:#fff3cd}")
          .Append(".notfrist{font-weight:bold;color:#b00020}.leer{width:90px}.warn{color:#b00020;font-weight:bold}")
          .Append("@media print{body{margin:0}button{display:none}}</style></head><body>");
        sb.Append("<button onclick=\"window.print()\" style=\"float:right;padding:6px 14px\">Drucken</button>");
        sb.Append("<h1>Fristenliste</h1><p>Stand ").Append(H(DatumEingabe.Lang(heute)))
          .Append(" · offene Fristen mit Ende oder Vorfrist bis ").Append(H(DatumEingabe.Format(bis)))
          .Append(" · ").Append(liste.Count).Append(liste.Count == 1 ? " Frist" : " Fristen").Append("</p>");
        if (vorschlaege > 0)
            sb.Append("<p class=\"warn\">").Append(vorschlaege)
              .Append(vorschlaege == 1 ? " Fristvorschlag ist" : " Fristvorschläge sind")
              .Append(" noch nicht geprüft und stehen nicht in dieser Liste.</p>");

        if (liste.Count == 0)
        {
            sb.Append("<p>Keine offenen Fristen in diesem Zeitraum.</p>");
        }
        else
        {
            sb.Append("<table><thead><tr><th>Fristende</th><th>Mandant</th><th>Frist</th><th>Rechtsgrundlage</th>")
              .Append("<th>Vorfrist</th><th class=\"leer\">Erledigt / Kürzel</th></tr></thead><tbody>");
            foreach (var f in liste)
            {
                var klasse = f.Ende < heute ? "ueberfaellig" : f.Ende <= heute.AddDays(1) ? "dringend" : "";
                sb.Append("<tr class=\"").Append(klasse).Append("\"><td>").Append(H(DatumEingabe.Lang(f.Ende)));
                if (f.Ende < heute) sb.Append("<br><span class=\"warn\">überfällig</span>");
                sb.Append("</td><td>").Append(H(Name(mandanten, f.MandantId)))
                  .Append("</td><td>").Append(H(f.Bezeichnung));
                if (f.Notfrist) sb.Append(" <span class=\"notfrist\">NOTFRIST</span>");
                sb.Append("</td><td>").Append(H(f.Rechtsgrundlage))
                  .Append("</td><td>").Append(f.Vorfrist < f.Ende ? H(DatumEingabe.Format(f.Vorfrist)) : "—")
                  .Append("</td><td></td></tr>");
            }
            sb.Append("</tbody></table>");
        }
        sb.Append("<p style=\"margin-top:16px;font-size:9pt\">Erstellt mit LexWolf. Fristen vor Ablauf anhand der Akte prüfen.</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string Name(IReadOnlyDictionary<string, string> mandanten, string id) =>
        mandanten.TryGetValue(id, out var n) ? n : "(unbekannter Mandant)";
}
