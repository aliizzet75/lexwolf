#!/usr/bin/env python3
"""
Qualitätstest für den Gutachten-Modus (T#278).

Zwei Modi:

- Self-Test (Default, kein API-Key nötig): baut aus jedem Testfall in
  gutachten_test_dataset.json ein plausibles Gutachten-JSON (konstruiert aus
  den im Testfall erwarteten Anspruchsgrundlagen/Einwänden) und führt die
  QA-Pipeline (backend/services/gutachten_quality.py) inkl. echter
  Normprüfung gegen die Postgres-DB aus. Zusätzlich werden 3 bewusst
  FEHLERHAFTE Fixtures (fehlender Einwand, erfundene Norm, Schema-Fehler)
  durchlaufen, um zu beweisen, dass das Qualitätstor solche Fälle tatsächlich
  blockiert statt immer "bestanden" zu melden.

- Live-Modus (--live, braucht ANTHROPIC_API_KEY oder LLM_PROVIDER=ollama):
  ruft für jeden Testfall den echten Gutachtenstil-Prompt
  (backend/services/gutachten_prompt.py) über ask_llm() auf. Das setzt
  voraus, dass die Prüfungs-Engine (T#274/T#275) bereits läuft — Stand
  T#278 ist das noch nicht der Fall, daher ist dieser Modus hier nur
  vorbereitet und nicht der primäre Qualitätsnachweis.
"""
import json
import logging
import os
import sys

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
logger = logging.getLogger(__name__)

QUALITY_DIR = os.path.dirname(os.path.abspath(__file__))
BACKEND = os.path.abspath(os.path.join(QUALITY_DIR, "..", "..", "backend"))
sys.path.insert(0, BACKEND)

os.environ.setdefault("DATABASE_URL", "postgresql://postgres:postgres@localhost:5432/lexwolf")
os.environ.setdefault("PG_DSN", "host=localhost port=5432 dbname=lexwolf user=postgres password=postgres")
os.environ.setdefault("NEO4J_URI", "bolt://localhost:7687")
os.environ.setdefault("NEO4J_USER", "neo4j")
os.environ.setdefault("NEO4J_PASSWORD", "lexwolf123")

DATASET_PATH = os.path.join(QUALITY_DIR, "gutachten_test_dataset.json")
RESULTS_PATH = os.path.join(QUALITY_DIR, "gutachten_results.json")


def _fixture_gutachten(fall: dict, vollstaendig: bool = True, mit_fehlerhafter_norm: bool = False) -> dict:
    """Baut ein plausibles Gutachten-JSON aus den im Testfall hinterlegten
    Erwartungen — für den Self-Test der QA-Pipeline, nicht als echte KI-Antwort."""
    agls = fall["erwartete_anspruchsgrundlagen"]
    einwaende = list(fall["erwartete_einwaende"])
    if not vollstaendig and einwaende:
        einwaende = einwaende[:-1]  # bewusst den letzten erwarteten Einwand weglassen

    voraussetzungen = [{
        "merkmal": "Tatbestandsmerkmal 1",
        "norm": agls[0]["norm"],
        "quelle": "Gesetzestext",
        "status": "erfüllt",
        "begruendung": "Im Sachverhalt dargelegt.",
    }]

    einwendungen_json = []
    for e in einwaende:
        norm = e["norm"]
        if mit_fehlerhafter_norm:
            norm = "§ 99999z ErfundenesGesetzbuch"
        einwendungen_json.append({
            "bezeichnung": e["bezeichnung"],
            "norm": norm,
            "greift": "offen",
            "begruendung": "Im Rahmen des Gutachtens zu prüfen.",
        })

    anspruchsgrundlagen_json = [{
        "bezeichnung": agls[0]["bezeichnung"],
        "norm": agls[0]["norm"],
        "obersatz": f"Der Mandant könnte einen Anspruch aus {agls[0]['norm']} haben.",
        "voraussetzungen": voraussetzungen,
        "entstanden": {"status": "ja", "begruendung": "Siehe Voraussetzungen."},
        "nicht_erloschen": {"status": "offen", "begruendung": "Einwände zu prüfen."},
        "durchsetzbar": {"status": "offen", "begruendung": "Einwände zu prüfen."},
        "einwendungen_einreden": einwendungen_json,
        "rechtsprechung": [],
        "ergebnis": "offen",
    }]

    return {
        "anspruchsgrundlagen": anspruchsgrundlagen_json,
        "erfolgsprognose": {"ampel": fall["erwartete_ampel_bereich"][0], "begruendung": "Testfixture."},
        "gesamtergebnis": "Testfixture für Qualitätsprüfung des Gutachten-Modus (T#278).",
        "offene_punkte": [],
    }


def run_self_test() -> dict:
    from services.gutachten_quality import pruefe_gutachten_qualitaet

    with open(DATASET_PATH, encoding="utf-8") as f:
        dataset = json.load(f)

    ergebnisse = []
    for fall in dataset:
        gutachten = _fixture_gutachten(fall, vollstaendig=True)
        pruefung = pruefe_gutachten_qualitaet(
            sachverhalt=fall["sachverhalt"],
            ergebnis=gutachten,
            erwartete_einwaende=fall["erwartete_einwaende"],
        )
        ergebnisse.append({
            "id": fall["id"],
            "rechtsgebiet": fall["rechtsgebiet"],
            "bestanden": pruefung["bestanden"],
            "einwaende_vollstaendigkeit": pruefung["einwaende_vollstaendigkeit"],
            "normen_fehlen": pruefung["normen_pruefung"]["fehlen"],
            "judge_score": pruefung["judge_score"],
            "gruende_blockiert": pruefung["gruende_blockiert"],
        })
        status = "OK" if pruefung["bestanden"] else "BLOCKIERT"
        logger.info(
            f"[{fall['id']}] {fall['rechtsgebiet']}: {status} "
            f"(Einwände {pruefung['einwaende_vollstaendigkeit']:.0%}, "
            f"Normen fehlend {pruefung['normen_pruefung']['fehlen']}, "
            f"Judge {pruefung['judge_score']}/5)"
        )

    # Negativtests: beweisen, dass das Qualitätstor echte Mängel erkennt.
    negativ_faelle = []
    testfall_mit_einwand = next(f for f in dataset if len(f["erwartete_einwaende"]) >= 2)

    unvollstaendig = _fixture_gutachten(testfall_mit_einwand, vollstaendig=False)
    pruefung_unvollstaendig = pruefe_gutachten_qualitaet(
        sachverhalt=testfall_mit_einwand["sachverhalt"],
        ergebnis=unvollstaendig,
        erwartete_einwaende=testfall_mit_einwand["erwartete_einwaende"],
    )
    negativ_faelle.append({
        "name": "fehlender_einwand",
        "bestanden": pruefung_unvollstaendig["bestanden"],
        "erwartet_blockiert": True,
        "gruende_blockiert": pruefung_unvollstaendig["gruende_blockiert"],
    })

    erfundene_norm = _fixture_gutachten(testfall_mit_einwand, vollstaendig=True, mit_fehlerhafter_norm=True)
    pruefung_erfunden = pruefe_gutachten_qualitaet(
        sachverhalt=testfall_mit_einwand["sachverhalt"],
        ergebnis=erfundene_norm,
        erwartete_einwaende=testfall_mit_einwand["erwartete_einwaende"],
    )
    negativ_faelle.append({
        "name": "erfundene_norm",
        "bestanden": pruefung_erfunden["bestanden"],
        "erwartet_blockiert": True,
        "gruende_blockiert": pruefung_erfunden["gruende_blockiert"],
    })

    schema_kaputt = {"anspruchsgrundlagen": []}
    pruefung_schema = pruefe_gutachten_qualitaet(
        sachverhalt=testfall_mit_einwand["sachverhalt"],
        ergebnis=schema_kaputt,
        erwartete_einwaende=testfall_mit_einwand["erwartete_einwaende"],
    )
    negativ_faelle.append({
        "name": "schema_ungueltig",
        "bestanden": pruefung_schema["bestanden"],
        "erwartet_blockiert": True,
        "gruende_blockiert": pruefung_schema["gruende_blockiert"],
    })

    for n in negativ_faelle:
        status = "OK (korrekt blockiert)" if (n["erwartet_blockiert"] and not n["bestanden"]) else "FEHLER (Gate hat Mangel NICHT erkannt)"
        logger.info(f"[Negativtest {n['name']}] {status}: {n['gruende_blockiert']}")

    bestanden_count = sum(1 for e in ergebnisse if e["bestanden"])
    avg_einwaende = sum(e["einwaende_vollstaendigkeit"] for e in ergebnisse) / len(ergebnisse)
    gesamt_normen_fehlen = sum(e["normen_fehlen"] for e in ergebnisse)

    report = {
        "getestete_faelle": len(ergebnisse),
        "bestanden": bestanden_count,
        "bestehensrate": bestanden_count / len(ergebnisse),
        "avg_einwaende_vollstaendigkeit": avg_einwaende,
        "normen_fehlen_gesamt": gesamt_normen_fehlen,
        "negativtests_korrekt_erkannt": sum(
            1 for n in negativ_faelle if n["erwartet_blockiert"] and not n["bestanden"]
        ),
        "negativtests_gesamt": len(negativ_faelle),
        "ergebnisse": ergebnisse,
        "negativ_faelle": negativ_faelle,
    }

    with open(RESULTS_PATH, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)

    logger.info("=== GESAMT ===")
    logger.info(f"Bestanden: {bestanden_count}/{len(ergebnisse)} ({report['bestehensrate']:.0%})")
    logger.info(f"Ø Einwände-Vollständigkeit: {avg_einwaende:.0%}")
    logger.info(f"Normen nicht in DB gefunden (gesamt): {gesamt_normen_fehlen}")
    logger.info(
        f"Negativtests korrekt erkannt: {report['negativtests_korrekt_erkannt']}/{report['negativtests_gesamt']}"
    )
    return report


if __name__ == "__main__":
    if "--live" in sys.argv:
        logger.error("Live-Modus setzt eine lauffähige Prüfungs-Engine (T#274/T#275) voraus, "
                      "die zum Zeitpunkt von T#278 noch nicht implementiert ist (Status 'geplant'). "
                      "Führe den Self-Test ohne --live aus.")
        sys.exit(2)
    ergebnis = run_self_test()
    sys.exit(0 if ergebnis["bestehensrate"] == 1.0 and ergebnis["negativtests_korrekt_erkannt"] == ergebnis["negativtests_gesamt"] else 1)
