# Plan: Export de Simulación VWAP a CSV para Streamlit

## Objetivo
Exportar los trades simulados por `RelativeVwap` (Signal 2 historical simulation) a un CSV compatible con el formato que ya usa `StreamlitAudit/app.py` para la estrategia SessionLevels. Sin cambios en la app — el CSV se coloca en `TradeExports/` y se analiza con todos los tabs existentes.

## Ubicación de Archivos Clave
- Simulación: `RelativeVwap.Utilities.cs` — clase `PendingSignal`, métodos `DrawSignalVisualization` y `DrawStoredSignalVisualization`
- Llamadas a simulación: `RelativeVwap.cs` líneas ~1637 (SHORT Signal 2) y ~1977 (LONG Signal 2)
- CSV de referencia: `Strategies/TradeExports/DEMO619219/MNQ_03-26.csv`
- App Streamlit: `Strategies/StreamlitAudit/app.py`

## Formato CSV Target
```
ID,Instrument,EntryTime,Type,EntryPrice,ExitTime,ExitPrice,Result,PnL,Commission,NetPnL,MAE,MFE,Setup,Attempt,RiskReward,DeltaAtEntry,DeltaDirection,SessionDelta,DeltaAtTP1,LevelAge,Quantity,Trade_Clust_ID
```

### Ejemplo
```
20260116_1,MNQ 03-26,2026-01-16 04:07:44,Short,21840.75,2026-01-16 06:30:58,21846.75,SL_Short_01,-24.00,3.80,-27.80,25.50,273.00,Asia High,1,-1.20,0,0,0,0,1,2,20260116_1
```

## Mapeo de Columnas

| Columna CSV | Fuente en Indicator | Nota |
|---|---|---|
| `ID` | fecha + contador diario | YYYYMMDD_N |
| `Instrument` | `Instrument.FullName` | ej: "MNQ 03-26" |
| `EntryTime` | `Time.GetValueAt(SignalBarIdx)` | formato: "YYYY-MM-DD HH:MM:SS" |
| `ExitTime` | `Time.GetValueAt(exitBar)` | si aún abierto: tiempo actual |
| `Type` | `IsLong ? "Long" : "Short"` | |
| `EntryPrice` | `Close.GetValueAt(SignalBarIdx)` | |
| `ExitPrice` | `exitPrice1` o `exitPrice2` | |
| `Result` | construir desde win/SL lógica | SL_Long_01, TP1_Short_02, etc. |
| `PnL` | `(exit-entry) * qty * pointValue` | con signo correcto |
| `Commission` | lookup tabla por instrumento | MNQ=2.00, ES=2.05, etc. |
| `NetPnL` | `PnL - Commission` | |
| `MAE` | máx adverse excursion en loop | calcular durante simulación |
| `MFE` | máx favorable excursion en loop | calcular durante simulación |
| `Setup` | `PendingSignal.SetupName` | "Asia High", "Europe Low"... |
| `Attempt` | `PendingSignal.AnchorSequence` | número de reintento |
| `RiskReward` | `|TP-Entry| / |Entry-SL|` | |
| `DeltaAtEntry` | **0** (no disponible) | placeholder |
| `DeltaDirection` | **0** | placeholder |
| `SessionDelta` | **0** | placeholder |
| `DeltaAtTP1` | **0** | placeholder |
| `LevelAge` | días entre `AnchorTime` y `EntryTime` | `(SignalBarDate - AnchorBarDate).Days` |
| `Quantity` | `qty1` o `qty2` | por posición |
| `Trade_Clust_ID` | mismo ID para ambas posiciones | agrupa TP1 y TP2 del mismo trade |

## Comisiones por Instrumento (por contrato, round trip)
```
MNQ: $2.00 * 2 = $4.00
MES: $1.25 * 2 = $2.50
MNQ: $4.10 (NinjaTrader default)
NQ:  $4.10
ES:  $4.10
CL:  $2.10
GC:  $2.10
MCL: $1.05
MGC: $1.05
```
*(Ajustar según broker real del usuario)*

---

## Columnas Obligatorias para la App (usadas en 5+ análisis)
1. `ID` — identificador único
2. `Instrument` — para filtros multi-instrumento
3. `EntryTime` / `ExitTime` — análisis temporal, calendario
4. `Type` — Long/Short split
5. `PnL` — CRÍTICO para toda la app
6. `Result` — para Exit_Tier (SL, TP1, TP2...)
7. `MAE` / `MFE` — TAB 4 MAE/MFE analysis
8. `Setup` (→ `SetupName`) — análisis por nivel
9. `Attempt` — análisis por reintento
10. `LevelAge` — análisis antigüedad de nivel

## Columnas Que la App Calcula Automáticamente
- `Cumulative_PnL` = PnL.cumsum()
- `Hour` = EntryTime.dt.hour
- `Weekday` = EntryTime.dt.day_name()
- `Trade_Clust_ID` = agrupación por ParentID
- `Exit_Tier` = extraído de Result (SL, TP1, TP2)
- `Exit_Rank` = numérico del tier

## Estructura de Result
Formato: `{ExitType}_{Direction}_{Attempt:02d}`
- `SL_Long_01`, `SL_Short_02`
- `TP1_Long_01`, `TP1_Short_01`
- `TP2_Long_01`, `TP2_Short_01`
- `Exit_Long_Market` (si aún abierto al final)

## Destino del CSV
`c:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeExports\DEMO619219\VWAP_{SYMBOL}_{MONTH}-{YEAR}.csv`

Ejemplo: `VWAP_MNQ_03-26.csv`

---
*Documento creado: 2026-02-07*
*Versión del plan: 1.0*
