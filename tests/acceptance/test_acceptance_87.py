import pytest, socket, sys, os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..', 'backend'))

DB_URL = "postgresql://postgres:postgres@localhost:5432/lexwolf"
SVC = os.path.join(os.path.dirname(__file__), '..', '..', 'backend', 'services', 'feedback_service.py')
API = os.path.join(os.path.dirname(__file__), '..', '..', 'backend', 'api', 'feedback.py')


def pg_ok():
    s = socket.socket(); r = s.connect_ex(("localhost", 5432)); s.close(); return r == 0


def test_feedback_service_existiert():
    assert os.path.exists(SVC), "backend/services/feedback_service.py fehlt"


def test_feedback_api_route_existiert():
    assert os.path.exists(API), "backend/api/feedback.py fehlt"
    content = open(API).read()
    assert '"/feedback"' in content or "'/feedback'" in content


def test_kategorisierung_paragraph():
    from services.feedback_service import FeedbackService
    kategorie = FeedbackService.kategorisiere_diff(
        "Nach § 1601 BGB besteht ein Anspruch.",
        "Nach § 1613 BGB besteht ein Anspruch.",
    )
    assert kategorie == "paragraph"


def test_kategorisierung_laenge():
    from services.feedback_service import FeedbackService
    kategorie = FeedbackService.kategorisiere_diff(
        "Kurzer Satz.",
        "Dies ist ein deutlich längerer und ausführlicherer Satz mit vielen mehr Wörtern als zuvor.",
    )
    assert kategorie == "laenge"


def test_kategorisierung_satzstruktur():
    from services.feedback_service import FeedbackService
    kategorie = FeedbackService.kategorisiere_diff(
        "Der Antrag wird abgelehnt, weil die Frist versäumt wurde.",
        "Der Antrag wird abgelehnt. Die Frist wurde versäumt.",
    )
    assert kategorie == "satzstruktur"


def test_kategorisierung_formulierung():
    from services.feedback_service import FeedbackService
    kategorie = FeedbackService.kategorisiere_diff(
        "Der Antrag wird abgelehnt.",
        "Der Antrag wird zurückgewiesen.",
    )
    assert kategorie == "formulierung"


def test_klartextname_wird_erkannt():
    from services.feedback_service import enthaelt_klartextnamen
    assert enthaelt_klartextnamen("Herr Müller hat unterschrieben.", "") is True
    assert enthaelt_klartextnamen("Kontakt: max.mustermann@example.com", "") is True


def test_anonymisierter_text_wird_akzeptiert():
    from services.feedback_service import enthaelt_klartextnamen
    assert enthaelt_klartextnamen(
        "Der Mandant hat unterschrieben.", "Der Mandant unterzeichnete."
    ) is False


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_feedback_wird_in_feedback_table_gespeichert():
    import psycopg2
    from services.feedback_service import FeedbackService

    svc = FeedbackService(db_url=DB_URL)
    ergebnis = svc.process_feedback({
        "original": "Der Antrag wird abgelehnt.",
        "korrigiert": "Der Antrag wird zurückgewiesen.",
    })
    assert ergebnis["kategorie"] == "formulierung"

    conn = psycopg2.connect(DB_URL)
    cur = conn.cursor()
    cur.execute("SELECT kategorie, original, korrigiert FROM feedback_table WHERE id = %s", (ergebnis["id"],))
    row = cur.fetchone()
    conn.close()
    assert row is not None, "Feedback-Eintrag wurde nicht in feedback_table gespeichert"
    assert row[0] == "formulierung"


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_klartextname_wird_bei_speicherung_abgelehnt():
    from services.feedback_service import FeedbackService, FeedbackValidationError

    svc = FeedbackService(db_url=DB_URL)
    with pytest.raises(FeedbackValidationError):
        svc.process_feedback({
            "original": "Herr Schmidt beantragt Unterhalt.",
            "korrigiert": "Herr Schmidt fordert Unterhalt.",
        })


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_post_api_feedback_endpoint():
    from fastapi.testclient import TestClient
    import main

    client = TestClient(main.app)
    response = client.post("/api/feedback", json={
        "original": "Der Vertrag wird gekündigt.",
        "korrigiert": "Der Vertrag wird beendet.",
    })
    assert response.status_code == 201
    body = response.json()
    assert body["status"] == "gespeichert"
    assert body["kategorie"] in ("formulierung", "satzstruktur", "paragraph", "laenge")


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_post_api_feedback_lehnt_klartextnamen_ab():
    from fastapi.testclient import TestClient
    import main

    client = TestClient(main.app)
    response = client.post("/api/feedback", json={
        "original": "Frau Weber erhält Unterhalt.",
        "korrigiert": "Frau Weber bekommt Unterhalt.",
    })
    assert response.status_code == 400
