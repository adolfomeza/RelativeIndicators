#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.AddOns; // RelativeMCP — this.RLog() + RelativeIndicatorRegistry
using SharpDX;
using SharpDX.Direct2D1;
using NinjaTrader.Custom.AddOns;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class RelativeVolumeProfile : Indicator
	{
		#region Constants
		private const string VERSION = "1.1.0";
		#endregion

		#region Data Structures

		private class VolumeLevelData
		{
			public double Price;
			public long   Volume;          // Volumen real en modo Volume, conteo TPO en modo TPO
			public HashSet<int> TpoPeriods; // Periodos TPO ya contados en este nivel (null en modo Volume)

			// Cache de TpoPeriods ordenados — evita recrear List+Sort por nivel por frame.
			// Se invalida (= null) cuando se agrega un periodo nuevo.
			public List<int> SortedPeriodsCache;
		}

		/// <summary>Segmento VWAP archivado (anchor anterior dentro de la misma sesión).</summary>
		private class VwapSegment
		{
			public int StartIdx;
			public int EndIdx;
			public Dictionary<int, double> Values; // barIdx → vwapValue
		}

		private class VolumeProfileSession
		{
			public DateTime StartTime;
			public DateTime EndTime;
			public int      StartBarIdx;
			public int      EndBarIdx;
			public int      LastVolumeBarIdx;  // última barra de la serie primaria que recibió volumen
			public bool     IsActive;
			public Dictionary<long, VolumeLevelData> Levels;
			public long     TotalVolume;

			// TPO: mapea periodIndex (0=A,1=B,...) → barIdx del chart primario (para vista extendida)
			public Dictionary<int, int> TpoPeriodBarMap;

			public double   POC;
			public double   VAH;
			public double   VAL;
			public long     POCVolume;

			public double   TickSize;
			public int      TicksPerLevel;

			// PERF: cache del max TpoPeriods.Count entre Levels (modo Compact).
			// Evita escaneo lineal de Levels por profile por frame para encontrar el max.
			// Invalidado (= -1) cuando se agrega un TPO period a algún level.
			public int      CachedMaxTpoCount = -1;

			// === Anchored VWAP State ===
			public double HighVwapPV;
			public double HighVwapVol;
			public int    HighVwapAnchorBar = -1;
			public double SessionHigh = double.MinValue;
			public bool   HighJustReset;

			public double LowVwapPV;
			public double LowVwapVol;
			public int    LowVwapAnchorBar = -1;
			public double SessionLow = double.MaxValue;
			public bool   LowJustReset;

			public Dictionary<int, double> HighVwapValues;
			public Dictionary<int, double> LowVwapValues;
			public List<VwapSegment> ArchivedHighVwaps;
			public List<VwapSegment> ArchivedLowVwaps;

			public long PriceToKey(double price)
			{
				return (long)Math.Round(price / (TickSize * TicksPerLevel));
			}

			public double KeyToPrice(long key)
			{
				return key * TickSize * TicksPerLevel;
			}
		}

		#endregion

		#region Private Fields

		private List<VolumeProfileSession> _allProfiles;
		private VolumeProfileSession       _activeProfile;
		private TimeSpan                   _profileStartTs;
		private TimeSpan                   _profileEndTs;
		private bool                       _tickReplayWarned;
		private DateTime                   _lastSessionDate;
		private SessionIterator            _sessionIterator;
		private DateTime                   _currentSessionEnd = DateTime.MinValue;
		private int                        _lastRealBar;      // último CurrentBar real procesado en OnBarUpdate
		// _tpoViewMode ahora se respalda contra una property publica (TpoView) que serializa
		// en el chart template. Asi el modo elegido (Compact/Extended/Histogram) sobrevive
		// F5 / reload / restart de NT.
		private TpoViewMode                _tpoViewMode = TpoViewMode.Compact;
		private bool                       _isLicensed;
		private string                     _licenseMessage;

		#endregion

		#region OnStateChange

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = string.Format("RelativeVolumeProfile v{0}: Volume Profile / TPO Profile intraday con POC, VAH, VAL.", VERSION);
				Name = "RelativeVolumeProfile";
				Calculate = Calculate.OnEachTick;
				IsOverlay = true;
				DisplayInDataBox = false;
				DrawOnPricePanel = true;
				IsSuspendedWhileInactive = false; // v1.0.1: debe seguir corriendo aunque el chart no esté activo para que MCP Registry reciba actualizaciones
				BarsRequiredToPlot = 20;

				// 00. License
				LicenseKey            = "";

				// 01. Profile Period
				SessionMode           = ProfileSessionMode.RTH;
				ProfileStartTime      = "09:30";
				ProfileEndTime        = "16:00";
				DataMode              = VolumeDataMode.BarBased;
				ProfileType           = ProfileDataType.Volume;
				BarBasedPeriod        = 1;
				TicksPerLevel         = 1;
				ValueAreaPercent      = 70;
				TpoPeriodMinutes      = 30;
				TpoView               = TpoViewMode.Compact;

				// 06. NADRO Auto-Merge
				AutoMergeNadroEnabled       = true;
				AutoMergeOverlapThreshold   = 0.40;  // 40% (con D-shape gate adicional, mas refinado)
				AutoMergeBreakoutTolerance  = 0.5;   // 0.5 pts tolerance breakout
				NadroRequireDShape          = true;  // gate de calidad: solo merge si forma D-shape

				// 02. Histogram Visuals
				HistogramMaxWidth     = 50;
				HistogramSideParam    = HistogramSide.Right;
				HistogramOpacity      = 40;
				POCColor              = Brushes.Yellow;
				ValueAreaColor        = Brushes.DodgerBlue;
				OutsideVAColor        = Brushes.Gray;

				// 03. Key Level Lines
				ShowPOCLine           = true;
				ShowVALines           = true;
				ExtendLines           = false;
				ExtendLineThickness   = 2.0;
				TouchedLineColor      = Brushes.Gray;
				POCLineColor          = Brushes.Yellow;
				VALineColor           = Brushes.DodgerBlue;

				// 04. History & Debug
				ShowHistoricalProfiles = true;
				// PERF: con TpoViewMode.Histogram (auto-activo cuando letras < 6pt), 100 profiles
				// renderizan rapido sin necesidad de simplificar. Si tenes lag, baja este valor.
				MaxFullDetailProfiles = 100;
				ShowDebugLogs         = false;

				// 05. Anchored VWAP
				ShowAnchoredVWAP      = false;
				VwapMethod            = RvpVwapPriceMethod.Typical;
				HighVwapColor         = Brushes.Coral;
				LowVwapColor          = Brushes.CornflowerBlue;
				ArchivedVwapColor     = Brushes.Gray;
				VwapLineThickness     = 2.0;
			}
			else if (State == State.Configure)
			{
				// TPO siempre necesita serie de minutos para calcular periodos de tiempo
				if (DataMode == VolumeDataMode.BarBased || ProfileType == ProfileDataType.TPO)
					AddDataSeries(BarsPeriodType.Minute, BarBasedPeriod);
			}
			else if (State == State.DataLoaded)
			{
				// License validation — DESACTIVADA para uso interno/desarrollo.
				// Para re-habilitar: reemplazar este bloque con el check de LicenseClient.
				_isLicensed = true;
				_licenseMessage = "Licencia desactivada (uso interno)";

				_allProfiles = new List<VolumeProfileSession>();
				_activeProfile = null;
				_tickReplayWarned = false;
				_lastSessionDate = DateTime.MinValue;
				_sessionIterator = new SessionIterator(Bars);
				_compositesRestored = false;

				if (SessionMode == ProfileSessionMode.Custom || SessionMode == ProfileSessionMode.RTH)
				{
					if (!TimeSpan.TryParse(ProfileStartTime, out _profileStartTs))
						_profileStartTs = new TimeSpan(9, 30, 0);
					if (!TimeSpan.TryParse(ProfileEndTime, out _profileEndTs))
						_profileEndTs = new TimeSpan(16, 0, 0);
				}
				else if (SessionMode == ProfileSessionMode.PitAuto)
				{
					ApplyPitAutoSession();
				}

				if (ShowDebugLogs)
					Print("RelativeVolumeProfile v" + VERSION + " loaded for " + Instrument.FullName
						+ " | SessionMode: " + SessionMode
						+ (SessionMode != ProfileSessionMode.ETH ? " | Period: " + _profileStartTs + " - " + _profileEndTs : "")
						+ " | DataMode: " + DataMode);

				// Setup context menu on UI thread (pattern from PATSToolBar.cs)
				if (ChartControl != null)
					ChartControl.Dispatcher.InvokeAsync((Action)(() => SetupContextMenu()));
			}
			else if (State == State.Terminated)
			{
				if (ChartControl != null)
					ChartControl.Dispatcher.InvokeAsync((Action)(() => CleanupContextMenu()));
				DisposeCachedBrushes();
			}
		}

		#endregion

		#region OnBarUpdate

		protected override void OnBarUpdate()
		{
			// PERF: bloque MCP duplicado eliminado. El segundo bloque (líneas ~290+)
			// hace lo mismo, mejor y throttled. Antes: 2× Publish por tick × 50 ticks/s
			// = 100 Dictionary allocations/s por chart × 7 charts = 700/s. Ahora: solo
			// 1× por bar cerrado en Realtime.

			if (!_isLicensed) return;

			if (BarsInProgress == 0)
			{
				// Serie primaria: detección de límites de sesión y recálculo de niveles
				if (CurrentBar < BarsRequiredToPlot) return;

				_lastRealBar = CurrentBar;

				CheckProfileBoundaries();

				// NADRO Auto-Merge: re-evaluar al cierre de cada nueva sesion (cheap, throttled
				// internamente por _lastNadroBuildClosedCount).
				NadroAutoMergeTick();

				// Anchored VWAP: detectar nuevos extremos de sesión
				if (ShowAnchoredVWAP && _activeProfile != null && _activeProfile.IsActive
					&& _activeProfile.HighVwapValues != null)
					CheckVwapAnchors(_activeProfile);

				// En modo TickReplay, el volumen se acumula via OnMarketData (no necesita serie secundaria)
				if (_activeProfile != null && IsFirstTickOfBar)
					RecalculateKeyLevels(_activeProfile);

				// --- RelativeMCP observability ---
				// PERF: throttled a IsFirstTickOfBar. Antes corría cada tick (50/s × 7 charts).
				if (State == State.Realtime && IsFirstTickOfBar)
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

						RelativeIndicatorRegistry.Publish(
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
							});

						this.RLog("bar={0} close={1:F2} POC={2:F2} VAH={3:F2} VAL={4:F2} levels={5} active={6} total_sessions={7}",
							CurrentBar, Close[0], poc, vah, val, levelCount, profActive, allProfCount);
					}
					catch { }
				}
				// --- end RelativeMCP ---
			}
			else if (BarsInProgress == 1 && (DataMode == VolumeDataMode.BarBased || ProfileType == ProfileDataType.TPO))
			{
				// Serie secundaria (1 min): distribuir volumen o TPO con resolución fina
				if (_activeProfile == null || !_activeProfile.IsActive) return;

				// En modo RTH / Custom / PitAuto, filtrar barras fuera del horario configurado
				if (SessionMode == ProfileSessionMode.RTH || SessionMode == ProfileSessionMode.Custom || SessionMode == ProfileSessionMode.PitAuto)
				{
					TimeSpan barTs = Times[1][0].TimeOfDay;
					bool inPeriod;
					if (_profileStartTs < _profileEndTs)
						inPeriod = barTs >= _profileStartTs && barTs < _profileEndTs;
					else
						inPeriod = barTs >= _profileStartTs || barTs < _profileEndTs;

					if (!inPeriod) return;
				}

				if (IsFirstTickOfBar)
				{
					if (ProfileType == ProfileDataType.TPO)
						DistributeTPO();
					else
						DistributeBarVolume();

					// Anchored VWAP: acumular con serie secundaria 1-min
					if (ShowAnchoredVWAP && _activeProfile.HighVwapValues != null)
						AccumulateVwapBarBased(_activeProfile);
				}
			}
		}

		#endregion

		#region OnMarketData

		protected override void OnMarketData(MarketDataEventArgs e)
		{
			if (e.MarketDataType != MarketDataType.Last) return;
			if (_activeProfile == null || !_activeProfile.IsActive) return;
			if (DataMode != VolumeDataMode.TickReplay) return;

			if (ProfileType != ProfileDataType.TPO)
				AccumulateTickVolume(e.Price, e.Volume);

			// Anchored VWAP: acumular tick a tick
			if (ShowAnchoredVWAP && _activeProfile.HighVwapValues != null)
				AccumulateVwapTick(_activeProfile, e.Price, e.Volume);
		}

		#endregion

		#region Profile Boundary Detection

		private void CheckProfileBoundaries()
		{
			bool startNewProfile = false;

			if (SessionMode == ProfileSessionMode.RTH || SessionMode == ProfileSessionMode.Custom || SessionMode == ProfileSessionMode.PitAuto)
			{
				// RTH / Custom / PitAuto: perfil basado en horas configuradas
				// (PitAuto resuelve ProfileStartTime/EndTime a partir del MasterInstrument)
				TimeSpan currentTs = Time[0].TimeOfDay;

				bool insidePeriod;
				if (_profileStartTs < _profileEndTs)
					insidePeriod = currentTs >= _profileStartTs && currentTs < _profileEndTs;
				else
					insidePeriod = currentTs >= _profileStartTs || currentTs < _profileEndTs;

				bool prevInsidePeriod = false;
				if (CurrentBar > 0)
				{
					TimeSpan prevTs = Time[1].TimeOfDay;
					if (_profileStartTs < _profileEndTs)
						prevInsidePeriod = prevTs >= _profileStartTs && prevTs < _profileEndTs;
					else
						prevInsidePeriod = prevTs >= _profileStartTs || prevTs < _profileEndTs;
				}

				bool crossedStart = insidePeriod && !prevInsidePeriod;

				if (_activeProfile == null && insidePeriod && CurrentBar == BarsRequiredToPlot)
					crossedStart = true;

				if (crossedStart)
				{
					if (_activeProfile != null && _activeProfile.IsActive)
					{
						_activeProfile.IsActive = false;
						_activeProfile.EndBarIdx = CurrentBar - 1;
						_activeProfile.EndTime = Time[0];
						RecalculateKeyLevels(_activeProfile);
					}

					StartNewProfile();
				}

				bool crossedEnd = !insidePeriod && prevInsidePeriod;
				if (crossedEnd && _activeProfile != null && _activeProfile.IsActive)
				{
					_activeProfile.IsActive = false;
					_activeProfile.EndBarIdx = CurrentBar;
					_activeProfile.EndTime = Time[0];
					RecalculateKeyLevels(_activeProfile);

					if (ShowDebugLogs)
						Print("RelativeVolumeProfile: " + SessionMode + " profile ended at " + Time[0]
							+ " | Levels: " + _activeProfile.Levels.Count
							+ " | POC: " + _activeProfile.POC
							+ " | VAH: " + _activeProfile.VAH
							+ " | VAL: " + _activeProfile.VAL);
				}

				if (_activeProfile != null && _activeProfile.IsActive)
					_activeProfile.EndBarIdx = CurrentBar;
			}
			else if (SessionMode == ProfileSessionMode.ETH)
			{
				// ETH: perfil por día de trading completo
				DateTime tradingDate = _sessionIterator.GetTradingDay(Time[0]);
				if (tradingDate.Date != _lastSessionDate.Date)
				{
					startNewProfile = true;
					_lastSessionDate = tradingDate;
				}

				// First bar edge case
				if (_activeProfile == null && CurrentBar == BarsRequiredToPlot)
					startNewProfile = true;

				if (startNewProfile)
				{
					if (_activeProfile != null && _activeProfile.IsActive)
					{
						_activeProfile.IsActive = false;
						_activeProfile.EndBarIdx = CurrentBar - 1;
						_activeProfile.EndTime = Time[0];
						RecalculateKeyLevels(_activeProfile);
					}

					StartNewProfile();

					// Cache session end for detecting ETH day end
					_sessionIterator.GetNextSession(Time[0], true);
					_currentSessionEnd = _sessionIterator.ActualSessionEnd;
				}

				// Detect session end
				if (_activeProfile != null && _activeProfile.IsActive
					&& _currentSessionEnd > DateTime.MinValue
					&& Time[0] >= _currentSessionEnd)
				{
					_activeProfile.IsActive = false;
					_activeProfile.EndBarIdx = CurrentBar;
					_activeProfile.EndTime = Time[0];
					RecalculateKeyLevels(_activeProfile);

					if (ShowDebugLogs)
						Print("RelativeVolumeProfile: ETH profile ended at " + Time[0]
							+ " | Levels: " + _activeProfile.Levels.Count
							+ " | POC: " + _activeProfile.POC);
				}

				if (_activeProfile != null && _activeProfile.IsActive)
					_activeProfile.EndBarIdx = CurrentBar;
			}
			// Nota: el antiguo modo Custom ahora se maneja junto con RTH arriba
		}

		private void StartNewProfile()
		{
			_activeProfile = new VolumeProfileSession
			{
				StartTime        = Time[0],
				StartBarIdx      = CurrentBar,
				EndBarIdx        = -1,
				LastVolumeBarIdx = CurrentBar,
				IsActive         = true,
				Levels           = new Dictionary<long, VolumeLevelData>(),
				TotalVolume      = 0,
				TickSize         = TickSize,
				TicksPerLevel    = this.TicksPerLevel,
				TpoPeriodBarMap  = ProfileType == ProfileDataType.TPO ? new Dictionary<int, int>() : null,
				// Anchored VWAP init
				HighVwapAnchorBar = -1,
				LowVwapAnchorBar  = -1,
				SessionHigh       = double.MinValue,
				SessionLow        = double.MaxValue,
				HighVwapValues    = ShowAnchoredVWAP ? new Dictionary<int, double>() : null,
				LowVwapValues     = ShowAnchoredVWAP ? new Dictionary<int, double>() : null,
				ArchivedHighVwaps = ShowAnchoredVWAP ? new List<VwapSegment>() : null,
				ArchivedLowVwaps  = ShowAnchoredVWAP ? new List<VwapSegment>() : null
			};
			_allProfiles.Add(_activeProfile);

			if (ShowDebugLogs)
				Print("RelativeVolumeProfile: New profile [" + SessionMode + "] started at " + Time[0] + " bar " + CurrentBar);
		}

		#endregion

		#region PitAuto — auto-detección de pit session por instrumento

		/// <summary>
		/// Setea _profileStartTs y _profileEndTs según el MasterInstrument del chart.
		/// Se llama en State.DataLoaded. Si el usuario cambia el instrumento en el chart,
		/// NT8 recicla el indicador (Terminated → Configure → DataLoaded), por lo que
		/// esta función se re-ejecuta automáticamente.
		/// Horarios referenciados al huso ET (timezone nativo del exchange).
		/// </summary>
		private void ApplyPitAutoSession()
		{
			string master = "";
			try { master = Instrument?.MasterInstrument?.Name?.ToUpperInvariant() ?? ""; }
			catch { master = ""; }

			TimeSpan startTs;
			TimeSpan endTs;
			string family;

			// CME Equity Index Futures (pit: 09:30 – 16:00 ET)
			if (master == "ES" || master == "MES" ||
			    master == "NQ" || master == "MNQ" ||
			    master == "YM" || master == "MYM" ||
			    master == "RTY" || master == "M2K")
			{
				startTs = new TimeSpan(9, 30, 0);
				endTs   = new TimeSpan(16, 0, 0);
				family  = "CME Index";
			}
			// COMEX Metals: Gold/Silver/Platinum (pit: 08:20 – 13:30 ET)
			else if (master == "GC" || master == "MGC" ||
			         master == "SI" || master == "SIL" ||
			         master == "PL")
			{
				startTs = new TimeSpan(8, 20, 0);
				endTs   = new TimeSpan(13, 30, 0);
				family  = "COMEX Metals";
			}
			// COMEX Copper (pit: 08:10 – 13:00 ET)
			else if (master == "HG" || master == "MHG")
			{
				startTs = new TimeSpan(8, 10, 0);
				endTs   = new TimeSpan(13, 0, 0);
				family  = "COMEX Copper";
			}
			// NYMEX Energy: Crude / Gasoline / Heating Oil / NatGas (pit: 09:00 – 14:30 ET)
			else if (master == "CL" || master == "MCL" ||
			         master == "QM" ||
			         master == "NG" || master == "QG" ||
			         master == "RB" || master == "HO")
			{
				startTs = new TimeSpan(9, 0, 0);
				endTs   = new TimeSpan(14, 30, 0);
				family  = "NYMEX Energy";
			}
			// CBOT Treasuries (pit: 07:20 – 14:00 ET)
			else if (master == "ZB" || master == "UB" ||
			         master == "ZN" || master == "TN" ||
			         master == "ZF" || master == "ZT")
			{
				startTs = new TimeSpan(7, 20, 0);
				endTs   = new TimeSpan(14, 0, 0);
				family  = "CBOT Treasuries";
			}
			// CBOT Grains (pit: 08:30 – 13:20 ET)
			else if (master == "ZC" || master == "ZS" || master == "ZW" ||
			         master == "ZM" || master == "ZL" || master == "ZO" || master == "ZR")
			{
				startTs = new TimeSpan(8, 30, 0);
				endTs   = new TimeSpan(13, 20, 0);
				family  = "CBOT Grains";
			}
			// CME FX Futures (aprox pit: 07:20 – 14:00 ET)
			else if (master == "6E" || master == "6B" || master == "6J" ||
			         master == "6C" || master == "6A" || master == "6S" ||
			         master == "6N" || master == "6M")
			{
				startTs = new TimeSpan(7, 20, 0);
				endTs   = new TimeSpan(14, 0, 0);
				family  = "CME FX";
			}
			// Fallback: si no se reconoce, usar ProfileStartTime/EndTime manuales
			// (para que el usuario pueda operar instrumentos exóticos con Custom times)
			else
			{
				if (!TimeSpan.TryParse(ProfileStartTime, out startTs))
					startTs = new TimeSpan(9, 30, 0);
				if (!TimeSpan.TryParse(ProfileEndTime, out endTs))
					endTs = new TimeSpan(16, 0, 0);
				family = "UNKNOWN → fallback manual";
			}

			_profileStartTs = startTs;
			_profileEndTs   = endTs;

			Print(string.Format(
				"RelativeVolumeProfile[PitAuto] {0} → family={1} pit={2:hh\\:mm}-{3:hh\\:mm} ET",
				master, family, _profileStartTs, _profileEndTs));
		}

		#endregion

		#region Volume Distribution (Bar-Based)

		private void DistributeBarVolume()
		{
			double high  = High[0];
			double low   = Low[0];
			long   vol   = (long)Volume[0];

			if (vol <= 0 || high <= low) return;

			double levelStep = TickSize * TicksPerLevel;
			double roundedLow  = Math.Floor(low / levelStep) * levelStep;
			double roundedHigh = Math.Ceiling(high / levelStep) * levelStep;

			int numLevels = (int)Math.Max(1, Math.Round((roundedHigh - roundedLow) / levelStep) + 1);

			// Uniform distribution: equal volume at each price level
			long volPerLevel = Math.Max(1, vol / numLevels);

			for (int i = 0; i < numLevels; i++)
			{
				double price = roundedLow + i * levelStep;

				long key = _activeProfile.PriceToKey(price);
				if (!_activeProfile.Levels.ContainsKey(key))
					_activeProfile.Levels[key] = new VolumeLevelData { Price = _activeProfile.KeyToPrice(key) };

				_activeProfile.Levels[key].Volume += volPerLevel;
				_activeProfile.TotalVolume += volPerLevel;
			}

			// Track last primary bar that received volume
			_activeProfile.LastVolumeBarIdx = CurrentBars[0];
		}

		#endregion

		#region TPO Distribution

		private void DistributeTPO()
		{
			double high = Highs[1][0];
			double low  = Lows[1][0];

			if (high <= low) return;

			// Determinar a qué periodo TPO pertenece esta barra (0=A, 1=B, 2=C...)
			DateTime barTime = Times[1][0];
			TimeSpan elapsed = barTime - _activeProfile.StartTime;
			int periodIndex = (int)(elapsed.TotalMinutes / TpoPeriodMinutes);
			if (periodIndex < 0) periodIndex = 0;

			// Registrar la primera barra del chart primario para este periodo (para vista extendida)
			if (_activeProfile.TpoPeriodBarMap != null && !_activeProfile.TpoPeriodBarMap.ContainsKey(periodIndex))
				_activeProfile.TpoPeriodBarMap[periodIndex] = CurrentBars[0];

			double levelStep = TickSize * TicksPerLevel;
			double roundedLow  = Math.Floor(low / levelStep) * levelStep;
			double roundedHigh = Math.Ceiling(high / levelStep) * levelStep;

			int numLevels = (int)Math.Max(1, Math.Round((roundedHigh - roundedLow) / levelStep) + 1);

			for (int i = 0; i < numLevels; i++)
			{
				double price = roundedLow + i * levelStep;
				long key = _activeProfile.PriceToKey(price);

				if (!_activeProfile.Levels.ContainsKey(key))
				{
					_activeProfile.Levels[key] = new VolumeLevelData
					{
						Price = _activeProfile.KeyToPrice(key),
						TpoPeriods = new HashSet<int>()
					};
				}

				var level = _activeProfile.Levels[key];

				// Inicializar HashSet si no existe (defensivo)
				if (level.TpoPeriods == null)
					level.TpoPeriods = new HashSet<int>();

				// Solo contar este periodo UNA VEZ por nivel de precio
				if (level.TpoPeriods.Add(periodIndex))
				{
					level.Volume += 1;
					_activeProfile.TotalVolume += 1;
					level.SortedPeriodsCache = null; // invalidar cache: hay nuevo periodo
					_activeProfile.CachedMaxTpoCount = -1; // invalidar el max cacheado
				}
			}

			_activeProfile.LastVolumeBarIdx = CurrentBars[0];
		}

		#endregion

		#region Volume Accumulation (Tick Replay)

		private void AccumulateTickVolume(double price, long volume)
		{
			if (volume <= 0) return;
			long vol = volume;

			long key = _activeProfile.PriceToKey(price);
			if (!_activeProfile.Levels.ContainsKey(key))
				_activeProfile.Levels[key] = new VolumeLevelData { Price = _activeProfile.KeyToPrice(key) };

			_activeProfile.Levels[key].Volume += vol;
			_activeProfile.TotalVolume += vol;

			// Track last primary bar that received volume
			_activeProfile.LastVolumeBarIdx = CurrentBar;
		}

		#endregion

		#region Value Area Calculation

		private void RecalculateKeyLevels(VolumeProfileSession profile)
		{
			if (profile == null || profile.Levels.Count == 0) return;

			// Find POC
			long maxVol = 0;
			long pocKey = 0;
			foreach (var kvp in profile.Levels)
			{
				if (kvp.Value.Volume > maxVol)
				{
					maxVol = kvp.Value.Volume;
					pocKey = kvp.Key;
				}
			}

			profile.POC = profile.KeyToPrice(pocKey);
			profile.POCVolume = maxVol;

			// Value Area Calculation: expand outward from POC
			List<long> sortedKeys = new List<long>(profile.Levels.Keys);
			sortedKeys.Sort();

			int pocIndex = sortedKeys.IndexOf(pocKey);
			if (pocIndex < 0)
			{
				profile.VAH = profile.POC;
				profile.VAL = profile.POC;
				return;
			}

			long targetVolume = (long)(profile.TotalVolume * ValueAreaPercent / 100.0);
			long cumVolume = profile.Levels[pocKey].Volume;

			int vaLowIdx = pocIndex;
			int vaHighIdx = pocIndex;

			while (cumVolume < targetVolume && (vaLowIdx > 0 || vaHighIdx < sortedKeys.Count - 1))
			{
				long volAbove = 0;
				long volBelow = 0;

				if (vaHighIdx < sortedKeys.Count - 1)
					volAbove = profile.Levels[sortedKeys[vaHighIdx + 1]].Volume;

				if (vaLowIdx > 0)
					volBelow = profile.Levels[sortedKeys[vaLowIdx - 1]].Volume;

				if (volAbove >= volBelow && vaHighIdx < sortedKeys.Count - 1)
				{
					vaHighIdx++;
					cumVolume += profile.Levels[sortedKeys[vaHighIdx]].Volume;
				}
				else if (vaLowIdx > 0)
				{
					vaLowIdx--;
					cumVolume += profile.Levels[sortedKeys[vaLowIdx]].Volume;
				}
				else if (vaHighIdx < sortedKeys.Count - 1)
				{
					vaHighIdx++;
					cumVolume += profile.Levels[sortedKeys[vaHighIdx]].Volume;
				}
				else
					break;
			}

			profile.VAH = profile.KeyToPrice(sortedKeys[vaHighIdx]);
			profile.VAL = profile.KeyToPrice(sortedKeys[vaLowIdx]);
		}

		#endregion

		#region Anchored VWAP Logic

		private double GetVwapPrice(int barsAgo)
		{
			switch (VwapMethod)
			{
				case RvpVwapPriceMethod.Typical:
					return (High[barsAgo] + Low[barsAgo] + Close[barsAgo]) / 3.0;
				case RvpVwapPriceMethod.OHLC4:
					return (Open[barsAgo] + High[barsAgo] + Low[barsAgo] + Close[barsAgo]) / 4.0;
				default:
					return Close[barsAgo];
			}
		}

		private double GetVwapPriceFromSeries(int seriesIdx)
		{
			switch (VwapMethod)
			{
				case RvpVwapPriceMethod.Typical:
					return (Highs[seriesIdx][0] + Lows[seriesIdx][0] + Closes[seriesIdx][0]) / 3.0;
				case RvpVwapPriceMethod.OHLC4:
					return (Opens[seriesIdx][0] + Highs[seriesIdx][0] + Lows[seriesIdx][0] + Closes[seriesIdx][0]) / 4.0;
				default:
					return Closes[seriesIdx][0];
			}
		}

		/// <summary>
		/// Detecta nuevos extremos de sesión y re-ancla VWAPs. Llamar desde BarsInProgress==0.
		/// </summary>
		private void CheckVwapAnchors(VolumeProfileSession profile)
		{
			if (profile == null || !profile.IsActive) return;

			double high = High[0];
			double low  = Low[0];
			double price = GetVwapPrice(0);
			double vol = Math.Max(1, Volume[0]);

			// Primera barra de sesión: inicializar extremos y anchors
			if (profile.SessionHigh == double.MinValue)
			{
				profile.SessionHigh = high;
				profile.SessionLow  = low;

				profile.HighVwapAnchorBar = CurrentBar;
				profile.HighVwapPV  = price * vol;
				profile.HighVwapVol = vol;
				profile.HighJustReset = true;

				profile.LowVwapAnchorBar = CurrentBar;
				profile.LowVwapPV  = price * vol;
				profile.LowVwapVol = vol;
				profile.LowJustReset = true;

				profile.HighVwapValues[CurrentBar] = price;
				profile.LowVwapValues[CurrentBar]  = price;
				return;
			}

			// Nuevo HIGH de sesión → archivar VWAP anterior, re-anclar
			if (high > profile.SessionHigh)
			{
				if (profile.HighVwapAnchorBar >= 0 && profile.HighVwapValues.Count > 0)
				{
					profile.ArchivedHighVwaps.Add(new VwapSegment
					{
						StartIdx = profile.HighVwapAnchorBar,
						EndIdx   = CurrentBar - 1,
						Values   = new Dictionary<int, double>(profile.HighVwapValues)
					});
				}

				profile.SessionHigh = high;
				profile.HighVwapAnchorBar = CurrentBar;
				profile.HighVwapPV  = price * vol;
				profile.HighVwapVol = vol;
				profile.HighJustReset = true;
				profile.HighVwapValues.Clear();
				profile.HighVwapValues[CurrentBar] = price;
			}

			// Nuevo LOW de sesión → archivar VWAP anterior, re-anclar
			if (low < profile.SessionLow)
			{
				if (profile.LowVwapAnchorBar >= 0 && profile.LowVwapValues.Count > 0)
				{
					profile.ArchivedLowVwaps.Add(new VwapSegment
					{
						StartIdx = profile.LowVwapAnchorBar,
						EndIdx   = CurrentBar - 1,
						Values   = new Dictionary<int, double>(profile.LowVwapValues)
					});
				}

				profile.SessionLow = low;
				profile.LowVwapAnchorBar = CurrentBar;
				profile.LowVwapPV  = price * vol;
				profile.LowVwapVol = vol;
				profile.LowJustReset = true;
				profile.LowVwapValues.Clear();
				profile.LowVwapValues[CurrentBar] = price;
			}
		}

		/// <summary>
		/// Acumula VWAP con datos de serie secundaria (BarBased mode). Llamar desde BarsInProgress==1.
		/// </summary>
		private void AccumulateVwapBarBased(VolumeProfileSession profile)
		{
			if (profile == null || !profile.IsActive) return;

			double price = GetVwapPriceFromSeries(1);
			double vol = Volumes[1][0];
			if (vol <= 0) return;

			// Acumular HIGH VWAP
			if (profile.HighVwapAnchorBar >= 0 && !profile.HighJustReset)
			{
				profile.HighVwapPV  += price * vol;
				profile.HighVwapVol += vol;
			}
			profile.HighJustReset = false;

			// Acumular LOW VWAP
			if (profile.LowVwapAnchorBar >= 0 && !profile.LowJustReset)
			{
				profile.LowVwapPV  += price * vol;
				profile.LowVwapVol += vol;
			}
			profile.LowJustReset = false;

			// Guardar valor VWAP actual en barra primaria
			int primaryBar = CurrentBars[0];
			if (profile.HighVwapVol > 0)
				profile.HighVwapValues[primaryBar] = profile.HighVwapPV / profile.HighVwapVol;
			if (profile.LowVwapVol > 0)
				profile.LowVwapValues[primaryBar] = profile.LowVwapPV / profile.LowVwapVol;
		}

		/// <summary>
		/// Acumula VWAP tick a tick (TickReplay mode). Llamar desde OnMarketData.
		/// </summary>
		private void AccumulateVwapTick(VolumeProfileSession profile, double price, long volume)
		{
			if (profile == null || !profile.IsActive || volume <= 0) return;

			double pv = price * volume;

			if (profile.HighVwapAnchorBar >= 0 && !profile.HighJustReset)
			{
				profile.HighVwapPV  += pv;
				profile.HighVwapVol += volume;
			}

			if (profile.LowVwapAnchorBar >= 0 && !profile.LowJustReset)
			{
				profile.LowVwapPV  += pv;
				profile.LowVwapVol += volume;
			}

			if (profile.HighVwapVol > 0)
				profile.HighVwapValues[CurrentBar] = profile.HighVwapPV / profile.HighVwapVol;
			if (profile.LowVwapVol > 0)
				profile.LowVwapValues[CurrentBar] = profile.LowVwapPV / profile.LowVwapVol;
		}

		#endregion

		#region Properties

		// === 00. License ===

		[NinjaScriptProperty]
		[Display(Name = "License Key", Description = "Enter your license key to activate the indicator.", GroupName = "00. Licencia", Order = 0)]
		public string LicenseKey { get; set; }

		// === 01. Profile Period ===

		[NinjaScriptProperty]
		[Display(Name = "Session Mode", Description = "RTH = Regular Trading Hours, ETH = Extended (full day), Custom = horario manual", GroupName = "01. Profile Period", Order = 0)]
		public ProfileSessionMode SessionMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Profile Start Time", Description = "Hora de inicio del perfil (formato HH:mm). Aplica en modos RTH y Custom.", GroupName = "01. Profile Period", Order = 1)]
		public string ProfileStartTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Profile End Time", Description = "Hora de fin del perfil (formato HH:mm). Aplica en modos RTH y Custom.", GroupName = "01. Profile Period", Order = 2)]
		public string ProfileEndTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Data Mode", Description = "BarBased (aproximado) o TickReplay (exacto, requiere Tick Replay habilitado)", GroupName = "01. Profile Period", Order = 3)]
		public VolumeDataMode DataMode { get; set; }

		[NinjaScriptProperty]
		[Range(1, 60)]
		[Display(Name = "BarBased Period (min)", Description = "Periodo en minutos de la serie interna para modo BarBased (default 1). Menor = más precisión, más datos.", GroupName = "01. Profile Period", Order = 4)]
		public int BarBasedPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Ticks Per Level", Description = "Agrupacion de precios: 1 = cada tick, 2 = cada 2 ticks, etc.", GroupName = "01. Profile Period", Order = 5)]
		public int TicksPerLevel { get; set; }

		[NinjaScriptProperty]
		[Range(50, 100)]
		[Display(Name = "Value Area %", Description = "Porcentaje de volumen para el Value Area (default 70)", GroupName = "01. Profile Period", Order = 6)]
		public int ValueAreaPercent { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Profile Type", Description = "Volume = perfil de volumen tradicional. TPO = Time Price Opportunity (tiempo en cada nivel).", GroupName = "01. Profile Period", Order = 7)]
		public ProfileDataType ProfileType { get; set; }

		[NinjaScriptProperty]
		[Range(5, 120)]
		[Display(Name = "TPO Period (min)", Description = "Duracion de cada periodo TPO en minutos (default 30). Cada periodo = una letra A, B, C...", GroupName = "01. Profile Period", Order = 8)]
		public int TpoPeriodMinutes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "TPO View Mode", Description = "Compact = letras stackeadas. Extended = letras por barra. Histogram = barras (mas rapido en zoom out).", GroupName = "01. Profile Period", Order = 9)]
		public TpoViewMode TpoView
		{
			get { return _tpoViewMode; }
			set { _tpoViewMode = value; }
		}

		// === 06. NADRO Auto-Merge ===

		[NinjaScriptProperty]
		[Display(Name = "NADRO Auto-Merge", Description = "Fusiona automaticamente perfiles consecutivos cuando overlap VA >= threshold (regla NADRO). Los merges manuales del usuario se preservan.", GroupName = "06. NADRO Auto-Merge", Order = 1)]
		public bool AutoMergeNadroEnabled { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, 1.0)]
		[Display(Name = "Overlap Threshold", Description = "Fraccion minima overlap VA para mergear (default 0.5 = 50%).", GroupName = "06. NADRO Auto-Merge", Order = 2)]
		public double AutoMergeOverlapThreshold { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, 5.0)]
		[Display(Name = "Breakout Tolerance (pts)", Description = "Tolerancia en puntos para detectar breakout limpio (default 0.5).", GroupName = "06. NADRO Auto-Merge", Order = 3)]
		public double AutoMergeBreakoutTolerance { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require D-Shape", Description = "Solo mergear si el composite resultante forma D-shape (perfil balanceado/rotacional). Filtra falsos CVAs que en realidad son transition (P-shape o b-shape).", GroupName = "06. NADRO Auto-Merge", Order = 4)]
		public bool NadroRequireDShape { get; set; }

		// === 02. Histogram Visuals ===

		[NinjaScriptProperty]
		[Range(5, 100)]
		[Display(Name = "Histogram Width %", Description = "Ancho del histograma como porcentaje del ancho de la sesion (5-100%)", GroupName = "02. Histogram Visuals", Order = 1)]
		public int HistogramMaxWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Histogram Side", Description = "Dibujar histograma a la derecha o izquierda del perfil", GroupName = "02. Histogram Visuals", Order = 2)]
		public HistogramSide HistogramSideParam { get; set; }

		[NinjaScriptProperty]
		[Range(5, 100)]
		[Display(Name = "Histogram Opacity", Description = "Transparencia del histograma (5-100)", GroupName = "02. Histogram Visuals", Order = 3)]
		public int HistogramOpacity { get; set; }

		[XmlIgnore]
		[Display(Name = "POC Color", Description = "Color de la barra POC en el histograma", GroupName = "02. Histogram Visuals", Order = 4)]
		public System.Windows.Media.Brush POCColor { get; set; }
		[Browsable(false)]
		public string POCColorSerializable
		{
			get { return Serialize.BrushToString(POCColor); }
			set { POCColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Value Area Color", Description = "Color de las barras dentro del Value Area", GroupName = "02. Histogram Visuals", Order = 5)]
		public System.Windows.Media.Brush ValueAreaColor { get; set; }
		[Browsable(false)]
		public string ValueAreaColorSerializable
		{
			get { return Serialize.BrushToString(ValueAreaColor); }
			set { ValueAreaColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Outside VA Color", Description = "Color de las barras fuera del Value Area", GroupName = "02. Histogram Visuals", Order = 6)]
		public System.Windows.Media.Brush OutsideVAColor { get; set; }
		[Browsable(false)]
		public string OutsideVAColorSerializable
		{
			get { return Serialize.BrushToString(OutsideVAColor); }
			set { OutsideVAColor = Serialize.StringToBrush(value); }
		}

		// === 03. Key Level Lines ===

		[NinjaScriptProperty]
		[Display(Name = "Show POC Line", Description = "Mostrar linea horizontal del POC", GroupName = "03. Key Level Lines", Order = 1)]
		public bool ShowPOCLine { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show VA Lines", Description = "Mostrar lineas VAH y VAL", GroupName = "03. Key Level Lines", Order = 2)]
		public bool ShowVALines { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Extend Lines", Description = "Extender lineas POC/VAH/VAL de perfiles historicos hasta donde el precio las toque", GroupName = "03. Key Level Lines", Order = 3)]
		public bool ExtendLines { get; set; }

		[Range(0.5, 5.0)]
		[Display(Name = "Extend Line Thickness", Description = "Grosor de las lineas extendidas", GroupName = "03. Key Level Lines", Order = 4)]
		public double ExtendLineThickness { get; set; }

		[XmlIgnore]
		[Display(Name = "Touched Line Color", Description = "Color del label cuando la linea fue tocada por el precio", GroupName = "03. Key Level Lines", Order = 5)]
		public System.Windows.Media.Brush TouchedLineColor { get; set; }
		[Browsable(false)]
		public string TouchedLineColorSerializable
		{
			get { return Serialize.BrushToString(TouchedLineColor); }
			set { TouchedLineColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "POC Line Color", Description = "Color de la linea POC", GroupName = "03. Key Level Lines", Order = 6)]
		public System.Windows.Media.Brush POCLineColor { get; set; }
		[Browsable(false)]
		public string POCLineColorSerializable
		{
			get { return Serialize.BrushToString(POCLineColor); }
			set { POCLineColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "VA Line Color", Description = "Color de las lineas VAH/VAL", GroupName = "03. Key Level Lines", Order = 7)]
		public System.Windows.Media.Brush VALineColor { get; set; }
		[Browsable(false)]
		public string VALineColorSerializable
		{
			get { return Serialize.BrushToString(VALineColor); }
			set { VALineColor = Serialize.StringToBrush(value); }
		}

		// === 04. History & Debug ===

		[NinjaScriptProperty]
		[Display(Name = "Show Historical Profiles", Description = "Mostrar perfiles de sesiones anteriores", GroupName = "04. History & Debug", Order = 1)]
		public bool ShowHistoricalProfiles { get; set; }

		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "Max Full Detail Profiles", Description = "Cantidad maxima de profiles que se renderizan con detalle completo (TPO letters / histograma). Los demas profiles visibles se renderizan simplificados (solo lineas VAH/VAL/POC). Reduce este valor si NT se traba con muchos dias visibles. Default 10.", GroupName = "04. History & Debug", Order = 2)]
		public int MaxFullDetailProfiles { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Debug Logs", Description = "Imprimir logs de debug en Output Window", GroupName = "04. History & Debug", Order = 3)]
		public bool ShowDebugLogs { get; set; }

		// === 05. Anchored VWAP ===

		[NinjaScriptProperty]
		[Display(Name = "Show Anchored VWAP", Description = "Mostrar VWAPs anclados al High/Low de cada sesión", GroupName = "05. Anchored VWAP", Order = 1)]
		public bool ShowAnchoredVWAP { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "VWAP Price Method", Description = "Método de precio para cálculo VWAP: Close, Typical (HLC/3), OHLC4", GroupName = "05. Anchored VWAP", Order = 2)]
		public RvpVwapPriceMethod VwapMethod { get; set; }

		[XmlIgnore]
		[Display(Name = "High VWAP Color", Description = "Color de la curva VWAP anclada al High", GroupName = "05. Anchored VWAP", Order = 3)]
		public System.Windows.Media.Brush HighVwapColor { get; set; }
		[Browsable(false)]
		public string HighVwapColorSerializable
		{
			get { return Serialize.BrushToString(HighVwapColor); }
			set { HighVwapColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Low VWAP Color", Description = "Color de la curva VWAP anclada al Low", GroupName = "05. Anchored VWAP", Order = 4)]
		public System.Windows.Media.Brush LowVwapColor { get; set; }
		[Browsable(false)]
		public string LowVwapColorSerializable
		{
			get { return Serialize.BrushToString(LowVwapColor); }
			set { LowVwapColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Archived VWAP Color", Description = "Color de segmentos VWAP históricos (anchors previos)", GroupName = "05. Anchored VWAP", Order = 5)]
		public System.Windows.Media.Brush ArchivedVwapColor { get; set; }
		[Browsable(false)]
		public string ArchivedVwapColorSerializable
		{
			get { return Serialize.BrushToString(ArchivedVwapColor); }
			set { ArchivedVwapColor = Serialize.StringToBrush(value); }
		}

		[Range(0.5, 5.0)]
		[Display(Name = "VWAP Line Thickness", Description = "Grosor de la línea VWAP activa", GroupName = "05. Anchored VWAP", Order = 6)]
		public double VwapLineThickness { get; set; }

		#endregion
	}
}

public enum HistogramSide
{
	Right,
	Left
}

public enum VolumeDataMode
{
	BarBased,
	TickReplay
}

public enum ProfileSessionMode
{
	RTH,
	ETH,
	Custom,
	PitAuto
}

public enum ProfileDataType
{
	Volume,
	TPO
}

public enum TpoViewMode
{
	Compact,
	Extended,
	Histogram  // PERF: barras horizontales en vez de letras (mucho mas rapido)
}

public enum RvpVwapPriceMethod
{
	Close,
	Typical,
	OHLC4
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RelativeVolumeProfile[] cacheRelativeVolumeProfile;
		public RelativeVolumeProfile RelativeVolumeProfile(string licenseKey, ProfileSessionMode sessionMode, string profileStartTime, string profileEndTime, VolumeDataMode dataMode, int barBasedPeriod, int ticksPerLevel, int valueAreaPercent, ProfileDataType profileType, int tpoPeriodMinutes, TpoViewMode tpoView, bool autoMergeNadroEnabled, double autoMergeOverlapThreshold, double autoMergeBreakoutTolerance, bool nadroRequireDShape, int histogramMaxWidth, HistogramSide histogramSideParam, int histogramOpacity, bool showPOCLine, bool showVALines, bool extendLines, bool showHistoricalProfiles, int maxFullDetailProfiles, bool showDebugLogs, bool showAnchoredVWAP, RvpVwapPriceMethod vwapMethod)
		{
			return RelativeVolumeProfile(Input, licenseKey, sessionMode, profileStartTime, profileEndTime, dataMode, barBasedPeriod, ticksPerLevel, valueAreaPercent, profileType, tpoPeriodMinutes, tpoView, autoMergeNadroEnabled, autoMergeOverlapThreshold, autoMergeBreakoutTolerance, nadroRequireDShape, histogramMaxWidth, histogramSideParam, histogramOpacity, showPOCLine, showVALines, extendLines, showHistoricalProfiles, maxFullDetailProfiles, showDebugLogs, showAnchoredVWAP, vwapMethod);
		}

		public RelativeVolumeProfile RelativeVolumeProfile(ISeries<double> input, string licenseKey, ProfileSessionMode sessionMode, string profileStartTime, string profileEndTime, VolumeDataMode dataMode, int barBasedPeriod, int ticksPerLevel, int valueAreaPercent, ProfileDataType profileType, int tpoPeriodMinutes, TpoViewMode tpoView, bool autoMergeNadroEnabled, double autoMergeOverlapThreshold, double autoMergeBreakoutTolerance, bool nadroRequireDShape, int histogramMaxWidth, HistogramSide histogramSideParam, int histogramOpacity, bool showPOCLine, bool showVALines, bool extendLines, bool showHistoricalProfiles, int maxFullDetailProfiles, bool showDebugLogs, bool showAnchoredVWAP, RvpVwapPriceMethod vwapMethod)
		{
			if (cacheRelativeVolumeProfile != null)
				for (int idx = 0; idx < cacheRelativeVolumeProfile.Length; idx++)
					if (cacheRelativeVolumeProfile[idx] != null && cacheRelativeVolumeProfile[idx].LicenseKey == licenseKey && cacheRelativeVolumeProfile[idx].SessionMode == sessionMode && cacheRelativeVolumeProfile[idx].ProfileStartTime == profileStartTime && cacheRelativeVolumeProfile[idx].ProfileEndTime == profileEndTime && cacheRelativeVolumeProfile[idx].DataMode == dataMode && cacheRelativeVolumeProfile[idx].BarBasedPeriod == barBasedPeriod && cacheRelativeVolumeProfile[idx].TicksPerLevel == ticksPerLevel && cacheRelativeVolumeProfile[idx].ValueAreaPercent == valueAreaPercent && cacheRelativeVolumeProfile[idx].ProfileType == profileType && cacheRelativeVolumeProfile[idx].TpoPeriodMinutes == tpoPeriodMinutes && cacheRelativeVolumeProfile[idx].TpoView == tpoView && cacheRelativeVolumeProfile[idx].AutoMergeNadroEnabled == autoMergeNadroEnabled && cacheRelativeVolumeProfile[idx].AutoMergeOverlapThreshold == autoMergeOverlapThreshold && cacheRelativeVolumeProfile[idx].AutoMergeBreakoutTolerance == autoMergeBreakoutTolerance && cacheRelativeVolumeProfile[idx].NadroRequireDShape == nadroRequireDShape && cacheRelativeVolumeProfile[idx].HistogramMaxWidth == histogramMaxWidth && cacheRelativeVolumeProfile[idx].HistogramSideParam == histogramSideParam && cacheRelativeVolumeProfile[idx].HistogramOpacity == histogramOpacity && cacheRelativeVolumeProfile[idx].ShowPOCLine == showPOCLine && cacheRelativeVolumeProfile[idx].ShowVALines == showVALines && cacheRelativeVolumeProfile[idx].ExtendLines == extendLines && cacheRelativeVolumeProfile[idx].ShowHistoricalProfiles == showHistoricalProfiles && cacheRelativeVolumeProfile[idx].MaxFullDetailProfiles == maxFullDetailProfiles && cacheRelativeVolumeProfile[idx].ShowDebugLogs == showDebugLogs && cacheRelativeVolumeProfile[idx].ShowAnchoredVWAP == showAnchoredVWAP && cacheRelativeVolumeProfile[idx].VwapMethod == vwapMethod && cacheRelativeVolumeProfile[idx].EqualsInput(input))
						return cacheRelativeVolumeProfile[idx];
			return CacheIndicator<RelativeVolumeProfile>(new RelativeVolumeProfile(){ LicenseKey = licenseKey, SessionMode = sessionMode, ProfileStartTime = profileStartTime, ProfileEndTime = profileEndTime, DataMode = dataMode, BarBasedPeriod = barBasedPeriod, TicksPerLevel = ticksPerLevel, ValueAreaPercent = valueAreaPercent, ProfileType = profileType, TpoPeriodMinutes = tpoPeriodMinutes, TpoView = tpoView, AutoMergeNadroEnabled = autoMergeNadroEnabled, AutoMergeOverlapThreshold = autoMergeOverlapThreshold, AutoMergeBreakoutTolerance = autoMergeBreakoutTolerance, NadroRequireDShape = nadroRequireDShape, HistogramMaxWidth = histogramMaxWidth, HistogramSideParam = histogramSideParam, HistogramOpacity = histogramOpacity, ShowPOCLine = showPOCLine, ShowVALines = showVALines, ExtendLines = extendLines, ShowHistoricalProfiles = showHistoricalProfiles, MaxFullDetailProfiles = maxFullDetailProfiles, ShowDebugLogs = showDebugLogs, ShowAnchoredVWAP = showAnchoredVWAP, VwapMethod = vwapMethod }, input, ref cacheRelativeVolumeProfile);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeVolumeProfile RelativeVolumeProfile(string licenseKey, ProfileSessionMode sessionMode, string profileStartTime, string profileEndTime, VolumeDataMode dataMode, int barBasedPeriod, int ticksPerLevel, int valueAreaPercent, ProfileDataType profileType, int tpoPeriodMinutes, TpoViewMode tpoView, bool autoMergeNadroEnabled, double autoMergeOverlapThreshold, double autoMergeBreakoutTolerance, bool nadroRequireDShape, int histogramMaxWidth, HistogramSide histogramSideParam, int histogramOpacity, bool showPOCLine, bool showVALines, bool extendLines, bool showHistoricalProfiles, int maxFullDetailProfiles, bool showDebugLogs, bool showAnchoredVWAP, RvpVwapPriceMethod vwapMethod)
		{
			return indicator.RelativeVolumeProfile(Input, licenseKey, sessionMode, profileStartTime, profileEndTime, dataMode, barBasedPeriod, ticksPerLevel, valueAreaPercent, profileType, tpoPeriodMinutes, tpoView, autoMergeNadroEnabled, autoMergeOverlapThreshold, autoMergeBreakoutTolerance, nadroRequireDShape, histogramMaxWidth, histogramSideParam, histogramOpacity, showPOCLine, showVALines, extendLines, showHistoricalProfiles, maxFullDetailProfiles, showDebugLogs, showAnchoredVWAP, vwapMethod);
		}

		public Indicators.RelativeVolumeProfile RelativeVolumeProfile(ISeries<double> input , string licenseKey, ProfileSessionMode sessionMode, string profileStartTime, string profileEndTime, VolumeDataMode dataMode, int barBasedPeriod, int ticksPerLevel, int valueAreaPercent, ProfileDataType profileType, int tpoPeriodMinutes, TpoViewMode tpoView, bool autoMergeNadroEnabled, double autoMergeOverlapThreshold, double autoMergeBreakoutTolerance, bool nadroRequireDShape, int histogramMaxWidth, HistogramSide histogramSideParam, int histogramOpacity, bool showPOCLine, bool showVALines, bool extendLines, bool showHistoricalProfiles, int maxFullDetailProfiles, bool showDebugLogs, bool showAnchoredVWAP, RvpVwapPriceMethod vwapMethod)
		{
			return indicator.RelativeVolumeProfile(input, licenseKey, sessionMode, profileStartTime, profileEndTime, dataMode, barBasedPeriod, ticksPerLevel, valueAreaPercent, profileType, tpoPeriodMinutes, tpoView, autoMergeNadroEnabled, autoMergeOverlapThreshold, autoMergeBreakoutTolerance, nadroRequireDShape, histogramMaxWidth, histogramSideParam, histogramOpacity, showPOCLine, showVALines, extendLines, showHistoricalProfiles, maxFullDetailProfiles, showDebugLogs, showAnchoredVWAP, vwapMethod);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeVolumeProfile RelativeVolumeProfile(string licenseKey, ProfileSessionMode sessionMode, string profileStartTime, string profileEndTime, VolumeDataMode dataMode, int barBasedPeriod, int ticksPerLevel, int valueAreaPercent, ProfileDataType profileType, int tpoPeriodMinutes, TpoViewMode tpoView, bool autoMergeNadroEnabled, double autoMergeOverlapThreshold, double autoMergeBreakoutTolerance, bool nadroRequireDShape, int histogramMaxWidth, HistogramSide histogramSideParam, int histogramOpacity, bool showPOCLine, bool showVALines, bool extendLines, bool showHistoricalProfiles, int maxFullDetailProfiles, bool showDebugLogs, bool showAnchoredVWAP, RvpVwapPriceMethod vwapMethod)
		{
			return indicator.RelativeVolumeProfile(Input, licenseKey, sessionMode, profileStartTime, profileEndTime, dataMode, barBasedPeriod, ticksPerLevel, valueAreaPercent, profileType, tpoPeriodMinutes, tpoView, autoMergeNadroEnabled, autoMergeOverlapThreshold, autoMergeBreakoutTolerance, nadroRequireDShape, histogramMaxWidth, histogramSideParam, histogramOpacity, showPOCLine, showVALines, extendLines, showHistoricalProfiles, maxFullDetailProfiles, showDebugLogs, showAnchoredVWAP, vwapMethod);
		}

		public Indicators.RelativeVolumeProfile RelativeVolumeProfile(ISeries<double> input , string licenseKey, ProfileSessionMode sessionMode, string profileStartTime, string profileEndTime, VolumeDataMode dataMode, int barBasedPeriod, int ticksPerLevel, int valueAreaPercent, ProfileDataType profileType, int tpoPeriodMinutes, TpoViewMode tpoView, bool autoMergeNadroEnabled, double autoMergeOverlapThreshold, double autoMergeBreakoutTolerance, bool nadroRequireDShape, int histogramMaxWidth, HistogramSide histogramSideParam, int histogramOpacity, bool showPOCLine, bool showVALines, bool extendLines, bool showHistoricalProfiles, int maxFullDetailProfiles, bool showDebugLogs, bool showAnchoredVWAP, RvpVwapPriceMethod vwapMethod)
		{
			return indicator.RelativeVolumeProfile(input, licenseKey, sessionMode, profileStartTime, profileEndTime, dataMode, barBasedPeriod, ticksPerLevel, valueAreaPercent, profileType, tpoPeriodMinutes, tpoView, autoMergeNadroEnabled, autoMergeOverlapThreshold, autoMergeBreakoutTolerance, nadroRequireDShape, histogramMaxWidth, histogramSideParam, histogramOpacity, showPOCLine, showVALines, extendLines, showHistoricalProfiles, maxFullDetailProfiles, showDebugLogs, showAnchoredVWAP, vwapMethod);
		}
	}
}

#endregion
