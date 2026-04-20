"""Metodología NADRO aplicada a los datos live del Registry + bars del AddOn.

Acrónimo N-A-D-R-O:
    N — Narrativa (estructura + bias + destinos via LMD)
    A — Aceptación (validación de niveles con regla 50%)
    D — DVA/Distribución (Fading vs Imbalance Pullback)
    R — Ritmo (rotaciones dinámicas últimas N barras)
    O — Order Flow (delta + divergencias + absorción)

Fuentes de datos consumidas:
- Registry: RelativeVwap, 5 forks VWAP, RelativeDelta, RelativeVolumeProfile,
  RelativeVwapLevels.
- AddOn HTTP: /bars y /quote vía ``observer``.
- Files: confluencias consolidadas vía ``vwap_levels``.
"""
from __future__ import annotations

import math
from dataclasses import dataclass
from datetime import datetime, timedelta
from typing import Any

from . import observer
from . import vwap_levels


# -----------------------------------------------------------------------------
# NADRO Freshness — Ley 8 (la energía se disipa alejándose del origen)
# -----------------------------------------------------------------------------


# Duración aproximada de cada período NADRO en horas
_PERIOD_HOURS = {
    "D": 24,
    "W": 168,       # 7 * 24
    "M": 720,       # 30 * 24
    "Q": 2160,      # 90 * 24
    "Y": 8760,      # 365 * 24
}


def _parse_bar_time(bar_time: Any) -> datetime | None:
    """Normaliza el bar_time (string ISO o datetime) a datetime naive."""
    if isinstance(bar_time, datetime):
        return bar_time
    if not isinstance(bar_time, str):
        return None
    for fmt in ("%Y-%m-%dT%H:%M:%S.%f%z", "%Y-%m-%d %H:%M:%S.%f", "%Y-%m-%d %H:%M:%S"):
        try:
            return datetime.strptime(bar_time.rstrip("Z"), fmt.rstrip("%z").rstrip())
        except ValueError:
            continue
    try:
        return datetime.fromisoformat(bar_time.replace("Z", ""))
    except ValueError:
        return None


def _period_start(tf: str, ref_time: datetime) -> datetime:
    """Inicio aproximado del período actual para el TF dado (en ET naive).

    Convenciones NT:
    - Daily: 18:00 ET del día previo (ETH) — aquí usamos 00:00 del día actual
      como aproximación simple (suficiente para freshness score).
    - Weekly: domingo 18:00 ET (globex open) → simplificado a lunes 00:00.
    - Monthly: día 1 del mes a las 00:00.
    - Quarterly: día 1 del mes inicial del trimestre.
    - Annual: 1 enero 00:00.
    """
    if tf == "D":
        return datetime(ref_time.year, ref_time.month, ref_time.day)
    if tf == "W":
        # lunes como start del período
        days_back = ref_time.weekday()  # Monday=0
        monday = ref_time - timedelta(days=days_back)
        return datetime(monday.year, monday.month, monday.day)
    if tf == "M":
        return datetime(ref_time.year, ref_time.month, 1)
    if tf == "Q":
        q_month = ((ref_time.month - 1) // 3) * 3 + 1
        return datetime(ref_time.year, q_month, 1)
    if tf == "Y":
        return datetime(ref_time.year, 1, 1)
    return ref_time


def _compute_freshness(ref_time: datetime, tf: str) -> dict:
    """Devuelve freshness info para un TF. Score 0-1 lineal (1 = recién arrancado)."""
    start = _period_start(tf, ref_time)
    age_hours = max(0.0, (ref_time - start).total_seconds() / 3600.0)
    total = _PERIOD_HOURS.get(tf, 24)
    progress = min(1.0, age_hours / total)  # 0 = inicio, 1 = fin
    freshness = 1.0 - progress
    # Clasificación cualitativa
    if freshness >= 0.8:
        label = "fresh"   # recién arrancado — máxima energía (A+ potencial)
    elif freshness >= 0.4:
        label = "developing"
    elif freshness >= 0.15:
        label = "matured"
    else:
        label = "expired"  # próximo reset — energía disipada
    return {
        "period_start": start.isoformat(),
        "age_hours": round(age_hours, 2),
        "period_total_hours": total,
        "progress": round(progress, 3),
        "freshness_score": round(freshness, 3),
        "freshness_label": label,
    }


# -----------------------------------------------------------------------------
# Data collectors
# -----------------------------------------------------------------------------


def _fetch_states_by_indicator(instrument: str) -> dict[str, dict]:
    """Devuelve states del Registry agrupados por indicador para el instrumento."""
    data = observer.list_indicator_states()
    result: dict[str, dict] = {}
    for state in data.get("states", []):
        key = state.get("key", "")
        parts = key.split(":")
        if len(parts) < 2:
            continue
        indicator, inst = parts[0], parts[1]
        if inst != instrument:
            continue
        # Si hay duplicados por TF, dejamos el más reciente — la clave de Registry
        # incluye el TF, así que cada fork VWAP entra una vez.
        result[indicator + (":" + parts[2] if len(parts) > 2 else "")] = state.get("payload", {})
    return result


def _find_payload(states: dict[str, dict], indicator_name: str) -> dict | None:
    """Busca el primer payload cuya clave empieza con ``indicator_name:``."""
    for full_key, payload in states.items():
        if full_key.split(":")[0] == indicator_name:
            return payload
    return None


def _all_payloads(states: dict[str, dict], indicator_name: str) -> list[dict]:
    """Todos los payloads con la base ``indicator_name``."""
    return [
        payload for full_key, payload in states.items()
        if full_key.split(":")[0] == indicator_name
    ]


# -----------------------------------------------------------------------------
# NADRO analyzers
# -----------------------------------------------------------------------------


def _analyze_narrativa(price: float, states: dict[str, dict]) -> dict:
    """N — Narrativa: bias macro / micro + zonas clave."""
    # Developing value areas por timeframe
    tf_map = [
        ("RelativeAnnualVwap", "Y"),
        ("RelativeQuarterlyVwap", "Q"),
        ("RelativeMonthlyVwap", "M"),
        ("RelativeWeeklyVwap", "W"),
        ("RelativeDailyVwap", "D"),
    ]

    bias_per_tf = {}
    for ind_name, prefix in tf_map:
        p = _find_payload(states, ind_name)
        if not p:
            continue
        vwap = p.get("vwap")
        dvah = p.get("dvah_sd1")
        dval = p.get("dval_sd1")
        if vwap is None or dvah is None or dval is None:
            continue
        pos = (
            "above_dvah" if price > dvah else
            "below_dval" if price < dval else
            "above_vwap" if price > vwap else
            "below_vwap"
        )
        bias_per_tf[prefix] = {
            "vwap": vwap,
            "dvah": dvah,
            "dval": dval,
            "price_position": pos,
            "distance_from_vwap_pts": price - vwap,
        }

    # Macro = Y/Q; micro = W/D
    def _avg_position(keys):
        positions = [bias_per_tf[k]["price_position"] for k in keys if k in bias_per_tf]
        if not positions:
            return "unknown"
        above = sum(1 for p in positions if p.startswith("above"))
        below = sum(1 for p in positions if p.startswith("below"))
        if above > below:
            return "bullish"
        if below > above:
            return "bearish"
        return "neutral"

    macro_bias = _avg_position(["Y", "Q"])
    micro_bias = _avg_position(["W", "D"])

    # Summary narrativo
    summary_parts = []
    y = bias_per_tf.get("Y")
    if y and y["vwap"]:
        d = price - y["vwap"]
        summary_parts.append(
            f"{'Alcista' if d > 0 else 'Bajista'} macro ({d:+.1f}pts vs Y-VWAP)"
        )
    w = bias_per_tf.get("W")
    if w:
        if w["price_position"] in ("above_vwap", "below_vwap"):
            summary_parts.append(
                f"rotacional corto plazo dentro Weekly DVA ({w['dval']:.1f}-{w['dvah']:.1f})"
            )
        elif w["price_position"] == "above_dvah":
            summary_parts.append(f"imbalance alcista semanal (sobre {w['dvah']:.1f})")
        else:
            summary_parts.append(f"imbalance bajista semanal (bajo {w['dval']:.1f})")

    return {
        "macro_bias": macro_bias,
        "micro_bias": micro_bias,
        "confluence_macro_vs_micro": (
            "confluence" if macro_bias == micro_bias and macro_bias != "neutral" else "dissonance"
            if macro_bias != "neutral" and micro_bias != "neutral" and macro_bias != micro_bias
            else "neutral"
        ),
        "bias_per_tf": bias_per_tf,
        "summary": ". ".join(summary_parts) if summary_parts else "datos insuficientes",
    }


def _analyze_distribucion(states: dict[str, dict]) -> dict:
    """D — DVA: detecta régimen rotacional vs imbalance y sugiere táctica."""
    rv = _find_payload(states, "RelativeVwap")
    if not rv:
        return {"regime": "unknown", "tactic": "wait", "reason": "RelativeVwap no disponible"}

    trend_mode = rv.get("trend_mode", False)
    bearish = rv.get("trend_bearish", False)
    delta_global = rv.get("delta_global", 0) or 0
    delta_usa = rv.get("delta_usa", 0) or 0

    if trend_mode:
        regime = "imbalance"
        direction = "bearish" if bearish else "bullish"
        tactic = f"imbalance_pullback ({direction})"
        reason = f"RelativeVwap.trend_mode=True, direction={direction}, deltaUSA={delta_usa:.0f}"
    else:
        regime = "rotational"
        tactic = "fading_extremes"
        reason = f"RelativeVwap.trend_mode=False, delta débil (G={delta_global:.0f} USA={delta_usa:.0f})"

    return {
        "regime": regime,
        "tactic": tactic,
        "trend_mode": trend_mode,
        "trend_bearish": bearish,
        "delta_global": delta_global,
        "delta_usa": delta_usa,
        "reason": reason,
    }


def _zigzag_swings(bars: list[dict], reversal_pts: float,
                   lookback: int = 3) -> list[dict]:
    """Zigzag NADRO 3.0: detecta swings alto/bajo reales (no sólo avg range).

    Una barra i es swing high si su high es ≥ highs de las ``lookback`` barras
    a cada lado. Análogo para swing low. Después se filtran alternando H/L y
    exigiendo ``reversal_pts`` mínimo entre swings consecutivos.
    """
    n = len(bars)
    if n < lookback * 2 + 1:
        return []

    raw: list[dict] = []
    for i in range(lookback, n - lookback):
        hi = bars[i]["h"]
        lo = bars[i]["l"]
        is_high = (
            all(bars[j]["h"] <= hi for j in range(i - lookback, i))
            and all(bars[j]["h"] <= hi for j in range(i + 1, i + lookback + 1))
        )
        is_low = (
            all(bars[j]["l"] >= lo for j in range(i - lookback, i))
            and all(bars[j]["l"] >= lo for j in range(i + 1, i + lookback + 1))
        )
        if is_high:
            raw.append({"idx": i, "t": bars[i]["t"], "price": hi, "type": "H"})
        elif is_low:
            raw.append({"idx": i, "t": bars[i]["t"], "price": lo, "type": "L"})

    # Filtrar: alternar H/L, aplicar reversal mínimo
    filtered: list[dict] = []
    for sw in raw:
        if not filtered:
            filtered.append(sw)
            continue
        last = filtered[-1]
        if sw["type"] == last["type"]:
            # Mantener el más extremo
            if sw["type"] == "H" and sw["price"] > last["price"]:
                filtered[-1] = sw
            elif sw["type"] == "L" and sw["price"] < last["price"]:
                filtered[-1] = sw
        else:
            rotation = abs(sw["price"] - last["price"])
            if rotation >= reversal_pts:
                filtered.append(sw)
    return filtered


def _analyze_ritmo(instrument: str, n_bars: int = 60, tf: str = "1m",
                   reversal_pts: float | None = None) -> dict:
    """R — Ritmo NADRO 3.0: rotaciones reales vía Zigzag dinámico.

    ``reversal_pts`` default = 75 percentile del rango de barra del período.
    Esto adapta el Zigzag a la volatilidad actual del instrumento (el mandato
    NADRO: "régimen dinámico, no estático").
    """
    bars_data = observer.get_bars(instrument, tf=tf, n=n_bars)
    if "error" in bars_data or not bars_data.get("bars"):
        return {
            "error": bars_data.get("error", "sin bars"),
            "addon_reachable": bars_data.get("addon_reachable", False),
        }

    bars = bars_data["bars"]
    ranges = sorted(b["h"] - b["l"] for b in bars)
    avg_range = sum(ranges) / len(ranges) if ranges else 0.0

    # Reversal auto-calibrado al 75% percentile del bar range
    if reversal_pts is None:
        p75_idx = int(len(ranges) * 0.75)
        reversal_pts = ranges[p75_idx] if ranges else 1.0
        reversal_pts = max(0.5, reversal_pts)  # mínimo sensato

    swings = _zigzag_swings(bars, reversal_pts=reversal_pts, lookback=3)

    rotations = []
    if len(swings) >= 2:
        for i in range(1, len(swings)):
            rotations.append(abs(swings[i]["price"] - swings[i - 1]["price"]))

    avg_rotation = sum(rotations) / len(rotations) if rotations else 0.0
    max_rotation = max(rotations) if rotations else 0.0

    # Acceptance distance NADRO = 50% del ritmo actual
    acceptance_distance = avg_rotation * 0.5 if rotations else avg_range * 0.5

    return {
        "tf": tf,
        "n_bars": len(bars),
        "avg_bar_range_pts": round(avg_range, 2),
        "reversal_threshold_pts": round(reversal_pts, 2),
        "n_swings": len(swings),
        "n_rotations": len(rotations),
        "avg_rotation_pts": round(avg_rotation, 2),
        "max_rotation_pts": round(max_rotation, 2),
        "acceptance_distance_pts": round(acceptance_distance, 2),
        "last_swing": swings[-1] if swings else None,
        "last_swing_direction": swings[-1]["type"] if swings else None,
        "first_bar_time": bars[0]["t"],
        "last_bar_time": bars[-1]["t"],
    }


def _detect_compresion(bars: list[dict], price: float,
                       extreme_level: float | None,
                       ritmo_acceptance: float,
                       min_bars: int = 5) -> dict:
    """Ley 10 NADRO — Compresión: precio presiona un extremo sin rebotar.

    Único escenario donde NADRO autoriza ANTICIPAR reversión. Criterio:
    - Las últimas ``min_bars`` barras tienen highs (o lows) dentro de
      ``ritmo_acceptance`` del ``extreme_level``.
    - El rebote intermedio es < 50% del ritmo actual.

    Dirección de compresión:
    - bullish_reversal: compresión sobre un soporte (lows cerca) → anticipar long
    - bearish_reversal: compresión bajo resistencia (highs cerca) → anticipar short
    """
    if not extreme_level or len(bars) < min_bars:
        return {"compressed": False, "reason": "datos insuficientes"}

    recent = bars[-min_bars:]
    tol = max(ritmo_acceptance, 1.0)  # mínimo 1 pt de tolerancia

    # Compresión sobre resistencia: highs cerca del nivel, sin rebote fuerte
    highs_near = [b for b in recent if abs(b["h"] - extreme_level) <= tol]
    lows_reach = max(b["l"] for b in recent) - min(b["l"] for b in recent)

    if len(highs_near) >= min_bars - 1 and lows_reach < ritmo_acceptance and extreme_level >= price:
        return {
            "compressed": True,
            "direction": "bearish_reversal",
            "pressing_level": extreme_level,
            "bars_pressing": len(highs_near),
            "nadro_law": "Ley 10 — autoriza ANTICIPAR reversión a la baja",
        }

    # Compresión sobre soporte: lows cerca del nivel, sin rebote fuerte
    lows_near = [b for b in recent if abs(b["l"] - extreme_level) <= tol]
    highs_reach = max(b["h"] for b in recent) - min(b["h"] for b in recent)

    if len(lows_near) >= min_bars - 1 and highs_reach < ritmo_acceptance and extreme_level <= price:
        return {
            "compressed": True,
            "direction": "bullish_reversal",
            "pressing_level": extreme_level,
            "bars_pressing": len(lows_near),
            "nadro_law": "Ley 10 — autoriza ANTICIPAR reversión al alza",
        }

    return {"compressed": False}


def _analyze_delta_slope(instrument: str, n_ticks: int = 500) -> dict:
    """Pendiente del Delta acumulativo vs pendiente del precio — detección
    de absorción / divergencia NADRO Order Flow.

    Calcula ángulo aproximado (en grados) del delta vs precio sobre los
    últimos N ticks. Si delta > 60° pero precio < 30° → absorción Phase 2.
    """
    data = observer.get_ticks(instrument, n=n_ticks)
    if "error" in data or not data.get("ticks"):
        return {"error": data.get("error", "sin ticks"), "addon_reachable": data.get("addon_reachable", False)}

    ticks = data["ticks"]
    if len(ticks) < 50:
        return {"error": "ticks insuficientes", "n_ticks": len(ticks)}

    # Construir delta acumulativo a partir de los ticks Last
    running_delta = 0
    deltas = []
    prices = []
    for t in ticks:
        if t.get("type") != "Last":
            continue
        # Heurística: si el tick fue ejecutado en ask (aggressive buyer) suma,
        # en bid resta. Como sólo tenemos price+volume, aproximamos por la
        # dirección respecto al tick previo.
        if prices and t["price"] > prices[-1]:
            running_delta += t["vol"]
        elif prices and t["price"] < prices[-1]:
            running_delta -= t["vol"]
        deltas.append(running_delta)
        prices.append(t["price"])

    if len(deltas) < 30:
        return {"error": "Last ticks insuficientes", "n_last": len(deltas)}

    def _slope_angle(ys: list[float]) -> float:
        if len(ys) < 2:
            return 0.0
        dy = ys[-1] - ys[0]
        # Normalizamos: dy / std(ys) da una pendiente relativa
        mean = sum(ys) / len(ys)
        var = sum((v - mean) ** 2 for v in ys) / len(ys)
        std = math.sqrt(var) if var > 0 else 1.0
        normalized = dy / (std * len(ys))
        # Arctan de la pendiente normalizada → ángulo
        return math.degrees(math.atan(normalized * 10))  # escala para interpretabilidad

    delta_angle = _slope_angle(deltas)
    price_angle = _slope_angle(prices)
    divergence = abs(delta_angle - price_angle)

    classification = "coherent"
    if abs(delta_angle) > 45 and abs(price_angle) < 15:
        classification = "absorption_phase2"
    elif (delta_angle > 30 and price_angle < -15) or (delta_angle < -30 and price_angle > 15):
        classification = "strong_divergence"
    elif divergence > 30:
        classification = "moderate_divergence"

    return {
        "n_last_ticks": len(deltas),
        "delta_change": deltas[-1] - deltas[0],
        "price_change": prices[-1] - prices[0],
        "delta_angle_deg": round(delta_angle, 1),
        "price_angle_deg": round(price_angle, 1),
        "angle_divergence_deg": round(divergence, 1),
        "classification": classification,
        "nadro_note": (
            "Phase 2: absorbentes atrapados, buscar fade" if classification == "absorption_phase2"
            else "Delta y precio discrepan fuertemente — posible trampa o giro"
                 if classification == "strong_divergence"
            else "Moderada discrepancia" if classification == "moderate_divergence"
            else "Delta y precio coherentes"
        ),
    }


def _analyze_order_flow(price: float, states: dict[str, dict]) -> dict:
    """O — Order Flow: delta acumulado + divergencia + contexto de sesión."""
    rd = _find_payload(states, "RelativeDelta")
    if not rd:
        return {"error": "RelativeDelta no disponible en Registry"}

    cd = rd.get("cumulative_delta", 0) or 0
    bar_d = rd.get("bar_delta", 0) or 0

    # Clasificación del delta acumulado (niveles ES/MES NADRO)
    if abs(cd) < 2500:
        strength = "weak"
    elif abs(cd) < 5000:
        strength = "moderate"
    elif abs(cd) < 10000:
        strength = "strong"
    elif abs(cd) < 15000:
        strength = "extreme"
    else:
        strength = "capitulation"

    # Sesiones activas
    sessions_active = [
        s for s in ("us", "eu", "asia", "global")
        if rd.get(f"{s}_active")
    ]

    # Heurística simple de divergencia: delta positivo + precio bajo ancla USA = posible absorción
    us_anchor = rd.get("us_anchor")
    divergence_hint = None
    if us_anchor and not math.isnan(us_anchor):
        if cd > 0 and price < us_anchor:
            divergence_hint = "bullish_delta_but_price_below_us_anchor (posible absorción / fade)"
        elif cd < 0 and price > us_anchor:
            divergence_hint = "bearish_delta_but_price_above_us_anchor"

    return {
        "cumulative_delta": cd,
        "bar_delta": bar_d,
        "strength": strength,
        "direction": "positive" if cd > 0 else "negative" if cd < 0 else "neutral",
        "sessions_active": sessions_active,
        "us_anchor": us_anchor,
        "divergence_hint": divergence_hint,
    }


_VWAP_PREFIX_ORDER = {"Y": 1, "Q": 2, "M": 3, "W": 4, "D": 5}
_SEGUNDA_GESTA_TOLERANCE_DEFAULT = 1.0  # 1 punto (= 4 ticks en MES)


def _collapse_segunda_gesta(
    levels: list[dict],
    tolerance: float = _SEGUNDA_GESTA_TOLERANCE_DEFAULT,
) -> list[dict]:
    """NADRO 4.0 — Regla de Segunda Gesta.

    Cuando un ciclo inferior está en su primer período, los LTWVs coinciden y
    NO tienen unicidad operativa. Política: **mostrar solo el TF más granular**
    (D > W > M > Q > Y) y marcar en note qué TFs quedaron ocultos.

    Ejemplo típico día lunes: W-DVA = D-DVA (la semana arrancó el domingo y
    el Daily lleva la misma data acumulada). El brief muestra solo el D-DVAH
    con note "W ocultado por Segunda Gesta".

    Solo aplica entre TFs de la jerarquía VWAP (Y/Q/M/W/D) y dentro del mismo
    sufijo (DVAH, VWAP, DVAL). No afecta TPO / Profile / otras fuentes
    independientes — esas coincidencias SÍ son confluencias reales.
    """
    vwap_hierarchy = set(_VWAP_PREFIX_ORDER.keys())
    vwap_lines: list[dict] = []
    others: list[dict] = []
    for lv in levels:
        label = lv.get("label", "")
        if "-" in label:
            prefix = label.split("-", 1)[0]
            if prefix in vwap_hierarchy:
                vwap_lines.append(lv)
                continue
        others.append(lv)

    # Agrupar por sufijo (DVAH/VWAP/DVAL)
    by_suffix: dict[str, list[dict]] = {}
    for lv in vwap_lines:
        suffix = lv["label"].split("-", 1)[1]
        by_suffix.setdefault(suffix, []).append(lv)

    collapsed: list[dict] = []
    for suffix, group in by_suffix.items():
        group.sort(key=lambda x: x["price"])
        # Cluster consecutivo dentro de tolerance
        clusters: list[list[dict]] = [[group[0]]]
        for lv in group[1:]:
            if abs(lv["price"] - clusters[-1][-1]["price"]) <= tolerance:
                clusters[-1].append(lv)
            else:
                clusters.append([lv])

        for cluster in clusters:
            if len(cluster) == 1:
                collapsed.append(cluster[0])
            else:
                # Conservar el MÁS granular (mayor valor en _VWAP_PREFIX_ORDER)
                cluster.sort(
                    key=lambda lv: _VWAP_PREFIX_ORDER.get(lv["label"].split("-", 1)[0], 99),
                    reverse=True,
                )
                keeper = dict(cluster[0])
                hidden_prefixes = [lv["label"].split("-", 1)[0] for lv in cluster[1:]]
                keeper["nadro_note"] = (
                    f"Segunda Gesta: {', '.join(hidden_prefixes)}-{suffix} "
                    f"ocultados (duplicados sin unicidad, tolerance {tolerance}pts)"
                )
                keeper["hidden_tfs"] = hidden_prefixes
                keeper["hidden_prices"] = [lv["price"] for lv in cluster[1:]]
                collapsed.append(keeper)

    return collapsed + others


def _generate_lineas_arena(price: float, states: dict[str, dict]) -> list[dict]:
    """Niveles relevantes multi-TF ordenados por proximidad al precio, con labels NADRO.

    Aplica Regla de Segunda Gesta para colapsar LTWVs duplicados (sin unicidad).
    """
    levels: list[dict] = []

    for ind_name, label_prefix in [
        ("RelativeAnnualVwap", "Y"),
        ("RelativeQuarterlyVwap", "Q"),
        ("RelativeMonthlyVwap", "M"),
        ("RelativeWeeklyVwap", "W"),
        ("RelativeDailyVwap", "D"),
    ]:
        p = _find_payload(states, ind_name)
        if not p:
            continue
        if p.get("dvah_sd1") is not None:
            levels.append({"price": p["dvah_sd1"], "label": f"{label_prefix}-DVAH", "source": ind_name})
        if p.get("vwap") is not None:
            levels.append({"price": p["vwap"], "label": f"{label_prefix}-VWAP", "source": ind_name})
        if p.get("dval_sd1") is not None:
            levels.append({"price": p["dval_sd1"], "label": f"{label_prefix}-DVAL", "source": ind_name})

    # TPO Value Area — fuente INDEPENDIENTE, no colapsa con VWAPs
    vp = _find_payload(states, "RelativeVolumeProfile")
    if vp:
        if vp.get("vah") is not None:
            levels.append({"price": vp["vah"], "label": "TPO-VAH", "source": "RelativeVolumeProfile"})
        if vp.get("poc") is not None:
            levels.append({"price": vp["poc"], "label": "TPO-POC", "source": "RelativeVolumeProfile"})
        if vp.get("val") is not None:
            levels.append({"price": vp["val"], "label": "TPO-VAL", "source": "RelativeVolumeProfile"})

    # Validación + colapso por Segunda Gesta + distancia al precio
    clean = []
    for lv in levels:
        p = lv["price"]
        if p is None or not isinstance(p, (int, float)):
            continue
        if math.isnan(p) or p <= 0:
            continue
        clean.append(lv)

    collapsed = _collapse_segunda_gesta(clean)

    # Inyectar freshness: usamos el bar_time del indicador más granular (D o W) como ref
    ref_time = None
    for ind_name in ("RelativeDailyVwap", "RelativeWeeklyVwap", "RelativeVwap"):
        payload = _find_payload(states, ind_name)
        if payload and payload.get("bar_time"):
            ref_time = _parse_bar_time(payload["bar_time"])
            if ref_time:
                break
    if ref_time is None:
        ref_time = datetime.utcnow()

    for lv in collapsed:
        lv["distance_pts"] = lv["price"] - price
        lv["distance_abs"] = abs(lv["price"] - price)
        # Freshness: mapear label prefix a TF
        label = lv.get("label", "")
        prefix = label.split("-", 1)[0] if "-" in label else ""
        # Si es merged (ej "W/D-DVAH"), usar el más granular (último en el /)
        if "/" in prefix:
            prefix = prefix.split("/")[-1]
        if prefix in _PERIOD_HOURS:
            lv["freshness"] = _compute_freshness(ref_time, prefix)

    collapsed.sort(key=lambda x: x["distance_abs"])
    return collapsed


def _detect_confluences(lineas: list[dict], tick_size: float = 0.25,
                        tolerance_ticks: int = 8) -> list[dict]:
    """Agrupa niveles dentro de ``tolerance_ticks`` como zonas de confluencia."""
    tol = tick_size * tolerance_ticks
    clusters = []
    sorted_by_price = sorted(lineas, key=lambda x: x["price"])
    for lv in sorted_by_price:
        if clusters and abs(clusters[-1]["center"] - lv["price"]) <= tol:
            clusters[-1]["members"].append(lv["label"])
            clusters[-1]["prices"].append(lv["price"])
            clusters[-1]["center"] = sum(clusters[-1]["prices"]) / len(clusters[-1]["prices"])
        else:
            clusters.append({
                "center": lv["price"],
                "members": [lv["label"]],
                "prices": [lv["price"]],
            })
    # Solo confluencias con 2+ miembros
    confluences = []
    for c in clusters:
        if len(c["members"]) >= 2:
            confluences.append({
                "center": round(c["center"], 2),
                "min": round(min(c["prices"]), 2),
                "max": round(max(c["prices"]), 2),
                "member_count": len(c["members"]),
                "members": c["members"],
            })
    return confluences


def _generate_hypos(price: float, lineas: list[dict], confluences: list[dict],
                    distribucion: dict, tick_size: float = 0.25,
                    point_value: float = 5.0) -> list[dict]:
    """3 hypos NADRO accionables con entry / target / invalidación / RR.

    Lógica:
    - H1 (bullish): rupture del primer nivel arriba. Target = siguiente nivel
      arriba (o confluencia). Invalidación = primer nivel abajo.
    - H2 (bearish): rupture del primer nivel abajo. Target = siguiente nivel
      abajo. Invalidación = primer nivel arriba.
    - H3: fade/mean-reversion desde el extremo más cercano hacia VWAP central.
    """
    aboves = sorted(
        [lv for lv in lineas if lv["distance_pts"] > 0],
        key=lambda x: x["distance_pts"],
    )
    belows = sorted(
        [lv for lv in lineas if lv["distance_pts"] < 0],
        key=lambda x: -x["distance_pts"],
    )

    regime = distribucion.get("regime", "unknown")

    def _pick_confluence_for_level(level_price: float) -> dict | None:
        for c in confluences:
            if c["min"] - tick_size <= level_price <= c["max"] + tick_size:
                return c
        return None

    def _rr_block(entry: float, target: float, stop: float) -> dict:
        risk_pts = abs(entry - stop)
        reward_pts = abs(target - entry)
        rr = reward_pts / risk_pts if risk_pts > 0 else 0.0
        return {
            "entry": round(entry, 2),
            "target": round(target, 2),
            "invalidation": round(stop, 2),
            "risk_pts": round(risk_pts, 2),
            "reward_pts": round(reward_pts, 2),
            "rr_ratio": round(rr, 2),
            "risk_usd": round(risk_pts * point_value, 2),
            "reward_usd": round(reward_pts * point_value, 2),
        }

    hypos: list[dict] = []

    # --- H1 Bullish breakout ---
    if aboves:
        trigger = aboves[0]
        trigger_conf = _pick_confluence_for_level(trigger["price"])
        # Target = siguiente nivel arriba o (fallback) extensión 2R del trigger
        target_lv = aboves[1] if len(aboves) > 1 else None
        # Invalidación = primer nivel abajo o 2 ticks bajo el trigger
        stop_lv = belows[0] if belows else None

        entry = trigger["price"] + tick_size * 2
        stop = (
            stop_lv["price"] - tick_size if stop_lv
            else trigger["price"] - tick_size * 4
        )
        target = target_lv["price"] if target_lv else entry + (entry - stop) * 2

        hypos.append({
            "priority": 1,
            "direction": "long",
            "scenario": (
                f"Rupture bullish de {trigger['label']} @ {trigger['price']:.2f}"
                + (" (confluencia)" if trigger_conf else "")
                + f" → target {target_lv['label'] if target_lv else 'extensión 2R'} "
                + f"@ {target:.2f}. Invalidación {stop_lv['label'] if stop_lv else 'fixed'} @ {stop:.2f}."
            ),
            "trigger_level": trigger["label"],
            "trigger_price": round(trigger["price"], 2),
            "trigger_confluence": trigger_conf["members"] if trigger_conf else None,
            "target_level": target_lv["label"] if target_lv else "ext_2R",
            "invalidation_level": stop_lv["label"] if stop_lv else "fixed_4t",
            "freshness_trigger": trigger.get("freshness", {}).get("freshness_label"),
            **_rr_block(entry, target, stop),
        })

    # --- H2 Bearish breakout ---
    if belows:
        trigger = belows[0]
        trigger_conf = _pick_confluence_for_level(trigger["price"])
        target_lv = belows[1] if len(belows) > 1 else None
        stop_lv = aboves[0] if aboves else None

        entry = trigger["price"] - tick_size * 2
        stop = (
            stop_lv["price"] + tick_size if stop_lv
            else trigger["price"] + tick_size * 4
        )
        target = target_lv["price"] if target_lv else entry - (stop - entry) * 2

        hypos.append({
            "priority": 2,
            "direction": "short",
            "scenario": (
                f"Rupture bearish de {trigger['label']} @ {trigger['price']:.2f}"
                + (" (confluencia)" if trigger_conf else "")
                + f" → target {target_lv['label'] if target_lv else 'extensión 2R'} "
                + f"@ {target:.2f}. Invalidación {stop_lv['label'] if stop_lv else 'fixed'} @ {stop:.2f}."
            ),
            "trigger_level": trigger["label"],
            "trigger_price": round(trigger["price"], 2),
            "trigger_confluence": trigger_conf["members"] if trigger_conf else None,
            "target_level": target_lv["label"] if target_lv else "ext_2R",
            "invalidation_level": stop_lv["label"] if stop_lv else "fixed_4t",
            "freshness_trigger": trigger.get("freshness", {}).get("freshness_label"),
            **_rr_block(entry, target, stop),
        })

    # --- H3 Fade/Rotational ---
    # Buscar VWAP central (D o W) como destino de reversion
    vwap_targets = [lv for lv in lineas if lv["label"].endswith("-VWAP")]
    fade_target = (
        min(vwap_targets, key=lambda x: x["distance_abs"]) if vwap_targets
        else None
    )
    if regime == "rotational" and aboves and belows and fade_target:
        # Desde el extremo más cercano, fade hacia el VWAP
        extremo = aboves[0] if aboves[0]["distance_abs"] <= belows[0]["distance_abs"] else belows[0]
        is_top = extremo["price"] > price
        entry = extremo["price"] - tick_size if is_top else extremo["price"] + tick_size
        stop = (
            extremo["price"] + tick_size * 4 if is_top
            else extremo["price"] - tick_size * 4
        )
        target = fade_target["price"]
        hypos.append({
            "priority": 3,
            "direction": "short" if is_top else "long",
            "scenario": (
                f"Fade de {extremo['label']} @ {extremo['price']:.2f} hacia "
                f"{fade_target['label']} @ {target:.2f} (Extreme Fade rotacional)."
            ),
            "trigger_level": extremo["label"],
            "target_level": fade_target["label"],
            **_rr_block(entry, target, stop),
        })
    else:
        hypos.append({
            "priority": 3,
            "direction": "none",
            "scenario": (
                "Sin setup claro de fade — régimen no rotacional o sin VWAP central "
                "accesible. Respetar inacción (Ley NADRO: la mayor parte del tiempo "
                "no hay oportunidad)."
            ),
        })

    return hypos


def _classify_setup(narrativa: dict, distribucion: dict, order_flow: dict,
                    confluences: list[dict], lineas: list[dict], price: float) -> dict:
    """Clasifica calidad A+/B/C según Leyes NADRO.

    Scoring sobre 5 puntos:
    - Confluencia macro+micro (mismo bias) → +1
    - Régimen imbalance con delta strong → +1
    - Confluencia real (2+ fuentes independientes) cercana (<5pts) → +1
    - Divergencia Order Flow detectable → +1
    - Nivel más cercano es FRESH (score ≥ 0.8, recién arrancado) → +1

    Ley 8 NADRO: la energía se disipa alejándose del origen. Niveles fresh
    cargan más energía que los maduros/expirados.
    """
    score = 0
    reasons = []

    if narrativa.get("confluence_macro_vs_micro") == "confluence":
        score += 1
        reasons.append("confluencia macro+micro en mismo bias")
    elif narrativa.get("confluence_macro_vs_micro") == "dissonance":
        reasons.append("⚠ disonancia macro vs micro — operar con cuidado")

    if distribucion.get("regime") == "imbalance" and order_flow.get("strength") in ("strong", "extreme"):
        score += 1
        reasons.append("imbalance con delta strong")

    nearest_conf = None
    if confluences:
        nearest_conf = min(confluences, key=lambda c: abs(c["center"] - price))
        if abs(nearest_conf["center"] - price) < 5:
            score += 1
            reasons.append(
                f"confluencia cercana ({nearest_conf['member_count']} miembros @ {nearest_conf['center']})"
            )

    if order_flow.get("divergence_hint"):
        score += 1
        reasons.append("divergencia Order Flow")

    # Ley 8: Recency del nivel clave
    recency_bonus = None
    if lineas:
        nearest = lineas[0]
        fresh = nearest.get("freshness", {})
        label = fresh.get("freshness_label")
        if label == "fresh":
            score += 1
            recency_bonus = f"{nearest['label']} es FRESH (age {fresh['age_hours']:.1f}h / {fresh['period_total_hours']}h)"
            reasons.append(recency_bonus)
        elif label == "expired":
            reasons.append(
                f"⚠ {nearest['label']} cerca de expirar — energía disipada (progress {fresh['progress']:.1%})"
            )

    quality = "A+" if score >= 4 else "A" if score == 3 else "B" if score == 2 else "C"

    return {
        "quality": quality,
        "score": score,
        "max_score": 5,
        "reasons": reasons,
        "nearest_confluence": nearest_conf,
        "recency_factor": recency_bonus,
    }


# -----------------------------------------------------------------------------
# Public entry point
# -----------------------------------------------------------------------------


def nadro_snapshot(instrument: str, tf_ritmo: str = "1m", n_bars: int = 60) -> dict:
    """Brief NADRO completo para el instrumento.

    Aplica el acrónimo N-A-D-R-O sobre el estado vivo publicado por los
    indicadores RelativeIndicators + bars vía HTTP.
    """
    # 1. Recolectar data
    states = _fetch_states_by_indicator(instrument)
    if not states:
        return {
            "error": f"no hay indicator states publicados para {instrument}. "
                     f"Verifica que los indicadores estén cargados en charts.",
            "instrument": instrument,
        }

    # Precio actual: usar close del RelativeVwap o RelativeDailyVwap
    price = None
    for key_candidate in ("RelativeVwap", "RelativeDailyVwap", "RelativeDelta"):
        p = _find_payload(states, key_candidate)
        if p and p.get("close"):
            price = p["close"]
            break
    if price is None:
        return {"error": "no se pudo determinar el precio actual", "instrument": instrument}

    # 2. Analizar cada letra NADRO
    narrativa = _analyze_narrativa(price, states)
    distribucion = _analyze_distribucion(states)
    ritmo = _analyze_ritmo(instrument, n_bars=n_bars, tf=tf_ritmo)
    order_flow = _analyze_order_flow(price, states)

    # 2b. Delta slope real (Order Flow refinado)
    delta_slope = _analyze_delta_slope(instrument, n_ticks=500)
    if "error" not in delta_slope:
        order_flow["slope_analysis"] = delta_slope
        # Promover divergencias detectadas al divergence_hint si antes era None
        if delta_slope.get("classification") in ("absorption_phase2", "strong_divergence") and not order_flow.get("divergence_hint"):
            order_flow["divergence_hint"] = delta_slope["nadro_note"]

    # 3. Lineas en la arena + confluencias
    lineas = _generate_lineas_arena(price, states)
    confluences = _detect_confluences(lineas)

    # 3b. Compresión (Ley 10)
    compresion = {"compressed": False, "reason": "sin bars"}
    bars_data = observer.get_bars(instrument, tf=tf_ritmo, n=n_bars)
    if "error" not in bars_data and bars_data.get("bars"):
        # Testear compresión contra el nivel más cercano (arriba y abajo)
        nearest_above = next((lv for lv in lineas if lv["distance_pts"] > 0), None)
        nearest_below = next((lv for lv in lineas if lv["distance_pts"] < 0), None)
        acceptance = ritmo.get("acceptance_distance_pts", 2.0)

        compresion_above = _detect_compresion(
            bars_data["bars"], price,
            nearest_above["price"] if nearest_above else None,
            acceptance,
        )
        compresion_below = _detect_compresion(
            bars_data["bars"], price,
            nearest_below["price"] if nearest_below else None,
            acceptance,
        )
        # Reportar la que detectó compresión
        if compresion_above.get("compressed"):
            compresion = {
                **compresion_above,
                "against_level": nearest_above["label"] if nearest_above else None,
            }
        elif compresion_below.get("compressed"):
            compresion = {
                **compresion_below,
                "against_level": nearest_below["label"] if nearest_below else None,
            }

    # 4. Hypos accionables (con targets + invalidación + RR) + setup quality
    hypos = _generate_hypos(price, lineas, confluences, distribucion)
    setup = _classify_setup(narrativa, distribucion, order_flow, confluences, lineas, price)

    # 4b. Bonus score por compresión (Ley 10)
    if compresion.get("compressed"):
        setup["score"] = min(setup.get("max_score", 5), setup.get("score", 0) + 1)
        setup["reasons"].append(
            f"Ley 10 Compresión detectada contra {compresion.get('against_level')} — "
            f"autoriza ANTICIPAR ({compresion['direction']})"
        )
        setup["quality"] = (
            "A+" if setup["score"] >= 4 else
            "A" if setup["score"] == 3 else
            "B" if setup["score"] == 2 else "C"
        )

    # 5. Brief estructurado
    return {
        "instrument": instrument,
        "price": price,
        "timestamp": datetime.utcnow().isoformat() + "Z",
        "narrativa": narrativa,
        "aceptacion": {
            "acceptance_distance_pts": ritmo.get("acceptance_distance_pts"),
            "nearest_above": lineas[0] if lineas and lineas[0]["distance_pts"] > 0 else None,
            "nearest_below": next(
                (lv for lv in lineas if lv["distance_pts"] < 0), None
            ),
        },
        "distribucion": distribucion,
        "ritmo": ritmo,
        "order_flow": order_flow,
        "compresion": compresion,
        "lineas_arena": lineas[:12],
        "confluences": confluences,
        "hypos": hypos,
        "setup_candidato": setup,
        "indicators_consumed": sorted(set(k.split(":")[0] for k in states.keys())),
    }


# -----------------------------------------------------------------------------
# nadro_detect_fresh_shift — detecta el último cambio de régimen
# -----------------------------------------------------------------------------


def nadro_classify_setup(
    instrument: str,
    direction: str,
    entry: float,
    target: float,
    stop: float,
    size: int = 1,
    tick_size: float = 0.25,
    point_value: float = 5.0,
) -> dict:
    """Evalúa un setup hipotético contra las leyes NADRO.

    Devuelve calidad A+/A/B/C, cumplimiento de leyes, alineación con el
    régimen actual, y recomendaciones específicas.

    Args:
        instrument: "MES 06-26"
        direction: "long" | "short"
        entry: precio de entrada propuesto
        target: take profit
        stop: invalidación
        size: contratos (default 1)
    """
    direction = direction.lower().strip()
    if direction not in ("long", "short"):
        return {"error": "direction debe ser 'long' o 'short'"}

    # Validar geometría según dirección
    if direction == "long":
        if target <= entry or stop >= entry:
            return {
                "error": "long requiere target > entry > stop",
                "provided": {"entry": entry, "target": target, "stop": stop},
            }
    else:
        if target >= entry or stop <= entry:
            return {
                "error": "short requiere target < entry < stop",
                "provided": {"entry": entry, "target": target, "stop": stop},
            }

    # Métricas básicas
    risk_pts = abs(entry - stop)
    reward_pts = abs(target - entry)
    rr = reward_pts / risk_pts if risk_pts > 0 else 0.0
    risk_usd = risk_pts * point_value * size
    reward_usd = reward_pts * point_value * size

    # Fetch contexto del mercado actual
    snapshot = nadro_snapshot(instrument)
    if "error" in snapshot:
        return {"error": snapshot["error"], "setup": {
            "direction": direction, "entry": entry, "target": target, "stop": stop}}

    price = snapshot["price"]
    narrativa = snapshot["narrativa"]
    distribucion = snapshot["distribucion"]
    ritmo = snapshot["ritmo"]
    order_flow = snapshot["order_flow"]
    compresion = snapshot.get("compresion", {})
    lineas = snapshot.get("lineas_arena", [])
    confluences = snapshot.get("confluences", [])

    # Función helper: buscar nivel cercano a un precio
    def _nearest_level(p: float, max_dist_ticks: int = 4) -> dict | None:
        candidates = [lv for lv in lineas if abs(lv["price"] - p) <= tick_size * max_dist_ticks]
        if not candidates:
            return None
        return min(candidates, key=lambda x: abs(x["price"] - p))

    # Proximidad de entry/stop/target a niveles estructurales
    level_proximity = {
        "entry_near_level": _nearest_level(entry),
        "stop_near_level": _nearest_level(stop, max_dist_ticks=6),
        "target_near_level": _nearest_level(target, max_dist_ticks=6),
    }

    # Chequeo de leyes NADRO
    laws: dict[str, dict] = {}

    # Ley 1: El balance es lo único que podemos entender
    # Verificar que el setup tenga referencia a un nivel estructural (no aleatorio)
    laws["ley_1_referencia_estructural"] = {
        "cumple": bool(level_proximity["entry_near_level"] or level_proximity["stop_near_level"]),
        "razon": (
            "Entry o stop tocan nivel estructural reconocido"
            if level_proximity["entry_near_level"] or level_proximity["stop_near_level"]
            else "Entry/stop no alineados con niveles VWAP/TPO — setup arbitrario"
        ),
    }

    # Ley 3: Aceptación fuera de balance → continuación
    # Si el precio está FUERA del DVA y el trade va en dirección del imbalance = cumple
    bias_per_tf = narrativa.get("bias_per_tf", {})
    w_bias = bias_per_tf.get("W", {})
    w_pos = w_bias.get("price_position", "")
    is_imbalance_up = w_pos == "above_dvah"
    is_imbalance_down = w_pos == "below_dval"
    aligned_with_imbalance = (
        (direction == "long" and is_imbalance_up) or
        (direction == "short" and is_imbalance_down)
    )
    laws["ley_3_imbalance_continuation"] = {
        "cumple": aligned_with_imbalance,
        "razon": (
            f"Trade {direction} alineado con imbalance weekly ({w_pos})"
            if aligned_with_imbalance
            else f"Weekly en {w_pos} — trade no sigue imbalance"
                 if w_pos else "Weekly en balance — ley 3 no aplica directamente"
        ),
    }

    # Ley 8: Spectrum de oportunidad (energía se disipa alejándose del origen)
    # Target razonable = no más de 2x el ritmo promedio
    avg_rotation = ritmo.get("avg_rotation_pts", 2.0) or 2.0
    reasonable_target = reward_pts <= avg_rotation * 3
    laws["ley_8_target_realistic"] = {
        "cumple": reasonable_target,
        "target_vs_rotation": f"{reward_pts:.2f}pts vs avg rotación {avg_rotation:.2f}pts",
        "razon": (
            f"Target alcanzable ({reward_pts:.2f}pts ≤ 3× ritmo)"
            if reasonable_target
            else f"Target demasiado ambicioso ({reward_pts:.2f}pts > 3× ritmo {avg_rotation:.2f})"
        ),
    }

    # Ley 10: Compresión autoriza anticipar
    ley_10_active = False
    ley_10_aligned = False
    if compresion.get("compressed"):
        ley_10_active = True
        comp_dir = compresion.get("direction", "")
        # bearish_reversal autoriza short, bullish_reversal autoriza long
        if (direction == "short" and "bearish" in comp_dir) or \
           (direction == "long" and "bullish" in comp_dir):
            ley_10_aligned = True
    laws["ley_10_compresion"] = {
        "active": ley_10_active,
        "aligned": ley_10_aligned,
        "razon": (
            f"✓ Compresión {compresion.get('direction')} autoriza este {direction}"
            if ley_10_aligned
            else f"✗ Compresión {compresion.get('direction')} contraria a {direction}"
                 if ley_10_active
            else "No hay compresión activa — setup debe cumplir otras leyes"
        ),
    }

    # Alineación con bias macro/micro
    alignment = {
        "macro_bias": narrativa.get("macro_bias"),
        "micro_bias": narrativa.get("micro_bias"),
        "trade_direction": direction,
        "aligned_with_macro": (
            (direction == "long" and narrativa.get("macro_bias") == "bullish") or
            (direction == "short" and narrativa.get("macro_bias") == "bearish")
        ),
        "aligned_with_micro": (
            (direction == "long" and narrativa.get("micro_bias") == "bullish") or
            (direction == "short" and narrativa.get("micro_bias") == "bearish")
        ),
    }

    # Alineación con régimen
    regime = distribucion.get("regime", "")
    tactic = distribucion.get("tactic", "")
    aligned_regime = (
        (direction == "short" and "bearish" in tactic) or
        (direction == "long" and "bullish" in tactic)
    )
    alignment["aligned_with_regime"] = aligned_regime
    alignment["regime"] = regime
    alignment["tactic_suggested"] = tactic

    # Scoring final
    score = 0
    reasons: list[str] = []

    # +1: RR ≥ 2
    if rr >= 2.0:
        score += 1
        reasons.append(f"RR {rr:.2f} ≥ 2:1")
    else:
        reasons.append(f"⚠ RR {rr:.2f} < 2:1 — riesgo/beneficio subóptimo")

    # +1: Entry o stop con referencia estructural
    if laws["ley_1_referencia_estructural"]["cumple"]:
        score += 1
        reasons.append(laws["ley_1_referencia_estructural"]["razon"])

    # +1: Alineación con macro bias
    if alignment["aligned_with_macro"]:
        score += 1
        reasons.append(f"Alineado con macro {alignment['macro_bias']}")

    # +1: Alineación con régimen intraday
    if aligned_regime:
        score += 1
        reasons.append(f"Alineado con régimen {regime} ({tactic})")

    # +1: Target realista vs ritmo
    if reasonable_target:
        score += 1
        reasons.append("Target realista vs ritmo")

    # +1: Compresión Ley 10 alineada
    if ley_10_aligned:
        score += 1
        reasons.append("Ley 10 Compresión autoriza este trade")
    elif ley_10_active and not ley_10_aligned:
        reasons.append("⚠ Compresión contraria al trade")

    # +1: Order flow coherente
    cd = order_flow.get("cumulative_delta", 0) or 0
    cd_aligned = (cd > 0 and direction == "long") or (cd < 0 and direction == "short")
    if cd_aligned and order_flow.get("strength") in ("strong", "extreme"):
        score += 1
        reasons.append(f"Delta acumulado {cd:+.0f} apoya el trade")

    max_score = 7
    quality = (
        "A+" if score >= 6 else
        "A" if score >= 5 else
        "B" if score >= 3 else "C"
    )

    # Recomendaciones NADRO según quality
    if quality in ("A+", "A"):
        recommendation = f"TOMAR — {quality} setup, múltiples leyes NADRO cumplidas"
    elif quality == "B":
        recommendation = "OPERAR CON CAUTELA — válido pero no óptimo, considera reducir tamaño"
    else:
        recommendation = "NO OPERAR — mayoría de leyes NADRO sin cumplir, mejor inacción (Ley NADRO)"

    return {
        "instrument": instrument,
        "current_price": price,
        "setup": {
            "direction": direction,
            "entry": entry,
            "target": target,
            "stop": stop,
            "size": size,
        },
        "metrics": {
            "rr_ratio": round(rr, 2),
            "risk_pts": round(risk_pts, 2),
            "reward_pts": round(reward_pts, 2),
            "risk_usd": round(risk_usd, 2),
            "reward_usd": round(reward_usd, 2),
        },
        "laws_nadro": laws,
        "alignment": alignment,
        "level_proximity": level_proximity,
        "quality": quality,
        "score": score,
        "max_score": max_score,
        "reasons": reasons,
        "recommendation": recommendation,
    }


def nadro_detect_fresh_shift(instrument: str, tf: str = "1m", n_bars: int = 200) -> dict:
    """Detecta el último Fresh Condition Shift (balance↔imbalance).

    Recorre las últimas N barras calculando para cada una si el close estaba
    dentro o fuera del Weekly/Daily DVA. Marca el momento de la última
    transición.

    ``balance``     = close dentro del DVA Weekly [DVAL, DVAH]
    ``imbalance``   = close fuera del DVA + distancia > acceptance
    """
    states = _fetch_states_by_indicator(instrument)
    weekly = _find_payload(states, "RelativeWeeklyVwap") or {}
    daily = _find_payload(states, "RelativeDailyVwap") or {}

    # Usar Weekly como referencia principal (NADRO prioritario según jerarquía)
    dvah = weekly.get("dvah_sd1") or daily.get("dvah_sd1")
    dval = weekly.get("dval_sd1") or daily.get("dval_sd1")
    if dvah is None or dval is None:
        return {
            "error": "no hay DVA Weekly/Daily publicado — carga los indicadores",
            "instrument": instrument,
        }

    bars_data = observer.get_bars(instrument, tf=tf, n=n_bars)
    if "error" in bars_data or not bars_data.get("bars"):
        return {"error": bars_data.get("error", "sin bars"), "instrument": instrument}

    bars = bars_data["bars"]
    acceptance = max(1.0, (dvah - dval) * 0.1)  # 10% del ancho del DVA

    def _state_at(close: float) -> str:
        if dval - acceptance <= close <= dvah + acceptance:
            return "balance"
        if close > dvah + acceptance:
            return "imbalance_up"
        if close < dval - acceptance:
            return "imbalance_down"
        return "balance"

    states_history = [(b["t"], _state_at(b["c"])) for b in bars]

    # Encontrar la última transición
    last_shift = None
    for i in range(1, len(states_history)):
        prev_state = states_history[i - 1][1]
        curr_state = states_history[i][1]
        if prev_state != curr_state:
            last_shift = {
                "bar_idx_relative": i,
                "from": prev_state,
                "to": curr_state,
                "timestamp": states_history[i][0],
                "bars_ago": len(states_history) - 1 - i,
            }

    current_state = states_history[-1][1] if states_history else "unknown"

    return {
        "instrument": instrument,
        "tf": tf,
        "dvah_used": dvah,
        "dval_used": dval,
        "acceptance_pts": round(acceptance, 2),
        "current_state": current_state,
        "last_fresh_shift": last_shift,
        "freshness_hint": (
            "Fresh shift muy reciente — energía máxima" if last_shift and last_shift["bars_ago"] < 10
            else "Shift moderadamente reciente" if last_shift and last_shift["bars_ago"] < 30
            else "Estado maduro, energía disipada" if last_shift
            else "Sin transición en el período analizado (régimen estable)"
        ),
        "bars_analyzed": len(bars),
    }
