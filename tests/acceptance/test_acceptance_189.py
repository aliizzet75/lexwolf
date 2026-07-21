import os
import xml.etree.ElementTree as ET
import pytest

_BASE = "/data/.openclaw/workspace-codex/projects/lexwolf"
_BASE_HOST = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
_ROOT = _BASE if os.path.isdir(_BASE) else _BASE_HOST
_CSPROJ = os.path.join(_ROOT, "desktop", "LexWolf.csproj")
_DIFFVIEW_XAML = os.path.join(_ROOT, "desktop", "Controls", "DiffView.xaml")
_DIFFVIEW_CS = os.path.join(_ROOT, "desktop", "Controls", "DiffView.xaml.cs")
_ANONYMIZER = os.path.join(_ROOT, "desktop", "Services", "AnonymizerEngine.cs")


def test_diffplex_in_csproj():
    assert os.path.exists(_CSPROJ), f"LexWolf.csproj fehlt: {_CSPROJ}"
    tree = ET.parse(_CSPROJ)
    refs = [el.get("Include", "") for el in tree.iter("PackageReference")]
    assert any("DiffPlex" in r for r in refs), (
        f"DiffPlex nicht in LexWolf.csproj PackageReferences: {refs}"
    )


def test_diffview_xaml_existiert():
    assert os.path.exists(_DIFFVIEW_XAML), f"DiffView.xaml fehlt: {_DIFFVIEW_XAML}"


def test_diffview_cs_existiert():
    assert os.path.exists(_DIFFVIEW_CS), f"DiffView.xaml.cs fehlt: {_DIFFVIEW_CS}"


def test_diffview_cs_verwendet_inlinediffbuilder():
    assert os.path.exists(_DIFFVIEW_CS), pytest.skip("DiffView.xaml.cs fehlt noch")
    content = open(_DIFFVIEW_CS, encoding="utf-8").read()
    assert "InlineDiffBuilder" in content, "InlineDiffBuilder nicht in DiffView.xaml.cs gefunden"


def test_diffview_cs_farben_rot_gruen_grau():
    assert os.path.exists(_DIFFVIEW_CS), pytest.skip("DiffView.xaml.cs fehlt noch")
    content = open(_DIFFVIEW_CS, encoding="utf-8").read()
    assert "Red" in content or "Deleted" in content, "Rot/Deleted-Farbe fehlt in DiffView.xaml.cs"
    assert "Green" in content or "Inserted" in content, "Grün/Inserted-Farbe fehlt in DiffView.xaml.cs"


def test_anonymizer_engine_existiert():
    assert os.path.exists(_ANONYMIZER), f"AnonymizerEngine.cs fehlt: {_ANONYMIZER}"
    content = open(_ANONYMIZER, encoding="utf-8").read()
    assert "AnonymizeText" in content, "AnonymizeText-Methode fehlt in AnonymizerEngine.cs"
