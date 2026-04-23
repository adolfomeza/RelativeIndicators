"""NADRO daily briefing HTML generator.

Lee todos los JSON de markups del día, calcula score por hipótesis y genera
un reporte HTML standalone (CSS inline) con:
- Hero: best setup del día
- Top 3 cards
- Panorama macro (confluencias cruzadas)
- Tabla ranqueada completa
- Detalle por instrumento (acordeón)
"""
from __future__ import annotations

import html
import json
from datetime import datetime
from pathlib import Path
from typing import Any

from ..paths import markups_dir, project_root


# ============================================================================
# Scoring
# ============================================================================

GRADE_POINTS = {
    "A+++": 5,
    "A++": 5,
    "A+": 4,
    "A": 3,
    "B+": 3,
    "B": 2,
    "C+": 2,
    "C": 1,
    "": 0,
}


def score_hypo(hypo: dict, snapshot: dict, all_snapshots: list[dict]) -> tuple[int, list[str]]:
    """Calcula el score de una hipótesis. Devuelve (score, reasons[])."""
    reasons: list[str] = []
    score = 0

    # Grade base
    grade = (hypo.get("grade") or "").strip()
    g_pts = GRADE_POINTS.get(grade, 0)
    score += g_pts
    if g_pts > 0:
        reasons.append(f"Grade {grade} (+{g_pts})")

    # RR T1 del primer target
    targets = hypo.get("targets", [])
    if targets:
        rr_t1 = float(targets[0].get("rr", 0) or 0)
        if rr_t1 >= 5:
            score += 3
            reasons.append(f"RR T1 {rr_t1:.1f} (+3)")
        elif rr_t1 >= 3:
            score += 2
            reasons.append(f"RR T1 {rr_t1:.1f} (+2)")
        elif rr_t1 >= 2:
            score += 1
            reasons.append(f"RR T1 {rr_t1:.1f} (+1)")

    # Confluencias del snapshot
    confluences = snapshot.get("confluences", [])
    for c in confluences:
        count = len(c.get("members", []))
        if count >= 5:
            score += 3
            reasons.append(f"Confluencia {count}mb '{c.get('label', '')[:30]}' (+3)")
            break  # solo cuenta la mejor
        elif count >= 3:
            score += 2
            reasons.append(f"Confluencia {count}mb '{c.get('label', '')[:30]}' (+2)")
            break

    # Ley 10 compresión (si la notes/regime lo menciona)
    regime = (snapshot.get("regime") or "").lower()
    notes = (hypo.get("notes") or "").lower()
    summary = (snapshot.get("summary") or "").lower()
    analysis = (snapshot.get("analysis_text") or "").lower()
    if "ley 10" in analysis or "compresion" in analysis or "compressed" in analysis:
        score += 4
        reasons.append("Ley 10 compresión (+4)")

    # Bias alineado con setup direction
    direction = (hypo.get("direction") or "").lower()
    bias = (snapshot.get("bias") or "").lower()
    if direction and bias:
        is_bullish_setup = direction == "long"
        is_bullish_bias = "bull" in bias and "bear" not in bias
        is_bearish_setup = direction == "short"
        is_bearish_bias = "bear" in bias
        if (is_bullish_setup and is_bullish_bias) or (is_bearish_setup and is_bearish_bias):
            score += 1
            reasons.append("Direction alineada con bias (+1)")

    # Dissonance macro/micro
    if "dissonance" in analysis or "disonancia" in analysis:
        score -= 1
        reasons.append("Dissonance macro/micro (-1)")

    # Delta USA fuerte (inferido de summary/analysis_text)
    delta_usa_strong = any(tok in analysis for tok in ["delta_usa", "delta usa", "venta brutal", "comprador fuerte"])
    if delta_usa_strong:
        # Si el número es grande vs el instrumento
        if any(big in analysis for big in ["-2.", "-1.", "+5.", "+2.", "-359", "monstruo"]):
            score += 2
            reasons.append("Delta USA fuerte alineado (+2)")

    # Entry cerca del precio (<1% distancia)
    try:
        price = float(snapshot.get("price_at_analysis", 0))
        entry = float(hypo.get("entry", 0))
        if price > 0:
            dist_pct = abs(entry - price) / price * 100
            if dist_pct < 0.5:
                score += 1
                reasons.append(f"Entry muy cerca ({dist_pct:.2f}%) (+1)")
            elif dist_pct < 1.0:
                score += 0.5
                reasons.append(f"Entry cerca ({dist_pct:.2f}%) (+0.5)")
    except (TypeError, ValueError):
        pass

    # Setup companions: confluencia de N setups en N TFs = NADRO
    companions = hypo.get("setup_companions", []) or []
    if isinstance(companions, list):
        n = len(companions)
        if n == 1:
            score += 3
            reasons.append(f"1 companion (+3)")
        elif n == 2:
            score += 5
            reasons.append(f"2 companions (+5)")
        elif n >= 3:
            score += 7
            reasons.append(f"{n} companions swing (+7)")

    # Swing trade horizon
    horizon = (hypo.get("trading_horizon") or "").lower()
    if horizon == "swing":
        reasons.append("SWING TRADE")

    return round(score, 1), reasons


# ============================================================================
# Data loader
# ============================================================================

def load_snapshots_for_date(date_str: str) -> list[dict]:
    """Carga todos los markups del día dado y devuelve lista plana de
    snapshots (cada uno con su instrument expuesto)."""
    mdir = markups_dir()
    if not mdir.exists():
        return []

    snapshots: list[dict] = []
    for path in sorted(mdir.glob(f"*_{date_str}.json")):
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
            instrument = data.get("instrument", path.stem.rsplit("_", 1)[0])
            for snap in data.get("snapshots", []):
                snap["_instrument"] = instrument
                snap["_file"] = path.name
                snapshots.append(snap)
        except Exception as exc:  # noqa: BLE001
            print(f"Warn: {path.name} skipped: {exc}")
    return snapshots


def build_rank(snapshots: list[dict]) -> list[dict]:
    """Para cada hypo en cada snapshot, calcula score y devuelve lista
    ordenada por score desc."""
    rank: list[dict] = []
    for snap in snapshots:
        for hypo in snap.get("hypos", []):
            score, reasons = score_hypo(hypo, snap, snapshots)
            rank.append({
                "instrument": snap.get("_instrument"),
                "snapshot_id": snap.get("id"),
                "timestamp": snap.get("timestamp"),
                "snap": snap,
                "hypo": hypo,
                "score": score,
                "reasons": reasons,
            })
    rank.sort(key=lambda r: r["score"], reverse=True)
    for i, r in enumerate(rank):
        r["rank"] = i + 1
    return rank


def panorama(snapshots: list[dict]) -> dict:
    """Resumen macro: cuántos bullish/bearish/neutral, tema del día."""
    biases = {"bullish": 0, "bearish": 0, "neutral": 0, "mixed": 0}
    instruments: list[str] = []
    for s in snapshots:
        b = (s.get("bias") or "").lower()
        instruments.append(s.get("_instrument", "?"))
        if "bull" in b and "bear" not in b:
            biases["bullish"] += 1
        elif "bear" in b and "bull" not in b:
            biases["bearish"] += 1
        elif "neutral" in b:
            biases["neutral"] += 1
        else:
            biases["mixed"] += 1

    total = sum(biases.values())
    dominant = max(biases, key=biases.get) if total else "mixed"
    return {
        "biases": biases,
        "total": total,
        "dominant": dominant,
        "instruments": instruments,
    }


# ============================================================================
# HTML rendering
# ============================================================================

def _esc(s: Any) -> str:
    return html.escape(str(s or ""))


def _grade_color(grade: str) -> str:
    g = (grade or "").upper()
    if "A+++" in g or "A++" in g:
        return "#ef4444"
    if "A+" in g:
        return "#f97316"
    if g.startswith("A"):
        return "#eab308"
    if "B" in g:
        return "#84cc16"
    return "#64748b"


def _direction_symbol(direction: str) -> str:
    return "↓" if direction == "short" else "↑"


def _direction_color(direction: str) -> str:
    return "#ef4444" if direction == "short" else "#22c55e"


def render_hero(top: dict) -> str:
    h = top["hypo"]
    s = top["snap"]
    grade = h.get("grade", "")
    direction = h.get("direction", "")
    targets = h.get("targets", [])
    outcome = h.get("outcome", {}) or {}
    trade_status = outcome.get("trade_status") or outcome.get("status") or "pending"
    outcome_badge = _status_badge(trade_status) if trade_status != "pending" else ""
    mfe = outcome.get("mfe_pts")
    mfe_txt = f" · MFE {mfe:.1f}pts" if mfe else ""

    targets_html = ""
    for i, t in enumerate(targets):
        targets_html += f"""
        <div class="target">
            <span class="target-label">t{i+1}</span>
            <span class="target-price">{_esc(t.get('price'))}</span>
            <span class="target-rr">RR {_esc(t.get('rr'))}</span>
            <span class="target-name">{_esc(t.get('label', ''))}</span>
        </div>"""

    return f"""
    <div class="hero">
        <div class="hero-badge">🏆 BEST SETUP OF THE DAY{mfe_txt}</div>
        <div class="hero-title">
            <span class="instr">{_esc(top['instrument'])}</span>
            <span class="direction" style="color:{_direction_color(direction)}">{_direction_symbol(direction)} {_esc(direction).upper()}</span>
            <span class="setup-type">{_esc(h.get('setup_type'))}</span>
            <span class="grade" style="background:{_grade_color(grade)}">{_esc(grade)}</span>
            <span class="score">Score {top['score']}</span>
            {outcome_badge}
        </div>
        <div class="hero-body">
            <div class="eslabon">
                <div class="eslabon-label">ENTRY</div>
                <div class="eslabon-value">{_esc(h.get('entry'))}</div>
            </div>
            <div class="eslabon">
                <div class="eslabon-label">STOP</div>
                <div class="eslabon-value stop">{_esc(h.get('stop'))}</div>
            </div>
            <div class="eslabon">
                <div class="eslabon-label">RISK</div>
                <div class="eslabon-value">{_esc(round(abs(h.get('entry', 0) - h.get('stop', 0)), 2))} pts</div>
            </div>
        </div>
        <div class="hero-targets">{targets_html}</div>
        <div class="hero-rationale">
            <strong>Rationale:</strong> {" · ".join(_esc(r) for r in top['reasons'])}
        </div>
        <div class="hero-notes">💬 {_esc(h.get('notes', ''))}</div>
    </div>"""


def render_card(item: dict) -> str:
    h = item["hypo"]
    grade = h.get("grade", "")
    direction = h.get("direction", "")
    targets = h.get("targets", [])
    rr_t1 = targets[0].get("rr", 0) if targets else 0

    return f"""
    <div class="card">
        <div class="card-head">
            <span class="rank">#{item['rank']}</span>
            <span class="instr">{_esc(item['instrument'])}</span>
            <span class="grade" style="background:{_grade_color(grade)}">{_esc(grade)}</span>
        </div>
        <div class="card-direction" style="color:{_direction_color(direction)}">
            {_direction_symbol(direction)} {_esc(direction).upper()} {_esc(h.get('setup_type'))}
        </div>
        <div class="card-numbers">
            <div><small>E</small> {_esc(h.get('entry'))}</div>
            <div><small>S</small> {_esc(h.get('stop'))}</div>
            <div><small>T1</small> RR {_esc(rr_t1)}</div>
        </div>
        <div class="card-score">Score <strong>{item['score']}</strong></div>
    </div>"""


def _status_badge(status: str) -> str:
    colors = {
        "filled": ("#22c55e", "white", "FILLED"),
        "stopped_out": ("#ef4444", "white", "STOPPED"),
        "triggered": ("#06b6d4", "white", "TRIGGERED"),
        "not_triggered": ("#64748b", "white", "NOT-TRIG"),
        "pending": ("#eab308", "#1e293b", "PENDING"),
    }
    bg, fg, txt = colors.get(status, ("#334155", "white", status.upper()))
    return f"<span style='background:{bg};color:{fg};padding:2px 6px;border-radius:3px;font-size:11px;font-weight:bold;'>{txt}</span>"


def _target_flags(outcome: dict) -> str:
    """Renderiza T1/T2/T3 reached flags."""
    parts = []
    for i, key in enumerate(["setup_reached_t1", "setup_reached_t2", "setup_reached_t3"], 1):
        reached = outcome.get(key, False)
        if reached:
            parts.append(f"<span style='color:#22c55e;font-weight:bold'>t{i}✓</span>")
        else:
            parts.append(f"<span style='color:#475569'>t{i}</span>")
    return " ".join(parts)


def render_rank_table(rank: list[dict]) -> str:
    rows = ""
    for item in rank:
        h = item["hypo"]
        s = item["snap"]
        grade = h.get("grade", "")
        direction = h.get("direction", "")
        targets = h.get("targets", [])
        rr_t1 = targets[0].get("rr", 0) if targets else 0
        dist = ""
        try:
            dist = f"{h.get('entry', 0) - s.get('price_at_analysis', 0):+.2f}"
        except Exception:  # noqa: BLE001
            dist = "—"
        companions = h.get("setup_companions", []) or []
        horizon = (h.get("trading_horizon") or "").lower()
        setup_display = _esc(h.get('setup_type'))
        if companions and isinstance(companions, list):
            setup_display += " <span style='color:#fbbf24'>/ " + " / ".join(_esc(c) for c in companions) + "</span>"
        if horizon == "swing":
            setup_display += " <span style='background:#8b5cf6;padding:1px 6px;border-radius:3px;font-size:10px;'>SWING</span>"

        outcome = h.get("outcome", {}) or {}
        trade_status = outcome.get("trade_status") or outcome.get("status") or "pending"
        status_html = _status_badge(trade_status)
        targets_html = _target_flags(outcome)
        mae = outcome.get("mae_pts")
        mfe = outcome.get("mfe_pts")
        mae_str = f"{mae:.1f}" if mae is not None else "—"
        mfe_str = f"{mfe:.1f}" if mfe is not None else "—"

        # Resaltar "stop tight" (stopped_out pero setup_reached_t1)
        stop_tight_badge = ""
        if trade_status == "stopped_out" and outcome.get("setup_reached_t1"):
            stop_tight_badge = " <span style='background:#f97316;color:white;padding:1px 5px;border-radius:3px;font-size:10px;'>STOP TIGHT</span>"

        rows += f"""
        <tr>
            <td class="num">{item['rank']}</td>
            <td><strong>{_esc(item['instrument'])}</strong></td>
            <td style="color:{_direction_color(direction)}">{_direction_symbol(direction)} {_esc(direction)}</td>
            <td>{setup_display}</td>
            <td><span class="grade" style="background:{_grade_color(grade)}">{_esc(grade)}</span></td>
            <td class="num">{_esc(h.get('entry'))}</td>
            <td class="num">{dist}</td>
            <td class="num">{_esc(h.get('stop'))}</td>
            <td class="num">RR {_esc(rr_t1)}</td>
            <td class="num score-cell">{item['score']}</td>
            <td>{status_html}{stop_tight_badge}</td>
            <td>{targets_html}</td>
            <td class="num" style="color:#ef4444">{mae_str}</td>
            <td class="num" style="color:#22c55e">{mfe_str}</td>
        </tr>"""

    return f"""
    <table class="rank-table">
        <thead>
            <tr>
                <th>#</th>
                <th>Instr</th>
                <th>Dir</th>
                <th>Setup</th>
                <th>Grade</th>
                <th>Entry</th>
                <th>Dist</th>
                <th>Stop</th>
                <th>RR T1</th>
                <th>Score</th>
                <th>Status</th>
                <th>Reached</th>
                <th>MAE</th>
                <th>MFE</th>
            </tr>
        </thead>
        <tbody>{rows}</tbody>
    </table>"""


def render_panorama(p: dict, rank: list[dict]) -> str:
    b = p["biases"]
    # Scorecard de outcomes
    counts = {"filled": 0, "stopped_out": 0, "triggered": 0, "not_triggered": 0, "pending": 0, "stop_tight": 0}
    for r in rank:
        o = r["hypo"].get("outcome", {}) or {}
        st = o.get("trade_status") or o.get("status") or "pending"
        counts[st] = counts.get(st, 0) + 1
        if st == "stopped_out" and o.get("setup_reached_t1"):
            counts["stop_tight"] += 1
    total = sum(counts.get(k, 0) for k in ["filled", "stopped_out", "triggered", "not_triggered"])

    return f"""
    <div class="panorama">
        <h3>📊 Panorama macro</h3>
        <div class="panorama-stats">
            <div class="stat"><span class="bull">↑ {b['bullish']}</span> bullish</div>
            <div class="stat"><span class="bear">↓ {b['bearish']}</span> bearish</div>
            <div class="stat"><span class="neut">= {b['neutral']}</span> neutral</div>
            <div class="stat"><span class="mix">~ {b['mixed']}</span> mixed</div>
        </div>
        <div class="panorama-inst">Instrumentos: {', '.join(_esc(x) for x in p['instruments'])} · Sesgo dominante: <strong>{_esc(p['dominant'])}</strong></div>
        <h3 style="margin-top:16px">🎯 Scorecard walk-forward ({total} hipos procesadas)</h3>
        <div class="panorama-stats">
            <div class="stat"><span style="color:#22c55e;font-weight:bold;font-size:18px">✓ {counts['filled']}</span> filled</div>
            <div class="stat"><span style="color:#ef4444;font-weight:bold;font-size:18px">✗ {counts['stopped_out']}</span> stopped</div>
            <div class="stat"><span style="color:#06b6d4;font-weight:bold;font-size:18px">~ {counts['triggered']}</span> open</div>
            <div class="stat"><span style="color:#64748b;font-weight:bold;font-size:18px">— {counts['not_triggered']}</span> not-trig</div>
            <div class="stat"><span style="color:#f97316;font-weight:bold;font-size:18px">⚠ {counts['stop_tight']}</span> stop tight</div>
        </div>
        <div style="color:#f97316;margin-top:8px;font-size:13px">
            <strong>"Stop tight"</strong> = trade stopped_out pero el setup sí alcanzó T1 post-stop → sugiere stops muy ajustados vs rango del día.
        </div>
    </div>"""


def render_snapshot_detail(snap: dict) -> str:
    instrument = snap.get("_instrument", "?")
    analysis = _esc(snap.get("analysis_text", "")).replace("\n", "<br>")
    confluences_html = ""
    for c in snap.get("confluences", []):
        members = ", ".join(_esc(m) for m in c.get("members", []))
        confluences_html += f"""
        <div class="confl">
            <span class="confl-grade" style="background:{_grade_color(c.get('grade', ''))}">{_esc(c.get('grade'))}</span>
            <strong>{_esc(c.get('label'))}</strong>
            <span class="confl-range">{_esc(c.get('price_min'))} - {_esc(c.get('price_max'))}</span>
            <div class="confl-members">{members}</div>
        </div>"""

    hypos_html = ""
    for h in snap.get("hypos", []):
        targets_str = " · ".join(f"t{i+1} {_esc(t.get('price'))} (RR {_esc(t.get('rr'))})"
                                   for i, t in enumerate(h.get("targets", [])))
        direction = h.get("direction", "")
        hypos_html += f"""
        <div class="hypo-detail">
            <div class="hypo-head">
                <strong>{_esc(h.get('id'))}</strong>
                <span style="color:{_direction_color(direction)}">{_direction_symbol(direction)} {_esc(direction)}</span>
                <span>{_esc(h.get('setup_type'))}</span>
                <span class="grade" style="background:{_grade_color(h.get('grade', ''))}">{_esc(h.get('grade'))}</span>
            </div>
            <div>E {_esc(h.get('entry'))} · S {_esc(h.get('stop'))} · {targets_str}</div>
            <div class="hypo-notes">{_esc(h.get('notes', ''))}</div>
        </div>"""

    return f"""
    <details class="snap-detail">
        <summary><strong>{_esc(instrument)}</strong> — {_esc(snap.get('id'))} · {_esc(snap.get('timestamp'))} · precio {_esc(snap.get('price_at_analysis'))} · <em>{_esc(snap.get('regime'))}</em></summary>
        <div class="snap-body">
            <div class="snap-summary">{_esc(snap.get('summary'))}</div>
            <div class="snap-hypos">{hypos_html}</div>
            <div class="snap-conflu">{confluences_html}</div>
            <div class="snap-analysis"><pre>{analysis}</pre></div>
        </div>
    </details>"""


CSS = """
* { box-sizing: border-box; }
body {
    margin: 0; padding: 24px;
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
    background: #0f172a; color: #e2e8f0;
    line-height: 1.5;
}
h1, h2, h3 { margin: 0 0 12px; }
h1 { font-size: 28px; }
h2 { font-size: 20px; color: #94a3b8; margin-top: 32px; }
h3 { font-size: 16px; color: #cbd5e1; }
header { border-bottom: 1px solid #334155; padding-bottom: 16px; margin-bottom: 24px; }
header .date { color: #94a3b8; }

/* Hero */
.hero {
    background: linear-gradient(135deg, #1e293b, #0f172a);
    border: 2px solid #fbbf24; border-radius: 16px;
    padding: 24px; margin-bottom: 32px;
    box-shadow: 0 0 40px rgba(251, 191, 36, 0.2);
}
.hero-badge { color: #fbbf24; font-weight: bold; font-size: 14px; letter-spacing: 2px; }
.hero-title { display: flex; align-items: center; gap: 16px; margin: 12px 0; flex-wrap: wrap; }
.hero-title .instr { font-size: 32px; font-weight: bold; }
.hero-title .direction { font-size: 20px; font-weight: bold; }
.hero-title .setup-type { font-size: 18px; color: #94a3b8; }
.hero-title .grade { padding: 4px 12px; border-radius: 6px; font-weight: bold; color: white; font-size: 16px; }
.hero-title .score { margin-left: auto; font-size: 24px; color: #fbbf24; font-weight: bold; }
.hero-body { display: flex; gap: 24px; margin-top: 16px; }
.eslabon { flex: 1; background: #0f172a; padding: 12px; border-radius: 8px; border: 1px solid #334155; }
.eslabon-label { font-size: 11px; color: #94a3b8; letter-spacing: 1px; }
.eslabon-value { font-size: 22px; font-weight: bold; margin-top: 4px; }
.eslabon-value.stop { color: #ef4444; }
.hero-targets { margin-top: 16px; display: flex; gap: 12px; flex-wrap: wrap; }
.target { background: #0f172a; padding: 10px 14px; border-radius: 8px; border: 1px solid #334155; display: flex; gap: 8px; align-items: baseline; }
.target-label { color: #60a5fa; font-weight: bold; }
.target-price { font-size: 18px; font-weight: bold; color: #22d3ee; }
.target-rr { color: #22c55e; font-weight: bold; }
.target-name { color: #94a3b8; font-size: 13px; }
.hero-rationale { margin-top: 16px; color: #cbd5e1; font-size: 14px; }
.hero-notes { margin-top: 8px; font-style: italic; color: #94a3b8; }

/* Cards top */
.cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 16px; margin: 24px 0; }
.card {
    background: #1e293b; border: 1px solid #334155; border-radius: 12px; padding: 16px;
}
.card-head { display: flex; align-items: center; gap: 12px; }
.card-head .rank { background: #475569; color: white; padding: 2px 8px; border-radius: 4px; font-size: 12px; }
.card-head .instr { font-size: 20px; font-weight: bold; }
.card-head .grade { margin-left: auto; padding: 2px 8px; border-radius: 4px; color: white; font-weight: bold; }
.card-direction { font-size: 16px; font-weight: bold; margin: 8px 0; }
.card-numbers { display: grid; grid-template-columns: repeat(3, 1fr); gap: 8px; font-size: 13px; }
.card-numbers small { color: #94a3b8; margin-right: 4px; }
.card-score { margin-top: 8px; color: #fbbf24; text-align: right; }
.card-score strong { font-size: 20px; }

/* Panorama */
.panorama { background: #1e293b; padding: 16px; border-radius: 12px; margin: 24px 0; border: 1px solid #334155; }
.panorama-stats { display: flex; gap: 24px; margin: 12px 0; }
.stat { font-size: 14px; }
.stat .bull { color: #22c55e; font-weight: bold; font-size: 18px; }
.stat .bear { color: #ef4444; font-weight: bold; font-size: 18px; }
.stat .neut { color: #94a3b8; font-weight: bold; font-size: 18px; }
.stat .mix { color: #fbbf24; font-weight: bold; font-size: 18px; }
.panorama-inst { color: #94a3b8; font-size: 13px; }
.panorama-dominant { margin-top: 8px; color: #cbd5e1; }

/* Rank table */
.rank-table { width: 100%; border-collapse: collapse; margin: 16px 0; background: #1e293b; border-radius: 8px; overflow: hidden; }
.rank-table th, .rank-table td { padding: 10px 12px; text-align: left; border-bottom: 1px solid #334155; }
.rank-table thead th { background: #0f172a; color: #94a3b8; font-size: 12px; letter-spacing: 1px; text-transform: uppercase; }
.rank-table tbody tr:hover { background: #334155; }
.rank-table .num { text-align: right; font-family: 'SF Mono', Monaco, monospace; }
.rank-table .grade { padding: 2px 8px; border-radius: 4px; color: white; font-size: 12px; }
.rank-table .score-cell { color: #fbbf24; font-weight: bold; font-size: 16px; }

/* Snapshot details */
.snap-detail { background: #1e293b; border: 1px solid #334155; border-radius: 8px; padding: 12px; margin-bottom: 8px; }
.snap-detail summary { cursor: pointer; padding: 4px; }
.snap-detail[open] summary { border-bottom: 1px solid #334155; padding-bottom: 12px; margin-bottom: 12px; }
.snap-summary { color: #cbd5e1; padding: 8px 0; font-size: 14px; }
.snap-hypos { display: flex; flex-direction: column; gap: 8px; margin: 12px 0; }
.hypo-detail { background: #0f172a; padding: 10px; border-radius: 6px; border: 1px solid #334155; }
.hypo-head { display: flex; gap: 8px; align-items: center; margin-bottom: 4px; }
.hypo-head .grade { padding: 2px 6px; border-radius: 3px; color: white; font-size: 11px; }
.hypo-notes { color: #94a3b8; font-size: 12px; margin-top: 4px; font-style: italic; }
.snap-conflu { margin-top: 8px; }
.confl { background: #0f172a; padding: 8px 12px; border-radius: 6px; margin: 4px 0; display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
.confl-grade { padding: 2px 6px; border-radius: 3px; color: white; font-size: 11px; }
.confl-range { color: #60a5fa; font-family: monospace; }
.confl-members { color: #94a3b8; font-size: 12px; width: 100%; margin-top: 4px; }
.snap-analysis { margin-top: 12px; }
.snap-analysis pre { background: #0f172a; padding: 12px; border-radius: 6px; font-size: 12px; color: #cbd5e1; overflow-x: auto; border: 1px solid #334155; }

footer { margin-top: 48px; padding-top: 16px; border-top: 1px solid #334155; color: #64748b; font-size: 12px; text-align: center; }
"""


def render_html(date_str: str) -> str:
    snapshots = load_snapshots_for_date(date_str)
    if not snapshots:
        return f"<html><body><h1>No hay snapshots para {date_str}</h1></body></html>"

    rank = build_rank(snapshots)
    pano = panorama(snapshots)

    hero_html = render_hero(rank[0]) if rank else ""
    cards_html = "".join(render_card(r) for r in rank[1:4])  # top 2, 3, 4
    table_html = render_rank_table(rank)
    panorama_html = render_panorama(pano, rank)
    details_html = "".join(render_snapshot_detail(s) for s in snapshots)

    return f"""<!DOCTYPE html>
<html lang="es">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>NADRO Daily Briefing · {date_str}</title>
<style>{CSS}</style>
</head>
<body>
<header>
    <h1>🎯 NADRO Daily Briefing</h1>
    <div class="date">{date_str} · Pre-market {_esc(snapshots[0].get('timestamp', '').split('T')[-1][:5] if snapshots else '')} VET · {len(snapshots)} instrumentos · {len(rank)} hipótesis ranqueadas</div>
</header>

{hero_html}

<h2>Top setups (ranks #2-#4)</h2>
<div class="cards">{cards_html}</div>

{panorama_html}

<h2>📋 Tabla completa ranqueada</h2>
{table_html}

<h2>📈 Detalle por instrumento</h2>
{details_html}

<footer>
    NADRO Daily Briefing · Generado {datetime.now().strftime('%Y-%m-%d %H:%M:%S')} · RelativeIndicators Suite
</footer>
</body>
</html>"""


def generate(date_str: str | None = None) -> dict:
    """Genera el HTML del briefing del día. Devuelve path + stats."""
    if not date_str:
        date_str = datetime.now().strftime("%Y-%m-%d")

    snapshots = load_snapshots_for_date(date_str)
    rank = build_rank(snapshots)

    reports_dir = project_root() / "RelativeMCP_Server" / "reports"
    reports_dir.mkdir(parents=True, exist_ok=True)
    out_path = reports_dir / f"briefing_{date_str}.html"

    html_content = render_html(date_str)
    out_path.write_text(html_content, encoding="utf-8")

    return {
        "path": str(out_path),
        "date": date_str,
        "total_instruments": len(snapshots),
        "total_hypos": len(rank),
        "top_setup": {
            "instrument": rank[0]["instrument"],
            "grade": rank[0]["hypo"].get("grade"),
            "score": rank[0]["score"],
        } if rank else None,
    }
