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
using System.Timers; // Required for Timer
using System.IO;     // v1.0.26: File logging

using NinjaTrader.NinjaScript.DrawingTools;
using System.Globalization;
using NinjaTrader.NinjaScript.Indicators.RelativeIndicators; // Fix for Generated Code Visibility
#endregion

namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
    public enum TradeDirectionMode { Both, LongOnly, ShortOnly }
    public enum VwapPriceMethod { Close, Typical, OHLC4 }
    public enum LabelMode { Default, Simple, Custom }
    
    public class RelativeVwap : Indicator
    {
        // ========== VERSION ==========
        private const string VERSION = "1.0.37";  // v1.0.37: Fix vela despintada incorrectamente - BarBrushes[0]=null solo en same bar
        // ==============================
        
        private SessionIterator sessionIterator;
        
        // Tracking for High Anchored VWAP
        private double sessionHighPV;
        private double sessionHighVol;
        private int sessionHighBarIdx;
        private double sessionHighPrice;

        // Tracking for Low Anchored VWAP
        private double sessionLowPV;
        private double sessionLowVol;
        private int sessionLowBarIdx;
        private double sessionLowPrice;
        
        private int tradeIdCounter = 0; // V_VISUAL: Trade Counter
        
        // Daily High/Low for finding anchor points
        private double currentDayHigh;
        private double currentDayLow;
        private bool highHasTakenRelevant;
        private bool lowHasTakenRelevant;

        // Signal Logic State
        private double highCumPV, highCumVol;
        private double lowCumPV, lowCumVol;
        private bool highDetached;
        private bool lowDetached;
        private bool _highJustReset;  // v1.0.2: Skip accumulation on anchor bar
        private bool _lowJustReset;   // v1.0.2: Skip accumulation on anchor bar
        private bool highSignalFired;
        private bool lowSignalFired;
        private double currentHighVWAP;
        private double currentLowVWAP;
        private bool hasHighVWAP;
        private bool hasLowVWAP;
        private bool highSignal2Fired; // V_SIGNAL_2 One-Shot Flag
        private bool lowSignal2Fired;  // V_SIGNAL_2 One-Shot Flag
        private int highAnchorSequence; // V_SIGNAL_2 Sequence Counter
        private int lowAnchorSequence;  // V_SIGNAL_2 Sequence Counter
        private int lastSignaledHighAnchorBar = -1; // V_SIGNAL_2 Anchor Tracker
        private int lastSignaledLowAnchorBar = -1;  // V_SIGNAL_2 Anchor Tracker
        private SessionLevelInfo lastUnlockedHighSession = null;
        private SessionLevelInfo lastUnlockedLowSession = null;
        
        // V_FIX_LIVE: Persistent Signal 2 Painting State
        private int highSignal2BarIdx = -1; // Tracks specific bar index for High Signal 2
        private int lowSignal2BarIdx = -1;  // Tracks specific bar index for Low Signal 2

        // v1.0.24: Tracking for movable "Liquidity Grabbed" label
        private int highLiqGrabBarIdx = -1;      // Bar where High liquidity grab label is drawn
        private double highLiqGrabExtreme = 0;   // Highest price reached since liquidity grab
        private string highLiqGrabSessionName = ""; // Session name for the label tag
        private int lowLiqGrabBarIdx = -1;       // Bar where Low liquidity grab label is drawn
        private double lowLiqGrabExtreme = 0;    // Lowest price reached since liquidity grab
        private string lowLiqGrabSessionName = ""; // Session name for the label tag

        // Session Levels Tracking
        public class SessionLevelInfo
        {
            public string Name;
            public DateTime StartTime;
            public DateTime EndTime;
            public double High;
            public double Low;
            public int StartBarIdx;
            public bool IsActive;
            
            public int HighBrokenBarIdx = -1;
            public int LowBrokenBarIdx = -1;
            public DateTime SessionDate; // Store the date the session belongs to
            
            // To track if we have initialized for the current day/session cycle
            public DateTime LastResetDate;
            
            // Track when the ghost line should end (End of the session where break occurred)
            public int HighGhostEndIdx = -1;
            public int LowGhostEndIdx = -1;
            
            // V_SYNC: Added Traded Flags to match Strategy "One-Shot" Rule
            public bool IsHighTraded = false;
            public bool IsLowTraded = false;
            
            // V_SYNC: Strategy Trade Counters (Added for shared state)
            public int HighTradeCount = 0; 
            public int LowTradeCount = 0; 
            
            // V_LOGIC: Internal vs Extreme Classification
            public bool IsInternalHigh = false;
            public bool IsInternalLow = false;
        }

        private List<SessionLevelInfo> asiaSessions;
        private List<SessionLevelInfo> europeSessions;
        private List<SessionLevelInfo> usSessions;
        
        // V_SMART: Public Accessors for Strategy Rendering
        [Browsable(false)] public List<SessionLevelInfo> AsiaSessions { get { return asiaSessions; } }
        [Browsable(false)] public List<SessionLevelInfo> EuropeSessions { get { return europeSessions; } }
        [Browsable(false)] public List<SessionLevelInfo> USSessions { get { return usSessions; } }

        private DateTime asiaStart, asiaEnd;
        private DateTime europeStart, europeEnd;
        private DateTime usStart, usEnd;

        private struct HistoricalAnchor 
        { 
            public int StartIdx; 
            public int EndIdx; 
            public bool WasRelevant;
            public int FirstBreakIdx;
        }

        private int highFirstBreakIdx = -1;
        private int lowFirstBreakIdx = -1;

        private List<HistoricalAnchor> historicalHighs = new List<HistoricalAnchor>();

        private List<HistoricalAnchor> historicalLows = new List<HistoricalAnchor>();
        
        // V39: Hybrid Logic Variables
        private double _lastVol = 0; // For Tick-based calculation
        private bool _isNewBar = true; // Track new bar for detachment check
        private int debugUpdateCounter = 0; // V_DEBUG: Heartbeat Monitor
        
        // V_NORM: ATR-based Normalization for consistent spacing across instruments
        private NinjaTrader.NinjaScript.Indicators.ATR atr;

        // v1.0.26: File Logging System
        private string logFilePath = "";
        private object logLock = new object();

        // v1.0.5: Anti-Collision System for Labels (SIMPLIFIED)
        // NOTE: Returns proposedY directly - collision avoidance removed due to visual issues
        private double _highLabelY = double.MinValue;
        private double _lowLabelY = double.MaxValue;
        
        /// <summary>
        /// Simply returns the proposed Y position.
        /// The LabelCollisionSpacing parameter now only affects the base offset from price.
        /// </summary>
        private double GetNonCollidingHighY(double proposedY, double spacing)
        {
            // Just return the proposed position - no stacking
            return proposedY;
        }
        
        /// <summary>
        /// Simply returns the proposed Y position.
        /// The LabelCollisionSpacing parameter now only affects the base offset from price.
        /// </summary>
        private double GetNonCollidingLowY(double proposedY, double spacing)
        {
            // Just return the proposed position - no stacking
            return proposedY;
        }

        // Smart Label Queue
        private class LabelData
        {
            public string Text;
            public float DrawX; // Top-Left X
            public float Y;
            public float Width;
            public Brush Brush;
            public DateTime Time;
        }
        private List<LabelData> labelQueue = new List<LabelData>();

        // V_STACK: Signal Label Logic
        private class SignalObj 
        { 
            public int BarIdx; 
            public double Price; 
            public string Text; 
            public bool IsHigh; 
            public Brush Brush; 
        }
        private Dictionary<string, SignalObj> signalLabels = new Dictionary<string, SignalObj>();



        // Countdown State
        private System.Timers.Timer updateTimer;
        private bool isVolume, isVolumeBase, isTimeBased;
        private double volume;
        private string _currentCountdownText = "";
        
        // Public Property for Strategy to read
        [Browsable(false)]
        [XmlIgnore]
        public string LastSignalCode { get; private set; } = "";

        [Browsable(false)]

        [XmlIgnore]
        public string CurrentCountdownText
        {
            get { return _currentCountdownText; }
        }

        // v1.0.26: File Logging Helper
        private void LogToFile(string message, string category = "INFO")
        {
            if (!EnableFileLogging) return;

            try
            {
                lock (logLock)
                {
                    string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                    string barTime = (Bars != null && CurrentBar >= 0) ? Time[0].ToString("HH:mm:ss") : "N/A";
                    string logLine = string.Format("[{0}] [{1}] Bar:{2} Time:{3} | {4}",
                        timestamp, category, CurrentBar, barTime, message);

                    File.AppendAllText(logFilePath, logLine + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Print("RelativeVwap LogToFile ERROR: " + ex.Message);
            }
        }

        // Helper for safely adding signals
        // Helper for safely adding signals
        private void AddSignal(int barIdx, double price, string text, bool isHigh, Brush brush, string signalType)
        {
            if (signalLabels == null) return;
            
            // v1.0.4: Skip if ShowSignalLabels is disabled
            if (!ShowSignalLabels) return;
            
            // Unique key per signal type/bar (Ignore text for key uniqueness)
            // This ensures we don't get duplicates if text evolves (e.g. "AH.1" -> "AH.1.1")
            string key = barIdx + "_" + signalType + "_" + (isHigh ? "H" : "L");
            
            // Always update/overwrite
            signalLabels[key] = new SignalObj 
            { 
                BarIdx = barIdx, 
                Price = price, 
                Text = text, 
                IsHigh = isHigh, 
                Brush = brush 
            };
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = $"RelativeVwap v{VERSION}: VWAP anclado a extremos de sesión con señales de trading y niveles relativos.";
                Name = "RelativeVwap"; // Restore Production Name
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                
                // Force On Top of Price
                ZOrder = 10002;
                // ForceMaximumBarsLookBack256 = false; // Removed: Strategy Property
                // ParametersDefault(); // Removed: Strategy Method
                
                // Countdown Defaults
                ShowCountdown = true;
                CountDown = true;
                ShowPercent = false;
                CountdownFontSize = 12;
                CountdownTextColor = Brushes.White;
                CountdownOffsetX = 20; // Pixels roughly
                CountdownOffsetY = 10; // Ticks

                ZOrder                                      = -5; // Aggressively behind price (Top Priority)
                //Description                                 = @"Calculates Anchored VWAP from the current Session's High and Low, plus optional Session Levels."; // Moved above
                //Name                                        = "RelativeVwap"; // Moved above
                //DrawVerticalGridLines                       = true; // Moved above
                //PaintPriceMarkers                           = true; // Moved above
                //IsOverlay                                   = true; // FORCE OVERLAY // Moved above
                //ScaleJustification                          = NinjaTrader.Gui.Chart.ScaleJustification.Right; // Moved above
                IsSuspendedWhileInactive                    = false; // FORCE ON
                BarsRequiredToPlot                          = 0;     // FORCE IMMEDIATE
                
                //Calculate = Calculate.OnEachTick; // Enforce OnEachTick for Visual Updates // Moved above
                Print("RelativeVwap Indicator: OnStateChange (SetDefaults) Reached");

                // V_FIX: Add Plots to ensure Values[0] (High) and Values[1] (Low) exist for Strategy Hookup
                // v1.0.24: PlotStyle.Dot = small markers (nearly invisible), but visible in DataBox
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Dot, "VWAP Hi"); // Values[0]
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Dot, "VWAP Lo"); // Values[1]

                // Defaults
                HighVWAPColor = Brushes.Cyan;
                LowVWAPColor = Brushes.Cyan;
                HistoricalVWAPColor = Brushes.Gray;
                HistoricalVWAPThickness = 2.0f;
                
                ShowLabels = true;
                
                
                MaxHistoryDays = 5;

                // Asia Defaults

                // Asia Defaults
                AsiaStartTime = "18:00"; // Changed to 18:00 (Exchange Time)
                AsiaEndTime = "03:00";   // Changed to 03:00
                ShowAsia = true;
                AsiaLineColor = Brushes.DarkGray;
                AsiaLabelColor = Brushes.Silver;
                ShowAsiaHigh = true;
                ShowAsiaLow = true;

                // Europe Defaults
                EuropeStartTime = "03:00"; // Changed to 03:00
                EuropeEndTime = "09:30";   // Changed to 09:30
                ShowEurope = true;
                EuropeLineColor = Brushes.Gold;
                EuropeLabelColor = Brushes.Silver;
                ShowEuropeHigh = true;
                ShowEuropeLow = true;

                // US Defaults
                USStartTime = "09:30";
                USEndTime = "16:00";       // Changed to 16:00
                ShowUS = true;
                USLineColor = Brushes.Blue;
                USLabelColor = Brushes.White;
                ShowUSHigh = true;
                ShowUSLow = true;
                
                UseExchangeTime = true;    // Default ON
                
                // VWAP Method (v1.0.1)
                VwapMethod = VwapPriceMethod.Close;  // Default: Close (matches SessionLevels strategy)
                
                EnableAlerts = true;
                AlertSound = "mzpack_alert4.wav";

                
                ShowDaysAgo = true; // Default True
                
                Print("RelativeVwap Indicator: OnStateChange (SetDefaults) Reached - VERSION " + VERSION);
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(14); // V_NORM: Correct Initialization

                // Version Info (Always Print)
                Print("======================================");
                Print("RelativeVwap LOADED - Version: " + VERSION);
                Print("Instrument: " + Instrument.FullName);
                Print("======================================");

                // v1.0.26: Initialize Log File Path
                if (EnableFileLogging)
                {
                    string dateStamp = DateTime.Now.ToString("yyyyMMdd");
                    string traceFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "NinjaTrader 8", "trace");

                    if (!Directory.Exists(traceFolder))
                        Directory.CreateDirectory(traceFolder);

                    logFilePath = Path.Combine(traceFolder, $"RelativeVwap_Debug_{dateStamp}.txt");

                    // Write header
                    LogToFile("=== RelativeVwap Debug Log Started ===", "SYSTEM");
                    LogToFile($"Version: {VERSION}", "SYSTEM");
                    LogToFile($"Instrument: {Instrument.FullName}", "SYSTEM");
                }

                // Initialize Countdown Logic
                if (ShowCountdown)
                {
                    isVolume = BarsPeriod.BarsPeriodType == BarsPeriodType.Volume;
                    isVolumeBase = (BarsPeriod.BarsPeriodType == BarsPeriodType.HeikenAshi || BarsPeriod.BarsPeriodType == BarsPeriodType.PriceOnVolume || BarsPeriod.BarsPeriodType == BarsPeriodType.Volumetric) && BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Volume;
                    isTimeBased = BarsPeriod.BarsPeriodType == BarsPeriodType.Minute || BarsPeriod.BarsPeriodType == BarsPeriodType.Second || BarsPeriod.BarsPeriodType == BarsPeriodType.Day
                        || BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Minute || BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Second || BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Day;

                    if (isTimeBased)
                    {
                        if (updateTimer == null)
                        {
                            updateTimer = new System.Timers.Timer(250); // 4Hz Update
                            updateTimer.Elapsed += OnTimerTick;
                            updateTimer.AutoReset = true;
                            updateTimer.Enabled = true;
                        }
                    }
                }
                try
                {
                    Print("RelativeVwap Indicator: Entering State.DataLoaded...");
                    sessionIterator = new SessionIterator(Bars);
                    // On initial load, clear lists
                    if (historicalHighs != null) historicalHighs.Clear();
                    if (historicalLows != null) historicalLows.Clear();
                    
                    asiaSessions = new List<SessionLevelInfo>();
                    europeSessions = new List<SessionLevelInfo>();
                    usSessions = new List<SessionLevelInfo>();
                    
                    if (signalLabels != null) signalLabels.Clear();
                    signalLabels = new Dictionary<string, SignalObj>();
    
                    activeTrades = new List<TradeSetup>();
    
                    ResetSession();
                    
                    // Note: Time parsing is now done dynamically in UpdateSession if UseExchangeTime is true.
                    // However, we still parse them once here to validate format or for non-Exchange mode.
                    DateTime.TryParse(AsiaStartTime, out asiaStart);
                    DateTime.TryParse(AsiaEndTime, out asiaEnd);
                    DateTime.TryParse(EuropeStartTime, out europeStart);
                    DateTime.TryParse(EuropeEndTime, out europeEnd);
                    DateTime.TryParse(USStartTime, out usStart);
                    DateTime.TryParse(USEndTime, out usEnd);
                    
                    Print("RelativeVwap Indicator: State.DataLoaded Completed Successfully.");
                }
                catch (Exception ex)
                {
                    Print("RelativeVwap Indicator CRASH in State.DataLoaded: " + ex.Message);
                }
            }
            else if (State == State.Historical)
            {
                Print("RelativeVwap Indicator: Entering State.Historical...");
            }
            else if (State == State.Configure)
            {
                Print("RelativeVwap Indicator: Entering State.Configure...");

                // v1.0.24: Color visible for DataBox, Width=0 to hide chart lines
                if (HighVWAPColor != null)
                {
                    Plots[0].Brush = HighVWAPColor;
                    Plots[0].Width = 0;
                }
                if (LowVWAPColor != null)
                {
                    Plots[1].Brush = LowVWAPColor;
                    Plots[1].Width = 0;
                }
            }
            else if (State == State.Terminated)
            {
                if (updateTimer != null)
                {
                    updateTimer.Enabled = false;
                    updateTimer.Elapsed -= OnTimerTick;
                    updateTimer.Dispose();
                    updateTimer = null;
                }

            }
        }

        private void ResetSession()
        {
            currentDayHigh = double.MinValue;
            currentDayLow = double.MaxValue;
            
            sessionHighBarIdx = -1;
            sessionLowBarIdx = -1;
            
            
            highHasTakenRelevant = false;
            lowHasTakenRelevant = false;
            

            highFirstBreakIdx = -1;
            lowFirstBreakIdx = -1;
            
            // Reset Signal State
            highDetached = false;
            lowDetached = false;
            highSignalFired = false;
            lowSignalFired = false;
            highSignal2Fired = false;
            lowSignal2Fired = false;
            highAnchorSequence = 0;
            lowAnchorSequence = 0;
            lastSignaledHighAnchorBar = -1;
            lastSignaledLowAnchorBar = -1;
            // highCumPV = 0; highCumVol = 0; // RESET DISABLED - not used anymore
            // lowCumPV = 0; lowCumVol = 0; // RESET DISABLED - not used anymore
            
            // v1.0.2 FIX: Reset the session variables that are now used for VWAP display
            sessionHighPV = 0;
            sessionHighVol = 0;
            sessionLowPV = 0;
            sessionLowVol = 0;
            
            // FIX: Reset Unlocked Sessions to prevent persistence of yesterday's internal anchors
            lastUnlockedHighSession = null;
            lastUnlockedLowSession = null;
            
            // V_FIX_LIVE: Reset Painting State
            highSignal2BarIdx = -1;
            lowSignal2BarIdx = -1;

            // v1.0.24: Reset Liquidity Grab label tracking
            highLiqGrabBarIdx = -1;
            highLiqGrabExtreme = 0;
            highLiqGrabSessionName = "";
            lowLiqGrabBarIdx = -1;
            lowLiqGrabExtreme = 0;
            lowLiqGrabSessionName = "";

            if (ShowDebugLabels)
                Draw.Text(this, "Reset" + CurrentBar, "RESET", 0, Low[0] - 5 * TickSize, Brushes.Red);
            
            Print(string.Format("RelativeVwap: ResetSession called at Bar {0} (Date: {1}). Cleared Anchors. Kept Sessions (Count A:{2} E:{3} U:{4}) ActiveTrades:{5}", 
                CurrentBar, Time[0], asiaSessions.Count, europeSessions.Count, usSessions.Count, (activeTrades != null ? activeTrades.Count : -1)));
        }

        private class TradeSetup
        {
            public int ID; // Unique ID for drawing tags
            public int EntryBar;
            public DateTime EntryTime;
            public double EntryPrice;
            public double SL;
            public double TP1;
            public double TP2; // Can be double.MinValue or 0 if invalid
            public bool IsLong;
            public bool TP1Hit;
            public bool TP2Hit;
            public bool SLHit;
            public bool IsClosed;
            
            // Exit Data
            public double ExitPrice;
            public DateTime ExitTime;
            public int ExitBar = -1;
            public double RealizedPnL;
            
            // Dynamic TP Flags
            public bool IsTP1Dynamic;
            public bool IsTP2Dynamic;
            
            // Advanced Metrics
            public double MAE; // Max Adverse Excursion (Max potential loss reached)
            public double MFE; // Max Favorable Excursion (Max potential profit reached)
        }
        
        private List<TradeSetup> activeTrades;

        protected override void OnBarUpdate()
        {
      if (CurrentBar < 14)
      {
          // v1.0.24: Use NaN to prevent plot lines before anchor
          Values[0][0] = double.NaN;
          Values[1][0] = double.NaN;
          return;
      }
              debugUpdateCounter++; // Count EVERY call

      double priceLimit = Close[0] * 0.5; // Safety threshold (50% of price)

      // v1.0.24: Only set Values when VWAP is active, otherwise NaN (no plot line)
      if (hasHighVWAP && sessionHighBarIdx >= 0 && CurrentBar >= sessionHighBarIdx) {
          double hVol = Math.Max(1, sessionHighVol);
          double val = sessionHighPV / hVol;

          // SAFETY: If val is 0 or absurdly low, use Close or Previous
          if (val < priceLimit)
              val = Values[0].IsValidDataPointAt(CurrentBar - 1) ? Values[0][1] : Close[0];

          Values[0][0] = val; // High VWAP
      } else {
          Values[0][0] = double.NaN; // No line before anchor
      }

      if (hasLowVWAP && sessionLowBarIdx >= 0 && CurrentBar >= sessionLowBarIdx) {
          double lVol = Math.Max(1, sessionLowVol);
          double val = sessionLowPV / lVol;

          if (val < priceLimit)
              val = Values[1].IsValidDataPointAt(CurrentBar - 1) ? Values[1][1] : Close[0];

          Values[1][0] = val; // Low VWAP
      } else {
          Values[1][0] = double.NaN; // No line before anchor
      }
             
             try
             {
                 if (CurrentBar % 500 == 0) 
                 {
                     Print(string.Format("RelativeVwap Alive @ {0} | Setup:{1} Trades:{2}", CurrentBar, "N/A", (activeTrades != null ? activeTrades.Count : -99)));

                     if (usSessions != null && usSessions.Count > 0)
                     {
                          var s = usSessions.Last();
                          Print(string.Format("  Stats: US Active={0} High={1} Broken={2}", s.IsActive, s.High, s.HighBrokenBarIdx));
                     }
                 }

                 // Manage Active Trades
                 ManageTrades();
                 
             // Check for Day Change (Strict Reset)
             // CRITICAL FIX: Only Reset Anchors if the Calendar Date changes.
             // Do NOT reset just because a new Intraday Session (Europe/US) starts.
              if (Bars.IsFirstBarOfSession)
              {
                  // Archive the final anchors of the previous session
                  if (sessionHighBarIdx != -1)
                      historicalHighs.Add(new HistoricalAnchor { StartIdx = sessionHighBarIdx, EndIdx = CurrentBar - 1, WasRelevant = highHasTakenRelevant, FirstBreakIdx = highFirstBreakIdx });
                  
                  if (sessionLowBarIdx != -1)
                      historicalLows.Add(new HistoricalAnchor { StartIdx = sessionLowBarIdx, EndIdx = CurrentBar - 1, WasRelevant = lowHasTakenRelevant, FirstBreakIdx = lowFirstBreakIdx });

                  // Close Ghost Lines
                   CloseGhostLines(asiaSessions, CurrentBar);
                   CloseGhostLines(europeSessions, CurrentBar);
                   CloseGhostLines(usSessions, CurrentBar);

                   ResetSession();
                   
                   // V_SYNC: Reset Traded Flags Deeply
                   foreach(var s in asiaSessions) { s.IsHighTraded = false; s.IsLowTraded = false; }
                   foreach(var s in europeSessions) { s.IsHighTraded = false; s.IsLowTraded = false; }
                   foreach(var s in usSessions) { s.IsHighTraded = false; s.IsLowTraded = false; }
                   
                   // Reset Last Volume for new session
                   _lastVol = 0;
                   
                   // Update Date Cache
                   CurrentBarDate = Time[0].Date;
                   RefreshTimezoneCache(CurrentBarDate);
               }
              
              // Detect New Bar for Detachment Logic (Sync with Strategy)
              if (IsFirstTickOfBar) 
              {
                  _isNewBar = true;
                  _lastVol = 0; // V_SYNC: Explicit Reset on New Bar to prevent calc drift
                  
                  // v1.0.2: RETROACTIVE ANCHOR UPDATE (matching VWAPCalculator.cs lines 86-113)
                  // If previous bar was the anchor, recalculate with Close[1] definitive
                  if (CurrentBar > 0)
                  {
                      // Check if previous bar was the HIGH anchor
                      if (sessionHighBarIdx == CurrentBar - 1 && sessionHighVol > 0)
                      {
                          double finalPrice = Close[1];  // Definitive close
                          if (VwapMethod == VwapPriceMethod.Typical)
                              finalPrice = (High[1] + Low[1] + Close[1]) / 3.0;
                          else if (VwapMethod == VwapPriceMethod.OHLC4)
                              finalPrice = (Open[1] + High[1] + Low[1] + Close[1]) / 4.0;
                          
                          Print(string.Format("[VWAP DEBUG] RETROACTIVE HIGH: Bar={0} VwapMethod={1} Close[1]={2:F2} Typical={3:F2} FinalPrice={4:F2} Vol[1]={5}",
                              CurrentBar, VwapMethod, Close[1], (High[1]+Low[1]+Close[1])/3.0, finalPrice, Volume[1]));
                          
                          // FIX: Reset the CORRECT accumulators (sessionHighPV/Vol, not highCumPV/Vol)
                          sessionHighPV = finalPrice * Volume[1];
                          sessionHighVol = Volume[1];
                          
                          // Update retroactively the visual value
                          if (Values[0].IsValidDataPointAt(CurrentBar - 1))
                              Values[0][1] = finalPrice;
                      }
                      
                      // Check if previous bar was the LOW anchor
                      if (sessionLowBarIdx == CurrentBar - 1 && sessionLowVol > 0)
                      {
                          double finalPrice = Close[1];  // Definitive close
                          if (VwapMethod == VwapPriceMethod.Typical)
                              finalPrice = (High[1] + Low[1] + Close[1]) / 3.0;
                          else if (VwapMethod == VwapPriceMethod.OHLC4)
                              finalPrice = (Open[1] + High[1] + Low[1] + Close[1]) / 4.0;
                          
                          Print(string.Format("[VWAP DEBUG] RETROACTIVE LOW: Bar={0} VwapMethod={1} Close[1]={2:F2} Typical={3:F2} FinalPrice={4:F2} Vol[1]={5}",
                              CurrentBar, VwapMethod, Close[1], (High[1]+Low[1]+Close[1])/3.0, finalPrice, Volume[1]));
                          
                          // FIX: Reset the CORRECT accumulators (sessionLowPV/Vol, not lowCumPV/Vol)
                          sessionLowPV = finalPrice * Volume[1];
                          sessionLowVol = Volume[1];
                          
                          // Update retroactively the visual value
                          if (Values[1].IsValidDataPointAt(CurrentBar - 1))
                              Values[1][1] = finalPrice;
                      }
                  }
              }
              else _isNewBar = false;
              
              // NEW LOGIC: Close Ghost Lines at Session Breaks
              if (Bars.IsFirstBarOfSession)
              {
                  int endOfLastSessionIdx = CurrentBar - 1;
                  if (endOfLastSessionIdx >= 0)
                  {
                      CloseGhostLines(asiaSessions, endOfLastSessionIdx);
                      CloseGhostLines(europeSessions, endOfLastSessionIdx);
                      CloseGhostLines(usSessions, endOfLastSessionIdx);
                  }
              }

             // Update High/Low MOVED UP

             
             // Update Session Levels
             DateTime time = Time[0];
             CurrentBarDate = time.Date; // Cache current date for helper if needed
             // V_OPTI: Refresh Timezone Cache Once Per Day
             if (CurrentBarDate != _lastCacheDate) RefreshTimezoneCache(CurrentBarDate);
             
             UpdateSession(asiaSessions, "Asia", time, AsiaStartTime, AsiaEndTime, ShowAsia);
             UpdateSession(europeSessions, "Europe", time, EuropeStartTime, EuropeEndTime, ShowEurope);
             UpdateSession(usSessions, "USA", time, USStartTime, USEndTime, ShowUS);
             
             // Check Touches - ALWAYS check now, for visibility logic
             CheckTouches(asiaSessions);
             CheckTouches(europeSessions);
             CheckTouches(usSessions);
             
             // --------------------------
             // V39: HYBRID VWAP LOGIC (Sync with Strategy)
             // --------------------------
             // Update High/Low (MOVED HERE FOR INITIALIZATION ORDER)
             double high = High[0];
             double low = Low[0];

             // Calculate price based on VwapMethod parameter (needed for reset logic)
             double price = Close[0];  // Default: Close
             if (VwapMethod == VwapPriceMethod.Typical)
                 price = (High[0] + Low[0] + Close[0]) / 3.0;
             else if (VwapMethod == VwapPriceMethod.OHLC4)
                 price = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;
             
             double volume = Volume[0];

             if (high > currentDayHigh)
             {
                 // Save previous high anchor if it existed
                 if (sessionHighBarIdx != -1)
                 {
                     historicalHighs.Add(new HistoricalAnchor { StartIdx = sessionHighBarIdx, EndIdx = CurrentBar, WasRelevant = highHasTakenRelevant });
                 }
                  currentDayHigh = high;
                  sessionHighBarIdx = CurrentBar;

                  // MANUAL FIX: Reset Signal State
                  highDetached = false;
                  highSignal2Fired = false;  // v1.0.33: Reset flag to allow Signal 2 for new anchor
                  lastSignaledHighAnchorBar = -1;  // v1.0.25: Reset tracker to allow Signal 2 for new anchor

                  Print(string.Format("[VWAP DEBUG] IMMEDIATE HIGH RESET: Bar={0} VwapMethod={1} price={2:F2} (Close={3:F2} Typical={4:F2}) Vol={5}",
                      CurrentBar, VwapMethod, price, Close[0], (High[0]+Low[0]+Close[0])/3.0, volume));

                  // v1.0.26: File Log
                  LogToFile(string.Format("NEW HIGH ANCHOR | Price:{0:F2} | High:{1:F2} | VWAP:{2:F2} | PrevAnchor:{3} | TrackerReset:-1",
                      price, high, price, sessionHighBarIdx - 1), "ANCHOR");

                  // v1.0.2: Initialize WITH first bar's volume (matching SessionLevels strategy)
                  // This ensures VWAP starts at 'price' (Close/Typical/OHLC4) instead of fallback
                  // FIX: Use sessionHighPV/Vol (the variables that Values[0][0] uses), not highCumPV/Vol
                  sessionHighPV = price * volume;
                  sessionHighVol = volume;
                  _highJustReset = true;  // Flag to skip accumulation this bar
                  
                  // v1.0.3 FIX: Update Values[0][0] IMMEDIATELY after reset
                  // (The display section ran BEFORE this reset, so it used old values)
                  Values[0][0] = price;  // VWAP = price on anchor bar
                  hasHighVWAP = true;
              }

             if (low < currentDayLow)
             {
                 // Save previous low anchor if it existed
                 if (sessionLowBarIdx != -1)
                 {
                     historicalLows.Add(new HistoricalAnchor { StartIdx = sessionLowBarIdx, EndIdx = CurrentBar, WasRelevant = lowHasTakenRelevant });
                 }
                  currentDayLow = low;
                  sessionLowBarIdx = CurrentBar;

                  // MANUAL FIX: Reset Signal State
                  lowDetached = false;
                  lowSignal2Fired = false;  // v1.0.33: Reset flag to allow Signal 2 for new anchor
                  lastSignaledLowAnchorBar = -1;  // v1.0.25: Reset tracker to allow Signal 2 for new anchor

                  Print(string.Format("[VWAP DEBUG] IMMEDIATE LOW RESET: Bar={0} VwapMethod={1} price={2:F2} (Close={3:F2} Typical={4:F2}) Vol={5}",
                      CurrentBar, VwapMethod, price, Close[0], (High[0]+Low[0]+Close[0])/3.0, volume));

                  // v1.0.26: File Log
                  LogToFile(string.Format("NEW LOW ANCHOR | Price:{0:F2} | Low:{1:F2} | VWAP:{2:F2} | PrevAnchor:{3} | TrackerReset:-1",
                      price, low, price, sessionLowBarIdx - 1), "ANCHOR");

                  // v1.0.2: Initialize WITH first bar's volume (matching SessionLevels strategy)
                  // FIX: Use sessionLowPV/Vol (the variables that Values[1][0] uses), not lowCumPV/Vol
                  sessionLowPV = price * volume;
                  sessionLowVol = volume;
                  _lowJustReset = true;  // Flag to skip accumulation this bar
              }
             
            // For time-based bars, let the Timer handle the update in Realtime
            // For time-based bars, let the Timer handle the update in Realtime
            // if (isTimeBased && State == State.Realtime) return; // REMOVED to allow Signal Logic to run
            
            CalculateCountdown();
            CalculateCountdown();
            // UpdateDisplay(); // Removed: Legacy call
             // Tick Validation
             if (State == State.Realtime)
             {
                 // In Realtime, use Tick Logic: Close * TickVolume
                 // We calculate the delta volume since last tick
                 // V_SYNC: Use selected price method
                 double tickVol = volume - _lastVol;
                 if (tickVol < 0) tickVol = volume; // New Bar
                 
                  // v1.0.2: Skip if just reset (already initialized with this bar's volume)
                  // FIX: Use sessionHighPV/Vol (the variables that Values[0][0] uses)
                  if (sessionHighBarIdx != -1 && !_highJustReset) { sessionHighPV += price * tickVol; sessionHighVol += tickVol; }
                  if (sessionLowBarIdx != -1 && !_lowJustReset) { sessionLowPV += price * tickVol; sessionLowVol += tickVol; }
                 
                 // Reset flags after use (each tick resets them)
                 _highJustReset = false;
                 _lowJustReset = false;
                 
                 _lastVol = volume;
             }
             else
             {
                 // Historical: Use Bar Approximation
                 // This runs once per bar Close
                 // v1.0.2: Skip if just reset (already initialized with this bar's volume)
                 // FIX: Use sessionHighPV/Vol (the variables that Values[0][0] uses)
                 if (sessionHighBarIdx != -1 && !_highJustReset) {
                     sessionHighPV += price * volume;
                     sessionHighVol += volume;
                 }
                 if (sessionLowBarIdx != -1 && !_lowJustReset) {
                     sessionLowPV += price * volume;
                     sessionLowVol += volume;
                 }
                 
                 // Reset flags after use
                 _highJustReset = false;
                 _lowJustReset = false;
                 
                // V_VWAP: Session-Specific Anchored VWAPs (Historical) - REMOVED
             }

               // 1. Calculate Current VWAP Values (using session variables for display)
               currentHighVWAP = (sessionHighVol > 0) ? sessionHighPV / sessionHighVol : High[0];
               currentLowVWAP = (sessionLowVol > 0) ? sessionLowPV / sessionLowVol : Low[0];
              
               hasHighVWAP = sessionHighBarIdx != -1 && sessionHighVol > 0;
               hasLowVWAP = sessionLowBarIdx != -1 && sessionLowVol > 0;

             // v1.0.24: Move "Liquidity Grabbed" label to new extreme
             if (highLiqGrabBarIdx >= 0 && !string.IsNullOrEmpty(highLiqGrabSessionName))
             {
                 // For High liquidity grab (short setup), track new HIGHS
                 if (High[0] > highLiqGrabExtreme)
                 {
                     highLiqGrabExtreme = High[0];
                     highLiqGrabBarIdx = CurrentBar;

                     // Redraw at new position
                     double atrOff = (atr != null && atr[0] > 0) ? atr[0] * LabelDistanceATR : TickSize * 10;
                     double newY = High[0] + atrOff;

                     if (ShowSignal1)
                     {
                         Draw.TriangleDown(this, "TakeHigh_" + highLiqGrabSessionName, true, 0, newY, SignalColor);
                         if (ShowSignalLabels)
                         {
                             string code = LabelDisplayMode == LabelMode.Custom ? CustomSignal1Text : "1";
                             SimpleFont font = new SimpleFont("Arial", LabelFontSize);
                             Draw.Text(this, "Sig1H_Txt_" + highLiqGrabSessionName, true, code, 0, newY, LabelTextOffset, SignalColor, font, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                         }
                     }
                 }
             }

             if (lowLiqGrabBarIdx >= 0 && !string.IsNullOrEmpty(lowLiqGrabSessionName))
             {
                 // For Low liquidity grab (long setup), track new LOWS
                 if (Low[0] < lowLiqGrabExtreme)
                 {
                     lowLiqGrabExtreme = Low[0];
                     lowLiqGrabBarIdx = CurrentBar;

                     // Redraw at new position
                     double atrOff = (atr != null && atr[0] > 0) ? atr[0] * LabelDistanceATR : TickSize * 10;
                     double newY = Low[0] - atrOff;

                     if (ShowSignal1)
                     {
                         Draw.TriangleUp(this, "TakeLow_" + lowLiqGrabSessionName, true, 0, newY, SignalColor);
                         if (ShowSignalLabels)
                         {
                             string code = LabelDisplayMode == LabelMode.Custom ? CustomSignal1Text : "1";
                             SimpleFont font = new SimpleFont("Arial", LabelFontSize);
                             Draw.Text(this, "Sig1L_Txt_" + lowLiqGrabSessionName, true, code, 0, newY, -LabelTextOffset, SignalColor, font, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                         }
                     }
                 }
             }

             // 2. Evaluate Signals (using calculated VWAPs)
             
              // V_CLEANUP: SIGNALS REMOVED (RESET)
              /* 
                 ALL SIGNAL LOGIC (High/Low/Detachment) DELETED
              */
              {
                  double hVwap = currentHighVWAP;
                  // V_VWAP: Use Session-Specific VWAP for Internal Signals - REMOVED
                 
                  // DEBUG STATE VARIABLES
                  string dbgText = "";
                  Brush dbgBrush = Brushes.Transparent;
                  double dbgOffset = 0;

                  if (highDetached)
                  {
                      dbgText = "D"; dbgBrush = Brushes.Cyan; dbgOffset = 40 * TickSize;
                  }

                  // UPDATED DETACHMENT LOGIC (Configurable Ticks)
                  // Condition: Close must be BELOW VWAP, AND High must be BELOW (VWAP - Buffer)
                  // This ensures the entire candle is "away" from the VWAP by a margin.
                  double detachThreshold = hVwap - (DetachmentTicks * TickSize);
                  
                  if (!highDetached && CurrentBar > 0 && Close[0] < hVwap && High[0] < detachThreshold)
                  {
                      highDetached = true;
                      // Update Debug State immediately if it flipped
                      dbgText = "D"; dbgBrush = Brushes.Cyan; dbgOffset = 40 * TickSize;
                  }

                  // Trigger: High >= VWAP
                  // Check Signal Condition (if detached OR if strictly forcing signal logic, here we rely on highDetached)
                  if (highDetached && high >= hVwap && !highSignalFired)
                  {
                      // Signal Fired -> Override Debug Label to 'E'
                      // Signal Fired -> Override Debug Label to 'E'
                      // Use Code if available
                      string sigCode = (lastUnlockedHighSession != null) ? GetSignalCode(lastUnlockedHighSession, "H") : "E";
                      dbgText = sigCode; dbgBrush = Brushes.Lime; dbgOffset = 60 * TickSize;
                      
                      bool isVisible = highHasTakenRelevant;
                      bool isTrendAllowed = true;
                      
                      // Removed Anti-Breakout Filter as per user request
                      // if (lastUnlockedHighSession != null && lastUnlockedHighSession.HighBrokenBarIdx == CurrentBar) isVisible = false;

                      // V_SYNC: ONE-SHOT RULE (Optional)
                      // V_SYNC: ONE-SHOT RULE Removed

                       if (isVisible && isTrendAllowed)
                       {
                           // ... Signal ...
                           string tag = "ShortSig" + CurrentBar;
                           
                           // V40: VISUAL SYNC
                           double yVal = hasHighVWAP ? currentHighVWAP : high; 
                         
                         // if (ShowTradeSetup)

                         {
                             // SMART TP CALCULATION
                             // 1. Identify Candidate Targets
                             double targetVWAP = hasLowVWAP ? currentLowVWAP : 0;
                             double targetSession = (lastUnlockedHighSession != null) ? lastUnlockedHighSession.Low : 0;
                             
                             double finalTP1 = 0;
                             double finalTP2 = 0;
                             bool tp1IsDyn = false;
                             bool tp2IsDyn = false;

                             // Logic:
                             // If we have both, check which is closer to Entry (High)
                             // Entry is at 'high' (or 'yVal').
                             if (targetVWAP != 0 && targetSession != 0)
                             {
                                 double distVWAP = Math.Abs(yVal - targetVWAP);
                                 double distSession = Math.Abs(yVal - targetSession);
                                 
                                 if (distSession < distVWAP)
                                 {
                                     // Session is CLOSER -> TP1
                                     finalTP1 = targetSession;
                                     finalTP2 = targetVWAP;
                                     tp2IsDyn = true; // VWAP is TP2
                                 }
                                 else
                                 {
                                     // VWAP is CLOSER (or same) -> TP1
                                     finalTP1 = targetVWAP;
                                     finalTP2 = targetSession;
                                     tp1IsDyn = true; // VWAP is TP1
                                 }
                             }
                             else if (targetVWAP != 0)
                             {
                                 finalTP1 = targetVWAP;
                                 tp1IsDyn = true;
                             }
                             else if (targetSession != 0)
                             {
                                 finalTP1 = targetSession;
                             }

                             // Visuals Removed
                             double slPrice = currentDayHigh + TickSize;
                             
                             // Add Trade Setup Tracking
                             TradeSetup trade = new TradeSetup();
                             trade.ID = ++tradeIdCounter;
                             trade.EntryBar = CurrentBar;
                             trade.EntryTime = Time[0];
                             trade.EntryPrice = high; // Entry at Touch
                             trade.IsLong = false;
                             trade.SL = slPrice;
                             trade.TP1 = finalTP1;
                             trade.TP2 = finalTP2;
                             trade.IsTP1Dynamic = tp1IsDyn;
                             trade.IsTP2Dynamic = tp2IsDyn;
                             
                             activeTrades.Add(trade);
                             Print(string.Format("RelativeVwap: Trade ADDED ID={0} at Bar {1} (Total Active: {2}, Entry: {3})", trade.ID, CurrentBar, activeTrades.Count, trade.EntryPrice));
                         }

                            Alert("AlertShort"+CurrentBar, Priority.High, "SHORT" + " Signal @ " + high, AlertSound, 10, Brushes.Black, Brushes.Red);
                            
                         highDetached = false; 
                         highSignalFired = true; // Lock
                         
                         // V_SYNC: Mark as Traded
                         if (lastUnlockedHighSession != null) lastUnlockedHighSession.IsHighTraded = true;
                   }
                 }
                  // V_LOGIC: Cancel Signal 3 if Opposing VWAP (Target) is hit first
                  // If we are waiting for a Short Entry (highSignal2Fired), but price hits the Low VWAP first -> CANCEL
                  if (highSignal2Fired && hasLowVWAP && Low[0] <= currentLowVWAP)
                  {
                      highSignal2Fired = false; 
                  }

                  // If we are waiting for a Long Entry (lowSignal2Fired), but price hits the High VWAP first -> CANCEL
                  if (lowSignal2Fired && hasHighVWAP && High[0] >= currentHighVWAP)
                  {
                      lowSignal2Fired = false;
                  }
                  
                  // MANUAL FIX: Auto-Reset Detachment on Touch
                  if (high >= hVwap) 
                  {
                      // V_SIGNAL_3: ENTRY TRIGGER (Arrow on Touch) -- v1.0.5: Synced with SessionLevels ATR-based positioning
                      if (highSignal2Fired)
                      {
                          // v1.0.5: Use ATR-based offset (same as SessionLevels DrawTriggerLabel)
                          double atrOffset = (atr != null && atr[0] > 0) ? atr[0] * LabelDistanceATR : TickSize * 10;
                          
                          // v1.0.5: Position relative to candle High (not VWAP) + offset
                          double arrowY = High[0] + atrOffset;

                          // v1.0.8: Use configurable SignalColor instead of session colors
                          Brush sigBrush = SignalColor;

                          // Arrow (if ShowSignal3)
                          if (ShowSignal3)
                          {
                              Draw.ArrowDown(this, "EntryH_" + CurrentBar, true, 0, arrowY, sigBrush);
                              
                              if (lastUnlockedHighSession != null && ShowSignalLabels)
                              {
                                   // Logic moved inside ShowSignal3 check
                                   string entryLabel = "";
                                   if (LabelDisplayMode == LabelMode.Simple) entryLabel = "3";
                                   else if (LabelDisplayMode == LabelMode.Custom) entryLabel = CustomSignal3Text;
                                   else 
                                   {
                                       entryLabel = GetSignalCode(lastUnlockedHighSession, "H");
                                       if (lastUnlockedHighSession.IsInternalHigh) entryLabel = "i" + entryLabel;
                                       entryLabel += "." + highAnchorSequence + ".1";
                                   }

                                   SimpleFont font = new SimpleFont("Arial", LabelFontSize);
                                   Draw.Text(this, "Sig3H_Txt_" + CurrentBar, true, entryLabel, 0, arrowY, LabelTextOffset, sigBrush, font, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                              }
                          }
                      }

                      highDetached = false;
                      Print(string.Format("[DEBUG FLAG] Bar:{0} | RESETTING highSignal2Fired ONLY (Touch VWAP) | high:{1:F2} >= hVwap:{2:F2} | Tracker:{3} stays set",
                          CurrentBar, high, hVwap, lastSignaledHighAnchorBar));
                      highSignal2Fired = false; // Reset Signal 2 on Touch (allows signal to reappear if bar closes without touching)
                      // v1.0.36: DO NOT reset lastSignaledHighAnchorBar - Signal 2 appears only ONCE per anchor
                      // Tracker only resets on: new anchor, price hits opposite session level, or breaks anchor bar
                      // If we reset, and we didn't just fire 'E' (dbgText != "E"), then we should NOT show 'D'.
                      if (dbgText == "D") dbgText = "";

                      // v1.0.27: Remove Signal 2 visuals ONLY if touched in SAME bar (permanence fix)
                      if (highSignal2BarIdx >= 0 && highSignal2BarIdx == CurrentBar)
                      {
                          int barsAgo = CurrentBar - highSignal2BarIdx;

                          // v1.0.26: File Log
                          LogToFile(string.Format("SIG2 SHORT CANCELLED | TouchedVWAP SAME BAR | High:{0:F2} | VWAP:{1:F2} | SignalBar:{2} | BarsAgo:{3}",
                              High[0], hVwap, highSignal2BarIdx, barsAgo), "CANCEL");

                          RemoveDrawObject("Sig2H_" + highSignal2BarIdx);
                          RemoveDrawObject("Sig2H_Txt_" + highSignal2BarIdx);
                          if (barsAgo >= 0 && barsAgo < CurrentBar)
                              BarBrushes[barsAgo] = null; // Unpaint that bar
                          highSignal2BarIdx = -1;

                          // v1.0.37: Only unpaint current bar if signal was THIS bar
                          BarBrushes[0] = null;
                      }

                      // v1.0.24: DO NOT reset lastSignaledHighAnchorBar - signal is dead for this anchor 
                  }
                 
                  // FINAL DRAW CALL
                  if (ShowDebugLabels && !string.IsNullOrEmpty(dbgText) && dbgText != "D") // Only show "D" or specialized debug. Don't show Signal Codes here.
                  {
                       // Draw.Text(this, "DebugHi" + CurrentBar, dbgText, 0, high + dbgOffset, dbgBrush); // DISABLED to prevent overlap with AddSignal
                  }

                  // V_SIGNAL_2: SECONDARY CONFIRMATION (Yellow Dot) - UNIQUE PER ANCHOR
                  // Condition: Active VWAP (Taken), Candle High BELOW VWAP by threshold
                  // CHECK: Have we signaled for THIS specific anchor yet?
                  if (highHasTakenRelevant && High[0] <= (hVwap - Signal2ThresholdTicks * TickSize))
                  {
                       // LOG DIAGNOSTICS FOR SIGNAL 2 SHORT
                       /*
                       Print(string.Format("DEBUG SIG2 SHORT: Bar={0} High={1} hVwap={2} Thresh={3} Diff={4}",
                           CurrentBar, High[0], hVwap, Signal2ThresholdTicks * TickSize, hVwap - High[0]));
                       */

                      // v1.0.33: DOUBLE CHECK - Both flag AND anchor tracker must allow signal
                      bool alreadyFired = highSignal2Fired;
                      bool alreadySignaledThisAnchor = (sessionHighBarIdx == lastSignaledHighAnchorBar);
                      bool canFire = !alreadyFired && !alreadySignaledThisAnchor;

                      Print(string.Format("[DEBUG FLAG] Bar:{0} | SHORT Check | Flag:{1} | AnchorSignaled:{2} | AnchorBar:{3} | LastSignaled:{4} | CanFire:{5}",
                          CurrentBar, alreadyFired, alreadySignaledThisAnchor, sessionHighBarIdx, lastSignaledHighAnchorBar, canFire));

                      if (canFire)
                      {
                          // v1.0.8: Paint Signal 2 candle yellow (only the first separation candle)
                          // FIX: Store the Index for persistent painting in Live/Tick mode
                          highSignal2BarIdx = CurrentBar;
                          BarBrushes[0] = Brushes.Yellow; // v1.0.34: Paint immediately when signal fires

                          // CRITICAL LOGGING: Confirming why this fired if user sees High > VWAP
                          Print(string.Format("[RelativeVwap-INDICATOR] SIG2 SHORT FIRED | NOW:{0} | CHART:{1} | Bar:{2} | High:{3:F2} | VWAP:{4:F2} | Thresh:{5} | Cond(H<=V-T):{6} | AnchorBar:{7}",
                              DateTime.Now, Time[0], CurrentBar, High[0], hVwap, Signal2ThresholdTicks, (High[0] <= (hVwap - Signal2ThresholdTicks * TickSize)), sessionHighBarIdx));

                          // v1.0.26: File Log
                          LogToFile(string.Format("SIG2 SHORT FIRED | High:{0:F2} | VWAP:{1:F2} | Sep:{2:F2} | Thresh:{3} | AnchorBar:{4} | LastSignaled:{5}",
                              High[0], hVwap, hVwap - High[0], Signal2ThresholdTicks, sessionHighBarIdx, lastSignaledHighAnchorBar), "SIGNAL2");

                          highAnchorSequence++;

                          // v1.0.8: Use configurable SignalColor instead of session colors
                          Brush sigBrush = SignalColor;

                          // v1.0.5: Use ATR-based offset (same as SessionLevels)
                          double atrOffset = (atr != null && atr[0] > 0) ? atr[0] * LabelDistanceATR : TickSize * 10;
                          
                          // v1.0.5: Position relative to candle High + offset
                          double dotY = High[0] + atrOffset;
                          
                          // Arrow (if ShowSignal2)
                          // Arrow (if ShowSignal2)
                          if (ShowSignal2)
                          {
                              Draw.ArrowDown(this, "Sig2H_" + CurrentBar, true, 0, dotY, sigBrush);

                              // Label: e.g. "UH1.1", "UH1.2"
                              if (lastUnlockedHighSession != null && ShowSignalLabels)
                              {
                                  string code = "";
                                  if (LabelDisplayMode == LabelMode.Simple) code = "2";
                                  else if (LabelDisplayMode == LabelMode.Custom) code = CustomSignal2Text;
                                  else 
                                  {
                                      code = GetSignalCode(lastUnlockedHighSession, "H");
                                      if (lastUnlockedHighSession.IsInternalHigh) code = "i" + code;
                                      code += "." + highAnchorSequence;
                                  }

                                  SimpleFont font = new SimpleFont("Arial", LabelFontSize);
                                  Draw.Text(this, "Sig2H_Txt_" + CurrentBar, true, code, 0, dotY, LabelTextOffset, sigBrush, font, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                              }
                          }

                          // v1.0.28: Force refresh to show signal immediately in playback/realtime
                          // v1.0.32: Fix threading error - must call from UI thread
                          if (ChartControl != null)
                          {
                              ChartControl.Dispatcher.InvokeAsync(() => ChartControl.InvalidateVisual());
                          }

                          highSignal2Fired = true; // v1.0.31: Mark signal as fired (prevents multiple signals)
                          lastSignaledHighAnchorBar = sessionHighBarIdx; // v1.0.33: Track which anchor was signaled
                          Print(string.Format("[DEBUG FLAG] Bar:{0} | SET highSignal2Fired=TRUE + lastSignaledHighAnchorBar={1}", CurrentBar, sessionHighBarIdx));
                      }
                  }
                  
                  // v1.0.29: Persistent Painting - paint the signal bar even after it closes
                  if (highSignal2BarIdx >= 0)
                  {
                      int barsAgo = CurrentBar - highSignal2BarIdx;
                      if (barsAgo >= 0 && barsAgo < Bars.Count)
                      {
                          BarBrushes[barsAgo] = Brushes.Yellow;
                      }
                  }
              }

             // --- Low VWAP Logic (Support -> Long Signal) ---
             if (hasLowVWAP && (TradeDirection == TradeDirectionMode.Both || TradeDirection == TradeDirectionMode.LongOnly))
             {
                  double lVwap = currentLowVWAP;
                  // V_VWAP: Use Session-Specific VWAP for Internal Signals - REMOVED

                  // DEBUG STATE VARIABLES
                  string dbgText = "";
                  Brush dbgBrush = Brushes.Transparent;
                  double dbgOffset = 0;
                 
                  // Initial (Pre-Calc)
                   if (lowDetached)
                   {
                       dbgText = "D"; dbgBrush = Brushes.Cyan; dbgOffset = 40 * TickSize;
                   }

                  // UPDATED DETACHMENT LOGIC (Configurable Ticks)
                  // Condition: Close must be ABOVE VWAP, AND Low must be ABOVE (VWAP + Buffer)
                  double detachThreshold = lVwap + (DetachmentTicks * TickSize);
                  
                  if (!lowDetached && CurrentBar > 0 && Close[0] > lVwap && Low[0] > detachThreshold)
                  {
                       lowDetached = true;
                       // Update Debug State immediately
                       dbgText = "D"; dbgBrush = Brushes.Cyan; dbgOffset = 40 * TickSize;
                   }
                      
                 // Trigger: Low <= VWAP
                 if (lowDetached && low <= lVwap && !lowSignalFired)
                 {
                     // Signal Fired -> Override Debug Label to 'E'
                     // Signal Fired -> Override Debug Label to 'E'
                     // Use Code if available
                      string sigCode = (lastUnlockedLowSession != null) ? GetSignalCode(lastUnlockedLowSession, "L") : "E";
                      dbgText = sigCode; dbgBrush = Brushes.Lime; dbgOffset = 60 * TickSize;

                     bool isVisible = lowHasTakenRelevant;
                     bool isTrendAllowed = true;
                     
                     // V13: ANTI-BREAKOUT FILTER (Sync) - REMOVED // TEST EDIT
                     // if (lastUnlockedLowSession != null && lastUnlockedLowSession.LowBrokenBarIdx == CurrentBar) isVisible = false;
                     
                      // V_SYNC: ONE-SHOT RULE (Optional)
                      // V_SYNC: ONE-SHOT RULE Removed



                     if (isVisible && isTrendAllowed)
                     {
                          // V38 Filter Removed for Low Signals too
                          {
                              // ... Signal ...
                              string tag = "LongSig" + CurrentBar;
                              
                              // V40: VISUAL SYNC
                              double yVal = hasLowVWAP ? currentLowVWAP : low;
                         
                             // if (ShowTradeSetup) { ... } REMOVED

                                 // SMART TP CALCULATION
                                 // 1. Identify Candidate Targets
                                 double targetVWAP = hasHighVWAP ? currentHighVWAP : 0;
                                 double targetSession = (lastUnlockedLowSession != null) ? lastUnlockedLowSession.High : 0;
                                 
                                 double finalTP1 = 0;
                                 double finalTP2 = 0;
                                 bool tp1IsDyn = false;
                                 bool tp2IsDyn = false;
    
                                 // Logic:
                                 // If we have both, check which is closer to Entry (Low)
                                 if (targetVWAP != 0 && targetSession != 0)
                                 {
                                     double distVWAP = Math.Abs(targetVWAP - yVal);
                                     double distSession = Math.Abs(targetSession - yVal);
                                     
                                     if (distSession < distVWAP)
                                     {
                                         // Session is CLOSER -> TP1
                                         finalTP1 = targetSession;
                                         finalTP2 = targetVWAP;
                                         tp2IsDyn = true; // VWAP is TP2
                                     }
                                     else
                                     {
                                         // VWAP is CLOSER (or same) -> TP1
                                         finalTP1 = targetVWAP;
                                         finalTP2 = targetSession;
                                         tp1IsDyn = true; // VWAP is TP1
                                     }
                                 }
                                 else if (targetVWAP != 0)
                                 {
                                     finalTP1 = targetVWAP;
                                     tp1IsDyn = true;
                                 }
                                 else if (targetSession != 0)
                                 {
                                     finalTP1 = targetSession;
                                 }

                                  // Add Trade Setup Tracking
                                  TradeSetup trade = new TradeSetup();
                                  trade.ID = ++tradeIdCounter;
                                  trade.EntryBar = CurrentBar;
                                  trade.EntryTime = Time[0];
                                  trade.EntryPrice = low;
                                  trade.IsLong = true;
                                  trade.SL = currentDayLow - TickSize;
                                  trade.TP1 = finalTP1;
                                  trade.TP2 = finalTP2;
                                  trade.IsTP1Dynamic = tp1IsDyn;
                                  trade.IsTP2Dynamic = tp2IsDyn;
                                  
                                  activeTrades.Add(trade);
                              }

                             if (EnableAlerts)
                                Alert("AlertLong"+CurrentBar, Priority.High, "LONG" + " Signal @ " + low, AlertSound, 10, Brushes.Black, Brushes.Lime);
                                
                             lowDetached = false; 
                             lowSignalFired = true; // Lock
                             
                             // V_SYNC: Mark as Traded
                             if (lastUnlockedLowSession != null) lastUnlockedLowSession.IsLowTraded = true;
                     } // End isVisible
                 } // End lowDetached condition

                  // MANUAL FIX: Auto-Reset Detachment on Touch for Lows
                  if (low <= lVwap) 
                  {
                      // V_SIGNAL_3: ENTRY TRIGGER (Arrow on Touch) -- v1.0.5: Synced with SessionLevels ATR-based positioning
                      if (lowSignal2Fired)
                      {
                          // v1.0.5: Use ATR-based offset (same as SessionLevels DrawTriggerLabel)
                          double atrOffset = (atr != null && atr[0] > 0) ? atr[0] * LabelDistanceATR : TickSize * 10;
                          
                          // v1.0.5: Position relative to candle Low (not VWAP) + offset
                          double arrowY = Low[0] - atrOffset;

                          // v1.0.8: Use configurable SignalColor instead of session colors
                          Brush sigBrush = SignalColor;

                          // Label
                          // Arrow (if ShowSignal3)
                          if (ShowSignal3)
                          {
                              Draw.ArrowUp(this, "EntryL_" + CurrentBar, true, 0, arrowY, sigBrush);
                              
                              if (lastUnlockedLowSession != null && ShowSignalLabels)
                              {
                                   // Logic moved inside ShowSignal3 check
                                   string entryLabel = "";
                                   if (LabelDisplayMode == LabelMode.Simple) entryLabel = "3";
                                   else if (LabelDisplayMode == LabelMode.Custom) entryLabel = CustomSignal3Text;
                                   else 
                                   {
                                       entryLabel = GetSignalCode(lastUnlockedLowSession, "L");
                                       if (lastUnlockedLowSession.IsInternalLow) entryLabel = "i" + entryLabel;
                                       entryLabel += "." + lowAnchorSequence + ".1";
                                   }

                                   SimpleFont font = new SimpleFont("Arial", LabelFontSize);
                                   Draw.Text(this, "Sig3L_Txt_" + CurrentBar, true, entryLabel, 0, arrowY, -LabelTextOffset, sigBrush, font, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                              }
                          }
                      }

                      lowDetached = false;
                      Print(string.Format("[DEBUG FLAG] Bar:{0} | RESETTING lowSignal2Fired ONLY (Touch VWAP) | low:{1:F2} <= lVwap:{2:F2} | Tracker:{3} stays set",
                          CurrentBar, low, lVwap, lastSignaledLowAnchorBar));
                      lowSignal2Fired = false; // Reset Signal 2 on Touch (allows signal to reappear if bar closes without touching)
                      // v1.0.36: DO NOT reset lastSignaledLowAnchorBar - Signal 2 appears only ONCE per anchor
                      // Tracker only resets on: new anchor, price hits opposite session level, or breaks anchor bar
                      // If we reset, and we didn't just fire 'E' (dbgText != "E"), then we should NOT show 'D'.
                      if (dbgText == "D") dbgText = "";

                      // v1.0.27: Remove Signal 2 visuals ONLY if touched in SAME bar (permanence fix)
                      if (lowSignal2BarIdx >= 0 && lowSignal2BarIdx == CurrentBar)
                      {
                          int barsAgo = CurrentBar - lowSignal2BarIdx;

                          // v1.0.26: File Log
                          LogToFile(string.Format("SIG2 LONG CANCELLED | TouchedVWAP SAME BAR | Low:{0:F2} | VWAP:{1:F2} | SignalBar:{2} | BarsAgo:{3}",
                              Low[0], lVwap, lowSignal2BarIdx, barsAgo), "CANCEL");

                          RemoveDrawObject("Sig2L_" + lowSignal2BarIdx);
                          RemoveDrawObject("Sig2L_Txt_" + lowSignal2BarIdx);
                          if (barsAgo >= 0 && barsAgo < CurrentBar)
                              BarBrushes[barsAgo] = null; // Unpaint that bar
                          lowSignal2BarIdx = -1;

                          // v1.0.37: Only unpaint current bar if signal was THIS bar
                          BarBrushes[0] = null;
                      }

                      // v1.0.24: DO NOT reset lastSignaledLowAnchorBar - signal is dead for this anchor
                  }

                 // FINAL DRAW CALL
                 if (ShowDebugLabels && !string.IsNullOrEmpty(dbgText) && dbgText != "D") // Only show "D" or specialized debug.
                 {
                      // Draw.Text(this, "DebugLow" + CurrentBar, dbgText, 0, low - dbgOffset, dbgBrush); // DISABLED to prevent overlap
                 }

                  // V_SIGNAL_2: SECONDARY CONFIRMATION (Yellow Dot) - UNIQUE PER ANCHOR
                  // Condition: Active VWAP (Taken), Candle Low ABOVE VWAP by threshold
                  // CHECK: Have we signaled for THIS specific anchor yet?
                  if (lowHasTakenRelevant && Low[0] >= (lVwap + Signal2ThresholdTicks * TickSize))
                  {
                      // v1.0.33: DOUBLE CHECK - Both flag AND anchor tracker must allow signal
                      bool alreadyFired = lowSignal2Fired;
                      bool alreadySignaledThisAnchor = (sessionLowBarIdx == lastSignaledLowAnchorBar);
                      bool canFire = !alreadyFired && !alreadySignaledThisAnchor;

                      Print(string.Format("[DEBUG FLAG] Bar:{0} | LONG Check | Flag:{1} | AnchorSignaled:{2} | AnchorBar:{3} | LastSignaled:{4} | CanFire:{5}",
                          CurrentBar, alreadyFired, alreadySignaledThisAnchor, sessionLowBarIdx, lastSignaledLowAnchorBar, canFire));

                      if (canFire)
                      {
                          // v1.0.8: Paint Signal 2 candle yellow (only the first separation candle)
                          // FIX: Store the Index for persistent painting in Live/Tick mode
                          lowSignal2BarIdx = CurrentBar;
                          BarBrushes[0] = Brushes.Yellow; // v1.0.34: Paint immediately when signal fires

                          // CRITICAL LOGGING: Confirming why this fired
                          Print(string.Format("[RelativeVwap-INDICATOR] SIG2 LONG FIRED | NOW:{0} | CHART:{1} | Bar:{2} | Low:{3:F2} | VWAP:{4:F2} | Thresh:{5} | Cond(L>=V+T):{6} | AnchorBar:{7}",
                              DateTime.Now, Time[0], CurrentBar, Low[0], lVwap, Signal2ThresholdTicks, (Low[0] >= (lVwap + Signal2ThresholdTicks * TickSize)), sessionLowBarIdx));

                          // v1.0.26: File Log
                          LogToFile(string.Format("SIG2 LONG FIRED | Low:{0:F2} | VWAP:{1:F2} | Sep:{2:F2} | Thresh:{3} | AnchorBar:{4} | LastSignaled:{5}",
                              Low[0], lVwap, Low[0] - lVwap, Signal2ThresholdTicks, sessionLowBarIdx, lastSignaledLowAnchorBar), "SIGNAL2");

                          lowAnchorSequence++;

                          // v1.0.8: Use configurable SignalColor instead of session colors
                          Brush sigBrush = SignalColor;

                          // v1.0.5: Use ATR-based offset (same as SessionLevels)
                          double atrOffset = (atr != null && atr[0] > 0) ? atr[0] * LabelDistanceATR : TickSize * 10;
                          
                          // v1.0.5: Position relative to candle Low + offset
                          double dotY = Low[0] - atrOffset;
                          
                          // Arrow (if ShowSignal2)
                          // Arrow (if ShowSignal2)
                          if (ShowSignal2)
                          {
                              Draw.ArrowUp(this, "Sig2L_" + CurrentBar, true, 0, dotY, sigBrush);

                              // Label: e.g. "UL1.1", "UL1.2"
                              if (lastUnlockedLowSession != null && ShowSignalLabels)
                              {
                                  string code = "";
                                  if (LabelDisplayMode == LabelMode.Simple) code = "2";
                                  else if (LabelDisplayMode == LabelMode.Custom) code = CustomSignal2Text;
                                  else 
                                  {
                                      code = GetSignalCode(lastUnlockedLowSession, "L");
                                      if (lastUnlockedLowSession.IsInternalLow) code = "i" + code;
                                      code += "." + lowAnchorSequence;
                                  }

                                  SimpleFont font = new SimpleFont("Arial", LabelFontSize);
                                  Draw.Text(this, "Sig2L_Txt_" + CurrentBar, true, code, 0, dotY, -LabelTextOffset, sigBrush, font, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                              }
                          }

                          // v1.0.28: Force refresh to show signal immediately in playback/realtime
                          // v1.0.32: Fix threading error - must call from UI thread
                          if (ChartControl != null)
                          {
                              ChartControl.Dispatcher.InvokeAsync(() => ChartControl.InvalidateVisual());
                          }

                          lowSignal2Fired = true; // v1.0.31: Mark signal as fired (prevents multiple signals)
                          lastSignaledLowAnchorBar = sessionLowBarIdx; // v1.0.33: Track which anchor was signaled
                          Print(string.Format("[DEBUG FLAG] Bar:{0} | SET lowSignal2Fired=TRUE + lastSignaledLowAnchorBar={1}", CurrentBar, sessionLowBarIdx));
                      }
                  }
                  
                  // v1.0.29: Persistent Painting - paint the signal bar even after it closes
                  if (lowSignal2BarIdx >= 0)
                  {
                      int barsAgo = CurrentBar - lowSignal2BarIdx;
                      if (barsAgo >= 0 && barsAgo < Bars.Count)
                      {
                          BarBrushes[barsAgo] = Brushes.Yellow;
                      }
                  }
              }


             // Version Label (Always Visible)
             if (CurrentBar == Bars.Count - 1)
             {
                 Draw.TextFixed(this, "VersionLabel", "RelativeVwap v" + VERSION, TextPosition.TopLeft, Brushes.White, new SimpleFont("Arial", 12), Brushes.Black, Brushes.Transparent, 100);
             }

             // Status Overlay
             if (ShowDebugLabels && (CurrentBar == Bars.Count - 1))
             {
                 string status = string.Format("RelativeVwap v{0}\nDEBUG STATUS\nTime: {1}\nHigh Active: {2} Locked: {3}\nLow Active: {4} Locked: {5}", VERSION, Time[0], highHasTakenRelevant, highSignalFired, lowHasTakenRelevant, lowSignalFired);
                 Draw.TextFixed(this, "DebugStatus", status, TextPosition.BottomRight, Brushes.White, new SimpleFont("Arial", 12), Brushes.Black, Brushes.Transparent, 100);
             }
             
             }
             catch (Exception ex)
             {
                 Print("RelativeVwap Indicator CRASH: " + ex.Message + " | Stack: " + ex.StackTrace);
             }
         }

        private void ManageTrades()
        {
             // V_CLEANUP: MANAGE TRADES DISABLED (RESET)
             if (activeTrades == null) return;
             /* 
             LOGIC REMOVED 
             */
            
            foreach (var trade in activeTrades)
            {
                if (trade.IsClosed) continue;
                
                // DYNAMIC TP UPDATE
                if (trade.IsTP1Dynamic)
                {
                    // If Long, TP1 was High VWAP? Or Session? 
                    // If it's dynamic, it tracks the VWAP.
                    // Long Target -> High VWAP. Short Target -> Low VWAP.
                    if (trade.IsLong) trade.TP1 = currentHighVWAP;
                    else trade.TP1 = currentLowVWAP;
                }
                
                if (trade.IsTP2Dynamic)
                {
                    if (trade.IsLong) trade.TP2 = currentHighVWAP;
                    else trade.TP2 = currentLowVWAP;
                }
                
                double currentHigh = High[0];
                double currentLow = Low[0];
                
                // Track MFE/MAE
                if (trade.IsLong)
                {
                    double potentialProfit = (currentHigh - trade.EntryPrice) * Instrument.MasterInstrument.PointValue;
                    double potentialLoss = (trade.EntryPrice - currentLow) * Instrument.MasterInstrument.PointValue; // Loss is positive number here for magnitude
                    
                    if (potentialProfit > trade.MFE) trade.MFE = potentialProfit;
                    if (potentialLoss > trade.MAE) trade.MAE = potentialLoss;
                }
                else
                {
                    double potentialProfit = (trade.EntryPrice - currentLow) * Instrument.MasterInstrument.PointValue;
                    double potentialLoss = (currentHigh - trade.EntryPrice) * Instrument.MasterInstrument.PointValue;
                    
                    if (potentialProfit > trade.MFE) trade.MFE = potentialProfit;
                    if (potentialLoss > trade.MAE) trade.MAE = potentialLoss;
                }
                
                if (trade.IsLong)
                {
                    // Check SL
                    if (currentLow <= trade.SL)
                    {
                        // DrawConnectionLine(trade, trade.SL, SLText, SLColor, "SL");
                        if (ShowDebugLabels) Print("Trade " + trade.ID + " LONG SL Hit! Low: " + currentLow + " <= SL: " + trade.SL);
                        
                        trade.SLHit = true;
                        trade.IsClosed = true;
                        
                        // Treat as 2 contracts logic if TP2 exists, else 1
                        bool twoContracts = (trade.TP2 != 0);
                        
                        if (twoContracts) 
                        {
                            // If TP1 already hit, only 1 contract stopped out
                           if (trade.TP1Hit && !trade.TP2Hit) 
                               trade.RealizedPnL += (trade.SL - trade.EntryPrice) * Instrument.MasterInstrument.PointValue;
                           else if (!trade.TP1Hit && !trade.TP2Hit) // None hit, 2 stopped out
                               trade.RealizedPnL += 2 * (trade.SL - trade.EntryPrice) * Instrument.MasterInstrument.PointValue;
                        }
                        else
                        {
                             // Single Contract
                             trade.RealizedPnL += (trade.SL - trade.EntryPrice) * Instrument.MasterInstrument.PointValue;
                        }
                        
                        trade.ExitPrice = trade.SL;
                        trade.ExitTime = Time[0];
                        trade.ExitBar = CurrentBar;
                    }
                    else
                    {
                        // Check TP1
                        if (!trade.TP1Hit && trade.TP1 != 0 && currentHigh >= trade.TP1)
                        {
                            // DrawConnectionLine(trade, trade.TP1, TP1Text, TP1Color, "TP1");
                            trade.TP1Hit = true;
                            trade.RealizedPnL += (trade.TP1 - trade.EntryPrice) * Instrument.MasterInstrument.PointValue;
                            
                            // Move to Break Even for remaining position
                            if (trade.TP2 != 0) trade.SL = trade.EntryPrice;
                        }
                        // Check TP2
                        if (!trade.TP2Hit && trade.TP2 != 0 && currentHigh >= trade.TP2)
                        {
                            // DrawConnectionLine(trade, trade.TP2, TP2Text, TP2Color, "TP2");
                            trade.TP2Hit = true;
                            trade.RealizedPnL += (trade.TP2 - trade.EntryPrice) * Instrument.MasterInstrument.PointValue;
                        }
                        
                        // Close if both TPs hit, or if SL hit (handled above)
                        if ((trade.TP1 == 0 || trade.TP1Hit) && (trade.TP2 == 0 || trade.TP2Hit)) 
                        {
                            trade.IsClosed = true;
                            trade.ExitPrice = trade.TP2Hit ? trade.TP2 : trade.TP1;
                            trade.ExitTime = Time[0];
                            trade.ExitBar = CurrentBar;
                        }
                    }
                }
                else // Short
                {
                    // Check SL
                    if (currentHigh >= trade.SL)
                    {
                        // DrawConnectionLine(trade, trade.SL, SLText, SLColor, "SL");
                        if (ShowDebugLabels) Print("Trade " + trade.ID + " SHORT SL Hit! High: " + currentHigh + " >= SL: " + trade.SL);

                        trade.SLHit = true;
                        trade.IsClosed = true;
                        
                        bool twoContracts = (trade.TP2 != 0);
                        
                        if (twoContracts) 
                        {
                           if (trade.TP1Hit && !trade.TP2Hit) 
                               trade.RealizedPnL += (trade.EntryPrice - trade.SL) * Instrument.MasterInstrument.PointValue;
                           else if (!trade.TP1Hit && !trade.TP2Hit) 
                               trade.RealizedPnL += 2 * (trade.EntryPrice - trade.SL) * Instrument.MasterInstrument.PointValue;
                        }
                        else
                        {
                             trade.RealizedPnL += (trade.EntryPrice - trade.SL) * Instrument.MasterInstrument.PointValue;
                        }

                        trade.ExitPrice = trade.SL;
                        trade.ExitTime = Time[0];
                    }
                    else
                    {
                         // Debug near miss
                         if (ShowDebugLabels && currentHigh >= trade.SL - 4 * TickSize)
                             Print("Trade " + trade.ID + " SHORT SL Near Miss. High: " + currentHigh + " SL: " + trade.SL);

                         // Check TP1
                        if (!trade.TP1Hit && trade.TP1 != 0 && currentLow <= trade.TP1)
                        {
                            // DrawConnectionLine(trade, trade.TP1, TP1Text, TP1Color, "TP1");
                            trade.TP1Hit = true;
                            trade.RealizedPnL += (trade.EntryPrice - trade.TP1) * Instrument.MasterInstrument.PointValue;
                            
                            // Move to Break Even for remaining position
                            if (trade.TP2 != 0) trade.SL = trade.EntryPrice;
                        }
                        // Check TP2
                        if (!trade.TP2Hit && trade.TP2 != 0 && currentLow <= trade.TP2)
                        {
                            // DrawConnectionLine(trade, trade.TP2, TP2Text, TP2Color, "TP2");
                            trade.TP2Hit = true;
                            trade.RealizedPnL += (trade.EntryPrice - trade.TP2) * Instrument.MasterInstrument.PointValue;
                        }
                        
                        if ((trade.TP1 == 0 || trade.TP1Hit) && (trade.TP2 == 0 || trade.TP2Hit)) 
                        {
                            trade.IsClosed = true;
                            trade.ExitPrice = trade.TP2Hit ? trade.TP2 : trade.TP1;
                             trade.ExitTime = Time[0];
                             trade.ExitBar = CurrentBar;
                        }
                    }
                }
            }
        }
        
        private void DrawConnectionLine(TradeSetup trade, double price, string label, Brush brush, string tagSuffix)
        {
            return; // V_CLEANUP: Disabled all visual drawing for trades
            /*
            // FORCE TAG UNIQUENESS...
            string tag = "Trade_" + trade.ID + "_" + tagSuffix;
            int barsAgo = CurrentBar - trade.EntryBar;
            
            // Draw Line
            Draw.Line(this, tag, false, barsAgo, trade.EntryPrice, 0, price, brush, ConnectionLineStyle, TradeLineWidth);
            
            // Calculate PnL for this specific leg
            double diff = trade.IsLong ? (price - trade.EntryPrice) : (trade.EntryPrice - price);
            double pnl = diff * Instrument.MasterInstrument.PointValue;
            
            // Styling
            Brush pnlColor = pnl >= 0 ? Brushes.LimeGreen : Brushes.RoyalBlue;
            SimpleFont font = new SimpleFont("Arial", TradeTextSize + 5) { Bold = true }; // Bigger and Bold
            
            // Stacking Logic to prevent overlap
            // Base offset
            double baseOffset = TextSeparationTicks * TickSize; // Used to be fixed 30
            double step = 15 * TickSize;
            double stackIndex = 0;
            
            if (label.Contains("TP2")) stackIndex = 1;
            else if (label.Contains("SL")) stackIndex = 2; // Show SL furthest away
            
            double totalOffset = baseOffset + (stackIndex * step);
            double yPos = trade.IsLong ? Low.GetValueAt(trade.EntryBar) - totalOffset : High.GetValueAt(trade.EntryBar) + totalOffset;
            
            string pnlText = string.Format("{0}: {1:C2}", label, pnl);
            
            // Unique Tag for Text so they don't overwrite each other
            string textTag = "TradePnL_" + trade.ID + "_" + tagSuffix;
            
            // Draw Text
            // Use pnlColor for text, Black background with 50% opacity as requested
            // Argument order: textBrush, font, alignment, outlineBrush, areaBrush, areaOpacity
                            // Draw Text REMOVED per user request (Minimalist)
                            // Draw.Text(this, textTag, false, pnlText, barsAgo, yPos, 0, pnlColor, font, TextAlignment.Center, Brushes.Black, Brushes.Black, 50);
                            // Draw.Text(this, textTag, false, pnlText, barsAgo, yPos, 0, pnlColor, font, TextAlignment.Center, Brushes.Black, Brushes.Black, 50);
            */
        }

        private int GetBusinessDays(DateTime start, DateTime end)
        {
            if (start.Date > end.Date) return 0;
            
            int count = 0;
            DateTime d = start.Date;
            while (d < end.Date)
            {
                d = d.AddDays(1);
                // Count if it's a weekday (Mon-Fri)
                if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                {
                    count++;
                }
            }
            return count;
        }

        private string GetSignalCode(SessionLevelInfo session, string levelType)
        {
            if (session == null) return "";
            
            // Region
            string r = "X";
            if (session.Name.StartsWith("Asia")) r = "A";
            else if (session.Name.StartsWith("Europe")) r = "E";
            else if (session.Name.StartsWith("USA")) r = "U";
            
            // Days Ago - Weekday Logic (Business Days)
            // 1. Resolve Trading Days (Normalize Sunday -> Monday)
            DateTime currentTradingDay = Time[0].Date;
            DateTime sessionTradingDay = session.SessionDate.Date;

            if (sessionIterator != null)
            {
                 // Try to normalize to Trading Day (handles Sunday 19:00 -> Monday)
                 try { currentTradingDay = sessionIterator.GetTradingDay(Time[0]); } catch {}
                 
                 if (session.StartBarIdx >= 0 && session.StartBarIdx < Bars.Count)
                 {
                     try { sessionTradingDay = sessionIterator.GetTradingDay(Bars.GetTime(session.StartBarIdx)); } catch {}
                 }
            }
            
            // 2. Count Business Days
            int days = GetBusinessDays(sessionTradingDay, currentTradingDay);
            
            // Debug check for the user's "UH0" report
            if (ShowDebugLabels && days == 0 && (currentTradingDay - sessionTradingDay).TotalDays > 1)
            {
                Print(string.Format("GetSignalCode DEBUG: Days=0 but diff>1? Curr={0} Sess={1}", currentTradingDay, sessionTradingDay));
            }

            return string.Format("{0}{1}{2}", r, levelType, days);
        }

        private void CheckTouches(List<SessionLevelInfo> sessions)
        {
            if (sessions == null) return;
            double high = High[0];
            double low = Low[0];
            DateTime today = Time[0].Date;
            

            foreach (var session in sessions)
            {
                if (ShowDebugLabels && (Math.Abs(low - session.Low) <= 10 * TickSize || Math.Abs(high - session.High) <= 10 * TickSize))
                {
                    Print(string.Format("Check: {0} {1} Active:{2} H:{3}({4}) L:{5}({6}) Now:{7}/{8}", 
                        session.Name, session.SessionDate.ToShortDateString(), session.IsActive, 
                        session.High, session.HighBrokenBarIdx, session.Low, session.LowBrokenBarIdx, high, low));
                }

                // Sanity Check
                if (session.High <= 0 || session.Low <= 0) continue;
                
                // V_SYNC: ALLOW TRADES DURING ACTIVE SESSION (MATCH STRATEGY)
                {
                    // Check High Break (Resistance)
                    if (session.HighBrokenBarIdx == -1 && high > session.High) 
                    {
                        Print(string.Format("RelativeVwap DEBUG: HIGH BREAK! Name={0} Bar={1} High={2} SessionHigh={3} TradesCount={4}", 
                            session.Name, CurrentBar, high, session.High, (activeTrades != null ? activeTrades.Count : -1)));

                        session.HighBrokenBarIdx = CurrentBar;
                        
                        // If this is the FIRST time we detect a High break for this VWAP session
                        if (!highHasTakenRelevant) highFirstBreakIdx = CurrentBar;
                        
                        highHasTakenRelevant = true;
                        highSignalFired = false; // UNLOCK SIGNAL (New Level Hit)
                        lastUnlockedHighSession = session; // FIX: Store session for TP2 Logic
                        highAnchorSequence = 0; // RESET SEQUENCE TO 0
                        // v1.0.36: Reset OPPOSITE tracker when hitting session level (allows new signals on opposite side)
                        lastSignaledLowAnchorBar = -1; // Reset LONG tracker when hitting HIGH level
                        Print(string.Format("[DEBUG RESET] Bar:{0} | Session HIGH broken | Reset lastSignaledLowAnchorBar to -1", CurrentBar));

                        // V_LOGIC: Hierarchy Check (Type A vs Type B) -> REMOVED (All signals are standard)
                        // session.IsInternalHigh = ...

                        highDetached = false; // SYNC: Reset Detachment on Break
                        
                        // V_LOGIC: Strategy Filters (High Break = Long?)
                        // Assumption: High Break is a Breakout Long.
                        
                        // 1. Trade Direction Filter
                        if (TradeDirection == TradeDirectionMode.ShortOnly) return; 

                        // 2. Re-entry Filter
                        // Removed per user request


                        // 3. Alerts
                        if (EnableAlerts && !string.IsNullOrEmpty(AlertSound))
                        {
                            try { PlaySound(NinjaTrader.Core.Globals.InstallDir + @"\sounds\" + AlertSound); } catch {}
                        }

                        session.HighTradeCount++; // Increment Counter
                        
                        // Generate Code
                        string code = "";
                        if (LabelDisplayMode == LabelMode.Custom) code = CustomSignal1Text;
                        else if (LabelDisplayMode == LabelMode.Simple) code = "1";
                        else 
                        {
                            code = GetSignalCode(session, "H");
                            // if (session.IsInternalHigh) code = "i" + code; // REMOVED
                        }

                        LastSignalCode = code;

                        // v1.0.8: Use configurable SignalColor instead of session colors
                        Brush sigBrush = SignalColor;

                        // V_VISUAL: SIGNAL 1 - TAKE LEVEL (RESISTANCE) - v1.0.5: Synced with SessionLevels ATR-based positioning
                        double atrOffset = (atr != null && atr[0] > 0) ? atr[0] * LabelDistanceATR : TickSize * 10;

                        // v1.0.5: Position relative to candle High + offset
                        double triY = high + atrOffset;

                        // Triangle (if ShowSignal1)
                        if (ShowSignal1)
                        {
                            // v1.0.24: Use session-based tag (not CurrentBar) so we can move the label
                            Draw.TriangleDown(this, "TakeHigh_" + session.Name, true, 0, triY, sigBrush);

                            // Label (if ShowSignalLabels)
                            if (ShowSignalLabels)
                            {
                                SimpleFont font = new SimpleFont("Arial", LabelFontSize);
                                Draw.Text(this, "Sig1H_Txt_" + session.Name, true, code, 0, triY, LabelTextOffset, sigBrush, font, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                            }

                            // v1.0.24: Track position for movable label
                            highLiqGrabBarIdx = CurrentBar;
                            highLiqGrabExtreme = high;
                            highLiqGrabSessionName = session.Name;
                        }


                // V_VISUAL: ADD TRADE LINE
                // if (ShowTradeSetup && activeTrades != null) { ... } REMOVED

                     // HIGH SIDE TRADES
                     double entryPxHigh = session.High + TickSize;
                     double slPxHigh = session.Low - TickSize;
                     
                     TradeSetup newTrade = new TradeSetup {
                         ID = ++tradeIdCounter,
                         EntryBar = CurrentBar,
                         EntryPrice = entryPxHigh,
                         EntryTime = Time[0],
                         IsLong = true,
                         SL = slPxHigh,
                         TP1 = 0, 
                         TP2 = 0
                     };
                     activeTrades.Add(newTrade);
                     Print(string.Format("RelativeVwap: Visual Trade ADDED (Long) ID={0} at {1}", newTrade.ID, entryPxHigh));
                // } REMOVED ORPHAN BRACE

                    }
                    
                    // Check Low Break (Support)
                    // MANUAL FIX: Use STRICT inequality (<)
                    if (session.LowBrokenBarIdx == -1 && low < session.Low) 
                    {
                         Print(string.Format("RelativeVwap DEBUG: LOW BREAK! Name={0} Bar={1} Low={2} SessionLow={3} TradesCount={4}", 
                             session.Name, CurrentBar, low, session.Low, (activeTrades != null ? activeTrades.Count : -1)));

                         session.LowBrokenBarIdx = CurrentBar;
                         
                         if (!lowHasTakenRelevant) lowFirstBreakIdx = CurrentBar;
                         
                         lowHasTakenRelevant = true;
                         lowSignalFired = false; // UNLOCK SIGNAL
                         lastUnlockedLowSession = session; // FIX: Store session for TP2 Logic
                         lowAnchorSequence = 0; // RESET
                         // v1.0.36: Reset OPPOSITE tracker when hitting session level (allows new signals on opposite side)
                         lastSignaledHighAnchorBar = -1; // Reset SHORT tracker when hitting LOW level
                         Print(string.Format("[DEBUG RESET] Bar:{0} | Session LOW broken | Reset lastSignaledHighAnchorBar to -1", CurrentBar));

                         // V_LOGIC: Hierarchy Check (Type A vs Type B) -> REMOVED
                         // session.IsInternalLow = ...

                         lowDetached = false; // SYNC: Reset Detachment

                         // V_LOGIC: Strategy Filters (Low Break = Short?)
                         
                         // 1. Trade Direction Filter
                         if (TradeDirection == TradeDirectionMode.LongOnly) return;

                         // 2. Re-entry Filter
                         // Removed per user request
                         
                         // 3. Alerts
                         if (EnableAlerts && !string.IsNullOrEmpty(AlertSound))
                         {
                             try { PlaySound(NinjaTrader.Core.Globals.InstallDir + @"\sounds\" + AlertSound); } catch {}
                         }

                         session.LowTradeCount++; // Increment Counter
                         
                         // Generate Code
                         string code = "";
                         if (LabelDisplayMode == LabelMode.Custom) code = CustomSignal1Text;
                         else if (LabelDisplayMode == LabelMode.Simple) code = "1";
                         else 
                         {
                             code = GetSignalCode(session, "L");
                             // if (session.IsInternalLow) code = "i" + code; // REMOVED
                         }

                         LastSignalCode = code;

                         // v1.0.8: Use configurable SignalColor instead of session colors
                         Brush sigBrush = SignalColor;

                         // V_VISUAL: SIGNAL 1 - TAKE LEVEL (SUPPORT) - v1.0.5: Synced with SessionLevels ATR-based positioning
                         double atrOffset = (atr != null && atr[0] > 0) ? atr[0] * LabelDistanceATR : TickSize * 10;

                         // v1.0.5: Position relative to candle Low + offset
                         double triY = low - atrOffset;

                         // Triangle (if ShowSignal1)
                         if (ShowSignal1)
                         {
                             // v1.0.24: Use session-based tag (not CurrentBar) so we can move the label
                             Draw.TriangleUp(this, "TakeLow_" + session.Name, true, 0, triY, sigBrush);

                             // Label (if ShowSignalLabels)
                             if (ShowSignalLabels)
                             {
                                 SimpleFont font = new SimpleFont("Arial", LabelFontSize);
                                 Draw.Text(this, "Sig1L_Txt_" + session.Name, true, code, 0, triY, -LabelTextOffset, sigBrush, font, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                             }

                             // v1.0.24: Track position for movable label
                             lowLiqGrabBarIdx = CurrentBar;
                             lowLiqGrabExtreme = low;
                             lowLiqGrabSessionName = session.Name;
                         }


                 // V_VISUAL: ADD TRADE LINE
                 // if (ShowTradeSetup && activeTrades != null) { ... } REMOVED

                     double entryPxLow = session.Low - TickSize;
                     double slPxLow = session.High + TickSize;
                     
                     TradeSetup newTradeLow = new TradeSetup {
                         ID = ++tradeIdCounter,
                         EntryBar = CurrentBar,
                         EntryPrice = entryPxLow,
                         EntryTime = Time[0],
                         IsLong = false, // Short
                         SL = slPxLow,
                         TP1 = 0,
                         TP2 = 0
                     };
                     activeTrades.Add(newTradeLow);
                     Print(string.Format("RelativeVwap: Visual Trade ADDED (Short) ID={0} at {1}", newTradeLow.ID, entryPxLow));
                 // } REMOVED ORPHAN BRACE

                    }
                }
            }
        }
        private void CloseGhostLines(List<SessionLevelInfo> sessions, int closeIdx)
        {
            if (sessions == null) return;
            foreach (var s in sessions)
            {
                // If broken but not yet closed, and break happened BEFORE the new session start
                if (s.HighBrokenBarIdx != -1 && s.HighGhostEndIdx == -1 && s.HighBrokenBarIdx <= closeIdx)
                    s.HighGhostEndIdx = closeIdx;
                    
                if (s.LowBrokenBarIdx != -1 && s.LowGhostEndIdx == -1 && s.LowBrokenBarIdx <= closeIdx)
                    s.LowGhostEndIdx = closeIdx;
            }
        }

        private void UpdateSession(List<SessionLevelInfo> sessions, string name, DateTime time, string startStr, string endStr, bool isEnabled)
        {
            if (!isEnabled || sessions == null) return;
            
            // CONVERT start/end strings (assumed Exchange Time) to Local/Chart time based on CurrentBarDate
            TimeSpan startTime = GetTimeByZone(startStr);
            TimeSpan endTime = GetTimeByZone(endStr);
            TimeSpan currentTime = time.TimeOfDay;

            bool isInside = false;
            
            // Logic: Start < End (Normal) | Start > End (Overnight)
            // Note: If times are equal (e.g. 16:00 to 16:00), it's never inside.
            // V_FIX: If Start == End, it's invalid/disabled, never inside.
            if (startTime == endTime)
                isInside = false;
            else if (startTime < endTime)
                isInside = currentTime >= startTime && currentTime < endTime;
            else // Crosses midnight (e.g. 18:00 to 03:00)
                isInside = currentTime >= startTime || currentTime < endTime;

            SessionLevelInfo currentSession = sessions.Count > 0 ? sessions.Last() : null;

            if (isInside)
            {
                // Determination of 'Session Date' logic for overnight sessions
                // If session is 18:00-03:00, and it is currently 19:00 on Monday, SessionDate is Monday.
                // If it is 01:00 on Tuesday (still 18-03 session), SessionDate is still Monday.
                // Logic: If NOW < END and START > END (overnight), we are in the 'second half', so SessionDate = Today - 1.
                DateTime sessionDate = time.Date;
                if (startTime > endTime && currentTime < endTime) sessionDate = time.Date.AddDays(-1);

                if (currentSession == null || !currentSession.IsActive || currentSession.SessionDate != sessionDate)
                {
                    // Start new session
                     currentSession = new SessionLevelInfo 
                     { 
                         Name = name,
                         IsActive = true,
                         StartBarIdx = CurrentBar,
                         High = High[0],
                         Low = Low[0],
                         SessionDate = sessionDate
                     };
                    sessions.Add(currentSession);
                        Print(string.Format("RelativeVwap: New Session Added -> {0} at Date {1} (StartBar:{2} H:{3} L:{4})", name, sessionDate, CurrentBar, High[0], Low[0]));
                }
                else
                {
                    // Update existing
                    if (High[0] > currentSession.High)
                    {
                        currentSession.High = High[0];
                    }
                    if (Low[0] < currentSession.Low)
                    {
                        currentSession.Low = Low[0];
                    }
                }
            }
            else
            {
                 // Outside session
                 if (currentSession != null && currentSession.IsActive)
                 {
                     // Close session
                     currentSession.IsActive = false;
                 }
            }
            
            // V_OPTI: Pruning REMOVED per user request (Historical levels needed)
            /* if (currentSession != null && currentSession.StartBarIdx == CurrentBar)
            {
                 PruneOldSessions(sessions);
            } */
        }
        
        #region Time Zone Helpers
        private DateTime CurrentBarDate; // Cache updated in OnBarUpdate
        private TimeZoneInfo _nyTimeZone; // Cache

        // V_OPTI: Cache Caching Variables
        private DateTime _lastCacheDate = DateTime.MinValue;
        private TimeSpan _cachedAsiaStart;
        private TimeSpan _cachedAsiaEnd;
        private TimeSpan _cachedEuropeStart;
        private TimeSpan _cachedEuropeEnd;
        private TimeSpan _cachedUSStart;
        private TimeSpan _cachedUSEnd;

        private TimeSpan GetTimeByZone(string timeStr)
        {
             // V_OPTI: Fast Cache Access
             if (UseExchangeTime && CurrentBarDate == _lastCacheDate)
             {
                 if (timeStr == AsiaStartTime) return _cachedAsiaStart;
                 if (timeStr == AsiaEndTime) return _cachedAsiaEnd;
                 if (timeStr == EuropeStartTime) return _cachedEuropeStart;
                 if (timeStr == EuropeEndTime) return _cachedEuropeEnd;
                 if (timeStr == USStartTime) return _cachedUSStart;
                 if (timeStr == USEndTime) return _cachedUSEnd;
             }
             
             // Fallback / First Run (should coverage by Refresh call)
             return CalculateTime(timeStr, CurrentBarDate);
        }
        
        private void RefreshTimezoneCache(DateTime date)
        {
             if (!UseExchangeTime) return;
             
             // Pre-calculate all session times for the new date
             _cachedAsiaStart = CalculateTime(AsiaStartTime, date);
             _cachedAsiaEnd = CalculateTime(AsiaEndTime, date);
             _cachedEuropeStart = CalculateTime(EuropeStartTime, date);
             _cachedEuropeEnd = CalculateTime(EuropeEndTime, date);
             _cachedUSStart = CalculateTime(USStartTime, date);
             _cachedUSEnd = CalculateTime(USEndTime, date);
             
             _lastCacheDate = date;
             // Print(string.Format("Debug: Timezone Cache Refreshed for {0}", date.ToShortDateString()));
        }

        private TimeSpan CalculateTime(string timeStr, DateTime date)
        {
             DateTime dt;
             if (!DateTime.TryParse(timeStr, out dt)) return TimeSpan.Zero;
             
             if (!UseExchangeTime) return dt.TimeOfDay;

             // --- EXCHANGE TIME CONVERSION LOGIC ---
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
        #endregion



        #region Smart Label Rendering
        private SharpDX.DirectWrite.Factory dwFactory;
        private SharpDX.DirectWrite.TextFormat textFormat;

        public override void OnRenderTargetChanged()
        {
            base.OnRenderTargetChanged();
            
            if (dwFactory != null) dwFactory.Dispose();
            if (textFormat != null) textFormat.Dispose();

            if (RenderTarget != null)
            {
                dwFactory = new SharpDX.DirectWrite.Factory();
                // Matching existing hardcoded size 12
                textFormat = new SharpDX.DirectWrite.TextFormat(dwFactory, "Arial", 12)
                {
                    TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading,
                    ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center
                };
            }
        }

        private float DrawLabel(string text, float x, float y, Brush color, ChartControl chartControl, DateTime timestamp, bool alignRight = false)
        {
            if (dwFactory == null || textFormat == null) return 0;

            // Measure Text
            float textWidth = 0;
            using (var layout = new SharpDX.DirectWrite.TextLayout(dwFactory, text, textFormat, 2000, 20))
            {
                textWidth = layout.Metrics.Width;
            }

            // Calculate 'True' Top-Left X position
            // V_VISUAL: Sticky Right Alignment
            // If alignRight is true, 'x' is the Right Screen Edge. We draw to the left of it.
            float drawX = alignRight ? (x - textWidth - 5) : (x + 5);

            // Queue EVERY label
            if (labelQueue != null)
            {
                labelQueue.Add(new LabelData {
                    Text = text,
                    DrawX = drawX,
                    Y = y,
                    Width = textWidth,
                    Brush = color,
                    Time = timestamp
                });
            }
            
            return textWidth;
        }

        private void RenderQueuedLabels(ChartControl chartControl)
        {
            if (labelQueue == null || labelQueue.Count == 0 || RenderTarget == null || dwFactory == null || textFormat == null) return;
            
            // De-duplicate
            var distinctQueue = labelQueue
                .GroupBy(l => l.Text)
                .Select(g => g.OrderByDescending(l => l.Time).First())
                .ToList();

            // Sort by Time DESC
            var sortedQueue = distinctQueue.OrderByDescending(l => l.Time).ToList();
            
            List<SharpDX.RectangleF> placedRects = new List<SharpDX.RectangleF>();
            
            foreach (var label in sortedQueue)
            {
                var solidColor = ((SolidColorBrush)label.Brush).Color;
                var dxColor = new SharpDX.Color((int)solidColor.R, (int)solidColor.G, (int)solidColor.B, 255);
                using (var brush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor))
                {
                    // Re-create layout for drawing
                    using (var layout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, label.Text, textFormat, 2000, 20))
                    {
                        float desiredX = label.DrawX;
                        float desiredY = label.Y - 10;
                        
                        // Candidate Box
                        SharpDX.RectangleF candidate = new SharpDX.RectangleF(desiredX, desiredY, label.Width, 20);
                        
                        // Resolve Collision (Shift Right - Horizontal Stacking)
                        int safety = 0;
                        while (safety < 100)
                        {
                            bool hit = false;
                            foreach (var rect in placedRects)
                            {
                                if (candidate.Intersects(rect))
                                {
                                    // Shift Right
                                    candidate.X = rect.Right + 5; 
                                    hit = true;
                                    break;
                                }
                            }
                            if (!hit) break;
                            safety++;
                        }
                        
                        // Draw Background (Updated per user request for visibility)
                        // Draw Background (Updated per user request for visibility)
                        // Conversion: Brush -> SharpDX Color
                        System.Windows.Media.Color bgColor = ((SolidColorBrush)LabelBackgroundColor).Color;
                        SharpDX.Color dxBgColor = new SharpDX.Color((byte)bgColor.R, (byte)bgColor.G, (byte)bgColor.B, (byte)255); // Fix Ambiguity: Cast to byte
                        
                        using (var backBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxBgColor))
                        {
                            RenderTarget.FillRectangle(candidate, backBrush);
                        }
                        
                        RenderTarget.DrawTextLayout(new SharpDX.Vector2(candidate.X, candidate.Y), layout, brush);
                        placedRects.Add(candidate);
                    }
                }
            }
        }


        
        private void RenderSignalLabels(ChartControl chartControl, ChartScale chartScale)
        {
            if (signalLabels == null || signalLabels.Count == 0 || RenderTarget == null || dwFactory == null || textFormat == null) return;
             if (Bars == null || ChartBars == null) return;

             // Map to track occupied space per bar to stack vertically
             Dictionary<int, List<SharpDX.RectangleF>> occupiedSpace = new Dictionary<int, List<SharpDX.RectangleF>>();

             // 1. Group signals by Bar Index to allow sorting
             var signalsByBar = signalLabels.Values
                 .Where(s => s.BarIdx >= ChartBars.FromIndex && s.BarIdx <= ChartBars.ToIndex)
                 .GroupBy(s => s.BarIdx);

             foreach (var group in signalsByBar)
             {
                 int idx = group.Key;
                 float barX = chartControl.GetXByBarIndex(ChartBars, idx);
                 
                 // Split into Highs and Lows
                 var highSignals = group.Where(s => s.IsHigh).ToList();
                 var lowSignals = group.Where(s => !s.IsHigh).ToList();

                 // Calc initial Y for sorting
                 // Note: This duplicates calc logic but is necessary for sort. 
                 // We'll just sort by Price roughly? No, use re-calc.
                 // Actually, sorting by Price is easier.
                 // Highs: Stack UP. We want start closest to candle (Lowest Price? No, Candle High is usually lower than VWAP High? No.)
                 // Logic:
                 // Highs: Y decreases as Price increases.
                 // We want to process LARGEST Y (Smallest Price) first ??
                 // Usually Signal is at High[0]. VWAP is at hVwap.
                 // If Price is higher, Y is smaller (higher up).
                 // We want to process the one "lower down" (closest to candle body) first.
                 // So we process LARGEST Y first. => SMALLEST PRICE first.
                 // Lows: Y increases as Price decreases.
                 // We want to process SMALLEST Y (Highest Price) first. => HIGHEST PRICE first.
                 
                 // Sort 
                 highSignals.Sort((a, b) => a.Price.CompareTo(b.Price)); // Ascending Price = Descending Y (Correct for Highs?)
                 // Wait. Ascending Price: 100, 101, 102.
                 // Y: 500, 490, 480.
                 // We process 100 (500) first. This is closest to candle. Correct.
                 
                 lowSignals.Sort((a, b) => b.Price.CompareTo(a.Price)); // Descending Price = Ascending Y (Correct for Lows?)
                 // Descending Price: 90, 89, 88.
                 // Y: 600, 610, 620.
                 // We process 90 (600) first. Closest to candle. Correct.

                 // Helper to process list
                 Action<List<SignalObj>> processList = (list) => 
                 {
                     foreach (var sig in list)
                     {
                         float y = (float)chartScale.GetYByValue(sig.Price);
                         // Use price directly as it now contains the visual offset (ATR-based)
                         float drawY = y;
                         
                         using (var layout = new SharpDX.DirectWrite.TextLayout(dwFactory, sig.Text, textFormat, 300f, 50f))
                         {
                             float w = layout.Metrics.Width;
                             float h = layout.Metrics.Height;
                             float drawX = barX - (w / 2);
                             
                             if (sig.IsHigh) drawY -= h; 
                             
                             SharpDX.RectangleF currentRect = new SharpDX.RectangleF(drawX, drawY, w, h);
                             
                             // Collision
                             if (!occupiedSpace.ContainsKey(idx)) occupiedSpace[idx] = new List<SharpDX.RectangleF>();
                             List<SharpDX.RectangleF> barRects = occupiedSpace[idx];
                             
                             int safety = 0;
                             while (safety < 20)
                             {
                                 bool collision = false;
                                 foreach (var obst in barRects)
                                 {
                                     // Add small internal padding to rect for intersection test
                                     // Or just check intersection
                                     if (currentRect.Intersects(obst))
                                     {
                                         collision = true;
                                         float padding = 4f; // Increased Padding
                                         
                                         if (sig.IsHigh) currentRect.Y = obst.Top - h - padding; 
                                         else currentRect.Y = obst.Bottom + padding;
                                         
                                         break;
                                     }
                                 }
                                 if (!collision) break;
                                 safety++;
                             }
                             
                             barRects.Add(currentRect);
                             
                             // Draw Background (Semi-transparent black/gray)
                             // Use LabelBackgroundColor property
                             var mediaCol = ((SolidColorBrush)LabelBackgroundColor).Color;
                             var dxBgColor = new SharpDX.Color((int)mediaCol.R, (int)mediaCol.G, (int)mediaCol.B, 180); // Explicit Cast to int
                             using (var bgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxBgColor))
                             {
                                 // Expand bg slightly
                                 RenderTarget.FillRectangle(new SharpDX.RectangleF(currentRect.X - 2, currentRect.Y - 1, currentRect.Width + 4, currentRect.Height + 2), bgBrush);
                             }

                             // Draw Text
                             var sc = ((SolidColorBrush)sig.Brush).Color;
                             var dxColor = new SharpDX.Color((int)sc.R, (int)sc.G, (int)sc.B, 255); 
                             using (var brush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor))
                             {
                                 RenderTarget.DrawTextLayout(new SharpDX.Vector2(currentRect.X, currentRect.Y), layout, brush);
                             }
                         }
                     }
                 };

                 processList(highSignals);
                 processList(lowSignals);
             }
        }

        #endregion

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
             // v1.0.24: NO base.OnRender() = no duplicate VWAP lines. BarBrushes works via ChartBars.
             // base.OnRender(chartControl, chartScale);
             if (Bars == null || chartControl == null || chartScale == null) return;
             
             // V_COLLISION: Reset Frame
             // if (occupiedYRanges != null) occupiedYRanges.Clear(); // Removed undefined reference
             
             // Render Active Trades (Direct2D)
             try { RenderTradeVisuals(chartControl, chartScale); } catch {}
             
             // Clear Queue
             if (labelQueue != null) labelQueue.Clear();
             
             // Render Session Levels first (behind VWAP basically)
             // Debug Print Once
             // Debug Print Throttled (approx every 2 seconds)
             if (CurrentBar == Bars.Count - 1)
             {
                 long nowTicks = DateTime.Now.Ticks;
                 if (nowTicks % 20000000 < 200000) // 20ms window every 2s
                 {
                     Print(string.Format("RelativeVwap Render: AsiaCount={0} EurCount={1} USCount={2} ShowAsia={3} Trades={4}", 
                         asiaSessions != null ? asiaSessions.Count : 0,
                         europeSessions != null ? europeSessions.Count : 0,
                         usSessions != null ? usSessions.Count : 0,
                         ShowAsia,
                         activeTrades != null ? activeTrades.Count : 0));
                 }
             }

             // Render Session Levels first
             if (ShowAsia && asiaSessions != null) 
                 foreach(var s in asiaSessions) RenderSessionLevels(s, AsiaLineColor, AsiaLabelColor, ShowAsiaHigh, ShowAsiaLow, chartControl, chartScale, GetTimeByZone(AsiaStartTime) > GetTimeByZone(AsiaEndTime));

             if (ShowEurope && europeSessions != null) 
                 foreach(var s in europeSessions) RenderSessionLevels(s, EuropeLineColor, EuropeLabelColor, ShowEuropeHigh, ShowEuropeLow, chartControl, chartScale, GetTimeByZone(EuropeStartTime) > GetTimeByZone(EuropeEndTime));

             if (ShowUS && usSessions != null) 
                 foreach(var s in usSessions) RenderSessionLevels(s, USLineColor, USLabelColor, ShowUSHigh, ShowUSLow, chartControl, chartScale, GetTimeByZone(USStartTime) > GetTimeByZone(USEndTime));

              // 1. Calculate and Draw Anchored VWAPs (High/Low)
              if (hasHighVWAP)
              {
                  DrawAnchoredLine(sessionHighBarIdx, HighVWAPColor, HighVwapLabel, chartControl, chartScale);
              }
              if (hasLowVWAP)
              {
                  DrawAnchoredLine(sessionLowBarIdx, LowVWAPColor, LowVwapLabel, chartControl, chartScale);
              }

              // V_HIST: Draw Historical VWAP Segments (Gray, 1px, No Label)
              foreach (var anchor in historicalHighs)
              {
                  DrawAnchoredLine(anchor.StartIdx, HistoricalVWAPColor, "", chartControl, chartScale, anchor.EndIdx, -1, HistoricalVWAPThickness, false);
              }
              foreach (var anchor in historicalLows)
              {
                  DrawAnchoredLine(anchor.StartIdx, HistoricalVWAPColor, "", chartControl, chartScale, anchor.EndIdx, -1, HistoricalVWAPThickness, false);
              }
             
             // Draw Trades (Entry, SL, TP)
              // Render Trades (Entry, SL, TP) - Direct2D Implementation
              // if (ShowTradeSetup && activeTrades != null) REMOVED
 
              {
 // RenderTradeVisuals(chartControl, chartScale); // V_CLEANUP: Disabled Direct2D rendering
              }
              
              // Render Signal Labels (Stacked)
              RenderSignalLabels(chartControl, chartScale);
              
              // FLUSH LABELS
             RenderQueuedLabels(chartControl);
             
             // Draw Countdown (Standalone Mode)
             if (ShowLabels && ShowCountdown && !string.IsNullOrEmpty(_currentCountdownText))
             {
                 // Calculate Position (Default: CurrentBar + Offset)
                 int idx = Bars.Count - 1;
                 float x = chartControl.GetXByBarIndex(ChartBars, idx) + CountdownOffsetX;
                 double price = High.GetValueAt(idx) + (CountdownOffsetY * TickSize);
                 float y = (float)chartScale.GetYByValue(price);
                 
                  using (var textFormat = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, (float)CountdownFontSize))
                 {
                      // Manual Color Conversion
                      System.Windows.Media.Color sysColor = ((SolidColorBrush)CountdownTextColor).Color;
                      SharpDX.Color dxColor = new SharpDX.Color(sysColor.R, sysColor.G, sysColor.B, sysColor.A);
                      
                      using (var brush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor))
                      {
                          RenderTarget.DrawText(_currentCountdownText, textFormat, new SharpDX.RectangleF(x, y, 200, 50), brush);
                      }
                 }
             }
         }

         private void RenderTradeVisuals(ChartControl chartControl, ChartScale chartScale)
         {
             return; // Disabled
             /*
             if (RenderTarget == null || activeTrades == null) return;

             try
             {
                 using (var textFormat = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 11f))
                 {
                     foreach (var trade in activeTrades)
                     {
                         // ... (Logic Disabled)
                     }
                 }
             }
             catch (Exception ex)
             {
                 Print("RelativeVwap RENDER ERROR: " + ex.ToString());
             }
             */
         }

         private void DrawDirectLine(double price, float x1, float x2, ChartScale chartScale, Brush brush, string label, SharpDX.DirectWrite.TextFormat fmt)
         {
             float y = (float)chartScale.GetYByValue(price);
             
             // Manual Color Conversion (System.Windows.Media.Color -> SharpDX.Color)
             System.Windows.Media.Color mColor = ((SolidColorBrush)brush).Color;
             SharpDX.Color dxColor = new SharpDX.Color(mColor.R, mColor.G, mColor.B, mColor.A);
             
             var dxBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor);
             
             // Draw Line (User Request: 1px width)
             RenderTarget.DrawLine(new SharpDX.Vector2(x1, y), new SharpDX.Vector2(x2, y), dxBrush, 1.0f);
             
             // Draw Label
             // Background Rect
             var layout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, label, fmt, 100f, 20f);
             float textW = layout.Metrics.Width;
             float textH = layout.Metrics.Height;
             
             // Draw Background
                // Conversion: Brush -> SharpDX Color
                System.Windows.Media.Color bgColor = ((SolidColorBrush)LabelBackgroundColor).Color;
                SharpDX.Color dxBgColor = new SharpDX.Color((byte)bgColor.R, (byte)bgColor.G, (byte)bgColor.B, (byte)128); // Fix Ambiguity: Cast to byte
                
                using (var bgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxBgColor)) // Use converted color
                {
                    bgBrush.Opacity = 0.5f;
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(x2, y - textH/2, textW + 4, textH), bgBrush);
                }
             
             // Draw Text
             RenderTarget.DrawText(label, fmt, new SharpDX.RectangleF(x2 + 2, y - textH/2, textW, textH), dxBrush);
             
             dxBrush.Dispose();
             layout.Dispose();
         }




        // Removed HasAnyLevelBeenTaken as we use boolean flag now

        private void DrawAnchoredLine(int startIdx, Brush color, string label, ChartControl chartControl, ChartScale chartScale, int limitIdx = -1, int visualStartIdx = -1, float thickness = 2.0f, bool showLabel = true)
        {
            if (Bars == null) return;

            // Render Target check
            if (RenderTarget == null) return;

            int endIdx = (limitIdx == -1) ? Bars.Count - 1 : limitIdx; 
            int safeStart = Math.Max(0, startIdx);
            int safeEnd = Math.Min(Bars.Count - 1, endIdx);
            
            // Visual Limit: Do not draw before this index
            int safeVisualStart = Math.Max(safeStart, (visualStartIdx == -1) ? safeStart : visualStartIdx);

            if (safeStart > safeEnd) return;
            
            // Optimization: if completely out of view
            if (safeEnd < ChartBars.FromIndex || safeStart > ChartBars.ToIndex) return;

            double cumPV = 0;
            double cumVol = 0;

            SharpDX.Vector2? lastPoint = null;
            SharpDX.Vector2? lastLabelPoint = null;

            var solidColor = ((SolidColorBrush)color).Color;
            var colorWithAlpha = new SharpDX.Color((int)solidColor.R, (int)solidColor.G, (int)solidColor.B, 255);

            using (var lineBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, colorWithAlpha))
            {
                // To draw the line correctly, we must calculate from the anchor start
                // We can't skip calculation of previous bars even if they are not visible
                // But we can skip DRAWING them.
                
                for (int i = safeStart; i <= safeEnd; i++)
                {
                    // v1.0.24: Use same price method as OnBarUpdate (VwapMethod)
                    double price;
                    if (VwapMethod == VwapPriceMethod.Close)
                        price = Close.GetValueAt(i);
                    else if (VwapMethod == VwapPriceMethod.Typical)
                        price = (High.GetValueAt(i) + Low.GetValueAt(i) + Close.GetValueAt(i)) / 3.0;
                    else // OHLC4
                        price = (Open.GetValueAt(i) + High.GetValueAt(i) + Low.GetValueAt(i) + Close.GetValueAt(i)) / 4.0;

                    double vol = Volume.GetValueAt(i);

                    cumPV += price * vol;
                    cumVol += vol;

                    // If volume is zero, VWAP is undefined or stays same?
                    if (cumVol == 0) continue;

                    double vwap = cumPV / cumVol;

                    // Rendering coordinate
                    float x = chartControl.GetXByBarIndex(ChartBars, i);
                    float y = (float)chartScale.GetYByValue(vwap);
                    
                    SharpDX.Vector2 currentPoint = new SharpDX.Vector2(x, y);

                     // Draw if visible
                     if (lastPoint.HasValue)
                     {
                          // Only Draw if we are past the Visual Start Index
                          if (i >= safeVisualStart && i >= ChartBars.FromIndex - 1 && i <= ChartBars.ToIndex + 1)
                          {
                               RenderTarget.DrawLine(lastPoint.Value, currentPoint, lineBrush, thickness);
                          }
                     }

                    lastPoint = currentPoint;
                    lastLabelPoint = currentPoint;
                }
            }
            
             // Draw Label
             if (showLabel && ShowLabels && !string.IsNullOrEmpty(label) && lastLabelPoint.HasValue && safeEnd >= ChartBars.FromIndex && safeEnd <= ChartBars.ToIndex)
             {
                 DateTime time = (safeEnd < Bars.Count) ? Bars.GetTime(safeEnd) : DateTime.Now;
                 DrawLabel(label, lastLabelPoint.Value.X, lastLabelPoint.Value.Y, color, chartControl, time, false);
             }
        }

        private void RenderSessionLevels(SessionLevelInfo session, Brush lineColor, Brush labelColor, bool showHigh, bool showLow, ChartControl chartControl, ChartScale chartScale, bool isOvernight)
        {
            if (session.StartBarIdx < 0 || session.High == 0) return;

             if (session.StartBarIdx > ChartBars.ToIndex) return;

             int startIdx = Math.Max(0, session.StartBarIdx);
             int endIdx = Bars.Count - 1; 
             
             // Calculate Limit Logic (matches RelativeLevels)
             int limitIdx;
             if (ExtendLinesUntilTouch)
             {
                 limitIdx = Bars.Count - 1;
             }
             else
             {
                 DateTime cutOff = session.SessionDate.AddDays(1).AddHours(16); // Rough approx
                 limitIdx = Bars.GetBar(cutOff);
                 if (limitIdx < 0) limitIdx = Bars.Count - 1;
             }
             
             if (limitIdx < startIdx) limitIdx = startIdx;

             // --- Prepare Suffix ---
            string suffixText = "";
            bool isGraySuffix = false;
            
            int days = 0;
            if (ShowDaysAgo)
            {
                // Use ChartBars.ToIndex to get the 'Right Edge' date of the visible chart
                int refIdx = (ChartBars != null) ? ChartBars.ToIndex : (Bars.Count - 1);
                if (refIdx >= Bars.Count) refIdx = Bars.Count - 1;
                if (refIdx < 0) refIdx = 0;
                
                DateTime refDate = (Bars != null && refIdx < Bars.Count) ? Bars.GetTime(refIdx).Date : DateTime.MinValue;

                // Basic Diff
                TimeSpan diff = (refDate != DateTime.MinValue) 
                    ? (refDate - session.SessionDate.Date)
                    : TimeSpan.Zero;
                    
                days = (int)diff.TotalDays; 
                if (days > 0) 
                {
                    // Debug Removed
                }

                if (days == 1 && !session.IsActive)
                {
                     // If it is overnight and we are 1 day out, it means it ended TODAY. Hide it.
                     if (isOvernight)
                     {
                         days = 0;
                     }
                }

                if (days > 0 && !session.IsActive) 
                {
                    suffixText = "  " + days + " days";
                    isGraySuffix = true; 
                }
            }

             Action<string, double, int, int> drawLevel = (suffix, price, breakIdx, ghostEndIdx) => {
                 int currentLimit = limitIdx;
                 int seg1End = currentLimit;
                 // V_FIX: Removed !session.IsActive check to allow immediate ghost lines
                 bool isBroken = (ExtendLinesUntilTouch && breakIdx != -1 && breakIdx < currentLimit);
                 // DEBUG: Trace why not broken
                 if (ShowDebugLabels && !isBroken && breakIdx != -1 && !session.IsActive)
                 {
                      // Print(string.Format("DebugRender: NotBroken but has BreakIdx? Name={0} Break={1} Limit={2} Extend={3}", session.Name, breakIdx, currentLimit, ExtendLinesUntilTouch));
                 }
                 

                 
                 if (isBroken) seg1End = breakIdx;
                 if (seg1End > Bars.Count-1) seg1End = Bars.Count-1;

                 float x1 = chartControl.GetXByBarIndex(ChartBars, startIdx);
                 float xEnd1 = chartControl.GetXByBarIndex(ChartBars, seg1End);
                 float y = (float)chartScale.GetYByValue(price);
                 
                 using(var dxBrush = lineColor.ToDxBrush(RenderTarget))
                 {
                     RenderTarget.DrawLine(new SharpDX.Vector2(x1, y), new SharpDX.Vector2(xEnd1, y), dxBrush, 2);
                 }
                 
                 float finalLabelX = xEnd1;
                 Brush finalLabelBrush = labelColor;
                 bool alignRight = false;

                  // Ghost Segment
                 if (isBroken)
                 {
                     int activeGhostEnd = (ghostEndIdx == -1) ? Bars.Count - 1 : ghostEndIdx;
                     
                     if (activeGhostEnd > Bars.Count - 1) activeGhostEnd = Bars.Count - 1;
                     if (activeGhostEnd < breakIdx) activeGhostEnd = breakIdx;

                     float xEnd2 = chartControl.GetXByBarIndex(ChartBars, activeGhostEnd);
                     
                     using (var ghostBrush = Brushes.Gray.ToDxBrush(RenderTarget))
                     using (var dashStyle = new SharpDX.Direct2D1.StrokeStyle(Core.Globals.D2DFactory, new SharpDX.Direct2D1.StrokeStyleProperties { DashStyle = SharpDX.Direct2D1.DashStyle.Dash }))
                     {
                          RenderTarget.DrawLine(new SharpDX.Vector2(xEnd1, y), new SharpDX.Vector2(xEnd2, y), ghostBrush, 1, dashStyle);
                     }
                     finalLabelX = xEnd2;
                     finalLabelBrush = Brushes.Gray;
                 }
                 else if (seg1End >= Bars.Count - 1)
                 {
                     // Do not force to right edge. Stick to line end.
                 }
                 
                  
                  if (ShowLabels)
                  {
                       string mainLabel = session.Name + " " + suffix; if (!string.IsNullOrEmpty(suffixText)) mainLabel += suffixText;
                       
                       float currentX = finalLabelX;
                       
                       // V_VISUAL: Sticky Right Label Logic
                       // If the line end (xEnd1 or xEnd2) is off-screen to the RIGHT, 
                       // but the line itself is visible (starts before screen right), clamp text to right edge.
                       
                       float screenRight = ChartPanel.X + ChartPanel.W;
                       bool isClamped = false;
                       
                       // Check if line end extends beyond visual area
                       if (finalLabelX > screenRight)
                       {
                           // Check if line start is visible or to the left (meaning line crosses view)
                           if (x1 < screenRight)
                           {
                       currentX = screenRight - 5; // Clamp to right edge with padding
                               isClamped = true;
                           }
                           else
                           {
                               // Line is completely to the right (future?) -> Don't draw label
                               return; 
                           }
                       }
                       
                       // V_DEBUG: Log overlapping coords
                       if (CurrentBar == Bars.Count - 1 && (DateTime.Now.Ticks % 50000000 < 200000)) // Throttle: Once every ~5s
                       {
                            Print(string.Format("LABEL DEBUG: {0} | Px: {1} | Y: {2:F2} | Days: {3} | Suffix: '{4}'", 
                                mainLabel, price, y, days, suffixText));
                       }

                       // Draw Main Label
                       // If clamped, align RIGHT so it sticks to edge properly
                       float w1 = DrawLabel(mainLabel, currentX, y, finalLabelBrush, chartControl, session.SessionDate, isClamped);
                  }
             };

             if (showHigh) drawLevel("High", session.High, session.HighBrokenBarIdx, session.HighGhostEndIdx);
             if (showLow) drawLevel("Low", session.Low, session.LowBrokenBarIdx, session.LowGhostEndIdx);
        }

        #region Properties
        
        // ========================================================================
        // 01. Configuración Principal
        // ========================================================================
        [NinjaScriptProperty]
        [Display(Name = "Método VWAP", Description = "Precio usado para el cálculo del VWAP: Cierre (default), Típico (H+L+C)/3, u OHLC4", GroupName = "01. Configuración Principal", Order = 1)]
        public VwapPriceMethod VwapMethod { get; set; } = VwapPriceMethod.Close;

        [NinjaScriptProperty]
        [Range(1, 365)]
        [Display(Name = "Días de Historia Máx", Description = "Ignorar niveles más antiguos de X días para optimizar rendimiento", GroupName = "01. Configuración Principal", Order = 2)]
        public int MaxHistoryDays { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Usar Hora Exchange", Description = "Si es true, usa los horarios del Exchange. Si es false, usa hora local.", GroupName = "01. Configuración Principal", Order = 3)]
        public bool UseExchangeTime { get; set; } = true;

        // ========================================================================
        // 02. Sesiones de Tiempo
        // ========================================================================
        
        // ASIA
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Asia", GroupName = "02. Sesiones de Tiempo", Order = 1)]
        public bool ShowAsia { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Asia Inicio", GroupName = "02. Sesiones de Tiempo", Order = 2)]
        public string AsiaStartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Asia Fin", GroupName = "02. Sesiones de Tiempo", Order = 3)]
        public string AsiaEndTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar High Asia", GroupName = "02. Sesiones de Tiempo", Order = 4)]
        public bool ShowAsiaHigh { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Low Asia", GroupName = "02. Sesiones de Tiempo", Order = 5)]
        public bool ShowAsiaLow { get; set; }

        [XmlIgnore]
        [Display(Name = "Color Línea Asia", GroupName = "02. Sesiones de Tiempo", Order = 6)]
        public Brush AsiaLineColor { get; set; }
        [Browsable(false)] public string AsiaLineColorSerializable { get { return Serialize.BrushToString(AsiaLineColor); } set { AsiaLineColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Color Etiqueta Asia", GroupName = "02. Sesiones de Tiempo", Order = 7)]
        public Brush AsiaLabelColor { get; set; }
        [Browsable(false)] public string AsiaLabelColorSerializable { get { return Serialize.BrushToString(AsiaLabelColor); } set { AsiaLabelColor = Serialize.StringToBrush(value); } }


        // EUROPE
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Europa", GroupName = "02. Sesiones de Tiempo", Order = 10)]
        public bool ShowEurope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Europa Inicio", GroupName = "02. Sesiones de Tiempo", Order = 11)]
        public string EuropeStartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Europa Fin", GroupName = "02. Sesiones de Tiempo", Order = 12)]
        public string EuropeEndTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar High Europa", GroupName = "02. Sesiones de Tiempo", Order = 13)]
        public bool ShowEuropeHigh { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Low Europa", GroupName = "02. Sesiones de Tiempo", Order = 14)]
        public bool ShowEuropeLow { get; set; }

        [XmlIgnore]
        [Display(Name = "Color Línea Europa", GroupName = "02. Sesiones de Tiempo", Order = 15)]
        public Brush EuropeLineColor { get; set; }
        [Browsable(false)] public string EuropeLineColorSerializable { get { return Serialize.BrushToString(EuropeLineColor); } set { EuropeLineColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Color Etiqueta Europa", GroupName = "02. Sesiones de Tiempo", Order = 16)]
        public Brush EuropeLabelColor { get; set; }
        [Browsable(false)] public string EuropeLabelColorSerializable { get { return Serialize.BrushToString(EuropeLabelColor); } set { EuropeLabelColor = Serialize.StringToBrush(value); } }

        // USA
        [NinjaScriptProperty]
        [Display(Name = "Mostrar USA", GroupName = "02. Sesiones de Tiempo", Order = 20)]
        public bool ShowUS { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "USA Inicio", GroupName = "02. Sesiones de Tiempo", Order = 21)]
        public string USStartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "USA Fin", GroupName = "02. Sesiones de Tiempo", Order = 22)]
        public string USEndTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar High USA", GroupName = "02. Sesiones de Tiempo", Order = 23)]
        public bool ShowUSHigh { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Low USA", GroupName = "02. Sesiones de Tiempo", Order = 24)]
        public bool ShowUSLow { get; set; }

        [XmlIgnore]
        [Display(Name = "Color Línea USA", GroupName = "02. Sesiones de Tiempo", Order = 25)]
        public Brush USLineColor { get; set; }
        [Browsable(false)] public string USLineColorSerializable { get { return Serialize.BrushToString(USLineColor); } set { USLineColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Color Etiqueta USA", GroupName = "02. Sesiones de Tiempo", Order = 26)]
        public Brush USLabelColor { get; set; }
        [Browsable(false)] public string USLabelColorSerializable { get { return Serialize.BrushToString(USLabelColor); } set { USLabelColor = Serialize.StringToBrush(value); } }


        // ========================================================================
        // 03. Visuales VWAP
        // ========================================================================
        [XmlIgnore]
        [Display(Name = "Color VWAP High", GroupName = "03. Visuales VWAP", Order = 1)]
        public Brush HighVWAPColor { get; set; }
        [Browsable(false)] public string HighVWAPColorSerializable { get { return Serialize.BrushToString(HighVWAPColor); } set { HighVWAPColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Color VWAP Low", GroupName = "03. Visuales VWAP", Order = 2)]
        public Brush LowVWAPColor { get; set; }
        [Browsable(false)] public string LowVWAPColorSerializable { get { return Serialize.BrushToString(LowVWAPColor); } set { LowVWAPColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Color VWAP Histórico", GroupName = "03. Visuales VWAP", Order = 3)]
        public Brush HistoricalVWAPColor { get; set; }
        [Browsable(false)] public string HistoricalVWAPColorSerializable { get { return Serialize.BrushToString(HistoricalVWAPColor); } set { HistoricalVWAPColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Range(1.0f, 10.0f)]
        [Display(Name = "Grosor VWAP Histórico", GroupName = "03. Visuales VWAP", Order = 4)]
        public float HistoricalVWAPThickness { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Extender Líneas Infinitas", Description = "Extender líneas hasta que sean tocadas", GroupName = "03. Visuales VWAP", Order = 5)]
        public bool ExtendLinesUntilTouch { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Etiqueta VWAP High", Description = "Texto para la línea VWAP superior (ej. Supply)", GroupName = "03. Visuales VWAP", Order = 6)]
        public string HighVwapLabel { get; set; } = "Supply";

        [NinjaScriptProperty]
        [Display(Name = "Etiqueta VWAP Low", Description = "Texto para la línea VWAP inferior (ej. Demand)", GroupName = "03. Visuales VWAP", Order = 7)]
        public string LowVwapLabel { get; set; } = "Demand";

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Días Atrás", Description = "Muestra 'X days' en lugar de fecha", GroupName = "03. Visuales VWAP", Order = 6)]
        public bool ShowDaysAgo { get; set; }

        // ========================================================================
        // 04. Señales y Textos
        // ========================================================================
        [NinjaScriptProperty]
        [Display(Name = "Dirección de Trades", GroupName = "04. Señales y Textos", Order = 1)]
        public TradeDirectionMode TradeDirection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Etiquetas", GroupName = "04. Señales y Textos", Order = 2)]
        public bool ShowLabels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Modo Etiquetas", Description = "Selecciona el modo de visualización de etiquetas", GroupName = "04. Señales y Textos", Order = 3)]
        public LabelMode LabelDisplayMode { get; set; } = LabelMode.Default;

        [NinjaScriptProperty]
        [Display(Name = "Texto Señal 1", Description = "Texto para señal de ruptura (ej. 'Liquidity Grabbed')", GroupName = "04. Señales y Textos", Order = 31)]
        public string CustomSignal1Text { get; set; } = "Liquidity Grabbed";

        [NinjaScriptProperty]
        [Display(Name = "Texto Señal 2", Description = "Texto para señal confirmada (ej. 'Entry 1')", GroupName = "04. Señales y Textos", Order = 32)]
        public string CustomSignal2Text { get; set; } = "Entry 1";

        [NinjaScriptProperty]
        [Display(Name = "Texto Señal 3", Description = "Texto para señal de re-test (ej. 'Entry 2')", GroupName = "04. Señales y Textos", Order = 33)]
        public string CustomSignal3Text { get; set; } = "Entry 2";

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Etiquetas Señal", Description = "Muestra texto en señales (AH.1, etc)", GroupName = "04. Señales y Textos", Order = 4)]
        public bool ShowSignalLabels { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Señal 1 (Ruptura)", Description = "Muestra la señal de toma de liquidez", GroupName = "04. Señales y Textos", Order = 40)]
        public bool ShowSignal1 { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Señal 2 (Confir.)", Description = "Muestra la señal de entrada 1", GroupName = "04. Señales y Textos", Order = 41)]
        public bool ShowSignal2 { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Señal 3 (Re-test)", Description = "Muestra la señal de entrada 2", GroupName = "04. Señales y Textos", Order = 42)]
        public bool ShowSignal3 { get; set; } = true;

        [XmlIgnore]
        [Display(Name = "Color Señales", Description = "Color para flechas y textos de señal", GroupName = "04. Señales y Textos", Order = 6)]
        public Brush SignalColor { get; set; } = Brushes.White;
        [Browsable(false)] public string SignalColorSerializable { get { return Serialize.BrushToString(SignalColor); } set { SignalColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Color Fondo Etiquetas", GroupName = "04. Señales y Textos", Order = 7)]
        public Brush LabelBackgroundColor { get; set; } = Brushes.Black;
        [Browsable(false)] public string LabelBackgroundColorSerializable { get { return Serialize.BrushToString(LabelBackgroundColor); } set { LabelBackgroundColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Range(6, 24)]
        [Display(Name = "Tamaño Fuente Señal", GroupName = "04. Señales y Textos", Order = 8)]
        public int LabelFontSize { get; set; } = 12;

        [NinjaScriptProperty]
        [Range(-50, 50)]
        [Display(Name = "Offset Texto (px)", GroupName = "04. Señales y Textos", Order = 9)]
        public int LabelTextOffset { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Distancia Etiqueta ATR", Description = "Multiplicador ATR para distancia desde precio", GroupName = "04. Señales y Textos", Order = 10)]
        public double LabelDistanceATR { get; set; } = 0.3;

        [NinjaScriptProperty]
        [Range(0.5, 5.0)]
        [Display(Name = "Espaciado Colisión", GroupName = "04. Señales y Textos", Order = 11)]
        public double LabelCollisionSpacing { get; set; } = 1.5;

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Ticks de Separación", Description = "Ticks mínimos requeridos entre High/Low y VWAP para considerar 'Detached'", GroupName = "04. Señales y Textos", Order = 12)]
        public int DetachmentTicks { get; set; } = 2;

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Umbral Señal 2", Description = "Ticks requeridos para cierre dentro del VWAP", GroupName = "04. Señales y Textos", Order = 13)]
        public int Signal2ThresholdTicks { get; set; } = 1;


        // ========================================================================
        // 05. Alertas & Debug
        // ========================================================================
        [NinjaScriptProperty]
        [Display(Name = "Habilitar Alertas", GroupName = "05. Alertas & Debug", Order = 1)]
        public bool EnableAlerts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Sonido Alerta", GroupName = "05. Alertas & Debug", Order = 2)]
        public string AlertSound { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Labels Debug", GroupName = "05. Alertas & Debug", Order = 3)]
        public bool ShowDebugLabels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Logging a Archivo", Description = "Escribe logs detallados a trace/RelativeVwap_Debug_YYYYMMDD.txt", GroupName = "05. Alertas & Debug", Order = 4)]
        public bool EnableFileLogging { get; set; }


        // ========================================================================
        // 06. Contador (Countdown)
        // ========================================================================
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Contador", GroupName = "06. Contador", Order = 1)]
        public bool ShowCountdown { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Modo Cuenta Regresiva", GroupName = "06. Contador", Order = 2)]
        public bool CountDown { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Porcentaje", GroupName = "06. Contador", Order = 3)]
        public bool ShowPercent { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Tamaño Fuente", GroupName = "06. Contador", Order = 4)]
        public int CountdownFontSize { get; set; } = 12;

        [XmlIgnore]
        [Display(Name = "Color Texto", GroupName = "06. Contador", Order = 5)]
        public Brush CountdownTextColor { get; set; } = Brushes.White;
        [Browsable(false)] public string CountdownTextColorSerializable { get { return Serialize.BrushToString(CountdownTextColor); } set { CountdownTextColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Display(Name = "Offset X (px)", GroupName = "06. Contador", Order = 6)]
        public int CountdownOffsetX { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name = "Offset Y (ticks)", GroupName = "06. Contador", Order = 7)]
        public int CountdownOffsetY { get; set; } = 10;

        #endregion
        
        // Countdown Helpers
        private void OnTimerTick(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (ChartControl != null && Bars != null)
            {
                ChartControl.Dispatcher.InvokeAsync(() => 
                {
                    if (CurrentBar == Bars.Count - 1) CalculateCountdown();
                });
            }
        }

        private void CalculateCountdown()
        {
            try
            {
                if (Bars == null || Bars.Count == 0 || Instrument == null) return;
                int idx = Bars.Count - 1;
                
                volume = Instrument.MasterInstrument.InstrumentType == InstrumentType.CryptoCurrency
                    ? Core.Globals.ToCryptocurrencyVolume((long)Bars.GetVolume(idx))
                    : Bars.GetVolume(idx);

                double val;

                if (ShowPercent)
                {
                    val = CountDown ? (1 - Bars.PercentComplete) * 100 : Bars.PercentComplete * 100;
                    _currentCountdownText = val.ToString("F0") + "%";
                }
                else
                {
                    if (isTimeBased)
                    {
                        double totalSeconds = 0;
                        if (BarsPeriod.BarsPeriodType == BarsPeriodType.Second) totalSeconds = BarsPeriod.Value;
                        else if (BarsPeriod.BarsPeriodType == BarsPeriodType.Minute) totalSeconds = BarsPeriod.Value * 60;
                        else if (BarsPeriod.BarsPeriodType == BarsPeriodType.Day) totalSeconds = 86400;

                         if (totalSeconds == 0 && (BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Second || BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Minute))
                        {
                            if (BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Second) totalSeconds = BarsPeriod.BaseBarsPeriodValue;
                            else if (BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Minute) totalSeconds = BarsPeriod.BaseBarsPeriodValue * 60;
                        }

                        if (totalSeconds > 0)
                        {
                             DateTime barTime = Bars.GetTime(idx);
                             if (CountDown && barTime > DateTime.Now)
                             {
                                 TimeSpan remaining = barTime.Subtract(DateTime.Now);
                                 val = Math.Max(0, remaining.TotalSeconds);
                             }
                             else
                             {
                                 val = CountDown ? totalSeconds * (1 - Bars.PercentComplete) : totalSeconds * Bars.PercentComplete;
                             }
                             
                             // Format
                             TimeSpan t = TimeSpan.FromSeconds(val);
                             if (t.TotalHours >= 1) _currentCountdownText = string.Format("{0:D2}:{1:D2}:{2:D2}", (int)t.TotalHours, t.Minutes, t.Seconds);
                             else _currentCountdownText = string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds);
                        }
                        else _currentCountdownText = "";
                    }
                    else
                    {
                        // Volume/Tick based
                        if (BarsPeriod.BarsPeriodType == BarsPeriodType.Tick)
                        {
                             val = CountDown ? BarsPeriod.Value - Bars.TickCount : Bars.TickCount;
                        }
                        else 
                        {
                             double totalVolume = isVolumeBase ? BarsPeriod.BaseBarsPeriodValue : BarsPeriod.Value;
                             val = CountDown ? totalVolume - volume : volume;
                        }
                        _currentCountdownText = val.ToString("F0");
                    }
                }
                
                // Repaint only if standalone (Strategy handles its own repaint)
                if (ShowLabels) 
                {
                    // If we are triggering invalidates too often it might be heavy.
                    // But for countdown it's needed.
                    // Only invalidate if we are actually drawing it here.
                    ChartControl.InvalidateVisual(); 
                }
            }
            catch {}
        }



    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RelativeIndicators.RelativeVwap[] cacheRelativeVwap;
		public RelativeIndicators.RelativeVwap RelativeVwap(VwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, bool showAsia, string asiaStartTime, string asiaEndTime, bool showAsiaHigh, bool showAsiaLow, bool showEurope, string europeStartTime, string europeEndTime, bool showEuropeHigh, bool showEuropeLow, bool showUS, string uSStartTime, string uSEndTime, bool showUSHigh, bool showUSLow, float historicalVWAPThickness, bool extendLinesUntilTouch, string highVwapLabel, string lowVwapLabel, bool showDaysAgo, TradeDirectionMode tradeDirection, bool showLabels, LabelMode labelDisplayMode, string customSignal1Text, string customSignal2Text, string customSignal3Text, bool showSignalLabels, bool showSignal1, bool showSignal2, bool showSignal3, int labelFontSize, int labelTextOffset, double labelDistanceATR, double labelCollisionSpacing, int detachmentTicks, int signal2ThresholdTicks, bool enableAlerts, string alertSound, bool showDebugLabels, bool enableFileLogging, bool showCountdown, bool countDown, bool showPercent, int countdownFontSize, int countdownOffsetX, int countdownOffsetY)
		{
			return RelativeVwap(Input, vwapMethod, maxHistoryDays, useExchangeTime, showAsia, asiaStartTime, asiaEndTime, showAsiaHigh, showAsiaLow, showEurope, europeStartTime, europeEndTime, showEuropeHigh, showEuropeLow, showUS, uSStartTime, uSEndTime, showUSHigh, showUSLow, historicalVWAPThickness, extendLinesUntilTouch, highVwapLabel, lowVwapLabel, showDaysAgo, tradeDirection, showLabels, labelDisplayMode, customSignal1Text, customSignal2Text, customSignal3Text, showSignalLabels, showSignal1, showSignal2, showSignal3, labelFontSize, labelTextOffset, labelDistanceATR, labelCollisionSpacing, detachmentTicks, signal2ThresholdTicks, enableAlerts, alertSound, showDebugLabels, enableFileLogging, showCountdown, countDown, showPercent, countdownFontSize, countdownOffsetX, countdownOffsetY);
		}

		public RelativeIndicators.RelativeVwap RelativeVwap(ISeries<double> input, VwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, bool showAsia, string asiaStartTime, string asiaEndTime, bool showAsiaHigh, bool showAsiaLow, bool showEurope, string europeStartTime, string europeEndTime, bool showEuropeHigh, bool showEuropeLow, bool showUS, string uSStartTime, string uSEndTime, bool showUSHigh, bool showUSLow, float historicalVWAPThickness, bool extendLinesUntilTouch, string highVwapLabel, string lowVwapLabel, bool showDaysAgo, TradeDirectionMode tradeDirection, bool showLabels, LabelMode labelDisplayMode, string customSignal1Text, string customSignal2Text, string customSignal3Text, bool showSignalLabels, bool showSignal1, bool showSignal2, bool showSignal3, int labelFontSize, int labelTextOffset, double labelDistanceATR, double labelCollisionSpacing, int detachmentTicks, int signal2ThresholdTicks, bool enableAlerts, string alertSound, bool showDebugLabels, bool enableFileLogging, bool showCountdown, bool countDown, bool showPercent, int countdownFontSize, int countdownOffsetX, int countdownOffsetY)
		{
			if (cacheRelativeVwap != null)
				for (int idx = 0; idx < cacheRelativeVwap.Length; idx++)
					if (cacheRelativeVwap[idx] != null && cacheRelativeVwap[idx].VwapMethod == vwapMethod && cacheRelativeVwap[idx].MaxHistoryDays == maxHistoryDays && cacheRelativeVwap[idx].UseExchangeTime == useExchangeTime && cacheRelativeVwap[idx].ShowAsia == showAsia && cacheRelativeVwap[idx].AsiaStartTime == asiaStartTime && cacheRelativeVwap[idx].AsiaEndTime == asiaEndTime && cacheRelativeVwap[idx].ShowAsiaHigh == showAsiaHigh && cacheRelativeVwap[idx].ShowAsiaLow == showAsiaLow && cacheRelativeVwap[idx].ShowEurope == showEurope && cacheRelativeVwap[idx].EuropeStartTime == europeStartTime && cacheRelativeVwap[idx].EuropeEndTime == europeEndTime && cacheRelativeVwap[idx].ShowEuropeHigh == showEuropeHigh && cacheRelativeVwap[idx].ShowEuropeLow == showEuropeLow && cacheRelativeVwap[idx].ShowUS == showUS && cacheRelativeVwap[idx].USStartTime == uSStartTime && cacheRelativeVwap[idx].USEndTime == uSEndTime && cacheRelativeVwap[idx].ShowUSHigh == showUSHigh && cacheRelativeVwap[idx].ShowUSLow == showUSLow && cacheRelativeVwap[idx].HistoricalVWAPThickness == historicalVWAPThickness && cacheRelativeVwap[idx].ExtendLinesUntilTouch == extendLinesUntilTouch && cacheRelativeVwap[idx].HighVwapLabel == highVwapLabel && cacheRelativeVwap[idx].LowVwapLabel == lowVwapLabel && cacheRelativeVwap[idx].ShowDaysAgo == showDaysAgo && cacheRelativeVwap[idx].TradeDirection == tradeDirection && cacheRelativeVwap[idx].ShowLabels == showLabels && cacheRelativeVwap[idx].LabelDisplayMode == labelDisplayMode && cacheRelativeVwap[idx].CustomSignal1Text == customSignal1Text && cacheRelativeVwap[idx].CustomSignal2Text == customSignal2Text && cacheRelativeVwap[idx].CustomSignal3Text == customSignal3Text && cacheRelativeVwap[idx].ShowSignalLabels == showSignalLabels && cacheRelativeVwap[idx].ShowSignal1 == showSignal1 && cacheRelativeVwap[idx].ShowSignal2 == showSignal2 && cacheRelativeVwap[idx].ShowSignal3 == showSignal3 && cacheRelativeVwap[idx].LabelFontSize == labelFontSize && cacheRelativeVwap[idx].LabelTextOffset == labelTextOffset && cacheRelativeVwap[idx].LabelDistanceATR == labelDistanceATR && cacheRelativeVwap[idx].LabelCollisionSpacing == labelCollisionSpacing && cacheRelativeVwap[idx].DetachmentTicks == detachmentTicks && cacheRelativeVwap[idx].Signal2ThresholdTicks == signal2ThresholdTicks && cacheRelativeVwap[idx].EnableAlerts == enableAlerts && cacheRelativeVwap[idx].AlertSound == alertSound && cacheRelativeVwap[idx].ShowDebugLabels == showDebugLabels && cacheRelativeVwap[idx].EnableFileLogging == enableFileLogging && cacheRelativeVwap[idx].ShowCountdown == showCountdown && cacheRelativeVwap[idx].CountDown == countDown && cacheRelativeVwap[idx].ShowPercent == showPercent && cacheRelativeVwap[idx].CountdownFontSize == countdownFontSize && cacheRelativeVwap[idx].CountdownOffsetX == countdownOffsetX && cacheRelativeVwap[idx].CountdownOffsetY == countdownOffsetY && cacheRelativeVwap[idx].EqualsInput(input))
						return cacheRelativeVwap[idx];
			return CacheIndicator<RelativeIndicators.RelativeVwap>(new RelativeIndicators.RelativeVwap(){ VwapMethod = vwapMethod, MaxHistoryDays = maxHistoryDays, UseExchangeTime = useExchangeTime, ShowAsia = showAsia, AsiaStartTime = asiaStartTime, AsiaEndTime = asiaEndTime, ShowAsiaHigh = showAsiaHigh, ShowAsiaLow = showAsiaLow, ShowEurope = showEurope, EuropeStartTime = europeStartTime, EuropeEndTime = europeEndTime, ShowEuropeHigh = showEuropeHigh, ShowEuropeLow = showEuropeLow, ShowUS = showUS, USStartTime = uSStartTime, USEndTime = uSEndTime, ShowUSHigh = showUSHigh, ShowUSLow = showUSLow, HistoricalVWAPThickness = historicalVWAPThickness, ExtendLinesUntilTouch = extendLinesUntilTouch, HighVwapLabel = highVwapLabel, LowVwapLabel = lowVwapLabel, ShowDaysAgo = showDaysAgo, TradeDirection = tradeDirection, ShowLabels = showLabels, LabelDisplayMode = labelDisplayMode, CustomSignal1Text = customSignal1Text, CustomSignal2Text = customSignal2Text, CustomSignal3Text = customSignal3Text, ShowSignalLabels = showSignalLabels, ShowSignal1 = showSignal1, ShowSignal2 = showSignal2, ShowSignal3 = showSignal3, LabelFontSize = labelFontSize, LabelTextOffset = labelTextOffset, LabelDistanceATR = labelDistanceATR, LabelCollisionSpacing = labelCollisionSpacing, DetachmentTicks = detachmentTicks, Signal2ThresholdTicks = signal2ThresholdTicks, EnableAlerts = enableAlerts, AlertSound = alertSound, ShowDebugLabels = showDebugLabels, EnableFileLogging = enableFileLogging, ShowCountdown = showCountdown, CountDown = countDown, ShowPercent = showPercent, CountdownFontSize = countdownFontSize, CountdownOffsetX = countdownOffsetX, CountdownOffsetY = countdownOffsetY }, input, ref cacheRelativeVwap);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeVwap RelativeVwap(VwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, bool showAsia, string asiaStartTime, string asiaEndTime, bool showAsiaHigh, bool showAsiaLow, bool showEurope, string europeStartTime, string europeEndTime, bool showEuropeHigh, bool showEuropeLow, bool showUS, string uSStartTime, string uSEndTime, bool showUSHigh, bool showUSLow, float historicalVWAPThickness, bool extendLinesUntilTouch, string highVwapLabel, string lowVwapLabel, bool showDaysAgo, TradeDirectionMode tradeDirection, bool showLabels, LabelMode labelDisplayMode, string customSignal1Text, string customSignal2Text, string customSignal3Text, bool showSignalLabels, bool showSignal1, bool showSignal2, bool showSignal3, int labelFontSize, int labelTextOffset, double labelDistanceATR, double labelCollisionSpacing, int detachmentTicks, int signal2ThresholdTicks, bool enableAlerts, string alertSound, bool showDebugLabels, bool enableFileLogging, bool showCountdown, bool countDown, bool showPercent, int countdownFontSize, int countdownOffsetX, int countdownOffsetY)
		{
			return indicator.RelativeVwap(Input, vwapMethod, maxHistoryDays, useExchangeTime, showAsia, asiaStartTime, asiaEndTime, showAsiaHigh, showAsiaLow, showEurope, europeStartTime, europeEndTime, showEuropeHigh, showEuropeLow, showUS, uSStartTime, uSEndTime, showUSHigh, showUSLow, historicalVWAPThickness, extendLinesUntilTouch, highVwapLabel, lowVwapLabel, showDaysAgo, tradeDirection, showLabels, labelDisplayMode, customSignal1Text, customSignal2Text, customSignal3Text, showSignalLabels, showSignal1, showSignal2, showSignal3, labelFontSize, labelTextOffset, labelDistanceATR, labelCollisionSpacing, detachmentTicks, signal2ThresholdTicks, enableAlerts, alertSound, showDebugLabels, enableFileLogging, showCountdown, countDown, showPercent, countdownFontSize, countdownOffsetX, countdownOffsetY);
		}

		public Indicators.RelativeIndicators.RelativeVwap RelativeVwap(ISeries<double> input , VwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, bool showAsia, string asiaStartTime, string asiaEndTime, bool showAsiaHigh, bool showAsiaLow, bool showEurope, string europeStartTime, string europeEndTime, bool showEuropeHigh, bool showEuropeLow, bool showUS, string uSStartTime, string uSEndTime, bool showUSHigh, bool showUSLow, float historicalVWAPThickness, bool extendLinesUntilTouch, string highVwapLabel, string lowVwapLabel, bool showDaysAgo, TradeDirectionMode tradeDirection, bool showLabels, LabelMode labelDisplayMode, string customSignal1Text, string customSignal2Text, string customSignal3Text, bool showSignalLabels, bool showSignal1, bool showSignal2, bool showSignal3, int labelFontSize, int labelTextOffset, double labelDistanceATR, double labelCollisionSpacing, int detachmentTicks, int signal2ThresholdTicks, bool enableAlerts, string alertSound, bool showDebugLabels, bool enableFileLogging, bool showCountdown, bool countDown, bool showPercent, int countdownFontSize, int countdownOffsetX, int countdownOffsetY)
		{
			return indicator.RelativeVwap(input, vwapMethod, maxHistoryDays, useExchangeTime, showAsia, asiaStartTime, asiaEndTime, showAsiaHigh, showAsiaLow, showEurope, europeStartTime, europeEndTime, showEuropeHigh, showEuropeLow, showUS, uSStartTime, uSEndTime, showUSHigh, showUSLow, historicalVWAPThickness, extendLinesUntilTouch, highVwapLabel, lowVwapLabel, showDaysAgo, tradeDirection, showLabels, labelDisplayMode, customSignal1Text, customSignal2Text, customSignal3Text, showSignalLabels, showSignal1, showSignal2, showSignal3, labelFontSize, labelTextOffset, labelDistanceATR, labelCollisionSpacing, detachmentTicks, signal2ThresholdTicks, enableAlerts, alertSound, showDebugLabels, enableFileLogging, showCountdown, countDown, showPercent, countdownFontSize, countdownOffsetX, countdownOffsetY);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeVwap RelativeVwap(VwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, bool showAsia, string asiaStartTime, string asiaEndTime, bool showAsiaHigh, bool showAsiaLow, bool showEurope, string europeStartTime, string europeEndTime, bool showEuropeHigh, bool showEuropeLow, bool showUS, string uSStartTime, string uSEndTime, bool showUSHigh, bool showUSLow, float historicalVWAPThickness, bool extendLinesUntilTouch, string highVwapLabel, string lowVwapLabel, bool showDaysAgo, TradeDirectionMode tradeDirection, bool showLabels, LabelMode labelDisplayMode, string customSignal1Text, string customSignal2Text, string customSignal3Text, bool showSignalLabels, bool showSignal1, bool showSignal2, bool showSignal3, int labelFontSize, int labelTextOffset, double labelDistanceATR, double labelCollisionSpacing, int detachmentTicks, int signal2ThresholdTicks, bool enableAlerts, string alertSound, bool showDebugLabels, bool enableFileLogging, bool showCountdown, bool countDown, bool showPercent, int countdownFontSize, int countdownOffsetX, int countdownOffsetY)
		{
			return indicator.RelativeVwap(Input, vwapMethod, maxHistoryDays, useExchangeTime, showAsia, asiaStartTime, asiaEndTime, showAsiaHigh, showAsiaLow, showEurope, europeStartTime, europeEndTime, showEuropeHigh, showEuropeLow, showUS, uSStartTime, uSEndTime, showUSHigh, showUSLow, historicalVWAPThickness, extendLinesUntilTouch, highVwapLabel, lowVwapLabel, showDaysAgo, tradeDirection, showLabels, labelDisplayMode, customSignal1Text, customSignal2Text, customSignal3Text, showSignalLabels, showSignal1, showSignal2, showSignal3, labelFontSize, labelTextOffset, labelDistanceATR, labelCollisionSpacing, detachmentTicks, signal2ThresholdTicks, enableAlerts, alertSound, showDebugLabels, enableFileLogging, showCountdown, countDown, showPercent, countdownFontSize, countdownOffsetX, countdownOffsetY);
		}

		public Indicators.RelativeIndicators.RelativeVwap RelativeVwap(ISeries<double> input , VwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, bool showAsia, string asiaStartTime, string asiaEndTime, bool showAsiaHigh, bool showAsiaLow, bool showEurope, string europeStartTime, string europeEndTime, bool showEuropeHigh, bool showEuropeLow, bool showUS, string uSStartTime, string uSEndTime, bool showUSHigh, bool showUSLow, float historicalVWAPThickness, bool extendLinesUntilTouch, string highVwapLabel, string lowVwapLabel, bool showDaysAgo, TradeDirectionMode tradeDirection, bool showLabels, LabelMode labelDisplayMode, string customSignal1Text, string customSignal2Text, string customSignal3Text, bool showSignalLabels, bool showSignal1, bool showSignal2, bool showSignal3, int labelFontSize, int labelTextOffset, double labelDistanceATR, double labelCollisionSpacing, int detachmentTicks, int signal2ThresholdTicks, bool enableAlerts, string alertSound, bool showDebugLabels, bool enableFileLogging, bool showCountdown, bool countDown, bool showPercent, int countdownFontSize, int countdownOffsetX, int countdownOffsetY)
		{
			return indicator.RelativeVwap(input, vwapMethod, maxHistoryDays, useExchangeTime, showAsia, asiaStartTime, asiaEndTime, showAsiaHigh, showAsiaLow, showEurope, europeStartTime, europeEndTime, showEuropeHigh, showEuropeLow, showUS, uSStartTime, uSEndTime, showUSHigh, showUSLow, historicalVWAPThickness, extendLinesUntilTouch, highVwapLabel, lowVwapLabel, showDaysAgo, tradeDirection, showLabels, labelDisplayMode, customSignal1Text, customSignal2Text, customSignal3Text, showSignalLabels, showSignal1, showSignal2, showSignal3, labelFontSize, labelTextOffset, labelDistanceATR, labelCollisionSpacing, detachmentTicks, signal2ThresholdTicks, enableAlerts, alertSound, showDebugLabels, enableFileLogging, showCountdown, countDown, showPercent, countdownFontSize, countdownOffsetX, countdownOffsetY);
		}
	}
}

#endregion
