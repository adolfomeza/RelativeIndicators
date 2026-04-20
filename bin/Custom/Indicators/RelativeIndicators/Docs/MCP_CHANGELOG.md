# RelativeMCP — CHANGELOG

Integración MCP (Model Context Protocol) entre Claude Code y NinjaTrader 8.

Arquitectura de tres procesos:
- `RelativeMCP_Server/` — servidor Python/FastMCP (stdio) que Claude Code spawnea
- `RelativeObserver.cs` — AddOn NinjaScript con HttpListener en `localhost:7891`
- `RelativeNewsSquawk_Server/` — servidor Flask existente (no modificado en esta fase)

## [0.5.0] — 2026-04-20

Backtest NADRO con los 4 setups + visualización PNG + ventana configurable.

### Nueva tool: `nadro_backtest`
- 4 setups NADRO detectables: **BPB, RPB, IPB, EF**
- Niveles históricos calculados desde bars crudos (VWAP + SD bands por sesión ETH)
- **Prior Day High/Low + PVA** sobre RTH (9:30-16:00) del último día hábil
- Filtro freshness Ley 8 (skip PDH/PDL > 3 días antigüedad)
- Stops dinámicos ATR-based (piso 4pts, sin cap artificial)
- Targets: RR 1:2 o SD3 (lo más lejos)
- Confirmation bars para RPB/IPB/EF (reduce falsos positivos)
- Regime filter para EF (no fade contra imbalance sostenido)
- Stats globales + por setup + daily breakdown + curva PnL

### Nueva tool: `nadro_backtest_with_charts`
Genera 4 PNG en `RelativeMCP_Server/reports/`:
- `equity_curve.png` — PnL acumulado total + por setup superpuestos
- `by_setup.png` — 4 subplots (uno por setup) con W/L markers
- `daily_pnl.png` — barras diarias verde/rojo
- `distribution.png` — histograma PnL por trade

### Resultados validados (MES 06-26, 30 días, 7am-23pm)
```
Trades: 38     WR: 44.7%     PnL: +$590.55     PF: 1.43     MaxDD: $632

BPB:  7t  WR 71%  +$652.15  PF 5.18  ← EDGE INSTITUCIONAL VALIDADO
RPB:  7t  WR 43%  +$69.50   PF 1.29  ← Break-even positivo
EF:  22t  WR 41%  +$7.80    PF 1.01  ← Ruido sin Order Flow
IPB:  2t  WR  0%  -$138.90  PF 0.00  ← Sample insuficiente
```

**Conclusión**: BPB es el único setup con edge estadístico sin OF. RPB/EF/IPB
requieren `nadro_classify_setup` live con delta acumulado para filtrar.

### Lecciones NADRO (respeto total a la metodología)
- IPB debe ser pullback al **DVAH/DVAL (±1 SD)**, NUNCA al VWAP central
  ("Chop Zone" — Guía NADRO 4.0: *prioridad SIEMPRE los extremos*)
- Setups son raros por diseño (1 BPB/5 días, IPB/30 días) — coherente con
  Ejecución 3.0: *"la mayor parte del tiempo no hay oportunidad"*
- BPB usa **Prior Day RTH High/Low** como nivel estático, no developing DVA
- Stops dinámicos ATR sin techo artificial — piso 4pts anti-ruido
- Targets proporcionales al stop (RR 1:2) en vez de niveles fijos

### Tools totales: 37
Nuevas v0.5.0: `nadro_backtest`, `nadro_backtest_with_charts`.

## [0.4.0] — 2026-04-20

NADRO metodología aplicada en profundidad. Sprints 1-4 del refinamiento.
Cierre del stack antes de escalar a multi-instrumento (ScannerAddOn / Fase C).

### Sprint 1 — Fresh Shift + Recency + Targets en Hypos

**`_compute_freshness` / `_period_start`**:
- Calcula edad del período actual para cada TF NADRO (Y/Q/M/W/D).
- Score 0-1 lineal (1.0 = recién arrancado, 0.0 = próximo reset).
- Etiquetas: ``fresh`` (≥0.8), ``developing`` (≥0.4), ``matured`` (≥0.15), ``expired`` (<0.15).
- Aplicado a cada nivel en ``_generate_lineas_arena``.

**Hypos refactor a accionables**:
- Cada hypo ahora trae ``entry``, ``target``, ``invalidation``, ``rr_ratio``,
  ``risk_pts``, ``reward_pts``, ``risk_usd``, ``reward_usd``.
- H1 bullish: rupture del nivel arriba → target siguiente nivel / invalidación nivel abajo.
- H2 bearish: simétrico.
- H3 fade/rango o "sin setup claro — respetar inacción".

**Setup quality extendido a 5 puntos (antes 4)**:
- Incluye +1 por nivel cercano FRESH (Ley 8 NADRO: energía se disipa).
- Max score 5 → grading A+/A/B/C.

### Sprint 2 — Ritmo Zigzag + Compresión + Delta slope + Fresh Shift tool

**`_zigzag_swings` + `_analyze_ritmo` refactor** (NADRO 3.0):
- Detección de swings alto/bajo sobre lookback configurable.
- Filtro alternando H/L con reversal mínimo auto-calibrado al 75% percentile
  del bar range (adapta a volatilidad del instrumento sin parámetros fijos).
- Reporta n_swings, n_rotations, avg_rotation_pts, acceptance_distance (50%).

**`_detect_compresion`** (Ley 10 NADRO):
- Detecta cuando ≥ min_bars barras mantienen highs (o lows) dentro del
  acceptance_distance de un nivel extremo, sin rebote fuerte.
- Direcciones: ``bullish_reversal`` (sobre soporte) o ``bearish_reversal``
  (bajo resistencia).
- NADRO autoriza ANTICIPAR sólo en este caso.

**`_analyze_delta_slope`**:
- Compara pendiente del Cumulative Delta vs pendiente del precio en los
  últimos 500 ticks.
- Ángulos en grados con clasificación: ``coherent``, ``absorption_phase2``
  (delta >45° pero precio flat), ``strong_divergence``, ``moderate_divergence``.

**Nueva tool: `nadro_detect_fresh_shift(instrument, tf, n_bars)`**:
- Detecta la última transición balance↔imbalance_up/down usando el DVA Weekly.
- Acceptance = 10% del ancho del DVA.
- Reporta estado actual + último shift + timestamp + bars_ago + freshness_hint.

### Sprint 3 — Instrumentación RelativeTrend + RelativeNewsFilter

**`RelativeTrend` instrumentado**:
- Publica high_vwap, low_vwap, has_high_vwap, has_low_vwap, session anchors.

**`RelativeNewsFilter` instrumentado (gatekeeper NADRO 5.0 The Work)**:
- Publica próximo evento con minutos al evento, country, impact.
- Próximo High Impact con flag ``high_impact_within_30min``.
- Contadores events_next_hour, events_next_24h.

**Fix bug download (NewsFilter)**:
- ``_downloaded = true`` solo tras download exitoso (antes se seteaba antes
  del await → loop de "success" sin datos si fallaba).
- Retry 3 veces con backoff exponencial (2s, 4s, 6s).
- User-Agent explícito + timeout 15s.
- Detección de respuesta vacía (<100 bytes).
- Logs detallados con attempt number.

### Sprint 4 — Tools compuestas finales

**Nueva tool: `nadro_classify_setup(instrument, direction, entry, target, stop, size)`**:
- Evalúa un setup hipotético contra Leyes NADRO.
- Quality A+/A/B/C sobre 7 puntos:
  - RR ≥ 2:1
  - Referencia estructural en entry/stop
  - Alineación con macro bias
  - Alineación con régimen intraday
  - Target realista vs ritmo
  - Ley 10 Compresión alineada
  - Order Flow strong + coherente
- Reporta cumplimiento de Leyes 1, 3, 8, 10.
- Calcula proximidad de entry/stop/target a niveles estructurales.
- Recomendación clara: TOMAR / CAUTELA / NO OPERAR.

**Nueva tool: `check_compile_status()`**:
- Cruza mtime del NinjaTrader.Custom.dll con uptime del AddOn.
- Detecta F7 exitoso (DLL mtime ≈ AddOn uptime reciente).
- Detecta F7 fallido (AddOn restart reciente pero DLL no actualizado).
- Útil para automatizar verificación sin preguntar al user.

### Tools totales: 35
Nuevas desde v0.3.0: nadro_detect_fresh_shift, nadro_classify_setup,
check_compile_status. Plus: instrumentación de RelativeTrend y RelativeNewsFilter.

## [0.3.0] — 2026-04-20

Integración metodología NADRO + instrumentación de 7 indicadores clave + primera
tool compuesta. Core de trading observability completo.

### Instrumentación de indicadores (RLog + RelativeIndicatorRegistry)
- **`RelativeVwap.cs`**: publica close, vwapH/L, deltas por sesión
  (Global/Asia/Europe/USA), trend_mode, bearish, signals sig2H/sig2L.
- **5 forks VWAP por timeframe** (Annual/Quarterly/Monthly/Weekly/Daily):
  publican vwap, dvah_sd1, dval_sd1, dvah_sd3, dval_sd3, active_zones.
- **`RelativeVolumeProfile.cs`**: POC, VAH, VAL, poc_volume, level_count,
  total_profiles, profile_type, session_mode.
- **`RelativeDelta.cs`** (Order Flow — NADRO "O"): cumulative_delta, bar_delta,
  4 anchors de sesión US/EU/Asia/Global + estado activo.
- **`RelativeVwapLevels.cs`** (NADRO "N" narrativa consolidada): total_levels,
  active_confluences, armed_confluences, breached_confluences.

### Fixes & Lessons Learned
- Patrón `typeof(Indicator).Name` para identificador de clase (evita quirks
  runtime donde `Name` vacía → key vacía en Registry).
- `CaptureDelta` default `true` en RelativeVwap (para que los 4 deltas se
  calculen automáticamente).
- `RelativeVolumeProfile.IsSuspendedWhileInactive = false` (sin esto no publica
  cuando el chart no está activo).
- Publish en cada OnBarUpdate Realtime (sin gate `IsFirstTickOfBar`) — el gate
  solo limita el RLog al buffer, no el Registry. Evita holes durante charts de
  TF alto con `Calculate.OnPriceChange`.
- Fallback `GetType().Name` en `RelativeLog.cs` cuando `ns.Name` vacío.

### Nueva tool MCP compuesta: `nadro_snapshot(instrument)`
- Aplica acrónimo **N-A-D-R-O** en una sola llamada cruzando los 9 indicadores
  + bars del AddOn.
- Output estructurado:
  - **N** (Narrativa): bias macro/micro + confluence/dissonance entre TFs.
  - **A** (Aceptación): acceptance_distance = 50% del ritmo.
  - **D** (Distribución): régimen rotacional vs imbalance + táctica sugerida
    (fading vs IPB).
  - **R** (Ritmo): rotaciones dinámicas de las últimas N barras.
  - **O** (Order Flow): delta strength, dirección, sessions, divergence hints.
  - **Líneas en la arena**: top 12 ordenadas por proximidad.
  - **Confluences**: clusters con 2+ fuentes independientes.
  - **Hypos**: 3 escenarios if-then.
  - **Setup Quality A+/B/C** con score y razones.

### Regla de Segunda Gesta (NADRO 4.0)
- `_collapse_segunda_gesta()` — cuando LTWVs de TFs adyacentes coinciden
  (ej: D-DVAH = W-DVAH durante primer día de semana), el TF mayor se oculta
  y solo se muestra el más granular con `nadro_note` explicativa.
- Tolerance default 1 punto (configurable). Evita inflar confluencias
  artificialmente durante períodos jóvenes.
- No afecta fuentes independientes (TPO vs VWAP seguirán como confluencia
  real si coinciden).

### Memoria persistente
- `memory/nadro_metodologia.md` — resumen operativo del acrónimo, 10 leyes LMD,
  LTWVs, Market Profile, Ejecución 3.0, The Work, Order Flow. Claude aplica
  NADRO cuando analiza mercado en este repo.
- `memory/relative_mcp_server.md` actualizada con lecciones API NT8.

### Docs/Nadro/
- 5 guías maestras de la metodología (del NotebookLM del usuario):
  - 02 Market Framework
  - 03 Ejecución 3.0
  - 04 Long-Term VWAPs + Narrativa Avanzada (NADRO 4.0)
  - 05 The Work (NADRO 5.0)
  - 06 Order Flow

### Tools totales: 32
(previo: 31) + `nadro_snapshot`.

## [0.2.0] — 2026-04-20

Añade capa de live-data + trading observability + logging estructurado.
Compatible con la Fase 1a existente — tools file-based siguen funcionando
aunque NT8 esté cerrado.

### Fase 1b — AddOn HTTP bridge
- **`RelativeObserver.cs`**: AddOn NT8 que expone `HttpListener` en
  `http://localhost:7891/`. Arranca en `State.Active`, se detiene en
  `State.Terminated`. Thread-safe (lock de estado + `DataLock` por suscripción).
- Endpoints implementados:
  - `GET /health` — versión, uptime, connections NT, subs activas
  - `GET /subscriptions` — instrumentos con market data suscrita
  - `POST|DELETE /subscribe/{instrument}` — gestión de subs (idempotente)
  - `GET /quote/{instrument}` — last/bid/ask/last_volume/day_volume/tick_count/deduped_count
  - `GET /ticks/{instrument}?n=N` — buffer circular (5000 slots) con Last/Bid/Ask
  - `GET /bars/{instrument}?tf=1m&n=50` — OHLCV vía `BarsRequest` async
  - `GET /accounts` — todas las cuentas con cash / buying power / PnL
  - `GET /positions?account=X` — posiciones abiertas con unrealized PnL
  - `GET /orders?account=X&state=active|filled|all` — órdenes
  - `GET /executions?account=X&n=50&since_hours=24` — fills individuales
  - `GET /trades?account=X&n=50` — trades cerrados con PnL/MAE/MFE/duración
  - `GET /charts` — charts abiertos con instrumento, período, indicadores
  - `GET /indicator-state` / `GET /indicator-state/{key}` — registry
  - `GET /print-output?n=N&indicator=X&level_min=2` — logs estructurados
  - `DELETE /print-output` — vacía el buffer
- Timeframes soportados en `/bars`: sufijos `s/m/h/d` (tiempo), `t` (ticks),
  `v` (volumen), `r` (rango). `h` se multiplica x60 → Minute.

### Suscripción a market data
- Uso correcto de la API NT8: `instrument.MarketData.Update += handler`
  (no `Instrument.MarketData += handler` que es propiedad, no evento).
- Subscription via `instrument.Dispatcher.InvokeAsync(...)` para thread safety.
- Auto-suscribe al primer request de `/quote` o `/ticks`.
- **Dedupe**: descarta duplicados exactos por `(Time, Type, Price, Volume)` —
  ocurren cuando otros suscriptores (ej. chart abierto) disparan el evento
  múltiples veces. Ratio típico ~6.8%. Expuesto como `deduped_count` en `/quote`.

### Trades manuales via execution pairing
- `SystemPerformance.AllTrades` solo captura trades de estrategias
  automatizadas. Para ChartTrader / ATM / manual entries, el endpoint
  `/trades` hace fallback: agrupa `Account.Executions` por instrumento,
  ordena por tiempo, lleva net position firmada y empareja entry→exit
  cuando net vuelve a cero.
- Soporta pyramiding (acumula en entry, calcula avg weighted).
- No soporta flips directos (long→short en la misma ejecución) en MVP.
- Calcula PnL currency automáticamente vía `Instrument.MasterInstrument.PointValue`
  y ticks vía `TickSize`.
- Campo `source` diferencia `"system_performance"` vs `"executions_paired"`.

### `RelativeIndicatorRegistry`
- Archivo nuevo `RelativeIndicatorRegistry.cs`.
- `ConcurrentDictionary<string, IndicatorState>` con publish/get/snapshot/clear.
- Un indicador publica con:
  ```csharp
  RelativeIndicatorRegistry.Publish("RelativeVwap:MES 06-26:1Minute",
      new Dictionary<string, object> { ["vwap"] = 7155.3, ... });
  ```
- Consumible desde Claude vía `list_indicator_states` / `get_indicator_state(key)`.

### `RelativeLog` — logging estructurado opt-in
- Archivo nuevo `RelativeLog.cs`.
- Buffer circular de 2000 entries con metadata automática:
  timestamp UTC, indicador, instrumento, período, bar_time, CurrentBar, level.
- Extension methods: `this.RLog(fmt, args)`, `RLogW(...)`, `RLogE(...)`.
- Mirror automático al Output Window vía `Output.Process` (preserva flujo
  habitual de Print).
- Endpoint `/print-output?n=N&indicator=X&level_min=2&since_minutes=10`.
- NT8 no expone API pública para interceptar `Print()` de terceros —
  este es el workaround opt-in: los indicadores reemplazan `Print(msg)` por
  `this.RLog(msg)` donde quieran captura estructurada.

### Enumerar charts abiertos
- Uso de `NinjaTrader.Core.Globals.AllWindows` (no documentado) vía reflection.
- Cada `NTWindow` tiene su propio `Dispatcher` — marshalado con
  `Dispatcher.Invoke(...)` para acceder a `ActiveChartControl.Instrument /
  BarsPeriod / Indicators` sin romper thread safety.
- Query param `?all=true` muestra todas las ventanas sin filtrar (debug).

### MCP Python server — 31 tools expuestas
Distribución:
- File-based (14): `health`, `tail_nt_log`, `search_nt_log`,
  `list_indicator_traces`, `tail_indicator_trace`, `get_trace_today`,
  `list_vwap_instruments`, `read_vwap_levels`, `list_confluences`,
  `vwap_snapshot`, `list_trade_accounts`, `list_trade_files`, `read_trades`,
  `trade_stats`.
- Observer/live (17): `observer_health`, `observer_list_subscriptions`,
  `observer_subscribe`, `observer_unsubscribe`, `get_quote`, `get_ticks`,
  `get_bars`, `list_accounts`, `list_positions`, `list_orders`,
  `list_executions`, `list_completed_trades`, `list_charts`,
  `list_indicator_states`, `get_indicator_state`, `get_print_output`,
  `clear_print_output`.
- Tool adicional compartida: `health` (file-based paths).

### Tested & verified en sesión
- MES 06-26 cotizando en vivo con dedupe activo (14665 ticks, 997 deduped).
- Trade real DEMO619219 capturado: Long 1 @ 7154.50 → stop @ 7143 con slippage
  de 3 puntos; PnL -$57.50 calculado automáticamente vía execution pairing.
- 5 charts detectados con TFs Daily/4H/1H/15m/1m y stack de RelativeIndicators
  cargados en cada uno.
- 24 confluencias activas en MES leídas del archivo exportado
  `VwapLevels/Confluences_MES.txt`.

## [0.1.0] — 2026-04-20

### Fase 1a — MCP file-based (initial)
- `RelativeMCP_Server/` creado con FastMCP 3.2.4.
- Parsers para `VwapLevels/*.txt` (INI-like) y
  `VwapLevels/Confluences_{INSTRUMENT}.txt` (pipe-format con N miembros + 6
  campos fijos: PriceMin|PriceMax|StartTime|LastSeenTime|EndTime|flags).
- Parser para `TradeExports/{Account}/*.csv`.
- Parser para logs NT `log.YYYYMMDD.NNNNN.en.txt` con formato
  `timestamp|level|category|message`.
- `.mcp.json` en raíz scope project para registro automático.
- Resolución de rutas con override `NT_HOME` env var.
