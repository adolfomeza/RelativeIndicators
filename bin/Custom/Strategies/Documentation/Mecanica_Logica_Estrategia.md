# 🧠 Mecánica y Lógica Interna: SessionLevelsStrategy
**Documento Técnico de Referencia**
**Versión de Estrategia**: v1.15.38
**Última Actualización**: 17 Enero 2026

Este documento detalla **exclusivamente** el "cómo" y el "por qué" de las decisiones algorítmicas de la estrategia. Elimina conceptos básicos para centrarse en la lógica de ejecución.

---

## 1. El Motor de Selección ("Deepest Level Logic")

Cuando el mercado se mueve con violencia, a menudo rompe varios niveles en una sola vela. La estrategia no entra ciegamente en el primero.

### El Problema
Supongamos una vela bajista fuerte que atraviesa:
1.  Asia Low ($5000)
2.  Europe Low ($4950)
3.  Yesterday Low ($4900)

Si entraras en el primero ($5000), tu posición estaría inmediatamente en pérdida ("cuchillo cayendo").

### La Solución Algorítmica
En cada tick, el `EntryStateMachine` escanea **todos** los niveles mitigados en la vela actual.

1.  **Recolección**: Identifica todos los niveles "virgenes" tocados.
2.  **Filtrado**: Descarta niveles bloqueados por filtros de IA o reintentos agotados.
3.  **Selección de "Punta de Lanza"**:
    *   **Caso Long**: Busca el nivel con el **PRECIO MÁS BAJO** de todos los candidatos.
    *   **Caso Short**: Busca el nivel con el **PRECIO MÁS ALTO**.

### Resultado Práctico
En el ejemplo anterior, la estrategia ignoraría los dos primeros niveles y armaría el setup exclusivamente en **Yesterday Low ($4900)**. Asegura el mejor precio posible.

---

## 2. El Protocolo de Confirmación

Tocar un nivel (`scan`) es solo el primer paso. El sistema requiere una confirmación de que la fuerza (momentum) se ha detenido.

### La Regla de "Despegue"
El sistema espera una vela cerrada que demuestre incapacidad de continuar la tendencia.

*   **Fórmula Long**: `Vela.Low > (SetupVWAP + 1 tick)`
*   **Fórmula Short**: `Vela.High < (SetupVWAP - 1 tick)`

### Flexibilidad Temporal
*   **Mito**: "Debe ser la vela siguiente inmediata".
*   **Realidad**: La confirmación puede ocurrir **en cualquier vela posterior** al toque.
    *   El precio puede quedarse "bailando" sobre el nivel por 5 o 10 velas.
    *   El sistema espera pacientemente.
    *   En el momento que una vela cierra "limpia" (despegada del VWAP), se activa el gatillo.

### Anclaje Dinámico (Re-Anchor)
Si mientras esperamos confirmación el precio hace un nuevo extremo (un Low más bajo en setup Long), el sistema:
1.  **Cancela** la espera actual.
2.  **Mueve el Ancla** al nuevo precio extremo.
3.  **Recalcula** el VWAP desde cero usando el nuevo punto de origen.
    *   *Objetivo*: No usar un VWAP "viejo" que ya no representa la realidad del nuevo movimiento.

---

## 3. Validación Matemática (Riesgo vs Recompensa)

Antes de enviar cualquier orden, se ejecuta una simulación matemática estricta. Si la matemática no cuadra, **no se opera**.

### Variables
*   **Riesgo ($)**: `abs(PrecioEntrada - StopLoss)`
    *   *Nota*: StopLoss se calcula como `Ancla +/- 1 tick` (Stop técnico ajustado).
*   **Recompensa ($)**: `abs(PrecioEntrada - VWAP_Global)`
    *   *Nota*: El target primario es siempre el VWAP Global (regresión a la media).

### La Fórmula
```csharp
Ratio = Recompensa / Riesgo;

if (Ratio < 1.0) {
    Log("ABORT: Risk/Reward < 1.0");
    return; // Setup cancelado
}
```
Esto filtra operaciones donde el Stop Loss necesario es demasiado amplio comparado con el recorrido probable hasta el equilibrio (VWAP).

---

## 4. Gestión de Posición (`OrderProtectionManager`)

Una vez la orden se llena, entra en juego el "Manager", un módulo autónomo encargado de proteger el capital y maximizar ganancias.

### La Protección: Stop Loss Adaptativo (Survival Mode)
El SL no es una orden estática "dispara y olvida".
1.  **Colocación Inicial**: `Ancla +/- 1 tick`.
2.  **Modo Supervivencia**: Si ocurre un slippage extremo y el mercado "salta" tu SL (el precio abre más allá de tu stop):
    *   El Manager detecta que la orden SL quedó huérfana.
    *   **Acción Inmediata**: Cancela el SL viejo y lanza una orden de emergencia a Mercado (o Limit agresivo) para cerrar la posición.
    *   *Prioridad*: "Sangrar lo menos posible".

### La Salida: Estrategia de Dos Tiers

#### Tier 1: "El Financiador" (50% Posición)
*   **Objetivo**: VWAP Global del Trade (`TradeVWAP`).
*   **Dinámica**: Es un objetivo móvil.
    *   Si el mercado se mueve a tu favor pero lentamente, el VWAP se acerca al precio. Tu TP1 se ajustará acercándose también.
    *   Asegura un Win Rate alto al tomar ganancias en el punto de equilibrio de volumen.

#### El Evento Breakeven
En el instante exacto (`OnExecutionUpdate`) que se llena el TP1:
*   El Manager modifica la orden de Stop Loss restante.
*   **Nuevo Precio SL**: `PrecioEntradaPromedio`.
*   **Resultado**: La segunda mitad de la posición es "gratis" (Risk-Free).

#### Tier 2: "El Corredor" (50% Restante)
*   **Objetivo**: Nivel Opuesto Más Extremo.
*   **Lógica de Búsqueda**:
    1.  Si compraste en **Asia Low**, el sistema busca **Asia High**.
    2.  Si existe un **USA High** del mismo día que está *más lejos*, selecciona ese.
    3.  Busca siempre el nivel que ofrezca la mayor expansión de rango.

---

## 5. Resiliencia y Costura de Datos (Smart Rollover)

### ¿Qué pasa si reinicio el PC?
El sistema posee memoria persistente de estado. Al arrancar:
1.  Escanea la cuenta en busca de posiciones abiertas de la estrategia.
2.  Si encuentra una posición pero no tiene órdenes de salida (SL/TP):
    *   Calcula dónde deberían estar.
    *   Las crea inmediatamente protegidas por el algoritmo de emergencia.

### Smart Rollover (Futuros)
Al operar contratos continuos o hacer backtesting de largo plazo:
*   El sistema detecta fechas de vencimiento (CME Rules: 2do Jueves del mes).
*   **Auto-Stitch**: Ignora los datos de volumen bajo del contrato viejo en días de transición y "cose" la curva de capital con el nuevo contrato.
*   Evita falsos negativos en backtesting por operar "contratos zombis" sin liquidez.

---

## 6. Integración de Inteligencia Artificial (AI Filters v1.15.43)

El sistema ahora posee la capacidad de "autocensura" basada en datos históricos procesados externamente.

### El Concepto
No todos los niveles son iguales. Un `Asia Low` puede ser muy rentable en Nasdaq pero tóxico en Oro. La estrategia recibe un "Informe de Inteligencia" (`ai_config.json`) al iniciar y ajusta su comportamiento agresivamente.

### Los Tres Filtros Activos
Si `Auto Load AI Config` está activo, cada setup potencial debe pasar tres pruebas adicionales antes de siquiera ser considerado por el `ScanForTriggers`:

1.  **Filtro de Zona (`IsZoneEnabled`)**:
    *   Verifica si el nombre del nivel (ej. "Asia High") está en la "Lista Blanca" aprobada por la IA.
    *   Si no está, el nivel es invisible para la estrategia.

2.  **Filtro de Caducidad (`MaxLevelAgeDays`)**:
    *   Niveles demasiado antiguos pierden relevancia estadística.
    *   Si `(HoraActual - HoraCreacionNivel) > MaxAge`, el nivel se ignora.

3.  **Filtro de Fatiga (`MaxRetries`)**:
    *   Limita cuántas veces se puede explotar un mismo nivel.
    *   Si `IntentosActuales >= MaxRetries`, se deja de operar ese nivel para evitar el "Over-trading" en zonas agotadas.

Este módulo asegura que la estrategia solo dispare en los escenarios de mayor probabilidad estadística, ignorando el ruido.

---

*Este documento define la verdad técnica del sistema. Cualquier desviación observada en los logs debe considerarse un bug respecto a esta especificación.*
