# Validación Completa: Todos los Pasos de Fase 1
# Ejecuta validación de PASO 1 a PASO 5 secuencialmente

Write-Host @"

╔═══════════════════════════════════════════════════════════════╗
║                                                               ║
║          VALIDACIÓN COMPLETA - FASE 1 FOUNDATION              ║
║                                                               ║
╚═══════════════════════════════════════════════════════════════╝

"@ -ForegroundColor Cyan

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$allPassed = $true
$results = @()

# Lista de pasos a validar
$steps = @(
    @{Number=1; Name="Export CSV Básico"; Script="validate_step1.ps1"},
    @{Number=2; Name="Refactoring TradeAnalyzer"; Script="validate_step2.ps1"},
    @{Number=3; Name="Parser CSV Robusto"; Script="validate_step3.ps1"},
    @{Number=4; Name="Auto-Discovery Multi-Instrumento"; Script="validate_step4.ps1"},
    @{Number=5; Name="Audit Stats"; Script="validate_step5.ps1"}
)

foreach ($step in $steps) {
    Write-Host "`n" + ("─" * 60) -ForegroundColor Gray
    Write-Host "Ejecutando validación PASO $($step.Number): $($step.Name)" -ForegroundColor Cyan
    Write-Host ("─" * 60) -ForegroundColor Gray
    
    $scriptPath = Join-Path $scriptDir $step.Script
    
    if (-not (Test-Path $scriptPath)) {
        Write-Host "❌ Script de validación no encontrado: $scriptPath" -ForegroundColor Red
        $results += @{
            Step = $step.Number
            Name = $step.Name
            Status = "MISSING"
            ExitCode = -1
        }
        $allPassed = $false
        continue
    }
    
    # Ejecutar script de validación
    try {
        & $scriptPath
        $exitCode = $LASTEXITCODE
        
        if ($exitCode -eq 0) {
            $results += @{
                Step = $step.Number
                Name = $step.Name
                Status = "PASSED"
                ExitCode = $exitCode
            }
            Write-Host "`n✅ PASO $($step.Number) VALIDADO" -ForegroundColor Green
        } else {
            $results += @{
                Step = $step.Number
                Name = $step.Name
                Status = "FAILED"
                ExitCode = $exitCode
            }
            $allPassed = $false
            Write-Host "`n❌ PASO $($step.Number) FALLÓ" -ForegroundColor Red
            
            $continue = Read-Host "`n¿Continuar con siguiente paso? (S/N)"
            if ($continue -ne 'S' -and $continue -ne 's') {
                Write-Host "`nValidación detenida por el usuario." -ForegroundColor Yellow
                break
            }
        }
    } catch {
        Write-Host "`n❌ Error al ejecutar validación: $_" -ForegroundColor Red
        $results += @{
            Step = $step.Number
            Name = $step.Name
            Status = "ERROR"
            ExitCode = -1
        }
        $allPassed = $false
    }
}

# Resumen final
Write-Host "`n`n" + ("═" * 60) -ForegroundColor Cyan
Write-Host "                    RESUMEN DE VALIDACIÓN                    " -ForegroundColor Cyan
Write-Host ("═" * 60) -ForegroundColor Cyan

foreach ($result in $results) {
    $statusColor = switch ($result.Status) {
        "PASSED" { "Green" }
        "FAILED" { "Red" }
        "MISSING" { "Yellow" }
        "ERROR" { "Red" }
        default { "Gray" }
    }
    
    $statusIcon = switch ($result.Status) {
        "PASSED" { "✅" }
        "FAILED" { "❌" }
        "MISSING" { "⚠️" }
        "ERROR" { "❌" }
        default { "?" }
    }
    
    Write-Host "$statusIcon PASO $($result.Step): $($result.Name)" -ForegroundColor $statusColor
}

Write-Host "`n" + ("═" * 60) -ForegroundColor Cyan

if ($allPassed -and $results.Count -eq 5) {
    Write-Host @"

    🎉 ¡FELICITACIONES! 🎉
    
    FASE 1: FOUNDATION COMPLETADA EXITOSAMENTE
    
    Todos los pasos validados correctamente:
    ✅ Export CSV desde NinjaTrader
    ✅ TradeAnalyzer refactorizado
    ✅ Parser CSV robusto
    ✅ Multi-instrumento funcionando
    ✅ Audit Stats implementadas
    
    El sistema está listo para usar.
    
    Próximos pasos:
    - Usa el TradeAnalyzer para analizar tus trades
    - Decide si continuar con Fase 2 (Order Flow, etc.)
    
"@ -ForegroundColor Green
} else {
    $passedCount = ($results | Where-Object {$_.Status -eq "PASSED"}).Count
    Write-Host @"

    ⚠️ VALIDACIÓN INCOMPLETA
    
    Pasos validados: $passedCount / $($results.Count)
    
    Revisa los pasos que fallaron y corrígelos antes de continuar.
    
"@ -ForegroundColor Yellow
}

Write-Host ("═" * 60) -ForegroundColor Cyan
Write-Host ""

if ($allPassed) {
    exit 0
} else {
    exit 1
}
