# Changelog - RelativeDelta

Historial de cambios del indicador RelativeDelta para NinjaTrader 8.

---

## [v1.0.2] - 2026-01-15

### Añadido
- **Control de Wick (Mecha) Separado**: Añadidos parámetros `WickColor` y `WickWidth` para controlar el color y grosor de la mecha vertical independientemente del Shadow (borde del cuerpo).
  - `WickColor`: Default White.
  - `WickWidth`: Default 2.

> [!WARNING]
> **Cambio que rompe compatibilidad (Breaking Change)**
> La firma del método `RelativeDelta(...)` ha cambiado. Estrategias que instancian este indicador programáticamente (como `SessionLevelsStrategy`) deben actualizarse para pasar los nuevos argumentos `WickColor` y `WickWidth`.

## [v1.0.1] - 2026-01-15

### Corregido
- **Ancho de Barras Sincronizado**: Ahora las barras delta tienen exactamente el mismo ancho que las velas del gráfico principal. Cambiado de `chartControl.BarWidth` a `ChartBars.Properties.ChartStyle.BarWidth * 2`.

---

## [v1.0.0] - Fecha Original Desconocida

### Descripción General
**RelativeDelta** es un indicador de Delta Cumulativo (Cumulative Delta) que visualiza el delta como velas OHLC en un panel separado. Calcula el balance entre órdenes ejecutadas al Ask (compras agresivas) vs al Bid (ventas agresivas) para detectar presión compradora/vendedora.

### Funcionalidad Core
- **Cálculo de Delta**: Usa datos tick-by-tick (`BarsPeriodType.Tick, 1`) para determinar si cada operación fue al Ask (compra) o al Bid (venta).
- **Velas OHLC de Delta**: Muestra delta_open, delta_high, delta_low, delta_close como velas estilo candlestick.
- **Renderizado Direct2D**: Usa `OnRender` con SharpDX para dibujar las velas de delta con alto rendimiento.
- **Reset por Sesión**: El delta se reinicia a 0 al inicio de cada sesión de trading.

### Parámetros Disponibles

#### Grupo "Parameters"
| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `MinSize` | int | 0 | Filtro de tamaño mínimo para incluir operaciones |
| `ShowDivs` | bool | false | Mostrar divergencias entre precio y delta |

#### Grupo "Performance"
| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `DaysToLoad` | int | 3 | Días de historial a calcular (0 = todos) |

#### Grupo "Optics"
| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `BarColorUp` | Brush | White | Color del cuerpo cuando delta cierra > delta abre |
| `BarColorDown` | Brush | RoyalBlue | Color del cuerpo cuando delta cierra < delta abre |
| `ShadowColor` | Brush | Silver | Color de las mechas (sombras) |
| `ShadowWidth` | int | 1 | Grosor de las mechas |

#### Grupo "Línea Horizontal"
| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `HorizontalLineColor` | Brush | RoyalBlue | Color de la línea horizontal principal |
| `HorizontalLineWidth` | int | 10 | Grosor de la línea |
| `HorizontalLineValue` | double | 0 | Valor de la línea (nivel de delta) |
| `HorizontalLineAlphaPercent` | int | 50 | Transparencia (0-100%) |

#### Grupo "Líneas Extra"
Líneas horizontales configurables para niveles de referencia:
- **±2500**: ShowLine2500, Line2500Color, Line2500Width, Line2500Alpha
- **±5000**: ShowLine5000, Line5000Color, Line5000Width, Line5000Alpha
- **±10000**: ShowLine10000, Line10000Color, Line10000Width, Line10000Alpha
- **LineLabelColor**: Color del texto de las etiquetas
- **LineLabelBackground**: Color de fondo de las etiquetas

### Plots Expuestos
El indicador expone cuatro Series accesibles por otras estrategias/indicadores:
- `Values[0]` / `DeltaOpen`: Delta al abrir la barra
- `Values[1]` / `DeltaHigh`: Delta máximo de la barra
- `Values[2]` / `DeltaLow`: Delta mínimo de la barra
- `Values[3]` / `DeltaClose`: Delta al cerrar la barra

### Divergencias (ShowDivs)
Cuando está habilitado, detecta divergencias usando Stochastics:
- **Divergencia Alcista**: Delta Low ↑ mientras Precio Low ↓ (con Stoch K < 20)
- **Divergencia Bajista**: Delta High ↓ mientras Precio High ↑ (con Stoch K > 80)

### Características Técnicas
- **Calculate.OnEachTick**: Actualiza en cada tick para precisión máxima.
- **MaximumBarsLookBack.Infinite**: Mantiene todo el historial disponible.
- **Optimización de Performance**: Salta barras anteriores a `DaysToLoad` días.

---

## Issues Conocidos

### Performance
- El cálculo tick-by-tick puede ser intensivo en CPU para períodos largos.
- Se recomienda mantener `DaysToLoad` en valores bajos (3-5 días).

### Compatibilidad
- Requiere acceso a datos de Ask/Bid tick-by-tick.
- Funciona solo con instrumentos que proveen estos datos (futuros, forex).
