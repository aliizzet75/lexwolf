# DSGVO-Dokumentation: Anonymisiertes Feedback an den Server

## Zweck
Der LexWolf Desktop-Client sendet keine Inhalte von Schriftsätzen, Mandantenakten
oder Korrekturen an den Server. Stattdessen werden ausschließlich aggregierte,
nicht personenbezogene Stil-Metriken übertragen, damit das Stil-Profil eines
Anwalts langfristig verbessert werden kann.

## Übermittelte Felder

| Feld | Inhalt | Beispiel |
|------|--------|----------|
| `satzlaengen_verteilung` | Buckets der Wortanzahl pro Satz über korrigierte Fragmente | `"6-10": 3`, `"11-15": 5` |
| `wortklassen_haeufigkeiten` | Häufigkeiten abstrahierter Wortklassen (Nomen, Verben, Adjektive, sonstige) | `"noun_or_name": 12` |
| `korrektur_kategorien` | Anzahl Korrekturen pro Kategorie (Formulierung, Satzstruktur, Paragraph, Länge) | `"formulierung": 4` |

## Was NIE übermittelt wird

- Originaltexte oder korrigierte Texte
- Mandanten-, Richter-, Gegner- oder sonstige Eigennamen
- §-Referenzen, Aktenzeichen, Gerichtsstandorte
- Geldbeträge, IBANs, Adressen, Geburtsdaten
- Rohe Satzfragmente, Vektoren oder Embeddings

## Differential Privacy (optional)

Die Methode `StyleMetricsExtractor.Extract(..., epsilon = 0.0)` kann mit
`epsilon > 0` Laplace-Rauschen auf die Häufigkeiten addieren. Damit lässt sich
einzelne Korrekturereignisse zusätzlich verschleiern. Standardmäßig ist das
Rauschen deaktiviert, um die statistische Genauigkeit zu bewahren.

## Verarbeitung auf dem Server

Der Server speichert die eingehenden Metriken in der `feedback_table` als
anonymisierte Stil-Profile. Es besteht keine Möglichkeit, die Metriken einem
bestimmten Mandanten, Dokument oder Schriftsatz zuzuordnen.
