"""TPO/Volume Profile + CVA fusion engine.

Reconstruye perfiles de volumen por día desde bars 1m del Observer y aplica
reglas NADRO 05-2 para fusionar CVAs multi-día cuando hay equilibrio
sostenido.

Reglas NADRO implementadas:
- Construcción forward-only (builds derecha, nunca mirar atrás)
- Overlap de VA ≥ 50% → fusionar al CVA actual
- Cambio de condición: close fuera del CVA + aceptación (2+ bars fuera) → cortar
- CVAs antiguos al cerrarse dejan "líneas secundarias" en el borde roto
"""
from __future__ import annotations

from collections import defaultdict
from datetime import datetime, time, timedelta

from . import observer


# ---------------------------------------------------------------------------
# Session presets (horarios LOCALES = VET = CT/ET con DST en abril)
# NADRO tradicional usa RTH para TPO/CVA del ES/MES
# ---------------------------------------------------------------------------

SESSION_PRESETS = {
    # RTH estándar US cash session (para ES/MES/NQ): 09:30-16:00 local
    "rth": {"start": time(9, 30), "end": time(16, 0), "group_by": "calendar"},
    # ETH completa (23h electrónica): 17:00-17:00 siguiente día
    "eth": {"start": None, "end": None, "group_by": "eth_reset"},
    # PIT crude oil: 09:00-14:30 local
    "pit_cl": {"start": time(9, 0), "end": time(14, 30), "group_by": "calendar"},
}


# ---------------------------------------------------------------------------
# Utilities
# ---------------------------------------------------------------------------


def _parse_dt(s: str) -> datetime:
    for fmt in ("%Y-%m-%d %H:%M:%S.%f", "%Y-%m-%d %H:%M:%S"):
        try:
            return datetime.strptime(s, fmt)
        except ValueError:
            continue
    return datetime.fromisoformat(s)


def _session_key(dt: datetime, reset_hour: int = 17) -> str:
    """Sesión ETH: si dt.hour >= reset_hour, pertenece al día siguiente."""
    if dt.hour >= reset_hour:
        d = (dt + timedelta(days=1)).date()
    else:
        d = dt.date()
    return d.isoformat()


# ---------------------------------------------------------------------------
# Profile construction
# ---------------------------------------------------------------------------


def build_volume_profile(bars: list[dict], bucket_size: float = 0.25) -> dict[float, int]:
    """Distribuye volumen por precio en buckets de tamaño bucket_size (tick MES)."""
    vp: dict[float, int] = defaultdict(int)
    for b in bars:
        # distribuir vol uniformemente sobre H-L del bar en buckets
        lo, hi = b["l"], b["h"]
        if hi <= lo:
            vp[round(lo / bucket_size) * bucket_size] += b["v"]
            continue
        n_buckets = max(1, int(round((hi - lo) / bucket_size)) + 1)
        share = b["v"] / n_buckets
        price = lo
        for _ in range(n_buckets):
            key = round(price / bucket_size) * bucket_size
            vp[key] += share
            price += bucket_size
    return dict(vp)


def compute_value_area(profile: dict[float, int], va_pct: float = 0.70) -> dict:
    """POC + VAH + VAL usando algoritmo estándar (expansion from POC).

    Returns: {poc, vah, val, total_vol, va_vol, n_prices}
    """
    if not profile:
        return {"poc": 0, "vah": 0, "val": 0, "total_vol": 0, "va_vol": 0, "n_prices": 0}

    total = sum(profile.values())
    target = total * va_pct
    sorted_prices = sorted(profile.keys())

    # POC = precio con mayor volumen
    poc = max(profile, key=profile.get)
    poc_idx = sorted_prices.index(poc)

    va_low_idx = poc_idx
    va_high_idx = poc_idx
    va_vol = profile[poc]

    while va_vol < target and (va_low_idx > 0 or va_high_idx < len(sorted_prices) - 1):
        next_up = profile[sorted_prices[va_high_idx + 1]] if va_high_idx < len(sorted_prices) - 1 else -1
        next_down = profile[sorted_prices[va_low_idx - 1]] if va_low_idx > 0 else -1
        if next_up >= next_down and next_up >= 0:
            va_high_idx += 1
            va_vol += next_up
        elif next_down >= 0:
            va_low_idx -= 1
            va_vol += next_down
        else:
            break

    return {
        "poc": round(poc, 2),
        "vah": round(sorted_prices[va_high_idx], 2),
        "val": round(sorted_prices[va_low_idx], 2),
        "total_vol": round(total),
        "va_vol": round(va_vol),
        "n_prices": len(profile),
    }


def build_daily_profiles(
    bars: list[dict],
    session: str = "rth",
    reset_hour: int = 17,
) -> dict[str, dict]:
    """Agrupa bars por sesión (RTH / ETH / PIT) y construye profile+VA por día.

    - rth (default NADRO): US cash 09:30-16:00 local, agrupa por fecha calendario.
    - eth: sesión electrónica completa 17:00-17:00, agrupa por session_key.
    - pit_cl: pit session crude 09:00-14:30 local.
    """
    preset = SESSION_PRESETS.get(session.lower())
    if preset is None:
        raise ValueError(f"session '{session}' no soportada. Usa: rth | eth | pit_cl")

    by_session: dict[str, list[dict]] = defaultdict(list)
    for b in bars:
        dt = _parse_dt(b["t"])
        if preset["group_by"] == "eth_reset":
            # Sesión ETH: reset_hour agrupa en día siguiente
            key = _session_key(dt, reset_hour)
        else:
            # RTH o PIT: filtrar por horario y agrupar por fecha calendario
            if preset["start"] is not None and preset["end"] is not None:
                if not (preset["start"] <= dt.time() < preset["end"]):
                    continue
            key = dt.date().isoformat()
        by_session[key].append(b)

    # Umbral mínimo bars para sesión válida. Calibrado para soportar 1m y 5m.
    # RTH 1m=390, 5m=78 ambos válidos; rechazar <50 (día incompleto / feriado)
    min_bars = {
        "rth": 50,
        "eth": 30,
        "pit_cl": 40,
    }.get(session.lower(), 30)

    profiles: dict[str, dict] = {}
    for key, ss_bars in by_session.items():
        if len(ss_bars) < min_bars:
            continue
        vp = build_volume_profile(ss_bars)
        va = compute_value_area(vp)
        va["session_date"] = key
        va["session_type"] = session.lower()
        va["n_bars"] = len(ss_bars)
        va["session_start"] = ss_bars[0]["t"]
        va["session_end"] = ss_bars[-1]["t"]
        profiles[key] = va
    return profiles


# ---------------------------------------------------------------------------
# CVA fusion
# ---------------------------------------------------------------------------


def _va_overlap_pct(va_a: dict, va_b: dict) -> float:
    """% overlap de value area entre dos sesiones. 0-1.

    Computado sobre el rango MAYOR (más estricto/NADRO-correcto). Un VA chico
    contenido dentro de un VA grande NO debe fusionar — eso es consolidación
    interna, no extensión de equilibrio. Si usáramos min_range, un VA chico
    enteramente adentro de uno grande daría 100% y fusionaría siempre.
    """
    lo = max(va_a["val"], va_b["val"])
    hi = min(va_a["vah"], va_b["vah"])
    if hi <= lo:
        return 0.0
    overlap = hi - lo
    max_range = max(va_a["vah"] - va_a["val"], va_b["vah"] - va_b["val"])
    return overlap / max_range if max_range > 0 else 0.0


def _breakout_detected(cva: dict, next_va: dict, tolerance_pts: float = 0.5) -> str | None:
    """Detecta breakout del bloque actual con tolerancia en puntos.

    Ignora gaps marginales ≤ tolerance_pts (default 0.5 = 2 ticks MES).
    """
    # Breakout al alza: val del próximo día está por encima del vah + tolerancia
    if next_va["val"] > cva["vah"] + tolerance_pts:
        return "up"
    # Breakout a la baja: vah del próximo día está por debajo del val - tolerancia
    if next_va["vah"] < cva["val"] - tolerance_pts:
        return "down"
    return None


def build_cvas(
    profiles: dict[str, dict],
    overlap_threshold: float = 0.50,
    breakout_tolerance_pts: float = 0.5,
) -> dict:
    """Aplica reglas NADRO forward-fusion para separar pVAs (1 día) y CVAs (2+ días).

    Terminología NADRO estricta:
    - **pVA** (Prior Value Area) = perfil de UN SOLO día, todavía no fusiona.
    - **CVA** (Composite Value Area) = 2+ días fusionados en equilibrio sostenido.

    Un bloque arranca como pVA; si el día siguiente tiene overlap ≥ threshold →
    pasa a CVA. Si el siguiente hace breakout → se cierra y el borde queda
    como línea secundaria.

    ``breakout_tolerance_pts``: ignora gaps marginales de 1-2 ticks que no son
    "cambio de condición" real NADRO.

    Returns:
        {
            "pvas": [días individuales sin fusionar con estado closed/active],
            "cvas": [composites 2+ días con estado closed/active],
            "secondary_lines": [bordes de bloques cerrados por breakout],
        }
    """
    sorted_days = sorted(profiles.keys())
    if not sorted_days:
        return {"pvas": [], "cvas": [], "secondary_lines": []}

    blocks = []  # bloques cerrados (pVA o CVA según len(days))
    secondary = []

    current = {
        "days": [sorted_days[0]],
        "val": profiles[sorted_days[0]]["val"],
        "vah": profiles[sorted_days[0]]["vah"],
        "poc": profiles[sorted_days[0]]["poc"],
        "start_date": sorted_days[0],
        "end_date": sorted_days[0],
        "status": "active",
    }

    def _close_and_push(block, reason, closed_on, breakout_side=None):
        block["status"] = "closed"
        block["closed_reason"] = reason
        block["closed_on"] = closed_on
        blocks.append(block)
        if breakout_side:
            secondary_price = block["vah"] if breakout_side == "up" else block["val"]
            secondary.append({
                "price": secondary_price,
                "side": "upper_of_closed" if breakout_side == "up" else "lower_of_closed",
                "from_start": block["start_date"],
                "from_end": block["end_date"],
                "block_type": "CVA" if len(block["days"]) >= 2 else "pVA",
                "closed_by_day": closed_on,
                "reason": f"breakout_{breakout_side}",
            })

    for day in sorted_days[1:]:
        va = profiles[day]
        overlap = _va_overlap_pct(current, va)
        breakout = _breakout_detected(current, va, tolerance_pts=breakout_tolerance_pts)

        if breakout:
            _close_and_push(current, f"breakout_{breakout}", day, breakout)
            current = {
                "days": [day], "val": va["val"], "vah": va["vah"], "poc": va["poc"],
                "start_date": day, "end_date": day, "status": "active",
            }
        elif overlap >= overlap_threshold:
            # Fusionar → se convierte (o continúa como) CVA
            current["days"].append(day)
            current["vah"] = max(current["vah"], va["vah"])
            current["val"] = min(current["val"], va["val"])
            current["end_date"] = day
            current["last_poc"] = va["poc"]
        else:
            # Drift low overlap sin breakout: cerrar bloque anterior sin línea secundaria
            _close_and_push(current, f"drift_low_overlap_{round(overlap*100, 1)}pct", day)
            current = {
                "days": [day], "val": va["val"], "vah": va["vah"], "poc": va["poc"],
                "start_date": day, "end_date": day, "status": "active",
            }

    blocks.append(current)

    # Separar pVAs (1 día) de CVAs (2+ días)
    pvas = []
    cvas = []
    for b in blocks:
        b["type"] = "CVA" if len(b["days"]) >= 2 else "pVA"
        if b["type"] == "CVA":
            cvas.append(b)
        else:
            pvas.append(b)

    return {"pvas": pvas, "cvas": cvas, "secondary_lines": secondary}


# ---------------------------------------------------------------------------
# Public tool
# ---------------------------------------------------------------------------


def get_cvas(
    instrument: str,
    weeks_back: int = 4,
    days_back: int | None = None,
    overlap_threshold: float = 0.50,
    reset_hour: int = 17,
    session: str = "rth",
    as_of: str | None = None,
) -> dict:
    """Reconstruye pVAs + CVAs NADRO para las últimas ``weeks_back`` semanas completas.

    Carga desde el LUNES de hace ``weeks_back`` semanas hasta hoy para NO
    cortar un CVA en medio por ventana arbitraria.

    ``session`` (NADRO tradicional usa RTH, no ETH):
    - "rth" (default): US cash 09:30-16:00 local. Estándar NADRO ES/MES/NQ.
    - "eth": sesión electrónica 23h, reset 17:00 local.
    - "pit_cl": pit session crude oil 09:00-14:30 local.

    ``days_back`` legacy: si se pasa, usa modo days_back absoluto (compat).

    ``as_of`` (opcional, ISO 8601): si se pasa, filtra bars al cierre del día
    anterior a esa fecha. Útil para replay snapshots — evita data leak del día
    actual al hacer backtest histórico.
    """
    if days_back is not None:
        calendar_days = days_back + 2
    else:
        calendar_days = weeks_back * 7 + 7  # margen 1 semana extra

    # RTH usa 6.5h/día, 5m=78 bars/día pit. ETH usa 23h/día
    if session.lower() == "rth":
        tf = "5m"
        n = min(50000, calendar_days * 370)
    elif calendar_days <= 7:
        tf = "1m"
        n = min(50000, calendar_days * 1840)
    else:
        tf = "5m"
        n = min(50000, calendar_days * 368)

    data = observer.get_bars(instrument, tf=tf, n=n)
    if "error" in data or not data.get("bars"):
        return {
            "error": data.get("error", "sin bars"),
            "addon_reachable": data.get("addon_reachable", False),
        }

    bars = data["bars"]
    # Para replay (as_of): el "ahora" virtual es as_of, no la última barra real.
    # NT8 devuelve bars en hora LOCAL del usuario (VET = ET durante DST), NO UTC.
    # Las SESSION_PRESETS (RTH 09:30-16:00) están en ese mismo huso, así que
    # comparamos as_of (también en ET local) directo como naive datetime.
    if as_of:
        try:
            asof_naive = datetime.fromisoformat(as_of.replace("Z", ""))
            # Filtrar bars < as_of (ambos naive en hora local del usuario = ET)
            bars = [b for b in bars if _parse_dt(b["t"]) < asof_naive]
            anchor_dt = asof_naive
        except Exception:
            anchor_dt = _parse_dt(bars[-1]["t"]) if bars else None
    else:
        anchor_dt = _parse_dt(bars[-1]["t"]) if bars else None

    if anchor_dt is None:
        return {
            "error": "no bars after as_of filter",
            "as_of": as_of,
            "addon_reachable": True,
        }

    if days_back is not None:
        cutoff = (anchor_dt - timedelta(days=days_back + 1)).date()
    else:
        # weekday(): Mon=0..Sun=6. Lunes de la semana del anchor, luego (weeks_back-1) semanas atrás
        days_to_monday = anchor_dt.weekday()
        current_monday = anchor_dt.date() - timedelta(days=days_to_monday)
        cutoff = current_monday - timedelta(weeks=weeks_back - 1)

    bars = [b for b in bars if _parse_dt(b["t"]).date() >= cutoff]

    profiles = build_daily_profiles(bars, session=session, reset_hour=reset_hour)
    cva_result = build_cvas(profiles, overlap_threshold=overlap_threshold)

    return {
        "instrument": instrument,
        "weeks_back": weeks_back if days_back is None else None,
        "days_back": days_back,
        "cutoff_date": cutoff.isoformat(),
        "session": session,
        "reset_hour": reset_hour,
        "tf_used": tf,
        "profiles_analyzed": len(profiles),
        "profiles_by_day": [
            {
                "session_date": d,
                "vah": p["vah"],
                "poc": p["poc"],
                "val": p["val"],
                "n_bars": p["n_bars"],
                "total_vol": p["total_vol"],
            }
            for d, p in sorted(profiles.items())
        ],
        "pvas": cva_result["pvas"],
        "cvas": cva_result["cvas"],
        "secondary_lines": cva_result["secondary_lines"],
        "config": {"overlap_threshold": overlap_threshold, "breakout_tolerance_pts": 0.5},
    }
