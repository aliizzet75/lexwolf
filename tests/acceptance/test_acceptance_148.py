import pytest
import socket
import psycopg2

PG_DSN = "postgresql://postgres:postgres@localhost:5432/lexwolf"


def pg_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 5432))
    s.close()
    return r == 0


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_users_tabelle_existiert():
    with psycopg2.connect(PG_DSN) as conn:
        cur = conn.cursor()
        cur.execute(
            "SELECT column_name FROM information_schema.columns "
            "WHERE table_schema='public' AND table_name='users' ORDER BY column_name"
        )
        cols = {r[0] for r in cur.fetchall()}
    assert cols, "Tabelle 'users' existiert nicht"
    assert {"email", "subscription_status", "paid_until", "created_at"} <= cols, (
        f"Pflichtfelder fehlen. Gefunden: {cols}"
    )


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_check_subscription_unbekannte_email():
    from backend.services.subscription_service import check_subscription
    result = check_subscription("nobody@example.com")
    assert result == "UNKNOWN", f"Erwartet UNKNOWN, erhalten: {result}"


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_check_subscription_rueckgabewerte():
    from backend.services.subscription_service import check_subscription
    result = check_subscription("test@example.com")
    assert result in ("ACTIVE", "INACTIVE", "UNKNOWN"), (
        f"Ungültiger Rückgabewert: {result}"
    )
