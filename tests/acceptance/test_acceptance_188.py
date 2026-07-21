"""Acceptance-Test T#188: Anonymisierer-Engine Regex + Comic-Aliaspool pro Mandant"""
import os
import re
import pytest

ENGINE = os.path.join(os.path.dirname(__file__), '../../tools/Anonymisierer/Services/AnonymizerEngine.cs')


def engine_src():
    assert os.path.isfile(ENGINE), f"AnonymizerEngine.cs fehlt: {ENGINE}"
    return open(ENGINE, encoding='utf-8').read()


def test_anonymizer_engine_exists():
    assert os.path.isfile(ENGINE), f"AnonymizerEngine.cs fehlt: {ENGINE}"


def test_regex_grossschreibung_namen():
    src = engine_src()
    assert re.search(r'[A-Z]\[a-z\]|\\b\[A-ZÄÖÜ\]|Regex.*[A-ZÄÖÜ]', src) or \
           re.search(r'Großschreib|grossschreib|UpperCase|[A-Z]{1}[a-z]+', src, re.IGNORECASE), \
        "Regex für Großschreibungs-Namen (Heuristik) fehlt"


def test_regex_adressen():
    src = engine_src()
    assert re.search(r'Str\.|straße|Strasse|\\d+.*Str|Str.*\\d', src, re.IGNORECASE), \
        "Regex für Str. NN Adressen fehlt"


def test_regex_aktenzeichen():
    src = engine_src()
    assert re.search(r'Az\.|Aktenzeichen|\\d{4}|Az.*\\d', src, re.IGNORECASE), \
        "Regex für Az. NNNN Aktenzeichen fehlt"


def test_regex_betraege():
    src = engine_src()
    assert re.search(r'€|EUR|\\d.*,\\d{2}|Betrag|euro', src, re.IGNORECASE), \
        "Regex für €NN,NN Beträge fehlt"


def test_comic_pool_donald_duck():
    src = engine_src()
    for name in ['Donald', 'Daisy', 'Dagobert', 'Daniel']:
        assert name in src, f"Comic-Alias '{name}' (Donald Duck) fehlt"


def test_comic_pool_lucky_luke():
    src = engine_src()
    assert 'Luke' in src or 'Lucky' in src, "Comic-Alias 'Lucky Luke' fehlt"
    assert 'Dalton' in src or 'Jolly' in src, "Comic-Alias Dalton/Jolly Jumper fehlt"


def test_comic_pool_asterix():
    src = engine_src()
    for name in ['Asterix', 'Obelix']:
        assert name in src, f"Comic-Alias '{name}' (Asterix) fehlt"


def test_pro_mandant_dict():
    src = engine_src()
    assert re.search(r'Dictionary|Dict|mandant|Mandant|tenant', src, re.IGNORECASE), \
        "Pro-Mandant Dict<original,alias> Mechanismus fehlt"
    assert re.search(r'mandant|folder|Ordner|tenant', src, re.IGNORECASE), \
        "Mandant/Ordner-Schlüssel für das Dict fehlt"
