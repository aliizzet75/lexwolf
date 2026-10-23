# Freigabe-Protokoll: Qualitätssicherung Gutachten-Modus

Bezug: T#278, Milestone "Gutachten / Anspruchsprüfung" (#79), Paket G-8 aus
`docs/KONZEPT_GUTACHTEN.md`. Baut auf der bestehenden LLM-as-Judge/
Post-Verification-Pipeline auf (Entry 48, `docs/quality_protocol.md`,
`backend/llm_judge.py`, `backend/services/quality_verification.py`).

## Warum ein eigenes, strengeres Protokoll?

Der allgemeine M3-Prozess (Entry 48) deckt Chat-Antworten mit Paragraphen-
Zitaten ab. Ein Gutachten ist etwas anderes: Es ist die **Grundlage für die
Entscheidung des Mandanten**, ob er klagt, sich vergleicht oder eine Klage
der Gegenseite abwehrt. Ein übersehener Einwand (z.B. Verjährung,
Mitverschulden, Zurückbehaltungsrecht) führt nicht nur zu einer ungenauen
Antwort, sondern kann den Mandanten zu einer wirtschaftlich falschen
Entscheidung verleiten. Deshalb gilt für die **Vollständigkeit der
Einwände/Einreden** eine strengere Metrik als die allgemeine
Vollständigkeits-Schwelle aus `docs/quality_protocol.md`.

## Metriken und Mindest-Werte

| Metrik | Mindest-Wert | Vergleich M3 (Entry 48) | Begründung |
|---|---|---|---|
| Einwände-Vollständigkeit | ≥ 95 % | allgemeine Vollständigkeit > 80 % | Ein fehlender Einwand beeinflusst die Klage-/Vergleichsentscheidung direkt |
| Normprüfung gegen DB | 0 nicht gefundene Normen | Quellengenauigkeit > 95 % (Toleranz) | Im Gutachten ist jede erfundene oder falsche Norm inakzeptabel, keine Toleranz |
| Schema-Validität | 100 % | — (neu für Gutachten) | Ergebnis-Editor (T#276) braucht garantiert vorhandene Felder |
| Zweiter LLM-Judge-Score | ≥ 4/5 | implizit über Halluzinationsrate | Unabhängiges zweites Modell prüft Schlüssigkeit und Vollständigkeit |
| Testfälle insgesamt bestanden | 100 % (alle 15-20 Fälle) | Stichprobe 50-100 Fragen | Kleineres, aber vollständig abzudeckendes Testset je Rechtsgebiet |

Implementiert in `backend/services/gutachten_quality.py`
(`EINWAENDE_VOLLSTAENDIGKEIT_SCHWELLE = 0.95`, `JUDGE_MINDEST_SCORE = 4`).

## Testverfahren

### Test-Set

`tests/quality/gutachten_test_dataset.json` — **18 realistische Testfälle**
(Zielbereich 15-20 laut Auftrag) über Arbeitsrecht, Mietrecht, Kaufrecht,
Werkvertragsrecht, Deliktsrecht, Familienrecht, Darlehensrecht,
Handelsrecht, Erbrecht, allgemeines Zivilrecht, Nachbarrecht,
Verbraucherrecht, Gesellschaftsrecht, Reiserecht und Arzthaftungsrecht. Jeder
Fall enthält Sachverhalt, Fragestellung, erwartete Anspruchsgrundlage(n),
erwartete Einwendungen/Einreden und den erwarteten Ampel-Bereich.

Hinweis zu G-8 aus `docs/KONZEPT_GUTACHTEN.md`: Dort war vorgesehen, dass
der Anwalt 10-20 abgeschlossene, anonymisierte Musterfälle mit seiner
eigenen Einschätzung liefert (offene Frage 2 in diesem Dokument). Solange
diese Fälle nicht vorliegen, deckt das hier erstellte, selbst konstruierte
Testset den vom Auftrag verlangten Umfang (15-20 Fälle) technisch ab; die
Einschätzung des Anwalts zu echten Fällen bleibt für die Kalibrierung
zusätzlich wertvoll und sollte bei Gelegenheit nachgezogen werden.

### Automatisierte Tests

1. `tests/quality/run_gutachten_quality_test.py` — führt für jeden Testfall
   die QA-Pipeline (`pruefe_gutachten_qualitaet()`) aus: Schema-Validierung,
   Einwände-Vollständigkeit, Normprüfung gegen die echte Postgres-DB
   (`legal_chunks`), zweiter LLM-Judge-Durchlauf. Enthält außerdem 3
   Negativtests mit bewusst fehlerhaften Fixtures (fehlender Einwand,
   erfundene Norm, ungültiges Schema), die beweisen, dass das Qualitätstor
   solche Mängel tatsächlich erkennt statt immer "bestanden" zu melden.
2. `tests/quality/gutachten_quality_gate.py` — CI/CD-Gate, liest die
   Ergebnisdatei und blockiert bei Schwellenwert-Unterschreitung **oder**
   fehlender Anwalts-Freigabe (siehe unten). Exit-Code 1 bei Blockade.
3. `tests/acceptance/test_acceptance_278.py` — pytest-Akzeptanztests gegen
   die echte Datenbank (kein Mock).

Sobald die Prüfungs-Engine (T#274/T#275, Stand T#278 noch "geplant") echte
Gutachten-JSON-Antworten liefert, kann `run_gutachten_quality_test.py --live`
dieselbe Pipeline gegen echte KI-Ausgaben statt gegen Fixtures laufen lassen
— die QA-Funktionen selbst sind bereits produktionsfähig.

## Freigabe-Prozess (analog M3/Entry 48)

### Wer gibt frei?

- **Automatisiertes Gate**: `gutachten_quality_gate.py` nach Bestehen aller
  Schwellenwerte.
- **Lead Developer (Tech-Review)**: Technische Freigabe nach Code-Review.
- **Anwalt**: **Schriftliche Freigabe nach manuellem Test** — das ist die
  entscheidende, nicht automatisierbare Stufe, weil die fachliche
  Verantwortung für ein Gutachten nach § 43 BRAO beim Anwalt bleibt
  (siehe `docs/KONZEPT_GUTACHTEN.md`, Abschnitt 2).

### Wie wird freigegeben?

1. Alle automatisierten Gates müssen grün sein (Einwände-Vollständigkeit
   ≥ 95 %, 0 nicht in der DB gefundene Normen, Schema 100 % valide,
   LLM-Judge ≥ 4/5, alle Testfälle bestanden).
2. Der Anwalt prüft eine manuelle Stichprobe der 15-20 Testfälle
   (insbesondere die Einwände-Liste je Fall) und bestätigt schriftlich die
   Freigabe, indem `docs/gutachten_freigabe_status.json` mit
   `"freigegeben": true`, seinem Namen (`freigegeben_durch`) und Datum
   aktualisiert wird.
3. Erst danach darf T#274 (Intent-Routing) den Gutachten-Modus produktiv
   anbieten. `ist_produktionsfreigabe_erteilt()` in
   `backend/services/gutachten_quality.py` ist die technische Umsetzung
   dieser Sperre und sollte vor jedem produktiven Aufruf geprüft werden.

### Status zum Zeitpunkt von T#278

`docs/gutachten_freigabe_status.json`: **`freigegeben: false`** — es liegt
noch keine Freigabe vor. Das ist beabsichtigt: T#278 bereitet die
Qualitätssicherung und den Freigabeprozess vor; die eigentliche Prüfungs-
Engine (T#274/T#275/T#276) ist noch nicht gebaut, und die schriftliche
Freigabe kann naturgemäß nur durch den Anwalt selbst erteilt werden, nicht
durch die Implementierung dieses Tasks.

## Gültig ab

2026-10-23 | Version 1.0
