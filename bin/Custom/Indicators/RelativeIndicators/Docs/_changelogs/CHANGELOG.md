# RelativeNewsFilter - Changelog

Historial de versiones y cambios del indicador RelativeNewsFilter para NinjaTrader 8.

---

## [Fix proximity adaptable] - 2026-04-29 — watcher.py escala por instrumento

### Problema
`watcher.py:process_request` usaba `proximity = max(50.0, min(2000.0, atr * 2.0))` con caps absolutos en "puntos del instrumento". Eso era válido para NQ (puntos = unidades de precio del orden de cientos) pero **catastrófico para FX**: para 6E (TickSize 0.0001, precio ~1.17), 50 unidades de precio = 42% del precio. Resultado: en 6E, niveles relevantes como pWDVAL (Weekly previous low, -71 pips del spot) quedaban dentro del filtro pero el cluster_threshold hardcoded de 15 unidades no detectaba confluencias bien.

### Fix
- **proximity adaptable**: `proximity = atr * factor` sin caps absolutos. Factor default 2.0x ATR.
- **fallback adaptable**: si ATR no se calcula, usa `0.5% del spot` (escala automática con el instrumento).
- **PROXIMITY_FACTOR_BY_INSTR** map para overrides por instrumento (FX agregado).
- **cluster_threshold escalado**: `2.5% del ATR` en vez de 15 hardcoded. Para 6E da ~1.5 pips (apropiado); para NQ ~10 pts (similar al anterior).

### Impacto
- 6E: proximity 122 pips, cluster 1.5 pips — niveles relevantes capturados correctamente.
- NQ: proximity 800 pts, cluster 10 pts — mismo comportamiento que antes.
- MGC, MCL, ES: idem.
- Snapshots NADRO ya no dependen de "puntos NQ-like".

---

## [Fix VAH/VAL acceptance] - 2026-04-29 — RelativeVolumeProfile detección bar-level

### Problema
La función `FindFirstAcceptanceBar` en `RelativeVolumeProfile.Rendering.cs` usaba criterio de **"close del día NADRO completo"** (último bar antes de las 18:00 ET) para detectar acceptance de VAH/VAL. Eso era más estricto que el criterio NADRO oficial Guía 03 §4: *"Aceptación: distancia de al menos el 50% del ritmo actual"*.

Caso concreto: oVAL 3d 1.17340 del 6E (28-abr). El precio cerró bars individuales abajo del nivel a las 02:30 (close 1.17305, 54% del ritmo del bar), pero el día NADRO 27-28 cerró arriba (close 17:00 28-abr = 1.17385). La lógica antigua descartaba la acceptance intra-día → la línea oVAL nunca se transformaba a dashed cuando el precio retornaba al nivel a las 11:30 del 28.

### Fix
Reemplazada Fase 1 de `FindFirstAcceptanceBar` por iteración bar-by-bar con criterio NADRO §4:
```csharp
double distance = isUpper ? (close - level) : (level - close);
bool accepted = distance > tolerance && distance >= 0.5 * (high - low);
```

- Saltea bars planos (`range <= tolerance`) para evitar falsos positivos.
- Mantiene tolerance = TickSize * 0.5 para evitar noise.
- Fase 2 (búsqueda de pullback) sin cambios.

### Impacto
- VAH/VAL/oVAH/oVAL/CVAH/CVAL/pVAH/pVAL: detectan acceptance + pullback más rápido (intra-día, no esperan al cierre del día NADRO).
- Líneas se transforman sólido→dashed en el bar del pullback post-acceptance.
- Aplica simétricamente a niveles H y L.
- POC sigue usando `FindFirstTouchBar` (wick simple), no afectado.

### Cómo activar
F7 + F5 al chart con `RelativeVolumeProfile`.

---

## [Infra] - 2026-04-29 — RelativeVolumeProfile publica composites/pVAs al registry

### Problema
El watcher Python (`RelativeMCP_Server/watcher.py`) generaba snapshots NADRO con CVAs/pVAs distintos a los que el usuario veía en chart:
- **Indicador NT8** (`RelativeVolumeProfile.NadroAutoMerge`): overlap 0.40 + D-shape gate.
- **Watcher Python** (`tpo_cva.get_cvas`): overlap 0.50, sin D-shape gate, recalculaba desde bars OHLCV.

Resultado: zonas presentes en chart no aparecían en el JSON del markup, y viceversa.

### Fix
- **`RelativeObserver.cs`**: `FormatValue` ahora soporta `IDictionary` y `IEnumerable` recursivamente, permitiendo payloads anidados en el endpoint `/indicator-state`.
- **`RelativeVolumeProfile.cs`**: el Publish al `RelativeIndicatorRegistry` ahora incluye `composites`, `closed_pvas`, `active_pva` y los parámetros del auto-merge (overlap_threshold, breakout_tolerance, require_dshape).
- **`RelativeVolumeProfile.NadroAutoMerge.cs`**: nuevos helpers `BuildCompositesPayload`, `BuildClosedPvasPayload`, `BuildActivePvaPayload` que serializan el estado real del indicador (mismas zonas que pinta en chart).
- **`RelativeMCP_Server/tools/replay.py`**: nueva función `_cvas_from_indicator_state` que lee el estado del indicador como source of truth. Si el indicador no está cargado o publicando (versión vieja), cae al fallback `tpo_cva.get_cvas`. El campo `_source` en `cvas_result` indica `indicator_registry` o `python_recalc`.

### Impacto
- Los snapshots NADRO ahora reflejan EXACTAMENTE las zonas del chart.
- `secondary_lines` y `closed_reason` se derivan localmente del listado del indicador con la misma tolerancia (default 0.5 pts).
- Compatible hacia atrás: si el indicador no publica `composites` (versión vieja sin recompilar), el watcher hace fallback transparente.

### `.csproj`
- Añadido `RelativeVolumeProfile.cs` que faltaba en `<Compile Include="...">`.

### Granularidad de publicación
- El Publish ahora corre también en `BarsInProgress == 1` (serie secundaria 1-min) además del bar primario. Antes con TF primario de 30min el state se actualizaba sólo cada 30min — un Pit-Open en mitad de una vela leía info hasta 30min stale. Ahora la latencia máxima es 1min independientemente del TF del chart.
- Helper extraído: `PublishStateToRegistry()` en `.NadroAutoMerge.cs`, llamado desde ambos lados.

### Spot del snapshot — cascada de fuentes
Antes `replay.py` exigía el `RelativeDailyVwap` cargado para `spot.close`. Cascada nueva:
1. `RelativeVolumeProfile.close` (siempre cargado en charts NADRO).
2. `RelativeDailyVwap.close` (si el fork está cargado).
3. `observer.get_bars(tf="1m", n=2).c` (universal).
Campo `spot.source` indica cuál resolvió.

### Niveles operables NADRO en el snapshot — alineado a Guía 04 §3
**Daily DVAH/DVAL excluido** (no es LTWV — referencia visual del usuario).
**Incluidos** ahora:
- `wDVAH/wDVAL, mDVAH/mDVAL, qDVAH/qDVAL, yDVAH/yDVAL` (LTWV developing, no eco).
- `pDVAH/pDVAL, pWDVAH/pWDVAL, pMDVAH/pMDVAL, pQDVAH/pQDVAL, pYDVAH/pYDVAL` (zonas previous, leídas de `VwapLevels/{TF}_{master}.txt` por nuevo helper `_read_previous_zones`).

Cambios:
- `replay.py:nadro_snapshot_replay`: `tfs` reducido a `["Weekly","Monthly","Quarterly","Annual"]`. Nuevo campo `previous_zones` en el output.
- `simulator.py:enumerate_hipos`: enumera LTWV W/M/Q/Y + previous zones; saca Daily.
- `watcher.py:build_levels`: incluye previous zones + LTWV developings (no eco). El filtro `level_type ∉ {DVAH,DVAL}` que antes excluía todos los DVA del markup fue removido — ahora los DVA W/M/Q/Y son operables.

---

## [v1.0.1] - 2026-04-10

### Fix RelativeNMonthlyVwap
- **Zone rendering corregido**: Zonas ahora parten desde `StartTime` (no desde borde del chart) y terminan en barra actual (activas) o `EndTime` (congeladas).
- **Todas las zonas visibles**: Activas + congeladas, eliminado filtro `!IsActive`.
- **Soporte Daily charts**: Eliminada restricción `!Bars.BarsType.IsIntraday`.
- **Etiquetas transparentes**: `zoneTextBackgroundOpacity = 0` en Monthly, Weekly y NMonthly.

## [v1.0.0] - 2026-04-10

### Nuevo Indicador: RelativeNMonthlyVwap
- **Fork de amaNMonthlyVWAP** con sistema de zonas historicas integrado.
- **Zonas de sesion**: Al cambiar de periodo (mensual/bimestral/trimestral/semestral/anual), se crea una zona con los valores de UpperBand1/LowerBand1 del periodo anterior.
- **Breach detection**: Las zonas detectan cuando el precio penetra el porcentaje configurado (ZoneCutoffPercentage) y se congelan al cruzar periodo.
- **Etiquetas dinamicas**: Prefijos automaticos segun resetPeriod (nm/2m/q/6m/y para actual, pNm/p2m/pQ/p6m/pY para zonas historicas).
- **Edad en etiquetas**: Las zonas muestran su antiguedad en periodos (ej: "-2Q" para 2 trimestres atras).
- **Anti-colision de labels**: Las etiquetas del periodo actual se desplazan si colisionan con etiquetas de zonas.
- **Export a archivo**: Exporta niveles DVAH/DVAL y zonas activas a `VwapLevels/{Timeframe}_{INSTRUMENT}.txt` cada 5 segundos en Realtime.
- **Rendering SharpDX**: Zonas renderizadas via OnRender con fill rectangulo, lineas horizontales y labels con fondo.
- **TypeConverter**: Oculta propiedades de bandas/custom hours segun configuracion (igual que original).
- **Reutiliza enums globales**: amaResetPeriodVWAPN, amaSessionTypeVWAPN, amaBandTypeVWAPN, amaTimeZonesVWAPN del original.
- **Namespace**: `NinjaTrader.NinjaScript.Indicators.RelativeIndicators`
- **Compile Include**: Agregado a NinjaTrader.Custom.csproj

## [v2.0.4] - 2026-02-08

### New Features (RelativeDelta) 🌟
- **4 Líneas Cero de Sesión**: Implementadas líneas cero independientes para 4 sesiones:
  - **Asia**: Configurable (Default 18:00)
  - **Europa**: Configurable (Default 03:00)
  - **USA**: Configurable (Default 10:30)
  - **Global**: Configurable (Default 17:00)
- **Etiquetas Visuales**: Cada línea ahora muestra su nombre y valor en el margen derecho del gráfico.
- **Personalización Completa**: Control total sobre color, grosor, estilo y transparencia.
- **Tiempos de Finalización (End Times)**: Configurable hora de fin para cada sesión, permitiendo limpiar el gráfico automáticamente (truncar líneas) al cierre de la sesión.


## [v2.0.3] - 2026-02-08

### Fixes (RelativeDelta) 🐛
- **XML Serialization Error**: Corregido error al guardar plantillas añadiendo `[XmlIgnore]` a propiedades de color.
- **Code Cleanup**: Eliminado código obsoleto de "Structure Analysis".

## [v2.0.2] - 2026-01-19

### Improved (RelativeDelta) 🚀
- **Smart Playback Loading**: Refinada la lógica de carga para Playback.
  - Ahora respeta el parámetro `DaysToLoad` incluso en Playback, pero calculando la fecha límite en base a la **fecha de simulación** y no a la fecha real.
  - **Beneficio**: Permite ver data histórica antigua de forma ligera (solo carga los últimos X días de la simulación) sin sobrecargar la memoria.

## [v2.0.1] - 2026-01-19

### Fixes (RelativeDelta) 🐛
- **Playback Visibility Fix**: Corregido problema que ocultaba el indicador en modo Playback al usar data histórica antigua.
  - **Solución**: El parámetro `DaysToLoad` ahora se ignora automáticamente cuando se detecta una conexión de Playback, permitiendo visualizar la data histórica completa sin restricciones.

---

## [v2.0.0] - 2026-01-19

### 🎉 Major Release - Mejoras Significativas

#### ✨ Nuevas Características

**Soporte Extendido de Instrumentos (50+ instrumentos)**
- Agregados **Micro Futuros**: MES, MNQ, MYM, M2K
- Agregados **Forex adicionales**: 6C (CAD), 6S (CHF), 6M (MXN), 6N (NZD)
- Agregados **Metales**: SI (Silver), HG (Copper), PL (Platinum)
- Agregados **Energía**: NG (Natural Gas), RB (Gasoline), HO (Heating Oil)
- Agregados **Agricultura**: ZC (Corn), ZW (Wheat), ZS (Soybeans), ZL (Soy Oil), ZM (Soy Meal), KC (Coffee), SB (Sugar), CT (Cotton), CC (Cocoa)
- Agregados **Bonos del Tesoro**: ZB (30Y), ZN (10Y), ZF (5Y), ZT (2Y), UB (Ultra)

**Sistema de Alertas por Email**
- Nueva funcionalidad de alertas por email antes de eventos importantes
- Configuración completa de SMTP (servidor, puerto, credenciales)
- Control anti-duplicados con HashSet
- Email personalizable con tiempo hasta el evento
- Propiedad `EnableEmailAlerts` para activar/desactivar
- Propiedad `EmailAlertMinutes` para configurar ventana de alerta (default: 15 min)

**Sistema de Caché Mejorado**
- Limpieza automática de archivos de caché >7 días
- Validación de integridad de archivos XML
- Logs mejorados de operaciones de caché
- Ejecución automática en `State.DataLoaded`

#### 🔧 Mejoras Técnicas

**Email**
- Using declarations agregadas: `System.Net`, `System.Net.Mail`
- Método `SendNewsEmail()` para envío de alertas
- Método `GetEmailKey()` para generación de claves únicas
- Variable `_sentEmailKeys` (HashSet) para prevenir duplicados

**Caché**
- Método `CleanOldCacheFiles()` para limpieza automática
- Método `IsValidCacheFile()` para validación de archivos
- Validación de tamaño (0 bytes - 10MB)
- Validación de parseo XML

**Configuración**
- 7 nuevas propiedades en grupo "Email":
  - `EnableEmailAlerts` (bool)
  - `EmailAlertMinutes` (int, 1-60)
  - `SmtpServer` (string)
  - `SmtpPort` (int, 1-65535)
  - `EmailFrom` (string)
  - `EmailTo` (string)
  - `EmailPassword` (string)

#### 📝 Archivos Modificados

- `RelativeNewsFilter.cs`:
  - Líneas 13-14: Using declarations
  - Línea 59: HashSet _sentEmailKeys
  - Líneas 92-98: Defaults de email
  - Líneas 106-107: CleanOldCacheFiles()
  - Líneas 188-194: Integración SendNewsEmail
  - Líneas 374-448: GetTargetCurrencies expandido
  - Líneas 470-514: Métodos de caché
  - Líneas 517-557: Métodos de email
  - Líneas 612-644: Propiedades email

---

## [v1.0.0] - Versión Inicial

### Características Iniciales

**Descarga de Noticias**
- Integración con ForexFactory Calendar API
- Descarga automática de eventos semanales
- Cache local de eventos en XML

**Filtrado de Eventos**
- Filtro por impacto: High, Medium, Low
- Auto-detección de moneda según instrumento
- Configuración personalizada de monedas

**Visualización**
- Zonas visuales (rectángulos) en el gráfico
- Ventanas configurables antes/después del evento
- Toggle de eventos históricos
- Click para mostrar detalles del evento

**Propiedades Expuestas**
- `IsNewsImminent` - Indica si hay noticias cercanas
- `NextNewsTitle` - Título del próximo evento
- `MinutesToNews` - Minutos hasta el evento

**Configuración Básica**
- `PauseBeforeMinutes` (default: 5)
- `PauseAfterMinutes` (default: 10)
- `FilterImpact` (default: "High")
- `CustomCurrencies` (default: auto-detect)
- `ShowLines` (default: true)
- `ShowHistoricalNews` (default: false)

**Soporte Inicial de Instrumentos**
- E-mini: ES, NQ, YM, RTY
- Forex: 6E (EUR), 6A (AUD), 6J (JPY), 6B (GBP)
- Commodities: GC (Gold), CL (Crude Oil)
- Índices Europeos: FDAX, FESX

---

## Notas de Compatibilidad

- **NinjaTrader 8**: Compatible con todas las versiones de NT8
- **Email**: Requiere configuración de SMTP (Gmail recomendado con App Password)
- **Cache**: Almacenado en `Documents/NinjaTrader 8/NewsCache/`

---

## Próximas Versiones Planificadas

### v2.1.0 (Futuro)
- [ ] Integración con múltiples fuentes de noticias
- [ ] Alertas de sonido personalizables
- [ ] Dashboard de próximos eventos
- [ ] Filtros por sesión de trading (Asia, London, NY)
- [ ] Estadísticas de eventos históricos

---

## Soporte

Para reportar problemas o solicitar características:
- Contactar al desarrollador
- Revisar documentación en carpeta `Docs/`
