# Feature Request: Entry Type B - Ruptura + Pullback

**Estado**: Pendiente (para implementar más adelante)  
**Prioridad**: Media  
**Fecha propuesta**: 2025-12-25

---

## Objetivo

Agregar un segundo tipo de entrada que opere **cambios de estructura intraday** cuando **no hay niveles de sesión activos** cercanos.

---

## Lógica del Setup

### Condiciones Previas
- Día direccional (rompió múltiples niveles)
- NO hay niveles de sesión (Asia/Europe/USA) activos cerca del precio actual
- Mercado ha formado nuevo extremo (High o Low del día)

### Secuencia de Entrada

**Para LONG** (después de día bajista):
1. **Identificar contexto**: Precio hace nuevo Low del día
2. **Anclar VWAP**: VWAP Low se ancla al nuevo mínimo
3. **Trigger**: Precio rompe el máximo anterior (cambio de estructura)
4. **Entry Setup**: Colocar orden **limit en VWAP Low** (esperar pullback)
5. **Entry Fill**: Precio retrocede y llena la orden
6. **TP1**: VWAP High o resistencia cercana
7. **TP2**: Máximo del día o nivel de sesión superior
8. **SL**: 1 tick debajo del Low que ancló el VWAP

**Para SHORT** (después de día alcista):
- Lógica inversa: Nuevo High → Rompe mínimo anterior → Limit en VWAP High

---

## Diferencias vs Entry Type A (Actual)

| Aspecto | Type A (Niveles) | Type B (Ruptura) |
|---------|------------------|------------------|
| **Trigger** | Toca nivel de sesión antiguo | Rompe extremo previo intraday |
| **Entry** | VWAP del nivel tocado | VWAP del nuevo extremo |
| **Contexto** | Niveles antiguos disponibles | Sin niveles cerca, día direccional |
| **Confirmación** | Separación de VWAP | Ruptura de estructura |

---

## Ventajas

1. **Captura reversiones**: Opera el primer rebote después de movimiento extremo
2. **Complementario**: Funciona cuando Type A no tiene setups
3. **Risk/Reward**: Entry con descuento (pullback), targets hacia resistencias

---

## Ejemplo Visual

![Setup 16/12/25](file:///C:/Users/prueba/.gemini/antigravity/brain/f2482fd8-f27e-43ae-bec9-59f0f56631f9/uploaded_image_1766708898333.png)

**Día bajista con 5 niveles rotos**:
1. Flecha 1: Máximo anterior (resistencia)
2. Flecha 2: Nuevo mínimo (ancla VWAP Low)
3. Flecha 3: Rompe máximo anterior (**Trigger**)
4. Flecha 4: Fill en VWAP Low (**Entry**)
5. Flecha 5: TP1 ejecutado
6. Flecha 6: TP2 ejecutado

---

## Implementación Sugerida

### Variables Necesarias
```csharp
private bool entryTypeBEnabled = true;
private double dayHigh = 0;
private double dayLow = 0;
private double previousSwingHigh = 0;
private double previousSwingLow = 0;
```

### Lógica de Activación
```csharp
// Solo activar Type B si:
// 1. No hay niveles de sesión activos cerca (ej: todos > 50 ticks away)
// 2. Día direccional (rango del día > ATR * 1.5)
// 3. Precio rompió extremo previo
```

### Integration Point
- Agregar en `ManageEntryA_Plus` como rama `else if` después de verificar niveles
- O crear método separado `ManageEntryB_Pullback`

---

## Tareas de Implementación

- [ ] Definir "sin niveles activos" (distancia mínima)
- [ ] Detectar ruptura de estructura (swing high/low previo)
- [ ] Lógica de pullback a VWAP
- [ ] Calcular targets (resistencias/soportes intraday)
- [ ] Testing en días muy direccionales
- [ ] Documentar en `analisis_session_levels.md`

---

## Notas

- Priorizar primero estabilidad de Entry Type A (niveles)
- Este setup es **complementario**, no reemplazo
- Útil para días con baja actividad de niveles antiguos
