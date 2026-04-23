"""NADRO markup snapshot persistence.

Escribe/actualiza archivos JSON en ``Docs/Nadro/markups/{INSTRUMENT}_{DATE}.json``
que son consumidos por el indicador ``RelativeNadroMarkup`` en NinjaTrader 8.

Schema: ver ``Docs/Nadro/markups/`` examples. Cada archivo agrupa los snapshots
del día bajo el array ``snapshots``. Esta tool hace APPEND si ya existe archivo
del día, sobrescribe si un snapshot con el mismo ``id`` ya está presente.
"""
from __future__ import annotations

import json
import re
from datetime import datetime
from pathlib import Path
from typing import Any

from ..paths import markups_dir


_SCHEMA_VERSION = "1.0"


def _master_symbol(instrument: str) -> str:
    """Extrae el master symbol. "MGC 06-26" -> "MGC". "M2K 06-26" -> "M2K".

    NT8 permite alfanuméricos en el master name (M2K, 6A, etc.). El separador
    del contrato es el primer espacio o guion, NO un cambio alfa->numérico.
    """
    if not instrument:
        return ""
    s = instrument.strip()
    # Tomar primer token separado por espacio o guion
    token = s.split()[0] if s.split() else s
    # Y cortar en el primer guion si aún tiene (caso "MES-06-26")
    token = token.split("-")[0]
    return token.upper()


def _parse_timestamp(ts: str | None) -> datetime:
    """Parsea timestamp ISO o devuelve now()."""
    if not ts:
        return datetime.now()
    try:
        return datetime.fromisoformat(ts.replace("Z", ""))
    except (ValueError, AttributeError):
        return datetime.now()


def _default_outcome() -> dict:
    return {
        "status": "pending",
        "triggered_at": None,
        "stop_hit_at": None,
        "targets_hit": [],
        "mae_pts": None,
        "mfe_pts": None,
    }


def save_snapshot(
    instrument: str,
    price_at_analysis: float,
    regime: str = "",
    bias: str = "",
    summary: str = "",
    analysis_text: str = "",
    confluences: list[dict] | None = None,
    levels: list[dict] | None = None,
    hypos: list[dict] | None = None,
    timestamp: str | None = None,
    snapshot_id: str | None = None,
) -> dict:
    """Persiste un snapshot NADRO en el archivo del día.

    - Si el archivo del día no existe → lo crea con schema mínimo.
    - Si existe y contiene un snapshot con el mismo ``id`` → lo sobrescribe.
    - Si existe y el ``id`` es nuevo → hace append al array ``snapshots``.

    El ``id`` por default es ``{MASTER}_{YYYYMMDD_HHMM}``.

    Normaliza cada hypo: asegura outcome pending por default y calcula
    ``risk_pts = |entry - stop|`` si no viene provisto.

    Returns: ``{"path": str, "action": "created"|"appended"|"updated",
                "snapshot_id": str, "total_snapshots_today": int}``.
    """
    master = _master_symbol(instrument)
    if not master:
        return {"error": "instrument invalido", "instrument": instrument}

    ts = _parse_timestamp(timestamp)
    date_str = ts.strftime("%Y-%m-%d")
    if not snapshot_id:
        snapshot_id = f"{master}_{ts.strftime('%Y%m%d_%H%M')}"

    # Normalizar hypos
    norm_hypos: list[dict] = []
    for i, h in enumerate(hypos or []):
        h2 = dict(h)
        if "id" not in h2 or not h2["id"]:
            h2["id"] = f"h{i + 1}"
        if "risk_pts" not in h2 or not h2["risk_pts"]:
            try:
                h2["risk_pts"] = abs(float(h2.get("entry", 0)) - float(h2.get("stop", 0)))
            except (TypeError, ValueError):
                h2["risk_pts"] = 0.0
        if "outcome" not in h2:
            h2["outcome"] = _default_outcome()
        # Auto-calcular rr por target si falta y hay risk
        risk = float(h2.get("risk_pts", 0) or 0)
        if risk > 0:
            for t in h2.get("targets", []):
                if "rr" not in t or t["rr"] in (None, 0):
                    try:
                        reward = abs(float(h2["entry"]) - float(t["price"]))
                        t["rr"] = round(reward / risk, 1)
                    except (TypeError, ValueError, KeyError):
                        pass
        norm_hypos.append(h2)

    new_snapshot = {
        "id": snapshot_id,
        "timestamp": ts.strftime("%Y-%m-%dT%H:%M:%S"),
        "price_at_analysis": price_at_analysis,
        "regime": regime,
        "bias": bias,
        "summary": summary,
        "analysis_text": analysis_text,
        "confluences": confluences or [],
        "levels": levels or [],
        "hypos": norm_hypos,
    }

    mdir = markups_dir()
    mdir.mkdir(parents=True, exist_ok=True)
    path = mdir / f"{master}_{date_str}.json"

    action: str
    if path.exists():
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            data = None
        if not isinstance(data, dict) or "snapshots" not in data:
            data = {
                "schema_version": _SCHEMA_VERSION,
                "instrument": master,
                "date": date_str,
                "snapshots": [],
            }

        # Sobrescribir si id ya existe, sino append
        existing_ids = [s.get("id") for s in data["snapshots"]]
        if snapshot_id in existing_ids:
            data["snapshots"] = [
                new_snapshot if s.get("id") == snapshot_id else s
                for s in data["snapshots"]
            ]
            action = "updated"
        else:
            data["snapshots"].append(new_snapshot)
            action = "appended"
    else:
        data = {
            "schema_version": _SCHEMA_VERSION,
            "instrument": master,
            "date": date_str,
            "snapshots": [new_snapshot],
        }
        action = "created"

    path.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")

    return {
        "path": str(path),
        "action": action,
        "snapshot_id": snapshot_id,
        "instrument_master": master,
        "date": date_str,
        "total_snapshots_today": len(data["snapshots"]),
    }


def list_snapshots(instrument: str | None = None, date: str | None = None) -> dict:
    """Lista los markups guardados. Filtra opcional por instrument (master) y date."""
    mdir = markups_dir()
    if not mdir.exists():
        return {"snapshots": [], "markups_dir": str(mdir), "exists": False}

    master = _master_symbol(instrument) if instrument else None
    files: list[dict] = []
    for p in sorted(mdir.glob("*.json")):
        name = p.stem  # e.g. "MGC_2026-04-21"
        parts = name.rsplit("_", 1)
        if len(parts) != 2:
            continue
        file_master, file_date = parts
        if master and file_master.upper() != master:
            continue
        if date and file_date != date:
            continue
        try:
            data = json.loads(p.read_text(encoding="utf-8"))
            snap_ids = [s.get("id") for s in data.get("snapshots", [])]
        except Exception as exc:  # noqa: BLE001
            snap_ids = [f"<error: {exc}>"]
        files.append({
            "file": p.name,
            "instrument": file_master,
            "date": file_date,
            "snapshot_ids": snap_ids,
            "count": len(snap_ids),
        })

    return {
        "markups_dir": str(mdir),
        "exists": True,
        "files": files,
        "total_files": len(files),
    }
