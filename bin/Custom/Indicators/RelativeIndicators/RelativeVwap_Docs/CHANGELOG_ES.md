# RelativeVwap - Historial de Cambios

Este documento registra todos los cambios notables en el proyecto **RelativeVwap**.

El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.0.0/),
y este proyecto se adhiere a [Semantic Versioning](https://semver.org/lang/es/).

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
