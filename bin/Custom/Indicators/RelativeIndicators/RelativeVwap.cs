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
using NinjaTrader.NinjaScript.AddOns; // v3.0.3: RelativeMCP — this.RLog() + RelativeIndicatorRegistry
#endregion

namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
    public enum TradeDirectionMode { Both, LongOnly, ShortOnly }
    public enum VwapPriceMethod { Close, Typical, OHLC4 }
    public enum LabelMode { Default, Simple, Custom }
    public enum PersonalityMode { Intraday, Weekly, Monthly, Quarterly, Yearly }
    public enum TouchStudyFilterMode { All, ConfigA, ConfigB, ConfigC, ConfigD, ConfigBC, ConfigCD, ConfigAD }
    public enum TouchStudyTemplate { Custom, Auto, Estudio, Conservador, Equilibrado, Agresivo, MaxTrades, BajaVolatilidad }

    public partial class RelativeVwap : Indicator
    {
        // ========== VERSION ==========
        private const string VERSION = "3.3.0";
        // ==============================

        // v3.1.2: Static shared data for companion indicators (RelativeVwapHealth)
        // v3.3.7: Indexado por instrumento para soportar múltiples charts simultáneos
        private static volatile RelativeVwap _lastInstance;
        public static RelativeVwap LastInstance { get { return _lastInstance; } }

        // Datos compartidos por instrumento (key = Instrument.FullName)
        private static readonly Dictionary<string, double>   SharedHighHealthMap   = new Dictionary<string, double>();
        private static readonly Dictionary<string, double>   SharedLowHealthMap    = new Dictionary<string, double>();
        private static readonly Dictionary<string, double>   SharedHighVWAPMap     = new Dictionary<string, double>();
        private static readonly Dictionary<string, double>   SharedLowVWAPMap      = new Dictionary<string, double>();
        private static readonly Dictionary<string, int>      SharedHighAnchorMap   = new Dictionary<string, int>();
        private static readonly Dictionary<string, int>      SharedLowAnchorMap    = new Dictionary<string, int>();

        // v3.3.7: Arrays de scores por barra, por instrumento
        private static readonly Dictionary<string, double[]> HistHealthHighMap     = new Dictionary<string, double[]>();
        private static readonly Dictionary<string, double[]> HistHealthLowMap      = new Dictionary<string, double[]>();
        // v3.3.7: Array de decisiones de pintura (1=demand stronger, -1=supply stronger, 0=no pintado)
        private static readonly Dictionary<string, sbyte[]>  HistCandleColorMap    = new Dictionary<string, sbyte[]>();
        private const int HIST_HEALTH_SIZE = 100000;

        // Helpers estáticos para que RelativeVwapHealth lea por instrumento
        public static double GetSharedHighHealth(string key) { double v; return SharedHighHealthMap.TryGetValue(key, out v) ? v : 0; }
        public static double GetSharedLowHealth(string key)  { double v; return SharedLowHealthMap.TryGetValue(key, out v) ? v : 0; }
        public static double GetSharedHighVWAP(string key)   { double v; return SharedHighVWAPMap.TryGetValue(key, out v) ? v : 0; }
        public static double GetSharedLowVWAP(string key)    { double v; return SharedLowVWAPMap.TryGetValue(key, out v) ? v : 0; }
        public static int    GetSharedHighAnchor(string key) { int v;    return SharedHighAnchorMap.TryGetValue(key, out v) ? v : -1; }
        public static int    GetSharedLowAnchor(string key)  { int v;    return SharedLowAnchorMap.TryGetValue(key, out v) ? v : -1; }
        public static double[] GetHistHealthHigh(string key) { double[] v; return HistHealthHighMap.TryGetValue(key, out v) ? v : null; }
        public static double[] GetHistHealthLow(string key)  { double[] v; return HistHealthLowMap.TryGetValue(key, out v) ? v : null; }
        public static sbyte[]  GetHistCandleColor(string key){ sbyte[] v;  return HistCandleColorMap.TryGetValue(key, out v) ? v : null; }

        // Key del instrumento actual (set en DataLoaded)
        private string _instrumentKey;

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

        // v1.0.49: Tracking for Internal Level VWAPs (for continuation trades)
        private double internalHighPV;       // PV for internal high VWAP
        private double internalHighVol;      // Volume for internal high VWAP
        private int internalHighBarIdx;      // Bar where internal high VWAP anchored
        private double internalHighPrice;    // Price where internal high VWAP anchored
        private bool hasInternalHighVWAP;    // True if internal high VWAP exists
        private double internalLowPV;        // PV for internal low VWAP
        private double internalLowVol;       // Volume for internal low VWAP
        private int internalLowBarIdx;       // Bar where internal low VWAP anchored
        private double internalLowPrice;     // Price where internal low VWAP anchored
        private bool hasInternalLowVWAP;     // True if internal low VWAP exists
        private double internalHighExtreme;  // v2.0.0: Track highest high for internal re-anchoring
        private double internalLowExtreme;   // v2.0.0: Track lowest low for internal re-anchoring

        private int tradeIdCounter = 0; // V_VISUAL: Trade Counter
        
        // Daily High/Low for finding anchor points
        private double currentDayHigh;
        private double currentDayLow;
        private bool highHasTakenRelevant;
        private bool lowHasTakenRelevant;

        // v2.1.0: Track multiple breaks in same bar to prioritize labels
        private List<SessionLevelInfo> _highBreaks = new List<SessionLevelInfo>();
        private List<SessionLevelInfo> _lowBreaks = new List<SessionLevelInfo>();

        // Signal Logic State
        private double highCumPV, highCumVol;
        private double lowCumPV, lowCumVol;
        private bool highDetached;
        private bool lowDetached;
        private bool _highJustReset;  // v1.0.2: Skip accumulation on anchor bar
        private bool _lowJustReset;   // v1.0.2: Skip accumulation on anchor bar
        private bool _internalHighJustReset;  // v1.0.49: Skip accumulation on internal VWAP creation bar
        private bool _internalLowJustReset;   // v1.0.49: Skip accumulation on internal VWAP creation bar
        private DateTime _lastTradingDay = DateTime.MinValue;  // v1.0.53: Track day changes for internal VWAP reset
        // v3.3.1: _currentSessionStartBarIdx eliminado — se escribía pero nunca se leía
        private bool highSignalFired;
        private bool lowSignalFired;
        private double currentHighVWAP;
        private double currentLowVWAP;
        // v3.3.7: _prevBarHighVwap/_prevBarLowVwap eliminados — ya no se usan con tracking continuo
        private bool hasHighVWAP;
        private bool hasLowVWAP;
        private bool highSignal2Fired; // V_SIGNAL_2 One-Shot Flag
        private bool lowSignal2Fired;  // V_SIGNAL_2 One-Shot Flag
        private int _lastSignal2BarIdx = -1; // v3.0.1: Bar of last Signal 2 (any side) for overlap prevention
        private int highAnchorSequence; // V_SIGNAL_2 Sequence Counter
        private int lowAnchorSequence;  // V_SIGNAL_2 Sequence Counter
        private int lastSignaledHighAnchorBar = -1; // V_SIGNAL_2 Anchor Tracker
        private int lastSignaledLowAnchorBar = -1;  // V_SIGNAL_2 Anchor Tracker
        private SessionLevelInfo lastUnlockedHighSession = null;
        private SessionLevelInfo lastUnlockedLowSession = null;
        private SessionLevelInfo currentHighAnchorSession = null; // v1.0.43: Session that created current HIGH anchor
        private SessionLevelInfo currentLowAnchorSession = null;  // v1.0.43: Session that created current LOW anchor
        
        // V_FIX_LIVE: Persistent Signal 2 Painting State
        private int highSignal2BarIdx = -1; // Tracks specific bar index for High Signal 2
        private int lowSignal2BarIdx = -1;  // Tracks specific bar index for Low Signal 2
        
        // v2.1.0: Internal Signal 2 Trackers
        private bool internalHighSignal2Fired = false;
        private bool internalLowSignal2Fired = false;
        private int internalHighSignal2Count = 0; // v2.1.0: Counter for max attempts
        private int internalLowSignal2Count = 0;  // v2.1.0: Counter for max attempts
        private int lastSignaledInternalHighBar = -1;
        private int lastSignaledInternalLowBar = -1;
        private int internalHighSignal2BarIdx = -1; // For painting
        private int internalLowSignal2BarIdx = -1;  // For painting

        // v1.0.24: Tracking for movable "Liquidity Grabbed" label
        private int highLiqGrabBarIdx = -1;      // Bar where High liquidity grab label is drawn
        private double highLiqGrabExtreme = 0;   // Highest price reached since liquidity grab
        private string highLiqGrabSessionName = ""; // Session name for the label tag
        private int lowLiqGrabBarIdx = -1;       // Bar where Low liquidity grab label is drawn
        private double lowLiqGrabExtreme = 0;    // Lowest price reached since liquidity grab
        private string lowLiqGrabSessionName = ""; // Session name for the label tag
        // v1.0.45: Liquidity Grabbed sequence and lock state
        private bool highLiqGrabLocked = false;  // Locked when Signal 2 fires (label freezes at pivot)
        private bool lowLiqGrabLocked = false;   // Locked when Signal 2 fires
        private int highLiqGrabSequence = 1;     // Sequence number (01, 02, 03, etc.)
        private int lowLiqGrabSequence = 1;      // Sequence number
        // v1.0.48: Track last bar where sequence was reset to prevent multiple resets per bar
        private int lastHighSeqResetBar = -1;    // Last bar where highAnchorSequence was reset
        private int lastLowSeqResetBar = -1;     // Last bar where lowAnchorSequence was reset
        // v1.0.49: Track if grabbed level is internal (not day extreme)
        private bool highLiqGrabIsInternal = false;  // True if session.High < currentDayHigh (internal level)
        private bool lowLiqGrabIsInternal = false;   // True if session.Low > currentDayLow (internal level)

        // Session Levels Tracking
        public class SessionLevelInfo
        {
            public string Name;
            public DateTime StartTime;
            public DateTime EndTime;
            public double High;
            public double Low;
            public int StartBarIdx;
            public int HighBarIdx = -1; // Exact bar index of High
            public int LowBarIdx = -1;  // Exact bar index of Low
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
        private List<SessionLevelInfo> periodSessions;  // v3.0.0: Para personalidades de periodo (Weekly, Monthly, Quarterly, Yearly)
        private List<int> periodDividerBars;  // v3.0.0: Barras donde inician nuevos periodos (para líneas divisorias)

        // v3.0.4: US First Hour Opening Range tracking
        private double _usFirstHourHigh;
        private double _usFirstHourLow;
        private int _usFirstHourStartBarIdx = -1;
        private int _usFirstHourEndBarIdx = -1;
        private bool _usFirstHourComplete;
        private DateTime _usFirstHourDate = DateTime.MinValue;
        private struct FirstHourRange
        {
            public double High;
            public double Low;
            public int StartBarIdx;
            public int EndBarIdx;
            public DateTime Date;
        }
        private List<FirstHourRange> _historicalFirstHours = new List<FirstHourRange>();

        // V_SMART: Public Accessors for Strategy Rendering
        [Browsable(false)] public List<SessionLevelInfo> AsiaSessions { get { return asiaSessions; } }
        [Browsable(false)] public List<SessionLevelInfo> EuropeSessions { get { return europeSessions; } }
        [Browsable(false)] public List<SessionLevelInfo> USSessions { get { return usSessions; } }
        [Browsable(false)] public List<SessionLevelInfo> PeriodSessions { get { return periodSessions; } }

        private DateTime asiaStart, asiaEnd;
        private DateTime europeStart, europeEnd;
        private DateTime usStart, usEnd;

        // v3.0.5: Touch study record — first touch after significant separation
        private struct FirstTouchRecord
        {
            public int BarIdx;
            public bool TouchedHighVwap;  // true = touched High VWAP, false = touched Low VWAP
            public double HighHealthScore;
            public double LowHealthScore;
            public double VwapPrice;       // VWAP price at touch bar (for label positioning)
            public double TouchPrice;      // Close price at touch bar
            public double ATRValue;
            public double Separation;      // distance in ticks when separation was detected
            public double MFE;             // Max Favorable Excursion in ticks (tracked post-touch)
            public double MAE;             // Max Adverse Excursion in ticks (tracked post-touch)
            public int MFEBars;            // Bars to reach MFE
            public bool MFEComplete;       // True when tracking window complete
            // v3.0.7: Trade simulation fields
            public string Config;          // "A", "B", "C", "D", "-"
            public bool IsEpisodeFirst;    // True = first touch in episode (>N bars gap)
            public int ExitBarIdx;         // Bar where SL/TP/EOD exit (0 = pending)
            public double ExitPrice;       // Price at exit
            public int ExitType;           // 0=pending, 1=TP, 2=SL, 3=EOD
            // v3.1.3: RAW mode fields — uncapped tracking to EOD
            public double RawMFE;          // MFE tracked to EOD (never truncated by SL/TP)
            public double RawMAE;          // MAE tracked to EOD (never truncated by SL/TP)
            public int RawMFEBars;         // Bars to reach RawMFE
            public bool RawComplete;       // True when EOD reached for raw tracking
            public double OtherVwapPrice;  // Price of the opposite VWAP at touch time
            public int EodBarIdx;          // Bar index at EOD (for path computation)
            // v3.2.0: Phase analysis — impulse (separation→peak) and retrace (peak→touch)
            public double ImpulseDelta;    // Cumulative delta during impulse phase
            public double ImpulseVolume;   // Cumulative volume during impulse phase
            public int ImpulseBars;        // Bars from separation to peak distance
            public double RetraceDelta;    // Cumulative delta during retrace phase
            public double RetraceVolume;   // Cumulative volume during retrace phase
            public int RetraceBars;        // Bars from peak to touch
        }

        private struct HistoricalAnchor
        {
            public int StartIdx;
            public int EndIdx;
            public bool WasRelevant;
            public int FirstBreakIdx;
            public Dictionary<int, double> VwapValues; // Store actual VWAP values to prevent diagonal line artifacts
            public bool IsSessionEnd;  // v3.0.3: true = archived at session boundary, false = mid-session re-anchor
            public double HealthScore;      // v3.0.4: VWAP health at archive time (MFE/MAE ratio)
            public int HealthTouchCount;    // v3.0.4: Touch count at archive time
            public List<FirstTouchRecord> FirstTouches; // v3.0.5: First touches after separation
        }

        private int highFirstBreakIdx = -1;
        private int lowFirstBreakIdx = -1;

        private List<HistoricalAnchor> historicalHighs = new List<HistoricalAnchor>();

        private List<HistoricalAnchor> historicalLows = new List<HistoricalAnchor>();

        // v2.0.0: Historical Internal VWAPs
        private List<HistoricalAnchor> historicalInternalHighs = new List<HistoricalAnchor>();
        private List<HistoricalAnchor> historicalInternalLows = new List<HistoricalAnchor>();
        
        // v3.0.5: Active first touches (current VWAPs, before archiving)
        private List<FirstTouchRecord> _activeFirstTouches = new List<FirstTouchRecord>();
        // v3.2.0: Completed touches (moved from active for performance — no longer tracked per bar)
        private List<FirstTouchRecord> _completedFirstTouches = new List<FirstTouchRecord>();

        // V39: Hybrid Logic Variables
        private double _lastVol = 0; // For Tick-based calculation
        private bool _isNewBar = true; // Track new bar for detachment check
        private int debugUpdateCounter = 0; // V_DEBUG: Heartbeat Monitor
        
        // V_NORM: ATR-based Normalization for consistent spacing across instruments
        private NinjaTrader.NinjaScript.Indicators.ATR atr;

        // v3.3.2: Demand/Supply candle painting
        private bool _prevDemandStronger = false;
        private bool _demandSupplyInitialized = false;

        // v3.3.8: Health cross — pendiente estable para filtrar cruces falsos
        private const int SLOPE_LOOKBACK = 15;           // barras para medir pendiente
        private const int DECLINE_LOOKBACK = 5;          // barras para confirmar declive del perdedor
        private double[] _recentDemandScores;             // buffer circular de scores Demand
        private double[] _recentSupplyScores;             // buffer circular de scores Supply
        private int _scoreBufIdx = 0;                     // índice actual en el buffer
        private bool _scoreBufFull = false;               // buffer ya tiene SLOPE_LOOKBACK muestras
        private bool _pendingFlip = false;                // cruce detectado pero esperando confirmación
        private bool _pendingFlipDirection = false;       // true = demand ganó (pending long), false = supply ganó
        private bool _pendingFlipStable = false;          // calidad del cruce pendiente
        private int  _confirmBarsCount = 0;               // v3.3.10: barras transcurridas desde cruce detectado
        private bool _confirmBarsPending = false;          // v3.3.10: esperando confirmación temporal
        private bool _confirmBarsDirection = false;        // v3.3.10: dirección del cruce en espera
        private DateTime _sessionResetTime = DateTime.MinValue; // v3.3.9: hora del último reset de sesión

        // v3.3.8: Health cross export — tracking MFE/MAE post-cruce
        private bool _crossTradeActive = false;
        private bool _crossTradeIsLong = false;           // true = demand won (long), false = supply won (short)
        private bool _crossTradeStable = false;           // true = crecimiento estable, false = zigzag
        private DateTime _crossEntryTime;
        private double _crossEntryPrice = 0;
        private double _crossEntryDemand = 0;
        private double _crossEntrySupply = 0;
        private double _crossEntryDeltaGlobal = 0;
        private double _crossEntryDeltaSession = 0;
        private double _crossEntryATR = 0;
        private double _crossMFE = 0;                     // máximo favorable post-cruce (ticks)
        private double _crossMAE = 0;                     // máximo adverso post-cruce (ticks)
        private int _crossEntryBar = 0;
        private int _crossID = 0;
        private System.Collections.Generic.List<CrossExportRecord> _crossExportRecords
            = new System.Collections.Generic.List<CrossExportRecord>();

        // v2.2.4: Session Delta calculation (internal, no external indicator needed)
        private double _lastBarDelta;        // Delta of current bar (Close-Open)*Volume
        private double _deltaGlobal;         // Full day: Asia start (6pm) to USA end (4pm)
        private double _deltaAsia;           // Asia session only
        private double _deltaEurope;         // Europe start to USA start
        private double _deltaUSA;            // USA session only (9:30-4pm)
        private bool _wasInAsia, _wasInEurope, _wasInUSA;  // Track session transitions

        // v2.2.7: Trend Mode Helper Functions
        /// <summary>
        /// Returns the delta accumulator for the current session (Asia/Europe/USA)
        /// </summary>
        private double GetCurrentSessionDelta()
        {
            if (!CaptureDelta) return 0;

            TimeSpan currentTime = Time[0].TimeOfDay;
            TimeSpan asiaStart = GetTimeByZone(AsiaStartTime);
            TimeSpan asiaEnd = GetTimeByZone(AsiaEndTime);
            TimeSpan europeStart = GetTimeByZone(EuropeStartTime);
            TimeSpan usaStart = GetTimeByZone(USStartTime);
            TimeSpan usaEnd = GetTimeByZone(USEndTime);

            // Session detection (handles overnight sessions)
            bool inAsia = (asiaStart > asiaEnd)
                ? (currentTime >= asiaStart || currentTime < asiaEnd)
                : (currentTime >= asiaStart && currentTime < asiaEnd);
            bool inEurope = (europeStart > usaStart)
                ? (currentTime >= europeStart || currentTime < usaStart)
                : (currentTime >= europeStart && currentTime < usaStart);
            bool inUSA = (currentTime >= usaStart && currentTime < usaEnd);

            // Return delta for current session (priority: USA > Europe > Asia)
            if (inUSA) return _deltaUSA;
            if (inEurope) return _deltaEurope;
            if (inAsia) return _deltaAsia;
            return 0;
        }

        /// <summary>
        /// Checks if trend conditions are met based on delta accumulators and TradingMode setting
        /// Returns true if both DeltaGlobal and current session delta exceed threshold in same direction
        /// v2.2.7: Now respects TradingMode property for manual mode selection
        /// </summary>
        private bool IsTrendMode(out bool isBearish)
        {
            isBearish = false;

            // v2.2.7: Check TradingMode first - allows manual override
            if (TradingMode == TradingModeType.ReversalOnly)
            {
                // User wants only reversals - never activate trend mode
                if (ShowDebugLogs && IsFirstTickOfBar)
                    Print(string.Format("[TREND_MODE] Bar:{0} | FORCED REVERSAL MODE | TradingMode=ReversalOnly", CurrentBar));
                return false;
            }

            if (!CaptureDelta) return false;

            double sessionDelta = GetCurrentSessionDelta();

            // v2.2.7: TrendOnly mode - force trend based on delta direction (ignore threshold)
            if (TradingMode == TradingModeType.TrendOnly)
            {
                if (_deltaGlobal < 0 && sessionDelta < 0)
                    isBearish = true;
                else if (_deltaGlobal > 0 && sessionDelta > 0)
                    isBearish = false;
                else
                    isBearish = _deltaGlobal < 0; // Mixed: use global delta
                return true;
            }

            // Auto mode: Original threshold-based detection
            if (_deltaGlobal < -TrendDeltaThreshold && sessionDelta < -TrendDeltaThreshold)
            {
                isBearish = true;
                return true;
            }
            if (_deltaGlobal > TrendDeltaThreshold && sessionDelta > TrendDeltaThreshold)
            {
                isBearish = false;
                return true;
            }

            return false; // No trend - use reversal mode
        }

        // v1.0.26: File Logging System

        // v1.0.5: Anti-Collision System for Labels (SIMPLIFIED)
        // NOTE: Returns proposedY directly - collision avoidance removed due to visual issues
        private double _highLabelY = double.MinValue;
        private double _lowLabelY = double.MaxValue;
        
        // GetNonCollidingHighY and GetNonCollidingLowY moved to RelativeVwap.Rendering.cs

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

        // LogToFile and AddSignal moved to RelativeVwap.Utilities.cs

        // v3.0.1: Hide indicator label from top-left chart corner
        public override string DisplayName { get { return "RelativeVwap"; } }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = $"RelativeVwap v{VERSION}: VWAP anclado a extremos de sesiÃ³n con seÃ±ales de trading y niveles relativos.";
                Name = "RelativeVwap"; // Restore Production Name
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DisplayInDataBox = false;
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
                if (ShowDebugLogs) Print("RelativeVwap Indicator: OnStateChange (SetDefaults) Reached");

                // V_FIX: Add Plots to ensure Values[0] (High) and Values[1] (Low) exist for Strategy Hookup
                // v1.0.24: PlotStyle.Dot = small markers (nearly invisible), but visible in DataBox
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Dot, "VWAP Hi"); // Values[0]
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Dot, "VWAP Lo"); // Values[1]
                // v1.0.49: Internal VWAPs for continuation trades on internal levels
                // v3.1.2: Transparent plots — visual rendering handled by SharpDX in OnRender (DrawInternalVWAP)
                // Using Orange plots caused duplicate lines overlapping candles
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Dot, "Internal VWAP Hi"); // Values[2]
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Dot, "Internal VWAP Lo"); // Values[3]

                // Defaults
                HighVWAPColor = Brushes.Cyan;
                LowVWAPColor = Brushes.Cyan;
                HistoricalVWAPColor = Brushes.Gray;
                PreviousVWAPColor = Brushes.White;  // v3.0.2: Last historical VWAP pair stands out
                HistoricalVWAPThickness = 2.0f;
                SessionLevelThickness = 2.0f; // v3.0.2: Default session level line thickness

                ShowLabels = true;
                ShowVwapHealth = true;
                ShowDemandSupplyCandles = true;
                ShowTouchStudy = false;
                TouchStudyDays = 3;
                TouchStudySeparationATR = 1.0;
                TouchStudyProximityTicks = 3;
                TouchStudyFilter = TouchStudyFilterMode.All;
                TouchStudySLTicks = 24;
                TouchStudyTPTicks = 38;
                TouchStudyEpisodeGap = 15;
                TouchStudyMaxATR = 0;
                TouchStudyMaxSeparation = 0;
                StudyTemplate = TouchStudyTemplate.Custom;
                ExportTouchStudyCSV = false;

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

                // Debug Performance Switch
                // Debug Performance Switch
                ShowDebugLogs = false;

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
                USEndTime = "18:00";       // v2.2.4: 6PM EST → EOD exit at 5:59:30 PM EST
                ShowUS = true;
                USLineColor = Brushes.Blue;
                USLabelColor = Brushes.White;
                ShowUSHigh = true;
                ShowUSLow = true;

                // v3.0.4: US First Hour Rectangle
                ShowUSFirstHour = true;
                USFirstHourMinutes = 15;
                USFirstHourColor = Brushes.White;
                USFirstHourOpacity = 10;

                UseExchangeTime = true;    // Default ON
                
                // VWAP Method (v1.0.1)
                VwapMethod = VwapPriceMethod.Close;  // Default: Close (matches SessionLevels strategy)
                
                EnableAlerts = false;
                AlertSound = "mzpack_alert4.wav";

                
                ShowDaysAgo = true; // Default True
                ShowDebugLogs = false; // Default OFF para rendimiento

                // v3.3.0: Market Structure Detection defaults (NT Swing fractal)
                ShowStructure = false;
                StructureSwingStrength = 3;          // Barras a cada lado para confirmar fractal
                StructureMinSwingTicks = 0;          // Sin filtro de tamano minimo
                StructureUptrendColor = Brushes.LimeGreen;
                StructureDowntrendColor = Brushes.Crimson;
                StructureDTDBColor = Brushes.Yellow;
                StructureLabelColor = Brushes.White;
                StructureLabelHH = "HH";
                StructureLabelHL = "HL";
                StructureLabelLH = "LH";
                StructureLabelLL = "LL";
                StructureLabelDT = "DT";
                StructureLabelDB = "DB";
                StructureLineThickness = 1.5f;
                StructureLabelSize = 10;
                ShowStructureLabels = true;
                ShowStructureLines = false;          // Zigzag OFF por default
                ShowStructureLabelBg = false;        // Fondos negros OFF
                StructureMaxSwings = 50;
                
                if (ShowDebugLogs) Print("RelativeVwap Indicator: OnStateChange (SetDefaults) Reached - VERSION " + VERSION);
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(14); // V_NORM: Correct Initialization

                // v3.4.0: TDU Price Action integration
                if (EnableTDUTrading)
                    InitializeTDU();

                // v2.2.4: Session Delta (internal calculation)
                _lastBarDelta = 0;
                _deltaGlobal = 0;
                _deltaAsia = 0;
                _deltaEurope = 0;
                _deltaUSA = 0;
                _wasInAsia = _wasInEurope = _wasInUSA = false;
                if (CaptureDelta && ShowDebugLogs)
                {
                    Print("RelativeVwap: CaptureDelta habilitado - 4 deltas: Global, Asia, Europe, USA");
                }

                // Version Info
                Print("RelativeVwap v" + VERSION + " | " + Instrument.FullName);

                // v3.0.2: Chart Toolbar Integration
                if (ChartControl != null)
                    ChartControl.Dispatcher.InvokeAsync((Action)(() => AddToolBar()));

                // v1.0.26: Initialize Log File Path

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
                            updateTimer = new System.Timers.Timer(500); // PHASE 5: Reduced to 2Hz (was 4Hz) - sufficient for countdown
                            updateTimer.Elapsed += OnTimerTick;
                            updateTimer.AutoReset = true;
                            updateTimer.Enabled = true;
                        }
                    }
                }
                try
                {
                    if (ShowDebugLogs) Print("RelativeVwap: Entering State.DataLoaded...");
                    _lastInstance = this; // v3.1.2: Register for companion indicators
                    // v3.3.0: Market Structure Detection
                    InitStructure();
                    sessionIterator = new SessionIterator(Bars);
                    // On initial load, clear lists
                    if (historicalHighs != null) historicalHighs.Clear();
                    if (historicalLows != null) historicalLows.Clear();
                    
                    asiaSessions = new List<SessionLevelInfo>();
                    europeSessions = new List<SessionLevelInfo>();
                    usSessions = new List<SessionLevelInfo>();
                    periodSessions = new List<SessionLevelInfo>();  // v3.0.0: Period personalities
                    periodDividerBars = new List<int>();  // v3.0.0: Track period start bars

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
                    
                    InitializeTrading(); // Initialize Account and Events
                    InitializeVoiceAlerts(); // v1.0.50: Initialize Text-to-Speech

                    // v3.3.8: Inicializar arrays por instrumento — NO recrear si ya existen (F5 preserva datos)
                    _instrumentKey = Instrument.FullName;
                    if (!HistHealthHighMap.ContainsKey(_instrumentKey))
                        HistHealthHighMap[_instrumentKey] = new double[HIST_HEALTH_SIZE];
                    if (!HistHealthLowMap.ContainsKey(_instrumentKey))
                        HistHealthLowMap[_instrumentKey]  = new double[HIST_HEALTH_SIZE];
                    if (!HistCandleColorMap.ContainsKey(_instrumentKey))
                        HistCandleColorMap[_instrumentKey] = new sbyte[HIST_HEALTH_SIZE];

                    // v3.3.8: Buffers para pendiente de health scores
                    _recentDemandScores = new double[SLOPE_LOOKBACK];
                    _recentSupplyScores = new double[SLOPE_LOOKBACK];
                    _scoreBufIdx = 0;
                    _scoreBufFull = false;

                    if (ShowDebugLogs) Print("RelativeVwap: State.DataLoaded OK.");
                }
                catch (Exception ex)
                {
                    Print("RelativeVwap Indicator CRASH in State.DataLoaded: " + ex.Message);
                }
            }
            else if (State == State.Historical)
            {
                if (ShowDebugLogs) Print("RelativeVwap: Entering State.Historical...");
            }
            else if (State == State.Configure)
            {
                if (ShowDebugLogs) Print("RelativeVwap: Entering State.Configure...");

                // v3.2.0: Apply study template if not Custom
                if (StudyTemplate != TouchStudyTemplate.Custom)
                    ApplyStudyTemplate(StudyTemplate);

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
                // v1.0.49: Configure internal VWAP plots (visible as orange dashed lines)
                // Note: DashStyle already configured in AddPlot (line 360), only set Brush and Width here
                // v3.1.2: Internal VWAP plots are now Transparent (SharpDX handles rendering)
                // Plots[2] and Plots[3] only hold data for series access — no visual output
                Plots[2].Brush = Brushes.Transparent;
                Plots[3].Brush = Brushes.Transparent;

            }
            else if (State == State.Terminated)
            {
                // Process all pending signals before terminating
                ProcessPendingSignals();

                // v3.0.9: Export touch study CSV independently (not gated by _signalsProcessed)
                if (ExportTouchStudyCSV)
                {
                    try { WriteTouchStudyCsv(); }
                    catch (Exception csvEx) { Print("[TOUCH_STUDY] CSV export error: " + csvEx.Message); }
                }

                // v3.3.10: Cerrar trade real al terminar
                if (_healthCrossTradeOpen)
                    CloseHealthCrossTrade("Terminated");

                // v3.4.0: Cerrar trade TDU al terminar
                if (_tduTradeActive)
                    CloseTDUTrade("Terminated");
                if (ExportTDUCSV)
                {
                    try { ExportTDURecords(); }
                    catch (Exception tduEx) { Print("[TDU_EXPORT] CSV export error: " + tduEx.Message); }
                }

                // v3.3.8: Export health cross CSV
                if (ExportHealthCrossCSV)
                {
                    if (_crossTradeActive) CloseCrossTradeTracking("EOD");
                    try { ExportCrossRecords(); }
                    catch (Exception csvEx) { Print("[CROSS_EXPORT] CSV export error: " + csvEx.Message); }
                }

                if (updateTimer != null)
                {
                    updateTimer.Enabled = false;
                    updateTimer.Elapsed -= OnTimerTick;
                    updateTimer.Dispose();
                    updateTimer = null;
                }

                TerminateTrading(); // Cleanup Trading Resources
                DisposeVoiceAlerts(); // v1.0.50: Cleanup Text-to-Speech
                DisposeCachedBrushes(); // PHASE 1.4: Cleanup SharpDX resources

                // v3.0.2: Remove Chart Toolbar
                if (ChartControl != null)
                    ChartControl.Dispatcher.InvokeAsync((Action)(() => RemoveToolBar()));
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
            _lastSignal2BarIdx = -1; // v3.0.1: Reset overlap prevention
            highAnchorSequence = 0;
            lowAnchorSequence = 0;
            lastSignaledHighAnchorBar = -1;
            lastSignaledLowAnchorBar = -1;
            // highCumPV = 0; highCumVol = 0; // RESET DISABLED - not used anymore
            // lowCumPV = 0; lowCumVol = 0; // RESET DISABLED - not used anymore

            // v2.2.4: Reset all deltas at start of trading day (Asia start)
            _deltaGlobal = 0;
            _deltaAsia = 0;
            _deltaEurope = 0;
            _deltaUSA = 0;
            
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
            // v1.0.45: Reset Liquidity Grab sequence and lock
            highLiqGrabLocked = false;
            lowLiqGrabLocked = false;
            highLiqGrabSequence = 1;
            lowLiqGrabSequence = 1;
            // v1.0.49: Reset internal level tracking
            highLiqGrabIsInternal = false;
            lowLiqGrabIsInternal = false;
            // v1.0.53: DO NOT reset internal VWAPs here - they should persist across intraday sessions
            // Only reset at day change or when touched
            // internalHighBarIdx = -1;
            // internalHighPV = 0;
            // internalHighVol = 0;
            // internalHighPrice = 0;
            // hasInternalHighVWAP = false;
            // internalLowBarIdx = -1;
            // internalLowPV = 0;
            // internalLowVol = 0;
            // internalLowPrice = 0;
            // hasInternalLowVWAP = false;

            // v3.3.9: Reset health cross state at session start
            if (_crossTradeActive && ExportHealthCrossCSV)
                CloseCrossTradeTracking("EOD");
            // v3.3.10: Cerrar trade real por EOD
            if (_healthCrossTradeOpen)
                CloseHealthCrossTrade("EOD");
            // v3.4.0: Cerrar trade TDU por EOD
            if (_tduTradeActive)
                CloseTDUTrade("EOD");
            _pendingFlip = false;
            _confirmBarsPending = false;
            _confirmBarsCount = 0;
            _scoreBufIdx = 0;
            _scoreBufFull = false;
            _demandSupplyInitialized = false;

            if (ShowDebugLabels)
                Draw.Text(this, "Reset" + CurrentBar, "RESET", 0, Low[0] - 5 * TickSize, Brushes.Red);

            if (ShowDebugLogs)
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

        // v3.0.0: Track personality changes
        private PersonalityMode _lastPersonality = PersonalityMode.Intraday;

        /// <summary>v3.2.0: Aplica template con parametros optimizados del estudio Big Data 2025 (87K toques MES)</summary>
        /// <summary>v3.2.0: Aplica template con parametros optimizados del estudio Big Data 2024 (100K toques MES)</summary>
        private void ApplyStudyTemplate(TouchStudyTemplate template)
        {
            // Auto mode: apply NORMAL defaults now, UpdateAutoTemplate() adjusts per-bar based on ATR
            if (template == TouchStudyTemplate.Auto)
            {
                HealthStrongThreshold = 2.0;
                HealthWeakThreshold = 1.5;
                TouchStudySLTicks = 40;
                TouchStudyTPTicks = 60;
                TouchStudyFilter = TouchStudyFilterMode.ConfigCD;
                _showCfgA = false;
                _showCfgB = false;
                _showCfgC = true;
                _showCfgD = true;
                return;
            }

            switch (template)
            {
                case TouchStudyTemplate.Estudio:
                    // Modo captura: abre todo para recolectar datos crudos de cualquier instrumento
                    HealthStrongThreshold = 0;
                    HealthWeakThreshold = 0;
                    TouchStudyFilter = TouchStudyFilterMode.All;
                    TouchStudySLTicks = 100;   // SL amplio para capturar MFE/MAE real
                    TouchStudyTPTicks = 100;   // TP amplio
                    TouchStudyMaxATR = 0;
                    TouchStudyMaxSeparation = 0;
                    TouchStudyEpisodeGap = 1;  // Capturar todos los toques sin gap
                    ExportTouchStudyCSV = true;
                    TouchStudyRawMode = true;  // MFE/MAE sin truncar + snapshots + fase
                    AnalyzeAllSignals = true;
                    CaptureDelta = true;
                    ShowTouchStudy = true;
                    _showCfgA = true;
                    _showCfgB = true;
                    _showCfgC = true;
                    _showCfgD = true;
                    return; // skip the C+D override at the bottom

                case TouchStudyTemplate.Conservador:
                    HealthStrongThreshold = 2.5;
                    HealthWeakThreshold = 2.0;
                    TouchStudyFilter = TouchStudyFilterMode.ConfigCD;
                    TouchStudySLTicks = 40;
                    TouchStudyTPTicks = 50;
                    TouchStudyMaxATR = 0;
                    TouchStudyMaxSeparation = 0;
                    break;

                case TouchStudyTemplate.Equilibrado:
                    HealthStrongThreshold = 2.0;
                    HealthWeakThreshold = 1.5;
                    TouchStudyFilter = TouchStudyFilterMode.ConfigCD;
                    TouchStudySLTicks = 40;
                    TouchStudyTPTicks = 60;
                    TouchStudyMaxATR = 0;
                    TouchStudyMaxSeparation = 0;
                    break;

                case TouchStudyTemplate.Agresivo:
                    HealthStrongThreshold = 2.0;
                    HealthWeakThreshold = 1.5;
                    TouchStudyFilter = TouchStudyFilterMode.ConfigCD;
                    TouchStudySLTicks = 35;
                    TouchStudyTPTicks = 40;
                    TouchStudyMaxATR = 0;
                    TouchStudyMaxSeparation = 0;
                    break;

                case TouchStudyTemplate.MaxTrades:
                    HealthStrongThreshold = 1.5;
                    HealthWeakThreshold = 1.0;
                    TouchStudyFilter = TouchStudyFilterMode.ConfigCD;
                    TouchStudySLTicks = 40;
                    TouchStudyTPTicks = 40;
                    TouchStudyMaxATR = 0;
                    TouchStudyMaxSeparation = 0;
                    break;

                case TouchStudyTemplate.BajaVolatilidad:
                    HealthStrongThreshold = 2.0;
                    HealthWeakThreshold = 1.5;
                    TouchStudyFilter = TouchStudyFilterMode.ConfigCD;
                    TouchStudySLTicks = 40;
                    TouchStudyTPTicks = 60;
                    TouchStudyMaxATR = 2.0;
                    TouchStudyMaxSeparation = 0;
                    break;
            }

            // Todos los templates usan solo C+D
            _showCfgA = false;
            _showCfgB = false;
            _showCfgC = true;
            _showCfgD = true;

            if (ShowDebugLogs)
                Print(string.Format("[TEMPLATE] Applied '{0}': Filter={1}, SL={2}, TP={3}, Strong={4}, Weak={5}, MaxATR={6}",
                    template, TouchStudyFilter, TouchStudySLTicks, TouchStudyTPTicks, HealthStrongThreshold, HealthWeakThreshold, TouchStudyMaxATR));
        }

        // v3.2.0: Auto mode — adapta parametros segun ATR actual
        private string _lastAutoMode = "";

        private void UpdateAutoTemplate()
        {
            if (StudyTemplate != TouchStudyTemplate.Auto) return;
            if (atr == null || CurrentBar < 14) return;

            double currentATR = atr[0];
            string newMode;

            if (currentATR < 1.5)
                newMode = "BAJA_VOL";
            else if (currentATR <= 3.0)
                newMode = "NORMAL";
            else
                newMode = "ALTA_VOL";

            // Solo aplicar cambios cuando el modo realmente cambia (perf: evita 7 asignaciones por barra)
            if (newMode == _lastAutoMode) return;
            _lastAutoMode = newMode;

            switch (newMode)
            {
                case "BAJA_VOL":
                    // Baja volatilidad: mercado tranquilo, VWAPs limpios — WR=84%, TP amplio
                    HealthStrongThreshold = 2.0;
                    HealthWeakThreshold = 1.5;
                    TouchStudySLTicks = 40;
                    TouchStudyTPTicks = 60;
                    break;
                case "NORMAL":
                    // Volatilidad normal: config equilibrado (mejor PnL total) — WR=72%, PF=3.9
                    HealthStrongThreshold = 2.0;
                    HealthWeakThreshold = 1.5;
                    TouchStudySLTicks = 40;
                    TouchStudyTPTicks = 60;
                    break;
                default: // ALTA_VOL
                    // Alta volatilidad: SL/TP ajustados, entrada/salida rapida — WR=71%, 117 cerr/mes
                    HealthStrongThreshold = 2.0;
                    HealthWeakThreshold = 1.5;
                    TouchStudySLTicks = 35;
                    TouchStudyTPTicks = 40;
                    break;
            }

            // C+D siempre en auto
            TouchStudyFilter = TouchStudyFilterMode.ConfigCD;
            _showCfgA = false;
            _showCfgB = false;
            _showCfgC = true;
            _showCfgD = true;

            // Sincronizar checkboxes WPF del toolbar (deben correr en UI thread)
            if (_chkCfgA != null)
            {
                ChartControl.Dispatcher.InvokeAsync(() =>
                {
                    _chkCfgA.IsChecked = false;
                    _chkCfgB.IsChecked = false;
                    _chkCfgC.IsChecked = true;
                    _chkCfgD.IsChecked = true;
                });
            }

            if (ShowDebugLogs)
                Print(string.Format("[AUTO] ATR={0:F2} -> Modo {1}: SL={2} TP={3} Strong={4} Weak={5}",
                    currentATR, newMode, TouchStudySLTicks, TouchStudyTPTicks, HealthStrongThreshold, HealthWeakThreshold));
        }

        private void ResetPersonalityState()
        {
            // v3.0.2: Only clear session lists for the TARGET personality mode.
            // Preserve the other mode's data so switching back doesn't lose history.
            if (Personality == PersonalityMode.Intraday)
            {
                // Switching TO Intraday: clear period data, preserve intraday sessions
                if (periodSessions != null) periodSessions.Clear();
                if (periodDividerBars != null) periodDividerBars.Clear();
            }
            else
            {
                // Switching TO a Period mode: clear period data for fresh start, preserve intraday sessions
                if (periodSessions != null) periodSessions.Clear();
                if (periodDividerBars != null) periodDividerBars.Clear();
                // NOTE: Do NOT clear asiaSessions/europeSessions/usSessions — they hold mitigation history
            }

            // v3.0.2: Reset divider tracker so it re-detects all period boundaries on reload
            _lastDividerPeriodStart = DateTime.MinValue;

            // v3.2.0: Reset auto template mode so it re-evaluates on next bar
            _lastAutoMode = "";

            // Reset VWAP calculation state (needed for any mode switch)
            hasHighVWAP = false;
            hasLowVWAP = false;
            // v3.3.7: _prevBarHighVwap/_prevBarLowVwap eliminados
            hasInternalHighVWAP = false;
            hasInternalLowVWAP = false;

            // v3.0.2: Do NOT clear historical anchors — they are mode-independent visual data
            // historicalHighs/historicalLows are needed for VWAP trail rendering in any mode

            // Reset VWAP tracking variables
            sessionHighPV = 0;
            sessionHighVol = 0;
            sessionHighBarIdx = -1;
            sessionLowPV = 0;
            sessionLowVol = 0;
            sessionLowBarIdx = -1;

            // v3.0.4: Reset health tracking
            ResetVwapHealth(true);
            ResetVwapHealth(false);

            // v3.0.5: Reset touch study
            ResetTouchStudy(true);
            ResetTouchStudy(false);
            _activeFirstTouches.Clear();
            _completedFirstTouches.Clear(); // v3.2.0
            // v3.0.7: Reset episode tracking
            _lastConfigBBar = -999;
            _lastConfigCBar = -999;
            _lastConfigABar = -999;
            _lastConfigDBar = -999;

            // v3.3.0: Reset market structure
            if (ShowStructure) ResetStructure();

            // v3.1.0: Reset auto-trade state (don't cancel live orders, just reset tracking)
            _autoTradeOpen = false;
            _autoTradeConfig = "";
            _autoTradeOcoId = "";

            // Clear plots (VWAP lines need recalculation)
            for (int i = 0; i < 4 && i < Values.Length; i++)
            {
                Values[i].Reset();
            }
        }

        protected override void OnBarUpdate()
        {
      if (CurrentBar < 14)
      {
          // v1.0.24: Use NaN to prevent plot lines before anchor
          Values[0][0] = double.NaN;
          Values[1][0] = double.NaN;
          return;
      }

      // v3.0.0: Detect personality changes and reset state
      if (_lastPersonality != Personality)
      {
          ResetPersonalityState();
          _lastPersonality = Personality;
          if (ShowDebugLogs) Print(string.Format("[PERSONALITY] Switched to {0} - State reset complete", Personality));
      }

      // PROCESS PENDING SIGNALS when entering Realtime (after all historical bars loaded)
      // OR when Historical processing completes (fallback for playback/weekend data)
      if (!_signalsProcessed && (State == State.Realtime || (State == State.Historical && IsFirstTickOfBar && CurrentBar >= Bars.Count - 2)))
      {
          if (ShowDebugLogs) Print(string.Format("[SIGNALS] Processing: State={0} Bar={1} Count={2}", State, CurrentBar, Bars.Count));
          ProcessPendingSignals();
      }

      // TRADING UI INIT
      // Allow creation in Realtime OR on the last historical bar (catch-all)
      if ((State == State.Realtime || (State == State.Historical && CurrentBar == Bars.Count - 1)) && _armButton == null)
      {
          CreateWpfControls();
      }

      // SMART ENTRY LOGIC (v3.1.2 perf: only in Realtime — no trading during Historical)
      if (State == State.Realtime) CheckSmartEntryLogic();
              debugUpdateCounter++; // Count EVERY call

      // v2.2.4: Calculate bar delta and accumulate to 4 session deltas
      if (CaptureDelta && IsFirstTickOfBar && CurrentBar > 0)
      {
          // Bar Delta = (Close - Open) * Volume (positive = buyers, negative = sellers)
          _lastBarDelta = (Close[0] - Open[0]) * Volume[0];

          // Detect current sessions using cached times (DST-aware)
          TimeSpan currentTime = Time[0].TimeOfDay;
          TimeSpan asiaStart = GetTimeByZone(AsiaStartTime);
          TimeSpan asiaEnd = GetTimeByZone(AsiaEndTime);
          TimeSpan europeStart = GetTimeByZone(EuropeStartTime);
          TimeSpan usaStart = GetTimeByZone(USStartTime);
          TimeSpan usaEnd = GetTimeByZone(USEndTime);

          // Session detection (handles overnight sessions)
          bool inAsia = (asiaStart > asiaEnd)
              ? (currentTime >= asiaStart || currentTime < asiaEnd)
              : (currentTime >= asiaStart && currentTime < asiaEnd);
          bool inEurope = (europeStart > usaStart)
              ? (currentTime >= europeStart || currentTime < usaStart)
              : (currentTime >= europeStart && currentTime < usaStart);
          bool inUSA = (currentTime >= usaStart && currentTime < usaEnd);

          // v3.3.10: Cerrar trade real 1 minuto antes del cierre de sesión USA
          {
              TimeSpan eodCutoff = usaEnd.Subtract(TimeSpan.FromMinutes(1));
              if (currentTime >= eodCutoff && currentTime < usaEnd)
              {
                  if (_healthCrossTradeOpen && State == State.Realtime)
                      CloseHealthCrossTrade("EOD_1min");
                  // v3.4.0: Cerrar trade TDU por EOD
                  if (_tduTradeActive)
                      CloseTDUTrade("EOD");
              }
          }

          // Reset individual deltas on session entry
          if (inAsia && !_wasInAsia) _deltaAsia = 0;
          if (inEurope && !_wasInEurope) _deltaEurope = 0;
          if (inUSA && !_wasInUSA) _deltaUSA = 0;

          // Accumulate deltas
          _deltaGlobal += _lastBarDelta;  // Always accumulates (full trading day)
          if (inAsia) _deltaAsia += _lastBarDelta;
          if (inEurope) _deltaEurope += _lastBarDelta;
          if (inUSA) _deltaUSA += _lastBarDelta;

          // Track session state for next bar
          _wasInAsia = inAsia;
          _wasInEurope = inEurope;
          _wasInUSA = inUSA;
      }

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

      // v1.0.49: Update internal VWAPs
      if (hasInternalHighVWAP && internalHighBarIdx >= 0 && CurrentBar >= internalHighBarIdx) {
          double iHVol = Math.Max(1, internalHighVol);
          double iHVal = internalHighPV / iHVol;
          if (iHVal < priceLimit)
              iHVal = Values[2].IsValidDataPointAt(CurrentBar - 1) ? Values[2][1] : Close[0];
          Values[2][0] = iHVal; // Internal High VWAP

      } else {
          Values[2][0] = double.NaN;
      }

      if (hasInternalLowVWAP && internalLowBarIdx >= 0 && CurrentBar >= internalLowBarIdx) {
          double iLVol = Math.Max(1, internalLowVol);
          double iLVal = internalLowPV / iLVol;
          if (iLVal < priceLimit)
              iLVal = Values[3].IsValidDataPointAt(CurrentBar - 1) ? Values[3][1] : Close[0];
          Values[3][0] = iLVal; // Internal Low VWAP

      } else {
          Values[3][0] = double.NaN;

      }

             try
             {
                 if (State == State.Realtime && CurrentBar % 500 == 0)
                 {
                     LogToFile("RelativeVwap Alive @ Bar " + CurrentBar, "HEARTBEAT");
                 }

                 // Manage Active Trades
                 // ManageTrades(); // Removed: Superseded by RelativeVwap.Trading.cs
                 
                 // Main logic
             // Check for Day Change (Strict Reset)
             // CRITICAL FIX: Only Reset Anchors if the Calendar Date changes.
             // Do NOT reset just because a new Intraday Session (Europe/US) starts.
              // Fix: Ensure ResetSession only runs once per bar (on the first tick) to prevent oscillation
              // v3.0.1: Only reset on day change for Intraday mode. Period modes handle their own reset logic.
              if (Bars.IsFirstBarOfSession && IsFirstTickOfBar && Personality == PersonalityMode.Intraday)
              {
                  // v1.0.53: Detect actual day change (not just session change)
                  DateTime currentTradingDay = Time[0].Date;
                  bool isNewTradingDay = (_lastTradingDay != DateTime.MinValue && currentTradingDay != _lastTradingDay);

                  // Archive the final anchors of the previous session
                  if (sessionHighBarIdx != -1)
                  {
                      historicalHighs.Add(new HistoricalAnchor
                      {
                          StartIdx = sessionHighBarIdx,
                          EndIdx = CurrentBar - 1,
                          WasRelevant = highHasTakenRelevant,
                          FirstBreakIdx = highFirstBreakIdx,
                          VwapValues = CopyVwapValues(sessionHighBarIdx, CurrentBar - 1, 0),
                          IsSessionEnd = true,
                          HealthScore = GetVwapHealthScore(true),
                          HealthTouchCount = _highVwapTouchCount,
                          FirstTouches = new List<FirstTouchRecord>(_activeFirstTouches.FindAll(t => t.TouchedHighVwap && t.BarIdx >= sessionHighBarIdx && t.BarIdx <= CurrentBar - 1))
                      });
                  }

                  if (sessionLowBarIdx != -1)
                  {
                      historicalLows.Add(new HistoricalAnchor
                      {
                          StartIdx = sessionLowBarIdx,
                          EndIdx = CurrentBar - 1,
                          WasRelevant = lowHasTakenRelevant,
                          FirstBreakIdx = lowFirstBreakIdx,
                          VwapValues = CopyVwapValues(sessionLowBarIdx, CurrentBar - 1, 1),
                          IsSessionEnd = true,
                          HealthScore = GetVwapHealthScore(false),
                          HealthTouchCount = _lowVwapTouchCount,
                          FirstTouches = new List<FirstTouchRecord>(_activeFirstTouches.FindAll(t => !t.TouchedHighVwap && t.BarIdx >= sessionLowBarIdx && t.BarIdx <= CurrentBar - 1))
                      });
                  }

                  // v3.0.5: Clear active touches after archiving (prevent unbounded growth)
                  _activeFirstTouches.Clear();
                  _completedFirstTouches.Clear(); // v3.2.0

                  // Close Ghost Lines
                   CloseGhostLines(asiaSessions, CurrentBar);
                   CloseGhostLines(europeSessions, CurrentBar);
                   CloseGhostLines(usSessions, CurrentBar);

                   ResetSession();
                   _sessionResetTime = Time[0]; // v3.3.9: marcar inicio de sesión para blackout de cruces

                   // v1.0.53: Reset internal VWAPs only on NEW TRADING DAY
                   if (isNewTradingDay)
                   {
                       // Archive Internal High trail from previous day
                       if (hasInternalHighVWAP && internalHighBarIdx != -1)
                       {
                           historicalInternalHighs.Add(new HistoricalAnchor
                           {
                               StartIdx = internalHighBarIdx,
                               EndIdx = CurrentBar - 1,
                               VwapValues = CopyVwapValues(internalHighBarIdx, CurrentBar - 1, 2)
                           });
                       }

                       internalHighBarIdx = -1;
                       internalHighPV = 0;
                       internalHighVol = 0;
                       internalHighPrice = 0;
                       hasInternalHighVWAP = false;

                       // Archive Internal Low trail from previous day
                       if (hasInternalLowVWAP && internalLowBarIdx != -1)
                       {
                           historicalInternalLows.Add(new HistoricalAnchor
                           {
                               StartIdx = internalLowBarIdx,
                               EndIdx = CurrentBar - 1,
                               VwapValues = CopyVwapValues(internalLowBarIdx, CurrentBar - 1, 3)
                           });
                       }

                       internalLowBarIdx = -1;
                       internalLowPV = 0;
                       internalLowVol = 0;
                       internalLowPrice = 0;
                       hasInternalLowVWAP = false;

                       hasInternalLowVWAP = false;

                   }

                   _lastTradingDay = currentTradingDay;
                   
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
                          
                          if (ShowDebugLogs)
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
                          
                          if (ShowDebugLogs)
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

             // v3.0.2: Always track period dividers (independent of Personality)
             TrackPeriodDividers(time);

             // v3.3.0: Market Structure Detection
             if (ShowStructure) UpdateStructure(time);

             // v3.0.0: Conditional Session Updates based on Personality Mode
             if (Personality == PersonalityMode.Intraday)
             {
                 // Intraday mode: Use time-based sessions (Asia, Europe, USA)
                 UpdateSession(asiaSessions, "Asia", time, AsiaStartTime, AsiaEndTime, ShowAsia);
                 UpdateSession(europeSessions, "Europe", time, EuropeStartTime, EuropeEndTime, ShowEurope);
                 UpdateSession(usSessions, "USA", time, USStartTime, USEndTime, ShowUS);

                 // v3.0.4: Track US First Hour Opening Range
                 if (ShowUSFirstHour) TrackUSFirstHour(time);

                 // v2.1.0: Reset break trackers at start of bar analysis
                 _highBreaks.Clear();
                 _lowBreaks.Clear();

                 // Check Touches - ALWAYS check now, for visibility logic
                 CheckTouches(asiaSessions);
                 CheckTouches(europeSessions);
                 CheckTouches(usSessions);
             }
             else
             {
                 // Period mode: Use date-based periods (Weekly, Monthly, Quarterly, Yearly)
                 UpdatePeriodSession(periodSessions, Personality, time, WeekStartDay);

                 // v3.0.1: Check if period changed - reset VWAP anchors on new period
                 if (periodSessions != null && periodSessions.Count > 0)
                 {
                     var lastPeriod = periodSessions.Last();
                     
                     // If the current period session was JUST created on this bar, it means we crossed a period boundary
                     if (lastPeriod.StartBarIdx == CurrentBar && periodSessions.Count > 1)
                     {
                         // Archive previous VWAP anchors
                         if (sessionHighBarIdx != -1)
                         {
                             historicalHighs.Add(new HistoricalAnchor
                             {
                                 StartIdx = sessionHighBarIdx,
                                 EndIdx = CurrentBar - 1,
                                 WasRelevant = highHasTakenRelevant,
                                 FirstBreakIdx = highFirstBreakIdx,
                                 VwapValues = CopyVwapValues(sessionHighBarIdx, CurrentBar - 1, 0),
                                 IsSessionEnd = true,
                                 HealthScore = GetVwapHealthScore(true),
                                 HealthTouchCount = _highVwapTouchCount,
                                 FirstTouches = new List<FirstTouchRecord>(_activeFirstTouches.FindAll(t => t.TouchedHighVwap && t.BarIdx >= sessionHighBarIdx && t.BarIdx <= CurrentBar - 1))
                             });
                         }
                         if (sessionLowBarIdx != -1)
                         {
                             historicalLows.Add(new HistoricalAnchor
                             {
                                 StartIdx = sessionLowBarIdx,
                                 EndIdx = CurrentBar - 1,
                                 WasRelevant = lowHasTakenRelevant,
                                 FirstBreakIdx = lowFirstBreakIdx,
                                 VwapValues = CopyVwapValues(sessionLowBarIdx, CurrentBar - 1, 1),
                                 IsSessionEnd = true,
                                 HealthScore = GetVwapHealthScore(false),
                                 HealthTouchCount = _lowVwapTouchCount,
                                 FirstTouches = new List<FirstTouchRecord>(_activeFirstTouches.FindAll(t => !t.TouchedHighVwap && t.BarIdx >= sessionLowBarIdx && t.BarIdx <= CurrentBar - 1))
                             });
                         }

                         // v3.0.5: Clear active touches after archiving
                         _activeFirstTouches.Clear();
                         _completedFirstTouches.Clear(); // v3.2.0

                         // Reset VWAP state for new period
                         ResetSession();
                         _lastTradingDay = time.Date;
                         
                         if (ShowDebugLabels)
                             Print(string.Format("[PERIOD RESET] New {0} period started at bar {1} - VWAP anchors reset",
                                 Personality, CurrentBar));
                     }
                 }

                 // Reset break trackers at start of bar analysis
                 _highBreaks.Clear();
                 _lowBreaks.Clear();

                 // Check Touches for period sessions
                 CheckTouches(periodSessions);
             }

             // v2.1.0: Process only the "best" break for each side if multiple happened
             ProcessBestBreaks();
             
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

             // v2.0.0: Internal High Re-anchoring
             // If price breaks the current internal anchor high but is still below day high
             if (EnableInternalLogic && hasInternalHighVWAP && high > internalHighExtreme && high < currentDayHigh)
             {
                 // ...
                 // Save previous internal anchor
                 if (internalHighBarIdx != -1)
                 {
                     historicalInternalHighs.Add(new HistoricalAnchor
                     {
                         StartIdx = internalHighBarIdx,
                         EndIdx = CurrentBar,
                         VwapValues = CopyVwapValues(internalHighBarIdx, CurrentBar, 2)
                     });
                 }

                 internalHighExtreme = high;
                 internalHighBarIdx = CurrentBar;
                 internalHighPV = price * volume;
                 internalHighVol = volume;
                 _internalHighJustReset = true;
                 
                 // v2.1.0: New Anchor -> Allow new Signal 2 (Attempt 2, 3...)
                 internalHighSignal2Fired = false;
             }

             // v2.0.0: Internal Low Re-anchoring
             // If price breaks the current internal anchor low but is still above day low
             if (EnableInternalLogic && hasInternalLowVWAP && low < internalLowExtreme && low > currentDayLow)
             {
                 // ...
                 // Save previous internal anchor
                 if (internalLowBarIdx != -1)
                 {
                     historicalInternalLows.Add(new HistoricalAnchor
                     {
                         StartIdx = internalLowBarIdx,
                         EndIdx = CurrentBar,
                         VwapValues = CopyVwapValues(internalLowBarIdx, CurrentBar, 3)
                     });
                 }

                 internalLowExtreme = low;
                 internalLowBarIdx = CurrentBar;
                 internalLowPV = price * volume;
                 internalLowVol = volume;
                 _internalLowJustReset = true;
                 
                 // v2.1.0: New Anchor -> Allow new Signal 2 (Attempt 2, 3...)
                 internalLowSignal2Fired = false;
             }

             // v2.0.0: Terminate Internal High VWAP (It has been mitigated by a new day extreme)
             if (hasInternalHighVWAP && internalHighBarIdx != -1 && high >= currentDayHigh)
             {
                 // Save the final segment before termination
                 historicalInternalHighs.Add(new HistoricalAnchor
                 {
                     StartIdx = internalHighBarIdx,
                     EndIdx = CurrentBar,
                     VwapValues = CopyVwapValues(internalHighBarIdx, CurrentBar, 2)
                 });
                 hasInternalHighVWAP = false;
                 internalHighBarIdx = -1;
                 Values[2][0] = double.NaN; // Stop plotting
             }

             // v2.0.0: Terminate Internal Low VWAP (It has been mitigated by a new day extreme)
             if (hasInternalLowVWAP && internalLowBarIdx != -1 && low <= currentDayLow)
             {
                 // Save the final segment before termination
                 historicalInternalLows.Add(new HistoricalAnchor
                 {
                     StartIdx = internalLowBarIdx,
                     EndIdx = CurrentBar,
                     VwapValues = CopyVwapValues(internalLowBarIdx, CurrentBar, 3)
                 });
                 hasInternalLowVWAP = false;
                 internalLowBarIdx = -1;
                 Values[3][0] = double.NaN; // Stop plotting
             }

             if (high > currentDayHigh)
             {
                 // Save previous high anchor if it existed
                 if (sessionHighBarIdx != -1)
                 {
                     historicalHighs.Add(new HistoricalAnchor
                     {
                         StartIdx = sessionHighBarIdx,
                         EndIdx = CurrentBar,
                         WasRelevant = highHasTakenRelevant,
                         VwapValues = CopyVwapValues(sessionHighBarIdx, CurrentBar, 0),
                         HealthScore = GetVwapHealthScore(true),
                         HealthTouchCount = _highVwapTouchCount,
                         FirstTouches = new List<FirstTouchRecord>(_activeFirstTouches.FindAll(t => t.TouchedHighVwap && t.BarIdx >= sessionHighBarIdx && t.BarIdx <= CurrentBar))
                     });
                 }
                  currentDayHigh = high;
                  sessionHighBarIdx = CurrentBar;

                  // v1.0.44: Save session that created this anchor (for Signal 2 validation)
                  currentHighAnchorSession = lastUnlockedHighSession;
                  if (ShowDebugLogs)
                      Print(string.Format("[DEBUG ANCHOR] Bar:{0} | NEW HIGH ANCHOR | Session:{1}", CurrentBar,
                          (currentHighAnchorSession != null ? currentHighAnchorSession.Name : "null")));

                  // MANUAL FIX: Reset Signal State
                  highDetached = false;
                  highSignal2Fired = false;  // v1.0.33: Reset flag to allow Signal 2 for new anchor
                  lastSignaledHighAnchorBar = -1;  // v1.0.25: Reset tracker to allow Signal 2 for new anchor
                  // v1.0.47: DO NOT reset highAnchorSequence here - sequence is per SESSION LEVEL, not per anchor
                  // Sequence only resets when touching opposite level in CheckTouches

                  if (ShowDebugLogs)
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
                  ResetVwapHealth(true);  // v3.0.4: New anchor = fresh health tracking
                  ResetTouchStudy(true); // v3.0.5

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
                     historicalLows.Add(new HistoricalAnchor
                     {
                         StartIdx = sessionLowBarIdx,
                         EndIdx = CurrentBar,
                         WasRelevant = lowHasTakenRelevant,
                         VwapValues = CopyVwapValues(sessionLowBarIdx, CurrentBar, 1),
                         HealthScore = GetVwapHealthScore(false),
                         HealthTouchCount = _lowVwapTouchCount,
                         FirstTouches = new List<FirstTouchRecord>(_activeFirstTouches.FindAll(t => !t.TouchedHighVwap && t.BarIdx >= sessionLowBarIdx && t.BarIdx <= CurrentBar))
                     });
                 }
                  currentDayLow = low;
                  sessionLowBarIdx = CurrentBar;

                  // v1.0.44: Save session that created this anchor (for Signal 2 validation)
                  currentLowAnchorSession = lastUnlockedLowSession;
                  if (ShowDebugLogs)
                      Print(string.Format("[DEBUG ANCHOR] Bar:{0} | NEW LOW ANCHOR | Session:{1}", CurrentBar,
                          (currentLowAnchorSession != null ? currentLowAnchorSession.Name : "null")));

                  // MANUAL FIX: Reset Signal State
                  lowDetached = false;
                  lowSignal2Fired = false;  // v1.0.33: Reset flag to allow Signal 2 for new anchor
                  lastSignaledLowAnchorBar = -1;  // v1.0.25: Reset tracker to allow Signal 2 for new anchor
                  // v1.0.47: DO NOT reset lowAnchorSequence here - sequence is per SESSION LEVEL, not per anchor
                  // Sequence only resets when touching opposite level in CheckTouches

                  if (ShowDebugLogs)
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
                  ResetVwapHealth(false);  // v3.0.4: New anchor = fresh health tracking
                  ResetTouchStudy(false); // v3.0.5
              }
             
            // For time-based bars, let the Timer handle the update in Realtime
            // For time-based bars, let the Timer handle the update in Realtime
            // if (isTimeBased && State == State.Realtime) return; // REMOVED to allow Signal Logic to run
            
            if (CurrentBar == Bars.Count - 1)
            {
                CalculateCountdown();
                
                // PHASE 5: Process all pending historical signals once at the end of historical data
                // This ensures we draw lines efficiently without recalculating every tick during history
                ProcessPendingSignals();
            }
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
                  // v1.0.49: Accumulate internal VWAPs if they exist (skip if just created to avoid double-counting)
                  if (EnableInternalLogic && hasInternalHighVWAP && internalHighBarIdx != -1 && !_internalHighJustReset) { internalHighPV += price * tickVol; internalHighVol += tickVol; }
                  if (EnableInternalLogic && hasInternalLowVWAP && internalLowBarIdx != -1 && !_internalLowJustReset) { internalLowPV += price * tickVol; internalLowVol += tickVol; }

                 // Reset flags after use (each tick resets them)
                 _highJustReset = false;
                 _lowJustReset = false;
                 _internalHighJustReset = false;
                 _internalLowJustReset = false;
                 
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
                 // v1.0.49: Accumulate internal VWAPs if they exist
                 if (hasInternalHighVWAP && internalHighBarIdx != -1 && !_internalHighJustReset) {
                     internalHighPV += price * volume;
                     internalHighVol += volume;
                 }
                 if (hasInternalLowVWAP && internalLowBarIdx != -1 && !_internalLowJustReset) {
                     internalLowPV += price * volume;
                     internalLowVol += volume;
                 }

                 // Reset flags after use
                 _highJustReset = false;
                 _lowJustReset = false;
                 _internalHighJustReset = false;
                 _internalLowJustReset = false;
                 
                // V_VWAP: Session-Specific Anchored VWAPs (Historical) - REMOVED
             }

               // 1. Calculate Current VWAP Values (using session variables for display)
               currentHighVWAP = (sessionHighVol > 0) ? sessionHighPV / sessionHighVol : High[0];
               currentLowVWAP = (sessionLowVol > 0) ? sessionLowPV / sessionLowVol : Low[0];
              
               hasHighVWAP = sessionHighBarIdx != -1 && sessionHighVol > 0;
               hasLowVWAP = sessionLowBarIdx != -1 && sessionLowVol > 0;

               // v3.2.0 perf: All tracking (health, touch study, approaches) only needs per-bar resolution.
               // Running per-tick was causing extreme slowness during playback and active trades.
               // Touch detection still happens per-tick but the heavy MFE/MAE loop is gated inside.
               bool runTracking = IsFirstTickOfBar;

               // v3.2.0: Auto template — adapta parámetros según ATR actual
               if (runTracking) UpdateAutoTemplate();

               // v3.0.4: Track VWAP approaches for dominance analysis
               if (runTracking && ExportVwapApproaches && hasHighVWAP && hasLowVWAP)
                   TrackVwapApproaches();

               // v3.0.4: Update VWAP health tracking — runs per-bar only
               // Accumulates MFE/MAE distances, must NOT run per-tick or values get inflated
               if (runTracking) UpdateVwapHealthTracking();

               // v3.3.7: Pintar velas con decisión persistida — live guarda, F5 lee
               if (ShowDemandSupplyCandles && IsFirstTickOfBar)
               {
                   sbyte[] colorArr = null;
                   string ck = _instrumentKey;
                   if (ck != null) HistCandleColorMap.TryGetValue(ck, out colorArr);

                   bool demandStronger;

                   if (State == State.Historical && colorArr != null && CurrentBar < HIST_HEALTH_SIZE && colorArr[CurrentBar] != 0)
                   {
                       // F5: reutilizar la decisión guardada en live
                       demandStronger = colorArr[CurrentBar] > 0;
                   }
                   else
                   {
                       // Live (o histórico sin datos previos): calcular normalmente
                       double demandScore = GetVwapHealthScore(false);
                       double supplyScore = GetVwapHealthScore(true);
                       demandStronger = demandScore > supplyScore;

                       // Guardar decisión para que F5 la use
                       if (colorArr != null && CurrentBar < HIST_HEALTH_SIZE)
                           colorArr[CurrentBar] = demandStronger ? (sbyte)1 : (sbyte)-1;
                   }

                   if (demandStronger)
                   {
                       BarBrushes[0] = Brushes.White;
                       CandleOutlineBrushes[0] = Brushes.White;
                   }
                   else
                   {
                       BarBrushes[0] = Brushes.Transparent;
                       CandleOutlineBrushes[0] = Brushes.White;
                   }

                   // v3.3.8: Almacenar scores en buffer circular para cálculo de pendiente
                   {
                       double curDemand = GetVwapHealthScore(false);
                       double curSupply = GetVwapHealthScore(true);
                       if (_recentDemandScores != null)
                       {
                           _recentDemandScores[_scoreBufIdx] = curDemand;
                           _recentSupplyScores[_scoreBufIdx] = curSupply;
                           _scoreBufIdx = (_scoreBufIdx + 1) % SLOPE_LOOKBACK;
                           if (!_scoreBufFull && _scoreBufIdx == 0) _scoreBufFull = true;
                       }
                   }

                   // v3.3.8: Tracking MFE/MAE del cross trade activo (cada barra)
                   if (_crossTradeActive && IsFirstTickOfBar)
                       UpdateCrossTradeTracking();

                   // v3.3.9: Blackout al inicio de sesión — VWAPs crudos generan cruces falsos
                   bool inBlackout = (_sessionResetTime != DateTime.MinValue
                       && HealthCrossBlackoutMinutes > 0
                       && (Time[0] - _sessionResetTime).TotalMinutes < HealthCrossBlackoutMinutes);

                   // v3.3.8: Detectar cruce con filtro de salida —
                   // No salimos al primer cruce. Solo confirmamos cuando la línea perdedora DECLINA.
                   if (!inBlackout && _demandSupplyInitialized && demandStronger != _prevDemandStronger && !_pendingFlip && !_confirmBarsPending)
                   {
                       // Cruce detectado. ¿La línea perdedora ya está declinando?
                       bool loserDeclining = _scoreBufFull ? IsHealthDeclining(!demandStronger) : true;

                       if (loserDeclining)
                       {
                           // v3.3.10: Si ConfirmBars > 0, no ejecutar inmediato — iniciar espera temporal
                           if (HealthCrossConfirmBars > 0)
                           {
                               _confirmBarsPending = true;
                               _confirmBarsDirection = demandStronger;
                               _confirmBarsCount = 0;

                               // Triángulo cyan = cruce esperando confirmación temporal
                               double atrOff2 = (atr != null && CurrentBar >= 14) ? atr[0] * 0.5 : TickSize * 10;
                               string confTag = "DSConf_" + CurrentBar;
                               if (ShowSignalText)
                               {
                                   if (demandStronger)
                                       Draw.TriangleUp(this, confTag, true, 0, Low[0] - atrOff2, Brushes.Cyan);
                                   else
                                       Draw.TriangleDown(this, confTag, true, 0, High[0] + atrOff2, Brushes.Cyan);
                               }
                           }
                           else
                           {
                               // ConfirmBars = 0 → confirmar flip inmediato
                               ExecuteHealthFlip(demandStronger);
                           }
                       }
                       else
                       {
                           // Perdedor aún fuerte → poner en espera de declive
                           _pendingFlip = true;
                           _pendingFlipDirection = demandStronger;
                           _pendingFlipStable = _scoreBufFull ? IsHealthStable(demandStronger) : true;

                           // Triángulo amarillo = cruce pendiente de confirmación de declive
                           double atrOff = (atr != null && CurrentBar >= 14) ? atr[0] * 0.5 : TickSize * 10;
                           string pendTag = "DSPend_" + CurrentBar;
                           if (ShowSignalText)
                           {
                               if (demandStronger)
                               {
                                   Draw.Text(this, pendTag, true, "▲\nPEND\nLONG", 0, Low[0] - atrOff, 0, Brushes.Yellow, new SimpleFont("Arial", 9) { Bold = true }, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                               }
                               else
                               {
                                   Draw.Text(this, pendTag, true, "PEND\nSHORT\n▼", 0, High[0] + atrOff, 0, Brushes.Yellow, new SimpleFont("Arial", 9) { Bold = true }, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                               }
                           }
                       }
                   }
                   // Chequear pending flip (esperando declive del perdedor)
                   else if (!inBlackout && _pendingFlip)
                   {
                       bool stillCrossed = (_pendingFlipDirection == demandStronger);
                       if (!stillCrossed)
                       {
                           _pendingFlip = false;
                       }
                       else
                       {
                           bool loserNowDeclining = IsHealthDeclining(!_pendingFlipDirection);
                           if (loserNowDeclining)
                           {
                               _pendingFlip = false;
                               // v3.3.10: Después de confirmar declive, iniciar espera temporal si ConfirmBars > 0
                               if (HealthCrossConfirmBars > 0)
                               {
                                   _confirmBarsPending = true;
                                   _confirmBarsDirection = _pendingFlipDirection;
                                   _confirmBarsCount = 0;
                               }
                               else
                               {
                                   ExecuteHealthFlip(_pendingFlipDirection);
                               }
                           }
                       }
                   }

                   // v3.3.10: Contador de confirmación temporal — esperar N barras tras cruce
                   if (_confirmBarsPending && !inBlackout)
                   {
                       _confirmBarsCount++;
                       bool stillValid = (_confirmBarsDirection == demandStronger);

                       if (!stillValid)
                       {
                           // Cruce se revirtió antes de confirmar → descartar
                           _confirmBarsPending = false;
                           _confirmBarsCount = 0;
                       }
                       else if (_confirmBarsCount >= HealthCrossConfirmBars)
                       {
                           // Cruce confirmado tras N barras → ejecutar
                           _confirmBarsPending = false;
                           _confirmBarsCount = 0;
                           ExecuteHealthFlip(_confirmBarsDirection);
                       }
                   }

                   // Solo actualizar _prevDemandStronger si NO hay pending flip ni confirm pending
                   if (!_pendingFlip && !_confirmBarsPending)
                       _prevDemandStronger = demandStronger;
                   _demandSupplyInitialized = true;
               }

               // v3.4.0: TDU Price Action — evaluar señales filtradas por Health bias
               if (EnableTDUTrading && IsFirstTickOfBar)
                   CheckTDUSignal();

               // v3.3.7: Calcular scores y guardar por instrumento (DESPUÉS del tracking)
               double hScore = hasHighVWAP ? GetVwapHealthScore(true) : 0;
               double lScore = hasLowVWAP ? GetVwapHealthScore(false) : 0;
               string key = _instrumentKey;
               SharedHighHealthMap[key] = hScore;
               SharedLowHealthMap[key]  = lScore;
               // Guardar en arrays para histórico
               double[] arrH, arrL;
               if (HistHealthHighMap.TryGetValue(key, out arrH) && HistHealthLowMap.TryGetValue(key, out arrL)
                   && CurrentBar < HIST_HEALTH_SIZE)
               {
                   arrH[CurrentBar] = hScore;
                   arrL[CurrentBar] = lScore;
               }
               SharedHighVWAPMap[key]   = hasHighVWAP ? currentHighVWAP : 0;
               SharedLowVWAPMap[key]    = hasLowVWAP ? currentLowVWAP : 0;
               SharedHighAnchorMap[key] = sessionHighBarIdx;
               SharedLowAnchorMap[key]  = sessionLowBarIdx;

               // v3.0.5: Update touch study tracking
               try { if (runTracking && ShowTouchStudy) UpdateTouchStudyTracking(); }
               catch (Exception ex) { Print("[TOUCH_STUDY] OnBarUpdate error: " + ex.Message); }

             // v1.0.24: Move "Liquidity Grabbed" label to new extreme
             // v1.0.45: Only move if NOT locked (locked when Signal 2 fires)
             // v1.0.50: DISABLED - Label stays at initial grab bar, doesn't follow extremes
             if (highLiqGrabBarIdx >= 0 && !string.IsNullOrEmpty(highLiqGrabSessionName) && !highLiqGrabLocked)
             {
                 // For High liquidity grab (short setup), track new HIGHS
                 if (High[0] > highLiqGrabExtreme)
                 {
                     highLiqGrabExtreme = High[0];
                     // highLiqGrabBarIdx = CurrentBar; // v1.0.50: COMMENTED - Keep label at initial grab bar

                     // v1.0.50: Draw at ORIGINAL grab bar, not current bar
                     int barsAgo = CurrentBar - highLiqGrabBarIdx;
                     if (barsAgo >= 0 && barsAgo < CurrentBar)
                     {
                         double atrOff = (atr != null && atr[0] > 0) ? atr[0] * LabelDistanceATR : TickSize * 10;
                         double grabBarHigh = High.GetValueAt(highLiqGrabBarIdx);
                         double newY = grabBarHigh + atrOff;

                         if (ShowSignal1)
                         {
                             Draw.TriangleDown(this, "TakeHigh_" + highLiqGrabSessionName, true, barsAgo, newY, SignalColor);
                             if (ShowSignalText)
                             {
                                 // v1.0.49: 3 lines - add session name, HIGH/LOW, and internal marker
                                 string internalMarker = highLiqGrabIsInternal ? " (i)" : "";
                                 string code = string.Format("Liquidity\nGrabbed {0:00}\n{1} High{2}", highLiqGrabSequence, highLiqGrabSessionName, internalMarker);
                                 SimpleFont font = new SimpleFont("Arial", LabelFontSize);
                                 // v1.0.45: Use sequence in tag to allow multiple labels
                                 Draw.Text(this, "Sig1H_Txt_" + highLiqGrabSessionName + "_" + highLiqGrabSequence, false, code, barsAgo, newY, LabelTextOffset, SignalColor, font, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                             }
                         }
                     }
                 }
             }

             // v1.0.45: Only move if NOT locked (locked when Signal 2 fires)
             // v1.0.50: DISABLED - Label stays at initial grab bar, doesn't follow extremes
             if (lowLiqGrabBarIdx >= 0 && !string.IsNullOrEmpty(lowLiqGrabSessionName) && !lowLiqGrabLocked)
             {
                 // For Low liquidity grab (long setup), track new LOWS
                 if (Low[0] < lowLiqGrabExtreme)
                 {
                     lowLiqGrabExtreme = Low[0];
                     // lowLiqGrabBarIdx = CurrentBar; // v1.0.50: COMMENTED - Keep label at initial grab bar

                     // v1.0.50: Draw at ORIGINAL grab bar, not current bar
                     int barsAgo = CurrentBar - lowLiqGrabBarIdx;
                     if (barsAgo >= 0 && barsAgo < CurrentBar)
                     {
                         double atrOff = (atr != null && atr[0] > 0) ? atr[0] * LabelDistanceATR : TickSize * 10;
                         double grabBarLow = Low.GetValueAt(lowLiqGrabBarIdx);
                         double newY = grabBarLow - atrOff;

                         if (ShowSignal1)
                         {
                             Draw.TriangleUp(this, "TakeLow_" + lowLiqGrabSessionName, true, barsAgo, newY, SignalColor);
                             if (ShowSignalText)
                             {
                                 // v1.0.49: 3 lines - add session name, HIGH/LOW, and internal marker
                                 string internalMarker = lowLiqGrabIsInternal ? " (i)" : "";
                                 string code = string.Format("Liquidity\nGrabbed {0:00}\n{1} Low{2}", lowLiqGrabSequence, lowLiqGrabSessionName, internalMarker);
                                 SimpleFont font = new SimpleFont("Arial", LabelFontSize);
                                 // v1.0.45: Use sequence in tag to allow multiple labels
                                 Draw.Text(this, "Sig1L_Txt_" + lowLiqGrabSessionName + "_" + lowLiqGrabSequence, false, code, barsAgo, newY, -LabelTextOffset, SignalColor, font, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                             }
                         }
                     }
                 }
             }

             // v1.0.45: Detect when price sweeps anchor bar again after Signal 2 was locked
             // HIGH: Check if locked and price breaks above anchor bar
             if (highLiqGrabLocked && sessionHighBarIdx >= 0)
             {
                 int barsAgo = CurrentBar - sessionHighBarIdx;
                 if (barsAgo >= 0 && barsAgo < Bars.Count)
                 {
                     double anchorHigh = High[barsAgo];
                     if (High[0] > anchorHigh)
                     {
                         // Price swept the anchor bar - create new Liquidity Grabbed label
                         highLiqGrabSequence++;
                         highLiqGrabLocked = false; // Unlock so it can move again
                         highLiqGrabBarIdx = CurrentBar;
                         highLiqGrabExtreme = High[0];

                         // v1.0.47: Reset tracker to allow new Signal 2 for this sweep
                         lastSignaledHighAnchorBar = -1;
                         highSignal2Fired = false;

                         // v1.0.50: Voice Alert - Calculate days old
                         int daysOld = 0;
                         if (barsAgo > 0)
                         {
                             DateTime anchorDate = Time[barsAgo].Date;
                             DateTime currentDate = Time[0].Date;
                             daysOld = (int)(currentDate - anchorDate).TotalDays;
                         }
                         SpeakLevelTouch(highLiqGrabSessionName, true, daysOld);

                         if (ShowDebugLogs) Print(string.Format("[DEBUG LG] Bar:{0} | HIGH Anchor Swept | NewSeq:{1} | AnchorBar:{2} | AnchorHigh:{3:F2} | CurrentHigh:{4:F2} | Tracker RESET",
                             CurrentBar, highLiqGrabSequence, sessionHighBarIdx, anchorHigh, High[0]));
                     }
                 }
             }

             // LOW: Check if locked and price breaks below anchor bar
             if (lowLiqGrabLocked && sessionLowBarIdx >= 0)
             {
                 int barsAgo = CurrentBar - sessionLowBarIdx;
                 if (barsAgo >= 0 && barsAgo < Bars.Count)
                 {
                     double anchorLow = Low[barsAgo];
                     if (Low[0] < anchorLow)
                     {
                         // Price swept the anchor bar - create new Liquidity Grabbed label
                         lowLiqGrabSequence++;
                         lowLiqGrabLocked = false; // Unlock so it can move again
                         lowLiqGrabBarIdx = CurrentBar;
                         lowLiqGrabExtreme = Low[0];

                         // v1.0.47: Reset tracker to allow new Signal 2 for this sweep
                         lastSignaledLowAnchorBar = -1;
                         lowSignal2Fired = false;

                         // v1.0.50: Voice Alert - Calculate days old
                         int daysOld = 0;
                         if (barsAgo > 0)
                         {
                             DateTime anchorDate = Time[barsAgo].Date;
                             DateTime currentDate = Time[0].Date;
                             daysOld = (int)(currentDate - anchorDate).TotalDays;
                         }
                         SpeakLevelTouch(lowLiqGrabSessionName, false, daysOld);

                         if (ShowDebugLogs) Print(string.Format("[DEBUG LG] Bar:{0} | LOW Anchor Swept | NewSeq:{1} | AnchorBar:{2} | AnchorLow:{3:F2} | CurrentLow:{4:F2} | Tracker RESET",
                             CurrentBar, lowLiqGrabSequence, sessionLowBarIdx, anchorLow, Low[0]));
                     }
                 }
             }

              // === EXTERNAL Signal Processing (New in V 1.0.27) ===
             // 2. Evaluate Signals (using calculated VWAPs)
             
              // V_CLEANUP: SIGNALS REMOVED (RESET)
              /* 
                 ALL SIGNAL LOGIC (High/Low/Detachment) DELETED
              */
              {
                  // v1.0.49: Use internal VWAP if it exists, otherwise use main VWAP
                  // v1.0.50: Use Values[0] (chart VWAP) instead of currentHighVWAP for consistency
                  double hVwap = (hasInternalHighVWAP && internalHighBarIdx >= 0 && Values[2].IsValidDataPointAt(0))
                      ? Values[2][0]  // Internal HIGH VWAP
                      : (Values[0].IsValidDataPointAt(0) ? Values[0][0] : currentHighVWAP);  // Main HIGH VWAP from chart
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
                             if (ShowDebugLogs)
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
                              
                              if (lastUnlockedHighSession != null && ShowSignalText)
                              {
                                   // v1.0.48: Simplified - only show confirmation marker
                                   string entryLabel = "Confirm";
                                   SimpleFont font = new SimpleFont("Arial", LabelFontSize);
                                   Draw.Text(this, "Sig3H_Txt_" + CurrentBar, false, entryLabel, 0, arrowY, LabelTextOffset, sigBrush, font, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                              }
                          }
                      }

                      highDetached = false;
                      if (ShowDebugLogs)
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
                          {
                              BarBrushes[barsAgo] = null; // Unpaint that bar
                              CandleOutlineBrushes[barsAgo] = null;
                          }
                          highSignal2BarIdx = -1;

                          // v1.0.37: Only unpaint current bar if signal was THIS bar
                          // v1.0.40: Remove BackBrushes (causes vertical bars)
                          BarBrushes[0] = null;
                          CandleOutlineBrushes[0] = null;

                          // v1.0.41: RESET tracker when signal is cancelled - allows new signal for same anchor
                          lastSignaledHighAnchorBar = -1;
                          highLiqGrabLocked = false; // v1.0.45: Unlock Liquidity Grab so label can move again
                          if (ShowDebugLogs) Print(string.Format("[DEBUG FLAG] Bar:{0} | CANCELLED -> Reset lastSignaledHighAnchorBar to -1 (allows new signal)", CurrentBar));
                      } 
                  }
                 
                  // FINAL DRAW CALL
                  if (ShowDebugLabels && !string.IsNullOrEmpty(dbgText) && dbgText != "D") // Only show "D" or specialized debug. Don't show Signal Codes here.
                  {
                       // Draw.Text(this, "DebugHi" + CurrentBar, dbgText, 0, high + dbgOffset, dbgBrush); // DISABLED to prevent overlap with AddSignal
                  }

                  // V_SIGNAL_2: SECONDARY CONFIRMATION (Yellow Dot) - UNIQUE PER ANCHOR
                  // FIX: Enforce validation ONLY ON BAR CLOSE (IsFirstTickOfBar checking previous bar)
                  // Use Index 1 to validate closed candle state.
                  if (IsFirstTickOfBar && CurrentBar > 0)
                  {
                       // v3.0.1: Trade Window Filter — block signals outside configured hours
                       if (UseTradeWindow)
                       {
                           TimeSpan barTime = Time[1].TimeOfDay;
                           TimeSpan twStart, twEnd;
                           if (TimeSpan.TryParse(TradeWindowStart, out twStart) && TimeSpan.TryParse(TradeWindowEnd, out twEnd))
                           {
                               bool inWindow = (twStart < twEnd)
                                   ? (barTime >= twStart && barTime < twEnd)
                                   : (barTime >= twStart || barTime < twEnd);
                               if (!inWindow)
                               {
                                   if (ShowDebugLogs)
                                       Print(string.Format("[TRADE_WINDOW] Bar:{0} | HIGH-side Signal BLOCKED | BarTime:{1} | Window:{2}-{3}", CurrentBar, barTime, twStart, twEnd));
                                   goto SkipSignal2High;
                               }
                           }
                       }

                       // Recalculate Previous VWAP (Index 1)
                       double hVwapPrev = (hasInternalHighVWAP && internalHighBarIdx >= 0 && Values[2].IsValidDataPointAt(1))
                           ? Values[2][1] : (Values[0].IsValidDataPointAt(1) ? Values[0][1] : currentHighVWAP);

                       // v2.2.7: TREND MODE CHECK - Bullish trend enters LONG when price breaks ABOVE High VWAP
                       bool trendModeHighSide = IsTrendMode(out bool trendBearishHigh);

                       if (trendModeHighSide && !trendBearishHigh && highHasTakenRelevant && Close[1] > hVwapPrev)
                       {
                           // TREND LONG: Sellers lost at High VWAP → continuation up
                           bool alreadyFiredTrend = highSignal2Fired;
                           bool alreadySignaledThisAnchorTrend = (sessionHighBarIdx == lastSignaledHighAnchorBar);
                           bool sameLevelAsVwapTrend = (lastUnlockedHighSession == currentHighAnchorSession);
                           bool canFireTrend = !alreadyFiredTrend && !alreadySignaledThisAnchorTrend && sameLevelAsVwapTrend && (highAnchorSequence < GlobalSignal2MaxAttempts) && !IsLastSimTradeStillOpen();

                           if (ShowDebugLogs)
                           {
                               string dbgMsg = string.Format("[DEBUG TREND] Bar:{0} | TREND LONG Check | DeltaGlobal:{1:F0} | SessionDelta:{2:F0} | Close:{3:F2} > VWAP:{4:F2} | CanFire:{5}",
                                   CurrentBar, _deltaGlobal, GetCurrentSessionDelta(), Close[1], hVwapPrev, canFireTrend);
                               Print(dbgMsg);
                               LogToFile(dbgMsg, "TREND_LONG");
                           }

                           if (canFireTrend)
                           {
                               highSignal2BarIdx = CurrentBar - 1;
                               BarBrushes[1] = TrendTradeColor;
                               CandleOutlineBrushes[1] = TrendTradeColor;

                               bool isRealtimeSignal = (State == State.Realtime);
                               if (isRealtimeSignal) SpeakEntrySignal(true, highAnchorSequence + 1);

                               highAnchorSequence++;
                               lastSignaledHighAnchorBar = sessionHighBarIdx;
                               if (!AnalyzeAllSignals) highSignal2Fired = true;
                               highLiqGrabLocked = true;

                               LogToFile(string.Format("TREND LONG FIRED | Close:{0:F2} > VWAP:{1:F2} | DeltaGlobal:{2:F0} | Seq:{3}",
                                   Close[1], hVwapPrev, _deltaGlobal, highAnchorSequence), "TREND_SIGNAL");

                               // TREND MODE: TP is EOD (no fixed TP1/TP2) - use 0 as placeholder, DrawSignalVisualization will handle
                               double trendTP1 = 0; // EOD exit
                               double trendTP2 = 0; // EOD exit

                               // SL: VWAP origin + offset (if breaks back below VWAP, trend failed)
                               double trendSL = hVwapPrev - (StopAnchorOffsetTicks * TickSize);
                               int qty = CalculateSignalPositionSize(Close[1], trendSL);

                               string _setupNameTrendLong = (lastUnlockedHighSession != null) ? lastUnlockedHighSession.Name + " High TREND" : "Unknown TREND";
                               DateTime _anchorTimeTrendLong = (lastUnlockedHighSession != null) ? lastUnlockedHighSession.SessionDate : Time[0].Date;
                               double _atrVal = (atr != null && atr[0] > 0) ? atr[0] : 0;
                               double _volRatio = (Volume[0] > 0 && Volume[1] > 0) ? Volume[0] / Volume[1] : 0;

                               // v2.2.7: Pass isTrendTrade = true
                               DrawSignalVisualization(true, sessionHighBarIdx, hVwapPrev, trendTP1, trendTP2, qty, trendSL, _setupNameTrendLong, highAnchorSequence, _anchorTimeTrendLong,
                                   CaptureDelta ? _deltaGlobal : 0, _atrVal, _volRatio, true);

                               if (ShowDebugLogs) Print(string.Format("[DEBUG TREND] Bar:{0} | TREND LONG Signal Generated | IsRealtime:{1}", CurrentBar, isRealtimeSignal));
                           }
                       }
                       // REVERSAL MODE: Condition: Active VWAP (Taken), Candle CLOSES below VWAP, High BELOW VWAP by threshold
                       // v2.2.7: BLOCK reversals when trend mode is active - only take trend trades in trend conditions
                       else if (highHasTakenRelevant && Close[1] < hVwapPrev && High[1] <= (hVwapPrev - Signal2ThresholdTicks * TickSize))
                       {
                           // v2.2.7: Check if blocked by trend mode
                           if (trendModeHighSide)
                           {
                               if (ShowDebugLogs)
                                   Print(string.Format("[REVERSAL_BLOCKED] Bar:{0} | SHORT reversal BLOCKED by trend mode | TrendBearish:{1}", CurrentBar, trendBearishHigh));
                               // Skip reversal - trend mode active
                           }
                           else
                           {
                           // LOG DIAGNOSTICS FOR SIGNAL 2 SHORT
                           /*
                           Print(string.Format("DEBUG SIG2 SHORT: Bar={0} High={1} hVwap={2} Thresh={3} Diff={4}",
                               CurrentBar, High[1], hVwapPrev, Signal2ThresholdTicks * TickSize, hVwapPrev - High[1]));
                           */

                          // v1.0.33: DOUBLE CHECK - Both flag AND anchor tracker must allow signal
                          bool alreadyFired = highSignal2Fired;
                          bool alreadySignaledThisAnchor = (sessionHighBarIdx == lastSignaledHighAnchorBar);
                          // v1.0.43: TRIPLE CHECK - VWAP anchor must be from same session as last Signal 1
                          bool sameLevelAsVwap = (lastUnlockedHighSession == currentHighAnchorSession);
                          
                          bool canFire = !alreadyFired && !alreadySignaledThisAnchor && sameLevelAsVwap && (highAnchorSequence < GlobalSignal2MaxAttempts) && !IsLastSimTradeStillOpen();

                          if (ShowDebugLogs)
                          {
                              string dbgMsg = string.Format("[DEBUG FLAG] Bar:{0} | SHORT Check (ClosedBar) | Flag:{1} | AnchorSignaled:{2} | AnchorBar:{3} | LastSignaled:{4} | SameLevel:{5} | Seq:{6} | CanFire:{7}",
                                  CurrentBar, alreadyFired, alreadySignaledThisAnchor, sessionHighBarIdx, lastSignaledHighAnchorBar, sameLevelAsVwap, highAnchorSequence, canFire);
                              Print(dbgMsg);
                              LogToFile(dbgMsg, "SIG2_PRE_CHECK");
                          }

                          if (canFire)
                          {
                              // v1.0.8: Paint Signal 2 candle yellow (only the first separation candle)
                              // FIX: Store the Index for persistent painting in Live/Tick mode
                              highSignal2BarIdx = CurrentBar - 1; // Paint previous bar
                              // v1.0.39: Paint all brush types for guaranteed visibility
                              // v1.0.40: Remove BackBrushes (causes vertical bars covering chart)
                              BarBrushes[1] = Brushes.Yellow;
                              CandleOutlineBrushes[1] = Brushes.Yellow;

                              // CRITICAL LOGGING: Confirming why this fired if user sees High > VWAP
                              if (ShowDebugLogs)
                                  Print(string.Format("[RelativeVwap-INDICATOR] SIG2 SHORT FIRED | NOW:{0} | CHART:{1} | Bar:{2} | High:{3:F2} | VWAP:{4:F2} | Thresh:{5} | Cond(H<=V-T):{6} | AnchorBar:{7}",
                                      DateTime.Now, Time[0], CurrentBar, High[1], hVwapPrev, Signal2ThresholdTicks, (High[1] <= (hVwapPrev - Signal2ThresholdTicks * TickSize)), sessionHighBarIdx));

                              // v1.0.26: File Log
                              // v1.0.48: Added Sequence to log
                              LogToFile(string.Format("SIG2 SHORT FIRED | High:{0:F2} | VWAP:{1:F2} | Sep:{2:F2} | Thresh:{3} | AnchorBar:{4} | LastSignaled:{5} | Seq BEFORE:{6}",
                                  High[1], hVwapPrev, hVwapPrev - High[1], Signal2ThresholdTicks, sessionHighBarIdx, lastSignaledHighAnchorBar, highAnchorSequence), "SIGNAL2");

                              // v1.0.50: Voice Alert for Entry Signal (ONLY in real-time, not historical recalc)
                              // v1.0.50: CRITICAL - Only update tracking variables in real-time to prevent duplicate signals after F5
                              bool isRealtimeSignal = (State == State.Realtime);

                              if (isRealtimeSignal)
                              {
                                  SpeakEntrySignal(false, highAnchorSequence + 1);
                              }
                              
                              // v1.0.50: ALWAYS update tracking variables (prevents duplicates in all modes)
                              highAnchorSequence++;
                              lastSignaledHighAnchorBar = sessionHighBarIdx; // Track which anchor was signaled
                              // v2.2.5: When AnalyzeAllSignals=true, allow ALL signals to fire (no blocking)
                              if (!AnalyzeAllSignals)
                                  highSignal2Fired = true; // Mark signal as fired (prevents multiple signals)
                              highLiqGrabLocked = true; // Lock Liquidity Grab label (freezes at pivot)

                              LogToFile(string.Format("→ Seq AFTER increment: {0} → Will show as Entry {1:00}", highAnchorSequence, highAnchorSequence), "SIGNAL2");

                              // v1.15.92: Split TP Calculation
                              // Short Signal -> Target Lows
                              // v1.15.93: Refined Split TP Calculation (Short)
                              // TP1: Opposite VWAP (Low VWAP)
                              double tp1 = (hasLowVWAP && Values[1].IsValidDataPointAt(1)) ? Values[1][1] : (hVwapPrev - 20 * TickSize);
                              
                              // TP2: Opposite Anchor (Low Session)
                              double tp2 = tp1; // Default to TP1 if invalid
                              if (hasLowVWAP && sessionLowBarIdx >= 0 && sessionLowBarIdx < CurrentBar)
                              {
                                  tp2 = Low.GetValueAt(sessionLowBarIdx);
                                  // Ensure TP2 is actually LOWER than Entry
                                  if (tp2 >= Low[1]) tp2 = tp1 - 20 * TickSize;
                              }
                              else
                              {
                                  tp2 = tp1 - 20 * TickSize; // Fallback extension
                              }
                              
                              // Ensure distinct separation
                              if (tp2 > tp1) tp2 = tp1 - 10 * TickSize; // TP2 should be further (Lower)
                              
                              double anchorPrice = High.GetValueAt(sessionHighBarIdx);
                              double slPrice = anchorPrice + (StopAnchorOffsetTicks * TickSize);
                              int qty = CalculateSignalPositionSize(Close[1], slPrice);
                              
                              // v1.0.50: Draw historical SL/TP visualization for analysis
                              string _setupNameShort = (lastUnlockedHighSession != null) ? lastUnlockedHighSession.Name + " High" : "Unknown";
                              DateTime _anchorTimeShort = (lastUnlockedHighSession != null) ? lastUnlockedHighSession.SessionDate : Time[0].Date;
                              // v2.2.5: Pass ATR and Volume Ratio
                              double _atrVal = (atr != null && atr[0] > 0) ? atr[0] : 0;
                              double _volRatio = (Volume[0] > 0 && Volume[1] > 0) ? Volume[0] / Volume[1] : 0; // Simple ratio
                              DrawSignalVisualization(false, sessionHighBarIdx, hVwapPrev, tp1, tp2, qty, slPrice, _setupNameShort, highAnchorSequence, _anchorTimeShort,
                                  CaptureDelta ? _deltaGlobal : 0, _atrVal, _volRatio);

                              // v1.0.8: Use configurable SignalColor instead of session colors
                              Brush sigBrush = SignalColor;

                              // v1.0.5: Use ATR-based offset (same as SessionLevels)
                              double atrOffset = (atr != null && atr[0] > 0) ? atr[0] * LabelDistanceATR : TickSize * 10;

                              // v1.0.5: Position relative to candle High + offset
                              double dotY = High[1] + atrOffset; // Index 1

                                  // Arrow (if ShowSignal2)
                              if (ShowSignal2)
                              {
                                  // Draw.TriangleDown(this, "Sig2H_" + CurrentBar, true, 0, dotY, sigBrush);

                                  // Label: e.g. "Entry 01", "Entry 02"
                                  if (lastUnlockedHighSession != null && ShowSignalText)
                                  {
                                      // v1.0.48: Simplified - only sequence format
                                      // v1.0.50: Sequence already incremented above, use directly
                                      
                                      
                                      // string code = string.Format("Qty: {0}\nEntry {1:00}", qty, highAnchorSequence);
                                      // SimpleFont font = new SimpleFont("Arial", LabelFontSize);
                                      // Draw.Text(this, "Sig2H_Txt_" + CurrentBar, false, code, 0, dotY, LabelTextOffset, sigBrush, font, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                                  }
                              }

                              // v1.0.41: REMOVED - Dispatcher.Invoke causes severe slowdown in playback
                              // Chart refresh not needed - BarBrushes updates automatically
                              if (ShowDebugLogs) Print(string.Format("[DEBUG FLAG] Bar:{0} | SHORT Signal | IsRealtime:{1} | lastSignaledHighAnchorBar={2}", CurrentBar, isRealtimeSignal, lastSignaledHighAnchorBar));
                          }
                          } // v2.2.7: Close else block (reversal logic)
                      }
                  SkipSignal2High:;
                  }

                  // v1.0.29: Persistent Painting - paint the signal bar even after it closes
                  // v1.0.38: Enhanced with refresh to ensure visibility in OnEachTick mode
                  // v1.0.39: Paint all brush types for guaranteed visibility
                  // v1.0.40: Remove BackBrushes (causes vertical bars)
                  if (highSignal2BarIdx >= 0)
                  {
                      int barsAgo = CurrentBar - highSignal2BarIdx;
                      if (barsAgo >= 0 && barsAgo < Bars.Count)
                      {
                          BarBrushes[barsAgo] = Brushes.Yellow;
                          CandleOutlineBrushes[barsAgo] = Brushes.Yellow;
                          // v1.0.41: Removed Dispatcher.Invoke - causes severe slowdown
                      }
                  }
              }

             // --- Low VWAP Logic (Support -> Long Signal) ---
             if (hasLowVWAP && (TradeDirection == TradeDirectionMode.Both || TradeDirection == TradeDirectionMode.LongOnly))
             {
                  // v1.0.49: Use internal VWAP if it exists, otherwise use main VWAP
                  // v1.0.50: Use Values[1] (chart VWAP) instead of currentLowVWAP for consistency
                  double lVwap = (hasInternalLowVWAP && internalLowBarIdx >= 0 && Values[3].IsValidDataPointAt(0))
                      ? Values[3][0]  // Internal LOW VWAP
                      : (Values[1].IsValidDataPointAt(0) ? Values[1][0] : currentLowVWAP);  // Main LOW VWAP from chart
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
                              
                              if (lastUnlockedLowSession != null && ShowSignalText)
                              {
                                   // v1.0.48: Simplified - only show confirmation marker
                                   string entryLabel = "Confirm";
                                   SimpleFont font = new SimpleFont("Arial", LabelFontSize);
                                   Draw.Text(this, "Sig3L_Txt_" + CurrentBar, false, entryLabel, 0, arrowY, -LabelTextOffset, sigBrush, font, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                              }
                          }
                      }

                      lowDetached = false;
                      if (ShowDebugLogs)
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
                          {
                              BarBrushes[barsAgo] = null; // Unpaint that bar
                              CandleOutlineBrushes[barsAgo] = null;
                          }
                          lowSignal2BarIdx = -1;

                          // v1.0.37: Only unpaint current bar if signal was THIS bar
                          // v1.0.40: Remove BackBrushes (causes vertical bars)
                          BarBrushes[0] = null;
                          CandleOutlineBrushes[0] = null;

                          // v1.0.41: RESET tracker when signal is cancelled - allows new signal for same anchor
                          lastSignaledLowAnchorBar = -1;
                          lowLiqGrabLocked = false; // v1.0.45: Unlock Liquidity Grab so label can move again
                          if (ShowDebugLogs) Print(string.Format("[DEBUG FLAG] Bar:{0} | CANCELLED â†’ Reset lastSignaledLowAnchorBar to -1 (allows new signal)", CurrentBar));
                      }
                  }

                 // FINAL DRAW CALL
                 if (ShowDebugLabels && !string.IsNullOrEmpty(dbgText) && dbgText != "D") // Only show "D" or specialized debug.
                 {
                      // Draw.Text(this, "DebugLow" + CurrentBar, dbgText, 0, low - dbgOffset, dbgBrush); // DISABLED to prevent overlap
                 }

                  // v1.0.50: DIAGNOSTIC LOGGING - Log signal detection variables for troubleshooting
                  if (ShowDebugLogs && lowHasTakenRelevant)
                  {
                      double thresholdPrice = lVwap + Signal2ThresholdTicks * TickSize;
                      double separation = Low[0] - lVwap;
                      bool closeCond = Close[0] > lVwap;
                      bool lowCond = Low[0] >= thresholdPrice;

                      if (ShowDebugLogs) Print(string.Format("[DIAG LONG] Bar:{0} | DateTime:{1} | lowHasTaken:{2} | Close:{3:F2}>VWAP:{4:F2}={5} | Low:{6:F2}>=Thresh:{7:F2}={8} | Sep:{9:F2} | lowSig2Fired:{10} | sessionLowBar:{11} | lastSigBar:{12} | lowSeq:{13} | MaxAttempts:{14}",
                          CurrentBar, Time[0].ToString("dd/MM/yy HH:mm:ss"), lowHasTakenRelevant, Close[0], lVwap, closeCond, Low[0], thresholdPrice, lowCond, separation, lowSignal2Fired, sessionLowBarIdx, lastSignaledLowAnchorBar, lowAnchorSequence, GlobalSignal2MaxAttempts));
                  }

                  // V_SIGNAL_2: SECONDARY CONFIRMATION (Yellow Dot) - UNIQUE PER ANCHOR
                  // FIX: Enforce validation ONLY ON BAR CLOSE (IsFirstTickOfBar checking previous bar)
                  // Use Index 1 to validate closed candle state.
                  if (IsFirstTickOfBar && CurrentBar > 0)
                  {
                       // v3.0.1: Trade Window Filter — block signals outside configured hours
                       if (UseTradeWindow)
                       {
                           TimeSpan barTime = Time[1].TimeOfDay;
                           TimeSpan twStart, twEnd;
                           if (TimeSpan.TryParse(TradeWindowStart, out twStart) && TimeSpan.TryParse(TradeWindowEnd, out twEnd))
                           {
                               bool inWindow = (twStart < twEnd)
                                   ? (barTime >= twStart && barTime < twEnd)
                                   : (barTime >= twStart || barTime < twEnd);
                               if (!inWindow)
                               {
                                   if (ShowDebugLogs)
                                       Print(string.Format("[TRADE_WINDOW] Bar:{0} | LOW-side Signal BLOCKED | BarTime:{1} | Window:{2}-{3}", CurrentBar, barTime, twStart, twEnd));
                                   goto SkipSignal2Low;
                               }
                           }
                       }

                       // Recalculate Previous VWAP (Index 1)
                       double lVwapPrev = (hasInternalLowVWAP && internalLowBarIdx >= 0 && Values[3].IsValidDataPointAt(1))
                           ? Values[3][1] : (Values[1].IsValidDataPointAt(1) ? Values[1][1] : currentLowVWAP);

                       // v2.2.7: TREND MODE CHECK - Bearish trend enters SHORT when price breaks BELOW Low VWAP
                       bool trendModeLowSide = IsTrendMode(out bool trendBearishLow);

                       if (trendModeLowSide && trendBearishLow && lowHasTakenRelevant && Close[1] < lVwapPrev)
                       {
                           // TREND SHORT: Buyers lost at Low VWAP → continuation down
                           bool alreadyFiredTrendS = lowSignal2Fired;
                           bool alreadySignaledThisAnchorTrendS = (sessionLowBarIdx == lastSignaledLowAnchorBar);
                           bool sameLevelAsVwapTrendS = (lastUnlockedLowSession == currentLowAnchorSession);
                           bool canFireTrendS = !alreadyFiredTrendS && !alreadySignaledThisAnchorTrendS && sameLevelAsVwapTrendS && (lowAnchorSequence < GlobalSignal2MaxAttempts) && !IsLastSimTradeStillOpen();

                           if (ShowDebugLogs)
                           {
                               string dbgMsg = string.Format("[DEBUG TREND] Bar:{0} | TREND SHORT Check | DeltaGlobal:{1:F0} | SessionDelta:{2:F0} | Close:{3:F2} < VWAP:{4:F2} | CanFire:{5}",
                                   CurrentBar, _deltaGlobal, GetCurrentSessionDelta(), Close[1], lVwapPrev, canFireTrendS);
                               Print(dbgMsg);
                               LogToFile(dbgMsg, "TREND_SHORT");
                           }

                           if (canFireTrendS)
                           {
                               lowSignal2BarIdx = CurrentBar - 1;
                               BarBrushes[1] = TrendTradeColor;
                               CandleOutlineBrushes[1] = TrendTradeColor;

                               bool isRealtimeSignalS = (State == State.Realtime);
                               if (isRealtimeSignalS) SpeakEntrySignal(false, lowAnchorSequence + 1);

                               lowAnchorSequence++;
                               lastSignaledLowAnchorBar = sessionLowBarIdx;
                               if (!AnalyzeAllSignals) lowSignal2Fired = true;
                               lowLiqGrabLocked = true;

                               LogToFile(string.Format("TREND SHORT FIRED | Close:{0:F2} < VWAP:{1:F2} | DeltaGlobal:{2:F0} | Seq:{3}",
                                   Close[1], lVwapPrev, _deltaGlobal, lowAnchorSequence), "TREND_SIGNAL");

                               // TREND MODE: TP is EOD (no fixed TP1/TP2) - use 0 as placeholder
                               double trendTP1S = 0;
                               double trendTP2S = 0;

                               // SL: VWAP origin + offset (if breaks back above VWAP, trend failed)
                               double trendSLS = lVwapPrev + (StopAnchorOffsetTicks * TickSize);
                               int qtyS = CalculateSignalPositionSize(Close[1], trendSLS);

                               string _setupNameTrendShort = (lastUnlockedLowSession != null) ? lastUnlockedLowSession.Name + " Low TREND" : "Unknown TREND";
                               DateTime _anchorTimeTrendShort = (lastUnlockedLowSession != null) ? lastUnlockedLowSession.SessionDate : Time[0].Date;
                               double _atrValS = (atr != null && atr[0] > 0) ? atr[0] : 0;
                               double _volRatioS = (Volume[0] > 0 && Volume[1] > 0) ? Volume[0] / Volume[1] : 0;

                               // v2.2.7: Pass isTrendTrade = true
                               DrawSignalVisualization(false, sessionLowBarIdx, lVwapPrev, trendTP1S, trendTP2S, qtyS, trendSLS, _setupNameTrendShort, lowAnchorSequence, _anchorTimeTrendShort,
                                   CaptureDelta ? _deltaGlobal : 0, _atrValS, _volRatioS, true);

                               if (ShowDebugLogs) Print(string.Format("[DEBUG TREND] Bar:{0} | TREND SHORT Signal Generated | IsRealtime:{1}", CurrentBar, isRealtimeSignalS));
                           }
                       }
                       // REVERSAL MODE: Condition: Active VWAP (Taken), Candle CLOSES above VWAP, Low ABOVE VWAP by threshold
                       // v2.2.7: BLOCK reversals when trend mode is active - only take trend trades in trend conditions
                       else if (lowHasTakenRelevant && Close[1] > lVwapPrev && Low[1] >= (lVwapPrev + Signal2ThresholdTicks * TickSize))
                       {
                           // v2.2.7: Check if blocked by trend mode
                           if (trendModeLowSide)
                           {
                               if (ShowDebugLogs)
                                   Print(string.Format("[REVERSAL_BLOCKED] Bar:{0} | LONG reversal BLOCKED by trend mode | TrendBearish:{1}", CurrentBar, trendBearishLow));
                               // Skip reversal - trend mode active
                           }
                           else
                           {
                          bool alreadyFired = lowSignal2Fired;
                          bool alreadySignaledThisAnchor = (sessionLowBarIdx == lastSignaledLowAnchorBar);
                          bool sameLevelAsVwap = (lastUnlockedLowSession == currentLowAnchorSession);
                          bool canFire = !alreadyFired && !alreadySignaledThisAnchor && sameLevelAsVwap && (lowAnchorSequence < GlobalSignal2MaxAttempts) && !IsLastSimTradeStillOpen();

                          if (ShowDebugLogs)
                          {
                              string dbgMsg = string.Format("[DEBUG FLAG] Bar:{0} | LONG Check (ClosedBar) | CanFire:{1} | Flag:{2} | AnchorSig:{3} | SameLvl:{4} | Seq:{5}",
                                  CurrentBar, canFire, alreadyFired, alreadySignaledThisAnchor, sameLevelAsVwap, lowAnchorSequence);
                              Print(dbgMsg);
                              LogToFile(dbgMsg, "SIG2_PRE_CHECK");
                          }

                          if (canFire)
                          {
                              lowSignal2BarIdx = CurrentBar - 1; // Paint previous bar
                              BarBrushes[1] = Brushes.Yellow;
                              CandleOutlineBrushes[1] = Brushes.Yellow;

                              // v1.0.50: Voice Alert
                              if (State == State.Realtime) SpeakEntrySignal(true, lowAnchorSequence + 1);

                              lowAnchorSequence++;
                              lastSignaledLowAnchorBar = sessionLowBarIdx;
                              // v2.2.5: When AnalyzeAllSignals=true, allow ALL signals to fire (no blocking)
                              if (!AnalyzeAllSignals)
                                  lowSignal2Fired = true;
                              lowLiqGrabLocked = true;

                              LogToFile(string.Format("SIG2 LONG FIRED (CLOSE) | Low:{0:F2} | Seq:{1}", Low[1], lowAnchorSequence), "SIGNAL2");

                              // v1.15.92: Split TP Calculation
                              // v1.15.93: Refined Split TP Calculation (Long)
                              // TP1: Opposite VWAP (High VWAP)
                              double tp1 = (hasHighVWAP && Values[0].IsValidDataPointAt(1)) ? Values[0][1] : (lVwapPrev + 20 * TickSize);
                              
                              // TP2: Opposite Anchor (High Session)
                              double tp2 = tp1;
                              if (hasHighVWAP && sessionHighBarIdx >= 0 && sessionHighBarIdx < CurrentBar)
                              {
                                  tp2 = High.GetValueAt(sessionHighBarIdx);
                                  // Ensure TP2 is actually HIGHER than Entry
                                  if (tp2 <= Low[1]) tp2 = tp1 + 20 * TickSize;
                              }
                              else
                              {
                                  tp2 = tp1 + 20 * TickSize; // Fallback
                              }

                              // Ensure distinct separation
                              if (tp2 < tp1) tp2 = tp1 + 10 * TickSize; // TP2 should be further (Higher)

                              double anchorPriceL = Low.GetValueAt(sessionLowBarIdx);
                              double slPriceL = anchorPriceL - (StopAnchorOffsetTicks * TickSize);
                              int qty = CalculateSignalPositionSize(Close[1], slPriceL);

                              string _setupNameLong = (lastUnlockedLowSession != null) ? lastUnlockedLowSession.Name + " Low" : "Unknown";
                              DateTime _anchorTimeLong = (lastUnlockedLowSession != null) ? lastUnlockedLowSession.SessionDate : Time[0].Date;
                              // v2.2.5: Pass ATR and Volume Ratio
                              double _atrVal = (atr != null && atr[0] > 0) ? atr[0] : 0;
                              double _volRatio = (Volume[0] > 0 && Volume[1] > 0) ? Volume[0] / Volume[1] : 0;
                              DrawSignalVisualization(true, sessionLowBarIdx, lVwapPrev, tp1, tp2, qty, slPriceL, _setupNameLong, lowAnchorSequence, _anchorTimeLong,
                                  CaptureDelta ? _deltaGlobal : 0, _atrVal, _volRatio);

                              Brush sigBrush = SignalColor;
                              double atrOffset = (atr != null && atr[0] > 0) ? atr[0] * LabelDistanceATR : TickSize * 10;
                              double dotY = Low[1] - atrOffset; // Use Previous Low

                              if (ShowSignal2)
                              {
                                  // Draw.TriangleUp(this, "Sig2L_" + lowSignal2BarIdx, true, 1, dotY, sigBrush);
                                  if (ShowSignalText)
                                  {

                                      
                                      // string code = string.Format("Qty: {0}\nEntry {1:00}", qty, lowAnchorSequence);
                                      // SimpleFont font = new SimpleFont("Arial", LabelFontSize);
                                      // Draw.Text(this, "Sig2L_Txt_" + lowSignal2BarIdx, false, code, 1, dotY, -LabelTextOffset, sigBrush, font, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                                  }
                              }
                          }
                          } // v2.2.7: Close else block (reversal logic)
                       }
                  SkipSignal2Low:;
                  }

                  // Persistent Painting
                  if (lowSignal2BarIdx >= 0)
                  {
                      int barsAgo = CurrentBar - lowSignal2BarIdx;
                      if (barsAgo >= 0 && barsAgo < Bars.Count)
                      {
                          BarBrushes[barsAgo] = Brushes.Yellow;
                          CandleOutlineBrushes[barsAgo] = Brushes.Yellow;
                      }
                  }
              }

             // -------------------------------------------------------------
             // v2.1.0: INTERNAL SIGNAL 2 LOGIC
             // -------------------------------------------------------------
             
             // INTERNAL SHORT SIGNAL 2
             if (EnableInternalLogic && hasInternalHighVWAP && internalHighBarIdx >= 0)
             {
                 double iHVwap = Values[2][0];

                 // A) CANCELLATION LOGIC (SAME BAR ONLY)
                 // If the signal JUST Fired in this bar, but then price touched VWAP -> Cancel it.
                 // We do NOT reset 'internalHighSignal2Fired' for future bars. Once fired, it is done for this anchor.
                 if (internalHighSignal2Fired)
                 {
                     if (High[0] >= iHVwap) // Touched VWAP
                     {
                         if (internalHighSignal2BarIdx == CurrentBar)
                         {
                             // IT WAS A FALSE ALARM (Same Bar)
                             internalHighSignal2Fired = false; // Reset Latch to try again if it separates again
                             internalHighSignal2Count--; 
                             if (internalHighSignal2Count < 0) internalHighSignal2Count = 0;
                             
                             // Unlock the anchor so we can try again in this same bar
                             lastSignaledInternalHighBar = -1;

                             // Unpaint
                             BarBrushes[0] = null;
                             CandleOutlineBrushes[0] = null;
                             RemoveDrawObject("IntSig2H_" + CurrentBar);
                         }
                         // ELSE: It was a past signal. We DO NOT reset. The signal stands. "One Shot Per Anchor".
                     }
                 }

                 // B) FIRING LOGIC
                 // B) FIRING LOGIC
                 if (highLiqGrabIsInternal)
                 {
                      // FIX: Enforce validation ONLY ON BAR CLOSE (IsFirstTickOfBar checking previous bar)
                      if (IsFirstTickOfBar && CurrentBar > 0)
                      {
                           // Recalculate Previous VWAP (Index 1) for Internal
                           double iHVwapPrev = (Values[2].IsValidDataPointAt(1)) ? Values[2][1] : iHVwap;
                           
                           if (High[1] <= (iHVwapPrev - Signal2ThresholdTicks * TickSize))
                           {
                               bool isNewAnchor = (internalHighBarIdx != lastSignaledInternalHighBar);
                               bool canFire = (isNewAnchor && !internalHighSignal2Fired && internalHighSignal2Count < InternalSignal2MaxAttempts);
                               
                               if (canFire)
                               {
                                   internalHighSignal2Fired = true;
                                   internalHighSignal2Count++; // Increment
                                   lastSignaledInternalHighBar = internalHighBarIdx; // LOCK this anchor
                                   internalHighSignal2BarIdx = CurrentBar - 1; // Mark previous bar
                                   
                                   // Visuals
                                   BarBrushes[1] = Brushes.Orange;
                                   CandleOutlineBrushes[1] = Brushes.Orange;

                                   if (ShowSignalText)
                                   {
                                       string label = (internalHighSignal2Count > 1) ? "Int (i)" + internalHighSignal2Count : "Int (i)";
                                       Draw.Text(this, "IntSig2H_" + internalHighSignal2BarIdx, label, 1, High[1] + (20 * TickSize), Brushes.Orange);
                                   }
                                   
                                   LogToFile(string.Format("INTERNAL SIG2 SHORT (CLOSE) | High:{0:F2} | Count:{1}", High[1], internalHighSignal2Count), "SIGNAL2");
                               }
                           }
                      }
                      
                      // Persistent Painting
                      if (internalHighSignal2BarIdx >= 0)
                      {
                           int barsAgo = CurrentBar - internalHighSignal2BarIdx;
                           if (barsAgo >= 0 && barsAgo < Bars.Count)
                           {
                               BarBrushes[barsAgo] = Brushes.Orange;
                               CandleOutlineBrushes[barsAgo] = Brushes.Orange;
                           }
                      }
                 }
             }
             
                 // INTERNAL LONG SIGNAL 2
                 if (lowLiqGrabIsInternal && hasInternalLowVWAP && internalLowBarIdx >= 0)
                 {
                      double iLVwap = Values[3][0];
                      
                      // A) CANCELLATION LOGIC (SAME BAR ONLY)
                      if (internalLowSignal2Fired)
                      {
                          if (Low[0] <= iLVwap) // Touched VWAP
                          {
                              if (internalLowSignal2BarIdx == CurrentBar)
                              {
                                  // FALSE ALARM
                                  internalLowSignal2Fired = false;
                                  internalLowSignal2Count--;
                                  if (internalLowSignal2Count < 0) internalLowSignal2Count = 0;
                                  
                                  // Unlock anchor
                                  lastSignaledInternalLowBar = -1;
                                  internalLowSignal2BarIdx = -1; // Reset

                                  BarBrushes[0] = null;
                                  CandleOutlineBrushes[0] = null;
                                  RemoveDrawObject("IntSig2L_" + CurrentBar);
                              }
                          }
                      }

                      // B) FIRING LOGIC
                      if (Low[0] >= (iLVwap + Signal2ThresholdTicks * TickSize))
                      {
                          // FIX: Enforce validation ONLY ON BAR CLOSE (IsFirstTickOfBar checking previous bar)
                          if (IsFirstTickOfBar && CurrentBar > 0)
                          {
                               // Recalculate Previous VWAP (Index 1) for Internal
                               double iLVwapPrev = (Values[3].IsValidDataPointAt(1)) ? Values[3][1] : iLVwap;
                               
                               if (Low[1] >= (iLVwapPrev + Signal2ThresholdTicks * TickSize))
                               {
                                   bool isNewAnchor = (internalLowBarIdx != lastSignaledInternalLowBar);
                                   bool canFire = (isNewAnchor && !internalLowSignal2Fired && internalLowSignal2Count < InternalSignal2MaxAttempts);
                                   
                                   if (canFire)
                                   {
                                       internalLowSignal2Fired = true;
                                       internalLowSignal2Count++;
                                       lastSignaledInternalLowBar = internalLowBarIdx; // LOCK this anchor
                                       internalLowSignal2BarIdx = CurrentBar - 1;
                                       
                                       // Visuals
                                       BarBrushes[1] = Brushes.Orange;
                                       CandleOutlineBrushes[1] = Brushes.Orange;

                                       if (ShowSignalText)
                                       {
                                           string label = (internalLowSignal2Count > 1) ? "Int (i)" + internalLowSignal2Count : "Int (i)";
                                           Draw.Text(this, "IntSig2L_" + internalLowSignal2BarIdx, label, 1, Low[1] - (20 * TickSize), Brushes.Orange);
                                       }
                                       
                                       LogToFile(string.Format("INTERNAL SIG2 LONG (CLOSE) | Low:{0:F2} | Count:{1}", Low[1], internalLowSignal2Count), "SIGNAL2");
                                   }
                               }
                          }
                          
                          // Persistent Painting (Internal Low)
                          if (internalLowSignal2BarIdx >= 0)
                          {
                               int barsAgo = CurrentBar - internalLowSignal2BarIdx;
                               if (barsAgo >= 0 && barsAgo < Bars.Count)
                               {
                                   BarBrushes[barsAgo] = Brushes.Orange;
                                   CandleOutlineBrushes[barsAgo] = Brushes.Orange;
                               }
                          }
                      }
                 }
             // -------------------------------------------------------------

             // v1.0.47: Reset sequence when price crosses OPPOSITE VWAP
             // If SHORT side (highAnchorSequence > 0) and price touches LOW VWAP â†’ reset SHORT sequence
             // v1.0.48: Only reset once per bar to avoid spam in OnEachTick mode
             if (highAnchorSequence > 0 && sessionLowBarIdx >= 0 && lowHasTakenRelevant && CurrentBar != lastHighSeqResetBar)
             {
                 double lVwap = Values[1][0];
                 if (Low[0] <= lVwap)
                 {
                     highAnchorSequence = 0;
                     highLiqGrabSequence = 1;
                     highLiqGrabLocked = false;
                     highLiqGrabBarIdx = -1;
                     lastHighSeqResetBar = CurrentBar; // Track this bar to prevent multiple resets

                     if (ShowDebugLogs) Print(string.Format("[DEBUG VWAP CROSS] Bar:{0} | Touched LOW VWAP | Low:{1:F2} <= VWAP:{2:F2} | Reset highAnchorSequence=0",
                         CurrentBar, Low[0], lVwap));
                 }
             }

             // If LONG side (lowAnchorSequence > 0) and price touches HIGH VWAP â†’ reset LONG sequence
             // v1.0.48: Only reset once per bar to avoid spam in OnEachTick mode
             if (lowAnchorSequence > 0 && sessionHighBarIdx >= 0 && highHasTakenRelevant && CurrentBar != lastLowSeqResetBar)
             {
                 double hVwap = Values[0][0];
                 if (High[0] >= hVwap)
                 {
                     lowAnchorSequence = 0;
                     lowLiqGrabSequence = 1;
                     lowLiqGrabLocked = false;
                     lowLiqGrabBarIdx = -1;
                     lastLowSeqResetBar = CurrentBar; // Track this bar to prevent multiple resets

                     if (ShowDebugLogs) Print(string.Format("[DEBUG VWAP CROSS] Bar:{0} | Touched HIGH VWAP | High:{1:F2} >= VWAP:{2:F2} | Reset lowAnchorSequence=0",
                         CurrentBar, High[0], hVwap));
                 }
             }

             // Version Label (Always Visible)
             if (CurrentBar == Bars.Count - 1)
             {
                 Draw.TextFixed(this, "VersionLabel", "RelativeVwap v" + VERSION, TextPosition.TopLeft, Brushes.White, new SimpleFont("Arial", 12), Brushes.Black, Brushes.Transparent, 100);
             }

             // v3.3.10: Health Cross Trading Status
             if (EnableHealthCrossTrading && ShowSignalText && CurrentBar >= Bars.Count - 2)
             {
                 string crossStatus;
                 Brush statusColor;
                 if (_healthCrossTradeOpen)
                 {
                     string dir = _healthCrossTradeIsLong ? "LONG" : "SHORT";
                     crossStatus = "CROSS TRADE: " + dir + " x" + _healthCrossTradeQty;
                     statusColor = _healthCrossTradeIsLong ? Brushes.Lime : Brushes.Red;
                 }
                 else if (_confirmBarsPending)
                 {
                     string pendDir = _confirmBarsDirection ? "LONG" : "SHORT";
                     crossStatus = "CONFIRMANDO: " + pendDir + " (" + _confirmBarsCount + "/" + HealthCrossConfirmBars + ")";
                     statusColor = Brushes.Cyan;
                 }
                 else
                 {
                     crossStatus = "CROSS TRADE: ESPERANDO";
                     statusColor = Brushes.DimGray;
                 }
                 Draw.TextFixed(this, "CrossTradeStatus", crossStatus, TextPosition.TopRight, statusColor, new SimpleFont("Consolas", 12) { Bold = true }, Brushes.Transparent, Brushes.Black, 80);
             }

             // Status Overlay
             if (ShowDebugLabels && (CurrentBar == Bars.Count - 1))
             {
                 string status = string.Format("RelativeVwap v{0}\nDEBUG STATUS\nTime: {1}\nHigh Active: {2} Locked: {3}\nLow Active: {4} Locked: {5}", VERSION, Time[0], highHasTakenRelevant, highSignalFired, lowHasTakenRelevant, lowSignalFired);
                 Draw.TextFixed(this, "DebugStatus", status, TextPosition.BottomRight, Brushes.White, new SimpleFont("Arial", 12), Brushes.Black, Brushes.Transparent, 100);
             }

              // FIX: Process Pending Signals at the right time
              // The issue: Bars.Count-1 might be 6899, but chart only has data up to bar 6507
              // Solution: Process when State changes to Realtime OR when we're at last bar AND Historical is done loading
              bool shouldProcess = false;
              
              
              // Option 1: Realtime detected
              if (State == State.Realtime && !_signalsProcessed)
              {
                  shouldProcess = true;
              }
              // Option 2: Last bar of Historical data (use BarsInProgress to detect last bar)
              else if (State == State.Historical && CurrentBar == Count - 1 && !_signalsProcessed)
              {
                  shouldProcess = true;
              }
              
              if (shouldProcess)
              {
                  ProcessPendingSignals();
              }

              // --- RelativeMCP observability (v3.0.3) ---
              // Publica estado runtime al inicio de cada barra nueva en realtime.
              // Consumible desde Claude vía get_print_output + get_indicator_state.
              // Try/catch individual — nunca debe romper OnBarUpdate.
              if (State == State.Realtime && IsFirstTickOfBar)
              {
                  try
                  {
                      double vwapH = CurrentBar >= 0 ? Values[0][0] : double.NaN;
                      double vwapL = CurrentBar >= 0 ? Values[1][0] : double.NaN;
                      bool isBearish;
                      bool trendMode = IsTrendMode(out isBearish);
                      double sessionDelta = GetCurrentSessionDelta();
                      // v3.0.3: typeof garantiza el nombre correcto en compile-time
                      string indName = typeof(RelativeVwap).Name;

                      this.RLog(
                          "bar={0} close={1:F2} vwapH={2:F2} vwapL={3:F2} | deltas G={4:F0} A={5:F0} E={6:F0} U={7:F0} sess={8:F0} | trend={9} bearish={10} mode={11} | sig2H={12} sig2L={13}",
                          CurrentBar, Close[0], vwapH, vwapL,
                          _deltaGlobal, _deltaAsia, _deltaEurope, _deltaUSA, sessionDelta,
                          trendMode, isBearish, TradingMode,
                          highSignal2Fired, lowSignal2Fired);

                      RelativeIndicatorRegistry.Publish(
                          string.Format("{0}:{1}:{2}{3}", indName, Instrument.FullName,
                              BarsPeriod.Value, BarsPeriod.BarsPeriodType),
                          new Dictionary<string, object>
                          {
                              ["bar"] = CurrentBar,
                              ["bar_time"] = Time[0],
                              ["close"] = Close[0],
                              ["vwap_high"] = vwapH,
                              ["vwap_low"] = vwapL,
                              ["delta_global"] = _deltaGlobal,
                              ["delta_asia"] = _deltaAsia,
                              ["delta_europe"] = _deltaEurope,
                              ["delta_usa"] = _deltaUSA,
                              ["session_delta"] = sessionDelta,
                              ["trend_mode"] = trendMode,
                              ["trend_bearish"] = isBearish,
                              ["trading_mode"] = TradingMode.ToString(),
                              ["signal2_high_fired"] = highSignal2Fired,
                              ["signal2_low_fired"] = lowSignal2Fired,
                          });
                  }
                  catch { /* no-op: logging nunca debe romper OnBarUpdate */ }
              }
              // --- end RelativeMCP ---

             }
             catch (Exception ex)
             {
                 Print("RelativeVwap Indicator CRASH: " + ex.Message + " | Stack: " + ex.StackTrace);
             }
         }

        // ManageTrades and DrawConnectionLine moved to RelativeVwap.Trading.cs
        // GetBusinessDays and GetSignalCode moved to RelativeVwap.Sessions.cs

        // CloseGhostLines and UpdateSession moved to RelativeVwap.Sessions.cs

        #region Time Zone Helpers
        // Variables kept here for access from all partial classes
        private DateTime CurrentBarDate;
        private TimeZoneInfo _nyTimeZone;
        private DateTime _lastCacheDate = DateTime.MinValue;
        private TimeSpan _cachedAsiaStart;
        private TimeSpan _cachedAsiaEnd;
        private TimeSpan _cachedEuropeStart;
        private TimeSpan _cachedEuropeEnd;
        private TimeSpan _cachedUSStart;
        private TimeSpan _cachedUSEnd;
        // GetTimeByZone, RefreshTimezoneCache, CalculateTime moved to RelativeVwap.Sessions.cs
        #endregion

        #region Smart Label Rendering
        private SharpDX.DirectWrite.Factory dwFactory;
        private SharpDX.DirectWrite.TextFormat textFormat;

        // All rendering methods moved to RelativeVwap.Rendering.cs
        #endregion

        #region Properties
        
        // ========================================================================
        // 01. Configuración Principal
        // ========================================================================
        [NinjaScriptProperty]
        [Display(Name = "Personalidad", Description = "Selecciona el tipo de período: Intraday (sesiones), Semanal, Mensual, Trimestral, Anual", GroupName = "01. Configuración Principal", Order = 0)]
        public PersonalityMode Personality { get; set; } = PersonalityMode.Intraday;

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

        // v3.0.4: US First Hour Rectangle
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Rect Primera Hora US", Description = "Rectángulo de fondo en la primera hora de la sesión americana", GroupName = "02. Sesiones de Tiempo", Order = 27)]
        public bool ShowUSFirstHour { get; set; }

        [NinjaScriptProperty]
        [Range(15, 120)]
        [Display(Name = "Duración Primera Hora (min)", Description = "Minutos desde apertura US para el rectángulo", GroupName = "02. Sesiones de Tiempo", Order = 28)]
        public int USFirstHourMinutes { get; set; }

        [XmlIgnore]
        [Display(Name = "Color Rect Primera Hora", Description = "Color del rectángulo de la primera hora US", GroupName = "02. Sesiones de Tiempo", Order = 29)]
        public Brush USFirstHourColor { get; set; }
        [Browsable(false)] public string USFirstHourColorSerializable { get { return Serialize.BrushToString(USFirstHourColor); } set { USFirstHourColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Opacidad Rect Primera Hora (%)", Description = "Opacidad del rectángulo (1-100%)", GroupName = "02. Sesiones de Tiempo", Order = 30)]
        public int USFirstHourOpacity { get; set; }

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

        [XmlIgnore]
        [Display(Name = "Color VWAP Sesión Anterior", Description = "Color del último par de VWAPs históricos (sesión anterior) para diferenciarlo de los demás", GroupName = "03. Visuales VWAP", Order = 5)]
        public Brush PreviousVWAPColor { get; set; }
        [Browsable(false)] public string PreviousVWAPColorSerializable { get { return Serialize.BrushToString(PreviousVWAPColor); } set { PreviousVWAPColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Display(Name = "Extender Líneas Infinitas", Description = "Extender líneas hasta que sean tocadas", GroupName = "03. Visuales VWAP", Order = 6)]
        public bool ExtendLinesUntilTouch { get; set; }

        [Range(0.5f, 5.0f)]
        [NinjaScriptProperty]
        [Display(Name = "Grosor Líneas Niveles", Description = "Grosor de las líneas horizontales de niveles de sesión (0.5 fino, 2 normal, 5 grueso)", GroupName = "03. Visuales VWAP", Order = 7)]
        public float SessionLevelThickness { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Etiqueta VWAP High", Description = "Texto para la línea VWAP superior (ej. Supply)", GroupName = "03. Visuales VWAP", Order = 9)]
        public string HighVwapLabel { get; set; } = "Supply";

        [NinjaScriptProperty]
        [Display(Name = "Etiqueta VWAP Low", Description = "Texto para la línea VWAP inferior (ej. Demand)", GroupName = "03. Visuales VWAP", Order = 10)]
        public string LowVwapLabel { get; set; } = "Demand";

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Días Atrás", Description = "Muestra 'X days' en lugar de fecha", GroupName = "03. Visuales VWAP", Order = 8)]
        public bool ShowDaysAgo { get; set; }

        // ========================================================================
        // 04. Señales y Textos
        // ========================================================================
        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Dirección de Trades", GroupName = "04. Señales y Textos", Order = 1)]
        public TradeDirectionMode TradeDirection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Etiquetas", GroupName = "04. Señales y Textos", Order = 2)]
        public bool ShowLabels { get; set; }

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Modo Etiquetas", Description = "Selecciona el modo de visualización de etiquetas", GroupName = "04. Señales y Textos", Order = 3)]
        public LabelMode LabelDisplayMode { get; set; } = LabelMode.Default;

        [NinjaScriptProperty]
        [Display(Name = "Pintar Velas Demanda/Oferta", Description = "Pinta velas blancas cuando demanda > oferta, transparentes cuando oferta > demanda", GroupName = "04. Señales y Textos", Order = 10)]
        public bool ShowDemandSupplyCandles { get; set; } = true;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Texto Señal 1", Description = "Texto para señal de ruptura (ej. 'Liquidity Grabbed')", GroupName = "04. Señales y Textos", Order = 31)]
        public string CustomSignal1Text { get; set; } = "Liquidity Grabbed";

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Texto Señal 2", Description = "Texto para señal confirmada (ej. 'Entry 1')", GroupName = "04. Señales y Textos", Order = 32)]
        public string CustomSignal2Text { get; set; } = "Entry 1";

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Texto Señal 3", Description = "Texto para señal de re-test (ej. 'Entry 2')", GroupName = "04. Señales y Textos", Order = 33)]
        public string CustomSignal3Text { get; set; } = "Entry 2";

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Etiquetas Señal", Description = "Muestra iconos y líneas de señales", GroupName = "04. Señales y Textos", Order = 4)]
        public bool ShowSignalLabels { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Textos Señal", Description = "Muestra textos de señales (Entry, TP1, SL, PEND LONG/SHORT, CROSS TRADE status)", GroupName = "04. Señales y Textos", Order = 5)]
        public bool ShowSignalText { get; set; } = false;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Trades Simulados", Description = "Muestra líneas, iconos y etiquetas de trades simulados históricos", GroupName = "04. Señales y Textos", Order = 6)]
        public bool ShowTradeVisualization { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Salud VWAP", Description = "Muestra score de fortaleza del VWAP (MFE/MAE ratio) con barra visual", GroupName = "04. Señales y Textos", Order = 7)]
        public bool ShowVwapHealth { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Salud: Offset Barras", Description = "Barras hacia atras desde el final del VWAP para colocar el label (negativo = izquierda)", GroupName = "04. Señales y Textos", Order = 8)]
        public int HealthLabelOffsetBars { get; set; } = -15;

        [NinjaScriptProperty]
        [Display(Name = "Salud: Offset Ticks", Description = "Ticks de separacion vertical del VWAP (positivo = arriba para Supply, abajo para Demand)", GroupName = "04. Señales y Textos", Order = 9)]
        public int HealthLabelOffsetTicks { get; set; } = 40;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Señal 1 (Ruptura)", Description = "Muestra la señal de toma de liquidez", GroupName = "04. Señales y Textos", Order = 40)]
        public bool ShowSignal1 { get; set; } = false;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Señal 2 (Confir.)", Description = "Muestra la señal de entrada 1", GroupName = "04. Señales y Textos", Order = 41)]
        public bool ShowSignal2 { get; set; } = false;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Señal 3 (Re-test)", Description = "Muestra la señal de entrada 2", GroupName = "04. Señales y Textos", Order = 42)]
        public bool ShowSignal3 { get; set; } = false;

        [Browsable(false)]
        [XmlIgnore]
        [Display(Name = "Color Señales", Description = "Color para flechas y textos de señal", GroupName = "04. Señales y Textos", Order = 13)]
        public Brush SignalColor { get; set; } = Brushes.White;
        [Browsable(false)] public string SignalColorSerializable { get { return Serialize.BrushToString(SignalColor); } set { SignalColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Color Fondo Etiquetas", GroupName = "04. Señales y Textos", Order = 14)]
        public Brush LabelBackgroundColor { get; set; } = Brushes.MidnightBlue;
        [Browsable(false)] public string LabelBackgroundColorSerializable { get { return Serialize.BrushToString(LabelBackgroundColor); } set { LabelBackgroundColor = Serialize.StringToBrush(value); } }

        [Browsable(false)]
        [NinjaScriptProperty]
        [Range(6, 24)]
        [Display(Name = "Tamaño Fuente Señal", GroupName = "04. Señales y Textos", Order = 15)]
        public int LabelFontSize { get; set; } = 12;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Range(-50, 50)]
        [Display(Name = "Offset Texto (px)", GroupName = "04. Señales y Textos", Order = 16)]
        public int LabelTextOffset { get; set; } = 10;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Distancia Etiqueta ATR", Description = "Multiplicador ATR para distancia desde precio", GroupName = "04. Señales y Textos", Order = 17)]
        public double LabelDistanceATR { get; set; } = 0.3;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Range(0.5, 5.0)]
        [Display(Name = "Espaciado Colisión", GroupName = "04. Señales y Textos", Order = 18)]
        public double LabelCollisionSpacing { get; set; } = 1.5;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Ticks de Separación", Description = "Ticks mínimos requeridos entre High/Low y VWAP para considerar 'Detached'", GroupName = "04. Señales y Textos", Order = 19)]
        public int DetachmentTicks { get; set; } = 2;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Umbral Señal 2", Description = "Ticks requeridos para cierre dentro del VWAP", GroupName = "04. Señales y Textos", Order = 20)]
        public int Signal2ThresholdTicks { get; set; } = 1;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Intentos Globales", Description = "Máximas señales permitidas por nivel externo", GroupName = "04. Señales y Textos", Order = 21)]
        public int GlobalSignal2MaxAttempts { get; set; } = 10;

        // ========================================================================
        // 05. Estudio de Toques
        // ========================================================================
        [Browsable(false)]
        [Display(Name = "Template", Description = "Estudio: captura datos crudos (todo abierto, CSV activado). Auto: trading optimizado, adapta SL/TP segun ATR. Conservador/Equilibrado/Agresivo/MaxTrades/BajaVol: presets fijos.", GroupName = "05. Estudio de Toques", Order = 0)]
        public TouchStudyTemplate StudyTemplate { get; set; } = TouchStudyTemplate.Custom;

        [Browsable(false)]
        [Display(Name = "Mostrar Estudio Toques", Description = "Muestra labels H:X.X L:X.X en primer toque tras separacion significativa", GroupName = "05. Estudio de Toques", Order = 1)]
        public bool ShowTouchStudy { get; set; } = false;

        [Browsable(false)]
        [Display(Name = "Estudio: Dias", Description = "Solo mostrar toques de los ultimos N dias", GroupName = "05. Estudio de Toques", Order = 2)]
        public int TouchStudyDays { get; set; } = 3;

        [Browsable(false)]
        [Display(Name = "Estudio: Separacion ATR", Description = "Multiplicador ATR para considerar despegue significativo (1.0 = 1x ATR)", GroupName = "05. Estudio de Toques", Order = 3)]
        public double TouchStudySeparationATR { get; set; } = 1.0;

        [Browsable(false)]
        [Display(Name = "Estudio: Proximidad Ticks", Description = "Ticks de proximidad al VWAP para considerar toque", GroupName = "05. Estudio de Toques", Order = 4)]
        public int TouchStudyProximityTicks { get; set; } = 3;

        [Browsable(false)]
        [Display(Name = "Estudio: Filtro Config", Description = "All=todos, A=LONG breakout, B=SHORT breakout, C=SHORT reversal, D=LONG reversal, CD=reversals, BC=shorts, AD=longs", GroupName = "05. Estudio de Toques", Order = 5)]
        public TouchStudyFilterMode TouchStudyFilter { get; set; } = TouchStudyFilterMode.All;

        [Browsable(false)]
        [Display(Name = "Estudio: SL Ticks", Description = "Stop Loss en ticks para simulacion de trade", GroupName = "05. Estudio de Toques", Order = 6)]
        public int TouchStudySLTicks { get; set; } = 24;

        [Browsable(false)]
        [Display(Name = "Estudio: TP Ticks", Description = "Take Profit en ticks para simulacion de trade", GroupName = "05. Estudio de Toques", Order = 7)]
        public int TouchStudyTPTicks { get; set; } = 38;

        [Browsable(false)]
        [Display(Name = "Estudio: Gap Episodio (barras)", Description = "Barras minimas entre toques para considerar nuevo episodio", GroupName = "05. Estudio de Toques", Order = 8)]
        public int TouchStudyEpisodeGap { get; set; } = 15;

        [Browsable(false)]
        [Range(0, 10)]
        [Display(Name = "Estudio: Max ATR", Description = "Filtrar toques con ATR mayor a este valor (0=sin filtro). Estudio 2025: ATR<2.5 sube WR a 81%, ATR<1.5 sube a 90%.", GroupName = "05. Estudio de Toques", Order = 10)]
        public double TouchStudyMaxATR { get; set; } = 0;

        [Browsable(false)]
        [Range(0, 200)]
        [Display(Name = "Estudio: Max Separacion", Description = "Filtrar toques con separacion mayor a N ticks (0=sin filtro). Estudio 2025: Sep<20 sube WR a 77%, Sep<10 sube a 93%.", GroupName = "05. Estudio de Toques", Order = 11)]
        public int TouchStudyMaxSeparation { get; set; } = 0;

        [Range(0, 10.0)]
        [Display(Name = "Health: Umbral Fuerte", Description = "Health score minimo para considerar un VWAP 'fuerte'. 2.0 = optimo.", GroupName = "08. Health Cross", Order = 20)]
        public double HealthStrongThreshold { get; set; } = 2.0;

        [Range(0, 10.0)]
        [Display(Name = "Health: Umbral Debil", Description = "Health score maximo para considerar un VWAP 'debil'. 1.5 = optimo.", GroupName = "08. Health Cross", Order = 21)]
        public double HealthWeakThreshold { get; set; } = 1.5;

        [Browsable(false)]
        [XmlIgnore]
        [Display(Name = "Estudio: Color", Description = "Color del label de estudio de toques", GroupName = "05. Estudio de Toques", Order = 12)]
        public Brush TouchStudyColor { get; set; } = Brushes.Cyan;
        [Browsable(false)]
        public string TouchStudyColorSerializable
        {
            get { return Serialize.BrushToString(TouchStudyColor); }
            set { TouchStudyColor = Serialize.StringToBrush(value); }
        }

        // ========================================================================
        // 06. Señales Internas
        // ========================================================================
        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Habilitar Señales Internas", Description = "Activa o desactiva toda la lógica interna (Grabs, Velas Naranjas, VWAPs internos)", GroupName = "06. Señales Internas", Order = 1)]
        public bool EnableInternalLogic { get; set; } = false;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Intentos Señal 2", Description = "Máximas señales permitidas por nivel interno", GroupName = "06. Señales Internas", Order = 2)]
        public int InternalSignal2MaxAttempts { get; set; } = 4;

        // ========================================================================
        // 07. Exportación y Delta
        // ========================================================================
        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Exportar Simulación CSV", Description = "Exporta trades simulados (Signal 2) a CSV compatible con Streamlit Audit. Se escribe al cargar el chart.", GroupName = "07. Exportación y Delta", Order = 1)]
        public bool ExportSimulationCSV { get; set; } = false;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Analizar Todas las Señales", Description = "Registra TODAS las señales Signal 2, incluso superpuestas. Útil para análisis estadístico completo. Genera columna Overlapping en CSV.", GroupName = "07. Exportación y Delta", Order = 2)]
        public bool AnalyzeAllSignals { get; set; } = false;

        [Browsable(false)]
        [Display(Name = "Exportar Estudio Toques CSV", Description = "Exporta primer toque post-separacion con health scores de ambos VWAPs a CSV.", GroupName = "07. Exportación y Delta", Order = 3)]
        public bool ExportTouchStudyCSV { get; set; } = false;

        [Browsable(false)]
        [Display(Name = "Modo RAW (MFE/MAE sin truncar)", Description = "Exporta MFE/MAE hasta EOD sin cortar por SL/TP + precio del otro VWAP + path snapshots a 5,10,20,50,100,200 barras. Requiere ExportTouchStudyCSV activo.", GroupName = "07. Exportación y Delta", Order = 3)]
        public bool TouchStudyRawMode { get; set; } = false;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Exportar Aproximaciones VWAP", Description = "Exporta cada toque al VWAP con MFE/MAE hasta EOD para análisis de dominancia.", GroupName = "07. Exportación y Delta", Order = 4)]
        public bool ExportVwapApproaches { get; set; } = false;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Separación Ticks", Description = "Precio debe cerrar a esta distancia del VWAP antes de contar otro toque (0=deshabilitado). Elimina toques ruido.", GroupName = "07. Exportación y Delta", Order = 5)]
        public int ApproachSeparationTicks { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "Capturar Delta", Description = "Calcula 4 deltas: DeltaGlobal (día), DeltaAsia, DeltaEurope, DeltaUSA. Columnas separadas en CSV.", GroupName = "07. Exportación y Delta", Order = 6)]
        public bool CaptureDelta { get; set; } = true; // v3.0.3: default true para observabilidad vía RelativeMCP

        [NinjaScriptProperty]
        [Range(0, 5)]
        [Display(Name = "EOD Offset Horas (Invierno)", Description = "Horas a sumar al USEndTime en invierno (sin DST) para cerrar trades. Ej: 1 = cierra 1h después del valor configurado.", GroupName = "07. Exportación y Delta", Order = 7)]
        public int EodWinterOffsetHours { get; set; } = 1;

        // ========================================================================
        // 08. Alertas y Debug
        // ========================================================================
        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Habilitar Alertas", GroupName = "08. Alertas y Debug", Order = 1)]
        public bool EnableAlerts { get; set; }

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Sonido Alerta", GroupName = "08. Alertas y Debug", Order = 2)]
        public string AlertSound { get; set; }

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Labels Debug", GroupName = "08. Alertas y Debug", Order = 3)]
        public bool ShowDebugLabels { get; set; }

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Logging a Archivo", Description = "Escribe logs detallados a trace/RelativeVwap/RelativeVwap_Debug_YYYYMMDD.txt", GroupName = "08. Alertas y Debug", Order = 4)]
        public bool EnableFileLogging { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Alerta Cruce Health", Description = "Reproduce sonido cuando Demand cruza sobre Supply o viceversa", GroupName = "08. Health Cross", Order = 5)]
        public bool EnableHealthCrossAlert { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Exportar Cruces CSV", Description = "Exporta señales de cruce Demand/Supply con MFE/MAE a VWAP_CROSS_{SYMBOL}.csv", GroupName = "08. Health Cross", Order = 6)]
        public bool ExportHealthCrossCSV { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0, 240)]
        [Display(Name = "Blackout Cruces (min)", Description = "Minutos tras inicio de sesión donde se ignoran cruces (VWAPs crudos)", GroupName = "08. Health Cross", Order = 7)]
        public int HealthCrossBlackoutMinutes { get; set; } = 0;

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Confirmar Cruce (barras)", Description = "Barras de espera tras cruce detectado. Si el cruce sigue vigente tras N barras → se confirma. 0 = inmediato.", GroupName = "08. Health Cross", Order = 8)]
        public int HealthCrossConfirmBars { get; set; } = 30;

        [NinjaScriptProperty]
        [Display(Name = "Operar Cruces Health", Description = "Envía órdenes market reales al confirmar cruce Health Score. Sale por cruce opuesto o EOD. SOLO en Realtime.", GroupName = "08. Health Cross", Order = 9)]
        public bool EnableHealthCrossTrading { get; set; } = false;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Show Debug Logs", Description = "Enables detailed logging to Output window. Disable for better performance.", GroupName = "08. Alertas y Debug", Order = 10)]
        [XmlIgnore]
        public bool ShowDebugLogs { get; set; }

        // ========================================================================
        // 10. TDU Integration (Health Cross bias + TDU Price Action breakout)
        // ========================================================================

        [NinjaScriptProperty]
        [Display(Name = "Activar TDU+Health", Description = "Usa Health Cross como sesgo direccional + TDU Price Action para timing de entrada por breakout de estructura.", GroupName = "10. TDU Integration", Order = 1)]
        public bool EnableTDUTrading { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Exportar TDU CSV", Description = "Exporta trades TDU a TDU_CROSS_{SYMBOL}.csv con MFE/MAE/PnL.", GroupName = "10. TDU Integration", Order = 2)]
        public bool ExportTDUCSV { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Usar SL del TDU", Description = "Usa el StopLoss del TDU como protección. Si se desactiva, solo sale por EOD.", GroupName = "10. TDU Integration", Order = 3)]
        public bool TDUUseSL { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Órdenes Reales TDU", Description = "Envía órdenes market reales al detectar señal TDU alineada con Health. SOLO en Realtime.", GroupName = "10. TDU Integration", Order = 4)]
        public bool EnableTDULiveOrders { get; set; } = false;

        // ========================================================================
        // 09. Contador
        // ========================================================================
        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Contador", GroupName = "09. Contador", Order = 1)]
        public bool ShowCountdown { get; set; } = true;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Modo Cuenta Regresiva", GroupName = "09. Contador", Order = 2)]
        public bool CountDown { get; set; } = true;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Porcentaje", GroupName = "09. Contador", Order = 3)]
        public bool ShowPercent { get; set; } = false;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Tamaño Fuente", GroupName = "09. Contador", Order = 4)]
        public int CountdownFontSize { get; set; } = 12;

        [Browsable(false)]
        [XmlIgnore]
        [Display(Name = "Color Texto", GroupName = "09. Contador", Order = 5)]
        public Brush CountdownTextColor { get; set; } = Brushes.White;
        [Browsable(false)] public string CountdownTextColorSerializable { get { return Serialize.BrushToString(CountdownTextColor); } set { CountdownTextColor = Serialize.StringToBrush(value); } }

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Offset X (px)", GroupName = "09. Contador", Order = 6)]
        public int CountdownOffsetX { get; set; } = 20;

        [Browsable(false)]
        [NinjaScriptProperty]
        [Display(Name = "Offset Y (ticks)", GroupName = "09. Contador", Order = 7)]
        public int CountdownOffsetY { get; set; } = 10;

        // ========================================================================
        // 10. Period Personalities
        // ========================================================================
        [NinjaScriptProperty]
        [Display(Name = "Week Start Day", Description = "Día de inicio de semana para personalidad Weekly (Lunes por defecto, ISO 8601)", GroupName = "10. Period Personalities", Order = 1)]
        public DayOfWeek WeekStartDay { get; set; } = DayOfWeek.Monday;

        [NinjaScriptProperty]
        [Range(1, 52)]
        [Display(Name = "Weekly History (weeks)", Description = "Número de semanas de historia a mostrar en modo Weekly", GroupName = "10. Period Personalities", Order = 10)]
        public int WeeklyHistoryWeeks { get; set; } = 8;

        [NinjaScriptProperty]
        [Range(1, 24)]
        [Display(Name = "Monthly History (months)", Description = "Número de meses de historia a mostrar en modo Monthly", GroupName = "10. Period Personalities", Order = 20)]
        public int MonthlyHistoryMonths { get; set; } = 6;

        [NinjaScriptProperty]
        [Range(1, 12)]
        [Display(Name = "Quarterly History (quarters)", Description = "Número de trimestres de historia a mostrar en modo Quarterly", GroupName = "10. Period Personalities", Order = 30)]
        public int QuarterlyHistoryQuarters { get; set; } = 4;

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Yearly History (years)", Description = "Número de años de historia a mostrar en modo Yearly", GroupName = "10. Period Personalities", Order = 40)]
        public int YearlyHistoryYears { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name = "Show Period Highs", Description = "Mostrar líneas de máximos del período", GroupName = "10. Period Personalities", Order = 50)]
        public bool ShowPeriodHigh { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Show Period Lows", Description = "Mostrar líneas de mínimos del período", GroupName = "10. Period Personalities", Order = 51)]
        public bool ShowPeriodLow { get; set; } = true;

        [XmlIgnore]
        [Display(Name = "Period Line Color", Description = "Color de las líneas de período", GroupName = "10. Period Personalities", Order = 60)]
        public Brush PeriodLineColor { get; set; } = Brushes.Goldenrod;
        [Browsable(false)] public string PeriodLineColorSerializable { get { return Serialize.BrushToString(PeriodLineColor); } set { PeriodLineColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Period Label Color", Description = "Color de las etiquetas de período", GroupName = "10. Period Personalities", Order = 61)]
        public Brush PeriodLabelColor { get; set; } = Brushes.White;
        [Browsable(false)] public string PeriodLabelColorSerializable { get { return Serialize.BrushToString(PeriodLabelColor); } set { PeriodLabelColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Display(Name = "Show Period Dividers", Description = "Mostrar líneas divisorias verticales al inicio de cada período", GroupName = "10. Period Personalities", Order = 70)]
        public bool ShowPeriodDividers { get; set; } = true;

        [XmlIgnore]
        [Display(Name = "Period Divider Color", Description = "Color de las líneas divisorias de período", GroupName = "10. Period Personalities", Order = 71)]
        public Brush PeriodDividerColor { get; set; } = Brushes.DimGray;
        [Browsable(false)] public string PeriodDividerColorSerializable { get { return Serialize.BrushToString(PeriodDividerColor); } set { PeriodDividerColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Display(Name = "Show Period Marker", Description = "Mostrar triángulo marcador en la parte inferior de divisorias", GroupName = "10. Period Personalities", Order = 72)]
        public bool ShowPeriodMarker { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Daily Divider Time", Description = "Hora de inicio de sesión ETH para línea divisoria diaria en Intraday (formato HH:mm)", GroupName = "10. Period Personalities", Order = 73)]
        public string DailyDividerTime { get; set; } = "19:00";

        #endregion

        // OnTimerTick and CalculateCountdown moved to RelativeVwap.Utilities.cs
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RelativeIndicators.RelativeVwap[] cacheRelativeVwap;
		public RelativeIndicators.RelativeVwap RelativeVwap(PersonalityMode personality, VwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, bool showAsia, string asiaStartTime, string asiaEndTime, bool showAsiaHigh, bool showAsiaLow, bool showEurope, string europeStartTime, string europeEndTime, bool showEuropeHigh, bool showEuropeLow, bool showUS, string uSStartTime, string uSEndTime, bool showUSHigh, bool showUSLow, bool showUSFirstHour, int uSFirstHourMinutes, int uSFirstHourOpacity, float historicalVWAPThickness, bool extendLinesUntilTouch, float sessionLevelThickness, string highVwapLabel, string lowVwapLabel, bool showDaysAgo, TradeDirectionMode tradeDirection, bool showLabels, LabelMode labelDisplayMode, bool showDemandSupplyCandles, string customSignal1Text, string customSignal2Text, string customSignal3Text, bool showSignalLabels, bool showSignalText, bool showTradeVisualization, bool showVwapHealth, int healthLabelOffsetBars, int healthLabelOffsetTicks, bool showSignal1, bool showSignal2, bool showSignal3, int labelFontSize, int labelTextOffset, double labelDistanceATR, double labelCollisionSpacing, int detachmentTicks, int signal2ThresholdTicks, int globalSignal2MaxAttempts, bool enableInternalLogic, int internalSignal2MaxAttempts, bool exportSimulationCSV, bool analyzeAllSignals, bool exportVwapApproaches, int approachSeparationTicks, bool captureDelta, int eodWinterOffsetHours, bool enableAlerts, string alertSound, bool showDebugLabels, bool enableFileLogging, bool enableHealthCrossAlert, int healthCrossBlackoutMinutes, int healthCrossConfirmBars, bool enableHealthCrossTrading, bool showDebugLogs, bool enableTDUTrading, bool tDUUseSL, bool enableTDULiveOrders, bool showCountdown, bool countDown, bool showPercent, int countdownFontSize, int countdownOffsetX, int countdownOffsetY, DayOfWeek weekStartDay, int weeklyHistoryWeeks, int monthlyHistoryMonths, int quarterlyHistoryQuarters, int yearlyHistoryYears, bool showPeriodHigh, bool showPeriodLow, bool showPeriodDividers, bool showPeriodMarker, string dailyDividerTime)
		{
			return RelativeVwap(Input, personality, vwapMethod, maxHistoryDays, useExchangeTime, showAsia, asiaStartTime, asiaEndTime, showAsiaHigh, showAsiaLow, showEurope, europeStartTime, europeEndTime, showEuropeHigh, showEuropeLow, showUS, uSStartTime, uSEndTime, showUSHigh, showUSLow, showUSFirstHour, uSFirstHourMinutes, uSFirstHourOpacity, historicalVWAPThickness, extendLinesUntilTouch, sessionLevelThickness, highVwapLabel, lowVwapLabel, showDaysAgo, tradeDirection, showLabels, labelDisplayMode, showDemandSupplyCandles, customSignal1Text, customSignal2Text, customSignal3Text, showSignalLabels, showSignalText, showTradeVisualization, showVwapHealth, healthLabelOffsetBars, healthLabelOffsetTicks, showSignal1, showSignal2, showSignal3, labelFontSize, labelTextOffset, labelDistanceATR, labelCollisionSpacing, detachmentTicks, signal2ThresholdTicks, globalSignal2MaxAttempts, enableInternalLogic, internalSignal2MaxAttempts, exportSimulationCSV, analyzeAllSignals, exportVwapApproaches, approachSeparationTicks, captureDelta, eodWinterOffsetHours, enableAlerts, alertSound, showDebugLabels, enableFileLogging, enableHealthCrossAlert, healthCrossBlackoutMinutes, healthCrossConfirmBars, enableHealthCrossTrading, showDebugLogs, enableTDUTrading, tDUUseSL, enableTDULiveOrders, showCountdown, countDown, showPercent, countdownFontSize, countdownOffsetX, countdownOffsetY, weekStartDay, weeklyHistoryWeeks, monthlyHistoryMonths, quarterlyHistoryQuarters, yearlyHistoryYears, showPeriodHigh, showPeriodLow, showPeriodDividers, showPeriodMarker, dailyDividerTime);
		}

		public RelativeIndicators.RelativeVwap RelativeVwap(ISeries<double> input, PersonalityMode personality, VwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, bool showAsia, string asiaStartTime, string asiaEndTime, bool showAsiaHigh, bool showAsiaLow, bool showEurope, string europeStartTime, string europeEndTime, bool showEuropeHigh, bool showEuropeLow, bool showUS, string uSStartTime, string uSEndTime, bool showUSHigh, bool showUSLow, bool showUSFirstHour, int uSFirstHourMinutes, int uSFirstHourOpacity, float historicalVWAPThickness, bool extendLinesUntilTouch, float sessionLevelThickness, string highVwapLabel, string lowVwapLabel, bool showDaysAgo, TradeDirectionMode tradeDirection, bool showLabels, LabelMode labelDisplayMode, bool showDemandSupplyCandles, string customSignal1Text, string customSignal2Text, string customSignal3Text, bool showSignalLabels, bool showSignalText, bool showTradeVisualization, bool showVwapHealth, int healthLabelOffsetBars, int healthLabelOffsetTicks, bool showSignal1, bool showSignal2, bool showSignal3, int labelFontSize, int labelTextOffset, double labelDistanceATR, double labelCollisionSpacing, int detachmentTicks, int signal2ThresholdTicks, int globalSignal2MaxAttempts, bool enableInternalLogic, int internalSignal2MaxAttempts, bool exportSimulationCSV, bool analyzeAllSignals, bool exportVwapApproaches, int approachSeparationTicks, bool captureDelta, int eodWinterOffsetHours, bool enableAlerts, string alertSound, bool showDebugLabels, bool enableFileLogging, bool enableHealthCrossAlert, int healthCrossBlackoutMinutes, int healthCrossConfirmBars, bool enableHealthCrossTrading, bool showDebugLogs, bool enableTDUTrading, bool tDUUseSL, bool enableTDULiveOrders, bool showCountdown, bool countDown, bool showPercent, int countdownFontSize, int countdownOffsetX, int countdownOffsetY, DayOfWeek weekStartDay, int weeklyHistoryWeeks, int monthlyHistoryMonths, int quarterlyHistoryQuarters, int yearlyHistoryYears, bool showPeriodHigh, bool showPeriodLow, bool showPeriodDividers, bool showPeriodMarker, string dailyDividerTime)
		{
			if (cacheRelativeVwap != null)
				for (int idx = 0; idx < cacheRelativeVwap.Length; idx++)
					if (cacheRelativeVwap[idx] != null && cacheRelativeVwap[idx].Personality == personality && cacheRelativeVwap[idx].VwapMethod == vwapMethod && cacheRelativeVwap[idx].MaxHistoryDays == maxHistoryDays && cacheRelativeVwap[idx].UseExchangeTime == useExchangeTime && cacheRelativeVwap[idx].ShowAsia == showAsia && cacheRelativeVwap[idx].AsiaStartTime == asiaStartTime && cacheRelativeVwap[idx].AsiaEndTime == asiaEndTime && cacheRelativeVwap[idx].ShowAsiaHigh == showAsiaHigh && cacheRelativeVwap[idx].ShowAsiaLow == showAsiaLow && cacheRelativeVwap[idx].ShowEurope == showEurope && cacheRelativeVwap[idx].EuropeStartTime == europeStartTime && cacheRelativeVwap[idx].EuropeEndTime == europeEndTime && cacheRelativeVwap[idx].ShowEuropeHigh == showEuropeHigh && cacheRelativeVwap[idx].ShowEuropeLow == showEuropeLow && cacheRelativeVwap[idx].ShowUS == showUS && cacheRelativeVwap[idx].USStartTime == uSStartTime && cacheRelativeVwap[idx].USEndTime == uSEndTime && cacheRelativeVwap[idx].ShowUSHigh == showUSHigh && cacheRelativeVwap[idx].ShowUSLow == showUSLow && cacheRelativeVwap[idx].ShowUSFirstHour == showUSFirstHour && cacheRelativeVwap[idx].USFirstHourMinutes == uSFirstHourMinutes && cacheRelativeVwap[idx].USFirstHourOpacity == uSFirstHourOpacity && cacheRelativeVwap[idx].HistoricalVWAPThickness == historicalVWAPThickness && cacheRelativeVwap[idx].ExtendLinesUntilTouch == extendLinesUntilTouch && cacheRelativeVwap[idx].SessionLevelThickness == sessionLevelThickness && cacheRelativeVwap[idx].HighVwapLabel == highVwapLabel && cacheRelativeVwap[idx].LowVwapLabel == lowVwapLabel && cacheRelativeVwap[idx].ShowDaysAgo == showDaysAgo && cacheRelativeVwap[idx].TradeDirection == tradeDirection && cacheRelativeVwap[idx].ShowLabels == showLabels && cacheRelativeVwap[idx].LabelDisplayMode == labelDisplayMode && cacheRelativeVwap[idx].ShowDemandSupplyCandles == showDemandSupplyCandles && cacheRelativeVwap[idx].CustomSignal1Text == customSignal1Text && cacheRelativeVwap[idx].CustomSignal2Text == customSignal2Text && cacheRelativeVwap[idx].CustomSignal3Text == customSignal3Text && cacheRelativeVwap[idx].ShowSignalLabels == showSignalLabels && cacheRelativeVwap[idx].ShowSignalText == showSignalText && cacheRelativeVwap[idx].ShowTradeVisualization == showTradeVisualization && cacheRelativeVwap[idx].ShowVwapHealth == showVwapHealth && cacheRelativeVwap[idx].HealthLabelOffsetBars == healthLabelOffsetBars && cacheRelativeVwap[idx].HealthLabelOffsetTicks == healthLabelOffsetTicks && cacheRelativeVwap[idx].ShowSignal1 == showSignal1 && cacheRelativeVwap[idx].ShowSignal2 == showSignal2 && cacheRelativeVwap[idx].ShowSignal3 == showSignal3 && cacheRelativeVwap[idx].LabelFontSize == labelFontSize && cacheRelativeVwap[idx].LabelTextOffset == labelTextOffset && cacheRelativeVwap[idx].LabelDistanceATR == labelDistanceATR && cacheRelativeVwap[idx].LabelCollisionSpacing == labelCollisionSpacing && cacheRelativeVwap[idx].DetachmentTicks == detachmentTicks && cacheRelativeVwap[idx].Signal2ThresholdTicks == signal2ThresholdTicks && cacheRelativeVwap[idx].GlobalSignal2MaxAttempts == globalSignal2MaxAttempts && cacheRelativeVwap[idx].EnableInternalLogic == enableInternalLogic && cacheRelativeVwap[idx].InternalSignal2MaxAttempts == internalSignal2MaxAttempts && cacheRelativeVwap[idx].ExportSimulationCSV == exportSimulationCSV && cacheRelativeVwap[idx].AnalyzeAllSignals == analyzeAllSignals && cacheRelativeVwap[idx].ExportVwapApproaches == exportVwapApproaches && cacheRelativeVwap[idx].ApproachSeparationTicks == approachSeparationTicks && cacheRelativeVwap[idx].CaptureDelta == captureDelta && cacheRelativeVwap[idx].EodWinterOffsetHours == eodWinterOffsetHours && cacheRelativeVwap[idx].EnableAlerts == enableAlerts && cacheRelativeVwap[idx].AlertSound == alertSound && cacheRelativeVwap[idx].ShowDebugLabels == showDebugLabels && cacheRelativeVwap[idx].EnableFileLogging == enableFileLogging && cacheRelativeVwap[idx].EnableHealthCrossAlert == enableHealthCrossAlert && cacheRelativeVwap[idx].HealthCrossBlackoutMinutes == healthCrossBlackoutMinutes && cacheRelativeVwap[idx].HealthCrossConfirmBars == healthCrossConfirmBars && cacheRelativeVwap[idx].EnableHealthCrossTrading == enableHealthCrossTrading && cacheRelativeVwap[idx].ShowDebugLogs == showDebugLogs && cacheRelativeVwap[idx].EnableTDUTrading == enableTDUTrading && cacheRelativeVwap[idx].TDUUseSL == tDUUseSL && cacheRelativeVwap[idx].EnableTDULiveOrders == enableTDULiveOrders && cacheRelativeVwap[idx].ShowCountdown == showCountdown && cacheRelativeVwap[idx].CountDown == countDown && cacheRelativeVwap[idx].ShowPercent == showPercent && cacheRelativeVwap[idx].CountdownFontSize == countdownFontSize && cacheRelativeVwap[idx].CountdownOffsetX == countdownOffsetX && cacheRelativeVwap[idx].CountdownOffsetY == countdownOffsetY && cacheRelativeVwap[idx].WeekStartDay == weekStartDay && cacheRelativeVwap[idx].WeeklyHistoryWeeks == weeklyHistoryWeeks && cacheRelativeVwap[idx].MonthlyHistoryMonths == monthlyHistoryMonths && cacheRelativeVwap[idx].QuarterlyHistoryQuarters == quarterlyHistoryQuarters && cacheRelativeVwap[idx].YearlyHistoryYears == yearlyHistoryYears && cacheRelativeVwap[idx].ShowPeriodHigh == showPeriodHigh && cacheRelativeVwap[idx].ShowPeriodLow == showPeriodLow && cacheRelativeVwap[idx].ShowPeriodDividers == showPeriodDividers && cacheRelativeVwap[idx].ShowPeriodMarker == showPeriodMarker && cacheRelativeVwap[idx].DailyDividerTime == dailyDividerTime && cacheRelativeVwap[idx].EqualsInput(input))
						return cacheRelativeVwap[idx];
			return CacheIndicator<RelativeIndicators.RelativeVwap>(new RelativeIndicators.RelativeVwap(){ Personality = personality, VwapMethod = vwapMethod, MaxHistoryDays = maxHistoryDays, UseExchangeTime = useExchangeTime, ShowAsia = showAsia, AsiaStartTime = asiaStartTime, AsiaEndTime = asiaEndTime, ShowAsiaHigh = showAsiaHigh, ShowAsiaLow = showAsiaLow, ShowEurope = showEurope, EuropeStartTime = europeStartTime, EuropeEndTime = europeEndTime, ShowEuropeHigh = showEuropeHigh, ShowEuropeLow = showEuropeLow, ShowUS = showUS, USStartTime = uSStartTime, USEndTime = uSEndTime, ShowUSHigh = showUSHigh, ShowUSLow = showUSLow, ShowUSFirstHour = showUSFirstHour, USFirstHourMinutes = uSFirstHourMinutes, USFirstHourOpacity = uSFirstHourOpacity, HistoricalVWAPThickness = historicalVWAPThickness, ExtendLinesUntilTouch = extendLinesUntilTouch, SessionLevelThickness = sessionLevelThickness, HighVwapLabel = highVwapLabel, LowVwapLabel = lowVwapLabel, ShowDaysAgo = showDaysAgo, TradeDirection = tradeDirection, ShowLabels = showLabels, LabelDisplayMode = labelDisplayMode, ShowDemandSupplyCandles = showDemandSupplyCandles, CustomSignal1Text = customSignal1Text, CustomSignal2Text = customSignal2Text, CustomSignal3Text = customSignal3Text, ShowSignalLabels = showSignalLabels, ShowSignalText = showSignalText, ShowTradeVisualization = showTradeVisualization, ShowVwapHealth = showVwapHealth, HealthLabelOffsetBars = healthLabelOffsetBars, HealthLabelOffsetTicks = healthLabelOffsetTicks, ShowSignal1 = showSignal1, ShowSignal2 = showSignal2, ShowSignal3 = showSignal3, LabelFontSize = labelFontSize, LabelTextOffset = labelTextOffset, LabelDistanceATR = labelDistanceATR, LabelCollisionSpacing = labelCollisionSpacing, DetachmentTicks = detachmentTicks, Signal2ThresholdTicks = signal2ThresholdTicks, GlobalSignal2MaxAttempts = globalSignal2MaxAttempts, EnableInternalLogic = enableInternalLogic, InternalSignal2MaxAttempts = internalSignal2MaxAttempts, ExportSimulationCSV = exportSimulationCSV, AnalyzeAllSignals = analyzeAllSignals, ExportVwapApproaches = exportVwapApproaches, ApproachSeparationTicks = approachSeparationTicks, CaptureDelta = captureDelta, EodWinterOffsetHours = eodWinterOffsetHours, EnableAlerts = enableAlerts, AlertSound = alertSound, ShowDebugLabels = showDebugLabels, EnableFileLogging = enableFileLogging, EnableHealthCrossAlert = enableHealthCrossAlert, HealthCrossBlackoutMinutes = healthCrossBlackoutMinutes, HealthCrossConfirmBars = healthCrossConfirmBars, EnableHealthCrossTrading = enableHealthCrossTrading, ShowDebugLogs = showDebugLogs, EnableTDUTrading = enableTDUTrading, TDUUseSL = tDUUseSL, EnableTDULiveOrders = enableTDULiveOrders, ShowCountdown = showCountdown, CountDown = countDown, ShowPercent = showPercent, CountdownFontSize = countdownFontSize, CountdownOffsetX = countdownOffsetX, CountdownOffsetY = countdownOffsetY, WeekStartDay = weekStartDay, WeeklyHistoryWeeks = weeklyHistoryWeeks, MonthlyHistoryMonths = monthlyHistoryMonths, QuarterlyHistoryQuarters = quarterlyHistoryQuarters, YearlyHistoryYears = yearlyHistoryYears, ShowPeriodHigh = showPeriodHigh, ShowPeriodLow = showPeriodLow, ShowPeriodDividers = showPeriodDividers, ShowPeriodMarker = showPeriodMarker, DailyDividerTime = dailyDividerTime }, input, ref cacheRelativeVwap);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeVwap RelativeVwap(PersonalityMode personality, VwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, bool showAsia, string asiaStartTime, string asiaEndTime, bool showAsiaHigh, bool showAsiaLow, bool showEurope, string europeStartTime, string europeEndTime, bool showEuropeHigh, bool showEuropeLow, bool showUS, string uSStartTime, string uSEndTime, bool showUSHigh, bool showUSLow, bool showUSFirstHour, int uSFirstHourMinutes, int uSFirstHourOpacity, float historicalVWAPThickness, bool extendLinesUntilTouch, float sessionLevelThickness, string highVwapLabel, string lowVwapLabel, bool showDaysAgo, TradeDirectionMode tradeDirection, bool showLabels, LabelMode labelDisplayMode, bool showDemandSupplyCandles, string customSignal1Text, string customSignal2Text, string customSignal3Text, bool showSignalLabels, bool showSignalText, bool showTradeVisualization, bool showVwapHealth, int healthLabelOffsetBars, int healthLabelOffsetTicks, bool showSignal1, bool showSignal2, bool showSignal3, int labelFontSize, int labelTextOffset, double labelDistanceATR, double labelCollisionSpacing, int detachmentTicks, int signal2ThresholdTicks, int globalSignal2MaxAttempts, bool enableInternalLogic, int internalSignal2MaxAttempts, bool exportSimulationCSV, bool analyzeAllSignals, bool exportVwapApproaches, int approachSeparationTicks, bool captureDelta, int eodWinterOffsetHours, bool enableAlerts, string alertSound, bool showDebugLabels, bool enableFileLogging, bool enableHealthCrossAlert, int healthCrossBlackoutMinutes, int healthCrossConfirmBars, bool enableHealthCrossTrading, bool showDebugLogs, bool enableTDUTrading, bool tDUUseSL, bool enableTDULiveOrders, bool showCountdown, bool countDown, bool showPercent, int countdownFontSize, int countdownOffsetX, int countdownOffsetY, DayOfWeek weekStartDay, int weeklyHistoryWeeks, int monthlyHistoryMonths, int quarterlyHistoryQuarters, int yearlyHistoryYears, bool showPeriodHigh, bool showPeriodLow, bool showPeriodDividers, bool showPeriodMarker, string dailyDividerTime)
		{
			return indicator.RelativeVwap(Input, personality, vwapMethod, maxHistoryDays, useExchangeTime, showAsia, asiaStartTime, asiaEndTime, showAsiaHigh, showAsiaLow, showEurope, europeStartTime, europeEndTime, showEuropeHigh, showEuropeLow, showUS, uSStartTime, uSEndTime, showUSHigh, showUSLow, showUSFirstHour, uSFirstHourMinutes, uSFirstHourOpacity, historicalVWAPThickness, extendLinesUntilTouch, sessionLevelThickness, highVwapLabel, lowVwapLabel, showDaysAgo, tradeDirection, showLabels, labelDisplayMode, showDemandSupplyCandles, customSignal1Text, customSignal2Text, customSignal3Text, showSignalLabels, showSignalText, showTradeVisualization, showVwapHealth, healthLabelOffsetBars, healthLabelOffsetTicks, showSignal1, showSignal2, showSignal3, labelFontSize, labelTextOffset, labelDistanceATR, labelCollisionSpacing, detachmentTicks, signal2ThresholdTicks, globalSignal2MaxAttempts, enableInternalLogic, internalSignal2MaxAttempts, exportSimulationCSV, analyzeAllSignals, exportVwapApproaches, approachSeparationTicks, captureDelta, eodWinterOffsetHours, enableAlerts, alertSound, showDebugLabels, enableFileLogging, enableHealthCrossAlert, healthCrossBlackoutMinutes, healthCrossConfirmBars, enableHealthCrossTrading, showDebugLogs, enableTDUTrading, tDUUseSL, enableTDULiveOrders, showCountdown, countDown, showPercent, countdownFontSize, countdownOffsetX, countdownOffsetY, weekStartDay, weeklyHistoryWeeks, monthlyHistoryMonths, quarterlyHistoryQuarters, yearlyHistoryYears, showPeriodHigh, showPeriodLow, showPeriodDividers, showPeriodMarker, dailyDividerTime);
		}

		public Indicators.RelativeIndicators.RelativeVwap RelativeVwap(ISeries<double> input , PersonalityMode personality, VwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, bool showAsia, string asiaStartTime, string asiaEndTime, bool showAsiaHigh, bool showAsiaLow, bool showEurope, string europeStartTime, string europeEndTime, bool showEuropeHigh, bool showEuropeLow, bool showUS, string uSStartTime, string uSEndTime, bool showUSHigh, bool showUSLow, bool showUSFirstHour, int uSFirstHourMinutes, int uSFirstHourOpacity, float historicalVWAPThickness, bool extendLinesUntilTouch, float sessionLevelThickness, string highVwapLabel, string lowVwapLabel, bool showDaysAgo, TradeDirectionMode tradeDirection, bool showLabels, LabelMode labelDisplayMode, bool showDemandSupplyCandles, string customSignal1Text, string customSignal2Text, string customSignal3Text, bool showSignalLabels, bool showSignalText, bool showTradeVisualization, bool showVwapHealth, int healthLabelOffsetBars, int healthLabelOffsetTicks, bool showSignal1, bool showSignal2, bool showSignal3, int labelFontSize, int labelTextOffset, double labelDistanceATR, double labelCollisionSpacing, int detachmentTicks, int signal2ThresholdTicks, int globalSignal2MaxAttempts, bool enableInternalLogic, int internalSignal2MaxAttempts, bool exportSimulationCSV, bool analyzeAllSignals, bool exportVwapApproaches, int approachSeparationTicks, bool captureDelta, int eodWinterOffsetHours, bool enableAlerts, string alertSound, bool showDebugLabels, bool enableFileLogging, bool enableHealthCrossAlert, int healthCrossBlackoutMinutes, int healthCrossConfirmBars, bool enableHealthCrossTrading, bool showDebugLogs, bool enableTDUTrading, bool tDUUseSL, bool enableTDULiveOrders, bool showCountdown, bool countDown, bool showPercent, int countdownFontSize, int countdownOffsetX, int countdownOffsetY, DayOfWeek weekStartDay, int weeklyHistoryWeeks, int monthlyHistoryMonths, int quarterlyHistoryQuarters, int yearlyHistoryYears, bool showPeriodHigh, bool showPeriodLow, bool showPeriodDividers, bool showPeriodMarker, string dailyDividerTime)
		{
			return indicator.RelativeVwap(input, personality, vwapMethod, maxHistoryDays, useExchangeTime, showAsia, asiaStartTime, asiaEndTime, showAsiaHigh, showAsiaLow, showEurope, europeStartTime, europeEndTime, showEuropeHigh, showEuropeLow, showUS, uSStartTime, uSEndTime, showUSHigh, showUSLow, showUSFirstHour, uSFirstHourMinutes, uSFirstHourOpacity, historicalVWAPThickness, extendLinesUntilTouch, sessionLevelThickness, highVwapLabel, lowVwapLabel, showDaysAgo, tradeDirection, showLabels, labelDisplayMode, showDemandSupplyCandles, customSignal1Text, customSignal2Text, customSignal3Text, showSignalLabels, showSignalText, showTradeVisualization, showVwapHealth, healthLabelOffsetBars, healthLabelOffsetTicks, showSignal1, showSignal2, showSignal3, labelFontSize, labelTextOffset, labelDistanceATR, labelCollisionSpacing, detachmentTicks, signal2ThresholdTicks, globalSignal2MaxAttempts, enableInternalLogic, internalSignal2MaxAttempts, exportSimulationCSV, analyzeAllSignals, exportVwapApproaches, approachSeparationTicks, captureDelta, eodWinterOffsetHours, enableAlerts, alertSound, showDebugLabels, enableFileLogging, enableHealthCrossAlert, healthCrossBlackoutMinutes, healthCrossConfirmBars, enableHealthCrossTrading, showDebugLogs, enableTDUTrading, tDUUseSL, enableTDULiveOrders, showCountdown, countDown, showPercent, countdownFontSize, countdownOffsetX, countdownOffsetY, weekStartDay, weeklyHistoryWeeks, monthlyHistoryMonths, quarterlyHistoryQuarters, yearlyHistoryYears, showPeriodHigh, showPeriodLow, showPeriodDividers, showPeriodMarker, dailyDividerTime);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeVwap RelativeVwap(PersonalityMode personality, VwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, bool showAsia, string asiaStartTime, string asiaEndTime, bool showAsiaHigh, bool showAsiaLow, bool showEurope, string europeStartTime, string europeEndTime, bool showEuropeHigh, bool showEuropeLow, bool showUS, string uSStartTime, string uSEndTime, bool showUSHigh, bool showUSLow, bool showUSFirstHour, int uSFirstHourMinutes, int uSFirstHourOpacity, float historicalVWAPThickness, bool extendLinesUntilTouch, float sessionLevelThickness, string highVwapLabel, string lowVwapLabel, bool showDaysAgo, TradeDirectionMode tradeDirection, bool showLabels, LabelMode labelDisplayMode, bool showDemandSupplyCandles, string customSignal1Text, string customSignal2Text, string customSignal3Text, bool showSignalLabels, bool showSignalText, bool showTradeVisualization, bool showVwapHealth, int healthLabelOffsetBars, int healthLabelOffsetTicks, bool showSignal1, bool showSignal2, bool showSignal3, int labelFontSize, int labelTextOffset, double labelDistanceATR, double labelCollisionSpacing, int detachmentTicks, int signal2ThresholdTicks, int globalSignal2MaxAttempts, bool enableInternalLogic, int internalSignal2MaxAttempts, bool exportSimulationCSV, bool analyzeAllSignals, bool exportVwapApproaches, int approachSeparationTicks, bool captureDelta, int eodWinterOffsetHours, bool enableAlerts, string alertSound, bool showDebugLabels, bool enableFileLogging, bool enableHealthCrossAlert, int healthCrossBlackoutMinutes, int healthCrossConfirmBars, bool enableHealthCrossTrading, bool showDebugLogs, bool enableTDUTrading, bool tDUUseSL, bool enableTDULiveOrders, bool showCountdown, bool countDown, bool showPercent, int countdownFontSize, int countdownOffsetX, int countdownOffsetY, DayOfWeek weekStartDay, int weeklyHistoryWeeks, int monthlyHistoryMonths, int quarterlyHistoryQuarters, int yearlyHistoryYears, bool showPeriodHigh, bool showPeriodLow, bool showPeriodDividers, bool showPeriodMarker, string dailyDividerTime)
		{
			return indicator.RelativeVwap(Input, personality, vwapMethod, maxHistoryDays, useExchangeTime, showAsia, asiaStartTime, asiaEndTime, showAsiaHigh, showAsiaLow, showEurope, europeStartTime, europeEndTime, showEuropeHigh, showEuropeLow, showUS, uSStartTime, uSEndTime, showUSHigh, showUSLow, showUSFirstHour, uSFirstHourMinutes, uSFirstHourOpacity, historicalVWAPThickness, extendLinesUntilTouch, sessionLevelThickness, highVwapLabel, lowVwapLabel, showDaysAgo, tradeDirection, showLabels, labelDisplayMode, showDemandSupplyCandles, customSignal1Text, customSignal2Text, customSignal3Text, showSignalLabels, showSignalText, showTradeVisualization, showVwapHealth, healthLabelOffsetBars, healthLabelOffsetTicks, showSignal1, showSignal2, showSignal3, labelFontSize, labelTextOffset, labelDistanceATR, labelCollisionSpacing, detachmentTicks, signal2ThresholdTicks, globalSignal2MaxAttempts, enableInternalLogic, internalSignal2MaxAttempts, exportSimulationCSV, analyzeAllSignals, exportVwapApproaches, approachSeparationTicks, captureDelta, eodWinterOffsetHours, enableAlerts, alertSound, showDebugLabels, enableFileLogging, enableHealthCrossAlert, healthCrossBlackoutMinutes, healthCrossConfirmBars, enableHealthCrossTrading, showDebugLogs, enableTDUTrading, tDUUseSL, enableTDULiveOrders, showCountdown, countDown, showPercent, countdownFontSize, countdownOffsetX, countdownOffsetY, weekStartDay, weeklyHistoryWeeks, monthlyHistoryMonths, quarterlyHistoryQuarters, yearlyHistoryYears, showPeriodHigh, showPeriodLow, showPeriodDividers, showPeriodMarker, dailyDividerTime);
		}

		public Indicators.RelativeIndicators.RelativeVwap RelativeVwap(ISeries<double> input , PersonalityMode personality, VwapPriceMethod vwapMethod, int maxHistoryDays, bool useExchangeTime, bool showAsia, string asiaStartTime, string asiaEndTime, bool showAsiaHigh, bool showAsiaLow, bool showEurope, string europeStartTime, string europeEndTime, bool showEuropeHigh, bool showEuropeLow, bool showUS, string uSStartTime, string uSEndTime, bool showUSHigh, bool showUSLow, bool showUSFirstHour, int uSFirstHourMinutes, int uSFirstHourOpacity, float historicalVWAPThickness, bool extendLinesUntilTouch, float sessionLevelThickness, string highVwapLabel, string lowVwapLabel, bool showDaysAgo, TradeDirectionMode tradeDirection, bool showLabels, LabelMode labelDisplayMode, bool showDemandSupplyCandles, string customSignal1Text, string customSignal2Text, string customSignal3Text, bool showSignalLabels, bool showSignalText, bool showTradeVisualization, bool showVwapHealth, int healthLabelOffsetBars, int healthLabelOffsetTicks, bool showSignal1, bool showSignal2, bool showSignal3, int labelFontSize, int labelTextOffset, double labelDistanceATR, double labelCollisionSpacing, int detachmentTicks, int signal2ThresholdTicks, int globalSignal2MaxAttempts, bool enableInternalLogic, int internalSignal2MaxAttempts, bool exportSimulationCSV, bool analyzeAllSignals, bool exportVwapApproaches, int approachSeparationTicks, bool captureDelta, int eodWinterOffsetHours, bool enableAlerts, string alertSound, bool showDebugLabels, bool enableFileLogging, bool enableHealthCrossAlert, int healthCrossBlackoutMinutes, int healthCrossConfirmBars, bool enableHealthCrossTrading, bool showDebugLogs, bool enableTDUTrading, bool tDUUseSL, bool enableTDULiveOrders, bool showCountdown, bool countDown, bool showPercent, int countdownFontSize, int countdownOffsetX, int countdownOffsetY, DayOfWeek weekStartDay, int weeklyHistoryWeeks, int monthlyHistoryMonths, int quarterlyHistoryQuarters, int yearlyHistoryYears, bool showPeriodHigh, bool showPeriodLow, bool showPeriodDividers, bool showPeriodMarker, string dailyDividerTime)
		{
			return indicator.RelativeVwap(input, personality, vwapMethod, maxHistoryDays, useExchangeTime, showAsia, asiaStartTime, asiaEndTime, showAsiaHigh, showAsiaLow, showEurope, europeStartTime, europeEndTime, showEuropeHigh, showEuropeLow, showUS, uSStartTime, uSEndTime, showUSHigh, showUSLow, showUSFirstHour, uSFirstHourMinutes, uSFirstHourOpacity, historicalVWAPThickness, extendLinesUntilTouch, sessionLevelThickness, highVwapLabel, lowVwapLabel, showDaysAgo, tradeDirection, showLabels, labelDisplayMode, showDemandSupplyCandles, customSignal1Text, customSignal2Text, customSignal3Text, showSignalLabels, showSignalText, showTradeVisualization, showVwapHealth, healthLabelOffsetBars, healthLabelOffsetTicks, showSignal1, showSignal2, showSignal3, labelFontSize, labelTextOffset, labelDistanceATR, labelCollisionSpacing, detachmentTicks, signal2ThresholdTicks, globalSignal2MaxAttempts, enableInternalLogic, internalSignal2MaxAttempts, exportSimulationCSV, analyzeAllSignals, exportVwapApproaches, approachSeparationTicks, captureDelta, eodWinterOffsetHours, enableAlerts, alertSound, showDebugLabels, enableFileLogging, enableHealthCrossAlert, healthCrossBlackoutMinutes, healthCrossConfirmBars, enableHealthCrossTrading, showDebugLogs, enableTDUTrading, tDUUseSL, enableTDULiveOrders, showCountdown, countDown, showPercent, countdownFontSize, countdownOffsetX, countdownOffsetY, weekStartDay, weeklyHistoryWeeks, monthlyHistoryMonths, quarterlyHistoryQuarters, yearlyHistoryYears, showPeriodHigh, showPeriodLow, showPeriodDividers, showPeriodMarker, dailyDividerTime);
		}
	}
}

#endregion
