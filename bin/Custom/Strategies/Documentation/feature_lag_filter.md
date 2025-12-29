# Feature: Filtro de Lag de Chart

## Investigación (Documentación Oficial + PATS Toolbar)

### Documentación Oficial NinjaTrader
- **No existe función nativa** en NinjaScript para medir latencia de datos
- **`DisconnectDelaySeconds`**: Propiedad para manejar desconexiones (default 10s)

### PATS Toolbar (Indicador Existente)
El indicador `PATSToolBar.cs` ya calcula el lag usando `OnMarketData`:

```csharp
protected override void OnMarketData(MarketDataEventArgs e) { 
    if (e.MarketDataType == MarketDataType.Last && lastPrice != e.Price) 
        updateLAG(e.Price, (e.Time - Core.Globals.Now).TotalSeconds); 
}
```

**Método**: `e.Time - Core.Globals.Now`
- `e.Time`: Timestamp del tick recibido del feed de datos
- `Core.Globals.Now`: Hora actual del sistema
- El lag es **negativo** cuando hay retraso (ej: -0.5 segundos)

**Umbrales configurables**:
- `LagWarning`: 0.3 segundos (DarkOrange)
- `LagAlert`: 1.0 segundos (Red)

---

## Objetivo
Detectar cuando los datos del chart tienen retraso respecto al tiempo real y **bloquear el envío de órdenes** si el lag excede un umbral configurable.

## Problema
Cuando hay lag en el chart (por conexión, CPU, o feed de datos):
- La estrategia ve datos **viejos** pero cree que son actuales
- Puede enviar órdenes basadas en precios que ya no existen
- El precio actual puede estar muy lejos del nivel detectado

## Solución Propuesta

### 1. Detección de Lag
Comparar `Time[0]` (timestamp de la última barra) con `DateTime.Now`:

```csharp
// En cada OnBarUpdate
TimeSpan chartLag = DateTime.Now - Time[0];
double lagSeconds = chartLag.TotalSeconds;
```

### 2. Propiedad Configurable
```csharp
[NinjaScriptProperty]
[Range(1, 60)]
[Display(Name = "Max Chart Lag (Seconds)", Order = 50, GroupName = "Safety")]
public int MaxChartLagSeconds { get; set; } = 5; // Default 5 segundos
```

### 3. Verificación Antes de Enviar Orden
Agregar al inicio de la lógica de entrada:

```csharp
// v1.11.17: Lag Filter
TimeSpan chartLag = DateTime.Now - Time[0];
double lagSeconds = chartLag.TotalSeconds;

if (lagSeconds > MaxChartLagSeconds)
{
    Log($"{Time[0]} ORDER BLOCKED: Chart lag {lagSeconds:F1}s > {MaxChartLagSeconds}s threshold");
    return; // No enviar orden
}
```

### 4. Visual Feedback (Opcional)
Mostrar lag actual en el panel de estado:
```csharp
string lagInfo = $"Lag: {lagSeconds:F1}s";
// Agregar a DrawStatusPanel()
```

## Ubicaciones de Implementación

### Paso 1: Agregar Propiedad
- Archivo: `SessionLevelsStrategy.cs`
- Ubicación: Después de otras propiedades de Safety (~línea 200)

### Paso 2: Agregar Verificación
- Lugares donde se envían órdenes:
  1. `ManageEntryA_Plus()` - Confirmación Short (~línea 2705)
  2. `ManageEntryA_Plus()` - Confirmación Long (~línea 2810)

### Paso 3: Agregar Log
- Usar método `Log()` existente

## Consideraciones

### Tiempo de Barras
- En barras de 1 minuto, `Time[0]` puede estar hasta 59 segundos atrás normalmente
- El lag real = `DateTime.Now - Time[0] - (barras parciales)` 
- Para barras en construcción, usar `Time[0] + TimeSpan.FromMinutes(Period)`

### Alternativa: Usar Calculate.OnEachTick
- Si `Calculate = Calculate.OnEachTick`, el lag debería ser mínimo
- Solo bloquear si lag > umbral (ej. 5 segundos)

### Casos Especiales
- **Playback**: Ignorar verificación de lag (siempre hay lag)
- **Historical**: Ignorar verificación de lag
- **Solo aplicar en `State == State.Realtime`**

## Versión Target
- v1.11.17

## Complejidad
- Baja-Media (1-2 horas)

---

*Pendiente de aprobación para implementar*
