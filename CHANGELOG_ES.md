# Registro de Cambios (Changelog)

Todos los cambios notables en el proyecto `SessionLevelsStrategy` serán documentados en este archivo.

El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.0.0/),
y este proyecto adhiere a [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.5.2] - 2025-12-23
### Corregido
- **Error de Validación:** Se arregló el error "Quantity is 0" asignando un valor por defecto de `1`.

## [1.5.1] - 2025-12-23
### Agregado
- **Cauduras Locales:** Se agregó `EnableLocalScreenshots` para permitir guardar imágenes del gráfico en el disco sin necesidad de activar alertas por correo.
### Cambiado
- **Versión de Estrategia:** Actualizada a v1.5.1.

## [1.5.0] - 2025-12-23
### Agregado
- **Actualizaciones Dinámicas de TP:** Las órdenes objetivo (TP1/TP2) ahora ajustan su precio automáticamente para seguir al VWAP Global y a los Niveles de Sesión Opuestos si estos se mueven mientras la orden está trabajando.
- **Rastreo de Versiones:** Se agregó `CHANGELOG.md` (y `CHANGELOG_ES.md`) y visualización explícita de la versión en el panel del gráfico.

### Cambiado
- Se refactorizó `ManagePositionExit` para soportar actualizaciones dinámicas de precios para órdenes activas.

## [1.4.0] - 2025-12-23
### Agregado
- **Soporte Multi-Contrato:** Lógica para dividir la posición en TP1 (Más cercano) y TP2 (Más lejano).
- **Protección Inteligente:** El Stop Loss se mueve a Breakeven cuando se llena el TP1.

### Corregido
- Se corrigieron problemas con órdenes huérfanas donde los stops no se asociaban correctamente con la cantidad restante de la posición.
