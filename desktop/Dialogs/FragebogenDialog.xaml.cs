using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LexWolf.Database;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>
/// Mandanten-Fragebogen (Familienrecht). LexWolf füllt ihn aus den Dokumenten der Akte; was der
/// Anwalt einträgt, hat Vorrang. Jedes Feld zeigt farbig, woher sein Wert stammt (Legende im Kopf),
/// beides wird getrennt gespeichert. Geleerte Felder füllt „✨ LexWolf ausfüllen“ wieder.
/// </summary>
public partial class FragebogenDialog : Window
{
    public sealed class KindZeile
    {
        public string Name { get; set; } = "";
        public string Geburt { get; set; } = "";
        public bool BeiMandant { get; set; } = true;
    }

    private sealed record Feld(string Key, FeldTyp Typ, Control Ctrl);

    private readonly LocalDb _db;
    private readonly string _mandantId;
    private readonly string _mandantName;
    private readonly string _dokumentePfad;
    private readonly string _backendUrl;
    private readonly FormularAusfueller _ausfueller;
    private readonly ObservableCollection<KindZeile> _kinder = new();
    private readonly List<Feld> _felder;
    private Formularwerte _werte;
    private bool _zeigeAn;
    private bool _geaendert;

    public FragebogenDialog(LocalDb db, string backendUrl, string mandantId, string mandantName, string dokumentePfad)
    {
        InitializeComponent();
        FensterGroesse.AnSchriftAnpassen(this);
        _db = db;
        _backendUrl = backendUrl;
        _mandantId = mandantId;
        _mandantName = mandantName;
        _dokumentePfad = dokumentePfad;
        _ausfueller = new FormularAusfueller(db, backendUrl);
        UntertitelText.Text = mandantName;
        KinderListe.ItemsSource = _kinder;

        _felder = new List<Feld>
        {
            new("netto_mandant", FeldTyp.Betrag, NettoM), new("netto_gegenseite", FeldTyp.Betrag, NettoG),
            new("abzuege_mandant", FeldTyp.Betrag, AbzugM), new("abzuege_gegenseite", FeldTyp.Betrag, AbzugG),
            new("mandant_erwerbstaetig", FeldTyp.JaNein, ErwerbM), new("gegenseite_erwerbstaetig", FeldTyp.JaNein, ErwerbG),
            new("heiratsdatum", FeldTyp.Datum, Heirat), new("trennungsdatum", FeldTyp.Datum, Trennung),
            new("zustellung_scheidungsantrag", FeldTyp.Datum, Zustellung),
            new("anfangsvermoegen_mandant", FeldTyp.Betrag, AnfangM), new("privilegiert_mandant", FeldTyp.Betrag, PrivM),
            new("endvermoegen_mandant", FeldTyp.Betrag, EndeM),
            new("anfangsvermoegen_gegenseite", FeldTyp.Betrag, AnfangG), new("privilegiert_gegenseite", FeldTyp.Betrag, PrivG),
            new("endvermoegen_gegenseite", FeldTyp.Betrag, EndeG),
            new("anzahl_rentenanrechte", FeldTyp.Ganzzahl, Anrechte),
            new("wohnsituation", FeldTyp.Text, Wohnen), new("bemerkungen", FeldTyp.Text, Bemerkungen),
        };
        foreach (var f in _felder)
        {
            switch (f.Ctrl)
            {
                case TextBox tb:
                    tb.TextChanged += (_, _) => OnFeldGeaendert(f);
                    tb.ContextMenu = Kontextmenue(f.Key, mitBearbeiten: true);
                    break;
                case CheckBox cb:
                    cb.Click += (_, _) => OnFeldGeaendert(f);
                    cb.ContextMenu = Kontextmenue(f.Key, mitBearbeiten: false);
                    break;
            }
        }
        RolleBerechtigt.Checked += (_, _) => OnRolleGeaendert();
        RollePflichtig.Checked += (_, _) => OnRolleGeaendert();
        KinderListe.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => OnKinderGeaendert()));
        KinderListe.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => OnKinderGeaendert()));

        _werte = _ausfueller.LadeFragebogen(mandantId);
        Anzeigen();
        Closing += (_, _) => { if (_geaendert) Speichern(); };
        Loaded += async (_, _) =>
        {
            if (!_ausfueller.IstAktuell(_mandantId)) await AusAkteLesenAsync(automatisch: true);
            else ZeigeStatus();
        };
    }

    // ------------------------------------------------------------ Anzeige

    private static string? HerkunftTag(Herkunft h) => h switch
    {
        Herkunft.LexWolf => "lexwolf",
        Herkunft.Anwalt => "anwalt",
        _ => null,
    };

    private string Tooltip(string key, FeldTyp typ)
    {
        var info = _werte.Info(key);
        return _werte.HerkunftVon(key) switch
        {
            Herkunft.LexWolf => $"Von LexWolf aus der Akte: {info?.KiQuelle}\nBitte prüfen — Sie können den Wert jederzeit überschreiben.",
            Herkunft.Anwalt when info?.Ki is { } ki => $"Von Ihnen eingegeben. LexWolf hatte: {Formularwerte.ZurAnzeige(typ, ki)} ({info.KiQuelle})",
            Herkunft.Anwalt => "Von Ihnen eingegeben.",
            _ when info?.Ki is { } ki => $"Geleert. LexWolf hätte: {Formularwerte.ZurAnzeige(typ, ki)} ({info.KiQuelle}) — Rechtsklick → „Von LexWolf ausfüllen“.",
            _ => "Noch leer.",
        };
    }

    private void Markiere(Feld f)
    {
        f.Ctrl.Tag = HerkunftTag(_werte.HerkunftVon(f.Key));
        f.Ctrl.ToolTip = Tooltip(f.Key, f.Typ);
    }

    private void Anzeigen()
    {
        _zeigeAn = true;
        try
        {
            foreach (var f in _felder)
            {
                var wert = _werte.Wert(f.Key);
                switch (f.Ctrl)
                {
                    case TextBox tb: tb.Text = Formularwerte.ZurAnzeige(f.Typ, wert); break;
                    // Ja/Nein ohne Angabe: „erwerbstätig“ ist der Normalfall
                    case CheckBox cb: cb.IsChecked = wert is null ? true : wert == "true"; break;
                }
                Markiere(f);
            }

            var pflichtig = _werte.Wert("mandant_ist_unterhaltspflichtig") == "true";
            RollePflichtig.IsChecked = pflichtig;
            RolleBerechtigt.IsChecked = !pflichtig;
            RolleMarkieren();

            _kinder.Clear();
            foreach (var k in Formularwerte.KinderAus(_werte.Wert("kinder")))
                _kinder.Add(new KindZeile { Name = k.Name, Geburt = k.Geburtsdatum is { } g ? DatumEingabe.Format(g) : "", BeiMandant = k.LebtBeiMandant });
            KinderMarkieren();
        }
        finally
        {
            _zeigeAn = false;
        }
    }

    private void RolleMarkieren()
    {
        var tag = HerkunftTag(_werte.HerkunftVon("mandant_ist_unterhaltspflichtig"));
        RollePflichtig.Tag = RolleBerechtigt.Tag = tag;
        RollePflichtig.ToolTip = RolleBerechtigt.ToolTip = Tooltip("mandant_ist_unterhaltspflichtig", FeldTyp.JaNein);
    }

    private void KinderMarkieren()
    {
        var h = _werte.HerkunftVon("kinder");
        KinderHerkunft.Visibility = h == Herkunft.Leer ? Visibility.Collapsed : Visibility.Visible;
        KinderHerkunft.Text = h == Herkunft.LexWolf ? "● von LexWolf aus der Akte" : "● von Ihnen eingegeben";
        KinderHerkunft.Foreground = (System.Windows.Media.Brush)FindResource(h == Herkunft.LexWolf ? "LwHerkunftLexWolfText" : "LwHerkunftAnwaltText");
        KinderHerkunft.ToolTip = Tooltip("kinder", FeldTyp.Kinder);
        KinderLeer.Visibility = _kinder.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------ Eingaben

    private void OnFeldGeaendert(Feld f)
    {
        if (_zeigeAn) return;
        string? wert;
        if (f.Ctrl is TextBox tb)
        {
            wert = Formularwerte.AusEingabe(f.Typ, tb.Text);
            // Halb getippte oder ungültige Zahl: noch nichts übernehmen
            if (wert is null && tb.Text.Trim().Length > 0) return;
        }
        else wert = ((CheckBox)f.Ctrl).IsChecked == true ? "true" : "false";
        _werte.Eingabe(f.Key, wert);
        _geaendert = true;
        Markiere(f);
        ZeigeStatus();
    }

    private void OnRolleGeaendert()
    {
        if (_zeigeAn) return;
        _werte.Eingabe("mandant_ist_unterhaltspflichtig", RollePflichtig.IsChecked == true ? "true" : "false");
        _geaendert = true;
        RolleMarkieren();
    }

    private void OnKinderGeaendert()
    {
        if (_zeigeAn) return;
        var kinder = _kinder.Where(k => k.Name.Trim().Length > 0 || k.Geburt.Trim().Length > 0)
            .Select(k => new FragebogenKind { Name = k.Name.Trim(), Geburtsdatum = DatumEingabe.Parse(k.Geburt), LebtBeiMandant = k.BeiMandant })
            .ToList();
        _werte.Eingabe("kinder", kinder.Count == 0 ? null : Formularwerte.KinderText(kinder));
        _geaendert = true;
        KinderMarkieren();
    }

    private void OnKindNeu(object sender, RoutedEventArgs e)
    {
        _kinder.Add(new KindZeile());
        KinderLeer.Visibility = Visibility.Collapsed;
    }

    private void OnKindEntfernen(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is KindZeile k) _kinder.Remove(k);
        OnKinderGeaendert();
    }

    private ContextMenu Kontextmenue(string key, bool mitBearbeiten)
    {
        var menue = new ContextMenu();
        if (mitBearbeiten)
        {
            menue.Items.Add(new MenuItem { Header = "Ausschneiden", Command = ApplicationCommands.Cut });
            menue.Items.Add(new MenuItem { Header = "Kopieren", Command = ApplicationCommands.Copy });
            menue.Items.Add(new MenuItem { Header = "Einfügen", Command = ApplicationCommands.Paste });
            menue.Items.Add(new Separator());
        }
        var ausfuellen = new MenuItem { Header = "✨ Von LexWolf ausfüllen" };
        ausfuellen.Click += (_, _) =>
        {
            if (_werte.LexWolfAusfuellen(key)) { _geaendert = true; Anzeigen(); ZeigeStatus(); }
        };
        menue.Opened += (_, _) => ausfuellen.IsEnabled = _werte.Info(key) is { Ki: not null, Anwalt: not null };
        menue.Items.Add(ausfuellen);
        return menue;
    }

    // ------------------------------------------------------------ Aus der Akte lesen

    private void OnLexWolfAusfuellen(object sender, RoutedEventArgs e) => _ = AusAkteLesenAsync(automatisch: false);

    /// <summary>Akte lesen lassen; neue Werte füllen leere Felder, Eingaben des Anwalts bleiben.
    /// Auf Knopfdruck werden zusätzlich geleerte Felder wieder gefüllt.</summary>
    private async System.Threading.Tasks.Task AusAkteLesenAsync(bool automatisch)
    {
        if (!_ausfueller.HatDokumente(_mandantId))
        {
            var geleert = automatisch ? 0 : _werte.LeereAusfuellen();
            if (geleert > 0) { _geaendert = true; Anzeigen(); }
            StatusText.Text = "In der Akte liegen noch keine lesbaren Dokumente — über „📎 Dokumente“ hinzufügen.";
            return;
        }
        AusfuellenBtn.IsEnabled = false;
        StatusText.Text = "⏳ LexWolf liest die Akte und füllt den Fragebogen …";
        try
        {
            var neu = await _ausfueller.ErmittelnAsync(_mandantId, _mandantName);
            var n = neu is null ? 0 : _werte.KiUebernehmen(neu);
            if (!automatisch) n += _werte.LeereAusfuellen();
            _geaendert = true;
            Speichern();
            Anzeigen();
            StatusText.Text = n > 0
                ? $"✓ {n} Angabe(n) aus der Akte übernommen — orange markiert, bitte prüfen."
                : "✓ Akte gelesen — keine neuen Angaben gefunden.";
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

    private void ZeigeStatus()
    {
        var alle = _felder.Select(f => f.Key).Append("kinder").Append("mandant_ist_unterhaltspflichtig").ToList();
        var lexwolf = alle.Count(k => _werte.HerkunftVon(k) == Herkunft.LexWolf);
        var geleert = alle.Count(k => _werte.Info(k) is { Ki: not null, Anwalt: "" });
        var teile = new List<string>();
        if (lexwolf > 0) teile.Add($"{lexwolf} Angabe(n) von LexWolf — bitte prüfen");
        if (geleert > 0) teile.Add($"{geleert} geleert — „✨ LexWolf ausfüllen“ füllt sie wieder");
        StatusText.Text = string.Join(" · ", teile);
    }

    /// <summary>Vom Mandanten zurückgeschickten Fragebogen auslesen — gilt als Angabe von LexWolf mit
    /// dem Dateinamen als Fundstelle; gespeichert wird beim Schließen bzw. mit „Speichern“.</summary>
    private async void OnEinlesen(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Ausgefüllten Fragebogen wählen",
            Filter = "Fragebogen (*.docx;*.pdf;*.eml;*.txt)|*.docx;*.pdf;*.eml;*.txt",
            InitialDirectory = Directory.Exists(Path.Combine(_dokumentePfad, _mandantName)) ? Path.Combine(_dokumentePfad, _mandantName) : "",
        };
        if (dlg.ShowDialog(this) != true) return;

        EinlesenBtn.IsEnabled = false;
        StatusText.Text = "⏳ Fragebogen wird ausgelesen …";
        try
        {
            var text = await System.Threading.Tasks.Task.Run(() => DokumentScanner.LeseText(dlg.FileName));
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var payload = System.Text.Json.JsonSerializer.Serialize(new { text, mandant = _mandantName });
            using var content = new System.Net.Http.StringContent(payload, Encoding.UTF8, "application/json");
            var response = await http.PostAsync($"{_backendUrl}/akte/fragebogen-lesen", content);
            response.EnsureSuccessStatusCode();
            var neu = Formularwerte.AusFlacherAntwort(await response.Content.ReadAsStringAsync(), FragebogenFelder.Typen,
                Path.GetFileName(dlg.FileName));
            var n = _werte.KiUebernehmen(neu);
            _geaendert = true;
            Anzeigen();
            StatusText.Text = n > 0
                ? $"✓ {n} Angabe(n) übernommen — orange markiert, bitte prüfen."
                : "Im Dokument wurden keine neuen Angaben gefunden.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Einlesen fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            EinlesenBtn.IsEnabled = true;
        }
    }

    // ------------------------------------------------------------ Speichern, Word

    private void Speichern()
    {
        _ausfueller.Speichern(_mandantId, _werte);
        _geaendert = false;
    }

    private void OnSpeichern(object sender, RoutedEventArgs e)
    {
        Speichern();
        var ungueltig = _kinder.Count(k => k.Geburt.Trim().Length > 0 && DatumEingabe.Parse(k.Geburt) is null);
        StatusText.Text = ungueltig > 0
            ? $"Gespeichert — bei {ungueltig} Kind(ern) ist das Geburtsdatum ungültig (z. B. 03.05.2015)."
            : $"✓ Gespeichert um {DateTime.Now:HH:mm} — der Rechner nutzt diese Angaben.";
    }

    /// <summary>Fragebogen als Word-Datei in die Akte (bereits bekannte Angaben vorausgefüllt).</summary>
    private void OnWord(object sender, RoutedEventArgs e)
    {
        var f = FragebogenFelder.ZuFragebogen(_werte);
        var de = CultureInfo.GetCultureInfo("de-DE");
        string Z(decimal? d) => d is { } v ? v.ToString("#,##0.##", de) : "";
        string D(DateTime? d) => d is { } v ? DatumEingabe.Format(v) : "";
        string W(string wert) => wert.Length > 0 ? wert : "________________";
        var sb = new StringBuilder();
        sb.AppendLine($"# Fragebogen für {_mandantName}");
        sb.AppendLine();
        sb.AppendLine("Bitte füllen Sie die Angaben aus bzw. ergänzen Sie sie und senden Sie den Fragebogen mit den genannten Unterlagen an uns zurück.");
        sb.AppendLine();
        sb.AppendLine("## 1. Einkommen");
        sb.AppendLine($"Ihr monatliches Nettoeinkommen (Durchschnitt der letzten 12 Monate): {W(Z(f.NettoMandant))} €");
        sb.AppendLine($"Sind Sie erwerbstätig? {(f.MandantErwerbstaetig ? "ja" : "nein")}");
        sb.AppendLine($"Monatliche Belastungen (Kredite, Altersvorsorge): {W(Z(f.AbzuegeMandant))} €");
        sb.AppendLine($"Nettoeinkommen Ihres Ehepartners (soweit bekannt): {W(Z(f.NettoGegenseite))} €");
        sb.AppendLine("Unterlagen: Gehaltsabrechnungen der letzten 12 Monate, letzter Steuerbescheid.");
        sb.AppendLine();
        sb.AppendLine("## 2. Kinder");
        if (f.Kinder.Count == 0) sb.AppendLine("Name, Geburtsdatum, lebt bei: ________________________________");
        foreach (var k in f.Kinder)
            sb.AppendLine($"- {W(k.Name)}, geboren {W(D(k.Geburtsdatum))}, lebt {(k.LebtBeiMandant ? "bei Ihnen" : "beim anderen Elternteil")}");
        sb.AppendLine();
        sb.AppendLine("## 3. Ehe");
        sb.AppendLine($"Heirat am: {W(D(f.Heiratsdatum))}");
        sb.AppendLine($"Trennung am: {W(D(f.Trennungsdatum))}");
        sb.AppendLine();
        sb.AppendLine("## 4. Vermögen");
        sb.AppendLine($"Ihr Vermögen bei der Heirat: {W(Z(f.AnfangsvermoegenMandant))} €");
        sb.AppendLine($"Erbschaften / Schenkungen während der Ehe: {W(Z(f.PrivilegiertMandant))} €");
        sb.AppendLine($"Ihr heutiges Vermögen (Konten, Immobilien, Fahrzeuge, abzüglich Schulden): {W(Z(f.EndvermoegenMandant))} €");
        sb.AppendLine($"Anzahl Rentenversicherungen (gesetzlich, betrieblich, privat): {W(f.AnzahlRentenanrechte?.ToString(de) ?? "")}");
        sb.AppendLine();
        sb.AppendLine("## 5. Wohnsituation");
        sb.AppendLine(W(f.Wohnsituation));
        sb.AppendLine();
        sb.AppendLine("Ort, Datum, Unterschrift: ______________________________");

        try
        {
            var ordner = Path.Combine(_dokumentePfad, _mandantName);
            Directory.CreateDirectory(ordner);
            var pfad = DokumentExport.FreierPfad(ordner, $"LexWolf_Fragebogen_{DateTime.Now:yyyy-MM-dd}.docx");
            DokumentExport.SchreibeDocx(pfad, sb.ToString());
            StatusText.Text = $"In der Akte abgelegt: {Path.GetFileName(pfad)}";
            DokumentExport.OeffneMitStandardprogramm(pfad);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Word-Datei konnte nicht erstellt werden: {ex.Message}";
        }
    }
}
