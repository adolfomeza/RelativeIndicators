// RelativeVolumeProfile — NADRO Auto-Merge
//
// Implementa la regla NADRO 05-2: cuando el VA del perfil consecutivo solapa
// >= 50% con el VA del perfil/composite anterior, fusionarlos automaticamente
// como CVA. Si NO hay overlap suficiente o hay breakout limpio, cerrar el bloque.
//
// Diseño:
// - Algoritmo FORWARD-ONLY: itera _allProfiles en orden cronologico
// - Reusa MergeMultipleProfiles existente para crear los composites
// - Marca cada composite auto-creado con CompositeInfo.IsNadroAuto = true
// - Antes de re-aplicar el algoritmo, deshace SOLO los composites NADRO-auto
//   (preserva merges manuales del usuario)
// - Se ejecuta:
//     a) On State.DataLoaded — primera vez que se construyen profiles historicos
//     b) Cada vez que cierra una sesion (transicion active→closed)
//     c) Cuando user cambia AutoMergeNadroEnabled u otros params del grupo 06

using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript.AddOns; // RelativeLog extension methods (RLog)

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class RelativeVolumeProfile
	{
		// Estado: cuantos profiles cerrados teniamos la ultima vez que ejecutamos auto-merge.
		// Si crece, hay sesion(es) nueva(s) cerrada(s) y hay que re-evaluar.
		private int _lastNadroBuildClosedCount = -1;

		/// <summary>% overlap del VA de A vs B sobre el rango VA MENOR (NADRO §11 doctrina pura).
		/// Returns 0.0–1.0.
		///
		/// FIX 2026-04-30: cambiado de Max a Min en el denominador. Con Max, un VA chico
		/// completamente envuelto por uno grande devolvía un overlap bajo (penalizaba dias
		/// pequeños de instrumentos concentrados como GC). Con Min, un VA chico totalmente
		/// dentro de uno grande da 100% — que es el comportamiento NADRO §11 correcto:
		/// "el menor área debe estar mayormente cubierta por el otro = misma zona de balance".
		///
		/// Si quieres el comportamiento conservador (NO fusionar dias pequeños envueltos),
		/// sube AutoMergeOverlapThreshold a 0.50 o más.</summary>
		private double NadroVaOverlap(double vahA, double valA, double vahB, double valB)
		{
			double lo = Math.Max(valA, valB);
			double hi = Math.Min(vahA, vahB);
			if (hi <= lo) return 0.0;
			double overlap = hi - lo;
			double minRange = Math.Min(vahA - valA, vahB - valB);
			return minRange > 0 ? overlap / minRange : 0.0;
		}

		/// <summary>Detecta breakout limpio: VA del nuevo profile esta totalmente arriba/abajo del VA prev.</summary>
		private bool NadroIsBreakoutUp(double prevVAH, double newVAL)
		{
			return newVAL > prevVAH + AutoMergeBreakoutTolerance;
		}

		private bool NadroIsBreakoutDown(double prevVAL, double newVAH)
		{
			return newVAH < prevVAL - AutoMergeBreakoutTolerance;
		}

		/// <summary>Detecta si un profile (típicamente merged tentativo) forma D-shape.
		///
		/// D-shape NADRO/Market Profile = perfil BALANCEADO/rotacional:
		///   - POC centrado dentro del VA (no skewed arriba ni abajo)
		///   - Rango total tiene tails arriba Y abajo del VA (no one-sided)
		///   - Tails relativamente simetricas (no triangular)
		///
		/// Si NO es D-shape, los dias forman P-shape (bullish exhaustion) o b-shape
		/// (bearish exhaustion) o trend day — son TRANSITION, no equilibrio sostenido.
		/// En ese caso NO se debe mergear como CVA aunque overlap >= threshold.</summary>
		private bool NadroIsDShape(VolumeProfileSession profile)
		{
			if (profile == null || profile.Levels == null || profile.Levels.Count == 0) return false;

			double poc = profile.POC;
			double vah = profile.VAH;
			double val = profile.VAL;

			// Range total (min/max de niveles con volumen)
			double rangeLow = double.MaxValue;
			double rangeHigh = double.MinValue;
			foreach (var kvp in profile.Levels)
			{
				if (kvp.Value.Volume > 0)
				{
					double price = kvp.Value.Price;
					if (price < rangeLow) rangeLow = price;
					if (price > rangeHigh) rangeHigh = price;
				}
			}
			double range = rangeHigh - rangeLow;
			double vaRange = vah - val;
			if (range <= 0 || vaRange <= 0) return false;

			// Thresholds configurables (auto-ajustados por familia si DShapeMode=Auto).
			double minTailFrac     = NadroDShapeMinTailPct / 100.0;
			double minSymmetryFrac = NadroDShapeMinSymmetryPct / 100.0;
			double pocMinFrac      = NadroDShapePocMinPct / 100.0;
			double pocMaxFrac      = NadroDShapePocMaxPct / 100.0;

			// Criterio 1: POC centrado dentro del VA (entre pocMin% y pocMax%)
			double pocInVa = (poc - val) / vaRange;
			if (pocInVa < pocMinFrac || pocInVa > pocMaxFrac)
			{
				if (NadroLogDShapeFailures)
					this.RLog("[NADRO D-shape FAIL] {0}: POC offset {1:0.0}% fuera de rango {2:0}-{3:0}%. VAH={4} POC={5} VAL={6}",
						profile.StartTime.ToString("yyyy-MM-dd"), pocInVa * 100, NadroDShapePocMinPct, NadroDShapePocMaxPct, vah, poc, val);
				return false;
			}

			// Criterio 2: tails arriba Y abajo del VA (>= NadroDShapeMinTailPct cada lado)
			double tailLow  = (val - rangeLow) / range;
			double tailHigh = (rangeHigh - vah) / range;
			if (tailLow < minTailFrac || tailHigh < minTailFrac)
			{
				if (NadroLogDShapeFailures)
					this.RLog("[NADRO D-shape FAIL] {0}: tails {1:0.0}%/{2:0.0}% < min {3:0.0}%. VA={4}-{5} range={6}-{7}",
						profile.StartTime.ToString("yyyy-MM-dd"), tailLow * 100, tailHigh * 100, NadroDShapeMinTailPct,
						val, vah, rangeLow, rangeHigh);
				return false;
			}

			// Criterio 3: tails simetricas (ratio min/max >= NadroDShapeMinSymmetryPct)
			double tailRatio = Math.Min(tailLow, tailHigh) / Math.Max(tailLow, tailHigh);
			if (tailRatio < minSymmetryFrac)
			{
				if (NadroLogDShapeFailures)
					this.RLog("[NADRO D-shape FAIL] {0}: simetria {1:0.0}% < min {2:0.0}%. tailLow={3:0.0}% tailHigh={4:0.0}%",
						profile.StartTime.ToString("yyyy-MM-dd"), tailRatio * 100, NadroDShapeMinSymmetryPct,
						tailLow * 100, tailHigh * 100);
				return false;
			}

			return true;
		}

		/// <summary>Deshace todos los composites NADRO-auto, dejando los originales en _allProfiles.
		/// PRESERVA composites creados manualmente por el usuario.</summary>
		private void NadroUndoAutoComposites()
		{
			if (_composites == null || _composites.Count == 0) return;

			var autoComposites = _composites.Where(c => c.IsNadroAuto).ToList();
			foreach (var comp in autoComposites)
			{
				// Find merged session in _allProfiles and replace with originals
				int mergedIdx = _allProfiles.IndexOf(comp.MergedSession);
				if (mergedIdx >= 0)
				{
					_allProfiles.RemoveAt(mergedIdx);
					// Insert originals back at same position, in their original order
					for (int i = 0; i < comp.OriginalProfiles.Count; i++)
					{
						_allProfiles.Insert(mergedIdx + i, comp.OriginalProfiles[i]);
					}
				}
				_composites.Remove(comp);
			}

			if (autoComposites.Count > 0)
				_compositesStamp++;
		}

		/// <summary>Algoritmo NADRO forward-only sobre profiles cerrados.
		/// Itera en orden cronologico, decide merge / breakout / drift por VA overlap.</summary>
		private void ApplyNadroAutoMerge()
		{
			if (!AutoMergeNadroEnabled) return;
			if (_allProfiles == null || _allProfiles.Count < 2) return;

			this.RLog("[NADRO] " + string.Format(" ApplyNadroAutoMerge START: _allProfiles.Count={0} threshold={1} tolerance={2}",
				_allProfiles.Count, AutoMergeOverlapThreshold, AutoMergeBreakoutTolerance));

			// PASO 1: deshacer auto-merges anteriores (preservar manuales)
			NadroUndoAutoComposites();

			// PASO 2: lista de profiles CERRADOS en orden cronologico, ya sin auto-composites.
			//
			// FIX 2026-05-02 ANTI-NESTING: excluir los MergedSession de composites MANUALES
			// (que NadroUndoAutoComposites preserva). Si los incluyéramos, ApplyNadroAutoMerge
			// los trataría como pseudo-profiles individuales y al fusionarlos generaría composites
			// con OriginalProfiles que son MergedSessions nesteados (con StartTime/EndTime
			// abarcando varios días). Esto rompe el QueryAt point-in-time y review post-hoc.
			var manualMergedSet = new HashSet<VolumeProfileSession>();
			if (_composites != null)
			{
				foreach (var c in _composites)
				{
					if (c != null && !c.IsNadroAuto && c.MergedSession != null)
						manualMergedSet.Add(c.MergedSession);
				}
			}

			var closedSorted = _allProfiles
				.Where(p => !p.IsActive && p.POCVolume > 0 && p.Levels != null && p.Levels.Count > 0)
				.Where(p => !manualMergedSet.Contains(p))
				.OrderBy(p => p.StartTime)
				.ToList();

			this.RLog("[NADRO] " + string.Format(" closedSorted.Count={0}", closedSorted.Count));
			if (closedSorted.Count < 2) return;

			// PASO 3: forward iteration. Build blocks de profiles consecutivos con overlap suficiente.
			int i = 0;
			while (i < closedSorted.Count)
			{
				var blockMembers = new List<VolumeProfileSession> { closedSorted[i] };
				double blockVAH = closedSorted[i].VAH;
				double blockVAL = closedSorted[i].VAL;

				int j = i + 1;
				while (j < closedSorted.Count)
				{
					var next = closedSorted[j];

					// 3a. Skip si el profile manual ya esta en otro composite (proteccion)
					bool isInManualComposite = _composites?.Any(c =>
						!c.IsNadroAuto && c.OriginalProfiles.Contains(next)) ?? false;
					if (isInManualComposite)
					{
						this.RLog("[NADRO] " + string.Format("   {0:yyyy-MM-dd} skip (in manual composite)", next.StartTime));
						break;
					}

					// 3b. Breakout limpio?
					if (NadroIsBreakoutUp(blockVAH, next.VAL))
					{
						this.RLog("[NADRO] " + string.Format("   {0:yyyy-MM-dd} BREAKOUT_UP block[VAH={1:F2} VAL={2:F2}] vs next[VAH={3:F2} VAL={4:F2}]",
							next.StartTime, blockVAH, blockVAL, next.VAH, next.VAL));
						break;
					}
					if (NadroIsBreakoutDown(blockVAL, next.VAH))
					{
						this.RLog("[NADRO] " + string.Format("   {0:yyyy-MM-dd} BREAKOUT_DOWN block[VAH={1:F2} VAL={2:F2}] vs next[VAH={3:F2} VAL={4:F2}]",
							next.StartTime, blockVAH, blockVAL, next.VAH, next.VAL));
						break;
					}

					// 3c. Overlap suficiente?
					double overlap = NadroVaOverlap(blockVAH, blockVAL, next.VAH, next.VAL);
					this.RLog("[NADRO] " + string.Format("   {0:yyyy-MM-dd} block[VAH={1:F2} VAL={2:F2}] vs next[VAH={3:F2} VAL={4:F2}] overlap={5:P1}",
						next.StartTime, blockVAH, blockVAL, next.VAH, next.VAL, overlap));
					if (overlap < AutoMergeOverlapThreshold)
					{
						this.RLog("[NADRO] " + string.Format("   {0:yyyy-MM-dd} DRIFT (overlap {1:P1} < threshold {2:P1})",
							next.StartTime, overlap, AutoMergeOverlapThreshold));
						break;
					}

					// 3c.bis: GATE D-shape — el merge tentativo debe formar perfil balanceado
					if (NadroRequireDShape)
					{
						var tentativeMembers = new List<VolumeProfileSession>(blockMembers);
						tentativeMembers.Add(next);
						VolumeProfileSession tentMerged = MergeMultipleProfiles(tentativeMembers);
						bool isD = tentMerged != null && NadroIsDShape(tentMerged);
						this.RLog("[NADRO] " + string.Format("   {0:yyyy-MM-dd} D-shape check: {1}",
							next.StartTime, isD ? "PASS" : "FAIL"));
						if (!isD)
						{
							this.RLog("[NADRO] " + string.Format("   {0:yyyy-MM-dd} REJECT MERGE (overlap OK pero NO D-shape — transition, no CVA)",
								next.StartTime));
							break;
						}
					}

					// 3d. Merge: extender VA del bloque (union de bordes para tracking)
					this.RLog("[NADRO] " + string.Format("   {0:yyyy-MM-dd} MERGE", next.StartTime));
					blockMembers.Add(next);
					blockVAH = Math.Max(blockVAH, next.VAH);
					blockVAL = Math.Min(blockVAL, next.VAL);
					j++;
				}

				// 3e. Si bloque tiene >=2 miembros, crear composite NADRO-auto
				if (blockMembers.Count >= 2)
				{
					this.RLog("[NADRO] " + string.Format(" CREATE COMPOSITE with {0} members starting {1:yyyy-MM-dd}",
						blockMembers.Count, blockMembers[0].StartTime));
					NadroCreateComposite(blockMembers);
				}

				i = j; // saltar profiles ya procesados
			}

			// ====================================================================
			// PASO 4 (FIX 2026-04-30): 2da pasada composite-a-composite.
			// Si dos composites consecutivos pasan overlap+breakout, los fusiona
			// SIN recomputar D-shape (cada uno ya pasó D-shape individualmente
			// en la pasada 1). Doctrina NADRO §11 aplicada recursivamente: dos
			// zonas de balance que comparten >threshold del VA son la misma zona.
			//
			// Fix targeted al bug donde RTY 17-27 abril genera 3 composites
			// (17-21, 22-23, 24-27) con overlap 95% y 82% entre ellos pero NO se
			// fusionan porque el merge tentativo acumulativo dia-a-dia rompe
			// D-shape al alcanzar cierto tamaño.
			// ====================================================================
			NadroMergeAdjacentComposites();

			this.RLog("[NADRO] " + string.Format(" DONE. Total composites in _composites: {0}",
				_composites?.Count ?? 0));
			_compositesStamp++;

			// FIX 2026-05-02: Re-publicar state al Registry tras generar composites.
			// El auto-merge corre DESPUÉS del primer OnRender (espera _compositesRestored).
			// Si el publish inicial en OnStateChange→Realtime ocurrió antes que el render,
			// publicó con composites=[]. Esta re-publicación garantiza state actualizado.
			//
			// IMPORTANTE: solo publicar si State == Realtime. Si Apply corre durante histórico
			// (después del primer OnRender pero antes de transicionar a Realtime), Time[0] sería
			// un bar histórico viejo y sobrescribiría el state correcto. En ese caso, dejar que
			// el publish final en OnStateChange→Realtime se encargue (con composites ya listos).
			if (State == State.Realtime)
			{
				try { PublishStateToRegistry(); }
				catch (Exception ex) { Print("RelativeVolumeProfile: Publish post-auto-merge ERROR: " + ex.Message); }
			}
		}

		/// <summary>2da pasada: fusiona composites NADRO-auto adyacentes que
		/// comparten overlap >= threshold y no tienen breakout. Recursiva (loop
		/// hasta que no haya más fusiones posibles).</summary>
		private void NadroMergeAdjacentComposites()
		{
			if (_composites == null || _composites.Count < 2) return;

			bool merged = true;
			int safetyIter = 0;
			while (merged && safetyIter < 20)
			{
				merged = false;
				safetyIter++;

				// Solo composites NADRO-auto, ordenados por fecha del primer miembro
				var autoComps = _composites
					.Where(c => c.IsNadroAuto && c.MergedSession != null)
					.OrderBy(c => c.MergedSession.StartTime)
					.ToList();

				for (int k = 0; k < autoComps.Count - 1; k++)
				{
					var a = autoComps[k];
					var b = autoComps[k + 1];
					if (a.MergedSession == null || b.MergedSession == null) continue;

					double aVAH = a.MergedSession.VAH;
					double aVAL = a.MergedSession.VAL;
					double bVAH = b.MergedSession.VAH;
					double bVAL = b.MergedSession.VAL;

					// Breakout?
					if (NadroIsBreakoutUp(aVAH, bVAL))
					{
						this.RLog("[NADRO-P2] {0:yyyy-MM-dd}+{1:yyyy-MM-dd} BREAKOUT_UP",
							a.MergedSession.StartTime, b.MergedSession.StartTime);
						continue;
					}
					if (NadroIsBreakoutDown(aVAL, bVAH))
					{
						this.RLog("[NADRO-P2] {0:yyyy-MM-dd}+{1:yyyy-MM-dd} BREAKOUT_DOWN",
							a.MergedSession.StartTime, b.MergedSession.StartTime);
						continue;
					}

					// Overlap suficiente?
					double ovr = NadroVaOverlap(aVAH, aVAL, bVAH, bVAL);
					this.RLog("[NADRO-P2] {0:yyyy-MM-dd}+{1:yyyy-MM-dd} overlap={2:P1}",
						a.MergedSession.StartTime, b.MergedSession.StartTime, ovr);
					if (ovr < AutoMergeOverlapThreshold) continue;

					// Fuse: combina OriginalProfiles de ambos y rebuild composite
					var combined = new List<VolumeProfileSession>(a.OriginalProfiles);
					combined.AddRange(b.OriginalProfiles);
					combined = combined.OrderBy(p => p.StartTime).ToList();

					this.RLog("[NADRO-P2] FUSE {0:yyyy-MM-dd}+{1:yyyy-MM-dd} ({2}+{3} = {4} dias)",
						a.MergedSession.StartTime, b.MergedSession.StartTime,
						a.OriginalProfiles.Count, b.OriginalProfiles.Count, combined.Count);

					// Crear el merged session combinado a partir de los originales
					VolumeProfileSession newMerged = MergeMultipleProfiles(combined);
					if (newMerged == null)
					{
						this.RLog("[NADRO-P2] FUSE ABORT: MergeMultipleProfiles returned null");
						continue;
					}

					// Deshace ambos composites del _allProfiles, reemplazando por el nuevo
					// en la posicion de A (el primero cronologicamente)
					int aIdx = _allProfiles.IndexOf(a.MergedSession);
					int bIdx = _allProfiles.IndexOf(b.MergedSession);

					if (aIdx >= 0)
					{
						_allProfiles[aIdx] = newMerged; // reemplazar in-place
					}
					else
					{
						// fallback: insertar al final si A no estaba (no deberia pasar)
						_allProfiles.Add(newMerged);
					}

					// Remover B si estaba (recalcular bIdx por si aIdx desplazo nada — no pasa con replace)
					bIdx = _allProfiles.IndexOf(b.MergedSession);
					if (bIdx >= 0) _allProfiles.RemoveAt(bIdx);

					_composites.Remove(a);
					_composites.Remove(b);

					// Tracking del nuevo composite combinado
					if (_composites == null)
						_composites = new List<CompositeInfo>();
					_composites.Add(new CompositeInfo
					{
						OriginalProfiles = new List<VolumeProfileSession>(combined),
						MergedSession = newMerged,
						IsNadroAuto = true,
					});

					merged = true;
					break; // restart loop con la lista actualizada
				}
			}
		}

		/// <summary>Crea un composite a partir de una lista de profiles consecutivos.
		/// Reusa MergeMultipleProfiles existente. Marca como IsNadroAuto = true.</summary>
		private void NadroCreateComposite(List<VolumeProfileSession> members)
		{
			if (members == null || members.Count < 2) return;

			VolumeProfileSession merged = MergeMultipleProfiles(members);
			if (merged == null) return;

			// Replace en _allProfiles: mantener posicion del primer miembro, remover los demas
			int firstIdx = _allProfiles.IndexOf(members[0]);
			if (firstIdx < 0) return;

			// Remover todos los miembros (en orden inverso para no desfasar indices)
			for (int k = members.Count - 1; k >= 0; k--)
			{
				_allProfiles.Remove(members[k]);
			}
			// Insertar el merged en la posicion del primero
			_allProfiles.Insert(firstIdx, merged);

			// Tracking
			if (_composites == null)
				_composites = new List<CompositeInfo>();
			_composites.Add(new CompositeInfo
			{
				OriginalProfiles = new List<VolumeProfileSession>(members),
				MergedSession = merged,
				IsNadroAuto = true,
			});
		}

		// ============================================================================
		// Payload helpers — exponen composites/pVAs al RelativeIndicatorRegistry
		// para que el watcher Python use exactamente las mismas zonas que pinta el chart.
		// ============================================================================

		/// <summary>Publica el estado completo al RelativeIndicatorRegistry. Llamado tanto
		/// desde la serie primaria (BarsInProgress==0) como desde la secundaria 1-min
		/// (BarsInProgress==1) para granularidad <= 1min independiente del TF del chart.</summary>
		private void PublishStateToRegistry()
		{
			try
			{
				string indName = typeof(RelativeVolumeProfile).Name;
				double poc = _activeProfile != null ? _activeProfile.POC : double.NaN;
				double vah = _activeProfile != null ? _activeProfile.VAH : double.NaN;
				double val = _activeProfile != null ? _activeProfile.VAL : double.NaN;
				long pocVol = _activeProfile != null ? _activeProfile.POCVolume : 0L;
				int levelCount = (_activeProfile != null && _activeProfile.Levels != null) ? _activeProfile.Levels.Count : 0;
				bool profActive = _activeProfile != null && _activeProfile.IsActive;
				int allProfCount = _allProfiles != null ? _allProfiles.Count : 0;

				var compositesList = BuildCompositesPayload();
				var closedPvasList = BuildClosedPvasPayload();
				var activePva = BuildActivePvaPayload();

				// FIX 2026-05-02: Usar SIEMPRE el último bar disponible (Bars.Count - 1) en lugar
				// de Time[0]/Close[0]/CurrentBar. Esos campos referencian al bar EN PROCESO de
				// OnBarUpdate, lo cual puede dar valores stale cuando PublishStateToRegistry se
				// llama desde OnRender (UI thread) o desde Apply en contextos no esperados.
				// Bars.GetTime(Bars.Count-1) siempre es el bar más reciente cargado en memoria.
				int lastBarIdx = (Bars != null) ? Bars.Count - 1 : -1;
				DateTime lastBarTime = (lastBarIdx >= 0) ? Bars.GetTime(lastBarIdx) : DateTime.MinValue;
				double lastBarClose = (lastBarIdx >= 0) ? Bars.GetClose(lastBarIdx) : double.NaN;

				NinjaTrader.NinjaScript.AddOns.RelativeIndicatorRegistry.Publish(
					string.Format("{0}:{1}:{2}{3}", indName, Instrument.FullName,
						BarsPeriod.Value, BarsPeriod.BarsPeriodType),
					new Dictionary<string, object>
					{
						["bar"] = lastBarIdx,
						["bar_time"] = lastBarTime,
						["close"] = lastBarClose,
						["poc"] = poc,
						["vah"] = vah,
						["val"] = val,
						["poc_volume"] = pocVol,
						["level_count"] = levelCount,
						["profile_active"] = profActive,
						["total_profiles"] = allProfCount,
						["profile_type"] = ProfileType.ToString(),
						["session_mode"] = SessionMode.ToString(),
						["composites"] = compositesList,
						["closed_pvas"] = closedPvasList,
						["active_pva"] = activePva,
						["auto_merge_enabled"] = AutoMergeNadroEnabled,
						["auto_merge_overlap_threshold"] = AutoMergeOverlapThreshold,
						["auto_merge_breakout_tolerance"] = AutoMergeBreakoutTolerance,
						["auto_merge_require_dshape"] = NadroRequireDShape,
					});
			}
			catch { }
		}

		/// <summary>Evalúa el estado de un nivel (VAH/POC/VAL) replicando la lógica del rendering:
		///   - "virgin"  → no hubo aceptación dura (VAH/VAL) o touch (POC). Línea solid hasta el bar actual.
		///   - "touched" → aceptación detectada y bar actual está dentro de la zona ghost (≤ session end).
		///   - "expired" → aceptación detectada y session end ya pasó. La línea NO se dibuja más → NO operable.
		///
		/// Devuelve dict con status + touch_time (ISO o null) + expire_time (ISO o null).
		/// VAH/VAL usan FindFirstAcceptanceBar (NADRO §5.3 close ≥50% del ritmo).
		/// POC usa FindFirstTouchBar (high/low cruza el nivel). NADRO no opera POC directo (Ley 9).</summary>
		private Dictionary<string, object> EvaluateLevelStatus(VolumeProfileSession profile, double priceLevel, string kind)
		{
			var defaults = new Dictionary<string, object>
			{
				["status"] = "unknown",
				["touch_time"] = null,
				["expire_time"] = null,
			};
			try
			{
				var bars = ChartBars != null ? ChartBars.Bars : Bars;
				if (bars == null || bars.Count == 0 || profile == null) return defaults;

				int currentBarIdx = bars.Count - 1;
				int searchStart = GetSafeTouchSearchStartBar(profile);

				int touchBar;
				if (kind == "VAH")
					touchBar = FindFirstAcceptanceBar(priceLevel, searchStart, currentBarIdx, isUpper: true);
				else if (kind == "VAL")
					touchBar = FindFirstAcceptanceBar(priceLevel, searchStart, currentBarIdx, isUpper: false);
				else // POC u otro
					touchBar = FindFirstTouchBar(priceLevel, searchStart, currentBarIdx);

				if (touchBar < 0)
				{
					return new Dictionary<string, object>
					{
						["status"] = "virgin",
						["touch_time"] = null,
						["expire_time"] = null,
					};
				}

				int sessionEndBar = FindSessionEndBar(touchBar);
				DateTime touchTime = bars.GetTime(Math.Min(touchBar, bars.Count - 1));
				DateTime expireTime = bars.GetTime(Math.Min(sessionEndBar, bars.Count - 1));

				string status = currentBarIdx <= sessionEndBar ? "touched" : "expired";

				return new Dictionary<string, object>
				{
					["status"] = status,
					["touch_time"] = touchTime.ToString("yyyy-MM-ddTHH:mm:ss"),
					["expire_time"] = expireTime.ToString("yyyy-MM-ddTHH:mm:ss"),
				};
			}
			catch
			{
				return defaults;
			}
		}

		/// <summary>Serializa cada CompositeInfo (CVA NADRO) como dict consumible por replay.py.
		/// start_date / end_date son ISO YYYY-MM-DD del primer y último OriginalProfiles.</summary>
		private List<Dictionary<string, object>> BuildCompositesPayload()
		{
			var result = new List<Dictionary<string, object>>();
			if (_composites == null || _composites.Count == 0) return result;

			foreach (var comp in _composites)
			{
				if (comp == null || comp.MergedSession == null
					|| comp.OriginalProfiles == null || comp.OriginalProfiles.Count == 0) continue;

				var first = comp.OriginalProfiles[0];
				var last = comp.OriginalProfiles[comp.OriginalProfiles.Count - 1];
				var ms = comp.MergedSession;

				result.Add(new Dictionary<string, object>
				{
					["start_date"] = first.StartTime.ToString("yyyy-MM-dd"),
					["end_date"] = last.StartTime.ToString("yyyy-MM-dd"),
					["start_time"] = first.StartTime.ToString("yyyy-MM-ddTHH:mm:ss"),
					["end_time"] = last.EndTime.ToString("yyyy-MM-ddTHH:mm:ss"),
					["vah"] = ms.VAH,
					["val"] = ms.VAL,
					["poc"] = ms.POC,
					["status"] = ms.IsActive ? "active" : "closed",
					["is_nadro_auto"] = comp.IsNadroAuto,
					["n_members"] = comp.OriginalProfiles.Count,
					// v2.x: estado virgen/touched/expired por nivel (replica lógica del rendering).
					// Solo niveles "virgin" son operables NADRO. "touched" están en zona ghost dashed
					// (visibles pero ya no entry de BPB). "expired" no se dibujan más.
					["vah_status"] = EvaluateLevelStatus(ms, ms.VAH, "VAH"),
					["poc_status"] = EvaluateLevelStatus(ms, ms.POC, "POC"),
					["val_status"] = EvaluateLevelStatus(ms, ms.VAL, "VAL"),
				});
			}
			return result;
		}

		/// <summary>pVAs CERRADAS que NO son parte de un composite (perfiles individuales del pasado).
		/// Cada profile en _allProfiles que no es MergedSession de ningún composite y !IsActive.</summary>
		private List<Dictionary<string, object>> BuildClosedPvasPayload()
		{
			var result = new List<Dictionary<string, object>>();
			if (_allProfiles == null || _allProfiles.Count == 0) return result;

			var mergedSet = new HashSet<VolumeProfileSession>();
			if (_composites != null)
			{
				foreach (var c in _composites)
				{
					if (c != null && c.MergedSession != null) mergedSet.Add(c.MergedSession);
				}
			}

			foreach (var p in _allProfiles)
			{
				if (p == null || p.IsActive) continue;
				if (mergedSet.Contains(p)) continue; // ya está representado en composites

				result.Add(new Dictionary<string, object>
				{
					["start_date"] = p.StartTime.ToString("yyyy-MM-dd"),
					["end_date"] = p.StartTime.ToString("yyyy-MM-dd"),
					["start_time"] = p.StartTime.ToString("yyyy-MM-ddTHH:mm:ss"),
					["end_time"] = p.EndTime.ToString("yyyy-MM-ddTHH:mm:ss"),
					["vah"] = p.VAH,
					["val"] = p.VAL,
					["poc"] = p.POC,
					["status"] = "closed",
					// v2.x: estado virgen/touched/expired por nivel.
					["vah_status"] = EvaluateLevelStatus(p, p.VAH, "VAH"),
					["poc_status"] = EvaluateLevelStatus(p, p.POC, "POC"),
					["val_status"] = EvaluateLevelStatus(p, p.VAL, "VAL"),
				});
			}
			return result;
		}

		/// <summary>FIX 2026-05-02: Reconstruye el state TPO POINT-IN-TIME al timestamp asOf.
		/// Habilita review post-hoc preciso y replay snapshots históricos del TPO.
		///
		/// Lógica:
		/// 1. Composites: SOLO incluir si TODOS sus OriginalProfiles cerraron antes de asOf.
		///    Si AL MENOS uno tiene EndTime > asOf → composite no estaba formado al asOf,
		///    EXCLUIR composite pero los originals quedan disponibles como pVAs individuales.
		/// 2. pVAs cerradas: incluir si EndTime <= asOf (excluyendo los que están en composites
		///    formados al asOf — esos se cuentan dentro del composite).
		/// 3. active_pva: el profile cuyo StartTime <= asOf < EndTime (developing al asOf).
		///
		/// Llamado por el QueryAt handler registrado en RelativeIndicatorRegistry.</summary>
		private IDictionary<string, object> BuildPointInTimePayload(DateTime asOf)
		{
			var dict = new Dictionary<string, object>();
			dict["as_of"] = asOf;

			if (_allProfiles == null)
			{
				dict["error"] = "no profiles loaded";
				dict["closed_pvas"] = new List<Dictionary<string, object>>();
				dict["composites"] = new List<Dictionary<string, object>>();
				dict["active_pva"] = null;
				return dict;
			}

			var closedPvasAtAsOf = new List<Dictionary<string, object>>();
			Dictionary<string, object> activePvaAtAsOf = null;
			var compositesAtAsOf = new List<Dictionary<string, object>>();
			var profilesInFormedComposites = new HashSet<VolumeProfileSession>();

			// Set de MergedSessions (para no contarlas como pVAs individuales abajo)
			var mergedSet = new HashSet<VolumeProfileSession>();
			if (_composites != null)
			{
				foreach (var c in _composites)
				{
					if (c != null && c.MergedSession != null) mergedSet.Add(c.MergedSession);
				}
			}

			// 1. Composites: incluir solo los formados al asOf.
			// Para composites NO formados (al menos un OriginalProfile con EndTime > asOf),
			// extraer los OriginalProfiles cerrados como pVAs individuales (ya que el indicador
			// los removió de _allProfiles al formar el composite — solo viven dentro del comp).
			if (_composites != null)
			{
				foreach (var comp in _composites)
				{
					if (comp == null || comp.MergedSession == null
						|| comp.OriginalProfiles == null || comp.OriginalProfiles.Count == 0) continue;

					bool allClosedBeforeAsOf = true;
					foreach (var p in comp.OriginalProfiles)
					{
						if (p.EndTime > asOf) { allClosedBeforeAsOf = false; break; }
					}

					if (allClosedBeforeAsOf)
					{
						// Composite estaba formado al asOf
						var first = comp.OriginalProfiles[0];
						var last = comp.OriginalProfiles[comp.OriginalProfiles.Count - 1];
						var ms = comp.MergedSession;

						compositesAtAsOf.Add(new Dictionary<string, object>
						{
							["start_date"] = first.StartTime.ToString("yyyy-MM-dd"),
							["end_date"] = last.StartTime.ToString("yyyy-MM-dd"),
							["start_time"] = first.StartTime.ToString("yyyy-MM-ddTHH:mm:ss"),
							["end_time"] = last.EndTime.ToString("yyyy-MM-ddTHH:mm:ss"),
							["vah"] = ms.VAH,
							["val"] = ms.VAL,
							["poc"] = ms.POC,
							["status"] = "closed",
							["is_nadro_auto"] = comp.IsNadroAuto,
							["n_members"] = comp.OriginalProfiles.Count,
						});

						foreach (var p in comp.OriginalProfiles) profilesInFormedComposites.Add(p);
					}
					else
					{
						// Composite NO formado al asOf — extraer OriginalProfiles cerrados
						// como pVAs individuales (no están en _allProfiles porque el indicador
						// los removió al fusionarlos en el composite).
						foreach (var p in comp.OriginalProfiles)
						{
							if (p.EndTime <= asOf)
							{
								closedPvasAtAsOf.Add(new Dictionary<string, object>
								{
									["start_date"] = p.StartTime.ToString("yyyy-MM-dd"),
									["end_date"] = p.StartTime.ToString("yyyy-MM-dd"),
									["start_time"] = p.StartTime.ToString("yyyy-MM-ddTHH:mm:ss"),
									["end_time"] = p.EndTime.ToString("yyyy-MM-ddTHH:mm:ss"),
									["vah"] = p.VAH,
									["val"] = p.VAL,
									["poc"] = p.POC,
									["status"] = "closed",
								});
								profilesInFormedComposites.Add(p); // marcar para no contar 2 veces abajo
							}
							else if (p.StartTime <= asOf && asOf < p.EndTime)
							{
								// Active developing al asOf (raro pero posible: primer profile de un
								// composite en formación cuando estábamos en su sesión)
								activePvaAtAsOf = new Dictionary<string, object>
								{
									["start_date"] = p.StartTime.ToString("yyyy-MM-dd"),
									["end_date"] = p.StartTime.ToString("yyyy-MM-dd"),
									["start_time"] = p.StartTime.ToString("yyyy-MM-ddTHH:mm:ss"),
									["end_time"] = p.EndTime.ToString("yyyy-MM-ddTHH:mm:ss"),
									["vah"] = p.VAH,
									["val"] = p.VAL,
									["poc"] = p.POC,
									["status"] = "active",
								};
								profilesInFormedComposites.Add(p);
							}
						}
					}
				}
			}

			// 2. Profiles individuales: pVAs cerradas + active al asOf
			foreach (var p in _allProfiles)
			{
				if (p == null) continue;
				if (mergedSet.Contains(p)) continue; // evitar contar la MergedSession como pVA
				if (profilesInFormedComposites.Contains(p)) continue; // ya en composite formado

				if (p.EndTime <= asOf)
				{
					// pVA cerrada al asOf
					closedPvasAtAsOf.Add(new Dictionary<string, object>
					{
						["start_date"] = p.StartTime.ToString("yyyy-MM-dd"),
						["end_date"] = p.StartTime.ToString("yyyy-MM-dd"),
						["start_time"] = p.StartTime.ToString("yyyy-MM-ddTHH:mm:ss"),
						["end_time"] = p.EndTime.ToString("yyyy-MM-ddTHH:mm:ss"),
						["vah"] = p.VAH,
						["val"] = p.VAL,
						["poc"] = p.POC,
						["status"] = "closed",
					});
				}
				else if (p.StartTime <= asOf && asOf < p.EndTime)
				{
					// Active developing al asOf
					activePvaAtAsOf = new Dictionary<string, object>
					{
						["start_date"] = p.StartTime.ToString("yyyy-MM-dd"),
						["end_date"] = p.StartTime.ToString("yyyy-MM-dd"),
						["start_time"] = p.StartTime.ToString("yyyy-MM-ddTHH:mm:ss"),
						["end_time"] = p.EndTime.ToString("yyyy-MM-ddTHH:mm:ss"),
						["vah"] = p.VAH,
						["val"] = p.VAL,
						["poc"] = p.POC,
						["status"] = "active",
					};
				}
				// else: profile aún no había empezado al asOf, skip
			}

			dict["closed_pvas"] = closedPvasAtAsOf;
			dict["composites"] = compositesAtAsOf;
			dict["active_pva"] = activePvaAtAsOf;
			return dict;
		}

		/// <summary>pVA activa (perfil del día/sesión actual). Null si no hay activo.</summary>
		private Dictionary<string, object> BuildActivePvaPayload()
		{
			if (_activeProfile == null || !_activeProfile.IsActive) return null;
			return new Dictionary<string, object>
			{
				["start_date"] = _activeProfile.StartTime.ToString("yyyy-MM-dd"),
				["end_date"] = _activeProfile.StartTime.ToString("yyyy-MM-dd"),
				["start_time"] = _activeProfile.StartTime.ToString("yyyy-MM-ddTHH:mm:ss"),
				["end_time"] = _activeProfile.EndTime.ToString("yyyy-MM-ddTHH:mm:ss"),
				["vah"] = _activeProfile.VAH,
				["val"] = _activeProfile.VAL,
				["poc"] = _activeProfile.POC,
				["status"] = "active",
			};
		}

		/// <summary>Hook que detecta si hay nuevas sesiones cerradas desde la ultima ejecucion
		/// y dispara ApplyNadroAutoMerge. Llamar al final de OnBarUpdate (cheap).
		///
		/// IMPORTANTE: espera a que _compositesRestored = true antes de actuar. RestoreComposites
		/// corre en el primer OnRender, restaurando recipes manuales del usuario. Si auto-merge
		/// corriera antes, los merges manuales no se podrian aplicar (originales ya nesteados
		/// en auto-composites).</summary>
		private void NadroAutoMergeTick()
		{
			if (!AutoMergeNadroEnabled) return;
			if (!_compositesRestored) return; // esperar a RestoreComposites
			if (_allProfiles == null) return;

			int closedCount = 0;
			for (int k = 0; k < _allProfiles.Count; k++)
			{
				if (!_allProfiles[k].IsActive) closedCount++;
			}

			if (closedCount != _lastNadroBuildClosedCount)
			{
				ApplyNadroAutoMerge();
				_lastNadroBuildClosedCount = closedCount;
			}
		}
	}
}
