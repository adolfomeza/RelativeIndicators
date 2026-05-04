# VWAPDelta - Changelog

## v1.0.0 (2026-02-20)

### Nuevo indicador
Indicador de data collection que dibuja VWAPs anclados desde extremos de sesion con mecanica freeze/re-anchor, detecta toques, los clasifica con 4 patrones orderflow, y trackea MFE/MAE hasta fin de sesion americana.

### Arquitectura
- 3 partial classes: `VWAPDelta.cs`, `VWAPDelta.TouchDetect.cs`, `VWAPDelta.Export.cs`
- Namespace: `NinjaTrader.NinjaScript.Indicators.RelativeIndicators`
- Rendering via `OnRender()` con SharpDX (VWAPs dinamicos, no AddPlot)

### Funcionalidades

#### VWAPs con Freeze/Re-Anchor
- Sesion continua de 23 horas (18:00 - 16:00 ET)
- VWAP High anclado al maximo de sesion, VWAP Low al minimo
- Al superarse el extremo: VWAP activo se congela (curva historica) y se crea uno nuevo desde la vela de breakout
- VWAPs activos: linea solida con opacidad completa
- VWAPs congelados: linea mas fina con opacidad reducida (configurable)

#### Deteccion de Toques
- Tolerancia configurable en ticks (`TouchToleranceTicks`)
- Cooldown entre toques al mismo VWAP (`TouchCooldownBars`)
- Clasificacion: FromAbove, FromBelow, CrossUp, CrossDown, Consolidation
- Marcadores visuales (diamantes) coloreados por calidad de senal

#### 4 Patrones Orderflow (scores 0-1)
- **Absorption (A)**: Wick rejection + delta contradictorio + alto volumen
- **Initiative (B)**: Body fuerte + delta a favor + alto volumen (breakout)
- **Exhaustion (C)**: Delta decreciente en toques sucesivos al mismo VWAP
- **Sweep (D)**: Bajo volumen + alto rango (precio llego por vacio)
- **Composite**: `(A*0.30) + ((1-B)*0.25) + (C*0.25) + (D*0.20)`

#### MFE/MAE hasta EOD
- Tracking continuo desde barra de toque hasta fin sesion USA (16:00 ET)
- MFE direccional: VWAP High = bajista (resistencia), VWAP Low = alcista (soporte)
- MAE para calibracion de stop losses
- BarsToMFE: barras desde toque hasta MFE maximo

#### CSV Export (~30 columnas)
- Ruta: `TradeExports/DEMO619219/VWAPDelta_{SYMBOL}_{MM-yy}.csv`
- Incluye: ID, horas (anchor/touch), scores de patrones, delta, ATR, volumen, MFE/MAE
- Formato: InvariantCulture, append mensual
- Opcion `ExportRealtimeOnly` para ignorar datos historicos

#### Delta
- Proxy: `(Close - Open) * Volume` como aproximacion de delta
- DeltaGlobal acumulado desde inicio de sesion
- DeltaAtAnchor capturado al momento de anclar cada VWAP

### Propiedades configurables
- **Sesion**: SessionStartTime, USAEndTime (formato "HH:mm")
- **Visual**: HighVwapColor, LowVwapColor, ActiveLineWidth, FrozenOpacity
- **Toques**: TouchToleranceTicks, TouchCooldownBars, ShowTouchMarkers
- **Export**: ExportCSV, ExportRealtimeOnly
- **Debug**: ShowDebugLogs
