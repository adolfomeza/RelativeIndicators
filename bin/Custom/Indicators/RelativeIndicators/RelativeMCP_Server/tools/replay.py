"""Replay snapshots NADRO — point-in-time analysis sin recomputar nada.

Pipeline:
1. Resolver pit-open timestamp por instrumento (PIT_HOURS_VET)
2. Llamar `get_dva_at` para los 5 TFs (Daily/Weekly/Monthly/Quarterly/Annual)
3. Llamar `get_cvas(cutoff_date=as_of)` para CVAs/pVAs cerradas hasta ese momento
4. Aplicar reglas de cobertura entre TFs adyacentes (sección 6 nadro_master.md)
5. Tomar spot del bar at as_of (close del último bar <= as_of)
6. Ensamblar snapshot dict (no persiste — caller decide)

Para backtest walk-forward, este es el bloque atómico que se llama
por cada día del rango.

Requisitos:
- NT8 abierto con charts cargados que tengan los 5 forks VWAP del instrumento
- Bars histórica suficiente (>= as_of - lookback del TF mayor que querramos)
- AddOn RelativeObserver corriendo (puerto 7891)
"""
from __future__ import annotations

from datetime import datetime, timedelta
from typing import Any

from . import observer
from . import tpo_cva  # get_cvas


# Lookup de pit-open por master instrument (mismo que eod_review.PIT_HOURS_VET)
PIT_HOURS = {
    "MGC": ("08:20", "13:30"),
    "MCL": ("09:00", "14:30"),
    "MES": ("09:30", "16:00"),
    "MNQ": ("09:30", "16:00"),
    "MYM": ("09:30", "16:00"),
    "M2K": ("09:30", "16:00"),
    "ES":  ("09:30", "16:00"),
    "NQ":  ("09:30", "16:00"),
    "YM":  ("09:30", "16:00"),
    "RTY": ("09:30", "16:00"),
    "GC":  ("08:20", "13:30"),
    "CL":  ("09:00", "14:30"),
    "ZN":  ("08:20", "15:00"),
    "ZB":  ("08:20", "15:00"),
    # CME FX futures — RTH NY session (overlap Londres + NY)
    "6E":  ("07:20", "14:00"),
    "6B":  ("07:20", "14:00"),
    "6J":  ("07:20", "14:00"),
    "6A":  ("07:20", "14:00"),
    "6C":  ("07:20", "14:00"),
    # CBOT Grains — pit RTH 09:30-14:15 ET
    "ZC":  ("09:30", "14:15"),  # Corn
    "ZW":  ("09:30", "14:15"),  # Wheat
    "ZS":  ("09:30", "14:15"),  # Soybean
    "ZL":  ("09:30", "14:15"),  # Soy oil
    "ZM":  ("09:30", "14:15"),  # Soy meal
    "ZO":  ("09:30", "14:15"),  # Oats
    "ZR":  ("09:30", "14:15"),  # Rice
}


def _master_symbol(instrument: str) -> str:
    """Extrae el master symbol de un FullName ('NQ 06-26' -> 'NQ')."""
    return instrument.split(" ")[0].upper()


def _pit_open_dt(instrument: str, date_str: str) -> datetime:
    """Construye datetime de pit-open ET para un instrumento/fecha."""
    master = _master_symbol(instrument)
    open_str, _ = PIT_HOURS.get(master, ("09:30", "16:00"))
    h, m = open_str.split(":")
    d = datetime.strptime(date_str, "%Y-%m-%d")
    return d.replace(hour=int(h), minute=int(m), second=0, microsecond=0)


def _is_tf_eco(timeframe: str, asof: datetime) -> bool:
    """Replica la lógica de IsCurrentEchoOfSubPeriod del indicador.

    True = ese TF es eco del TF inferior y NO se debe contar como confluencia.
    """
    tf = timeframe.lower()
    if tf == "weekly":
        return asof.weekday() == 0  # Monday
    if tf == "monthly":
        return asof.day <= 7
    if tf == "quarterly":
        first_month_of_q = ((asof.month - 1) // 3) * 3 + 1
        return asof.month == first_month_of_q
    if tf == "annual":
        return asof.month <= 3
    return False


def _read_developing_dva_from_file(instrument: str, tf: str) -> dict | None:
    """Lee DVAH/VWAP/DVAL developing del header de `VwapLevels/{TF}_{master}.txt`.

    Fallback usado cuando el query handler del fork VWAP no está registrado
    (forks sin código de RegisterQueryHandler). El archivo se exporta cada 5s
    por el indicador en NT, así que el lag es <5s vs realtime.

    Returns: {dvah, vwap, dval, timestamp} o None si no se puede leer.
    """
    from .. import paths

    master = instrument.split(" ")[0].upper()
    path = paths.vwap_levels_dir() / f"{tf}_{master}.txt"
    if not path.exists():
        return None
    try:
        content = path.read_text(encoding="utf-8", errors="ignore")
    except Exception:
        return None

    out = {}
    for line in content.splitlines():
        line = line.strip()
        if "=" not in line:
            continue
        k, v = line.split("=", 1)
        k = k.strip().upper()
        v = v.strip()
        if k == "DVAH":
            try: out["dvah"] = float(v)
            except: pass
        elif k == "DVAL":
            try: out["dval"] = float(v)
            except: pass
        elif k == "VWAP":
            try: out["vwap"] = float(v)
            except: pass
        elif k == "TIMESTAMP":
            out["bar_time"] = v
        elif k.startswith("ZONE_"):
            break  # solo header
    if "dvah" in out and "dval" in out:
        return out
    return None


def _read_previous_zones(instrument: str, asof_dt: datetime) -> list[dict]:
    """Lee zonas previous (pXDVA) de los archivos `VwapLevels/{TF}_{master}.txt`.

    Cada fork VWAP exporta un archivo con el DVA developing actual + ZONE_N históricas
    (períodos cerrados). Las ZONE_N son las pXDVAH/pXDVAL — operables NADRO §2.5.

    Filtra solo zonas con StartTime <= asof_dt (no leak de zonas futuras al as_of).

    Returns: lista de dicts {label, price, tf, side, start_time}.
    """
    from .. import paths

    master = instrument.split(" ")[0].upper()

    # Mapping TF -> (label_prefix_upper, label_prefix_lower)
    # Daily previous = pDVAH/pDVAL (única excepción: NO `pDDVAH`, una sola D)
    tf_labels = {
        "Daily":     ("pDVAH",  "pDVAL"),
        "Weekly":    ("pWDVAH", "pWDVAL"),
        "Monthly":   ("pMDVAH", "pMDVAL"),
        "Quarterly": ("pQDVAH", "pQDVAL"),
        "Annual":    ("pYDVAH", "pYDVAL"),
    }

    vwap_dir = paths.vwap_levels_dir()

    out = []
    for tf, (lbl_up, lbl_lo) in tf_labels.items():
        path = vwap_dir / f"{tf}_{master}.txt"
        if not path.exists():
            continue
        try:
            content = path.read_text(encoding="utf-8", errors="ignore")
        except Exception:
            continue

        for line in content.splitlines():
            line = line.strip()
            if not line.startswith("ZONE_"):
                continue
            try:
                _, val = line.split("=", 1)
                parts = val.split("|")
                if len(parts) < 4:
                    continue
                upper = float(parts[0])
                lower = float(parts[2])
                start_time_str = parts[3].strip()
                # ZONE_N = upper|mid|lower|startTime (mid = VWAP central, no operable)
                try:
                    zt = datetime.strptime(start_time_str, "%Y-%m-%d %H:%M:%S")
                except Exception:
                    zt = None
                if zt is not None and zt > asof_dt:
                    continue  # zona futura al as_of, skip

                out.append({"label": lbl_up, "price": upper, "tf": tf,
                            "side": "upper", "start_time": start_time_str})
                out.append({"label": lbl_lo, "price": lower, "tf": tf,
                            "side": "lower", "start_time": start_time_str})
            except Exception:
                continue
    return out


def _cvas_from_indicator_state(instrument: str, as_of: str) -> dict | None:
    """Construye `cvas_result` a partir del state que publica RelativeVolumeProfile.

    Retorna None si el indicador no está publicando para `instrument` (no cargado en NT,
    versión vieja sin payload composites, etc.) — el caller hace fallback al cálculo Python.

    Aplica el filtro `as_of`: sólo incluye profiles con end_time <= as_of (evita data leak
    del día actual). El active_pva se incluye sólo si su start_time <= as_of.

    Deriva `closed_reason` y `secondary_lines` a partir de los bloques consecutivos
    usando la misma lógica del indicador (tolerancia 0.5 pts).
    """
    try:
        states = observer.list_indicator_states()
    except Exception:
        return None
    if not isinstance(states, dict):
        return None

    # Buscar key del RelativeVolumeProfile para este instrumento
    target_prefix = f"RelativeVolumeProfile:{instrument}:"
    chosen = None
    for s in states.get("states", []):
        if not isinstance(s, dict): continue
        key = s.get("key", "")
        if not key.startswith(target_prefix): continue
        payload = s.get("payload", {})
        if not isinstance(payload, dict): continue
        # Versión vieja del indicador no incluye composites/closed_pvas
        if "composites" not in payload or "closed_pvas" not in payload:
            continue
        chosen = payload
        break
    if chosen is None:
        return None

    # Parsear as_of para filtrar profiles posteriores
    try:
        asof_dt = datetime.fromisoformat(as_of.replace("Z", ""))
    except Exception:
        return None

    composites = chosen.get("composites", []) or []
    closed_pvas = chosen.get("closed_pvas", []) or []
    active_pva = chosen.get("active_pva")

    # Filtrar: sólo profiles que ya estaban CERRADOS antes de as_of.
    # end_time <= as_of significa que el día/sesión ya cerró cuando hicimos el snapshot.
    def _within_asof(p):
        et = p.get("end_time")
        if not et: return True
        try:
            return datetime.fromisoformat(et) <= asof_dt
        except Exception:
            return True

    composites = [c for c in composites if _within_asof(c)]
    closed_pvas = [p for p in closed_pvas if _within_asof(p)]

    # active_pva = TPO en DESARROLLO del día actual. NUNCA es nivel operable
    # (sus bordes cambian intra-día — NADRO §5.1.1: DV no establecido = NO operable).
    # Lo ignoramos siempre para construcción de blocks/out_pvas.
    #
    # IMPORTANTE: el indicador C# escribe end_time = DateTime.MinValue ('0001-01-01...')
    # mientras la sesión sigue activa. NO usar end_time como criterio de cierre —
    # confunde con timestamps anteriores al as_of.
    active_pva = None

    # Unificar bloques en orden cronológico para derivar closed_reason / secondary_lines.
    # Cada item es {start_date, end_date, vah, val, poc, status, ...}.
    # NOTA: NO incluir active_pva (TPO del día en desarrollo). Es dinámico, no
    # operable según preferencia del usuario — sus bordes cambian intra-día.
    blocks = []
    for c in composites:
        blocks.append({**c, "_kind": "CVA"})
    for p in closed_pvas:
        blocks.append({**p, "_kind": "pVA"})

    # Ordenar por start_date
    def _key_start(b):
        return b.get("start_time") or b.get("start_date", "")
    blocks.sort(key=_key_start)

    # Tolerance del breakout. El indicador lo publica en el payload con valor absoluto en
    # puntos del instrumento. Si no, escalamos por el precio de referencia para evitar
    # bug en instrumentos como 6E (tick 0.0001 → TOL=0.5 daría 5000 pips, todo sería drift).
    # Heurística: TOL ≈ 0.005% del precio (medio tick típico). NQ@27000 → ~1.35 pts.
    # 6E@1.17 → ~0.00006 = 0.6 pips. MGC@4500 → ~0.22.
    publish_tol = chosen.get("auto_merge_breakout_tolerance")
    if publish_tol is not None and publish_tol > 0:
        # Validar contra escala: si el publish_tol es claramente demasiado grande
        # respecto al precio (>0.1% del precio), recalcular.
        ref = max(closed_pvas[-1].get("vah", 0) if closed_pvas else 0, 0.0001)
        if publish_tol > ref * 0.001:
            TOL = ref * 0.00005
        else:
            TOL = publish_tol
    else:
        ref = closed_pvas[-1].get("vah", 0) if closed_pvas else 0
        TOL = max(ref * 0.00005, 0.0001) if ref > 0 else 0.5
    secondary_lines = []
    out_pvas = []
    out_cvas = []
    for i, b in enumerate(blocks):
        nxt = blocks[i + 1] if i + 1 < len(blocks) else None
        if nxt is not None and b.get("status") != "active":
            try:
                if nxt["val"] > b["vah"] + TOL:
                    b["closed_reason"] = "breakout_up"
                    b["closed_on"] = nxt.get("start_date")
                    secondary_lines.append({
                        "price": b["vah"],
                        "side": "upper_of_closed",
                        "from_start": b.get("start_date"),
                        "from_end": b.get("end_date"),
                        "block_type": b["_kind"],
                        "closed_by_day": nxt.get("start_date"),
                        "reason": "breakout_up",
                    })
                elif nxt["vah"] < b["val"] - TOL:
                    b["closed_reason"] = "breakout_down"
                    b["closed_on"] = nxt.get("start_date")
                    secondary_lines.append({
                        "price": b["val"],
                        "side": "lower_of_closed",
                        "from_start": b.get("start_date"),
                        "from_end": b.get("end_date"),
                        "block_type": b["_kind"],
                        "closed_by_day": nxt.get("start_date"),
                        "reason": "breakout_down",
                    })
                else:
                    b["closed_reason"] = "drift"
                    b["closed_on"] = nxt.get("start_date")
            except Exception:
                pass

        # Reformat: días array para compatibilidad con get_cvas
        item = {
            "days": [b.get("start_date")] if b["_kind"] == "pVA" else [b.get("start_date"), b.get("end_date")],
            "val": b.get("val"),
            "vah": b.get("vah"),
            "poc": b.get("poc"),
            "start_date": b.get("start_date"),
            "end_date": b.get("end_date"),
            "status": b.get("status", "closed"),
            "type": b["_kind"],
        }
        if "closed_reason" in b:
            item["closed_reason"] = b["closed_reason"]
        if "closed_on" in b:
            item["closed_on"] = b["closed_on"]
        if b["_kind"] == "CVA":
            out_cvas.append(item)
        else:
            out_pvas.append(item)

    return {
        "instrument": instrument,
        "session": "indicator",
        "pvas": out_pvas,
        "cvas": out_cvas,
        "secondary_lines": secondary_lines,
        "config": {
            "overlap_threshold": chosen.get("auto_merge_overlap_threshold"),
            "require_dshape": chosen.get("auto_merge_require_dshape"),
            "auto_merge_enabled": chosen.get("auto_merge_enabled"),
        },
    }


def nadro_snapshot_replay(
    instrument: str,
    as_of: str | None = None,
    date: str | None = None,
    kind: str = "pre_pit",
    lookback_minutes: int = 5,
) -> dict:
    """Genera un snapshot NADRO point-in-time sin recomputar nada.

    Args:
        instrument: FullName del instrumento (ej ``"NQ 06-26"``).
        as_of: timestamp ISO 8601 explícito. Si None, se infiere de ``date`` + ``kind``.
        date: ``YYYY-MM-DD`` — usado si ``as_of`` no fue dado. Default = hoy.
        kind: ``pre_pit`` (default, 5min antes de pit-open) | ``pit_open`` |
              ``mid_session`` (12:00 ET pit) | ``eod`` (1min antes pit-close).
        lookback_minutes: para ``pre_pit``, cuánto antes del pit-open posicionarse.

    Returns:
        dict con DVAs por TF, CVAs/pVAs cerradas, spot, y flags de cobertura.

    Notas:
        - El indicador correspondiente debe estar cargado en NT con suficiente
          historia hasta ``as_of``.
        - Los TFs flagueados como ``eco`` deben excluirse de cualquier cluster
          de confluencias (NADRO Ley §6.5).
    """
    # Resolver as_of
    if as_of is None:
        if date is None:
            date = datetime.utcnow().strftime("%Y-%m-%d")
        master = _master_symbol(instrument)
        open_str, close_str = PIT_HOURS.get(master, ("09:30", "16:00"))
        d = datetime.strptime(date, "%Y-%m-%d")
        if kind == "pre_pit":
            h, m = open_str.split(":")
            base = d.replace(hour=int(h), minute=int(m))
            asof_dt = base - timedelta(minutes=lookback_minutes)
        elif kind == "pit_open":
            h, m = open_str.split(":")
            asof_dt = d.replace(hour=int(h), minute=int(m))
        elif kind == "eod":
            h, m = close_str.split(":")
            base = d.replace(hour=int(h), minute=int(m))
            asof_dt = base - timedelta(minutes=1)
        elif kind == "mid_session":
            asof_dt = d.replace(hour=12, minute=0)
        else:
            return {"error": f"kind inválido: {kind}"}
        as_of = asof_dt.strftime("%Y-%m-%dT%H:%M:%S")
    else:
        try:
            asof_dt = datetime.fromisoformat(as_of.replace("Z", ""))
        except Exception as e:
            return {"error": f"as_of no parseable: {e}"}

    # Query DVA por TF — solo LTWVs (Long-Term VWAPs) según NADRO 4.0 §3.
    # Daily NO es LTWV; el DVAH/DVAL diario es referencia visual del usuario,
    # no operable. La función de "día previo" la cumple pVA (TPO) y pDVAH/pDVAL
    # (zona Daily previous), que se cargan abajo desde VwapLevels/.
    tfs = ["Weekly", "Monthly", "Quarterly", "Annual"]
    dvas: dict[str, dict] = {}
    coverage: dict[str, bool] = {}
    for tf in tfs:
        eco = _is_tf_eco(tf, asof_dt)
        coverage[tf.lower() + "_eco"] = eco
        result = observer.get_dva_at(instrument=instrument, timeframe=tf, as_of=as_of)
        # Fallback: si el query handler no está registrado (404), leer el developing
        # del archivo VwapLevels/{TF}_{master}.txt. Solo aplica si as_of es "ahora"
        # (el archivo es realtime, no histórico).
        if isinstance(result, dict) and result.get("error") and "no query handler" in str(result.get("error", "")):
            file_dva = _read_developing_dva_from_file(instrument, tf)
            if file_dva:
                result = {
                    "key": f"Relative{tf}Vwap:{instrument}",
                    "as_of": as_of,
                    "payload": {
                        "dvah": file_dva.get("dvah"),
                        "dval": file_dva.get("dval"),
                        "vwap": file_dva.get("vwap"),
                        "bar_time": file_dva.get("bar_time"),
                    },
                    "_source": "vwap_levels_file",
                }
        if isinstance(result, dict):
            result["is_echo_of_sub_period"] = eco
        dvas[tf.lower()] = result

    # Zonas previous (pXDVAH/pXDVAL) leídas de los archivos VwapLevels/{TF}_{master}.txt
    # que exporta cada fork VWAP. Son zonas ya CERRADAS (no developing) y por tanto
    # estáticas — entran al markup como niveles operables.
    # NADRO §2.5 las marca como ✅ operable directo.
    previous_zones = _read_previous_zones(instrument, asof_dt)

    # CVAs / pVAs — preferir el SOURCE OF TRUTH del indicador NT8 si está publicando.
    # El indicador RelativeVolumeProfile aplica auto-merge NADRO con threshold 0.40
    # + D-shape gate. Si recalculamos en Python con tpo_cva.get_cvas (threshold 0.50,
    # sin D-shape), divergimos del chart que ve el usuario.
    #
    # Fallback al cálculo Python sólo si el indicador no publica (no cargado).
    cvas_result = _cvas_from_indicator_state(instrument, as_of)
    if cvas_result is None:
        cvas_result = tpo_cva.get_cvas(
            instrument=instrument,
            weeks_back=4,
            session="rth",
            as_of=as_of,
        )
        cvas_result["_source"] = "python_recalc"
    else:
        cvas_result["_source"] = "indicator_registry"

    # Spot del bar at as_of. Estrategia con cascada de fuentes (de más específica
    # a más genérica) para no depender del Daily VWAP — el usuario lo usa como
    # referencia visual, NO obligatoria. Si solo está cargado el RelativeVolumeProfile
    # (caso típico), igual debe poder calcularse el spot.
    spot_close = None
    spot_bar_time = None
    spot_source = None

    # 1) RelativeVolumeProfile — siempre cargado en charts NADRO; publica `close`.
    try:
        states = observer.list_indicator_states()
        if isinstance(states, dict):
            target = f"RelativeVolumeProfile:{instrument}:"
            for s in states.get("states", []):
                if isinstance(s, dict) and s.get("key", "").startswith(target):
                    payload = s.get("payload", {})
                    if isinstance(payload, dict) and payload.get("close") is not None:
                        spot_close = payload.get("close")
                        spot_bar_time = payload.get("bar_time")
                        spot_source = "RelativeVolumeProfile"
                        break
    except Exception:
        pass

    # 2) Daily VWAP fork (si está cargado). El usuario puede no tenerlo; opcional.
    if spot_close is None:
        daily = dvas.get("daily", {})
        if isinstance(daily, dict):
            payload = daily.get("payload", {})
            if isinstance(payload, dict) and payload.get("close") is not None:
                spot_close = payload.get("close")
                spot_bar_time = payload.get("bar_time")
                spot_source = "RelativeDailyVwap"

    # 3) Bars del observer — fallback universal, no depende de ningún indicador.
    if spot_close is None:
        try:
            bars_data = observer.get_bars(instrument=instrument, tf="1m", n=2)
            bars = bars_data.get("bars", [])
            if bars:
                last = bars[-1]
                spot_close = last.get("c")
                spot_bar_time = last.get("t")
                spot_source = "observer_bars_1m"
        except Exception:
            pass

    return {
        "instrument": instrument,
        "as_of": as_of,
        "kind": kind,
        "spot": {
            "close": spot_close,
            "bar_time": spot_bar_time,
            "source": spot_source,
        },
        "dvas": dvas,
        "coverage": coverage,
        "cvas": cvas_result,
        "previous_zones": previous_zones,
        "_notes": [
            "DVAs LTWV (Weekly/Monthly/Quarterly/Annual) operables. Daily DVA EXCLUIDO (referencia visual del usuario).",
            "Eco de TF: si is_echo_of_sub_period=true, NO contar como confluencia separada del TF inferior.",
            "previous_zones: pDVAH/pDVAL + pWDVA/pMDVA/pQDVA/pYDVA leídos de VwapLevels/{TF}_{master}.txt — operables NADRO §2.5.",
            "CVAs source: 'indicator_registry' = leído del indicador NT (zonas idénticas al chart). 'python_recalc' = fallback con tpo_cva.get_cvas.",
            "Spot source cascada: RelativeVolumeProfile.close > Daily VWAP > observer.get_bars(1m).",
            "Spot = close del último bar <= as_of (no quote live).",
        ],
    }
