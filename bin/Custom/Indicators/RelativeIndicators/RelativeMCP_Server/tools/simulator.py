"""NADRO walk-forward backtest simulator — raw edge analysis.

Pipeline:
1. enumerate_hipos(snapshot) -> list of candidate setups for visible levels
2. simulate_hypo(hypo, future_bars) -> trigger detection + outcome tracking
3. find_last_ha_pivot(bars, side) -> dynamic stop based on last HA swing
4. nadro_walkforward(instrument, dates) -> loop + aggregate stats per setup type

Diseño raw (Capa 1):
- Sin filtros de disciplina NADRO (no max_attempts, no cooldowns)
- Cada gatillo dispara un trade independiente
- Stops por HA pivot dinámico (NADRO Guía 03 §9)
- Targets estáticos calculados por offset del entry

Reglas de gatillo (NADRO Guía 03 §8):
- BPB: breakout previo + retest del nivel + cierre HA cambio color + 4 barras pullback
- IPB: precio se acerca al nivel + cierre HA cambio color contra rebote + 4 barras

NO confundir con:
- RPB (range pullback) - requiere rango establecido
- EF (evaluation failure) - requiere falso breakout
Para v1 solo BPB e IPB que son los más estructurales.
"""
from __future__ import annotations

from collections import defaultdict
from datetime import datetime, time as dtime, timedelta
from typing import Any

from . import observer
from . import replay


# Instrument-specific config
PIT_HOURS = {
    "NQ":  ("09:30", "16:00"),
    "MNQ": ("09:30", "16:00"),
    "ES":  ("09:30", "16:00"),
    "MES": ("09:30", "16:00"),
    "MGC": ("08:20", "13:30"),
    "MCL": ("09:00", "14:30"),
}


def _master_symbol(instrument: str) -> str:
    return instrument.split(" ")[0].upper()


# ----------------------------------------------------------------------------
# Hypo enumerator — generate all candidate setups from a replay snapshot
# ----------------------------------------------------------------------------

def enumerate_hipos(snapshot: dict) -> list[dict]:
    """Genera lista de hipos candidatas a partir de un replay snapshot.

    Cada nivel visible (no-eco) genera 2 candidatos: BPB y IPB en ambas direcciones
    según la posición del precio respecto al nivel.
    """
    hipos = []
    spot = snapshot.get("spot", {}).get("close")
    if spot is None:
        return hipos

    # Recolectar TODOS los niveles operables
    levels = []
    cvas = snapshot.get("cvas", {})

    # pVAs cerradas (válidas como referencia)
    for pva in cvas.get("pvas", []):
        if pva.get("status") == "closed":
            label = f"pVA-{pva['start_date']}"
            levels.append({"label": f"{label}-VAH", "price": pva["vah"], "type": "pVAH"})
            levels.append({"label": f"{label}-VAL", "price": pva["val"], "type": "pVAL"})
        elif pva.get("status") == "active":
            # pVA activa = pVA del día anterior, válida también
            levels.append({"label": "pVAH-active", "price": pva["vah"], "type": "pVAH"})
            levels.append({"label": "pVAL-active", "price": pva["val"], "type": "pVAL"})

    # CVAs cerradas
    for cva in cvas.get("cvas", []):
        if cva.get("status") == "closed":
            label = f"CVA-{cva['start_date']}-{cva['end_date']}"
            levels.append({"label": f"{label}-VAH", "price": cva["vah"], "type": "CVAH"})
            levels.append({"label": f"{label}-VAL", "price": cva["val"], "type": "CVAL"})
        elif cva.get("status") == "active":
            levels.append({"label": "CVAH-active", "price": cva["vah"], "type": "CVAH"})
            levels.append({"label": "CVAL-active", "price": cva["val"], "type": "CVAL"})

    # Secondary lines (bordes rotos)
    for sec in cvas.get("secondary_lines", []):
        levels.append({
            "label": f"sec-{sec['side']}-{sec['from_start']}",
            "price": sec["price"],
            "type": "secondary",
        })

    # DVAs operables (no eco)
    for tf_name in ["daily", "weekly", "monthly", "quarterly", "annual"]:
        dva = snapshot.get("dvas", {}).get(tf_name, {})
        if dva.get("is_echo_of_sub_period"):
            continue
        payload = dva.get("payload", {})
        if not payload or "dvah" not in payload:
            continue
        prefix = {"daily": "D", "weekly": "W", "monthly": "M", "quarterly": "Q", "annual": "Y"}[tf_name]
        if payload["dvah"]:
            levels.append({"label": f"{prefix}DVAH", "price": payload["dvah"], "type": "DVAH"})
        if payload["dval"]:
            levels.append({"label": f"{prefix}DVAL", "price": payload["dval"], "type": "DVAL"})

    # Generar hipos: para cada nivel, BPB e IPB según posición del precio
    for lvl in levels:
        if lvl["price"] is None or lvl["price"] <= 0:
            continue

        is_resistance = lvl["price"] > spot  # nivel arriba del precio = potencial resistencia
        is_support = lvl["price"] < spot     # nivel abajo del precio = potencial soporte

        if is_resistance:
            # BPB-long: breakout up + retest, fires long
            hipos.append({
                "id": f"BPB-{lvl['label']}",
                "setup_type": f"BPB-{lvl['type']}",
                "direction": "long",
                "level_label": lvl["label"],
                "level_price": lvl["price"],
                "level_type": lvl["type"],
                "trigger_condition": "BPB",
                "distance_to_level": lvl["price"] - spot,
            })
            # IPB-short: rebote desde resistencia, fires short
            hipos.append({
                "id": f"IPB-{lvl['label']}",
                "setup_type": f"IPB-{lvl['type']}",
                "direction": "short",
                "level_label": lvl["label"],
                "level_price": lvl["price"],
                "level_type": lvl["type"],
                "trigger_condition": "IPB",
                "distance_to_level": lvl["price"] - spot,
            })
        elif is_support:
            # BPB-short: breakout down + retest
            hipos.append({
                "id": f"BPB-{lvl['label']}",
                "setup_type": f"BPB-{lvl['type']}",
                "direction": "short",
                "level_label": lvl["label"],
                "level_price": lvl["price"],
                "level_type": lvl["type"],
                "trigger_condition": "BPB",
                "distance_to_level": spot - lvl["price"],
            })
            # IPB-long: rebote desde soporte
            hipos.append({
                "id": f"IPB-{lvl['label']}",
                "setup_type": f"IPB-{lvl['type']}",
                "direction": "long",
                "level_label": lvl["label"],
                "level_price": lvl["price"],
                "level_type": lvl["type"],
                "trigger_condition": "IPB",
                "distance_to_level": spot - lvl["price"],
            })

    return hipos


# ----------------------------------------------------------------------------
# HA pivot finder — find last swing high/low in HA bars
# ----------------------------------------------------------------------------

def find_last_ha_pivot(ha_bars: list[dict], side: str, lookback: int = 30) -> float | None:
    """Encuentra el último HA swing high (side=high) o low (side=low).

    Pivot HA = bar con HA color cambio significativo.
    Para v1 simplificado: bar con highest high (o lowest low) en últimas N bars
    que también es un pivote local (3 bars cada lado).
    """
    if not ha_bars or len(ha_bars) < 5:
        return None

    bars = ha_bars[-lookback:] if len(ha_bars) > lookback else ha_bars

    if side == "high":
        # Para stop short: último swing high
        best = None
        for i in range(2, len(bars) - 2):
            h = bars[i].get("hah") or bars[i].get("h")
            if h is None:
                continue
            # Pivot: high mayor que 2 bars atrás y 2 bars adelante
            left = max(bars[i-2].get("hah") or bars[i-2].get("h"), bars[i-1].get("hah") or bars[i-1].get("h"))
            right = max(bars[i+1].get("hah") or bars[i+1].get("h"), bars[i+2].get("hah") or bars[i+2].get("h"))
            if h > left and h > right:
                best = h  # último encontrado
        return best
    else:  # side == "low"
        best = None
        for i in range(2, len(bars) - 2):
            l = bars[i].get("hal") or bars[i].get("l")
            if l is None:
                continue
            left = min(bars[i-2].get("hal") or bars[i-2].get("l"), bars[i-1].get("hal") or bars[i-1].get("l"))
            right = min(bars[i+1].get("hal") or bars[i+1].get("l"), bars[i+2].get("hal") or bars[i+2].get("l"))
            if l < left and l < right:
                best = l
        return best


# ----------------------------------------------------------------------------
# Trigger detector — detect entry trigger for a hypo
# ----------------------------------------------------------------------------

def detect_trigger(
    hypo: dict,
    bars: list[dict],
    bar_idx: int,
    proximity_pts: float = 15.0,
    min_pullback_bars: int = 2,
) -> bool:
    """Detecta si el hypo dispara entry en bar_idx.

    Reglas v1 simplificadas:
    - BPB long: precio había roto level UP previamente, ahora está dentro de proximity_pts
      del nivel desde arriba, último cierre HA es verde (BULL), bar mostró pullback.
    - BPB short: mirror.
    - IPB long: precio se acerca al nivel desde arriba (soporte), HA cambia a BULL al cierre.
    - IPB short: mirror.
    """
    if bar_idx < min_pullback_bars + 1:
        return False

    bar = bars[bar_idx]
    level = hypo["level_price"]
    direction = hypo["direction"]
    setup = hypo["trigger_condition"]

    # HA color del cierre
    ha_color = bar.get("ha_color")
    ha_change = bar.get("ha_change", False)
    if ha_color is None:
        return False

    close = bar["c"]
    high = bar["h"]
    low = bar["l"]

    # Dist al nivel
    dist = abs(close - level)
    if dist > proximity_pts * 4:
        return False  # muy lejos

    if setup == "BPB":
        if direction == "long":
            # BPB long: necesitamos breakout UP previo, ahora retest
            # Buscar: alguna bar previa cerró por encima del nivel + min_pullback bars actuales cerca
            prior_breakout = False
            for k in range(max(0, bar_idx - 30), bar_idx):
                if bars[k]["c"] > level + 2:  # cerró claramente arriba
                    prior_breakout = True
                    break
            if not prior_breakout:
                return False
            # Pullback: últimas N bars con highs por encima de level
            recent = bars[bar_idx - min_pullback_bars : bar_idx + 1]
            if all(b["l"] >= level - proximity_pts for b in recent):
                # Bar actual: HA verde + close por encima del nivel
                if ha_color == "BULL" and ha_change and close >= level:
                    return True
        else:  # short
            prior_breakout = False
            for k in range(max(0, bar_idx - 30), bar_idx):
                if bars[k]["c"] < level - 2:
                    prior_breakout = True
                    break
            if not prior_breakout:
                return False
            recent = bars[bar_idx - min_pullback_bars : bar_idx + 1]
            if all(b["h"] <= level + proximity_pts for b in recent):
                if ha_color == "BEAR" and ha_change and close <= level:
                    return True

    elif setup == "IPB":
        if direction == "long":
            # IPB long: precio se acerca al nivel desde arriba (soporte), rebote
            # Bar actual hace mecha al nivel y cierra HA verde
            touched = low <= level + proximity_pts and low >= level - proximity_pts * 2
            if touched and ha_color == "BULL" and ha_change and close > level:
                # No debe haber roto el nivel previamente (para que sea rebote, no BPB)
                broke_level = any(b["c"] < level - 5 for b in bars[max(0, bar_idx-20):bar_idx])
                if not broke_level:
                    return True
        else:  # short, IPB en resistencia
            touched = high >= level - proximity_pts and high <= level + proximity_pts * 2
            if touched and ha_color == "BEAR" and ha_change and close < level:
                broke_level = any(b["c"] > level + 5 for b in bars[max(0, bar_idx-20):bar_idx])
                if not broke_level:
                    return True

    return False


# ----------------------------------------------------------------------------
# Outcome simulator — track hypo from trigger to exit
# ----------------------------------------------------------------------------

def simulate_outcome(
    hypo: dict,
    bars: list[dict],
    trigger_idx: int,
    eod_idx: int,
    target_pts: tuple[float, float] = (50.0, 100.0),
) -> dict:
    """Simula el outcome de un hypo desde trigger_idx hasta exit (target/stop/EOD).

    Stop dinámico = último HA pivot opuesto a la dirección.
    Targets = entry ± target_pts (T1, T2).
    """
    bar = bars[trigger_idx]
    entry = bar["c"]  # entry al cierre del bar de trigger
    direction = hypo["direction"]

    # Stop dinámico: último HA pivot en bars previos. CRÍTICO: validar que el
    # pivot esté del lado correcto del entry — long necesita pivot DEBAJO, short
    # necesita pivot ARRIBA. Si no, fallback a offset fijo.
    prior_bars = bars[max(0, trigger_idx - 30) : trigger_idx]
    pivot_side = "low" if direction == "long" else "high"
    pivot = find_last_ha_pivot(prior_bars, pivot_side)

    valid_pivot = False
    if pivot is not None:
        if direction == "long" and pivot < entry:
            valid_pivot = True
        elif direction == "short" and pivot > entry:
            valid_pivot = True

    if not valid_pivot:
        # Fallback: stop a 30 pts del entry
        stop = entry - 30 if direction == "long" else entry + 30
    else:
        # Stop 1 tick más allá del pivot
        stop = pivot - 1 if direction == "long" else pivot + 1

    risk = abs(entry - stop)
    if risk < 5:  # stop demasiado cerca, descartar
        return {"valid": False, "reason": "stop_too_tight", "risk_pts": risk}
    if risk > 200:  # stop demasiado lejos, no realista para un trade de RTH
        return {"valid": False, "reason": "stop_too_wide", "risk_pts": risk}

    if direction == "long":
        t1 = entry + target_pts[0]
        t2 = entry + target_pts[1]
    else:
        t1 = entry - target_pts[0]
        t2 = entry - target_pts[1]

    # Walk forward
    mfe = 0.0
    mae = 0.0
    exit_reason = None
    exit_price = None
    exit_idx = None

    for i in range(trigger_idx + 1, min(eod_idx + 1, len(bars))):
        b = bars[i]
        h, l = b["h"], b["l"]

        if direction == "long":
            mfe = max(mfe, h - entry)
            mae = min(mae, l - entry)
            # ¿stop?
            if l <= stop:
                exit_reason = "stopped"
                exit_price = stop
                exit_idx = i
                break
            # ¿T1? (asumimos tocar wick es hit)
            if h >= t2:
                exit_reason = "t2_hit"
                exit_price = t2
                exit_idx = i
                break
            elif h >= t1:
                exit_reason = "t1_hit"
                exit_price = t1
                exit_idx = i
                break
        else:  # short
            mfe = max(mfe, entry - l)
            mae = min(mae, entry - h)
            if h >= stop:
                exit_reason = "stopped"
                exit_price = stop
                exit_idx = i
                break
            if l <= t2:
                exit_reason = "t2_hit"
                exit_price = t2
                exit_idx = i
                break
            elif l <= t1:
                exit_reason = "t1_hit"
                exit_price = t1
                exit_idx = i
                break

    if exit_reason is None:
        # EOD close
        last_bar = bars[min(eod_idx, len(bars) - 1)]
        exit_reason = "eod"
        exit_price = last_bar["c"]
        exit_idx = min(eod_idx, len(bars) - 1)

    pnl = (exit_price - entry) if direction == "long" else (entry - exit_price)
    rr = pnl / risk if risk > 0 else 0

    return {
        "valid": True,
        "trigger_time": bar["t"],
        "trigger_idx": trigger_idx,
        "entry": entry,
        "stop": stop,
        "t1": t1,
        "t2": t2,
        "risk_pts": risk,
        "exit_time": bars[exit_idx]["t"],
        "exit_idx": exit_idx,
        "exit_reason": exit_reason,
        "exit_price": exit_price,
        "pnl_pts": pnl,
        "rr_realized": rr,
        "mfe_pts": mfe,
        "mae_pts": mae,
        "duration_bars": exit_idx - trigger_idx,
    }


# ----------------------------------------------------------------------------
# Walk-forward backtest
# ----------------------------------------------------------------------------

def _parse_dt(s: str) -> datetime:
    for fmt in ("%Y-%m-%d %H:%M:%S.%f", "%Y-%m-%d %H:%M:%S"):
        try:
            return datetime.strptime(s, fmt)
        except ValueError:
            continue
    return datetime.fromisoformat(s)


def compute_ha(bars: list[dict]) -> list[dict]:
    """Computa Heikin Ashi sobre lista de bars OHLCV. Modifica in-place añadiendo
    hao/hac/hah/hal/ha_color/ha_change.

    Fórmula:
        HA_close = (O+H+L+C)/4
        HA_open  = (HA_open[prev] + HA_close[prev]) / 2
        HA_high  = max(H, HA_open, HA_close)
        HA_low   = min(L, HA_open, HA_close)
    """
    prev_hao = None
    prev_hac = None
    prev_color = None
    for b in bars:
        o, h, l, c = b["o"], b["h"], b["l"], b["c"]
        hac = (o + h + l + c) / 4
        if prev_hao is None:
            hao = (o + c) / 2
        else:
            hao = (prev_hao + prev_hac) / 2
        hah = max(h, hao, hac)
        hal = min(l, hao, hac)
        color = "BULL" if hac >= hao else "BEAR"
        b["hao"] = hao
        b["hac"] = hac
        b["hah"] = hah
        b["hal"] = hal
        b["ha_color"] = color
        b["ha_change"] = (prev_color is not None and prev_color != color)
        prev_hao = hao
        prev_hac = hac
        prev_color = color
    return bars


def simulate_day(
    instrument: str,
    date: str,
    kind: str = "pre_pit",
    target_pts: tuple[float, float] = (50.0, 100.0),
) -> dict:
    """Ejecuta el backtest para UN día.

    1. Genera replay snapshot a las 09:25 ET (pre_pit)
    2. Enumera hipos candidatos
    3. Pide bars HA del día (desde pit-open hasta pit-close)
    4. Para cada hypo, walk forward bar-a-bar:
       - Detecta trigger
       - Si dispara, simula outcome
    5. Retorna lista de trades del día
    """
    # 1. Replay snapshot
    snap = replay.nadro_snapshot_replay(
        instrument=instrument, date=date, kind=kind, lookback_minutes=5,
    )
    if "error" in snap:
        return {"date": date, "error": snap["error"], "trades": []}

    # 2. Enumerate hipos
    hipos = enumerate_hipos(snap)
    if not hipos:
        return {"date": date, "trades": [], "n_hipos": 0}

    # 3. Pedir bars HA del día (pit-open a pit-close del día)
    master = _master_symbol(instrument)
    open_str, close_str = PIT_HOURS.get(master, ("09:30", "16:00"))
    pit_open = datetime.strptime(f"{date} {open_str}", "%Y-%m-%d %H:%M")
    pit_close = datetime.strptime(f"{date} {close_str}", "%Y-%m-%d %H:%M")

    # Pedimos suficientes bars para cubrir el día + buffer histórico para detectar pullbacks
    # 5m bars × 6.5h RTH = 78 bars por día. Más buffer histórico de 30 bars.
    # Pero get_bars_with_ha trae los ÚLTIMOS N bars. Así que debemos pedir suficientes
    # desde "ahora" para alcanzar el día solicitado.
    today = datetime.now()
    days_to_target = (today.date() - pit_close.date()).days
    if days_to_target < 0:
        return {"date": date, "error": "fecha futura", "trades": []}

    # Conservador: 300 5m bars/day ETH × N días + buffer
    n_bars_needed = (days_to_target + 2) * 300 + 100
    n_bars_needed = min(n_bars_needed, 50000)

    bars_data = observer.get_bars(instrument=instrument, tf="5m", n=n_bars_needed)
    bars = bars_data.get("bars", [])
    if not bars:
        return {"date": date, "error": "no bars", "trades": []}

    # Computar HA in-place
    compute_ha(bars)

    # Filter to RTH del día (pit_open a pit_close)
    day_bars = []
    for b in bars:
        bt = _parse_dt(b["t"])
        if pit_open <= bt <= pit_close:
            day_bars.append(b)

    if len(day_bars) < 20:
        return {"date": date, "trades": [], "n_hipos": len(hipos),
                "warning": f"insuficientes bars en RTH: {len(day_bars)}"}

    eod_idx = len(day_bars) - 1

    # 4. Para cada hypo, scan day_bars buscando trigger
    trades = []
    for hypo in hipos:
        for idx in range(5, len(day_bars)):  # min 5 bars para tener pullback
            if detect_trigger(hypo, day_bars, idx):
                outcome = simulate_outcome(hypo, day_bars, idx, eod_idx, target_pts)
                if outcome.get("valid"):
                    trades.append({
                        "hypo_id": hypo["id"],
                        "setup_type": hypo["setup_type"],
                        "direction": hypo["direction"],
                        "level_label": hypo["level_label"],
                        "level_price": hypo["level_price"],
                        "level_type": hypo["level_type"],
                        **outcome,
                    })
                    break  # 1 trade por hypo (primer trigger)

    return {
        "date": date,
        "n_hipos": len(hipos),
        "n_triggered": len(trades),
        "trades": trades,
    }


def nadro_walkforward(
    instrument: str,
    start_date: str,
    end_date: str,
    kind: str = "pre_pit",
    target_pts: tuple[float, float] = (50.0, 100.0),
) -> dict:
    """Walk-forward backtest sobre rango de fechas (skip weekends).

    Returns:
        - trades_by_day: dict[date] -> list of trades
        - all_trades: list flat
        - stats_by_setup: agregados por setup_type
    """
    start = datetime.strptime(start_date, "%Y-%m-%d")
    end = datetime.strptime(end_date, "%Y-%m-%d")
    dates = []
    d = start
    while d <= end:
        if d.weekday() < 5:
            dates.append(d.strftime("%Y-%m-%d"))
        d += timedelta(days=1)

    trades_by_day = {}
    all_trades = []
    errors = []

    for date in dates:
        result = simulate_day(instrument, date, kind=kind, target_pts=target_pts)
        if "error" in result:
            errors.append({"date": date, "error": result["error"]})
            continue
        trades_by_day[date] = result.get("trades", [])
        all_trades.extend(result.get("trades", []))

    # Stats por setup_type
    stats_by_setup = defaultdict(lambda: {
        "n_triggers": 0,
        "n_t1": 0,
        "n_t2": 0,
        "n_stop": 0,
        "n_eod": 0,
        "sum_rr": 0.0,
        "sum_pnl": 0.0,
        "wins": 0,
        "losses": 0,
        "mfe_avg": 0.0,
        "mae_avg": 0.0,
    })

    for t in all_trades:
        st = stats_by_setup[t["setup_type"]]
        st["n_triggers"] += 1
        if t["exit_reason"] == "t1_hit":
            st["n_t1"] += 1
        elif t["exit_reason"] == "t2_hit":
            st["n_t2"] += 1
        elif t["exit_reason"] == "stopped":
            st["n_stop"] += 1
        elif t["exit_reason"] == "eod":
            st["n_eod"] += 1
        st["sum_rr"] += t["rr_realized"]
        st["sum_pnl"] += t["pnl_pts"]
        if t["pnl_pts"] > 0:
            st["wins"] += 1
        else:
            st["losses"] += 1
        st["mfe_avg"] += t["mfe_pts"]
        st["mae_avg"] += t["mae_pts"]

    # Promediar
    for setup, s in stats_by_setup.items():
        n = s["n_triggers"]
        if n > 0:
            s["avg_rr"] = round(s["sum_rr"] / n, 2)
            s["avg_pnl_pts"] = round(s["sum_pnl"] / n, 2)
            s["mfe_avg"] = round(s["mfe_avg"] / n, 2)
            s["mae_avg"] = round(s["mae_avg"] / n, 2)
            s["hit_rate"] = round(s["wins"] / n * 100, 1)

    return {
        "instrument": instrument,
        "start_date": start_date,
        "end_date": end_date,
        "n_days": len(dates),
        "n_days_with_trades": len(trades_by_day),
        "n_total_trades": len(all_trades),
        "n_errors": len(errors),
        "errors": errors,
        "stats_by_setup": dict(stats_by_setup),
        "trades_by_day": trades_by_day,
    }
