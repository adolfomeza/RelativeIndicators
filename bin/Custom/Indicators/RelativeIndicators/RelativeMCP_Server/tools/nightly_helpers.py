"""Helpers para NADRO nightly_report — constantes, detección de setups missed, utilities."""
from __future__ import annotations

from datetime import datetime
from pathlib import Path


# ---------------------------------------------------------------------------
# Constants (extraídas de attic/blind_snapshot.py + transcripts NADRO)
# ---------------------------------------------------------------------------

PIT_SESSIONS = {
    "MES": ("09:30", "16:00"),
    "MNQ": ("09:30", "16:00"),
    "MYM": ("09:30", "16:00"),
    "M2K": ("09:30", "16:00"),
    "MGC": ("08:20", "13:30"),
    "MCL": ("09:00", "14:30"),
}

TICK_SIZES = {
    "MES": 0.25, "MNQ": 0.25, "MYM": 1.0, "M2K": 0.10,
    "MGC": 0.10, "MCL": 0.01,
}

CONTRACT_SUFFIX = "06-26"


# ---------------------------------------------------------------------------
# Time helpers
# ---------------------------------------------------------------------------

def parse_bar_time(t: str) -> datetime | None:
    for fmt in ("%Y-%m-%d %H:%M:%S.%f", "%Y-%m-%d %H:%M:%S"):
        try:
            return datetime.strptime(t, fmt)
        except ValueError:
            continue
    return None


def in_pit_session(bar_time: datetime, master: str) -> bool:
    pit = PIT_SESSIONS.get(master)
    if not pit:
        return False
    start = datetime.strptime(pit[0], "%H:%M").time()
    end = datetime.strptime(pit[1], "%H:%M").time()
    t = bar_time.time()
    return start <= t <= end


# ---------------------------------------------------------------------------
# Levels extraction from snapshot vs VwapLevels
# ---------------------------------------------------------------------------

def extract_levels_from_snapshot(snap: dict) -> list[tuple[float, str]]:
    """Niveles que el trader YA tenía en el snapshot — usaremos esto para excluir."""
    out: list[tuple[float, str]] = []
    for lv in snap.get("levels", []) or []:
        try:
            out.append((float(lv["price"]), str(lv.get("label", ""))))
        except (ValueError, TypeError, KeyError):
            continue
    for h in snap.get("hypos", []) or []:
        for key in ("entry", "stop"):
            v = h.get(key)
            if v is not None:
                try:
                    out.append((float(v), f"hypo-{key}"))
                except (ValueError, TypeError):
                    pass
        for tg in h.get("targets", []) or []:
            try:
                out.append((float(tg["price"]), f"hypo-target"))
            except (ValueError, TypeError, KeyError):
                pass
    for c in snap.get("confluences", []) or []:
        try:
            mid = (float(c["price_min"]) + float(c["price_max"])) / 2.0
            out.append((mid, f"confluence-{c.get('label', '')}"))
        except (ValueError, TypeError, KeyError):
            pass
    return out


def collect_candidate_levels(vwap_snap: dict, tpo_profiles: dict) -> list[tuple[float, str]]:
    """Niveles que debieron haber sido tracked desde VwapLevels + TPO pit profile."""
    out: list[tuple[float, str]] = []

    # VwapLevels: DVAH, VWAP, DVAL por cada TF + pVAs históricas
    for tf, data in (vwap_snap.get("timeframes") or {}).items():
        if not data:
            continue
        for key in ("dvah", "vwap", "dval"):
            v = data.get(key)
            if v is not None:
                try:
                    # Prefijos NADRO: Daily=sin prefix, Weekly=w, Monthly=m, Quarterly=q, Annual=y
                    prefix = {"Daily": "", "Weekly": "w", "Monthly": "m",
                              "Quarterly": "q", "Annual": "y"}.get(tf, tf[:1].lower())
                    label_map = {"dvah": "DVAH", "vwap": "VWAP", "dval": "DVAL"}
                    out.append((float(v), f"{prefix}{label_map[key]}"))
                except (ValueError, TypeError):
                    pass
        # Zonas históricas (PVA): cada zone tiene upper/mid/lower
        for i, z in enumerate(data.get("zones", []) or []):
            age_prefix = {"Daily": "p", "Weekly": "pW", "Monthly": "pM",
                          "Quarterly": "pQ", "Annual": "pY"}.get(tf, "p")
            for key, suffix in (("upper", "DVAH"), ("mid", "DVAP"), ("lower", "DVAL")):
                v = z.get(key)
                if v is not None:
                    try:
                        out.append((float(v), f"{age_prefix}{suffix}-{i}"))
                    except (ValueError, TypeError):
                        pass

    # TPO del día (pit): POC, VAH, VAL del día actual
    for session_date, va in (tpo_profiles or {}).items():
        for key, label in (("poc", "POC"), ("vah", "TPO-VAH"), ("val", "TPO-VAL")):
            v = va.get(key)
            if v is not None:
                try:
                    out.append((float(v), f"pit-{label}"))
                except (ValueError, TypeError):
                    pass

    # Dedup
    seen: set = set()
    uniq: list[tuple[float, str]] = []
    for price, label in out:
        key = (round(price, 4), label)
        if key in seen:
            continue
        seen.add(key)
        uniq.append((price, label))
    return uniq


def level_in_snapshot(candidate_price: float, snapshot_levels: list[tuple[float, str]],
                      tick_size: float, tolerance_ticks: int = 4) -> bool:
    """True si el candidato ya estaba en el snapshot (±tolerance_ticks)."""
    tol = tick_size * tolerance_ticks
    for price, _ in snapshot_levels:
        if abs(candidate_price - price) <= tol:
            return True
    return False


# ---------------------------------------------------------------------------
# Missed setup detection
# ---------------------------------------------------------------------------

def detect_level_touches(bars: list[dict], level_price: float, tick_size: float,
                         touch_ticks: int = 3) -> list[int]:
    """Índices de bars donde high/low toca el nivel ±touch_ticks."""
    tol = tick_size * touch_ticks
    out: list[int] = []
    for i, b in enumerate(bars):
        try:
            hi = float(b.get("h", 0) or 0)
            lo = float(b.get("l", 0) or 0)
        except (ValueError, TypeError):
            continue
        if hi == 0 or lo == 0:
            continue
        if (lo - tol) <= level_price <= (hi + tol):
            out.append(i)
    return out


def find_reversal_after_touch(bars: list[dict], touch_idx: int,
                              lookback_ctx: int = 5, forward: int = 3) -> dict | None:
    """Detecta si hubo reversal de al menos `forward` bars tras el touch.

    Returns {direction, mfe, mae, bars_to_mfe} o None si no hubo reversal clara.
    """
    if touch_idx + forward >= len(bars):
        return None

    # Dirección del approach: últimas N bars antes del touch
    start = max(0, touch_idx - lookback_ctx)
    pre = bars[start:touch_idx + 1]
    if len(pre) < 2:
        return None

    try:
        approach_start = float(pre[0].get("c", 0) or 0)
        approach_end = float(pre[-1].get("c", 0) or 0)
    except (ValueError, TypeError):
        return None
    if approach_start == 0 or approach_end == 0:
        return None

    approaching_down = approach_end < approach_start  # precio bajando → reversal sería long
    approaching_up = approach_end > approach_start    # precio subiendo → reversal sería short

    if not approaching_down and not approaching_up:
        return None

    reversal_direction = "long" if approaching_down else "short"

    # Touch bar: si el approach fue bajando, touch low ≈ level. Desde ahí buscamos MFE hacia arriba.
    try:
        touch_close = float(bars[touch_idx].get("c", 0) or 0)
        touch_high = float(bars[touch_idx].get("h", 0) or 0)
        touch_low = float(bars[touch_idx].get("l", 0) or 0)
    except (ValueError, TypeError):
        return None

    # Ventana post-touch (incluye la touch bar)
    window = bars[touch_idx:touch_idx + forward + 1]
    mfe = 0.0
    mae = 0.0
    bars_to_mfe = 0

    if reversal_direction == "long":
        entry_ref = touch_low  # punto óptimo de entry
        for j, b in enumerate(window):
            try:
                hi = float(b.get("h", 0) or 0)
                lo = float(b.get("l", 0) or 0)
            except (ValueError, TypeError):
                continue
            fav = hi - entry_ref
            adv = entry_ref - lo
            if fav > mfe:
                mfe = fav
                bars_to_mfe = j
            if adv > mae:
                mae = adv
    else:
        entry_ref = touch_high
        for j, b in enumerate(window):
            try:
                hi = float(b.get("h", 0) or 0)
                lo = float(b.get("l", 0) or 0)
            except (ValueError, TypeError):
                continue
            fav = entry_ref - lo
            adv = hi - entry_ref
            if fav > mfe:
                mfe = fav
                bars_to_mfe = j
            if adv > mae:
                mae = adv

    return {
        "direction": reversal_direction,
        "entry_ref": entry_ref,
        "mfe": round(mfe, 4),
        "mae": round(mae, 4),
        "bars_to_mfe": bars_to_mfe,
        "touch_close": touch_close,
        "touch_time": bars[touch_idx].get("t"),
    }


def classify_setup_type(level_label: str, direction: str) -> str:
    """Clasifica el setup tipo IPB/BPB/RPB según nivel tocado.

    Heurística NADRO:
    - pXDVAH / pXDVAL (zonas previas / PVA) → RPB (Return Pullback)
    - DVAH / DVAL actual (desarrollo) → IPB (Imbalance Pullback)
    - VWAP → IPB suave
    - TPO-POC/VAH/VAL → BPB (Breakout Pullback) si extremos, IPB si POC
    """
    label_l = level_label.lower()
    if label_l.startswith("p") and len(label_l) > 1 and label_l[1] in "wmqy":
        return "RPB"
    if "poc" in label_l:
        return "BPB"
    if "vwap" in label_l:
        return "IPB"
    if "dvah" in label_l or "dval" in label_l:
        return "IPB"
    if "vah" in label_l or "val" in label_l:
        return "BPB"
    return "IPB"


def detect_compression_expansion(bars: list[dict]) -> dict:
    """Detecta fase de compresión y expansión post-compresión.

    Compresión = ventana de N bars con rango < 60% del rango medio de 3×N anteriores.
    Expansión = bar siguiente con rango > 200% del rango de compresión.
    """
    if len(bars) < 30:
        return {"compression_detected": False, "expansion_detected": False}

    def bar_range(b: dict) -> float:
        try:
            return float(b.get("h", 0) or 0) - float(b.get("l", 0) or 0)
        except (ValueError, TypeError):
            return 0.0

    n_compress = 8
    n_ctx = 24
    best_comp_idx = -1
    best_ratio = 1.0
    for i in range(n_ctx, len(bars) - 1):
        ctx = bars[i - n_ctx:i - n_compress]
        comp = bars[i - n_compress:i]
        avg_ctx = sum(bar_range(b) for b in ctx) / max(1, len(ctx))
        avg_comp = sum(bar_range(b) for b in comp) / max(1, len(comp))
        if avg_ctx <= 0:
            continue
        ratio = avg_comp / avg_ctx
        if ratio < best_ratio and ratio < 0.6:
            best_ratio = ratio
            best_comp_idx = i

    if best_comp_idx < 0:
        return {"compression_detected": False, "expansion_detected": False}

    # Buscar expansion post-compression
    comp_range_avg = sum(bar_range(b) for b in bars[best_comp_idx - n_compress:best_comp_idx]) / n_compress
    expansion_detected = False
    expansion_idx = -1
    for j in range(best_comp_idx, min(best_comp_idx + 8, len(bars))):
        if bar_range(bars[j]) > comp_range_avg * 2:
            expansion_detected = True
            expansion_idx = j
            break

    return {
        "compression_detected": True,
        "compression_idx": best_comp_idx,
        "compression_time": bars[best_comp_idx].get("t") if best_comp_idx < len(bars) else None,
        "compression_ratio": round(best_ratio, 2),
        "expansion_detected": expansion_detected,
        "expansion_idx": expansion_idx,
        "expansion_time": bars[expansion_idx].get("t") if expansion_idx >= 0 else None,
    }


def compute_delta_trend(bars: list[dict]) -> dict:
    """Aproxima delta usando close vs open por bar (proxy simple)."""
    up_vol = 0.0
    dn_vol = 0.0
    for b in bars:
        try:
            o = float(b.get("o", 0) or 0)
            c = float(b.get("c", 0) or 0)
            v = float(b.get("v", 0) or 0)
        except (ValueError, TypeError):
            continue
        if c > o:
            up_vol += v
        elif c < o:
            dn_vol += v
    total = up_vol + dn_vol
    if total == 0:
        return {"up_vol": 0, "dn_vol": 0, "delta_pct": 0, "bias": "neutral"}
    delta_pct = (up_vol - dn_vol) / total * 100
    bias = "bullish" if delta_pct > 15 else ("bearish" if delta_pct < -15 else "neutral")
    return {
        "up_vol": round(up_vol),
        "dn_vol": round(dn_vol),
        "delta_pct": round(delta_pct, 1),
        "bias": bias,
    }


def compute_price_trend(bars: list[dict]) -> dict:
    """Dirección neta del precio en la ventana."""
    if not bars:
        return {"direction": "flat", "change": 0, "pct": 0}
    try:
        first = float(bars[0].get("o", 0) or 0)
        last = float(bars[-1].get("c", 0) or 0)
    except (ValueError, TypeError):
        return {"direction": "flat", "change": 0, "pct": 0}
    if first == 0 or last == 0:
        return {"direction": "flat", "change": 0, "pct": 0}
    change = last - first
    pct = (change / first) * 100
    direction = "up" if change > 0 else ("down" if change < 0 else "flat")
    return {"direction": direction, "change": round(change, 2), "pct": round(pct, 3)}
