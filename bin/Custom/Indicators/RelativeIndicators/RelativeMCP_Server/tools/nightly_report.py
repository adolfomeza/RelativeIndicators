"""NADRO Nightly Report — review EOD narrativo alineado con la metodología oficial.

Basado en transcripts oficiales NADRO (02, 03, 06, 07, 08, 09, 14, 15, 18, 19, 22 +
livestreams 24-28). Ver `Docs/Nadro/nightly_report_template.md` para la estructura.

Diferencias con `eod_review.py`:
- Secciones N-A-D-R-O (Narrativa / Aceptación / DVA / Ritmo / OrderFlow)
- **MISSED SETUPS** — setups que dispararon en niveles fuera del snapshot
- Disonancia narrativa (hipos LONG+SHORT coexistiendo)
- Lecciones auto-generadas + placeholder manual
- Sugerencia de hipo #1 para mañana

Uso principal desde server.py:
- generate_nightly_report(instrument, date)
- generate_nightly_all(date) → HTML consolidado
"""
from __future__ import annotations

import json
from datetime import datetime

from ..paths import markups_dir
from . import observer
from . import tpo_cva as tpo_tool
from . import vwap_levels as vwap_tool
from . import eod_review as classic_review
from . import nightly_html
from .nightly_helpers import (
    PIT_SESSIONS,
    TICK_SIZES,
    CONTRACT_SUFFIX,
    classify_setup_type,
    collect_candidate_levels,
    compute_delta_trend,
    compute_price_trend,
    detect_compression_expansion,
    detect_level_touches,
    extract_levels_from_snapshot,
    find_reversal_after_touch,
    level_in_snapshot,
    parse_bar_time,
)


# ---------------------------------------------------------------------------
# Missed setups
# ---------------------------------------------------------------------------

def scan_missed_setups(instrument: str, date_str: str,
                       min_mfe_pct: float = 0.10,
                       max_mae_to_mfe_ratio: float = 0.60) -> dict:
    """Detecta setups que fireearon en niveles NO previstos en el snapshot.

    Algoritmo:
    1. Carga morning snapshot → niveles ya previstos
    2. Recupera VwapLevels (pDVAH, W-DVAH, etc.) + TPO profile del día
    3. Para cada nivel candidato no en snapshot:
       - Encuentra touches en pit session (high/low ±3 ticks)
       - Detecta reversal candle en las siguientes 3 bars
       - Valida MFE >= min_mfe_pct * price, MAE/MFE < max_mae_to_mfe_ratio
       - Clasifica setup (IPB/BPB/RPB)

    Returns:
        {missed_setups: [...], scanned_levels: int, snapshot_levels: int}
    """
    master = instrument.split()[0].split("-")[0].upper()
    tick_size = TICK_SIZES.get(master, 0.25)

    # --- 1. Cargar snapshot
    mdir = markups_dir()
    snap_path = mdir / f"{master}_{date_str}.json"
    if not snap_path.is_file():
        return {"error": f"no existe {snap_path}", "missed_setups": []}
    try:
        data = json.loads(snap_path.read_text(encoding="utf-8"))
    except Exception as exc:  # noqa: BLE001
        return {"error": f"no se pudo parsear {snap_path}: {exc}", "missed_setups": []}

    snapshots = data.get("snapshots", [])
    if not snapshots:
        return {"error": "sin snapshots", "missed_setups": []}

    snap = max(snapshots, key=lambda s: s.get("timestamp", ""))
    snapshot_levels = extract_levels_from_snapshot(snap)

    # --- 2. Bars de la pit session
    full_instrument = instrument if " " in instrument else f"{master} {CONTRACT_SUFFIX}"
    bars_resp = observer.get_bars(instrument=full_instrument, tf="1m", n=3000)
    if "error" in bars_resp:
        return {"error": f"bars feed error: {bars_resp['error']}", "missed_setups": []}
    all_bars = bars_resp.get("bars", []) or []

    pit = PIT_SESSIONS.get(master)
    if not pit:
        return {"error": f"sin pit session para {master}", "missed_setups": []}
    pit_start = datetime.strptime(f"{date_str} {pit[0]}:00", "%Y-%m-%d %H:%M:%S")
    pit_end = datetime.strptime(f"{date_str} {pit[1]}:00", "%Y-%m-%d %H:%M:%S")

    pit_bars: list[dict] = []
    for b in all_bars:
        bt = parse_bar_time(b.get("t", ""))
        if bt is None:
            continue
        if pit_start <= bt <= pit_end:
            pit_bars.append(b)

    if len(pit_bars) < 30:
        return {
            "error": f"insufficient pit bars ({len(pit_bars)}) para {master} {date_str}",
            "missed_setups": [],
        }

    # --- 3. VwapLevels snapshot + TPO profile
    vwap_snap = vwap_tool.snapshot(instrument=master)
    tpo_profiles: dict = {}
    try:
        tpo_profiles = tpo_tool.build_daily_profiles(pit_bars, session="rth")
    except Exception:  # noqa: BLE001
        tpo_profiles = {}

    candidate_levels = collect_candidate_levels(vwap_snap, tpo_profiles)

    # --- 4. Detectar missed setups
    missed: list[dict] = []
    pit_bars_sorted = sorted(pit_bars, key=lambda b: b.get("t", ""))

    for level_price, level_label in candidate_levels:
        if level_in_snapshot(level_price, snapshot_levels, tick_size):
            continue

        touches = detect_level_touches(pit_bars_sorted, level_price, tick_size, touch_ticks=3)
        if not touches:
            continue

        # Tomar el primer touch con reversal válida
        for touch_idx in touches:
            reversal = find_reversal_after_touch(pit_bars_sorted, touch_idx,
                                                 lookback_ctx=5, forward=3)
            if reversal is None:
                continue

            # Validar
            mfe = reversal["mfe"]
            mae = reversal["mae"]
            if level_price <= 0:
                continue
            min_mfe = max(level_price * min_mfe_pct / 100.0, tick_size * 6)
            if mfe < min_mfe:
                continue
            if mfe <= 0:
                continue
            if (mae / mfe) > max_mae_to_mfe_ratio:
                continue

            setup_type = classify_setup_type(level_label, reversal["direction"])
            missed.append({
                "level_label": level_label,
                "level_price": round(level_price, 4),
                "direction": reversal["direction"],
                "setup_type": f"{setup_type}-{level_label}",
                "touch_time": reversal["touch_time"],
                "entry_ref": reversal["entry_ref"],
                "mfe_pts": mfe,
                "mae_pts": mae,
                "bars_to_mfe": reversal["bars_to_mfe"],
                "mae_to_mfe_ratio": round(mae / mfe, 2) if mfe > 0 else None,
            })
            break  # solo el primer touch con reversal por nivel

    # Sort por MFE descendente
    missed.sort(key=lambda m: m["mfe_pts"], reverse=True)

    return {
        "instrument": master,
        "date": date_str,
        "missed_setups": missed,
        "scanned_levels": len(candidate_levels),
        "snapshot_levels": len(snapshot_levels),
        "pit_bars": len(pit_bars),
        "tpo_profile": tpo_profiles.get(date_str) if tpo_profiles else None,
    }


# ---------------------------------------------------------------------------
# NADRO section analysis
# ---------------------------------------------------------------------------

def analyze_narrativa(snap: dict, price_trend: dict, classic: dict) -> dict:
    """¿Se cumplió la narrativa pre-open?"""
    bias = (snap.get("bias") or "").lower()
    direction = price_trend.get("direction", "flat")
    change = price_trend.get("change", 0)

    bias_fulfilled = False
    verdict = "neutral"
    if bias in ("bullish", "alcista") and direction == "up":
        bias_fulfilled = True
        verdict = "bias alcista confirmado — el precio cerró al alza"
    elif bias in ("bearish", "bajista") and direction == "down":
        bias_fulfilled = True
        verdict = "bias bajista confirmado — el precio cerró a la baja"
    elif bias in ("neutral", "rotacional"):
        if abs(price_trend.get("pct", 0)) < 0.5:
            bias_fulfilled = True
            verdict = "bias neutral confirmado — rotación sin desequilibrio"
        else:
            verdict = f"bias neutral pero mercado se desequilibró {direction} ({change:+.2f})"
    else:
        verdict = f"bias '{bias}' vs precio '{direction}' — no alineado"

    # Hipos alineados vs no
    wins = classic.get("counts", {}).get("WIN", 0) + classic.get("counts", {}).get("WIN_MINOR", 0)
    dead = classic.get("counts", {}).get("DEAD", 0)
    stops = classic.get("counts", {}).get("STOP_TIGHT", 0) + classic.get("counts", {}).get("STOP_GENUINE", 0)

    commentary = []
    if wins > 0:
        commentary.append(f"{wins} hipos ganadoras — narrativa operable")
    if stops > 0:
        commentary.append(f"{stops} hipos stopped — niveles de invalidación trabajando")
    if dead >= len(classic.get("hypos", [])) and dead > 0:
        commentary.append("todas las hipos DEAD — precio nunca se acercó a los entries")

    return {
        "bias_stated": bias or "(no especificado)",
        "price_direction": direction,
        "price_change": change,
        "bias_fulfilled": bias_fulfilled,
        "verdict": verdict,
        "commentary": commentary,
    }


def analyze_aceptacion(snap: dict, pit_bars: list[dict], tick_size: float) -> dict:
    """Niveles con wick (rechazo) vs close (aceptación)."""
    levels = [(float(lv["price"]), str(lv.get("label", ""))) for lv in snap.get("levels", []) or []
              if lv.get("price") is not None]
    if not levels or not pit_bars:
        return {"rejected": [], "accepted": [], "summary": "sin niveles/bars para analizar"}

    # Session high/low/close
    try:
        session_high = max(float(b.get("h", 0) or 0) for b in pit_bars)
        session_low = min(float(b.get("l", 99999999) or 99999999) for b in pit_bars if float(b.get("l", 0) or 0) > 0)
        session_close = float(pit_bars[-1].get("c", 0) or 0)
    except (ValueError, TypeError):
        return {"rejected": [], "accepted": [], "summary": "error parsing bars"}

    rejected: list[dict] = []
    accepted: list[dict] = []
    for price, label in levels:
        # Rechazado: precio hizo wick en el nivel pero cerró del lado contrario
        wick_above = session_high > price + tick_size * 2 and session_close < price
        wick_below = session_low < price - tick_size * 2 and session_close > price
        if wick_above:
            rejected.append({"label": label, "price": price, "type": "rejection from above"})
        elif wick_below:
            rejected.append({"label": label, "price": price, "type": "rejection from below"})
        else:
            # Aceptación parcial si el close está del "mismo lado" consistente
            if abs(session_close - price) < tick_size * 10:
                accepted.append({"label": label, "price": price, "type": "close cerca del nivel"})

    summary = f"{len(rejected)} rechazos (wicks), {len(accepted)} aceptaciones. "
    if rejected:
        top = rejected[0]
        summary += f"Principal rechazo: {top['label']} @ {top['price']} ({top['type']})."

    return {"rejected": rejected, "accepted": accepted, "summary": summary}


def analyze_dva(tpo_profile: dict | None, snap_price: float) -> dict:
    """Cómo se desarrolló el DVA/TPO del día y su posición final."""
    if not tpo_profile:
        return {"available": False, "summary": "no TPO profile disponible"}

    poc = tpo_profile.get("poc", 0)
    vah = tpo_profile.get("vah", 0)
    val = tpo_profile.get("val", 0)

    # Forma del perfil (skew)
    va_range = vah - val if vah > val else 0
    poc_position = "center"
    if va_range > 0:
        rel_poc = (poc - val) / va_range
        if rel_poc > 0.7:
            poc_position = "upper (p-skew alcista)"
        elif rel_poc < 0.3:
            poc_position = "lower (n-skew bajista)"

    summary = (f"POC {poc} / VAH {vah} / VAL {val} (rango {va_range:.2f}). "
               f"POC en {poc_position}.")

    return {
        "available": True,
        "poc": poc,
        "vah": vah,
        "val": val,
        "range_pts": round(va_range, 2),
        "poc_position": poc_position,
        "summary": summary,
    }


def analyze_ritmo(pit_bars: list[dict]) -> dict:
    """Compresión → expansión. Rango, velocidad."""
    compression = detect_compression_expansion(pit_bars)

    # Range stats
    ranges = []
    for b in pit_bars:
        try:
            r = float(b.get("h", 0) or 0) - float(b.get("l", 0) or 0)
            if r > 0:
                ranges.append(r)
        except (ValueError, TypeError):
            continue
    avg_range = sum(ranges) / max(1, len(ranges))

    # Categorizar régimen
    if compression.get("compression_detected") and compression.get("expansion_detected"):
        regime = "compresión → expansión (ley NADRO)"
    elif compression.get("compression_detected"):
        regime = "compresión sin expansión — energía latente"
    else:
        regime = "sin compresión clara — mercado extendido"

    summary = f"Regime: {regime}. Avg bar range 1m: {avg_range:.2f} pts."
    if compression.get("compression_time"):
        summary += f" Compresión detectada @ {compression['compression_time']}."
    if compression.get("expansion_time"):
        summary += f" Expansión @ {compression['expansion_time']}."

    return {
        "avg_range_pts": round(avg_range, 2),
        "compression_detected": compression.get("compression_detected", False),
        "compression_time": compression.get("compression_time"),
        "expansion_detected": compression.get("expansion_detected", False),
        "expansion_time": compression.get("expansion_time"),
        "regime": regime,
        "summary": summary,
    }


def analyze_order_flow(pit_bars: list[dict], price_trend: dict) -> dict:
    """Delta proxy vs precio — alineación o divergencia."""
    delta = compute_delta_trend(pit_bars)
    direction = price_trend.get("direction", "flat")
    delta_bias = delta.get("bias", "neutral")

    alignment = "aligned" if (
        (delta_bias == "bullish" and direction == "up") or
        (delta_bias == "bearish" and direction == "down") or
        (delta_bias == "neutral" and direction == "flat")
    ) else "divergent"

    commentary = ""
    if alignment == "divergent":
        commentary = (f"Divergencia: delta {delta_bias} vs precio {direction}. "
                      "Posible absorción / señal de fatiga.")
    else:
        commentary = f"Delta alineado con precio ({direction}). Impulso confirmado."

    return {
        "delta_pct": delta.get("delta_pct", 0),
        "delta_bias": delta_bias,
        "up_vol": delta.get("up_vol", 0),
        "dn_vol": delta.get("dn_vol", 0),
        "price_direction": direction,
        "alignment": alignment,
        "summary": commentary,
    }


def detect_dissonance(hypos: list[dict]) -> dict:
    """Hipos LONG y SHORT simultáneas = dos narrativas válidas."""
    longs = [h for h in hypos if (h.get("direction") or "").lower() == "long"]
    shorts = [h for h in hypos if (h.get("direction") or "").lower() == "short"]
    has_dissonance = bool(longs) and bool(shorts)
    return {
        "has_dissonance": has_dissonance,
        "long_count": len(longs),
        "short_count": len(shorts),
        "summary": (
            f"Disonancia narrativa: {len(longs)} hipos LONG + {len(shorts)} SHORT. "
            "Dos tesis válidas — elección dependió del marco de foco."
            if has_dissonance
            else "Narrativa unificada — todas las hipos en la misma dirección."
        ),
    }


# ---------------------------------------------------------------------------
# Lessons (auto-generated)
# ---------------------------------------------------------------------------

def generate_lessons(classic_review: dict, missed: list[dict],
                     narrativa: dict, ritmo: dict, of: dict) -> list[str]:
    """Genera 3-5 lecciones automáticas basadas en lo observado."""
    lessons: list[str] = []

    counts = classic_review.get("counts", {})
    if counts.get("STOP_TIGHT", 0) > 0:
        lessons.append(
            f"STOP TIGHT x{counts['STOP_TIGHT']}: stops más ajustados que rango natural. "
            "Regla: stop >= 50% rango overnight o ATR*2."
        )
    if counts.get("DEAD", 0) >= 2:
        lessons.append(
            f"{counts['DEAD']} hipos DEAD — entries muy lejos del precio de apertura. "
            "Replantear el spread entry-precio al momento del pit open."
        )
    if len(missed) >= 3:
        levels_missed = ", ".join(m["level_label"] for m in missed[:3])
        lessons.append(
            f"MISSED SETUPS: {len(missed)} niveles dispararon fuera del snapshot ({levels_missed}). "
            "Ampliar el universo de niveles tracked pre-open."
        )
    if not narrativa.get("bias_fulfilled"):
        lessons.append(
            f"Bias pre-open ({narrativa.get('bias_stated')}) NO se cumplió. "
            "Revisar el top-down del LTVWs — ¿qué indicio se ignoró?"
        )
    if of.get("alignment") == "divergent":
        lessons.append(
            "Divergencia delta-precio visible durante el día. "
            "Monitorear absorción como señal adelantada en el próximo review."
        )
    if ritmo.get("compression_detected") and not ritmo.get("expansion_detected"):
        lessons.append(
            "Compresión detectada pero sin expansión dentro de la pit session. "
            "Oportunidad potencial para mañana — vigilar ese rango."
        )

    # Asegurar al menos 3
    if len(lessons) < 3:
        lessons.append('Principio NADRO: "áreas, no líneas". Revisar si los entries respetan zonas.')
    if len(lessons) < 4:
        lessons.append('Principio NADRO: "a veces la operación te deja y está bien". No perseguir.')
    return lessons[:5]


def suggest_tomorrow(classic_review: dict, narrativa: dict, missed: list[dict]) -> str | None:
    """Sugiere hipo #1 para mañana si hay patrón claro."""
    counts = classic_review.get("counts", {})
    hypos = classic_review.get("hypos", [])

    # Patrón 1: hipo no triggered pero ahora cerca del precio → hipo #1 mañana
    dead_hypos = [h for h in hypos if h.get("classification") == "DEAD"]
    if dead_hypos:
        h = dead_hypos[0]
        return (f"Si el mercado abre cerca del entry de la hipo {h['id']} "
                f"({h.get('direction')} {h.get('setup_type')} @ {h.get('entry')}), "
                f"podría reactivarse como hipo #1 mañana.")

    # Patrón 2: missed setup con MFE grande → nivel a tracked mañana
    if missed:
        top = missed[0]
        return (f"Incluir {top['level_label']} @ {top['level_price']} en el snapshot de mañana — "
                f"hoy dio un {top['direction']} con MFE {top['mfe_pts']} pts fuera del análisis.")

    return None


# ---------------------------------------------------------------------------
# Markdown generation
# ---------------------------------------------------------------------------

def generate_markdown(report: dict) -> str:
    """Markdown NADRO-compliant — delega al renderer en nightly_html.py."""
    return nightly_html.render_markdown(report)


# ---------------------------------------------------------------------------
# Main entry
# ---------------------------------------------------------------------------

def generate_nightly_report(instrument: str, date_str: str | None = None) -> dict:
    """Genera el nightly report NADRO completo para un instrumento.

    Unifica:
    - Classic review (eod_review.review_snapshot) para walk-forward de hipos
    - scan_missed_setups para los setups fuera del snapshot
    - Análisis N-A-D-R-O sobre bars + TPO + snapshot
    - Lecciones auto-generadas
    - Sugerencia para mañana

    Guarda el markdown en `Docs/Nadro/nightly_reports/{INST}_{DATE}.md`.
    """
    if not date_str:
        date_str = datetime.now().strftime("%Y-%m-%d")

    master = instrument.split()[0].split("-")[0].upper()
    mdir = markups_dir()
    snap_path = mdir / f"{master}_{date_str}.json"

    if not snap_path.is_file():
        return {"error": f"no existe {snap_path}", "instrument": master, "date": date_str}

    try:
        data = json.loads(snap_path.read_text(encoding="utf-8"))
    except Exception as exc:  # noqa: BLE001
        return {"error": f"no se pudo parsear {snap_path}: {exc}"}

    snapshots = data.get("snapshots", [])
    if not snapshots:
        return {"error": f"sin snapshots en {snap_path}", "instrument": master, "date": date_str}

    # Snapshot base
    snap = max(snapshots, key=lambda s: s.get("timestamp", ""))

    # Walk-forward classic review
    classic = classic_review.review_snapshot(snap)

    # Missed setups (puede requerir feed activo de NT)
    missed_result = scan_missed_setups(master, date_str)
    missed = missed_result.get("missed_setups", [])

    # Pit bars (reutilizamos del scan si disponible, si no refetch)
    full_instrument = instrument if " " in instrument else f"{master} {CONTRACT_SUFFIX}"
    bars_resp = observer.get_bars(instrument=full_instrument, tf="1m", n=3000)
    all_bars = bars_resp.get("bars", []) or []
    pit = PIT_SESSIONS.get(master)
    pit_bars: list[dict] = []
    if pit:
        pit_start = datetime.strptime(f"{date_str} {pit[0]}:00", "%Y-%m-%d %H:%M:%S")
        pit_end = datetime.strptime(f"{date_str} {pit[1]}:00", "%Y-%m-%d %H:%M:%S")
        for b in all_bars:
            bt = parse_bar_time(b.get("t", ""))
            if bt and pit_start <= bt <= pit_end:
                pit_bars.append(b)
        pit_bars.sort(key=lambda b: b.get("t", ""))

    # N-A-D-R-O analysis
    tick_size = TICK_SIZES.get(master, 0.25)
    price_trend = compute_price_trend(pit_bars)
    narrativa = analyze_narrativa(snap, price_trend, classic)
    aceptacion = analyze_aceptacion(snap, pit_bars, tick_size)
    dva = analyze_dva(missed_result.get("tpo_profile"), snap.get("price_at_analysis", 0))
    ritmo = analyze_ritmo(pit_bars)
    order_flow = analyze_order_flow(pit_bars, price_trend)
    dissonance = detect_dissonance(snap.get("hypos", []) or [])

    lessons = generate_lessons(classic, missed, narrativa, ritmo, order_flow)
    tomorrow_hint = suggest_tomorrow(classic, narrativa, missed)

    report = {
        "instrument": master,
        "date": date_str,
        "pit_session": pit,
        "snapshot": {"snapshot_id": snap.get("id"), "timestamp": snap.get("timestamp")},
        "classic": classic,
        "missed_setups": missed,
        "missed_scan_stats": {
            "scanned_levels": missed_result.get("scanned_levels", 0),
            "snapshot_levels": missed_result.get("snapshot_levels", 0),
            "pit_bars": missed_result.get("pit_bars", len(pit_bars)),
        },
        "narrativa": narrativa,
        "aceptacion": aceptacion,
        "dva": dva,
        "ritmo": ritmo,
        "order_flow": order_flow,
        "dissonance": dissonance,
        "lessons": lessons,
        "tomorrow_hint": tomorrow_hint,
        "price_trend": price_trend,
    }

    md = generate_markdown(report)
    report["markdown"] = md

    # Persist
    reports_dir = mdir.parent / "nightly_reports"
    reports_dir.mkdir(parents=True, exist_ok=True)
    out_path = reports_dir / f"{master}_{date_str}.md"
    out_path.write_text(md, encoding="utf-8")
    report["md_path"] = str(out_path)

    return report


def generate_nightly_all(date_str: str | None = None, write_html: bool = True) -> dict:
    """Corre nightly para los 6 instrumentos NADRO y genera HTML consolidado."""
    if not date_str:
        date_str = datetime.now().strftime("%Y-%m-%d")

    instruments = ["MGC", "MCL", "MES", "MNQ", "MYM", "M2K"]
    results: dict = {}
    for inst in instruments:
        results[inst] = generate_nightly_report(inst, date_str)

    # Aggregate
    total_hypos = 0
    total_missed = 0
    total_wins = 0
    total_stop_tight = 0
    total_dissonance = 0
    for inst, r in results.items():
        if "error" in r:
            continue
        classic = r.get("classic", {})
        total_hypos += len(classic.get("hypos", []))
        total_wins += classic.get("wins", 0)
        total_stop_tight += classic.get("counts", {}).get("STOP_TIGHT", 0)
        total_missed += len(r.get("missed_setups", []))
        if r.get("dissonance", {}).get("has_dissonance"):
            total_dissonance += 1

    aggregate = {
        "total_hypos": total_hypos,
        "total_wins": total_wins,
        "total_stop_tight": total_stop_tight,
        "total_missed": total_missed,
        "total_dissonance": total_dissonance,
    }

    html_path = None
    if write_html:
        reports_dir = markups_dir().parent / "nightly_reports"
        reports_dir.mkdir(parents=True, exist_ok=True)
        html_path = reports_dir / f"nightly_all_{date_str}.html"
        html_content = generate_html_consolidated(results, aggregate, date_str)
        html_path.write_text(html_content, encoding="utf-8")
        html_path = str(html_path)

    return {
        "date": date_str,
        "reports": results,
        "aggregate": aggregate,
        "html_path": html_path,
    }


# ---------------------------------------------------------------------------
# HTML consolidated (renderer está en nightly_html.py para mantener archivo bajo 700 LOC)
# ---------------------------------------------------------------------------

def generate_html_consolidated(reports: dict, aggregate: dict, date_str: str) -> str:
    """HTML NADRO Nightly Report consolidado — delega al módulo nightly_html."""
    return nightly_html.render_consolidated(reports, aggregate, date_str)
