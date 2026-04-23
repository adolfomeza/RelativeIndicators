"""Cliente HTTP del AddOn RelativeObserver (localhost:7891).

Consume los endpoints del AddOn NinjaScript para exponer al MCP:
- /health
- /subscriptions
- /subscribe/{instrument} (POST/DELETE)
- /quote/{instrument}
- /ticks/{instrument}?n=200
- /bars/{instrument}?tf=1m&n=50

Todos los tools fallan limpiamente si el AddOn no está corriendo (NT cerrado
o el addon no instalado): devuelven ``{"error": "...", "addon_reachable": false}``.
"""
from __future__ import annotations

import json
import os
from typing import Any
from urllib import error, parse, request


DEFAULT_BASE = os.environ.get("RELATIVE_OBSERVER_URL", "http://localhost:7891")
DEFAULT_TIMEOUT = float(os.environ.get("RELATIVE_OBSERVER_TIMEOUT", "10"))


def _url(path: str, base: str | None = None) -> str:
    base = (base or DEFAULT_BASE).rstrip("/")
    if not path.startswith("/"):
        path = "/" + path
    return base + path


def _request(
    method: str,
    path: str,
    query: dict | None = None,
    timeout: float | None = None,
    base: str | None = None,
) -> dict:
    url = _url(path, base=base)
    if query:
        qs = parse.urlencode({k: v for k, v in query.items() if v is not None})
        url = f"{url}?{qs}"
    req = request.Request(url, method=method)
    try:
        with request.urlopen(req, timeout=timeout or DEFAULT_TIMEOUT) as resp:
            body = resp.read().decode("utf-8", errors="replace")
            status = resp.status
    except error.HTTPError as e:
        try:
            body = e.read().decode("utf-8", errors="replace")
        except Exception:
            body = ""
        status = e.code
    except (error.URLError, ConnectionError, TimeoutError, OSError) as e:
        return {
            "error": f"AddOn no alcanzable en {url}: {e}. "
                     "Verifica que NT8 esté abierto y RelativeObserver cargado.",
            "addon_reachable": False,
        }

    try:
        data = json.loads(body) if body else {}
    except json.JSONDecodeError:
        return {"error": "respuesta no JSON", "status": status, "body": body[:400]}
    if status >= 400:
        return {"error": data.get("error", f"HTTP {status}"), "status": status, "addon_reachable": True}
    if isinstance(data, dict):
        data.setdefault("addon_reachable", True)
    return data


def health() -> dict:
    """Info básica del AddOn + estado de connections de NT."""
    return _request("GET", "/health")


def list_subscriptions() -> dict:
    """Instrumentos actualmente suscritos al MarketData."""
    return _request("GET", "/subscriptions")


def subscribe(instrument: str) -> dict:
    """Suscribe market data para un instrumento. Idempotente."""
    return _request("POST", f"/subscribe/{parse.quote(instrument, safe='')}")


def unsubscribe(instrument: str) -> dict:
    """Cancela la suscripción de market data."""
    return _request("DELETE", f"/subscribe/{parse.quote(instrument, safe='')}")


def get_quote(instrument: str) -> dict:
    """Last / Bid / Ask / volumen actual del instrumento.

    Si aún no estaba suscrito, el AddOn lo suscribe automáticamente; las primeras
    llamadas pueden devolver NaN hasta que lleguen los primeros ticks.
    """
    return _request("GET", f"/quote/{parse.quote(instrument, safe='')}")


def get_ticks(instrument: str, n: int = 200) -> dict:
    """Últimos N ticks del buffer circular (máx 5000, default 200).

    Devuelve ticks de tipo Last/Bid/Ask mezclados en orden cronológico.
    """
    return _request(
        "GET",
        f"/ticks/{parse.quote(instrument, safe='')}",
        query={"n": n},
    )


def list_accounts() -> dict:
    """Cuentas conectadas con cash/PnL/posiciones abiertas/órdenes activas."""
    return _request("GET", "/accounts")


def list_positions(account: str | None = None, include_flat: bool = False) -> dict:
    """Posiciones abiertas. Por default excluye las Flat.

    ``account`` opcional filtra por nombre de cuenta.
    """
    q: dict = {}
    if account: q["account"] = account
    if include_flat: q["include_flat"] = "true"
    return _request("GET", "/positions", query=q or None)


def list_orders(account: str | None = None, state: str = "active") -> dict:
    """Órdenes. ``state`` ∈ active | filled | all (default active)."""
    q: dict = {"state": state}
    if account: q["account"] = account
    return _request("GET", "/orders", query=q)


def list_charts() -> dict:
    """Charts y SuperDOMs abiertos en NT con instrumento/período/indicadores cargados."""
    return _request("GET", "/charts")


def list_executions(account: str | None = None, n: int = 50, since_hours: int = 0) -> dict:
    """Ejecuciones (fills) recientes. Por cuenta + opcionalmente filtrado por antigüedad."""
    q: dict = {"n": n}
    if account: q["account"] = account
    if since_hours > 0: q["since_hours"] = since_hours
    return _request("GET", "/executions", query=q)


def list_completed_trades(account: str | None = None, n: int = 50) -> dict:
    """Trades cerrados (entry+exit emparejados) con PnL, MAE/MFE.

    Fuente: ``Account.SystemPerformance.AllTrades`` — mismo motor que Performance Explorer.
    """
    q: dict = {"n": n}
    if account: q["account"] = account
    return _request("GET", "/trades", query=q)


def list_indicator_states() -> dict:
    """Todos los estados publicados en RelativeIndicatorRegistry (opt-in por indicador)."""
    return _request("GET", "/indicator-state")


def get_indicator_state(key: str) -> dict:
    """Estado actual de un indicador publicado bajo ``key`` en el registry."""
    from urllib.parse import quote as _q
    return _request("GET", f"/indicator-state/{_q(key, safe='/')}")


def get_print_output(
    n: int = 200,
    indicator: str | None = None,
    instrument: str | None = None,
    level_min: int = 1,
    since_minutes: int = 0,
) -> dict:
    """Buffer circular de RelativeLog: logs estructurados de los indicadores.

    Los indicadores deben llamar ``this.RLog(...)`` (extension method de
    RelativeLog.cs) para aparecer acá. El buffer guarda hasta 2000 entries.
    """
    q: dict = {"n": n, "level_min": level_min}
    if indicator: q["indicator"] = indicator
    if instrument: q["instrument"] = instrument
    if since_minutes > 0: q["since_minutes"] = since_minutes
    return _request("GET", "/print-output", query=q)


def clear_print_output() -> dict:
    """Vacía el buffer de RelativeLog (no resetea total_count monotónico)."""
    return _request("DELETE", "/print-output")


def get_bars(
    instrument: str,
    tf: str = "1m",
    n: int = 50,
    from_date: str | None = None,
    to_date: str | None = None,
) -> dict:
    """Barras del instrumento. Dos modos:

    - **Últimas N** (default): pasar ``n`` (max 10000). Latencia ~1-15s.
    - **Rango por fechas**: pasar ``from_date`` y ``to_date`` en ISO
      (``YYYY-MM-DD`` o ``YYYY-MM-DDTHH:MM:SS``). Sin cap de 10k — pedí el
      rango completo que tengas en NT. Latencia hasta 60s para rangos grandes.

    ``tf`` sufijos: ``s`` segundos, ``m`` minutos, ``h`` horas, ``d`` días,
    ``t`` ticks, ``v`` volumen, ``r`` rango. Ejemplos: ``1m``, ``5m``, ``1h``.
    """
    if from_date and to_date:
        query = {"tf": tf, "from": from_date, "to": to_date}
        timeout = 90
    else:
        query = {"tf": tf, "n": n}
        timeout = 30
    return _request(
        "GET",
        f"/bars/{parse.quote(instrument, safe='')}",
        query=query,
        timeout=timeout,
    )


def get_bars_with_ha(instrument: str, tf: str = "1m", n: int = 50) -> dict:
    """Últimas N barras OHLCV + Heikin Ashi calculado server-side.

    HA_close = (O+H+L+C)/4
    HA_open  = (HA_open[prev] + HA_close[prev]) / 2
    HA_high  = max(H, HA_open, HA_close)
    HA_low   = min(L, HA_open, HA_close)
    color    = 'BULL' si HA_close > HA_open else 'BEAR'

    Devuelve misma estructura que get_bars pero cada bar tiene campos extra:
    hao, hac, hah, hal, ha_color, ha_change (True si cambió de color vs bar previa).
    """
    result = get_bars(instrument, tf=tf, n=n)
    if "error" in result or not result.get("bars"):
        return result

    bars = result["bars"]
    prev_hao = None
    prev_hac = None
    prev_color = None
    for b in bars:
        hac = (b["o"] + b["h"] + b["l"] + b["c"]) / 4.0
        if prev_hao is None:
            hao = (b["o"] + b["c"]) / 2.0
        else:
            hao = (prev_hao + prev_hac) / 2.0
        hah = max(b["h"], hao, hac)
        hal = min(b["l"], hao, hac)
        color = "BULL" if hac > hao else "BEAR"
        b["hao"] = round(hao, 4)
        b["hac"] = round(hac, 4)
        b["hah"] = round(hah, 4)
        b["hal"] = round(hal, 4)
        b["ha_color"] = color
        b["ha_change"] = prev_color is not None and color != prev_color
        prev_hao, prev_hac, prev_color = hao, hac, color
    return result
