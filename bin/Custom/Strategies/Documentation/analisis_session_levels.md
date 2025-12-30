# SessionLevelsStrategy - Guía Completa

**Versión Actual**: v1.11.25 (29 Diciembre 2025)  
**Última Actualización**: 29 Diciembre 2025

---

## ¿Qué es SessionLevelsStrategy?

**SessionLevelsStrategy** es un sistema de trading automatizado profesional que opera futuros micro en la plataforma NinjaTrader 8. La estrategia identifica niveles de soporte y resistencia creados por las principales sesiones del mercado global y ejecuta operaciones de alta probabilidad cuando el precio retorna a estos niveles.

### En Términos Simples
Imagina que el mercado deja "huellas" durante cada sesión de trading (Asia, Europa, USA). Estas huellas son los puntos más altos y más bajos del día. Cuando el precio regresa a tocar estas huellas, frecuentemente rebota. Esta estrategia captura esos rebotes de forma automática.

---

## ¿Cómo Funciona?

### Paso 1: Detección de Niveles de Sesión

El mercado opera 24 horas pero se divide en tres sesiones principales:

| Sesión | Horario (NY) | Característica |
|--------|--------------|----------------|
| **Asia** | 20:00 - 01:00 | Menor volumen, establece rango inicial |
| **Europa** | 03:00 - 09:30 | Expansión de rango, alta actividad |
| **USA** | 09:30 - 16:00 | Mayor liquidez, define dirección del día |

Durante cada sesión, la estrategia registra:
- **High de Sesión**: El punto más alto alcanzado
- **Low de Sesión**: El punto más bajo alcanzado

Estos extremos se convierten en **niveles de referencia** para operaciones futuras.

### Paso 2: Concepto de "Nivel Virgen"

Un nivel es **virgen** si el precio lo creó pero nunca regresó a tocarlo. Los niveles vírgenes tienen mayor probabilidad de causar un rebote cuando son tocados por primera vez.

```
Ejemplo:
- Europa crea un High en $5,000
- El precio baja durante USA sin regresar a $5,000
- Este High de $5,000 es un "nivel virgen"
- Cuando el precio regrese a $5,000, esperamos un rebote
```

### Paso 3: Señal de Entrada (El "Trigger")

Cuando el precio **toca un nivel virgen**, la estrategia activa el proceso de entrada:

1. **Toque del Nivel**: El precio alcanza un High o Low de sesión anterior
2. **Confirmación**: El precio debe "despegarse" del nivel (separación mínima)
3. **Validación**: Se calcula si la operación ofrece suficiente ganancia potencial
4. **Ejecución**: Si todo es favorable, se coloca una orden límite

### Paso 4: VWAP - El Precio "Justo"

**VWAP (Volume Weighted Average Price)** es el precio promedio ponderado por volumen. Representa el precio "justo" del mercado en un momento dado.

La estrategia usa dos tipos de VWAP:

| Tipo | Propósito |
|------|-----------|
| **VWAP Global** | Referencia del día completo, usado para targets |
| **VWAP de Setup** | Se ancla en el momento del trigger, usado para entrada |

### Paso 5: Gestión de la Operación

Una vez dentro de la operación, la estrategia gestiona automáticamente:

```
ENTRADA: 10 contratos @ $5,000

SALIDAS:
├── TP1: 5 contratos → VWAP Global (objetivo dinámico)
├── TP2: 5 contratos → Nivel de Sesión Opuesto (objetivo fijo)
└── SL: 10 contratos → 1 tick del nivel (protección)

DESPUÉS DE TP1:
└── SL se mueve a "Breakeven" (entrada original)
    → Elimina riesgo en los 5 contratos restantes
```

---

## Características Principales

### 🎯 Position Sizing Dinámico
El sistema calcula automáticamente cuántos contratos operar basándose en:
- Tu riesgo máximo por operación en USD
- La distancia del stop loss
- La volatilidad actual del mercado (ATR)

**Ejemplo**:
```
Riesgo configurado: $100
Distancia al SL: 10 ticks
Valor por tick: $5
→ Contratos = $100 / (10 × $5) = 2 contratos
```

### 🔄 Multi-Entradas (Reintentos)
Si una operación se cierra por Stop Loss pero el nivel sigue siendo válido, la estrategia puede reintentar la entrada. Esto se basa en la premisa de que los niveles fuertes a menudo requieren múltiples intentos antes de producir el movimiento esperado.

### 🛡️ Filtro de Lag
La estrategia detecta si los datos del gráfico están llegando con retraso y **bloquea las operaciones** hasta que la conexión se normalice. Esto previene entradas con precios desactualizados.

### 🔌 Reinicio Inteligente
Si NinjaTrader se desconecta o reinicia:
- La estrategia **recupera** posiciones abiertas
- **Adopta** órdenes de protección existentes
- **Crea protección de emergencia** si faltan SL/TP
- Nunca deja posiciones sin protección

### ⚡ Carga Optimizada
Al cargar la estrategia, solo procesa la lógica de trading para los últimos 3 días, mientras mantiene todos los niveles históricos. Esto permite cargas 10x más rápidas sin perder información.

---

## Instrumentos Compatibles

La estrategia está diseñada para operar futuros micro, incluyendo:

| Instrumento | Mercado | Tick Size | Valor por Tick |
|-------------|---------|-----------|----------------|
| MES | S&P 500 | 0.25 | $1.25 |
| MNQ | Nasdaq 100 | 0.25 | $0.50 |
| MYM | Dow Jones | 1.00 | $0.50 |
| M2K | Russell 2000 | 0.10 | $0.50 |
| MGC | Oro | 0.10 | $1.00 |
| MCL | Petróleo | 0.01 | $1.00 |
| M6E | Euro | 0.00005 | $0.625 |
| 6E | Euro (full) | 0.00005 | $6.25 |

---

## Parámetros Configurables

### Gestión de Riesgo
| Parámetro | Descripción | Valor Predeterminado |
|-----------|-------------|---------------------|
| RiskPerTradeUSD | Riesgo máximo por operación | $75 |
| MinQuantity | Cantidad mínima de contratos | 2 |
| MaxQuantity | Cantidad máxima de contratos | 20 |
| StopLossTicks | Distancia del SL al nivel | 12 ticks |

### Filtros de Calidad
| Parámetro | Descripción | Valor Predeterminado |
|-----------|-------------|---------------------|
| MinRR | Ratio riesgo/recompensa mínimo | 1.0 |
| MaxRetriesPerLevel | Reintentos por nivel | 3 |
| LevelAgeDays | Antigüedad máxima de niveles | 7 días |

### Sesiones
| Parámetro | Descripción | Valor Predeterminado |
|-----------|-------------|---------------------|
| AsiaStart/End | Horario sesión Asia | 20:00 - 01:00 NY |
| EuStart/End | Horario sesión Europa | 03:00 - 09:30 NY |
| UsaStart/End | Horario sesión USA | 09:30 - 16:00 NY |

---

## Filosofía de Trading

### ¿Por Qué Funciona Esta Estrategia?

1. **Niveles Institucionales**: Los extremos de sesión representan precios donde grandes jugadores tomaron decisiones. Cuando el precio regresa, esos jugadores tienden a defender sus posiciones.

2. **VWAP como Referencia**: El VWAP representa el precio promedio del día. Operar cerca o al VWAP ofrece entradas de bajo riesgo.

3. **Gestión de Riesgo Estricta**: El sistema rechaza operaciones que no ofrecen al menos 1:1 de ganancia potencial versus riesgo.

4. **Breakeven Automático**: Una vez que TP1 se ejecuta, el riesgo de la operación se elimina completamente.

### Estadísticas Esperadas

| Métrica | Objetivo |
|---------|----------|
| Win Rate | 50-60% |
| Profit Factor | >1.5 |
| Operaciones Rechazadas | 30-40% (filtrado estricto) |
| Drawdown Máximo | <15% cuenta |

---

## Requisitos Técnicos

### Software
- NinjaTrader 8 (versión reciente)
- Conexión de datos en tiempo real
- **Tick Replay habilitado** (requerido para cálculo preciso de VWAP)

### Hardware Recomendado
- Procesador: Intel i5/AMD Ryzen 5 o superior
- RAM: 8GB mínimo, 16GB recomendado
- Conexión: Internet estable de baja latencia

### Cuenta de Trading
- Broker compatible con NinjaTrader
- Cuenta de futuros con permisos para micros
- Margen suficiente para los instrumentos a operar

---

## Indicadores Complementarios

La estrategia puede trabajar en conjunto con:

1. **RelativeVWAP**: VWAP visualizado en el gráfico con bandas de desviación
2. **RelativeDVAPVA**: DVA (Developing Value Area) con zonas de valor
3. **RelativeLevels**: Niveles de sesión visualizados independientemente

---

## Actualizaciones y Soporte

### Historial de Versiones Recientes

| Versión | Fecha | Mejora Principal |
|---------|-------|------------------|
| v1.11.25 | 29 Dic 2025 | Vela de confirmación en reintentos |
| v1.11.24 | 29 Dic 2025 | R/R persiste al cruzar sesiones |
| v1.11.22 | 29 Dic 2025 | Carga histórica 10x más rápida |
| v1.11.17 | 29 Dic 2025 | Filtro de lag para datos retrasados |
| v1.11.0 | 28 Dic 2025 | Reinicio inteligente de estrategia |

Para el historial completo, consultar `CHANGELOG_ES.md`.

---

## Preguntas Frecuentes

### ¿Puedo usar la estrategia en cuentas demo?
Sí. De hecho, se recomienda operar primero en demo para familiarizarse con el comportamiento.

### ¿Funciona durante las 24 horas?
La estrategia está activa 24/5 pero solo opera cuando detecta condiciones favorables. La mayoría de operaciones ocurren durante las sesiones de Europa y USA.

### ¿Qué pasa si pierdo conexión a internet?
La estrategia tiene mecanismos de reinicio inteligente. Al reconectarse, recupera posiciones y órdenes existentes. Además, las órdenes de protección (SL/TP) permanecen en el broker.

### ¿Puedo modificar los parámetros?
Sí. Todos los parámetros son configurables. Sin embargo, se recomienda no modificar valores sin comprender su impacto.

### ¿Cuánto capital necesito?
Depende del instrumento y número de contratos. Como referencia:
- MES (S&P 500 micro): ~$1,000 por contrato
- MNQ (Nasdaq micro): ~$1,500 por contrato

Se recomienda tener al menos 5x el margen requerido para manejar drawdowns.

---

## Contacto y Soporte

Para preguntas, soporte técnico o información sobre licencias, contactar a través de los canales oficiales.

---

*Documento generado automáticamente*  
*Versión de estrategia: v1.11.25*  
*Fecha: 29 Diciembre 2025*
