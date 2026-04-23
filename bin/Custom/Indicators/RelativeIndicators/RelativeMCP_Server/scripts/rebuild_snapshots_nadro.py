"""Reconstruye bias (N) y contexto (D) de los snapshots del 2026-04-21.

Usa el TPO del DIA ANTERIOR (20/04) para inferir el bias forward del dia
actual (21/04), segun metodologia NADRO correcta. NO toca los hipos
(esos son decisiones del trader), solo actualiza:

- bias: derivado de estructura TPO del dia anterior
- regime: day type + contexto multi-TF
- analysis_text: narrativa N + D estructurada
- summary: resumen breve
- nadro_context: dict estructurado con N y D

Uso (desde raiz del proyecto RelativeIndicators):
    python RelativeMCP_Server/scripts/rebuild_snapshots_nadro.py
"""
from __future__ import annotations

import json
import sys
from datetime import datetime
from pathlib import Path

# Asegurar imports desde la raiz del proyecto
REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO))

from RelativeMCP_Server.tools import observer
from RelativeMCP_Server.tools import vwap_levels as vwap_tool
from RelativeMCP_Server.tools import tpo_cva
from RelativeMCP_Server.paths import markups_dir


PIT_HOURS = {
    "MGC": ("08:20", "13:30"),
    "MCL": ("09:00", "14:30"),
    "MES": ("09:30", "16:00"),
    "MNQ": ("09:30", "16:00"),
    "MYM": ("09:30", "16:00"),
    "M2K": ("09:30", "16:00"),
}
TICK_SIZE = {"MGC": 0.1, "MCL": 0.01, "MES": 0.25, "MNQ": 0.25, "MYM": 1.0, "M2K": 0.1}

PREV_DAY = "2026-04-20"
TODAY = "2026-04-21"


def parse_t(t):
    for fmt in ("%Y-%m-%d %H:%M:%S.%f", "%Y-%m-%d %H:%M:%S"):
        try:
            return datetime.strptime(t, fmt)
        except ValueError:
            continue
    return None


def analyze_prev_tpo(bars, pit_start_str, pit_end_str, tick_size):
    """Estructura TPO del dia anterior -> bias forward para HOY."""
    ps = datetime.strptime(f"{PREV_DAY} {pit_start_str}:00", "%Y-%m-%d %H:%M:%S")
    pe = datetime.strptime(f"{PREV_DAY} {pit_end_str}:00", "%Y-%m-%d %H:%M:%S")
    pit = []
    for b in bars:
        t = parse_t(b.get("t", ""))
        if t and ps <= t <= pe:
            pit.append(b)
    if len(pit) < 20:
        return {"error": f"insufficient bars ({len(pit)})", "forward_bias": "neutral"}

    # TPO profile
    vp = tpo_cva.build_volume_profile(pit, bucket_size=tick_size)
    va = tpo_cva.compute_value_area(vp)
    poc = va["poc"]
    vah = va["vah"]
    val = va["val"]

    # Extremes
    hi = max(float(b.get("h", 0) or 0) for b in pit)
    low_vals = [float(b.get("l", 0) or 0) for b in pit if float(b.get("l", 0) or 0) > 0]
    lo = min(low_vals) if low_vals else 0
    close_prev = float(pit[-1].get("c", 0) or 0)

    # Bars que tocaron los extremos
    high_bars = [b for b in pit if float(b.get("h", 0) or 0) >= hi - tick_size]
    low_bars = [b for b in pit if float(b.get("l", 0) or 0) > 0 and float(b.get("l", 0) or 0) <= lo + tick_size]

    poor_high = weak_low = high_excess = low_excess = False
    if high_bars:
        ex_top = sum(
            float(b.get("h", 0) or 0) - max(float(b.get("o", 0) or 0), float(b.get("c", 0) or 0))
            for b in high_bars
        ) / len(high_bars)
        high_excess = ex_top >= tick_size * 4
        poor_high = (not high_excess) and len(high_bars) <= 2
    if low_bars:
        ex_bot = sum(
            min(float(b.get("o", 0) or 0), float(b.get("c", 0) or 0)) - float(b.get("l", 0) or 0)
            for b in low_bars
        ) / len(low_bars)
        low_excess = ex_bot >= tick_size * 4
        weak_low = (not low_excess) and len(low_bars) <= 2

    # Close vs POC
    if close_prev > vah:
        close_pos = "above VAH"
    elif close_prev < val:
        close_pos = "below VAL"
    elif close_prev > poc:
        close_pos = "above POC"
    else:
        close_pos = "below POC"

    # Day type
    va_range = vah - val
    range_pts = hi - lo
    if va_range > 0 and range_pts > va_range * 1.8:
        day_type = "trend day"
    elif va_range > 0 and range_pts < va_range * 1.1:
        day_type = "non-trend / balance"
    else:
        day_type = "normal"

    # Forward bias
    reasons = []
    bias = "neutral"
    if poor_high:
        bias = "bullish"
        reasons.append(f"alto pobre en {hi} -> likely revisitar high manana")
    if weak_low:
        if bias == "bullish":
            bias = "neutral"
            reasons.append(f"+ minimo debil en {lo} = balance, ambos extremos expuestos")
        else:
            bias = "bearish"
            reasons.append(f"minimo debil en {lo} -> likely revisitar low manana")
    if high_excess and not weak_low:
        if bias != "bullish":
            bias = "bearish"
        reasons.append(f"excess en high {hi} -> vendedores confirmados")
    if low_excess and not poor_high:
        if bias != "bearish":
            bias = "bullish"
        reasons.append(f"excess en low {lo} -> compradores confirmados")
    if close_pos == "above VAH":
        reasons.append("cierre above VAH = acceptance bullish corto plazo")
    elif close_pos == "below VAL":
        reasons.append("cierre below VAL = acceptance bearish corto plazo")

    return {
        "prev_date": PREV_DAY,
        "tpo": {
            "poc": round(poc, 4), "vah": round(vah, 4), "val": round(val, 4),
            "range": round(va_range, 2),
        },
        "extremes": {
            "high": round(hi, 4), "low": round(lo, 4), "close": round(close_prev, 4),
        },
        "features": {
            "poor_high": poor_high, "weak_low": weak_low,
            "high_excess": high_excess, "low_excess": low_excess,
            "day_type": day_type, "close_vs_poc": close_pos,
        },
        "forward_bias": bias,
        "reasons": reasons,
    }


def analyze_dva_multi_tf(master, ref_price):
    """D = Developing Value Areas en los 5 TFs."""
    snap = vwap_tool.snapshot(instrument=master)
    levels = []
    for tf in ["Daily", "Weekly", "Monthly", "Quarterly", "Annual"]:
        d = (snap.get("timeframes") or {}).get(tf) or {}
        dvah = d.get("dvah")
        vwapv = d.get("vwap")
        dval = d.get("dval")
        if dvah is None or dval is None:
            continue
        try:
            dvah = float(dvah)
            dval = float(dval)
            vwapv = float(vwapv) if vwapv is not None else None
        except (ValueError, TypeError):
            continue
        if ref_price > dvah:
            pos = f"above DVAH (+{ref_price - dvah:.2f})"
            zone = "above"
        elif ref_price < dval:
            pos = f"below DVAL ({ref_price - dval:.2f})"
            zone = "below"
        else:
            pos = "inside VA"
            zone = "inside"
        levels.append({
            "tf": tf, "dvah": round(dvah, 4),
            "vwap": round(vwapv, 4) if vwapv else None,
            "dval": round(dval, 4), "position": pos, "zone": zone,
        })
    above = sum(1 for x in levels if x["zone"] == "above")
    below = sum(1 for x in levels if x["zone"] == "below")
    inside = sum(1 for x in levels if x["zone"] == "inside")
    if above >= 3:
        ctx = f"precio extendido arriba en {above}/5 TFs - bias mean-revert bearish corto plazo"
    elif below >= 3:
        ctx = f"precio extendido abajo en {below}/5 TFs - bias mean-revert bullish corto plazo"
    elif inside == len(levels) and levels:
        ctx = "precio dentro del VA en todos los TFs - rotacion"
    else:
        ctx = f"mixed: {above} above / {inside} inside / {below} below (tension multi-TF)"
    return {
        "levels": levels, "above": above, "inside": inside, "below": below,
        "contextual": ctx,
    }


def rebuild_snapshot(master):
    """Actualiza el snapshot de {master}_{TODAY}.json con N + D corregidos."""
    full = f"{master} 06-26"
    bars_resp = observer.get_bars(instrument=full, tf="1m", n=3000)
    if "error" in bars_resp:
        return {"error": f"bars feed: {bars_resp['error']}"}
    bars = bars_resp.get("bars", [])

    po, pc = PIT_HOURS[master]
    tick = TICK_SIZE[master]
    prev_tpo = analyze_prev_tpo(bars, po, pc, tick)
    if "error" in prev_tpo:
        return {"error": prev_tpo["error"]}

    mpath = markups_dir() / f"{master}_{TODAY}.json"
    if not mpath.is_file():
        return {"error": f"no snapshot at {mpath}"}
    doc = json.loads(mpath.read_text(encoding="utf-8"))

    changes = []
    for snap in doc.get("snapshots", []):
        ref_price = snap.get("price_at_analysis") or prev_tpo["extremes"]["close"]
        try:
            ref_price = float(ref_price)
        except (ValueError, TypeError):
            ref_price = prev_tpo["extremes"]["close"]

        dva = analyze_dva_multi_tf(master, ref_price)

        old_bias = snap.get("bias")
        snap["bias"] = prev_tpo["forward_bias"]
        snap["regime"] = f"day type previo: {prev_tpo['features']['day_type']} | {dva['contextual']}"
        snap["nadro_context"] = {
            "N_forward_bias": prev_tpo["forward_bias"],
            "N_prev_day_tpo": prev_tpo,
            "D_multi_tf_dva": dva,
        }

        n_text = []
        n_text.append(f"**N (bias forward desde TPO del {PREV_DAY})**: {prev_tpo['forward_bias'].upper()}")
        for r in prev_tpo["reasons"]:
            n_text.append(f"  - {r}")
        if not prev_tpo["reasons"]:
            n_text.append("  - estructura neutral (sin alto pobre ni minimo debil marcados)")
        t = prev_tpo["tpo"]
        e = prev_tpo["extremes"]
        f = prev_tpo["features"]
        n_text.append(f"**TPO prev**: POC {t['poc']} / VAH {t['vah']} / VAL {t['val']} (rango {t['range']})")
        n_text.append(f"**Extremos prev**: H {e['high']} / L {e['low']} / C {e['close']} ({f['close_vs_poc']})")
        n_text.append(f"**Dia tipo**: {f['day_type']}")
        n_text.append("")
        n_text.append(f"**D (DVAs multi-TF vs precio {ref_price})**: {dva['contextual']}")
        for lv in dva["levels"]:
            vwap_s = f"VWAP {lv['vwap']}" if lv["vwap"] is not None else "VWAP -"
            n_text.append(f"  - {lv['tf']}: DVAH {lv['dvah']} / {vwap_s} / DVAL {lv['dval']} -> {lv['position']}")

        # Preservar analisis manual previo si existe
        prev_at = snap.get("analysis_text", "") or ""
        if prev_at and not prev_at.startswith("**N (bias forward"):
            n_text.append("")
            n_text.append("**Analisis manual original (pre-open)**:")
            n_text.append(prev_at)

        snap["analysis_text"] = "\n".join(n_text)

        flags = []
        if f["poor_high"]: flags.append("poor_high")
        if f["weak_low"]: flags.append("weak_low")
        if f["high_excess"]: flags.append("high_excess")
        if f["low_excess"]: flags.append("low_excess")
        flags_str = ",".join(flags) if flags else "none"
        snap["summary"] = (
            f"N={prev_tpo['forward_bias']} ({f['day_type']}, {flags_str}) | "
            f"D={dva['above']}↑/{dva['inside']}◆/{dva['below']}↓"
        )

        changes.append({
            "snapshot_id": snap.get("id"),
            "old_bias": old_bias,
            "new_bias": prev_tpo["forward_bias"],
            "day_type": f["day_type"],
            "flags": flags_str,
            "dva_context": dva["contextual"],
        })

    mpath.write_text(json.dumps(doc, indent=2, ensure_ascii=False), encoding="utf-8")
    return {"instrument": master, "changes": changes}


def main():
    print(f"{'Inst':5} {'OldBias':10} {'NewBias':10} {'DayType':22} {'Features':35} {'DVA context':45}")
    print("-" * 130)
    for master in ["MGC", "MCL", "MES", "MNQ", "MYM", "M2K"]:
        r = rebuild_snapshot(master)
        if "error" in r:
            print(f"{master:5} ERROR: {r['error']}")
            continue
        for c in r["changes"]:
            old = c["old_bias"] or "-"
            print(f"{master:5} {old:10} {c['new_bias']:10} "
                  f"{c['day_type'][:22]:22} {c['flags'][:35]:35} {c['dva_context'][:45]:45}")
    print("\nOK - snapshots actualizados.")


if __name__ == "__main__":
    main()
