# RelativeVwap - Historial de Cambios

Este documento registra todos los cambios notables en el proyecto **RelativeVwap**.

El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.0.0/),
y este proyecto se adhiere a [Semantic Versioning](https://semver.org/lang/es/).

## [1.0.49] - 2026-01-27
### Corregido
- **CRÍTICO: Lógica "Touched Again" Causaba Resets Constantes**: Se eliminó completamente la lógica que reseteaba secuencia al tocar el mismo nivel múltiples veces.
  - **Problema**: A pesar de los fixes en v1.0.48, todas las Entry seguían mostrando "01"
  - **Causa Raíz (Descubierta con Logs)**:
    - Logs mostraban resets en CADA BARRA mientras precio arriba del nivel: Bar:2480, 2481, 2482... hasta 2509 (30 barras consecutivas)
    - Lógica "touched again" (líneas ~2157-2167 HIGH, ~2273-2282 LOW) verificaba: `if (high >= session.High)` → true en CADA BARRA
    - Resultado: `highAnchorSequence` se reseteaba a 0 constantemente → siempre "Entry 01"
  - **Solución**:
    - ELIMINADA completamente la lógica "touched again"
    - Secuencia ahora SOLO resetea cuando:
      1. Toca un NUEVO nivel por primera vez (Asia → Europe → USA)
      2. Precio cruza al VWAP OPUESTO
    - NO resetea por estar arriba del mismo nivel múltiples barras
  - **Resultado**: Entry labels ahora incrementan correctamente: 01, 02, 03, 04...

### Agregado
- **VWAP Interno para Trades de Continuación**: Cuando se toma liquidez de un nivel interno, se crea un VWAP adicional específico para ese nivel.
  - **Propósito**: Permite detectar señales de continuación en niveles internos independientemente del VWAP principal
  - **Funcionamiento**:
    1. Precio toma liquidez de nivel interno (ejemplo: Europe High cuando USA High > Europe High)
    2. Se crea un VWAP anclado específicamente a ese nivel interno (Europe High)
    3. Cuando precio se separa de ese VWAP interno → Signal 2 dispara
    4. Entry labels para trades de continuación usan el VWAP interno
  - **Visual**:
    - VWAP Interno HIGH: Línea naranja punteada (Values[2])
    - VWAP Interno LOW: Línea naranja punteada (Values[3])
    - VWAP Principal: Líneas cyan (sin cambios)
  - **Variables nuevas** (línea ~56-67):
    - `internalHighPV`, `internalHighVol`, `internalHighBarIdx`, `internalHighPrice`
    - `internalLowPV`, `internalLowVol`, `internalLowBarIdx`, `internalLowPrice`
    - `hasInternalHighVWAP`, `hasInternalLowVWAP` (banderas de activación)
  - **Creación** (CheckTouches):
    - Línea ~2142: Crea VWAP interno HIGH cuando `highLiqGrabIsInternal=true`
    - Línea ~2277: Crea VWAP interno LOW cuando `lowLiqGrabIsInternal=true`
    - Anclado al precio del nivel de sesión (session.High/Low)
    - Inicializado con volumen de la barra actual
  - **Cálculo** (acumulación en cada barra):
    - Línea ~933-934: Acumulación Realtime (OnEachTick)
    - Línea ~955-962: Acumulación Historical (OnBarClose)
    - Línea ~685-705: Actualización de Values[2] y Values[3]
  - **Señales Signal 2**:
    - Línea ~1117: Señales SHORT usan VWAP interno HIGH si existe
    - Línea ~1433: Señales LONG usan VWAP interno LOW si existe
    - Lógica: `hasInternalHighVWAP ? Values[2][0] : currentHighVWAP`
  - **Reset**: ResetSession (línea ~594-605)
  - **Resultado**: Los traders obtienen señales de continuación válidas en niveles internos que antes no tenían VWAP propio

- **Detección Automática de Niveles Internos**: Las etiquetas "Liquidity Grabbed" ahora muestran "(i)" cuando el nivel no es el extremo del día.
  - **Nivel Interno**: Cuando el High/Low de una sesión NO es el máximo/mínimo del día
    - Ejemplo: Europe High = 25900 pero USA High = 26000 → Europe es interno
  - **Etiquetas**:
    - `"Liquidity\nGrabbed 01\nEurope High (i)"` - nivel interno (tiene "(i)")
    - `"Liquidity\nGrabbed 02\nUSA High"` - extremo del día (sin marcador)
  - **Lógica**:
    - HIGH interno: `session.High < currentDayHigh`
    - LOW interno: `session.Low > currentDayLow`
  - **Variables nuevas** (línea ~107-108):
    - `highLiqGrabIsInternal`: true si es nivel interno
    - `lowLiqGrabIsInternal`: true si es nivel interno
  - **Ubicaciones actualizadas** (4 total):
    - Línea ~957: HIGH movable label
    - Línea ~987: LOW movable label
    - Línea ~2096: HIGH CheckTouches label
    - Línea ~2216: LOW CheckTouches label

### Cambiado
- **Simplificación de Etiquetas - Un Solo Formato**: Se eliminaron los 3 modos de etiquetas (Default, Simple, Custom) y se dejó solo el formato con secuencias.
  - **Antes**: LabelDisplayMode con 3 opciones (Default, Simple, Custom)
  - **Ahora**: Solo un formato fijo para todas las etiquetas:
    - Liquidity Grabbed: 3 líneas mostrando "Liquidity\nGrabbed 01\nUSA High" o "Liquidity\nGrabbed 02\nAsia Low (i)"
      - Línea 1: "Liquidity"
      - Línea 2: "Grabbed 01" (con número de secuencia)
      - Línea 3: "USA High" o "Europe High (i)" (sesión + tipo de nivel + marcador interno si aplica)
    - Entry: "Entry 01", "Entry 02", "Entry 03"...
    - Confirm: "Confirm" (Signal 3 - confirmación)
  - **Ubicaciones simplificadas** (6 total):
    - Línea ~952: HIGH Liquidity Grabbed
    - Línea ~980: LOW Liquidity Grabbed
    - Línea ~1332: SHORT Entry (Signal 2)
    - Línea ~1634: LONG Entry (Signal 2)
    - Línea ~1209: SHORT Confirm (Signal 3)
    - Línea ~1509: LONG Confirm (Signal 3)
  - **Removidas**: Propiedades CustomSignal1Text, CustomSignal2Text, CustomSignal3Text, LabelDisplayMode (ya no se usan)

- **Organización de Logs en Carpeta Dedicada**: Los archivos de log ahora se guardan en una subcarpeta para mejor organización.
  - **Antes**: `trace/RelativeVwap_Debug_YYYYMMDD.txt`
  - **Ahora**: `trace/RelativeVwap/RelativeVwap_Debug_YYYYMMDD.txt`
  - La carpeta `RelativeVwap` se crea automáticamente si no existe
  - Mantiene los logs del indicador organizados separadamente de otros logs de NinjaTrader
  - Actualizada la descripción del parámetro "Logging a Archivo" con la nueva ruta

## [1.0.48] - 2026-01-27
### Corregido
- **CRÍTICO: Lógica de Reset Invertida (Bug en v1.0.47)**: Se corrigió el bug donde se reseteaba la secuencia del lado OPUESTO en lugar del MISMO lado.
  - **Problema**: A pesar del fix de v1.0.47, Entry seguía mostrando 03, 04 en lugar de 01
  - **Causa Raíz (Descubierta con Logs Detallados)**:
    - Cuando precio tocaba HIGH level → código reseteaba `lowAnchorSequence = 0` (OPUESTO/MAL)
    - Pero señales SHORT usan `highAnchorSequence` → reset no tenía EFECTO en lado SHORT
    - Resultado: `highAnchorSequence` seguía incrementando (6, 7, 8...) → Entry 07, Entry 08...
    - Ejemplo del log: `RESET lowAnchorSequence=0 | Touched HIGH` seguido de `SIG2 SHORT FIRED | Seq BEFORE:6 → Entry 07`
  - **Lógica Correcta**:
    - HIGH level crea HIGH VWAP → usado por señales SHORT (highAnchorSequence)
    - LOW level crea LOW VWAP → usado por señales LONG (lowAnchorSequence)
    - Por lo tanto: HIGH level touch → reset `highAnchorSequence` (MISMO lado)
  - **Solución**:
    - CheckTouches HIGH first break (línea ~2048): reset `highAnchorSequence = 0` (era lowAnchorSequence)
    - CheckTouches HIGH touched again (línea ~2152): reset `highAnchorSequence = 0` (era lowAnchorSequence)
    - CheckTouches LOW first break (línea ~2172): reset `lowAnchorSequence = 0` (era highAnchorSequence)
    - CheckTouches LOW touched again (línea ~2276): reset `lowAnchorSequence = 0` (era highAnchorSequence)

- **CRÍTICO: Múltiples Resets por Barra en Modo OnEachTick**: Se corrigió el bug donde todas las Entry mostraban "01" debido a resets constantes.
  - **Problema**: Usuario reportó "todas las entradas dicen Entry 01" después del primer fix
  - **Causa Raíz (Descubierta con Logs)**:
    - Log mostraba CIENTOS de resets en la misma barra: Bar:2180 Time:08:21:00
    - En modo Calculate.OnEachTick, lógica "touched again" se ejecutaba en CADA TICK
    - Condición `if (high >= session.High)` era true para CADA TICK mientras precio arriba del nivel
    - Resultado: `highAnchorSequence` reseteado a 0 constantemente → siempre Entry 01
  - **Solución**:
    - Agregadas variables de tracking: `lastHighSeqResetBar`, `lastLowSeqResetBar` (línea ~105-106)
    - Verificación `CurrentBar != lastHighSeqResetBar` antes de resetear
    - Aplicado a TODAS las ubicaciones de reset (6 total):
      1. HIGH level first break (línea ~2052)
      2. HIGH level touched again (línea ~2153)
      3. LOW level first break (línea ~2178)
      4. LOW level touched again (línea ~2283)
      5. Cruzó LOW VWAP (línea ~1691)
      6. Cruzó HIGH VWAP (línea ~1708)
  - **Resultado**: Ahora resetea MÁXIMO UNA VEZ por barra → Entry secuencia incrementa correctamente: 01, 02, 03...

- **Agregado Logging Detallado para Diagnóstico**: Se agregaron logs de "Seq BEFORE" y "Seq AFTER" para rastrear incrementos de secuencia.
  - Ayudó a identificar ambos bugs críticos
  - Categorías: [SIGNAL2] y [SEQ_RESET]

## [1.0.47] - 2026-01-26
### Corregido
- **Entry Seguía Mostrando Solo "01" (Bug en v1.0.46)**: Se corrigió el lugar donde se resetea la secuencia de Entry.
  - **Problema**: v1.0.46 intentó arreglar resetendo en CheckTouches (nivel opuesto), pero seguía mostrando solo "01"
  - **Causa Raíz Real**:
    - La secuencia `highAnchorSequence` / `lowAnchorSequence` NO se reseteaba cuando se creaba NUEVO ANCHOR (nuevo día max/min)
    - CheckTouches valida si un nivel fue tocado (para desbloquear señales), pero NO crea nuevos anchors
    - Crear nuevo anchor sucede en líneas ~795, ~835 cuando `high > currentDayHigh` o `low < currentDayLow`
  - **Solución Correcta**:
    - Resetear `highAnchorSequence = 0` cuando se crea nuevo HIGH anchor (línea ~814)
    - Resetear `lowAnchorSequence = 0` cuando se crea nuevo LOW anchor (línea ~854)
    - NO resetear en CheckTouches (removido código incorrecto de v1.0.46)
  - **Comportamiento Correcto Ahora**:
    1. Se crea nuevo anchor (nuevo día max/min) → secuencia = 0
    2. Primera Señal 2 → incrementa a 1 → "Entry 01"
    3. Precio barre anchor (o se cancela y vuelve) → Señal 2 → incrementa a 2 → "Entry 02"
    4. Continúa incrementando: 03, 04, etc.
    5. Se crea NUEVO anchor → resetea a 0 → siguiente Señal 2 → "Entry 01"

## [1.0.46] - 2026-01-26
### Corregido
- **Entry Siempre Mostraba "01" (Bug en v1.0.45)**: Se corrigió el bug donde todas las etiquetas Entry mostraban "01" en lugar de incrementar secuencialmente.
  - **Problema**: Usuario reportó "las etiquetas entry 01 entry 02 no veo que cambien de numeros secuanciales todas dicen Entry 01"
  - **Causa Raíz**:
    - En CheckTouches, cuando se rompía un nivel del MISMO lado, se reseteaba la secuencia a 0
    - Ejemplo SHORT: Rompe Asia High → `highAnchorSequence = 0` → Señal 2 → `highAnchorSequence++` (ahora 1) → "Entry 01"
    - Luego rompe Europa High → `highAnchorSequence = 0` otra vez → Señal 2 → "Entry 01" otra vez
  - **Solución**:
    - NO resetear secuencia cuando rompe nivel del MISMO lado
    - Solo resetear cuando toca nivel OPUESTO virgen
    - Cuando rompe HIGH → resetea `lowAnchorSequence = 0` (opuesto)
    - Cuando rompe LOW → resetea `highAnchorSequence = 0` (opuesto)
  - **Resultado**: Ahora Entry incrementa correctamente: 01, 02, 03... y resetea solo cuando toca nivel opuesto.

## [1.0.45] - 2026-01-26
### Agregado
- **Secuencias Numeradas para Etiquetas de Liquidity Grabbed y Entry**: Las etiquetas ahora muestran el número de intento del nivel.
  - **Liquidity Grabbed**: Formato de dos líneas "Liquidity\nGrabbed 01", "Liquidity\nGrabbed 02", etc.
  - **Entry**: Formato simple "Entry 01", "Entry 02", etc.
  - **Variables nuevas** (líneas ~98-101):
    - `highLiqGrabLocked` / `lowLiqGrabLocked`: Estado de congelamiento cuando Señal 2 dispara
    - `highLiqGrabSequence` / `lowLiqGrabSequence`: Contador de secuencia (01, 02, 03, ...)

### Cambiado
- **Liquidity Grabbed se Congela en Pivote cuando Señal 2 Dispara**: La etiqueta ya NO se mueve después de que Señal 2 se confirma.
  - **Comportamiento Anterior**: Etiqueta se movía constantemente siguiendo el nuevo high/low
  - **Comportamiento Nuevo**:
    1. Nivel roto → "Liquidity Grabbed 01" aparece y se mueve mientras el precio continúa
    2. Señal 2 dispara → Etiqueta se CONGELA en ese pivote (`locked = true`)
    3. Si Señal 2 se cancela (toca VWAP misma barra) → Descongela (`locked = false`)
    4. Si precio barre el anchor bar otra vez → Nueva etiqueta "Liquidity Grabbed 02"
    5. Toca nivel opuesto virgen → Reset secuencia a 01
  - **Implementación**:
    - Movimiento de etiqueta solo si `!highLiqGrabLocked` (líneas ~918, ~950)
    - Lock cuando Señal 2 dispara (líneas ~1336, ~1642)
    - Unlock cuando Señal 2 se cancela (líneas ~1199, ~1512)
    - Detectar nuevo barrido del anchor bar (líneas ~979-1018)
    - Reset en CheckTouches cuando toca opuesto (líneas ~1944-1946, ~2052-2054)

- **Formato de Etiquetas Simplificado**:
  - **Signal 1 (Liquidity Grabbed)**:
    - Antes: "1" (simple) o código complejo "AL0.1"
    - Ahora: "Liquidity\nGrabbed 01" (con `\n` para dos líneas)
    - Tags únicos por secuencia: `"Sig1H_Txt_" + session + "_" + sequence`
  - **Signal 2 (Entry)**:
    - Antes: "2" (simple) o código complejo "AL0.1.1"
    - Ahora: "Entry 01" (simple con número de intento)
    - Usa `highAnchorSequence` / `lowAnchorSequence` existente

### Detalles Técnicos
- **Detección de Nuevo Barrido** (líneas ~979-1018):
  - Si `locked` && precio > anchor bar high (o < anchor bar low)
  - Incrementar secuencia
  - Descongelar para permitir movimiento
  - Reiniciar tracking
- **Reset de Secuencia** (líneas ~1944-1946, ~2052-2054):
  - Cuando se rompe nivel opuesto relevante
  - `sequence = 1`, `locked = false`, `barIdx = -1`
- **Tags con Secuencia**: Permite múltiples etiquetas simultáneas (01, 02, 03) en el chart

## [1.0.44] - 2026-01-26
### Corregido
- **Bloqueaba Trades Válidos (Regresión v1.0.43)**: Se corrigió el bug donde v1.0.43 bloqueaba trades válidos.
  - **Problema**: Usuario reportó "dejó de tomar trades válidos los que estudiamos recientemente".
  - **Causa Raíz**: En v1.0.43, se guardaba `currentHighAnchorSession` en CheckTouches con condición `if (session.High > currentDayHigh)`.
    - CheckTouches se ejecuta ANTES de actualizar `currentDayHigh` (línea 792)
    - Entonces `currentDayHigh` tiene el valor VIEJO durante CheckTouches
    - La condición `session.High > currentDayHigh` usaba el valor incorrecto
    - Resultado: `currentHighAnchorSession` no se guardaba cuando debía
  - **Solución**: Mover el guardado a donde realmente se CREA el nuevo anchor (líneas ~795-798, ~835-838)
    - Cuando `high > currentDayHigh` → crea nuevo anchor → guardar `currentHighAnchorSession = lastUnlockedHighSession`
    - Removido código incorrecto de CheckTouches (líneas ~1928-1934, ~2034-2040)
  - **Resultado**: Ahora guarda el session correcto cuando el anchor se crea realmente, restaurando trades válidos.

## [1.0.43] - 2026-01-26
### Corregido
- **Señal 2 Usa VWAP de Nivel Diferente al Último Liquidity Grab**: Se corrigió el bug donde la Señal 2 usaba VWAP de un nivel diferente al que disparó la última Señal 1.
  - **Problema Reportado**: Señal 2 a las 10:45 usa VWAP de **Asia Low** (AnchorBar:1764), pero la última Señal 1 fue de **Europa Low** (~21705).
  - **Causa**: No había validación para verificar que el VWAP anchor correspondiera al mismo nivel (session) que disparó la última Señal 1.
  - **Comportamiento Incorrecto**:
    1. Señal 1: Toma **Europa Low** (~21705) - nivel interno, NO hay VWAP (solo se crean en máx/mín del día)
    2. VWAP disponible: **Asia Low** (~21658) - mínimo del día
    3. Señal 2: Usa VWAP de **Asia Low** ❌ (nivel diferente)
  - **Comportamiento Correcto**:
    1. Señal 1: Toma **Europa Low** - NO hay VWAP
    2. Señal 2: NO debe dispararse (VWAP es de otro nivel)
    3. Solo disparar Señal 2 cuando último nivel roto == nivel del VWAP
  - **Solución Implementada**:
    1. Nuevas variables `currentHighAnchorSession` / `currentLowAnchorSession` (líneas ~85-86)
    2. Cuando se rompe nivel que será máx/mín del día, guardar session (líneas ~1920-1924, ~2030-2034)
    3. Al disparar Señal 2, validar: `lastUnlockedHighSession == currentHighAnchorSession` (líneas ~1201, ~1507)
    4. Si son diferentes → NO disparar Señal 2
  - **Logging agregado**:
    - `[DEBUG ANCHOR]` cuando se guarda session del anchor
    - `[DEBUG FLAG]` ahora muestra `SameLevel` en validación Señal 2
  - **Resultado**: Señal 2 solo se dispara si el VWAP corresponde al MISMO nivel que disparó la última Señal 1.

## [1.0.42] - 2026-01-26
### Corregido
- **Señal 2 Aparece Sin Nuevo Liquidity Grab**: Se corrigió el bug donde aparecía una nueva Señal 2 inmediatamente después de que la anterior tocara el nivel opuesto, sin esperar un nuevo liquidity grab.
  - **Problema Reportado**: Señal a las 10:35 toca nivel opuesto → inmediatamente aparece nueva señal a las 10:40 para el **mismo anchor** (Bar 5070).
  - **Comportamiento Incorrecto**: Cuando se toca nivel opuesto, se reseteaba el tracker permitiendo nueva Señal 2 sin nuevo liquidity grab.
  - **Causa**: En v1.0.36 se agregó reset de tracker opuesto al tocar nivel (líneas ~1920, ~2026): "Reset lastSignaledLowAnchorBar = -1 cuando toca session HIGH".
  - **Secuencia Incorrecta**:
    1. Liquidity Grab → Señal 2 (vela amarilla)
    2. Toca nivel opuesto → Reset tracker
    3. ❌ Nueva Señal 2 aparece inmediatamente (mismo anchor)
  - **Secuencia Correcta**:
    1. Liquidity Grab → Señal 2 (vela amarilla)
    2. Toca nivel opuesto → NO resetear tracker
    3. Solo permitir nueva Señal 2 cuando haya **NUEVO liquidity grab** (nuevo anchor)
  - **Solución**: Removido reset de tracker opuesto en CheckTouches (líneas ~1919-1921, ~2024-2026).
  - **Condiciones Reset Tracker** (actualizadas):
    1. ✅ **Nuevo anchor** creado (nuevo liquidity grab)
    2. ✅ **Señal cancelada** (tocó VWAP en misma barra)
    3. ❌ ~~Toca nivel opuesto~~ (REMOVIDO - no resetea tracker)
  - **Resultado**: Ahora nueva Señal 2 solo aparece después de nuevo liquidity grab, no solo por tocar nivel opuesto.

## [1.0.41] - 2026-01-26
### Corregido
- **Velas Subsecuentes No Se Pintan + Ralentización Severa**: Se corrigieron dos bugs críticos reportados por el usuario.
  - **Problema 1 - Velas subsecuentes no aparecen**: Después de que una señal se cancelaba (tocaba VWAP), no se permitían más señales para el mismo anchor.
    - **Causa**: Cuando se cancelaba la señal, `lastSignaledHighAnchorBar` / `lastSignaledLowAnchorBar` NO se reseteaban, bloqueando futuras señales.
    - **Solución**: Resetear el tracker a `-1` cuando la señal se cancela (líneas ~1176, ~1497).
    - **Resultado**: Ahora permite múltiples señales para el mismo anchor, siempre que las anteriores hayan sido canceladas por tocar VWAP.
  - **Problema 2 - Ralentización severa en playback**: El playback se ralentizaba drásticamente al llegar a la vela amarilla, luego volvía a velocidad normal.
    - **Causa**: `ChartControl.Dispatcher.Invoke()` es sincrónico y bloquea OnBarUpdate en cada tick, causando lag acumulativo.
    - **Solución**: Removido completamente todos los `Dispatcher.Invoke` (líneas ~1256-1262, ~1278-1281, ~1560-1566, ~1587-1590).
    - **Razón**: `BarBrushes` se actualiza automáticamente en NinjaTrader - no necesita refresh manual forzado.
    - **Resultado**: Playback vuelve a velocidad normal sin ralentizaciones.

## [1.0.40] - 2026-01-26
### Corregido
- **Barras Verticales Amarillas**: Se corrigió el bug introducido en v1.0.39 donde aparecían barras verticales amarillas cubriendo todo el chart.
  - **Causa**: `BackBrushes` pinta el FONDO completo detrás de la vela (toda la altura del chart), no la vela en sí.
  - **Solución**: Removido completamente `BackBrushes` de todas las ubicaciones (líneas ~1163, ~1171, ~1208, ~1277, ~1481, ~1489, ~1520, ~1589).
  - **Mantiene**: Solo `BarBrushes` (color de vela) y `CandleOutlineBrushes` (contorno de vela).
  - **Resultado**: Las barras verticales desaparecen, volviendo a comportamiento esperado.

## [1.0.39] - 2026-01-26
### Corregido
- **Vela Amarilla No Aparece Hasta F5 (Intento 2)**: Solución alternativa pintando todos los tipos de brush para garantizar visibilidad.
  - **Problema Persistente**: v1.0.38 no resolvió el problema - la vela amarilla sigue sin aparecer hasta F5.
  - **Nueva Estrategia**: En Calculate.OnEachTick mode, `BarBrushes` puede no renderizarse inmediatamente. Pintando también `BackBrushes` y `CandleOutlineBrushes`.
  - **Cambios**:
    1. Cuando señal se dispara: Pintar BarBrushes[0], BackBrushes[0] y CandleOutlineBrushes[0] (líneas ~1201-1204, ~1504-1507)
    2. En persistent painting: Pintar los 3 tipos de brush (líneas ~1267-1269, ~1570-1572)
    3. Al cancelar señal: Limpiar los 3 tipos de brush (líneas ~1161-1169, ~1468-1476)
  - **Objetivo**: Garantizar que al menos uno de los brushes se renderice en tiempo real durante playback.

## [1.0.38] - 2026-01-26
### Corregido
- **Vela Amarilla No Aparece Hasta F5**: Se corrigió el bug donde la vela amarilla no aparecía en tiempo real en playback, requiriendo presionar F5 para verla.
  - **Síntoma**: La señal se disparaba correctamente (visible en logs) pero la vela amarilla no se pintaba hasta refrescar con F5.
  - **Causa Raíz**: El refresh del chart era asincrónico (`InvokeAsync`), causando delay en el renderizado. Además, el persistent painting no forzaba refresh para la barra actual.
  - **Solución**:
    1. Cambiado `ChartControl.Dispatcher.InvokeAsync()` → `ChartControl.Dispatcher.Invoke()` con `DispatcherPriority.Render` para forzar refresh sincrónico e inmediato (líneas ~1250, ~1542).
    2. Agregado refresh también en persistent painting cuando `barsAgo == 0` (barra actual) para garantizar visibilidad en Calculate.OnEachTick mode (líneas ~1270, ~1563).
  - **Resultado**: La vela amarilla ahora aparece inmediatamente cuando la señal se dispara, sin necesidad de F5.

## [1.0.37] - 2026-01-26
### Corregido
- **Vela Amarilla Despintada Incorrectamente**: Se corrigió el bug donde la vela amarilla se despintaba cuando el precio tocaba VWAP en barras posteriores.
  - **Síntoma**: Señal disparada en Bar 3588, pero cuando el precio tocaba VWAP en Bar 3687 (barra actual), la vela amarilla desaparecía.
  - **Causa**: `BarBrushes[0] = null` estaba FUERA del bloque `if (highSignal2BarIdx == CurrentBar)`, ejecutándose SIEMPRE que tocaba VWAP, despintando la barra actual incluso si la señal estaba en una barra anterior.
  - **Solución**: Movido `BarBrushes[0] = null` DENTRO del condicional que verifica si es la misma barra (líneas ~1165, ~1464).
  - **Resultado**: Ahora solo despinta la barra actual si la señal fue generada EN esa misma barra.

## [1.0.36] - 2026-01-26
### Corregido
- **UNA Señal por VWAP Anchor (CORRECCIÓN DE v1.0.35)**: Implementación correcta de la lógica "una señal por anchor VWAP".
  - **Comportamiento Deseado**: La Señal 2 (vela amarilla) debe aparecer SOLO UNA VEZ por cada VWAP anclado, sin importar cuántas veces el precio toque y se separe del VWAP.
  - **Condiciones de Reset** (cuando se permite nueva señal):
    1. **Nuevo Anchor VWAP**: Cuando el precio crea un nuevo HIGH/LOW del día (ya implementado en v1.0.33)
    2. **Nivel Opuesto**: Cuando el precio llega al nivel de sesión OPUESTO (ej: si está trabajando con VWAP anclado a LOW, resetea cuando llega al session HIGH)
    3. **Rompe Anchor**: Cuando el precio rompe 1 tick por encima/debajo de la barra de anclaje del VWAP (pendiente implementación)
  - **Problema con v1.0.35**: Reseteaba el tracker cuando tocaba VWAP, permitiendo múltiples señales para el mismo anchor (comportamiento INCORRECTO).
  - **Solución**:
    1. **Revertir v1.0.35**: NO resetear tracker cuando toca VWAP (líneas ~1144, ~1443). Solo resetear el FLAG para permitir que la señal reaparezca si la vela cierra sin tocar.
    2. **Reset en Nivel Opuesto**: Cuando se rompe session HIGH (línea ~1897), resetear `lastSignaledLowAnchorBar = -1`. Cuando se rompe session LOW (línea ~2003), resetear `lastSignaledHighAnchorBar = -1`.
  - **Resultado**: Ahora la Señal 2 aparece SOLO UNA VEZ por anchor, y solo se resetea cuando el precio llega al nivel opuesto o crea nuevo anchor.

## [1.0.35] - 2026-01-26
### Nota
- **Versión Incorrecta**: Esta versión reseteaba el tracker al tocar VWAP, permitiendo múltiples señales para el mismo anchor (comportamiento NO deseado por el usuario). Revertido en v1.0.36.
### Corregido
- **Nueva Señal No Aparece Después de Cancelación**: Se corrigió el bug donde después de que una señal se cancelaba (por tocar VWAP), las siguientes barras separadas NO generaban nueva señal amarilla.
  - **Síntoma**:
    1. Vela 1 abre por debajo del VWAP → Se pinta amarilla ✓
    2. Vela 1 toca el VWAP → Se cancela, vuelve a color normal ✓
    3. Vela 2 abre separada del VWAP → NO se pinta amarilla ✗
    4. Vela 2 cierra separada del VWAP → Sigue sin pintarse ✗
  - **Causa**: Cuando se cancelaba la señal (líneas ~1143 SHORT, ~1440 LONG), solo se reseteaba `highSignal2Fired = false`, pero NO se reseteaba `lastSignaledHighAnchorBar`. La doble verificación de v1.0.33 bloqueaba la nueva señal porque `sessionHighBarIdx == lastSignaledHighAnchorBar` seguía siendo verdadero.
  - **Solución**: Ahora cuando se cancela la señal por tocar VWAP, TAMBIÉN se resetea `lastSignaledHighAnchorBar = -1` y `lastSignaledLowAnchorBar = -1`, permitiendo que la siguiente barra separada genere una nueva señal.
  - **Cambio Técnico**: Agregado `lastSignaledHighAnchorBar = -1;` en línea ~1144 y `lastSignaledLowAnchorBar = -1;` en línea ~1441.
  - **Resultado**: Después de una cancelación, la siguiente barra separada del VWAP correctamente genera una nueva señal amarilla.

## [1.0.34] - 2026-01-26
### Corregido
- **Vela Amarilla No Aparece en Playback (REGRESIÓN)**: Se corrigió el bug recurrente donde la vela amarilla (Señal 2) no aparecía en playback tick-a-tick, pero sí aparecía después de presionar F5.
  - **Síntoma**: Al ejecutar playback, cuando se disparaba la Señal 2, la flecha y etiqueta "Entry 1" aparecían correctamente, pero la vela NO se pintaba amarilla. Al presionar F5 (recarga histórica), la vela sí aparecía amarilla.
  - **Causa**: Cuando se disparaba la señal (líneas ~1199 SHORT, ~1490 LONG), el código solo seteaba `highSignal2BarIdx = CurrentBar` pero NO ejecutaba `BarBrushes[0] = Brushes.Yellow`. El código de "Persistent Painting" (líneas ~1256, ~1546) sí pintaba la barra, pero se ejecuta DESPUÉS en el ciclo de OnBarUpdate, y en algunos casos no se ejecutaba en el mismo tick.
  - **Solución**: Agregado `BarBrushes[0] = Brushes.Yellow` INMEDIATAMENTE después de setear `highSignal2BarIdx = CurrentBar` cuando se dispara la señal. Esto garantiza que la barra se pinte amarilla en el mismo tick donde se crea la flecha y etiqueta.
  - **Referencia Histórica**: Este es el mismo problema que se intentó resolver en v1.0.28 (InvalidateVisual) y v1.0.29 (Persistent Painting). La solución correcta es pintar INMEDIATAMENTE cuando se dispara la señal, NO solo en el código de pintado persistente.
  - **Resultado**: Ahora la vela se pinta amarilla INMEDIATAMENTE en playback, sin necesidad de presionar F5.

## [1.0.33] - 2026-01-26
### Corregido
- **Múltiples Señales por VWAP Anchor (DEFINITIVO)**: Implementación de doble verificación (flag + tracker) para garantizar UNA SOLA señal por anchor VWAP.
  - **Síntoma**: A pesar de los fixes en v1.0.31-32, seguían apareciendo múltiples señales "Entry 1" para el mismo anchor VWAP.
  - **Causa**: El flag `highSignal2Fired` no se estaba reseteando cuando se creaba un nuevo anchor, Y no se estaba verificando el tracker `lastSignaledHighAnchorBar` junto con el flag.
  - **Solución**:
    1. **Reset en Nuevo Anchor**: Ahora cuando se crea un nuevo HIGH/LOW anchor (líneas ~795, ~832), se resetea explícitamente `highSignal2Fired = false` y `lowSignal2Fired = false`.
    2. **Doble Verificación**: Antes de disparar señal, se verifica AMBOS: `!highSignal2Fired` Y `sessionHighBarIdx != lastSignaledHighAnchorBar`. Solo si AMBAS condiciones son verdaderas, se permite la señal.
    3. **Set Tracker**: Después de disparar señal, se setea AMBOS: `highSignal2Fired = true` Y `lastSignaledHighAnchorBar = sessionHighBarIdx`.
  - **Debug Mejorado**: Los logs ahora muestran ambos valores (Flag y AnchorSignaled) para facilitar diagnóstico.
  - **Resultado**: GARANTÍA ABSOLUTA de UNA señal por anchor VWAP. Imposible disparar segunda señal para el mismo anchor.

## [1.0.32] - 2026-01-26
### Corregido
- **Threading Error en ChartControl.InvalidateVisual()**: Eliminado el error "The calling thread cannot access this object because a different thread owns it".
  - **Causa**: `ChartControl.InvalidateVisual()` se llamaba directamente desde `OnBarUpdate()`, que puede ejecutarse en thread diferente al UI thread.
  - **Solución**: Ahora se ejecuta en el Dispatcher del UI thread: `ChartControl.Dispatcher.InvokeAsync(() => ChartControl.InvalidateVisual())`.
### Añadido
- **Debug Logging para Flags**: Se agregaron logs `[DEBUG FLAG]` para diagnosticar el estado de `highSignal2Fired` y `lowSignal2Fired`:
  - Cuando se verifica el flag antes de disparar señal
  - Cuando se setea el flag después de disparar señal
  - Cuando se resetea el flag al tocar VWAP

## [1.0.31] - 2026-01-26
### Corregido
- **Señales en TODAS las Velas**: Se corrigió el bug crítico donde se generaba una señal "Entry 1" y vela amarilla en CADA barra que cumplía la condición de separación, en lugar de generar SOLO UNA señal por ciclo.
  - **Síntoma**: Si el precio estaba separado del VWAP por el threshold, cada nueva barra generaba una etiqueta "Entry 1" y se pintaba amarilla, creando docenas de señales para el mismo "swing" de precios.
  - **Causa**: La lógica de v1.0.30 verificaba `sessionLowBarIdx != lastSignaledLowAnchorBar`, pero cuando el LOW del día bajaba gradualmente (cada barra era un nuevo anchor), esta condición siempre era verdadera, permitiendo señales infinitas.
  - **Solución**: Cambio a usar el flag booleano `lowSignal2Fired` (y `highSignal2Fired` para shorts). La señal SOLO se genera si `!lowSignal2Fired`, garantizando UNA señal por ciclo.
  - **Ciclo de Señal**: La señal se resetea (permitiendo una nueva) cuando:
    1. El precio toca el VWAP (cancelación - línea 1414: `lowSignal2Fired = false`)
    2. Nueva sesión/día (reset completo en `ResetSession()`)
  - **Cambio Técnico**: Líneas ~1459 (LONG) y ~1184 (SHORT) - Cambio de condición `if (sessionLowBarIdx != lastSignaledLowAnchorBar)` a `if (!lowSignal2Fired)`.
  - **Resultado**: Ahora aparece exactamente UNA señal "Entry 1" y UNA vela amarilla por cada "ciclo" de separación del VWAP, sin importar cuántas barras permanezca separado.

## [1.0.30] - 2026-01-26
### Nota
- **Versión Obsoleta**: Esta versión intentó resolver el problema de señales múltiples verificando `sessionLowBarIdx != CurrentBar` antes de resetear el tracker, pero no solucionó el caso donde el LOW/HIGH baja gradualmente en barras consecutivas. La v1.0.31 implementa la solución correcta usando flags booleanos.
### Corregido
- **Señales Múltiples en la Misma Barra (OnEachTick)**: Se eliminó el bug crítico donde aparecían múltiples señales "Entry 1" y múltiples velas amarillas para el mismo VWAP anchor debido a resets repetidos en modo Calculate.OnEachTick.
  - **Síntoma**: En una barra volátil con muchos ticks, cada vez que el LOW (o HIGH) de la barra cambiaba, se generaba una nueva señal "Entry 1" con su propia vela amarilla y etiqueta, resultando en 5-10+ señales duplicadas en la misma barra.
  - **Causa Raíz**: En Calculate.OnEachTick, el LOW/HIGH de la barra actual puede cambiar con cada tick. El código de líneas 789 y 823 reseteaba `lastSignaledHighAnchorBar = -1` y `lastSignaledLowAnchorBar = -1` CADA VEZ que detectaba un nuevo extremo, incluso si era dentro de la MISMA barra (CurrentBar no cambiaba). Esto causaba que la condición `sessionLowBarIdx != lastSignaledLowAnchorBar` se volviera verdadera repetidamente en la misma barra.
  - **Ejemplo de Flujo Problemático**:
    1. Tick 1: LOW=100 → Nuevo mínimo → `sessionLowBarIdx=105`, `lastSignaledLowAnchorBar=-1` → Precio separado → **Genera Signal 2** → `lastSignaledLowAnchorBar=105`
    2. Tick 2: LOW=99 → Nuevo mínimo OTRA VEZ (mismo CurrentBar) → `sessionLowBarIdx=105`, **`lastSignaledLowAnchorBar=-1`** (resetea!) → Precio separado → **Genera Signal 2 OTRA VEZ**
    3. Tick 3-N: Se repite el ciclo...
  - **Solución**: Se modificó la lógica para SOLO resetear el tracker si el anchor está cambiando a una **BARRA DIFERENTE**. Ahora se verifica `if (sessionLowBarIdx != CurrentBar)` antes de resetear, evitando resets múltiples en la misma barra.
  - **Cambios Técnicos**:
    - Línea ~789 (HIGH): `if (sessionHighBarIdx != CurrentBar) lastSignaledHighAnchorBar = -1;`
    - Línea ~823 (LOW): `if (sessionLowBarIdx != CurrentBar) lastSignaledLowAnchorBar = -1;`
  - **Resultado**: Ahora aparece exactamente UNA señal "Entry 1" y UNA vela amarilla por cada anchor VWAP, incluso en barras extremadamente volátiles con muchos cambios de tick.

## [1.0.29] - 2026-01-25
### Corregido
- **Señales Múltiples para el Mismo Anchor VWAP**: Se eliminó el bug donde aparecían múltiples señales "Entry 1" para el mismo VWAP (debería ser solo UNA señal por anchor).
  - **Síntoma**: Cada vez que una vela se separaba del VWAP, generaba una nueva flecha y etiqueta "Entry 1", incluso para el mismo anchor VWAP.
  - **Causa**: El código reseteaba `lastSignaledLowAnchorBar = -1` cuando el precio rompía un **nivel de sesión** (Asia Low, Europe Low, etc.), permitiendo señales duplicadas del mismo anchor VWAP.
  - **Solución**: Se eliminaron los resets de `lastSignaledLowAnchorBar` y `lastSignaledHighAnchorBar` de la sección CheckTouches (líneas 1839 y 1943). Ahora estos trackers SOLO se resetean cuando se crea un **nuevo anchor VWAP** (nuevo High/Low), no cuando se rompe una sesión.
  - **Resultado**: Ahora aparece exactamente UNA señal "Entry 1" por cada anchor VWAP, independientemente de cuántos niveles de sesión se rompan.

- **Velas Amarillas NO Persistían**: Se corrigió el bug donde las velas amarillas (Señal 2) desaparecían después de cerrar.
  - **Síntoma**: Las flechas y etiquetas permanecían visibles, pero la vela amarilla solo se veía mientras era la barra actual. Cuando cerraba, volvía al color normal.
  - **Causa**: El código solo pintaba cuando `lowSignal2BarIdx == CurrentBar`, lo cual solo es verdadero para la barra actual. Al avanzar a la siguiente barra, la condición era falsa y no se repintaba.
  - **Solución**: Se modificó la lógica para calcular `barsAgo` y pintar la barra correcta usando `BarBrushes[barsAgo] = Brushes.Yellow` en cada OnBarUpdate, independientemente de si es la barra actual o histórica.
  - **Cambio Técnico**: Líneas ~1234 (SHORT) y ~1505 (LONG).

## [1.0.28] - 2026-01-25
### Corregido
- **Visualización Inmediata de Señal 2**: Se corrigió el bug donde la vela amarilla y las flechas/etiquetas de la Señal 2 NO aparecían inmediatamente en playback/realtime, requiriendo presionar F5 para verlas.
  - **Síntoma**: La lógica funcionaba correctamente (logs mostraban señal disparada), pero la visualización NO se actualizaba hasta refrescar el gráfico con F5.
  - **Causa**: Los objetos `Draw.ArrowUp/ArrowDown` y `Draw.Text` se creaban correctamente, pero el gráfico no se refrescaba automáticamente en playback tick-a-tick.
  - **Solución**: Se agregó `ChartControl?.InvalidateVisual()` inmediatamente después de crear los objetos Draw de la Señal 2, forzando el refresh inmediato del gráfico.
  - **Cambio Técnico**: Agregado después de líneas 1491 (LONG) y 1223 (SHORT).

## [1.0.27] - 2026-01-25
### Corregido
- **Permanencia de Señal 2 (Vela Amarilla)**: Se corrigió el bug crítico donde las señales amarillas desaparecían cuando el precio tocaba el VWAP en barras futuras.
  - **Comportamiento Anterior (INCORRECTO)**: Si una vela se pintaba amarilla (Señal 2) y cerraba sin tocar el VWAP, pero 15 barras después el precio tocaba el VWAP, la señal amarilla desaparecía retroactivamente.
  - **Comportamiento Nuevo (CORRECTO)**:
    - La Señal 2 se **CANCELA** SOLO si la vela que abrió separada del VWAP **toca el VWAP en la MISMA barra** antes de cerrar.
    - Si la vela **cierra sin tocar** el VWAP, la señal amarilla es **PERMANENTE** y se mantiene visible para siempre.
    - Toques del VWAP en barras futuras **NO afectan** la señal existente.
  - **Reset**: La señal solo se resetea cuando el precio crea un nuevo anchor VWAP (nuevo High para shorts, nuevo Low para longs).
  - **Evidencia del Bug**: Log mostraba cancelación en Bar:7128 (15 barras después de la señal en Bar:7113).
  - **Cambio Técnico**: Se agregó la condición `&& lowSignal2BarIdx == CurrentBar` y `&& highSignal2BarIdx == CurrentBar` para limitar la cancelación solo a la misma barra donde se generó la señal.

## [1.0.26] - 2026-01-25
### Añadido
- **Sistema de Logging a Archivo**: Se implementó un sistema completo de logging para facilitar el debugging y diagnóstico de problemas.
  - **Parámetro**: `Logging a Archivo` en grupo "05. Alertas & Debug" (default: false)
  - **Ubicación**: Los logs se escriben en `Documents/NinjaTrader 8/trace/RelativeVwap_Debug_YYYYMMDD.txt`
  - **Contenido**: Cada log incluye timestamp, categoría, CurrentBar, hora de la vela y mensaje detallado
  - **Categorías implementadas**:
    - `SYSTEM`: Inicio del sistema, versión, instrumento
    - `ANCHOR`: Creación de nuevos anchors VWAP (High/Low)
    - `SIGNAL2`: Activación de Señal 2 (vela amarilla) con datos de separación
    - `CANCEL`: Cancelación de Señal 2 cuando toca el VWAP
  - **Uso**: Activar el parámetro, ejecutar playback, y revisar el archivo de log para análisis detallado de por qué las señales se activan o cancelan

## [1.0.25] - 2026-01-25
### Corregido
- **Señal 2 en nuevos anchors VWAP**: Se corrigió un bug crítico donde la Señal 2 (vela amarilla) NO aparecía en tiempo real cuando se creaba un nuevo anchor VWAP intraday.
  - **Síntoma**: Si el precio rompía un Low creando un nuevo VWAP Low, la siguiente vela que abría por encima del VWAP NO se pintaba amarilla en vivo (playback), pero SÍ aparecía después de F5 (recarga histórica).
  - **Causa**: El tracker `lastSignaledLowAnchorBar` (y `lastSignaledHighAnchorBar` para Highs) NO se reseteaba al crear un nuevo anchor, bloqueando la señal.
  - **Solución**: Ahora cuando se crea un nuevo anchor VWAP (líneas 742 y 771), se resetea explícitamente el tracker a `-1`, permitiendo que la Señal 2 se active correctamente para el nuevo anchor.
  - **Evidencia**: Vela con Low=21515.00, VWAP Lo=21503.26 (separación de 11.74 puntos), nunca tocó el VWAP, debió pintarse amarilla en vivo pero no lo hizo hasta F5.

## [1.0.24] - 2026-01-25
### Mejoras
- **DataBox con VWAP Hi/Lo**: Los valores de VWAP anclado a High y Low ahora son visibles en el Data Box de NinjaTrader.
  - Se renombraron las series a "VWAP Hi" y "VWAP Lo" para mayor claridad.
  - Los colores se sincronizan con `HighVWAPColor` y `LowVWAPColor`.
  - Se usa `double.NaN` antes del anchor point para evitar líneas duplicadas.
  - Se omite `base.OnRender()` para eliminar las líneas de plots automáticos.

### Corregido
- **Cancelación de Señal 2 completa**: Cuando la vela toca el VWAP, ahora se:
  - Remueve la flecha "Entry 1" y el texto asociado
  - Despinta la vela amarilla correctamente
  - Resetea `lastSignaledAnchorBar` para permitir nuevas señales del mismo anchor
- **Validación de índice**: Se agregó verificación `barsAgo >= 0 && barsAgo < CurrentBar` antes de acceder a `BarBrushes`.

## [1.0.23] - 2026-01-25
### Cambios
- **Data Box Visible**: Se activaron y renombraron las series de datos internas a "Supply" y "Demand". Ahora los valores del VWAP se muestran correctamente en la ventana Data Box de NinjaTrader.
- **Reversión de Resurrección (Muerte Estricta)**: Se eliminó la lógica de v1.0.22.
  - **Motivo**: El usuario prefiere un comportamiento estricto: "Si la vela toca el VWAP, la señal muere definitivamente para esa vela".
  - **Consecuencia**: La discrepancia de F5 (donde una señal aparece en el histórico aunque murió en vivo) se acepta como una limitación técnica de NinjaTrader (que no ve los toques intra-vela en el histórico), priorizando la seguridad operativa en tiempo real.

## [1.0.22] - 2026-01-25
### Corregido
- **Discrepancia F5 (Resurrección de Señal)**: Se solucionó el problema donde una señal desaparecía para siempre si tocaba el VWAP (o entraba en zona de threshold) momentáneamente en tiempo real, pero reaparecía al recargar (F5).
  - **Causa**: La lógica de "Muerte Súbita" bloqueaba el anclaje permanentemente.
  - **Solución**: Ahora, si la señal se cancela en la **misma barra** donde nació, el código "revierte el tiempo": desbloquea el anclaje y restaura el contador de secuencia. Esto permite que la señal vuelva a aparecer ("resucite") si el precio se mueve de nuevo a una posición válida antes del cierre de la vela.

## [1.0.21] - 2026-01-25
### Mejoras
- **Lógica Híbrida para Estabilidad Visual (F5 Fix)**: Se implementó un sistema dual para calcular la Señal 2 (Vela Amarilla).
  1.  **Validación de Apertura (Gap)**: Usa el **VWAP de la vela anterior** (`VWAP[1]`). Comparar el Precio de Apertura (fijo) con el VWAP Anterior (fijo) hace que la decisión de "pintar" sea 100% estable y no dependa de F5 o recargas. Evita el "poste que se mueve".
  2.  **Validación de Toque (Cancelación)**: Usa el **VWAP Actual** (Visual). Comparar el Precio (Wick) con el VWAP Visible asegura que si visualmente toca la línea, la señal se cancela.
  - El resultado es lo mejor de ambos mundos: **Estabilidad en tiempo real** (no desaparece la señal) y **Precisión visual** (no miente sobre toques).

## [1.0.20] - 2026-01-25
### Corregido
- **Limpieza Visual de Señal 2 (Fantasma)**: Se corrigió un error donde la flecha y el texto de la Señal 2 (Amarilla) permanecían en pantalla incluso después de que la vela tocara el VWAP.
  - Aunque el color amarillo de la vela se borraba correctamente (desde la v1.0.15), los objetos de dibujo (Flecha/Texto) insertados en ticks anteriores persistían por defecto en NinjaTrader.
  - Ahora el código fuerza la eliminación explícita (`RemoveDrawObject`) de estos elementos visuales al detectar el toque, eliminando cualquier "señal fantasma".

## [1.0.19] - 2026-01-25
### Revertido
- **Consistencia Visual (Volver a VWAP Actual)**: Se revirtió el cambio experimental de usar el VWAP anterior (`VWAP[1]`).
  - **Motivo**: Causaba discrepancias donde la señal se validaba/invalidaba contra una linea invisible (la anterior) mientras el usuario veía otra linea (la actual), generando confusión en situaciones de toque límite.
  - **Estado Actual**: Las señales ahora se calculan 100% contra el **VWAP Actual** (la línea visible). Esto, combinado con los filtros de Gaps estrictos (v1.0.17), asegura que "Lo que Ves es Lo que Obtienes".

## [1.0.18] - 2026-01-25
### Mejoras
- **Estabilidad de Señales (Lógica Estática)**: Se cambió la referencia de VWAP usada para calcular señales.
  - Ahora se usa el **VWAP de la vela anterior** (`VWAP[1]`) como referencia fija ("Línea en la Arena").
  - Esto evita que la referencia se mueva mientras la vela actual se está formando, eliminando la inestabilidad o parpadeo de la vela amarilla (Señal 2) y asegurando que las condiciones de "Toque" y "Gap" sean consistentes durante toda la duración de la barra.

## [1.0.17] - 2026-01-25
### Corregido
- **Filtro de Gaps en Señal 2 (Vela Amarilla)**: Se movió el filtro de apertura a la Señal 2 según feedback del usuario (revertido de la Señal 3).
  - Ahora es la **Señal 2** la que no se activa si la vela abre en el lado incorrecto del VWAP (gaps), evitando que se pinte de amarillo prematuramente.
  - Se reforzó la lógica para que la Señal 2 sea **imposible** de activar si la vela está tocando estrictamente el VWAP, solucionando conflictos con umbrales bajos o negativos.

## [1.0.16] - 2026-01-25
### Corregido
- **Filtro de Geometría en Señal 3 (Entry)**: Se añadió una validación estricta de la apertura de la vela para evitar señales prematuras en gaps.
  - **Short**: Requiere que la vela abra **por debajo** del VWAP (`Open < VWAP`) para validarse como un retest alcista a la resistencia.
  - **Long**: Requiere que la vela abra **por encima** del VWAP (`Open > VWAP`) para validarse como un retest bajista al soporte.
  - Esto evita que velas que "nacen" cruzando la línea (o gaps) disparen la señal instantáneamente.

## [1.0.15] - 2026-01-25
### Corregido
- **Persistencia Incorrecta de Vela Amarilla**: Se corrigió un error donde la vela se mantenía amarilla incluso después de tocar el VWAP en la misma barra.
  - Ahora, al tocar la línea VWAP, se resetea explícitamente el estado de pintado (`highSignal2BarIdx = -1`), permitiendo que el color vuelva a su estado normal inmediatamente.

## [1.0.14] - 2026-01-25
### Corregido
- **Visualización VWAP Sincronizada con Cálculo**: Se corrigió un error donde la línea visual del VWAP siempre usaba el "Precio Típico" (H+L+C)/3, independientemente de la configuración del usuario.
  - Ahora la línea visual respeta el parámetro `VwapMethod` (Close, Typical, OHLC4), coincidiendo exactamente con el valor lógico usado para las señales.
  - Esto elimina la discrepancia visual donde las velas parecían no tocar la línea pero generaban señal.

## [1.0.13] - 2026-01-25
### Revertido
- **Filtro de Ruido en Señal 2**: Se revirtió el cambio de la versión 1.0.12. El filtro de proximidad no era la solución correcta y violaba las reglas de "No Adivinar". Se restauran los logs de debug para continuar la investigación.

## [1.0.12] - 2026-01-25
### Corregido
- **Filtro de Ruido en Señal 2**: Se añadió un filtro de proximidad (`CurrentBar > AnchorBar + 1`) para evitar que la Señal 2 (Entrada) se dispare inmediatamente en la vela siguiente a un nuevo anclaje VWAP.
  - Esto evita falsas señales cuando el precio hace un "micro-pullback" natural al formar un nuevo High/Low, que técnicamente cruza el VWAP pero no representa una estructura de retest válida.

## [1.0.11] - 2026-01-25
### Corregido
- **Pintado en Vivo de Señal 2**: Se solucionó un error visual donde la vela amarilla (Señal 2) parpadeaba o desaparecía en tiempo real (Playback/Live) debido al ciclo de ticks de NinjaTrader.
  - Se implementó persistencia de estado por vela (`highSignal2BarIdx`) para asegurar que el color se mantenga en cada tick de la barra activa.

## [1.0.10] - 2026-01-23
### Añadido
- **Etiquetas Personalizadas**: Implementado sistema de etiquetas personalizables para las señales.
  - Nuevo parámetro `LabelDisplayMode` con opciones: Default, Simple, Custom.
  - Nuevos campos de texto `CustomSignal1Text`, `CustomSignal2Text`, `CustomSignal3Text` para definir textos propios.
### Corregido
- **Cálculo de Días en Etiquetas (Trading Days)**: Se perfeccionó el cálculo para que ignore fines de semana (días hábiles).
  - Ejemplo: Un setup el Lunes (o Domingo noche) vs una sesión del Viernes ahora se mostrará correctamente como **1 día** de diferencia (ej. `UH1`), en lugar de 3 días calendario o 0 días erróneos.
  - Se implementó lógica de "Días Hábiles" (Business Days) para contar solo de Lunes a Viernes.
- **Persistencia Color Señal 2**: Se corrigió un error donde la vela amarilla (Señal 2) permanecía pintada incluso si el precio tocaba el VWAP posteriormente en la misma barra (lo cual debería invalidar la señal visual). Ahora el color se elimina correctamente si ocurre el toque.
  
### Cambiado
- **Icono Señal 2**: Se reemplazó el icono de "Punto" (Dot) por una "Flecha" (Arrow), igualando el estilo visual de la Señal 3.
- **Visibilidad Granular de Señales**: Se reemplazó la opción global "Mostrar Iconos Señal" por 3 opciones individuales:
  - `Mostrar Señal 1`: Controla la visibilidad del Triángulo y su Texto (Ruptura/Liquidez).
  - `Mostrar Señal 2`: Controla la visibilidad de la Flecha y su Texto (Entrada 1).
  - `Mostrar Señal 3`: Controla la visibilidad de la Flecha y su Texto (Entrada 2).
  - `Mostrar Señal 3`: Controla la visibilidad de la Flecha y su Texto (Entrada 2).
  - **Corrección**: Se aseguró que al ocultar una señal, también se oculte su etiqueta de texto asociada.
  - Defaults: "Supply" (antes High VWAP) y "Demand" (antes Low VWAP).
  - Configurable en el grupo "03. Visuales VWAP".
- **Relative Delta 2.0 (Mejoras Mayores)**:
  - **Línea Cero Sesión USA**: Proyecta una línea de referencia desde el inicio de la sesión (Default 10:30).
    - **Histórico**: Las líneas de días anteriores permanecen visibles.
    - **Lógica Exacta**: Corrección para detectar el inicio de sesión exacto ignorando datos overnight.
  - **Optimización Gráfica**: Reescritura del motor de renderizado usando Caché de Direct2D. Elimina el lag completamente.
  - **Persistencia de Colores**: Los colores personalizados (Texto, Líneas) ahora se guardan correctamente en los Templates.
  - **Estilo Por Defecto**: Configuración inicial ajustada a "Limpio" (Velas transparentes/blancas, Textos blancos).

### Eliminado
- **UseSimpleLabels**: Propiedad obsoleta eliminada en favor del nuevo sistema `LabelMode`.

## [1.0.9] - 2026-01-23
### Mejoras (UI)
- **Organización de Propiedades**: Se han reorganizado todas las propiedades del indicador en grupos lógicos y numerados para una apariencia más profesional en el panel de configuración.
  - 01. Configuración Principal
  - 02. Sesiones de Tiempo
  - 03. Visuales VWAP
  - 04. Señales y Textos
  - 05. Alertas & Debug
  - 06. Contador
- **Etiquetas**: Se añadieron descripciones (tooltips) a varias propiedades para mejor claridad.

## [1.0.8] - 2026-01-23
### Añadido
- **Pintado de Señal 2**: Se ha añadido la funcionalidad para pintar la vela de color amarillo cuando ocurre una "Signal 2" (Rebote en VWAP opuesto).

## [1.0.7] - 2026-01-20
### Añadido
- Versión anterior estable con cálculo de VWAP anclado a extremos de sesión.
- Lógica de señales de trading básica.
- Integración inicial para SessionLevelsStrategy.
