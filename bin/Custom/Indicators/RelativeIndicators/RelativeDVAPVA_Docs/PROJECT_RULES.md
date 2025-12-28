# Reglas del Proyecto - RelativeDVAPVA

Este documento sirve como punto central de verdad para el indicador **RelativeDVAPVA**.

## Referencias Oficiales
- **Documentación de NinjaTrader 8 API**: [https://developer.ninjatrader.com/docs/desktop](https://developer.ninjatrader.com/docs/desktop)

## Descripción del Indicador
**RelativeDVAPVA** (Developing Value Area / Prior Value Area) es un indicador avanzado que incluye:
- VWAP de sesión con bandas de desviación estándar (SD 0.5, 1, 1.5, 2, 3)
- Zonas DVA (Developing Value Area) y PVA (Prior Value Area)
- Señales de trading: IPB, EF, BPB, RPB
- Botones de estado en la barra de herramientas del chart
- Integración con Market Analyzer

## Archivo Principal
- **RelativeDVAPVA.cs** (~218K bytes, 4816 líneas)

## Reglas de Desarrollo
1.  **Versionado**: Seguir SemVer (Major.Minor.Patch) y registrar cambios en `CHANGELOG_ES.md`.
2.  **Backups**: Antes de cambios críticos, crear backup con extensión `.bak`.
3.  **Commits Automáticos**: Cada cambio debe ser registrado en CHANGELOG y subido a GitHub automáticamente.
4.  **Idioma**: Todos los documentos `.md` deben estar en **ESPAÑOL**.
5.  **Sincronización de Versión**: La constante `versionString` en el código debe coincidir con `CHANGELOG_ES.md`.

---

## Prompt de Contexto para AI Assistant

> **Copiar y pegar este bloque al inicio de cada nueva conversación:**

```
Tu rol hoy es de:
1. **Experto Programador en NinjaScript** para NinjaTrader 8
2. **Experto Quant** en trading algorítmico y análisis de datos

### Contexto del Proyecto
Estamos trabajando en **RelativeDVAPVA.cs**, un indicador avanzado que:
- Calcula VWAP de sesión con bandas de desviación estándar
- Dibuja zonas DVA/PVA para identificar áreas de valor
- Genera señales IPB (Initial Push Back), EF (Exhaustion Failure), BPB (Breakout Pullback), RPB (Rejection Pullback)
- Muestra botones de estado en la barra de herramientas

### Archivos Clave
- **Indicador principal**: `RelativeDVAPVA.cs`
- **Reglas del proyecto**: `RelativeDVAPVA_Docs/PROJECT_RULES.md`
- **Historial de cambios**: `RelativeDVAPVA_Docs/CHANGELOG_ES.md`
- **API Referencia**: https://developer.ninjatrader.com/docs/desktop

### Instrucciones
1. Lee y mantén en memoria las reglas de `PROJECT_RULES.md`
2. Actualiza `CHANGELOG_ES.md` con cada cambio de código
3. Sigue versionado SemVer
4. Commits automáticos a GitHub tras cada cambio funcional
5. Todos los documentos deben estar en ESPAÑOL
```
