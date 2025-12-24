# Análisis de SessionLevelsStrategy.cs

## Resumen General
La estrategia `SessionLevelsStrategy` es un sistema de trading automatizado complejo para NinjaTrader 8. Su núcleo se basa en la identificación de niveles de Soporte y Resistencia generados por los extremos (High/Low) de sesiones de mercado específicas (Asia, Europa, USA) y utiliza VWAP (Volume Weighted Average Price) anclado dinámicamente para filtrar y ejecutar entradas.

El código consta de aproximadamente 2007 líneas y destaca por su robustez en la gestión de estado, persistencia de datos y mecanismos de seguridad (fail-safes).

## Arquitectura y Componentes Clave

### 1. Gestión de Sesiones (`CheckSession`, `ManageLevels`)
- **Sesiones Definidas:** Asia, Europa y USA, con horarios configurables.
- **Formación de Niveles:** 
  - Durante el horario de una sesión, el sistema rastrea los extremos (High/Low).
  - Utiliza una lógica de "empuje": si el precio supera el extremo actual durante la sesión, el nivel se actualiza y el VWAP de ese nivel se reinicia (re-ancla).
- **Mitigación:** 
  - Una vez formada, si el precio cruza (mitiga) la línea de un nivel en el futuro, se marca como `IsMitigated`.
  - **Ghost Lines:** Las líneas mitigadas continúan dibujándose visualmente hasta un tiempo de corte (cierre de USA) para referencia, pero cambian de estilo (punteado/gris).

### 2. Sistema de VWAP Dual
La estrategia maneja dos tipos de VWAP simultáneamente:
- **Global ETH VWAP (`ManageGlobalVWAPs`):**
  - Rastrea el High y Low absoluto de todo el día de trading (iniciando 18:00 NY).
  - Se utiliza como referencia macro y para targets (TP).
  - Si se rompe un High/Low global, se reinicia el cálculo del VWAP desde ese nuevo punto.
- **Ad-Hoc / Setup VWAP (`ManageEntryA_Plus`):**
  - Se utiliza específicamente para la ejecución de entradas.
  - Se activa solo cuando hay un "Trigger" (toque de un nivel de sesión).
  - Se ancla dinámicamente al High/Low de la vela que disparó la señal o a los nuevos extremos que se formen durante la validación ("Wick Growth").

### 3. Lógica de Entrada (State Machine)
El sistema utiliza una máquina de estados para gestionar el ciclo de vida de un trade (`ManageEntryA_Plus`):
1.  **Idle (Inactivo):** Escanea niveles activos en busca de toques (mitigaciones).
2.  **WaitingForConfirmation (Esperando Confirmación):**
    - Se activa al tocar un nivel.
    - Dibuja flechas visuales (Cyan/Lime).
    - Espera a que el precio cierre por debajo (Short) o por encima (Long) del Ad-Hoc VWAP para confirmar.
    - Verifica Ratios de Riesgo/Beneficio (R/R) antes de pasar al siguiente estado.
3.  **WorkingOrder (Orden Pendiente):**
    - Coloca una orden Límite al precio del VWAP.
    - **Persecución Dinámica:** Si el VWAP se mueve, la orden se actualiza (`ChangeOrder`) para seguir al precio justo, siempre que la R/R siga siendo favorable.
4.  **PositionActive (Posición Activa):** 
    - Gestiona la salida una vez que la orden se llena.

### 4. Gestión de Riesgo y Salidas (`EnsureProtection`)
- **Smart Split (División Inteligente):** Divide la posición en dos partes para TP1 y TP2.
- **Targets Dinámicos:**
  - TP1: El objetivo más cercano entre el VWAP Global opuesto y el Nivel de Sesión opuesto.
  - TP2: El objetivo más lejano.
- **Stop Loss:** Fijo en ticks (`StopLossTicks`) anclado al extremo de la estructura (Setup Anchor).
- **Breakeven:** Mueve el SL al precio de entrada automáticamente cuando se llena el TP1.

### 5. Mecanismos de Seguridad y Persistencia
- **Persistencia XML (`LoadLevels`/`SaveLevels`):** 
  - Guarda los niveles detectados en el disco duro. Esto permite recargar la estrategia o reiniciar NinjaTrader sin perder las líneas de sesión históricas.
  - Incluye lógica para detectar "Gaps" si la data del archivo es más antigua que la data del gráfico.
- **Safety Nets (Redes de Seguridad):**
  - **Zombie Positions:** Detecta si hay una posición abierta en el Broker pero la estrategia cree que está plana, y la "adopta" o la cierra si es insegura.
  - **Orphan Positions:** Detecta posiciones huérfanas y aplana la cuenta si el riesgo es alto.
  - **Hard Stop:** Un stop de emergencia en el código que cierra la posición si el precio viola el Anchor significativamente, por si el Stop Loss del broker falla.

### 6. Observaciones Técnicas
- **Uso de Memoria:** La estrategia carga datos históricos y dibuja muchas líneas, pero tiene optimizaciones como `ShowVisuals` para reducir carga.
- **Timezones:** Depende explícitamente de "Eastern Standard Time" para la lógica de sesiones.
- **Debug:** Tiene un sistema de logs detallado (`EnableDebugLogs`) y capturas de pantalla automáticas (`TriggerScreenshot`) al operar.

## Conclusión
Es una estrategia sofisticada que combina análisis de estructura de mercado (Sesiones) con análisis de flujo de órdenes (VWAP). Está diseñada para operar de manera autónoma con un alto grado de protección contra fallos técnicos y desconexiones.
