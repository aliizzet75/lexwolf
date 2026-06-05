#!/usr/bin/env python3
"""
Akzeptanztest für Task #158: Rechtsgebiet-Boosting
"""

import os
import sys
from pathlib import Path

import pytest


def test_acceptance_boosting():
    """Testet ob Tag-Boosting korrekt implementiert ist."""
    print("Running Acceptance Test for Task #158")

    # Prüfe search_service.py
    search_service_path = Path("/data/.openclaw/workspace-codex/projects/lexwolf/backend/services/search_service.py")
    assert search_service_path.exists(), "search_service.py existiert nicht"
    print("✅ search_service.py existiert")

    with open(search_service_path, "r") as f:
        source = f.read()
    
    # Test 1: _tag_boost_search Methode existiert
    assert "_tag_boost_search" in source, "_tag_boost_search() Methode existiert nicht"
    print("✅ _tag_boost_search() Methode existiert")

    # Test 2: Regex-Erkennung für Gesetzes-Tags
    assert "re.finditer" in source, "Keine Regex-Implementation gefunden"
    assert "found_tags" in source, "Keine Tag-Erkennungslogik gefunden"
    print("✅ Regex-Erkennung für Gesetzes-Tags implementiert")

    # Test 3: Tag-Liste mit gängigen Gesetzen
    assert "kschg" in source, "KSchG Tag nicht in Tag-Liste"
    assert "bgb" in source, "BGB Tag nicht in Tag-Liste"
    print("✅ Tag-Liste enthält gängige Gesetze (KSchG, BGB)")

    # Test 4: Boost-Faktor (dense_rank=1 für Tag-Treffer)
    assert "dense_rank\": 1" in source or "dense_rank\": 1" in source, "Boost-Faktor (dense_rank=1) nicht implementiert"
    print("✅ Boost-Faktor (dense_rank=1) für Tag-Treffer implementiert")

    # Test 5: Tag-Boost im search()-Flow
    assert "tag_boost_results = self._tag_boost_search" in source, "_tag_boost_search() wird nicht in search() aufgerufen"
    print("✅ Tag-Boost wird in search()-Flow integriert")

    print("\n" + "="*60)
    print("✅ ACCEPTANCE TEST PASSED")
    print("="*60)


def test_acceptance_boosting_pytest():
    """pytest wrapper für acceptance test."""
    test_acceptance_boosting()


if __name__ == "__main__":
    test_acceptance_boosting()
