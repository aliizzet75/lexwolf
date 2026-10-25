using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using LexWolf.Controls;
using LexWolf.Dialogs;
using LexWolf.Services;

namespace LexWolf;

// Akten-Funktionen: Fristen, Posteingang, Aktenstamm, Schriftsatz, Rechner,
// Sachstand, Fragebogen. Eigene Datei, damit MainWindow.xaml.cs nicht weiter wächst.
public partial class MainWindow
{
    private AkteService? _akte;
    private PostfachService? _postfach;
    private GespraechsRecorder? _gespraechsRecorder;

    private void InitAkte()
    {
        _akte = new AkteService(_db, _http, BackendUrl, () => _settings);
        _akte.Geaendert += () => Dispatcher.BeginInvoke(AktualisiereAktenAnsichten);
        _akte.WarteschlangeGeaendert += n => Dispatcher.BeginInvoke(() => EingangPanel.SetAuswertung(n));

        FristenPanel.Init(_db, () => _settings);
        FristenPanel.MandantGewaehlt += WaehleMandant;
        FristenPanel.DringendGeaendert += n => FristenTabHeader.Text = n > 0 ? $"⏰ Fristen ({n})" : "⏰ Fristen";

        _postfach = new PostfachService(_db, () => _settings);
        EingangPanel.Init(_db, _postfach, () => _settings);
        _postfach.Abgerufen += (zugeordnet, offen, fehler) => Dispatcher.BeginInvoke(() =>
        {
            if (fehler is not null) AddReasoning("⚠️", $"E-Mail-Postfach: {fehler}");
            else if (zugeordnet + offen > 0)
                AddReasoning("✉", $"{zugeordnet + offen} neue E-Mail(s): {zugeordnet} in die Akten gelegt" +
                                  (offen > 0 ? $", {offen} ohne Mandant — bitte im Eingang zuordnen" : ""));
        });
        _postfach.Starten();
        EingangPanel.OffeneGeaendert += n => EingangTabHeader.Text = n > 0 ? $"📥 Eingang ({n})" : "📥 Eingang";
        EingangPanel.AntwortAngefordert += OnEingangAntwort;

        // Fristen hängen am Datum — einmal pro Stunde neu gruppieren ("morgen" wird "heute").
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        timer.Tick += (_, _) => AktualisiereAktenAnsichten();
        timer.Start();

        ZeigeTagesueberblick();
        StarteGrossesSprachmodell();
    }

    private bool _grossesModellGestartet;

    /// <summary>Großes Vosk-Modell im Hintergrund laden, mit Fortschritt über dem Eingabefeld —
    /// nur wenn in den Einstellungen eingeschaltet (braucht viel Arbeitsspeicher). Bis es
    /// fertig ist bzw. ohne die Option diktiert das kleine Modell.</summary>
    private void StarteGrossesSprachmodell()
    {
        if (_grossesModellGestartet || !_settings.VoskGrossBeimStart || !DiktatService.VoskGrossExists(_settings)) return;
        _grossesModellGestartet = true;

        ModellLadePanel.Visibility = Visibility.Visible;
        ModellLadeBalken.Value = 0;
        _diktat.GrossLadeFortschritt += (p, rest) => Dispatcher.BeginInvoke(() =>
        {
            if (_diktat.GrossBereit) return;
            ModellLadeBalken.Value = p;
            ModellLadeText.Text = $"🎤 Großes Sprachmodell wird geladen … {p:P0} — bis dahin diktiert das kleine Modell";
            ModellLadeRest.Text = rest is { } r
                ? $"noch ca. {(r.TotalMinutes >= 1 ? $"{(int)r.TotalMinutes}:{r.Seconds:00} min" : $"{r.Seconds} s")}"
                : "dauert länger als sonst …";
        });
        _diktat.GrossGeladen += (fehler, dauer) => Dispatcher.BeginInvoke(() =>
        {
            if (fehler is not null)
            {
                ModellLadeText.Text = $"⚠ Großes Sprachmodell konnte nicht geladen werden: {fehler} — es bleibt beim kleinen.";
                ModellLadeRest.Text = "";
                return;
            }
            ModellLadeBalken.Value = 1;
            ModellLadeText.Text = "✓ Großes Sprachmodell bereit — Diktate werden jetzt genauer erkannt";
            ModellLadeRest.Text = $"geladen in {(int)dauer.TotalMinutes}:{dauer.Seconds:00} min";
            DiktatBtn.ToolTip = "Diktieren (großes Sprachmodell) — Klick startet, zweiter Klick beendet die Aufnahme. Die Erkennung läuft lokal.";
            _settings.VoskGrossLadezeitSek = dauer.TotalSeconds;
            try { _settings.Save(); } catch { }
            var aus = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            aus.Tick += (_, _) => { aus.Stop(); ModellLadePanel.Visibility = Visibility.Collapsed; };
            aus.Start();
        });
        _ = _diktat.GrossVorladenAsync(_settings);
    }

    private void AktualisiereAktenAnsichten()
    {
        FristenPanel.Aktualisieren();
        EingangPanel.Aktualisieren();
        AktualisiereKollisionHinweis();
    }

    /// <summary>Beim Start: was ist heute wichtig? Erscheint oben im Chat-Bereich.</summary>
    private void ZeigeTagesueberblick()
    {
        var heute = DateTime.Today;
        var fristen = _db.GetFristen();
        var dringend = fristen.Count(f => f.Status == "offen" && f.Ende <= heute.AddDays(1));
        var woche = fristen.Count(f => f.Status == "offen" && f.Ende > heute.AddDays(1) && f.Ende <= heute.AddDays(7));
        var vorschlaege = fristen.Count(f => f.Status == "vorschlag");
        var eingaenge = _db.GetEingaenge(nurUngelesen: true).Count;
        if (dringend + woche + vorschlaege + eingaenge == 0) return;

        var teile = new System.Collections.Generic.List<string>();
        if (dringend > 0) teile.Add($"⚠ {dringend} Frist(en) heute/morgen");
        if (woche > 0) teile.Add($"{woche} Frist(en) diese Woche");
        if (vorschlaege > 0) teile.Add($"{vorschlaege} neue Fristvorschläge");
        if (eingaenge > 0) teile.Add($"{eingaenge} offene Eingänge");
        AddReasoning("☀", "Tagesüberblick: " + string.Join(" · ", teile));
        if (dringend > 0 || vorschlaege > 0) LinksTabs.SelectedIndex = 1;
    }

    /// <summary>Mandant per Id auswählen (z. B. Klick auf eine Frist oder einen Eingang).</summary>
    private void WaehleMandant(string mandantId)
    {
        if (_activeMandantId == mandantId) return;
        var alle = _db.GetMandanten();
        var name = alle.FirstOrDefault(m => m.Id == mandantId).Name;
        if (name is null) return;
        _mandanten.Clear();
        _mandanten.AddRange(alle);
        ApplyMandantFilter("");
        if (!MandantBox.Items.Contains(name)) MandantBox.Items.Add(name);
        MandantBox.SelectedItem = name;
    }

    /// <summary>Aus OnMandantChanged aufgerufen.</summary>
    private void AktenMandantGewechselt()
    {
        AktenLeiste.Visibility = _activeMandantId is null ? Visibility.Collapsed : Visibility.Visible;
        FristenPanel.SetAktiverMandant(_activeMandantId);
        EingangPanel.SetAktiverMandant(_activeMandantId);
        AktualisiereKollisionHinweis();
    }

    private void AktualisiereKollisionHinweis()
    {
        if (_akte is null || _activeMandantId is null)
        {
            KollisionHinweis.Visibility = Visibility.Collapsed;
            return;
        }
        var k = _akte.Kollisionen(_activeMandantId);
        KollisionHinweis.Visibility = k.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        KollisionHinweis.ToolTip = string.Join("\n", k.Select(x => $"{x.Beschreibung} (Akte: {x.AndereAkte})"));
    }

    private void OnKollisionHinweisClick(object sender, MouseButtonEventArgs e) => OnAktenstammClick(sender, e);

    private void OnEingangAntwort(string mandantId, EingangZeile z)
    {
        var dlg = new AntwortDialog(_db, BackendUrl, z, _settings.DokumentePfad) { Owner = this };
        dlg.ShowDialog();
        if (dlg.InChatUebernehmen is not { } text) return;
        // Weiterarbeiten im Chat: dort hat die KI den ganzen Mandanten-Kontext
        WaehleMandant(mandantId);
        InputBox.Text = text;
        InputBox.CaretIndex = InputBox.Text.Length;
        InputBox.Focus();
    }

    private bool MandantNoetig()
    {
        if (_activeMandantId is not null) return true;
        MessageBox.Show(this, "Bitte zuerst oben einen Mandanten auswählen.", "LexWolf",
            MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void OnNeuesMandatClick(object sender, RoutedEventArgs e) => _ = NeuesMandatAsync(null);

    private async Task NeuesMandatAsync(IEnumerable<string>? unterlagen)
    {
        if (_akte is null) return;
        var dlg = new NeuesMandatDialog(_db, _akte, _settings, unterlagen) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Ergebnis is not { } ergebnis) return;

        WaehleMandant(ergebnis.MandantId);
        // Erst ablegen und einlesen — dann kennt die KI die Unterlagen schon bei der Ersteinschätzung.
        if (ergebnis.Unterlagen is { } u)
            await InAkteUebernehmenAsync(ergebnis.MandantId, ergebnis.Name, u);
        AktualisiereAktenAnsichten();
        if (ergebnis.KiAuftrag is null) return;

        // Erst die (leere) Historie fertig laden lassen — sonst landet der Auftrag doppelt im Verlauf.
        if (_historieLaden is { } laden)
            try { await laden; } catch { }
        if (_activeMandantId != ergebnis.MandantId) return;
        await SendeAsync(ergebnis.KiAuftrag);
    }

    // ------------------------------------------------------------ Dokumente in die Akte

    private void OnDokumenteClick(object sender, RoutedEventArgs e) => _ = DokumenteHinzufuegenAsync(null);

    private async Task DokumenteHinzufuegenAsync(IEnumerable<string>? vorbelegt)
    {
        if (!MandantNoetig()) return;
        var mandantId = _activeMandantId!;
        var name = _activeMandantName ?? "";
        var dlg = new DokumenteDialog(_db, name, vorbelegt) { Owner = this };
        if (dlg.ShowDialog() != true)
        {
            dlg.Dokumente.ZwischenordnerAufraeumen();
            return;
        }
        await InAkteUebernehmenAsync(mandantId, name, dlg.Dokumente);
    }

    /// <summary>Dateien kopieren, E-Mails samt Anhängen ablegen, Postfach-Mails zuordnen; danach
    /// sofort einlesen (für Chat und Schriftsatz) und auswerten lassen (Fristen, Beteiligte).</summary>
    private async Task InAkteUebernehmenAsync(string mandantId, string mandantName, DokumentAuswahl auswahl)
    {
        var ordner = Path.Combine(_settings.DokumentePfad, mandantName);
        var dateien = auswahl.Dateien.ToList();
        var postfach = auswahl.PostfachMails.ToList();
        var postfachDienst = _postfach;
        var scanner = _scanner;
        ZeigeWolfLoading("Dokumente werden in die Akte gelegt und gelesen …");
        try
        {
            var (neu, fehler) = await Task.Run(() =>
            {
                var neu = new List<string>();
                var fehler = new List<string>();
                foreach (var quelle in dateien)
                {
                    try { neu.AddRange(AktenAblage.Ablegen(quelle, ordner, MsgKonverter.Laden)); }
                    catch (Exception ex) { fehler.Add($"{Path.GetFileName(quelle)}: {ex.Message}"); }
                }
                foreach (var (pfad, absender) in postfach)
                {
                    try
                    {
                        var basis = Path.GetFileNameWithoutExtension(pfad);
                        postfachDienst?.Zuordnen(pfad, mandantId, mandantName, absender);
                        var mails = Path.Combine(ordner, AktenAblage.EmailOrdner);
                        if (Directory.Exists(mails)) neu.AddRange(Directory.EnumerateFiles(mails, basis + "*"));
                    }
                    catch (Exception ex) { fehler.Add($"{Path.GetFileName(pfad)}: {ex.Message}"); }
                }
                foreach (var p in neu) scanner?.DateiEinlesen(mandantId, p);
                return (neu, fehler);
            });

            var inAuswertung = neu.Count(p => _akte?.Auswerten(mandantId, p) == true);
            if (_activeMandantId == mandantId) BuildFileTree(mandantName);
            AktualisiereAktenAnsichten();
            if (neu.Count > 0)
                AddReasoning("📎", $"{neu.Count} Datei(en) in die Akte „{mandantName}“ gelegt" +
                                  (inAuswertung > 0 ? $" — {inAuswertung} werden ausgewertet, erkannte Fristen erscheinen als Vorschlag." : "."));
            if (fehler.Count > 0)
                MessageBox.Show(this, "Nicht übernommen:\n\n" + string.Join("\n", fehler), "Dokumente",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            VerbergeWolfLoading();
            auswahl.ZwischenordnerAufraeumen();
        }
    }

    // Dateien oder Outlook-Mails aufs Fenster ziehen: in die offene Akte bzw. in ein neues Mandat.
    private void OnFensterDragOver(object sender, DragEventArgs e)
    {
        if (!DragDropDateien.EnthaeltDateien(e.Data)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnFensterDrop(object sender, DragEventArgs e)
    {
        if (!DragDropDateien.EnthaeltDateien(e.Data)) return;
        e.Handled = true;
        List<string> dateien;
        var zwischen = Path.Combine(Path.GetTempPath(), "LexWolf-Ablage", Guid.NewGuid().ToString("N"));
        try
        {
            // Outlook gibt die Inhalte nur während des Drop-Ereignisses heraus.
            dateien = DragDropDateien.Holen(e.Data, zwischen);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Die Dateien konnten nicht übernommen werden: {ex.Message}", "Dokumente",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (dateien.Count == 0) return;

        // Dialog erst nach dem Drop öffnen — sonst hängt die Quellanwendung (Outlook, Explorer).
        Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                if (_activeMandantId is not null)
                {
                    await DokumenteHinzufuegenAsync(dateien);
                }
                else if (MessageBox.Show(this, "Es ist kein Mandant ausgewählt. Neues Mandat mit diesen Dokumenten anlegen?",
                             "Dokumente", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    await NeuesMandatAsync(dateien);
                }
            }
            finally
            {
                try { if (Directory.Exists(zwischen)) Directory.Delete(zwischen, recursive: true); } catch { }
            }
        });
    }

    private void OnAktenstammClick(object sender, RoutedEventArgs e)
    {
        if (!MandantNoetig() || _akte is null) return;
        var dlg = new AkteDialog(_db, _akte, _activeMandantId!, _activeMandantName ?? "") { Owner = this };
        dlg.ShowDialog();
        AktualisiereAktenAnsichten();
        if (dlg.LoeschenAngefordert) MandantLoeschen(_activeMandantId!, _activeMandantName ?? "");
    }

    // ------------------------------------------------------------ Mandant löschen

    private void OnMandantLoeschenAusBaum(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { CommandParameter: Models.FileTreeNode node }) return;
        var ordner = MandantOrdnerVon(node.Path, _settings.DokumentePfad);
        if (ordner is null) return;
        MandantLoeschen(DokumentScanner.MandantIdFuer(ordner), ordner);
    }

    /// <summary>Nach Sicherheitsabfrage: Aktenordner in den Papierkorb, danach alle Daten in LexWolf löschen.
    /// Scheitert der Ordner (Datei noch geöffnet), wird gar nichts gelöscht.</summary>
    private void MandantLoeschen(string mandantId, string mandantName)
    {
        var ordner = Path.Combine(_settings.DokumentePfad, mandantName);
        var dlg = new MandantLoeschenDialog(_db, mandantId, mandantName, ordner) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var ordnerDa = Directory.Exists(ordner);
        var papierkorb = MandantLoeschenDialog.HatPapierkorb(ordner);
        try
        {
            if (ordnerDa)
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(ordner,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    papierkorb
                        ? Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin
                        : Microsoft.VisualBasic.FileIO.RecycleOption.DeletePermanently);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Der Aktenordner konnte nicht gelöscht werden — ist noch eine Datei daraus geöffnet?\n\n{ex.Message}\n\n" +
                                  "Es wurde nichts gelöscht.", "Mandant löschen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_activeMandantId == mandantId)
        {
            // Erst abmelden, damit beim Abwählen nichts mehr für den gelöschten Mandanten gespeichert wird
            _activeMandantId = null;
            _activeMandantName = null;
            _history.Clear();
            MandantBox.SelectedItem = null;
            MandantBox.Text = "";
        }
        _db.DeleteMandantKomplett(mandantId);
        _ = LoadMandantenAsync();
        BuildFileTree(_activeMandantName);
        AktualisiereAktenAnsichten();
        AktenMandantGewechselt();
        AddReasoning("🗑", $"Mandant „{mandantName}“ gelöscht" +
                          (!ordnerDa ? "." : papierkorb ? " — der Aktenordner liegt im Papierkorb." : " — der Aktenordner wurde endgültig gelöscht."));
    }

    private void OnSchriftsatzClick(object sender, RoutedEventArgs e)
    {
        if (!MandantNoetig()) return;
        new SchriftsatzDialog(_db, BackendUrl, _activeMandantId!, _activeMandantName ?? "", _settings) { Owner = this }.ShowDialog();
    }

    private async void OnGespraechClick(object sender, RoutedEventArgs e)
    {
        if (!MandantNoetig()) return;

        if (!DiktatService.WhisperExists(_settings) && !await WhisperJetztHerunterladenAsync())
            return;

        _gespraechsRecorder ??= new GespraechsRecorder();
        bool gestartet;
        try
        {
            gestartet = GespraechEinwilligungDialog.StarteAufnahmeMitEinwilligung(
                this, _db, _activeMandantId!, _activeMandantName ?? "", _gespraechsRecorder);
        }
        catch (InvalidOperationException ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Gespräch aufnehmen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!gestartet) return;

        AddReasoning("🎙", $"Gesprächsaufnahme gestartet — Mandant „{_activeMandantName}“.");
        GespraechBtn.IsEnabled = false;

        // Blockiert, bis der Anwalt auf "Aufnahme beenden" klickt (oder den Dialog
        // schließt — beides beendet die Aufnahme) UND Transkription+Strukturierung
        // fertig sind — der Dialog zeigt "bitte warten" statt einfach zu verschwinden.
        var aufnahmeDialog = new GespraechAufnahmeDialog(_gespraechsRecorder, DiktatService.WhisperPath(_settings), _http, BackendUrl) { Owner = this };
        aufnahmeDialog.ShowDialog();
        GespraechBtn.IsEnabled = true;

        await VerarbeiteGespraechAsync(aufnahmeDialog);
    }

    /// <summary>Bietet den einmaligen Whisper-Download an (wie DownloadDiktatModelAsync
    /// für die Chat-Diktierfunktion, aber ohne Vosk — für Gesprächsaufnahmen reicht die
    /// Nachkorrektur-Transkription). Liefert true, wenn das Modell danach vorhanden ist.</summary>
    private async Task<bool> WhisperJetztHerunterladenAsync()
    {
        var antwort = System.Windows.MessageBox.Show(this,
            "Für Gesprächstranskription wird einmalig das Whisper-Sprachmodell heruntergeladen " +
            $"und unter\n{_settings.WhisperOrdner}\ngespeichert.\n\n" +
            "Die Transkription läuft danach komplett auf diesem Rechner. Jetzt herunterladen?",
            "Gespräch aufnehmen — Modell einrichten", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (antwort != MessageBoxResult.Yes) return false;

        GespraechBtn.IsEnabled = false;
        var ursprungsText = GespraechBtn.Content;
        var progress = new Progress<double>(p => GespraechBtn.Content = $"{p:P0}");
        try
        {
            await DiktatService.DownloadModelsAsync(_settings, whisper: true, progress);
            return true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"Download fehlgeschlagen: {ex.Message}", "Gespräch aufnehmen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            GespraechBtn.Content = ursprungsText;
            GespraechBtn.IsEnabled = true;
        }
    }

    /// <summary>Speichert das vom bereits geschlossenen GespraechAufnahmeDialog gelieferte
    /// Transkript in der Akte, zeigt eine kurze Zusammenfassung und danach die Vorschläge
    /// für die nächsten Schritte (T#130/T#131). Transkription und Strukturierung sind beim
    /// Aufruf hier schon fertig — der Dialog hat während dessen "bitte warten" gezeigt.</summary>
    private async Task VerarbeiteGespraechAsync(GespraechAufnahmeDialog aufnahmeDialog)
    {
        if (aufnahmeDialog.Fehler is { } fehler)
        {
            AddReasoning("⚠️", $"Gespräch konnte nicht verarbeitet werden: {fehler}");
            return;
        }

        var transkript = aufnahmeDialog.Transkript;
        if (string.IsNullOrWhiteSpace(transkript))
        {
            AddReasoning("🎙", "Kein Text erkannt — nichts gespeichert.");
            return;
        }

        if (_activeMandantId is not null)
            _db.InsertGespraech(_activeMandantId, DateTime.Now, JsonSerializer.Serialize(new { transkript }));

        if (aufnahmeDialog.Strukturiert is not { } strukturiert)
        {
            AddReasoning("🎙", $"Transkript ({transkript.Length} Zeichen) gespeichert — Strukturierung fehlgeschlagen.");
            return;
        }

        var zusammenfassung = BaueGespraechZusammenfassung(strukturiert);
        string? protokollPfad = null;
        if (!string.IsNullOrWhiteSpace(_activeMandantName))
        {
            try
            {
                var ordner = Path.Combine(_settings.DokumentePfad, _activeMandantName);
                Directory.CreateDirectory(ordner);
                protokollPfad = DokumentExport.FreierPfad(ordner,
                    $"LexWolf_Gespraechsprotokoll_{DateTime.Now:yyyy-MM-dd_HHmmss}.docx");
                var protokollText = $"# Gesprächsprotokoll vom {DateTime.Now:dd.MM.yyyy HH:mm}\n\n" +
                                     $"**Mandant:** {_activeMandantName}\n\n{zusammenfassung}\n\n" +
                                     $"## Transkript\n\n{transkript}";
                DokumentExport.SchreibeDocx(protokollPfad, protokollText);
            }
            catch (Exception ex)
            {
                AddReasoning("⚠️", $"Gesprächsprotokoll konnte nicht in der Akte gespeichert werden: {ex.Message}");
            }
        }

        System.Windows.MessageBox.Show(this,
            zusammenfassung +
            (protokollPfad is not null
                ? $"\n\nVollständiges Protokoll gespeichert: {Path.GetFileName(protokollPfad)}"
                : $"\n\nTranskript ({transkript.Length} Zeichen)."),
            "Gespräch — Zusammenfassung", MessageBoxButton.OK, MessageBoxImage.Information);

        await VorschlaegeDialog.ZeigeNachGespraechAsync(
            this, _http, BackendUrl, strukturiert, _db, () => _settings, _activeMandantId ?? "",
            dokumentePfad: _settings.DokumentePfad, mandantName: _activeMandantName ?? "");
    }

    /// <summary>Baut aus dem Ergebnis von POST /conversation/analyze (T#129) eine kurze,
    /// aber vollständige Zusammenfassung — Themen, erkannte Fristen, Aufgaben und nächste
    /// Schritte — für die Anzeige und für das Gesprächsprotokoll in der Akte.</summary>
    private static string BaueGespraechZusammenfassung(JsonElement strukturiert)
    {
        static List<string> Liste(JsonElement e, string key) =>
            e.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "").Where(s => s.Length > 0).ToList()
                : new List<string>();

        var themen = Liste(strukturiert, "themen");
        var schritte = Liste(strukturiert, "naechste_schritte");

        var fristen = new List<string>();
        if (strukturiert.TryGetProperty("fristen", out var fArr) && fArr.ValueKind == JsonValueKind.Array)
            foreach (var f in fArr.EnumerateArray())
            {
                var datum = f.TryGetProperty("datum", out var d) ? d.GetString() : null;
                var besch = f.TryGetProperty("beschreibung", out var b) ? b.GetString() : null;
                if (!string.IsNullOrWhiteSpace(datum) || !string.IsNullOrWhiteSpace(besch))
                    fristen.Add($"{datum ?? "(Datum offen)"}: {besch ?? ""}".Trim());
            }

        var aufgaben = new List<string>();
        if (strukturiert.TryGetProperty("aufgaben", out var aArr) && aArr.ValueKind == JsonValueKind.Array)
            foreach (var a in aArr.EnumerateArray())
            {
                var was = a.TryGetProperty("was", out var w) ? w.GetString() : null;
                if (!string.IsNullOrWhiteSpace(was)) aufgaben.Add(was!);
            }

        var teile = new List<string>();
        if (themen.Count > 0) teile.Add("## Themen\n" + string.Join("\n", themen.Select(t => $"- {t}")));
        if (fristen.Count > 0) teile.Add("## Fristen\n" + string.Join("\n", fristen.Select(f => $"- {f}")));
        if (aufgaben.Count > 0) teile.Add("## Aufgaben\n" + string.Join("\n", aufgaben.Select(a => $"- {a}")));
        if (schritte.Count > 0) teile.Add("## Nächste Schritte\n" + string.Join("\n", schritte.Select(s => $"- {s}")));
        return teile.Count > 0 ? string.Join("\n\n", teile) : "Keine strukturierten Angaben erkannt.";
    }

    private void OnRechnerClick(object sender, RoutedEventArgs e)
    {
        var fragebogen = _activeMandantId is null ? new Fragebogen() : Fragebogen.FromJson(_db.GetFragebogen(_activeMandantId));
        var dlg = new RechnerDialog(fragebogen, _activeMandantName, db: _db, mandantId: _activeMandantId, backendUrl: BackendUrl) { Owner = this };
        dlg.ShowDialog();
        if (dlg.InChatUebernehmen is { } text)
        {
            var vorher = InputBox.Text.TrimEnd();
            InputBox.Text = vorher.Length > 0 ? $"{vorher}\n{text}" : text;
            InputBox.CaretIndex = InputBox.Text.Length;
            InputBox.Focus();
        }
    }

    private void OnSachstandClick(object sender, RoutedEventArgs e)
    {
        if (!MandantNoetig()) return;
        new SachstandDialog(_db, BackendUrl, _activeMandantId!, _activeMandantName ?? "", _settings.DokumentePfad) { Owner = this }.ShowDialog();
    }

    private void OnFragebogenClick(object sender, RoutedEventArgs e)
    {
        if (!MandantNoetig()) return;
        new FragebogenDialog(_db, BackendUrl, _activeMandantId!, _activeMandantName ?? "", _settings.DokumentePfad) { Owner = this }.ShowDialog();
    }
}
