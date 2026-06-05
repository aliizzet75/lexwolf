import json
import os
import pytest

import pathlib as _pathlib
_here = _pathlib.Path(__file__).resolve().parent.parent / "quality" / "test_dataset.json"
DATASET_PATH = str(_here)

def dataset_exists():
    return os.path.exists(DATASET_PATH)

@pytest.fixture(scope="module")
def dataset():
    with open(DATASET_PATH, "r", encoding="utf-8") as f:
        return json.load(f)

@pytest.mark.skipif(not dataset_exists(), reason="test_dataset.json nicht vorhanden")
class TestDataset55:
    def test_mindestens_10_fragen(self, dataset):
        assert len(dataset) >= 10, f"Nur {len(dataset)} Fragen, mindestens 10 erwartet"

    def test_pflichtfelder_vorhanden(self, dataset):
        for item in dataset:
            assert "frage" in item, f"Feld 'frage' fehlt in ID {item.get('id')}"
            assert "erwartete_paragraphen" in item, f"Feld 'erwartete_paragraphen' fehlt in ID {item.get('id')}"
            assert "schwierigkeit" in item, f"Feld 'schwierigkeit' fehlt in ID {item.get('id')}"

    def test_frage_nicht_leer(self, dataset):
        for item in dataset:
            assert item["frage"].strip(), f"Leere Frage in ID {item.get('id')}"

    def test_paragraphen_sind_liste(self, dataset):
        for item in dataset:
            assert isinstance(item["erwartete_paragraphen"], list), f"erwartete_paragraphen ist keine Liste in ID {item.get('id')}"

    def test_verschiedene_rechtsgebiete(self, dataset):
        themen = set()
        for item in dataset:
            themen.update(item.get("erwartete_themen", []))
        rechtsgebiete = {"Kündigungsschutz", "Mietrecht", "Unterhalt", "Arbeitsrecht", "Familienrecht"}
        abgedeckt = themen & rechtsgebiete
        assert len(abgedeckt) >= 2, f"Zu wenige Rechtsgebiete abgedeckt: {abgedeckt}"
