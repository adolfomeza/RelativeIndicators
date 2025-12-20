// Relative DVA-PVA Indicator
// Customized by User Request

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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators.RelativeIndicators;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;
using SharpDX.Direct2D1;
#endregion

// This namespace holds indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
	/// <summary>
	/// The Current Day VWAP is the volume weighted average price of today's trading session. 
	/// The indicator further displays three upper and lower volatility bands.
	/// </summary>
	/// 
	[Gui.CategoryOrder("Algorithmic Options", 0)]
	[Gui.CategoryOrder("Custom Hours", 5)]
	[Gui.CategoryOrder("Standard Deviation Bands", 10)]
	[Gui.CategoryOrder("Quarter Range Bands", 15)]
	[Gui.CategoryOrder("Data Series", 20)]
	[Gui.CategoryOrder("Set up", 30)]
	[Gui.CategoryOrder("Visual", 40)]
	[Gui.CategoryOrder("Plot Colors", 50)]
	[Gui.CategoryOrder("Plot Parameters", 60)]
	[Gui.CategoryOrder("Area Opacity", 70)]
	[Gui.CategoryOrder("Version", 80)]
	[TypeConverter("NinjaTrader.NinjaScript.Indicators.RelativeDVAPVA_v2TypeConverter")]
	public class RelativeDVAPVA_v2 : Indicator
	{
		private DateTime						sessionDateTmp				= Globals.MinDate;
		private DateTime						cacheSessionDate			= Globals.MinDate;
		private TimeSpan						customSessionStart			= new TimeSpan(8,30,0);
		private TimeSpan						customSessionEnd			= new TimeSpan(15,15,0);
		private double							multiplierSD1				= 1.0;
		private double							multiplierSD2				= 2.0;
		private double							multiplierSD3				= 3.0;
		private double							multiplierQR1				= 1.0;
		private double							multiplierQR2				= 2.0;
		private double							multiplierQR3				= 3.0;
		private double							multiplier1					= 1.0;
		private double							multiplier2					= 2.0;
		private double							multiplier3					= 3.0;

		private bool							showDVABands				= true;
		private bool							showSessionVWAPLine			= true;
		private double							open						= 0.0;
		private double							high						= 0.0;
		private double							low							= 0.0;
		private double							close						= 0.0;
		private double							mean						= 0.0;
		private double							mean1						= 0.0;
		private double							mean2						= 0.0;
		private	double							priorVolSum					= 0.0;
		private	double							priorSquareSum				= 0.0;
		private	double							priorSessionHigh			= 0.0;
		private	double							priorSessionLow				= 0.0;
		private double							priorVWAP					= 0.0;
		private double							sessionVWAP					= 0.0;
		private int								displacement				= 0;
		private int								count						= 0;
		private bool							showBands					= true;
		private bool							plotVWAP					= false;
		private bool							gap0						= true;
		private bool							gap1						= true;
		private bool							timeBased					= true;
		private bool							breakAtEOD					= true;
		private bool							calculateFromPriceData		= true;
		private bool							applyTradingHours			= false;
		private bool							anchorBar					= false;
		private bool							basicError					= false;
		private bool							errorMessage				= false;
		private bool							sundaySessionError			= false;
		private bool							startEndTimeError			= false;
		private SessionTypeVWAPD				sessionType					= SessionTypeVWAPD.Full_Session;
		private TimeZonesVWAPD				customTZSelector			= TimeZonesVWAPD.Exchange_Time;
		private BandTypeVWAPD				bandType					= BandTypeVWAPD.Standard_Deviation;
		private readonly List<int>				newSessionBarIdxArr			= new List<int>();
		private SessionIterator					sessionIterator				= null;
		private System.Windows.Media.Brush		upBrush						= Brushes.Blue;
		private System.Windows.Media.Brush  	downBrush					= Brushes.Red;
		private System.Windows.Media.Brush		innerBandBrush				= Brushes.RoyalBlue;
		private System.Windows.Media.Brush  	middleBandBrush				= Brushes.MediumBlue;
		private System.Windows.Media.Brush		outerBandBrush				= Brushes.Navy;
		private System.Windows.Media.Brush		band05Brush					= Brushes.Transparent; // Configurable
		private System.Windows.Media.Brush		band15Brush					= Brushes.Transparent; // Configurable
		private System.Windows.Media.Brush		innerAreaBrush 				= null;
		private System.Windows.Media.Brush		middleAreaBrush 			= null;
		private System.Windows.Media.Brush		outerAreaBrush 				= null;
		private System.Windows.Media.Brush		errorBrush					= null;
		private SharpDX.Direct2D1.Brush 		innerAreaBrushDX 			= null;
		private SharpDX.Direct2D1.Brush 		middleAreaBrushDX 			= null;
		private SharpDX.Direct2D1.Brush 		outerAreaBrushDX 			= null;
		private SimpleFont						errorFont					= null;
		private string							errorText1					= "The amaCurrentDayVWAP only works on price data.";
		private string							errorText2					= "The amaCurrentDayVWAP can only be displayed on intraday charts.";
		private string							errorText3					= "The amaCurrentDayVWAP cannot be used with a negative displacement.";
		private string							errorText4					= "The amaCurrentDayVWAP cannot be used with a displacement on non-equidistant chart bars.";
		private string							errorText5					= "The amaCurrentDayVWAP cannot be used when the 'Break at EOD' data series property is unselected.";
		private string							errorText6					= "amaCurrentDayVWAP: The VWAP may not be calculated from fractional Sunday sessions. Please change your trading hours template.";
		private string							errorText7					= "amaCurrentDayVWAP: Mismatch between trading hours selected for the VWAP and the session template selected for the chart bars!";
		private int								innerAreaOpacity			= 60;
		private int								middleAreaOpacity			= 0;
		private int								outerAreaOpacity			= 60;
		private int								plot0Width					= 3;
		private int								plot1Width					= 1;
		private PlotStyle						plot0Style					= PlotStyle.Line;
		private DashStyleHelper					dash0Style					= DashStyleHelper.DashDot;
		private PlotStyle						plot1Style					= PlotStyle.Line;
		private DashStyleHelper					dash1Style					= DashStyleHelper.Solid;
		private TimeZoneInfo					globalTimeZone				= Core.Globals.GeneralOptions.TimeZoneInfo;
		private TimeZoneInfo					customTimeZone;
		private string							versionString				= "v 2.5  -  October 27, 2019";
		private Series<DateTime>				tradingDate;
		private Series<DateTime>				sessionBegin;
		private Series<DateTime>				anchorTime;
		private Series<DateTime>				cutoffTime;
		private Series<bool>					calcOpen;
		private Series<bool>					initPlot;
		private Series<int>						sessionBar;
		private Series<double>					firstBarOpen;
		private Series<double>					currentVolSum;
		private Series<double>					currentVWAP;
		private Series<double>					currentSquareSum;
		private Series<double>					sessionHigh;
		private Series<double>					sessionLow;
		private Series<double>					offset;
		
		// Public Signal Series
		private Series<bool>					ipbLong;
		private Series<bool>					ipbShort;
		private Series<bool>					efLong;
		private Series<bool>					efShort;
		private Series<bool>					bpbLong;
		private Series<bool>					bpbShort;
		private Series<bool>					rpbLong;
		private Series<bool>					rpbShort;

        // UI State Cache
        private string lastBtnText = "";
        private System.Windows.Media.Brush lastBtnColor = null;
        private System.Windows.Media.Brush lastTxtColor = null;
        private string lastSignalText = "";
        private System.Windows.Media.Brush lastSignalColor = null;
        private System.Windows.Media.Brush lastSignalTextColor = null;
        private bool lastShowSignalBtn = false;
		private string instanceGuid;
		private Series<int> debugStateHistory;
		
		private class SessionZone
		{
			public DateTime StartTime;
			public DateTime EndTime;
			public double UpperY;
			public double LowerY;
			public string Tag;
			public bool IsActive;
			public bool IsBreached;
			
			// BPB/RPB State
			public bool IsBreakoutLongConfirmed;
			public bool IsBreakoutShortConfirmed;
			public int BreakoutBarsCount;
			public int BreakoutDirection; // 0=None, 1=Long, -1=Short
			public DateTime BreakoutStartTime;
			public bool BPBLongFired;
			public bool BPBShortFired;
			public bool RPBLongFired;
			public bool RPBShortFired;
			
			// Bar Number Restriction
			public int ConfirmationBarNumberLong = -1;
			public int ConfirmationBarNumberShort = -1;
			
			// Multiple Signals Tracking
			public int LastSignalBarLong = -1;
			public int LastSignalBarShort = -1;
			
			// Phase 2: New Logic State
			public bool IsGapConfirmed;
			public bool IsGapLong;
			public bool IsGapShort;
			public bool RPBSetupActive; // WaitingForRPB
			public bool RPBFailureDepthReached;
			public DateTime RPBFailureStartTime; // For Time-based failure
			public bool IsRotational;
			public bool IsRotationalLong;
			public bool IsRotationalShort;
			
			// Mitigation State
			public bool IsMitigated;
		}
		private List<SessionZone> activeZones = new List<SessionZone>();
		private List<SessionZone> allZones = new List<SessionZone>();
		private object zonesLock = new object();
		private bool showSessionZones = true;
		private int zoneCutoffPercentage = 50;
		private System.Windows.Media.Brush sessionZoneBrush = Brushes.Gray;
		private int sessionZoneOpacity = 40;
		private System.Windows.Media.Brush zoneLineBrush = Brushes.Gray;
		private int zoneLineWidth = 1;
		private System.Windows.Media.Brush zoneTextBrush = Brushes.White;
		private int zoneTextSize = 12;
		private string zoneLabelUpper = "pDVAH";
		private string zoneLabelLower = "pDVAL";
		private System.Windows.Media.Brush zoneTextBackgroundBrush = Brushes.Black;
		private int zoneTextBackgroundOpacity = 100;

		// IPB/EF Logic Variables
		public enum MarketState
		{
			Waiting,
			Neutral,
			ImbalanceLong,
			ImbalanceShort,
			FailedLong,
			FailedShort,
			Rotational,
			WaitingForRPB
		}



		private MarketState currentMarketState = MarketState.Waiting;
		// BPB/RPB State Tracking
		private bool bpbLongFired = false;
		private bool bpbShortFired = false;
		private bool rpbLongFired = false;
		private bool rpbShortFired = false;
		private int breakoutBarsCount = 0;
		private bool isBreakoutLongConfirmed = false;
		private bool isBreakoutShortConfirmed = false;
		private bool ipbSignalFired = false;
		private bool allowMultipleIPB = false;
		private bool allowMultipleEF = false;
		private bool allowRotation = false;
		private bool filterBroadBars = false; // User requested to disable by default
		private double maxCandleWidthPercent = 30.0; // Volatility Filter
		private double imbalanceEntryVWAP = 0;
		private int imbalanceEntryBar = 0;
		private int failureEntryBar = 0;
		private MarketState lastMarketState = MarketState.Neutral; // Track changes

		
		private int tradingStartDelay = 0; // Disabled by default
		private int maxDaysToDraw = 3;
		private int textSizeIPB = 10;
		private int lineLengthIPB = 3;
		private System.Windows.Media.Brush textColorIPB = Brushes.White;
		private System.Windows.Media.Brush lineColorIPB = Brushes.Gray;
		private int lineWidthIPB = 1;
		private DashStyleHelper lineStyleIPB = DashStyleHelper.Solid;

		// EF Visuals
		private int textSizeEF = 10;
		private int lineLengthEF = 3;
		private System.Windows.Media.Brush textColorEF = Brushes.White;
		private System.Windows.Media.Brush lineColorEF = Brushes.Gray;
		private int lineWidthEF = 1;
		private DashStyleHelper lineStyleEF = DashStyleHelper.Solid;
		
		private string textIPBLong = "IPB Long";
		private string textIPBShort = "IPB Short";
		private string textEFLong = "EF Long";
		private string textEFShort = "EF Short";

		// BPB/RPB Logic & Visuals
		private AcceptanceMode acceptanceMode = AcceptanceMode.Any; // Keep Any to allow Dist OR Time
		private int breakoutConfirmationBars = 1;
		private double breakoutConfirmationDistance = 25.0; // Restored to 25.0
		private int breakoutMinTimeMinutes = 30; // Restored to 30
		
		private double rpbDepthPercent = 25.0;

		private bool showDebugState = false;
		private bool showZoneDate = false;

        // Alerts
        private bool useAlerts = true;
        private bool alertOnBPB = true;
        private bool alertOnRPB = true;
        private bool alertOnIPB = true;
        private bool alertOnEF = true;
        private string alertSound = "Alert2.wav";
        private bool sendEmail = false;
        private string emailAddress = "";
        private bool attachScreenshot = false;
		
		// Toolbar Button
		private System.Windows.Controls.Button statusButton = null;
		private System.Windows.Controls.Button btnSignal = null;
		private System.Windows.Controls.Grid chartGrid = null;
		
		// Button Colors
		private System.Windows.Media.Brush colorButtonGapLong = Brushes.Orange;
		private System.Windows.Media.Brush colorButtonGapShort = Brushes.OrangeRed;
		private System.Windows.Media.Brush colorButtonRotationalLong = Brushes.DodgerBlue;
		private System.Windows.Media.Brush colorButtonRotationalShort = Brushes.DeepSkyBlue;
		private System.Windows.Media.Brush colorButtonBreakoutLong = Brushes.LimeGreen;
		private System.Windows.Media.Brush colorButtonBreakoutShort = Brushes.Red;
		private System.Windows.Media.Brush colorButtonRPBLong = Brushes.LimeGreen;
		private System.Windows.Media.Brush colorButtonRPBShort = Brushes.Red;
		private System.Windows.Media.Brush colorButtonNeutral = Brushes.Gray;
		private System.Windows.Media.Brush colorButtonPending = Brushes.Gray;
		private System.Windows.Media.Brush colorButtonTextPending = Brushes.White;
		
		// Button Text Colors
		private System.Windows.Media.Brush colorButtonTextGapLong = Brushes.Black;
		private System.Windows.Media.Brush colorButtonTextGapShort = Brushes.White;
		private System.Windows.Media.Brush colorButtonTextRotationalLong = Brushes.White;
		private System.Windows.Media.Brush colorButtonTextRotationalShort = Brushes.White;
		private System.Windows.Media.Brush colorButtonTextBreakoutLong = Brushes.Black;
		private System.Windows.Media.Brush colorButtonTextBreakoutShort = Brushes.White;
		private System.Windows.Media.Brush colorButtonTextRPBLong = Brushes.Black;
		private System.Windows.Media.Brush colorButtonTextRPBShort = Brushes.White;
		private System.Windows.Media.Brush colorButtonTextNeutral = Brushes.White;

		// EF/IPB Button Colors
		private System.Windows.Media.Brush colorButtonEFLong = Brushes.LimeGreen;
		private System.Windows.Media.Brush colorButtonEFShort = Brushes.Red;
		private System.Windows.Media.Brush colorButtonIPBLong = Brushes.LimeGreen;
		private System.Windows.Media.Brush colorButtonIPBShort = Brushes.Red;
		
		private System.Windows.Media.Brush colorButtonTextEFLong = Brushes.Black;
		private System.Windows.Media.Brush colorButtonTextEFShort = Brushes.White;
		private System.Windows.Media.Brush colorButtonTextIPBLong = Brushes.Black;
		private System.Windows.Media.Brush colorButtonTextIPBShort = Brushes.White;

		private string textBPBLong = "BPB-Long";
		private string textBPBShort = "BPB-Short";
		private string textRPBLong = "RPB-Long";
		private string textRPBShort = "RPB-Short";
		
		private int textSizeBPB_RPB = 10;
		private System.Windows.Media.Brush textColorBPB = Brushes.Yellow;
		private System.Windows.Media.Brush textColorRPB = Brushes.Yellow;
		private System.Windows.Media.Brush historicalSignalColor = Brushes.Gray;
		private System.Windows.Media.Brush lineColorBPB = Brushes.Blue;
		private System.Windows.Media.Brush lineColorRPB = Brushes.Orange;
		private int lineWidthBPB_RPB = 2;
		private DashStyleHelper lineStyleBPB_RPB = DashStyleHelper.Solid;
		private int lineLengthBPB_RPB = 3;
		
		// Debug / Logic Visualization
		private bool showLogicLines = false;
		private bool showLogicLabels = false;
		private System.Windows.Media.Brush logicLineColor = Brushes.Gray;
		private DashStyleHelper logicLineStyle = DashStyleHelper.Dot;
		private int logicLineWidth = 1;
		private int logicTextSize = 9;
		
		// Signal Visibility
		private bool showIPB = true;
		private bool showEF = true;
		private bool showBPB = true;
		private bool showRPB = true;
		
		// Market Analyzer Properties
		[Browsable(false)]
		[XmlIgnore]
		public string StateText { get; private set; } = "NEUTRAL";
		
		[Browsable(false)]
		[XmlIgnore]
		public System.Windows.Media.Brush StateColor { get; private set; } = Brushes.Gray;
		[Browsable(false)]
		[XmlIgnore]
		public System.Windows.Media.Brush StateTextColor { get; private set; } = Brushes.White;

		[Browsable(false)]
		[XmlIgnore]
		public int ActiveZonesCount { get { return activeZones.Count; } }

		[Browsable(false)]
		[XmlIgnore]
		public double ActiveZoneHigh 
		{ 
			get 
			{ 
				if (activeZones.Count > 0) return activeZones[activeZones.Count - 1].UpperY;
				return 0;
			} 
		}

		[Browsable(false)]
		[XmlIgnore]
		public double ActiveZoneLow 
		{ 
			get 
			{ 
				if (activeZones.Count > 0) return activeZones[activeZones.Count - 1].LowerY;
				return 0;
			} 
		}

		[Browsable(false)]
		[XmlIgnore]
		public bool IsGapConfirmed 
		{ 
			get 
			{ 
				if (activeZones.Count > 0) return activeZones[activeZones.Count - 1].IsGapConfirmed;
				return false;
			} 
		}

		[Browsable(false)]
		[XmlIgnore]
		public bool IsRotational 
		{ 
			get 
			{ 
				if (activeZones.Count > 0) return activeZones[activeZones.Count - 1].IsRotational;
				return false;
			} 
		}



		[Browsable(false)]
		[XmlIgnore]
		public double SessionOpen 
		{ 
			get 
			{ 
				if (firstBarOpen != null && firstBarOpen.Count > 0) return firstBarOpen[0];
				return 0;
			} 
		}

		[Browsable(false)]
		[XmlIgnore]
		public string ZoneDebugString
		{
			get
			{
				if (activeZones.Count == 0) return "No Zones";
				SessionZone z = activeZones[activeZones.Count - 1];
				return $"C:{activeZones.Count} R:{z.IsRotational} BC:{z.BreakoutBarsCount} ZH:{z.UpperY:F2} ZL:{z.LowerY:F2}";
			}
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= "\r\nThe Relative DVA-PVA indicator displays volume weighted average price and volatility bands.";
				Name						= "Relative DVA-PVA v2";
				IsSuspendedWhileInactive	= true;
				IsOverlay					= true;
				Calculate					= Calculate.OnEachTick;
				IsAutoScale					= false;
				ArePlotsConfigurable		= false;
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "Session VWAP");	
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "Upper Band SD 3");
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "Upper Band SD 2");
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "Upper Band SD 1");	
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "Lower Band SD 1");
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "Lower Band SD 2");
				AddPlot(new Stroke(Brushes.Gray, 2), PlotStyle.Line, "Lower Band SD 3");
				// New 0.5 SD Plots (Transparent by default)
				AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "Upper Band SD 0.5");
				AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "Lower Band SD 0.5");
				// New 1.5 SD Plots (Transparent by default)
				AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "Upper Band SD 1.5");
				AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "Lower Band SD 1.5");
				SetZOrder(-100);
			}
			else if (State == State.Configure)
			{
				displacement = Displacement;
				Plots[1].Brush = outerBandBrush.Clone();
				Plots[2].Brush = middleBandBrush.Clone();
				Plots[3].Brush = innerBandBrush.Clone();
				Plots[4].Brush = innerBandBrush.Clone();
				Plots[5].Brush = middleBandBrush.Clone();
				Plots[6].Brush = outerBandBrush.Clone();
				Plots[0].Width = plot0Width;
				Plots[0].PlotStyle = plot0Style;
				Plots[0].DashStyleHelper = dash0Style;			
				Plots[1].Width = plot1Width;
				Plots[1].PlotStyle = plot1Style;
				Plots[1].DashStyleHelper = dash1Style;
				Plots[2].Width = plot1Width;
				Plots[2].PlotStyle = plot1Style;
				Plots[2].DashStyleHelper = dash1Style;
				Plots[3].Width = plot1Width;
				Plots[3].PlotStyle = plot1Style;
				Plots[3].DashStyleHelper = dash1Style;
				Plots[4].Width = plot1Width;
				Plots[4].PlotStyle = plot1Style;
				Plots[4].DashStyleHelper = dash1Style;
				Plots[5].Width = plot1Width;
				Plots[5].PlotStyle = plot1Style;
				Plots[5].DashStyleHelper = dash1Style;
				Plots[6].Width = plot1Width;
				Plots[6].PlotStyle = plot1Style;
				Plots[6].DashStyleHelper = dash1Style;
				Plots[6].DashStyleHelper = dash1Style;
				// Config for new plots
				Plots[7].Width = plot1Width; 
				Plots[7].Brush = band05Brush.Clone(); 
				Plots[7].PlotStyle = plot1Style;
				Plots[7].DashStyleHelper = dash1Style;
				
				Plots[8].Width = plot1Width;
				Plots[8].Brush = band05Brush.Clone();
				Plots[8].PlotStyle = plot1Style;
				Plots[8].DashStyleHelper = dash1Style;
				
				upBrush.Freeze();
				if (band05Brush != null) band05Brush.Freeze();
				
				// Config for 1.5 plots
				Plots[9].Width = plot1Width; 
				Plots[9].Brush = band15Brush.Clone(); 
				Plots[9].PlotStyle = plot1Style;
				Plots[9].DashStyleHelper = dash1Style;
				
				Plots[10].Width = plot1Width;
				Plots[10].Brush = band15Brush.Clone();
				Plots[10].PlotStyle = plot1Style;
				Plots[10].DashStyleHelper = dash1Style;

				if (band15Brush != null) band15Brush.Freeze();
				
				downBrush.Freeze();
				innerAreaBrush	= innerBandBrush.Clone();
				innerAreaBrush.Opacity = (float) innerAreaOpacity/100.0;
				innerAreaBrush.Freeze();
				middleAreaBrush	= middleBandBrush.Clone();
				middleAreaBrush.Opacity = (float) middleAreaOpacity/100.0;
				middleAreaBrush.Freeze();
				outerAreaBrush	= outerBandBrush.Clone();
				outerAreaBrush.Opacity = (float) outerAreaOpacity/100.0;
				outerAreaBrush.Freeze();
				if (sessionZoneBrush != null) sessionZoneBrush.Freeze();
				if (zoneLineBrush != null) zoneLineBrush.Freeze();
				if (zoneTextBrush != null) zoneTextBrush.Freeze();
				if (zoneTextBackgroundBrush != null) zoneTextBackgroundBrush.Freeze();
				if (textColorIPB != null) textColorIPB.Freeze();
				if (lineColorIPB != null) lineColorIPB.Freeze();
				if (textColorEF != null) textColorEF.Freeze();
				if (lineColorEF != null) lineColorEF.Freeze();
				if (textColorBPB != null) textColorBPB.Freeze();
				if (textColorRPB != null) textColorRPB.Freeze();
				if (lineColorBPB != null) lineColorBPB.Freeze();
				if (lineColorRPB != null) lineColorRPB.Freeze();
			}
			else if (State == State.Historical)
			{
				if (ChartControl != null)
				{
					ChartControl.Dispatcher.InvokeAsync(() =>
					{
						InsertWpfControls();
						
						// DIAGNOSTIC: List all indicators on the chart
						NinjaTrader.Code.Output.Process("=== DIAGNOSTIC: LIST OF INDICATORS ON CHART ===", PrintTo.OutputTab1);
						if (ChartControl != null && ChartControl.Indicators != null)
						{
							foreach (var ind in ChartControl.Indicators)
							{
								NinjaTrader.Code.Output.Process(string.Format("FOUND INDICATOR: Name='{0}' Type='{1}'", ind.Name, ind.GetType().FullName), PrintTo.OutputTab1);
							}
						}
						NinjaTrader.Code.Output.Process("===============================================", PrintTo.OutputTab1);
					});
				}
			}
			else if (State == State.Terminated)
			{
				if (ChartControl != null)
				{
					ChartControl.Dispatcher.InvokeAsync(() =>
					{
						RemoveWpfControls();
					});
				}
			}
		  	else if (State == State.DataLoaded)
		 	{
				instanceGuid = Guid.NewGuid().ToString().Substring(0, 8);
				lock (zonesLock)
				{
					activeZones.Clear();
					allZones.Clear();
				}
				tradingDate = new Series<DateTime>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				sessionBegin = new Series<DateTime>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				anchorTime = new Series<DateTime>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				debugStateHistory = new Series<int>(this); // Infinite history for debug
				cutoffTime = new Series<DateTime>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				calcOpen = new Series<bool>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				initPlot = new Series<bool>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				sessionBar = new Series<int>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				firstBarOpen = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				currentVolSum = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				currentVWAP = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				currentSquareSum = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				sessionHigh = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				sessionLow = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				offset = new Series<double>(this, MaximumBarsLookBack.TwoHundredFiftySix);
				
				ipbLong = new Series<bool>(this);
				ipbShort = new Series<bool>(this);
				efLong = new Series<bool>(this);
				efShort = new Series<bool>(this);
				bpbLong = new Series<bool>(this);
				bpbShort = new Series<bool>(this);
				rpbLong = new Series<bool>(this);
				rpbShort = new Series<bool>(this);
				if (Bars.BarsType.IsTimeBased) 
					timeBased = true;
				else
					timeBased = false;
				if(Input is PriceSeries)
					calculateFromPriceData = true;
				else
					calculateFromPriceData = false;
		    	sessionIterator = new SessionIterator(Bars);

				// Moved logic from State.Historical to State.DataLoaded
				if (sessionType == SessionTypeVWAPD.Full_Session) 
					applyTradingHours = false;
				else if (sessionType == SessionTypeVWAPD.Custom_Hours) 
				{
					applyTradingHours = true;
				}

				if(bandType == BandTypeVWAPD.Standard_Deviation)
				{
					multiplier1 = multiplierSD1;
					multiplier2 = multiplierSD2;
					multiplier3 = multiplierSD3;
					showBands = true;
				}
				else if(bandType == BandTypeVWAPD.Quarter_Range)
				{
					multiplier1 = multiplierQR1;
					multiplier2 = multiplierQR2;
					multiplier3 = multiplierQR3;
					showBands = true;
				}
				else if(bandType == BandTypeVWAPD.None)
					showBands = false;

				switch (customTZSelector)
				{
					case TimeZonesVWAPD.Exchange_Time:	
						customTimeZone = Instrument.MasterInstrument.TradingHours.TimeZoneInfo;
						break;
					case TimeZonesVWAPD.Chart_Time:	
						customTimeZone = Core.Globals.GeneralOptions.TimeZoneInfo; 
						break;
					case TimeZonesVWAPD.US_Eastern_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");	
						break;
					case TimeZonesVWAPD.US_Central_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");	
						break;
					case TimeZonesVWAPD.US_Mountain_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Mountain Standard Time");	
						break;
					case TimeZonesVWAPD.US_Pacific_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");	
						break;
					case TimeZonesVWAPD.AUS_Eastern_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");	
						break;
					case TimeZonesVWAPD.Japan_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");	
						break;
					case TimeZonesVWAPD.China_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");	
						break;
					case TimeZonesVWAPD.India_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");	
						break;
					case TimeZonesVWAPD.Central_European_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");	
						break;
					case TimeZonesVWAPD.GMT_Standard_Time:	
						customTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");	
						break;
				}					
				gap0 = (plot0Style == PlotStyle.Line || plot0Style == PlotStyle.Square);
				gap1 = (plot1Style == PlotStyle.Line || plot1Style == PlotStyle.Square);
				if(ChartBars != null)
				{	
					breakAtEOD = ChartBars.Bars.IsResetOnNewTradingDay;
					errorBrush = ChartControl.Properties.AxisPen.Brush;
					errorBrush.Freeze();
					errorFont = new SimpleFont("Arial", 24);
				}
				basicError = false;
				errorMessage = false;
				if(!calculateFromPriceData)
				{
					Draw.TextFixed(this, "error text 1", errorText1, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);  
					errorMessage = true;
					basicError = true;
				}	
				else if (!Bars.BarsType.IsIntraday)
				{
					Draw.TextFixed(this, "error text 2", errorText2, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);  
					errorMessage = true;
					basicError = true;
				}
				else if(displacement < 0)
				{
					Draw.TextFixed(this, "error text 3", errorText3, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);  
					errorMessage = true;
					basicError = true;
				}
				else if (ChartBars != null && (ChartControl.BarSpacingType == BarSpacingType.TimeBased || ChartControl.BarSpacingType == BarSpacingType.EquidistantMulti) && displacement != 0)
				{
					Draw.TextFixed(this, "error text 4", errorText4, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);  
					errorMessage = true;
					basicError = true;
				}	
				else if(!breakAtEOD)
				{
					Draw.TextFixed(this, "error text 5", errorText5, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);  
					errorMessage = true;
					basicError = true;
				}
				sundaySessionError = false;
				startEndTimeError = false;

                // Ensure timeBased is correctly set
                if (Bars != null)
                    timeBased = Bars.BarsType.IsTimeBased;
		  	}
		}

		protected override void OnBarUpdate()
		{
			if(IsFirstTickOfBar)
			{	
				if(errorMessage)
				{	
					if(basicError)
						return;
					else if(sundaySessionError)
					{	
						Draw.TextFixed(this, "error text 6", errorText6, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);
						RemoveDrawObject("error text 7");
						return;
					}	
					else if(startEndTimeError)
					{	
						Draw.TextFixed(this, "error text 7", errorText7, TextPosition.Center, errorBrush, errorFont, Brushes.Transparent, Brushes.Transparent, 0);
						return;
					}
				}	
			}

            // Performance Optimization: Cleanup old zones
            if (IsFirstTickOfBar && activeZones.Count > 0)
            {
                lock (zonesLock)
                {
                    // Remove zones that are older than maxDaysToDraw + 1 (buffer)
                    // We check activeZones[0] because it's sorted by time (oldest first)
                    while (activeZones.Count > 0)
                    {
                        if ((Time[0].Date - activeZones[0].StartTime.Date).TotalDays > maxDaysToDraw + 1)
                        {
                            activeZones.RemoveAt(0);
                        }
                        else
                        {
                            break; // First zone is valid, so all subsequent zones are valid
                        }
                    }
                }
            }
			
			if (CurrentBar == 0)
			{	
				if(IsFirstTickOfBar)
				{	
					tradingDate[0] = GetLastBarSessionDate(Time[0]);
                    
                    // Fallback if TradingDate is invalid (MinDate)
                    if (tradingDate[0] <= DateTime.MinValue.AddDays(1))
                    {
                        Print("Debug: TradingDate invalid (MinDate) at Bar 0. Attempting fallback.");
                        if (sessionIterator.ActualTradingDayExchange > DateTime.MinValue)
                            tradingDate[0] = sessionIterator.ActualTradingDayExchange;
                        else
                        {
                            if (Time[0].Hour >= 17) 
                                tradingDate[0] = Time[0].Date.AddDays(1);
                            else
                                tradingDate[0] = Time[0].Date;
                        }
                    }
					sessionBegin[0] = sessionIterator.ActualSessionBegin;
					if(applyTradingHours)
					{	
						anchorTime[0] = TimeZoneInfo.ConvertTime(tradingDate[0].Add(customSessionStart), customTimeZone, globalTimeZone);
						if(anchorTime[0] >= sessionBegin[0].AddHours(24))
							anchorTime[0] = anchorTime[0].AddHours(-24);
						else if(anchorTime[0] < sessionBegin[0])
							anchorTime[0] = anchorTime[0].AddHours(24);
						cutoffTime[0] = TimeZoneInfo.ConvertTime(tradingDate[0].Add(customSessionEnd), customTimeZone, globalTimeZone);
						if(cutoffTime[0] > sessionBegin[0].AddHours(24))
							cutoffTime[0] = cutoffTime[0].AddHours(-24);
						else if(cutoffTime[0] <= sessionBegin[0])
							cutoffTime[0] = cutoffTime[0].AddHours(24);
						if(cutoffTime[0] <= anchorTime[0])
						{
							startEndTimeError = true;
							errorMessage = true;
							return;
						}
					}	
					calcOpen[0] = false;
					initPlot[0] = false;
					firstBarOpen[0] = Open[0];
					anchorBar = false;
					sessionBar.Reset();
					currentVolSum.Reset();
					currentVWAP.Reset();
					currentSquareSum.Reset();
					sessionHigh.Reset();
					sessionLow.Reset();
					offset.Reset();
					SessionVWAP.Reset();
					UpperBand3.Reset();
					UpperBand2.Reset();
					UpperBand1.Reset();
					LowerBand1.Reset();
					LowerBand2.Reset();
					LowerBand3.Reset();
					LowerBand3.Reset();
					lock (zonesLock)
					{
						activeZones.Clear();
						allZones.Clear();
					}
				}	
				return;
			}
			if(IsFirstTickOfBar)
			{	
				if(Bars.IsFirstBarOfSession)
				{	
                    if (CurrentBar < 10) Print(string.Format("Debug: Bar={0} Time={1} ApplyHours={2} SessionType={3} AnchorTime={4}", CurrentBar, Time[0], applyTradingHours, sessionType, anchorTime[0]));
					tradingDate[0] = GetLastBarSessionDate(Time[0]);
					if(tradingDate[0].DayOfWeek == DayOfWeek.Sunday)
					{
						sundaySessionError = true; 
						errorMessage = true;
						return;
					}
					sessionBegin[0] = sessionIterator.ActualSessionBegin;
					
					// -------------------------------------------------------------------------
					// Safety Sweep: Force close any zombie zones from previous days
					// -------------------------------------------------------------------------
					for (int i = activeZones.Count - 1; i >= 0; i--)
					{
						SessionZone zone = activeZones[i];
						// If zone belongs to a different session/day than current, close it
						// If zone belongs to a different session/day than current, close it ONLY IF MITIGATED
						if (GetLastBarSessionDate(zone.StartTime) != tradingDate[0] && zone.IsMitigated)
						{
							zone.IsActive = false;
							if (zone.EndTime == DateTime.MinValue) zone.EndTime = Time[0]; // Fallback end time
							activeZones.RemoveAt(i);
						}
					}

					if(tradingDate[0] != tradingDate[1] && IsFirstTickOfBar)
					{	
						// Cleanup ALL zones from previous sessions
						for (int i = activeZones.Count - 1; i >= 0; i--)
						{
							SessionZone zone = activeZones[i];
							// Close all zones at session end ONLY IF MITIGATED
							if (zone.IsMitigated)
							{
								zone.IsActive = false;
								zone.EndTime = Time[1];
								// Draw one last time ending at Time[1] (end of previous session)
								Draw.Rectangle(this, zone.Tag, false, zone.StartTime, zone.UpperY, Time[1], zone.LowerY, Brushes.Transparent, sessionZoneBrush, sessionZoneOpacity);
								Draw.Line(this, zone.Tag + "_LineUp", false, zone.StartTime, zone.UpperY, Time[1], zone.UpperY, zoneLineBrush, DashStyleHelper.Solid, zoneLineWidth);
								Draw.Line(this, zone.Tag + "_LineLow", false, zone.StartTime, zone.LowerY, Time[1], zone.LowerY, zoneLineBrush, DashStyleHelper.Solid, zoneLineWidth);
								// Finalize Logic Lines drawing (same as zone)
								if (showLogicLines)
								{
									double zoneRange = zone.UpperY - zone.LowerY;
									double level25 = zone.LowerY + (zoneRange * 0.25);
									double level75 = zone.LowerY + (zoneRange * 0.75);
									double levelMinus25 = zone.LowerY - (zoneRange * 0.25);
									double levelPlus125 = zone.UpperY + (zoneRange * 0.25);
									string tagBase = "Logic_" + zone.Tag;

									Draw.Line(this, tagBase + "_25", false, zone.StartTime, level25, Time[1], level25, logicLineColor, logicLineStyle, logicLineWidth);
									if (showLogicLabels) Draw.Text(this, tagBase + "_25_Txt", false, "25% (Reset Long)", Time[1], level25, 0, logicLineColor, new SimpleFont("Arial", logicTextSize), TextAlignment.Right, Brushes.Transparent, Brushes.Gray, 100);

									Draw.Line(this, tagBase + "_75", false, zone.StartTime, level75, Time[1], level75, logicLineColor, logicLineStyle, logicLineWidth);
									if (showLogicLabels) Draw.Text(this, tagBase + "_75_Txt", false, "75% (Reset Short)", Time[1], level75, 0, logicLineColor, new SimpleFont("Arial", logicTextSize), TextAlignment.Right, Brushes.Transparent, Brushes.Gray, 100);

									Draw.Line(this, tagBase + "_M25", false, zone.StartTime, levelMinus25, Time[1], levelMinus25, logicLineColor, logicLineStyle, logicLineWidth);
									if (showLogicLabels) Draw.Text(this, tagBase + "_M25_Txt", false, "-25% (Conf Short)", Time[1], levelMinus25, 0, logicLineColor, new SimpleFont("Arial", logicTextSize), TextAlignment.Right, Brushes.Transparent, Brushes.Gray, 100);

									Draw.Line(this, tagBase + "_P125", false, zone.StartTime, levelPlus125, Time[1], levelPlus125, logicLineColor, logicLineStyle, logicLineWidth);
									if (showLogicLabels) Draw.Text(this, tagBase + "_P125_Txt", false, "+125% (Conf Long)", Time[1], levelPlus125, 0, logicLineColor, new SimpleFont("Arial", logicTextSize), TextAlignment.Right, Brushes.Transparent, Brushes.Gray, 100);
								}

								activeZones.RemoveAt(i);
							}
						}

						if (showSessionZones && bandType == BandTypeVWAPD.Standard_Deviation)
						{
							// Robust Duplicate Check: Ensure we haven't already added a zone for this start time
							if (activeZones.Count > 0 && activeZones[activeZones.Count - 1].StartTime == Time[1])
							{
								// Duplicate detected, skip creation
							}
							else
							{
								double up1 = UpperBand1[1];
								double low1 = LowerBand1[1];
								if (up1 > 0 && low1 > 0 && up1 > low1)
								{
								string tag = "SessionZone_" + instanceGuid + "_" + Time[1].Ticks;
								SessionZone zone = new SessionZone
								{
									StartTime = Time[1],
									UpperY = up1,
									LowerY = low1,
									Tag = tag,
									IsActive = true
								};
								
								// Phase 2: Session Start Logic
								// Check if we are opening Inside or Outside
								// Use Close[0] (current bar) as the "Open" of the session relative to the zone
								if (Close[0] > up1)
								{
									// GAP UP (LONG)
									zone.IsGapConfirmed = true;
									zone.IsGapLong = true;
									zone.BreakoutStartTime = Time[0];
								}
								else if (Close[0] < low1)
								{
									// GAP DOWN (SHORT)
									zone.IsGapConfirmed = true;
									zone.IsGapShort = true;
									zone.BreakoutStartTime = Time[0];
								}
								else
								{
									// INSIDE OPEN (ROTATIONAL)
									zone.IsRotational = true;
									double midPoint = (up1 + low1) / 2.0;
									if (Close[0] >= midPoint) zone.IsRotationalLong = true;
									else zone.IsRotationalShort = true;
								}
								
								lock (zonesLock)
								{
									// Prevent duplicates
									bool exists = false;
									for(int z=0; z<activeZones.Count; z++) 
									{ 
										if(activeZones[z].StartTime == zone.StartTime) 
										{ 
											exists = true; 
											break; 
										} 
									}
									
									if (!exists)
									{
										activeZones.Add(zone);
										allZones.Add(zone);
									}
								}
								Draw.Rectangle(this, tag, false, Time[1], up1, Time[0], low1, Brushes.Transparent, sessionZoneBrush, sessionZoneOpacity);
								Draw.Line(this, tag + "_LineUp", false, Time[1], up1, Time[0], up1, zoneLineBrush, DashStyleHelper.Solid, zoneLineWidth);
								Draw.Line(this, tag + "_LineLow", false, Time[1], low1, Time[0], low1, zoneLineBrush, DashStyleHelper.Solid, zoneLineWidth);
							}
							}
						}
						calcOpen[0] = false;
						initPlot[0] = false;
						firstBarOpen[0] = Open[0];
						if(applyTradingHours)
						{	
							anchorTime[0] = TimeZoneInfo.ConvertTime(tradingDate[0].Add(customSessionStart), customTimeZone, globalTimeZone);
							if(anchorTime[0] >= sessionBegin[0].AddHours(24))
								anchorTime[0] = anchorTime[0].AddHours(-24);
							else if(anchorTime[0] < sessionBegin[0])
								anchorTime[0] = anchorTime[0].AddHours(24);
							cutoffTime[0] = TimeZoneInfo.ConvertTime(tradingDate[0].Add(customSessionEnd), customTimeZone, globalTimeZone);
							if(cutoffTime[0] > sessionBegin[0].AddHours(24))
								cutoffTime[0] = cutoffTime[0].AddHours(-24);
							else if(cutoffTime[0] <= sessionBegin[0])
								cutoffTime[0] = cutoffTime[0].AddHours(24);
							if(cutoffTime[0] <= anchorTime[0])
							{
								startEndTimeError = true;
								errorMessage = true;
								return;
							}
						}	
					}	
					else
					{	
						calcOpen[0] = calcOpen[1];
						initPlot[0] = initPlot[1];
						firstBarOpen[0] = firstBarOpen[1];
						if(applyTradingHours)
						{	
							anchorTime[0] = anchorTime[1];
							cutoffTime[0] = cutoffTime[1];
						}	
					}	
				}	
				else
				{	
					tradingDate[0] = tradingDate[1];
					sessionBegin[0] = sessionBegin[1];
					calcOpen[0] = calcOpen[1];
					initPlot[0] = initPlot[1];
					firstBarOpen[0] = firstBarOpen[1];	
					if(applyTradingHours)
					{	
						anchorTime[0] = anchorTime[1];
						cutoffTime[0] = cutoffTime[1];
					}	
				}	
			}	
			if(applyTradingHours) 
			{
				if(timeBased && Time[0] > anchorTime[0] && Time[1] <= anchorTime[0])
					anchorBar = true;
				else if(!timeBased && Time[0] >= anchorTime[0] && Time[1] < anchorTime[0])
					anchorBar = true;
				else
					anchorBar = false;
				if(timeBased && Time[0] > cutoffTime[0] && Time[1] <= cutoffTime[0])
					calcOpen[0] = false;
				else if(!timeBased && Time[0] >= cutoffTime[0] && Time[1] < cutoffTime[0])
					calcOpen[0] = false;
			}
			
			if ((!applyTradingHours && tradingDate[0] != tradingDate[1]) || (applyTradingHours && anchorBar))
			{	
				if(IsFirstTickOfBar || !calcOpen[0])
				{	
					initPlot[0]		= true;
					sessionBar[0]	= 1;
				}	
				open				= Open[0] - firstBarOpen[0];
				high				= High[0] - firstBarOpen[0];
				low 				= Low[0] - firstBarOpen[0];
				close				= Close[0] - firstBarOpen[0];
				mean1				= 0.5*(high + low);
				mean2				= 0.5*(open + close);
				mean				= 0.5*(mean1 + mean2);
				currentVolSum[0] 	= Volume[0];
				currentVWAP[0]		= mean;
				if(bandType == BandTypeVWAPD.Standard_Deviation)
				{	
					currentSquareSum[0] = Volume[0]*(open*open + high*high + low*low + close*close + 2*mean2*mean2 + 2*mean1*mean1)/8.0;
					offset[0]			= (currentVolSum[0] > 0.5) ? Math.Sqrt(currentSquareSum[0]/currentVolSum[0] - currentVWAP[0]*currentVWAP[0]) : 0;
				}
				else if(bandType == BandTypeVWAPD.Quarter_Range)
				{	
					sessionHigh[0]	= High[0];
					sessionLow[0]	= Low[0];
					offset[0]		= 0.25*(sessionHigh[0] - sessionLow[0]);
				}	
				else
				{
					currentSquareSum.Reset();
					sessionHigh.Reset();
					sessionLow.Reset();
					offset.Reset();
				}	
				calcOpen[0] = true;
				plotVWAP = true;
			}
			else if (calcOpen[0])
			{
				if (IsFirstTickOfBar)
				{
					sessionBar[0] 	= sessionBar[1] + 1;
					priorVolSum		= currentVolSum[1];
					priorVWAP		= currentVWAP[1];
				}
				open				= Open[0] - firstBarOpen[0];
				high				= High[0] - firstBarOpen[0];
				low 				= Low[0] - firstBarOpen[0];
				close				= Close[0] - firstBarOpen[0];
				mean1				= 0.5*(high + low);
				mean2				= 0.5*(open + close);
				mean				= 0.5*(mean1 + mean2);
				currentVolSum[0]	= priorVolSum + Volume[0];
				currentVWAP[0]		= (currentVolSum[0] > 0.5 ) ? (priorVolSum*priorVWAP + Volume[0]*mean)/currentVolSum[0] : mean;
				if(bandType == BandTypeVWAPD.Standard_Deviation)
				{	
					if(IsFirstTickOfBar)
						priorSquareSum 	= currentSquareSum[1];
					currentSquareSum[0] = priorSquareSum + Volume[0]*(open*open + high*high + low*low + close*close + 2*mean2*mean2 + 2*mean1*mean1)/8.0;
					offset[0]			= (currentVolSum[0] > 0.5) ? Math.Sqrt(currentSquareSum[0]/currentVolSum[0] - currentVWAP[0]*currentVWAP[0]) : 0;
				}	
				else if(bandType == BandTypeVWAPD.Quarter_Range)
				{
					if(IsFirstTickOfBar)
					{
						priorSessionHigh = sessionHigh[1];
						priorSessionLow	= sessionLow[1];
					}
					sessionHigh[0]		= Math.Max(priorSessionHigh, High[0]);
					sessionLow[0]		= Math.Min(priorSessionLow, Low[0]);
					offset[0]			= 0.25*(sessionHigh[0] - sessionLow[0]);
				}
				else
				{
					currentSquareSum.Reset();
					sessionHigh.Reset();
					sessionLow.Reset();
					offset.Reset();
				}	
			}
			else 
			{	
				if(initPlot[0])
				{	
					if(IsFirstTickOfBar)
						sessionBar[0] = sessionBar[1] + 1;
					currentVolSum[0] = currentVolSum[1];
					currentVWAP[0] = currentVWAP[1];
					if(bandType == BandTypeVWAPD.Standard_Deviation)
					{	
						currentSquareSum[0] = currentSquareSum[1];
						offset[0] = offset[1];	
					}	
					else if(bandType == BandTypeVWAPD.Quarter_Range)	
					{	
						sessionHigh[0]	= sessionHigh[1];
						sessionLow[0]	= sessionLow[1];
						offset[0] = offset[1];
					}
					else
					{
						currentSquareSum.Reset();
						sessionHigh.Reset();
						sessionLow.Reset();
						offset.Reset();
					}	
				}	
				else if (IsFirstTickOfBar)
				{		
					sessionBar.Reset();
					currentVolSum.Reset();
					currentVWAP.Reset();
					currentSquareSum.Reset();
					sessionHigh.Reset();
					sessionLow.Reset();
					offset.Reset();
				}	
			}	

			if (plotVWAP && initPlot[0])
			{
				sessionVWAP = currentVWAP[0] + firstBarOpen[0];
				SessionVWAP[0] = sessionVWAP;
				if (bandType == BandTypeVWAPD.None)
				{
					UpperBand3.Reset();
					UpperBand2.Reset();
					UpperBand1.Reset();
					LowerBand1.Reset();
					LowerBand2.Reset();
					LowerBand3.Reset();
				}	
				else
				{
					UpperBand3[0] = sessionVWAP + multiplier3 * offset[0];
					UpperBand2[0] = sessionVWAP + multiplier2 * offset[0];
					UpperBand1[0] = sessionVWAP + multiplier1 * offset[0];
					LowerBand1[0] = sessionVWAP - multiplier1 * offset[0];
					LowerBand2[0] = sessionVWAP - multiplier2 * offset[0];
					LowerBand3[0] = sessionVWAP - multiplier3 * offset[0];
					// Calculate 0.5 SD Bands for EF Logic Targets
					Values[7][0] = sessionVWAP + 0.5 * offset[0];
					Values[8][0] = sessionVWAP - 0.5 * offset[0];
					// Calculate 1.5 SD Bands for EF Logic Targets
					Values[9][0] = sessionVWAP + 1.5 * offset[0];
					Values[10][0] = sessionVWAP - 1.5 * offset[0];
				}
				
				if (sessionBar[0] == 1 && gap0)
					PlotBrushes[0][0] = Brushes.Transparent;
				else if (SessionVWAP[0] > SessionVWAP[1])
					PlotBrushes[0][0] = upBrush;
				else if (SessionVWAP[0] < SessionVWAP[1])
					PlotBrushes[0][0] = downBrush;
				else if(sessionBar[0] == 2 && gap0)
					PlotBrushes[0][0] = upBrush;
				else
					PlotBrushes[0][0] = PlotBrushes[0][1];
				if(sessionBar[0] == 1 && gap1)
				{
					for (int i = 1; i <= 6; i++)
						PlotBrushes[i][0] = Brushes.Transparent;
				}

				// User Request: Visibility Toggles (Keep calculation active for Zones, but hide plots)
				if (!showSessionVWAPLine) PlotBrushes[0][0] = Brushes.Transparent;
				if (!showDVABands)
				{
					for (int i = 1; i <= 10; i++) PlotBrushes[i][0] = Brushes.Transparent;
				}
			}
			else
			{
				SessionVWAP.Reset();
				UpperBand3.Reset();
				UpperBand2.Reset();
				UpperBand1.Reset();
				LowerBand1.Reset();
				LowerBand2.Reset();
				LowerBand3.Reset();
			}	
			
			if (showSessionZones)
			{
				DateTime limitDate = DateTime.Now.Date.AddDays(-maxDaysToDraw);
				
				for (int i = activeZones.Count - 1; i >= 0; i--)
				{
					SessionZone zone = activeZones[i];
					if (!zone.IsActive) continue;
					
					// Performance Optimization: Skip drawing if older than MaxDaysToDraw
					if (zone.StartTime < limitDate) continue;

					Draw.Rectangle(this, zone.Tag, false, zone.StartTime, zone.UpperY, Time[0], zone.LowerY, Brushes.Transparent, sessionZoneBrush, sessionZoneOpacity);
					Draw.Line(this, zone.Tag + "_LineUp", false, zone.StartTime, zone.UpperY, Time[0], zone.UpperY, zoneLineBrush, DashStyleHelper.Solid, zoneLineWidth);
					Draw.Line(this, zone.Tag + "_LineLow", false, zone.StartTime, zone.LowerY, Time[0], zone.LowerY, zoneLineBrush, DashStyleHelper.Solid, zoneLineWidth);
					
					// Determine if this is the most recent zone (yesterday's zone relative to current bar)
					bool isMostRecent = (i == activeZones.Count - 1);
					
					string labelUp = zoneLabelUpper;
					string labelLow = zoneLabelLower;
					
					// Append date only if requested AND it's NOT the most recent zone
					if (showZoneDate && !isMostRecent)
					{
						string dateStr = " " + zone.StartTime.ToString("dd/MM/yy");
						labelUp += dateStr;
						labelLow += dateStr;
					}
					
					// DEBUG: Append Instance ID to identify source
					string debugID = " [" + instanceGuid.ToString().Substring(0, 4) + "]";
					labelUp += debugID;
					labelLow += debugID;

					// Determine Label Position
					if (isMostRecent)
					{
						// Active Current Zone: Draw at CurrentBar + Offset (Future)
						// Debug: Print offset to verify
						// if (IsFirstTickOfBar) Print("Drawing Current Zone Label with Offset: " + ZoneLabelOffsetBars);
						
						Draw.Text(this, zone.Tag + "_TextUp", false, labelUp, -ZoneLabelOffsetBars, zone.UpperY, 0, zoneTextBrush, new SimpleFont("Arial", zoneTextSize), TextAlignment.Left, Brushes.Transparent, zoneTextBackgroundBrush, zoneTextBackgroundOpacity);
						Draw.Text(this, zone.Tag + "_TextLow", false, labelLow, -ZoneLabelOffsetBars, zone.LowerY, 0, zoneTextBrush, new SimpleFont("Arial", zoneTextSize), TextAlignment.Left, Brushes.Transparent, zoneTextBackgroundBrush, zoneTextBackgroundOpacity);
					}
					else
					{
						// Historical Zone (Active or Inactive): Draw at StartTime (Fixed)
						// This prevents "floating" labels for unmitigated zones
						Draw.Text(this, zone.Tag + "_TextUp", false, labelUp, zone.StartTime, zone.UpperY, 0, zoneTextBrush, new SimpleFont("Arial", zoneTextSize), TextAlignment.Left, Brushes.Transparent, zoneTextBackgroundBrush, zoneTextBackgroundOpacity);
						Draw.Text(this, zone.Tag + "_TextLow", false, labelLow, zone.StartTime, zone.LowerY, 0, zoneTextBrush, new SimpleFont("Arial", zoneTextSize), TextAlignment.Left, Brushes.Transparent, zoneTextBackgroundBrush, zoneTextBackgroundOpacity);
					}

					double range = zone.UpperY - zone.LowerY;
					double cutoffY = zone.LowerY + (range * (zoneCutoffPercentage / 100.0));
					
					// Check if price crosses the cutoff line
					if (!zone.IsBreached && High[0] >= cutoffY && Low[0] <= cutoffY)
					{
						zone.IsBreached = true;
					}
				}
			}

			// IPB/EF Logic Implementation
			if (IsFirstTickOfBar && Bars.IsFirstBarOfSession)
			{
				currentMarketState = MarketState.Waiting;
				ipbSignalFired = false;
			}

			if (bandType == BandTypeVWAPD.Standard_Deviation && currentVolSum[0] > 0.5)
			{
				// Calculate internal levels (1.5 SD and 0.5 SD)
				double sdOffset = offset[0]; // This is 1.0 SD offset
				double vwap = SessionVWAP[0];
				
				double upper15 = vwap + (1.5 * sdOffset);
				double lower15 = vwap - (1.5 * sdOffset);
				double upper05 = vwap + (0.5 * sdOffset);
				double lower05 = vwap - (0.5 * sdOffset);
				double upper10 = UpperBand1[0]; // DVAH
				double lower10 = LowerBand1[0]; // DVAL

				// DEBUG: Global State Trace
				// REMOVED verbose trace.
				/*
				if (Time[0] >= DateTime.Now.Date.AddDays(-5)) 
				{
					string bands = " L10:" + lower10.ToString("F2") + " L05:" + lower05.ToString("F2");
					Draw.Text(this, "Trace_" + CurrentBar, CurrentBar + "\nSt:" + currentMarketState + "\nH:" + High[0] + "\nL:" + Low[0] + bands, 0, High[0] + TickSize*150, Brushes.Gray);
				}
				*/

				// Check Wait Period
				if (Time[0] < sessionBegin[0].AddMinutes(tradingStartDelay))
				{
					currentMarketState = MarketState.Waiting;
					return;
				}
				else if (currentMarketState == MarketState.Waiting)
				{
					currentMarketState = MarketState.Neutral;
				}

				// State Machine
				switch (currentMarketState)
				{
					case MarketState.Neutral:
						// Check for Imbalance Trigger
						if (High[0] >= upper15)
						{
							currentMarketState = MarketState.ImbalanceLong;
							ipbSignalFired = false;
							imbalanceEntryVWAP = SessionVWAP[0];
							imbalanceEntryBar = CurrentBar;
						}
						else if (Low[0] <= lower15)
						{
							currentMarketState = MarketState.ImbalanceShort;
							ipbSignalFired = false;
							imbalanceEntryVWAP = SessionVWAP[0];
							imbalanceEntryBar = CurrentBar;
						}
						break;
					
					case MarketState.FailedLong:
						// Check for Imbalance Trigger (Re-Imbalance)
						if (High[0] >= upper15)
						{
							currentMarketState = MarketState.ImbalanceLong;
							ipbSignalFired = false;
							imbalanceEntryVWAP = SessionVWAP[0];
							imbalanceEntryBar = CurrentBar;
						}
						// Check for Imbalance Short Trigger
						else if (Low[0] <= lower15)
						{
							currentMarketState = MarketState.ImbalanceShort;
							ipbSignalFired = false;
							imbalanceEntryVWAP = SessionVWAP[0];
							imbalanceEntryBar = CurrentBar;
						}
						// Check for Extreme Fade (EF) Short: Retest of DVAH (+1.0 SD)
						// Re-arm logic: If AllowMultipleEF is ON, and we fired, check if we went back to 0.5 SD (upper05)
						if (AllowMultipleEF && ipbSignalFired && Low[0] <= upper05) ipbSignalFired = false;
						
						// Filter: If Broad Bar (touches both 1.0 and 0.5), do not fire
						bool isBroadBarEFShort = Low[0] <= upper05;
						
						// Volatility Filter: Candle Range vs Band Width (User requested to apply to EF too)
						double bandWidthEFShort = UpperBand1[0] - LowerBand1[0];
						double candleRangeEFShort = High[0] - Low[0];
						bool volConditionEFShort = (maxCandleWidthPercent <= 0) || (candleRangeEFShort <= (bandWidthEFShort * (maxCandleWidthPercent / 100.0)));

						bool efShortCondition = ShowEF && !ipbSignalFired && Open[0] < upper10 && High[0] >= upper10;
						// Duration Filter: Check if enough time passed since Reset
						bool durationConditionShort = (CurrentBar - failureEntryBar) >= MinFailureDuration;
						
						// Update: Removed (!isBroadBarEFShort) check. We allow broad signal candles if the RESET was clean (State logic).
						if (efShortCondition && durationConditionShort && volConditionEFShort)
						{
							ipbSignalFired = true; // Latch
							efShort[0] = true;

							if (showDVABands && Time[0] >= DateTime.Now.Date.AddDays(-maxDaysToDraw))
							{
								string tag = "EF_Short_" + Time[0].Ticks;
								// Draw Line (X bars to left)
								Draw.Line(this, tag + "_Line", false, lineLengthEF, upper10, 0, upper10, lineColorEF, lineStyleEF, lineWidthEF);
								// Draw Text
								Draw.Text(this, tag + "_Text", false, textEFShort, lineLengthEF, upper10, 5, textColorEF, new SimpleFont("Arial", textSizeEF), TextAlignment.Right, Brushes.Transparent, Brushes.Black, 100).ZOrderType = DrawingToolZOrder.AlwaysDrawnLast;
							}
							
							// Reset State to Neutral to force re-confirmation (Distance Filter equivalent)
							if (!AllowMultipleEF)
							{
								currentMarketState = MarketState.Neutral;
								ipbSignalFired = false;
							}

                            if (alertOnEF) TriggerAlert("EF Short", "EF Short Signal");
						}
						// Reset Condition: Target Reached without Entry (Invalidated OR Rotation)
						else if (Low[0] <= lower10)
						{
							if (AllowRotation)
							{
								// Rotation: Full traverse to DVAL -> Treat as Imbalance Short (Bearish Control)
								// We set ipbSignalFired = true to skip the IPB Short signal and look for FailedShort next.
								currentMarketState = MarketState.ImbalanceShort;
								ipbSignalFired = true; 
								imbalanceEntryVWAP = SessionVWAP[0];
								imbalanceEntryBar = CurrentBar;
							}
							else
							{
								currentMarketState = MarketState.Neutral;
								ipbSignalFired = false;
							}
						}
						break;

					case MarketState.FailedShort:
						// Check for Imbalance Trigger (Re-Imbalance)
						if (Low[0] <= lower15)
						{
							currentMarketState = MarketState.ImbalanceShort;
							ipbSignalFired = false;
							imbalanceEntryVWAP = SessionVWAP[0];
							imbalanceEntryBar = CurrentBar;
						}
						// Check for Imbalance Long Trigger
						else if (High[0] >= upper15)
						{
							currentMarketState = MarketState.ImbalanceLong;
							ipbSignalFired = false;
							imbalanceEntryVWAP = SessionVWAP[0];
							imbalanceEntryBar = CurrentBar;
						}
						// Check for Extreme Fade (EF) Long: Retest of DVAL (-1.0 SD)
						// Re-arm logic: If AllowMultipleEF is ON, and we fired, check if we went back to -0.5 SD (lower05)
						if (AllowMultipleEF && ipbSignalFired && High[0] >= lower05) ipbSignalFired = false;

						// Filter: If Broad Bar (touches both -1.0 and -0.5), do not fire
						bool isBroadBarEFLong = High[0] >= lower05;
						
						// Volatility Filter: Candle Range vs Band Width
						double bandWidthEFLong = UpperBand1[0] - LowerBand1[0];
						double candleRangeEFLong = High[0] - Low[0];
						bool volConditionEFLong = (maxCandleWidthPercent <= 0) || (candleRangeEFLong <= (bandWidthEFLong * (maxCandleWidthPercent / 100.0)));

						bool efLongCondition = ShowEF && !ipbSignalFired && Open[0] > lower10 && Low[0] <= lower10;
						// Duration Filter: Check if enough time passed since Reset
						bool durationConditionLong = (CurrentBar - failureEntryBar) >= MinFailureDuration;

						// Update: Removed (!isBroadBarEFLong) check. We allow broad signal candles if the RESET was clean (State logic).
						
						// DEBUG: Force print reasoning
                        if (ShowDebugState) // Only if enabled
                        {
                            string reason = "EF_L Check: ";
                            if (!efLongCondition) reason += "CondFail ";
                            if (!durationConditionLong) reason += "DurFail(" + (CurrentBar - failureEntryBar) + ") ";
							if (!volConditionEFLong) reason += "VolFail(" + (candleRangeEFLong/bandWidthEFLong*100).ToString("F1") + "%) ";
							if (efLongCondition && durationConditionLong && volConditionEFLong) reason += "PASS";
                            
                            Draw.Text(this, "Dbg_EF_L_" + CurrentBar, reason, 0, Low[0] - TickSize * 5, Brushes.Yellow);
                        }

						if (efLongCondition && durationConditionLong && volConditionEFLong)
						{
							ipbSignalFired = true; // Latch
							efLong[0] = true;
							
							// Performance Optimization: Check MaxDaysToDraw
							// Performance Optimization: Check MaxDaysToDraw
							if (showDVABands && Time[0] >= DateTime.Now.Date.AddDays(-maxDaysToDraw))
							{
								string tag = "EF_Long_" + Time[0].Ticks;
								// Draw Line (X bars to left)
								Draw.Line(this, tag + "_Line", false, lineLengthEF, lower10, 0, lower10, lineColorEF, lineStyleEF, lineWidthEF);
								// Draw Text
								Draw.Text(this, tag + "_Text", false, textEFLong, lineLengthEF, lower10, -5, textColorEF, new SimpleFont("Arial", textSizeEF), TextAlignment.Right, Brushes.Transparent, Brushes.Black, 100).ZOrderType = DrawingToolZOrder.AlwaysDrawnLast;
							}
							
							// Reset State to Neutral to force re-confirmation (Distance Filter equivalent)
							if (!AllowMultipleEF)
							{
								currentMarketState = MarketState.Neutral;
								ipbSignalFired = false;
							}

                            if (alertOnEF) TriggerAlert("EF Long", "EF Long Signal");
						}
						// Reset Condition: Target Reached without Entry (Invalidated OR Rotation)
						else if (High[0] >= upper10)
						{
							if (AllowRotation)
							{
								// Rotation: Full traverse to DVAH -> Treat as Imbalance Long (Bullish Control)
								// We set ipbSignalFired = true to skip the IPB Long signal and look for FailedLong next.
								currentMarketState = MarketState.ImbalanceLong;
								ipbSignalFired = true;
								imbalanceEntryVWAP = SessionVWAP[0];
								imbalanceEntryBar = CurrentBar;
							}
							else
							{
								currentMarketState = MarketState.Neutral;
								ipbSignalFired = false;
							}
						}
						break;

					case MarketState.ImbalanceLong:
						// CRITICAL FIX: Check for Opposing Imbalance (Crash) FIRST
						if (Low[0] <= lower15)
						{
							currentMarketState = MarketState.ImbalanceShort;
							ipbSignalFired = false;
							imbalanceEntryVWAP = SessionVWAP[0];
							imbalanceEntryBar = CurrentBar;
							break; // Exit immediately
						}
						// Check for IPB Signal (Retracement to +1.0 SD)
						// Re-arm logic: If AllowMultipleIPB is ON, and we fired, check if we went back to 1.5 SD (upper15)
						if (AllowMultipleIPB && ipbSignalFired && High[0] >= upper15) ipbSignalFired = false;
						
						// Filter: If Broad Bar (touches both 1.0 and 1.5), do not fire
						// Filter: If Broad Bar (touches both 1.0 and 1.5), do not fire
						// Updated: Check Current [0] AND Previous [1] bar. Reset Level is 1.5 (upper15)
						bool isBroadBarIPBLong = High[0] >= upper15 || High[1] >= upper15;
						

						
						// Filter: VWAP Angle Check REMOVED

						// Volatility Filter: Candle Range vs Band Width
						double bandWidthLong = UpperBand1[0] - LowerBand1[0];
						double candleRangeLong = High[0] - Low[0];
						bool volConditionLong = (maxCandleWidthPercent <= 0) || (candleRangeLong <= (bandWidthLong * (maxCandleWidthPercent / 100.0)));
						
						bool ipbLongCondition = ShowIPB && !ipbSignalFired && Open[0] > upper10 && Low[0] <= upper10;
						if (ipbLongCondition && (!FilterBroadBars || !isBroadBarIPBLong) && volConditionLong)
						{
							ipbSignalFired = true;
							ipbLong[0] = true;
							
							// Performance Optimization: Check MaxDaysToDraw
							if (showDVABands && Time[0] >= DateTime.Now.Date.AddDays(-maxDaysToDraw))
							{
								string tag = "IPB_Long_" + Time[0].Ticks;
								// Draw Line (X bars to left)
								int startIdx = Math.Min(CurrentBar, lineLengthIPB);
								Draw.Line(this, tag + "_Line", false, lineLengthIPB, upper10, 0, upper10, lineColorIPB, lineStyleIPB, lineWidthIPB);
								// Draw Text
								Draw.Text(this, tag + "_Text", false, textIPBLong, lineLengthIPB, upper10, 5, textColorIPB, new SimpleFont("Arial", textSizeIPB), TextAlignment.Right, Brushes.Transparent, Brushes.Black, 100).ZOrderType = DrawingToolZOrder.AlwaysDrawnLast;
							}
							
							// Reset State to Neutral to force re-confirmation (Distance Filter equivalent)
							// FIXED: Do NOT reset to Neutral here. We must allow the state to persist 
                            // to check for "Failure" (touch of 0.5 SD) which leads to EF signal.
							// currentMarketState = MarketState.Neutral; 
							
                            if (alertOnIPB) TriggerAlert("IPB Long", "IPB Long Signal");
						}
						
						// Check for Neutralization (Reset)
						// Use the ACTUAL plot value (Values[7] -> Upper 0.5, Values[8] -> Lower 0.5)
						// User requested to use the band object as target
						// Check for Neutralization (Reset)
						// Update: Do NOT reset if it's a Broad Bar (touches Signal Level 'Values[7]' and Target Level 'upper10')
						// We want a CLEAN failure, not a whip-saw.
						// Reset Level: Values[7] (Upper 0.5)
						// Signal Level (Imbalance Long Target): Values[6] (Upper 1.0) -> 'upper10' variable
						bool isBroadBarResetLong = Low[0] <= Values[7][0] && High[0] >= Values[6][0]; // Wait, Imbalance Long target is 1.0. reset is 0.5. Price is above 1.0. 
						// Imbalance Long: Price > 1.0. Failure: Price drops < 0.5.
						// Broad Bar: High > 1.0 AND Low < 0.5.
						
						if (Low[0] <= Values[7][0])
						{
							// Only transition if NOT a broad bar (or if filter is disabled)
							if (!FilterBroadBars || !isBroadBarResetLong)
							{
								currentMarketState = MarketState.FailedLong; // Ready for EF logic later
								ipbSignalFired = false;
								failureEntryBar = CurrentBar;
	                            
	                            if (ShowDebugState) 
	                                Draw.Text(this, "Dbg_Rst_Long_" + CurrentBar, "Reset Long\nL:" + Low[0] + "\nLimit:" + Values[7][0], 0, Low[0] - TickSize*10, Brushes.White);
							}
						}
						break;

					case MarketState.ImbalanceShort:
						// CRITICAL FIX: Check for Opposing Imbalance (Rally) FIRST
						if (High[0] >= upper15)
						{
							currentMarketState = MarketState.ImbalanceLong;
							ipbSignalFired = false;
							imbalanceEntryVWAP = SessionVWAP[0];
							imbalanceEntryBar = CurrentBar;
							break; // Exit immediately
						}
						// Check for IPB Signal (Retracement to -1.0 SD)
						// Re-arm logic: If AllowMultipleIPB is ON, and we fired, check if we went back to -1.5 SD (lower15)
						if (AllowMultipleIPB && ipbSignalFired && Low[0] <= lower15) ipbSignalFired = false;

						// Filter: If Broad Bar (touches both -1.0 and -1.5), do not fire
						// Filter: If Broad Bar (touches both -1.0 and -1.5), do not fire
						// Updated: Check Current [0] AND Previous [1] bar. Reset Level is -1.5 (lower15)
						bool isBroadBarIPBShort = Low[0] <= lower15 || Low[1] <= lower15;
						

						
						// Filter: VWAP Angle Check REMOVED

						// Volatility Filter: Candle Range vs Band Width
						double bandWidthShort = UpperBand1[0] - LowerBand1[0];
						double candleRangeShort = High[0] - Low[0];
						bool volConditionShort = (maxCandleWidthPercent <= 0) || (candleRangeShort <= (bandWidthShort * (maxCandleWidthPercent / 100.0)));

						bool ipbShortCondition = ShowIPB && !ipbSignalFired && Open[0] < lower10 && High[0] >= lower10;
						if (ipbShortCondition && (!FilterBroadBars || !isBroadBarIPBShort) && volConditionShort)
						{
							ipbSignalFired = true;
							ipbShort[0] = true;
							
							// Performance Optimization: Check MaxDaysToDraw
							if (showDVABands && Time[0] >= DateTime.Now.Date.AddDays(-maxDaysToDraw))
							{
								string tag = "IPB_Short_" + Time[0].Ticks;
								// Draw Line (X bars to left)
								int startIdx = Math.Min(CurrentBar, lineLengthIPB);
								Draw.Line(this, tag + "_Line", false, lineLengthIPB, lower10, 0, lower10, lineColorIPB, lineStyleIPB, lineWidthIPB);
								// Draw Text
								Draw.Text(this, tag + "_Text", false, textIPBShort, lineLengthIPB, lower10, -5, textColorIPB, new SimpleFont("Arial", textSizeIPB), TextAlignment.Right, Brushes.Transparent, Brushes.Black, 100).ZOrderType = DrawingToolZOrder.AlwaysDrawnLast;
							}
							
							// Reset State to Neutral to force re-confirmation (Distance Filter equivalent)
							// FIXED: Do NOT reset to Neutral here.
							// currentMarketState = MarketState.Neutral;
							
                            if (alertOnIPB) TriggerAlert("IPB Short", "IPB Short Signal");
						}
						
						// Check for Neutralization (Reset)
						// Use the ACTUAL plot value (Values[8] -> Lower 0.5 for Short Reset check against High?? No wait.)
						// Imbalance Short means we went DOWN (-1.5). Reset means we went UP back to -0.5 (Lower Band 0.5)
						// Check for Neutralization (Reset)
						// Imbalance Short: Price < -1.0. Failure: Price > -0.5.
						// Broad Bar: Low < -1.0 AND High > -0.5.
						// Target: -1.0 (Values[4] aka 'lower10'). Reset: -0.5 (Values[8] aka 'lower05' wait, 'Values[8]' is lower05?)
						// Checking Init: Values[4] is lower10 (-1.0). Values[8] is lower05 (-0.5).
						
						bool isBroadBarResetShort = Low[0] <= Values[4][0] && High[0] >= Values[8][0]; 

						if (High[0] >= Values[8][0])
						{
							// Only transition if NOT a broad bar
							if (!FilterBroadBars || !isBroadBarResetShort)
							{
								currentMarketState = MarketState.FailedShort;
								ipbSignalFired = false;
								failureEntryBar = CurrentBar;
	
	                            if (ShowDebugState) 
	                                Draw.Text(this, "Dbg_Rst_Short_" + CurrentBar, "Reset SUCCESS\nH:" + High[0] + "\nLimit:" + Values[8][0], 0, High[0] + TickSize*80, Brushes.Lime);
							}
                            else if (ShowDebugState)
                            {
                                Draw.Text(this, "Dbg_Rst_Short_BLK_" + CurrentBar, "Reset BLOCKED (Broad)\nH:" + High[0] + "\nL:" + Low[0], 0, High[0] + TickSize*80, Brushes.Red);
                            }
						}
						break;
				}

				// RECORD STATE HISTORY
				debugStateHistory[0] = (int)currentMarketState;

                if (ShowDebugState)
                {
					// Only draw if state CHANGED from previous bar
					if (CurrentBar > 0 && debugStateHistory[0] != debugStateHistory[1])
					{
						string stateStr = currentMarketState.ToString();
						// Shorten the text
						stateStr = stateStr.Replace("MarketState.", "").Replace("Imbalance", "Imb").Replace("Failed", "Fail").Replace("Neutral", "Neut");
						
						// Standard format
						string displayState ="State -> " + stateStr;
						
						if (currentMarketState == MarketState.ImbalanceLong) displayState += "\nStart < " + upper05.ToString("F2");
						else if (currentMarketState == MarketState.ImbalanceShort) displayState += "\nStart > " + lower05.ToString("F2");
						else if (currentMarketState == MarketState.FailedLong) displayState += "\nReset > " + upper10.ToString("F2");
						else if (currentMarketState == MarketState.FailedShort) displayState += "\nReset < " + lower10.ToString("F2");
						
						Draw.Text(this, "StateChange_" + CurrentBar, displayState, 0, High[0] + TickSize * 50, Brushes.Cyan);
					}
                }
				}
			
				// -------------------------------------------------------------------------
				// BPB / RPB Logic (Prior Value)
				// -------------------------------------------------------------------------
				// -------------------------------------------------------------------------
				// BPB / RPB Logic (Prior Value)
				// -------------------------------------------------------------------------
				// Fix: Use Time[0] instead of DateTime.Now to support historical data/backtesting
				// Fix: Use Time[0] instead of DateTime.Now to support historical data/backtesting
				bool shouldDraw = true; // Force draw for verification 
				// Note: For drawing, we might still want to limit based on chart perspective, 
				// but for SIGNAL GENERATION we must rely on Time[0].
				
				// Removed outer date check to allow backtesting logic
				{
					// DateTime limitDate = DateTime.Now.Date.AddDays(-maxDaysToDraw); // OLD LOGIC

					for (int i = 0; i < activeZones.Count; i++)
					{
						SessionZone zone = activeZones[i];
						if (!zone.IsActive) continue;

						// Filter: Ignore zones that are too old RELATIVE TO CURRENT BAR
						// This allows signals to fire in 2024 even if we are in 2025
						if ((Time[0].Date - zone.StartTime.Date).TotalDays > maxDaysToDraw) continue;

						// -------------------------------------------------------------------------
						// PHASE 2 LOGIC IMPLEMENTATION
						// -------------------------------------------------------------------------
						
						double zoneRange = zone.UpperY - zone.LowerY;
						
						// Mitigation Logic (Using Cutoff Percentage, default 50%)
						double cutoffLevel = zone.LowerY + (zoneRange * (zoneCutoffPercentage / 100.0));
						
						// Check 1: Bar Range Intersection
						if (High[0] >= cutoffLevel && Low[0] <= cutoffLevel) 
							zone.IsMitigated = true;
						// Check 2: Close Crossing (Gap over level)
						else if (CurrentBar > 0 && ((Close[1] < cutoffLevel && Close[0] > cutoffLevel) || (Close[1] > cutoffLevel && Close[0] < cutoffLevel)))
							zone.IsMitigated = true;
						double level75 = zone.LowerY + (zoneRange * 0.75);
						double level25 = zone.LowerY + (zoneRange * 0.25);
						
						// Current Bar Status
						bool isLongBreakout = Close[0] > zone.UpperY;
						bool isShortBreakout = Close[0] < zone.LowerY;
						bool isInside = !isLongBreakout && !isShortBreakout;
						
						// -------------------------------------------------------------------------
						// 1. GAP LOGIC (Priority)
						// -------------------------------------------------------------------------
						if (zone.IsGapConfirmed)
						{
							// Strict Time Wait
							if ((Time[0] - zone.BreakoutStartTime).TotalMinutes >= breakoutMinTimeMinutes)
							{
								// Time Passed - Check Status
								if (isLongBreakout) 
								{ 
									zone.IsBreakoutLongConfirmed = true; 
									zone.ConfirmationBarNumberLong = CurrentBar;
									zone.IsGapConfirmed = false; 
								}
								else if (isShortBreakout) 
								{ 
									zone.IsBreakoutShortConfirmed = true; 
									zone.ConfirmationBarNumberShort = CurrentBar;
									zone.IsGapConfirmed = false; 
								}
								else 
								{ 
									// Failed Gap -> Rotational
									zone.IsGapConfirmed = false; 
									zone.IsRotational = true; 
								}
							}
							else if (isInside)
							{
								// Re-entered before time -> Rotational
								zone.IsGapConfirmed = false;
								zone.IsRotational = true;
							}
						}
						
						// -------------------------------------------------------------------------
						// 2. ROTATIONAL LOGIC
						// -------------------------------------------------------------------------
						if (zone.IsRotational)
						{
							// Exit Condition: Breakout Detected (Start Standard Confirmation)
							if (isLongBreakout || isShortBreakout)
							{
								zone.IsRotational = false;
								zone.BreakoutBarsCount = 1;
								zone.BreakoutStartTime = Time[0];
								// Let Standard Logic below handle confirmation
							}
							else
							{
								// Rotational Signals (Fade from Edges)
								bool allowMultiple = (acceptanceMode == AcceptanceMode.Multiple);
								
								// Short at PDVAH (UpperY)
								if (High[0] >= zone.UpperY - TickSize && Close[0] < zone.UpperY)
								{
									if (!zone.BPBShortFired || (allowMultiple && CurrentBar > zone.LastSignalBarShort + SignalCooldown))
									{
										// Use BPB/RPB visual slots for Rotational signals for now
										if (ShowRPB) 
										{
											zone.BPBShortFired = true; 
											zone.LastSignalBarShort = CurrentBar;
											rpbShort[0] = true; 
											if (shouldDraw)
											{
												string tag = "Rot_Short_" + zone.Tag + "_" + Time[0].Ticks;
												Draw.Text(this, tag, false, "Rot-Short", 0, zone.UpperY, 5, textColorRPB, new SimpleFont("Arial", textSizeBPB_RPB), TextAlignment.Center, Brushes.Transparent, Brushes.Black, 100).ZOrderType = DrawingToolZOrder.AlwaysDrawnLast;
											}
										}
									}
								}
								
								// Long at PDVAL (LowerY)
								if (Low[0] <= zone.LowerY + TickSize && Close[0] > zone.LowerY)
								{
									if (!zone.BPBLongFired || (allowMultiple && CurrentBar > zone.LastSignalBarLong + SignalCooldown))
									{
										if (ShowRPB)
										{
											zone.BPBLongFired = true;
											zone.LastSignalBarLong = CurrentBar;
											rpbLong[0] = true;
											if (shouldDraw)
											{
												string tag = "Rot_Long_" + zone.Tag + "_" + Time[0].Ticks;
												Draw.Text(this, tag, false, "Rot-Long", 0, zone.LowerY, -5, textColorRPB, new SimpleFont("Arial", textSizeBPB_RPB), TextAlignment.Center, Brushes.Transparent, Brushes.Black, 100).ZOrderType = DrawingToolZOrder.AlwaysDrawnLast;
											}
										}
									}
								}
							}
						}

						// -------------------------------------------------------------------------
						// 3. STANDARD BREAKOUT CONFIRMATION (If not Gap/Rotational)
						// -------------------------------------------------------------------------
						if (!zone.IsGapConfirmed && !zone.IsRotational)
						{
							// Standard Breakout Logic (Same as before but simplified)
							if (isLongBreakout && !zone.IsBreakoutLongConfirmed)
							{
								if (zone.BreakoutBarsCount == 0 || zone.BreakoutDirection != 1) 
								{ 
									zone.BreakoutBarsCount = 1; 
									zone.BreakoutStartTime = Time[0]; 
									zone.BreakoutDirection = 1; // Long
								}
								else zone.BreakoutBarsCount++;
								
								bool confirmed = false;
								// Time
								if (breakoutMinTimeMinutes > 0)
								{
									// Use persistent start time
									if ((Time[0] - zone.BreakoutStartTime).TotalMinutes >= breakoutMinTimeMinutes) confirmed = true;
								}
								
								// Distance
								if (acceptanceMode != AcceptanceMode.Time)
								{
									double zoneWidth = zone.UpperY - zone.LowerY;
									// Modified to check High/Low (Touch) and >=
									if (High[0] >= zone.UpperY + (zoneWidth * (breakoutConfirmationDistance / 100.0))) confirmed = true;
								}
								
								if (confirmed) 
								{ 
									zone.IsBreakoutLongConfirmed = true; 
									zone.ConfirmationBarNumberLong = CurrentBar;
									
									// Dynamic State: Reset Short/RPB if we switch to Long
									zone.IsBreakoutShortConfirmed = false;
									zone.RPBSetupActive = false;
									zone.RPBFailureDepthReached = false;
									
									// Reset Signal Latch to allow new signal for this new breakout cycle
									zone.BPBLongFired = false; 
								}
							}
							else if (isShortBreakout && !zone.IsBreakoutShortConfirmed)
							{
								if (zone.BreakoutBarsCount == 0 || zone.BreakoutDirection != -1) 
								{ 
									zone.BreakoutBarsCount = 1; 
									zone.BreakoutStartTime = Time[0]; 
									zone.BreakoutDirection = -1; // Short
								}
								else zone.BreakoutBarsCount++;
								
								bool confirmed = false;
								// Time
								if (breakoutMinTimeMinutes > 0)
								{
									if ((Time[0] - zone.BreakoutStartTime).TotalMinutes >= breakoutMinTimeMinutes) confirmed = true;
								}
								
								// Distance
								if (acceptanceMode != AcceptanceMode.Time)
								{
									double zoneWidth = zone.UpperY - zone.LowerY;
									// Modified to check Low (Touch) and <=
									if (Low[0] <= zone.LowerY - (zoneWidth * (breakoutConfirmationDistance / 100.0))) confirmed = true;
								}
								
								if (confirmed) 
								{ 
									zone.IsBreakoutShortConfirmed = true; 
									zone.ConfirmationBarNumberShort = CurrentBar;
									
									// Dynamic State: Reset Long/RPB if we switch to Short
									zone.IsBreakoutLongConfirmed = false;
									zone.RPBSetupActive = false;
									zone.RPBFailureDepthReached = false;
									
									// Reset Signal Latch to allow new signal for this new breakout cycle
									zone.BPBShortFired = false;
								}
							}
							else if (isInside)
							{
								// RPB Failure Logic (Trigger WaitingForRPB)
								// 1. Time Failure: If we were counting breakout bars but failed to confirm in time? 
								// Actually, BreakoutBarsCount resets if we go inside. So we need to track "MaxBreakoutDuration" or similar?
								// No, the user said: "If time expires (30 min) without confirmation".
								// But if we are INSIDE, we are not breaking out.
								// The logic is: If we TRIED to break out, but failed.
								// Let's use the existing "BreakoutBarsCount" before resetting it?
								// Or better: If we are inside, check if we previously had a breakout attempt that failed?
								// Simplified: If we are inside, check Depth.
								
								// Depth Failure (75% penetration from the breakout side)
								// If we were trying Long Breakout (Price > UpperY), and now we are Inside.
								// We need to know which side we failed FROM.
								// Let's assume if we are inside, we check both depths?
								
								double zoneWidth = zone.UpperY - zone.LowerY;
								double depth25 = zoneWidth * 0.25;
								
								// Check if we touched the "Deep" level relative to UpperY (for Long Failure)
								// Long Failure = Price drops below UpperY - 25% width (i.e. reaches 75% level)
								// Check if we touched the "Deep" level relative to UpperY (for Long Failure)
								// Long Failure = Price drops below UpperY - 25% width (i.e. reaches 75% level)
								if (Low[0] <= zone.UpperY - depth25) 
								{
									// Deep Failure - Reset Breakout Timer
									zone.BreakoutBarsCount = 0;
									zone.BreakoutDirection = 0;
									
									if (!zone.IsBreakoutLongConfirmed && !zone.IsBreakoutShortConfirmed) 
									{
										// Potential RPB Setup if we were trying Long
										// But we can enable it generically if price is deep inside
										zone.RPBSetupActive = true; 
									}
								}
								
								// Short Failure = Price rises above LowerY + 25% width (i.e. reaches 25% level)
								if (High[0] >= zone.LowerY + depth25)
								{
									// Deep Failure - Reset Breakout Timer
									zone.BreakoutBarsCount = 0;
									zone.BreakoutDirection = 0;
									
									if (!zone.IsBreakoutLongConfirmed && !zone.IsBreakoutShortConfirmed)
									{
										zone.RPBSetupActive = true;
									}
								}
								
								// IMPORTANT: User requested STRICT reset if Close < PVAH (Inside).
								// This disables "Shallow Re-entry" persistence.
								zone.BreakoutBarsCount = 0;
								zone.BreakoutDirection = 0;
								// We don't strictly need to reset StartTime as it gets overwritten on next breakout start,
								// but it's cleaner.
								zone.BreakoutStartTime = DateTime.MinValue;
							}
						}

						// -------------------------------------------------------------------------
						// 4. RPB SETUP LOGIC (Failure Detection)
						// -------------------------------------------------------------------------
						if (zone.IsBreakoutLongConfirmed && !zone.RPBSetupActive)
						{
							if (Close[0] < zone.UpperY) // Inside
							{
								// Check Depth (75% of zone = Lower 25%)
								// Wait, "75% of zone" usually means penetrating 75% of the way down?
								// User said: "bajar hasta el nivel del 75% de la zona (penetración del 25%)" -> This is ambiguous.
								// Usually "Retracement to 75%" means deep.
								// Let's use the user's explicit level: "nivel del 75%".
								// If UpperY is 100 and LowerY is 0. 75% level is 75.
								// Penetration of 25% from top (100 -> 75).
								// Let's use the RPBDepthPercent property.
								// Depth = Distance from Breakout Level (UpperY).
								// Target Level = UpperY - (Range * Percent/100).
								double targetLevel = zone.UpperY - (zoneRange * (rpbDepthPercent / 100.0));
								
								if (Low[0] <= targetLevel) zone.RPBFailureDepthReached = true;
								
								// Check Time
								if (zone.RPBFailureStartTime == DateTime.MinValue) zone.RPBFailureStartTime = Time[0];
								// Use same time filter as breakout for simplicity? Or add new property?
								// User said "RPB Min Time". Let's use BreakoutMinTimeMinutes for now or 15 min default?
								// User didn't specify a separate property, so I'll use BreakoutMinTimeMinutes as a proxy or hardcode a reasonable default if not added.
								// I'll use BreakoutMinTimeMinutes for now.
								bool timeMet = (Time[0] - zone.RPBFailureStartTime).TotalMinutes >= breakoutMinTimeMinutes;
								
								if (zone.RPBFailureDepthReached || timeMet)
								{
									zone.RPBSetupActive = true;
									zone.RPBFailureStartTime = DateTime.MinValue; // Reset
								}
							}
							else
							{
								zone.RPBFailureStartTime = DateTime.MinValue; // Reset if goes back out
							}
						}
						else if (zone.IsBreakoutShortConfirmed && !zone.RPBSetupActive)
						{
							if (Close[0] > zone.LowerY) // Inside
							{
								double targetLevel = zone.LowerY + (zoneRange * (rpbDepthPercent / 100.0));
								
								if (High[0] >= targetLevel) zone.RPBFailureDepthReached = true;
								
								if (zone.RPBFailureStartTime == DateTime.MinValue) zone.RPBFailureStartTime = Time[0];
								bool timeMet = (Time[0] - zone.RPBFailureStartTime).TotalMinutes >= breakoutMinTimeMinutes;
								
								if (zone.RPBFailureDepthReached || timeMet)
								{
									zone.RPBSetupActive = true;
									zone.RPBFailureStartTime = DateTime.MinValue;
								}
							}
							else
							{
								zone.RPBFailureStartTime = DateTime.MinValue;
							}
						}
						
						// (Moved Cancellation logic to end of loop to allow Signal on the breaking candle)

						// 5. SIGNAL GENERATION (BPB & RPB)
						// -------------------------------------------------------------------------
						bool allowMultipleSignals = (acceptanceMode == AcceptanceMode.Multiple);
						
						// BPB Long
						if (ShowBPB && zone.IsBreakoutLongConfirmed &&
							(!zone.BPBLongFired || (allowMultipleSignals && CurrentBar > zone.LastSignalBarLong + SignalCooldown)))
						{
							// Modified: Removed Close >= zone.UpperY condition. Fire on Touch (High/Low intersection).
							if (Low[0] <= zone.UpperY + TickSize && High[0] >= zone.UpperY - TickSize && CurrentBar > zone.ConfirmationBarNumberLong)
							{
								zone.BPBLongFired = true;
								zone.LastSignalBarLong = CurrentBar;
								bpbLong[0] = true;
								if (shouldDraw)
								{
									string tag = "BPB_Long_" + zone.Tag + "_" + Time[0].Ticks;
									Draw.Line(this, tag + "_Line", false, lineLengthBPB_RPB, zone.UpperY, 0, zone.UpperY, lineColorBPB, lineStyleBPB_RPB, lineWidthBPB_RPB);
									Draw.Text(this, tag + "_Text", false, (i < activeZones.Count - 1) ? textBPBLong.ToLower() : textBPBLong.ToUpper(), lineLengthBPB_RPB, zone.UpperY, 5, (i < activeZones.Count - 1) ? HistoricalSignalColor : Brushes.Yellow, new SimpleFont("Arial", textSizeBPB_RPB), TextAlignment.Right, Brushes.Transparent, Brushes.Black, 100).ZOrderType = DrawingToolZOrder.AlwaysDrawnLast;

								}

								
								// Reset Confirmation? No, we want to keep confirmation active for potential RPB.
								// Only reset timer if needed, but Confirm flag should stay.
								zone.BreakoutBarsCount = 0;
								zone.BreakoutStartTime = Time[0];

                                if (alertOnBPB && i == activeZones.Count - 1) TriggerAlert("BPB Long", "BPB Long Signal");
							}
						}
						
						// RPB Short (Failure of Long Breakout -> Target UpperY)
						if (ShowRPB && zone.IsBreakoutLongConfirmed && zone.RPBSetupActive &&
							(!zone.RPBShortFired || (allowMultipleSignals && CurrentBar > zone.LastSignalBarShort + SignalCooldown)))
						{
							// Trigger: Touch UpperY (Original Edge) from Inside
							// Modified: Removed Close < zone.UpperY requirement. Fire on Touch.
							if (High[0] >= zone.UpperY - TickSize)
							{
								zone.RPBShortFired = true;
								zone.LastSignalBarShort = CurrentBar;
								rpbShort[0] = true;
								if (shouldDraw)
								{
									string tag = "RPB_Short_" + zone.Tag + "_" + Time[0].Ticks;
									Draw.Line(this, tag + "_Line", false, lineLengthBPB_RPB, zone.UpperY, 0, zone.UpperY, lineColorRPB, lineStyleBPB_RPB, lineWidthBPB_RPB);
									Draw.Text(this, tag + "_Text", false, (i < activeZones.Count - 1) ? textRPBShort.ToLower() : textRPBShort.ToUpper(), lineLengthBPB_RPB, zone.UpperY, -5, (i < activeZones.Count - 1) ? HistoricalSignalColor : Brushes.Yellow, new SimpleFont("Arial", textSizeBPB_RPB), TextAlignment.Right, Brushes.Transparent, Brushes.Black, 100).ZOrderType = DrawingToolZOrder.AlwaysDrawnLast;

								}

								
								// Reset Confirmation (RPB implies failure, so we reset to force new setup)
								// Actually, if it's Single Shot, RPB firing ends the sequence anyway.
								// But for Multiple mode, we might want to keep it?
								// Let's keep consistency: Don't reset Confirmed Flag, just reset Timer.
								// zone.IsBreakoutLongConfirmed = false; // REMOVED
								zone.BreakoutBarsCount = 0;
								zone.BreakoutStartTime = Time[0];
								zone.RPBSetupActive = false; // Also reset RPB setup state

                                if (alertOnRPB && i == activeZones.Count - 1) TriggerAlert("RPB Short", "RPB Short Signal");
							}
						}
						
						// BPB Short
						if (ShowBPB && zone.IsBreakoutShortConfirmed &&
							(!zone.BPBShortFired || (allowMultipleSignals && CurrentBar > zone.LastSignalBarShort + SignalCooldown)))
						{
							// Modified: Removed Close <= zone.LowerY condition. Fire on Touch.
							if (High[0] >= zone.LowerY - TickSize && Low[0] <= zone.LowerY + TickSize && CurrentBar > zone.ConfirmationBarNumberShort)
							{
								zone.BPBShortFired = true;
								zone.LastSignalBarShort = CurrentBar;
								bpbShort[0] = true;
								if (shouldDraw)
								{
									string tag = "BPB_Short_" + zone.Tag + "_" + Time[0].Ticks;
									Draw.Text(this, tag + "_Text", false, (i < activeZones.Count - 1) ? textBPBShort.ToLower() : textBPBShort.ToUpper(), lineLengthBPB_RPB, zone.LowerY, 5, (i < activeZones.Count - 1) ? HistoricalSignalColor : Brushes.Yellow, new SimpleFont("Arial", textSizeBPB_RPB), TextAlignment.Right, Brushes.Transparent, Brushes.Black, 100).ZOrderType = DrawingToolZOrder.AlwaysDrawnLast;

								}


								// Reset Confirmation? No.
								zone.BreakoutBarsCount = 0;
								zone.BreakoutStartTime = Time[0];

                                if (alertOnBPB && i == activeZones.Count - 1) TriggerAlert("BPB Short", "BPB Short Signal");
							}
						}


						
						// RPB Long (Failure of Short Breakout)
						if (ShowRPB && zone.IsBreakoutShortConfirmed && zone.RPBSetupActive &&
							(!zone.RPBLongFired || (allowMultipleSignals && CurrentBar > zone.LastSignalBarLong + SignalCooldown)))
						{
							// Trigger: Touch LowerY from Inside
							// Modified: Removed Close > zone.LowerY requirement. Fire on Touch.
							if (Low[0] <= zone.LowerY + TickSize)
							{
								zone.RPBLongFired = true;
								zone.LastSignalBarLong = CurrentBar;
								rpbLong[0] = true;
								if (shouldDraw)
								{
									string tag = "RPB_Long_" + zone.Tag + "_" + Time[0].Ticks;
									Draw.Line(this, tag + "_Line", false, lineLengthBPB_RPB, zone.LowerY, 0, zone.LowerY, lineColorRPB, lineStyleBPB_RPB, lineWidthBPB_RPB);
									Draw.Text(this, tag + "_Text", false, (i < activeZones.Count - 1) ? textRPBLong.ToLower() : textRPBLong.ToUpper(), lineLengthBPB_RPB, zone.LowerY, 5, (i < activeZones.Count - 1) ? HistoricalSignalColor : Brushes.Yellow, new SimpleFont("Arial", textSizeBPB_RPB), TextAlignment.Right, Brushes.Transparent, Brushes.Black, 100).ZOrderType = DrawingToolZOrder.AlwaysDrawnLast;

								}


								// Reset Confirmation (RPB implies failure, so we reset to force new setup)
								// zone.IsBreakoutShortConfirmed = false; // REMOVED
								zone.BreakoutBarsCount = 0;
								zone.BreakoutStartTime = Time[0];
								zone.RPBSetupActive = false; // Also reset RPB setup state

                                if (alertOnRPB && i == activeZones.Count - 1) TriggerAlert("RPB Long", "RPB Long Signal");
							}
						}
						
						// -------------------------------------------------------------------------
						// 6. STATE UPDATES (Post-Signal)
						// -------------------------------------------------------------------------
						// Cancellation of RPB Setup (Moved here)
						if (zone.RPBSetupActive)
						{
							// If price breaks out again with strength (Close outside)
							// We check this AFTER signals, so we can fire the RPB Short on the candle that breaks out.
							if ((zone.IsBreakoutLongConfirmed && Close[0] > zone.UpperY) || 
								(zone.IsBreakoutShortConfirmed && Close[0] < zone.LowerY))
							{
								zone.RPBSetupActive = false;
								zone.RPBFailureDepthReached = false;
							}
						}

						// -------------------------------------------------------------------------
						// Logic Visualization (Debug Lines)
						// -------------------------------------------------------------------------
						if (showLogicLines && shouldDraw)
						{
                           // ...
                        }

						// Logic Visualization (Debug Lines)
						// -------------------------------------------------------------------------
						if (showLogicLines && shouldDraw)
						{
							string tagBase = "Logic_" + zone.Tag;
							
							// 25% (Reset Long)
							Draw.Line(this, tagBase + "_25", false, zone.StartTime, level25, Time[0], level25, logicLineColor, logicLineStyle, logicLineWidth);
							if (showLogicLabels) Draw.Text(this, tagBase + "_25_Txt", false, "25% (Reset Long)", Time[0], level25, 0, logicLineColor, new SimpleFont("Arial", logicTextSize), TextAlignment.Right, Brushes.Transparent, Brushes.Gray, 100);

							// 75% (Reset Short)
							Draw.Line(this, tagBase + "_75", false, zone.StartTime, level75, Time[0], level75, logicLineColor, logicLineStyle, logicLineWidth);
							if (showLogicLabels) Draw.Text(this, tagBase + "_75_Txt", false, "75% (Reset Short)", Time[0], level75, 0, logicLineColor, new SimpleFont("Arial", logicTextSize), TextAlignment.Right, Brushes.Transparent, Brushes.Gray, 100);
							
							// -25% (Confirm Short)
							double levelMinus25 = zone.LowerY - (zoneRange * 0.25);
							Draw.Line(this, tagBase + "_M25", false, zone.StartTime, levelMinus25, Time[0], levelMinus25, logicLineColor, logicLineStyle, logicLineWidth);
							if (showLogicLabels) Draw.Text(this, tagBase + "_M25_Txt", false, "-25% (Conf Short)", Time[0], levelMinus25, 0, logicLineColor, new SimpleFont("Arial", logicTextSize), TextAlignment.Right, Brushes.Transparent, Brushes.Gray, 100);

							// +125% (Confirm Long)
							double levelPlus125 = zone.UpperY + (zoneRange * 0.25);
							Draw.Line(this, tagBase + "_P125", false, zone.StartTime, levelPlus125, Time[0], levelPlus125, logicLineColor, logicLineStyle, logicLineWidth);
							if (showLogicLabels) Draw.Text(this, tagBase + "_P125_Txt", false, "+125% (Conf Long)", Time[0], levelPlus125, 0, logicLineColor, new SimpleFont("Arial", logicTextSize), TextAlignment.Right, Brushes.Transparent, Brushes.Gray, 100);
						}
						
						// -------------------------------------------------------------------------
						// Visual Debugger
						// -------------------------------------------------------------------------
						if (ShowDebugState && shouldDraw)
						{
							string stateText = "";
							if (zone.IsGapConfirmed) stateText = "GAP CONFIRMED";
							else if (zone.IsRotational) stateText = "ROTATIONAL";
							else if (zone.IsBreakoutLongConfirmed) stateText = "BREAKOUT LONG";
							else if (zone.IsBreakoutShortConfirmed) stateText = "BREAKOUT SHORT";
							else stateText = "NEUTRAL";
							
							if (zone.RPBSetupActive) stateText += " | RPB SETUP";
							else if (zone.RPBFailureDepthReached) stateText += " | RPB DEPTH";
							
							string tag = "Debug_" + zone.Tag;
							// Draw text slightly above the zone
							Draw.Text(this, tag, false, stateText, Time[0], zone.UpperY + (zoneRange * 0.5), 0, Brushes.Yellow, new SimpleFont("Arial", 10) { Bold = true }, TextAlignment.Center, Brushes.Transparent, Brushes.Black, 100);
						}

						// -------------------------------------------------------------------------
						// Update Toolbar Button (Last Zone Wins)
						// -------------------------------------------------------------------------
							// -------------------------------------------------------------------------
							// Update Toolbar Button (Last Zone Wins) - ZONE STATE
							// -------------------------------------------------------------------------
							if (i == activeZones.Count - 1)
							{
								// 1. STATUS BUTTON (Zone State)
								string btnText = "NEUTRAL";
								System.Windows.Media.Brush btnColor = ColorButtonPending;
								System.Windows.Media.Brush txtColor = ColorButtonTextPending;

								// Default to "Pending Breakout" visual if outside zone but not confirmed yet
								if (Close[0] > zone.UpperY)
								{
									btnText = "BREAKOUT LONG";
									// Keep Pending Color until confirmed
								}
								else if (Close[0] < zone.LowerY)
								{
									btnText = "BREAKOUT SHORT";
									// Keep Pending Color until confirmed
								}
								
								if (zone.IsGapConfirmed) 
								{ 
									if (zone.IsGapLong)
									{
										btnText = "GAP UP CONFIRMED"; 
										btnColor = ColorButtonGapLong; 
										txtColor = ColorButtonTextGapLong;
									}
									else
									{
										btnText = "GAP DOWN CONFIRMED"; 
										btnColor = ColorButtonGapShort; 
										txtColor = ColorButtonTextGapShort;
									}
								}
								else if (zone.IsRotational) 
								{ 
									if (zone.IsRotationalLong)
									{
										btnText = "ROTATIONAL LONG"; 
										btnColor = ColorButtonRotationalLong; 
										txtColor = ColorButtonTextRotationalLong;
									}
									else if (zone.IsRotationalShort)
									{
										btnText = "ROTATIONAL SHORT"; 
										btnColor = ColorButtonRotationalShort; 
										txtColor = ColorButtonTextRotationalShort;
									}
									else
									{
										btnText = "ROTATIONAL"; 
										btnColor = ColorButtonPending; 
										txtColor = ColorButtonTextPending;
									}
								}
								else if (zone.IsBreakoutLongConfirmed) 
								{ 
									if (zone.BPBLongFired)
										btnText = "BPB LONG";
									else
										btnText = "BPB Long Setup";
									
									btnColor = ColorButtonBreakoutLong; 
									txtColor = ColorButtonTextBreakoutLong;
								}
								else if (zone.IsBreakoutShortConfirmed) 
								{ 
									if (zone.BPBShortFired)
										btnText = "BPB SHORT";
									else
										btnText = "BPB Short Setup";
										
									btnColor = ColorButtonBreakoutShort; 
									txtColor = ColorButtonTextBreakoutShort;
								}
								
								if (zone.RPBSetupActive) 
								{ 
									// RPB Long Setup = Failure of Breakout Short -> Price returning UP
									if (zone.IsBreakoutShortConfirmed)
									{
										// Check if Signal Fired (Active Trade)
										if (zone.RPBLongFired || zone.BPBLongFired)
										{
											// Check Target Reached (PDVAH / UpperY)
											if (High[0] >= zone.UpperY)
											{
												btnText = "RPB LONG";
												btnColor = Brushes.Blue;
												txtColor = Brushes.White;
											}
											else
											{
												btnText = "RPB LONG";
												btnColor = ColorButtonRPBLong;
												txtColor = ColorButtonTextRPBLong;
											}
										}
										else
										{
											// Setup Phase (Waiting)
											btnText = "RPB Long Setup";
											btnColor = ColorButtonRPBLong;
											txtColor = ColorButtonTextRPBLong;
										}
									}
									// RPB Short Setup = Failure of Breakout Long -> Price returning DOWN
									else if (zone.IsBreakoutLongConfirmed)
									{
										// Check if Signal Fired (Active Trade)
										if (zone.RPBShortFired || zone.BPBShortFired)
										{
											// Check Target Reached (PDVAL / LowerY)
											if (Low[0] <= zone.LowerY)
											{
												btnText = "RPB SHORT";
												btnColor = Brushes.Blue;
												txtColor = Brushes.White;
											}
											else
											{
												btnText = "RPB SHORT";
												btnColor = ColorButtonRPBShort;
												txtColor = ColorButtonTextRPBShort;
											}
										}
										else
										{
											// Setup Phase (Waiting)
											btnText = "RPB Short Setup";
											btnColor = ColorButtonRPBShort;
											txtColor = ColorButtonTextRPBShort;
										}
									}
									else
									{
										btnText = "RPB SETUP";
										btnColor = ColorButtonPending;
										txtColor = ColorButtonTextPending;
									}
								}
								else if (zone.RPBFailureDepthReached) 
								{ 
									btnText += " | RPB DEPTH"; 
								}
								
								// 2. SIGNAL BUTTON (EF/IPB)
								// Default State: NEUTRAL (Always Visible)
								string signalText = "NEUTRAL";
								System.Windows.Media.Brush signalColor = ColorButtonPending;
								System.Windows.Media.Brush signalTextColor = ColorButtonTextPending;
								bool showSignalBtn = true; // Always show

								// Use persistent flag ipbSignalFired + MarketState to keep button visible
								if (ipbSignalFired)
								{
									if (currentMarketState == MarketState.FailedShort) // EF Long context
									{
										signalText = "EF LONG";
										// Check if Target Reached (DVAH)
										if (High[0] >= UpperBand1[0])
										{
											signalColor = Brushes.Blue;
											signalTextColor = Brushes.White;
										}
										else
										{
											signalColor = ColorButtonEFLong;
											signalTextColor = ColorButtonTextEFLong;
										}
										showSignalBtn = true;
									}
									else if (currentMarketState == MarketState.FailedLong) // EF Short context
									{
										signalText = "EF SHORT";
										// Check if Target Reached (DVAL)
										if (Low[0] <= LowerBand1[0])
										{
											signalColor = Brushes.Blue;
											signalTextColor = Brushes.White;
										}
										else
										{
											signalColor = ColorButtonEFShort;
											signalTextColor = ColorButtonTextEFShort;
										}
										showSignalBtn = true;
									}
									else if (currentMarketState == MarketState.ImbalanceLong) // IPB Long context
									{
										signalText = "IPB LONG";
										signalColor = ColorButtonIPBLong;
										signalTextColor = ColorButtonTextIPBLong;
										showSignalBtn = true;
									}
									else if (currentMarketState == MarketState.ImbalanceShort) // IPB Short context
									{
										signalText = "IPB SHORT";
										signalColor = ColorButtonIPBShort;
										signalTextColor = ColorButtonTextIPBShort;
										showSignalBtn = true;
									}
								}
								else
								{
									// IPB Setup Logic (Signal not fired yet, but in Imbalance State)
									if (currentMarketState == MarketState.ImbalanceLong)
									{
										signalText = "IPB Long Set UP";
										signalColor = Brushes.Lime;
										signalTextColor = Brushes.Black;
										showSignalBtn = true;
									}
									else if (currentMarketState == MarketState.ImbalanceShort)
									{
										signalText = "IPB Short Set UP";
										signalColor = Brushes.Red;
										signalTextColor = Brushes.White;
										showSignalBtn = true;
									}
									else if (currentMarketState == MarketState.FailedLong)
									{
										signalText = "EF Short Setup";
										signalColor = ColorButtonEFShort; 
										signalTextColor = ColorButtonTextEFShort;
										showSignalBtn = true;
									}
									else if (currentMarketState == MarketState.FailedShort)
									{
										signalText = "EF Long Setup";
										signalColor = ColorButtonEFLong; 
										signalTextColor = ColorButtonTextEFLong;
										showSignalBtn = true;
									}
								}

								// Update Buttons (Only if ChartControl exists and state changed)
								if (ChartControl != null)
								{
                                    // Check if Status Button state changed
                                    bool statusChanged = btnText != lastBtnText || btnColor != lastBtnColor || txtColor != lastTxtColor;
                                    
                                    // Check if Signal Button state changed
                                    bool signalChanged = signalText != lastSignalText || signalColor != lastSignalColor || signalTextColor != lastSignalTextColor || showSignalBtn != lastShowSignalBtn;

                                    if (statusChanged || signalChanged)
                                    {
                                        // Update cache
                                        lastBtnText = btnText;
                                        lastBtnColor = btnColor;
                                        lastTxtColor = txtColor;
                                        lastSignalText = signalText;
                                        lastSignalColor = signalColor;
                                        lastSignalTextColor = signalTextColor;
                                        lastShowSignalBtn = showSignalBtn;

                                        ChartControl.Dispatcher.InvokeAsync(() => 
                                        {
                                            if (statusChanged) UpdateToolbarButtonState(btnText, btnColor, txtColor);
                                            if (signalChanged) UpdateSignalButtonState(signalText, signalColor, signalTextColor, showSignalBtn);
                                        });
                                    }
								}
								
								// Update Public Properties for Market Analyzer (Always)
								StateText = btnText;
								StateColor = btnColor;
								StateTextColor = txtColor;
							}

					}
				}
			}


		


		#region Properties
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> SessionVWAP
		{
			get { return Values[0]; }
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> UpperBand3
		{
			get { return Values[1]; }
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> UpperBand2
		{
			get { return Values[2]; }
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> UpperBand1
		{
			get { return Values[3]; }
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> LowerBand1
		{
			get { return Values[4]; }
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> LowerBand2
		{
			get { return Values[5]; }
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> LowerBand3
		{
			get { return Values[6]; }
		}
		
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Select session", Description = "Select session - full session or custom session - for calculating the VWAP", GroupName = "1. VWAP Settings", Order = 0)]
		[RefreshProperties(RefreshProperties.All)] 
		public SessionTypeVWAPD SessionType
		{	
            get { return sessionType; }
            set { sessionType = value; }
		}
			
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Band type", Description = "Select formula for calculating volatility bands", GroupName = "1. VWAP Settings", Order = 1)]
 		[RefreshProperties(RefreshProperties.All)] 
		public BandTypeVWAPD BandType
		{	
            get { return bandType; }
            set { bandType = value; }
		}
			
		[NinjaScriptProperty] 
		[Display(ResourceType = typeof(Custom.Resource), Name="Select time zone", Description="Enter time zone for custom session", GroupName = "1. VWAP Settings", Order = 2)]
		public TimeZonesVWAPD CustomTZSelector
		{
			get
			{
				return customTZSelector;
			}
			set
			{
				customTZSelector = value;
			}
		}
			
		[Browsable(false)]
		[XmlIgnore]
		public TimeSpan CustomSessionStart
		{
			get { return customSessionStart;}
			set { customSessionStart = value;}
		}	
	
		[NinjaScriptProperty] 
		[Display(ResourceType = typeof(Custom.Resource), Name="Custom start time (+ h:min)", Description="Enter start time for VWAP calculation in selected time zone", GroupName = "1. VWAP Settings", Order = 3)]
		public string S_CustomSessionStart	
		{
			get 
			{ 
				return string.Format("{0:D2}:{1:D2}", customSessionStart.Hours, customSessionStart.Minutes);
			}
			set 
			{ 
				char[] delimiters = new char[] {':'};
				string[]values =((string)value).Split(delimiters, StringSplitOptions.None);
				customSessionStart = new TimeSpan(Convert.ToInt16(values[0]),Convert.ToInt16(values[1]),0);
			}
		}
	
		[Browsable(false)]
		[XmlIgnore]
		public TimeSpan CustomSessionEnd
		{
			get { return customSessionEnd;}
			set { customSessionEnd = value;}
		}	
	
		[NinjaScriptProperty] 
		[Display(ResourceType = typeof(Custom.Resource), Name="Custom end time (+ h:min)", Description="Enter end time for VWAP calculation in time zone of exchange", GroupName = "1. VWAP Settings", Order = 4)]
		public string S_CustomSessionEnd	
		{
			get 
			{ 
				return string.Format("{0:D2}:{1:D2}", customSessionEnd.Hours, customSessionEnd.Minutes);
			}
			set 
			{ 
				char[] delimiters = new char[] {':'};
				string[]values =((string)value).Split(delimiters, StringSplitOptions.None);
				customSessionEnd = new TimeSpan(Convert.ToInt16(values[0]),Convert.ToInt16(values[1]),0);
			}
		}
	
		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "SD multiplier 1", Description = "Select multiplier for inner standard deviation bands", GroupName = "1. VWAP Settings", Order = 5)]
		public double MultiplierSD1 
		{
			get { return multiplierSD1; }
			set { multiplierSD1 = value; }
		}
			
		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "SD multiplier 2", Description = "Select multiplier for central standard deviation bands", GroupName = "1. VWAP Settings", Order = 6)]
		public double MultiplierSD2
		{
			get { return multiplierSD2; }
			set { multiplierSD2 = value; }
		}
		
		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "SD multiplier 3", Description = "Select multiplier for outer standard deviation bands", GroupName = "1. VWAP Settings", Order = 7)]
		public double MultiplierSD3
		{
			get { return multiplierSD3; }
			set { multiplierSD3 = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show DVA Bands", Description = "Show/Hide DVA Bands (Calculation remains active)", GroupName = "1. VWAP Settings", Order = 8)]
		public bool ShowDVABands
		{
			get { return showDVABands; }
			set { showDVABands = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show Session VWAP", Description = "Show/Hide Session VWAP Line", GroupName = "1. VWAP Settings", Order = 9)]
		public bool ShowSessionVWAPLine
		{
			get { return showSessionVWAPLine; }
			set { showSessionVWAPLine = value; }
		}
			
		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "QR multiplier 1", Description = "Select multiplier for inner quarter range bands", GroupName = "1. VWAP Settings", Order = 8)]
		public double MultiplierQR1
		{
			get { return multiplierQR1; }
			set { multiplierQR1 = value; }
		}
			
		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "QR multiplier 2", Description = "Select multiplier for central quarter range bands", GroupName = "1. VWAP Settings", Order = 9)]
		public double MultiplierQR2
		{
			get { return multiplierQR2; }
			set { multiplierQR2 = value; }
		}
		
		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "QR multiplier 3", Description = "Select multiplier for outer quarter range bands", GroupName = "1. VWAP Settings", Order = 10)]
		public double MultiplierQR3
		{
			get { return multiplierQR3; }
			set { multiplierQR3 = value; }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Rising VWAP", Description = "Sets the color for a bullish VWAP", GroupName = "5. Plot Styles", Order = 0)]
		public System.Windows.Media.Brush UpBrush
		{ 
			get {return upBrush;}
			set {upBrush = value;}
		}

		[Browsable(false)]
		public string UpBrushSerializable
		{
			get { return Serialize.BrushToString(upBrush); }
			set { upBrush = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Falling VWAP", Description = "Sets the color for a bearish VWAP", GroupName = "5. Plot Styles", Order = 1)]
		public System.Windows.Media.Brush DownBrush
		{ 
			get {return downBrush;}
			set {downBrush = value;}
		}

		[Browsable(false)]
		public string DownBrushSerializable
		{
			get { return Serialize.BrushToString(downBrush); }
			set { downBrush = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Inner bands", Description = "Sets the color for the inner bands", GroupName = "5. Plot Styles", Order = 2)]
		public System.Windows.Media.Brush InnerBandBrush
		{ 
			get {return innerBandBrush;}
			set {innerBandBrush = value;}
		}

		[Browsable(false)]
		public string InnerBandBrushSerializable
		{
			get { return Serialize.BrushToString(innerBandBrush); }
			set { innerBandBrush = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Middle bands", Description = "Sets the color for the middle bands", GroupName = "5. Plot Styles", Order = 3)]
		public System.Windows.Media.Brush MiddleBandBrush
		{ 
			get {return middleBandBrush;}
			set {middleBandBrush = value;}
		}

		[Browsable(false)]
		public string MiddleBandBrushSerializable
		{
			get { return Serialize.BrushToString(middleBandBrush); }
			set { middleBandBrush = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Outer bands", Description = "Sets the color for the outer bands", GroupName = "5. Plot Styles", Order = 4)]
		public System.Windows.Media.Brush OuterBandBrush
		{ 
			get {return outerBandBrush;}
			set {outerBandBrush = value;}
		}

		[Browsable(false)]
		public string OuterBandBrushSerializable
		{
			get { return Serialize.BrushToString(outerBandBrush); }
			set { outerBandBrush = Serialize.StringToBrush(value); }
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plot style VWAP", Description = "Sets the plot style for the VWAP plot", GroupName = "5. Plot Styles", Order = 5)]
		public PlotStyle Plot0Style
		{	
            get { return plot0Style; }
            set { plot0Style = value; }
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Dash style VWAP", Description = "Sets the dash style for the VWAP plot", GroupName = "5. Plot Styles", Order = 6)]
		public DashStyleHelper Dash0Style
		{
			get { return dash0Style; }
			set { dash0Style = value; }
		}
		
		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plot width VWAP", Description = "Sets the plot width for the VWAP plot", GroupName = "5. Plot Styles", Order = 7)]
		public int Plot0Width
		{	
            get { return plot0Width; }
            set { plot0Width = value; }
		}
			
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plot style SD bands", Description = "Sets the plot style for the volatility bands", GroupName = "5. Plot Styles", Order = 8)]
		public PlotStyle Plot1Style
		{	
            get { return plot1Style; }
            set { plot1Style = value; }
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "Dash style SD bands", Description = "Sets the dash style for the volatility bands", GroupName = "5. Plot Styles", Order = 9)]
		public DashStyleHelper Dash1Style
		{
			get { return dash1Style; }
			set { dash1Style = value; }
		}
		
		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Plot width SD bands", Description = "Sets the plot width for the volatility bands", GroupName = "5. Plot Styles", Order = 10)]
		public int Plot1Width
		{	
            get { return plot1Width; }
            set { plot1Width = value; }
		}
		
		[Range(0, 100)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Inner bands opacity", Description = "Select channel opacity between 0 (transparent) and 100 (no opacity)", GroupName = "5. Plot Styles", Order = 11)]
        public int InnerAreaOpacity
        {
            get { return innerAreaOpacity; }
            set { innerAreaOpacity = value; }
        }
		
		[Range(0, 100)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Middle bands opacity", Description = "Select channel opacity between 0 (transparent) and 100 (no opacity)", GroupName = "5. Plot Styles", Order = 12)]
        public int MiddleAreaOpacity
        {
            get { return middleAreaOpacity; }
            set { middleAreaOpacity = value; }
        }
		
		[Range(0, 100)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Outer bands opacity", Description = "Select channel opacity between 0 (transparent) and 100 (no opacity)", GroupName = "5. Plot Styles", Order = 13)]
        public int OuterAreaOpacity
        {
            get { return outerAreaOpacity; }
            set { outerAreaOpacity = value; }
        }
		
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Release and date", Description = "Release and date", GroupName = "6. Version", Order = 0)]
		public string VersionString
		{	
            get { return versionString; }
            set { ; }
		}


		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show Session Zones", Description = "Draws a rectangle at the end of the session", GroupName = "2. Session Zones", Order = 20)]
		public bool ShowSessionZones
		{
			get { return showSessionZones; }
			set { showSessionZones = value; }
		}

		[Range(0, 100)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Zone Cutoff %", Description = "Percentage of zone penetration to cut the zone (50 = midline)", GroupName = "2. Session Zones", Order = 21)]
		public int ZoneCutoffPercentage
		{
			get { return zoneCutoffPercentage; }
			set { zoneCutoffPercentage = value; }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Zone Color", Description = "Color of the session zone rectangle", GroupName = "2. Session Zones", Order = 22)]
		public System.Windows.Media.Brush SessionZoneBrush
		{
			get { return sessionZoneBrush; }
			set { sessionZoneBrush = value; }
		}

		[Browsable(false)]
		public string SessionZoneBrushSerializable
		{
			get { return Serialize.BrushToString(sessionZoneBrush); }
			set { sessionZoneBrush = Serialize.StringToBrush(value); }
		}

		[Range(0, 100)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Zone Opacity", Description = "Opacity of the session zone rectangle", GroupName = "2. Session Zones", Order = 23)]
		public int SessionZoneOpacity
		{
			get { return sessionZoneOpacity; }
			set { sessionZoneOpacity = value; }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Line Color", Description = "Color of the zone lines", GroupName = "2. Session Zones", Order = 24)]
		public System.Windows.Media.Brush ZoneLineBrush
		{
			get { return zoneLineBrush; }
			set { zoneLineBrush = value; }
		}

		[Browsable(false)]
		public string ZoneLineBrushSerializable
		{
			get { return Serialize.BrushToString(zoneLineBrush); }
			set { zoneLineBrush = Serialize.StringToBrush(value); }
		}

		[Range(1, 10)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Line Width", Description = "Width of the zone lines", GroupName = "2. Session Zones", Order = 25)]
		public int ZoneLineWidth
		{
			get { return zoneLineWidth; }
			set { zoneLineWidth = value; }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Text Color", Description = "Color of the zone labels", GroupName = "2. Session Zones", Order = 26)]
		public System.Windows.Media.Brush ZoneTextBrush
		{
			get { return zoneTextBrush; }
			set { zoneTextBrush = value; }
		}

		[Browsable(false)]
		public string ZoneTextBrushSerializable
		{
			get { return Serialize.BrushToString(zoneTextBrush); }
			set { zoneTextBrush = Serialize.StringToBrush(value); }
		}

		[Range(1, 100)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Text Size", Description = "Size of the zone labels", GroupName = "2. Session Zones", Order = 27)]
		public int ZoneTextSize
		{
			get { return zoneTextSize; }
			set { zoneTextSize = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Upper Label", Description = "Text for the upper line", GroupName = "2. Session Zones", Order = 28)]
		public string ZoneLabelUpper
		{
			get { return zoneLabelUpper; }
			set { zoneLabelUpper = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Lower Label", Description = "Text for the lower line", GroupName = "2. Session Zones", Order = 29)]
		public string ZoneLabelLower
		{
			get { return zoneLabelLower; }
			set { zoneLabelLower = value; }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Text Background", Description = "Color of the text background", GroupName = "2. Session Zones", Order = 30)]
		public System.Windows.Media.Brush ZoneTextBackgroundBrush
		{
			get { return zoneTextBackgroundBrush; }
			set { zoneTextBackgroundBrush = value; }
		}

		[Browsable(false)]
		public string ZoneTextBackgroundBrushSerializable
		{
			get { return Serialize.BrushToString(zoneTextBackgroundBrush); }
			set { zoneTextBackgroundBrush = Serialize.StringToBrush(value); }
		}

		[Range(0, 100)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Text Opacity", Description = "Opacity of the text background", GroupName = "2. Session Zones", Order = 31)]
		public int ZoneTextBackgroundOpacity
		{
			get { return zoneTextBackgroundOpacity; }
			set { zoneTextBackgroundOpacity = value; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public int TradingStartDelay
		{
			get { return tradingStartDelay; }
			set { tradingStartDelay = value; }
		}

		[Range(1, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Max Days To Draw", Description = "Limit drawing of zones and signals to the last X days", GroupName = "3. IPB/EF Logic", Order = 41)]
		public int MaxDaysToDraw
		{
			get { return maxDaysToDraw; }
			set { maxDaysToDraw = value; }
		}

		[Range(1, 100)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "IPB Text Size", Description = "Size of the IPB signal text", GroupName = "4. IPB Visuals", Order = 50)]
		public int TextSizeIPB
		{
			get { return textSizeIPB; }
			set { textSizeIPB = value; }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "IPB Text Color", Description = "Color of the IPB signal text", GroupName = "4. IPB Visuals", Order = 51)]
		public System.Windows.Media.Brush TextColorIPB
		{
			get { return textColorIPB; }
			set { textColorIPB = value; }
		}

		[Browsable(false)]
		public string TextColorIPBSerializable
		{
			get { return Serialize.BrushToString(textColorIPB); }
			set { textColorIPB = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "IPB Line Color", Description = "Color of the IPB signal line", GroupName = "4. IPB Visuals", Order = 52)]
		public System.Windows.Media.Brush LineColorIPB
		{
			get { return lineColorIPB; }
			set { lineColorIPB = value; }
		}

		[Browsable(false)]
		public string LineColorIPBSerializable
		{
			get { return Serialize.BrushToString(lineColorIPB); }
			set { lineColorIPB = Serialize.StringToBrush(value); }
		}

		[Range(1, 100)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "IPB Line Width", Description = "Width of the IPB signal line", GroupName = "4. IPB Visuals", Order = 53)]
		public int LineWidthIPB
		{
			get { return lineWidthIPB; }
			set { lineWidthIPB = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "IPB Line Style", Description = "Dash style of the IPB signal line", GroupName = "4. IPB Visuals", Order = 54)]
		public DashStyleHelper LineStyleIPB
		{
			get { return lineStyleIPB; }
			set { lineStyleIPB = value; }
		}

		[Range(1, 100)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "IPB Line Length", Description = "Length of the IPB signal line in bars", GroupName = "4. IPB Visuals", Order = 55)]
		public int LineLengthIPB
		{
			get { return lineLengthIPB; }
			set { lineLengthIPB = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "IPB Text Long", Description = "Text to display for IPB Long signals", GroupName = "4. IPB Visuals", Order = 56)]
		public string TextIPBLong
		{
			get { return textIPBLong; }
			set { textIPBLong = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "IPB Text Short", Description = "Text to display for IPB Short signals", GroupName = "4. IPB Visuals", Order = 57)]
		public string TextIPBShort
		{
			get { return textIPBShort; }
			set { textIPBShort = value; }
		}

		[Range(1, 100)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "EF Text Size", Description = "Size of the EF signal text", GroupName = "5. EF Visuals", Order = 60)]
		public int TextSizeEF
		{
			get { return textSizeEF; }
			set { textSizeEF = value; }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "EF Text Color", Description = "Color of the EF signal text", GroupName = "5. EF Visuals", Order = 61)]
		public System.Windows.Media.Brush TextColorEF
		{
			get { return textColorEF; }
			set { textColorEF = value; }
		}

		[Browsable(false)]
		public string TextColorEFSerializable
		{
			get { return Serialize.BrushToString(textColorEF); }
			set { textColorEF = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "EF Line Color", Description = "Color of the EF signal line", GroupName = "5. EF Visuals", Order = 62)]
		public System.Windows.Media.Brush LineColorEF
		{
			get { return lineColorEF; }
			set { lineColorEF = value; }
		}

		[Browsable(false)]
		public string LineColorEFSerializable
		{
			get { return Serialize.BrushToString(lineColorEF); }
			set { lineColorEF = Serialize.StringToBrush(value); }
		}

		[Range(1, 100)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "EF Line Width", Description = "Width of the EF signal line", GroupName = "5. EF Visuals", Order = 63)]
		public int LineWidthEF
		{
			get { return lineWidthEF; }
			set { lineWidthEF = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "EF Line Style", Description = "Dash style of the EF signal line", GroupName = "5. EF Visuals", Order = 64)]
		public DashStyleHelper LineStyleEF
		{
			get { return lineStyleEF; }
			set { lineStyleEF = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "EF Text Long", Description = "Text to display for EF Long signals", GroupName = "5. EF Visuals", Order = 65)]
		public string TextEFLong
		{
			get { return textEFLong; }
			set { textEFLong = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "EF Text Short", Description = "Text to display for EF Short signals", GroupName = "5. EF Visuals", Order = 66)]
		public string TextEFShort
		{
			get { return textEFShort; }
			set { textEFShort = value; }
		}

		[Range(1, 100)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "EF Line Length", Description = "Length of the EF signal line in bars", GroupName = "5. EF Visuals", Order = 67)]
		public int LineLengthEF
		{
			get { return lineLengthEF; }
			set { lineLengthEF = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Acceptance Mode", Description = "Method to confirm breakout acceptance", GroupName = "6. BPB/RPB Logic", Order = 70)]
		public AcceptanceMode AcceptanceModeProp
		{
			get { return acceptanceMode; }
			set { acceptanceMode = value; }
		}

		[Range(1, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Breakout Confirmation Bars", Description = "Number of bars closing outside to confirm breakout", GroupName = "6. BPB/RPB Logic", Order = 71)]
		public int BreakoutConfirmationBars
		{
			get { return breakoutConfirmationBars; }
			set { breakoutConfirmationBars = value; }
		}

		[Range(0.1, double.MaxValue)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Breakout Distance %", Description = "Percentage of zone width for distance acceptance", GroupName = "6. BPB/RPB Logic", Order = 72)]
		public double BreakoutConfirmationDistance
		{
			get { return breakoutConfirmationDistance; }
			set { breakoutConfirmationDistance = value; }
		}

		[Range(1, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Breakout Min Time (Min)", Description = "Minimum time in minutes to confirm breakout", GroupName = "6. BPB/RPB Logic", Order = 73)]
		public int BreakoutMinTimeMinutes
		{
			get { return breakoutMinTimeMinutes; }
			set { breakoutMinTimeMinutes = value; }
		}


		[Range(0.1, 100.0)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "RPB Depth %", Description = "Percentage of zone depth required to confirm failure", GroupName = "6. BPB/RPB Logic", Order = 74)]
		public double RPBDepthPercent
		{
			get { return rpbDepthPercent; }
			set { rpbDepthPercent = value; }
		}
		
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show Debug State", Description = "Draw current market state on chart", GroupName = "8. Debug", Order = 100)]
		public bool ShowDebugState
		{
			get { return showDebugState; }
			set { showDebugState = value; }
		}



		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "BPB Text Long", Description = "Text for BPB Long", GroupName = "7. BPB/RPB Visuals", Order = 80)]
		public string TextBPBLong
		{
			get { return textBPBLong; }
			set { textBPBLong = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show IPB Signals", Description = "Toggle visibility of IPB signals", GroupName = "4. IPB Visuals", Order = 49)]
		public bool ShowIPB
		{
			get { return showIPB; }
			set { showIPB = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Allow Multiple IPB", Description = "Allow multiple IPB signals in the same Imbalance state", GroupName = "4. IPB Visuals", Order = 49)]
		public bool AllowMultipleIPB
		{
			get { return allowMultipleIPB; }
			set { allowMultipleIPB = value; }
		}

		[Browsable(false)] // Hidden from UI
		[XmlIgnore]
		public bool FilterBroadBars
		{
			get { return filterBroadBars; }
			set { filterBroadBars = value; }
		}



		[Range(0.01, 1000.0)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Max Candle Width % of Band", Description = "Max candle range as % of DVA Band Width (Volatility Filter)", GroupName = "4. IPB Visuals", Order = 54)]
		public double MaxCandleWidthPercent
		{
			get { return maxCandleWidthPercent; }
			set { maxCandleWidthPercent = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show RPB Signals", Description = "Toggle visibility of RPB signals", GroupName = "7. BPB/RPB Visuals", Order = 79)]
		public bool ShowRPB
		{
			get { return showRPB; }
			set { showRPB = value; }
		}
		


		[Range(1, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Signal Cooldown (Bars)", Description = "Minimum bars between signals in the same zone", GroupName = "6. BPB/RPB Logic", Order = 75)]
		public int SignalCooldown
		{
			get; set;
		} = 5;

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show EF Signals", Description = "Toggle visibility of EF signals", GroupName = "5. EF Visuals", Order = 59)]
		public bool ShowEF
		{
			get { return showEF; }
			set { showEF = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Allow Multiple EF", Description = "Allow multiple EF signals in the same Failed state", GroupName = "5. EF Visuals", Order = 59)]
		public bool AllowMultipleEF
		{
			get { return allowMultipleEF; }
			set { allowMultipleEF = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Allow Rotation Triggers", Description = "Trigger opposite Imbalance state when price traverses to opposite DVA band (e.g. Failed Long -> DVAL)", GroupName = "5. EF Visuals", Order = 60)]
		public bool AllowRotation
		{
			get { return allowRotation; }
			set { allowRotation = value; }
		}

		[Range(1, 50)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Min Failure Duration", Description = "Minimum bars after Reset (-0.5/0.5 SD) before EF signal (Default: 1)", GroupName = "5. EF Visuals", Order = 65)]
		public int MinFailureDuration
		{
			get; set;
		} = 1;



		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show BPB Signals", Description = "Toggle visibility of BPB signals", GroupName = "7. BPB/RPB Visuals", Order = 79)]
		public bool ShowBPB
		{
			get { return showBPB; }
			set { showBPB = value; }
		}



		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "BPB Text Short", Description = "Text for BPB Short", GroupName = "7. BPB/RPB Visuals", Order = 81)]
		public string TextBPBShort
		{
			get { return textBPBShort; }
			set { textBPBShort = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "RPB Text Long", Description = "Text for RPB Long", GroupName = "7. BPB/RPB Visuals", Order = 82)]
		public string TextRPBLong
		{
			get { return textRPBLong; }
			set { textRPBLong = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "RPB Text Short", Description = "Text for RPB Short", GroupName = "7. BPB/RPB Visuals", Order = 83)]
		public string TextRPBShort
		{
			get { return textRPBShort; }
			set { textRPBShort = value; }
		}

		[Range(1, 100)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Text Size", Description = "Size of BPB/RPB text", GroupName = "7. BPB/RPB Visuals", Order = 84)]
		public int TextSizeBPB_RPB
		{
			get { return textSizeBPB_RPB; }
			set { textSizeBPB_RPB = value; }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "BPB Text Color", Description = "Color of BPB text", GroupName = "7. BPB/RPB Visuals", Order = 85)]
		public System.Windows.Media.Brush TextColorBPB
		{
			get { return textColorBPB; }
			set { textColorBPB = value; }
		}

		[Browsable(false)]
		public string TextColorBPBSerializable
		{
			get { return Serialize.BrushToString(textColorBPB); }
			set { textColorBPB = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "RPB Text Color", Description = "Color of RPB text", GroupName = "7. BPB/RPB Visuals", Order = 86)]
		public System.Windows.Media.Brush TextColorRPB
		{
			get { return textColorRPB; }
			set { textColorRPB = value; }
		}

		[Browsable(false)]
		public string TextColorRPBSerializable
		{
			get { return Serialize.BrushToString(textColorRPB); }
			set { textColorRPB = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "BPB Line Color", Description = "Color of BPB line", GroupName = "7. BPB/RPB Visuals", Order = 87)]
		public System.Windows.Media.Brush LineColorBPB
		{
			get { return lineColorBPB; }
			set { lineColorBPB = value; }
		}

		[Browsable(false)]
		public string LineColorBPBSerializable
		{
			get { return Serialize.BrushToString(lineColorBPB); }
			set { lineColorBPB = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "RPB Line Color", Description = "Color of RPB line", GroupName = "7. BPB/RPB Visuals", Order = 88)]
		public System.Windows.Media.Brush LineColorRPB
		{
			get { return lineColorRPB; }
			set { lineColorRPB = value; }
		}

		[Browsable(false)]
		public string LineColorRPBSerializable
		{
			get { return Serialize.BrushToString(lineColorRPB); }
			set { lineColorRPB = Serialize.StringToBrush(value); }
		}

		[Range(1, 100)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Line Width", Description = "Width of BPB/RPB line", GroupName = "7. BPB/RPB Visuals", Order = 89)]
		public int LineWidthBPB_RPB
		{
			get { return lineWidthBPB_RPB; }
			set { lineWidthBPB_RPB = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Line Style", Description = "Style of BPB/RPB line", GroupName = "7. BPB/RPB Visuals", Order = 90)]
		public DashStyleHelper LineStyleBPB_RPB
		{
			get { return lineStyleBPB_RPB; }
			set { lineStyleBPB_RPB = value; }
		}

		[Range(1, 100)]
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Line Length", Description = "Length of BPB/RPB line", GroupName = "7. BPB/RPB Visuals", Order = 91)]
		public int LineLengthBPB_RPB
		{
			get { return lineLengthBPB_RPB; }
			set { lineLengthBPB_RPB = value; }
		}

		[Browsable(false)]
		[XmlIgnore()]
		public Series<bool> IPBLong
		{
			get { return ipbLong; }
		}

		[Browsable(false)]
		[XmlIgnore()]
		public Series<bool> IPBShort
		{
			get { return ipbShort; }
		}

		[Browsable(false)]
		[XmlIgnore()]
		public Series<bool> EFLong
		{
			get { return efLong; }
		}

		[Browsable(false)]
		[XmlIgnore()]
		public Series<bool> EFShort
		{
			get { return efShort; }
		}

		[Browsable(false)]
		[XmlIgnore()]
		public Series<bool> BPBLong
		{
			get { return bpbLong; }
		}

		[Browsable(false)]
		[XmlIgnore()]
		public Series<bool> BPBShort
		{
			get { return bpbShort; }
		}

		[Browsable(false)]
		[XmlIgnore()]
		public Series<bool> RPBLong
		{
			get { return rpbLong; }
		}

		[Browsable(false)]
		[XmlIgnore()]
		public Series<bool> RPBShort
		{
			get { return rpbShort; }
		}
		#endregion
	
		#region Miscellaneous
		
		public override string FormatPriceMarker(double price)
		{
			return Instrument.MasterInstrument.FormatPrice(Instrument.MasterInstrument.RoundToTickSize(price));
		}
		
		private DateTime GetLastBarSessionDate(DateTime time)
		{
			sessionIterator.CalculateTradingDay(time, timeBased);
			sessionDateTmp = sessionIterator.ActualTradingDayExchange;
			if(cacheSessionDate != sessionDateTmp) 
			{
				cacheSessionDate = sessionDateTmp;
				if (newSessionBarIdxArr.Count == 0 || (newSessionBarIdxArr.Count > 0 && CurrentBar > (int) newSessionBarIdxArr[newSessionBarIdxArr.Count - 1]))
						newSessionBarIdxArr.Add(CurrentBar);
			}
			return sessionDateTmp;			
		}

        private int lastAlertBar = -1;

        private void TriggerAlert(string id, string message)
        {
            // Only trigger alerts in Realtime
            if (State != State.Realtime) return;
            if (!useAlerts) return;

            // Prevent multiple alerts per bar
            if (CurrentBar <= lastAlertBar) return;
            lastAlertBar = CurrentBar;

            // Audio Alert
            string soundToPlay = !string.IsNullOrEmpty(alertSound) ? alertSound : "Alert2.wav";
            string fullPath = System.IO.Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds", soundToPlay);
            
            if (System.IO.File.Exists(fullPath))
            {
                // Force playback directly to ensure audio is heard
                try { NinjaTrader.Core.Globals.PlaySound(fullPath); } catch { }
            }

            // Keep the standard Alert for the log
            Alert(id, Priority.High, message, fullPath, 10, Brushes.Black, Brushes.White);

            // Email Alert
            if (sendEmail)
            {
                try
                {
                    string subject = "Alert: " + message;
                    string body = "Signal generated at " + Time[0] + "\n" + message;
                    string to = string.IsNullOrEmpty(emailAddress) ? "default_email@example.com" : emailAddress; // Fallback or use configured

                    if (attachScreenshot)
                    {
                        // Use Share service for screenshot if possible
                        // Note: Programmatic Share("Email") is the standard way to attach screenshot in NT8
                        // Cast null to object[] to resolve ambiguity
                        Share("Email", message, (object[])null); 
                    }
                    else
                    {
                        SendMail(to, subject, body);
                    }
                }
                catch (Exception ex)
                {
                    Print("RelativeDVAPVA: Failed to send email alert. Error: " + ex.Message);
                }
            }
        }
		
		private void InsertWpfControls()
		{
			if (statusButton != null) return;

			chartGrid = ChartControl.Parent as System.Windows.Controls.Grid;
			if (chartGrid == null) return;

			statusButton = new System.Windows.Controls.Button();
			statusButton.Content = "Initializing...";
			statusButton.Foreground = Brushes.Black;
			statusButton.Background = Brushes.LightGray;
			statusButton.Padding = new Thickness(5, 2, 5, 2);
			statusButton.HorizontalAlignment = HorizontalAlignment.Center;
			statusButton.VerticalAlignment = VerticalAlignment.Top;
			statusButton.Margin = new Thickness(0, 30, 0, 0); 
			statusButton.FontSize = 12;
			statusButton.FontWeight = FontWeights.Bold;
			statusButton.Opacity = 0.9;
			statusButton.IsHitTestVisible = false;

			chartGrid.Children.Add(statusButton);

			if (btnSignal != null) return;
			btnSignal = new System.Windows.Controls.Button();
			btnSignal.Content = "";
			btnSignal.Foreground = Brushes.Black;
			btnSignal.Background = Brushes.Transparent;
			btnSignal.Padding = new Thickness(5, 2, 5, 2);
			btnSignal.HorizontalAlignment = HorizontalAlignment.Center;
			btnSignal.VerticalAlignment = VerticalAlignment.Top;
			btnSignal.Margin = new Thickness(0, 55, 0, 0); // Position below statusButton (30 + ~25)
			btnSignal.FontSize = 12;
			btnSignal.FontWeight = FontWeights.Bold;
			btnSignal.Opacity = 0.9;
			btnSignal.IsHitTestVisible = false;
			btnSignal.Visibility = Visibility.Collapsed; // Hidden by default

			chartGrid.Children.Add(btnSignal);
		}

		private void RemoveWpfControls()
		{
			if (statusButton != null)
			{
				if (chartGrid != null)
				{
					chartGrid.Children.Remove(statusButton);
					if (btnSignal != null) chartGrid.Children.Remove(btnSignal);
				}
				statusButton = null;
				btnSignal = null;
			}
		}

		private void UpdateToolbarButtonState(string text, System.Windows.Media.Brush bgBrush, System.Windows.Media.Brush fgBrush)
		{
			if (statusButton == null) return;
			
			if (statusButton.Content.ToString() != text || statusButton.Background != bgBrush || statusButton.Foreground != fgBrush)
			{
				statusButton.Content = text;
				statusButton.Background = bgBrush;
				statusButton.Foreground = fgBrush;
			}
		}

		private void UpdateSignalButtonState(string text, System.Windows.Media.Brush bgBrush, System.Windows.Media.Brush fgBrush, bool visible)
		{
			if (btnSignal == null) return;
			
			if (visible)
			{
				if (btnSignal.Visibility != Visibility.Visible) btnSignal.Visibility = Visibility.Visible;
				if (btnSignal.Content.ToString() != text || btnSignal.Background != bgBrush || btnSignal.Foreground != fgBrush)
				{
					btnSignal.Content = text;
					btnSignal.Background = bgBrush;
					btnSignal.Foreground = fgBrush;
				}
			}
			else
			{
				if (btnSignal.Visibility != Visibility.Collapsed) btnSignal.Visibility = Visibility.Collapsed;
			}
		}

		public override void OnRenderTargetChanged()
		{
			if (innerAreaBrushDX != null)
				innerAreaBrushDX.Dispose();
			if (middleAreaBrushDX != null)
				middleAreaBrushDX.Dispose();
			if (outerAreaBrushDX != null)
				outerAreaBrushDX.Dispose();

			if (RenderTarget != null)
			{
				try
				{
					innerAreaBrushDX 	= innerAreaBrush.ToDxBrush(RenderTarget);
					middleAreaBrushDX 	= middleAreaBrush.ToDxBrush(RenderTarget);
					outerAreaBrushDX 	= outerAreaBrush.ToDxBrush(RenderTarget);
				}
				catch (Exception e) { }
			}
		}
		
		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (ChartBars == null || errorMessage || !IsVisible) return;
			
			// -------------------------------------------------------------------------
			// Sticky Zone Labels Logic
			// -------------------------------------------------------------------------
			// Thread safety
			List<SessionZone> zonesToRender = null;
			lock (zonesLock)
			{
				if (allZones != null && allZones.Count > 0)
					zonesToRender = new List<SessionZone>(allZones);
			}
			
			if (zonesToRender != null && zonesToRender.Count > 0)
			{
				// Set up text format
				SharpDX.DirectWrite.TextFormat textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal, zoneTextSize);
				textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
				textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;

				// Brush for text
				SharpDX.Direct2D1.SolidColorBrush dxBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.White);
				
				try
				{
					foreach (SessionZone zone in zonesToRender)
					{
						DateTime effectiveEnd = zone.IsActive ? DateTime.MaxValue : zone.EndTime;
						
						if (zone.StartTime > chartControl.LastTimePainted || effectiveEnd < chartControl.FirstTimePainted)
							continue;

						float yUpper = (float)chartScale.GetYByValue(zone.UpperY);
						float yLower = (float)chartScale.GetYByValue(zone.LowerY);
						
						float stickyX = (float)(chartControl.CanvasRight - 120);
						float xPos = stickyX;
						
						if (!zone.IsActive)
						{
							float naturalX = (float)chartControl.GetXByTime(zone.EndTime);
							xPos = Math.Min(naturalX, stickyX);
						}
						
						string labelUp = zoneLabelUpper;
						string labelLow = zoneLabelLower;
						
						if (ShowZoneDate)
						{
							string dateStr = " " + zone.StartTime.ToString("dd/MM/yy");
							labelUp += dateStr;
							labelLow += dateStr;
						}
						
						RenderTarget.DrawText(labelUp, textFormat, new SharpDX.RectangleF(xPos, yUpper - 10, 120, 20), dxBrush);
						RenderTarget.DrawText(labelLow, textFormat, new SharpDX.RectangleF(xPos, yLower - 10, 120, 20), dxBrush);
					}
				}
				finally
				{
					textFormat.Dispose();
					dxBrush.Dispose();
				}
			}

			int	lastBarPainted = ChartBars.ToIndex;
			if(lastBarPainted  < 0 || BarsArray[0].Count < lastBarPainted) return;

			SharpDX.Direct2D1.AntialiasMode oldAntialiasMode = RenderTarget.AntialiasMode;
			RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;
			
			bool nonEquidistant 			= (chartControl.BarSpacingType == BarSpacingType.TimeBased || chartControl.BarSpacingType == BarSpacingType.EquidistantMulti);
			int lastBarCounted				= Inputs[0].Count - 1;
			int	lastBarOnUpdate				= lastBarCounted - (Calculate == Calculate.OnBarClose ? 1 : 0);
			int	lastBarIndex				= Math.Min(lastBarPainted, lastBarOnUpdate);
			int firstBarPainted	 			= ChartBars.FromIndex;
			int firstBarIndex  	 			= Math.Max(BarsRequiredToPlot, firstBarPainted);
			int firstBarIdxToPaint  		= 0;
			int lastPlotIndex				= 0;
			int firstPlotIndex				= 0;
			int	returnBar					= 0;
			double barWidth					= chartControl.GetBarPaintWidth(chartControl.BarsArray[0]);
			int x							= 0;
			int y							= 0;
			Vector2[] cloudArray 			= new Vector2[2 * (Math.Max(0, lastBarIndex - firstBarIndex + displacement) + 1)]; 
			
			if(displacement > 0 && nonEquidistant)
				return;
			if(lastBarIndex + displacement >= firstBarIndex)
			{	
				if (displacement > 0 && lastBarIndex < lastBarOnUpdate)
					lastPlotIndex = lastBarIndex + 1;
				else if (displacement > 0 && lastBarIndex == lastBarOnUpdate)
				{	
					lastPlotIndex = lastBarIndex + displacement;
					for(int i = 0; i < displacement; i++)
					{
						x = ChartControl.GetXByBarIndex(ChartBars, lastPlotIndex);
						if(x > ChartPanel.X + ChartPanel.W + 1.5*barWidth - ChartControl.Properties.BarMarginRight)
							lastPlotIndex = lastPlotIndex - 1;
						else
							break;
					}	
				}	
				else
					lastPlotIndex = lastBarIndex;
			
				if(showBands)
				{	
					do
					{
						for (int i = newSessionBarIdxArr.Count - 1; i >= 0; i--)
						{
							int prevSessionBreakIdx = newSessionBarIdxArr[i];
							if (prevSessionBreakIdx + displacement <= lastPlotIndex)
							{
								firstBarIdxToPaint = prevSessionBreakIdx + displacement;
								break;
							}
						}
						firstPlotIndex = Math.Max(firstBarIndex, firstBarIdxToPaint);
						
						if(showDVABands && innerAreaOpacity > 0) 
						{
							SharpDX.Direct2D1.PathGeometry 	pathI;
							SharpDX.Direct2D1.GeometrySink 	sinkI;
							pathI = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
							using (pathI)
							{
								count = -1;
								for (int idx = lastPlotIndex; idx >= firstPlotIndex; idx --)	
								{
									x = ChartControl.GetXByBarIndex(ChartBars, idx);
									if(Values[3].IsValidDataPointAt(idx-displacement))
									{	
										y = chartScale.GetYByValue(UpperBand1.GetValueAt(idx - displacement));
										returnBar = idx;	
									}	
									else
									{	
										returnBar = idx + 1;
										break;
									}	
									count = count + 1;
									cloudArray[count] = new Vector2(x,y);
								}
								if (count > 0)
								{	
									for (int idx = returnBar ; idx <= lastPlotIndex; idx ++)	
									{
										x = ChartControl.GetXByBarIndex(ChartBars, idx);
										y = chartScale.GetYByValue(LowerBand1.GetValueAt(idx - displacement));   
										count = count + 1;
										cloudArray[count] = new Vector2(x,y);
									}
								}	
								sinkI = pathI.Open();
								sinkI.BeginFigure(cloudArray[0], FigureBegin.Filled);
								for (int i = 1; i <= count; i++)
									sinkI.AddLine(cloudArray[i]);
								sinkI.EndFigure(FigureEnd.Closed);
				        		sinkI.Close();
								RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			 					RenderTarget.FillGeometry(pathI, innerAreaBrushDX);
								RenderTarget.AntialiasMode = oldAntialiasMode;
							}
							pathI.Dispose();
							sinkI.Dispose();					
						}
						
						if(showDVABands && middleAreaOpacity > 0) 
						{
							SharpDX.Direct2D1.PathGeometry 	pathMU;
							SharpDX.Direct2D1.GeometrySink 	sinkMU;
							pathMU = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
							using (pathMU)
							{
								count = -1;
								for (int idx = lastPlotIndex; idx >= firstPlotIndex; idx --)	
								{
									x = ChartControl.GetXByBarIndex(ChartBars, idx);
									if(Values[2].IsValidDataPointAt(idx-displacement))
									{	
										y = chartScale.GetYByValue(UpperBand2.GetValueAt(idx - displacement));
										returnBar = idx;	
									}	
									else
									{	
										returnBar = idx + 1;
										break;
									}	
									count = count + 1;
									cloudArray[count] = new Vector2(x,y);
								}
								if (count > 0)
								{	
									for (int idx = returnBar ; idx <= lastPlotIndex; idx ++)	
									{
										x = ChartControl.GetXByBarIndex(ChartBars, idx);
										y = chartScale.GetYByValue(UpperBand1.GetValueAt(idx - displacement));   
										count = count + 1;
										cloudArray[count] = new Vector2(x,y);
									}
								}	
								sinkMU = pathMU.Open();
								sinkMU.BeginFigure(cloudArray[0], FigureBegin.Filled);
								for (int i = 1; i <= count; i++)
									sinkMU.AddLine(cloudArray[i]);
								sinkMU.EndFigure(FigureEnd.Closed);
				        		sinkMU.Close();
								RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			 					RenderTarget.FillGeometry(pathMU, middleAreaBrushDX);
								RenderTarget.AntialiasMode = oldAntialiasMode;
							}
							pathMU.Dispose();
							sinkMU.Dispose();					
							SharpDX.Direct2D1.PathGeometry 	pathML;
							SharpDX.Direct2D1.GeometrySink 	sinkML;
							pathML = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
							using (pathML)
							{
								count = -1;
								for (int idx = lastPlotIndex; idx >= firstPlotIndex; idx --)	
								{
									x = ChartControl.GetXByBarIndex(ChartBars, idx);
									if(Values[4].IsValidDataPointAt(idx-displacement))
									{	
										y = chartScale.GetYByValue(LowerBand1.GetValueAt(idx - displacement));
										returnBar = idx;	
									}	
									else
									{	
										returnBar = idx + 1;
										break;
									}	
									count = count + 1;
									cloudArray[count] = new Vector2(x,y);
								}
								if (count > 0)
								{	
									for (int idx = returnBar ; idx <= lastPlotIndex; idx ++)	
									{
										x = ChartControl.GetXByBarIndex(ChartBars, idx);
										y = chartScale.GetYByValue(LowerBand2.GetValueAt(idx - displacement));   
										count = count + 1;
										cloudArray[count] = new Vector2(x,y);
									}
								}	
								sinkML = pathML.Open();
								sinkML.BeginFigure(cloudArray[0], FigureBegin.Filled);
								for (int i = 1; i <= count; i++)
									sinkML.AddLine(cloudArray[i]);
								sinkML.EndFigure(FigureEnd.Closed);
				        		sinkML.Close();
								RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			 					RenderTarget.FillGeometry(pathML, middleAreaBrushDX);
								RenderTarget.AntialiasMode = oldAntialiasMode;
							}
							pathML.Dispose();
							sinkML.Dispose();						
						}					
						
						if(showDVABands && outerAreaOpacity > 0) 
						{
							SharpDX.Direct2D1.PathGeometry 	pathOU;
							SharpDX.Direct2D1.GeometrySink 	sinkOU;
							pathOU = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
							using (pathOU)
							{
								count = -1;
								for (int idx = lastPlotIndex; idx >= firstPlotIndex; idx --)	
								{
									x = ChartControl.GetXByBarIndex(ChartBars, idx);
									if(Values[1].IsValidDataPointAt(idx-displacement))
									{	
										y = chartScale.GetYByValue(UpperBand3.GetValueAt(idx - displacement));
										returnBar = idx;	
									}	
									else
									{	
										returnBar = idx + 1;
										break;
									}	
									count = count + 1;
									cloudArray[count] = new Vector2(x,y);
								}
								if (count > 0)
								{	
									for (int idx = returnBar ; idx <= lastPlotIndex; idx ++)	
									{
										x = ChartControl.GetXByBarIndex(ChartBars, idx);
										y = chartScale.GetYByValue(UpperBand2.GetValueAt(idx - displacement));   
										count = count + 1;
										cloudArray[count] = new Vector2(x,y);
									}
								}	
								sinkOU = pathOU.Open();
								sinkOU.BeginFigure(cloudArray[0], FigureBegin.Filled);
								for (int i = 1; i <= count; i++)
									sinkOU.AddLine(cloudArray[i]);
								sinkOU.EndFigure(FigureEnd.Closed);
				        		sinkOU.Close();
								RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			 					RenderTarget.FillGeometry(pathOU, outerAreaBrushDX);
								RenderTarget.AntialiasMode = oldAntialiasMode;
							}
							pathOU.Dispose();
							sinkOU.Dispose();					
							SharpDX.Direct2D1.PathGeometry 	pathOL;
							SharpDX.Direct2D1.GeometrySink 	sinkOL;
							pathOL = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
							using (pathOL)
							{
								count = -1;
								for (int idx = lastPlotIndex; idx >= firstPlotIndex; idx --)	
								{
									x = ChartControl.GetXByBarIndex(ChartBars, idx);
									if(Values[5].IsValidDataPointAt(idx-displacement))
									{	
										y = chartScale.GetYByValue(LowerBand2.GetValueAt(idx - displacement));
										returnBar = idx;	
									}	
									else
									{	
										returnBar = idx + 1;
										break;
									}	
									count = count + 1;
									cloudArray[count] = new Vector2(x,y);
								}
								if (count > 0)
								{	
									for (int idx = returnBar ; idx <= lastPlotIndex; idx ++)	
									{
										x = ChartControl.GetXByBarIndex(ChartBars, idx);
										y = chartScale.GetYByValue(LowerBand3.GetValueAt(idx - displacement));   
										count = count + 1;
										cloudArray[count] = new Vector2(x,y);
									}
								}	
								sinkOL = pathOL.Open();
								sinkOL.BeginFigure(cloudArray[0], FigureBegin.Filled);
								for (int i = 1; i <= count; i++)
									sinkOL.AddLine(cloudArray[i]);
								sinkOL.EndFigure(FigureEnd.Closed);
				        		sinkOL.Close();
								RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			 					RenderTarget.FillGeometry(pathOL, outerAreaBrushDX);
								RenderTarget.AntialiasMode = oldAntialiasMode;
							}
							pathOL.Dispose();
							sinkOL.Dispose();						
						}					
						if(lastPlotIndex < firstPlotIndex)
							lastPlotIndex = 0;
						else
							lastPlotIndex = firstPlotIndex - 1;
					}	
					while (lastPlotIndex > firstBarIndex);
				}
		
			}
			RenderTarget.AntialiasMode = oldAntialiasMode;
			base.OnRender(chartControl, chartScale);
		}

		[Display(Name="Show Logic Lines", Order=80, GroupName="Visual")]
		[XmlIgnore]
		public bool ShowLogicLines
		{
			get { return showLogicLines; }
			set { showLogicLines = value; }
		}

		[Display(Name="Show Logic Labels", Order=81, GroupName="Visual")]
		[XmlIgnore]
		public bool ShowLogicLabels
		{
			get { return showLogicLabels; }
			set { showLogicLabels = value; }
		}
		
		[Display(Name="Show Zone Date", Description="Show date (dd/MM/yy) next to zone labels", Order=82, GroupName="Visual")]
		[XmlIgnore]
		public bool ShowZoneDate
		{
			get { return showZoneDate; }
			set { showZoneDate = value; }
		}

		[Range(0, int.MaxValue)]
		[Display(Name="Separación Etiqueta Zona (Velas)", Description="Separación horizontal para las etiquetas de la zona actual", Order=83, GroupName="Visual")]
		public int ZoneLabelOffsetBars
		{ get; set; } = 10;

		[Browsable(false)]
		public string ShowZoneDateSerializable
		{
			get { return showZoneDate.ToString(); }
			set { showZoneDate = bool.Parse(value); }
		}
		
		[Browsable(false)]
		public string ShowLogicLinesSerializable
		{
			get { return showLogicLines.ToString(); }
			set { showLogicLines = bool.Parse(value); }
		}

		[Browsable(false)]
		public string ShowLogicLabelsSerializable
		{
			get { return showLogicLabels.ToString(); }
			set { showLogicLabels = bool.Parse(value); }
		}

		[XmlIgnore]
		[Display(Name="Color Gap Long", Description="Button color for Gap Long state", Order=1, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonGapLong
		{
			get { return colorButtonGapLong; }
			set { colorButtonGapLong = value; }
		}
		[Browsable(false)]
		public string ColorButtonGapLongSerializable
		{
			get { return Serialize.BrushToString(colorButtonGapLong); }
			set { colorButtonGapLong = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Color Gap Short", Description="Button color for Gap Short state", Order=2, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonGapShort
		{
			get { return colorButtonGapShort; }
			set { colorButtonGapShort = value; }
		}
		[Browsable(false)]
		public string ColorButtonGapShortSerializable
		{
			get { return Serialize.BrushToString(colorButtonGapShort); }
			set { colorButtonGapShort = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Color Rotational Long", Description="Button color for Rotational Long state", Order=3, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonRotationalLong
		{
			get { return colorButtonRotationalLong; }
			set { colorButtonRotationalLong = value; }
		}
		[Browsable(false)]
		public string ColorButtonRotationalLongSerializable
		{
			get { return Serialize.BrushToString(colorButtonRotationalLong); }
			set { colorButtonRotationalLong = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Color Rotational Short", Description="Button color for Rotational Short state", Order=4, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonRotationalShort
		{
			get { return colorButtonRotationalShort; }
			set { colorButtonRotationalShort = value; }
		}
		[Browsable(false)]
		public string ColorButtonRotationalShortSerializable
		{
			get { return Serialize.BrushToString(colorButtonRotationalShort); }
			set { colorButtonRotationalShort = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Color Breakout Long", Description="Button color for Breakout Long state", Order=5, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonBreakoutLong
		{
			get { return colorButtonBreakoutLong; }
			set { colorButtonBreakoutLong = value; }
		}
		[Browsable(false)]
		public string ColorButtonBreakoutLongSerializable
		{
			get { return Serialize.BrushToString(colorButtonBreakoutLong); }
			set { colorButtonBreakoutLong = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Color Breakout Short", Description="Button color for Breakout Short state", Order=6, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonBreakoutShort
		{
			get { return colorButtonBreakoutShort; }
			set { colorButtonBreakoutShort = value; }
		}
		[Browsable(false)]
		public string ColorButtonBreakoutShortSerializable
		{
			get { return Serialize.BrushToString(colorButtonBreakoutShort); }
			set { colorButtonBreakoutShort = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Color RPB Long Setup", Description="Button color for RPB Long Setup (Green)", Order=7, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonRPBLong
		{
			get { return colorButtonRPBLong; }
			set { colorButtonRPBLong = value; }
		}
		[Browsable(false)]
		public string ColorButtonRPBLongSerializable
		{
			get { return Serialize.BrushToString(colorButtonRPBLong); }
			set { colorButtonRPBLong = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Color RPB Short Setup", Description="Button color for RPB Short Setup (Red)", Order=8, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonRPBShort
		{
			get { return colorButtonRPBShort; }
			set { colorButtonRPBShort = value; }
		}
		[Browsable(false)]
		public string ColorButtonRPBShortSerializable
		{
			get { return Serialize.BrushToString(colorButtonRPBShort); }
			set { colorButtonRPBShort = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Color Neutral", Description="Button color for Neutral state", Order=9, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonNeutral
		{
			get { return colorButtonNeutral; }
			set { colorButtonNeutral = value; }
		}
		[Browsable(false)]
		public string ColorButtonNeutralSerializable
		{
			get { return Serialize.BrushToString(colorButtonNeutral); }
			set { colorButtonNeutral = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Text Color Gap Long", Description="Button text color for Gap Long state", Order=10, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonTextGapLong
		{
			get { return colorButtonTextGapLong; }
			set { colorButtonTextGapLong = value; }
		}
		[Browsable(false)]
		public string ColorButtonTextGapLongSerializable
		{
			get { return Serialize.BrushToString(colorButtonTextGapLong); }
			set { colorButtonTextGapLong = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Text Color Gap Short", Description="Button text color for Gap Short state", Order=11, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonTextGapShort
		{
			get { return colorButtonTextGapShort; }
			set { colorButtonTextGapShort = value; }
		}
		[Browsable(false)]
		public string ColorButtonTextGapShortSerializable
		{
			get { return Serialize.BrushToString(colorButtonTextGapShort); }
			set { colorButtonTextGapShort = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Text Color Rotational Long", Description="Button text color for Rotational Long state", Order=12, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonTextRotationalLong
		{
			get { return colorButtonTextRotationalLong; }
			set { colorButtonTextRotationalLong = value; }
		}
		[Browsable(false)]
		public string ColorButtonTextRotationalLongSerializable
		{
			get { return Serialize.BrushToString(colorButtonTextRotationalLong); }
			set { colorButtonTextRotationalLong = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Text Color Rotational Short", Description="Button text color for Rotational Short state", Order=13, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonTextRotationalShort
		{
			get { return colorButtonTextRotationalShort; }
			set { colorButtonTextRotationalShort = value; }
		}
		[Browsable(false)]
        [NinjaScriptProperty]
		public string ColorButtonTextRotationalShortSerializable
		{
			get { return Serialize.BrushToString(colorButtonTextRotationalShort); }
			set { colorButtonTextRotationalShort = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Text Color Breakout Long", Description="Button text color for Breakout Long state", Order=14, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonTextBreakoutLong
		{
			get { return colorButtonTextBreakoutLong; }
			set { colorButtonTextBreakoutLong = value; }
		}
		[Browsable(false)]
        [NinjaScriptProperty]
		public string ColorButtonTextBreakoutLongSerializable
		{
			get { return Serialize.BrushToString(colorButtonTextBreakoutLong); }
			set { colorButtonTextBreakoutLong = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Text Color Breakout Short", Description="Button text color for Breakout Short state", Order=15, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonTextBreakoutShort
		{
			get { return colorButtonTextBreakoutShort; }
			set { colorButtonTextBreakoutShort = value; }
		}
		[Browsable(false)]
        [NinjaScriptProperty]
		public string ColorButtonTextBreakoutShortSerializable
		{
			get { return Serialize.BrushToString(colorButtonTextBreakoutShort); }
			set { colorButtonTextBreakoutShort = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Text Color RPB Long Setup", Description="Button text color for RPB Long Setup", Order=16, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonTextRPBLong
		{
			get { return colorButtonTextRPBLong; }
			set { colorButtonTextRPBLong = value; }
		}
		[Browsable(false)]
        [NinjaScriptProperty]
		public string ColorButtonTextRPBLongSerializable
		{
			get { return Serialize.BrushToString(colorButtonTextRPBLong); }
			set { colorButtonTextRPBLong = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Text Color RPB Short Setup", Description="Button text color for RPB Short Setup", Order=17, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonTextRPBShort
		{
			get { return colorButtonTextRPBShort; }
			set { colorButtonTextRPBShort = value; }
		}
		[Browsable(false)]
        [NinjaScriptProperty]
		public string ColorButtonTextRPBShortSerializable
		{
			get { return Serialize.BrushToString(colorButtonTextRPBShort); }
			set { colorButtonTextRPBShort = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Text Color Neutral", Description="Button text color for Neutral state", Order=18, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonTextNeutral
		{
			get { return colorButtonTextNeutral; }
			set { colorButtonTextNeutral = value; }
		}
		[Browsable(false)]
        [NinjaScriptProperty]
		public string ColorButtonTextNeutralSerializable
		{
			get { return Serialize.BrushToString(colorButtonTextNeutral); }
			set { colorButtonTextNeutral = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Color EF Long", Description="Button color for EF Long state", Order=19, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonEFLong
		{
			get { return colorButtonEFLong; }
			set { colorButtonEFLong = value; }
		}
		[Browsable(false)]
        [NinjaScriptProperty]
		public string ColorButtonEFLongSerializable
		{
			get { return Serialize.BrushToString(colorButtonEFLong); }
			set { colorButtonEFLong = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Color EF Short", Description="Button color for EF Short state", Order=20, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonEFShort
		{
			get { return colorButtonEFShort; }
			set { colorButtonEFShort = value; }
		}
		[Browsable(false)]
        [NinjaScriptProperty]
		public string ColorButtonEFShortSerializable
		{
			get { return Serialize.BrushToString(colorButtonEFShort); }
			set { colorButtonEFShort = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Color IPB Long", Description="Button color for IPB Long state", Order=21, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonIPBLong
		{
			get { return colorButtonIPBLong; }
			set { colorButtonIPBLong = value; }
		}
		[Browsable(false)]
        [NinjaScriptProperty]
		public string ColorButtonIPBLongSerializable
		{
			get { return Serialize.BrushToString(colorButtonIPBLong); }
			set { colorButtonIPBLong = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Color IPB Short", Description="Button color for IPB Short state", Order=22, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonIPBShort
		{
			get { return colorButtonIPBShort; }
			set { colorButtonIPBShort = value; }
		}
		[Browsable(false)]
        [NinjaScriptProperty]
		public string ColorButtonIPBShortSerializable
		{
			get { return Serialize.BrushToString(colorButtonIPBShort); }
			set { colorButtonIPBShort = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Text Color EF Long", Description="Button text color for EF Long state", Order=23, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonTextEFLong
		{
			get { return colorButtonTextEFLong; }
			set { colorButtonTextEFLong = value; }
		}
		[Browsable(false)]
        [NinjaScriptProperty]
		public string ColorButtonTextEFLongSerializable
		{
			get { return Serialize.BrushToString(colorButtonTextEFLong); }
			set { colorButtonTextEFLong = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Text Color EF Short", Description="Button text color for EF Short state", Order=24, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonTextEFShort
		{
			get { return colorButtonTextEFShort; }
			set { colorButtonTextEFShort = value; }
		}
		[Browsable(false)]
        [NinjaScriptProperty]
		public string ColorButtonTextEFShortSerializable
		{
			get { return Serialize.BrushToString(colorButtonTextEFShort); }
			set { colorButtonTextEFShort = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Text Color IPB Long", Description="Button text color for IPB Long state", Order=25, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonTextIPBLong
		{
			get { return colorButtonTextIPBLong; }
			set { colorButtonTextIPBLong = value; }
		}
		[Browsable(false)]
        [NinjaScriptProperty]
		public string ColorButtonTextIPBLongSerializable
		{
			get { return Serialize.BrushToString(colorButtonTextIPBLong); }
			set { colorButtonTextIPBLong = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Text Color IPB Short", Description="Button text color for IPB Short state", Order=26, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonTextIPBShort
		{
			get { return colorButtonTextIPBShort; }
			set { colorButtonTextIPBShort = value; }
		}
		[Browsable(false)]
        [NinjaScriptProperty]
		public string ColorButtonTextIPBShortSerializable
		{
			get { return Serialize.BrushToString(colorButtonTextIPBShort); }
			set { colorButtonTextIPBShort = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Color Pending", Description="Button color for Pending/Waiting states", Order=27, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonPending
		{
			get { return colorButtonPending; }
			set { colorButtonPending = value; }
		}
		[Browsable(false)]
        [NinjaScriptProperty]
		public string ColorButtonPendingSerializable
		{
			get { return Serialize.BrushToString(colorButtonPending); }
			set { colorButtonPending = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Text Color Pending", Description="Button text color for Pending/Waiting states", Order=28, GroupName="Visual - Button")]
		public System.Windows.Media.Brush ColorButtonTextPending
		{
			get { return colorButtonTextPending; }
			set { colorButtonTextPending = value; }
		}
		[Browsable(false)]
        [NinjaScriptProperty]
		public string ColorButtonTextPendingSerializable
		{
			get { return Serialize.BrushToString(colorButtonTextPending); }
			set { colorButtonTextPending = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Historical Signal Color", Description="Color for historical signals (older than previous day)", Order=29, GroupName="Visual")]
		public System.Windows.Media.Brush HistoricalSignalColor
		{
			get { return historicalSignalColor; }
			set { historicalSignalColor = value; }
		}
		[Browsable(false)]
        [NinjaScriptProperty]
		public string HistoricalSignalColorSerializable
		{
			get { return Serialize.BrushToString(historicalSignalColor); }
			set { historicalSignalColor = Serialize.StringToBrush(value); }
		}

        #region Alerts
        [NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Use Alerts", Description = "Master switch for all alerts", GroupName = "9. Alerts", Order = 1)]
		public bool UseAlerts
		{
			get { return useAlerts; }
			set { useAlerts = value; }
		}

        [NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Alert on BPB", Description = "Trigger alert on Breakout Penetration Buy/Sell", GroupName = "9. Alerts", Order = 2)]
		public bool AlertOnBPB
		{
			get { return alertOnBPB; }
			set { alertOnBPB = value; }
		}

        [NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Alert on RPB", Description = "Trigger alert on Retracement Penetration Buy/Sell", GroupName = "9. Alerts", Order = 3)]
		public bool AlertOnRPB
		{
			get { return alertOnRPB; }
			set { alertOnRPB = value; }
		}

        [NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Alert on IPB", Description = "Trigger alert on Imbalance Penetration Buy/Sell", GroupName = "9. Alerts", Order = 4)]
		public bool AlertOnIPB
		{
			get { return alertOnIPB; }
			set { alertOnIPB = value; }
		}

        [NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Alert on EF", Description = "Trigger alert on Extreme Fade Buy/Sell", GroupName = "9. Alerts", Order = 5)]
		public bool AlertOnEF
		{
			get { return alertOnEF; }
			set { alertOnEF = value; }
		}

        [NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Alert Sound File", Description = "Sound file to play (e.g. Alert2.wav)", GroupName = "9. Alerts", Order = 6)]
		public string AlertSound
		{
			get { return alertSound; }
			set { alertSound = value; }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Send Email", Description = "Send email on alert (requires SMTP setup)", GroupName = "9. Alerts", Order = 7)]
		public bool SendEmail
		{
			get { return sendEmail; }
			set { sendEmail = value; }
		}

        [NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Email Address", Description = "Destination email (leave empty to use default)", GroupName = "9. Alerts", Order = 8)]
		public string EmailAddress
		{
			get { return emailAddress; }
			set { emailAddress = value; }
		}

        [NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Attach Screenshot", Description = "Attach chart screenshot to email (if supported)", GroupName = "9. Alerts", Order = 9)]
		public bool AttachScreenshot
		{
			get { return attachScreenshot; }
			set { attachScreenshot = value; }
		}
        #endregion

        [XmlIgnore]
        [Display(Name = "Band 0.5 SD Color", Description = "Color for the 0.5 SD bands (Inner-most)", Order = 4, GroupName = "Plot Colors")]
        public System.Windows.Media.Brush Band05Brush
        {
            get { return band05Brush; }
            set { band05Brush = value; }
        }
        [Browsable(false)]
        public string Band05BrushSerializable
        {
            get { return Serialize.BrushToString(band05Brush); }
            set { band05Brush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Band 1.5 SD Color", Description = "Color for the 1.5 SD bands (Imbalance Trigger)", Order = 5, GroupName = "Plot Colors")]
        public System.Windows.Media.Brush Band15Brush
        {
            get { return band15Brush; }
            set { band15Brush = value; }
        }
        [Browsable(false)]
        public string Band15BrushSerializable
        {
            get { return Serialize.BrushToString(band15Brush); }
            set { band15Brush = Serialize.StringToBrush(value); }
        }

		#endregion
	}
}

namespace NinjaTrader.NinjaScript.Indicators
{		
	public class RelativeDVAPVA_v2TypeConverter : NinjaTrader.NinjaScript.IndicatorBaseConverter
	{
		public override bool GetPropertiesSupported(ITypeDescriptorContext context) { return true; }

		public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object value, Attribute[] attributes)
		{
			PropertyDescriptorCollection propertyDescriptorCollection = base.GetPropertiesSupported(context) ? base.GetProperties(context, value, attributes) : TypeDescriptor.GetProperties(value, attributes);

			RelativeDVAPVA_v2			thisVWAPInstance			= (RelativeDVAPVA_v2) value;
			SessionTypeVWAPD			sessionTypeFromInstance		= thisVWAPInstance.SessionType;
			BandTypeVWAPD			bandTypeFromInstance		= thisVWAPInstance.BandType;
		
			PropertyDescriptorCollection adjusted = new PropertyDescriptorCollection(null);
			
			// Custom Property Filtering Logic
			foreach (PropertyDescriptor thisDescriptor in propertyDescriptorCollection)
			{
				// Always allow standard properties
                // Add logic here if needed to filter properties dynamically
				adjusted.Add(thisDescriptor);
			}
            return adjusted;
        }
	}
}

#region Global Enums

public enum SessionTypeVWAPD 
{
	Full_Session, 
	Custom_Hours
}

public enum BandTypeVWAPD 
{
	Standard_Deviation,
	Quarter_Range,
	None
} 

public enum TimeZonesVWAPD
{
	Exchange_Time, 
	Chart_Time, 
	US_Eastern_Standard_Time, 
	US_Central_Standard_Time, 
	US_Mountain_Standard_Time, 
	US_Pacific_Standard_Time, 
	AUS_Eastern_Standard_Time, 
	Japan_Standard_Time, 
	China_Standard_Time, 
	India_Standard_Time, 
	Central_European_Time, 
	GMT_Standard_Time
}

	public enum AcceptanceMode
	{
		Time,
		Distance,
		Any,
		Multiple
	} 
#endregion

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RelativeIndicators.RelativeDVAPVA_v2[] cacheRelativeDVAPVA_v2;
		public RelativeIndicators.RelativeDVAPVA_v2 RelativeDVAPVA_v2(SessionTypeVWAPD sessionType, BandTypeVWAPD bandType, TimeZonesVWAPD customTZSelector, string s_CustomSessionStart, string s_CustomSessionEnd, double multiplierSD1, double multiplierSD2, double multiplierSD3, bool showDVABands, bool showSessionVWAPLine, double multiplierQR1, double multiplierQR2, double multiplierQR3, bool showSessionZones, int zoneCutoffPercentage, int sessionZoneOpacity, int zoneLineWidth, int zoneTextSize, string zoneLabelUpper, string zoneLabelLower, int zoneTextBackgroundOpacity, int maxDaysToDraw, int textSizeIPB, int lineWidthIPB, DashStyleHelper lineStyleIPB, int lineLengthIPB, string textIPBLong, string textIPBShort, int textSizeEF, int lineWidthEF, DashStyleHelper lineStyleEF, string textEFLong, string textEFShort, int lineLengthEF, AcceptanceMode acceptanceModeProp, int breakoutConfirmationBars, double breakoutConfirmationDistance, int breakoutMinTimeMinutes, double rPBDepthPercent, bool showDebugState, string textBPBLong, bool showIPB, bool allowMultipleIPB, double maxCandleWidthPercent, bool showRPB, int signalCooldown, bool showEF, bool allowMultipleEF, bool allowRotation, int minFailureDuration, bool showBPB, string textBPBShort, string textRPBLong, string textRPBShort, int textSizeBPB_RPB, int lineWidthBPB_RPB, DashStyleHelper lineStyleBPB_RPB, int lineLengthBPB_RPB, string colorButtonTextRotationalShortSerializable, string colorButtonTextBreakoutLongSerializable, string colorButtonTextBreakoutShortSerializable, string colorButtonTextRPBLongSerializable, string colorButtonTextRPBShortSerializable, string colorButtonTextNeutralSerializable, string colorButtonEFLongSerializable, string colorButtonEFShortSerializable, string colorButtonIPBLongSerializable, string colorButtonIPBShortSerializable, string colorButtonTextEFLongSerializable, string colorButtonTextEFShortSerializable, string colorButtonTextIPBLongSerializable, string colorButtonTextIPBShortSerializable, string colorButtonPendingSerializable, string colorButtonTextPendingSerializable, string historicalSignalColorSerializable, bool useAlerts, bool alertOnBPB, bool alertOnRPB, bool alertOnIPB, bool alertOnEF, string alertSound, bool sendEmail, string emailAddress, bool attachScreenshot)
		{
			return RelativeDVAPVA_v2(Input, sessionType, bandType, customTZSelector, s_CustomSessionStart, s_CustomSessionEnd, multiplierSD1, multiplierSD2, multiplierSD3, showDVABands, showSessionVWAPLine, multiplierQR1, multiplierQR2, multiplierQR3, showSessionZones, zoneCutoffPercentage, sessionZoneOpacity, zoneLineWidth, zoneTextSize, zoneLabelUpper, zoneLabelLower, zoneTextBackgroundOpacity, maxDaysToDraw, textSizeIPB, lineWidthIPB, lineStyleIPB, lineLengthIPB, textIPBLong, textIPBShort, textSizeEF, lineWidthEF, lineStyleEF, textEFLong, textEFShort, lineLengthEF, acceptanceModeProp, breakoutConfirmationBars, breakoutConfirmationDistance, breakoutMinTimeMinutes, rPBDepthPercent, showDebugState, textBPBLong, showIPB, allowMultipleIPB, maxCandleWidthPercent, showRPB, signalCooldown, showEF, allowMultipleEF, allowRotation, minFailureDuration, showBPB, textBPBShort, textRPBLong, textRPBShort, textSizeBPB_RPB, lineWidthBPB_RPB, lineStyleBPB_RPB, lineLengthBPB_RPB, colorButtonTextRotationalShortSerializable, colorButtonTextBreakoutLongSerializable, colorButtonTextBreakoutShortSerializable, colorButtonTextRPBLongSerializable, colorButtonTextRPBShortSerializable, colorButtonTextNeutralSerializable, colorButtonEFLongSerializable, colorButtonEFShortSerializable, colorButtonIPBLongSerializable, colorButtonIPBShortSerializable, colorButtonTextEFLongSerializable, colorButtonTextEFShortSerializable, colorButtonTextIPBLongSerializable, colorButtonTextIPBShortSerializable, colorButtonPendingSerializable, colorButtonTextPendingSerializable, historicalSignalColorSerializable, useAlerts, alertOnBPB, alertOnRPB, alertOnIPB, alertOnEF, alertSound, sendEmail, emailAddress, attachScreenshot);
		}

		public RelativeIndicators.RelativeDVAPVA_v2 RelativeDVAPVA_v2(ISeries<double> input, SessionTypeVWAPD sessionType, BandTypeVWAPD bandType, TimeZonesVWAPD customTZSelector, string s_CustomSessionStart, string s_CustomSessionEnd, double multiplierSD1, double multiplierSD2, double multiplierSD3, bool showDVABands, bool showSessionVWAPLine, double multiplierQR1, double multiplierQR2, double multiplierQR3, bool showSessionZones, int zoneCutoffPercentage, int sessionZoneOpacity, int zoneLineWidth, int zoneTextSize, string zoneLabelUpper, string zoneLabelLower, int zoneTextBackgroundOpacity, int maxDaysToDraw, int textSizeIPB, int lineWidthIPB, DashStyleHelper lineStyleIPB, int lineLengthIPB, string textIPBLong, string textIPBShort, int textSizeEF, int lineWidthEF, DashStyleHelper lineStyleEF, string textEFLong, string textEFShort, int lineLengthEF, AcceptanceMode acceptanceModeProp, int breakoutConfirmationBars, double breakoutConfirmationDistance, int breakoutMinTimeMinutes, double rPBDepthPercent, bool showDebugState, string textBPBLong, bool showIPB, bool allowMultipleIPB, double maxCandleWidthPercent, bool showRPB, int signalCooldown, bool showEF, bool allowMultipleEF, bool allowRotation, int minFailureDuration, bool showBPB, string textBPBShort, string textRPBLong, string textRPBShort, int textSizeBPB_RPB, int lineWidthBPB_RPB, DashStyleHelper lineStyleBPB_RPB, int lineLengthBPB_RPB, string colorButtonTextRotationalShortSerializable, string colorButtonTextBreakoutLongSerializable, string colorButtonTextBreakoutShortSerializable, string colorButtonTextRPBLongSerializable, string colorButtonTextRPBShortSerializable, string colorButtonTextNeutralSerializable, string colorButtonEFLongSerializable, string colorButtonEFShortSerializable, string colorButtonIPBLongSerializable, string colorButtonIPBShortSerializable, string colorButtonTextEFLongSerializable, string colorButtonTextEFShortSerializable, string colorButtonTextIPBLongSerializable, string colorButtonTextIPBShortSerializable, string colorButtonPendingSerializable, string colorButtonTextPendingSerializable, string historicalSignalColorSerializable, bool useAlerts, bool alertOnBPB, bool alertOnRPB, bool alertOnIPB, bool alertOnEF, string alertSound, bool sendEmail, string emailAddress, bool attachScreenshot)
		{
			if (cacheRelativeDVAPVA_v2 != null)
				for (int idx = 0; idx < cacheRelativeDVAPVA_v2.Length; idx++)
					if (cacheRelativeDVAPVA_v2[idx] != null && cacheRelativeDVAPVA_v2[idx].SessionType == sessionType && cacheRelativeDVAPVA_v2[idx].BandType == bandType && cacheRelativeDVAPVA_v2[idx].CustomTZSelector == customTZSelector && cacheRelativeDVAPVA_v2[idx].S_CustomSessionStart == s_CustomSessionStart && cacheRelativeDVAPVA_v2[idx].S_CustomSessionEnd == s_CustomSessionEnd && cacheRelativeDVAPVA_v2[idx].MultiplierSD1 == multiplierSD1 && cacheRelativeDVAPVA_v2[idx].MultiplierSD2 == multiplierSD2 && cacheRelativeDVAPVA_v2[idx].MultiplierSD3 == multiplierSD3 && cacheRelativeDVAPVA_v2[idx].ShowDVABands == showDVABands && cacheRelativeDVAPVA_v2[idx].ShowSessionVWAPLine == showSessionVWAPLine && cacheRelativeDVAPVA_v2[idx].MultiplierQR1 == multiplierQR1 && cacheRelativeDVAPVA_v2[idx].MultiplierQR2 == multiplierQR2 && cacheRelativeDVAPVA_v2[idx].MultiplierQR3 == multiplierQR3 && cacheRelativeDVAPVA_v2[idx].ShowSessionZones == showSessionZones && cacheRelativeDVAPVA_v2[idx].ZoneCutoffPercentage == zoneCutoffPercentage && cacheRelativeDVAPVA_v2[idx].SessionZoneOpacity == sessionZoneOpacity && cacheRelativeDVAPVA_v2[idx].ZoneLineWidth == zoneLineWidth && cacheRelativeDVAPVA_v2[idx].ZoneTextSize == zoneTextSize && cacheRelativeDVAPVA_v2[idx].ZoneLabelUpper == zoneLabelUpper && cacheRelativeDVAPVA_v2[idx].ZoneLabelLower == zoneLabelLower && cacheRelativeDVAPVA_v2[idx].ZoneTextBackgroundOpacity == zoneTextBackgroundOpacity && cacheRelativeDVAPVA_v2[idx].MaxDaysToDraw == maxDaysToDraw && cacheRelativeDVAPVA_v2[idx].TextSizeIPB == textSizeIPB && cacheRelativeDVAPVA_v2[idx].LineWidthIPB == lineWidthIPB && cacheRelativeDVAPVA_v2[idx].LineStyleIPB == lineStyleIPB && cacheRelativeDVAPVA_v2[idx].LineLengthIPB == lineLengthIPB && cacheRelativeDVAPVA_v2[idx].TextIPBLong == textIPBLong && cacheRelativeDVAPVA_v2[idx].TextIPBShort == textIPBShort && cacheRelativeDVAPVA_v2[idx].TextSizeEF == textSizeEF && cacheRelativeDVAPVA_v2[idx].LineWidthEF == lineWidthEF && cacheRelativeDVAPVA_v2[idx].LineStyleEF == lineStyleEF && cacheRelativeDVAPVA_v2[idx].TextEFLong == textEFLong && cacheRelativeDVAPVA_v2[idx].TextEFShort == textEFShort && cacheRelativeDVAPVA_v2[idx].LineLengthEF == lineLengthEF && cacheRelativeDVAPVA_v2[idx].AcceptanceModeProp == acceptanceModeProp && cacheRelativeDVAPVA_v2[idx].BreakoutConfirmationBars == breakoutConfirmationBars && cacheRelativeDVAPVA_v2[idx].BreakoutConfirmationDistance == breakoutConfirmationDistance && cacheRelativeDVAPVA_v2[idx].BreakoutMinTimeMinutes == breakoutMinTimeMinutes && cacheRelativeDVAPVA_v2[idx].RPBDepthPercent == rPBDepthPercent && cacheRelativeDVAPVA_v2[idx].ShowDebugState == showDebugState && cacheRelativeDVAPVA_v2[idx].TextBPBLong == textBPBLong && cacheRelativeDVAPVA_v2[idx].ShowIPB == showIPB && cacheRelativeDVAPVA_v2[idx].AllowMultipleIPB == allowMultipleIPB && cacheRelativeDVAPVA_v2[idx].MaxCandleWidthPercent == maxCandleWidthPercent && cacheRelativeDVAPVA_v2[idx].ShowRPB == showRPB && cacheRelativeDVAPVA_v2[idx].SignalCooldown == signalCooldown && cacheRelativeDVAPVA_v2[idx].ShowEF == showEF && cacheRelativeDVAPVA_v2[idx].AllowMultipleEF == allowMultipleEF && cacheRelativeDVAPVA_v2[idx].AllowRotation == allowRotation && cacheRelativeDVAPVA_v2[idx].MinFailureDuration == minFailureDuration && cacheRelativeDVAPVA_v2[idx].ShowBPB == showBPB && cacheRelativeDVAPVA_v2[idx].TextBPBShort == textBPBShort && cacheRelativeDVAPVA_v2[idx].TextRPBLong == textRPBLong && cacheRelativeDVAPVA_v2[idx].TextRPBShort == textRPBShort && cacheRelativeDVAPVA_v2[idx].TextSizeBPB_RPB == textSizeBPB_RPB && cacheRelativeDVAPVA_v2[idx].LineWidthBPB_RPB == lineWidthBPB_RPB && cacheRelativeDVAPVA_v2[idx].LineStyleBPB_RPB == lineStyleBPB_RPB && cacheRelativeDVAPVA_v2[idx].LineLengthBPB_RPB == lineLengthBPB_RPB && cacheRelativeDVAPVA_v2[idx].ColorButtonTextRotationalShortSerializable == colorButtonTextRotationalShortSerializable && cacheRelativeDVAPVA_v2[idx].ColorButtonTextBreakoutLongSerializable == colorButtonTextBreakoutLongSerializable && cacheRelativeDVAPVA_v2[idx].ColorButtonTextBreakoutShortSerializable == colorButtonTextBreakoutShortSerializable && cacheRelativeDVAPVA_v2[idx].ColorButtonTextRPBLongSerializable == colorButtonTextRPBLongSerializable && cacheRelativeDVAPVA_v2[idx].ColorButtonTextRPBShortSerializable == colorButtonTextRPBShortSerializable && cacheRelativeDVAPVA_v2[idx].ColorButtonTextNeutralSerializable == colorButtonTextNeutralSerializable && cacheRelativeDVAPVA_v2[idx].ColorButtonEFLongSerializable == colorButtonEFLongSerializable && cacheRelativeDVAPVA_v2[idx].ColorButtonEFShortSerializable == colorButtonEFShortSerializable && cacheRelativeDVAPVA_v2[idx].ColorButtonIPBLongSerializable == colorButtonIPBLongSerializable && cacheRelativeDVAPVA_v2[idx].ColorButtonIPBShortSerializable == colorButtonIPBShortSerializable && cacheRelativeDVAPVA_v2[idx].ColorButtonTextEFLongSerializable == colorButtonTextEFLongSerializable && cacheRelativeDVAPVA_v2[idx].ColorButtonTextEFShortSerializable == colorButtonTextEFShortSerializable && cacheRelativeDVAPVA_v2[idx].ColorButtonTextIPBLongSerializable == colorButtonTextIPBLongSerializable && cacheRelativeDVAPVA_v2[idx].ColorButtonTextIPBShortSerializable == colorButtonTextIPBShortSerializable && cacheRelativeDVAPVA_v2[idx].ColorButtonPendingSerializable == colorButtonPendingSerializable && cacheRelativeDVAPVA_v2[idx].ColorButtonTextPendingSerializable == colorButtonTextPendingSerializable && cacheRelativeDVAPVA_v2[idx].HistoricalSignalColorSerializable == historicalSignalColorSerializable && cacheRelativeDVAPVA_v2[idx].UseAlerts == useAlerts && cacheRelativeDVAPVA_v2[idx].AlertOnBPB == alertOnBPB && cacheRelativeDVAPVA_v2[idx].AlertOnRPB == alertOnRPB && cacheRelativeDVAPVA_v2[idx].AlertOnIPB == alertOnIPB && cacheRelativeDVAPVA_v2[idx].AlertOnEF == alertOnEF && cacheRelativeDVAPVA_v2[idx].AlertSound == alertSound && cacheRelativeDVAPVA_v2[idx].SendEmail == sendEmail && cacheRelativeDVAPVA_v2[idx].EmailAddress == emailAddress && cacheRelativeDVAPVA_v2[idx].AttachScreenshot == attachScreenshot && cacheRelativeDVAPVA_v2[idx].EqualsInput(input))
						return cacheRelativeDVAPVA_v2[idx];
			return CacheIndicator<RelativeIndicators.RelativeDVAPVA_v2>(new RelativeIndicators.RelativeDVAPVA_v2(){ SessionType = sessionType, BandType = bandType, CustomTZSelector = customTZSelector, S_CustomSessionStart = s_CustomSessionStart, S_CustomSessionEnd = s_CustomSessionEnd, MultiplierSD1 = multiplierSD1, MultiplierSD2 = multiplierSD2, MultiplierSD3 = multiplierSD3, ShowDVABands = showDVABands, ShowSessionVWAPLine = showSessionVWAPLine, MultiplierQR1 = multiplierQR1, MultiplierQR2 = multiplierQR2, MultiplierQR3 = multiplierQR3, ShowSessionZones = showSessionZones, ZoneCutoffPercentage = zoneCutoffPercentage, SessionZoneOpacity = sessionZoneOpacity, ZoneLineWidth = zoneLineWidth, ZoneTextSize = zoneTextSize, ZoneLabelUpper = zoneLabelUpper, ZoneLabelLower = zoneLabelLower, ZoneTextBackgroundOpacity = zoneTextBackgroundOpacity, MaxDaysToDraw = maxDaysToDraw, TextSizeIPB = textSizeIPB, LineWidthIPB = lineWidthIPB, LineStyleIPB = lineStyleIPB, LineLengthIPB = lineLengthIPB, TextIPBLong = textIPBLong, TextIPBShort = textIPBShort, TextSizeEF = textSizeEF, LineWidthEF = lineWidthEF, LineStyleEF = lineStyleEF, TextEFLong = textEFLong, TextEFShort = textEFShort, LineLengthEF = lineLengthEF, AcceptanceModeProp = acceptanceModeProp, BreakoutConfirmationBars = breakoutConfirmationBars, BreakoutConfirmationDistance = breakoutConfirmationDistance, BreakoutMinTimeMinutes = breakoutMinTimeMinutes, RPBDepthPercent = rPBDepthPercent, ShowDebugState = showDebugState, TextBPBLong = textBPBLong, ShowIPB = showIPB, AllowMultipleIPB = allowMultipleIPB, MaxCandleWidthPercent = maxCandleWidthPercent, ShowRPB = showRPB, SignalCooldown = signalCooldown, ShowEF = showEF, AllowMultipleEF = allowMultipleEF, AllowRotation = allowRotation, MinFailureDuration = minFailureDuration, ShowBPB = showBPB, TextBPBShort = textBPBShort, TextRPBLong = textRPBLong, TextRPBShort = textRPBShort, TextSizeBPB_RPB = textSizeBPB_RPB, LineWidthBPB_RPB = lineWidthBPB_RPB, LineStyleBPB_RPB = lineStyleBPB_RPB, LineLengthBPB_RPB = lineLengthBPB_RPB, ColorButtonTextRotationalShortSerializable = colorButtonTextRotationalShortSerializable, ColorButtonTextBreakoutLongSerializable = colorButtonTextBreakoutLongSerializable, ColorButtonTextBreakoutShortSerializable = colorButtonTextBreakoutShortSerializable, ColorButtonTextRPBLongSerializable = colorButtonTextRPBLongSerializable, ColorButtonTextRPBShortSerializable = colorButtonTextRPBShortSerializable, ColorButtonTextNeutralSerializable = colorButtonTextNeutralSerializable, ColorButtonEFLongSerializable = colorButtonEFLongSerializable, ColorButtonEFShortSerializable = colorButtonEFShortSerializable, ColorButtonIPBLongSerializable = colorButtonIPBLongSerializable, ColorButtonIPBShortSerializable = colorButtonIPBShortSerializable, ColorButtonTextEFLongSerializable = colorButtonTextEFLongSerializable, ColorButtonTextEFShortSerializable = colorButtonTextEFShortSerializable, ColorButtonTextIPBLongSerializable = colorButtonTextIPBLongSerializable, ColorButtonTextIPBShortSerializable = colorButtonTextIPBShortSerializable, ColorButtonPendingSerializable = colorButtonPendingSerializable, ColorButtonTextPendingSerializable = colorButtonTextPendingSerializable, HistoricalSignalColorSerializable = historicalSignalColorSerializable, UseAlerts = useAlerts, AlertOnBPB = alertOnBPB, AlertOnRPB = alertOnRPB, AlertOnIPB = alertOnIPB, AlertOnEF = alertOnEF, AlertSound = alertSound, SendEmail = sendEmail, EmailAddress = emailAddress, AttachScreenshot = attachScreenshot }, input, ref cacheRelativeDVAPVA_v2);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeDVAPVA_v2 RelativeDVAPVA_v2(SessionTypeVWAPD sessionType, BandTypeVWAPD bandType, TimeZonesVWAPD customTZSelector, string s_CustomSessionStart, string s_CustomSessionEnd, double multiplierSD1, double multiplierSD2, double multiplierSD3, bool showDVABands, bool showSessionVWAPLine, double multiplierQR1, double multiplierQR2, double multiplierQR3, bool showSessionZones, int zoneCutoffPercentage, int sessionZoneOpacity, int zoneLineWidth, int zoneTextSize, string zoneLabelUpper, string zoneLabelLower, int zoneTextBackgroundOpacity, int maxDaysToDraw, int textSizeIPB, int lineWidthIPB, DashStyleHelper lineStyleIPB, int lineLengthIPB, string textIPBLong, string textIPBShort, int textSizeEF, int lineWidthEF, DashStyleHelper lineStyleEF, string textEFLong, string textEFShort, int lineLengthEF, AcceptanceMode acceptanceModeProp, int breakoutConfirmationBars, double breakoutConfirmationDistance, int breakoutMinTimeMinutes, double rPBDepthPercent, bool showDebugState, string textBPBLong, bool showIPB, bool allowMultipleIPB, double maxCandleWidthPercent, bool showRPB, int signalCooldown, bool showEF, bool allowMultipleEF, bool allowRotation, int minFailureDuration, bool showBPB, string textBPBShort, string textRPBLong, string textRPBShort, int textSizeBPB_RPB, int lineWidthBPB_RPB, DashStyleHelper lineStyleBPB_RPB, int lineLengthBPB_RPB, string colorButtonTextRotationalShortSerializable, string colorButtonTextBreakoutLongSerializable, string colorButtonTextBreakoutShortSerializable, string colorButtonTextRPBLongSerializable, string colorButtonTextRPBShortSerializable, string colorButtonTextNeutralSerializable, string colorButtonEFLongSerializable, string colorButtonEFShortSerializable, string colorButtonIPBLongSerializable, string colorButtonIPBShortSerializable, string colorButtonTextEFLongSerializable, string colorButtonTextEFShortSerializable, string colorButtonTextIPBLongSerializable, string colorButtonTextIPBShortSerializable, string colorButtonPendingSerializable, string colorButtonTextPendingSerializable, string historicalSignalColorSerializable, bool useAlerts, bool alertOnBPB, bool alertOnRPB, bool alertOnIPB, bool alertOnEF, string alertSound, bool sendEmail, string emailAddress, bool attachScreenshot)
		{
			return indicator.RelativeDVAPVA_v2(Input, sessionType, bandType, customTZSelector, s_CustomSessionStart, s_CustomSessionEnd, multiplierSD1, multiplierSD2, multiplierSD3, showDVABands, showSessionVWAPLine, multiplierQR1, multiplierQR2, multiplierQR3, showSessionZones, zoneCutoffPercentage, sessionZoneOpacity, zoneLineWidth, zoneTextSize, zoneLabelUpper, zoneLabelLower, zoneTextBackgroundOpacity, maxDaysToDraw, textSizeIPB, lineWidthIPB, lineStyleIPB, lineLengthIPB, textIPBLong, textIPBShort, textSizeEF, lineWidthEF, lineStyleEF, textEFLong, textEFShort, lineLengthEF, acceptanceModeProp, breakoutConfirmationBars, breakoutConfirmationDistance, breakoutMinTimeMinutes, rPBDepthPercent, showDebugState, textBPBLong, showIPB, allowMultipleIPB, maxCandleWidthPercent, showRPB, signalCooldown, showEF, allowMultipleEF, allowRotation, minFailureDuration, showBPB, textBPBShort, textRPBLong, textRPBShort, textSizeBPB_RPB, lineWidthBPB_RPB, lineStyleBPB_RPB, lineLengthBPB_RPB, colorButtonTextRotationalShortSerializable, colorButtonTextBreakoutLongSerializable, colorButtonTextBreakoutShortSerializable, colorButtonTextRPBLongSerializable, colorButtonTextRPBShortSerializable, colorButtonTextNeutralSerializable, colorButtonEFLongSerializable, colorButtonEFShortSerializable, colorButtonIPBLongSerializable, colorButtonIPBShortSerializable, colorButtonTextEFLongSerializable, colorButtonTextEFShortSerializable, colorButtonTextIPBLongSerializable, colorButtonTextIPBShortSerializable, colorButtonPendingSerializable, colorButtonTextPendingSerializable, historicalSignalColorSerializable, useAlerts, alertOnBPB, alertOnRPB, alertOnIPB, alertOnEF, alertSound, sendEmail, emailAddress, attachScreenshot);
		}

		public Indicators.RelativeIndicators.RelativeDVAPVA_v2 RelativeDVAPVA_v2(ISeries<double> input , SessionTypeVWAPD sessionType, BandTypeVWAPD bandType, TimeZonesVWAPD customTZSelector, string s_CustomSessionStart, string s_CustomSessionEnd, double multiplierSD1, double multiplierSD2, double multiplierSD3, bool showDVABands, bool showSessionVWAPLine, double multiplierQR1, double multiplierQR2, double multiplierQR3, bool showSessionZones, int zoneCutoffPercentage, int sessionZoneOpacity, int zoneLineWidth, int zoneTextSize, string zoneLabelUpper, string zoneLabelLower, int zoneTextBackgroundOpacity, int maxDaysToDraw, int textSizeIPB, int lineWidthIPB, DashStyleHelper lineStyleIPB, int lineLengthIPB, string textIPBLong, string textIPBShort, int textSizeEF, int lineWidthEF, DashStyleHelper lineStyleEF, string textEFLong, string textEFShort, int lineLengthEF, AcceptanceMode acceptanceModeProp, int breakoutConfirmationBars, double breakoutConfirmationDistance, int breakoutMinTimeMinutes, double rPBDepthPercent, bool showDebugState, string textBPBLong, bool showIPB, bool allowMultipleIPB, double maxCandleWidthPercent, bool showRPB, int signalCooldown, bool showEF, bool allowMultipleEF, bool allowRotation, int minFailureDuration, bool showBPB, string textBPBShort, string textRPBLong, string textRPBShort, int textSizeBPB_RPB, int lineWidthBPB_RPB, DashStyleHelper lineStyleBPB_RPB, int lineLengthBPB_RPB, string colorButtonTextRotationalShortSerializable, string colorButtonTextBreakoutLongSerializable, string colorButtonTextBreakoutShortSerializable, string colorButtonTextRPBLongSerializable, string colorButtonTextRPBShortSerializable, string colorButtonTextNeutralSerializable, string colorButtonEFLongSerializable, string colorButtonEFShortSerializable, string colorButtonIPBLongSerializable, string colorButtonIPBShortSerializable, string colorButtonTextEFLongSerializable, string colorButtonTextEFShortSerializable, string colorButtonTextIPBLongSerializable, string colorButtonTextIPBShortSerializable, string colorButtonPendingSerializable, string colorButtonTextPendingSerializable, string historicalSignalColorSerializable, bool useAlerts, bool alertOnBPB, bool alertOnRPB, bool alertOnIPB, bool alertOnEF, string alertSound, bool sendEmail, string emailAddress, bool attachScreenshot)
		{
			return indicator.RelativeDVAPVA_v2(input, sessionType, bandType, customTZSelector, s_CustomSessionStart, s_CustomSessionEnd, multiplierSD1, multiplierSD2, multiplierSD3, showDVABands, showSessionVWAPLine, multiplierQR1, multiplierQR2, multiplierQR3, showSessionZones, zoneCutoffPercentage, sessionZoneOpacity, zoneLineWidth, zoneTextSize, zoneLabelUpper, zoneLabelLower, zoneTextBackgroundOpacity, maxDaysToDraw, textSizeIPB, lineWidthIPB, lineStyleIPB, lineLengthIPB, textIPBLong, textIPBShort, textSizeEF, lineWidthEF, lineStyleEF, textEFLong, textEFShort, lineLengthEF, acceptanceModeProp, breakoutConfirmationBars, breakoutConfirmationDistance, breakoutMinTimeMinutes, rPBDepthPercent, showDebugState, textBPBLong, showIPB, allowMultipleIPB, maxCandleWidthPercent, showRPB, signalCooldown, showEF, allowMultipleEF, allowRotation, minFailureDuration, showBPB, textBPBShort, textRPBLong, textRPBShort, textSizeBPB_RPB, lineWidthBPB_RPB, lineStyleBPB_RPB, lineLengthBPB_RPB, colorButtonTextRotationalShortSerializable, colorButtonTextBreakoutLongSerializable, colorButtonTextBreakoutShortSerializable, colorButtonTextRPBLongSerializable, colorButtonTextRPBShortSerializable, colorButtonTextNeutralSerializable, colorButtonEFLongSerializable, colorButtonEFShortSerializable, colorButtonIPBLongSerializable, colorButtonIPBShortSerializable, colorButtonTextEFLongSerializable, colorButtonTextEFShortSerializable, colorButtonTextIPBLongSerializable, colorButtonTextIPBShortSerializable, colorButtonPendingSerializable, colorButtonTextPendingSerializable, historicalSignalColorSerializable, useAlerts, alertOnBPB, alertOnRPB, alertOnIPB, alertOnEF, alertSound, sendEmail, emailAddress, attachScreenshot);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeDVAPVA_v2 RelativeDVAPVA_v2(SessionTypeVWAPD sessionType, BandTypeVWAPD bandType, TimeZonesVWAPD customTZSelector, string s_CustomSessionStart, string s_CustomSessionEnd, double multiplierSD1, double multiplierSD2, double multiplierSD3, bool showDVABands, bool showSessionVWAPLine, double multiplierQR1, double multiplierQR2, double multiplierQR3, bool showSessionZones, int zoneCutoffPercentage, int sessionZoneOpacity, int zoneLineWidth, int zoneTextSize, string zoneLabelUpper, string zoneLabelLower, int zoneTextBackgroundOpacity, int maxDaysToDraw, int textSizeIPB, int lineWidthIPB, DashStyleHelper lineStyleIPB, int lineLengthIPB, string textIPBLong, string textIPBShort, int textSizeEF, int lineWidthEF, DashStyleHelper lineStyleEF, string textEFLong, string textEFShort, int lineLengthEF, AcceptanceMode acceptanceModeProp, int breakoutConfirmationBars, double breakoutConfirmationDistance, int breakoutMinTimeMinutes, double rPBDepthPercent, bool showDebugState, string textBPBLong, bool showIPB, bool allowMultipleIPB, double maxCandleWidthPercent, bool showRPB, int signalCooldown, bool showEF, bool allowMultipleEF, bool allowRotation, int minFailureDuration, bool showBPB, string textBPBShort, string textRPBLong, string textRPBShort, int textSizeBPB_RPB, int lineWidthBPB_RPB, DashStyleHelper lineStyleBPB_RPB, int lineLengthBPB_RPB, string colorButtonTextRotationalShortSerializable, string colorButtonTextBreakoutLongSerializable, string colorButtonTextBreakoutShortSerializable, string colorButtonTextRPBLongSerializable, string colorButtonTextRPBShortSerializable, string colorButtonTextNeutralSerializable, string colorButtonEFLongSerializable, string colorButtonEFShortSerializable, string colorButtonIPBLongSerializable, string colorButtonIPBShortSerializable, string colorButtonTextEFLongSerializable, string colorButtonTextEFShortSerializable, string colorButtonTextIPBLongSerializable, string colorButtonTextIPBShortSerializable, string colorButtonPendingSerializable, string colorButtonTextPendingSerializable, string historicalSignalColorSerializable, bool useAlerts, bool alertOnBPB, bool alertOnRPB, bool alertOnIPB, bool alertOnEF, string alertSound, bool sendEmail, string emailAddress, bool attachScreenshot)
		{
			return indicator.RelativeDVAPVA_v2(Input, sessionType, bandType, customTZSelector, s_CustomSessionStart, s_CustomSessionEnd, multiplierSD1, multiplierSD2, multiplierSD3, showDVABands, showSessionVWAPLine, multiplierQR1, multiplierQR2, multiplierQR3, showSessionZones, zoneCutoffPercentage, sessionZoneOpacity, zoneLineWidth, zoneTextSize, zoneLabelUpper, zoneLabelLower, zoneTextBackgroundOpacity, maxDaysToDraw, textSizeIPB, lineWidthIPB, lineStyleIPB, lineLengthIPB, textIPBLong, textIPBShort, textSizeEF, lineWidthEF, lineStyleEF, textEFLong, textEFShort, lineLengthEF, acceptanceModeProp, breakoutConfirmationBars, breakoutConfirmationDistance, breakoutMinTimeMinutes, rPBDepthPercent, showDebugState, textBPBLong, showIPB, allowMultipleIPB, maxCandleWidthPercent, showRPB, signalCooldown, showEF, allowMultipleEF, allowRotation, minFailureDuration, showBPB, textBPBShort, textRPBLong, textRPBShort, textSizeBPB_RPB, lineWidthBPB_RPB, lineStyleBPB_RPB, lineLengthBPB_RPB, colorButtonTextRotationalShortSerializable, colorButtonTextBreakoutLongSerializable, colorButtonTextBreakoutShortSerializable, colorButtonTextRPBLongSerializable, colorButtonTextRPBShortSerializable, colorButtonTextNeutralSerializable, colorButtonEFLongSerializable, colorButtonEFShortSerializable, colorButtonIPBLongSerializable, colorButtonIPBShortSerializable, colorButtonTextEFLongSerializable, colorButtonTextEFShortSerializable, colorButtonTextIPBLongSerializable, colorButtonTextIPBShortSerializable, colorButtonPendingSerializable, colorButtonTextPendingSerializable, historicalSignalColorSerializable, useAlerts, alertOnBPB, alertOnRPB, alertOnIPB, alertOnEF, alertSound, sendEmail, emailAddress, attachScreenshot);
		}

		public Indicators.RelativeIndicators.RelativeDVAPVA_v2 RelativeDVAPVA_v2(ISeries<double> input , SessionTypeVWAPD sessionType, BandTypeVWAPD bandType, TimeZonesVWAPD customTZSelector, string s_CustomSessionStart, string s_CustomSessionEnd, double multiplierSD1, double multiplierSD2, double multiplierSD3, bool showDVABands, bool showSessionVWAPLine, double multiplierQR1, double multiplierQR2, double multiplierQR3, bool showSessionZones, int zoneCutoffPercentage, int sessionZoneOpacity, int zoneLineWidth, int zoneTextSize, string zoneLabelUpper, string zoneLabelLower, int zoneTextBackgroundOpacity, int maxDaysToDraw, int textSizeIPB, int lineWidthIPB, DashStyleHelper lineStyleIPB, int lineLengthIPB, string textIPBLong, string textIPBShort, int textSizeEF, int lineWidthEF, DashStyleHelper lineStyleEF, string textEFLong, string textEFShort, int lineLengthEF, AcceptanceMode acceptanceModeProp, int breakoutConfirmationBars, double breakoutConfirmationDistance, int breakoutMinTimeMinutes, double rPBDepthPercent, bool showDebugState, string textBPBLong, bool showIPB, bool allowMultipleIPB, double maxCandleWidthPercent, bool showRPB, int signalCooldown, bool showEF, bool allowMultipleEF, bool allowRotation, int minFailureDuration, bool showBPB, string textBPBShort, string textRPBLong, string textRPBShort, int textSizeBPB_RPB, int lineWidthBPB_RPB, DashStyleHelper lineStyleBPB_RPB, int lineLengthBPB_RPB, string colorButtonTextRotationalShortSerializable, string colorButtonTextBreakoutLongSerializable, string colorButtonTextBreakoutShortSerializable, string colorButtonTextRPBLongSerializable, string colorButtonTextRPBShortSerializable, string colorButtonTextNeutralSerializable, string colorButtonEFLongSerializable, string colorButtonEFShortSerializable, string colorButtonIPBLongSerializable, string colorButtonIPBShortSerializable, string colorButtonTextEFLongSerializable, string colorButtonTextEFShortSerializable, string colorButtonTextIPBLongSerializable, string colorButtonTextIPBShortSerializable, string colorButtonPendingSerializable, string colorButtonTextPendingSerializable, string historicalSignalColorSerializable, bool useAlerts, bool alertOnBPB, bool alertOnRPB, bool alertOnIPB, bool alertOnEF, string alertSound, bool sendEmail, string emailAddress, bool attachScreenshot)
		{
			return indicator.RelativeDVAPVA_v2(input, sessionType, bandType, customTZSelector, s_CustomSessionStart, s_CustomSessionEnd, multiplierSD1, multiplierSD2, multiplierSD3, showDVABands, showSessionVWAPLine, multiplierQR1, multiplierQR2, multiplierQR3, showSessionZones, zoneCutoffPercentage, sessionZoneOpacity, zoneLineWidth, zoneTextSize, zoneLabelUpper, zoneLabelLower, zoneTextBackgroundOpacity, maxDaysToDraw, textSizeIPB, lineWidthIPB, lineStyleIPB, lineLengthIPB, textIPBLong, textIPBShort, textSizeEF, lineWidthEF, lineStyleEF, textEFLong, textEFShort, lineLengthEF, acceptanceModeProp, breakoutConfirmationBars, breakoutConfirmationDistance, breakoutMinTimeMinutes, rPBDepthPercent, showDebugState, textBPBLong, showIPB, allowMultipleIPB, maxCandleWidthPercent, showRPB, signalCooldown, showEF, allowMultipleEF, allowRotation, minFailureDuration, showBPB, textBPBShort, textRPBLong, textRPBShort, textSizeBPB_RPB, lineWidthBPB_RPB, lineStyleBPB_RPB, lineLengthBPB_RPB, colorButtonTextRotationalShortSerializable, colorButtonTextBreakoutLongSerializable, colorButtonTextBreakoutShortSerializable, colorButtonTextRPBLongSerializable, colorButtonTextRPBShortSerializable, colorButtonTextNeutralSerializable, colorButtonEFLongSerializable, colorButtonEFShortSerializable, colorButtonIPBLongSerializable, colorButtonIPBShortSerializable, colorButtonTextEFLongSerializable, colorButtonTextEFShortSerializable, colorButtonTextIPBLongSerializable, colorButtonTextIPBShortSerializable, colorButtonPendingSerializable, colorButtonTextPendingSerializable, historicalSignalColorSerializable, useAlerts, alertOnBPB, alertOnRPB, alertOnIPB, alertOnEF, alertSound, sendEmail, emailAddress, attachScreenshot);
		}
	}
}

#endregion
