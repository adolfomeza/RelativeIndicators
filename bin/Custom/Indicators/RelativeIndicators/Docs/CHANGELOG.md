# RelativeNewsFilter - Changelog

Historial de versiones y cambios del indicador RelativeNewsFilter para NinjaTrader 8.

---

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
