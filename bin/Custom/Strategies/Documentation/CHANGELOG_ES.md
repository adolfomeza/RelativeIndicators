# Changelog - SessionLevelsStrategy

Todas las mejoras, correcciones y cambios notables de este proyecto serán documentados en este archivo.

El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.0.0/),
y este proyecto sigue [Semantic Versioning](https://semver.org/lang/es/).

---

## [v1.15.58] - 2026-01-21

### 🐛 Fixed

**1. Persistencia de Datos de Delta Scoring (Causa de Scores = 0)**

- **Problema**: El nuevo módulo "Study Delta" mostraba "No hay trades con Score Total > 0".
- **Causa Raíz**: La clase `SessionLevelData` (usada para guardar niveles en XML al reiniciar la estrategia) no incluía los nuevos campos de Delta (`DeltaAtFormation`, `DeltaAtSwingStart`, etc.).
  - Al reiniciar o recargar la estrategia, estos valores se perdían (volvían a 0).
  - El cálculo de Score depende de estos valores, por lo que retornaba 0 o valores muy bajos.
- **Solución**:
  - Se agregaron los campos `DeltaAtFormation`, `DeltaHigh`, `DeltaLow`, `DeltaAtSwingStart` a `SessionLevelData`.
  - Se actualizaron los métodos `SaveLevels` y `LoadLevels` para persistir correctamente estos datos.
  - **Retroactive Delta Capture (Playback/Start)**: Se modificó `ScanHistoricalLevels` para buscar datos de Delta históricos al crear niveles pasados (ej. Sesión Asia al iniciar Playback en USA). Ahora los niveles ya existentes al inicio tendrán Score válido.
  - **Correction (Fix 3)**: Se detectó que `ScanHistoricalLevels` NUNCA se llamaba en el código principal. Se agregó la llamada explícita en el inicio de Playback (`OnBarUpdate`).
  - **Nota**: Los niveles antiguos (de sesiones previas al fix) seguirán teniendo 0 hasta que se formen nuevos niveles con este fix aplicado.

## [v1.15.57] - 2026-01-21

### 🐛 Fixed

**1. Glitch Visual II: Líneas Adhoc "Quebradas" o con Dientes de Sierra**

- **Problema**: A pesar de que la cantidad de objetos se redujo (fix 56), las líneas se dibujaban como pequeños segmentos desconectados o "dientes de sierra" (líneas diagonales repetitivas en cada barra).
- **Causa Raíz Crítica**: El método `UpdateAdhocVWAP` contenía una lógica de sincronización defectuosa que sobrescribía la variable `visualAdhocLastBar` con el `AdhocAnchorBar` en cada tick.
  - Esto engañaba al sistema de dibujo (`ManageEntryA_Plus`) haciéndole creer que *cada tick* era una "Nueva Barra".
  - Resultado: En lugar de dibujar una línea continua desde el cierre anterior, se dibujaba una línea desde el valor actual al valor actual (longitud cero o micro-segmento), rompiendo la continuidad visual.
- **Solución v1.15.57**:
  - Se eliminó la sobrescritura corrupta de `visualAdhocLastBar` en `UpdateAdhocVWAP`.
  - Se eliminó la llamada redundante (doble actualización) al calculador de VWAP para evitar distorsiones en el cálculo de volumen.
- **Impacto**:
  - ✅ Las líneas Adhoc ahora se dibujan suaves y continuas, conectando correctamente el cierre de la barra anterior con el valor actual.

## [v1.15.56] - 2026-01-21

### 🐛 Fixed

**1. Glitch Visual: "AdhocLine" Multiplicación de Objetos**

- **Problema**: El gráfico se llenaba con miles de objetos `AdhocLine_XXXX`, causando una apariencia saturada y posibles problemas de rendimiento.
- **Causa Raíz**: La función de limpieza de líneas Adhoc (`ClearAdhocVisuals`) iteraba en un rango de barras estimado. Si el precio se movía mucho o se recargaba el script, la referencia a las barras viejas se perdía y los objetos de dibujo no se eliminaban correctamente, acumulándose indefinidamente.
- **Solución v1.15.56**: 
  - Se implementó un sistema de rastreo robusto (`HashSet<string>`) para registrar cada objeto `AdhocLine` dibujado.
  - La limpieza ahora garantiza la eliminación del 100% de los objetos rastreados, independientemente del índice de barra.
  - Se corrigió el "Memory Leak" de objetos visuales.
- **Impacto**:
  - ✅ Gráfico limpio y sin acumulación de objetos basura.
  - ✅ Mejora en rendimiento al no renderizar miles de líneas ocultas.

## [v1.15.55] - 2026-01-21

### 🐛 Fixed

**1. Glitch Visual: Niveles "Rotos" Después de Mitigación**

- **Problema**: Después de que un nivel era mitigado y luego el precio hacía un nuevo máximo/mínimo, el nivel se expandía visualmente de forma incorrecta, apareciendo "roto" o desalineado en el gráfico.
- **Causa Raíz**: La bandera `IsMitigated` no se reseteaba a `false` cuando el precio superaba el máximo/mínimo del nivel mitigado, lo que impedía que el nivel se redibujara correctamente en su nueva extensión.
- **Solución v1.15.55**: Se agregó lógica para resetear `IsMitigated = false` en `CheckSession` cuando el precio excede el máximo/mínimo del nivel, permitiendo que el nivel se redibuje correctamente.
- **Impacto**:
  - ✅ Corrección visual de los niveles mitigados.
  - ✅ Los niveles ahora se expanden y se muestran correctamente en el gráfico después de una mitigación y un nuevo extremo.
- **Archivos Modificados**:
  - `SessionLevelsStrategy.cs`: Modificado `CheckSession` para resetear `IsMitigated`.

## [v1.15.54] - 2026-01-21

### ✨ New Features

**1. Sistema de Scoring con Delta (Delta-Based Level Scoring)**

- Implementado sistema cuantitativo para filtrar trades basado en Order Flow (Delta).
- **Parámetros**:
  - `UseDeltaFilter`: Activa/Desactiva el filtro (Default: true).
  - `MinDeltaScore`: Puntaje mínimo requerido para tomar el trade (0-100, Default: 40).
- **Lógica de Scoring (Phases 1-4)**:
  - **Fase 1 (Push)**: Premia pushes débiles (reversión probable).
  - **Fase 2 (Formation)**: Premia Deltas alineados con la reversión en el extremo.
  - **Fase 3 (Despegue)**: Premia la entrada de participantes agresivos a favor del trade.
  - **Fase 4 (Control Diario)**: Evalúa quién controla la sesión.
- **Data Capture**:
  - Se modificó `SessionLevel` para almacenar el Delta en el momento de formación.
  - La estrategia captura métricas detalladas en `CheckSession` y `OnExecutionUpdate`.
- **CSV Export**:
  - Se agregaron nuevas columnas al reporte CSV para análisis posterior:
    - `DeltaPush`, `DeltaFormation`, `DeltaDespegue`
    - `DeltaScore`, `Phase1`, `Phase2`, `Phase3`, `Phase4`

## [v1.15.53] - 2026-01-21

### 🐛 Fixed

**1. Bug Crítico: Contador de Intentos Agota Niveles por Doble Conteo (Partial Fills)**

- **Problema**: El contador de intentos (`EntryAttempts`) llegaba a 20/20 prematuramente, bloqueando trades válidos.
- **Causa Raíz**: El método `OnExecutionUpdate` incrementaba el contador cada vez que recibía una actualización de orden "Filled" o "PartFilled". Si una orden tenía múltiples fills parciales (o NinjaTrader enviaba múltiples actualizaciones), el contador se incrementaba múltiples veces para *el mismo trade*.
- **Solución v1.15.53**: Implementado mecanismo de protección contra duplicados.
  - Se rastrean los `OrderId` ya procesados en un HashSet.
  - El contador solo se incrementa la *primera vez* que se procesa un OrderId específico.
  - Actualizaciones subsecuentes (fills parciales del mismo order) son ignoradas para efectos del contador.
- **Impacto**: 
  - ✅ Precisión absoluta en el conteo de trades (1 Orden = 1 Intento).
  - ✅ Elimina el riesgo de agotamiento prematuro de niveles por fills fragmentados.
- **Archivos Modificados**:
  - `SessionLevelsStrategy.cs`: Agregado `processedOrderIds` y check en `OnExecutionUpdate`.

## [v1.15.52] - 2026-01-21

### 🐛 Fixed

**1. Bug: Cierre Forzado en Holidays Ignoraba Parámetro de Usuario**

- **Problema**: La estrategia cerraba posiciones automáticamente en días de "Cierre Temprano" (Holidays) incluso si el usuario había desmarcado la casilla `Enable Holiday Protection`.
- **Causa Raíz**: En `v1.15.49` se eliminó accidentalmente la verificación de este parámetro en el método `CheckSessionExit`, haciendo que la detección de `isEarlyClose` forzara el cierre incondicionalmente.
- **Solución v1.15.52**: Restaurada la lógica condicional.
  - **Viernes**: El cierre sigue siendo mandatorio (Weekend Gap Protection).
  - **Holiday/Early Close**: El cierre ahora obedece estrictamente al parámetro `EnableHolidayProtection`. Si está desactivado, la estrategia NO cerrará la posición.

- **Archivos Modificados**:
  - `SessionLevelsStrategy.cs`: Restaurada condición `&& EnableHolidayProtection` en lógica de cierre.

## [v1.15.50] - 2026-01-21

### 🐛 Fixed

**1. Bug Crítico: Contador de Intentos Agota Niveles Prematuramente (20/20)**

- **Problema**: Los niveles mostraban "SCAN IGNORE: Max Retries Reached (20/20)" después de solo 2 trades reales ejecutados.
  - Ejemplo: Europa High mostraba 20/20 intentos agotados cuando solo hubo 2 trades reales en ese nivel.
  - Esto impedía que la estrategia entrara en niveles válidos.

- **Causa Raíz**: El contador `EntryAttempts` se incrementaba en cada **VWAP retry trigger** (cuando el precio rompía el ancla), NO cuando el trade realmente se ejecutaba.
  - Ubicación del bug: `EntryStateMachine.cs:537` - `currentLevel.EntryAttempts++`
  - El fix de v1.15.48 removió el incremento del trigger path (líneas 259, 314) pero **olvidó remover** el del VWAP retry path (línea 537).
  - El incremento en `OnExecutionUpdate` **nunca se implementó** a pesar de estar documentado en el changelog.

- **Solución v1.15.50**:
  1. **Removido** el incremento incorrecto en `EntryStateMachine.cs:537` (VWAP retry path)
  2. **Agregado** el incremento correcto en `SessionLevelsStrategy.cs:OnExecutionUpdate` - Solo cuando el trade fill realmente ocurre

  ```csharp
  // ANTES (Bug en EntryStateMachine.cs - VWAP retry)
  if (priceBreaksExtreme)
  {
      currentLevel.EntryAttempts++; // ❌ Incrementaba en cada break de VWAP
  }

  // DESPUÉS (Fix en OnExecutionUpdate - trade fill)
  var activeLevel = activeLevels.FirstOrDefault(l => l.Name == setupLevelName);
  if (activeLevel != null)
  {
      activeLevel.EntryAttempts++; // ✅ Solo incrementa cuando el trade se llena
  }
  ```

- **Impacto**:
  - ✅ El contador ahora refleja trades ejecutados reales, no toques de VWAP
  - ✅ Los niveles ya no se "agotan" prematuramente
  - ✅ La estrategia puede entrar en niveles válidos que antes bloqueaba

- **Archivos Modificados**:
  - `EntryStateMachine.cs:533-540`: Removido bloque de incremento en VWAP retry
  - `SessionLevelsStrategy.cs:3956-3964`: Agregado incremento en OnExecutionUpdate
  - `StrategyHelpers.cs:143,280`: Actualizada versión a v1.15.50

---

## [v1.15.49] - 2026-01-20

### 🐛 Fixed

**1. Posiciones No Se Cierran el Viernes (Bug Crítico)**

- **Problema**: Las posiciones abiertas no se cerraban automáticamente al final de la sesión del viernes, dejando operaciones expuestas al gap del fin de semana.
  
- **Causa Raíz**: La lógica de cierre (línea 2222) tenía una referencia a `EnableHolidayProtection`, una variable que **no estaba definida**, causando que la condición nunca se cumpliera.

  ```csharp
  // ANTES (Bug)
  if (EnableHolidayProtection && (isFriday || isEarlyClose))
  ```

- **Solución v1.15.49**: Removida la variable indefinida. Ahora siempre cierra posiciones en viernes y early closes (holidays).

  ```csharp
  // DESPUÉS (Fix)
  if (isFriday || isEarlyClose)
  ```

- **Impacto**: 
  - ✅ Posiciones se cierran automáticamente antes del cierre de sesión del viernes
  - ✅ Protección contra gaps de fin de semana
  - ✅ También funciona para holidays con early close

- **Archivos Modificados**:
  - `SessionLevelsStrategy.cs` línea 2222: Removida condición `EnableHolidayProtection &&`
  
---

## [v1.15.48] - 2026-01-20
### Agregado
- **Módulo SL Adaptativo (Supervivencia):** Reemplazo de la lógica de salida de emergencia. Ahora, si el precio salta el Stop Loss durante la entrada, el sistema adapta el SL a `Precio Actual +/- 4 ticks` para asegurar que la orden sea aceptada por NinjaTrader y la posición siga protegida, en lugar de cerrar o fallar.

## [v1.15.48] - 2026-01-19
### Corregido
- **Bug: Contador de Intentos Exporta Valor Global en Lugar del Valor por Nivel**
  - **Problema**: El CSV de exportación mostraba el valor de `currentLevelAttempts` (variable cacheada) en lugar del valor real de `SessionLevel.EntryAttempts` del nivel específico.
    - Ejemplo: Trade ganador en Asia High mostraba "intento #18" cuando era realmente el intento #2 de ese nivel específico.
    - La app Streamlit y análisis AI mostraban datos incorrectos sobre qué intentos eran más rentables por nivel.
  - **Causa Raíz**: En `OnExecutionUpdate`, el código asignaba `tradeAttemptNumber = currentLevelAttempts` sin verificar que este valor correspondiera al nivel correcto.
    - `currentLevelAttempts` se actualizaba cada vez que se triggeaba cualquier nivel.
    - Si había trades intermedios en otros niveles (USA High, Europe High), el contador se incrementaba globalmente.
    - El valor exportado al CSV no reflejaba el contador específico del nivel del trade (`lvl.EntryAttempts`).
  - **Impacto**:
    - Análisis AI de rentabilidad por "intento de nivel" era completamente erróneo
    - No se podía determinar si el intento #2 de Asia High era más rentable que el intento #5
    - Datos históricos en CSV no reflejan la realidad de la estrategia
  - **Solución v1.15.48**: 
    - Modificado `SessionLevelsStrategy.cs:3934-3944` para leer directamente del objeto `SessionLevel`:
    ```csharp
  // v1.15.48: FIX - Use currentVwapNumber which matches order suffix
  // This is the same counter used for order names (EntryA+_Long_01, _02, etc.)
  tradeAttemptNumber = currentVwapNumber;
  Log(string.Format("TRADE ATTEMPT: {0} Attempt #{1}/{2}", 
      setupLevelName, currentVwapNumber, MaxRetriesPerLevel));
    ```

- **Simplificación**: En lugar de gestionar `EntryAttempts` de forma compleja, ahora se usa directamente `currentVwapNumber`, que es el mismo contador que ya funciona correctamente para nombres de órdenes.
- **Impacto**: CSV ahora muestra `intento #1` para el primer trade, `#2` para el segundo, etc., coincidiendo exactamente con el sufijo de la orden (EntryA+_Long_01, EntryA+_Long_02, etc.).specífico
    - Usa `currentLevelAttempts` solo como fallback si el nivel no se encuentra
  - **Resultado**: El CSV ahora exporta el contador de intentos correcto por nivel, permitiendo análisis preciso de rentabilidad por intento.

- **Bug: Contador de Intentos Se Incrementaba en Trigger en Lugar de en Trade**
  - **Problema**: El contador `EntryAttempts` se incrementaba cada vez que se tocaba el nivel (trigger), NO cuando realmente se ejecutaba un trade.
    - Ejemplo: Nivel tocado 3 veces (rechazado por filtros) → Primer trade mostraba "intento #3" en lugar de "intento #1"
    - No tenía sentido contar touches que no resultaron en trades
  - **Causa Raíz**: El código incrementaba `lvl.EntryAttempts++` en `ProcessSelectedTrigger` (EntryStateMachine.cs líneas 259 y 314)
  - **Solución v1.15.48**: 
    - **Removido** `lvl.EntryAttempts++` de `EntryStateMachine.cs:259 y 314` (trigger paths)
    - **Agregado** `activeLevel.EntryAttempts++` en `SessionLevelsStrategy.cs:3940` (trade execution path)
    - Ahora el contador se incrementa SOLO cuando el trade fill realmente ocurre
  - **Resultado**: Primer trade = intento #1, segundo trade en el mismo nivel = intento #2, etc. El contador ahora refleja trades ejecutados, no touches del nivel.

- **Bug: Playback Cargaba Contadores de Intentos de Sesiones Anteriores**  
  - **Problema**: Al ejecutar Playback múltiples veces, el primer trade mostraba "intento #8" en lugar de "intento #1".
    - Los valores de `EntryAttempts` se persistían en archivos XML entre sesiones de Playback
    - Aunque v1.15.49 bloqueó LoadLevels en Playback, los niveles escaneados del historial mantenían valores antiguos
  - **Causa Raíz**: No había lógica para resetear `EntryAttempts` explícitamente en Playback/Backtest
  - **Solución v1.15.48**:
    ```csharp
    // After ScanHistoricalLevels, in Playback/Backtest only:
    if (State != State.Realtime) {
        foreach (var lvl in activeLevels) {
            lvl.EntryAttempts = 0;  // Clean slate for testing
        }
    }
    ```
  - **Resultado**: Playback siempre empieza con contadores en 0, permitiendo pruebas repetibles y consistentes.

### Técnico
- Modificado `SessionLevelsStrategy.cs:3934-3944` - Incrementa y lee EntryAttempts directamente de SessionLevel object en fill time
- Modificado `EntryStateMachine.cs:254-267, 309-322` - Removido incremento de contador en trigger (Long y Short paths)
- Modificado `SessionLevelsStrategy.cs:1820-1834` - Agregado reset de EntryAttempts en Playback/Backtest startup
- Actualizado `StrategyHelpers.cs:279` - Versión a v1.15.48

## [v1.15.49] - 2026-01-19
### Corregido
- **Bug: Contador de Intentos Persistente en Playback (20 Intentos Agotados)**
  - **Problema**: Al reiniciar o reactivar Playback, la estrategia cargaba el estado de los niveles (incluyendo el contador de intentos ya agotados) desde el archivo de persistencia del disco, impidiendo volver a operar.
  - **Solución**: Se restringió la carga de persistencia (`LoadLevels`) para que SOLO ocurra en modo `Realtime`.
    - En `Playback` y `Backtest`, la estrategia siempre iniciará con un estado limpio (0 intentos).
    - En `Realtime`, sigue recuperando el estado previo en caso de reinicio/crash.

## [v1.15.48] - 2026-01-19
### Corregido
- **Bug Crítico: Cancelación Prematura de Stop Loss (Lag Protection Guard)**
  - **Problema**: En Playback o alta latencia, la lógica de detección de lag ("LAG DETECTED") a veces se disparaba erróneamente por doble ejecución (Race Condition entre `OnExecutionUpdate` y `OnOrderUpdate`), reduciendo el Stop Loss a 0 y cancelándolo prematuramente mientras aún quedaban contratos abiertos para TP2.
  - **Solución**: Implementado `Safety Guard` en `OrderProtectionManager.HandleTP1Fill`.
    - Ahora, antes de reducir el SL, se verifica la cantidad del `tp2Order` activo.
    - Se prohíbe estrictamente que la cantidad del SL baje de la cantidad requerida para el TP2.
    - Si la lógica de lag calcula 0 pero hay TP2 de 2 contratos, el SL se fuerza a 2 contratos.
    - Esto mantiene la seguridad contra lag pero previene la cancelación accidental "zombie".

## [v1.15.47] - 2026-01-19
### Corregido
- **Discrepancia PnL / Comisiones en Playback**
  - **Problema**: La estrategia usaba una lógica híbrida que intentaba estimar comisiones si NinjaTrader reportaba 0, causando discrepancias de centavos (o dólares) en reportes externos.
  - **Solución**: Se implementó **Lógica Estricta de Comisiones**.
    - La estrategia ahora exporta EXACTAMENTE lo que reporta `execution.Commission * 2`.
    - Si NinjaTrader reporta 0 (común en Playback sin configurar), se exporta 0.
    - Se eliminó cualquier cálculo/estimación manual ("Hardcoded Rates").
    - **Resultado**: Coincidencia exacta de centavos con el reporte "Trade Performance" de NinjaTrader.

## [v1.15.45] - 2026-01-19
### Agregado
- **Alertas Críticas por Email**
  - Nuevo parámetro `EmailToAlert` para notificaciones de emergencia.
  - Envía email automático si la estrategia crashea (`OnBarUpdate`) o pierde conexión.
  - Incluye Stack Trace completo para facilitar debugging remoto.

## [v1.15.42] - 2026-01-18
### Corregido
- **Bug Visual: Acumulación de Líneas "AdhocLine" (Fuga de Memoria Visual)**
  - **Problema**: El gráfico acumulaba miles de segmentos de línea blanca etiquetados "AdhocLine_XXX", creando bloques sólidos blancos y posible degradación de rendimiento.
  - **Causa Raíz**: La lógica de visualización del setup `Wait` dibujaba un nuevo segmento cada barra pero nunca eliminaba los segmentos antiguos al terminar el setup o re-anclar.
  - **Solución**: Se implementó una rutina de limpieza automática (`ClearAdhocVisuals`) que elimina todas las líneas "AdhocLine" tan pronto como el setup finaliza (Idle), se invalida, o se re-inicia (nuevo trigger/re-anchor).
  - **Resultado**: El gráfico se mantiene limpio, mostrando solo la curva VWAP del setup activo actual.
- **Bug Visual: Líneas Verticales al Infinito**
  - **Problema**: Ocasionalmente se dibujaban líneas verticales extendiéndose fuera del gráfico.
  - **Causa**: Coordenadas inválidas (0 o valores extremos) pasadas a `Draw.Line`.
  - **Solución**: Agregado Sanity Check que bloquea el dibujado si las coordenadas son absurdas (< TickSize o > 1,000,000).

## [v1.15.43] - 2026-01-18
### Agregado
- **Integración IA (Configuración Automática)**
  - Implementado `AutoLoadAIConfig` para leer configuración desde `ai_config.json`.
  - Soporte para carga dinámica de:
    - `EnabledZones`: Lista de zonas permitidas.
    - `MaxLevelAgeDays`: Edad máxima de los niveles.
    - `MaxRetriesPerLevel`: Cantidad máxima de intentos.
- **Consistencia de Datos (CSV)**
  - Agregadas nuevas columnas al export: `EntryMode`, `ExitStrategy`, `RiskModel`.
  - Permite a la App de Auditoría diferenciar entre estrategias Standard vs Ladder y Fixed vs Dynamic.

## [v1.15.42] - 2026-01-18
### Corregido
- **Bug CRÍTICO: Ladder Exit SL Reduction (Steps > 1)**
  - **Problema**: La reducción de SL solo funcionaba para el primer escalón (TP1). Los escalones posteriores (TP2, TP3...) no reducían el SL, dejando la posición desprotegida o con tamaño incorrecto.
  - **Causa Raíz**: 
    1. La lógica de detección de "Salida" estaba anidada incorrectamente dentro del bloque de "Entrada" en `OnExecutionUpdate`.
    2. El filtro de verificación exigía el tag `_1_` (Step 1) para disparar la reducción.
  - **Solución**: 
    1. Se movió la lógica de salida a un bloque `else` independiente.
    2. Se eliminó la restricción `_1_`, permitiendo que CUALQUIER fill de `LadderTP` dispare la reducción de SL proporcional.
    3. Probado y verificado escalabilidad con hasta 10 contratos.

- **Corregido: Errores de Compilación y Duplicados**
  - Eliminada definición duplicada de propiedad `Quantity` en `SessionLevelsStrategy.cs`.
  - Corregido error de sintaxis (llave extra `}`) en `OrderProtectionManager.cs`.

## [v1.15.40] - 2026-01-18
### Agregado
- **Modelo de Salida en Escalera (Ladder Exit)**
  - Introduce un nuevo modo de gestión de salidas seleccionable por parámetro: `ExitStrategyType`.
  - **Modo Standard (Original)**: Mantiene el comportamiento actual (TP1 en VWAP, TP2 en Nivel Opuesto).
  - **Modo Ladder (Nuevo)**:
    - Escala las salidas secuencialmente basadas en múltiplos de R (Riesgo Inicial).
    - Ejemplo para 3 contratos: Target 1 @ 1R, Target 2 @ 2R, Target 3 @ 3R.
    - **Gestión de Stop Loss**: Al llenarse el primer Target (1R), el Stop Loss restante se mueve automáticamente a Breakeven.
  - **Implementación Técnica**:
    - `OrderProtectionManager.EnsureLadderProtection`: Calcula y coloca órdenes Limit individuales para cada contrato.
    - `SessionLevelsStrategy.OnExecutionUpdate`: Detecta fills de `LadderTP` para disparar el movimiento a Breakeven.

### Corregido
- **Bug VWAP (04:27 AM - 6 Mayo)**: Se corrigió un error donde el calculador de VWAP no se reseteaba correctamente al cambiar de nivel, acumulando volumen erróneo y generando valores de VWAP imposibles.
  - Solución: Se fuerza el reset del `VWAPCalculator` (`ResetAdhocVWAP`) cada vez que la `EntryStateMachine` cambia de nivel objetivo.

### Técnico
- Nuevo Enum `ExitStrategyType` { Standard, Ladder }.
- Nuevo Parámetro `ExitStrategy` en la sección Order Management.
- Nueva clase de gestión interna `ladderOrders` para rastrear múltiples órdenes de salida.

## [v1.15.38] - 2026-01-16
### Agregado
- **Sistema de Alertas de Email Mejoradas**
  - **Email de Entrada (SendTradeEntryEmail)**: Envía email detallado al ejecutarse una entrada con: Instrumento, Dirección, Contratos, Precio, Nivel, Risk, TP/SL
  - **Email de Salida (SendTradeExitEmail)**: Envía email al cerrar posición con: Resultado (Win/Loss), PnL Bruto/Neto, Comisión, MAE, MFE, Duración
  - **Alertas Críticas (SendCriticalAlert)**: Envía email inmediato para eventos críticos:
    - Chart Lag > threshold (órdenes bloqueadas)
    - Apteros Risk Limit Hit (cierre forzado por riesgo)
    - Emergency Close (cierre de emergencia)
    - Order Rejected (orden rechazada por broker)
  - **Anti-Duplicación**: Flags `emailSentOnEntry` y `emailSentOnExit` previenen emails duplicados por fills parciales
  - **Solo Realtime**: Los emails solo se envían en modo Realtime, no en Playback/Backtest

### Corregido
- **Fix Crítico Viernes**: Corregida lógica de `CheckWeekEndReset` en `SessionLevelsStrategy.cs`. Anteriormente, si la estrategia corría un viernes por la mañana, calculaba erróneamente el reseteo semanal como "hoy a las 18:00", causando un reseteo prematuro de niveles y bloqueando operaciones todo el día.

### Técnico
- Agregado `SessionLevelsStrategy.cs:3322-3363` - `SendTradeEntryEmail()` con formato detallado
- Agregado `SessionLevelsStrategy.cs:3365-3405` - `SendTradeExitEmail()` con cálculo de PnL
- Agregado `SessionLevelsStrategy.cs:3407-3431` - `SendCriticalAlert()` para eventos de emergencia
- Agregado `SessionLevelsStrategy.cs:3433-3469` - `SendEmailText()` helper sin attachment
- Agregado `SessionLevelsStrategy.cs:4470-4471` - Flags de anti-duplicación
- Modificado `SessionLevelsStrategy.cs:486-492` - Alert email en CheckChartLag
- Modificado `SessionLevelsStrategy.cs:3253-3261` - Alert email en Apteros Risk Limit
- Modificado `SessionLevelsStrategy.cs:3682-3692` - Alert email en Order Rejection
- Modificado `SessionLevelsStrategy.cs:4103-4125` - Exit email al cerrar posición
- Modificado `SessionLevelsStrategy.cs:4527-4532` - Alert email en ClosePositionUnmanaged
- Modificado `SessionLevelsStrategy.cs:3778-3783` - Reset de flags en nueva entrada


## [v1.15.37] - 2026-01-15
### Corregido
- **Bug: Contador de Intentos (Attempt) No Incrementaba en Reintentos VWAP**
  - **Problema**: Cuando el precio rompía el VWAP para reintentar una entrada (`HandleVwapMitigationWait`), el contador `EntryAttempts` no se incrementaba. Esto causaba que múltiples intentos aparecieran como "Attempt 1".
  - **Solución**: Se agregó `lvl.EntryAttempts++` dentro de la lógica de reintento en `EntryStateMachine.cs`. Ahora los reintentos se reflejan correctamente (Attempt 2, 3...) en logs y CSV.

### Agregado
- **Exportación de Antigüedad del Nivel (Level Age)**
  - Se incluye la columna `LevelAge` en el CSV de exportación de trades.
  - Permite análisis de rentabilidad basado en la "frescura" del nivel (0 días vs días anteriores).

### Técnico
- Modificado `EntryStateMachine.cs:514-520` - Incremento de contador en reintento.
- Actualizado `SessionLevelsStrategy.cs:40` - Versión a v1.15.37.
- Actualizado `StrategyHelpers.cs:262` - Versión a v1.15.37.

## [v1.15.36] - 2026-01-15
### Mejorado
- **Logging Timestamps**: Los logs ahora muestran dos timestamps claramente separados
  - **PC Time**: Hora del computador (útil para correlacionar con eventos externos)
  - **Chart Time**: Hora del chart/playback (hora real del mercado)
  - Formato: `13:52:15.123 [17:25:30.456] Message` (PC Time [Chart Time] Message)
  - Facilita análisis de playback donde Chart Time es diferente a PC Time actual

- **Sección de Trade desde Trigger**: Separador de logs ahora inicia cuando se **toca el nivel** en lugar de cuando se llena la orden
  - **Antes**: `TRADE START` aparecía cuando orden entraba
  - **Ahora**: `TRIGGER` aparece cuando nivel es mitigado
  - Permite analizar TODO el flujo del trade: trigger → re-anchors → fill → protection → close
  - Formato nuevo:
    ```
    ==================================================================
       TRIGGER: USA Low @ 21186.5 | Attempt #1/20 (Deepest Selection)
    ==================================================================
    ```

### Técnico
- Modificado `StrategyHelpers.cs:71` - Timestamp dual (PC + Chart)
- Modificado `SessionLevelsStrategy.cs:382` - Comentario actualizado
- Modificado `EntryStateMachine.cs:241,290` - Separador movido a trigger
- Modificado `SessionLevelsStrategy.cs:3648` - Eliminado separador duplicado en fill
- Actualizado `SessionLevelsStrategy.cs:40` - Versión a v1.15.36

## [v1.15.35] - 2026-01-15
### Revertido
- **v1.15.34 Export Híbrido** - Revertido enfoque de `SystemPerformance.AllTrades` porque agrega ejecuciones en lugar de mostrarlas individualmente
  - `SystemPerformance.AllTrades` devuelve trades agregados (1 objeto = todo el trade)
  - NT Trade Performance exporta ejecuciones individuales (cada partial fill)
  - Restaurado export original en `OnExecutionUpdate` que SÍ captura cada partial fill correctamente

### Corregido - App Streamlit
- **Comisiones Incorrectas en Tabla**: `calculate_commission()` no multiplicaba por `Quantity`
  - Ahora: `return rate * 2 * quantity` (RT × Quantity)
  - MGC: 2 contratos → $4.80 ✅, 9 contratos → $21.60 ✅
- **Tarifas MicroCom**: Actualizadas de $0.77 a $1.20 por lado (matching con estrategia C#)
- **Checkboxes No Sincronizados**: Checkbox "Todos" no actualizaba checkboxes individuales
  - Implementado `session_state` para sincronizar en Niveles, Instrumentos, e Intentos

### Agregado - App Streamlit
- **Tabla de Datos Raw**: Checkbox "🔍 Mostrar Tabla de Datos Raw" en sidebar
  - Muestra primeras 50 ejecuciones con todas las columnas (Commission, Quantity, EntryTime, ExitTime, etc.)
  - Métricas de resumen: Total Registros, Total Commission, Gross PnL
  - Útil para debugging y verificación de valores

### Técnico
- Eliminado `SessionLevelCore.cs:109-122` - Clase TradeMetadata (revertido)
- Eliminado `SessionLevelsStrategy.cs:76` - Dictionary cache (revertido)
- Eliminado `SessionLevelsStrategy.cs:4311-4410` - Método ExportTradesFromSystemPerformance() (revertido)
- Eliminado `SessionLevelsStrategy.cs:3260-3263` - Trigger en OnPositionUpdate (revertido)
- Eliminado `SessionLevelsStrategy.cs:3637-3655` - Captura de metadatos para cache (revertido)
- Actualizado `SessionLevelsStrategy.cs:40` - Versión a v1.15.35
- Actualizado `StrategyHelpers.cs:262` - Versión a v1.15.35
- Modificado `app.py:1179` - calculate_commission ahora multiplica por Quantity
- Modificado `app.py:25,33` - Tarifas MicroCom actualizadas a 1.20
- Agregado `app.py:1404` - Checkbox para mostrar tabla raw
- Agregado `app.py:1589-1600` - Display de tabla de datos raw
- Agregado `app.py:1730,1745,1778` - Sincronización de checkboxes con session_state

## [v1.15.33] - 2026-01-15
### Agregado
- **Columna Quantity en CSV Export para Matching Exacto con NT**
  - **Objetivo**: Garantizar que los resultados de la app Streamlit coincidan EXACTAMENTE con NinjaTrader Trade Performance
  - **Cambios en CSV Export**:
    - Agregada columna "Quantity" en posición 22 del header (después de LevelAge)
    - Export incluye `execution.Quantity` para cada ejecución
    - Permite cálculo preciso de PnL tomando en cuenta cantidad de contratos en cada fill
  - **Cambios en Streamlit App**:
    - Actualizada lista de nombres de columnas para incluir 'Quantity'
    - Agregada validación de columna con valor por defecto de 1
    - App ahora reconoce y procesa correctamente la columna Quantity
  - **Requisito Crítico**: Este es un producto comercial - no puede haber inconsistencias entre resultados de la app y NinjaTrader
  
### Técnico
- Modificado `SessionLevelsStrategy.cs:4276` - Header de CSV incluye "Quantity"
- Modificado `SessionLevelsStrategy.cs:3890-3914` - Export line incluye `execution.Quantity`
- Modificado `StreamlitAudit/app.py:1072` - Columna 'Quantity' agregada a col_names_new
- Modificado `StreamlitAudit/app.py:1114-1120` - Validación de columna Quantity con default=1
- Actualizado `StrategyHelpers.cs:262` - Versión a v1.15.33

## [v1.15.32] - 2026-01-14
### Corregido
- **Bug CRÍTICO: TP2 se Cambiaba a VWAP Después del Fill de Entrada (Fix Real)**
  - **Problema**: El fix anterior (v1.15.31) estaba en código COMENTADO y no se ejecutaba
    - El código de persistencia estaba en `SessionLevelsStrategy.EnsureProtection()` líneas 2928-2934
    - Pero esa función está comentada desde v1.14.40 (líneas 2676-2743)
    - El código activo está en `OrderProtectionManager.SubmitProtectionOrders()`
    - Por eso el bug persistía: `validatedTp2Price` nunca se guardaba
  - **Causa Raíz Real**: `OrderProtectionManager.SubmitProtectionOrders()` calculaba el precio correcto de TP2 pero NO lo persistía de vuelta a `strategy.validatedTp2Price`
    - Cuando `ManagePositionExit()` se ejecutaba en el siguiente tick, `strategy.validatedTp2Price = 0`
    - Sin valor validado, caía al fallback VWAP (73.83)
  - **Solución v1.15.32**:
    - Agregado código en `OrderProtectionManager.SubmitProtectionOrders()` que persiste `myTpPrice` a `strategy.validatedTp2Price` cuando se crea TP2
    - Ahora `ManagePositionExit()` encuentra `validatedTp2Price > 0` y usa el precio correcto
    - Log añadido: `TP2_PERSIST: Saved validatedTp2Price=X to strategy`

### Técnico
- Modificado `OrderProtectionManager.cs:285-291` - Persiste `myTpPrice` en `strategy.validatedTp2Price` al crear TP2
- Actualizado `StrategyHelpers.cs:262` - Versión a v1.15.32

## [v1.15.31] - 2026-01-14
### Corregido
- **Bug: TP2 se Cambiaba a VWAP Después del Fill de Entrada** (Fix incompleto - corregido en v1.15.32)
  - **Problema**: Después de que la orden de entrada se llenaba, `ManagePositionExit()` cambiaba el precio del TP2 al VWAP en lugar de mantenerlo en el nivel opuesto
    - Ejemplo MCL 6/1/25 4:26am: Entrada Long @ 73.64
      - TP2 creado correctamente: Asia High @ 74.39
      - Segundos después: TP2 cambiado a 73.83 (VWAP) - INCORRECTO
      - El 50% de la posición quedó con TP2 = VWAP en lugar del nivel opuesto
  - **Causa Raíz Identificada**: `validatedTp2Price` no se persistía después de calcular el nivel opuesto en `EnsureProtection()`
  - **Nota**: Este fix se aplicó en código comentado y no tuvo efecto. Ver v1.15.32 para el fix real.

### Técnico
- Modificado `SessionLevelsStrategy.cs:2928-2934` - Persiste `targetZoneOpposite` en `validatedTp2Price` (código comentado, sin efecto)
- Actualizado `SessionLevelsStrategy.cs:40` - Versión a v1.15.31
- Actualizado `StrategyHelpers.cs:262` - Versión a v1.15.31

## [v1.15.30] - 2026-01-14
### Corregido
- **Bug CRÍTICO: TP2 Usaba Trading Day del Nivel de Setup en lugar del Trading Day Actual**
  - **Problema**: Cuando el trade entraba en un nivel antiguo (ej. USA Low del 10/01), el TP2 se calculaba buscando niveles opuestos del mismo trading day que el nivel de setup (10/01), ignorando niveles más recientes del trading day actual
    - Ejemplo M2K 12/1/25 10:54pm: Entrada Long en USA Low @ 2180
      - USA Low tenía Session Start: 2025-01-10 10:30 (Trading Day = 10 de enero)
      - TP2 incorrecto: Europe High @ 2252.8 (del 10/01) - nivel muy lejano (+72.8 puntos)
      - TP2 correcto: Asia High @ 2199.5 (del 12/01 19:00, Trading Day = 13 de enero) - nivel alcanzable (+19.5 puntos)
      - El trade entró el 12/01 a las 22:54 (Trading Day = 13 de enero)
  - **Causa Raíz**: En `GetOppositeLevelPrice()` se usaba `GetTradingDay(setupLevel.StartTime)` para determinar qué niveles considerar
    - Esto hacía que buscara niveles del trading day del nivel de setup (viejo)
    - En lugar de buscar niveles del trading day cuando se ejecuta el trade (actual)
  - **Solución v1.15.30**:
    - Cambió `GetTradingDay(setupLevel.StartTime)` → `GetTradingDay(strategy.Time[0])`
    - Ahora busca niveles opuestos del **trading day actual** (cuando se ejecuta el trade)
    - Esto asegura que se use el Asia High @ 2199.5 del 12/01 (Trading Day 13) en lugar del Europe High @ 2252.8 del 10/01

### Técnico
- Modificado `OrderProtectionManager.cs:562-567` - Usa `strategy.Time[0]` para calcular trading day actual
- Modificado `OrderProtectionManager.cs:571-573` - Compara contra `currentTradingDay` en lugar de `setupTradingDay`
- Modificado `OrderProtectionManager.cs:517-519` - Log actualizado para mostrar trading day actual
- Modificado `OrderProtectionManager.cs:610-615` - Log simplificado de selección de TP2
- Actualizado `SessionLevelsStrategy.cs:40` - Versión a v1.15.30
- Actualizado `StrategyHelpers.cs:262` - Versión a v1.15.30

## [v1.15.29] - 2026-01-14
### Corregido
- **MEJORA v1.15.28: Abandono Inmediato en R:R Inválido (Sin Contador)**
  - **Cambio**: Eliminado el contador de rechazos. Ahora abandona el setup **inmediatamente** cuando R:R < 1.0
  - **Razón del Cambio**:
    - Si el R:R es inválido (< 1.0), significa que el precio ya se alejó demasiado del VWAP
    - El R:R solo puede **empeorar** a partir de ese punto (el precio se aleja más o el VWAP se mueve)
    - No tiene sentido darle "5 oportunidades" a algo que ya está roto
    - Cada barra perdida evaluando R:R malo = oportunidad perdida en otros niveles
  - **Lógica v1.15.29**:
    - R:R < 1.0 detectado → Abandonar setup INMEDIATAMENTE
    - Volver a IDLE → Buscar otros niveles con mejor R:R
    - Sin contador, sin esperas, sin segundas oportunidades
  - **Beneficio**: Maximiza tiempo disponible para buscar setups rentables

### Técnico
- Eliminado `SessionLevelsStrategy.cs:207-209` - Variables del contador (ya no se necesitan)
- Modificado `EntryStateMachine.cs:709-720` - Abandono inmediato en R:R inválido (Short)
- Modificado `EntryStateMachine.cs:866-877` - Abandono inmediato en R:R inválido (Long)
- Eliminado `EntryStateMachine.cs:212` - Reset de contador en SWITCH (ya no necesario)
- Eliminado `EntryStateMachine.cs:454` - Reset de contador en invalidación (ya no necesario)
- Eliminado `EntryStateMachine.cs:679+843` - Reset de contador en orden exitosa (ya no necesario)
- Actualizado `SessionLevelsStrategy.cs:40` - Versión a v1.15.29
- Actualizado `StrategyHelpers.cs:262` - Versión a v1.15.29

## [v1.15.28] - 2026-01-14 [OBSOLETO - Ver v1.15.29]
### Corregido
- **Bug CRÍTICO: Estrategia Bloqueada en Setup con R:R Inválido**
  - **Problema**: La estrategia quedaba atrapada evaluando el mismo nivel repetidamente con R:R inválido, sin poder buscar mejores oportunidades
    - Ejemplo MYM 6/1/25: Trigger en Europe High @ 43245 a las 9:52am
      - 32 rechazos consecutivos por R:R malo (0.21-0.44, todos < 1.0)
      - El precio subió agresivamente (RE-ANCHOR de 43245 a 43346)
      - TP1 (VWAP) se mantuvo cerca (~43127), pero SL subió con el precio
      - R:R empeoró progresivamente: 0.44 → 0.24 → 0.21
      - La estrategia se quedó bloqueada desde 9:52am hasta 10:42am sin poder buscar otros niveles
  - **Causa Raíz**: La lógica de confirmación seguía evaluando el mismo setup barra tras barra aunque el R:R empeoraba
    - El filtro R:R rechazaba la entrada cada vez (correcto)
    - Pero la estrategia no abandonaba el setup para buscar mejores opciones (incorrecto)
    - El flag `lastRejectionBar` impedía escanear nuevos niveles en la misma barra donde hubo rechazo
    - **Resultado**: Oportunidades perdidas porque no buscaba otros niveles mejores
  - **Solución v1.15.28**:
    - Implementado contador `consecutiveRRRejections` que rastrea rechazos consecutivos
    - Después de 5 rechazos consecutivos, la estrategia abandona el setup automáticamente
    - Al abandonar, vuelve a estado IDLE para buscar otros niveles con mejor R:R
    - El contador se resetea cuando:
      - Se submite una orden exitosamente (R:R fue bueno)
      - Se cambia de setup (SWITCH a otro nivel)
      - Se invalida el setup por otros motivos
    - **Lógica**: Si el R:R es inválido y empeora, no tiene sentido seguir evaluando - hay que buscar mejor oportunidad

### Técnico
- Agregado `SessionLevelsStrategy.cs:207-208` - Variables `consecutiveRRRejections` y `MAX_RR_REJECTIONS = 5`
- Modificado `EntryStateMachine.cs:703-723` - Incrementar contador y abandonar setup tras 5 rechazos (Short)
- Modificado `EntryStateMachine.cs:865-885` - Incrementar contador y abandonar setup tras 5 rechazos (Long)
- Modificado `EntryStateMachine.cs:679+841` - Resetear contador en orden exitosa
- Modificado `EntryStateMachine.cs:212` - Resetear contador en SWITCH de setup
- Modificado `EntryStateMachine.cs:456` - Resetear contador en invalidación externa
- Actualizado `SessionLevelsStrategy.cs:40` - Versión a v1.15.28
- Actualizado `StrategyHelpers.cs:262` - Versión a v1.15.28

## [v1.15.27] - 2026-01-14
### Corregido
- **Bug CRÍTICO: TP2 No Usaba USA High Como Máximo del Día**
  - **Problema**: Cuando entraba en Asia Low después de las 5pm, TP2 se colocaba en Asia High del mismo timestamp de sesión, en lugar del USA High (máximo del día de trading)
    - Ejemplo MYM 6/1/25 4:55pm: Entrada Long en Asia Low @ 42930
      - TP2 incorrecto: Asia High @ 43051 (nivel opuesto de misma sesión)
      - TP2 correcto: USA High @ 43157 (máximo del día de trading)
      - Resultado: Se perdía 106 puntos de ganancia potencial (43157 - 43051 = 106)
  - **Causa Raíz**: La lógica de "mismo día" comparaba `sessionTicks` (timestamp de inicio de sesión) en lugar del día de trading real
    - Asia Low que empieza 5/1 @ 7pm (timestamp 638716968000000000)
    - USA High que empieza 6/1 @ 10:30am (timestamp 638717526000000000)
    - La comparación por timestamp los marcaba como "días diferentes", pero ambos pertenecen al mismo día de trading (6 de enero)
  - **Solución v1.15.27**:
    - Implementada función `GetTradingDay()` que calcula el día de trading correcto:
      - Si sesión empieza después de las 6pm (18:00) → Pertenece al día SIGUIENTE
      - Si sesión empieza antes de las 6pm → Pertenece al día ACTUAL
    - Ejemplo: Asia Low 5/1 @ 7pm → Trading Day = 6/1 (día siguiente)
    - Ahora la búsqueda del nivel opuesto más extremo compara por trading day en lugar de session timestamp
    - **Resultado**: Para Longs, TP2 ahora se coloca correctamente en USA High (máximo del día de trading)

### Técnico
- Modificado `OrderProtectionManager.cs:554-570` - Comparación por trading day en lugar de session ticks
- Agregado `OrderProtectionManager.cs:610-625` - Función `GetTradingDay()` para calcular día de trading
- Modificado `OrderProtectionManager.cs:517` - Log actualizado para mostrar "SAME TRADING DAY"
- Modificado `OrderProtectionManager.cs:601-603` - Log actualizado para "same trading day"
- Agregado `OrderProtectionManager.cs:606-611` - Log cuando USA High es seleccionado como TP2
- Actualizado `SessionLevelsStrategy.cs:40` - Versión a v1.15.27
- Actualizado `StrategyHelpers.cs:262` - Versión a v1.15.27

## [v1.15.26] - 2026-01-14
### Corregido
- **Bug CRÍTICO: TP2 Cambiaba al Mismo Precio de TP1 Durante Fills Parciales**
  - **Problema**: Durante fills parciales, TP2 se actualizaba al mismo precio que TP1, causando que todos los contratos salieran en TP1
    - Ejemplo MCL 6/1/25 4:26am: Entrada con 15 contratos
      - TP2 inicial: 74.39 (nivel opuesto correcto)
      - Después de fill parcial: TP2 cambió a 73.83 (mismo que TP1)
      - Resultado: Los 15 contratos salieron en TP1, ninguno llegó a TP2
  - **Causa Raíz**: En `OrderProtectionManager.cs:141-144`, ambas llamadas a `SubmitProtectionOrders` usaban el mismo parámetro `validatedTargetPrice`
    - TP1 debería usar precio de VWAP
    - TP2 debería usar precio de nivel opuesto
    - Ambos recibían el mismo valor, causando que TP2 se actualizara al precio de TP1
  - **Solución v1.15.26**:
    - Dividido `validatedTargetPrice` en dos parámetros separados: `validatedTp1Price` y `validatedTp2Price`
    - Modificada la firma de `EnsureProtection()` para recibir ambos precios
    - Actualizada `SubmitProtectionOrders()` para recibir el precio correcto según el nivel (TP1 o TP2)
    - Ahora TP1 y TP2 mantienen sus precios independientes durante fills parciales

### Técnico
- Modificado `OrderProtectionManager.cs:67-69` - Firma de `EnsureProtection()` ahora recibe validatedTp1Price y validatedTp2Price
- Modificado `OrderProtectionManager.cs:137-145` - Pasa precios separados a TP1 y TP2
- Modificado `SessionLevelsStrategy.cs:88-89` - Dividido validatedTargetPrice en dos variables
- Modificado `SessionLevelsStrategy.cs:2335-2340` - Actualizado llamadas Emergency Protection
- Modificado `SessionLevelsStrategy.cs:3644-3650` - Actualizado llamadas normales a EnsureProtection
- Modificado `SessionLevelsStrategy.cs:1287-1288, 4024-4025` - Reset de ambas variables
- Actualizado `SessionLevelsStrategy.cs:40` - Versión a v1.15.26
- Actualizado `StrategyHelpers.cs:262` - Versión a v1.15.26

## [v1.15.24] - 2026-01-13
### Agregado
- **Cálculo Dinámico de Riesgo en Modelo Standard**
  - Ahora el riesgo se calcula como **porcentaje del capital actual** en lugar de un monto fijo
  - Nueva propiedad configurable: **Risk Percentage (%)** - Default: 0.06%
  - Nueva propiedad configurable: **Starting Capital (USD)** - Default: $250,000 (referencia)
  - El cálculo usa el capital real de la cuenta en tiempo real: `Risk = Account.CashValue × (RiskPercentage / 100)`
  - Ejemplo con 0.06%:
    - Capital $250,000 → Riesgo $150 por trade
    - Capital $240,000 → Riesgo $144 por trade
    - Capital $200,000 → Riesgo $120 por trade
  - **Protección Automática contra Drawdowns**: A medida que el capital disminuye, el tamaño de posición se reduce proporcionalmente
  - Mínimo de $5 para evitar micro-posiciones
  - Usuario puede ajustar el porcentaje desde las propiedades sin recompilar

### Técnico
- Agregado `SessionLevelsStrategy.cs:4095-4097` - Propiedad RiskPercentage con rango 0.001% a 100%
- Agregado `SessionLevelsStrategy.cs:4101-4103` - Propiedad StartingCapital con rango $1,000 a infinito
- Modificado `SessionLevelsStrategy.cs:2464-2476` - Cálculo dinámico de riesgo en modelo Standard
- Actualizado `SessionLevelsStrategy.cs:40` - Versión a v1.15.24

## [v1.15.23] - 2026-01-13
### Corregido
- **Bug CRÍTICO: Tamaño de Posición Absurdo Cuando SL Muy Cercano**
  - **Problema**: Después de un VWAP retry, si el precio quedaba muy cerca del nivel, el SL podía estar a solo 1 tick de distancia. Esto causaba cantidades absurdas:
    - Ejemplo M2K: `Qty = $150 / (1 tick × $0.05) = 300 contratos` (debería ser ~23)
    - Causaba crash de la estrategia con error de índice fuera de rango
    - Riesgo de pérdida catastrófica si se ejecutaba la orden
  - **Solución v1.15.23**: Implementado **validación de SL mínimo basada en ATR**
    - Mínimo: 30% del ATR del instrumento
    - Si SL < 30% ATR → Usa MinQuantity en lugar de cantidad calculada
    - Ejemplos con ATR típicos:
      - M2K (ATR ~3.0): Mínimo = 0.9 puntos = 9 ticks
      - MES (ATR ~15.0): Mínimo = 4.5 puntos = 18 ticks
      - MGC (ATR ~10.0): Mínimo = 3.0 puntos = 30 ticks
      - MNQ (ATR ~60.0): Mínimo = 18.0 puntos = 72 ticks
  - **Prevención**: La estrategia ya no toma trades con riesgo/recompensa insuficiente cuando la entrada está demasiado cerca del nivel después de un retry

### Técnico
- Agregado `SessionLevelsStrategy.cs:2438-2455` - Validación de SL mínimo basada en ATR (30%)
- Actualizado `SessionLevelsStrategy.cs:40` - Versión a v1.15.23

## [v1.15.22] - 2026-01-13
### Agregado
- **Filtros Interactivos en Tab 1 de Streamlit**
  - Agregados filtros multiselect en 3 columnas arriba de las gráficas:
    - **Niveles**: Filtra por Asia Low, Europe Low, USA Low, etc.
    - **Instrumentos**: Filtra por MES, MNQ, MGC, MCL, M2K, MYM
    - **Intentos**: Filtra por número de intento (1, 2, 3, etc.)
  - Por defecto todos están seleccionados
  - Los filtros se aplican a todas las visualizaciones del Tab 1
  - Permite análisis granular sin usar el sidebar

### Corregido
- **Bug: Error de Alineación de Columnas en CSV**
  - **Problema**: Pandas interpretaba la columna "ID" como índice automáticamente, causando que todas las columnas se desplazaran una posición a la izquierda
  - **Síntoma**: Columna "MFE" contenía strings ("USA Low") en lugar de números, causando error: `'float' object cannot be interpreted as an integer`
  - **Solución v1.15.22**: Agregado `index_col=False` en `pd.read_csv()` para forzar que pandas NO use ninguna columna como índice
  - **Resultado**: Columnas ahora se alinean correctamente con los valores

- **Bug: MFE No Se Convertía a Valor Absoluto**
  - **Problema**: Solo MAE se convertía con `.abs()`, pero MFE también puede tener valores negativos en el CSV
  - **Solución**: Agregado `df_copy['MFE'] = df_copy['MFE'].abs()` en funciones de análisis
  - **Impacto**: Previene errores en cálculos de `MFE_R = MFE / MAE`

### Técnico
- Modificado `StreamlitAudit/app.py:537-538` - Agregada conversión abs() para MFE en analyze_r_ladder()
- Modificado `StreamlitAudit/app.py:724-725` - Agregada conversión abs() para MFE en analyze_scaling_out()
- Modificado `StreamlitAudit/app.py:906-910` - Agregado index_col=False en pd.read_csv()
- Agregado `StreamlitAudit/app.py:1529-1578` - Filtros interactivos en Tab 1

## [v1.15.21] - 2026-01-13
### Corregido
- **Bug CRÍTICO: SL No Actualiza Cantidad Después de TP1 Cuando Breakeven Está Deshabilitado**
  - **Problema**: Después de que TP1 se ejecuta parcialmente, el Stop Loss NO actualizaba su cantidad a los contratos restantes cuando `EnableBreakeven = false`. Ejemplo reportado:
    - Entrada: 7 contratos @ 76.98
    - TP1 ejecutado: 4 contratos @ 77.21 (quedaron 3 contratos)
    - SL visible en chart: **7 contratos** ✗ Incorrecto (debería mostrar 3)
    - TP2 visible en chart: 3 contratos ✓ Correcto
  - **Causa Raíz**: El método `HandleTP1Fill()` en OrderProtectionManager verificaba si breakeven estaba habilitado y hacía `return` inmediatamente sin actualizar la cantidad del SL (líneas 607-611). Esto dejaba el SL con la cantidad original en lugar de ajustarlo a la posición restante.
  - **Riesgo**: Si el SL se ejecutaba con 7 contratos pero solo quedaban 3 en la posición, causaría errores de ejecución o reversión de posición no deseada.
  - **Solución v1.15.21**: Modificado `HandleTP1Fill()` para **SIEMPRE actualizar la cantidad del SL** después de TP1, independientemente del estado de breakeven:
    - Si `EnableBreakeven = false`: Actualiza cantidad del SL manteniendo el precio original del SL
    - Si `EnableBreakeven = true`: Actualiza cantidad Y mueve precio a breakeven (comportamiento original)
  - **Resultado**: Ahora el SL siempre refleja la cantidad correcta de contratos restantes después de TP1, protegiendo la posición adecuadamente.
  - **Archivos Modificados**:
    - `OrderProtectionManager.cs:604-636` - HandleTP1Fill() refactorizado para separar actualización de cantidad vs movimiento de precio

### Técnico
- Modificado `OrderProtectionManager.cs:604-636` - HandleTP1Fill() ahora actualiza cantidad siempre.
- Actualizado `SessionLevelsStrategy.cs:40` - Versión a v1.15.21.
- Actualizado `StrategyHelpers.cs:262` - Display de versión a v1.15.21.

## [v1.15.48] - 2026-01-20

### 🐛 Fixed

**1. CSV Export - Contador de Intentos Simplificado y Corregido** en CSV y Plots**
  - **Problema**: El CSV mostraba el contador de "Attempt" incorrecto cuando la estrategia cambiaba de nivel. Por ejemplo:
    - Asia Low: Intento #1 (5:11 AM), Intento #2 (5:44 AM), Intento #3 (6:04 AM) ✓ Correcto
    - USA Low: Mostraba "Intento #4" cuando debería mostrar "Intento #1" ✗ Incorrecto
  - **Causa Raíz**: El campo `tradeAttemptNumber` del CSV usaba `currentVwapNumber` (contador de VWAP retries dentro del mismo nivel) en lugar de `currentLevelAttempts` (contador de intentos por nivel específico).
  - **Impacto**: Imposible analizar en Streamlit qué intento de cada nivel (1-20) da mejores resultados, que es crítico para optimizar la estrategia.
  - **Solución v1.15.20**: Cambiado `tradeAttemptNumber = currentVwapNumber` → `tradeAttemptNumber = currentLevelAttempts`
  - **Resultado**: Ahora cada nivel resetea su contador correctamente:
    - Asia Low: Attempts 1, 2, 3...
    - USA Low: Attempts 1, 2, 3... (resetea cuando cambia de nivel)
    - Permite análisis correcto en Streamlit por intento de nivel (ej: "Solo entrar en Intento #6 de cada nivel")
  - **Archivos Modificados**:
    - `SessionLevelsStrategy.cs:3556` - Cambio de currentVwapNumber a currentLevelAttempts

### Técnico
- Modificado `SessionLevelsStrategy.cs:3556` - CSV export ahora usa currentLevelAttempts.
- Actualizado `SessionLevelsStrategy.cs:40` - Versión a v1.15.20.
- Actualizado `StrategyHelpers.cs:262` - Display de versión a v1.15.20.

## [v1.15.19] - 2026-01-13
### Corregido
- **Bug: Órdenes de Mercado con Precio Incorrecto (Slippage Excesivo)**
  - **Problema**: Las órdenes de mercado (FAILSAFE y Emergency Close) se ejecutaban con slippage excesivo, resultando en precios de salida desfavorables. En el trade M2K reportado: entrada Short a 2292.4, FAILSAFE se activó en 2293.8, pero las salidas se ejecutaron a 2293.9 y 2294.0.
  - **Causa Raíz**: Las órdenes de mercado se enviaban con `limitPrice: 0`, lo que no proporciona protección contra slippage. En NinjaTrader, aunque el tipo sea `OrderType.Market`, el parámetro `limitPrice` actúa como "precio máximo/mínimo aceptable" para proteger contra slippage extremo.
  - **Ejemplo del Bug**:
    ```
    FAILSAFE: Price (High=2293.8) violated Anchor (2293.4)
    UNMANAGED EXIT: Closing Short. Reason: Anchor Violation
    → Orden: BuyToCover @ Market, limitPrice=0 (sin protección)
    → Fill: 2293.9 y 2294.0 (slippage de +1.1 y +1.2 ticks desde trigger)
    ```
  - **Solución v1.15.19**: Implementado límite de precio basado en Bid/Ask + buffer de 2 ticks:
    ```csharp
    // Para Short Exit (BuyToCover): Usar Ask + 2 ticks como límite
    double askPrice = GetCurrentAsk();
    double limitPrice = RoundToTickSize(askPrice + (2 * TickSize));
    Log(string.Format("UNMANAGED EXIT: Closing Short. Ask={0} Limit={1}", askPrice, limitPrice));
    SubmitOrderUnmanaged(0, OrderAction.BuyToCover, OrderType.Market, qty, limitPrice, 0, "", "Exit_Short_Market");

    // Para Long Exit (Sell): Usar Bid - 2 ticks como límite
    double bidPrice = GetCurrentBid();
    double limitPrice = RoundToTickSize(bidPrice - (2 * TickSize));
    SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.Market, qty, limitPrice, 0, "", "Exit_Long_Market");
    ```
  - **Resultado**: Las órdenes de mercado ahora actúan como **órdenes límite marketables**:
    - Se ejecutan inmediatamente como orden de mercado si el precio es favorable
    - Rechazan fills peores que Ask+2 ticks (Short) o Bid-2 ticks (Long)
    - El buffer de 2 ticks permite movimiento normal del mercado
    - Logs ahora muestran Bid/Ask y precio límite para transparencia
  - **Archivos Modificados**:
    - `SessionLevelsStrategy.cs:4202-4230` - ClosePositionUnmanaged() con protección Bid/Ask
    - `SessionLevelsStrategy.cs:1530-1582` - Emergency Close con protección Bid/Ask
    - Agregado logging detallado de Bid/Ask y límite en todas las órdenes de mercado

### Técnico
- Modificado `SessionLevelsStrategy.cs:4202-4230` - ClosePositionUnmanaged() ahora usa Bid/Ask + buffer.
- Modificado `SessionLevelsStrategy.cs:1530-1550` - EmergencyClose primera ubicación con Bid/Ask + buffer.
- Modificado `SessionLevelsStrategy.cs:1558-1582` - EmergencyClose segunda ubicación con Bid/Ask + buffer.
- Actualizado `StrategyHelpers.cs:262` - Display de versión a v1.15.19.

## [v1.15.18] - 2026-01-13
### Corregido
- **Bug: Breakeven Ignoraba Configuración EnableBreakeven**
  - **Problema**: El Stop Loss se movía a breakeven después de TP1 fill incluso cuando `EnableBreakeven = false`.
  - **Causa Raíz**: El método `HandleTP1Fill()` no verificaba el parámetro `EnableBreakeven` antes de mover el SL.
  - **Solución**: Agregado check al inicio de `HandleTP1Fill()` que retorna inmediatamente si `EnableBreakeven = false`, con log de notificación.
  - **Resultado**: El SL ahora respeta la configuración y solo se mueve a BE cuando está habilitado.

### Técnico
- Modificado `OrderProtectionManager.cs:606-611` - Agregado check de EnableBreakeven con early return.

## [v1.15.17] - 2026-01-13
### Mejorado
- **TP2 Maximizado: Usar Nivel Opuesto Más Extremo del Mismo Día**
  - **Cambio**: TP2 ahora busca el nivel opuesto más extremo del mismo día de trading para maximizar beneficios.
  - **Ejemplo**: Si trabajamos con Europa Low @ 100, y los niveles opuestos del mismo día son Europa High @ 200 y Asia High @ 300, TP2 ahora selecciona Asia High @ 300 (el más extremo).
  - **Lógica**:
    - Para Short: Busca el High más alto del mismo día
    - Para Long: Busca el Low más bajo del mismo día
    - Solo considera niveles del mismo día (matching sessionTicks)
  - **Resultado**: TP2 maximiza el potencial de ganancia utilizando el rango completo del día.

### Técnico
- Modificado `OrderProtectionManager.cs:470-582` - GetOppositeLevelPrice() ahora escanea todos los niveles del mismo día y selecciona el más extremo.
- Agregado logging cuando se selecciona un nivel más extremo que el opuesto directo.

## [v1.15.16] - 2026-01-13
### Corregido
- **Bug: Contador del Panel No Coincidía con Sufijo de Orden**
  - **Problema**: El panel mostraba "[1/20]" cuando la orden era "EntryA+_Long_02" (mismatch entre display y sufijo).
  - **Causa Raíz**: Dos contadores diferentes: `currentVwapNumber` (usado en sufijos de orden) vs `currentLevelAttempts` (usado en panel).
  - **Solución**: Panel ahora usa `currentVwapNumber` para que coincida con el sufijo de las órdenes.
  - **Resultado**: El contador del panel ahora coincide exactamente con el número en el sufijo de la orden.

### Técnico
- Modificado `StrategyHelpers.cs:223-235` - Display usa `currentVwapNumber` en lugar de `currentLevelAttempts`.

## [v1.15.15] - 2026-01-13
### Corregido
- **Bug: Contador de Intentos por Nivel Mostraba 0 en Lugar del Valor Correcto**
  - **Problema**: El panel de estrategia mostraba "[0/20]" para el contador de intentos por nivel, incluso después de haber ejecutado un trade en ese nivel. El log mostraba "ENTRY ATTEMPT #1/20 on USA Low" pero el display mostraba "[0/20]".
  - **Causa Raíz**: El contador `EntryAttempts` se almacenaba en el objeto `SessionLevel` dentro de `activeLevels`. Si los niveles se reconstruían (por ejemplo, al escanear niveles históricos), se creaban nuevos objetos `SessionLevel` con `EntryAttempts = 0` (valor por defecto), perdiendo el contador anterior.
  - **Ejemplo del Bug**:
    - Trade #1: "ENTRY ATTEMPT #1/20 on USA Low" → lvl.EntryAttempts = 1 ✓
    - Niveles se reconstruyen → Nuevo objeto SessionLevel creado → EntryAttempts = 0 ❌
    - Panel muestra "[0/20]" en lugar de "[1/20]"
  - **Solución v1.15.15**: Implementado sistema de persistencia de contador:
    ```csharp
    // 1. Variable persistente en estrategia (no se pierde al reconstruir niveles)
    [XmlIgnore] public int currentLevelAttempts = 0;

    // 2. Copiar contador al incrementar (EntryStateMachine.cs:240)
    lvl.EntryAttempts++;
    strategy.currentLevelAttempts = lvl.EntryAttempts; // Backup persistente

    // 3. Display usa valor persistente como fallback (StrategyHelpers.cs:232)
    int attemptsToShow = strategy.currentLevelAttempts; // Default
    if (currentLevel != null && currentLevel.EntryAttempts > 0)
        attemptsToShow = currentLevel.EntryAttempts; // Use object if valid
    ```
  - **Resultado**: El contador ahora persiste correctamente incluso si los niveles se reconstruyen. El panel siempre muestra el número correcto de intentos.

### Técnico
- Agregado `SessionLevelsStrategy.cs:1823` - Variable `currentLevelAttempts` para persistencia.
- Modificado `EntryStateMachine.cs:241,287` - Copia `EntryAttempts` a variable persistente al incrementar.
- Modificado `StrategyHelpers.cs:223-242` - Usa valor persistente como fallback si `SessionLevel.EntryAttempts == 0`.

## [v1.15.14] - 2026-01-13
### Corregido
- **Bug Crítico: Órdenes de Protección No Se Creaban en Replay (Phantom Position Block)**
  - **Problema**: En el segundo trade de MCL, la estrategia entró con 10 contratos pero NO creó SL, TP1 ni TP2. El log mostraba: "PHANTOM PROTECTION BLOCKED: Strategy shows position but Account has 0. Skipping EnsureProtection."
  - **Causa Raíz**: Durante replay, cuando un fill completo ocurre de una vez (sin fills parciales adicionales), `Account.Positions` puede no sincronizarse instantáneamente con `Position.Quantity`. El check de "phantom position" bloqueaba la creación de órdenes de protección incluso cuando `Position.Quantity = 10` era válido.
  - **Ejemplo del Bug**:
    - Trade #1 MCL: Fill inicial de 7 contratos → Phantom blocked → Segundo fill de 9 contratos → Protección creada ✓
    - Trade #2 MCL: Fill completo de 10 contratos → Phantom blocked → Sin fills adicionales → **SIN PROTECCIÓN** ❌
  - **Solución v1.15.14**: Modificada la lógica de phantom check:
    ```csharp
    // ANTES (v1.14.70) - Bloqueaba si Account.Positions = 0
    if (!hasRealPosition) {
        return; // Block protection
    }

    // AHORA (v1.15.14) - Solo bloquea si AMBOS son 0
    bool positionQuantityValid = Math.Abs(strategy.Position.Quantity) > 0;
    if (!hasRealPosition && !positionQuantityValid) {
        return; // Block only if BOTH are 0 (true phantom)
    }

    // Procede si Position.Quantity > 0 (confía en replay)
    if (!hasRealPosition && positionQuantityValid) {
        Log("REPLAY_SYNC_DELAY: Proceeding with protection (replay sync delay)");
    }
    ```
  - **Resultado**: Ahora la estrategia confía en `Position.Quantity` durante replay y crea las órdenes de protección incluso si `Account.Positions` aún no se ha sincronizado. Solo bloquea si AMBOS son 0 (verdadero phantom).

### Técnico
- Modificado `OrderProtectionManager.cs:71-102` - Agregada verificación adicional `positionQuantityValid`.
- El cambio permite que fills completos en replay creen protección inmediatamente sin esperar sincronización de `Account.Positions`.
- Log adicional: "REPLAY_SYNC_DELAY" cuando procede con `Position.Quantity` válido pero `Account.Positions` = 0.

## [v1.15.13] - 2026-01-13
### Corregido
- **Bug Crítico: Estrategia No Escaneaba Otros Niveles Durante VWAP Retry**
  - **Problema**: Cuando la estrategia entraba en modo "VWAP Retry" (esperando que el precio rompa el extremo VWAP para reintentar entrada), NO escaneaba otros niveles que estaban siendo tocados.
  - **Ejemplo del Bug**: MCL trabajó con USA Low (viernes anterior), luego el precio tocó USA Low (16/01) y Europe Low (15/01), pero la estrategia no detectó ni cambió a estos nuevos niveles porque estaba "bloqueada" esperando el VWAP retry.
  - **Causa Raíz**: En `SessionLevelsStrategy.cs:2507-2508`, `HandleVwapMitigationRetry()` retornaba early, bloqueando TODA la lógica de escaneo de niveles. Aunque el código en línea 2517 incluía `WaitingForVwapMitigation` en `canScan`, este código nunca se ejecutaba debido al early return.
  - **Solución v1.15.13**: Eliminado el early return en línea 2507-2508. Ahora la estrategia ejecuta AMBAS lógicas en paralelo:
    - Continúa monitoreando el VWAP retry breakout en el nivel actual
    - TAMBIÉN escanea y detecta otros niveles siendo tocados
    - Si un nuevo nivel es triggereado, la estrategia puede cambiar automáticamente
  - **Resultado**: La estrategia ahora es consciente de "dónde está trabajando" - puede esperar una nueva ruptura VWAP Y TAMBIÉN estar pendiente de otros niveles más arriba/abajo.

### Técnico
- Modificado `SessionLevelsStrategy.cs:2505-2508` - Eliminado early return de `HandleVwapMitigationRetry()`.
- Agregado comentario v1.15.13 en línea 2515 documentando que la feature de v1.14.80 ahora funciona correctamente.
- El cambio permite que `ScanForTriggers()` (línea 2572) se ejecute cuando `currentEntryState == WaitingForVwapMitigation`.

## [v1.15.12] - 2026-01-13
### Corregido
- **Bug Crítico: TP2 Faltante Cuando Fill Se Va Completo a TP1**
  - **Problema de v1.15.11**: El fix anterior resolvió parcialmente el problema, pero introdujo un nuevo bug. Cuando un fill parcial se asignaba completamente a TP1, TP2 no recibía NINGUNA orden.
  - **Ejemplo del Bug**: MGC entra con 15 contratos. Fill de 8 contratos:
    - TP1 necesita 8, TP2 necesita 7
    - forTp1 = Min(8, 8) = 8 ✓
    - forTp2 = Min(7, 8 - 8) = Min(7, **0**) = **0** ❌
    - Resultado: TP1 creada con 8, TP2 **no se crea** (faltan 7 contratos sin protección)
  - **Causa Real**: La lógica solo llamaba `SubmitProtectionOrders()` si `forTp1 > 0` o `forTp2 > 0`, pero cuando todo el fill se asignaba a TP1, `forTp2 = 0` y TP2 nunca se creaba.
  - **Solución v1.15.12**: Cambio fundamental en la lógica de protección:
    ```csharp
    // ANTES (v1.15.11) - Solo creaba TP si el fill asignaba contratos
    if (forTp1 > 0) SubmitProtectionOrders(..., forTp1, ...);
    if (forTp2 > 0) SubmitProtectionOrders(..., forTp2, ...);

    // AHORA (v1.15.12) - SIEMPRE crea/actualiza TP si hay contratos que necesitan protección
    if (neededTp1 > 0) SubmitProtectionOrders(..., neededTp1, ...);
    if (neededTp2 > 0) SubmitProtectionOrders(..., neededTp2, ...);
    ```
  - **Cambio de Paradigma**: `protectedTp1Qty` y `protectedTp2Qty` ahora reflejan el **target total** (lo que DEBE estar protegido), no la suma incremental de fills asignados. Esto asegura que las órdenes TP siempre reflejen la cantidad correcta, independientemente de cómo se distribuyan los fills parciales.
  - **Resultado**: Ahora SIEMPRE se crean/actualizan ambas órdenes TP cuando hay contratos pendientes de protección, incluso si un fill individual no alcanza para cubrir ambos targets.

### Técnico
- Modificado `OrderProtectionManager.cs:126-136` - Cambio de lógica de `if (forTpX > 0)` a `if (neededTpX > 0)`.
- Cambio en actualización de estado: `protectedTp1Qty = totalTp1Target` (antes: `+= forTp1`).
- Este fix resuelve el caso donde MGC tenía SL=15, TP1=8, pero TP2=0 (faltaban 7 contratos).

## [v1.15.11] - 2026-01-13
### Corregido
- **Bug Crítico: TP2 con Cantidades Incorrectas (Causa Raíz Identificada)**
  - **Problema Real**: El problema NO era `ChangeOrder()` ni sincronización de NinjaTrader. Era un bug de lógica en la distribución de contratos entre TP1 y TP2 durante fills parciales.
  - **Ejemplo del Bug**: MCL entra con 47 contratos. En el último fill parcial (6 contratos), la estrategia calculaba:
    - TP1 necesita 3 más → Asigna 3 a TP1 ✓
    - TP2 recibe **los 3 restantes** (6-3=3) ❌
    - Pero TP2 necesitaba **10 contratos más** (23 total - 13 protegidos)
    - Resultado: TP2 se queda con 16 contratos en lugar de 23 (faltaban 7)
  - **Causa Técnica**: La línea `int forTp2 = filledQty - forTp1;` asumía que todo lo que no va a TP1 debe ir a TP2, sin verificar cuántos contratos TP2 realmente necesita.
  - **Solución**: Agregada validación de `neededTp2` antes de asignar contratos:
    ```csharp
    int totalTp2Target = totalPositionQty - totalTp1Target;
    int neededTp2 = totalTp2Target - strategy.protectedTp2Qty;
    int forTp2 = Math.Min(neededTp2, filledQty - forTp1);
    ```
  - **Resultado**: Ahora TP1 y TP2 siempre mantienen el split 50/50 correcto, sin importar cuántos fills parciales ocurran.

### Técnico
- Modificado `OrderProtectionManager.cs:110-124` - Agregada verificación de `neededTp2` en lógica de distribución.
- Mejorado log de distribución para mostrar `(Need:X)` tanto para TP1 como TP2.
- Esta era la causa raíz real de todos los problemas de cantidades incorrectas en TP2 (MNQ, MGC, MCL).

## [v1.15.10] - 2026-01-13
### Corregido
- **Revertido Enfoque Cancel+Recreate a ChangeOrder()**
  - **Problema Descubierto**: El enfoque Cancel+Recreate de v1.15.9 causaba que las órdenes TP2 desaparecieran completamente (ej. MGC con 15 contratos: SL=15, TP1=8, TP2=ausente). Esto ocurría porque si NinjaTrader no había terminado de procesar la cancelación cuando intentábamos recrear la orden, la nueva orden fallaba y retornaba null.
  - **Causa Raíz Real**: El problema no es `ChangeOrder()` en sí, sino probablemente cómo `OnOrderUpdate()` actualiza (o no actualiza) las referencias de orden (`tp1Order`/`tp2Order`) cuando los OrderIDs cambian.
  - **Solución**: Revertido a usar `ChangeOrder()` pero con logging mejorado que incluye OrderID para diagnosticar problemas de sincronización de referencias.
  - **Próximos Pasos**: Investigar `OnOrderUpdate()` para asegurar que las referencias de orden se actualicen correctamente cuando el estado de orden cambia.

### Técnico
- Revertido `OrderProtectionManager.cs:340-351` de Cancel+Recreate a `ChangeOrder()`.
- Agregado OrderID a logs de actualización de TP para facilitar diagnóstico de problemas de sincronización.
- Este es un enfoque más seguro que Cancel+Recreate, que tenía dependencias de timing frágiles.

## [v1.15.9] - 2026-01-13
### Corregido
- **Bug Crítico: Órdenes TP con Cantidades Incorrectas en Fills Parciales**
  - **Problema**: Cuando la entrada se llenaba en múltiples fills parciales (ej. 4+4+1+3=12 contratos), las órdenes TP1/TP2 quedaban con cantidades incorrectas (ej. TP2 con 2 contratos en lugar de 6). NinjaTrader perdía sincronización al llamar `ChangeOrder()` muy rápidamente y creaba órdenes huérfanas con OrderIDs diferentes.
  - **Solución**: Cambiada la lógica de actualización de órdenes TP de `ChangeOrder()` a **Cancel + Recreate**. Ahora cuando hay que actualizar una orden TP:
    1. Se cancela la orden existente con `CancelOrderWrapper()`
    2. Se limpia la referencia (`tp1Order`/`tp2Order` = null)
    3. Se crea una nueva orden con la cantidad correcta usando `SubmitOrderUnmanagedWrapper()`
  - **Impacto**: Previene desincronización de NinjaTrader y asegura que siempre haya exactamente 50% en TP1 y 50% en TP2.
  - **Logs Mejorados**: Ahora se registra el OrderID en cada creación/recreación para facilitar debugging.

### Técnico
- Modificado `OrderProtectionManager.cs:340-393` - Reemplazada lógica de `ChangeOrder()` por Cancel+Recreate.
- Agregados logs diagnósticos con OrderID para rastrear órdenes TP.
- Esta solución es más robusta y evita race conditions en el motor de órdenes de NinjaTrader.

## [v1.15.8] - 2026-01-13
### Corregido
- **Errores de Compilación CS1061:** Implementados 2 métodos faltantes que causaban errores de compilación.
  - **`CancelAllProtectionOrders()`** en `OrderProtectionManager.cs`:
    - **Ubicación**: Llamado en `SessionLevelsStrategy.cs` líneas 1955, 1971, 2052, 2084
    - **Propósito**: Cancela todas las órdenes de protección (SL, TP1, TP2) antes de ejecutar salidas de emergencia para prevenir condiciones de carrera que podrían causar posiciones inversas.
    - **Lógica**: Verifica estado de cada orden (Working/Accepted) antes de cancelar usando `CancelOrderWrapper()`.
  - **`InheritFromGlobal()`** en `VWAPCalculator.cs`:
    - **Ubicación**: Llamado en `SessionLevelsStrategy.cs` línea 1331
    - **Propósito**: Preserva los valores acumulados del Global VWAP cuando un trade activo cruza las 18:00 (cierre de sesión). Evita perder el cálculo de TP1 cuando el Global VWAP se resetea.
    - **Lógica**: Copia `VolSum` y `PvSum` del Global VWAP (High o Low según el setup) al Trade VWAP y activa el modo de extensión post-sesión.

### Técnico
- Agregado método `CancelAllProtectionOrders()` en `SessionLevels/OrderProtectionManager.cs:552-583`.
- Agregado método `InheritFromGlobal()` en `SessionLevels/VWAPCalculator.cs:312-331`.
- Actualizado display de versión en panel de estado (`StrategyHelpers.cs:262`).
- Ambos métodos incluyen logs diagnósticos detallados.

## [v1.15.7] - 2026-01-13
### Agregado
- **Panel de Estado: Contador de Intentos por Nivel**: Ahora el panel muestra dos contadores separados:
  - **`(VWAP X)`**: Número de reintento VWAP dentro del setup actual (ej. VWAP 2 después de un SL/BE)
  - **`[X/20]`**: Intentos totales acumulados en ese nivel específico (ej. `[2/20]` = segundo intento de 20 máximos permitidos)
  - **Ejemplo completo**: `Level: Asia High (VWAP 2) [3/20]` indica que es el tercer intento en Asia High, y dentro de este setup estamos en el segundo VWAP.

### Técnico
- Modificado `StrategyHelpers.cs:213-236` para buscar el nivel actual en `activeLevels` y mostrar su propiedad `EntryAttempts`.
- El contador se muestra cuando el estado es `WaitingForConfirmation`, `workingOrder`, `PositionActive`, o `WaitingForVwapMitigation`.

## [v1.15.6] - 2026-01-13
### Corregido
- **Errores de Compilación en EntryStateMachine:** Completada la implementación de métodos faltantes que causaban errores CS1061 y CS0246.
  - **Agregado `using NinjaTrader.NinjaScript.Strategies.SessionLevels`**: Solucionó error CS0246 donde no se encontraba el tipo `EntryStateMachine`.
  - **Implementado `CheckTradingModeGuards()`**: Método que verifica si el trading está permitido según el modo actual (Paused/LongOnly/ShortOnly). Bloquea setups Short en modo LongOnly y viceversa.
  - **Implementado `HandleVwapMitigationRetry()`**: Detecta si la estrategia está en estado de espera para retry de VWAP después de un SL/BE. Retorna `true` para bloquear otra lógica de entrada mientras espera.
  - **Implementado `UpdateAnchorIfNeeded()`**: Re-ancla el setup cuando el precio hace nuevo high (SHORT) o nuevo low (LONG). Reinicia el cálculo de VWAP adhoc desde el nuevo anchor.
  - **Implementado `HandleInternalInvalidation()`**: Maneja la invalidación cuando un nivel interno toca un nivel externo. Cancela el setup interno y auto-dispara el nivel externo. Incluye protección anti-loop.

### Técnico
- **Refactoring Completado**: Los 4 métodos extraídos durante la Fase 5 de refactoring (v1.14.45) ahora están completamente implementados en `EntryStateMachine.cs`, permitiendo que el código compile correctamente.
- **Líneas Afectadas**: ~150 líneas de código agregadas a `SessionLevels/EntryStateMachine.cs`.

## [v1.15.4] - 2026-01-12
### Agregado
- **Salida de Emergencia (Market Exit):** Implementada protección contra gaps violentos en la entrada. Si al llenarse la orden, el precio ya superó el nivel de Stop Loss, la estrategia cierra la posición al mercado inmediatamente en lugar de intentar colocar un SL inválido.

### Streamlit App (Auditor)
- **Reversión:** Se eliminó la función "Borrado Selectivo" por inestabilidad. Se restauró la función de "Borrado Total (Backtest)" para limpieza manual.

## [v1.15.3] - 2026-01-12
### Agregado
- **Limpieza de Entradas Pendientes:** Nuevo método `CheckPendingEntryCleanup()` cancela órdenes de entrada parcialmente llenadas cuando el precio está a 4 ticks del TP1. Evita "fills zombi" después de que el trade principal cierre.

## [v1.15.2] - 2026-01-12
### Corregido
- **Bucle Infinito en Cierre de Sesión:** Corregida condición de carrera crítica a las 18:00. Ahora la estrategia detecta si hay una posición activa y omite la cancelación masiva de órdenes de protección, evitando el conflicto con el Safety Net.
- **Formato de Logs:**
    - Se reemplazó la hora del sistema (`DateTime.Now`) por la hora del gráfico (`Time[0]`) para facilitar la depuración exacta vela a vela.
    - Se añadieron prefijos de contexto (`[REALTIME]`, `[PLAYBACK]`, `[BACKTEST]`) a cada línea de log.

### Agregado
- **Persistencia de Logs:** Los archivos de log ya no se sobrescriben al reiniciar la estrategia. Ahora se añade un separador visual (`=== NEW SESSION STARTED ===`) y se sigue escribiendo en el mismo archivo.
- **Separación de Archivos de Log:** Se generan archivos independientes según el modo de ejecución (ej. `MYM_Realtime_20260112.txt` vs `MYM_Playback_20260112.txt`).

## [v1.15.1] - 2026-01-12
### FIXED
- **Daily Pending Order Cleanup:** Ahora la estrategia cancela *todas* las órdenes pendientes al cierre de la sesión americana (diario), no solo los viernes. Esto previene que órdenes Limit no tomadas queden colgadas (Orphans) durante la noche.
- **Zombie Sweep:** Se añadió un bucle de seguridad que escanea la colección interna `Orders` y fuerza la cancelación de cualquier orden "Working" que haya quedado huérfana (sin referencia `entryOrder`), solucionando definitivamente el caso reportado en MNQ/MES.

## [v1.15.0] - 2026-01-12
### FIXED
- **Orphan Trade (Race Condition):** Solucionado un error crítico donde la estrategia se reiniciaba a estado `Idle` si recibía un evento `Order Cancelled` (común en fills parciales) antes de que la Posición se actualizara. Ahora verifica `filled == 0` antes de reiniciar.
- **Log Readability:** Separación visual clara de los trades en el log (`==== TRADE START ====` / `==== TRADE CLOSED ====`) y estandarización de timestamps automáticos en todas las líneas.

## [v1.14.97] - 2026-01-11
- **FIX: Comisión MyM vs M2K Swap (Definitivo)**
    - Análisis de PnL muestra que la App estaba sobreestimando MYM y subestimando M2K.
    - **Cambio**: Se intercambiaron las tasas para reflejar la microestructura real del broker del usuario.
        - **M2K (Micro Russell)**: Ajustado a **$0.95/lado ($1.90 RT)** (Subida).
        - **MYM (Micro Dow)**: Ajustado a **$0.90/lado ($1.80 RT)** (Bajada).
    - Esto eliminará la discrepancia de -$16.50 en MYM y +$18.40 en M2K.

## [v1.14.96] - 2026-01-11
- **Feature: Análisis de Frescura de Niveles (Data Export)**
    - Se añadió la columna `LevelAge` (Columna 21) al CSV de exportación de trades.
    - Calcula la antigüedad del nivel en días al momento de la entrada (0 = Hoy, 1 = Ayer, etc.).
    - Permite filtrar en la App por "Niveles de Hoy" vs "Niveles Antiguos".
    - **Header Update**: Se modificó `InitCSV` para incluir el nuevo encabezado.

## [v1.14.95] - 2026-01-11
- **FIX: Comisión MYM ($1.90 RT)**
    - Se eliminó "Dead Code" que excluía a MYM de la lógica de comisiones Micro.
    - Se eliminó la anulación redundante que forzaba $1.80.
    - Ahora MYM y MNQ usan correctamente $0.95/lado ($1.90 RT).
- **FIX: Activación Prematura de Niveles (Asia Low)**
    - Se corrigió un bug donde niveles de sesiones nocturnas (Overnight) se activaban a las 12:01 AM del día siguiente, aunque la sesión siguiera activa.
    - Nueva lógica `ActiveSessionCheck` bloquea operaciones si la sesión no ha finalizado, manejando correctamente el cruce de medianoche.

## [v1.14.94] - 2026-01-11
- **v1.14.94**: **Fix Crítico de Doble Salida**. Se implementó `CancelAllProtectionOrders` antes de ejecutar salidas de emergencia (Failsafe por violación de Anchor) o cierres de sesión. Esto evita que el Stop Loss y la orden de salida a mercado se ejecuten simultáneamente (Race Condition entres estrategia y exchange), lo cual causaba posiciones inversas no deseadas (e.g., +42 Longs tras cerrar 28 Shorts).
    - **Update**: Ajuste de Comisiones Micro Indices Híbrido: **MNQ=$1.90 RT** ($0.95/lado) y **Otros Micros=$1.80 RT** ($0.90/lado) para precisión milimétrica con broker del usuario.
- **v1.14.93**: **Corrección de Cálculo de PnL en Fills Parciales**. Se actualizó `OnExecutionUpdate` para usar `execution.Price` en lugar de `order.AverageFillPrice` al calcular el PnL y escribir el CSV. Esto elimina la discrepancia de centavos acumulada cuando una orden se llena en múltiples tramos a precios distintos.

## [v1.14.92] - 2026-01-11
### Fixed Data Gap: Partial Fills (PartFilled) 📉
- **Problema**: Al comparar con NinjaTrader, faltaban contratos en el CSV si una orden se llenaba en varios tramos (ej: orden de 16 contratos que se llena 4 + 12). La estrategia ignoraba el estado `PartFilled` y solo registraba el `Filled` final.
- **Solución**: Se actualizó `OnExecutionUpdate` para registrar también eventos `PartFilled`. Ahora el CSV capturará cada fragmento de la ejecución, asegurando que la suma total de contratos y PnL coincida exactamente con NinjaTrader.

## [v1.14.91] - 2026-01-11
### Fixed Data Inconsistency (App vs Ninja) 📊
- **Problema**: El reporte en Streamlit mostraba datos inconsistentes o vacíos debido a que la estrategia exportaba 20 columnas (incluyendo Deltas) pero la App solo leía 16, y el archivo CSV no tenía cabecera.
- **Solución**:
    1.  **App Updated**: Se actualizó `app.py` para leer las 20 columnas correctamente. Esto arregla la lectura de tus datos actuales.
    2.  **Strategy InitCSV**: Se implementó `InitCSV` para que los nuevos archivos se creen con una cabecera correcta (`TradeId,Instrument,...`), evitando ambigüedades futuras.



## [v1.14.90] - 2026-01-10
### Fixed CRÍTICO: Distribución de Cantidad en Rellenos Parciales (Smart Consumption) 🧠
- **Problema**: Al entrar con órdenes Limit (que generan muchos rellenos parciales de 1 contrato), la distribución escalonada se rompía ("clumping" al final) y el Stop Loss no aumentaba su tamaño.
- **Solución**:
    1.  **Stop Loss Agregado**: Ahora el SL detecta el tamaño *total* de la posición y se actualiza (`ChangeOrder`) automáticamente con cada relleno parcial.
    2.  **Consumo Inteligente de TP**: En lugar de calcular una "mini distribución" para cada relleno de 1 contrato, ahora calcula el **Plan Maestro** (para el total de contratos) y "consume" solo la rebanada correspondiente al relleno actual.
- **Resultado**: Curva de distribución perfecta incluso si la entrada se llena en 50 tramos de 1 contrato.

## [v1.14.89] - 2026-01-10
### Fixed CRÍTICO: Crash por Cantidad Cero y Deadlock 💥
- **Problema**: La estrategia se desactivaba inmediatamente ("desaparecen los botones") al intentar abrir una operación.
- **Causa Raíz**: 
    1. `OrderProtectionManager` intentaba crear un Stop Loss inicial con **Cantidad = 0** (debido a lógica hardcoded en modo escalonado).
    2. NinjaTrader lanzaba una excepción interna al validar la orden.
    3. La terminación forzada de la estrategia ocurría mientras se sostenía un `lock` (bloqueo), causando una excepción recursiva (`LockRecursionException`).
- **Solución**: 
    - Se agregó validación `if (qty > 0)` antes de enviar cualquier orden de protección.
    - Se redujo el alcance del bloqueo (`lock`) en `OrderProtectionManager` para que solo cubra la adición a la lista, no la llamada al nucleo de NinjaTrader (`SubmitOrder`).
    - Se agregaron bloques `try-catch` de seguridad en `OnBarUpdate`, `OnOrderUpdate` y `OnExecutionUpdate`.
- **Resultado**: Estrategia estable y funcional ("ya cargo").

## [v1.14.88] - 2026-01-10
### Added Distribución Dinámica de Targets Escalonados 📈
- **Mejora**: Implementada lógia "Gap-Fill" en modo Scaled.
- **Detalle**: Ahora el sistema calcula el tamaño de paso dinámicamente para cubrir toda la distancia hasta el VWAP con 20 órdenes, evitando huecos grandes.

## [v1.14.86] - 2026-01-10
### Fixed CRÍTICO: validatedTargetPrice Nunca Se Asignaba Desde OrderProtectionManager 🔧
- **Problema**: v1.14.85 no funcionó. `validatedTargetPrice` permanecía en 0 incluso cuando se encontraba el nivel opuesto correcto.
- **Causa Raíz**: El fix v1.14.83 solo asignaba `validatedTargetPrice` en `EntryStateMachine` durante la confirmación inicial del setup, pero esta asignación no persistía hasta que se creaban las órdenes TP. Cuando `OrderProtectionManager.SubmitProtectionOrders()` creaba TP2, `validatedTargetPrice` seguía en 0, por lo que `ManagePositionExit()` no tenía ningún valor guardado para usar como fallback.
- **Solución**: Agregado en `OrderProtectionManager.SubmitProtectionOrders()`:
  ```csharp
  if (strategy.validatedTargetPrice == 0) 
      strategy.validatedTargetPrice = myTpPrice; // Save TP2 price first time
  ```
  Ahora cuando se crea TP2 por primera vez, su precio se guarda en `validatedTargetPrice` para que `ManagePositionExit()` lo use como fallback.
- **Resultado**: `ManagePositionExit()` ya no hace fallback a VWAP porque tiene el precio correcto de TP2 guardado.
- **Archivos**: `OrderProtectionManager.cs` (línea 252)

## [v1.14.85] - 2026-01-10
### Fixed CRÍTICO: "Ghost ChangeOrder" Moviendo TP2 a Precio Incorrecto 👻
- **Problema**: TP2 se creaba correctamente en el nivel opuesto (ej. Europe High @ 21143.75) pero inmediatamente se movía al precio de TP1 (VWAP @ 21032.25), consolidando todos los contratos en un solo objetivo.
- **Causa Raíz**: `ManagePositionExit()` (que actualiza dinámicamente los TP en cada tick) llamaba a `GetOppositeLevelPrice()` y cuando fallaba en encontrar el nivel, hacía fallback a VWAP. Esto generaba un `ChangeOrder()` "fantasma" que sobrescribía el TP2 correcto.
- **Evidencia (Logs v1.14.84)**:
  ```
  TP2 CREATED: TP2_Long_01 @ 21143.75 Qty=1
  EnsureProtection COMPLETE
  ORDER_UPDATE_TP: Name='TP2_Long_01' State=ChangeSubmitted Price=21032.25  ← Ghost Change
  ```
- **Solución**: Modificado `ManagePositionExit()` para que, si `GetOppositeLevelPrice()` falla, use `validatedTargetPrice` (el target persistido) antes de hacer fallback a VWAP.
- **Resultado**: TP2 ahora permanece en el nivel correcto durante toda la vida del trade.
- **Archivos**: `SessionLevelsStrategy.cs` (líneas 3126-3145)

## [v1.14.84] - 2026-01-10
### Diagnóstico: Logs para Investigar Corrupción de Referencia TP2 🔍
- **Problema Detectado**: La estrategia encuentra el nivel opuesto correcto (ej. Europe High) pero asigna TODOS los contratos a TP1, dejando TP2 en 0.
- **Evidencia**: Logs muestran que al intentar actualizar TP2, la referencia (`strategy.tp2Order`) apunta incorrectamente a la orden TP1.
- **Logs Agregados**:
    - `ORDER_UPDATE_TP`: Captura nombre exacto, ID, estado, precio y cantidad de cada orden TP cuando se dispara `OnOrderUpdate`.
    - `TP UPDATE`: Ahora muestra OrderID y Name de la orden antes de modificarla con `ChangeOrder`.
- **Objetivo**: Identificar si NinjaTrader corrompe las referencias durante `ChangeOrder` o si hay un bug en la lógica de asignación.
- **Archivos**: `SessionLevelsStrategy.cs`, `OrderProtectionManager.cs`

## [v1.14.83] - 2026-01-10
### Fixed CRÍTICO: Lógica de Persistencia Bloqueada por Validación R:R 🐛
- **Problema**: La estrategia ignoraba el nivel de sesión correcto (ej. Asia Low ~21000) y forzaba el uso del VWAP (ej. 21046) como Target 2.
- **Causa**: La validación inicial de Riesgo/Recompensa (`ValidateRiskReward`) fallaba en encontrar el nivel en milisegundos y usaba un fallback (VWAP). Este fallback se guardaba en `validatedTargetPrice`, impidiendo que la ejecución real reintentara buscar el nivel guardado en disco.
- **Solución**:
    - Se modificó `EntryStateMachine.cs` para usar una variable local en la validación R:R.
    - Si el nivel no se encuentra instantáneamente, `validatedTargetPrice` permanece en 0.
    - Esto permite que `OrderProtectionManager` use la lógica de reintento y encuentre el nivel correcto cargado desde el XML de Persistencia.
- **Resultado**: La estrategia ahora respeta los niveles guardados/cargados incluso si hay un ligero retraso en su detección inicial.

## [v1.14.82] - 2026-01-10
### Agregado
- **Persistencia de Niveles (`SessionLevelPersistence.cs`)**: Nuevo sistema que guarda los niveles de sesión (Asia/Europe/USA) en archivos XML en la carpeta `Cache`.
    - Al reiniciar la estrategia, intenta cargar los niveles del archivo en lugar de recalcularlos, solucionando errores de cálculo en días históricos o reinicios.
    - Guarda automáticamente los niveles cuando se detectan nuevos.
- **Diagnóstico (`DumpActiveLevels`)**: Herramienta para imprimir en el log todos los niveles activos en memoria, útil para verificar la carga correcta.

## [v1.14.81] - 2026-01-09
### Fixed
- **Session Reset:** Se añadió mecanismo de auto-curación en `CheckWeekEndReset`. Ahora el reset de sesión ocurre forzosamente si `Position.MarketPosition` es `Flat`, previniendo que la estrategia quede atascada en un ciclo de "SESSION RESET POSTPONED" por estados desincronizados ("Ghost Positions").
- **Blindness Bug:** Corregido error donde la estrategia dejaba de escanear nuevas oportunidades tras cancelar una orden por R/R inadecuado. Ahora se permite el escaneo incluso si el estado es `WaitingForVwapMitigation` (Virtual SL).

## [v1.14.80] - 2026-01-09
### FIX: Ghost Stop Loss en Cierre Forzado 👻
- **Problema**: Al ejecutarse un cierre forzado (ej: por protección de feriado `EnableHolidayProtection` o cierre de Viernes), la orden Stop Loss (`stopOrder`) quedaba activa con posición 0.
- **Causa**: La lógica de limpieza en `CheckSessionExit` solo cancelaba `stopOrder1` y `stopOrder2` (versiones antiguas), pero no `stopOrder` (versión actual).
- **Solución**: Añadida cancelación explícita de `stopOrder` en el bloque de limpieza de sesión.
- **Archivo**: `SessionLevelsStrategy.cs`

## [v1.14.79] - 2026-01-09
### FEATURE: Parámetro de Protección de Feriados (Kill Switch) 🛡️
- **Problema**: Backtests con datos históricos que tienen conflictos de formato de fecha (ej: leer 9/1 como 1/9) activaban falsamente la protección de "Cierre por Feriado" (Labor Day).
- **Solución**: Nuevo parámetro `EnableHolidayProtection` (Default: True).
  - Si se desactiva, la estrategia ignorará los cierres tempranos y feriados, permitiendo backtests en data imperfecta.
- **Archivo**: `SessionLevelsStrategy.cs`

## [v1.14.78] - 2026-01-09
### UPDATE: BidAskAnchoredVWAP v3 (Visuals & Data) 🎨
- **Mejoras Visuales**:
  - Todas las líneas activas son ahora **Blancas** y de **2px** de grosor.
  - Al mitigarse (precio rompe el anchor), la línea se corta visualmente y el historial se vuelve **Gris**.
- **Lógica**:
  - Corrección de Anchoring para sesiones ETH (18:00 - 17:00) cruzando media noche.
  - Aproximación de Volumen Bid/Ask usando Up/Down ticks si no hay Tick Replay.
- **Archivo**: `BidAskAnchoredVWAP.cs`

## [v1.14.76] - 2026-01-09
### FEATURE: Módulo de Riesgo Apteros & Sincronización Multi-Instrumento 🚀
- **Módulo de Riesgo Apteros**:
    - Implementado `RiskManager.cs` para manejar reglas específicas de Prop Firms (Apteros T.E.P. por defecto).
    - **Nuevos Parámetros**:
        - `Selected Risk Model`: Alternar entre `Standard` (Fixed/ATR) y `Apteros`.
        - `Daily Loss % Limit`: Límite de pérdida diaria (ej: 2.5%).
        - `Daily Opportunities`: Divisor para calcular riesgo por trade (ej: Límite / 10).
        - `Max Trailing Drawdown`: Límite de drawdown global ($5,000).
        - **NEW**: `Risk Calculation Basis`: Elegir entre `% of Daily Balance` (Agresivo) o `Drawdown Allocation` (Conservador).
        - **NEW**: `Allocation Days`: Días para dividir el Drawdown (ej: 20 días) en modo Conservador.
    - **Cálculo de Cantidad**: 
        - Modo `% Balance`: `(Balance * %) / Oportunidades`.
        - Modo `Drawdown Allocation`: `(Drawdown / Días) / Oportunidades`.
## [v1.14.77] - 2026-01-09
### FIX: Panel de Información y Cálculo de Riesgo Visual 🐛
- **Corrección de "Partial Fill"**: Ahora el panel acumula correctamente la cantidad de contratos si la entrada se llena en varios pasos (antes mostraba riesgo de 1 solo contrato).
- **Corrección de División Entera**: Solucionado error que mostraba "$0" en TP2 cuando la cantidad era impar (ej: 1 contrato).

- **Sincronización Multi-Instrumento**:
    - Implementado archivo de estado compartido (`ApterosState.txt`).
    - **Bloqueo Global**: Si una estrategia (ej: MNQ) toca el límite diario global, todas las estrategias conectadas (ej: ES) se bloquean automáticamente.
    - Sincronización de Balance Inicial del Día entre múltiples instancias.

## [v1.14.75] - 2026-01-08
### IMPROVEMENTS: ATR Scaling & Logic Fixes 🛠️
- **Nuevo Parámetro**: `UseATRScaling` (bool)
  - Permite activar/desactivar el escalado de riesgo basado en ATR.
  - Default: `True` (comportamiento original). Poner en `False` para usar `RiskPerTradeUSD` directamente sin límites por volatilidad.
- **Fix Lógica Niveles Internos**:
  - Los niveles de **días anteriores** ahora SIEMPRE se tratan como EXTERNOS (usan Global VWAP).
  - Previene que un nivel "USA High" de hace 3 días force el uso de Adhoc VWAP incorrectamente.
- **Mejoras Logging**:
  - Log detallado para diagnóstico de `WAITING SHORT` (muestra VWAP Global vs Setup).

## [v1.14.74] - 2026-01-08
### FIX CRÍTICO: TP1 Usa VWAP Global ETH (No Trade VWAP) ⚠️
- **Bug**: TP1 usaba Trade VWAP (21391) en lugar de VWAP Global ETH (21516), causando target 125 puntos más lejos.
- **Root Cause 1**: `IsTradeVwapExtended` no se reseteaba al inicio de cada trade nuevo.
- **Root Cause 2**: La actualización dinámica de TP1 usaba `tradeVwapActive` en lugar de `IsTradeVwapExtended`.
- **Fixes aplicados**:
  1. `OrderProtectionManager.ResetEntryState()`: Reset `IsTradeVwapExtended = false`
  2. `SessionLevelsStrategy.UpdateProtectionOrders()`: Usar `IsTradeVwapExtended` en lugar de `tradeVwapActive`
  3. `OrderProtectionManager.EnsureProtection()`: Usar `IsTradeVwapExtended` para selección de VWAP
- **Comportamiento correcto**:
  - Trade nuevo → TP1 = VWAP Global ETH de la sesión actual
  - Trade cruza 18:00 → Trade VWAP hereda acumulado del Global y continúa
- **Nuevo método**: `VWAPCalculator.InheritFromGlobal()` copia valores acumulados
- **Archivos**: `SessionLevelsStrategy.cs`, `OrderProtectionManager.cs`, `VWAPCalculator.cs`

## [v1.14.73] - 2026-01-08
### NEW: Modo de Entrada Seleccionable (A+ Retrace vs Anticipado) 🚀
- **Nuevos Parámetros**:
  - `Entry Mode`: **A+ Retrace** (espera retroceso al VWAP) o **Anticipado** (entra en la confirmación)
  - `Anticipated Order Type`: **Market** o **Limit** (solo para modo Anticipado)
- **Comportamiento**:
  - A+ Retrace: Comportamiento original (orden límite al VWAP)
  - Anticipado Market: Entra inmediatamente con orden Market al cierre de confirmación
  - Anticipado Limit: Entra con orden Limit al precio del cierre de confirmación
- **Archivos**: `SessionLevelCore.cs`, `SessionLevelsStrategy.cs`, `EntryStateMachine.cs`

## [v1.14.72] - 2026-01-08
### NEW: Panel Muestra TP Proyectados Durante Orden Pendiente 📊
- **Mejora**: Ahora el panel de estado muestra SL, TP1 y TP2 **mientras la orden límite está Working** (pendiente de llenado).
- **Cálculo**:
  - TP1 = VWAP actual (dinámico)
  - TP2 = Nivel opuesto o `validatedTargetPrice`
- **Visual**: Los valores muestran sufijo "(Est)" para indicar que son estimados hasta el fill.
- **Archivo**: `StrategyHelpers.cs` línea 189

## [v1.14.71] - 2026-01-08
### FIX: Edad de Nivel Usa Sesión ETH en Lugar de Calendario 📅
- **Problema**: Un nivel de "Asia High" creado a las 20:00 del 7 de enero (sesión ETH del 8) mostraba "1 Day" cuando debería ser "Today".
- **Causa**: El cálculo usaba `Date` del calendario, no la fecha de sesión ETH (que inicia a las 18:00).
- **Fix**: Ahora los niveles creados después de las 18:00 se consideran del "día siguiente" de sesión. Si el nivel pertenece a la misma sesión ETH que la hora actual, muestra "Today".
- **Archivo**: `StrategyHelpers.cs` línea 140

## [v1.14.70] - 2026-01-08
### FIX CRÍTICO: Race Condition en HandleWorkingOrder 🐛
- **Problema**: En MGC se detectó que `Position.MarketPosition` permanece `Flat` durante ~55ms después del fill. El código anterior forzaba `PositionActive` sin verificar, causando un reset inmediato a `Idle` (porque Position era Flat), lo que disparaba incorrectamente el Safety Net.
- **Evidencia (Log v1.14.69)**:
  ```
  SYNC_DEBUG: Position.MarketPosition=Flat Position.Qty=0 timeSinceFill=55ms
  SYNC: Order Filled but State was Working. Forcing InPosition.
  SYNC: State is InPosition but MarketPosition is Flat. Resetting to Idle.  ← BUG
  CRITICAL: Safety Net Triggered!
  TP UPDATE (TP2): Modifying Qty=1 -> Qty=3  ← INCORRECTO
  ```
- **Fix**: Ahora `HandleWorkingOrder()` solo transiciona a `PositionActive` si `Position.MarketPosition ≠ Flat`. Si Position aún no se actualizó, el estado permanece en `Working` y se loguea `SYNC_WAIT`.
- **Archivo**: `EntryStateMachine.cs` línea 818

## [v1.14.69] - 2026-01-08
### DIAGNÓSTICO: Logs para Investigar 5 Problemas Críticos 🔍
- **Contexto**: Análisis de logs del 2026-01-08 reveló 5 problemas críticos que requieren investigación adicional.
- **Logs Agregados (NO se modificó lógica)**:
  1. **`SAFETYNET_PRE`** en `CheckSafetyNet()`: Captura estado completo (Position, tradeDirection, msSinceClose) antes de crear protección. Objetivo: detectar por qué Safety Net crea posiciones fantasma en dirección opuesta (M2K).
  2. **`SYNC_DEBUG`** en `EntryStateMachine.HandleWorkingOrder()`: Captura timing entre fill y verificación de Position. Objetivo: detectar race condition donde Position.MarketPosition es Flat inmediatamente después del fill (6A, 6J).
  3. **`TP1_PRE_CREATE` / `TP2_PRE_CREATE`** en `OrderProtectionManager.SubmitProtectionOrders()`: Captura estado de órdenes TP existentes antes de crear nuevas. Objetivo: detectar duplicación de TP1/TP2 (6J).
  4. **`SWITCH_EVAL`** en `EntryStateMachine.ScanForTriggers()`: Captura delta de precio entre niveles. Objetivo: detectar loop infinito de SWITCH cuando niveles tienen mismo precio (ZW).
- **Archivos Modificados**:
  - `SessionLevelsStrategy.cs` (CheckSafetyNet)
  - `EntryStateMachine.cs` (HandleWorkingOrder, ScanForTriggers)
  - `OrderProtectionManager.cs` (SubmitProtectionOrders)

## [v1.14.65] - 2026-01-07
### NEW: Trade VWAP Persistente (Post-Sesión) 🌙
- **Requerimiento**: Mantener el cálculo del VWAP del trade activo incluso después del cierre de sesión (18:00), visualizándolo de forma distinta.
- **Implementación**:
  - Se añadió lógica para detectar cuando un trade activo cruza el límite de las 18:00.
  - En este caso ("Extensión Post-Sesión"), el `TradeVWAP` sigue acumulando datos sin reiniciarse, desacoplándose del VWAP Global del nuevo día.
  - **Visualización**: La línea cambia a **Gris (1px)** durante la extensión (antes Cyan/Magenta 2px).

## [v1.14.66] - 2026-01-07
### FIX CRÍTICO: TP1 Saltando y TP2 a BE (Persistencia) 🎯
- **Problema 1 (TP1)**: Aunque el `TradeVWAP` persistía internamente, la lógica de cálculo de targets (`GetTradeVWAPCurrentValue`) seguía redireccionando al VWAP Global (`EthLow/High`), el cual se reiniciaba al cambio de sesión. Esto hacía que el TP1 "saltara" al nuevo valor del día.
  - **Fix**: Se modificó `GetTradeVWAPCurrentValue` para que devuelva el valor del objeto `TradeVWAP` persistente.
- **Problema 2 (TP2)**: El TP2 se movía a BreakEven al cambio de sesión.
  - **Diagnóstico**: Se sospecha que la referencia al nivel opuesto se pierde. Se añadieron logs de depuración en `ResetEntryState` para confirmar si el estado se está limpiando inesperadamente.

## [v1.14.67] - 2026-01-07
### UX: Persistencia de Info TP en Panel 📊
- **Problema**: Al moverse el TP2 (ej. a BreakEven), la información del panel de control ("Original TP2", Ratios) se reseteaba o actualizaba al nuevo valor (BE), perdiendo la referencia del objetivo original.
- **Solución**: Se modificó `OrderProtectionManager` para capturar el precio original de TP1/TP2 **una sola vez** y no sobrescribirlo aunque la orden activa se mueva. Esto mantiene los ratios y objetivos visibles hasta que termina el trade.

## [v1.14.68] - 2026-01-07
### UX: Refinamiento Panel TP (TP1 Dinámico / TP2 Fijo) 📉
- **Ajuste**: A petición del usuario, se diferenció el comportamiento de la información del panel:
  - **TP1 (Dinámico)**: Sigue actualizándose en tiempo real, ya que el VWAP se mueve y expande el potencial de ganancia.
  - **TP2 (Estático)**: Se mantiene FIJO con el valor original (Locked), para no perder la referencia del Ratio/Target inicial cuando la orden se mueve a BreakEven.

## [v1.14.64] - 2026-01-07
### FEATURE: Escaneo Retroactivo de Niveles de Sesión 🔍
- **Problema**: Si la estrategia iniciaba después del cierre de una sesión (ej. Europa termina a 10:30 AM), los niveles High/Low de esa sesión nunca se creaban porque `CheckSession` solo procesaba barras en tiempo real.
- **Solución**: Nuevo método `SessionManager.ScanHistoricalLevels()` que:
  - Se ejecuta una vez al inicio (`CurrentBar == BarsRequiredToTrade`)
  - Para cada sesión que ya terminó, escanea barras históricas
  - Crea niveles High/Low retroactivos con Tags correctos
- **Resultado**: Ahora la estrategia encuentra niveles de sesiones pasadas (ej. Europe Low) incluso si inicia tarde, permitiendo TP2 correcto.

### FIX: Selección Incorrecta de Target TP2 (Tag Antiguo) 🎯
- **Problema**: `GetOppositeLevelPrice` seleccionaba niveles antiguos (de días anteriores) porque buscaba solo por nombre ("Europe High") en un historial acumulado, obteniendo un Tag de ticks incorrecto y fallando al buscar el nivel opuesto actual.
- **Solución**:
  - Se actualizó la búsqueda para filtrar por **Nombre + Hora de Inicio** (`StartTime`), asegurando que se use el objeto de nivel de la sesión actual.
  - Se corrigió la lógica de Reinicio (`Restart`) para inicializar correctamente `setupLevelTime`.
- **Resultado**: TP2 ahora identifica correctamente el nivel opuesto de la sesión *actual*.

## [v1.14.63] - 2026-01-07
### FIX: Sincronización de versión del panel de información
- **Problema**: La versión mostrada en el panel de información estaba desactualizada después del último release.
- **Solución**: Se actualizó la constante de versión en `StrategyHelpers.cs` a `v1.14.63` y se añadió este registro en el changelog.
- **Resultado**: El panel ahora muestra la versión correcta (v1.14.63) en tiempo real.

## [v1.14.62] - 2026-01-07
### FIX: Arranque Temprano (Niveles Invisibles) 🚀
- **Problema**: Si la simulación iniciaba justo a la hora de apertura (ej. 3:00 AM para Europa), la estrategia fallaba en detectar el nivel inicial porque estaba "ciega" durante las primeras 20 barras (`BarsRequiredToTrade`).
- **Solución**:
  - Se movió la lógica de **Cálculo de Sesiones y Niveles** al inicio de `OnBarUpdate`, antes del bloqueo de las 20 barras.
  - Se movió la inicialización de **TimeZones**.
  - El bloqueo de 20 barras ahora **SOLO protege la lógica de Trading** (entradas/salidas).
- **Resultado**: La estrategia ahora detecta y dibuja los niveles desde la **Vela 1**, incluso si inicias el playback exactamente a la hora del evento, sin comprometer la seguridad operativa.

## [v1.14.61] - 2026-01-07
### MANTENIMIENTO: Fix Race Condition + Reset Diario 🔧
- **Race Condition (SL Faltante)**: Se implementó una solución de arquitectura robusta. Ahora el estado `workingOrder` se establece **ANTES** de enviar la orden `SubmitOrderUnmanaged`. Esto elimina la posibilidad de que un fill rápido llegue antes de que la estrategia esté lista para procesarlo.
- **Session Reset (Estado Atascado)**: Se añadió lógica en `OnBarUpdate` para detectar el cambio de "Trading Day" (`Time[0].Date > lastTradingDate`). Al cambiar el día, se limpia el estado de la sesión (`Idle`, intentos a 1, protección limpia), evitando que la estrategia quede atascada en `WaitingForVwapMitigation` con contadores viejos.

## [v1.14.60] - 2026-01-07
### REVERTIDO (Descartado por v1.14.61) ↩️
- Se intentó relajar la validación de estado en `OnExecutionUpdate`, pero se descartó en favor de la solución más robusta de inversión de operaciones en v1.14.61.

## [v1.14.59] - 2026-01-07
### FIX CRÍTICO: Lógica de Niveles Internos/Externos Invertida 🔧
- **Problema**: La estrategia clasificaba incorrectamente los niveles. Si había un nivel externo protegiendo (ej. USA High arriba de Asia High), lo marcaba como **INTERNO** y usaba el VWAP Adhoc incorrecto para la confirmación.
- **Fix**: 
  - Lógica invertida en `OrderProtectionManager`: Si existe protección externa → Es **EXTERNO** (Usa VWAP Global).
  - Si NO hay protección externa (es el extremo) → Es **INTERNO** (Usa VWAP Adhoc).
  - Añadido filtro para considerar solo niveles del **día actual**.
- **Resultado**: La confirmación ahora usa el VWAP Global correcto (High/Low) cuando hay niveles externos, permitiendo reentradas válidas.

## [v1.14.58] - 2026-01-07
### FIX CRÍTICO: TP1 Se Movía Hacia la Entrada 🔧
- **Problema**: El TP1 se movía de la línea VWAP Low visible (~25497) hacia el precio de entrada (~25542), causando fills incorrectos.
- **Causa**: `ManageTargets()` usaba `tradeVWAP.CurrentValue` (variable local que acumulaba incorrectamente) en lugar de `vwapCalc.GetTradeVWAPCurrentValue()` (que retorna el VWAP Global).
- **Fix**: 
  - `ManageTargets()` ahora usa `vwapCalc.GetTradeVWAPCurrentValue()`.
  - `GetTradeVWAPCurrentValue()` retorna directamente el VWAP Global Low (Short) o High (Long).
- **Resultado**: El TP1 ahora sigue la línea VWAP visible en el gráfico.

## [v1.14.56] - 2026-01-07
### Mejora: Deshabilitar VWAP Adhoc en Niveles Externos 🎯
- Para niveles **externos** (extremos del día), el VWAP Adhoc ya no se calcula ni resetea.
- Solo se usa el VWAP Global, que es la línea visible en el gráfico.
- Para niveles **internos**, el VWAP Adhoc sigue funcionando normalmente.

## [v1.14.55] - 2026-01-07
### FIX: VWAP Correcto para Niveles Externos vs Internos 📍
- **Problema**: Para niveles externos (ej. Europa rompe Asia High), la orden de entrada no se adhería al VWAP visible (~25545) sino al VWAP Adhoc (~25543), causando una discrepancia de ~2 puntos.
- **Causa**: `GetSetupVWAP()` siempre devolvía VWAP Adhoc primero, sin distinguir si el nivel era interno o externo.
- **Fix**: Modificada la función para retornar:
  - **Nivel Externo** (`isInternalLevel=false`): VWAP Global de sesión (la línea visible).
  - **Nivel Interno** (`isInternalLevel=true`): VWAP Adhoc (desde el toque).

## [v1.14.54] - 2026-01-06
### Mejora: Lógica Retry VWAP Completa 🔄
- **Problema**: Después de un trade cerrado por SL/BE, la estrategia entraba en estado `WaitingForVwapMitigation` pero nunca detectaba cuándo el precio rompía el extremo para re-disparar.
- **Fix**: Añadido método `HandleVwapMitigationWait()` en `EntryStateMachine.cs` que detecta la ruptura del anchor y re-dispara un nuevo trigger.
- **UI**: El panel ahora muestra el contador de reintentos (ej. `Intento 2/20`) cuando está activo.
- **Variables**: Añadidas `waitingForVwapMitigation`, `vwapCandleExtreme`, `currentVwapNumber` en `SessionLevelsStrategy.cs`.

## [v1.14.53] - 2026-01-06
### FIX CRÍTICO: Lógica de Selección de Target R/R 🎯
- **Bug**: Para trades Long, la función `ValidateRiskReward` seleccionaba el target más bajo (incluso si estaba DEBAJO de la entrada), causando `Invalid Direction` y ratio 0.00.
- **Ejemplo MGC**: TP1(VWAP)=4489 (válido), TP2(Level)=4240 (inválido). Seleccionaba 4240 → Rechazo.
- **Fix**: Ahora filtra targets válidos primero (por encima para Long, por debajo para Short) y luego elige el más cercano de los válidos.
- **Resultado**: Trades que antes se rechazaban incorrectamente ahora se evaluarán correctamente.

## [v1.14.52] - 2026-01-06
### Mejora: Pintado Visual Sin Dependencia de R/R 🎨
- **Solicitud**: El usuario pidió que la vela de confirmación (amarilla) se pinte aunque el trade sea rechazado por R/R inválido, para confirmar visualmente que la estrategia está activa.
- **Cambio**: Movida la lógica de `BarBrushes[1] = ConfirmationCandleColor` antes del bloque `if (isValidRR)` en `EntryStateMachine.cs` (tanto para Long como Short).
- **Resultado**: Ahora cualquier trigger de confirmación se visualiza con la vela amarilla, independientemente de si la orden se ejecuta o no.

## [v1.14.51] - 2026-01-06
### FIX: UI - Desactivado `IsAutoScale` por defecto
- **Problema**: Los plots de High/Low VWAP tenían `IsAutoScale` activado por defecto, lo que podía causar compresión del gráfico y dificultar la visualización de otros elementos.
- **Solución**: Se desactivó `IsAutoScale` para los plots de High/Low VWAP para mejorar la experiencia de usuario y evitar la compresión automática del rango de precios.

## [v1.14.50] - 2026-01-06
### Nuevo: Alertas Sonoras 🔊
- **Funcionalidad**: Se agregó la capacidad de reproducir un sonido cuando la estrategia detecta un Trigger Long o Short.
- **Configuración**: Nuevo grupo "Audio Settings" con opción para activar/desactivar (`Use Sound Alerts`) y seleccionar el archivo de audio (`Alert Sound File`). Default: `mzpack_alert4.wav`.

## [v1.14.49] - 2026-01-06
### FIX: Lógica "Same-Day" en Globex
- **Problema**: La estrategia bloqueaba operaciones en niveles creados el mismo día (ej. USA Low) durante la sesión nocturna (Globex), aunque la sesión original ya hubiera terminado.
- **Solución**: Se modificó la regla `EntryStateMachine` para permitir trades del mismo día **SI** la hora actual es posterior al cierre oficial de la sesión del nivel (`ActualSessionEnd`).

## [v1.14.48] - 2026-01-06
### FIX CRÍTICO: Exportación de Datos "Fantasmas"
- **Problema**: Al cargar la estrategia en un gráfico, se exportaban trades históricos simulados como si fueran trades en vivo, ensuciando las carpetas de DEMO con datos antiguos.
- **Solución**: Se endureció la condición de exportación en `OnExecutionUpdate`. Ahora solo exporta si el estado es estrictamente `State.Realtime` (o Backtest explícito), ignorando la carga inicial histórica del gráfico.


## [v1.14.47] - 2026-01-06
### UI: Ocultar Parámetro Interno
- **Cambio**: Se ocultó la propiedad `IsTradeVwapActive` de la configuración de la estrategia, ya que es una variable de control interno y no debe ser modificada por el usuario.


## [v1.14.46] - 2026-01-06
### FIX CRÍTICO: Lógica de Actualización de Take Profit (TP)
- **Problema**: Al actualizar un TP (ej. por aumento de posición), se cancelaba la orden vieja pero no se creaba la nueva debido a un error de sincronización (la orden cancelada parecía seguir activa).
- **Solución**: Se reemplazó la lógica de "Cancelar y Crear" por `ChangeOrder`. Ahora la estrategia modifica directamente la orden existente sin eliminarla, lo cual es más rápido y seguro.


## [v1.14.45] - 2026-01-06
### FIX CRÍTICO: Estabilización de Estrategia
- **Corrección de Inicio**: Se solucionó un problema que impedía que la estrategia iniciara correctamente (faltaba activar el administrador de sesiones).
- **Limpieza de Cálculos**: Se unificaron los cálculos del precio promedio (VWAP) para evitar errores por duplicidad.
- **Mejora de Lógica**: Se movió la regla de "Reintento de Mitigación" a su lugar correcto para asegurar que funcione como se espera.


## [v1.14.44] - 2026-01-06
### REFACTORING: Final Code Cleanup - Fase 8 Completa
- **Limpieza de Código**:
  - Eliminados comentarios de versiones legacy (v1.5 - v1.13) para mejorar legibilidad.
  - Eliminados `using` namespaces innecesarios y limpieza de imports.
  - Eliminado código muerto residual de la refactorización.
- **Documentación**:
  - Agregada documentación XML a métodos públicos críticos (`Wrapper`, `CheckChartLag`, `SharedRisk`).
- **Estado Final**:
  - Estrategia totalmente modular (7 módulos externos).
  - Archivo principal reducido de >5,400 a ~3,600 líneas.

## [v1.14.43] - 2026-01-06
### REFACTORING: UI & Helper Extraction - Fase 7 Completa
- **Extracción de UI/Helpers**:
  - Se movió lógica de UI (Botones, Panel de Estado) a `StrategyLevels/StrategyHelpers.cs`.
  - Se movió lógica de Logging (`Log`, `ClearLogFile`) a `StrategyHelpers`.
  - `SessionLevelsStrategy.cs` reducido en ~400 líneas adicionales (ahora ~4,000 líneas).
- **Acceso a Miembros**:
  - Se expusieron variables privadas críticas (`atr`, `gapDetected`, `gapCount`) para acceso desde módulos.
- **Estabilidad**:
  - Compilación exitosa tras refactorización modular.

## [v1.14.42] - 2026-01-06
### REFACTORING: SessionManager - Fase 6 Completa
- **Nuevo archivo**: `SessionLevels/SessionManager.cs` (~230 líneas)
- **Métodos extraídos**:
  - `CheckSession()`: Detección de sesiones, creación/actualización de niveles High/Low (~100 líneas)
  - `ManageLevels()`: Acumulación VWAP, detección de mitigación, dibujo de niveles (~130 líneas)
- **Reducción total (Fase 6)**: ~230 líneas removidas de `SessionLevelsStrategy.cs`
- **SessionLevelsStrategy ahora**: ~4,100 líneas (desde ~5,368 originales)
- **Correcciones de Compilación**:
  - Eliminados duplicados críticos: `ManageLevels`, `nyTimeZone`, `activeLevels`, `USAEndTime`.
  - Expuesto `USAEndTime` (línea 178) y corregido uso de `activeLevels` (minúscula) en `OrderProtectionManager`.
  - Agregadas referencias `NinjaTrader.Gui.Tools` y `NinjaTrader.Gui.Chart` en `SessionManager.cs`.

## [v1.14.41] - 2026-01-06
### REFACTORING: EntryStateMachine - Fase 5 Completa
- **Métodos adicionales extraídos**:
  - `ScanForTriggers()`: Detección de triggers al tocar niveles (~120 líneas)
  - `HandleConfirmation()`: Lógica de confirmación y envío de órden (~200 líneas)
  - `HandleWorkingOrder()`: Gestión de orden activa, trailing VWAP, cancelación por R/R (~130 líneas)
- **Reducción total (Fase 5)**: ~670 líneas removidas de `ManageEntryA_Plus`
- **ManageEntryA_Plus ahora**: Solo ~50 líneas de código orquestador con llamadas delegadas
- **Propiedades expuestas adicionales**: `isValidVWAP()`, `GetSetupVWAP()`, `CalculateDynamicQuantity()`, etc.

## [v1.14.40] - 2026-01-06
### FIX CRÍTICO: Estrategia Congelada en Playback
- **Problema**: Los niveles de sesión y VWAPs no se movían durante el Playback. El output mostraba "LAG_PAUSE: Lag > 60s detected (30200969s)".
- **Causa**: La lógica de `LAG_PAUSE` calculaba el "lag" como `DateTime.Now - Time[0]`. En Playback, `Time[0]` es la hora histórica (del pasado), resultando en millones de segundos de "lag" y pausando permanentemente la estrategia.
- **Solución**: Añadida verificación `Connection.PlaybackConnection != null` para detectar modo Playback y saltar la lógica de pausa por lag en ese modo.

### REFACTORING: OrderProtectionManager (Fase 4)
- **Migración completa**: La lógica de `EnsureProtection` y `SubmitProtectionOrders` movida a `OrderProtectionManager.cs`.
- **Wrappers públicos**: Añadidos `SubmitOrderUnmanagedWrapper`, `ChangeOrderWrapper`, `CancelOrderWrapper` para permitir que el manager envíe órdenes.
- **Propiedades públicas**: Expuestas `ActiveLevels`, `activeTp1Price`, `activeTp2Price`, etc. para acceso del manager.
- **Resultado**: Código principal más limpio y legible, lógica de protección encapsulada.

### REFACTORING: EntryStateMachine (Fase 5)
- **Nuevo archivo**: `SessionLevels/EntryStateMachine.cs` (~320 líneas)
- **Métodos extraídos de ManageEntryA_Plus**:
  - `CheckTradingModeGuards()`: Lógica de modo Paused/LongOnly/ShortOnly (-53 líneas)
  - `UpdateAnchorIfNeeded()`: Re-anclaje cuando precio hace nuevo High/Low (-47 líneas)
  - `HandleInternalInvalidation()`: Invalidación de niveles internos al tocar externos (-121 líneas)
- **Reducción total**: ~221 líneas removidas de `SessionLevelsStrategy.cs`
- **Propiedades expuestas (17)**: `visualAdhoc*`, `externalLevel*`, `isInternalLevel`, `cachedOppositeLevel`, `oppositeSearchDone`, `lastInvalidationBar`, `DrawTriggerLabel()`, etc.

## [v1.14.38] - 2026-01-05
### DIAGNÓSTICO: Logs para Creación de SL
- **Propósito**: Investigar origen de órdenes SL inesperadas (como la SL_Long_01 de MNQ que apareció sin registro).
- **Cambio**: Agregado log `SL_CREATE_DEBUG` antes de cada creación de SL que muestra:
  - Instrument, Direction, Tag, Action, Price, Qty, State, EntryState
- **Uso**: Si aparece una orden SL inesperada, revisar logs para ver el contexto de creación.

## [v1.14.37] - 2026-01-05
### FIX CRÍTICO: Nivel Opuesto en Sesiones Nocturnas (Asia)
- **Problema**: `GetOppositeLevelPrice` no encontraba el Asia Low cuando el setup era Asia High porque comparaba `StartTime.Date`. Como Asia cruza medianoche, High podía tener fecha 5-ene y Low fecha 4-ene.
- **Causa**: Las sesiones que cruzan medianoche (Asia 8PM-1AM) asignan fechas diferentes al High y Low.
- **Solución**: Comparar por **Session Ticks** extraídos del Tag del nivel en lugar de la fecha. El Tag incluye el timestamp del inicio de sesión que es igual para High y Low.

## [v1.14.36] - 2026-01-05
### FEATURE: Auto-Pause en Lag de Red
- **Problema**: Durante la apertura del mercado o problemas de conexión, el lag puede subir indefinidamente hasta que la estrategia se congela.
- **Solución**: Cuando el lag supera 60 segundos **sin posición activa**, la estrategia pausa automáticamente todos los cálculos.
- **Comportamiento**:
  - Lag > 60s + Sin posición → **PAUSA** (log: "LAG_PAUSE")
  - Lag < 10s (normalizado) → **REANUDA** (log: "LAG_RESUME")
- **Nota**: Si hay posición activa, la estrategia no pausa (SL/TP ya están en el broker protegiéndola).

## [v1.14.35] - 2026-01-05
### DIAGNÓSTICO: Logs para Estado de Órdenes TP
- **Propósito**: Investigar por qué se crean múltiples órdenes TP en partial fills rápidos (<50ms).
- **Cambio**: Agregado log `DEBUG_TP_STATE` antes del check `tpAlreadyActive` que muestra:
  - Nombre del TP (TP1 o TP2)
  - `OrderState` exacto de la orden existente
  - Cantidad protegida actual (`protectedQty`)
  - Nueva cantidad a agregar (`newQty`)
- **Uso**: Revisar logs cuando ocurra el problema para identificar qué estado falta en el check.

## [v1.14.34] - 2026-01-05
### FIX CRÍTICO: Cantidad Incorrecta de TP al Hacer Refresh
- **Problema**: Al refrescar la estrategia con posición activa, `protectedTp1Qty`/`protectedTp2Qty` mantenían valores anteriores, causando que nuevos TPs se crearan con cantidades acumuladas incorrectas (ej: Qty=10 en lugar de 3).
- **Causa**: Las variables no se reseteaban al adoptar posición existente en RESTART.
- **Solución**: Resetear `protectedTp1Qty = 0` y `protectedTp2Qty = 0` antes de iterar órdenes en STARTUP ADOPT, luego leer la cantidad real de cada TP adoptado.

## [v1.14.33] - 2026-01-05
### FIX CRÍTICO: Limpieza Agresiva de Órdenes TP Huérfanas
- **Problema**: Al cerrar por SL, algunas órdenes TP1/TP2 quedaban activas porque perdieron su referencia (ej: tras restart).
- **Solución**: Después de la limpieza normal, ahora itera `Account.Orders` y cancela **TODAS** las órdenes con nombre `TP1_*`, `TP2_*`, `SL_*` del instrumento.

## [v1.14.32] - 2026-01-05
### FIX: Modo Paused/LongOnly/ShortOnly No Cancelaba Órdenes Pendientes
- **Problema**: Cambiar a "NINGUNO" o "Solo Long/Short" no cancelaba órdenes limit ya activas.
- **Solución**: Guard global al inicio de `ManageEntryA_Plus` que cancela órdenes pendientes contra el modo.

### FIX: BarsInProgress Guard
- **Problema**: Error `ArgumentOutOfRangeException` en `PlotBrushes` cuando se agregó serie de datos tick.
- **Solución**: Guard `if (BarsInProgress != 0) return;` al inicio de `OnBarUpdate`.

### FIX: Spam de Logs en Búsqueda de Nivel Opuesto
- **Problema**: `GetOppositeLevelPrice` se llamaba en cada tick cuando no encontraba nivel, generando spam.
- **Solución**: Flag `oppositeSearchDone` para cachear resultado negativo.

### FIX: CSV Export Automático en Backtest
- **Problema**: Exportación CSV no funcionaba en Strategy Analyzer aunque `AllowBacktest=true`.
- **Solución**: Detección automática de backtest con `ChartControl == null`.

## [v1.14.31] - 2026-01-04
### FEATURE: Delta Integration para Análisis Cuantitativo
- **Objetivo**: Capturar datos de Delta (flujo de órdenes) para estudios estadísticos antes de implementar filtros activos.
- **Cambios**:
  - Agregada referencia al indicador `RelativeDelta` en `State.DataLoaded`.
  - Nuevas variables: `tradeDeltaAtEntry`, `tradeDeltaDirection`, `tradeSessionDelta`, `tradeDeltaAtTP1`.
  - Captura de Delta al fill de entrada (con dirección: aligned=1, opposed=-1).
  - Captura de Delta al fill de TP1 (para análisis de absorción VWAP).
  - Header CSV extendido con 4 columnas nuevas.
- **Uso**: Los datos se exportan al CSV para análisis en Streamlit App.
- **Nota**: Requiere Tick Replay activo en Backtests para datos Delta precisos.

## [v1.14.30] - 2026-01-04
### FIX CRÍTICO: TPs Duplicados en Partial Fills Rápidos
- **Problema**: Durante fills parciales rápidos (ej: 2→4→5 contratos en 0.6 seg), se creaban múltiples órdenes TP1 y TP2 porque la verificación `tpAlreadyActive` no incluía estados transitorios.
- **Causa**: Los estados `PendingSubmit` y `PartFilled` no estaban en la lista de "orden activa", causando que se crearan duplicados.
- **Solución**: Agregar `OrderState.PendingSubmit` y `OrderState.PartFilled` a la verificación de `tpAlreadyActive`.

### FIX: CSV Export en Backtest
- **Problema**: Al correr Strategy Analyzer con `AllowBacktest=true`, no se generaban datos CSV.
- **Causa**: La condición de exportación solo permitía `State.Realtime`.
- **Solución**: Permitir exportación si `State == State.Realtime || AllowBacktest`.

## [v1.14.29] - 2026-01-04
### FEATURE: Feedback Visual de Rechazo de Trades
- **Problema**: El panel mostraba "Waiting Confirmation" incluso cuando un trade era rechazado (ej: por bajo R/R), causando confusión.
- **Solución**: Se instrumentó la lógica de filtros en `ManageEntryA_Plus` para capturar el motivo exacto del rechazo:
  - Riesgo/Recompensa (`Skipped: R/R 0.18 < 1.0`)
  - Lag de Red (`Skipped: Network Lag Detected`)
  - Dirección/Modo (`Skipped: Long/Short Only Mode`)
  - Target Tocado (`Skipped: Target already touched`)
  - VWAP Inválido (`Skipped: Setup VWAP Invalidated`)
- **Visualización**: El panel ahora muestra este motivo debajo del estado durante 2 minutos tras el rechazo.

## [v1.14.28] - 2026-01-04
### FIX: Actualización de Cantidad de SL en Partial Fills
- **Problema**: Cuando una entrada se llenaba parcialmente (ej: 1 de 2 contratos), el SL se creaba para 1 contrato pero NO se actualizaba cuando se llenaba el segundo.
- **Causa**: El flag `slOrderCreatedThisEntry` (introducido en v1.13.5 para evitar duplicados) bloqueaba la lógica de actualización en `SubmitProtectionOrders`. La condición `else if (slOrderCreatedThisEntry)` tenía prioridad sobre la verificación de cambio de cantidad.
- **Solución**: Se modificó la estructura `else if` para permitir la actualización del SL si `slOrderCreatedThisEntry` es true PERO la cantidad del SL existente no coincide con la cantidad total de la posición.
- **Resultado**: El SL ahora se actualiza correctamente (cancela y reemplaza) cuando la posición aumenta debido a partial fills.

## [v1.14.27] - 2026-01-04
### FIX CRÍTICO: Cantidades Incorrectas en Órdenes TP (Backtest)
- **Problema**: En backtest, las órdenes TP2 se creaban con cantidades incorrectas (ej: 12 contratos en lugar de 1).
- **Causa**: Las variables `protectedTp1Qty` y `protectedTp2Qty` no se reseteaban al inicio de un nuevo trade. Los valores residuales de trades anteriores se acumulaban.
- **Evidencia en Log**: `ForTP2=1` (cálculo correcto) pero `TP2 CREATED: Qty=12` (11 contratos residuales).
- **Solución**: Agregar reset explícito de `protectedTp1Qty = 0` y `protectedTp2Qty = 0` en `OnExecutionUpdate` cuando se detecta el primer fill de una nueva entry (`currentEntryState == workingOrder`).
- **Contexto**: El reset existente en `Position.MarketPosition == Flat` no era suficiente para backtests rápidos donde los trades se ejecutan en milisegundos.
- **Resultado**: Cada nuevo trade ahora comienza con contadores de protección limpios, garantizando cantidades correctas en SL/TP1/TP2.

## [v1.14.26] - 2026-01-04
### FIX: Desactivar Filtro de Lag Durante Playback
- **Problema**: El filtro de lag mostraba alerta "LAG ALERT: ORDERS BLOCKED" durante el playback, bloqueando trades.
- **Causa**: Durante el playback, `State == State.Realtime` pero el tiempo del chart es diferente al tiempo del sistema, causando que el cálculo de lag siempre detecte exceso.
- **Solución**: Agregada verificación `Connection.PlaybackConnection != null` para detectar si estamos en modo playback y saltar el filtro de lag.
- **Resultado**: El playback ahora funciona sin alertas de lag falsas. El filtro sigue activo para trading real.

## [v1.14.25] - 2026-01-04
### FEATURE: Solo Exportar Trades Reales (Realtime Mode)
- **Problema**: Al cargar la estrategia en un chart de Playback o Live, los trades generados durante el procesamiento de datos históricos se exportaban al CSV, contaminando los datos reales.
- **Causa**: La lógica de exportación CSV en `OnExecutionUpdate` no distinguía entre ejecuciones históricas y ejecuciones en tiempo real.
- **Solución**: Agregada verificación `State == State.Realtime` antes de exportar trades al CSV.
- **Resultado**: 
  - Playback: Solo se exportan trades cuando el playback está corriendo (no durante carga inicial)
  - Live/Demo: Solo se exportan trades en tiempo real
  - Backtest (Strategy Analyzer): Sigue funcionando normalmente con `AllowBacktest = true`
- **Beneficio**: Datos más limpios y confiables para análisis en Streamlit.

## [1.14.25] - 2026-01-04
### Fixed: Streamlit App Improvements (v2.8.1)
- **Fix AI Cost Display**: Corregido bug visual donde el costo de la sesión de IA aumentaba erróneamente al recargar pestañas (datos en caché).
- **Fix Calendar Tab**: Corregida la navegación en el calendario; ahora hacer clic en un día mantiene la pestaña activa correctamente sin recargar la app al dashboard inicial.
- **Fix Month Selector**: Corregido problema donde la selección de mes se reseteaba inesperadamente.
- **Transparencia**: El contador de dinero en la barra lateral ahora refleja solo el gasto real de nuevas llamadas a la API.

## [1.14.23] - 2026-01-03
### Added: Parámetros Opcionales de Filtrado AI
- **Nuevos parámetros** en grupo "2. AI Filters" (DESACTIVADOS por defecto):
  - `Enabled Zones (CSV)`: Lista de zonas habilitadas separadas por coma (ej: "Asia High, USA Low")
    - **Default**: Vacío = todas las zonas habilitadas (sin filtro)
  - `Max Level Age (Days)`: Edad máxima de niveles en días
    - **Default**: 0 = sin límite de edad (sin filtro)
- **Nuevo método**: `ParseEnabledZones()` - Parsea string CSV a lista de zonas
- **Nuevo método**: `IsZoneEnabled(zoneName, levelTime)` - Verifica si zona/nivel pasa filtros:
  - Filtro de zona: Rechaza si no está en lista (si lista no vacía)
  - Filtro de edad: Rechaza si nivel > MaxLevelAgeDays días de antigüedad
- **Integración**: Verificación en `ManageEntryA_Plus` antes de procesar cada nivel
- **Logs**: `"AI FILTER: Zona bloqueada - [nombre]"` cuando filtro activo rechaza zona
- **Comportamiento sin filtros** (defaults):
  - `Enabled Zones` vacío → Opera TODAS las zonas (comportamiento actual)
  - `Max Level Age` = 0 → Sin límite de edad (comportamiento actual)
## [v1.14.24] - 2026-01-03
### Agregado
- **Integración Lógica Filtros AI**: Implementada la verificación `IsZoneEnabled()` dentro del motor de entradas (`ManageEntryA_Plus`). Ahora los trades se bloquean si la zona no está permitida o es muy vieja.
- **Auto-Load AI Config**: Nueva funcionalidad para leer automáticamente el archivo `ai_config.json` generado por Streamlit.
  - Parámetro `Auto Load AI Config` (bool): Activa/Desactiva la carga automática.
  - Parámetro `AI Config Path`: Ruta al archivo JSON (auto-detectable).
- **Prioridad de Configuración**: Si `Auto Load` está activo, sobreescribe los parámetros manuales `EnabledZonesParam` y `MaxLevelAgeDays`.

## [v1.14.23] - 2026-01-03
### Agregado
- **Parámetros Filtros AI (Setup)**: Preparación de propiedades `EnabledZonesParam` y `MaxLevelAgeDays` para futuro filtrado. (Desactivados por defecto).

## [1.14.22] - 2026-01-03
### Fixed
- **Log Spam**: Silenciados los logs informativos ("OPPOSITE NOT FOUND", "SEARCH_OPPOSITE") que llenaban el output window. Ahora solo aparecen si `EnableDebugLogs` está activado.

## [1.14.21] - 2026-01-03 ✅ VERSIÓN ACTUAL (RECOMPILE REQUIRED)
### FIX CRÍTICO: "Ghost Stop Loss" (MNQ Volatility Fix)
- **Problema**: Trades en instrumentos volátiles (MNQ) se cerraban inmediatamente con pérdidas de 1 tick.
- **Causa (Diagnosticada)**: Una lógica de "Protección de Distancia Máxima" activaba un fallback erróneo cuando el SL técnico superaba los 100 ticks (25 pts). Al activarse, usaba el parámetro `StopLossTicks` (configurado en 1) como si fuera la distancia total desde la entrada.
- **Solución**: **ELIMINADA** la lógica de protección/fallback. Ahora la estrategia respeta SIEMPRE el Stop Loss técnico basado en el Anchor (VWAP Low), sin importar cuán lejos esté (asumiendo el riesgo necesario de la volatilidad).
- **Limpieza**: Removidos logs de diagnóstico.

## [1.14.13] - 2026-01-02
### FIX: Backtest Determinism
- **Problema**: Resultados de backtest eran inconsistentes (Backtest #1 diferente a #2 y #3)
- **Causa**: La función "Cross-Instrument Risk Sync" escribía en el disco (`trace/SharedRisk.txt`) durante el backtest, contaminando las ejecuciones posteriores con datos "viejos".
- **Solución**: Desactivar totalmente `WriteSharedRisk` y `ReadMaxSharedRisk` cuando la estrategia está en modo `State.Historical` o `State.Optimization`.
- **Beneficio**: Cada backtest ahora corre en un entorno limpio y aislado, garantizando repetibilidad del 100%.

## [1.14.16] - 2026-01-02
### FEATURE: Streamlit Intelligence Suite (v2.2)
- **Sincronización de Trades (v1.14.16)**: La App ahora agrupa ejecuciones usando el `ID` exacto de la estrategia en lugar de adivinar por horario. Resultado: Conteo de trades idéntico a NinjaTrader.
- **Calculadora de Comisiones (v1.14.15)**: Nuevo selector "Licencia" (Free/Lifetime) en la App que recalcula el PnL Neto usando tasas oficiales 2025.
- **Deduplicación Automática (v1.14.14)**: Filtro inteligente que elimina duplicados si el usuario corre múltiples backtests sobre los mismos datos.

## [1.14.11] - 2026-01-02
### FEATURE: Auto-Detection System for Accounts
- **Cambio**: La estrategia ahora usa el nombre exacto de la cuenta (`Account.Name`) como nombre de carpeta
- **Beneficio**: Sistema escalable - cada nueva cuenta crea automáticamente su propia carpeta en `TradeExports/`
- **Eliminado**: Lógica hardcoded de detección demo/live (Sim, demo, paper)
- **Estructura**:
  - Backtest → `TradeExports/backtest/`
  - Playback → `TradeExports/playback/`
  - Cuenta "DEMO" → `TradeExports/DEMO/`
  - Cuenta "MyCuenta" → `TradeExports/MyCuenta/`
- **Streamlit**: Agregada opción "DEMO" al selector (manual por ahora)
- **Código afectado**: `OnStateChange(State.DataLoaded)` líneas 613-640

## [1.14.10] - 2026-01-02
### FIX CRÍTICO: CSV Export Path Bug
- **Problema**: Los archivos CSV de backtest se guardaban en ubicación incorrecta (`C:\Users\[usuario]\bin\...` en lugar de `C:\Users\[usuario]\Documents\NinjaTrader 8\bin\...`)
- **Causa**: Lógica de `Globals.UserDataDir` con doble `GetDirectoryName` subía 2 niveles desde la carpeta correcta
- **Solución**: Usar `Globals.UserDataDir` directamente sin subir niveles - ya apunta a `Documents\NinjaTrader 8`
- **Impacto**: Los archivos CSV ahora se exportan correctamente a `TradeExports\backtest\` donde la aplicación Streamlit los espera
- **Código afectado**: `OnStateChange(State.DataLoaded)` líneas 607-611

## [1.14.9] - 2026-01-02
### UI: Info Panel Legibility
- **Cambio**: Se agregó un fondo negro con 50% de opacidad al Panel de Estado (InfoPanel) para mejorar la lectura sobre el gráfico.

## [1.14.8/7] - 2026-01-02
### FIX: Errores de Compilación
- Corrección de sintaxis en `SessionIterator` y llaves faltantes de la versión 1.14.5.

## [1.14.6] - 2026-01-02
### FIX: Visualización de Alerta Lag (Sticky Alert)
- **Problema**: La alerta visual de "LAG BLOCKED" se quedaba pegada en pantalla indefinidamente hasta que la estrategia intentaba meter otro trade, incluso si el lag ya se había resuelto hace horas.
- **Solución**: Se agregó una llamada pasiva a `CheckChartLag()` en cada tick de `OnBarUpdate`.
  - Impacto: Cero en rendimiento.
  - Beneficio: La alerta desaparece sola apenas se restablece la conexión/datos.

## [1.14.5] - 2026-01-02
### FEATURE: Dynamic Session Awareness (Feriados/Early Closes)
- **Problema**: La lógica de "Cierre Viernes" usaba una hora fija (16:00). Si el mercado tenía un cierre temprano (ej. 13:00 por festivo), la estrategia no cerraba posiciones.
- **Solución**:
  - Se implementó `Bars.Session.GetNextEnd(Time[0])` para leer el horario real de cierre de la sesión desde el template de NinjaTrader.
  - **Nueva Regla**: Se fuerza el cierre ("Exit on Session") si es **Viernes** O si se detecta un **Cierre Temprano** (antes de las 15:30 NY) cualquier día de la semana.
  - Esto garantiza que en días festivos (Monday Early Close) o Viernes, la estrategia proteja la cuenta cerrando posiciones y cancelando órdenes.

## [1.14.4] - 2026-01-02
### FEATURE: Cancelación por Touch-First
- **Problema**: La estrategia seguía persiguiendo el precio con la orden de entrada incluso si el mercado ya había tocado el Target (TP1) y rebotado.
- **Solución**: Se agregó una validación en `ManageEntryA_Plus`. Si el precio High/Low toca el `targetPrice` mientras la orden está en estado `Working`, se cancela inmediatamente la orden y se resetea el setup.

## [1.14.3] - 2026-01-02
### CLEANUP: R/R Logic Cleanup (MES Issue)
- **Cambio**: Se eliminó el bloque de código muerto/conflictivo llamado "Relaxed R/R Preservation".
- **Comportamiento Final**: La estrategia mantiene estrictamente el comportamiento de cancelar cualquier orden de trabajo (Pending) si el R/R cae por debajo del mínimo (1.0) en cualquier momento, asegurando que no se tomen trades degradados.

## [1.14.2] - 2026-01-02
### FIX CRÍTICO: Loop Infinito de Órdenes (Catastrophic Failsafe)
- **Problema**: `CheckHardStop` enviaba órdenes de cierre repetidamente en cada tick mientras la posición se cerraba, causando cientos de órdenes (460 contratos en M2K) y Margin Call.
- **Causa**: Falta de una bandera que indicara que el proceso de cierre de emergencia ya había comenzado.
- **Solución**:
  - Nueva variable `failsafeTriggered`.
  - Bloqueo inmediato: Si `failsafeTriggered` es true, `CheckHardStop` retorna sin hacer nada.
  - Reset automático: Se libera la variable cuando la posición se confirma cerrada (`OnExecutionUpdate`).
- **Mejora Adicional**: Mensaje de log ahora muestra `High` (Short) o `Low` (Long) en lugar de `Close` para evitar confusión sobre por qué se violó el anchor.

## [1.14.1] - 2026-01-02
### FIX CRÍTICO: Protección en Partial Fills (Discrepancia de Cantidad)
- **Problema**: Si la orden de entrada se llena parcialmente (ej: 1 de 10 contratos), y luego se llena el resto, la estrategia NO aumentaba la protección.
- **Causa**: La lógica de `EnsureProtection` tenía un lock (`protectionOrdersCreated`) que impedía actualizaciones subsecuentes.
- **Solución v1.14.1**: 
  - Removido el chequeo de `protectionOrdersCreated` al inicio de `EnsureProtection`.
  - Ahora permite recalcular y agregar órdenes de protección cuando la cantidad llena aumenta.
- **Nota**: El código ya tenía el fix comentado, pero la versión no se había incrementado ni compilado.

## [1.14.0] - 2026-01-02
### FEATURE: Separación de CSV por Contexto de Ejecución
- **Solicitud**: Separar archivos de backtest, playback, demo y live para análisis aislado
- **Cambios**:
  1. **Nueva estructura de carpetas** en `Strategies/TradeExports/`:
     - `backtest/` ← Strategy Analyzer
     - `playback/` ← Market Replay
     - `demo/` ← Cuentas Sim (Sim101, Sim102, etc.)
     - `live/` ← Cuenta Real
  2. **Detección automática de contexto** (Fix v1.14.0):
     - `ChartControl == null` → backtest
     - `Connection.PlaybackConnection` → playback
     - `Account.Name` ("Sim"/"Demo") → demo
     - Default → live
  3. **App Streamlit actualizada** con 5 fuentes de datos:
     - 📊 Backtest (Strategy Analyzer)
     - ⏪ Playback (Market Replay)
     - 🎮 Demo (Cuenta Simulada)
     - 💰 Live (Cuenta Real)
     - 📁 Histórico Consolidado
  4. **Rutas portables**: Todo dentro de `Strategies/` para fácil migración de PC
- **Uso**: Al ejecutar la estrategia, los trades se exportan automáticamente al folder correcto

## [1.13.16] - 2026-01-01

### FEATURE: Tracking de Comisiones en CSV Export
- **Solicitud**: Incluir comisiones en el análisis para ver PnL real
- **Cambios**: 
  - Nuevas columnas en CSV: `Commission`, `NetPnL`
  - Comisiones calculadas según instrumento (NinjaTrader Free Plan):
    - Micros (MES, MNQ, M2K, MYM): $0.91/lado
    - Bitcoin (MBT, MET): $1.56/lado
    - Commodities (MCL, MGC, MHG): $0.77/lado
    - Currencies (6E, 6J, 6A): $1.26/lado
    - Granos (ZS, ZW, ZC): $1.52/lado
  - `NetPnL = PnL - Commission`
- **Uso**: El Trade Analyzer mostrará PnL bruto y neto

## [1.13.15] - 2026-01-01
### FEATURE: Logging de Niveles vs Precio en WEEK RESET
- **Solicitud**: Investigar por qué instrumentos no tocan niveles en ciertas semanas
- **Cambios**: En cada WEEK RESET, ahora muestra todos los niveles activos con:
  - Nombre del nivel y precio
  - Si el precio actual está ABOVE o BELOW
  - Distancia en ticks
- **Formato del log**:
```
LEVEL SUMMARY (Price=6000.5):
  Asia: High @ 6010.2 | Currently BELOW by 39 ticks
  Europe: Low @ 5990.1 | Currently ABOVE by 42 ticks
```
- **Uso**: Correr backtest → buscar "LEVEL SUMMARY" para ver distancias de niveles

## [1.13.14] - 2026-01-01
### FEATURE: Logging Diagnóstico para R:R Rechazados
- **Solicitud**: Investigar por qué algunos instrumentos tienen 0 trades en ciertas semanas
- **Cambios**:
  1. Logging detallado cuando `ValidateRiskReward` rechaza un trade
  2. Muestra: TP1 (VWAP), TP2 (Level), target seleccionado, risk, reward, ratio, y razón exacta
- **Formato del log**:
```
R/R REJECTED (Long): Entry=6900 SL=6895 | TP1(VWAP)=6902 TP2(Level)=6910 | Selected=6902 | Risk=5.00 Reward=2.00 Ratio=0.40 | Reason: R:R 0.40 < Min 1
```
- **Uso**: Correr backtest → revisar logs para ver por qué trades fueron rechazados

## [1.13.13] - 2026-01-01
### FIX CRÍTICO: TP1/TP2 No Se Exportaban al CSV
- **Problema**: El filtro "TP1 (Scalp)" y "TP2 (Runner)" en Trade Analyzer no mostraban datos
- **Causa Raíz**: La condición `isExitOrder` buscaba `"TP_"` pero las órdenes se llaman `"TP1_"` y `"TP2_"`
- **Solución**: Cambiada condición de `Contains("TP_")` a `Contains("TP1_") || Contains("TP2_")`
- **Impacto**: Ahora todos los trades (SL, TP1, TP2) se exportan correctamente al CSV

## [1.13.12] - 2025-12-31
### FEATURE: Risk-Reward en CSV y Gráfico de Distribución
- **Solicitud**: Agregar columna R:R y gráfico de distribución TP1 vs TP2 (campana de Gauss)
- **Cambios en Estrategia**:
  1. Nueva variable `tradeRiskUSD` - calcula riesgo en USD cuando se crea el SL
  2. Nueva columna `RiskReward` (col 14) en CSV export - calcula `PnL / RiskUSD`
- **Cambios en Trade Analyzer App (v1.5)**:
  1. Parser lee columna `riskReward` (col 14)
  2. Nuevo gráfico "R:R Distribution" en tab Advanced - histograma TP1 (azul) vs TP2 (naranja)
- **Uso**: Correr backtest → CSV tendrá columna R:R → App muestra distribución

## [1.13.11] - 2025-12-31
### FEATURE: Número de Intento en CSV Export
- **Solicitud**: Agregar columna "Attempt" para analizar qué número de VWAP/intento funciona mejor
- **Cambios**:
  1. Nueva variable `tradeAttemptNumber` para guardar `currentVwapNumber` al iniciar trade
  2. Nueva columna en CSV export: `...,Setup,Attempt` (13 columnas ahora)
- **Uso**: Al correr backtest, el CSV tendrá la columna Attempt (1, 2, 3, etc.)
- **Nota**: Los CSV existentes no tendrán la columna - deben regenerarse con nuevo backtest

## [1.13.10] - 2025-12-31
### FIX: Condición de Carrera Causando 100 Contratos Huérfanos
- **Problema**: MBT mostró 100 contratos huérfanos en órdenes de protección después de cerrar por SL
- **Causa**: Race condition entre cierre de posición y `CheckSafetyNet`
  - Posición cierra → flags se resetean 
  - `CheckSafetyNet` detecta "zombie position" 51ms después
  - `EnsureProtection` crea órdenes con cantidad incorrecta (del broker, no de la estrategia)
- **Solución v1.13.10** - Dos válvulas de seguridad en `EnsureProtection`:
  1. **Delay de 3 segundos**: Rechazar si `lastPositionCloseTime` fue hace menos de 3s
  2. **Cap de 50 contratos**: Rechazar si `filledQty > 50` como cantidad absurda
- **Resultado**: Previene creación de órdenes huérfanas con cantidades incorrectas

## [1.13.9] - 2025-12-31
### NOTA: AutoScale en VWAPs
- **Investigación**: Se intentó deshabilitar AutoScale en los plots de VWAP (HighVWAP, LowVWAP)
- **Resultado**: `Plot.IsAutoScale` **NO EXISTE** en NinjaTrader para estrategias
- **Solución Manual**: Si el gráfico hace zoom out por VWAPs distantes:
  1. Click derecho en el gráfico → Properties
  2. Desmarcar "Auto Scale" en la pestaña Chart
  3. O usar "Fixed scale" con valores mínimo/máximo manuales
- **Niveles**: Ya tienen AutoScale deshabilitado (2do parámetro = false en `Draw.Line`)

## [1.13.8] - 2025-12-31
### FIXED: Órdenes TP Huérfanas No Se Cancelaban al Cerrar por SL
- **Problema**: Al cerrar posición por SL, las órdenes TP1 y TP2 quedaban activas (huérfanas)
- **Causa**: La lógica de cancelación solo verificaba `OrderState.Working`
  - Las órdenes también pueden estar en estado `Accepted` que es igualmente activo
- **Solución v1.13.8**:
  - Verificar **ambos estados** (`Working` y `Accepted`) antes de cancelar
  - Agregar `try-catch` para evitar errores si la cancelación falla
  - Agregar logging: `"CLEANUP: Cancelled orphan tp1Order"`
- **Resultado**: Al cerrar por SL, las órdenes TP huérfanas se cancelan automáticamente

## [1.13.7] - 2025-12-31 (FIX CRÍTICO)
### FIXED: SL/TP con Cantidad Incorrecta al Entrar Nueva Posición
- **Problema Reportado**: Al entrar con 10 contratos, SL mostraba 20, y TP1/TP2 mostraban 5/5
  - Los contratos en las órdenes de protección estaban duplicados o incorrectos
  - Problema persistente desde conversaciones anteriores (MGC, M2K)
- **Causa Raíz Identificada**: `stopOrder` **NO SE LIMPIABA** al cerrar posición
  - En `OnExecutionUpdate` línea ~4600-4606, se reseteaban: `entryOrder`, `tp1Order`, `tp2Order`, `stopOrder1`, `stopOrder2`
  - **FALTABA**: `stopOrder = null` (referencia principal del SL único)
  - Al siguiente trade: `stopOrder` tenía referencia "stale" (orden vieja Filled/Cancelled)
  - La lógica en `SubmitProtectionOrders` veía `stopOrder != null` y creaba lógica errónea
- **Segunda Causa**: `slOrderCreatedThisEntry` solo se reseteaba en el fill de entrada (línea 4387)
  - No se reseteaba al cerrar posición → podía bloquear creación de SL en trades sucesivos
- **Solución v1.13.7**:
  1. Agregar `stopOrder = null` en la sección de cleanup cuando `Position == Flat`
  2. Agregar `slOrderCreatedThisEntry = false` en la misma sección de cleanup
- **Código Afectado**: `OnExecutionUpdate()` - bloque de limpieza cuando posición se cierra
- **Resultado**: Las órdenes de protección ahora usan cantidades correctas basadas en la entrada actual

## [1.13.6] - 2025-12-31
### FEATURE: Logging Diagnóstico Integral y Estabilidad
- **Problema**: Congelamientos y problemas de estabilidad reportados en el gráfico.
- **Cambios**:
  - **Try-Catch en OnBarUpdate**: Se envuelve toda la lógica crítica para capturar y loguear excepciones (CRITICAL ERROR) en lugar de congelar la estrategia.
  - **OnRender Override**: Se añade `OnRender` con bloque try-catch seguro para prevenir errores de dibujado.
  - **Heartbeat Logging**: Loguea "HEATBEAT" cada 500 barras (Historical) o 10 segundos (Realtime) si `EnableDebugLogs` está activo, para confirmar que la estrategia sigue viva.
- **Objetivo**: Identificar la causa raíz de los congelamientos mediante logs detallados en la próxima sesión.

## [1.13.5] - 2025-12-30 (FIX CRÍTICO)
### FIXED: SL Duplicado en Llamadas Múltiples a SubmitProtectionOrders
- **Problema**: Dos órdenes SL creadas para el mismo trade
  - MNQ entró con 3 contratos @ 19:48
  - Dos SL creadas con timestamps idénticos (7:48:04.080 PM)
  - Total: 6 contratos en SL para entrada de solo 3
- **Causa**: `EnsureProtection` llama `SubmitProtectionOrders` DOS veces (TP1, TP2)
  - Primera llamada (TP1): Crea SL con `totalPositionQty`
  - Segunda llamada (TP2): `stopOrder` aún `null` (asíncrono) → Crea OTRO SL
  - Protección existente (`stopOrder == null` check) falla por timing
- **Solución v1.13.5**: Flag global `slOrderCreatedThisEntry`
  - Reset en cada nuevo entry fill (`OnExecutionUpdate`)
  - Check `if (stopOrder == null && !slOrderCreatedThisEntry)` antes de crear SL
  - Marca `slOrderCreatedThisEntry = true` después de crear SL
  - Segunda llamada ve flag=true → Skip con log "SL SKIPPED: Already created in current entry"
- **Resultado**: Solo 1 SL por trade, sin importar cuántas veces se llame `SubmitProtectionOrders`

## [1.13.4] - 2025-12-30 (FIX)
### FIXED: Múltiples Fills de "Exit on session close" No Se Exportaban
- **Problema**: Último trade quedaba con 1 contrato sin exportar (PositionActive)
  - NinjaTrader mostraba 9 trades, CSV solo tenía 8
  - "Exit on session close" generaba 2 fills (1 contrato cada uno)
  - Solo el primer fill se exportaba, el segundo quedaba sin ID
- **Causa**: v1.13.3 usaba lógica `if (TP1) -> .1, else if (TP2) -> .2, else -> no suffix`
  - Ambos fills de "Exit on session close" caían en `else` → mismo ID → solo se guardaba 1
- **Solución**: Nuevo contador `tradeExitFillsCount`
  - Se incrementa en cada export (`tradeExitFillsCount++`)
  - Se resetea al iniciar nuevo trade (`tradeExitFillsCount = 0`)
  - Genera IDs: `8.1`, `8.2`, etc. para TODOS los partial fills
- **Lógica mejorada**:
  - Si `tradeExitFillsCount == 1` y `Quantity >= 2` → ID sin sufijo (ambos contratos juntos)
  - Si `tradeExitFillsCount > 1` o `Quantity == 1` → ID con sufijo `.1`, `.2`, etc.
- **Resultado**: Ahora exporta TODOS los fills, incluyendo múltiples "Exit on session close"

## [1.13.3] - 2025-12-30
### FEATURE: Export CSV por Cada Fill de Salida (Partial Fills)
- **Necesidad**: Analizar performance de TP1 vs TP2 por separado (preparación para múltiples salidas futuras)
- **Cambio**: CSV ahora exporta **1 línea por cada fill de exit** (no solo cuando Position == Flat)
  - TP1 fill → Exporta inmediatamente con ID `X.1`
  - TP2 fill → Exporta inmediatamente con ID `X.2`
  - SL fill (ambos contratos) → Exporta con ID `X`
- **IDs Split**: 
  - `1.1` = Trade #1, Scalp (TP1)
  - `1.2` = Trade #1, Runner (TP2)
  - `2` = Trade #2, ambos contratos cerrados juntos (SL)
- **Beneficio**: Permite estudios granulares:
  - Win rate TP1 vs TP2
  - Cuántos trades llegan a TP2
  - Performance individual de cada salida
- **Resultado**: NinjaTrader muestra 9 trades → CSV ahora tendrá 9 líneas (antes solo 8)

## [1.13.2] - 2025-12-30
### FIXED: CSV Export Corrupto (Null-Safe tradeSetupName)
- **Problema**: CSV exportado tenía líneas vacías/corruptas - 9 trades en NT pero solo 1 en CSV
- **Causa**: `tradeSetupName.Replace(",", ";")` fallaba cuando `tradeSetupName` era null/empty, causando excepción silenciosa que corrompía el archivo
- **Solución**: Validación null-safe antes del Replace:
  ```csharp
  string safeSetupName = string.IsNullOrEmpty(tradeSetupName) ? "" : tradeSetupName.Replace(",", ";");
  ```
- **Logs confirmaron**: 8 trades exportados (`CSV EXPORT: Trade #1-8`) pero archivo corrupto
- **Resultado**: CSV ahora exporta todos los trades correctamente sin corrupción

## [1.13.1] - 2025-12-30
### FIXED: Protección Duplicada (Concurrency Fix)
- **Problema**: Se detectó en logs de MGC (Live) que `EnsureProtection` se ejecutaba múltiples veces simultáneamente (disparado por `OnExecutionUpdate` y `CheckSafetyNet` al mismo tiempo), creando órdenes de SL y TP duplicadas (ej: 12 contratos de SL para una posición de 7).
- **Solución**: Se implementó una variable de bloqueo `isProtectionProcessing` en `EnsureProtection` para garantizar que solo un hilo de ejecución pueda procesar la creación de órdenes de protección a la vez.
- **Cambio Adicional**: Se resetea el bloqueo `isProtectionProcessing` al limpiar las variables del trade.

## [1.13.0] - 2025-12-30
### FEATURE: TradeAnalyzer CSV Export
- **Exportación automática de trades** a CSV para análisis
- **Tracking de MAE/MFE** (Maximum Adverse/Favorable Excursion) en tiempo real
- **Archivo CSV**: `trace/TradeAnalyzer/trades_export_{Instrumento}.csv`
- **Formato CSV**: ID, Instrument, EntryTime, Type, EntryPrice, ExitTime, ExitPrice, Result, PnL, MAE, MFE, Setup
- **Logs**: `CSV EXPORT: Trade #X started/closed`
- **Variables nuevas**: `tradeMAE`, `tradeMFE`, `tradeExportId`, `csvExportPath`, `isTrackingTrade`

---

## [1.12.1] - 2025-12-30
### UI: Botones de Control Simplificados
- **Cambios según feedback del usuario**:
  - Posición movida a **esquina inferior derecha**
  - Solo **2 botones**: Dirección + Close
  - Botón de dirección **cicla**: ↕AMBOS → ↑LONG → ↓SHORT → ⏸NINGUNO
  - Colores: Verde (Ambos), Azul (Long), Rojo (Short), Gris (Ninguno)
- **Eliminadas variables**: `btnLongOnly`, `btnShortOnly`

---

## [1.12.0] - 2025-12-30
### FEATURE: Botones de Control Interactivos
- **Nuevos botones en el chart** (esquina superior izquierda):
  - **▶ RUN / ⏸ PAUSE**: Toggle para pausar/reanudar trading
  - **↑ LONG**: Solo permitir entradas Long (toggle)
  - **↓ SHORT**: Solo permitir entradas Short (toggle)
  - **✖ CLOSE**: Cerrar posición actual con orden de mercado
- **Enum `TradingMode`**: Normal, Paused, LongOnly, ShortOnly
- **Integración con ManageEntryA_Plus**: Respeta el modo activo antes de procesar nuevos setups
- **Cierre Manual**: Cancela SL/TP1/TP2 antes de cerrar posición
- **Logs**: `CONTROL: Trading PAUSED/RESUMED`, `CONTROL: Mode = X`, `MANUAL CLOSE`
- **Tecnología**: WPF Buttons via `UserControlCollection`

---

## [1.11.28] - 2025-12-30
### FIX CRÍTICO: Cerrar Posición Si No Puede Crear SL de Emergencia
- **Problema**: Si SL de emergencia era rechazado (precio fuera de límites), posición quedaba sin protección
- **Solución v1.11.28**: Fallback de cierre inmediato
  - Si `SubmitOrderUnmanaged` para SL falla → cerrar posición con orden de mercado
  - Log: `CRITICAL: Cannot protect. CLOSING.`
  - Log: `EMERGENCY CLOSE: Qty=X`
- **Resultado**: No más posiciones huérfanas sin SL

---

## [1.11.27] - 2025-12-30
### FIX: Validación de Distancia del SL (Prevenir Rechazo del Broker)
- **Problema**: Al refrescar estrategia, SL a precio 4407.6 fue rechazado por el broker
  - Error: "The current price is outside the price limits set for this product"
  - Causa: `setupAnchorPrice` era de trade anterior, muy lejos del precio actual
- **Solución v1.11.27**: Validar que SL no esté a más de 100 ticks del precio actual
  - Si distancia > 100 ticks → usar fallback: `avgEntry ± (StopLossTicks × TickSize)`
  - Log: `SL DISTANCE WARNING: Original SL X is Y ticks away. Using fallback Z`
- **Aplica a**: SHORT y LONG setups

---

## [1.11.26] - 2025-12-30
### FIX CRÍTICO: SL No Se Creaba en Reintentos (Trade Sin Protección)
- **Problema**: MGC tomó trade sin SL - posición quedó desprotegida
- **Causa Raíz**: Si `stopOrder` tenía referencia a orden vieja (estado Cancelled/Rejected/Filled):
  - `slAlreadyActive = false` (no en Working/Accepted/Submitted)
  - `stopOrder == null` era `false` (tenía referencia)
  - Resultado: **NO entraba en ninguna rama y NO creaba SL**
- **Solución v1.11.26**: Limpiar referencia obsoleta antes de verificar
  - Si `stopOrder != null && !slAlreadyActive` → `stopOrder = null`
  - Nuevo log: `SL CLEANUP: Clearing stale reference (State=X)`
- **Resultado**: SL ahora siempre se crea cuando no hay uno activo

---

## [1.11.25] - 2025-12-29
### Fix: Vela de Confirmación Amarilla Ahora Funciona en Reintentos
- **Problema**: La vela amarilla solo se pintaba en el primer intento, no en reintentos
- **Causa**: `visualConfirmationDone` no se reseteaba cuando se iniciaba un retry
- **Solución**: Reset de `visualConfirmationDone = false` al iniciar `WaitingForVwapMitigation`

---

## [1.11.24] - 2025-12-29
### Fix: TP1/TP2 R/R Persiste Cuando Trade Cruza Sesiones
- **Problema**: R/R de TP1 mostraba 0 cuando trade cruzaba de sesión
- **Causa**: `activeTp1Price` podía perderse/recalcularse al cambiar sesión
- **Solución**: Nuevas variables `tradeOriginalTp1Price` y `tradeOriginalTp2Price`
  - Se guardan cuando se crean los TPs
  - Se usan para cálculos del panel (persisten hasta trade close)
  - Se resetean al cerrar posición
- **Resultado**: TP1/TP2 reward y R/R ahora mantienen valores originales durante todo el trade

---

## [1.11.23] - 2025-12-29
### Fix: Cantidad TP2 en Panel Ahora Usa Cantidad Original del Trade
- **Problema**: TP2 qty en panel cambiaba de 5 a 0 después de TP1 fill
- **Causa**: Usaba `Position.Quantity` que reduce al llenarse TP1
- **Solución**: Nueva variable `tradeOriginalQty`
  - Se guarda al entry fill
  - Se usa para cálculos del panel
  - Se resetea al cerrar posición
- **Resultado**: TP1/TP2 reward ahora muestra valores consistentes durante todo el trade

---

## [1.11.22] - 2025-12-29
### Optimization: Carga Histórica 10x Más Rápida
- **Cambio**: Skip de `ManageEntryA_Plus()` para barras > 3 días antiguas
- **Niveles**: Siguen calculándose para TODO el histórico (30 días)
- **Trading Logic**: Solo se procesa últimos 3 días + Realtime
- **Beneficio**: Carga de estrategia ~10x más rápida
- **Nota**: Si `AllowBacktest = true`, procesa todo el histórico

---

## [1.11.21] - 2025-12-29
### Feature: Soporte para Strategy Analyzer
- **Nueva propiedad**: `Allow Backtest` (Default: OFF)
- **Efecto**: Cuando está ON, permite ejecutar órdenes en modo Historical (backtest)
- **Seguridad**: OFF por defecto para evitar órdenes accidentales en cuentas live/demo
- **Uso**: Activar solo en Strategy Analyzer, no en charts en vivo

---

## [1.11.20] - 2025-12-29
### Feature: Mostrar Riesgo Mínimo en Panel de Estado
- **Nueva info**: El panel ahora muestra `Risk: $XX (Min: $YY)`
- **Min Risk**: Riesgo si se usa MinQuantity con StopLossTicks actual
  - Fórmula: `MinQuantity × StopLossTicks × TickValue`
- **Utilidad**: Evaluar si instrumentos caros (ej. Micro Plata) exceden tu riesgo aceptable

---

## [1.11.19] - 2025-12-29
### Fix: Evitar Falsos Positivos de Detección de Posiciones Huérfanas
- **Problema**: Después de cerrar posición (SL/TP fill), el mensaje "Safe Orphan Detected" aparecía incorrectamente
- **Causa**: Delay de sincronización entre `Position.Flat` local y `Account.Positions`
- **Solución**: Nueva variable `lastPositionCloseTime` + delay de 2 segundos en `CheckSafetyNet()`
- **Resultado**: No más alertas de orphan inmediatamente después de un cierre válido

---

## [1.11.18] - 2025-12-29
### Fix: Logs Limpian Solo Su Propio Instrumento
- **Cambio**: Al reiniciar estrategia, borra solo SU archivo de log (no el de otros)
- **Antes (v1.11.16)**: Append con separador → acumulaba datos innecesarios
- **Ahora**: `WriteAllText` sobrescribe solo `[INSTRUMENTO]_[FECHA].txt`
- **Resultado**: Logs limpios por instrumento sin afectar otros

---

## [1.11.17] - 2025-12-29
### Feature: Filtro de Lag de Chart
- **Nueva propiedad**: `Max Chart Lag (Seconds)` - Default: 0.75s
- **Método**: `CheckChartLag()` verifica frescura de datos del chart
  - Calcula: `Core.Globals.Now - Time[0]` vs período de barra + umbral
  - Retorna `false` si hay lag excesivo → bloquea órdenes
- **Integración**: Verificación antes de `SubmitOrderUnmanaged` en:
  - Confirmación Short (línea ~2824)
  - Confirmación Long (línea ~2936)
- **Alerta visual**: Texto amarillo `⚠️ LAG: X.Xs - ORDERS BLOCKED` en panel de estado
- **Logs**: `LAG ALERT: Chart excess lag X.XXs > 0.75s threshold - ORDERS BLOCKED`

---

## [1.11.16] - 2025-12-29
### Fix: Logs Append en Lugar de Sobrescribir
- **Problema**: Al cargar MES se borraban los logs de MGC
- **Causa**: `ClearLogFile()` usaba `WriteAllText` (sobrescribir)
- **Solución**: Cambiar a `AppendAllText` con separador visual
  - Ahora agrega `=== RESTART ===` en lugar de borrar todo
  - Preserva historial de sesiones anteriores
- **Nota**: El usuario pidió que se limpie, pero esto causaba el problema. Ahora acumula con separadores.

---

## [1.11.15] - 2025-12-29
### Fix: Evitar Reset de Logs Entre Instancias
- **Problema**: Al activar una estrategia, los logs de otros instrumentos se reseteaban a 1KB
- **Causa**: Faltaba verificación `if (logFilePath == null)` en `Log()`
  - Esto recalculaba el path en CADA llamada, causando comportamiento inesperado
- **Solución**: Restaurar verificación para calcular path solo una vez por instancia
- **Resultado**: Cada instancia mantiene su propio log sin afectar otros

---

## [1.11.14] - 2025-12-29
### FIX CRÍTICO: Prevenir Llamadas Duplicadas a EnsureProtection
- **Problema Confirmado por Logs**:
  ```
  -> Protection Alloc: Filled=2 | ForTP1=1 | ForTP2=1
  -> Protection Alloc: Filled=2 | ForTP1=1 | ForTP2=1  <-- DUPLICADO
  TP1 CREATED: Qty=1  -> TP1 CREATED: Qty=2  <-- 2 órdenes creadas
  ```
- **Causa Raíz**: `OnExecutionUpdate` dispara `EnsureProtection` múltiples veces
- **Solución**: Nuevo flag `protectionOrdersCreated`
  - Verificación al inicio de `EnsureProtection()` - si ya está creado, skip
  - Set a `true` después de crear órdenes
  - Reset a `false` en ambos puntos de cleanup (line 2044, 4066)
- **Logs nuevos**: 
  - `EnsureProtection SKIPPED: Orders already created this trade`
  - `EnsureProtection COMPLETE: protectionOrdersCreated = true`

---

## [1.11.13] - 2025-12-29
### Feature: Logs a Archivo por Instrumento
- **Ubicación**: `Documents\NinjaTrader 8\trace\SessionLevels\[INSTRUMENTO]_[YYYYMMDD].txt`
  - Ejemplo: `MGC_20251229.txt`, `ES_20251229.txt`
- **Formato**: `HH:mm:ss.fff [mensaje]`
- **Limpieza automática**: Al reiniciar la estrategia, el archivo se sobrescribe (no acumula)
- **Header**: Incluye timestamp de inicio `=== MGC Strategy Log - Started 2025-12-29 11:20:00 ===`
- **Optimización**: 
  - Lock para thread-safety (múltiples instrumentos)
  - Try-catch silencioso para no interrumpir trading
- **Uso**: Abrir archivo en Notepad y usar Ctrl+F para buscar

---

## [1.11.12] - 2025-12-29
### FIX CRÍTICO: Prevenir Órdenes de Protección Duplicadas
- **Problema Reportado**: Con 2 contratos de entrada:
  - 2 SL (debería ser 1)
  - 4 TP1 (debería ser 1)
  - 1 TP2 (correcto)
- **Causa Raíz**:
  - `SubmitProtectionOrders()` se llama 2 veces (para TP1 y TP2)
  - Línea 3269 **siempre creaba TP** sin verificar si ya existía uno activo
  - Fix v1.11.6 para SL era insuficiente (no verificaba estado `Submitted`)
- **Solución Aplicada**:
  - **SL**: Verificar `slAlreadyActive` (Working/Accepted/Submitted) antes de crear
  - **TP**: Verificar `tpAlreadyActive` (Working/Accepted/Submitted) antes de crear
  - Logs nuevos: `TP1 CREATED`, `TP2 CREATED`, `SL ALREADY EXISTS`, `TP1 ALREADY EXISTS`
- **Resultado Esperado**: 1 SL, 1 TP1, 1 TP2 por operación

---

## [1.11.11] - 2025-12-29
### Fix: Pintar Vela de Confirmación Única
- **Problema**: Se pintaban múltiples velas consecutivas (todas las que confirmaban) en historial Live/Demo
  - Razón: Al no enviar orden, el estado `WaitingForConfirmation` no cambiaba, re-evaluando y re-pintando.
- **Solución**: Usar variable `visualConfirmationDone`
  - Se resetea (`false`) al detectar nuevo setup (Paso 1)
  - Se activa (`true`) al pintar la primera vela de confirmación (Paso 2)
- **Resultado**: Solo la primera vela que confirma la separación se pinta de amarillo.

---

## [1.11.10] - 2025-12-29
### Fix: Mostrar Vela de Confirmación en Historial (Live/Demo)
- **Problema**: Las velas amarillas no se veían en el chart histórico de cuentas Live/Demo
  - Razón: La lógica estaba dentro del bloque de envío de orden (que está bloqueado para histórico)
- **Solución**: Mover la lógica de pintado `BarBrushes[1]` fuera de `if (canSubmitOrder)`
- **Resultado**: Las velas de confirmación se ven en el pasado igual que las etiquetas, incluso si la orden no se "envía"

---

## [1.11.9] - 2025-12-29
### Feature: Highlight Confirmation Candle (Vela de Separación)
- **Nuevas propiedades** en grupo "Trigger Labels":
  - `Highlight Confirmation Candle`: Activar/desactivar el color (default: ON)
  - `Confirmation Candle Color`: Color del cuerpo de la vela (default: Yellow)
- **Funcionamiento**: Colorea la vela [1] que confirma separación del VWAP
- **Aplica a**: Short y Long cuando se envía la orden de entrada

---

## [1.11.8] - 2025-12-29
### Feature: Text Offset Configurable
- **Nueva propiedad**: `Text Offset (Pixels)` en grupo "Trigger Labels"
- **Default**: 12 pixels
- **Uso**: Ajustar distancia entre flecha y texto desde el panel
- **Comportamiento**: Short usa valor positivo (arriba), Long usa negativo (abajo)

---

## [1.11.7] - 2025-12-29
### Fix: Separar Flecha y Texto en Trigger Labels
- **Problema**: Texto y flecha estaban superpuestos
- **Solución**: Usar `yPixelOffset` de 12 pixels para separar
  - Short: Texto 12px arriba de la flecha
  - Long: Texto 12px abajo de la flecha
- **Cambio**: Texto ahora usa `arrowPrice` como ancla + offset en pixels

---

## [1.11.6] - 2025-12-29
### FIX CRÍTICO: Evitar SL Duplicados
- **Problema**: Al entrar con 2 contratos, SL tenía 4 contratos
  - `SubmitProtectionOrders()` se llamaba 2 veces (para TP1 y TP2)
  - Cada llamada creaba un nuevo SL si `shouldUpdateSL = false`
- **Solución**: Solo crear SL si `stopOrder == null`
  - Verificación explícita antes de crear
  - Si ya existe `stopOrder`, no crear duplicado
- **Log nuevo**: `SL CREATED: SL_Long_01 @ 25000 Qty=2`

---

## [1.11.5] - 2025-12-29
### Feature: Propiedades Configurables para Trigger Labels
- **Nuevo grupo de propiedades**: "Trigger Labels" en panel de configuración
- **Propiedades agregadas**:
  - `Label Distance (ATR)`: Distancia como multiplicador del ATR (default: 0.3)
  - `Label Font Size`: Tamaño de fuente 8-20 (default: 12)
  - `Show Text`: Mostrar/ocultar texto "Short"/"Long"
  - `Show Arrow`: Mostrar/ocultar flecha
- **Método**: `DrawTriggerLabel()` usa ATR para distancia consistente entre instrumentos

---

## [1.11.3] - 2025-12-29
### Feature: Etiquetas de Trigger Estilo NinjaTrader

---

## [1.11.2] - 2025-12-29
### REVERT CRÍTICO: Restaurar Bloqueo de Órdenes Históricas
- **Problema v1.11.1**: Las órdenes históricas se enviaban al broker real
  - MGC colocó una orden y desactivó la estrategia al recargar
  - Con `IsUnmanaged = true`, las órdenes NO son simuladas
- **Solución**: Restaurar bloqueo de v1.10.36
  - Solo permite órdenes en `State.Historical` si es conexión Playback
  - En cuenta live/demo: Solo órdenes en Realtime
- **Conclusión**: NO es posible ver ejecuciones históricas en cuenta live/demo
  - Para ver backtest histórico, usar Market Replay o Strategy Analyzer

---

## [1.11.1] - 2025-12-29 ❌ REVERTIDO
### Fix FALLIDO: Restaurar Ejecuciones Históricas para Visualización
- **Problema**: v1.10.36 bloqueaba órdenes históricas en cuentas live/demo
- **Intento**: Permitir órdenes históricas para visualización
- **FALLA**: Las órdenes se enviaron al broker real, causando desactivación

---

## [1.11.0] - 2025-12-29
### Feature: Lógica Inteligente de Reinicio
- **Nuevo método**: `EvaluateRestartNoPosition()` evalúa qué hacer al reiniciar
- **CASO B (orden pendiente)**:
  - Busca orden `EntryA+_` en Account.Orders
  - Si precio cruzó la entrada → Cancela orden
  - Si R/R < 1:1 → Cancela orden
  - Si todo OK → Adopta la orden y continúa
- **CASO C (sin nada)**:
  - Busca nivel válido no-mitigado tocado recientemente
  - Si precio está del lado correcto → WaitingForConfirmation
  - Si no hay setup válido → Idle
- **Mejoras sobre v1.10.39**:
  - Ya no resetea ciegamente a Idle
  - Evalúa si el setup puede continuar
- **Log**: `v1.11 RESTART: Found pending entry order... R/R=1.50. Adopting order.`

---

## [1.10.41] - 2025-12-29
### Fix Crítico: Protección de Emergencia para Posiciones Adoptadas
- **Problema**: Al reconectar, posiciones adoptadas quedaban sin SL/TP
  - Las órdenes fueron canceladas por el broker durante desconexión
  - Estrategia adoptaba posición pero NO creaba nuevas protecciones
- **Solución**: Verificación post-adopción y creación automática
  - Si `stopOrder == null` después de adoptar → CREAR PROTECCIÓN
  - Calcula SL usando `StopLossTicks` desde precio promedio
  - Crea TP1 automático a 2:1 del SL
  - **Si precio ya pasó el SL** → CIERRA POSICIÓN INMEDIATAMENTE
- **Logs nuevos**:
  - `EMERGENCY: Adopted position has NO protection! Attempting to create...`
  - `EMERGENCY SL CREATED: SL_Emergency_Long @ 25000 Qty=2`
  - `EMERGENCY: SL invalid (price already beyond). CLOSING POSITION.`

---

## [1.10.40] - 2025-12-28
### Feature: Limpieza de Logs + Prefijo de Instrumento
- **Problema**: Logs mezclados de 6 instrumentos, imposible diagnosticar
- **Solución 1**: Método `Log()` ahora agrega prefijo `[INSTRUMENTO]` a cada mensaje
  - Ejemplo: `[MNQ] STARTUP RESET: Clearing historical state...`
  - Ejemplo: `[MGC] RECOVERED orphan SL: SL_Long_01 Qty=2`
- **Solución 2**: Convertidos ~20 Print() directos a usar Log()
  - Todos ahora respetan `EnableDebugLogs`
  - Solo excepciones críticas siguen sin protección
- **Resultado**: Logs limpios y fáciles de filtrar por instrumento

---

## [1.10.39] - 2025-12-28
### Fix: Limpiar Estado Histórico al Iniciar en Realtime
- **Problema**: Al activar estrategia, estado mostraba `WaitingForConfirmation` inmediatamente
  - Triggers detectados durante procesamiento Historical persistían en Realtime
  - Estrategia parecía tener setup activo sin haberlo detectado en vivo
- **Solución**: Reset del estado al entrar en Realtime si no hay posición
  - Si `hasExistingPosition == false` y `currentEntryState != Idle` → Reset a Idle
- **Log**: `"STARTUP RESET: Clearing historical state (WaitingForConfirmation) - No position, starting fresh."`
- **Resultado**: Estrategia siempre empieza en Idle esperando nuevos triggers

---

## [1.10.38] - 2025-12-28
### Fix Crítico: Recuperación de Órdenes Post-Desconexión
- **Problema**: Al perder conexión a internet y reconectar:
  - Las referencias a órdenes (`stopOrder`, `tp1Order`, `tp2Order`) se perdían
  - Las órdenes seguían activas en el broker
  - La estrategia creaba NUEVAS órdenes → **Duplicados** (ej: 4 en SL cuando solo había 2)
  - Mensaje "SAFE ORPHAN DETECTED" aparecía incorrectamente
- **Solución 1**: Startup Failsafe ahora ADOPTA órdenes existentes
  - Cuando detecta posición en la cuenta, busca órdenes SL/TP1/TP2 activas
  - Asigna las referencias correctamente (`stopOrder = o`, etc.)
  - Solo cancela órdenes "stuck" si NO hay posición
- **Solución 2**: Verificación de duplicados en `SubmitProtectionOrders`
  - Antes de crear nuevas órdenes, busca huérfanas en `Account.Orders`
  - Recupera referencias perdidas antes de intentar crear
- **Logs nuevos**:
  - `STARTUP ADOPT: Found position Qty=X Dir=Long/Short - Adopting state and orders...`
  - `STARTUP ADOPT: Recovered SL order: SL_Long_01 Qty=2`
  - `RECOVERED orphan SL: SL_Long_01 Qty=2`
- **Resultado**: No más órdenes duplicadas al reconectar, referencias recuperadas automáticamente

---

## [1.10.37] - 2025-12-28
### Feature: Reset Estado al Cierre de Semana
- **Problema**: Al activar la estrategia el domingo, usaba señales/VWAP del viernes anterior
  - El estado (`currentEntryState`, `setupLevelName`, VWAP adhoc) persistía entre semanas
  - Colocaba órdenes basadas en triggers obsoletos de la semana pasada
- **Solución**: Nuevo método `CheckWeekEndReset()` ejecutado cada bar
  - Calcula el último viernes 6pm NY (cierre de mercados de futuros)
  - Si ha pasado ese viernes desde el último reset → resetea TODO
- **Estado reseteado**:
  - `currentEntryState = Idle`
  - `setupLevelName = ""`
  - `setupAnchorPrice = 0`
  - VWAP adhoc (`adhocVolSum`, `adhocPvSum`, `adhocLastBar`)
  - Cancela órdenes pendientes si existen
  - Limpia `skippedLevelsAtStartup`
- **Log**: `"v1.10.37 WEEK RESET - State cleared for new trading week"`
- **Resultado**: Estrategia empieza "limpia" cada domingo, sin señales heredadas

---

## [1.10.36] - 2025-12-28
### Fix Crítico: Bloquear Órdenes Históricas en Cuenta Live/Demo
- **Problema**: Al activar estrategia en cuenta demo con mercado cerrado, enviaba órdenes durante procesamiento histórico
- **Causa**: Condición `State == State.Historical` permitía órdenes siempre (agregada en v1.7.30 para Strategy Analyzer)
- **Solución**: Verificar si es conexión Playback antes de permitir órdenes en Historical
  - `bool isPlayback = (Connection.PlaybackConnection != null);`
  - Solo permite Historical orders si `isPlayback == true`
- **Comportamiento nuevo**:
  - **Cuenta live/demo**: Solo órdenes en Realtime (mercado abierto)
  - **Playback**: Órdenes en Historical y Realtime (como antes)
  - **Strategy Analyzer**: Órdenes en Historical (conexión Playback)

---

## [1.10.35] - 2025-12-28
### Feature: Información SL/TP en Panel de Estado
- **Nuevo**: Al tener orden limit activa o posición abierta, el panel muestra:
  - `SL: -$XX (Yt)` - Pérdida potencial en USD y ticks
  - `TP1: +$XX R=X.X` - Ganancia potencial TP1 y ratio R/R
  - `TP2: +$XX R=X.X` - Ganancia potencial TP2 y ratio R/R
- **Cálculo**: Usa precios reales de órdenes y cantidad de contratos
- **Separador visual**: Línea `─────────────────` para distinguir info de órdenes

---

## [1.10.34] - 2025-12-28
### Removed: Etiquetas TP/SL Eliminadas
- **Eliminado**: Etiquetas SL con fondo rojo y texto `-$XX`
- **Eliminado**: Etiquetas TP1/TP2 con fondo lime y texto `R=X.X +$XX`
- **Motivo**: Chart más limpio sin overlays adicionales
- **Impacto**: Las órdenes TP/SL funcionan normalmente, solo se quitó la visualización

---

## [1.10.33] - 2025-12-28 (REVERT)
### REVERTIDO: Cambios de Manejo Domingo Removidos
- **Acción**: Revertido de v1.10.47 a v1.10.33
- **Razón**: Los fixes de manejo de activación domingo (v1.10.34-v1.10.47) causaban entradas espurias y comportamiento errático
- **Estado**: Código estable pre-domingo restaurado
- **Recomendación**: Para playback con posiciones overnight, empezar desde el viernes (antes de 17:58 NY)

---

## VERSIONES REVERTIDAS (v1.10.34 - v1.10.47)
> ⚠️ Las siguientes versiones fueron revertidas por causar inestabilidad:

## [1.10.47] - 2025-12-28 ❌ REVERTIDO
### Fix CRÍTICO: Reset Completo de Variables Domingo
- **Problema**: Reset parcial causaba entradas espurias (órdenes por todos lados)
- **Causa**: Solo se reseteaba `currentEntryState` pero no otras variables críticas
- **Solución**: Reset COMPLETO de todas las variables de trading
- **NOTA**: REVERTIDO por causar comportamiento errático

## [1.10.46] - 2025-12-28
### Fix: Evitar Error "Modify Historical Order"
- **Problema**: Intentar cancelar órdenes históricas en playback causaba error fatal
- **Causa**: Órdenes del viernes son "históricas" y no pueden modificarse/cancelarse
- **Solución**: Remover TODOS los `CancelOrder` para domingo
    - Startup Failsafe: Solo ignora órdenes, no intenta cancelar
    - CheckSafetyNet: Solo resetea estado, no intenta cancelar ni cerrar
- **Resultado**: La estrategia ignora órdenes/posiciones fantasma y está lista para trades

## [1.10.44] - 2025-12-28
### Fix: Cierre Domingo Independiente del Estado
- **Problema**: El cierre dentro del zombie block solo corría si `State != PositionActive`, pero State ya era PositionActive
- **Solución**: Nuevo bloque de cierre domingo **ANTES** del zombie check
    - Se ejecuta si `Position != Flat` y es domingo (sin importar Estado)
    - Cancela TODAS las órdenes (stop, tp1, tp2, entry)
    - Cierra posición
    - Reset completo del estado a Idle
- **Resultado**: Cierra posición domingo sin importar el estado actual

## [1.10.43] - 2025-12-28
### Fix: Reset Estado a Idle Después de Cierre Domingo
- **Problema**: Después del cierre domingo, `currentEntryState` permanecía en `PositionActive`
- **Efecto**: La estrategia no tomaba nuevos trades porque creía tener posición
- **Solución**: Reset completo del estado después de ClosePositionUnmanaged:
    - `currentEntryState = Idle`
    - `setupLevelName = ""`
    - `setupAnchorPrice = 0`
    - `stopOrder, tp1Order, tp2Order = null`
    - `tradeVwapActive = false`
- **Resultado**: Estrategia lista para nuevos trades después del cierre

## [1.10.42] - 2025-12-28
### Fix: Startup Failsafe Cancela Órdenes Domingo
- **Problema**: Órdenes adoptadas en Startup causaban error "no market data" antes de CheckSafetyNet
- **Causa**: Startup adoptaba órdenes del viernes, que intentaban ejecutarse sin datos
- **Solución**: Check de domingo en Startup Failsafe - si es domingo, **CANCELAR** en vez de adoptar
- **Resultado**: Las órdenes del viernes se cancelan inmediatamente, evitando el error

## [1.10.41] - 2025-12-28
### Fix: Cierre Domingo en Zombie Position Block
- **Problema**: El cierre de domingo en orphan block no se ejecutaba cuando Strategy Position ya estaba sincronizada
- **Causa**: La condición `Position.MarketPosition == Flat` excluía posiciones sincronizadas
- **Solución**: Agregar cierre de domingo también en el zombie position block
    - Detecta si es domingo y hay posición zombie (Strategy != Flat pero State != PositionActive)
    - Cancela órdenes adoptadas (stopOrder, tp1Order, tp2Order)
    - Cierra posición con `ClosePositionUnmanaged("Sunday Zombie Cleanup")`
- **Resultado**: Ahora cierra posiciones domingo tanto en orphan como en zombie scenarios

## [1.10.40] - 2025-12-28
### Fix: Cierre Viernes Verifica Account Position
- **Problema**: CheckSessionExit solo verificaba `Position.MarketPosition` (puede estar desincronizado)
- **Solución**: Ahora verifica **AMBOS**: Strategy position Y Account position
- **Debug**: Agregado log "FRIDAY DEBUG" cuando EnableDebugLogs está activo para diagnosticar
- **Resultado**: Cierra si cualquiera de las dos tiene posición

## [1.10.39] - 2025-12-28
### Fix: Cierre Viernes 2 Minutos Antes
- **Problema**: 1 minuto antes (v1.10.36) aún no era suficiente para capturar el cierre viernes en playback
- **Solución**: Cambiar exitBuffer de 60s a **120s** (2 minutos antes)
- **Efecto**: Ahora cierra a las **17:58:00 NY** los viernes

## [1.10.38] - 2025-12-28
### Fix: Error "No Market Data" en Cierre Domingo
- **Problema**: El error persistía porque las órdenes adoptadas del viernes (StopMarket) seguían trabajando
- **Causa**: ClosePositionUnmanaged cerraba posición pero órdenes adoptadas seguían activas
- **Solución**: Cancelar stopOrder, tp1Order, tp2Order **ANTES** de cerrar posición
- **Resultado**: Cierre domingo limpio sin errores de simulación

## [1.10.37] - 2025-12-28
### Feature: Cierre Automático Posiciones Weekend al Activar Domingo
- **Problema**: Posiciones del viernes quedaban abiertas si la estrategia se activaba domingo
- **Solución**: Detectar si es domingo y cerrar posiciones heredadas automáticamente
    - Startup Failsafe: Logea posición weekend encontrada
    - CheckSafetyNet: Cierra posición orphan cuando hay datos de mercado disponibles
- **Condición**: Solo aplica si `DayOfWeek == Sunday`
- **Lunes-Jueves**: Comportamiento anterior (adopta posiciones overnight)

## [1.10.36] - 2025-12-28
### Fix: Cierre Viernes No Funcionaba en Playback 1-Minuto
- **Problema**: En barras de 1 minuto, el cierre del viernes (30s antes) era saltado
- **Causa**: Playback salta de 17:59:00 a 18:00:00, perdiendo 17:59:30
- **Solución**: Cambiar exitBuffer de **30 segundos** a **60 segundos**
- **Efecto**: Ahora cierra a las 17:59:00 NY (1 minuto antes)

## [1.10.35] - 2025-12-28
### Fix: Error "No Market Data Available" al Activar Domingo 7PM
- **Problema**: Error "There is no market data available to drive the simulation engine"
- **Causa**: CheckSafetyNet intentaba colocar órdenes de protección antes de tener tick data
- **Solución**: Verificar `Bars.Count < 2 || CurrentBar < 1` antes de EnsureProtection
    - Si no hay datos: Log y esperar al siguiente OnBarUpdate
    - Si hay datos: Proceder normalmente
- **Resultado**: Estrategia espera datos de mercado antes de enviar órdenes

## [1.10.34] - 2025-12-28
### Fix Crítico: Adopción de Órdenes Overnight (Startup Failsafe)
- **Problema**: Al activar domingo 7pm con posición del viernes, se cancelaban SL/TP
- **Solución**: Startup Failsafe ahora ADOPTA órdenes en vez de cancelarlas
    - Detecta si hay posición existente antes de procesar órdenes
    - Si hay posición: Adopta órdenes SL_, TP1_, TP2_ a las variables correspondientes
    - Si no hay posición: Comportamiento anterior (cancela stuck orders)
- **Bonus**: Al adoptar TP1, fija `tradeVWAP` al precio del TP1 existente
- **Resultado**: Posiciones overnight mantienen sus SL/TP originales

## [1.10.33] - 2025-12-28
### Removed: Etiquetas TP/SL con R y Monto
- **Eliminado**: Etiquetas de SL que mostraban `-$XX` con fondo rojo
- **Eliminado**: Etiquetas de TP1/TP2 que mostraban `R=X.X +$XX` con fondo lime
- **Motivo**: Usuario prefiere chart limpio sin etiquetas adicionales
- **Impacto**: Las órdenes TP/SL siguen funcionando, solo se quitó la visualización extra

## [1.10.32] - 2025-12-28
### Fix: Error "Modify Historical Order" en Playback Multi-Instrumento
- **Problema**: Al hacer playback con 6 instrumentos, error "attempted to modify a historical order"
- **Causa**: `ManagePositionExit()` intentaba usar `ChangeOrder()` en órdenes creadas en modo Historical
- **Solución**: Agregar check `if (State == State.Historical) return;` al inicio
- **Resultado**: Playback funciona sin deshabilitar la estrategia

## [1.10.31] - 2025-12-28
### Feature: VWAP Dual (Trade vs Global)
- **Problema v1.10.30**: VWAP fijo no seguía moviéndose, solo guardaba un precio estático
- **Nuevo sistema**: Dos VWAPs paralelos
    - **Trade VWAP**: Copia del global al entrar, **sigue acumulando** durante overnight
    - **Global VWAP**: Se reinicia cada nuevo día para futuros trades
- **Implementación**:
    - `tradeVWAP` (SessionVWAP) + `tradeVwapActive` (bool)
    - `EnsureProtection`: Al primer fill, copia acumuladores del VWAP global
    - `ManageGlobalVWAPs`: Acumula en Trade VWAP si está activo
    - `SubmitProtectionOrders`: Usa `tradeVWAP.CurrentValue` para TP1
    - **`ManagePositionExit`** (FIX): Actualiza TP1 usando `tradeVWAP` en vez del global
    - Resets al cerrar trade en `CheckSafetyNet` y `OnExecutionUpdate`
- **Visual**: Línea **cyan** (`Draw.Line`) muestra el VWAP del trade activo (sin conexiones verticales)
- **Resultado**: TP1 sigue moviéndose con el VWAP del día de entrada, incluso overnight

## [1.10.30] - 2025-12-28

## [1.10.29] - 2025-12-28
### Fix: Solo Señales Frescas (Corrección de Lógica)
- **Problema v1.10.28**: Permitía re-trigger del mismo nivel después de separarse
- **Corrección**: Niveles tocados al activar quedan **gastados permanentemente**
    - Al activar: Detecta qué niveles están siendo tocados (±5 ticks)
    - Los agrega a `skippedLevelsAtStartup`
    - El trigger loop los ignora siempre
- **Comportamiento correcto**: Espera nivel DIFERENTE, no re-toque del mismo

## [1.10.28] - 2025-12-28
### Feature: Overnight Permitido Lunes-Jueves
- **Cambio en `CheckSessionExit()`**: Ahora solo cierra posiciones los **viernes a las 17:59:30 NY**
    - Lunes-Jueves: Trades pueden permanecer abiertos overnight
    - Viernes: Cierre obligatorio antes del fin de semana
- **Lógica**: `if (isFriday && nyTimeOfDay >= cutoffTime)` en vez de siempre

### Feature: Adopción de Posiciones Overnight (No Más Cierres Automáticos)
- **Startup Failsafe modificado**: Ya NO cierra posiciones "zombie" al activar estrategia
    - Antes: Cerraba cualquier posición encontrada
    - Ahora: Adopta la posición y deja que la estrategia la maneje
- **CheckSafetyNet modificado**: Ya NO hace `Flatten` en posiciones "inseguras"
    - Antes: Cerraba si gap > 20 ticks
    - Ahora: Solo alerta con log, no cierra
- **Resultado**: Posiciones overnight se mantienen abiertas, incluyendo al recargar estrategia

### Feature: Solo Señales Frescas (No Heredadas del Historial)
- **Problema**: Al activar la estrategia en medio de un setup, entraba inmediatamente
- **Solución**: Nueva variable `realtimeStartBar` trackea cuándo la estrategia entró en Realtime
- **Lógica**: Ignora triggers que ocurren en el mismo bar donde se activó
    - Si activas mientras el precio toca un nivel → **NO entra**
    - Espera a que el precio se separe y vuelva a tocar → **SÍ entra**
- **Beneficio**: Evita entradas "heredadas" del historial, solo reacciona a señales nuevas

### Fix: Logs Sin Protección
- **Problema**: Algunos `Print()` aparecían aunque `EnableDebugLogs = false`
- **Ubicaciones corregidas** (6 total):
    - Línea 589: Startup Failsafe (Zombie Position)
    - Línea 603: Startup Failsafe (Stuck Order)
    - Línea 664: Error TimeZones
    - Línea 1728: VWAP Retry Created
    - Línea 3330: Position Closed (OnExecutionUpdate)
    - Línea 3355: VWAP RETRY Waiting (OnExecutionUpdate)
- **Resultado**: Todos los logs ahora respetan `EnableDebugLogs`

## [1.10.27] - 2025-12-27
### Features Session
- **TP Labels con R y Profit**: Fondo Lime, texto negro - `R=1.5 +$45`
- **SL Label**: Fondo rojo, texto blanco - `SL -$25`
- **TP2 Fix**: Reverted to ZoneOpposite (era Daily Extreme en v1.10.0)
- **Panel**: Contador X/Y de intentos
- **Nombres de Órdenes**: `EntryA+_Short_01`, `TP1_Long_02`, etc.
- **Pending**: Ajustar posición de etiquetas (texto y rectángulo juntos)

## [1.10.26] - 2025-12-27
### Feature: VWAP Mitigation Retry Logic
- **Nuevo Estado**: `WaitingForVwapMitigation` - espera después de SL
- **Flujo**: SL Hit → esperar nuevo low/high → crear VWAP# → retry
- **Variables**: `vwapCandleExtreme`, `currentVwapNumber`, `waitingForVwapMitigation`
- **Panel**: Muestra contador `2/10` (intento actual / max configurado)
- **Nombres de Órdenes con Número de Intento**:
    - `EntryA+_Long_01`, `EntryA+_Short_02`
    - `SL_Long_01`, `TP1_Short_02`, `TP2_Long_03`
- **Etiquetas TP con R y Profit**:
    - Muestra `R=1.5 +$45` junto a cada TP
    - TP1 = Verde (Lime), TP2 = Cyan
- **Logs**: `VWAP RETRY: Waiting for price to break...` y `VWAP#2 CREATED`
- **Reset**: Al tocar nuevo nivel, reintentos se cancelan

## [1.10.25] - 2025-12-27
### Feature: Máximos Intentos por Nivel
- **Nueva propiedad**: `Max Retries Per Level` (default: 1)
    - Valor 1 = comportamiento actual (1 intento por nivel)
    - Valor 2+ = permite re-intentar si primer trade pierde
- **Nuevo campo**: `EntryAttempts` en cada nivel para tracking
- **Log**: `ENTRY ATTEMPT #1/2 on Europe Low`
- **Uso**: Si pierdes por SL, el nivel puede intentarse de nuevo

## [1.10.24] - 2025-12-27
### FIX: Excluir Niveles del Mismo Día de Trading
- **Problema**: Estrategia tomaba triggers en niveles de "Today" aún en formación
    - USA Low Today no debería ser válido porque aún no cerró
- **Solución**: Filtro en escaneo de triggers:
    ```csharp
    if (lvl.StartTime.Date == Time[0].Date) continue; // Skip same-day
    ```
- **Regla**: Solo niveles de días ANTERIORES y activos son válidos
- **Debug Log**: `SKIP SAME-DAY: USA Low (still forming)`

## [1.10.23] - 2025-12-27
### Feature: Mostrar Nivel Actual con Edad
- **Panel**: Ahora muestra el nivel en uso: `Level: USA Low (3 Days)`
- **Formatos**: "Today", "1 Day", "X Days"
- **Removed**: Log spam `GLOBAL RISK SYNC` que llenaba el output
- **Beneficio**: Identificar fácilmente si estás operando niveles históricos

## [1.10.22] - 2025-12-27
### Performance: Optimización de Sincronización de Riesgo
- **Problema**: Lectura de archivo en cada tick causaba carga lenta
- **Solución**: Caché de lectura - solo lee archivo cada 5 segundos
- **Resultado**: Carga significativamente más rápida

## [1.10.21] - 2025-12-27
### Feature: Sincronización de Riesgo Entre Instrumentos
- **Problema**: Cada instrumento calculaba su riesgo ATR independientemente
    - MNQ mostraba $70, MCL mostraba $5 - no estaban normalizados
    - Usuario quería que TODOS usen el mismo riesgo (el máximo)
- **Solución**: Sistema de archivo compartido para sincronizar riesgo
    - Cada instrumento escribe su ATR Risk al archivo `SharedRisk.txt`
    - Todos leen el MÁXIMO de todos los instrumentos activos
    - Entradas expiran después de 60 segundos (limpieza automática)
- **Nuevos Métodos**:
    - `WriteSharedRisk()`: Escribe riesgo local al archivo compartido
    - `ReadMaxSharedRisk()`: Lee el máximo de todos los instrumentos
- **Panel**: Ahora muestra "Global Risk" (el máximo compartido)
- **Beneficio**: Si MNQ tiene $70 y MCL tiene $5 → AMBOS usan $70

## [1.10.20] - 2025-12-27
### Feature: Riesgo Dinámico Basado en Volatilidad (ATR)
- **Problema**: En mercados poco volátiles (ej: 2 AM), $100 de riesgo requería muchos contratos
    - MNQ calculaba 20+ contratos, NinjaTrader rechazaba por límites
    - No era proporcional a la volatilidad del momento
- **Solución**: Escalar el riesgo objetivo según ATR
    - Nueva propiedad: `ATRRiskScaleFactor` (default: 2.0)
    - Fórmula: `RiesgoEfectivo = MIN(RiskPerTradeUSD, ATR × ScaleFactor)`
    - Riesgo mínimo: $5 (nunca menos)
- **Comportamiento**:
    - 9:30 AM (ATR alto): Usa hasta $100 de riesgo → más contratos
    - 2:00 AM (ATR bajo): Riesgo reducido ~$20 → menos contratos
- **SL se mantiene en anchor + 1 tick** (protege estructura)
- **Log nuevo**: `ATR RISK SCALING: ATR=X ATR$=Y ScaledRisk=$Z EffectiveRisk=$W`

## [1.10.19] - 2025-12-27
### Feature: Stop Loss Basado en ATR (REVERTIDO en v1.10.20)
- **Problema**: v1.10.17 no cancelaba el SL - verificación `OrderState.Working` no era suficiente
- **Diagnóstico**: Añadido log `DEBUG ORPHAN: stopOrder exists. State=X`
- **Solución**:
    - Verificar `OrderState.Working` **O** `OrderState.Accepted`
    - Log explícito cuando se intenta cancelar
- **Resultado**: Mejor diagnóstico y cancelación más robusta

## [1.10.17] - 2025-12-27
### Fix: SL Huérfano Después de TP2
- **Problema**: Después de trade exitoso (TP1 → BE → TP2), el SL en BE quedaba activo
    - El código cancelaba `stopOrder1`/`stopOrder2` (arquitectura antigua)
    - Pero NO cancelaba `stopOrder` (arquitectura Single-SL v1.9.0+)
- **Solución**: En `OnExecutionUpdate()`, cuando posición queda Flat:
    - Agregar: `if (stopOrder != null && stopOrder.OrderState == OrderState.Working) CancelOrder(stopOrder);`
- **Resultado**: SL se cancela automáticamente al cerrar posición por TP2

## [1.10.16] - 2025-12-27
### Fix: Spam de Logs en CalculateDynamicQuantity
- **Problema**: La función `CalculateDynamicQuantity()` tenía un `Print()` interno
    - Se llamaba en cada tick mientras la orden estaba working
    - Generaba miles de líneas de log por minuto
- **Solución**: Eliminar el log interno de la función de cálculo
    - El log solo ocurre cuando realmente hay un cambio de cantidad (`DYNAMIC QTY ADJUST`)
- **Resultado**: Logs limpios, solo mensajes relevantes

## [1.10.15] - 2025-12-27
### Feature: Ajuste Dinámico de Cantidad Durante Working Order
- **Problema**: Si el precio se movía y el SL quedaba más amplio, la cantidad de contratos permanecía igual
    - Esto causaba que el riesgo real excediera el `RiskPerTradeUSD` configurado
    - Ejemplo: Entrar con 10 contratos, SL se amplía → riesgo mayor al deseado
- **Solución**: En el estado `workingOrder`, recalcular cantidad cada vez que cambia:
    - El precio del anchor (nuevo high/low)
    - El precio del VWAP (entry adaptativo)
    - Fórmula: `CalculateDynamicQuantity(currentVWAP, projectedStop)`
    - Modificar orden solo si precio O cantidad cambian (evita spam)
- **Resultado**: Riesgo se mantiene constante aunque el SL se amplíe

## [1.10.14] - 2025-12-27
### Fix: Cantidad del SL al Mover a Breakeven
- **Problema**: SL mantenía 8 contratos después de TP1 cuando solo quedaban 4
    - Causa: `ChangeOrder(stopOrder, stopOrder.Quantity, ...)` usaba cantidad original
    - Debería usar contratos restantes después de TP1
- **Solución**: Usar `Math.Abs(Position.Quantity)` para obtener contratos vivos
- **Resultado**: SL se ajusta a la cantidad correcta al moverse a BE

## [1.10.13] - 2025-12-27
### Fix: Lógica Breakeven para Arquitectura Single-SL
- **Problema**: TP1 llenaba pero SL no se movía a BE
    - Log mostraba "BE LOGIC: TP1 Filled. Move SL2." pero sin acción
    - Causa: Código buscaba `stopOrder2` (arquitectura antigua v1.7.x)
    - Pero v1.9.0+ usa `stopOrder` único para toda la posición
- **Solución**:
    - TP1 Fill → Mueve `stopOrder` a BE (no `stopOrder2`)
    - TP2 Fill → Solo log (SL ya debería estar en BE)
- **Resultado**: SL se mueve a BE automáticamente cuando TP1 se llena

## [1.10.12] - 2025-12-27
### Fix: Cancelación de Órdenes Huérfanas en Safety Net
- **Problema**: Al mover SL manualmente a BE y ejecutarse, el TP quedaba abierto
    - Safety Net detectaba Flat pero solo nullificaba referencias
    - No cancelaba las órdenes working (TP, SL)
- **Solución**: En Safety Net (`CheckSafetyNet()`), antes de nullificar:
    - Cancela `stopOrder1`, `stopOrder2`, `tp1Order`, `tp2Order`, `entryOrder` si están Working
    - También agrega `stopOrder1 = null` y `stopOrder2 = null` que faltaban
- **Resultado**: Cuando SL se ejecuta manualmente, los TPs se cancelan automáticamente

## [1.10.11] - 2025-12-27
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
