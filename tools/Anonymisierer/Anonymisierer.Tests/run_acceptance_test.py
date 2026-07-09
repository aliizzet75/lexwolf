#!/usr/bin/env python3
"""End-to-End Akzeptanztest (Task #200) fuer den Anonymisierer.

Ruft die echte, produktive Anonymisierungs-Engine (Anonymisierer.Core) ueber
den plattformunabhaengigen Anonymisierer.Cli-Runner fuer alle Testdokumente in
./TestData auf, prueft die Ergebnisse gegen die Akzeptanzkriterien und schreibt
den Report nach anon_report.md:

  1. Laedt die "<Dokument>_anon.txt" Ausgaben der Testdokumente.
  2. Prueft, dass echte Namen/Orte (Mandanten-PII) nicht mehr vorkommen.
  3. Prueft, dass die juristischen Pflichtbegriffe erhalten geblieben sind.
  4. Schreibt anon_report.md mit Entitaeten+Aliasen, Anzahl Ersetzungen und
     Kontexterhaltungs-Score.
"""
import json
import subprocess
import sys
import unicodedata
from pathlib import Path

BASE = Path(__file__).resolve().parent
TOOLS_DIR = BASE.parent
CLI_PROJECT = TOOLS_DIR / "Anonymisierer.Cli" / "Anonymisierer.Cli.csproj"
CLI_DLL = TOOLS_DIR / "Anonymisierer.Cli" / "bin" / "Debug" / "net10.0" / "Anonymisierer.Cli.dll"
TESTDATA_DIR = BASE / "TestData"
REPORT_PATH = BASE / "anon_report.md"
ENTITIES_JSON = BASE / "entities.json"

# Echte Mandanten-PII aus den Testdokumenten, die nach der Anonymisierung in
# keinem Ausgabetext mehr vorkommen darf.
FORBIDDEN_NAMES = [
    "Erkol", "Ruck", "Ali", "Dilara", "Maysa", "Izzet",
    "Stuttgart", "Unterlaender", "Wallenstein",
]

# Juristische Pflichtbegriffe, die trotz Anonymisierung erhalten bleiben muessen
# (Vergleich diakritik-unabhaengig, z.B. "Steuererklärung" == "Steuererklarung").
MANDATORY_TERMS = [
    "Kindergeld", "Leasingrate", "Mietvertrag", "Immobilie",
    "Kfz", "Nebenkosten", "Steuererklarung", "Gesamtbelastung",
]


def strip_diacritics(s: str) -> str:
    normalized = unicodedata.normalize("NFD", s)
    return "".join(c for c in normalized if unicodedata.category(c) != "Mn")


def run_anonymizer() -> None:
    """Baut und startet den Anonymisierer.Cli-Runner gegen TestData/, schreibt
    die "<Dokument>_anon.txt"-Dateien + entities.json direkt in BASE."""
    subprocess.run(
        ["dotnet", "build", str(CLI_PROJECT), "-c", "Debug", "-v", "quiet"],
        check=True,
        cwd=TOOLS_DIR,
        timeout=180,
    )
    subprocess.run(
        ["dotnet", str(CLI_DLL), str(TESTDATA_DIR), str(BASE)],
        check=True,
        cwd=BASE,
        timeout=180,
    )


def main() -> int:
    if not TESTDATA_DIR.is_dir():
        print(f"Testdokumente-Ordner fehlt: {TESTDATA_DIR}", file=sys.stderr)
        return 1

    run_anonymizer()

    anon_files = sorted(BASE.glob("*_anon.txt"))
    if not anon_files:
        print("Keine *_anon.txt Dateien erzeugt.", file=sys.stderr)
        return 1

    doc_reports = json.loads(ENTITIES_JSON.read_text(encoding="utf-8")) if ENTITIES_JSON.is_file() else []

    texts_by_file = {f.name: f.read_text(encoding="utf-8") for f in anon_files}
    combined_text = "\n".join(texts_by_file.values())

    # (2) Pruefung: echte Namen/Orte duerfen nicht mehr vorkommen
    pii_hits = [
        (name, fname)
        for name in FORBIDDEN_NAMES
        for fname, text in texts_by_file.items()
        if name in text
    ]
    pii_count = len(pii_hits)

    # (3) Pruefung: juristische Pflichtbegriffe muessen erhalten bleiben
    normalized_combined = strip_diacritics(combined_text).lower()
    found_terms = [t for t in MANDATORY_TERMS if strip_diacritics(t).lower() in normalized_combined]
    missing_terms = [t for t in MANDATORY_TERMS if t not in found_terms]
    context_score = 100.0 * len(found_terms) / len(MANDATORY_TERMS)

    # (4) Report-Daten: alle Entitaeten + Aliase, Anzahl Ersetzungen
    all_entities = [
        (doc["Document"], ent["Text"], ent["Type"], ent["Alias"])
        for doc in doc_reports
        for ent in doc.get("Entities", [])
    ]
    unique_entities = sorted({(e[1], e[2], e[3]) for e in all_entities})
    total_replacements = len(all_entities)

    lines = [
        "# Anonymisierer - Akzeptanztest-Report (Task #200)",
        "",
        f"Verarbeitete Testdokumente: {len(doc_reports)}",
        f"PII-Treffer: {pii_count}",
        f"Kontexterhaltung: {context_score:.1f}%",
        f"Anzahl Ersetzungen (gesamt): {total_replacements}",
        f"Anzahl eindeutiger Entitaeten: {len(unique_entities)}",
        "",
    ]

    if pii_hits:
        lines.append("## PII-Treffer (Details)")
        for name, fname in pii_hits:
            lines.append(f"- '{name}' gefunden in {fname}")
        lines.append("")

    lines.append("## Rechtsbegriffe (Kontexterhaltung)")
    lines.append(f"Gefunden ({len(found_terms)}/{len(MANDATORY_TERMS)}): {', '.join(found_terms) or '-'}")
    if missing_terms:
        lines.append(f"Fehlend: {', '.join(missing_terms)}")
    lines.append("")

    lines.append("## Entitaeten und Aliase")
    lines.append("")
    lines.append("| Original | Typ | Alias |")
    lines.append("|---|---|---|")
    for text, etype, alias in unique_entities:
        safe_text = text.replace("|", "\\|").replace("\n", " ")
        lines.append(f"| {safe_text} | {etype} | {alias} |")
    lines.append("")

    lines.append("## Verarbeitete Testdokumente")
    for doc in doc_reports:
        lines.append(f"- {doc['Document']} -> {doc['AnonTextFile']} ({len(doc.get('Entities', []))} Entitaeten)")
    lines.append("")

    REPORT_PATH.write_text("\n".join(lines), encoding="utf-8")
    print(f"Report geschrieben: {REPORT_PATH}")
    print(f"PII-Treffer: {pii_count}, Kontexterhaltung: {context_score:.1f}%")
    return 0


if __name__ == "__main__":
    sys.exit(main())
