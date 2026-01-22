# 🤖 Guía de Integración IA - SessionLevelsStrategy (v1.15.43)

Esta guía explica cómo utilizar el nuevo sistema de configuración automática basado en Inteligencia Artificial.

## 1. El Flujo de Trabajo (Workflow)

El sistema conecta tu análisis de datos (Streamlit App) directamente con tu ejecución en NinjaTrader.

1.  **Analizar**: La App procesa tus trades históricos y detecta qué zonas son rentables y cuáles son tóxicas.
2.  **Configurar**: La App genera un archivo "cerebro" (`ai_config.json`) con las reglas óptimas.
3.  **Ejecutar**: La Estrategia en NinjaTrader lee este archivo y **se bloquea automáticamente** ante setups malos.

---

## 2. Pasos en la App (Streamlit)

1.  Abre la aplicación: `streamlit run app.py`.
2.  Ve a la sección **"Configuración Automática"** (Sidebar o Pestaña dedicada).
3.  Verás las sugerencias de la IA basadas en tu historial:
    *   **Zonas Habilitadas**: Lista de zonas con PnL positivo (ej. `Asia High`, `USA Low`).
    *   **Edad Máxima (Días)**: Sugerencia de hasta cuántos días atrás un nivel sigue siendo relevante.
    *   **Intentos Máximos**: Cuántas veces rebotar en un mismo nivel.
4.  Ajusta los valores si lo deseas.
5.  Haz clic en **"Generar Configuración (ai_config.json)"**.
    *   *Confirmación*: Verás un mensaje indicando que el archivo se guardó en `.../Strategies/StreamlitAudit/ai_config.json`.

---

## 3. Pasos en NinjaTrader 8

1.  Abre tu estrategia `SessionLevelsStrategy`.
2.  Ve al grupo de parámetros **"AI Integrations"**.
3.  Activa la casilla: `Auto Load AI Config`.
4.  (Opcional) Verifica la ruta en `AI Config Path` (por defecto ya apunta a la carpeta correcta).
5.  Haz clic en **OK** o **Apply**.

### ¿Cómo sé si funcionó?
Abre la ventana de **Output** en NinjaTrader (`New` -> `NinjaScript Output`). Al activar la estrategia, verás mensajes como:

```text
AI CONFIG: Leyendo configuración...
AI CONFIG: Max Age actualizado -> 5
AI CONFIG: Loaded MaxRetriesPerLevel = 2
AI CONFIG: Activas 4 zonas: Asia High, Asia Low, USA High, USA Low
```

---

## 4. Nuevas Columnas en el CSV (Reportes)

Para que la IA aprenda mejor en el futuro, la estrategia ahora exporta más datos en cada trade:

*   **EntryMode**: ¿Entraste en modo `A+` (esperando confirmación) o `Anticipado`?
*   **ExitStrategy**: ¿Usaste salida `Standard` (TP1/TP2) o `Ladder`?
*   **RiskModel**: ¿Usaste riesgo `Fijo` o `Dinámico` (% Balance)?

Estos datos permitirán a la App generar reportes como: *"La estrategia Ladder funciona mejor en Asia High, pero la Standard es mejor en USA Low"*.

---

## 5. Solución de Problemas

**P: La estrategia no toma trades válidos.**
R: Revisa el Output. ¿Está la zona habilitada en la lista de la IA? Si la IA detectó que `Asia High` pierde dinero, la estrategia la bloqueará. Para forzar una operación, desactiva `Auto Load AI Config`.

**P: Me da error "Archivo no encontrado".**
R: Asegúrate de haber pulsado "Generar Configuración" en la App al menos una vez. Verifica que la ruta en NinjaTrader coincida con donde la App guarda el archivo.
