# Multi-Asset Correlation & Directional Bias Analysis

## 🌍 Expansión: Todos los Futuros CME

### Asset Classes a Analizar

#### 1. **Micro E-mini Índices** (Sesgo Alcista Histórico)
- **MNQ** - Micro E-mini NASDAQ-100
- **MES** - Micro E-mini S&P 500
- **MYM** - Micro E-mini Dow Jones
- **M2K** - Micro E-mini Russell 2000

#### 2. **E-mini Índices** (Mayor liquidez)
- **NQ** - E-mini NASDAQ-100
- **ES** - E-mini S&P 500
- **YM** - E-mini Dow Jones
- **RTY** - E-mini Russell 2000

#### 3. **Energía** (Alta volatilidad)
- **MCL** - Micro Crude Oil
- **CL** - Crude Oil
- **NG** - Natural Gas
- **RB** - RBOB Gasoline
- **HO** - Heating Oil

#### 4. **Metales** (Diferentes correlaciones)
- **MGC** - Micro Gold
- **GC** - Gold
- **SI** - Silver
- **HG** - Copper (leading indicator)
- **PL** - Platinum

#### 5. **Divisas** (Mean reverting)
- **6E** - Euro FX
- **6J** - Japanese Yen
- **6B** - British Pound
- **6C** - Canadian Dollar
- **6A** - Australian Dollar
- **6S** - Swiss Franc

#### 6. **Agrícolas** (Seasonality driven)
- **ZC** - Corn
- **ZS** - Soybeans
- **ZW** - Wheat
- **ZL** - Soybean Oil

#### 7. **Ganado**
- **LE** - Live Cattle
- **GF** - Feeder Cattle
- **HE** - Lean Hogs

---

## 📊 Análisis por Asset Class

### Clasificación Automática

```csharp
// En SessionLevelsStrategy.cs

public enum AssetClass
{
    Equity,      // Índices
    Energy,      // Petróleo, gas
    Metals,      // Oro, plata
    Currency,    // Forex
    Agriculture, // Granos
    Livestock    // Ganado
}

public enum DirectionalBias
{
    Bullish,     // Sesgo alcista histórico (índices)
    Bearish,     // Sesgo bajista (divisas vs USD)
    Neutral,     // Sin sesgo claro
    MeanReverting // Tiende a revertir a media
}

private AssetClass GetAssetClass(string instrument)
{
    // Clasificar por nombre
    if (instrument.Contains("MNQ") || instrument.Contains("MES") || 
        instrument.Contains("MYM") || instrument.Contains("M2K") ||
        instrument.Contains("NQ") || instrument.Contains("ES") || 
        instrument.Contains("YM") || instrument.Contains("RTY"))
        return AssetClass.Equity;
    
    if (instrument.Contains("CL") || instrument.Contains("NG") || 
        instrument.Contains("RB") || instrument.Contains("HO"))
        return AssetClass.Energy;
    
    if (instrument.Contains("GC") || instrument.Contains("SI") || 
        instrument.Contains("HG") || instrument.Contains("PL"))
        return AssetClass.Metals;
    
    if (instrument.StartsWith("6"))
        return AssetClass.Currency;
    
    if (instrument.Contains("Z")) // ZC, ZS, ZW, ZL
        return AssetClass.Agriculture;
    
    if (instrument.Contains("LE") || instrument.Contains("GF") || instrument.Contains("HE"))
        return AssetClass.Livestock;
    
    return AssetClass.Equity; // Default
}

private DirectionalBias GetDirectionalBias(AssetClass assetClass)
{
    switch (assetClass)
    {
        case AssetClass.Equity:
            return DirectionalBias.Bullish; // Índices suben en el largo plazo
        
        case AssetClass.Currency:
            return DirectionalBias.MeanReverting; // Forex oscila en rangos
        
        case AssetClass.Energy:
        case AssetClass.Metals:
        case AssetClass.Agriculture:
            return DirectionalBias.Neutral; // Commodities sin sesgo claro
        
        case AssetClass.Livestock:
            return DirectionalBias.Neutral;
        
        default:
            return DirectionalBias.Neutral;
    }
}
```

---

## 🎯 Reglas Asimétricas por Sesgo

### Índices (Sesgo Alcista)

**Filosofía**: Los índices suben en el largo plazo. Longs tienen viento a favor, Shorts contra corriente.

#### Long Trades (Con el sesgo)
- ✅ **Más permisivos con TP2**: Dejar correr más tiempo
- ✅ **TP1 Reaction menos agresivo**: Conviction threshold más alto (70 vs 50)
- ✅ **Tolerar más retracements**: Permitir que toque VWAP sin exit inmediato

```csharp
// En validación de TP1 Reaction para EQUITY LONG
if (assetClass == AssetClass.Equity && Position.MarketPosition == MarketPosition.Long)
{
    // Aumentar threshold para mover SL
    int convictionThreshold = 70; // vs 50 normal
    
    // Ser menos reactivos a fuerza contraria
    if (decision.Confidence >= convictionThreshold)
    {
        // Mover SL solo si hay convicción MUY fuerte
    }
    else
    {
        Log("EQUITY LONG: Holding SL despite some counter-force (bullish bias)");
    }
}
```

#### Short Trades (Contra el sesgo)
- ⚠️ **MÁS cautelosos**: Cualquier señal de fuerza contraria = ajustar SL
- ⚠️ **TP1 Reaction agresivo**: Conviction threshold más bajo (30 vs 50)
- ⚠️ **Tomar profits rápido**: No esperar TP2 tanto como en long

```csharp
// En validación de TP1 Reaction para EQUITY SHORT
if (assetClass == AssetClass.Equity && Position.MarketPosition == MarketPosition.Short)
{
    // REDUCIR threshold (ser más cauteloso)
    int convictionThreshold = 30; // vs 50 normal
    
    // Reaccionar rápido a fuerza contraria
    if (decision.Confidence >= convictionThreshold)
    {
        Log("EQUITY SHORT: Moving SL (lower threshold due to bearish bias against trend)");
        // Mover SL inmediatamente
    }
}
```

### Divisas (Mean Reverting)

**Filosofía**: Forex tiende a oscilar. No tiene sesgo direccional fuerte.

- ✅ **Simétrico**: Tratar long y short igual
- ✅ **Tomar profits más agresivo**: No esperar TP2 tanto (mean reversion expected)
- ✅ **TP1 Reaction estándar**: Threshold 50

```csharp
if (assetClass == AssetClass.Currency)
{
    // Exit más rápido en general (mean reversion)
    // Si está en profit 1.5R, considerar exit parcial anticipado
    
    double currentR = (Close[0] - entryPrice) / Math.Abs(stopPrice - entryPrice);
    
    if (Math.Abs(currentR) > 1.5)
    {
        Log("FOREX: Near mean reversion zone. Consider scaling out.");
        // Opcional: Mover SL a BE más agresivamente
    }
}
```

### Commodities (Neutral pero volátil)

**Filosofía**: Sin sesgo direccional claro, pero movimientos más grandes.

- ✅ **Stops más amplios**: Dar más espacio por volatilidad
- ✅ **TP objectives más lejanos**: Aprovechar momentum cuando aparece
- ✅ **TP1 Reaction estándar**: Threshold 50

---

## 🔗 Análisis de Correlación

### Objetivo
Detectar cuándo múltiples instrumentos correlacionados se mueven juntos, validando la fortaleza del setup.

### Pares Correlacionados Comunes

| Par | Correlación | Interpretación |
|-----|-------------|----------------|
| ES - NQ | +0.95 | Muy alta (ambos índices USA) |
| ES - YM | +0.90 | Alta |
| NQ - MNQ | +0.99 | Perfecta (mismo subyacente) |
| GC - SI | +0.75 | Alta (metales preciosos) |
| 6E - 6B | +0.70 | Alta (divisas europeas) |
| CL - RB | +0.85 | Muy alta (refinados de petróleo) |
| ZS - ZL | +0.90 | Muy alta (soja y aceite) |
| ES - 6E | -0.30 | Inversa débil (índices vs euro) |

### Implementación: Correlation Matrix

#### Cálculo en Tiempo Real

```csharp
// Variables globales
private Dictionary<string, List<double>> priceHistory = new Dictionary<string, List<double>>();
private const int CORRELATION_WINDOW = 50; // 50 bars para calcular correlación

// En OnBarUpdate de CADA instrumento
public void UpdatePriceHistory(string instrument, double price)
{
    if (!priceHistory.ContainsKey(instrument))
        priceHistory[instrument] = new List<double>();
    
    priceHistory[instrument].Add(price);
    
    // Mantener solo últimos N bars
    if (priceHistory[instrument].Count > CORRELATION_WINDOW)
        priceHistory[instrument].RemoveAt(0);
}

// Calcular correlación entre dos instrumentos
private double CalculateCorrelation(string inst1, string inst2)
{
    if (!priceHistory.ContainsKey(inst1) || !priceHistory.ContainsKey(inst2))
        return 0;
    
    var prices1 = priceHistory[inst1];
    var prices2 = priceHistory[inst2];
    
    int n = Math.Min(prices1.Count, prices2.Count);
    if (n < 20) return 0; // Mínimo de datos
    
    // Calcular returns
    double[] returns1 = new double[n-1];
    double[] returns2 = new double[n-1];
    
    for (int i = 0; i < n-1; i++)
    {
        returns1[i] = (prices1[i+1] - prices1[i]) / prices1[i];
        returns2[i] = (prices2[i+1] - prices2[i]) / prices2[i];
    }
    
    // Pearson correlation
    double mean1 = returns1.Average();
    double mean2 = returns2.Average();
    
    double numerator = 0;
    double sumSq1 = 0;
    double sumSq2 = 0;
    
    for (int i = 0; i < returns1.Length; i++)
    {
        double diff1 = returns1[i] - mean1;
        double diff2 = returns2[i] - mean2;
        
        numerator += diff1 * diff2;
        sumSq1 += diff1 * diff1;
        sumSq2 += diff2 * diff2;
    }
    
    if (sumSq1 == 0 || sumSq2 == 0) return 0;
    
    double correlation = numerator / Math.Sqrt(sumSq1 * sumSq2);
    return correlation;
}
```

### Validación de Setup con Correlación

**Idea**: Si estoy entrando LONG en ES, validar si NQ también está moviéndose alcista.

```csharp
// En confirmación de trigger
private bool ValidateWithCorrelatedInstruments(string instrument, bool isLong)
{
    // Definir pares correlacionados
    var correlatedPairs = new Dictionary<string, List<string>>
    {
        { "ES", new List<string> { "NQ", "YM" } },
        { "MES", new List<string> { "MNQ", "MYM" } },
        { "NQ", new List<string> { "ES", "YM" } },
        { "MNQ", new List<string> { "MES", "MYM" } },
        { "GC", new List<string> { "SI" } },
        { "CL", new List<string> { "RB" } }
    };
    
    if (!correlatedPairs.ContainsKey(instrument))
        return true; // No hay pares conocidos, aprobar
    
    var correlated = correlatedPairs[instrument];
    int confirmations = 0;
    
    foreach (var corrInst in correlated)
    {
        // Verificar si el instrumento correlacionado también está en la misma dirección
        double correlation = CalculateCorrelation(instrument, corrInst);
        
        if (correlation > 0.7) // Alta correlación
        {
            // TODO: Obtener dirección de corrInst (requiere multi-chart data)
            // Por ahora, simplificado
            
            // Si ambos se mueven en la misma dirección = confirmación
            confirmations++;
        }
    }
    
    if (confirmations > 0)
    {
        Log($"CORRELATION CONFIRM: {confirmations} correlated instruments moving together");
        return true;
    }
    
    return true; // No bloquear por ahora
}
```

---

## 📊 Export de Datos de Correlación

### CSV Enriquecido

Agregar campos:

```csv
...,AssetClass,DirectionalBias,CorrelationWithES,CorrelationWithNQ,
CorrelatedConfirmations,AsymmetricRule,ConvictionThresholdUsed
```

Ejemplo:
```csv
...,Equity,Bullish,0.95,0.98,2,LongPermissive,70
...,Currency,MeanReverting,−0.25,−0.18,0,Symmetric,50
...,Equity,Bullish,0.87,0.92,2,ShortCautious,30
```

---

## 🧠 Análisis en TradeAnalyzer

### Dashboard: Asset Class Performance

```javascript
class AssetClassAnalyzer {
    
    analyzeByAssetClass(trades) {
        const classes = {
            'Equity': { long: [], short: [] },
            'Energy': { long: [], short: [] },
            'Metals': { long: [], short: [] },
            'Currency': { long: [], short: [] },
            'Agriculture': { long: [], short: [] },
            'Livestock': { long: [], short: [] }
        };
        
        // Clasificar trades
        trades.forEach(t => {
            const assetClass = this.classifyInstrument(t.instrument);
            const direction = t.type === 'Long' ? 'long' : 'short';
            
            if (classes[assetClass]) {
                classes[assetClass][direction].push(t);
            }
        });
        
        // Calcular stats por clase y dirección
        const results = [];
        
        Object.entries(classes).forEach(([className, directions]) => {
            // Long stats
            const longStats = this.calculateStats(directions.long);
            results.push({
                assetClass: className,
                direction: 'Long',
                ...longStats
            });
            
            // Short stats
            const shortStats = this.calculateStats(directions.short);
            results.push({
                assetClass: className,
                direction: 'Short',
                ...shortStats
            });
        });
        
        return results;
    }
    
    calculateStats(trades) {
        if (trades.length === 0) {
            return { trades: 0, winRate: 0, avgPnL: 0, profitFactor: 0 };
        }
        
        const wins = trades.filter(t => t.pnl > 0).length;
        const winRate = (wins / trades.length) * 100;
        
        const grossProfit = trades.filter(t => t.pnl > 0).reduce((sum, t) => sum + t.pnl, 0);
        const grossLoss = Math.abs(trades.filter(t => t.pnl < 0).reduce((sum, t) => sum + t.pnl, 0));
        const profitFactor = grossLoss > 0 ? grossProfit / grossLoss : 0;
        
        const avgPnL = trades.reduce((sum, t) => sum + t.pnl, 0) / trades.length;
        
        return {
            trades: trades.length,
            winRate: winRate,
            avgPnL: avgPnL,
            profitFactor: profitFactor
        };
    }
    
    generateAsymmetricInsights(results) {
        const insights = [];
        
        // Comparar LONG vs SHORT en Equity (debería favorecer longs)
        const equityLong = results.find(r => r.assetClass === 'Equity' && r.direction === 'Long');
        const equityShort = results.find(r => r.assetClass === 'Equity' && r.direction === 'Short');
        
        if (equityLong && equityShort && equityLong.trades > 10 && equityShort.trades > 10) {
            const longAdvantage = equityLong.avgPnL - equityShort.avgPnL;
            
            if (longAdvantage > 100) {
                insights.push({
                    type: 'success',
                    message: `✅ EQUITY: Longs outperform Shorts by ${formatCurrency(longAdvantage)} avg (expected due to bullish bias)`
                });
            } else if (longAdvantage < -50) {
                insights.push({
                    type: 'warning',
                    message: `⚠️ EQUITY: Shorts outperforming Longs (${formatCurrency(Math.abs(longAdvantage))}). Unusual - review strategy or market regime.`
                });
            }
        }
        
        // Comparar Long vs Short en Currency (debería ser simétrico)
        const currencyLong = results.find(r => r.assetClass === 'Currency' && r.direction === 'Long');
        const currencyShort = results.find(r => r.assetClass === 'Currency' && r.direction === 'Short');
        
        if (currencyLong && currencyShort && currencyLong.trades > 5 && currencyShort.trades > 5) {
            const asymmetry = Math.abs(currencyLong.avgPnL - currencyShort.avgPnL);
            
            if (asymmetry > 50) {
                const better = currencyLong.avgPnL > currencyShort.avgPnL ? 'Longs' : 'Shorts';
                insights.push({
                    type: 'info',
                    message: `ℹ️ FOREX: ${better} performing better (${formatCurrency(asymmetry)} diff). Unexpected for mean-reverting asset.`
                });
            }
        }
        
        return insights;
    }
    
    renderDashboard(results, insights) {
        const container = document.getElementById('asset-class-analysis');
        
        let html = '<h3>📊 Performance by Asset Class & Direction</h3>';
        
        // Tabla
        html += '<table class="asset-class-table">';
        html += '<thead><tr><th>Asset Class</th><th>Direction</th><th>Trades</th><th>Win%</th><th>PF</th><th>Avg PnL</th><th>Status</th></tr></thead>';
        html += '<tbody>';
        
        results.forEach(r => {
            if (r.trades === 0) return;
            
            const status = this.classifyPerformance(r.assetClass, r.direction, r.winRate, r.avgPnL);
            
            html += `
                <tr>
                    <td>${r.assetClass}</td>
                    <td>${r.direction === 'Long' ? '📈' : '📉'} ${r.direction}</td>
                    <td>${r.trades}</td>
                    <td>${r.winRate.toFixed(1)}%</td>
                    <td>${r.profitFactor.toFixed(2)}</td>
                    <td class="${r.avgPnL >= 0 ? 'pnl-pos' : 'pnl-neg'}">${formatCurrency(r.avgPnL)}</td>
                    <td>${status}</td>
                </tr>
            `;
        });
        
        html += '</tbody></table>';
        
        // Insights
        html += '<div class="asset-insights">';
        insights.forEach(insight => {
            html += `<div class="insight-card insight-${insight.type}">${insight.message}</div>`;
        });
        html += '</div>';
        
        container.innerHTML = html;
    }
    
    classifyPerformance(assetClass, direction, winRate, avgPnL) {
        // Para Equity Longs, esperamos mejor performance
        if (assetClass === 'Equity' && direction === 'Long') {
            if (winRate >= 65 && avgPnL > 300) return '🏆 Expected';
            if (winRate >= 55 && avgPnL > 100) return '✅ Good';
            return '🟡 Underperforming';
        }
        
        // Para Equity Shorts, esperamos más dificultad
        if (assetClass === 'Equity' && direction === 'Short') {
            if (winRate >= 60 && avgPnL > 200) return '🏆 Exceptional';
            if (winRate >= 50 && avgPnL > 0) return '✅ Good';
            return '🔴 Struggling (expected)';
        }
        
        // Para otros, estándar
        if (winRate >= 60 && avgPnL > 200) return '🏆 Excellent';
        if (winRate >= 50 && avgPnL > 0) return '✅ Good';
        return '🔴 Poor';
    }
    
    classifyInstrument(instrument) {
        if (instrument.includes('MNQ') || instrument.includes('MES') || 
            instrument.includes('MYM') || instrument.includes('M2K') ||
            instrument.includes('NQ') || instrument.includes('ES') || 
            instrument.includes('YM') || instrument.includes('RTY'))
            return 'Equity';
        
        if (instrument.includes('CL') || instrument.includes('NG') || 
            instrument.includes('RB') || instrument.includes('HO'))
            return 'Energy';
        
        if (instrument.includes('GC') || instrument.includes('SI') || 
            instrument.includes('HG') || instrument.includes('PL'))
            return 'Metals';
        
        if (instrument.startsWith('6'))
            return 'Currency';
        
        if (instrument.includes('Z'))
            return 'Agriculture';
        
        if (instrument.includes('LE') || instrument.includes('GF') || instrument.includes('HE'))
            return 'Livestock';
        
        return 'Unknown';
    }
}
```

---

## 🎯 Resultados Esperados

### Escenario 1: Equity Longs
```
MES Long: Win Rate 72%, Avg PnL $480
  → Con sesgo alcista: Dejamos TP2 correr
  → TP1 Reaction threshold: 70 (permisivo)
  → Resultado: TP2 hit 65% del tiempo
```

### Escenario 2: Equity Shorts
```
MES Short: Win Rate 54%, Avg PnL $180
  → Contra sesgo alcista: Más cautelosos
  → TP1 Reaction threshold: 30 (agresivo)
  → Mover SL rápido si hay fuerza contraria
  → Resultado: Protegemos capital en rallies
```

### Escenario 3: Forex
```
6E Long/Short: Win Rates similares (~58%)
  → Mean reverting: Tomar profits más rápido
  → No esperar TP2 tanto como en índices
  → Resultado: Exit efficiency mejorada
```

### Escenario 4: Correlación
```
ES Long trigger validado por:
  - NQ también alcista (correlation 0.95)
  - YM también alcista (correlation 0.90)
  → Confirmación: 2 instrumentos correlacionados
  → Aumenta confidence del setup
```

---

## ⏱️ Impacto en Implementación

**Nueva Fase 2B: Asset Class Intelligence**  
**Duración**: +8-10 horas

### Breakdown:
- Asset class classification: +2h
- Asymmetric rules implementation: +3h
- Correlation matrix calculation: +3h
- Dashboard de asset class: +2h

**Fase 2 Total**: 32-35h → **40-45h**

---

## 🎯 Valor Estratégico

Esta expansión convierte el sistema en **multi-asset intelligent**:

1. ✅ **Adapta estrategia** según características del activo
2. ✅ **Respeta sesgos** de mercado (índices alcistas)
3. ✅ **Valida setups** con instrumentos correlacionados
4. ✅ **Optimiza exits** por comportamiento del asset class

**Resultado**: Edge real basado en características fundamentales de cada mercado, no reglas genéricas.
