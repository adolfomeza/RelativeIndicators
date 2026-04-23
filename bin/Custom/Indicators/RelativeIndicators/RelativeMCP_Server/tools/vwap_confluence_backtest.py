"""Dual-Anchor VWAP Confluence Fade — backtest baseline crudo.

Estrategia:
- 2 VWAPs dinámicos en paralelo: **ETH** (reset 18:00 ET) y **RTH** (09:30-16:00 ET)
- Bandas de desviación estándar (SD1/SD2/SD3 up/dn) por cada uno
- **Confluencia inferior** = bandas lower SD2/SD3 de ETH y RTH que coinciden dentro
  de ``confluence_tolerance_ticks``. La zona es ``[min, max]`` de las bandas calificantes
- **Confluencia superior** = simétrico con upper bands
- Ventana operativa: 09:30-15:00 ET (RTH activo → ambos VWAPs calculan)

Máquina de estados:

    IDLE
      ↓ (wick ≥ wick_ticks_inside dentro de confluencia inferior/superior)
    LONG_ARMED / SHORT_ARMED
      ↓ (Signal 2: close - anchored_low_vwap ≥ threshold_ticks)
    IN_TRADE

Cancelación: durante ARMED, si close cruza cualquier VWAP central (ETH o RTH) en
dirección contraria al fade → vuelta a IDLE.

Anchor modes:
- ``TOUCH`` (default, B): ancla VWAP en la vela del toque. Re-ancla si hay
  nuevo low/high posterior mientras ARMED.
- ``SESSION_EXTREME`` (A): usa el Low/High VWAP nativo anclado al extremo
  absoluto de sesión ETH.

Entry: Close del bar de Signal 2.
Stop: Low[vela_anclaje_final] - 1 tick (long) / High[vela_anclaje_final] + 1 tick (short).
Target (dinámico):
  1. Si existe confluencia opuesta activa y el bar la toca → TP ahí
  2. Fallback: bar toca SD2 ETH del lado opuesto → TP ahí

Re-arm: tras TP/SL, IDLE inmediato. Nueva confluencia tocada → nuevo setup
(sin límite de trades/día).

Cierre forzado: si ``close_at_rth_end``, exit al precio de la bar que cruza
``rth_end`` (default 16:00 ET).

**Cobertura**: ``observer.get_bars`` cap 10000 bars/request. Cobertura aproximada
(sesión ETH 23h):

- ``1m`` → ~7 días | ``5m`` → ~36 días | ``15m`` → ~108 días | ``1h`` → ~434 días

Para horizontes largos con granularidad 1m se necesita feed CSV alternativo.
Output reporta ``bars_received`` y ``effective_days_covered`` para transparencia.
"""
from __future__ import annotations

import math
from collections import defaultdict
from datetime import datetime, time, timedelta
from typing import Any, Optional

from . import observer
from .backtest import _compute_stats, _parse_dt, _parse_hhmm


# ---------------------------------------------------------------------------
# Session helpers
# ---------------------------------------------------------------------------


def _effective_eth_session_date(dt: datetime, reset_hour: int = 18):
    """Fecha de la sesión ETH. Si hora >= reset_hour → pertenece al día siguiente."""
    if dt.hour >= reset_hour:
        return (dt + timedelta(days=1)).date()
    return dt.date()


def _in_time_window(t: time, start: time, end: time) -> bool:
    return start <= t <= end


# ---------------------------------------------------------------------------
# Dual-VWAP computation
# ---------------------------------------------------------------------------


def _compute_dual_vwaps(
    bars: list[dict],
    eth_reset_hour: int = 18,
    rth_start: time = time(9, 30),
    rth_end: time = time(16, 0),
) -> None:
    """Inyecta VWAP + bandas ETH y RTH en cada bar (in-place).

    Campos añadidos por bar:
    - ``eth_vwap``, ``eth_sd{1,2,3}_up``, ``eth_sd{1,2,3}_dn``
    - ``rth_vwap``, ``rth_sd{1,2,3}_up``, ``rth_sd{1,2,3}_dn`` (NaN fuera RTH)
    - ``eth_session_date``, ``rth_active`` (bool)
    """
    # ETH accumulators
    eth_cum_pv = 0.0
    eth_cum_v = 0.0
    eth_cum_p2v = 0.0
    eth_last_session = None

    # RTH accumulators (reset each calendar day at rth_start)
    rth_cum_pv = 0.0
    rth_cum_v = 0.0
    rth_cum_p2v = 0.0
    rth_last_cal_date = None

    for bar in bars:
        dt = bar["dt"]
        eth_session = _effective_eth_session_date(dt, eth_reset_hour)
        if eth_session != eth_last_session:
            eth_cum_pv = eth_cum_v = eth_cum_p2v = 0.0
            eth_last_session = eth_session

        tp = (bar["h"] + bar["l"] + bar["c"]) / 3.0
        vol = bar["v"]
        eth_cum_pv += tp * vol
        eth_cum_v += vol
        eth_cum_p2v += tp * tp * vol

        if eth_cum_v > 0:
            eth_vwap = eth_cum_pv / eth_cum_v
            eth_var = (eth_cum_p2v / eth_cum_v) - (eth_vwap * eth_vwap)
            eth_std = math.sqrt(max(0.0, eth_var))
        else:
            eth_vwap = tp
            eth_std = 0.0

        bar["eth_session_date"] = eth_session
        bar["eth_vwap"] = eth_vwap
        bar["eth_sd1_up"] = eth_vwap + eth_std
        bar["eth_sd1_dn"] = eth_vwap - eth_std
        bar["eth_sd2_up"] = eth_vwap + 2 * eth_std
        bar["eth_sd2_dn"] = eth_vwap - 2 * eth_std
        bar["eth_sd3_up"] = eth_vwap + 3 * eth_std
        bar["eth_sd3_dn"] = eth_vwap - 3 * eth_std

        # RTH block: reset at rth_start each calendar day, accumulate only inside RTH
        cal_date = dt.date()
        t = dt.time()
        in_rth = rth_start <= t < rth_end
        if in_rth:
            if cal_date != rth_last_cal_date:
                rth_cum_pv = rth_cum_v = rth_cum_p2v = 0.0
                rth_last_cal_date = cal_date
            rth_cum_pv += tp * vol
            rth_cum_v += vol
            rth_cum_p2v += tp * tp * vol

            if rth_cum_v > 0:
                rth_vwap = rth_cum_pv / rth_cum_v
                rth_var = (rth_cum_p2v / rth_cum_v) - (rth_vwap * rth_vwap)
                rth_std = math.sqrt(max(0.0, rth_var))
            else:
                rth_vwap = tp
                rth_std = 0.0

            bar["rth_active"] = True
            bar["rth_vwap"] = rth_vwap
            bar["rth_sd1_up"] = rth_vwap + rth_std
            bar["rth_sd1_dn"] = rth_vwap - rth_std
            bar["rth_sd2_up"] = rth_vwap + 2 * rth_std
            bar["rth_sd2_dn"] = rth_vwap - 2 * rth_std
            bar["rth_sd3_up"] = rth_vwap + 3 * rth_std
            bar["rth_sd3_dn"] = rth_vwap - 3 * rth_std
        else:
            bar["rth_active"] = False
            bar["rth_vwap"] = float("nan")
            bar["rth_sd1_up"] = float("nan")
            bar["rth_sd1_dn"] = float("nan")
            bar["rth_sd2_up"] = float("nan")
            bar["rth_sd2_dn"] = float("nan")
            bar["rth_sd3_up"] = float("nan")
            bar["rth_sd3_dn"] = float("nan")


# ---------------------------------------------------------------------------
# Confluence detection
# ---------------------------------------------------------------------------


def _collect_band_values(bar: dict, side: str, bands_to_use: list[str]) -> dict:
    """Devuelve {source_label: price} para las bandas del lado pedido en ETH y RTH.

    side: "lower" → SDn_dn ; "upper" → SDn_up
    Si RTH no está activa, solo devuelve las ETH (no habrá confluencia).
    """
    suffix = "_dn" if side == "lower" else "_up"
    out: dict = {}
    for sd in bands_to_use:
        n = sd.replace("SD", "").strip()
        eth_key = f"eth_sd{n}{suffix}"
        if eth_key in bar and not _isnan(bar[eth_key]):
            out[f"ETH_{sd}"] = bar[eth_key]
        if bar.get("rth_active"):
            rth_key = f"rth_sd{n}{suffix}"
            if rth_key in bar and not _isnan(bar[rth_key]):
                out[f"RTH_{sd}"] = bar[rth_key]
    return out


def _detect_confluence_zone(
    bar: dict,
    side: str,
    tolerance_points: float,
    bands_to_use: list[str],
) -> Optional[dict]:
    """Encuentra confluencia ETH∩RTH en las bandas del lado pedido.

    Requiere ≥1 par (ETH, RTH) con |eth - rth| ≤ tolerance_points. Si se cumple,
    ``zone = [min, max]`` sobre todos los precios de bandas involucradas en
    algún par calificante, más los miembros.
    """
    if not bar.get("rth_active"):
        return None
    levels = _collect_band_values(bar, side, bands_to_use)
    eth_levels = {k: v for k, v in levels.items() if k.startswith("ETH_")}
    rth_levels = {k: v for k, v in levels.items() if k.startswith("RTH_")}
    if not eth_levels or not rth_levels:
        return None

    qualifying = set()
    for ek, ev in eth_levels.items():
        for rk, rv in rth_levels.items():
            if abs(ev - rv) <= tolerance_points:
                qualifying.add(ek)
                qualifying.add(rk)

    if not qualifying:
        return None

    prices = [levels[k] for k in qualifying]
    return {
        "min": round(min(prices), 4),
        "max": round(max(prices), 4),
        "members": sorted(qualifying),
        "side": side,
    }


def _is_touched(
    bar: dict,
    zone: dict,
    side: str,
    wick_ticks_inside: int,
    tick_size: float,
) -> bool:
    """¿La vela penetra la zona por al menos ``wick_ticks_inside`` ticks?

    - lower zone (long setup): bar.low ≤ zone.max - wick_threshold
    - upper zone (short setup): bar.high ≥ zone.min + wick_threshold
    """
    wick_threshold = wick_ticks_inside * tick_size
    if side == "lower":
        return bar["l"] <= (zone["max"] - wick_threshold + 1e-9)
    else:
        return bar["h"] >= (zone["min"] + wick_threshold - 1e-9)


def _isnan(x) -> bool:
    return isinstance(x, float) and math.isnan(x)


# ---------------------------------------------------------------------------
# Anchored VWAP tracker
# ---------------------------------------------------------------------------


class AnchoredVwap:
    """Cum VWAP anclado desde una bar específica. Se alimenta secuencialmente
    con ``update(bar)`` a partir del anchor inclusive.
    """

    def __init__(self, anchor_idx: int, anchor_price: float):
        self.anchor_idx = anchor_idx
        self.anchor_price = anchor_price
        self.cum_pv = 0.0
        self.cum_v = 0.0
        self.value: float = anchor_price  # placeholder hasta la 1ra update

    def update(self, bar: dict) -> float:
        tp = (bar["h"] + bar["l"] + bar["c"]) / 3.0
        vol = bar["v"]
        self.cum_pv += tp * vol
        self.cum_v += vol
        self.value = self.cum_pv / self.cum_v if self.cum_v > 0 else tp
        return self.value


# ---------------------------------------------------------------------------
# State machine (simulación principal)
# ---------------------------------------------------------------------------


def _session_extremes_up_to(bars: list[dict], idx: int, session_key) -> tuple[int, float, int, float]:
    """Recorre bars de la sesión ETH actual hasta ``idx`` y devuelve
    ``(low_idx, low_price, high_idx, high_price)`` absolutos.
    """
    low_idx = idx
    low_price = bars[idx]["l"]
    high_idx = idx
    high_price = bars[idx]["h"]
    for j in range(idx, -1, -1):
        if bars[j].get("eth_session_date") != session_key:
            break
        if bars[j]["l"] < low_price:
            low_price = bars[j]["l"]
            low_idx = j
        if bars[j]["h"] > high_price:
            high_price = bars[j]["h"]
            high_idx = j
    return low_idx, low_price, high_idx, high_price


def _run_state_machine(bars: list[dict], params: dict) -> tuple[list[dict], dict]:
    """Recorre bars secuencialmente y simula el setup. Devuelve (trades, counters)."""
    tick_size = params["tick_size"]
    signal2_threshold_pts = params["signal2_threshold_ticks"] * tick_size
    tolerance_pts = params["confluence_tolerance_ticks"] * tick_size
    bands_to_use = params["bands_to_use"]
    wick_ticks_inside = params["touch_wick_ticks_inside"]
    anchor_mode = params["anchor_mode"]
    cancel_on_central = params["cancel_on_central_cross"]
    close_at_rth_end = params["close_at_rth_end"]
    window_start = params["_window_start_t"]
    window_end = params["_window_end_t"]
    rth_end_t = params["_rth_end_t"]

    state = "IDLE"
    direction: Optional[str] = None  # "long" / "short"
    setups_armed_total = 0
    setups_cancelled = 0
    setups_triggered = 0

    # Armed / in-trade context
    touch_bar_idx: Optional[int] = None
    touch_price: Optional[float] = None
    anchor_idx: Optional[int] = None
    anchor_price: Optional[float] = None
    avwap: Optional[AnchoredVwap] = None
    state_transitions: list[dict] = []

    # Active trade context
    entry_idx: Optional[int] = None
    entry_price: Optional[float] = None
    stop_price: Optional[float] = None
    current_anchor_low = float("inf")
    current_anchor_high = float("-inf")
    current_session: Any = None

    trades: list[dict] = []

    def _reset_armed():
        nonlocal state, direction, touch_bar_idx, touch_price, anchor_idx
        nonlocal anchor_price, avwap, current_anchor_low, current_anchor_high
        nonlocal state_transitions
        state = "IDLE"
        direction = None
        touch_bar_idx = None
        touch_price = None
        anchor_idx = None
        anchor_price = None
        avwap = None
        current_anchor_low = float("inf")
        current_anchor_high = float("-inf")
        state_transitions = []

    def _reset_trade():
        nonlocal entry_idx, entry_price, stop_price
        entry_idx = None
        entry_price = None
        stop_price = None

    for i, bar in enumerate(bars):
        # Detectar boundary de sesión ETH → reset full state machine
        bar_session = bar.get("eth_session_date")
        if bar_session != current_session:
            # Si había trade abierto entre sesiones, cerrarlo al close de la bar previa
            if state == "IN_TRADE" and i > 0:
                prev = bars[i - 1]
                trades.append(_build_trade_record(
                    session_date=str(current_session),
                    direction=direction,
                    state_transitions=state_transitions,
                    touch_bar=bars[touch_bar_idx] if touch_bar_idx is not None else prev,
                    anchor_bar=bars[anchor_idx] if anchor_idx is not None else prev,
                    anchor_price=anchor_price,
                    entry_bar=bars[entry_idx] if entry_idx is not None else prev,
                    entry_price=entry_price,
                    stop=stop_price,
                    target_hit=None,
                    target_type=None,
                    exit_bar=prev,
                    exit_price=prev["c"],
                    exit_reason="session_boundary",
                    bars=bars,
                ))
            _reset_armed()
            _reset_trade()
            current_session = bar_session

        t = bar["dt"].time()

        # === IN_TRADE: chequear salida antes que cualquier otra lógica ===
        if state == "IN_TRADE":
            # Cierre forzado al rth_end
            if close_at_rth_end and t >= rth_end_t:
                trades.append(_build_trade_record(
                    session_date=str(bar_session),
                    direction=direction,
                    state_transitions=state_transitions,
                    touch_bar=bars[touch_bar_idx],
                    anchor_bar=bars[anchor_idx],
                    anchor_price=anchor_price,
                    entry_bar=bars[entry_idx],
                    entry_price=entry_price,
                    stop=stop_price,
                    target_hit=None,
                    target_type=None,
                    exit_bar=bar,
                    exit_price=bar["o"],  # open del bar rth_end ≈ precio al 16:00
                    exit_reason="time_out_rth",
                    bars=bars,
                ))
                _reset_armed()
                _reset_trade()
                # Mismo bar puede re-armar → seguimos al bloque de detección
            else:
                # Chequear stop primero (conservador)
                hit_stop = False
                if direction == "long" and bar["l"] <= stop_price:
                    trades.append(_build_trade_record(
                        session_date=str(bar_session),
                        direction=direction,
                        state_transitions=state_transitions,
                        touch_bar=bars[touch_bar_idx],
                        anchor_bar=bars[anchor_idx],
                        anchor_price=anchor_price,
                        entry_bar=bars[entry_idx],
                        entry_price=entry_price,
                        stop=stop_price,
                        target_hit=None,
                        target_type=None,
                        exit_bar=bar,
                        exit_price=stop_price,
                        exit_reason="stop",
                        bars=bars,
                    ))
                    hit_stop = True
                elif direction == "short" and bar["h"] >= stop_price:
                    trades.append(_build_trade_record(
                        session_date=str(bar_session),
                        direction=direction,
                        state_transitions=state_transitions,
                        touch_bar=bars[touch_bar_idx],
                        anchor_bar=bars[anchor_idx],
                        anchor_price=anchor_price,
                        entry_bar=bars[entry_idx],
                        entry_price=entry_price,
                        stop=stop_price,
                        target_hit=None,
                        target_type=None,
                        exit_bar=bar,
                        exit_price=stop_price,
                        exit_reason="stop",
                        bars=bars,
                    ))
                    hit_stop = True

                if hit_stop:
                    _reset_armed()
                    _reset_trade()
                else:
                    # Target dinámico: confluencia opuesta activa o fallback SD2 ETH opuesto
                    opp_side = "upper" if direction == "long" else "lower"
                    opp_zone = _detect_confluence_zone(
                        bar, opp_side, tolerance_pts, bands_to_use
                    )
                    target_price = None
                    target_type = None
                    if opp_zone is not None:
                        if direction == "long" and bar["h"] >= opp_zone["min"]:
                            target_price = opp_zone["min"]
                            target_type = "confluence_opposite"
                        elif direction == "short" and bar["l"] <= opp_zone["max"]:
                            target_price = opp_zone["max"]
                            target_type = "confluence_opposite"
                    if target_price is None:
                        # Fallback SD2 ETH opuesto
                        fb = bar["eth_sd2_up"] if direction == "long" else bar["eth_sd2_dn"]
                        if direction == "long" and bar["h"] >= fb:
                            target_price = fb
                            target_type = "fallback_sd2_eth"
                        elif direction == "short" and bar["l"] <= fb:
                            target_price = fb
                            target_type = "fallback_sd2_eth"

                    if target_price is not None:
                        trades.append(_build_trade_record(
                            session_date=str(bar_session),
                            direction=direction,
                            state_transitions=state_transitions,
                            touch_bar=bars[touch_bar_idx],
                            anchor_bar=bars[anchor_idx],
                            anchor_price=anchor_price,
                            entry_bar=bars[entry_idx],
                            entry_price=entry_price,
                            stop=stop_price,
                            target_hit=target_price,
                            target_type=target_type,
                            exit_bar=bar,
                            exit_price=target_price,
                            exit_reason="target",
                            bars=bars,
                        ))
                        _reset_armed()
                        _reset_trade()
                    else:
                        continue  # sigue en IN_TRADE, pasa al siguiente bar

        # === IDLE: buscar toque de confluencia ===
        if state == "IDLE":
            if not _in_time_window(t, window_start, window_end):
                continue
            # Lower → long; upper → short
            for side, this_dir in (("lower", "long"), ("upper", "short")):
                zone = _detect_confluence_zone(bar, side, tolerance_pts, bands_to_use)
                if zone is None:
                    continue
                if not _is_touched(bar, zone, side, wick_ticks_inside, tick_size):
                    continue
                # ARMAR
                state = "LONG_ARMED" if this_dir == "long" else "SHORT_ARMED"
                direction = this_dir
                touch_bar_idx = i
                touch_price = bar["l"] if this_dir == "long" else bar["h"]
                setups_armed_total += 1
                state_transitions = [{"bar_time": bar["t"], "to": state, "zone": zone}]

                # Anchor inicial
                if anchor_mode == "TOUCH":
                    anchor_idx = i
                    anchor_price = touch_price
                else:  # SESSION_EXTREME
                    low_i, low_p, high_i, high_p = _session_extremes_up_to(
                        bars, i, bar_session
                    )
                    if this_dir == "long":
                        anchor_idx = low_i
                        anchor_price = low_p
                    else:
                        anchor_idx = high_i
                        anchor_price = high_p

                # Inicializar anchored VWAP corriendo desde anchor_idx hasta i
                avwap = AnchoredVwap(anchor_idx, anchor_price)
                for k in range(anchor_idx, i + 1):
                    avwap.update(bars[k])

                if this_dir == "long":
                    current_anchor_low = anchor_price
                    current_anchor_high = float("-inf")
                else:
                    current_anchor_high = anchor_price
                    current_anchor_low = float("inf")
                break  # solo un lado por bar

            continue  # procesar el siguiente bar

        # === ARMED: tracking + chequeo de cancelación + Signal 2 ===
        if state in ("LONG_ARMED", "SHORT_ARMED"):
            # Cancelación por cruce de VWAP central
            if cancel_on_central:
                cancel = False
                if direction == "long":
                    if bar["c"] > bar["eth_vwap"] or (
                        bar.get("rth_active") and bar["c"] > bar["rth_vwap"]
                    ):
                        cancel = True
                else:
                    if bar["c"] < bar["eth_vwap"] or (
                        bar.get("rth_active") and bar["c"] < bar["rth_vwap"]
                    ):
                        cancel = True
                if cancel:
                    setups_cancelled += 1
                    state_transitions.append(
                        {"bar_time": bar["t"], "to": "IDLE", "reason": "central_cross"}
                    )
                    _reset_armed()
                    continue

            # Ventana: si salió de ventana, también cancelamos (no podemos disparar fuera)
            if not _in_time_window(t, window_start, window_end):
                setups_cancelled += 1
                state_transitions.append(
                    {"bar_time": bar["t"], "to": "IDLE", "reason": "window_end"}
                )
                _reset_armed()
                continue

            # Re-anchor en TOUCH si hay nuevo extremo
            if anchor_mode == "TOUCH":
                if direction == "long" and bar["l"] < current_anchor_low:
                    current_anchor_low = bar["l"]
                    anchor_idx = i
                    anchor_price = bar["l"]
                    avwap = AnchoredVwap(anchor_idx, anchor_price)
                    avwap.update(bar)
                    state_transitions.append(
                        {"bar_time": bar["t"], "event": "reanchor", "price": anchor_price}
                    )
                elif direction == "short" and bar["h"] > current_anchor_high:
                    current_anchor_high = bar["h"]
                    anchor_idx = i
                    anchor_price = bar["h"]
                    avwap = AnchoredVwap(anchor_idx, anchor_price)
                    avwap.update(bar)
                    state_transitions.append(
                        {"bar_time": bar["t"], "event": "reanchor", "price": anchor_price}
                    )
                else:
                    avwap.update(bar)
            else:
                # SESSION_EXTREME: anchor sólo cambia si sesión hace nuevo extremo absoluto
                low_i, low_p, high_i, high_p = _session_extremes_up_to(
                    bars, i, bar_session
                )
                if direction == "long" and low_i != anchor_idx:
                    anchor_idx = low_i
                    anchor_price = low_p
                    avwap = AnchoredVwap(anchor_idx, anchor_price)
                    for k in range(anchor_idx, i + 1):
                        avwap.update(bars[k])
                    state_transitions.append(
                        {"bar_time": bar["t"], "event": "reanchor", "price": anchor_price}
                    )
                elif direction == "short" and high_i != anchor_idx:
                    anchor_idx = high_i
                    anchor_price = high_p
                    avwap = AnchoredVwap(anchor_idx, anchor_price)
                    for k in range(anchor_idx, i + 1):
                        avwap.update(bars[k])
                    state_transitions.append(
                        {"bar_time": bar["t"], "event": "reanchor", "price": anchor_price}
                    )
                else:
                    avwap.update(bar)

            # Signal 2: despegue del anchored VWAP
            if direction == "long":
                sep = bar["c"] - avwap.value
                if sep >= signal2_threshold_pts:
                    # DISPARAR entry
                    entry_idx = i
                    entry_price = bar["c"]
                    stop_price = bars[anchor_idx]["l"] - tick_size
                    state = "IN_TRADE"
                    setups_triggered += 1
                    state_transitions.append(
                        {"bar_time": bar["t"], "to": "IN_TRADE",
                         "entry": entry_price, "avwap": round(avwap.value, 4),
                         "sep_pts": round(sep, 4)}
                    )
            else:
                sep = avwap.value - bar["c"]
                if sep >= signal2_threshold_pts:
                    entry_idx = i
                    entry_price = bar["c"]
                    stop_price = bars[anchor_idx]["h"] + tick_size
                    state = "IN_TRADE"
                    setups_triggered += 1
                    state_transitions.append(
                        {"bar_time": bar["t"], "to": "IN_TRADE",
                         "entry": entry_price, "avwap": round(avwap.value, 4),
                         "sep_pts": round(sep, 4)}
                    )

    # Cerrar trade final abierto si quedó colgado
    if state == "IN_TRADE" and bars:
        last = bars[-1]
        trades.append(_build_trade_record(
            session_date=str(last.get("eth_session_date")),
            direction=direction,
            state_transitions=state_transitions,
            touch_bar=bars[touch_bar_idx],
            anchor_bar=bars[anchor_idx],
            anchor_price=anchor_price,
            entry_bar=bars[entry_idx],
            entry_price=entry_price,
            stop=stop_price,
            target_hit=None,
            target_type=None,
            exit_bar=last,
            exit_price=last["c"],
            exit_reason="end_of_data",
            bars=bars,
        ))

    counters = {
        "setups_armed_total": setups_armed_total,
        "setups_cancelled_by_central_cross": setups_cancelled,
        "setups_triggered": setups_triggered,
    }
    return trades, counters


# ---------------------------------------------------------------------------
# Trade record builder
# ---------------------------------------------------------------------------


def _build_trade_record(
    session_date: str,
    direction: str,
    state_transitions: list[dict],
    touch_bar: dict,
    anchor_bar: dict,
    anchor_price: float,
    entry_bar: dict,
    entry_price: float,
    stop: float,
    target_hit: Optional[float],
    target_type: Optional[str],
    exit_bar: dict,
    exit_price: float,
    exit_reason: str,
    bars: list[dict],
) -> dict:
    """Construye el dict de trade + calcula pnl, bars_held, MFE/MAE."""
    pnl_pts = (exit_price - entry_price) if direction == "long" else (entry_price - exit_price)

    # MFE/MAE entre entry y exit
    entry_idx = _find_bar_idx(bars, entry_bar["t"])
    exit_idx = _find_bar_idx(bars, exit_bar["t"])
    mfe_pts = 0.0
    mae_pts = 0.0
    if entry_idx is not None and exit_idx is not None and exit_idx >= entry_idx:
        for j in range(entry_idx, exit_idx + 1):
            b = bars[j]
            if direction == "long":
                mfe_pts = max(mfe_pts, b["h"] - entry_price)
                mae_pts = max(mae_pts, entry_price - b["l"])
            else:
                mfe_pts = max(mfe_pts, entry_price - b["l"])
                mae_pts = max(mae_pts, b["h"] - entry_price)

    return {
        "session_date": session_date,
        "direction": direction,
        "state_transitions": state_transitions,
        "touch_bar_time": touch_bar["t"],
        "touch_price": round(touch_bar["l"] if direction == "long" else touch_bar["h"], 4),
        "anchor_bar_time": anchor_bar["t"],
        "anchor_price": round(anchor_price, 4) if anchor_price is not None else None,
        "entry_bar_time": entry_bar["t"],
        "entry_price": round(entry_price, 4),
        "stop": round(stop, 4) if stop is not None else None,
        "target_hit": round(target_hit, 4) if target_hit is not None else None,
        "target_type": target_type,
        "exit_bar_time": exit_bar["t"],
        "exit_price": round(exit_price, 4),
        "exit_reason": exit_reason,
        "pnl_pts": round(pnl_pts, 4),
        "bars_held": (exit_idx - entry_idx) if (entry_idx is not None and exit_idx is not None) else 0,
        "mfe_pts": round(mfe_pts, 4),
        "mae_pts": round(mae_pts, 4),
    }


def _find_bar_idx(bars: list[dict], bar_time: str) -> Optional[int]:
    for i, b in enumerate(bars):
        if b["t"] == bar_time:
            return i
    return None


# ---------------------------------------------------------------------------
# Fetch con paginación por rango
# ---------------------------------------------------------------------------


_CHUNK_DAYS = 30  # chunk size para paginación en modo rango


def _fetch_bars(
    instrument: str,
    tf: str,
    days_back: int,
    estimated_bars: int,
):
    """Devuelve (bars_list, meta) o ({"error": ...}, None).

    Si ``estimated_bars <= 10000`` usa modo ``n`` (1 request). Si excede, usa
    modo rango paginado en chunks de ``_CHUNK_DAYS`` días.
    """
    if estimated_bars <= 10000:
        target_n = min(10000, estimated_bars)
        data = observer.get_bars(instrument, tf=tf, n=target_n)
        if "error" in data or not data.get("bars"):
            return {
                "error": data.get("error", "sin bars"),
                "addon_reachable": data.get("addon_reachable", False),
            }, None
        meta = {
            "mode": "n",
            "requests": 1,
            "bars_received": data.get("count", len(data["bars"])),
        }
        return data["bars"], meta

    # Modo rango: anchor por la última bar disponible
    anchor = observer.get_bars(instrument, tf=tf, n=1)
    if "error" in anchor or not anchor.get("bars"):
        return {
            "error": "no se pudo anclar rango: " + str(anchor.get("error", "sin bars")),
            "addon_reachable": anchor.get("addon_reachable", False),
        }, None

    end_dt = _parse_dt(anchor["bars"][-1]["t"])
    start_dt = end_dt - timedelta(days=days_back)

    all_bars: list[dict] = []
    seen_times: set[str] = set()
    cursor = start_dt
    n_requests = 0
    while cursor < end_dt:
        chunk_end = min(cursor + timedelta(days=_CHUNK_DAYS), end_dt + timedelta(days=1))
        chunk = observer.get_bars(
            instrument,
            tf=tf,
            from_date=cursor.strftime("%Y-%m-%dT%H:%M:%S"),
            to_date=chunk_end.strftime("%Y-%m-%dT%H:%M:%S"),
        )
        n_requests += 1
        if "error" in chunk:
            return {
                "error": f"chunk {cursor.date()}..{chunk_end.date()}: {chunk['error']}",
                "bars_so_far": len(all_bars),
                "chunks_completed": n_requests - 1,
            }, None
        for b in chunk.get("bars", []):
            if b["t"] not in seen_times:
                seen_times.add(b["t"])
                all_bars.append(b)
        cursor = chunk_end

    # Garantizar orden cronológico (chunks secuenciales ya lo dan)
    all_bars.sort(key=lambda x: x["t"])
    meta = {
        "mode": "range",
        "requests": n_requests,
        "bars_received": len(all_bars),
        "range_from": start_dt.strftime("%Y-%m-%d %H:%M:%S"),
        "range_to": end_dt.strftime("%Y-%m-%d %H:%M:%S"),
    }
    return all_bars, meta


# ---------------------------------------------------------------------------
# Main entry
# ---------------------------------------------------------------------------


def vwap_confluence_backtest(
    instrument: str = "MES 06-26",
    days_back: int = 365,
    tf: str = "1m",
    eth_reset_hour: int = 18,
    rth_start: str = "09:30",
    rth_end: str = "16:00",
    window_start: str = "09:30",
    window_end: str = "15:00",
    confluence_tolerance_ticks: int = 1,
    bands_to_use: Optional[list[str]] = None,
    signal2_threshold_ticks: int = 1,
    touch_wick_ticks_inside: int = 1,
    anchor_mode: str = "TOUCH",
    close_at_rth_end: bool = True,
    cancel_on_central_cross: bool = True,
    tick_size: float = 0.25,
    point_value: float = 5.0,
) -> dict:
    """Backtest baseline crudo del Dual-Anchor VWAP Confluence Fade.

    Por diseño NO aplica filtro de sentimiento / régimen. Devuelve stats
    globales, por dirección (long/short), por exit_reason, y daily_breakdown
    para cruzar luego con un clasificador rotacional-vs-tendencial (fase 2).
    """
    if bands_to_use is None:
        bands_to_use = ["SD2", "SD3"]

    # 1. Fetch bars — modo N si cabe, paginado por rango si no
    if tf.endswith("m"):
        bars_per_day = 23 * 60 // int(tf.rstrip("m"))
    elif tf.endswith("h"):
        bars_per_day = 23 // int(tf.rstrip("h"))
    else:
        bars_per_day = 1380

    estimated_bars = (days_back + 2) * bars_per_day
    bars, fetch_meta = _fetch_bars(instrument, tf, days_back, estimated_bars)
    if isinstance(bars, dict) and "error" in bars:
        return bars

    for b in bars:
        b["dt"] = _parse_dt(b["t"])

    # Filtrar a últimos days_back (respetando lo recibido)
    if bars:
        cutoff = bars[-1]["dt"].date() - timedelta(days=days_back)
        bars = [b for b in bars if b["dt"].date() >= cutoff]

    if len(bars) < 50:
        return {"error": f"bars insuficientes ({len(bars)} recibidos)"}

    # 2. VWAPs duales en cada bar
    rth_start_t = _parse_hhmm(rth_start)
    rth_end_t = _parse_hhmm(rth_end)
    _compute_dual_vwaps(bars, eth_reset_hour, rth_start_t, rth_end_t)

    # 3. Parámetros internos
    params = {
        "tick_size": tick_size,
        "signal2_threshold_ticks": signal2_threshold_ticks,
        "confluence_tolerance_ticks": confluence_tolerance_ticks,
        "bands_to_use": bands_to_use,
        "touch_wick_ticks_inside": touch_wick_ticks_inside,
        "anchor_mode": anchor_mode,
        "cancel_on_central_cross": cancel_on_central_cross,
        "close_at_rth_end": close_at_rth_end,
        "_window_start_t": _parse_hhmm(window_start),
        "_window_end_t": _parse_hhmm(window_end),
        "_rth_end_t": rth_end_t,
    }

    # 4. Run state machine
    trades, counters = _run_state_machine(bars, params)

    # 5. Stats globales
    stats = _compute_stats(trades, point_value=point_value)

    # 6. Stats por dirección
    long_trades = [t for t in trades if t["direction"] == "long"]
    short_trades = [t for t in trades if t["direction"] == "short"]
    stats_by_direction = {
        "long": _compute_stats(long_trades, point_value=point_value),
        "short": _compute_stats(short_trades, point_value=point_value),
    }

    # 7. Stats por exit_reason
    by_reason: dict[str, list] = defaultdict(list)
    for t in trades:
        by_reason[t["exit_reason"]].append(t)
    stats_by_exit_reason = {
        reason: _compute_stats(ts, point_value=point_value)
        for reason, ts in by_reason.items()
    }

    # 8. Daily breakdown (clave para cruzar con sentimiento)
    by_day = defaultdict(lambda: {"trades": 0, "pnl_pts": 0.0,
                                   "wins": 0, "losses": 0})
    for t in trades:
        d = by_day[t["session_date"]]
        d["trades"] += 1
        d["pnl_pts"] += t["pnl_pts"]
        if t["pnl_pts"] > 0:
            d["wins"] += 1
        else:
            d["losses"] += 1
    daily = []
    for d, v in sorted(by_day.items()):
        daily.append({
            "date": d,
            "trades": v["trades"],
            "wins": v["wins"],
            "losses": v["losses"],
            "pnl_pts": round(v["pnl_pts"], 2),
            "pnl_usd": round(v["pnl_pts"] * point_value, 2),
        })

    # Coverage transparente
    unique_sessions = {b.get("eth_session_date") for b in bars}
    unique_sessions.discard(None)
    effective_days = len(unique_sessions)

    return {
        "instrument": instrument,
        "config": {
            "days_back": days_back,
            "tf": tf,
            "eth_reset_hour": eth_reset_hour,
            "rth_start": rth_start,
            "rth_end": rth_end,
            "window_start": window_start,
            "window_end": window_end,
            "confluence_tolerance_ticks": confluence_tolerance_ticks,
            "bands_to_use": bands_to_use,
            "signal2_threshold_ticks": signal2_threshold_ticks,
            "touch_wick_ticks_inside": touch_wick_ticks_inside,
            "anchor_mode": anchor_mode,
            "close_at_rth_end": close_at_rth_end,
            "cancel_on_central_cross": cancel_on_central_cross,
            "tick_size": tick_size,
            "point_value": point_value,
        },
        "bars_analyzed": len(bars),
        "bars_received": fetch_meta.get("bars_received") if fetch_meta else len(bars),
        "fetch_mode": fetch_meta.get("mode") if fetch_meta else "n",
        "fetch_requests": fetch_meta.get("requests") if fetch_meta else 1,
        "effective_days_covered": effective_days,
        "first_bar": bars[0]["t"] if bars else None,
        "last_bar": bars[-1]["t"] if bars else None,
        "setups_armed_total": counters["setups_armed_total"],
        "setups_cancelled_by_central_cross": counters["setups_cancelled_by_central_cross"],
        "setups_triggered": counters["setups_triggered"],
        "stats": stats,
        "stats_by_direction": stats_by_direction,
        "stats_by_exit_reason": stats_by_exit_reason,
        "daily_breakdown": daily,
        "trades": trades,
    }
