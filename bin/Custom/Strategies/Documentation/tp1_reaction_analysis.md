# TP1 Reaction Analysis - Protección Inteligente del Runner

## 🎯 Concepto

### El Problema
Cuando **TP1 se alcanza**, significa que:
1. El precio ha retrocedido al nivel opuesto (hay **fuerza contraria**)
2. El runner (posición restante hacia TP2) está **expuesto**
3. Si la reversión tiene **convicción real** (volumen + delta), el precio puede seguir contra nosotros

### La Solución
**Detectar fuerza contraria REAL** en TP1 y ajustar SL dinámicamente:
- Si hay convicción → Mover SL al VWAP de entrada (proteger runner)
- Si es solo noise → Dejar SL original (dar espacio a TP2)

---

## 📊 Métricas de Reacción a Capturar

### 1. **Volume Spike at TP1**
```
Volume at TP1 = Volumen del bar donde TP1 se llena
Avg Volume = Promedio de últimos 20 bars

Volume Ratio = Volume at TP1 / Avg Volume

Si Volume Ratio > 1.5: "High Volume" (fuerza real)
Si Volume Ratio < 1.0: "Normal/Low" (solo noise)
```

### 2. **Delta During Retracement**
```
Para Long con TP1 alcanzado (precio retrocedió hacia VWAP/nivel bajo):
  Delta Change = Cumulative Delta ahora - Cumulative Delta at TP1 fill
  
  Si Delta Change < -1000: Vendedores dominando (fuerza contraria REAL)
  Si Delta Change > -500: Solo ruido
  
Para Short con TP1 alcanzado:
  Si Delta Change > +1000: Compradores dominando (fuerza contraria REAL)
```

### 3. **Retracement Speed**
```
Bars to TP1 = # de bars desde entry hasta TP1 fill
Bars After TP1 = # de bars desde TP1 fill hasta ahora

Speed Ratio = Bars to TP1 / Bars After TP1

Si Speed Ratio > 2: Retroceso rápido (fuerza)
Si Speed Ratio < 1: Retroceso lento (débil)
```

### 4. **VWAP Rejection Test**
```
Para Long:
  ¿Precio está testeando el VWAP de entrada desde abajo?
  ¿Está siendo rechazado (formando lower highs)?
  
Para Short:
  ¿Precio está testeando el VWAP de entrada desde arriba?
  ¿Está siendo rechazado (formando higher lows)?
```

---

## 🧠 Lógica de Decisión

### Algoritmo de Detección

```javascript
class TP1ReactionDetector {
    
    /**
     * Determina si hay fuerza contraria REAL post-TP1
     * @returns {object} { hasConviction: bool, confidence: 0-100, signals: [] }
     */
    detectCounterForce(trade) {
        const signals = [];
        let convictionScore = 0;
        
        // Signal 1: High Volume
        if (trade.volumeRatioAtTP1 > 1.5) {
            signals.push('High Volume');
            convictionScore += 30;
        }
        
        // Signal 2: Delta Against
        const deltaThreshold = 1000; // Ajustar por instrumento
        
        if (trade.type === 'Long' && trade.deltaChangeAfterTP1 < -deltaThreshold) {
            signals.push('Strong Selling');
            convictionScore += 40;
        } else if (trade.type === 'Short' && trade.deltaChangeAfterTP1 > deltaThreshold) {
            signals.push('Strong Buying');
            convictionScore += 40;
        }
        
        // Signal 3: Fast Retracement
        if (trade.speedRatio > 2) {
            signals.push('Fast Reversal');
            convictionScore += 20;
        }
        
        // Signal 4: VWAP Rejection
        if (trade.vwapRejectionConfirmed) {
            signals.push('VWAP Rejection');
            convictionScore += 10;
        }
        
        return {
            hasConviction: convictionScore >= 50, // Threshold
            confidence: convictionScore,
            signals: signals,
            recommendation: convictionScore >= 50 
                ? 'MOVE SL to Entry VWAP' 
                : 'HOLD current SL'
        };
    }
}
```

### Matriz de Decisión

| Volume | Delta | Speed | VWAP Reject | Score | Acción |
|--------|-------|-------|-------------|-------|--------|
| ✅ High | ✅ Strong | ✅ Fast | ✅ Yes | 100 | 🔴 **MOVE SL NOW** |
| ✅ High | ✅ Strong | ❌ Normal | ❌ No | 70 | 🟡 **MOVE SL** |
| ✅ High | ❌ Weak | ✅ Fast | ❌ No | 50 | 🟡 **CONSIDER MOVE** |
| ❌ Normal | ✅ Strong | ❌ Normal | ❌ No | 40 | 🟢 **HOLD** |
| ❌ Normal | ❌ Weak | ❌ Normal | ❌ No | 0 | 🟢 **HOLD** |

---

## 💻 Implementación en NinjaTrader

### Variables de Tracking

```csharp
// Agregar después de línea 160 en SessionLevelsStrategy.cs

// ===================================================================
// TP1 REACTION ANALYSIS (v1.7.33)
// ===================================================================

// Estado del tracking
private bool isTrackingTP1Reaction = false;
private DateTime tp1FillTime;
private double tp1FillPrice;
private int tp1FillBar;

// Métricas pre-TP1
private double avgVolumePreTP1 = 0;
private double cumulativeDeltaAtTP1 = 0;

// Métricas post-TP1
private double volumeAtTP1 = 0;
private double volumeRatio = 0;
private double deltaChangeAfterTP1 = 0;
private int barsToTP1 = 0;
private int barsAfterTP1 = 0;
private double speedRatio = 0;
private bool vwapRejectionConfirmed = false;

// Decisión
private bool movedSLToVWAP = false;
private string reactionDecision = "None";
private int reactionConfidence = 0;
```

### Inicializar Tracking cuando TP1 se llena

```csharp
// En OnExecutionUpdate, cuando TP1 se llena

if (execution.Order.Name.StartsWith("TP1_") && execution.Order.OrderState == OrderState.Filled)
{
    Log(Time[0] + " TP1 Filled. Starting Reaction Analysis.");
    
    // Activar tracking
    isTrackingTP1Reaction = true;
    tp1FillTime = execution.Time;
    tp1FillPrice = execution.Price;
    tp1FillBar = CurrentBar;
    
    // Calcular volumen promedio pre-TP1
    double sumVolume = 0;
    int lookback = Math.Min(20, CurrentBar);
    for (int i = 1; i <= lookback; i++)
    {
        sumVolume += Volume[i];
    }
    avgVolumePreTP1 = sumVolume / lookback;
    
    // Capturar volume en el bar de TP1
    volumeAtTP1 = Volume[0];
    volumeRatio = avgVolumePreTP1 > 0 ? volumeAtTP1 / avgVolumePreTP1 : 0;
    
    // Capturar delta en momento de TP1
    if (cumulativeDelta != null)
    {
        cumulativeDeltaAtTP1 = cumulativeDelta.DeltaClose[0];
    }
    
    // Calcular bars to TP1
    barsToTP1 = CurrentBar - tp1FillBar; // Será 0 inicialmente, se actualiza después
    
    Log(string.Format("TP1_REACTION_INIT: Vol={0} AvgVol={1} Ratio={2:F2}", 
        volumeAtTP1, avgVolumePreTP1, volumeRatio));
}
```

### Monitorear Reacción en OnBarUpdate

```csharp
// En OnBarUpdate, después de tracking MAE/MFE

if (isTrackingTP1Reaction && Position.MarketPosition != MarketPosition.Flat)
{
    // Update bars after TP1
    barsAfterTP1 = CurrentBar - tp1FillBar;
    
    // Solo analizar si han pasado al menos 1 bar
    if (barsAfterTP1 < 1) return;
    
    // Calcular barsToTP1 correctamente (desde entry hasta TP1)
    // Necesitamos guardar el bar de entry también
    // Asumiendo que tenemos entryBar guardado
    barsToTP1 = tp1FillBar - entryBar;
    speedRatio = barsAfterTP1 > 0 ? (double)barsToTP1 / barsAfterTP1 : 0;
    
    // Calcular delta change
    if (cumulativeDelta != null)
    {
        double currentDelta = cumulativeDelta.DeltaClose[0];
        deltaChangeAfterTP1 = currentDelta - cumulativeDeltaAtTP1;
    }
    
    // Test VWAP rejection
    vwapRejectionConfirmed = TestVWAPRejection();
    
    // Ejecutar algoritmo de decisión
    var decision = AnalyzeTP1Reaction();
    
    // Si hay convicción Y no hemos movido el SL todavía
    if (decision.hasConviction && !movedSLToVWAP && stopOrder2 != null)
    {
        // MOVER SL2 al VWAP de entrada
        double newSLPrice = vwapAtEntry; // VWAP usado en la entrada
        
        Log(string.Format("TP1_REACTION: CONVICTION DETECTED ({0}%). Moving SL2 to Entry VWAP {1}",
            decision.confidence, newSLPrice));
        
        // Verificar que el nuevo SL es mejor que el actual
        bool shouldMove = false;
        
        if (Position.MarketPosition == MarketPosition.Long)
        {
            // Para Long, nuevo SL debe ser mayor que el actual
            if (newSLPrice > stopOrder2.StopPrice)
            {
                shouldMove = true;
            }
        }
        else if (Position.MarketPosition == MarketPosition.Short)
        {
            // Para Short, nuevo SL debe ser menor que el actual
            if (newSLPrice < stopOrder2.StopPrice)
            {
                shouldMove = true;
            }
        }
        
        if (shouldMove)
        {
            ChangeOrder(stopOrder2, stopOrder2.Quantity, 0, newSLPrice);
            movedSLToVWAP = true;
            reactionDecision = decision.recommendation;
            reactionConfidence = decision.confidence;
            
            Log(string.Format("TP1_REACTION: SL2 moved from {0} to {1} (VWAP protection)",
                stopOrder2.StopPrice, newSLPrice));
        }
        else
        {
            Log(string.Format("TP1_REACTION: New SL {0} is not better than current {1}. Skipping.",
                newSLPrice, stopOrder2.StopPrice));
        }
    }
    
    // Logs de debugging cada 5 bars
    if (EnableDebugLogs && barsAfterTP1 % 5 == 0)
    {
        Log(string.Format("TP1_REACTION_MONITOR: Bars={0} Speed={1:F2} DeltaΔ={2} Vol={3:F2} Reject={4}",
            barsAfterTP1, speedRatio, deltaChangeAfterTP1, volumeRatio, 
            vwapRejectionConfirmed ? "YES" : "NO"));
    }
}
```

### Helper: Test VWAP Rejection

```csharp
private bool TestVWAPRejection()
{
    // Necesitamos al menos 3 bars para confirmar rejection
    if (barsAfterTP1 < 3) return false;
    
    if (Position.MarketPosition == MarketPosition.Long)
    {
        // Long: Precio debería estar siendo rechazado en VWAP desde abajo
        // Buscar lower highs en los últimos 3 bars
        bool lowerHigh1 = High[1] < High[2];
        bool lowerHigh2 = High[0] < High[1];
        bool belowVWAP = Close[0] < vwapAtEntry;
        
        return lowerHigh1 && lowerHigh2 && belowVWAP;
    }
    else if (Position.MarketPosition == MarketPosition.Short)
    {
        // Short: Precio siendo rechazado en VWAP desde arriba
        // Buscar higher lows
        bool higherLow1 = Low[1] > Low[2];
        bool higherLow2 = Low[0] > Low[1];
        bool aboveVWAP = Close[0] > vwapAtEntry;
        
        return higherLow1 && higherLow2 && aboveVWAP;
    }
    
    return false;
}
```

### Helper: Analyze Reaction

```csharp
private ReactionDecision AnalyzeTP1Reaction()
{
    List<string> signals = new List<string>();
    int convictionScore = 0;
    
    // Signal 1: High Volume
    if (volumeRatio > 1.5)
    {
        signals.Add("High Volume");
        convictionScore += 30;
    }
    
    // Signal 2: Delta Against
    const double DELTA_THRESHOLD = 1000; // Ajustar por instrumento
    
    if (Position.MarketPosition == MarketPosition.Long && deltaChangeAfterTP1 < -DELTA_THRESHOLD)
    {
        signals.Add("Strong Selling");
        convictionScore += 40;
    }
    else if (Position.MarketPosition == MarketPosition.Short && deltaChangeAfterTP1 > DELTA_THRESHOLD)
    {
        signals.Add("Strong Buying");
        convictionScore += 40;
    }
    
    // Signal 3: Fast Retracement
    if (speedRatio > 2)
    {
        signals.Add("Fast Reversal");
        convictionScore += 20;
    }
    
    // Signal 4: VWAP Rejection
    if (vwapRejectionConfirmed)
    {
        signals.Add("VWAP Rejection");
        convictionScore += 10;
    }
    
    bool hasConviction = convictionScore >= 50;
    string recommendation = hasConviction ? "MOVE SL to Entry VWAP" : "HOLD current SL";
    
    return new ReactionDecision
    {
        HasConviction = hasConviction,
        Confidence = convictionScore,
        Signals = signals,
        Recommendation = recommendation
    };
}

// Clase helper
private class ReactionDecision
{
    public bool HasConviction { get; set; }
    public int Confidence { get; set; }
    public List<string> Signals { get; set; }
    public string Recommendation { get; set; }
}
```

### Reset en Exit

```csharp
// En OnExecutionUpdate, cuando posición se cierra

if (Position.MarketPosition == MarketPosition.Flat && isTrackingPosition)
{
    // ... código existente de MAE/MFE ...
    
    // Export con datos de TP1 Reaction
    ExportTP1ReactionData(execution);
    
    // Reset tracking
    isTrackingTP1Reaction = false;
    movedSLToVWAP = false;
    reactionDecision = "None";
    reactionConfidence = 0;
}
```

### Export a CSV

```csharp
private void ExportTP1ReactionData(Execution execution)
{
    // Agregar campos a CSV:
    string csvLine = string.Format(
        "{0},{1},...," + // Campos básicos
        "{2},{3},{4:F2},{5},{6:F2},{7},{8}," + // TP1 Reaction
        "...",
        
        // ... campos básicos ...
        
        // TP1 Reaction
        volumeAtTP1,
        volumeRatio,
        deltaChangeAfterTP1,
        speedRatio,
        vwapRejectionConfirmed ? "true" : "false",
        reactionDecision,
        reactionConfidence
    );
    
    File.AppendAllText(csvFilePath, csvLine + Environment.NewLine);
}
```

---

## 📊 Análisis Retrospectivo en TradeAnalyzer

### Objetivo
Responder: **¿Cuándo funcionó mover el SL vs dejarlo?**

### Código JavaScript

```javascript
// ===================================================================
// TP1 REACTION EFFECTIVENESS ANALYZER
// ===================================================================

class TP1ReactionAnalyzer {
    
    analyzeTP1Decisions(trades) {
        // Filtrar solo trades donde TP1 se alcanzó
        const tp1Trades = trades.filter(t => 
            t.reactionDecision && t.reactionDecision !== 'None'
        );
        
        if (tp1Trades.length === 0) {
            return { message: 'No TP1 reaction data available' };
        }
        
        // Agrupar por decisión
        const moved = tp1Trades.filter(t => t.reactionDecision.includes('MOVE'));
        const held = tp1Trades.filter(t => t.reactionDecision.includes('HOLD'));
        
        // Analizar outcomes
        const movedResults = this.analyzeOutcome(moved);
        const heldResults = this.analyzeOutcome(held);
        
        return {
            moved: movedResults,
            held: heldResults,
            totalTP1Trades: tp1Trades.length,
            insights: this.generateTP1Insights(movedResults, heldResults)
        };
    }
    
    analyzeOutcome(trades) {
        if (trades.length === 0) return null;
        
        // Clasificar outcomes
        const tp2Hit = trades.filter(t => t.result.includes('TP2'));
        const slHit = trades.filter(t => t.result.includes('SL'));
        const other = trades.filter(t => !t.result.includes('TP2') && !t.result.includes('SL'));
        
        const avgPnL = trades.reduce((sum, t) => sum + t.pnl, 0) / trades.length;
        const winRate = (trades.filter(t => t.pnl > 0).length / trades.length) * 100;
        
        return {
            total: trades.length,
            tp2Count: tp2Hit.length,
            slCount: slHit.length,
            otherCount: other.length,
            avgPnL: avgPnL,
            winRate: winRate,
            tp2Rate: (tp2Hit.length / trades.length) * 100,
            slRate: (slHit.length / trades.length) * 100
        };
    }
    
    generateTP1Insights(moved, held) {
        const insights = [];
        
        if (!moved || !held) return insights;
        
        // Comparar TP2 hit rates
        if (moved.tp2Rate > held.tp2Rate + 10) {
            insights.push({
                type: 'success',
                message: `✅ Moving SL increased TP2 hits: ${moved.tp2Rate.toFixed(0)}% vs ${held.tp2Rate.toFixed(0)}% when held`
            });
        } else if (held.tp2Rate > moved.tp2Rate + 10) {
            insights.push({
                type: 'warning',
                message: `⚠️ Holding SL gave more TP2 hits: ${held.tp2Rate.toFixed(0)}% vs ${moved.tp2Rate.toFixed(0)}% when moved. Consider higher conviction threshold.`
            });
        }
        
        // Comparar SL hit rates
        if (moved.slRate < held.slRate - 10) {
            insights.push({
                type: 'success',
                message: `✅ Moving SL reduced SL hits: ${moved.slRate.toFixed(0)}% vs ${held.slRate.toFixed(0)}% when held`
            });
        }
        
        // Comparar PnL
        const pnlDiff = moved.avgPnL - held.avgPnL;
        if (Math.abs(pnlDiff) > 50) {
            const better = pnlDiff > 0 ? 'Moving' : 'Holding';
            insights.push({
                type: pnlDiff > 0 ? 'success' : 'warning',
                message: `${better} SL performed better: ${formatCurrency(Math.abs(pnlDiff))} difference in avg PnL`
            });
        }
        
        // Recomendación final
        const moveScore = (moved.tp2Rate - held.tp2Rate) + ((moved.avgPnL - held.avgPnL) / 100);
        
        if (moveScore > 10) {
            insights.push({
                type: 'recommendation',
                message: `💡 RECOMMENDATION: Continue using TP1 reaction logic. It's working.`
            });
        } else if (moveScore < -10) {
            insights.push({
                type: 'recommendation',
                message: `💡 RECOMMENDATION: Increase conviction threshold or disable TP1 reaction logic.`
            });
        } else {
            insights.push({
                type: 'info',
                message: `ℹ️ TP1 reaction logic is neutral. Results are similar either way.`
            });
        }
        
        return insights;
    }
    
    renderDashboard(analysis) {
        const container = document.getElementById('tp1-reaction-analysis');
        
        let html = '<h3>🎯 TP1 Reaction Analysis</h3>';
        
        if (analysis.message) {
            html += `<p>${analysis.message}</p>`;
        } else {
            // Tabla comparativa
            html += '<table class="tp1-comparison-table">';
            html += '<thead><tr><th>Decision</th><th>Trades</th><th>TP2 Hit</th><th>SL Hit</th><th>Avg PnL</th><th>Win Rate</th></tr></thead>';
            html += '<tbody>';
            
            if (analysis.moved) {
                html += `
                    <tr>
                        <td>🔴 Moved SL</td>
                        <td>${analysis.moved.total}</td>
                        <td>${analysis.moved.tp2Rate.toFixed(1)}%</td>
                        <td>${analysis.moved.slRate.toFixed(1)}%</td>
                        <td>${formatCurrency(analysis.moved.avgPnL)}</td>
                        <td>${analysis.moved.winRate.toFixed(1)}%</td>
                    </tr>
                `;
            }
            
            if (analysis.held) {
                html += `
                    <tr>
                        <td>🟢 Held SL</td>
                        <td>${analysis.held.total}</td>
                        <td>${analysis.held.tp2Rate.toFixed(1)}%</td>
                        <td>${analysis.held.slRate.toFixed(1)}%</td>
                        <td>${formatCurrency(analysis.held.avgPnL)}</td>
                        <td>${analysis.held.winRate.toFixed(1)}%</td>
                    </tr>
                `;
            }
            
            html += '</tbody></table>';
            
            // Insights
            html += '<div class="tp1-insights">';
            analysis.insights.forEach(insight => {
                html += `<div class="insight-card insight-${insight.type}">${insight.message}</div>`;
            });
            html += '</div>';
        }
        
        container.innerHTML = html;
    }
}

// Usar en dashboard
const tp1Analyzer = new TP1ReactionAnalyzer();
const tp1Analysis = tp1Analyzer.analyzeTP1Decisions(globalAllTrades);
tp1Analyzer.renderDashboard(tp1Analysis);
```

---

## 📊 Formato CSV Actualizado

Agregar campos:

```csv
...,VolumeAtTP1,VolumeRatio,DeltaChangeAfterTP1,SpeedRatio,VWAPRejection,
ReactionDecision,ReactionConfidence,MovedSLToVWAP
```

Ejemplo:
```csv
...,2580,1.85,−1450,2.3,true,MOVE SL to Entry VWAP,70,true
```

---

## 🎯 Resultados Esperados

### Escenario 1: Convicción Detectada Correctamente
```
TP1 fills → High volume + Strong selling → SL moved to VWAP
Resultado: TP2 no se alcanza, pero runner protegido en VWAP
PnL: Preservado vs pérdida si SL original se hubiera tocado
```

### Escenario 2: False Positive
```
TP1 fills → Signal débil → Algoritmo dice HOLD
Resultado: TP2 se alcanza normalmente
PnL: Maximizado
```

### Análisis Retroactivo
```
Moved SL: 67 trades
  - TP2 Hit: 23% (normal dado que hubo reversión)
  - SL Hit at VWAP: 45% (protegido)
  - Other: 32%
  - Avg PnL: $280

Held SL: 145 trades
  - TP2 Hit: 58%
  - SL Hit original: 28%
  - Other: 14%
  - Avg PnL: $420

INSIGHT:
→ Algoritmo está siendo conservador
→ Muchos trades que moveríamos SL terminan alcanzando TP2
→ RECOMENDACIÓN: Aumentar conviction threshold de 50 a 70
```

---

## 🎛️ Parámetros Ajustables

```csharp
// Agregar a properties de la estrategia

[Display(Name="TP1 Reaction Enabled", GroupName="5. Advanced", Order=1)]
public bool EnableTP1Reaction { get; set; } = true;

[Display(Name="Volume Ratio Threshold", GroupName="5. Advanced", Order=2)]
public double VolumeRatioThreshold { get; set; } = 1.5;

[Display(Name="Delta Threshold", GroupName="5. Advanced", Order=3)]
public double DeltaThreshold { get; set; } = 1000;

[Display(Name="Conviction Score Threshold", GroupName="5. Advanced", Order=4)]
public int ConvictionThreshold { get; set; } = 50; // 0-100
```

---

## ⏱️ Impacto en Tiempo

**Fase 2 actualizada**: 25-28h → **32-35h** (+7h para TP1 reaction)

### Breakdown adicional:
- Implementación tracking: +3h
- Testing con diferentes thresholds: +2h
- Análisis retroactivo en TradeAnalyzer: +2h

---

## 🎯 Valor Agregado

Esta funcionalidad convierte la estrategia en **adaptativa**:
- No es estático "siempre dejar TP2"
- Ni siempre "mover a BE después de TP1"
- Es **inteligente**: decide basándose en **evidencia de mercado**

**Resultado**: Maximizar TP2 cuando es viable, proteger cuando no lo es.
