using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LexWolf.Services;

public sealed class FragebogenKind
{
    public string Name { get; set; } = "";
    public DateTime? Geburtsdatum { get; set; }
    public bool LebtBeiMandant { get; set; } = true;
}

/// <summary>
/// Mandanten-Fragebogen (Familienrecht): wird im Gespräch ausgefüllt oder als
/// Word-Datei an den Mandanten geschickt; die Werte füllen die Rechner vor.
/// "Mandant" / "Gegenseite" statt "Pflichtiger" / "Berechtigter", weil die
/// Rolle je nach Verfahren wechselt.
/// </summary>
public sealed class Fragebogen
{
    public bool MandantIstUnterhaltspflichtig { get; set; }
    public decimal? NettoMandant { get; set; }
    public decimal? NettoGegenseite { get; set; }
    public bool MandantErwerbstaetig { get; set; } = true;
    public bool GegenseiteErwerbstaetig { get; set; } = true;
    public decimal? AbzuegeMandant { get; set; }
    public decimal? AbzuegeGegenseite { get; set; }
    public List<FragebogenKind> Kinder { get; set; } = new();

    public DateTime? Heiratsdatum { get; set; }
    public DateTime? Trennungsdatum { get; set; }
    public DateTime? ZustellungScheidungsantrag { get; set; }

    public decimal? AnfangsvermoegenMandant { get; set; }
    public decimal? EndvermoegenMandant { get; set; }
    public decimal? PrivilegiertMandant { get; set; }
    public decimal? AnfangsvermoegenGegenseite { get; set; }
    public decimal? EndvermoegenGegenseite { get; set; }
    public decimal? PrivilegiertGegenseite { get; set; }
    public int? AnzahlRentenanrechte { get; set; }

    public string Wohnsituation { get; set; } = "";
    public string Bemerkungen { get; set; } = "";

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = false };
    public string ToJson() => JsonSerializer.Serialize(this, Opts);

    public static Fragebogen FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Fragebogen();
        try { return JsonSerializer.Deserialize<Fragebogen>(json) ?? new Fragebogen(); }
        catch { return new Fragebogen(); }
    }

    /// <summary>Übernimmt die von der KI ausgelesenen Angaben (Backend /akte/fragebogen-lesen).
    /// Nur vorhandene Werte werden gesetzt; Kinder werden ergänzt, nicht doppelt angelegt.
    /// Gibt die Anzahl übernommener Angaben zurück.</summary>
    public int UebernehmeAus(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var n = 0;

        decimal? Z(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;
        bool? B(string k) => r.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
        string? S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && v.GetString()!.Trim().Length > 0 ? v.GetString()!.Trim() : null;
        DateTime? D(string k) => S(k) is { } s ? DokumentAnalyse.ParseDatum(s) : null;
        void Setze<T>(T? wert, Action<T> setzen) where T : struct { if (wert is { } w) { setzen(w); n++; } }

        Setze(Z("netto_mandant"), v => NettoMandant = v);
        Setze(Z("netto_gegenseite"), v => NettoGegenseite = v);
        Setze(B("mandant_erwerbstaetig"), v => MandantErwerbstaetig = v);
        Setze(B("gegenseite_erwerbstaetig"), v => GegenseiteErwerbstaetig = v);
        Setze(Z("abzuege_mandant"), v => AbzuegeMandant = v);
        Setze(Z("abzuege_gegenseite"), v => AbzuegeGegenseite = v);
        Setze(D("heiratsdatum"), v => Heiratsdatum = v);
        Setze(D("trennungsdatum"), v => Trennungsdatum = v);
        Setze(Z("anfangsvermoegen_mandant"), v => AnfangsvermoegenMandant = v);
        Setze(Z("privilegiert_mandant"), v => PrivilegiertMandant = v);
        Setze(Z("endvermoegen_mandant"), v => EndvermoegenMandant = v);
        Setze(Z("anfangsvermoegen_gegenseite"), v => AnfangsvermoegenGegenseite = v);
        Setze(Z("privilegiert_gegenseite"), v => PrivilegiertGegenseite = v);
        Setze(Z("endvermoegen_gegenseite"), v => EndvermoegenGegenseite = v);
        Setze(Z("anzahl_rentenanrechte"), v => AnzahlRentenanrechte = (int)v);
        if (S("wohnsituation") is { } w) { Wohnsituation = w; n++; }
        if (S("bemerkungen") is { } b) { Bemerkungen = Bemerkungen.Length > 0 ? $"{Bemerkungen}\n{b}" : b; n++; }

        if (r.TryGetProperty("kinder", out var kinder) && kinder.ValueKind == JsonValueKind.Array)
            foreach (var k in kinder.EnumerateArray())
            {
                var name = k.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString()!.Trim() : "";
                var geb = k.TryGetProperty("geburtsdatum", out var g) && g.ValueKind == JsonValueKind.String ? DokumentAnalyse.ParseDatum(g.GetString()!) : null;
                if (name.Length == 0 && geb is null) continue;
                var bei = !(k.TryGetProperty("lebt_bei_mandant", out var lb) && lb.ValueKind == JsonValueKind.False);
                var vorhanden = Kinder.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && name.Length > 0);
                if (vorhanden is not null)
                {
                    vorhanden.Geburtsdatum ??= geb;
                    vorhanden.LebtBeiMandant = bei;
                }
                else Kinder.Add(new FragebogenKind { Name = name, Geburtsdatum = geb, LebtBeiMandant = bei });
                n++;
            }
        return n;
    }

    /// <summary>Netto des Unterhaltspflichtigen bzw. -berechtigten, je nach Rolle des Mandanten.</summary>
    public decimal? NettoPflichtiger => MandantIstUnterhaltspflichtig ? NettoMandant : NettoGegenseite;
    public decimal? NettoBerechtigter => MandantIstUnterhaltspflichtig ? NettoGegenseite : NettoMandant;
    public bool PflichtigerErwerbstaetig => MandantIstUnterhaltspflichtig ? MandantErwerbstaetig : GegenseiteErwerbstaetig;
    public bool BerechtigterErwerbstaetig => MandantIstUnterhaltspflichtig ? GegenseiteErwerbstaetig : MandantErwerbstaetig;
    public decimal? AbzuegePflichtiger => MandantIstUnterhaltspflichtig ? AbzuegeMandant : AbzuegeGegenseite;
}
