# Feature Request: Gestión Avanzada de Niveles Internos

**Estado**: Pendiente (para implementar después de validar estrategia básica)  
**Prioridad**: Alta  
**Fecha propuesta**: 2025-12-25  
**Origen**: Usuario identificó comportamiento subóptimo en trades de niveles internos

---

## Problema Actual

La estrategia actual (v1.7.25) **NO gestiona el contexto dinámico** de niveles internos:

1. **No re-ancla VWAP** cuando precio rompe el nivel interno inicial
2. **No invalida trades** cuando precio alcanza nivel externo superior/inferior
3. Resultado: Trades en contexto inválido, menor win rate en internos

---

## Definiciones

### Nivel Interno
Nivel de sesión formado **dentro del rango** de otra sesión de mayor jerarquía.

**Ejemplo**:
```
Asia High @ 56.00 (externo)
Europe High @ 55.70 (interno, dentro del rango Asia)
Asia Low @ 55.50 (externo)
```

### Nivel Externo
Nivel que establece el rango dominante de su sesión, no contenido por otros.

---

## Lógica Propuesta

### Regla 1: Re-Anclaje de VWAP en Ruptura de Nivel Interno

**Condición**: Precio rompe el extremo del nivel interno original

**Acción SHORT** (nivel interno HIGH):
```
Trigger inicial: Europe High @ 55.70
VWAP anclado: 55.70
Precio sube: 55.75 (rompe nivel interno)
→ RE-ANCLAR VWAP a 55.75 (nuevo extremo)
Precio sube: 55.80
→ RE-ANCLAR VWAP a 55.80
```

**Acción LONG** (nivel interno LOW):
```
Trigger inicial: Europe Low @ 55.30
VWAP anclado: 55.30
Precio baja: 55.25 (rompe nivel interno)
→ RE-ANCLAR VWAP a 55.25 (nuevo extremo)
```

**Justificación**: 
- El nivel interno fue invalidado por el precio
- El nuevo extremo es el verdadero "anchor" del rechazo
- VWAP debe reflejar el precio desde el extremo REAL

---

### Regla 2: Invalidación por Toque de Nivel Externo

**Condición**: Precio alcanza nivel externo que contiene al interno

**Escenario SHORT en Interno**:
```
Setup: Europe High @ 55.70 (interno)
Nivel externo superior: Asia High @ 56.00
Precio sube: 55.80, 55.90, 55.95...
Toca: 56.00 (Asia High) ← INVALIDA trade interno
→ Cancelar entry limit
→ Resetear estado
→ Ahora buscar setup en Asia High (externo)
```

**Escenario LONG en Interno**:
```
Setup: Europe Low @ 55.30 (interno)
Nivel externo inferior: Asia Low @ 55.00
Precio baja: 55.20, 55.10, 55.05...
Toca: 55.00 (Asia Low) ← INVALIDA trade interno
→ Cancelar entry, buscar setup en Asia Low
```

**Justificación**:
- El contexto cambió de interno a externo
- El nivel externo tiene mayor prioridad/fuerza
- Trade interno ya no tiene sentido (target sería el externo)

---

## Implementación Técnica

### Variables Necesarias

```csharp
private bool isInternalLevel = false; // ¿Es nivel interno?
private double externalLevelAbove = 0; // Nivel externo superior (SHORT)
private double externalLevelBelow = 0; // Nivel externo inferior (LONG)
private string externalLevelAboveName = "";
private string externalLevelBelowName = "";
```

### Detección de Nivel Interno vs Externo

**Al detectar trigger**:
```csharp
// Buscar si hay niveles externos que contengan este
if (lvl.IsResistance) // SHORT en High
{
    // Buscar nivel HIGH externo por encima
    externalLevelAbove = FindExternalLevelAbove(lvl);
    isInternalLevel = (externalLevelAbove > 0);
    
    // Buscar nivel LOW externo por debajo (para TP2)
    externalLevelBelow = FindExternalLevelBelow(lvl);
}
```

**Lógica de búsqueda**:
```csharp
private double FindExternalLevelAbove(SessionLevel currentLevel)
{
    foreach (var l in activeLevels)
    {
        // Solo niveles superiores, resistencias
        if (!l.IsResistance || l.Price <= currentLevel.Price) continue;
        
        // Diferente sesión (Asia vs Europe, etc)
        if (l.Name.Contains(currentLevel.SessionName)) continue;
        
        // Encontrado → es internal
        return l.Price;
    }
    return 0; // No encontrado → es external
}
```

---

### Regla 1: Re-Anclaje (OnBarUpdate)

```csharp
if (currentEntryState == EntryState.WaitingForConfirmation)
{
    // SHORT: Si precio rompe anchor hacia arriba
    if (isShortSetup && High[0] > setupAnchorPrice + TickSize)
    {
        setupAnchorPrice = High[0]; // Re-anclar
        
        // Reset VWAP Ad-Hoc desde nuevo anchor
        double price = GetTypicalPrice();
        adhocVolSum = Volume[0]; 
        adhocPvSum = Volume[0] * price;
        adhocLastBar = CurrentBar;
        
        if (EnableDebugLogs) 
            Print(Time[0] + " RE-ANCHOR: New High @ " + setupAnchorPrice);
    }
    
    // LONG: Similar pero con Low
    if (!isShortSetup && Low[0] < setupAnchorPrice - TickSize)
    {
        setupAnchorPrice = Low[0];
        // Reset VWAP...
    }
}
```

---

### Regla 2: Invalidación (OnBarUpdate)

```csharp
if (currentEntryState == EntryState.WaitingForConfirmation && isInternalLevel)
{
    bool touchedExternal = false;
    
    // SHORT interno: Verificar si toca nivel externo superior
    if (isShortSetup && externalLevelAbove > 0)
    {
        if (High[0] >= externalLevelAbove)
        {
            touchedExternal = true;
            if (EnableDebugLogs)
                Print(Time[0] + " INVALIDATED: Touched external " + externalLevelAboveName);
        }
    }
    
    // LONG interno: Verificar nivel externo inferior
    if (!isShortSetup && externalLevelBelow > 0)
    {
        if (Low[0] <= externalLevelBelow)
        {
            touchedExternal = true;
        }
    }
    
    if (touchedExternal)
    {
        // Cancelar orden limit si existe
        if (entryOrder != null && entryOrder.OrderState == OrderState.Working)
            CancelOrder(entryOrder);
        
        // Resetear estado
        currentEntryState = EntryState.Idle;
        isInternalLevel = false;
        
        // Opcional: Trigger inmediato en el nivel externo
        // (implementar lógica de nuevo trigger)
    }
}
```

---

## Casos Edge

### Caso 1: Niveles Internos Múltiples
```
Asia High @ 56.00
Europe High @ 55.80
USA High @ 55.70
Asia Low @ 55.00
```

**Pregunta**: ¿USA High es interno de Europe o Asia?
**Solución**: Buscar el externo MÁS CERCANO (Europe @ 55.80)

---

### Caso 2: Gap que Rompe Interno
```
Friday: Europe High @ 55.70
Weekend gap
Monday: Open @ 55.90 (ya rompió)
```

**Solución**: No trigger en Europe High, buscar siguiente nivel

---

### Caso 3: Nivel Externo Muy Lejano
```
Europe High @ 55.70 (interno)
Asia High @ 60.00 (externo, 4.30 puntos arriba)
```

**Pregunta**: ¿Seguir considerando internal con tanta distancia?
**Solución**: Definir threshold (ej: si distancia > 200 ticks, tratar como externo)

---

## Testing Sugerido

1. **Playback con niveles internos** (Europe dentro de Asia)
2. **Verificar re-anclaje** cuando precio rompe interno
3. **Verificar invalidación** cuando toca externo
4. **Comparar win rate**: Internos antes vs después del fix

---

## Prioridad de Implementación

1. **Primero**: Validar estrategia básica (v1.7.25) en playback
2. **Segundo**: Implementar detección Internal vs External
3. **Tercero**: Implementar Regla 1 (re-anclaje)
4. **Cuarto**: Implementar Regla 2 (invalidación)
5. **Quinto**: Testing extensivo

---

## Notas del Usuario

"Vi ese trade B con ese comportamiento raro" - Usuario observó trade en nivel interno que no se invalidó al tocar externo, confirmando la necesidad de esta feature.
