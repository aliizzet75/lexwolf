"""
/ask — Zentraler Endpunkt für den LexWolf-Prototyp.
Nimmt freien Text entgegen, erkennt den Intent, sucht in der Datenbank
und gibt Denkprozess + Ergebnis zurück.
"""
from fastapi import APIRouter
from pydantic import BaseModel
from typing import List, Optional
import time
import re

from services.database_service import DatabaseService
from services.search_service import HybridSearchService

router = APIRouter(prefix="/ask", tags=["ask"])

db = DatabaseService()
_search_service = None

def _get_search():
    global _search_service
    if _search_service is None:
        _search_service = HybridSearchService()
    return _search_service


class AskRequest(BaseModel):
    text: str


class ReasoningStep(BaseModel):
    emoji: str
    text: str


class Source(BaseModel):
    title: str
    court: Optional[str] = None
    date: Optional[str] = None
    case_number: Optional[str] = None
    excerpt: str


class AskResponse(BaseModel):
    intent: str
    reasoning: List[ReasoningStep]
    output: str
    sources: List[Source]
    duration_ms: int


# ── Intent-Erkennung via semantische Ähnlichkeit ─────────────────────────────
# Kein Keyword-Matching — das Embedding-Modell versteht den Sinn der Anfrage.
# Jeder Intent hat Beispielsätze. Zur Laufzeit wird die Cosine-Ähnlichkeit
# zwischen Nutzeranfrage und Beispielen berechnet. Schnell (~50ms), robust.

_INTENT_EXAMPLES = {
    "erstelle": [
        "Erstelle mir ein Kündigungsschreiben",
        "Schreib eine Abmahnung",
        "Formuliere ein Schreiben",
        "Kannst du mir einen Brief aufsetzen",
        "Ich möchte eine Vollmacht haben",
        "Ich benötige ein Kündigungsschreiben",
        "Bereite mir eine Klageschrift vor",
        "Ich brauche eine Befristungsvereinbarung",
        "Stell mir einen Vergleich zusammen",
        "Schreibe einen Einspruch",
        "Ich möchte eine Kündigung formuliert haben",
        "Kannst du eine Kündigung formulieren",
        "Ich benötige einen Lizenzvertrag",
        "Schreibe einen Schriftsatz",
        "Bereite einen Vergleich vor",
        "Ich brauche eine Scheidungsfolgenvereinbarung",
        "Ich benötige einen Mietvertrag",
        "Formuliere mir einen Vergleich",
        "Setze einen Arbeitsvertrag auf",
        "Verfasse ein Abmahnungsschreiben",
    ],
    "erklaere": [
        "Was ist Kündigungsschutz?",
        "Was bedeutet eine fristlose Kündigung?",
        "Erkläre mir den Unterschied zwischen ordentlicher und außerordentlicher Kündigung",
        "Was versteht man unter Elternzeit?",
        "Was ist eine Abmahnung?",
        "Was ist der Unterschied zwischen GmbH und UG?",
        "Was versteht man unter dem AGG?",
        "Was ist der Versorgungsausgleich?",
        "Was ist ein Pflichtteil?",
        "Was ist eine AGB-Klausel?",
    ],
    "suche": [
        "Gibt es Urteile zum Kündigungsschutz?",
        "Zeig mir Rechtsprechung zur Mietminderung",
        "Suche Urteile zum Thema Abfindung",
        "Welche Gerichtsurteile gibt es zu Eigenbedarfskündigung?",
        "Finde Urteile zum Arbeitsrecht",
    ],
    "frage": [
        "Wie kündige ich einem Arbeitnehmer fristgerecht?",
        "Darf mein Vermieter einfach die Miete erhöhen?",
        "Habe ich Anspruch auf Elternzeit?",
        "Wann kann ich einen Mietvertrag kündigen?",
        "Muss ich eine Abfindung zahlen?",
        "Welche Rechte habe ich als Arbeitnehmer?",
        "Was sind die Voraussetzungen für eine Eigenbedarfskündigung?",
        "Was sind die Voraussetzungen für eine betriebsbedingte Kündigung?",
        "Wann greift der Kündigungsschutz?",
        "Welche Rechte hat mein Mandant bei fristloser Entlassung?",
        "Wann hat man Anspruch auf Abfindung?",
        "Mein Mandant wurde fristlos entlassen. Was sind seine Rechte?",
        "Mein Mandant hat Überstunden nicht bezahlt bekommen. Was kann er tun?",
        "Mein Mandant möchte das Sorgerecht alleine haben. Was sind die Voraussetzungen?",
        "Mein Mandant wurde bei einem Verkehrsunfall verletzt. Wer haftet?",
        "Mein Mandant möchte gegen seinen Nachbarn vorgehen wegen Lärm.",
        "Wie läuft das Scheidungsverfahren ab?",
        "Was passiert wenn jemand ohne Testament stirbt?",
        "Was sind die Gewährleistungsrechte beim Autokauf?",
        "Wann macht sich jemand strafbar?",
        "Wie gründe ich eine GmbH?",
        "Was sind die Rechte von Arbeitnehmern bei Insolvenz?",
        "Wie berechnet sich der Kindesunterhalt?",
        "Was sind Schönheitsreparaturen und wer muss sie durchführen?",
    ],
}

_intent_embeddings: dict | None = None

def _get_intent_embeddings():
    """Lazy-load: Beispiel-Embeddings einmalig beim ersten Aufruf berechnen."""
    global _intent_embeddings
    if _intent_embeddings is not None:
        return _intent_embeddings
    from services.embedding_service import EmbeddingService
    import numpy as np
    es = EmbeddingService()
    _intent_embeddings = {}
    for intent, examples in _INTENT_EXAMPLES.items():
        vecs = [np.array(es.generate_embedding(ex)) for ex in examples]
        _intent_embeddings[intent] = np.mean(vecs, axis=0)  # Durchschnitt der Beispiele
    return _intent_embeddings

def _cosine_sim(a, b):
    import numpy as np
    na, nb = np.linalg.norm(a), np.linalg.norm(b)
    if na == 0 or nb == 0:
        return 0.0
    return float(np.dot(a, b) / (na * nb))

def _detect_intent(text: str) -> str:
    """Semantische Intent-Klassifizierung via Embedding-Ähnlichkeit."""
    try:
        import numpy as np
        from services.embedding_service import EmbeddingService
        es = EmbeddingService()
        query_vec = np.array(es.generate_embedding(text))
        archetypes = _get_intent_embeddings()
        scores = {intent: _cosine_sim(query_vec, vec) for intent, vec in archetypes.items()}
        best = max(scores, key=scores.get)
        return best
    except Exception:
        return "frage"


# ── Query-Erweiterung: Themenkontext für präzisere Suche ─────────────────────
# Wenn eine Anfrage thematisch eindeutig ist, aber ohne Fachkontext formuliert,
# wird der Kontext ergänzt damit die Suche nicht in Nischenbereichen landet.
# z.B. "wie kündige ich" → ohne "Arbeitnehmer" findet die Suche Pachtrecht statt Arbeitsrecht.

_THEMA_KONTEXT = [
    (r"kündig\w+|kündige",
     r"arbeitnehm|arbeitgeb|arbeit|mitarbeit|angestellt|betrieb|beschäftigt|pacht|mietvertrag",
     "Kündigung Arbeitsverhältnis Arbeitnehmer Arbeitgeber"),

    (r"miete?|vermieter|mietvertrag|wohnung\w*|untervermiet\w+|schönheitsrep\w+",
     r"pacht|landwirt|acker|grundstück",
     "Mietrecht Wohnraum Mieter Vermieter BGB"),

    (r"unterhalt",
     r"rente|sozial|alters",
     "Kindesunterhalt Familienrecht Unterhaltspflicht BGB"),

    (r"erbschaft|erbe|erben|erbrecht|pflichtteil|erbvertrag|testament",
     r"",
     "Erbrecht Erbfall Erblasser Testament BGB"),

    (r"abmahn\w+",
     r"",
     "Abmahnung Arbeitnehmer Pflichtverletzung Kündigung BGB"),

    (r"sorgerecht|sorgefalt|elternteil|kindeswohl",
     r"",
     "Sorgerecht Kind Eltern Familienrecht BGB"),

    (r"gmbh|geschäftsführer|gesellschaftsrecht|gesellschafter",
     r"",
     "GmbH Gesellschaft Haftung Geschäftsführer GmbHG"),

    (r"agb|allgemeine geschäftsbeding\w+|klausel",
     r"",
     "AGB Verbraucher Klausel unwirksam BGB §307"),

    (r"insolvenz|arbeitnehmer.*insolvenz|insolvenz.*arbeitnehmer",
     r"",
     "Insolvenz Arbeitnehmer Lohn Gehalt InsO"),

    (r"versetzung|direktionsrecht",
     r"",
     "Versetzung Direktionsrecht Arbeitnehmer Arbeitsvertrag"),

    (r"scheidung|scheidungsverfahren|ehegattenunterhalt|versorgungsausgleich",
     r"",
     "Scheidung Familienrecht Unterhalt Versorgungsausgleich BGB"),

    (r"zwangsvollstreck\w+|pfändung",
     r"",
     "Zwangsvollstreckung ZPO Gläubiger Schuldner"),

    (r"einstweilig\w+|verfügung",
     r"",
     "einstweilige Verfügung ZPO Unterlassung Gericht"),

    (r"verkehrsunfall|unfallschaden|schadenersatz.*unfall",
     r"",
     "Verkehrsunfall Haftung Schadensersatz BGB"),

    (r"gewährleistung|mangel|nacherfüllung",
     r"",
     "Gewährleistung Mangel Nacherfüllung Kaufvertrag BGB"),
]

def _enrich_query(query: str) -> str:
    """Ergänzt generische Anfragen mit thematischem Kontext für präzisere Suche."""
    lower = query.lower()
    for topic_pattern, exclude_pattern, context in _THEMA_KONTEXT:
        if re.search(topic_pattern, lower):
            # Nur ergänzen wenn spezifischer Kontext NICHT schon vorhanden
            if not exclude_pattern or not re.search(exclude_pattern, lower):
                # Nur wenn der Kontext nicht schon im Query steckt
                first_context_word = context.split()[0].lower()
                if first_context_word not in lower:
                    return f"{query} {context}"
    return query

# ── Schlüsselwort-Extraktion für DB-Query ────────────────────────────────────

_STOP = {"mir", "eine", "einen", "einer", "der", "die", "das", "ein", "und", "oder",
         "ist", "sind", "ich", "für", "zu", "von", "mit", "auf", "an", "im",
         "in", "des", "dem", "den", "wie", "was", "ob", "bitte", "mich",
         "erstelle", "erstell", "schreibe", "schreib", "formuliere", "stelle", "stell",
         "zusammen", "aufsetzen", "entwerfe", "erkläre", "erklaere", "suche", "finde",
         "zeig", "zeige", "mach", "generiere", "erarbeite"}

_DOKUMENT_TYPEN = {
    # (query_für_embedding, [tags_für_direktsuche], fts_stichwort)
    "kündigungsschreiben": ("Kündigung Arbeitsverhältnis Kündigungsfrist",  ["kschg", "bgb"], "Kündigung"),
    "kündigung":           ("Kündigung Arbeitsverhältnis Kündigungsfrist",  ["kschg", "bgb"], "Kündigung"),
    "mietvertrag":         ("Mietvertrag Wohnraum Miete Vermieter",         ["bgb"],          "Mietvertrag"),
    "abmahnung":           ("Pflichtverletzung Arbeitnehmer Kündigung außerordentlich", ["kschg", "bgb"], "Kündigung"),
    "kaufvertrag":         ("Kaufvertrag Kaufpreis Eigentumsübertragung",   ["bgb"],          "Kaufvertrag"),
    "vollmacht":           ("Vollmacht Bevollmächtigung Vertretung",         ["bgb"],          "Vollmacht"),
    "widerspruch":         ("Widerspruch Verwaltungsakt Einspruch",          [],               "Widerspruch"),
    "klage":               ("Klage Zivilprozess Gericht Antrag",            ["zpo"],          "Klage"),
    "mahnung":             ("Mahnung Zahlungsverzug Forderung",             ["bgb"],          "Mahnung"),
}

_TOPIC_TAGS = {
    r"gmbh|geschäftsführer":          ("gmbhg", None),
    r"insolvenz":                       ("inso",  None),
    r"sorgerecht|elterliche sorge":    ("bgb",   "Sorgerecht"),
    r"pflichtteil":                     ("bgb",   "Pflichtteil"),
    r"agb|allgemeine geschäftsbeding": ("bgb",   "§ 307"),
    r"zwangsvollstreck":               ("zpo",   None),
    r"einstweilig.*verfügung":         ("zpo",   None),
    r"handelsrecht|kaufmann":          ("hgb",   None),
    r"trennungsunterhalt|ehegattenunterhalt": ("bgb", "Unterhalt"),
    r"wohnungsmiet|mietwohnung|kündigungsfrist.*miete?": ("bgb", "§ 573"),
    r"untervermiet":                   ("bgb",   "Untervermietung"),
    r"schönheitsreparatur":            ("bgb",   "§ 535"),
    r"verkehrsunfall|straßenverkehr":  ("stvo_2013", None),
    r"arbeitnehmer.*insolvenz|insolvenz.*arbeitnehmer": ("inso", "Arbeitnehmer"),
}

def _direct_tag_search(query: str, db_svc, limit: int = 3) -> list:
    """Direkte DB-Suche für erkannte Rechtsgebiete — umgeht RRF-Verwässerung."""
    lower = query.lower()
    found_tag = None
    fts_term = None
    for pattern, (tag, term) in _TOPIC_TAGS.items():
        if re.search(pattern, lower):
            found_tag = tag
            fts_term = term
            break
    if not found_tag:
        return []
    try:
        from sqlalchemy import text as sqltxt
        db = db_svc.SessionLocal()
        try:
            if fts_term:
                rows = db.execute(sqltxt(
                    "SELECT id, title, text, tags FROM legal_chunks "
                    "WHERE tags = :tag AND (title ILIKE :term OR text ILIKE :term) LIMIT :n"
                ), {"tag": found_tag, "term": f"%{fts_term}%", "n": limit}).fetchall()
            else:
                rows = db.execute(sqltxt(
                    "SELECT id, title, text, tags FROM legal_chunks "
                    "WHERE tags = :tag LIMIT :n"
                ), {"tag": found_tag, "n": limit}).fetchall()
            return [{"id": r.id, "title": r.title, "text": r.text,
                     "tags": r.tags, "score": 0.95, "source": r.tags} for r in rows]
        finally:
            db.close()
    except Exception:
        return []


def _get_dokument_info(text: str):
    """Gibt (embed_query, tags, fts) für bekannte Dokumenttypen zurück, sonst None."""
    lower = text.lower()
    for key, info in _DOKUMENT_TYPEN.items():
        if key in lower:
            return info
    return None

def _extract_query(text: str, intent: str = "frage") -> str:
    lower = text.lower()
    if intent == "erstelle":
        info = _get_dokument_info(text)
        if info:
            return info[0]  # embed_query
    words = re.findall(r"[a-zA-ZäöüÄÖÜß]{4,}", text)
    keywords = [w for w in words if w.lower() not in _STOP]
    return " ".join(keywords[:8]) if keywords else text


# ── Ergebnis-Formatierung ─────────────────────────────────────────────────────

def _format_output(intent: str, original_text: str, chunks: list) -> str:
    if not chunks:
        return (
            "Zu dieser Anfrage konnten keine passenden Einträge in der "
            "Rechtsdatenbank gefunden werden.\n\n"
            "Mögliche Gründe:\n"
            "• Das Thema ist noch nicht in der Datenbank erfasst\n"
            "• Die Datenbank wird nächtlich aktualisiert\n\n"
            "Bitte reformulieren Sie Ihre Anfrage oder wenden Sie sich an einen Kollegen."
        )

    if intent == "erstelle":
        # Dokumentgerüst aus den relevanten Rechtsgrundlagen aufbauen
        gesetze = [c.get("title", "") for c in chunks[:5] if c.get("title")]
        gesetze_str = " · ".join(gesetze[:3]) if gesetze else "relevante Rechtsgrundlagen"
        lines = [
            f"📝 **Dokumententwurf: {original_text}**\n",
            f"*Rechtsgrundlagen: {gesetze_str}*\n",
            "---\n",
            "**[Briefkopf]**\n",
            "_[Name, Adresse Absender]_\n",
            "_[Name, Adresse Empfänger]_\n",
            f"_[Ort, Datum]_\n\n",
        ]
        # Dokumenttyp erkennen für spezifisches Gerüst
        lower = original_text.lower()
        if "kündigung" in lower:
            lines += [
                "**Kündigung des Arbeitsverhältnisses**\n\n",
                "Sehr geehrte Damen und Herren,\n\n",
                "hiermit kündige ich das zwischen uns bestehende Arbeitsverhältnis "
                "fristgerecht zum nächstmöglichen Termin, spätestens jedoch zum "
                "_[Datum]_, gemäß § 622 BGB.\n\n",
                "Ich bitte um eine schriftliche Bestätigung des Eingangs dieser Kündigung "
                "sowie um Ausstellung eines qualifizierten Arbeitszeugnisses gemäß § 630 BGB.\n\n",
            ]
        elif "abmahnung" in lower:
            lines += [
                "**Abmahnung**\n\n",
                "Sehr geehrte/r _[Name]_,\n\n",
                "hiermit mahnen wir Sie wegen folgendem Verhalten ab:\n\n",
                "_[Schilderung des Vorfalls mit Datum]_\n\n",
                "Wir fordern Sie auf, dieses Verhalten künftig zu unterlassen. "
                "Bei Wiederholung behalten wir uns eine Kündigung vor.\n\n",
            ]
        else:
            lines += [
                "**[Betreff]**\n\n",
                "Sehr geehrte Damen und Herren,\n\n",
                "_[Haupttext des Dokuments]_\n\n",
            ]
        lines += [
            "Mit freundlichen Grüßen\n\n",
            "_[Unterschrift]_\n",
            "---\n",
            "**Relevante Rechtsgrundlagen aus der Datenbank:**\n",
        ]
        for i, chunk in enumerate(chunks[:3], 1):
            lines.append(f"\n**{i}. {chunk.get('title','Ohne Titel')}**")
            lines.append(f"{chunk.get('text','')[:250].strip()}...\n")
        return "\n".join(lines)

    elif intent == "erklaere":
        header = f"**{original_text}**\n\n"
    elif intent == "frage":
        header = f"**Zur Frage:** _{original_text}_\n\n"
    else:
        header = f"**Ergebnisse:** {original_text}\n\n"

    lines = [header]
    for i, chunk in enumerate(chunks[:5], 1):
        title = chunk.get("title", "Ohne Titel")
        court = chunk.get("court", "")
        date = chunk.get("date", "")
        text = chunk.get("text", "")[:400].strip()
        meta = " · ".join(filter(None, [court, date]))

        lines.append(f"**{i}. {title}**")
        if meta:
            lines.append(f"*{meta}*")
        lines.append(f"\n{text}{'...' if len(chunk.get('text','')) > 400 else ''}\n")
        lines.append("---\n")

    return "\n".join(lines)


# ── Endpoint ──────────────────────────────────────────────────────────────────

@router.post("", response_model=AskResponse)
async def ask(request: AskRequest):
    t0 = time.monotonic()
    text = request.text.strip()
    reasoning: List[ReasoningStep] = []

    # 1. Intent erkennen
    intent = _detect_intent(text)
    intent_labels = {
        "erstelle":  "Dokumenterstellung",
        "erklaere":  "Rechtliche Erklärung",
        "suche":     "Rechtsprechungssuche",
        "frage":     "Rechtliche Auskunft",
    }
    reasoning.append(ReasoningStep(
        emoji="🧠",
        text=f"Intent erkannt: **{intent_labels.get(intent, intent)}**"
    ))

    # 2. Suchbegriffe extrahieren (intent-bewusst)
    # embed_query: für Vektor-Embedding (natürliche Sprache funktioniert besser)
    # tag_query:   für Tag-Boost (enthält explizite Gesetzes-Abkürzungen)
    tag_query = _extract_query(text, intent)   # z.B. "KSchG Kündigung Arbeitsverhältnis BGB"
    embed_query = _extract_query(text, "frage") or text  # natürlichsprachliche Keywords
    emoji_map = {"erstelle": "📝", "erklaere": "📖", "suche": "🔍", "frage": "⚖️"}
    reasoning.append(ReasoningStep(
        emoji=emoji_map.get(intent, "🔍"),
        text=f"Suche relevante Rechtsgrundlagen: *{tag_query}*"
    ))

    # 3. Suche: bei bekannten Dokumenttypen direkte Tag-DB-Suche + Vektor-Suche
    chunks = []
    try:
        search = _get_search()
        dok_info = _get_dokument_info(text) if intent == "erstelle" else None

        if dok_info:
            embed_q, tags, fts_term = dok_info
            # Direkte Tag-Suche für die relevanten Gesetze
            direct = []
            if tags:
                db_svc = search.database_service
                db = db_svc.SessionLocal()
                try:
                    from sqlalchemy import text as sqltxt
                    for tag in tags:
                        rows = db.execute(sqltxt(
                            "SELECT id, title, text, tags FROM legal_chunks "
                            "WHERE tags = :tag AND text ILIKE :term ORDER BY id LIMIT 3"
                        ), {"tag": tag, "term": f"%{fts_term}%"}).fetchall()
                        for r in rows:
                            direct.append({"id": r.id, "title": r.title, "text": r.text,
                                           "tags": r.tags, "score": 0.95, "source": r.tags})
                finally:
                    db.close()
            raw = direct + search.hybrid_search_with_graph(embed_q, limit=max(0, 8-len(direct)), fast_mode=True)
        else:
            enriched = _enrich_query(embed_query)
            # Direkte Tag-Suche für erkannte Rechtsgebiete (verhindert Nischenrecht)
            direct = _direct_tag_search(enriched, search.database_service, limit=3)
            rest_limit = max(0, 8 - len(direct))
            fused = search.hybrid_search_with_graph(enriched, limit=rest_limit, fast_mode=True)
            direct_ids = {c.get("id") for c in direct}
            raw = direct + [r for r in fused if r.get("id") not in direct_ids]
        # Distanz → Score: bester Treffer = 100%, Rest relativ dazu normalisiert
        distances = [float(r.get("dense_score", r.get("score", 1.0))) for r in raw]
        min_dist = min(distances) if distances else 1.0
        max_dist = max(distances) if distances else 1.0
        dist_range = max_dist - min_dist if max_dist != min_dist else 1.0
        for r, dist in zip(raw, distances):
            score = 1.0 - (dist - min_dist) / dist_range  # bester=1.0, schlechtester=0.0
            # Skaliere auf 60-100% da alle Treffer bereits relevant sind
            score = 0.60 + score * 0.40
            chunks.append({
                "id": r.get("id"),
                "text": r.get("text", ""),
                "title": r.get("title", ""),
                "score": round(score, 2),
                "source": r.get("tags", ""),
                "url": "",
            })
        reasoning.append(ReasoningStep(
            emoji="📚",
            text=f"{len(chunks)} relevante Einträge gefunden"
        ))
    except Exception as e:
        reasoning.append(ReasoningStep(
            emoji="⚠️",
            text=f"Suchfehler: {str(e)[:120]}"
        ))

    # 4. Konfidenz prüfen
    if chunks:
        scores = [c.get("score", 0) for c in chunks if isinstance(c, dict)]
        avg = sum(scores) / len(scores) if scores else 0
        reasoning.append(ReasoningStep(
            emoji="📊",
            text=f"Durchschnittliche Relevanz: {avg:.0%}"
        ))

    # 5. Output aufbereiten
    reasoning.append(ReasoningStep(
        emoji="✍️",
        text="Ergebnis wird aufbereitet..."
    ))
    output = _format_output(intent, text, chunks)

    # Sources
    sources = []
    for c in chunks[:5]:
        if not isinstance(c, dict):
            continue
        sources.append(Source(
            title=c.get("title", "Ohne Titel"),
            court=c.get("court"),
            date=c.get("date"),
            case_number=c.get("case_number"),
            excerpt=(c.get("text") or "")[:200],
        ))

    duration_ms = int((time.monotonic() - t0) * 1000)
    reasoning.append(ReasoningStep(
        emoji="✅",
        text=f"Fertig in {duration_ms} ms"
    ))

    return AskResponse(
        intent=intent,
        reasoning=reasoning,
        output=output,
        sources=sources,
        duration_ms=duration_ms,
    )
