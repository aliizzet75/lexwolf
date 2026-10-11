# Feature-Feedback API — Screenshot-Anhang

## Übersicht

Der `/feature-feedback` Endpunkt nimmt Feature-Wünsche und Bug-Meldungen von
Anwälten entgegen. Bug-Meldungen können optional einen Screenshot-Anhang
enthalten, der im Board sichtbar und herunterladbar ist.

## Endpunkte

### `POST /feature-feedback/chat`

Chat-basierte Klärung eines Feature-Wunsches oder Bugs. Akzeptiert JSON:

```json
{
  "messages": [
    {"role": "user", "content": "Das Mandanten-Dropdown ist leer"}
  ]
}
```

Antwort:

```json
{
  "status": "ready",
  "reply": "Danke für die Meldung. Soll ich das als Bug eintragen?",
  "summary": {
    "titel": "Mandanten-Dropdown leer",
    "beschreibung": "Das Mandanten-Dropdown zeigt keine Einträge."
  }
}
```

`status` ist entweder `clarifying` (Rückfragen) oder `ready` (Zusammenfassung
fertig).

### `POST /feature-feedback/confirm`

Bestätigt ein zuvor erstelltes Summary und legt im Board einen Milestone + Task
an. Bug-Meldungen können optional einen Screenshot enthalten.

**Request-Format:** `multipart/form-data`

| Feld       | Typ    | Erforderlich | Beschreibung                                              |
|------------|--------|--------------|-----------------------------------------------------------|
| `summary`  | Text   | Ja           | JSON-String mit `titel` und `beschreibung`.               |
| `typ`      | Text   | Ja           | `"feature"` oder `"bug"`.                                 |
| `bug`      | Text   | Nur bei Bug  | JSON-String mit `repro_steps`, `client_version`, optional `affected_ui_location`. |
| `screenshot` | Datei | Nein        | Bild im Format PNG, JPG, GIF oder WebP, maximal 10 MB.    |

**Beispiel (curl):**

```bash
curl -s -X POST http://localhost:8000/feature-feedback/confirm \
  -F 'summary={"titel":"Dropdown leer","beschreibung":"Mandanten-Dropdown zeigt keine Eintraege"}' \
  -F 'typ=bug' \
  -F 'bug={"repro_steps":"1. Dialog oeffnen 2. Dropdown klicken","client_version":"1.2.3","affected_ui_location":"Mandanten-Liste"}' \
  -F 'screenshot=@bug.png;type=image/png'
```

**Antwort:**

```json
{
  "ok": true,
  "milestone_id": 64,
  "task_id": 250,
  "attachment_id": "90e8793fc03e4a48b12e432d0cb64ebd.png",
  "attachment_url": "/attachments/90e8793fc03e4a48b12e432d0cb64ebd.png"
}
```

Für Meldungen ohne Screenshot sind `attachment_id` und `attachment_url`
`null`.

## Screenshot-Abruf

Der Screenshot wird unter `/attachments/<attachment_id>` vom LexWolf-Backend
ausgeliefert. Im Board wird die vollqualifizierte URL
`http://localhost:8000/attachments/<attachment_id>` gespeichert, damit
Entwickler sie direkt öffnen oder herunterladen können.

## Validierung

- **Dateityp:** Nur `image/png`, `image/jpeg`, `image/gif` oder
  `image/webp` werden akzeptiert. Ungültige Typen werden mit HTTP 400
  abgelehnt.
- **Dateigröße:** Maximal 10 MB (über `LEXWOLF_MAX_ATTACHMENT_SIZE`
  konfigurierbar, Angabe in Bytes). Größere Dateien liefern HTTP 413.
- **Speicherort:** Die Dateien werden ausschließlich im Verzeichnis
  `LEXWOLF_ATTACHMENTS_DIR` abgelegt. Standard ist
  `<repo-root>/attachments`. Das Verzeichnis ist identisch mit dem
  `StaticFiles`-Mount in `backend/main.py`, damit gespeicherte Dateien auch
  abgerufen werden können.
- **Keine Ausführung:** Anhänge werden als statische Bilder ausgeliefert und
  niemals interpretiert oder ausgeführt.

## Konfiguration

| Umgebungsvariable             | Bedeutung                                 | Standard                    |
|-------------------------------|-------------------------------------------|-----------------------------|
| `LEXWOLF_ATTACHMENTS_DIR`     | Verzeichnis für Screenshot-Anhänge      | `<repo-root>/attachments`   |
| `LEXWOLF_MAX_ATTACHMENT_SIZE` | Maximale Dateigröße in Bytes              | `10485760` (10 MB)          |
| `LEXWOLF_BACKEND_BASE_URL`    | Öffentliche Base-URL für Board-Links      | `http://localhost:8000`      |
