# Indicadores RelativeIndicators — Índice maestro

Tabla de los indicadores activos y su documentación. Las versiones se mantienen en `../{Indicator}_Docs/` y en `MEMORY.md` (memoria de Claude).

| Indicador | Versión | Propósito | Carpeta de docs |
|-----------|---------|-----------|-----------------|
| **RelativeVwap** | v3.0.2 | VWAP por sesión + Signal 1 (liquidez) y Signal 2 (separación). 9 partial classes. CSV trades. | [`../RelativeVwap_Docs/`](../RelativeVwap_Docs/) |
| **RelativeMonthlyVwap** | v1.1.1 | Fork de `amaCurrentMonthVWAP`. Zonas `mDVAH/mDVAL` + `pMDVAH/pMDVAL`. SharpDX OnRender. | [`../RelativeMonthlyVwap_Docs/`](../RelativeMonthlyVwap_Docs/) |
| **RelativeWeeklyVwap** | v1.0.1 | Fork de `amaCurrentWeekVWAP`. Zonas `wDVAH/wDVAL` + `pWDVAH/pWDVAL`. | [`../RelativeWeeklyVwap_Docs/`](../RelativeWeeklyVwap_Docs/) |
| **RelativeNMonthlyVwap** | v1.0.1 | Fork de `amaNMonthlyVWAP`. Soporta Monthly/Bimonthly/**Quarterly** (default)/Semiannual/Annual. | [`../RelativeNMonthlyVwap_Docs/`](../RelativeNMonthlyVwap_Docs/) |
| **RelativeVolumeProfile** | v2.0 | TPO con auto-merge NADRO + D-shape gate. 4 partial classes. PitAuto v1.1.0. | [`../RelativeDVAPVA_Docs/`](../RelativeDVAPVA_Docs/) |
| **RelativeVwapLevels** | v2.3.10 | Lector liviano de `VwapLevels/*_{INSTRUMENT}.txt`. Confluencias persistentes. Anti-colisión + dedup. | (sin carpeta dedicada) |
| **RelativeNewsFilter** | — | Calendario económico ForexFactory, alertas, visualización 50+ instrumentos. | [`../RelativeNewsFilter_Docs/`](../RelativeNewsFilter_Docs/) |
| **RelativeDelta** | v1.17 | Delta líneas. **DeltaHistory eliminada** en v1.17 (bug de disco). | [`../RelativeDelta_Docs/`](../RelativeDelta_Docs/) |
| **RelativeDVAPVA** | — | Delta Volume + Profile/PVA (auxiliar). | [`../RelativeDVAPVA_Docs/`](../RelativeDVAPVA_Docs/) |
| **RelativeSBS** | — | Smart Break System (estructura de mercado). | [`../RelativeSBS_Docs/`](../RelativeSBS_Docs/) |
| **RelativeVwapsHiLo** | — | VWAPs Hi/Lo. | [`../RelativeVwapsHiLo_Docs/`](../RelativeVwapsHiLo_Docs/) |

## Convención de etiquetas (resumen)

| Timeframe | DVA actual | Zona previa | Sufijo edad |
|-----------|------------|-------------|-------------|
| Daily     | `DVAH`/`DVAL` (sin prefijo) | `pDVAH`/`pDVAL` (UNA D) | `-3D` |
| Weekly    | `wDVAH`/`wDVAL` | `pWDVAH`/`pWDVAL` | `-2W` |
| Monthly   | `mDVAH`/`mDVAL` | `pMDVAH`/`pMDVAL` | `-2M` |
| Quarterly | `qDVAH`/`qDVAL` | `pQDVAH`/`pQDVAL` | `-1Q` |
| Annual    | `yDVAH`/`yDVAL` | `pYDVAH`/`pYDVAL` | `-1Y` |

Detalle completo en `~/.claude/projects/.../memory/vwap_forks_index.md`.

## Indicadores en desarrollo

- **VWAPDelta** — pendiente. Spec: [`_blueprints/VWAPDelta_Blueprint.md`](_blueprints/VWAPDelta_Blueprint.md). 3 partial classes proyectadas, sesión 23h, MFE/MAE hasta EOD.
