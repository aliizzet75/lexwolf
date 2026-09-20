import os

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_XAML = os.path.join(_DESKTOP, "Dialogs", "NotizenDialog.xaml")
_CS = os.path.join(_DESKTOP, "Dialogs", "NotizenDialog.xaml.cs")


def test_notizendialog_dateien_existieren():
    assert os.path.isfile(_XAML), "Dialogs/NotizenDialog.xaml fehlt noch — Implementierung von T#214 aussteht"
    assert os.path.isfile(_CS), "Dialogs/NotizenDialog.xaml.cs fehlt noch — Implementierung von T#214 aussteht"


def test_xaml_hat_navigationsliste_und_editierbereich_und_ist_modal():
    xaml = open(_XAML, encoding="utf-8").read()
    assert "ListBox" in xaml, "Keine Navigationsliste (ListBox) fuer die Notizen gefunden"
    assert "AcceptsReturn=\"True\"" in xaml, "Kein mehrzeiliges Textfeld zum Bearbeiten der Notiz gefunden"
    assert "WindowStartupLocation=\"CenterOwner\"" in xaml, "Dialog ist nicht wie SettingsDialog/InfoDialog fuer modale Nutzung ausgelegt"
    assert "ShowInTaskbar=\"False\"" in xaml, "Dialog erscheint in der Taskleiste statt modal ueber MainWindow"


def test_codebehind_laedt_und_speichert_notizen_ueber_localdb():
    cs = open(_CS, encoding="utf-8").read()
    assert "GetNotizen" in cs, "Liste der Notizen wird nicht ueber LocalDb.GetNotizen() geladen"
    assert "UpdateNotiz" in cs, "Aenderungen werden nicht ueber LocalDb.UpdateNotiz() persistiert"
