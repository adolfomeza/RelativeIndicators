# Order Flow & Microstructure Analysis - Implementación

## 📊 Features Críticos Solicitados

### 1. **Efectividad de Niveles por Sesión Origen**

#### Pregunta:
> ¿Qué niveles son más efectivos? ¿USA toma liquidez de Asia o Europa? ¿De hoy? ¿Ayer? ¿Hace 4 días?

#### Análisis a Implementar:

**A) Session Cross-Reference Matrix**

| Setup Session | Target Session | Age | Trades | Win Rate | Avg PnL | Insight |
|---------------|----------------|-----|--------|----------|---------|---------|
| USA | Asia (same day) | 0d | 45 | 72% | $520 | 🏆 BEST |
| USA | Europe (same day) | 0d | 23 | 65% | $380 | ✅ Good |
| USA | Asia (yesterday) | 1d | 67 | 58% | $210 | 🟡 OK |
| USA | Asia (4 days ago) | 4d | 12 | 42% | -$85 | 🔴 POOR |
| Europe | Asia (same day) | 0d | 34 | 68% | $450 | ✅ Good |

**Insight Automático**:
> 💡 **DISCOVERY**: USA tomando liquidez de Asia mismo día tiene 72% WR vs 42% cuando el nivel tiene 4+ días.  
> **RECOMMENDATION**: Priorizar niveles de Asia/Europe del mismo día. Filtrar niveles >3 días.

---

### 2. **Tiempo Debajo/Encima del VWAP de Entrada**

#### Pregunta:
> ¿Cuánto tiempo pasa el precio debajo del VWAP usado para entrada? Si está debajo, el setup no está funcionando.

#### Métrica: **VWAP Support/Resistance Strength**

**Para Longs**:
- VWAP debería actuar como soporte
- Si precio pasa mucho tiempo debajo → weak setup

**Para Shorts**:
- VWAP debería actuar como resistencia
- Si precio pasa mucho tiempo encima → weak setup

#### Cálculo:

```
Time Against VWAP (%) = (Bars Against VWAP / Total Bars in Trade) * 100

Para Long:
  Bars Against = # de bars donde Close[i] < VWAP Entry

Para Short:
  Bars Against = # de bars donde Close[i] > VWAP Entry
```

**Análisis Comparativo**:

| Time Against VWAP | Trades | Win Rate | Avg PnL | Status |
|-------------------|--------|----------|---------|--------|
| 0-20% | 89 | 75% | $560 | 🏆 Strong |
| 21-40% | 145 | 64% | $320 | ✅ Good |
| 41-60% | 67 | 52% | $120 | 🟡 Weak |
| 61-100% | 23 | 35% | -$180 | 🔴 Failed |

**Insight Automático**:
> ⚠️ **WARNING**: Trades con >40% tiempo contra VWAP tienen solo 45% WR vs 75% cuando VWAP sostiene.  
> **RECOMMENDATION**: Considerar exit early si precio rompe y cierra contra VWAP por 3+ bars consecutivos.

---

### 3. **Delta a Favor del Trade**

#### Pregunta:
> ¿El delta acumulado está a favor del trade? El precio se mueve por órdenes a mercado.

#### Métricas Order Flow:

**A) Cumulative Delta at Entry**
- Snapshot del delta acumulado en el momento de entrada
- Indica si hay presión compradora (positivo) o vendedora (negativo)

**B) Cumulative Delta Change Durante Trade**
```
Delta Change = Cumulative Delta at Exit - Cumulative Delta at Entry

Para Long:
  Delta Change > 0 = Good (compradores activos)
  Delta Change < 0 = Bad (vendedores dominando)

Para Short:
  Delta Change < 0 = Good (vendedores activos)
  Delta Change > 0 = Bad (compradores dominando)
```

**C) Delta Bias Classification**
```javascript
function calculateDeltaBias(trade) {
    const deltaChange = trade.cumulativeDeltaExit - trade.cumulativeDeltaEntry;
    
    if (trade.type === 'Long') {
        if (deltaChange > 1000) return 'Strong Bullish';
        if (deltaChange > 0) return 'Bullish';
        if (deltaChange > -1000) return 'Neutral/Weak';
        return 'Bearish (Against)';
    } else {
        if (deltaChange < -1000) return 'Strong Bearish';
        if (deltaChange < 0) return 'Bearish';
        if (deltaChange < 1000) return 'Neutral/Weak';
        return 'Bullish (Against)';
    }
}
```

**Análisis Comparativo**:

| Delta Bias | Trades | Win Rate | Avg PnL | Conviction |
|------------|--------|----------|---------|------------|
| Strong Bullish (Long) | 45 | 82% | $680 | 🏆🏆🏆 |
| Bullish (Long) | 98 | 71% | $420 | 🏆 |
| Neutral (Long) | 67 | 58% | $180 | 🟡 |
| Against (Long) | 23 | 38% | -$210 | 🔴 |

**Insight Automático**:
> 🚨 **CRITICAL**: Longs con delta a favor tienen 82% WR vs 38% cuando delta va contra.  
> **RECOMMENDATION**: Agregar filtro de delta mínimo. Requiere delta bullish para confirmar long entry.

---

## 🏗️ Implementación en NinjaTrader

### Código C# - SessionLevelsStrategy.cs

#### 1. Variables de Tracking

```csharp
// Agregar después de línea 160

// ===================================================================
// ORDER FLOW & MICROSTRUCTURE TRACKING (v1.7.32)
// ===================================================================

// Session Relationship
private string levelSourceSession = "";      // Asia/Europe/USA que generó el nivel
private string levelTargetSession = "";      // Asia/Europe/USA opuesta (target)
private string sessionCrossRef = "";         // "USA takes Asia", etc.
private int levelSourceAgeDays = 0;          // Edad del nivel fuente

// VWAP Context
private string vwapAnchorSession = "";       // Sesión del VWAP anchor
private int vwapAnchorAgeDays = 0;           // Edad del VWAP anchor
private int barsAgainstVWAP = 0;             // Contador de bars contra VWAP
private int totalBarsInTrade = 0;            // Total bars en trade
private double vwapAtEntry = 0;              // VWAP usado en entrada

// Order Flow (si disponible)
private double cumulativeDeltaAtEntry = 0;
private double cumulativeDeltaAtExit = 0;
private string deltaBias = "Unknown";

// NinjaTrader tiene OrderFlowCumulativeDelta indicator
private OrderFlowCumulativeDelta cumulativeDelta;
```

#### 2. Inicialización

```csharp
// En OnStateChange, State.DataLoaded

if (State == State.DataLoaded)
{
    // Inicializar Cumulative Delta indicator (si tienes Order Flow license)
    try
    {
        cumulativeDelta = OrderFlowCumulativeDelta(CumulativeDeltaType.BidAsk, 
                                                    CumulativeDeltaPeriod.Session, 
                                                    0);
        AddChartIndicator(cumulativeDelta);
        Log("Order Flow Cumulative Delta initialized.");
    }
    catch
    {
        Print("Warning: Order Flow not available. Delta metrics will be disabled.");
    }
    
    // ... resto de inicialización
}
```

#### 3. Captura en Entry

```csharp
// En ManageEntryA_Plus, cuando se confirma el trigger SHORT

if (confirmation && setupAnchorPrice > 0 && currentEntryState == EntryState.Idle)
{
    // ... código existente de validación ...
    
    // === NUEVO: Capturar contexto de sesión ===
    
    // Determinar sesión del nivel fuente (el nivel que tocamos)
    levelSourceSession = DetermineSessionFromTime(setupLevelTime);
    
    // Sesión actual (donde estamos entrando)
    string currentSession = DetermineSessionFromTime(Time[0]);
    
    // Sesión del target (nivel opuesto)
    if (validatedTargetPrice > 0 && cachedOppositeLevel != null)
    {
        levelTargetSession = DetermineSessionFromTime(cachedOppositeLevel.StartTime);
    }
    
    // Generar cross-reference string
    sessionCrossRef = $"{currentSession} takes {levelSourceSession}";
    
    // Calcular edad del nivel fuente
    TimeSpan levelAge = Time[0] - setupLevelTime;
    levelSourceAgeDays = (int)levelAge.TotalDays;
    
    // Capturar VWAP anchor context
    vwapAtEntry = setupVWAP;
    vwapAnchorSession = DetermineSessionFromVWAP(setupVWAP, setupLevelTime);
    TimeSpan vwapAge = Time[0] - setupLevelTime;
    vwapAnchorAgeDays = (int)vwapAge.TotalDays;
    
    // Resetear contadores
    barsAgainstVWAP = 0;
    totalBarsInTrade = 0;
    
    // Capturar Cumulative Delta at Entry
    if (cumulativeDelta != null)
    {
        cumulativeDeltaAtEntry = cumulativeDelta.DeltaClose[0];
    }
    
    // ... resto del código de entrada
}
```

#### 4. Tracking Durante Trade

```csharp
// En OnBarUpdate, mientras posición activa

if (isTrackingPosition && Position.MarketPosition != MarketPosition.Flat)
{
    // Update MAE/MFE (ya existente)
    double unrealizedPnL = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
    if (unrealizedPnL < currentMAE) currentMAE = unrealizedPnL;
    if (unrealizedPnL > currentMFE) currentMFE = unrealizedPnL;
    
    // === NUEVO: Track VWAP Support/Resistance ===
    totalBarsInTrade++;
    
    // Para Long: check si está debajo del VWAP
    if (Position.MarketPosition == MarketPosition.Long)
    {
        if (Close[0] < vwapAtEntry)
        {
            barsAgainstVWAP++;
        }
    }
    // Para Short: check si está encima del VWAP
    else if (Position.MarketPosition == MarketPosition.Short)
    {
        if (Close[0] > vwapAtEntry)
        {
            barsAgainstVWAP++;
        }
    }
    
    // === NUEVO: Update Cumulative Delta ===
    if (cumulativeDelta != null)
    {
        // Solo actualizar si el valor cambió (evitar logging excesivo)
        double currentDelta = cumulativeDelta.DeltaClose[0];
        // Store para export al cerrar
    }
}
```

#### 5. Cálculo en Exit

```csharp
// En OnExecutionUpdate, cuando se cierra posición

if (Position.MarketPosition == MarketPosition.Flat && isTrackingPosition)
{
    // Capturar Cumulative Delta at Exit
    if (cumulativeDelta != null)
    {
        cumulativeDeltaAtExit = cumulativeDelta.DeltaClose[0];
        
        // Calcular Delta Bias
        double deltaChange = cumulativeDeltaAtExit - cumulativeDeltaAtEntry;
        deltaBias = CalculateDeltaBias(execution.Order.IsLong, deltaChange);
    }
    
    // Calcular % tiempo contra VWAP
    double timeAgainstVWAPPct = totalBarsInTrade > 0 
        ? (barsAgainstVWAP / (double)totalBarsInTrade) * 100 
        : 0;
    
    // Log para debugging
    Log(string.Format("VWAP_STRENGTH: {0}% against VWAP ({1}/{2} bars)", 
        timeAgainstVWAPPct.toFixed(1), barsAgainstVWAP, totalBarsInTrade));
    
    Log(string.Format("DELTA_BIAS: {0} (Entry: {1}, Exit: {2}, Change: {3})", 
        deltaBias, cumulativeDeltaAtEntry, cumulativeDeltaAtExit, 
        cumulativeDeltaAtExit - cumulativeDeltaAtEntry));
    
    // Export a CSV (agregar a ExportEnrichedTradeToCSV)
    ExportMicrostructureData(execution, timeAgainstVWAPPct);
}
```

#### 6. Helper Methods

```csharp
/// <summary>
/// Determina la sesión basándose en el timestamp
/// </summary>
private string DetermineSessionFromTime(DateTime time)
{
    TimeSpan ts = time.TimeOfDay;
    
    // Asia: 00:00 - 09:00 EST
    if (ts >= new TimeSpan(0, 0, 0) && ts < new TimeSpan(9, 0, 0))
        return "Asia";
    
    // Europe: 03:00 - 11:30 EST
    if (ts >= new TimeSpan(3, 0, 0) && ts < new TimeSpan(11, 30, 0))
        return "Europe";
    
    // USA: 09:30 - 16:00 EST
    if (ts >= new TimeSpan(9, 30, 0) && ts < new TimeSpan(16, 0, 0))
        return "USA";
    
    return "Extended";
}

/// <summary>
/// Determina sesión del VWAP anchor
/// </summary>
private string DetermineSessionFromVWAP(double vwapValue, DateTime anchorTime)
{
    // Si es VWAP ad-hoc, usar la sesión del anchor time
    if (anchorTime != DateTime.MinValue)
    {
        return DetermineSessionFromTime(anchorTime);
    }
    
    // Si es VWAP global, marcar como "Global"
    return "Global";
}

/// <summary>
/// Calcula Delta Bias basado en cambio de cumulative delta
/// </summary>
private string CalculateDeltaBias(bool isLong, double deltaChange)
{
    const double STRONG_THRESHOLD = 1000; // Ajustar según instrumento
    
    if (isLong)
    {
        if (deltaChange > STRONG_THRESHOLD) return "Strong Bullish";
        if (deltaChange > 0) return "Bullish";
        if (deltaChange > -STRONG_THRESHOLD) return "Neutral";
        return "Bearish (Against)";
    }
    else
    {
        if (deltaChange < -STRONG_THRESHOLD) return "Strong Bearish";
        if (deltaChange < 0) return "Bearish";
        if (deltaChange < STRONG_THRESHOLD) return "Neutral";
        return "Bullish (Against)";
    }
}

/// <summary>
/// Export con datos de microestructura
/// </summary>
private void ExportMicrostructureData(Execution execution, double timeAgainstVWAPPct)
{
    // ... código existente de CSV export ...
    
    // Agregar campos adicionales:
    string csvLine = string.Format(
        "{0},{1},...," + // Campos básicos existentes
        "{2},{3},{4},{5}," + // Session context
        "{6},{7},{8:F1}," + // VWAP context
        "{9},{10},{11}", // Order Flow
        
        // ... campos básicos ...
        
        // Session Context
        levelSourceSession, levelTargetSession, sessionCrossRef, levelSourceAgeDays,
        
        // VWAP Context
        vwapAnchorSession, vwapAnchorAgeDays, timeAgainstVWAPPct,
        
        // Order Flow
        cumulativeDeltaAtEntry, cumulativeDeltaAtExit, deltaBias
    );
    
    File.AppendAllText(csvFilePath, csvLine + Environment.NewLine);
}
```

---

## 📊 Análisis en TradeAnalyzer

### JavaScript - Análisis de Efectividad de Niveles

```javascript
// ===================================================================
// SESSION EFFECTIVENESS ANALYZER
// ===================================================================

class SessionEffectivenessAnalyzer {
    
    analyzeSessionCrossReferences(trades) {
        const matrix = {};
        
        trades.forEach(t => {
            const key = `${t.sessionCrossRef}_${t.levelSourceAgeDays}d`;
            
            if (!matrix[key]) {
                matrix[key] = {
                    crossRef: t.sessionCrossRef,
                    age: t.levelSourceAgeDays,
                    trades: [],
                    wins: 0,
                    pnl: 0
                };
            }
            
            matrix[key].trades.push(t);
            matrix[key].pnl += t.pnl;
            if (t.pnl > 0) matrix[key].wins++;
        });
        
        // Calcular stats
        const results = [];
        Object.values(matrix).forEach(group => {
            if (group.trades.length < 5) return; // Mínimo para significancia
            
            const winRate = (group.wins / group.trades.length) * 100;
            const avgPnL = group.pnl / group.trades.length;
            
            results.push({
                crossRef: group.crossRef,
                age: group.age,
                trades: group.trades.length,
                winRate: winRate,
                avgPnL: avgPnL,
                totalPnL: group.pnl,
                status: this.classifyEffectiveness(winRate, avgPnL)
            });
        });
        
        // Ordenar por win rate
        results.sort((a, b) => b.winRate - a.winRate);
        
        return results;
    }
    
    classifyEffectiveness(winRate, avgPnL) {
        if (winRate >= 70 && avgPnL > 400) return 'BEST';
        if (winRate >= 60 && avgPnL > 200) return 'GOOD';
        if (winRate >= 50 && avgPnL > 0) return 'OK';
        return 'POOR';
    }
    
    generateSessionInsights(results) {
        const insights = [];
        
        // Best performer
        const best = results[0];
        if (best) {
            insights.push({
                type: 'success',
                message: `🏆 BEST: ${best.crossRef} (age ${best.age}d) - Win Rate ${best.winRate.toFixed(1)}%, Avg ${formatCurrency(best.avgPnL)}`
            });
        }
        
        // Compare same cross-ref but different ages
        const crossRefs = {};
        results.forEach(r => {
            if (!crossRefs[r.crossRef]) crossRefs[r.crossRef] = [];
            crossRefs[r.crossRef].push(r);
        });
        
        Object.entries(crossRefs).forEach(([ref, groups]) => {
            if (groups.length < 2) return;
            
            // Ordenar por edad
            groups.sort((a, b) => a.age - b.age);
            
            const fresh = groups[0];
            const stale = groups[groups.length - 1];
            
            const degradation = ((fresh.winRate - stale.winRate) / fresh.winRate) * 100;
            
            if (degradation > 30) {
                insights.push({
                    type: 'warning',
                    message: `⚠️ ${ref}: Fresh levels (${fresh.age}d) @${fresh.winRate.toFixed(0)}% vs Stale (${stale.age}d) @${stale.winRate.toFixed(0)}% = ${degradation.toFixed(0)}% degradation`
                });
            }
        });
        
        return insights;
    }
}

// ===================================================================
// VWAP STRENGTH ANALYZER
// ===================================================================

class VWAPStrengthAnalyzer {
    
    analyzeVWAPSupport(trades) {
        const brackets = [
            { min: 0, max: 20, label: '0-20%' },
            { min: 21, max: 40, label: '21-40%' },
            { min: 41, max: 60, label: '41-60%' },
            { min: 61, max: 100, label: '61-100%' }
        ];
        
        const results = brackets.map(bracket => {
            const filtered = trades.filter(t => {
                const pct = t.timeAgainstVWAPPct || 0;
                return pct >= bracket.min && pct <= bracket.max;
            });
            
            if (filtered.length === 0) return null;
            
            const wins = filtered.filter(t => t.pnl > 0).length;
            const winRate = (wins / filtered.length) * 100;
            const avgPnL = filtered.reduce((sum, t) => sum + t.pnl, 0) / filtered.length;
            
            return {
                bracket: bracket.label,
                trades: filtered.length,
                winRate: winRate,
                avgPnL: avgPnL,
                status: this.classifyStrength(winRate)
            };
        }).filter(r => r !== null);
        
        return results;
    }
    
    classifyStrength(winRate) {
        if (winRate >= 70) return 'Strong 🏆';
        if (winRate >= 60) return 'Good ✅';
        if (winRate >= 50) return 'Weak 🟡';
        return 'Failed 🔴';
    }
    
    generateVWAPInsights(results) {
        const insights = [];
        
        const strong = results.find(r => r.bracket === '0-20%');
        const failed = results.find(r => r.bracket === '61-100%');
        
        if (strong && failed) {
            const spread = strong.winRate - failed.winRate;
            
            if (spread > 30) {
                insights.push({
                    type: 'critical',
                    message: `🚨 VWAP is critical support: ${strong.winRate.toFixed(0)}% WR when holding vs ${failed.winRate.toFixed(0)}% when broken (${spread.toFixed(0)}% difference)`
                });
                
                insights.push({
                    type: 'recommendation',
                    message: `💡 RECOMMENDATION: Exit trades if price closes against VWAP for 3+ consecutive bars`
                });
            }
        }
        
        return insights;
    }
}

// ===================================================================
// DELTA BIAS ANALYZER
// ===================================================================

class DeltaBiasAnalyzer {
    
    analyzeDeltaImpact(trades) {
        const groups = {
            'Strong Bullish': [],
            'Bullish': [],
            'Neutral': [],
            'Bearish': [],
            'Strong Bearish': [],
            'Bullish (Against)': [],
            'Bearish (Against)': []
        };
        
        trades.forEach(t => {
            const bias = t.deltaBias || 'Unknown';
            if (groups[bias]) {
                groups[bias].push(t);
            }
        });
        
        const results = [];
        Object.entries(groups).forEach(([bias, trades]) => {
            if (trades.length === 0) return;
            
            const wins = trades.filter(t => t.pnl > 0).length;
            const winRate = (wins / trades.length) * 100;
            const avgPnL = trades.reduce((sum, t) => sum + t.pnl, 0) / trades.length;
            
            results.push({
                bias: bias,
                trades: trades.length,
                winRate: winRate,
                avgPnL: avgPnL,
                conviction: this.classifyConviction(bias, winRate)
            });
        });
        
        return results;
    }
    
    classifyConviction(bias, winRate) {
        if (bias.includes('Strong') && !bias.includes('Against') && winRate >= 75) {
            return '🏆🏆🏆';
        }
        if (!bias.includes('Against') && winRate >= 65) {
            return '🏆';
        }
        if (bias.includes('Neutral')) {
            return '🟡';
        }
        return '🔴';
    }
    
    generateDeltaInsights(results) {
        const insights = [];
        
        // Comparar aligned vs against
        const aligned = results.filter(r => !r.bias.includes('Against'));
        const against = results.filter(r => r.bias.includes('Against'));
        
        if (aligned.length > 0 && against.length > 0) {
            const avgAlignedWR = aligned.reduce((sum, r) => sum + r.winRate, 0) / aligned.length;
            const avgAgainstWR = against.reduce((sum, r) => sum + r.winRate, 0) / against.length;
            
            const spread = avgAlignedWR - avgAgainstWR;
            
            if (spread > 30) {
                insights.push({
                    type: 'critical',
                    message: `🚨 Delta alignment is CRITICAL: ${avgAlignedWR.toFixed(0)}% WR when aligned vs ${avgAgainstWR.toFixed(0)}% when against`
                });
                
                insights.push({
                    type: 'recommendation',
                    message: `💡 RECOMMENDATION: Add delta filter. Require bullish delta for longs, bearish for shorts.`
                });
            }
        }
        
        return insights;
    }
}
```

---

## 🎯 Dashboard Nuevo: "Microstructure"

### UI Mockup

```
┌─────────────────────────────────────────────────────────────┐
│  🔬 MICROSTRUCTURE ANALYSIS                                  │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  📊 Session Effectiveness Matrix                             │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Cross-Ref        │ Age │ Trades │ Win%  │ Avg PnL    │ │
│  ├────────────────────────────────────────────────────────┤ │
│  │ 🏆 USA takes Asia│  0d │   45   │  72%  │ $520  BEST│ │
│  │ ✅ Europe > Asia │  0d │   34   │  68%  │ $450  GOOD│ │
│  │ ✅ USA > Europe  │  0d │   23   │  65%  │ $380  GOOD│ │
│  │ 🟡 USA takes Asia│  1d │   67   │  58%  │ $210  OK  │ │
│  │ 🔴 USA takes Asia│  4d │   12   │  42%  │ -$85  POOR│ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  🎯 VWAP Support/Resistance Strength                         │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Time Against VWAP│ Trades │ Win%  │ Avg PnL │ Status │ │
│  ├────────────────────────────────────────────────────────┤ │
│  │ 0-20%           │   89   │  75%  │ $560    │ 🏆 Strong│
│  │ 21-40%          │  145   │  64%  │ $320    │ ✅ Good  │
│  │ 41-60%          │   67   │  52%  │ $120    │ 🟡 Weak  │
│  │ 61-100%         │   23   │  35%  │ -$180   │ 🔴 Failed│
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  📈 Delta Bias Analysis                                      │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Delta Bias      │ Trades │ Win%  │ Avg PnL │ Conviction│
│  ├────────────────────────────────────────────────────────┤ │
│  │ Strong Bullish  │   45   │  82%  │ $680    │ 🏆🏆🏆  │
│  │ Bullish         │   98   │  71%  │ $420    │ 🏆      │
│  │ Neutral         │   67   │  58%  │ $180    │ 🟡      │
│  │ Against         │   23   │  38%  │ -$210   │ 🔴      │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  🧠 Intelligence Insights                                    │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ 🏆 USA taking Asia same day = 72% WR (BEST)            │ │
│  │ ⚠️  Levels >3 days degrade to 42% WR                   │ │
│  │ 🚨 VWAP holding = 75% WR vs broken = 35% WR           │ │
│  │ 🚨 Delta aligned = 77% WR vs against = 38% WR         │ │
│  │ 💡 Filter: Same-day levels + VWAP holds + Delta aligned│
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

---

## 📝 CSV Format Actualizado

```csv
ID,Instrument,EntryTime,ExitTime,Type,EntryPrice,ExitPrice,PnL,MAE,MFE,
SetupType,LevelAge,DistanceToVWAP,SessionType,ATR,Volatility,Spread,Slippage,
TimeToFill,RRRatio,ExitReason,ExitEfficiency,TimeInTrade,BarsInTrade,
LevelSourceSession,LevelTargetSession,SessionCrossRef,LevelSourceAgeDays,
VWAPAnchorSession,VWAPAnchorAgeDays,TimeAgainstVWAPPct,BarsAgainstVWAP,
CumulativeDeltaEntry,CumulativeDeltaExit,DeltaChange,DeltaBias,
BacktestFlag

abc123_1,MNQ 03-26,2025-12-26T10:30:00,2025-12-26T11:15:00,Long,25800.5,25850.25,497.5,
-125.5,620.0,Asia Low,2,15.5,USA,12.3,0.045,0.5,0.25,150,2.3,TP1,79.5,
2700,45,Asia,USA,USA takes Asia,0,Asia,0,18.5,8,
15420,16850,1430,Bullish,true
```

---

## ⏱️ Impacto en Tiempo de Implementación

**Fase 2 actualizada**: 18-20h → **25-28h** (+7h para order flow)

### Breakdown:
- Session effectiveness tracking: +2h
- VWAP strength tracking: +2h
- Order Flow integration: +3h (requiere testing con diferentes instrumentos)

---

## 🎯 Insights Esperados

Una vez implementado, el sistema podrá responder automáticamente:

1. ✅ "USA tomando Asia mismo día tiene 72% WR vs 42% a los 4 días"
2. ✅ "Cuando VWAP sostiene (precio <20% tiempo contra), WR sube a 75%"
3. ✅ "Delta a favor mejora WR de 58% a 82%"
4. ✅ "Combinando: same-day levels + VWAP holds + delta aligned = 85%+ WR"

**Resultado**: Filtros científicos basados en datos reales, no corazonadas.
