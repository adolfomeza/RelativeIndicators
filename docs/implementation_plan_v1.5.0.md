# Plan de Control de Versiones (Versioning)

Para llevar un control numérico "de verdad", propongo implementar **Semantic Versioning (SemVer)** y un historial de cambios (**Changelog**).

## 1. Estándar de Versionado (SemVer)
Usaremos el formato `vMajor.Minor.Patch` (ej. `v1.5.0`):
-   **Major (1.x.x):** Cambios grandes que rompen compatibilidad o lógica principal.
-   **Minor (x.5.x):** Nuevas funcionalidades (features) que no rompen lo anterior.
-   **Patch (x.x.1):** Arreglos de errores (bugs) o ajustes pequeños.

## 2. Archivo CHANGELOG.md
Crearé un archivo `CHANGELOG.md` en tu carpeta de documentación (Brain) que registrará cada cambio.
Formato:
```markdown
## [1.5.0] - 2025-12-23
### Added
- Targets dinámicos que persiguen al VWAP.
### Fixed
- Lógica de mitigación corregida.
```

## 3. Integración en el Código
Modificaré `SessionLevelsStrategy.cs` para usar este sistema.
-   Cambiar `private const string StrategyVersion` por propiedades estructuradas o un string limpio que coincida con el Changelog.
-   Agregar la fecha de compilación o modificación en el panel de info para saber si es la última versión.

## 4. (Opcional) Git Local
Si quieres seguridad total, puedo inicializar un repositorio Git en tu carpeta `Strategies`. Esto te permite "viajar en el tiempo" a cualquier versión anterior. **¿Te interesa esto también?**

## Pasos Inmediatos
1.  Crear `CHANGELOG.md`.
2.  Actualizar la versión actual a `v1.5.0` (por la funcionalidad dinámica recién agregada).
3.  Reflejar esto en el panel visual.
