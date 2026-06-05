import socket
import pytest
from datetime import date, timedelta

PG_DSN = "postgresql://postgres:postgres@localhost:5432/lexwolf"


def _pg_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 5432))
    s.close()
    return r == 0


def _smtp_ok():
    for port in (25, 587, 1025):
        s = socket.socket()
        s.settimeout(2)
        if s.connect_ex(("localhost", port)) == 0:
            s.close()
            return True
        s.close()
    return False


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_template_selection_alle_vier_cases():
    import psycopg2
    from backend.services.auto_reply_service import select_template, TEMPLATES

    conn = psycopg2.connect(PG_DSN)
    cur = conn.cursor()

    # Setup: bekannt+bezahlt (active), bekannt+nicht-bezahlt (inactive), unbekannt
    cur.execute("DELETE FROM users WHERE email LIKE 't152_%@test.local'")
    paid = (date.today() + timedelta(days=30)).isoformat()
    cur.execute(
        "INSERT INTO users (email, subscription_status, paid_until) VALUES"
        " ('t152_active@test.local', 'active', %s),"
        " ('t152_inactive@test.local', 'inactive', NULL)"
        " ON CONFLICT (email) DO UPDATE SET subscription_status=EXCLUDED.subscription_status, paid_until=EXCLUDED.paid_until",
        (paid,),
    )
    conn.commit()
    cur.close()
    conn.close()

    assert select_template("t152_active@test.local", has_ausschreibung=True) == "case1"
    assert select_template("t152_unknown@test.local", has_ausschreibung=True) == "case2"
    assert select_template("t152_inactive@test.local", has_ausschreibung=False) == "case3"
    assert select_template("t152_active@test.local", has_ausschreibung=False) == "case4"

    for key in ("case1", "case2", "case3", "case4"):
        assert key in TEMPLATES
        assert TEMPLATES[key].get("subject") and TEMPLATES[key].get("body")


@pytest.mark.skipif(not _smtp_ok(), reason="Kein SMTP-Server erreichbar (ports 25/587/1025)")
def test_smtp_send_auto_reply():
    from backend.services.auto_reply_service import send_auto_reply
    ok = send_auto_reply(
        to="recipient@test.local",
        case="case1",
        smtp_host="localhost",
        smtp_port=1025,
        smtp_user="",
        smtp_password="",
    )
    assert ok is True
