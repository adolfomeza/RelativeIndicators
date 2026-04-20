"""MVP backtest NADRO — BPB (Breakout Pullback) en últimos N días.

Replica la lógica de VWAP + bandas SD desde bars crudos para reconstruir
los niveles NADRO en CADA barra histórica (no solo el estado actual).

Detecta BPB:
- Bar N-1 cierra dentro del DVA
- Bar N cierra fuera del DVA (rompe DVAH o DVAL) con volumen > 1.5x SMA(20)
- Bar N+1 a N+5 retrocede y toca (±1pt) el nivel roto → entry al close del retest
- Stop: detrás del nivel contrario (DVAL si bullish, DVAH si bearish) o 5pts fijos
- Target: banda SD3 o +2R
- Time stop: 20 bars

Output:
- Tabla de trades por día
- Cumulative PnL curve
- Stats: win rate, profit factor, expectancy, max DD
"""
from __future__ import annotations

import math
from collections import defaultdict
from datetime import datetime, time, timedelta
from typing import Any

from . import observer


# -----------------------------------------------------------------------------
# Utilities
# -----------------------------------------------------------------------------


def _parse_dt(s: str) -> datetime:
    for fmt in ("%Y-%m-%d %H:%M:%S.%f", "%Y-%m-%d %H:%M:%S"):
        try:
            return datetime.strptime(s, fmt)
        except ValueError:
            continue
    return datetime.fromisoformat(s)


def _parse_hhmm(hhmm: str) -> time:
    h, m = hhmm.split(":")
    return time(int(h), int(m))


def _effective_session_date(dt: datetime, reset_hour: int = 18) -> datetime:
    """Fecha de la sesión ETH — si la hora es >= reset_hour, pertenece al día siguiente."""
    if dt.hour >= reset_hour:
        return (dt + timedelta(days=1)).date()
    return dt.date()


# -----------------------------------------------------------------------------
# Cálculo de niveles NADRO históricos
# -----------------------------------------------------------------------------


def _compute_prior_day_dvas(
    bars: list[dict],
    rth_start: time = time(9, 30),
    rth_end: time = time(16, 0),
) -> dict:
    """Calcula PVA + PDH/PDL del RTH (9:30-16:00) por CALENDAR date.

    NADRO clásico usa RTH Prior Day High/Low — no ETH completo. Así se alinea
    con los niveles que traders institucionales observan.
    """
    by_date: dict = {}
    for bar in bars:
        t = bar["dt"].time()
        if t < rth_start or t >= rth_end:
            continue
        date_key = bar["dt"].date()
        acc = by_date.setdefault(date_key, {
            "cum_pv": 0.0, "cum_v": 0.0, "cum_p2v": 0.0,
            "high": -float("inf"), "low": float("inf"),
            "bar_count": 0,
        })
        tp = (bar["h"] + bar["l"] + bar["c"]) / 3.0
        acc["cum_pv"] += tp * bar["v"]
        acc["cum_v"] += bar["v"]
        acc["cum_p2v"] += tp * tp * bar["v"]
        acc["bar_count"] += 1
        if bar["h"] > acc["high"]:
            acc["high"] = bar["h"]
        if bar["l"] < acc["low"]:
            acc["low"] = bar["l"]

    result = {}
    for date_key, acc in by_date.items():
        if acc["cum_v"] <= 0 or acc["bar_count"] < 10:
            continue  # día incompleto (holiday / early close)
        vwap = acc["cum_pv"] / acc["cum_v"]
        var = (acc["cum_p2v"] / acc["cum_v"]) - (vwap * vwap)
        std = math.sqrt(max(0.0, var))
        result[date_key] = {
            "pvwap": round(vwap, 2),
            "pvah": round(vwap + std, 2),
            "pval": round(vwap - std, 2),
            "pvah_sd3": round(vwap + 3 * std, 2),
            "pval_sd3": round(vwap - 3 * std, 2),
            "day_high": round(acc["high"], 2),  # PDH RTH
            "day_low": round(acc["low"], 2),    # PDL RTH
            "bar_count": acc["bar_count"],
        }
    return result


def _compute_session_vwaps(
    bars: list[dict],
    session_reset_hour: int = 18,
) -> None:
    """Inyecta campos vwap / dvah_sd1 / dval_sd1 / dvah_sd3 / dval_sd3 en cada bar.

    Reset al inicio de cada sesión ETH (18:00 ET del día anterior).
    Modifica ``bars`` in-place.
    """
    cum_pv = 0.0      # sum(typical_price * volume)
    cum_v = 0.0       # sum(volume)
    cum_p2v = 0.0     # sum(typical_price² * volume) — para std dev
    last_session = None

    for bar in bars:
        dt = bar["dt"]
        session = _effective_session_date(dt, session_reset_hour)
        if session != last_session:
            cum_pv = cum_v = cum_p2v = 0.0
            last_session = session

        tp = (bar["h"] + bar["l"] + bar["c"]) / 3.0
        vol = bar["v"]
        cum_pv += tp * vol
        cum_v += vol
        cum_p2v += tp * tp * vol

        if cum_v > 0:
            vwap = cum_pv / cum_v
            variance = (cum_p2v / cum_v) - (vwap * vwap)
            std = math.sqrt(max(0.0, variance))
        else:
            vwap = tp
            std = 0.0

        bar["vwap"] = vwap
        bar["dvah_sd1"] = vwap + std
        bar["dval_sd1"] = vwap - std
        bar["dvah_sd2"] = vwap + 2 * std
        bar["dval_sd2"] = vwap - 2 * std
        bar["dvah_sd3"] = vwap + 3 * std
        bar["dval_sd3"] = vwap - 3 * std
        bar["session_date"] = session


# -----------------------------------------------------------------------------
# Detector BPB
# -----------------------------------------------------------------------------


def _detect_bpb_setups(
    bars: list[dict],
    prior_dvas: dict,
    window_start: time,
    window_end: time,
    vol_multiplier: float = 1.5,
    vol_lookback: int = 20,
    min_prior_bars: int = 50,
    max_prior_age_days: int = 3,
) -> list[dict]:
    """Detecta BPB NADRO contra el Prior Day High/Low + PVA (día hábil previo).

    Busca el último día ETH con data significativa (≥ ``min_prior_bars``) como
    referencia estática. Así salta weekends y días incompletos automáticamente.

    Criterio BPB bullish:
    - Close del bar > Prior Day High (PDH) + primer cruce del día
    - Volumen del bar > vol_multiplier × avg_vol

    Bearish simétrico con PDL.
    """
    from datetime import timedelta as _td

    # prior_dvas ya está keyed por calendar date; extraemos las fechas válidas
    valid_dates = sorted(prior_dvas.keys())

    def _prior_valid_date(cal_date):
        priors = [d for d in valid_dates if d < cal_date]
        return priors[-1] if priors else None

    setups = []
    seen_bullish_per_day: set = set()
    seen_bearish_per_day: set = set()

    for i in range(vol_lookback, len(bars) - 1):
        bar = bars[i]
        prev = bars[i - 1]

        t = bar["dt"].time()
        if t < window_start or t > window_end:
            continue

        # Usar calendar date (no session_date) para coordinación con RTH
        cal_date = bar["dt"].date()
        prev_date = _prior_valid_date(cal_date)
        prior = prior_dvas.get(prev_date) if prev_date else None
        if not prior:
            continue

        # Filtro freshness Ley 8: prior day demasiado viejo → energía disipada
        from datetime import timedelta as _td
        if (cal_date - prev_date) > _td(days=max_prior_age_days):
            continue

        # Niveles de referencia: PDH/PDL + PVAH/PVAL como refuerzo
        pdh = prior["day_high"]
        pdl = prior["day_low"]
        pvah = prior["pvah"]
        pval = prior["pval"]

        recent_vols = [bars[j]["v"] for j in range(i - vol_lookback, i)]
        avg_vol = sum(recent_vols) / len(recent_vols) if recent_vols else 0
        vol_ok = bar["v"] > avg_vol * vol_multiplier

        # BPB bullish: primer cruce del PDH del día hábil anterior
        if (prev["c"] <= pdh and bar["c"] > pdh and vol_ok
                and cal_date not in seen_bullish_per_day):
            setups.append({
                "idx": i,
                "type": "BPB_BULLISH",
                "break_level": pdh,
                "break_bar_time": bar["t"],
                "break_bar_close": bar["c"],
                "break_bar_volume": bar["v"],
                "avg_volume": round(avg_vol, 0),
                "pdh": pdh,
                "pdl": pdl,
                "pvah": pvah,
                "pval": pval,
                "pvah_sd3": prior["pvah_sd3"],
                "session_date": str(cal_date),
                "prior_session": str(prev_date),
            })
            seen_bullish_per_day.add(cal_date)

        elif (prev["c"] >= pdl and bar["c"] < pdl and vol_ok
              and cal_date not in seen_bearish_per_day):
            setups.append({
                "idx": i,
                "type": "BPB_BEARISH",
                "break_level": pdl,
                "break_bar_time": bar["t"],
                "break_bar_close": bar["c"],
                "break_bar_volume": bar["v"],
                "avg_volume": round(avg_vol, 0),
                "pdh": pdh,
                "pdl": pdl,
                "pvah": pvah,
                "pval": pval,
                "pval_sd3": prior["pval_sd3"],
                "session_date": str(cal_date),
                "prior_session": str(prev_date),
            })
            seen_bearish_per_day.add(cal_date)

    return setups


def _detect_rpb_setups(
    bars: list[dict],
    prior_dvas: dict,
    window_start: time,
    window_end: time,
    acceptance_bars: int = 2,
    vol_lookback: int = 20,
    require_confirmation: bool = True,
    max_prior_age_days: int = 3,
) -> list[dict]:
    """RPB (Return Pullback) — NADRO Ley 2.

    "Aceptación dentro de un área de balance → Intento de cruce (Traversal)".

    Criterio:
    - El precio abre/está FUERA del PVA al inicio de la ventana
    - Retorna al PVA y lo acepta (≥ ``acceptance_bars`` bars consecutivas dentro)
    - Entry al cierre de la barra de aceptación (apunta a Traversal hacia el borde opuesto)
    """
    setups = []
    seen_per_day: set = set()
    valid_dates = sorted(prior_dvas.keys())

    def _prior_valid_date(cal_date):
        priors = [d for d in valid_dates if d < cal_date]
        return priors[-1] if priors else None

    for i in range(vol_lookback, len(bars) - 2):  # -2 porque necesitamos confirmation bar
        bar = bars[i]
        t = bar["dt"].time()
        if t < window_start or t > window_end:
            continue

        cal_date = bar["dt"].date()
        if cal_date in seen_per_day:
            continue
        prev_date = _prior_valid_date(cal_date)
        prior = prior_dvas.get(prev_date) if prev_date else None
        if not prior:
            continue

        # Filtro freshness (Ley 8): PDH/PDL de hace > max_prior_age_days = energía disipada
        from datetime import timedelta as _td
        if (cal_date - prev_date) > _td(days=max_prior_age_days):
            continue

        pvah, pval = prior["pvah"], prior["pval"]

        # Verificar que las acceptance_bars consecutivas están DENTRO del PVA
        # (barras i, i-1) — la bar anterior debió estar fuera
        if i - acceptance_bars < 0:
            continue
        window_in_pva = all(pval <= bars[j]["c"] <= pvah
                            for j in range(i - acceptance_bars + 1, i + 1))
        prev_outside = (bars[i - acceptance_bars]["c"] > pvah or
                        bars[i - acceptance_bars]["c"] < pval)
        if not (window_in_pva and prev_outside):
            continue

        # Determinar dirección del Traversal: hacia el borde OPUESTO al que entró
        came_from_above = bars[i - acceptance_bars]["c"] > pvah
        direction = "short" if came_from_above else "long"
        break_level = pvah if came_from_above else pval
        target_level = pval if came_from_above else pvah

        # Filtro NADRO: confirmation bar (bar i+1) debe cerrar en dirección del Traversal
        if require_confirmation:
            conf = bars[i + 1]
            if direction == "long" and conf["c"] <= bar["c"]:
                continue  # bar siguiente no avanza al alza
            if direction == "short" and conf["c"] >= bar["c"]:
                continue  # bar siguiente no avanza a la baja
            # Además, la confirmation bar debe seguir dentro del PVA (sino rompe, no es Traversal)
            if conf["c"] > pvah or conf["c"] < pval:
                continue
            # Shift entry al close de confirmation bar
            entry_idx = i + 1
        else:
            entry_idx = i

        setups.append({
            "idx": entry_idx,
            "type": "RPB_" + ("BEARISH" if direction == "short" else "BULLISH"),
            "break_level": break_level,
            "target_level": target_level,
            "break_bar_time": bars[entry_idx]["t"],
            "break_bar_close": bars[entry_idx]["c"],
            "pvah": pvah,
            "pval": pval,
            "session_date": str(cal_date),
            "prior_session": str(prev_date),
        })
        seen_per_day.add(cal_date)

    return setups


def _detect_ipb_setups(
    bars: list[dict],
    window_start: time,
    window_end: time,
    acceptance_pts_mult: float = 1.0,
    imbalance_lookback: int = 12,  # 3 horas en 15m — intra-sesión
    vol_lookback: int = 20,
    tolerance_pts: float = 5.0,
    require_confirmation: bool = True,
) -> list[dict]:
    """IPB (Imbalance Pullback) — NADRO setup en mercado tendencial.

    Criterio (con filtros NADRO):
    - Precio está en imbalance (fuera del DVAH/DVAL con distancia)
    - Pullback retrocede hacia el DVAH/DVAL (tolerancia ±tolerance_pts)
    - Close del setup bar vuelve a aceptar en dirección del imbalance
    - Confirmation: bar siguiente cierra continuando (no rompe hacia VWAP central)
    """
    setups = []
    seen_per_day: set = set()

    for i in range(vol_lookback, len(bars) - 2):
        bar = bars[i]
        t = bar["dt"].time()
        if t < window_start or t > window_end:
            continue
        cal_date = bar["dt"].date()
        if cal_date in seen_per_day:
            continue

        vwap = bar["vwap"]
        dvah = bar["dvah_sd1"]
        dval = bar["dval_sd1"]
        atr = _compute_atr(bars, i, period=14)

        # IPB bullish NADRO puro — pullback a DVAH (±1 SD) en IMBALANCE SOSTENIDO
        # Guía NADRO 4.0: "Prioridad son SIEMPRE los Extremos (DVAH/DVAL)".
        # Imbalance up confirmado: precio arriba del DVAH por lookback + 70% de closes > DVAH
        recent_bars = bars[max(0, i - imbalance_lookback):i]
        closes_above_dvah = sum(1 for b in recent_bars if b["c"] > b["dvah_sd1"])
        is_imbalance_up = (
            len(recent_bars) >= imbalance_lookback * 0.7 and
            closes_above_dvah >= len(recent_bars) * 0.7
        )
        if is_imbalance_up:
            # Pullback a DVAH: swing low local toca o respeta DVAH ±tolerance
            recent_lows_last4 = [bars[j]["l"] for j in range(max(0, i - 3), i + 1)]
            is_swing_low = bar["l"] == min(recent_lows_last4)
            touches_or_respects_dvah = (
                bar["l"] <= dvah + tolerance_pts and   # llegó cerca/al DVAH
                bar["l"] >= dvah - tolerance_pts       # no lo perforó mucho
            )
            is_bullish_bar = bar["c"] > bar["o"]
            close_above_dvah = bar["c"] > dvah          # respetó el nivel como soporte
            if is_swing_low and touches_or_respects_dvah and is_bullish_bar and close_above_dvah:
                # Confirmation: bar siguiente debe cerrar > bar actual (continuation)
                if require_confirmation:
                    conf = bars[i + 1]
                    if conf["c"] <= bar["c"] or conf["c"] < dvah:
                        continue
                    entry_idx = i + 1
                else:
                    entry_idx = i
                setups.append({
                    "idx": entry_idx,
                    "type": "IPB_BULLISH",
                    "break_level": round(dvah, 2),
                    "pvah": round(dvah, 2),
                    "pvah_sd3": round(bar["dvah_sd3"], 2),
                    "pvwap": round(vwap, 2),
                    "break_bar_time": bars[entry_idx]["t"],
                    "break_bar_close": bars[entry_idx]["c"],
                    "session_date": str(cal_date),
                })
                seen_per_day.add(cal_date)
                continue

        # IPB bearish NADRO puro — pullback a DVAL (±1 SD) en imbalance DOWN sostenido
        closes_below_dval = sum(1 for b in recent_bars if b["c"] < b["dval_sd1"])
        is_imbalance_down = (
            len(recent_bars) >= imbalance_lookback * 0.7 and
            closes_below_dval >= len(recent_bars) * 0.7
        )
        if is_imbalance_down:
            recent_highs_last4 = [bars[j]["h"] for j in range(max(0, i - 3), i + 1)]
            is_swing_high = bar["h"] == max(recent_highs_last4)
            touches_or_respects_dval = (
                bar["h"] >= dval - tolerance_pts and
                bar["h"] <= dval + tolerance_pts
            )
            is_bearish_bar = bar["c"] < bar["o"]
            close_below_dval = bar["c"] < dval
            if is_swing_high and touches_or_respects_dval and is_bearish_bar and close_below_dval:
                if require_confirmation:
                    conf = bars[i + 1]
                    if conf["c"] >= bar["c"] or conf["c"] > dval:
                        continue
                    entry_idx = i + 1
                else:
                    entry_idx = i
                setups.append({
                    "idx": entry_idx,
                    "type": "IPB_BEARISH",
                    "break_level": round(dval, 2),
                    "pval": round(dval, 2),
                    "pval_sd3": round(bar["dval_sd3"], 2),
                    "pvwap": round(vwap, 2),
                    "break_bar_time": bars[entry_idx]["t"],
                    "break_bar_close": bars[entry_idx]["c"],
                    "session_date": str(cal_date),
                })
                seen_per_day.add(cal_date)

    return setups


def _detect_ef_setups(
    bars: list[dict],
    window_start: time,
    window_end: time,
    vol_lookback: int = 20,
    require_confirmation: bool = True,
) -> list[dict]:
    """EF (Extreme Fade) — NADRO rotacional: rechazo +2SD / -2SD hacia VWAP.

    Filtros NADRO agregados:
    - Regimen check: el imbalance NO puede ser a favor del extremo (Guía 03:
      "Si se establece desequilibrio EN SU CONTRA... NO DISPARE")
    - Confirmation: bar siguiente debe cerrar en dirección del fade + bajo/arriba VWAP
    - Mecha mínima: (high-close) > 50% del rango + (high-close) > ATR × 0.5
    """
    setups = []
    seen_per_day: set = set()

    for i in range(vol_lookback, len(bars) - 2):
        bar = bars[i]
        t = bar["dt"].time()
        if t < window_start or t > window_end:
            continue
        cal_date = bar["dt"].date()
        if cal_date in seen_per_day:
            continue

        vwap = bar["vwap"]
        dvah_sd2 = bar["dvah_sd2"]
        dval_sd2 = bar["dval_sd2"]
        atr = _compute_atr(bars, i, period=14)

        bar_range = bar["h"] - bar["l"]
        if bar_range < max(1.0, atr * 0.5):
            continue

        # Regime filter: si el precio está sostenidamente en imbalance up (≥ 8/10 bars con close > dvah_sd2), NO fade hacia abajo
        recent_closes = [bars[j]["c"] for j in range(max(0, i - 10), i)]

        # EF short: high toca +2SD con rechazo fuerte
        if bar["h"] >= dvah_sd2 and (bar["h"] - bar["c"]) > bar_range * 0.5 and (bar["h"] - bar["c"]) > atr * 0.5:
            # Régimen check: si mayoría reciente está arriba del +2SD, es imbalance fuerte → no fade
            sustained_above = sum(1 for c in recent_closes if c > dvah_sd2) >= 8
            if sustained_above:
                continue
            if require_confirmation:
                conf = bars[i + 1]
                # Bar siguiente debe cerrar por debajo del bar actual + bajo el high
                if conf["c"] >= bar["c"] or conf["c"] >= bar["h"]:
                    continue
                entry_idx = i + 1
            else:
                entry_idx = i
            setups.append({
                "idx": entry_idx,
                "type": "EF_BEARISH",
                "break_level": round(dvah_sd2, 2),
                "pval": round(vwap, 2),
                "pval_sd3": round(vwap, 2),
                "pvwap": round(vwap, 2),
                "break_bar_time": bars[entry_idx]["t"],
                "break_bar_close": bars[entry_idx]["c"],
                "session_date": str(cal_date),
            })
            seen_per_day.add(cal_date)
            continue

        # EF long
        if bar["l"] <= dval_sd2 and (bar["c"] - bar["l"]) > bar_range * 0.5 and (bar["c"] - bar["l"]) > atr * 0.5:
            sustained_below = sum(1 for c in recent_closes if c < dval_sd2) >= 8
            if sustained_below:
                continue
            if require_confirmation:
                conf = bars[i + 1]
                if conf["c"] <= bar["c"] or conf["c"] <= bar["l"]:
                    continue
                entry_idx = i + 1
            else:
                entry_idx = i
            setups.append({
                "idx": entry_idx,
                "type": "EF_BULLISH",
                "break_level": round(dval_sd2, 2),
                "pvah": round(vwap, 2),
                "pvah_sd3": round(vwap, 2),
                "pvwap": round(vwap, 2),
                "break_bar_time": bars[entry_idx]["t"],
                "break_bar_close": bars[entry_idx]["c"],
                "session_date": str(cal_date),
            })
            seen_per_day.add(cal_date)

    return setups


# -----------------------------------------------------------------------------
# Simulador de trade
# -----------------------------------------------------------------------------


def _compute_atr(bars: list[dict], idx: int, period: int = 14) -> float:
    """Average True Range simple (promedio de high-low de las últimas N barras)."""
    start = max(0, idx - period)
    ranges = [b["h"] - b["l"] for b in bars[start:idx] if b["h"] > b["l"]]
    return sum(ranges) / len(ranges) if ranges else 1.0


def _simulate_trade(
    bars: list[dict],
    setup: dict,
    retest_lookahead: int = 5,
    retest_tolerance: float = 2.0,
    stop_mode: str = "dynamic",
    stop_pts: float = 5.0,
    stop_atr_mult: float = 1.5,
    max_hold_bars: int = 20,
) -> dict | None:
    """Simulador unificado para BPB / RPB / IPB / EF.

    - BPB: entry al retest del nivel roto
    - RPB: entry al close del setup (ya confirmado aceptación en PVA)
    - IPB: entry al close del bounce
    - EF: entry al close del rechazo (bar ya se alejó del extremo)
    """
    idx = setup["idx"]
    break_level = setup["break_level"]
    setup_type = setup["type"]
    direction = "long" if "BULLISH" in setup_type else "short"

    # Entry logic depende del setup type
    entry_idx = None
    entry_price = None

    if setup_type.startswith("BPB"):
        # Retest del nivel roto
        for i in range(idx + 1, min(idx + 1 + retest_lookahead, len(bars))):
            bar = bars[i]
            if direction == "long":
                if bar["l"] <= break_level + retest_tolerance and bar["c"] > break_level:
                    entry_idx = i
                    entry_price = bar["c"]
                    break
            else:
                if bar["h"] >= break_level - retest_tolerance and bar["c"] < break_level:
                    entry_idx = i
                    entry_price = bar["c"]
                    break
        if entry_idx is None:
            return None
    else:
        # RPB / IPB / EF: entry al close de la bar del setup (ya confirmada)
        entry_idx = idx
        entry_price = bars[idx]["c"]

    # Stop dinámico basado en ATR real del tf actual (sin techo artificial)
    if stop_mode == "dynamic":
        atr = _compute_atr(bars, entry_idx, period=14)
        effective_stop_pts = max(4.0, atr * stop_atr_mult)  # solo piso de 4pts (anti-ruido)
    else:
        effective_stop_pts = stop_pts

    # Target: RR 1:2 por default, pero RPB usa el PVA opuesto como target (Traversal)
    rr_target_pts = effective_stop_pts * 2.0

    if setup_type.startswith("RPB"):
        # Target Traversal = borde opuesto del PVA
        target = setup.get("target_level") or (
            entry_price + rr_target_pts if direction == "long"
            else entry_price - rr_target_pts
        )
    else:
        sd3_level = setup.get("pvah_sd3" if direction == "long" else "pval_sd3")
        if direction == "long":
            target_rr = entry_price + rr_target_pts
            target = min(target_rr, sd3_level) if sd3_level and sd3_level > entry_price else target_rr
            if sd3_level and sd3_level < entry_price + effective_stop_pts:
                target = target_rr
        else:
            target_rr = entry_price - rr_target_pts
            target = max(target_rr, sd3_level) if sd3_level and sd3_level < entry_price else target_rr
            if sd3_level and sd3_level > entry_price - effective_stop_pts:
                target = target_rr

    if direction == "long":
        stop = entry_price - effective_stop_pts
    else:
        stop = entry_price + effective_stop_pts

    # Walk forward
    exit_idx = None
    exit_price = None
    exit_reason = "time_out"
    for j in range(entry_idx + 1, min(entry_idx + 1 + max_hold_bars, len(bars))):
        bar = bars[j]
        if direction == "long":
            if bar["l"] <= stop:
                exit_idx = j
                exit_price = stop
                exit_reason = "stop"
                break
            if bar["h"] >= target:
                exit_idx = j
                exit_price = target
                exit_reason = "target"
                break
        else:
            if bar["h"] >= stop:
                exit_idx = j
                exit_price = stop
                exit_reason = "stop"
                break
            if bar["l"] <= target:
                exit_idx = j
                exit_price = target
                exit_reason = "target"
                break

    if exit_idx is None:
        # Time out: salir al close de la última bar del hold
        last_j = min(entry_idx + max_hold_bars, len(bars) - 1)
        exit_idx = last_j
        exit_price = bars[last_j]["c"]

    # Compute PnL
    if direction == "long":
        pnl_pts = exit_price - entry_price
    else:
        pnl_pts = entry_price - exit_price

    return {
        "setup_type": setup["type"],
        "session_date": setup["session_date"],
        "break_bar_time": setup["break_bar_time"],
        "break_level": break_level,
        "entry_bar_time": bars[entry_idx]["t"],
        "entry_price": round(entry_price, 2),
        "stop": round(stop, 2),
        "target": round(target, 2),
        "exit_bar_time": bars[exit_idx]["t"],
        "exit_price": round(exit_price, 2),
        "exit_reason": exit_reason,
        "pnl_pts": round(pnl_pts, 2),
        "bars_held": exit_idx - entry_idx,
        "direction": direction,
        "stop_pts_used": round(effective_stop_pts, 2),
    }


# -----------------------------------------------------------------------------
# Stats + PnL curve
# -----------------------------------------------------------------------------


def _compute_stats(trades: list[dict], point_value: float = 5.0) -> dict:
    if not trades:
        return {"n_trades": 0, "note": "sin trades"}

    wins = [t for t in trades if t["pnl_pts"] > 0]
    losses = [t for t in trades if t["pnl_pts"] <= 0]

    total_pnl_pts = sum(t["pnl_pts"] for t in trades)
    total_pnl_usd = total_pnl_pts * point_value

    sum_wins = sum(t["pnl_pts"] for t in wins)
    sum_losses = sum(t["pnl_pts"] for t in losses)

    avg_win = sum_wins / len(wins) if wins else 0
    avg_loss = sum_losses / len(losses) if losses else 0

    profit_factor = sum_wins / abs(sum_losses) if losses and sum_losses != 0 else float("inf")
    win_rate = len(wins) / len(trades) if trades else 0
    expectancy_pts = (win_rate * avg_win) + ((1 - win_rate) * avg_loss)

    # PnL curve + Max Drawdown
    cumulative = 0.0
    peak = 0.0
    max_dd = 0.0
    curve = []
    for t in sorted(trades, key=lambda x: x["entry_bar_time"]):
        cumulative += t["pnl_pts"]
        peak = max(peak, cumulative)
        dd = peak - cumulative
        if dd > max_dd:
            max_dd = dd
        curve.append({
            "time": t["entry_bar_time"],
            "cumulative_pnl_pts": round(cumulative, 2),
            "cumulative_pnl_usd": round(cumulative * point_value, 2),
        })

    return {
        "n_trades": len(trades),
        "wins": len(wins),
        "losses": len(losses),
        "win_rate": round(win_rate, 3),
        "total_pnl_pts": round(total_pnl_pts, 2),
        "total_pnl_usd": round(total_pnl_usd, 2),
        "avg_win_pts": round(avg_win, 2),
        "avg_loss_pts": round(avg_loss, 2),
        "largest_win_pts": round(max((t["pnl_pts"] for t in wins), default=0), 2),
        "largest_loss_pts": round(min((t["pnl_pts"] for t in losses), default=0), 2),
        "profit_factor": round(profit_factor, 2) if profit_factor != float("inf") else "inf",
        "expectancy_pts": round(expectancy_pts, 2),
        "expectancy_usd": round(expectancy_pts * point_value, 2),
        "max_drawdown_pts": round(max_dd, 2),
        "max_drawdown_usd": round(max_dd * point_value, 2),
        "pnl_curve": curve,
    }


# -----------------------------------------------------------------------------
# Main entry
# -----------------------------------------------------------------------------


def nadro_backtest(
    instrument: str,
    days_back: int = 7,
    tf: str = "5m",
    window_start: str = "09:30",
    window_end: str = "12:00",
    stop_mode: str = "dynamic",
    stop_pts: float = 5.0,
    stop_atr_mult: float = 1.5,
    vol_multiplier: float = 1.3,
    retest_tolerance: float = 2.0,
    max_hold_bars: int = 20,
    point_value: float = 5.0,
) -> dict:
    """MVP: backtest BPB NADRO en los últimos ``days_back`` días.

    Args:
        instrument: "MES 06-26"
        days_back: cuántos días hacia atrás analizar
        tf: timeframe de las bars (default 5m para balance entre precisión y cobertura)
        window_start/end: rango horario donde detectar setups (ET), default RTH open hora
        stop_pts: stop fijo en puntos
        max_hold_bars: salida forzada si no hit target/stop
        point_value: valor del punto en USD (MES = $5)
    """
    # 1. Fetch bars — con tf=5m y n=2000 cubrimos ~7 días de trading
    # Calculamos n según días deseados (asumiendo ~276 bars/day en 5m con 23h session)
    bars_per_day = 23 * 60 // int(tf.rstrip("m")) if tf.endswith("m") else 288
    target_n = min(2000, (days_back + 2) * bars_per_day)

    data = observer.get_bars(instrument, tf=tf, n=target_n)
    if "error" in data or not data.get("bars"):
        return {
            "error": data.get("error", "sin bars"),
            "addon_reachable": data.get("addon_reachable", False),
        }

    bars = data["bars"]
    # Parsear timestamps
    for b in bars:
        b["dt"] = _parse_dt(b["t"])

    # Filtrar a últimos days_back días
    if bars:
        cutoff = bars[-1]["dt"].date() - timedelta(days=days_back)
        bars = [b for b in bars if b["dt"].date() >= cutoff]

    if len(bars) < 50:
        return {"error": f"bars insuficientes ({len(bars)} recibidos)"}

    # 2. Calcular niveles NADRO históricos
    _compute_session_vwaps(bars, session_reset_hour=18)

    # 2b. Computar PVA (Prior Value Area) por día
    prior_dvas = _compute_prior_day_dvas(bars)

    # 3. Detectar los 4 setups NADRO
    w_start = _parse_hhmm(window_start)
    w_end = _parse_hhmm(window_end)

    setups_bpb = _detect_bpb_setups(bars, prior_dvas, w_start, w_end, vol_multiplier=vol_multiplier)
    setups_rpb = _detect_rpb_setups(bars, prior_dvas, w_start, w_end)
    setups_ipb = _detect_ipb_setups(bars, w_start, w_end)
    setups_ef = _detect_ef_setups(bars, w_start, w_end)
    setups = setups_bpb + setups_rpb + setups_ipb + setups_ef

    # 4. Simular cada setup
    trades = []
    for setup in setups:
        trade = _simulate_trade(
            bars, setup,
            retest_tolerance=retest_tolerance,
            stop_mode=stop_mode,
            stop_pts=stop_pts,
            stop_atr_mult=stop_atr_mult,
            max_hold_bars=max_hold_bars,
        )
        if trade:
            trades.append(trade)

    # 5. Stats globales
    stats = _compute_stats(trades, point_value=point_value)

    # 5b. Stats por tipo de setup
    by_type_trades = defaultdict(list)
    for t in trades:
        key = t["setup_type"].split("_")[0]  # BPB / RPB / IPB / EF
        by_type_trades[key].append(t)
    stats_by_setup = {
        setup_type: _compute_stats(ts, point_value=point_value)
        for setup_type, ts in by_type_trades.items()
    }

    # 6. Agregado por día
    by_day = defaultdict(lambda: {"trades": 0, "pnl_pts": 0.0})
    for t in trades:
        by_day[t["session_date"]]["trades"] += 1
        by_day[t["session_date"]]["pnl_pts"] += t["pnl_pts"]
    daily = [
        {"date": d, **v, "pnl_usd": round(v["pnl_pts"] * point_value, 2)}
        for d, v in sorted(by_day.items())
    ]
    for d in daily:
        d["pnl_pts"] = round(d["pnl_pts"], 2)

    return {
        "instrument": instrument,
        "config": {
            "days_back": days_back,
            "tf": tf,
            "window_start": window_start,
            "window_end": window_end,
            "stop_mode": stop_mode,
            "stop_pts_fixed": stop_pts,
            "stop_atr_mult": stop_atr_mult,
            "vol_multiplier": vol_multiplier,
            "retest_tolerance": retest_tolerance,
            "max_hold_bars": max_hold_bars,
            "point_value": point_value,
        },
        "bars_analyzed": len(bars),
        "first_bar": bars[0]["t"] if bars else None,
        "last_bar": bars[-1]["t"] if bars else None,
        "setups_detected": {
            "total": len(setups),
            "BPB": len(setups_bpb),
            "RPB": len(setups_rpb),
            "IPB": len(setups_ipb),
            "EF": len(setups_ef),
        },
        "setups_with_entry": len(trades),
        "setups_no_retest": len(setups) - len(trades),
        "stats": stats,
        "stats_by_setup_type": stats_by_setup,
        "daily_breakdown": daily,
        "trades": trades,
    }
