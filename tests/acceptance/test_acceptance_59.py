import os
import sys
import pytest

sys.path.insert(0, "/data/.openclaw/workspace-codex/projects/lexwolf/tests/quality")

CHECKER_PATH = "/data/.openclaw/workspace-codex/projects/lexwolf/tests/quality/source_checker.py"
PG_DSN = "host=172.18.0.1 port=5432 dbname=lexwolf user=postgres password=postgres"


def _db_available():
    try:
        import psycopg2
        conn = psycopg2.connect(PG_DSN)
        conn.close()
        return True
    except Exception:
        return False


HAS_DB = _db_available()


class TestSourceCheckerImport:
    def test_file_exists(self):
        assert os.path.exists(CHECKER_PATH), f"source_checker.py nicht unter {CHECKER_PATH}"

    def test_import(self):
        from source_checker import check_source_references
        assert callable(check_source_references)


class TestSourceCheckerOutput:
    def setup_method(self):
        from source_checker import check_source_references
        self.fn = check_source_references

    def test_ergebnis_keys(self):
        result = self.fn([])
        for key in ('gesamt', 'existieren', 'fehlen', 'fehler_liste'):
            assert key in result, f"Key '{key}' fehlt im Ergebnis"

    def test_leere_liste(self):
        result = self.fn([])
        assert result['gesamt'] == 0
        assert result['fehler_liste'] == []

    def test_keine_paragraphen(self):
        result = self.fn(["Hallo Welt ohne Paragraphen."])
        assert result['gesamt'] == 0

    def test_extraktion_string(self):
        result = self.fn(["Gemaess §1 KSchG und §622 BGB gilt..."], pg_dsn="invalid://noop")
        assert result['gesamt'] == 2

    def test_extraktion_dict(self):
        result = self.fn([{"antwort": "Laut §558 BGB ist das zulaessig."}], pg_dsn="invalid://noop")
        assert result['gesamt'] == 1

    def test_fehlerrate_berechnung(self):
        result = self.fn(["§1 KSchG und §2 BGB"], pg_dsn="invalid://noop")
        assert result['gesamt'] == result['existieren'] + result['fehlen']
        assert len(result['fehler_liste']) == result['fehlen']

    def test_deduplizierung(self):
        result = self.fn(["§1 KSchG", "§1 KSchG wiederholt"], pg_dsn="invalid://noop")
        assert result['gesamt'] == 1

    def test_fehler_liste_ist_list(self):
        result = self.fn(["§999 DSGVO"], pg_dsn="invalid://noop")
        assert isinstance(result['fehler_liste'], list)


@pytest.mark.skipif(not HAS_DB, reason="PostgreSQL nicht erreichbar")
class TestSourceCheckerDB:
    def setup_method(self):
        from source_checker import check_source_references
        self.fn = check_source_references

    def test_existierende_referenz(self):
        result = self.fn(["Gemaess §2 BGB gilt folgendes..."])
        assert result['gesamt'] >= 1
        assert result['existieren'] >= 1

    def test_nicht_existierende_referenz(self):
        result = self.fn(["§9999 FIKTIVGESETZ ist relevant."])
        assert '§9999 FIKTIVGESETZ' in result['fehler_liste']

    def test_gesamt_equals_existieren_plus_fehlen(self):
        result = self.fn(["§1 KSchG und §9999 FIKTIV"])
        assert result['gesamt'] == result['existieren'] + result['fehlen']
