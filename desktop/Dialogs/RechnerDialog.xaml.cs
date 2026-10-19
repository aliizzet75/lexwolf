using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using LexWolf.Database;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>Unterhalt, Zugewinn, Verfahrenswert, RVG — fest programmiert, rechnet bei jeder Eingabe neu.</summary>
public partial class RechnerDialog : Window
{
    public sealed class KindZeile
    {
        public string Name { get; set; } = "";
        public string Geburt { get; set; } = "";
        public bool EigenerHausstand { get; set; }
    }

    public sealed class TatbestandZeile
    {
        public required KanzleiRechner.Gebuehrentatbestand Tatbestand { get; init; }
        public bool Aktiv { get; set; }
        public string Anzeige => $"{Tatbestand.Satz.ToString("0.0", CultureInfo.GetCultureInfo("de-DE"))} {Tatbestand.Bezeichnung} ({Tatbestand.Vv})";
    }

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private readonly Rechenwerte _werte = Rechenwerte.Laden();
    private readonly ObservableCollection<KindZeile> _kinder = new();
    private readonly List<TatbestandZeile> _tatbestaende;
    private bool _berechnungGeplant;

    /// <summary>Gesetzt, wenn "In den Chat übernehmen" geklickt wurde.</summary>
    public string? InChatUebernehmen { get; private set; }

    // Herkunft der Werte (nur mit Mandant): Rechnerfelder kommen aus dem Fragebogen, eigene
    // Änderungen im Rechner werden als Eingabe des Anwalts gespeichert (Formular "rechner").
    private const string Formular = "rechner";
    private sealed record RFeld(string Key, FeldTyp Typ, Control Ctrl, Func<string?>? Quelle, string Standard);
    private readonly LocalDb? _db;
    private readonly string? _mandantId;
    private readonly string _mandantName;
    private readonly FormularAusfueller? _ausfueller;
    private Formularwerte _fb = new();
    private Formularwerte _eigen = new();
    private List<RFeld> _rfelder = new();
    private bool _zeigeAn;
    private bool _eigenGeaendert;

    public RechnerDialog(Fragebogen f, string? mandantName, string uebernehmenText = "💬 In den Chat übernehmen",
        LocalDb? db = null, string? mandantId = null, string? backendUrl = null)
    {
        InitializeComponent();
        FensterGroesse.AnSchriftAnpassen(this);
        _db = db;
        _mandantId = mandantId;
        _mandantName = mandantName ?? "";
        if (db is not null && mandantId is not null && backendUrl is not null)
            _ausfueller = new FormularAusfueller(db, backendUrl);
        UebernehmenBtn.Content = uebernehmenText;
        if (!string.IsNullOrEmpty(mandantName))
        {
            UntertitelText.Text = mandantName;
            UntertitelText.Visibility = Visibility.Visible;
        }

        StandText.Text = $"{_werte.DuesseldorferTabelleStand} · Kindergeld {KanzleiRechner.Euro(_werte.Kindergeld)} · {_werte.RvgStand}" +
                         (_werte.DuesseldorferTabelleJahr < DateTime.Today.Year
                             ? $"  ·  ⚠ Werte älter als {DateTime.Today.Year} — bitte unter „Tabellenwerte“ aktualisieren"
                             : "");
        StandText.ToolTip = $"{_werte.DuesseldorferTabelleStand}\nKindergeld {KanzleiRechner.Euro(_werte.Kindergeld)}\n{_werte.RvgStand}\n{_werte.GerichtStand}";
        if (_werte.DuesseldorferTabelleJahr < DateTime.Today.Year)
            StandText.Foreground = (System.Windows.Media.Brush)FindResource("LwOrange");

        UStichtag.Text = DatumEingabe.Format(DateTime.Today);
        KinderListe.ItemsSource = _kinder;
        _tatbestaende = KanzleiRechner.Tatbestaende
            .Select(t => new TatbestandZeile { Tatbestand = t, Aktiv = t.Vv is "VV 3100" or "VV 3104" }).ToList();
        RTatbestaende.ItemsSource = _tatbestaende;
        RVerfahren.ItemsSource = KanzleiRechner.Gerichtsverfahrensarten;
        RVerfahren.SelectedIndex = 0;

        if (_ausfueller is not null)
        {
            HerkunftEinrichten();
            Legende.Visibility = AusfuellenBtn.Visibility = Visibility.Visible;
        }
        else VorbelegenAus(f);

        AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => PlaneBerechnung()));
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => PlaneBerechnung()));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => PlaneBerechnung()));
        Loaded += async (_, _) =>
        {
            Berechnen();
            if (_ausfueller is not null && !_ausfueller.IstAktuell(_mandantId!)) await AusAkteLesenAsync(automatisch: true);
        };
        Closing += (_, _) =>
        {
            if (_eigenGeaendert && _db is not null && _mandantId is not null)
                _db.SaveFormularFelder(_mandantId, Formular, _eigen.Felder);
        };
    }

    // ------------------------------------------------------------ Herkunft der Werte

    private void HerkunftEinrichten()
    {
        bool Pflichtig() => _fb.Wert("mandant_ist_unterhaltspflichtig") == "true";
        _rfelder = new List<RFeld>
        {
            new("u_netto", FeldTyp.Betrag, UNetto, () => _fb.Wert(Pflichtig() ? "netto_mandant" : "netto_gegenseite"), ""),
            new("u_abzug", FeldTyp.Betrag, UAbzug, () => _fb.Wert(Pflichtig() ? "abzuege_mandant" : "abzuege_gegenseite"), "0"),
            new("u_erwerb", FeldTyp.JaNein, UErwerb, () => _fb.Wert(Pflichtig() ? "mandant_erwerbstaetig" : "gegenseite_erwerbstaetig"), "true"),
            new("u_netto_berechtigt", FeldTyp.Betrag, UNettoBerechtigt, () => _fb.Wert(Pflichtig() ? "netto_gegenseite" : "netto_mandant"), "0"),
            new("u_berechtigt_erwerb", FeldTyp.JaNein, UBerechtigtErwerb, () => _fb.Wert(Pflichtig() ? "gegenseite_erwerbstaetig" : "mandant_erwerbstaetig"), "true"),
            new("z_anfang_m", FeldTyp.Betrag, ZAnfangM, () => _fb.Wert("anfangsvermoegen_mandant"), "0"),
            new("z_priv_m", FeldTyp.Betrag, ZPrivM, () => _fb.Wert("privilegiert_mandant"), "0"),
            new("z_ende_m", FeldTyp.Betrag, ZEndeM, () => _fb.Wert("endvermoegen_mandant"), "0"),
            new("z_anfang_g", FeldTyp.Betrag, ZAnfangG, () => _fb.Wert("anfangsvermoegen_gegenseite"), "0"),
            new("z_priv_g", FeldTyp.Betrag, ZPrivG, () => _fb.Wert("privilegiert_gegenseite"), "0"),
            new("z_ende_g", FeldTyp.Betrag, ZEndeG, () => _fb.Wert("endvermoegen_gegenseite"), "0"),
            new("v_netto", FeldTyp.Betrag, VNetto, NettoBeider, "0"),
            new("v_anrechte", FeldTyp.Ganzzahl, VAnrechte, () => _fb.Wert("anzahl_rentenanrechte"), "2"),
        };
        foreach (var rf in _rfelder)
        {
            if (rf.Ctrl is TextBox tb) tb.TextChanged += (_, _) => OnRFeldGeaendert(rf);
            else if (rf.Ctrl is CheckBox cb) cb.Click += (_, _) => OnRFeldGeaendert(rf);
        }
        _eigen = new Formularwerte(_db!.GetFormularFelder(_mandantId!, Formular));
        FragebogenLaden();
    }

    private string? NettoBeider()
    {
        var m = _fb.Wert("netto_mandant");
        var g = _fb.Wert("netto_gegenseite");
        if (m is null && g is null) return null;
        static decimal Z(string? s) => decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0;
        return (Z(m) + Z(g)).ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>Woher stammt der Wert im Fragebogen, aus dem das Rechnerfeld gefüllt wird?</summary>
    private Herkunft QuellHerkunft(RFeld rf)
    {
        bool Pflichtig() => _fb.Wert("mandant_ist_unterhaltspflichtig") == "true";
        string[] keys = rf.Key switch
        {
            "u_netto" => new[] { Pflichtig() ? "netto_mandant" : "netto_gegenseite" },
            "u_abzug" => new[] { Pflichtig() ? "abzuege_mandant" : "abzuege_gegenseite" },
            "u_erwerb" => new[] { Pflichtig() ? "mandant_erwerbstaetig" : "gegenseite_erwerbstaetig" },
            "u_netto_berechtigt" => new[] { Pflichtig() ? "netto_gegenseite" : "netto_mandant" },
            "u_berechtigt_erwerb" => new[] { Pflichtig() ? "gegenseite_erwerbstaetig" : "mandant_erwerbstaetig" },
            "z_anfang_m" => new[] { "anfangsvermoegen_mandant" }, "z_priv_m" => new[] { "privilegiert_mandant" },
            "z_ende_m" => new[] { "endvermoegen_mandant" }, "z_anfang_g" => new[] { "anfangsvermoegen_gegenseite" },
            "z_priv_g" => new[] { "privilegiert_gegenseite" }, "z_ende_g" => new[] { "endvermoegen_gegenseite" },
            "v_netto" => new[] { "netto_mandant", "netto_gegenseite" },
            "v_anrechte" => new[] { "anzahl_rentenanrechte" },
            _ => Array.Empty<string>(),
        };
        var h = keys.Select(_fb.HerkunftVon).ToList();
        return h.Contains(Herkunft.Anwalt) ? Herkunft.Anwalt : h.Contains(Herkunft.LexWolf) ? Herkunft.LexWolf : Herkunft.Leer;
    }

    private void FragebogenLaden()
    {
        _fb = _ausfueller!.LadeFragebogen(_mandantId!);
        // Basiswert jedes Rechnerfelds = Wert aus dem Fragebogen; eigene Änderungen bleiben darüber.
        foreach (var rf in _rfelder)
        {
            if (!_eigen.Felder.TryGetValue(rf.Key, out var fw)) _eigen.Felder[rf.Key] = fw = new FeldWert();
            fw.Ki = rf.Quelle?.Invoke();
            fw.KiQuelle = "Fragebogen";
            if (fw.Anwalt is not null && fw.Anwalt == fw.Ki) fw.Anwalt = null;
        }
        RFelderAnzeigen();

        _kinder.Clear();
        foreach (var k in Formularwerte.KinderAus(_fb.Wert("kinder")))
            _kinder.Add(new KindZeile { Name = k.Name, Geburt = k.Geburtsdatum is { } g ? DatumEingabe.Format(g) : "" });
        var hk = _fb.HerkunftVon("kinder");
        KinderHerkunft.Visibility = hk == Herkunft.Leer ? Visibility.Collapsed : Visibility.Visible;
        KinderHerkunft.Text = hk == Herkunft.LexWolf ? "● von LexWolf aus der Akte" : "● aus dem Fragebogen (von Ihnen)";
        KinderHerkunft.Foreground = (System.Windows.Media.Brush)FindResource(hk == Herkunft.LexWolf ? "LwHerkunftLexWolfText" : "LwHerkunftAnwaltText");
    }

    private void RFelderAnzeigen()
    {
        _zeigeAn = true;
        try
        {
            foreach (var rf in _rfelder)
            {
                var wert = _eigen.Wert(rf.Key);
                // Ohne Wert: Vorgabe des Rechners (z. B. 0) — außer der Anwalt hat das Feld bewusst geleert.
                var geleert = _eigen.Info(rf.Key)?.Anwalt == "";
                if (rf.Ctrl is TextBox tb)
                    tb.Text = Formularwerte.ZurAnzeige(rf.Typ, wert ?? (geleert ? null : rf.Standard));
                else if (rf.Ctrl is CheckBox cb)
                    cb.IsChecked = (wert ?? rf.Standard) == "true";
                RMarkiere(rf);
            }
        }
        finally
        {
            _zeigeAn = false;
        }
    }

    private void RMarkiere(RFeld rf)
    {
        var h = _eigen.HerkunftVon(rf.Key);
        var anzeige = h == Herkunft.Anwalt ? Herkunft.Anwalt : h == Herkunft.LexWolf ? QuellHerkunft(rf) : Herkunft.Leer;
        rf.Ctrl.Tag = anzeige switch { Herkunft.LexWolf => "lexwolf", Herkunft.Anwalt => "anwalt", _ => null };
        rf.Ctrl.ToolTip = anzeige switch
        {
            Herkunft.LexWolf => "Von LexWolf aus der Akte (über den Fragebogen) — bitte prüfen.",
            Herkunft.Anwalt when h == Herkunft.Anwalt && _eigen.Info(rf.Key)?.Ki is { } basis =>
                $"Im Rechner von Ihnen geändert. Fragebogen: {Formularwerte.ZurAnzeige(rf.Typ, basis)}",
            Herkunft.Anwalt => "Von Ihnen eingegeben.",
            _ => null,
        };
    }

    private void OnRFeldGeaendert(RFeld rf)
    {
        if (_zeigeAn) return;
        string? wert;
        if (rf.Ctrl is TextBox tb)
        {
            wert = Formularwerte.AusEingabe(rf.Typ, tb.Text);
            if (wert is null && tb.Text.Trim().Length > 0) return;
        }
        else wert = ((CheckBox)rf.Ctrl).IsChecked == true ? "true" : "false";
        _eigen.Eingabe(rf.Key, wert);
        _eigenGeaendert = true;
        RMarkiere(rf);
    }

    private void OnLexWolfAusfuellen(object sender, RoutedEventArgs e) => _ = AusAkteLesenAsync(automatisch: false);

    /// <summary>Akte lesen (über den Fragebogen) und Rechnerfelder neu füllen. Auf Knopfdruck werden
    /// auch im Rechner geleerte Felder wieder gefüllt; eigene Werte bleiben.</summary>
    private async System.Threading.Tasks.Task AusAkteLesenAsync(bool automatisch)
    {
        if (_ausfueller is null || _mandantId is null) return;
        AusfuellenBtn.IsEnabled = false;
        StatusText.Text = "⏳ LexWolf liest die Akte …";
        try
        {
            var n = 0;
            if (_ausfueller.HatDokumente(_mandantId) && (!automatisch || !_ausfueller.IstAktuell(_mandantId)))
            {
                var neu = await _ausfueller.ErmittelnAsync(_mandantId, _mandantName);
                if (neu is not null)
                {
                    var fb = _ausfueller.LadeFragebogen(_mandantId);
                    n = fb.KiUebernehmen(neu);
                    _ausfueller.Speichern(_mandantId, fb);
                }
            }
            if (!automatisch)
            {
                n += _eigen.LeereAusfuellen();
                _eigenGeaendert = true;
            }
            FragebogenLaden();
            PlaneBerechnung();
            StatusText.Text = n > 0 ? $"✓ {n} Angabe(n) aus der Akte übernommen — orange markiert, bitte prüfen."
                : "✓ Akte gelesen — keine neuen Angaben.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Akte konnte nicht gelesen werden: {ex.Message}";
        }
        finally
        {
            AusfuellenBtn.IsEnabled = true;
        }
    }

    private void VorbelegenAus(Fragebogen f)
    {
        string Z(decimal? d) => d is { } v ? v.ToString("0.##", De) : "0";
        if (f.NettoPflichtiger is { } np) UNetto.Text = Z(np);
        UErwerb.IsChecked = f.PflichtigerErwerbstaetig;
        UAbzug.Text = Z(f.AbzuegePflichtiger);
        foreach (var k in f.Kinder)
            _kinder.Add(new KindZeile { Name = k.Name, Geburt = k.Geburtsdatum is { } g ? DatumEingabe.Format(g) : "" });
        if (f.NettoBerechtigter is { } nb)
        {
            UNettoBerechtigt.Text = Z(nb);
            UBerechtigtErwerb.IsChecked = f.BerechtigterErwerbstaetig;
        }

        ZAnfangM.Text = Z(f.AnfangsvermoegenMandant);
        ZPrivM.Text = Z(f.PrivilegiertMandant);
        ZEndeM.Text = Z(f.EndvermoegenMandant);
        ZAnfangG.Text = Z(f.AnfangsvermoegenGegenseite);
        ZPrivG.Text = Z(f.PrivilegiertGegenseite);
        ZEndeG.Text = Z(f.EndvermoegenGegenseite);

        VNetto.Text = Z((f.NettoMandant ?? 0) + (f.NettoGegenseite ?? 0));
        if (f.AnzahlRentenanrechte is { } a) VAnrechte.Text = a.ToString(De);
    }

    /// <summary>"3.200,50", "3200", "3200.5" → Zahl; leer/ungültig → 0.</summary>
    private static decimal Zahl(TextBox box) => Formularwerte.Betrag(box.Text) ?? 0;

    private void PlaneBerechnung()
    {
        if (_berechnungGeplant || !IsLoaded) return;
        _berechnungGeplant = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _berechnungGeplant = false;
            Berechnen();
        });
    }

    private void OnNeuRechnen(object sender, SelectionChangedEventArgs e) => PlaneBerechnung();
    private void OnTabGewechselt(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == Tabs) PlaneBerechnung();
    }

    private void Berechnen()
    {
        try
        {
            switch (Tabs.SelectedIndex)
            {
                case 0: BerechneUnterhalt(); break;
                case 1: BerechneZugewinn(); break;
                case 2: BerechneVerfahrenswert(); break;
                default: BerechneRvg(); break;
            }
        }
        catch (Exception ex)
        {
            ErgebnisText.Text = "Eingaben prüfen";
            RechenwegBox.Text = ex.Message;
        }
    }

    private void BerechneUnterhalt()
    {
        UEhegattePanel.IsEnabled = UEhegatte.IsChecked == true;
        var stichtag = DatumEingabe.Parse(UStichtag.Text) ?? DateTime.Today;
        var kinder = _kinder
            .Select((k, i) => (k, geb: DatumEingabe.Parse(k.Geburt)))
            .Where(x => x.geb is not null)
            .Select((x, i) => new Kind(x.k.Name.Length > 0 ? x.k.Name : $"Kind {i + 1}", x.geb!.Value, x.k.EigenerHausstand))
            .ToList();
        var stufung = int.Parse((string)((ComboBoxItem)UStufung.SelectedItem).Tag, CultureInfo.InvariantCulture);
        var r = KanzleiRechner.Unterhalt(Zahl(UNetto), UErwerb.IsChecked == true, Zahl(UAbzug), kinder, stichtag, _werte, stufung,
            UEhegatte.IsChecked == true ? Zahl(UNettoBerechtigt) : null, UBerechtigtErwerb.IsChecked == true, USuedL.IsChecked == true);

        var teile = new List<string>();
        if (r.Kinder.Count > 0) teile.Add($"Kindesunterhalt gesamt {KanzleiRechner.Euro(r.SummeKindesunterhalt)}" + (r.Mangelfall ? " (Mangelfall)" : ""));
        if (r.Ehegattenunterhalt is { } eu) teile.Add($"Ehegattenunterhalt {KanzleiRechner.Euro(eu)}");
        ErgebnisText.Text = teile.Count > 0 ? string.Join("\n", teile) + " monatlich" : $"Einkommensgruppe {r.Einkommensgruppe} — Kinder oder Ehegattenunterhalt angeben";
        var fehlend = _kinder.Count(k => DatumEingabe.Parse(k.Geburt) is null);
        RechenwegBox.Text = r.Rechenweg + (fehlend > 0 ? $"\n\nHinweis: {fehlend} Kind(er) ohne gültiges Geburtsdatum nicht berücksichtigt." : "");
    }

    private void BerechneZugewinn()
    {
        var (ausgleich, berechtigt, weg) = KanzleiRechner.Zugewinn(
            new KanzleiRechner.ZugewinnSeite("Mandant", Zahl(ZAnfangM), Zahl(ZPrivM), Zahl(ZEndeM)),
            new KanzleiRechner.ZugewinnSeite("Gegenseite", Zahl(ZAnfangG), Zahl(ZPrivG), Zahl(ZEndeG)),
            Zahl(ZIndex) is var i && i > 0 ? i : 1m);
        ErgebnisText.Text = ausgleich > 0 ? $"Zugewinnausgleich {KanzleiRechner.Euro(ausgleich)}\nzugunsten {berechtigt}" : "Kein Zugewinnausgleich";
        RechenwegBox.Text = weg;
    }

    private decimal VerfahrenswertSumme(out string weg)
    {
        var posten = new List<KanzleiRechner.VerfahrenswertPosten>();
        KanzleiRechner.VerfahrenswertPosten? ehe = null;
        if (VScheidung.IsChecked == true) posten.Add(ehe = KanzleiRechner.Ehesache(Zahl(VNetto), Zahl(VKinderAbzug)));
        if (VVa.IsChecked == true)
            posten.Add(KanzleiRechner.Versorgungsausgleich(ehe?.Wert ?? KanzleiRechner.Ehesache(Zahl(VNetto), Zahl(VKinderAbzug)).Wert,
                (int)Zahl(VAnrechte)));
        if (VUnterhalt.IsChecked == true) posten.Add(KanzleiRechner.Unterhaltssache(Zahl(VUMonat), Zahl(VURueck)));
        if (VZugewinn.IsChecked == true) posten.Add(new("Zugewinn", Zahl(VZForderung), "§ 35 FamGKG: Höhe der Forderung"));
        if (VSorge.IsChecked == true) posten.Add(KanzleiRechner.Kindschaftssache());
        var summe = posten.Sum(p => p.Wert);
        weg = string.Join("\n", posten.Select(p => $"{p.Bezeichnung}: {KanzleiRechner.Euro(p.Wert)}  ({p.Grundlage})")) +
              (posten.Count > 1 ? $"\nGesamtwert (Verbund, § 44 FamGKG): {KanzleiRechner.Euro(summe)}" : "");
        return summe;
    }

    private void BerechneVerfahrenswert()
    {
        var summe = VerfahrenswertSumme(out var weg);
        ErgebnisText.Text = $"Verfahrenswert {KanzleiRechner.Euro(summe)}";
        RechenwegBox.Text = weg.Length > 0 ? weg : "Bitte mindestens einen Verfahrensgegenstand wählen.";
    }

    private void BerechneRvg()
    {
        var wert = Zahl(RWert);
        var gebuehren = _tatbestaende.Where(t => t.Aktiv).Select(t => t.Tatbestand).ToList();
        var (_, brutto, weg) = KanzleiRechner.Rvg(wert, gebuehren, _werte, RPauschale.IsChecked == true);
        if (RRisiko.IsChecked != true || RVerfahren.SelectedItem is not KanzleiRechner.Gerichtsverfahren verfahren)
        {
            ErgebnisText.Text = $"Anwaltskosten {KanzleiRechner.Euro(brutto)} brutto";
            RechenwegBox.Text = weg;
            return;
        }
        var (gericht, gerichtWeg) = KanzleiRechner.Gerichtskosten(wert, verfahren, RErmaessigt.IsChecked == true, _werte);
        var risiko = 2 * brutto + gericht;
        ErgebnisText.Text = $"Anwaltskosten {KanzleiRechner.Euro(brutto)} brutto\nKostenrisiko gesamt {KanzleiRechner.Euro(risiko)}";
        RechenwegBox.Text = weg +
            $"\n\n{gerichtWeg}" +
            $"\n\nKostenrisiko bei vollem Unterliegen:" +
            $"\n  eigene Anwaltskosten      {KanzleiRechner.Euro(brutto)}" +
            $"\n  gegnerische Anwaltskosten {KanzleiRechner.Euro(brutto)}" +
            $"\n  Gerichtskosten            {KanzleiRechner.Euro(gericht)}" +
            $"\n  gesamt                    {KanzleiRechner.Euro(risiko)}" +
            "\n(ohne Auslagen des Gerichts, z. B. Sachverständige; gleiche Gebühren auf beiden Seiten angenommen)";
    }

    private void OnKindNeu(object sender, RoutedEventArgs e)
    {
        _kinder.Add(new KindZeile());
        PlaneBerechnung();
    }

    private void OnKindEntfernen(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is KindZeile k) _kinder.Remove(k);
        PlaneBerechnung();
    }

    private void OnWertZuRvg(object sender, RoutedEventArgs e)
    {
        RWert.Text = VerfahrenswertSumme(out _).ToString("0.##", De);
        Tabs.SelectedIndex = 3;
    }

    private void OnWerteBearbeiten(object sender, RoutedEventArgs e)
    {
        var pfad = Rechenwerte.DateiSicherstellen();
        MessageBox.Show(this,
            "Die Tabellenwerte werden jetzt im Editor geöffnet. Nach dem Speichern den Rechner neu öffnen.\n\n" +
            "Bitte nur Zahlen ändern und das Format beibehalten.", "Tabellenwerte", MessageBoxButton.OK, MessageBoxImage.Information);
        try { System.Diagnostics.Process.Start("notepad.exe", $"\"{pfad}\""); }
        catch { DokumentExport.OeffneMitStandardprogramm(pfad); }
    }

    private void OnKopieren(object sender, RoutedEventArgs e) =>
        Clipboard.SetText($"{ErgebnisText.Text}\n\n{RechenwegBox.Text}");

    private void OnInChat(object sender, RoutedEventArgs e)
    {
        InChatUebernehmen = $"Berechnung ({((TabItem)Tabs.SelectedItem).Header}):\n{RechenwegBox.Text}";
        Close();
    }

    private void OnSchliessen(object sender, RoutedEventArgs e) => Close();
}
