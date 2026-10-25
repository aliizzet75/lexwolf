"""
Task #80: Inline-Quellenangaben klickbar.

KORREKTUR (2026-10-25): vorige "fertig/verified"-Markierung war fabriziert — es gab
weder Quellenliste noch Hyperlink noch Click-Handler in desktop/MainWindow.xaml.cs.
Dieser Test prüft die echte Implementierung:

  1) ChatHtmlRenderer rendert eine Paragraphen-Referenz (z.B. "§ 23 KSchG") als
     klickbaren <a>-Link mit data-action='quelle' (kein reiner <span>-Platzhalter).
  2) MainWindow.xaml.cs hat einen echten Click-Handler (OnChatWebMessage -> Fall
     "quelle" -> ZeigeQuellenPopupAsync), der NICHT durch den Entwurfskarten-Lookup
     ausgebremst wird (die Referenz hat keine data-msg/Karten-ID).
  3) GesetzestextPopup zeigt Gesetzestext, Fundstelle und Datum und ist per X (native
     Titelleiste) oder Esc (OnPreviewKeyDown) schließbar.
  4) Der Popup-Pfad GET /legal-db/chunks/{chunk_id} (das vom Task genannte
     "GET /api/chunks/<chunk_id>") liefert gegen die echte Postgres-DB echten
     Gesetzestext -- inklusive Regressionstest für den vorgefundenen Bug
     ("truth value of an array is ambiguous" bei chunk.vector), der den Endpunkt
     bisher für jeden Chunk mit Embedding auf 404 fallen ließ.
  5) Der neue Auflöse-Endpunkt /legal-db/chunks/by-reference löst eine Referenz wie
     "§ 23 KSchG" oder "§242 BGB" auf echte Chunks auf (kein Mock).
"""
import os
import re
import socket

import pytest
import requests

_ROOT = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
if not os.path.isdir(_ROOT):
    _ROOT = "/data/.openclaw/workspace-codex/projects/lexwolf"

DESKTOP = os.path.join(_ROOT, "desktop")
MAIN_CS = os.path.join(DESKTOP, "MainWindow.xaml.cs")
RENDERER_CS = os.path.join(DESKTOP, "Services", "ChatHtmlRenderer.cs")
SERVICE_CS = os.path.join(DESKTOP, "Services", "QuellenangabeService.cs")
POPUP_XAML = os.path.join(DESKTOP, "Dialogs", "GesetzestextPopup.xaml")
POPUP_CS = os.path.join(DESKTOP, "Dialogs", "GesetzestextPopup.xaml.cs")

BACKEND_URL = os.environ.get("LEXWOLF_BACKEND_URL", "http://localhost:8000")


def _read(path):
    assert os.path.exists(path), f"Datei fehlt: {path}"
    with open(path, encoding="utf-8") as f:
        return f.read()


def backend_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", int(BACKEND_URL.rsplit(":", 1)[-1])))
    s.close()
    return r == 0


# ── 1) Klickbarer Link statt reinem Platzhalter ─────────────────────────────

def test_paragraph_referenz_wird_als_link_mit_click_handler_gerendert():
    renderer = _read(RENDERER_CS)
    assert "data-action='quelle'" in renderer, (
        "Paragraphen-Referenzen bekommen kein data-action='quelle' -> kein echter "
        "Click-Handler, nur Hervorhebung wie vor der Korrektur."
    )
    assert "<a href='#' class='highlight-paragraph" in renderer, (
        "Paragraphen-Referenzen werden nicht als <a>-Link gerendert."
    )
    # Reine Gesetzeskürzel ohne Nummer (z.B. "das BGB") bleiben unklickbar, da sich
    # daraus kein eindeutiger Chunk auflösen lässt.
    assert "if (!Regex.IsMatch(m.Value, @\"\\d\"))" in renderer


# ── 2) Echter Click-Handler in MainWindow.xaml.cs ───────────────────────────

def test_mainwindow_hat_echten_click_handler_fuer_quelle():
    main_cs = _read(MAIN_CS)
    assert 'action == "quelle"' in main_cs, "OnChatWebMessage behandelt die Aktion 'quelle' nicht"
    assert "ZeigeQuellenPopupAsync" in main_cs, "Kein Popup-Aufruf für Quellenangaben"
    assert "GesetzestextPopup" in main_cs, "MainWindow öffnet kein GesetzestextPopup"

    # Die alte Karten-Lookup-Zeile darf die neue Aktion nicht vor der Behandlung
    # abfangen (keine data-msg/Karten-ID bei einer reinen Quellenangabe).
    handler = main_cs[main_cs.index("private void OnChatWebMessage"):]
    handler = handler[:handler.index("\n    }\n", handler.index("ZeigeQuellenPopupAsync"))]
    quelle_pos = handler.index('action == "quelle"')
    karten_pos = handler.index("_karten.TryGetValue")
    assert quelle_pos < karten_pos, (
        "Der frühe Karten-Lookup läuft vor der 'quelle'-Behandlung -> Klick auf eine "
        "Quellenangabe würde wirkungslos bleiben (kein Karten-Eintrag vorhanden)."
    )


# ── 3) Popup: Gesetzestext, Fundstelle, Datum, schließbar (Esc/X) ───────────

def test_popup_zeigt_gesetzestext_fundstelle_datum():
    popup_xaml = _read(POPUP_XAML)
    popup_cs = _read(POPUP_CS)
    assert "GesetzestextText" in popup_xaml
    assert "FundstelleText" in popup_xaml
    assert "DatumText" in popup_xaml
    assert "chunk.Fundstelle" in popup_cs
    assert "chunk.DatumAnzeige" in popup_cs


def test_popup_ist_per_esc_und_x_schliessbar():
    popup_cs = _read(POPUP_CS)
    assert "Key.Escape" in popup_cs and "Close()" in popup_cs, "Kein Esc-Handler zum Schließen"
    popup_xaml = _read(POPUP_XAML)
    # Kein eigenes WindowStyle -> native Titelleiste mit X-Button bleibt aktiv.
    assert "WindowStyle" not in popup_xaml, (
        "Eigenes WindowStyle würde die native Titelleiste (X zum Schließen) entfernen"
    )


# ── 4) GET /legal-db/chunks/{chunk_id} gegen die echte Postgres-DB ──────────

@pytest.mark.skipif(not backend_ok(), reason=f"Backend nicht erreichbar auf {BACKEND_URL}")
def test_chunk_api_liefert_echten_gesetzestext_aus_der_db():
    resp = requests.get(f"{BACKEND_URL}/legal-db/chunks/746133", timeout=10)
    assert resp.status_code == 200, (
        f"GET /legal-db/chunks/746133 lieferte {resp.status_code} statt 200 -- "
        "Regression des 'ambiguous array truth value'-Bugs bei chunk.vector?"
    )
    data = resp.json()
    assert data["id"] == 746133
    assert "§" in data["title"]
    assert len(data["text"]) > 20


@pytest.mark.skipif(not backend_ok(), reason=f"Backend nicht erreichbar auf {BACKEND_URL}")
def test_chunk_api_404_bei_unbekannter_id():
    resp = requests.get(f"{BACKEND_URL}/legal-db/chunks/999999999", timeout=10)
    assert resp.status_code == 404


# ── 5) Referenz -> Chunk-Auflösung gegen die echte DB (für den Klick im Chat) ─

@pytest.mark.skipif(not backend_ok(), reason=f"Backend nicht erreichbar auf {BACKEND_URL}")
@pytest.mark.parametrize("referenz,erwarteter_paragraph,gesetz", [
    ("§ 23 KSchG", "23", "kschg"),
    ("§242 BGB", "242", "bgb"),
])
def test_referenz_wird_auf_echten_chunk_aufgeloest(referenz, erwarteter_paragraph, gesetz):
    resp = requests.get(
        f"{BACKEND_URL}/legal-db/chunks/by-reference", params={"ref": referenz}, timeout=10
    )
    assert resp.status_code == 200, f"{referenz} wurde nicht aufgelöst: {resp.text}"
    data = resp.json()
    assert re.search(rf"§\s*{erwarteter_paragraph}\b", data["title"])
    assert gesetz in (data.get("tags") or "").lower()
    assert len(data["text"]) > 10


@pytest.mark.skipif(not backend_ok(), reason=f"Backend nicht erreichbar auf {BACKEND_URL}")
def test_unbekannte_referenz_liefert_404_statt_platzhalter():
    resp = requests.get(
        f"{BACKEND_URL}/legal-db/chunks/by-reference", params={"ref": "§99999999 XYZ"}, timeout=10
    )
    assert resp.status_code == 404
