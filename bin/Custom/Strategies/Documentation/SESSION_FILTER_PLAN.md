# Plan de Implementación: Filtro de Combinaciones Tóxicas

## Objetivo
Bloquear automáticamente combinaciones de sesión agresor/defensor que han demostrado ser no rentables en el análisis histórico.

## Combinaciones identificadas (del análisis Quant):
| Atacante | Defensor | PnL     | Acción     |
|----------|----------|---------|------------|
| Europe   | Asia     | +$5,540 | ✅ PERMITIR |
| USA      | Asia     | -$2,631 | ❌ BLOQUEAR |

## Implementación

### 1. Nueva Propiedad de Usuario (Configurable)

```csharp
// v1.14.0: SESSION INTERACTION FILTER
[NinjaScriptProperty]
[Display(Name="Block USA→Asia", Description="Block entries when USA session attacks Asia levels", Order=100, GroupName="Session Filter")]
public bool BlockUSAAttackingAsia { get; set; } = true;

[NinjaScriptProperty]
[Display(Name="Block Asia→USA", Description="Block entries when Asia session attacks USA levels", Order=101, GroupName="Session Filter")]
public bool BlockAsiaAttackingUSA { get; set; } = true;
```

### 2. Función Helper para Detectar Sesión Actual

```csharp
private string GetCurrentSessionName()
{
    TimeSpan now = Time[0].TimeOfDay;
    
    // Asia: 18:00 - 02:30 (encompasses midnight)
    if (now >= tsAsiaStart || now < tsAsiaEnd)
        return "Asia";
    
    // Europe: 02:30 - 09:30
    if (now >= tsEuStart && now < tsEuEnd)
        return "Europe";
    
    // USA: 09:30 - 17:00
    if (now >= tsUsaStart && now < tsUsaEnd)
        return "USA";
    
    return "Post-Mkt";
}
```

### 3. Función Helper para Extraer Sesión del Nivel

```csharp
private string GetLevelSession(string levelName)
{
    if (string.IsNullOrEmpty(levelName)) return "";
    
    if (levelName.Contains("Asia")) return "Asia";
    if (levelName.Contains("Europe") || levelName.Contains("EU")) return "Europe";
    if (levelName.Contains("USA") || levelName.Contains("US")) return "USA";
    
    return "Unknown";
}
```

### 4. Función de Validación de Combinación

```csharp
private bool IsSessionCombinationAllowed(string attackerSession, string defenderSession)
{
    // Check blocked combinations
    if (BlockUSAAttackingAsia && attackerSession == "USA" && defenderSession == "Asia")
    {
        Log($"BLOCKED: {attackerSession} → {defenderSession} (toxic combination)");
        return false;
    }
    
    if (BlockAsiaAttackingUSA && attackerSession == "Asia" && defenderSession == "USA")
    {
        Log($"BLOCKED: {attackerSession} → {defenderSession} (toxic combination)");
        return false;
    }
    
    // Add more blocked combinations as needed
    return true;
}
```

### 5. Integración en el Flujo de Entrada

Ubicación: Antes de colocar la orden de entrada (en la lógica de `WaitingForConfirmation` o similar)

```csharp
// v1.14.0: Session Interaction Filter
string currentSession = GetCurrentSessionName();
string levelSession = GetLevelSession(setupLevelName);

if (!IsSessionCombinationAllowed(currentSession, levelSession))
{
    Log($"ENTRY SKIPPED: Session filter blocked {currentSession} attacking {levelSession}");
    currentEntryState = EntryState.Idle;
    return; // Skip this entry
}
```

## Archivos a Modificar

1. **SessionLevelsStrategy.cs**
   - Agregar propiedades de filtro (línea ~250)
   - Agregar funciones helper (línea ~400)
   - Integrar validación antes de entrada (buscar lógica de entrada)

## Versión
- Cambiar de v1.13.16 a **v1.14.0**
- Documentar en CHANGELOG

## Notas
- El filtro es CONFIGURABLE - el usuario puede activar/desactivar cada combinación
- Se registra en logs cuando una entrada es bloqueada
- Basado en datos reales del análisis Quant del usuario

---

## 🚀 ROADMAP - Features Premium (Para Después)

### v1.15.0: Sistema de Scoring Dinámico
**Concepto:** En lugar de reglas estáticas, el sistema consulta histórico y decide dinámicamente.

**Implementación:**
1. Streamlit exporta archivo `session_scores.json` con rendimiento por combinación
2. Estrategia lee el archivo al iniciar
3. Antes de cada entrada: `GetHistoricalScore(atacante, defensor, dirección)`
4. Si `score < umbral` → Bloquear automáticamente

**Ejemplo de decisión:**
```
Nivel: Europe Low | Sesión: USA | Dirección: Long
→ Consulta: USA → Europe (Long) = -$45/trade promedio
→ Decisión: BLOQUEADO (score negativo)
```

### v1.16.0: Integración LLM para Análisis
- API de Gemini/GPT para insights personalizados
- Análisis de patrones psicológicos
- Recomendaciones de mejora en lenguaje natural

### v1.17.0: Dashboard Web Centralizado
- Monitoreo multi-instrumento en tiempo real
- Alertas por email/Telegram
- Histórico accesible desde cualquier dispositivo
