# Technologie-Entscheidung: Electron vs. C# WPF für LexWolf Desktop

**Datum:** 2026-05-29  
**Autor:** LexWolf-Team  
**Status:** Entscheidung getroffen

---

## 1. Hintergrund

LexWolf benötigt einen Desktop-Client für Rechtsanwälte. Kernfunktionen: Dokumentenverarbeitung, Anonymisierung sensibler Daten (lokal), Integration mit beA (besonderes elektronisches Anwaltspostfach) und Outlook/Exchange. Bewertet werden zwei Technologie-Optionen: Electron (JavaScript/Web-Technologie) und C# WPF (nativer Windows-Client).

---

## 2. Bewertungskriterien

### 2.1 Entwicklungsaufwand

| Kriterium | Electron | WPF (C#) |
|---|---|---|
| Initiales Setup | 2–3 Tage | 1–2 Tage |
| UI-Entwicklung | Einfach (HTML/CSS) | Etwas steiler (XAML) |
| Gesamtaufwand MVP | ca. 8–12 Wochen | ca. 6–10 Wochen |
| Teamkenntnisse | JavaScript nötig | Ali kennt C# bereits |

**WPF-Vorteil:** Da Ali bereits C#-Kenntnisse mitbringt, reduziert sich der Einarbeitungsaufwand erheblich. Das Team kann sofort produktiv entwickeln, ohne eine neue Sprache zu erlernen.

### 2.2 Outlook / Office-Integration (MAPI)

| Kriterium | Electron | WPF (C#) |
|---|---|---|
| Outlook MAPI | Über COM-Brücken, komplex | Nativ via `Microsoft.Office.Interop.Outlook` |
| Exchange-Integration | REST (Graph API) | REST + MAPI nativ |
| Aufwand Integration | ca. 3–5 Wochen | ca. 1–2 Wochen |

**Bewertung:** C# WPF bietet native Outlook-Integration über MAPI ohne zusätzliche Abstraktionsschichten. Electron benötigt komplexe COM-Brücken (z. B. via Node.js `win32ole`), was fehleranfällig ist und wegen der vollen MAPI-Unterstützung ca. 3–5 Wochen Mehraufwand bedeutet.

### 2.3 Anonymisierung (lokal)

| Kriterium | Electron | WPF (C#) |
|---|---|---|
| Lokale ML-Modelle | Möglich (ONNX.js, TF.js) | Nativ (ML.NET, ONNX Runtime C#) |
| Performance | Eingeschränkt (JS-Runtime) | Hohe Performance (.NET Runtime) |
| Datenschutz | Lokal möglich | Lokal, vollständig kontrolliert |
| Aufwand | ca. 4–6 Wochen | ca. 3–5 Wochen |

**Bewertung:** Beide Optionen ermöglichen lokale Anonymisierung ohne Datenübertragung an externe Server. C# WPF hat aufgrund der .NET-Runtime und ML.NET einen Leistungsvorteil bei der lokalen Verarbeitung sensibler Mandantendaten.

### 2.4 beA-Kompatibilität (besonderes elektronisches Anwaltspostfach)

| Kriterium | Electron | WPF (C#) |
|---|---|---|
| beA-API (SOAP/REST) | Möglich (node-soap) | Nativ (`System.ServiceModel`) |
| Client-Zertifikate | Via Node.js crypto | Nativ Windows Certificate Store |
| Kartenleser-Integration | Schwierig | Einfach (Windows PCSC API) |
| Aufwand | ca. 3–4 Wochen | ca. 1–2 Wochen |

**Bewertung:** beA nutzt SOAP-Web-Services und erfordert Kartenleser-Unterstützung (Signaturkarte) sowie Windows-Zertifikatsspeicher. C# WPF hat hier klare technische Vorteile, da die Windows-APIs direkt und nativ nutzbar sind.

---

## 3. Gesamtvergleich Aufwand-Schätzung

| Phase | Electron | WPF (C#) |
|---|---|---|
| Setup & Architektur | 3 Tage | 2 Tage |
| UI-Grundgerüst | 2 Wochen | 2 Wochen |
| Outlook-Integration | 4 Wochen | 1,5 Wochen |
| Anonymisierung lokal | 5 Wochen | 4 Wochen |
| beA-Integration | 3,5 Wochen | 1,5 Wochen |
| Tests & Stabilisierung | 2 Wochen | 2 Wochen |
| **Gesamt MVP** | **~16–17 Wochen** | **~11–12 Wochen** |

---

## 4. Empfehlung: C# WPF

**Empfehlung:** LexWolf Desktop wird mit **C# WPF** entwickelt.

### Begründung

Die Entscheidung fällt auf C# WPF aufgrund folgender Hauptargumente:

1. **Entwicklungsgeschwindigkeit:** Da Ali bereits C# beherrscht, entfällt der Lernaufwand für eine neue Technologie. Der Zeitvorteil gegenüber Electron beträgt schätzungsweise 4–6 Wochen.

2. **Outlook MAPI-Integration:** Rechtsanwälte nutzen intensiv Outlook für Mandantenkommunikation. Die native MAPI-Unterstützung in C# ist technisch ausgereift und spart ca. 2–3 Wochen Entwicklungsaufwand wegen der einfacheren Integration.

3. **beA-Kompatibilität:** beA ist eine Windows-zentrierte Lösung mit Smartcard-Anforderungen. C# WPF greift nativ auf Windows PCSC API und den Zertifikatsspeicher zu. Electron würde hier native Brücken erfordern, die fragil und schwer wartbar sind.

4. **Lokale Anonymisierung:** ML.NET bietet eine robuste, gut dokumentierte Lösung für lokale NLP-Verarbeitung in C#. Die Performance ist besser als JavaScript-basierte Alternativen, was bei großen Dokumentenmengen relevant ist.

5. **Zielplattform:** LexWolf Desktop richtet sich primär an Windows-Nutzer (Kanzleien). Ein nativer Windows-Client ist technisch überlegen und bietet bessere UX-Integration (Windows-Benachrichtigungen, Tray-Icon, System-Themes).

### Einschränkungen

- **Kein macOS/Linux-Support:** WPF ist Windows-only. Falls später plattformübergreifende Unterstützung gewünscht wird, wäre eine Migration auf .NET MAUI oder Electron nötig.
- **Längere Compile-Zeiten** als Electron bei großen Codebases.

### Fazit

C# WPF ist die strategisch richtige Entscheidung für LexWolf Desktop, weil die Kernintegrationspunkte (Outlook MAPI, beA, Windows-Zertifikate) nativ und effizient unterstützt werden, die vorhandenen C#-Kenntnisse direkt nutzbar sind, und der Gesamtaufwand für das MVP ca. 5 Wochen kürzer ist als bei Electron.

---

*Dokument erstellt am 2026-05-29 | Version 1.0*
