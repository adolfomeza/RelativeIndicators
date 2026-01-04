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
using System.Net;
using System.Net.Mail;
using System.IO;
using System.Windows.Controls; // v1.12.0: For control buttons
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	// v1.12.0: Trading Mode Control
	public enum TradingMode { Normal, Paused, LongOnly, ShortOnly }
	
	public class SessionLevelsStrategy_2026_01_02_82 : Strategy
	{
		private const string StrategyVersion = "v1.14.13"; // BACKTEST DETERMINISM FIX
		
		// v1.12.1: CONTROL BUTTONS (simplified to 2 buttons)
		private TradingMode currentTradingMode = TradingMode.Normal;
		private System.Windows.Controls.Button btnPause; // Direction button (cycles through modes)
		private System.Windows.Controls.Button btnClose;
		private System.Windows.Controls.StackPanel buttonPanel;
		private bool buttonsInitialized = false;
		private bool isProtectionProcessing = false; // v1.13.1: Concurrency lock
		private bool failsafeTriggered = false; // v1.14.2: Prevent infinite loop in CheckHardStop
		
		// =========================================================
		// v1.13.0: TRADE ANALYZER EXPORT
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
		private double tradeRiskUSD = 0;       // v1.13.12: Original risk in USD for R:R calculation
		private string csvExportPath = "";
		private bool isTrackingTrade = false;  // Flag to track MAE/MFE
		private bool slOrderCreatedThisEntry = false; // v1.13.5: Prevent duplicate SL creation

		// Version Control
        // V_STACK: Stacking Logic Variables
        private double stackHighY = double.MinValue;
        private double stackLowY = double.MaxValue;
        private int lastColBarIdx = -1;
        private double verticalUnit = 0;
        private NinjaTrader.NinjaScript.Indicators.ATR atr;
		
		// v1.7.16: Persistence for EnsureProtection
		private double validatedTargetPrice = 0;

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


		public enum VwapCalculationMode
		{
			Typical, // (H+L+C)/3
			Close,   // Close
			OHLC4    // (O+H+L+C)/4
		}


		// ... existing properties ...

		// Optimize Performance: Cache TimeSpans
		private TimeSpan tsAsiaStart, tsAsiaEnd;
		private TimeSpan tsEuStart, tsEuEnd;
		private TimeSpan tsUsaStart, tsUsaEnd;
		private SessionIterator sessionIterator; // v1.14.7 fix
		
		// OPTIMIZATION (v1.7.3): Cache Opposite Level to avoid loops
		private SessionLevel cachedOppositeLevel = null;
		
		// v1.10.0: Internal Levels Management
		private bool isInternalLevel = false;
		private double externalLevelAbove = 0;  // For SHORT setups (external High above)
		private double externalLevelBelow = 0;  // For LONG setups (external Low below)
		private string externalLevelAboveName = "";
		private string externalLevelBelowName = "";
	private int lastInvalidationBar = -1;  // v1.10.1: Anti-loop for invalidation
	
		// v1.10.26: VWAP Retry Tracking
		private double vwapCandleExtreme = 0;           // Low (LONG) or High (SHORT) to mitigate
		private bool waitingForVwapMitigation = false;  // Are we waiting for price to break?
		private int currentVwapNumber = 1;              // Which VWAP# (1, 2, 3...)
		private int vwapTouchBar = -1;                  // Bar where VWAP was touched

		private bool enableDebugLogs = false; // Default false for performance

		[NinjaScriptProperty]
		[Display(Name="Enable Debug Logs", Description="Print detailed execution steps to Output. Disable for faster backtests.", Order=60, GroupName="General")]
		public bool EnableDebugLogs
		{
			get { return enableDebugLogs; }
			set { enableDebugLogs = value; }
		}
		
		// v1.11.17: Lag Filter - Maximum allowed chart lag before blocking orders
		[NinjaScriptProperty]
		[Range(0.1, 10)]
		[Display(Name="Max Chart Lag (Seconds)", Description="Block orders when chart data is older than this threshold. Set higher if experiencing false positives.", Order=62, GroupName="General")]
		public double MaxChartLagSeconds { get; set; } = 0.75;
		
		// v1.11.21: Strategy Analyzer Support - Enable backtest execution in Historical state
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
		// v1.11.5: TRIGGER LABEL SETTINGS
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
		// v1.11.9: CONFIRMATION CANDLE HIGHLIGHT
		// =========================================================
		private bool highlightConfirmationCandle = true;
		[NinjaScriptProperty]
		[Display(Name="Highlight Confirmation Candle", Description="Color the candle that confirms VWAP separation.", Order=80, GroupName="Trigger Labels")]
		public bool HighlightConfirmationCandle
		{
			get { return highlightConfirmationCandle; }
			set { highlightConfirmationCandle = value; }
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
		private double visualAdhocPrevBarVal = 0;
		private double visualAdhocLastVal = 0;
		private int visualAdhocLastBar = -1;

		// v1.11.13: File-based logging per instrument for easier debugging
		private static object logFileLock = new object();
		private string logFilePath = null;
		private DateTime lastLogFlush = DateTime.MinValue;
		
		// v1.11.17: Lag Filter - Chart data freshness detection
		private double currentChartLag = 0;
		private bool isLagAlertActive = false;
		
		// v1.11.19: Orphan false positive prevention - delay after position close
		private DateTime lastPositionCloseTime = DateTime.MinValue;
		
		private void Log(string message)
		{
			if (!EnableDebugLogs) return;
			
			string instrumentName = Instrument != null ? Instrument.MasterInstrument.Name : "UNKNOWN";
			string prefix = "[" + instrumentName + "] ";
			string fullMessage = prefix + message;
			
			// Print to Output window
			Print(fullMessage);
			
			// Write to file (buffered, low overhead)
			try
			{
				// v1.11.15: Only calculate path once per instance
				if (logFilePath == null)
				{
					// Use NinjaTrader's trace folder (always exists)
					string ntDocsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
					string logsDir = System.IO.Path.Combine(ntDocsPath, "NinjaTrader 8", "trace", "SessionLevels");
					if (!System.IO.Directory.Exists(logsDir))
						System.IO.Directory.CreateDirectory(logsDir);
					
					// One file per instrument per day
					string fileName = string.Format("{0}_{1:yyyyMMdd}.txt", instrumentName, DateTime.Now);
					logFilePath = System.IO.Path.Combine(logsDir, fileName);
				}
				
				lock (logFileLock)
				{
					System.IO.File.AppendAllText(logFilePath, 
						string.Format("{0:HH:mm:ss.fff} {1}\r\n", DateTime.Now, message));
				}
			}
			catch { } // Silently ignore file errors to not disrupt trading
		}
		
		// v1.11.13: Clear log file on strategy restart (overwrite instead of append)
		private void ClearLogFile()
		{
			try
			{
				if (Instrument == null) return;
				
				string instrumentName = Instrument.MasterInstrument.Name;
				string ntDocsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
				string logsDir = System.IO.Path.Combine(ntDocsPath, "NinjaTrader 8", "trace", "SessionLevels");
				
				if (!System.IO.Directory.Exists(logsDir))
					System.IO.Directory.CreateDirectory(logsDir);
				
				string fileName = string.Format("{0}_{1:yyyyMMdd}.txt", instrumentName, DateTime.Now);
				logFilePath = System.IO.Path.Combine(logsDir, fileName);
				
				// v1.11.18: Overwrite THIS instrument's log only
				// Each instrument has its own file, so this won't affect other instruments
				lock (logFileLock)
				{
					System.IO.File.WriteAllText(logFilePath, 
						string.Format("=== {0} Strategy Log - Started {1:yyyy-MM-dd HH:mm:ss} ===\r\n\r\n", 
							instrumentName, DateTime.Now));
				}
			}
			catch { }
		}


		// =========================================================
		// v1.11.17: LAG FILTER - Check chart data freshness
		// =========================================================
		private bool CheckChartLag()
		{
			// Only check in Realtime (not Playback/Historical)
			if (State != State.Realtime) return true;
			
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
				return false; // Not safe to trade
			}
			
			isLagAlertActive = false;
			return true; // Safe to trade
		}


		// =========================================================
		// v1.11.5: TRIGGER LABELS - Distancia basada en ATR
		// =========================================================
		private void DrawTriggerLabel(string tag, bool isShort, int barsAgo, double anchorPrice)
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
		// v1.11.0: INTELLIGENT RESTART EVALUATION (No Position)
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
				if (lvl.StartTime.Date == Time[0].Date) continue;
				
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
				Name										= "SessionLevelsStrategy_2026_01_02_82" + StrategyVersion;
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
				IsOverlay = true;
				
				// IsUnmanaged moved to top
				
				// Add Plots for VWAP
				// Note: Plot AutoScale cannot be disabled in NinjaTrader strategies
				// User should disable AutoScale manually in Chart properties if needed
				AddPlot(Brushes.White, "HighVWAP"); // Values[0]
				AddPlot(Brushes.White, "LowVWAP");  // Values[1]
				// Trade VWAP is calculated internally but NOT plotted (v1.10.31)
				
				// FINAL FORCE: Unmanaged Mode
				// FINAL FORCE: Unmanaged Mode
				IsUnmanaged = true; // Enabled for v1.7.0 Unmanaged Refactor
			}
			else if (State == State.DataLoaded)
			{
				Log("DEBUG: OnStateChange(DataLoaded) IsUnmanaged = " + IsUnmanaged);
				// Initialize Helper Indicators
				atr = ATR(14); // For Dynamic Spacing
				
				// v1.12.0: Initialize control buttons
				InitializeControlButtons();
				
				// v1.14.0: Initialize TradeAnalyzer CSV Export (Separated by Context)
				try
				{
					string safeInstrument = Instrument.FullName.Replace("/", "-").Replace(":", "-").Replace(" ", "_");
					
					// v1.14.0: Export to Strategies/TradeExports/{context}/ folder
					// FIX: Use UserDataDir directly, it already points to "Documents\NinjaTrader 8"
					string strategiesDir = System.IO.Path.Combine(
						NinjaTrader.Core.Globals.UserDataDir.TrimEnd(System.IO.Path.DirectorySeparatorChar),
						"bin", "Custom", "Strategies", "TradeExports");
					
					// Determine context subfolder based on execution state and account
					// v1.14.11: Usar nombre exacto de cuenta para auto-detección en Streamlit
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
						// v1.13.16: Added Commission and NetPnL columns
						string header = "ID,Instrument,EntryTime,Type,EntryPrice,ExitTime,ExitPrice,Result,PnL,Commission,NetPnL,MAE,MFE,Setup,Attempt,RiskReward";
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
				
				// Clear Lists
				activeLevels.Clear();
				virginLevels.Clear();
				// PERSISTENCE DISABLED (v1.5.5) - Relying on Chart History
				/*
				try 
				{
					LoadLevels();
				} 
				catch(Exception ex) { Print("Warning: Failed to load levels: " + ex.Message); }
				*/
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
		// CROSS-INSTRUMENT RISK SYNC (v1.10.21)
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
		
		private void WriteSharedRisk(double atrRisk)
		{
			// FIX v1.14.13: Disable Shared Risk in Backtest/Optimization to prevent state leak
			if (State == State.Historical || State == State.Optimization) return;

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
				}
			}
			catch { }
		}
		
		private double ReadMaxSharedRisk()
		{
			// FIX v1.14.13: Disable Shared Risk in Backtest/Optimization
			if (State == State.Historical || State == State.Optimization) return RiskPerTradeUSD;

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
		private TimeZoneInfo nyTimeZone;
		private TimeZoneInfo chartTimeZone;
		private bool timeZonesLoaded = false;
		private double lastVol = 0;

		// Level Persistence
		private class SessionLevel
		{
			public string Name;
			public double Price;
			public DateTime StartTime;
			public DateTime EndTime;
			public DateTime MitigationTime; // When it was touched
			public bool IsResistance; // True = High, False = Low
			public bool IsMitigated;
			public Brush Color;
			public string Tag; // For Drawing
			
			// VWAP Data
			public double VolSum;
			public double PvSum;
			public bool JustReset;
			
			// v1.10.25: Retry tracking
			public int EntryAttempts = 0;
		}
		
		private List<SessionLevel> activeLevels = new List<SessionLevel>();
		private List<SessionLevel> virginLevels = new List<SessionLevel>();

		// Strategy Initialization Flag
		private bool isStrategyInitialized = false;
		private bool isRealtimeInitialized = false; // v1.7.7 Cleanup Flag
		private int realtimeStartBar = -1; // v1.10.28: Bar when strategy entered Realtime (for fresh signals only)
		private HashSet<string> skippedLevelsAtStartup = new HashSet<string>(); // v1.10.29: Levels already touched at startup
		private bool gapDetected = false;
		private int gapCount = 0;
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
			if (daysToFriday == 0 && nyTime.TimeOfDay >= TimeSpan.Parse("18:00"))
				daysToFriday = 0; // Already past this Friday 6pm
			else if (daysToFriday == 0)
				daysToFriday = 7; // Before Friday 6pm, use last Friday
			
			DateTime lastFriday6pm = nyTime.Date.AddDays(-((7 - daysToFriday) % 7)).Date.Add(TimeSpan.Parse("18:00"));
			
			// Adjust: If we're on Friday after 6pm, lastFriday6pm is TODAY
			if (nyTime.DayOfWeek == DayOfWeek.Friday && nyTime.TimeOfDay >= TimeSpan.Parse("18:00"))
				lastFriday6pm = nyTime.Date.Add(TimeSpan.Parse("18:00"));
			// If Saturday/Sunday, last Friday was recent
			else if (nyTime.DayOfWeek == DayOfWeek.Saturday)
				lastFriday6pm = nyTime.Date.AddDays(-1).Add(TimeSpan.Parse("18:00"));
			else if (nyTime.DayOfWeek == DayOfWeek.Sunday)
				lastFriday6pm = nyTime.Date.AddDays(-2).Add(TimeSpan.Parse("18:00"));
			else
				lastFriday6pm = nyTime.Date.AddDays(-((int)nyTime.DayOfWeek + 2)).Add(TimeSpan.Parse("18:00"));
			
			// Convert to chart timezone for comparison
			DateTime lastFriday6pmChart = TimeZoneInfo.ConvertTime(lastFriday6pm, nyTimeZone, chartTimeZone);
			
			// Check if we need to reset
			if (lastFriday6pmChart > lastWeeklyReset && currentEntryState != EntryState.PositionActive)
			{
				lastWeeklyReset = lastFriday6pmChart;
				
				Log(Time[0] + " WEEK RESET - State cleared for new trading week (Last Friday 6pm: " + lastFriday6pm + " NY)");
				
				// v1.13.15: Diagnostic logging - show active levels vs current price at week start
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
				validatedTargetPrice = 0;
				cachedOppositeLevel = null;
				isInternalLevel = false;
				waitingForVwapMitigation = false;
				currentVwapNumber = 1;
				
				// Reset Adhoc VWAP
				adhocVolSum = 0;
				adhocPvSum = 0;
				adhocLastBar = -1;
				
				// Clear skipped levels from last week
				skippedLevelsAtStartup.Clear();
			}
		}

		protected override void OnBarUpdate()
		{
			try
			{
			// v1.13.7: Heartbeat REMOVED - was spamming output

			// v1.7.7: STARTUP CLEANUP FAILSAFE
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
									Log(Time[0] + " STARTUP ADOPT: Recovered TP1 order: " + o.Name + " Qty=" + o.Quantity);
								}
								else if (o.Name.StartsWith("TP2_") || o.Name.Contains("_TP2"))
								{
									tp2Order = o;
									Log(Time[0] + " STARTUP ADOPT: Recovered TP2 order: " + o.Name + " Qty=" + o.Quantity);
								}
							}
						}
						
						Log(Time[0] + " STARTUP ADOPT COMPLETE: SL=" + (stopOrder != null) + 
							" TP1=" + (tp1Order != null) + " TP2=" + (tp2Order != null));
						
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
// v1.11.28: CRITICAL - If cant protect, CLOSE
Log(Time[0] + " CRITICAL: Cannot protect. CLOSING.");
try {
if (isShortSetup)
SubmitOrderUnmanaged(0, OrderAction.BuyToCover, OrderType.Market, posQty, 0, 0, "", "EmergencyClose_Short");
else
SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.Market, posQty, 0, 0, "", "EmergencyClose_Long");
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
										if (isShortSetup)
											SubmitOrderUnmanaged(0, OrderAction.BuyToCover, OrderType.Market, posQty, 0, 0, "", "EmergencyClose_Short");
										else
											SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.Market, posQty, 0, 0, "", "EmergencyClose_Long");
										
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
						// v1.11.0: INTELLIGENT RESTART EVALUATION
						// Instead of blindly cancelling orders, evaluate if setup is still valid
						EvaluateRestartNoPosition();
					}
				}
				
				// v1.10.29: DETECT LEVELS ALREADY BEING TOUCHED AT STARTUP
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
				
				// v1.11.0: Historical state now handled by EvaluateRestartNoPosition()
			}

			if (CurrentBar < 20) return;
			
			// v1.10.37: Reset state at week end (Friday 6pm NY) or new week start
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
				ethHighVWAP = new SessionVWAP(); ethHighVWAP.Reset(Volume[0], Close[0]);
				ethLowVWAP = new SessionVWAP(); ethLowVWAP.Reset(Volume[0], Close[0]);
				
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
					nyTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
					
					// Get the TimeZone of the current bars/chart
					if (NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo != null)
						chartTimeZone = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo;
					else
						chartTimeZone = TimeZoneInfo.Local; // Fallback
						
					timeZonesLoaded = true;
				}
				catch (Exception ex)
				{
					Log("Error loading TimeZones: " + ex.Message);
					timeZonesLoaded = true; 
				}
			}

			// v1.14.6: Continuous Lag Monitoring (Visuals only)
			// Ensure visual alert clears when lag dissipates, even if no trade is attempting
			CheckChartLag();

			// 0. Calculate Volume Delta for VWAP
			if (IsFirstTickOfBar) lastVol = 0;
			double deltaVol = Volume[0] - lastVol;
			lastVol = Volume[0];

			// CSV LOGGING INIT (Once per session)
			if (CurrentBar == BarsRequiredToTrade) // Use a safe bar index to init
			{
				InitCSV();
			}

			// 1. Session Logic: Identify/Create Levels (Use Cached TimeSpans)
			CheckSession("Asia", tsAsiaStart, tsAsiaEnd, Brushes.White, deltaVol);
			CheckSession("Europe", tsEuStart, tsEuEnd, Brushes.Yellow, deltaVol);
			CheckSession("USA", tsUsaStart, tsUsaEnd, Brushes.RoyalBlue, deltaVol);
			
			// 2. Manage Extension & Touching
			ManageLevels(deltaVol);
			
			// 3. Global ETH VWAPs
			ManageGlobalVWAPs(deltaVol);
			
			// v1.11.22: HISTORICAL LOAD OPTIMIZATION
			// Skip trading logic for old bars to speed up strategy loading
			// Levels are still calculated above, only entry/exit logic is skipped
			bool isRecentBar = (Time[0].Date >= DateTime.Today.AddDays(-3));
			if (State == State.Historical && !isRecentBar && !AllowBacktest)
			{
				return; // Levels calculated, skip trading logic for speed
			}
			
			// 4. Entry Logic (only for recent bars or Realtime)
			ManageEntryA_Plus();
			
			// v1.13.0: Track MAE/MFE for active trades
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
				if (EnableDebugLogs)
					Log($"CRITICAL ERROR in OnBarUpdate at Bar {CurrentBar}: {ex.ToString()}");
			}
		}
		
		// v1.13.6: Diagnostic OnRender
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

		private void CheckSession(string sessionName, TimeSpan startTs, TimeSpan endTs, Brush color, double deltaVol)
		{
			if (nyTimeZone == null || chartTimeZone == null) return;

			DateTime chartTime = Time[0];
			DateTime nyTime = TimeZoneInfo.ConvertTime(chartTime, chartTimeZone, nyTimeZone);
			TimeSpan nyTimeOfDay = nyTime.TimeOfDay;
			
			// REMOVED PARSING for Performance
			// TimeSpan startTs = TimeSpan.Parse(startStr);
			// TimeSpan endTs = TimeSpan.Parse(endStr);
			
			bool inSession = false;
			
			if (startTs > endTs) { if (nyTimeOfDay >= startTs || nyTimeOfDay < endTs) inSession = true; }
			else { if (nyTimeOfDay >= startTs && nyTimeOfDay < endTs) inSession = true; }
			
			if (inSession)
			{
				// Determine Session Date (for unique ID)
				DateTime calculatedSessionStartNY = (startTs > endTs && nyTimeOfDay < endTs) ? nyTime.Date.AddDays(-1) : nyTime.Date;
				calculatedSessionStartNY = calculatedSessionStartNY.Add(startTs);
				
				// Unique IDs for High and Low
				string tagH = sessionName + "_High_" + calculatedSessionStartNY.Ticks;
				string tagL = sessionName + "_Low_" + calculatedSessionStartNY.Ticks;
				
				// Find or Create Levels (Legacy ID Lookup first)
				SessionLevel highLvl = activeLevels.FirstOrDefault(l => l.Tag == tagH);
				SessionLevel lowLvl = activeLevels.FirstOrDefault(l => l.Tag == tagL);
				
				// Convert Start Time to Chart Time for Visuals
				DateTime chartStartTime = TimeZoneInfo.ConvertTime(calculatedSessionStartNY, nyTimeZone, chartTimeZone);

				// FUZZY MATCHING (v1.5.4):
				// Instead of relying purely on the Exact Ticks ID (which is fragile to precision errors),
				// We check if a level with the SAME NAME and APPROXIMATE TIME (within 4 hours) already exists.
				
				if (highLvl == null)
				{
					highLvl = activeLevels.FirstOrDefault(l => l.Tag == tagH || (l.Name == sessionName + " High" && Math.Abs((l.StartTime - chartStartTime).TotalHours) < 4));
				}
				if (lowLvl == null)
				{
					lowLvl = activeLevels.FirstOrDefault(l => l.Tag == tagL || (l.Name == sessionName + " Low" && Math.Abs((l.StartTime - chartStartTime).TotalHours) < 4));
				}

				if (highLvl == null)
				{
					// New High Level (Init VWAP with current Bar Full Volume as it creates the anchor)
					highLvl = new SessionLevel 
					{ 
						Name = sessionName + " High", Price = double.MinValue, StartTime = chartStartTime, EndTime = Time[0], 
						IsResistance = true, IsMitigated = false, Color = color, Tag = tagH,
						VolSum = Volume[0], PvSum = Volume[0] * Close[0], JustReset = true
					};
					activeLevels.Add(highLvl);
				}
				else highLvl.JustReset = false; // Reset flag default
				
				if (lowLvl == null)
				{
					// New Low Level
					lowLvl = new SessionLevel 
					{ 
						Name = sessionName + " Low", Price = double.MaxValue, StartTime = chartStartTime, EndTime = Time[0], 
						IsResistance = false, IsMitigated = false, Color = color, Tag = tagL,
						VolSum = Volume[0], PvSum = Volume[0] * Close[0], JustReset = true
					};
					activeLevels.Add(lowLvl);
				}
				else lowLvl.JustReset = false;
				
				// Logic: While in session, we push the High/Low out. 
				// If New High -> Reset VWAP to Anchor HERE.
				
				if (High[0] > highLvl.Price) 
				{
					highLvl.Price = High[0];
					// RE-ANCHOR VWAP
					highLvl.VolSum = Volume[0];
					highLvl.PvSum = Volume[0] * Close[0];
					highLvl.JustReset = true;
				}
				
				if (Low[0] < lowLvl.Price) 
				{
					lowLvl.Price = Low[0];
					// RE-ANCHOR VWAP
					lowLvl.VolSum = Volume[0];
					lowLvl.PvSum = Volume[0] * Close[0];
					lowLvl.JustReset = true;
				}
				
				// While in session, update EndTime to current to keep line growing
				if (!highLvl.IsMitigated) highLvl.EndTime = Time[0];
				if (!lowLvl.IsMitigated) lowLvl.EndTime = Time[0];
			}
		}

		private void ManageLevels(double deltaVol)
		{
			// Check for touches on existing active levels
			
			foreach (var lvl in activeLevels)
			{
				// BACKTEST SAFETY: Completely ignore future levels (Visuals + Logic)
				if (lvl.StartTime > Time[0]) continue;

				// VWAP ACCUMULATION
			if (!lvl.JustReset)
			{
				lvl.VolSum += deltaVol;
				double price = Close[0];
				if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
				else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;
				
				lvl.PvSum += deltaVol * price;
			}
				// If JustReset was true, we already set VolSum/PvSum in CheckSession. 
				// JustReset is ephemeral for this tick.
				
				// Calculate VWAP
				double vwap = 0;
				if (lvl.VolSum > 0) vwap = lvl.PvSum / lvl.VolSum;

				// LINE EXTENSION LOGIC
				// Alive: Always extend.
				// Mitigated: Extend ONLY if we are still in the same calendar day as the mitigation Event.
				
				if (!lvl.IsMitigated)
				{
					lvl.EndTime = Time[0];
				}
				else
				{
					// Ghost Line Extension
					// Extension Rule: Continue until the End of the American Session (USAEndTime).
					// We need to calculate the *specific* cutoff time relative to the Mitigation event.
					
					// 1. Convert MitigationTime to NY to understand when it happened
					DateTime mitNy = TimeZoneInfo.ConvertTime(lvl.MitigationTime, chartTimeZone, nyTimeZone);
					TimeSpan usaEndTs = TimeSpan.Parse(USAEndTime);
					
					// 2. Determine the Cutoff Date/Time (NY)
					// If mitigation happened BEFORE the cutoff today (e.g. 10:00 vs 18:00), cutoff is Today 18:00.
					// If mitigation happened AFTER the cutoff (e.g. 19:00 vs 18:00), cutoff is Tomorrow 18:00.
					
					DateTime cutoffNy;
					if (mitNy.TimeOfDay < usaEndTs)
						cutoffNy = mitNy.Date.Add(usaEndTs);
					else
						cutoffNy = mitNy.Date.AddDays(1).Add(usaEndTs);
						
					// 3. Compare Current Time (NY) to Cutoff (NY)
					DateTime currentNy = TimeZoneInfo.ConvertTime(Time[0], chartTimeZone, nyTimeZone);
					
					if (currentNy < cutoffNy)
					{
						lvl.EndTime = Time[0];
					}
					// Else: Freeze (Stop extending)
				}
				
				// Check for Mitigation (if not already broken)
				// Only if session is effectively done (Start/End checks or just assume if formed)
				// Simplified: Just always check touch.
				
				if (!lvl.IsMitigated)
				{
					// Avoid self-mitigation during formation
					// If the StartTime was effectively "today" or "recent" and we are still largely in that window?
					// Problem: CheckSession pushes Price up/down. 
					// If we are IN session, CheckSession updates Price.
					// So if High[0] == Price, CheckSession makes Price = High[0].
					// So High[0] == Price.
					// So "High[0] >= Price" is TRUE.
					// We need to know if we are "In Session" to avoid mitigation.
					
					// Heuristic: If CheckSession updated it THIS TICK, don't mitigate.
					// But we run ManageLevels AFTER CheckSession.
					// Let's rely on a flag or simply check if Time is outside Session Window?
					// Checking Time is hard because of the varying session hours.
					// Let's use a "InSession" flag on the object?
					// Or reusing the "EndTime" check from previous step:
					// If(lvl.EndTime == Time[0]) it means CheckSession updated it? 
					// NO, we just updated lvl.EndTime = Time[0] at the top of this loop! Invalid logic now.
					
					// Let's add an explicit "LastUpdateBar" or similar to SessionLevel?
					// Or simpler: We know the logic in CheckSession updates Price.
					// If Price == High[0], it's likely pushing.
					// But if Price < High[0], it's a break.
					// Wait, if Price < High[0] (Resistance), then CheckSession WOULD have updated it if we were in session!
					// So if CheckSession DID NOT update it (Price < High[0]), it means we are NOT in session (or logic failed).
					// Therefore, if High[0] > Price, it MUST be a mitigation break!
					// CORRECT.
					
					// Exception: The very specific moment High[0] jumps? 
					// If in session, CheckSession runs first. 
					// If High[0] > currentHigh, set currentHigh = High[0].
					// So entering ManageLevels, Price == High[0].
					// Use strict inequality? High[0] > Price? No, touch is enough.
					
					// Let's iterate:
					// In Session: Price = 100. High[0] = 101. -> CheckSession sets Price = 101. -> ManageLevels sees Price=101, High[0]=101.
					// Out Session: Price = 100. High[0] = 101. -> CheckSession does nothing. -> ManageLevels sees Price=100, High[0]=101. -> MITIGATION!
					
					// So, logic:
					// Resistance: If High[0] > Price -> Mitigation.
					// Support: If Low[0] < Price -> Mitigation.
					// Equality (Touch) shouldn't count if we assume "Break"?
					// User said "cortadas" (cut/broken) or "tocada" (touched)?
					// "hasta que sea tocada" (touched).
					// If it's a touch (==), then in-session formation is a touch.
					// We MUST distinguish In-Session.
					
					// Let's calculate In-Session locally again or store it.
					// Re-calculating properly is safer.
					
					// Actually, let's use the object creation/update time?
					// Let's look at `IsResistance`.
					bool potentialMitigation = false;
					if (lvl.IsResistance && High[0] >= lvl.Price) potentialMitigation = true;
					if (!lvl.IsResistance && Low[0] <= lvl.Price) potentialMitigation = true;
					
					if (potentialMitigation)
					{
						// Check if we are physically in the session window for this specific level
						// This line's tag has StartTicks.
						// Simplest: Check if the *current price* is EQUAL to level price.
						// If equal, likely just forming/touching.
						// If strictly Greater (Res) or Less (Sup) AND Level Price wasn't updated?
						// It's ambiguous.
						
						// CLEAN FIX: Add `IsActive` bool to SessionLevel, set by CheckSession.
						// But I can't easily change CheckSession signature in this edit without replacing whole file.
						// I'll calculate `inSession` simply here. It's safe.
						// Oh wait, I don't know WHICH session hours apply to THIS level (Asia? USA?).
						// I have `lvl.Name` ("Asia High"). I can parse or Map.
						
						// HACK: Just assume if the Price CHANGED this bar, it's active?
						// No.
	
						// Let's guess based on inequality.
						// If High[0] > lvl.Price, it's definitely a Break (Mitigation), because if it was active, Price would have updated to match High[0].
						// Wait. CheckSession updates logic: `if (High[0] > highLvl.Price) highLvl.Price = High[0];`
						// So Price will ALWAYS be >= High[0] if active.
						// Price will never be < High[0].
						// So if High[0] > Price, it implies CheckSession did NOT run/update -> We are Out of Session -> Mitigation.
						// If High[0] == Price? Could be "Just forming" OR "Perfect double top touch".
						// We'll ignore Exact Touch for mitigation to be safe against formation noise.
						// Strictly greater/less for "Cut/Break".
						
						bool strictBreak = false;
						if (lvl.IsResistance && High[0] > lvl.Price) strictBreak = true;
						if (!lvl.IsResistance && Low[0] < lvl.Price) strictBreak = true;
						
						if (strictBreak)
						{
							lvl.IsMitigated = true;
							lvl.MitigationTime = Time[0];
						}
					}
				}
				
				// Drawing Logic in Low Performance Mode (Optional)
				if (ShowVisuals)
				{
					string tagA = lvl.Tag + "_A";
					string tagB = lvl.Tag + "_B";
					
					if (!lvl.IsMitigated)
					{
						// Phase A Only: Start -> Current
						Draw.Line(this, tagA, false, lvl.StartTime, lvl.Price, lvl.EndTime, lvl.Price, lvl.Color, DashStyleHelper.Solid, 2);
					}
					else
					{
						// Phase A: Start -> Mitigation
						Draw.Line(this, tagA, false, lvl.StartTime, lvl.Price, lvl.MitigationTime, lvl.Price, lvl.Color, DashStyleHelper.Solid, 2);
						
						// Phase B (Ghost): Mitigation -> Current (Gray, Dash, 1px)
						Draw.Line(this, tagB, false, lvl.MitigationTime, lvl.Price, lvl.EndTime, lvl.Price, Brushes.Gray, DashStyleHelper.Dash, 1);
					}
				}
			}
		}


		// -------------------------------------------------------------------------
		// ENTRY LOGIC VARIABLES
		// -------------------------------------------------------------------------
		private enum EntryState { Idle, WaitingForConfirmation, WaitingForVwapMitigation, workingOrder, PositionActive } // Entry State
		private EntryState currentEntryState = EntryState.Idle;
		private string setupLevelName = "";
		private DateTime setupLevelTime = DateTime.MinValue; // NEW (v1.5.8): Track time of the level we are trading
		private double setupAnchorPrice = 0;
		private bool isShortSetup = false; // true = Short, false = Long
		private bool visualConfirmationDone = false; // v1.11.11: Control para pintar vela solo la primera vez
		// Rejection Loop Protection (v1.7.1)
		private int lastRejectionBar = -1;
		// V_EXEC: Execution Variables
		private Order entryOrder = null; // Consolidated Entry (v1.7.17)
		// REMOVED: entryOrder1, entryOrder2
		
		// Protection State (v1.7.17)
		private int protectedTp1Qty = 0;
		private int protectedTp2Qty = 0;
		private bool protectionOrdersCreated = false; // v1.11.14: Prevent duplicate creation
		private int tradeOriginalQty = 0; // v1.11.23: Original trade quantity for panel display (doesn't change after TP1 fill)
		private double tradeOriginalTp1Price = 0; // v1.11.24: Original TP1 price for panel display
		private double tradeOriginalTp2Price = 0; // v1.11.24: Original TP2 price for panel display
		// v1.10.31: Trade VWAP - continues accumulating even when day changes
		// Separate from global VWAP to keep TP1 moving with original day's VWAP
		private SessionVWAP tradeVWAP = new SessionVWAP();
		private bool tradeVwapActive = false;

		// REFACTOR v1.7.3: Consolidated SL/TP tracking
		private Order stopOrder = null; // Legacy fallback, kept to avoid compile errors if referenced elsewhere (e.g. Draw)
		private Order stopOrder1 = null; 
		private Order stopOrder2 = null; 
		private Order tp1Order = null;
		private Order tp2Order = null;
		private Order targetOrder = null; // Legacy tracker
		
		// Visual Tracking
		private string triggerTag = "";
		private int triggerBar = 0;
		
		// -------------------------------------------------------------------------
		// GLOBAL ETH SESSION VWAP LOGIC
		// -------------------------------------------------------------------------
		private class SessionVWAP
		{
			public double VolSum;
			public double PvSum;
			public double CurrentValue => VolSum == 0 ? 0 : PvSum / VolSum;
			
			public void Reset(double vol, double price)
			{
				VolSum = vol;
				PvSum = vol * price;
			}
			
			public void Accumulate(double vol, double price)
			{
				VolSum += vol;
				PvSum += vol * price;
			}
		}
		
		private SessionVWAP ethHighVWAP = new SessionVWAP();
		private SessionVWAP ethLowVWAP = new SessionVWAP();
		
		#region Properties
		// Email & Screenshot Properties
		[NinjaScriptProperty]
		[Display(Name = "Enable Local Screenshots", Description = "Save screenshots to disk without sending email", GroupName = "8. Email Alerts", Order = 0)]
		public bool EnableLocalScreenshots { get; set; } = true;

		[NinjaScriptProperty]
		[Display(Name = "Enable Email Alerts", Description = "Send screenshot via email (Requires SMTP settings)", GroupName = "8. Email Alerts", Order = 1)]
		public bool EnableEmailAlerts { get; set; } = false;

		[NinjaScriptProperty]
		[Display(Name = "To Address", Description = "Recipient address", GroupName = "8. Email Alerts", Order = 2)]
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

		// AD-HOC VWAP Variables (Fresh Start)
		private double adhocVolSum = 0;
		private double adhocPvSum = 0;
		private double adhocLastVol = 0; // To track delta volume inside a bar
		private int adhocLastBar = -1;
		private int adhocAnchorBar = -1; // v1.10.11: Track anchor bar for retroactive update

		private void UpdateAdhocVWAP()
		{
			// Reset tracker on new bar for proper delta calculation
			if (CurrentBar != adhocLastBar)
			{
				adhocLastVol = 0;
				adhocLastBar = CurrentBar;
				
				// v1.10.11: Retroactive update - if previous bar was anchor, recalculate with final Close
				if (CurrentBar > 0 && adhocAnchorBar == CurrentBar - 1 && adhocVolSum > 0)
				{
					double finalPrice = Close[1];
					if (VwapMethod == VwapCalculationMode.Typical) finalPrice = (High[1] + Low[1] + Close[1]) / 3.0;
					else if (VwapMethod == VwapCalculationMode.OHLC4) finalPrice = (Open[1] + High[1] + Low[1] + Close[1]) / 4.0;
					
					// Recalculate VWAP with final values
					adhocVolSum = Volume[1];
					adhocPvSum = Volume[1] * finalPrice;
					
					// Update visual start point retroactively
					visualAdhocPrevBarVal = finalPrice;
					visualAdhocLastVal = finalPrice;
				}
			}

			// Calculate Delta Volume (Current Bar Volume so far - what we already processed)
			// NinjaTrader Volume[0] is cumulative for the bar
			double currentBarVol = Volume[0];
			double deltaVol = currentBarVol - adhocLastVol;
			
			if (deltaVol > 0)
	{
		adhocVolSum += deltaVol;
		double price = Close[0];
		if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
		else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;
		
		adhocPvSum += deltaVol * price; 
		adhocLastVol = currentBarVol; // Update processed volume
	}
		} 
		
		private int highAnchorBar = 0;
		private int lowAnchorBar = 0;

		private void ManageGlobalVWAPs(double deltaVol)
		{
			if (nyTimeZone == null || chartTimeZone == null) return;
			
			// 1. Determine Current Trading Day (based on 18:00 NY start)
			DateTime currentNy = TimeZoneInfo.ConvertTime(Time[0], chartTimeZone, nyTimeZone);
			TimeSpan cutoff = TimeSpan.FromHours(18);
			DateTime tradingDay = currentNy.TimeOfDay >= cutoff ? currentNy.Date.AddDays(1) : currentNy.Date;
			
			// 2. HARD RESET at Start of Day
			bool hardReset = false;
			if (tradingDay != lastEthResetDate)
			{
				ethHighPrice = double.MinValue;
				ethLowPrice = double.MaxValue;
				ethHighVWAP = new SessionVWAP();
				ethLowVWAP = new SessionVWAP();
				lastEthResetDate = tradingDay;
				hardReset = true;
				
				// Reset Anchor Trackers
				highAnchorBar = CurrentBar;
				lowAnchorBar = CurrentBar;
			}
			
			// 3. Update High/Low and Anchor Logic
	bool highReset = false;
	bool lowReset = false;
	
	double price = Close[0];
	if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
	else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;
	
	// v1.10.10: Retroactive anchor update - On first tick of new bar, if previous bar was anchor,
	// recalculate VWAP with the FINAL Close[1] and update Values[x][1] to correct the visual
	if (IsFirstTickOfBar && CurrentBar > 0)
	{
		// Check if previous bar was the high anchor
		if (highAnchorBar == CurrentBar - 1 && ethHighVWAP.VolSum > 0)
		{
			double finalPrice = Close[1];
			if (VwapMethod == VwapCalculationMode.Typical) finalPrice = (High[1] + Low[1] + Close[1]) / 3.0;
			else if (VwapMethod == VwapCalculationMode.OHLC4) finalPrice = (Open[1] + High[1] + Low[1] + Close[1]) / 4.0;
			
			// Recalculate VWAP with final values
			ethHighVWAP.Reset(Volume[1], finalPrice);
			// Update the previous bar's visual value retroactively
			Values[0][1] = finalPrice;
		}
		
		// Check if previous bar was the low anchor
		if (lowAnchorBar == CurrentBar - 1 && ethLowVWAP.VolSum > 0)
		{
			double finalPrice = Close[1];
			if (VwapMethod == VwapCalculationMode.Typical) finalPrice = (High[1] + Low[1] + Close[1]) / 3.0;
			else if (VwapMethod == VwapCalculationMode.OHLC4) finalPrice = (Open[1] + High[1] + Low[1] + Close[1]) / 4.0;
			
			// Recalculate VWAP with final values
			ethLowVWAP.Reset(Volume[1], finalPrice);
			// Update the previous bar's visual value retroactively
			Values[1][1] = finalPrice;
		}
	}
	
	// Check High
	if (High[0] > ethHighPrice)
	{
		// New High found! The PREVIOUS segment (from highAnchorBar to CurrentBar-1) is now "Old/Cut".
		// We must paint it GRAY.
		if (!hardReset && CurrentBar > highAnchorBar)
		{
			int barsBack = CurrentBar - highAnchorBar;
			// IMPORTANT: Use i < barsBack to avoid overwriting the Transparency of the Anchor Bar itself.
			for (int i = 1; i < barsBack; i++)
			{
				PlotBrushes[0][i] = Brushes.Gray;
			}
		}
		
		ethHighPrice = High[0];
		highReset = true;
		ethHighVWAP.Reset(Volume[0], price);
		highAnchorBar = CurrentBar; // Update anchor to here
	}
	else
	{
		ethHighVWAP.Accumulate(deltaVol, price);
	}
	
	// Check Low
	if (Low[0] < ethLowPrice)
	{
		// New Low found! Paint previous segment Gray.
		if (!hardReset && CurrentBar > lowAnchorBar)
		{
			int barsBack = CurrentBar - lowAnchorBar;
			for (int i = 1; i < barsBack; i++)
			{
				PlotBrushes[1][i] = Brushes.Gray;
			}
		}
		
		ethLowPrice = Low[0];
		lowReset = true;
		ethLowVWAP.Reset(Volume[0], price);
		lowAnchorBar = CurrentBar;
	}
	else
	{
		ethLowVWAP.Accumulate(deltaVol, price);
	}
	
	// v1.10.31: Also accumulate in Trade VWAP if active (keeps TP1 moving during overnight)
	if (tradeVwapActive && deltaVol > 0)
	{
		tradeVWAP.Accumulate(deltaVol, price);
	}
			
			// 4. Assign to Plots (Values[0] = High, Values[1] = Low)
			// Default color is White (defined in AddPlot). We only override active history to Gray when it dies.
			// The "Current" active segment stays White until it dies.
			
			if (ethHighVWAP.VolSum > 0)
			{
				Values[0][0] = ethHighVWAP.CurrentValue;
				
				if (hardReset || highReset)
				{
					PlotBrushes[0][0] = Brushes.Transparent;
				}
			}
			else
			{
				Values[0][0] = double.NaN; 
			}

			if (ethLowVWAP.VolSum > 0)
			{
				Values[1][0] = ethLowVWAP.CurrentValue;
				
				if (hardReset || lowReset)
				{
					PlotBrushes[1][0] = Brushes.Transparent;
				}
			}
			else
			{
				Values[1][0] = double.NaN;
			}
			
			// v1.10.31: Draw Trade VWAP line manually (no vertical connections)
			if (tradeVwapActive && tradeVWAP.VolSum > 0 && CurrentBar > 0)
			{
				double tradeVwapValue = tradeVWAP.CurrentValue;
				string lineTag = "TradeVWAP_" + CurrentBar;
				Draw.Line(this, lineTag, false, 1, tradeVwapValue, 0, tradeVwapValue, Brushes.Cyan, DashStyleHelper.Solid, 2);
			}
			
			// Debug Panel
			DrawStatePanel();
			
			// SAFETY NET: Check for Zombie Positions (In Market, but State logic missed it)
			CheckSafetyNet();
			
			// FAILSAFE: Hard Stop Check (In case Managed Order fails)
			CheckHardStop();

			// SESSION EXIT (v1.7.4)
			CheckSessionExit();
		}
		
		private void CheckHardStop()
		{
			if (Position.MarketPosition == MarketPosition.Flat) return;
			// FIX v1.14.2: Prevent infinite loop if position close takes time
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
						ClosePositionUnmanaged("Anchor Violation");
						// Reset handled in OnExecutionUpdate
						return;
				}
			}
		}


		// -------------------------------------------------------------------------
		// SESSION EXIT MANANAGEMENT (v1.7.4)
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
			
			// v1.14.5: DYNAMIC SESSION AWARENESS (Holidays/Early Closes)
			// Instead of fixed "16:00" string, we ask NinjaTrader for the TRUE session end of this bar.
			// FIX v1.14.7: Use SessionIterator properly
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
			
			// Trigger logic if Friday OR Early Holiday
			if (isFriday || isEarlyClose)
			{
				DateTime dynamicCutoff = actualSessionEnd.Subtract(exitBuffer);
				
				// Check Window: From Cutoff (End-30s) up to End+5min (Gap/Cleanup)
				if (Time[0] >= dynamicCutoff && Time[0] <= actualSessionEnd.Add(gapBuffer))
				{
					// LOGIC ACTIVATED (Indent matches original block)
				// 3. Execution Logic - ONLY ON FRIDAYS
				
				// A) Close Positions
				if (Position.MarketPosition != MarketPosition.Flat)
				{
					// Only log once per bar to avoid spam
					if (IsFirstTickOfBar)
						Log(Time[0] + " FRIDAY CLOSE: Market closing for weekend. Forcing Exit.");
						
					ClosePositionUnmanaged("Exit on Friday Close");
				}
				
				// B) Cancel Working Orders & Reset State
				if (currentEntryState != EntryState.Idle)
				{
					if (IsFirstTickOfBar)
						Log(Time[0] + " SESSION CLOSE PROTECT: Cancelling Pending Orders.");
						
					// CONSOLIDATED ENTRY (v1.7.17)
					if (entryOrder != null && entryOrder.OrderState == OrderState.Working) CancelOrder(entryOrder);
					if (stopOrder1 != null && stopOrder1.OrderState == OrderState.Working) CancelOrder(stopOrder1);
					if (stopOrder2 != null && stopOrder2.OrderState == OrderState.Working) CancelOrder(stopOrder2);
					if (tp1Order != null && tp1Order.OrderState == OrderState.Working) CancelOrder(tp1Order);
					if (tp2Order != null && tp2Order.OrderState == OrderState.Working) CancelOrder(tp2Order);
					
					currentEntryState = EntryState.Idle; // Force Idle
					setupLevelName = "";
				}
			}
		}
	}


		
		// Orphan State Tracking
		private bool orphanHandled = false;

		private void CheckSafetyNet()
		{
			// 0. ACCOUNT SYNC CHECK (Realtime Only)
			if (State == State.Realtime && Account != null && Position.MarketPosition == MarketPosition.Flat)
			{
				// v1.11.19: Skip orphan check for 2 seconds after position close to avoid false positives
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

							// v1.10.28: Don't flatten overnight positions - user wants them open
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
				if (Position.MarketPosition == MarketPosition.Short)
				{
					EnsureProtection("Short", "Emergency_Short_1", Position.Quantity);
				}
				else if (Position.MarketPosition == MarketPosition.Long)
				{
					EnsureProtection("Long", "Emergency_Long_1", Position.Quantity);
				}
			}
			
			// 2. Ghost State: State thinks we are InPosition, but we are Flat.
			if (Position.MarketPosition == MarketPosition.Flat && currentEntryState == EntryState.PositionActive)
			{
				Log(Time[0] + " SYNC: State is InPosition but MarketPosition is Flat. Resetting to Idle.");
				currentEntryState = EntryState.Idle;
				setupLevelName = "";
			
			// RESET PROTECTION COUNTERS (v1.7.26) - Fix bucket allocation in SYNC path
			protectedTp1Qty = 0;
			protectedTp2Qty = 0;
			protectionOrdersCreated = false; // v1.11.14: Reset flag for next trade
			isProtectionProcessing = false; // v1.13.1: Reset lock
			tradeOriginalQty = 0; // v1.11.23: Reset original trade qty
			tradeOriginalTp1Price = 0; // v1.11.24: Reset original TP prices
			tradeOriginalTp2Price = 0;
			tradeVwapActive = false; // v1.10.31: Reset Trade VWAP
				
				// v1.10.12: Cancel orphan orders before nullifying references
				// This handles cases where SL was manually moved and executed
				// v1.10.17: Also cancel stopOrder (Single-SL architecture v1.9.0+)
				// v1.10.18: More robust cancellation - check for Working, Accepted, or any active state
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
			}
		}
		
		private void DrawStatePanel()
		{
			double accountPnL = 0;
			double sessionPnL = 0;

			try {
				if (Account != null)
					accountPnL = Account.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);

				if (SystemPerformance != null && SystemPerformance.RealTimeTrades != null)
					sessionPnL = SystemPerformance.RealTimeTrades.TradesPerformance.Currency.CumProfit;
			} catch {}

			// v1.10.21: Calculate both local and global risk for display
			double localRiskDisplay = RiskPerTradeUSD;
			if (atr != null && atr[0] > 0)
			{
				double atrInUSD = atr[0] * Instrument.MasterInstrument.PointValue;
				double scaledRisk = atrInUSD * ATRRiskScaleFactor;
				localRiskDisplay = Math.Min(RiskPerTradeUSD, scaledRisk);
				if (localRiskDisplay < 5.0) localRiskDisplay = 5.0;
				
				// Write our risk to shared file (every bar update)
				WriteSharedRisk(localRiskDisplay);
			}
			double globalRiskDisplay = ReadMaxSharedRisk();
			
			// v1.11.20: Calculate minimum risk in USD (what MinQuantity would cost if stopped out)
			double minTickValue = Instrument.MasterInstrument.PointValue * TickSize;
			double minRiskUSD = StopLossTicks * MinQuantity * minTickValue;

			// v1.10.23: Show current level with age
			string levelInfo = "-";
			if (!string.IsNullOrEmpty(setupLevelName) && setupLevelTime != DateTime.MinValue)
			{
				int daysOld = (int)(Time[0].Date - setupLevelTime.Date).TotalDays;
				if (daysOld == 0)
					levelInfo = setupLevelName + " (Today)";
				else if (daysOld == 1)
					levelInfo = setupLevelName + " (1 Day)";
				else
					levelInfo = setupLevelName + " (" + daysOld + " Days)";
				
				// v1.10.26: Show entry attempts as X/Y counter
				if (MaxRetriesPerLevel > 1)
					levelInfo += " " + currentVwapNumber + "/" + MaxRetriesPerLevel;
			}

			// v1.10.35: Build order info string when orders are active
			string orderInfo = "";
			bool hasActiveOrders = (currentEntryState == EntryState.workingOrder || currentEntryState == EntryState.PositionActive);
			
			if (hasActiveOrders)
			{
				double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
				double avgEntry = 0;
				double slPrice = 0;
				// v1.11.24: Use original TP prices if available (don't change when session changes)
				double tp1Price = tradeOriginalTp1Price > 0 ? tradeOriginalTp1Price : activeTp1Price;
				double tp2Price = tradeOriginalTp2Price > 0 ? tradeOriginalTp2Price : activeTp2Price;
				int totalQty = 0;
				
				// Get entry price
				if (entryOrder != null && entryOrder.AverageFillPrice > 0)
					avgEntry = entryOrder.AverageFillPrice;
				else if (entryOrder != null && entryOrder.LimitPrice > 0)
					avgEntry = entryOrder.LimitPrice;
				else if (Position.MarketPosition != MarketPosition.Flat)
					avgEntry = Position.AveragePrice;
				
				// Get quantity (v1.11.23: Use tradeOriginalQty if available for consistent display)
				if (tradeOriginalQty > 0)
					totalQty = tradeOriginalQty; // Use original qty for panel calculations
				else if (Position.MarketPosition != MarketPosition.Flat)
					totalQty = Math.Abs(Position.Quantity);
				else if (entryOrder != null)
					totalQty = entryOrder.Quantity;
				
				// Calculate SL price
				slPrice = isShortSetup ? (setupAnchorPrice + TickSize) : (setupAnchorPrice - TickSize);
				
				if (avgEntry > 0 && slPrice > 0 && totalQty > 0)
				{
					// Calculate risk
					double riskTicks = Math.Abs(avgEntry - slPrice) / TickSize;
					double riskUSD = riskTicks * tickValue * totalQty;
					
					// Calculate TP1 reward
					double tp1RewardTicks = 0;
					double tp1RewardUSD = 0;
					double tp1RR = 0;
					if (tp1Price > 0)
					{
						tp1RewardTicks = Math.Abs(tp1Price - avgEntry) / TickSize;
						tp1RewardUSD = tp1RewardTicks * tickValue * ((totalQty + 1) / 2); // TP1 gets ~50%
						tp1RR = riskTicks > 0 ? tp1RewardTicks / riskTicks : 0;
					}
					
					// Calculate TP2 reward
					double tp2RewardTicks = 0;
					double tp2RewardUSD = 0;
					double tp2RR = 0;
					if (tp2Price > 0)
					{
						tp2RewardTicks = Math.Abs(tp2Price - avgEntry) / TickSize;
						tp2RewardUSD = tp2RewardTicks * tickValue * (totalQty / 2); // TP2 gets ~50%
						tp2RR = riskTicks > 0 ? tp2RewardTicks / riskTicks : 0;
					}
					
					// Build order info lines
					orderInfo = string.Format("\n─────────────────\nSL: -${0:F0} ({1:F0}t)\nTP1: +${2:F0} R={3:F1}\nTP2: +${4:F0} R={5:F1}",
						riskUSD, riskTicks,
						tp1RewardUSD, tp1RR,
						tp2RewardUSD, tp2RR);
				}
			}

			string text = string.Format("Ver: {0}\nState: {1}\nLevel: {2}\nPosition: {3}\nPnL: {4} | Risk: {5:C0} (Min: {6:C0}){7}",
				StrategyVersion,
				currentEntryState,
				levelInfo,
				Position.MarketPosition,
				sessionPnL.ToString("C"),
				globalRiskDisplay,
				minRiskUSD,
				orderInfo);
				
			// v1.14.9: UI Polish - Black Background 50%
			Draw.TextFixed(this, "InfoPanel", text, TextPosition.TopRight, Brushes.White, new SimpleFont("Arial", 12), Brushes.Black, Brushes.Black, 50);
			
			if (gapDetected || gapCount > 0)
			{
				string msg = "GAP DETECTED";
				if (gapCount > 0) msg = "ALERTA: FALTAN DIAS\n" + gapCount + " NIVELES OCULTOS\nCARGA MAS HISTORIAL";
				
				// Increased padding to roughly 12 lines to clear the InfoPanel
				Draw.TextFixed(this, "GapWarning", "\n\n\n\n\n\n\n\n\n\n\n\n" + msg, TextPosition.TopRight, Brushes.Red, new SimpleFont("Arial", 12) { Bold = true }, Brushes.Transparent, Brushes.Transparent, 100);
			}
			
			// v1.11.17: Lag Alert - Yellow text warning when chart has excessive lag
			if (isLagAlertActive)
			{
				string lagMsg = string.Format("⚠️ LAG: {0:F1}s - ORDERS BLOCKED", currentChartLag);
				Draw.TextFixed(this, "LagAlert", "\n\n\n\n\n\n\n" + lagMsg, TextPosition.TopRight, Brushes.Yellow, new SimpleFont("Arial", 14) { Bold = true }, Brushes.Transparent, Brushes.Transparent, 100);
			}
			else
			{
				RemoveDrawObject("LagAlert"); // Clear when no lag
			}
		}
		
		// -------------------------------------------------------------------------
		// v1.12.1: CONTROL BUTTONS (DIRECTION + CLOSE) - Bottom Right
		// -------------------------------------------------------------------------
		private void InitializeControlButtons()
		{
			if (buttonsInitialized || ChartControl == null) return;
			
			ChartControl.Dispatcher.InvokeAsync(() =>
			{
				try
				{
					// Panel horizontal para botones - ABAJO A LA DERECHA
					buttonPanel = new System.Windows.Controls.StackPanel();
					buttonPanel.Orientation = Orientation.Horizontal;
					buttonPanel.HorizontalAlignment = HorizontalAlignment.Right;
					buttonPanel.VerticalAlignment = VerticalAlignment.Bottom;
					buttonPanel.Margin = new Thickness(0, 0, 10, 10);
					
					// v1.12.1: Solo 2 botones
					btnPause = CreateControlButton("↕ AMBOS", Brushes.ForestGreen); // Direction button
					btnClose = CreateControlButton("✖ CLOSE", Brushes.Crimson);
					
					// Eventos
					btnPause.Click += OnDirectionClick; // Renamed from OnPauseClick
					btnClose.Click += OnCloseClick;
					
					buttonPanel.Children.Add(btnPause);
					buttonPanel.Children.Add(btnClose);
					
					UserControlCollection.Add(buttonPanel);
					buttonsInitialized = true;
					Log(Time[0] + " CONTROL BUTTONS: Initialized (Bottom Right)");
				}
				catch (Exception ex)
				{
					Log(Time[0] + " CONTROL BUTTONS ERROR: " + ex.Message);
				}
			});
		}
		
		private System.Windows.Controls.Button CreateControlButton(string text, Brush bgColor)
		{
			var btn = new System.Windows.Controls.Button();
			btn.Content = text;
			btn.Width = 85;
			btn.Height = 24;
			btn.Margin = new Thickness(3);
			btn.Background = bgColor;
			btn.Foreground = Brushes.White;
			btn.FontWeight = FontWeights.Bold;
			btn.FontSize = 11;
			btn.BorderThickness = new Thickness(0);
			return btn;
		}
		
		// v1.12.1: Single direction button cycles: AMBOS → LONG → SHORT → NINGUNO → AMBOS
		private void OnDirectionClick(object sender, RoutedEventArgs e)
		{
			switch (currentTradingMode)
			{
				case TradingMode.Normal:
					currentTradingMode = TradingMode.LongOnly;
					break;
				case TradingMode.LongOnly:
					currentTradingMode = TradingMode.ShortOnly;
					break;
				case TradingMode.ShortOnly:
					currentTradingMode = TradingMode.Paused; // NINGUNO
					break;
				case TradingMode.Paused:
					currentTradingMode = TradingMode.Normal; // AMBOS
					break;
			}
			Log(Time[0] + " CONTROL: Mode = " + currentTradingMode);
			UpdateButtonStates();
		}
		
		private void OnCloseClick(object sender, RoutedEventArgs e)
		{
			ClosePositionManual();
		}
		
		private void ClosePositionManual()
		{
			if (Position.MarketPosition == MarketPosition.Flat)
			{
				Log(Time[0] + " MANUAL CLOSE: No position to close");
				return;
			}
			
			int qty = Math.Abs(Position.Quantity);
			
			try
			{
				// Cancel existing orders first
				if (stopOrder != null && (stopOrder.OrderState == OrderState.Working || stopOrder.OrderState == OrderState.Accepted))
				{
					CancelOrder(stopOrder);
					Log(Time[0] + " MANUAL CLOSE: Cancelled SL");
				}
				if (tp1Order != null && (tp1Order.OrderState == OrderState.Working || tp1Order.OrderState == OrderState.Accepted))
				{
					CancelOrder(tp1Order);
					Log(Time[0] + " MANUAL CLOSE: Cancelled TP1");
				}
				if (tp2Order != null && (tp2Order.OrderState == OrderState.Working || tp2Order.OrderState == OrderState.Accepted))
				{
					CancelOrder(tp2Order);
					Log(Time[0] + " MANUAL CLOSE: Cancelled TP2");
				}
				
				// Close position
				if (Position.MarketPosition == MarketPosition.Long)
					SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.Market, qty, 0, 0, "", "ManualClose_Long");
				else
					SubmitOrderUnmanaged(0, OrderAction.BuyToCover, OrderType.Market, qty, 0, 0, "", "ManualClose_Short");
				
				Log(Time[0] + " MANUAL CLOSE SUBMITTED: Qty=" + qty);
				currentEntryState = EntryState.Idle;
				setupLevelName = "";
			}
			catch (Exception ex)
			{
				Log(Time[0] + " MANUAL CLOSE FAILED: " + ex.Message);
			}
		}
		
		private void UpdateButtonStates()
		{
			ChartControl?.Dispatcher.InvokeAsync(() =>
			{
				if (btnPause == null) return;
				
				// v1.12.1: Direction button shows current mode
				switch (currentTradingMode)
				{
					case TradingMode.Normal:
						btnPause.Content = "↕ AMBOS";
						btnPause.Background = Brushes.ForestGreen;
						break;
					case TradingMode.LongOnly:
						btnPause.Content = "↑ LONG";
						btnPause.Background = Brushes.DodgerBlue;
						break;
					case TradingMode.ShortOnly:
						btnPause.Content = "↓ SHORT";
						btnPause.Background = Brushes.OrangeRed;
						break;
					case TradingMode.Paused:
						btnPause.Content = "⏸ NINGUNO";
						btnPause.Background = Brushes.Gray;
						break;
				}
			});
		}
		
		private void CleanupControlButtons()
		{
			if (ChartControl == null) return;
			
			ChartControl.Dispatcher.InvokeAsync(() =>
			{
				try
				{
					if (btnPause != null) btnPause.Click -= OnDirectionClick;
					if (btnClose != null) btnClose.Click -= OnCloseClick;
					
					if (buttonPanel != null && UserControlCollection.Contains(buttonPanel))
						UserControlCollection.Remove(buttonPanel);
				}
				catch { }
			});
		}
		
		// -------------------------------------------------------------------------
		// DYNAMIC POSITION SIZING (v1.8.0) + ATR Risk Scaling (v1.10.20)
		// -------------------------------------------------------------------------
		private int CalculateDynamicQuantity(double entryPrice, double stopPrice)
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
			
			// v1.10.20: RIESGO DINAMICO BASADO EN ATR
			// Escalar el riesgo objetivo según la volatilidad actual
			// ATR alto (volatilidad) = usar RiskPerTradeUSD completo
			// ATR bajo (calma) = reducir riesgo proporcionalmente
			double localAtrRisk = RiskPerTradeUSD;
			if (atr != null && atr[0] > 0)
			{
				// ATR-scaled risk: riesgo proporcional al ATR
				double atrInUSD = atr[0] * (Instrument.MasterInstrument.PointValue);
				double scaledRisk = atrInUSD * ATRRiskScaleFactor;
				
				// Usar el MENOR entre el riesgo máximo configurado y el escalado por ATR
				localAtrRisk = Math.Min(RiskPerTradeUSD, scaledRisk);
				
				// Nunca menos de $5 de riesgo
				if (localAtrRisk < 5.0) localAtrRisk = 5.0;
				
				// v1.10.21: Write our risk to shared file
				WriteSharedRisk(localAtrRisk);
			}
			
			// v1.10.21: Read GLOBAL MAX risk from all instruments
			double effectiveRisk = ReadMaxSharedRisk();
			

			
			// Fórmula: Quantity = EffectiveRisk / (Ticks × Value)
			double calculatedQty = effectiveRisk / (riskInTicks * tickValue);
			
			// Redondear a entero
			int quantity = (int)Math.Round(calculatedQty);
			
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
			// v1.12.0: Check trading mode before processing new entries
			if (currentEntryState == EntryState.Idle)
			{
				// If paused, don't look for new setups
				if (currentTradingMode == TradingMode.Paused)
					return;
				
				// Check direction filter for new entries
				if (currentTradingMode == TradingMode.LongOnly && isShortSetup)
					return; // Skip short setups
				if (currentTradingMode == TradingMode.ShortOnly && !isShortSetup)
					return; // Skip long setups
			}
			
			// v1.10.26: VWAP MITIGATION RETRY DETECTION
			if (currentEntryState == EntryState.WaitingForVwapMitigation && waitingForVwapMitigation)
			{
				bool mitigated = false;
				
				// LONG: price must break below -> new low
				if (!isShortSetup && Low[0] < vwapCandleExtreme - TickSize)
					mitigated = true;
				
				// SHORT: price must break above -> new high
				if (isShortSetup && High[0] > vwapCandleExtreme + TickSize)
					mitigated = true;
				
				if (mitigated)
				{
					// Re-anchor VWAP from this new extreme
					double newAnchor = isShortSetup ? High[0] : Low[0];
					setupAnchorPrice = newAnchor;
					
					// Reset VWAP from new anchor
					double price = Close[0];
					if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
					else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;
					
					adhocVolSum = Volume[0];
					adhocPvSum = Volume[0] * price;
					adhocLastBar = CurrentBar;
					adhocLastVol = Volume[0];
					adhocAnchorBar = CurrentBar;
					
					// Reset Visual
					visualAdhocPrevBarVal = price;
					visualAdhocLastVal = price;
					visualAdhocLastBar = -1;
					
					// Transition to WaitingForConfirmation
					currentEntryState = EntryState.WaitingForConfirmation;
					waitingForVwapMitigation = false;
					
					Log(string.Format("{0} VWAP#{1} CREATED @ {2:F2} - Ready for entry",
						Time[0], currentVwapNumber, newAnchor));
				}
				
				return; // Don't proceed with other logic while waiting
			}
			
			// 1. TRIGGER DETECTION (Transition from Idle -> Waiting OR Switch Setup)
			// Allow scanning for triggers if Idle OR Waiting (to switch setups).
			bool canScan = (currentEntryState == EntryState.Idle || currentEntryState == EntryState.WaitingForConfirmation);
			
			// Always Update ADHOC VWAP if we are in a setup based on it
			// Wait... we need to accumulate ONLY after trigger? Or always?
			// User wants "Ends when touched". So we accumulate FROM Trigger.
			if (currentEntryState == EntryState.WaitingForConfirmation || currentEntryState == EntryState.workingOrder)
			{
				UpdateAdhocVWAP();
			
			//v1.10.0: PHASE 3 - RE-ANCHORING (Internal levels behave like external)
			// Both internal AND external levels should re-anchor when price breaks the anchor
			// SHORT: Re-anchor if price makes new high
			if (isShortSetup && High[0] >= setupAnchorPrice + TickSize)
			{
				setupAnchorPrice = High[0];
				
				// Reset VWAP from new anchor
				double price = Close[0];
				if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
				else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;
				
				adhocVolSum = Volume[0];
				adhocPvSum = Volume[0] * price;
				adhocLastBar = CurrentBar;
				adhocLastVol = Volume[0];
				adhocAnchorBar = CurrentBar; // v1.10.11: Track for retroactive update
				
				// Reset Visual				
				visualAdhocPrevBarVal = price;
				visualAdhocLastVal = price;
				visualAdhocLastBar = -1;
				
				Log(string.Format("RE-ANCHOR: New High @ {0} (Setup: {1})", setupAnchorPrice, setupLevelName));
			}
			
			// LONG: Re-anchor if price makes new low
			if (!isShortSetup && Low[0] <= setupAnchorPrice - TickSize)
			{
				setupAnchorPrice = Low[0];
				
				// Reset VWAP from new anchor
				double price = Close[0];
				if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
				else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;
				
				adhocVolSum = Volume[0];
				adhocPvSum = Volume[0] * price;
				adhocLastBar = CurrentBar;
				adhocLastVol = Volume[0];
				adhocAnchorBar = CurrentBar; // v1.10.11: Track for retroactive update
				
				// Reset Visual
				visualAdhocPrevBarVal = price;
				visualAdhocLastVal = price;
				visualAdhocLastBar = -1;
				
				Log(string.Format("RE-ANCHOR: New Low @ {0} (Setup: {1})", setupAnchorPrice, setupLevelName));
			}
			
			// v1.10.0: PHASE 4 - INVALIDATION (If internal level touches external)
			if (isInternalLevel && currentEntryState == EntryState.WaitingForConfirmation)
			{
				bool touchedExternal = false;
				
				// SHORT internal: Check if touched external High above
				if (isShortSetup && externalLevelAbove > 0)
				{
					if (High[0] >= externalLevelAbove)
					{
						touchedExternal = true;
						Log(string.Format("INVALIDATED: Touched external {0} @ {1}", externalLevelAboveName, externalLevelAbove));
					}
				}
				
				// LONG internal: Check if touched external Low below
				if (!isShortSetup && externalLevelBelow > 0)
				{
					if (Low[0] <= externalLevelBelow)
					{
						touchedExternal = true;
						Log(string.Format("INVALIDATED: Touched external {0} @ {1}", externalLevelBelowName, externalLevelBelow));
					}
				}
				
				if (touchedExternal)
				{
					// v1.10.1: Mark bar to prevent re-triggering (infinite loop fix)
					lastInvalidationBar = CurrentBar;
					
					// Cancel entry order if exists
					if (entryOrder != null && (entryOrder.OrderState == OrderState.Working || entryOrder.OrderState == OrderState.Accepted))
					{
						CancelOrder(entryOrder);
					}
					
					// Reset to Idle
					currentEntryState = EntryState.Idle;
					isInternalLevel = false;
					
					// v1.10.2: AUTO-TRIGGER on external level after invalidation
					// The external level was touched, so it should become the new setup
					string externalName = isShortSetup ? externalLevelAboveName : externalLevelBelowName;
					double externalPrice = isShortSetup ? externalLevelAbove : externalLevelBelow;
					
					if (externalPrice > 0 && !string.IsNullOrEmpty(externalName))
					{
						Log(string.Format("AUTO-TRIGGER: Switching to external level {0} @ {1}", externalName, externalPrice));
						
						// Setup new trigger on external level
						if (isShortSetup)
						{
							// SHORT on external High
							triggerTag = "TriggerShort_" + Time[0].Ticks;
							triggerBar = CurrentBar;
							DrawTriggerLabel(triggerTag, true, 0, High[0]);
							
							currentEntryState = EntryState.WaitingForConfirmation;
							visualConfirmationDone = false; // Reset visual flag
							isShortSetup = true;
							setupAnchorPrice = High[0]; // Current extreme
							setupLevelName = externalName;
							setupLevelTime = Time[0]; // Use current time as reference
							validatedTargetPrice = 0;
							cachedOppositeLevel = null;
							
							// NO call DetectInternalLevel again (external is not internal)
							isInternalLevel = false;
							
							// Reset VWAP
							double price = Close[0];
							if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
							else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;
							
							adhocVolSum = Volume[0];
							adhocPvSum = Volume[0] * price;
							adhocLastBar = CurrentBar;
							adhocLastVol = Volume[0];
							adhocAnchorBar = CurrentBar; // v1.10.11: Track for retroactive update
							
							visualAdhocPrevBarVal = price;
							visualAdhocLastVal = price;
							visualAdhocLastBar = -1;
						}
						else
						{
							// LONG on external Low
							triggerTag = "TriggerLong_" + Time[0].Ticks;
							triggerBar = CurrentBar;
							DrawTriggerLabel(triggerTag, false, 0, Low[0]);
							
							currentEntryState = EntryState.WaitingForConfirmation;
							visualConfirmationDone = false; // Reset visual flag
							isShortSetup = false;
							setupAnchorPrice = Low[0];
							setupLevelName = externalName;
							setupLevelTime = Time[0];
							validatedTargetPrice = 0;
							cachedOppositeLevel = null;
							
							isInternalLevel = false;
							
							// Reset VWAP
							double price = Close[0];
							if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
							else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;
							
							adhocVolSum = Volume[0];
							adhocPvSum = Volume[0] * price;
							adhocLastBar = CurrentBar;
							adhocLastVol = Volume[0];
							adhocAnchorBar = CurrentBar; // v1.10.11: Track for retroactive update
							
							visualAdhocPrevBarVal = price;
							visualAdhocLastVal = price;
							visualAdhocLastBar = -1;
						}
					}
					
					// Note: Could optionally trigger new A+ setup on external level here
				}
			}
				
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
					if (visualAdhocLastBar != -1 && visualAdhocPrevBarVal > 0)
					{
						string lineTag = "AdhocLine_" + CurrentBar;
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
			
			if (canScan)
			{
				// LOOP PROTECTION: If rejected OR invalidated this bar, DO NOT scan again.
				if (CurrentBar == lastRejectionBar || CurrentBar == lastInvalidationBar) return;
				
				// v1.10.28: FRESH SIGNAL ONLY - Don't trigger on historical setups
				// Wait for a new trigger AFTER strategy is active in Realtime
				if (State == State.Realtime && realtimeStartBar > 0 && CurrentBar <= realtimeStartBar)
				{
					// We are on the same bar where we started - don't trigger on inherited setup
					return;
				}

				foreach (var lvl in activeLevels)
				{
					// BACKTEST SAFETY: Ignore Future Levels (Cheat Prevention)
					if (lvl.StartTime > Time[0]) continue;

					// v1.10.24: Ignore Same-Day Levels (still forming, not closed)
					// Only trade levels from PREVIOUS days that are still active
					if (lvl.StartTime.Date == Time[0].Date)
						continue;

					// v1.10.25: Check if max retries exceeded for this level
					if (lvl.EntryAttempts >= MaxRetriesPerLevel)
						continue;
					
					// v1.10.29: Skip levels that were already being touched at startup
					// These are "spent" and we need a fresh level
					if (skippedLevelsAtStartup.Contains(lvl.Name))
						continue;

					// If level is mitigated exactly NOW
					// Note: ManageLevels sets MitigationTime = Time[0].
					if (lvl.IsMitigated && lvl.MitigationTime == Time[0])
					{
						// If we are already waiting, check if this is a DIFFERENT level.
						// If it's the same level, we ignore re-triggering to preserve the 'setupAnchorPrice' (Extreme).
						if (currentEntryState == EntryState.WaitingForConfirmation)
						{
							if (lvl.Name == setupLevelName)
								continue;
							else
							{
								// SWITCHING SETUP!
								Log(Time[0] + " SWITCH: New Trigger on " + lvl.Name + " overrides " + setupLevelName);
								// Fall through to process new trigger...
							}
						}
							
						// Valid Trigger (New or Switch)
						
						if (lvl.IsResistance)
						{
					Log(Time[0] + " DEBUG: Trigger Short Detected on " + lvl.Name + " Price: " + lvl.Price);
							// Short Setup
							triggerTag = "TriggerShort_" + Time[0].Ticks; // Store Tag
							triggerBar = CurrentBar;
							DrawTriggerLabel(triggerTag, true, 0, High[0]);
							
							currentEntryState = EntryState.WaitingForConfirmation;
							visualConfirmationDone = false; // Reset visual flag
							isShortSetup = true;
							setupAnchorPrice = High[0]; // ANCHOR START: Current Wick High
							setupLevelName = lvl.Name;
							setupLevelTime = lvl.StartTime; // CAPTURE TIME (v1.5.8)
							validatedTargetPrice = 0; // RESET for new setup
			cachedOppositeLevel = null; // CLEAR CACHE (v1.7.22)
							
							// v1.10.26: Reset retry state for new level
							waitingForVwapMitigation = false;
							currentVwapNumber = 1;
							vwapCandleExtreme = 0;
							
							// v1.10.25: Increment entry attempts
							lvl.EntryAttempts++;
							Log(string.Format("{0} ENTRY ATTEMPT #{1}/{2} on {3}", Time[0], lvl.EntryAttempts, MaxRetriesPerLevel, lvl.Name));
							
							// v1.10.0: Detect if this is an internal level
							DetectInternalLevel(lvl, activeLevels);
							
							// RESET ADHOC VWAP (Start Fresh from this touch)
							// ALIGNMENT: To match Global VWAP behavior, we must Include the Trigger Bar's volume completely.
							double price = Close[0];
							if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
							else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;

							adhocVolSum = Volume[0]; 
							adhocPvSum = Volume[0] * price;
							adhocLastBar = CurrentBar;
							adhocLastVol = Volume[0]; // So Delta next tick in same bar is 0, but we already have base volume.
							adhocAnchorBar = CurrentBar; // v1.10.11: Track for retroactive update
							
							// Reset Visual State
							visualAdhocPrevBarVal = price;
							visualAdhocLastVal = price;
							visualAdhocLastBar = -1;
						}
						else
						{
					Log(Time[0] + " DEBUG: Trigger Long Detected on " + lvl.Name + " Price: " + lvl.Price);
							// Long Setup
							triggerTag = "TriggerLong_" + Time[0].Ticks;
							triggerBar = CurrentBar;
							DrawTriggerLabel(triggerTag, false, 0, Low[0]);
							
							currentEntryState = EntryState.WaitingForConfirmation;
							visualConfirmationDone = false; // Reset visual flag
							isShortSetup = false; // Long
							setupAnchorPrice = Low[0]; // ANCHOR START: Current Wick Low
							setupLevelName = lvl.Name;
							setupLevelTime = lvl.StartTime; // CAPTURE TIME (v1.5.8)
							validatedTargetPrice = 0; // RESET for new setup
			cachedOppositeLevel = null; // CLEAR CACHE (v1.7.22)
							
							// v1.10.26: Reset retry state for new level
							waitingForVwapMitigation = false;
							currentVwapNumber = 1;
							vwapCandleExtreme = 0;
							
							// v1.10.25: Increment entry attempts
							lvl.EntryAttempts++;
							Log(string.Format("{0} ENTRY ATTEMPT #{1}/{2} on {3}", Time[0], lvl.EntryAttempts, MaxRetriesPerLevel, lvl.Name));
							
							// v1.10.0: Detect if this is an internal level
							DetectInternalLevel(lvl, activeLevels);
							
							// RESET ADHOC VWAP
							double price = Close[0];
							if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
							else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;

							adhocVolSum = Volume[0]; 
							adhocPvSum = Volume[0] * price;
							adhocLastBar = CurrentBar;
							adhocLastVol = Volume[0];
							adhocAnchorBar = CurrentBar; // v1.10.11: Track for retroactive update

							// Reset Visual State
							visualAdhocPrevBarVal = price;
							visualAdhocLastVal = price;
							visualAdhocLastBar = -1;
						}
						
						break; // Only take one trigger at a time
					}
				}
			}
			
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

				// WICK GROWTH (Mid-Bar during Trigger)
				// We allow the anchor to expand while we form the trigger candle.
				if (isShortSetup && High[0] > setupAnchorPrice) setupAnchorPrice = High[0];
				if (!isShortSetup && Low[0] < setupAnchorPrice) setupAnchorPrice = Low[0];
			}
			
			// 2. CONFIRMATION LOGIC (Waiting -> Working)
			// "Wait for a candle... close... max below vwap 1 tick"
			
			if (currentEntryState == EntryState.WaitingForConfirmation && IsFirstTickOfBar && CurrentBar > triggerBar)
			{
				// Determine Local VWAP to use
				double setupVWAP = GetSetupVWAP(isShortSetup);
				
				if (isShortSetup)
				{
					// Short: High[1] < Bearish VWAP (setupVWAP) - 1 Tick
					if (isValidVWAP(setupVWAP) && High[1] < (setupVWAP - TickSize))
					{
						// ... (Trigger logic) ...

						// --- RISK / REWARD CHECK ---
						double projectedEntry = setupVWAP;
						Log(string.Format("{0} | DEBUG_ENTRY: Calling GetOppositeLevelPrice. SetupName='{1}' SetupTime='{2}' RefPrice='{3}'", Time[0], setupLevelName, setupLevelTime, setupAnchorPrice));
						// Padding: Stop is placed 1 tick ABOVE the anchor for breathing room.
						double projectedStop = setupAnchorPrice + TickSize; 
						
						
						// VALIDATE R/R (v1.7.28) - Continuous validation
						double risk, reward, ratio;
						bool isValidRR = ValidateRiskReward(true, projectedEntry, projectedStop, out risk, out reward, out ratio);
						
						if (isValidRR)
						{
							// CAPTURE TARGET (v1.7.16)
							double tp2Target = GetOppositeLevelPrice(setupLevelName, setupLevelTime, setupAnchorPrice, true);
							if (tp2Target == 0) tp2Target = GetCurrentLowVWAP();
							validatedTargetPrice = tp2Target;

							// EXE DEBUG & ROUNDING (v1.7.1 Fix MGC Exec)
							double limitPrice = Instrument.MasterInstrument.RoundToTickSize(setupVWAP);
							if (EnableDebugLogs)
							{
								// Use Try/Catch for Bid/Ask in case data is missing
								try { Log(string.Format("{0} | EXEC_DEBUG: Submitting Short Limit @ {1} (Raw: {2}). Bid={3} Ask={4}", Time[0], limitPrice, setupVWAP, GetCurrentBid(), GetCurrentAsk())); } catch {}
							}

							// ACCOUNTS FOR 1 Entry -> 1 OCO Group limitation
						// UPDATED (v1.7.30): Allow Historical for Strategy Analyzer
						// FIX (v1.10.36): Block Historical orders on live/demo (RESTORED v1.11.2 - orders were being sent to broker)
						bool isPlayback = (Connection.PlaybackConnection != null);
						bool canSubmitOrder = (State == State.Realtime) || (State == State.Historical && (isPlayback || AllowBacktest));
						// v1.11.11: Highlight confirmation candle (ONLY ONCE)
						// Check visualConfirmationDone flag to avoid painting multiple candles
						if (HighlightConfirmationCandle && CurrentBar > 1 && !visualConfirmationDone)
						{
							BarBrushes[1] = ConfirmationCandleColor;
							CandleOutlineBrushes[1] = ConfirmationCandleColor;
							visualConfirmationDone = true;
						}

						if (canSubmitOrder)
							{
								if (entryOrder != null) 
								{
									Log("WARNING: Entry Order already exists? Overwriting.");
								}
								
								// DYNAMIC SIZING (v1.8.0): Calcular cantidad según riesgo
								int dynamicQuantity = CalculateDynamicQuantity(limitPrice, projectedStop);
								
								// CONSOLIDATED ENTRY (v1.7.17)
								string entryTag = string.Format("EntryA+_Short_{0:D2}", currentVwapNumber);
								
								// v1.11.17: Lag Filter - Block order if chart has lag
								if (!CheckChartLag())
								{
									Log(Time[0] + " Short order BLOCKED due to chart lag");
									return;
								}
								
								entryOrder = SubmitOrderUnmanaged(0, OrderAction.SellShort, OrderType.Limit, dynamicQuantity, limitPrice, 0, "", entryTag);
								currentEntryState = EntryState.workingOrder;
								Log(Time[0] + " Order Submitted (Short Consolidated). Qty=" + dynamicQuantity);
							}
							else
							{
								// If in simple Backtest, we might need default behavior, but for Playback/Live reload fixes:
								// Log(Time[0] + " Trade Signal Valid (Short) but SKIPPED (Historical/Catchup State).");
							}
						}
						else
						{
							Log(Time[0] + string.Format(" Trade Skipped (Short). Risk: {0:F2} Reward: {1:F2} Ratio: {2:F2}", risk, reward, (risk > 0 ? (reward/risk) : 0)));
						}
					}
					else
					{
						// Check invalidation
						// Check invalidation (End of Bar)
						if (High[0] > setupAnchorPrice)
						{
							// DYNAMIC UPDATE: Don't kill the setup, just update the reference High.
							setupAnchorPrice = High[0];
							Log(Time[0] + " Anchor Updated (Short End-Bar) to New High: " + setupAnchorPrice);
							
							// DO NOT RESET VWAP HERE (v1.7.17 Fix)
							// If we reset here, we lose the 'Touch' volume accumulation.
							// We only assume the anchor expanded, but the 'Touch' event is still valid.
							// Unless... does a new High mean the previous touch was invalid?
							// Actually, if we make a new high, we haven't really 'reversed' yet.
							// But resetting 'adhocVolSum' to Volume[0] essentially restarts the VWAP from THIS bar.
							// Maybe that IS correct? "VWAP from the Top".
							// If we keep the old volume, the VWAP will lag behind.
							// Let's Keep it for now, but ensure 'adhocVolSum' is not 0.
						}
					else
					{
						// DEBUG: Why are we waiting?
						if (CurrentBar % 10 == 0) // Limit spam
							Log(string.Format("{0} | WAITING SHORT: High[1]={1:F2} VWAP={2:F2} Req={3:F2} ValidVWAP={4} Anchor={5}", 
								Time[0], High[1], setupVWAP, (setupVWAP - TickSize), isValidVWAP(setupVWAP), setupAnchorPrice));
					}
					}
				}
				else
				{
					// Long: Low[1] > Bullish VWAP (setupVWAP) + 1 Tick
					if (isValidVWAP(setupVWAP) && Low[1] > (setupVWAP + TickSize))
					{
						// --- RISK / REWARD CHECK ---
						double projectedEntry = setupVWAP;
						Log(string.Format("{0} | DEBUG_ENTRY (Long): Calling GetOppositeLevelPrice. SetupName='{1}' SetupTime='{2}' RefPrice='{3}'", Time[0], setupLevelName, setupLevelTime, setupAnchorPrice));
						// Padding: Stop is placed 1 tick BELOW the anchor.
						double projectedStop = setupAnchorPrice - TickSize;
						
						
						// VALIDATE R/R (v1.7.28) - Continuous validation
						double risk, reward, ratio;
						bool isValidRR = ValidateRiskReward(false, projectedEntry, projectedStop, out risk, out reward, out ratio);
						
						if (isValidRR)
						{
							// CAPTURE TARGET (v1.7.16)
							double tp2Target = GetOppositeLevelPrice(setupLevelName, setupLevelTime, setupAnchorPrice, false);
							if (tp2Target == 0) tp2Target = GetCurrentHighVWAP();
							validatedTargetPrice = tp2Target;

							// EXE DEBUG & ROUNDING (v1.7.1 Fix MGC Exec)
							double limitPrice = Instrument.MasterInstrument.RoundToTickSize(setupVWAP);
							if (EnableDebugLogs)
							{
								try { Log(string.Format("{0} | EXEC_DEBUG: Submitting Long Limit @ {1} (Raw: {2}). Bid={3} Ask={4}", Time[0], limitPrice, setupVWAP, GetCurrentBid(), GetCurrentAsk())); } catch {}
							}
							
							// UPDATED (v1.7.30): Allow Historical for Strategy Analyzer
						// FIX (v1.10.36): Block Historical orders on live/demo (RESTORED v1.11.2)
						bool isPlaybackLong = (Connection.PlaybackConnection != null);
						bool canSubmitOrderLong = (State == State.Realtime) || (State == State.Historical && (isPlaybackLong || AllowBacktest));
						// v1.11.11: Highlight confirmation candle (ONLY ONCE)
						if (HighlightConfirmationCandle && CurrentBar > 1 && !visualConfirmationDone)
						{
							BarBrushes[1] = ConfirmationCandleColor;
							CandleOutlineBrushes[1] = ConfirmationCandleColor;
							visualConfirmationDone = true;
						}

						if (canSubmitOrderLong)
							{
								// CONSOLIDATED ENTRY (v1.7.17)
								if (entryOrder != null) 
								{
									Log("WARNING: Entry Order already exists? Overwriting.");
								}
								
								int dynamicQuantity = CalculateDynamicQuantity(limitPrice, projectedStop); // v1.8.0
				string entryTag = string.Format("EntryA+_Long_{0:D2}", currentVwapNumber);
				
				// v1.11.17: Lag Filter - Block order if chart has lag
				if (!CheckChartLag())
				{
					Log(Time[0] + " Long order BLOCKED due to chart lag");
					return;
				}
				
				entryOrder = SubmitOrderUnmanaged(0, OrderAction.Buy, OrderType.Limit, dynamicQuantity, limitPrice, 0, "", entryTag);
								currentEntryState = EntryState.workingOrder;
								Log(Time[0] + " Order Submitted (Long Consolidated). Qty=" + dynamicQuantity);
							}
							else
							{
								// Skip Historical Execution
							}
						}
						else
						{
							Log(Time[0] + string.Format(" Trade Skipped (Long). Risk: {0:F2} Reward: {1:F2} Ratio: {2:F2}", risk, reward, (risk > 0 ? (reward/risk) : 0)));
						}
					}
					else
					{
						// Check invalidation
						// Check invalidation (End of Bar)
						if (Low[0] < setupAnchorPrice)
						{
							// DYNAMIC UPDATE: Don't kill the setup, just update the reference Low.
							setupAnchorPrice = Low[0];
							Log(Time[0] + " Anchor Updated (Long End-Bar) to New Low: " + setupAnchorPrice);
							
							// RESET VWAP Calculation (Start fresh from new low)
							double price = Close[0];
							if (VwapMethod == VwapCalculationMode.Typical) price = (High[0] + Low[0] + Close[0]) / 3.0;
							else if (VwapMethod == VwapCalculationMode.OHLC4) price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;

							adhocVolSum = Volume[0]; 
							adhocPvSum = Volume[0] * price;
							// Keep visual continuity: visualAdhocLastBar = -1; // Removed to allow drop visualization
						}
					}
				}
			}
			
			// Mid-bar check for Anchor Update / Invalidation
			// ONLY if we are PAST the trigger bar (because logic above handles Trigger Bar growth)
			if (currentEntryState == EntryState.WaitingForConfirmation && !IsFirstTickOfBar && CurrentBar > triggerBar)
			{
				if (isShortSetup && High[0] > setupAnchorPrice) 
				{
					// DYNAMIC UPDATE: Don't kill the setup, just update the reference High.
					setupAnchorPrice = High[0];
					// PERFORMANCE OPTIMIZATION: Reduce spam.
					// Log(Time[0] + " Anchor Updated (Short) to New High: " + setupAnchorPrice);
					
					// RESET VWAP Calculation
					adhocVolSum = 0; adhocPvSum = 0;
					// visualAdhocLastBar = -1; 
				}
				if (!isShortSetup && Low[0] < setupAnchorPrice) 
				{
					// DYNAMIC UPDATE: Don't kill the setup, just update the reference Low.
					setupAnchorPrice = Low[0];
					// PERFORMANCE OPTIMIZATION: Reduce spam.
					// Log(Time[0] + " Anchor Updated (Long) to New Low: " + setupAnchorPrice);
					
					// RESET VWAP Calculation
					adhocVolSum = 0; adhocPvSum = 0;
					// visualAdhocLastBar = -1;
				}
			}

			// 3. ORDER MANAGEMENT & SYNC (Working -> InPosition)
			// 3. ORDER MANAGEMENT & SYNC (Working -> InPosition)
			// Handle BOTH orders (1 and 2)

// CONTINUOUS R/R VALIDATION (v1.7.28) - Monitor while order is working
if (currentEntryState == EntryState.workingOrder && entryOrder != null && entryOrder.OrderState == OrderState.Working)
{
double currentEntry = (entryOrder.LimitPrice > 0) ? entryOrder.LimitPrice : Close[0];
double currentStop = isShortSetup ? (setupAnchorPrice + TickSize) : (setupAnchorPrice - TickSize);

double risk, reward, ratio;
bool isStillValid = ValidateRiskReward(isShortSetup, currentEntry, currentStop, out risk, out reward, out ratio);

if (!isStillValid)
{
Log(string.Format("{0} R/R Invalidated While Working. Risk: {1:F2} Reward: {2:F2} Ratio: {3:F2} - Cancelling Order", 
Time[0], risk, reward, ratio));

if (entryOrder != null && entryOrder.OrderState == OrderState.Working)
CancelOrder(entryOrder);

currentEntryState = EntryState.Idle;
setupLevelName = "";
}
}

			if (currentEntryState == EntryState.workingOrder)
			{
				bool anyFilled = false;
				if (entryOrder != null && (entryOrder.OrderState == OrderState.Filled || entryOrder.OrderState == OrderState.PartFilled)) anyFilled = true;

				if (anyFilled)
				{
					Log(Time[0] + " SYNC: Order Filled but State was Working. Forcing InPosition.");
					currentEntryState = EntryState.PositionActive;
					// Note: OnExecutionUpdate handles the specific EnsureProtection calls.
					// This is just a fallback state transition.
				}
				// Tracking the VWAP (Only if still working)
				else 
				{
					// --- SAFETY VALIDATION: ANCHOR BREAK (RELAXED) ---
					// If price moves against us and breaks the Anchor while we are trying to enter, 
					// DO NOT CANCEL immediately to prevent thrashing loops.
					// Let the Stop Loss (which is placed at Anchor) handle it if filled, or let validity logic handle it.
					bool anchorViolated = false;
					if (isShortSetup && High[0] > setupAnchorPrice) anchorViolated = true;
					if (!isShortSetup && Low[0] < setupAnchorPrice) anchorViolated = true;
					
					if (anchorViolated)
					{
						// Log(Time[0] + " WARNING: Anchor Violated while Working Order. Keeping Order active.");
						// if (entryOrder1 != null) CancelOrder(entryOrder1); // DISABLED
						// if (entryOrder2 != null) CancelOrder(entryOrder2); // DISABLED
						// return; 
					}
				
					// Track the SETUP VWAP (Local), not just Global
					double currentVWAP = GetSetupVWAP(isShortSetup);
					
					// --- DYNAMIC RISK / REWARD CHECK ---
					// As VWAP moves, our entry price moves. We must re-validate R/R.
					double projectedEntry = currentVWAP;
					double projectedStop = isShortSetup ? (setupAnchorPrice + TickSize) : (setupAnchorPrice - TickSize);
					double targetPrice = GetOppositeLevelPrice(setupLevelName, setupLevelTime); 
					if (targetPrice == 0) targetPrice = isShortSetup ? GetCurrentLowVWAP() : GetCurrentHighVWAP(); 
					
					double risk = Math.Abs(projectedEntry - projectedStop);
					double reward = Math.Abs(targetPrice - projectedEntry);
					
					// 1. Check Trailing Valid (VWAP still valid?)
					if (!isValidVWAP(currentVWAP))
					{
						Log(Time[0] + " CANCEL: Setup VWAP Invalidated.");
						if (entryOrder != null) CancelOrder(entryOrder);
						// if (entryOrder2 != null) CancelOrder(entryOrder2); // Removed
						return;
					}
					
					// 2. CHECK TARGET TOUCH (v1.14.4)
					// If price already hit the target while we are waiting/chasing, the setup is invalid.
					bool targetTouched = false;
					if (isShortSetup && Low[0] <= targetPrice) targetTouched = true;
					if (!isShortSetup && High[0] >= targetPrice) targetTouched = true;
					
					if (targetTouched)
					{
						Log(string.Format("{0} CANCEL: Target Touched ({1}) before Entry. Setup invalidated.", Time[0], targetPrice));
						if (entryOrder != null) CancelOrder(entryOrder);
						currentEntryState = EntryState.Idle; // Reset
						setupLevelName = "";
						return;
					}

					// 3. CHECK R/R PRESERVATION (STRICT) - Handled above by Strict Validation Block
					// (Relaxed block removed v1.14.3 to enforce strict R/R > 1.0)

					// UPDATE ORDER PRICE (Trailing)
					// Only update if price difference is significant (e.g. 1 tick) to avoid spamming modification
					
					if (entryOrder != null && entryOrder.OrderState == OrderState.Working)
					{
						// v1.10.15: DYNAMIC QUANTITY ADJUSTMENT
						// Recalcular cantidad basada en el stop actual para mantener riesgo constante
						int newQuantity = CalculateDynamicQuantity(currentVWAP, projectedStop);
						
						bool priceChanged = Math.Abs(entryOrder.LimitPrice - currentVWAP) >= TickSize;
						bool quantityChanged = newQuantity != entryOrder.Quantity;
						
						if (priceChanged || quantityChanged)
						{
							double newLimitPrice = priceChanged ? currentVWAP : entryOrder.LimitPrice;
							ChangeOrder(entryOrder, newQuantity, newLimitPrice, 0);
							
							if (quantityChanged)
							{
								Log(string.Format("{0} | DYNAMIC QTY ADJUST: Old={1} New={2} (Stop moved to {3:F2})",
									Time[0], entryOrder.Quantity, newQuantity, projectedStop));
							}
						}
					}
					// Removed entryOrder2 logic
				}
			}

			
			// 4. IN POSITION MANAGEMENT
			ManagePositionExit();
		} // End ManageEntryA_Plus

			// REFACTORED EnsureProtection (v1.7.17) - Consolidated Split Handling
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
				tradeVWAP.VolSum = ethLowVWAP.VolSum;
				tradeVWAP.PvSum = ethLowVWAP.PvSum;
			}
			else
			{
				tradeVWAP.VolSum = ethHighVWAP.VolSum;
				tradeVWAP.PvSum = ethHighVWAP.PvSum;
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
	
	// v1.10.0: Get daily high extreme (for LONG TP2)
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
	
	// v1.10.0: Get daily low extreme (for SHORT TP2)
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
	
	private void SubmitProtectionOrders(string direction, bool isTp1, int qty)
	{
		// v1.9.0: SINGLE-SL ARCHITECTURE
		// Instead of creating SL1 and SL2, we create ONE SL for the entire position
		// TP1 and TP2 remain independent
		
		// v1.10.38: ORPHAN RECOVERY - Check if orders exist in Account but lost reference
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
			
			// v1.11.27: Validate SL is not too far from current price (broker rejection protection)
			// If SL is more than 100 ticks away, use fallback based on StopLossTicks
			double slDistanceTicks = Math.Abs(slPrice - lastPrice) / TickSize;
			if (slDistanceTicks > 100)
			{
				double fallbackSL = avgEntry + (StopLossTicks * TickSize);
				Log(string.Format("SL DISTANCE WARNING: Original SL {0} is {1:F0} ticks away. Using fallback {2}", slPrice, slDistanceTicks, fallbackSL));
				slPrice = fallbackSL;
			} 

			// v1.10.31: Use Trade VWAP if active (continues accumulating even on day change)
			if (tradeVwapActive)
				targetGlobalVWAP = tradeVWAP.CurrentValue;
			else
				targetGlobalVWAP = GetCurrentLowVWAP(); 
			
			if (cachedOppositeLevel != null) targetZoneOpposite = cachedOppositeLevel.Price;
			else targetZoneOpposite = GetOppositeLevelPrice(setupLevelName, setupLevelTime);

			if (validatedTargetPrice > 0) 
			{
				targetZoneOpposite = validatedTargetPrice;
				Log("FORCE TARGET: Using Validated Price: " + validatedTargetPrice);
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
			
			// v1.11.27: Validate SL is not too far from current price (broker rejection protection)
			// If SL is more than 100 ticks away, use fallback based on StopLossTicks
			double slDistanceTicksLong = Math.Abs(slPrice - lastPrice) / TickSize;
			if (slDistanceTicksLong > 100)
			{
				double fallbackSLLong = avgEntry - (StopLossTicks * TickSize);
				Log(string.Format("SL DISTANCE WARNING: Original SL {0} is {1:F0} ticks away. Using fallback {2}", slPrice, slDistanceTicksLong, fallbackSLLong));
				slPrice = fallbackSLLong;
			} 

			// v1.10.31: Use Trade VWAP if active (continues accumulating even on day change)
			if (tradeVwapActive)
				targetGlobalVWAP = tradeVWAP.CurrentValue;
			else
				targetGlobalVWAP = GetCurrentHighVWAP(); 

			if (cachedOppositeLevel != null) targetZoneOpposite = cachedOppositeLevel.Price;
			else targetZoneOpposite = GetOppositeLevelPrice(setupLevelName, setupLevelTime);
			
			if (validatedTargetPrice > 0) 
			{
				targetZoneOpposite = validatedTargetPrice;
				Log("FORCE TARGET: Using Validated Price: " + validatedTargetPrice);
			}

			if (targetZoneOpposite <= avgEntry) targetZoneOpposite = 0; // Invalid Long Target
			if (targetGlobalVWAP <= avgEntry) targetGlobalVWAP = 0; // Invalid Long Target

			if (targetZoneOpposite <= 0) targetZoneOpposite = avgEntry + fallbackTargetDist;
			if (targetGlobalVWAP <= 0) targetGlobalVWAP = avgEntry + fallbackTargetDist;
		}
		
		if (targetGlobalVWAP <= 0) targetGlobalVWAP = avgEntry;
		if (targetZoneOpposite <= 0) targetZoneOpposite = avgEntry;

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
		Log(string.Format("TP CALC ({0}): Entry={1} | GlobalVWAP={2} | ZoneOpp={3} (Val={4}) | TP1={5} TP2={6} | Selected={7}",
			direction, avgEntry, targetGlobalVWAP, targetZoneOpposite, validatedTargetPrice, tp1Price, tp2Price, myTpPrice));

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
				
				stopOrder = SubmitOrderUnmanaged(0, slAction, OrderType.StopMarket, totalPositionQty, 0,slPrice, "", slTag);
				slOrderCreatedThisEntry = true; // v1.13.5: Mark SL as created
				
				// v1.13.12: Calculate and store risk in USD for R:R analysis
				tradeRiskUSD = Math.Abs(avgEntry - slPrice) * totalPositionQty * Instrument.MasterInstrument.PointValue;
				
				Log(string.Format("SL CREATED: {0} @ {1} Qty={2} Risk=${3:F2}", slTag, slPrice, totalPositionQty, tradeRiskUSD));
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
			bool tpAlreadyActive = (currentTP != null && 
				(currentTP.OrderState == OrderState.Working || 
				 currentTP.OrderState == OrderState.Accepted ||
				 currentTP.OrderState == OrderState.Submitted));
			
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
		
	// v1.10.0: Detect if setup level is INTERNAL (contained within external levels)
	private void DetectInternalLevel(SessionLevel setupLevel, List<SessionLevel> allLevels)
	{
		// Reset state
		isInternalLevel = false;
		externalLevelAbove = 0;
		externalLevelBelow = 0;
		externalLevelAboveName = "";
		externalLevelBelowName = "";
		
		if (setupLevel == null || allLevels == null) return;
		
		// For SHORT setups (High resistance): find external High above
		if (setupLevel.IsResistance)
		{
			externalLevelAbove = FindExternalLevelAbove(setupLevel, allLevels);
			if (externalLevelAbove >0)
			{
				isInternalLevel = true;
				Log(string.Format("INTERNAL LEVEL: {0} @ {1} (External above: {2} @ {3})",
					setupLevel.Name, setupLevel.Price, externalLevelAboveName, externalLevelAbove));
			}
			
			// Also find external Low below (for TP2 context)
			externalLevelBelow = FindExternalLevelBelow(setupLevel, allLevels);
		}
		// For LONG setups (Low support): find external Low below
		else
		{
			externalLevelBelow = FindExternalLevelBelow(setupLevel, allLevels);
			if (externalLevelBelow > 0)
			{
				isInternalLevel = true;
				Log(string.Format("INTERNAL LEVEL: {0} @ {1} (External below: {2} @ {3})",
					setupLevel.Name, setupLevel.Price, externalLevelBelowName, externalLevelBelow));
			}
			
			// Also find external High above (for TP2 context)
			externalLevelAbove = FindExternalLevelAbove(setupLevel, allLevels);
		}
	}
	
	// v1.10.3 CORRECTED: Find HIGHEST High of the day (daily extreme) from different session
	// For SHORT: Level is internal if there's a higher High from another session
	private double FindExternalLevelAbove(SessionLevel currentLevel, List<SessionLevel> allLevels)
	{
		double highestExternal = 0;
		string highestName = "";
		
		foreach (var level in allLevels)
		{
			// Only consider High levels (resistances) above current
			if (!level.IsResistance) continue;
			if (level.Price <= currentLevel.Price) continue;
			
			// Skip if same session (we want EXTERNAL, not same session)
			string currentSession = GetSessionName(currentLevel.Name);
			string candidateSession = GetSessionName(level.Name);
			if (currentSession == candidateSession) continue;
			
			// Find HIGHEST High (daily extreme), not closest
			if (level.Price > highestExternal)
			{
				highestExternal = level.Price;
				highestName = level.Name;
			}
		}
		
		if (highestExternal > 0)
		{
			externalLevelAboveName = highestName;
		}
		
		return highestExternal;
	}
	
	// v1.10.3 CORRECTED: Find LOWEST Low of the day (daily extreme) from different session
	// For LONG: Level is internal if there's a lower Low from another session
	private double FindExternalLevelBelow(SessionLevel currentLevel, List<SessionLevel> allLevels)
	{
		double lowestExternal = 0;
		string lowestName = "";
		
		foreach (var level in allLevels)
		{
			// Only consider Low levels (supports) below current
			if (level.IsResistance) continue;
			if (level.Price >= currentLevel.Price) continue;
			
			// Skip if same session
			string currentSession = GetSessionName(currentLevel.Name);
			string candidateSession = GetSessionName(level.Name);
			if (currentSession == candidateSession) continue;
			
			// Find LOWEST Low (daily extreme), not closest
			if (lowestExternal == 0 || level.Price < lowestExternal)
			{
				lowestExternal = level.Price;
				lowestName = level.Name;
			}
		}
		
		if (lowestExternal > 0)
		{
			externalLevelBelowName = lowestName;
		}
		
		return lowestExternal;
	}
	
	// v1.10.0: Extract session name from level name (e.g., "Asia High" -> "Asia")
	private string GetSessionName(string levelName)
	{
		if (levelName.Contains("Asia")) return "Asia";
		if (levelName.Contains("Europe")) return "Europe";
		if (levelName.Contains("USA")) return "USA";
		return "";
	}
		
	// CORRECTED (v1.7.22): Search for opposite level from SAME DAY (not same hour)
	private double GetOppositeLevelPrice(string name, DateTime refTime, double refPrice = 0, bool expectLower = false)
	{
		// OPTIMIZATION (v1.7.3): Return Cached Price if available
		if (cachedOppositeLevel != null) return cachedOppositeLevel.Price;

		// Try to find the opposite.
		if (string.IsNullOrEmpty(name)) return 0;
		
		string oppName = "";
		if (name.Contains(" Low")) oppName = name.Replace(" Low", " High");
		else if (name.Contains(" High")) oppName = name.Replace(" High", " Low");
		else return 0; // Can't guess
		
		// DEBUG (v1.7.22): Log búsqueda
		Log(string.Format("{0} | SEARCH_OPPOSITE: Looking for '{1}' from SAME DAY as '{2}' (RefDate: {3:yyyy-MM-dd})", Time[0], oppName, name, refTime.Date));
		
		// Perform Scan - SAME DAY (matching Date only, ignore time)
		SessionLevel foundLvl = null;
		int candidatesFound = 0;
		int rejectedByDate = 0;
		
		foreach(var l in activeLevels)
		{
			bool nameMatch = l.Name.Trim().Equals(oppName.Trim(), StringComparison.OrdinalIgnoreCase);
			if (nameMatch) {
				candidatesFound++;
				
				// Compare DATES only (ignore time of day)
				bool sameDay = (l.StartTime.Date == refTime.Date);
				
				// DEBUG: Log candidato
				Log(string.Format("   -> Candidate #{0}: {1} @ {2:F2} (Date: {3:yyyy-MM-dd}, SameDay: {4})", candidatesFound, l.Name, l.Price, l.StartTime.Date, sameDay));
				
				// SAME DAY CHECK: High and Low must be from same calendar day
				if (sameDay)
				{
					foundLvl = l;
					Log(string.Format("   -> ACCEPTED (Same Day): {0} @ {1:F2}", l.Name, l.Price));
					break;
				}
				else
				{
					rejectedByDate++;
					Log(string.Format("   -> REJECTED (Different Day): {0:yyyy-MM-dd} != {1:yyyy-MM-dd}", l.StartTime.Date, refTime.Date));
				}
			}
		}
		
		if (foundLvl != null)
		{
			cachedOppositeLevel = foundLvl; // Cache it!
			return foundLvl.Price;
		}
		
		// DEBUG: Summary if not found
		Log(string.Format("{0} | OPPOSITE NOT FOUND: '{1}' from same day (Found {2} candidates, {3} rejected by date mismatch)", Time[0], oppName, candidatesFound, rejectedByDate));
		
		return 0;
	}



		
		private bool isValidVWAP(double val)
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

		private double GetCurrentHighVWAP() { return ethHighVWAP.CurrentValue; }
		private double GetCurrentLowVWAP() { return ethLowVWAP.CurrentValue; }
	
	// CONTINUOUS R/R VALIDATION (v1.7.28)
	// v1.13.14: Added detailed diagnostic logging
	private bool ValidateRiskReward(bool isShort, double entryPrice, double stopPrice, out double risk, out double reward, out double ratio)
	{
		// Calculate both targets
		double tp1Target = isShort ? GetCurrentLowVWAP() : GetCurrentHighVWAP();
		double tp2Target = GetOppositeLevelPrice(setupLevelName, setupLevelTime, setupAnchorPrice, isShort); // isShort = expectLower
		
		if (tp2Target == 0) tp2Target = tp1Target; // Fallback
		
		// Find closest target
		double closestTarget = isShort 
			? Math.Max(tp1Target, tp2Target)  // Short: higher price = closer
			: Math.Min(tp1Target, tp2Target); // Long: lower price = closer
		
		// Calculate risk/reward
		risk = Math.Abs(entryPrice - stopPrice);
		
		// Direction check
		bool validDirection = isShort ? (closestTarget < entryPrice) : (closestTarget > entryPrice);
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
		
		private double GetSetupVWAP(bool isShort)
	{
		// 1. If we have ADHOC VOLUME tracked, use it.
		// This represents the "VWAP since touch".
		if (!string.IsNullOrEmpty(setupLevelName) && adhocVolSum > 0)
		{
			double adhocValue = adhocPvSum / adhocVolSum;
			return adhocValue;
		}
		
		// 2. Fallback to Global (e.g. if logic fails or we are tracking a Global Extremum trade where we didn't reset adhoc)
		double globalValue = isShort ? GetCurrentHighVWAP() : GetCurrentLowVWAP();
		return globalValue;
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
				// v1.10.31: Use Trade VWAP if active (continues from day of entry)
				if (tradeVwapActive)
					targetGlobalVWAP = tradeVWAP.CurrentValue;
				else
					targetGlobalVWAP = GetCurrentLowVWAP(); 
				// FIX (v1.6.2): Use setupLevelTime to ensure stable target throughout the trade
				targetZoneOpposite = GetOppositeLevelPrice(setupLevelName, setupLevelTime);
				if (targetZoneOpposite <= 0) targetZoneOpposite = targetGlobalVWAP; // Fallback
			}
			else
			{
				// v1.10.31: Use Trade VWAP if active (continues from day of entry)
				if (tradeVwapActive)
					targetGlobalVWAP = tradeVWAP.CurrentValue;
				else
					targetGlobalVWAP = GetCurrentHighVWAP(); 
				// FIX (v1.6.2): Use setupLevelTime here too
				targetZoneOpposite = GetOppositeLevelPrice(setupLevelName, setupLevelTime);
				if (targetZoneOpposite <= 0) targetZoneOpposite = targetGlobalVWAP;
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
			// 1. Entry Order Tracking
			if (order.Name.Contains("EntryA+_"))
			{
				entryOrder = order;
				
				// Handle Terminal States for Entry
				// Unmanaged: If order is Rejected, we must reset state or we get stuck 'Working' forever.
				if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected) 
				{
					Log(Time[0] + " ENTRY TERMINATED: " + order.Name + " State: " + orderState + " Err: " + error);
					
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
						
						// FIX (Zombie Prev): Only reset if we are truly FLAT.
						// If one split order filled and the other rejected, we are NOT Flat.
						if (Position.MarketPosition == MarketPosition.Flat)
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
				if (order.Name.Contains("TP1_")) tp1Order = order;
				else if (order.Name.Contains("TP2_")) tp2Order = order;
			}
			
			// TP Orders tracked via SubmitOrder return, but we can capture them here too if needed.
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (execution.Order.OrderState == OrderState.Filled || execution.Order.OrderState == OrderState.PartFilled)
			{
				string n = execution.Order.Name;
				
				// CONSOLIDATED ROUTING (v1.7.17)
				if (n.Contains("EntryA+_")) 
				{
					if (currentEntryState == EntryState.workingOrder)
					{
						currentEntryState = EntryState.PositionActive;
						tradeOriginalQty = quantity; // v1.11.23: Save original trade qty for panel display
						Log(Time + " Entry Filled ("+n+") Qty=" + quantity + ". State -> PositionActive. TradeOriginalQty=" + tradeOriginalQty);
						
						// v1.13.0: Initialize TradeAnalyzer export variables
						tradeExportId++;
						tradeExitFillsCount = 0; // v1.13.4: Reset exit fills counter
				slOrderCreatedThisEntry = false; // v1.13.5: Reset SL duplication flag
						tradeEntryPrice = execution.Order.AverageFillPrice;
						tradeEntryTime = time;
							tradeDirection = Position.MarketPosition == MarketPosition.Long ? "Long" : "Short";
						tradeSetupName = setupLevelName;
						tradeAttemptNumber = currentVwapNumber; // v1.13.11: Save attempt number for CSV
						tradeMAE = 0;
						tradeMFE = 0;
						isTrackingTrade = true; // Flag to track MAE/MFE
						Log(Time + " CSV EXPORT: Trade #" + tradeExportId + " started - " + tradeDirection + " @ " + tradeEntryPrice);
					}
					
					// Ensure Protection Runs based on FILLED QTY
					// v1.7.17: We pass the filled amount, protection logic distributes it to buckets.
					if (Position.MarketPosition == MarketPosition.Short)
					{
						EnsureProtection("Short", n, quantity);
						TriggerScreenshot("Entry_Short_" + n, DateTime.Now, executionId);
					}
					else if (Position.MarketPosition == MarketPosition.Long)
					{
						EnsureProtection("Long", n, quantity);
						TriggerScreenshot("Entry_Long_" + n, DateTime.Now, executionId);
					}
				}
			}
			
			// BREAKEVEN LOGIC DEBUGGING
			if (execution.Order != null && execution.Order.OrderState == OrderState.Filled)
			{
				// Debug Log
				// Print(Time[0] + " EXEC FILLED: " + execution.Order.Name);

				// CHECK TP1 -> Move SL2
				bool isTP1 = (tp1Order != null && execution.Order == tp1Order);
				if (!isTP1 && execution.Order.Name.StartsWith("TP1_")) isTP1 = true; // Fallback by Name

				if (isTP1)
				{
					Log(Time[0] + " BE LOGIC: TP1 Filled. Moving SL to BE.");
					
					// v1.10.13: Use stopOrder (Single-SL architecture v1.9.0+)
					// After TP1 fills, move the single SL to breakeven
					if (stopOrder != null)
					{
						if (entryOrder != null)
						{
							// v1.10.14: Use Position.Quantity (remaining contracts) not stopOrder.Quantity (original)
							int remainingQty = Math.Abs(Position.Quantity);
							Log(Time[0] + " BE ACTION: Moving SL (" + stopOrder.Name + ") to " + entryOrder.AverageFillPrice + " Qty=" + remainingQty);
							ChangeOrder(stopOrder, remainingQty, 0, entryOrder.AverageFillPrice);
						}
					}
				}

				// CHECK TP2 -> SL should already be at BE, nothing to do
				bool isTP2 = (tp2Order != null && execution.Order == tp2Order);
				if (!isTP2 && execution.Order.Name.StartsWith("TP2_")) isTP2 = true;

				if (isTP2)
				{
					// v1.10.13: With Single-SL architecture, SL is already at BE from TP1 fill
					// No additional action needed for TP2
					Log(Time[0] + " TP2 Filled. SL already at BE (if TP1 filled first).");
				}
			}

			
			// Reset if Closed (Filled) OR Cancelled/Rejected
			// CHECK ENTRY
			bool resetNeeded = false;
			if (entryOrder != null && execution.Order == entryOrder && (execution.Order.OrderState == OrderState.Cancelled || execution.Order.OrderState == OrderState.Rejected)) resetNeeded = true;

			if (resetNeeded)
			{
				Log(Time + " Entry Order Cancelled/Rejected. Resetting to Idle.");
				currentEntryState = EntryState.Idle;
				setupLevelName = "";
				
				// CLEAR ALL
				entryOrder = null;
				targetOrder = null;
				tp1Order = null;
				tp2Order = null; 
				stopOrder = null;
				
				// Clear Cache
				cachedOppositeLevel = null;
				
				failsafeTriggered = false; // v1.14.2: Reset failsafe lock
			}
			
			// CRITICAL FIX: Only reset if we are truly FLAT. include "Exit on session close"
			// Also checking if it is an Unmanaged Exit order (SL/TP) OR the System Session Close
			// v1.13.13 FIX: TP orders are named TP1_ and TP2_, not TP_ - was causing TPs to not export to CSV!
			bool isExitOrder = (execution.Order.Name.Contains("SL_") || execution.Order.Name.Contains("TP1_") || execution.Order.Name.Contains("TP2_") || execution.Order.Name == "Exit on session close");
			
			if (execution.Order.OrderState == OrderState.Filled && isExitOrder)
			{
				// v1.13.3: Export CSV on EACH exit fill (not only when flat)
				if (isTrackingTrade && !string.IsNullOrEmpty(csvExportPath))
				{
					try
					{
						double exitPrice = execution.Order.AverageFillPrice;
						
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
						string tradeId;
						if (tradeExitFillsCount == 1 && execution.Quantity >= 2)
							tradeId = tradeExportId.ToString(); // First fill of whole position (both contracts closed together)
						else
							tradeId = tradeExportId + "." + tradeExitFillsCount; // Partial fill: 1.1, 1.2, etc
						
						// Format CSV line - Ensure all values are valid
						string safeSetupName = string.IsNullOrEmpty(tradeSetupName) ? "" : tradeSetupName.Replace(",", ";");
						
						// v1.13.11: Added Attempt column for retry analysis
						// v1.13.12: Added RiskReward column for R:R distribution chart
						// v1.13.16: Added Commission calculation (NinjaTrader Free Plan rates)
						double riskReward = (tradeRiskUSD > 0) ? (pnl / tradeRiskUSD) : 0;
						
						// Calculate commission based on instrument (2 sides per trade)
						// NinjaTrader Free Plan All-In Rates
						// LOGIC: Micros typically start with "M" (MES, MNQ, MCL, MGC, M6E, MBT...)
						//        Full-size do NOT start with "M" (ES, NQ, CL, GC, 6E, ZS, BTC...)
						string instName = Instrument.MasterInstrument.Name.ToUpper();
						bool isMicro = instName.StartsWith("M") && !instName.StartsWith("MY"); // MYM is exception (micro dow)
						if (instName.StartsWith("MYM") || instName.StartsWith("M2K")) isMicro = true; // Explicitly micro
						
						double commissionPerSide;
						if (isMicro)
						{
							// MICRO CONTRACTS
							if (instName.Contains("MBT") || instName.Contains("MET")) commissionPerSide = 1.56; // Micro Bitcoin/Ether
							else if (instName.Contains("MCL") || instName.Contains("MGC") || instName.Contains("MHG")) commissionPerSide = 0.77; // Micro commodities
							else commissionPerSide = 0.91; // Micro indices (MES, MNQ, M2K, MYM, M6E, etc.)
						}
						else
						{
							// FULL-SIZE CONTRACTS
							if (instName.StartsWith("6E") || instName.StartsWith("6J") || instName.StartsWith("6A") || instName.StartsWith("6B")) commissionPerSide = 3.09; // Full currencies
							else if (instName.StartsWith("ZS") || instName.StartsWith("ZW") || instName.StartsWith("ZC") || instName.StartsWith("ZJ")) commissionPerSide = 2.85; // Full grains
							else if (instName.StartsWith("CL") || instName.StartsWith("GC") || instName.StartsWith("HG")) commissionPerSide = 2.29; // Full commodities
							else if (instName.StartsWith("ES") || instName.StartsWith("NQ") || instName.StartsWith("YM") || instName.StartsWith("RTY")) commissionPerSide = 2.29; // Full indices
							else if (instName.StartsWith("BTC") || instName.StartsWith("ETH")) commissionPerSide = 6.00; // Full crypto
							else commissionPerSide = 2.50; // Default full-size
						}
						
						double commission = execution.Quantity * 2 * commissionPerSide; // 2 sides (entry + exit)
						double netPnl = pnl - commission;
						
						string line = string.Format("{0},{1},{2:yyyy-MM-dd HH:mm:ss},{3},{4},{5:yyyy-MM-dd HH:mm:ss},{6},{7},{8:F2},{9:F2},{10:F2},{11:F2},{12:F2},{13},{14},{15:F2}",
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
							riskReward
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

				// RESET PROTECTION COUNTERS (v1.7.24) - Fix bucket allocation
				protectedTp1Qty = 0;
				protectedTp2Qty = 0;
				protectionOrdersCreated = false; // v1.11.14: Reset flag for next trade
				isProtectionProcessing = false; // v1.13.1: Reset lock
				tradeOriginalQty = 0; // v1.11.23: Reset original trade qty
				tradeOriginalTp1Price = 0; // v1.11.24: Reset original TP prices
				tradeOriginalTp2Price = 0;
				tradeVwapActive = false; // v1.10.31: Reset Trade VWAP

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
					validatedTargetPrice = 0; // v1.7.17 FIX: Ensure stale target cleared
				}
				else
				{
					Log(Time + " Partial Execution (" + execution.Order.Name + "). Position Active. Qty=" + Position.Quantity);
				}
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
		[Display(Name="Risk Per Trade (USD)", Order=4, GroupName="Order Management")]
		public double RiskPerTradeUSD
		{ get; set; } = 50.0;
		
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
		[Range(0.1, 10.0)]
		[Display(Name="ATR Risk Scale Factor", Description="Multiplier to convert ATR to risk $. Higher = more risk in volatile markets. (e.g. 2.0 means Risk$ = ATR × 2)", Order=8, GroupName="Order Management")]
		public double ATRRiskScaleFactor
		{ get; set; } = 2.0;
		
		// Internal Targets State
		private double activeTp1Price = 0;
		private double activeTp2Price = 0;
		

		
		[NinjaScriptProperty]
		[Display(Name="Europe End Time", Order=4, GroupName="1. Sessions")]
		public string EuropeEndTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name="USA Start Time", Order=5, GroupName="1. Sessions")]
		public string USAStartTime { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="USA End Time", Order=6, GroupName="1. Sessions")]
		public string USAEndTime { get; set; }
		


		// Fix: Missing InitCSV stub.
		private void InitCSV()
		{
			// Safe stub to ensure compilation
		}

		// UNMANAGED HELPER: Close Position Market


		private void ClosePositionUnmanaged(string reason)
		{
			if (Position.MarketPosition == MarketPosition.Long)
			{
				Log(Time + " UNMANAGED EXIT: Closing Long. Reason: " + reason);
				SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.Market, Position.Quantity, 0, 0, "", "Exit_Long_Market");
			}
			else if (Position.MarketPosition == MarketPosition.Short)
			{
				Log(Time + " UNMANAGED EXIT: Closing Short. Reason: " + reason);
				SubmitOrderUnmanaged(0, OrderAction.BuyToCover, OrderType.Market, Position.Quantity, 0, 0, "", "Exit_Short_Market");
			}
			
			// Cancel any working entry orders to be safe
			// Cancel any working entry orders to be safe
			if (entryOrder != null && entryOrder.OrderState == OrderState.Working) CancelOrder(entryOrder);
		}
	} // End of SessionLevelsStrategy_2026_01_02_82 class

	public class SessionLevelData
	{
		public string Name;
		public double Price;
		public DateTime StartTime;
		public DateTime EndTime;
		public DateTime MitigationTime;
		public bool IsResistance;
		public bool IsMitigated;
		public double VolSum;
		public double PvSum;
		public string Tag;
		// Color is not serialized easily, we infer it from Name or defaults.

	}
} // End of Namespace
