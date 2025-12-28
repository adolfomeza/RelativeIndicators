# Validación PASO 3: Parser CSV Robusto
# Verifica mejoras en el parser (auto-detect delimiter, headers flexibles)

Write-Host "`n=== VALIDANDO PASO 3: Parser CSV Robusto ===" -ForegroundColor Cyan
Write-Host "Verificando mejoras en parser CSV...`n" -ForegroundColor Gray

$errors = @()
$warnings = @()

# 1. Verificar que script.js tiene función parseCSV mejorada
Write-Host "1. Verificando función parseCSV en script.js..." -ForegroundColor Cyan
$scriptPath = "$env:USERPROFILE\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeAnalyzer\script.js"

if (-not (Test-Path $scriptPath)) {
    Write-Host "   ❌ script.js no existe" -ForegroundColor Red
    exit 1
}

$scriptContent = Get-Content $scriptPath -Raw

# Verificar auto-detección de delimiter
if ($scriptContent -match 'delimiter.*=.*header.*includes.*[;:]') {
    Write-Host "   ✅ Auto-detección de delimiter presente" -ForegroundColor Green
} else {
    Write-Host "   ❌ Auto-detección de delimiter faltante" -ForegroundColor Red
    $errors += "Parser no tiene auto-detección de delimiter"
}

# Verificar mapeo de headers flexibles
if ($scriptContent -match 'headerMap|possibleNames|findIndex') {
    Write-Host "   ✅ Mapeo flexible de headers presente" -ForegroundColor Green
} else {
    Write-Host "   ⚠️ Mapeo flexible de headers posiblemente faltante" -ForegroundColor Yellow
    $warnings += "Parser podría no tener mapeo flexible"
}

# Verificar manejo de errores
if ($scriptContent -match 'try\s*\{.*parseCSV.*\}.*catch|catch.*parseCSV') {
    Write-Host "   ✅ Manejo de errores en parsing" -ForegroundColor Green
} else {
    Write-Host "   ⚠️ Manejo de errores podría mejorar" -ForegroundColor Yellow
    $warnings += "Considera agregar try/catch en parseCSV"
}

# 2. Crear CSVs de test con diferentes formatos
Write-Host "`n2. Preparando CSVs de test..." -ForegroundColor Cyan
$testDir = "$env:USERPROFILE\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeAnalyzer\test"
if (-not (Test-Path $testDir)) {
    New-Item -ItemType Directory -Path $testDir -Force | Out-Null
}

# CSV con coma
$csvComma = @"
TradeID,Instrument,EntryTime,ExitTime,Type,EntryPrice,ExitPrice,Result,PnL,MAE,MFE,Setup
test-001,MNQ 03-26,2025-12-26T10:00:00,2025-12-26T10:30:00,Long,25800.00,25850.00,Closed,500.00,-50.00,600.00,Test Setup
"@
$csvComma | Out-File -FilePath (Join-Path $testDir "test_comma.csv") -Encoding UTF8
Write-Host "   ✅ test_comma.csv creado" -ForegroundColor Green

# CSV con punto y coma
$csvSemicolon = @"
TradeID;Instrument;EntryTime;ExitTime;Type;EntryPrice;ExitPrice;Result;PnL;MAE;MFE;Setup
test-002;MNQ 03-26;2025-12-26T11:00:00;2025-12-26T11:30:00;Short;25900.00;25850.00;Closed;500.00;-50.00;600.00;Test Setup
"@
$csvSemicolon | Out-File -FilePath (Join-Path $testDir "test_semicolon.csv") -Encoding UTF8
Write-Host "   ✅ test_semicolon.csv creado" -ForegroundColor Green

# CSV con headers variantes
$csvVariant = @"
id,instrument,entry_time,exit_time,direction,entryprice,exitprice,result,profit,mae,mfe,setup
test-003,MNQ 03-26,2025-12-26T12:00:00,2025-12-26T12:30:00,Long,25850.00,25900.00,Closed,500.00,-50.00,600.00,Test Setup
"@
$csvVariant | Out-File -FilePath (Join-Path $testDir "test_variant_headers.csv") -Encoding UTF8
Write-Host "   ✅ test_variant_headers.csv creado" -ForegroundColor Green

# 3. Instrucciones de testing manual
Write-Host "`n3. Testing Manual Requerido:" -ForegroundColor Yellow
Write-Host "   Los CSVs de test fueron creados en:" -ForegroundColor Gray
Write-Host "   $testDir" -ForegroundColor Cyan
Write-Host ""
Write-Host "   Realiza los siguientes tests:" -ForegroundColor Gray
Write-Host "   a) Abre TradeAnalyzer (index.html)" -ForegroundColor Gray
Write-Host "   b) Arrastra test_comma.csv → Verifica que carga" -ForegroundColor Gray
Write-Host "   c) Limpia datos, arrastra test_semicolon.csv → Verifica que carga" -ForegroundColor Gray
Write-Host "   d) Limpia datos, arrastra test_variant_headers.csv → Verifica que carga" -ForegroundColor Gray
Write-Host "   e) Verifica Console (F12):" -ForegroundColor Gray
Write-Host "      - 'CSV Delimiter detected' debe aparecer" -ForegroundColor Gray
Write-Host "      - 'Parsed X trades' debe aparecer" -ForegroundColor Gray
Write-Host "      - NO debe haber errores" -ForegroundColor Gray

# Resumen
Write-Host "`n" + ("=" * 60) -ForegroundColor Cyan
if ($errors.Count -eq 0 -and $warnings.Count -eq 0) {
    Write-Host "✅ PASO 3 VALIDACIÓN AUTOMÁTICA EXITOSA" -ForegroundColor Green
    Write-Host "Parser mejorado detectado en código." -ForegroundColor Green
    Write-Host "`n⚠️ IMPORTANTE: Realiza testing manual con los CSVs de test." -ForegroundColor Yellow
    Write-Host "Si todos los CSVs cargan correctamente, continúa con PASO 4." -ForegroundColor Cyan
    exit 0
} elseif ($errors.Count -eq 0) {
    Write-Host "⚠️ PASO 3 VALIDADO CON ADVERTENCIAS" -ForegroundColor Yellow
    foreach ($warn in $warnings) {
        Write-Host "  • $warn" -ForegroundColor Yellow
    }
    Write-Host "`nRealiza testing manual antes de continuar." -ForegroundColor Cyan
    exit 0
} else {
    Write-Host "❌ PASO 3 FALLÓ VALIDACIÓN" -ForegroundColor Red
    foreach ($err in $errors) {
        Write-Host "  • $err" -ForegroundColor Red
    }
    Write-Host "`n⚠️ Arregla los errores antes de continuar." -ForegroundColor Red
    exit 1
}
