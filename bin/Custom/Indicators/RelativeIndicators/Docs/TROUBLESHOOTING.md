# Diagnóstico: Franjas de Noticias No Visibles

## Checklist de Verificación

### 1. Configuración del Indicador
- [ ] `ShowLines` está en `true`
- [ ] `ShowHistoricalNews` configurado correctamente
- [ ] `FilterImpact` no demasiado restrictivo
- [ ] `CustomCurrencies` compatible con instrumento

### 2. Descarga de Datos
- [ ] Verificar Output Window para mensajes de descarga
- [ ] Cache de noticias existe en `Documents/NinjaTrader 8/NewsCache/`
- [ ] Archivos XML no están vacíos

### 3. Coincidencia de Moneda
- [ ] Instrumento detectado correctamente
- [ ] Moneda coincide con eventos descargados

### 4. Visualización
- [ ] `LineColor` es visible (no transparente)
- [ ] Eventos dentro del rango de tiempo del gráfico
- [ ] Escala del gráfico permite ver las zonas

## Soluciones Rápidas

### Cambiar a FilterImpact = "Low"
Temporalmente para ver TODOS los eventos

### Verificar ShowLines = true
En propiedades del indicador

### Revisar Output Window
Buscar mensajes como:
- "RelativeNewsFilter: Downloaded & Cached Today's Data"
- "RelativeNewsFilter: Loaded X unique events"

### Habilitar ShowHistoricalNews = true
Para ver eventos pasados si estás en datos históricos
