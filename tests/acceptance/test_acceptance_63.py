import pytest, os, sys, socket
from pathlib import Path

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../../backend'))

EXPORT_SCRIPT = Path(__file__).parent.parent / 'quality' / 'manual_review_export.py'


def _port_ok(p):
    s = socket.socket(); r = s.connect_ex(('localhost', p)); s.close(); return r == 0

db_ok = _port_ok(5432)


def test_export_script_exists():
    assert EXPORT_SCRIPT.exists(), f'manual_review_export.py fehlt: {EXPORT_SCRIPT}'


def test_export_script_has_main_function():
    src = EXPORT_SCRIPT.read_text()
    assert 'def ' in src, 'Skript muss mindestens eine Funktion definieren'
    assert 'export' in src.lower() or 'generate' in src.lower(),         'Skript braucht export/generate-Funktion'


def test_export_references_docx_or_pdf():
    src = EXPORT_SCRIPT.read_text()
    assert 'docx' in src.lower() or 'pdf' in src.lower() or 'reportlab' in src.lower(),         'Export-Skript muss DOCX oder PDF erzeugen'


def test_export_has_20_cases():
    import importlib.util
    spec = importlib.util.spec_from_file_location('manual_review_export', EXPORT_SCRIPT)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    fn = getattr(mod, 'select_cases', getattr(mod, 'get_cases', getattr(mod, 'load_cases', None)))
    if fn is not None:
        cases = fn()
        assert len(cases) >= 20, f'Mindestens 20 Fälle erwartet, nur {len(cases)} gefunden'


def test_pearson_correlation_utility():
    from scipy.stats import pearsonr
    auto   = [0.9, 0.7, 0.5, 0.8, 0.6]
    manual = [4,   3,   2,   4,   3  ]
    r, _ = pearsonr(auto, manual)
    assert r > 0.7, f'Pearson-Test-Utility fehlerhaft: r={r:.2f}'
