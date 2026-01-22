# SessionLevelsStrategy - Guía Completa de Auditoría y Trading

**Versión Estrategia**: v1.15.38 (16 Enero 2026)  
**Versión App**: v2.11.0 (17 Enero 2026)  
**Última Actualización**: 17 Enero 2026

---

## 🧭 Introducción

**SessionLevelsStrategy** es un ecosistema de trading algorítmico híbrido. No es solo una estrategia de ejecución en NinjaTrader 8; es un sistema completo que integra:
1.  **Ejecución Precisa (C#)**: Una estrategia de NinjaTrader que detecta niveles institucionales, gestiona entradas con precisión de tick y protege el capital dinámicamente.
2.  **Auditoría Cuantitativa (Python/Streamlit)**: Una aplicación web local que analiza la data histórica para encontrar "Edges" (ventajas estadísticas) y "Leaks" (fugas de capital).
3.  **Inteligencia Artificial (Gemini Pro)**: Un motor de razonamiento integrado que interpreta las métricas complejas y ofrece consejos tácticos en lenguaje natural.

---

## 🤖 Auditoría Cuantitativa con IA

La joya del sistema es su capacidad de auto-análisis. La App de Auditoría (`StreamlitAudit`) no solo muestra gráficos, sino que **razona** sobre ellos usando Google Gemini.

### Capacidades de la IA
*   **Detector de Patrones Tóxicos**: Analiza combinaciones de factores (Hora + Zona + Dirección) que sistemáticamente pierden dinero.
    *   *Ejemplo*: "Evita operar Shorts en Asia High entre las 12:00 y 13:00".
*   **Análisis de Penetración (MAE)**: Determina la "frontera de dolor" óptima.
    *   *Insight*: "El 95% de tus trades ganadores nunca soportan más de $85 de drawdown. Si el precio baja $90, corta la pérdida; ya no es un pullback, es un cambio de tendencia."
*   **Matriz de Interacción**: Descubre quién domina a quién.
    *   *Insight*: "La sesión Americana rompe los niveles de Asia consistentemente al alza, pero falla al intentar romper los de Europa."

### ¿Cómo activar la IA?
En el sidebar de la aplicación web, encontrarás el control **"🤖 Configuración IA"**. 
*   **Activar Auditor IA**: Habilita el envío de métricas anonimizadas a Gemini para recibir insights.
*   Si no tienes una API Key válida, desactívalo para usar la app en modo estadístico clásico.

---

## ⚙️ Mecánica de la Estrategia (NinjaTrader 8)

### Mecánica de Entrada Detallada (El "Algoritmo de Selección")

No todos los toques son iguales. La estrategia emplea un algoritmo avanzado para filtrar el ruido:

1.  **Selección de "Nivel Profundo" (Deepest Level Logic)**
    *   ¿Qué pasa si una vela gigante rompe tres niveles a la vez?
    *   La estrategia **NO** entra en el primero que toca.
    *   Escanea todos los niveles rotos y selecciona el precio **más ventajoso**:
        *   *Long*: El nivel con el precio más BAJO.
        *   *Short*: El nivel con el precio más ALTO.
    *   *Resultado*: Compras más barato y vendes más caro, evitando entrar "a mitad de camino".

2.  **La Confirmación de Ruptura (Confirmación Visual)**
    *   No basta con tocar el nivel. El precio debe demostrar rechazo.
    *   **Regla**: Cualquier vela posterior al toque que cierre respetando el VWAP del Setup (confirmación diferida).
        *   *Short*: High de la vela < VWAP - 1 tick.
        *   *Long*: Low de la vela > VWAP + 1 tick.
    *   Esto permite que el precio "baile" en el nivel antes de decidirse, filtrando falsos rebotes inmediatos.

3.  **Validación de Riesgo/Recompensa (R:R)**
    *   Antes de poner la orden, el sistema calcula:
        *   **Riesgo**: Distancia al Stop Loss (Ancla +/- 1 tick).
        *   **Recompensa**: Distancia al VWAP Objetivo.
    *   Si `Recompensa / Riesgo < 1.0`, la operación se **aborta inmediatamente**.
    *   *Filosofía*: "No arriesgues 1 dólar para ganar 50 centavos".

---

### Mecánica de Gestión de Posición (El "Manager")

Una vez dentro, el `OrderProtectionManager` toma el control:

1.  **TP1: El Financiador (Dinámico)**
    *   **Objetivo**: VWAP Global del Trade.
    *   **Comportamiento**: Se mueve con el mercado. Si el VWAP sube, tu TP1 sube. Se adapta a la volatilidad real.
    *   **Misión**: Asegurar ganancia en el 50% de la posición para financiar el riesgo.

2.  **TP2: El corredor (Estático Intencional)**
    *   **Objetivo**: Nivel de Sesión Opuesto "Más Extremo".
    *   **Selección**: Si estás en Long desde Asia Low, busca el Asia High (o USA High si es del mismo día). Si hay varios, elige el que esté más lejos para maximizar el ratio.
    *   **Misión**: Capturar la expansión del rango.

3.  **Stop Loss y Breakeven**
    *   **Inicial**: Colocado técnicamente en el "Ancla" del movimiento (el punto extremo del giro) +/- 1 tick.
    *   **Evento Breakeven**: En el milisegundo que se llena el TP1, el SL del resto de la posición se mueve automáticamente a tu precio de entrada.
    *   *Resultado*: "Risk Free Trade". Si el mercado se da la vuelta, sales tablas en la segunda mitad, pero ya cobraste la primera.

### 4. Gestión de Riesgo "Supervivencia" (Adaptive SL)
El mercado a veces salta órdenes (slippage extremo).
*   **SL Dinámico**: Si el precio salta tu Stop Loss inicial, la estrategia no se queda paralizada. Detecta la anomalía y coloca una orden de emergencia a `Precio Actual +/- 4 ticks`.
*   **Prioridad**: Salir del mercado a cualquier costo razonable antes que sufrir una pérdida catastrófica.

### 5. Smart Rollover (Costura de Contratos)
Resuelve el problema de la data histórica discontinua en futuros.
*   **Auto-Stitch**: Al analizar backtests de largo plazo (ej: 1 año), el sistema detecta automáticamente las fechas de vencimiento oficiales (Rollover Dates).
*   **Limpieza de Zombis**: Elimina los días donde operaste el contrato viejo sin volumen, uniendo las curvas de equidad de forma perfecta.

---

## 📊 Dashboard de Rendimiento (Streamlit App v2.11+)

La interfaz ha sido consolidada para máxima eficiencia.

### Tablero Principal (Tab 1)
Centraliza toda la inteligencia crítica:
1.  **KPIs Clave**: Profit Factor, Win Rate Real (por trade, no por ejecución), Drawdown.
2.  **Curva de Equidad**: Visualiza el crecimiento de tu cuenta capital.
3.  **Matriz de Interacción**: Mapa de calor que muestra qué sesión es el mejor "Depredador" y cuál es la mejor "Presa".
4.  **Análisis de Niveles**: Rendimiento desglosado por zona (Asia High, USA Low, etc.) con insights de IA.
5.  **Combinaciones Tóxicas**: Lista negra de setups que debes filtrar.

### Análisis de Escala ("La Cebolla") (Tab 2)
¿Vale la pena dejar correr las ganancias (Runners)?
*   Analiza cada Tier (TP1, TP2, TP3) por separado.
*   **Sharpe Proxy**: Te dice si el riesgo extra de buscar un Home Run está justificado por el retorno.

### Análisis de Penetración (Tab 4)
*   **Gráfico de Eficiencia**: Scatter plot de MAE (Maximum Adverse Excursion) vs PnL.
*   Ayuda a ajustar el Stop Loss técnico: "¿Estoy regalando dinero con un stop muy amplio o me están sacando por violines?"

---

## 🛠️ Guía Rápida de Uso

### Pasos para una Auditoría Completa
1.  **Backtest en NinjaTrader**: Ejecuta la estrategia en un periodo amplio (3-6 meses).
2.  **Exportación Automática**: La estrategia guarda automáticamente un CSV en `Documents/NinjaTrader 8/TradeExports/`.
3.  **Abrir App**: Ejecuta el script `run_app.bat` (o `streamlit run app.py`).
4.  **Cargar Data**: En el sidebar, selecciona "📂 Backtest" y elige tu archivo más reciente.
5.  **Interrogar**: Revisa los cuadros azules de "Insight de Experto Quant" generados por la IA.

### Solución de Problemas Comunes
*   **"Spinner Infinito"**: Posiblemente tu API Key de Gemini expiró.
    *   *Solución*: Ve al sidebar -> Configuración IA -> Desactiva "Activar Auditor IA".
*   **Datos "Raros" en Viernes**: Revisa que tu hora de cierre de sesión sea correcta. La versión v1.15.38 corrigió un bug específico de reset en viernes.

---

## 📅 Historial de Cambios Recientes

| Fecha | Versión App | Cambio Clave |
|-------|-------------|--------------|
| 17/01 | v2.11.0 | **Fusión UI**: Análisis de Niveles integrado al Dashboard. |
| 17/01 | v2.11.0 | **Fix Matriz**: IA ahora recibe tabla completa para evitar alucinaciones. |
| 17/01 | v2.11.0 | **Fix Charts**: Soporte para PnL por Intento en IA. |
| 16/01 | v2.10.x | **Smart Rollover**: Costura automática de contratos futuros. |
| 16/01 | v1.15.38 (Strat) | **Fix Reset Viernes**: Lógica crítica de fin de semana corregida. |

---

*Documento generado automáticamente por el sistema de documentación dinámica.*
