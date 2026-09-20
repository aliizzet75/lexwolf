import os
import re

DESKTOP_DIR = os.path.join(os.path.dirname(__file__), '..', '..', 'desktop')
NOTIZEN_XAML = os.path.join(DESKTOP_DIR, 'Dialogs', 'NotizenDialog.xaml')
NOTIZEN_CS = os.path.join(DESKTOP_DIR, 'Dialogs', 'NotizenDialog.xaml.cs')


def _cs():
    return open(NOTIZEN_CS, encoding='utf-8').read()


def test_xaml_hat_zusammenfassungsbereich_mit_ladeindikator():
    xaml = open(NOTIZEN_XAML, encoding='utf-8').read()
    assert re.search(r'Zusammenfassung|KISummary|AiSummary', xaml), \
        "Kein Bereich fuer die KI-Zusammenfassung im XAML gefunden"
    assert re.search(r'ProgressBar|IsIndeterminate', xaml), \
        "Kein Ladeindikator (z.B. ProgressBar) fuer die Zusammenfassung im XAML gefunden"


def test_leere_notizenliste_ohne_llm_call_zeigt_hinweistext():
    src = _cs()
    assert 'Noch keine Notizen' in src, \
        "Hinweistext 'Noch keine Notizen' fehlt fuer den Fall ohne Notizen"
    assert re.search(r'_eintraege\.Count\s*==\s*0', src), \
        "Es fehlt eine Pruefung auf leere Notizenliste vor dem LLM-Aufruf"


def test_zusammenfassung_laeuft_async_ohne_dialog_zu_blockieren():
    src = _cs()
    assert not re.search(r'GenerateZusammenfassungAsync\([^)]*\)\.(Result|Wait\(\))', src), \
        "LLM-Aufruf darf nicht synchron blockieren (.Result/.Wait())"
    assert re.search(r'(_ = |async Task LadeZusammenfassung)', src), \
        "Zusammenfassung muss asynchron (fire-and-forget oder async Task) beim Oeffnen gestartet werden"


def test_llm_fehler_zeigt_fallback_statt_absturz():
    src = _cs()
    m = re.search(r'(async Task\s+\w*Zusammenfassung\w*Async\s*\([^)]*\)\s*\{.*?\n    \})', src, re.S)
    assert m, "Keine async Methode zur Zusammenfassungs-Generierung gefunden"
    assert 'catch' in m.group(1), "Fehler beim LLM-Call muss abgefangen werden (kein Absturz)"
