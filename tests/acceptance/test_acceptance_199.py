import os
import re
import socket
import requests
import pytest

MAIN_CS = os.path.join(os.path.dirname(__file__), '..', '..', 'tools', 'Anonymisierer', 'Main.cs')
NER_URL = "http://localhost:8000/ner"


def ner_ok():
    s = socket.socket(); s.settimeout(2)
    r = s.connect_ex(("localhost", 8000)); s.close()
    return r == 0


@pytest.mark.skipif(not ner_ok(), reason="NER-Backend nicht erreichbar auf localhost:8000")
def test_auszug_kredit_wird_von_spacy_nicht_als_person_erkannt():
    resp = requests.post(NER_URL, json={"text": "Auszug Kredit"}, timeout=5)
    assert resp.status_code == 200
    entities = resp.json()["entities"]
    assert not any(e["text"] == "Auszug Kredit" for e in entities), \
        "'Auszug Kredit' sollte von spaCy NICHT als PER klassifiziert werden (DoD-Voraussetzung)"


def test_load_mapping_validiert_person_eintraege_gegen_ner():
    src = open(MAIN_CS, encoding='utf-8').read()
    m = re.search(r'private static (?:async Task|void) LoadMappingFromFile\(\)\s*\{(.*?)\n        \}', src, re.S)
    assert m, "LoadMappingFromFile() nicht gefunden in Main.cs"
    body = m.group(1)
    assert 'NerEndpoint' in body or '_http' in body, \
        "LoadMappingFromFile muss POST /ner zur Validierung von Person-Eintraegen aufrufen"
    assert '_mapping.Remove' in body and '_entityTypes.Remove' in body, \
        "Falsch-positive Person-Eintraege muessen aus _mapping und _entityTypes entfernt werden"
    assert 'catch' in body, \
        "Fallback fuer nicht erreichbares NER (Eintraege behalten) fehlt"
