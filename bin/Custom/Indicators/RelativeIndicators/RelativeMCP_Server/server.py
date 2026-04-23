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
from .tools import briefing as briefing_tool
from .tools import markup as markup_tool
from .tools import walkforward as walkforward_tool
from .tools import eod_review as eod_review_tool
from .tools import nightly_report as nightly_report_tool
from .tools import nadro as nadro_tool
from .tools import observer
from .tools import tpo_cva as tpo_cva_tool
from .tools import vwap_levels as vwap
from .tools import delta_history as delta_history_tool
from .tools import vwap_confluence_backtest as vwap_confluence_tool


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


# ---------------------------------------------------------------------------
# Delta history (Apteros backtest)
# ---------------------------------------------------------------------------


@mcp.tool
def list_delta_history() -> dict:
    """Archivos ``DeltaHistory/{INSTRUMENT}_{YYYY-MM-DD}.jsonl`` disponibles
    exportados por RelativeDelta. Agrupados por instrumento.
    """
    return delta_history_tool.list_delta_history_files()


@mcp.tool
def read_delta_history(
    instrument: str,
    date_start: str | None = None,
    date_end: str | None = None,
    sample_every: int = 1,
) -> dict:
    """Lee bars históricas del cumulative delta para un instrumento + rango.

    Cada bar cerrado tiene cdOpen/cdHigh/cdLow/cdClose + bar_delta + anchors
    por sesión (us/eu/asia/global). El `global` es el reset ETH (exchange reset
    17:00 ET) — referente canónico Apteros.

    Args:
        instrument: master symbol (MNQ, MES, etc.)
        date_start/date_end: YYYY-MM-DD. None = sin límite.
        sample_every: 1=todas las bars, N=cada N-ésima (reduce payload).
    """
    return delta_history_tool.read_delta_history(
        instrument=instrument,
        date_start=date_start,
        date_end=date_end,
        sample_every=sample_every,
    )


@mcp.tool
def delta_neutralization_scan(
    instrument: str,
    date_start: str | None = None,
    date_end: str | None = None,
    session: str = "global",
) -> dict:
    """Detecta cruces del cumulative delta por zero line (fresh delta
    neutralization candidatas Apteros) en el rango dado.

    `session`: "global" (ETH reset — canónico Apteros), "us", "eu", "asia".

    Devuelve lista de eventos con timestamp, precio, dirección del cruce,
    bars desde último cruce, flag fresh (≥10 bars desde cruce previo).
    """
    return delta_history_tool.delta_neutralization_scan(
        instrument=instrument,
        date_start=date_start,
        date_end=date_end,
        session=session,
    )


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
def nadro_snapshot_markup(
    instrument: str,
    price_at_analysis: float,
    regime: str = "",
    bias: str = "",
    summary: str = "",
    analysis_text: str = "",
    confluences: list | None = None,
    levels: list | None = None,
    hypos: list | None = None,
    timestamp: str | None = None,
    snapshot_id: str | None = None,
) -> dict:
    """Persiste un snapshot NADRO en JSON que el indicador RelativeNadroMarkup lee.

    Escribe en ``Docs/Nadro/markups/{INSTRUMENT_MASTER}_YYYY-MM-DD.json``. Si el
    archivo existe hace APPEND al array ``snapshots``; si el ``snapshot_id`` ya
    existe lo sobrescribe.

    Parámetros:
      ``instrument``: "MGC 06-26" o "MGC" (se normaliza a master symbol).
      ``price_at_analysis``: precio al momento del snapshot.
      ``regime``: ej. "imbalance bearish", "rotacional", "balance".
      ``bias``: "bullish" | "bearish" | "neutral".
      ``summary``: línea única del análisis (header del chart).
      ``analysis_text``: texto completo multilínea para el panel izquierdo.
      ``confluences``: ``[{label, price_min, price_max, grade, members: []}]``.
      ``levels``: ``[{label, price}]``.
      ``hypos``: ``[{id, direction, setup_type, entry, stop, grade, notes,
                    targets: [{label, price, rr}], outcome: {...}}]``.
      ``timestamp``: ISO ``2026-04-21T09:05:00`` (default: now).
      ``snapshot_id``: default ``{MASTER}_{YYYYMMDD_HHMM}``.

    Auto-calcula ``risk_pts = |entry - stop|`` y ``rr`` por target si faltan.
    Inicializa ``outcome.status = "pending"`` si no viene.

    Returns: path escrito, acción (created/appended/updated), total snapshots.
    """
    return markup_tool.save_snapshot(
        instrument=instrument,
        price_at_analysis=price_at_analysis,
        regime=regime,
        bias=bias,
        summary=summary,
        analysis_text=analysis_text,
        confluences=confluences,
        levels=levels,
        hypos=hypos,
        timestamp=timestamp,
        snapshot_id=snapshot_id,
    )


@mcp.tool
def nadro_markup_close_eod(date: str | None = None, instrument: str | None = None) -> dict:
    """Walk-forward sobre bars 1m del día para actualizar outcomes de hipótesis.

    Para cada snapshot del día, recorre bars desde `timestamp` hasta ahora
    (o fin del día si ya cerró), y determina para cada hipo:

    - `status`: pending/triggered/filled/stopped_out/not_triggered
    - `triggered_at`, `stop_hit_at`: timestamps de eventos
    - `targets_hit`: índices de targets alcanzados
    - `mae_pts`, `mfe_pts`: max adverse / favorable excursion desde entry

    Tras correr esto, las flechas del indicador `RelativeNadroMarkup` en
    NT8 cambian color automáticamente (pending→verde/rojo según outcome).

    ``date``: YYYY-MM-DD (default: hoy).
    ``instrument``: master symbol opcional (ej "MGC"). Si se omite, procesa todos.

    Returns: stats + status_changes por archivo procesado.
    """
    return walkforward_tool.close_eod(date_str=date, instrument=instrument)


@mcp.tool
def nadro_eod_review(instrument: str, date: str | None = None) -> dict:
    """Genera el review narrativo EOD de un instrumento para un día.

    Es el ANÁLISIS post-cierre del snapshot que se tomó al pit open.
    NO genera hipos nuevos — solo narra lo que pasó:

    - Hipos propuestos al pit open
    - Outcome de cada uno (filled / stopped / not_triggered)
    - MAE/MFE por hipo
    - STOP TIGHT detection (stopped_out pero setup_reached_t1+)
    - Clasificación: WIN / WIN_MINOR / STOP_TIGHT / STOP_GENUINE / DEAD
    - Placeholder "Aprendizajes" para completar manual

    ``instrument``: master symbol (MGC, MCL, MES, MNQ, MYM, M2K).
    ``date``: YYYY-MM-DD (default: hoy).

    Guarda el markdown en ``Docs/Nadro/eod_reviews/{INST}_{DATE}.md``.
    """
    return eod_review_tool.eod_review(instrument=instrument, date_str=date)


@mcp.tool
def nadro_eod_review_all(date: str | None = None) -> dict:
    """Corre nadro_eod_review para los 6 instrumentos NADRO estándar.

    Devuelve reviews individuales + agregado (total hipos, win rate, stop tights).
    Cada review queda persistido en disco como markdown.
    """
    return eod_review_tool.eod_review_all(date_str=date)


@mcp.tool
def nadro_nightly_report(instrument: str, date: str | None = None) -> dict:
    """**NADRO Nightly Report** — review EOD completo bajo metodología oficial.

    Reemplazo recomendado de ``nadro_eod_review`` (que se mantiene como alias
    mínimo). Estructura inspirada en transcripts oficiales NADRO 02-22 +
    livestreams 24-28. Plantilla en ``Docs/Nadro/nightly_report_template.md``.

    Secciones generadas:
    - Preparación pre-open (snapshot del día)
    - Walk-forward de hipos (filled/stopped/dead + MAE/MFE)
    - **MISSED SETUPS** — niveles fuera del snapshot que dispararon (algoritmo
      detecta touch ±3 ticks + reversal con MFE >= 0.10% precio + MAE/MFE < 0.6).
      Clasificación BPB/RPB/IPB automática
    - Review N-A-D-R-O en orden:
      * Narrativa: ¿bias se cumplió?
      * Aceptación: niveles rejected (wick) vs accepted (close cerca)
      * DVA/Distribución: POC/VAH/VAL del pit + skew
      * Ritmo: compresión → expansión detectada
      * Order Flow: delta proxy vs precio (alignment / divergence)
    - Disonancia narrativa (si hipos LONG y SHORT coexisten)
    - Lecciones auto-generadas (3-5)
    - Sugerencia hipo #1 mañana

    Guarda markdown en ``Docs/Nadro/nightly_reports/{INST}_{DATE}.md``.

    ``instrument``: master symbol (MGC, MCL, MES, MNQ, MYM, M2K).
    ``date``: YYYY-MM-DD (default: hoy).
    """
    return nightly_report_tool.generate_nightly_report(instrument=instrument, date_str=date)


@mcp.tool
def nadro_nightly_report_all(date: str | None = None) -> dict:
    """Genera nightly report NADRO para los 6 instrumentos + HTML consolidado.

    HTML output: ``Docs/Nadro/nightly_reports/nightly_all_{date}.html``.
    Reemplaza al ``eod_all_{date}.html`` del review clásico (que sigue
    funcionando como alias).
    """
    return nightly_report_tool.generate_nightly_all(date_str=date)


@mcp.tool
def nadro_daily_briefing(date: str | None = None) -> dict:
    """Genera un reporte HTML ranqueado con todos los snapshots NADRO del día.

    Lee `Docs/Nadro/markups/*_{date}.json`, calcula score por hipótesis y
    produce `reports/briefing_{date}.html` con:
    - Hero: best setup del día
    - Cards top #2-#4
    - Panorama macro (bullish/bearish count)
    - Tabla completa ranqueada
    - Detalle por instrumento (acordeón colapsable)

    Score factors: Grade (A+++=5, A=3, B=2), RR T1 (>5=+3, >3=+2),
    confluencias (5+mb=+3, 3+mb=+2), Ley 10 (+4), bias alineado (+1),
    dissonance (-1), entry cerca (+1).

    ``date``: YYYY-MM-DD (default: hoy).
    Returns path + stats (total instruments, hypos, top_setup info).
    """
    return briefing_tool.generate(date)


@mcp.tool
def nadro_markup_list(instrument: str | None = None, date: str | None = None) -> dict:
    """Lista markups NADRO guardados. Filtro opcional por instrument y/o date.

    ``instrument`` se normaliza a master symbol (ej "MGC 06-26" → "MGC").
    ``date`` formato ``YYYY-MM-DD``. Útil para saber qué análisis se han hecho
    y con qué IDs, antes de hacer walk-forward de outcomes.
    """
    return markup_tool.list_snapshots(instrument=instrument, date=date)


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
    stop_mode: str = "dynamic",
    stop_pts: float = 5.0,
    stop_atr_mult: float = 1.5,
    dva_width_pct: float = 0.25,
    min_stop_pts: float = 3.0,
    max_stop_pts: float = 15.0,
    max_hold_bars: int = 20,
) -> dict:
    """Backtest NADRO 4 setups en los últimos N días.

    Modos de stop:
    - ``dynamic`` (default): ATR × stop_atr_mult, piso 4pts.
    - ``dva_width``: NADRO — stop anclado al nivel ± dva_width_pct × ancho DVA.
      BPB/RPB usan ancho pDVA (día anterior); IPB usa ancho DVA developing;
      EF usa ancho DVA central aplicado al extremo ±2SD.
      Piso ``min_stop_pts``, techo ``max_stop_pts``.
    - ``fixed``: stop_pts fijo en puntos.

    Devuelve stats, daily_breakdown, trades (con ``dva_width_info`` si aplica).
    """
    return backtest_tool.nadro_backtest(
        instrument=instrument,
        days_back=days_back,
        tf=tf,
        window_start=window_start,
        window_end=window_end,
        stop_mode=stop_mode,
        stop_pts=stop_pts,
        stop_atr_mult=stop_atr_mult,
        dva_width_pct=dva_width_pct,
        min_stop_pts=min_stop_pts,
        max_stop_pts=max_stop_pts,
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
def vwap_confluence_backtest(
    instrument: str = "MES 06-26",
    days_back: int = 365,
    tf: str = "1m",
    eth_reset_hour: int = 18,
    rth_start: str = "09:30",
    rth_end: str = "16:00",
    window_start: str = "09:30",
    window_end: str = "15:00",
    confluence_tolerance_ticks: int = 1,
    bands_to_use: list | None = None,
    signal2_threshold_ticks: int = 1,
    touch_wick_ticks_inside: int = 1,
    anchor_mode: str = "TOUCH",
    close_at_rth_end: bool = True,
    cancel_on_central_cross: bool = True,
    tick_size: float = 0.25,
    point_value: float = 5.0,
) -> dict:
    """**Dual-Anchor VWAP Confluence Fade** — backtest baseline CRUDO (sin filtro).

    Estrategia:
    - 2 VWAPs dinámicos en paralelo: **ETH** (reset 18:00 ET) + **RTH** (09:30-16:00)
    - Bandas SDn up/dn por cada VWAP
    - Zona de operación: confluencia ETH∩RTH entre bandas ``bands_to_use``
      (default ``["SD2","SD3"]``) dentro de ``confluence_tolerance_ticks``
    - Máquina: IDLE → (wick ≥ ``touch_wick_ticks_inside`` en confluencia)
      → ARMED → (Signal 2: close - anchored_vwap ≥ ``signal2_threshold_ticks``)
      → IN_TRADE → (target dinámico / stop / EOD)

    Anchor modes:
    - ``TOUCH`` (default): VWAP anclado en bar del toque, re-ancla ante nuevo extremo
    - ``SESSION_EXTREME``: usa el low/high absoluto de la sesión ETH

    Entry: close del bar Signal 2.
    Stop: Low[anchor_final]-1tick (long) / High[anchor_final]+1tick (short).
    Target: (1) confluencia opuesta activa si el bar la toca, (2) fallback SD2 ETH opuesto.

    Cancelación (si ``cancel_on_central_cross``): durante ARMED, close cruza VWAP
    central (ETH o RTH) en contra del fade → vuelta a IDLE.

    Re-arm automático tras TP/SL sin límite de trades/día. Cierre forzado al
    ``rth_end`` si ``close_at_rth_end`` y el trade sigue abierto.

    **Cobertura**: ``observer.get_bars`` cap 10000 bars/request. Aproximado:
    ``1m`` → ~7 días | ``5m`` → ~36 días | ``15m`` → ~108 días | ``1h`` → ~434 días.
    Output reporta ``bars_received`` y ``effective_days_covered`` para transparencia.

    Args:
        instrument: FullName (``"MES 06-26"``) o master symbol.
        days_back: días hacia atrás a filtrar (tras fetch).
        tf: timeframe (``"1m"``, ``"5m"``, etc.). Default 1m para granularidad máxima.
        eth_reset_hour: hora local reset sesión ETH (default 18).
        rth_start/rth_end: ventana del VWAP RTH ``HH:MM`` (default 09:30-16:00).
        window_start/window_end: ventana donde armar/disparar setups (default 09:30-15:00).
        confluence_tolerance_ticks: tolerancia para confluencia ETH-RTH (default 1 tick).
        bands_to_use: lista de bandas a considerar (default ``["SD2","SD3"]``).
        signal2_threshold_ticks: ticks de despegue vs anchored VWAP para disparar (default 1).
        touch_wick_ticks_inside: penetración mínima en ticks para registrar toque (default 1).
        anchor_mode: ``"TOUCH"`` (default) o ``"SESSION_EXTREME"``.
        close_at_rth_end: si True, cierra trade abierto al rth_end (default True).
        cancel_on_central_cross: cancela ARMED si close cruza VWAP central (default True).
        tick_size: tamaño de tick del instrumento (default 0.25 = MES/MNQ).
        point_value: USD por punto (default 5.0 = MES).

    Returns: dict con ``config``, ``bars_analyzed``, ``setups_armed_total/cancelled/triggered``,
    ``stats`` globales, ``stats_by_direction`` (long/short), ``stats_by_exit_reason``
    (target/stop/time_out_rth), ``daily_breakdown``, y lista completa de ``trades``
    con state_transitions + MFE/MAE por trade.
    """
    return vwap_confluence_tool.vwap_confluence_backtest(
        instrument=instrument,
        days_back=days_back,
        tf=tf,
        eth_reset_hour=eth_reset_hour,
        rth_start=rth_start,
        rth_end=rth_end,
        window_start=window_start,
        window_end=window_end,
        confluence_tolerance_ticks=confluence_tolerance_ticks,
        bands_to_use=bands_to_use,
        signal2_threshold_ticks=signal2_threshold_ticks,
        touch_wick_ticks_inside=touch_wick_ticks_inside,
        anchor_mode=anchor_mode,
        close_at_rth_end=close_at_rth_end,
        cancel_on_central_cross=cancel_on_central_cross,
        tick_size=tick_size,
        point_value=point_value,
    )


@mcp.tool
def get_cvas(
    instrument: str,
    weeks_back: int = 4,
    days_back: int | None = None,
    overlap_threshold: float = 0.50,
    reset_hour: int = 17,
    session: str = "rth",
) -> dict:
    """Reconstruye pVAs + CVAs NADRO con fusión forward automática.

    **Terminología NADRO estricta**:
    - **pVA** (Prior Value Area) = 1 día individual (el del día actual es pVA activa).
    - **CVA** (Composite Value Area) = 2+ días fusionados en equilibrio sostenido.

    **Carga por semanas completas**: desde el LUNES de hace `weeks_back` semanas
    hasta hoy. Esto evita cortar un CVA en medio por ventana arbitraria.

    Reglas NADRO 05-2 Market Framework:
    - Construcción forward-only (builds derecha, nunca mirar atrás)
    - Overlap VA ≥ overlap_threshold entre días consecutivos → fusionar (pasa a CVA)
    - Cambio de condición (breakout ± tolerancia 0.5 pts) → cerrar bloque
    - Bloques cerrados dejan "línea secundaria" en el borde roto

    Args:
        instrument: "MES 06-26"
        weeks_back: semanas completas hacia atrás (default 4)
        days_back: días absolutos (legacy, anula weeks_back si se pasa)
        overlap_threshold: fracción mínima overlap VA para fusionar (default 0.50)
        reset_hour: hora local reset ETH (default 17 = 17:00 local)
        session: "rth" (default NADRO) | "eth" | "pit_cl"

    Returns dict con: pvas, cvas, secondary_lines, profiles_by_day, cutoff_date.
    """
    return tpo_cva_tool.get_cvas(
        instrument=instrument,
        weeks_back=weeks_back,
        days_back=days_back,
        overlap_threshold=overlap_threshold,
        reset_hour=reset_hour,
        session=session,
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
    Máximo 10000 barras. Latencia típica 1-15s (BarsRequest es asíncrono).
    """
    return observer.get_bars(instrument=instrument, tf=tf, n=n)


@mcp.tool
def get_bars_with_ha(instrument: str, tf: str = "1m", n: int = 50) -> dict:
    """Últimas ``n`` barras OHLCV + Heikin Ashi calculado.

    Cada bar incluye además de OHLCV: hao, hac, hah, hal, ha_color (BULL/BEAR),
    ha_change (True si la bar cambió color HA respecto a la previa).

    Útil para aplicar reglas NADRO sobre HA (Goldilocks, cambio de color al
    cierre). Usuario típicamente opera con HA en el chart.

    Fórmula:
        HA_close = (O+H+L+C)/4
        HA_open  = (HA_open[prev] + HA_close[prev]) / 2
        HA_high  = max(H, HA_open, HA_close)
        HA_low   = min(L, HA_open, HA_close)
    """
    return observer.get_bars_with_ha(instrument=instrument, tf=tf, n=n)


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
