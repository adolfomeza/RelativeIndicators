#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
    public partial class RelativeVwap
    {
        #region Logging

        private string logFilePath = "";
        private object logLock = new object();

        private void LogToFile(string message, string category = "INFO")
        {
            if (!EnableFileLogging) return;
            // v3.1.1 perf: Skip file I/O during historical processing (massive perf hit)
            if (State == State.Historical) return;

            try
            {
                lock (logLock)
                {
                    // Initialize log path if needed
                    if (string.IsNullOrEmpty(logFilePath))
                    {
                        string dateStamp = DateTime.Now.ToString("yyyyMMdd");
                        // Store logs in the indicator's folder for easy access
                        string indicatorFolder = Path.GetDirectoryName(typeof(RelativeVwap).Assembly.Location);
                        string logFolder = Path.Combine(indicatorFolder, "RelativeIndicators");

                        if (!Directory.Exists(logFolder))
                            Directory.CreateDirectory(logFolder);

                        logFilePath = Path.Combine(logFolder, $"RelativeVwap_Debug_{dateStamp}.txt");
                    }

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

        #endregion

        // PHASE 5: Track previous countdown text to avoid unnecessary invalidations
        private string _previousCountdownText = "";

        #region Signal Management

        private void AddSignal(int barIdx, double price, string text, bool isHigh, Brush brush, string signalType)
        {
            if (signalLabels == null) return;
            if (!ShowSignalText) return;

            string key = barIdx + "_" + signalType + "_" + (isHigh ? "H" : "L");

            signalLabels[key] = new SignalObj
            {
                BarIdx = barIdx,
                Price = price,
                Text = text,
                IsHigh = isHigh,
                Brush = brush
            };
        }

        #endregion

        #region Countdown Timer

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

                             TimeSpan t = TimeSpan.FromSeconds(val);
                             if (t.TotalHours >= 1) _currentCountdownText = string.Format("{0:D2}:{1:D2}:{2:D2}", (int)t.TotalHours, t.Minutes, t.Seconds);
                             else _currentCountdownText = string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds);
                        }
                        else _currentCountdownText = "";
                    }
                    else
                    {
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

                // PHASE 5: Only invalidate if countdown text actually changed
                if (ShowLabels && ChartControl != null && _previousCountdownText != _currentCountdownText)
                {
                    _previousCountdownText = _currentCountdownText;
                    if (ChartControl.Dispatcher.CheckAccess())
                        ChartControl.InvalidateVisual();
                    else
                        ChartControl.Dispatcher.InvokeAsync(() => ChartControl.InvalidateVisual());
                }
            }
            catch (Exception ex)
            {
                Print("CalculateCountdown error: " + ex.Message);
            }
        }

        #endregion

        #region VWAP Value Archiving

        /// <summary>
        /// Copies VWAP values from a Values series to a Dictionary for historical storage.
        /// This prevents diagonal line artifacts when drawing historical VWAP segments.
        /// </summary>
        private Dictionary<int, double> CopyVwapValues(int startIdx, int endIdx, int seriesIdx)
        {
            var dict = new Dictionary<int, double>();

            if (Values == null || seriesIdx < 0 || seriesIdx >= Values.Length)
                return dict;

            int safeStart = Math.Max(0, startIdx);
            int safeEnd = Math.Min(Values[seriesIdx].Count - 1, endIdx);

            for (int i = safeStart; i <= safeEnd; i++)
            {
                double val = Values[seriesIdx].GetValueAt(i);
                if (!double.IsNaN(val))
                {
                    dict[i] = val;
                }
            }

            return dict;
        }

        #endregion

        #region Signal Visualization (SL/TP Lines)

        // Pending signals storage for deferred drawing
        private class PendingSignal
        {
            public bool IsLong;
            public int AnchorBarIdx;
            public int SignalBarIdx;
            public double VwapPrice;
            public double TP1;
            public double TP2;
            public int Quantity;
            public double SL;
            // CSV export fields
            public string SetupName;    // "Asia High", "Europe Low", etc.
            public int AnchorSequence;  // Retry number
            public DateTime AnchorTime; // Bar time at anchor (for LevelAge)
            public DateTime SignalTime; // Bar time at Signal 2 (EntryTime)
            public double DeltaGlobal;  // Full day (Asia start to USA end)
            public double ATR_Value;    // ATR at signal
            public double VolumeRatio;  // Volume ratio at signal
            public bool IsTrendTrade;   // true = trend mode, false = reversal mode
        }

        // v3.0.4: Cleaned CSV — removed 11 redundant/placeholder columns
        private class SimTradeRecord
        {
            public string ID;
            public string Instrument;
            public DateTime EntryTime;
            public string Type;
            public double EntryPrice;
            public DateTime ExitTime;
            public double ExitPrice;
            public string Result;
            public double PnL;
            public double MAE;
            public double MFE;
            public string Setup;
            public int Attempt;
            public double DeltaGlobal;
            public int LevelAge;
            public string TradeClustID;
            public double ATR_Value;
            public double VolumeRatio;
            public bool Overlapping;
            public int SignalBarIdx;     // For drawing overlap labels
            public bool IsTrendTrade;    // true = trend mode, false = reversal mode
        }

        private List<PendingSignal> _pendingSignals = new List<PendingSignal>();
        private List<SimTradeRecord> _simExportRecords = new List<SimTradeRecord>();
        private int _dailyTradeCounter = 0;
        private DateTime _lastExportDate = DateTime.MinValue;
        private bool _signalsProcessed = false;

        // v3.0.4: VWAP Approach Tracking
        private class VwapApproachRecord
        {
            public DateTime Date;
            public DateTime Time;
            public string Instrument;
            public string VwapSide;       // "Supply" or "Demand"
            public double VwapPrice;      // VWAP price at touch
            public double TouchPrice;     // Actual price at touch (Close)
            public int VwapAge;           // Bars since VWAP was anchored
            public int TouchNumber;       // Nth touch to this VWAP instance
            public double VwapSlope;      // VWAP rate of change (ticks over last 10 bars)
            public double VwapSpread;     // Distance between High VWAP and Low VWAP (ticks)
            public double DeltaGlobal;
            public double ATR;
            public double MFE_Rejection;  // Max favorable away from VWAP (ticks) until EOD
            public double MAE_Penetration; // Max adverse through VWAP (ticks) until EOD
            public double EOD_Price;
            public string EOD_Result;     // "Held" or "Broken"
            public int TouchBarIdx;       // Bar index where touch occurred (for path export)
            public int EODBarIdx;         // Bar index of EOD (for path export)
        }
        private List<VwapApproachRecord> _vwapApproaches = new List<VwapApproachRecord>();
        private int _highVwapTouchCount = 0;  // Resets on new anchor
        private int _lowVwapTouchCount = 0;
        // (health touch count removed — v3.2.2 reverted to accumulated MFE/MAE)
        private int _lastHighVwapTouchBar = -1; // Prevent multiple touches per bar
        private int _lastLowVwapTouchBar = -1;
        private int _highVwapAnchorBarForTouch = -1; // Track anchor changes to reset touch count
        private int _lowVwapAnchorBarForTouch = -1;
        // Separation filter state: price must close away from VWAP before next touch counts
        private bool _highVwapHasSeparated = true;  // true = first touch allowed
        private bool _lowVwapHasSeparated = true;

        // v3.0.4: VWAP Health Score — touch-episode based MFE/MAE
        private double _highVwapRunningMFE = 0;
        private double _highVwapRunningMAE = 0;
        private double _lowVwapRunningMFE = 0;
        private double _lowVwapRunningMAE = 0;
        private double _highVwapCurrentTouchMFE = 0;
        private double _highVwapCurrentTouchMAE = 0;
        private double _lowVwapCurrentTouchMFE = 0;
        private double _lowVwapCurrentTouchMAE = 0;
        private bool _highVwapInTouch = false;
        private bool _lowVwapInTouch = false;

        // v3.0.5: Touch study — first touch after significant separation
        private bool _highVwapSeparated = false;
        private bool _lowVwapSeparated = false;
        private double _highSeparationTicks = 0;  // distance in ticks at separation moment
        private double _lowSeparationTicks = 0;
        // v3.2.0: Phase tracking — impulse (sep→peak) and retrace (peak→touch)
        private int _highSepBarIdx = -1;           // bar where high separation was detected
        private double _highSepCumDelta = 0;       // running delta since separation
        private double _highSepCumVolume = 0;      // running volume since separation
        private double _highSepMaxDist = 0;        // max distance from VWAP (to detect peak)
        private int _highSepPeakBarIdx = -1;       // bar at peak distance
        private double _highSepPeakDelta = 0;      // delta at peak (snapshot for impulse)
        private double _highSepPeakVolume = 0;     // volume at peak (snapshot for impulse)
        private int _lowSepBarIdx = -1;
        private double _lowSepCumDelta = 0;
        private double _lowSepCumVolume = 0;
        private double _lowSepMaxDist = 0;
        private int _lowSepPeakBarIdx = -1;
        private double _lowSepPeakDelta = 0;
        private double _lowSepPeakVolume = 0;
        private int _lastConfigBBar = -999;  // v3.0.7: last Config B episode bar
        private int _lastConfigCBar = -999;
        private int _lastConfigABar = -999;
        private int _lastConfigDBar = -999;

        // v3.1.2 perf: Cached EOD parameters (avoid per-tick parsing/lookup)
        private TimeZoneInfo _cachedEasternZone;
        private TimeSpan _cachedUsaEndTimeSpan = TimeSpan.MinValue;
        private string _cachedUsaEndTimeStr = null;

        /// <summary>
        /// v3.0.4: Detect touches to either VWAP and record approach data.
        /// Called from OnBarUpdate after VWAP values are calculated.
        /// </summary>
        private void TrackVwapApproaches()
        {
            if (!ExportVwapApproaches || !hasHighVWAP || !hasLowVWAP) return;
            if (CurrentBar < 10) return; // Need minimum bars for slope calc

            double tickSize = TickSize;
            double high = High[0];
            double low = Low[0];
            double close = Close[0];
            double hVwap = currentHighVWAP;
            double lVwap = currentLowVWAP;

            // Reset touch counts and separation state when anchor changes
            if (sessionHighBarIdx != _highVwapAnchorBarForTouch)
            {
                _highVwapTouchCount = 0;
                _highVwapAnchorBarForTouch = sessionHighBarIdx;
                _highVwapHasSeparated = true; // New VWAP → first touch allowed
            }
            if (sessionLowBarIdx != _lowVwapAnchorBarForTouch)
            {
                _lowVwapTouchCount = 0;
                _lowVwapAnchorBarForTouch = sessionLowBarIdx;
                _lowVwapHasSeparated = true; // New VWAP → first touch allowed
            }

            // Update separation state BEFORE checking touches
            if (ApproachSeparationTicks > 0)
            {
                double sepDist = ApproachSeparationTicks * tickSize;
                // High VWAP (Supply/resistance): price must close BELOW vwap - separation to "separate"
                if (!_highVwapHasSeparated && close < hVwap - sepDist)
                    _highVwapHasSeparated = true;
                // Low VWAP (Demand/support): price must close ABOVE vwap + separation to "separate"
                if (!_lowVwapHasSeparated && close > lVwap + sepDist)
                    _lowVwapHasSeparated = true;
            }

            // High VWAP (Supply) touch: price reaches up to it
            if (high >= hVwap && _lastHighVwapTouchBar != CurrentBar)
            {
                if (ApproachSeparationTicks <= 0 || _highVwapHasSeparated)
                {
                    _lastHighVwapTouchBar = CurrentBar;
                    _highVwapTouchCount++;
                    RecordVwapApproach("Supply", hVwap, close, sessionHighBarIdx, _highVwapTouchCount, hVwap, lVwap);
                    if (ApproachSeparationTicks > 0)
                        _highVwapHasSeparated = false; // Must separate again before next touch
                }
            }

            // Low VWAP (Demand) touch: price reaches down to it
            if (low <= lVwap && _lastLowVwapTouchBar != CurrentBar)
            {
                if (ApproachSeparationTicks <= 0 || _lowVwapHasSeparated)
                {
                    _lastLowVwapTouchBar = CurrentBar;
                    _lowVwapTouchCount++;
                    RecordVwapApproach("Demand", lVwap, close, sessionLowBarIdx, _lowVwapTouchCount, hVwap, lVwap);
                    if (ApproachSeparationTicks > 0)
                        _lowVwapHasSeparated = false; // Must separate again before next touch
                }
            }
        }

        /// <summary>
        /// v3.0.4: Update VWAP health tracking each bar.
        /// Tracks running MFE (rejection away from VWAP) and MAE (penetration through VWAP).
        /// Called from OnBarUpdate after VWAP values are calculated.
        /// </summary>
        private void UpdateVwapHealthTracking()
        {
            if (!hasHighVWAP && !hasLowVWAP) return;

            double tickSize = TickSize;
            if (tickSize <= 0) return;
            double high = High[0];
            double low = Low[0];
            double close = Close[0];

            // === HIGH VWAP (Supply/Resistance) ===
            if (hasHighVWAP && sessionHighBarIdx >= 0)
            {
                double hVwap = currentHighVWAP;
                // MFE = price moves BELOW VWAP (rejection = good for Supply)
                double distBelow = (hVwap - low) / tickSize;
                if (distBelow > 0 && distBelow > _highVwapCurrentTouchMFE)
                    _highVwapCurrentTouchMFE = distBelow;
                // MAE = price moves ABOVE VWAP (penetration = bad for Supply)
                double distAbove = (high - hVwap) / tickSize;
                if (distAbove > 0 && distAbove > _highVwapCurrentTouchMAE)
                    _highVwapCurrentTouchMAE = distAbove;

                // Detect touch start/end: price touches VWAP when high >= hVwap
                bool touching = high >= hVwap;
                if (touching && !_highVwapInTouch)
                {
                    // New touch episode starts
                    _highVwapInTouch = true;
                    _highVwapCurrentTouchMFE = 0;
                    _highVwapCurrentTouchMAE = 0;
                }
                else if (!touching && _highVwapInTouch)
                {
                    // Touch episode ends — accumulate to running totals
                    _highVwapRunningMFE += _highVwapCurrentTouchMFE;
                    _highVwapRunningMAE += _highVwapCurrentTouchMAE;
                    _highVwapInTouch = false;
                }
            }

            // === LOW VWAP (Demand/Support) ===
            if (hasLowVWAP && sessionLowBarIdx >= 0)
            {
                double lVwap = currentLowVWAP;
                // MFE = price moves ABOVE VWAP (rejection = good for Demand)
                double distAbove = (high - lVwap) / tickSize;
                if (distAbove > 0 && distAbove > _lowVwapCurrentTouchMFE)
                    _lowVwapCurrentTouchMFE = distAbove;
                // MAE = price moves BELOW VWAP (penetration = bad for Demand)
                double distBelow = (lVwap - low) / tickSize;
                if (distBelow > 0 && distBelow > _lowVwapCurrentTouchMAE)
                    _lowVwapCurrentTouchMAE = distBelow;

                bool touching = low <= lVwap;
                if (touching && !_lowVwapInTouch)
                {
                    _lowVwapInTouch = true;
                    _lowVwapCurrentTouchMFE = 0;
                    _lowVwapCurrentTouchMAE = 0;
                }
                else if (!touching && _lowVwapInTouch)
                {
                    _lowVwapRunningMFE += _lowVwapCurrentTouchMFE;
                    _lowVwapRunningMAE += _lowVwapCurrentTouchMAE;
                    _lowVwapInTouch = false;
                }
            }
        }

        /// <summary>
        /// v3.0.4: Calculate VWAP health score using accumulated MFE/MAE ratio.
        /// Higher score = VWAP is being respected (good MFE relative to MAE).
        /// </summary>
        private double GetVwapHealthScore(bool isHighVwap)
        {
            double mfe, mae;
            if (isHighVwap)
            {
                mfe = _highVwapRunningMFE + _highVwapCurrentTouchMFE;
                mae = _highVwapRunningMAE + _highVwapCurrentTouchMAE;
            }
            else
            {
                mfe = _lowVwapRunningMFE + _lowVwapCurrentTouchMFE;
                mae = _lowVwapRunningMAE + _lowVwapCurrentTouchMAE;
            }
            return mfe / (mae + 1.0);
        }

        // v3.1.2: Public read-only properties for companion indicator (RelativeVwapHealth)
        [Browsable(false)]
        public double HighHealthScore { get { return GetVwapHealthScore(true); } }
        [Browsable(false)]
        public double LowHealthScore { get { return GetVwapHealthScore(false); } }

        /// <summary>
        /// v3.0.4: Reset health tracking when VWAP anchor changes.
        /// </summary>
        private void ResetVwapHealth(bool isHighVwap)
        {
            if (isHighVwap)
            {
                _highVwapRunningMFE = 0;
                _highVwapRunningMAE = 0;
                _highVwapCurrentTouchMFE = 0;
                _highVwapCurrentTouchMAE = 0;
                _highVwapInTouch = false;
                _highVwapTouchCount = 0;
            }
            else
            {
                _lowVwapRunningMFE = 0;
                _lowVwapRunningMAE = 0;
                _lowVwapCurrentTouchMFE = 0;
                _lowVwapCurrentTouchMAE = 0;
                _lowVwapInTouch = false;
                _lowVwapTouchCount = 0;
            }
        }

        // v3.0.5: Touch Study — detect first touch after significant separation
        private void UpdateTouchStudyTracking()
        {
            if (!hasHighVWAP && !hasLowVWAP) return;
            if (CurrentBar < 15) return;
            double tickSize = TickSize > 0 ? TickSize : 0.25;
            double atrVal = (atr != null && CurrentBar >= 14 && atr[0] > 0) ? atr[0] : 20 * tickSize;
            double separationThreshold = atrVal * TouchStudySeparationATR;
            double proximityThreshold = TouchStudyProximityTicks * tickSize;

            // --- High VWAP (Supply) ---
            if (hasHighVWAP && currentHighVWAP > 0)
            {
                double distFromHigh = Math.Abs(Close[0] - currentHighVWAP);
                if (!_highVwapSeparated)
                {
                    if (distFromHigh > separationThreshold)
                    {
                        _highVwapSeparated = true;
                        _highSeparationTicks = distFromHigh / tickSize;
                        // v3.2.0: Start phase tracking — separation bar IS the first impulse data
                        double sepBarDelta = (Close[0] - Open[0]) * Volume[0];
                        _highSepBarIdx = CurrentBar;
                        _highSepCumDelta = sepBarDelta;
                        _highSepCumVolume = Volume[0];
                        _highSepMaxDist = distFromHigh;
                        _highSepPeakBarIdx = CurrentBar;
                        _highSepPeakDelta = sepBarDelta;
                        _highSepPeakVolume = Volume[0];
                    }
                }
                else
                {
                    // v3.2.0: Accumulate delta/volume during separation journey
                    double barDelta = (Close[0] - Open[0]) * Volume[0];
                    _highSepCumDelta += barDelta;
                    _highSepCumVolume += Volume[0];
                    // Track peak distance (impulse→retrace transition)
                    if (distFromHigh > _highSepMaxDist)
                    {
                        _highSepMaxDist = distFromHigh;
                        _highSepPeakBarIdx = CurrentBar;
                        _highSepPeakDelta = _highSepCumDelta;
                        _highSepPeakVolume = _highSepCumVolume;
                    }

                    double touchDist = Math.Abs(High[0] - currentHighVWAP);
                    if (touchDist <= proximityThreshold || High[0] >= currentHighVWAP)
                    {
                        double hScore = GetVwapHealthScore(true);
                        double lScore = GetVwapHealthScore(false);
                        string cfg = ClassifyTouchConfig(true, hScore, lScore);
                        bool isFirst = IsEpisodeFirstTouch(cfg, CurrentBar);

                        _activeFirstTouches.Add(new FirstTouchRecord
                        {
                            BarIdx = CurrentBar,
                            TouchedHighVwap = true,
                            HighHealthScore = hScore,
                            LowHealthScore = lScore,
                            VwapPrice = currentHighVWAP,
                            TouchPrice = Close[0],
                            ATRValue = atrVal,
                            Separation = _highSeparationTicks,
                            Config = cfg,
                            IsEpisodeFirst = isFirst,
                            ExitType = 0,
                            OtherVwapPrice = (hasLowVWAP && currentLowVWAP > 0) ? currentLowVWAP : 0,
                            // v3.2.0: Phase data
                            ImpulseDelta = _highSepPeakDelta,
                            ImpulseVolume = _highSepPeakVolume,
                            ImpulseBars = _highSepPeakBarIdx - _highSepBarIdx,
                            RetraceDelta = _highSepCumDelta - _highSepPeakDelta,
                            RetraceVolume = _highSepCumVolume - _highSepPeakVolume,
                            RetraceBars = CurrentBar - _highSepPeakBarIdx
                        });
                        if (isFirst) UpdateLastConfigBar(cfg, CurrentBar);

                        // v3.1.0: Auto-trade trigger (High VWAP touch → C is SHORT, A is LONG)
                        if (isFirst && State == State.Realtime && IsAutoTradeEnabled(cfg))
                        {
                            bool isShort = (cfg == "B" || cfg == "C");
                            SubmitAutoTrade(cfg, Close[0], isShort);
                        }

                        _highVwapSeparated = false;
                        _highSeparationTicks = 0;
                    }
                }
            }

            // --- Low VWAP (Demand) ---
            if (hasLowVWAP && currentLowVWAP > 0)
            {
                double distFromLow = Math.Abs(Close[0] - currentLowVWAP);
                if (!_lowVwapSeparated)
                {
                    if (distFromLow > separationThreshold)
                    {
                        _lowVwapSeparated = true;
                        _lowSeparationTicks = distFromLow / tickSize;
                        // v3.2.0: Start phase tracking — separation bar IS the first impulse data
                        double sepBarDeltaL = (Close[0] - Open[0]) * Volume[0];
                        _lowSepBarIdx = CurrentBar;
                        _lowSepCumDelta = sepBarDeltaL;
                        _lowSepCumVolume = Volume[0];
                        _lowSepMaxDist = distFromLow;
                        _lowSepPeakBarIdx = CurrentBar;
                        _lowSepPeakDelta = sepBarDeltaL;
                        _lowSepPeakVolume = Volume[0];
                    }
                }
                else
                {
                    // v3.2.0: Accumulate delta/volume during separation journey
                    double barDelta = (Close[0] - Open[0]) * Volume[0];
                    _lowSepCumDelta += barDelta;
                    _lowSepCumVolume += Volume[0];
                    // Track peak distance (impulse→retrace transition)
                    if (distFromLow > _lowSepMaxDist)
                    {
                        _lowSepMaxDist = distFromLow;
                        _lowSepPeakBarIdx = CurrentBar;
                        _lowSepPeakDelta = _lowSepCumDelta;
                        _lowSepPeakVolume = _lowSepCumVolume;
                    }

                    double touchDist = Math.Abs(Low[0] - currentLowVWAP);
                    if (touchDist <= proximityThreshold || Low[0] <= currentLowVWAP)
                    {
                        double hScore = GetVwapHealthScore(true);
                        double lScore = GetVwapHealthScore(false);
                        string cfg = ClassifyTouchConfig(false, hScore, lScore);
                        bool isFirst = IsEpisodeFirstTouch(cfg, CurrentBar);

                        _activeFirstTouches.Add(new FirstTouchRecord
                        {
                            BarIdx = CurrentBar,
                            TouchedHighVwap = false,
                            HighHealthScore = hScore,
                            LowHealthScore = lScore,
                            VwapPrice = currentLowVWAP,
                            TouchPrice = Close[0],
                            ATRValue = atrVal,
                            Separation = _lowSeparationTicks,
                            Config = cfg,
                            IsEpisodeFirst = isFirst,
                            ExitType = 0,
                            OtherVwapPrice = (hasHighVWAP && currentHighVWAP > 0) ? currentHighVWAP : 0,
                            // v3.2.0: Phase data
                            ImpulseDelta = _lowSepPeakDelta,
                            ImpulseVolume = _lowSepPeakVolume,
                            ImpulseBars = _lowSepPeakBarIdx - _lowSepBarIdx,
                            RetraceDelta = _lowSepCumDelta - _lowSepPeakDelta,
                            RetraceVolume = _lowSepCumVolume - _lowSepPeakVolume,
                            RetraceBars = CurrentBar - _lowSepPeakBarIdx
                        });
                        if (isFirst) UpdateLastConfigBar(cfg, CurrentBar);

                        // v3.1.0: Auto-trade trigger (Low VWAP touch → B is SHORT, D is LONG)
                        if (isFirst && State == State.Realtime && IsAutoTradeEnabled(cfg))
                        {
                            bool isShort = (cfg == "B" || cfg == "C");
                            SubmitAutoTrade(cfg, Close[0], isShort);
                        }

                        _lowVwapSeparated = false;
                        _lowSeparationTicks = 0;
                    }
                }
            }

            // --- MFE/MAE + SL/TP exit tracking ---
            // v3.2.0 perf: Only run on first tick of bar — MFE/MAE uses High[0]/Low[0] which are bar-level.
            // Running per-tick was the #1 cause of slowness during active trades in playback.
            if (!IsFirstTickOfBar) return;

            double slTicks = TouchStudySLTicks;
            double tpTicks = TouchStudyTPTicks;

            for (int idx = 0; idx < _activeFirstTouches.Count; idx++)
            {
                var t = _activeFirstTouches[idx];
                if (t.RawComplete) continue; // v3.1.3: fully tracked to EOD — done
                int elapsed = CurrentBar - t.BarIdx;
                if (elapsed <= 0) continue;

                double favorable = 0;
                double adverse = 0;

                // v3.0.8: Direction based on Config trade direction, not just TouchedHighVwap
                // Config B/C = SHORT trade, Config A/D = LONG trade
                // Default (no config): TouchedHighVwap=true → short, false → long
                bool tradeIsShort;
                if (t.Config == "B" || t.Config == "C")
                    tradeIsShort = true;
                else if (t.Config == "A" || t.Config == "D")
                    tradeIsShort = false;
                else
                    tradeIsShort = t.TouchedHighVwap; // fallback: supply touch = short

                if (tradeIsShort)
                {
                    favorable = (t.TouchPrice - Low[0]) / tickSize;
                    adverse = (High[0] - t.TouchPrice) / tickSize;
                }
                else
                {
                    favorable = (High[0] - t.TouchPrice) / tickSize;
                    adverse = (t.TouchPrice - Low[0]) / tickSize;
                }

                // v3.1.3: Always update Raw MFE/MAE (uncapped, tracks to EOD)
                if (favorable > t.RawMFE) { t.RawMFE = favorable; t.RawMFEBars = elapsed; }
                if (adverse > t.RawMAE) t.RawMAE = adverse;

                // Update regular MFE/MAE only while trade is still open (capped by SL/TP)
                if (t.ExitType == 0)
                {
                    if (favorable > t.MFE) { t.MFE = favorable; t.MFEBars = elapsed; }
                    if (adverse > t.MAE) t.MAE = adverse;
                }

                // --- SL/TP exit detection (only for episode-first touches with pending exit) ---
                if (t.IsEpisodeFirst && t.ExitType == 0)
                {
                    if (adverse >= slTicks)
                    {
                        t.ExitType = 2; // SL
                        t.ExitBarIdx = CurrentBar;
                        bool isShort = (t.Config == "B" || t.Config == "C");
                        t.ExitPrice = isShort ? t.TouchPrice + slTicks * tickSize : t.TouchPrice - slTicks * tickSize;
                    }
                    else if (favorable >= tpTicks)
                    {
                        t.ExitType = 1; // TP
                        t.ExitBarIdx = CurrentBar;
                        bool isShort = (t.Config == "B" || t.Config == "C");
                        t.ExitPrice = isShort ? t.TouchPrice - tpTicks * tickSize : t.TouchPrice + tpTicks * tickSize;
                    }
                }

                // v3.1.3: EOD check — applies to both pending and already-exited trades (for raw tracking)
                if (!t.RawComplete)
                {
                    DateTime barTime = Bars.GetTime(CurrentBar);
                    DateTime entryTime = Bars.GetTime(t.BarIdx);

                    if (_cachedUsaEndTimeStr != USEndTime)
                    {
                        _cachedUsaEndTimeStr = USEndTime;
                        DateTime tmpEnd;
                        _cachedUsaEndTimeSpan = DateTime.TryParse(USEndTime, out tmpEnd) ? tmpEnd.TimeOfDay : new TimeSpan(17, 0, 0);
                    }
                    TimeSpan usaEnd = _cachedUsaEndTimeSpan;

                    try
                    {
                        if (_cachedEasternZone == null)
                            _cachedEasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                        if (_cachedEasternZone != null && !_cachedEasternZone.IsDaylightSavingTime(barTime) && EodWinterOffsetHours > 0)
                            usaEnd = usaEnd.Add(TimeSpan.FromHours(EodWinterOffsetHours));
                    }
                    catch { }

                    bool entryBeforeEod = entryTime.TimeOfDay < usaEnd;
                    bool barAtEod = barTime.TimeOfDay >= usaEnd;
                    bool nextDayEod = barTime.Date > entryTime.Date && barAtEod;

                    if (barAtEod && (entryBeforeEod || nextDayEod))
                    {
                        // Mark raw tracking complete
                        t.RawComplete = true;
                        t.EodBarIdx = CurrentBar;

                        // If trade never exited by SL/TP, mark as EOD exit
                        if (t.ExitType == 0)
                        {
                            t.ExitType = 3; // EOD
                            t.ExitBarIdx = CurrentBar;
                            t.ExitPrice = Close[0];
                        }
                    }
                }

                if (t.MFE >= 2.0 && favorable <= 0)
                    t.MFEComplete = true;

                _activeFirstTouches[idx] = t;
            }

            // v3.2.0: Purge completed touches to _completedFirstTouches for performance
            // Without this, _activeFirstTouches grows unbounded during playback → O(n) per bar
            if (_activeFirstTouches.Count > 50)
            {
                for (int idx = _activeFirstTouches.Count - 1; idx >= 0; idx--)
                {
                    var t = _activeFirstTouches[idx];
                    bool done = TouchStudyRawMode ? t.RawComplete : (t.ExitType != 0);
                    if (done)
                    {
                        _completedFirstTouches.Add(t);
                        _activeFirstTouches.RemoveAt(idx);
                    }
                }
            }
        }

        /// <summary>v3.1.0: Check if auto-trade is enabled for this config + within trade window</summary>
        private bool IsAutoTradeEnabled(string config)
        {
            if (_autoTradeOpen) return false;
            if (string.IsNullOrEmpty(config) || config == "-") return false;

            // Check toolbar toggle
            bool enabled = false;
            if (config == "A") enabled = _tradeCfgA;
            else if (config == "B") enabled = _tradeCfgB;
            else if (config == "C") enabled = _tradeCfgC;
            else if (config == "D") enabled = _tradeCfgD;
            if (!enabled) return false;

            // Check trade window
            if (UseTradeWindow)
            {
                TimeSpan barTime = Time[0].TimeOfDay;
                TimeSpan windowStart, windowEnd;
                if (!TimeSpan.TryParse(TradeWindowStart, out windowStart)) windowStart = new TimeSpan(9, 30, 0);
                if (!TimeSpan.TryParse(TradeWindowEnd, out windowEnd)) windowEnd = new TimeSpan(12, 30, 0);
                if (barTime < windowStart || barTime > windowEnd) return false;
            }

            return true;
        }

        /// <summary>v3.0.7: Classify touch config at detection time</summary>
        private string ClassifyTouchConfig(bool touchedHigh, double hScore, double lScore)
        {
            // v3.2.0: Use configurable thresholds (previously hardcoded 3.0/2.0)
            double strong = HealthStrongThreshold;
            double weak = HealthWeakThreshold;
            bool supplyFuerte = hScore >= strong;
            bool demandDebil = lScore < weak;
            bool demandFuerte = lScore >= strong;
            bool supplyDebil = hScore < weak;

            if (!touchedHigh && supplyFuerte && demandDebil) return "B";
            if (touchedHigh && supplyFuerte && demandDebil) return "C";
            if (touchedHigh && demandFuerte && supplyDebil) return "A";
            if (!touchedHigh && demandFuerte && supplyDebil) return "D";
            return "-";
        }

        /// <summary>v3.0.8: Episode grouping — no new trade if same config has open trade or within gap bars</summary>
        private bool IsEpisodeFirstTouch(string config, int barIdx)
        {
            if (config == "-") return true; // unclassified always shown

            int gap = TouchStudyEpisodeGap > 0 ? TouchStudyEpisodeGap : 15;
            int lastBar = -999;
            if (config == "B") lastBar = _lastConfigBBar;
            else if (config == "C") lastBar = _lastConfigCBar;
            else if (config == "A") lastBar = _lastConfigABar;
            else if (config == "D") lastBar = _lastConfigDBar;

            // Bar gap check
            if ((barIdx - lastBar) <= gap) return false;

            // v3.0.8: Block new episode if same config has an open trade (ExitType == 0)
            for (int i = 0; i < _activeFirstTouches.Count; i++)
            {
                var t = _activeFirstTouches[i];
                if (t.IsEpisodeFirst && t.Config == config && t.ExitType == 0)
                    return false; // there's still an open trade for this config
            }
            return true;
        }

        /// <summary>v3.0.7: Update last config bar for episode tracking</summary>
        private void UpdateLastConfigBar(string config, int barIdx)
        {
            if (config == "B") _lastConfigBBar = barIdx;
            else if (config == "C") _lastConfigCBar = barIdx;
            else if (config == "A") _lastConfigABar = barIdx;
            else if (config == "D") _lastConfigDBar = barIdx;
        }

        private void ResetTouchStudy(bool isHighVwap)
        {
            if (isHighVwap)
            {
                _highVwapSeparated = false;
                _highSeparationTicks = 0;
            }
            else
            {
                _lowVwapSeparated = false;
                _lowSeparationTicks = 0;
            }
        }

        private void WriteTouchStudyCsv()
        {
            // Collect ALL touches: active + all historical (CSV exports everything, TouchStudyDays only affects chart labels)
            var allTouches = new List<FirstTouchRecord>();
            int totalBars = Bars != null ? Bars.Count : 0;

            // Collect from active list
            if (_activeFirstTouches != null)
            {
                for (int i = 0; i < _activeFirstTouches.Count; i++)
                    allTouches.Add(_activeFirstTouches[i]);
            }

            // v3.2.0: Collect from completed list (purged from active for performance)
            if (_completedFirstTouches != null)
            {
                for (int i = 0; i < _completedFirstTouches.Count; i++)
                    allTouches.Add(_completedFirstTouches[i]);
            }

            // Collect from ALL historical anchors (no day filter for CSV)
            if (historicalHighs != null)
                foreach (var a in historicalHighs)
                    if (a.FirstTouches != null)
                        for (int i = 0; i < a.FirstTouches.Count; i++)
                            allTouches.Add(a.FirstTouches[i]);
            if (historicalLows != null)
                foreach (var a in historicalLows)
                    if (a.FirstTouches != null)
                        for (int i = 0; i < a.FirstTouches.Count; i++)
                            allTouches.Add(a.FirstTouches[i]);

            if (allTouches.Count == 0) return;

            try
            {
                string baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NinjaTrader 8", "bin", "Custom", "Strategies", "TradeExports", "DEMO619219"
                );
                if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);

                string instrName = Instrument.MasterInstrument.Name.Replace(" ", "");
                string fileName = string.Format("VWAP_TOUCHSTUDY_{0}_{1:MM-yy}.csv", instrName, DateTime.Now);
                string filePath = Path.Combine(baseDir, fileName);

                bool rawMode = TouchStudyRawMode;

                using (var sw = new StreamWriter(filePath, false, System.Text.Encoding.UTF8))
                {
                    // v3.1.3: Header with optional RAW columns
                    string header = "Date,Time,TouchedVwap,VwapPrice,TouchPrice,HighHealthScore,LowHealthScore,ATR,SeparationTicks,MFE,MAE,MFEBars,Config,EpisodeFirst,ExitType,ExitTime,ExitPrice";
                    if (rawMode)
                        header += ",OtherVwapPrice,VwapGapTicks,RawMFE,RawMAE,RawMFEBars,TotalBars,MFE_5,MAE_5,MFE_10,MAE_10,MFE_20,MAE_20,MFE_50,MAE_50,MFE_100,MAE_100,MFE_200,MAE_200,ImpulseDelta,ImpulseVolume,ImpulseBars,RetraceDelta,RetraceVolume,RetraceBars";
                    sw.WriteLine(header);

                    allTouches.Sort((a, b) => a.BarIdx.CompareTo(b.BarIdx));

                    // Deduplicate by BarIdx + TouchedHighVwap — prefer record with ExitType > 0 (closed trades have real data)
                    var dedupDict = new Dictionary<long, FirstTouchRecord>();
                    foreach (var t in allTouches)
                    {
                        long key = (long)t.BarIdx * 10 + (t.TouchedHighVwap ? 1 : 0);
                        if (!dedupDict.ContainsKey(key))
                            dedupDict[key] = t;
                        else if (t.ExitType > 0 && dedupDict[key].ExitType == 0)
                            dedupDict[key] = t; // prefer closed trade over stale open copy
                    }
                    var dedupTouches = new List<FirstTouchRecord>(dedupDict.Values);
                    dedupTouches.Sort((a, b) => a.BarIdx.CompareTo(b.BarIdx));

                    // v3.1.3: Path snapshot intervals
                    int[] snapIntervals = new int[] { 5, 10, 20, 50, 100, 200 };

                    int exportCount = 0;
                    foreach (var t in dedupTouches)
                    {
                        if (t.BarIdx < 0 || t.BarIdx >= totalBars) continue;

                        // v3.0.7: Use pre-classified config, fallback for old data
                        string config = t.Config;
                        if (string.IsNullOrEmpty(config))
                        {
                            config = "-";
                            bool supplyFuerte = t.HighHealthScore >= 3.0;
                            bool demandDebil = t.LowHealthScore < 2.0;
                            bool demandFuerte = t.LowHealthScore >= 3.0;
                            bool supplyDebil = t.HighHealthScore < 2.0;

                            if (!t.TouchedHighVwap && supplyFuerte && demandDebil) config = "B";
                            else if (t.TouchedHighVwap && supplyFuerte && demandDebil) config = "C";
                            else if (!t.TouchedHighVwap && demandFuerte && supplyDebil) config = "D";
                            else if (t.TouchedHighVwap && demandFuerte && supplyDebil) config = "A";
                        }

                        DateTime barTime = Bars.GetTime(t.BarIdx);
                        string exitTypeStr = t.ExitType == 1 ? "TP" : t.ExitType == 2 ? "SL" : t.ExitType == 3 ? "EOD" : "Open";
                        string exitTimeStr = "";
                        string exitPriceStr = "";
                        if (t.ExitType > 0 && t.ExitBarIdx > 0 && t.ExitBarIdx < totalBars)
                        {
                            exitTimeStr = Bars.GetTime(t.ExitBarIdx).ToString("HH:mm:ss");
                            exitPriceStr = t.ExitPrice.ToString("F2");
                        }

                        string baseLine = string.Format(
                            "{0:yyyy-MM-dd},{1:HH:mm:ss},{2},{3:F2},{4:F2},{5:F2},{6:F2},{7:F4},{8:F1},{9:F1},{10:F1},{11},{12},{13},{14},{15},{16}",
                            barTime.Date, barTime, t.TouchedHighVwap ? "Supply" : "Demand",
                            t.VwapPrice, t.TouchPrice, t.HighHealthScore, t.LowHealthScore,
                            t.ATRValue, t.Separation, t.MFE, t.MAE, t.MFEBars, config,
                            t.IsEpisodeFirst ? 1 : 0, exitTypeStr, exitTimeStr, exitPriceStr);

                        if (rawMode)
                        {
                            double tickSz = TickSize > 0 ? TickSize : 0.25;
                            double vwapGap = t.OtherVwapPrice > 0 ? Math.Abs(t.VwapPrice - t.OtherVwapPrice) / tickSz : 0;
                            int eodBar = t.EodBarIdx > 0 ? t.EodBarIdx : (t.ExitBarIdx > 0 ? t.ExitBarIdx : totalBars - 1);
                            int totalBarCount = eodBar - t.BarIdx;

                            // Determine trade direction for path computation
                            bool isShort = (config == "B" || config == "C") || (config != "A" && config != "D" && t.TouchedHighVwap);

                            // Compute path snapshots from bar data
                            var snapMfe = new double[snapIntervals.Length];
                            var snapMae = new double[snapIntervals.Length];

                            // Walk through bars to compute cumulative MFE/MAE at each snapshot
                            double cumMfe = 0, cumMae = 0;
                            int snapIdx = 0;
                            int maxBars = Math.Min(eodBar, totalBars - 1) - t.BarIdx;
                            for (int b = 1; b <= maxBars && snapIdx < snapIntervals.Length; b++)
                            {
                                int barI = t.BarIdx + b;
                                if (barI >= totalBars) break;
                                double hi = Bars.GetHigh(barI);
                                double lo = Bars.GetLow(barI);
                                double fav, adv;
                                if (isShort)
                                {
                                    fav = (t.TouchPrice - lo) / tickSz;
                                    adv = (hi - t.TouchPrice) / tickSz;
                                }
                                else
                                {
                                    fav = (hi - t.TouchPrice) / tickSz;
                                    adv = (t.TouchPrice - lo) / tickSz;
                                }
                                if (fav > cumMfe) cumMfe = fav;
                                if (adv > cumMae) cumMae = adv;

                                // Record snapshot when we reach the interval
                                while (snapIdx < snapIntervals.Length && b >= snapIntervals[snapIdx])
                                {
                                    snapMfe[snapIdx] = cumMfe;
                                    snapMae[snapIdx] = cumMae;
                                    snapIdx++;
                                }
                            }
                            // Fill remaining snapshots with final values
                            for (; snapIdx < snapIntervals.Length; snapIdx++)
                            {
                                snapMfe[snapIdx] = cumMfe;
                                snapMae[snapIdx] = cumMae;
                            }

                            baseLine += string.Format(",{0:F2},{1:F1},{2:F1},{3:F1},{4},{5}",
                                t.OtherVwapPrice, vwapGap, t.RawMFE, t.RawMAE, t.RawMFEBars, totalBarCount);
                            for (int s = 0; s < snapIntervals.Length; s++)
                                baseLine += string.Format(",{0:F1},{1:F1}", snapMfe[s], snapMae[s]);
                            // v3.2.0: Phase analysis columns
                            baseLine += string.Format(",{0:F0},{1:F0},{2},{3:F0},{4:F0},{5}",
                                t.ImpulseDelta, t.ImpulseVolume, t.ImpulseBars,
                                t.RetraceDelta, t.RetraceVolume, t.RetraceBars);
                        }

                        sw.WriteLine(baseLine);
                        exportCount++;
                    }
                    // Diagnostic: count per config
                    int cntA = 0, cntB = 0, cntC = 0, cntD = 0, cntDash = 0;
                    foreach (var t in dedupTouches)
                    {
                        string c = t.Config;
                        if (string.IsNullOrEmpty(c)) c = "-";
                        if (c == "A") cntA++; else if (c == "B") cntB++; else if (c == "C") cntC++; else if (c == "D") cntD++; else cntDash++;
                    }
                    string modeStr = rawMode ? " [RAW]" : "";
                    Print(string.Format("[TOUCH_STUDY]{0} CSV guardado: {1} ({2} exportados, {3} dedup) A={4} B={5} C={6} D={7} -={8}",
                        modeStr, filePath, exportCount, dedupTouches.Count, cntA, cntB, cntC, cntD, cntDash));
                }
            }
            catch (Exception ex)
            {
                Print("[TOUCH_STUDY] ERROR escribiendo CSV: " + ex.Message);
            }
        }

        private void RecordVwapApproach(string side, double vwapPrice, double touchPrice, int anchorBarIdx, int touchNum, double hVwap, double lVwap)
        {
            double tickSize = TickSize;

            // VWAP Slope: rate of change over last 10 bars (in ticks)
            double slope = 0;
            if (side == "Supply" && Values[0].IsValidDataPointAt(10) && Values[0].IsValidDataPointAt(0))
                slope = (Values[0][0] - Values[0][10]) / tickSize;
            else if (side == "Demand" && Values[1].IsValidDataPointAt(10) && Values[1].IsValidDataPointAt(0))
                slope = (Values[1][0] - Values[1][10]) / tickSize;

            // VWAP Spread (distance between the two VWAPs in ticks)
            double spread = Math.Abs(hVwap - lVwap) / tickSize;

            _vwapApproaches.Add(new VwapApproachRecord
            {
                Date = Time[0].Date,
                Time = Time[0],
                Instrument = Instrument.FullName,
                VwapSide = side,
                VwapPrice = vwapPrice,
                TouchPrice = touchPrice,
                VwapAge = CurrentBar - anchorBarIdx,
                TouchNumber = touchNum,
                VwapSlope = Math.Round(slope, 1),
                VwapSpread = Math.Round(spread, 1),
                DeltaGlobal = CaptureDelta ? _deltaGlobal : 0,
                ATR = atr != null && atr.IsValidDataPointAt(0) ? atr[0] : 0,
                // MFE/MAE/EOD calculated later in FinalizeVwapApproaches()
                MFE_Rejection = 0,
                MAE_Penetration = 0,
                EOD_Price = 0,
                EOD_Result = "Pending",
                TouchBarIdx = CurrentBar,
                EODBarIdx = -1
            });
        }

        /// <summary>
        /// v3.0.4: Calculate MFE/MAE/EOD for all pending approach records.
        /// Called when entering Realtime state (all historical bars available).
        /// </summary>
        private void FinalizeVwapApproaches()
        {
            if (_vwapApproaches.Count == 0) return;

            double tickSize = TickSize;
            TimeSpan usaEnd = GetTimeByZone(USEndTime);

            for (int i = 0; i < _vwapApproaches.Count; i++)
            {
                var rec = _vwapApproaches[i];
                if (rec.EOD_Result != "Pending") continue;

                // Find the bar where this touch happened
                int touchBar = -1;
                for (int b = 0; b < Bars.Count; b++)
                {
                    if (Time.GetValueAt(b) == rec.Time) { touchBar = b; break; }
                }
                if (touchBar < 0) continue;

                // Find EOD bar (first bar at or after US end time on same date, or last bar of day)
                int eodBar = -1;
                for (int b = touchBar + 1; b < Bars.Count; b++)
                {
                    DateTime barTime = Time.GetValueAt(b);
                    // Past the touch date's US end
                    if (barTime.Date == rec.Date && barTime.TimeOfDay >= usaEnd)
                    {
                        eodBar = b;
                        break;
                    }
                    // Crossed into next day
                    if (barTime.Date > rec.Date)
                    {
                        eodBar = b - 1;
                        break;
                    }
                }
                if (eodBar < 0) eodBar = Bars.Count - 1;

                // Calculate MFE and MAE from touch bar to EOD
                double vwap = rec.VwapPrice;
                bool isSupply = (rec.VwapSide == "Supply");
                double maxRejection = 0; // Movement AWAY from VWAP (favorable hold)
                double maxPenetration = 0; // Movement THROUGH VWAP (adverse break)

                for (int b = touchBar; b <= eodBar; b++)
                {
                    double h = High.GetValueAt(b);
                    double l = Low.GetValueAt(b);

                    if (isSupply)
                    {
                        // Supply VWAP: rejection = price going DOWN, penetration = price going UP through VWAP
                        double rejDist = (vwap - l) / tickSize;
                        double penDist = (h - vwap) / tickSize;
                        if (rejDist > maxRejection) maxRejection = rejDist;
                        if (penDist > maxPenetration) maxPenetration = penDist;
                    }
                    else
                    {
                        // Demand VWAP: rejection = price going UP, penetration = price going DOWN through VWAP
                        double rejDist = (h - vwap) / tickSize;
                        double penDist = (vwap - l) / tickSize;
                        if (rejDist > maxRejection) maxRejection = rejDist;
                        if (penDist > maxPenetration) maxPenetration = penDist;
                    }
                }

                double eodClose = Close.GetValueAt(eodBar);
                rec.MFE_Rejection = Math.Round(maxRejection, 1);
                rec.MAE_Penetration = Math.Round(maxPenetration, 1);
                rec.EOD_Price = eodClose;
                rec.TouchBarIdx = touchBar;
                rec.EODBarIdx = eodBar;

                // Held = price closed on rejection side, Broken = price closed on penetration side
                if (isSupply)
                    rec.EOD_Result = (eodClose < vwap) ? "Held" : "Broken";
                else
                    rec.EOD_Result = (eodClose > vwap) ? "Held" : "Broken";
            }
        }

        /// <summary>
        /// v3.0.4: Write VWAP approach records to CSV.
        /// </summary>
        private void WriteVwapApproachesCsv()
        {
            if (_vwapApproaches.Count == 0) return;
            try
            {
                string baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NinjaTrader 8", "bin", "Custom", "Strategies", "TradeExports", "DEMO619219"
                );
                if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);

                string instrName = Instrument.MasterInstrument.Name.Replace(" ", "");
                string fileName = string.Format("VWAP_APPROACH_{0}_{1:MM-yy}.csv", instrName, DateTime.Now);
                string filePath = Path.Combine(baseDir, fileName);

                using (var sw = new StreamWriter(filePath, false, System.Text.Encoding.UTF8))
                {
                    sw.WriteLine("Date,Time,Instrument,VwapSide,VwapPrice,TouchPrice," +
                        "VwapAge,TouchNumber,VwapSlope,VwapSpread,DeltaGlobal,ATR," +
                        "MFE_Rejection,MAE_Penetration,EOD_Price,EOD_Result");

                    foreach (var r in _vwapApproaches)
                    {
                        if (r.EOD_Result == "Pending") continue; // Skip unfinalized
                        sw.WriteLine(string.Format(
                            "{0:yyyy-MM-dd},{1:HH:mm:ss},{2},{3},{4:F2},{5:F2}," +
                            "{6},{7},{8:F1},{9:F1},{10:F0},{11:F4}," +
                            "{12:F1},{13:F1},{14:F2},{15}",
                            r.Date, r.Time, r.Instrument, r.VwapSide, r.VwapPrice, r.TouchPrice,
                            r.VwapAge, r.TouchNumber, r.VwapSlope, r.VwapSpread, r.DeltaGlobal, r.ATR,
                            r.MFE_Rejection, r.MAE_Penetration, r.EOD_Price, r.EOD_Result));
                    }
                }
                Print(string.Format("[VWAP_APPROACH] CSV guardado: {0} ({1} registros)", filePath, _vwapApproaches.Count));
            }
            catch (Exception ex)
            {
                Print("[VWAP_APPROACH] ERROR: " + ex.Message);
            }
        }

        /// <summary>
        /// Write bar-by-bar price path for each touch to a separate CSV.
        /// Each row = one bar after the touch, with High/Low/Close and running MFE/MAE in ticks.
        /// Used by Python to simulate trailing stops with realistic price paths.
        /// </summary>
        private void WriteApproachPathCsv()
        {
            if (_vwapApproaches.Count == 0) return;

            // Count how many have valid bar indices
            int validCount = 0;
            for (int i = 0; i < _vwapApproaches.Count; i++)
            {
                var r = _vwapApproaches[i];
                if (r.TouchBarIdx >= 0 && r.EODBarIdx > r.TouchBarIdx && r.EOD_Result != "Pending"
                    && r.VwapSpread >= 51 && r.VwapSpread <= 100 && r.TouchNumber <= 3)
                    validCount++;
            }
            if (validCount == 0) return;

            try
            {
                string baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NinjaTrader 8", "bin", "Custom", "Strategies", "TradeExports", "DEMO619219"
                );
                if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);

                string instrName = Instrument.MasterInstrument.Name.Replace(" ", "");
                string fileName = string.Format("VWAP_PATH_{0}_{1:MM-yy}.csv", instrName, DateTime.Now);
                string filePath = Path.Combine(baseDir, fileName);

                double tickSize = TickSize > 0 ? TickSize : 0.25;

                using (var sw = new StreamWriter(filePath, false, System.Text.Encoding.UTF8))
                {
                    sw.WriteLine("TouchDate,TouchTime,VwapSide,VwapSpread,TouchNumber,ATR,BarsSinceTouch,Time,High,Low,Close,MFE_Running,MAE_Running");

                    for (int i = 0; i < _vwapApproaches.Count; i++)
                    {
                        var rec = _vwapApproaches[i];
                        if (rec.TouchBarIdx < 0 || rec.EODBarIdx <= rec.TouchBarIdx || rec.EOD_Result == "Pending")
                            continue;

                        // Filter: Spread 51-100 (Wide), first 3 touches — matches trailing study criteria
                        if (rec.VwapSpread < 51 || rec.VwapSpread > 100 || rec.TouchNumber > 3)
                            continue;

                        double vwap = rec.VwapPrice;
                        bool isSupply = (rec.VwapSide == "Supply");
                        double runMFE = 0;
                        double runMAE = 0;

                        // Limit to 120 bars (2h) per touch — enough for trailing stop simulation
                        int maxBar = Math.Min(rec.EODBarIdx, rec.TouchBarIdx + 120);
                        for (int b = rec.TouchBarIdx; b <= maxBar; b++)
                        {
                            double h = High.GetValueAt(b);
                            double l = Low.GetValueAt(b);
                            double c = Close.GetValueAt(b);
                            DateTime barTime = Time.GetValueAt(b);
                            int barsSince = b - rec.TouchBarIdx;

                            if (isSupply)
                            {
                                double rej = (vwap - l) / tickSize;
                                double pen = (h - vwap) / tickSize;
                                if (rej > runMFE) runMFE = rej;
                                if (pen > runMAE) runMAE = pen;
                            }
                            else
                            {
                                double rej = (h - vwap) / tickSize;
                                double pen = (vwap - l) / tickSize;
                                if (rej > runMFE) runMFE = rej;
                                if (pen > runMAE) runMAE = pen;
                            }

                            // Write every bar: Python needs full path for trailing simulation
                            sw.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "{0:yyyy-MM-dd},{1:HH:mm:ss},{2},{3:F1},{4},{5:F4},{6},{7:HH:mm:ss},{8:F2},{9:F2},{10:F2},{11:F1},{12:F1}",
                                rec.Date, rec.Time, rec.VwapSide, rec.VwapSpread, rec.TouchNumber, rec.ATR,
                                barsSince, barTime, h, l, c, runMFE, runMAE));
                        }
                    }
                }

                Print(string.Format("[VWAP_PATH] CSV guardado: {0} ({1} toques con path)", filePath, validCount));
            }
            catch (Exception ex)
            {
                Print("[VWAP_PATH] ERROR: " + ex.Message);
            }
        }

        private double GetCommissionPerContract()
        {
            string sym = Instrument.MasterInstrument.Name;
            if (sym.StartsWith("MNQ")) return 4.10;
            if (sym.StartsWith("MES")) return 2.50;
            if (sym.StartsWith("MCL")) return 2.10;
            if (sym.StartsWith("MGC")) return 2.10;
            if (sym.StartsWith("NQ"))  return 4.10;
            if (sym.StartsWith("ES"))  return 4.10;
            if (sym.StartsWith("CL"))  return 4.10;
            if (sym.StartsWith("GC"))  return 4.10;
            return 4.10;
        }

        private void WriteSimulatedTradesToCsv()
        {
            if (_simExportRecords.Count == 0) return;
            try
            {
                string baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NinjaTrader 8", "bin", "Custom", "Strategies", "TradeExports", "DEMO619219"
                );
                if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);

                string instrName = Instrument.MasterInstrument.Name.Replace(" ", "");
                string fileName = string.Format("VWAP_{0}_{1:MM-yy}.csv", instrName, DateTime.Now);
                string filePath = Path.Combine(baseDir, fileName);

                bool fileExists = File.Exists(filePath);
                
                // v2.2.2: Fix Overwrite Issue - Use Append=true
                using (var sw = new StreamWriter(filePath, true, System.Text.Encoding.UTF8))
                {
                    if (!fileExists)
                    {
                        // v3.0.4: Cleaned CSV — 20 columns
                        sw.WriteLine("ID,Instrument,EntryTime,Type,EntryPrice,ExitTime,ExitPrice,Result," +
                            "PnL,MAE,MFE,Setup,Attempt,DeltaGlobal,LevelAge,Trade_Clust_ID," +
                            "ATR_Value,VolumeRatio,Overlapping,TradeMode");
                    }

                    foreach (var r in _simExportRecords)
                    {
                        // v3.0.4: Cleaned CSV — 20 columns
                        sw.WriteLine(string.Format(
                            "{0},{1},{2:yyyy-MM-dd HH:mm:ss},{3},{4:F2},{5:yyyy-MM-dd HH:mm:ss},{6:F2},{7}," +
                            "{8:F2},{9:F2},{10:F2},{11},{12},{13:F0},{14},{15}," +
                            "{16:F4},{17:F2},{18},{19}",
                            r.ID, r.Instrument, r.EntryTime, r.Type, r.EntryPrice,
                            r.ExitTime, r.ExitPrice, r.Result,
                            r.PnL, r.MAE, r.MFE,
                            r.Setup, r.Attempt, r.DeltaGlobal, r.LevelAge, r.TradeClustID,
                            r.ATR_Value, r.VolumeRatio,
                            r.Overlapping ? 1 : 0, r.IsTrendTrade ? "Trend" : "Reversal"));
                    }
                }
                Print(string.Format("[VWAP_EXPORT] CSV guardado: {0} ({1} registros)", filePath, _simExportRecords.Count));
            }
            catch (Exception ex)
            {
                Print("[VWAP_EXPORT] ERROR: " + ex.Message);
            }
        }

        /// <summary>
        /// Stores signal for later processing when all historical data is available
        /// </summary>
        // v3.0.4: Cleaned signature — removed redundant delta params
        private void DrawSignalVisualization(bool isLong, int anchorBarIdx, double vwapPrice, double tp1, double tp2, int quantity, double sl,
            string setupName = "", int anchorSequence = 0, DateTime anchorTime = default(DateTime),
            double deltaGlobal = 0, double atrVal = 0, double volRatio = 0, bool isTrendTrade = false)
        {
            var sig = new PendingSignal
            {
                IsLong = isLong,
                AnchorBarIdx = anchorBarIdx,
                SignalBarIdx = CurrentBar - 1,
                VwapPrice = vwapPrice,
                TP1 = tp1,
                TP2 = tp2,
                Quantity = quantity,
                SL = sl,
                SetupName = setupName,
                AnchorSequence = anchorSequence,
                AnchorTime = (anchorTime == default(DateTime)) ? Time[0] : anchorTime,
                SignalTime = (CurrentBar >= 0 && Bars != null) ? Time[1] : DateTime.Now,
                DeltaGlobal = deltaGlobal,
                ATR_Value = atrVal,
                VolumeRatio = volRatio,
                IsTrendTrade = isTrendTrade
            };

            // DEBUG FORCE: Log entry to this method
            LogToFile(string.Format("VISUAL_ENTRY: {0} Sig @ {1} | Anch:{2} | Qty:{3} | State:{4}", 
                isLong?"LONG":"SHORT", CurrentBar, anchorBarIdx, quantity, State), "VISUAL_DEBUG");

            // ALWAYS queue signals for batch processing at the end
            // This allows simulation of future bars to detect TP/SL hits
            _pendingSignals.Add(sig);
            _lastSignal2BarIdx = CurrentBar - 1; // v3.0.1: Track for overlap prevention
            LogToFile(string.Format("SIGNAL_QUEUED: {0} Signal @ Bar {1} | Anchor:{2} | Qty:{3} | TP1:{4} | TP2:{5} | Total:{6}",
                isLong ? "LONG" : "SHORT", CurrentBar, anchorBarIdx, quantity, tp1, tp2, _pendingSignals.Count), "SIGNAL");
        }

        /// <summary>
        /// v3.0.1: Quick check if the last simulated trade is still potentially active.
        /// Scans from last signal bar to current bar checking SL/TP1 hits.
        /// Returns true if trade would still be open → block new signals.
        /// </summary>
        private bool IsLastSimTradeStillOpen()
        {
            if (_lastSignal2BarIdx < 0 || _pendingSignals.Count == 0) return false;
            if (AnalyzeAllSignals) return false; // Overlap mode allows all signals

            var last = _pendingSignals[_pendingSignals.Count - 1];
            double sl = last.SL;
            double tp1 = last.TP1;

            // Quick scan: did SL or TP1 get hit since the signal?
            int start = last.SignalBarIdx + 1;
            for (int i = start; i <= CurrentBar && i < Bars.Count; i++)
            {
                double h = High.GetValueAt(i);
                double l = Low.GetValueAt(i);

                if (last.IsLong)
                {
                    if (l <= sl) return false;  // SL hit → trade closed
                    if (h >= tp1) return false;  // TP1 hit → partial close, allow new trade
                }
                else
                {
                    if (h >= sl) return false;  // SL hit → trade closed
                    if (l <= tp1) return false;  // TP1 hit → partial close, allow new trade
                }
            }
            return true; // Neither SL nor TP1 hit → still open → block
        }

        /// <summary>
        /// Process all pending signals and draw SL/TP lines for those that hit their targets
        /// </summary>
        public void ProcessPendingSignals()
        {
            // FIX: Only process once when entering Realtime
            if (_signalsProcessed) return;
            _signalsProcessed = true;
            
            // FIX: Don't use _signalsProcessed flag - always process pending signals
            // The reason: signals are added at different bars (e.g., 929, 932) but this gets called earlier too
            if (_pendingSignals.Count == 0) return; // Nothing to process

            LogToFile(string.Format("PROCESSING {0} pending signals...", _pendingSignals.Count), "SIGNAL_PROCESS");

            int processedCount = 0;
            foreach (var signal in _pendingSignals)
            {
                processedCount++;
                DrawStoredSignalVisualization(signal);
            }

            LogToFile("Signal processing complete", "SIGNAL_PROCESS");

            // v2.2.5: Detect overlapping trades when AnalyzeAllSignals is enabled
            if (AnalyzeAllSignals && _simExportRecords.Count > 1)
            {
                // Sort by EntryTime to process chronologically
                var sortedRecords = _simExportRecords.OrderBy(r => r.EntryTime).ToList();

                for (int i = 0; i < sortedRecords.Count; i++)
                {
                    var current = sortedRecords[i];
                    // Check if any earlier trade was still open when this one entered
                    for (int j = 0; j < i; j++)
                    {
                        var earlier = sortedRecords[j];
                        // If earlier trade's exit is after current's entry, they overlap
                        if (earlier.ExitTime > current.EntryTime)
                        {
                            current.Overlapping = true;
                            break;
                        }
                    }
                }

                int overlapCount = _simExportRecords.Count(r => r.Overlapping);
                if (ShowDebugLogs)
                    Print(string.Format("[VWAP_OVERLAP] Detected {0} overlapping trades out of {1}", overlapCount, _simExportRecords.Count));

                // v2.2.5: Draw visual labels for overlapping trades
                if (ShowSignalText)
                {
                    foreach (var rec in _simExportRecords.Where(r => r.Overlapping))
                    {
                        int barsAgo = CurrentBar - rec.SignalBarIdx;
                        if (barsAgo >= 0 && barsAgo < CurrentBar)
                        {
                            string tag = "OVL_" + rec.SignalBarIdx + "_" + rec.Type;
                            // Draw "OVL" label slightly offset from entry price
                            double offset = rec.Type == "Long" ? -8 * TickSize : 8 * TickSize;
                            Draw.Text(this, tag, "OVL", barsAgo, rec.EntryPrice + offset, Brushes.Magenta);
                        }
                    }
                }
            }

            // Export CSV if enabled
            if (ExportSimulationCSV)
                WriteSimulatedTradesToCsv();

            // v3.0.4: Export VWAP approach data
            if (ExportVwapApproaches)
            {
                FinalizeVwapApproaches();
                WriteVwapApproachesCsv();
                WriteApproachPathCsv();
            }

            // Clear after processing to avoid reprocessing
            _pendingSignals.Clear();
        }

        /// <summary>
        /// Draws SL/TP lines for a stored signal
        /// </summary>
        /// <summary>
        /// Draws SL/TP lines for a stored signal with Split TP Logic (50% / 50%)
        /// </summary>
        private void DrawStoredSignalVisualization(PendingSignal signal)
        {
            LogToFile(string.Format("DRAW_ENTRY: Signal Bar={0} IsLong={1} Anchor={2}", signal.SignalBarIdx, signal.IsLong, signal.AnchorBarIdx), "DRAW_DEBUG");
            
            try
            {
                // 1. Calculate Prices
                LogToFile("DRAW_STEP1: Calculating prices", "DRAW_DEBUG");
                double anchorPrice = signal.IsLong ? Low.GetValueAt(signal.AnchorBarIdx) : High.GetValueAt(signal.AnchorBarIdx);
                double slPrice = signal.IsLong 
                    ? anchorPrice - (StopAnchorOffsetTicks * TickSize) 
                    : anchorPrice + (StopAnchorOffsetTicks * TickSize);

                // Round SL
                slPrice = Instrument.MasterInstrument.RoundToTickSize(slPrice);
                
                // Define Entry Price for BE logic
                double entryPrice = Close.GetValueAt(signal.SignalBarIdx);
                
                // Position Sizing
                int qty1 = (int)Math.Ceiling(signal.Quantity / 2.0); // odd -> larger to TP1
                int qty2 = signal.Quantity - qty1;
                
                // Simulation State
                bool q1Open = true;
                bool q2Open = (qty2 > 0); // FIX: Only open if quantity exists
                int exitBar1 = -1, exitBar2 = -1;
                double exitPrice1 = 0, exitPrice2 = 0;
                bool win1 = false, win2 = false;
                bool eodExit1 = false, eodExit2 = false; // v2.2.4: End of Day exit tracking

                // MAE/MFE tracking (in price points)
                double maxAdverse1 = 0, maxFavorable1 = 0;
                double maxAdverse2 = 0, maxFavorable2 = 0;

                // v2.2.6: Track if TP1 was hit for trailing SL logic
                bool tp1WasHit = false;
                double trailingSL = slPrice; // Current SL (may trail after TP1)

                // 2. Run Simulation
                int start = signal.SignalBarIdx + 1;
                int end = Math.Min(CurrentBar, Bars.Count - 1);

                for (int i = start; i <= end; i++)
                {
                    double h = High.GetValueAt(i);
                    double l = Low.GetValueAt(i);

                    // Track MAE/MFE
                    if (q1Open || q2Open)
                    {
                        double adverse  = signal.IsLong ? Math.Max(0, entryPrice - l) : Math.Max(0, h - entryPrice);
                        double favorable = signal.IsLong ? Math.Max(0, h - entryPrice) : Math.Max(0, entryPrice - l);
                        if (q1Open) { maxAdverse1 = Math.Max(maxAdverse1, adverse); maxFavorable1 = Math.Max(maxFavorable1, favorable); }
                        if (q2Open) { maxAdverse2 = Math.Max(maxAdverse2, adverse); maxFavorable2 = Math.Max(maxFavorable2, favorable); }
                    }

                    // v2.2.6: Update Trailing SL after TP1 hit (if enabled)
                    if (tp1WasHit && TrailSLToVwapAfterTP1 && q2Open)
                    {
                        // Get the VWAP that originated the signal (LowVWAP for Long, HighVWAP for Short)
                        double vwapSL = 0;
                        if (signal.IsLong)
                        {
                            // For Long: SL trails the LowVWAP (Values[1])
                            if (Values[1].IsValidDataPointAt(i))
                                vwapSL = Values[1].GetValueAt(i);
                        }
                        else
                        {
                            // For Short: SL trails the HighVWAP (Values[0])
                            if (Values[0].IsValidDataPointAt(i))
                                vwapSL = Values[0].GetValueAt(i);
                        }

                        // Only update if valid and better than current (trail only, never widen)
                        if (vwapSL > 0)
                        {
                            if (signal.IsLong && vwapSL > trailingSL)
                                trailingSL = vwapSL; // Move SL up for Long
                            else if (!signal.IsLong && vwapSL < trailingSL)
                                trailingSL = vwapSL; // Move SL down for Short
                        }
                    }

                    // A) Check Stop Loss (Global or Trailing) - FUZZY COMPARISON
                    // Use a tiny epsilon (1e-9) to ensure "exact touches" are counted as hits visually
                    double currentSL = (tp1WasHit && TrailSLToVwapAfterTP1) ? trailingSL : slPrice;
                    bool slHit = false;
                    if (signal.IsLong)
                    {
                        slHit = (l <= currentSL + 1e-9);
                    }
                    else
                    {
                        slHit = (h >= currentSL - 1e-9);
                    }

                    if (slHit)
                    {
                        if (q1Open) { exitBar1 = i; exitPrice1 = currentSL; win1 = false; q1Open = false; }
                        if (q2Open) { exitBar2 = i; exitPrice2 = currentSL; win2 = (currentSL != slPrice); q2Open = false; } // win2 = true if trailing SL (profit)
                        break; // All out
                    }

                    // B) Check TPs
                    // v2.2.7: TREND TRADES skip TP1/TP2 checks - they use EOD exit only
                    // TP1: Dynamic VWAP
                    if (q1Open && !signal.IsTrendTrade)
                    {
                        double dynTp1 = 0;
                        if (signal.IsLong)
                        {
                             // Check Plot 0 (HighVWAP) using ABSOLUTE index
                             if (Values[0].IsValidDataPointAt(i))
                                 dynTp1 = Values[0].GetValueAt(i);
                             else
                                 dynTp1 = 999999; // Unreachable
                        }
                        else
                        {
                             // Check Plot 1 (LowVWAP) using ABSOLUTE index
                             if (Values[1].IsValidDataPointAt(i))
                                 dynTp1 = Values[1].GetValueAt(i);
                             else
                                 dynTp1 = 0; // Unreachable
                        }

                        // Treat 0 as invalid for VWAP (unless uninitialized, but here just safety)
                        if (Math.Abs(dynTp1) < 0.0001)
                            dynTp1 = signal.IsLong ? 999999 : 0;

                        bool tp1Hit = signal.IsLong ? (h >= dynTp1) : (l <= dynTp1);

                        // DEBUG LOGGING
                        if (signal.SignalBarIdx > Bars.Count - 200 && ShowDebugLogs)
                        {
                            LogToFile(string.Format("LOOP Bar:{0} | {1} | H:{2:F2} L:{3:F2} | DynTP1:{4:F2} | Hit:{5}",
                                i, signal.IsLong?"LONG":"SHORT", h, l, dynTp1, tp1Hit), "LOOP_DEBUG");
                        }

                        if (tp1Hit)
                        {
                            exitBar1 = i; exitPrice1 = dynTp1; win1 = true; q1Open = false;

                            // v2.2.6: Mark TP1 as hit for trailing SL logic
                            tp1WasHit = true;

                            // Initialize trailing SL to current VWAP if enabled
                            if (TrailSLToVwapAfterTP1)
                            {
                                if (signal.IsLong && Values[1].IsValidDataPointAt(i))
                                    trailingSL = Values[1].GetValueAt(i);
                                else if (!signal.IsLong && Values[0].IsValidDataPointAt(i))
                                    trailingSL = Values[0].GetValueAt(i);
                            }
                        }
                    }

                    // TP2: Fixed Level (Passed in Signal)
                    // v2.2.7: TREND TRADES skip TP2 check - they use EOD exit only
                    if (q2Open && !signal.IsTrendTrade)
                    {
                        // signal.TP2 is the stored fixed target
                        bool tp2Hit = signal.IsLong ? (h >= signal.TP2) : (l <= signal.TP2);
                        if (tp2Hit)
                        {
                            exitBar2 = i; exitPrice2 = signal.TP2; win2 = true; q2Open = false;
                        }
                    }

                    // C) End of Day Exit - Close at or after USA session end
                    // v2.2.4: Adjust for DST - add 1 hour in winter (non-DST) since bar times are exchange TZ
                    if (q1Open || q2Open)
                    {
                        DateTime barTime = Time.GetValueAt(i);
                        // Parse USEndTime property
                        DateTime tmpEnd;
                        TimeSpan usaEnd = DateTime.TryParse(USEndTime, out tmpEnd) ? tmpEnd.TimeOfDay : new TimeSpan(17, 0, 0);

                        // Check if US is in DST for this bar's date - if NOT, add offset hours
                        TimeZoneInfo nyZone = null;
                        try { nyZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); } catch { }
                        if (nyZone != null && !nyZone.IsDaylightSavingTime(barTime) && EodWinterOffsetHours > 0)
                        {
                            usaEnd = usaEnd.Add(TimeSpan.FromHours(EodWinterOffsetHours)); // Winter: add configured offset
                        }

                        TimeSpan barTimeOfDay = barTime.TimeOfDay;

                        // Close on FIRST bar that reaches or passes USA end time
                        bool isEndOfDay = (barTimeOfDay >= usaEnd);

                        if (isEndOfDay)
                        {
                            double closePrice = Close.GetValueAt(i);
                            if (q1Open)
                            {
                                exitBar1 = i; exitPrice1 = closePrice;
                                win1 = signal.IsLong ? (closePrice > entryPrice) : (closePrice < entryPrice);
                                q1Open = false;
                                eodExit1 = true;
                            }
                            if (q2Open)
                            {
                                exitBar2 = i; exitPrice2 = closePrice;
                                win2 = signal.IsLong ? (closePrice > entryPrice) : (closePrice < entryPrice);
                                q2Open = false;
                                eodExit2 = true;
                            }
                        }
                    }

                    if (!q1Open && !q2Open) break;
                }

                // === CSV EXPORT RECORDS ===
                if (ExportSimulationCSV)
                {
                    string dateStr = signal.SignalTime.ToString("yyyyMMdd");
                    if (signal.SignalTime.Date != _lastExportDate.Date) { _dailyTradeCounter = 0; _lastExportDate = signal.SignalTime; }
                    _dailyTradeCounter++;

                    string clustID = dateStr + "_" + _dailyTradeCounter;
                    string direction = signal.IsLong ? "Long" : "Short";
                    int levelAge = GetBusinessDays(signal.AnchorTime.Date, signal.SignalTime.Date);
                    double pointValue = Instrument.MasterInstrument.PointValue;
                    double commPerCtr = GetCommissionPerContract();

                    if (exitBar1 != -1)
                    {
                        double pnl1csv = (signal.IsLong ? exitPrice1 - entryPrice : entryPrice - exitPrice1) * qty1 * pointValue;
                        double risk = Math.Abs(entryPrice - signal.SL);
                        double rr1 = risk > 0 ? Math.Abs(exitPrice1 - entryPrice) / risk : 0;
                        if (!win1) rr1 = -rr1;
                        // v2.2.4: EOD exit result type
                        string result1 = eodExit1
                            ? string.Format("EOD_{0}_{1:00}", direction, signal.AnchorSequence)
                            : (win1
                                ? string.Format("TP1_{0}_{1:00}", direction, signal.AnchorSequence)
                                : string.Format("SL_{0}_{1:00}", direction, signal.AnchorSequence));
                        _simExportRecords.Add(new SimTradeRecord {
                            ID = clustID, Instrument = Instrument.FullName,
                            EntryTime = signal.SignalTime, Type = direction, EntryPrice = entryPrice,
                            ExitTime = Time.GetValueAt(exitBar1), ExitPrice = exitPrice1, Result = result1,
                            PnL = pnl1csv, MAE = maxAdverse1 * qty1 * pointValue, MFE = maxFavorable1 * qty1 * pointValue,
                            Setup = signal.SetupName, Attempt = signal.AnchorSequence,
                            DeltaGlobal = signal.DeltaGlobal, LevelAge = levelAge, TradeClustID = clustID,
                            ATR_Value = signal.ATR_Value, VolumeRatio = signal.VolumeRatio,
                            SignalBarIdx = signal.SignalBarIdx,
                            IsTrendTrade = signal.IsTrendTrade
                        });
                    }

                    if (exitBar2 != -1 && qty2 > 0)
                    {
                        double pnl2csv = (signal.IsLong ? exitPrice2 - entryPrice : entryPrice - exitPrice2) * qty2 * pointValue;
                        double risk2 = Math.Abs(entryPrice - signal.SL);
                        double rr2 = risk2 > 0 ? Math.Abs(exitPrice2 - entryPrice) / risk2 : 0;
                        if (!win2) rr2 = -rr2;
                        // v2.2.4: EOD exit result type
                        string result2 = eodExit2
                            ? string.Format("EOD_{0}_{1:00}", direction, signal.AnchorSequence)
                            : (win2
                                ? string.Format("TP2_{0}_{1:00}", direction, signal.AnchorSequence)
                                : string.Format("SL_{0}_{1:00}", direction, signal.AnchorSequence));
                        _simExportRecords.Add(new SimTradeRecord {
                            ID = clustID + ".2", Instrument = Instrument.FullName,
                            EntryTime = signal.SignalTime, Type = direction, EntryPrice = entryPrice,
                            ExitTime = Time.GetValueAt(exitBar2), ExitPrice = exitPrice2, Result = result2,
                            PnL = pnl2csv, MAE = maxAdverse2 * qty2 * pointValue, MFE = maxFavorable2 * qty2 * pointValue,
                            Setup = signal.SetupName, Attempt = signal.AnchorSequence,
                            DeltaGlobal = signal.DeltaGlobal, LevelAge = levelAge, TradeClustID = clustID,
                            ATR_Value = signal.ATR_Value, VolumeRatio = signal.VolumeRatio,
                            SignalBarIdx = signal.SignalBarIdx,
                            IsTrendTrade = signal.IsTrendTrade
                        });
                    }
                }

                // 3. Draw Results
                // Draw for all trades (active or closed)
                // v3.0.3: ShowTradeVisualization controls visibility of trade lines/icons/labels
                if (ShowTradeVisualization)
                {
                     // double entryPrice = Close.GetValueAt(signal.SignalBarIdx); // Already defined above
                     double pointValue = Instrument.MasterInstrument.PointValue;
                     int signalBarsAgo = CurrentBar - signal.SignalBarIdx;
                     
                     // PnL Calc
                     double pnl1 = (exitBar1 != -1) ? (signal.IsLong ? exitPrice1 - entryPrice : entryPrice - exitPrice1) * qty1 * pointValue : 0;
                     double pnl2 = (exitBar2 != -1) ? (signal.IsLong ? exitPrice2 - entryPrice : entryPrice - exitPrice2) * qty2 * pointValue : 0;
                     double totalPnl = pnl1 + pnl2;
                     
                     // Draw Lines
                     // v2.2.7: Use TrendTradeColor for trend trades
                     Brush line1Color = signal.IsTrendTrade ? TrendTradeColor : (win1 ? WinTradeColor : LossTradeColor);
                     Brush line2Color = signal.IsTrendTrade ? TrendTradeColor : (win2 ? WinTradeColor : LossTradeColor);

                     // Line 1 (TP1 for reversal, EOD for trend)
                     if (exitBar1 != -1)
                     {
                         int endBars1 = CurrentBar - exitBar1;
                         Draw.Line(this, "Ex1_" + signal.SignalBarIdx, false, signalBarsAgo, entryPrice, endBars1, exitPrice1,
                             line1Color, DashStyleHelper.Solid, 2);
                     }

                     // Line 2 (TP2 for reversal, EOD for trend) - Only draw if different endpoint from 1
                     if (exitBar2 != -1 && (exitBar2 != exitBar1 || Math.Abs(exitPrice2 - exitPrice1) > TickSize))
                     {
                         int endBars2 = CurrentBar - exitBar2;
                         Draw.Line(this, "Ex2_" + signal.SignalBarIdx, false, signalBarsAgo, entryPrice, endBars2, exitPrice2,
                             line2Color, DashStyleHelper.Solid, 2);
                     }

                     // v2.1.0: FILTER: ONLY DRAW FOR CONFIRMED ENTRIES (SIGNAL 2)
                     // Skip visualization for simple liquidity grabs (Signal 1) to avoid clutter
                     // We identify Signal 2 by checking if it has a valid TP2 or specific flag
                     // Better: signal.Note or similar? We don't have that.
                     // Heuristic: Signal 1 doesn't usually have a fixed TP2 or sequence logic?
                     // Actually, the user said "labels on signal 1 is incorrect". 
                     // Signal 1 acts as a setup, Signal 2 is the entry.
                     // The pending signals we store are created in DrawSignalVisualization.
                     // We need to differentiate them. 
                     // For now, checks if it has a valid Stop sequence?
                     
                     // Assuming all stored signals here ARE trade entries? 
                     // No, DrawSignalVisualization is called for Signal 1 too.
                     // We need to filter based on logic.
                     
                     // v2.2.7: Check if it's a trade signal
                     // Reversal trades: have TP2 > 0
                     // Trend trades: have TP2 = 0 but IsTrendTrade = true
                     bool isTradeSignal = (Math.Abs(signal.TP2) > 0) || signal.IsTrendTrade; 
                     
                     if (isTradeSignal)
                     {
                          // FIX: Use relative indexing (BarsAgo) for robust historical access
                          // If exitBar1 is -1 (Active), use 0 (CurrentBar)
                          int barsAgo1 = (exitBar1 != -1) ? CurrentBar - exitBar1 : 0;
                          
                          // Basic validity check: ATR exists, and index is within bounds
                          // Note: atr[barsAgo] looks back from CurrentBar. 
                          // If barsAgo is 0, it's CurrentBar. If barsAgo is CurrentBar, it's the first bar.
                          bool isAtrValid1 = (atr != null && barsAgo1 >= 0); 
                          
                          double atrVal1 = -1;
                          if (isAtrValid1)
                          {
                              try 
                              {
                                  // Use relative indexing: atr[barsAgo1]
                                  atrVal1 = atr[barsAgo1]; 
                              }
                              catch 
                              { 
                                  atrVal1 = -1; 
                                  isAtrValid1 = false;
                              }
                          }
                          
                          if (atrVal1 > 1000 || atrVal1 <= 0) { atrVal1 = -1; isAtrValid1 = false; }

                          double atrOffset1 = isAtrValid1 ? atrVal1 * TradeLabelDistanceATR : TickSize * 30;


                          
                          string icon = signal.IsLong ? "▲" : "▼";

                          // 1. ENTRY ICON (Always visible) + LABEL (Controlled by ShowSignalText)
                          // Draw at High/Low of signal bar +/- Offset (visually anchored to candle extremes)
                          double entryYDir = signal.IsLong ? -1.0 : 1.0; // Below for Long, Above for Short
                          double entryYBase = signal.IsLong ? Low.GetValueAt(signal.SignalBarIdx) : High.GetValueAt(signal.SignalBarIdx);
                          double entryY = entryYBase + (atrOffset1 * entryYDir);

                          // v2.2.6: Draw Entry Icon (Triangle) - Always visible, independent of ShowSignalText
                          // v2.2.7: Use TrendTradeColor for trend trades
                          Brush entryBrush = signal.IsTrendTrade
                              ? TrendTradeColor
                              : (signal.IsLong ? Brushes.LimeGreen : Brushes.Red);
                          if (signal.IsLong)
                              Draw.TriangleUp(this, "Tag_EntryIcon_" + signal.SignalBarIdx, false, CurrentBar - signal.SignalBarIdx, entryY, entryBrush);
                          else
                              Draw.TriangleDown(this, "Tag_EntryIcon_" + signal.SignalBarIdx, false, CurrentBar - signal.SignalBarIdx, entryY, entryBrush);

                          // v2.2.6: Draw SL Level Line - From SIGNAL bar (entry) to exit/current bar
                          // This ensures visual consistency: line starts where trade starts
                          // SL is at anchor price +/- offset (1 tick above High for Short, 1 tick below Low for Long)
                          int slEndBar = (exitBar1 != -1 || exitBar2 != -1) ? Math.Max(exitBar1, exitBar2) : CurrentBar;
                          int slEndBarsAgo = CurrentBar - slEndBar;

                          // If trade hit SL (Q1 or Q2 loss), draw SL line to actual exit price for consistency
                          // Otherwise draw at original slPrice level
                          double slLinePrice = slPrice; // Default: original SL

                          // Check if trade exited at a different SL price (trailing SL scenario)
                          bool q1HitSL = (!win1 && exitBar1 != -1);
                          bool q2HitSL = (qty2 > 0 && !win2 && exitBar2 != -1);

                          if (q2HitSL && Math.Abs(exitPrice2 - slPrice) > TickSize)
                          {
                              // Q2 exited at trailing SL - draw line at trailing SL level
                              slLinePrice = exitPrice2;

                              // Also draw the original SL line (dimmer) for reference
                              Draw.Line(this, "Tag_SLLineOrig_" + signal.SignalBarIdx, false, signalBarsAgo, slPrice, slEndBarsAgo, slPrice,
                                  Brushes.DimGray, DashStyleHelper.Dot, 1);
                          }
                          else if (q1HitSL && Math.Abs(exitPrice1 - slPrice) > TickSize)
                          {
                              // Q1 exited at different price (shouldn't happen normally, but safety check)
                              slLinePrice = exitPrice1;
                          }

                          // Draw main SL level line
                          Draw.Line(this, "Tag_SLLine_" + signal.SignalBarIdx, false, signalBarsAgo, slLinePrice, slEndBarsAgo, slLinePrice,
                              LossTradeColor, DashStyleHelper.Dash, 1);

                          // Text Label (Only if ShowSignalText)
                          if (ShowSignalText)
                          {
                              string entryText = string.Format("Entry\nQty: {0}", signal.Quantity);

                              // Force White Text for max visibility, keeping background box
                              Draw.Text(this, "Tag_Entry_" + signal.SignalBarIdx, false, entryText,
                                  CurrentBar - signal.SignalBarIdx, entryY + (atrOffset1 * entryYDir * 0.5), 0,
                                  Brushes.White, new SimpleFont("Arial", LabelFontSize), TextAlignment.Center, Brushes.Transparent, LabelBackgroundColor, 60);
                          }


                          // 2. TP1 VISUALIZATION
                          if (win1 && exitBar1 != -1)
                          {
                              double yDir1 = (signal.IsLong) ? 1.0 : -1.0;
                              double lblY = exitPrice1 + (atrOffset1 * yDir1);

                              // v2.2.6: Draw TP1 Icon (Diamond) - Always visible
                              Draw.Diamond(this, "Tag_TP1Icon_" + signal.SignalBarIdx, false, CurrentBar - exitBar1, exitPrice1, WinTradeColor);

                              // Text only if ShowSignalText
                              if (ShowSignalText)
                              {
                                  double distance = Math.Abs(exitPrice1 - entryPrice);
                                  double risk = Math.Abs(entryPrice - signal.SL);
                                  double rr1 = (risk > 0) ? (distance / risk) : 0;

                                  string labelText = string.Format("TP1\n${0:F0}\nR: {1:F1}", pnl1, rr1);

                                  Draw.Text(this, "Tag_TP1_" + signal.SignalBarIdx, false, labelText,
                                      CurrentBar - exitBar1, lblY, 0,
                                      WinTradeColor, new SimpleFont("Arial", LabelFontSize) { Bold = true }, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                              }
                          }
                          else if (exitBar1 == -1) // Active Trade TP1
                          {
                              // Draw Projected TP1 (Dynamic)
                              double currentTp1 = 0;
                              for (int k = 0; k < 50; k++)
                              {
                                  if (signal.IsLong) { if (Values[0].IsValidDataPointAt(CurrentBar - k)) { currentTp1 = Values[0][k]; break; } }
                                  else { if (Values[1].IsValidDataPointAt(CurrentBar - k)) { currentTp1 = Values[1][k]; break; } }
                              }

                              if (currentTp1 > 0)
                              {
                                   // v2.2.6: Draw TP1 Active Icon
                                   Draw.Diamond(this, "Tag_TP1Icon_Active_" + signal.SignalBarIdx, false, 0, currentTp1, Brushes.Yellow);

                                   if (ShowSignalText)
                                   {
                                       double yDir1 = (signal.IsLong) ? 1.0 : -1.0;
                                       double lblY = currentTp1 + (atrOffset1 * yDir1);

                                       Draw.Text(this, "Tag_TP1_Active_" + signal.SignalBarIdx, false, "TP1 (Active)",
                                          0, lblY, 0,
                                          Brushes.Yellow, new SimpleFont("Arial", LabelFontSize), TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                                   }
                              }
                          }
                          
                          // 3. TP2 VISUALIZATION
                          // STRICT: Only if contracts are assigned to TP2
                          bool showTp2 = (qty2 > 0);

                          if (showTp2)
                          {
                              // Only draw TP2 if it was a WIN
                              if (win2 && exitBar2 != -1)
                              {
                                  // v2.2.6: Draw TP2 Icon (Diamond) - Always visible
                                  Draw.Diamond(this, "Tag_TP2Icon_" + signal.SignalBarIdx, false, CurrentBar - exitBar2, exitPrice2, WinTradeColor);

                                  // Text only if ShowSignalText
                                  if (ShowSignalText)
                                  {
                                      int refBar = exitBar2;

                                      // Recalculate ATR for TP2 bar if different
                                      int barsAgo2 = CurrentBar - refBar;
                                      double atrVal2 = (atr != null && barsAgo2 >= 0 && barsAgo2 < CurrentBar) ? atr[barsAgo2] : atrVal1;
                                      if (atrVal2 <= 0) atrVal2 = atrVal1;

                                      double atrOffset2 = atrVal2 * TradeLabelDistanceATR;

                                      double yDir2 = (signal.IsLong) ? 1.0 : -1.0;

                                      // Check overlap with one another logic... (Simplified)
                                      double lblY2 = exitPrice2 + (atrOffset2 * yDir2);

                                      // Overlap check with TP1
                                      if (win1 && exitBar1 == exitBar2 && Math.Abs(exitPrice1 - exitPrice2) < TickSize * 5)
                                      {
                                           lblY2 += (atrOffset2 * yDir2 * 0.8);
                                      }

                                      double distance2 = Math.Abs(exitPrice2 - entryPrice);
                                      double risk2 = Math.Abs(entryPrice - signal.SL);
                                      double rr2 = (risk2 > 0) ? (distance2 / risk2) : 0;

                                      string labelText2 = string.Format("TP2\n${0:F0}\nR: {1:F1}", pnl2, rr2);

                                      Draw.Text(this, "Tag_TP2_" + signal.SignalBarIdx, false, labelText2,
                                          CurrentBar - exitBar2, lblY2, 0,
                                          WinTradeColor, new SimpleFont("Arial", LabelFontSize) { Bold = true }, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                                  }
                              }
                              else if (exitBar2 == -1) // Active TP2
                              {
                                      // v2.2.6: Draw TP2 Active Icon
                                      Draw.Diamond(this, "Tag_TP2Icon_Active_" + signal.SignalBarIdx, false, 0, signal.TP2, Brushes.Yellow);

                                      if (ShowSignalText)
                                      {
                                          double yDir2 = (signal.IsLong) ? 1.0 : -1.0;
                                          double lblY2 = signal.TP2 + (atrOffset1 * yDir2);
                                          Draw.Text(this, "Tag_TP2_Active_" + signal.SignalBarIdx, false, "TP2 (Active)",
                                              0, lblY2, 0,
                                              Brushes.Yellow, new SimpleFont("Arial", LabelFontSize), TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                                      }
                              }
                          }
                          
                          // 4. FAILSAFE SL CHECK for OPEN TRADES
                          // If simulation didn't catch the hit but price is currently beyond SL
                          bool failsafeSL = false;
                          if (exitBar2 == -1 && qty2 > 0)
                          {
                              double currentPrice = Close[0];
                              if (signal.IsLong && currentPrice <= slPrice + 1e-9) failsafeSL = true;
                              if (!signal.IsLong && currentPrice >= slPrice - 1e-9) failsafeSL = true;
                          }

                          // SL Condition: Either caught by simulation OR open failsafe
                          bool slCondition = (!win1 && exitBar1 != -1) || (qty2 > 0 && !win2 && exitBar2 != -1) || failsafeSL;
                          
                          if (slCondition)
                          {
                              // Detectar dónde se cerró por SL
                              // Priority: If TP2 lost, draw at TP2 exit. If TP1 lost, draw at TP1 exit.
                              int slBar = (!win2 && qty2 > 0 && exitBar2 != -1) ? exitBar2 :
                                          (failsafeSL ? 0 : exitBar1); // Use 0 offset for current? CurrentBar - slBar = CurrentBar - (CurrentBar) = 0

                              // If failsafe is true, override slBar to CurrentBar
                              if (failsafeSL) slBar = CurrentBar;

                              // Use execution price logic
                              double executionPrice = (!win2 && qty2 > 0 && exitBar2 != -1) ? exitPrice2 :
                                                      (failsafeSL ? Close[0] : exitPrice1);

                              if (slBar != -1)
                              {
                                  // v2.2.6: Draw SL Icon (Square) - Always visible
                                  Draw.Square(this, "Tag_SLIcon_" + signal.SignalBarIdx, false, CurrentBar - slBar, executionPrice, LossTradeColor);

                                  // Text only if ShowSignalText
                                  if (ShowSignalText)
                                  {
                                      double yDirSL = (signal.IsLong) ? -1.0 : 1.0; // Draw below for Long Loss
                                      // Draw At EXIT Price (corrected)
                                      double slY = executionPrice + (atrOffset1 * yDirSL);

                                      // OVERLAP CHECK: If SL label position is too close to Entry Label position (BreakEven)
                                      // Shift it further out to avoid overlap with "Entry" label
                                      if (Math.Abs(slY - entryY) < (TickSize * 20)) // ~20 ticks threshold
                                      {
                                          // Push further out (multiply offset by 2.2) to clear the Entry label
                                          slY = executionPrice + (atrOffset1 * yDirSL * 2.2);
                                      }

                                      // Recalculate PnL for failsafe
                                      if (failsafeSL)
                                      {
                                          double pnlFS = (signal.IsLong ? executionPrice - entryPrice : entryPrice - executionPrice) * qty2 * pointValue;
                                          totalPnl += pnlFS;
                                      }

                                      string slText = string.Format("SL\n${0:F0}", totalPnl);

                                      Draw.Text(this, "Tag_SL_" + signal.SignalBarIdx, false, slText,
                                          CurrentBar - slBar, slY, 0,
                                          LossTradeColor, new SimpleFont("Arial", LabelFontSize) { Bold = true }, TextAlignment.Center, Brushes.Transparent, LabelBackgroundColor, 60);
                                  }
                              }
                          }
                     }
                }
            }
            catch (Exception ex)
            {
                Print("[VISUAL SIGNAL] ERROR: " + ex.Message);
                LogToFile("VISUAL SIGNAL ERROR: " + ex.Message + "\n" + ex.StackTrace, "ERROR");
            }
        }
        
        // Helper to return the bar index where the trade closed using DYNAMIC TP (Sticky)
        private int CheckIfTpWasHitReturnBar(int signalBarIdx, double slPrice, bool isLong, out bool tpHit, out bool slHit, out double exitPrice)
        {
            tpHit = false;
            slHit = false;
            exitPrice = 0;
            
            try
            {
                int startBarIdx = signalBarIdx + 1;
                int endBarIdx = Math.Min(CurrentBar, Bars.Count - 1);
                
                for (int i = startBarIdx; i <= endBarIdx; i++)
                {
                    double high = High.GetValueAt(i);
                    double low = Low.GetValueAt(i);
                    
                    // Determine Dynamic TP Price for this bar
                    double dynamicTp = 0;
                    if (isLong)
                    {
                        // Long Target: High VWAP (Values[0])
                        if (hasHighVWAP && Values[0].IsValidDataPointAt(i))
                            dynamicTp = Values[0].GetValueAt(i);
                        else
                             dynamicTp = 999999; // Unreachable
                    }
                    else
                    {
                         // Short Target: Low VWAP (Values[1])
                         if (hasLowVWAP && Values[1].IsValidDataPointAt(i))
                            dynamicTp = Values[1].GetValueAt(i);
                         else
                             dynamicTp = 0; // Unreachable
                    }

                    if (isLong)
                    {
                        // Check TP (High >= DynamicTP)
                        if (high >= dynamicTp) 
                        { 
                            tpHit = true; 
                            exitPrice = dynamicTp; // Exit exactly at VWAP
                            return i; 
                        }
                        // Check SL (Low <= FixedSL)
                        if (low <= slPrice) 
                        { 
                            slHit = true; 
                            exitPrice = slPrice; 
                            return i; 
                        }
                    }
                    else
                    {
                        // Check TP (Low <= DynamicTP)
                        if (low <= dynamicTp) 
                        { 
                            tpHit = true; 
                            exitPrice = dynamicTp; // Exit exactly at VWAP
                            return i; 
                        }
                        // Check SL (High >= FixedSL)
                        if (high >= slPrice) 
                        { 
                            slHit = true; 
                            exitPrice = slPrice; 
                            return i; 
                        }
                    }
                }
                return -1; // Open
            }
            catch { return -1; }
        }

        /// <summary>
        /// Calculates position size for signal visualization (uses Trading.cs logic if available)
        /// </summary>
        private int CalculateSignalPositionSize(double entryPrice, double stopLossPrice)
        {
            // If UseRiskBasedSizing is disabled, return fixed quantity
            if (!UseRiskBasedSizing)
                return TradeQuantity;

            try
            {
                // Use user-defined simulated balance
                double simulatedBalance = SimulatedBalance; 

                // Calculate risk in dollars using exposed property
                double riskInDollars = simulatedBalance * (RiskPercentage / 100.0);

                // Calculate SL distance
                double distanceInPrice = Math.Abs(entryPrice - stopLossPrice);
                double distanceInTicks = distanceInPrice / TickSize;

                // Calculate dollar value
                double pointValue = Instrument.MasterInstrument.PointValue;
                double dollarValuePerTick = pointValue * TickSize;
                double distanceInDollars = distanceInTicks * dollarValuePerTick;

                if (distanceInDollars <= 0)
                    return 1;

                // Calculate quantity
                double calculatedQty = riskInDollars / distanceInDollars;
                return Math.Max(1, (int)Math.Floor(calculatedQty));
            }
            catch
            {
                return 1; // Fallback
            }
        }

        /// <summary>
        /// Checks if TP target was hit before SL by looking at future bars
        /// </summary>
        private bool CheckIfTpWasHit(int anchorBarIdx, double slPrice, double tpPrice, bool isLong)
        {
            try
            {
                // Look at bars after the anchor bar
                int startBarIdx = anchorBarIdx + 1;
                int endBarIdx = Math.Min(CurrentBar, Bars.Count - 1);

                string msg1 = string.Format("TP_CHECK: {0} Signal | Anchor:{1} | Searching from bar {2} to {3} (CurrentBar={4})",
                    isLong ? "LONG" : "SHORT", anchorBarIdx, startBarIdx, endBarIdx, CurrentBar);
                string msg2 = string.Format("TP_CHECK: SL:{0:F2} | TP:{1:F2}", slPrice, tpPrice);

                if (ShowDebugLogs)
                {
                    Print("[" + msg1 + "]");
                    Print("[" + msg2 + "]");
                }
                LogToFile(msg1, "TP_CHECK");
                LogToFile(msg2, "TP_CHECK");

                for (int i = startBarIdx; i <= endBarIdx; i++)
                {
                    double high = High.GetValueAt(i);
                    double low = Low.GetValueAt(i);

                    if (isLong)
                    {
                        // For LONG: TP is above, SL is below
                        // Check if TP was hit first
                        if (high >= tpPrice)
                        {
                            string msgHit = string.Format("TP_CHECK: ✓ TP HIT at bar {0} | High:{1:F2} >= TP:{2:F2}", i, high, tpPrice);
                            if (ShowDebugLogs)
                                Print("[" + msgHit + "]");
                            LogToFile(msgHit, "TP_HIT");
                            return true;
                        }

                        // Check if SL was hit first
                        if (low <= slPrice)
                        {
                            string msgSL = string.Format("TP_CHECK: ✗ SL HIT FIRST at bar {0} | Low:{1:F2} <= SL:{2:F2}", i, low, slPrice);
                            if (ShowDebugLogs)
                                Print("[" + msgSL + "]");
                            LogToFile(msgSL, "SL_HIT");
                            return false;
                        }
                    }
                    else
                    {
                        // For SHORT: TP is below, SL is above
                        // Check if TP was hit first
                        if (low <= tpPrice)
                        {
                            string msgHit = string.Format("TP_CHECK: ✓ TP HIT at bar {0} | Low:{1:F2} <= TP:{2:F2}", i, low, tpPrice);
                            if (ShowDebugLogs)
                                Print("[" + msgHit + "]");
                            LogToFile(msgHit, "TP_HIT");
                            return true;
                        }

                        // Check if SL was hit first
                        if (high >= slPrice)
                        {
                            string msgSL = string.Format("TP_CHECK: ✗ SL HIT FIRST at bar {0} | High:{1:F2} >= SL:{2:F2}", i, high, slPrice);
                            if (ShowDebugLogs)
                                Print("[" + msgSL + "]");
                            LogToFile(msgSL, "SL_HIT");
                            return false;
                        }
                    }
                }

                // If we reached the end without hitting either level, TP was not hit
                string msgNoHit = string.Format("TP_CHECK: Neither TP nor SL hit yet. Checked {0} bars.", endBarIdx - startBarIdx + 1);
                if (ShowDebugLogs)
                    Print("[" + msgNoHit + "]");
                LogToFile(msgNoHit, "TP_CHECK");

                return false;
            }
            catch (Exception ex)
            {
                string errMsg = "CheckIfTpWasHit ERROR: " + ex.Message;
                Print("[" + errMsg + "]");
                LogToFile(errMsg, "ERROR");
                return false;
            }
        }

        #endregion
    }
}
