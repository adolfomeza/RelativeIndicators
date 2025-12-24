# Reglas del Proyecto y Referencias

Este documento sirve como punto central de verdad para enlaces importantes, reglas de desarrollo y referencias para cualquier agente o desarrollador que trabaje en este proyecto.

## Referencias Oficiales
-   **Documentación de NinjaTrader 8 API:** [https://developer.ninjatrader.com/docs/desktop](https://developer.ninjatrader.com/docs/desktop)
    *Consultar esta guía para dudas sobre clases, métodos y eventos de NinjaScript.*

## Reglas de Desarrollo
1.  **Versionado:** Seguir SemVer (Major.Minor.Patch) y registrar cambios en `CHANGELOG.md` y `CHANGELOG_ES.md`.
2.  **Backups:** Antes de cambios críticos, asegurar que el código funcional esté commiteado en Git.
3.  **Verificación antes de Push:** NO subir cambios a GitHub (`git push`) hasta que el usuario haya confirmado que el cambio funciona correctamente en simulación.
