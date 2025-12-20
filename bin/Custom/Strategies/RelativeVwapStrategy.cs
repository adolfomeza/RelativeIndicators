// 
// Copyright (C) 2024, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
// VERIFICACION_GEMINI: 2024-12-19 20:31 - SI VES ESTO, ES EL ARCHIVO CORRECTO
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
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators.RelativeIndicators;
using System.Windows.Controls; // Needed for Button
using System.Windows.Media;    // Needed for Colors
using System.Windows;          // Needed for RoutedEventArgs
using System.IO;               // V_CSV: Added for file writing

// V_SYNC: Simplify Type Access
using SessionLevelInfo = NinjaTrader.NinjaScript.Indicators.RelativeIndicators.RelativeVwap.SessionLevelInfo;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public enum RelativeVwapTradeModeBacktest { Both, LongOnly, ShortOnly }
    public enum RiskManagementModeBacktest { FixedContracts, RiskCalculated }

    public class RelativeVwapStrategy : Strategy, ICustomTypeDescriptor
    {
        #region Internal Structures
        // V_SYNC: Removed local SessionLevelInfo class
        // We now use NinjaTrader.NinjaScript.Indicators.RelativeIndicators.RelativeVwap.SessionLevelInfo directly.
        #endregion

        #region Variables
        // UI Button
        private System.Windows.Controls.StackPanel myPanel;
        private System.Windows.Controls.Button myButton;
        private System.Windows.Controls.Button modeButton;
        private System.Windows.Controls.TextBlock pnlText; // PnL Display
        private System.Windows.Controls.TextBlock refText; // V_REF: Last Setup Display
        private bool IsTradingEnabled = true;
        private bool _hasReportedState = false; // V19: Log Control

        // V_SYNC: Removed local lists (asiaSessions, etc.)
        // We access _vwapIndicator.AsiaSessions directly.
        
        private DateTime asiaStart, asiaEnd, europeStart, europeEnd, usStart, usEnd;

        // Daily Anchors
        private double currentDayHigh = double.MinValue;
        private double currentDayLow = double.MaxValue;
        private int sessionHighBarIdx = -1;
        private int sessionLowBarIdx = -1;
// private bool tp1Hit = false; // Moved to Trade Management section
        private bool tp1Hit = false; // V_FIX: Restored missing variable
// private bool isTrailingFrozen = false; // Replaced by useLegacyTrailing
        
        // Snapshots State (REPLACED by Tick Accumulators)
        private double sessionHighPV;
        private double sessionHighVol; // Using double for flexibility, though Volume is usually long/double
        private double sessionLowPV;
        private double sessionLowVol;
        private double _lastVol; // For Tick tracking

        
        // Detachment State
        private bool highDetached, lowDetached;

        // Live VWAP
        private double liveHighVWAP, liveLowVWAP;
        private bool hasHighVWAP, hasLowVWAP;
        
        // V_CSV: Export Variables
        private string csvPath;
        private string jsPath; // V_AUTO: Path for JS data file
        private string lastEntrySetup = "Unknown"; // Stores "USA High 5 Days"
        private DateTime tradeEntryTime; // Stores precise entry time for PnL log
        private bool exportChartData = true; // V_AUTO: Internal flag (controlled by property)

        // Logic Flags
        private bool highHasTakenRelevant, lowHasTakenRelevant;
        private SessionLevelInfo lastUnlockedHighSession = null;
        private SessionLevelInfo lastUnlockedLowSession = null;
        private bool highSignalFired, lowSignalFired; 

        // Trade Management
        private double tradeEntryPrice;
        private double tradeSL;
        private double tradeTP1;
        private double tradeTP2;
        // Duplicate tp1Hit removed
        
        // V51: GHOST VWAP STATE (Legacy Trailing)
        private double legacyHighPV, legacyHighVol, legacyLowPV, legacyLowVol;
        private double legacyHighVWAP, legacyLowVWAP;
        private double lastLegacyHighVWAP, lastLegacyLowVWAP; // For drawing segments
        private bool useLegacyTrailing = false; // Replaces isTrailingFrozenLogic
        // private bool isTrailingFrozen = false; // Deprecated
        
        // V37: Dynamic TP Flags
        private bool tp1IsVwap, tp2IsVwap;
        
        // V21: Realtime Barrier Flag
        private bool _isFirstRealtimeDiff = true;
        
        // V22: Safety Cooldown Timer
        private DateTime _canTradeTime;
        
        // Visual Indicator
        private RelativeVwap _vwapIndicator;
        
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "RelativeVwap (Unified V2): Enhanced Strategy with robust logging and account filtering.";
                Name = "RelativeVwapStrategy";
                Calculate = Calculate.OnEachTick; 
                IsAdoptAccountPositionAware = true; // REQUIRED for Resilience
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false; // We control this manually now
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = true;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                StartBehavior = StartBehavior.AdoptAccountPosition; // Correct place: SetDefaults
                TimeInForce = TimeInForce.Gtc; 
                // CancelEntriesOnStrategyDisable = false; // UNAVAILABLE IN THIS VERSION
                // CancelExitsOnStrategyDisable = false;   // UNAVAILABLE IN THIS VERSION
                TimeInForce = TimeInForce.Gtc; // Changed from Day to Gtc to survive session close
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors; // V46: NUCLEAR OPTION to prevent Strategy Disable
                // RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose; 
                
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;

                Contracts = 1;
                RiskPercent = 2.0;


                // State
                // State
                useLegacyTrailing = false;
                // isTrailingFrozen = false; // Default Min Contracts (fallback)
                TradeDirection = RelativeVwapTradeModeBacktest.Both;
                RiskMethod = RiskManagementModeBacktest.FixedContracts; // Default to "Old Behavior" (User Request)
                // ShowVisualIndicator = false; // Override removed to allow Property Initializer (True)

                
                // Session settings now handled by Property Initializers (Bottom of file)
                // AsiaStartTime, ShowAsia, etc. are set in properties.
                
                ShowLiveVWAP = false; // Default OFF as requested
                ShowOrderLines = false; // Default OFF as requested

                // UseExchangeTime = false; // V_BACKTEST: Disabled to match Chart Time exactly (Now handled by Property)
                
                // Add Plots for VWAP (Standard "Gray" Curves)
                AddPlot(new Stroke(Brushes.Gray, 1), PlotStyle.Line, "HighVWAP");
                AddPlot(new Stroke(Brushes.Gray, 1), PlotStyle.Line, "LowVWAP");

            }
            else if (State == State.Configure)
            {
                // V38: Init new flags (Removed local declaration)
                tp1IsVwap = false; tp2IsVwap = false;
                // Dynamic Session Close Logic
                IsExitOnSessionCloseStrategy = CloseOnSessionEnd;
                Print("RelativeVwapStrategy Config: CloseOnSessionEnd=" + CloseOnSessionEnd + " -> IsExitOnSessionCloseStrategy=" + IsExitOnSessionCloseStrategy);
                // Note: Time parsing is now dynamic in UpdateSession if UseExchangeTime is true.
                DateTime.TryParse(SessionAsiaStart, out asiaStart);
                DateTime.TryParse(SessionAsiaEnd, out asiaEnd);
                DateTime.TryParse(SessionUSEnd, out usEnd);
                ResetGlobal(true); // Full Reset on Config
                
                // TEST: Instantiate Here for Event Hookup
                if (EnableVisualIndicator && _vwapIndicator == null)
                {
                    Print("RelativeVwapStrategy: Attempting to add Visual Indicator (Configure)...");
                    _vwapIndicator = RelativeVwap(
                        UseExchangeTime,            // useExchangeTime
                        ShowDaysAgo,                // showDaysAgo
                        1.0f,                       // historicalVWAPThickness
                        false,                      // showLabels (Strategy handles collision-aware labels)
                        false,                      // useSimpleLabels
                        15,                         // signalIconOffsetTicks
                        30,                         // signalTextOffsetTicks
                        ExtendLinesUntilTouch,      // extendLinesUntilTouch
                        MaxHistoryDays,             // maxHistoryDays
                        ShowDebugLabels,            // showDebugLabels
                        false,                      // enableAlerts
                        "",                         // alertSound
                        (NinjaTrader.NinjaScript.Indicators.RelativeIndicators.TradeDirectionMode)TradeDirection // tradeDirection
                        );
                    
                    // Manually set Session Times to match Strategy
                    _vwapIndicator.AsiaStartTime = SessionAsiaStart;
                    _vwapIndicator.AsiaEndTime = SessionAsiaEnd;
                    _vwapIndicator.ShowAsia = SessionShowAsia;
                    _vwapIndicator.AsiaLineColor = SessionAsiaColor;
                    
                    _vwapIndicator.EuropeStartTime = SessionEuropeStart;
                    _vwapIndicator.EuropeEndTime = SessionEuropeEnd;
                    _vwapIndicator.ShowEurope = SessionShowEurope;
                    _vwapIndicator.EuropeLineColor = SessionEuropeColor;
                    
                    _vwapIndicator.USStartTime = SessionUSStart;
                    _vwapIndicator.USEndTime = SessionUSEnd;
                    _vwapIndicator.ShowUS = SessionShowUS;
                    _vwapIndicator.USLineColor = SessionUSColor;
                    
                    _vwapIndicator.HighVWAPColor = HighVWAPColor;
                    _vwapIndicator.LowVWAPColor = LowVWAPColor;
                    _vwapIndicator.LabelBackgroundColor = LabelBackgroundColor;
                    // _vwapIndicator.TradeLabelOpacity = TradeLabelOpacity; // Property removed from Indicator
                    
                    // Countdown Sync
                    _vwapIndicator.ShowCountdown = ShowCountdown;
                    _vwapIndicator.CountDown = CountDown;
                    _vwapIndicator.ShowPercent = ShowPercent;
                    
                    AddChartIndicator(_vwapIndicator);
                }
            }
            else if (State == State.DataLoaded)
            {
                // V_CSV: Initialize File
                // V_CSV: Initialize File based on Mode
                string fileName = "live_log.csv";
                if (!LiveTradingMode)
                {
                     // V_MULTI: Unique filename per instrument for Backtests
                     string safeName = Instrument.FullName.Replace(" ", "_").Replace("/", "-");
                     fileName = "backtest_log_" + safeName + ".csv";
                }
                
                string folder = LiveTradingMode ? "LiveTradeLogs" : ""; // Separate folder for Live
                string dirPath = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, folder);
                if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);
                
                csvPath = Path.Combine(dirPath, fileName);
                
                // Backtest JS path remains same for Analyzer
                jsPath = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "TradeAnalyzer", "backtest_data.js"); 
                // For simplicity, let's keep JS overwriting for now as it's the specific "Current View"
                
                if (LiveTradingMode)
                {
                    // LIVE MODE: Locked Append
                    lock (fileLock) 
                    {
                        if (!File.Exists(csvPath))
                        {
                             try {
                                File.WriteAllText(csvPath, "ID,Instrument,EntryTime,Type,EntryPrice,ExitTime,ExitPrice,Result,PnL,SetupName,MAE,MFE,Account\n");
                             } catch {}
                        }
                    }
                }
                else
                {
                }

                lock (fileLock)
                {
                    try {
                        string dir = Path.GetDirectoryName(jsPath);
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        // For JS, we just overwrite it once if not Live
                        if (!LiveTradingMode && !IsFileReset) // Actually logic is tricky for JS if separate lock. Reuse logic.
                        {
                             File.WriteAllText(jsPath, "window.RTA_DATA = [];\n");
                        }
                    } catch {}
                }


            }
            else if (State == State.Terminated)
            {
                Print("RelativeVwapStrategy: State=Terminated. Strategy is shutting down or reloading.");
            }
            else if (State == State.Historical)
            {
                if (ChartControl == null) return; // FIX: Prevent crash in Strategy Analyzer
                if (UserControlCollection.Contains(myPanel)) return;
                
                ChartControl.Dispatcher.InvokeAsync(() =>
                {
                    // Main Container (Vertical Stack) - Bottom Right
                    myPanel = new System.Windows.Controls.StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(5, 0, 0, 25) // Moved to Bottom Left
                    };
                    
                    // PnL Text (Top Row in Vertical Stack)
                    pnlText = new System.Windows.Controls.TextBlock
                    {
                        Text = "PnL: $0.00",
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold,
                        FontSize = 11, // Smaller Font
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left, // Left Align
                        Margin = new Thickness(0,0,0,2) // Little gap below
                    };

                    // V_REF: Ref Information Text (Below PnL)
                    refText = new System.Windows.Controls.TextBlock
                    {
                        Text = "Ref: --",
                        Foreground = Brushes.LightGray,
                        FontWeight = FontWeights.Normal,
                        FontSize = 10,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0,0,0,2) 
                    };

                    // Buttons Container (Horizontal Row)
                    System.Windows.Controls.StackPanel buttonsPanel = new System.Windows.Controls.StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };

                    // Button 1: On/Off
                    myButton = new System.Windows.Controls.Button
                    {
                        Content = "TRADING ON",
                        Foreground = Brushes.White,
                        Background = Brushes.RoyalBlue,
                        Padding = new Thickness(5,2,5,2), // Compact Padding
                        FontSize = 10, // Compact Font
                        Margin = new Thickness(0,0,2,0) // Tight margin
                    };
                    myButton.Click += OnButtonClick;
                    
                    // Button 2: Mode
                    modeButton = new System.Windows.Controls.Button
                    {
                        Content = "MODE: BOTH", 
                        Foreground = Brushes.Black,
                        Background = Brushes.Gray, 
                        Padding = new Thickness(5,2,5,2), // Compact Padding
                        FontSize = 10 // Compact Font
                    };
                    
                    // Set Initial State Logic
                    if (TradeDirection == RelativeVwapTradeModeBacktest.LongOnly) {
                        modeButton.Content = "MODE: LONG"; 
                        modeButton.Background = Brushes.LimeGreen;
                    } else if (TradeDirection == RelativeVwapTradeModeBacktest.ShortOnly) {
                        modeButton.Content = "MODE: SHORT"; 
                        modeButton.Background = Brushes.Red;
                    } else {
                        modeButton.Content = "MODE: BOTH"; 
                        modeButton.Background = Brushes.Gray;
                    }

                    modeButton.Click += OnModeClick;

                    // Button 3: RESET
                    System.Windows.Controls.Button resetButton = new System.Windows.Controls.Button
                    {
                        Content = "RESET", 
                        Foreground = Brushes.White,
                        Background = Brushes.Crimson, 
                        Padding = new Thickness(5,2,5,2), // Compact Padding
                        FontSize = 10, // Compact Font
                        Margin = new Thickness(2,0,0,0) // Tight margin
                    };
                    resetButton.Click += (sender, e) => {
                         // FULL CYCLE RESET: Call ResetGlobal to clear ALL flags (HasTaken, Detached, Fired)
                         // This forces the strategy to "hunt" for a new level touch and new detachment.
                         ResetGlobal(true); // Explicit User Reset -> Full Reset
                         if (ChartControl != null) ChartControl.InvalidateVisual();
                    };

                    // Assemble Layout
                    // 1. Buttons into horizontal panel
                    buttonsPanel.Children.Add(myButton);
                    buttonsPanel.Children.Add(modeButton);
                    buttonsPanel.Children.Add(resetButton);

                    // 2. PnL + Buttons Panel into Main Vertical Panel
                    // 2. PnL + Buttons Panel into Main Vertical Panel
                    myPanel.Children.Add(pnlText);
                    myPanel.Children.Add(refText); // Add Ref Text
                    myPanel.Children.Add(buttonsPanel);
                    
                    UserControlCollection.Add(myPanel);
                    
                    // Initial Update
                    UpdatePnL();
                });
            }
            else if (State == State.Realtime)
            {
               // V_FIX: Restore Traded State from CSV to survive resets
               RestoreTradedState();

               // V_DEBUG: Log Termination for Diagnosis
               // ... existing code ...
            }
            else if (State == State.Terminated)
            {
                if (ChartControl != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() =>
                    {
                        if (myPanel != null)
                        {
                            if (myButton != null) { myButton.Click -= OnButtonClick; myButton = null; }
                            if (modeButton != null) { modeButton.Click -= OnModeClick; modeButton = null; }
                            UserControlCollection.Remove(myPanel);
                            myPanel = null;
                        }
                    });
                }
            }
        }

        private void UpdatePnL()
        {
            if (pnlText == null) return;
            
            // Capture Time from Strategy Context (safe here) to support Playback/Backtest
            DateTime strategyNow = (Bars != null && CurrentBar >= 0) ? Time[0] : DateTime.Now;

            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                // Calculate Daily PnL based on TRADING DAY (Starts at 19:00 of previous day)
                double dailyPnL = 0;
                DateTime now = strategyNow;
                
                // Determine the Start of the current "Trading Day"
                // Logic: If current hour >= 19, Start is Today 19:00. Else, Start is Yesterday 19:00.
                
                DateTime sessionStart;
                if (now.Hour >= 19)
                    sessionStart = now.Date.AddHours(19);
                else
                    sessionStart = now.Date.AddDays(-1).AddHours(19);
                
                // Iterate ALL trades (SystemPerformance contains history)
                foreach (Trade t in SystemPerformance.AllTrades)
                {
                    // Filter: Trade must have EXITED after the session start
                    if (t.Exit.Time >= sessionStart)
                    {
                        dailyPnL += t.ProfitCurrency;
                    }
                }

                pnlText.Text = "Session PnL: " + dailyPnL.ToString("C");
                
                if (dailyPnL >= 0) pnlText.Foreground = Brushes.LimeGreen;
                else pnlText.Foreground = Brushes.RoyalBlue;
                
                // V_REF: Update Last Setup Info
                if (refText != null) refText.Text = "Ref: " + lastEntrySetup;
            });
        }
        


        private void OnButtonClick(object sender, RoutedEventArgs e)
        {
            IsTradingEnabled = !IsTradingEnabled;
            if (IsTradingEnabled)
            {
                myButton.Content = "TRADING ON";
                myButton.Background = Brushes.RoyalBlue;
                
                // V27: RESET ON ENABLE
                // If the user enables the strategy, we must treat it as a fresh start.
                // Otherwise, the strategy might have "armed" itself while the user was watching with "Trading Off".
                ResetGlobal(false); // V31: Keep Anchors logic
                
                // V_FIX: Context-Aware Reset (Pass Current Price)
                double resetPrice = (Bars != null && CurrentBar >= 0) ? Close[0] : 0;
                
                if (_vwapIndicator != null)
                {
                    ResetSessionDeep(_vwapIndicator.AsiaSessions, resetPrice);
                    ResetSessionDeep(_vwapIndicator.EuropeSessions, resetPrice);
                    ResetSessionDeep(_vwapIndicator.USSessions, resetPrice);
                }
                
                _canTradeTime = DateTime.Now.AddSeconds(5); // 5s Buffer for safety
                Print("RELATIVE VWAP STRATEGY: Trading ENABLED. State Reset. Waiting 5s...");
            }
            else
            {
                myButton.Content = "TRADING OFF";
                myButton.Background = Brushes.Gray;
                Print("RELATIVE VWAP STRATEGY: Trading DISABLED.");
            }
        }
        
        private void OnModeClick(object sender, RoutedEventArgs e)
        {
            // Cycle: Both -> LongOnly -> ShortOnly -> Both
            if (TradeDirection == RelativeVwapTradeModeBacktest.Both) TradeDirection = RelativeVwapTradeModeBacktest.LongOnly;
            else if (TradeDirection == RelativeVwapTradeModeBacktest.LongOnly) TradeDirection = RelativeVwapTradeModeBacktest.ShortOnly;
            else TradeDirection = RelativeVwapTradeModeBacktest.Both;
            
            // Update UI
            switch (TradeDirection)
            {
                case RelativeVwapTradeModeBacktest.Both:
                    modeButton.Content = "MODE: BOTH"; 
                    modeButton.Background = Brushes.Gray;
                    break;
                case RelativeVwapTradeModeBacktest.LongOnly:
                    modeButton.Content = "MODE: LONG"; 
                    modeButton.Background = Brushes.LimeGreen;
                    break;
                case RelativeVwapTradeModeBacktest.ShortOnly:
                    modeButton.Content = "MODE: SHORT"; 
                    modeButton.Background = Brushes.Red;
                    break;
            }
            
            // Optional: Update Visual Indicator too if it supports live updates
            if (_vwapIndicator != null) 
            {
                 // Need to cast correctly
                 _vwapIndicator.TradeDirection = (NinjaTrader.NinjaScript.Indicators.RelativeIndicators.TradeDirectionMode)TradeDirection; 
                 ChartControl.InvalidateVisual();
            }
        }

        protected override void OnBarUpdate()
        {
            if (Bars == null) return;
            
            // Force Indicator Update Cycle (Required for Visuals)
            if (_vwapIndicator != null) { double trigger = _vwapIndicator.Values[0][0]; }

            try
            {
                // V19: LOG RESTORED STATE (ONCE)
            if (State == State.Realtime && !_hasReportedState)
            {
                _hasReportedState = true;
                Print("========================================");
                Print("RELATIVE VWAP STRATEGY: STATE RESTORED");
                if (_vwapIndicator != null && _vwapIndicator.AsiaSessions != null && _vwapIndicator.AsiaSessions.Count > 0) {
                    var last = _vwapIndicator.AsiaSessions.Last();
                    Print(string.Format("ASIA: Active={0} HighCount={1} LowCount={2}", last.IsActive, last.HighTradeCount, last.LowTradeCount));
                }
                if (_vwapIndicator != null && _vwapIndicator.EuropeSessions != null && _vwapIndicator.EuropeSessions.Count > 0) {
                    var last = _vwapIndicator.EuropeSessions.Last();
                    Print(string.Format("EUROPE: Active={0} HighCount={1} LowCount={2}", last.IsActive, last.HighTradeCount, last.LowTradeCount));
                }
                if (_vwapIndicator != null && _vwapIndicator.USSessions != null && _vwapIndicator.USSessions.Count > 0) {
                     var last = _vwapIndicator.USSessions.Last();
                     Print(string.Format("USA: Active={0} HighCount={1} LowCount={2}", last.IsActive, last.HighTradeCount, last.LowTradeCount));
                }
                
                // V20: RESET STALE SIGNALS ON RELOAD
                // Prevent "Delayed" entries from historical bars processing just before Realtime
                highDetached = false; 
                lowDetached = false;
                
                // V22: FULL WIPEOUT & SAFETY COOLDOWN (The "Nuclear Option")
                // We wipe ALL signal state to force the strategy to 're-watch' the market.
                // We also impose a 10s Cooldown where NO orders can be submitted.
                
                ResetGlobal(false); // V28/V31: EXPLICIT CALL (False = Keep Anchors, preventing VWAP reset)
                
                highHasTakenRelevant = false; 
                lowHasTakenRelevant = false;
                
                _canTradeTime = DateTime.Now.AddSeconds(10); 
                
                if (Position.MarketPosition == MarketPosition.Flat) {
                    highSignalFired = false;
                    lowSignalFired = false;
                }

                // V24: FORCE FRESH TOUCH (THE "GHOST BUSTERS" FIX)
                // The 'CheckTouches' logic persists the "Taken" state if BrokenBarIdx != -1.
                // We MUST reset this index to -1 for the active session so the strategy demands a NEW touch.
                double resetPrice = (Bars != null && CurrentBar >= 0) ? Close[0] : 0;
                if (_vwapIndicator != null)
                {
                    ResetSessionDeep(_vwapIndicator.AsiaSessions, resetPrice);
                    ResetSessionDeep(_vwapIndicator.EuropeSessions, resetPrice);
                    ResetSessionDeep(_vwapIndicator.USSessions, resetPrice);
                }
                
                Print(string.Format("RELATIVE VWAP STRATEGY: Stale signals wiped. Safety Cooldown active until {0:HH:mm:ss} (10s).", _canTradeTime));
                Print("Current State: " + State); // Debug
                Print("========================================");
            }
            
            // V15: RESURRECTION LOGIC (Safety Check on First Tick available)
            // We run this BEFORE the BarsRequired check to ensure immediate protection of existing positions.
            // Removed State check to ensure it triggers on reload even if first tick is historical/transition.
            // Reliability provided by Quantity > 0 check.
            if (Position.MarketPosition != MarketPosition.Flat && Position.Quantity > 0 && tradeSL == 0)
            {
                // Case: Strategy restarted while in a trade. Variables (tradeSL, etc) are lost (0).
                // Action: RESTORE Protection immediately.
                
                // Recover Entry Price (Approximate from Position)
                tradeEntryPrice = Position.AveragePrice;
                bool isLong = (Position.MarketPosition == MarketPosition.Long);
                
                // 1. RECALCULATE SL (Safety Fallback)
                // Need valid session indices? If not available yet (due to BarsRequired), use emergency pips.
                double fallbackSL = isLong ? tradeEntryPrice - 100 * TickSize : tradeEntryPrice + 100 * TickSize;
                
                if (isLong) {
                    if (sessionLowBarIdx != -1 && currentDayLow != double.MaxValue) tradeSL = currentDayLow - TickSize;
                    else tradeSL = fallbackSL;
                } else {
                    if (sessionHighBarIdx != -1 && currentDayHigh != double.MinValue) tradeSL = currentDayHigh + TickSize;
                    else tradeSL = fallbackSL;
                }
                
                // 2. RECALCULATE TPs
                tradeTP1 = 0; 
                tradeTP2 = 0; 
                
                // Restore Flags
                if (isLong) { lowSignalFired = true; } else { highSignalFired = true; }
                
                Print("CRITICAL: Strategy Resurrected! Restored SL protection at " + tradeSL);
                
                // Force an immediate exit supervision call
                ManagePosition(); 
            }

            // Standard Check
            if (CurrentBar < BarsRequiredToTrade) return;

            // 1. DATA SNAPSHOT (Tick cleanup)
            if (IsFirstTickOfBar)
            {
                 // V_REPLAY: Log OHLC Data for Web Replay
                 if (ExportChartData) LogMarketData();

                 _lastVol = 0; // Reset tick volume tracker at start of new bar
                
                // (Resurrection Logic moved to top of method)


                if (Bars.IsFirstBarOfSession)
                {
                     // Close Ghost Lines from previous session context
                     int endIdx = CurrentBar - 1;
                     if (endIdx >= 0 && _vwapIndicator != null) {
                         if (_vwapIndicator.AsiaSessions != null) CloseGhostLines(_vwapIndicator.AsiaSessions, endIdx);
                         if (_vwapIndicator.EuropeSessions != null) CloseGhostLines(_vwapIndicator.EuropeSessions, endIdx);
                         if (_vwapIndicator.USSessions != null) CloseGhostLines(_vwapIndicator.USSessions, endIdx);
                     }
                     ResetGlobal(true); // Full Reset (New Session)
                }
                
                // V21: PREVENT STALE DETACHMENT ON RELOAD
                // We skip the very first bar boundary check in Realtime to ensure we don't process a historical bar
                // that effectively "just closed" from the strategy's perspective but is old in reality.
                bool skipDetachmentCheck = false;
                if (State == State.Realtime) {
                     if (_isFirstRealtimeDiff) {
                         _isFirstRealtimeDiff = false;
                         skipDetachmentCheck = true;
                         Print("RELATIVE VWAP STRATEGY: Skipping first Realtime bar boundary to avoid stale signals.");
                     }
                }

                // V12: BAR CLOSE DETACHMENT CHECK WITH BUFFER
                if (!skipDetachmentCheck)
                {
                    // Use the CALCULATED valid VWAP from the plot (Value at close of previous bar)
                    // Note: Values[0][1] is previous bar's final VWAP
                    if (sessionHighBarIdx != -1 && Values[0].IsValidDataPointAt(CurrentBar - 1)) {
                         double closedVWAP = Values[0][1];
                         // V30: STRICT SEQUENCE (Taken -> Detached)
                         if (highHasTakenRelevant && High[1] <= closedVWAP - TickSize) highDetached = true;
                    }
                    if (sessionLowBarIdx != -1 && Values[1].IsValidDataPointAt(CurrentBar - 1)) {
                         double closedVWAP = Values[1][1];
                         // V30: STRICT SEQUENCE (Taken -> Detached)
                         if (lowHasTakenRelevant && Low[1] >= closedVWAP + TickSize) lowDetached = true;
                    }
                }
            }
            
            // V39: DYNAMIC VWAP TRAILING UPDATE
            // Update the TP variables if they are linked to VWAP
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if ((tp1IsVwap && !tp1Hit) || tp2IsVwap) {
                     // Recalculate High VWAP (Target for Long? No, Long targets High Session but... wait.
                     // A Long Trade (Fading Low) targets the HIGH VWAP? or the LOW VWAP Mean Reversion?
                     // STANDARD: Fade Low -> Target High side? Or Mean Reversion to internal?
                     // Let's check OnExecutionUpdate logic.
                     // Long sets TP1 = sessionHighPV / sessionHighVol. Yes, Targets High VWAP.
                     
                     // We need to ensure we have the latest High VWAP values.
                     // They are calculated BELOW in this method (Step 2).
                     // So we should do this update AT THE END of OnBarUpdate or use the previous bar's values?
                     // Verify order: Logic is at Step 3. VWAP Calc is Step 2.
                     // So values ARE updated.
                     
                     // BUT: Logic is BEFORE VWAP Calc in the file structure?
                     // Let's check line numbers. 
                     // VWAP Calc is lines ~580. Entry Logic is ~660.
                     // So Entry Logic has FRESH VWAP.
                     // But this Management Logic (updating TPs) should also happen.
                     
                     // IMPORTANT: We need access to 'liveHighVWAP' which is set in Step 2.
                     // So we must do this updating AFTER Step 2.
                }
            } 
            double high = High[0]; double low = Low[0]; double close = Close[0]; double vol = Volume[0]; DateTime time = Time[0];
            CurrentBarDate = time.Date; // Cache for TimeZone Helper
            
            // Determine Contribution
            double contribPrice = 0;
            double contribVol = 0;

            if (State == State.Historical)
            {
                // BAR-BASED (Fast for History)
                contribPrice = (high + low + close) / 3.0; // Typical Price
                contribVol = vol; // Full Bar Volume
                _lastVol = 0; // Reset tick tracker so Realtime starts fresh
            }
            else
            {
                // TICK-BASED (Precise for Realtime)
                contribPrice = close; // current tick price
                
                // V48: FIX VOLUME ACCUMULATION (Sync with Indicator)
                // We must use the DELTA volume since the last tick, not the full bar volume.
                double tickDelta = vol - _lastVol;
                if (tickDelta < 0) tickDelta = vol; // New Bar
                
                contribVol = tickDelta; 
                _lastVol = vol;

                // V51: UPDATE LEGACY (GHOST) ACCUMULATORS
                if (useLegacyTrailing)
                {
                    // Accumulate to the "Ghost" sessions
                    legacyHighPV += contribPrice * contribVol;
                    legacyHighVol += contribVol;
                    legacyLowPV += contribPrice * contribVol;
                    legacyLowVol += contribVol;
                    
                    // Update Ghost VWAPs
                    double newLegacyHigh = (legacyHighVol > 0) ? legacyHighPV / legacyHighVol : 0;
                    double newLegacyLow = (legacyLowVol > 0) ? legacyLowPV / legacyLowVol : 0;
                    
                    // V52: DRAW GHOST LINE
                    if (CurrentBar > 0)
                    {
                         if (Position.MarketPosition == MarketPosition.Long && lastLegacyHighVWAP > 0 && newLegacyHigh > 0) {
                             Draw.Line(this, "GhostH" + CurrentBar, false, 1, lastLegacyHighVWAP, 0, newLegacyHigh, Brushes.White, DashStyleHelper.Dash, 2);
                         }
                         if (Position.MarketPosition == MarketPosition.Short && lastLegacyLowVWAP > 0 && newLegacyLow > 0) {
                             Draw.Line(this, "GhostL" + CurrentBar, false, 1, lastLegacyLowVWAP, 0, newLegacyLow, Brushes.White, DashStyleHelper.Dash, 2);
                         }
                    }
                    
                    legacyHighVWAP = newLegacyHigh;
                    legacyLowVWAP = newLegacyLow;
                    lastLegacyHighVWAP = legacyHighVWAP;
                    lastLegacyLowVWAP = legacyLowVWAP;
                }
            }

            // Update Anchors
            if (high > currentDayHigh) {
                currentDayHigh = high; sessionHighBarIdx = CurrentBar;
                
                // RESET Accumulators (New Anchor Starts NOW)
                sessionHighPV = contribPrice * contribVol; 
                sessionHighVol = contribVol;
                
                highDetached = false; // Reset Detachment
                highSignalFired = false; // V17: RESET LOCK on New High (Allow new trade attempt)
                // highHasTakenRelevant = false; // V_FIX: DO NOT RESET. If we took a level, we want to REMEMBER it while trending.
                
                // V50: BREAK PLOT LINE on Reset (Hide connecting line)
                PlotBrushes[0][0] = Brushes.Transparent;
            }
            // If not reset, accumulate
            else if (sessionHighBarIdx != -1) {
                sessionHighPV += contribPrice * contribVol;
                sessionHighVol += contribVol;
            }

            if (low < currentDayLow) {
                currentDayLow = low; sessionLowBarIdx = CurrentBar;
                
                // RESET Accumulators
                sessionLowPV = contribPrice * contribVol;
                sessionLowVol = contribVol;
                
                lowDetached = false;
                lowSignalFired = false; // V17: RESET LOCK on New Low
                // lowHasTakenRelevant = false; // V_FIX: DO NOT RESET. Persist Taken state during trend.
                
                // V50: BREAK PLOT LINE on Reset
                PlotBrushes[1][0] = Brushes.Transparent;
            }
            // If not reset, accumulate
            else if (sessionLowBarIdx != -1) {
                sessionLowPV += contribPrice * contribVol;
                sessionLowVol += contribVol;
            }
            
            // V_SYNC: REMOVED Duplicate Session Calculation
            // The Strategy now relies on the Indicator's lists (which contain full history)
            // to avoid "Amensia" on Reset.
            // UpdateSession(asiaSessions, "Asia", time, SessionAsiaStart, SessionAsiaEnd, SessionShowAsia, high, low);
            // UpdateSession(europeSessions, "Europe", time, SessionEuropeStart, SessionEuropeEnd, SessionShowEurope, high, low);
            // UpdateSession(usSessions, "USA", time, SessionUSStart, SessionUSEnd, SessionShowUS, high, low);
            
            // V_SYNC: Use Indicator Lists for Logic
            // Ensure we are checking the lists that effectively "Know the Past"
            // Note: We need to reference them dynamically or change the variable types?
            // Current 'asiaSessions' is a local list.
            // We should use _vwapIndicator.AsiaSessions directly in CheckTouches
            
            if (_vwapIndicator != null)
            {
                CheckTouches(_vwapIndicator.AsiaSessions, high, low);
                CheckTouches(_vwapIndicator.EuropeSessions, high, low);
                CheckTouches(_vwapIndicator.USSessions, high, low);
            }
            
            // Set Plot Values
            hasHighVWAP = false; liveHighVWAP = 0;
            if (sessionHighBarIdx != -1 && sessionHighVol > 0) {
                liveHighVWAP = sessionHighPV / sessionHighVol;
                hasHighVWAP = true;
                // V57: ALWAYS populate Values for logic continuity (Detachment checks depend on it)
                Values[0][0] = liveHighVWAP;
                
                if (!ShowLiveVWAP) PlotBrushes[0][0] = Brushes.Transparent; // Hide visual if requested
            }
            else {
                Values[0].Reset();
            }
            
            hasLowVWAP = false; liveLowVWAP = 0;
            if (sessionLowBarIdx != -1 && sessionLowVol > 0) {
                liveLowVWAP = sessionLowPV / sessionLowVol;
                hasLowVWAP = true;
                // V57: ALWAYS populate Values
                Values[1][0] = liveLowVWAP;
                
                if (!ShowLiveVWAP) PlotBrushes[1][0] = Brushes.Transparent;
            }
            else {
                Values[1].Reset();
            }

            // V_FIX: Explicit Detachment Logic (Wait for Price to Separate from VWAP)
            // V_SEQ: Enforce Sequence -> MUST have Taken a Level BEFORE Detaching.
            // Prevents "Premature Detachment" at session start where price spawns slightly away from VWAP.
            if (hasHighVWAP && sessionHighBarIdx != -1) {
                 if (highHasTakenRelevant && Close[0] < liveHighVWAP - (2 * TickSize)) highDetached = true; 
            }
            if (hasLowVWAP && sessionLowBarIdx != -1) {
                if (lowHasTakenRelevant && Close[0] > liveLowVWAP + (2 * TickSize)) lowDetached = true;
            }

            // V39: DYNAMIC VWAP TRAILING UPDATE (Placed AFTER VWAP Calc)
            // V39: DYNAMIC VWAP TRAILING UPDATE (Placed AFTER VWAP Calc)
            // V51: Use Legacy Logic if active (Ghost Trailing) OR Live Logic if not frozen
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                double targetHighVWAP = useLegacyTrailing ? legacyHighVWAP : (hasHighVWAP ? liveHighVWAP : 0);
                double targetLowVWAP = useLegacyTrailing ? legacyLowVWAP : (hasLowVWAP ? liveLowVWAP : 0);

                // LONG Position (Targets High)
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    if (targetHighVWAP > 0) {
                       if (tp1IsVwap && !tp1Hit) tradeTP1 = targetHighVWAP;
                       if (tp2IsVwap) tradeTP2 = targetHighVWAP;
                    }
                }
                // SHORT Position (Targets Low)
                else if (Position.MarketPosition == MarketPosition.Short)
                {
                    if (targetLowVWAP > 0) {
                        if (tp1IsVwap && !tp1Hit) tradeTP1 = targetLowVWAP;
                        if (tp2IsVwap) tradeTP2 = targetLowVWAP;
                    }
                }
            }
            
            // V11: No Intra-bar Detachment Update. We strictly wait for Bar Close (above).
            
            // UI Switch: Valid Point to block Trades. Data is updated, but we won't enter.
            if (!IsTradingEnabled) return;

            // V55: GHOST REVERSAL LOGIC
            // User Request: If in a Ghost Trade (Legacy) and a NEW opposite signal appears (Detached/Broken), 
            // CLOSE the Ghost Trade immediately and allow the new entry.
            bool isGhostReversal = false;
            if (State == State.Realtime && useLegacyTrailing && Position.MarketPosition != MarketPosition.Flat)
            {
                 // Case 1: Ghost Long, Short Logic Triggered
                 if (Position.MarketPosition == MarketPosition.Long)
                 {
                     // Check Short Signal basic prereqs (High Detached + Level Broken)
                     // Note: Filters (RunUp, etc) will be checked in the Entry Block.
                     if (highDetached && !highSignalFired && hasHighVWAP && lastUnlockedHighSession != null && lastUnlockedHighSession.HighBrokenBarIdx != -1)
                     {
                         Print("RelativeVwapStrategy: GHOST REVERSAL DETECTED (Long -> Short). Closing Ghost Trade.");
                         ExitLong(); 
                         useLegacyTrailing = false; // Disable Ghost Mode
                         isGhostReversal = true; // Bypass Flat check
                     }
                 }
                 // Case 2: Ghost Short, Long Logic Triggered
                 else if (Position.MarketPosition == MarketPosition.Short)
                 {
                     if (lowDetached && !lowSignalFired && hasLowVWAP && lastUnlockedLowSession != null && lastUnlockedLowSession.LowBrokenBarIdx != -1)
                     {
                         Print("RelativeVwapStrategy: GHOST REVERSAL DETECTED (Short -> Long). Closing Ghost Trade.");
                         ExitShort(); 
                         useLegacyTrailing = false;
                         isGhostReversal = true;
                     }
                 }
            }

            // 3. ENTRY LOGIC (Requires Detachment)
            // Guard: Only trade in Realtime to avoid log errors about "Ignored" historical orders
            // V55: Allow Entry if Flat OR if it's a Ghost Reversal
            if ((State == State.Realtime || State == State.Historical) && (Position.MarketPosition == MarketPosition.Flat || isGhostReversal))
            {
                // V22: Safety Cooldown Check
                if (DateTime.Now < _canTradeTime) return;

                // DEBUG LOGGING (Backtest Only - Every 100 bars or on event)
                if (State == State.Historical && CurrentBar % 100 == 0)
                {
                    Print(string.Format("DEBUG BAR {0}: HighDet={1} HighTaken={2} LowDet={3} LowTaken={4} HighVWAP={5} LowVWAP={6}", 
                        CurrentBar, highDetached, highHasTakenRelevant, lowDetached, lowHasTakenRelevant, hasHighVWAP, hasLowVWAP));
                    
                    if (_vwapIndicator != null && _vwapIndicator.AsiaSessions != null && _vwapIndicator.AsiaSessions.Count > 0) Print("  ASIA Active: " + _vwapIndicator.AsiaSessions.Last().IsActive);
                    if (_vwapIndicator != null && _vwapIndicator.EuropeSessions != null && _vwapIndicator.EuropeSessions.Count > 0) Print("  EURO Active: " + _vwapIndicator.EuropeSessions.Last().IsActive);
                    if (_vwapIndicator != null && _vwapIndicator.USSessions != null && _vwapIndicator.USSessions.Count > 0) Print("  USA Active: " + _vwapIndicator.USSessions.Last().IsActive);
                }

                // SHORT (Fade Resistance)
                if (hasHighVWAP && (TradeDirection == RelativeVwapTradeModeBacktest.Both || RelativeVwapTradeModeBacktest.ShortOnly == TradeDirection))
                {
                    // V9: Restore Detachment Check.
                    // Only Enter if we have been Detached (Price completely below line).
                    if (highDetached && !highSignalFired) 
                    {
                        bool ok = highHasTakenRelevant;

                        
                        // V13: ANTI-BREAKOUT FILTER
                        if (lastUnlockedHighSession != null && lastUnlockedHighSession.HighBrokenBarIdx == CurrentBar) ok = false;
                        
                        // V23: STALENESS / ONE-SHOT CHECK
                        // Should not trade if ALREADY TRADED this level.
                        // V23: COUNT CHECK
                        // Should not trade if Trade Count >= Max (Default 1)
                        if (lastUnlockedHighSession != null && lastUnlockedHighSession.HighTradeCount >= MaxEntriesPerLevel) ok = false;

                        // V38: ANCHOR SYNC FILTER
                        // Prevent entry if VWAP is "Old" (Started BEFORE the break).
                        // We want a FRESH VWAP caused by the liquidity grab (New High).
                        // sessionHighBarIdx = Start of current VWAP.
                        // lastUnlockedHighSession.HighBrokenBarIdx = When level was broken.
                        if (lastUnlockedHighSession != null && sessionHighBarIdx < lastUnlockedHighSession.HighBrokenBarIdx)
                        {
                            // Triggered by a lower high that didn't reset the VWAP. Ignore.
                            // Triggered by a lower high that didn't reset the VWAP. Ignore.
                            ok = false;
                        }



                        if (ok)
                        {
                            tradeSL = 0; // V14: Reset SL to prevent Race Condition
                            
                            // RISK CALCULATION SHORT
                            // SL = currentDayHigh + TickSize. Entry = liveHighVWAP.
                            double plannedSL = currentDayHigh + TickSize;
                            int qty = CalculateRiskQuantity(liveHighVWAP, plannedSL);
                            
                            if (qty > 0)
                            {
                                try 
                                { 
                                    Print(string.Format("DEBUG_ENTRY: Attempting EntryShort at {0} (Qty={1})", liveHighVWAP, qty));
                                    EnterShortLimit(qty, liveHighVWAP, "EntryShort");
                                    highSignalFired = true; // V_FIX: Immediate Lock to prevent Machine Gun entries (Tick #2)
                                    
                                    // V_CSV: Capture Setup Name
                                    int daysAgo = 0;
                                    if (lastUnlockedHighSession != null) 
                                        daysAgo = (Time[0].Date - lastUnlockedHighSession.SessionDate.Date).Days;
                                        
                                    lastEntrySetup = string.Format("{0} High {1} Days", 
                                        lastUnlockedHighSession != null ? lastUnlockedHighSession.Name : "UNK", 
                                        daysAgo); 
                                    
 
                                }
                                catch (Exception ex)
                                {
                                    Print("RelativeVwapStrategy: CRITICAL ENTRY ERROR (Short). Strategy preventing crash. Error: " + ex.Message);
                                    tradeSL = 0; // Reset
                                    highSignalFired = false; // Unlock on error
                                }
                            }
                            else
                            {
                                Print("RelativeVwapStrategy: Entry Short Skipped (Qty=0 or > MaxContracts).");
                                tradeSL = 0; // Reset safety
                                // User Request: Mark as traded if skipped due to contracts to avoid late entry
                                if (lastUnlockedHighSession != null) lastUnlockedHighSession.HighTradeCount++;
                                highSignalFired = true; 
                            }
                            
                            // V19 REMOVED: Do NOT mark as traded here. It kills the working order instantly.
                            // Moved to OnExecutionUpdate.
                        }
                    }
                }
                
                // LONG (Fade Support)
                if (hasLowVWAP && (TradeDirection == RelativeVwapTradeModeBacktest.Both || RelativeVwapTradeModeBacktest.LongOnly == TradeDirection))
                {
                    // V9: Restore Detachment Check.
                    if (lowDetached && !lowSignalFired)
                    {
                        bool ok = lowHasTakenRelevant;

                        
                        // V13: ANTI-BREAKOUT FILTER
                        if (lastUnlockedLowSession != null && lastUnlockedLowSession.LowBrokenBarIdx == CurrentBar) ok = false;
                        
                        // V23: COUNT CHECK
                        if (lastUnlockedLowSession != null && lastUnlockedLowSession.LowTradeCount >= MaxEntriesPerLevel) ok = false;

                        // V38: ANCHOR SYNC FILTER (Low Side)
                        // Prevent entry if VWAP is "Old".
                        if (lastUnlockedLowSession != null && sessionLowBarIdx < lastUnlockedLowSession.LowBrokenBarIdx)
                        {
                            ok = false;
                        }
                        

                        if (ok)
                        {
                             tradeSL = 0; // V14: Reset SL to prevent Race Condition
                             
                             // RISK CALCULATION LONG
                             // SL = currentDayLow - TickSize. Entry = liveLowVWAP.
                             double plannedSL = currentDayLow - TickSize;
                             int qty = CalculateRiskQuantity(liveLowVWAP, plannedSL);
                             
                             if (qty > 0)
                             {
                                 try
                                 {
                                     Print(string.Format("DEBUG_ENTRY: Attempting EntryLong at {0} (Qty={1})", liveLowVWAP, qty));
                                     EnterLongLimit(qty, liveLowVWAP, "EntryLong");
                                     lowSignalFired = true; // V_FIX: Immediate Lock to prevent Machine Gun entries
                                     
                                     // V_CSV: Capture Setup Name
                                     int daysAgo = 0;
                                     if (lastUnlockedLowSession != null) 
                                         daysAgo = (Time[0].Date - lastUnlockedLowSession.SessionDate.Date).Days;

                                     lastEntrySetup = string.Format("{0} Low {1} Days", 
                                         lastUnlockedLowSession != null ? lastUnlockedLowSession.Name : "UNK", 
                                         daysAgo);


                                 }
                                 catch (Exception ex)
                                 {
                                     Print("RelativeVwapStrategy: CRITICAL ENTRY ERROR (Long). Strategy preventing crash. Error: " + ex.Message);
                                     tradeSL = 0; // Reset
                                     lowSignalFired = false; // Unlock on error
                                 }
                             }
                             else
                             {
                                 Print("RelativeVwapStrategy: Entry Long Skipped (Qty=0 or > MaxContracts).");
                                 tradeSL = 0; // Reset safety
                                 // User Request: Mark as traded if skipped due to contracts to avoid late entry
                                 if (lastUnlockedLowSession != null) lastUnlockedLowSession.LowTradeCount++;
                                 lowSignalFired = true;
                             }
                             
                             // V19 REMOVED: Do NOT mark as traded here. It kills the working order instantly.
                             // Moved to OnExecutionUpdate.
                        }
                    }
                }
            }
            
            // V44: Check Session End Protection
            if (State == State.Realtime || State == State.Historical) CheckSessionEndProtection(Time[0]);
            
                ManagePosition();
            }
            catch (Exception ex)
            {
                 // GLOBAL SAFETY NET
                 // Identify the error but DO NOT CRASH the strategy.
                 if (State == State.Realtime)
                    Print("RelativeVwapStrategy CRITICAL ERROR in OnBarUpdate: " + ex.Message + " | Stack: " + ex.StackTrace);
            }
        }
        
        // V44: SAFETY BE AT SESSION END (30s buffer)
        private void CheckSessionEndProtection(DateTime currentTime)
        {
            if (Position.MarketPosition == MarketPosition.Flat) return;
            
            // Define all end times
            // User Request: ONLY US Session (End of Day)
            string[] endTimes = { SessionUSEnd };
            
            foreach (string endStr in endTimes)
            {
                // Parse Time
                DateTime dt;
                if (!DateTime.TryParse(endStr, out dt)) continue;
                
                // Construct target EndTime for TODAY (or relevant session day)
                // We compare TimeOfDay. 
                // Problem: If EndTime is 16:00, and we are at 15:59:30.
                
                TimeSpan endTs = dt.TimeOfDay;
                TimeSpan currentTs = currentTime.TimeOfDay;
                
                // Calculate remaining seconds
                double secondsDiff = (endTs - currentTs).TotalSeconds;
                
                // Handle rollover (e.g. End 02:00, Current 01:59)?
                // Simple logic: If current is 'just before' end.
                // If diff is between 0 and 30 seconds.
                
                if (secondsDiff > 0 && secondsDiff <= 30)
                {
                    // Move to BE if not already there or better
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        if (tradeSL < tradeEntryPrice) 
                        {
                             tradeSL = tradeEntryPrice;
                             Print("RELATIVE VWAP STRATEGY: Session End Protection (30s) -> Moved SL to BE.");
                        }
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        if (tradeSL > tradeEntryPrice) 
                        {
                            tradeSL = tradeEntryPrice;
                            Print("RELATIVE VWAP STRATEGY: Session End Protection (30s) -> Moved SL to BE.");
                        }
                    }
                }
            }
        }
        
        private void ManagePosition()
        {
            // V14: Safety Check. 
            if (tradeSL == 0) return;
            // V_DEBUG: Monitor calls
            // Print(string.Format("DEBUG_MANAGE: Pos={0} SL={1} Close={2}", Position.MarketPosition, tradeSL, Close[0]));

            try
            {
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    // Safety: Check if SL is already violated (Price below OR EQUAL to SL)
                    // Prevents "Sell Stop above market" errors if price gaps down
                    // V_SAFETY: Added Buffer (2 Ticks)
                    if (Close[0] <= tradeSL + (2 * TickSize)) {
                        Print(string.Format("DEBUG_EXIT: Safety Exit Long triggered! Close ({0}) <= SL ({1}) + Buffer", Close[0], tradeSL));
                        Print(string.Format("RelativeVwapStrategy: Safety Exit Long triggered! Close ({0}) <= SL ({1}) + Buffer", Close[0], tradeSL));
                        ExitLong(); // Corrected from ExitLongMarket
                        return;
                    }

                    // Place SL for current quantity
                    // Note: We use "" for entry signal to allow managing ADOPTED positions (Resilience)
                    ExitLongStopMarket(0, true, Position.Quantity, tradeSL, "SL_Long", "");
                    
                    // TP Management
                    if (!tp1Hit && tradeTP1 != 0) {
                        // Risk Management V30: Dynamic Split based on CURRENT position size
                        // Was: Math.Max(1, Contracts/2);
                        int currentQty = Position.Quantity;
                        int qty1 = Math.Max(1, currentQty / 2);
                        
                        ExitLongLimit(0, true, qty1, tradeTP1, "TP1_Long", "");
                        
                        // Remainder to TP2
                        if (tradeTP2 != 0) ExitLongLimit(0, true, currentQty - qty1, tradeTP2, "TP2_Long", "");
                    } else if (tradeTP2 != 0) {
                        ExitLongLimit(0, true, Position.Quantity, tradeTP2, "TP2_Long", "");
                    }
                }
                else if (Position.MarketPosition == MarketPosition.Short)
                {
                    // Safety: Check if SL is already violated (Price above OR EQUAL to SL)
                    // Prevents "Buy Stop below market" errors (Validation Error)
                    // V_SAFETY: Added Buffer (2 Ticks) to prevent "Too Close" rejection or latency crossovers.
                    if (Close[0] >= tradeSL - (2 * TickSize)) {
                        Print(string.Format("RelativeVwapStrategy: Safety Exit Short triggered! Close ({0}) >= SL ({1}) - Buffer", Close[0], tradeSL));
                        ExitShort(); // Corrected from ExitShortMarket
                        return;
                    }

                    // Place SL for current quantity
                    // Note: We use "" for entry signal to allow managing ADOPTED positions (Resilience)
                    ExitShortStopMarket(0, true, Position.Quantity, tradeSL, "SL_Short", "");
                    
                    // TP Management
                    if (!tp1Hit && tradeTP1 != 0) {
                        // Risk Management V30: Dynamic Split based on CURRENT position size
                        // Was: Math.Max(1, Contracts/2);
                        int currentQty = Position.Quantity;
                        int qty1 = Math.Max(1, currentQty / 2);

                        ExitShortLimit(0, true, qty1, tradeTP1, "TP1_Short", "");
                        
                        // Remainder to TP2
                        if (tradeTP2 != 0) ExitShortLimit(0, true, currentQty - qty1, tradeTP2, "TP2_Short", "");
                    } else if (tradeTP2 != 0) {
                        ExitShortLimit(0, true, Position.Quantity, tradeTP2, "TP2_Short", "");
                    }
                }
            }
            catch (Exception ex)
            {
                // V45: IGNORE "Unable to change order" ERROR
                // This specific error causes Strategy Termination. It happens when we try to modify a dead order.
                // By catching it, we allow the strategy to retry on the next tick (sending a new order if needed).
                if (ex.Message.Contains("Unable to change order") || ex.Message.Contains("ignoring"))
                {
                     Print("RelativeVwapStrategy: Handled 'Unable to change order' (Expected during session switch). Retrying next tick.");
                }
                else
                {
                     // Re-throw other errors
                     // throw; // actually, better to just print and keep running if possible?
                     // Verify if re-throwing crashes the strategy. Yes.
                     Print("RelativeVwapStrategy ERROR in ManagePosition: " + ex.Message);
                }
            }
            // V16 CLEANUP REMOVED: User wants to keep ghost lines for study.
            
            // V_VISUAL: Draw Strategy Lines
            // DrawStrategyTradeLines(); // V_FIX: Duplicate removed (Replaced by OnRender Smart Lines)
        }
        


        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order != null && execution.Order.OrderState == OrderState.Filled)
            {
                if (execution.Order.Name == "EntryLong" || execution.Order.Name == "EntryShort")
                {
                    bool isLong = (execution.Order.Name == "EntryLong");
                    tradeEntryPrice = price;
                    tradeEntryTime = time; // V_CSV: Capture Time
                    tp1Hit = false;

                    if (isLong) {
                        lowSignalFired = true; // Lock
                        tradeSL = currentDayLow - TickSize;
                        
                        // V37: DYNAMIC TP SORTING
                        // Calculate both potential targets
                        double targetVWAP = (sessionHighVol > 0) ? sessionHighPV/sessionHighVol : 0;
                        double targetSession = (lastUnlockedLowSession != null) ? lastUnlockedLowSession.High : 0;
                        
                        // 1. Filter Invalid Targets
                        // Must be above Entry + 1 Tick
                        if (targetVWAP <= tradeEntryPrice + TickSize) targetVWAP = 0;
                        if (targetSession <= tradeEntryPrice + TickSize) targetSession = 0;

                        // 2. Sort by Distance
                        // If both valid, pick closest as TP1
                        if (targetVWAP > 0 && targetSession > 0) {
                            if (targetVWAP < targetSession) {
                                tradeTP1 = targetVWAP; tp1IsVwap = true;
                                tradeTP2 = targetSession; tp2IsVwap = false;
                            } else {
                                tradeTP1 = targetSession; tp1IsVwap = false;
                                tradeTP2 = targetVWAP; tp2IsVwap = true;
                            }
                        }
                        // If only one valid
                        else if (targetVWAP > 0) { tradeTP1 = targetVWAP; tp1IsVwap = true; tradeTP2 = 0; tp2IsVwap = false; }
                        else if (targetSession > 0) { tradeTP1 = targetSession; tp1IsVwap = false; tradeTP2 = 0; tp2IsVwap = false; }
                        else { tradeTP1 = 0; tp1IsVwap = false; tradeTP2 = 0; tp2IsVwap = false; }
                        
                        // V33: MARK AS TRADED
                        if (lastUnlockedLowSession != null) lastUnlockedLowSession.LowTradeCount++;
                        
                    } else {
                        highSignalFired = true; // Lock
                        tradeSL = currentDayHigh + TickSize;
                        
                        // V37: DYNAMIC TP SORTING (SHORT)
                        double targetVWAP = (sessionLowVol > 0) ? sessionLowPV/sessionLowVol : 0;
                        double targetSession = (lastUnlockedHighSession != null) ? lastUnlockedHighSession.Low : 0;
                        
                        // 1. Filter Invalid Targets (Must be below Entry - Tick)
                        // Note: targetVWAP > 0 check is implicit for "Existence", but price level must be < Entry
                        if (targetVWAP > 0 && targetVWAP >= tradeEntryPrice - TickSize) targetVWAP = 0; 
                        if (targetSession > 0 && targetSession >= tradeEntryPrice - TickSize) targetSession = 0;

                         // 2. Sort by Distance (Highest Price is Closest for Short? No, Highest Price is Entry. Target < Entry.)
                         // Closer target = Higher Price (Closer to Entry). Farther target = Lower Price.
                         
                        if (targetVWAP > 0 && targetSession > 0) {
                            // Example: Entry 100. VWAP 90. Session 80.
                            // 90 > 80. 90 is closer.
                            if (targetVWAP > targetSession) {
                                tradeTP1 = targetVWAP; tp1IsVwap = true;
                                tradeTP2 = targetSession; tp2IsVwap = false;
                            } else {
                                tradeTP1 = targetSession; tp1IsVwap = false;
                                tradeTP2 = targetVWAP; tp2IsVwap = true;
                            }
                        }
                        else if (targetVWAP > 0) { tradeTP1 = targetVWAP; tp1IsVwap = true; tradeTP2 = 0; tp2IsVwap = false; }
                        else if (targetSession > 0) { tradeTP1 = targetSession; tp1IsVwap = false; tradeTP2 = 0; tp2IsVwap = false; }
                        else { tradeTP1 = 0; tp1IsVwap = false; tradeTP2 = 0; tp2IsVwap = false; }
                        
                         // V33: MARK AS TRADED
                        if (lastUnlockedHighSession != null) lastUnlockedHighSession.HighTradeCount++;
                    }
                }
                // Detect TP1 Fill to move SL to Breakeven
                else if (execution.Order.Name == "TP1_Long" || execution.Order.Name == "TP1_Short")
                {
                    tp1Hit = true; 
                    tradeSL = tradeEntryPrice; // Breakeven
                }
                
                
                // V_CSV: Log Exits (Split Contracts)
                // Broadened filter: Capture ANY filled order that is NOT an Entry
                // V_CSV: Logging MOVED to OnTrade to capture correct MAE/MFE
        // Detect if a trade was closed by checking SystemPerformance
        if (SystemPerformance.AllTrades.Count > _lastTradeCount)
        {
            // Iterate through new trades (in case multiple closed at once)
            for (int i = _lastTradeCount; i < SystemPerformance.AllTrades.Count; i++)
            {
                Trade t = SystemPerformance.AllTrades[i];
                LogTrade(t);
            }
            _lastTradeCount = SystemPerformance.AllTrades.Count;
        }

                if (!execution.Order.Name.Contains("Entry"))
                {
                     // V_CSV: Logging MOVED BACK to OnExecutionUpdate because OnTrade is not available in StrategyBase
                     // Usage of SystemPerformance to find the Closed Trade
                     try {
                         lock(fileLock)
                         {
                             // Find the trade associated with this execution ID
                             var trade = SystemPerformance.AllTrades.FirstOrDefault(t => t.Exit.ExecutionId == execution.ExecutionId);
                             
                             // V_CSV FIX: In Live Mode, ignore historical calculations (startup)
                             if (LiveTradingMode && State == State.Historical) return;
                             
                             if (trade != null && trade.Entry != null && trade.Exit != null)
                             {
                                 // Calc Direction
                                 bool isLong = trade.Entry.MarketPosition == MarketPosition.Long;
                                 string typeStr = isLong ? "Long" : "Short";
                                 
                                 double pnl = trade.ProfitCurrency;
                                 double mae = trade.MaeCurrency;
                                 double mfe = trade.MfeCurrency;
                                 string setupName = this.lastEntrySetup;
                                 
                                 int qty = trade.Quantity; // Usually correct for the executed chunk
                                 
                                 for(int i=0; i<qty; i++)
                                 {
                                     string exitId = trade.Exit.Order != null ? trade.Exit.Order.OrderId : "T_" + trade.Exit.ExecutionId;
                                     string line = string.Format("{0}_{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13}\n",
                                         exitId, i + 1, Instrument.FullName,
                                         trade.Entry.Time.ToString("o"), typeStr, trade.Entry.Price,
                                         trade.Exit.Time.ToString("o"), trade.Exit.Price,
                                         trade.Exit.Order != null ? trade.Exit.Order.Name : "Exit",
                                         (pnl/qty).ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                                         setupName,
                                         (mae/qty).ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                                         (mfe/qty).ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                                         trade.Entry.Account.Name
                                     );
                                     File.AppendAllText(csvPath, line);
                                     
                                     // JS Write
                                     string jsLine = string.Format("window.RTA_DATA.push({{id:'{0}_{1}',instrument:'{2}',entryTime:'{3}',type:'{4}',entryPrice:{5},exitTime:'{6}',exitPrice:{7},result:'{8}',pnl:{9},setup:'{10}'}});\n",
                                         exitId, i + 1, Instrument.FullName,
                                         trade.Entry.Time.ToString("o"), typeStr, trade.Entry.Price,
                                         trade.Exit.Time.ToString("o"), trade.Exit.Price,
                                         trade.Exit.Order != null ? trade.Exit.Order.Name : "Exit",
                                         (pnl/qty).ToString(System.Globalization.CultureInfo.InvariantCulture),
                                         setupName
                                     );
                                     File.AppendAllText(jsPath, jsLine);
                                 }
                             }
                         }
                     } catch (Exception ex) { Print("Log Error: " + ex.Message); }
                }
            }
            
            // Note: We moved PnL update to OnTrade for accuracy, but we can keep it here too just in case.
            if (State == State.Realtime) UpdatePnL();
            else UpdatePnL();
        }





        // V56: MANUAL SL SYNC
        // Allows user to move SL on chart without Strategy fighting back.
        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
             if (State != State.Realtime) return;

             // V_DEBUG: Live Trace of Entry Orders for Diagnosis
             if (order != null && order.Name.Contains("Entry"))
             {
                 Print(string.Format("LIVE_TRACE: Name={0} | State={1} | Filled={2}/{3} | Limit={4} | Stop={5} | Err={6} | Native={7}",
                     order.Name, orderState, filled, quantity, limitPrice, stopPrice, error, nativeError));

                 // V_FIX: Auto-Retry if Order is Cancelled by External Force (NoError)
                 if (orderState == OrderState.Cancelled && error == ErrorCode.NoError)
                 {
                     Print("RelativeVwapStrategy: External Cancel Detected. Unlocking Signal for Retry.");
                     if (order.Name == "EntryLong") lowSignalFired = false;
                     else if (order.Name == "EntryShort") highSignalFired = false;
                 }
             }

             if (order == null) return;
             
             // Check if it's our SL order
             if (order.Name == "SL_Long" || order.Name == "SL_Short" || order.Name == "Stop Loss")
             {
                 // Check if Working or Accepted (Active)
                 if (orderState == OrderState.Working || orderState == OrderState.Accepted)
                 {
                     // If Strategy's internal SL differs from Order's SL, assume User moved it.
                     if (Math.Abs(stopPrice - tradeSL) > TickSize && tradeSL != 0)
                     {
                         // Update Internal Variable to match Line on Chart
                         tradeSL = stopPrice;
                         Print("RelativeVwapStrategy: Manual SL Detected. Syncing tradeSL to: " + tradeSL);
                     }
                 }
             }
        }

        #region Helpers
        private void ResetSessionDeep(List<SessionLevelInfo> sessions, double currentClose)
        {
            if (sessions == null || sessions.Count == 0) return;
            
            // V_FIX: Context-Aware Reset WITH TOLERANCE
            // If price is Above OR "Virtually At" the level (within tolerance), we assume it's history/noise.
            // Tolerance prevents "Spawning on the line" from triggering immediate breaks.
            double tolerance = 10 * TickSize; 
            
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                bool isLast = (i == sessions.Count - 1); // Track if it's the latest session
                
                // HIGH SIDE
                // If Price > (High - Tolerance), we consider it "Taken/Passed" contextually.
                if (s.High > 0 && currentClose > (s.High - tolerance)) 
                {
                    // V_VISUAL: HIDE OLD MITIGATED LINES
                    // Instead of marking them broken 'Now' (which draws a line from Start to Now),
                    // we mark them Broken 'At Start' and End Ghost 'At Start'.
                    // This creates a 0-length line, effectively hiding it.
                    
                    // V_FIX: DO NOT HIDE THE CURRENT SESSION (isLast)
                    // If it's the active/latest session, let standard CheckTouches handle it 
                    // (which will mark it broken at CurrentBar, keeping it visible).
                    if (!isLast)
                    {
                        s.HighBrokenBarIdx = s.StartBarIdx; 
                        s.HighGhostEndIdx = s.StartBarIdx; 
                        Print(string.Format("ResetDeep: {0} High {1} silenced/hidden (Price {2} > Level-Tol)", s.Name, s.High, currentClose));
                    }
                }
                // V_FIX: DO NOT RESET TO -1. 

                // LOW SIDE
                // If Price < (Low + Tolerance)
                if (s.Low > 0 && currentClose < (s.Low + tolerance))
                {
                    if (!isLast)
                    {
                        s.LowBrokenBarIdx = s.StartBarIdx; // Hide
                        s.LowGhostEndIdx = s.StartBarIdx;  // Hide
                        Print(string.Format("ResetDeep: {0} Low {1} silenced/hidden (Price {2} < Level+Tol)", s.Name, s.Low, currentClose));
                    }
                }
                // V_FIX: DO NOT RESET TO -1. Preserves Indicator Memory.
            }
        }
        
        // V_FIX: State Persistence against Strategy Resets
        private void RestoreTradedState()
        {
            try 
            {
                 if (!File.Exists(csvPath)) return;
                 
                 // Read CSV (Allow concurrent access)
                 string[] lines;
                 using (var fs = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                 using (var sr = new StreamReader(fs, Encoding.Default)) {
                     lines = sr.ReadToEnd().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                 }
                 
                 DateTime today = DateTime.Now.Date;
                 
                 foreach(var line in lines)
                 {
                     // Format: ID,1,Instr,EntryTime,Type,EntryPx,ExitTime,ExitPx,ExitName,PnL,Setup,MAE,MFE,Acc
                     var parts = line.Split(',');
                     if (parts.Length < 11) continue;
                     
                     // Check Date: V_FIX: Relaxed check for Overnight persistence (Last 24 hours)
                     DateTime entryTime;
                     if (!DateTime.TryParse(parts[3], out entryTime)) continue;
                     
                     // If entry was within last 24h, we consider it relevant for "Current State" restoration
                     // This handles the "Asian Session at 23:00 Yesterday" vs "Restart at 08:00 Today" case.
                     if (entryTime > DateTime.Now.AddHours(-24))
                     {
                         string setup = parts[10]; // "Asia Low 5 Days"
                         lastEntrySetup = setup; // V_REF: Store for UI
                         
                         // Parse: {Name} {Side} {Days} Days
                         // Parse: {Name} {Side} {Days} Days
                         var setupParts = setup.Split(' ');
                         if (setupParts.Length < 4) continue;
                         
                         string sName = setupParts[0]; // Asia/Europe/USA
                         string sSide = setupParts[1]; // High/Low
                         string sDays = setupParts[2]; // 5
                         
                         int daysAgo = 0;
                         int.TryParse(sDays, out daysAgo);
                         
                          // Find Session
                          List<SessionLevelInfo> targetList = null;
                          if (_vwapIndicator != null)
                          {
                              if (sName == "Asia") targetList = _vwapIndicator.AsiaSessions;
                              else if (sName == "Europe") targetList = _vwapIndicator.EuropeSessions;
                              else if (sName == "USA") targetList = _vwapIndicator.USSessions;
                          }
                         
                         if (targetList != null)
                         {
                             // Find session by Date Logic
                             // DaysAgo = (Today - SessionDate).Days
                             // SessionDate = Today - DaysAgo
                             DateTime targetDate = today.AddDays(-daysAgo);
                             
                             // Fuzzy match date (SessionDate includes time, usually 00:00 or StartTime)
                             // Use .Date comparison
                             var session = targetList.FirstOrDefault(s => s.SessionDate.Date == targetDate.Date);
                             
                             if (session != null)
                             {
                                 if (sSide == "High") 
                                 {
                                     session.HighTradeCount++; 
                                     Print(string.Format("RelativeVwapStrategy: RESTORED HISTORY -> {0} High Entry Restored (Count={1})", session.Name, session.HighTradeCount));
                                 }
                                 else if (sSide == "Low") 
                                 {
                                     session.LowTradeCount++; 
                                     Print(string.Format("RelativeVwapStrategy: RESTORED HISTORY -> {0} Low Entry Restored (Count={1})", session.Name, session.LowTradeCount));
                                 }
                             }
                         }
                     }
                 }
                 Print("RelativeVwapStrategy: Global State Restored from CSV.");
                 UpdatePnL(); // V_REF: Force UI Refresh after restore
            }
            catch (Exception ex) 
            {
                Print("RelativeVwapStrategy: State Restore Failed: " + ex.Message);
            }
        }

        // V31: START - Refactored ResetGlobal to preserve Anchors on Reload/Button Click
        private void ResetGlobal(bool resetAnchors)
        {
            // V43/V51: PRESERVE STATE & ACTIVATE GHOST *BEFORE* WIPING
            bool isFlat = (Position == null || Position.MarketPosition == MarketPosition.Flat);

            if (!isFlat && resetAnchors)
            {
                // V51: ACTIVATE GHOST TRAILING
                // Capture current LIVE state into LEGACY state before it gets wiped
                if (!useLegacyTrailing) // Only capture once (on the first reset)
                {
                    legacyHighPV = sessionHighPV; legacyHighVol = sessionHighVol;
                    legacyLowPV = sessionLowPV; legacyLowVol = sessionLowVol;
                    
                    // Pre-calculate just in case
                    if (legacyHighVol > 0) legacyHighVWAP = legacyHighPV / legacyHighVol;
                    if (legacyLowVol > 0) legacyLowVWAP = legacyLowPV / legacyLowVol;
                    
                    lastLegacyHighVWAP = legacyHighVWAP; // Initialize Last for drawing
                    lastLegacyLowVWAP = legacyLowVWAP;

                    useLegacyTrailing = true;
                    Print("RELATIVE VWAP STRATEGY: Session Reset -> Ghost Trailing ACTIVATED.");
                } 
            }

            // Anchors (Only reset on NEW DAY/SESSION)
            if (resetAnchors)
            {
                currentDayHigh = double.MinValue; currentDayLow = double.MaxValue;
                sessionHighBarIdx = -1; sessionLowBarIdx = -1;
                Print("RELATIVE VWAP STRATEGY: Global Anchors (High/Low) Reset for New Day.");
                
                 // Volume State
                 // If we keep Anchors, we MUST keep Volume state too, otherwise VWAP starts from 0 at noon.
                 sessionHighPV = 0; sessionHighVol = 0; sessionLowPV = 0; sessionLowVol = 0;
                 liveHighVWAP = 0; liveLowVWAP = 0; hasHighVWAP = false; hasLowVWAP = false;
            }
            
            // Logic Flags (ALWAYS reset to clear stale state)
            if (isFlat)
            {
                highSignalFired = false; lowSignalFired = false;
                
                // V43: PRESERVE TP FLAGS IF ACTIVE
                tp1IsVwap = false; tp2IsVwap = false;
                
                // Clear Session References ONLY if Flat
                lastUnlockedHighSession = null;
                lastUnlockedLowSession = null;
                
                // Reset Ghost
                tradeSL = 0; tradeTP1 = 0; tradeTP2 = 0; tp1Hit = false;
                useLegacyTrailing = false;
            }
            else
            {
                // Partial Reset: If Long, keep Low Fired (Locked). If Short, keep High Fired.
                 if (Position.MarketPosition == MarketPosition.Long) { lowSignalFired = true; highSignalFired = false; }
                 else if (Position.MarketPosition == MarketPosition.Short) { highSignalFired = true; lowSignalFired = false; }
            }
            
            highDetached = false; lowDetached = false;
            
            // (Removed redundant blocks below)
            // ...
            
            // Removed old ghost block (Moved to top)
            
            string timeStr = (Bars != null && CurrentBar >= 0) ? Time[0].ToString() : "Init";
            if (resetAnchors) Print(timeStr + " [RelativeVwapStrategy] GLOBAL RESET (FULL) - Mode: " + (isFlat ? "Flat/Null" : "Active"));
            else Print(timeStr + " [RelativeVwapStrategy] GLOBAL RESET (LOGIC ONLY - KEEPING ANCHORS)");
        }
        // V31: END

        private void CheckTouches(List<SessionLevelInfo> sessions, double high, double low)
        {
            if (sessions == null) return;
            foreach (var s in sessions) {
                if (!s.IsActive && s.High > 0) {
                    // Check breaks ONLY if not already closed (GhostEnded) AND Not Max Traded
                    if (s.HighGhostEndIdx == -1 && s.HighTradeCount < MaxEntriesPerLevel) {
                         // Case 1: First Break (STRICT > to match VWAP Anchor Logic)
                         if (s.HighBrokenBarIdx == -1 && high > s.High) { 
                             s.HighBrokenBarIdx = CurrentBar; 
                             
                             // V41: INSTANT REACTION (Removed Cooldown)
                             // Trusted because CurrentBar break in Realtime IS a live event.
                             Print("RELATIVE VWAP STRATEGY: Valid Break Detected (Instant)");
                             highHasTakenRelevant = true; 
                             highSignalFired = false; 
                             highDetached = false; 
                             lastUnlockedHighSession = s;
                         }
                         // Case 2: Already Broken (Persist Flag)
                         else if (s.HighBrokenBarIdx != -1) {
                             if (lastUnlockedHighSession == s) highHasTakenRelevant = true;
                         }
                    }
                    
                    if (s.LowGhostEndIdx == -1 && s.LowTradeCount < MaxEntriesPerLevel) {
                        // Case 1: First Break (STRICT < to match VWAP Anchor Logic)
                        if (s.LowBrokenBarIdx == -1 && low < s.Low) { 
                            s.LowBrokenBarIdx = CurrentBar; 
                            
                            // V41: INSTANT REACTION (Removed Cooldown)
                            Print("RELATIVE VWAP STRATEGY: Valid Break Detected (Instant) - LOW");
                            lowHasTakenRelevant = true; 
                            lowSignalFired = false; 
                            lowDetached = false;
                            lastUnlockedLowSession = s; 
                        }
                        // Case 2: Already Broken
                         else if (s.LowBrokenBarIdx != -1) {
                             if (lastUnlockedLowSession == s) lowHasTakenRelevant = true;
                         }
                    }
                }
            }
        }
        
        private void CloseGhostLines(List<SessionLevelInfo> sessions, int closeIdx)
        {
            if (sessions == null) return;
            foreach (var s in sessions)
            {
                if (s.HighBrokenBarIdx != -1 && s.HighGhostEndIdx == -1 && s.HighBrokenBarIdx <= closeIdx) s.HighGhostEndIdx = closeIdx;
                if (s.LowBrokenBarIdx != -1 && s.LowGhostEndIdx == -1 && s.LowBrokenBarIdx <= closeIdx) s.LowGhostEndIdx = closeIdx;
            }
        }

        private void UpdateSession(List<SessionLevelInfo> sessions, string name, DateTime time, string startStr, string endStr, bool enable, double high, double low)
        {
            if (!enable || sessions == null) return;
            
            // CONVERT start/end strings (assumed Exchange Time) to Local/Chart time based on CurrentBarDate
            TimeSpan startTime = GetTimeByZone(startStr);
            TimeSpan endTime = GetTimeByZone(endStr);
            TimeSpan currentTime = time.TimeOfDay;

            bool isInside = false;
            // Logic: Start < End (Normal) | Start > End (Overnight)
            if (startTime < endTime)
                isInside = currentTime >= startTime && currentTime < endTime;
            else 
                isInside = currentTime >= startTime || currentTime < endTime;

            SessionLevelInfo currentSession = sessions.Count > 0 ? sessions.Last() : null;

            if (isInside)
            {
                // Determination of 'Session Date' logic for overnight sessions
                DateTime sessionDate = time.Date;
                if (startTime > endTime && currentTime < endTime) sessionDate = time.Date.AddDays(-1);

                if (currentSession == null || !currentSession.IsActive || currentSession.SessionDate != sessionDate)
                {
                    // Case: Start new session
                    currentSession = new SessionLevelInfo 
                    { 
                        Name = name, 
                        IsActive = true, 
                        High = high, 
                        Low = low, 
                        SessionDate = sessionDate,
                        StartBarIdx = CurrentBar // Added to match Strategy struct if needed, though mostly for logic
                    };
                    sessions.Add(currentSession);
                }
                else
                {
                    // Update existing
                    currentSession.High = Math.Max(currentSession.High, high);
                    currentSession.Low = Math.Min(currentSession.Low, low);
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
        }
        
        #region Time Zone Helpers
        private DateTime CurrentBarDate; // Cache updated in OnBarUpdate
        private TimeZoneInfo _nyTimeZone; // Cache

        private int CalculateRiskQuantity(double entryPrice, double slPrice)
        {
            // V35: RISK MODE TOGGLE
            if (RiskMethod == RiskManagementModeBacktest.FixedContracts) return Contracts;

            // 1. Check for valid prices
            if (entryPrice <= 0 || slPrice <= 0) return Contracts;

            // 2. Calculate Risk per Share
            double riskPerContract = Math.Abs(entryPrice - slPrice) * Instrument.MasterInstrument.PointValue;
            
            // DEBUG: Print critical values for diagnostic
            if (State == State.Realtime)
            {
                Print(string.Format("RISK CALC DEBUG: Instr={0} Entry={1} SL={2} Diff={3} PointValue={4}", 
                    Instrument.FullName, entryPrice, slPrice, Math.Abs(entryPrice - slPrice), Instrument.MasterInstrument.PointValue));
            }

            if (riskPerContract <= 0) return Contracts;

            // 3. Get Account Balance (Cash Value)
            // Use Account.Get(AccountItem.CashValue) to be safe across different modes
            double balance = Account.Get(AccountItem.CashValue, Currency.UsDollar); 
            // Note: If account currency differs from instrument currency, NinjaTrader usually handles it 
            // but relying on "CashValue" is standard practice.
            
            // 4. Calculate Risk Amount
            double riskAmount = balance * (RiskPercent / 100.0);
            
            // 5. Calculate Raw Quantity
            int qty = (int)(riskAmount / riskPerContract);
            
            if (State == State.Realtime)
            {
                Print(string.Format("RISK CALC RESULT: Balance={0} RiskAmt={1} RiskPerCon={2} -> CrudeQty={3}", 
                    balance, riskAmount, riskPerContract, qty));
            }
            
            // 6. Apply "Min Contracts" Rule
            int finalQty = Math.Max(Contracts, qty); 
            
            // 7. Safety Check: Max Contracts (User Request: "If exceeds, do not enter")
            // Previously: Capped at MaxContracts (Math.Min)
            // New Logic: If calculated exceeds MaxContracts, SKIP completely (Return 0)
            if (finalQty > MaxContracts)
            {
                 Print(string.Format("RISK CALC WARNING: Calculated Qty ({0}) > MaxContracts ({1}). Trade Skipped per user rule.", finalQty, MaxContracts));
                 return 0; // Signal to Skip
            }
            
            return finalQty;
        }

        // V_CSV: RESTORED LOGGING LOGIC
        private int _lastTradeCount = 0; // State tracker for logging

        // V_REPLAY: Market Data Logger
        // V_REPLAY: Market Data Logger
        private void LogMarketData()
        {
             // Safety checks
             if (Bars == null || CurrentBar < 1) return;

             try 
             {
                 // Log PARTIAL or COMPLETED bar? 
                 // IsFirstTickOfBar context means CurrentBar is the NEW open bar.
                 // So we log the PREVIOUS bar (Index 1) which just closed.
                 int idx = 1;
                 
                 DateTime barTime = Time[idx];
                 double o = Open[idx]; 
                 double h = High[idx]; 
                 double l = Low[idx]; 
                 double c = Close[idx]; 
                 double v = Volume[idx];
                 
                 // V_ADVANCED: Capture Context Data
                 double hVwap = (Values[0] != null) ? Values[0][idx] : 0;
                 double lVwap = (Values[1] != null) ? Values[1][idx] : 0;
                 
                 double activeLevel = 0;
                 // Determine which level is "Active" (Reference)
                 // Start with High Session
                 if (lastUnlockedHighSession != null) activeLevel = lastUnlockedHighSession.High;
                 // Override with Low Session if it's the more recent relevant trigger or we are Short?
                 // Simple logic: If we have a Low Session active/unlocked, it might be the ref.
                 // Better: Log BOTH? Or just the one that IS actively unlocking the trade logic.
                 // For now, let's log the one that is non-null. If both non-null, implies both sides active.
                 // Let's rely on the "Unlock" logic priority.
                 if (lastUnlockedLowSession != null) 
                 {
                     // If both are present, which one matters? 
                     // Usually the one closer to price or recently broken.
                     // Let's use the one matching the current "HasTaken" logic if possible.
                     if (lowHasTakenRelevant) activeLevel = lastUnlockedLowSession.Low;
                 }
                 if (lastUnlockedHighSession != null && highHasTakenRelevant) activeLevel = lastUnlockedHighSession.High;

                 
                 // File Logic
                 string dateStr = barTime.ToString("yyyy-MM-dd");
                 string cleanInstr = Instrument.FullName.Replace(" ", "_").Replace("/", "-");
                 string fileName = string.Format("{0}_{1}.csv", cleanInstr, dateStr);
                 
                 string dirPath = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "MarketData_Exports");
                 if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);
                 
                 string fullPath = Path.Combine(dirPath, fileName);
                 
                 // CSV Format: Date,Time,Open,High,Low,Close,Volume,HighVWAP,LowVWAP,LevelPrice
                 string line = string.Format("{0},{1},{2},{3},{4},{5},{6},{7:F2},{8:F2},{9:F2}", 
                     barTime.ToString("yyyy-MM-dd"),
                     barTime.ToString("HH:mm:ss"),
                     o, h, l, c, v,
                     hVwap,
                     lVwap,
                     activeLevel);

                 // Append
                 lock (fileLock)
                 {
                     if (!File.Exists(fullPath)) 
                        File.WriteAllText(fullPath, "Date,Time,Open,High,Low,Close,Volume,HighVWAP,LowVWAP,LevelPrice" + Environment.NewLine);
                        
                     File.AppendAllText(fullPath, line + Environment.NewLine);
                 }
             }
             catch {}
        }

        private void LogTrade(Trade trade)
        {
             if (csvPath == null) return;
             
             // 1. Get Setup Name securely
             // If we are in a trade, lastEntrySetup should hold the setup name. 
             // Ideally we'd link it to the trade, but for now global state serves.
             string setupName = !string.IsNullOrEmpty(lastEntrySetup) ? lastEntrySetup : "Unknown";

             // 2. Format Data
             // ID, Instr, EntryTime, Type, EntryPx, ExitTime, ExitPx, Result, PnL, SetupName, MAE, MFE, Account
             // Note: trade.Entry.Time is often the time of the *first* execution if scaled.
             
             string type = (trade.Entry.MarketPosition == MarketPosition.Long) ? "Long" : "Short";
             string result = (trade.ProfitCurrency >= 0) ? "Win" : "Loss";
             
             double mae = trade.MaeCurrency; // Maximum Adverse Excursion in Currency
             double mfe = trade.MfeCurrency; // Maximum Favorable Excursion
             
             // Escape CSV fields if needed (SetupName shouldn't have commas, but safety first)
             if (setupName.Contains(",")) setupName = setupName.Replace(",", " ");

             string line = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12}",
                 trade.TradeNumber,
                 Instrument.FullName,
                 trade.Entry.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                 type,
                 trade.Entry.Price, // Corrected: Use Entry.Price
                 trade.Exit.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                 trade.Exit.Price,  // Corrected: Use Exit.Price
                 result,
                 trade.ProfitCurrency.ToString("F2"),
                 setupName,
                 mae.ToString("F2"),
                 mfe.ToString("F2"),
                 Account.Name
             );
             
             // 3. Write to File
             try
             {
                 // In Backtest, no lock needed usually, but safe to add context checks
                 if (LiveTradingMode)
                 {
                     lock (fileLock) { File.AppendAllText(csvPath, line + Environment.NewLine); }
                 }
                 else
                 {
                     File.AppendAllText(csvPath, line + Environment.NewLine);
                 }
             }
             catch (Exception ex)
             {
                 Print("RelativeVwapStrategy ERROR: Failed to log trade. " + ex.Message);
             }
        }

        private TimeSpan GetTimeByZone(string timeStr)
        {
             DateTime dt;
             // Default parsing (User Local Time / Chart Time)
             if (!DateTime.TryParse(timeStr, out dt)) return TimeSpan.Zero;
             
             if (!UseExchangeTime) return dt.TimeOfDay;

             // --- EXCHANGE TIME CONVERSION LOGIC ---
             if (_nyTimeZone == null)
             {
                 try { _nyTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
                 catch { _nyTimeZone = TimeZoneInfo.Local; } // Fallback
             }
             
             // 1. Construct "Today + TimeStr" in NY Time
             // We use CurrentBarDate as the base.
             // Note: CurrentBarDate is in "Chart Time" (Local). 
             
             // Base date: Use CurrentBarDate (Local). 
             
             DateTime nyTimeUnspec = CurrentBarDate.Add(dt.TimeOfDay);
             
             try 
             {
                 // Convert NY Time -> UTC
                 DateTime utcTime = TimeZoneInfo.ConvertTimeToUtc(nyTimeUnspec, _nyTimeZone);
                 
                 // Convert UTC -> Local (Chart Time)
                 DateTime localTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, TimeZoneInfo.Local);
                 
                 return localTime.TimeOfDay;
             }
             catch
             {
                 return dt.TimeOfDay;
             }
        }
        #endregion
        #region Smart Label Stacking
        private class SmartLabel
        {
            public float X;
            public float Y;
            public string Text;
            public Brush TextBrush;
            public float FontSize;
            public float EstimatedWidth; // Rough estimate for collision
        }
        
        private List<SmartLabel> pendingLabels = new List<SmartLabel>();
        #endregion

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            // Call base render first (if needed, but usually empty for strategies)
            base.OnRender(chartControl, chartScale);
            
            if (Bars == null || chartControl == null || chartScale == null || RenderTarget == null) return;
            
            pendingLabels.Clear(); // V_SMART: Reset frame
            
            // Calculate Current State to Display
            string statusText = "WAIT";
            Brush statusColor = Brushes.Gray; // Default WAIT (Changed from Red to Gray)
            
            // Check Locked first
            if (highSignalFired && lowSignalFired) { statusText = "LOCKED (ALL)"; statusColor = Brushes.Gray; }
            else if (highSignalFired && !lowSignalFired) { statusText = "LOCKED (HIGH)"; statusColor = Brushes.Gray; } // Partial Lock
            
            // Check In-Trade
            // Added Quantity > 0 check to filter ghost positions
            // Check In-Trade
            // Added Quantity > 0 check to filter ghost positions
            if (Position.MarketPosition != MarketPosition.Flat && Position.Quantity > 0)
            {
                // DEBUG: Print ONCE to confirm what the strategy sees if it's confused
                if (DateTime.Now.Second % 10 == 0 && DateTime.Now.Millisecond < 100) 
                   Print(string.Format("DEBUG GHOST: Pos={0} Qty={1} AvgPrice={2} Account={3}", 
                       Position.MarketPosition, Position.Quantity, Position.AveragePrice, Account.Name));

                if (tp1Hit)  
                {
                    // Check if SL is at Breakeven (approximate check)
                    bool isBE = false;
                    if (Position.MarketPosition == MarketPosition.Long && tradeSL >= tradeEntryPrice) isBE = true;
                    if (Position.MarketPosition == MarketPosition.Short && tradeSL <= tradeEntryPrice) isBE = true;
                    
                    if (isBE) { statusText = "SHIELD UP (BE)"; statusColor = Brushes.DodgerBlue; } // Breakeven
                    else { statusText = "TP1 HIT"; statusColor = Brushes.MediumPurple; }
                }
                else 
                {
                    statusText = "IN TRADE"; statusColor = Brushes.RoyalBlue;
                }
            }
            else // No Position
            {
                // Prioritize "Closest" or "Most Active" state
                // Logic: ARMED > READY > WAIT
                
                bool armed = false;
                bool ready = false;
                
                // Check High Side
                if (!highSignalFired)
                {
                     // V38: ANCHOR SYNC VISUAL CHECK
                     bool oldAnchor = (lastUnlockedHighSession != null && sessionHighBarIdx < lastUnlockedHighSession.HighBrokenBarIdx);

                     // Strict ARMED Check: Must be Detached AND Have Taken Liquidity (Ready)
                     if (highDetached && highHasTakenRelevant) {
                         if (oldAnchor) { statusText = "FILTERED (OLD H)"; statusColor = Brushes.LightSlateGray; armed = false; }
                         else armed = true; 
                     }
                     else if (highHasTakenRelevant) {
                         if (oldAnchor) { statusText = "FILTERED (OLD H)"; statusColor = Brushes.LightSlateGray; ready = false; }
                         else ready = true; 
                     }
                }
                
                // Check Low Side (Override if 'more advanced' state?)
                if (!lowSignalFired)
                {
                    // V38: ANCHOR SYNC VISUAL CHECK
                    bool oldAnchor = (lastUnlockedLowSession != null && sessionLowBarIdx < lastUnlockedLowSession.LowBrokenBarIdx);

                    if (lowDetached && lowHasTakenRelevant) {
                         if (oldAnchor) { statusText = "FILTERED (OLD L)"; statusColor = Brushes.LightSlateGray; armed = false; }
                         else armed = true;
                    }
                    else if (lowHasTakenRelevant) {
                         if (oldAnchor) { statusText = "FILTERED (OLD L)"; statusColor = Brushes.LightSlateGray; ready = false; }
                         else ready = true;
                    }
                }
                
                if (armed) { statusText = "ARMED"; statusColor = Brushes.LimeGreen; }
                else if (ready) { statusText = "READY"; statusColor = Brushes.Yellow; }
                else if (statusText.Contains("FILTERED")) { /* Keep Filtered Status */ }
                else if (highSignalFired || lowSignalFired) {
                     // If we are not armed/ready but have fired one side, we are waiting for the other or reset.
                     // Keep "LOCKED (PARTIAL)" or just WAIT if the remaining side hasn't triggered.
                     if (statusText.Contains("LOCKED")) { /* Keep Locked */ }
                     else { statusText = "WAIT"; statusColor = Brushes.RoyalBlue; }
                }
                
                // V28: VISUALIZE STATE (Debug)
                if (State == State.Historical) {
                    statusText += " (HIST)";
                    // Optional: Fade color to indicate it's not "Live" logic yet
                    statusColor = Brushes.DarkSlateGray; 
                }
            }


            // Draw the Label
            // Position: Top Right of the chart, below the strategy name? Or specific corner?
            // Let's put it Top Right, below standard text.
            
            SimpleFont font = new SimpleFont("Arial", 16) { Bold = true };
            
            // Use standard direct 2D rendering for overlay
            // We need to use 'ChartPanel' coordinates
            

                    // ----------------------------------------------------
                    // 1. STATUS TEXT (Top-Right)
                    // ----------------------------------------------------
                    float panelWidth = 175;
                    float rightEdge = ChartPanel.X + ChartPanel.W;
                    
                    // Coordinates: Top Right
                    float statusX = rightEdge - panelWidth; 
                    float statusY = ChartPanel.Y + 20; 
                    float statusW = panelWidth;

                    // ----------------------------------------------------
                    // 2. DEBUG PANEL (Bottom-Left)
                    // ----------------------------------------------------
                    // Coordinates: Bottom Left
                    // Must be above Buttons (~60px from bottom)
                    float checkH = 300; 
                    float bottomMargin = 95; // Adjusted to 95 (Halfway between 70 and 120)
                    
                    float checkX = ChartPanel.X + 10;
                    float checkY = (ChartPanel.Y + ChartPanel.H) - checkH - bottomMargin;
                    if (checkY < ChartPanel.Y) checkY = ChartPanel.Y; 
                    float checkW = panelWidth;

                    using (var brush = statusColor.ToDxBrush(RenderTarget))
                    using (var textFormat = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", 20) { TextAlignment = SharpDX.DirectWrite.TextAlignment.Center, ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center })
                    {
                        // Draw Status Text (Top Right)
                        SharpDX.RectangleF rect = new SharpDX.RectangleF(statusX, statusY, statusW, 40);
                        RenderTarget.DrawText(statusText, textFormat, rect, brush);

                        // Background Box for Debug Panel (Bottom Left)
                        using (var backBrush = PanelBackgroundColor.ToDxBrush(RenderTarget))
                        {
                            backBrush.Opacity = PanelOpacity; 
                            RenderTarget.FillRectangle(new SharpDX.RectangleF(checkX, checkY, checkW, checkH), backBrush);
                        }

                        // Text Format
                        using (var listFormat = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Consolas", 11) { TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading, ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Near })
                        using (var whiteBrush = Brushes.WhiteSmoke.ToDxBrush(RenderTarget))
                        using (var grayBrush = Brushes.Gray.ToDxBrush(RenderTarget))
                        using (var greenBrush = Brushes.Lime.ToDxBrush(RenderTarget))
                        using (var redBrush = Brushes.RoyalBlue.ToDxBrush(RenderTarget))
                        {
                            float pad = 10;
                            float lineH = 15;
                            float curY = checkY + pad;
                            float left = checkX + pad;

                            // Helpers
                            Action<string, bool, bool> DrawItem = (label, value, isBad) => {
                                string mark = value ? "[x]" : "[ ]";
                                var brush = value ? greenBrush : (isBad ? redBrush : grayBrush);
                                RenderTarget.DrawText(string.Format("{0,-10} {1}", label, mark), listFormat, new SharpDX.RectangleF(left, curY, checkW, lineH), brush);
                                curY += lineH;
                            };
                            
                            // Header HIGH
                            RenderTarget.DrawText("--- HIGH SIDE ---", listFormat, new SharpDX.RectangleF(left, curY, checkW, lineH), whiteBrush); curY += lineH;
                            DrawItem("Detached", highDetached, false);
                            DrawItem("Taken", highHasTakenRelevant, false);
                            DrawItem("Traded", (lastUnlockedHighSession != null && lastUnlockedHighSession.HighTradeCount >= MaxEntriesPerLevel), true); // Red if max reached
                            
                            // Header LOW
                            curY += 5;
                            RenderTarget.DrawText("--- LOW SIDE ---", listFormat, new SharpDX.RectangleF(left, curY, checkW, lineH), whiteBrush); curY += lineH;
                            DrawItem("Detached", lowDetached, false);
                            DrawItem("Taken", lowHasTakenRelevant, false);
                            DrawItem("Traded", (lastUnlockedLowSession != null && lastUnlockedLowSession.LowTradeCount >= MaxEntriesPerLevel), true);

                            // Header SESSIONS
                            curY += 5;
                            RenderTarget.DrawText("--- SESSIONS ---", listFormat, new SharpDX.RectangleF(left, curY, checkW, lineH), whiteBrush); curY += lineH;
                            
                            // Helper for Session Row
                            Action<string, List<SessionLevelInfo>> DrawSession = (name, list) => {
                                if (list == null || list.Count == 0) return;
                                var s = list.Last();
                                string act = s.IsActive ? "ACT" : "---";
                                string trd = string.Format("H:{0}/{2} L:{1}/{2}", s.HighTradeCount, s.LowTradeCount, MaxEntriesPerLevel);
                                string line = string.Format("{0,-6} {1}  {2}", name, act, trd);
                                RenderTarget.DrawText(line, listFormat, new SharpDX.RectangleF(left, curY, checkW, lineH), s.IsActive ? whiteBrush : grayBrush);
                                curY += lineH;
                            };

                            DrawSession("Asia", _vwapIndicator.AsiaSessions);
                            DrawSession("Europe", _vwapIndicator.EuropeSessions);
                            DrawSession("USA", _vwapIndicator.USSessions);

                            // V49: TRADE DETAIL SECTION
                            curY += 5;
                            RenderTarget.DrawText("--- TRADE INFO ---", listFormat, new SharpDX.RectangleF(left, curY, checkW, lineH), whiteBrush); curY += lineH;
                            
                            string refName = "-";
                            if (Position.MarketPosition != MarketPosition.Flat) refName = lastEntrySetup;
                            else if (lastUnlockedHighSession != null) refName = lastUnlockedHighSession.Name + " (Pend)"; // Debug context if needed
                            else if (lastUnlockedLowSession != null) refName = lastUnlockedLowSession.Name + " (Pend)";
                            else refName = "-";
                            
                            string trailStatus = useLegacyTrailing ? "(GHOST)" : "(LIVE)";
                            
                            Action<string, double, string> DrawVal = (lbl, val, extra) => {
                                string valStr = (val > 0) ? val.ToString("F2") : "-";
                                RenderTarget.DrawText(string.Format("{0,-6} {1} {2}", lbl, valStr, extra), listFormat, new SharpDX.RectangleF(left, curY, checkW, lineH), whiteBrush);
                                curY += lineH;
                            };

                            DrawVal("Ref:", 0, refName); // Hack to show string
                            DrawVal("TP1:", tradeTP1, tp1Hit ? "(HIT)" : trailStatus);
                            DrawVal("TP2:", tradeTP2, trailStatus);
                        }
                    }


                    // V18: DRAW LINES (SL, TP, Entry)
                    // Only draw if we have valid price levels AND user wants to see them
                    if (ShowOrderLines)
                    {
                        // Helper for PnL Calc
                        Func<double, int, string> GetPnL = (targetPx, qty) => {
                             if (qty <= 0 || tradeEntryPrice == 0) return "";
                             double pts = 0;
                             if (Position.MarketPosition == MarketPosition.Long) pts = targetPx - tradeEntryPrice;
                             else if (Position.MarketPosition == MarketPosition.Short) pts = tradeEntryPrice - targetPx;
                             
                             double val = pts * Instrument.MasterInstrument.PointValue * qty;
                             return " ($" + val.ToString("N0") + ")"; 
                        };
                        
                        // Quantity Logic (Estimates based on ManagePosition split)
                        int currentQty = Position.Quantity;
                        int tp1Qty = Math.Max(1, currentQty / 2);
                        int tp2Qty = currentQty - tp1Qty;
                        
                        // SL covers entire remaining position
                        if (tradeSL != 0) DrawLevelLine(RenderTarget, chartScale, tradeSL, StopLossColor, "SL" + GetPnL(tradeSL, currentQty), StopLossWidth, StopLossStyle, 5, 0);
                        
                        // TPs
                        if (tradeTP1 != 0) DrawLevelLine(RenderTarget, chartScale, tradeTP1, ProfitTargetColor, "TP1" + GetPnL(tradeTP1, tp1Qty), ProfitTargetWidth, ProfitTargetStyle, 5, 0);
                        if (tradeTP2 != 0) DrawLevelLine(RenderTarget, chartScale, tradeTP2, ProfitTargetColor, "TP2" + GetPnL(tradeTP2, tp2Qty), ProfitTargetWidth, ProfitTargetStyle, 5, 0);
                        
                        // Entry
                        if (tradeEntryPrice != 0 && Position.MarketPosition != MarketPosition.Flat) 
                            DrawLevelLine(RenderTarget, chartScale, tradeEntryPrice, EntryLineColor, "Entry", EntryLineWidth, EntryLineStyle, 5, 0);
                    }

                     // V_SMART: Draw VWAP Labels (Integrated for Collision)
                     if (ShowLiveVWAP && ShowLabels)
                     {
                         float x2 = (float)ChartControl.GetXByBarIndex(ChartBars, CurrentBar + 3);

                         if (hasHighVWAP && liveHighVWAP > 0)
                         {
                             float y = (float)chartScale.GetYByValue(liveHighVWAP);
                             float fontSize = (float)TradeTextSize; if (fontSize < 6) fontSize = 11;
                             float estWidth = HighVWAPText.Length * (fontSize * 0.6f) + 10;
                             
                             pendingLabels.Add(new SmartLabel {
                                 X = x2 + 5, // 5px Offset
                                 Y = y - (fontSize * 0.6f), // Centered Y
                                 Text = HighVWAPText,
                                 TextBrush = HighVWAPColor,
                                 FontSize = fontSize,
                                 EstimatedWidth = estWidth
                             });
                         }
                         
                         if (hasLowVWAP && liveLowVWAP > 0)
                         {
                             float y = (float)chartScale.GetYByValue(liveLowVWAP);
                             float fontSize = (float)TradeTextSize; if (fontSize < 6) fontSize = 11;
                             float estWidth = LowVWAPText.Length * (fontSize * 0.6f) + 10;
                             
                             pendingLabels.Add(new SmartLabel {
                                 X = x2 + 5, // 5px Offset
                                 Y = y - (fontSize * 0.6f), // Centered Y
                                 Text = LowVWAPText,
                                 TextBrush = LowVWAPColor,
                                 FontSize = fontSize,
                                 EstimatedWidth = estWidth
                             });
                         }
                     }
                     
                     // V_SMART: Draw Session Labels (Strategy handles text to avoid overlap)
                     if (ShowLabels && _vwapIndicator != null)
                     {
                          float rightEdgeBuffer = 40; // Space for text
                          float x2 = (float)ChartControl.GetXByBarIndex(ChartBars, CurrentBar + 3);
                          
                          // Helper Action
                          Action<List<NinjaTrader.NinjaScript.Indicators.RelativeIndicators.RelativeVwap.SessionLevelInfo>, bool, bool, Brush, Brush> AddSessionLabels = 
                              (sessions, showHigh, showLow, lineColor, labelColor) => 
                          {
                              if (sessions == null) return;
                              // Iterate reverse to find latest active? Or just all valid ones?
                              // Usually we only care about the LATEST active or recently broken ones.
                              // RelativeLevels logic draws ALL valid ones. Let's do same but limit to visible?
                              // Actually, for simplicity, let's just do the ones that would be drawn by indicator.
                              // Strategy primarily focuses on Current/Last session usually.
                              
                              // Optimization: Only last 2 sessions to avoid clutter?
                              // Let's iterate all, assuming count is low per day.
                              
                              foreach(var s in sessions)
                              {
                                   // Skipping logic matches Indicator's RenderSessionLevels roughly
                                   if (s.StartBarIdx > ChartBars.ToIndex) continue;
                                   
                                   // HIGH LABEL
                                   if (showHigh && s.High > 0)
                                   {
                                        // Check if line extends to current time (active or extended)
                                        // For now, assume if it's in the list, we draw it at X2 (CurrentBar+3) 
                                        // ONLY IF IT'S RELEVANT?
                                        // RelativeLevels logic draws line from Start to End.
                                        // Label is at End.
                                        
                                        // Logic: If active, End is Future. If closed, End is SessionEnd.
                                        // But if ExtendLinesUntilTouch, it goes until touch.
                                        
                                        // Simplified: Draw label for the *latest* session of each type?
                                        // Or just draw label at X2 if the line is "Alive"?
                                        
                                        // Let's stick to: If session is Active OR (Extended & Not Broken), draw at current X.
                                        // If it's a history session, the label should be at its historical end (which might be off screen).
                                        // Strategy Collision is mostly for LIVE price action.
                                        // So let's only add SmartLabels for the CURRENT active/extended lines.
                                        
                                        bool isRelevant = s.IsActive || (_vwapIndicator.ExtendLinesUntilTouch && s.HighBrokenBarIdx == -1);
                                        
                                        if (isRelevant)
                                        {
                                             float y = (float)chartScale.GetYByValue(s.High);
                                             string txt = s.Name + " High"; // Or custom text?
                                             float fontSize = (float)TradeTextSize; if(fontSize < 6) fontSize = 11;
                                             float estWidth = txt.Length * (fontSize * 0.6f);
                                             
                                             pendingLabels.Add(new SmartLabel {
                                                 X = x2 + 5,
                                                 Y = y - (fontSize * 0.6f),
                                                 Text = txt,
                                                 TextBrush = labelColor,
                                                 FontSize = fontSize,
                                                 EstimatedWidth = estWidth
                                             });
                                        }
                                   }
                                   
                                   // LOW LABEL
                                   if (showLow && s.Low > 0)
                                   {
                                        bool isRelevant = s.IsActive || (_vwapIndicator.ExtendLinesUntilTouch && s.LowBrokenBarIdx == -1);
                                        if (isRelevant)
                                        {
                                             float y = (float)chartScale.GetYByValue(s.Low);
                                             string txt = s.Name + " Low";
                                             float fontSize = (float)TradeTextSize; if(fontSize < 6) fontSize = 11;
                                             float estWidth = txt.Length * (fontSize * 0.6f);
                                             
                                             pendingLabels.Add(new SmartLabel {
                                                 X = x2 + 5,
                                                 Y = y - (fontSize * 0.6f),
                                                 Text = txt,
                                                 TextBrush = labelColor,
                                                 FontSize = fontSize,
                                                 EstimatedWidth = estWidth
                                             });
                                        }
                                   }
                              }
                          };
                          
                          // Add Asia
                          if (SessionShowAsia) AddSessionLabels(_vwapIndicator.AsiaSessions, true, true, SessionAsiaColor, Brushes.Gray); // Label color? Using Gray/Silver
                          
                          // Add Europe
                          if (SessionShowEurope) AddSessionLabels(_vwapIndicator.EuropeSessions, true, true, SessionEuropeColor, Brushes.Gray);
                          
                          // Add US
                          if (SessionShowUS) AddSessionLabels(_vwapIndicator.USSessions, true, true, SessionUSColor, Brushes.White); // US matches text color usually
                     }

                     // V_SMART: Countdown Label (Real Data)
                     if (ShowLabels && ShowCountdown && _vwapIndicator != null && !string.IsNullOrEmpty(_vwapIndicator.CurrentCountdownText))
                     {
                          double countPrice = High.GetValueAt(CurrentBar) + (10 * TickSize); // Default Offset Y
                          float countX = (float)ChartControl.GetXByBarIndex(ChartBars, CurrentBar + 2); // Default Offset X
                          float countY = (float)chartScale.GetYByValue(countPrice);
                          
                          // Heuristic Width for "00:00" or similar
                          float estWidth = _vwapIndicator.CurrentCountdownText.Length * (float)(TradeTextSize * 0.6f);
                          
                          pendingLabels.Add(new SmartLabel {
                              X = countX,
                              Y = countY - ((float)TradeTextSize * 0.6f),
                              Text = _vwapIndicator.CurrentCountdownText,
                              TextBrush = Brushes.White, // Adjust if needed
                              FontSize = (float)TradeTextSize,
                              EstimatedWidth = estWidth
                          });
                     }
                    
                    // V_SMART: Resolve Horizontal Collisions & Draw
                    if (pendingLabels.Count > 0)
                    {
                         // 1. Sort by Y (Top Down) to group nearby labels
                         // Actually, we just want to compare every label with every other label.
                         // Simple O(N^2) is fine for < 10 labels.
                         
                         for (int i = 0; i < pendingLabels.Count; i++)
                         {
                             for (int j = 0; j < i; j++)
                             {
                                 var L1 = pendingLabels[i];
                                 var L2 = pendingLabels[j];
                                 
                                 // Check Vertical Collision (Same Price Level approx)
                                 // Threshold: Label Height (FontSize + Padding) ~ 20px
                                 if (Math.Abs(L1.Y - L2.Y) < (L1.FontSize + 5))
                                 {
                                     // Collision Detected!
                                     // Move L1 (the later one) to the right of L2
                                     float newX = L2.X + L2.EstimatedWidth + 10; // 10px padding
                                     if (newX > L1.X) L1.X = newX;
                                 }
                             }
                         }
                         
                         // 2. Draw All
                         foreach (var lbl in pendingLabels)
                         {
                             // Convert Brush again (Inefficient but robust)
                             System.Windows.Media.Color mediaColor = ((SolidColorBrush)lbl.TextBrush).Color;
                             SharpDX.Color4 dxColor = new SharpDX.Color4(mediaColor.R / 255f, mediaColor.G / 255f, mediaColor.B / 255f, mediaColor.A / 255f);
                             using (var dxBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor))
                             {
                                 RenderTarget.DrawText(lbl.Text, 
                                     new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", lbl.FontSize), 
                                     new SharpDX.RectangleF(lbl.X, lbl.Y, 300, lbl.FontSize + 5), 
                                     dxBrush);
                             }
                         }
                         pendingLabels.Clear(); // Cleanup
                    }
        } // End OnRender
        
        // Helper for Drawing Lines
        private void DrawLevelLine(SharpDX.Direct2D1.RenderTarget renderTarget, ChartScale chartScale, double price, Brush brush, string label, float width, DashStyleHelper dashStyle, int offsetX, int offsetY)
        {
             if (ChartPanel == null || ChartControl == null) return;
             
             float y = (float)chartScale.GetYByValue(price);
             
             // V36: Short Lines (Start at Current Bar, Extend Right 3 Bars)
             // Use ChartControl.GetXByBarIndex to find pixel X.
             float x1 = (float)ChartControl.GetXByBarIndex(ChartBars, CurrentBar);
             float x2 = (float)ChartControl.GetXByBarIndex(ChartBars, CurrentBar + 3); 
             
             // Convert Brush Manual Logic (ToDXColor not standard)
             System.Windows.Media.Color mediaColor = ((SolidColorBrush)brush).Color;
             SharpDX.Color4 dxColor = new SharpDX.Color4(mediaColor.R / 255f, mediaColor.G / 255f, mediaColor.B / 255f, mediaColor.A / 255f);
             
             SharpDX.Direct2D1.SolidColorBrush dxBrush = new SharpDX.Direct2D1.SolidColorBrush(renderTarget, dxColor);
             SharpDX.Direct2D1.StrokeStyle style = null;
             
             // Convert DashStyleHelper to SharpDX DashStyle
             var props = new SharpDX.Direct2D1.StrokeStyleProperties();
             switch (dashStyle)
             {
                 case DashStyleHelper.Dash: props.DashStyle = SharpDX.Direct2D1.DashStyle.Dash; break;
                 case DashStyleHelper.Dot: props.DashStyle = SharpDX.Direct2D1.DashStyle.Dot; break;
                 case DashStyleHelper.DashDot: props.DashStyle = SharpDX.Direct2D1.DashStyle.DashDot; break;
                 case DashStyleHelper.DashDotDot: props.DashStyle = SharpDX.Direct2D1.DashStyle.DashDotDot; break;
                 case DashStyleHelper.Solid: default: props.DashStyle = SharpDX.Direct2D1.DashStyle.Solid; break;
             }
             
             style = new SharpDX.Direct2D1.StrokeStyle(renderTarget.Factory, props);
             
             renderTarget.DrawLine(new SharpDX.Vector2(x1, y), new SharpDX.Vector2(x2, y), dxBrush, width, style);
             

             // Label X: Start slightly after line end + Offset
             // Label Y: Calc with Offset (Centered Vertically)
             
             // Using TradeTextSize property
             float fontSize = (float)TradeTextSize;
             if (fontSize < 6) fontSize = 11;
             
             float labelX = x2 + offsetX; 
             // Center Vertically: y - (fontSize * 0.6) approx centers Arial text on the line
             float labelY = y - (fontSize * 0.6f) + offsetY; 

             // V_SMART: Defer drawing. Add to pending list for collision resolution.
             // Estimate width: char count * (fontSize * 0.6) approx
             float estWidth = label.Length * (fontSize * 0.6f) + 10;
             
             pendingLabels.Add(new SmartLabel {
                 X = labelX,
                 Y = labelY,
                 Text = label,
                 TextBrush = brush,
                 FontSize = fontSize,
                 EstimatedWidth = estWidth
             });
             
             dxBrush.Dispose();
             if (style != null) style.Dispose();
        }



        /*
        private void DrawStrategyTradeLines()
        {
            // ... (Redundant logic commented out to prevent double lines) ...
        }
        */

        #endregion
        #region Properties
        // Duplicate UseExchangeTime removed from here


        [NinjaScriptProperty]
        [Display(Name = "Show Live VWAP Lines", Description = "Draw the internal High/Low VWAP lines on the chart?", Order = 1, GroupName = "6. Visual - VWAP")]
        public bool ShowLiveVWAP { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Order Lines (SL/TP)", Description = "Draw the Strategy's internal SL, TP, and Entry lines?", Order = 1, GroupName = "5. Visual - Trade Setup")]
        public bool ShowOrderLines { get; set; }
        
        // V_AUTO: Locks for Portfolio Backtest (Placed correctly in Class Scope)
        private static object fileLock = new object();
        private static bool IsFileReset = false;

        [Range(1, 100)]
        [Display(Name="Min Contracts", Description="Minimum quantity to trade (overrides risk calc if result is lower)", GroupName="2. Strategy - Risk", Order=2)]
        public int Contracts { get; set; }

        [Range(0.1, 100.0)]
        [Display(Name="Risk Percent", Description="% of Account Balance to risk per trade", GroupName="2. Strategy - Risk", Order=1)]
        public double RiskPercent { get; set; }

        [Range(1, 500)]
        [Display(Name="Max Contracts", Description="Absolute maximum contracts allowed (Safety Cap)", GroupName="2. Strategy - Risk", Order=3)]
        public int MaxContracts { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name="Close Positions End of Day", Description="If true, closes all positions 30 seconds before session end. If false, keeps positions overnight (GTC).", GroupName="1. Strategy - Main", Order=5)]
        public bool CloseOnSessionEnd { get; set; } = false;



        [NinjaScriptProperty]
        [Display(Name = "Trade Direction", Description = "Cycle directions", Order = 1, GroupName = "1. Strategy - Main")]
        public RelativeVwapTradeModeBacktest TradeDirection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Risk Management Mode", Description = "Choose between Fixed Contracts or Risk Calculated.", Order = 0, GroupName = "2. Strategy - Risk")]
        [RefreshProperties(RefreshProperties.All)] // Forces property grid refresh
        public RiskManagementModeBacktest RiskMethod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Panel Right Offset", Description = "Adjust the horizontal position of the Status Panel (Positive moves right, Negative moves left).", Order = 1, GroupName = "4. Visual - Panel")]
        public int PanelRightOffset { get; set; }

        [XmlIgnore]
        [Display(Name = "Panel Background Color", Description = "Background color of the status panel.", Order = 2, GroupName = "4. Visual - Panel")]
        public Brush PanelBackgroundColor { get; set; } = Brushes.Black; // Default Black (Opacity controlled separately)

        [Browsable(false)]
        public string PanelBackgroundColorSerializable
        {
            get { return Serialize.BrushToString(PanelBackgroundColor); }
            set { PanelBackgroundColor = Serialize.StringToBrush(value); }
        }

        [Range(0.0f, 1.0f)]
        [Display(Name = "Panel Opacity", Description = "Opacity of the status panel background (0.0 = Transparent, 1.0 = Solid).", Order = 3, GroupName = "4. Visual - Panel")]
        public float PanelOpacity { get; set; } = 0.6f;

        [Range(6, 30)]
        [Display(Name = "Trade Label Size", Description = "Font size for Trade Setup labels (Entry, SL, TP).", Order = 2, GroupName = "5. Visual - Trade Setup")]
        public int TradeTextSize { get; set; } = 11;

        [Range(0.0f, 1.0f)]
        [Display(Name = "Trade Label Opacity", Description = "Opacity of the Trade Setup label background.", Order = 3, GroupName = "5. Visual - Trade Setup")]
        public float TradeLabelOpacity { get; set; } = 0.5f;

        // Entry Line Properties
        [XmlIgnore] [Display(Name = "Entry Line Color", GroupName = "5. Visual - Trade Setup", Order = 4)]
        public Brush EntryLineColor { get; set; } = Brushes.Yellow;
        [Browsable(false)] public string EntryLineColorSerializable { get { return Serialize.BrushToString(EntryLineColor); } set { EntryLineColor = Serialize.StringToBrush(value); } }
        
        [Display(Name = "Entry Line Style", GroupName = "5. Visual - Trade Setup", Order = 5)]
        public DashStyleHelper EntryLineStyle { get; set; } = DashStyleHelper.Solid;
        
        [Range(1, 10)] [Display(Name = "Entry Line Width", GroupName = "5. Visual - Trade Setup", Order = 6)]
        public int EntryLineWidth { get; set; } = 1;

        // Stop Loss Properties
        [XmlIgnore] [Display(Name = "Stop Loss Color", GroupName = "5. Visual - Trade Setup", Order = 7)]
        public Brush StopLossColor { get; set; } = Brushes.White;
        [Browsable(false)] public string StopLossColorSerializable { get { return Serialize.BrushToString(StopLossColor); } set { StopLossColor = Serialize.StringToBrush(value); } }

        [Display(Name = "Stop Loss Style", GroupName = "5. Visual - Trade Setup", Order = 8)]
        public DashStyleHelper StopLossStyle { get; set; } = DashStyleHelper.Dash; 

        [Range(1, 10)] [Display(Name = "Stop Loss Width", GroupName = "5. Visual - Trade Setup", Order = 9)]
        public int StopLossWidth { get; set; } = 2;

        // Profit Target Properties
        [XmlIgnore] [Display(Name = "TP Color", GroupName = "5. Visual - Trade Setup", Order = 10)]
        public Brush ProfitTargetColor { get; set; } = Brushes.LimeGreen;
        [Browsable(false)] public string ProfitTargetColorSerializable { get { return Serialize.BrushToString(ProfitTargetColor); } set { ProfitTargetColor = Serialize.StringToBrush(value); } }

        [Display(Name = "TP Style", GroupName = "5. Visual - Trade Setup", Order = 11)]
        public DashStyleHelper ProfitTargetStyle { get; set; } = DashStyleHelper.Solid;

        [Range(1, 10)] [Display(Name = "TP Width", GroupName = "5. Visual - Trade Setup", Order = 12)]
        public int ProfitTargetWidth { get; set; } = 2;
        
        [NinjaScriptProperty]
        [Display(Name = "Show Debug Labels", Description = "Show technical debug info on chart (coordinates, etc)", Order = 2, GroupName = "7. Data Log & Debug")]
        public bool ShowDebugLabels { get; set; }
        


        [NinjaScriptProperty]
        [Display(Name = "🔴 LIVE TRADING MODE (Append)", Description = "IF TRUE: Uses live_log.csv and APPENDS data (Safe for Real Money). IF FALSE: Uses backtest_log.csv and DELETES on start (For Backtesting).", Order = 0, GroupName = "7. Data Log & Debug")]
        public bool LiveTradingMode { get; set; } = false;

        [Display(Name="ENABLE Visual Indicator", Description="Show the underlying indicator on the chart/strategy analyzer", GroupName="1. Strategy - Main", Order=0)]
        public bool EnableVisualIndicator { get; set; } = true;

        [Display(Name="Export Chart Data", Description="If true, saves 1-min OHLC data to CSV for Web Replay. (Warning: Slows down Backtest slightly)", GroupName="7. Data Log & Debug", Order=1)]
        public bool ExportChartData
        {
            get { return exportChartData; }
            set { exportChartData = value; }
        }

        [Display(Name="Use Exchange Time", Description="If true, adapts start times to exchange timezone/DST.", GroupName="3. Sessions", Order=0)]
        public bool UseExchangeTime { get; set; } = false;

        [Display(Name="Asia Start", GroupName="3. Sessions")] public string SessionAsiaStart { get; set; } = "19:00";
        [Display(Name="Asia End", GroupName="3. Sessions")] public string SessionAsiaEnd { get; set; } = "03:00";
        [Display(Name="Show Asia", GroupName="3. Sessions")] public bool SessionShowAsia { get; set; } = true;
        [XmlIgnore] [Display(Name="Asia Color", GroupName="3. Sessions")] public Brush SessionAsiaColor { get; set; } = Brushes.DodgerBlue;
        [Browsable(false)] public string SessionAsiaColorSerializable { get { return Serialize.BrushToString(SessionAsiaColor); } set { SessionAsiaColor = Serialize.StringToBrush(value); } }
        
        [Display(Name="Europe Start", GroupName="3. Sessions")] public string SessionEuropeStart { get; set; } = "03:00";
        [Display(Name="Europe End", GroupName="3. Sessions")] public string SessionEuropeEnd { get; set; } = "11:30";
        [Display(Name="Show Europe", GroupName="3. Sessions")] public bool SessionShowEurope { get; set; } = true;
        [XmlIgnore] [Display(Name="Europe Color", GroupName="3. Sessions")] public Brush SessionEuropeColor { get; set; } = Brushes.Cyan;
        [Browsable(false)] public string SessionEuropeColorSerializable { get { return Serialize.BrushToString(SessionEuropeColor); } set { SessionEuropeColor = Serialize.StringToBrush(value); } }
        
        [Display(Name="US Start", GroupName="3. Sessions")] public string SessionUSStart { get; set; } = "09:30";
        [Display(Name="US End", GroupName="3. Sessions")] public string SessionUSEnd { get; set; } = "16:15";
        [Display(Name="Show US", GroupName="3. Sessions")] public bool SessionShowUS { get; set; } = true;
        [XmlIgnore] [Display(Name="US Color", GroupName="3. Sessions")] public Brush SessionUSColor { get; set; } = Brushes.Magenta;
        [Browsable(false)] public string SessionUSColorSerializable { get { return Serialize.BrushToString(SessionUSColor); } set { SessionUSColor = Serialize.StringToBrush(value); } }
        
        // Indicator Visual Properties
        [XmlIgnore] [Display(Name = "High VWAP Color", GroupName = "6. Visual - VWAP", Order = 20)]
        public Brush HighVWAPColor { get; set; } = Brushes.Gray;
        [Browsable(false)] public string HighVWAPColorSerializable { get { return Serialize.BrushToString(HighVWAPColor); } set { HighVWAPColor = Serialize.StringToBrush(value); } }

        [XmlIgnore] [Display(Name = "Low VWAP Color", GroupName = "6. Visual - VWAP", Order = 21)]
        public Brush LowVWAPColor { get; set; } = Brushes.Gray;
        [Browsable(false)] public string LowVWAPColorSerializable { get { return Serialize.BrushToString(LowVWAPColor); } set { LowVWAPColor = Serialize.StringToBrush(value); } }
        
        [XmlIgnore] [Display(Name = "Label Background Color", GroupName = "6. Visual - VWAP", Order = 22)]
        public Brush LabelBackgroundColor { get; set; } = Brushes.Black;
        [Browsable(false)] public string LabelBackgroundColorSerializable { get { return Serialize.BrushToString(LabelBackgroundColor); } set { LabelBackgroundColor = Serialize.StringToBrush(value); } }

        [Display(Name = "High VWAP Text", GroupName = "6. Visual - VWAP", Order = 23)]
        public string HighVWAPText { get; set; } = "High VWAP";
        
        [Display(Name = "Low VWAP Text", GroupName = "6. Visual - VWAP", Order = 24)]
        public string LowVWAPText { get; set; } = "Low VWAP";


        [Range(1, 365)]
        [Display(Name = "Max History Days", Description = "Ignore levels older than X days", GroupName = "1. Strategy - Main", Order = 24)]
        public int MaxHistoryDays { get; set; } = 5;

        [Range(1, 10)]
        [Display(Name="Max Entries Per Level", Description="Number of times the strategy can trade a single level per session.", Order=6, GroupName="2. Strategy - Risk")]
        public int MaxEntriesPerLevel { get; set; } = 1;



        [Display(Name = "Show Labels", Description = "Show VWAP text labels on the chart", GroupName = "Visual Indicator", Order = 27)]
        public bool ShowLabels { get; set; } = true;

        [Display(Name = "Show Days Ago", Description = "Show 'X Days Ago' text next to levels", GroupName = "Visual Indicator", Order = 28)]
        public bool ShowDaysAgo { get; set; } = true;

        [Display(Name = "Extend Lines Until Touch", Description = "Keep lines extending right until price touches them", GroupName = "Visual Indicator", Order = 29)]
        public bool ExtendLinesUntilTouch { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Show Countdown", GroupName = "Visual Indicator", Order = 30)]
        public bool ShowCountdown { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Count Down Mode", GroupName = "Visual Indicator", Order = 31)]
        public bool CountDown { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Show Percent", GroupName = "Visual Indicator", Order = 32)]
        public bool ShowPercent { get; set; }
        
        #endregion


        #region ICustomTypeDescriptor Members
        public AttributeCollection GetAttributes() { return TypeDescriptor.GetAttributes(GetType()); }
        public string GetClassName() { return TypeDescriptor.GetClassName(GetType()); }
        public string GetComponentName() { return TypeDescriptor.GetComponentName(GetType()); }
        public TypeConverter GetConverter() { return TypeDescriptor.GetConverter(GetType()); }
        public EventDescriptor GetDefaultEvent() { return TypeDescriptor.GetDefaultEvent(GetType()); }
        public PropertyDescriptor GetDefaultProperty() { return TypeDescriptor.GetDefaultProperty(GetType()); }
        public object GetEditor(Type editorBaseType) { return TypeDescriptor.GetEditor(GetType(), editorBaseType); }
        public EventDescriptorCollection GetEvents(Attribute[] attributes) { return TypeDescriptor.GetEvents(GetType(), attributes); }
        public EventDescriptorCollection GetEvents() { return TypeDescriptor.GetEvents(GetType()); }
        public PropertyDescriptorCollection GetProperties(Attribute[] attributes)
        {
            PropertyDescriptorCollection orig = TypeDescriptor.GetProperties(GetType(), attributes);
            List<PropertyDescriptor> filtered = new List<PropertyDescriptor>();

            foreach (PropertyDescriptor pd in orig)
            {
                // Logic:
                // If FixedContracts -> Hide RiskPercent, MinContracts, MaxContracts
                // If RiskCalculated -> Hide Contracts
                
                if (RiskMethod == RiskManagementModeBacktest.FixedContracts)
                {
                    if (pd.Name == "RiskPercent" || pd.Name == "MinContracts" || pd.Name == "MaxContracts") continue; 
                }
                else if (RiskMethod == RiskManagementModeBacktest.RiskCalculated)
                {
                     if (pd.Name == "Contracts") continue;
                }
                
                filtered.Add(pd);
            }
            return new PropertyDescriptorCollection(filtered.ToArray());
        }
        public PropertyDescriptorCollection GetProperties() { return GetProperties(null); }
        public object GetPropertyOwner(PropertyDescriptor pd) { return this; }
        #endregion
}
}
