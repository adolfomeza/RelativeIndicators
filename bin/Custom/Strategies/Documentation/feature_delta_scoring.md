# Feature: Delta-Based Level Scoring

## Resumen
Sistema de scoring de niveles de sesión basado en análisis de Order Flow (Delta) usando el indicador `RelativeDelta.cs` existente.

---

## Objetivo
Filtrar niveles de sesión (Asia/Europe High/Low) basándose en la actividad institucional detectada por el Delta, evitando tradear niveles "débiles" sin respaldo de flujo.

---

## Indicador Disponible: RelativeDelta

**Ubicación**: `c:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Indicators\RelativeIndicators\RelativeDelta.cs`

### Series Accesibles desde Estrategia:
```csharp
relativeDelta.DeltaClose[0]  // Delta al cierre de la barra
relativeDelta.DeltaHigh[0]   // Delta máximo de la barra
relativeDelta.DeltaLow[0]    // Delta mínimo de la barra
relativeDelta.DeltaOpen[0]   // Delta al open de la barra
```

### Características:
- Usa datos tick-by-tick (`AddDataSeries(Tick, 1)`)
- Calcula Buy/Sell basado en precio vs Ask/Bid
- Detecta divergencias Delta vs Precio
- Ya probado y funcional

---

## Modelo de Análisis: 4 Fases

```
     Fase 1          Fase 2           Fase 3           Fase 4
       │               │                │                │
       │   PUSH UP     │   FORMACIÓN    │   DESPEGUE     │   ENTRADA
       │               │                │                │
       ▼               ▼                ▼                ▼
    Swing Low ───────► HIGH ─────────► VWAP ──────────► ORDER FILL
       │               │                │                │
       └── ΔPush ──────┴─── ΔFormación ─┴─── ΔDespegue ──┴─── ΔAcumulado
```

---

## Fase 1: El Push (Swing → Extreme)

**Pregunta**: ¿Cómo llegó el precio al extreme?

| Delta del Push | Interpretación | Significado |
|----------------|----------------|-------------|
| Push débil (poco delta) | Subida sin convicción | Probable reversión |
| Push fuerte + Wick larga | Liquidity Grab | Barrieron stops → Bueno para reversión |
| Push fuerte sostenido | Tendencia real | ⚠️ Precaución |

### Liquidity Grab (Stop Hunt):
- Delta explota positivo EN el High
- PERO inmediatamente hay absorción (ventas)
- Wick larga = Rechazo visible

---

## Fase 2: Formación del Extreme

**Pregunta**: ¿Qué pasó exactamente en el High/Low?

### Para SHORT Setup (Asia/Europe HIGH):
| Delta en Formación | Score |
|--------------------|-------|
| < -2000 (sellers dominando) | +40 |
| -500 a -2000 | +30 |
| -500 a +500 (neutro) | +10 |
| > +500 (buyers dominando) | 0 (o considerar Liquidity Grab) |

### Para LONG Setup (Asia/Europe LOW):
| Delta en Formación | Score |
|--------------------|-------|
| > +2000 (buyers dominando) | +40 |
| +500 a +2000 | +30 |
| -500 a +500 (neutro) | +10 |
| < -500 (sellers dominando) | 0 (o considerar Liquidity Grab) |

### Absorción:
| Comportamiento | Interpretación | Score Extra |
|----------------|----------------|-------------|
| Precio sube + Delta baja | Instituciones vendiendo | +25 |
| Precio baja + Delta sube | Instituciones comprando | +25 |
| Alto volumen + Precio estancado | Stalling | +20 |

---

## Fase 3: El Despegue (VWAP Separation)

**Pregunta**: ¿Cómo se comporta el Delta mientras el precio confirma?

### Para SHORT:
| Delta durante Despegue | Score |
|------------------------|-------|
| Disminuyendo (sellers entrando) | +25 |
| Neutro | +10 |
| Aumentando (buyers) | ❌ Skip trade |

### Para LONG:
| Delta durante Despegue | Score |
|------------------------|-------|
| Aumentando (buyers entrando) | +25 |
| Neutro | +10 |
| Disminuyendo (sellers) | ❌ Skip trade |

---

## Fase 4: Delta Acumulado del Día

**Pregunta**: ¿Quién controla la sesión en el momento de la entrada?

| Delta Acumulado | Control | Para SHORT | Para LONG |
|-----------------|---------|------------|-----------|
| < -2000 | Sellers | ✅ +30 | ⚠️ -20 |
| -2000 a 0 | Sesgo vendedor | ✅ +20 | ⚠️ -10 |
| 0 a +2000 | Sesgo comprador | ⚠️ -10 | ✅ +20 |
| > +2000 | Buyers | ⚠️ -20 | ✅ +30 |

---

## Tabla de Scoring Completa

### SHORT Setup (High Level)
| Criterio | Condición | Puntos |
|----------|-----------|--------|
| Fase 1: Push | Débil o Liquidity Grab | +15 |
| Fase 2: Formación | Delta < -2000 | +40 |
| Fase 2: Formación | Delta -500 a -2000 | +30 |
| Fase 2: Absorción | ΔHigh - ΔClose > 1000 | +25 |
| Fase 3: Despegue | Delta disminuyendo | +25 |
| Fase 4: Acumulado | < -2000 | +30 |
| **MÁXIMO** | | **100** |

### LONG Setup (Low Level)
| Criterio | Condición | Puntos |
|----------|-----------|--------|
| Fase 1: Push | Débil o Liquidity Grab | +15 |
| Fase 2: Formación | Delta > +2000 | +40 |
| Fase 2: Formación | Delta +500 a +2000 | +30 |
| Fase 2: Absorción | ΔClose - ΔLow > 1000 | +25 |
| Fase 3: Despegue | Delta aumentando | +25 |
| Fase 4: Acumulado | > +2000 | +30 |
| **MÁXIMO** | | **100** |

---

## Configuración Propuesta

```csharp
[Display(Name = "Use Delta Filter", GroupName = "Order Flow")]
public bool UseDeltaFilter { get; set; } = true;

[Display(Name = "Min Delta Score", GroupName = "Order Flow")]
[Range(0, 100)]
public int MinDeltaScore { get; set; } = 40;
```

---

## Plan de Implementación

### Paso 1: Agregar RelativeDelta a la Estrategia
```csharp
// Declaración
private RelativeIndicators.RelativeDelta relativeDelta;

// En OnStateChange → State.DataLoaded
relativeDelta = RelativeDelta(...parámetros...);
```

### Paso 2: Modificar SessionLevel para guardar Delta
```csharp
public class SessionLevel
{
    // Existentes...
    public double Price { get; set; }
    public DateTime StartTime { get; set; }
    
    // Nuevos para Delta
    public double DeltaAtFormation { get; set; }
    public double DeltaHigh { get; set; }
    public double DeltaLow { get; set; }
    public double DeltaAtSwingStart { get; set; }
    public bool AbsorptionDetected { get; set; }
}
```

### Paso 3: Capturar Delta al formar niveles (CheckSession)
```csharp
if (High[0] > highLvl.Price)
{
    highLvl.Price = High[0];
    highLvl.DeltaAtFormation = relativeDelta.DeltaClose[0];
    highLvl.DeltaHigh = relativeDelta.DeltaHigh[0];
}
```

### Paso 4: Implementar CalculateDeltaScoreAdvanced()
```csharp
private double CalculateDeltaScoreAdvanced(SessionLevel lvl, bool isShort)
{
    double score = 0;
    
    // Fase 2: Delta en formación
    if (isShort && lvl.DeltaAtFormation < -1000) score += 30;
    else if (isShort && lvl.DeltaAtFormation < 0) score += 20;
    
    // Fase 3: Delta durante despegue
    double deltaDetach = relativeDelta.DeltaClose[0] - lvl.DeltaAtFormation;
    if (isShort && deltaDetach < -500) score += 25;
    
    // Fase 4: Delta acumulado del día
    double deltaDay = relativeDelta.DeltaClose[0];
    if (isShort && deltaDay < -2000) score += 30;
    else if (isShort && deltaDay < 0) score += 20;
    
    return Math.Max(0, score);
}
```

### Paso 5: Integrar en ManageEntryA_Plus
```csharp
if (UseDeltaFilter)
{
    double deltaScore = CalculateDeltaScoreAdvanced(lvl, isShortSetup);
    if (deltaScore < MinDeltaScore)
    {
        Log("Trigger skipped - Low Delta Score: " + deltaScore);
        continue;
    }
}
```

---

## Estimación de Tiempo

| Componente | Tiempo |
|------------|--------|
| Agregar RelativeDelta a estrategia | 1 hora |
| Modificar SessionLevel | 30 min |
| Capturar Delta en CheckSession | 1 hora |
| Implementar CalculateDeltaScoreAdvanced | 1.5 horas |
| Integrar en triggers | 1 hora |
| Testing y ajustes | 2 horas |
| **Total** | **~7 horas** |

---

## Resultado Esperado

| Métrica | Sin Filtro | Con Filtro Delta |
|---------|------------|------------------|
| Total Trades | 100% | ~60-70% |
| Win Rate | ~50% | ~55-65% |
| Calidad de Trades | Variable | Consistente |

---

## Metodología de Optimización

### Flujo de Trabajo

```
┌─────────────────────────────────────────────────────────────────────┐
│                                                                     │
│  FASE 1: BACKTESTING INCREMENTAL                                   │
│  ─────────────────────────────────                                  │
│                                                                     │
│    Mes 1 ──► Strategy Analyzer ──► CSV con Delta Scores            │
│    Mes 2 ──► Strategy Analyzer ──► CSV con Delta Scores            │
│    Mes 3 ──► Strategy Analyzer ──► CSV con Delta Scores            │
│    ...                                                              │
│    Mes 12 ──► Strategy Analyzer ──► CSV con Delta Scores           │
│                                                                     │
│    ⚠️ IMPORTANTE: 1 mes a la vez para no sobrecargar la PC         │
│                                                                     │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│                                                                     │
│  FASE 2: CONSOLIDACIÓN                                              │
│  ────────────────────────                                           │
│                                                                     │
│    12 archivos CSV ──► Merge ──► Master Dataset                     │
│                                                                     │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│                                                                     │
│  FASE 3: ANÁLISIS IA (TradeAnalyzer + ML)                          │
│  ──────────────────────────────────────────                         │
│                                                                     │
│    Master Dataset ──► Análisis ──► Umbrales Óptimos                 │
│                                                                     │
│    • ¿Cuál MinDeltaScore maximiza Win Rate?                        │
│    • ¿Cuál combinación de Fases da mejor Profit Factor?            │
│    • ¿Hay diferencias por instrumento?                              │
│    • ¿Hay diferencias por sesión (Asia vs Europe)?                 │
│                                                                     │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│                                                                     │
│  FASE 4: IMPLEMENTACIÓN LIVE                                        │
│  ─────────────────────────────                                      │
│                                                                     │
│    Parámetros optimizados ──► SessionLevelsStrategy                │
│                                                                     │
│    MinDeltaScore = [valor óptimo encontrado]                       │
│    Fase1Weight = [valor óptimo]                                     │
│    Fase2Weight = [valor óptimo]                                     │
│    ...                                                              │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Estructura del CSV para Análisis

### Campos a Exportar:

```csv
Date,Time,Instrument,Setup,LevelName,
DeltaPush,DeltaFormation,DeltaDespegue,DeltaAcumulado,
AbsorptionDetected,WickRatio,
DeltaScoreTotal,Fase1Score,Fase2Score,Fase3Score,Fase4Score,
EntryPrice,ExitPrice,PnL,Result
```

### Ejemplo de Registro:

```csv
2025-12-17,10:52:00,MNQ,Long,Europe Low,
1200,-800,-500,-1500,
true,2.5,
75,15,30,15,15,
6848.25,6861.00,12.75,Win
```

---

## Preguntas que la IA Respondería

1. **¿Cuál es el umbral mínimo de DeltaScore que da Win Rate > 55%?**
2. **¿Qué fase tiene mayor correlación con trades ganadores?**
3. **¿El DeltaAcumulado < 0 realmente ayuda a filtrar malos shorts?**
4. **¿Hay instrumentos donde el Delta NO añade valor?**
5. **¿Cuáles combinaciones de scores nunca pierden?**
6. **¿Hay horarios donde el Delta es más predictivo?**

---

## Requisitos de Hardware

### Para Backtesting con Tick Replay:

| Componente | Mínimo | Recomendado | Tu PC |
|------------|--------|-------------|-------|
| CPU | i5 | i7/Ryzen 7 | ✅ i7-10750H |
| RAM | 16 GB | 32 GB | ✅ 24 GB |
| Disco | SSD 50 GB libres | SSD 100 GB+ | ⚠️ 34 GB libres |

### Recomendaciones:
- Procesar 1 mes a la vez
- `DaysToLoad = 3-5` en RelativeDelta
- Cerrar otras aplicaciones durante backtesting
- Liberar espacio en disco antes de empezar

---

## Estimación de Tiempo por Fase

| Fase | Trabajo | Tiempo |
|------|---------|--------|
| **Fase 1** | Backtesting 12 meses (1 mes a la vez) | 12 × 30 min = 6 horas |
| **Fase 2** | Merge de archivos | 1 hora |
| **Fase 3** | Análisis IA | 2-4 horas |
| **Fase 4** | Implementación | 2 horas |
| **Total** | | **~12-15 horas** |

---

## Checklist de Implementación

### Pre-Requisitos:
- [ ] Confirmar que RelativeDelta funciona correctamente en chart
- [ ] Liberar espacio en disco (mínimo 50 GB)
- [ ] Tener datos históricos de 12 meses

### Desarrollo:
- [ ] Agregar RelativeDelta a SessionLevelsStrategy
- [ ] Modificar SessionLevel para guardar Delta
- [ ] Capturar Delta en CheckSession
- [ ] Implementar CalculateDeltaScoreAdvanced()
- [ ] Agregar exportación CSV con campos Delta
- [ ] Integrar filtro en ManageEntryA_Plus

### Optimización:
- [ ] Ejecutar backtesting Mes 1
- [ ] Ejecutar backtesting Mes 2-12
- [ ] Consolidar archivos CSV
- [ ] Analizar con TradeAnalyzer/IA
- [ ] Documentar umbrales óptimos

### Go-Live:
- [ ] Implementar parámetros optimizados
- [ ] Testing en demo 1 semana
- [ ] Deploy a producción

---

*Documento creado: 2025-12-27*
*Última actualización: 2025-12-27*
*Basado en conversación de planificación*

