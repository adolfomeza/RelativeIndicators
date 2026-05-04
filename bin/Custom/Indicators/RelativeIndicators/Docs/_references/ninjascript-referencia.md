# Guía de Referencia Rápida de NinjaScript

## Estados del Ciclo de Vida (OnStateChange)
| Estado | Usar Para |
|--------|-----------|
| SetDefaults | Valores por defecto de parámetros, AddPlot(), Name, Description |
| Configure | AddDataSeries(), AddHeikenAshi(), dependencias |
| DataLoaded | Inicializar Series<T>, cachear refs de indicadores (SMA, EMA, etc.) |
| Historical | Se ejecuta durante el procesamiento de barras históricas |
| Realtime | Transición a datos en tiempo real |
| Terminated | Limpieza, liberar recursos |

## Hoja de Trucos para Acceso a Series
```
Close[0]  = cierre de la barra actual
Close[1]  = cierre de la barra anterior
Close[n]  = cierre de n barras atrás

Closes[0][0] = barra actual de la serie primaria
Closes[1][0] = barra actual de la serie secundaria
```

## Atributos de Propiedades
```csharp
[NinjaScriptProperty]           // Aparece en el strategy builder
[Range(1, int.MaxValue)]        // Validación de rango de valores
[Display(Name="", Order=1, GroupName="Parámetros")]  // Visualización en UI
[Browsable(false)]              // Ocultar del panel de propiedades
[XmlIgnore]                     // No serializar
```

## Herramientas de Dibujo
```csharp
Draw.Line(this, tag, barraInicio, yInicio, barraFin, yFin, brush);
Draw.ArrowUp(this, tag, autoEscala, barrasAtras, y, brush);
Draw.ArrowDown(this, tag, autoEscala, barrasAtras, y, brush);
Draw.Text(this, tag, texto, barrasAtras, y, brush);
Draw.TextFixed(this, tag, texto, TextPosition.TopRight);
Draw.Diamond(this, tag, autoEscala, barrasAtras, y, brush);
Draw.Dot(this, tag, autoEscala, barrasAtras, y, brush);
Draw.HorizontalLine(this, tag, precio, brush);
Draw.VerticalLine(this, tag, tiempo, brush);
Draw.Rectangle(this, tag, barrasAtrasInicio, yInicio, barrasAtrasFin, yFin, brush);
Draw.Region(this, tag, barrasAtrasInicio, barrasAtrasFin, serie1, serie2, brush, brush, opacidad);
RemoveDrawObject(tag);
```

## Alertas y Sonido
```csharp
Alert("miAlerta", Priority.High, "Mensaje", NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav", 10, Brushes.Green, Brushes.White);
PlaySound(NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav");
```

## Indicadores Incorporados Comunes
```csharp
SMA(periodo)[barrasAtras]
EMA(periodo)[barrasAtras]
WMA(periodo)[barrasAtras]
ATR(periodo)[barrasAtras]
RSI(periodo, suavizado)[barrasAtras]
MACD(rapido, lento, senal).Value[barrasAtras]
MACD(rapido, lento, senal).Avg[barrasAtras]
MACD(rapido, lento, senal).Diff[barrasAtras]
Bollinger(desviacion, periodo).Upper[barrasAtras]
Bollinger(desviacion, periodo).Middle[barrasAtras]
Bollinger(desviacion, periodo).Lower[barrasAtras]
Stochastics(periodoD, periodoK, suavizado).D[barrasAtras]
Stochastics(periodoD, periodoK, suavizado).K[barrasAtras]
CCI(periodo)[barrasAtras]
ADX(periodo)[barrasAtras]
VOL()[barrasAtras]
VWAP().Value[barrasAtras]
```

## Métodos Útiles
```csharp
CrossAbove(serie1, serie2, lookback)    // Cruce por encima
CrossAbove(serie1, valorDouble, lookback)
CrossBelow(serie1, serie2, lookback)    // Cruce por debajo
IsRising(serie)                          // ¿Está subiendo?
IsFalling(serie)                         // ¿Está bajando?
MAX(serie, periodo)[barrasAtras]         // Máximo de la serie
MIN(serie, periodo)[barrasAtras]         // Mínimo de la serie
SUM(serie, periodo)[barrasAtras]         // Suma de la serie
Slope(serie, periodo, barrasAtras)       // Pendiente
```

## Tiempo de Barra y Sesión
```csharp
Time[0]              // DateTime de la barra actual
Bars.IsFirstBarOfSession  // ¿Es la primera barra de la sesión?
Bars.IsLastBarOfSession   // ¿Es la última barra de la sesión?
ToDay(Time[0])       // int YYYYMMDD
ToTime(Time[0])      // int HHmmss
```

## Trucos de Depuración
```csharp
// Imprimir en ventana de Output
Print("Barra: " + CurrentBar + " Cierre: " + Close[0]);

// Imprimir solo en tiempo real
if (State == State.Realtime)
    Print("Tick en tiempo real: " + Close[0]);

// Medir rendimiento
var sw = System.Diagnostics.Stopwatch.StartNew();
// ... código a medir ...
sw.Stop();
Print("Tiempo: " + sw.ElapsedMilliseconds + "ms");
```

## Manejo de Colores Dinámicos
```csharp
// Cambiar color del plot según condición
PlotBrushes[0][0] = valor > 0 ? Brushes.Green : Brushes.Red;

// Cambiar color de fondo de la barra
BackBrush = esSenal ? Brushes.LightGreen : null;

// Cambiar color de la barra de precio
BarBrush = Brushes.Yellow;        // Color del cuerpo
CandleOutlineBrush = Brushes.Red; // Color del borde
```
