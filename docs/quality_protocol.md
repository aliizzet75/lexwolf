# Freigabe-Protokoll: Qualitätsstandards für Produktions-Release

## Zweck

Dieses Dokument definiert verbindliche Qualitätsziele und den Freigabe-Prozess vor jedem Produktions-Release von LexWolf.

## Metriken und Mindest-Werte

| Metrik | Mindest-Wert | Beschreibung |
|--------|-------------|--------------|
|  | < 5% (0.05) | Anteil falscher oder erfundener Rechtsaussagen |
|  | > 95% (0.95) | Korrekte Zuordnung von Quellen zu Aussagen |
|  | > 80% (0.80) | Abdeckung relevanter Rechtsinformationen |

### Halluzinations-Rate (< 5%)
- Ziel: halluzinations_rate < 0.05
- Messung: Stichprobe von 100 Antworten, manuelle Prüfung durch Jurist
- Grenzwert: Bei >= 5% wird der Release blockiert

### Quellengenauigkeit (> 95%)
- Ziel: quellen_genauigkeit > 0.95
- Messung: Automatisierter Abgleich von Quellangaben mit Datenbank
- Grenzwert: Bei <= 95% wird der Release blockiert

### Vollständigkeits-Score (> 80%)
- Ziel: vollstaendigkeits_score > 0.80
- Messung: Testset mit bekannten Rechtsfragen, Recall-Berechnung
- Grenzwert: Bei <= 80% wird der Release blockiert

## Testverfahren

### Automatisierte Tests (CI/CD)
1.  prüft alle Schwellenwerte automatisch
2. Läuft bei jedem PR auf den -Branch
3. Blockiert Merge bei Unterschreitung der Mindest-Werte

### Manuelle Tests (vor Major Release)
1. QA-Lead führt manuelle Stichprobe durch (min. 50 Testfälle)
2. Lead Developer prüft kritische Rechtsbereiche
3. Ergebnis wird im Release-Ticket dokumentiert

## Freigabe-Prozess

### Wer gibt frei?
- **QA-Lead**: Freigabe nach Bestehen aller automatisierten Tests
- **Lead Developer (Tech-Review)**: Technische Freigabe nach Code-Review
- **Product Owner**: Finale Freigabe nach Abnahme

### Wie wird freigegeben?
1. Alle CI/CD-Gates müssen grün sein (halluzinations_rate < 0.05, quellen_genauigkeit > 0.95, vollstaendigkeits_score > 0.80)
2. QA-Lead bestätigt manuelle Tests im PR-Kommentar: 
3. Lead Developer mergt nach Review: 
4. Product Owner erteilt finale Freigabe: 
5. Deploy auf Production durch DevOps

### Verantwortliche

| Rolle | Verantwortung |
|-------|--------------|
| QA-Lead | Testdurchführung, QA-Freigabe |
| Lead Developer | Code-Review, Tech-Freigabe |
| Product Owner | Fachliche Abnahme, finale Freigabe |
| DevOps | Production-Deployment |

## CI/CD-Gates

Die Schwellenwerte werden durch  im CI/CD-Workflow erzwungen:



Ein Release wird automatisch blockiert, wenn einer der Schwellenwerte nicht erfüllt ist.

## Gültig ab

2026-05-29 | Version 1.0
