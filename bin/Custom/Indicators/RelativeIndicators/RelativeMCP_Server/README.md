# RelativeIndicators MCP Server — Fase 1a + 1b

Servidor MCP local (stdio) que permite a Claude Code leer en tiempo real lo que
pasa en NinjaTrader 8 mientras desarrollas indicadores.

- **Fase 1a** (file-based, operativa): logs, traces y archivos exportados por
  la propia suite. Funciona sin NT8 abierto.
- **Fase 1b** (AddOn HTTP, opcional): cotizaciones, ticks y barras en vivo
  vía `RelativeObserver.cs` escuchando en `http://localhost:7891/`.

**Solo lectura. No ejecuta órdenes. No toca UI/Series de NT8.**

## Qué expone

### Logs de NinjaTrader
| Tool | Descripción |
|------|-------------|
| `health` | Verifica que las rutas de NT estén accesibles |
| `tail_nt_log(lines=100, level_min=1)` | Últimas N líneas del log principal |
| `search_nt_log(pattern, since_minutes=60)` | Regex sobre logs de las últimas N minutos |
| `list_indicator_traces` | Carpetas de trace por indicador (RelativeVwap, etc.) |
| `tail_indicator_trace(indicator, lines=100)` | Tail del trace de un indicador |
| `get_trace_today` | Archivos `trace.YYYYMMDD.*` del día |

### Niveles VWAP + confluencias
| Tool | Descripción |
|------|-------------|
| `list_vwap_instruments` | Instrumentos con VwapLevels exportados |
| `read_vwap_levels(instrument, timeframe)` | DVAH/VWAP/DVAL + zonas |
| `list_confluences(instrument, only_active=True)` | Grupos de confluencia |
| `vwap_snapshot(instrument)` | Snapshot completo (todos los TFs + activas) |

### Trade exports
| Tool | Descripción |
|------|-------------|
| `list_trade_accounts` | Cuentas con CSVs exportados |
| `list_trade_files(account)` | CSVs disponibles |
| `read_trades(account, csv_file, limit=100, tail=True)` | Lee filas |
| `trade_stats(account, csv_file, group_by="Quality")` | Win rate + PnL por grupo |

### Live data vía AddOn (Fase 1b)
Requieren el AddOn `RelativeObserver` compilado y corriendo dentro de NT8.

| Tool | Descripción |
|------|-------------|
| `observer_health` | Estado AddOn + connections NT |
| `observer_list_subscriptions` | Instrumentos con market data suscrita |
| `observer_subscribe(instrument)` | Suscribe (idempotente) |
| `observer_unsubscribe(instrument)` | Desuscribe |
| `get_quote(instrument)` | last/bid/ask/volumen/hora |
| `get_ticks(instrument, n=200)` | Últimos N ticks (Last+Bid+Ask) |
| `get_bars(instrument, tf="1m", n=50)` | OHLCV histórico vía BarsRequest |

Timeframes: `1s`, `1m`, `5m`, `15m`, `1h`, `1d`, `500t` (ticks), `5000v`
(volumen), `4r` (rango).

Si el AddOn no está corriendo, estas tools devuelven
`addon_reachable: false` con mensaje claro — las tools file-based siguen
funcionando normalmente.

### Resources
- `nt://paths` — rutas resueltas actualmente (NT_HOME, logs, vwap, etc.)

## Instalación

Desde la raíz del repo (`Indicators/RelativeIndicators/`):

```bash
pip install -r RelativeMCP_Server/requirements.txt
```

## Registro automático en Claude Code

El archivo `.mcp.json` de la raíz del repo ya registra el servidor con scope
`project`. Claude Code lo detecta al abrir el proyecto y pide aprobación la
primera vez.

Verificar dentro de Claude Code:
```
/mcp
```
Debe listar `relative-indicators` como conectado.

## Override de rutas

Por default usa `%USERPROFILE%/Documents/NinjaTrader 8`. Para apuntar a otra
instalación, definir la variable de entorno `NT_HOME`:

```json
"env": {"NT_HOME": "D:/NT8_alt"}
```

## Rutas leídas

| Qué | Dónde |
|-----|-------|
| Logs | `{NT_HOME}/log/log.*.en.txt` |
| Trace general | `{NT_HOME}/trace/trace.*.txt` |
| Trace por indicador | `{NT_HOME}/trace/{IndicatorName}/*.txt` |
| Niveles VWAP | `{NT_HOME}/bin/Custom/VwapLevels/*.txt` |
| Trades | `{NT_HOME}/bin/Custom/Strategies/TradeExports/{Account}/*.csv` |

## Ejemplos de uso en Claude Code

- "¿Hubo errores en NT los últimos 10 minutos?"
  → `search_nt_log(pattern="error|lost|failed", since_minutes=10, case_sensitive=False)`

- "¿Qué niveles VWAP tiene MES hoy?"
  → `vwap_snapshot(instrument="MES")`

- "¿Cuántas confluencias activas hay en MCL?"
  → `list_confluences(instrument="MCL")`

- "Win rate de mi cuenta DEMO619219 en marzo por Direction"
  → `trade_stats(account="DEMO619219", csv_file="VWAP_CROSS_6A_03-26.csv", group_by="Direction")`

- "Últimas 50 líneas del trace de RelativeVwap"
  → `tail_indicator_trace(indicator="RelativeVwap", lines=50)`

## Activar Fase 1b — AddOn RelativeObserver

1. Abrir NinjaScript Editor en NT8.
2. Compilar con **F7** (el archivo `Indicators/RelativeIndicators/RelativeObserver.cs`
   ya está registrado en `NinjaTrader.Custom.csproj`).
3. Abrir menú **New → NinjaScript Explorer → Add Ons**, seleccionar
   `RelativeObserver` y arrastrarlo o activarlo. Alternativamente el AddOn
   entra en `State.Active` automáticamente al arrancar NT8 tras compilar.
4. En el Output Window (tab 1) debe aparecer:
   `[RelativeObserver HH:MM:SS] HTTP listening en http://localhost:7891/`
5. Verificar desde Claude: `observer_health` debe devolver
   `addon_reachable: true` y listar las connections activas.

### Override del puerto o del host
Variables de entorno en `.mcp.json`:
```json
"env": {
  "RELATIVE_OBSERVER_URL": "http://localhost:7891",
  "RELATIVE_OBSERVER_TIMEOUT": "10"
}
```
El puerto del AddOn está hardcoded en `RelativeObserver.cs` (`LISTEN_PREFIX`).
Para cambiarlo, editar esa constante y recompilar.

## Próxima fase (2)

Escritura controlada detrás de flag `ENABLE_TRADING=false`: place/modify/cancel
de órdenes en cuenta sim, alertas registradas en el AddOn que evalúa
`OnMarketData`, webhook al squawk server en eventos.
