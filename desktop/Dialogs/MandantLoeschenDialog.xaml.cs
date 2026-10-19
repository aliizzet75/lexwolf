using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using LexWolf.Database;

namespace LexWolf.Dialogs;

/// <summary>Sicherheitsabfrage vor dem Löschen eines Mandanten: zeigt, was gelöscht wird,
/// warnt vor offenen Fristen und verlangt den Namen zur Bestätigung. Gelöscht wird vom Aufrufer.</summary>
public partial class MandantLoeschenDialog : Window
{
    private sealed record Zeile(string Symbol, string Text);

    private readonly string _name;

    /// <summary>Kann der Ordner in den Papierkorb? Auf Netzlaufwerken löscht Windows endgültig.</summary>
    public static bool HatPapierkorb(string ordner)
    {
        try
        {
            var voll = Path.GetFullPath(ordner);
            if (voll.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            return new DriveInfo(Path.GetPathRoot(voll)!).DriveType is DriveType.Fixed or DriveType.Removable;
        }
        catch { return false; }
    }

    public MandantLoeschenDialog(LocalDb db, string mandantId, string mandantName, string aktenordner)
    {
        InitializeComponent();
        FensterGroesse.AnSchriftAnpassen(this);
        _name = mandantName;
        UntertitelText.Text = mandantName;

        var u = db.GetMandantUmfang(mandantId);
        int dateien;
        try { dateien = Directory.Exists(aktenordner) ? Directory.EnumerateFiles(aktenordner, "*", SearchOption.AllDirectories).Count() : 0; }
        catch { dateien = 0; }

        static string Anzahl(int n, string eins, string mehr) => n == 1 ? $"1 {eins}" : $"{n} {mehr}";
        var papierkorb = HatPapierkorb(aktenordner);
        var zeilen = new List<Zeile>
        {
            new("📁", Directory.Exists(aktenordner)
                ? $"Aktenordner „{Path.GetFileName(aktenordner)}“ mit {Anzahl(dateien, "Datei", "Dateien")}" +
                  (papierkorb ? " — in den Papierkorb" : " — ENDGÜLTIG (Netzlaufwerk, kein Papierkorb)")
                : "Aktenordner (nicht mehr vorhanden)"),
            new("⏰", Anzahl(u.Fristen, "Frist", "Fristen") + (u.FristenOffen > 0 ? $", davon {u.FristenOffen} offen" : "")),
            new("💬", Anzahl(u.ChatNachrichten, "Chat-Nachricht", "Chat-Nachrichten")),
            new("📝", Anzahl(u.Notizen, "Notiz", "Notizen")),
            new("🗂", "Aktenstamm, Fragebogen, Rechnerwerte, Auswertungen und Zusammenfassungen"),
        };
        UmfangListe.ItemsSource = zeilen;

        if (u.FristenOffen > 0)
        {
            FristWarnung.Visibility = Visibility.Visible;
            FristWarnungText.Text = $"⚠ Achtung: {Anzahl(u.FristenOffen, "offene Frist", "offene Fristen")} bzw. Fristvorschläge. " +
                                    "Nach dem Löschen erscheinen sie nirgends mehr — auch nicht in der Fristenliste.";
        }
        if (!papierkorb && dateien > 0)
            PapierkorbHinweis.Text = "Der Aktenordner liegt auf einem Netzlaufwerk: Windows hat dort keinen Papierkorb, die Dateien sind danach " +
                                     "endgültig gelöscht. Sichern Sie den Ordner vorher, wenn Sie ihn noch brauchen.";
        BestaetigungLabel.Text = $"Zur Bestätigung den Namen eingeben: {mandantName}";
        Loaded += (_, _) => BestaetigungBox.Focus();
    }

    private void OnBestaetigungGeaendert(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        LoeschenBtn.IsEnabled = string.Equals(BestaetigungBox.Text.Trim(), _name.Trim(), StringComparison.OrdinalIgnoreCase);

    private void OnLoeschen(object sender, RoutedEventArgs e) => DialogResult = true;
}
