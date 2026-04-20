# RelativeMCP — CHANGELOG

Integración MCP (Model Context Protocol) entre Claude Code y NinjaTrader 8.

Arquitectura de tres procesos:
- `RelativeMCP_Server/` — servidor Python/FastMCP (stdio) que Claude Code spawnea
- `RelativeObserver.cs` — AddOn NinjaScript con HttpListener en `localhost:7891`
- `RelativeNewsSquawk_Server/` — servidor Flask existente (no modificado en esta fase)

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
