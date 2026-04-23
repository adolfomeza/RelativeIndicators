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
    """Clasifica un hipo según LO QUE EL TRADER REALMENTE CAPTURÓ.

    Distinguir crítico:
    - `targets_hit_before_stop`: lo que el trader ganó (T1/T2/T3 antes del stop)
    - `targets_hit_after_stop`: validación del setup (T2/T3 tocados POST-stop,
      pero el trader ya estaba afuera — no contó como ganancia)
    - `setup_reached_tN`: incluye ambos (validación del setup, no del trade)

    Clasificaciones:
    - **BIG_WIN**: T1+T2+T3 hit ANTES del stop (trade tomó todo el movimiento)
    - **WIN**: T2 antes del stop (trade tomó al menos T1 y T2)
    - **WIN_MINOR**: solo T1 antes del stop (trade tomó T1, después stopped o
      todavía corriendo)
    - **STOP_TIGHT**: stopped_out PERO el setup eventualmente alcanzó T1+ post-stop
      (stop fue muy ajustado vs el rango natural — setup era válido)
    - **STOP_GENUINE**: stopped sin que el setup alcance T1 (setup mal direccionado)
    - **DEAD**: not_triggered
    - **OPEN**: triggered pero todavía abierto (sin T1 ni stop hit)
    """
    oc = h.get("outcome") or {}
    trade_status = oc.get("trade_status") or oc.get("status", "pending")

    # Targets hit antes vs después del stop
    th_before = oc.get("targets_hit_before_stop") or []
    th_after  = oc.get("targets_hit_after_stop") or []
    th_all    = oc.get("targets_hit") or list(set(th_before) | set(th_after))

    # Setup reached flags (incluyen pre+post stop = validación del setup)
    reached_t1 = bool(oc.get("setup_reached_t1"))
    reached_t2 = bool(oc.get("setup_reached_t2"))
    reached_t3 = bool(oc.get("setup_reached_t3"))

    # Trade reached flags (SOLO antes del stop = lo que el trader capturó)
    trade_t1 = 0 in th_before
    trade_t2 = 1 in th_before
    trade_t3 = 2 in th_before

    if trade_status == "filled":
        if trade_t3:
            classification = "BIG_WIN"
        elif trade_t2:
            classification = "WIN"
        elif trade_t1:
            classification = "WIN_MINOR"
        else:
            # Edge case: filled marcado pero ningún target hit antes del stop
            classification = "WIN_MINOR"
    elif trade_status == "stopped_out":
        # STOP_TIGHT si el setup eventualmente alcanzó T1+ post-stop
        if reached_t1 and not trade_t1:
            classification = "STOP_TIGHT"
        else:
            classification = "STOP_GENUINE"
    elif trade_status == "not_triggered":
        classification = "DEAD"
    else:
        classification = "OPEN"

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
        # Validación del setup (incluye post-stop)
        "reached_t1":   reached_t1,
        "reached_t2":   reached_t2,
        "reached_t3":   reached_t3,
        # Capturado por el trader (antes del stop)
        "trade_t1":     trade_t1,
        "trade_t2":     trade_t2,
        "trade_t3":     trade_t3,
        "targets_hit_before_stop": th_before,
        "targets_hit_after_stop":  th_after,
        "targets_hit":  th_all,
        "triggered_at": oc.get("triggered_at"),
        "stop_hit_at":  oc.get("stop_hit_at"),
        "mae_pts":      oc.get("mae_pts"),
        "mfe_pts":      oc.get("mfe_pts"),
        "classification": classification,
    }


def review_snapshot(snap: dict) -> dict:
    """Analiza un snapshot completo y devuelve stats + hipos analizados."""
    hypos = [review_hypo(h) for h in snap.get("hypos", [])]

    counts = {"BIG_WIN": 0, "WIN": 0, "WIN_MINOR": 0, "STOP_TIGHT": 0, "STOP_GENUINE": 0, "DEAD": 0, "OPEN": 0}
    for h in hypos:
        counts[h["classification"]] = counts.get(h["classification"], 0) + 1

    triggered = sum(1 for h in hypos if h["trade_status"] in ("filled", "stopped_out", "triggered"))
    wins      = counts["BIG_WIN"] + counts["WIN"] + counts["WIN_MINOR"]

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

    # Lección por clasificación (basada en LO QUE EL TRADER CAPTURÓ)
    cls = h["classification"]
    th_before = h.get("targets_hit_before_stop", [])
    th_after  = h.get("targets_hit_after_stop", [])
    if cls == "BIG_WIN":
        lines.append(
            "\n**BIG WIN**: T1+T2+T3 alcanzados ANTES del stop. "
            "Trade capturó el movimiento completo. Setup + gestión óptimos."
        )
    elif cls == "WIN":
        lines.append(
            "\n**WIN**: T1+T2 alcanzados antes del stop (T3 no se logró pre-stop). "
            f"Trade capturó ~2/3 del rango estimado."
        )
        if th_after:
            t_labels = "/".join(f"T{i+1}" for i in th_after)
            lines.append(f"   Info: {t_labels} SÍ se alcanzó post-stop (validación del setup), pero trader ya estaba afuera.")
    elif cls == "WIN_MINOR":
        msg = "\n**WIN parcial**: solo T1 alcanzado antes del stop."
        if th_after:
            t_labels = "/".join(f"T{i+1}" for i in th_after)
            msg += f" Después del stop el precio reach {t_labels} (setup era válido, pero trade se cortó en T1)."
        else:
            msg += " Trade exitó en T1, precio no extendió más. Evaluar si T2/T3 estaban mal ubicados."
        lines.append(msg)
    elif cls == "STOP_TIGHT":
        last_t = 3 if h["reached_t3"] else 2 if h["reached_t2"] else 1
        lines.append(
            f"\n**STOP TIGHT**: stop cortó el trade, pero el setup eventualmente "
            f"alcanzó T{last_t} post-stop. Stop fue más ajustado que el rango natural. "
            f"Revisar regla de stop vs rango overnight/ATR."
        )
    elif cls == "STOP_GENUINE":
        lines.append(
            "\n**STOP genuino**: stopped sin que el setup alcance T1 "
            "(ni pre ni post-stop). Setup mal direccionado — bias "
            "equivocado o entry muy lejos de la zona de rechazo."
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
    lines.append(f"- Wins (filled): **{c['BIG_WIN'] + c['WIN'] + c['WIN_MINOR']}** "
                 f"(big: {c['BIG_WIN']}, medio: {c['WIN']}, parcial: {c['WIN_MINOR']})")
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


def eod_review_all(date_str: str | None = None, write_html: bool = True) -> dict:
    """Corre EOD review para los 6 instrumentos NADRO estándar.

    Si ``write_html=True``, también genera un HTML consolidado en
    ``Docs/Nadro/eod_reviews/eod_all_{date}.html``.
    """
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

    aggregate = {
        "total_hypos":      total_hypos,
        "total_triggered":  total_triggered,
        "total_wins":       total_wins,
        "total_stop_tight": total_stop_tight,
        "win_rate":         (total_wins / total_triggered) if total_triggered else None,
    }

    html_path = None
    if write_html:
        reviews_dir = markups_dir().parent / "eod_reviews"
        reviews_dir.mkdir(parents=True, exist_ok=True)
        html_path = reviews_dir / f"eod_all_{date_str}.html"
        html_content = generate_html_consolidated(results, aggregate, date_str)
        html_path.write_text(html_content, encoding="utf-8")
        html_path = str(html_path)

    return {
        "date": date_str,
        "reviews": results,
        "aggregate": aggregate,
        "html_path": html_path,
    }


# ============================================================================
# HTML consolidated generator
# ============================================================================

def _status_badge(status: str) -> str:
    """Devuelve HTML span con color según status."""
    colors = {
        "filled":        ("#16a34a", "FILLED"),
        "stopped_out":   ("#dc2626", "STOPPED"),
        "not_triggered": ("#64748b", "NOT TRIG"),
        "triggered":     ("#2563eb", "OPEN"),
        "pending":       ("#94a3b8", "PENDING"),
    }
    color, label = colors.get(status, ("#94a3b8", status.upper()))
    return f'<span style="background:{color};color:#fff;padding:2px 8px;border-radius:4px;font-size:11px;font-weight:600;">{label}</span>'


def _classification_badge(cls: str) -> str:
    colors = {
        "BIG_WIN":      ("#15803d", "BIG WIN"),
        "WIN":          ("#22c55e", "WIN"),
        "WIN_MINOR":    ("#16a34a", "WIN T1"),
        "STOP_TIGHT":   ("#f59e0b", "STOP TIGHT"),
        "STOP_GENUINE": ("#dc2626", "STOP"),
        "DEAD":         ("#6b7280", "DEAD"),
        "OPEN":         ("#3b82f6", "OPEN"),
    }
    color, label = colors.get(cls, ("#6b7280", cls))
    return f'<span style="background:{color};color:#fff;padding:2px 8px;border-radius:4px;font-size:10px;font-weight:700;letter-spacing:0.5px;">{label}</span>'


def generate_html_consolidated(reviews: dict, aggregate: dict, date_str: str) -> str:
    """Genera HTML consolidado con los 6 reviews en una sola página."""
    import html as _html

    wr = aggregate.get("win_rate")
    wr_str = f"{wr*100:.0f}%" if wr is not None else "N/A"

    parts = []
    parts.append(f"""<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>EOD Review NADRO — {date_str}</title>
<style>
  body {{ font-family: 'Segoe UI', system-ui, sans-serif; background: #0f172a; color: #e2e8f0; margin: 0; padding: 24px; line-height: 1.5; }}
  h1 {{ font-size: 28px; margin: 0 0 4px; color: #f1f5f9; }}
  h2 {{ font-size: 20px; margin: 32px 0 12px; color: #f1f5f9; border-bottom: 2px solid #334155; padding-bottom: 6px; }}
  h3 {{ font-size: 16px; margin: 16px 0 8px; color: #cbd5e1; }}
  .subtitle {{ color: #94a3b8; font-size: 14px; margin-bottom: 24px; }}
  .aggregate {{ display: grid; grid-template-columns: repeat(5, 1fr); gap: 12px; margin: 16px 0 32px; }}
  .stat {{ background: #1e293b; border: 1px solid #334155; border-radius: 8px; padding: 16px; text-align: center; }}
  .stat .label {{ font-size: 11px; color: #94a3b8; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 4px; }}
  .stat .value {{ font-size: 24px; font-weight: 700; color: #f1f5f9; }}
  .stat .value.win {{ color: #4ade80; }}
  .stat .value.stop {{ color: #fca5a5; }}
  .stat .value.tight {{ color: #fbbf24; }}
  .instrument-card {{ background: #1e293b; border: 1px solid #334155; border-radius: 12px; padding: 20px; margin-bottom: 20px; }}
  .instrument-header {{ display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; }}
  .instrument-header h2 {{ margin: 0; border: none; padding: 0; font-size: 22px; color: #f1f5f9; }}
  .instrument-meta {{ font-size: 12px; color: #94a3b8; }}
  .snapshot-info {{ background: #0f172a; border-radius: 8px; padding: 12px 16px; margin-bottom: 16px; font-size: 13px; }}
  .snapshot-info strong {{ color: #cbd5e1; }}
  .hypo {{ background: #0f172a; border-left: 3px solid #475569; border-radius: 0 8px 8px 0; padding: 12px 16px; margin: 12px 0; }}
  .hypo.WIN {{ border-left-color: #16a34a; }}
  .hypo.WIN_MINOR {{ border-left-color: #22c55e; }}
  .hypo.STOP_TIGHT {{ border-left-color: #f59e0b; }}
  .hypo.STOP_GENUINE {{ border-left-color: #dc2626; }}
  .hypo.DEAD {{ border-left-color: #6b7280; }}
  .hypo-header {{ display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px; flex-wrap: wrap; gap: 8px; }}
  .hypo-title {{ font-weight: 600; color: #f1f5f9; font-size: 15px; }}
  .hypo-badges {{ display: flex; gap: 6px; }}
  .hypo-details {{ font-size: 12px; color: #cbd5e1; display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 8px; margin: 8px 0; }}
  .hypo-details .field {{ background: #1e293b; padding: 4px 8px; border-radius: 4px; }}
  .hypo-details .field .label {{ color: #64748b; font-size: 10px; text-transform: uppercase; }}
  .hypo-note {{ font-size: 12px; color: #fbbf24; margin-top: 8px; padding: 6px 10px; background: rgba(251,191,36,0.1); border-radius: 4px; }}
  .hypo-note.win {{ color: #4ade80; background: rgba(74,222,128,0.1); }}
  .hypo-note.stop {{ color: #fca5a5; background: rgba(252,165,165,0.1); }}
  .targets {{ font-size: 11px; color: #94a3b8; margin-top: 4px; }}
  .error {{ color: #fca5a5; font-style: italic; }}
  a {{ color: #60a5fa; }}
  footer {{ margin-top: 48px; padding-top: 16px; border-top: 1px solid #334155; color: #64748b; font-size: 12px; text-align: center; }}
</style>
</head>
<body>
<h1>EOD Review NADRO — {date_str}</h1>
<div class="subtitle">Análisis post-cierre de los 6 instrumentos. 1 snapshot por instrumento al pit open, review narrativo al pit close.</div>

<div class="aggregate">
  <div class="stat"><div class="label">Hipos propuestos</div><div class="value">{aggregate['total_hypos']}</div></div>
  <div class="stat"><div class="label">Triggered</div><div class="value">{aggregate['total_triggered']}</div></div>
  <div class="stat"><div class="label">Wins</div><div class="value win">{aggregate['total_wins']}</div></div>
  <div class="stat"><div class="label">Stop Tight</div><div class="value tight">{aggregate['total_stop_tight']}</div></div>
  <div class="stat"><div class="label">Win rate</div><div class="value">{wr_str}</div></div>
</div>
""")

    for inst in ["MGC", "MCL", "MES", "MNQ", "MYM", "M2K"]:
        r = reviews.get(inst, {})
        if "error" in r:
            parts.append(f'<div class="instrument-card"><h2>{inst}</h2><p class="error">{_html.escape(r["error"])}</p></div>')
            continue

        rv = r["review"]
        pit_open, pit_close = PIT_HOURS_VET.get(inst, ("?", "?"))
        c = rv["counts"]
        wins = c["WIN"] + c["WIN_MINOR"]
        stopped = c["STOP_TIGHT"] + c["STOP_GENUINE"]
        wr_inst = f"{rv['win_rate']*100:.0f}%" if rv["win_rate"] is not None else "N/A"

        bias = _html.escape(rv.get("bias") or "—")
        summary = _html.escape(rv.get("summary") or "")
        regime = _html.escape(rv.get("regime") or "—")
        price = rv.get("price_at_analysis") or "—"
        timestamp = _fmt_ts(rv.get("timestamp"))

        parts.append(f"""
<div class="instrument-card">
  <div class="instrument-header">
    <h2>{inst}</h2>
    <div class="instrument-meta">Pit {pit_open}–{pit_close} VET · {len(rv['hypos'])} hipos · WR {wr_inst}</div>
  </div>
  <div class="snapshot-info">
    <strong>Snapshot</strong> {_html.escape(rv['snapshot_id'] or '')} @ {timestamp} · <strong>Precio</strong> {price} · <strong>Bias</strong> {bias} · <strong>Regime</strong> {regime}<br>
    <em>{summary}</em>
  </div>
  <div style="font-size:13px;color:#cbd5e1;margin-bottom:8px;">
    Triggered {rv['triggered']} · Wins {wins} (big {c['WIN']}, parcial {c['WIN_MINOR']}) · Stopped {stopped} (tight {c['STOP_TIGHT']}, genuino {c['STOP_GENUINE']}) · Dead {c['DEAD']}
  </div>
""")

        for h in rv["hypos"]:
            cls = h["classification"]
            companions = (" / " + " / ".join(h["companions"])) if h["companions"] else ""
            setup_str = _html.escape((h["setup_type"] or "") + companions)
            direction = h["direction"] or ""

            targets_html = ""
            if h["targets"]:
                hit_set = set(h["targets_hit"] or [])
                tgt_parts = []
                for i, t in enumerate(h["targets"]):
                    marker = "✓" if i in hit_set else "·"
                    tgt_parts.append(f'{marker} T{i+1} {t.get("price")} (RR {float(t.get("rr",0)):.1f})')
                targets_html = f'<div class="targets">{" | ".join(tgt_parts)}</div>'

            # note
            note_html = ""
            if cls == "STOP_TIGHT":
                last_t = 3 if h["reached_t3"] else 2 if h["reached_t2"] else 1
                note_html = f'<div class="hypo-note">⚠ STOP TIGHT: setup alcanzó T{last_t} post-stop. Stop fue más ajustado que el rango natural.</div>'
            elif cls == "WIN":
                note_html = f'<div class="hypo-note win">✓ BIG WIN: filled completo T1+T2+T3.</div>'
            elif cls == "WIN_MINOR":
                note_html = f'<div class="hypo-note win">✓ WIN parcial: filled en T1.</div>'
            elif cls == "STOP_GENUINE":
                note_html = f'<div class="hypo-note stop">✗ STOP genuino: setup no se confirmó.</div>'
            elif cls == "DEAD":
                note_html = f'<div class="hypo-note">— NOT TRIGGERED: precio nunca tocó el entry en el pit.</div>'

            parts.append(f"""
  <div class="hypo {cls}">
    <div class="hypo-header">
      <div class="hypo-title">HYPO {h['id']} — {direction} {setup_str} <span style="color:#94a3b8;font-size:12px;">{h['grade'] or ''}</span></div>
      <div class="hypo-badges">{_status_badge(h['trade_status'])}{_classification_badge(cls)}</div>
    </div>
    <div class="hypo-details">
      <div class="field"><div class="label">Entry</div>{h['entry']}</div>
      <div class="field"><div class="label">Stop</div>{h['stop']} ({h['risk_pts']} pts)</div>
      <div class="field"><div class="label">MAE / MFE</div>{_fmt_pts(h['mae_pts'])} / {_fmt_pts(h['mfe_pts'])} pts</div>
      <div class="field"><div class="label">Triggered</div>{_fmt_ts(h['triggered_at'])}</div>
    </div>
    {targets_html}
    {note_html}
  </div>
""")

        parts.append("</div>")

    parts.append(f"""
<footer>
  Generado por <code>nadro_eod_review_all('{date_str}')</code> · NADRO workflow end-to-end ·
  Regla: 1 snapshot pit open + 1 review pit close.
</footer>
</body>
</html>
""")

    return "".join(parts)
