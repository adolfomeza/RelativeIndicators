"""Renderers del NADRO Nightly Report — markdown individual + HTML consolidado.

Mantenido fuera de `nightly_report.py` para que ese archivo quede por debajo
de 700 LOC (límite del project plan).
"""
from __future__ import annotations

import html as _html

from .nightly_helpers import PIT_SESSIONS


def _fmt_ts(ts: str | None) -> str:
    if not ts:
        return "-"
    return str(ts).replace("T", " ")[:16]


def _fmt_pts(v) -> str:
    if v is None:
        return "-"
    try:
        return f"{float(v):.2f}"
    except (ValueError, TypeError):
        return str(v)


def _build_trade_narrative(h: dict) -> list[str]:
    """Reconstruye el timeline cronológico de qué pasó con el trade.

    Usa triggered_at, stop_hit_at, targets_hit_before_stop, targets_hit_after_stop
    para narrar el orden real de los eventos como los vivió el trader.
    """
    direction = (h.get("direction") or "").upper()
    entry = h.get("entry")
    stop = h.get("stop")
    targets = h.get("targets", []) or []
    trade_status = h.get("trade_status")

    triggered_at = h.get("triggered_at")
    stop_hit_at = h.get("stop_hit_at")
    th_before = h.get("targets_hit_before_stop", []) or []
    th_after  = h.get("targets_hit_after_stop", []) or []

    if trade_status == "not_triggered":
        return [
            f"- Entry @ {entry}: **NUNCA TOCADO** durante la pit session.",
            f"- El precio se movió sin acercarse al entry, o estaba muy lejos del open price.",
        ]

    if trade_status == "pending":
        return [f"- Trade pending — sin datos walk-forward aún."]

    lines = []
    # 1. Entry
    if triggered_at:
        lines.append(f"1. **{_fmt_ts(triggered_at)}** — entry {direction} @ {entry}")
    else:
        lines.append(f"1. Entry {direction} @ {entry} (timestamp no registrado)")

    # 2. Construir orden cronológico de eventos
    # Si filled = primer evento fue un target. Si stopped_out = primer evento fue stop.
    # En filled, los targets en th_before ocurrieron antes del stop_hit_at.
    # En stopped_out, los targets en th_after ocurrieron después.

    step = 2
    if trade_status == "filled":
        # T1 (y posiblemente T2/T3) hit antes del stop
        for ti in sorted(th_before):
            if ti < len(targets):
                t_price = targets[ti].get("price")
                gain = abs((t_price - entry) if entry else 0)
                lines.append(
                    f"{step}. T{ti+1} = {t_price} hit ANTES del stop "
                    f"({direction} ganó +{gain:.2f} pts hasta este punto). "
                    f"Trader podría haber salido aquí."
                )
                step += 1
        if stop_hit_at:
            lines.append(
                f"{step}. **{_fmt_ts(stop_hit_at)}** — stop {stop} tocado "
                f"(después de T1+ → si trader salió antes, no le afectó)"
            )
            step += 1
        # Targets after stop
        for ti in sorted(th_after):
            if ti < len(targets):
                lines.append(
                    f"{step}. T{ti+1} = {targets[ti].get('price')} alcanzado POST-stop "
                    f"(setup siguió, pero trader ya estaba afuera tras el stop)"
                )
                step += 1

    elif trade_status == "stopped_out":
        if stop_hit_at:
            lines.append(
                f"2. **{_fmt_ts(stop_hit_at)}** — stop {stop} tocado primero "
                f"(antes de cualquier target). Trade cerró en pérdida."
            )
            step = 3
        for ti in sorted(th_after):
            if ti < len(targets):
                lines.append(
                    f"{step}. T{ti+1} = {targets[ti].get('price')} alcanzado POST-stop "
                    f"(stop tight: el setup era válido pero el stop fue muy ajustado)"
                )
                step += 1

    elif trade_status == "triggered":
        lines.append(
            f"2. Trade triggered pero todavía abierto al cierre — "
            f"sin stop ni target hit aún."
        )

    # MAE/MFE interpretation
    mae = h.get("mae_pts")
    mfe = h.get("mfe_pts")
    if mae is not None and mfe is not None:
        try:
            mae_f, mfe_f = float(mae), float(mfe)
            if direction == "LONG":
                worst_low = (entry - mae_f) if entry else None
                best_high = (entry + mfe_f) if entry else None
                if worst_low is not None and best_high is not None:
                    lines.append(
                        f"- Excursión: máximo precio en contra {worst_low:.2f} (MAE -{mae_f:.2f}) / "
                        f"máximo a favor {best_high:.2f} (MFE +{mfe_f:.2f})"
                    )
            else:  # SHORT
                worst_high = (entry + mae_f) if entry else None
                best_low = (entry - mfe_f) if entry else None
                if worst_high is not None and best_low is not None:
                    lines.append(
                        f"- Excursión: máximo precio en contra {worst_high:.2f} (MAE +{mae_f:.2f}) / "
                        f"máximo a favor {best_low:.2f} (MFE -{mfe_f:.2f})"
                    )
        except (ValueError, TypeError):
            pass

    return lines


def _build_trade_verdict(h: dict) -> str:
    """Veredicto interpretativo según la clasificación."""
    cls = h.get("classification")
    th_before = h.get("targets_hit_before_stop", []) or []
    th_after  = h.get("targets_hit_after_stop", []) or []

    if cls == "BIG_WIN":
        return ("**BIG WIN** — T1+T2+T3 alcanzados ANTES del stop. Trade capturó todo "
                "el rango. Setup + gestión óptimos.")
    if cls == "WIN":
        msg = ("**WIN** — T1+T2 alcanzados antes del stop. Trade capturó ~2/3 del "
               "movimiento estimado.")
        if 2 in th_after:
            msg += " T3 también se alcanzó, pero post-stop (no contó al trade)."
        return msg
    if cls == "WIN_MINOR":
        if th_after:
            t_labels = "/".join(f"T{i+1}" for i in th_after)
            return (f"**WIN parcial** — solo T1 antes del stop. Después del stop, el "
                   f"precio extendió hasta {t_labels} (setup era válido pero trade "
                   f"se cortó en T1). Considerar trailing stop más amplio.")
        return ("**WIN parcial** — solo T1 alcanzado. T2/T3 no se tocaron ni siquiera "
                "post-stop. Evaluar si T2/T3 estaban mal ubicados.")
    if cls == "STOP_TIGHT":
        if th_after:
            t_labels = "/".join(f"T{i+1}" for i in th_after)
            return (f"**STOP TIGHT** — stop cortó el trade antes de cualquier target. "
                   f"DESPUÉS del stop, el setup alcanzó {t_labels}. Stop fue muy "
                   f"ajustado vs el rango natural — revisar regla stop vs ATR/overnight.")
        return "**STOP TIGHT** — stop muy ajustado (clasificado como tight)."
    if cls == "STOP_GENUINE":
        return ("**STOP genuino** — stop cortó sin que el setup alcance T1 (ni pre ni "
                "post-stop). Setup mal direccionado: bias equivocado o entry mal ubicado.")
    if cls == "DEAD":
        return ("**NOT TRIGGERED** — precio nunca tocó el entry en el pit. ¿Entry muy "
                "lejos del open? ¿Bias equivocado?")
    if cls == "OPEN":
        return "**OPEN** — trade triggered pero sin resolver al cierre."
    return ""


# ---------------------------------------------------------------------------
# Markdown renderer (NADRO-compliant)
# ---------------------------------------------------------------------------

def render_markdown(report: dict) -> str:
    """Markdown del nightly report individual de un instrumento."""
    inst = report["instrument"]
    date_str = report["date"]
    pit = PIT_SESSIONS.get(inst, ("?", "?"))

    lines: list[str] = []
    lines.append(f"# NADRO Nightly Report — {inst} {date_str}")
    lines.append(f"Pit session: {pit[0]} – {pit[1]} VET")
    lines.append("")

    if "error" in report:
        lines.append(f"**Error**: {report['error']}")
        return "\n".join(lines)

    classic = report.get("classic", {}) or {}
    snap = report.get("snapshot", {}) or {}
    missed = report.get("missed_setups", []) or []
    narrativa = report.get("narrativa", {}) or {}
    aceptacion = report.get("aceptacion", {}) or {}
    dva = report.get("dva", {}) or {}
    ritmo = report.get("ritmo", {}) or {}
    of = report.get("order_flow", {}) or {}
    dissonance = report.get("dissonance", {}) or {}
    lessons = report.get("lessons", []) or []
    tomorrow = report.get("tomorrow_hint")

    # 1. Preparación pre-open
    lines.append("## 1. Preparación pre-open")
    lines.append(f"- **Snapshot ID**: `{snap.get('snapshot_id') or classic.get('snapshot_id') or '-'}`")
    lines.append(f"- **Timestamp**: {_fmt_ts(classic.get('timestamp'))}")
    lines.append(f"- **Precio al análisis**: {classic.get('price_at_analysis')}")
    lines.append(f"- **Regime declarado**: {classic.get('regime') or '(sin especificar)'}")
    lines.append(f"- **Bias declarado**: {classic.get('bias') or '(sin especificar)'}")
    if classic.get("summary"):
        lines.append(f"- **Summary**: {classic['summary']}")
    lines.append(f"- **Hipos propuestas**: {len(classic.get('hypos', []))}")
    lines.append(f"- **Niveles tracked**: {classic.get('n_levels', 0)}")
    lines.append(f"- **Confluencias detectadas**: {classic.get('n_confluences', 0)}")
    lines.append("")

    # 2. Resultado hipos
    c = classic.get("counts", {})
    wins_total = c.get('WIN', 0) + c.get('WIN_MINOR', 0)
    stops_total = c.get('STOP_TIGHT', 0) + c.get('STOP_GENUINE', 0)
    lines.append("## 2. Resultado de hipos (walk-forward)")
    lines.append(
        f"- Triggered: **{classic.get('triggered', 0)}**  ·  Wins: **{wins_total}**"
        f"  ·  Stopped: **{stops_total}**  ·  Dead: **{c.get('DEAD', 0)}**"
    )
    if classic.get("win_rate") is not None:
        lines.append(f"- Win rate: **{classic['win_rate']*100:.0f}%**")
    lines.append("")
    for h in classic.get("hypos", []):
        status_tag = h.get("classification", "?")
        direction = (h.get("direction") or "?").upper()
        setup = h.get("setup_type") or "?"
        lines.append(f"### Hipo {h.get('id')} — {direction} {setup} [{status_tag}]")
        lines.append(f"Entry {h.get('entry')} / Stop {h.get('stop')} / Risk {h.get('risk_pts')} pts")

        # Targets con marca de "antes del stop" vs "después"
        th_before = h.get("targets_hit_before_stop", []) or []
        th_after = h.get("targets_hit_after_stop", []) or []
        targets = h.get("targets", []) or []
        if targets:
            tgt_lines = []
            for i, t in enumerate(targets):
                marker = "✓pre" if i in th_before else ("·post" if i in th_after else "—")
                tgt_lines.append(f"T{i+1}={t.get('price')} (RR {float(t.get('rr',0)):.1f}) [{marker}]")
            lines.append(f"Targets: {' / '.join(tgt_lines)}")
        lines.append(f"MAE {_fmt_pts(h.get('mae_pts'))} pts / MFE {_fmt_pts(h.get('mfe_pts'))} pts")
        lines.append("")

        # NARRACIÓN del trade — timeline cronológico
        narr = _build_trade_narrative(h)
        if narr:
            lines.append("**Narración del trade**:")
            for ln in narr:
                lines.append(ln)
            lines.append("")

        # Veredicto interpretativo
        verdict = _build_trade_verdict(h)
        if verdict:
            lines.append(f"**Veredicto**: {verdict}")
            lines.append("")

    # 3. Missed setups
    lines.append("## 3. Missed Setups — lo que funcionó fuera del snapshot")
    if missed:
        lines.append(f"Se detectaron **{len(missed)}** setups válidos en niveles NO incluidos en el análisis pre-open.")
        lines.append("")
        for i, m in enumerate(missed[:8], 1):
            lines.append(f"### MISSED #{i} — {m['setup_type']} ({m['direction']})")
            lines.append(f"- Nivel: **{m['level_label']}** @ {m['level_price']}")
            lines.append(f"- Touch @ {_fmt_ts(m['touch_time'])} — reversal desde {m['entry_ref']}")
            lines.append(f"- **MFE {m['mfe_pts']} pts** / MAE {m['mae_pts']} pts (ratio {m.get('mae_to_mfe_ratio')})")
            lines.append(f"- Tiempo a MFE: {m['bars_to_mfe']} bars de 1m")
            lines.append("")
    else:
        lines.append("_No se detectaron setups missed con los umbrales actuales (MFE >= 0.10% price, MAE/MFE < 0.6)._")
        lines.append("")

    # 4. Review NADRO
    lines.append("## 4. Review NADRO")
    lines.append("")
    lines.append("### Narrativa (N) — bias desde estructura TPO")
    lines.append(f"- Bias DECLARADO pre-open: **{narrativa.get('bias_stated')}**")
    lines.append(f"- Precio cerró: {narrativa.get('price_direction')} ({narrativa.get('price_change'):+.2f} pts)")
    lines.append(f"- ¿Bias declarado se cumplió hoy?: {narrativa.get('fulfilled_verdict')}")
    lines.append("")
    lines.append("**Estructura TPO de hoy** (lo que define el bias para mañana):")
    tpo_feat = narrativa.get("tpo_features") or {}
    if tpo_feat.get("poor_high"):
        lines.append("- **Alto pobre** detectado → likely revisitar el high mañana")
    if tpo_feat.get("weak_low"):
        lines.append("- **Mínimo débil** detectado → likely revisitar el low mañana")
    if tpo_feat.get("high_excess"):
        lines.append("- Excess en el high → rechazo confirmado por vendedores")
    if tpo_feat.get("low_excess"):
        lines.append("- Excess en el low → rechazo confirmado por compradores")
    if tpo_feat.get("close_vs_poc") and tpo_feat.get("close_vs_poc") != "unknown":
        lines.append(f"- Cierre: {tpo_feat['close_vs_poc']}")
    if tpo_feat.get("day_type") and tpo_feat.get("day_type") != "unknown":
        lines.append(f"- Día tipo: **{tpo_feat['day_type']}**")
    lines.append("")
    lines.append(f"**Bias FORWARD para mañana**: **{narrativa.get('forward_bias', 'neutral')}**")
    for r in narrativa.get("forward_reasons") or []:
        lines.append(f"- {r}")
    for c_line in narrativa.get("commentary", []):
        lines.append(f"- {c_line}")
    lines.append("")
    lines.append("### Aceptación (A)")
    lines.append(f"- {aceptacion.get('summary', '-')}")
    for r in aceptacion.get("rejected", [])[:5]:
        lines.append(f"- **Rechazo**: {r['label']} @ {r['price']} ({r['type']})")
    for a in aceptacion.get("accepted", [])[:5]:
        lines.append(f"- **Aceptado**: {a['label']} @ {a['price']} ({a['type']})")
    lines.append("")
    lines.append("### DVA (D) — Developing Value Areas multi-TF")
    if dva.get("available"):
        lines.append(f"- {dva.get('summary')}")
        lines.append("")
        lines.append("| TF | DVAH | VWAP | DVAL | Posición precio actual |")
        lines.append("|---|---|---|---|---|")
        for d in dva.get("dva_levels", []):
            vwap_v = d.get("vwap")
            vwap_str = f"{vwap_v}" if vwap_v is not None else "-"
            lines.append(f"| **{d['tf']}** | {d['dvah']} | {vwap_str} | {d['dval']} | {d['position']} |")
        lines.append("")
        tpo = dva.get("tpo_intraday")
        if tpo and tpo.get("available"):
            lines.append(f"- **TPO intraday (sesión hoy)**: POC {tpo['poc']} / VAH {tpo['vah']} / VAL {tpo['val']} (rango {tpo['range_pts']} pts)")
    else:
        lines.append(f"- {dva.get('summary', 'No disponible')}")
    lines.append("")
    lines.append("### Ritmo (R)")
    lines.append(f"- {ritmo.get('summary', '-')}")
    lines.append(f"- Régimen: **{ritmo.get('regime')}**")
    if ritmo.get("compression_detected"):
        lines.append(f"- Compresión @ {_fmt_ts(ritmo.get('compression_time'))}")
    if ritmo.get("expansion_detected"):
        lines.append(f"- Expansión @ {_fmt_ts(ritmo.get('expansion_time'))}")
    lines.append("")
    lines.append("### Order Flow (O)")
    lines.append(f"- {of.get('summary', '-')}")
    lines.append(f"- Delta: {of.get('delta_pct')}%  ({of.get('delta_bias')})")
    lines.append(f"- Alineación delta-precio: **{of.get('alignment')}**")
    lines.append("")

    # 5. Disonancia
    if dissonance.get("has_dissonance"):
        lines.append("## 5. Disonancia narrativa")
        lines.append(f"- {dissonance.get('summary')}")
        lines.append("- Si el foco del trader era **LTVWs**, pudo haber tomado largos.")
        lines.append("- Si el foco era **estructural CVA**, pudo haber tomado cortos.")
        lines.append("- Ambas decisiones son válidas dentro de su marco.")
        lines.append("")

    # 6. Lecciones
    lines.append("## 6. Lecciones de hoy")
    lines.append("_Auto-generadas del análisis + espacio para anotación manual._")
    lines.append("")
    for i, lesson in enumerate(lessons, 1):
        lines.append(f"{i}. {lesson}")
    lines.append("")
    lines.append("_Anotaciones manuales del trader:_")
    lines.append("- _(¿Qué funcionó de mi proceso?)_")
    lines.append("- _(¿Qué error repetí?)_")
    lines.append("- _(¿Qué haré distinto mañana?)_")
    lines.append("")

    # 7. Mañana
    if tomorrow:
        lines.append("## 7. Sugerencia para mañana")
        lines.append(f"- {tomorrow}")
        lines.append("")

    return "\n".join(lines)


_CSS = """
  body { font-family: 'Segoe UI', system-ui, sans-serif; background: #0f172a; color: #e2e8f0; margin: 0; padding: 24px; line-height: 1.55; }
  h1 { font-size: 28px; color: #f1f5f9; margin: 0 0 4px; }
  h2 { font-size: 20px; color: #f1f5f9; border-bottom: 2px solid #334155; padding-bottom: 6px; margin: 28px 0 12px; }
  h3 { font-size: 15px; color: #cbd5e1; margin: 14px 0 6px; }
  .subtitle { color: #94a3b8; font-size: 14px; margin-bottom: 20px; }
  .aggregate { display: grid; grid-template-columns: repeat(5, 1fr); gap: 12px; margin: 16px 0 28px; }
  .stat { background: #1e293b; border: 1px solid #334155; border-radius: 8px; padding: 14px; text-align: center; }
  .stat .label { font-size: 11px; color: #94a3b8; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 4px; }
  .stat .value { font-size: 22px; font-weight: 700; color: #f1f5f9; }
  .stat .value.win { color: #4ade80; }
  .stat .value.missed { color: #fbbf24; }
  .instrument { background: #1e293b; border: 1px solid #334155; border-radius: 12px; padding: 20px; margin-bottom: 20px; }
  .inst-hdr { display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; }
  .inst-hdr h2 { margin: 0; border: none; padding: 0; font-size: 22px; }
  .inst-meta { font-size: 12px; color: #94a3b8; }
  .block { background: #0f172a; border-radius: 8px; padding: 10px 14px; margin: 10px 0; font-size: 13px; }
  .block strong { color: #cbd5e1; }
  .nadro-row { display: grid; grid-template-columns: 80px 1fr; gap: 10px; margin: 8px 0; font-size: 13px; }
  .nadro-row .letter { font-weight: 700; color: #60a5fa; text-align: center; background: #1e293b; padding: 4px 8px; border-radius: 4px; }
  .missed { background: rgba(251,191,36,0.08); border-left: 3px solid #fbbf24; padding: 8px 12px; margin: 6px 0; font-size: 12px; color: #fde68a; border-radius: 0 6px 6px 0; }
  .lessons { background: #0f172a; border-radius: 6px; padding: 10px 14px; margin-top: 10px; }
  .lessons ol { margin: 4px 0 4px 20px; padding: 0; }
  .lessons li { margin: 4px 0; font-size: 13px; color: #cbd5e1; }
  .tomorrow { background: rgba(96,165,250,0.1); border-left: 3px solid #60a5fa; padding: 8px 12px; margin-top: 10px; font-size: 13px; color: #bfdbfe; border-radius: 0 6px 6px 0; }
  .error { color: #fca5a5; font-style: italic; }
  .badge { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 11px; font-weight: 600; margin-right: 6px; }
  .badge.win { background: #16a34a; color: #fff; }
  .badge.stop { background: #dc2626; color: #fff; }
  .badge.dead { background: #6b7280; color: #fff; }
  .badge.missed { background: #f59e0b; color: #fff; }
  footer { margin-top: 40px; padding-top: 16px; border-top: 1px solid #334155; color: #64748b; font-size: 12px; text-align: center; }
"""


def _render_instrument(inst: str, r: dict) -> list[str]:
    """Render del card de un instrumento."""
    parts: list[str] = []

    if "error" in r:
        parts.append(
            f'<div class="instrument"><h2>{inst}</h2>'
            f'<p class="error">{_html.escape(r["error"])}</p></div>'
        )
        return parts

    classic = r.get("classic", {})
    narrativa = r.get("narrativa", {})
    aceptacion = r.get("aceptacion", {})
    dva = r.get("dva", {})
    ritmo = r.get("ritmo", {})
    of = r.get("order_flow", {})
    missed = r.get("missed_setups", [])
    lessons = r.get("lessons", [])
    tomorrow = r.get("tomorrow_hint")
    dissonance = r.get("dissonance", {})
    pit = r.get("pit_session") or ("?", "?")

    counts = classic.get("counts", {})
    wins = counts.get("WIN", 0) + counts.get("WIN_MINOR", 0)
    stops = counts.get("STOP_TIGHT", 0) + counts.get("STOP_GENUINE", 0)
    dead = counts.get("DEAD", 0)
    price_change = r.get("price_trend", {}).get("change", 0)

    parts.append(f"""
<div class="instrument">
  <div class="inst-hdr">
    <h2>{inst}</h2>
    <div class="inst-meta">Pit {pit[0]}–{pit[1]} VET · {len(classic.get('hypos', []))} hipos · {len(missed)} missed</div>
  </div>
  <div class="block">
    <strong>Bias</strong>: {_html.escape(classic.get('bias') or '—')} ·
    <strong>Regime</strong>: {_html.escape(classic.get('regime') or '—')} ·
    <strong>Precio open</strong>: {classic.get('price_at_analysis')} ·
    <strong>Close</strong>: {price_change:+.2f} pts
    <br><em>{_html.escape(classic.get('summary') or '')}</em>
  </div>
  <div class="block">
    <span class="badge win">WINS {wins}</span>
    <span class="badge stop">STOPS {stops}</span>
    <span class="badge dead">DEAD {dead}</span>
    <span class="badge missed">MISSED {len(missed)}</span>
  </div>

  <h3>Review N-A-D-R-O</h3>
  <div class="nadro-row"><div class="letter">N</div><div>
    <strong>Bias forward (estructura TPO):</strong> {_html.escape(narrativa.get('forward_bias', 'neutral'))}<br>
    <small>Cumplió pre-open: {_html.escape(narrativa.get('fulfilled_verdict', '-'))}</small>
  </div></div>
  <div class="nadro-row"><div class="letter">A</div><div>{_html.escape(aceptacion.get('summary', '-'))}</div></div>
  <div class="nadro-row"><div class="letter">D</div><div>
    <strong>DVAs multi-TF:</strong> {_html.escape(dva.get('contextual', dva.get('summary', '-')))}<br>
    <small>{dva.get('above_count', 0)} above · {dva.get('inside_count', 0)} inside · {dva.get('below_count', 0)} below</small>
  </div></div>
  <div class="nadro-row"><div class="letter">R</div><div>{_html.escape(ritmo.get('summary', '-'))}</div></div>
  <div class="nadro-row"><div class="letter">O</div><div>{_html.escape(of.get('summary', '-'))}</div></div>
""")

    if dissonance.get("has_dissonance"):
        parts.append(
            f'<div class="block"><strong>Disonancia</strong>: '
            f'{_html.escape(dissonance.get("summary", ""))}</div>'
        )

    if missed:
        parts.append("<h3>Missed Setups</h3>")
        for m in missed[:5]:
            parts.append(
                f'<div class="missed">'
                f'<strong>{_html.escape(m["setup_type"])}</strong> ({m["direction"]}) · '
                f'{_html.escape(m["level_label"])} @ {m["level_price"]} · '
                f'MFE {m["mfe_pts"]} pts / MAE {m["mae_pts"]} pts · '
                f'touch {_fmt_ts(m["touch_time"])}'
                f'</div>'
            )

    if lessons:
        parts.append('<div class="lessons"><h3 style="margin-top:0">Lecciones</h3><ol>')
        for l in lessons:
            parts.append(f"<li>{_html.escape(l)}</li>")
        parts.append("</ol></div>")

    if tomorrow:
        parts.append(
            f'<div class="tomorrow"><strong>Mañana</strong>: '
            f'{_html.escape(tomorrow)}</div>'
        )

    parts.append("</div>")
    return parts


def render_consolidated(reports: dict, aggregate: dict, date_str: str) -> str:
    """HTML completo NADRO Nightly Report consolidado de los 6 instrumentos."""
    parts: list[str] = []
    parts.append(f"""<!doctype html>
<html lang="es"><head><meta charset="utf-8">
<title>NADRO Nightly Report — {date_str}</title>
<style>{_CSS}</style></head><body>
<h1>NADRO Nightly Report — {date_str}</h1>
<div class="subtitle">Review EOD completo bajo metodología N-A-D-R-O · Missed setups · Lecciones · Sugerencias mañana.</div>
<div class="aggregate">
  <div class="stat"><div class="label">Hipos totales</div><div class="value">{aggregate['total_hypos']}</div></div>
  <div class="stat"><div class="label">Wins</div><div class="value win">{aggregate['total_wins']}</div></div>
  <div class="stat"><div class="label">Stop Tight</div><div class="value">{aggregate['total_stop_tight']}</div></div>
  <div class="stat"><div class="label">Missed setups</div><div class="value missed">{aggregate['total_missed']}</div></div>
  <div class="stat"><div class="label">Disonancia (inst.)</div><div class="value">{aggregate['total_dissonance']}</div></div>
</div>
""")

    for inst in ["MGC", "MCL", "MES", "MNQ", "MYM", "M2K"]:
        parts.extend(_render_instrument(inst, reports.get(inst, {})))

    parts.append(f"""
<footer>
  Generado por <code>nadro_nightly_report_all('{date_str}')</code> · Metodología NADRO oficial ·
  Template: <code>Docs/Nadro/nightly_report_template.md</code>
</footer>
</body></html>
""")

    return "".join(parts)
