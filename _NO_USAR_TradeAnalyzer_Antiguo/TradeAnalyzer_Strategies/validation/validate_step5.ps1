# Validación PASO 5: Audit Stats
# Verifica T-Test, Monte Carlo, Sharpe Ratio

Write-Host "`n=== VALIDANDO PASO 5: Audit Stats ===" -ForegroundColor Cyan
Write-Host "Verificando estadísticas de Audit & Edge...`n" -ForegroundColor Gray

$errors = @()
$warnings = @()

# 1. Verificar funciones en script.js
Write-Host "1. Verificando funciones de stats en script.js..." -ForegroundColor Cyan
$scriptPath = "$env:USERPROFILE\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeAnalyzer\script.js"
$scriptContent = Get-Content $scriptPath -Raw

$requiredFunctions = @(
    "calculateAuditStats",
    "calculateTTest",
    "runMonteCarlo",
    "calculateSharpeRatio"
)

foreach ($func in $requiredFunctions) {
    if ($scriptContent -match "function\s+$func\s*\(") {
        Write-Host "   ✅ $func presente" -ForegroundColor Green
    } else {
        Write-Host "   ❌ $func faltante" -ForegroundColor Red
        $errors += "Función $func no encontrada"
    }
}

# 2. Verificar funciones auxiliares matemáticas
Write-Host "`n2. Verificando funciones matemáticas auxiliares..." -ForegroundColor Cyan

$mathFunctions = @(
    "tDistributionCDF",
    "normalCDF",
    "erf"
)

foreach ($func in $mathFunctions) {
    if ($scriptContent -match "function\s+$func\s*\(") {
        Write-Host "   ✅ $func presente" -ForegroundColor Green
    } else {
        Write-Host "   ⚠️ $func faltante (aproximaciones matemáticas)" -ForegroundColor Yellow
        $warnings += "Función $func podría estar faltante"
    }
}

# 3. Verificar integración con tab Audit
Write-Host "`n3. Verificando integración con tab Audit..." -ForegroundColor Cyan

if ($scriptContent -match "renderAuditStats|audit.*stats|switchTab.*audit") {
    Write-Host "   ✅ Renderización de stats presente" -ForegroundColor Green
} else {
    Write-Host "   ❌ Función renderAuditStats faltante" -ForegroundColor Red
    $errors += "Integración con tab Audit no encontrada"
}

# 4. Verificar elementos UI en index.html
Write-Host "`n4. Verificando elementos UI en index.html..." -ForegroundColor Cyan
$indexPath = "$env:USERPROFILE\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeAnalyzer\index.html"
$indexContent = Get-Content $indexPath -Raw

if ($indexContent -match 'tab.*audit|audit.*tab') {
    Write-Host "   ✅ Tab 'Audit & Edge' presente" -ForegroundColor Green
} else {
    Write-Host "   ⚠️ Tab 'Audit & Edge' podría faltar" -ForegroundColor Yellow
    $warnings += "Verifica que existe el tab 'Audit & Edge'"
}

if ($indexContent -match 'audit-stats|stats.*content') {
    Write-Host "   ✅ Contenedor para stats presente" -ForegroundColor Green
} else {
    Write-Host "   ⚠️ Contenedor para renderizar stats podría faltar" -ForegroundColor Yellow
    $warnings += "Necesitas un div con id para mostrar stats"
}

# 5. Verificar que hay datos para testear
Write-Host "`n5. Verificando datos disponibles para testing..." -ForegroundColor Cyan
$csvDir = "$env:USERPROFILE\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeAnalyzer"
$csvFiles = Get-ChildItem -Path $csvDir -Filter "trades_export_*.csv" -ErrorAction SilentlyContinue

if ($csvFiles.Count -gt 0) {
    $csv = $csvFiles[0]
    $content = Get-Content $csv.FullName
    $tradeCount = $content.Count - 1
    
    if ($tradeCount -ge 10) {
        Write-Host "   ✅ CSV con $tradeCount trades (suficiente para stats)" -ForegroundColor Green
    } elseif ($tradeCount -gt 0) {
        Write-Host "   ⚠️ CSV con solo $tradeCount trades (mínimo 10 recomendado)" -ForegroundColor Yellow
        $warnings += "Pocas trades para estadísticas significativas"
    } else {
        Write-Host "   ⚠️ CSV sin trades" -ForegroundColor Yellow
        $warnings += "Ejecuta backtest con datos para generar trades"
    }
} else {
    Write-Host "   ⚠️ No hay CSVs de trades" -ForegroundColor Yellow
    $warnings += "Necesitas trades para testear las estadísticas"
}

# 6. Instrucciones de testing manual
Write-Host "`n6. Testing Manual Requerido:" -ForegroundColor Yellow
Write-Host "   a) Asegúrate de tener al menos 50 trades en CSV (para stats confiables)" -ForegroundColor Gray
Write-Host ""
Write-Host "   b) Abre TradeAnalyzer (index.html)" -ForegroundColor Gray
Write-Host "      - Carga CSV con trades" -ForegroundColor Gray
Write-Host "      - Click tab 'Audit & Edge'" -ForegroundColor Gray
Write-Host ""
Write-Host "   c) Verifica que se muestran:" -ForegroundColor Gray
Write-Host "      - T-Test:" -ForegroundColor Gray
Write-Host "        • T-Statistic (número)" -ForegroundColor Gray
Write-Host "        • P-Value (< 0.05 = significativo)" -ForegroundColor Gray
Write-Host "        • Resultado: ✅ Significant o ⚠️ Not Significant" -ForegroundColor Gray
Write-Host ""
Write-Host "      - Monte Carlo:" -ForegroundColor Gray
Write-Host "        • 5th Percentile (peor caso)" -ForegroundColor Gray
Write-Host "        • Median" -ForegroundColor Gray
Write-Host "        • 95th Percentile (mejor caso)" -ForegroundColor Gray
Write-Host "        • Avg Max Drawdown" -ForegroundColor Gray
Write-Host ""
Write-Host "      - Sharpe Ratio:" -ForegroundColor Gray
Write-Host "        • Daily Sharpe" -ForegroundColor Gray
Write-Host "        • Annualized Sharpe" -ForegroundColor Gray
Write-Host "        • Rating: 🏆 Excellent / ✅ Good / 🟡 Acceptable" -ForegroundColor Gray
Write-Host ""
Write-Host "   d) Console (F12) debe mostrar:" -ForegroundColor Gray
Write-Host "      - NO errores" -ForegroundColor Gray
Write-Host "      - Mensajes de log pueden aparecer" -ForegroundColor Gray
Write-Host ""
Write-Host "   e) Validación de valores:" -ForegroundColor Gray
Write-Host "      - Si tienes edge real → P-Value < 0.05" -ForegroundColor Gray
Write-Host "      - Sharpe > 1.0 = bueno" -ForegroundColor Gray
Write-Host "      - Percentiles de Monte Carlo dentro de rango esperado" -ForegroundColor Gray

# Resumen
Write-Host "`n" + ("=" * 60) -ForegroundColor Cyan
if ($errors.Count -eq 0 -and $warnings.Count -eq 0) {
    Write-Host "✅ PASO 5 VALIDACIÓN AUTOMÁTICA EXITOSA" -ForegroundColor Green
    Write-Host "Funciones de Audit Stats detectadas en código." -ForegroundColor Green
    Write-Host "`n⚠️ IMPORTANTE: Realiza testing manual en tab Audit & Edge." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "🎉 Si todo funciona, ¡FASE 1 COMPLETA!" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Checkpoint Fase 1:" -ForegroundColor Yellow
    Write-Host "  ✅ Export CSV desde NinjaTrader" -ForegroundColor Green
    Write-Host "  ✅ TradeAnalyzer refactorizado" -ForegroundColor Green
    Write-Host "  ✅ Parser CSV robusto" -ForegroundColor Green
    Write-Host "  ✅ Multi-instrumento funcionando" -ForegroundColor Green
    Write-Host "  ✅ Audit Stats implementadas" -ForegroundColor Green
    Write-Host ""
    Write-Host "Puedes decidir:" -ForegroundColor Cyan
    Write-Host "  A) Usar el sistema como está" -ForegroundColor Gray
    Write-Host "  B) Continuar con Fase 2 (Order Flow, etc.)" -ForegroundColor Gray
    exit 0
} elseif ($errors.Count -eq 0) {
    Write-Host "⚠️ PASO 5 VALIDADO CON ADVERTENCIAS" -ForegroundColor Yellow
    foreach ($warn in $warnings) {
        Write-Host "  • $warn" -ForegroundColor Yellow
    }
    Write-Host "`nRealiza testing manual antes de considerar Fase 1 completa." -ForegroundColor Cyan
    exit 0
} else {
    Write-Host "❌ PASO 5 FALLÓ VALIDACIÓN" -ForegroundColor Red
    foreach ($err in $errors) {
        Write-Host "  • $err" -ForegroundColor Red
    }
    Write-Host "`n⚠️ Arregla los errores antes de considerar Fase 1 completa." -ForegroundColor Red
    exit 1
}
