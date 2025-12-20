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

using NinjaTrader.NinjaScript.DrawingTools;
using System.Globalization;
using NinjaTrader.NinjaScript.Indicators.RelativeIndicators; // Fix for Generated Code Visibility
#endregion

namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
    public enum TradeDirectionMode { Both, LongOnly, ShortOnly }
    public class RelativeVwap : Indicator
    {
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
        private double verticalUnit;
        
        // V_COLLISION: Anti-Collision Stacks
        private int lastColBarIdx = -1;
        private double stackHighY = double.MinValue;
        private double stackLowY = double.MaxValue;
        
        // Helper Methods for Stacking
        private double GetStackedHighY(double desiredY, double heightBuffer)
        {
             // If this is the first item (stack is empty), just take desiredY
             if (stackHighY == double.MinValue) 
             {
                 stackHighY = desiredY;
                 return desiredY;
             }
             
             // If desiredY is overlapping or below the stack, push it UP
             if (desiredY <= stackHighY + heightBuffer)
             {
                  double newY = stackHighY + heightBuffer;
                  stackHighY = newY;
                  return newY;
             }
             else
             {
                  // It's way above, so it's safe. Update stack to this new high.
                  stackHighY = desiredY;
                  return desiredY;
             }
        }

        private double GetStackedLowY(double desiredY, double heightBuffer)
        {
             // If this is the first item (stack is empty), just take desiredY
             if (stackLowY == double.MaxValue) 
             {
                 stackLowY = desiredY;
                 return desiredY;
             }
             
             // If desiredY is overlapping or ABOVE the stack (remember Lows go DOWN), push it DOWN
             if (desiredY >= stackLowY - heightBuffer)
             {
                  double newY = stackLowY - heightBuffer;
                  stackLowY = newY;
                  return newY;
             }
             else
             {
                  // It's way below, so it's safe.
                  stackLowY = desiredY;
                  return desiredY;
             }
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

        // Helper for safely adding signals
        // Helper for safely adding signals
        private void AddSignal(int barIdx, double price, string text, bool isHigh, Brush brush, string signalType)
        {
            if (signalLabels == null) return;
            
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
                Description = "RelativeVwap (Unified V2): Enhanced Strategy with robust logging and account filtering.";
                Name = "RelativeVwap";
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
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "HighVWAP"); // Values[0]
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "LowVWAP");  // Values[1]

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
                
                EnableAlerts = true;
                AlertSound = "mzpack_alert4.wav";

                
                ShowDaysAgo = true; // Default True
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(14); // V_NORM: Correct Initialization

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
            // highCumPV = 0; highCumVol = 0; // RESET DISABLED
            // lowCumPV = 0; lowCumVol = 0; // RESET DISABLED
            
            // FIX: Reset Unlocked Sessions to prevent persistence of yesterday's internal anchors
            lastUnlockedHighSession = null;
            lastUnlockedLowSession = null;
            
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
              if (CurrentBar < 14) return; // Wait for ATR
              debugUpdateCounter++; // Count EVERY call
              
              // V_NORM: Calculate dynamic vertical unit (1 unit = 10% of 14-period ATR)
              // This ensures visually identical spacing on MNQ, MES, MYM, etc.
              verticalUnit = atr[0] * 0.1;
              if (verticalUnit <= 0) verticalUnit = TickSize; // Fallback
              
              // V_COLLISION: Reset Stacks for New Bar / Tick Re-calculation
              // Must reset EVERY update because OnBarUpdate rebuilds the layout for the current bar from scratch.
              stackHighY = double.MinValue;
              stackLowY = double.MaxValue;
              lastColBarIdx = CurrentBar;
              
             if (hasHighVWAP) {
         double hVol = Math.Max(1, sessionHighVol); // DivZero Prot
         double val = sessionHighPV / hVol;
         Values[0][0] = val; // High VWAP
     } else {
         Values[0][0] = Values[0].IsValidDataPointAt(CurrentBar - 1) ? Values[0][1] : Close[0];
     }
     
     if (hasLowVWAP) {
         double lVol = Math.Max(1, sessionLowVol);
         double val = sessionLowPV / lVol;
         Values[1][0] = val; // Low VWAP
     } else {
         Values[1][0] = Values[1].IsValidDataPointAt(CurrentBar - 1) ? Values[1][1] : Close[0];
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
                  
                  highCumPV = 0; highCumVol = 0;
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

                  lowCumPV = 0; lowCumVol = 0;
              }
             
             double typicalPrice = (High[0] + Low[0] + Close[0]) / 3.0; // Approximation for Historical
             double volume = Volume[0];
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
                 // V_SYNC: Use Typical Price to match Strategy/Backtest precision
                 double tickVol = volume - _lastVol;
                 if (tickVol < 0) tickVol = volume; // New Bar
                 
                  // Accumulate using Typical Price
                  if (sessionHighBarIdx != -1) { highCumPV += typicalPrice * tickVol; highCumVol += tickVol; }
                  if (sessionLowBarIdx != -1) { lowCumPV += typicalPrice * tickVol; lowCumVol += tickVol; }
                 
                 _lastVol = volume;
             }
             else
             {
                 // Historical: Use Bar Approximation (Typical * Vol)
                 // This runs once per bar Close
                 if (sessionHighBarIdx != -1) {
                     highCumPV += typicalPrice * volume;
                     highCumVol += volume;
                 }
                 if (sessionLowBarIdx != -1) {
                     lowCumPV += typicalPrice * volume;
                     lowCumVol += volume;
                 }
                 
                // V_VWAP: Session-Specific Anchored VWAPs (Historical) - REMOVED
             }

               // 1. Calculate Current VWAP Values
               currentHighVWAP = (highCumVol > 0) ? highCumPV / highCumVol : High[0];
               currentLowVWAP = (lowCumVol > 0) ? lowCumPV / lowCumVol : Low[0];
              
               hasHighVWAP = sessionHighBarIdx != -1 && highCumVol > 0;
               hasLowVWAP = sessionLowBarIdx != -1 && lowCumVol > 0;

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
                      // V_SIGNAL_3: ENTRY TRIGGER (White Arrow on Touch) -- AUTOMATIC OFFSET
                      if (highSignal2Fired)
                      {
                          // 1. Calculate Volatility (Avg Range of last 3 bars)
                          double range0 = High[0] - Low[0];
                          double range1 = CurrentBar > 0 ? High[1] - Low[1] : range0;
                          double range2 = CurrentBar > 1 ? High[2] - Low[2] : range0;
                          double avgRange = (range0 + range1 + range2) / 3.0;

                          // 2. Configurable Offsets & Anti-Collision
                          // Calculate Base Positions
                          double proposedArrowY = hVwap + (SignalIconOffsetTicks * verticalUnit);
                          double proposedLabelY = hVwap + (SignalTextOffsetTicks * verticalUnit);

                          // STACK 1: Arrow (Pushes stack up)
                          double arrowY = GetStackedHighY(proposedArrowY, 8 * verticalUnit);
                          
                          // STACK 2: Label (Pushes stack further up)
                          double labelY = GetStackedHighY(Math.Max(proposedLabelY, arrowY + 2 * verticalUnit), 15 * verticalUnit); 
                          
                          // 3. Draw Visuals (Order: Line -> Text -> Arrow on Top)
                          // Connector Line REMOVED
                          // Draw.Line(this, "ConnH_" + CurrentBar, true, 0, arrowY, 0, labelY, Brushes.White, DashStyleHelper.Solid, 1);
                          // Determine Brush
                          Brush sigBrush = Brushes.White;
                          if (lastUnlockedHighSession != null)
                          {
                               if (lastUnlockedHighSession.Name.StartsWith("Asia")) sigBrush = AsiaLineColor;
                               else if (lastUnlockedHighSession.Name.StartsWith("Europe")) sigBrush = EuropeLineColor;
                               else if (lastUnlockedHighSession.Name.StartsWith("USA")) sigBrush = USLineColor;
                          }

                          if (lastUnlockedHighSession != null)
                          {
                              string entryLabel = "3";
                              if (!UseSimpleLabels)
                              {
                                  entryLabel = GetSignalCode(lastUnlockedHighSession, "H");
                                  if (lastUnlockedHighSession.IsInternalHigh) entryLabel = "i" + entryLabel;
                                  entryLabel += "." + highAnchorSequence + ".1";
                              }
                              // Pass 'labelY' which accounts for ATR offset and Stacking calculated above
                              AddSignal(CurrentBar, labelY, entryLabel, true, sigBrush, "Sig3");
                          }
                          // Arrow (Last to draw on top of line start)
                          Draw.ArrowDown(this, "EntryH_" + CurrentBar, true, 0, arrowY, sigBrush);
                      }

                      highDetached = false;
                      highSignal2Fired = false; // Reset Signal 2 on Touch
                      // If we reset, and we didn't just fire 'E' (dbgText != "E"), then we should NOT show 'D'.
                      if (dbgText == "D") dbgText = ""; 
                  }
                 
                  // FINAL DRAW CALL
                  if (ShowDebugLabels && !string.IsNullOrEmpty(dbgText) && dbgText != "D") // Only show "D" or specialized debug. Don't show Signal Codes here.
                  {
                       // Draw.Text(this, "DebugHi" + CurrentBar, dbgText, 0, high + dbgOffset, dbgBrush); // DISABLED to prevent overlap with AddSignal
                  }

                  // V_SIGNAL_2: SECONDARY CONFIRMATION (Yellow Dot) - UNIQUE PER ANCHOR
                  // Condition: Active VWAP (Taken), Candle High BELOW VWAP by threshold
                  // CHECK: Have we signaled for THIS specific anchor yet?
                  // FIX: Confirmed on Close (IsFirstTickOfBar check for Previous Bar [1])
                  if (IsFirstTickOfBar && highHasTakenRelevant)
                  {
                      // Use Previous Bar's VWAP (Values[0][1]) and Previous Bar's Low (High[1])
                      double prevHVwap = Values[0][1];
                      if (High[1] <= (prevHVwap - Signal2ThresholdTicks * TickSize))
                      {
                          if (sessionHighBarIdx != lastSignaledHighAnchorBar)
                          {
                              highAnchorSequence++;
                              
                              // Determine Brush
                              Brush sigBrush = HighVWAPColor; // Default
                              if (lastUnlockedHighSession != null)
                              {
                                   if (lastUnlockedHighSession.Name.StartsWith("Asia")) sigBrush = AsiaLineColor;
                                   else if (lastUnlockedHighSession.Name.StartsWith("Europe")) sigBrush = EuropeLineColor;
                                   else if (lastUnlockedHighSession.Name.StartsWith("USA")) sigBrush = USLineColor;
                              }
    
                              double proposedDotY = High[1] + (SignalIconOffsetTicks * verticalUnit);
                              double dotY = GetStackedHighY(proposedDotY, 5 * verticalUnit);
                              Draw.Dot(this, "Sig2H_" + (CurrentBar - 1), true, 1, dotY, sigBrush); // Draw on T-1
                              
                              // Label: e.g. "UH1.1", "UH1.2"
                              if (lastUnlockedHighSession != null)
                              {
                                  string code = "2";
                                  if (!UseSimpleLabels)
                                  {
                                      code = GetSignalCode(lastUnlockedHighSession, "H");
                                      if (lastUnlockedHighSession.IsInternalHigh) code = "i" + code;
                                      code += "." + highAnchorSequence;
                                  }
                                  // Calculate Label Y for Sig2 (Dot)
                                  double label2Y = GetStackedHighY(High[1] + (SignalTextOffsetTicks * verticalUnit), 15 * verticalUnit);
                                  AddSignal(CurrentBar - 1, label2Y, code, true, sigBrush, "Sig2");
                              }
                              
                              lastSignaledHighAnchorBar = sessionHighBarIdx; // Mark this anchor as USED
                              highSignal2Fired = true;
                          }
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
                       dbgText = "D"; dbgBrush = Brushes.Cyan; dbgOffset = 40 * verticalUnit;
                   }

                  // UPDATED DETACHMENT LOGIC (Configurable Ticks)
                  // Condition: Close must be ABOVE VWAP, AND Low must be ABOVE (VWAP + Buffer)
                  double detachThreshold = lVwap + (DetachmentTicks * TickSize);
                  
                  if (!lowDetached && CurrentBar > 0 && Close[0] > lVwap && Low[0] > detachThreshold)
                  {
                       lowDetached = true;
                       // Update Debug State immediately
                       dbgText = "D"; dbgBrush = Brushes.Cyan; dbgOffset = 40 * verticalUnit;
                   }
                      
                 // Trigger: Low <= VWAP
                 if (lowDetached && low <= lVwap && !lowSignalFired)
                 {
                     // Signal Fired -> Override Debug Label to 'E'
                     // Signal Fired -> Override Debug Label to 'E'
                     // Use Code if available
                      string sigCode = (lastUnlockedLowSession != null) ? GetSignalCode(lastUnlockedLowSession, "L") : "E";
                      dbgText = sigCode; dbgBrush = Brushes.Lime; dbgOffset = 60 * verticalUnit;

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
                      // V_SIGNAL_3: ENTRY TRIGGER (White Arrow on Touch) -- AUTOMATIC OFFSET
                      if (lowSignal2Fired)
                      {
                          // 1. Calculate Volatility (Avg Range of last 3 bars)
                          double range0 = High[0] - Low[0];
                          double range1 = CurrentBar > 0 ? High[1] - Low[1] : range0;
                          double range2 = CurrentBar > 1 ? High[2] - Low[2] : range0;
                          double avgRange = (range0 + range1 + range2) / 3.0;

                          // 2. Configurable Offsets & Anti-Collision
                          // Calculate Base Positions (Below Low)
                          double proposedArrowY = lVwap - (SignalIconOffsetTicks * verticalUnit);
                          double proposedTextY = lVwap - (SignalTextOffsetTicks * verticalUnit);
                          
                          // STACK 1: Arrow (Pushes stack DOWN)
                          double arrowY = GetStackedLowY(proposedArrowY, 8 * verticalUnit);
                          
                          // STACK 2: Label (Pushes stack further DOWN)
                          // Ensure label is at least below the arrow
                          double labelY = GetStackedLowY(Math.Min(proposedTextY, arrowY - 2 * verticalUnit), 15 * verticalUnit);
                          
                          // 3. Draw Visuals (Order: Line -> Text -> Arrow on Top)
                          // Connector Line REMOVED
                          // Draw.Line(this, "ConnL_" + CurrentBar, true, 0, arrowY, 0, labelY, Brushes.White, DashStyleHelper.Solid, 1);
                          // Label
                          // Determine Brush
                          Brush sigBrush = Brushes.White;
                          if (lastUnlockedLowSession != null)
                          {
                               if (lastUnlockedLowSession.Name.StartsWith("Asia")) sigBrush = AsiaLineColor;
                               else if (lastUnlockedLowSession.Name.StartsWith("Europe")) sigBrush = EuropeLineColor;
                               else if (lastUnlockedLowSession.Name.StartsWith("USA")) sigBrush = USLineColor;
                          }

                          // Label
                          if (lastUnlockedLowSession != null)
                          {
                              string entryLabel = "3";
                              if (!UseSimpleLabels)
                              {
                                  entryLabel = GetSignalCode(lastUnlockedLowSession, "L");
                                  if (lastUnlockedLowSession.IsInternalLow) entryLabel = "i" + entryLabel;
                                  entryLabel += "." + lowAnchorSequence + ".1";
                              }
                              AddSignal(CurrentBar, labelY, entryLabel, false, sigBrush, "Sig3");
                          }
                          // Arrow (Last)
                          Draw.ArrowUp(this, "EntryL_" + CurrentBar, true, 0, arrowY, sigBrush);
                      }

                      lowDetached = false;
                      lowSignal2Fired = false; // Reset Signal 2 on Touch
                      // If we reset, and we didn't just fire 'E' (dbgText != "E"), then we should NOT show 'D'.
                      if (dbgText == "D") dbgText = ""; 
                  }
                 
                 // FINAL DRAW CALL
                 if (ShowDebugLabels && !string.IsNullOrEmpty(dbgText) && dbgText != "D") // Only show "D" or specialized debug.
                 {
                      // Draw.Text(this, "DebugLow" + CurrentBar, dbgText, 0, low - dbgOffset, dbgBrush); // DISABLED to prevent overlap
                 }

                  // V_SIGNAL_2: SECONDARY CONFIRMATION (Yellow Dot) - UNIQUE PER ANCHOR
                  // Condition: Active VWAP (Taken), Candle Low ABOVE VWAP by threshold
                  // CHECK: Have we signaled for THIS specific anchor yet?
                  // FIX: Confirmed on Close (IsFirstTickOfBar check for Previous Bar [1])
                  if (IsFirstTickOfBar && lowHasTakenRelevant)
                  {
                       // Use Previous Bar's VWAP (Values[1][1]) and Previous Bar's Low (Low[1])
                       double prevLVwap = Values[1][1];
                       if (Low[1] >= (prevLVwap + Signal2ThresholdTicks * TickSize))
                       {
                           if (sessionLowBarIdx != lastSignaledLowAnchorBar)
                           {
                               lowAnchorSequence++;
                               
                               // Determine Brush
                               Brush sigBrush = LowVWAPColor; // Default
                               if (lastUnlockedLowSession != null)
                               {
                                    if (lastUnlockedLowSession.Name.StartsWith("Asia")) sigBrush = AsiaLineColor;
                                    else if (lastUnlockedLowSession.Name.StartsWith("Europe")) sigBrush = EuropeLineColor;
                                    else if (lastUnlockedLowSession.Name.StartsWith("USA")) sigBrush = USLineColor;
                               }
    
                               double proposedDotY = Low[1] - (SignalIconOffsetTicks * verticalUnit);
                               double dotY = GetStackedLowY(proposedDotY, 5 * verticalUnit);
                               Draw.Dot(this, "Sig2L_" + (CurrentBar - 1), true, 1, dotY, sigBrush); // Draw on T-1
                               
                               // Label: e.g. "UL1.1", "UL1.2"
                               if (lastUnlockedLowSession != null)
                               {
                                    string code = "2";
                                    if (!UseSimpleLabels)
                                    {
                                        code = GetSignalCode(lastUnlockedLowSession, "L");
                                        if (lastUnlockedLowSession.IsInternalLow) code = "i" + code;
                                        code += "." + lowAnchorSequence;
                                    }
                                    double label2Y = GetStackedLowY(Low[1] - (SignalTextOffsetTicks * verticalUnit), 15 * verticalUnit);
                                    AddSignal(CurrentBar - 1, label2Y, code, false, sigBrush, "Sig2");
                               }
                               
                               lastSignaledLowAnchorBar = sessionLowBarIdx; // Mark this anchor as USED
                               lowSignal2Fired = true;
                           }
                       }
                  }
              }

             
             // Status Overlay
             if (ShowDebugLabels && (CurrentBar == Bars.Count - 1))
             {
                 string status = string.Format("DEBUG STATUS\nTime: {0}\nHigh Active: {1} Locked: {2}\nLow Active: {3} Locked: {4}", Time[0], highHasTakenRelevant, highSignalFired, lowHasTakenRelevant, lowSignalFired);
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

        private string GetSignalCode(SessionLevelInfo session, string levelType)
        {
            if (session == null) return "";
            
            // Region
            string r = "X";
            if (session.Name.StartsWith("Asia")) r = "A";
            else if (session.Name.StartsWith("Europe")) r = "E";
            else if (session.Name.StartsWith("USA")) r = "U";
            
            // Days Ago
            // Use Time[0].Date as reference 'Today'
            int days = (int)(Time[0].Date - session.SessionDate.Date).TotalDays;
            if (days < 0) days = 0; // Should not happen
            
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
                        lastSignaledHighAnchorBar = -1; // FORCE RESET TRACKER
                        
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
                        string code = GetSignalCode(session, "H");
                        // if (session.IsInternalHigh) code = "i" + code; // REMOVED
                        
                        if (UseSimpleLabels) code = "1"; // OVERRIDE

                        LastSignalCode = code;

                        // Determine Brush
                        Brush sigBrush = Brushes.Yellow;
                        if (session.Name.StartsWith("Asia")) sigBrush = AsiaLineColor;
                        else if (session.Name.StartsWith("Europe")) sigBrush = EuropeLineColor;
                        else if (session.Name.StartsWith("USA")) sigBrush = USLineColor;

                        // V_VISUAL: SIGNAL 1 - TAKE LEVEL (RESISTANCE) - DEV MODE
                        double triY = GetStackedHighY(high + SignalIconOffsetTicks * verticalUnit, 5 * verticalUnit);
                        Draw.TriangleDown(this, "TakeHigh_" + session.Name + CurrentBar, true, 0, triY, sigBrush);
                        // V_VISUAL: Label Code - Route to Direct2D
                        double label1Y = GetStackedHighY(high + SignalTextOffsetTicks * verticalUnit, 15 * verticalUnit);
                        AddSignal(CurrentBar, label1Y, code, true, sigBrush, "Sig1");


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
                         lastSignaledLowAnchorBar = -1; // RESET
                         
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
                         string code = GetSignalCode(session, "L");
                         // if (session.IsInternalLow) code = "i" + code; // REMOVED
                         
                         if (UseSimpleLabels) code = "1"; // OVERRIDE

                         LastSignalCode = code;

                         // Determine Brush
                         Brush sigBrush = Brushes.Yellow;
                         if (session.Name.StartsWith("Asia")) sigBrush = AsiaLineColor;
                         else if (session.Name.StartsWith("Europe")) sigBrush = EuropeLineColor;
                         else if (session.Name.StartsWith("USA")) sigBrush = USLineColor;

                         // V_VISUAL: SIGNAL 1 - TAKE LEVEL (SUPPORT) - DEV MODE
                         // V_VISUAL: SIGNAL 1 - TAKE LEVEL (SUPPORT) - DEV MODE
                         double triY = GetStackedLowY(low - SignalIconOffsetTicks * verticalUnit, 5 * verticalUnit);
                         Draw.TriangleUp(this, "TakeLow_" + session.Name + CurrentBar, true, 0, triY, sigBrush);
                         
                         // V_VISUAL: Label Code - Route to Direct2D
                         double label1Y = GetStackedLowY(low - SignalTextOffsetTicks * verticalUnit, 15 * verticalUnit);
                         AddSignal(CurrentBar, label1Y, code, false, sigBrush, "Sig1");


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

        #region Properties
        [NinjaScriptProperty]
        [Display(Name="Use Exchange Time", Description="If true, start/end times are interpreted as New York Time and converted to Local Time.", Order=0, GroupName="Parameters")]
        public bool UseExchangeTime
        { get; set; }
        
        [Range(0, 50)]
        [Display(Name = "Detachment Ticks", Description = "Min ticks required between Candle High/Low and VWAP to consider it 'Detached'.", GroupName = "Parameters", Order = 10)]
        public int DetachmentTicks { get; set; } = 2; // Default 2

        [Range(0, 200)]
        [Display(Name = "Execution Plot Offset", Description = "Vertical distance (ticks) for signal arrows and text from the candle.", GroupName = "Visual", Order = 11)]
        public int ExecutionPlotOffsetTicks { get; set; } = 2;

        [Range(0, 200)]
        [Display(Name = "Text Separation", Description = "Vertical distance (ticks) between arrow and text.", GroupName = "Visual", Order = 12)]
        public int TextSeparationTicks { get; set; } = 30;

        [Range(0, 50)]
        [Display(Name = "Signal 2 Threshold", Description = "Ticks required for candle to close Inside VWAP for 2nd signal.", GroupName = "Visual", Order = 13)]
        public int Signal2ThresholdTicks { get; set; } = 1;
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
             base.OnRender(chartControl, chartScale); // Best practice: Call base
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
                  DrawAnchoredLine(sessionHighBarIdx, HighVWAPColor, "High VWAP", chartControl, chartScale);
              }
              if (hasLowVWAP)
              {
                  DrawAnchoredLine(sessionLowBarIdx, LowVWAPColor, "Low VWAP", chartControl, chartScale);
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
                    double price = (High.GetValueAt(i) + Low.GetValueAt(i) + Close.GetValueAt(i)) / 3.0;
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
        [XmlIgnore]
        [Display(Name = "High VWAP Color", GroupName = "3. Visuals", Order = 1)]
        public Brush HighVWAPColor { get; set; }

        [Browsable(false)]
        public string HighVWAPColorSerializable
        {
            get { return Serialize.BrushToString(HighVWAPColor); }
            set { HighVWAPColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Low VWAP Color", GroupName = "3. Visuals", Order = 3)]
        public Brush LowVWAPColor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Days Ago", Description = "Show 'X days' instead of date for past levels", GroupName = "3. Visuals", Order = 9)]
        public bool ShowDaysAgo { get; set; }

        [Browsable(false)]
        public string LowVWAPColorSerializable
        {
            get { return Serialize.BrushToString(LowVWAPColor); }
            set { LowVWAPColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Historical VWAP Color", GroupName = "3. Visuals", Order = 4)]
        public Brush HistoricalVWAPColor { get; set; }

        [Browsable(false)]
        public string HistoricalVWAPColorSerializable
        {
            get { return Serialize.BrushToString(HistoricalVWAPColor); }
            set { HistoricalVWAPColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(1.0f, 10.0f)]
        [Display(Name = "Historical VWAP Thickness", GroupName = "3. Visuals", Order = 4)]
        public float HistoricalVWAPThickness { get; set; }




        
        [NinjaScriptProperty]
        [Display(Name = "Show Labels", GroupName = "3. Visuals", Order = 5)]
        public bool ShowLabels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Simple Labels (1, 2, 3)", Description = "If true, shows '1', '2', '3' instead of full codes", GroupName = "3. Visuals", Order = 5)]
        public bool UseSimpleLabels { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Signal Icon Offset (Ticks)", Description = "Distance from candle/price to the Icon (Arrow/Dot)", GroupName = "3. Visuals", Order = 6)]
        public int SignalIconOffsetTicks { get; set; } = 15;

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Signal Text Offset (Ticks)", Description = "Distance from candle/price to the Text Label", GroupName = "3. Visuals", Order = 7)]
        public int SignalTextOffsetTicks { get; set; } = 30;


        
        [NinjaScriptProperty]
        [Display(Name = "Extend Lines Until Touch", GroupName = "3. Visuals", Order = 10)]
        public bool ExtendLinesUntilTouch { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 365)]
        [Display(Name = "Max History Days", Description = "Ignore levels older than X days", GroupName = "1. General", Order = 2)]
        public int MaxHistoryDays { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Show Debug Labels", Description = "Show text when a level is broken", GroupName = "4. Alerts", Order = 3)]
        public bool ShowDebugLabels { get; set; }


        
        [NinjaScriptProperty]
        [Display(Name = "Enable Alerts", Description = "Play sound on signal", GroupName = "4. Alerts", Order = 1)]
        public bool EnableAlerts { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Alert Sound", Description = "Sound file for alerts", GroupName = "4. Alerts", Order = 2)]
        public string AlertSound { get; set; }
        




        
        [NinjaScriptProperty]
        [Display(Name = "Trade Direction", GroupName = "5. Strategy", Order = 1)]
        public TradeDirectionMode TradeDirection { get; set; }
        
        // Session Properties
        
        // Asia
        [Display(Name = "Asia Start Time", GroupName = "2. Sessions", Order = 1)]
        public string AsiaStartTime { get; set; }
        [Display(Name = "Asia End Time", GroupName = "2. Sessions", Order = 2)]
        public string AsiaEndTime { get; set; }
        [Display(Name = "Show Asia", GroupName = "2. Sessions", Order = 3)]
        public bool ShowAsia { get; set; }
        [XmlIgnore]
        [Display(Name = "Asia Line Color", GroupName = "2. Sessions", Order = 6)]
        public Brush AsiaLineColor { get; set; }
        [Browsable(false)]
        public string AsiaLineColorSerializable
        {
            get { return Serialize.BrushToString(AsiaLineColor); }
            set { AsiaLineColor = Serialize.StringToBrush(value); }
        }
        [XmlIgnore]
        [Display(Name = "Asia Label Color", GroupName = "2. Sessions", Order = 7)]
        public Brush AsiaLabelColor { get; set; }
        [Browsable(false)]
        public string AsiaLabelColorSerializable
        {
            get { return Serialize.BrushToString(AsiaLabelColor); }
            set { AsiaLabelColor = Serialize.StringToBrush(value); }
        }
        [Display(Name = "Show Asia High", GroupName = "2. Sessions", Order = 4)]
        public bool ShowAsiaHigh { get; set; }
        [Display(Name = "Show Asia Low", GroupName = "2. Sessions", Order = 5)]
        public bool ShowAsiaLow { get; set; }

        // Europe
        [Display(Name = "Europe Start Time", GroupName = "2. Sessions", Order = 8)]
        public string EuropeStartTime { get; set; }
        [Display(Name = "Europe End Time", GroupName = "2. Sessions", Order = 9)]
        public string EuropeEndTime { get; set; }
        [Display(Name = "Show Europe", GroupName = "2. Sessions", Order = 10)]
        public bool ShowEurope { get; set; }
        [XmlIgnore]
        [Display(Name = "Europe Line Color", GroupName = "2. Sessions", Order = 13)]
        public Brush EuropeLineColor { get; set; }
        [Browsable(false)]
        public string EuropeLineColorSerializable
        {
            get { return Serialize.BrushToString(EuropeLineColor); }
            set { EuropeLineColor = Serialize.StringToBrush(value); }
        }
        [XmlIgnore]
        [Display(Name = "Europe Label Color", GroupName = "2. Sessions", Order = 14)]
        public Brush EuropeLabelColor { get; set; }
        [Browsable(false)]
        public string EuropeLabelColorSerializable
        {
            get { return Serialize.BrushToString(EuropeLabelColor); }
            set { EuropeLabelColor = Serialize.StringToBrush(value); }
        }
        [Display(Name = "Show Europe High", GroupName = "2. Sessions", Order = 11)]
        public bool ShowEuropeHigh { get; set; }
        [Display(Name = "Show Europe Low", GroupName = "2. Sessions", Order = 12)]
        public bool ShowEuropeLow { get; set; }

        // US
        [Display(Name = "US Start Time", GroupName = "2. Sessions", Order = 15)]
        public string USStartTime { get; set; }
        [Display(Name = "US End Time", GroupName = "2. Sessions", Order = 16)]
        public string USEndTime { get; set; }
        [Display(Name = "Show US", GroupName = "2. Sessions", Order = 17)]
        public bool ShowUS { get; set; }
        [XmlIgnore]
        [Display(Name = "US Line Color", GroupName = "2. Sessions", Order = 20)]
        public Brush USLineColor { get; set; }
        [Browsable(false)]
        public string USLineColorSerializable
        {
            get { return Serialize.BrushToString(USLineColor); }
            set { USLineColor = Serialize.StringToBrush(value); }
        }
        [XmlIgnore]
        [Display(Name = "US Label Color", GroupName = "2. Sessions", Order = 21)]
        public Brush USLabelColor { get; set; }
        [Browsable(false)]
        public string USLabelColorSerializable
        {
            get { return Serialize.BrushToString(USLabelColor); }
            set { USLabelColor = Serialize.StringToBrush(value); }
        }
        [Display(Name = "Show US High", GroupName = "2. Sessions", Order = 18)]
        public bool ShowUSHigh { get; set; }
        [Display(Name = "Show US Low", GroupName = "2. Sessions", Order = 19)]
        public bool ShowUSLow { get; set; }

        [XmlIgnore] [Display(Name = "Label Background Color", GroupName = "3. Visuals", Order = 6)]
        public Brush LabelBackgroundColor { get; set; } = Brushes.Black;
        [Browsable(false)] public string LabelBackgroundColorSerializable { get { return Serialize.BrushToString(LabelBackgroundColor); } set { LabelBackgroundColor = Serialize.StringToBrush(value); } }

        #endregion
        // Countdown Properties
        [Display(Name = "Show Countdown", GroupName = "7. Countdown", Order = 1)]
        public bool ShowCountdown { get; set; }

        [Display(Name = "Count Down Mode", GroupName = "7. Countdown", Order = 2)]
        public bool CountDown { get; set; }

        [Display(Name = "Show Percent", GroupName = "7. Countdown", Order = 3)]
        public bool ShowPercent { get; set; }

        [Display(Name = "Font Size", GroupName = "7. Countdown", Order = 4)]
        public int CountdownFontSize { get; set; }

        [Display(Name = "Offset X (Pixels)", GroupName = "7. Countdown", Order = 5)]
        public int CountdownOffsetX { get; set; }

        [Display(Name = "Offset Y (Ticks)", GroupName = "7. Countdown", Order = 6)]
        public int CountdownOffsetY { get; set; }

        [XmlIgnore]
        [Display(Name = "Text Color", GroupName = "7. Countdown", Order = 7)]
        public Brush CountdownTextColor { get; set; }
        [Browsable(false)]
        public string CountdownTextColorSerializable
        {
            get { return Serialize.BrushToString(CountdownTextColor); }
            set { CountdownTextColor = Serialize.StringToBrush(value); }
        }
        
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
		public RelativeIndicators.RelativeVwap RelativeVwap(bool useExchangeTime, bool showDaysAgo, float historicalVWAPThickness, bool showLabels, bool useSimpleLabels, int signalIconOffsetTicks, int signalTextOffsetTicks, bool extendLinesUntilTouch, int maxHistoryDays, bool showDebugLabels, bool enableAlerts, string alertSound, TradeDirectionMode tradeDirection)
		{
			return RelativeVwap(Input, useExchangeTime, showDaysAgo, historicalVWAPThickness, showLabels, useSimpleLabels, signalIconOffsetTicks, signalTextOffsetTicks, extendLinesUntilTouch, maxHistoryDays, showDebugLabels, enableAlerts, alertSound, tradeDirection);
		}

		public RelativeIndicators.RelativeVwap RelativeVwap(ISeries<double> input, bool useExchangeTime, bool showDaysAgo, float historicalVWAPThickness, bool showLabels, bool useSimpleLabels, int signalIconOffsetTicks, int signalTextOffsetTicks, bool extendLinesUntilTouch, int maxHistoryDays, bool showDebugLabels, bool enableAlerts, string alertSound, TradeDirectionMode tradeDirection)
		{
			if (cacheRelativeVwap != null)
				for (int idx = 0; idx < cacheRelativeVwap.Length; idx++)
					if (cacheRelativeVwap[idx] != null && cacheRelativeVwap[idx].UseExchangeTime == useExchangeTime && cacheRelativeVwap[idx].ShowDaysAgo == showDaysAgo && cacheRelativeVwap[idx].HistoricalVWAPThickness == historicalVWAPThickness && cacheRelativeVwap[idx].ShowLabels == showLabels && cacheRelativeVwap[idx].UseSimpleLabels == useSimpleLabels && cacheRelativeVwap[idx].SignalIconOffsetTicks == signalIconOffsetTicks && cacheRelativeVwap[idx].SignalTextOffsetTicks == signalTextOffsetTicks && cacheRelativeVwap[idx].ExtendLinesUntilTouch == extendLinesUntilTouch && cacheRelativeVwap[idx].MaxHistoryDays == maxHistoryDays && cacheRelativeVwap[idx].ShowDebugLabels == showDebugLabels && cacheRelativeVwap[idx].EnableAlerts == enableAlerts && cacheRelativeVwap[idx].AlertSound == alertSound && cacheRelativeVwap[idx].TradeDirection == tradeDirection && cacheRelativeVwap[idx].EqualsInput(input))
						return cacheRelativeVwap[idx];
			return CacheIndicator<RelativeIndicators.RelativeVwap>(new RelativeIndicators.RelativeVwap(){ UseExchangeTime = useExchangeTime, ShowDaysAgo = showDaysAgo, HistoricalVWAPThickness = historicalVWAPThickness, ShowLabels = showLabels, UseSimpleLabels = useSimpleLabels, SignalIconOffsetTicks = signalIconOffsetTicks, SignalTextOffsetTicks = signalTextOffsetTicks, ExtendLinesUntilTouch = extendLinesUntilTouch, MaxHistoryDays = maxHistoryDays, ShowDebugLabels = showDebugLabels, EnableAlerts = enableAlerts, AlertSound = alertSound, TradeDirection = tradeDirection }, input, ref cacheRelativeVwap);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeVwap RelativeVwap(bool useExchangeTime, bool showDaysAgo, float historicalVWAPThickness, bool showLabels, bool useSimpleLabels, int signalIconOffsetTicks, int signalTextOffsetTicks, bool extendLinesUntilTouch, int maxHistoryDays, bool showDebugLabels, bool enableAlerts, string alertSound, TradeDirectionMode tradeDirection)
		{
			return indicator.RelativeVwap(Input, useExchangeTime, showDaysAgo, historicalVWAPThickness, showLabels, useSimpleLabels, signalIconOffsetTicks, signalTextOffsetTicks, extendLinesUntilTouch, maxHistoryDays, showDebugLabels, enableAlerts, alertSound, tradeDirection);
		}

		public Indicators.RelativeIndicators.RelativeVwap RelativeVwap(ISeries<double> input , bool useExchangeTime, bool showDaysAgo, float historicalVWAPThickness, bool showLabels, bool useSimpleLabels, int signalIconOffsetTicks, int signalTextOffsetTicks, bool extendLinesUntilTouch, int maxHistoryDays, bool showDebugLabels, bool enableAlerts, string alertSound, TradeDirectionMode tradeDirection)
		{
			return indicator.RelativeVwap(input, useExchangeTime, showDaysAgo, historicalVWAPThickness, showLabels, useSimpleLabels, signalIconOffsetTicks, signalTextOffsetTicks, extendLinesUntilTouch, maxHistoryDays, showDebugLabels, enableAlerts, alertSound, tradeDirection);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeVwap RelativeVwap(bool useExchangeTime, bool showDaysAgo, float historicalVWAPThickness, bool showLabels, bool useSimpleLabels, int signalIconOffsetTicks, int signalTextOffsetTicks, bool extendLinesUntilTouch, int maxHistoryDays, bool showDebugLabels, bool enableAlerts, string alertSound, TradeDirectionMode tradeDirection)
		{
			return indicator.RelativeVwap(Input, useExchangeTime, showDaysAgo, historicalVWAPThickness, showLabels, useSimpleLabels, signalIconOffsetTicks, signalTextOffsetTicks, extendLinesUntilTouch, maxHistoryDays, showDebugLabels, enableAlerts, alertSound, tradeDirection);
		}

		public Indicators.RelativeIndicators.RelativeVwap RelativeVwap(ISeries<double> input , bool useExchangeTime, bool showDaysAgo, float historicalVWAPThickness, bool showLabels, bool useSimpleLabels, int signalIconOffsetTicks, int signalTextOffsetTicks, bool extendLinesUntilTouch, int maxHistoryDays, bool showDebugLabels, bool enableAlerts, string alertSound, TradeDirectionMode tradeDirection)
		{
			return indicator.RelativeVwap(input, useExchangeTime, showDaysAgo, historicalVWAPThickness, showLabels, useSimpleLabels, signalIconOffsetTicks, signalTextOffsetTicks, extendLinesUntilTouch, maxHistoryDays, showDebugLabels, enableAlerts, alertSound, tradeDirection);
		}
	}
}

#endregion
