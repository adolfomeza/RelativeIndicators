#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns; // RelativeMCP — RLog + Registry
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
	/// <summary>
	/// RelativeVwapLevels v2.3.1 — Indicador lector liviano.
	/// Lee archivos .txt exportados por RelativeMonthlyVwap (y futuros Weekly/Quarterly/Annual)
	/// y dibuja DVA actual + todas las zonas históricas con etiquetas de edad.
	/// Anti-colisión horizontal: etiquetas parten desde la vela actual y se escalonan hacia la derecha.
	/// v2.2.0: Detección automática de cambio de instrumento — recarga niveles sin necesidad de F5.
	/// v2.3.0: Confluencias y deduplicación de DVA actual entre timeframes.
	/// v2.3.1: Se ocultan las etiquetas PVA (VWAP central) en DVA actual y zonas históricas.
	///         El archivo sigue exportando el valor; las confluencias y deduplicación siguen considerando el PVA.
	/// v2.3.2: Daily previous ahora se etiqueta pDVAH/pDVAL (una sola D, sin duplicar).
	///         Convención: DVA = Developing Value Area (actual), pXDVA = Previous/Prior Value Area del timeframe X.
	/// v2.3.3: Daily sin prefijo de timeframe → DVAH/DVAL (actual), pDVAH/pDVAL (previous).
	///         Daily es el timeframe implícito; PVA y CVA quedan reservados para indicadores futuros.
	/// v2.3.4: Renombre del campo central en el .txt: "PVA=" → "VWAP=" (variable interna: level.PVA → level.VWAP).
	///         El término PVA queda liberado para su uso correcto (Previous Value Area = pXDVA).
	///         Retrocompat: el parser aún acepta "PVA=" de archivos escritos por exportadores antiguos.
	/// v2.3.5: Tracking histórico de confluencias. Cada zona se pinta con inicio (primera detección) y fin (separación
	///         de niveles o breach por precio). Activas llegan hasta la vela actual; cerradas quedan congeladas.
	///         Persistencia en VwapLevels/Confluences_{INSTRUMENT}.txt — sobrevive reinicios.
	/// v2.3.6: Toggle individual por timeframe para incluir/excluir del cálculo y render de confluencias.
	///         Un TF deshabilitado (a) no participa en nuevos tracks, (b) oculta tracks existentes que lo involucren.
	/// v2.3.7: Fix auto-breach: los tracks nuevos requieren un Close fuera del rango para "armarse" antes de poder
	///         ser breached. Sin esto, confluencias cerca del precio actual se auto-mataban en el mismo tick.
	/// v2.3.8: Defensa contra tracks inválidos en load y render (PriceMin/PriceMax <= 0 o mal ordenados).
	///         Evita rectángulos que se dibujan hasta Y=0 por datos corruptos del archivo.
	/// v2.3.9: Rango de ConfluenceThreshold ampliado de [1..50] a [1..1000] ticks.
	///         En instrumentos volátiles como MNQ (tick=0.25), el default 5 ticks (1.25 pts) es demasiado estricto.
	/// v2.3.10: Reconstrucción histórica para confluencias puramente entre zonas PVA (todos los miembros son históricos).
	///          StartTime = max(zone.StartTime) del grupo; se busca breach en barras pasadas. Confluencias que
	///          involucran DVA actuales siguen arrancando en "ahora" (no hay historial del DVA developing).
	/// </summary>
	public class RelativeVwapLevels : Indicator
	{
		private class ZoneEntry
		{
			public double UpperY;
			public double MidY;
			public double LowerY;
			public DateTime StartTime;
		}

		private class VwapLevel
		{
			public string Timeframe;
			public double DVAH;
			public double VWAP;
			public double DVAL;
			public DateTime Timestamp;
			public List<ZoneEntry> Zones = new List<ZoneEntry>();
		}

		// v2.3.5: tracking histórico de zonas de confluencia
		private class ConfluenceTrack
		{
			public string GroupKey;           // identificador estable del conjunto de miembros
			public double PriceMin;
			public double PriceMax;
			public DateTime StartTime;        // momento de la primera detección del grupo
			public DateTime LastSeenTime;     // último frame en que el grupo fue detectado confluyendo
			public DateTime EndTime;          // congelado al cerrar (separación o breach)
			public bool IsActive;             // true = aún confluyendo, extender hasta NOW
			public bool IsBreached;           // true = el precio atravesó el rango, congela EndTime
			public bool IsArmed;              // v2.3.7: true tras primer Close fuera del rango — evita auto-breach inmediato
		}

		private List<VwapLevel> _levels = new List<VwapLevel>();
		private List<ConfluenceTrack> _confluenceHistory = new List<ConfluenceTrack>();
		private DateTime _lastRead = DateTime.MinValue;
		private string _levelsDir;
		private string _confluencesFile;
		private string _lastInstrument = "";

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description		= "Lee niveles VWAP exportados (Monthly/Weekly/Quarterly/Annual) y los dibuja en el chart.";
				Name			= "RelativeVwapLevels";
				IsOverlay		= true;
				Calculate		= Calculate.OnPriceChange;
				IsAutoScale		= false;
				IsSuspendedWhileInactive = true;
			}
			else if (State == State.DataLoaded)
			{
				_levelsDir = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
					"NinjaTrader 8", "bin", "Custom", "VwapLevels");
			}
		}

		protected override void OnBarUpdate()
		{
			if (State != State.Realtime && CurrentBar < Bars.Count - 2) return;

			// Detectar cambio de instrumento y forzar recarga + recargar historial de confluencias
			string currentInstrument = Instrument.MasterInstrument.Name;
			if (currentInstrument != _lastInstrument)
			{
				_confluencesFile = Path.Combine(_levelsDir, "Confluences_" + currentInstrument + ".txt");
				LoadConfluenceHistory();
				_lastRead = DateTime.MinValue;
				_lastInstrument = currentInstrument;
			}

			// Refrescar niveles desde archivos y actualizar tracking de confluencias
			if ((DateTime.Now - _lastRead).TotalSeconds >= RefreshSeconds)
			{
				ReadLevels();
				UpdateConfluenceTracking();
				SaveConfluenceHistory();
			}

			// Chequear breach en cada tick/bar: si High[0]/Low[0] atraviesan un rect activo, congelarlo
			CheckConfluenceBreach();

			// --- RelativeMCP observability ---
			if (CurrentBar >= 0)
			{
				try
				{
					int activeConfs = 0, armedConfs = 0, breachedConfs = 0;
					foreach (var t in _confluenceHistory)
					{
						if (t.IsActive && !t.IsBreached) activeConfs++;
						if (t.IsArmed) armedConfs++;
						if (t.IsBreached) breachedConfs++;
					}

					RelativeIndicatorRegistry.Publish(
						string.Format("{0}:{1}:{2}{3}", typeof(RelativeVwapLevels).Name,
							Instrument.FullName, BarsPeriod.Value, BarsPeriod.BarsPeriodType),
						new Dictionary<string, object>
						{
							["bar"] = CurrentBar,
							["bar_time"] = Time[0],
							["close"] = Close[0],
							["total_levels"] = _levels.Count,
							["total_confluences_history"] = _confluenceHistory.Count,
							["active_confluences"] = activeConfs,
							["armed_confluences"] = armedConfs,
							["breached_confluences"] = breachedConfs,
							["instrument_read"] = _lastInstrument ?? "",
							["confluences_file"] = _confluencesFile ?? "",
						});

					if (IsFirstTickOfBar && State == State.Realtime)
						this.RLog("bar={0} close={1:F2} levels={2} active_confluences={3} armed={4} breached={5}",
							CurrentBar, Close[0], _levels.Count, activeConfs, armedConfs, breachedConfs);
				}
				catch { }
			}
			// --- end RelativeMCP ---
		}

		#region Confluence Tracking (v2.3.5)

		// v2.3.6: ¿este timeframe cuenta para el cálculo/render de confluencias?
		private bool IsConfluenceEnabled(string timeframe)
		{
			switch (timeframe)
			{
				case "Daily":     return UseDailyInConfluences;
				case "Weekly":    return UseWeeklyInConfluences;
				case "Monthly":   return UseMonthlyInConfluences;
				case "Quarterly": return UseQuarterlyInConfluences;
				case "Annual":    return UseAnnualInConfluences;
				default:          return false;
			}
		}

		// ¿Todos los miembros del track pertenecen a TFs actualmente habilitados para confluencias?
		private bool TrackMembersEnabled(ConfluenceTrack t)
		{
			if (string.IsNullOrEmpty(t.GroupKey)) return false;
			foreach (var member in t.GroupKey.Split('|'))
			{
				int us = member.IndexOf('_');
				if (us < 0) continue;
				string tf = member.Substring(0, us);
				if (!IsConfluenceEnabled(tf)) return false;
			}
			return true;
		}

		// Recalcula confluencias actuales, actualiza tracks activos, marca cerrados por separación de niveles.
		// NO chequea breach — eso lo hace CheckConfluenceBreach() por tick.
		private void UpdateConfluenceTracking()
		{
			if (!ShowConfluences) return;

			// 1. Armar lista (price, memberKey) de todos los niveles visibles y habilitados para confluencia
			var items = new List<System.ValueTuple<double, string>>();
			foreach (var level in _levels)
			{
				if (!IsLevelVisible(level.Timeframe)) continue;
				if (!IsConfluenceEnabled(level.Timeframe)) continue; // v2.3.6: filtro por TF
				if (level.DVAH > 0) items.Add(System.ValueTuple.Create(level.DVAH, level.Timeframe + "_DVAH"));
				if (level.VWAP > 0) items.Add(System.ValueTuple.Create(level.VWAP, level.Timeframe + "_VWAP"));
				if (level.DVAL > 0) items.Add(System.ValueTuple.Create(level.DVAL, level.Timeframe + "_DVAL"));
				foreach (var zone in level.Zones)
				{
					string zKey = level.Timeframe + "_Z_" + zone.StartTime.Ticks;
					if (zone.UpperY > 0) items.Add(System.ValueTuple.Create(zone.UpperY, zKey + "_H"));
					if (zone.MidY   > 0) items.Add(System.ValueTuple.Create(zone.MidY,   zKey + "_M"));
					if (zone.LowerY > 0) items.Add(System.ValueTuple.Create(zone.LowerY, zKey + "_L"));
				}
			}
			items.Sort((a, b) => a.Item1.CompareTo(b.Item1));

			double threshold = ConfluenceThreshold * Instrument.MasterInstrument.TickSize;
			DateTime now = Time[0];

			// 2. Agrupar y construir tracks candidatos
			var seenKeys = new HashSet<string>();
			int i = 0;
			while (i < items.Count)
			{
				int j = i + 1;
				while (j < items.Count && items[j].Item1 - items[j - 1].Item1 <= threshold) j++;
				int cnt = j - i;
				if (cnt >= MinConfluenceCount)
				{
					var members = new List<string>();
					for (int k = i; k < j; k++) members.Add(items[k].Item2);
					members.Sort(System.StringComparer.Ordinal);
					string groupKey = string.Join("|", members);
					double pMin = items[i].Item1;
					double pMax = items[j - 1].Item1;

					seenKeys.Add(groupKey);

					// 3. Match contra tracks activos existentes
					ConfluenceTrack existing = null;
					for (int t = 0; t < _confluenceHistory.Count; t++)
					{
						if (_confluenceHistory[t].IsActive && !_confluenceHistory[t].IsBreached
						    && _confluenceHistory[t].GroupKey == groupKey)
						{ existing = _confluenceHistory[t]; break; }
					}
					if (existing != null)
					{
						existing.PriceMin = pMin;
						existing.PriceMax = pMax;
						existing.LastSeenTime = now;
					}
					else
					{
						// v2.3.10: si el grupo es solo zonas históricas (PVAs), reconstruir StartTime retroactivamente
						// al momento en que nació el PVA más joven del grupo. Los precios de PVAs son fijos en el tiempo,
						// así que la confluencia existía desde ese momento. Luego buscamos breach histórico.
						DateTime groupStart = now;
						bool allHistorical = true;
						DateTime maxZoneStart = DateTime.MinValue;
						for (int k = i; k < j; k++)
						{
							string mk = items[k].Item2;
							int zIdx = mk.IndexOf("_Z_");
							if (zIdx < 0) { allHistorical = false; continue; }
							string rest = mk.Substring(zIdx + 3);
							int endTicks = rest.IndexOf('_');
							string ticksStr = endTicks >= 0 ? rest.Substring(0, endTicks) : rest;
							long ticks;
							if (long.TryParse(ticksStr, out ticks))
							{
								var zStart = new DateTime(ticks);
								if (zStart > maxZoneStart) maxZoneStart = zStart;
							}
						}
						if (allHistorical && maxZoneStart > DateTime.MinValue)
							groupStart = maxZoneStart;

						var newTrack = new ConfluenceTrack {
							GroupKey = groupKey,
							PriceMin = pMin,
							PriceMax = pMax,
							StartTime = groupStart,
							LastSeenTime = now,
							IsActive = true,
							IsBreached = false,
							IsArmed = allHistorical  // retroactivo ya cuenta como armado
						};
						if (allHistorical)
							CheckHistoricalBreach(newTrack);
						_confluenceHistory.Add(newTrack);
					}
				}
				i = j;
			}

			// 4. Cerrar tracks activos que ya no están confluyendo (separación de niveles)
			foreach (var t in _confluenceHistory)
			{
				if (t.IsActive && !t.IsBreached && !seenKeys.Contains(t.GroupKey))
				{
					t.IsActive = false;
					t.EndTime = t.LastSeenTime;
				}
			}
		}

		// Cierra tracks activos cuya banda es atravesada por el precio de la barra actual.
		// v2.3.7: los tracks solo se arman tras un Close fuera del rango. Evita auto-breach inmediato
		// cuando la confluencia aparece en un rango de precio cercano al precio actual.
		private void CheckConfluenceBreach()
		{
			if (_confluenceHistory.Count == 0) return;
			double hi = High[0];
			double lo = Low[0];
			double cl = Close[0];
			DateTime now = Time[0];
			foreach (var t in _confluenceHistory)
			{
				if (!t.IsActive || t.IsBreached) continue;

				// Arm: primer Close fuera del rango
				if (!t.IsArmed)
				{
					if (cl > t.PriceMax || cl < t.PriceMin)
						t.IsArmed = true;
					continue; // no chequear breach hasta estar armado
				}

				// Breach: vela actual atraviesa el rango de la confluencia ya armada
				if (hi >= t.PriceMin && lo <= t.PriceMax)
				{
					t.IsBreached = true;
					t.IsActive = false;
					t.EndTime = now;
				}
			}
		}

		// v2.3.10: busca el primer breach histórico de un track retroactivo entre StartTime y la barra actual.
		// Si encuentra una vela pasada que envuelva el rango, congela el track en ese momento.
		private void CheckHistoricalBreach(ConfluenceTrack t)
		{
			if (Bars == null || CurrentBar < 0) return;
			// Localizar primer bar >= StartTime (lineal desde el inicio — aceptable, se hace 1 vez por nuevo track)
			int startBar = -1;
			for (int b = 0; b <= CurrentBar; b++)
			{
				if (Bars.GetTime(b) >= t.StartTime) { startBar = b; break; }
			}
			if (startBar < 0) return;

			for (int b = startBar; b <= CurrentBar; b++)
			{
				double hi = Bars.GetHigh(b);
				double lo = Bars.GetLow(b);
				if (hi >= t.PriceMin && lo <= t.PriceMax)
				{
					t.IsBreached = true;
					t.IsActive   = false;
					t.EndTime    = Bars.GetTime(b);
					return;
				}
			}
		}

		private void LoadConfluenceHistory()
		{
			_confluenceHistory.Clear();
			if (string.IsNullOrEmpty(_confluencesFile) || !File.Exists(_confluencesFile)) return;
			try
			{
				var lines = File.ReadAllLines(_confluencesFile);
				foreach (var line in lines)
				{
					if (string.IsNullOrWhiteSpace(line)) continue;
					var p = line.Split('|');
					if (p.Length < 7) continue;
					var t = new ConfluenceTrack();
					t.GroupKey = p[0];
					double.TryParse(p[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out t.PriceMin);
					double.TryParse(p[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out t.PriceMax);
					DateTime.TryParse(p[3], out t.StartTime);
					DateTime.TryParse(p[4], out t.LastSeenTime);
					DateTime.TryParse(p[5], out t.EndTime);
					int flags = 0; int.TryParse(p[6], out flags);
					t.IsActive   = (flags & 1) != 0;
					t.IsBreached = (flags & 2) != 0;
					t.IsArmed    = (flags & 4) != 0;
					// v2.3.8: descartar tracks inválidos (precios <=0 o mal ordenados). Evita rectángulos gigantes.
					if (t.PriceMin <= 0 || t.PriceMax <= 0 || t.PriceMax < t.PriceMin) continue;
					_confluenceHistory.Add(t);
				}
			}
			catch { }
		}

		private void SaveConfluenceHistory()
		{
			if (string.IsNullOrEmpty(_confluencesFile) || _confluenceHistory.Count == 0) return;
			try
			{
				if (!Directory.Exists(_levelsDir)) Directory.CreateDirectory(_levelsDir);
				var sb = new System.Text.StringBuilder();
				foreach (var t in _confluenceHistory)
				{
					int flags = (t.IsActive ? 1 : 0) | (t.IsBreached ? 2 : 0) | (t.IsArmed ? 4 : 0);
					sb.AppendLine(string.Join("|", new[] {
						t.GroupKey,
						t.PriceMin.ToString(System.Globalization.CultureInfo.InvariantCulture),
						t.PriceMax.ToString(System.Globalization.CultureInfo.InvariantCulture),
						t.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
						t.LastSeenTime.ToString("yyyy-MM-dd HH:mm:ss"),
						t.EndTime.ToString("yyyy-MM-dd HH:mm:ss"),
						flags.ToString()
					}));
				}
				File.WriteAllText(_confluencesFile, sb.ToString());
			}
			catch { }
		}

		#endregion

		private void ReadLevels()
		{
			_levels.Clear();
			_lastRead = DateTime.Now;
			if (!Directory.Exists(_levelsDir)) return;

			try
			{
				string instrument = Instrument.MasterInstrument.Name;
				string[] files = Directory.GetFiles(_levelsDir, "*_" + instrument + ".txt");
				foreach (string file in files)
				{
					var level = ParseLevelFile(file);
					if (level != null)
						_levels.Add(level);
				}
			}
			catch { }
		}

		private VwapLevel ParseLevelFile(string filePath)
		{
			try
			{
				string[] lines = File.ReadAllLines(filePath);
				var level = new VwapLevel();
				int zoneCount = 0;
				foreach (string line in lines)
				{
					int eq = line.IndexOf('=');
					if (eq < 0) continue;
					string key = line.Substring(0, eq).Trim();
					string val = line.Substring(eq + 1).Trim();
					switch (key)
					{
						case "TIMEFRAME": level.Timeframe = val; break;
						case "TIMESTAMP": DateTime.TryParse(val, out level.Timestamp); break;
						case "DVAH": double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out level.DVAH); break;
						case "VWAP": // v2.3.4: nombre actualizado (antes "PVA" — era confuso: no es Previous Value Area)
						case "PVA":  // retrocompat con archivos escritos por versiones anteriores
							double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out level.VWAP); break;
						case "DVAL": double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out level.DVAL); break;
						case "ZONE_COUNT": int.TryParse(val, out zoneCount); break;
						default:
							if (key.StartsWith("ZONE_"))
							{
								string[] parts = val.Split('|');
								if (parts.Length >= 4)
								{
									// Nuevo formato: upper|mid|lower|startTime
									var zone = new ZoneEntry();
									double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out zone.UpperY);
									double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out zone.MidY);
									double.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out zone.LowerY);
									DateTime.TryParse(parts[3], out zone.StartTime);
									if (zone.UpperY > 0 && zone.LowerY > 0)
										level.Zones.Add(zone);
								}
							}
							break;
					}
				}
				if (string.IsNullOrEmpty(level.Timeframe)) return null;
				return level;
			}
			catch { return null; }
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);
			if (_levels.Count == 0 || chartControl == null || !IsVisible) return;

			List<SharpDX.RectangleF> labelObstacles = new List<SharpDX.RectangleF>();
			var currentPeriodPrices = new List<System.ValueTuple<double, int>>();

			// Posición X de la última barra visible y borde izquierdo del panel
			int lastBarIndex = ChartBars.ToIndex;
			float lastBarX = (float)chartControl.GetXByBarIndex(ChartBars, lastBarIndex);
			float xLeft    = (float)chartControl.GetXByBarIndex(ChartBars, ChartBars.FromIndex);

			// Poblar currentPeriodPrices para deduplicación de etiquetas DVA actual
			foreach (var level in _levels)
			{
				if (!IsLevelVisible(level.Timeframe)) continue;
				int h = GetHierarchy(level.Timeframe);
				if (level.DVAH > 0) currentPeriodPrices.Add(System.ValueTuple.Create(level.DVAH, h));
				if (level.VWAP > 0) currentPeriodPrices.Add(System.ValueTuple.Create(level.VWAP, h));
				if (level.DVAL > 0) currentPeriodPrices.Add(System.ValueTuple.Create(level.DVAL, h));
			}
			double dedupTick = Instrument.MasterInstrument.TickSize;

			// Dibujar zonas de confluencia primero (quedan detrás de las líneas)
			if (ShowConfluences)
				DrawConfluences(chartControl, chartScale, lastBarX);

			foreach (var level in _levels)
			{
				bool show = false;
				System.Windows.Media.Brush colorBrush = Brushes.White;
				System.Windows.Media.Brush zoneColorBrush = Brushes.White;

				switch (level.Timeframe)
				{
					case "Daily":     show = ShowDaily;     colorBrush = DailyColor;     zoneColorBrush = DailyZoneColor;   break;
					case "Monthly":   show = ShowMonthly;   colorBrush = MonthlyColor;   zoneColorBrush = MonthlyZoneColor; break;
					case "Weekly":    show = ShowWeekly;    colorBrush = WeeklyColor;    zoneColorBrush = WeeklyColor;      break;
					case "Quarterly": show = ShowQuarterly; colorBrush = QuarterlyColor; zoneColorBrush = QuarterlyColor;   break;
					case "Annual":    show = ShowAnnual;    colorBrush = AnnualColor;    zoneColorBrush = AnnualColor;      break;
				}
				if (!show) continue;

				var dxBrush = colorBrush.ToDxBrush(RenderTarget);
				var zoneDxBrush = zoneColorBrush.ToDxBrush(RenderTarget);
				var textFmt = new SimpleFont("Arial", LabelFontSize).ToDirectWriteTextFormat();
				textFmt.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
				textFmt.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;

				string tfPrefix;
				string tfPrefixZone;
				switch (level.Timeframe)
				{
					case "Annual":    tfPrefix = "y";  tfPrefixZone = "pY"; break;
					case "Monthly":   tfPrefix = "m";  tfPrefixZone = "pM"; break;
					case "Daily":     tfPrefix = "";   tfPrefixZone = "p";  break; // v2.3.3: Daily sin prefijo → DVAH/DVAL (actual), pDVAH/pDVAL (previous)
					default:          tfPrefix = level.Timeframe.Substring(0, 1).ToLower();
					                  tfPrefixZone = "p" + level.Timeframe.Substring(0, 1); break;
				}

				// DVA actual — suprimir si un timeframe más granular tiene el mismo precio (±1 tick)
				// v2.3.1: la etiqueta PVA (VWAP central) ya no se dibuja; el valor sigue participando en confluencias y deduplicación.
				int lvlH = GetHierarchy(level.Timeframe);
				if (level.DVAH > 0 && !IsCoveredByMoreGranular(level.DVAH, lvlH, currentPeriodPrices, dedupTick))
					DrawLevel(chartScale, dxBrush, textFmt, lastBarX, level.DVAH, tfPrefix + "DVAH", labelObstacles);
				if (level.DVAL > 0 && !IsCoveredByMoreGranular(level.DVAL, lvlH, currentPeriodPrices, dedupTick))
					DrawLevel(chartScale, dxBrush, textFmt, lastBarX, level.DVAL, tfPrefix + "DVAL", labelObstacles);

				// Zonas históricas con edad según timeframe
				// v2.3.1: la etiqueta PVA (VWAP central de la zona) ya no se dibuja.
				DateTime refTime = level.Timestamp != DateTime.MinValue ? level.Timestamp : DateTime.Now;
				foreach (var zone in level.Zones)
				{
					string ageStr = GetAgeString(level.Timeframe, refTime, zone.StartTime);
					DrawLevel(chartScale, zoneDxBrush, textFmt, lastBarX, zone.UpperY, tfPrefixZone + "DVAH" + ageStr, labelObstacles);
					DrawLevel(chartScale, zoneDxBrush, textFmt, lastBarX, zone.LowerY, tfPrefixZone + "DVAL" + ageStr, labelObstacles);
				}

				dxBrush.Dispose();
				zoneDxBrush.Dispose();
				textFmt.Dispose();
			}
		}

		private void DrawLevel(ChartScale chartScale, SharpDX.Direct2D1.Brush brush,
			SharpDX.DirectWrite.TextFormat textFmt, float lastBarX, double price, string label,
			List<SharpDX.RectangleF> obstacles)
		{
			float y      = (float)chartScale.GetYByValue(price);
			float labelX = lastBarX + 10;
			int   pad    = LabelFontSize / 2 + 3; // padding vertical para colisión

			// Rect de colisión: más alta que el texto real para evitar solapamiento visual
			SharpDX.RectangleF rect = new SharpDX.RectangleF(labelX, y - LabelFontSize - pad, 120, (LabelFontSize + pad) * 2);

			// Anti-colisión horizontal: desplazar hacia la derecha si hay solapamiento
			for (int attempt = 0; attempt < 20; attempt++)
			{
				bool collision = false;
				foreach (var obs in obstacles)
				{
					// Intersección incluyendo contacto en borde (>= y <=)
					if (rect.Bottom >= obs.Top && rect.Top <= obs.Bottom &&
					    rect.Right  >  obs.Left && rect.Left < obs.Right)
					{
						collision = true;
						break;
					}
				}
				if (!collision) break;
				labelX += 130;
				rect = new SharpDX.RectangleF(labelX, y - LabelFontSize - pad, 120, (LabelFontSize + pad) * 2);
			}

			// Dibujar texto en rect centrada en y (sin el padding extra)
			var drawRect = new SharpDX.RectangleF(labelX, y - LabelFontSize, 120, LabelFontSize * 2);
			RenderTarget.DrawText(label, textFmt, drawRect, brush);
			obstacles.Add(rect); // guardar rect CON padding para proteger espacio
		}

		private bool IsLevelVisible(string timeframe)
		{
			switch (timeframe)
			{
				case "Daily":     return ShowDaily;
				case "Monthly":   return ShowMonthly;
				case "Weekly":    return ShowWeekly;
				case "Quarterly": return ShowQuarterly;
				case "Annual":    return ShowAnnual;
				default:          return false;
			}
		}

		private string GetAgeString(string timeframe, DateTime refTime, DateTime startTime)
		{
			switch (timeframe)
			{
				case "Daily":
				{
					int days = (int)(refTime.Date - startTime.Date).TotalDays;
					return days > 0 ? " -" + days + "D" : "";
				}
				case "Weekly":
				{
					int weeks = (int)((refTime.Date - startTime.Date).TotalDays / 7.0);
					return weeks > 0 ? " -" + weeks + "W" : "";
				}
				case "Quarterly":
				{
					int months = ((refTime.Year - startTime.Year) * 12) + refTime.Month - startTime.Month;
					int quarters = months / 3;
					return quarters > 0 ? " -" + quarters + "Q" : "";
				}
				case "Annual":
				{
					int years = refTime.Year - startTime.Year;
					return years > 0 ? " -" + years + "Y" : "";
				}
				default: // Monthly
				{
					int months = ((refTime.Year - startTime.Year) * 12) + refTime.Month - startTime.Month;
					return months > 0 ? " -" + months + "M" : "";
				}
			}
		}

		private int GetHierarchy(string timeframe)
		{
			switch (timeframe)
			{
				case "Daily":     return 5; // más granular
				case "Weekly":    return 4;
				case "Monthly":   return 3;
				case "Quarterly": return 2;
				case "Annual":    return 1; // menos granular
				default:          return 0;
			}
		}

		private bool IsCoveredByMoreGranular(double price, int hierarchy,
			List<System.ValueTuple<double, int>> allPrices, double tickSize)
		{
			foreach (var entry in allPrices)
			{
				if (entry.Item2 > hierarchy && Math.Abs(entry.Item1 - price) <= tickSize)
					return true;
			}
			return false;
		}

		// v2.3.5: dibuja cada track histórico con inicio y fin horizontales definidos.
		// - Activos (IsActive && !IsBreached): llegan hasta la vela actual
		// - Cerrados por separación o breach: terminan en EndTime (congelado)
		private void DrawConfluences(ChartControl chartControl, ChartScale chartScale, float lastBarX)
		{
			if (_confluenceHistory.Count == 0) return;

			var confBrush = ConfluenceColor.ToDxBrush(RenderTarget);
			confBrush.Opacity = (float)(ConfluenceOpacity / 100.0);

			foreach (var t in _confluenceHistory)
			{
				if (!TrackMembersEnabled(t)) continue; // v2.3.6: ocultar tracks con algún TF deshabilitado
				// v2.3.8: defensa contra tracks inválidos (precios 0, NaN, etc.) — evita rect hasta Y=0
				if (t.PriceMin <= 0 || t.PriceMax <= 0 || t.PriceMax < t.PriceMin) continue;

				float xStart = (float)chartControl.GetXByTime(t.StartTime);
				float xEnd;
				if (t.IsActive && !t.IsBreached) xEnd = lastBarX;
				else                             xEnd = (float)chartControl.GetXByTime(t.EndTime);

				if (xEnd <= xStart) continue;  // nada que dibujar

				float yTop = (float)chartScale.GetYByValue(t.PriceMax);
				float yBot = (float)chartScale.GetYByValue(t.PriceMin);
				if (yBot - yTop < 1) yBot = yTop + 1;
				RenderTarget.FillRectangle(
					new SharpDX.RectangleF(xStart, yTop, xEnd - xStart, yBot - yTop),
					confBrush);
			}

			confBrush.Dispose();
		}

		#region Properties

		[Display(Name = "Mostrar Monthly", GroupName = "01. Timeframes", Order = 0)]
		public bool ShowMonthly { get; set; } = true;

		[Display(Name = "Mostrar Weekly", GroupName = "01. Timeframes", Order = 1)]
		public bool ShowWeekly { get; set; } = true;

		[Display(Name = "Mostrar Quarterly", GroupName = "01. Timeframes", Order = 2)]
		public bool ShowQuarterly { get; set; } = true;

		[Display(Name = "Mostrar Annual", GroupName = "01. Timeframes", Order = 3)]
		public bool ShowAnnual { get; set; } = true;

		[Display(Name = "Mostrar Daily", GroupName = "01. Timeframes", Order = 4)]
		public bool ShowDaily { get; set; } = true;

		[XmlIgnore]
		[Display(Name = "Color Monthly DVA", GroupName = "02. Colores", Order = 0)]
		public System.Windows.Media.Brush MonthlyColor { get; set; } = Brushes.Cyan;
		[Browsable(false)] public string MonthlyColorSerializable { get { return Serialize.BrushToString(MonthlyColor); } set { MonthlyColor = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name = "Color Monthly Zonas", GroupName = "02. Colores", Order = 1)]
		public System.Windows.Media.Brush MonthlyZoneColor { get; set; } = Brushes.DarkCyan;
		[Browsable(false)] public string MonthlyZoneColorSerializable { get { return Serialize.BrushToString(MonthlyZoneColor); } set { MonthlyZoneColor = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name = "Color Weekly", GroupName = "02. Colores", Order = 2)]
		public System.Windows.Media.Brush WeeklyColor { get; set; } = Brushes.Yellow;
		[Browsable(false)] public string WeeklyColorSerializable { get { return Serialize.BrushToString(WeeklyColor); } set { WeeklyColor = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name = "Color Quarterly", GroupName = "02. Colores", Order = 3)]
		public System.Windows.Media.Brush QuarterlyColor { get; set; } = Brushes.Orange;
		[Browsable(false)] public string QuarterlyColorSerializable { get { return Serialize.BrushToString(QuarterlyColor); } set { QuarterlyColor = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name = "Color Annual", GroupName = "02. Colores", Order = 4)]
		public System.Windows.Media.Brush AnnualColor { get; set; } = Brushes.Magenta;
		[Browsable(false)] public string AnnualColorSerializable { get { return Serialize.BrushToString(AnnualColor); } set { AnnualColor = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name = "Color Daily DVA", GroupName = "02. Colores", Order = 5)]
		public System.Windows.Media.Brush DailyColor { get; set; } = Brushes.LimeGreen;
		[Browsable(false)] public string DailyColorSerializable { get { return Serialize.BrushToString(DailyColor); } set { DailyColor = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name = "Color Daily Zonas", GroupName = "02. Colores", Order = 6)]
		public System.Windows.Media.Brush DailyZoneColor { get; set; } = Brushes.Green;
		[Browsable(false)] public string DailyZoneColorSerializable { get { return Serialize.BrushToString(DailyZoneColor); } set { DailyZoneColor = Serialize.StringToBrush(value); } }

		[Range(6, 20)]
		[Display(Name = "Tamaño Fuente", GroupName = "03. Visual", Order = 0)]
		public int LabelFontSize { get; set; } = 10;

		[Range(1, 60)]
		[Display(Name = "Refresh (seg)", GroupName = "03. Visual", Order = 1)]
		public int RefreshSeconds { get; set; } = 5;

		[Display(Name = "Mostrar Confluencias", GroupName = "04. Confluencias", Order = 0)]
		public bool ShowConfluences { get; set; } = true;

		[Range(1, 1000)]
		[Display(Name = "Threshold (ticks)", GroupName = "04. Confluencias", Order = 1)]
		public int ConfluenceThreshold { get; set; } = 5;

		[Range(2, 10)]
		[Display(Name = "Min. Niveles", GroupName = "04. Confluencias", Order = 2)]
		public int MinConfluenceCount { get; set; } = 2;

		[XmlIgnore]
		[Display(Name = "Color", GroupName = "04. Confluencias", Order = 3)]
		public System.Windows.Media.Brush ConfluenceColor { get; set; } = Brushes.Yellow;
		[Browsable(false)] public string ConfluenceColorSerializable { get { return Serialize.BrushToString(ConfluenceColor); } set { ConfluenceColor = Serialize.StringToBrush(value); } }

		[Range(5, 80)]
		[Display(Name = "Opacidad (%)", GroupName = "04. Confluencias", Order = 4)]
		public int ConfluenceOpacity { get; set; } = 20;

		// v2.3.6: toggle individual por timeframe para excluir del cálculo/render de confluencias
		[Display(Name = "Incluir Daily", GroupName = "04. Confluencias", Order = 10)]
		public bool UseDailyInConfluences { get; set; } = true;

		[Display(Name = "Incluir Weekly", GroupName = "04. Confluencias", Order = 11)]
		public bool UseWeeklyInConfluences { get; set; } = true;

		[Display(Name = "Incluir Monthly", GroupName = "04. Confluencias", Order = 12)]
		public bool UseMonthlyInConfluences { get; set; } = true;

		[Display(Name = "Incluir Quarterly", GroupName = "04. Confluencias", Order = 13)]
		public bool UseQuarterlyInConfluences { get; set; } = true;

		[Display(Name = "Incluir Annual", GroupName = "04. Confluencias", Order = 14)]
		public bool UseAnnualInConfluences { get; set; } = true;

		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RelativeIndicators.RelativeVwapLevels[] cacheRelativeVwapLevels;
		public RelativeIndicators.RelativeVwapLevels RelativeVwapLevels()
		{
			return RelativeVwapLevels(Input);
		}

		public RelativeIndicators.RelativeVwapLevels RelativeVwapLevels(ISeries<double> input)
		{
			if (cacheRelativeVwapLevels != null)
				for (int idx = 0; idx < cacheRelativeVwapLevels.Length; idx++)
					if (cacheRelativeVwapLevels[idx] != null &&  cacheRelativeVwapLevels[idx].EqualsInput(input))
						return cacheRelativeVwapLevels[idx];
			return CacheIndicator<RelativeIndicators.RelativeVwapLevels>(new RelativeIndicators.RelativeVwapLevels(), input, ref cacheRelativeVwapLevels);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeVwapLevels RelativeVwapLevels()
		{
			return indicator.RelativeVwapLevels(Input);
		}

		public Indicators.RelativeIndicators.RelativeVwapLevels RelativeVwapLevels(ISeries<double> input )
		{
			return indicator.RelativeVwapLevels(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeVwapLevels RelativeVwapLevels()
		{
			return indicator.RelativeVwapLevels(Input);
		}

		public Indicators.RelativeIndicators.RelativeVwapLevels RelativeVwapLevels(ISeries<double> input )
		{
			return indicator.RelativeVwapLevels(input);
		}
	}
}

#endregion
