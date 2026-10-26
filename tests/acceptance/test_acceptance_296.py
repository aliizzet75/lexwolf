"""Task #296: Backend: Positionsangabe (Absatz/Satz-Index) pro Quelle mitliefern.

/ask und /chat liefern pro zitierter Quelle eine exakte Positionsangabe
(chunk_id = id des legal_chunks-Eintrags aus der bestehenden M7-Chunking-
Pipeline, services/paragraph_parser.py bzw. crawlers/gesetze_crawler.py) —
keine neue Parsing-Logik, nur bestehende Daten (die ohnehin schon pro Chunk
vorliegende id) durchreichen.

Läuft gegen die echte Postgres-DB (localhost:5432/lexwolf), keine Mocks/SQLite.
Nur der LLM-Call (ask_llm) wird im /chat-Test gemockt, um keine echten
API-Kosten zu verursachen und eine deterministische Antwort zu erhalten.
"""
import os
import sys
import pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent.parent / "backend"))
os.environ.setdefault("DATABASE_URL", "postgresql://postgres:postgres@localhost:5432/lexwolf")

import pytest
from sqlalchemy import text as sqltxt
from fastapi.testclient import TestClient

from services.database_service import DatabaseService


@pytest.fixture(scope="module")
def db_service():
    try:
        svc = DatabaseService()
        db = svc.SessionLocal()
        db.execute(sqltxt("SELECT 1"))
        db.close()
    except Exception as e:
        pytest.skip(f"Postgres nicht erreichbar: {e}")
    yield svc


@pytest.fixture(scope="module")
def client():
    from main import app
    return TestClient(app)


def test_ask_response_liefert_chunk_id_pro_quelle(client, db_service):
    """/ask: jede Quelle in 'sources' trägt eine chunk_id (Positionsangabe,
    T#296) — übernommen aus der id, die bereits aus legal_chunks kommt."""
    resp = client.post("/ask", json={"text": "Was regelt § 433 BGB?"})
    assert resp.status_code == 200
    data = resp.json()

    assert "sources" in data
    assert isinstance(data["sources"], list)
    assert len(data["sources"]) >= 1

    for src in data["sources"]:
        assert "chunk_id" in src
        assert src["chunk_id"] is not None
        assert isinstance(src["chunk_id"], int)

    # § 433 BGB ist real gecrawlt -> die chunk_id muss auf einen tatsächlich
    # existierenden legal_chunks-Eintrag zu § 433 BGB verweisen.
    db = db_service.SessionLocal()
    try:
        ids = {src["chunk_id"] for src in data["sources"]}
        rows = db.execute(sqltxt(
            "SELECT id FROM legal_chunks WHERE id = ANY(:ids)"), {"ids": list(ids)}).fetchall()
        found_ids = {r.id for r in rows}
        assert found_ids == ids, "chunk_id muss auf real existierende legal_chunks-Zeilen zeigen"
    finally:
        db.close()


def test_chat_quellen_check_liefert_chunk_id_fuer_bekannte_norm(client, db_service, monkeypatch):
    """/chat: quellen_check-Einträge für real existierende Normen tragen eine
    chunk_id (Positionsangabe, T#296), die auf den gefundenen legal_chunks-
    Eintrag zeigt."""
    import api.chat as chat_module

    fake_answer = (
        "Sehr geehrte Damen und Herren,\n\n"
        "gemäß § 433 BGB ist der Verkäufer verpflichtet, die Kaufsache zu übergeben. "
        "Daher besteht ein Anspruch auf Erfüllung.\n\n"
        "Mit freundlichen Grüßen\nAnwalt"
    )
    monkeypatch.setattr(chat_module, "ask_llm", lambda *a, **k: fake_answer)

    resp = client.post("/chat", json={
        "messages": [{"role": "user", "content": "Was regelt § 433 BGB?"}],
    })
    assert resp.status_code == 200
    data = resp.json()

    zitat = next(z for z in data["quellen_check"] if "433" in z["zitat"])
    assert "chunk_id" in zitat
    assert zitat["geprueft"] is True
    assert zitat["existiert"] is True
    assert isinstance(zitat["chunk_id"], int)

    db = db_service.SessionLocal()
    try:
        row = db.execute(sqltxt(
            "SELECT id, title, tags FROM legal_chunks WHERE id = :id"), {"id": zitat["chunk_id"]}).first()
        assert row is not None
        assert row.tags == "bgb"
        assert "433" in row.title
    finally:
        db.close()


def test_chat_quellen_check_chunk_id_ist_none_wenn_nicht_in_db(client, monkeypatch):
    """Zitat einer frei erfundenen Norm, die nicht in der DB existiert:
    chunk_id bleibt None statt eine falsche Position zu behaupten."""
    import api.chat as chat_module

    fake_answer = (
        "Gemäß § 999 Fantasiegesetz besteht ein Anspruch. "
        "Mit freundlichen Grüßen"
    )
    monkeypatch.setattr(chat_module, "ask_llm", lambda *a, **k: fake_answer)

    resp = client.post("/chat", json={
        "messages": [{"role": "user", "content": "Gibt es dazu eine Regelung?"}],
    })
    assert resp.status_code == 200
    data = resp.json()
    zitat = next(z for z in data["quellen_check"] if "999" in z["zitat"])
    assert zitat["existiert"] is False
    assert zitat["chunk_id"] is None
