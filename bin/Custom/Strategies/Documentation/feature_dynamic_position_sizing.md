# Feature Request: Normalización de Riesgo por Instrumento

**Estado**: Pendiente (implementar después de playback y features previas)  
**Prioridad**: Alta  
**Fecha propuesta**: 2025-12-25  
**Origen**: Usuario identificó riesgo desigual entre instrumentos usando Quantity fijo

---

## Problema Actual

La estrategia usa **`Quantity` fijo** (ej: 2 contratos) para todos los instrumentos, resultando en **riesgo desigual en dólares**.

### Ejemplo del Problema

**Configuración actual**: `Quantity = 2` contratos, SL = 10 ticks

| Instrumento | Valor/Tick | Riesgo Real |
|-------------|------------|-------------|
| MNQ | $2.00 | 10 × $2 × 2 = **$40** |
| MES | $5.00 | 10 × $5 × 2 = **$100** |
| MYM | $0.50 | 10 × $0.50 × 2 = **$10** |
| M2K | $0.50 | 10 × $0.50 × 2 = **$10** |

**Resultado**: 
- Pérdida en MNQ = -$40
- Ganancia en M2K = +$10
- **Net = -$30** ❌ (aunque ambos fueron 10 puntos)

---

## Solución Propuesta

**Calcular cantidad dinámicamente** basado en:
1. Riesgo deseado en dólares (`RiskPerTrade`)
2. Ticks de riesgo real del trade (`setupAnchorPrice ± 1 tick`)
3. Valor de tick del instrumento (`Instrument.MasterInstrument.PointValue × TickSize`)

---

## Fórmula

```
Quantity = RiskPerTrade / (TicksDeRiesgo × ValorPorTick)
```

### Variables
- **RiskPerTrade**: Cantidad en USD que estás dispuesto a perder (ej: $50)
- **TicksDeRiesgo**: Distancia en ticks entre Entry y SL
  - SHORT: `(setupAnchorPrice + TickSize) - entryPrice`
  - LONG: `entryPrice - (setupAnchorPrice - TickSize)`
- **ValorPorTick**: `Instrument.MasterInstrument.PointValue × TickSize`

---

## Implementación

### Nueva Propiedad

```csharp
[NinjaScriptProperty]
[Display(Name = "Risk Per Trade (USD)", Order = 1, GroupName = "Risk Management")]
public double RiskPerTradeUSD { get; set; } = 50.0; // $50 por trade

[NinjaScriptProperty]
[Display(Name = "Min Quantity", Order = 2, GroupName = "Risk Management")]
public int MinQuantity { get; set; } = 1; // Mínimo 1 contrato

[NinjaScriptProperty]
[Display(Name = "Max Quantity", Order = 3, GroupName = "Risk Management")]
public int MaxQuantity { get; set; } = 10; // Máximo 10 contratos

[NinjaScriptProperty]
[Display(Name = "Use Dynamic Sizing", Order = 4, GroupName = "Risk Management")]
public bool UseDynamicSizing { get; set; } = true; // Toggle ON/OFF
```

---

### Método de Cálculo

```csharp
private int CalculateDynamicQuantity(double entryPrice, double stopPrice)
{
    // Si dynamic sizing está OFF, usar Quantity fijo
    if (!UseDynamicSizing) return Quantity;
    
    // Calcular ticks de riesgo
    double riskInPrice = Math.Abs(entryPrice - stopPrice);
    double riskInTicks = riskInPrice / TickSize;
    
    // Valor de 1 tick en USD
    double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
    
    // Fórmula: Quantity = RiskUSD / (Ticks × Value)
    double calculatedQty = RiskPerTradeUSD / (riskInTicks * tickValue);
    
    // Redondear a entero
    int quantity = (int)Math.Round(calculatedQty);
    
    // Aplicar límites
    if (quantity < MinQuantity) quantity = MinQuantity;
    if (quantity > MaxQuantity) quantity = MaxQuantity;
    
    // Log para debugging
    if (EnableDebugLogs)
    {
        Print(string.Format("DYNAMIC SIZING: Risk=${0} Ticks={1:F1} Value=${2:F2} → Qty={3}",
            RiskPerTradeUSD, riskInTicks, tickValue, quantity));
    }
    
    return quantity;
}
```

---

### Integración en Confirmación

**Modificar líneas de confirmación SHORT (1590-1636)**:

```csharp
// Calcular entry y stop proyectados
double projectedEntry = setupVWAP;
double projectedStop = setupAnchorPrice + TickSize;

// NUEVO: Calcular cantidad dinámica
int dynamicQuantity = CalculateDynamicQuantity(projectedEntry, projectedStop);

// Validar R/R con cantidad calculada
double risk = Math.Abs(projectedEntry - projectedStop);
// ... resto de validación R/R ...

if (validDirection && risk > 0 && (reward / risk) >= MinRiskRewardRatio)
{
    validatedTargetPrice = projectedTarget;
    
    // NUEVO: Usar cantidad dinámica
    int orderQuantity = dynamicQuantity;
    
    double limitPrice = Instrument.MasterInstrument.RoundToTickSize(setupVWAP);
    
    if (State == State.Realtime)
    {
        entryOrder = SubmitOrderUnmanaged(0, OrderAction.SellShort, OrderType.Limit, 
            orderQuantity, limitPrice, 0, "", "EntryA_Short"); // ← Usar orderQuantity
        // ...
    }
}
```

**Similar para LONG (líneas 1683-1728)**

---

### Modificar EnsureProtection

**Cambiar de `Quantity` fijo a cantidad real de la entrada**:

```csharp
private void EnsureProtection(string direction, string entrySignalName, int filledQty)
{
    // Usar filledQty directamente (ya viene de la ejecución real)
    // NO usar Quantity hardcoded
    
    int totalTp1Target = (filledQty + 1) / 2; // ← Usar filledQty
    //...
}
```

---

## Ejemplos con RiskPerTrade = $50

### MNQ (Nasdaq Micro)
```
Entry: 56.00
SL: 56.10 (10 ticks arriba)
TickValue: $2.00
Riesgo: 10 ticks

Quantity = $50 / (10 × $2) = $50 / $20 = 2.5 → 3 contratos
Riesgo real: 10 × $2 × 3 = $60 ✓ (cercano a $50)
```

### MES (S&P Micro)
```
Entry: 5700.00
SL: 5702.50 (10 ticks arriba)
TickValue: $5.00
Riesgo: 10 ticks

Quantity = $50 / (10 × $5) = $50 / $50 = 1 contrato
Riesgo real: 10 × $5 × 1 = $50 ✓
```

### MYM (Dow Micro)
```
Entry: 42000
SL: 42010 (10 ticks)
TickValue: $0.50
Riesgo: 10 ticks

Quantity = $50 / (10 × $0.50) = $50 / $5 = 10 contratos
Riesgo real: 10 × $0.50 × 10 = $50 ✓
```

### M2K (Russell Micro)
```
Similar a MYM: 10 contratos
Riesgo real: $50 ✓
```

---

## Tabla Comparativa

**Antes** (Quantity fijo = 2):

| Instrumento | Pérdida 10 ticks | Ganancia 10 ticks |
|-------------|------------------|-------------------|
| MNQ | -$40 | +$40 |
| MES | -$100 | +$100 |
| MYM | -$10 | +$10 |
| M2K | -$10 | +$10 |

**Después** (Risk = $50):

| Instrumento | Contratos | Pérdida 10 ticks | Ganancia 10 ticks |
|-------------|-----------|------------------|-------------------|
| MNQ | 3 | -$60 | +$60 |
| MES | 1 | -$50 | +$50 |
| MYM | 10 | -$50 | +$50 |
| M2K | 10 | -$50 | +$50 |

**Resultado**: Riesgo normalizado ~$50 en todos ✅

---

## Casos Edge

### Caso 1: Riesgo Muy Pequeño (< 5 ticks)
```
Entry: 56.00
SL: 56.02 (2 ticks)
RiskPerTrade: $50

MNQ: $50 / (2 × $2) = 12.5 → 13 contratos
Riesgo real: 2 × $2 × 13 = $52

¿Problema?: Cantidad muy alta para SL pequeño
```

**Solución**: Aplicar `MaxQuantity = 10` como límite superior

---

### Caso 2: Riesgo Muy Grande (> 50 ticks)
```
Entry: 56.00
SL: 57.00 (100 ticks)
RiskPerTrade: $50

MYM: $50 / (100 × $0.50) = 1 contrato
Riesgo real: 100 × $0.50 × 1 = $50 ✓
```

**Funciona bien**, pero podría rechazar el trade por R/R bajo.

---

### Caso 3: Instrumento con TickValue Muy Alto
```
Instrumento exótico: $100/tick
SL: 5 ticks
RiskPerTrade: $50

Quantity = $50 / (5 × $100) = 0.1 → MinQuantity = 1
Riesgo real: 5 × $100 × 1 = $500 ❌ (10x el riesgo deseado)
```

**Solución**: 
- Aumentar `RiskPerTrade` para ese instrumento
- O usar `UseDynamicSizing = false` y configurar `Quantity` manual

---

## Ajustes por Volatilidad (Opcional)

**Considerar ATR** para ajustar riesgo en mercados volátiles:

```csharp
private int CalculateDynamicQuantity(double entryPrice, double stopPrice)
{
    double riskInPrice = Math.Abs(entryPrice - stopPrice);
    double riskInTicks = riskInPrice / TickSize;
    double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
    
    // NUEVO: Factor de volatilidad
    double atr = ATR(14)[0];
    double avgRisk = 10 * TickSize; // Riesgo promedio esperado
    double volatilityFactor = avgRisk / atr; // Si ATR alto, reducir quantity
    
    double adjustedRisk = RiskPerTradeUSD * volatilityFactor;
    double calculatedQty = adjustedRisk / (riskInTicks * tickValue);
    
    int quantity = (int)Math.Round(calculatedQty);
    if (quantity < MinQuantity) quantity = MinQuantity;
    if (quantity > MaxQuantity) quantity = MaxQuantity;
    
    return quantity;
}
```

---

## Testing Requerido

1. **Backtest multi-instrumento** (MNQ, MES, MYM, M2K)
2. **Verificar**: Todas las pérdidas ~$50
3. **Verificar**: Todas las ganancias proporcionales
4. **Edge cases**: SL muy pequeños/grandes
5. **Performance**: No afecta velocidad de ejecución

---

## Beneficios

✅ **Riesgo consistente** en todos los instrumentos  
✅ **Gestión de capital** profesional  
✅ **Escalabilidad** a nuevos instrumentos sin reconfigurar  
✅ **Transparencia** en logs de cuántos contratos y por qué  

---

## Orden de Implementación

1. Validar estrategia básica (v1.7.25)
2. Implementar features de niveles internos
3. **Implementar dynamic sizing** (este documento)
4. Testing extensivo multi-instrumento
5. Ajustes de volatilidad (opcional)

---

## Notas

- Compatible con división TP1/TP2 existente
- No afecta lógica de targets ni R/R
- Toggle `UseDynamicSizing` permite OFF para testing
