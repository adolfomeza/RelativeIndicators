# Registro de Cambios (Changelog) - RelativeDVAPVA

Todos los cambios notables en **RelativeDVAPVA** serán documentados en este archivo.

El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.0.0/), y este proyecto adhiere a [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
