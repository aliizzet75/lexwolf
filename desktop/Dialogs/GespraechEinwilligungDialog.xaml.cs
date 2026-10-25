using System.Windows;
using LexWolf.Database;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>DSGVO-Einwilligung vor Beginn einer Gesprächsaufzeichnung (Task #128): der Mandant
/// muss aktiv zustimmen, bevor <see cref="GespraechsRecorder.StartAufnahme"/> aufgerufen wird.
/// Bei "Ja" wird die Einwilligung mit Zeitstempel in der lokalen SQLite-Datenbank (LocalDb)
/// gespeichert; bei "Nein" wird nichts gespeichert und es darf nicht aufgezeichnet werden.</summary>
public partial class GespraechEinwilligungDialog : Window
{
    public const string EinwilligungsText =
        "Das Gespräch wird für Protokollzwecke aufgezeichnet und lokal transkribiert. " +
        "Audio wird nicht dauerhaft gespeichert. Einverstanden?";

    private readonly LocalDb _db;
    private readonly string _mandantId;

    public GespraechEinwilligungDialog(LocalDb db, string mandantId, string? mandantName = null)
    {
        InitializeComponent();
        _db = db;
        _mandantId = mandantId;
        UntertitelText.Text = mandantName ?? "";
        HinweisText.Text = EinwilligungsText;
    }

    private void OnJa(object sender, RoutedEventArgs e)
    {
        _db.InsertEinwilligung(_mandantId, EinwilligungsText, true);
        DialogResult = true;
    }

    private void OnNein(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>Zeigt die Einwilligungsabfrage modal (blockiert bis zur Entscheidung) und startet
    /// die Aufnahme nur, wenn der Mandant zustimmt. Liefert <c>true</c>, wenn aufgezeichnet wird,
    /// <c>false</c> bei "Nein" — in diesem Fall wird <paramref name="recorder"/> nicht gestartet
    /// und auch keine Einwilligung gespeichert.</summary>
    public static bool StarteAufnahmeMitEinwilligung(
        Window owner, LocalDb db, string mandantId, string mandantName, GespraechsRecorder recorder)
    {
        var dialog = new GespraechEinwilligungDialog(db, mandantId, mandantName) { Owner = owner };
        if (dialog.ShowDialog() != true)
            return false;

        recorder.StartAufnahme();
        return true;
    }
}
