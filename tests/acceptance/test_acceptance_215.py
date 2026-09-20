import os

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_XAML = os.path.join(_DESKTOP, "Dialogs", "NotizenDialog.xaml")
_CS = os.path.join(_DESKTOP, "Dialogs", "NotizenDialog.xaml.cs")


def test_xaml_hat_neue_notiz_button_unten_in_scrollbarer_nav():
    xaml = open(_XAML, encoding="utf-8").read()
    assert "Neue Notiz" in xaml, "Button 'Neue Notiz' fehlt noch im XAML — Implementierung von T#215 aussteht"
    assert "ScrollViewer" in xaml or "VerticalScrollBarVisibility" in xaml, \
        "Navigationsliste ist nicht als scrollbar ausgelegt, Button koennte bei langer Liste unerreichbar sein"


def test_codebehind_erstellt_notiz_ueber_insertnotiz_mit_leerem_text():
    cs = open(_CS, encoding="utf-8").read()
    assert "InsertNotiz" in cs, "Neue Notiz wird nicht ueber LocalDb.InsertNotiz() angelegt"
    assert "OnNeueNotiz" in cs or "NeueNotiz" in cs, "Kein Click-Handler fuer den 'Neue Notiz'-Button gefunden"


def test_codebehind_waehlt_neue_notiz_aus_und_fokussiert_editor():
    cs = open(_CS, encoding="utf-8").read()
    assert "NoteEditorBox.Focus()" in cs, \
        "Nach dem Anlegen einer neuen Notiz wird der Fokus nicht ins Textfeld rechts gesetzt"
    assert "SelectedItem" in cs or "SelectedIndex" in cs, \
        "Neue Notiz wird nach dem Anlegen nicht automatisch in der Navigationsliste ausgewaehlt"
