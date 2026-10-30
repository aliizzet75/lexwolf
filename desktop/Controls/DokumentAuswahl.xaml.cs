using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LexWolf.Database;
using LexWolf.Services;

namespace LexWolf.Controls;

/// <summary>Dokumente für eine Akte sammeln: hineinziehen (Explorer, Outlook), auswählen oder
/// eine noch nicht zugeordnete Postfach-Mail anhaken. Übernommen wird erst vom aufrufenden Dialog.</summary>
public partial class DokumentAuswahl : UserControl
{
    public sealed class Eintrag
    {
        public required string Pfad { get; init; }
        public string Name => Path.GetFileName(Pfad);
        public string Symbol => AktenAblage.IstEmail(Pfad) ? "✉" : Path.GetExtension(Pfad).ToLowerInvariant() switch
        {
            ".pdf" => "📄", ".docx" or ".doc" => "📝", ".jpg" or ".jpeg" or ".png" or ".heic" => "🖼", _ => "📎",
        };
        public string Hinweis => AktenAblage.IstEmail(Pfad) ? "E-Mail — wird mit Anhängen unter „E-Mails“ abgelegt"
            : AktenAblage.IstAuswertbar(Pfad) ? "wird abgelegt und ausgewertet"
            : "wird abgelegt, aber nicht ausgewertet (Dateityp nicht lesbar)";
        public Brush HinweisFarbe => AktenAblage.IstAuswertbar(Pfad) ? Grau : Orange;
        private static readonly Brush Grau = new SolidColorBrush(Color.FromRgb(0x9d, 0xa7, 0xb3));
        private static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xd2, 0x99, 0x22));
    }

    public sealed class PostfachEintrag
    {
        public required string Pfad { get; init; }
        public required string Datum { get; init; }
        public required string Betreff { get; init; }
        public required string Absender { get; init; }
        public required string AbsenderAdresse { get; init; }
        public bool Ausgewaehlt { get; set; }
    }

    private readonly ObservableCollection<Eintrag> _dateien = new();
    private readonly List<PostfachEintrag> _postfach = new();
    private static readonly Brush RahmenNormal = new SolidColorBrush(Color.FromRgb(0x48, 0x4f, 0x58));
    private static readonly Brush RahmenAktiv = new SolidColorBrush(Color.FromRgb(0xe8, 0xa8, 0x38));

    /// <summary>Zwischenordner für Outlook-Mails; nach der Übernahme löschen.</summary>
    public string Zwischenordner { get; } = Path.Combine(Path.GetTempPath(), "LexWolf-Ablage", Guid.NewGuid().ToString("N"));

    public event Action? Geaendert;

    public DokumentAuswahl()
    {
        InitializeComponent();
        Liste.ItemsSource = _dateien;
    }

    /// <summary>Noch nicht zugeordnete Postfach-Mails zur Auswahl anbieten.</summary>
    public void PostfachAnbieten(LocalDb db)
    {
        _postfach.Clear();
        _postfach.AddRange(db.GetUnzugeordnet().Where(u => File.Exists(u.Pfad)).OrderByDescending(u => u.Datum).Take(30)
            .Select(u => new PostfachEintrag
            {
                Pfad = u.Pfad, Datum = DatumEingabe.Format(u.Datum), Betreff = u.Betreff.Length > 0 ? u.Betreff : "(ohne Betreff)",
                Absender = u.AbsenderName.Length > 0 ? $"{u.AbsenderName} <{u.Absender}>" : u.Absender, AbsenderAdresse = u.Absender,
            }));
        PostfachListe.ItemsSource = _postfach;
        PostfachBereich.Visibility = _postfach.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public IReadOnlyList<string> Dateien => _dateien.Select(d => d.Pfad).ToList();

    public IReadOnlyList<(string Pfad, string Absender)> PostfachMails =>
        _postfach.Where(p => p.Ausgewaehlt).Select(p => (p.Pfad, p.AbsenderAdresse)).ToList();

    public int Anzahl => _dateien.Count + _postfach.Count(p => p.Ausgewaehlt);

    public void Hinzufuegen(IEnumerable<string> pfade)
    {
        foreach (var p in pfade)
            if (!_dateien.Any(d => string.Equals(d.Pfad, p, StringComparison.OrdinalIgnoreCase)))
                _dateien.Add(new Eintrag { Pfad = p });
        Geaendert?.Invoke();
    }

    /// <summary>Für den Drop direkt aufs Hauptfenster: Daten sofort lesen (Outlook gibt sie nur im Drop-Ereignis her).</summary>
    public void HinzufuegenAus(IDataObject daten) => Hinzufuegen(DragDropDateien.Holen(daten, Zwischenordner));

    public void ZwischenordnerAufraeumen()
    {
        try { if (Directory.Exists(Zwischenordner)) Directory.Delete(Zwischenordner, recursive: true); } catch { }
    }

    private void OnWaehlen(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Dokumente für die Akte auswählen",
            Multiselect = true,
            Filter = "Dokumente und E-Mails|*.pdf;*.docx;*.doc;*.txt;*.eml;*.msg;*.jpg;*.jpeg;*.png;*.xlsx|Alle Dateien|*.*",
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) Hinzufuegen(dlg.FileNames);
    }

    /// <summary>Alle Dateien eines gewählten Ordners (inkl. Unterordner) zur Auswahl hinzufügen —
    /// beim Übernehmen landen sie flach im Aktenverzeichnis des Mandanten (AktenAblage.Ablegen
    /// nimmt nur den Dateinamen, keine Unterordner-Struktur), unabhängig davon, wie tief eine
    /// Datei im gewählten Ordner lag.</summary>
    private void OnOrdnerWaehlen(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Ordner für die Akte auswählen" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var dateien = Directory.EnumerateFiles(dlg.FolderName, "*", SearchOption.AllDirectories);
            Hinzufuegen(dateien);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Der Ordner konnte nicht gelesen werden: {ex.Message}", "Dokumente",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnEntfernen(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Eintrag d) _dateien.Remove(d);
        Geaendert?.Invoke();
    }

    private void OnPostfachGeaendert(object sender, RoutedEventArgs e) => Geaendert?.Invoke();

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        var ok = DragDropDateien.EnthaeltDateien(e.Data);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        Rahmen.Stroke = ok ? RahmenAktiv : RahmenNormal;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => Rahmen.Stroke = RahmenNormal;

    private void OnDrop(object sender, DragEventArgs e)
    {
        Rahmen.Stroke = RahmenNormal;
        try
        {
            HinzufuegenAus(e.Data);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Die Dateien konnten nicht übernommen werden: {ex.Message}", "Dokumente",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        e.Handled = true;
    }
}
