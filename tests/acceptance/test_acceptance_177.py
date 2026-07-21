import socket
import pytest


def _pg_ok():
    s = socket.socket(); s.settimeout(2); r = s.connect_ex(("localhost", 5432)); s.close(); return r == 0


def _neo4j_ok():
    s = socket.socket(); s.settimeout(2); r = s.connect_ex(("localhost", 7687)); s.close(); return r == 0


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_pg_verbindung():
    import psycopg2
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute("SELECT 1")
    assert cur.fetchone()[0] == 1
    conn.close()


@pytest.mark.skipif(not _neo4j_ok(), reason="Neo4j nicht erreichbar auf port 7687")
def test_neo4j_verbindung():
    from neo4j import GraphDatabase
    driver = GraphDatabase.driver("bolt://localhost:7687", auth=("neo4j", "lexwolf123"))
    driver.verify_connectivity()
    driver.close()
