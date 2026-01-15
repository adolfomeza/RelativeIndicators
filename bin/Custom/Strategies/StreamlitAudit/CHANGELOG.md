# Changelog - Streamlit Trading Analysis App

## [v2.9.0] - 2026-01-15

### Fixed: Trade Counting Consistency with NinjaTrader 🔧
Corregida inconsistencia donde la app contaba EJECUCIONES en lugar de TRADES LÓGICOS.

#### Problema:
- El CSV exporta una línea por cada ejecución parcial (ej: Trade #2 → filas 2.1, 2.2, 2.3...)
- La app usaba `len(df)` para contar trades, contando cada fila como trade separado
- Esto causaba que Total Trades, Win Rate, y otras métricas no coincidieran con NinjaTrader Trade Performance

#### Solución:
- ✅ **compile_exec_summary()** - Ahora agrupa por `Trade_Clust_ID` antes de contar
- ✅ **compile_instrument_perf()** - Agrupa trades lógicos por instrumento
- ✅ **compile_levels_perf()** - Agrupa trades lógicos por zona de nivel
- ✅ **Win/Loss counting** - Usa PnL agregado por trade, no por ejecución

#### Resultado:
- 🎯 Total Trades ahora coincide con NinjaTrader Trade Performance
- 🎯 Win Rate calculado correctamente (trades ganadores / total trades)
- 🎯 Avg Win/Loss basado en PnL total por trade lógico
- 🎯 Profit Factor usando sumas correctas de ganancias/pérdidas

---

## [v2.8.3] - 2026-01-14

### Changed: Compact Layout - Optimized Space Usage 🗜️
Optimizado el uso del espacio en toda la aplicación reduciendo padding, márgenes y espacios vacíos innecesarios.

#### Global Improvements:
- ✅ **Padding Reducido** - Contenedores principales con menos padding (1rem vs 3rem)
- ✅ **Márgenes Compactos** - Espaciado entre elementos reducido de 1rem a 0.3rem
- ✅ **Headers Compactos** - Títulos y subtítulos con menos espacio vertical
- ✅ **KPIs Compactos** - Tarjetas de métricas con padding 12px vs 20px, fuente 1.5rem vs 1.8rem
- ✅ **Tabs Compactos** - Altura reducida a 40px (antes 50px), gap 16px (antes 24px)
- ✅ **Gráficos Compactos** - Padding 10px (antes 15px), margen 0.5rem (antes 10px)
- ✅ **Tablas Compactas** - Padding 8px (antes 10px), margen 0.5rem
- ✅ **Separadores Compactos** - Líneas horizontales con margen 0.5rem (antes 1rem)
- ✅ **Checkboxes/Radios Compactos** - Fuente 0.9rem, padding 0.2rem
- ✅ **Columnas Compactas** - Padding horizontal reducido a 0.3rem
- ✅ **Expanders Compactos** - Padding interno 0.5rem

#### Visual Impact:
- 🎯 **30-40% menos scroll** - Más contenido visible sin desplazamiento
- 🎯 **Uso eficiente del viewport** - Mejor aprovechamiento del espacio de pantalla
- 🎯 **Interfaz más densa** - Más información en menos espacio sin sacrificar legibilidad
- 🎯 **Experiencia profesional** - Dashboard style compacto y eficiente

#### Technical Details:
- CSS global aplicado a `.block-container`, `.element-container`, `div[data-testid="stVerticalBlock"]`
- Reducciones aplicadas a: headers (h1, h2, h3), markdown, metrics, columns, tabs, charts, tables, checkboxes, radios, expanders
- Mantenida la legibilidad con fuentes ajustadas proporcionalmente
- Preservados los efectos hover y transiciones visuales

### Added: Per-Instrument Equity Curves 📈
Restauradas y mejoradas las curvas de equity individuales por instrumento en Tab 1 (Tablero).

#### Features:
- ✅ **Curvas Individuales por Instrumento** - Cada instrumento muestra su propia curva de equity con color distintivo
- ✅ **Curva Global Total** - Línea verde gruesa (width=4) que muestra la equity combinada de todos los instrumentos
- ✅ **Visualización Limpia** - Las curvas individuales están **ocultas por defecto** (click en la leyenda para mostrarlas)
- ✅ **Paleta de Colores Mejorada** - Colores más distintivos por instrumento:
  - MNQ: Verde (#00FF99)
  - MES: Rojo (#FF6B6B)
  - MYM: Naranja (#FFA500)
  - M2K: Dorado (#FFD700)
  - MCL: Turquesa oscuro (#00CED1)
  - MGC: Rosa intenso (#FF1493)
  - MBT: Púrpura (#9370DB)
- ✅ **Monto de Equity en Título** - Muestra el valor final de equity en el título del gráfico (ej: "Equidad del Portafolio: $1,234.56")
- ✅ **Hover Tooltips con Contexto** - Muestra el nombre del instrumento y valor exacto (ej: "MNQ: $1,234.56")
- ✅ **Líneas Punteadas** - Las curvas individuales usan `dash='dot'` para distinguirse de la curva global

#### Technical Details:
- Usa `plotly.graph_objects` para crear múltiples traces en una sola figura
- Las curvas de instrumentos tienen opacidad 0.4 y están ocultas por defecto (`visible='legendonly'`)
- La curva global tiene width=4 (línea muy gruesa) vs width=1.2 para instrumentos
- Líneas punteadas (dash='dot') para curvas individuales
- Leyenda horizontal en la parte superior para mejor aprovechamiento del espacio
- Compatible con el selector de tipo de gráfico existente (Curva vs Barras)

#### UX Improvements:
- **Solución al "spaghetti chart"**: Al cargar, solo se muestra la curva TOTAL (limpia y clara)
- **Exploración bajo demanda**: Click en la leyenda para ver curvas individuales según necesidad
- **Mejor contraste**: Curva total en verde brillante, curvas individuales más tenues y punteadas

### Changed: Horizontal Checkbox Filters for Better UX ☑️
Convertidos los filtros de Tab 1 a checkboxes horizontales para aprovechar mejor el espacio y mejorar la usabilidad.

#### Improvements:
- ✅ **Layout Horizontal** - Filtros distribuidos horizontalmente en lugar de verticales para mejor uso del espacio
- ✅ **Filtro de Niveles** - Checkboxes en 6 columnas horizontales (Asia High, Europe High, USA High, etc.)
- ✅ **Filtro de Instrumentos** - Checkboxes en fila horizontal para todos los instrumentos (MNQ, MES, MYM, etc.)
- ✅ **Filtro de Intentos** - Checkboxes en 10 columnas con etiquetas cortas "Int 1", "Int 2", etc.
- ✅ **"Todos"** - Cada filtro incluye un checkbox compacto para activar/desactivar todas las opciones
- ✅ **Menos Scroll** - Diseño compacto que reduce el scroll vertical necesario
- ✅ **Vista Completa** - Todas las opciones visibles de un vistazo sin abrir dropdowns
- ✅ **Activación Rápida** - Un solo click para activar/desactivar cualquier opción

#### Technical Details:
- Reemplazados `st.multiselect` por `st.checkbox` en layout horizontal usando `st.columns()`
- Filtro de Niveles: hasta 6 columnas horizontales
- Filtro de Instrumentos: 1 columna por instrumento (típicamente 7-8)
- Filtro de Intentos: hasta 10 columnas horizontales con etiquetas cortas
- Cada checkbox tiene un key único para evitar conflictos de estado
- Los checkboxes "Todos" controlan el valor inicial de los checkboxes individuales
- La lógica de filtrado permanece sin cambios (usa las mismas listas `selected_*`)

### Fixed: Attempt Filter Column Mapping 🔧
Corregida la lectura de la columna "Attempt" del CSV para mostrar todos los valores de intentos (1-10+) en el filtro.

#### Problem:
- El filtro "Intentos" en Tab 1 solo mostraba valor "1" cuando los datos reales contenían intentos de 1 a 10+
- **Causa Raíz:** CSV tiene 21 columnas de datos pero el header solo declara 20 nombres
  - Header: `ID,Instrument,...,DeltaAtTP1` (20 nombres)
  - Datos: 21 valores por fila
  - Columna 15 (header "Attempt") siempre contiene valor 1
  - Columna 21 (sin nombre en header) contiene los valores reales de intentos (1, 3, 8, 10, etc.)
  - Pandas creaba columna "Attempt" desde posición 15 e ignoraba la columna 21 sin nombre

#### Solution:
- Actualizada la definición `col_names_new` para mapear las 21 columnas correctamente:
  - Columna 15 renombrada a `Attempt_UNUSED` (siempre vale 1, no se usa)
  - Agregadas columnas Delta: `DeltaAtEntry`, `DeltaDirection`, `SessionDelta`, `DeltaAtTP1`
  - Columna 21 (última, sin nombre en header original) correctamente mapeada como `Attempt`
- **Fix crítico:** Pandas descartaba la columna 21 al detectar mismatch entre header (20 nombres) y datos (21 valores)
- Solución: Ignorar header defectuoso usando `header=None, skiprows=1` para forzar lectura de todas las columnas

#### Result:
- Filtro "Intentos" ahora muestra todos los valores únicos de intentos presentes en los datos
- Usuarios pueden filtrar por nivel de intento (1er intento, 2do intento, etc.)

---

## [v2.8.2] - 2026-01-04

### Fixed: Calendar Tab Jump on Page Reload 📅
- **Problema:** Al recargar la página (F5), la app saltaba automáticamente al tab "Calendario" si previamente habías clickeado en un día del calendario.
- **Causa:** El query param `audit_date` persistía en la URL y el script JS de restauración de tab se re-ejecutaba en cada recarga.
- **Solución:** Limpiar el query param `audit_date` inmediatamente después de guardarlo en `session_state` con `del st.query_params["audit_date"]`.
- **Resultado:** Al recargar, la app se mantiene en el tab "Tablero" (comportamiento normal) en lugar de saltar al Calendario.

---

## [v2.8.1] - 2026-01-04

### Fixed: AI Cache Cost Display 💰
Corrección en cómo se muestra y acumula el costo de la sesión de IA.
- **Antes:** Al volver a una pestaña, se sumaba el costo del análisis (incluso si venía de caché) al total de la sesión, dando la impresión de un doble cobro.
- **Ahora:** El contador de "Sesión Actual" solo incrementa cuando realmente se hace una llamada a la API (Cache Miss). Si el dato viene de memoria, el costo de sesión no aumenta.
- **Beneficio:** Transparencia total. El usuario ve exactamente lo que está gastando *ahora*, sin "cobros fantasma".

### Fixed: Calendar Tab Interaction 📅
Corrección del comportamiento al hacer clic en un día del calendario.
- **Problema:** Al hacer clic en un día, la app se recargaba y volvía a la primera pestaña (Dashboard), perdiendo el foco.
- **Solución:** Mejora en el script JS de restauración de pestaña con polling más robusto y selectores actualizados.
- **Resultado:** Al hacer clic en un día, la app se mantiene en la pestaña "Calendario" y muestra el detalle del día seleccionado correctamente.

### Fixed: Month Selector Persistence 🗓️
Corrección de estado en el selector de meses.
- **Problema:** Al seleccionar un mes, la selección se perdía al recargar la página.
- **Solución:** Se agregó una `key` única al widget para persistir su estado.

---

## [v2.8.0] - 2026-01-03

### Added: R-Ladder MFE Analysis 📊
Análisis granular de MFE (Maximum Favorable Excursion) en incrementos de 1R hasta 20R.

#### Features:
- ✅ **Función `analyze_r_ladder()`** - Calcula cuántos trades alcanzaron cada nivel R
- ✅ **Tabla Detallada** - Muestra para cada nivel R:
  - Cantidad de trades que lo alcanzaron
  - Porcentaje del total
  - PnL potencial si se hubiera salido en ese nivel
  - PnL acumulado
- ✅ **Identificación de Punto Dulce** - Algoritmo automático que encuentra el mejor balance entre probabilidad de alcance y ganancia
- ✅ **Recomendaciones TP** - Sugerencias automáticas para TP1 (probabilidad alta) y TP2 (óptimo)
- ✅ **Advertencias** - Identifica niveles R con menos del 10% de alcance
- ✅ **Visualización Interactiva** - Gráfico de cascada con:
  - Barras verdes: % de trades que alcanzan cada nivel R
  - Línea azul: PnL acumulado potencial
  - Ejes duales para mejor comparación
- ✅ **Integración en Tab 10** - Sección 7 del Executive Report
- ✅ **Tabla Expandible** - Vista detallada de todos los datos

#### Technical Details:
- Cálculo: `MFE_R = MFE / MAE` (cuántas veces el riesgo capturamos como ganancia)
- Validación automática de datos (filtra MAE = 0 o NaN)
- Análisis limitado a primeros 10R para evitar outliers en recomendaciones
- Utiliza Plotly para gráficos interactivos con tema premium

### Added: Scaling Out Simulation 🎯
Simulación de estrategias de salida distribuyendo contratos uniformemente entre niveles R.

#### Features:
- ✅ **Función `analyze_scaling_out()`** - Simula diferentes tamaños de posición (3, 5, 10, 20 contratos)
- ✅ **Distribución Dinámica** - Asigna contratos uniformemente entre niveles R alcanzados
  - Ejemplo: 20 contratos = 1 contrato cada 1R (1R, 2R, 3R... 20R)
  - Ejemplo: 5 contratos = 1 contrato cada 4R (4R, 8R, 12R, 16R, 20R)
  - Ejemplo: 3 contratos = 1 contrato cada ~7R (7R, 14R, 20R)
- ✅ **Comparación vs Sistema Actual** - Calcula PnL total para cada estrategia y compara contra TP1/TP2 actual
- ✅ **Identificación de Mejor Estrategia** - Marca con ⭐ la configuración de mayor PnL
- ✅ **Recomendaciones Detalladas** - Si scaling out es mejor, lista los niveles R específicos a usar
- ✅ **Advertencias Realistas** - Incluye notas sobre slippage, comisiones y complejidad operativa
- ✅ **Integración en Tab 10** - Sección 8 del Executive Report

#### Use Case:
Permite evaluar si un enfoque de "múltiples salidas graduales" (scaling out) superaría al sistema actual de 2 TPs fijos, optimizando la captura de ganancia promedio.

---

## [v1.16.0] - 2026-01-03

### Added: TAB 10 - Reporte Ejecutivo IA 🎯
Nuevo tab que compila TODOS los análisis en un reporte estratégico único con código C# generado.

#### Secciones del Reporte:
1. **Resumen Ejecutivo** - Métricas globales, veredicto del sistema
2. **Performance por Instrumento** - Análisis detallado por cada instrumento con recomendaciones
3. **Análisis de Niveles** - Top 5 mejores/peores zonas globales
4. **Filtros Recomendados** - Zonas tóxicas a deshabilitar con impacto estimado
5. **Código C# Generado** - Filtros listos para copiar/pegar en estrategia
6. **Plan de Acción** - Próximos pasos concretos y advertencias

#### Features:
- ✅ Botón "Generar Reporte Completo"
- ✅ Botón "Exportar (.txt)" - Descarga archivo con timestamp
- ✅ Análisis IA Profundo (opcional, requiere API key)
- ✅ Formato monospace para mejor legibilidad del código
- ✅ Verdicts automáticos (RENTABLE/MARGINAL/PERDEDOR)
- ✅ Estimaciones de impacto de filtros en PnL

---

## [v1.15.0] - 2026-01-03

### Added: Deep Level Analysis Module 🎯
Módulo completo de análisis de niveles de trading con 6 secciones:

#### Sección 1: Dashboard de Rendimiento
- Tabla completa con métricas: PnL Total, Win Rate, R:R, Sharpe, Avg Win, Trades
- Ordenada por rentabilidad total
- Insights automáticos identificando mejor/peor zona
- Detección de zonas premium (Sharpe > 1.5)
- Análisis IA con datos formateados (PnL, WR, RR, sample size, verdicts)

#### Sección 2: Matriz Direccional
- Análisis Long vs Short por zona
- Dos tablas: PnL y Win Rate por dirección
- Detección de sesgo direccional (diferencia > 20%)
- Identificación de zonas que solo operan una dirección
- Insights automáticos sugiriendo deshabilitar dirección débil
- Análisis IA con veredictos RENTABLE/PERDEDOR

#### Sección 3: Análisis Temporal
- Heatmap Zone x Hour mostrando PnL por hora del día
- Detección automática de ventanas tóxicas (pérdida > $100)
- Tabla de combinaciones Zone+Hour problemáticas
- Código C# sugerido para filtrar horarios
- Análisis IA con contexto de sesiones de mercado

#### Sección 4: Penetration Analysis
- Scatter plot MAE vs PnL (ya existía, mantenido)
- Identificación de "Punto de No Retorno"
- Cálculo de threshold del 95% de winners

#### Sección 5: Filtros Tóxicos
- Análisis Zone+Direction+Hour (3 variables)
- Top 10 peores combinaciones
- Win Rate y PnL por combo
- Impacto estimado de filtrarlas
- Análisis de patrones (zona/hora más problemática)
- Código C# completo para implementar filtros
- Análisis IA con veredictos RUIDO/SISTEMÁTICO

#### Sección 6: Recomendaciones Accionables
- Lista "EVITAR": Zonas con WR < 40% y PnL negativo
- Lista "PRIORIZAR": Edges confirmados (WR > 60%, PnL > $500)
- Recomendaciones direccionales específicas

### Improved: AI Data Formatting
- **Problema Resuelto:** IA recibía dicts complejos y malinterpretaba datos
  - Ejemplo: Rechazaba Asia High SHORT (WR 25%, PnL +$230) solo por WR bajo
- **Solución:** Datos formateados en texto plano con:
  - Verdicts: ✅ RENTABLE / ❌ PERDEDOR
  - Sample size warnings: ⚠️ MUESTRA PEQUEÑA
  - Reglas de interpretación ("WR bajo + PnL positivo = R:R alto, válido")
  - Contexto de mercado (sesiones, volumen)
- **Secciones arregladas:**
  - Performance Dashboard
  - Directionality Matrix  
  - Temporal Performance
  - Toxic Combinations

### Improved: Layout
- Dashboard y Decaimiento Temporal ocupan todo el ancho (antes en columnas 50/50)
- Mejor balance visual

---

## [v1.14.16] - 2026-01-02

### Added: Trade Count Synchronization
- Agrupación de trades basada en columna `ID` del CSV (ej: "105.1", "105.2")
- Extrae Parent ID para agrupar correctamente
- **Resultado:** Conteo exacto 1:1 con NinjaTrader Strategy Analyzer
- Resuelve discrepancia donde 23 ejecuciones mostraban como 19 trades

---

## [v1.14.15] - 2026-01-02

### Added: Dynamic Commission Calculator
- Selector de licencia NinjaTrader (Free/Lifetime) en sidebar
- Tabla de tasas oficiales 2025 por instrumento
- Recalcula NetPnL automáticamente según licencia seleccionada
- Muestra comisiones totales en métricas principales

### Tasas Implementadas:
**Free Plan:**
- MNQ: $0.89/lado
- MES: $0.85/lado  
- M2K: $0.89/lado
- MYM: $0.89/lado
- MCL: $1.89/lado
- MGC: $1.89/lado
- Resto: $0.99/lado

**Lifetime Plan:**
- MNQ/MES/M2K/MYM: $0.59/lado
- MCL/MGC: $1.29/lado
- Resto: $0.69/lado

---

## [v1.14.14] - 2026-01-02

### Added: Automatic Data Deduplication
- Detecta y elimina registros duplicados en CSVs
- Criterio: `['ID', 'Instrument', 'EntryTime', 'ExitTime', 'Type']`
- **Problema Resuelto:** Múltiples backtests inflaban datos
- Muestra advertencia si detecta duplicados

---

## [v1.13.0 - v1.14.13] - 2026-01-01

### Added: AI Integration (Gemini)
- Motor de análisis cuantitativo con Gemini Pro
- Análisis breve (gratis, automático) para cada gráfico
- Botón "Ver Análisis Completo" (premium, requiere API key)
- Tipos soportados:
  - Equity Curve
  - Long vs Short
  - Setup Performance
  - Tier Analysis
  - Drawdown
  - MAE/MFE
  - Monte Carlo
  - Calendar
  - Hourly
  - Levels
  - Interaction Matrix
  - Performance Dashboard
  - Directionality Matrix
  - Temporal Performance
  - Toxic Combinations

### Added: AI Engine
- `ai_engine.py`: Motor con prompts especializados
- Modelo rápido: `gemini-2.0-flash` (brief)
- Modelo completo: `gemini-2.5-pro` (full analysis)
- Cache de 30 minutos para optimizar llamadas
- Configuración vía `.env` (GEMINI_API_KEY)

---

## [v1.0.0 - v1.12.0] - 2025-12-XX

### Core Features
- Visualización de curva de equity
- Análisis Long vs Short
- Performance por setup
- Análisis de tiers (TP1/TP2/TP3)
- Drawdown analysis
- MAE/MFE scatter
- Simulación Monte Carlo
- Calendario de trades
- Análisis horario
- Matriz de interacción (Agresor vs Defensor)
- Live vs Backtest comparison

---

## Dependencies
```
streamlit
pandas
plotly
google-generativeai
python-dotenv
numpy
matplotlib
```

---

## GitHub Workflow
Ver: `PROJECT_RULES.md` para instrucciones completas de versionado y commits
