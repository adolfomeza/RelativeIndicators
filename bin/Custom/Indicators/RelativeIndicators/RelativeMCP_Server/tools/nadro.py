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
from datetime import datetime
from typing import Any

from . import observer
from . import vwap_levels


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


def _analyze_ritmo(instrument: str, n_bars: int = 20, tf: str = "1m") -> dict:
    """R — Ritmo: rotaciones dinámicas de las últimas N barras."""
    bars_data = observer.get_bars(instrument, tf=tf, n=n_bars)
    if "error" in bars_data or not bars_data.get("bars"):
        return {
            "error": bars_data.get("error", "sin bars"),
            "addon_reachable": bars_data.get("addon_reachable", False),
        }

    bars = bars_data["bars"]
    ranges = [b["h"] - b["l"] for b in bars]
    avg_range = sum(ranges) / len(ranges) if ranges else 0.0
    max_range = max(ranges) if ranges else 0.0
    min_range = min(ranges) if ranges else 0.0

    # Rotación neta = recorrido total (suma de swings)
    closes = [b["c"] for b in bars]
    swings = sum(abs(closes[i] - closes[i - 1]) for i in range(1, len(closes)))

    # Regla 50%: "aceptación" = desplazamiento mínimo para considerar válida una transición.
    acceptance_distance = avg_range * 0.5

    return {
        "tf": tf,
        "n_bars": len(bars),
        "avg_bar_range_pts": avg_range,
        "max_bar_range_pts": max_range,
        "min_bar_range_pts": min_range,
        "cumulative_swings_pts": swings,
        "acceptance_distance_pts": acceptance_distance,
        "first_bar_time": bars[0]["t"],
        "last_bar_time": bars[-1]["t"],
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
    for lv in collapsed:
        lv["distance_pts"] = lv["price"] - price
        lv["distance_abs"] = abs(lv["price"] - price)

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


def _generate_hypos(price: float, lineas: list[dict], distribucion: dict) -> list[dict]:
    """3 hypos NADRO basados en niveles arriba/abajo y régimen actual."""
    aboves = [lv for lv in lineas if lv["distance_pts"] > 0][:3]
    belows = [lv for lv in lineas if lv["distance_pts"] < 0][:3]

    regime = distribucion.get("regime", "unknown")
    tactic = distribucion.get("tactic", "")

    hypos = []
    if aboves:
        h1_level = aboves[0]
        hypos.append({
            "priority": 1,
            "scenario": (
                f"Test de {h1_level['label']} en {h1_level['price']:.2f}. "
                + (
                    f"Si acepta fuera → BPB bullish continuation. "
                    f"Si rechaza → EF (Extreme Fade) hacia el VWAP/POC."
                    if regime == "rotational"
                    else f"IPB bullish si respeta la resistencia sin quiebre."
                )
            ),
            "target_up": h1_level["price"],
            "target_down": belows[0]["price"] if belows else None,
        })

    if belows:
        h2_level = belows[0]
        hypos.append({
            "priority": 2,
            "scenario": (
                f"Test de {h2_level['label']} en {h2_level['price']:.2f}. "
                + (
                    f"Si acepta abajo → BPB bearish. Si rechaza → EF hacia VWAP."
                    if regime == "rotational"
                    else f"IPB bearish si respeta el soporte."
                )
            ),
            "target_down": h2_level["price"],
            "target_up": aboves[0]["price"] if aboves else None,
        })

    # H3 — fallback / errático
    hypos.append({
        "priority": 3,
        "scenario": (
            "Mercado errático sin dirección — respetar inacción, esperar "
            "resolución hacia extremos."
        ),
        "regime": regime,
        "tactic": tactic,
    })
    return hypos


def _classify_setup(narrativa: dict, distribucion: dict, order_flow: dict,
                    confluences: list[dict], price: float) -> dict:
    """Clasifica calidad A+/B/C según Leyes NADRO."""
    # A+: cambio de condición fresh (no implementable sin historial)
    # Por ahora heurística simple:
    # - Confluence macro+micro = +1 punto
    # - Imbalance régimen con delta strong = +1 punto
    # - Distance al nivel más cercano < 0.5 * acceptance = +1 punto (zona de decisión)
    # - Divergencia OF = +1 punto para contra-trade

    score = 0
    reasons = []

    if narrativa.get("confluence_macro_vs_micro") == "confluence":
        score += 1
        reasons.append("confluencia macro+micro en mismo bias")
    elif narrativa.get("confluence_macro_vs_micro") == "dissonance":
        reasons.append("disonancia macro vs micro — operar con cuidado")

    if distribucion.get("regime") == "imbalance" and order_flow.get("strength") in ("strong", "extreme"):
        score += 1
        reasons.append("imbalance con delta strong")

    nearest_conf = None
    if confluences:
        nearest_conf = min(confluences, key=lambda c: abs(c["center"] - price))
        if abs(nearest_conf["center"] - price) < 5:
            score += 1
            reasons.append(f"confluencia cercana ({nearest_conf['member_count']} miembros @ {nearest_conf['center']})")

    if order_flow.get("divergence_hint"):
        score += 1
        reasons.append("divergencia Order Flow")

    quality = "A+" if score >= 3 else "B" if score == 2 else "C"

    return {
        "quality": quality,
        "score": score,
        "reasons": reasons,
        "nearest_confluence": nearest_conf,
    }


# -----------------------------------------------------------------------------
# Public entry point
# -----------------------------------------------------------------------------


def nadro_snapshot(instrument: str, tf_ritmo: str = "1m", n_bars: int = 20) -> dict:
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

    # 3. Lineas en la arena + confluencias
    lineas = _generate_lineas_arena(price, states)
    confluences = _detect_confluences(lineas)

    # 4. Hypos + setup quality
    hypos = _generate_hypos(price, lineas, distribucion)
    setup = _classify_setup(narrativa, distribucion, order_flow, confluences, price)

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
        "lineas_arena": lineas[:12],  # top 12 más cercanas
        "confluences": confluences,
        "hypos": hypos,
        "setup_candidato": setup,
        "indicators_consumed": sorted(set(k.split(":")[0] for k in states.keys())),
    }
