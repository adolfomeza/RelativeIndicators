# Lógica de Reinicio de Estrategia

Este documento explica cómo la estrategia determina si puede entrar o no cuando se reinicia/activa.

---

## Escenarios al Reiniciar

### 1. Con Posición Existente (v1.10.38)
```
SI Account.Positions tiene posición:
  → currentEntryState = PositionActive
  → ADOPTA órdenes SL/TP existentes
  → NO busca nuevos triggers
```

### 2. Sin Posición - Estado Histórico (v1.10.39)
```
SI no hay posición Y estado ≠ Idle (heredado de Historical):
  → LIMPIA el estado a Idle
  → Log: "STARTUP RESET: Clearing historical state"
```

### 3. Sin Posición - Fresh Start
```
SI no hay posición Y estado = Idle:
  → Marca niveles "tocados" como "skipped"
  → Espera NUEVO trigger
```

### 4. Nueva Semana (v1.10.37)
```
SI domingo y pasó viernes 6pm NY:
  → LIMPIA todo el estado
  → Log: "WEEK RESET"
```

---

## Condiciones para Entrar

| Condición | Requisito |
|-----------|-----------|
| Estado | `Idle` (sin setup activo) |
| Nivel | De día ANTERIOR, no en `skippedLevelsAtStartup` |
| Intentos | < `MaxRetriesPerLevel` |
| Trigger | Precio toca nivel → `WaitingForConfirmation` |
| Confirmación | Precio se separa del VWAP → `workingOrder` |
| R/R | ≥ 1:1 |
| State | `Realtime` (o `Historical` en Playback) |

---

## Flujo de Estados

```
Idle → [Trigger] → WaitingForConfirmation → [Separación VWAP] → workingOrder → [Fill] → PositionActive
```

