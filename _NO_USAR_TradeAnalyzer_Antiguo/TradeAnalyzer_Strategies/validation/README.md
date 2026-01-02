# Scripts de Validación - TradeAnalyzer

Esta carpeta contiene scripts PowerShell para validar cada paso de la implementación.

## 📋 Scripts Disponibles

### Validaciones Individuales

- `validate_step1.ps1` - Export CSV Básico
- `validate_step2.ps1` - Refactoring TradeAnalyzer
- `validate_step3.ps1` - Parser CSV Robusto
- `validate_step4.ps1` - Auto-Discovery Multi-Instrumento
- `validate_step5.ps1` - Audit Stats

### Validación Completa

- `validate_all.ps1` - Ejecuta todos los pasos secuencialmente

## 🚀 Cómo Usar

### Validar un solo paso

```powershell
cd "C:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeAnalyzer\validation"
.\validate_step1.ps1
```

### Validar todos los pasos

```powershell
.\validate_all.ps1
```

## ✅ Interpretación de Resultados

### ✅ Verde = PASSED
- Todo funciona correctamente
- Puedes continuar al siguiente paso

### ⚠️ Amarillo = WARNING
- Funciona pero hay advertencias
- Revisa las advertencias antes de continuar
- OPCIONAL: continúa si aceptas los warnings

### ❌ Rojo = FAILED
- Algo no funciona
- OBLIGATORIO: arregla antes de continuar

## 📝 Qué Verifica Cada Script

### PASO 1: Export CSV Básico
- ✅ Carpeta TradeAnalyzer existe
- ✅ CSV de export existe
- ✅ Headers correctos
- ✅ Datos válidos (timestamps, PnL, MAE, MFE)

### PASO 2: Refactoring
- ✅ Sin código JavaScript duplicado
- ✅ script.js tiene contenido
- ✅ index.html referencia script.js
- ✅ Funciones clave presentes

### PASO 3: Parser CSV
- ✅ Auto-detección de delimiter
- ✅ Mapeo flexible de headers
- ✅ Manejo de errores
- ✅ Crea CSVs de test

### PASO 4: Multi-Instrumento
- ✅ Botón Auto-Load presente
- ✅ Funciones multi-instrumento
- ✅ Lógica anti-duplicados
- ✅ Filtro de instrumento

### PASO 5: Audit Stats
- ✅ Funciones T-Test, Monte Carlo, Sharpe
- ✅ Funciones matemáticas auxiliares
- ✅ Integración con tab Audit
- ✅ Elementos UI presentes

## 🔧 Troubleshooting

### "Script de validación no encontrado"
Verifica que estás en la carpeta correcta:
```powershell
cd "C:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\TradeAnalyzer\validation"
```

### "Execution Policy"
Si PowerShell bloquea los scripts:
```powershell
Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
```

### Permisos
Ejecuta PowerShell como Administrador si hay problemas de permisos.

## 📊 Testing Manual Adicional

Los scripts validan el **código**, pero algunos pasos requieren **testing manual**:

- **PASO 2**: Abrir TradeAnalyzer y verificar funcionalidad
- **PASO 3**: Cargar CSVs con diferentes formatos
- **PASO 4**: Probar botón Auto-Load con múltiples instrumentos
- **PASO 5**: Verificar que stats se calculan en tab Audit

## 💡 Tips

1. **Ejecuta después de cada implementación**: No esperes a terminar todo
2. **Lee la salida completa**: Los scripts dan detalles de qué falló
3. **Testing manual es clave**: Scripts solo verifican código, no funcionalidad completa
4. **Guarda logs**: Copia salida si necesitas ayuda

## 🎯 Objetivo

**Confianza total** antes de avanzar. Si el script dice ✅, puedes confiar que esa parte funciona.
