# RelativeNewsFilter

Indicador de NinjaTrader 8 para filtrar operaciones basándose en el calendario de noticias económicas de ForexFactory.

---

## 📋 Descripción

`RelativeNewsFilter` descarga automáticamente el calendario económico semanal y proporciona alertas visuales y por email antes de eventos importantes. Diseñado para proteger tus operaciones evitando períodos de alta volatilidad causados por noticias.

### Características Principales

✅ **50+ Instrumentos Soportados** - Auto-detección de moneda/país  
✅ **Alertas por Email** - Notificaciones antes de eventos importantes  
✅ **Visualización en Gráfico** - Zonas de exclusión coloreadas  
✅ **Cache Inteligente** - Descarga una vez, usa todo el día  
✅ **Limpieza Automática** - Elimina archivos antiguos (>7 días)  
✅ **Filtrado por Impacto** - High, Medium, Low  
✅ **Click Interactivo** - Haz clic en zonas para ver detalles  

---

## 🎯 Uso Básico

### 1. Agregar al Gráfico

1. Abre un gráfico en NinjaTrader 8
2. Click derecho → Indicators → RelativeNewsFilter
3. Configura los parámetros según tus necesidades

### 2. Configuración Recomendada

**Para Trading Conservador:**
- `PauseBeforeMinutes`: 15
- `PauseAfterMinutes`: 15
- `FilterImpact`: "High"
- `EnableEmailAlerts`: true (opcional)

**Para Trading Agresivo:**
- `PauseBeforeMinutes`: 5
- `PauseAfterMinutes`: 5
- `FilterImpact`: "Medium"

### 3. Uso desde Estrategias

```csharp
// Agregar el indicador
RelativeNewsFilter newsFilter = RelativeNewsFilter(5, 10, "High", "", true);

// Verificar si hay noticias inminentes
if (newsFilter.IsNewsImminent)
{
    Print("NEWS ALERT: " + newsFilter.NextNewsTitle);
    Print("Minutes to news: " + newsFilter.MinutesToNews);
    // Pausar trading o cerrar posiciones
}
```

---

## ⚙️ Parámetros

### Grupo: Parameters

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `PauseBeforeMinutes` | int (0-120) | 5 | Minutos antes del evento para pausar |
| `PauseAfterMinutes` | int (0-120) | 10 | Minutos después del evento para pausar |
| `FilterImpact` | string | "High" | Impacto mínimo: "Low", "Medium", "High" |
| `CustomCurrencies` | string | "" | Monedas personalizadas (ej: "USD,EUR") |

### Grupo: Visual

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `ShowLines` | bool | true | Mostrar zonas en el gráfico |
| `ShowHistoricalNews` | bool | false | Incluir eventos pasados |
| `LineColor` | Brush | Red | Color de las zonas |
| `TextColor` | Brush | White | Color del texto (al hacer clic) |

### Grupo: Email

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `EnableEmailAlerts` | bool | false | Activar alertas por email |
| `EmailAlertMinutes` | int (1-60) | 15 | Minutos antes para enviar email |
| `SmtpServer` | string | smtp.gmail.com | Servidor SMTP |
| `SmtpPort` | int | 587 | Puerto SMTP |
| `EmailFrom` | string | "" | Email remitente |
| `EmailTo` | string | "" | Email destinatario |
| `EmailPassword` | string | "" | Contraseña o App Password |

---

## 📧 Configuración de Email (Gmail)

### Paso 1: Habilitar 2FA
1. Ve a https://myaccount.google.com/security
2. Activa la verificación en dos pasos

### Paso 2: Crear App Password
1. Ve a https://myaccount.google.com/apppasswords
2. Selecciona "Correo" y "Windows Computer"
3. Copia el password de 16 caracteres

### Paso 3: Configurar en NinjaTrader
- `Enable Email Alerts`: `true`
- `SMTP Server`: `smtp.gmail.com`
- `SMTP Port`: `587`
- `Email From`: `tu-email@gmail.com`
- `Email To`: `tu-email@gmail.com`
- `Email Password`: `[16-char app password]`

---

## 🎨 Instrumentos Soportados

### Índices USA
ES, NQ, YM, RTY (E-mini)  
MES, MNQ, MYM, M2K (Micro)

### Índices Europeos
FDAX (DAX), FESX (STOXX)

### Forex
6E (EUR/USD), 6A (AUD/USD), 6J (JPY/USD), 6B (GBP/USD)  
6C (CAD/USD), 6S (CHF/USD), 6M (MXN/USD), 6N (NZD/USD)

### Metales
GC (Gold), SI (Silver), HG (Copper), PL (Platinum)

### Energía
CL (Crude Oil), NG (Natural Gas), RB (Gasoline), HO (Heating Oil)

### Agricultura
ZC (Corn), ZW (Wheat), ZS (Soybeans), ZL (Soy Oil), ZM (Soy Meal)  
KC (Coffee), SB (Sugar), CT (Cotton), CC (Cocoa)

### Bonos del Tesoro
ZB (30-Year), ZN (10-Year), ZF (5-Year), ZT (2-Year), UB (Ultra)

---

## 📚 Propiedades Públicas

Estas propiedades están disponibles para estrategias:

```csharp
public bool IsNewsImminent { get; }      // ¿Hay noticias cercanas?
public string NextNewsTitle { get; }     // Título del próximo evento
public double MinutesToNews { get; }     // Minutos hasta el evento
```

---

## 🗂️ Sistema de Caché

### Ubicación
```
Documents/NinjaTrader 8/NewsCache/
```

### Formato de Archivos
```
NewsCache_YYYYMMDD.xml
```

### Retención
- Los archivos se mantienen por **7 días**
- Limpieza automática al cargar el indicador
- Un archivo por día (descarga única)

---

## 🔧 Solución de Problemas

### No se Descargan Noticias
- Verifica conexión a internet
- Revisa Output Window para mensajes de error
- El feed es: `https://nfs.faireconomy.media/ff_calendar_thisweek.xml`

### Emails No se Envían
- Verifica que `EnableEmailAlerts` esté en `true`
- Confirma credenciales SMTP correctas
- Gmail requiere "App Password", no tu contraseña normal
- Los emails solo se envían en modo `Realtime`

### Zonas No Aparecen en el Gráfico
- Verifica que `ShowLines` esté en `true`
- Asegúrate que hay eventos descargados (revisa Output Window)
- Cambia `FilterImpact` a "Low" temporalmente para ver más eventos

---

## 📎 Archivos Relacionados

- [`CHANGELOG.md`](./CHANGELOG.md) - Historial de versiones
- [`RelativeNewsFilter.cs`](../RelativeNewsFilter.cs) - Código fuente

---

## 📄 Versión

**Versión Actual**: v2.0.0  
**Fecha**: 2026-01-19  
**Compatibilidad**: NinjaTrader 8 (todas las versiones)

---

## 🤝 Contribuciones

Para reportar bugs o sugerir mejoras, contacta al desarrollador.

---

## ⚖️ Licencia

Este indicador es para uso personal. No redistribuir sin permiso.
