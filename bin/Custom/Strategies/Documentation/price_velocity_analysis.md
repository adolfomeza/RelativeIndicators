# Price Velocity & Momentum Analysis - "Catching Falling Knives"

## 🎯 El Problema

### Escenario Común
```
Precio viene cayendo rápido hacia nivel de soporte (Short setup)
│
│  ↓↓↓  Velocidad: -5 puntos/minuto
│   ↓↓↓
│    ↓↓↓ Toca el nivel
│     ↓ ← ¿ENTRAR AQUÍ? (Catching falling knife)
│      
└────────── Nivel VWAP
```

### La Pregunta
> **¿Entramos inmediatamente o esperamos confirmación de absorción?**

**Opción A**: Entrar inmediatamente (asumir rebote)
- ✅ Mejor precio de entrada
- ❌ Riesgo: momentum continúa y nos atropella

**Opción B**: Esperar confirmación
- ✅ Mayor probabilidad de reversión real
- ❌ Peor precio de entrada (ya rebotó)

---

## 📊 Métricas de Velocity

### 1. **Rate of Change (ROC)**

```
ROC = (Precio Actual - Precio hace N bars) / N bars

Ejemplo:
Precio actual: 25,800
Precio hace 5 bars: 25,850
ROC = (25,800 - 25,850) / 5 = -10 puntos/bar
```

**Clasificación**:
- **Slow**: |ROC| < 5 puntos/bar
- **Medium**: 5 ≤ |ROC| < 15 puntos/bar
- **Fast**: |ROC| ≥ 15 puntos/bar

### 2. **Acceleration** (cambio en velocidad)

```
Acceleration = ROC(ahora) - ROC(hace N bars)

Positivo = Desacelerando (bueno para reversión)
Negativo = Acelerando (peligro - momentum continúa)
```

### 3. **Velocity Score**

```
Velocity Score = |ROC| * (1 + Acceleration Weight)

Si está acelerando: Score alto (peligro)
Si está desacelerando: Score bajo (mejor momento)
```

---

## 💻 Implementación en NinjaTrader

### Variables de Tracking

```csharp
// Agregar después de línea 160

// ===================================================================
// PRICE VELOCITY ANALYSIS (v1.7.35)
// ===================================================================

private const int VELOCITY_LOOKBACK = 5; // Bars para calcular ROC
private double priceRateOfChange = 0;
private double priceAcceleration = 0;
private string velocityClassification = "Unknown"; // Slow/Medium/Fast
private bool isFallingKnife = false;

// Thresholds (ajustar por instrumento)
private double slowVelocityThreshold = 5.0;
private double fastVelocityThreshold = 15.0;

// Confirmación de absorción
private bool hasAbsorptionConfirmation = false;
private int barsWaitingForAbsorption = 0;
private const int MAX_WAIT_BARS = 3; // Máximo bars a esperar confirmación
```

### Cálculo de Velocity en OnBarUpdate

```csharp
/// <summary>
/// Calcula velocity del precio antes de confirmar trigger
/// </summary>
private void CalculatePriceVelocity()
{
    if (CurrentBar < VELOCITY_LOOKBACK + 1) return;
    
    // Rate of Change actual
    double priceNow = Close[0];
    double priceBefore = Close[VELOCITY_LOOKBACK];
    priceRateOfChange = (priceNow - priceBefore) / VELOCITY_LOOKBACK;
    
    // Rate of Change anterior (para acceleration)
    double priceBeforePrev = Close[VELOCITY_LOOKBACK + 1];
    double previousROC = (Close[1] - priceBeforePrev) / VELOCITY_LOOKBACK;
    
    // Acceleration (cambio en ROC)
    priceAcceleration = priceRateOfChange - previousROC;
    
    // Clasificación
    double absROC = Math.Abs(priceRateOfChange);
    
    if (absROC < slowVelocityThreshold)
        velocityClassification = "Slow";
    else if (absROC < fastVelocityThreshold)
        velocityClassification = "Medium";
    else
        velocityClassification = "Fast";
    
    // Detectar "Falling Knife" (para Long setups)
    // o "Rising Knife" (para Short setups)
    // Criterios:
    // 1. Fast velocity
    // 2. Acelerando (acceleration negativo para caída)
    
    if (velocityClassification == "Fast" && priceAcceleration < 0)
    {
        isFallingKnife = true;
    }
    else
    {
        isFallingKnife = false;
    }
}
```

### Validación de Absorción

```csharp
/// <summary>
/// Verifica si hay señales de absorción en el nivel
/// </summary>
private bool CheckAbsorptionSignals(bool isLongSetup)
{
    // Señales de absorción:
    // 1. Volume spike
    // 2. Delta a favor
    // 3. Wick rejection
    // 4. Velocity desacelerando
    
    int absorptionScore = 0;
    
    // 1. Volume Spike
    double avgVolume = 0;
    for (int i = 1; i <= 20; i++)
    {
        avgVolume += Volume[i];
    }
    avgVolume /= 20;
    
    double volumeRatio = Volume[0] / avgVolume;
    if (volumeRatio > 1.5)
    {
        absorptionScore += 30;
        Log("ABSORPTION: High volume detected (ratio: " + volumeRatio.ToString("F2") + ")");
    }
    
    // 2. Delta a favor
    if (cumulativeDelta != null)
    {
        double deltaChange = cumulativeDelta.DeltaClose[0] - cumulativeDelta.DeltaClose[1];
        
        if (isLongSetup && deltaChange > 500) // Compradores entrando
        {
            absorptionScore += 40;
            Log("ABSORPTION: Bullish delta detected (change: " + deltaChange.ToString("F0") + ")");
        }
        else if (!isLongSetup && deltaChange < -500) // Vendedores entrando
        {
            absorptionScore += 40;
            Log("ABSORPTION: Bearish delta detected (change: " + deltaChange.ToString("F0") + ")");
        }
    }
    
    // 3. Wick Rejection
    bool hasWick = false;
    
    if (isLongSetup)
    {
        // Para Long, buscar wick inferior (rechazo de bajada)
        double bodySize = Math.Abs(Close[0] - Open[0]);
        double lowerWickSize = Math.Min(Open[0], Close[0]) - Low[0];
        
        if (lowerWickSize > bodySize * 1.5)
        {
            hasWick = true;
            absorptionScore += 20;
            Log("ABSORPTION: Lower wick rejection detected");
        }
    }
    else
    {
        // Para Short, buscar wick superior (rechazo de subida)
        double bodySize = Math.Abs(Close[0] - Open[0]);
        double upperWickSize = High[0] - Math.Max(Open[0], Close[0]);
        
        if (upperWickSize > bodySize * 1.5)
        {
            hasWick = true;
            absorptionScore += 20;
            Log("ABSORPTION: Upper wick rejection detected");
        }
    }
    
    // 4. Velocity desacelerando
    if (priceAcceleration > 0) // Desacelerando
    {
        absorptionScore += 10;
        Log("ABSORPTION: Price decelerating");
    }
    
    // Score threshold: 50+ para confirmar absorción
    bool hasAbsorption = absorptionScore >= 50;
    
    Log(string.Format("ABSORPTION_SCORE: {0} - {1}", 
        absorptionScore, hasAbsorption ? "CONFIRMED" : "NOT CONFIRMED"));
    
    return hasAbsorption;
}
```

### Lógica de Decisión en Entry

```csharp
// En ManageEntryA_Plus, ANTES de confirmar trigger

// Calcular velocity
CalculatePriceVelocity();

// Si detectamos "falling knife" scenario
if (isFallingKnife && velocityClassification == "Fast")
{
    Log(string.Format("FALLING_KNIFE detected: ROC={0:F2}, Accel={1:F2}", 
        priceRateOfChange, priceAcceleration));
    
    // Opción 1: RECHAZAR entrada
    if (RejectFallingKnives)
    {
        Log("Setup REJECTED: Falling knife (fast velocity with acceleration)");
        LogMissedOpportunity($"Falling knife (ROC={priceRateOfChange:F2})");
        return; // No entrar
    }
    
    // Opción 2: ESPERAR confirmación de absorción
    if (WaitForAbsorptionConfirmation)
    {
        hasAbsorptionConfirmation = CheckAbsorptionSignals(isLongSetup);
        
        if (!hasAbsorptionConfirmation)
        {
            // No hay absorción todavía, esperar
            barsWaitingForAbsorption++;
            
            if (barsWaitingForAbsorption > MAX_WAIT_BARS)
            {
                Log("Setup REJECTED: No absorption after " + MAX_WAIT_BARS + " bars");
                LogMissedOpportunity($"No absorption (waited {barsWaitingForAbsorption} bars)");
                barsWaitingForAbsorption = 0;
                return;
            }
            
            Log("Waiting for absorption confirmation... (bar " + barsWaitingForAbsorption + "/" + MAX_WAIT_BARS + ")");
            return; // Esperar próximo bar
        }
        else
        {
            Log("ABSORPTION CONFIRMED! Proceeding with entry.");
            barsWaitingForAbsorption = 0;
            // Continuar con entry
        }
    }
}
else
{
    // Velocity normal, no necesitamos confirmación especial
    Log(string.Format("Normal velocity: {0} (ROC={1:F2})", 
        velocityClassification, priceRateOfChange));
}

// ... resto del código de entry
```

### Properties Configurables

```csharp
[Display(Name="Reject Falling Knives", GroupName="5. Advanced", Order=10)]
public bool RejectFallingKnives { get; set; } = false;

[Display(Name="Wait for Absorption Confirmation", GroupName="5. Advanced", Order=11)]
public bool WaitForAbsorptionConfirmation { get; set; } = true;

[Display(Name="Max Wait Bars for Absorption", GroupName="5. Advanced", Order=12)]
[Range(1, 10)]
public int MaxWaitBarsAbsorption { get; set; } = 3;

[Display(Name="Slow Velocity Threshold", GroupName="5. Advanced", Order=13)]
public double SlowVelocityThreshold { get; set; } = 5.0;

[Display(Name="Fast Velocity Threshold", GroupName="5. Advanced", Order=14)]
public double FastVelocityThreshold { get; set; } = 15.0;
```

### Export a CSV

```csharp
// Agregar campos a CSV:
string csvLine = string.Format(
    "{0},{1},...," + // Campos existentes
    "{2:F2},{3:F2},{4},{5},{6},{7}," + // Velocity metrics
    "...",
    
    // ... campos existentes ...
    
    // Velocity Metrics
    priceRateOfChange,
    priceAcceleration,
    velocityClassification,
    isFallingKnife ? "true" : "false",
    hasAbsorptionConfirmation ? "true" : "false",
    barsWaitingForAbsorption
);
```

---

## 📊 Análisis en TradeAnalyzer

### Dashboard: Velocity Analysis

```javascript
// ===================================================================
// VELOCITY ANALYZER
// ===================================================================

class VelocityAnalyzer {
    
    analyzeByVelocity(trades) {
        const velocityGroups = {
            'Slow': [],
            'Medium': [],
            'Fast': []
        };
        
        trades.forEach(t => {
            const velocity = t.velocityClassification || 'Unknown';
            if (velocityGroups[velocity]) {
                velocityGroups[velocity].push(t);
            }
        });
        
        const results = [];
        
        Object.entries(velocityGroups).forEach(([velocity, trades]) => {
            if (trades.length === 0) return;
            
            const wins = trades.filter(t => t.pnl > 0).length;
            const winRate = (wins / trades.length) * 100;
            const avgPnL = trades.reduce((sum, t) => sum + t.pnl, 0) / trades.length;
            
            // Separar falling knives vs normal
            const fallingKnives = trades.filter(t => t.isFallingKnife === 'true');
            const normal = trades.filter(t => t.isFallingKnife !== 'true');
            
            results.push({
                velocity: velocity,
                total: trades.length,
                winRate: winRate,
                avgPnL: avgPnL,
                fallingKnives: {
                    count: fallingKnives.length,
                    winRate: this.calculateWinRate(fallingKnives),
                    avgPnL: this.calculateAvgPnL(fallingKnives)
                },
                normal: {
                    count: normal.length,
                    winRate: this.calculateWinRate(normal),
                    avgPnL: this.calculateAvgPnL(normal)
                }
            });
        });
        
        return results;
    }
    
    analyzeAbsorptionImpact(trades) {
        const withAbsorption = trades.filter(t => t.hasAbsorptionConfirmation === 'true');
        const withoutAbsorption = trades.filter(t => t.hasAbsorptionConfirmation !== 'true');
        
        return {
            withAbsorption: {
                count: withAbsorption.length,
                winRate: this.calculateWinRate(withAbsorption),
                avgPnL: this.calculateAvgPnL(withAbsorption)
            },
            withoutAbsorption: {
                count: withoutAbsorption.length,
                winRate: this.calculateWinRate(withoutAbsorption),
                avgPnL: this.calculateAvgPnL(withoutAbsorption)
            }
        };
    }
    
    calculateWinRate(trades) {
        if (trades.length === 0) return 0;
        const wins = trades.filter(t => t.pnl > 0).length;
        return (wins / trades.length) * 100;
    }
    
    calculateAvgPnL(trades) {
        if (trades.length === 0) return 0;
        return trades.reduce((sum, t) => sum + t.pnl, 0) / trades.length;
    }
    
    generateVelocityInsights(velocityResults, absorptionResults) {
        const insights = [];
        
        // 1. Fast velocity analysis
        const fast = velocityResults.find(r => r.velocity === 'Fast');
        if (fast && fast.total > 10) {
            const fkWR = fast.fallingKnives.winRate;
            const normalWR = fast.normal.winRate;
            
            if (fkWR < normalWR - 15) {
                insights.push({
                    type: 'critical',
                    message: `🚨 FALLING KNIVES: Fast entries without confirmation have ${fkWR.toFixed(0)}% WR vs ${normalWR.toFixed(0)}% normal. WAIT for absorption!`
                });
            }
        }
        
        // 2. Absorption impact
        if (absorptionResults.withAbsorption.count > 5 && absorptionResults.withoutAbsorption.count > 5) {
            const withWR = absorptionResults.withAbsorption.winRate;
            const withoutWR = absorptionResults.withoutAbsorption.winRate;
            
            if (withWR > withoutWR + 10) {
                insights.push({
                    type: 'success',
                    message: `✅ ABSORPTION: Waiting for confirmation increases WR from ${withoutWR.toFixed(0)}% to ${withWR.toFixed(0)}%`
                });
            } else if (withWR < withoutWR - 10) {
                insights.push({
                    type: 'warning',
                    message: `⚠️ ABSORPTION: Waiting reduces WR (${withWR.toFixed(0)}% vs ${withoutWR.toFixed(0)}%). Consider immediate entries.`
                });
            }
        }
        
        // 3. Velocity recommendation
        const slow = velocityResults.find(r => r.velocity === 'Slow');
        const medium = velocityResults.find(r => r.velocity === 'Medium');
        
        if (slow && medium && fast) {
            const bestVelocity = [slow, medium, fast].sort((a, b) => b.winRate - a.winRate)[0];
            
            insights.push({
                type: 'info',
                message: `💡 BEST VELOCITY: ${bestVelocity.velocity} entries perform best (${bestVelocity.winRate.toFixed(0)}% WR, ${formatCurrency(bestVelocity.avgPnL)} avg)`
            });
        }
        
        return insights;
    }
    
    renderDashboard(velocityResults, absorptionResults, insights) {
        const container = document.getElementById('velocity-analysis');
        
        let html = '<h3>⚡ Price Velocity Analysis</h3>';
        
        // Velocity breakdown
        html += '<h4>Performance by Entry Velocity</h4>';
        html += '<table class="velocity-table">';
        html += '<thead><tr><th>Velocity</th><th>Total</th><th>Win%</th><th>Avg PnL</th><th>Falling Knives</th><th>FK Win%</th><th>Normal Win%</th></tr></thead>';
        html += '<tbody>';
        
        velocityResults.forEach(r => {
            html += `
                <tr>
                    <td>${r.velocity}</td>
                    <td>${r.total}</td>
                    <td>${r.winRate.toFixed(1)}%</td>
                    <td class="${r.avgPnL >= 0 ? 'pnl-pos' : 'pnl-neg'}">${formatCurrency(r.avgPnL)}</td>
                    <td>${r.fallingKnives.count}</td>
                    <td>${r.fallingKnives.winRate.toFixed(1)}%</td>
                    <td>${r.normal.winRate.toFixed(1)}%</td>
                </tr>
            `;
        });
        
        html += '</tbody></table>';
        
        // Absorption comparison
        html += '<h4>Absorption Confirmation Impact</h4>';
        html += '<table class="absorption-table">';
        html += '<thead><tr><th>Type</th><th>Trades</th><th>Win%</th><th>Avg PnL</th></tr></thead>';
        html += '<tbody>';
        
        html += `
            <tr>
                <td>✅ With Absorption</td>
                <td>${absorptionResults.withAbsorption.count}</td>
                <td>${absorptionResults.withAbsorption.winRate.toFixed(1)}%</td>
                <td class="${absorptionResults.withAbsorption.avgPnL >= 0 ? 'pnl-pos' : 'pnl-neg'}">${formatCurrency(absorptionResults.withAbsorption.avgPnL)}</td>
            </tr>
            <tr>
                <td>❌ Without Absorption</td>
                <td>${absorptionResults.withoutAbsorption.count}</td>
                <td>${absorptionResults.withoutAbsorption.winRate.toFixed(1)}%</td>
                <td class="${absorptionResults.withoutAbsorption.avgPnL >= 0 ? 'pnl-pos' : 'pnl-neg'}">${formatCurrency(absorptionResults.withoutAbsorption.avgPnL)}</td>
            </tr>
        `;
        
        html += '</tbody></table>';
        
        // Insights
        html += '<div class="velocity-insights">';
        insights.forEach(insight => {
            html += `<div class="insight-card insight-${insight.type}">${insight.message}</div>`;
        });
        html += '</div>';
        
        container.innerHTML = html;
    }
}
```

---

## 📈 Ejemplo de Resultados

### Escenario: MNQ Backtesting

**By Velocity**:

| Velocity | Total | Win% | Avg PnL | Falling Knives | FK Win% | Normal Win% |
|----------|-------|------|---------|----------------|---------|-------------|
| Slow | 89 | 72% | $520 | 2 | 50% | 73% |
| Medium | 145 | 68% | $420 | 12 | 58% | 69% |
| Fast | 67 | 54% | $180 | 34 | 38% | 68% |

**By Absorption**:

| Type | Trades | Win% | Avg PnL |
|------|--------|------|---------|
| ✅ With Absorption | 78 | 71% | $480 |
| ❌ Without Absorption | 89 | 52% | $210 |

**Insights**:
> 🚨 **FALLING KNIVES**: Fast entries without confirmation = 38% WR vs 68% normal  
> **ACTION**: WAIT for absorption on fast velocity setups

> ✅ **ABSORPTION**: Waiting increases WR from 52% to 71%  
> **ACTION**: Keep absorption confirmation enabled

> 💡 **BEST**: Slow velocity entries perform best (72% WR)  
> **OBSERVATION**: Patient entries in stable conditions win more

---

## 🎯 Impacto

**Nueva Feature**: Price Velocity & Absorption Analysis  
**+8-10 horas** de implementación

### Breakdown:
- Velocity calculation & tracking: +3h
- Absorption detection logic: +3h
- Entry logic integration: +2h
- TradeAnalyzer dashboard: +2h

**Fase 2 actualizada**: 46-53h → **54-63h**

---

## 💡 Valor Estratégico

Esta feature resuelve el dilema del trading:

1. ✅ **Detecta "cuchillos cayendo"** antes de agarrarlos
2. ✅ **Valida momentum** con volumen/delta
3. ✅ **Optimiza timing** esperando absorción
4. ✅ **Aumenta win rate** evitando entradas prematuras

**Resultado**: Mejor timing de entrada, menos stop-outs por momentum contrario.
