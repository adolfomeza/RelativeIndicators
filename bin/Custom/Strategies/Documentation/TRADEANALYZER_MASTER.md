# TradeAnalyzer: Guía Completa de Implementación

> **Objetivo**: Implementar sistema completo de análisis de backtests y trades en vivo  
> **Fecha**: 2025-12-30  
> **Versión**: 2.0 Consolidado

---

## 📊 Resumen del Sistema

TradeAnalyzer es una aplicación web que permite:
1. **Importar** trades desde CSVs exportados por NinjaTrader
2. **Analizar** performance con métricas avanzadas
3. **Detectar** patrones y zonas problemáticas
4. **Comparar** múltiples instrumentos
5. **Recibir** recomendaciones automáticas

### Estado Actual

| Componente | Estado | Ubicación |
|------------|--------|-----------|
| **Web App** | ✅ Funcional (v1.3) | `TradeAnalyzer/index.html` |
| **Export CSV** | ❌ No implementado | `SessionLevelsStrategy.cs` |
| **MAE/MFE Tracking** | ❌ No implementado | `SessionLevelsStrategy.cs` |
| **Audit Stats** | ⚠️ Parcial | `TradeAnalyzer/script.js` |
| **Multi-Instrumento** | ⚠️ UI lista, falta dashboard | `TradeAnalyzer/` |

---

## 🎯 Plan de Implementación Paso a Paso

### FASE 1: Export CSV desde NinjaTrader (PRIORIDAD ALTA)
**Duración**: 3-4 horas  
**Objetivo**: Que SessionLevelsStrategy exporte trades automáticamente

#### Paso 1.1: Agregar Variables de Tracking

Agregar en `SessionLevelsStrategy.cs` (~línea 160):

```csharp
// =========================================================
// TRADE ANALYZER EXPORT (v1.13.0)
// =========================================================
private double tradeMAE = 0;      // Maximum Adverse Excursion
private double tradeMFE = 0;      // Maximum Favorable Excursion
private double tradeEntryPrice = 0;
private DateTime tradeEntryTime;
private string tradeSetupName = "";
private string tradeDirection = "";
private int tradeId = 0;           // Auto-incrementing ID

// CSV Export Path
private string csvExportPath = "";
```

#### Paso 1.2: Inicializar CSV al Inicio

En `OnStateChange` → `State.DataLoaded`:

```csharp
// Initialize CSV Export
string safeInstrument = Instrument.FullName.Replace("/", "-").Replace(":", "-");
csvExportPath = System.IO.Path.Combine(
    NinjaTrader.Core.Globals.UserDataDir,
    "trace",
    "TradeAnalyzer",
    $"trades_export_{safeInstrument}.csv"
);

// Create directory if needed
string dir = System.IO.Path.GetDirectoryName(csvExportPath);
if (!System.IO.Directory.Exists(dir))
    System.IO.Directory.CreateDirectory(dir);

// Write header if file doesn't exist
if (!System.IO.File.Exists(csvExportPath))
{
    string header = "ID,Instrument,EntryTime,Type,EntryPrice,ExitTime,ExitPrice,Result,PnL,MAE,MFE,Setup";
    System.IO.File.WriteAllText(csvExportPath, header + Environment.NewLine);
    Log("CSV EXPORT: Created " + csvExportPath);
}
```

#### Paso 1.3: Tracking MAE/MFE en OnBarUpdate

```csharp
// En OnBarUpdate, cuando hay posición activa:
if (Position.MarketPosition != MarketPosition.Flat)
{
    double unrealizedPnL = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
    
    // Update MAE (worst point)
    if (unrealizedPnL < tradeMAE)
        tradeMAE = unrealizedPnL;
    
    // Update MFE (best point)
    if (unrealizedPnL > tradeMFE)
        tradeMFE = unrealizedPnL;
}
```

#### Paso 1.4: Inicializar al Entrar Trade

En `OnExecutionUpdate`, cuando se llena la entrada:

```csharp
if (execution.Order.Name.Contains("Entry") && execution.Order.OrderState == OrderState.Filled)
{
    tradeId++;
    tradeEntryPrice = execution.Order.AverageFillPrice;
    tradeEntryTime = Time[0];
    tradeDirection = Position.MarketPosition == MarketPosition.Long ? "Long" : "Short";
    tradeSetupName = setupLevelName; // Ya existente
    tradeMAE = 0;
    tradeMFE = 0;
}
```

#### Paso 1.5: Exportar al Cerrar Trade

En `OnExecutionUpdate`, cuando se llena SL/TP:

```csharp
if (execution.Order.Name.Contains("TP") || execution.Order.Name.Contains("SL") || 
    execution.Order.Name.Contains("Close"))
{
    double exitPrice = execution.Order.AverageFillPrice;
    double pnl = execution.Order.AverageFillPrice - tradeEntryPrice;
    if (tradeDirection == "Short") pnl = -pnl;
    pnl *= execution.Quantity * Instrument.MasterInstrument.PointValue;
    
    string resultName = execution.Order.Name; // "TP1_Long", "SL_Short", etc.
    
    // Format CSV line
    string line = string.Format("{0},{1},{2:yyyy-MM-dd HH:mm:ss},{3},{4},{5:yyyy-MM-dd HH:mm:ss},{6},{7},{8:F2},{9:F2},{10:F2},{11}",
        tradeId,
        Instrument.FullName,
        tradeEntryTime,
        tradeDirection,
        tradeEntryPrice,
        Time[0],
        exitPrice,
        resultName,
        pnl,
        tradeMAE,
        tradeMFE,
        tradeSetupName
    );
    
    // Append to CSV
    try
    {
        System.IO.File.AppendAllText(csvExportPath, line + Environment.NewLine);
        Log("CSV EXPORT: Trade #" + tradeId + " PnL=" + pnl.ToString("F2"));
    }
    catch (Exception ex)
    {
        Log("CSV EXPORT ERROR: " + ex.Message);
    }
}
```

#### ✅ Verificación Paso 1
- [ ] Ejecutar 3-5 trades en Playback
- [ ] Verificar que existe `trace/TradeAnalyzer/trades_export_MNQ.csv`
- [ ] Abrir CSV y confirmar columnas correctas

---

### FASE 2: Limpiar TradeAnalyzer Web (PRIORIDAD MEDIA)
**Duración**: 1-2 horas  
**Objetivo**: Eliminar código duplicado y completar Audit Stats

#### Paso 2.1: Unificar JavaScript

1. Copiar contenido inline de `index.html` (líneas 272-1340) a `script.js`
2. Eliminar `<script>...</script>` inline de `index.html`
3. Agregar referencia externa: `<script src="script.js"></script>`
4. Actualizar versión a v1.4

#### Paso 2.2: Verificar Audit Stats

El código ya tiene `MathUtils.tTestInfo()` y `shuffle()`. Solo falta conectar con la UI:

```javascript
// En processData(), agregar al final:
updateAuditStats(trades);

function updateAuditStats(trades) {
    const pnls = trades.map(t => t.pnl);
    
    // T-Test
    const ttest = MathUtils.tTestInfo(pnls);
    document.getElementById('audit-ttest').textContent = ttest.t;
    document.getElementById('audit-ttest-desc').textContent = 
        ttest.significant ? "✅ Edge Significativo (p<0.05)" : "⚠️ No Significativo";
    
    // Monte Carlo (1000 shuffles)
    let betterCount = 0;
    const realSum = pnls.reduce((a,b) => a+b, 0);
    for (let i = 0; i < 1000; i++) {
        const shuffled = MathUtils.shuffle([...pnls]);
        const shuffledSum = shuffled.slice(0, Math.floor(pnls.length/2)).reduce((a,b) => a+b, 0);
        if (shuffledSum >= realSum) betterCount++;
    }
    const luckProb = (betterCount / 1000 * 100).toFixed(1);
    document.getElementById('audit-montecarlo').textContent = luckProb + "%";
    document.getElementById('audit-montecarlo-desc').textContent = 
        luckProb < 5 ? "✅ Resultados NO son suerte" : "⚠️ Posible suerte";
    
    // Sharpe Ratio
    const mean = MathUtils.mean(pnls);
    const stdDev = MathUtils.stdDev(pnls);
    const sharpe = stdDev > 0 ? (mean / stdDev * Math.sqrt(252)).toFixed(2) : "N/A";
    document.getElementById('audit-sharpe').textContent = sharpe;
    
    // MAE/MFE
    const avgMFE = MathUtils.mean(trades.map(t => t.mfe || 0));
    const avgMAE = MathUtils.mean(trades.map(t => t.mae || 0));
    const efficiency = avgMFE > 0 ? (realSum / (avgMFE * trades.length) * 100).toFixed(1) : "N/A";
    
    document.getElementById('audit-mfe').textContent = formatCurrency(avgMFE);
    document.getElementById('audit-mae').textContent = formatCurrency(avgMAE);
    document.getElementById('audit-efficiency').textContent = efficiency + "%";
}
```

#### ✅ Verificación Paso 2
- [ ] Abrir `index.html` en Chrome
- [ ] Cargar CSV de Paso 1 o usar `playback.csv`
- [ ] Verificar Tab "Audit & Edge" muestra valores reales

---

### FASE 3: Hacer Backtest y Exportar (LISTO PARA USAR)
**Duración**: 30 min  
**Objetivo**: Obtener datos reales para análisis

#### Paso 3.1: Configurar Backtest en NinjaTrader

1. Abrir Strategy Analyzer
2. Seleccionar `SessionLevelsStrategy`
3. Configurar:
   - Instrumento: MNQ 03-26
   - Período: 3 meses (Oct-Dic 2025)
   - Datos: 1 min
4. Ejecutar

#### Paso 3.2: Verificar CSV Exportado

```powershell
# Verificar que el CSV existe
Get-ChildItem "$env:USERPROFILE\Documents\NinjaTrader 8\trace\TradeAnalyzer\"

# Ver primeras líneas
Get-Content "...\trades_export_MNQ.csv" -Head 10
```

#### Paso 3.3: Cargar en TradeAnalyzer

1. Abrir `TradeAnalyzer/index.html`
2. Arrastrar el CSV al drop zone
3. Explorar:
   - **Overview**: Equity curve, KPIs
   - **Time Analysis**: Patrones horarios
   - **Advanced**: MAE/MFE scatter
   - **Audit & Edge**: Edge estadístico

---

### FASE 4: Dashboard Multi-Instrumento (OPCIONAL)
**Duración**: 4-5 horas  
**Objetivo**: Comparar MNQ vs MES vs MGC etc.

Agregar Tab "Portfolio" con:
- Tabla comparativa de performance
- Equity curves superpuestas
- Insights automáticos (mejor/peor performer)
- Recomendaciones de allocation

*Ver código completo en sección de implementación avanzada abajo.*

---

## 📋 Checklist de Implementación

### Fase 1: Export CSV ⏳
- [ ] Agregar variables de tracking (MAE/MFE)
- [ ] Inicializar CSV en OnStateChange
- [ ] Trackear MAE/MFE en OnBarUpdate
- [ ] Exportar trade al cerrar posición
- [ ] Probar con 5 trades en Playback

### Fase 2: Limpiar Web App ⏳
- [ ] Unificar script.js (eliminar inline)
- [ ] Implementar updateAuditStats()
- [ ] Probar Tab Audit & Edge
- [ ] Verificar filtros funcionan

### Fase 3: Backtest ⏳
- [ ] Ejecutar backtest 3 meses MNQ
- [ ] Verificar CSV creado
- [ ] Cargar en TradeAnalyzer
- [ ] Analizar resultados

### Fase 4: Multi-Instrumento (Opcional) ⏳
- [ ] Agregar Tab Portfolio
- [ ] Implementar PortfolioAnalyzer class
- [ ] Probar con 3 instrumentos

---

## 🔬 Métricas Clave a Analizar

### Básicas
| Métrica | Descripción | Objetivo |
|---------|-------------|----------|
| **Win Rate** | % trades ganadores | >60% |
| **Profit Factor** | Gross Profit / Gross Loss | >1.5 |
| **Expected Value** | Promedio PnL por trade | >$50 |
| **Max Drawdown** | Peor pérdida desde equity peak | <20% |

### Avanzadas
| Métrica | Descripción | Uso |
|---------|-------------|-----|
| **Sharpe Ratio** | Retorno ajustado por riesgo | >1.0 bueno, >2.0 excelente |
| **T-Test** | Significancia estadística del edge | p<0.05 = edge real |
| **Exit Efficiency** | PnL / MFE capturado | >60% ideal |
| **MAE Ratio** | Avg MAE / Avg Win | <0.5 = SL bien calibrado |

### Patrones a Buscar
1. **Dead Zones horarias**: Horas con Win Rate <50%
2. **Días débiles**: Días con PF <1.0
3. **Instrumentos perdedores**: PnL negativo sostenido
4. **SL muy ajustado**: MAE >>  típico de trades perdedores

---

## 🚀 Próximos Pasos Recomendados

1. **HOY**: Implementar Fase 1 (Export CSV) - 3 horas
2. **MAÑANA**: Fase 2 (Limpiar Web) + Fase 3 (Backtest) - 2 horas
3. **DESPUÉS**: Analizar resultados y ajustar estrategia

---

## 📂 Estructura de Archivos

```
NinjaTrader 8/
├── bin/Custom/Strategies/
│   ├── SessionLevelsStrategy.cs  (agregar export)
│   ├── TradeAnalyzer/
│   │   ├── index.html            (UI principal)
│   │   ├── script.js             (lógica JS)
│   │   ├── style.css             (estilos)
│   │   ├── backtest_data.js      (datos ejemplo)
│   │   └── validation/           (scripts test)
│   └── Documentation/
│       └── TRADEANALYZER_MASTER.md (este archivo)
└── trace/
    └── TradeAnalyzer/
        ├── trades_export_MNQ.csv
        ├── trades_export_MES.csv
        └── trades_export_MGC.csv
```

---

**Autor**: Gemini Antigravity  
**Última actualización**: 2025-12-30
