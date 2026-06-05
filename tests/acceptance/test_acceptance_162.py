import re
import sys
import pytest

sys.path.insert(0, "/home/claudeuser/aligator")


def _board_ok():
    import socket
    s = socket.socket(); r = s.connect_ex(("localhost", 8082)); s.close(); return r == 0


ABHAENGIG_RE = re.compile(r"ABHÄNGIG_VON:\s*([T#\d,\s]+)")


def _parse_deps(notizen: str) -> list[int]:
    m = ABHAENGIG_RE.search(notizen or "")
    if not m:
        return []
    return [int(x) for x in re.findall(r"\d+", m.group(1))]


def test_regex_parst_abhaengigkeiten():
    ids = _parse_deps("ABHÄNGIG_VON: T#156, T#157, T#158\nSonstige Notizen")
    assert ids == [156, 157, 158]


def test_keine_abhaengigkeiten_leer():
    assert _parse_deps("Normale Notizen ohne Block") == []


@pytest.mark.skipif(not _board_ok(), reason="Board API nicht erreichbar auf port 8082")
def test_aufgeloeste_abhaengigkeit_erlaubt_start():
    from aligator import get_tasks, parse_abhaengigkeiten, abhaengigkeiten_geloest
    tasks = get_tasks(1)
    fertige_ids = {t["id"] for t in tasks if t["status"] == "fertig"}
    assert fertige_ids, "Keine fertigen Tasks vorhanden"
    sample_id = next(iter(fertige_ids))
    dep_ids = [sample_id]
    assert abhaengigkeiten_geloest(dep_ids, tasks), \
        f"T#{sample_id} ist fertig — Abhängigkeit sollte als gelöst gelten"


@pytest.mark.skipif(not _board_ok(), reason="Board API nicht erreichbar auf port 8082")
def test_ungeloeste_abhaengigkeit_blockiert():
    from aligator import get_tasks, abhaengigkeiten_geloest
    tasks = get_tasks(1)
    dep_ids = [999999]
    assert not abhaengigkeiten_geloest(dep_ids, tasks), \
        "T#999999 existiert nicht — Abhängigkeit sollte als ungelöst gelten"
