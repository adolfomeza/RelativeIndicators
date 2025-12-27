# 📋 Guía de Implementación Paso a Paso - TradeAnalyzer Quant

> **Filosofía**: Implementación incremental. Cada paso agrega UNA feature, se prueba, y se valida ANTES de continuar.

---

## 🎯 Estructura de Cada Paso

```
PASO N: [Nombre del paso]
├── Objetivo: Qué vamos a implementar
├── Archivos a modificar
├── Código a agregar
├── Testing: Cómo probar que funciona
├── Validación: Qué outputs esperar
└── Rollback: Cómo revertir si falla
```

---

# 🚀 FASE 1: Foundation (15-18h)

## ✅ PASO 1: Export CSV Básico (2-3h)

### Objetivo
Exportar trades básicos desde SessionLevelsStrategy a CSV con MAE/MFE.

### Prerequisitos
- Tener SessionLevelsStrategy v1.7.30+ funcionando
- Crear carpeta `TradeAnalyzer` en `Documents/NinjaTrader 8/bin/Custom/Strategies/`

### Archivos a Modificar
1. `SessionLevelsStrategy.cs`

### Código a Agregar

**Ubicación**: Después de línea ~160 (variables globales)

```csharp
// ===================================================================
// CSV EXPORT BASIC (v1.7.31)
// ===================================================================

private string csvExportPath = "";
private bool csvInitialized = false;
```

**Ubicación**: Después de método `OnStateChange`, State.Configure

```csharp
// En State.Configure
if (State == State.Configure)
{
    // Inicializar path de CSV
    string strategyDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "NinjaTrader 8", "bin", "Custom", "Strategies", "TradeAnalyzer"
    );
    
    if (!Directory.Exists(strategyDir))
        Directory.CreateDirectory(strategyDir);
    
    string sanitizedInstrument = Instrument.MasterInstrument.Name.Replace(" ", "_");
    csvExportPath = Path.Combine(strategyDir, $"trades_export_{sanitizedInstrument}.csv");
    
    Print("CSV Export Path: " + csvExportPath);
}
```

**Ubicación**: Al final del archivo, antes del último `}`

```csharp
/// <summary>
/// Export trade básico a CSV
/// </summary>
private void ExportBasicTradeToCSV(Trade trade)
{
    try
    {
        // Inicializar headers si es primera vez
        if (!csvInitialized)
        {
            if (!File.Exists(csvExportPath))
            {
                string headers = "TradeID,Instrument,EntryTime,ExitTime,Type,EntryPrice,ExitPrice," +
                               "Result,PnL,MAE,MFE,Setup";
                File.WriteAllText(csvExportPath, headers + Environment.NewLine);
            }
            csvInitialized = true;
        }
        
        // Datos básicos
        string tradeId = Guid.NewGuid().ToString();
        string instrument = Instrument.MasterInstrument.Name;
        string entryTime = trade.Entry.Time.ToString("yyyy-MM-ddTHH:mm:ss");
        string exitTime = trade.Exit.Time.ToString("yyyy-MM-ddTHH:mm:ss");
        string type = trade.Entry.MarketPosition == MarketPosition.Long ? "Long" : "Short";
        string result = "Closed"; // Simplificado por ahora
        string setup = setupLevelName; // Variable existente
        
        // Construir línea CSV
        string csvLine = string.Format(
            "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}",
            tradeId,
            instrument,
            entryTime,
            exitTime,
            type,
            trade.Entry.Price,
            trade.Exit.Price,
            result,
            trade.ProfitCurrency,
            currentMAE,  // Variable existente
            currentMFE,  // Variable existente
            setup
        );
        
        File.AppendAllText(csvExportPath, csvLine + Environment.NewLine);
        Print("Trade exported to CSV: " + csvExportPath);
    }
    catch (Exception ex)
    {
        Print("CSV Export Error: " + ex.Message);
    }
}
```

**Ubicación**: En método `OnExecutionUpdate`, cuando posición se cierra

```csharp
// Buscar la sección donde se cierra la posición (cerca de línea 800-900)
// Agregar DESPUÉS de resetear isTrackingPosition:

if (Position.MarketPosition == MarketPosition.Flat && isTrackingPosition)
{
    // ... código existente de reset ...
    
    // NUEVO: Export a CSV
    if (execution.Order.OrderState == OrderState.Filled)
    {
        // Buscar el trade en SystemPerformance
        foreach (Trade trade in SystemPerformance.AllTrades)
        {
            if (trade.Exit.Time == execution.Time)
            {
                ExportBasicTradeToCSV(trade);
                break;
            }
        }
    }
    
    // ... resto del código ...
}
```

### Testing

1. **Compilar**:
   - Tools → New NinjaScript Editor
   - Abrir `SessionLevelsStrategy.cs`
   - Compile (F5)
   - Verificar: 0 errores

2. **Ejecutar Strategy Analyzer**:
   - Tools → Strategy Analyzer
   - Cargar `SessionLevelsStrategy`
   - Instrumento: MNQ 03-26
   - Data: Últimos 30 días
   - Run Backtest

3. **Verificar Export**:
   - Abrir: `Documents/NinjaTrader 8/bin/Custom/Strategies/TradeAnalyzer/`
   - Verificar existe: `trades_export_MNQ_03-26.csv`
   - Abrir CSV en Excel/Notepad
   - Verificar tiene headers y al menos 1 trade

### Validación

**Output esperado** del CSV:
```
TradeID,Instrument,EntryTime,ExitTime,Type,EntryPrice,ExitPrice,Result,PnL,MAE,MFE,Setup
abc123...,MNQ 03-26,2025-12-26T10:30:00,2025-12-26T11:15:00,Long,25800.5,25850.25,Closed,497.5,-50.0,600.0,Asia Low
```

**Checklist**:
- [ ] CSV creado en ubicación correcta
- [ ] Headers presentes
- [ ] Al menos 1 trade exportado
- [ ] MAE es negativo, MFE es positivo
- [ ] Timestamps en formato ISO

### Rollback
Si algo falla:
```bash
# En NinjaScript Editor, deshacer cambios:
# Ctrl+Z hasta antes de los cambios
# O restaurar desde git si tienes version control
git checkout SessionLevelsStrategy.cs
```

---

## ✅ PASO 2: Refactoring TradeAnalyzer (3-4h)

### Objetivo
Eliminar duplicación de código JavaScript en TradeAnalyzer.

### Archivos a Modificar
1. `TradeAnalyzer/index.html`
2. `TradeAnalyzer/script.js`

### Código a Modificar

**En `index.html`**:

1. Ubicar el bloque `<script>` inline (cerca de línea 400-1200)
2. **ELIMINAR TODO** el código inline entre `<script>` y `</script>`
3. Reemplazar con:

```html
<script src="script.js"></script>
```

**En `script.js`**:

1. Verificar que la versión en `script.js` sea v1.2 o inferior
2. Si es v1.2, reemplazar TODO el contenido con el código inline de `index.html` (que es v1.3)
3. Si ya es v1.3, no hacer nada

### Testing

1. **Abrir TradeAnalyzer**:
   - Navegar a carpeta TradeAnalyzer
   - Doble click en `index.html`
   - Debería abrir en Chrome/Edge

2. **Drag & Drop CSV**:
   - Arrastrar `trades_export_MNQ_03-26.csv` del Paso 1
   - Verificar que se carga

3. **Verificar Funcionalidad**:
   - [ ] Dashboard muestra stats
   - [ ] Filtros funcionan
   - [ ] Charts se renderizan
   - [ ] Tabla muestra trades
   - [ ] Console (F12) sin errores

### Validación

**Output esperado**:
- Dashboard visible con stats
- No errores en console
- CSV cargado correctamente

### Rollback
```bash
# Restaurar versión anterior
git checkout TradeAnalyzer/index.html TradeAnalyzer/script.js
```

---

## ✅ PASO 3: Parser CSV Robusto (2h)

### Objetivo
Mejorar parser CSV para detectar delimitador automáticamente.

### Archivos a Modificar
1. `TradeAnalyzer/script.js`

### Código a Modificar

**Ubicación**: Buscar función `parseCSV` (cerca de línea 200-300)

**Reemplazar** la función completa con:

```javascript
function parseCSV(text) {
    const lines = text.trim().split(/\r?\n/);
    if (lines.length < 2) {
        alert('CSV vacío o inválido');
        return [];
    }
    
    // Auto-detect delimiter
    const header = lines[0];
    const delimiter = header.includes(';') ? ';' : ',';
    
    console.log(`CSV Delimiter detected: "${delimiter}"`);
    
    const headers = lines[0].split(delimiter).map(h => h.trim());
    console.log('CSV Headers:', headers);
    
    // Mapeo de headers esperados
    const headerMap = {
        id: ['tradeid', 'id'],
        instrument: ['instrument'],
        entryTime: ['entrytime', 'entry_time'],
        exitTime: ['exittime', 'exit_time'],
        type: ['type', 'direction'],
        entryPrice: ['entryprice', 'entry_price'],
        exitPrice: ['exitprice', 'exit_price'],
        result: ['result', 'exit_reason'],
        pnl: ['pnl', 'profit'],
        mae: ['mae'],
        mfe: ['mfe'],
        setup: ['setup']
    };
    
    // Encontrar índices de columnas
    const columnIndices = {};
    Object.entries(headerMap).forEach(([key, possibleNames]) => {
        const index = headers.findIndex(h => 
            possibleNames.includes(h.toLowerCase())
        );
        if (index !== -1) {
            columnIndices[key] = index;
        }
    });
    
    console.log('Column Indices:', columnIndices);
    
    // Parse trades
    const trades = [];
    
    for (let i = 1; i < lines.length; i++) {
        const line = lines[i].trim();
        if (!line) continue;
        
        const cols = line.split(delimiter);
        
        try {
            const trade = {
                id: cols[columnIndices.id] || `trade_${i}`,
                instrument: cols[columnIndices.instrument] || 'Unknown',
                entryTime: new Date(cols[columnIndices.entryTime]),
                exitTime: new Date(cols[columnIndices.exitTime]),
                type: cols[columnIndices.type] || 'Long',
                entryPrice: parseFloat(cols[columnIndices.entryPrice]),
                exitPrice: parseFloat(cols[columnIndices.exitPrice]),
                result: cols[columnIndices.result] || 'Closed',
                pnl: parseFloat(cols[columnIndices.pnl]),
                mae: parseFloat(cols[columnIndices.mae]) || 0,
                mfe: parseFloat(cols[columnIndices.mfe]) || 0,
                setup: cols[columnIndices.setup] || 'Unknown'
            };
            
            // Validar
            if (isNaN(trade.pnl) || isNaN(trade.entryTime.getTime())) {
                console.warn(`Skipping invalid trade at line ${i}:`, cols);
                continue;
            }
            
            trades.push(trade);
        } catch (err) {
            console.error(`Error parsing line ${i}:`, err, cols);
        }
    }
    
    console.log(`Parsed ${trades.length} trades from ${lines.length - 1} lines`);
    return trades;
}
```

### Testing

1. **Test con CSV del Paso 1**:
   - Refresh TradeAnalyzer
   - Cargar CSV
   - Verificar console: "Parsed X trades from Y lines"

2. **Test con delimiter `;`**:
   - Editar CSV manualmente, cambiar `,` por `;`
   - Cargar CSV
   - Verificar que detecta `;` y carga correctamente

### Validación

- [ ] Parser detecta delimitador correcto
- [ ] Todos los trades se parsean
- [ ] No hay warnings de "invalid trade"
- [ ] Console muestra mensaje correcto

### Rollback
```javascript
// Restaurar versión anterior desde backup
```

---

## ✅ PASO 4: Auto-Discovery Multi-Instrumento (3-4h)

### Objetivo
Botón para auto-cargar múltiples CSVs de diferentes instrumentos.

### Archivos a Modificar
1. `TradeAnalyzer/index.html`
2. `TradeAnalyzer/script.js`

### Código a Agregar

**En `index.html`** (después de línea ~23):

```html
<div class="header-buttons">
    <!-- NUEVO BOTÓN -->
    <button id="auto-load-btn" class="secondary-btn" onclick="autoDiscoverInstruments()">
        📂 Auto-Load All Instruments
    </button>
    
    <!-- Botones existentes -->
    <button id="add-files-btn" class="secondary-btn" style="display:none;">+ Add Data</button>
    ...
</div>
```

**En `script.js`** (al final del archivo):

```javascript
// ===================================================================
// AUTO-DISCOVERY MULTI-INSTRUMENTO
// ===================================================================

async function autoDiscoverInstruments() {
    try {
        const dirHandle = await window.showDirectoryPicker({
            mode: 'read'
        });
        
        const csvFiles = [];
        
        for await (const entry of dirHandle.values()) {
            if (entry.kind === 'file' && 
                entry.name.startsWith('trades_export_') && 
                entry.name.endsWith('.csv')) {
                
                const file = await entry.getFile();
                csvFiles.push({
                    name: entry.name,
                    file: file,
                    instrument: entry.name.replace('trades_export_', '').replace('.csv', '').replace(/_/g, ' ')
                });
            }
        }
        
        if (csvFiles.length === 0) {
            alert('No se encontraron archivos trades_export_*.csv');
            return;
        }
        
        console.log(`Found ${csvFiles.length} CSVs:`, csvFiles.map(f => f.instrument));
        
        await loadMultipleInstruments(csvFiles);
        
    } catch (err) {
        if (err.name !== 'AbortError') {
            console.error('Auto-discovery error:', err);
            alert('Error. Usa Chrome/Edge 86+ con File System Access API.');
        }
    }
}

async function loadMultipleInstruments(csvFiles) {
    let totalNew = 0;
    let totalUpdated = 0;
    
    for (const csvFile of csvFiles) {
        console.log(`Loading ${csvFile.instrument}...`);
        
        const text = await csvFile.file.text();
        const trades = parseCSV(text);
        
        if (trades.length === 0) {
            console.warn(`No trades in ${csvFile.name}`);
            continue;
        }
        
        // Merge con globalAllTrades
        trades.forEach(t => {
            const exists = globalAllTrades.find(existing => 
                existing.id === t.id && 
                existing.entryTime.getTime() === t.entryTime.getTime()
            );
            
            if (!exists) {
                globalAllTrades.push(t);
                totalNew++;
            } else {
                Object.assign(exists, t);
                totalUpdated++;
            }
        });
    }
    
    if (globalAllTrades.length > 0) {
        console.log(`Multi-load complete: ${totalNew} new, ${totalUpdated} updated`);
        saveData();
        populateFilters(globalAllTrades);
        applyFilters();
        dashboard.classList.remove('hidden');
        dropZone.style.display = 'none';
        
        alert(`Loaded ${csvFiles.length} instruments:\n${totalNew} new trades, ${totalUpdated} updated.`);
    }
}
```

### Testing

1. **Preparar múltiples CSVs**:
   - Ejecutar backtest con MNQ
   - Ejecutar backtest con MES
   - Ejecutar backtest con MYM
   - Verificar 3 CSVs en carpeta TradeAnalyzer

2. **Test Auto-Load**:
   - Abrir TradeAnalyzer
   - Click "📂 Auto-Load All Instruments"
   - Seleccionar carpeta TradeAnalyzer
   - Verificar alert: "Loaded 3 instruments: X new trades..."

3. **Verificar consolidación**:
   - Dashboard muestra todos los trades
   - Filtro "Instrument" muestra: MNQ, MES, MYM
   - No hay duplicados

### Validación

- [ ] Botón visible en UI
- [ ] Detecta múltiples CSVs
- [ ] Carga sin duplicados
- [ ] Filtro de instrumento funciona

### Rollback
```bash
git checkout TradeAnalyzer/
```

---

## ✅ PASO 5: Implementar Audit Stats (4-5h)

### Objetivo
Calcular T-Test, Monte Carlo, Sharpe Ratio en tab "Audit & Edge".

### Archivos a Modificar
1. `TradeAnalyzer/script.js`

### Código a Agregar

**Ubicación**: Al final de `script.js`

```javascript
// ===================================================================
// AUDIT STATISTICS
// ===================================================================

function calculateAuditStats(trades) {
    if (trades.length < 10) {
        return {
            message: 'Need at least 10 trades for statistical significance'
        };
    }
    
    const pnls = trades.map(t => t.pnl);
    
    // T-Test (one-sample, testing if mean > 0)
    const tTest = calculateTTest(pnls);
    
    // Monte Carlo simulation
    const monteCarlo = runMonteCarlo(pnls, 1000);
    
    // Sharpe Ratio
    const sharpe = calculateSharpeRatio(pnls);
    
    return {
        tTest: tTest,
        monteCarlo: monteCarlo,
        sharpeRatio: sharpe
    };
}

function calculateTTest(values) {
    const n = values.length;
    const mean = values.reduce((a,b) => a+b, 0) / n;
    const variance = values.reduce((sq, v) => sq + Math.pow(v - mean, 2), 0) / (n - 1);
    const stdError = Math.sqrt(variance / n);
    
    const tStatistic = mean / stdError;
    const degreesOfFreedom = n - 1;
    
    // P-value approximation (two-tailed)
    const pValue = 2 * (1 - tDistributionCDF(Math.abs(tStatistic), degreesOfFreedom));
    
    return {
        tStatistic: tStatistic,
        pValue: pValue,
        significant: pValue < 0.05,
        mean: mean,
        stdError: stdError
    };
}

// Approximation of t-distribution CDF
function tDistributionCDF(t, df) {
    // Simplified approximation (for large df, approaches normal distribution)
    if (df > 30) {
        return normalCDF(t);
    }
    // For small df, use a rough approximation
    const x = df / (df + t * t);
    return 1 - 0.5 * Math.pow(x, df / 2);
}

function normalCDF(z) {
    return 0.5 * (1 + erf(z / Math.sqrt(2)));
}

function erf(x) {
    // Approximation of error function
    const sign = x >= 0 ? 1 : -1;
    x = Math.abs(x);
    
    const a1 = 0.254829592;
    const a2 = -0.284496736;
    const a3 = 1.421413741;
    const a4 = -1.453152027;
    const a5 = 1.061405429;
    const p = 0.3275911;
    
    const t = 1.0 / (1.0 + p * x);
    const y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.exp(-x * x);
    
    return sign * y;
}

function runMonteCarlo(pnls, iterations) {
    const results = [];
    
    for (let i = 0; i < iterations; i++) {
        // Shuffle pnls
        const shuffled = [...pnls].sort(() => Math.random() - 0.5);
        
        // Calculate cumulative PnL
        let cumulative = 0;
        let maxDrawdown = 0;
        let peak = 0;
        
        shuffled.forEach(pnl => {
            cumulative += pnl;
            if (cumulative > peak) peak = cumulative;
            const drawdown = peak - cumulative;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;
        });
        
        results.push({
            finalPnL: cumulative,
            maxDrawdown: maxDrawdown
        });
    }
    
    // Stats de simulaciones
    results.sort((a, b) => a.finalPnL - b.finalPnL);
    
    const percentile5 = results[Math.floor(iterations * 0.05)].finalPnL;
    const percentile95 = results[Math.floor(iterations * 0.95)].finalPnL;
    const median = results[Math.floor(iterations * 0.5)].finalPnL;
    
    const avgMaxDD = results.reduce((sum, r) => sum + r.maxDrawdown, 0) / iterations;
    
    return {
        percentile5: percentile5,
        percentile95: percentile95,
        median: median,
        avgMaxDrawdown: avgMaxDD
    };
}

function calculateSharpeRatio(pnls) {
    const mean = pnls.reduce((a,b) => a+b, 0) / pnls.length;
    const variance = pnls.reduce((sq, v) => sq + Math.pow(v - mean, 2), 0) / (pnls.length - 1);
    const stdDev = Math.sqrt(variance);
    
    if (stdDev === 0) return 0;
    
    const dailySharpe = mean / stdDev;
    const annualizedSharpe = dailySharpe * Math.sqrt(252);
    
    return {
        daily: dailySharpe,
        annualized: annualizedSharpe
    };
}

// Renderizar en tab Audit
function renderAuditStats() {
    const filtered = applyCurrentFilters(); // Usa filtros actuales
    const stats = calculateAuditStats(filtered);
    
    const container = document.getElementById('audit-stats-content');
    
    if (stats.message) {
        container.innerHTML = `<p>${stats.message}</p>`;
        return;
    }
    
    let html = '<h3>Statistical Edge Analysis</h3>';
    
    // T-Test
    html += '<div class="stat-card">';
    html += '<h4>T-Test (Is profit statistically significant?)</h4>';
    html += `<p>T-Statistic: <strong>${stats.tTest.tStatistic.toFixed(3)}</strong></p>`;
    html += `<p>P-Value: <strong>${stats.tTest.pValue.toFixed(4)}</strong></p>`;
    html += `<p>Result: ${stats.tTest.significant ? 
        '<span class="success">✅ Statistically Significant (p < 0.05)</span>' : 
        '<span class="warning">⚠️ Not Significant (p ≥ 0.05)</span>'}</p>`;
    html += '</div>';
    
    // Monte Carlo
    html += '<div class="stat-card">';
    html += '<h4>Monte Carlo Simulation (1000 runs)</h4>';
    html += `<p>5th Percentile: ${formatCurrency(stats.monteCarlo.percentile5)}</p>`;
    html += `<p>Median: ${formatCurrency(stats.monteCarlo.median)}</p>`;
    html += `<p>95th Percentile: ${formatCurrency(stats.monteCarlo.percentile95)}</p>`;
    html += `<p>Avg Max Drawdown: ${formatCurrency(stats.monteCarlo.avgMaxDrawdown)}</p>`;
    html += '</div>';
    
    // Sharpe
    html += '<div class="stat-card">';
    html += '<h4>Sharpe Ratio</h4>';
    html += `<p>Daily: <strong>${stats.sharpeRatio.daily.toFixed(3)}</strong></p>`;
    html += `<p>Annualized: <strong>${stats.sharpeRatio.annualized.toFixed(3)}</strong></p>`;
    html += `<p>Rating: ${stats.sharpeRatio.annualized > 2 ? '🏆 Excellent' : 
                          stats.sharpeRatio.annualized > 1 ? '✅ Good' : 
                          '🟡 Acceptable'}</p>`;
    html += '</div>';
    
    container.innerHTML = html;
}

// Hook en tab switch
document.querySelector('[onclick="switchTab(\'audit\')"]').addEventListener('click', () => {
    renderAuditStats();
});
```

### Testing

1. **Abrir tab Audit & Edge**:
   - Cargar CSV con al menos 50 trades
   - Click tab "Audit & Edge"
   - Verificar que se calculan stats

2. **Verificar cálculos**:
   - T-Test: p-value < 0.05 si hay edge real
   - Monte Carlo: Percentiles razonables
   - Sharpe: > 1.0 es bueno

### Validación

- [ ] Tab Audit muestra stats
- [ ] T-Test calcula correctamente
- [ ] Monte Carlo completa 1000 runs
- [ ] Sharpe Ratio es positivo (si hay profit)

### Rollback
```bash
git diff script.js  # Ver cambios
git checkout script.js  # Restaurar si falla
```

---

# 📊 Checkpoint Fase 1

**Antes de continuar a Fase 2, verificar**:

- [ ] ✅ CSV se exporta desde NinjaTrader
- [ ] ✅ TradeAnalyzer sin código duplicado
- [ ] ✅ Parser CSV robusto funciona
- [ ] ✅ Auto-load múltiples instrumentos
- [ ] ✅ Audit Stats calculándose

**Si TODO funciona → Continuar a Fase 2**  
**Si algo falla → Revisar paso específico antes de continuar**

---

# 🚀 FASE 2: Data Enrichment (54-63h)

## ✅ PASO 6: Agregar Contexto de Sesión (3-4h)

### Objetivo
Capturar sesión (Asia/Europe/USA), edad del nivel, nivel origen/destino.

### Archivos a Modificar
1. `SessionLevelsStrategy.cs`

### Código a Agregar

*(Continuará con PASO 6, 7, 8... en siguiente sección)*

---

## 📝 Notas para el Usuario

### Cómo Usar Esta Guía

1. **Lee el paso completo** antes de empezar
2. **Haz backup** de archivos antes de modificar
3. **Modifica código exacto** indicado
4. **Compila y testea** después de cada paso
5. **NO continúes** si algo falla - arregla primero
6. **Documenta** en changelog qué hiciste

### Si Algo Falla

1. Lee el error completo
2. Verifica código copiado exacto
3. Usa rollback del paso
4. Pregúntame específicamente qué falló

### Progreso Tracking

Marca aquí tu progreso:

- [ ] PASO 1: Export CSV Básico
- [ ] PASO 2: Refactoring TradeAnalyzer
- [ ] PASO 3: Parser CSV Robusto
- [ ] PASO 4: Auto-Discovery Multi-Instrumento
- [ ] PASO 5: Audit Stats
- [ ] Checkpoint Fase 1 ✅

---

**¿Listo para empezar con PASO 1?**
