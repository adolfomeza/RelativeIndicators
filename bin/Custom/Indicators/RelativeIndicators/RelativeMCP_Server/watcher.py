"""Watcher daemon — procesa snapshot requests del botón Pit-Open / Ad-Hoc / EOD.

Se ejecuta en loop, polling `Docs/Nadro/snapshot_requests/*.json` cada N segundos.
Cuando aparece un archivo nuevo:
1. Mueve a `processing/` para evitar doble procesamiento
2. Lee el request (instrument, timestamp_bar, price_at_click, trigger_type)
3. Llama `nadro_snapshot_replay` con as_of = timestamp_bar
4. Construye confluences + levels mecánicamente desde el snapshot
5. Enumera y rankea top 3 hipos automáticamente
6. Persiste el markup vía `save_snapshot`
7. Mueve a `processed/` (success) o `failed/` (error)

Uso:
    python -m RelativeMCP_Server.watcher
    # o con interval custom:
    python -m RelativeMCP_Server.watcher --interval 5

Para detener: Ctrl+C.
"""
from __future__ import annotations

import argparse
import json
import shutil
import sys
import time
import traceback
from datetime import datetime
from pathlib import Path

from .paths import project_root
from .tools.markup import save_snapshot


def nadro_dir():
    return project_root() / "Docs" / "Nadro"
from .tools.replay import nadro_snapshot_replay
from .tools.simulator import enumerate_hipos


def _log(msg: str) -> None:
    print(f"[{datetime.now().strftime('%H:%M:%S')}] {msg}", flush=True)


def _kind_from_trigger(trigger_type: str) -> str:
    """Map trigger_type del request a kind del replay."""
    return {
        "pit_open": "pit_open",
        "ad_hoc": "pre_pit",     # ad-hoc = analizar como pre-pit del momento
        "eod": "eod",
    }.get(trigger_type, "pre_pit")


def compute_daily_atr(instrument: str, lookback_days: int = 14) -> float | None:
    """ATR(N) de bars diarios. Mide la volatilidad típica del instrumento.

    Fórmula clásica:
        TR_i = max(high_i - low_i, |high_i - close_{i-1}|, |low_i - close_{i-1}|)
        ATR  = SMA de los últimos N TR

    Returns: ATR en puntos del instrumento, o None si no hay data suficiente.
    """
    from .tools import observer
    try:
        # Pedir bars diarios con margen (25 días para tener al menos 14 cerrados)
        data = observer.get_bars(instrument=instrument, tf="1d", n=lookback_days + 10)
        bars = data.get("bars", [])
        if len(bars) < 2:
            return None
        # Excluir el día actual (que está in-progress)
        # NT devuelve hasta el bar actual; usar todos menos el último
        bars = bars[:-1] if len(bars) > lookback_days else bars
        if len(bars) < 2:
            return None
        bars = bars[-lookback_days:]  # últimos N

        trs = []
        for i in range(1, len(bars)):
            h, l = bars[i]["h"], bars[i]["l"]
            prev_c = bars[i - 1]["c"]
            tr = max(h - l, abs(h - prev_c), abs(l - prev_c))
            trs.append(tr)
        if not trs:
            return None
        return sum(trs) / len(trs)
    except Exception:
        return None


def build_levels(snapshot: dict) -> list[dict]:
    """Niveles ESTATICOS operables del snapshot.

    EXCLUYE deliberadamente los DVA (Daily/Weekly/Monthly/Quarterly/Annual)
    porque son DINAMICOS — el valor en el snapshot no sera el mismo cuando
    el trade se gatille horas despues. El usuario tiene los indicadores DVA
    en el chart que se actualizan en tiempo real.

    Solo niveles ESTATICOS (no cambian post-snapshot):
      - pVAH/pVAL/pPOC (cerradas o active del dia previo)
      - CVA boundaries (cerradas y active)
      - Secondary lines (bordes rotos)
    """
    levels = []
    cvas = snapshot.get("cvas", {})

    # pVAs (estaticas)
    for pva in cvas.get("pvas", []):
        date = pva.get("start_date", "?")[5:]  # MM-DD
        tag = "act" if pva.get("status") == "active" else ""
        levels.append({"label": f"pVAH{tag}", "price": pva["vah"]})
        levels.append({"label": f"pVAL{tag}", "price": pva["val"]})

    # CVAs (estaticas)
    for cva in cvas.get("cvas", []):
        tag = "act" if cva.get("status") == "active" else ""
        levels.append({"label": f"CVAH-{cva['start_date'][5:]}-{cva['end_date'][5:]}{tag}", "price": cva["vah"]})
        levels.append({"label": f"CVAL-{cva['start_date'][5:]}-{cva['end_date'][5:]}{tag}", "price": cva["val"]})

    # Secondary lines (estaticas)
    for sec in cvas.get("secondary_lines", []):
        levels.append({"label": "sec", "price": sec["price"]})

    # NOTA: DVAs intencionalmente NO incluidos. Ver docstring.
    return levels


def build_confluences(levels: list[dict], proximity_pts: float = 15.0, min_count: int = 2) -> list[dict]:
    """Detecta clusters de niveles cercanos (mechanical confluences).

    Para NQ: 15pts es un cluster típico.
    Grade automático: 3+ niveles = A+, 2 niveles = A.
    """
    if not levels:
        return []
    sorted_lvls = sorted(levels, key=lambda x: x["price"])
    confluences = []
    used = set()

    for i, lvl in enumerate(sorted_lvls):
        if i in used:
            continue
        cluster = [lvl]
        cluster_idx = [i]
        for j in range(i + 1, len(sorted_lvls)):
            if j in used:
                continue
            if sorted_lvls[j]["price"] - lvl["price"] <= proximity_pts:
                cluster.append(sorted_lvls[j])
                cluster_idx.append(j)
            else:
                break
        if len(cluster) >= min_count:
            for idx in cluster_idx:
                used.add(idx)
            prices = [c["price"] for c in cluster]
            grade = "A+" if len(cluster) >= 3 else "A"
            members = [f"{c['label']} {c['price']:.1f}" for c in cluster]
            label = "+".join(c["label"] for c in cluster[:3])
            confluences.append({
                "label": label,
                "price_min": min(prices),
                "price_max": max(prices),
                "grade": grade,
                "members": members,
            })

    return confluences


def rank_hipos(hipos: list[dict], snapshot: dict, confluences: list[dict] | None = None, max_n: int = 3) -> list[dict]:
    """Rankea hipos y devuelve top N.

    Criterios:
    - **Pertenecer a un cluster A+ es boost MAYOR** (es lo que NADRO premia)
    - Distancia razonable al precio (penaliza solo si está MUY lejos > 300pts)
    - Tipo de nivel (CVAH/CVAL/pVAH/pVAL > DVA)
    - Alineación con tendencia (boost long si bullish, short si bearish)
    """
    spot = snapshot.get("spot", {}).get("close", 0)
    confluences = confluences or []

    # Bias estructural por breakouts
    cvas = snapshot.get("cvas", {})
    breaks_up = sum(1 for b in cvas.get("pvas", []) + cvas.get("cvas", [])
                    if b.get("closed_reason", "").startswith("breakout_up"))
    breaks_down = sum(1 for b in cvas.get("pvas", []) + cvas.get("cvas", [])
                      if b.get("closed_reason", "").startswith("breakout_down"))
    bullish = breaks_up >= breaks_down + 2
    bearish = breaks_down >= breaks_up + 2

    # Pre-mapear: para cada nivel (precio), ¿está en algún cluster? ¿qué grade?
    def cluster_grade_for(price: float) -> str | None:
        for c in confluences:
            if c["price_min"] - 1 <= price <= c["price_max"] + 1:
                return c["grade"]
        return None

    def score(h: dict) -> float:
        s = 0.0
        dist = h.get("distance_to_level", 999)

        # CLUSTER MEMBERSHIP: el verdadero edge NADRO
        cluster_g = cluster_grade_for(h["level_price"])
        if cluster_g == "A+":
            s += 100  # cluster A+ = top priority
        elif cluster_g == "A":
            s += 50

        # Penaliza solo si está MUY lejos (>300pts en NQ)
        if dist > 300:
            s -= (dist - 300) * 0.3

        # Tipo de nivel
        lvl_type = h.get("level_type", "")
        s += {"CVAH": 30, "CVAL": 30, "pVAH": 20, "pVAL": 20,
              "secondary": 15, "DVAH": 10, "DVAL": 10}.get(lvl_type, 0)

        # Alineación con tendencia
        if bullish and h["direction"] == "long":
            s += 20
        elif bearish and h["direction"] == "short":
            s += 20
        elif bullish and h["direction"] == "short":
            s -= 50  # penalizar fade vs imbalance
        elif bearish and h["direction"] == "long":
            s -= 50

        # BPB > IPB
        if h.get("trigger_condition") == "BPB":
            s += 5

        return s

    ranked = sorted(hipos, key=score, reverse=True)
    return ranked[:max_n]


def hipo_to_markup(h: dict, idx: int, spot: float) -> dict:
    """Convierte un hypo enumerado al formato del markup, calculando entry/stop/targets."""
    level = h["level_price"]
    direction = h["direction"]

    # Entry near level (BPB: at level on retest; IPB: at level on touch)
    entry = level

    # Stop offset 30pts (será reemplazado por HA pivot dinámico al disparar trade real)
    stop_offset = 30
    if direction == "long":
        stop = level - stop_offset
        t1 = level + 50
        t2 = level + 100
    else:
        stop = level + stop_offset
        t1 = level - 50
        t2 = level - 100

    grade = "A" if h["level_type"] in ("CVAH", "CVAL", "pVAH", "pVAL") else "B"

    return {
        "id": f"h{idx}",
        "direction": direction,
        "setup_type": f"{h['level_label']} {h['setup_type']}",
        "entry": entry,
        "stop": stop,
        "grade": grade,
        "notes": f"auto-watcher | dist {h.get('distance_to_level', 0):.0f}pts del spot",
        "targets": [
            {"label": "T1", "price": t1},
            {"label": "T2", "price": t2},
        ],
    }


def process_request(req_path: Path) -> dict:
    """Procesa un archivo de request. Returns dict con result info."""
    try:
        with open(req_path, "r", encoding="utf-8") as f:
            req = json.load(f)
    except Exception as e:
        return {"ok": False, "error": f"no se pudo leer JSON: {e}"}

    instrument = req.get("instrument_full") or req.get("instrument")
    timestamp = req.get("timestamp_bar") or req.get("timestamp_real")
    price_click = req.get("price_at_click")
    trigger_type = req.get("trigger_type", "ad_hoc")

    if not instrument or not timestamp:
        return {"ok": False, "error": "request sin instrument o timestamp"}

    kind = _kind_from_trigger(trigger_type)

    # Generar replay snapshot
    snap = nadro_snapshot_replay(instrument=instrument, as_of=timestamp, kind=kind)
    if "error" in snap and snap.get("spot", {}).get("close") is None:
        return {"ok": False, "error": f"replay error: {snap.get('error', '?')}"}

    spot = snap.get("spot", {}).get("close") or price_click
    if spot is None:
        return {"ok": False, "error": "sin spot"}

    # Construir levels mecánicamente.
    all_levels = build_levels(snap)

    # Filtro ESTADISTICO basado en ATR(14) de bars diarios.
    # Mejor que 500pts hardcoded porque escala con volatilidad y por instrumento.
    # Default: 2x ATR (cubre ~95% de los movimientos típicos diarios).
    atr = compute_daily_atr(instrument, lookback_days=14)
    if atr is None or atr <= 0:
        proximity = 500.0  # fallback si no hay ATR
    else:
        # 2x ATR como ventana operativa, con floor 50pts y cap 2000pts
        proximity = max(50.0, min(2000.0, atr * 2.0))

    # Filtro ESTADISTICO puro: solo niveles dentro de ±2*ATR del spot.
    # Sin excepciones — si ATR(14) dice que el precio típicamente NO llega al
    # nivel intra-día, no tiene sentido renderizarlo. Mejor chart limpio.
    levels = [l for l in all_levels if abs(l["price"] - spot) <= proximity]

    confluences = build_confluences(levels, proximity_pts=15.0, min_count=2)

    # Enumerar hipos. Filtrar las que apuntan a DVA (DINAMICAS) — el markup
    # solo debe contener hipos con entry/stop/target en niveles ESTATICOS.
    # Las DVA se ven en vivo en el chart via los indicadores VWAP forks.
    all_hipos = enumerate_hipos(snap)
    static_hipos = [h for h in all_hipos if h.get("level_type") not in ("DVAH", "DVAL")]
    top_hipos = rank_hipos(static_hipos, snap, confluences=confluences, max_n=3)

    # Bias estructural mecánico
    cvas = snap.get("cvas", {})
    breaks_up = sum(1 for b in cvas.get("pvas", []) + cvas.get("cvas", [])
                    if b.get("closed_reason", "").startswith("breakout_up"))
    breaks_down = sum(1 for b in cvas.get("pvas", []) + cvas.get("cvas", [])
                      if b.get("closed_reason", "").startswith("breakout_down"))
    if breaks_up >= breaks_down + 2:
        bias = "bullish"
        regime = f"imbalance bullish ({breaks_up} breakouts up)"
    elif breaks_down >= breaks_up + 2:
        bias = "bearish"
        regime = f"imbalance bearish ({breaks_down} breakouts down)"
    else:
        bias = "neutral"
        regime = "rotacional / balance"

    # Convertir hipos enumerados al formato markup
    markup_hipos = [hipo_to_markup(h, i + 1, spot) for i, h in enumerate(top_hipos)]

    # Summary
    if markup_hipos:
        summary_parts = []
        for h in markup_hipos:
            tgs = h["targets"]
            summary_parts.append(
                f"{h['id'].upper()}[{h['grade']}] {h['setup_type']} {h['direction']} "
                f"E {h['entry']:.0f} S {h['stop']:.0f} -> T1 {tgs[0]['price']:.0f} T2 {tgs[1]['price']:.0f}"
            )
        summary = "  |  ".join(summary_parts)
    else:
        summary = f"AUTO {trigger_type.upper()} | spot {spot:.0f} | sin hipos detectables"

    # Analysis text
    analysis = f"AUTO-WATCHER {trigger_type.upper()} | {timestamp[:16]} | spot {spot:.1f}\n\n"
    analysis += f"REGIMEN: {regime}\n"
    analysis += f"BIAS: {bias}\n\n"
    if markup_hipos:
        analysis += "HIPOS (auto-rankeadas por proximidad+tipo+alineacion):\n"
        for h in markup_hipos:
            tgs = h["targets"]
            analysis += (f"  {h['id'].upper()} [{h['grade']}] {h['direction'].upper()} "
                         f"{h['setup_type']} | E {h['entry']:.0f} S {h['stop']:.0f} "
                         f"-> T1 {tgs[0]['price']:.0f} T2 {tgs[1]['price']:.0f}\n")
        analysis += "\nNOTA: hipos generadas automaticamente. Stops calculados con offset fijo;\n"
        analysis += "el stop real al disparar entry sera el ultimo HA pivot (Guia 03 §9).\n"
    else:
        analysis += "Sin hipos candidatas en proximidad razonable del spot.\n"

    # Cobertura aplicada
    cov = snap.get("coverage", {})
    eco = [tf for tf, val in cov.items() if val]
    if eco:
        analysis += f"\nCOBERTURA APLICADA: {', '.join(eco)} (TFs eco excluidos de confluencias)\n"

    # Ventana estadistica usada
    if atr and atr > 0:
        analysis += (f"\nVENTANA OPERATIVA: ATR(14)={atr:.0f}pts | proximity=±{proximity:.0f}pts "
                     f"(2x ATR). Niveles fuera de rango filtrados, salvo los estructurales "
                     f"(pVA active, DVA, CVA active).\n")

    # Persistir
    snapshot_id = f"NQ_{datetime.fromisoformat(timestamp.replace('Z','')).strftime('%Y%m%d_%H%M')}_{trigger_type.upper()}"
    save_result = save_snapshot(
        instrument=instrument,
        price_at_analysis=spot,
        timestamp=timestamp,
        snapshot_id=snapshot_id,
        regime=regime,
        bias=bias,
        summary=summary,
        analysis_text=analysis,
        confluences=confluences,
        levels=levels,
        hypos=markup_hipos,
    )

    return {
        "ok": True,
        "snapshot_id": snapshot_id,
        "n_levels": len(levels),
        "n_confluences": len(confluences),
        "n_hipos": len(markup_hipos),
        "regime": regime,
        "bias": bias,
        "save_action": save_result.get("action"),
    }


def watch_loop(interval_seconds: int = 10) -> None:
    requests_dir = nadro_dir() / "snapshot_requests"
    processed_dir = requests_dir / "processed"
    failed_dir = requests_dir / "failed"
    processing_dir = requests_dir / "processing"
    for d in (processed_dir, failed_dir, processing_dir):
        d.mkdir(parents=True, exist_ok=True)

    _log(f"Watcher iniciado | dir={requests_dir} | interval={interval_seconds}s")
    _log("Esperando snapshot requests... (Ctrl+C para salir)")

    while True:
        try:
            # Buscar archivos json en el directorio principal (no en subcarpetas)
            pending = sorted([p for p in requests_dir.glob("*.json") if p.is_file()])

            for req_path in pending:
                _log(f"Detectado: {req_path.name}")
                # Mover a processing/ inmediatamente para evitar doble proceso
                processing_path = processing_dir / req_path.name
                try:
                    shutil.move(str(req_path), str(processing_path))
                except Exception as e:
                    _log(f"  ERROR move a processing: {e}")
                    continue

                try:
                    result = process_request(processing_path)
                except Exception as e:
                    _log(f"  EXCEPCION: {e}")
                    traceback.print_exc()
                    result = {"ok": False, "error": str(e)}

                if result.get("ok"):
                    _log(f"  OK: {result['snapshot_id']} | "
                         f"{result['n_hipos']}h, {result['n_confluences']}cf, {result['n_levels']}lv | "
                         f"{result['bias']}/{result['regime']}")
                    shutil.move(str(processing_path), str(processed_dir / req_path.name))
                else:
                    _log(f"  FALLO: {result.get('error', '?')}")
                    shutil.move(str(processing_path), str(failed_dir / req_path.name))

            time.sleep(interval_seconds)
        except KeyboardInterrupt:
            _log("Detenido por usuario (Ctrl+C). Adios.")
            sys.exit(0)
        except Exception as e:
            _log(f"ERROR en loop principal: {e}")
            traceback.print_exc()
            time.sleep(interval_seconds * 2)


def main() -> None:
    parser = argparse.ArgumentParser(description="NADRO snapshot request watcher")
    parser.add_argument("--interval", type=int, default=10,
                        help="segundos entre polls (default 10)")
    args = parser.parse_args()
    watch_loop(interval_seconds=args.interval)


if __name__ == "__main__":
    main()
