# Registro de Cambios (Changelog)

Todos los cambios notables en el proyecto `SessionLevelsStrategy` serán documentados en este archivo.

El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.0.0/), y este proyecto adhiere a [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [v1.14.69] - 2026-01-08
### FIX CRÍTICO: Protección Contra Barras Históricas (Stale Bar Protection) 🛡️
- **Problema**: Al activar la estrategia, se ejecutaban órdenes basadas en datos históricos (meses atrás) durante la carga del chart. Las órdenes se enviaban a la cuenta demo/live aunque los datos fueran de diciembre 2025.
- **Causa Raíz**: 
  - Durante la carga del chart, `State == State.Realtime` pero los datos procesados eran históricos.
  - La verificación `Connection.PlaybackConnection != null` retornaba `true` incluso sin estar en modo Playback activo.
  - La estrategia interpretaba barras de hace meses como barras válidas para operar.
- **Solución**: Nueva verificación "Stale Bar Protection" en `EntryStateMachine.cs`:
  - En `ScanForTriggers()`: Bloquea escaneo de triggers si `barAge > 5 minutos` en Realtime.
  - En `HandleConfirmation()`: Bloquea confirmación y resetea estado si `barAge > 5 minutos`.
  - Usa `NinjaTrader.Core.Globals.Now - Time[0]` para calcular antigüedad real de la barra.
- **Resultado**: La estrategia ahora ignora completamente barras históricas durante la carga del chart, previniendo órdenes fantasma.

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
