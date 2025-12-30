# Análisis del Indicador RelativeDVAPVA

## Resumen
Este documento analiza la estructura actual del indicador.
*Nota: A partir de la versión v2.5.16, se han eliminado todas las funcionalidades relacionadas con zonas PVA, señales BPB/RPB, y señales IPB/EF, dejando únicamente el cálculo de VWAP y Bandas DVA.*

---

## COMPONENTE 1: VWAP y Bandas de Desviación Estándar

### Propósito
Calcular el VWAP de sesión y sus bandas de desviación estándar.

### Variables Principales
- `sessionVWAP` - VWAP de la sesión actual
- `offset[0]` - Desviación estándar calculada
- `UpperBand1/2/3` - Bandas superiores (1SD, 2SD, 3SD)
- `LowerBand1/2/3` - Bandas inferiores (-1SD, -2SD, -3SD)

### Flujo
1. Al inicio de sesión, se resetean los cálculos
2. Cada barra se acumula volumen y se recalcula VWAP
3. Las bandas se dibujan como plots

---

## COMPONENTE 2: Zonas PVA Simplificadas (Reintroducido en v2.5.17)

### Propósito
Visualizar el área de valor de la sesión anterior (DVAH/DVAL) extendida en el tiempo.

### Lógica
- **Creación**: Al inicio de una nueva sesión (`sessionBar == 1`), captura el `UpperBand1` y `LowerBand1` de la sesión anterior.
- **Visualización**:
  - Dibuja dos líneas horizontales (**Rays**) hacia el futuro.
  - Dibuja un rectángulo de relleno transparente entre las líneas.
- **Mitigación**:
  - Si el precio cruza más del 50% (variable `pvaCutoffPercent`) del ancho de la zona, la zona se considera "mitigada".
  - **Comportamiento al Mitigar**: El dibujo de la zona se corta visualmente al final de la sesión donde ocurrió la mitigación, dejando de ser infinita.

---

*Documento actualizado: 2025-12-30*
*Versión actual del indicador: v2.5.20*
