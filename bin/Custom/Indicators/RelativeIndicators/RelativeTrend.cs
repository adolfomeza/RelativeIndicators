#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators.RelativeIndicators;
using NinjaTrader.NinjaScript.AddOns; // RelativeMCP — RLog + Registry
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
	public enum TrendVwapPriceMethod
	{
		Close,
		Typical, // (H+L+C)/3
		Weighted // (H+L+C+O)/4
	}

	public class RelativeTrend : Indicator
	{
		#region Variables
		
		private string versionLabel = "v1.0.0";
		
		// Session Data
		private List<SessionLevelInfo> asiaSessions = new List<SessionLevelInfo>();
		private List<SessionLevelInfo> europeSessions = new List<SessionLevelInfo>();
		private List<SessionLevelInfo> usSessions = new List<SessionLevelInfo>();
		
		// Active Session Tracking
		private SessionLevelInfo currentAsia;
		private SessionLevelInfo currentEurope;
		private SessionLevelInfo currentUS;
		
		// VWAP Anchoring
		private int sessionHighBarIdx = -1;
		private int sessionLowBarIdx = -1;
		private bool hasHighVWAP = false;
		private bool hasLowVWAP = false;
		
		// VWAP Calculation - High Anchor
		private double highCumulativePV = 0;
		private double highCumulativeVol = 0;
		private double _lastVol = 0;
		
		// VWAP Calculation - Low Anchor
		private double lowCumulativePV = 0;
		private double lowCumulativeVol = 0;
		
		// Historical VWAPs (for visualization of past sessions)
		private class HistoricalAnchor
		{
			public int StartIdx;
			public int EndIdx;
			public Dictionary<int, double> VwapValues = new Dictionary<int, double>();
		}
		private List<HistoricalAnchor> historicalHighs = new List<HistoricalAnchor>();
		private List<HistoricalAnchor> historicalLows = new List<HistoricalAnchor>();
		private HistoricalAnchor currentHighAnchorObj;
		private HistoricalAnchor currentLowAnchorObj;

		// Session Time Handling
		private TimeSpan _cachedAsiaStart, _cachedAsiaEnd;
		private TimeSpan _cachedEuropeStart, _cachedEuropeEnd;
		private TimeSpan _cachedUSStart, _cachedUSEnd;
		private DateTime _lastCacheDate = DateTime.MinValue;
		private TimeZoneInfo _nyTimeZone;
		
		// Rendering Resources
		private SharpDX.Direct2D1.SolidColorBrush _cachedHighVwapBrush;
		private SharpDX.Direct2D1.SolidColorBrush _cachedLowVwapBrush;
		private SharpDX.Direct2D1.SolidColorBrush _cachedHistoricalBrush;
		private SharpDX.Direct2D1.SolidColorBrush _cachedLabelBgBrush;
		private SharpDX.Direct2D1.StrokeStyle _cachedDashStyle;
		
		private SharpDX.DirectWrite.Factory dwFactory;
		private SharpDX.DirectWrite.TextFormat textFormat;

		private class LabelData
		{
			public string Text;
			public float DrawX;
			public float Y;
			public float Width;
			public Brush Brush;
			public DateTime Time;
		}
		private List<LabelData> labelQueue = new List<LabelData>();
		
		// Session Definitions
		public class SessionLevelInfo
		{
			public string Name;
			public bool IsActive;
			public int StartBarIdx;
			public double High;
			public double Low;
			public int HighBarIdx;
			public int LowBarIdx;
			public int HighBrokenBarIdx = -1; // When price breaks high
			public int LowBrokenBarIdx = -1;  // When price breaks low
			public int HighGhostEndIdx = -1;
			public int LowGhostEndIdx = -1;
			public DateTime SessionDate;
		}

		#endregion

		#region Properties

		[NinjaScriptProperty]
		[Display(Name="Version", Order=0, GroupName="0. Info")]
		[ReadOnly(true)]
		public string Version
		{
			get { return versionLabel; }
			set { }
		}

		[NinjaScriptProperty]
		[Display(Name="VWAP Price Source", Description="Price source for VWAP (Typical, Close, etc)", Order=1, GroupName="1. Settings")]
		public TrendVwapPriceMethod VwapMethod { get; set; }

		[NinjaScriptProperty]
		[Range(1, 365)]
		[Display(Name="Max History Days", Description="Days to calculate back", Order=2, GroupName="1. Settings")]
		public int MaxHistoryDays { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Use Exchange Time", Description="Sync times with NY Exchange Time", Order=3, GroupName="1. Settings")]
		public bool UseExchangeTime { get; set; }

		// --- Textures & Colors ---
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="High VWAP Color", Order=1, GroupName="2. Visuals")]
		public Brush HighVWAPColor { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Low VWAP Color", Order=2, GroupName="2. Visuals")]
		public Brush LowVWAPColor { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Asia Line Color", Order=10, GroupName="3. Sessions")]
		public Brush AsiaLineColor { get; set; }
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Asia Label Color", Order=11, GroupName="3. Sessions")]
		public Brush AsiaLabelColor { get; set; }
		[NinjaScriptProperty]
		[Display(Name="Show Asia High", Order=12, GroupName="3. Sessions")]
		public bool ShowAsiaHigh { get; set; }
		[NinjaScriptProperty]
		[Display(Name="Show Asia Low", Order=13, GroupName="3. Sessions")]
		public bool ShowAsiaLow { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Europe Line Color", Order=20, GroupName="3. Sessions")]
		public Brush EuropeLineColor { get; set; }
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Europe Label Color", Order=21, GroupName="3. Sessions")]
		public Brush EuropeLabelColor { get; set; }
		[NinjaScriptProperty]
		[Display(Name="Show Europe High", Order=22, GroupName="3. Sessions")]
		public bool ShowEuropeHigh { get; set; }
		[NinjaScriptProperty]
		[Display(Name="Show Europe Low", Order=23, GroupName="3. Sessions")]
		public bool ShowEuropeLow { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="US Line Color", Order=30, GroupName="3. Sessions")]
		public Brush USLineColor { get; set; }
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="US Label Color", Order=31, GroupName="3. Sessions")]
		public Brush USLabelColor { get; set; }
		[NinjaScriptProperty]
		[Display(Name="Show US High", Order=32, GroupName="3. Sessions")]
		public bool ShowUSHigh { get; set; }
		[NinjaScriptProperty]
		[Display(Name="Show US Low", Order=33, GroupName="3. Sessions")]
		public bool ShowUSLow { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Label Background Color", Order=40, GroupName="2. Visuals")]
		public Brush LabelBackgroundColor { get; set; }

		[Browsable(false)]
		public string AsiaLineColorSerialize { get { return Serialize.BrushToString(AsiaLineColor); } set { AsiaLineColor = Serialize.StringToBrush(value); } }
		[Browsable(false)]
		public string AsiaLabelColorSerialize { get { return Serialize.BrushToString(AsiaLabelColor); } set { AsiaLabelColor = Serialize.StringToBrush(value); } }
		[Browsable(false)]
		public string EuropeLineColorSerialize { get { return Serialize.BrushToString(EuropeLineColor); } set { EuropeLineColor = Serialize.StringToBrush(value); } }
		[Browsable(false)]
		public string EuropeLabelColorSerialize { get { return Serialize.BrushToString(EuropeLabelColor); } set { EuropeLabelColor = Serialize.StringToBrush(value); } }
		[Browsable(false)]
		public string USLineColorSerialize { get { return Serialize.BrushToString(USLineColor); } set { USLineColor = Serialize.StringToBrush(value); } }
		[Browsable(false)]
		public string USLabelColorSerialize { get { return Serialize.BrushToString(USLabelColor); } set { USLabelColor = Serialize.StringToBrush(value); } }
		[Browsable(false)]
		public string LabelBackgroundColorSerialize { get { return Serialize.BrushToString(LabelBackgroundColor); } set { LabelBackgroundColor = Serialize.StringToBrush(value); } }

		[Browsable(false)]
		public string HighVWAPColorSerialize
		{
			get { return Serialize.BrushToString(HighVWAPColor); }
			set { HighVWAPColor = Serialize.StringToBrush(value); }
		}
		
		[Browsable(false)]
		public string LowVWAPColorSerialize
		{
			get { return Serialize.BrushToString(LowVWAPColor); }
			set { LowVWAPColor = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Historical VWAP Color", Order=3, GroupName="2. Visuals")]
		public Brush HistoricalVWAPColor { get; set; }

		// --- Sessions ---
		[NinjaScriptProperty]
		[Display(Name="Show Asia", Order=1, GroupName="3. Sessions")]
		public bool ShowAsia { get; set; }
		[NinjaScriptProperty]
		[Display(Name="Asia Start", Order=2, GroupName="3. Sessions")]
		public string AsiaStartTime { get; set; }
		[NinjaScriptProperty]
		[Display(Name="Asia End", Order=3, GroupName="3. Sessions")]
		public string AsiaEndTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Show Europe", Order=4, GroupName="3. Sessions")]
		public bool ShowEurope { get; set; }
		[NinjaScriptProperty]
		[Display(Name="Europe Start", Order=5, GroupName="3. Sessions")]
		public string EuropeStartTime { get; set; }
		[NinjaScriptProperty]
		[Display(Name="Europe End", Order=6, GroupName="3. Sessions")]
		public string EuropeEndTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Show US", Order=7, GroupName="3. Sessions")]
		public bool ShowUS { get; set; }
		[NinjaScriptProperty]
		[Display(Name="US Start", Order=8, GroupName="3. Sessions")]
		public string USStartTime { get; set; }
		[NinjaScriptProperty]
		[Display(Name="US End", Order=9, GroupName="3. Sessions")]
		public string USEndTime { get; set; }

		#endregion



		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Trend Following VWAP Implementation";
				Name										= "RelativeTrend";
				Calculate									= Calculate.OnEachTick;
				IsOverlay									= true;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= false;
				
				VwapMethod = TrendVwapPriceMethod.Typical;
				MaxHistoryDays = 30;
				UseExchangeTime = true;
				
				HighVWAPColor = Brushes.DodgerBlue;
				LowVWAPColor = Brushes.Crimson;
				HistoricalVWAPColor = Brushes.DarkGray;
				
				ShowAsia = true;
				AsiaStartTime = "18:00";
				AsiaEndTime = "02:00";
				AsiaLineColor = Brushes.DarkGray;
				AsiaLabelColor = Brushes.Silver;
				ShowAsiaHigh = true;
				ShowAsiaLow = true;
				
				ShowEurope = true;
				EuropeStartTime = "03:00";
				EuropeEndTime = "09:00";
				EuropeLineColor = Brushes.Gold;
				EuropeLabelColor = Brushes.Silver;
				ShowEuropeHigh = true;
				ShowEuropeLow = true;
				
				ShowUS = true;
				USStartTime = "09:30";
				USEndTime = "16:00";
				USLineColor = Brushes.CornflowerBlue; // Slightly different to distinguish
				USLabelColor = Brushes.White;
				ShowUSHigh = true;
				ShowUSLow = true;
				
				LabelBackgroundColor = Brushes.Black;
				
				// Plots restored for Values initialization, but transparent to rely on OnRender
				AddPlot(new Stroke(Brushes.Transparent, 2), PlotStyle.Line, "HighVWAP");
				AddPlot(new Stroke(Brushes.Transparent, 2), PlotStyle.Line, "LowVWAP");
			}
			else if (State == State.Configure)
			{
			}
			else if (State == State.DataLoaded)
			{
				ClearAll();
			}
		}

		protected override void OnBarUpdate()
		{
			// if (CurrentBar < 20) return; // Removed to allow initialization of Values and Timezone Cache from Bar 0
			
			// 1. Timezone Management
			if (UseExchangeTime && (CurrentBar == 0 || Bars.IsFirstBarOfSession))
			{
				RefreshTimezoneCache(Time[0].Date);
			}

			// 2. Manage Sessions (Detect Start/End)
			DateTime now = Time[0];
			
			UpdateSession(asiaSessions, "Asia", now, AsiaStartTime, AsiaEndTime, ShowAsia);
			UpdateSession(europeSessions, "Europe", now, EuropeStartTime, EuropeEndTime, ShowEurope);
			UpdateSession(usSessions, "USA", now, USStartTime, USEndTime, ShowUS);
			
			// 3. Check for Anchoring Events (Session High/Low Breaks)
			// In Trend Mode, we might anchor to the most recent significant session extreme
			// For now, we reuse the logic: if a session is Active, we track its H/L.
			
			ProcessAnchors();
			
			// 4. Accumulate Volume (Realtime/Historical Split)
			double price = VwapMethod == TrendVwapPriceMethod.Close ? Close[0] :
						   VwapMethod == TrendVwapPriceMethod.Typical ? (High[0] + Low[0] + Close[0]) / 3.0 :
						   (High[0] + Low[0] + Close[0] + Open[0]) / 4.0;
			double vol = Volume[0];
			double tickVol = vol;

			if (State == State.Realtime)
			{
				if (IsFirstTickOfBar) _lastVol = 0;
				tickVol = vol - _lastVol;
				if (tickVol < 0) tickVol = vol;
				_lastVol = vol;
			}
			
			// Accumulate for High VWAP
			if (hasHighVWAP && sessionHighBarIdx != -1)
			{
				if (CurrentBar == sessionHighBarIdx)
				{
				     // If just anchored, we might need to reset or handle the first tick
					 // RelativeVwap pattern: On new anchor, reset accumulators.
					 // Here ProcessAnchors resets them. But we need to add the initial volume.
					 // If this is the Anchor Bar:
					 // In Historical: we add full volume.
					 // In Realtime: we add tick volume.
					 // BUT ProcessAnchors reset them to 0. So we just add current tickVol.
					 highCumulativePV += price * tickVol;
					 highCumulativeVol += tickVol;
				}
				else
				{
					highCumulativePV += price * tickVol;
					highCumulativeVol += tickVol;
				}
			}

			// Accumulate for Low VWAP
			if (hasLowVWAP && sessionLowBarIdx != -1)
			{
				if (CurrentBar == sessionLowBarIdx)
				{
					lowCumulativePV += price * tickVol;
					lowCumulativeVol += tickVol;
				}
				else
				{
					lowCumulativePV += price * tickVol;
					lowCumulativeVol += tickVol;
				}
			}

			// 5. Calculate & Assign VWAP Values
			CalculateVwap(price);

			// 6. Update Historical Storage
			UpdateHistoricalStorage();

			// --- RelativeMCP observability ---
			if (CurrentBar >= 0)
			{
				try
				{
					double highVwap = Values[0].IsValidDataPointAt(CurrentBar) ? Values[0][0] : double.NaN;
					double lowVwap = Values[1].IsValidDataPointAt(CurrentBar) ? Values[1][0] : double.NaN;

					RelativeIndicatorRegistry.Publish(
						string.Format("{0}:{1}:{2}{3}", typeof(RelativeTrend).Name,
							Instrument.FullName, BarsPeriod.Value, BarsPeriod.BarsPeriodType),
						new Dictionary<string, object>
						{
							["bar"] = CurrentBar,
							["bar_time"] = Time[0],
							["close"] = Close[0],
							["high_vwap"] = highVwap,
							["low_vwap"] = lowVwap,
							["has_high_vwap"] = hasHighVWAP,
							["has_low_vwap"] = hasLowVWAP,
							["session_high_bar_idx"] = sessionHighBarIdx,
							["session_low_bar_idx"] = sessionLowBarIdx,
						});

					if (IsFirstTickOfBar && State == State.Realtime)
						this.RLog("bar={0} close={1:F2} highVwap={2:F2} lowVwap={3:F2} hasH={4} hasL={5}",
							CurrentBar, Close[0], highVwap, lowVwap, hasHighVWAP, hasLowVWAP);
				}
				catch { }
			}
			// --- end RelativeMCP ---
		}

		private void ClearAll()
		{
			asiaSessions.Clear();
			europeSessions.Clear();
			usSessions.Clear();
			
			historicalHighs.Clear();
			historicalLows.Clear();
			
			sessionHighBarIdx = -1;
			sessionLowBarIdx = -1;
			
			highCumulativePV = 0;
			highCumulativeVol = 0;
			lowCumulativePV = 0;
			lowCumulativeVol = 0;
		}

		#region Logic - Anchoring & VWAP

		private void ProcessAnchors()
		{
			// Logic to determine where to anchor the High/Low VWAPs
			// In RelativeVwap (Reversion), this was complex (Breaks, Detachment, etc.)
			// In RelativeTrend, we want to anchor to:
			// - The High of the Day (or last significant high) -> Low VWAP (Support)
			// - The Low of the Day (or last significant low) -> High VWAP (Resistance)
			
			// Simplified Logic for Trend Alpha:
			// Anchor LowVWAP to the Low of the current Session(s)
			// Anchor HighVWAP to the High of the current Session(s)
			
			// Find overall High/Low of all active sessions
			double currentHigh = double.MinValue;
			double currentLow = double.MaxValue;
			int currentHighBar = -1;
			int currentLowBar = -1;
			
			bool anyActive = false;
			
			Action<List<SessionLevelInfo>> check = (list) => {
				if (list != null && list.Count > 0) {
					var s = list.Last();
					if (s.IsActive) {
						if (s.High > currentHigh) { currentHigh = s.High; currentHighBar = s.HighBarIdx; }
						if (s.Low < currentLow) { currentLow = s.Low; currentLowBar = s.LowBarIdx; }
						anyActive = true;
					}
				}
			};
			
			if (ShowAsia) check(asiaSessions);
			if (ShowEurope) check(europeSessions);
			if (ShowUS) check(usSessions);
			
			if (anyActive)
			{
				// If we found a new extreme, re-anchor
				if (currentHighBar != -1 && currentHighBar != sessionHighBarIdx)
				{
					// New High Anchor
					ArchiveHighVwap(); // Store old one
					sessionHighBarIdx = currentHighBar;
					highCumulativePV = 0;
					highCumulativeVol = 0;
					hasHighVWAP = true;
				}
				
				if (currentLowBar != -1 && currentLowBar != sessionLowBarIdx)
				{
					// New Low Anchor
					ArchiveLowVwap(); // Store old one
					sessionLowBarIdx = currentLowBar;
					lowCumulativePV = 0;
					lowCumulativeVol = 0;
					hasLowVWAP = true;
				}
			}
			else
			{
				// No active session - Terminate any running VWAPs
				if (hasHighVWAP)
				{
					ArchiveHighVwap();
					hasHighVWAP = false;
					sessionHighBarIdx = -1;
				}
				
				if (hasLowVWAP)
				{
					ArchiveLowVwap();
					hasLowVWAP = false;
					sessionLowBarIdx = -1;
				}
			}
		}
		
		private void CalculateVwap(double currentPrice)
		{
			// High VWAP Display
			if (hasHighVWAP && sessionHighBarIdx != -1 && CurrentBar >= sessionHighBarIdx)
			{
				if (highCumulativeVol > 0)
					Values[0][0] = highCumulativePV / highCumulativeVol;
				else 
					Values[0][0] = High[0];
			}
			else
			{
				Values[0][0] = double.NaN;
			}
			
			// Low VWAP Display
			if (hasLowVWAP && sessionLowBarIdx != -1 && CurrentBar >= sessionLowBarIdx)
			{
				if (lowCumulativeVol > 0)
					Values[1][0] = lowCumulativePV / lowCumulativeVol;
				else 
					Values[1][0] = Low[0];
			}
			else
			{
				Values[1][0] = double.NaN;
			}
		}
		
		#endregion

		#region Historical Storage
		
		private void ArchiveHighVwap()
		{
			if (hasHighVWAP && sessionHighBarIdx != -1)
			{
				var hist = new HistoricalAnchor 
				{
					StartIdx = sessionHighBarIdx,
					EndIdx = CurrentBar - 1,
					VwapValues = CopyVwapValues(sessionHighBarIdx, CurrentBar - 1, 0)
				};
				historicalHighs.Add(hist);
			}
		}
		
		private void ArchiveLowVwap()
		{
			if (hasLowVWAP && sessionLowBarIdx != -1)
			{
				var hist = new HistoricalAnchor 
				{
					StartIdx = sessionLowBarIdx,
					EndIdx = CurrentBar - 1,
					VwapValues = CopyVwapValues(sessionLowBarIdx, CurrentBar - 1, 1)
				};
				historicalLows.Add(hist);
			}
		}
		
		private void UpdateHistoricalStorage()
		{
			// Check if we need to close out historical segments? 
			// No, Archiving handles creation. We just render them.
		}
		
		private Dictionary<int, double> CopyVwapValues(int startIdx, int endIdx, int seriesIdx)
		{
			var dict = new Dictionary<int, double>();
			if (Values == null || seriesIdx < 0 || seriesIdx >= Values.Length) return dict;
			
			for (int i = startIdx; i <= endIdx; i++)
			{
				double val = Values[seriesIdx].GetValueAt(i);
				if (!double.IsNaN(val)) dict[i] = val;
			}
			return dict;
		}

		#endregion
		
		#region Session Helpers
		
		private void UpdateSession(List<SessionLevelInfo> sessions, string name, DateTime time, string startStr, string endStr, bool isEnabled)
		{
			if (!isEnabled || sessions == null) return;

			TimeSpan startTime = GetTimeByZone(startStr);
			TimeSpan endTime = GetTimeByZone(endStr);
			TimeSpan currentTime = time.TimeOfDay;

			bool isInside = false;

			if (startTime == endTime) isInside = false;
			else if (startTime < endTime) isInside = currentTime >= startTime && currentTime < endTime;
			else isInside = currentTime >= startTime || currentTime < endTime;

			SessionLevelInfo currentSession = sessions.Count > 0 ? sessions.Last() : null;

			if (isInside)
			{
				DateTime sessionDate = time.Date;
				if (startTime > endTime && currentTime < endTime) sessionDate = time.Date.AddDays(-1);

				if (currentSession == null || !currentSession.IsActive || currentSession.SessionDate != sessionDate)
				{
					currentSession = new SessionLevelInfo
					{
						Name = name,
						IsActive = true,
						StartBarIdx = CurrentBar,
						High = High[0],
						Low = Low[0],
						HighBarIdx = CurrentBar,
						LowBarIdx = CurrentBar,
						SessionDate = sessionDate
					};
					sessions.Add(currentSession);
				}
				else
				{
					if (High[0] > currentSession.High)
					{
						currentSession.High = High[0];
						currentSession.HighBarIdx = CurrentBar;
					}
					if (Low[0] < currentSession.Low)
					{
						currentSession.Low = Low[0];
						currentSession.LowBarIdx = CurrentBar;
					}
				}
			}
			else
			{
				if (currentSession != null && currentSession.IsActive)
				{
					currentSession.IsActive = false;
				}
			}
		}
		
		private TimeSpan GetTimeByZone(string timeStr)
        {
            if (UseExchangeTime && _lastCacheDate != DateTime.MinValue && CurrentBarDate == _lastCacheDate)
            {
                if (timeStr == AsiaStartTime) return _cachedAsiaStart;
                if (timeStr == AsiaEndTime) return _cachedAsiaEnd;
                if (timeStr == EuropeStartTime) return _cachedEuropeStart;
                if (timeStr == EuropeEndTime) return _cachedEuropeEnd;
                if (timeStr == USStartTime) return _cachedUSStart;
                if (timeStr == USEndTime) return _cachedUSEnd;
            }

            return CalculateTime(timeStr, CurrentBarDate);
        }

        private void RefreshTimezoneCache(DateTime date)
        {
            if (!UseExchangeTime) return;

            _cachedAsiaStart = CalculateTime(AsiaStartTime, date);
            _cachedAsiaEnd = CalculateTime(AsiaEndTime, date);
            _cachedEuropeStart = CalculateTime(EuropeStartTime, date);
            _cachedEuropeEnd = CalculateTime(EuropeEndTime, date);
            _cachedUSStart = CalculateTime(USStartTime, date);
            _cachedUSEnd = CalculateTime(USEndTime, date);

            _lastCacheDate = date;
        }

        private TimeSpan CalculateTime(string timeStr, DateTime date)
        {
            DateTime dt;
            if (!DateTime.TryParse(timeStr, out dt)) return TimeSpan.Zero;

            if (!UseExchangeTime) return dt.TimeOfDay;

            if (_nyTimeZone == null)
            {
                try { _nyTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
                catch { _nyTimeZone = TimeZoneInfo.Local; }
            }

            try
            {
                DateTime nyTimeUnspec = date.Add(dt.TimeOfDay);
                DateTime utcTime = TimeZoneInfo.ConvertTimeToUtc(nyTimeUnspec, _nyTimeZone);
                DateTime localTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, TimeZoneInfo.Local);
                return localTime.TimeOfDay;
            }
            catch
            {
                return dt.TimeOfDay;
            }
        }
        
        // Minimal replacement for missing CurrentBarDate
        private DateTime CurrentBarDate { get { return Bars.GetTime(CurrentBar).Date; } }

		#endregion

		#region Rendering
		
		public override void OnRenderTargetChanged()
        {
            base.OnRenderTargetChanged();
            
            // Dispose managed brushes
            if (_cachedHighVwapBrush != null) _cachedHighVwapBrush.Dispose();
            if (_cachedLowVwapBrush != null) _cachedLowVwapBrush.Dispose();
            if (_cachedHistoricalBrush != null) _cachedHistoricalBrush.Dispose();

            if (RenderTarget != null)
            {
                _cachedHighVwapBrush = CreateBrushFromMedia(HighVWAPColor);
                _cachedLowVwapBrush = CreateBrushFromMedia(LowVWAPColor);
                _cachedHistoricalBrush = CreateBrushFromMedia(HistoricalVWAPColor);
            }
        }
        
        private SharpDX.Direct2D1.SolidColorBrush CreateBrushFromMedia(Brush brush)
        {
             var solid = (SolidColorBrush)brush;
             return new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color((byte)solid.Color.R, (byte)solid.Color.G, (byte)solid.Color.B, (byte)255));
        }

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (Bars == null || chartControl == null || chartScale == null || RenderTarget == null) return;

			// Draw Active VWAPs
			// Series 0 (High)
			if (hasHighVWAP && sessionHighBarIdx != -1)
				DrawAnchoredLine(sessionHighBarIdx, HighVWAPColor, chartControl, chartScale, -1, 0);
				
			// Series 1 (Low)
			if (hasLowVWAP && sessionLowBarIdx != -1)
				DrawAnchoredLine(sessionLowBarIdx, LowVWAPColor, chartControl, chartScale, -1, 1);
				
			// Draw Historical Highs
			foreach (var hist in historicalHighs)
				DrawHistoricalVWAP(hist.VwapValues, hist.StartIdx, hist.EndIdx, HistoricalVWAPColor, chartControl, chartScale);
				
			// Draw Historical Lows
			foreach (var hist in historicalLows)
				DrawHistoricalVWAP(hist.VwapValues, hist.StartIdx, hist.EndIdx, HistoricalVWAPColor, chartControl, chartScale);
		}
		
		private void DrawAnchoredLine(int startIdx, Brush color, ChartControl chartControl, ChartScale chartScale, int endIdx, int seriesIdx)
        {
            if (Bars == null || RenderTarget == null) return;

            int end = (endIdx == -1) ? Bars.Count - 1 : endIdx;
            int start = Math.Max(0, startIdx);
            
            // Optimization: Only draw visible
            int viewStart = Math.Max(start, ChartBars.FromIndex);
            int viewEnd = Math.Min(end, ChartBars.ToIndex);
            
            if (viewStart > viewEnd) return;

            SharpDX.Vector2? lastPoint = null;
            
            // Use Cached Brush if possible
            SharpDX.Direct2D1.SolidColorBrush dxBrush = null;
            if (seriesIdx == 0 && _cachedHighVwapBrush != null) dxBrush = _cachedHighVwapBrush;
            else if (seriesIdx == 1 && _cachedLowVwapBrush != null) dxBrush = _cachedLowVwapBrush;
            else dxBrush = CreateBrushFromMedia(color);

            try {
	            for (int i = viewStart; i <= viewEnd; i++)
	            {
	                double val = Values[seriesIdx].GetValueAt(i);
	                if (double.IsNaN(val)) { lastPoint = null; continue; }
	                
	                float x = chartControl.GetXByBarIndex(ChartBars, i);
	                float y = (float)chartScale.GetYByValue(val);
	                SharpDX.Vector2 point = new SharpDX.Vector2(x, y);
	                
	                if (lastPoint.HasValue)
	                {
	                    RenderTarget.DrawLine(lastPoint.Value, point, dxBrush, 2.0f);
	                }
	                lastPoint = point;
	            }
            }
            finally {
            	// Dispose if it was a temp brush
            	if (dxBrush != null && dxBrush != _cachedHighVwapBrush && dxBrush != _cachedLowVwapBrush)
            		dxBrush.Dispose();
            }
        }
        
        private void DrawHistoricalVWAP(Dictionary<int, double> vwapValues, int startIdx, int endIdx, Brush color, ChartControl chartControl, ChartScale chartScale)
        {
             if (vwapValues == null || vwapValues.Count == 0) return;
             
            int viewStart = Math.Max(startIdx, ChartBars.FromIndex);
            int viewEnd = Math.Min(endIdx, ChartBars.ToIndex);
            
            if (viewStart > viewEnd) return;
            
            SharpDX.Direct2D1.SolidColorBrush dxBrush = _cachedHistoricalBrush != null ? _cachedHistoricalBrush : CreateBrushFromMedia(color);
            bool cleanUp = (_cachedHistoricalBrush == null);
            
            SharpDX.Vector2? lastPoint = null;
            
            try {
	            for (int i = viewStart; i <= viewEnd; i++)
	            {
	                if (!vwapValues.ContainsKey(i)) { lastPoint = null; continue; }
	                double val = vwapValues[i];
	                
	                float x = chartControl.GetXByBarIndex(ChartBars, i);
	                float y = (float)chartScale.GetYByValue(val);
	                SharpDX.Vector2 point = new SharpDX.Vector2(x, y);
	                
	                if (lastPoint.HasValue)
	                {
	                    RenderTarget.DrawLine(lastPoint.Value, point, dxBrush, 1.5f);
	                }
	                lastPoint = point;
	            }
            }
            finally {
            	if (cleanUp && dxBrush != null) dxBrush.Dispose();
            }
        }
		
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RelativeIndicators.RelativeTrend[] cacheRelativeTrend;
		public RelativeIndicators.RelativeTrend RelativeTrend(string version, TrendVwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, Brush highVWAPColor, Brush lowVWAPColor, Brush asiaLineColor, Brush asiaLabelColor, bool showAsiaHigh, bool showAsiaLow, Brush europeLineColor, Brush europeLabelColor, bool showEuropeHigh, bool showEuropeLow, Brush uSLineColor, Brush uSLabelColor, bool showUSHigh, bool showUSLow, Brush labelBackgroundColor, Brush historicalVWAPColor, bool showAsia, string asiaStartTime, string asiaEndTime, bool showEurope, string europeStartTime, string europeEndTime, bool showUS, string uSStartTime, string uSEndTime)
		{
			return RelativeTrend(Input, version, vwapMethod, maxHistoryDays, useExchangeTime, highVWAPColor, lowVWAPColor, asiaLineColor, asiaLabelColor, showAsiaHigh, showAsiaLow, europeLineColor, europeLabelColor, showEuropeHigh, showEuropeLow, uSLineColor, uSLabelColor, showUSHigh, showUSLow, labelBackgroundColor, historicalVWAPColor, showAsia, asiaStartTime, asiaEndTime, showEurope, europeStartTime, europeEndTime, showUS, uSStartTime, uSEndTime);
		}

		public RelativeIndicators.RelativeTrend RelativeTrend(ISeries<double> input, string version, TrendVwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, Brush highVWAPColor, Brush lowVWAPColor, Brush asiaLineColor, Brush asiaLabelColor, bool showAsiaHigh, bool showAsiaLow, Brush europeLineColor, Brush europeLabelColor, bool showEuropeHigh, bool showEuropeLow, Brush uSLineColor, Brush uSLabelColor, bool showUSHigh, bool showUSLow, Brush labelBackgroundColor, Brush historicalVWAPColor, bool showAsia, string asiaStartTime, string asiaEndTime, bool showEurope, string europeStartTime, string europeEndTime, bool showUS, string uSStartTime, string uSEndTime)
		{
			if (cacheRelativeTrend != null)
				for (int idx = 0; idx < cacheRelativeTrend.Length; idx++)
					if (cacheRelativeTrend[idx] != null && cacheRelativeTrend[idx].Version == version && cacheRelativeTrend[idx].VwapMethod == vwapMethod && cacheRelativeTrend[idx].MaxHistoryDays == maxHistoryDays && cacheRelativeTrend[idx].UseExchangeTime == useExchangeTime && cacheRelativeTrend[idx].HighVWAPColor == highVWAPColor && cacheRelativeTrend[idx].LowVWAPColor == lowVWAPColor && cacheRelativeTrend[idx].AsiaLineColor == asiaLineColor && cacheRelativeTrend[idx].AsiaLabelColor == asiaLabelColor && cacheRelativeTrend[idx].ShowAsiaHigh == showAsiaHigh && cacheRelativeTrend[idx].ShowAsiaLow == showAsiaLow && cacheRelativeTrend[idx].EuropeLineColor == europeLineColor && cacheRelativeTrend[idx].EuropeLabelColor == europeLabelColor && cacheRelativeTrend[idx].ShowEuropeHigh == showEuropeHigh && cacheRelativeTrend[idx].ShowEuropeLow == showEuropeLow && cacheRelativeTrend[idx].USLineColor == uSLineColor && cacheRelativeTrend[idx].USLabelColor == uSLabelColor && cacheRelativeTrend[idx].ShowUSHigh == showUSHigh && cacheRelativeTrend[idx].ShowUSLow == showUSLow && cacheRelativeTrend[idx].LabelBackgroundColor == labelBackgroundColor && cacheRelativeTrend[idx].HistoricalVWAPColor == historicalVWAPColor && cacheRelativeTrend[idx].ShowAsia == showAsia && cacheRelativeTrend[idx].AsiaStartTime == asiaStartTime && cacheRelativeTrend[idx].AsiaEndTime == asiaEndTime && cacheRelativeTrend[idx].ShowEurope == showEurope && cacheRelativeTrend[idx].EuropeStartTime == europeStartTime && cacheRelativeTrend[idx].EuropeEndTime == europeEndTime && cacheRelativeTrend[idx].ShowUS == showUS && cacheRelativeTrend[idx].USStartTime == uSStartTime && cacheRelativeTrend[idx].USEndTime == uSEndTime && cacheRelativeTrend[idx].EqualsInput(input))
						return cacheRelativeTrend[idx];
			return CacheIndicator<RelativeIndicators.RelativeTrend>(new RelativeIndicators.RelativeTrend(){ Version = version, VwapMethod = vwapMethod, MaxHistoryDays = maxHistoryDays, UseExchangeTime = useExchangeTime, HighVWAPColor = highVWAPColor, LowVWAPColor = lowVWAPColor, AsiaLineColor = asiaLineColor, AsiaLabelColor = asiaLabelColor, ShowAsiaHigh = showAsiaHigh, ShowAsiaLow = showAsiaLow, EuropeLineColor = europeLineColor, EuropeLabelColor = europeLabelColor, ShowEuropeHigh = showEuropeHigh, ShowEuropeLow = showEuropeLow, USLineColor = uSLineColor, USLabelColor = uSLabelColor, ShowUSHigh = showUSHigh, ShowUSLow = showUSLow, LabelBackgroundColor = labelBackgroundColor, HistoricalVWAPColor = historicalVWAPColor, ShowAsia = showAsia, AsiaStartTime = asiaStartTime, AsiaEndTime = asiaEndTime, ShowEurope = showEurope, EuropeStartTime = europeStartTime, EuropeEndTime = europeEndTime, ShowUS = showUS, USStartTime = uSStartTime, USEndTime = uSEndTime }, input, ref cacheRelativeTrend);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeTrend RelativeTrend(string version, TrendVwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, Brush highVWAPColor, Brush lowVWAPColor, Brush asiaLineColor, Brush asiaLabelColor, bool showAsiaHigh, bool showAsiaLow, Brush europeLineColor, Brush europeLabelColor, bool showEuropeHigh, bool showEuropeLow, Brush uSLineColor, Brush uSLabelColor, bool showUSHigh, bool showUSLow, Brush labelBackgroundColor, Brush historicalVWAPColor, bool showAsia, string asiaStartTime, string asiaEndTime, bool showEurope, string europeStartTime, string europeEndTime, bool showUS, string uSStartTime, string uSEndTime)
		{
			return indicator.RelativeTrend(Input, version, vwapMethod, maxHistoryDays, useExchangeTime, highVWAPColor, lowVWAPColor, asiaLineColor, asiaLabelColor, showAsiaHigh, showAsiaLow, europeLineColor, europeLabelColor, showEuropeHigh, showEuropeLow, uSLineColor, uSLabelColor, showUSHigh, showUSLow, labelBackgroundColor, historicalVWAPColor, showAsia, asiaStartTime, asiaEndTime, showEurope, europeStartTime, europeEndTime, showUS, uSStartTime, uSEndTime);
		}

		public Indicators.RelativeIndicators.RelativeTrend RelativeTrend(ISeries<double> input , string version, TrendVwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, Brush highVWAPColor, Brush lowVWAPColor, Brush asiaLineColor, Brush asiaLabelColor, bool showAsiaHigh, bool showAsiaLow, Brush europeLineColor, Brush europeLabelColor, bool showEuropeHigh, bool showEuropeLow, Brush uSLineColor, Brush uSLabelColor, bool showUSHigh, bool showUSLow, Brush labelBackgroundColor, Brush historicalVWAPColor, bool showAsia, string asiaStartTime, string asiaEndTime, bool showEurope, string europeStartTime, string europeEndTime, bool showUS, string uSStartTime, string uSEndTime)
		{
			return indicator.RelativeTrend(input, version, vwapMethod, maxHistoryDays, useExchangeTime, highVWAPColor, lowVWAPColor, asiaLineColor, asiaLabelColor, showAsiaHigh, showAsiaLow, europeLineColor, europeLabelColor, showEuropeHigh, showEuropeLow, uSLineColor, uSLabelColor, showUSHigh, showUSLow, labelBackgroundColor, historicalVWAPColor, showAsia, asiaStartTime, asiaEndTime, showEurope, europeStartTime, europeEndTime, showUS, uSStartTime, uSEndTime);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeTrend RelativeTrend(string version, TrendVwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, Brush highVWAPColor, Brush lowVWAPColor, Brush asiaLineColor, Brush asiaLabelColor, bool showAsiaHigh, bool showAsiaLow, Brush europeLineColor, Brush europeLabelColor, bool showEuropeHigh, bool showEuropeLow, Brush uSLineColor, Brush uSLabelColor, bool showUSHigh, bool showUSLow, Brush labelBackgroundColor, Brush historicalVWAPColor, bool showAsia, string asiaStartTime, string asiaEndTime, bool showEurope, string europeStartTime, string europeEndTime, bool showUS, string uSStartTime, string uSEndTime)
		{
			return indicator.RelativeTrend(Input, version, vwapMethod, maxHistoryDays, useExchangeTime, highVWAPColor, lowVWAPColor, asiaLineColor, asiaLabelColor, showAsiaHigh, showAsiaLow, europeLineColor, europeLabelColor, showEuropeHigh, showEuropeLow, uSLineColor, uSLabelColor, showUSHigh, showUSLow, labelBackgroundColor, historicalVWAPColor, showAsia, asiaStartTime, asiaEndTime, showEurope, europeStartTime, europeEndTime, showUS, uSStartTime, uSEndTime);
		}

		public Indicators.RelativeIndicators.RelativeTrend RelativeTrend(ISeries<double> input , string version, TrendVwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, Brush highVWAPColor, Brush lowVWAPColor, Brush asiaLineColor, Brush asiaLabelColor, bool showAsiaHigh, bool showAsiaLow, Brush europeLineColor, Brush europeLabelColor, bool showEuropeHigh, bool showEuropeLow, Brush uSLineColor, Brush uSLabelColor, bool showUSHigh, bool showUSLow, Brush labelBackgroundColor, Brush historicalVWAPColor, bool showAsia, string asiaStartTime, string asiaEndTime, bool showEurope, string europeStartTime, string europeEndTime, bool showUS, string uSStartTime, string uSEndTime)
		{
			return indicator.RelativeTrend(input, version, vwapMethod, maxHistoryDays, useExchangeTime, highVWAPColor, lowVWAPColor, asiaLineColor, asiaLabelColor, showAsiaHigh, showAsiaLow, europeLineColor, europeLabelColor, showEuropeHigh, showEuropeLow, uSLineColor, uSLabelColor, showUSHigh, showUSLow, labelBackgroundColor, historicalVWAPColor, showAsia, asiaStartTime, asiaEndTime, showEurope, europeStartTime, europeEndTime, showUS, uSStartTime, uSEndTime);
		}
	}
}

#endregion
