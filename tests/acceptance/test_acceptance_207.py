import os
import subprocess

# Wir laufen im Container; der Host-Pfad aus der Task-Vorgabe ist hier nicht
# erreichbar. Der Test arbeitet deshalb mit dem Container-Workspace.
_ANON_DIR = os.environ.get(
    "ANON_DIR",
    "/data/.openclaw/workspace-codex/projects/lexwolf/tools/Anonymisierer",
)
_CORE_CS = os.path.join(_ANON_DIR, "Anonymisierer.Core", "Anonymizer.cs")
_CORE_PROJ = os.path.join(_ANON_DIR, "Anonymisierer.Core", "Anonymisierer.Core.csproj")
_TEST_PROJ = os.path.join(_ANON_DIR, "Anonymisierer.Tests", "Anonymisierer.Tests.csproj")
_WORDLIST = os.path.join(_ANON_DIR, "Anonymisierer.Core", "Data", "de_words_20k.txt")


def test_struktureller_filter_statt_stopwortliste():
    """Task #207: Personen-False-Positive-Filter muss auf strukturellen Kriterien
    basieren, nicht auf einer reinen Auflistung der bekannten Fehlerwoerter."""
    src = open(_CORE_CS, encoding="utf-8").read()

    # Es muss eine deutsche Wortliste als Embedded Resource geladen werden.
    assert "_germanWordList" in src, "Anonymizer.cs laedt keine deutsche Wortliste."
    assert "de_words_20k.txt" in src, "Anonymizer.cs referenziert nicht die Wortliste."
    assert "IsGenericGermanSubstantivComposite" in src, (
        "Strukturelles Suffix-/Praefix-Kriterium fehlt."
    )
    assert "_top100GermanWords" in src, (
        "Top-100-Haeufigkeits-Kriterium fuer hochfrequente deutsche Woerter fehlt."
    )

    # Keine harte Aufzaehlung der Task-207-Beispielwoerter als Negativliste.
    # Ausnahme: Einzelne Beispiele duerfen im erklaerenden Kommentar vorkommen.
    src_code_only = "\n".join(
        line.split("//", 1)[0] for line in src.splitlines()
    )
    for beispiel in ["Wirtschaftsjahrs", "Waermecontracting", "Elektroheizgeraete",
                     "Betriebskostenpauschale", "Bruttoarbeitslohn", "Hauptvordruck"]:
        assert beispiel not in src_code_only, (
            f"Anonymizer.cs enthaelt noch eine harte Aufzaehlung des Beispielworts "
            f"'{beispiel}' — Task #207 erfordert ein strukturelles Kriterium."
        )


def test_wortliste_als_embedded_resource_vorhanden():
    assert os.path.exists(_WORDLIST), f"Wortliste fehlt: {_WORDLIST}"
    lines = open(_WORDLIST, encoding="utf-8").read().splitlines()
    assert len(lines) >= 1000, f"Wortliste zu kurz: {len(lines)} Zeilen"
    assert "will" in lines, "Wortliste enthaelt nicht 'will'"
    assert "wirtschaftsjahr" not in [w.lower() for w in lines[:100]], (
        "Wirtschaftsjahr darf nicht in Top 100 sein"
    )


def test_dotnet_build_core_erfolgreich():
    result = subprocess.run(
        ["dotnet", "build", _CORE_PROJ],
        cwd=_ANON_DIR, capture_output=True, text=True, timeout=180,
    )
    assert result.returncode == 0, (
        f"dotnet build Anonymisierer.Core schlaegt fehl:\n"
        f"{result.stdout[-3000:]}\n{result.stderr[-2000:]}"
    )


def test_dotnet_test_structural_false_positive_gruen():
    result = subprocess.run(
        ["dotnet", "test", _TEST_PROJ, "--filter", "FullyQualifiedName~StructuralFalsePositivePersonTests"],
        cwd=_ANON_DIR, capture_output=True, text=True, timeout=300,
    )
    assert result.returncode == 0, (
        f"dotnet test StructuralFalsePositivePersonTests schlaegt fehl:\n"
        f"{result.stdout[-3000:]}\n{result.stderr[-2000:]}"
    )


def test_dotnet_test_false_positive_person_204_bleibt_gruen():
    result = subprocess.run(
        ["dotnet", "test", _TEST_PROJ, "--filter", "FullyQualifiedName~FalsePositivePersonTests"],
        cwd=_ANON_DIR, capture_output=True, text=True, timeout=300,
    )
    assert result.returncode == 0, (
        f"dotnet test FalsePositivePersonTests (Task #204) schlaegt fehl:\n"
        f"{result.stdout[-3000:]}\n{result.stderr[-2000:]}"
    )
