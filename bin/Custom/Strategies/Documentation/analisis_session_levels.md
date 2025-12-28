# Análisis de SessionLevelsStrategy.cs

## Resumen General
La estrategia `SessionLevelsStrategy` es un sistema de trading automatizado complejo para NinjaTrader 8. Su núcleo se basa en la identificación de niveles de Soporte y Resistencia generados por los extremos (High/Low) de sesiones de mercado específicas (Asia, Europa, USA) y utiliza VWAP (Volume Weighted Average Price) anclado dinámicamente para filtrar y ejecutar entradas.

**Versión Actual**: v1.7.30 (26 Diciembre 2025)  
**Líneas de Código**: ~2680 líneas

## Arquitectura y Componentes Clave

### 1. Gestión de Sesiones (`CheckSession`, `ManageLevels`)
- **Sesiones Definidas**: Asia, Europa y USA, con horarios configurables.
- **Formación de Niveles**:
    - Durante el horario de una sesión, el sistema rastrea los extremos (High/Low).
    - Utiliza una lógica de "empuje": si el precio supera el extremo actual durante la sesión, el nivel se actualiza y el VWAP de ese nivel se reinicia (re-ancla).
- **Mitigación**:
    - Una vez formada, si el precio cruza (mitiga) la línea de un nivel en el futuro, se marca como `IsMitigated`.
    - **Ghost Lines**: Las líneas mitigadas continúan dibujándose visualmente hasta un tiempo de corte (cierre de USA) para referencia, pero cambian de estilo (punteado/gris).

### 2. Sistema de VWAP Dual
La estrategia maneja dos tipos de VWAP simultáneamente:

#### Global ETH VWAP (`ManageGlobalVWAPs`)
- Rastrea el High y Low absoluto de todo el día de trading (iniciando 18:00 NY).
- Se utiliza como referencia macro y para targets (TP1).
- Si se rompe un High/Low global, se reinicia el cálculo del VWAP desde ese nuevo punto.
- **Consistencia de Cálculo**: Usa el mismo `VwapMethod` configurado (Close, Typical HLC/3, o OHLC4).

#### Ad-Hoc / Setup VWAP (`ManageEntryA_Plus`, `UpdateAdhocVWAP`)
- Se utiliza específicamente para la ejecución de entradas.
- Se activa solo cuando hay un "Trigger" (toque de un nivel de sesión).
- Se ancla dinámicamente al High/Low de la vela que disparó la señal.
- Se acumula tick por tick usando `UpdateAdhocVWAP()`.
- **Consistencia de Cálculo**: Usa el mismo `VwapMethod` que el VWAP global.
- **Propósito**: Representar el VWAP "desde el trigger" en lugar de todo el día.

### 3. Lógica de Entrada (State Machine)
El sistema utiliza una máquina de estados para gestionar el ciclo de vida de un trade:

1. **Idle (Inactivo)**: Escanea niveles activos en busca de toques (mitigaciones).

2. **WaitingForConfirmation (Esperando Confirmación)**:
   - Se activa al tocar un nivel.
   - Dibuja flechas visuales (Cyan/Lime).
   - Espera a que el precio cierre por debajo (Short) o por encima (Long) del Ad-Hoc VWAP para confirmar.
   - **Validación R/R**: Calcula ambos targets (TP1 VWAP, TP2 Nivel) y valida contra el MÁS CERCANO.

3. **workingOrder (Orden Pendiente)**:
   - Coloca una orden Límite al precio del VWAP ad-hoc actual.
   - **Validación Continua (v1.7.28)**: Cada bar, re-calcula R/R. Si cae < 1:1, cancela orden automáticamente.

4. **PositionActive (Posición Activa)**:
   - Gestiona la salida una vez que la orden se llena.

### 4. Gestión de Riesgo y Salidas (`EnsureProtection`)

#### Entrada Consolidada (v1.7.17+)
- **Una sola orden** por la cantidad total (`Quantity`).
- Evita problemas de sincronización de órdenes divididas.

#### Salidas Dinámicas Split
- **División Post-Ejecución**: Una vez llenada la entrada, protección se divide en:
  - **TP1**: 50% de contratos → VWAP global opuesto (dinámico, más cercano)
  - **TP2**: 50% restante → Nivel de sesión opuesto (fijo, más lejano)
- **Target Histórico (v1.7.16)**: Usa `validatedTargetPrice` para TP2 fijo.

#### Stop Loss y Breakeven
- **Stop Loss**: Fijo a 1 tick del `setupAnchorPrice` (v1.7.21).
- **Breakeven**: Mueve SL al entry price automáticamente cuando TP1 se llena.

### 5. Mecanismos de Seguridad y Persistencia

#### Persistencia XML (`LoadLevels`/`SaveLevels`)
- Guarda niveles detectados en disco.
- Permite recargar estrategia sin perder líneas históricas.
- Detecta "Gaps" si data del archivo es más antigua que data del gráfico.

#### Safety Nets
- **Zombie Positions**: Detecta posiciones en broker sin estado en estrategia.
- **Orphan Positions**: Detecta y aplana posiciones huérfanas.
- **Hard Stop**: Stop de emergencia en código.
- **Startup Failsafe (v1.7.6+)**: Limpieza agresiva al iniciar.
- **Re-Entry Protection (v1.7.10)**: Bloquea órdenes históricas durante reload.

## Evolución Reciente (v1.7.18 - v1.7.30)

### v1.7.21-v1.7.23: Corrección de Targets y Búsqueda ⭐
**Fecha**: 25 Diciembre 2025

**Problemas Corregidos**:
- Stop Loss dinámico causaba inconsistencias
- Targets se invertían (TP1 lejos, TP2 cerca) por sorting
- Búsqueda de nivel opuesto fallaba por filtros incorrectos

**Soluciones**:
- **Stop Loss Fijo (v1.7.21)**: 1 tick del anchor, no dinámico
- **Asignación Fija**:
  - TP1 = VWAP global (dinámico, más cercano)
  - TP2 = Nivel opuesto (fijo, más lejano)
- **Búsqueda por Fecha (v1.7.22)**: Compara `StartTime.Date` para High/Low del mismo día
- **Cache Limpia (v1.7.23)**: `cachedOppositeLevel = null` en triggers

### v1.7.24-v1.7.26: Reset de Contadores ⚠️
**Fecha**: 25 Diciembre 2025

**Problema Crítico**:
- Contadores `protectedTp1Qty` y `protectedTp2Qty` no se reseteaban
- Trade subsecuente asignaba todos los contratos a TP2 (0 a TP1)

**Soluciones**:
- **v1.7.24**: Reset en cierre por ejecución (líneas 2510-2511)
- **v1.7.26**: Reset también en ruta SYNC para cierres externos (líneas 1353-1356)

### v1.7.27: Validación R/R Contra Target Más Cercano ⭐⭐
**Fecha**: 25 Diciembre 2025

**Problema Crítico**:
```
Validaba solo contra TP2 (lejano)
Ejemplo: Entry 2552, TP1 2548.1, TP2 2535.8, SL 2567
- R/R para TP1 = 0.26 ❌ (debió rechazar)
- R/R para TP2 = 1.08 ✅ (aceptaba erróneamente)
```

**Solución**:
- Calcula AMBOS targets (TP1 VWAP, TP2 Nivel)
- Valida contra el más cercano:
  - SHORT: `Math.Max()` (precio alto = cerca)
  - LONG: `Math.Min()` (precio bajo = cerca)
- Garantiza recuperación de riesgo en primer 50%

### v1.7.28: Validación CONTINUA de R/R ⭐⭐⭐
**Fecha**: 26 Diciembre 2025 (Madrugada)

**Problema Crítico**:
```
VWAP se mueve DESPUÉS de validación inicial
Ejemplo:
- 9:34 AM: Valida con R/R 6.4 ✅
- 9:37 AM: Llena con R/R 0.26 ❌
```

**Solución Implementada**:
1. **Función Reutilizable**: `ValidateRiskReward()` (línea 2251)
   - Calcula risk, reward, ratio
   - Retorna bool si válido

2. **Monitoreo Continuo** (líneas 1784-1806):
   - Cada bar mientras `workingOrder`
   - Re-calcula R/R con precios actuales
   - Si R/R < 1:1 → Cancela orden automáticamente
   - Log: "R/R Invalidated While Working"

**Impacto**: Previene 100% de trades con R/R inválido por movimiento del VWAP.

### v1.7.30: Soporte Strategy Analyzer 🎯
**Fecha**: 26 Diciembre 2025

**Problema**:
- Strategy bloqueada en `State.Historical`
- Solo funcionaba en Playback (Realtime)
- No permitía backtests en Strategy Analyzer

**Solución**:
```csharp
// Antes
if (State == State.Realtime)

// Después
if (State == State.Realtime || State == State.Historical)
```

**Beneficios**:
- Backtests completos con Tick Replay
- Optimización de parámetros
- Análisis estadístico robusto

**Debug Agregado**:
- Logs "VWAP_DEBUG" para diagnosticar ad-hoc vs global

## Observaciones Técnicas

### Sistema VWAP
- **Dual Calculation**: Global ETH + Ad-Hoc simultáneos
- **Consistencia**: Ambos usan mismo `VwapMethod` configurado
- **Tick-by-Tick**: Acumulación delta de volumen en tiempo real
- **Visualización**: Línea blanca representa valor al final de bar (no tick exacto)

### Performance
- **Tick Replay Required**: Para cálculo correcto de VWAP ad-hoc
- **Memoria**: Carga datos históricos + muchas líneas dibujadas
- **Optimización**: `ShowVisuals` reduce carga gráfica

### Timezones
- Depende de "Eastern Standard Time"
- Trading day reset @ 18:00 NY

### Debug y Logs
- **EnableDebugLogs**: Control de verbosidad
  - `false`: Solo logs de auditoría (orders, fills)
  - `true`: Debug completo (triggers, targets, búsquedas)
- **TriggerScreenshot**: Capturas automáticas al operar

## Estado Actual (v1.7.30)

### ✅ Funcionalidades Verificadas
- Validación R/R continua funcionando
- División de contratos 50/50 correcta
- VWAP ad-hoc calculándose correctamente
- Strategy Analyzer compatible
- Auto-cancelación de órdenes inválidas

### 📋 Features Pendientes (Documentadas)
1. **Entry Type B**: Ruptura + pullback cuando no hay niveles
2. **Gestión Niveles Internos**: Re-anclar VWAP, invalidación por niveles externos
3. **Dynamic Position Sizing**: Normalizar riesgo en USD entre instrumentos

### 🎯 Métricas Objetivo
- Win Rate: >50% esperado
- Profit Factor: >1.5 esperado  
- Max Drawdown: <15% cuenta
- Trades rechazados: 30-40% por R/R (filtrado agresivo)

## Conclusión
Es una estrategia sofisticada que combina análisis de estructura de mercado (Sesiones) con análisis de flujo de órdenes (VWAP). Está diseñada para operar de manera autónoma con un alto grado de protección contra fallos técnicos, desconexiones, y especialmente contra la ejecución de trades con R/R inválido mediante validación continua.

La versión v1.7.30 representa el estado más robusto de la estrategia, con correcciones críticas de bugs de división de contratos, validación R/R, y soporte completo para backtesting sistemático.

---

*Última actualización: 26 Diciembre 2025*  
*Versión documentada: v1.7.30*  
*Próxima revisión: Post-backtest 1 año MNQ*
