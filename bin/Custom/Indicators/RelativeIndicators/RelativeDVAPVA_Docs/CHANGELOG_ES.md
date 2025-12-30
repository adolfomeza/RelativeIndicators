# Registro de Cambios (Changelog) - RelativeDVAPVA

Todos los cambios notables en **RelativeDVAPVA** serán documentados en este archivo.

El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.0.0/), y este proyecto adhiere a [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [2.5.20] - 2025-12-30
### 🎨 Ajuste de Z-Order

### Cambiado
- **Profundidad visual (Z-Order)**: Se ha cambiado el Z-Order de `-100` a `-300` para asegurar que los rectángulos y líneas PVA se rendericen detrás de las barras de precios y otros indicadores.

---

## [2.5.19] - 2025-12-30
### ✨ Lógica de Mitigación de Zonas PVA

### Añadido
- **Mitigación Automática**: Las zonas PVA ahora detectan si el precio cruza su rango.
  - **Condición**: Si el precio cruza más del X% de la zona (configurable vía `PvaCutoffPercent`, default 50%).
  - **Acción**: Si se mitiga, el dibujo de la zona (líneas y relleno) se "corta" y termina al final de la sesión actual (18:00 / Fin de Sesión).
  - **Visual**: Las zonas no mitigadas continúan infinitamente. Las mitigadas se detienen en el día de la mitigación.

---

## [2.5.18] - 2025-12-30
### ✨ Mejoras visuales en Zonas PVA

### Añadido
- **Relleno de Zonas PVA**: Ahora las zonas PVA tienen un color de relleno con opacidad configurable y se extienden en el tiempo (30 días) para simular infinito junto con las líneas.
- **Visual**: Las líneas PVA ahora usan `Draw.Ray` para extensión infinita real.
- **Configuración**:
  - `PVA Opacity` (0-100%): Controla la transparencia del relleno entre las líneas.
  - Se eliminó la restricción `MaxDaysToDraw` para las zonas PVA, permitiendo verlas en todo el historial cargado.

---

## [2.5.17] - 2025-12-30
### ✨ Nueva Implementación de Zonas PVA (Simple)

### Añadido
- **Zonas PVA Simplificadas**: Se ha implementado una nueva lógica para dibujar líneas DVA del día anterior.
  - Al inicio de cada sesión, se dibujan dos líneas horizontales que representan el `UpperBand1` (pDVAH) y `LowerBand1` (pDVAL) de la sesión finalizada.
  - Las líneas se extienden hacia la derecha cubriendo la sesión actual.
  - **Propiedades Editables**: Nuevo grupo "2. PVA Zones" con opciones para `ShowPVA` (Mostrar/Ocultar), Color, Grosor y Estilo de línea.
- **Correcciones**: Solucionado error de compilación por atributos duplicados (`CS0579`).
- **Configuración de Plots**: Habilitada la edición de propiedades de trazado (`Plots`) desde la UI.

---

## [2.5.16] - 2025-12-30
### 🗑️ Eliminación Completa de Funcionalidades PVA

### Eliminado
- **Propiedades Ocultas**: Se han ocultado (`[Browsable(false)]`) todas las propiedades relacionadas con:
  - Zonas de Sesión (PVA)
  - Señales BPB / RPB / IPB / EF
  - Configuraciones de Mitigación y Breakout
  - Botones y colores de estado PVA
- **Funcionalidad**: El indicador ahora funciona exclusivamente como un indicador de VWAP y Bandas de Desviación (DVA). Todo el código relacionado con la lógica de zonas y señales ha sido desactivado o escondido.
- **Limpieza UI**: El panel de propiedades ahora solo muestra las configuraciones relevantes para VWAP y Bandas.

---

## [2.5.10] - 2025-12-30
### 🐛 Corrección de Lógica de Mitigación Duplicada

### Corregido
- **Eliminada lógica de mitigación antigua**: Se identificó y comentó un bloque de código antiguo (líneas ~1800-1808) que sobrescribía la nueva lógica direccional implementada en v2.5.9.
- **Resultado**: Ahora la mitigación respeta estrictamente la dirección de entrada (Toque desde arriba requiere bajada de X%, Toque desde abajo requiere subida de X%), y no se activa simplemente por cruzar el nivel de cutoff.

---

## [2.5.9] - 2025-12-29
### 🔧 Simplificación de Lógica de Mitigación

### Cambiado
- **Opción A implementada**: Solo usa la PRIMERA dirección de entrada:
  - Si el precio toca `UpperY` primero → debe bajar X% para mitigar
  - Si el precio toca `LowerY` primero → debe subir X% para mitigar
- **Removida dependencia** de IsGapLong/IsGapShort/IsRotational para mitigación
- Logs ahora muestran `[v2.5.9] MITIGATED FROM ABOVE/BELOW`

---

## [2.5.8] - 2025-12-29
### ✨ Versión Visible en Chart + Debug Zones

### Añadido
- **Versión visible en chart**: Ahora la versión se muestra en la esquina superior izquierda del chart
- **Debug log de creación de zonas**: Log `[v2.5.8] ZONE CREATED:` muestra el tipo de zona (GAP_LONG/GAP_SHORT/ROTATIONAL)

---

## [2.5.7] - 2025-12-29
### 🐛 Corrección de Mitigación Basada en Tipo de Apertura

### Corregido
- **Mitigación ahora usa IsGapLong/IsGapShort/IsRotational**: La lógica anterior marcaba zonas como mitigadas incorrectamente porque `TouchedFromAbove` se activaba al tocar el borde (no al entrar desde afuera).
- **GAP LONG**: Si abrió arriba de la zona → solo se mitiga cuando baja X% desde UpperY
- **GAP SHORT**: Si abrió abajo de la zona → solo se mitiga cuando sube X% desde LowerY
- **ROTATIONAL**: Si abrió dentro → debe salir por un lado y recorrer X% para mitigarse

---

## [2.5.6] - 2025-12-29
### ✨ Lógica de Mitigación Direccional

### Cambiado
- **Mitigación direccional**: Ahora la zona rastrea desde qué lado entró el precio (arriba o abajo). El porcentaje de mitigación (`zoneCutoffPercentage`) se calcula desde el punto de entrada:
  - Si entra desde arriba (tocó `UpperY`): debe recorrer X% hacia abajo
  - Si entra desde abajo (tocó `LowerY`): debe recorrer X% hacia arriba
- **Nuevos campos en SessionZone**: `TouchedFromAbove`, `TouchedFromBelow`

---

## [2.5.5] - 2025-12-29
### 🐛 Corrección de Mitigación de Zonas

### Corregido
- **Bug**: Las zonas mitigadas no se cerraban al fin de sesión. La variable `IsMitigated` nunca se seteaba a `true` cuando el precio cruzaba el `cutoffY`. Ahora cuando `IsBreached = true`, también se marca `IsMitigated = true` para que la zona se cierre correctamente.

---

## [2.5.4] - 2025-12-29
### 🐛 Corrección de Zonas PVA

### Corregido
- **Bug crítico**: Las zonas PVA no se pintaban en datos históricos. El filtro usaba `DateTime.Now.Date` en lugar de `Time[0].Date`, causando que todas las zonas históricas fueran omitidas durante el reprocesamiento del chart.

---

## [2.5.3] - 2025-12-28
### 🔧 Corrección de Colores

### Corregido
- **Bandas SD width**: Revertido a 1px (VWAP se mantiene en 2px)
- **Zonas PVA**: Revertido color a `Gray` (no era parte del cambio original)

---

## [2.5.2] - 2025-12-28
### 🎨 Cambio de Colores Predeterminados
Nueva paleta de colores para las bandas DVA.

### Cambiado
- **Bandas DVA**: Todas las bandas ahora usan `RoyalBlue` por defecto
- **VWAP Line**: Cambiado de `Blue/Red` a `RoyalBlue`
- **VWAP width**: Cambiado de 3px a 2px
- **VWAP style**: Cambiado de `DashDot` a `Solid`

---

## [2.5.1] - 2025-12-28
### 🧹 Limpieza de Código
Refactorización menor sin cambios funcionales.

### Eliminado
- Import duplicado `using System.Windows.Input;`
- Asignación duplicada `Plots[6].DashStyleHelper = dash1Style;`
- Código de diagnóstico (`DIAGNOSTIC: LIST OF INDICATORS`) que imprimía en Output

---

## [2.5.0] - 2025-12-28
### 🎉 Versión Inicial Documentada
Primera versión oficialmente documentada del indicador RelativeDVAPVA.

> **Nota**: La versión original del indicador era `v 2.5 - October 27, 2019`. 
> Se adopta como v2.5.0 para seguir SemVer.

### Funcionalidades Incluidas

#### VWAP y Bandas
- **Session VWAP**: VWAP calculado para la sesión de trading
- **Bandas de Desviación Estándar**: SD 0.5, 1, 1.5, 2, 3 (configurables)
- **Quarter Range Bands**: Opción alternativa a SD
- **Áreas coloreadas** entre bandas con opacidad configurable

#### Zonas DVA/PVA
- **pDVAH / pDVAL**: Prior Day Value Area High/Low
- **Zonas de sesión** con etiquetas configurables
- **Cutoff Percentage**: Para determinar límites de la zona de valor
- **Historial configurable**: Máximo días a mostrar

#### Señales de Trading
| Señal | Nombre | Descripción |
|-------|--------|-------------|
| **IPB** | Initial Push Back | Retroceso inicial desde extremo |
| **EF** | Exhaustion Failure | Fallo de momentum |
| **BPB** | Breakout Pullback | Pullback tras breakout confirmado |
| **RPB** | Rejection Pullback | Pullback tras rechazo |

#### Machine State Logic
- Estados: Waiting, Neutral, ImbalanceLong, ImbalanceShort, FailedLong, FailedShort, Rotational, WaitingForRPB
- Lógica de breakout con confirmación por tiempo/distancia
- Filtro de volatilidad (broad bars)

#### Interfaz de Usuario
- **Botón de Estado**: Muestra estado actual en la barra de herramientas
- **Botón de Señal**: Muestra última señal generada
- **Colores configurables** para cada estado y señal

#### Alertas
- Alertas sonoras para IPB, EF, BPB, RPB
- Opción de envío de email
- Opción de adjuntar screenshot

#### Integración
- Properties públicas para Market Analyzer (`StateText`, `StateColor`, `ActiveZoneHigh`, `ActiveZoneLow`)
- Series públicas para señales (`ipbLong`, `efShort`, etc.)

### Configuración Principal
| Parámetro | Default | Descripción |
|-----------|---------|-------------|
| `SessionType` | Full_Session | Tipo de sesión (Full/Custom) |
| `BandType` | Standard_Deviation | Tipo de bandas |
| `ShowSessionZones` | `true` | Mostrar zonas DVA/PVA |
| `MaxDaysToDraw` | `3` | Días de historial a mostrar |
| `AcceptanceMode` | Any | Modo de confirmación breakout |
| `BreakoutConfirmationBars` | `1` | Barras para confirmar breakout |

---

> **Nota**: Este changelog se creó el 2025-12-28. Cambios anteriores no están documentados.
> A partir de ahora, cada modificación será registrada con su versión correspondiente.
