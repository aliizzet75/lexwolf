import os
import psycopg2
import psycopg2.extras
from fastapi import APIRouter, HTTPException
from typing import List, Optional
from pydantic import BaseModel
from datetime import datetime

router = APIRouter(prefix="/mandant", tags=["mandant"])

DATABASE_URL = os.getenv("DATABASE_URL", "postgresql://postgres:postgres@host.docker.internal:5432/lexwolf")


def _get_conn():
    return psycopg2.connect(DATABASE_URL)


class Mandant(BaseModel):
    id: int
    name: str
    email: Optional[str] = None
    aktenzeichen: Optional[str] = None
    erstellt_am: Optional[datetime] = None


@router.get("/search", response_model=List[Mandant])
def search_mandanten(q: str = ""):
    conn = _get_conn()
    try:
        cur = conn.cursor(cursor_factory=psycopg2.extras.RealDictCursor)
        if q:
            cur.execute(
                "SELECT id, name, email, aktenzeichen, erstellt_am FROM mandanten "
                "WHERE name ILIKE %s OR email ILIKE %s OR aktenzeichen ILIKE %s "
                "ORDER BY id LIMIT 50",
                (f"%{q}%", f"%{q}%", f"%{q}%"),
            )
        else:
            cur.execute("SELECT id, name, email, aktenzeichen, erstellt_am FROM mandanten ORDER BY id LIMIT 50")
        rows = cur.fetchall()
        return [dict(r) for r in rows]
    finally:
        conn.close()


@router.get("/{mandant_id}", response_model=Mandant)
def get_mandant(mandant_id: int):
    conn = _get_conn()
    try:
        cur = conn.cursor(cursor_factory=psycopg2.extras.RealDictCursor)
        cur.execute("SELECT id, name, email, aktenzeichen, erstellt_am FROM mandanten WHERE id = %s", (mandant_id,))
        row = cur.fetchone()
        if row is None:
            raise HTTPException(status_code=404, detail="Mandant nicht gefunden")
        return dict(row)
    finally:
        conn.close()
