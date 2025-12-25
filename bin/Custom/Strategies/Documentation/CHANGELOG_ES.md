# Registro de Cambios (Changelog)

Todos los cambios notables en el proyecto `SessionLevelsStrategy` serán documentados en este archivo.

El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.0.0/), y este proyecto adhiere a [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
