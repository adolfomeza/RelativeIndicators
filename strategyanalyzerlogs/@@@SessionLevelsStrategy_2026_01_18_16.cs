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
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Core; // Added explicit Core usage
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Strategies.SessionLevels;
using System.Net;
using System.Net.Mail;
using System.IO;
// using System.Windows.Controls; // Removed v1.14.43 (Moved to StrategyHelpers)
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	// Trading Mode Control



        // v1.15.40: Exit Strategy Type
        public enum ExitStrategyType
        {
            Standard, // TP1 (VWAP) + TP2 (Zone)
            Ladder    // 1R, 2R, 3R...
        }
		
	public class SessionLevelsStrategy_2026_01_18_16 : Strategy
	{
		private const string StrategyVersion = "v1.15.37"; // v1.15.37: Fix Attempt counter & Add LevelAge export
		
		// CONTROL BUTTONS (Delegated to StrategyHelpers)
		[XmlIgnore] public TradingMode currentTradingMode = TradingMode.Normal;
		private StrategyHelpers helpers; // Phase 7: UI & Helpers Module
		private bool isProtectionProcessing = false; // v1.13.1: Concurrency lock
		private bool failsafeTriggered = false; // v1.14.2: Prevent infinite loop in CheckHardStop
		
		// v1.14.29: Visual Filter Feedback
		[XmlIgnore] public string lastFilterReason = "";
		[XmlIgnore] public DateTime lastFilterTime = DateTime.MinValue;
		
		// =========================================================
		// TRADE ANALYZER EXPORT
		// =========================================================
		private double tradeMAE = 0;           // Maximum Adverse Excursion (worst unrealized PnL)
		private double tradeMFE = 0;           // Maximum Favorable Excursion (best unrealized PnL)
		private double tradeEntryPrice = 0;
		private DateTime tradeEntryTime;
		private string tradeSetupName = "";
		private string tradeDirection = "";
		private int tradeExportId = 0;         // Auto-incrementing ID for CSV
		private int tradeExitFillsCount = 0;   // v1.13.4: Count exit fills for split IDs
		private int tradeAttemptNumber = 0;    // v1.13.11: VWAP attempt number for analysis
		[XmlIgnore] public double tradeRiskUSD = 0;       // v1.13.12: Original risk in USD for R:R calculation
		private string csvExportPath = "";
		private bool isTrackingTrade = false;  // Flag to track MAE/MFE
		private bool slOrderCreatedThisEntry = false; // v1.13.5: Prevent duplicate SL creation

	// v1.14.31: Delta Integration for quantitative analysis
	private NinjaTrader.NinjaScript.Indicators.RelativeIndicators.RelativeDelta relativeDelta;
	private double tradeDeltaAtEntry = 0;      // Delta when entry filled
	private int tradeDeltaDirection = 0;       // 1=aligned, -1=opposed, 0=neutral
	private double tradeSessionDelta = 0;      // Session cumulative delta at entry
	private double tradeDeltaAtTP1 = 0;        // Delta when TP1 filled



		// Version Control
        // V_STACK: Stacking Logic Variables
        private double stackHighY = double.MinValue;
        private double stackLowY = double.MaxValue;
        private int lastColBarIdx = -1;
        private double verticalUnit = 0;
        [XmlIgnore] public NinjaTrader.NinjaScript.Indicators.ATR atr;
		
		// Persistence for EnsureProtection
		// v1.15.26: Split into separate TP1 and TP2 prices to fix MCL bug
		[XmlIgnore] public double validatedTp1Price = 0;
		[XmlIgnore] public double validatedTp2Price = 0;

        // Helper Methods for Stacking
        private double GetStackedHighY(double desiredY, double heightBuffer)
        {
             // If stack is empty/reset, take desiredY
             if (stackHighY == double.MinValue) 
             {
                 stackHighY = desiredY;
                 return desiredY;
             }
             
             // If desiredY is overlapping or below the stack (Highs stack upwards), push it UP
             if (desiredY <= stackHighY + heightBuffer)
             {
                  double newY = stackHighY + heightBuffer;
                  stackHighY = newY;
                  return newY;
             }
             else
             {
                  // It's way above, safe.
                  stackHighY = desiredY;
                  return desiredY;
             }
        }

        private double GetStackedLowY(double desiredY, double heightBuffer)
        {
             // If stack is empty, take desiredY
             if (stackLowY == double.MaxValue) 
             {
                 stackLowY = desiredY;
                 return desiredY;
             }
             
             // If desiredY is overlapping or ABOVE the stack (Lows stack downwards), push it DOWN
             if (desiredY >= stackLowY - heightBuffer)
             {
                  double newY = stackLowY - heightBuffer;
                  stackLowY = newY;
                  return newY;
             }
             else
             {
                  // It's way below, safe.
                  stackLowY = desiredY;
                  return desiredY;
             }
        }

		// Version Control

        [XmlIgnore]
        public VWAPCalculator vwapCalc;
        [XmlIgnore]
        public OrderProtectionManager protectionManager;
        [XmlIgnore]
        public EntryStateMachine entryMachine;
        [XmlIgnore]
        public SessionManager sessionManager;


        // v1.14.42: activeLevels is now public directly (property removed to avoid ambiguity)

        [Browsable(false)]
        [XmlIgnore]
        public bool IsTradeVwapActive 
        { 
            get { return tradeVwapActive; } 
            set { tradeVwapActive = value; }
        }
        
        // v1.14.74: New flag to indicate trade crossed 18:00 and should use Trade VWAP for TP1
        [Browsable(false)]
        [XmlIgnore]
        public bool IsTradeVwapExtended { get; set; } = false;





		// ... existing properties ...

		// Optimize Performance: Cache TimeSpans
		private TimeSpan tsAsiaStart, tsAsiaEnd;
		private TimeSpan tsEuStart, tsEuEnd;
		private TimeSpan tsUsaStart, tsUsaEnd;
		private SessionIterator sessionIterator; // v1.14.7 fix
		
		// v1.14.42: Public timezone references for SessionManager
		[XmlIgnore] public TimeZoneInfo nyTimeZone;
		[XmlIgnore] public TimeZoneInfo chartTimeZone;
		
		[XmlIgnore] public List<SessionLevel> activeLevels = new List<SessionLevel>();
	[XmlIgnore] public Dictionary<string, int> levelEntryAttempts = new Dictionary<string, int>(); // v1.15.15: Persistent counter
		// v1.14.42: Public property for SessionManager access
		public string USAEndTime => "18:00:00"; // USA session close time
		
		// OPTIMIZATION (v1.7.3): Cache Opposite Level to avoid loops
		[XmlIgnore] public SessionLevel cachedOppositeLevel = null;
		[XmlIgnore] public bool oppositeSearchDone = false; // v1.14.32: Prevent repeated searches when not found
		
		// Internal Levels Management
		[XmlIgnore] public bool isInternalLevel = false;
		[XmlIgnore] public double externalLevelAbove = 0;  // For SHORT setups (external High above)
		[XmlIgnore] public double externalLevelBelow = 0;  // For LONG setups (external Low below)
		[XmlIgnore] public string externalLevelAboveName = "";
		[XmlIgnore] public string externalLevelBelowName = "";
	[XmlIgnore] public int lastInvalidationBar = -1;  // v1.10.1: Anti-loop for invalidation
	
		// VWAP Retry Tracking
		[XmlIgnore] public double vwapCandleExtreme = 0;           // Low (LONG) or High (SHORT) to mitigate
		[XmlIgnore] public bool waitingForVwapMitigation = false;  // Are we waiting for price to break?
		[XmlIgnore] public int currentVwapNumber = 1;              // Which VWAP# (1, 2, 3...)
		private int vwapTouchBar = -1;                  // Bar where VWAP was touched

		// v1.15.29: R:R immediate abandonment (removed counter - abandons immediately on invalid R:R)

		private bool enableDebugLogs = false; // Default false for performance
		private bool enableHolidayProtection = true; // v1.14.79: Default true
		private bool isLagPaused = false; // v1.14.36: Auto-pause when lag > 60s

		[NinjaScriptProperty]
		[Display(Name="Enable Holiday Protection", Description="If true, exits early on holidays/early closes. Disable if using bad backtest data.", Order=59, GroupName="General")]
		public bool EnableHolidayProtection
		{
			get { return enableHolidayProtection; }
			set { enableHolidayProtection = value; }
		}

		[NinjaScriptProperty]
		[Display(Name="Enable Debug Logs", Description="Print detailed execution steps to Output. Disable for faster backtests.", Order=60, GroupName="General")]
		public bool EnableDebugLogs
		{
			get { return enableDebugLogs; }
			set { enableDebugLogs = value; }
		}
		
		// Lag Filter - Maximum allowed chart lag before blocking orders
		[NinjaScriptProperty]
		[Range(0.1, 10)]
		[Display(Name="Max Chart Lag (Seconds)", Description="Block orders when chart data is older than this threshold. Set higher if experiencing false positives.", Order=62, GroupName="General")]
		public double MaxChartLagSeconds { get; set; } = 0.75;
		
		// Strategy Analyzer Support - Enable backtest execution in Historical state
		[NinjaScriptProperty]
		[Display(Name="Allow Backtest", Description="Enable order execution in Strategy Analyzer. Keep OFF for live/demo accounts.", Order=63, GroupName="General")]
		public bool AllowBacktest { get; set; } = false;



		private bool showVisuals = true;
		
		[NinjaScriptProperty]
		[Display(Name="Show Visuals", Description="Draw lines on chart. Disable to save resources.", Order=61, GroupName="General")]
		public bool ShowVisuals
		{
			get { return showVisuals; }
			set { showVisuals = value; }
		}
		
		private VwapCalculationMode vwapMethod = VwapCalculationMode.Typical;
		[NinjaScriptProperty]
		[Display(Name="VWAP Calculation Method", Description="Select formula for VWAP.", Order=62, GroupName="General")]
		public VwapCalculationMode VwapMethod
		{
			get { return vwapMethod; }
			set { vwapMethod = value; }
		}
		
		// =========================================================
		// TRIGGER LABEL SETTINGS
		// =========================================================
		private double labelDistanceATR = 0.3;
		[NinjaScriptProperty]
		[Display(Name="Label Distance (ATR)", Description="Distance from candle as ATR multiplier. Lower = closer to candle.", Order=70, GroupName="Trigger Labels")]
		public double LabelDistanceATR
		{
			get { return labelDistanceATR; }
			set { labelDistanceATR = Math.Max(0.1, value); }
		}
		
		private int labelFontSize = 12;
		[NinjaScriptProperty]
		[Display(Name="Label Font Size", Description="Font size for trigger labels (8-20).", Order=71, GroupName="Trigger Labels")]
		public int LabelFontSize
		{
			get { return labelFontSize; }
			set { labelFontSize = Math.Max(8, Math.Min(20, value)); }
		}
		
		private bool labelShowText = true;
		[NinjaScriptProperty]
		[Display(Name="Show Text", Description="Show 'Short'/'Long' text label.", Order=72, GroupName="Trigger Labels")]
		public bool LabelShowText
		{
			get { return labelShowText; }
			set { labelShowText = value; }
		}
		
		private bool labelShowArrow = true;
		[NinjaScriptProperty]
		[Display(Name="Show Arrow", Description="Show arrow marker.", Order=73, GroupName="Trigger Labels")]
		public bool LabelShowArrow
		{
			get { return labelShowArrow; }
			set { labelShowArrow = value; }
		}
		
		private int labelTextOffset = 12;
		[NinjaScriptProperty]
		[Display(Name="Text Offset (Pixels)", Description="Distance in pixels between arrow and text. Positive = text above arrow.", Order=74, GroupName="Trigger Labels")]
		public int LabelTextOffset
		{
			get { return labelTextOffset; }
			set { labelTextOffset = value; }
		}
		
		// =========================================================
		// CONFIRMATION CANDLE HIGHLIGHT
		// =========================================================
		private bool highlightConfirmationCandle = true;
		[NinjaScriptProperty]
		[Display(Name="Highlight Confirmation Candle", Description="Color the candle that confirms VWAP separation.", Order=80, GroupName="Trigger Labels")]
		public bool HighlightConfirmationCandle
		{
			get { return highlightConfirmationCandle; }
			set { highlightConfirmationCandle = value; }
		}

		// =========================================================
		// AUDIO SETTINGS
		// =========================================================
		private bool useAlerts = true;
		[NinjaScriptProperty]
		[Display(Name="Use Sound Alerts", Description="Play sound when a setup triggers.", Order=90, GroupName="Audio Settings")]
		public bool UseAlerts
		{
			get { return useAlerts; }
			set { useAlerts = value; }
		}
		
		private string alertSoundFile = "mzpack_alert4.wav";
		[NinjaScriptProperty]
		[Display(Name="Alert Sound File", Description="Sound file to play. Must be in NinjaTrader 8/sounds folder.", Order=91, GroupName="Audio Settings")]
		public string AlertSoundFile
		{
			get { return alertSoundFile; }
			set { alertSoundFile = value; }
		}
		
		private Brush confirmationCandleColor = Brushes.Yellow;
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="Confirmation Candle Color", Description="Color for the confirmation candle body.", Order=81, GroupName="Trigger Labels")]
		public Brush ConfirmationCandleColor
		{
			get { return confirmationCandleColor; }
			set { confirmationCandleColor = value; }
		}
		
		[Browsable(false)]
		public string ConfirmationCandleColorSerializable
		{
			get { return Serialize.BrushToString(confirmationCandleColor); }
			set { confirmationCandleColor = Serialize.StringToBrush(value); }
		}

		// Visual State for Adhoc VWAP Line
		[XmlIgnore] public double visualAdhocPrevBarVal = 0;
		[XmlIgnore] public double visualAdhocLastVal = 0;
		[XmlIgnore] public int visualAdhocLastBar = -1;

		// File-based logging per instrument for easier debugging
		private static object logFileLock = new object();
		private string logFilePath = null;
		private DateTime lastLogFlush = DateTime.MinValue;
		
		// Lag Filter - Chart data freshness detection
		[XmlIgnore] public double currentChartLag = 0;
		[XmlIgnore] public bool isLagAlertActive = false;
		
		// Orphan false positive prevention - delay after position close
		private DateTime lastPositionCloseTime = DateTime.MinValue;
		
		// v1.14.61: Daily Reset Tracking
		private DateTime lastTradingDate = DateTime.MinValue;
		
		public void Log(string message)
		{
            // v1.15.36: Timestamps are added by StrategyHelpers.Log() (PC Time + Chart Time)
            // Don't add timestamp here to avoid duplication
			if (helpers != null) helpers.Log(message);
		}
		
		// Wrappers for OrderProtectionManager (Exposing Protected Methods)
		/// <summary>
		/// Wrapper for SubmitOrderUnmanaged to allow calling from helper classes.
		/// </summary>
		public Order SubmitOrderUnmanagedWrapper(int barsInProgress, OrderAction action, OrderType orderType, int quantity, double limitPrice, double stopPrice, string ocoId, string signalName)
		{
			return SubmitOrderUnmanaged(barsInProgress, action, orderType, quantity, limitPrice, stopPrice, ocoId, signalName);
		}

		public void ChangeOrderWrapper(Order order, int quantity, double limitPrice, double stopPrice)
		{
			try
			{
				if (order != null && order.OrderType == OrderType.StopMarket)
				{
					// v1.14.98: Validation to prevent "Stop price can't be changed above/below market" crash
					// Use Bid/Ask for accuracy during Realtime/Playback if available, fallback to Close[0]
					double currentAsk = 0;
					double currentBid = 0;

                    // Only try to get Bid/Ask if Realtime or Playback (State check or just catch 0)
					try { currentAsk = GetCurrentAsk(); currentBid = GetCurrentBid(); } catch {}
					
					if (currentAsk == 0) currentAsk = Close[0];
					if (currentBid == 0) currentBid = Close[0];

					// BUY STOP (Short Protection): Must be ABOVE market (Ask)
					if (order.OrderAction == OrderAction.BuyToCover || order.OrderAction == OrderAction.Buy)
					{
						// Strict check: Must be > Ask
						if (stopPrice <= currentAsk)
						{
							Log(string.Format("CRITICAL: Attempted to set BUY STOP @ {0} which is <= Market/Ask ({1}). Clamping to Ask + 5 ticks.", stopPrice, currentAsk));
							stopPrice = currentAsk + (5 * TickSize); // Increased buffer to 5 ticks
						}
					}
					// SELL STOP (Long Protection): Must be BELOW market (Bid)
					else if (order.OrderAction == OrderAction.Sell || order.OrderAction == OrderAction.SellShort)
					{
						// Strict check: Must be < Bid
						if (stopPrice >= currentBid)
						{
							Log(string.Format("CRITICAL: Attempted to set SELL STOP @ {0} which is >= Market/Bid ({1}). Clamping to Bid - 5 ticks.", stopPrice, currentBid));
							stopPrice = currentBid - (5 * TickSize); // Increased buffer to 5 ticks
						}
					}
				}
				
				ChangeOrder(order, quantity, limitPrice, stopPrice);
			}
			catch (Exception ex)
			{
				Log($"ERROR in ChangeOrder: {ex.Message} (Order: {order?.Name} Qty: {quantity} Stop: {stopPrice})");
			}
		}

		public void CancelOrderWrapper(Order order)
		{
			CancelOrder(order);
		}
		
		// Clear log file on strategy restart (overwrite instead of append)
		private void ClearLogFile()
		{
			if (helpers != null) helpers.ClearLogFile();
		}


		/// <summary>
		/// Checks for chart lag effectively blocking orders if data is too old.
		/// </summary>
		// =========================================================
		// LAG FILTER - Check chart data freshness
		// Skip lag check during Playback
		// =========================================================
		public bool CheckChartLag()
		{
			// Only check in Realtime (not Playback/Historical)
			if (State != State.Realtime) return true;
			
			// v1.14.26: Skip lag check during Playback (chart time != system time)
			if (Connection.PlaybackConnection != null) 
			{
				isLagAlertActive = false;
				return true;
			}
			
			// Calculate lag: System time vs last bar time
			TimeSpan chartLag = Core.Globals.Now - Time[0];
			currentChartLag = chartLag.TotalSeconds;
			
			// For 1-minute bars, we expect up to 60 seconds "lag" normally
			// So we only flag if it exceeds the bar period + threshold
			double expectedLag = BarsPeriod.Value * 60; // Bar period in seconds
			double actualExcessLag = currentChartLag - expectedLag;
			
			// Check if exceeds threshold
			if (actualExcessLag > MaxChartLagSeconds)
			{
				isLagAlertActive = true;
				Log(string.Format("{0} LAG ALERT: Chart excess lag {1:F2}s > {2}s threshold - ORDERS BLOCKED", 
					Time[0], actualExcessLag, MaxChartLagSeconds));
				
				// v1.15.38: Send email alert for significant lag
				SendCriticalAlert("LAG DETECTED", string.Format("Chart lag {0:F1}s exceeds {1}s threshold. Orders blocked.", actualExcessLag, MaxChartLagSeconds));
				
				return false; // Not safe to trade
			}
			
			isLagAlertActive = false;
			return true; // Safe to trade
		}


		// =========================================================
		// TRIGGER LABELS - Distancia basada en ATR
		// =========================================================
		public void DrawTriggerLabel(string tag, bool isShort, int barsAgo, double anchorPrice)
		{
			// Calcular offset basado en ATR (consistente entre instrumentos)
			// Usa la propiedad LabelDistanceATR configurable desde el panel
			double atrOffset = (atr != null && atr[0] > 0) ? atr[0] * LabelDistanceATR : TickSize * 10;
			double textOffset = atrOffset * 1.5; // Texto un poco más lejos que la flecha
			
			// Colores: Cyan para Short, Lime para Long
			Brush color = isShort ? Brushes.Cyan : Brushes.Lime;
			
			// Calcular precios para flecha y texto
			double arrowPrice = isShort ? anchorPrice + atrOffset : anchorPrice - atrOffset;
			double textPrice = isShort ? anchorPrice + textOffset : anchorPrice - textOffset;
			
			// Dibujar flecha (si está habilitado)
			if (LabelShowArrow)
			{
				if (isShort)
					Draw.ArrowDown(this, tag, true, barsAgo, arrowPrice, color);
				else
					Draw.ArrowUp(this, tag, true, barsAgo, arrowPrice, color);
			}
			
			// Dibujar texto (si está habilitado)
			if (LabelShowText)
			{
				string label = isShort ? "Short" : "Long";
				SimpleFont font = new SimpleFont("Arial", LabelFontSize);
				// v1.11.8: Usar propiedad LabelTextOffset configurable
				// Short: texto ARRIBA de la flecha (valor positivo)
				// Long: texto ABAJO de la flecha (valor negativo)
				int textPixelOffset = isShort ? LabelTextOffset : -LabelTextOffset;
				Draw.Text(this, tag + "_Txt", true, label, barsAgo, arrowPrice, textPixelOffset, color, font, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
			}
		}

		// =========================================================
		// INTELLIGENT RESTART EVALUATION (No Position)
		// =========================================================
		private void EvaluateRestartNoPosition()
		{
			Log(Time[0] + " " + StrategyVersion + " RESTART EVAL: Checking for pending orders or valid setup to continue...");
			
			// STEP 1: Check for pending entry order in Account
			Order pendingEntry = null;
			foreach(Order o in Account.Orders)
			{
				if (o.Instrument.FullName == Instrument.FullName && 
					o.Name.Contains("EntryA+_") &&
					(o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted))
				{
					pendingEntry = o;
					break;
				}
			}
			
			if (pendingEntry != null)
			{
				Log(Time[0] + " " + StrategyVersion + " RESTART: Found pending entry order: " + pendingEntry.Name + " @ " + pendingEntry.LimitPrice);
				
				// Evaluate if setup is still valid
				double entryPrice = pendingEntry.LimitPrice;
				bool isShort = pendingEntry.OrderAction == OrderAction.SellShort;
				
				// Check if price crossed the entry (opportunity may have passed)
				bool entryCrossed = isShort ? (Low[0] <= entryPrice) : (High[0] >= entryPrice);
				
				if (entryCrossed && Position.MarketPosition == MarketPosition.Flat)
				{
					// Price crossed but we didn't get filled - cancel
					Log(Time[0] + " " + StrategyVersion + " RESTART: Entry price crossed but not filled. Cancelling order.");
					try { CancelOrder(pendingEntry); } catch {}
					currentEntryState = EntryState.Idle;
					return;
				}
				
				// Check R/R - estimate SL at StopLossTicks from entry
				double slDistance = StopLossTicks * TickSize;
				double estimatedSL = isShort ? entryPrice + slDistance : entryPrice - slDistance;
				
				// Find a target to calculate R/R
				double targetPrice = isShort ? GetCurrentLowVWAP() : GetCurrentHighVWAP();
				if (targetPrice <= 0) targetPrice = isShort ? entryPrice - (slDistance * 2) : entryPrice + (slDistance * 2);
				
				double risk = Math.Abs(entryPrice - estimatedSL);
				double reward = Math.Abs(targetPrice - entryPrice);
				double rr = risk > 0 ? reward / risk : 0;
				
				if (rr < 1.0)
				{
					Log(Time[0] + " " + StrategyVersion + " RESTART: R/R too low (" + rr.ToString("F2") + "). Cancelling order.");
					try { CancelOrder(pendingEntry); } catch {}
					currentEntryState = EntryState.Idle;
					return;
				}
				
				// Setup still valid - adopt the order
				Log(Time[0] + " " + StrategyVersion + " RESTART: Setup valid. R/R=" + rr.ToString("F2") + ". Adopting order.");
				entryOrder = pendingEntry;
				currentEntryState = EntryState.workingOrder;
				isShortSetup = isShort;
				setupAnchorPrice = estimatedSL;
				return;
			}
			
			// STEP 2: No pending order - check if we can find a valid setup to continue
			SessionLevel validLevel = null;
			foreach (var lvl in activeLevels)
			{
				if (lvl.IsMitigated) continue;
				
				// v1.14.95: ROBUST SESSION COMPLETION CHECK (RESTART LOGIC)
                // Ensure we don't restart trading on a level that is still forming (Overnight Session)
                if (nyTimeZone != null && chartTimeZone != null) 
                {
                    DateTime chartTime = Time[0];
                    DateTime currentNyTime = TimeZoneInfo.ConvertTime(chartTime, chartTimeZone, nyTimeZone);
                    DateTime startNyTime = TimeZoneInfo.ConvertTime(lvl.StartTime, chartTimeZone, nyTimeZone);
                    
                    TimeSpan levelStartTs = startNyTime.TimeOfDay;
                    TimeSpan levelEndTs = lvl.ActualSessionEnd; 
                    bool isOvernightSession = levelStartTs > levelEndTs;

                    if (isOvernightSession)
                    {
                        // Case A: Still on Start Day -> BLOCK
                        if (currentNyTime.Date == startNyTime.Date) continue;
                        // Case B: Next Day (Morning) -> BLOCK
                        if (currentNyTime.Date == startNyTime.Date.AddDays(1) && currentNyTime.TimeOfDay < levelEndTs) continue;
                    }
                    else
                    {
                        // Intraday -> BLOCK if same day active
                        if (currentNyTime.Date == startNyTime.Date && currentNyTime.TimeOfDay < levelEndTs) continue;
                    }
                }
                else
                {
                    // Fallback (unsafe)
                    if (lvl.StartTime.Date == Time[0].Date) continue;
                }
				
				bool wasTouched = false;
				bool priceOnCorrectSide = false;
				
				if (lvl.IsResistance)
				{
					wasTouched = High[0] >= lvl.Price - (3 * TickSize);
					priceOnCorrectSide = Close[0] < lvl.Price;
				}
				else
				{
					wasTouched = Low[0] <= lvl.Price + (3 * TickSize);
					priceOnCorrectSide = Close[0] > lvl.Price;
				}
				
				if (wasTouched && priceOnCorrectSide && lvl.EntryAttempts < MaxRetriesPerLevel)
				{
					validLevel = lvl;
					break;
				}
			}
			
			if (validLevel != null)
			{
				Log(Time[0] + " " + StrategyVersion + " RESTART: Found valid level: " + validLevel.Name + ". Setting to WaitingForConfirmation.");
				setupLevelName = validLevel.Name;
				setupLevelTime = validLevel.StartTime; // v1.14.64 FIX: Initialize time for correct lookup
				setupAnchorPrice = validLevel.Price;
				isShortSetup = validLevel.IsResistance;
				currentEntryState = EntryState.WaitingForConfirmation;
				return;
			}
			
			// STEP 3: Nothing valid found - reset to Idle
			Log(Time[0] + " " + StrategyVersion + " RESTART: No valid setup found. Starting fresh.");
			currentEntryState = EntryState.Idle;
			setupLevelName = "";
			setupAnchorPrice = 0;
		}

		// =========================================================
		// STATE PERSISTENCE (XML)
		// =========================================================
		



		

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				// CRITICAL: Unmanaged Mode (Moved to end)
				// IsUnmanaged = true; // Moved down
				
				// ...
				Description									= @"Advanced Session Levels Strategy with VWAP and R/R Filters.";
				Name									= "SessionLevelsStrategy_2026_01_18_16";
				Calculate									= Calculate.OnEachTick;
				EntriesPerDirection							= 4; // Visual reference (Unmanaged ignores this limit)
				EntryHandling								= EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy				= false; // Disabled to prevent Playback errors
				ExitOnSessionCloseSeconds					= 30;
				IsFillLimitOnTouch							= false;
				MaximumBarsLookBack							= MaximumBarsLookBack.TwoHundredFiftySix; // REVERTED FROM INFINITE 
				OrderFillResolution							= OrderFillResolution.Standard;
				Slippage									= 0;
				StartBehavior								= StartBehavior.ImmediatelySubmit; // FIX (v1.7.8): Allow start with Zombie positions to kill them
				TimeInForce									= TimeInForce.Gtc; // Aligned with RLS
				TraceOrders									= false;
				RealtimeErrorHandling						= RealtimeErrorHandling.StopCancelClose;
				IsAutoScale = false; // Fix v1.14.51: Prevent plots from squashing the chart
				IsOverlay = true;
				
				// IsUnmanaged moved to top
				
				// Add Plots for VWAP
		// Disable AutoScale to prevent chart compression
		AddPlot(Brushes.White, "HighVWAP"); // Values[0]
		AddPlot(Brushes.White, "LowVWAP");  // Values[1]
				// Trade VWAP is calculated internally but NOT plotted (v1.10.31)
				
				// FINAL FORCE: Unmanaged Mode
				// FINAL FORCE: Unmanaged Mode
				IsUnmanaged = true; // Enabled for v1.7.0 Unmanaged Refactor
			}
			else if (State == State.Configure)
			{
				// v1.14.31: Add tick data series for RelativeDelta indicator
				// This must be done in Configure state, not by the indicator itself
				AddDataSeries(BarsPeriodType.Tick, 1);
			}
			else if (State == State.DataLoaded)
			{
				try
				{
					// v1.14.89: FIX CRASH - Ensure Locks are Initialized
					if (scaledOrdersLock == null) scaledOrdersLock = new object();
				
					// Phase 7: Initialize Helpers FIRST (for Log)
					helpers = new StrategyHelpers(this);

					Log("DEBUG: OnStateChange(DataLoaded) IsUnmanaged = " + IsUnmanaged);
					// Initialize VWAP Calculator Module
					vwapCalc = new VWAPCalculator(this);
				riskManager = new RiskManager(this); // v1.14.76: Apteros Risk
				riskManager.InitializeState();
				sessionManager = new SessionManager(this); // v1.14.45: Fix Initialization
				protectionManager = new OrderProtectionManager(this);
				entryMachine = new EntryStateMachine(this); // Entry State Machine
				
				// v1.14.78: Initialize Persistence
				levelPersistence = new SessionLevelPersistence(this);
				
				// Initialize Helper Indicators
				atr = ATR(14); // For Dynamic Spacing
				
				// Initialize RelativeDelta indicator for Delta analysis
				try
				{
					// Use default parameters - RelativeDelta calculates on tick data
					relativeDelta = RelativeDelta(
						Brushes.RoyalBlue, Brushes.White, Brushes.Silver, 1, Brushes.White, 2, // Colors
						0, 3, false, // MinSize, DaysToLoad, ShowDivs
						Brushes.RoyalBlue, 10, 0, 50, // HorizontalLine params
						true, true, Brushes.Gray, 100, true, Brushes.Gray, 1, 100, // Line2500 params
						true, Brushes.Gray, 1, 100, true, Brushes.Gray, 1, 100, // Line5000 params
						true, Brushes.Gray, 1, 100, true, Brushes.Gray, 1, 100, // Line10000 params
						Brushes.Gray, Brushes.Black); // Label colors
				}
				catch (Exception ex)
				{
					Log("DELTA INIT WARNING: " + ex.Message);
					relativeDelta = null;
				}
				
				// Initialize control buttons
				InitializeControlButtons();
				
				// Initialize TradeAnalyzer CSV Export (Separated by Context)
				try
				{
					string safeInstrument = Instrument.FullName.Replace("/", "-").Replace(":", "-").Replace(" ", "_");
					
					// Export to Strategies/TradeExports/{context}/ folder
					// FIX: Use UserDataDir directly, it already points to "Documents\NinjaTrader 8"
					string strategiesDir = System.IO.Path.Combine(
						NinjaTrader.Core.Globals.UserDataDir.TrimEnd(System.IO.Path.DirectorySeparatorChar),
						"bin", "Custom", "Strategies", "TradeExports");
					
					// Determine context subfolder based on execution state and account
					// Determine context subfolder based on execution state and account
					string contextFolder;
					
					// 1. Backtest detection: Strategy Analyzer has no ChartControl
					if (ChartControl == null)
					{
						contextFolder = "backtest";
					}
					// 2. Playback detection: Check if Playback Connection is active
					else if (NinjaTrader.Cbi.Connection.PlaybackConnection != null && 
							 NinjaTrader.Cbi.Connection.PlaybackConnection.Status == NinjaTrader.Cbi.ConnectionStatus.Connected)
					{
						contextFolder = "playback";
					}
					// 3. Use Account Name directly for automatic detection
					else if (Account != null)
					{
						// Sanitize account name for folder path (remove invalid chars)
						contextFolder = Account.Name.Replace("/", "-").Replace(":", "-").Replace(" ", "_");
					}
					else
					{
						// Fallback if no account info available
						contextFolder = "unknown";
					}
					
					Log("DEBUG_CONTEXT: Account = '" + (Account != null ? Account.Name : "null") + "', Context = '" + contextFolder + "'");
					string exportDir = System.IO.Path.Combine(strategiesDir, contextFolder);

					
					if (!System.IO.Directory.Exists(exportDir))
						System.IO.Directory.CreateDirectory(exportDir);
					
					csvExportPath = System.IO.Path.Combine(exportDir, $"{safeInstrument}.csv");
					
					// Create file with header if doesn't exist
					if (!System.IO.File.Exists(csvExportPath))
					{
						// Added Delta columns for quantitative analysis
						string header = "ID,Instrument,EntryTime,Type,EntryPrice,ExitTime,ExitPrice,Result,PnL,Commission,NetPnL,MAE,MFE,Setup,Attempt,RiskReward,DeltaAtEntry,DeltaDirection,SessionDelta,DeltaAtTP1";
						System.IO.File.WriteAllText(csvExportPath, header + Environment.NewLine);
						Log("CSV EXPORT: Created " + csvExportPath);
					}
					else
					{
						Log("CSV EXPORT: Using existing " + csvExportPath);
					}
				}
				catch (Exception ex)
				{
					Log("CSV EXPORT INIT ERROR: " + ex.Message);
				}

				
				// CACHE SESSION TIMES (Optimization for MES)
				if (sessionIterator == null) sessionIterator = new SessionIterator(Bars);

				try 
				{
					tsAsiaStart = TimeSpan.Parse(AsiaStartTime);
					tsAsiaEnd = TimeSpan.Parse(AsiaEndTime);
					tsEuStart = TimeSpan.Parse(EuropeStartTime);
					tsEuEnd = TimeSpan.Parse(EuropeEndTime);
					tsUsaStart = TimeSpan.Parse(USAStartTime);
					tsUsaEnd = TimeSpan.Parse(USAEndTime);
				}
				catch (Exception ex) { Print("TimeSpan Parse Error: " + ex.Message); }
				
				// AI Filters - Parse zone configuration
				// Auto Load Config has priority
				if (AutoLoadAIConfig) LoadAIConfig();
				ParseEnabledZones();
				
				// Clear Lists
				activeLevels.Clear();
				virginLevels.Clear();
				// PERSISTENCE DISABLED (v1.5.5) - Relying on Chart History
				/*
				try 
				{
					LoadLevels();
				} 
				} 
				catch(Exception ex) { Log("Warning: Failed to load levels: " + ex.Message); }
				*/
				}
				catch (Exception ex)
				{
					// v1.14.89: CATCH CRITICAL STARTUP ERRORS
					NinjaTrader.Code.Output.Process("CRITICAL STRATEGY ERROR (OnStateChange): " + ex.ToString(), PrintTo.OutputTab1);
					Log("CRITICAL STRATEGY ERROR: " + ex.ToString());
				}
			}
			else if (State == State.Transition)
			{
				// MOVED TO OnBarUpdate (v1.7.7) due to NinjaScript State Error
			}
			else if (State == State.Terminated)
			{
				// TERMINATION CLEANUP
				// REVERTED (v1.7.11): Do NOT cancel orders on Termination.
				// Why? If user reloads strategy (F5 or Settings), Termination fires first.
				// If we cancel stops here, the position becomes "naked" for a few seconds until reload is done.
				// Worse, if logic fails to reload managed stops, we are left unsafe.
				// Better approach: Leave orders ALIVE. 
				// The NEW instance (Startup Failsafe) will detect the "Ghost Orders" and cancel them 
				// right before submitting new ones or closing the position.
				
				// v1.12.0: Cleanup control buttons
				CleanupControlButtons();
			}
		}


		// -------------------------------------------------------------------------
		// PERSISTENCE LOGIC (v3 - Safe Mode - Multi-Instrument)
		// -------------------------------------------------------------------------
		private string GetPersistencePath()
		{
			// Safe Filename: Remove slashes or colons from Instrument Name
			string safeName = Instrument.FullName.Replace('/', '-').Replace(':', '-');
			string filename = "SessionLevels_State_" + safeName + "_v3.xml";
			return System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "trace", filename);
		}

		// -------------------------------------------------------------------------
		// CROSS-INSTRUMENT RISK SYNC
		// -------------------------------------------------------------------------
		private static readonly object sharedRiskLock = new object();
		private double lastWrittenRisk = 0;
		private DateTime lastRiskWriteTime = DateTime.MinValue;
		
		// Performance cache for reading
		private double cachedGlobalRisk = 5.0;
		private DateTime lastRiskReadTime = DateTime.MinValue;
		
		private string GetSharedRiskPath()
		{
			return System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "trace", "SharedRisk.txt");
		}
		
		/// <summary>
		/// Writes the current risk (ATR-based) to a shared file for cross-instrument coordination.
		/// </summary>
		public void WriteSharedRisk(double atrRisk)
		{
			// Disable Shared Risk in Backtest/Optimization to prevent state leak
			if (State == State.Historical) return;
            // v1.14.80: Disable in Playback to prevent File I/O Lag
            if (Connection.PlaybackConnection != null) return;

			// Only write if significantly different or every 5 seconds
			if (Math.Abs(atrRisk - lastWrittenRisk) < 1 && (DateTime.Now - lastRiskWriteTime).TotalSeconds < 5)
				return;
				
			try
			{
				lock (sharedRiskLock)
				{
					string path = GetSharedRiskPath();
					string safeName = Instrument.FullName.Replace('/', '-').Replace(':', '-');
					string line = safeName + "|" + atrRisk.ToString("F2") + "|" + DateTime.Now.Ticks;
					
					// Read existing, update our line, write back
					var lines = new System.Collections.Generic.Dictionary<string, string>();
					if (File.Exists(path))
					{
						foreach (var l in File.ReadAllLines(path))
						{
							var parts = l.Split('|');
							if (parts.Length >= 2)
								lines[parts[0]] = l;
						}
					}
					lines[safeName] = line;
					
					// Write all lines
					File.WriteAllLines(path, lines.Values);
					lastWrittenRisk = atrRisk;
					lastRiskWriteTime = DateTime.Now;
					
					// v1.14.75: FIX CACHE - Ensure local cache reflects what we just wrote
					// This prevents ReadMaxSharedRisk() from returning stale lower values from cache
					if (atrRisk > cachedGlobalRisk) cachedGlobalRisk = atrRisk;
				}
			}
			catch { }
		}
		
		/// <summary>
		/// Reads the maximum risk from the shared file to prevent over-leveraging across instruments.
		/// </summary>
		public double ReadMaxSharedRisk()
		{
			// Disable Shared Risk in Backtest/Optimization
			if (State == State.Historical) return RiskPerTradeUSD;
            // v1.14.80: Disable in Playback
            if (Connection.PlaybackConnection != null) return RiskPerTradeUSD;

			// PERFORMANCE: Only read file every 5 seconds, use cache otherwise
			if ((DateTime.Now - lastRiskReadTime).TotalSeconds < 5)
				return Math.Min(cachedGlobalRisk, RiskPerTradeUSD);
			
			double maxRisk = 5.0; // Minimum fallback
			try
			{
				lock (sharedRiskLock)
				{
					string path = GetSharedRiskPath();
					if (!File.Exists(path)) return maxRisk;
					
					foreach (var line in File.ReadAllLines(path))
					{
						var parts = line.Split('|');
						if (parts.Length >= 3)
						{
							// Check if entry is recent (within 300 seconds = 5 minutes)
							long ticks;
							if (long.TryParse(parts[2], out ticks))
							{
								DateTime entryTime = new DateTime(ticks);
								if ((DateTime.Now - entryTime).TotalSeconds > 300)
									continue; // Skip stale entries
							}
							
							// Parse with InvariantCulture to handle decimal point correctly
							double risk;
							if (double.TryParse(parts[1], System.Globalization.NumberStyles.Any, 
								System.Globalization.CultureInfo.InvariantCulture, out risk))
							{
								if (risk > maxRisk) maxRisk = risk;
							}
						}
					}
				}
			}
			catch { }
			
			// Update cache
			cachedGlobalRisk = maxRisk;
			lastRiskReadTime = DateTime.Now;
			
			// Cap at RiskPerTradeUSD
			return Math.Min(maxRisk, RiskPerTradeUSD);
		}

		private void SaveLevels()
		{
			// Only save if we have data and logic initialized
			if (activeLevels == null || activeLevels.Count == 0) return;

			string path = GetPersistencePath();
			
			// Map specific List<SessionLevel> to List<SessionLevelData>
			List<SessionLevelData> dataToSave = new List<SessionLevelData>();
			foreach(var level in activeLevels)
			{
				dataToSave.Add(new SessionLevelData
				{
					Name = level.Name,
					Price = level.Price,
					StartTime = level.StartTime,
					EndTime = level.EndTime,
					MitigationTime = level.MitigationTime,
					IsResistance = level.IsResistance,
					IsMitigated = level.IsMitigated,
					VolSum = level.VolSum,
					PvSum = level.PvSum,
					Tag = level.Tag
				});
			}

			try
			{
				// Ensure directory exists
				string dir = System.IO.Path.GetDirectoryName(path);
				if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);

				XmlSerializer serializer = new XmlSerializer(typeof(List<SessionLevelData>));
				using (StreamWriter writer = new StreamWriter(path))
				{
					serializer.Serialize(writer, dataToSave);
				}
				Log(DateTime.Now + " State Saved: " + dataToSave.Count + " levels to " + path);
			}
			catch (Exception ex)
			{
				Print("SaveLevels Exception: " + ex.Message);
			}
		}

		private void LoadLevels()
		{
			string path = GetPersistencePath();
			if (!File.Exists(path)) return;
			
			// Define variable at method scope
			DateTime firstBarTime = (Bars != null && Bars.Count > 0) ? Bars.GetTime(0) : DateTime.MinValue;

			// 1. GAP DETECTION
			try
			{
				if (Bars != null && Bars.Count > 0)
				{
					DateTime fileTime = File.GetLastWriteTime(path);
					// Removed local declaration to prevent shadowing/scope issues
					
					// If the file is OLDER than the First Bar loaded, we have a blind spot.
					if (fileTime < firstBarTime) 
					{
						gapDetected = true;
						Print("WARNING: Persistence Gap Detected! File Time: " + fileTime + " < First Bar: " + firstBarTime);
					}
				}
			}
			catch {}

			try
			{
				XmlSerializer serializer = new XmlSerializer(typeof(List<SessionLevelData>));
				List<SessionLevelData> loadedData;
				
				using (StreamReader reader = new StreamReader(path))
				{
					loadedData = (List<SessionLevelData>)serializer.Deserialize(reader);
				}

				if (loadedData != null && loadedData.Count > 0)
				{
					// 2. SANITY CHECK (Auto-Mitigate Ghost Lines)
					double sanityPrice = -1;
					if (Bars != null && Bars.Count > 0) sanityPrice = Bars.GetOpen(0);

					int count = 0;
					int mitigatedCount = 0;
					
					foreach (var d in loadedData)
					{
						if (activeLevels.Any(l => l.Tag == d.Tag)) continue;

						SessionLevel newLvl = new SessionLevel
						{
							Name = d.Name,
							Price = d.Price,
							StartTime = d.StartTime,
							EndTime = d.EndTime,
							MitigationTime = d.MitigationTime,
							IsResistance = d.IsResistance,
							IsMitigated = d.IsMitigated,
							Tag = d.Tag,
							VolSum = d.VolSum,
							PvSum = d.PvSum,
							JustReset = false
						};
						
						// Check Staleness Gap (STRICT)
						// If the level START time is older than our First Bar, we are blind to its history.
						// We MUST skip it to prevent "Time Warp" lines.
						if (d.StartTime < firstBarTime)
						{
							gapDetected = true; 
							gapCount++;
							continue;
						}

						// SANITY LOGIC
						if (sanityPrice > 0 && !newLvl.IsMitigated)
						{
							if (newLvl.IsResistance && sanityPrice > newLvl.Price)
							{
								newLvl.IsMitigated = true;
								newLvl.MitigationTime = Bars.GetTime(0); // Mark as broken at open
								mitigatedCount++;
							}
							else if (!newLvl.IsResistance && sanityPrice < newLvl.Price)
							{
								newLvl.IsMitigated = true;
								newLvl.MitigationTime = Bars.GetTime(0);
								mitigatedCount++;
							}
						}
						
						// Restore Color
						if (d.Name.Contains("Asia")) newLvl.Color = Brushes.White;
						else if (d.Name.Contains("Europe")) newLvl.Color = Brushes.Yellow;
						else if (d.Name.Contains("USA")) newLvl.Color = Brushes.RoyalBlue;
						else newLvl.Color = Brushes.Gray;

						activeLevels.Add(newLvl);
						count++;
					}
					
					string msg = "State Loaded: " + count + " levels restored.";
					if (mitigatedCount > 0) msg += " (Auto-Mitigated " + mitigatedCount + " ghosts due to Gap).";
					if (gapCount > 0) msg += " (Skipped " + gapCount + " stale levels. Load more days).";
					Log(DateTime.Now + " " + msg);
				}
			}
			catch (Exception ex)
			{
				Print("LoadLevels Exception: " + ex.Message);
			}
		}

		// TimeZone Caching
		// TimeZone Caching (moved to public section - lines 173-174)

		private bool timeZonesLoaded = false;
		private double lastVol = 0;

		// Level Persistence

		
		// activeLevels moved to public section (line 176)
		private List<SessionLevel> virginLevels = new List<SessionLevel>();

		// Strategy Initialization Flag
		private bool isStrategyInitialized = false;
		private bool isRealtimeInitialized = false; // v1.7.7 Cleanup Flag
		[XmlIgnore] public int realtimeStartBar = -1; // v1.10.28: Bar when strategy entered Realtime (for fresh signals only)
		[XmlIgnore] public HashSet<string> skippedLevelsAtStartup = new HashSet<string>(); // v1.10.29: Levels already touched at startup
		[XmlIgnore] public bool gapDetected = false;
		[XmlIgnore] public int gapCount = 0;
		private DateTime lastWeeklyReset = DateTime.MinValue; // v1.10.37: Track weekly reset

		// -------------------------------------------------------------------------
		// WEEKLY RESET LOGIC (v1.10.37)
		// -------------------------------------------------------------------------
		private void CheckWeekEndReset()
		{
			if (nyTimeZone == null || chartTimeZone == null) return;
			
			DateTime nyTime = TimeZoneInfo.ConvertTime(Time[0], chartTimeZone, nyTimeZone);
			
			// Calculate this week's Friday 6pm
			int daysToFriday = ((int)DayOfWeek.Friday - (int)nyTime.DayOfWeek + 7) % 7;
			
			// Determine the target Friday based on current time
			DateTime targetFriday;
			
			if (daysToFriday == 0 && nyTime.TimeOfDay >= TimeSpan.Parse("18:00"))
			{
				// It is Friday evening/night -> The reset point is TODAY at 18:00
				targetFriday = nyTime.Date;
			}
			else if (daysToFriday == 0)
			{
				// It is Friday morning (before 18:00) -> The reset point was LAST Friday (7 days ago)
				targetFriday = nyTime.Date.AddDays(-7);
			}
			else
			{
				// It is another day (Sat-Thu) -> Calculate the previous Friday
				// Example: Saturday (daysToFriday=6) -> Last Friday was in 6 days. Last Friday was 1 day ago.
				// If Monday (1), next Friday is in 4 days. Last Friday was 3 days ago.
				// The formula used previously was confusing. Let's simplify.
				
				// Standard "Find Previous Friday" logic:
				// Subtract days until we hit Friday.
				int daysSinceFriday = ((int)nyTime.DayOfWeek - (int)DayOfWeek.Friday + 7) % 7;
				if (daysSinceFriday == 0) daysSinceFriday = 7; // Should be covered above but safety check
				
				targetFriday = nyTime.Date.AddDays(-daysSinceFriday);
			}

			DateTime lastFriday6pm = targetFriday.Add(TimeSpan.Parse("18:00"));
			
			// Convert to chart timezone for comparison
			DateTime lastFriday6pmChart = TimeZoneInfo.ConvertTime(lastFriday6pm, nyTimeZone, chartTimeZone);
			
			// Check if we need to reset
			if (lastFriday6pmChart > lastWeeklyReset && currentEntryState != EntryState.PositionActive)
			{
				lastWeeklyReset = lastFriday6pmChart;
				
				Log(Time[0] + " WEEK RESET - State cleared for new trading week (Last Friday 6pm: " + lastFriday6pm + " NY)");
				
				// Diagnostic logging - show active levels vs current price at week start
				if (activeLevels != null && activeLevels.Count > 0)
				{
					Log("LEVEL SUMMARY (Price=" + Close[0] + "):");
					foreach (var lvl in activeLevels)
					{
						if (!lvl.IsMitigated)
						{
							string above = Close[0] > lvl.Price ? "ABOVE" : "BELOW";
							double dist = Math.Abs(Close[0] - lvl.Price);
							double distTicks = dist / TickSize;
						Log(string.Format("  {0} @ {1} | Currently {2} by {3:F0} ticks", lvl.Name, lvl.Price, above, distTicks));
						}
					}
				}
				else
				{
					Log("LEVEL SUMMARY: No active levels at week start!");
				}
				
				// Cancel pending entry if any
				if (entryOrder != null && (entryOrder.OrderState == OrderState.Working || entryOrder.OrderState == OrderState.Accepted))
				{
					try { CancelOrder(entryOrder); } catch {}
				}
				
				// Reset ALL setup state
				currentEntryState = EntryState.Idle;
				setupLevelName = "";
				setupLevelTime = DateTime.MinValue;
				setupAnchorPrice = 0;
				validatedTp1Price = 0; // v1.15.26: Split from validatedTargetPrice
				validatedTp2Price = 0; // v1.15.26: Split from validatedTargetPrice
				cachedOppositeLevel = null;
				oppositeSearchDone = false; // v1.14.32: Reset search flag
				isInternalLevel = false;
				waitingForVwapMitigation = false;
				currentVwapNumber = 1;
				
				// Reset Adhoc VWAP via Module
				if (vwapCalc != null) vwapCalc.ClearAdhoc();

                if (protectionManager != null) protectionManager.ResetEntryState();
				
				// Clear skipped levels from last week
				skippedLevelsAtStartup.Clear();
			}
			
			// v1.14.61: DAILY RESET Logic (Refined)
			// User requires reset at 18:00 (Asia Open), not just date change.
			// Logic: If we are past 18:00 AND we haven't reset for this calendar date yet.
			DateTime currentDate = Time[0].Date;
			TimeSpan sessionStartTime = new TimeSpan(18, 0, 0); // 18:00 PM EST/Server Time
			
			if (Time[0].TimeOfDay >= sessionStartTime && lastTradingDate < currentDate)
			{
				// Only reset if flat (safety) - v1.14.81: Trust MarketPosition over internal state (Self-Healing)
				if (currentEntryState != EntryState.PositionActive || Position.MarketPosition == MarketPosition.Flat)
				{
					Log(Time[0] + " SESSION RESET: Asia Open (18:00). Clearing previous session state.");
					
					currentEntryState = EntryState.Idle;
					currentVwapNumber = 1; // Reset attempts to 1/20
					waitingForVwapMitigation = false;
					setupLevelName = "";
					
					// Also reset protection state
					if (protectionManager != null) protectionManager.ResetEntryState();
					
					// Mark this date as reset
					lastTradingDate = currentDate;
				}
				else
				{
					// v1.14.74: Trade crosses 18:00 - Activate Trade VWAP Extension
					// The Global VWAP will reset, but we need to continue with the accumulated value for TP1
					if (!IsTradeVwapExtended && vwapCalc != null)
					{
						// Copy the accumulated values from Global to Trade VWAP before Global resets
						vwapCalc.InheritFromGlobal(isShortSetup);
						IsTradeVwapExtended = true;
						Log(Time[0] + " TRADE VWAP EXTENDED: Position active at 18:00 crossing. TP1 will now use Trade VWAP.");
					}
					
					// Log postponement (once per minute to avoid spam)
					if (Convert.ToInt32(Time[0].TimeOfDay.TotalSeconds) % 60 == 0)
						Log(Time[0] + " SESSION RESET POSTPONED: Position Active. Will retry when Flat.");
				}
			}
		}

		protected override void OnBarUpdate()
		{
			try
			{
			// Skip processing for tick data series (BarsInProgress == 1)
			// Only process main price series to avoid index errors with PlotBrushes
			if (BarsInProgress != 0)
				return;
			
			// DIAGNOSTIC HEARTBEAT REMOVED
		
		// Initialize timezones for SessionManager
		if (nyTimeZone == null || chartTimeZone == null)
		{
			try
			{
				nyTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
				chartTimeZone = TimeZoneInfo.Local;
			}
			catch
			{
				nyTimeZone = TimeZoneInfo.Utc;
				chartTimeZone = TimeZoneInfo.Local;
			}
		}
			
			// AUTO-PAUSE ON LAG - Pause strategy when lag > 60s without position
			// This prevents erratic behavior during market open or connection issues
			// SKIP in Playback mode - Time[0] is historical so lag calculation is meaningless
			bool isPlayback = (Connection.PlaybackConnection != null);
			if (State == State.Realtime && !isPlayback)
			{
				double lagSeconds = (DateTime.Now - Time[0]).TotalSeconds;
				
				if (lagSeconds > 60 && Position.MarketPosition == MarketPosition.Flat)
				{
					if (!isLagPaused)
					{
						isLagPaused = true;
						string msg = "LAG_PAUSE: Lag > 60s detected (" + lagSeconds.ToString("F0") + "s). Pausing until connection normalizes.";
						Log(msg);
						Alert("LagDetected", Priority.High, msg, NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav", 10, Brushes.Red, Brushes.White);
					}
					return; // Skip all calculations until lag normalizes
				}
				else if (isLagPaused && lagSeconds < 10)
				{
					isLagPaused = false;
					Log("LAG_RESUME: Connection normalized (lag=" + lagSeconds.ToString("F1") + "s). Resuming calculations.");
				}
				else if (isLagPaused)
				{
					return; // Still paused, skip calculations
				}
			}
				
			try
			{
			// Heartbeat REMOVED

			// STARTUP CLEANUP FAILSAFE
			// Must run inside OnBarUpdate when State is Realtime to allow Order Submission
			// v1.10.28: Skip if overnight positions are allowed (user wants to keep positions)
			if (State == State.Realtime && !isRealtimeInitialized)
			{
			isRealtimeInitialized = true;
				realtimeStartBar = CurrentBar; // v1.10.28: Track when we went live
				
				// v1.11.13: Clear log file on strategy restart
				ClearLogFile();
				
				// v1.10.39: Track if position exists (used for historical state reset later)
				bool hasExistingPosition = false;
				
				// 1. Zombie Position Cleanup (ACCOUNT LEVEL - v1.7.8)
				// Strategy 'Position' starts flat on reload, checking that is useless for Zombies.
				// We must check if the Account has a position for this instrument.
				if (Account != null)
				{
					// v1.13.10: DIAGNOSTIC LOGS for reconnection debugging
					Log($"DEBUG_RECONNECT: Checking Account.Positions. Strategy Position.Qty={Position.Quantity} MarketPosition={Position.MarketPosition}");
					int posCount = 0;
					foreach(Position p in Account.Positions)
					{
						posCount++;
						Log($"DEBUG_RECONNECT: Account.Position[{posCount}] Instrument={p.Instrument.FullName} Qty={p.Quantity} Dir={p.MarketPosition}");
						
						if (p.Instrument == Instrument && p.MarketPosition != MarketPosition.Flat)
						{
							hasExistingPosition = true;
							
							// v1.10.38: ADOPT position and its orders
							currentEntryState = EntryState.PositionActive;
							isShortSetup = (p.MarketPosition == MarketPosition.Short);
							
							Log(Time[0] + " STARTUP ADOPT: Found position Qty=" + p.Quantity + 
								" Dir=" + p.MarketPosition + " - Adopting state and orders...");
						}
					}
					Log($"DEBUG_RECONNECT: Total Account.Positions count = {posCount}");
					
					// v1.10.38: If we have a position, ADOPT orders instead of cancelling
					if (hasExistingPosition)
					{
						// v1.14.34: CRITICAL FIX - Reset protection counters before adopting
						// This prevents accumulation of qty from previous strategy instances
						protectedTp1Qty = 0;
						protectedTp2Qty = 0;
						
						foreach(Order o in Account.Orders)
						{
							if (o.Instrument.FullName == Instrument.FullName && 
								(o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted))
							{
								if (o.Name.StartsWith("SL_") || o.Name.Contains("_SL"))
								{
									stopOrder = o;
									Log(Time[0] + " STARTUP ADOPT: Recovered SL order: " + o.Name + " Qty=" + o.Quantity);
								}
								else if (o.Name.StartsWith("TP1_") || o.Name.Contains("_TP1"))
								{
									tp1Order = o;
									protectedTp1Qty = o.Quantity; // v1.14.34: Adopt actual qty
									Log(Time[0] + " STARTUP ADOPT: Recovered TP1 order: " + o.Name + " Qty=" + o.Quantity);
								}
								else if (o.Name.StartsWith("TP2_") || o.Name.Contains("_TP2"))
								{
									tp2Order = o;
									protectedTp2Qty = o.Quantity; // v1.14.34: Adopt actual qty
									Log(Time[0] + " STARTUP ADOPT: Recovered TP2 order: " + o.Name + " Qty=" + o.Quantity);
								}
							}
						}
						
						Log(Time[0] + " STARTUP ADOPT COMPLETE: SL=" + (stopOrder != null) + 
							" TP1=" + (tp1Order != null) + " TP2=" + (tp2Order != null) +
							" | protectedTp1Qty=" + protectedTp1Qty + " protectedTp2Qty=" + protectedTp2Qty);
						
						// v1.10.41: EMERGENCY PROTECTION - If no SL found, create protection or close
						if (stopOrder == null)
						{
							Log(Time[0] + " EMERGENCY: Adopted position has NO protection! Attempting to create...");
							
							// Get position details
							double avgPrice = 0;
							int posQty = 0;
							foreach(Position p in Account.Positions)
							{
								if (p.Instrument == Instrument && p.MarketPosition != MarketPosition.Flat)
								{
									avgPrice = p.AveragePrice;
									posQty = Math.Abs(p.Quantity);
									break;
								}
							}
							
							if (avgPrice > 0 && posQty > 0)
							{
								// Calculate emergency SL using StopLossTicks parameter
								double slDistance = StopLossTicks * TickSize;
								double emergencySlPrice = isShortSetup ? avgPrice + slDistance : avgPrice - slDistance;
								
								// Validate SL is on correct side
								bool slValid = isShortSetup ? (emergencySlPrice > Close[0]) : (emergencySlPrice < Close[0]);
								
								if (slValid)
								{
									// Create emergency SL
									string slTag = "SL_Emergency_" + (isShortSetup ? "Short" : "Long");
									OrderAction slAction = isShortSetup ? OrderAction.BuyToCover : OrderAction.Sell;
									
									try
									{
										stopOrder = SubmitOrderUnmanaged(0, slAction, OrderType.StopMarket, posQty, 0, emergencySlPrice, "", slTag);
										Log(Time[0] + " EMERGENCY SL CREATED: " + slTag + " @ " + emergencySlPrice + " Qty=" + posQty);
										
										// Try to create TP at 2:1 minimum
										double tpDistance = slDistance * 2;
										double emergencyTpPrice = isShortSetup ? avgPrice - tpDistance : avgPrice + tpDistance;
										
										string tpTag = "TP1_Emergency_" + (isShortSetup ? "Short" : "Long");
										
										tp1Order = SubmitOrderUnmanaged(0, slAction, OrderType.Limit, posQty, emergencyTpPrice, 0, "", tpTag);
										Log(Time[0] + " EMERGENCY TP CREATED: " + tpTag + " @ " + emergencyTpPrice + " Qty=" + posQty);
									}
									catch (Exception ex)
{
Log(Time[0] + " EMERGENCY ORDER FAILED: " + ex.Message);
// CRITICAL - If cant protect, CLOSE
Log(Time[0] + " CRITICAL: Cannot protect. CLOSING.");
try {
// v1.15.19: Use Bid/Ask with buffer for slippage protection on emergency market orders
if (isShortSetup)
{
	double askPrice = GetCurrentAsk();
	double limitPrice = Instrument.MasterInstrument.RoundToTickSize(askPrice + (2 * TickSize));
	Log(Time[0] + " EMERGENCY CLOSE SHORT: Ask=" + askPrice + " Limit=" + limitPrice);
	SubmitOrderUnmanaged(0, OrderAction.BuyToCover, OrderType.Market, posQty, limitPrice, 0, "", "EmergencyClose_Short");
}
else
{
	double bidPrice = GetCurrentBid();
	double limitPrice = Instrument.MasterInstrument.RoundToTickSize(bidPrice - (2 * TickSize));
	Log(Time[0] + " EMERGENCY CLOSE LONG: Bid=" + bidPrice + " Limit=" + limitPrice);
	SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.Market, posQty, limitPrice, 0, "", "EmergencyClose_Long");
}
Log(Time[0] + " EMERGENCY CLOSE: Qty=" + posQty);
currentEntryState = EntryState.Idle;
} catch (Exception ex2) { Log(Time[0] + " FATAL: " + ex2.Message); }
}
								}
								else
								{
									// SL would be on wrong side - position already in loss beyond SL
									Log(Time[0] + " EMERGENCY: SL invalid (price already beyond). CLOSING POSITION.");

									try
									{
										// v1.15.19: Use Bid/Ask with buffer for slippage protection on emergency market orders
										if (isShortSetup)
										{
											double askPrice = GetCurrentAsk();
											double limitPrice = Instrument.MasterInstrument.RoundToTickSize(askPrice + (2 * TickSize));
											Log(Time[0] + " EMERGENCY CLOSE SHORT: Ask=" + askPrice + " Limit=" + limitPrice);
											SubmitOrderUnmanaged(0, OrderAction.BuyToCover, OrderType.Market, posQty, limitPrice, 0, "", "EmergencyClose_Short");
										}
										else
										{
											double bidPrice = GetCurrentBid();
											double limitPrice = Instrument.MasterInstrument.RoundToTickSize(bidPrice - (2 * TickSize));
											Log(Time[0] + " EMERGENCY CLOSE LONG: Bid=" + bidPrice + " Limit=" + limitPrice);
											SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.Market, posQty, limitPrice, 0, "", "EmergencyClose_Long");
										}

										Log(Time[0] + " EMERGENCY CLOSE SUBMITTED for Qty=" + posQty);
										currentEntryState = EntryState.Idle;
									}
									catch (Exception ex)
									{
										Log(Time[0] + " EMERGENCY CLOSE FAILED: " + ex.Message);
									}
								}
							}
						}
					}
					else
					{
						// INTELLIGENT RESTART EVALUATION
						// Instead of blindly cancelling orders, evaluate if setup is still valid
						EvaluateRestartNoPosition();
					}
				}
				
				// DETECT LEVELS ALREADY BEING TOUCHED AT STARTUP
				// These levels are "spent" - we should NOT trigger on them
				foreach (var lvl in activeLevels)
				{
					if (lvl.IsMitigated) continue; // Already mitigated = already spent
					
					// Check if price is currently AT this level (within 5 ticks)
					double tolerance = 5 * TickSize;
					bool isBeingTouched = false;
					
					if (lvl.IsResistance && High[0] >= lvl.Price - tolerance && High[0] <= lvl.Price + tolerance)
						isBeingTouched = true;
					if (!lvl.IsResistance && Low[0] >= lvl.Price - tolerance && Low[0] <= lvl.Price + tolerance)
						isBeingTouched = true;
					
					if (isBeingTouched)
					{
						skippedLevelsAtStartup.Add(lvl.Name);
						Log(Time[0] + " STARTUP: Level '" + lvl.Name + "' is already being touched - will be skipped.");
					}
				}
				
				// Historical state now handled by EvaluateRestartNoPosition()
			}

			// v1.14.62: Removed 'CurrentBar < 20' check here to allow Session/Levels calculation from Bar 0.
			// The check is moved down to protect only the Entry Logic.

			
			// Reset state at week end (Friday 6pm NY) or new week start
			CheckWeekEndReset();
			
			// V_STACK: Reset Stack per bar update cycle (re-draws everything)
            verticalUnit = (atr != null && atr[0] > 0) ? atr[0] * 0.1 : TickSize;
			stackHighY = double.MinValue;
			stackLowY = double.MaxValue;
			
			// INITIALIZATION (Snap Anchors to start)
			if (!isStrategyInitialized)
			{
				isStrategyInitialized = true;
				highAnchorBar = CurrentBar;
				lowAnchorBar = CurrentBar;
				ethHighPrice = High[0];
				ethLowPrice = Low[0];
				
				// Reset VWAPs to start fresh here
				// Reset VWAPs to start fresh here
				// vwapCalc handles this, but we can force re-init
                if (vwapCalc != null) vwapCalc = new VWAPCalculator(this);
				
				// Init AdHoc
				adhocLastBar = CurrentBar;
				lastVol = Volume[0]; // Set volume baseline
				
				// Don't modify plots on init frame
				return;
			}
			
			// Initialize TimeZones & Lists once
			if (!timeZonesLoaded)
			{
				try 
				{
					// "Eastern Standard Time" handles both EST and EDT automatically on Windows
					try { nyTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
                    catch { nyTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); } // Linux/Mac fallback?
                    
                    if (nyTimeZone == null) nyTimeZone = TimeZoneInfo.Local; // Safe Fallback

					// Get the TimeZone of the current bars/chart
					if (NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo != null)
						chartTimeZone = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo;
					else
						chartTimeZone = TimeZoneInfo.Local; // Fallback
						
                    Print($"TimeZones Loaded: NY={nyTimeZone.Id} Chart={chartTimeZone.Id}");
					timeZonesLoaded = true;
				}
				catch (Exception ex)
				{
                    // Fallback to Local to prevent freeze
                    nyTimeZone = TimeZoneInfo.Local;
                    chartTimeZone = TimeZoneInfo.Local;
                    
					Print("Error loading TimeZones (Using Local): " + ex.Message);
                    Log("Error loading TimeZones (Using Local): " + ex.ToString());
					timeZonesLoaded = true; 
				}
			}

			// Continuous Lag Monitoring (Visuals only)
			// Ensure visual alert clears when lag dissipates, even if no trade is attempting
			CheckChartLag();
			
			// DIAGNOSTIC HEARTBEAT REMOVED
			// if (CurrentBar % 50 == 0) Print("HEARTBEAT: ...");

			// 0. Calculate Volume Delta for VWAP
			if (IsFirstTickOfBar) lastVol = 0;
			double deltaVol = Volume[0] - lastVol;
			lastVol = Volume[0];

			// CSV LOGGING INIT (Once per session)
			if (CurrentBar == BarsRequiredToTrade) // Use a safe bar index to init
			{
				InitCSV();
			}

			// 1. Session Logic: Identify/Create Levels (Delegated to SessionManager)
			if (sessionManager == null) sessionManager = new SessionManager(this);
            
            // v1.14.78: PERSISTENCE LOAD
            // If starting fresh (no levels), try to load from disk FIRST
            if (activeLevels.Count == 0 && CurrentBar == BarsRequiredToTrade && levelPersistence != null)
            {
                var loaded = levelPersistence.LoadLevels();
                if (loaded != null && loaded.Count > 0)
                {
                    activeLevels = loaded;
                    DumpActiveLevels("Loaded from Disk");
                    lastLevelCount = activeLevels.Count;
                }
            }
			
			// v1.14.64: Scan for historical levels from sessions that already ended (once at startup)
			if (CurrentBar == BarsRequiredToTrade)
			{
				sessionManager.ScanHistoricalLevels();
			}
			
			sessionManager.CheckSession("Asia", tsAsiaStart, tsAsiaEnd, Brushes.White, deltaVol);
			sessionManager.CheckSession("Europe", tsEuStart, tsEuEnd, Brushes.Yellow, deltaVol);
			sessionManager.CheckSession("USA", tsUsaStart, tsUsaEnd, Brushes.RoyalBlue, deltaVol);
			
			// 2. Manage Extension & Touching (Delegated to SessionManager)
			sessionManager.ManageLevels(deltaVol);
            
            // v1.14.78 PERSISTENCE SAVE
            // If levels changed (new level added), save to disk
            if (levelPersistence != null && activeLevels.Count != lastLevelCount)
            {
                // Only save if we have levels (don't overwrite with empty list unless intended)
                if (activeLevels.Count > 0)
                {
                    levelPersistence.SaveLevels(activeLevels);
                    lastLevelCount = activeLevels.Count;
                }
            }
			
			// 3. Global ETH VWAPs
			// 3. Global ETH VWAPs (Delegated)
            if (vwapCalc != null)
            {
			    vwapCalc.ManageGlobalVWAPs(deltaVol, Time[0], CurrentBar, High, Low, Close, Open, Volume, nyTimeZone, chartTimeZone);
                
                // ADHOC VWAP UPDATE (Delegate)
			    if (currentEntryState == EntryState.WaitingForConfirmation || currentEntryState == EntryState.workingOrder)
			    {
				    vwapCalc.UpdateAdhocVWAP(deltaVol, CurrentBar, High, Low, Close, Open, Volume);
			    }
            }
			
			// RESTORED: Safety Checks & UI (Previously in ManageGlobalVWAPs)
			DrawStatePanel();
			CheckSafetyNet();
			CheckHardStop();
			CheckSessionExit();
			CheckPendingEntryCleanup(); // v1.15.3: Cancel pending entries near TP1

			// v1.14.76: Apteros Risk Enforcement (Bar Update)
            if (riskManager != null && SelectedRiskModel == RiskModelType.Apteros)
            {
               if (!riskManager.CheckRiskState(SelectedRiskModel, Account.Get(AccountItem.CashValue, Currency.UsDollar), ApterosDailyLossPercent, ApterosMaxTrailingDrawdown))
               {
                   if (Position.MarketPosition != MarketPosition.Flat)
                       ClosePositionUnmanaged("Apteros Risk Limit Hit (OnBarUpdate)");
                   
                   return; // Stop processing entry logic
               }
            }

			// HISTORICAL LOAD OPTIMIZATION
			// Skip trading logic for old bars to speed up strategy loading
			// Levels are still calculated above, only entry/exit logic is skipped
			bool isRecentBar = (Time[0].Date >= DateTime.Today.AddDays(-3));
			if (State == State.Historical && !isRecentBar && !AllowBacktest)
			{
				return; // Levels calculated, skip trading logic for speed
			}
			
			// 4. Entry Logic (only for recent bars or Realtime)
			// v1.14.62: Protect ENTRY logic with 20-bar delay (moved from top)
			if (CurrentBar < 20) return;

			ManageEntryA_Plus();
			
			// Track MAE/MFE for active trades
			if (isTrackingTrade && Position.MarketPosition != MarketPosition.Flat)
			{
				double unrealizedPnL = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
				
				// Update MAE (worst point - negative values)
				if (unrealizedPnL < tradeMAE)
					tradeMAE = unrealizedPnL;
				
				// Update MFE (best point - positive values)
				if (unrealizedPnL > tradeMFE)
					tradeMFE = unrealizedPnL;
			}


			}
			catch (Exception ex)
			{
				// Force Print even if debug disabled to catch Critical Runtime Errors
				Print($"CRITICAL_ERROR in OnBarUpdate (Bar {CurrentBar}): {ex.GetType().Name} - {ex.Message}");
				if (EnableDebugLogs)
					Log($"CRITICAL STACK: {ex.StackTrace}");
			}
			}
			catch (Exception ex)
			{
				NinjaTrader.Code.Output.Process("OUTER CRITICAL_ERROR in OnBarUpdate: " + ex.ToString(), PrintTo.OutputTab1);
			}
		}


		
		// Diagnostic OnRender
		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			try
			{
				base.OnRender(chartControl, chartScale);
			}
			catch (Exception ex)
			{
				// Print to console only to avoid IO lock storms in render loop
				Print($"Render Error: {ex.Message}");
			}
		}

		// -------------------------------------------------------------------------
		// ENTRY LOGIC VARIABLES
		// -------------------------------------------------------------------------

		[XmlIgnore] public EntryState currentEntryState = EntryState.Idle;
		[XmlIgnore] public string setupLevelName = "";
		[XmlIgnore] public DateTime setupLevelTime = DateTime.MinValue; // NEW (v1.5.8): Track time of the level we are trading
	[XmlIgnore] public int currentLevelAttempts = 0; // v1.15.15: Current level entry attempts
		[XmlIgnore] public double setupAnchorPrice = 0;
		[XmlIgnore] public bool isShortSetup = false; // true = Short, false = Long
		[XmlIgnore] public bool visualConfirmationDone = false; // Control para pintar vela solo la primera vez
		// Rejection Loop Protection
		[XmlIgnore] public int lastRejectionBar = -1;
		// V_EXEC: Execution Variables
		[XmlIgnore] public Order entryOrder = null; // Consolidated Entry
		// REMOVED: entryOrder1, entryOrder2
		
		// Protection State
		[XmlIgnore] public int protectedTp1Qty = 0;
		[XmlIgnore] public int protectedTp2Qty = 0;
		private bool protectionOrdersCreated = false; // v1.11.14: Prevent duplicate creation
		[XmlIgnore] public int tradeOriginalQty = 0; // v1.11.23: Original trade quantity for panel display (doesn't change after TP1 fill)
		[XmlIgnore] public double tradeOriginalTp1Price = 0; // v1.11.24: Original TP1 price for panel display
		[XmlIgnore] public double tradeOriginalTp2Price = 0; // v1.11.24: Original TP2 price for panel display
		// v1.10.31: Trade VWAP - continues accumulating even when day changes
		// Separate from global VWAP to keep TP1 moving with original day's VWAP
		private SessionVWAP tradeVWAP = new SessionVWAP();
		private bool tradeVwapActive = false;

		// REFACTOR v1.7.3: Consolidated SL/TP tracking
		[XmlIgnore] public Order stopOrder = null; // Legacy fallback, kept to avoid compile errors if referenced elsewhere (e.g. Draw)
		private Order stopOrder1 = null; 
		private Order stopOrder2 = null; 
		[XmlIgnore] public Order tp1Order = null;
        [XmlIgnore] public Order tp2Order = null;
		private Order targetOrder = null; // Legacy tracker
		
		// Visual Tracking
		[XmlIgnore] public string triggerTag = "";
		[XmlIgnore] public int triggerBar = 0;
        [XmlIgnore] public int triggerLabelIndex = 0; // v1.14.80: For Recycling Labels

        // v1.15.40: Ladder Exit Model State
        [XmlIgnore] public List<Order> ladderOrders = new List<Order>();
		
		// -------------------------------------------------------------------------
		// GLOBAL ETH SESSION VWAP LOGIC
		// -------------------------------------------------------------------------

		
		private SessionVWAP ethHighVWAP_OBSOLETE; // Kept as placeholder if needed during transition, but logic moved to vwapCalc
		private SessionVWAP ethLowVWAP_OBSOLETE;
		
		#region Properties
		// Email & Screenshot Properties
		[NinjaScriptProperty]
		[Display(Name = "Enable Local Screenshots", Description = "Save screenshots to disk without sending email", GroupName = "8. Email Alerts", Order = 0)]
		public bool EnableLocalScreenshots { get; set; } = true;

		[NinjaScriptProperty]
		[Display(Name = "Enable Email Alerts", Description = "Send screenshot via email (Requires SMTP settings)", GroupName = "8. Email Alerts", Order = 1)]
		public bool EnableEmailAlerts { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Risk Model", Description = "Standard (Legacy) or Optimization (Fixed Contracts)", GroupName = "2. Risk Management", Order = 0)]
        public RiskModelType SelectedRiskModel { get; set; } = RiskModelType.Standard;

        [NinjaScriptProperty]
        [Display(Name = "Exit Strategy", Description = "Standard (VWAP+Zone) or Ladder (1R, 2R...)", GroupName = "2. Risk Management", Order = 1)]
        public ExitStrategyType ExitStrategy { get; set; } = ExitStrategyType.Standard;

		[NinjaScriptProperty]
		[Display(Name = "Email To", Description = "Destination email address", GroupName = "8. Email Alerts", Order = 2)]
		public string EmailTo { get; set; } = "user@example.com";

		[NinjaScriptProperty]
		[Display(Name = "From Address", Description = "Sender address (usually same as username)", GroupName = "8. Email Alerts", Order = 3)]
		public string EmailFrom { get; set; } = "user@gmail.com";

		[NinjaScriptProperty]
		[Display(Name = "SMTP Host", Description = "e.g. smtp.gmail.com", GroupName = "8. Email Alerts", Order = 4)]
		public string EmailHost { get; set; } = "smtp.gmail.com";

		[NinjaScriptProperty]
		[Display(Name = "SMTP Port", Description = "e.g. 587", GroupName = "8. Email Alerts", Order = 5)]
		public int EmailPort { get; set; } = 587;

		[NinjaScriptProperty]
		[Display(Name = "SMTP Username", Description = "Full email address", GroupName = "8. Email Alerts", Order = 6)]
		public string EmailUsername { get; set; } = "user@gmail.com";

		[NinjaScriptProperty]
		[Display(Name = "SMTP Password", Description = "App Password (not your login password)", GroupName = "8. Email Alerts", Order = 7)]
		public string EmailPassword { get; set; } = "password";
		
		private double ethHighPrice = double.MinValue;
		private double ethLowPrice = double.MaxValue;
		private DateTime lastEthResetDate = DateTime.MinValue; 

		// AD-HOC VWAP Variables (Fresh Start) - Public for EntryStateMachine access
		[XmlIgnore] public double adhocVolSum = 0;
		[XmlIgnore] public double adhocPvSum = 0;
		[XmlIgnore] public double adhocLastVol = 0; // To track delta volume inside a bar
		[XmlIgnore] public int adhocLastBar = -1;
		[XmlIgnore] public int adhocAnchorBar = -1; // Track anchor bar for retroactive update

		public void UpdateAdhocVWAP()
		{
			// v1.14.45: Delegate to VWAPCalculator Module
			if (vwapCalc != null)
			{
				vwapCalc.UpdateAdhocVWAP(0, CurrentBar, High, Low, Close, Open, Volume);
				
				// Sync local visual variables for drawing (Backwards Compatibility for now)
				visualAdhocPrevBarVal = vwapCalc.VisualAdhocPrevBarVal;
				visualAdhocLastVal = vwapCalc.VisualAdhocLastVal;
				visualAdhocLastBar = vwapCalc.AdhocAnchorBar; // This might need refinement, visual tracking logic
				
				// Important: Retrieve calculated values for local usage
				adhocVolSum = vwapCalc.AdhocVolSum;
				adhocPvSum = vwapCalc.AdhocPvSum;
			}
		} 

		public void ResetAdhocVWAP(double vol, double price, int bar)
		{
			if (vwapCalc != null)
			{
				vwapCalc.ResetAdhoc(vol, price, bar);
				// Sync local variables to match calculator state immediately
				adhocVolSum = vwapCalc.AdhocVolSum;
				adhocPvSum = vwapCalc.AdhocPvSum;
				adhocLastVol = vol;
				adhocLastBar = bar;
				adhocAnchorBar = bar;
				visualAdhocPrevBarVal = price;
				visualAdhocLastVal = price;
			}
		}
		
		private int highAnchorBar = 0;
		private int lowAnchorBar = 0;


		
		private void CheckHardStop()
		{
			if (Position.MarketPosition == MarketPosition.Flat) return;
			// Prevent infinite loop if position close takes time
			if (failsafeTriggered) return;
			
			// Validate Anchor
			if (setupAnchorPrice <= 0 || setupAnchorPrice == double.MaxValue || setupAnchorPrice == double.MinValue) return;

			// BUFFER: Only force exit if price breaches Anchor SIGNIFICANTLY (e.g., 4 ticks).
			// This gives the native Stop Order priority to execute at the correct price.
			// Failsafe is only for when the Stop Order fails.
			double checkBuffer = 4 * TickSize;

			if (Position.MarketPosition == MarketPosition.Short)
			{
				// If Price is ABOVE Anchor + Buffer
				if (High[0] >= (setupAnchorPrice + checkBuffer))
				{
						Log(Time[0] + " FAILSAFE: Price (High=" + High[0] + ") violated Anchor (" + setupAnchorPrice + "). Forcing ExitShort.");
						failsafeTriggered = true; // Lock immediately
						
						// v1.14.94: RACE CONDITION FIX - Cancel SL/TP before Market Exit
						if (protectionManager != null) protectionManager.CancelAllProtectionOrders();
						
						ClosePositionUnmanaged("Anchor Violation");
						// Reset handled in OnExecutionUpdate
						return;
				}
			}
			else if (Position.MarketPosition == MarketPosition.Long)
			{
				// If Price is BELOW Anchor - Buffer
				if (Low[0] <= (setupAnchorPrice - checkBuffer))
				{
						Log(Time[0] + " FAILSAFE: Price (Low=" + Low[0] + ") violated Anchor (" + setupAnchorPrice + "). Forcing ExitLong.");
						failsafeTriggered = true; // Lock immediately
						
						// v1.14.94: RACE CONDITION FIX - Cancel SL/TP before Market Exit
						if (protectionManager != null) protectionManager.CancelAllProtectionOrders();
						
						ClosePositionUnmanaged("Anchor Violation");
						// Reset handled in OnExecutionUpdate
						return;
				}
			}
		}


		// -------------------------------------------------------------------------
		// SESSION EXIT MANANAGEMENT
		// -------------------------------------------------------------------------
		private void CheckSessionExit()
		{
			// Only valid if timezones are loaded
			if (nyTimeZone == null || chartTimeZone == null) return;
			
			// 1. Calculate Cutoff Time (USA End - 30 seconds)
			// We use the CACHED TimeSpan for performance
			if (tsUsaEnd == TimeSpan.Zero) return; // Not initialized?

			// Convert Chart Time to NY Time
			DateTime chartTime = Time[0];
			DateTime nyTime = TimeZoneInfo.ConvertTime(chartTime, chartTimeZone, nyTimeZone);
			TimeSpan nyTimeOfDay = nyTime.TimeOfDay;
			
			// Safety Margin (30 seconds before close)
			TimeSpan exitBuffer = TimeSpan.FromSeconds(30);
			TimeSpan cutoffTime = tsUsaEnd.Subtract(exitBuffer);
			
			// 2. Trigger Window: Are we in the LAST 30 seconds of the session OR in the cooldown/gap period (5 mins after)?
			// Broadened window to catch exact 16:00:00 bars and any immediate post-close processing.
			TimeSpan gapBuffer = TimeSpan.FromMinutes(5);
			
			// DYNAMIC SESSION AWARENESS (Holidays/Early Closes)
			// Instead of fixed "16:00" string, we ask NinjaTrader for the TRUE session end of this bar.
			// Use SessionIterator properly
			if (sessionIterator == null) sessionIterator = new SessionIterator(Bars);
			sessionIterator.GetNextSession(Time[0], true);
			DateTime actualSessionEnd = sessionIterator.ActualSessionEnd;
			
			// Determine if this is a "Friday-like" closing event
			// 1. Is it actually Friday?
			// 2. OR is it an Early Close (Holiday)? e.g. 13:00 close on a Tuesday.
			
			// Convert Actual Close to NY Time for consistency with "15:30" logic
			DateTime nyClose = TimeZoneInfo.ConvertTime(actualSessionEnd, chartTimeZone, nyTimeZone);
			bool isFriday = nyTime.DayOfWeek == DayOfWeek.Friday;
			// Early Close Definition: Market closes before 15:30 NY Time (Standard is 16:00 or 17:00)
			bool isEarlyClose = nyClose.TimeOfDay < TimeSpan.FromHours(15.5);
			
			// v1.14.78: DEBUG HOLIDAY LOGIC
			if (IsFirstTickOfBar && Time[0].Hour == 10 && Time[0].Minute == 29)
			{
				Log(string.Format("DEBUG DATE: Time={0} ActualSessionEnd={1} (NY={2}) IsEarlyClose={3} IsFriday={4}", 
					Time[0], actualSessionEnd, nyClose, isEarlyClose, isFriday));
			}
			
			// Trigger logic - DAILY CHECK (Previously only Friday/Holiday)
			// v1.15.1: User Request - Cancel Pending Orders DAILY at Session Close (17:00 NY)
			// "esa orden si no fue tomada se debe cancelar antes del cierre de amrica"
			
			DateTime dynamicCutoff = actualSessionEnd.Subtract(exitBuffer);
			
			// Check Window: From Cutoff (End-30s) up to End+5min (Gap/Cleanup)
			if (Time[0] >= dynamicCutoff && Time[0] <= actualSessionEnd.Add(gapBuffer))
			{
				// ---------------------------------------------------------------------
				// 1. POSITION EXIT (FRIDAY / HOLIDAY ONLY) or explicit ExitOnSessionClose
				// ---------------------------------------------------------------------
				// Keep strict Position closing for Weekends/Holidays to avoid gap risk
				if (EnableHolidayProtection && (isFriday || isEarlyClose))
				{
					if (Position.MarketPosition != MarketPosition.Flat)
					{
						// Only log once per bar to avoid spam
						if (IsFirstTickOfBar)
							Log(Time[0] + " SESSION CLOSE PROTECT: Market closing/holiday. Forcing Exit. (Reason: " + (isEarlyClose ? "Holiday" : (isFriday ? "Friday" : "DailyClose")) + ")");
						
						// v1.14.94: RACE CONDITION FIX - Cancel SL/TP before Market Exit (Consistency)
						if (protectionManager != null) protectionManager.CancelAllProtectionOrders();
						
						ClosePositionUnmanaged("Exit on Session Close");
					}
				}

				// ---------------------------------------------------------------------
				// 2. ORDER CLEANUP (DAILY - MANDATORY)
				// ---------------------------------------------------------------------
				// Always cancel pending orders at session close to prevent "Orphan Limit" orders overnight.
				if (currentEntryState != EntryState.Idle || true) // Force check always
				{
					if (IsFirstTickOfBar)
						Log(Time[0] + " DAILY SESSION CLEANUP: Scanning for Pending Orders...");
						
					// ALWAYS Cancel Pending ENTRIES (prevent fills during gap)
					if (entryOrder != null && (entryOrder.OrderState == OrderState.Working || entryOrder.OrderState == OrderState.Accepted))
					{
						Log(Time[0] + " DAILY CLEANUP: Cancelling Pending Entry: " + entryOrder.Name);
						CancelOrder(entryOrder);
					}
					
					// v1.15.2: CRITICAL FIX - Only Cancel Protection/Reset State if FLATTENED
					// If we are holding a position (Overnight), we MUST KEEP SL/TP active.
					if (Position.MarketPosition == MarketPosition.Flat)
					{
						if (stopOrder != null && (stopOrder.OrderState == OrderState.Working || stopOrder.OrderState == OrderState.Accepted)) CancelOrder(stopOrder);
						if (stopOrder1 != null && stopOrder1.OrderState == OrderState.Working) CancelOrder(stopOrder1);
						if (stopOrder2 != null && stopOrder2.OrderState == OrderState.Working) CancelOrder(stopOrder2);
						if (tp1Order != null && tp1Order.OrderState == OrderState.Working) CancelOrder(tp1Order);
						if (tp2Order != null && tp2Order.OrderState == OrderState.Working) CancelOrder(tp2Order);

						if (protectionManager != null) protectionManager.CancelAllProtectionOrders();
						
						// v1.15.1: ZOMBIE SWEEP - Iterate ALL Strategy Orders to catch lost references
						lock (Orders)
						{
							foreach (Order o in Orders)
							{
								if (o != null && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted))
								{
									// Since we are FLAT, ANY working order is a zombie/orphan. Kill it.
									Log(Time[0] + " ZOMBIE SWEEP: Force Cancelling orphaned order: " + o.Name);
									CancelOrder(o);
								}
							}
						}
						
						currentEntryState = EntryState.Idle; // Force Idle only if Flat
						setupLevelName = "";
					}
					else
					{
						// Position Active -> Log that we are skipping cleanup to protect position
						if (IsFirstTickOfBar)
							Log(Time[0] + " DAILY CLEANUP SKIPPED (Active Position): Preservation Mode.");
					}
				}
			}
		}





		
		// Orphan State Tracking
		private bool orphanHandled = false;

		// -------------------------------------------------------------------------
		// v1.15.3: PENDING ENTRY CLEANUP - Cancel partial fills near TP1
		// -------------------------------------------------------------------------
		private void CheckPendingEntryCleanup()
		{
			// Only relevant if we have an active position AND a pending entry order
			if (Position.MarketPosition == MarketPosition.Flat) return;
			if (entryOrder == null) return;
			if (entryOrder.OrderState != OrderState.Working && entryOrder.OrderState != OrderState.Accepted) return;
			
			// Get current TP1 price (from working order or cached)
			double tp1Price = 0;
			if (tp1Order != null && (tp1Order.OrderState == OrderState.Working || tp1Order.OrderState == OrderState.Accepted))
			{
				tp1Price = tp1Order.LimitPrice;
			}
			
			if (tp1Price <= 0) return; // No valid TP1 to reference
			
			// Calculate 4-tick buffer
			double buffer = 4 * TickSize;
			bool shouldCancel = false;
			
			if (Position.MarketPosition == MarketPosition.Short)
			{
				// Short: TP1 is below entry. Cancel if price is within 4 ticks of TP1 (price descending)
				if (Low[0] <= tp1Price + buffer)
				{
					shouldCancel = true;
				}
			}
			else if (Position.MarketPosition == MarketPosition.Long)
			{
				// Long: TP1 is above entry. Cancel if price is within 4 ticks of TP1 (price ascending)
				if (High[0] >= tp1Price - buffer)
				{
					shouldCancel = true;
				}
			}
			
			if (shouldCancel)
			{
				Log(Time[0] + " ENTRY CLEANUP: Price near TP1. Cancelling pending entry: " + entryOrder.Name + " (Remaining Qty=" + (entryOrder.Quantity - entryOrder.Filled) + ")");
				try { CancelOrder(entryOrder); } catch {}
			}
		}

		private void CheckSafetyNet()
		{
			// 0. ACCOUNT SYNC CHECK (Realtime Only)
			if (State == State.Realtime && Account != null && Position.MarketPosition == MarketPosition.Flat)
			{
				// Skip orphan check for 2 seconds after position close to avoid false positives
				// (Account.Positions can have sync delay after SL/TP fill)
				if ((DateTime.Now - lastPositionCloseTime).TotalSeconds < 2.0)
				{
					return; // Too soon after position close, skip check
				}
				
				bool foundOrphan = false; 
				
				try 
				{
					foreach (Position accPos in Account.Positions)
					{
						// Filter for this Instrument (String compare safer)
						if (accPos.Instrument.FullName == Instrument.FullName && accPos.MarketPosition != MarketPosition.Flat)
						{
							foundOrphan = true;
							
							// ORPHAN DETECTED
							double avgPrice = accPos.AveragePrice;
							double safetyMargin = 20 * TickSize;
							
							// Safety Check
							bool unsafeOrphan = false;
							if (accPos.MarketPosition == MarketPosition.Long)
							{
								if (Low[0] <= avgPrice - safetyMargin) unsafeOrphan = true;
							}
							else if (accPos.MarketPosition == MarketPosition.Short)
							{
								if (High[0] >= avgPrice + safetyMargin) unsafeOrphan = true;
							}

							// Don't flatten overnight positions - user wants them open
							// Only alert, don't close
							if (unsafeOrphan)
							{
								// Log warning but DON'T close - this is intentional overnight
								if (!orphanHandled)
								{
									Log(Time[0] + " WARNING: Orphan Position (gap detected) @ " + avgPrice + ". Overnight mode - NOT closing.");
									orphanHandled = true;
								}
							}
							else if (!orphanHandled)
							{
								// Safe Orphan & Not Handled -> ALERT ONLY (Managed Mode Risk)
								// Visual Confirmation
								Draw.Text(this, "OrphanTxt_" + CurrentBar, "SAFE ORPHAN DETECTED\nMANAGE MANUALLY", 0, avgPrice, Brushes.LimeGreen);
								
								// Alert
								PlaySound(NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav");
								Log(Time[0] + " WARNING: Safe Orphan Position Detected @ " + avgPrice + ". Unable to auto-manage in Managed Mode. PLEASE SET SL/TP MANUALLY.");
								
								orphanHandled = true;
							}
						}
					}
				}
				catch (Exception ex) { Log("Account Sync Check Failed: " + ex.Message); }
				
				// Reset Handled flag if no orphan found (position closed)
				if (!foundOrphan) orphanHandled = false;
			}
			else
			{
				orphanHandled = false; // Reset if Strategy has position (managed)
			}

			// 1. Zombie Position: We have a position, but State thinks we are Idle/Working.
			if (Position.MarketPosition != MarketPosition.Flat && currentEntryState != EntryState.PositionActive)
			{
				Log(Time[0] + " CRITICAL: Safety Net Triggered! Position exists but State was " + currentEntryState);
				Log($"DEBUG_SAFETYNET: Position.Qty={Position.Quantity} Position.MarketPosition={Position.MarketPosition} tradeOriginalQty={tradeOriginalQty}");
				
				// v1.14.69: DIAGNOSTIC LOG - Capture full state before EnsureProtection to debug phantom position creation
				string tradeDir = isShortSetup ? "Short" : "Long";
				double msSinceClose = (DateTime.Now - lastPositionCloseTime).TotalMilliseconds;
				Log($"SAFETYNET_PRE: Position.MarketPosition={Position.MarketPosition} Position.Qty={Position.Quantity} " +
					$"currentEntryState={currentEntryState} tradeDirection={tradeDir} " +
					$"lastPositionCloseTime={msSinceClose:F0}ms ago");
				
				// --- SMART ADOPTION LOGIC (Strategy Position) ---
				// ... (Existing Logic) ...
				
				if (setupAnchorPrice == 0 || setupAnchorPrice == double.MaxValue || setupAnchorPrice == double.MinValue)
				{
					// We have AMNESIA. Let's infer a safety anchor.
					double avgPrice = Position.AveragePrice;
					double safetyMargin = 20 * TickSize; // Emergency allow 20 ticks from entry
					
					if (Position.MarketPosition == MarketPosition.Short)
					{
						// Short: Anchor should be ABOVE entry.
						double inferredAnchor = avgPrice + safetyMargin;
						
						// Validation: Are we ALREADY dead?
						if (High[0] >= inferredAnchor)
						{
							Log(Time[0] + " ZOMBIE CHECK: Price (" + High[0] + ") is above Inferred Anchor (" + inferredAnchor + "). Closing Unsafe Position.");
							ClosePositionUnmanaged("Zombie Check");
							return; // Don't adopt. Kill.
						}
						
						// If safe, adopt.
						setupAnchorPrice = inferredAnchor;
						Log(Time[0] + " ZOMBIE ADOPTED (Short). Inferred Anchor: " + setupAnchorPrice);
					}
					else if (Position.MarketPosition == MarketPosition.Long)
					{
						// Long: Anchor should be BELOW entry.
						double inferredAnchor = avgPrice - safetyMargin;
						
						// Validation: Are we ALREADY dead?
						if (Low[0] <= inferredAnchor)
						{
							Log(Time[0] + " ZOMBIE CHECK: Price (" + Low[0] + ") is below Inferred Anchor (" + inferredAnchor + "). Closing Unsafe Position.");
							ClosePositionUnmanaged("Zombie Check");
							return; // Don't adopt. Kill.
						}
						
						// If safe, adopt.
						setupAnchorPrice = inferredAnchor;
						Log(Time[0] + " ZOMBIE ADOPTED (Long). Inferred Anchor: " + setupAnchorPrice);
					}
				}

				// If we reached here, we are adopting (or already had an anchor).
				currentEntryState = EntryState.PositionActive;
				
				// Force Place Stops if missing
				// Use "Emergency" signal tag for safety net adoption
				// v1.15.26: Pass separate TP1 and TP2 prices
				if (Position.MarketPosition == MarketPosition.Short)
				{
					// EnsureProtection Delegate
					protectionManager.EnsureProtection("Short", "Emergency_Short_1", Position.Quantity, currentVwapNumber, true, setupLevelName, setupLevelTime, setupAnchorPrice, validatedTp1Price, validatedTp2Price);
				}
				else if (Position.MarketPosition == MarketPosition.Long)
				{
					// EnsureProtection Delegate
					protectionManager.EnsureProtection("Long", "Emergency_Long_1", Position.Quantity, currentVwapNumber, false, setupLevelName, setupLevelTime, setupAnchorPrice, validatedTp1Price, validatedTp2Price);
				}
			}
			
			// 2. Ghost State: State thinks we are InPosition, but we are Flat.
			if (Position.MarketPosition == MarketPosition.Flat && currentEntryState == EntryState.PositionActive)
			{
				Log(Time[0] + " SYNC: State is InPosition but MarketPosition is Flat. Resetting to Idle.");
				currentEntryState = EntryState.Idle;
				setupLevelName = "";
			
			// RESET PROTECTION COUNTERS - Fix bucket allocation in SYNC path
			protectedTp1Qty = 0;
			protectedTp2Qty = 0;
			protectionOrdersCreated = false; // v1.11.14: Reset flag for next trade
			isProtectionProcessing = false; // v1.13.1: Reset lock
			tradeOriginalQty = 0; // v1.11.23: Reset original trade qty
			tradeOriginalTp1Price = 0; // v1.11.24: Reset original TP prices
			tradeOriginalTp2Price = 0;
			tradeVwapActive = false; // v1.10.31: Reset Trade VWAP
			IsTradeVwapExtended = false; // v1.14.74: Reset extension flag
				
				// Cancel orphan orders before nullifying references
				// This handles cases where SL was manually moved and executed
				// Also cancel stopOrder (Single-SL architecture)
				// More robust cancellation - check for Working, Accepted, or any active state
				if (stopOrder != null)
				{
					Log(Time[0] + " DEBUG ORPHAN: stopOrder exists. State=" + stopOrder.OrderState + " Name=" + stopOrder.Name);
					if (stopOrder.OrderState == OrderState.Working || stopOrder.OrderState == OrderState.Accepted)
					{
						Log(Time[0] + " CANCELLING ORPHAN SL: " + stopOrder.Name);
						CancelOrder(stopOrder);
					}
				}
				if (stopOrder1 != null && (stopOrder1.OrderState == OrderState.Working || stopOrder1.OrderState == OrderState.Accepted)) CancelOrder(stopOrder1);
				if (stopOrder2 != null && (stopOrder2.OrderState == OrderState.Working || stopOrder2.OrderState == OrderState.Accepted)) CancelOrder(stopOrder2);
				if (tp1Order != null && (tp1Order.OrderState == OrderState.Working || tp1Order.OrderState == OrderState.Accepted)) CancelOrder(tp1Order);
				if (tp2Order != null && (tp2Order.OrderState == OrderState.Working || tp2Order.OrderState == OrderState.Accepted)) CancelOrder(tp2Order);
				if (entryOrder != null && (entryOrder.OrderState == OrderState.Working || entryOrder.OrderState == OrderState.Accepted)) CancelOrder(entryOrder);
				
				// CRITICAL FIX: Ensure ALL order references are cleared to prevent "Exits already exist" blocking future trades.
				entryOrder = null;
				// entryOrder1 = null; // Removed
				// entryOrder2 = null; // Removed
				// entryOrder = null; // Removed
				targetOrder = null; // Legacy
				stopOrder = null; 
				stopOrder1 = null;
				stopOrder2 = null;
				tp1Order = null;
				tp2Order = null;

                // v1.15.42: Cleanup Adhoc Lines on Reset
                ClearAdhocVisuals();
			}
		}
		
		// v1.15.42: Cleanup Method for Adhoc Lines (Visual Leak Fix)
		public void ClearAdhocVisuals()
		{
			// Iterate from anchor to current and remove all segments
			// We add a buffer (+5) to catch any edge cases
			if (adhocAnchorBar > 0)
			{
				for (int i = adhocAnchorBar; i <= CurrentBar + 5; i++)
				{
					RemoveDrawObject("AdhocLine_" + i);
				}
			}
			// Reset tracking
			visualAdhocLastBar = -1;
		}
		
		private void DrawStatePanel()
		{
			if (helpers != null) helpers.DrawStatePanel();
		}
		
		// -------------------------------------------------------------------------
		// CONTROL BUTTONS (Delegated to StrategyHelpers)
		// -------------------------------------------------------------------------
		private void InitializeControlButtons()
		{
			if (helpers != null) helpers.InitializeControlButtons();
		}
		
		private void ClosePositionManual()
		{
			if (helpers != null) helpers.ClosePositionManual();
		}
		
		private void CleanupControlButtons()
		{
			if (helpers != null) helpers.CleanupControlButtons();
		}
		
		// -------------------------------------------------------------------------
		// DYNAMIC POSITION SIZING + ATR Risk Scaling
		// -------------------------------------------------------------------------
		public int CalculateDynamicQuantity(double entryPrice, double stopPrice)
		{
			// Si dynamic sizing está OFF, usar Quantity fijo
			if (!UseDynamicSizing) return Quantity;
			
			// Calcular ticks de riesgo
			double riskInPrice = Math.Abs(entryPrice - stopPrice);
			double riskInTicks = riskInPrice / TickSize;
			
			// Valor de 1 tick en USD
			double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
			
			// Validación: evitar división por cero
			if (riskInTicks <= 0 || tickValue <= 0)
			{
				Log(string.Format("{0} DYNAMIC SIZING ERROR: Invalid risk calculation. RiskTicks={1:F2} TickValue=${2:F4} - Using MinQuantity",
					Time[0], riskInTicks, tickValue));
				return MinQuantity;
			}

			// v1.15.23: CRITICAL FIX - Enforce minimum SL distance to prevent absurd quantities
			// When SL is too close (e.g., 1 tick after VWAP retry), quantity becomes dangerously high
			// Use ATR-based minimum to adapt to each instrument's volatility
			if (atr != null && atr[0] > 0)
			{
				const double MIN_ATR_PERCENTAGE = 0.30; // 30% of ATR minimum
				double minRiskInPrice = atr[0] * MIN_ATR_PERCENTAGE;
				double minRiskInTicks = minRiskInPrice / TickSize;

				if (riskInTicks < minRiskInTicks)
				{
					double riskInUSD = riskInTicks * tickValue;
					double minRiskInUSD = minRiskInTicks * tickValue;
					Log(string.Format("{0} DYNAMIC SIZING WARNING: SL too close ({1:F2} ticks < {2:F2} min [{3}% ATR]). Risk=${4:F2} < ${5:F2} min. Entry={6:F2} SL={7:F2} - Using MinQuantity",
						Time[0], riskInTicks, minRiskInTicks, (MIN_ATR_PERCENTAGE * 100), riskInUSD, minRiskInUSD, entryPrice, stopPrice));

					// v1.15.25: Display warning in status panel
					lastFilterReason = "⚠️ SL Muy Cercano - MinQty";
					lastFilterTime = DateTime.Now;

					return MinQuantity;
				}
			}

			// v1.14.76: Risk Model Selection
			double effectiveRisk = RiskPerTradeUSD; // Default

            // v1.15.40: Ladder Exit Model
            if (ExitStrategy == ExitStrategyType.Ladder)
            {
                // Ladder logic doesn't change RISK calculation (Entry - SL), 
                // but it changes how TPs are placed (1R, 2R, etc).
                // Risk calculation remains standard.
            }

			if (SelectedRiskModel == RiskModelType.Standard)
			{
				// --- STANDARD MODEL (Legacy + ATR) ---

				// v1.15.24: Dynamic risk based on current account value percentage
				// Calculate risk as percentage of current capital instead of fixed amount
				double currentCapital = Account.Get(AccountItem.CashValue, Currency.UsDollar);
				double riskPercentageDecimal = RiskPercentage / 100.0; // Convert 0.06% to 0.0006
				effectiveRisk = currentCapital * riskPercentageDecimal;

				// Ensure minimum risk of $5
				if (effectiveRisk < 5.0) effectiveRisk = 5.0;

				// DEBUG: Log initial state
                if (EnableDebugLogs)
				    Log(string.Format("RISK_DEBUG_PRE: Capital=${0:F2} | RiskPct={1}% | CalcRisk=${2:F2} | UseATR={3} | ATRFactor={4}",
					    currentCapital, RiskPercentage, effectiveRisk, UseATRScaling, ATRRiskScaleFactor));
				
				if (UseATRScaling && atr != null && atr[0] > 0)
				{
					// ATR-scaled risk: riesgo proporcional al ATR
					double atrInUSD = atr[0] * (Instrument.MasterInstrument.PointValue);
					double scaledRisk = atrInUSD * ATRRiskScaleFactor;
					
					if (EnableDebugLogs) 
                        Log(string.Format("RISK_DEBUG_ATR: ATR=${0:F2} | ScaledRisk=${1:F2}", atrInUSD, scaledRisk));
					
					// Usar el MENOR entre el riesgo máximo configurado y el escalado por ATR
					effectiveRisk = Math.Min(RiskPerTradeUSD, scaledRisk);
					
					// Nunca menos de $5 de riesgo
					if (effectiveRisk < 5.0) effectiveRisk = 5.0;
				}
				
				// Write to shared file for multi-instrument sync
				WriteSharedRisk(effectiveRisk);
				
				// Read GLOBAL MAX risk from all instruments (for multi-instrument scenarios)
				double fileRisk = ReadMaxSharedRisk();
				
				if (EnableDebugLogs)
				    Log(string.Format("RISK_DEBUG_POST: Written=${0:F2} | ReadFromFile=${1:F2} | Using=${2:F2}", 
					    effectiveRisk, fileRisk, (fileRisk > 0 ? fileRisk : effectiveRisk)));
				
				effectiveRisk = fileRisk;
			}
			else if (SelectedRiskModel == RiskModelType.Apteros)
			{
				// --- APTEROS MODEL (RiskManager) ---
				// Uses StartOfDayBalance and Opportunity division
				if (riskManager != null)
				{
					// Apteros logic determines the MAX risk per trade based on account size and remaining opportunities
					effectiveRisk = riskManager.GetEffectiveRiskPerTrade(
						SelectedRiskModel, 
						RiskPerTradeUSD, 
						Account.Get(AccountItem.CashValue, Currency.UsDollar), // Pass Current Account Value (or StartOfDay internally)
						ApterosDailyLossPercent, 
						ApterosDailyOpportunities,
						RiskCalculationBasis,
						ApterosMaxTrailingDrawdown,
						ApterosAllocationDays
					);
					Log(string.Format("APTEROS RISK: Risk=${0:F2} (Based on {1}% Limit / {2} Opportunities)", 
						effectiveRisk, ApterosDailyLossPercent, ApterosDailyOpportunities));
				}
			}
			

			
			// Formula: Quantity = EffectiveRisk / (Ticks * Value)
			double calculatedQty = effectiveRisk / (riskInTicks * tickValue);
			
			// Redondear a entero
			int quantity = (int)Math.Round(calculatedQty);
			
			// DEBUG LOG for Quantity Calculation
            if (EnableDebugLogs)
			    Log(string.Format("QTY_DEBUG: Risk=${0:F2} | SL_Ticks={1:F2} | TickVal=${2:F2} | CalcQty={3:F2} | MaxQty={4} | MinQty={5}", 
				    effectiveRisk, riskInTicks, tickValue, calculatedQty, MaxQuantity, MinQuantity));
			
			// Aplicar límites
			if (quantity < MinQuantity) quantity = MinQuantity;
			if (quantity > MaxQuantity) quantity = MaxQuantity;
			
			return quantity;
		}

		// -------------------------------------------------------------------------
		// ENTRY A+ MANAGEMENT
		// -------------------------------------------------------------------------
		private void ManageEntryA_Plus()
		{
			// Delegate mode guards to EntryStateMachine
			if (!entryMachine.CheckTradingModeGuards())
				return;
			
			// v1.15.15: REMOVED early return for VWAP Mitigation Retry
			// Previously this blocked ALL other entry logic while waiting for VWAP retry
			// Now we allow level scanning to run in parallel with VWAP retry monitoring
			// This allows the strategy to detect and switch to other levels while waiting

			
			// 1. TRIGGER DETECTION (Transition from Idle -> Waiting OR Switch Setup)
			// Allow scanning for triggers if Idle OR Waiting (to switch setups).
            // v1.14.80: Also allow scanning if WaitingForVwapMitigation (Virtual SL)
            // This ensures we catch OTHER opportunities while waiting for a specific level to break anchor
            // v1.15.15: Now actually works since we removed the early return above
			bool canScan = (currentEntryState == EntryState.Idle || 
                            currentEntryState == EntryState.WaitingForConfirmation ||
                            currentEntryState == EntryState.WaitingForVwapMitigation);
			
			// Always Update ADHOC VWAP if we are in a setup based on it
			// Wait... we need to accumulate ONLY after trigger? Or always?
			// User wants "Ends when touched". So we accumulate FROM Trigger.
			if (currentEntryState == EntryState.WaitingForConfirmation || currentEntryState == EntryState.workingOrder)
			{
				UpdateAdhocVWAP();
			
			// PHASE 3 - RE-ANCHORING (Delegated to EntryStateMachine)
			entryMachine.UpdateAnchorIfNeeded();
			
			// PHASE 4 - INVALIDATION (Delegated to EntryStateMachine)
			entryMachine.HandleInternalInvalidation();
				
				// VISUAL DEBUG: Draw 1px White Line
				bool isShort = (isShortSetup); 
				double v = GetSetupVWAP(isShort);
				
				// Redundancy Check: Is this Anchor the Global High or Low?
				// If so, the Global Plot (Values[0] or [1]) is already drawing this. We don't need a double line.
				bool isGlobal = false;
				if (isShort && Math.Abs(setupAnchorPrice - ethHighPrice) < TickSize) isGlobal = true;
				if (!isShort && Math.Abs(setupAnchorPrice - ethLowPrice) < TickSize) isGlobal = true;
				
				if (v > 0 && !isGlobal)
				{
					// Update Visual State logic
					if (visualAdhocLastBar != CurrentBar && visualAdhocLastBar != -1)
					{
						// New Bar Detected. Store the FINAL value of previous bar as start point.
						visualAdhocPrevBarVal = visualAdhocLastVal;
					}
					
					// Draw Line from PrevBarVal (Start of this bar logic) to CurrentVal (v)
					// Only draw if we have a valid previous point (not just started)
					if (visualAdhocLastBar != -1 && visualAdhocPrevBarVal > TickSize && v > TickSize)
					{
						string lineTag = "AdhocLine_" + CurrentBar;
                        
                        // Sanity Check: Prevent drawing if values are absurdly high (Infinity Lines)
                        if (visualAdhocPrevBarVal < 1000000 && v < 1000000)
						    Draw.Line(this, lineTag, false, 1, visualAdhocPrevBarVal, 0, v, Brushes.White, DashStyleHelper.Solid, 1);
					}
					
					// REMOVED TEXT LABEL AS REQUESTED
					// string label = "  " + setupLevelName; 
					// Draw.Text(this, "AdhocCurrentLabel", label, 0, v, Brushes.White);

					// Update Tracking
					visualAdhocLastVal = v;
					visualAdhocLastBar = CurrentBar;
				}
			}
			
			// Trigger Scanning (Delegated to EntryStateMachine)
			if (canScan)
			{
				entryMachine.ScanForTriggers();
			}
			
			// v1.14.54: Handle VWAP Retry (waiting for price to break extreme after SL/BE)
			entryMachine.HandleVwapMitigationWait();
			
			// Handle Confirmation Logic
			entryMachine.HandleConfirmation();
			
			// ... (Visuals Update Skipped for brevity, unchanged) ...
			if (currentEntryState == EntryState.WaitingForConfirmation && CurrentBar == triggerBar)
			{
				// VISUALS
				if (isShortSetup)
				{
					DrawTriggerLabel(triggerTag, true, 0, High[0]);
				}
				else
				{
					DrawTriggerLabel(triggerTag, false, 0, Low[0]);
				}

						// DYNAMIC ANCHOR UPDATE (Wait Phase)
						// Keep anchor at extremum while waiting for confirmation
						if (isShortSetup && High[0] > setupAnchorPrice) setupAnchorPrice = High[0];
						if (!isShortSetup && Low[0] < setupAnchorPrice) setupAnchorPrice = Low[0];
			}
			
			// 2. CONFIRMATION LOGIC (Delegated to EntryStateMachine)
			entryMachine.HandleConfirmation();

			// 3. ORDER MANAGEMENT & SYNC (Working -> InPosition) (Delegated to EntryStateMachine)
			entryMachine.HandleWorkingOrder();

			
			// 4. IN POSITION MANAGEMENT
			ManagePositionExit();
		} // End ManageEntryA_Plus

		// v1.14.40: EnsureProtection and SubmitProtectionOrders logic moved to OrderProtectionManager.cs
	/* 
	private void EnsureProtection(string direction, string entrySignalName, int filledQty)
	{
		// FIX v1.14.1 (Partial Fills): Removed protectionOrdersCreated check to allow multiple fills to update qty
		if (isProtectionProcessing)
		{
			Log(Time[0] + " EnsureProtection SKIPPED: Loop detected (isProtectionProcessing=true)");
			return;
		}
		
		// v1.13.10: DIAGNOSTIC LOGS for 100 contract bug investigation
		Log($"DEBUG_PROTECTION: EnsureProtection CALLED - Direction={direction} FilledQty={filledQty} Position.Qty={Position.Quantity} Position.MarketPosition={Position.MarketPosition} SecsSinceClose={(DateTime.Now - lastPositionCloseTime).TotalSeconds:F1}");
		
		isProtectionProcessing = true; // LOCK
		
		// v1.10.31: Initialize Trade VWAP on first fill
		// Copy accumulators from global VWAP so it continues accumulating
		if (!tradeVwapActive)
		{
			if (isShortSetup)
			{
                // Delegate to Module
                if (vwapCalc != null) vwapCalc.InitTradeVWAP(true);
			}
			else
			{
				if (vwapCalc != null) vwapCalc.InitTradeVWAP(false);
			}
			tradeVwapActive = true;
			Log(Time[0] + " TRADE VWAP: Initialized @ " + tradeVWAP.CurrentValue);
		}
		
		// DYNAMIC BUCKET ALLOCATION (v1.7.17)
		// We decide now how many of this 'filledQty' go to TP1 vs TP2.
		
		// v1.8.1 FIX: Use TOTAL position quantity, not partial fill quantity
		// This ensures correct 50/50 distribution even with partial fills
		int totalPositionQty = Math.Abs(Position.Quantity);
		int totalTp1Target = (totalPositionQty + 1) / 2;
		
		// How many does TP1 still need?
		int neededTp1 = totalTp1Target - protectedTp1Qty;
		if (neededTp1 < 0) neededTp1 = 0;
		
		// Allocate to TP1
		int forTp1 = Math.Min(neededTp1, filledQty);
		
		// Allocate remainder to TP2
		int forTp2 = filledQty - forTp1;
		
		Log(string.Format("   -> Protection Alloc: Filled={0} | ForTP1={1} (Need:{2}) | ForTP2={3}", filledQty, forTp1, neededTp1, forTp2));

		if (forTp1 > 0)
			SubmitProtectionOrders(direction, true, forTp1);
			
		if (forTp2 > 0)
			SubmitProtectionOrders(direction, false, forTp2);
			
		// Update State
		protectedTp1Qty += forTp1;
		protectedTp2Qty += forTp2;
		
		// v1.11.14: Mark protection orders as created
		protectionOrdersCreated = true;
		isProtectionProcessing = false; // UNLOCK
		Log(Time[0] + " EnsureProtection COMPLETE: protectionOrdersCreated = true");
	}
	*/
	
	// Get daily high extreme (for LONG TP2)
	private double GetDailyHigh()
	{
		// Find today's midnight
		DateTime today = Time[0].Date;
		
		// Search backwards from current bar to find highest high since midnight
		double highestPrice = High[0];
		for (int i = 0; i < CurrentBar && i < 500; i++) // Limit to 500 bars for safety
		{
			if (Time[i].Date < today) break; // Stop when we reach yesterday
			if (High[i] > highestPrice) highestPrice = High[i];
		}
		
		return highestPrice;
	}
	
	// Get daily low extreme (for SHORT TP2)
	private double GetDailyLow()
	{
		// Find today's midnight
		DateTime today = Time[0].Date;
		
		// Search backwards from current bar to find lowest low since midnight
		double lowestPrice = Low[0];
		for (int i = 0; i < CurrentBar && i < 500; i++) // Limit to 500 bars for safety
		{
			if (Time[i].Date < today) break; // Stop when we reach yesterday
			if (Low[i] < lowestPrice) lowestPrice = Low[i];
		}
		
		return lowestPrice;
	}

    // v1.14.88: Helper to find Nearest Working Order for Info Panel
    public double GetDisplayPriceForScaled(List<Order> orders, double currentPrice)
    {
        try
        {
            lock (scaledOrdersLock)
            {
                if (orders == null || orders.Count == 0) return 0;
                
                double nearestPrice = 0;
                double minDistance = double.MaxValue;
                
                // Create a copy or iterate safely?
                // Since we locked, we can iterate.
                foreach(var o in orders)
                {
                    if (o != null && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted || o.OrderState == OrderState.Submitted))
                    {
                        double dist = Math.Abs(o.LimitPrice - currentPrice);
                        if (dist < minDistance)
                        {
                            minDistance = dist;
                            nearestPrice = o.LimitPrice;
                        }
                    }
                }
                return nearestPrice;
            }
        }
        catch (Exception) 
        {
            // Fail silently on UI errors to avoid strategy deactivation
            return 0; 
        }
    }
	
	/*
	private void SubmitProtectionOrders(string direction, bool isTp1, int qty)
	{
		// SINGLE-SL ARCHITECTURE
		// Instead of creating SL1 and SL2, we create ONE SL for the entire position
		// TP1 and TP2 remain independent
		
		// ORPHAN RECOVERY - Check if orders exist in Account but lost reference
		if (Account != null)
		{
			foreach(Order o in Account.Orders)
			{
				if (o.Instrument.FullName == Instrument.FullName && 
					(o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted))
				{
					// Recover orphan SL
					if (stopOrder == null && (o.Name.StartsWith("SL_") || o.Name.Contains("_SL")))
					{
						stopOrder = o;
						Log(Time[0] + " RECOVERED orphan SL: " + o.Name + " Qty=" + o.Quantity);
					}
					// Recover orphan TP1
					if (tp1Order == null && (o.Name.StartsWith("TP1_") || o.Name.Contains("_TP1")))
					{
						tp1Order = o;
						Log(Time[0] + " RECOVERED orphan TP1: " + o.Name + " Qty=" + o.Quantity);
					}
					// Recover orphan TP2
					if (tp2Order == null && (o.Name.StartsWith("TP2_") || o.Name.Contains("_TP2")))
					{
						tp2Order = o;
						Log(Time[0] + " RECOVERED orphan TP2: " + o.Name + " Qty=" + o.Quantity);
					}
				}
			}
		}
		
		// 2. Determine Targets (TP1 vs TP2)
		double avgEntry = Position.AveragePrice; 
		
		double targetGlobalVWAP = 0;
		double targetZoneOpposite = 0;
		double slPrice = 0;
		
		double lastPrice = Close[0];
		double fallbackTargetDist = (StopLossTicks * TickSize) * 2.0;

		if (isShortSetup)
		{
			// FIXED (v1.7.21): SL siempre a 1 tick del anchor
			slPrice = setupAnchorPrice + TickSize;
			if (slPrice <= lastPrice) slPrice = lastPrice + (5 * TickSize); 
			
			// v1.14.21: REMOVED BROKEN DISTANCE CHECK (Was causing 1-tick SL in MNQ)
			// Logic removed: if (slDistanceTicks > 100) ... 

			// v1.10.31: Use Trade VWAP if active (continues accumulating even on day change)
			if (tradeVwapActive)
				targetGlobalVWAP = tradeVWAP.CurrentValue;
			else
				targetGlobalVWAP = GetCurrentLowVWAP(); 
			
			if (cachedOppositeLevel != null) targetZoneOpposite = cachedOppositeLevel.Price;
			else targetZoneOpposite = GetOppositeLevelPrice(setupLevelName, setupLevelTime);

			// v1.15.26: Use validatedTp2Price (opposite level price for TP2)
			if (validatedTp2Price > 0)
			{
				targetZoneOpposite = validatedTp2Price;
				Log("FORCE TARGET: Using Validated TP2 Price: " + validatedTp2Price);
			}

			if (targetZoneOpposite >= avgEntry) targetZoneOpposite = 0; // Invalid Short Target (must be below)
			if (targetGlobalVWAP >= avgEntry) targetGlobalVWAP = 0; // Invalid Short Target
			
			if (targetZoneOpposite <= 0) targetZoneOpposite = avgEntry - fallbackTargetDist;
			if (targetGlobalVWAP <= 0) targetGlobalVWAP = avgEntry - fallbackTargetDist;
		}
		else
		{
			// FIXED (v1.7.21): SL siempre a 1 tick del anchor
			slPrice = setupAnchorPrice - TickSize;
			if (slPrice >= lastPrice) slPrice = lastPrice - (5 * TickSize); 
			
			// v1.14.21: REMOVED BROKEN DISTANCE CHECK (Was causing 1-tick SL in MNQ)
			// Logic removed: if (slDistanceTicksLong > 100) ... 

			// v1.10.31: Use Trade VWAP if active (continues accumulating even on day change)
			if (tradeVwapActive)
				targetGlobalVWAP = tradeVWAP.CurrentValue;
			else
				targetGlobalVWAP = GetCurrentHighVWAP(); 

			if (cachedOppositeLevel != null) targetZoneOpposite = cachedOppositeLevel.Price;
			else targetZoneOpposite = GetOppositeLevelPrice(setupLevelName, setupLevelTime);
			
			// v1.15.26: Use validatedTp2Price (opposite level price for TP2)
			if (validatedTp2Price > 0)
			{
				targetZoneOpposite = validatedTp2Price;
				Log("FORCE TARGET: Using Validated TP2 Price: " + validatedTp2Price);
			}

			if (targetZoneOpposite <= avgEntry) targetZoneOpposite = 0; // Invalid Long Target
			if (targetGlobalVWAP <= avgEntry) targetGlobalVWAP = 0; // Invalid Long Target

			if (targetZoneOpposite <= 0) targetZoneOpposite = avgEntry + fallbackTargetDist;
			if (targetGlobalVWAP <= 0) targetGlobalVWAP = avgEntry + fallbackTargetDist;
		}
		
		if (targetGlobalVWAP <= 0) targetGlobalVWAP = avgEntry;
		if (targetZoneOpposite <= 0) targetZoneOpposite = avgEntry;

		// v1.15.31: Persist targetZoneOpposite to validatedTp2Price so ManagePositionExit() uses the correct value
		// This prevents TP2 from being changed to VWAP after entry fills
		if (targetZoneOpposite > 0 && validatedTp2Price <= 0)
		{
			validatedTp2Price = targetZoneOpposite;
			Log("TP2_PERSIST: Saved validatedTp2Price=" + validatedTp2Price);
		}

		// FIXED ASSIGNMENT (v1.7.21): TP1=VWAP (dinámico), TP2=Nivel (fijo)
	// v1.10.27: TP2 reverted to opposite level (was Daily Extreme in v1.10.0)
	double tp1Price = targetGlobalVWAP; // TP1 siempre VWAP opuesto
	double tp2Price = targetZoneOpposite; // TP2 = Nivel opuesto
	
	// v1.10.0: Validate TP2 is valid target
	if (isShortSetup && tp2Price >= avgEntry)
		tp2Price = avgEntry - fallbackTargetDist;
	if (!isShortSetup && tp2Price <= avgEntry)
		tp2Price = avgEntry + fallbackTargetDist;
		
		double myTpPrice = isTp1 ? tp1Price : tp2Price;
		string myTpTag = isTp1 ? "TP1" : "TP2";
		
		myTpPrice = Instrument.MasterInstrument.RoundToTickSize(myTpPrice);
		slPrice = Instrument.MasterInstrument.RoundToTickSize(slPrice);

		if (isTp1) { activeTp1Price = myTpPrice; tradeOriginalTp1Price = myTpPrice; } // v1.11.24: Also save original
		else { activeTp2Price = myTpPrice; tradeOriginalTp2Price = myTpPrice; } 

		// DEBUG TARGETS
		// v1.15.26: Show both validated prices for debugging
		Log(string.Format("TP CALC ({0}): Entry={1} | GlobalVWAP={2} | ZoneOpp={3} (ValTP1={4} ValTP2={5}) | TP1={6} TP2={7} | Selected={8}",
			direction, avgEntry, targetGlobalVWAP, targetZoneOpposite, validatedTp1Price, validatedTp2Price, tp1Price, tp2Price, myTpPrice));

		// v1.9.0: SINGLE-SL CREATION/UPDATE
		try
		{
			int totalPositionQty = Math.Abs(Position.Quantity);
			
			// Check if SL already exists
			Order existingSL = stopOrder;
			Order existingTP = isTp1 ? tp1Order : tp2Order;
			
			// Determine if we need to cancel-consolidate the SL
			bool shouldUpdateSL = (existingSL != null && (existingSL.OrderState == OrderState.Working || existingSL.OrderState == OrderState.Accepted));
			bool shouldUpdateTP = (existingTP != null && (existingTP.OrderState == OrderState.Working || existingTP.OrderState == OrderState.Accepted));
			
			// STEP 1: Handle STOP LOSS (single for entire position)
			// v1.11.26 FIX: Crear SL si no existe O si la orden existente ya no está activa
			bool slAlreadyActive = (stopOrder != null && 
				(stopOrder.OrderState == OrderState.Working || 
				 stopOrder.OrderState == OrderState.Accepted ||
				 stopOrder.OrderState == OrderState.Submitted));
			
			// v1.11.26: Si stopOrder tiene referencia pero NO está activa, limpiarla
			if (stopOrder != null && !slAlreadyActive)
			{
				Log(string.Format("SL CLEANUP: Clearing stale reference (State={0})", stopOrder.OrderState));
				stopOrder = null;
			}
			
			// v1.13.5: Also check if SL already created in this call to EnsureProtection
			if (stopOrder == null && !slOrderCreatedThisEntry)
			{
				// Create new SL
				string slTag = string.Format("{0}_{1:D2}", direction == "Short" ? "SL_Short" : "SL_Long", currentVwapNumber);
				OrderAction slAction = direction == "Short" ? OrderAction.BuyToCover : OrderAction.Sell;
				
				// v1.14.38: DIAGNOSTIC - Log every SL creation with full context
				Log(string.Format("SL_CREATE_DEBUG: Instrument={0} Direction={1} Tag={2} Action={3} Price={4} Qty={5} State={6} EntryState={7}",
					Instrument.FullName, direction, slTag, slAction, slPrice, totalPositionQty, State, currentEntryState));
				
				stopOrder = SubmitOrderUnmanaged(0, slAction, OrderType.StopMarket, totalPositionQty, 0,slPrice, "", slTag);
				slOrderCreatedThisEntry = true; // v1.13.5: Mark SL as created
				
				// v1.13.12: Calculate and store risk in USD for R:R analysis
				tradeRiskUSD = Math.Abs(avgEntry - slPrice) * totalPositionQty * Instrument.MasterInstrument.PointValue;
				
				Log(string.Format("SL CREATED: {0} @ {1} Qty={2} Risk=${3:F2}", slTag, slPrice, totalPositionQty, tradeRiskUSD));
			}
			else if (slOrderCreatedThisEntry && stopOrder != null && 
				(stopOrder.OrderState == OrderState.Working || stopOrder.OrderState == OrderState.Accepted) &&
				stopOrder.Quantity != totalPositionQty)
			{
				// v1.14.28: SL exists but quantity changed due to partial fill - UPDATE IT
				Log(string.Format("SL UPDATE (Partial Fill): Old Qty={0} New Qty={1}", stopOrder.Quantity, totalPositionQty));
				ChangeOrder(stopOrder, totalPositionQty, 0, slPrice);
			}
			else if (slOrderCreatedThisEntry)
			{
				Log("SL SKIPPED: Already created in current entry (duplicate prevention)");
			}
			else if (slAlreadyActive)
			{
				Log(string.Format("SL ALREADY EXISTS (State={0}), skipping creation", stopOrder.OrderState));
			}
			else if (shouldUpdateSL && stopOrder.Quantity != totalPositionQty)
			{
				// SL exists but needs quantity update
				Log(string.Format("SL UPDATE: Cancelling old SL (Qty={0}), creating new (Qty={1})", 
					stopOrder.Quantity, totalPositionQty));
				try {
					CancelOrder(stopOrder);
					stopOrder = null; // Clear reference, will be recreated on next call
				} catch (Exception ex) {
					Log("Warning: Could not cancel old SL: " + ex.Message);
				}
			}
			
			// STEP 2: Handle TAKE PROFIT (TP1 or TP2)
			int tpQty = isTp1 ? (protectedTp1Qty + qty) : (protectedTp2Qty + qty);
			
			if (shouldUpdateTP)
			{
				// TP exists - cancel and recreate with updated quantity
				Log(string.Format("CANCEL-CONSOLIDATE {0}: Cancelling old (Qty={1}), creating new (Qty={2})", 
					myTpTag, existingTP.Quantity, tpQty));
				try {
					CancelOrder(existingTP);
				} catch (Exception ex) {
					Log("Warning: Could not cancel old TP: " + ex.Message);
				}
			}
			
			// v1.11.12 FIX: Solo crear TP si NO existe uno activo
			// Antes: siempre creaba TP causando 4 TP1 en lugar de 1
			Order currentTP = isTp1 ? tp1Order : tp2Order;
			
			// v1.14.35: DIAGNOSTIC LOG - Capture exact OrderState before check
			string tpStateDebug = currentTP != null ? currentTP.OrderState.ToString() : "NULL";
			Log(string.Format("DEBUG_TP_STATE: {0} currentTP={1} State={2} | protectedQty={3} newQty={4}",
				isTp1 ? "TP1" : "TP2",
				currentTP != null ? currentTP.Name : "null",
				tpStateDebug,
				isTp1 ? protectedTp1Qty : protectedTp2Qty,
				qty));
			
			// v1.14.30: Added PartFilled to prevent duplicate TPs during rapid partial fills
			bool tpAlreadyActive = (currentTP != null && 
				(currentTP.OrderState == OrderState.Working || 
				 currentTP.OrderState == OrderState.Accepted ||
				 currentTP.OrderState == OrderState.Submitted ||
				 currentTP.OrderState == OrderState.PartFilled));
			
			if (!tpAlreadyActive)
			{
				// Create TP only if none exists
				string tpBase = direction == "Short" ? 
					(isTp1 ? "TP1_Short" : "TP2_Short") : 
					(isTp1 ? "TP1_Long" : "TP2_Long");
				string tpTag = string.Format("{0}_{1:D2}", tpBase, currentVwapNumber);
				OrderAction tpAction = direction == "Short" ? OrderAction.BuyToCover : OrderAction.Sell;
				
				if (isTp1) {
					tp1Order = SubmitOrderUnmanaged(0, tpAction, OrderType.Limit, tpQty, myTpPrice, 0, "", tpTag);
					Log(string.Format("TP1 CREATED: {0} @ {1} Qty={2}", tpTag, myTpPrice, tpQty));
				} else {
					tp2Order = SubmitOrderUnmanaged(0, tpAction, OrderType.Limit, tpQty, myTpPrice, 0, "", tpTag);
					Log(string.Format("TP2 CREATED: {0} @ {1} Qty={2}", tpTag, myTpPrice, tpQty));
				}
			}
			else
			{
				Log(string.Format("{0} ALREADY EXISTS (State={1}), skipping creation", 
					isTp1 ? "TP1" : "TP2", currentTP.OrderState));
			}
			// v1.10.34: TP labels removed for cleaner chart
		}
		catch (Exception ex)
		{
			Log("CRITICAL ERROR Submitting Exits: " + ex.Message);
		}
	}
	*/
		
	// v1.10.0: Detect if setup level is INTERNAL (Delegated)
	public void DetectInternalLevel(SessionLevel setupLevel, List<SessionLevel> allLevels)
	{
		if (protectionManager != null) 
        {
            protectionManager.DetectInternalLevel(setupLevel, allLevels);
            // Sync local state
            isInternalLevel = protectionManager.IsInternalLevel;
            externalLevelAboveName = protectionManager.ExternalLevelAboveName;
            externalLevelBelowName = protectionManager.ExternalLevelBelowName;
        }
	}
		
	// CORRECTED (v1.7.22): Search for opposite level from SAME DAY (Delegated)
	public double GetOppositeLevelPrice(string name, DateTime refTime, double refPrice = 0, bool expectLower = false)
	{
		if (protectionManager != null)
        {
            SessionLevel found = null;
            double price = protectionManager.GetOppositeLevelPrice(name, refTime, activeLevels, cachedOppositeLevel, oppositeSearchDone, out found);
            if (found != null) cachedOppositeLevel = found;
            // Also update the Done flag if manager set it? 
            // The manager returns 0 if not found, but we need to know if it finished searching.
            // Actually, manager logic handles the search.
            // Let's assume manager handles it correctly.
            // Wait, we need to sync 'oppositeSearchDone' flag back? 
            // Or just rely on manager returning 0 consistently.
            // Let's check manager implementation... Manager checks 'oppositeSearchDone' passed in.
            // But it doesn't return 'oppositeSearchDone' status explicitly other than via 'found'.
            // Simpler: Just rely on local flag update if I can access it.
            // I'll make sure to update local 'oppositeSearchDone' if price is 0 and we expected something.
            // Actually, simpler: Let's assume if it returns, it's done.
            if (found == null) oppositeSearchDone = true; 
            return price;
        }
        return 0;
	}



		
		public bool isValidVWAP(double val)
		{
			return val > 0 && !double.IsNaN(val);
		}

		private int screenshotSequence = 0; // For unique filenames

		private void TriggerScreenshot(string eventName, DateTime time, string execId)
		{
			// Only take screenshots in LIVE/REALTIME mode or if forcing it (users choice)
			// Generally we prefer Realtime to avoid spam during reload.
			if (State != State.Realtime) return;

			// Check if EITHER Local OR Email is enabled
			if ((EnableLocalScreenshots || EnableEmailAlerts) && ChartControl != null)
			{
				try 
				{
					// Must run on UI Thread
					ChartControl.Dispatcher.InvokeAsync((Action)(() => 
					{
						try 
						{
							// 1. Get Screen Coordinates of the ChartControl
							System.Windows.Point p = ChartControl.PointToScreen(new System.Windows.Point(0, 0));
							
							// 2. Get Dimensions
							int w = (int)ChartControl.ActualWidth;
							int h = (int)ChartControl.ActualHeight;
							
							if (w > 0 && h > 0)
							{
								// 3. Creates System.Drawing.Bitmap (WinForms/GDI+)
								// Fully qualified to avoid namespace ambiguity
								using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(w, h))
								{
									using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bitmap))
									{
										// 4. Capture Screen
										g.CopyFromScreen((int)p.X, (int)p.Y, 0, 0, new System.Drawing.Size(w, h));
									}
									
									// 5. Build Path
									string docPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
									string shotFolder = System.IO.Path.Combine(docPath, "NinjaTrader 8", "Strategy_Screenshots");
									if (!System.IO.Directory.Exists(shotFolder)) System.IO.Directory.CreateDirectory(shotFolder);
									
									// Increment Sequence
									screenshotSequence++;

									string cleanName = Instrument.FullName.Replace(" ", "_");
									string fileName = string.Format("{0:D4}_{1}_{2}_{3}_{4}.png", 
										screenshotSequence,
										eventName,
										cleanName,
										time.ToString("yyyyMMdd_HHmmss"),
										execId.GetHashCode()); 
										
									string fullPath = System.IO.Path.Combine(shotFolder, fileName);
									
									// 6. Save
									bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
									Log(Time[0] + " Snapshot Saved: " + fullPath);
									
									// 7. Send Email (Async-ish)
									if (EnableEmailAlerts)
									{
										SendEmailWithAttachment(eventName, fullPath);
									}
								}
							}
						}
						catch (Exception innerEx) { Print(Time[0] + " Screen Capture Failed: " + innerEx.Message); }
					}));
				}
				catch (Exception ex) { Print(Time[0] + " Snapshot Dispatch Failed: " + ex.Message); }
			}
		}


		#endregion
		
		private void LogTrade(Trade trade)
		{
			string action = (trade.Entry.MarketPosition == MarketPosition.Long) ? "TR_LONG" : "TR_SHORT";
			Log(string.Format("{0} TRADE CLOSED: {1} at {2}, Exit at {3}, Profit: {4}", 
				DateTime.Now, action, trade.Entry.Price, trade.Exit.Price, trade.ProfitCurrency.ToString("C")));
		}

		protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
		{
			// v1.14.76: Check Apteros Risk Intra-bar (Realtime Monitoring)
            if (riskManager != null && SelectedRiskModel == RiskModelType.Apteros)
            {
               // Check if we hit the limit RIGHT NOW
               if (!riskManager.CheckRiskState(SelectedRiskModel, Account.Get(AccountItem.CashValue, Currency.UsDollar), ApterosDailyLossPercent, ApterosMaxTrailingDrawdown))
               {
                   if (marketPosition != MarketPosition.Flat)
                   {
                       SendCriticalAlert("APTEROS RISK LIMIT", string.Format("Daily loss limit reached. Position closed. DailyLoss%={0}, MaxDD={1}", ApterosDailyLossPercent, ApterosMaxTrailingDrawdown));
                       ClosePositionUnmanaged("Apteros Risk Limit Hit (Intra-bar)");
                   }
               }
            }

			// Detect Trade Close (Transition to Flat)
			if (marketPosition == MarketPosition.Flat && position.Instrument == Instrument)
			{
				// We just closed a position. Log the last trade.
				if (SystemPerformance != null && SystemPerformance.RealTimeTrades != null && SystemPerformance.RealTimeTrades.Count > 0)
				{
					Trade lastTrade = SystemPerformance.RealTimeTrades[SystemPerformance.RealTimeTrades.Count - 1];
					// Verify it just happened (within last few seconds? Or just assume sequentially correct)
					// "RealTimeTrades" only updates on close. So the last one IS the one we just closed.
					LogTrade(lastTrade);
				}
				
			}
		}

		private void SendEmailWithAttachment(string subject, string attachmentPath)
		{
			try 
			{
				// Basic Validation
				if (string.IsNullOrEmpty(EmailHost) || string.IsNullOrEmpty(EmailUsername) || string.IsNullOrEmpty(EmailPassword))
					return;

				Task.Run(() => 
				{
					try 
					{
						using (MailMessage mail = new MailMessage())
						{
							mail.From = new MailAddress(EmailFrom);
							mail.To.Add(EmailTo);
							mail.Subject = "NinjaTrader Alert: " + subject + " - " + Instrument.FullName;
							mail.Body = string.Format("Trade alert for {0} at {1}.\nEvent: {2}", Instrument.FullName, DateTime.Now, subject);
							
							if (File.Exists(attachmentPath))
							{
								Attachment data = new Attachment(attachmentPath, "image/png");
								mail.Attachments.Add(data);
							}
							
							using (SmtpClient smtp = new SmtpClient(EmailHost, EmailPort))
							{
								smtp.Credentials = new NetworkCredential(EmailUsername, EmailPassword);
								smtp.EnableSsl = true;
								smtp.Send(mail);
							}
							Log("Email Sent to " + EmailTo);
						}
					}
					catch (Exception ex)
					{
						Print("Email Failed: " + ex.Message);
					}
				});
			}
			catch (Exception ex) { Print("Email Setup Failed: " + ex.Message); }
		}

		// =========================================================
		// v1.15.38: ENHANCED EMAIL NOTIFICATIONS
		// =========================================================
		
		/// <summary>
		/// Sends a detailed email when a trade entry is filled (once per trade, ignores partial fills)
		/// </summary>
		private void SendTradeEntryEmail()
		{
			if (!EnableEmailAlerts || emailSentOnEntry) return;
			if (State != State.Realtime) return;
			
			emailSentOnEntry = true;
			
			string direction = isShortSetup ? "SHORT" : "LONG";
			int qty = tradeOriginalQty > 0 ? tradeOriginalQty : Math.Abs(Position.Quantity);
			double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
			double riskUSD = Math.Abs(tradeEntryPrice - setupAnchorPrice) / TickSize * tickValue * qty;
			
			string subject = string.Format("ENTRY: {0} {1} x{2}", Instrument.FullName, direction, qty);
			string body = string.Format(
				"=== TRADE ENTRY ===\n" +
				"Instrumento: {0}\n" +
				"Dirección: {1}\n" +
				"Contratos: {2}\n" +
				"Precio Entrada: {3}\n" +
				"Nivel: {4}\n" +
				"Risk: ${5:F2}\n" +
				"SL: {6}\n" +
				"TP1: {7}\n" +
				"TP2: {8}\n" +
				"Hora: {9}",
				Instrument.FullName,
				direction,
				qty,
				tradeEntryPrice,
				setupLevelName,
				riskUSD,
				setupAnchorPrice,
				validatedTp1Price > 0 ? validatedTp1Price.ToString("F2") : "VWAP",
				validatedTp2Price > 0 ? validatedTp2Price.ToString("F2") : "Opposite Level",
				DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			
			SendEmailText(subject, body);
			Log("EMAIL SENT: " + subject);
		}
		
		/// <summary>
		/// Sends a detailed email when a trade exits (with PnL details)
		/// </summary>
		private void SendTradeExitEmail(double grossPnL, double commission, string exitReason)
		{
			if (!EnableEmailAlerts || emailSentOnExit) return;
			if (State != State.Realtime) return;
			
			emailSentOnExit = true;
			
			double netPnL = grossPnL - commission;
			string result = netPnL >= 0 ? "WIN" : "LOSS";
			TimeSpan duration = DateTime.Now - tradeEntryTime;
			
			string subject = string.Format("EXIT ({0}): {1} ${2:F2}", result, Instrument.FullName, netPnL);
			string body = string.Format(
				"=== TRADE EXIT ===\n" +
				"Instrumento: {0}\n" +
				"Resultado: {1}\n" +
				"Razón: {2}\n" +
				"PnL Bruto: ${3:F2}\n" +
				"Comisión: ${4:F2}\n" +
				"PnL Neto: ${5:F2}\n" +
				"MAE: ${6:F2}\n" +
				"MFE: ${7:F2}\n" +
				"Duración: {8}\n" +
				"Hora: {9}",
				Instrument.FullName,
				result,
				exitReason,
				grossPnL,
				commission,
				netPnL,
				tradeMAE,
				tradeMFE,
				duration.ToString(@"hh\:mm\:ss"),
				DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			
			SendEmailText(subject, body);
			Log("EMAIL SENT: " + subject);
		}
		
		/// <summary>
		/// Sends an email for critical/emergency events
		/// </summary>
		private void SendCriticalAlert(string eventType, string details)
		{
			if (!EnableEmailAlerts) return;
			if (State != State.Realtime) return;
			
			string subject = string.Format("⚠️ CRITICAL: {0} - {1}", eventType, Instrument.FullName);
			string body = string.Format(
				"=== CRITICAL ALERT ===\n" +
				"Evento: {0}\n" +
				"Instrumento: {1}\n" +
				"Detalles: {2}\n" +
				"Hora: {3}\n" +
				"==================\n" +
				"Revisa NinjaTrader inmediatamente.",
				eventType,
				Instrument.FullName,
				details,
				DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			
			SendEmailText(subject, body);
			Log("CRITICAL EMAIL SENT: " + subject);
		}
		
		/// <summary>
		/// Sends a plain text email (no attachment)
		/// </summary>
		private void SendEmailText(string subject, string body)
		{
			try 
			{
				if (string.IsNullOrEmpty(EmailHost) || string.IsNullOrEmpty(EmailUsername) || string.IsNullOrEmpty(EmailPassword))
					return;

				Task.Run(() => 
				{
					try 
					{
						using (MailMessage mail = new MailMessage())
						{
							mail.From = new MailAddress(EmailFrom);
							mail.To.Add(EmailTo);
							mail.Subject = subject;
							mail.Body = body;
							
							using (SmtpClient smtp = new SmtpClient(EmailHost, EmailPort))
							{
								smtp.Credentials = new NetworkCredential(EmailUsername, EmailPassword);
								smtp.EnableSsl = true;
								smtp.Send(mail);
							}
						}
					}
					catch (Exception ex)
					{
						Print("Email Failed: " + ex.Message);
					}
				});
			}
			catch (Exception ex) { Print("Email Setup Failed: " + ex.Message); }
		}

		public double GetCurrentHighVWAP() { return vwapCalc != null ? vwapCalc.GetCurrentHighVWAP() : 0; }
		public double GetCurrentLowVWAP() { return vwapCalc != null ? vwapCalc.GetCurrentLowVWAP() : 0; }
	
	// CONTINUOUS R/R VALIDATION (v1.7.28)
	// v1.13.14: Added detailed diagnostic logging
	public bool ValidateRiskReward(bool isShort, double entryPrice, double stopPrice, out double risk, out double reward, out double ratio)
	{
	// Calculate both targets
		double tp1Target = isShort ? GetCurrentLowVWAP() : GetCurrentHighVWAP();
		double tp2Target = GetOppositeLevelPrice(setupLevelName, setupLevelTime, setupAnchorPrice, isShort); // isShort = expectLower
		
		if (tp2Target == 0) tp2Target = tp1Target; // Fallback
		
		// v1.14.53: Fix - Filter valid targets FIRST, then find closest
		// For Long: target must be ABOVE entry
		// For Short: target must be BELOW entry
		bool tp1Valid = isShort ? (tp1Target < entryPrice) : (tp1Target > entryPrice);
		bool tp2Valid = isShort ? (tp2Target < entryPrice) : (tp2Target > entryPrice);
		
		double closestTarget = 0;
		bool validDirection = false;
		
		if (tp1Valid && tp2Valid)
		{
			// Both valid: pick the closest
			closestTarget = isShort 
				? Math.Max(tp1Target, tp2Target)  // Short: higher (closer to entry) is better
				: Math.Min(tp1Target, tp2Target); // Long: lower (closer to entry) is better
			validDirection = true;
		}
		else if (tp1Valid)
		{
			closestTarget = tp1Target;
			validDirection = true;
		}
		else if (tp2Valid)
		{
			closestTarget = tp2Target;
			validDirection = true;
		}
		// else: neither valid, validDirection stays false
		
		// Calculate risk/reward
		risk = Math.Abs(entryPrice - stopPrice);
		
		reward = validDirection 
			? (isShort ? (entryPrice - closestTarget) : (closestTarget - entryPrice))
			: 0;
		
		ratio = (risk > 0) ? (reward / risk) : 0;
		
		// v1.13.14: Detailed diagnostic logging when R:R fails
		bool isValid = validDirection && risk > 0 && ratio >= MinRiskRewardRatio;
		if (!isValid)
		{
			string dir = isShort ? "Short" : "Long";
			string reason = !validDirection ? "Invalid Direction" : (risk <= 0 ? "Zero Risk" : $"R:R {ratio:F2} < Min {MinRiskRewardRatio}");
			Log(string.Format("R/R REJECTED ({0}): Entry={1} SL={2} | TP1(VWAP)={3} TP2(Level)={4} | Selected={5} | Risk={6:F4} Reward={7:F4} Ratio={8:F2} | Reason: {9}",
				dir, entryPrice, stopPrice, tp1Target, tp2Target, closestTarget, risk, reward, ratio, reason));
		}
		
		// Return true if valid
		return isValid;
	}
		
		public double GetSetupVWAP(bool isShort)
	{
		// v1.14.55: Use VWAP Global for EXTERNAL levels, VWAP Adhoc for INTERNAL levels
		// External level = session breaks a level from a different session (e.g., Europe breaks Asia High)
		// Internal level = session trades its own High/Low
		
		// For EXTERNAL levels, use the Global Session VWAP (the visible line on the chart)
		if (!isInternalLevel)
		{
			double globalValue = isShort ? GetCurrentHighVWAP() : GetCurrentLowVWAP();
			return globalValue;
		}
		
		// For INTERNAL levels, use the ADHOC VWAP (calculated since the touch)
		if (!string.IsNullOrEmpty(setupLevelName) && adhocVolSum > 0)
		{
			double adhocValue = adhocPvSum / adhocVolSum;
			return adhocValue;
		}
		
		// Fallback to Global
		double fallbackValue = isShort ? GetCurrentHighVWAP() : GetCurrentLowVWAP();
		return fallbackValue;
	}

		private void ManagePositionExit()
		{
			// FAILSAFE: If we are actually Flat, do not attempt to manage exits.
			// This prevents "Ghost" order modifications after "Exit on session close"
			if (Position.MarketPosition == MarketPosition.Flat) return;
			
			// v1.10.32: Do not modify orders during Historical mode or transition
			// This prevents "attempted to modify a historical order" error during playback
			if (State == State.Historical) return;
			
			// Dynamic TP Management
			// We only update if we have active TP orders that are working
			bool updateTp1 = (tp1Order != null && (tp1Order.OrderState == OrderState.Working || tp1Order.OrderState == OrderState.Accepted));
			bool updateTp2 = (tp2Order != null && (tp2Order.OrderState == OrderState.Working || tp2Order.OrderState == OrderState.Accepted));
			
			if (!updateTp1 && !updateTp2) return;

			// Recalculate Targets
			double targetGlobalVWAP = 0;
			double targetZoneOpposite = 0;
			double avgEntry = Position.AveragePrice; // Use Position Avg Price or Entry Order

			// Use Position Avg Price as primary
	// Or entryOrder1 / entryOrder2 if available
			// Or entryOrder if available
			if (entryOrder != null) avgEntry = entryOrder.AverageFillPrice;
	// Calculate weighted if both exist (rarely differs if same tick)
			// v1.7.17: Consolidated Entry, so no averaging needed.
			// (Old averaging logic removed)
			
			if (isShortSetup)
			{
				// v1.14.74: Use Trade VWAP only if trade crossed 18:00, otherwise use Global VWAP
				if (IsTradeVwapExtended && vwapCalc != null)
					targetGlobalVWAP = vwapCalc.GetTradeVWAPCurrentValue();
				else
					targetGlobalVWAP = GetCurrentLowVWAP(); 
				// FIX (v1.6.2): Use setupLevelTime to ensure stable target throughout the trade
				targetZoneOpposite = GetOppositeLevelPrice(setupLevelName, setupLevelTime);
				
				// v1.14.85: FIX - Use validatedTp2Price if available, don't fallback to VWAP
				// v1.15.26: Use validatedTp2Price (opposite level price for TP2)
				if (targetZoneOpposite <= 0)
				{
					if (validatedTp2Price > 0)
						targetZoneOpposite = validatedTp2Price; // Use persistent validated target for TP2
					else
						targetZoneOpposite = targetGlobalVWAP; // Last resort fallback
				}
			}
			else
			{
				// v1.14.74: Use Trade VWAP only if trade crossed 18:00, otherwise use Global VWAP
				if (IsTradeVwapExtended && vwapCalc != null)
					targetGlobalVWAP = vwapCalc.GetTradeVWAPCurrentValue();
				else
					targetGlobalVWAP = GetCurrentHighVWAP(); 
				// FIX (v1.6.2): Use setupLevelTime here too
				targetZoneOpposite = GetOppositeLevelPrice(setupLevelName, setupLevelTime);
				
				// v1.14.85: FIX - Use validatedTp2Price if available, don't fallback to VWAP
				// v1.15.26: Use validatedTp2Price (opposite level price for TP2)
				if (targetZoneOpposite <= 0)
				{
					if (validatedTp2Price > 0)
						targetZoneOpposite = validatedTp2Price; // Use persistent validated target for TP2
					else
						targetZoneOpposite = targetGlobalVWAP; // Last resort fallback
				}
			}
			
			// Sanity
			if (targetGlobalVWAP <= 0) targetGlobalVWAP = avgEntry; 
			if (targetZoneOpposite <= 0) targetZoneOpposite = avgEntry;

			// FIXED ASSIGNMENT (v1.7.21): TP1=VWAP (actualiza), TP2=Nivel (fijo)
		double newTp1Price = targetGlobalVWAP; // Actualiza dinámicamente
		double newTp2Price = targetZoneOpposite; // Mantiene nivel validado fijo
			
			// Rounding
			newTp1Price = Instrument.MasterInstrument.RoundToTickSize(newTp1Price);
			newTp2Price = Instrument.MasterInstrument.RoundToTickSize(newTp2Price);

			// Update TP1
			if (updateTp1)
			{
				if (Math.Abs(tp1Order.LimitPrice - newTp1Price) >= TickSize)
				{
					// Keep same Quantity, update Price
					ChangeOrder(tp1Order, tp1Order.Quantity, newTp1Price, 0);
				}
			}

			// Update TP2
			if (updateTp2)
			{
				if (Math.Abs(tp2Order.LimitPrice - newTp2Price) >= TickSize)
				{
					ChangeOrder(tp2Order, tp2Order.Quantity, newTp2Price, 0);
				}
			}
		}

		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
		{
			try
			{
			// 1. Entry Order Tracking
			if (order.Name.Contains("EntryA+_"))
			{
				entryOrder = order;
				
				// Handle Terminal States for Entry
				// Unmanaged: If order is Rejected, we must reset state or we get stuck 'Working' forever.
				if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected) 
				{
					Log(Time[0] + " ENTRY TERMINATED: " + order.Name + " State: " + orderState + " Err: " + error);
					
					// v1.15.38: Send critical alert for order rejection
					if (orderState == OrderState.Rejected)
					{
						SendCriticalAlert("ORDER REJECTED", string.Format("Order {0} rejected. Error: {1}. NativeError: {2}", order.Name, error, nativeError));
					}
					
					// Force check: Is it dead?
					// Use 'entryOrder'
					bool anyWorking = false;
					if (entryOrder != null && (entryOrder.OrderState == OrderState.Working || entryOrder.OrderState == OrderState.Accepted)) anyWorking = true;
					
					if (!anyWorking && currentEntryState == EntryState.workingOrder)
					{
						Log(Time[0] + " ENTRY RESET: All entry orders cancelled/rejected. Resetting to IDLE.");
						
						// LOOP PROTECTION:
						// If we reset to Idle on the SAME BAR, ManageEntryA_Plus will see the trigger again and loop.
						// We must block re-entry for this bar.
						lastRejectionBar = CurrentBar;
						
						// FIX (Zombie Prev): Only reset if we are truly FLAT and NO FILLS occurred.
						// If filled > 0, it means we have a partial position even if the rest was cancelled.
						// "filled" param is cumulative.
						if (Position.MarketPosition == MarketPosition.Flat && filled == 0)
						{
							currentEntryState = EntryState.Idle; // UNSTUCK THE STRATEGY
							Log(Time[0] + " ENTRY RESET: All entry orders cancelled/rejected and Flat. Resetting to IDLE.");
							
							// Clear references to be clean
							entryOrder = null;
						}
						else
						{
							Log(Time[0] + " ENTRY WARNING: Order Rejected but Position Active. Keeping State " + currentEntryState);
						}
					}
				}
			}
			
			// 2. Generic Reference Updates
		if (order.Name.Contains("SL_"))
		{
			stopOrder = order; // Legacy/Fallback
			if (order.Name.EndsWith("_1")) stopOrder1 = order;
			else if (order.Name.EndsWith("_2")) stopOrder2 = order;
		}
		
		if (order.Name.Contains("TP"))
		{
			// v1.14.84: DIAGNOSTIC - Log exact order name to detect reference corruption
			Log($"ORDER_UPDATE_TP: Name='{order.Name}' State={order.OrderState} Price={order.LimitPrice} Qty={order.Quantity}");
			
			if (order.Name.Contains("TP1_")) 
			{
				tp1Order = order;
				Log($"  -> Updated tp1Order reference (ID={order.OrderId})");
			}
			else if (order.Name.Contains("TP2_")) 
			{
				tp2Order = order;
				Log($"  -> Updated tp2Order reference (ID={order.OrderId})");
			}
			else
			{
				Log($"  -> WARNING: TP order name does not contain TP1_ or TP2_!");
			}
		}	
			// TP Orders tracked via SubmitOrder return, but we can capture them here too if needed.
			}
			catch (Exception ex)
			{
				NinjaTrader.Code.Output.Process("CRITICAL ERROR in OnOrderUpdate: " + ex.ToString(), PrintTo.OutputTab1);
				Log("CRITICAL ERROR in OnOrderUpdate: " + ex.ToString());
			}
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			try
			{
			if (execution.Order.OrderState == OrderState.Filled || execution.Order.OrderState == OrderState.PartFilled)
			{
				string n = execution.Order.Name;
				
				// CONSOLIDATED ROUTING (v1.7.17)
				if (n.Contains("EntryA+_") || n.Contains("EntryAnticipado_")) 
				{
					if (currentEntryState == EntryState.workingOrder)
					{
						currentEntryState = EntryState.PositionActive;
						tradeOriginalQty = quantity; // v1.11.23: Save original trade qty for panel display
						Log(Time + " Entry Filled ("+n+") Qty=" + quantity + ". State -> PositionActive. TradeOriginalQty=" + tradeOriginalQty);
						
						// v1.13.0: Initialize TradeAnalyzer export variables
						tradeExportId++;
						tradeExitFillsCount = 0; // v1.13.4: Reset exit fills counter
				// v1.14.39: Reset Manager State
				protectionManager.ResetEntryState(); 
						protectedTp1Qty = 0; // v1.14.27: Reset protection counters for new trade
						protectedTp2Qty = 0; // v1.14.27: Prevents residual values from previous trades
						tradeEntryPrice = execution.Order.AverageFillPrice;
						tradeEntryTime = time;
							tradeDirection = Position.MarketPosition == MarketPosition.Long ? "Long" : "Short";
						tradeSetupName = setupLevelName;
						tradeAttemptNumber = currentLevelAttempts; // v1.15.20: Use level attempts instead of VWAP retries
						tradeMAE = 0;
						tradeMFE = 0;
						isTrackingTrade = true; // Flag to track MAE/MFE
						
						// v1.15.38: Reset email flags for new trade
						emailSentOnEntry = false;
						emailSentOnExit = false;
						
						// v1.14.31: Capture Delta values at entry for quantitative analysis
						if (relativeDelta != null && CurrentBar > 0)
						{
							try
							{
								tradeDeltaAtEntry = relativeDelta.DeltaClose[0];
								tradeSessionDelta = relativeDelta.DeltaClose[0]; // Session cumulative
								// Direction: 1=aligned (Long+positiveDelta or Short+negativeDelta), -1=opposed
								if (tradeDirection == "Long")
									tradeDeltaDirection = tradeDeltaAtEntry >= 0 ? 1 : -1;
								else
									tradeDeltaDirection = tradeDeltaAtEntry <= 0 ? 1 : -1;
								Log(Time + " DELTA CAPTURE: Entry Delta=" + tradeDeltaAtEntry + " Direction=" + tradeDeltaDirection);
							}
							catch { tradeDeltaAtEntry = 0; tradeDeltaDirection = 0; }
						}
						else
						{
							tradeDeltaAtEntry = 0; tradeDeltaDirection = 0; tradeSessionDelta = 0;
						}
					}
					else if (currentEntryState == EntryState.PositionActive)
					{
						// v1.14.77: Accumulate quantity on partial fills
						tradeOriginalQty += quantity;
						Log(Time + " Entry Partial Fill ("+n+") Qty=" + quantity + ". New TradeOriginalQty=" + tradeOriginalQty);
					}
						tradeDeltaAtTP1 = 0; // Reset, will be set when TP1 fills
						
						// v1.14.81: DIAGNOSTIC LOG to check for Position Lag
						Log(string.Format("DIAG_EXEC: Name={0} Qty={1} | Position.MP={2} Arg.MP={3} | State={4}", 
							n, quantity, Position.MarketPosition, marketPosition, currentEntryState));
					
                        // v1.15.36: Section header moved to trigger (EntryStateMachine)
                        // Just log the fill event here
                        Log(string.Format("   FILL: Trade #{0} | {1} @ {2} | Qty={3}", tradeExportId, tradeDirection, tradeEntryPrice, quantity));

					Log(Time + " CSV EXPORT: Trade #" + tradeExportId + " started - " + tradeDirection + " @ " + tradeEntryPrice);
					
					// Ensure Protection Runs based on FILLED QTY
					// v1.7.17: We pass the filled amount, protection logic distributes it to buckets.
					// v1.14.81: Use Arg.MP as fallback if Position.MP is lagging (Flat)
					// v1.15.26: Pass separate TP1 and TP2 prices to fix MCL bug
					if (Position.MarketPosition == MarketPosition.Short || marketPosition == MarketPosition.Short)
					{
						// EnsureProtection Delegate (v1.14.39)
						protectionManager.EnsureProtection("Short", n, quantity, currentVwapNumber, isShortSetup, setupLevelName, setupLevelTime, setupAnchorPrice, validatedTp1Price, validatedTp2Price);
						TriggerScreenshot("Entry_Short_" + n, DateTime.Now, executionId);
						SendTradeEntryEmail(); // v1.15.38: Enhanced email notification
					}
					else if (Position.MarketPosition == MarketPosition.Long || marketPosition == MarketPosition.Long)
					{
						// EnsureProtection Delegate (v1.14.39)
						protectionManager.EnsureProtection("Long", n, quantity, currentVwapNumber, isShortSetup, setupLevelName, setupLevelTime, setupAnchorPrice, validatedTp1Price, validatedTp2Price);
						TriggerScreenshot("Entry_Long_" + n, DateTime.Now, executionId);
					}
					else
					{
						Log("DIAG_ERROR: Protection Skipped! P.MP=" + Position.MarketPosition + " A.MP=" + marketPosition);
					}
				}
				else
				{
					// EXIT EXECUTION (TP1, Ladder, etc.)
					Log(Time + " Exit Execution (" + execution.Order.Name + "). Qty=" + quantity);

					// v1.15.40: Ladder Exit - Handle 1R Fill (Step 1) to trigger Breakeven
					if (execution.Order.Name.Contains("LadderTP_"))
					{
						// Tag format: LadderTP_{step}_{vwapNum} -> e.g. LadderTP_1_1
						Log(Time + " LADDER EXECUTION: " + execution.Order.Name + " Qty=" + quantity);
						
						// ALL Steps triggers SL Quantity Reduction (Smart Logic in HandleTP1Fill handles lag)
						// Step 1 also ensures BE (handled inside)
						if (protectionManager != null) protectionManager.HandleTP1Fill(quantity);
					}

					// Standard TP1 Fill (Fallback if not handled elsewhere)
					if (execution.Order.Name.Contains("TP1_"))
					{
							// Ensure BE logic runs
							if (protectionManager != null) protectionManager.HandleTP1Fill(quantity);
					}
				}
			}
			if (execution.Order != null && execution.Order.OrderState == OrderState.Filled)
			{
				// v1.14.57: DIAGNOSTIC LOG for TP1 detection
				Log(string.Format("EXEC_FILL_DEBUG: Order={0} Name={1} State={2} Price={3} | tp1Order={4} tp1Name={5}",
					execution.Order.GetHashCode(),
					execution.Order.Name,
					execution.Order.OrderState,
					execution.Order.AverageFillPrice,
					tp1Order != null ? tp1Order.GetHashCode().ToString() : "NULL",
					tp1Order != null ? tp1Order.Name : "N/A"));

				// CHECK TP1 -> Move SL to BE (Delegated to OrderProtectionManager v1.14.40)
				bool isTP1 = (tp1Order != null && execution.Order == tp1Order);
				if (!isTP1 && execution.Order.Name.StartsWith("TP1_")) isTP1 = true; // Fallback by Name
				// v1.14.88: Check Scaled Orders List
				if (!isTP1 && tp1Orders != null)
                {
                    lock (scaledOrdersLock) { if (tp1Orders.Contains(execution.Order)) isTP1 = true; }
                }

				if (isTP1)
				{
					// v1.14.31: Capture Delta at TP1 for VWAP absorption analysis
					if (relativeDelta != null && CurrentBar > 0)
					{
						try
						{
							tradeDeltaAtTP1 = relativeDelta.DeltaClose[0];
							Log(Time[0] + " DELTA CAPTURE: TP1 Delta=" + tradeDeltaAtTP1);
						}
						catch { tradeDeltaAtTP1 = 0; }
					}
					
					// v1.14.40: Delegate BE handling to OrderProtectionManager
					if (protectionManager != null)
						protectionManager.HandleTP1Fill(quantity);
				}

				// CHECK TP2 -> SL should already be at BE (Delegated v1.14.40)
				bool isTP2 = (tp2Order != null && execution.Order == tp2Order);
				if (!isTP2 && execution.Order.Name.StartsWith("TP2_")) isTP2 = true;
				// v1.14.88: Check Scaled Orders List
				if (!isTP2 && tp2Orders != null)
                {
                    lock (scaledOrdersLock) { if (tp2Orders.Contains(execution.Order)) isTP2 = true; }
                }

				if (isTP2)
				{
					// v1.14.40: Delegate to OrderProtectionManager
					if (protectionManager != null)
						protectionManager.HandleTP2Fill();
				}
			}

			
			// Reset if Closed (Filled) OR Cancelled/Rejected
			// CHECK ENTRY
			bool resetNeeded = false;
			if (entryOrder != null && execution.Order == entryOrder && (execution.Order.OrderState == OrderState.Cancelled || execution.Order.OrderState == OrderState.Rejected)) resetNeeded = true;

			if (resetNeeded)
			{
				Log(Time + " Entry Order Cancelled/Rejected. Resetting to Idle.");
                
                // v1.15.0: Log Readability Improvement - Trade Footer (Cancelled)
                Log("==========================================================================================");
                Log(string.Format("   TRADE CLOSED (CANCELLED): #{0} | {1} | State: {2}", tradeExportId, Time[0], currentEntryState));
                Log("==========================================================================================");

				currentEntryState = EntryState.Idle;
				setupLevelName = "";
				
				// CLEAR ALL
				entryOrder = null;
				targetOrder = null;
				tp1Order = null;
				tp2Order = null; 
				// v1.14.88: Clear Scaled Lists
				if (tp1Orders != null || tp2Orders != null)
                {
                    lock (scaledOrdersLock)
                    {
                        if (tp1Orders != null) tp1Orders.Clear();
                        if (tp2Orders != null) tp2Orders.Clear();
                    }
                }
				
				stopOrder = null;
				
				// Clear Cache
				cachedOppositeLevel = null;
				oppositeSearchDone = false; // v1.14.32
				
				failsafeTriggered = false; // v1.14.2: Reset failsafe lock
			}
			
			// CRITICAL FIX: Only reset if we are truly FLAT. include "Exit on session close"
			// Also checking if it is an Unmanaged Exit order (SL/TP) OR the System Session Close
			// v1.13.13 FIX: TP orders are named TP1_ and TP2_, not TP_ - was causing TPs to not export to CSV!
			bool isExitOrder = (execution.Order.Name.Contains("SL_") || execution.Order.Name.Contains("TP1_") || execution.Order.Name.Contains("TP2_") || execution.Order.Name == "Exit on session close" || execution.Order.Name.StartsWith("Exit_"));
			
			// v1.14.91 FIX: Include PartFilled. 
			// Previously only checks 'Filled', so if an order fills in chunks (e.g. 4 then 12), the first 4 (PartFilled) were IGNORED and lost.
			// execution.Quantity is specific to the chunk, so we must record all chunks.
			if ((execution.Order.OrderState == OrderState.Filled || execution.Order.OrderState == OrderState.PartFilled) && isExitOrder)
			{
			// v1.13.3: Export CSV on EACH exit fill (not only when flat)
				// v1.14.24: Only export in Realtime mode to avoid historical data pollution
				// v1.14.30: Allow CSV export in backtest mode when AllowBacktest is enabled
				// v1.14.32: Auto-detect backtest (ChartControl == null means Strategy Analyzer)
				bool isStrategyAnalyzer = (ChartControl == null);
				bool isRealtime = (State == State.Realtime);
				// FIX v1.14.48: STRICT check. Only export if Realtime, OR if explicit Backtest in Analyzer.
				// Prevents historical chart data from polluting Demo/Live folders on startup.
				if (isTrackingTrade && !string.IsNullOrEmpty(csvExportPath) && (isRealtime || (isStrategyAnalyzer && AllowBacktest)))
				{
					try
					{
						// v1.14.93 FIX: Use specific execution price, not order average.
						// AverageFillPrice shifts as order fills, causing PnL drift vs NT.
						double exitPrice = execution.Price;
						
						// Calculate PnL based on direction
						double pnl = 0;
						if (tradeDirection == "Long")
							pnl = (exitPrice - tradeEntryPrice) * execution.Quantity * Instrument.MasterInstrument.PointValue;
						else
							pnl = (tradeEntryPrice - exitPrice) * execution.Quantity * Instrument.MasterInstrument.PointValue;
						
						string resultName = execution.Order.Name; // "TP1_Long", "SL_Short", etc.
						
						// v1.13.4: Increment fill counter and generate sub-ID
						tradeExitFillsCount++;
						
						// Use fill counter for unique IDs: 1.1, 1.2, etc (handles TP1, TP2, and multiple 'Exit on session close')
						// v1.15.38: Use Date-Prefixed ID (yyyyMMdd_ID) to prevent collisions in cumulative backtests
						string baseTradeId = tradeEntryTime.ToString("yyyyMMdd") + "_" + tradeExportId;
						string tradeId;
						
						if (tradeExitFillsCount == 1 && execution.Quantity >= 2)
							tradeId = baseTradeId; // First fill of whole position
						else
							tradeId = baseTradeId + "." + tradeExitFillsCount; // Partial fill: 20250105_1.1
						
						// Format CSV line - Ensure all values are valid
						string safeSetupName = string.IsNullOrEmpty(tradeSetupName) ? "" : tradeSetupName.Replace(",", ";");
						
						// v1.13.11: Added Attempt column for retry analysis
						// v1.13.12: Added RiskReward column for R:R distribution chart
						// v1.13.16: Added Commission calculation (NinjaTrader Free Plan rates)
						double riskReward = (tradeRiskUSD > 0) ? (pnl / tradeRiskUSD) : 0;
						
						// Calculate commission based on instrument (2 sides per trade)
						// NinjaTrader All-In Rates (User Verified 2026-01-11)
						// Logic splits Micros vs Standard, then by Asset Class
						
						string instName = Instrument.MasterInstrument.Name.ToUpper();
						double commissionPerSide = 0; // Initialize
						
						// 1. MICROS
						if (instName.StartsWith("M")) // Removed MY exclusion to allow MYM
						// Actually MYM is Micro YM. Logic:
						{
						    // Handle specific exceptions where "M" is start but not Micro? No, standard convention is M=Micro.
						    // Exceptions: MBT (Micro Bitcoin), MET (Micro Ether).
						    // Asset Classes:
						    
						    if (instName.Contains("MBT") || instName.Contains("MET")) 
						        commissionPerSide = 1.60; // Micro Crypto
						    else if (instName.StartsWith("MNQ") || instName.StartsWith("M2K"))
						        commissionPerSide = 0.95; // MNQ & M2K ($1.90 RT - Adjusted based on User PnL)
						    else if (instName.StartsWith("MES") || instName.StartsWith("MYM"))
						        commissionPerSide = 0.90; // MES & MYM ($1.80 RT - Adjusted based on User PnL)
						    else if (instName.StartsWith("MCL") || instName.StartsWith("QM"))
						        commissionPerSide = 1.10; // Micro Oil/Energy
						    else if (instName.StartsWith("MGC") || instName.StartsWith("SIL") || instName.StartsWith("MHG"))
						        commissionPerSide = 1.20; // Micro Metals (Gold, Silver, Copper)
						    else if (instName.StartsWith("M6"))
						        commissionPerSide = 1.20; // Micro Currencies (M6E, M6A, etc) - Estimate based on Gold
						    else
						        commissionPerSide = 1.20; // Default Micro Fallback
						}
						// 2. CRYPTO (Standard)
						else if (instName.StartsWith("BTC") || instName.StartsWith("ETH"))
						{
						    commissionPerSide = 6.00; 
						}
						// 3. STANDARD / FULL SIZE
						else if (instName.StartsWith("ES") || instName.StartsWith("NQ") || instName.StartsWith("YM") || instName.StartsWith("RTY"))
						{
						    commissionPerSide = 2.29; // Standard Indices (Keeping existing logic)
						}
						else if (instName.StartsWith("CL") || instName.StartsWith("NG") || instName.StartsWith("GC") || instName.StartsWith("SI") || instName.StartsWith("HG"))
						{
						    commissionPerSide = 2.40; // Standard Commodities (Estimate)
						}
						else if (instName.StartsWith("6"))
						{
                            commissionPerSide = 2.50; // Standard Currencies
						}
						else
						{
						    commissionPerSide = 2.50; // Generic Default
						}
						
						// Removed redundant MYM override
						
						double commission = execution.Quantity * 2 * commissionPerSide; // 2 sides (entry + exit)
						double netPnl = pnl - commission;
						
						// v1.14.96: Calculate Level Age (Days between Level Creation and Trade Entry)
						int levelAgeDays = 0;
						if (setupLevelTime != DateTime.MinValue)
							levelAgeDays = (tradeEntryTime.Date - setupLevelTime.Date).Days;

						// v1.15.33: Added Quantity (column 22) to match NT Trade Performance exactly
						// v1.15.38: Added ExecutionId (Column 23)
                        // v1.15.43: Added EntryMode, ExitStrategy, RiskModel (Columns 24-26)
                        string riskModelStr = UseDynamicSizing ? "Dynamic" : "Fixed";
                        if (UseDynamicSizing && UseATRScaling) riskModelStr += "_ATR";
                        
						string line = string.Format("{0},{1},{2:yyyy-MM-dd HH:mm:ss},{3},{4},{5:yyyy-MM-dd HH:mm:ss},{6},{7},{8:F2},{9:F2},{10:F2},{11:F2},{12:F2},{13},{14},{15:F2},{16:F0},{17},{18:F0},{19:F0},{20},{21},{22},{23},{24},{25}",
							tradeId,
							Instrument.FullName,
							tradeEntryTime,
							tradeDirection,
							tradeEntryPrice,
							time,
							exitPrice,
							resultName,
							pnl,
							commission,
							netPnl,
							tradeMAE,
							tradeMFE,
							safeSetupName,
							tradeAttemptNumber,
							riskReward,
							tradeDeltaAtEntry,      // v1.14.31
							tradeDeltaDirection,    // v1.14.31
							tradeSessionDelta,      // v1.14.31
							tradeDeltaAtTP1,        // v1.14.31
							levelAgeDays,           // v1.14.96: Level Age
							execution.Quantity,     // v1.15.33: Quantity
							execution.ExecutionId,   // v1.15.38: ExecutionId
                            SelectedEntryMode.ToString(), // {23}
                            TargetDistribution.ToString(), // {24}
                            riskModelStr            // {25}
						);
						
						System.IO.File.AppendAllText(csvExportPath, line + Environment.NewLine);
						Log(Time + " CSV EXPORT: Trade #" + tradeId + " closed - " + resultName + " PnL=" + pnl.ToString("F2"));
					}
					catch (Exception ex)
					{
						Log(Time + " CSV EXPORT ERROR: " + ex.Message);
					}
				}
				
				// Only stop tracking when position is FULLY closed
				if (Position.MarketPosition == MarketPosition.Flat)
				{
					bool isSLClose = execution.Order.Name.Contains("SL_");
					Log(Time + " Position Closed (" + execution.Order.Name + "). Resetting to Idle.");
					lastPositionCloseTime = DateTime.Now; // v1.11.19: Prevent orphan false positives
					TriggerScreenshot("Exit_" + execution.Order.Name, DateTime.Now, executionId);
					
					// v1.15.38: Send exit email with PnL details
					{
						double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
						double grossPnL = 0;
						double commission = 0;
						string exitReason = isSLClose ? "Stop Loss" : (execution.Order.Name.Contains("TP1") ? "TP1" : (execution.Order.Name.Contains("TP2") ? "TP2" : "Manual/Other"));
						
						// Calculate PnL from entry/exit prices
						if (tradeEntryPrice > 0)
						{
							double priceDiff = tradeDirection == "Long" ? (price - tradeEntryPrice) : (tradeEntryPrice - price);
							grossPnL = priceDiff / TickSize * tickValue * tradeOriginalQty;
							// Estimate commission (use common rates)
							commission = tradeOriginalQty * 2 * 1.20; // $1.20 per side per contract (MicroCom)
						}
						SendTradeExitEmail(grossPnL, commission, exitReason);
					}
					
					isTrackingTrade = false;
					
					// v1.10.26: Check if we can retry
					bool canRetry = false;
					SessionLevel currentLevel = null;
					if (isSLClose && !string.IsNullOrEmpty(setupLevelName))
					{
						currentLevel = activeLevels.FirstOrDefault(l => l.Name == setupLevelName);
						if (currentLevel != null && currentLevel.EntryAttempts < MaxRetriesPerLevel)
						{
							canRetry = true;
						}
					}
					
					if (canRetry && currentLevel != null)
					{
						// Enter VWAP Mitigation Wait state
						currentEntryState = EntryState.WaitingForVwapMitigation;
						waitingForVwapMitigation = true;
						
						// Save the extreme to mitigate (Low for LONG, High for SHORT)
						vwapCandleExtreme = isShortSetup ? setupAnchorPrice : setupAnchorPrice;
						currentVwapNumber++;
						
						Log(string.Format("{0} VWAP RETRY: Waiting for price to break {1:F2} for VWAP#{2}",
							Time, vwapCandleExtreme, currentVwapNumber));
						visualConfirmationDone = false; // v1.11.25: Reset to allow confirmation candle highlight on retries
					}
					else
					{
						// Normal reset to Idle
						currentEntryState = EntryState.Idle;
						setupLevelName = "";
						waitingForVwapMitigation = false;
						currentVwapNumber = 1;
						vwapCandleExtreme = 0;
					}
					
					// CLEANUP: Force Cancel any remaining working orders to prevent "Zombie Orders" on Chart
				// v1.10.17: Also cancel stopOrder (Single-SL architecture v1.9.0+)
				// v1.13.7: Check both Working AND Accepted states
				if (stopOrder != null && (stopOrder.OrderState == OrderState.Working || stopOrder.OrderState == OrderState.Accepted)) 
				{
					try { CancelOrder(stopOrder); Log("CLEANUP: Cancelled orphan stopOrder"); } catch {}
				}
				if (entryOrder != null && (entryOrder.OrderState == OrderState.Working || entryOrder.OrderState == OrderState.Accepted)) 
				{
					try { CancelOrder(entryOrder); Log("CLEANUP: Cancelled orphan entryOrder"); } catch {}
				}
				if (stopOrder1 != null && (stopOrder1.OrderState == OrderState.Working || stopOrder1.OrderState == OrderState.Accepted)) 
				{
					try { CancelOrder(stopOrder1); } catch {}
				}
				if (stopOrder2 != null && (stopOrder2.OrderState == OrderState.Working || stopOrder2.OrderState == OrderState.Accepted)) 
				{
					try { CancelOrder(stopOrder2); } catch {}
				}
				if (tp1Order != null && (tp1Order.OrderState == OrderState.Working || tp1Order.OrderState == OrderState.Accepted)) 
				{
					try { CancelOrder(tp1Order); Log("CLEANUP: Cancelled orphan tp1Order"); } catch {}
				}
				if (tp2Order != null && (tp2Order.OrderState == OrderState.Working || tp2Order.OrderState == OrderState.Accepted)) 
				{
					try { CancelOrder(tp2Order); Log("CLEANUP: Cancelled orphan tp2Order"); } catch {}
				}
				
				// v1.14.33: AGGRESSIVE CLEANUP - Cancel ALL TP/SL orders for this instrument in Account.Orders
				// This catches any orders that lost their reference (e.g., due to restart)
				if (Account != null)
				{
					foreach(Order o in Account.Orders)
					{
						if (o.Instrument.FullName == Instrument.FullName && 
							(o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted))
						{
							if (o.Name.StartsWith("TP1_") || o.Name.StartsWith("TP2_") || o.Name.StartsWith("SL_") || o.Name.StartsWith("LadderTP_"))
							{
								try 
								{ 
									CancelOrder(o); 
									Log("CLEANUP (Aggressive): Cancelled " + o.Name + " Qty=" + o.Quantity); 
								} 
								catch {}
							}
						}
					}
				}

				// RESET PROTECTION COUNTERS (v1.7.24) - Fix bucket allocation
				protectedTp1Qty = 0;
				protectedTp2Qty = 0;
				protectionOrdersCreated = false; // v1.11.14: Reset flag for next trade
				isProtectionProcessing = false; // v1.13.1: Reset lock
				tradeOriginalQty = 0; // v1.11.23: Reset original trade qty
				tradeOriginalTp1Price = 0; // v1.11.24: Reset original TP prices
				tradeOriginalTp2Price = 0;
				tradeVwapActive = false; // v1.10.31: Reset Trade VWAP
				IsTradeVwapExtended = false; // v1.14.74: Reset extension flag

					// CLEARED
				entryOrder = null;
				tp1Order = null;
				tp2Order = null; 
				stopOrder = null; // v1.13.7: CRITICAL FIX - Clear main SL reference
				stopOrder1 = null;
				stopOrder2 = null;
				slOrderCreatedThisEntry = false; // v1.13.7: Also reset SL creation flag here
					
					// FIXED (v1.7.3): Clear Cache on Successful Exit too!
					cachedOppositeLevel = null;
					oppositeSearchDone = false; // v1.14.32
					validatedTp1Price = 0; // v1.15.26: Split from validatedTargetPrice
					validatedTp2Price = 0; // v1.15.26: Split from validatedTargetPrice
				}
				else
				{
					Log(Time + " Partial Execution (" + execution.Order.Name + "). Position Active. Qty=" + Position.Quantity);

						Log(Time + " Partial Execution (" + execution.Order.Name + "). Position Active. Qty=" + Position.Quantity);
				}
			}
			}
			catch (Exception ex)
			{
				NinjaTrader.Code.Output.Process("CRITICAL ERROR in OnExecutionUpdate: " + ex.ToString(), PrintTo.OutputTab1);
				Log("CRITICAL ERROR in OnExecutionUpdate: " + ex.ToString());
			}
		}

		[NinjaScriptProperty]
		[Display(Name="Asia Start Time", Order=1, GroupName="1. Sessions")]
		public string AsiaStartTime { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Asia End Time", Order=2, GroupName="1. Sessions")]
		public string AsiaEndTime { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Europe Start Time", Order=3, GroupName="1. Sessions")]
		public string EuropeStartTime { get; set; }
		
		// ===== ENTRY MODE SELECTION (v1.14.73) =====
		
		[NinjaScriptProperty]
		[Display(Name="Entry Mode", Description="A+ Retrace waits for VWAP pullback. Anticipado enters on confirmation candle.", Order=0, GroupName="Order Management")]
		public EntryMode SelectedEntryMode
		{ get; set; } = EntryMode.APlusRetrace;
		
		[NinjaScriptProperty]
		[Display(Name="Anticipated Order Type", Description="Only used when Entry Mode is Anticipado", Order=0, GroupName="Order Management")]
		public AnticipatedOrderType AnticipatedType
		{ get; set; } = AnticipatedOrderType.Market;
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Quantity", Order=1, GroupName="Order Management")]
		public int Quantity
		{ get; set; } = 1;

		[NinjaScriptProperty]
		[Display(Name="Move to Breakeven", Order=1, GroupName="Order Management")]
		public bool EnableBreakeven
		{ get; set; } = true;

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Stop Loss (Ticks)", Order=2, GroupName="Order Management")]
		public int StopLossTicks
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="Min Risk/Reward Ratio", Order=3, GroupName="Order Management")]
		public double MinRiskRewardRatio
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name="Max Retries Per Level", Description="Maximum entry attempts per level before giving up", Order=3, GroupName="Order Management")]
		public int MaxRetriesPerLevel
		{ get; set; } = 1;
		
		// ===== DYNAMIC POSITION SIZING (v1.8.0) =====
		
		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name="Risk Per Trade (USD)", Order=4, GroupName="Order Management", Description="Fixed risk amount (only used in Apteros model, ignored in Standard)")]
		public double RiskPerTradeUSD
		{ get; set; } = 50.0;

		[NinjaScriptProperty]
		[Range(0.001, 100.0)]
		[Display(Name="Risk Percentage (%)", Order=41, GroupName="Order Management", Description="Percentage of account to risk per trade in Standard model (0.06 = 0.06%)")]
		public double RiskPercentage
		{ get; set; } = 0.06;

		[NinjaScriptProperty]
		[Range(1000, double.MaxValue)]
		[Display(Name="Starting Capital (USD)", Order=42, GroupName="Order Management", Description="Reference capital for risk calculation (actual account value used in real-time)")]
		public double StartingCapital
		{ get; set; } = 250000.0;

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Min Quantity", Order=5, GroupName="Order Management")]
		public int MinQuantity
		{ get; set; } = 1;
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Max Quantity", Order=6, GroupName="Order Management")]
		public int MaxQuantity
		{ get; set; } = 10;
		
		[NinjaScriptProperty]
		[Display(Name="Use Dynamic Sizing", Order=7, GroupName="Order Management")]
		public bool UseDynamicSizing
		{ get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name="Use ATR Scaling", Description="Limit risk based on ATR volatility", Order=7, GroupName="Order Management")]
		public bool UseATRScaling
		{ get; set; } = true;
		
		[NinjaScriptProperty]
		[Range(0.1, 10.0)]
		[Display(Name="ATR Risk Scale Factor", Description="Multiplier to convert ATR to risk $. Higher = more risk in volatile markets. (e.g. 2.0 means Risk$ = ATR × 2)", Order=8, GroupName="Order Management")]
		public double ATRRiskScaleFactor
		{ get; set; } = 2.0;

		// ===== TARGET DISTRIBUTION (v1.14.88) =====
		public enum TargetDistributionMode
		{
			Standard, // 50/50 Split (TP1 VWAP, TP2 Level)
			Scaled    // Hybrid Scaled Distribution
		}
		
		[NinjaScriptProperty]
		[Display(Name="Target Distribution", Description="Standard=Fixed Targets, Scaled=R-Based Ladder", Order=5, GroupName="Order Management")]
		public TargetDistributionMode TargetDistribution { get; set; } = TargetDistributionMode.Standard;
		
		// List support for Scaled Targets
		[XmlIgnore] public List<Order> tp1Orders = new List<Order>();
		[XmlIgnore] public List<Order> tp2Orders = new List<Order>();
		

		


		[NinjaScriptProperty]
		[Display(Name="Daily Loss % Limit", Description="Daily Loss Limit as % of previous EOD Balance (Default 2.5%)", Order=1, GroupName="Apteros Risk Module")]
		public double ApterosDailyLossPercent
		{ get; set; } = 2.5;
		
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name="Daily Opportunities", Description="Divisor for Daily Limit to calculate Risk Per Trade (e.g. Limit/10)", Order=2, GroupName="Apteros Risk Module")]
		public int ApterosDailyOpportunities
		{ get; set; } = 10;
		
		[NinjaScriptProperty]
		[Display(Name="Max Trailing Drawdown", Description="Max Trailing Drawdown from High Water Mark (e.g. $5000)", Order=3, GroupName="Apteros Risk Module")]
		public double ApterosMaxTrailingDrawdown
		{ get; set; } = 5000.0;
		
		[NinjaScriptProperty]
		[Display(Name="Risk Calculation Basis", Description="Choose between % of Daily Balance or Drawdown Allocation", Order=4, GroupName="Apteros Risk Module")]
		public ApterosRiskBasis RiskCalculationBasis
		{ get; set; } = ApterosRiskBasis.PercentageOfBalance;
		
		[NinjaScriptProperty]
		[Range(1, 365)]
		[Display(Name="Allocation Days", Description="Days to allocate the Max Drawdown over (e.g. 20 days)", Order=5, GroupName="Apteros Risk Module")]
		public int ApterosAllocationDays
		{ get; set; } = 20;

        // ===== AI AUTO-CONFIGURATION (v1.15.43) =====
        [NinjaScriptProperty]
        [Display(Name = "Auto Load AI Config", Description = "Automatically load settings from AI generated config file", Order = 1, GroupName = "AI Integrations")]
        public bool AutoLoadAIConfig
        { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "AI Config Path", Description = "Path to the ai_config.json file", Order = 2, GroupName = "AI Integrations")]
        public string AIConfigPath
        { get; set; } = @"C:\Users\prueba\Documents\NinjaTrader 8\bin\Custom\Strategies\StreamlitAudit\ai_config.json";

        [NinjaScriptProperty]
        [Range(0, 3650)]
        [Display(Name = "Max Level Age (Days)", Description = "Maximum age in days for a level to be traded (0 = Unlimited)", Order = 3, GroupName = "AI Integrations")]
        public int MaxLevelAgeDays
        { get; set; } = 0;

        // v2.12: Expanded AI Config Fields
        public int MinAttemptStart { get; set; } = 1;
        
        [XmlIgnore]
        public List<string> AllowedDirections { get; set; } = new List<string> { "Long", "Short" };

	// ===== AI FILTERS (v1.15.43 - Consolidated) =====
	[NinjaScriptProperty]
	[Display(Name="Enabled Zones (CSV)", 
	         Description="Lista de zonas habilitadas separadas por coma. Vacío = todas habilitadas. Ej: 'Asia High, USA Low'. (Se llena auto si AutoLoadAI está activo)", 
	         GroupName="2. AI Filters", 
	         Order=1)]
	public string EnabledZonesParam { get; set; }
	
	private List<string> enabledZonesList = new List<string>();
	
	private void ParseEnabledZones()
	{
		enabledZonesList = new List<string>();
		if (string.IsNullOrWhiteSpace(EnabledZonesParam)) return;
		
		string[] zones = EnabledZonesParam.Split(new char[] { ',', '\"', '[', ']', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string z in zones)
		{
			if (!string.IsNullOrWhiteSpace(z))
				enabledZonesList.Add(z.Trim());
		}
		Log("AI CONFIG: Activas " + enabledZonesList.Count + " zonas: " + string.Join(", ", enabledZonesList));
	}
	
	// Helper to check if a zone is enabled (Simple)
	public bool IsZoneEnabled(string zoneName)
	{
		if (enabledZonesList == null || enabledZonesList.Count == 0) return true; // Default: Enable All
		foreach(string zone in enabledZonesList)
		{
			 if (zoneName.Contains(zone)) return true;
		}
		return false;
	}
	
	// Helper to check if a zone is enabled (With Age, Attempt, and Direction Check)
	public bool IsZoneEnabled(string zoneName, DateTime levelTime)
	{
		// 1. Zone Name Check
		if (!IsZoneEnabled(zoneName)) return false;
		
		// 2. Age Check
		if (MaxLevelAgeDays > 0)
		{
			TimeSpan age = DateTime.Now - levelTime;
			if (age.TotalDays > MaxLevelAgeDays) return false;
		}
		
		// v2.12: Advanced Checks (Attempt & Direction)
		// We need to look up the level object to know its state (Resistance/Attempts)
		// This avoids modifying EntryStateMachine.cs signature
		var lvl = activeLevels.FirstOrDefault(l => l.Name == zoneName && l.StartTime == levelTime);
		if (lvl != null)
		{
			// 3. Direction Check
			bool isShort = lvl.IsResistance;
			string requiredDir = isShort ? "Short" : "Long";
			
			// AllowedDirections might be null if not initialized, default to allow
			if (AllowedDirections != null && AllowedDirections.Count > 0)
			{
				bool dirAllowed = false;
				foreach(var dir in AllowedDirections)
				{
					if (string.Equals(dir, requiredDir, StringComparison.OrdinalIgnoreCase))
					{
						dirAllowed = true;
						break;
					}
				}
				if (!dirAllowed) return false;
			}
			
			// 4. Attempt Check (Min Start)
			// lvl.EntryAttempts is how many have been DONE.
			// Current attempt will be lvl.EntryAttempts + 1
			if ((lvl.EntryAttempts + 1) < MinAttemptStart) return false;
		}
		
		return true;
	}

	private void LoadAIConfig()
	{
		if (!AutoLoadAIConfig || string.IsNullOrEmpty(AIConfigPath) || !System.IO.File.Exists(AIConfigPath))
			return;

		try
		{
			string json = System.IO.File.ReadAllText(AIConfigPath);
			Log("AI CONFIG: Leyendo " + AIConfigPath);

			// Parse Max Age
			if (json.Contains("\"max_age\":"))
			{
				string agePart = json.Substring(json.IndexOf("\"max_age\":") + 10);
				agePart = agePart.Substring(0, agePart.IndexOfAny(new char[] { ',', '}' }));
				int age = 0;
				if (int.TryParse(agePart.Trim(), out age))
				{
					MaxLevelAgeDays = age;
					Log("AI CONFIG: Loaded MaxLevelAgeDays = " + MaxLevelAgeDays);
				}
			}

			// Parse Max Retries
			if (json.Contains("\"max_retries\":"))
			{
				string retryPart = json.Substring(json.IndexOf("\"max_retries\":") + 14);
				retryPart = retryPart.Substring(0, retryPart.IndexOfAny(new char[] { ',', '}' }));
				int retries = 1;
				if (int.TryParse(retryPart.Trim(), out retries))
				{
					MaxRetriesPerLevel = retries;
					Log("AI CONFIG: Loaded MaxRetriesPerLevel = " + MaxRetriesPerLevel);
				}
			}

            // v2.12: Parse Min Attempt
			if (json.Contains("\"min_attempt\":"))
			{
				string minPart = json.Substring(json.IndexOf("\"min_attempt\":") + 14);
				minPart = minPart.Substring(0, minPart.IndexOfAny(new char[] { ',', '}' }));
				int minAtt = 1;
				if (int.TryParse(minPart.Trim(), out minAtt))
				{
					MinAttemptStart = minAtt;
					Log("AI CONFIG: Loaded MinAttemptStart = " + MinAttemptStart);
				}
			}
            
            // v2.12: Parse Allowed Directions
             if (json.Contains("\"allowed_directions\":"))
            {
                int start = json.IndexOf("\"allowed_directions\":") + 21;
                int end = json.IndexOf("]", start);
                if (start > 0 && end > start)
                {
                    string listContent = json.Substring(start, end - start);
                    string[] dirs = listContent.Split(new char[] { ',', '\"', '[', ']', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
					
                    AllowedDirections = new List<string>();
                    foreach (string d in dirs) if (!string.IsNullOrWhiteSpace(d)) AllowedDirections.Add(d.Trim());
					
                    Log("AI CONFIG: AllowedDirections = " + string.Join(", ", AllowedDirections));
                }
            }

            // Parse Enabled Zones -> Set to Property -> Parse to List
             if (json.Contains("\"enabled_zones\":"))
            {
                int start = json.IndexOf("\"enabled_zones\":") + 16;
                int end = json.IndexOf("]", start);
                if (start > 0 && end > start)
                {
                    string listContent = json.Substring(start, end - start);
                    string[] zones = listContent.Split(new char[] { ',', '\"', '[', ']', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
					
					// Reconstruct into clean CSV string for UI Property
                    List<string> cleanList = new List<string>();
                    foreach (string z in zones) if (!string.IsNullOrWhiteSpace(z)) cleanList.Add(z.Trim());
					
					EnabledZonesParam = string.Join(", ", cleanList);
                }
            }
			
			// Refresh Internal List
			ParseEnabledZones();
		}
		catch (Exception ex)
		{
			Log("AI CONFIG ERROR: " + ex.Message);
		}
	}
		[XmlIgnore]
		public RiskManager riskManager;
		
		// Internal Targets State
		[XmlIgnore] public double activeTp1Price = 0;
		[XmlIgnore] public double activeTp2Price = 0;
		
		// v1.14.88: Original Order Prices for Info Panel Display
		[XmlIgnore] public double tradeOriginalSlPrice = 0;
		// v1.14.89: Thread Safety Lock for List Access (UI vs Strategy Thread)
		[XmlIgnore] public object scaledOrdersLock = new object();

		
		[NinjaScriptProperty]
		[Display(Name="Europe End Time", Order=4, GroupName="1. Sessions")]
		public string EuropeEndTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name="USA Start Time", Order=5, GroupName="1. Sessions")]
		public string USAStartTime { get; set; }
		// USAEndTime moved to public section (line 177)
		

	// ===== AI FILTERS (Legacy Block Removed - Replaced by New Auto Config Logic) =====



		// v1.15.38: Anti-duplication flags for email alerts
		private bool emailSentOnEntry = false;
		private bool emailSentOnExit = false;

		// Fix: InitCSV to write Header
		private void InitCSV()
		{
			try
			{
				if (!string.IsNullOrEmpty(csvExportPath))
				{
					// Ensure directory exists
					string dir = System.IO.Path.GetDirectoryName(csvExportPath);
					if (!System.IO.Directory.Exists(dir))
						System.IO.Directory.CreateDirectory(dir);
						
					// If file doesn't exist, write header
					if (!System.IO.File.Exists(csvExportPath))
					{
						// v1.14.90: Header matching 20 columns (including Delta)
						// v1.14.96: Added LevelAge (Column 21)
						// v1.15.33: Added Quantity (Column 22) for accurate PnL calculation matching NT Trade Performance
						// v1.15.38: Added ExecutionId (Column 23) for robust deduplication
                        // v1.15.43: Added EntryMode, ExitStrategy, RiskModel (Columns 24-26) to sync with Streamlit App
						string header = "TradeId,Instrument,EntryTime,Type,EntryPrice,ExitTime,ExitPrice,Result,GrossPnL,Commission,NetPnL,MAE,MFE,SetupName,Attempt,RiskReward,DeltaEntry,DeltaDir,SessionDelta,DeltaTP1,LevelAge,Quantity,ExecutionId,EntryMode,ExitStrategy,RiskModel";
						System.IO.File.WriteAllText(csvExportPath, header + Environment.NewLine);
						Log("CSV INIT: Created new export file with header at " + csvExportPath);
					}
				}
			}
			catch (Exception ex)
			{
				Log("CSV INIT ERROR: " + ex.Message);
			}
		}

		// UNMANAGED HELPER: Close Position Market


		private void ClosePositionUnmanaged(string reason)
		{
			if (Position.MarketPosition == MarketPosition.Long)
			{
				// v1.15.19: Use Bid as limit for slippage protection on Sell market orders
				double bidPrice = GetCurrentBid();
				double limitPrice = Instrument.MasterInstrument.RoundToTickSize(bidPrice - (2 * TickSize)); // 2 ticks buffer for slippage

				Log(string.Format("{0} UNMANAGED EXIT: Closing Long. Reason: {1} | Bid={2} Limit={3}",
					Time, reason, bidPrice, limitPrice));

				SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.Market, Position.Quantity, limitPrice, 0, "", "Exit_Long_Market");
			}
			else if (Position.MarketPosition == MarketPosition.Short)
			{
				// v1.15.19: Use Ask as limit for slippage protection on BuyToCover market orders
				double askPrice = GetCurrentAsk();
				double limitPrice = Instrument.MasterInstrument.RoundToTickSize(askPrice + (2 * TickSize)); // 2 ticks buffer for slippage

				Log(string.Format("{0} UNMANAGED EXIT: Closing Short. Reason: {1} | Ask={2} Limit={3}",
					Time, reason, askPrice, limitPrice));

				SubmitOrderUnmanaged(0, OrderAction.BuyToCover, OrderType.Market, Position.Quantity, limitPrice, 0, "", "Exit_Short_Market");
			}

			// v1.15.38: Send critical alert for emergency close
			SendCriticalAlert("EMERGENCY CLOSE", reason);

			// Cancel any working entry orders to be safe
			if (entryOrder != null && entryOrder.OrderState == OrderState.Working) CancelOrder(entryOrder);
		}

	// (AI Filter Helpers refactored to top of file)

		// v1.14.78: Level Persistence
	[XmlIgnore] public SessionLevelPersistence levelPersistence;
	private int lastLevelCount = 0;

	// DIAGNOSTIC DUMP
	public void DumpActiveLevels(string context)
	{
		if (activeLevels == null) return;
		Log($"---- DUMP LEVELS ({context}) ----");
		Log($"Total Levels: {activeLevels.Count}");
		foreach (var lvl in activeLevels)
		{
			string startTimeStr = lvl.StartTime.ToString("MM/dd HH:mm");
			Log($"LVL: Name='{lvl.Name}' Price={lvl.Price:F2} Start={startTimeStr} Tag='{lvl.Tag}'");
		}
		Log("--------------------------------");
	}




	} // End of SessionLevelsStrategy_2026_01_18_16 class


} // End of Namespace
