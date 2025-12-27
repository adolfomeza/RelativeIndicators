# Registro de Cambios (Changelog)

Todos los cambios notables en el proyecto `SessionLevelsStrategy` serán documentados en este archivo.

El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.0.0/), y este proyecto adhiere a [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.10.11] - 2025-12-27 ✅ VERSIÓN ACTUAL
### Fix: VWAP Ad-Hoc También Usa Close Definitivo
- **Problema**: v1.10.10 solo arreglaba VWAP Global, ad-hoc seguía usando Close momentáneo
    - Re-anchors (nuevos highs/lows durante setup) usaban Close[0] momentáneo
    - Triggers iniciales y external triggers igualmente afectados
- **Solución**:
    - Nueva variable: `adhocAnchorBar` para rastrear barra de anchor
    - En `UpdateAdhocVWAP()`: Si barra anterior fue anchor → recalcula con Close[1]
    - Actualiza 6 ubicaciones: re-anchor SHORT/LONG, trigger SHORT/LONG, external SHORT/LONG
- **Resultado**: Ambos VWAPs (Global y Ad-Hoc) comienzan en Close definitivo

## [1.10.10] - 2025-12-27
### Fix: VWAP Comienza en Close Definitivo (Actualización Retroactiva)
- **Problema**: En tiempo real, Close[0] es el último precio, no el cierre final
    - Causaba que el VWAP comenzara en punto intermedio durante formación de vela
- **Solución** (Actualización Retroactiva):
    - Reset inmediato del VWAP usando Close[0] momentáneo (para tener valor visible)
    - En IsFirstTickOfBar, si barra anterior fue anchor → recalcula con Close[1] definitivo
    - Actualiza `Values[x][1]` retroactivamente para corregir el valor visual
- **Resultado**: VWAP se muestra durante formación de vela, pero al cerrar queda en Close exacto
- **Beneficio**: Evita señales falsas de entrada al usar Close más conservador (más cerca del precio)

## [1.10.9] - 2025-12-27
### Reversión: VWAP Global con Reset Inmediato
- **Problema**: v1.10.7/v1.10.8 intentaron diferir el reset para usar Close definitivo
    - Causó línea de conexión no deseada con VWAP histórico
    - El VWAP comenzaba en barra siguiente o con problemas visuales
- **Solución**: Revertido al comportamiento original (reset inmediato)
    - El VWAP se resetea inmediatamente cuando se detecta nuevo High/Low
    - En tiempo real, usará Close momentáneo (se actualiza tick-a-tick)
    - Al cerrar la barra, el valor se fija al Close definitivo
- **Nota**: En barras históricas siempre funciona correctamente. En tiempo real el primer punto puede moverse durante formación de la vela.

## [1.10.8] - 2025-12-27
### Fix: VWAP Visual Comienza en Barra de Anchor Correcta
- **Problema**: v1.10.7 colocaba el inicio del VWAP en la barra siguiente al nuevo High
    - Causa: Solo asignaba `Values[0][0]` (barra actual), no `Values[0][1]` (barra anterior)
- **Solución**: Al aplicar reset diferido, asigna el valor inicial a `Values[x][1]`
    - Línea VWAP ahora comienza visualmente en la barra que hizo el nuevo High/Low
    - Usa `Close[1]` definitivo para el precio de anclaje
- **Resultado**: VWAP comienza en la vela correcta CON el Close definitivo

## [1.10.7] - 2025-12-27
### Fix: VWAP Global Ahora Usa Close Definitivo de la Vela
- **Problema**: VWAP Global comenzaba en el "medio de la mecha" cuando se formaba nuevo High/Low
    - Causa: Al detectar nuevo High intra-bar, `Close[0]` era el último precio, no el cierre final
    - Resultado: La línea VWAP comenzaba en punto aleatorio dentro de la vela
- **Solución** (Sistema de Reset Diferido):
    - Variables nuevas: `pendingHighReset`, `pendingLowReset` para marcar reset pendiente
    - Cuando se detecta nuevo High/Low → marca como pendiente, NO resetea inmediatamente
    - En primera tick de siguiente barra → aplica reset con `Close[1]` (cierre definitivo)
    - Calcula precio según VwapMethod configurado (Close/Typical/OHLC4)
- **Resultado**: VWAP Global ahora siempre comienza exactamente en el Close de la vela anchor
- **Testing**: Verificar en Playback que nuevos Highs del día generen VWAP desde Close, no desde precio intermedio

## [1.10.6] - 2025-12-27
### Fix Visual Completo: VWAP Ad-Hoc LONG Setups
- **Problema**: v1.10.5 solo corrigió SHORT setups, LONG seguían anclando desde Low
    - Re-anchor LONG: Faltaba `visualAdhocLastVal = price` y usaba `= 0`
    - Trigger LONG: Usaba `visualAdhocPrevBarVal = 0` y `visualAdhocLastVal = 0`
    - External level triggers (SHORT y LONG): Ambos usaban `= 0`
- **Solución** (4 ubicaciones corregidas):
    - Línea 1513-1517: Re-anchor LONG → `= price` + agregada línea faltante
    - Línea 1597-1599: External SHORT trigger → `= price`
    - Línea 1629-1631: External LONG trigger → `= price`
    - Línea 1773-1775: Trigger LONG regular → `= price`
- **Resultado**: Ahora TODOS los VWAPs ad-hoc (SHORT y LONG) comienzan en el precio correcto
- **Testing**: Verificar que líneas VWAP de LONG setups inicien en Close/Typical, no en Low

## [1.10.5] - 2025-12-26
### Fix Visual: VWAP Ahora Comienza en Precio Calculado
- **Problema**: VWAP visual comenzaba en Low/High en vez del Close/Typical configurado
    - Usuario reportó: "Todas las líneas VWAP históricas comienzan desde el low de la vela"
    - Causa: `visualAdhocPrevBarVal = 0` y `visualAdhocLastVal = 0` en inicialización
- **Solución** (parcial - solo SHORT):
    - Cambio: `visualAdhocLastVal = 0` → `visualAdhocLastVal = price`
    - Donde `price` = Close/Typical/OHLC4 según configuración VwapMethod
- **Nota**: Este fix fue incompleto, corregido en v1.10.6

## [1.10.4] - 2025-12-26
### Fix Crítico: Re-Anclaje de VWAP Corregido
- **Problema**: VWAP no se re-anclaba cuando precio se movía exactamente 1 tick
    - Condición usaba `<` y `>` (comparación estricta) en vez de `<=` y `>=`
    - Ejemplo: Anchor @ 6901, price baja a 6900.75 (1 tick)
    - Evaluación: `6900.75 < (6901 - 0.25)` → `6900.75 < 6900.75` → FALSE ❌
- **Solución** (líneas 1476, 1499):
    - **SHORT**: `High[0] > setupAnchorPrice + TickSize` → `High[0] >= setupAnchorPrice + TickSize`
    - **LONG**: `Low[0] < setupAnchorPrice - TickSize` → `Low[0] <= setupAnchorPrice - TickSize`
    - Ahora re-ancla cuando precio se mueve 1 tick O MÁS
- **Impacto**: VWAP ahora se resetea correctamente en nuevos extremos
- **Testing**: Verificar logs `RE-ANCHOR: New Low/High` aparecen con cada nuevo extremo

## [1.10.3] - 2025-12-26
### Fix Crítico: Corrección de Lógica de Detección de Niveles Internos
- **Problema**: Lógica INVERTIDA para detectar niveles internos
    - Buscaba nivel más CERCANO de otra sesión
    - **INCORRECTO**: Europe High @ 90 con Asia High @ 100 arriba no se detectaba como interno
- **Definición CORRECTA de nivel interno**:
    - **SHORT**: Nivel es interno si existe un High del DÍA de otra sesión POR ENCIMA (máximo del día)
        - Ejemplo: Europe High @ 90 es INTERNO porque Asia High @ 100 es el máximo del día
    - **LONG**: Nivel es interno si existe un Low del DÍA de otra sesión POR DEBAJO (mínimo del día)
        - Ejemplo: Europe Low @ 60 es INTERNO porque Asia Low @ 50 es el mínimo del día
- **Solución** (líneas 2391-2455):
    - `FindExternalLevelAbove()`: Ahora busca el **HIGHEST High** del día (no el closest)
        - `if (level.Price > highestExternal)` en vez de `if (level.Price < closestExternal)`
    - `FindExternalLevelBelow()`: Ahora busca el **LOWEST Low** del día (no el closest)
        - `if (level.Price < lowestExternal)` en vez de `if (level.Price > closestExternal)`
- **Testing**: Con Asia High @ 100, Europe High @ 90 debe detectarse como interno ✅

## [1.10.2] - 2025-12-26
### Fix: Auto-Trigger en Nivel Externo Tras Invalidación
- **Problema**: Después de invalidar nivel interno, NO hacía trigger en nivel externo
    - Invalidaba Asia High @ 6911.75 (toca Europe High @ 6912)
    - En barra 4:53 precio se despegaba de VWAP de Europe High
    - NO colocaba orden limit porque Europe High ya tenía `IsMitigated = true` de barra anterior
- **Solución** (líneas 1561-1640):
    - Al invalidar nivel interno, automáticamente hace **AUTO-TRIGGER** en nivel externo
    - Crea nuevo setup completo: anchor, VWAP, visual, estado
    - Log: `AUTO-TRIGGER: Switching to external level Europe High @ 6912`
    - `isInternalLevel = false` (externo no es interno)
- **Flujo ahora**:
    1. Detecta Asia High interno
    2. Invalida (toca Europe High externo)
    3. AUTO-TRIGGER en Europe High
    4. En 4:53 se despega → Crea orden limit ✅
- **Testing**: Verificar log AUTO-TRIGGER y que coloca orden en 4:53

## [1.10.1] - 2025-12-26
### Hotfix Crítico: Infinite Loop en Invalidación
- **Bug Corregido**: Loop infinito cuando nivel interno se invalida inmediatamente
    - **Problema**: Al invalidar (tocar externo), estrategia reseteaba a `Idle` pero continuaba foreach en misma barra
        - Resultado: Re-detectaba trigger → Invalidaba → Re-detectaba infinitamente
        - Log spam: 80+ líneas idénticas en mismo timestamp
    - **Root Cause**: No había protección anti-loop para invalidación (solo para rejection)
- **Solución** (líneas 121, 1546, 1598):
    - Nueva variable: `lastInvalidationBar` (línea 121)
    - Al invalidar: `lastInvalidationBar = CurrentBar` (línea 1546)
    - Check loop protection: `if (CurrentBar == lastRejectionBar || CurrentBar == lastInvalidationBar) return` (línea 1598)
- **Beneficio**: Invalidación solo ocurre una vez por barra
- **Testing**: Verificar con Asia High interno que se invalida al tocar Europe High

## [1.10.0] - 2025-12-26
### Feature Mayor: Internal Levels Management
- **Objetivo**: Mejorar comportamiento y win rate de trades en niveles internos
    - **Niveles internos**: Niveles de sesión contenidos dentro del rango de otra sesión (ej: Europe Low dentro de rango Asia)
    - **Problema previo**: Niveles internos no se comportaban correctamente (VWAP no se re-anclaba, no se invalidaban al tocar externos, TP2 lejano)

#### Fase 1-2: Detección de Niveles Internos
- **Nuevas variables** (líneas 115-120):
  - `isInternalLevel`: Flag que indica si setup actual es interno
  - `externalLevelAbove/Below`: Precio de niveles externos que contienen al interno
  - `externalLevelAboveName/BelowName`: Nombres para logs
- **Nuevas funciones** (líneas 2133-2248):
  - `DetectInternalLevel()`: Detecta si nivel es interno y encuentra externos
  - `FindExternalLevelAbove()`: Busca High externo superior (SHORT setups)
  - `FindExternalLevelBelow()`: Busca Low externo inferior (LONG setups)
  - `GetSessionName()`: Extrae nombre de sesión del nivel
- **Log nuevo**: `INTERNAL LEVEL: Europe Low @ 6884 (External below: Asia Low @ 6850)`

#### Fase 3: Re-Anclaje de VWAP para Internos (líneas 1473-1519)
- **Cambio**: Niveles internos ahora re-anclan VWAP igual que externos
  - ANTES: Solo niveles externos re-anclaban cuando precio rompía anchor
  - AHORA: **TODOS** los niveles re-anclan (internos y externos)
- **Implementación**:
  - SHORT: Si `High[0] > setupAnchorPrice + TickSize` → Re-anclar a nuevo High
  - LONG: Si `Low[0] < setupAnchorPrice - TickSize` → Re-anclar a nuevo Low
  - Reset completo de VWAP desde nuevo anchor
- **Log nuevo**: `RE-ANCHOR: New High @ 6920 (Setup: Europe High)`
- **Beneficio**: VWAP siempre refleja precio desde extremo REAL, independiente de tipo de nivel

#### Fase 4: Invalidación al Tocar Nivel Externo (líneas 1521-1558)
- **Cambio**: Trade se cancela si precio toca nivel externo
  - SHORT interno: Si toca High externo superior → Invalidar
  - LONG interno: Si toca Low externo inferior → Invalidar
- **Acción al invalidar**:
  1. Cancel entry order si existe
  2. Reset a EntryState.Idle
  3. Log: `INVALIDATED: Touched external Asia Low @ 6850`
- **Beneficio**: No entra en contexto inválido (nivel externo tiene prioridad)

#### Fase 5: TP2 = Extremos Diarios (líneas 2067-2090, 2158-2167)
- **Cambio**: TP2 usa High/Low del día en vez de nivel opuesto
  - ANTES: TP2 = Nivel opuesto de sesión (ej: Europe High para Europe Low LONG)
    - Problema: Muy lejano o ilógico en internos
  - AHORA: TP2 = `GetDailyHigh()` (LONG) o `GetDailyLow()` (SHORT)
- **Nuevas funciones**:
  - `GetDailyHigh()`: Retorna High más alto desde medianoche
  - `GetDailyLow()`: Retorna Low más bajo desde medianoche
  - Usa `BarsSinceNewTradingDay` + `HighestBar`/`LowestBar`
- **Validación**: Si TP2 inválido (>= entry para SHORT), usa fallback
- **Beneficio**: TP2 más realista, basado en extremos reales del día

#### Fase 6: Integración (líneas 1645-1647, 1679-1681)
- **Call point**: `DetectInternalLevel(lvl, activeLevels)` llamado cuando trigger detectado
  - Para SHORT triggers (línea 1645)
  - Para LONG triggers (línea 1679)
- **Flujo completo**:
  1. Trigger detectado → `DetectInternalLevel()` ejecuta
  2. Si interno: `isInternalLevel = true`, encuentra externos
  3. Durante confirmación: Re-anclaje automático si precio rompe
  4. Durante confirmación: Invalidación si toca externo
  5. Al crear órdenes: TP2 usa daily extreme

### Logs Esperados (Ejemplo: Europe Low Interno)
```
INTERNAL LEVEL: Europe Low @ 6884 (External below: Asia Low @ 6850)
RE-ANCHOR: New Low @ 6880 (Setup: Europe Low)
RE-ANCHOR: New Low @ 6875 (Setup: Europe Low)
TP CALC (Long): ... | TP2=6950 | Selected=6950  ← Daily High, no Europe High
```

### Código Agregado
- **Total**: ~250 líneas nuevas
  - Variables: 5 líneas
  - Funciones detección: 120 líneas
  - Re-anclaje: 50 líneas
  - Invalidación: 40 líneas
  - Daily extremes: 30 líneas
  - Integ ración: 5 líneas

### Testing Sugerido
1. **Playback con nivel interno** (Europe dentro de Asia)
2. **Verificar logs**: INTERNAL LEVEL, RE-ANCHOR, INVALIDATED
3. **Verificar TP2**: Debe ser daily extreme, no nivel opuesto
4. **Verificar invalidación**: Si toca Asia Low, debe cancelar Europe Low trade

## [1.9.0] - 2025-12-26
### Cambio Arquitectónico Mayor (Single-SL Architecture)
- **Rediseño Completo**: Arquitectura de órdenes de protección  reorganizada
    - **Problema v1.8.6 y anteriores**: Dual-SL (SL1↔TP1, SL2↔TP2) causaba ejecución simultánea de ambos SL
        - OCO solo funciona DENTRO de cada grupo, no ENTRE grupos
        - Cuando precio tocaba SL → SL1 y SL2 se ejecutaban juntos
        - Resultado: Cierre doble (20 contratos en vez de 10)
    - **Solución v1.9.0**: SINGLE-SL Architecture
        - **UN SOLO SL** para toda la posición (`stopOrder`)
        - TP1 y TP2 independientes (sin OCO)
        - SL siempre refleja `Position.Quantity` total
    - **Cambios en código** (`SubmitProtectionOrders`, líneas 1970-2124):
        - Eliminada lógica dual-SL (`stopOrder1`/`stopOrder2`)
        - Implementado SL único que se cancela/recrea con partial fills
        - `stopOrder.Quantity` siempre = `Math.Abs(Position.Quantity)`
    - **Logs nuevos**:
        - `SL UPDATE: Cancelling old SL (Qty=10), creating new (Qty=20)`
        - `CANCEL-CONSOLIDATE TP1: Cancelling old (Qty=2), creating new (Qty=3)`
    - **Impacto**: Solo UN SL puede ejecutarse. Protección correcta garantizada.

## [1.8.6] - 2025-12-26
### Corrección Crítica (Cancel-Before-Consolidate for Safe OCO)
- **Bug Corregido**: Dual SL execution - ambos SL se ejecutaban simultáneamente (arquitectura OCO incorrecta)
    - **Problema ROOT CAUSE**: Consolidación con `ChangeOrder` dejaba **múltiples grupos OCO activos** simultáneamente
        - Cada partial fill creaba nuevo grupo OCO (SL1↔TP1, SL2↔TP2)
        - OCO solo funciona **dentro** de cada grupo, no **entre** grupos
        - Resultado: Cuando precio tocaba SL, **ambos SL1 y SL2** se ejecutaban (20 contratos en vez de 10)
    - **Solución**: Arquitectura Cancel-Before-Consolidate (líneas 2060-2083):
        1. **Cancelar** órdenes antiguas del OCO group
        2. **Crear** nuevas órdenes consolidadas con cantidad total
        3. Garantiza solo **UN** grupo OCO activo a la vez
   - **Log nuevo**: `CANCEL-CONSOLIDATE TP1: Cancelling old orders (Qty=2), creating new (Qty=3)`
    - **Impacto**: Solo un SL activo por bucket. OCO funciona correctamente. Cierre seguro.

## [1.8.5] - 2025-12-26
### Corrección Crítica (Dual SL Execution Fix)
- **Bug Corregido**: Ambos SL se ejecutaban simultáneamente al mismo precio
    - **Problema**: Durante consolidación, `ChangeOrder` actualizaba el precio del SL con `slPrice` recalculado
        - SL1 y SL2 terminaban ambos al mismo precio (ej: 6916.75)
        - Cuando precio tocaba ese nivel, **ambos** SL se ejecutaban antes de que OCO cancelara uno
        - Resultado: Posición cerrada doble (20 contratos en vez de 10)
    - **Solución**: Preservar precio original del SL durante consolidación (línea 2070):
        ```csharp
        // ANTES (v1.8.4) - Recalcula precio ❌
        ChangeOrder(existingSL, targetQty, 0, slPrice);
        
        // AHORA (v1.8.5) - Preserva precio original ✅
        ChangeOrder(existingSL, targetQty, 0, existingSL.StopPrice);
        ```
    - **Impacto**: Solo actualiza cantidad del SL, **no el precio**. Cada OCO group mantiene su SL independiente.

## [1.8.4] - 2025-12-26
### Corrección Crítica (Over-Consolidation Fix)
- **Bug Corregido**: Sobre-consolidación causaba doble protección
    - **Problema**: La lógica de consolidación v1.8.3 sumaba cantidades **incrementalmente** en vez de establecer la cantidad **absoluta**
        - Con 6 partial fills, acumulaba: 1+2+3+5+4+5 = 20 → TP1=10, TP2=10 (total 20 en vez de 10)
        - Resultado: Ambos SL se llenaron cerrando 20 contratos cuando la posición solo tenía 10
    - **Solución**: Cambiar cálculo de consolidación (líneas 2062-2065):
        ```csharp
        // ANTES (v1.8.3) - Suma incremental ❌
        int newQty = existingTP.Quantity + qty;
        
        // AHORA (v1.8.4) - Cantidad absoluta ✅
        int targetQty = isTp1 ? (protectedTp1Qty + qty) : (protectedTp2Qty + qty);
        ```
    - **Log actualizado**: `CONSOLIDATE TP1: Current=2 → Target=3 (was adding 1)`
    - **Impacto**: Ahora la cantidad de protección es correcta. Con 20 contratos llenados → TP1=10, TP2=10 (total 20) ✅

## [1.8.3] - 2025-12-26
### Corrección Crítica (Partial Fills Consolidation)
- **Bug Corregido**: Órdenes de protección duplicadas con partial fills múltiples
    - **Problema**: Con partial fills fragmentados (ej: orden de 20 llenada en 6 fills), `SubmitProtectionOrders` creaba nuevas órdenes TP1/TP2 en cada fill en vez de consolidar, causando:
        - Múltiples órdenes TP/SL activas simultáneas
        - Solo la última orden se actualizaba con VWAP dinámico
        - Órdenes "huérfanas" permanecían en precios obsoletos
        - Ejemplo: 10 contratos en TP1, pero solo 2 se movían con VWAP
    - **Solución**: Implementada lógica de consolidación en `SubmitProtectionOrders()` (líneas 2050-2103):
        - Verifica si ya existe orden activa (`Working` o `Accepted`)
        - Si existe: usa `ChangeOrder` para **aumentar cantidad** de orden existente
        - Si no existe: crea nueva orden (comportamiento original)
        - Mantiene integridad de OCO groups
    - **Log nuevo**: `CONSOLIDATE TP1: Existing=2 + New=3 = Total=5`
    - **Impacto**: Ahora todos los contratos se mueven juntos con actualizaciones de VWAP. Una sola orden TP1, una sola orden TP2.

## [1.8.2] - 2025-12-26
### Optimización (Output Log Cleanup)
- **Logs VWAP Removidos**: Eliminados logs verbosos de debug del VWAP
    - **Problema**: `GetSetupVWAP()` imprimía mensajes duplicados en cada bar, saturando el Output Window
    - **Ejemplo**: `VWAP_DEBUG: Using ADHOC VWAP=6875.58 (VolSum=44655.00)` aparecía 2-3 veces por minuto
    - **Solución**: Removidos ambos logs de `GetSetupVWAP()` (líneas 2341, 2347)
    - **Impacto**: Output más limpio. Los logs importantes de targets (TP CALC) y búsqueda (SEARCH_OPPOSITE) permanecen intactos
    - **Beneficio**: Más fácil identificar problemas reales en playback/live testing

## [1.8.1] - 2025-12-26
### Corrección (Partial Fills Distribution)
- **Bug Corregido**: Distribución incorrecta de TP1/TP2 con partial fills
    - **Problema**: Con partial fills, `EnsureProtection` usaba `filledQty` (cantidad del fill parcial) en vez de la posición total, resultando en distribución desigual (ej: 4 en TP1, 16 en TP2 en vez de 10/10 con 20 contratos)
    - **Solución**: Cambiar fórmula para usar `Position.Quantity` (total acumulado) en vez de `filledQty` (parcial)
    - **Código modificado**:
        - `EnsureProtection()`: Usa `Math.Abs(Position.Quantity)` para calcular `totalTp1Target`
    - **Impacto**: Ahora la distribución 50/50 se mantiene correcta incluso con fills parciales en instrumentos de bajo volumen

## [1.8.0] - 2025-12-26
### Feature Mayor (Dynamic Position Sizing)
- **Normalización de Riesgo por Instrumento**:
    - **Problema**: Quantity fijo resultaba en riesgo desigual en USD entre instrumentos (ej: MES $100 vs MYM $10 para mismo setup)
    - **Solución**: Sistema de cálculo dinámico basado en riesgo objetivo en USD
    - **Nuevas Propiedades** (Order Management):
        - `RiskPerTradeUSD`: Riesgo deseado por trade en USD (default: $50)
        - `MinQuantity`: Cantidad mínima de contratos (default: 1)
        - `MaxQuantity`: Cantidad máxima de contratos (default: 10)
        - `UseDynamicSizing`: Toggle para activar/desactivar sizing dinámico (default: true)
    - **Fórmula**: `Quantity = RiskUSD / (TicksDeRiesgo × ValorPorTick)`
    - **Ejemplo**: Con Risk=$50 y SL=10 ticks → MES (1 contrato), MNQ (3 contratos), MYM (10 contratos) → Riesgo normalizado ~$50
    - **Código modificado**:
        - Nuevo método `CalculateDynamicQuantity()` (línea 1405)
        - Confirmación SHORT (línea 1674): Ahora calcula quantity dinámicamente
        - Confirmación LONG (línea 1761): Ahora calcula quantity dinámicamente
        - `EnsureProtection` (línea 1942): Usa `filledQty` real en vez de `Quantity` configurado
    - **Beneficio**: Riesgo consistente entre todos los instrumentos. Compatible con toggle OFF para usar Quantity fijo tradicional.

## [1.7.30] - 2025-12-26
### Feature (Strategy Analyzer Support)
- **Soporte para Strategy Analyzer**:
    - **Problema**: Strategy no podía ejecutarse en Strategy Analyzer (State.Historical bloqueado)
    - **Solución**: Modificado check de `State == State.Realtime` a `State == State.Realtime || State == State.Historical`
    - **Impacto**: Ahora funciona en Playback (Realtime) Y en Strategy Analyzer (Historical)
    - **Código modificado**: Líneas 1622-1625 (SHORT), líneas 1706-1708 (LONG)
- **Debug VWAP Ad-Hoc**:
    - Agregados logs debug en `GetSetupVWAP()` para diagnosticar fallback a VWAP global
    - Log: "VWAP_DEBUG: Using ADHOC VWAP=..." o "VWAP_DEBUG: FALLBACK to GLOBAL VWAP=..."

## [1.7.29-debug] - 2025-12-26 (No publicada)
### Debug
- Logs temporales para investigar problema de VWAP ad-hoc.

## [1.7.28] - 2025-12-26
### Feature Crítica (Validación Continua R/R)
- **Validación Continua de Risk/Reward**:
    - **Problema**: Validación R/R se hacía solo en confirmación inicial, pero el VWAP seguía moviéndose después. Ejemplo: valida @ 9:34 AM con entry 2564 (R/R válido), pero orden se llena @ 9:37 AM con entry 2552 (R/R inválido 0.26).
    - **Solución**: 
        - Creada función reutilizable `ValidateRiskReward()` (línea 2251)
        - Confirmaciones SHORT/LONG ahora usan esta función
        - **Validación continua**: Cada bar mientras orden está en `workingOrder`, re-calcula R/R con precios actuales
        - Si R/R cae debajo de 1:1, **cancela automáticamente** la orden limit
        - Log: "R/R Invalidated While Working. Risk: X Reward: Y Ratio: Z - Cancelling Order"
    - **Código modificado**: Líneas 1595-1610 (SHORT), 1677-1692 (LONG), 1784-1806 (monitoreo continuo).

## [1.7.27] - 2025-12-25
### Corrección Crítica (Validación R/R)
- **Validar R/R contra Target Más Cercano**:
    - **Problema**: La validación R/R usaba solo TP2 (nivel opuesto, más lejano) para calcular el reward. Esto permitía trades con R/R inválido en TP1, donde el primer target no recuperaba el riesgo (ejemplo: Entry 2552, TP1 2548.1, TP2 2535.8, SL 2567 → R/R para TP1 = 0.26 ❌, pero para TP2 = 1.08 ✅).
    - **Impacto**: Strategy aceptaba trades que solo eran rentables si llegaban a TP2, sin garantizar recuperación de riesgo en TP1 (50% de la posición).
    - **Solución**: 
        - SHORT: Calcula ambos targets (TP1 VWAP, TP2 Nivel), usa `Math.Max()` para obtener el más cercano (precio más alto = más cerca)
        - LONG: Calcula ambos targets (TP1 VWAP, TP2 Nivel), usa `Math.Min()` para obtener el más cercano (precio más bajo = más cerca)
        - Valida R/R contra el target más cercano (TP1), asegurando que el primer 50% recupere el riesgo
    - **Código modificado**: Líneas 1601-1617 (SHORT), líneas 1696-1710 (LONG).

## [1.7.26] - 2025-12-25
### Corrección Crítica (Reset de Contadores en SYNC)
- **Reset de `protectedTp1Qty` y `protectedTp2Qty` en Ruta SYNC**:
    - **Problema**: v1.7.24 agregó reset de contadores en cierre por ejecución (líneas 2510-2511), pero NO en la ruta SYNC (línea 1350). Cuando una posición se cierra por sincronización (ej: OrderState diferente al esperado), los contadores no se limpiaban, causando que el siguiente trade asignara TODOS los contratos a TP2 en lugar de dividir.
    - **Ejemplo del bug**: Trade 1 termina con SYNC reset → `protectedTp1Qty = 1`. Trade 2 calcula `neededTp1 = 1 - 1 = 0` → `ForTP1=0, ForTP2=2`.
    - **Solución**: Agregado reset de contadores en ruta SYNC (líneas 1353-1356).
    - **Resultado**: Todos los cierres ahora resetean correctamente los contadores.

## [1.7.25] - 2025-12-25
### Cambio Menor (Logs de Auditoría)
- **Protección de Logs de Trigger Detection**:
    - Los logs "DEBUG: Trigger Short/Long Detected" ahora están protegidos con `if (EnableDebugLogs)` (líneas 1486, 1519).
    - Con `EnableDebugLogs = false`, solo se muestran logs de auditoría esenciales (orden submissions, fills, cierres).
    - Con `EnableDebugLogs = true`, se muestran todos los logs de debugging (búsquedas, targets, triggers).

## [1.7.24] - 2025-12-25
### Corrección Crítica (Contadores de Protección)
- **Reset de Contadores `protectedTp1Qty` y `protectedTp2Qty`**:
    - **Problema**: Los contadores de protección no se reseteaban al cerrar una posición, acumulándose entre trades. En el segundo trade, `protectedTp1Qty` todavía contenía el valor del trade anterior (ej: 1), haciendo que la lógica de asignación calculara `neededTp1 = totalTp1Target - protectedTp1Qty = 1 - 1 = 0`, resultando en que **todos los contratos** se asignaran a TP2 en lugar de dividirse.
    - **Síntoma**: Segundo trade y subsecuentes tenían ambos contratos en TP2 (logs: `ForTP1=0 (Need:0) | ForTP2=1` para ambas ejecuciones).
    - **Solución**: Agregado `protectedTp1Qty = 0` y `protectedTp2Qty = 0` en el reset de posición (líneas 2510-2511).
    - **Resultado**: Cada trade nuevo divide correctamente los contratos entre TP1 y TP2.

## [1.7.23] - 2025-12-25
### Corrección Crítica (Cache de Nivel Opuesto)
- **Limpieza de Cache en Triggers**:
    - **Problema ROOT CAUSE**: La variable `cachedOppositeLevel` no se limpiaba al detectar un nuevo trigger, causando que `GetOppositeLevelPrice` devolviera un nivel opuesto de un trigger anterior (posiblemente de otro día) sin ejecutar la búsqueda fresca. Resultado: `Reward: 0.00` y trades siempre rechazados.
    - **Diagnóstico**: Los logs de `SEARCH_OPPOSITE` no aparecían porque la función retornaba el cache inmediatamente en la línea `if (cachedOppositeLevel != null) return cachedOppositeLevel.Price;`.
    - **Solución**: Se agregó `cachedOppositeLevel = null` en ambos triggers (SHORT línea 1499, LONG línea 1532), junto con `validatedTargetPrice = 0`.
    - **Resultado**: Búsqueda fresca del nivel opuesto en cada nuevo trigger. Estrategia ahora ejecuta trades correctamente.

## [1.7.22] - 2025-12-25
### Corrección Crítica (Búsqueda de Niveles Opuestos)
- **Lógica Correcta: Nivel Opuesto del Mismo Día (Date Match)**:
    - **Problema Original**: La función tenía filtros arbitrarios (16h/72h) agregados por otra IA que bloqueaban niveles antiguos válidos.
    - **Iteración 1**: Se eliminaron filtros, pero tomaba primer nivel sin validar misma sesión.
    - **Iteración 2**: Se comparó StartTime con tolerancia de 1 hora, PERO falla porque High y Low se forman en diferentes horas del mismo día (ej: High a 4PM, Low a 11AM = 5h diferencia).
    - **Solución Final**: Comparación por **fecha del día** (`StartTime.Date`) sin importar la hora. USA High del viernes 12 busca USA Low del viernes 12, sin importar si el High fue a las 4PM y el Low a las 11AM.
    - **Resultado**: Rotación correcta de zonas del mismo día calendario. Puede operar niveles antiguos (días atrás), pero garantiza que High y Low pertenezcan al mismo día.

## [1.7.21] - 2025-12-25
### Corrección Crítica (Lógica de Stops y Targets)
- **Stop Loss Fijo a 1 Tick**:
    - **Problema**: Existía una inconsistencia donde la validación R/R usaba `setupAnchorPrice ± 1 tick`, pero la ejecución real usaba el parámetro configurable `StopLossTicks`, causando discrepancia entre el riesgo calculado y el riesgo real.
    - **Solución**: El Stop Loss ahora se coloca **siempre** a 1 tick del anchor (encima del high para SHORT, debajo del low para LONG), consistente con la lógica de confirmación.
    - **Resultado**: Cálculo de R/R honesto y predecible. El SL protege el extremo de la vela que ancló el VWAP.
- **Targets con Asignación Fija**:
    - **Problema**: La lógica anterior asignaba TP1 al target "más cercano" y TP2 al "más lejano" (sorting por distancia), lo cual funcionaba en casos normales pero fallaba en niveles internos donde el nivel opuesto estaba más cerca que el VWAP.
    - **Solución**: Se eliminó el sorting por distancia. Ahora:
        - **TP1 = VWAP Opuesto Global** (dinámico, se actualiza en tiempo real)
        - **TP2 = Nivel de Sesión Opuesto** (fijo, del mismo día que causó el trigger)
    - **Beneficio en Niveles Internos**: Si el nivel opuesto está más cerca que el VWAP, TP2 se llena primero. Gracias a los OCO groups separados, esto cierra solo su bucket y TP1 sigue trabajando hacia el VWAP más lejano.
    - **Archivos modificados**: `SubmitProtectionOrders` (líneas 1963-1970) y `ManagePositionExit` (líneas 2286-2295).
- **Validación**: Se confirmó que `validatedTargetPrice` se captura correctamente para mantener TP2 fijo post-gap.

## [1.7.20] - 2025-12-25
### Corrección Técnica (Cache Fix)
- **Limpieza de Caché de Target (`cachedOppositeLevel`)**:
    - **Problema**: Se identificó que la estrategia "recordaba" targets de operaciones anteriores (Valores Zombis, ej. 58.99). Al detectar un nuevo setup (ej. Short), reutilizaba ese valor inmediatamente sin validar si correspondía a la nueva sesión, provocando targets erróneos e ilógicos.
    - **Solución**: Se forzó la limpieza de esta variable (`cachedOppositeLevel = null`) cada vez que se dispara un nuevo Trigger.
    - **Impacto**: Esto garantiza que la estrategia recalcule el target basándose puramente en los niveles de la sesión actual (o la referenciada), sin mezclar datos antiguos. Respeta estrictamente las reglas de selección de niveles sin alterarlas.

## [1.7.19-Patch] - 2025-12-25
### Corrección de Reglas (Validación de Target)
- **Mejora en `GetOppositeLevelPrice`**:
    - **Problema**: La estrategia seleccionaba un "Nivel Opuesto" incorrecto (antiguo o post-gap) que resultaba no rentable (ej. Target Short > Entrada).
    - **Solución (User Request)**: Se modificó la función de búsqueda para aceptar el precio y dirección de referencia.
    - **Lógica**: Ahora, al buscar el nivel opuesto, el sistema verifica que cumpla la geometría básica:
        - Si estoy vendiendo en un High, el nivel opuesto (Low) DEBE ser menor que mi entrada.
        - Si no cumple, el sistema ignora ese candidato "falso" y sigue buscando o devuelve 0.
    - **Resultado**: Prioriza encontrar el nivel *correcto* de la sesión que permita una operación rentable, respetando la regla de operar niveles antiguos.

## [1.7.19] - 2025-12-25
### Hotfix Visual
- **R/R Short Fix Definitivo**: Se verificó y corrigió el bloque de lógica para entradas en Corto que no había aceptado el parche anterior.
- **Validación Visual**: Incremento de versión para confirmar recompilación exitosa en pantalla del usuario.

## [1.7.18] - 2025-12-25
### Corrección Crítica
- **Dirección de Target (R/R Fix)**: Se agregó validación estricta en el cálculo de Riesgo/Recompensa.
    - **Problema**: El uso de `Math.Abs` permitía validar trades con targets invertidos (ej. Short con Target por encima de la entrada), que luego resultaban en ejecuciones defectuosas usando fallbacks cortos.
    - **Solución**: Ahora se fuerza a que el Target sea menor a la entrada (Short) o mayor (Long). Si no cumple, la recompensa es 0 y el setup se descarta (o espera un mejor target).

## [1.7.17] - 2025-12-25
### Cambio Arquitectónico
- **Consolidated Entry (Entrada Unificada)**
    - **Problema**: Al dividir la entrada en 2 órdenes ("Split Entry"), a veces solo se llenaba 1, o el Breakeven fallaba porque la segunda orden no existía.
    - **Solución**: Ahora se envía **UNA sola orden** de entrada por la cantidad total (ej. 2 contratos).
    - **Protección Dinámica**: Una vez que la orden se llena (total o parcialmente), la estrategia divide *automáticamente* la protección en 2 grupos (TP1/SL1 y TP2/SL2).
    - **Odd Logic (Impares)**: Si la cantidad es impar (ej. 7), se prioriza TP1 (4 contratos) para reducir riesgo rápido, dejando el resto a TP2 (3 contratos).
    - **Resultado**: Garantiza que si el precio toca, entran todos los contratos juntos (o ninguno), eliminando errores de "Stop Perdido".

### Correcciones
- **Validación de Targets ("Stale Targets")**:
    - **Problema**: Las órdenes de Take Profit salían prematuramente en precios ilógicos (ej. TP1 encima de la entrada en Short).
    - **Causa**: La variable `validatedTargetPrice` retenía valores antiguos de trades previos y tenía prioridad absoluta, sobrescribiendo el cálculo correcto del VWAP actual.
    - **Solución**: Se implementó la limpieza obligatoria de `validatedTargetPrice = 0` al detectarse un nuevo Trigger y al cerrar operaciones. Además, se añadieron logs "FORCE TARGET" para auditar cuándo se usa esta variable.
- **Visibilidad de Propiedades**:
    - Se restauró la propiedad visual `EntriesPerDirection` en el panel de propiedades para mayor claridad del usuario, aunque lógica unmanaged la ignore internamente.
- **Limpieza de Código**:
    - Remoción total de bloques comentados y lógica obsoleta de versiones anteriores (Split Entry Legacy) para mejorar mantenibilidad.

## [1.7.16] - 2025-12-25
### Corrección Crítica
- **Persistencia de Target**: Se soluciona la discrepancia entre la validación de entrada y la colocación de TP.
    - Problema: `ManageEntry` validaba un trade con un target correcto, pero milisegundos después, `EnsureProtection` recalculaba el target y a veces encontraba un nivel diferente (inválido), causando cierre inmediato.
    - Solución: Ahora la estrategia guarda en una variable interna (`validatedTargetPrice`) el precio exacto del target usado para aprobar la entrada, y `EnsureProtection` está obligado a usar ese mismo precio.
    - Resultado: Cohesión total entre Entrada y Salida.

## [1.7.15] - 2025-12-25
### Corregido
- **Validación de Targets (Anti-Instant Exit)**: Se detectó que a veces el "Nivel Opuesto" calculado pertenecía a una sesión anterior o futura con un precio ilógico para la operación actual (ej. TP Short por ENCIMA de la entrada), causando una ejecución inmediata.
    - **Solución**: Ahora `EnsureProtection` valida matemáticamente el target.
    - **Fallback**: Si el Target y el VWAP Global son inválidos (precio negativo o dirección incorrecta), se asigna automáticamente un Target de Seguridad a una distancia de `StopTicks * 2` (Ratio 1:2) para mantener la estructura del trade.

## [1.7.14] - 2025-12-25
### Optimización
- **Lógica de Impares (Risk Reduction)**: Ajuste para cantidades impares.
    - REGLA: La "Orden 1" (TP1, salida rápida) siempre lleva la carga mayor o igual.
    - Fórmula: `qty1 = (Total + 1) / 2`.
    - Ejemplo: Quantity 5 -> **3** contratos a TP1 (para asegurar ganancia rápido) y **2** contratos a TP2 (correr).
    - Ejemplo: Quantity 3 -> **2** a TP1, **1** a TP2.

## [1.7.13] - 2025-12-25
### Corrección Conceptual
- **Restauración de Lógica Split (50/50)**: Se revierte a la lógica original de división, bajo confirmación estricta del usuario de que `Quantity` representa la **Exposición Total**.
    - Ejemplo: Si `Quantity = 10` -> Orden1 (5 contratos) + Orden2 (5 contratos).
    - Ejemplo: Si `Quantity = 2` -> Orden1 (1 contrato) + Orden2 (1 contrato).
    - Esto asegura que el "Total de Contratos" en mercado coincida exactamente con el número que el usuario escribe en el panel.

## [1.7.12] - 2025-12-25
### Cambio Lógico
- **Cantidad por Pata (User Request)**: Se modificó la lógica de gestión de capital.
    - Antes: `Quantity` = Total de la posición (se dividía entre 2 para el Split).
    - Ahora: `Quantity` = Cantidad por cada orden del Split.
    - Ejemplo: Si `Quantity = 2`, ahora el sistema abre 2 contratos para TP1 y 2 contratos para TP2 (Total Expuesto = 4).

## [1.7.11] - 2025-12-25
### Optimización
- **Protección en Recargas (Safer Cleanup)**: Se eliminó la cancelación de órdenes al "Terminar" la estrategia.
    - Ahora, al recargar (F5 o cambiar Propiedades), las órdenes de Stop Loss antiguas se mantienen vivas hasta que la nueva instancia arranca y toma el control. Esto evita el momento de peligro ("Naked Position") donde la posición quedaba huérfana de protección durante unos segundos.
- **Clarificación de Lógica**: (Nota de Uso) `Quantity` define el total de contratos de la posición. Si se usa Split (2 entradas), la cantidad se divide (Total 2 = 1 + 1).

## [1.7.10] - 2025-12-25
### Corregido
- **Eliminación de Re-Entrada Histórica (v1.7.10)**: Se bloqueó la ejecución de entradas si el estado no es `State.Realtime`.
    - Al usar `StartBehavior.ImmediatelySubmit` (necesario para el autocleanup), la estrategia tendía a "ejecutar" la última señal del historial al cargar, creando órdenes nuevas indeseadas. Ahora solo entra si la señal se genera en vivo.
- **Log Limpio**: Se redujo la alerta de "Ghost Order" a un mensaje interno para no saturar el log visual, ya que es un fallo esperado de NinjaTrader con órdenes huérfanas.

## [1.7.9] - 2025-12-25
### Corregido
- **Crash por "Ghost Order" (v1.7.9)**: Se protegió la llamada `CancelOrder` dentro de la limpieza inicial con un bloque `try-catch`.
    - Al recargar la estrategia, las órdenes "pendientes" de la instancia anterior no pueden ser canceladas por código (pertenecen a otro ID de estrategia), lo que causaba un error crítico y detenía la estrategia.
    - Ahora, el error se captura silenciosamente: la estrategia **SÍ ejecuta** el cierre de posición (Flatten) y continúa funcionando, aunque la orden visual antigua deba cancelarse manualmente.

## [1.7.8] - 2025-12-25
### Corregido
- **Zombie Cleanup (Cuenta Real)**: Se actualizó la lógica de limpieza inicial.
    - Ahora inspecciona `Account.Positions` en lugar de la posición interna de la estrategia (que siempre inicia plana). Esto detecta y cierra posiciones zombis verdaderas que hayan quedado en el Broker/Simulador.
- **Configuración de Inicio**: Se cambió `StartBehavior` a `ImmediatelySubmit`.
    - Anteriormente `WaitUntilFlat` impedía que la estrategia arrancara si había una posición zombi, bloqueando la cura. Ahora arranca inmediatamente para poder ejecutar la limpieza.

## [1.7.7] - 2025-12-25
### Corregido
- **Error Runtime 'State.Transition'**: Se corrigió el error crítico que impedía activar la estrategia ("SubmitOrderUnmanaged can't be called in Transition").
    - La lógica de limpieza (v1.7.5/v1.7.6) se movió de `OnStateChange` a `OnBarUpdate`.
    - Ahora se ejecuta exactamente una vez al detectar el primer tick de `State.Realtime`, asegurando que el motor de órdenes esté listo para cancelar y cerrar posiciones.

## [1.7.6] - 2025-12-25
### Corregido
- **Cleanup Failsafe (Limpieza Total de Órdenes)**:
    - **Al Desactivar (Terminated)**: Cuando se apaga la estrategia, ahora se iteran y cancelan explícitamente todas las órdenes activas en la cuenta para ese instrumento.
    - **Al Iniciar (Transition)**: Al arrancar (ej. Playback), se realiza un chequeo adicional para cancelar cualquier orden `Pending` o `Working` que haya quedado "pegada" de la sesión anterior, además de cerrar posiciones.
    - Esto elimina los residuos visuales ("Cancel Pending") y bloqueos al reiniciar Playback.

## [1.7.5] - 2025-12-25
### Corregido
- **Transition Failsafe (Zombie Fix Final)**: Se añadió una comprobación en `OnStateChange` (`State.Transition`).
    - Si al terminar de calcular los datos históricos (al cargar la estrategia o iniciar Playback) todavía hay una posición abierta ("Zombie" del día anterior), se cierra forzosamente antes de pasar a Tiempo Real.
    - Esto soluciona definitivamente el problema de órdenes activas residuales al iniciar Playback a las 7 PM.

## [1.7.4] - 2025-12-25
### Corregido
- **No Market Data (Cierre de Sesión Manual)**: Se implementó la función `CheckSessionExit()`.
    - Fuerza el cierre de posiciones y cancelación de órdenes desde 30 segundos antes de `USAEndTime` hasta 5 minutos después.
    - Captura la vela exacta de cierre (ej. 16:00:00) y cualquier estado residual post-cierre.

## [1.7.2] - 2025-12-25
### Corregido
- **Regresión "No Market Data" (Exit on Session Close)**: Se solucionó un problema de "Posición Zombi" específico del cierre de sesión.
    - Se añadió "Exit on session close" a la lista de disparadores de reinicio en `OnExecutionUpdate`.
    - **Limpieza de Órdenes Huérfanas**: Al detectar el cierre de sesión o un reinicio forzado, la estrategia ahora cancela explícitamente cualquier orden activa (TP/SL/Entry) antes de perder su referencia.
- **Optimización de Rendimiento (MES)**: Se implementó caché de horarios (`TimeSpan`) para evitar analizar cadenas de texto millones de veces por sesión. Esto soluciona los tiempos de espera (timeouts) al cargar datos históricos de alto volumen como MES.


## [1.7.3] - 2025-12-25
### Optimizado
- **Caché de TP Dinámico**: Se implementó caché para `GetOppositeLevelPrice` ("Nivel Opuesto"). En lugar de buscar en bucle en cada tick, ahora se busca una sola vez al inicio de la operación.
- **Stop Loss Buffer**: Se añadió un margen de seguridad de 2 ticks al verificar el precio del Stop Loss para evitar rechazos de orden ("Invalid Price") en mercados rápidos como MES.
- **Forzado Off Debug Logs**: Se desactiva explícitamente `EnableDebugLogs` al cargar para asegurar un rendimiento óptimo.

## [1.7.2] - 2025-12-25
### Corregido
- **Zombie Positions (Rechazos Parciales)**: Se corrigió un error crítico donde la estrategia reiniciaba el estado a `Idle` si una orden era rechazada, incluso si otra parte de la posición ya estaba activa. Ahora verifica explícitamente `Position.MarketPosition == Flat` antes de reiniciar.
- **Fallo en Breakeven (BE FAIL)**: Se fortaleció la lógica de búsqueda de órdenes en `OnExecutionUpdate`. Si la referencia a `entryOrder2` o `stopOrder2` se pierde, la estrategia ahora busca agresivamente en la colección `Orders` para recuperar el control y mover el Stop Loss correctamente.
- **Precio inválido de Stop Loss**: Se agregó validación en `EnsureProtection` para garantizar que los precios de Stop Loss (BuyToCover/Sell) estén siempre en el lado correcto del precio actual del mercado, evitando rechazos de órdenes por "Invalid Price".

## [1.7.0] - 2025-12-24
### Refactorización Mayor (Unmanaged)
- **Gestión de Órdenes No Gestionada (`IsUnmanaged = true`)**:
    - Se reescribió la lógica completa de entrada y salida para usar `SubmitOrderUnmanaged`.
    - Esto otorga control total sobre la vinculación de órdenes y resuelve definitivamente los conflictos de OCO y Breakeven en entradas divididas.
- **Backups**: Se creó backup de la versión Managed antes del cambio.
- **Grupo OCO Manual**:
    - Se implementó generación de IDs OCO únicos (`OCO_Short_1_[Ticks]`) para vincular explícitamente TP1 con SL1 y TP2 con SL2, garantizando independencia total.
- **Failsafes Migrados**:
    - Las protecciones de seguridad (Violación de Ancla, Zombie Check) ahora usan `ClosePositionUnmanaged` para cerrar posiciones de emergencia sin violar las reglas de modo Unmanaged.

## [1.6.5] - 2025-12-24
### Corregido
- **Bucle "Thrashing" de Órdenes**: Se relajaron las validaciones dinámicas de Riesgo/Recompensa y Violación de Ancla para órdenes que ya están trabajando.
    - Anteriormente, micro-fluctuaciones en el VWAP (R/R < 1) o en el Precio (tocar el Ancla) causaban la cancelación inmediata y un bucle infinito de re-entrada.
    - Ahora, la estrategia prioriza la estabilidad: si una orden está Activa ("Working"), NO será cancelada por estas comprobaciones dinámicas. Esperará al Mercado (Llenado o Stop Loss) o a un cambio de estado mayor.
- **Entrada Bloqueada (Límite 1 Contrato)**: Se incrementó `EntriesPerDirection` de 1 a 4.
    - La funcionalidad de "Entrada Dividida" (v1.6.4) envía 2 órdenes separadas. Como la estrategia es "Gestionada" (Managed), NinjaTrader bloqueaba la segunda orden porque el límite por defecto era 1. Al incrementarlo, se asegura que ambas partes de la entrada se envíen y llenen correctamente, respetando la Cantidad Total del usuario (ej. 2 Contratos).
- **Fallo en Breakeven**: Se restauró y adaptó la lógica de "Mover a Breakeven" para la nueva arquitectura de Entrada Dividida.
    - Cuando se llena el TP1, la estrategia identifica correctamente el Stop Loss restante (SL2) y lo mueve al precio de entrada de la posición restante (Entrada2).

## [1.6.4] - 2025-12-23
### Cambiado
- **Lógica de Entrada Dividida (OCO Robusto)**: Se refactorizó el mecanismo de entrada para enviar dos órdenes separadas de 1 contrato en lugar de una orden multicontrato. Esto asegura que NinjaTrader cree dos grupos OCO independientes (Entrada1+TP1+SL1 y Entrada2+TP2+SL2), eliminando por completo el riesgo de posiciones "huérfanas" o cancelaciones no deseadas.

## [1.6.3] - 2025-12-23
### Corregido
- **Conflicto OCO en Salidas**: Se desacoplaron las órdenes Take Profit del grupo OCO de la Señal de Entrada. Anteriormente, al llenarse el TP1, NinjaTrader cancelaba erróneamente el TP2 (debido a la lógica auto-OCO para órdenes con la misma señal). Ahora, los TPs son independientes y la reducción del Stop Loss se maneja manualmente por la lógica interna de la estrategia.

## [1.6.2] - 2025-12-23
### Corregido
- **Estabilidad de TP Dinámico**: Se corrigió una regresión donde los Take Profits volvían a fusionarse en el VWAP poco después de la entrada. La lógica de actualización dinámica ahora usa correctamente el `setupLevelTime` almacenado en lugar de la hora actual, asegurando que el nivel opuesto se encuentre consistentemente incluso durante grandes gaps temporales (ej. fines de semana).

## [1.6.1] - 2025-12-23
### Cambiado
- **Reversión de Visuales**: Se eliminaron las etiquetas de texto y la visualización de antigüedad añadidas recientemente (v1.5.9/v1.6.0) a petición del usuario para mantener la apariencia limpia del gráfico original.

## [1.6.0] - 2025-12-23
### Agregado
- **Apilamiento Inteligente de Etiquetas**: Se portó la lógica "Anti-Colisión" del indicador RelativeVwap. Las etiquetas de los niveles ahora se apilarán verticalmente (hacia arriba para Highs, hacia abajo para Lows) para evitar superposiciones cuando hay muchos niveles juntos.

## [1.5.9] - 2025-12-23
### Agregado
- **Antigüedad de Niveles**: Se agregaron etiquetas de texto gris a los niveles de sesión mostrando su antigüedad (ej. "(6d)" o "(12h)"), facilitando la identificación de la relevancia de niveles antiguos.

## [1.5.8] - 2025-12-23
### Corregido
- **Tiempo de Referencia de Nivel Opuesto**: Se corrigió un error donde los Take Profits se duplicaban (ambos al VWAP) durante gaps de fin de semana. La estrategia ahora rastrea el `setupLevelTime` original y lo usa para encontrar el nivel opuesto, en lugar de usar la hora actual que podría tener un desfase >48h.

## [1.5.7] - 2025-12-23
### Corregido
- **Sincronización Robusta SL**: Se eliminó la verificación de `OrderState.Working` para la sincronización del Stop Loss. Ahora, si la cantidad del SL es menor que la posición, se fuerza la actualización inmediatamente, solucionando condiciones de carrera en playback rápido.

## [1.5.6] - 2025-12-23
### Corregido
- **Sincronización Stop Loss**: Se añadió una validación para actualizar automáticamente la cantidad de contratos del Stop Loss si no coinciden con la Posición total (soluciona problemas con entradas parciales).

## [1.5.5] - 2025-12-23
### Cambiado
- **Persistencia Deshabilitada**: Se desactivaron `LoadLevels` y `SaveLevels`. La estrategia ahora depende totalmente del historial cargado en el gráfico, garantizando una sincronización perfecta y eliminando artefactos visuales en playback.

## [1.5.4] - 2025-12-23
### Corregido
- **Niveles Duplicados**: Se implementó una "búsqueda difusa" (fuzzy matching) para fusionar niveles restaurados con los nuevos, evitando que aparezcan líneas dobles cuando los tiempos difieren por milisegundos.

## [1.5.3] - 2025-12-23
### Cambiado
- **Manejo de Gaps**: Niveles antiguos (más de 12h del inicio del gráfico) son filtrados para evitar líneas erróneas.
### Agregado
- **Alerta de Historial**: Aviso rojo en el gráfico cuando hay niveles ocultos, indicando al usuario que cargue más días.

## [1.5.2] - 2025-12-23
### Corregido
- **Error de Validación**: Se arregló el error "Quantity is 0" asignando un valor por defecto de 1.

## [1.5.1] - 2025-12-23
### Agregado
- **Cauduras Locales**: Se agregó `EnableLocalScreenshots` para permitir guardar imágenes del gráfico en el disco sin necesidad de activar alertas por correo.
### Cambiado
- **Versión de Estrategia**: Actualizada a v1.5.1.

## [1.5.0] - 2025-12-23
### Agregado
- **Actualizaciones Dinámicas de TP**: Las órdenes objetivo (TP1/TP2) ahora ajustan su precio automáticamente para seguir al VWAP Global y a los Niveles de Sesión Opuestos si estos se mueven mientras la orden está trabajando.
- **Rastreo de Versiones**: Se agregó `CHANGELOG.md` (y `CHANGELOG_ES.md`) y visualización explícita de la versión en el panel del gráfico.
### Cambiado
- Se refactorizó `ManagePositionExit` para soportar actualizaciones dinámicas de precios para órdenes activas.

## [1.4.0] - 2025-12-23
### Agregado
- **Soporte Multi-Contrato**: Lógica para dividir la posición en TP1 (Más cercano) y TP2 (Más lejano).
- **Protección Inteligente**: El Stop Loss se mueve a Breakeven cuando se llena el TP1.
### Corregido
- Se corrigieron problemas con órdenes huérfanas donde los stops no se asociaban correctamente con la cantidad restante de la posición.
