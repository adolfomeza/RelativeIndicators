# Docs/ — Índice del directorio

Documentación operativa y de referencia de la suite **RelativeIndicators** para NinjaTrader 8.

> Para empezar a trabajar: ver `../CLAUDE.md` (codebase) y `~/.claude/projects/.../memory/MEMORY.md` (autoridad NADRO + feedback vivos).

---

## Dominios de trabajo

### 🎯 Nadro/ — Trading metodología NADRO
Análisis de mercado bajo metodología NADRO (Merritt Black). Operativo cada sesión.

| Carpeta | Contenido |
|---------|-----------|
| [Nadro/](Nadro/) | Guías 02-06 metodología, briefings, eod_reviews, markups, nightly_reports, snapshots, snapshot_requests, trade_journal, transcripts |
| `Nadro/markups/` | JSON + HTML con análisis NADRO por instrumento/fecha (operados via tool MCP `nadro_snapshot_markup`) |
| `Nadro/nightly_reports/` | Reports 8-secciones por instrumento por sesión (tool `nadro_nightly_report`) |
| `Nadro/trade_journal/` | Bitácora de setups con correcciones del usuario |
| `Nadro/transcripts/` | 113 transcripts de entrenamiento NADRO |

### 🆕 Apteros Scalping/ — Scalping order-flow
Framework independiente de Merritt Black. **NO mezclar vocabulario con NADRO.**

| Carpeta | Contenido |
|---------|-----------|
| [Apteros Scalping/](Apteros%20Scalping/) | Transcripts, suscriptores, reglas operativas |

---

## Referencia técnica

### Indicadores
- [_indicators_index.md](_indicators_index.md) — **tabla maestra** de los 9 indicadores con versión, propósito y archivos clave (RelativeVwap, RelativeMonthlyVwap, RelativeWeeklyVwap, RelativeNMonthlyVwap, RelativeVolumeProfile, RelativeVwapLevels, RelativeNewsFilter, RelativeDelta, RelativeDVAPVA).
- Cada indicador tiene su carpeta propia `Relative*_Docs/` en la raíz del repo (un nivel arriba).

### Persona / rol experto
- [persona-trading-ninjascript.md](persona-trading-ninjascript.md) — system prompt de rol "Experto en Trading Algorítmico con NinjaTrader". Contiene identidad, especialización NinjaScript, patrones de logging y GUIs. **No es codebase guide** (eso está en `../CLAUDE.md`).

### Changelogs
- [_changelogs/CHANGELOG.md](_changelogs/CHANGELOG.md) — Changelog general de la suite
- [_changelogs/MCP_CHANGELOG.md](_changelogs/MCP_CHANGELOG.md) — Changelog del servidor MCP (`RelativeMCP_Server/`)
- [_changelogs/VWAPDelta_CHANGELOG.md](_changelogs/VWAPDelta_CHANGELOG.md) — Changelog del indicador VWAPDelta (en desarrollo)

### Blueprints (especificaciones)
- [_blueprints/VWAPDelta_Blueprint.md](_blueprints/VWAPDelta_Blueprint.md) — Especificación del indicador VWAPDelta (3 partial classes, 23h sesión, MFE/MAE)

### Setup / operación
- [_setup/EMAIL_SETUP.md](_setup/EMAIL_SETUP.md) — Configuración de alertas por email
- [_setup/TROUBLESHOOTING.md](_setup/TROUBLESHOOTING.md) — Diagnóstico de problemas comunes

### Planificación
- [_planning/CSV_EXPORT_PLAN.md](_planning/CSV_EXPORT_PLAN.md) — Plan de exportación CSV
- [_planning/CSV_EXPORT_TASKS.md](_planning/CSV_EXPORT_TASKS.md) — Tareas asociadas

### Referencias
- [_references/ninjascript-referencia.md](_references/ninjascript-referencia.md) — Referencia rápida de NinjaScript

---

## Infraestructura externa (fuera de este `Docs/`)

- **`../RelativeMCP_Server/`** — Servidor MCP Python/FastMCP (≥3.2.0). 20 tools cubriendo NADRO analysis, replay, backtest, nightly reports. Watcher daemon: `python -m RelativeMCP_Server.watcher`.
- **`../VwapLevels/`** — Archivos `{Timeframe}_{INSTRUMENT}.txt` exportados por los 5 forks VWAP (Daily/Weekly/Monthly/Quarterly/Annual). Lector: `RelativeVwapLevels`.
- **`../TradeExports/{Account}/`** — CSVs de trades (RelativeVwap).
- **Memorias Claude** — `C:\Users\prueba\.claude\projects\C--Users-prueba-Documents-NinjaTrader-8\memory\` con `MEMORY.md` como índice y `nadro_master.md` como autoridad operativa.
