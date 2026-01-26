# RelativeVwap - Historial de Cambios

Este documento registra todos los cambios notables en el proyecto **RelativeVwap**.

El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.0.0/),
y este proyecto se adhiere a [Semantic Versioning](https://semver.org/lang/es/).

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
