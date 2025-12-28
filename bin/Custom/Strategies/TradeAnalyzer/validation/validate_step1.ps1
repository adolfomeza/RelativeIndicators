# Validación PASO 1: Export CSV Básico
# Verifica que SessionLevelsStrategy exporta trades correctamente

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "`n=== VALIDANDO PASO 1: Export CSV Basico ===" -ForegroundColor Cyan
Write-Host "Verificando export de trades desde NinjaTrader...`n" -ForegroundColor Gray

$errors = @()
$warnings = @()

# 1. Verificar carpeta TradeAnalyzer
Write-Host "1. Verificando carpeta TradeAnalyzer..." -NoNewline
$tradeAnalyzerPath = "$env:USERPROFILE\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeAnalyzer"
if (Test-Path $tradeAnalyzerPath) {
    Write-Host " ✅" -ForegroundColor Green
} else {
    Write-Host " ❌" -ForegroundColor Red
    $errors += "Carpeta TradeAnalyzer no existe en: $tradeAnalyzerPath"
    Write-Host "`n⚠️ CRÍTICO: Crea la carpeta antes de continuar" -ForegroundColor Red
    exit 1
}

# 2. Verificar archivos CSV de export
Write-Host "2. Buscando archivos CSV exportados..." -NoNewline
$csvFiles = Get-ChildItem -Path $tradeAnalyzerPath -Filter "trades_export_*.csv" -ErrorAction SilentlyContinue
if ($csvFiles.Count -gt 0) {
    Write-Host " ✅" -ForegroundColor Green
    Write-Host "   Encontrados: $($csvFiles.Count) archivo(s)" -ForegroundColor Gray
    foreach ($csv in $csvFiles) {
        Write-Host "   📄 $($csv.Name)" -ForegroundColor Gray
    }
} else {
    Write-Host " ❌" -ForegroundColor Red
    $errors += "No se encontraron CSVs con patrón 'trades_export_*.csv'"
    Write-Host "`n⚠️ Ejecuta un backtest primero para generar el CSV" -ForegroundColor Yellow
    exit 1
}

# 3. Validar estructura del primer CSV
$testCsv = $csvFiles[0]
Write-Host "`n3. Validando estructura de: $($testCsv.Name)" -ForegroundColor Cyan

$content = Get-Content $testCsv.FullName
if ($content.Count -eq 0) {
    Write-Host " ❌ CSV vacío" -ForegroundColor Red
    $errors += "CSV está vacío"
    exit 1
}

# Headers
$headers = $content[0]
Write-Host "   Headers encontrados: " -NoNewline
Write-Host "$headers" -ForegroundColor Gray

$requiredHeaders = @("TradeId", "Instrument", "EntryTime", "EntryPrice", "ExitTime", "ExitPrice", "Direction", "Profit", "MAE", "MFE", "SetupType")
$missingHeaders = @()

foreach ($header in $requiredHeaders) {
    if ($headers -notmatch $header) {
        $missingHeaders += $header
    }
}

if ($missingHeaders.Count -eq 0) {
    Write-Host "   ✅ Todos los headers requeridos presentes" -ForegroundColor Green
} else {
    Write-Host "   ❌ Headers faltantes: $($missingHeaders -join ', ')" -ForegroundColor Red
    $errors += "Headers faltantes en CSV"
}

# 4. Verificar datos
Write-Host "`n4. Validando datos en CSV..." -ForegroundColor Cyan

$tradeCount = $content.Count - 1
if ($tradeCount -gt 0) {
    Write-Host "   ✅ CSV contiene $tradeCount trade(s)" -ForegroundColor Green
} else {
    Write-Host "   ⚠️ CSV solo tiene headers, sin trades" -ForegroundColor Yellow
    $warnings += "CSV sin trades - ejecuta backtest con datos"
}

# 5. Validar primer trade (si existe)
if ($tradeCount -gt 0) {
    Write-Host "`n5. Validando formato del primer trade..." -ForegroundColor Cyan
    
    $delimiter = if ($headers -match ';') { ';' } else { ',' }
    $firstTrade = $content[1] -split $delimiter
    
    # TradeID (debe ser GUID)
    $tradeId = $firstTrade[0]
    if ($tradeId -match '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$') {
        Write-Host "   ✅ TradeID formato GUID correcto" -ForegroundColor Green
    } else {
        Write-Host "   ⚠️ TradeID no es GUID: $tradeId" -ForegroundColor Yellow
        $warnings += "TradeID no tiene formato GUID estándar"
    }
    
    # Timestamps (columnas 2 y 4)
    $entryTime = $firstTrade[2]
    $exitTime = $firstTrade[4]
    Write-Host "   Entry: $entryTime" -ForegroundColor Gray
    Write-Host "   Exit:  $exitTime" -ForegroundColor Gray
    
    try {
        $entryDate = [DateTime]::Parse($entryTime)
        $exitDate = [DateTime]::Parse($exitTime)
        Write-Host "   ✅ Timestamps parseables" -ForegroundColor Green
    } catch {
        Write-Host "   ❌ Timestamps no parseables" -ForegroundColor Red
        $errors += "Formato de timestamp inválido"
    }
    
    # Profit (PnL), MAE, MFE
    try {
        $pnl = [double]$firstTrade[7]
        $mae = [double]$firstTrade[8]
        $mfe = [double]$firstTrade[9]
        
        Write-Host "   PnL: $pnl" -ForegroundColor Gray
        Write-Host "   MAE: $mae" -ForegroundColor Gray
        Write-Host "   MFE: $mfe" -ForegroundColor Gray
        
        # MAE debe ser negativo o cero, MFE positivo o cero
        if ($mae -le 0) {
            Write-Host "   ✅ MAE negativo/cero (correcto)" -ForegroundColor Green
        } else {
            Write-Host "   ⚠️ MAE es positivo ($mae) - debería ser negativo" -ForegroundColor Yellow
            $warnings += "MAE positivo (inusual)"
        }
        
        if ($mfe -ge 0) {
            Write-Host "   ✅ MFE positivo/cero (correcto)" -ForegroundColor Green
        } else {
            Write-Host "   ⚠️ MFE es negativo ($mfe) - debería ser positivo" -ForegroundColor Yellow
            $warnings += "MFE negativo (inusual)"
        }
        
    } catch {
        Write-Host "   ❌ PnL/MAE/MFE no son números válidos" -ForegroundColor Red
        $errors += "Valores numéricos inválidos en PnL/MAE/MFE"
    }
}

# Resumen
Write-Host "`n" + ("=" * 60) -ForegroundColor Cyan
if ($errors.Count -eq 0 -and $warnings.Count -eq 0) {
    Write-Host "OK PASO 1 VALIDADO EXITOSAMENTE" -ForegroundColor Green
    Write-Host "El export CSV YA EXISTE y funciona correctamente." -ForegroundColor Green
    Write-Host "`nNOTA: Tu formato CSV es diferente al diseñado originalmente, pero es VALIDO." -ForegroundColor Yellow
    Write-Host "TradeAnalyzer se adaptara a tu formato existente." -ForegroundColor Yellow
    Write-Host "`nPuedes continuar con PASO 2 (Refactoring TradeAnalyzer)." -ForegroundColor Cyan
    exit 0
} elseif ($errors.Count -eq 0) {
    Write-Host "⚠️ PASO 1 VALIDADO CON ADVERTENCIAS" -ForegroundColor Yellow
    Write-Host "`nAdvertencias encontradas:" -ForegroundColor Yellow
    foreach ($warn in $warnings) {
        Write-Host "  • $warn" -ForegroundColor Yellow
    }
    Write-Host "`nPuedes continuar, pero revisa las advertencias." -ForegroundColor Cyan
    exit 0
} else {
    Write-Host "❌ PASO 1 FALLÓ VALIDACIÓN" -ForegroundColor Red
    Write-Host "`nErrores encontrados:" -ForegroundColor Red
    foreach ($err in $errors) {
        Write-Host "  • $err" -ForegroundColor Red
    }
    if ($warnings.Count -gt 0) {
        Write-Host "`nAdvertencias:" -ForegroundColor Yellow
        foreach ($warn in $warnings) {
            Write-Host "  • $warn" -ForegroundColor Yellow
        }
    }
    Write-Host "`n⚠️ Arregla los errores antes de continuar con PASO 2." -ForegroundColor Red
    exit 1
}
