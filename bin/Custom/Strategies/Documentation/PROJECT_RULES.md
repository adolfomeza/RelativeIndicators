# Reglas del Proyecto y Referencias

Este documento sirve como punto central de verdad para enlaces importantes, reglas de desarrollo y referencias para cualquier agente o desarrollador que trabaje en este proyecto.

## Referencias Oficiales
- **Documentación de NinjaTrader 8 API**: [https://developer.ninjatrader.com/docs/desktop](https://developer.ninjatrader.com/docs/desktop)
    *Consultar esta guía para dudas sobre clases, métodos y eventos de NinjaScript.

## Reglas de Desarrollo
1.  **Versionado**: Seguir SemVer (Major.Minor.Patch) y registrar cambios en `CHANGELOG.md` y `CHANGELOG_ES.md`.
2.  **Backups**: Antes de cambios críticos, asegurar que el código funcional esté commiteado en Git.
3.  **Commits Automáticos**: Cada cambio de código debe ser registrado inmediatamente en el CHANGELOG con nueva versión y subido a GitHub (`git add`, `git commit`, `git push`) **automáticamente sin esperar autorización del usuario**. Esto permite usar el historial de GitHub para debugging.
4.  **Extensiones de Backup**: AL crear backups en la carpeta local, SIEMPRE usar la extensión `.bak` (ej. `Estrategia.bak`) para evitar errores de compilación por clases duplicadas.
5.  **Mantenimiento de Documentación**: Mantener SIEMPRE actualizados los archivos de la carpeta `Documentation` (`CHANGELOG_ES.md`, `analisis_session_levels.md`, `PROJECT_RULES.md`) en sintonía con cada cambio de código realizado.
6.  **Idioma de Documentación**: Todos los documentos con extensión `.md` deben estar escritos en **ESPAÑOL** sin excepción.
7.  **Sincronización de Versión**: Asegurar que la constante de versión en el código (`StrategyVersion`) coincida *exactamente* con la última entrada en `CHANGELOG_ES.md` y se muestre en el panel de estado.
8.  **Limpieza de Artifacts**: Al completar exitosamente una implementación, mover todos los documentos temporales de planificación/implementación (de `.gemini/antigravity/brain/`) a una subcarpeta `completed/` para mantener el workspace limpio. Solo mantener abiertos documentos relevantes para trabajo actual.
9.  **Formato de Logs**: Todos los logs de debug DEBEN seguir este formato:
    - **Prefijo de instrumento**: `[MNQ]`, `[MGC]`, `[MCL]`, etc. al inicio de cada mensaje
    - **Timestamp**: Incluir `Time[0]` con fecha y hora para facilitar búsqueda
    - **Usar método `Log()`**: NUNCA usar `Print()` directamente. El método `Log()` ya incluye el prefijo automáticamente
    - Ejemplo: `[MNQ] 28/12/25 9:30:00 a.m. EXEC_DEBUG: Submitting Long Limit @ 25000`

---

## Prompt de Contexto para AI Assistant

> **Copiar y pegar este bloque al inicio de cada nueva conversación para establecer el contexto:**

```
Tu rol hoy es de:
1. **Experto Programador en NinjaScript** para NinjaTrader 8
2. **Experto Quant** en trading algorítmico y análisis de datos

### Contexto del Proyecto
Estamos trabajando en `SessionLevelsStrategy.cs`, una estrategia avanzada que:
- Detecta niveles de sesión (Asia, Europe, USA) con High/Low automáticos
- Usa VWAP anclado desde el momento del "touch" del nivel
- Implementa lógica A+ entry (confirmación de separación del VWAP)
- Gestiona órdenes en modo Unmanaged con TP1 (VWAP dinámico), TP2 (nivel opuesto) y SL único
- Soporta sizing dinámico basado en ATR y riesgo normalizado entre instrumentos
- Maneja niveles internos vs externos, invalidación, y retry logic

### Archivos Clave
- **Estrategia principal**: `SessionLevelsStrategy.cs` (~3600 líneas)
- **Reglas del proyecto**: `Documentation/PROJECT_RULES.md`
- **Historial de cambios**: `Documentation/CHANGELOG_ES.md`
- **API Referencia**: https://developer.ninjatrader.com/docs/desktop

### Instrucciones
1. Lee y mantén en memoria las reglas de `PROJECT_RULES.md`
2. Actualiza `CHANGELOG_ES.md` con cada cambio de código
3. Sigue versionado SemVer y sincroniza `StrategyVersion` con el changelog
4. Commits automáticos a GitHub tras cada cambio funcional
5. Todos los documentos deben estar en ESPAÑOL
**Limpieza de Artifacts**: Al completar exitosamente una implementación, mover todos los documentos temporales de planificación/implementación (de `.gemini/antigravity/brain/`) a una subcarpeta `completed/` para mantener el workspace limpio. Solo mantener abiertos documentos relevantes para trabajo actual.
```**Sincronización de Versión**: Asegurar que la constante de versión en el código (`StrategyVersion`) coincida *exactamente* con la última entrada en `CHANGELOG_ES.md` y se muestre en el panel de estado.
