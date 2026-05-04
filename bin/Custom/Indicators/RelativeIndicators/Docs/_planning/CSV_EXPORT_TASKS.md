# Tareas: Implementación Export CSV para Streamlit

## Estado General
- [ ] Fase 1: Enriquecer PendingSignal
- [ ] Fase 2: Calcular MAE/MFE y exportar CSV
- [ ] Fase 3: Integración Streamlit

---

## FASE 1 — Enriquecer PendingSignal y DrawSignalVisualization

### Tarea 1.1 — Agregar campos a `PendingSignal`
**Archivo:** `RelativeVwap.Utilities.cs`
**Ubicación:** clase `PendingSignal` (~línea 217)

Agregar estos campos:
```csharp
public string SetupName;     // "Asia High", "Europe Low", "USA Low", etc.
public int AnchorSequence;   // Número de reintento (highAnchorSequence / lowAnchorSequence)
public DateTime AnchorTime;  // Time del bar de anclaje (para calcular LevelAge)
public DateTime SignalTime;  // Time del bar de Signal 2 (EntryTime exacto)
```

### Tarea 1.2 — Pasar los nuevos campos desde Signal 2 SHORT
**Archivo:** `RelativeVwap.cs`
**Ubicación:** bloque Signal 2 SHORT (~línea 1620-1640)

Buscar la llamada:
```csharp
DrawSignalVisualization(false, sessionHighBarIdx, hVwap, tp1, tp2, qty, slPrice);
```

Antes de esa línea, construir:
```csharp
string setupNameShort = (lastUnlockedHighSession != null)
    ? lastUnlockedHighSession.Name + " High"
    : "Unknown";
int seqShort = highAnchorSequence;
DateTime anchorTimeShort = (sessionHighBarIdx >= 0 && sessionHighBarIdx < CurrentBar)
    ? Time.GetValueAt(sessionHighBarIdx) : Time[0];
```

Modificar firma de `DrawSignalVisualization` para aceptar estos parámetros (ver Tarea 1.3).

### Tarea 1.3 — Pasar los nuevos campos desde Signal 2 LONG
**Archivo:** `RelativeVwap.cs`
**Ubicación:** bloque Signal 2 LONG (~línea 1960-1980)

Buscar la llamada:
```csharp
DrawSignalVisualization(true, sessionLowBarIdx, lVwapPrev, tp1, tp2, qty, slPriceL);
```

Misma lógica que 1.2 pero para LONG:
```csharp
string setupNameLong = (lastUnlockedLowSession != null)
    ? lastUnlockedLowSession.Name + " Low"
    : "Unknown";
int seqLong = lowAnchorSequence;
DateTime anchorTimeLong = (sessionLowBarIdx >= 0 && sessionLowBarIdx < CurrentBar)
    ? Time.GetValueAt(sessionLowBarIdx) : Time[0];
```

### Tarea 1.4 — Actualizar firma de `DrawSignalVisualization`
**Archivo:** `RelativeVwap.Utilities.cs`
**Ubicación:** método `DrawSignalVisualization` (~línea 236)

Cambiar firma de:
```csharp
private void DrawSignalVisualization(bool isLong, int anchorBarIdx, double vwapPrice,
    double tp1, double tp2, int quantity, double sl)
```
A:
```csharp
private void DrawSignalVisualization(bool isLong, int anchorBarIdx, double vwapPrice,
    double tp1, double tp2, int quantity, double sl,
    string setupName = "", int anchorSequence = 0,
    DateTime anchorTime = default(DateTime))
```

Y en el constructor de `PendingSignal`:
```csharp
var sig = new PendingSignal
{
    // ... campos existentes ...
    SetupName = setupName,
    AnchorSequence = anchorSequence,
    AnchorTime = (anchorTime == default(DateTime)) ? Time[0] : anchorTime,
    SignalTime = Time[0]
};
```

---

## FASE 2 — Calcular MAE/MFE y Exportar CSV

### Tarea 2.1 — Agregar MAE/MFE al loop de simulación
**Archivo:** `RelativeVwap.Utilities.cs`
**Ubicación:** dentro del `for` loop de `DrawStoredSignalVisualization` (~línea 340-420)

Agregar variables ANTES del loop:
```csharp
double maxAdverse1 = 0;   // MAE para posición 1
double maxFavorable1 = 0; // MFE para posición 1
double maxAdverse2 = 0;   // MAE para posición 2
double maxFavorable2 = 0; // MFE para posición 2
```

DENTRO del loop, después de obtener `h` y `l` y ANTES de chequear SL:
```csharp
// Calcular MAE/MFE en tiempo real
if (q1Open || q2Open)
{
    double adverse = signal.IsLong
        ? Math.Max(0, entryPrice - l)   // Long: si baja es dolor
        : Math.Max(0, h - entryPrice);  // Short: si sube es dolor
    double favorable = signal.IsLong
        ? Math.Max(0, h - entryPrice)   // Long: si sube es ganancia
        : Math.Max(0, entryPrice - l);  // Short: si baja es ganancia

    if (q1Open) { maxAdverse1 = Math.Max(maxAdverse1, adverse); maxFavorable1 = Math.Max(maxFavorable1, favorable); }
    if (q2Open) { maxAdverse2 = Math.Max(maxAdverse2, adverse); maxFavorable2 = Math.Max(maxFavorable2, favorable); }
}
```

### Tarea 2.2 — Crear clase `SimTradeRecord`
**Archivo:** `RelativeVwap.Utilities.cs`
**Ubicación:** junto a `PendingSignal` (~línea 228)

```csharp
private class SimTradeRecord
{
    public string ID;
    public string Instrument;
    public DateTime EntryTime;
    public string Type;
    public double EntryPrice;
    public DateTime ExitTime;
    public double ExitPrice;
    public string Result;
    public double PnL;
    public double Commission;
    public double NetPnL;
    public double MAE;
    public double MFE;
    public string Setup;
    public int Attempt;
    public double RiskReward;
    public int LevelAge;
    public int Quantity;
    public string TradeClustID;
}
```

### Tarea 2.3 — Crear lista de registros
**Archivo:** `RelativeVwap.Utilities.cs`
**Ubicación:** junto a `_pendingSignals` (~línea 230)

```csharp
private List<SimTradeRecord> _simExportRecords = new List<SimTradeRecord>();
private int _dailyTradeCounter = 0;
private DateTime _lastExportDate = DateTime.MinValue;
```

### Tarea 2.4 — Función helper para calcular comisión
**Archivo:** `RelativeVwap.Utilities.cs`

```csharp
private double GetCommissionPerContract()
{
    string sym = Instrument.MasterInstrument.Name;
    // Round trip commission (entry + exit)
    if (sym.StartsWith("MNQ") || sym == "MNQ") return 4.10;
    if (sym.StartsWith("MES") || sym == "MES") return 2.50;
    if (sym.StartsWith("MCL") || sym == "MCL") return 2.10;
    if (sym.StartsWith("MGC") || sym == "MGC") return 2.10;
    if (sym.StartsWith("NQ") || sym == "NQ") return 4.10;
    if (sym.StartsWith("ES") || sym == "ES") return 4.10;
    if (sym.StartsWith("CL") || sym == "CL") return 4.10;
    if (sym.StartsWith("GC") || sym == "GC") return 4.10;
    return 4.10; // Default
}
```

### Tarea 2.5 — Crear registros al finalizar la simulación
**Archivo:** `RelativeVwap.Utilities.cs`
**Ubicación:** dentro de `DrawStoredSignalVisualization`, después del loop y ANTES del bloque `// 3. Draw Results`

```csharp
// === EXPORT RECORD CREATION ===
if (ExportSimulationCSV)
{
    string dateStr = signal.SignalTime.ToString("yyyyMMdd");
    if (signal.SignalTime.Date != _lastExportDate.Date) { _dailyTradeCounter = 0; _lastExportDate = signal.SignalTime; }
    _dailyTradeCounter++;

    string clustID = dateStr + "_" + _dailyTradeCounter;
    string direction = signal.IsLong ? "Long" : "Short";
    int levelAge = (int)(signal.SignalTime.Date - signal.AnchorTime.Date).TotalDays;

    double pointValue = Instrument.MasterInstrument.PointValue;
    double commission1 = GetCommissionPerContract() * qty1;
    double commission2 = GetCommissionPerContract() * qty2;

    // Posición 1 (TP1)
    if (exitBar1 != -1)
    {
        string result1 = win1
            ? string.Format("TP1_{0}_{1:00}", direction, signal.AnchorSequence + 1)
            : string.Format("SL_{0}_{1:00}", direction, signal.AnchorSequence + 1);
        double pnl1 = (signal.IsLong ? exitPrice1 - entryPrice : entryPrice - exitPrice1) * qty1 * pointValue;
        double rr1 = Math.Abs(entryPrice - signal.SL) > 0 ? Math.Abs(exitPrice1 - entryPrice) / Math.Abs(entryPrice - signal.SL) : 0;
        if (!win1) rr1 = -rr1;

        _simExportRecords.Add(new SimTradeRecord {
            ID = clustID,
            Instrument = Instrument.FullName,
            EntryTime = signal.SignalTime,
            Type = direction,
            EntryPrice = entryPrice,
            ExitTime = Time.GetValueAt(exitBar1),
            ExitPrice = exitPrice1,
            Result = result1,
            PnL = pnl1,
            Commission = commission1,
            NetPnL = pnl1 - commission1,
            MAE = maxAdverse1 * qty1 * pointValue,
            MFE = maxFavorable1 * qty1 * pointValue,
            Setup = signal.SetupName,
            Attempt = signal.AnchorSequence + 1,
            RiskReward = Math.Round(rr1, 2),
            LevelAge = levelAge,
            Quantity = qty1,
            TradeClustID = clustID
        });
    }

    // Posición 2 (TP2) — solo si cantidad > 0
    if (exitBar2 != -1 && qty2 > 0)
    {
        string result2 = win2
            ? string.Format("TP2_{0}_{1:00}", direction, signal.AnchorSequence + 1)
            : string.Format("SL_{0}_{1:00}", direction, signal.AnchorSequence + 1);
        double pnl2 = (signal.IsLong ? exitPrice2 - entryPrice : entryPrice - exitPrice2) * qty2 * pointValue;
        double rr2 = Math.Abs(entryPrice - signal.SL) > 0 ? Math.Abs(exitPrice2 - entryPrice) / Math.Abs(entryPrice - signal.SL) : 0;
        if (!win2) rr2 = -rr2;

        _simExportRecords.Add(new SimTradeRecord {
            ID = clustID + ".2",
            Instrument = Instrument.FullName,
            EntryTime = signal.SignalTime,
            Type = direction,
            EntryPrice = entryPrice,
            ExitTime = Time.GetValueAt(exitBar2),
            ExitPrice = exitPrice2,
            Result = result2,
            PnL = pnl2,
            Commission = commission2,
            NetPnL = pnl2 - commission2,
            MAE = maxAdverse2 * qty2 * pointValue,
            MFE = maxFavorable2 * qty2 * pointValue,
            Setup = signal.SetupName,
            Attempt = signal.AnchorSequence + 1,
            RiskReward = Math.Round(rr2, 2),
            LevelAge = levelAge,
            Quantity = qty2,
            TradeClustID = clustID
        });
    }
}
```

### Tarea 2.6 — Agregar propiedad `ExportSimulationCSV`
**Archivo:** `RelativeVwap.cs` (sección de properties, grupo "08. Gestión de Riesgo")

```csharp
[Display(Name = "Exportar Simulación CSV",
    Description = "Si TRUE, exporta los trades simulados a CSV compatible con Streamlit",
    GroupName = "08. Gestión de Riesgo (Simulación & Trading)", Order = 8)]
public bool ExportSimulationCSV { get; set; } = false;
```

### Tarea 2.7 — Crear método `WriteSimulatedTradesToCsv()`
**Archivo:** `RelativeVwap.Utilities.cs`

```csharp
private void WriteSimulatedTradesToCsv()
{
    if (_simExportRecords.Count == 0) return;

    try
    {
        // Build path: same folder as strategy exports
        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NinjaTrader 8", "bin", "Custom", "Strategies", "TradeExports", "DEMO619219"
        );

        if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);

        // Filename: VWAP_MNQ_03-26.csv
        string instrName = Instrument.MasterInstrument.Name.Replace(" ", "");
        string fileName = string.Format("VWAP_{0}_{1:MM-yy}.csv", instrName, DateTime.Now);
        string filePath = Path.Combine(baseDir, fileName);

        using (var sw = new StreamWriter(filePath, false, System.Text.Encoding.UTF8))
        {
            // Header
            sw.WriteLine("ID,Instrument,EntryTime,Type,EntryPrice,ExitTime,ExitPrice,Result," +
                "PnL,Commission,NetPnL,MAE,MFE,Setup,Attempt,RiskReward," +
                "DeltaAtEntry,DeltaDirection,SessionDelta,DeltaAtTP1,LevelAge,Quantity,Trade_Clust_ID");

            // Rows
            foreach (var r in _simExportRecords)
            {
                sw.WriteLine(string.Format(
                    "{0},{1},{2:yyyy-MM-dd HH:mm:ss},{3},{4:F2},{5:yyyy-MM-dd HH:mm:ss},{6:F2},{7}," +
                    "{8:F2},{9:F2},{10:F2},{11:F2},{12:F2},{13},{14},{15:F2}," +
                    "0,0,0,0,{16},{17},{18}",
                    r.ID, r.Instrument, r.EntryTime, r.Type, r.EntryPrice,
                    r.ExitTime, r.ExitPrice, r.Result,
                    r.PnL, r.Commission, r.NetPnL, r.MAE, r.MFE, r.Setup,
                    r.Attempt, r.RiskReward, r.LevelAge, r.Quantity, r.TradeClustID
                ));
            }
        }

        Print(string.Format("[EXPORT] CSV guardado: {0} ({1} trades)", filePath, _simExportRecords.Count));
    }
    catch (Exception ex)
    {
        Print("[EXPORT] ERROR: " + ex.Message);
    }
}
```

### Tarea 2.8 — Llamar `WriteSimulatedTradesToCsv()` al terminar de procesar
**Archivo:** `RelativeVwap.Utilities.cs`
**Ubicación:** final de `ProcessPendingSignals()` (~línea 290-295)

```csharp
// Al final de ProcessPendingSignals:
if (ExportSimulationCSV)
{
    WriteSimulatedTradesToCsv();
}
```

---

## FASE 3 — Integración Streamlit (opcional / mínima)

### Tarea 3.1 — Verificar que la app lee el CSV correctamente
Ejecutar la app y seleccionar el CSV `VWAP_MNQ_03-26.csv` en el selector de instrumentos.
Verificar que los tabs principales muestran datos.

### Tarea 3.2 — (Opcional) Agregar indicador visual de fuente
Si se quiere distinguir trades de estrategia vs trades simulados del VWAP indicator,
agregar columna `Source` con valor `"VWAP_Sim"` vs `"Strategy"`.
La app puede filtrar por esta columna en un sidebar.

---

## ORDEN DE IMPLEMENTACIÓN RECOMENDADO
1. Tarea 1.4 (modificar firma DrawSignalVisualization — sin parámetros obligatorios, usar defaults)
2. Tarea 1.1 (agregar campos a PendingSignal)
3. Tarea 1.2 + 1.3 (pasar datos desde bloques Signal 2)
4. Tarea 2.2 (crear SimTradeRecord)
5. Tarea 2.3 (crear lista y contadores)
6. Tarea 2.4 (helper comisión)
7. Tarea 2.1 (MAE/MFE en loop)
8. Tarea 2.5 (crear registros post-loop)
9. Tarea 2.6 (propiedad ExportSimulationCSV)
10. Tarea 2.7 (método WriteSimulatedTradesToCsv)
11. Tarea 2.8 (llamada al final de ProcessPendingSignals)
12. Tarea 3.1 (prueba en Streamlit)

---

## NOTAS IMPORTANTES
- `PendingSignal.SetupName` usa el nombre de sesión + "High"/"Low": "Asia High", "Europe Low", "USA Low"
- El `AnchorSequence` se debe leer ANTES de incrementarlo (el indicador lo incrementa post-señal)
- El CSV se escribe UNA SOLA VEZ cuando `ProcessPendingSignals()` termina (al pasar a Realtime)
- Si el usuario recarga el chart (F5), el CSV se sobreescribe — eso es correcto
- `Trade_Clust_ID` agrupa TP1 y TP2 del mismo trade para análisis lógicos en la app

---

## ARCHIVOS A MODIFICAR
1. `RelativeVwap.Utilities.cs` — mayor parte del trabajo
2. `RelativeVwap.cs` — dos llamadas a DrawSignalVisualization + propiedad

---
*Documento creado: 2026-02-07*
*Versión: 1.0*
