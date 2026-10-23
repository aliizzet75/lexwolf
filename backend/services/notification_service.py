"""Benachrichtigungen für Ops-Events (nächtlicher Crawl, Watchdog).

Primärkanal ist Microsoft Teams (Incoming Webhook via O365 Connector
MessageCard). Solange TEAMS_WEBHOOK_URL nicht gesetzt ist (Stand 2026-10-23:
keine Webhook-URL im System gefunden — muss mit Ali abgestimmt werden),
oder der Teams-Versand fehlschlägt, geht die Nachricht per Telegram raus
(selbe Bot-Credentials wie in aligator.py, dort bereits nachweislich
funktionsfähig) — damit Ali auch ohne konfigurierten Webhook benachrichtigt
wird statt stillschweigend nichts zu bekommen.
"""
import logging
import os

import requests

logger = logging.getLogger(__name__)

TEAMS_WEBHOOK_URL = os.getenv("TEAMS_WEBHOOK_URL", "")
TELEGRAM_BOT_TOKEN = os.getenv("TELEGRAM_BOT_TOKEN", "8670442402:AAGmQInAwWjzSnGluMmMl4tf_i-Pe8dHYvU")
TELEGRAM_CHAT_ID = os.getenv("TELEGRAM_CHAT_ID", "8778331431")


def _send_teams(title: str, text: str, color: str) -> bool:
    if not TEAMS_WEBHOOK_URL:
        return False
    try:
        r = requests.post(
            TEAMS_WEBHOOK_URL,
            json={
                "@type": "MessageCard",
                "@context": "http://schema.org/extensions",
                "themeColor": color,
                "summary": title,
                "sections": [{"activityTitle": title, "text": text}],
            },
            timeout=10,
        )
        if r.status_code >= 300:
            logger.warning(f"Teams-Webhook antwortete {r.status_code}: {r.text[:300]}")
            return False
        return True
    except Exception as e:
        logger.warning(f"Teams-Webhook fehlgeschlagen: {e}")
        return False


def _send_telegram(text: str) -> bool:
    try:
        r = requests.post(
            f"https://api.telegram.org/bot{TELEGRAM_BOT_TOKEN}/sendMessage",
            data={"chat_id": TELEGRAM_CHAT_ID, "text": text, "parse_mode": "Markdown"},
            timeout=10,
        )
        return r.status_code < 300
    except Exception as e:
        logger.warning(f"Telegram-Fallback fehlgeschlagen: {e}")
        return False


def notify(title: str, text: str, is_error: bool = False) -> None:
    """Schickt eine Ops-Benachrichtigung. Versucht Teams, fällt auf Telegram
    zurück wenn Teams nicht konfiguriert ist oder der Versand fehlschlägt."""
    color = "D32F2F" if is_error else "2E7D32"
    teams_ok = _send_teams(title, text, color)
    if teams_ok:
        logger.info(f"Teams-Benachrichtigung gesendet: {title}")
        return

    reason = "TEAMS_WEBHOOK_URL nicht konfiguriert" if not TEAMS_WEBHOOK_URL else "Teams-Versand fehlgeschlagen"
    logger.warning(f"{reason} — Fallback auf Telegram")
    prefix = "🔴" if is_error else "🟢"
    fallback_text = f"{prefix} *{title}*\n{text}\n\n_(Teams-Fallback: {reason})_"
    if _send_telegram(fallback_text):
        logger.info(f"Telegram-Fallback gesendet: {title}")
    else:
        logger.error(f"Benachrichtigung komplett fehlgeschlagen (Teams + Telegram): {title}")
