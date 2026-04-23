"""Generate HTML comparison briefing for blind validation 2026-04-21."""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path


INSTRUMENTS = ["MES", "MNQ", "MYM", "M2K", "MGC", "MCL"]
PHASES = ["context", "pre_open", "eod"]


def _load_all(markups_dir: Path) -> dict:
    all_data = {}
    for inst in INSTRUMENTS:
        path = markups_dir / f"{inst}_2026-04-21.json"
        if not path.exists():
            continue
        data = json.loads(path.read_text(encoding="utf-8"))
        all_data[inst] = data["snapshots"]
    return all_data


def _get_snap(all_data, inst, sid):
    for s in all_data.get(inst, []):
        if s.get("id") == sid:
            return s
    return None


def _morning_snap(all_data, inst):
    for s in all_data.get(inst, []):
        if s.get("id", "").endswith("_0700"):
            return s
    return None


def generate(out_path: Path, markups_dir: Path, date_str: str = "2026-04-21") -> dict:
    all_data = _load_all(markups_dir)

    t1_hits = 0
    total_blind_hipos = 0
    for inst in INSTRUMENTS:
        for phase in PHASES:
            sid = f"{inst}_20260421_{phase}_BLIND"
            s = _get_snap(all_data, inst, sid)
            if not s:
                continue
            for h in s.get("hypos", []):
                total_blind_hipos += 1
                out = h.get("outcome") or {}
                if out.get("setup_reached_t1"):
                    t1_hits += 1

    parts = []
    parts.append("<!doctype html>")
    parts.append('<html lang="es"><head><meta charset="utf-8"/>')
    parts.append(f"<title>Blind Validation {date_str}</title>")
    parts.append("<style>")
    parts.append("body { font-family: -apple-system, Segoe UI, Arial, sans-serif; max-width: 1300px; margin: 24px auto; padding: 0 16px; color: #1f2d3d; background: #fafcff; }")
    parts.append("h1 { color: #0f172a; border-bottom: 3px solid #0ea5e9; padding-bottom: 6px; }")
    parts.append("h2 { color: #0f172a; margin-top: 32px; border-left: 4px solid #0ea5e9; padding-left: 10px; }")
    parts.append("table { width: 100%; border-collapse: collapse; font-size: 13px; margin: 12px 0 24px; }")
    parts.append("th { background: #0ea5e9; color: white; padding: 6px 8px; text-align: left; }")
    parts.append("td { padding: 5px 8px; border-bottom: 1px solid #e2e8f0; vertical-align: top; }")
    parts.append("tr:nth-child(even) { background: #f1f5f9; }")
    parts.append(".pill { display: inline-block; padding: 2px 8px; border-radius: 10px; font-size: 11px; font-weight: 600; }")
    parts.append(".pill-long { background: #dcfce7; color: #14532d; }")
    parts.append(".pill-short { background: #fee2e2; color: #7f1d1d; }")
    parts.append(".pill-neutral { background: #e0e7ff; color: #312e81; }")
    parts.append(".pill-A { background: #fef3c7; color: #78350f; }")
    parts.append(".pill-B { background: #e0e7ff; color: #312e81; }")
    parts.append(".pill-C { background: #e2e8f0; color: #475569; }")
    parts.append(".status-filled { color: #15803d; font-weight: 600; }")
    parts.append(".status-stopped_out { color: #b91c1c; font-weight: 600; }")
    parts.append(".status-not_triggered { color: #64748b; }")
    parts.append(".status-pending { color: #64748b; }")
    parts.append(".status-triggered { color: #854d0e; font-weight: 600; }")
    parts.append(".kbd { font-family: monospace; background: #eef2ff; padding: 1px 4px; border-radius: 3px; font-size: 12px; }")
    parts.append(".section { background: white; padding: 16px; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,0.06); margin-bottom: 20px; }")
    parts.append(".note { background: #fef9c3; border-left: 4px solid #ca8a04; padding: 10px 14px; margin: 16px 0; }")
    parts.append(".small { font-size: 11px; color: #64748b; }")
    parts.append("</style></head><body>")

    parts.append(f"<h1>Blind Validation NADRO — {date_str}</h1>")
    parts.append(f'<p class="small">Generado: {date_str} post-NY close. Dataset: 6 instrumentos x 3 fases (context / pre_open / eod) = 18 snapshots blindos vs morning 07:00 originales.</p>')
    parts.append('<div class="note"><b>Proposito.</b> Reconstruir retrospectivamente que analisis NADRO habria generado el sistema completo (incluyendo TPO por instrumento con pit-session correcto - bug arreglado hoy) en 3 momentos del dia, sin mirar el estado live. Comparar contra los snapshots morning (07:00) originales y los outcomes del walk-forward para medir calidad reconstructiva.</div>')

    # Summary table
    parts.append('<div class="section"><h2>Resumen por instrumento x fase</h2>')
    parts.append("<table><tr><th>Instrumento</th><th>Fase</th><th>Hora</th><th>Precio</th><th>Bias</th><th>#Confl</th><th>#Hipos</th><th>Hipos (direccion &middot; setup &middot; grade &middot; outcome)</th></tr>")

    for inst in INSTRUMENTS:
        for phase in PHASES:
            sid = f"{inst}_20260421_{phase}_BLIND"
            s = _get_snap(all_data, inst, sid)
            if not s:
                continue
            hipos_blocks = []
            for h in s.get("hypos", []):
                out = h.get("outcome") or {}
                dir_ = h.get("direction", "")
                pill_cls = "pill-long" if dir_ == "long" else ("pill-short" if dir_ == "short" else "pill-neutral")
                st = out.get("trade_status", "pending")
                block = (
                    f'<div><span class="pill {pill_cls}">{dir_}</span> '
                    f'<span class="kbd">{h.get("setup_type","")}</span> '
                    f'<span class="pill pill-{h.get("grade","B")}">{h.get("grade","B")}</span> '
                    f'E={h.get("entry")} S={h.get("stop")} risk={h.get("risk_pts")} '
                    f'<span class="status-{st}">{st}</span> '
                    f'mae={out.get("mae_pts")} mfe={out.get("mfe_pts")} t={out.get("targets_hit", [])}</div>'
                )
                hipos_blocks.append(block)
            hipos_txt = "".join(hipos_blocks) or '<span class="small">sin hipos</span>'
            bias = s.get("bias", "")
            bias_pill = "pill-long" if "long" in bias else ("pill-short" if "short" in bias else "pill-neutral")
            ts = s.get("timestamp", "")
            hhmm = ts[-8:-3] if len(ts) >= 8 else ""
            parts.append(
                f"<tr><td><b>{inst}</b></td><td>{phase}</td><td>{hhmm}</td>"
                f"<td>{s.get('price_at_analysis')}</td>"
                f'<td><span class="pill {bias_pill}">{bias}</span></td>'
                f"<td>{len(s.get('confluences', []))}</td>"
                f"<td>{len(s.get('hypos', []))}</td>"
                f"<td>{hipos_txt}</td></tr>"
            )
    parts.append("</table></div>")

    # Comparison vs morning
    parts.append('<div class="section"><h2>Comparacion con hipos de la manana (07:00 originales)</h2>')
    parts.append('<p class="small">Matching por dir + prefijo setup (BPB/RPB/IPB). Se compara morning 07:00 vs blind context (mismo cutoff 07:00 salvo MGC).</p>')
    parts.append("<table><tr><th>Instrumento</th><th>Morning (07:00)</th><th>Blind Context</th><th>Similar?</th></tr>")

    for inst in INSTRUMENTS:
        morning = _morning_snap(all_data, inst)
        ctx = _get_snap(all_data, inst, f"{inst}_20260421_context_BLIND")
        morning_h = morning.get("hypos", []) if morning else []
        ctx_h = ctx.get("hypos", []) if ctx else []

        morning_txt_list = []
        for h in morning_h:
            out = h.get("outcome") or {}
            st = out.get("trade_status", "pending")
            morning_txt_list.append(
                f'<div>{h.get("direction","")} <span class="kbd">{h.get("setup_type","")}</span> '
                f'E={h.get("entry")} <span class="status-{st}">{st}</span></div>'
            )
        morning_txt = "".join(morning_txt_list) or '<span class="small">&mdash;</span>'

        ctx_txt_list = []
        for h in ctx_h:
            out = h.get("outcome") or {}
            st = out.get("trade_status", "pending")
            ctx_txt_list.append(
                f'<div>{h.get("direction","")} <span class="kbd">{h.get("setup_type","")}</span> '
                f'E={h.get("entry")} <span class="status-{st}">{st}</span></div>'
            )
        ctx_txt = "".join(ctx_txt_list) or '<span class="small">&mdash;</span>'

        match = "no"
        for mh in morning_h:
            for bh in ctx_h:
                if mh.get("direction") == bh.get("direction"):
                    m_prefix = (mh.get("setup_type") or "").split("-")[0]
                    b_prefix = (bh.get("setup_type") or "").split("-")[0]
                    if m_prefix == b_prefix:
                        match = "si (dir + setup)"
                        break
            if match != "no":
                break

        parts.append(
            f"<tr><td><b>{inst}</b></td><td>{morning_txt}</td><td>{ctx_txt}</td><td>{match}</td></tr>"
        )
    parts.append("</table></div>")

    # Walk-forward table
    parts.append('<div class="section"><h2>Walk-forward &mdash; 18 blindos</h2>')
    parts.append('<p class="small">Outcomes ejecutados desde cutoff hasta NY close.</p>')
    parts.append("<table><tr><th>Snapshot ID</th><th>Hipo</th><th>Dir</th><th>Setup</th><th>Entry</th><th>Stop</th><th>Status</th><th>MAE</th><th>MFE</th><th>Targets</th><th>T1 setup?</th></tr>")

    for inst in INSTRUMENTS:
        for phase in PHASES:
            sid = f"{inst}_20260421_{phase}_BLIND"
            s = _get_snap(all_data, inst, sid)
            if not s:
                continue
            if not s.get("hypos"):
                parts.append(f"<tr><td><b>{sid}</b></td><td colspan='10' class='small'>sin hipos</td></tr>")
                continue
            for h in s["hypos"]:
                out = h.get("outcome") or {}
                st = out.get("trade_status", "pending")
                t1 = "OK" if out.get("setup_reached_t1") else ""
                parts.append(
                    f"<tr><td><b>{sid}</b></td>"
                    f"<td>{h.get('id','')}</td>"
                    f"<td>{h.get('direction','')}</td>"
                    f'<td class="kbd">{h.get("setup_type","")}</td>'
                    f"<td>{h.get('entry')}</td>"
                    f"<td>{h.get('stop')}</td>"
                    f'<td class="status-{st}">{st}</td>'
                    f"<td>{out.get('mae_pts', '-')}</td>"
                    f"<td>{out.get('mfe_pts', '-')}</td>"
                    f"<td>{out.get('targets_hit', [])}</td>"
                    f"<td>{t1}</td></tr>"
                )
    parts.append("</table></div>")

    # Stats + conclusion
    parts.append('<div class="section"><h2>Estadisticas agregadas</h2>')
    parts.append(f"<ul><li>Total snapshots blindos generados: <b>18/18</b></li>")
    parts.append(f"<li>Total hipos blindos: <b>{total_blind_hipos}</b></li>")
    parts.append(f"<li>Hipos que alcanzaron T1 (setup_reached_t1): <b>{t1_hits}/{total_blind_hipos}</b></li></ul></div>")

    parts.append('<div class="section"><h2>Conclusion</h2>')
    parts.append('<p class="small"><i>(Placeholder para llenar tras revision).</i></p>')
    parts.append("<ul><li></li><li></li><li></li></ul></div>")

    parts.append('<p class="small">Generado por <span class="kbd">blind_snapshot.run_all_blind_snapshots</span> + <span class="kbd">walkforward.close_eod</span> + <span class="kbd">blind_briefing.generate</span>.</p>')
    parts.append("</body></html>")

    html = "\n".join(parts)
    out_path.write_text(html, encoding="utf-8")
    return {
        "path": str(out_path),
        "total_blind_hipos": total_blind_hipos,
        "t1_hits": t1_hits,
    }


if __name__ == "__main__":
    from RelativeMCP_Server.paths import markups_dir
    d = markups_dir()
    out = Path(r"C:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Indicators\RelativeIndicators\Docs\Nadro\briefings\blind_validation_2026-04-21.html")
    out.parent.mkdir(parents=True, exist_ok=True)
    result = generate(out, d)
    print(result)
