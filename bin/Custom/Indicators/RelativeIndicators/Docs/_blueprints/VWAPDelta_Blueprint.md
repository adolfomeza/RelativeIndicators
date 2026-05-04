# VWAPDelta — Blueprint v3 (Simplificado)
## Indicador NinjaTrader 8: Session VWAPs + Touch Detection + MFE Data Collection

**Version:** 1.0.0
**Fecha:** 2026-02-20
**Instrumento objetivo:** MNQ (extensible a NQ, ES, MES, CL, etc.)
**Timeframe:** 1 minuto (compatible con 2-5 min)

---

## 1. VISION GENERAL

### Que es
Un indicador para NinjaTrader 8 que hace tres cosas:

1. **Calcula y dibuja VWAPs anclados** desde los extremos del dia — cuando el precio hace un nuevo high/low, el VWAP anterior se **congela** y nace uno nuevo
2. **Detecta cada toque del precio con los VWAPs activos** y clasifica con 4 patrones de orderflow
3. **Registra el MFE hasta fin de sesion americana** para cada toque, exportando a CSV para analisis en Streamlit

### Proposito
Responder la pregunta: **"Cuando el precio toca un VWAP, en promedio cuanto recorre a favor antes de que termine el dia?"**

- VWAP High tocado → MFE bajista (cuanto bajo el precio despues)
- VWAP Low tocado → MFE alcista (cuanto subio el precio despues)

### Que NO es
- NO tiene sesiones separadas (Asia/Europe/USA) — una sola sesion de 23 horas
- NO dibuja niveles horizontales de session highs/lows
- NO genera senales de trading
- NO tiene TTS ni rendering complejo

### Mecanica de VWAPs — FREEZE + RE-ANCHOR
1. Al inicio del dia, se establece el primer High y Low
2. Se ancla un VWAP High desde la vela del maximo y un VWAP Low desde la vela del minimo
3. Estas curvas VWAP se actualizan barra a barra (acumulando TP*Vol)
4. **Cuando el precio supera el maximo actual** (nuevo extremo):
   - El VWAP High activo **SE CONGELA** — la curva simplemente deja de actualizarse
   - Queda visible en el chart como curva historica
   - Se crea un **NUEVO VWAP** anclado desde la vela que hizo el nuevo maximo
5. Misma logica para los lows (espejo)
6. Resultado: **multiples curvas VWAP** van quedando en el chart durante el dia
7. Sin limite de VWAPs

---

## 2. ARQUITECTURA

### Archivos (3 partial classes)

```
VWAPDelta.cs              — Clase principal: VwapCurve, OnBarUpdate, VWAPs, delta, OnRender
VWAPDelta.TouchDetect.cs  — Deteccion de toques, scoring, MFE tracking
VWAPDelta.Export.cs       — CSV export
```

### Namespace

```csharp
namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
    public partial class VWAPDelta : Indicator { }
}
```

### Estructura clave: VwapCurve

```csharp
private class VwapCurve
{
    public int ID;                          // Secuencial unico por dia
    public bool IsHighVwap;                 // true = anclado desde High, false = desde Low
    public int AnchorBar;                   // Bar donde se anclo
    public DateTime AnchorTime;             // Timestamp del anchor
    public double AnchorPrice;              // Precio extremo que genero este anchor
    public int FreezeBar;                   // Bar donde se congelo (-1 = aun activo)
    public double DeltaAtAnchor;            // Snapshot de _deltaGlobal al momento del anchor

    // Acumuladores VWAP
    public double CumTPV;                   // Sum(TP * Volume)
    public double CumVol;                   // Sum(Volume)

    // Valores por barra para rendering
    public Dictionary<int, double> BarValues;  // barIndex -> vwapValue

    public bool IsActive => FreezeBar < 0;
    public double CurrentValue => (CumVol > 0) ? CumTPV / CumVol : 0;
}
```

---

## 3. ARCHIVO 1: VWAPDelta.cs (Principal)

### 3.1 Estado y Variables

```csharp
// === VWAP CURVES (dinamicas) ===
private List<VwapCurve> _allVwapCurves;     // Todas del dia (activas + frozen)
private VwapCurve _activeHighVwap;           // VWAP High activo (null si no hay)
private VwapCurve _activeLowVwap;            // VWAP Low activo (null si no hay)
private int _vwapIdCounter;

// === SESSION EXTREMES ===
private double _sessionHigh, _sessionLow;
private int _sessionHighBarIdx, _sessionLowBarIdx;

// === DELTA ===
private double _barDelta;
private double _deltaGlobal;

// === TIEMPOS ===
private TimeSpan _sessionStartTs;           // 18:00 ET (inicio del dia de futuros)
private TimeSpan _usaEndTs;                 // 16:00 ET (fin sesion USA, corte MFE)
private DateTime _lastTradingDay;

// === INDICADORES AUXILIARES ===
private SMA _volumeSMA;                     // SMA(Volume, 20)
private ATR _atr;                           // ATR(14)
```

### 3.2 Propiedades

```csharp
// 01. Session
[Display(Name = "Session Start", GroupName = "01. Session", Order = 1)]
public string SessionStartTime { get; set; } = "18:00";

[Display(Name = "USA End (MFE Cutoff)", GroupName = "01. Session", Order = 2)]
public string USAEndTime { get; set; } = "16:00";

// 02. VWAP Visuals
[Display(Name = "High VWAP Color", GroupName = "02. VWAP Visuals", Order = 1)]
public Brush HighVwapColor { get; set; } = Brushes.OrangeRed;

[Display(Name = "Low VWAP Color", GroupName = "02. VWAP Visuals", Order = 2)]
public Brush LowVwapColor { get; set; } = Brushes.DodgerBlue;

[Display(Name = "Active Line Width", GroupName = "02. VWAP Visuals", Order = 3)]
public int ActiveLineWidth { get; set; } = 2;

[Display(Name = "Frozen Opacity", GroupName = "02. VWAP Visuals", Order = 4)]
public double FrozenOpacity { get; set; } = 0.6;

// 03. Touch Detection
[Display(Name = "Touch Tolerance (Ticks)", GroupName = "03. Touch Detection", Order = 1)]
public int TouchToleranceTicks { get; set; } = 3;

[Display(Name = "Touch Cooldown (Bars)", GroupName = "03. Touch Detection", Order = 2)]
public int TouchCooldownBars { get; set; } = 5;

[Display(Name = "Show Touch Markers", GroupName = "03. Touch Detection", Order = 3)]
public bool ShowTouchMarkers { get; set; } = true;

// 04. Export
[Display(Name = "Export CSV", GroupName = "04. Export", Order = 1)]
public bool ExportCSV { get; set; } = false;

[Display(Name = "Export Realtime Only", GroupName = "04. Export", Order = 2)]
public bool ExportRealtimeOnly { get; set; } = false;

// 05. Debug
[Display(Name = "Show Debug Logs", GroupName = "05. Debug", Order = 1)]
public bool ShowDebugLogs { get; set; } = false;
```

### 3.3 OnStateChange

```
State.SetDefaults:
  - Name = "VWAPDelta"
  - Calculate = Calculate.OnBarClose
  - IsOverlay = true
  - NO AddPlot (rendering en OnRender)

State.DataLoaded:
  - _volumeSMA = SMA(Volume, 20)
  - _atr = ATR(14)
  - Cache TimeSpan: _sessionStartTs, _usaEndTs
  - Inicializar _allVwapCurves

State.Terminated:
  - ForceFinalizeAll()
  - Dispose SharpDX brushes
```

### 3.4 OnBarUpdate — Flujo Principal

```
OnBarUpdate()
{
    if (CurrentBar < 20) return;

    // 1. Nuevo dia → Reset
    CheckNewDay();

    // 2. Calcular delta
    CalculateBarDelta();

    // 3. FREEZE + RE-ANCHOR si hay nuevos extremos
    CheckForNewExtremes();

    // 4. Crear primer VWAP si inicio del dia
    CreateInitialVwapsIfNeeded();

    // 5. Acumular VWAPs activos
    AccumulateActiveVwaps();

    // 6. Detectar toques en VWAPs activos
    DetectAllTouches();

    // 7. Actualizar MFE pendientes (hasta EOD)
    UpdatePendingResults();

    // 8. Exportar completados
    if (ExportCSV) FlushExportQueue();
}
```

### 3.5 CheckNewDay — Reset diario

```csharp
private void CheckNewDay()
{
    // Futuros: nuevo dia cuando cruza SessionStartTime (18:00 ET)
    TimeSpan currentTs = Time[0].TimeOfDay;
    TimeSpan prevTs = CurrentBar > 0 ? Time[1].TimeOfDay : currentTs;

    bool crossed = (prevTs < _sessionStartTs && currentTs >= _sessionStartTs);

    if (crossed || _lastTradingDay == DateTime.MinValue)
    {
        ResetDaily();
        _lastTradingDay = Time[0].Date;
    }
}

private void ResetDaily()
{
    _allVwapCurves.Clear();
    _activeHighVwap = null;
    _activeLowVwap = null;
    _vwapIdCounter = 0;
    _sessionHigh = double.MinValue;
    _sessionLow = double.MaxValue;
    _deltaGlobal = 0;
    ClearDailyTouchHistory();
}
```

### 3.6 CheckForNewExtremes — Freeze + Re-Anchor

```csharp
private void CheckForNewExtremes()
{
    // === HIGH: Precio supera el maximo actual ===
    if (_activeHighVwap != null && High[0] > _activeHighVwap.AnchorPrice)
    {
        _activeHighVwap.FreezeBar = CurrentBar;
        _sessionHigh = High[0];
        _sessionHighBarIdx = CurrentBar;
        _activeHighVwap = CreateNewVwap(isHigh: true);
    }

    // === LOW: Precio perfora el minimo actual ===
    if (_activeLowVwap != null && Low[0] < _activeLowVwap.AnchorPrice)
    {
        _activeLowVwap.FreezeBar = CurrentBar;
        _sessionLow = Low[0];
        _sessionLowBarIdx = CurrentBar;
        _activeLowVwap = CreateNewVwap(isHigh: false);
    }
}
```

### 3.7 Acumulacion y VWAP Calculation

```csharp
private void AccumulateActiveVwaps()
{
    double tp = (High[0] + Low[0] + Close[0]) / 3.0;
    double vol = Volume[0];
    if (vol <= 0) return;
    double pv = tp * vol;

    foreach (var curve in _allVwapCurves)
    {
        if (!curve.IsActive) continue;
        curve.CumTPV += pv;
        curve.CumVol += vol;
        curve.BarValues[CurrentBar] = curve.CumTPV / curve.CumVol;
    }
}
```

### 3.8 OnRender — Dibujo de Curvas VWAP

```csharp
protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
{
    base.OnRender(chartControl, chartScale);
    if (_allVwapCurves == null || _allVwapCurves.Count == 0) return;

    int firstBar = ChartBars.FromIndex;
    int lastBar = ChartBars.ToIndex;

    foreach (var curve in _allVwapCurves)
    {
        int startBar = Math.Max(curve.AnchorBar, firstBar);
        int endBar = curve.IsActive
            ? Math.Min(CurrentBar, lastBar)
            : Math.Min(curve.FreezeBar - 1, lastBar);

        if (startBar >= endBar) continue;

        // Color segun tipo + activo/frozen
        var brush = GetDxBrush(curve);
        float width = curve.IsActive ? ActiveLineWidth : 1.0f;

        for (int bar = startBar; bar < endBar; bar++)
        {
            if (!curve.BarValues.ContainsKey(bar) || !curve.BarValues.ContainsKey(bar + 1))
                continue;

            float x1 = chartControl.GetXByBarIndex(ChartBars, bar);
            float y1 = chartScale.GetYByValue(curve.BarValues[bar]);
            float x2 = chartControl.GetXByBarIndex(ChartBars, bar + 1);
            float y2 = chartScale.GetYByValue(curve.BarValues[bar + 1]);

            RenderTarget.DrawLine(
                new SharpDX.Vector2(x1, y1),
                new SharpDX.Vector2(x2, y2),
                brush, width);
        }
    }
}
```

**Colores:**
- High VWAPs activos: OrangeRed, 2px
- Low VWAPs activos: DodgerBlue, 2px
- Frozen: mismo color pero opacity reducida (0.6), 1px

---

## 4. ARCHIVO 2: VWAPDelta.TouchDetect.cs

### 4.1 Definicion de Toque

```csharp
bool IsTouching(double vwapValue, double tolerance)
{
    return (Low[0] <= vwapValue + tolerance && High[0] >= vwapValue - tolerance);
}
```

Solo en VWAPs **activos**.

### 4.2 Clasificacion

```csharp
public enum TouchType
{
    FromAbove,      // Bajo al VWAP, testeo como soporte
    FromBelow,      // Subio al VWAP, testeo como resistencia
    CrossUp,        // Cruzo de abajo hacia arriba
    CrossDown,      // Cruzo de arriba hacia abajo
    Consolidation   // Ya estaba en zona del VWAP
}
```

### 4.3 TouchRecord

```csharp
private class TouchRecord
{
    // Identificacion
    public int ID;
    public int VwapCurveID;
    public bool IsHighVwap;
    public DateTime TouchTime;
    public int TouchHour;           // Hora del toque (0-23) para filtrar en Streamlit
    public int TouchBarIdx;

    // Anchor info
    public DateTime AnchorTime;
    public int AnchorHour;          // Hora del anchor (0-23) para filtrar en Streamlit
    public double AnchorPrice;

    // Precio
    public double TouchPrice;       // Close[0]
    public double VwapPrice;        // Valor VWAP al momento del toque

    // Tipo y senal
    public TouchType Type;
    public TouchSignal Signal;

    // Pattern scores (0-1)
    public double AbsorptionScore;
    public double InitiativeScore;
    public double ExhaustionRatio;
    public double SweepScore;
    public double CompositeScore;

    // Delta contexto
    public double BarDelta;
    public double DeltaGlobal;
    public double DeltaAtAnchor;

    // Barra contexto
    public double ATR;
    public double Volume;
    public double VolumeRatio;
    public double BarRange;
    public double BodyRatio;
    public double WickRatio;

    // === RESULTADO HASTA EOD ===
    public double MFE_EOD;          // Max excursion FAVORABLE hasta fin de USA
    public double MAE_EOD;          // Max excursion ADVERSA hasta fin de USA
    public int BarsToMFE;           // Barras desde toque hasta MFE maximo
    public bool IsComplete;
}
```

**Direccion del MFE:**
- VWAP **High** tocado → actua como **resistencia** → MFE = cuanto BAJO el precio hasta EOD
  - `MFE = TouchPrice - Lowest Low hasta EOD` (en ticks)
  - `MAE = Highest High hasta EOD - TouchPrice` (en ticks)
- VWAP **Low** tocado → actua como **soporte** → MFE = cuanto SUBIO el precio hasta EOD
  - `MFE = Highest High hasta EOD - TouchPrice` (en ticks)
  - `MAE = TouchPrice - Lowest Low hasta EOD` (en ticks)

### 4.4 PendingTouchResult — Tracking MFE hasta EOD

```csharp
private class PendingTouchResult
{
    public TouchRecord Record;
    public int StartBar;
    public double StartPrice;
    public double RunningMFE;       // MFE acumulado hasta ahora
    public double RunningMAE;       // MAE acumulado hasta ahora
    public int MFEBar;              // Bar donde se alcanzo el MFE maximo
}
```

**UpdatePendingResults():**
```csharp
private void UpdatePendingResults()
{
    TimeSpan currentTs = Time[0].TimeOfDay;
    bool isEOD = (currentTs >= _usaEndTs);  // Llegamos al fin de sesion USA

    for (int i = _pendingResults.Count - 1; i >= 0; i--)
    {
        var p = _pendingResults[i];

        // Calcular excursion de esta barra
        if (p.Record.IsHighVwap)
        {
            // High VWAP = resistencia → favorable = baja, adverso = sube
            double favorable = p.StartPrice - Low[0];
            double adverse = High[0] - p.StartPrice;
            if (favorable > p.RunningMFE) { p.RunningMFE = favorable; p.MFEBar = CurrentBar; }
            if (adverse > p.RunningMAE) p.RunningMAE = adverse;
        }
        else
        {
            // Low VWAP = soporte → favorable = sube, adverso = baja
            double favorable = High[0] - p.StartPrice;
            double adverse = p.StartPrice - Low[0];
            if (favorable > p.RunningMFE) { p.RunningMFE = favorable; p.MFEBar = CurrentBar; }
            if (adverse > p.RunningMAE) p.RunningMAE = adverse;
        }

        // Completar al llegar a EOD
        if (isEOD)
        {
            p.Record.MFE_EOD = p.RunningMFE / TickSize;  // En ticks
            p.Record.MAE_EOD = p.RunningMAE / TickSize;
            p.Record.BarsToMFE = p.MFEBar - p.StartBar;
            p.Record.IsComplete = true;
            _completedTouches.Add(p.Record);
            _pendingResults.RemoveAt(i);
        }
    }
}
```

### 4.5 Patrones Deep Orderflow (A-D)

#### Patron A — ABSORCION
**Score:** `wickScore * 0.4 + deltaScore * 0.35 + volScore * 0.25`
- Mecha grande + delta contradictorio + volumen alto = institucional absorbiendo

#### Patron B — INICIATIVA
**Score:** `bodyScore * 0.35 + deltaScore * 0.35 + volScore * 0.30`
- Cuerpo grande + delta a favor + volumen > 1.5x = breakout real

#### Patron C — AGOTAMIENTO
**Deteccion:** `ratio = abs(deltaActual) / avg(abs(deltaPrevios))`
- Cada toque sucesivo en el MISMO VwapCurve tiene menos delta = agotamiento

#### Patron D — SWEEP
**Score:** `lowVolScore * 0.6 + highRangeScore * 0.4`
- Volumen bajo + rango grande = precio llego por vacio, no por conviccion

#### TouchSignal compuesto
```csharp
public enum TouchSignal
{
    StrongBounce,     // Alta probabilidad de rebote
    WeakBounce,       // Rebote posible sin conviccion
    Neutral,          // Sin senal clara
    LikelyPierce,    // Probable perforacion
    StrongPierce      // Perforacion con conviccion
}
```

### 4.6 Anti-Spam

Cooldown de `TouchCooldownBars` (default 5) por VwapCurve ID.

---

## 5. ARCHIVO 3: VWAPDelta.Export.cs

### 5.1 CSV Columnas (~30)

```
ID, Instrument, TouchTime, TouchHour, VwapCurveID, IsHighVwap,
AnchorTime, AnchorHour, AnchorPrice,
TouchPrice, VwapPrice, TouchType, TouchSignal,
AbsorptionScore, InitiativeScore, ExhaustionRatio, SweepScore, CompositeScore,
BarDelta, DeltaGlobal, DeltaAtAnchor,
ATR, Volume, VolumeRatio, BarRange, BodyRatio, WickRatio,
MFE_EOD, MAE_EOD, BarsToMFE
```

### 5.2 Path

```
TradeExports/DEMO619219/VWAPDelta_{SYMBOL}_{MM-yy}.csv
```

### 5.3 ForceFinalizeAll

En State.Terminated o fin de datos historicos, completar todos los pending con la data disponible.

---

## 6. FLUJO OnBarUpdate COMPLETO

```
OnBarUpdate()
{
    if (CurrentBar < 20) return;

    // 1. Nuevo dia → Reset (cuando cruza SessionStartTime)
    CheckNewDay();

    // 2. Delta de esta barra
    CalculateBarDelta();    // _barDelta = (Close-Open)*Vol, _deltaGlobal += _barDelta

    // 3. Nuevos extremos → Freeze viejo VWAP + crear nuevo
    CheckForNewExtremes();

    // 4. Primer VWAP si inicio del dia
    CreateInitialVwapsIfNeeded();

    // 5. Acumular VWAPs activos con TP*Vol
    AccumulateActiveVwaps();

    // 6. Detectar toques en VWAPs activos
    DetectAllTouches();

    // 7. Actualizar MFE pendientes (cada barra hasta EOD)
    UpdatePendingResults();

    // 8. Exportar completados
    if (ExportCSV) FlushExportQueue();
}
```

---

## 7. PLAN DE IMPLEMENTACION

### Fase 1: Esqueleto
- 3 archivos con namespace y partial class
- OnStateChange (sin AddPlot)
- OnRender vacio
- Agregar al .csproj
- Compilar

### Fase 2: VWAPs con Freeze/Re-Anchor
- VwapCurve class
- CheckNewDay() + ResetDaily()
- CheckForNewExtremes()
- CreateInitialVwapsIfNeeded()
- AccumulateActiveVwaps()
- OnRender() con polylines
- Verificar visualmente

### Fase 3: Delta
- CalculateBarDelta() con proxy (Close-Open)*Vol
- DeltaAtAnchor por VwapCurve

### Fase 4: Touch Detection + Patterns
- TouchRecord completo
- IsTouching() + ClassifyTouch()
- 4 patrones (A-D)
- EvaluateTouch()
- Cooldown anti-spam
- Draw.Diamond marcadores

### Fase 5: MFE Tracking hasta EOD
- PendingTouchResult con tracking continuo
- MFE/MAE segun direccion del VWAP (High=bajista, Low=alcista)
- Completar al llegar a USAEndTime

### Fase 6: CSV Export
- ~30 columnas con hora del anchor y hora del toque
- StreamWriter append, UTF8

### Fase 7: Validacion
- Freeze/re-anchor visual correcto
- MFE hasta EOD correcto
- CSV completo y legible en Streamlit

---

## 8. FUTURO (Post v1.0)

### v1.1 — Filtros calibrados con data de Streamlit
### v1.2 — OnMarketData: Delta real tick-by-tick
### v1.3 — Senales de trading basadas en data
### v1.4 — Alertas TTS en StrongBounce
### v1.5 — Touch en VWAPs frozen (revisita de niveles historicos)

---

## 9. REFERENCIA RAPIDA — ESTRUCTURA

```
VWAPDelta.cs                    (~700 lineas)
--- VwapCurve class
--- Variables: _allVwapCurves, _activeHighVwap, _activeLowVwap
--- Properties (Session, Visuals, Detection, Export, Debug)
--- OnStateChange (sin AddPlot)
--- OnBarUpdate
--- CheckNewDay() + ResetDaily()
--- CheckForNewExtremes()         *** freeze + re-anchor
--- CreateInitialVwapsIfNeeded()
--- AccumulateActiveVwaps()
--- CalculateBarDelta()
--- OnRender()                    *** polylines SharpDX

VWAPDelta.TouchDetect.cs        (~500 lineas)
--- TouchRecord class (con MFE_EOD, MAE_EOD, horas)
--- PendingTouchResult class
--- DetectAllTouches() (solo activos)
--- CheckVwapTouch(VwapCurve)
--- IsTouching() + ClassifyTouch()
--- CalculateAbsorptionScore()
--- CalculateInitiativeScore()
--- CalculateExhaustionRatio()
--- CalculateSweepScore()
--- EvaluateTouch()
--- UpdatePendingResults()        *** MFE hasta EOD
--- DrawTouchMarker()

VWAPDelta.Export.cs              (~150 lineas)
--- CSV_HEADER (~30 columnas)
--- WriteAllToCsv()
--- FlushExportQueue()
--- ForceFinalizeAll()
```
