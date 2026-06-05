"""Qualitätsprüfung: §-Referenzen in Antworten gegen DB abgleichen."""
import re
import os

# §<nr>[letter] [Abs. X] [Nr. Y] <GESETZ>
_REF_RE = re.compile(
    r'§+\s*(\d+[a-z]?)'
    r'(?:\s+(?:Abs\.?|Satz|Nr\.?)\s*\S+)*'
    r'\s+([A-ZÄÖÜ][A-Za-zÄäÖöÜüß]{1,20})',
    re.UNICODE,
)

PG_DSN_DEFAULT = "host=host.docker.internal port=5432 dbname=lexwolf user=postgres password=postgres"


def _extract_refs(text: str) -> list:
    """Extrahiert §-Referenzen aus Text als normalisierte '§Nr Gesetz'-Strings."""
    return [f"§{nr} {gesetz}" for nr, gesetz in _REF_RE.findall(text)]


def _check_in_db(refs: list, pg_dsn: str) -> set:
    """Gibt die Teilmenge von refs zurück, die in legal_chunks existieren.

    Sucht im title-Feld: legal_chunks speichert '§ 626 Fristlose Kündigung...'
    (Leerzeichen zwischen § und Nummer, dann Leerzeichen + Titel).
    """
    if not refs:
        return set()
    import psycopg2

    existing: set = set()
    try:
        conn = psycopg2.connect(pg_dsn)
        try:
            with conn.cursor() as cur:
                for ref in refs:
                    m = re.match(r'§(\d+[a-z]?)', ref)
                    if not m:
                        continue
                    para_nr = m.group(1)
                    # DB-Format: '§ 626 Titel...' — Leerzeichen nach § und nach Nummer
                    cur.execute(
                        "SELECT 1 FROM legal_chunks WHERE title LIKE %s OR title LIKE %s LIMIT 1",
                        (f'§ {para_nr} %', f'§ {para_nr}'),
                    )
                    if cur.fetchone():
                        existing.add(ref)
        finally:
            conn.close()
    except Exception:
        pass
    return existing


def check_source_references(antwort_list: list, pg_dsn: str | None = None) -> dict:
    """Prüft ob §-Referenzen in Antworten tatsächlich in der DB existieren.

    Args:
        antwort_list: Liste von Antwort-Strings oder -Dicts (mit 'antwort'/'text'-Key)
        pg_dsn: PostgreSQL DSN; falls None wird PG_DSN aus Umgebung oder Default genutzt

    Returns:
        {
            'gesamt':     int  — eindeutige §-Referenzen gesamt,
            'existieren': int  — davon in DB gefunden,
            'fehlen':     int  — davon nicht in DB,
            'fehler_liste': list[str]  — die fehlenden Referenzen,
        }
    """
    if pg_dsn is None:
        pg_dsn = os.environ.get("PG_DSN", PG_DSN_DEFAULT)

    unique_refs: list = list(
        dict.fromkeys(
            ref
            for antwort in antwort_list
            for ref in _extract_refs(
                antwort.get("antwort", "") or antwort.get("text", "")
                if isinstance(antwort, dict)
                else str(antwort)
            )
        )
    )

    gesamt = len(unique_refs)
    if gesamt == 0:
        return {"gesamt": 0, "existieren": 0, "fehlen": 0, "fehler_liste": []}

    existing = _check_in_db(unique_refs, pg_dsn)
    fehler_liste = sorted(ref for ref in unique_refs if ref not in existing)

    return {
        "gesamt": gesamt,
        "existieren": len(existing),
        "fehlen": len(fehler_liste),
        "fehler_liste": fehler_liste,
    }
