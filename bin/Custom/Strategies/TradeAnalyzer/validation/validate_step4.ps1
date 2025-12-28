# Validación PASO 4: Auto-Discovery Multi-Instrumento
# Verifica botón auto-load y capacidad multi-instrumento

Write-Host "`n=== VALIDANDO PASO 4: Auto-Discovery Multi-Instrumento ===" -ForegroundColor Cyan
Write-Host "Verificando funcionalidad multi-instrumento...`n" -ForegroundColor Gray

$errors = @()
$warnings = @()

# 1. Verificar botón en index.html
Write-Host "1. Verificando botón Auto-Load en index.html..." -ForegroundColor Cyan
$indexPath = "$env:USERPROFILE\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeAnalyzer\index.html"
$indexContent = Get-Content $indexPath -Raw

if ($indexContent -match 'auto-load.*button|button.*auto.*load|showDirectoryPicker') {
    Write-Host "   ✅ Botón Auto-Load presente en HTML" -ForegroundColor Green
} else {
    Write-Host "   ❌ Botón Auto-Load faltante" -ForegroundColor Red
    $errors += "Botón 'Auto-Load Instruments' no encontrado en index.html"
}

# 2. Verificar funciones en script.js
Write-Host "`n2. Verificando funciones multi-instrumento en script.js..." -ForegroundColor Cyan
$scriptPath = "$env:USERPROFILE\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeAnalyzer\script.js"
$scriptContent = Get-Content $scriptPath -Raw

$requiredFunctions = @(
    "autoDiscoverInstruments",
    "loadMultipleInstruments",
    "showDirectoryPicker"
)

foreach ($func in $requiredFunctions) {
    if ($scriptContent -match $func) {
        Write-Host "   ✅ $func presente" -ForegroundColor Green
    } else {
        Write-Host "   ❌ $func faltante" -ForegroundColor Red
        $errors += "Función $func no encontrada"
    }
}

# 3. Verificar múltiples CSVs de instrumentos (si existen)
Write-Host "`n3. Verificando CSVs de múltiples instrumentos..." -ForegroundColor Cyan
$csvDir = "$env:USERPROFILE\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeAnalyzer"
$csvFiles = Get-ChildItem -Path $csvDir -Filter "trades_export_*.csv" -ErrorAction SilentlyContinue

if ($csvFiles.Count -gt 1) {
    Write-Host "   ✅ Múltiples instrumentos detectados: $($csvFiles.Count)" -ForegroundColor Green
    foreach ($csv in $csvFiles) {
        $instrument = $csv.Name -replace 'trades_export_', '' -replace '.csv', '' -replace '_', ' '
        Write-Host "      📄 $instrument" -ForegroundColor Gray
    }
} elseif ($csvFiles.Count -eq 1) {
    Write-Host "   ⚠️ Solo 1 instrumento encontrado" -ForegroundColor Yellow
    Write-Host "      Para probar multi-instrumento, ejecuta backtests con varios instrumentos:" -ForegroundColor Gray
    Write-Host "      - MNQ, MES, MYM, etc." -ForegroundColor Gray
    $warnings += "Solo 1 CSV disponible para testing"
} else {
    Write-Host "   ⚠️ No hay CSVs de instrumentos" -ForegroundColor Yellow
    $warnings += "Ejecuta backtests primero para generar CSVs"
}

# 4. Verificar lógica de merge sin duplicados
Write-Host "`n4. Verificando lógica anti-duplicados..." -ForegroundColor Cyan
if ($scriptContent -match 'getTradeKey|tradeMap|exists.*find') {
    Write-Host "   ✅ Lógica de detección de duplicados presente" -ForegroundColor Green
} else {
    Write-Host "   ⚠️ Lógica anti-duplicados podría estar faltante" -ForegroundColor Yellow
    $warnings += "Verifica que no se dupliquen trades al cargar múltiples veces"
}

# 5. Verificar filtro de instrumento en UI
Write-Host "`n5. Verificando filtro de instrumento..." -ForegroundColor Cyan
if ($indexContent -match 'instrument.*filter|filter.*instrument' -or 
    $scriptContent -match 'populateFilters.*instrument|instrument.*dropdown|instrument.*select') {
    Write-Host "   ✅ Filtro de instrumento detectado" -ForegroundColor Green
} else {
    Write-Host "   ⚠️ Filtro de instrumento podría faltar" -ForegroundColor Yellow  
    $warnings += "Verifica que existe dropdown para filtrar por instrumento"
}

# 6. Instrucciones de testing manual
Write-Host "`n6. Testing Manual Requerido:" -ForegroundColor Yellow
Write-Host "   a) Genera CSVs de varios instrumentos:" -ForegroundColor Gray
Write-Host "      - Ejecuta Strategy Analyzer con MNQ" -ForegroundColor Gray
Write-Host "      - Ejecuta Strategy Analyzer con MES" -ForegroundColor Gray
Write-Host "      - Ejecuta Strategy Analyzer con MYM" -ForegroundColor Gray
Write-Host ""
Write-Host "   b) Abre TradeAnalyzer (index.html)" -ForegroundColor Gray
Write-Host ""
Write-Host "   c) Click botón '📂 Auto-Load All Instruments'" -ForegroundColor Gray
Write-Host "      - Selecciona carpeta TradeAnalyzer" -ForegroundColor Gray
Write-Host "      - Verifica alert: 'Loaded X instruments'" -ForegroundColor Gray
Write-Host ""
Write-Host "   d) Verifica consolidación:" -ForegroundColor Gray
Write-Host "      - Dashboard muestra stats de todos los instrumentos" -ForegroundColor Gray
Write-Host "      - Filtro 'Instrument' muestra: MNQ, MES, MYM, etc." -ForegroundColor Gray
Write-Host "      - Cambia filtro y verifica que stats cambian" -ForegroundColor Gray
Write-Host ""
Write-Host "   e) Console (F12) debe mostrar:" -ForegroundColor Gray
Write-Host "      - 'Found X CSVs'" -ForegroundColor Gray
Write-Host "      - 'Multi-load complete: Y new, Z updated'" -ForegroundColor Gray
Write-Host "      - NO errores" -ForegroundColor Gray

# Resumen
Write-Host "`n" + ("=" * 60) -ForegroundColor Cyan
if ($errors.Count -eq 0 -and $warnings.Count -eq 0) {
    Write-Host "✅ PASO 4 VALIDACIÓN AUTOMÁTICA EXITOSA" -ForegroundColor Green
    Write-Host "Multi-instrumento detectado en código." -ForegroundColor Green
    Write-Host "`n⚠️ IMPORTANTE: Realiza testing manual con múltiples CSVs." -ForegroundColor Yellow
    Write-Host "Si funciona correctamente, continúa con PASO 5." -ForegroundColor Cyan
    exit 0
} elseif ($errors.Count -eq 0) {
    Write-Host "⚠️ PASO 4 VALIDADO CON ADVERTENCIAS" -ForegroundColor Yellow
    foreach ($warn in $warnings) {
        Write-Host "  • $warn" -ForegroundColor Yellow
    }
    Write-Host "`nRealiza testing manual antes de continuar." -ForegroundColor Cyan
    exit 0
} else {
    Write-Host "❌ PASO 4 FALLÓ VALIDACIÓN" -ForegroundColor Red
    foreach ($err in $errors) {
        Write-Host "  • $err" -ForegroundColor Red
    }
    Write-Host "`n⚠️ Arregla los errores antes de continuar." -ForegroundColor Red
    exit 1
}
