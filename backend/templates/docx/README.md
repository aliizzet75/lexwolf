# DOCX-Schrifftsatz-Templates

Dieses Verzeichnis enthält Formatvorlagen pro Schriftsatztyp für den LexWolf-Export.

## Vorhandene Templates

| Datei | Schriftsatztyp | Struktur |
|-------|----------------|----------|
| `klage.docx` | Klage | Rubrum → Anträge → Begründung → Unterschrift |
| `klageerwiderung.docx` | Klageerwiderung | Rubrum (Klagebeklagte/r) → Erwiderung → Begründung → Anträge → Unterschrift |
| `widerspruch.docx` | Widerspruch | Adressen → Widerspruch → Begründung → Anträge → Unterschrift |
| `antrag.docx` | Antrag | Rubrum → Antrag → Begründung → Unterschrift |
| `anwaltsschreiben.docx` | Anwaltsschreiben | Adressen → Betreff → Anrede → Begründung → Unterschrift |
| `mahnschreiben.docx` | Mahnschreiben | Adressen → Mahnung → Zahlungsaufforderung → Unterschrift |

## Template-Felder (Platzhalter)

| Platzhalter | Bedeutung |
|-------------|-----------|
| `{DATUM}` | Datum (Default: heute) |
| `{AKTENZEICHEN}` | Aktenzeichen der Angelegenheit |
| `{MANDANT}` | Name des Mandanten |
| `{MANDANT_ADRESSE}` | Adresse des Mandanten |
| `{BEKLAGTER}` | Name des Beklagten (Klage) |
| `{BEKLAGTER_ADRESSE}` | Adresse des Beklagten (Klage) |
| `{GEGNER}` | Gegner im Verfahren |
| `{GEGNER_ADRESSE}` | Adresse des Gegners |
| `{SCHULDNER}` | Name des Schuldners (Mahnung) |
| `{SCHULDNER_ADRESSE}` | Adresse des Schuldners |
| `{EMPFAENGER}` | Empfänger eines Anwaltsschreibens |
| `{EMPFAENGER_ADRESSE}` | Adresse des Empfängers |
| `{GERICHT}` | Zuständiges Gericht |
| `{ANTRAEGE}` | Antrags-/Forderungstext |
| `{BEGRUENDUNG}` | Begründungstext |
| `{BETREFF}` | Betreffzeile |
| `{KANZLEI}` | Kanzleiname |
| `{ANWALT}` | Name des Anwalts |

## Auswahl im Export

Der Export-Route wird der Schriftsatztyp über `metadaten.schriftsatz_typ` mitgeteilt.
Gültige Werte: `klage`, `klageerwiderung`, `widerspruch`, `antrag`, `anwaltsschreiben`, `mahnschreiben`.
