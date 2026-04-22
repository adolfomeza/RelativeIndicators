"""Recomputa stops estructurales NADRO + ajusta risk + RR de targets.

Rule NADRO (por prioridad):
1. Pivote/swing +- 1 tick (requiere bars pre-pit para identificar swing)
2. Siguiente nivel estructural en direccion adversa (del snapshot levels)
3. 25% del ancho pDVA como fallback
4. Minimo floor: >= 0.5 x ATR aprox o max(50% rango overnight, 25% pDVA)

Implementacion practica:
- LONG: stop = min(siguiente nivel estructural por debajo, low pre-pit) - 1 tick
- SHORT: stop = max(siguiente nivel estructural por encima, high pre-pit) + 1 tick
- Floor: stop NO puede ser mas ajustado que 0.5 * max(overnight_range, ATR_daily)
  Si sale mas ajustado que el floor, se expande al floor (stop estructural correcto
  segun NADRO, aunque implica reducir sizing).

Outputs:
- Actualiza snapshot JSON con nuevos stops + risk_pts
- Recalcula RR de cada target
- Conserva notes + agrega entry 'stop_reasoning' explicando la logica
"""
from __future__ import annotations

import json
import sys
from datetime import datetime
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO))

from RelativeMCP_Server.tools import observer
from RelativeMCP_Server.paths import markups_dir


PIT_HOURS = {
    "MGC": ("08:20", "13:30"),
    "MCL": ("09:00", "14:30"),
    "MES": ("09:30", "16:00"),
    "MNQ": ("09:30", "16:00"),
    "MYM": ("09:30", "16:00"),
    "M2K": ("09:30", "16:00"),
}
TICK = {"MGC": 0.1, "MCL": 0.01, "MES": 0.25, "MNQ": 0.25, "MYM": 1.0, "M2K": 0.1}
# Floor = % del precio bajo el cual NADRO considera "stop muy tight"
# Calibrado por memory nadro_stops_vs_range: al menos 50% rango overnight o 25% pDVA
MIN_STOP_PCT = {
    "MGC": 0.0025,   # 0.25% price = ~12 pts @ 4800
    "MCL": 0.0020,   # 0.20% price = ~0.18 @ 90
    "MES": 0.0015,   # 0.15% = ~10 pts @ 7150
    "MNQ": 0.0020,   # 0.20% = ~54 pts @ 26800
    "MYM": 0.0020,   # 0.20% = ~100 pts @ 49900
    "M2K": 0.0025,   # 0.25% = ~7 pts @ 2815
}
TARGET_DATE = "2026-04-21"


def parse_t(t):
    for fmt in ("%Y-%m-%d %H:%M:%S.%f", "%Y-%m-%d %H:%M:%S"):
        try:
            return datetime.strptime(t, fmt)
        except ValueError:
            continue
    return None


def get_overnight_range(bars, pit_start_str):
    """Rango overnight: desde 17:00 prev day hasta pit_start."""
    pit_start = datetime.strptime(f"{TARGET_DATE} {pit_start_str}:00", "%Y-%m-%d %H:%M:%S")
    # 17:00 del dia anterior
    from datetime import timedelta
    on_start = (pit_start.replace(hour=17, minute=0, second=0) - timedelta(days=1))
    on_bars = []
    for b in bars:
        t = parse_t(b.get("t", ""))
        if t and on_start <= t < pit_start:
            on_bars.append(b)
    if not on_bars:
        return {"high": 0, "low": 0, "range": 0}
    hi = max(float(b.get("h", 0) or 0) for b in on_bars)
    lows = [float(b.get("l", 0) or 0) for b in on_bars if float(b.get("l", 0) or 0) > 0]
    lo = min(lows) if lows else 0
    return {"high": hi, "low": lo, "range": hi - lo}


def compute_structural_stop(h, levels_sorted, direction, entry, overnight, tick_size, min_stop_pct, price):
    """Compute stop estructural NADRO.

    direction: 'long' o 'short'
    levels_sorted: lista ordenada asc de (price, label) de niveles del snapshot
    overnight: dict con high/low/range
    Returns: {stop, reasoning, fallback_reason}
    """
    reasoning = []
    min_stop = price * min_stop_pct  # floor dinamico

    if direction == "long":
        # Niveles debajo del entry (posibles soportes estructurales)
        below = [(p, l) for p, l in levels_sorted if p < entry]
        stop_cand = None
        if below:
            # Tomar el mas cercano por debajo = invalidation si se rompe ese nivel
            nearest_p, nearest_l = below[-1]
            stop_cand = nearest_p - tick_size
            reasoning.append(f"Debajo del nearest support {nearest_l}={nearest_p}")

        # Tambien considerar overnight low - tick
        on_low_stop = overnight["low"] - tick_size if overnight["low"] > 0 else None
        if on_low_stop and (stop_cand is None or on_low_stop < stop_cand):
            # overnight low es soporte tecnico mas fuerte
            stop_cand = on_low_stop
            reasoning.append(f"Debajo de overnight-low {overnight['low']}")

        if stop_cand is None:
            # Fallback: % del price
            stop_cand = entry - max(overnight["range"] * 0.5, min_stop)
            reasoning.append(f"Fallback: max(50% ON range, {min_stop_pct*100:.2f}% price)")

        risk = entry - stop_cand
        if risk < min_stop:
            stop_cand = entry - min_stop
            risk = min_stop
            reasoning.append(f"Expandido a floor {min_stop_pct*100:.2f}% price = {min_stop:.2f} pts")

    else:  # short
        above = [(p, l) for p, l in levels_sorted if p > entry]
        stop_cand = None
        if above:
            nearest_p, nearest_l = above[0]
            stop_cand = nearest_p + tick_size
            reasoning.append(f"Arriba del nearest resistance {nearest_l}={nearest_p}")

        on_high_stop = overnight["high"] + tick_size if overnight["high"] > 0 else None
        if on_high_stop and (stop_cand is None or on_high_stop > stop_cand):
            stop_cand = on_high_stop
            reasoning.append(f"Arriba de overnight-high {overnight['high']}")

        if stop_cand is None:
            stop_cand = entry + max(overnight["range"] * 0.5, min_stop)
            reasoning.append(f"Fallback: max(50% ON range, {min_stop_pct*100:.2f}% price)")

        risk = stop_cand - entry
        if risk < min_stop:
            stop_cand = entry + min_stop
            risk = min_stop
            reasoning.append(f"Expandido a floor {min_stop_pct*100:.2f}% price = {min_stop:.2f} pts")

    return {
        "stop": round(stop_cand, 4),
        "risk_pts": round(risk, 4),
        "reasoning": " | ".join(reasoning),
    }


def rebuild_instrument(master):
    full = f"{master} 06-26"
    bars_resp = observer.get_bars(instrument=full, tf="1m", n=3000)
    if "error" in bars_resp:
        return {"error": bars_resp["error"]}
    bars = bars_resp.get("bars", [])

    po, _pc = PIT_HOURS[master]
    tick = TICK[master]
    min_pct = MIN_STOP_PCT[master]
    overnight = get_overnight_range(bars, po)

    mpath = markups_dir() / f"{master}_{TARGET_DATE}.json"
    if not mpath.is_file():
        return {"error": f"no snapshot"}
    doc = json.loads(mpath.read_text(encoding="utf-8"))

    changes = []
    for snap in doc.get("snapshots", []):
        levels = snap.get("levels", []) or []
        # Sort niveles ascendente por precio
        levels_sorted = sorted([(float(lv["price"]), lv["label"]) for lv in levels if lv.get("price")])

        price = snap.get("price_at_analysis") or (
            levels_sorted[len(levels_sorted) // 2][0] if levels_sorted else 0
        )

        for h in snap.get("hypos", []):
            direction = (h.get("direction") or "").lower()
            entry = float(h.get("entry") or 0)
            old_stop = h.get("stop")
            old_risk = h.get("risk_pts")
            if not entry or direction not in ("long", "short"):
                continue

            result = compute_structural_stop(
                h, levels_sorted, direction, entry, overnight, tick, min_pct, price
            )
            new_stop = result["stop"]
            new_risk = result["risk_pts"]

            # Recalcular RR de targets con nuevo risk
            for t in h.get("targets", []) or []:
                tp = float(t.get("price") or 0)
                if tp and new_risk:
                    reward = abs(tp - entry)
                    t["rr"] = round(reward / new_risk, 2)

            h["stop"] = new_stop
            h["risk_pts"] = round(new_risk, 2)
            h["stop_reasoning"] = result["reasoning"]

            changes.append({
                "hypo": h.get("id"),
                "direction": direction,
                "entry": entry,
                "old_stop": old_stop,
                "new_stop": new_stop,
                "old_risk": old_risk,
                "new_risk": new_risk,
                "reasoning": result["reasoning"],
            })

    mpath.write_text(json.dumps(doc, indent=2, ensure_ascii=False), encoding="utf-8")
    return {"instrument": master, "overnight": overnight, "changes": changes}


def main():
    print(f"{'Inst':5} {'H':3} {'Dir':5} {'Entry':10} {'OldStop':10} {'NewStop':10} {'OldRisk':8} {'NewRisk':8}  Reasoning")
    print("-" * 145)
    for master in ["MGC", "MCL", "MES", "MNQ", "MYM", "M2K"]:
        r = rebuild_instrument(master)
        if "error" in r:
            print(f"{master}: ERROR {r['error']}")
            continue
        for c in r["changes"]:
            print(f"{master:5} {c['hypo']:3} {c['direction'][:5]:5} "
                  f"{c['entry']:10.2f} {c['old_stop']:10} {c['new_stop']:10} "
                  f"{str(c['old_risk'])[:8]:8} {c['new_risk']:8.2f}  {c['reasoning'][:80]}")


if __name__ == "__main__":
    main()
