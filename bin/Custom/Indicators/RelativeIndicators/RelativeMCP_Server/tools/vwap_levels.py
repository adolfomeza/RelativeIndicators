"""Parseo de archivos VwapLevels/ exportados por la suite RelativeIndicators.

Formato Daily|Weekly|Monthly|Quarterly|Annual:
    INSTRUMENT=MES
    TIMEFRAME=Daily
    TIMESTAMP=2026-04-19 22:46:13
    DVAH=...
    VWAP=...            (antes PVA= — retrocompat)
    DVAL=...
    ZONE_COUNT=N
    ZONE_i=upper|mid|lower|startTime

Formato Confluences_{INSTRUMENT}.txt (pipe, sin cabecera):
    member1|member2|...|memberN|PriceMin|PriceMax|StartTime|LastSeenTime|EndTime|flags

Los últimos 6 campos son fijos; todos los previos son miembros del grupo.
Flag 0x01=IsActive, 0x02=IsBreached, 0x04=IsArmed (ver v2.3.7).
"""
from __future__ import annotations

from datetime import datetime
from pathlib import Path
from typing import Iterable

from ..paths import vwap_levels_dir


TIMEFRAMES = ("Daily", "Weekly", "Monthly", "Quarterly", "Annual")


def list_instruments() -> dict:
    """Lista instrumentos con archivos VwapLevels presentes."""
    d = vwap_levels_dir()
    if not d.is_dir():
        return {"error": f"no existe {d}", "instruments": []}
    found: dict[str, dict] = {}
    for f in d.iterdir():
        if not f.is_file() or f.suffix != ".txt":
            continue
        parts = f.stem.split("_", 1)
        if len(parts) != 2:
            continue
        kind, inst = parts
        slot = found.setdefault(inst, {"timeframes": [], "has_confluences": False})
        if kind == "Confluences":
            slot["has_confluences"] = True
        elif kind in TIMEFRAMES:
            slot["timeframes"].append(kind)
    return {
        "dir": str(d),
        "instruments": [
            {"instrument": k, **v, "timeframes": sorted(v["timeframes"])}
            for k, v in sorted(found.items())
        ],
    }


def _parse_ini_like(path: Path) -> dict:
    kv: dict[str, str] = {}
    for ln in path.read_text(encoding="utf-8", errors="replace").splitlines():
        if not ln or "=" not in ln:
            continue
        k, _, v = ln.partition("=")
        kv[k.strip()] = v.strip()
    return kv


def read_vwap_levels(instrument: str, timeframe: str) -> dict:
    """Devuelve DVAH/VWAP/DVAL + zonas históricas de un timeframe."""
    if timeframe not in TIMEFRAMES:
        return {"error": f"timeframe inválido: {timeframe}. Usa {list(TIMEFRAMES)}"}
    path = vwap_levels_dir() / f"{timeframe}_{instrument}.txt"
    if not path.is_file():
        return {"error": f"no existe {path}"}
    kv = _parse_ini_like(path)

    def _f(key: str) -> float | None:
        v = kv.get(key)
        try:
            return float(v) if v is not None else None
        except (TypeError, ValueError):
            return None

    zones: list[dict] = []
    try:
        n = int(kv.get("ZONE_COUNT", "0"))
    except ValueError:
        n = 0
    for i in range(n):
        raw = kv.get(f"ZONE_{i}")
        if not raw:
            continue
        parts = raw.split("|")
        if len(parts) < 4:
            continue
        try:
            zones.append({
                "index": i,
                "upper": float(parts[0]),
                "mid": float(parts[1]),
                "lower": float(parts[2]),
                "start_time": parts[3],
            })
        except ValueError:
            continue

    # Retrocompat: si el archivo trae PVA= en lugar de VWAP=
    vwap = _f("VWAP")
    if vwap is None:
        vwap = _f("PVA")

    return {
        "file": str(path),
        "instrument": kv.get("INSTRUMENT", instrument),
        "timeframe": kv.get("TIMEFRAME", timeframe),
        "timestamp": kv.get("TIMESTAMP"),
        "dvah": _f("DVAH"),
        "vwap": vwap,
        "dval": _f("DVAL"),
        "zones": zones,
    }


def _parse_confluence_line(line: str) -> dict | None:
    parts = line.rstrip("\r\n").split("|")
    # últimos 6 campos fijos: PriceMin, PriceMax, StartTime, LastSeenTime, EndTime, flags
    if len(parts) < 7:
        return None
    fixed = parts[-6:]
    members = parts[: len(parts) - 6]
    try:
        price_min = float(fixed[0])
        price_max = float(fixed[1])
        flags = int(fixed[5])
    except ValueError:
        return None
    if price_min <= 0 or price_max <= 0 or price_max < price_min:
        return None
    end_time = fixed[4]
    is_active = (flags & 0x01) != 0 and end_time.startswith("0001-01-01")
    return {
        "members": members,
        "member_count": len(members),
        "price_min": price_min,
        "price_max": price_max,
        "start_time": fixed[2],
        "last_seen_time": fixed[3],
        "end_time": end_time,
        "flags": flags,
        "is_active": is_active,
        "is_breached": (flags & 0x02) != 0,
        "is_armed": (flags & 0x04) != 0,
    }


def list_confluences(instrument: str, only_active: bool = True) -> dict:
    """Lista grupos de confluencia.

    Por default solo devuelve los activos (no cerrados ni breached).
    Si ``only_active=False`` incluye todos los históricos.
    """
    path = vwap_levels_dir() / f"Confluences_{instrument}.txt"
    if not path.is_file():
        return {"error": f"no existe {path}", "confluences": []}
    items: list[dict] = []
    for ln in path.read_text(encoding="utf-8", errors="replace").splitlines():
        if not ln.strip():
            continue
        parsed = _parse_confluence_line(ln)
        if parsed is None:
            continue
        if only_active and not parsed["is_active"]:
            continue
        items.append(parsed)
    items.sort(key=lambda c: c["price_min"])
    return {
        "file": str(path),
        "instrument": instrument,
        "only_active": only_active,
        "count": len(items),
        "confluences": items,
    }


def snapshot(instrument: str) -> dict:
    """Snapshot completo de un instrumento: todos los timeframes + confluencias activas."""
    out: dict = {"instrument": instrument, "timeframes": {}}
    for tf in TIMEFRAMES:
        data = read_vwap_levels(instrument, tf)
        if "error" not in data:
            out["timeframes"][tf] = data
    out["confluences"] = list_confluences(instrument, only_active=True).get("confluences", [])
    return out
