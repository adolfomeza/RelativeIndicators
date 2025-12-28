# Addendum: Dashboard Multi-Instrumento

## Tab Nuevo: "Portfolio Analysis"

### UI Mockup

```
┌─────────────────────────────────────────────────────────────┐
│  📊 PORTFOLIO ANALYSIS                                       │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  Performance por Instrumento                                 │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Instrument │ Trades │ Win% │ PF  │ Net PnL │ Sharpe  │ │
│  ├────────────────────────────────────────────────────────┤ │
│  │ 🏆 MYM     │ 67     │ 71%  │ 2.4 │ $3,200  │ 1.8     │ │
│  │ 🟢 MNQ     │ 145    │ 68%  │ 2.1 │ $12,450 │ 1.5     │ │
│  │ 🟡 MES     │ 98     │ 62%  │ 1.8 │ $4,320  │ 1.2     │ │
│  │ 🔴 MCL     │ 112    │ 55%  │ 1.2 │ -$1,200 │ -0.3    │ │
│  │ 🔴 MGC     │ 34     │ 48%  │ 0.9 │ -$850   │ -0.8    │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  📈 Equity Curves Comparadas                                 │
│  [Gráfico multi-línea con equity curve por instrumento]     │
│                                                              │
│  🧠 Insights Automáticos                                     │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ ✅ MYM es tu mejor performer (71% WR)                  │ │
│  │ 💡 Considera aumentar allocation de MNQ/MYM            │ │
│  │ ⚠️  MCL está perdiendo dinero (-$1,200)                │ │
│  │ 🔴 MGC tiene edge negativo (PF < 1), considerar pausa  │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  Recomendaciones de Capital Allocation                       │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Actual:  MNQ 40% | MES 20% | MYM 20% | MCL 15% | MGC 5%│ │
│  │ Óptimo:  MNQ 45% | MYM 35% | MES 20% | MCL  0% | MGC 0%│ │
│  │ Impacto: +$4,800/mes (estimado)                        │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### Código JavaScript

```javascript
// ===================================================================
// PORTFOLIO ANALYSIS MODULE
// ===================================================================

class PortfolioAnalyzer {
    
    constructor(trades) {
        this.trades = trades;
        this.instruments = [...new Set(trades.map(t => t.instrument))].sort();
    }
    
    // Calcular estadísticas por instrumento
    calculateInstrumentStats() {
        const stats = {};
        
        this.instruments.forEach(inst => {
            const instTrades = this.trades.filter(t => t.instrument === inst);
            
            if (instTrades.length === 0) {
                stats[inst] = null;
                return;
            }
            
            const wins = instTrades.filter(t => t.pnl > 0);
            const losses = instTrades.filter(t => t.pnl < 0);
            
            const grossProfit = wins.reduce((sum, t) => sum + t.pnl, 0);
            const grossLoss = Math.abs(losses.reduce((sum, t) => sum + t.pnl, 0));
            const netPnL = instTrades.reduce((sum, t) => sum + t.pnl, 0);
            
            const winRate = (wins.length / instTrades.length) * 100;
            const profitFactor = grossLoss > 0 ? grossProfit / grossLoss : 0;
            
            // Calcular Sharpe Ratio
            const pnls = instTrades.map(t => t.pnl);
            const sharpe = this.calculateSharpe(pnls);
            
            // Best/Worst trades
            const allPnLs = instTrades.map(t => t.pnl).sort((a,b) => b - a);
            const bestTrade = allPnLs[0] || 0;
            const worstTrade = allPnLs[allPnLs.length - 1] || 0;
            
            stats[inst] = {
                instrument: inst,
                trades: instTrades.length,
                wins: wins.length,
                losses: losses.length,
                winRate: winRate,
                profitFactor: profitFactor,
                netPnL: netPnL,
                grossProfit: grossProfit,
                grossLoss: grossLoss,
                avgWin: wins.length > 0 ? grossProfit / wins.length : 0,
                avgLoss: losses.length > 0 ? -grossLoss / losses.length : 0,
                sharpe: sharpe,
                bestTrade: bestTrade,
                worstTrade: worstTrade,
                // Equity curve
                equityCurve: this.buildEquityCurve(instTrades)
            };
        });
        
        return stats;
    }
    
    // Construir equity curve
    buildEquityCurve(trades) {
        trades.sort((a, b) => a.entryTime - b.entryTime);
        
        let equity = 0;
        const curve = [{ x: 0, y: 0, date: null }];
        
        trades.forEach((trade, idx) => {
            equity += trade.pnl;
            curve.push({
                x: idx + 1,
                y: equity,
                date: trade.exitTime || trade.entryTime
            });
        });
        
        return curve;
    }
    
    // Calcular Sharpe Ratio
    calculateSharpe(pnls) {
        if (pnls.length < 2) return 0;
        
        const mean = pnls.reduce((a,b) => a+b, 0) / pnls.length;
        const variance = pnls.reduce((sq, n) => sq + Math.pow(n - mean, 2), 0) / (pnls.length - 1);
        const stdDev = Math.sqrt(variance);
        
        if (stdDev === 0) return 0;
        
        const dailySharpe = mean / stdDev;
        return dailySharpe * Math.sqrt(252); // Anualizado
    }
    
    // Generar insights automáticos
    generateInsights(stats) {
        const insights = [];
        const instruments = Object.values(stats).filter(s => s !== null);
        
        if (instruments.length === 0) return insights;
        
        // Ordenar por Sharpe Ratio
        instruments.sort((a, b) => b.sharpe - a.sharpe);
        
        const best = instruments[0];
        const worst = instruments[instruments.length - 1];
        
        // Insight 1: Best Performer
        insights.push({
            type: 'success',
            icon: '🏆',
            message: `${best.instrument} es tu mejor performer (Win Rate ${best.winRate.toFixed(1)}%, Sharpe ${best.sharpe.toFixed(2)})`
        });
        
        // Insight 2: Capital Allocation
        const profitable = instruments.filter(i => i.netPnL > 0);
        if (profitable.length > 0 && profitable.length < instruments.length) {
            const profitableNames = profitable.map(i => i.instrument).join(', ');
            insights.push({
                type: 'info',
                icon: '💡',
                message: `Considera concentrar capital en: ${profitableNames}`
            });
        }
        
        // Insight 3: Losing Instruments
        const losers = instruments.filter(i => i.netPnL < 0);
        losers.forEach(loser => {
            insights.push({
                type: 'warning',
                icon: '⚠️',
                message: `${loser.instrument} está perdiendo dinero (${formatCurrency(loser.netPnL)})`
            });
        });
        
        // Insight 4: Negative Edge
        const negativeEdge = instruments.filter(i => i.profitFactor < 1);
        negativeEdge.forEach(ne => {
            insights.push({
                type: 'error',
                icon: '🔴',
                message: `${ne.instrument} tiene edge negativo (PF ${ne.profitFactor.toFixed(2)}), considerar pausa`
            });
        });
        
        return insights;
    }
    
    // Sugerir allocation óptimo
    suggestAllocation(stats) {
        const instruments = Object.values(stats).filter(s => s !== null && s.profitFactor > 1);
        
        if (instruments.length === 0) {
            return {
                current: {},
                optimal: {},
                message: 'Ningún instrumento tiene edge positivo'
            };
        }
        
        // Peso basado en Sharpe Ratio
        const totalSharpe = instruments.reduce((sum, i) => sum + Math.max(i.sharpe, 0), 0);
        
        const optimal = {};
        instruments.forEach(inst => {
            const weight = totalSharpe > 0 ? (Math.max(inst.sharpe, 0) / totalSharpe) * 100 : 0;
            optimal[inst.instrument] = weight.toFixed(0) + '%';
        });
        
        return {
            optimal: optimal,
            message: 'Allocation óptimo basado en Sharpe Ratio'
        };
    }
    
    // Renderizar dashboard
    renderDashboard() {
        const stats = this.calculateInstrumentStats();
        const insights = this.generateInsights(stats);
        const allocation = this.suggestAllocation(stats);
        
        // Tabla de performance
        this.renderPerformanceTable(stats);
        
        // Equity curves comparadas
        this.renderComparativeEquityCurve(stats);
        
        // Insights automáticos
        this.renderInsights(insights);
        
        // Capital allocation
        this.renderAllocation(allocation);
    }
    
    renderPerformanceTable(stats) {
        const tbody = document.getElementById('portfolio-table-body');
        let html = '';
        
        // Ordenar por Net PnL descendente
        const sorted = Object.values(stats)
            .filter(s => s !== null)
            .sort((a, b) => b.netPnL - a.netPnL);
        
        sorted.forEach(stat => {
            const pnlClass = stat.netPnL >= 0 ? 'pnl-pos' : 'pnl-neg';
            const icon = stat.profitFactor > 2 ? '🏆' : 
                        stat.profitFactor > 1.5 ? '🟢' :
                        stat.profitFactor > 1 ? '🟡' : '🔴';
            
            html += `
                <tr>
                    <td>${icon} ${stat.instrument}</td>
                    <td>${stat.trades}</td>
                    <td>${stat.winRate.toFixed(1)}%</td>
                    <td>${stat.profitFactor.toFixed(2)}</td>
                    <td class="${pnlClass}">${formatCurrency(stat.netPnL)}</td>
                    <td>${stat.sharpe.toFixed(2)}</td>
                    <td>${formatCurrency(stat.avgWin)}</td>
                    <td>${formatCurrency(stat.avgLoss)}</td>
                    <td>${formatCurrency(stat.bestTrade)}</td>
                    <td>${formatCurrency(stat.worstTrade)}</td>
                </tr>
            `;
        });
        
        // Row total
        const totalTrades = sorted.reduce((sum, s) => sum + s.trades, 0);
        const totalNetPnL = sorted.reduce((sum, s) => sum + s.netPnL, 0);
        const totalWins = sorted.reduce((sum, s) => sum + s.wins, 0);
        const totalWinRate = (totalWins / totalTrades) * 100;
        
        html += `
            <tr class="total-row">
                <td><strong>TOTAL</strong></td>
                <td><strong>${totalTrades}</strong></td>
                <td><strong>${totalWinRate.toFixed(1)}%</strong></td>
                <td>-</td>
                <td class="${totalNetPnL >= 0 ? 'pnl-pos' : 'pnl-neg'}">
                    <strong>${formatCurrency(totalNetPnL)}</strong>
                </td>
                <td colspan="5">-</td>
            </tr>
        `;
        
        tbody.innerHTML = html;
    }
    
    renderComparativeEquityCurve(stats) {
        const ctx = document.getElementById('portfolioEquityChart').getContext('2d');
        
        if (charts.portfolioEquity) charts.portfolioEquity.destroy();
        
        const datasets = [];
        const colors = ['#10b981', '#f59e0b', '#8b5cf6', '#ec4899', '#6366f1', '#14b8a6'];
        
        Object.values(stats).forEach((stat, idx) => {
            if (stat === null) return;
            
            datasets.push({
                label: stat.instrument,
                data: stat.equityCurve,
                borderColor: colors[idx % colors.length],
                borderWidth: 2,
                fill: false,
                tension: 0.2,
                pointRadius: 0
            });
        });
        
        charts.portfolioEquity = new Chart(ctx, {
            type: 'line',
            data: { datasets: datasets },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                scales: {
                    x: { type: 'linear', title: { display: true, text: 'Trade #' } },
                    y: { title: { display: true, text: 'Equity' } }
                },
                plugins: {
                    legend: { display: true },
                    tooltip: { mode: 'index', intersect: false }
                }
            }
        });
    }
    
    renderInsights(insights) {
        const container = document.getElementById('portfolio-insights');
        let html = '';
        
        insights.forEach(insight => {
            const className = `insight-${insight.type}`;
            html += `
                <div class="insight-card ${className}">
                    <span class="insight-icon">${insight.icon}</span>
                    <span class="insight-message">${insight.message}</span>
                </div>
            `;
        });
        
        container.innerHTML = html;
    }
    
    renderAllocation(allocation) {
        const container = document.getElementById('allocation-recommendation');
        
        let html = '<h4>Recomendación de Capital Allocation</h4>';
        html += '<table class="allocation-table">';
        
        Object.entries(allocation.optimal).forEach(([inst, weight]) => {
            html += `<tr><td>${inst}</td><td>${weight}</td></tr>`;
        });
        
        html += '</table>';
        html += `<p class="allocation-note">${allocation.message}</p>`;
        
        container.innerHTML = html;
    }
}

// Inicializar en tab Portfolio Analysis
window.switchToPortfolioTab = () => {
    const analyzer = new PortfolioAnalyzer(globalAllTrades);
    analyzer.renderDashboard();
};
```

### HTML Adicional

```html
<!-- Agregar tab -->
<div class="tabs">
    <button class="tab-btn active" onclick="switchTab('overview')">Overview</button>
    <button class="tab-btn" onclick="switchTab('temporal')">Time Analysis</button>
    <button class="tab-btn" onclick="switchTab('advanced')">Advanced (MAE/MFE)</button>
    <button class="tab-btn" onclick="switchTab('audit')">Audit & Edge</button>
    <button class="tab-btn" onclick="switchTab('portfolio'); switchToPortfolioTab()">Portfolio</button>
</div>

<!-- Contenido del tab -->
<div id="tab-portfolio" class="tab-content">
    <h2>📊 Portfolio Analysis</h2>
    
    <!-- Performance Table -->
    <div class="table-container">
        <h3>Performance por Instrumento</h3>
        <table>
            <thead>
                <tr>
                    <th>Instrument</th>
                    <th>Trades</th>
                    <th>Win%</th>
                    <th>PF</th>
                    <th>Net PnL</th>
                    <th>Sharpe</th>
                    <th>Avg Win</th>
                    <th>Avg Loss</th>
                    <th>Best</th>
                    <th>Worst</th>
                </tr>
            </thead>
            <tbody id="portfolio-table-body"></tbody>
        </table>
    </div>
    
    <!-- Equity Curves -->
    <div class="chart-card large">
        <h3>Equity Curves Comparadas</h3>
        <canvas id="portfolioEquityChart"></canvas>
    </div>
    
    <!-- Insights -->
    <div id="portfolio-insights" class="insights-container"></div>
    
    <!-- Allocation -->
    <div id="allocation-recommendation" class="allocation-container"></div>
</div>
```

### CSS Adicional

```css
.insight-card {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 12px;
    margin-bottom: 8px;
    border-radius: var(--radius-sm);
    border-left: 4px solid;
}

.insight-success {
    background: var(--success-bg);
    border-color: var(--success);
}

.insight-info {
    background: rgba(99, 102, 241, 0.1);
    border-color: var(--accent);
}

.insight-warning {
    background: rgba(245, 158, 11, 0.1);
    border-color: #f59e0b;
}

.insight-error {
    background: var(--danger-bg);
    border-color: var(--danger);
}

.total-row {
    border-top: 2px solid var(--accent);
    font-weight: bold;
}

.allocation-table {
    width: 100%;
    max-width: 400px;
    margin: 10px auto;
}

.allocation-note {
    text-align: center;
    color: var(--text-secondary);
    font-size: 0.85rem;
}
```

---

## Resumen de Cambios Multi-Instrumento

✅ **Export**: Cada instrumento exporta a su propio CSV  
✅ **Auto-Discovery**: Detecta y carga múltiples CSVs automáticamente  
✅ **Consolidación**: Merge inteligente sin duplicados  
✅ **Comparative Analysis**: Dashboard con performance por instrumento  
✅ **Insights Específicos**: Recomendaciones por instrumento  
✅ **Capital Allocation**: Sugerencias de distribución óptima

**Impacto en Estimación de Tiempo**:
- Fase 1: +3 horas (total 15-18h) para multi-instrumento
