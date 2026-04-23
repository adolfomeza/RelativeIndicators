"""NADRO walk-forward EOD — actualiza outcomes de hipótesis.

Dado un snapshot con timestamp T y hipos (entry/stop/targets), recorre las
bars 1m desde T hasta el fin del día (o 'ahora' si aún no terminó) y calcula:

- status: pending | triggered | filled | stopped_out | not_triggered
  * pending:        aún no se ha procesado (estado inicial)
  * triggered:      el precio alcanzó el entry pero no el target ni el stop
  * filled:         alcanzó al menos el target 1 (T1)
  * stopped_out:    alcanzó el stop antes del T1
  * not_triggered:  el precio nunca alcanzó el entry
- triggered_at: timestamp ISO del primer toque al entry
- stop_hit_at:  timestamp ISO del primer toque al stop
- targets_hit:  lista de indices de targets alcanzados ordenados
- mae_pts:      máximo adverso desde entry (puntos)
- mfe_pts:      máximo favorable desde entry (puntos)

Asume:
- Para long: entry alcanzado cuando low ≤ entry (retest). Stop cuando low ≤ stop.
  Target cuando high ≥ target_price.
- Para short: entry alcanzado cuando high ≥ entry. Stop cuando high ≥ stop.
  Target cuando low ≤ target_price.

Order of precedence en la misma barra (conservador): asumimos stop antes que
target si ambos pueden tocar (penaliza optimismo).
"""
from __future__ import annotations

import json
from datetime import datetime, timedelta
from pathlib import Path
from typing import Any

from ..paths import markups_dir
from . import observer


# ============================================================================
# Bars helpers
# ============================================================================

def _parse_bar_time(t: str) -> datetime | None:
    for fmt in ("%Y-%m-%d %H:%M:%S.%f", "%Y-%m-%d %H:%M:%S"):
        try:
            return datetime.strptime(t, fmt)
        except ValueError:
            continue
    return None


def get_bars_range(instrument: str, start: datetime, end: datetime, tf: str = "1m") -> list[dict]:
    """Obtiene bars del AddOn observer entre start y end (aprox)."""
    # Calcular cuántas bars necesitamos (con buffer)
    delta_min = int((end - start).total_seconds() // 60)
    # 1m bars: 1 bar por minuto. Buffer de 30% para asegurar cobertura.
    tf_mult = 1 if tf == "1m" else (5 if tf == "5m" else 15)
    n_bars = max(100, int(delta_min / tf_mult * 1.4))
    n_bars = min(n_bars, 10000)  # cap del AddOn

    data = observer.get_bars(instrument=instrument, tf=tf, n=n_bars)
    all_bars = data.get("bars", [])

    # Filtrar por rango
    out: list[dict] = []
    for b in all_bars:
        bt = _parse_bar_time(b.get("t", ""))
        if bt is None:
            continue
        if bt < start:
            continue
        if bt > end:
            continue
        out.append(b)
    return out


# ============================================================================
# Walk-forward per hypo
# ============================================================================

def walk_forward_hypo(hypo: dict, snapshot_ts: datetime, bars: list[dict]) -> dict:
    """Procesa una hipo y devuelve el outcome dict actualizado.

    Distingue entre:
    - trade_status: lo que le pasó al trader real (stop protege contra eventos post)
    - setup_reached_t1/t2/t3: flags de targets alcanzados eventualmente (ignorando stop)
      → útil para validar si el setup "iba bien" con mala gestión de stop
    """
    direction = (hypo.get("direction") or "").lower()
    entry = float(hypo.get("entry", 0) or 0)
    stop = float(hypo.get("stop", 0) or 0)
    targets = hypo.get("targets", []) or []

    outcome = {
        "status": "pending",
        "trade_status": "pending",
        "triggered_at": None,
        "stop_hit_at": None,
        "targets_hit": [],                 # compat: todos los alcanzados en el período
        "targets_hit_before_stop": [],     # trade real
        "targets_hit_after_stop": [],      # setup validation
        "setup_reached_t1": False,
        "setup_reached_t2": False,
        "setup_reached_t3": False,
        "mae_pts": None,
        "mfe_pts": None,
    }

    if not direction or entry == 0 or stop == 0 or not bars:
        return outcome

    is_long = direction == "long"
    triggered = False
    triggered_time: datetime | None = None
    stopped = False
    mae = 0.0
    mfe = 0.0
    first_event = None  # "stop" | ti (index de target) — primera cosa que pasó tras entry

    for b in bars:
        bt = _parse_bar_time(b.get("t", ""))
        high = float(b.get("h", 0) or 0)
        low = float(b.get("l", 0) or 0)
        if high == 0 or low == 0:
            continue

        # 1) detección de entry
        if not triggered:
            if is_long and low <= entry:
                triggered = True
                triggered_time = bt
            elif not is_long and high >= entry:
                triggered = True
                triggered_time = bt
            if triggered:
                outcome["triggered_at"] = bt.isoformat() if bt else None

        if not triggered:
            continue

        # 2) MAE/MFE desde entry (se actualiza TODO el período triggered)
        if is_long:
            adv = entry - low
            fav = high - entry
        else:
            adv = high - entry
            fav = entry - low
        if adv > mae:
            mae = adv
        if fav > mfe:
            mfe = fav

        # 3) detección de stop — NO break, solo marca el momento
        if not stopped:
            if is_long and low <= stop:
                stopped = True
                outcome["stop_hit_at"] = bt.isoformat() if bt else None
                if first_event is None:
                    first_event = "stop"
            elif not is_long and high >= stop:
                stopped = True
                outcome["stop_hit_at"] = bt.isoformat() if bt else None
                if first_event is None:
                    first_event = "stop"

        # 4) detección de targets — tracking continuo (antes y después del stop)
        for ti, t in enumerate(targets):
            tp = float(t.get("price", 0) or 0)
            if tp == 0:
                continue
            if ti in outcome["targets_hit"]:
                continue
            hit = False
            if is_long and high >= tp:
                hit = True
            elif not is_long and low <= tp:
                hit = True
            if hit:
                outcome["targets_hit"].append(ti)
                if stopped:
                    outcome["targets_hit_after_stop"].append(ti)
                else:
                    outcome["targets_hit_before_stop"].append(ti)
                    if first_event is None:
                        first_event = ti
                # Flags rápidos
                if ti == 0:
                    outcome["setup_reached_t1"] = True
                elif ti == 1:
                    outcome["setup_reached_t2"] = True
                elif ti == 2:
                    outcome["setup_reached_t3"] = True

    outcome["mae_pts"] = round(mae, 2) if triggered else None
    outcome["mfe_pts"] = round(mfe, 2) if triggered else None

    # trade_status: lo que le pasó al trader real (primer evento tras entry)
    if not triggered:
        trade_status = "not_triggered"
    elif first_event == "stop":
        trade_status = "stopped_out"
    elif isinstance(first_event, int):
        trade_status = "filled"  # tocó target antes del stop
    else:
        trade_status = "triggered"  # triggered pero aún abierto (ni stop ni target)

    outcome["trade_status"] = trade_status
    outcome["status"] = trade_status  # compat con schema existente

    return outcome


# ============================================================================
# Snapshot-level processor
# ============================================================================

def close_snapshot_file(path: Path, end_time: datetime | None = None) -> dict:
    """Procesa todas las hipos de un archivo markup y actualiza outcomes."""
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)

    instrument_master = data.get("instrument", "")
    # Buscar nombre completo con sufijo contrato (ej MGC 06-26) — el AddOn lo necesita
    # Heurística: tomar el primer match de indicator_states; fallback: master + " 06-26"
    # Por simplicidad lo preguntamos al observer de una vez
    full_instrument = f"{instrument_master} 06-26"  # TODO: inferir sufijo dinámicamente

    updates = {"hypos_processed": 0, "status_changes": {}}
    for snap in data.get("snapshots", []):
        ts_str = snap.get("timestamp")
        snap_ts = None
        if ts_str:
            try:
                snap_ts = datetime.fromisoformat(ts_str.replace("Z", ""))
            except ValueError:
                snap_ts = None
        if not snap_ts:
            continue

        # Rango: desde snapshot hasta end_time (o ahora)
        end = end_time or datetime.now()
        if end < snap_ts:
            continue

        # Obtener bars
        bars = get_bars_range(full_instrument, snap_ts, end, tf="1m")
        if not bars:
            continue

        for hypo in snap.get("hypos", []):
            outcome = walk_forward_hypo(hypo, snap_ts, bars)
            old_status = (hypo.get("outcome") or {}).get("status", "pending")
            new_status = outcome["status"]
            if old_status != new_status:
                key = f"{hypo.get('id', '?')}: {old_status} -> {new_status}"
                updates["status_changes"][key] = {
                    "triggered_at": outcome["triggered_at"],
                    "stop_hit_at": outcome["stop_hit_at"],
                    "targets_hit": outcome["targets_hit"],
                    "mae_pts": outcome["mae_pts"],
                    "mfe_pts": outcome["mfe_pts"],
                }
            hypo["outcome"] = outcome
            updates["hypos_processed"] += 1

    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)

    return {
        "file": path.name,
        "instrument": instrument_master,
        "hypos_processed": updates["hypos_processed"],
        "status_changes": updates["status_changes"],
    }


def close_eod(date_str: str | None = None, instrument: str | None = None) -> dict:
    """Walk-forward para todos los markups del día especificado.

    ``date_str``: YYYY-MM-DD (default: hoy).
    ``instrument``: si se especifica, procesa solo ese master symbol (ej "MGC").
    """
    if not date_str:
        date_str = datetime.now().strftime("%Y-%m-%d")

    mdir = markups_dir()
    if not mdir.exists():
        return {"error": "markups_dir no existe", "path": str(mdir)}

    pattern = f"{instrument}_{date_str}.json" if instrument else f"*_{date_str}.json"
    files = sorted(mdir.glob(pattern))
    if not files:
        return {"error": "no se encontraron archivos", "pattern": pattern}

    results = []
    for f in files:
        try:
            r = close_snapshot_file(f)
            results.append(r)
        except Exception as exc:  # noqa: BLE001
            results.append({"file": f.name, "error": str(exc)})

    return {
        "date": date_str,
        "files_processed": len(results),
        "results": results,
    }
