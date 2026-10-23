"""Akzeptanztests T#278: Qualitätssicherung Gutachten-Modus + Freigabe durch Anwalt."""
import json
import os
import socket
import sys

import pytest

_BASE = "/data/.openclaw/workspace-codex/projects/lexwolf"
_BASE_HOST = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
_ROOT = _BASE if os.path.isdir(_BASE) else _BASE_HOST
sys.path.insert(0, os.path.join(_ROOT, "backend"))

_DATASET_PATH = os.path.join(_ROOT, "tests", "quality", "gutachten_test_dataset.json")
_FREIGABE_STATUS_PATH = os.path.join(_ROOT, "docs", "gutachten_freigabe_status.json")

os.environ.setdefault("PG_DSN", "host=localhost port=5432 dbname=lexwolf user=postgres password=postgres")


def _pg_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 5432))
    s.close()
    return r == 0


def test_dataset_hat_15_bis_20_faelle():
    with open(_DATASET_PATH, encoding="utf-8") as f:
        dataset = json.load(f)
    assert 15 <= len(dataset) <= 20, f"Testset hat {len(dataset)} Fälle, gefordert sind 15-20"


def test_dataset_faelle_haben_pflichtfelder():
    with open(_DATASET_PATH, encoding="utf-8") as f:
        dataset = json.load(f)
    for fall in dataset:
        assert fall.get("sachverhalt"), f"{fall.get('id')}: sachverhalt fehlt"
        assert fall.get("erwartete_anspruchsgrundlagen"), f"{fall.get('id')}: erwartete_anspruchsgrundlagen fehlt"
        assert isinstance(fall.get("erwartete_einwaende"), list), f"{fall.get('id')}: erwartete_einwaende fehlt"


def test_mindestens_die_haelfte_der_faelle_hat_einwaende():
    """Die zusätzliche Metrik zielt auf Einwände — das Testset muss davon genug enthalten."""
    with open(_DATASET_PATH, encoding="utf-8") as f:
        dataset = json.load(f)
    faelle_mit_einwand = [f for f in dataset if f["erwartete_einwaende"]]
    assert len(faelle_mit_einwand) >= len(dataset) // 2


def test_einwaende_vollstaendigkeit_erkennt_vollstaendigen_fall():
    from services.gutachten_quality import einwaende_vollstaendigkeit

    ergebnis = {
        "anspruchsgrundlagen": [{
            "einwendungen_einreden": [
                {"bezeichnung": "Verjährung der Forderung", "norm": "§195 BGB", "greift": "offen"},
            ]
        }]
    }
    erwartete = [{"bezeichnung": "Verjährung", "norm": "§ 195 BGB"}]
    res = einwaende_vollstaendigkeit(ergebnis, erwartete)
    assert res["score"] == 1.0
    assert res["fehlend"] == []


def test_einwaende_vollstaendigkeit_erkennt_fehlenden_einwand_trotz_gleicher_grundnorm():
    """Regressionstest für den beim Implementieren gefundenen Bug: zwei
    verschiedene Einwände zur selben Grundnorm (§ 626 BGB) dürfen sich nicht
    gegenseitig als 'gefunden' durchgehen lassen."""
    from services.gutachten_quality import einwaende_vollstaendigkeit

    ergebnis = {
        "anspruchsgrundlagen": [{
            "einwendungen_einreden": [
                {"bezeichnung": "Fehlende vorherige Abmahnung", "norm": "§ 626 BGB", "greift": "offen"},
            ]
        }]
    }
    erwartete = [
        {"bezeichnung": "Fehlende vorherige Abmahnung", "norm": "§ 626 BGB"},
        {"bezeichnung": "Zwei-Wochen-Frist für außerordentliche Kündigung", "norm": "§ 626 Abs. 2 BGB"},
    ]
    res = einwaende_vollstaendigkeit(ergebnis, erwartete)
    assert res["score"] == 0.5, "Der fehlende zweite Einwand darf nicht durch den ersten verdeckt werden"
    assert len(res["fehlend"]) == 1
    assert "Zwei-Wochen-Frist" in res["fehlend"][0]["bezeichnung"]


def test_schema_validierung_wird_in_qualitaetstor_eingebunden():
    from services.gutachten_quality import pruefe_gutachten_qualitaet

    pruefung = pruefe_gutachten_qualitaet(
        sachverhalt="Test",
        ergebnis={"anspruchsgrundlagen": []},
        erwartete_einwaende=[],
    )
    assert pruefung["bestanden"] is False
    assert any("Schema ungültig" in g for g in pruefung["gruende_blockiert"])


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_normpruefung_gegen_echte_db_erkennt_existierende_norm():
    """Echte DB-Abfrage (kein Mock) gegen legal_chunks."""
    from services.gutachten_quality import pruefe_normen_in_db

    ergebnis = {
        "anspruchsgrundlagen": [{
            "norm": "§ 433 BGB",
            "voraussetzungen": [],
            "einwendungen_einreden": [],
        }]
    }
    result = pruefe_normen_in_db(ergebnis)
    assert result["gesamt"] == 1
    assert result["existieren"] == 1, f"§ 433 BGB sollte in der echten DB existieren: {result}"
    assert result["fehlen"] == 0


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_normpruefung_gegen_echte_db_erkennt_erfundene_norm():
    """Echte DB-Abfrage (kein Mock): eine garantiert nicht existierende Norm
    muss als fehlend erkannt werden."""
    from services.gutachten_quality import pruefe_normen_in_db

    ergebnis = {
        "anspruchsgrundlagen": [{
            "norm": "§ 99999z ErfundenesGesetzbuch",
            "voraussetzungen": [],
            "einwendungen_einreden": [],
        }]
    }
    result = pruefe_normen_in_db(ergebnis)
    assert result["fehlen"] == 1
    assert "§ 99999z ErfundenesGesetzbuch" in result["fehlende_normen"]


def test_produktionsfreigabe_ist_standardmaessig_nicht_erteilt():
    """Ohne schriftliche Freigabe durch den Anwalt darf der Gutachten-Modus
    nicht produktiv laufen — der Default muss False sein."""
    from services.gutachten_quality import ist_produktionsfreigabe_erteilt

    assert ist_produktionsfreigabe_erteilt(_FREIGABE_STATUS_PATH) is False


def test_freigabe_status_datei_existiert_und_hat_pflichtfelder():
    assert os.path.exists(_FREIGABE_STATUS_PATH), "docs/gutachten_freigabe_status.json fehlt"
    with open(_FREIGABE_STATUS_PATH, encoding="utf-8") as f:
        status = json.load(f)
    assert "freigegeben" in status
    assert "freigegeben_durch" in status


def test_produktionsfreigabe_greift_wenn_anwalt_freigibt(tmp_path):
    """Belegt, dass der Freigabe-Mechanismus bei einer echten Freigabe auch
    tatsächlich auf True umschaltet (ohne die reale Statusdatei zu verändern)."""
    from services.gutachten_quality import ist_produktionsfreigabe_erteilt

    fake_status = tmp_path / "freigabe_status.json"
    fake_status.write_text(
        json.dumps({"freigegeben": True, "freigegeben_durch": "Dr. Mustermann", "datum": "2026-10-23"}),
        encoding="utf-8",
    )
    assert ist_produktionsfreigabe_erteilt(str(fake_status)) is True
