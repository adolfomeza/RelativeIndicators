# Validación PASO 2: Refactoring TradeAnalyzer
# Verifica que no hay código duplicado y script.js funciona

Write-Host "`n=== VALIDANDO PASO 2: Refactoring TradeAnalyzer ===" -ForegroundColor Cyan
Write-Host "Verificando eliminación de código duplicado...`n" -ForegroundColor Gray

$errors = @()
$warnings = @()

# 1. Verificar estructura de archivos
Write-Host "1. Verificando archivos..." -ForegroundColor Cyan
$baseDir = "$env:USERPROFILE\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeAnalyzer"

$requiredFiles = @("index.html", "script.js", "style.css")
foreach ($file in $requiredFiles) {
    $path = Join-Path $baseDir $file
    Write-Host "   Verificando $file..." -NoNewline
    if (Test-Path $path) {
        Write-Host " ✅" -ForegroundColor Green
    } else {
        Write-Host " ❌" -ForegroundColor Red
        $errors += "$file no existe"
    }
}

if ($errors.Count -gt 0) {
    Write-Host "`n❌ Archivos faltantes. Verifica la estructura." -ForegroundColor Red
    exit 1
}

# 2. Verificar que index.html NO tiene código JavaScript inline largo
Write-Host "`n2. Verificando que index.html no tiene código duplicado..." -ForegroundColor Cyan
$indexPath = Join-Path $baseDir "index.html"
$indexContent = Get-Content $indexPath -Raw

# Buscar bloques <script> largos (más de 100 líneas indica código inline)
$scriptBlocks = [regex]::Matches($indexContent, '(?s)<script[^>]*>(.*?)</script>')
$hasLargeInlineScript = $false

foreach ($block in $scriptBlocks) {
    $scriptContent = $block.Groups[1].Value
    $lines = ($scriptContent -split "`n").Count
    
    # Si el script tiene más de 100 líneas Y no es solo un import
    if ($lines -gt 100 -and $scriptContent -notmatch '^\s*$') {
        $hasLargeInlineScript = $true
        Write-Host "   ⚠️ Bloque <script> inline con $lines líneas detectado" -ForegroundColor Yellow
    }
}

if (-not $hasLargeInlineScript) {
    Write-Host "   ✅ Sin código JavaScript inline largo" -ForegroundColor Green
} else {
    Write-Host "   ❌ Código JavaScript duplicado en index.html" -ForegroundColor Red
    $errors += "index.html todavía tiene código inline (debería estar en script.js)"
}

# 3. Verificar que script.js existe y tiene contenido
Write-Host "`n3. Verificando script.js..." -ForegroundColor Cyan
$scriptPath = Join-Path $baseDir "script.js"
$scriptContent = Get-Content $scriptPath -Raw

$scriptLines = ($scriptContent -split "`n").Count
Write-Host "   Líneas en script.js: $scriptLines" -ForegroundColor Gray

if ($scriptLines -gt 100) {
    Write-Host "   ✅ script.js tiene contenido sustancial" -ForegroundColor Green
} else {
    Write-Host "   ⚠️ script.js parece vacío o incompleto ($scriptLines líneas)" -ForegroundColor Yellow
    $warnings += "script.js tiene pocas líneas, verifica que todo el código esté ahí"
}

# 4. Verificar que index.html referencia script.js
Write-Host "`n4. Verificando que index.html referencia script.js..." -ForegroundColor Cyan
if ($indexContent -match '<script[^>]*src\s*=\s*["\']script\.js["\']') {
    Write-Host "   ✅ index.html referencia script.js correctamente" -ForegroundColor Green
} else {
    Write-Host "   ❌ index.html NO referencia script.js" -ForegroundColor Red
    $errors += "Falta <script src='script.js'></script> en index.html"
}

# 5. Verificar funciones clave en script.js
Write-Host "`n5. Verificando funciones clave en script.js..." -ForegroundColor Cyan

$requiredFunctions = @("parseCSV", "applyFilters", "processData", "handleDrop")
$missingFunctions = @()

foreach ($func in $requiredFunctions) {
    if ($scriptContent -match "function\s+$func\s*\(") {
        Write-Host "   ✅ $func presente" -ForegroundColor Green
    } else {
        Write-Host "   ❌ $func faltante" -ForegroundColor Red
        $missingFunctions += $func
    }
}

if ($missingFunctions.Count -gt 0) {
    $errors += "Funciones faltantes en script.js: $($missingFunctions -join ', ')"
}

# Resumen
Write-Host "`n" + ("=" * 60) -ForegroundColor Cyan
if ($errors.Count -eq 0 -and $warnings.Count -eq 0) {
    Write-Host "✅ PASO 2 VALIDADO EXITOSAMENTE" -ForegroundColor Green
    Write-Host "Refactoring completado. Código sin duplicación." -ForegroundColor Green
    Write-Host "`n📝 TESTING MANUAL REQUERIDO:" -ForegroundColor Yellow
    Write-Host "   1. Abre index.html en Chrome/Edge" -ForegroundColor Gray
    Write-Host "   2. Arrastra un CSV de trades" -ForegroundColor Gray
    Write-Host "   3. Verifica que carga y muestra dashboard" -ForegroundColor Gray
    Write-Host "   4. Abre Console (F12) y verifica sin errores" -ForegroundColor Gray
    Write-Host "`nSi funciona, continúa con PASO 3." -ForegroundColor Cyan
    exit 0
} elseif ($errors.Count -eq 0) {
    Write-Host "⚠️ PASO 2 VALIDADO CON ADVERTENCIAS" -ForegroundColor Yellow
    foreach ($warn in $warnings) {
        Write-Host "  • $warn" -ForegroundColor Yellow
    }
    Write-Host "`nRealiza testing manual antes de continuar." -ForegroundColor Cyan
    exit 0
} else {
    Write-Host "❌ PASO 2 FALLÓ VALIDACIÓN" -ForegroundColor Red
    foreach ($err in $errors) {
        Write-Host "  • $err" -ForegroundColor Red
    }
    Write-Host "`n⚠️ Arregla los errores antes de continuar." -ForegroundColor Red
    exit 1
}
