"""T#323: Dokumente im Chat auswählen + Bugfix GetLatestTemplateContext + Vorlagen-Priorität."""
import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_MAINWINDOW = os.path.join(_DESKTOP, "MainWindow.xaml.cs")
_MAINWINDOW_XAML = os.path.join(_DESKTOP, "MainWindow.xaml")
_VORLAGENORDNER = os.path.join(_DESKTOP, "Services", "VorlagenOrdner.cs")


def _read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def test_get_latest_template_context_nutzt_sicheres_lesen():
    # Die Methode liest nicht mehr selbst roh per File.ReadAllText, sondern über die
    # testbare (plattformunabhängige) VorlagenOrdner.LiesInhaltOderNull — siehe
    # test_vorlagenordner_liest_inhalt_oder_null_hat_try_catch für den eigentlichen
    # Fehlerbehandlungs-Nachweis inkl. Laufzeittest (LexWolf.Tests).
    mw = _read(_MAINWINDOW)
    m = re.search(r"private string\? GetLatestTemplateContext\(\)\s*\{", mw)
    assert m, "GetLatestTemplateContext() nicht gefunden."
    body = mw[m.end():m.end() + 900]
    assert "File.ReadAllText" not in body, \
        "GetLatestTemplateContext() liest Dateien noch direkt statt über VorlagenOrdner.LiesInhaltOderNull."
    assert "VorlagenOrdner.LiesInhaltOderNull(" in body, \
        "GetLatestTemplateContext() nutzt VorlagenOrdner.LiesInhaltOderNull nicht."


def test_vorlagenordner_liest_inhalt_oder_null_hat_try_catch():
    vo = _read(_VORLAGENORDNER)
    m = re.search(r"public static string\? LiesInhaltOderNull\(string pfad\)\s*\{", vo)
    assert m, "VorlagenOrdner.LiesInhaltOderNull(string) nicht gefunden."
    body = vo[m.end():m.end() + 400]
    assert "try" in body and "catch" in body, \
        "LiesInhaltOderNull() fängt File.ReadAllText-Fehler nicht ab (fehlendes try/catch)."
    assert re.search(r"catch\s*\(\s*IOException[^}]*\)\s*\{\s*return\s+null", body, re.DOTALL), \
        "IOException führt nicht zu return null in LiesInhaltOderNull()."
    assert re.search(r"catch\s*\(\s*UnauthorizedAccessException[^}]*\)\s*\{\s*return\s+null", body, re.DOTALL), \
        "UnauthorizedAccessException führt nicht zu return null in LiesInhaltOderNull()."


def test_chat_dokumentenauswahl_vorhanden_bei_der_eingabe():
    xaml = _read(_MAINWINDOW_XAML)
    m = re.search(r'Click="OnSend"', xaml)
    assert m, "SendBtn/OnSend nicht gefunden — Chat-Eingabebereich fehlt."
    # Vorsicht: DokumenteBtn (📎 Dokumente, OnDokumenteClick) legt Dateien in der Akte ab
    # und ist NICHT die neue Chat-Dokumentenauswahl — die muss zusätzlich existieren,
    # in der Nähe der Eingabe/des Senden-Buttons.
    umgebung = xaml[max(0, m.start() - 2500):m.end() + 500]
    neue_buttons = re.findall(r'<Button[^>]*x:Name="(\w+)"[^>]*>', umgebung)
    assert any(name != "DokumenteBtn" for name in neue_buttons if "📎" in umgebung), \
        "Kein zusätzlicher 📎-Button (verschieden von DokumenteBtn) nahe der Chat-Eingabe gefunden."
    assert "📎" in umgebung, "Kein 📎-Button zur Dokumentenauswahl nahe der Chat-Eingabe."


def test_chat_dokumentenauswahl_nutzt_vorlagenordner_findevorlagenpfad():
    mw = _read(_MAINWINDOW)
    # Ein Aufruf existiert bereits in GetLatestTemplateContext() (der Bugfix-Zielmethode).
    # Baut die neue Dokumentenauswahl auch Vorlagen ein, muss sie denselben
    # VorlagenOrdner.FindeVorlagenPfad wiederverwenden statt eine eigene Lade-Logik zu
    # erfinden — das zeigt sich an einem zusätzlichen echten Aufruf (nicht nur Kommentar).
    aufrufe = re.findall(r"(?<!// )VorlagenOrdner\.FindeVorlagenPfad\(", mw)
    assert len(aufrufe) >= 2, \
        "Kein zusätzlicher Aufruf von VorlagenOrdner.FindeVorlagenPfad außerhalb von " \
        "GetLatestTemplateContext() — neue Dokumentenauswahl scheint Vorlagen eigenständig zu laden."


def test_chat_dokumente_werden_in_kontext_eingebaut():
    mw = _read(_MAINWINDOW)
    assert "_chatSelectedPaths" in mw, \
        "Kein Speicher für die im Chat ausgewählten Dokumente-Pfade (_chatSelectedPaths)."
    # Die ausgewählten Dokumente müssen in PostChatAsync (bzw. dem Bau des Mandanten-
    # Dokumente-Kontexts) verwendet werden — erkennbar an der markierten Überschrift.
    assert "Für diese Chat-Frage vom Anwalt ausgewählt" in mw, \
        "Ausgewählte Chat-Dokumente fließen nicht als eigener Kontext-Abschnitt ein."
