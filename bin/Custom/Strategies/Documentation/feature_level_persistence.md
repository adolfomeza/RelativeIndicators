# Feature: Persistencia de Niveles de Sesión

## Objetivo
Reducir tiempo de carga de la estrategia guardando niveles calculados en archivo, evitando recalcular 30 días de histórico en cada inicio.

## Estado Propuesto

### Flujo de Primera Carga (Sin datos guardados)
1. Cargar 30 días de histórico
2. Calcular todos los niveles de sesión
3. Guardar niveles en archivo `{Instrumento}_levels.json`
4. Continuar a Realtime

### Flujo de Cargas Subsecuentes (Con datos guardados)
1. Cargar archivo `{Instrumento}_levels.json`
2. Cargar solo 3 días de histórico (para actualizar niveles recientes)
3. Fusionar: niveles del archivo + niveles de últimos 3 días
4. Continuar a Realtime

---

## Estructura de Datos a Persistir

```json
{
  "instrument": "MES",
  "lastUpdated": "2025-12-29T12:00:00",
  "schemaVersion": 1,
  "levels": [
    {
      "name": "US High",
      "price": 6050.25,
      "time": "2025-12-28T16:00:00",
      "isResistance": true,
      "isVirgin": true,
      "mitigatedAt": null,
      "entryAttempts": 0
    }
  ]
}
```

---

## Escenarios a Considerar

### ⚠️ Escenario 1: Nivel se Mitiga Mientras Estás Offline
**Problema**: Al cargar, nivel aparece como virgen pero ya fue tocado.
**Solución**: Verificar cada nivel contra últimos 3 días de datos.

### ⚠️ Escenario 2: Datos Antiguos
**Problema**: Niveles obsoletos contaminan la lista.
**Solución**: Solo cargar niveles de últimos `LevelAgeDays`.

### ⚠️ Escenario 3: Corrupción de Archivo
**Solución**: Validar `schemaVersion`, si error → recalcular 30 días.

### ⚠️ Escenario 4: Cambio de Configuración
**Solución**: Guardar hash de config, si no coincide → recalcular.

### ⚠️ Escenario 5: Múltiples Instrumentos
**Solución**: Archivo por instrumento: `MES_levels.json`.

### ⚠️ Escenario 6: Sincronización
**Solución**: Guardar al cerrar estrategia (`State.Terminated`).

---

## Preguntas Pendientes

1. ¿Cuántos días de niveles guardar? (Sugerido: 14)
2. ¿Guardar solo al cerrar o también durante ejecución?
3. ¿Opción para forzar recálculo manual?

---

## Estimación
- **Complejidad**: Media-Alta (5-8 horas)
- **Versión target**: v1.12.0

*Pendiente de revisión*
