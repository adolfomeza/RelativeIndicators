# Registro de Cambios (Changelog) - RelativeVwap

Todos los cambios notables en **RelativeVwap** serán documentados en este archivo.

El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.0.0/), y este proyecto adhiere a [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0] - 2025-12-28
### 🎉 Versión Inicial
Primera versión oficialmente versionada del indicador RelativeVwap.

### Funcionalidades Incluidas

#### VWAP Anclado
- VWAP calculado desde el **High del día** (Short Setup)
- VWAP calculado desde el **Low del día** (Long Setup)
- Re-anclaje automático cuando se hacen nuevos extremos
- Historial de anclas previas (configurable hasta 365 días)

#### Niveles de Sesión
- **Asia**: Configurable (default 18:00 - 03:00 ET)
- **Europe**: Configurable (default 03:00 - 09:30 ET)
- **USA**: Configurable (default 09:30 - 16:00 ET)
- High/Low de cada sesión dibujados como líneas horizontales
- Líneas "ghost" extendidas hasta que el precio toque el nivel
- Clasificación de niveles internos vs extremos

#### Señales de Trading
- **Señal 1 (Entry)**: Detachment del VWAP + retorno al nivel
- Detachment configurable en ticks (`DetachmentTicks`)
- Soporte para `UseExchangeTime` (conversión automática NY → Local)

#### Visuales
- Colores configurables para VWAP High/Low
- Colores configurables para cada sesión (Asia/Europe/USA)
- Labels con códigos de señal (ej: "AH.1", "EL.2")
- Modo simple de labels (1, 2, 3)
- Offset configurable para iconos y texto
- Label background color configurable

#### Countdown
- Countdown para barras basadas en tiempo
- Countdown para barras basadas en volumen
- Posición configurable (X/Y offset)
- Color y tamaño de fuente configurables

#### Integración
- Plots públicos `Values[0]` (High VWAP) y `Values[1]` (Low VWAP)
- Listas públicas de sesiones (`AsiaSessions`, `EuropeSessions`, `USSessions`)
- Propiedad `LastSignalCode` para lectura por estrategias
- Propiedad `CurrentCountdownText` para display externo

#### Técnico
- Normalización ATR para spacing visual consistente entre instrumentos
- Anti-collision stacking para labels superpuestos
- Timer de actualización 4Hz para countdown en tiempo real
- Cache de timezone para optimización de rendimiento

### Configuración
| Parámetro | Default | Descripción |
|-----------|---------|-------------|
| `UseExchangeTime` | `true` | Interpretar tiempos como NY y convertir a local |
| `DetachmentTicks` | `2` | Ticks de separación para señal de detachment |
| `MaxHistoryDays` | `5` | Días máximos de historial a mostrar |
| `ShowLabels` | `true` | Mostrar labels de señales |
| `ShowCountdown` | `true` | Mostrar countdown de barra |

---

> **Nota**: Este changelog se creó el 2025-12-28. Cambios anteriores no están documentados.
> A partir de ahora, cada modificación será registrada con su versión correspondiente.
