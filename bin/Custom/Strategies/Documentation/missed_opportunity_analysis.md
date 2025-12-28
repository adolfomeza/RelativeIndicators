# Missed Opportunity Analysis - Implementación Completa

## 🎯 Concepto Clave

> **"Los trades que NO tomas son tan importantes como los que tomas"**

### Por qué es CRÍTICO

1. **Filtros demasiado restrictivos** → Bloqueando buenos setups
2. **Oportunidades perdidas** → Cuantificar costo de oportunidad
3. **Optimización de reglas** → Relajar filtros que no agregan valor
4. **Validación de edge** → ¿Los setups rechazados hubieran ganado?

---

## 📊 Tipos de "Missed Trades"

### 1. **Setup Detectado, No Ejecutado** (CRÍTICO)
- Trigger válido detectado
- VWAP anchor confirmado
- **PERO**: Rechazado por filtros

**Razones comunes**:
- RR < 1:1
- Filtro de hora
- Nivel demasiado antiguo
- Ya alcanzado max trades del día
- Connection issues
- Fill rate (limit order no se llenó)

### 2. **Partial Fill**
- Orden limit enviada
- Solo se llenó parcialmente
- **Resultado**: Ejecutamos 1 contrato en lugar de 2

### 3. **Rejected by Exchange**
- Orden enviada
- Rechazada por broker/exchange
- **Razones**: Invalid price, margin, etc.

---

## 💻 Implementación en NinjaTrader

### Clase de Tracking

```csharp
// Agregar a SessionLevelsStrategy.cs (después de línea 160)

// ===================================================================
// MISSED OPPORTUNITY TRACKING (v1.7.34)
// ===================================================================

private class MissedSetup
{
    public DateTime DetectedTime { get; set; }
    public string SetupType { get; set; }        // "Short Asia High", etc.
    public double TriggerPrice { get; set; }
    public double SetupVWAP { get; set; }
    public double TargetPrice { get; set; }
    public double StopPrice { get; set; }
    public double RRRatio { get; set; }
    public string RejectionReason { get; set; }  // "RR < 1:1", "Hour filter", etc.
    public string SessionType { get; set; }      // "Asia", "Europe", "USA"
    public int LevelAgeDays { get; set; }
    
    // Simulación
    public double SimulatedPnL { get; set; }
    public string SimulatedOutcome { get; set; } // "Would Hit TP1", "Would Hit SL", etc.
    public bool WouldBeWinner { get; set; }
}

private List<MissedSetup> missedSetups = new List<MissedSetup>();
private string missedSetupsCSVPath = "";
```

### Logging de Setup Detectado

```csharp
// En ManageEntryA_Plus, ANTES de validaciones

// NUEVO: Log ANTES de cualquier validación
private void LogSetupDetected(bool isShort, double triggerPrice, double setupVWAP, 
                               double targetPrice, double stopPrice, double rrRatio)
{
    string setupType = isShort ? "Short " + setupLevelName : "Long " + setupLevelName;
    string sessionType = DetermineSessionFromTime(Time[0]);
    
    // Crear registro preliminar
    var missedSetup = new MissedSetup
    {
        DetectedTime = Time[0],
        SetupType = setupType,
        TriggerPrice = triggerPrice,
        SetupVWAP = setupVWAP,
        TargetPrice = targetPrice,
        StopPrice = stopPrice,
        RRRatio = rrRatio,
        SessionType = sessionType,
        LevelAgeDays = levelSourceAgeDays,
        RejectionReason = "To be determined" // Se actualizará si se rechaza
    };
    
    // Guardar temporalmente (se actualizará después)
    currentDetectedSetup = missedSetup;
}

private MissedSetup currentDetectedSetup = null;

// Si setup es RECHAZADO, actualizar razón
private void LogMissedOpportunity(string reason)
{
    if (currentDetectedSetup == null) return;
    
    currentDetectedSetup.RejectionReason = reason;
    
    // Simular outcome
    SimulateMissedTrade(currentDetectedSetup);
    
    // Guardar en lista
    missedSetups.Add(currentDetectedSetup);
    
    // Export a CSV
    ExportMissedSetupToCSV(currentDetectedSetup);
    
    Log(string.Format("MISSED_SETUP: {0} - Reason: {1} - Simulated: {2}",
        currentDetectedSetup.SetupType, reason, currentDetectedSetup.SimulatedOutcome));
    
    // Reset
    currentDetectedSetup = null;
}

// Si setup es EJECUTADO, limpiar
private void ClearDetectedSetup()
{
    currentDetectedSetup = null;
}
```

### Razones de Rechazo

```csharp
// Actualizar código existente para loggear rechazos

// Ejemplo 1: RR Validation
if (rrRatio < 1.0)
{
    Log(string.Format("SHORT Setup REJECTED: RR {0:F2} < 1.0", rrRatio));
    LogMissedOpportunity($"RR too low ({rrRatio:F2})");
    return; // No entrar
}

// Ejemplo 2: Hour Filter
if (isFilteredByHour)
{
    Log("Setup REJECTED: Hour filter active");
    LogMissedOpportunity($"Hour filter ({Time[0].Hour}:00)");
    return;
}

// Ejemplo 3: Level Age Filter
if (levelSourceAgeDays > MaxLevelAgeDays)
{
    Log($"Setup REJECTED: Level too old ({levelSourceAgeDays} days)");
    LogMissedOpportunity($"Level age > {MaxLevelAgeDays} days");
    return;
}

// Ejemplo 4: Connection State
if (State != State.Realtime && State != State.Historical)
{
    Log("Setup REJECTED: Not in valid state");
    LogMissedOpportunity($"Invalid state ({State})");
    return;
}

// Ejemplo 5: Max Trades Limit
if (tradesCountToday >= MaxTradesPerDay)
{
    Log("Setup REJECTED: Max trades limit reached");
    LogMissedOpportunity($"Max trades/day ({MaxTradesPerDay})");
    return;
}

// Si pasa todas las validaciones
ClearDetectedSetup(); // Setup ejecutado, no es "missed"
```

### Simulación de Outcome

```csharp
/// <summary>
/// Simula qué hubiera pasado si hubiéramos tomado el setup
/// </summary>
private void SimulateMissedTrade(MissedSetup setup)
{
    // Buscar hacia adelante qué pasó después
    // Nota: Esto requiere CurrentBar tracking
    
    double entryPrice = setup.TriggerPrice;
    double tp1 = setup.SetupVWAP;
    double tp2 = setup.TargetPrice;
    double sl = setup.StopPrice;
    
    bool isLong = setup.SetupType.Contains("Long");
    
    // Buscar en los próximos N bars qué se tocó primero
    int lookAhead = Math.Min(100, Bars.Count - CurrentBar - 1);
    
    for (int i = 1; i <= lookAhead; i++)
    {
        if (CurrentBar + i >= Bars.Count) break;
        
        double high = High[CurrentBar + i];
        double low = Low[CurrentBar + i];
        
        if (isLong)
        {
            // Check SL primero (más cercano)
            if (low <= sl)
            {
                setup.SimulatedOutcome = "Would Hit SL";
                setup.SimulatedPnL = sl - entryPrice; // Negativo
                setup.WouldBeWinner = false;
                return;
            }
            
            // Check TP1
            if (high >= tp1)
            {
                setup.SimulatedOutcome = "Would Hit TP1";
                setup.SimulatedPnL = tp1 - entryPrice; // Positivo
                setup.WouldBeWinner = true;
                return;
            }
            
            // Check TP2
            if (high >= tp2)
            {
                setup.SimulatedOutcome = "Would Hit TP2";
                setup.SimulatedPnL = (tp1 - entryPrice) + (tp2 - entryPrice); // Ambos TPs
                setup.WouldBeWinner = true;
                return;
            }
        }
        else // Short
        {
            // Check SL primero
            if (high >= sl)
            {
                setup.SimulatedOutcome = "Would Hit SL";
                setup.SimulatedPnL = entryPrice - sl; // Negativo
                setup.WouldBeWinner = false;
                return;
            }
            
            // Check TP1
            if (low <= tp1)
            {
                setup.SimulatedOutcome = "Would Hit TP1";
                setup.SimulatedPnL = entryPrice - tp1; // Positivo
                setup.WouldBeWinner = true;
                return;
            }
            
            // Check TP2
            if (low <= tp2)
            {
                setup.SimulatedOutcome = "Would Hit TP2";
                setup.SimulatedPnL = (entryPrice - tp1) + (entryPrice - tp2); // Ambos
                setup.WouldBeWinner = true;
                return;
            }
        }
    }
    
    // No se tocó nada en lookAhead bars
    setup.SimulatedOutcome = "Would Expire";
    setup.SimulatedPnL = 0;
    setup.WouldBeWinner = false;
}
```

### Export a CSV

```csharp
/// <summary>
/// Exporta setups perdidos a CSV separado
/// </summary>
private void ExportMissedSetupToCSV(MissedSetup setup)
{
    if (string.IsNullOrEmpty(missedSetupsCSVPath))
    {
        // Inicializar CSV de missed setups
        string strategyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NinjaTrader 8", "bin", "Custom", "Strategies", "TradeAnalyzer"
        );
        
        if (!Directory.Exists(strategyDir))
            Directory.CreateDirectory(strategyDir);
        
        string sanitizedInstrument = Instrument.MasterInstrument.Name.Replace(" ", "_");
        missedSetupsCSVPath = Path.Combine(strategyDir, $"missed_setups_{sanitizedInstrument}.csv");
        
        // Headers
        if (!File.Exists(missedSetupsCSVPath))
        {
            string headers = "DetectedTime,SetupType,TriggerPrice,SetupVWAP,TargetPrice,StopPrice,RRRatio," +
                           "RejectionReason,SessionType,LevelAgeDays,SimulatedOutcome,SimulatedPnL,WouldBeWinner";
            File.WriteAllText(missedSetupsCSVPath, headers + Environment.NewLine);
        }
    }
    
    try
    {
        string csvLine = string.Format(
            "{0},{1},{2},{3},{4},{5},{6:F2},{7},{8},{9},{10},{11:F2},{12}",
            FormatDateTime(setup.DetectedTime),
            setup.SetupType,
            setup.TriggerPrice,
            setup.SetupVWAP,
            setup.TargetPrice,
            setup.StopPrice,
            setup.RRRatio,
            setup.RejectionReason,
            setup.SessionType,
            setup.LevelAgeDays,
            setup.SimulatedOutcome,
            setup.SimulatedPnL,
            setup.WouldBeWinner ? "true" : "false"
        );
        
        File.AppendAllText(missedSetupsCSVPath, csvLine + Environment.NewLine);
    }
    catch (Exception ex)
    {
        Print("Missed Setup CSV Export Error: " + ex.Message);
    }
}
```

---

## 📊 Análisis en TradeAnalyzer

### Dashboard: Missed Opportunities

```javascript
// ===================================================================
// MISSED OPPORTUNITY ANALYZER
// ===================================================================

class MissedOpportunityAnalyzer {
    
    constructor(missedSetups, executedTrades) {
        this.missed = missedSetups;
        this.executed = executedTrades;
    }
    
    // Analizar por razón de rechazo
    analyzeByRejectionReason() {
        const reasons = {};
        
        this.missed.forEach(m => {
            const reason = m.rejectionReason;
            
            if (!reasons[reason]) {
                reasons[reason] = {
                    count: 0,
                    winners: 0,
                    losers: 0,
                    totalSimulatedPnL: 0,
                    setups: []
                };
            }
            
            reasons[reason].count++;
            reasons[reason].totalSimulatedPnL += m.simulatedPnL;
            reasons[reason].setups.push(m);
            
            if (m.wouldBeWinner) {
                reasons[reason].winners++;
            } else {
                reasons[reason].losers++;
            }
        });
        
        // Calcular stats
        const results = [];
        Object.entries(reasons).forEach(([reason, data]) => {
            const winRate = (data.winners / data.count) * 100;
            const avgPnL = data.totalSimulatedPnL / data.count;
            const opportunityCost = data.totalSimulatedPnL; // Total money left on table
            
            results.push({
                reason: reason,
                count: data.count,
                winRate: winRate,
                avgPnL: avgPnL,
                opportunityCost: opportunityCost,
                recommendation: this.generateRecommendation(reason, winRate, opportunityCost)
            });
        });
        
        // Ordenar por opportunity cost descendente
        results.sort((a, b) => b.opportunityCost - a.opportunityCost);
        
        return results;
    }
    
    generateRecommendation(reason, winRate, opportunityCost) {
        // Si el filtro está bloqueando buenos trades
        if (winRate >= 60 && opportunityCost > 1000) {
            return {
                type: 'critical',
                action: 'REMOVE FILTER',
                message: `🚨 Filter "${reason}" is blocking profitable setups (${winRate.toFixed(0)}% WR, ${formatCurrency(opportunityCost)} lost)`
            };
        }
        
        // Si el filtro está bloqueando trades mediocres
        if (winRate >= 50 && winRate < 60 && opportunityCost > 500) {
            return {
                type: 'warning',
                action: 'REVIEW FILTER',
                message: `⚠️ Filter "${reason}" blocking marginal setups (${winRate.toFixed(0)}% WR, ${formatCurrency(opportunityCost)} opportunity cost)`
            };
        }
        
        // Si el filtro está bloqueando trades malos (BUENO)
        if (winRate < 45) {
            return {
                type: 'success',
                action: 'KEEP FILTER',
                message: `✅ Filter "${reason}" is working (blocking losing setups: ${winRate.toFixed(0)}% WR)`
            };
        }
        
        return {
            type: 'info',
            action: 'NEUTRAL',
            message: `ℹ️ Filter "${reason}" is neutral (${winRate.toFixed(0)}% WR, ${formatCurrency(opportunityCost)} impact)`
        };
    }
    
    // Comparar missed vs executed
    comparePerformance() {
        // Stats de executed
        const executedWins = this.executed.filter(t => t.pnl > 0).length;
        const executedWinRate = (executedWins / this.executed.length) * 100;
        const executedAvgPnL = this.executed.reduce((sum, t) => sum + t.pnl, 0) / this.executed.length;
        
        // Stats de missed (simulados)
        const missedWins = this.missed.filter(m => m.wouldBeWinner).length;
        const missedWinRate = (missedWins / this.missed.length) * 100;
        const missedAvgPnL = this.missed.reduce((sum, m) => sum + m.simulatedPnL, 0) / this.missed.length;
        
        const comparison = {
            executed: {
                count: this.executed.length,
                winRate: executedWinRate,
                avgPnL: executedAvgPnL
            },
            missed: {
                count: this.missed.length,
                winRate: missedWinRate,
                avgPnL: missedAvgPnL
            },
            insights: []
        };
        
        // Generar insights
        if (missedWinRate > executedWinRate + 10) {
            comparison.insights.push({
                type: 'critical',
                message: `🚨 CRITICAL: Missed setups would have ${missedWinRate.toFixed(0)}% WR vs ${executedWinRate.toFixed(0)}% executed. Filters too restrictive!`
            });
        }
        
        if (this.missed.length > this.executed.length * 2) {
            comparison.insights.push({
                type: 'warning',
                message: `⚠️ Rejecting ${this.missed.length} setups vs ${this.executed.length} executed (${(this.missed.length / this.executed.length).toFixed(1)}x). Consider relaxing filters.`
            });
        }
        
        const totalOpportunityCost = this.missed.reduce((sum, m) => sum + m.simulatedPnL, 0);
        if (totalOpportunityCost > 5000) {
            comparison.insights.push({
                type: 'critical',
                message: `💰 OPPORTUNITY COST: ${formatCurrency(totalOpportunityCost)} left on table from missed setups`
            });
        }
        
        return comparison;
    }
    
    renderDashboard() {
        const byReason = this.analyzeByRejectionReason();
        const comparison = this.comparePerformance();
        
        const container = document.getElementById('missed-opportunities-analysis');
        
        let html = '<h3>🔍 Missed Opportunities Analysis</h3>';
        
        // Comparison summary
        html += '<div class="comparison-summary">';
        html += `<div class="stat-box">
            <h4>Executed Trades</h4>
            <p>Count: ${comparison.executed.count}</p>
            <p>Win Rate: ${comparison.executed.winRate.toFixed(1)}%</p>
            <p>Avg PnL: ${formatCurrency(comparison.executed.avgPnL)}</p>
        </div>`;
        
        html += `<div class="stat-box">
            <h4>Missed Setups (Simulated)</h4>
            <p>Count: ${comparison.missed.count}</p>
            <p>Win Rate: ${comparison.missed.winRate.toFixed(1)}%</p>
            <p>Avg PnL: ${formatCurrency(comparison.missed.avgPnL)}</p>
        </div>`;
        html += '</div>';
        
        // Insights
        html += '<div class="missed-insights">';
        comparison.insights.forEach(insight => {
            html += `<div class="insight-card insight-${insight.type}">${insight.message}</div>`;
        });
        html += '</div>';
        
        // Table by rejection reason
        html += '<h4>Breakdown by Rejection Reason</h4>';
        html += '<table class="missed-table">';
        html += '<thead><tr><th>Reason</th><th>Count</th><th>Win%</th><th>Avg PnL</th><th>Opportunity Cost</th><th>Recommendation</th></tr></thead>';
        html += '<tbody>';
        
        byReason.forEach(r => {
            const recClass = `rec-${r.recommendation.type}`;
            html += `
                <tr>
                    <td>${r.reason}</td>
                    <td>${r.count}</td>
                    <td>${r.winRate.toFixed(1)}%</td>
                    <td class="${r.avgPnL >= 0 ? 'pnl-pos' : 'pnl-neg'}">${formatCurrency(r.avgPnL)}</td>
                    <td class="${r.opportunityCost >= 0 ? 'pnl-pos' : 'pnl-neg'}">${formatCurrency(r.opportunityCost)}</td>
                    <td class="${recClass}">
                        <strong>${r.recommendation.action}</strong><br>
                        <small>${r.recommendation.message}</small>
                    </td>
                </tr>
            `;
        });
        
        html += '</tbody></table>';
        
        container.innerHTML = html;
    }
}

// Cargar missed setups CSV
async function loadMissedSetups() {
    // Similar a loadTrades pero para missed_setups_*.csv
    // Parser específico para el formato de missed setups
}
```

---

## 📈 Ejemplo de Análisis

### Escenario Real

**Executed**: 145 trades, 68% WR, Avg $420  
**Missed**: 89 setups rechazados

**Breakdown por razón**:

| Reason | Count | Win% | Avg PnL | Opp Cost | Recommendation |
|--------|-------|------|---------|----------|----------------|
| Hour filter (14-16h) | 34 | 72% | $480 | $16,320 | 🚨 **REMOVE** |
| RR < 1.5 | 28 | 52% | $140 | $3,920 | ⚠️ **REVIEW** |
| Level age > 3d | 18 | 38% | -$120 | -$2,160 | ✅ **KEEP** |
| Max trades/day | 9 | 68% | $510 | $4,590 | ⚠️ **INCREASE LIMIT** |

**Insights**:
> 🚨 **CRITICAL**: Hour filter (14-16) blocking 34 setups @ 72% WR = $16,320 lost!  
> **ACTION**: Remove hour filter or make exception for high-quality setups

> ✅ **GOOD**: Level age filter working correctly (blocking 38% WR losers)

> ⚠️ **REVIEW**: Consider increasing max trades/day from current limit

---

## 🎯 Impacto

**Nuevo componente**: Missed Opportunity Tracking  
**+6-8 horas** de implementación

### Breakdown:
- Logging infrastructure: +2h
- Simulation logic: +2h
- CSV export: +1h
- TradeAnalyzer dashboard: +2h
- Testing: +1h

**Fase 2 actualizada**: 40-45h → **46-53h**

---

## 💡 Valor Estratégico

Este análisis te permite:

1. ✅ **Detectar filtros contraproducentes** que bloquean buenos trades
2. ✅ **Cuantificar opportunity cost** de cada decisión
3. ✅ **Optimizar reglas basado en data** no en corazonadas
4. ✅ **Aumentar win rate Y frequency** relajando filtros incorrectos

**Resultado**: Sistema que se auto-optimiza identificando qué está dejando dinero sobre la mesa.
