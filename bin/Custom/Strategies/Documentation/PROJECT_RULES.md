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
