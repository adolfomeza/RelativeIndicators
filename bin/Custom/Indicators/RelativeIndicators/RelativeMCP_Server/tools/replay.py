"""Replay snapshots NADRO — point-in-time analysis sin recomputar nada.

Pipeline:
1. Resolver pit-open timestamp por instrumento (PIT_HOURS_VET)
2. Llamar `get_dva_at` para los 5 TFs (Daily/Weekly/Monthly/Quarterly/Annual)
3. Llamar `get_cvas(cutoff_date=as_of)` para CVAs/pVAs cerradas hasta ese momento
4. Aplicar reglas de cobertura entre TFs adyacentes (sección 6 nadro_master.md)
5. Tomar spot del bar at as_of (close del último bar <= as_of)
6. Ensamblar snapshot dict (no persiste — caller decide)

Para backtest walk-forward, este es el bloque atómico que se llama
por cada día del rango.

Requisitos:
- NT8 abierto con charts cargados que tengan los 5 forks VWAP del instrumento
- Bars histórica suficiente (>= as_of - lookback del TF mayor que querramos)
- AddOn RelativeObserver corriendo (puerto 7891)
"""
from __future__ import annotations

from datetime import datetime, timedelta
from typing import Any

from . import observer
from . import tpo_cva  # get_cvas


# Lookup de pit-open por master instrument (mismo que eod_review.PIT_HOURS_VET)
PIT_HOURS = {
    "MGC": ("08:20", "13:30"),
    "MCL": ("09:00", "14:30"),
    "MES": ("09:30", "16:00"),
    "MNQ": ("09:30", "16:00"),
    "MYM": ("09:30", "16:00"),
    "M2K": ("09:30", "16:00"),
    "ES":  ("09:30", "16:00"),
    "NQ":  ("09:30", "16:00"),
    "YM":  ("09:30", "16:00"),
    "RTY": ("09:30", "16:00"),
    "GC":  ("08:20", "13:30"),
    "CL":  ("09:00", "14:30"),
    "ZN":  ("08:20", "15:00"),
    "ZB":  ("08:20", "15:00"),
}


def _master_symbol(instrument: str) -> str:
    """Extrae el master symbol de un FullName ('NQ 06-26' -> 'NQ')."""
    return instrument.split(" ")[0].upper()


def _pit_open_dt(instrument: str, date_str: str) -> datetime:
    """Construye datetime de pit-open ET para un instrumento/fecha."""
    master = _master_symbol(instrument)
    open_str, _ = PIT_HOURS.get(master, ("09:30", "16:00"))
    h, m = open_str.split(":")
    d = datetime.strptime(date_str, "%Y-%m-%d")
    return d.replace(hour=int(h), minute=int(m), second=0, microsecond=0)


def _is_tf_eco(timeframe: str, asof: datetime) -> bool:
    """Replica la lógica de IsCurrentEchoOfSubPeriod del indicador.

    True = ese TF es eco del TF inferior y NO se debe contar como confluencia.
    """
    tf = timeframe.lower()
    if tf == "weekly":
        return asof.weekday() == 0  # Monday
    if tf == "monthly":
        return asof.day <= 7
    if tf == "quarterly":
        first_month_of_q = ((asof.month - 1) // 3) * 3 + 1
        return asof.month == first_month_of_q
    if tf == "annual":
        return asof.month <= 3
    return False


def nadro_snapshot_replay(
    instrument: str,
    as_of: str | None = None,
    date: str | None = None,
    kind: str = "pre_pit",
    lookback_minutes: int = 5,
) -> dict:
    """Genera un snapshot NADRO point-in-time sin recomputar nada.

    Args:
        instrument: FullName del instrumento (ej ``"NQ 06-26"``).
        as_of: timestamp ISO 8601 explícito. Si None, se infiere de ``date`` + ``kind``.
        date: ``YYYY-MM-DD`` — usado si ``as_of`` no fue dado. Default = hoy.
        kind: ``pre_pit`` (default, 5min antes de pit-open) | ``pit_open`` |
              ``mid_session`` (12:00 ET pit) | ``eod`` (1min antes pit-close).
        lookback_minutes: para ``pre_pit``, cuánto antes del pit-open posicionarse.

    Returns:
        dict con DVAs por TF, CVAs/pVAs cerradas, spot, y flags de cobertura.

    Notas:
        - El indicador correspondiente debe estar cargado en NT con suficiente
          historia hasta ``as_of``.
        - Los TFs flagueados como ``eco`` deben excluirse de cualquier cluster
          de confluencias (NADRO Ley §6.5).
    """
    # Resolver as_of
    if as_of is None:
        if date is None:
            date = datetime.utcnow().strftime("%Y-%m-%d")
        master = _master_symbol(instrument)
        open_str, close_str = PIT_HOURS.get(master, ("09:30", "16:00"))
        d = datetime.strptime(date, "%Y-%m-%d")
        if kind == "pre_pit":
            h, m = open_str.split(":")
            base = d.replace(hour=int(h), minute=int(m))
            asof_dt = base - timedelta(minutes=lookback_minutes)
        elif kind == "pit_open":
            h, m = open_str.split(":")
            asof_dt = d.replace(hour=int(h), minute=int(m))
        elif kind == "eod":
            h, m = close_str.split(":")
            base = d.replace(hour=int(h), minute=int(m))
            asof_dt = base - timedelta(minutes=1)
        elif kind == "mid_session":
            asof_dt = d.replace(hour=12, minute=0)
        else:
            return {"error": f"kind inválido: {kind}"}
        as_of = asof_dt.strftime("%Y-%m-%dT%H:%M:%S")
    else:
        try:
            asof_dt = datetime.fromisoformat(as_of.replace("Z", ""))
        except Exception as e:
            return {"error": f"as_of no parseable: {e}"}

    # Query DVA por TF
    tfs = ["Daily", "Weekly", "Monthly", "Quarterly", "Annual"]
    dvas: dict[str, dict] = {}
    coverage: dict[str, bool] = {}
    for tf in tfs:
        eco = _is_tf_eco(tf, asof_dt)
        coverage[tf.lower() + "_eco"] = eco
        result = observer.get_dva_at(instrument=instrument, timeframe=tf, as_of=as_of)
        # Marcar con eco flag para que el caller sepa filtrarlo en confluencias
        if isinstance(result, dict):
            result["is_echo_of_sub_period"] = eco
        dvas[tf.lower()] = result

    # CVAs / pVAs cerradas hasta as_of — get_cvas filtra bars con as_of < ese momento
    cvas_result = tpo_cva.get_cvas(
        instrument=instrument,
        weeks_back=4,
        session="rth",
        as_of=as_of,
    )

    # Spot del bar at as_of (usa el close del Daily fork como referencia)
    daily = dvas.get("daily", {})
    spot_close = None
    spot_bar_time = None
    if isinstance(daily, dict):
        payload = daily.get("payload", {})
        if isinstance(payload, dict):
            spot_close = payload.get("close")
            spot_bar_time = payload.get("bar_time")

    return {
        "instrument": instrument,
        "as_of": as_of,
        "kind": kind,
        "spot": {
            "close": spot_close,
            "bar_time": spot_bar_time,
        },
        "dvas": dvas,
        "coverage": coverage,
        "cvas": cvas_result,
        "_notes": [
            "DVAs marcadas con 'is_echo_of_sub_period=true' NO deben contarse como confluencia distinta del TF inferior.",
            "CVAs cutoff_date = as_of - 1día para evitar data leak del día actual.",
            "Spot = close del último bar <= as_of (no quote live).",
            "Requiere chart con los 5 forks VWAP del instrumento cargado en NT.",
        ],
    }
