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

		/// <summary>% overlap del VA de A vs B sobre el rango VA mayor (regla NADRO conservadora).
		/// Returns 0.0–1.0. Un VA chico contenido en uno grande NO da 100% — eso seria consolidacion,
		/// no extension de equilibrio.</summary>
		private double NadroVaOverlap(double vahA, double valA, double vahB, double valB)
		{
			double lo = Math.Max(valA, valB);
			double hi = Math.Min(vahA, vahB);
			if (hi <= lo) return 0.0;
			double overlap = hi - lo;
			double maxRange = Math.Max(vahA - valA, vahB - valB);
			return maxRange > 0 ? overlap / maxRange : 0.0;
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

			// Criterio 1: POC centrado dentro del VA (entre 30% y 70%)
			double pocInVa = (poc - val) / vaRange;
			if (pocInVa < 0.30 || pocInVa > 0.70) return false;

			// Criterio 2: existen tails arriba Y abajo del VA (>= 10% cada lado)
			double tailLow  = (val - rangeLow) / range;
			double tailHigh = (rangeHigh - vah) / range;
			if (tailLow < 0.10 || tailHigh < 0.10) return false;

			// Criterio 3: tails simetricas (ratio min/max >= 0.40)
			double tailRatio = Math.Min(tailLow, tailHigh) / Math.Max(tailLow, tailHigh);
			if (tailRatio < 0.40) return false;

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

			// PASO 2: lista de profiles CERRADOS en orden cronologico, ya sin auto-composites
			var closedSorted = _allProfiles
				.Where(p => !p.IsActive && p.POCVolume > 0 && p.Levels != null && p.Levels.Count > 0)
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

			this.RLog("[NADRO] " + string.Format(" DONE. Total composites in _composites: {0}",
				_composites?.Count ?? 0));
			_compositesStamp++;
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

				NinjaTrader.NinjaScript.AddOns.RelativeIndicatorRegistry.Publish(
					string.Format("{0}:{1}:{2}{3}", indName, Instrument.FullName,
						BarsPeriod.Value, BarsPeriod.BarsPeriodType),
					new Dictionary<string, object>
					{
						["bar"] = CurrentBar,
						["bar_time"] = Time[0],
						["close"] = Close[0],
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

				result.Add(new Dictionary<string, object>
				{
					["start_date"] = first.StartTime.ToString("yyyy-MM-dd"),
					["end_date"] = last.StartTime.ToString("yyyy-MM-dd"),
					["start_time"] = first.StartTime.ToString("yyyy-MM-ddTHH:mm:ss"),
					["end_time"] = last.EndTime.ToString("yyyy-MM-ddTHH:mm:ss"),
					["vah"] = comp.MergedSession.VAH,
					["val"] = comp.MergedSession.VAL,
					["poc"] = comp.MergedSession.POC,
					["status"] = comp.MergedSession.IsActive ? "active" : "closed",
					["is_nadro_auto"] = comp.IsNadroAuto,
					["n_members"] = comp.OriginalProfiles.Count,
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
				});
			}
			return result;
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
