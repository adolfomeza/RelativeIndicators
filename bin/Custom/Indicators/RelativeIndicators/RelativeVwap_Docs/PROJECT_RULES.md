# Reglas del Proyecto - RelativeVwap

Este documento sirve como punto central de verdad para el indicador **RelativeVwap**.

## Referencias Oficiales
- **Documentación de NinjaTrader 8 API**: [https://developer.ninjatrader.com/docs/desktop](https://developer.ninjatrader.com/docs/desktop)

## Descripción del Indicador
**RelativeVwap** es un indicador avanzado de VWAP que incluye:
- VWAP anclado a extremos de sesión (High/Low)
- Señales de trading basadas en separación del VWAP
- Niveles relativos para identificar zonas de valor
- Integración con SessionLevelsStrategy

## Archivo Principal
- **RelativeVwap.cs** (~143K bytes)

## Reglas de Desarrollo
1.  **Versionado**: Seguir SemVer (Major.Minor.Patch) y registrar cambios en `CHANGELOG_ES.md`.
2.  **Backups**: Antes de cambios críticos, crear backup con extensión `.bak`.
3.  **Commits Automáticos**: Cada cambio debe ser registrado en CHANGELOG y subido a GitHub automáticamente.
4.  **Idioma**: Todos los documentos `.md` deben estar en **ESPAÑOL**.
5.  **Sincronización de Versión**: La constante de versión en el código debe coincidir con `CHANGELOG_ES.md`.

---

## Prompt de Contexto para AI Assistant

> **Copiar y pegar este bloque al inicio de cada nueva conversación:**

```
Tu rol hoy es de:
1. **Experto Programador en NinjaScript** para NinjaTrader 8
2. **Experto Quant** en trading algorítmico y análisis de datos

### Contexto del Proyecto
Estamos trabajando en **RelativeVwap.cs**, un indicador avanzado de VWAP que:
- Calcula VWAP anclado a extremos de sesión (High/Low del día)
- Genera señales de trading basadas en separación del VWAP
- Dibuja niveles relativos para identificar zonas de valor
- Se integra con SessionLevelsStrategy para proporcionar datos de VWAP

### Archivos Clave
- **Indicador principal**: `RelativeVwap.cs`
- **Reglas del proyecto**: `RelativeVwap_Docs/PROJECT_RULES.md`
- **Historial de cambios**: `RelativeVwap_Docs/CHANGELOG_ES.md`
- **API Referencia**: https://developer.ninjatrader.com/docs/desktop

### Instrucciones
1. Lee y mantén en memoria las reglas de `PROJECT_RULES.md`
2. Actualiza `CHANGELOG_ES.md` con cada cambio de código
3. Sigue versionado SemVer
4. Commits automáticos a GitHub tras cada cambio funcional
5. Todos los documentos deben estar en ESPAÑOL
```
