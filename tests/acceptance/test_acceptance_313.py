"""Task #313: search() darf bei Exception keine Mock-Treffer (score=0.0, ohne
ORDER BY) mehr liefern. Läuft gegen echte Postgres-DB, nur Embedding gemockt."""
import logging
import os
import pathlib
import socket
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent.parent / "backend"))
os.environ.setdefault("DATABASE_URL", "postgresql://postgres:postgres@localhost:5432/lexwolf")

import pytest


def _pg_ok() -> bool:
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 5432))
    s.close()
    return r == 0


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf Port 5432")
def test_search_liefert_leere_liste_statt_mock_treffer_bei_exception(monkeypatch, caplog):
    from services.search_service import HybridSearchService

    svc = HybridSearchService()

    def kaputte_embedding(_text):
        raise RuntimeError("simulierte kaputte DB-Verbindung")

    monkeypatch.setattr(svc.embedding_service, "generate_embedding", kaputte_embedding)

    with caplog.at_level(logging.ERROR):
        results = svc.search("Kündigungsschutzklage", limit=5, fast_mode=True)

    assert results == [], "Bei Exception dürfen keine Mock-Chunks zurückkommen"
    assert any(r.levelno == logging.ERROR for r in caplog.records), "Fehler muss geloggt werden"
