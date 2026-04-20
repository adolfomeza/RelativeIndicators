"""RelativeIndicators MCP Server — Fase 1a (solo lectura).

Expone vía MCP:
  * Logs y traces de NinjaTrader 8 (diagnóstico en vivo mientras codeas)
  * VwapLevels exportados por la suite (niveles DVAH/VWAP/DVAL + confluencias)
  * TradeExports (CSV de trades por cuenta)

Transporte: stdio. Se registra en ``.mcp.json`` en la raíz del repo.
"""
from __future__ import annotations

from fastmcp import FastMCP

from .paths import (
    logs_dir,
    nt_home,
    trace_dir,
    trade_exports_dir,
    vwap_levels_dir,
)
from .tools import backtest as backtest_tool
from .tools import backtest_charts as backtest_charts_tool
from .tools import compile as compile_tool
from .tools import exports as trade_exports
from .tools import logs as nt_logs
from .tools import nadro as nadro_tool
from .tools import observer
from .tools import vwap_levels as vwap


mcp = FastMCP(
    name="relative-indicators",
    instructions=(
        "Servidor MCP para la suite RelativeIndicators en NinjaTrader 8. "
        "Solo lectura: logs, traces, niveles VWAP exportados y CSVs de trades. "
        "Útil para diagnosticar lo que pasa en NT mientras desarrollas indicadores."
    ),
)


# ---------------------------------------------------------------------------
# Health & paths
# ---------------------------------------------------------------------------


@mcp.tool
def health() -> dict:
    """Verifica que las rutas de NT están disponibles."""
    paths = {
        "nt_home": nt_home(),
        "logs_dir": logs_dir(),
        "trace_dir": trace_dir(),
        "vwap_levels_dir": vwap_levels_dir(),
        "trade_exports_dir": trade_exports_dir(),
    }
    return {
        "server": "relative-indicators",
        "version": "0.1.0",
        "paths": {k: {"path": str(v), "exists": v.exists()} for k, v in paths.items()},
    }


# ---------------------------------------------------------------------------
# NT logs
# ---------------------------------------------------------------------------


@mcp.tool
def tail_nt_log(lines: int = 100, level_min: int = 1) -> dict:
    """Últimas ``lines`` entradas del log principal de NinjaTrader.

    ``level_min`` filtra por severidad (1=Info, 2=Warning, 3=Error).
    Útil tras un F7 o para revisar desconexiones y errores de cargado de assemblies.
    """
    return nt_logs.tail_nt_log(lines=lines, level_min=level_min)


@mcp.tool
def search_nt_log(pattern: str, since_minutes: int = 60, case_sensitive: bool = False) -> dict:
    """Busca regex en los logs (por defecto última hora, case-insensitive)."""
    return nt_logs.search_nt_log(
        pattern=pattern, since_minutes=since_minutes, case_sensitive=case_sensitive
    )


@mcp.tool
def list_indicator_traces() -> dict:
    """Carpetas de trace por indicador (RelativeVwap, RelativeNewsSquawk, etc.).

    Tu suite escribe ahí vía ``LogToFile``. Devuelve indicadores con archivos y fecha del último.
    """
    return nt_logs.list_indicator_traces()


@mcp.tool
def tail_indicator_trace(indicator: str, lines: int = 100, file: str | None = None) -> dict:
    """Últimas ``lines`` líneas del trace de un indicador específico.

    ``indicator`` = nombre de la carpeta (ej. ``RelativeVwap``).
    Si no pasas ``file``, toma el archivo más reciente de esa carpeta.
    """
    return nt_logs.tail_indicator_trace(indicator=indicator, lines=lines, file=file)


@mcp.tool
def get_trace_today() -> dict:
    """Archivos trace.YYYYMMDD.* del día (diagnóstico profundo de NT)."""
    return nt_logs.get_trace_today()


# ---------------------------------------------------------------------------
# VWAP levels + confluencias
# ---------------------------------------------------------------------------


@mcp.tool
def list_vwap_instruments() -> dict:
    """Instrumentos con archivos VwapLevels/* presentes."""
    return vwap.list_instruments()


@mcp.tool
def read_vwap_levels(instrument: str, timeframe: str) -> dict:
    """DVAH/VWAP/DVAL + zonas históricas de un instrumento + timeframe.

    ``timeframe`` ∈ Daily, Weekly, Monthly, Quarterly, Annual.
    """
    return vwap.read_vwap_levels(instrument=instrument, timeframe=timeframe)


@mcp.tool
def list_confluences(instrument: str, only_active: bool = True) -> dict:
    """Grupos de confluencia de un instrumento.

    Por default solo los activos. Con ``only_active=False`` incluye históricos.
    """
    return vwap.list_confluences(instrument=instrument, only_active=only_active)


@mcp.tool
def vwap_snapshot(instrument: str) -> dict:
    """Snapshot completo: todos los TFs + confluencias activas de un instrumento."""
    return vwap.snapshot(instrument=instrument)


# ---------------------------------------------------------------------------
# Trade exports
# ---------------------------------------------------------------------------


@mcp.tool
def list_trade_accounts() -> dict:
    """Cuentas con CSVs bajo TradeExports/."""
    return trade_exports.list_accounts()


@mcp.tool
def list_trade_files(account: str) -> dict:
    """CSVs de trades en la carpeta de una cuenta."""
    return trade_exports.list_trade_files(account=account)


@mcp.tool
def read_trades(
    account: str,
    csv_file: str,
    limit: int = 100,
    tail: bool = True,
) -> dict:
    """Lee filas de un CSV de trades. Por default últimas ``limit`` filas."""
    return trade_exports.read_trades(
        account=account, csv_file=csv_file, limit=limit, tail=tail
    )


@mcp.tool
def trade_stats(
    account: str,
    csv_file: str,
    group_by: str | None = "Quality",
) -> dict:
    """Win rate, PnL total y por grupo (Quality/Direction/ExitReason) de un CSV."""
    return trade_exports.compute_stats(
        account=account, csv_file=csv_file, group_by=group_by
    )


# ---------------------------------------------------------------------------
# Observer (AddOn HTTP en NT — Fase 1b: bars/quote/ticks vivos)
# ---------------------------------------------------------------------------


@mcp.tool
def observer_health() -> dict:
    """Estado del AddOn RelativeObserver en NT (localhost:7891).

    Si devuelve ``addon_reachable=False``, el AddOn no está cargado o NT8 está
    cerrado. En ese caso las tools de datos vivos fallarán — usar las
    file-based (``vwap_snapshot``, ``tail_nt_log``, etc.).
    """
    return observer.health()


@mcp.tool
def observer_list_subscriptions() -> dict:
    """Instrumentos actualmente suscritos a market data en el AddOn."""
    return observer.list_subscriptions()


@mcp.tool
def observer_subscribe(instrument: str) -> dict:
    """Suscribe market data para un instrumento (ej. ``MES 12-26``, ``MES``).

    Idempotente. Las suscripciones sobreviven mientras NT esté abierto.
    """
    return observer.subscribe(instrument=instrument)


@mcp.tool
def observer_unsubscribe(instrument: str) -> dict:
    """Cancela la suscripción de market data para un instrumento."""
    return observer.unsubscribe(instrument=instrument)


@mcp.tool
def get_quote(instrument: str) -> dict:
    """Cotización en vivo: last / bid / ask / volumen / hora del último tick.

    Auto-suscribe si es la primera vez. Primeras llamadas pueden devolver NaN
    hasta que llegue el primer tick del feed.
    """
    return observer.get_quote(instrument=instrument)


@mcp.tool
def get_ticks(instrument: str, n: int = 200) -> dict:
    """Últimos ``n`` ticks del buffer circular (máx 5000, default 200).

    Incluye Last/Bid/Ask en orden cronológico con precio, volumen y timestamp.
    Útil para analizar microestructura reciente, order flow, absorción.
    """
    return observer.get_ticks(instrument=instrument, n=n)


@mcp.tool
def list_accounts() -> dict:
    """Cuentas NT conectadas con cash / buying power / PnL realizado y no realizado.

    Incluye conteos de posiciones abiertas y órdenes activas por cuenta.
    """
    return observer.list_accounts()


@mcp.tool
def list_positions(account: str | None = None, include_flat: bool = False) -> dict:
    """Posiciones abiertas (Long/Short) con avg price + unrealized PnL.

    ``account`` opcional para filtrar por cuenta. Por default excluye Flat.
    """
    return observer.list_positions(account=account, include_flat=include_flat)


@mcp.tool
def list_orders(account: str | None = None, state: str = "active") -> dict:
    """Órdenes: stops, limits, trailing, OCO, etc.

    ``state``: ``active`` (default — working/accepted/submitted/pending),
    ``filled``, ``all``.
    """
    return observer.list_orders(account=account, state=state)


@mcp.tool
def list_charts() -> dict:
    """Charts y SuperDOMs abiertos con instrumento, período, indicadores cargados.

    Útil para responder "¿qué tengo abierto en NT?". Usa reflection para
    extraer info del ChartControl — puede devolver campos vacíos si el layout
    WPF cambió entre versiones.
    """
    return observer.list_charts()


@mcp.tool
def list_executions(account: str | None = None, n: int = 50, since_hours: int = 0) -> dict:
    """Ejecuciones (fills) individuales: hora, precio, qty, comisión, order_id.

    ``since_hours`` > 0 filtra a las últimas N horas. Máx 500 registros.
    """
    return observer.list_executions(account=account, n=n, since_hours=since_hours)


@mcp.tool
def list_completed_trades(account: str | None = None, n: int = 50) -> dict:
    """Trades cerrados (entry+exit emparejados) con PnL, MAE, MFE.

    Fuente: ``Account.SystemPerformance.AllTrades`` (mismo motor que el
    Performance Explorer de NT). Incluye duración, precios de entry/exit,
    dirección, profit en currency/points/ticks.
    """
    return observer.list_completed_trades(account=account, n=n)


@mcp.tool
def list_indicator_states() -> dict:
    """Todos los estados publicados por indicadores en RelativeIndicatorRegistry.

    Cada indicador de la suite puede llamar
    ``RelativeIndicatorRegistry.Publish(key, payload)`` en su OnBarUpdate para
    exponer valores runtime (VWAP actual, deltas, señales, etc.) sin escribir
    a disco. Útil para *"¿qué valor tiene RelativeDelta en MES 1m ahora?"*.
    """
    return observer.list_indicator_states()


@mcp.tool
def get_indicator_state(key: str) -> dict:
    """Estado de un indicador específico por clave.

    Convención sugerida de key:
    ``"{IndicatorName}:{Instrument.FullName}:{BarsPeriod.Value}{BarsPeriodType}"``
    """
    return observer.get_indicator_state(key=key)


@mcp.tool
def get_print_output(
    n: int = 200,
    indicator: str | None = None,
    instrument: str | None = None,
    level_min: int = 1,
    since_minutes: int = 0,
) -> dict:
    """Últimas ``n`` líneas del buffer estructurado RelativeLog.

    Los indicadores que usen ``this.RLog(...)`` van a parecer acá con metadata
    automática (timestamp, nivel, indicador, instrumento, período, bar_time,
    CurrentBar, mensaje). Filtros opcionales:

    - ``indicator``: nombre exacto (ej. ``RelativeVwap``)
    - ``instrument``: FullName (ej. ``MES 06-26``)
    - ``level_min``: 1=Info, 2=Warning, 3=Error (default 1)
    - ``since_minutes``: últimos N minutos
    """
    return observer.get_print_output(
        n=n, indicator=indicator, instrument=instrument,
        level_min=level_min, since_minutes=since_minutes,
    )


@mcp.tool
def clear_print_output() -> dict:
    """Vacía el buffer de RelativeLog (no resetea total_count monotónico)."""
    return observer.clear_print_output()


# ---------------------------------------------------------------------------
# NADRO — tools compuestas que aplican la metodología del usuario
# ---------------------------------------------------------------------------


@mcp.tool
def nadro_snapshot(instrument: str, tf_ritmo: str = "1m", n_bars: int = 20) -> dict:
    """Brief NADRO completo aplicado al estado vivo del mercado.

    Aplica el acrónimo N-A-D-R-O (Narrativa, Aceptación, DVA, Ritmo, Order Flow)
    cruzando los 9 indicadores publicados en el Registry + bars del AddOn.

    Devuelve:
    - ``narrativa``: bias macro/micro, confluence vs dissonance, resumen textual
    - ``distribucion``: régimen (rotacional / imbalance) y táctica sugerida
    - ``ritmo``: rotaciones dinámicas de las últimas ``n_bars`` del timeframe ``tf_ritmo``
    - ``order_flow``: cumulative delta + clasificación + divergencia
    - ``lineas_arena``: top 12 niveles ordenados por proximidad al precio
    - ``confluences``: zonas con 2+ niveles agrupados (tolerancia 8 ticks)
    - ``hypos``: 3 escenarios if-then para pre-market
    - ``setup_candidato``: calidad A+/B/C con justificación

    Requiere que los indicadores estén publicando (ver ``list_indicator_states``).
    """
    return nadro_tool.nadro_snapshot(instrument=instrument, tf_ritmo=tf_ritmo, n_bars=n_bars)


@mcp.tool
def nadro_classify_setup(
    instrument: str,
    direction: str,
    entry: float,
    target: float,
    stop: float,
    size: int = 1,
) -> dict:
    """Evalúa un setup hipotético contra las Leyes NADRO.

    Devuelve calidad A+/A/B/C + cumplimiento de leyes + alineación con régimen
    actual + recomendación concreta (tomar / cautela / no operar).

    ``direction``: ``"long"`` o ``"short"``. Geometría validada automáticamente.
    """
    return nadro_tool.nadro_classify_setup(
        instrument=instrument, direction=direction,
        entry=entry, target=target, stop=stop, size=size,
    )


@mcp.tool
def nadro_backtest(
    instrument: str,
    days_back: int = 7,
    tf: str = "5m",
    window_start: str = "09:30",
    window_end: str = "11:00",
    stop_pts: float = 5.0,
    max_hold_bars: int = 20,
) -> dict:
    """Backtest MVP NADRO — setups BPB en los últimos N días.

    Recalcula VWAP + bandas SD desde bars crudos para cada día histórico.
    Detecta breakouts del DVAH/DVAL con volumen > 1.5× promedio. Simula
    entry al retest del nivel, stop fijo, target = banda SD3.

    Devuelve:
    - ``stats``: win_rate, profit_factor, expectancy, max DD, PnL total
    - ``daily_breakdown``: PnL por día
    - ``trades``: detalle de cada trade
    - ``pnl_curve`` dentro de stats: curva cumulativa

    Default MVP: 7 días, 5m bars, ventana 09:30-11:00 ET, stop 5pts, hold 20 bars.
    """
    return backtest_tool.nadro_backtest(
        instrument=instrument,
        days_back=days_back,
        tf=tf,
        window_start=window_start,
        window_end=window_end,
        stop_pts=stop_pts,
        max_hold_bars=max_hold_bars,
    )


@mcp.tool
def nadro_backtest_with_charts(
    instrument: str,
    days_back: int = 30,
    tf: str = "15m",
    window_start: str = "07:00",
    window_end: str = "23:00",
) -> dict:
    """Corre el backtest NADRO y genera 4 gráficos PNG automáticamente.

    Produce:
    - ``equity_curve.png``: PnL acumulado total + por setup superpuestos
    - ``by_setup.png``: 4 subplots, uno por setup (BPB/RPB/IPB/EF) con W/L markers
    - ``daily_pnl.png``: barras diarias verde/rojo
    - ``distribution.png``: histograma de PnL por trade por setup

    Archivos en ``RelativeMCP_Server/reports/``. Returns dict con las rutas.
    """
    return backtest_charts_tool.nadro_backtest_with_charts(
        instrument=instrument,
        days_back=days_back,
        tf=tf,
        window_start=window_start,
        window_end=window_end,
    )


@mcp.tool
def check_compile_status() -> dict:
    """Verifica si NT8 recompiló recientemente y si fue exitoso.

    Cruza DLL mtime + AddOn uptime para detectar F7 exitoso vs fallido.
    Útil para confirmar compilación sin preguntar al usuario.
    """
    return compile_tool.check_compile_status()


@mcp.tool
def nadro_detect_fresh_shift(instrument: str, tf: str = "1m", n_bars: int = 200) -> dict:
    """Detecta el último Fresh Condition Shift (balance ↔ imbalance).

    Recorre las últimas ``n_bars`` comparando cada close contra el DVA Weekly
    (fallback Daily). Marca la última transición de régimen:

    - ``balance`` → close dentro del DVA (±10% de tolerancia del ancho)
    - ``imbalance_up`` → close arriba del DVAH + tolerancia
    - ``imbalance_down`` → close debajo del DVAL - tolerancia

    Útil para determinar cuánta "energía fresca" tiene el régimen actual
    (Ley 8 NADRO). Fresh shift reciente = máxima convicción para BPB/RPB.
    """
    return nadro_tool.nadro_detect_fresh_shift(instrument=instrument, tf=tf, n_bars=n_bars)


@mcp.tool
def get_bars(instrument: str, tf: str = "1m", n: int = 50) -> dict:
    """Últimas ``n`` barras OHLCV en el timeframe indicado.

    ``tf``: sufijos ``s/m/h/d`` (tiempo), ``t`` (ticks), ``v`` (volumen), ``r`` (rango).
    Ejemplos: ``1m``, ``5m``, ``15m``, ``1h``, ``1d``, ``1000t``, ``5000v``.
    Máximo 2000 barras. Latencia típica 1-15s (BarsRequest es asíncrono).
    """
    return observer.get_bars(instrument=instrument, tf=tf, n=n)


# ---------------------------------------------------------------------------
# Resources estáticos (rutas bajo las que NT escribe archivos)
# ---------------------------------------------------------------------------


@mcp.resource("nt://paths")
def resource_paths() -> str:
    """Devuelve las rutas resueltas que usa el servidor."""
    return (
        f"NT_HOME={nt_home()}\n"
        f"LOGS={logs_dir()}\n"
        f"TRACE={trace_dir()}\n"
        f"VWAP_LEVELS={vwap_levels_dir()}\n"
        f"TRADE_EXPORTS={trade_exports_dir()}\n"
    )


if __name__ == "__main__":
    mcp.run()
