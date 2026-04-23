"""Blind snapshot generator — reconstructs NADRO pre-market analysis for 3 phases
(context, pre_open, eod) across 6 instruments, using ONLY historical bars
up to a cutoff time. Does NOT depend on live indicator state.

Used for the 2026-04-21 blind validation exercise: 18 snapshots generated
after-the-fact to compare vs the morning 07:00 originals and walk-forward
outcomes.
"""
from __future__ import annotations

import math
from datetime import datetime, timedelta
from typing import Any

from . import observer, tpo_cva, vwap_levels, markup


# Pit session mapping (mirrored from RelativeVolumeProfile.cs)
PIT_SESSIONS = {
    "MES": ("09:30", "16:00"),
    "MNQ": ("09:30", "16:00"),
    "MYM": ("09:30", "16:00"),
    "M2K": ("09:30", "16:00"),
    "MGC": ("08:20", "13:30"),
    "MCL": ("09:00", "14:30"),
}

# Phase timings (VET = ET)
PHASE_TIMES = {
    "MGC": {"context": "06:30", "pre_open": "08:15", "eod": "13:30"},
    "MCL": {"context": "07:00", "pre_open": "08:55", "eod": "14:30"},
    "MES": {"context": "07:00", "pre_open": "09:25", "eod": "16:00"},
    "MNQ": {"context": "07:00", "pre_open": "09:25", "eod": "16:00"},
    "MYM": {"context": "07:00", "pre_open": "09:25", "eod": "16:00"},
    "M2K": {"context": "07:00", "pre_open": "09:25", "eod": "16:00"},
}

TICK_SIZES = {
    "MES": 0.25, "MNQ": 0.25, "MYM": 1.0, "M2K": 0.10,
    "MGC": 0.10, "MCL": 0.01,
}

CONTRACT_SUFFIX = "06-26"


# --------------------------------------------------------------------------
# Time helpers
# --------------------------------------------------------------------------

def _parse_dt(s: str) -> datetime:
    for fmt in ("%Y-%m-%d %H:%M:%S.%f", "%Y-%m-%d %H:%M:%S"):
        try:
            return datetime.strptime(s, fmt)
        except ValueError:
            continue
    return datetime.fromisoformat(s)


def _prev_trading_day(date_str: str) -> str:
    """Returns YYYY-MM-DD of prior business day."""
    d = datetime.strptime(date_str, "%Y-%m-%d")
    days_back = 1
    if d.weekday() == 0:  # Monday -> Friday
        days_back = 3
    elif d.weekday() == 6:  # Sunday -> Friday
        days_back = 2
    return (d - timedelta(days=days_back)).strftime("%Y-%m-%d")


# --------------------------------------------------------------------------
# Core computations
# --------------------------------------------------------------------------

def compute_developing_daily_vwap(bars: list[dict], cutoff_ts: datetime) -> dict:
    """Session-anchored VWAP (from previous day 17:00 ET) with stddev bands.

    Returns {vwap, dvah, dval, n_bars}.
    """
    # Session start: previous day 17:00 ET (Globex rollover)
    session_date = cutoff_ts.date()
    if cutoff_ts.hour < 17:
        session_start = datetime.combine(session_date - timedelta(days=1),
                                          datetime.strptime("17:00", "%H:%M").time())
    else:
        session_start = datetime.combine(session_date,
                                          datetime.strptime("17:00", "%H:%M").time())

    filtered = []
    for b in bars:
        bt = _parse_dt(b["t"])
        if session_start <= bt <= cutoff_ts:
            filtered.append(b)

    if not filtered:
        return {"vwap": 0.0, "dvah": 0.0, "dval": 0.0, "n_bars": 0}

    tv = 0.0
    tpv = 0.0
    for b in filtered:
        typical = (b["h"] + b["l"] + b["c"]) / 3.0
        v = max(b.get("v", 0), 0)
        tv += v
        tpv += typical * v
    if tv <= 0:
        # Equal-weight fallback
        n = len(filtered)
        tpv = sum((b["h"] + b["l"] + b["c"]) / 3.0 for b in filtered)
        vwap = tpv / n
        sq = sum(((b["h"] + b["l"] + b["c"]) / 3.0 - vwap) ** 2 for b in filtered)
        sd = math.sqrt(sq / n) if n else 0
    else:
        vwap = tpv / tv
        sq = 0.0
        for b in filtered:
            typical = (b["h"] + b["l"] + b["c"]) / 3.0
            v = max(b.get("v", 0), 0)
            sq += v * (typical - vwap) ** 2
        sd = math.sqrt(sq / tv)

    return {
        "vwap": round(vwap, 4),
        "dvah": round(vwap + sd, 4),
        "dval": round(vwap - sd, 4),
        "n_bars": len(filtered),
    }


def compute_pit_tpo(bars_30m: list[dict], pit_start_ts: datetime, pit_end_ts: datetime,
                     bucket_size: float = 0.25) -> dict:
    """Build pit-session volume profile and return POC/VAH/VAL."""
    filtered = []
    for b in bars_30m:
        bt = _parse_dt(b["t"])
        if pit_start_ts <= bt < pit_end_ts:
            filtered.append(b)
    if not filtered:
        return {"poc": 0, "vah": 0, "val": 0, "n_bars": 0}
    profile = tpo_cva.build_volume_profile(filtered, bucket_size=bucket_size)
    va = tpo_cva.compute_value_area(profile, va_pct=0.70)
    va["n_bars"] = len(filtered)
    return va


def read_historical_zones(master: str) -> dict[str, list[dict]]:
    """Read prior zones from VwapLevels files across all timeframes."""
    out: dict[str, list[dict]] = {}
    for tf in ("Daily", "Weekly", "Monthly", "Quarterly", "Annual"):
        data = vwap_levels.read_vwap_levels(master, tf)
        if "error" in data:
            continue
        out[tf] = data.get("zones", [])
        # Also attach current-developing DVA from the file — they don't change
        # "with cutoff" for Weekly/Monthly/... within the same session.
        out[f"{tf}_current"] = {
            "dvah": data.get("dvah"),
            "vwap": data.get("vwap"),
            "dval": data.get("dval"),
        }
    return out


# --------------------------------------------------------------------------
# Level + confluence assembly
# --------------------------------------------------------------------------

_TF_PREFIX = {
    "Daily": ("d", "pD"),      # dDVAH / pDVAH
    "Weekly": ("w", "pW"),
    "Monthly": ("m", "pM"),
    "Quarterly": ("q", "pQ"),
    "Annual": ("y", "pY"),
}


def _build_levels(master: str, price: float, dev_daily: dict, tpo: dict,
                  zones_by_tf: dict, price_band_pct: float = 0.05) -> list[dict]:
    """Return list of {label, price} levels within ±price_band_pct of price."""
    levels: list[dict] = []
    band = price * price_band_pct

    def _add(label: str, p: float):
        if not p:
            return
        if abs(p - price) > band:
            return
        levels.append({"label": label, "price": round(p, 4)})

    # Developing daily — these are the live VWAP bands
    _add("DVAH", dev_daily.get("dvah", 0))
    _add("VWAP", dev_daily.get("vwap", 0))
    _add("DVAL", dev_daily.get("dval", 0))

    # Higher-TF developing (from file snapshots)
    for tf in ("Weekly", "Monthly", "Quarterly", "Annual"):
        curr = zones_by_tf.get(f"{tf}_current") or {}
        prefix, _ = _TF_PREFIX[tf]
        _add(f"{prefix}DVAH", curr.get("dvah"))
        _add(f"{prefix}VWAP", curr.get("vwap"))
        _add(f"{prefix}DVAL", curr.get("dval"))

    # TPO pit
    _add("TPO-POC", tpo.get("poc", 0))
    _add("TPO-VAH", tpo.get("vah", 0))
    _add("TPO-VAL", tpo.get("val", 0))

    # Historical zones (past sessions)
    for tf in ("Daily", "Weekly", "Monthly", "Quarterly", "Annual"):
        zones = zones_by_tf.get(tf) or []
        _, zpref = _TF_PREFIX[tf]
        for z in zones:
            # Best-effort date from start_time
            date_label = ""
            try:
                dt = _parse_dt(z["start_time"])
                date_label = dt.strftime("-%d/%m")
            except Exception:
                pass
            _add(f"{zpref}DVAH{date_label}", z.get("upper"))
            _add(f"{zpref}DVAL{date_label}", z.get("lower"))

    return levels


def _build_confluences(levels: list[dict], tick_size: float, thresh_ticks: int = 20) -> list[dict]:
    """Cluster levels into confluences. Sort by price, greedy consecutive grouping."""
    if not levels:
        return []
    tol = tick_size * thresh_ticks
    sorted_lv = sorted(levels, key=lambda x: x["price"])
    groups: list[list[dict]] = []
    cur: list[dict] = [sorted_lv[0]]
    for lv in sorted_lv[1:]:
        if lv["price"] - cur[-1]["price"] <= tol:
            cur.append(lv)
        else:
            if len(cur) >= 2:
                groups.append(cur)
            cur = [lv]
    if len(cur) >= 2:
        groups.append(cur)

    confluences: list[dict] = []
    for g in groups:
        n = len(g)
        if n >= 4:
            grade = "A+"
        elif n >= 3:
            grade = "A"
        else:
            grade = "B"
        prices = [lv["price"] for lv in g]
        confluences.append({
            "label": " + ".join(lv["label"] for lv in g),
            "price_min": round(min(prices), 4),
            "price_max": round(max(prices), 4),
            "grade": grade,
            "members": [lv["label"] for lv in g],
        })
    return confluences


# --------------------------------------------------------------------------
# Hypothesis generation
# --------------------------------------------------------------------------

def _detect_bias(price: float, dev: dict, tpo: dict) -> str:
    dvah = dev.get("dvah", 0)
    dval = dev.get("dval", 0)
    if dvah and price > dvah:
        return "long"
    if dval and price < dval:
        return "short"
    # Use TPO as tiebreaker
    poc = tpo.get("poc", 0)
    if poc and price > poc:
        return "long-bias"
    if poc and price < poc:
        return "short-bias"
    return "neutral"


def _pick_targets(entry: float, direction: str, levels: list[dict], risk: float,
                   max_targets: int = 3) -> list[dict]:
    """Pick up to N levels in direction as targets; compute RR per target."""
    if risk <= 0:
        return []
    candidates = []
    for lv in levels:
        p = lv["price"]
        if direction == "long" and p > entry:
            candidates.append(lv)
        elif direction == "short" and p < entry:
            candidates.append(lv)
    # Sort by distance from entry (ascending)
    candidates.sort(key=lambda x: abs(x["price"] - entry))
    out = []
    for lv in candidates[:max_targets]:
        reward = abs(lv["price"] - entry)
        rr = round(reward / risk, 1) if risk > 0 else 0
        out.append({"label": lv["label"], "price": lv["price"], "rr": rr})
    return out


def _find_pdva_edge(levels: list[dict], entry: float, direction: str) -> float | None:
    """Find nearest pDVA edge beyond entry (for stop placement)."""
    candidates = [lv for lv in levels if "pD" in lv["label"] or "pW" in lv["label"]
                  or "TPO-V" in lv["label"]]
    candidates = [lv for lv in candidates
                  if (direction == "long" and lv["price"] < entry)
                  or (direction == "short" and lv["price"] > entry)]
    if not candidates:
        return None
    return max(candidates, key=lambda x: x["price"]) if direction == "short" \
        else min(candidates, key=lambda x: x["price"])["price"]


def _generate_hypos(price: float, bias: str, confluences: list[dict],
                    levels: list[dict], master: str, phase: str,
                    overnight_range: float, price_band_pct: float = 0.02) -> list[dict]:
    """Build 1-2 hipos from top confluences near price."""
    if not confluences:
        return []
    band = price * price_band_pct
    # Filter confluences near price
    near = [c for c in confluences
            if abs(((c["price_min"] + c["price_max"]) / 2.0) - price) <= band * 2]
    # Sort by nearness
    near.sort(key=lambda c: abs(((c["price_min"] + c["price_max"]) / 2.0) - price))

    hypos: list[dict] = []
    tick = TICK_SIZES.get(master, 0.25)
    idx_to_dir_pref = bias if bias in ("long", "short") else None

    # If no strongly filtered "near" pick, use the 2 closest overall
    if not near:
        all_sorted = sorted(confluences,
                            key=lambda c: abs(((c["price_min"] + c["price_max"]) / 2.0) - price))
        near = all_sorted[:2]

    for i, conf in enumerate(near[:2]):
        mid = (conf["price_min"] + conf["price_max"]) / 2.0
        # Direction: toward the confluence
        if idx_to_dir_pref:
            direction = idx_to_dir_pref
        else:
            direction = "long" if mid > price else "short"

        # Setup type heuristic
        if direction == "long" and price < mid:
            setup = "BPB"  # price broke/pulling back toward level
        elif direction == "short" and price > mid:
            setup = "BPB"
        elif direction == "long" and price > mid:
            setup = "RPB"
        elif direction == "short" and price < mid:
            setup = "RPB"
        else:
            setup = "IPB"

        primary = conf["members"][0] if conf["members"] else "CONF"
        # Clean label for setup_type (drop date suffix)
        nivel_clean = primary.split("-")[0]
        setup_type = f"{setup}-{nivel_clean}"

        # Entry: confluence mid
        entry = round(mid, 4)

        # Stop: prefer nearest pDVA/TPO edge on the opposite side.
        # Fallback: half overnight range, capped at 0.3% of price (indices) or tight ATR.
        fallback_cap = price * 0.003 if master in ("MGC", "MCL") else 25.0 * tick
        stop_dist_overnight = min((overnight_range or 0) * 0.5, fallback_cap)
        stop_dist = max(stop_dist_overnight, 5.0 * tick)

        # Try pDVA/TPO edge (nearest beyond entry, on the risk side)
        pdva_edge = None
        for lv in levels:
            lbl = lv["label"]
            if "pD" in lbl or "pW" in lbl or "TPO-V" in lbl:
                if direction == "long" and lv["price"] < entry:
                    if pdva_edge is None or lv["price"] > pdva_edge:
                        pdva_edge = lv["price"]
                if direction == "short" and lv["price"] > entry:
                    if pdva_edge is None or lv["price"] < pdva_edge:
                        pdva_edge = lv["price"]
        if pdva_edge is not None:
            edge_dist = abs(entry - pdva_edge) + tick  # 1 tick beyond edge
            # Prefer pDVA edge if reasonable (<= 1.5× fallback cap)
            if edge_dist <= fallback_cap * 1.5:
                stop_dist = edge_dist
            else:
                stop_dist = min(edge_dist, fallback_cap)

        stop = entry - stop_dist if direction == "long" else entry + stop_dist
        stop = round(stop, 4)
        risk = abs(entry - stop)

        targets = _pick_targets(entry, direction, levels, risk)

        # Grade from confluence
        n_members = len(conf.get("members", []))
        grade = "A+" if n_members >= 4 else "A" if n_members >= 3 else "B"

        # Trading horizon
        higher_tf = any(m.startswith(("m", "q", "y", "pM", "pQ", "pY"))
                        for m in conf.get("members", []))
        horizon = "swing" if higher_tf else "intraday"

        companions = [m for m in conf.get("members", []) if m != primary][:3]

        hypos.append({
            "id": f"h{i+1}",
            "direction": direction,
            "setup_type": setup_type,
            "setup_companions": companions,
            "trading_horizon": horizon,
            "entry": entry,
            "stop": stop,
            "targets": targets,
            "grade": grade,
            "notes": f"Blind snapshot fase {phase}. Confluencia {conf['grade']}: {conf['label']}.",
            "risk_pts": round(risk, 4),
        })

    return hypos


# --------------------------------------------------------------------------
# Main snapshot generator
# --------------------------------------------------------------------------

def generate_blind_snapshot(instrument: str, phase: str,
                             date_str: str = "2026-04-21") -> dict:
    """Build a blind snapshot for the given instrument/phase."""
    master = instrument.upper().strip()
    full_instrument = f"{master} {CONTRACT_SUFFIX}"
    if phase not in ("context", "pre_open", "eod"):
        return {"error": f"phase invalido: {phase}"}

    phase_time = PHASE_TIMES[master][phase]
    cutoff_ts = datetime.strptime(f"{date_str} {phase_time}:00", "%Y-%m-%d %H:%M:%S")

    # Fetch bars
    bars_1m = observer.get_bars(full_instrument, "1m", 3000)
    if "error" in bars_1m or not bars_1m.get("bars"):
        return {"error": f"no 1m bars: {bars_1m.get('error', 'unknown')}",
                "instrument": full_instrument, "phase": phase}
    bars_30m = observer.get_bars(full_instrument, "30m", 400)
    if "error" in bars_30m or not bars_30m.get("bars"):
        bars_30m_list = []
    else:
        bars_30m_list = bars_30m["bars"]

    bars_1m_list = bars_1m["bars"]
    # Filter by cutoff
    bars_1m_filt = [b for b in bars_1m_list if _parse_dt(b["t"]) <= cutoff_ts]
    bars_30m_filt = [b for b in bars_30m_list if _parse_dt(b["t"]) <= cutoff_ts]

    if not bars_1m_filt:
        return {"error": "sin bars pre-cutoff",
                "instrument": full_instrument, "phase": phase, "cutoff": cutoff_ts.isoformat()}

    # Current price = last bar close at cutoff
    price = float(bars_1m_filt[-1]["c"])

    # Developing daily VWAP
    dev_daily = compute_developing_daily_vwap(bars_1m_filt, cutoff_ts)

    # Pit TPO — yesterday if pre-pit, today if eod
    pit_start_s, pit_end_s = PIT_SESSIONS[master]
    if phase == "eod":
        pit_day = date_str
    else:
        pit_day = _prev_trading_day(date_str)
    pit_start_ts = datetime.strptime(f"{pit_day} {pit_start_s}:00", "%Y-%m-%d %H:%M:%S")
    pit_end_ts = datetime.strptime(f"{pit_day} {pit_end_s}:00", "%Y-%m-%d %H:%M:%S")
    tick = TICK_SIZES.get(master, 0.25)
    tpo = compute_pit_tpo(bars_30m_filt, pit_start_ts, pit_end_ts, bucket_size=tick)

    # Historical zones
    zones_by_tf = read_historical_zones(master)

    # Overnight range: 17:00 prev day -> cutoff
    ovn_start = datetime.combine((cutoff_ts - timedelta(days=1)).date(),
                                  datetime.strptime("17:00", "%H:%M").time())
    ovn_bars = [b for b in bars_1m_filt if _parse_dt(b["t"]) >= ovn_start]
    if ovn_bars:
        ovn_high = max(b["h"] for b in ovn_bars)
        ovn_low = min(b["l"] for b in ovn_bars)
        overnight_range = ovn_high - ovn_low
    else:
        overnight_range = 0.0

    # Levels + confluences
    levels = _build_levels(master, price, dev_daily, tpo, zones_by_tf)
    confluences = _build_confluences(levels, tick_size=tick, thresh_ticks=5)

    # Bias
    bias = _detect_bias(price, dev_daily, tpo)

    # Hipos
    hypos = _generate_hypos(price, bias, confluences, levels, master, phase,
                            overnight_range)

    # Summary + analysis text
    summary = f"Blind {phase} — bias {bias}, price {round(price, 2)}"
    top2 = confluences[:2]
    top_conf_txt = ""
    for c in top2:
        top_conf_txt += f"  {c['grade']} {c['label']} [{c['price_min']}-{c['price_max']}]\n"

    analysis_text = (
        f"NADRO BLIND {master} — fase {phase.upper()} cutoff {phase_time} VET\n"
        f"Precio: {round(price, 2)}\n"
        f"Bias: {bias}\n"
        f"\nVWAP desarrollo: {dev_daily['vwap']} (DVAH {dev_daily['dvah']} / DVAL {dev_daily['dval']})\n"
        f"TPO pit ({pit_day}): POC {tpo.get('poc', 0)} / VAH {tpo.get('vah', 0)} / VAL {tpo.get('val', 0)}\n"
        f"Overnight range: {round(overnight_range, 2)} pts\n"
        f"\nTop confluencias:\n{top_conf_txt}"
        f"\nHipotesis generadas: {len(hypos)}\n"
        f"Nota: snapshot BLINDO reconstructivo, sin estado indicador."
    )

    return {
        "master": master,
        "full_instrument": full_instrument,
        "phase": phase,
        "phase_time": phase_time,
        "cutoff_ts": cutoff_ts,
        "price": round(price, 4),
        "dev_daily": dev_daily,
        "tpo": tpo,
        "bias": bias,
        "levels": levels,
        "confluences": confluences,
        "hypos": hypos,
        "summary": summary,
        "analysis_text": analysis_text,
        "n_bars_1m": len(bars_1m_filt),
        "n_bars_30m": len(bars_30m_filt),
    }


def run_all_blind_snapshots(date_str: str = "2026-04-21") -> dict:
    """Run all 18 combinations (6 instruments × 3 phases) and save to markup JSON."""
    instruments = ["MES", "MNQ", "MYM", "M2K", "MGC", "MCL"]
    phases = ["context", "pre_open", "eod"]

    results: list[dict] = []
    saved = 0
    errors = 0

    for inst in instruments:
        for phase in phases:
            try:
                snap = generate_blind_snapshot(inst, phase, date_str)
                if "error" in snap:
                    print(f"[SKIP] {inst} {phase}: {snap['error']}")
                    results.append({"instrument": inst, "phase": phase, **snap})
                    errors += 1
                    continue
                snap_id = f"{inst}_{date_str.replace('-', '')}_{phase}_BLIND"
                ts_iso = f"{date_str}T{snap['phase_time']}:00"
                save_result = markup.save_snapshot(
                    instrument=snap["full_instrument"],
                    price_at_analysis=snap["price"],
                    regime=f"blind_{phase}",
                    bias=snap["bias"],
                    summary=snap["summary"],
                    analysis_text=snap["analysis_text"],
                    confluences=snap["confluences"],
                    levels=snap["levels"],
                    hypos=snap["hypos"],
                    timestamp=ts_iso,
                    snapshot_id=snap_id,
                )
                print(f"[OK]  {snap_id}: price={snap['price']} bias={snap['bias']} "
                      f"hipos={len(snap['hypos'])} -> {save_result.get('action')}")
                results.append({
                    "instrument": inst, "phase": phase,
                    "snapshot_id": snap_id,
                    "price": snap["price"], "bias": snap["bias"],
                    "n_hypos": len(snap["hypos"]),
                    "n_confluences": len(snap["confluences"]),
                    "save_action": save_result.get("action"),
                    "save_path": save_result.get("path"),
                })
                saved += 1
            except Exception as exc:  # noqa: BLE001
                print(f"[ERR] {inst} {phase}: {exc}")
                results.append({"instrument": inst, "phase": phase, "error": str(exc)})
                errors += 1

    return {
        "date": date_str,
        "total_attempted": len(instruments) * len(phases),
        "saved": saved,
        "errors": errors,
        "results": results,
    }
