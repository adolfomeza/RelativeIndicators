"""NADRO EOD review — análisis narrativo post-cierre del snapshot del día.

IMPORTANTE: esto NO genera hipos nuevos. Es un REVIEW de lo que pasó con el
snapshot que se tomó al pit open. La disciplina NADRO es:

  - 1 snapshot por instrumento por día (al pit open)
  - 1 review por instrumento por día (al pit close, narrativo)

El review responde:
  - ¿Qué hipos propusimos al open?
  - ¿Qué pasó con cada uno (filled / stopped / not_triggered)?
  - MAE/MFE por hipo
  - STOP TIGHT detection (stopped_out pero setup_reached_t1+)
  - Lecciones aprendidas (sección placeholder para el trader)

Output: markdown + dict estructurado. El markdown se puede renderizar a HTML
en el briefing diario o leer directo en consola.
"""
from __future__ import annotations

import json
from datetime import datetime
from pathlib import Path

from ..paths import markups_dir


PIT_HOURS_VET = {
    "MGC": ("08:20", "13:30"),
    "MCL": ("09:00", "14:30"),
    "MES": ("09:30", "16:00"),
    "MNQ": ("09:30", "16:00"),
    "MYM": ("09:30", "16:00"),
    "M2K": ("09:30", "16:00"),
}


def _emoji_for_status(status: str) -> str:
    return {
        "filled":         "[FILLED]",
        "stopped_out":    "[STOPPED]",
        "not_triggered": "[NOT TRIG]",
        "triggered":     "[OPEN]",
        "pending":       "[PENDING]",
    }.get(status, f"[{status.upper()}]")


def _fmt_pts(v) -> str:
    if v is None:
        return "-"
    try:
        return f"{float(v):.1f}"
    except (ValueError, TypeError):
        return str(v)


def _fmt_ts(ts: str | None) -> str:
    if not ts:
        return "-"
    return ts.replace("T", " ")[:16]


def review_hypo(h: dict) -> dict:
    """Extrae campos relevantes de un hipo + clasifica su outcome."""
    oc = h.get("outcome") or {}
    trade_status = oc.get("trade_status") or oc.get("status", "pending")
    t1 = bool(oc.get("setup_reached_t1"))
    t2 = bool(oc.get("setup_reached_t2"))
    t3 = bool(oc.get("setup_reached_t3"))

    is_stop_tight = (trade_status == "stopped_out") and t1
    is_big_win    = (trade_status == "filled") and t3
    is_dead       = trade_status == "not_triggered"

    classification = "WIN" if is_big_win else \
                     "WIN_MINOR" if trade_status == "filled" else \
                     "STOP_TIGHT" if is_stop_tight else \
                     "STOP_GENUINE" if trade_status == "stopped_out" else \
                     "DEAD" if is_dead else \
                     "OPEN"

    targets_hit = oc.get("targets_hit", [])

    return {
        "id":           h.get("id"),
        "direction":    h.get("direction"),
        "setup_type":   h.get("setup_type"),
        "companions":   h.get("setup_companions") or [],
        "horizon":      h.get("trading_horizon"),
        "entry":        h.get("entry"),
        "stop":         h.get("stop"),
        "risk_pts":     h.get("risk_pts"),
        "grade":        h.get("grade"),
        "targets":      h.get("targets", []),
        "trade_status": trade_status,
        "reached_t1":   t1,
        "reached_t2":   t2,
        "reached_t3":   t3,
        "targets_hit":  targets_hit,
        "triggered_at": oc.get("triggered_at"),
        "stop_hit_at":  oc.get("stop_hit_at"),
        "mae_pts":      oc.get("mae_pts"),
        "mfe_pts":      oc.get("mfe_pts"),
        "classification": classification,
    }


def review_snapshot(snap: dict) -> dict:
    """Analiza un snapshot completo y devuelve stats + hipos analizados."""
    hypos = [review_hypo(h) for h in snap.get("hypos", [])]

    counts = {"WIN": 0, "WIN_MINOR": 0, "STOP_TIGHT": 0, "STOP_GENUINE": 0, "DEAD": 0, "OPEN": 0}
    for h in hypos:
        counts[h["classification"]] = counts.get(h["classification"], 0) + 1

    triggered = sum(1 for h in hypos if h["trade_status"] in ("filled", "stopped_out", "triggered"))
    wins      = counts["WIN"] + counts["WIN_MINOR"]

    return {
        "snapshot_id":      snap.get("id"),
        "timestamp":        snap.get("timestamp"),
        "price_at_analysis": snap.get("price_at_analysis"),
        "regime":           snap.get("regime"),
        "bias":             snap.get("bias"),
        "summary":          snap.get("summary"),
        "hypos":            hypos,
        "counts":           counts,
        "triggered":        triggered,
        "wins":             wins,
        "win_rate":         (wins / triggered) if triggered else None,
        "n_confluences":    len(snap.get("confluences", [])),
        "n_levels":         len(snap.get("levels", [])),
    }


# ============================================================================
# Narrative generation
# ============================================================================

def _narrative_for_hypo(h: dict, instrument: str) -> str:
    direction = h["direction"]
    setup = h["setup_type"]
    companions = h["companions"]
    setup_str = setup
    if companions:
        setup_str += " / " + " / ".join(companions)

    lines = []
    header = f"### HYPO {h['id']} — {direction} {setup_str} {h['grade']}"
    lines.append(header)
    lines.append(f"Entry {h['entry']} | Stop {h['stop']} | Risk {h['risk_pts']} pts")

    if h["targets"]:
        tgt_str = " | ".join(
            f"T{i+1} {t.get('price')} (RR {t.get('rr', 0):.1f})"
            for i, t in enumerate(h["targets"])
        )
        lines.append(f"Targets: {tgt_str}")

    status_line = _emoji_for_status(h["trade_status"])
    lines.append(f"\n**Outcome {status_line}**")

    if h["triggered_at"]:
        lines.append(f"Triggered at {_fmt_ts(h['triggered_at'])}")
    if h["stop_hit_at"]:
        lines.append(f"Stop hit at {_fmt_ts(h['stop_hit_at'])}")

    if h["targets_hit"]:
        th = ", ".join(f"T{i+1}" for i in h["targets_hit"])
        lines.append(f"Targets alcanzados: {th}")

    lines.append(f"MAE {_fmt_pts(h['mae_pts'])} pts | MFE {_fmt_pts(h['mfe_pts'])} pts")

    # Lección por clasificación
    cls = h["classification"]
    if cls == "STOP_TIGHT":
        last_t = 3 if h["reached_t3"] else 2 if h["reached_t2"] else 1
        lines.append(
            f"\n**STOP TIGHT**: setup alcanzó T{last_t} post-stop. "
            f"El stop fue muy ajustado vs rango natural del día. "
            f"Revisar regla de stop vs rango overnight/ATR."
        )
    elif cls == "WIN" and h["trade_status"] == "filled":
        lines.append(
            "\n**BIG WIN**: filled completo (T1+T2+T3). Setup y gestión correctos."
        )
    elif cls == "WIN_MINOR" and h["trade_status"] == "filled":
        lines.append(
            "\n**WIN parcial**: filled en T1. Considerar si T2/T3 estaban bien ubicados."
        )
    elif cls == "STOP_GENUINE":
        lines.append(
            "\n**STOP genuino**: ni T1 alcanzado. Setup no se confirmó — bias "
            "probablemente equivocado o entry muy lejos de la zona de rechazo."
        )
    elif cls == "DEAD":
        lines.append(
            "\n**NOT TRIGGERED**: precio nunca tocó el entry en el pit. "
            "¿Entry muy lejos del precio de apertura?"
        )

    return "\n".join(lines)


def generate_markdown(review: dict, instrument: str, date_str: str) -> str:
    """Genera el markdown narrativo del review."""
    pit_open, pit_close = PIT_HOURS_VET.get(instrument, ("?", "?"))

    lines = []
    lines.append(f"# EOD Review — {instrument} {date_str}")
    lines.append(f"Pit session: {pit_open} – {pit_close} VET")
    lines.append("")

    if "error" in review:
        lines.append(f"**Error**: {review['error']}")
        return "\n".join(lines)

    lines.append(f"## Snapshot al open")
    lines.append(f"- **ID**: `{review['snapshot_id']}`")
    lines.append(f"- **Timestamp**: {_fmt_ts(review['timestamp'])}")
    lines.append(f"- **Precio al análisis**: {review['price_at_analysis']}")
    lines.append(f"- **Regime**: {review['regime'] or '(no especificado)'}")
    lines.append(f"- **Bias**: {review['bias'] or '(no especificado)'}")
    if review["summary"]:
        lines.append(f"- **Summary**: {review['summary']}")
    lines.append(f"- **Confluencias detectadas**: {review['n_confluences']}")
    lines.append(f"- **Niveles tracked**: {review['n_levels']}")
    lines.append("")

    # Stats
    c = review["counts"]
    lines.append(f"## Resultado de la sesión")
    lines.append(f"- Hipos propuestos: **{len(review['hypos'])}**")
    lines.append(f"- Triggered: **{review['triggered']}**")
    lines.append(f"- Wins (filled): **{c['WIN'] + c['WIN_MINOR']}** (big: {c['WIN']}, parcial: {c['WIN_MINOR']})")
    lines.append(f"- Stopped: **{c['STOP_TIGHT'] + c['STOP_GENUINE']}** (stop tight: {c['STOP_TIGHT']}, genuino: {c['STOP_GENUINE']})")
    lines.append(f"- Not triggered: **{c['DEAD']}**")
    if review["win_rate"] is not None:
        lines.append(f"- Win rate: **{review['win_rate']*100:.0f}%**")
    lines.append("")

    # Hipos
    lines.append(f"## Detalle por hipo")
    for h in review["hypos"]:
        lines.append("")
        lines.append(_narrative_for_hypo(h, instrument))

    # STOP TIGHT summary
    stop_tights = [h for h in review["hypos"] if h["classification"] == "STOP_TIGHT"]
    if stop_tights:
        lines.append("")
        lines.append(f"## STOP TIGHT — stops que cortaron setups válidos")
        for h in stop_tights:
            last_t = 3 if h["reached_t3"] else 2 if h["reached_t2"] else 1
            lines.append(
                f"- **{h['id']}** {h['direction']} {h['setup_type']}: "
                f"stop {h['stop']} (MAE {_fmt_pts(h['mae_pts'])}), "
                f"setup llegó a T{last_t} (MFE {_fmt_pts(h['mfe_pts'])}). "
                f"Stop {_fmt_pts(h['mae_pts'])}/{_fmt_pts(h['mfe_pts'])} = "
                f"{(float(h['mae_pts'])/float(h['mfe_pts'])*100) if h['mfe_pts'] else 0:.0f}% del MFE real."
            )

    # Learnings placeholder
    lines.append("")
    lines.append(f"## Aprendizajes de hoy")
    lines.append("_(completar manualmente tras revisar el chart)_")
    lines.append("")
    lines.append("1. _¿Qué funcionó? (setup, entry, stop, target)_")
    lines.append("2. _¿Qué no funcionó?_")
    lines.append("3. _¿Qué haría distinto mañana?_")
    lines.append("")

    return "\n".join(lines)


# ============================================================================
# Main entry
# ============================================================================

def eod_review(instrument: str, date_str: str | None = None) -> dict:
    """Genera el review EOD de un instrumento para un día.

    Si hay múltiples snapshots para el día (no debería en workflow NADRO), usa
    el último (most recent timestamp). El review incluye narrativo markdown +
    dict estructurado para usar en briefing HTML.

    Returns:
        {
          "instrument": str,
          "date": str,
          "review": dict (ver review_snapshot),
          "markdown": str,
          "md_path": str (ruta al archivo guardado si exitoso),
        }
    """
    if not date_str:
        date_str = datetime.now().strftime("%Y-%m-%d")

    master = instrument.split()[0].split("-")[0].upper()
    mdir = markups_dir()
    path = mdir / f"{master}_{date_str}.json"
    if not path.is_file():
        return {"error": f"no existe {path}", "instrument": master, "date": date_str}

    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:  # noqa: BLE001
        return {"error": f"no se pudo parsear {path}: {exc}"}

    snapshots = data.get("snapshots", [])
    if not snapshots:
        return {"error": f"no hay snapshots en {path}", "instrument": master, "date": date_str}

    # Usar el snapshot más reciente (normalmente hay 1 en workflow NADRO correcto)
    snap = max(snapshots, key=lambda s: s.get("timestamp", ""))
    review = review_snapshot(snap)

    md = generate_markdown(review, master, date_str)

    # Guardar a disco en Docs/Nadro/eod_reviews/
    reviews_dir = mdir.parent / "eod_reviews"
    reviews_dir.mkdir(parents=True, exist_ok=True)
    out_path = reviews_dir / f"{master}_{date_str}.md"
    out_path.write_text(md, encoding="utf-8")

    return {
        "instrument": master,
        "date": date_str,
        "pit_session": PIT_HOURS_VET.get(master),
        "review": review,
        "markdown": md,
        "md_path": str(out_path),
    }


def eod_review_all(date_str: str | None = None) -> dict:
    """Corre EOD review para los 6 instrumentos NADRO estándar."""
    if not date_str:
        date_str = datetime.now().strftime("%Y-%m-%d")

    instruments = ["MGC", "MCL", "MES", "MNQ", "MYM", "M2K"]
    results = {}
    for inst in instruments:
        results[inst] = eod_review(inst, date_str)

    # Agregado consolidado
    total_hypos = 0
    total_triggered = 0
    total_wins = 0
    total_stop_tight = 0
    for inst, r in results.items():
        if "error" in r:
            continue
        rv = r["review"]
        total_hypos += len(rv["hypos"])
        total_triggered += rv["triggered"]
        total_wins += rv["wins"]
        total_stop_tight += rv["counts"].get("STOP_TIGHT", 0)

    return {
        "date": date_str,
        "reviews": results,
        "aggregate": {
            "total_hypos":      total_hypos,
            "total_triggered":  total_triggered,
            "total_wins":       total_wins,
            "total_stop_tight": total_stop_tight,
            "win_rate":         (total_wins / total_triggered) if total_triggered else None,
        },
    }
