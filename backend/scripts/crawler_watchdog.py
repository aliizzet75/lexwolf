#!/usr/bin/env python3
"""Watchdog für lexwolf-crawler.service.

Läuft morgens (eigener Timer, siehe lexwolf-crawler-watchdog.timer) deutlich
nach dem erwarteten nächtlichen 3-Uhr-Lauf und prüft via systemctl, ob der
Crawl heute überhaupt gestartet ist und erfolgreich durchlief. Existiert für
genau den Fall, den main_crawler.py selbst nicht abdecken kann: der Prozess
startet gar nicht erst (Timer deaktiviert, Server down um 3 Uhr, Python
crasht vor der eigenen notify()-Benachrichtigung) — dann bliebe Ali sonst
ganz ohne Nachricht.
"""
import subprocess
import sys
from datetime import datetime, timezone

sys.path.insert(0, "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf/backend")
from services.notification_service import notify

SERVICE = "lexwolf-crawler.service"


def _systemctl_show() -> dict:
    r = subprocess.run(
        ["systemctl", "show", SERVICE,
         "-p", "ExecMainStartTimestamp,ExecMainStatus,Result,ActiveState,SubState"],
        capture_output=True, text=True, timeout=10,
    )
    out = {}
    for line in r.stdout.splitlines():
        if "=" in line:
            k, v = line.split("=", 1)
            out[k] = v
    return out


def check() -> tuple[bool, str]:
    """Gibt (ok, begründung) zurück."""
    info = _systemctl_show()
    if not info:
        return False, "systemctl show lieferte keine Daten — Service evtl. unbekannt"

    start_ts = info.get("ExecMainStartTimestamp", "")
    if not start_ts:
        return False, f"{SERVICE} hat noch nie gestartet (ExecMainStartTimestamp leer)"

    try:
        started = datetime.strptime(start_ts, "%a %Y-%m-%d %H:%M:%S %Z")
        started = started.replace(tzinfo=timezone.utc)
    except ValueError:
        return False, f"Konnte ExecMainStartTimestamp nicht parsen: {start_ts!r}"

    now = datetime.now(timezone.utc)
    if started.date() != now.date():
        hours_ago = (now - started).total_seconds() / 3600
        return False, (
            f"Letzter Lauf war am {started.date()} ({hours_ago:.1f}h her) — "
            f"heute ({now.date()}) noch KEIN Lauf gestartet"
        )

    result = info.get("Result", "")
    status = info.get("ExecMainStatus", "")
    if result != "success" or status != "0":
        return False, f"Heutiger Lauf ({started}) war nicht erfolgreich: Result={result}, ExecMainStatus={status}"

    return True, f"Heutiger Lauf um {started.strftime('%H:%M UTC')} erfolgreich (Result=success)"


def main():
    ok, reason = check()
    if ok:
        print(f"OK: {reason}")
        return
    print(f"ESKALATION: {reason}")
    notify(
        "⚠️ LexWolf-Crawl-Watchdog: Kein erfolgreicher nächtlicher Lauf gefunden",
        f"{reason}\n\nBitte prüfen: systemctl status {SERVICE}, "
        f"journalctl -u {SERVICE} -S today, systemctl list-timers {SERVICE.replace('.service', '.timer')}",
        is_error=True,
    )


if __name__ == "__main__":
    main()
