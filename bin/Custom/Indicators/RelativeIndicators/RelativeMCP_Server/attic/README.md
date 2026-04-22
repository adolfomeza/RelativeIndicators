# Attic — experimentos archivados

Código que funcionó pero cuyo modelo conceptual era incorrecto. Conservado como
referencia histórica — NO usar en producción.

## blind_snapshot.py + blind_briefing.py (2026-04-21)

**Propósito original**: generar 18 snapshots blindos (6 instrumentos × 3 fases:
context, pre_open, eod) reconstructivos del día 2026-04-21, para validar que el
stack completo (con TPO PitAuto) producía análisis más fieles que los snapshots
originales de la mañana.

**Por qué se archivó**: el modelo "3 fases por día por instrumento" es
sobre-ingeniería. NADRO real es:
- **1 snapshot** por instrumento por día **al pit open**
- **1 review** por instrumento al pit close (narrativo, NO snapshot nuevo)

Los 18 snapshots generados el 2026-04-21 fueron eliminados de los JSON del día
(via script one-shot). Los archivos en este attic quedan por si en el futuro se
quiere hacer otro backtest reconstructivo con cutoffs temporales.

**Funcionalidad preservada en producción**:
- Computación offline de TPO pit-based → `tools/tpo_cva.py` (no se movió)
- Walk-forward con schema v2 → `tools/walkforward.py` (activo)
- Briefing HTML diario → `tools/briefing.py` (activo, produce el HTML del día)
