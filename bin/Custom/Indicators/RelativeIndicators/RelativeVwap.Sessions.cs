#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.Core;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
    public partial class RelativeVwap
    {
        // v3.0.2: Independent period divider tracking (works in ALL personality modes)
        private DateTime _lastDividerPeriodStart = DateTime.MinValue;

        /// <summary>
        /// Track period boundaries for divider visualization.
        /// - Intraday: Daily divider at ETH session start time (DailyDividerTime property)
        /// - Period modes: Divider at each period boundary (Weekly/Monthly/Quarterly/Yearly)
        /// </summary>
        private void TrackPeriodDividers(DateTime currentTime)
        {
            if (periodDividerBars == null || !ShowPeriodDividers) return;

            if (Personality == PersonalityMode.Intraday)
            {
                // Daily divider at ETH start time (e.g., 19:00)
                TimeSpan ethTime;
                if (!TimeSpan.TryParse(DailyDividerTime, out ethTime)) return;

                // Calculate the "session day": if before ETH start, we're still in previous session day
                DateTime sessionDay = currentTime.Date;
                if (currentTime.TimeOfDay < ethTime)
                    sessionDay = sessionDay.AddDays(-1);

                // Add the ETH start time to get the exact boundary
                DateTime sessionBoundary = sessionDay.Add(ethTime);

                if (sessionBoundary != _lastDividerPeriodStart)
                {
                    _lastDividerPeriodStart = sessionBoundary;
                    if (!periodDividerBars.Contains(CurrentBar))
                        periodDividerBars.Add(CurrentBar);
                }
            }
            else
            {
                // Period mode: use period boundaries (Weekly/Monthly/Quarterly/Yearly)
                DateTime periodStart = GetPeriodStartDate(Personality, currentTime, WeekStartDay);

                if (periodStart != _lastDividerPeriodStart)
                {
                    _lastDividerPeriodStart = periodStart;
                    if (!periodDividerBars.Contains(CurrentBar))
                        periodDividerBars.Add(CurrentBar);
                }
            }
        }

        #region Session Methods

        // v3.1.1 perf: O(1) math instead of day-by-day loop
        private int GetBusinessDays(DateTime start, DateTime end)
        {
            if (start.Date >= end.Date) return 0;
            int totalDays = (int)(end.Date - start.Date).TotalDays;
            int fullWeeks = totalDays / 7;
            int remainingDays = totalDays % 7;
            int bizDays = fullWeeks * 5;
            int dow = (int)start.Date.DayOfWeek;
            for (int i = 1; i <= remainingDays; i++)
            {
                int d = (dow + i) % 7;
                if (d != 0 && d != 6) bizDays++; // 0=Sunday, 6=Saturday
            }
            return bizDays;
        }

        private string GetSignalCode(SessionLevelInfo session, string levelType)
        {
            if (session == null) return "";

            string r = "X";
            if (session.Name.StartsWith("Asia")) r = "A";
            else if (session.Name.StartsWith("Europe")) r = "E";
            else if (session.Name.StartsWith("USA")) r = "U";
            else if (session.Name.StartsWith("Week")) r = "W";   // v3.0.0: Weekly personality
            else if (session.Name.StartsWith("Month")) r = "M";  // v3.0.0: Monthly personality
            else if (session.Name.StartsWith("Q")) r = "Q";      // v3.0.0: Quarterly personality
            else if (session.Name.StartsWith("Year")) r = "Y";   // v3.0.0: Yearly personality

            DateTime currentTradingDay = Time[0].Date;
            DateTime sessionTradingDay = session.SessionDate.Date;

            if (sessionIterator != null)
            {
                try { currentTradingDay = sessionIterator.GetTradingDay(Time[0]); } catch {}

                if (session.StartBarIdx >= 0 && session.StartBarIdx < Bars.Count)
                {
                    try { sessionTradingDay = sessionIterator.GetTradingDay(Bars.GetTime(session.StartBarIdx)); } catch {}
                }
            }

            int days = GetBusinessDays(sessionTradingDay, currentTradingDay);

            if (ShowDebugLogs && days == 0 && (currentTradingDay - sessionTradingDay).TotalDays > 1)
            {
                Print(string.Format("GetSignalCode DEBUG: Days=0 but diff>1? Curr={0} Sess={1}", currentTradingDay, sessionTradingDay));
            }

            return string.Format("{0}{1}{2}", r, levelType, days);
        }

        private void CloseGhostLines(List<SessionLevelInfo> sessions, int closeIdx)
        {
            if (sessions == null) return;
            foreach (var s in sessions)
            {
                if (s.HighBrokenBarIdx != -1 && s.HighGhostEndIdx == -1 && s.HighBrokenBarIdx <= closeIdx)
                    s.HighGhostEndIdx = closeIdx;

                if (s.LowBrokenBarIdx != -1 && s.LowGhostEndIdx == -1 && s.LowBrokenBarIdx <= closeIdx)
                    s.LowGhostEndIdx = closeIdx;
            }
        }

        private void UpdateSession(List<SessionLevelInfo> sessions, string name, DateTime time, string startStr, string endStr, bool isEnabled)
        {
            if (!isEnabled || sessions == null) return;

            TimeSpan startTime = GetTimeByZone(startStr);
            TimeSpan endTime = GetTimeByZone(endStr);
            TimeSpan currentTime = time.TimeOfDay;

            bool isInside = false;

            if (startTime == endTime)
                isInside = false;
            else if (startTime < endTime)
                isInside = currentTime >= startTime && currentTime < endTime;
            else
                isInside = currentTime >= startTime || currentTime < endTime;

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
                        HighBarIdx = CurrentBar, // Init
                        LowBarIdx = CurrentBar,  // Init
                        SessionDate = sessionDate
                    };
                    sessions.Add(currentSession);
                    if (ShowDebugLogs)
                        Print(string.Format("RelativeVwap: New Session Added -> {0} at Date {1} (StartBar:{2} H:{3} L:{4})", name, sessionDate, CurrentBar, High[0], Low[0]));
                }
                else
                {
                    if (High[0] > currentSession.High)
                    {
                        currentSession.High = High[0];
                        currentSession.HighBarIdx = CurrentBar; // Update Idx
                    }
                    if (Low[0] < currentSession.Low)
                    {
                        currentSession.Low = Low[0];
                        currentSession.LowBarIdx = CurrentBar; // Update Idx
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

        // v3.0.0: Period Detection Functions for Personality Modes

        private bool IsNewWeek(DateTime current, DateTime previous, DayOfWeek weekStart)
        {
            if (previous == DateTime.MinValue) return true;

            // Use Calendar to get week numbers (handles year rollover correctly)
            System.Globalization.Calendar calendar = System.Globalization.CultureInfo.CurrentCulture.Calendar;
            System.Globalization.CalendarWeekRule weekRule = System.Globalization.CalendarWeekRule.FirstFourDayWeek;

            int currentWeek = calendar.GetWeekOfYear(current, weekRule, weekStart);
            int previousWeek = calendar.GetWeekOfYear(previous, weekRule, weekStart);

            // Different weeks or different years
            return (currentWeek != previousWeek) || (current.Year != previous.Year);
        }

        private bool IsNewMonth(DateTime current, DateTime previous)
        {
            if (previous == DateTime.MinValue) return true;
            return current.Month != previous.Month || current.Year != previous.Year;
        }

        private int GetQuarter(DateTime date)
        {
            return (date.Month - 1) / 3 + 1;  // Q1=1-3, Q2=4-6, Q3=7-9, Q4=10-12
        }

        private bool IsNewQuarter(DateTime current, DateTime previous)
        {
            if (previous == DateTime.MinValue) return true;
            return GetQuarter(current) != GetQuarter(previous) || current.Year != previous.Year;
        }

        private bool IsNewYear(DateTime current, DateTime previous)
        {
            if (previous == DateTime.MinValue) return true;
            return current.Year != previous.Year;
        }

        private DateTime GetPeriodStartDate(PersonalityMode mode, DateTime current, DayOfWeek weekStart)
        {
            // v3.0.1: For 24-hour futures markets, periods should end at USA session close (5 PM ET)
            // not at midnight. This ensures weekly/monthly periods align with trading sessions.
            TimeSpan usaCloseTime = new TimeSpan(17, 0, 0); // 5:00 PM ET
            
            switch (mode)
            {
                case PersonalityMode.Weekly:
                    // Adjust current time to account for USA close
                    // If current time is before 5 PM, we're still in the previous trading day for period calculation
                    DateTime adjustedCurrent = current;
                    if (current.TimeOfDay < usaCloseTime)
                    {
                        adjustedCurrent = current.AddDays(-1);
                    }
                    
                    // Get the start of the week (Monday or Sunday)
                    int daysToSubtract = ((int)adjustedCurrent.DayOfWeek - (int)weekStart + 7) % 7;
                    DateTime weekStart_Date = adjustedCurrent.Date.AddDays(-daysToSubtract);
                    
                    // Add USA close time to the week start date
                    return weekStart_Date.Add(usaCloseTime);

                case PersonalityMode.Monthly:
                    // Month starts at USA close on the first day
                    return new DateTime(current.Year, current.Month, 1).Add(usaCloseTime);

                case PersonalityMode.Quarterly:
                    int quarter = GetQuarter(current);
                    int quarterStartMonth = (quarter - 1) * 3 + 1;
                    return new DateTime(current.Year, quarterStartMonth, 1).Add(usaCloseTime);

                case PersonalityMode.Yearly:
                    return new DateTime(current.Year, 1, 1).Add(usaCloseTime);

                default:
                    return current.Date;
            }
        }

        private string GetPeriodName(PersonalityMode mode, DateTime periodStart)
        {
            switch (mode)
            {
                case PersonalityMode.Weekly:
                    return "Week " + periodStart.ToString("yyyy-MM-dd");

                case PersonalityMode.Monthly:
                    return "Month " + periodStart.ToString("yyyy-MM");

                case PersonalityMode.Quarterly:
                    return "Q" + GetQuarter(periodStart) + " " + periodStart.Year;

                case PersonalityMode.Yearly:
                    return "Year " + periodStart.Year;

                default:
                    return "Unknown";
            }
        }

        private void UpdatePeriodSession(
            List<SessionLevelInfo> sessions,
            PersonalityMode mode,
            DateTime currentTime,
            DayOfWeek weekStart)
        {
            if (sessions == null) return;

            // 1. Calculate period start date
            DateTime periodStart = GetPeriodStartDate(mode, currentTime, weekStart);
            string name = GetPeriodName(mode, periodStart);

            // 2. Get last session
            SessionLevelInfo currentSession = sessions.Count > 0 ? sessions.Last() : null;

            // 3. Check if we need to create a new session
            if (currentSession == null || currentSession.SessionDate != periodStart)
            {
                // Mark previous session as inactive and calculate its EndTime
                if (currentSession != null)
                {
                    currentSession.IsActive = false;
                    
                    // v3.0.1: Calculate period end time based on the new period's start
                    // Period ends just before the next period starts
                    currentSession.EndTime = periodStart.AddSeconds(-1);
                    
                    if (ShowDebugLogs)
                        Print(string.Format("[PERIOD] Closed {0} session: {1} (End: {2})",
                            mode, currentSession.Name, currentSession.EndTime));
                }

                // Create new session
                currentSession = new SessionLevelInfo
                {
                    Name = name,
                    IsActive = true,
                    StartBarIdx = CurrentBar,
                    High = High[0],
                    Low = Low[0],
                    HighBarIdx = CurrentBar,
                    LowBarIdx = CurrentBar,
                    SessionDate = periodStart
                };
                sessions.Add(currentSession);

                // v3.0.2: Period divider tracking moved to TrackPeriodDividers() (independent of personality)

                if (ShowDebugLogs)
                    Print(string.Format("[PERIOD] New {0} session: {1} at bar {2} (H:{3} L:{4})",
                        mode, name, CurrentBar, High[0], Low[0]));
            }
            else
            {
                // Update existing session high/low
                if (High[0] > currentSession.High)
                {
                    currentSession.High = High[0];
                    currentSession.HighBarIdx = CurrentBar;

                    if (ShowDebugLogs)
                        Print(string.Format("[PERIOD] {0} new HIGH: {1} at bar {2}", name, High[0], CurrentBar));
                }
                if (Low[0] < currentSession.Low)
                {
                    currentSession.Low = Low[0];
                    currentSession.LowBarIdx = CurrentBar;

                    if (ShowDebugLogs)
                        Print(string.Format("[PERIOD] {0} new LOW: {1} at bar {2}", name, Low[0], CurrentBar));
                }
            }
        }

        // v3.0.0: Historical Filtering for Period Personalities

        private bool ShouldSkipHistoricalPeriod(SessionLevelInfo session, DateTime currentDate)
        {
            if (Personality == PersonalityMode.Intraday)
            {
                // Use existing MaxHistoryDays logic for intraday
                if (MaxHistoryDays <= 0) return false;
                int levelAge = GetBusinessDays(session.SessionDate, currentDate);
                return levelAge > MaxHistoryDays;
            }
            else if (Personality == PersonalityMode.Weekly)
            {
                int weeksDiff = GetPeriodCount(session.SessionDate, currentDate, PersonalityMode.Weekly);
                return weeksDiff > WeeklyHistoryWeeks;
            }
            else if (Personality == PersonalityMode.Monthly)
            {
                int monthsDiff = GetPeriodCount(session.SessionDate, currentDate, PersonalityMode.Monthly);
                return monthsDiff > MonthlyHistoryMonths;
            }
            else if (Personality == PersonalityMode.Quarterly)
            {
                int quartersDiff = GetPeriodCount(session.SessionDate, currentDate, PersonalityMode.Quarterly);
                return quartersDiff > QuarterlyHistoryQuarters;
            }
            else // Yearly
            {
                int yearsDiff = currentDate.Year - session.SessionDate.Year;
                return yearsDiff > YearlyHistoryYears;
            }
        }

        private int GetPeriodCount(DateTime start, DateTime end, PersonalityMode mode)
        {
            if (mode == PersonalityMode.Weekly)
            {
                return (int)((end - start).TotalDays / 7);
            }
            else if (mode == PersonalityMode.Monthly)
            {
                return ((end.Year - start.Year) * 12) + (end.Month - start.Month);
            }
            else if (mode == PersonalityMode.Quarterly)
            {
                int startQ = GetQuarter(start);
                int endQ = GetQuarter(end);
                return ((end.Year - start.Year) * 4) + (endQ - startQ);
            }
            return 0;
        }

        private void CheckTouches(List<SessionLevelInfo> sessions)
        {
            if (sessions == null) return;
            double high = High[0];
            double low = Low[0];
            DateTime today = Time[0].Date;

            // v3.0.2: Determine if historical filter should skip SIGNAL generation (not break detection)
            bool isHistoryFiltered;

            foreach (var session in sessions)
            {
                if (ShowDebugLogs && (Math.Abs(low - session.Low) <= 10 * TickSize || Math.Abs(high - session.High) <= 10 * TickSize))
                {
                    Print(string.Format("Check: {0} {1} Active:{2} H:{3}({4}) L:{5}({6}) Now:{7}/{8}",
                        session.Name, session.SessionDate.ToShortDateString(), session.IsActive,
                        session.High, session.HighBrokenBarIdx, session.Low, session.LowBrokenBarIdx, high, low));
                }

                // Sanity Check
                if (session.High <= 0 || session.Low <= 0) continue;

                // v3.0.2: Always detect breaks for visual mitigation (regardless of history filter)
                // Check High Break (Resistance)
                if (session.HighBrokenBarIdx == -1 && high > session.High)
                {
                    session.HighBrokenBarIdx = CurrentBar;
                }

                // Check Low Break (Support)
                if (session.LowBrokenBarIdx == -1 && low < session.Low)
                {
                    session.LowBrokenBarIdx = CurrentBar;
                }

                // v3.0.2: Historical Filter - Skip SIGNAL generation for old levels (not break detection)
                isHistoryFiltered = ShouldSkipHistoricalPeriod(session, today);
                if (isHistoryFiltered)
                    continue;

                // V_SYNC: Generate trading signals only for non-filtered levels
                {
                    // Check High Break for signals (only if just broken on this bar)
                    if (session.HighBrokenBarIdx == CurrentBar)
                    {
                        if (ShowDebugLogs)
                            Print(string.Format("RelativeVwap DEBUG: HIGH BREAK! Name={0} Bar={1} High={2} SessionHigh={3} TradesCount={4}",
                                session.Name, CurrentBar, high, session.High, (activeTrades != null ? activeTrades.Count : -1)));

                        // v2.1.0: Add to candidates list for prioritization
                        _highBreaks.Add(session);
                    }

                    // Check Low Break for signals (only if just broken on this bar)
                    if (session.LowBrokenBarIdx == CurrentBar)
                    {
                         if (ShowDebugLogs)
                             Print(string.Format("RelativeVwap DEBUG: LOW BREAK! Name={0} Bar={1} Low={2} SessionLow={3} TradesCount={4}",
                                 session.Name, CurrentBar, low, session.Low, (activeTrades != null ? activeTrades.Count : -1)));

                         // v2.1.0: Add to candidates list for prioritization
                         _lowBreaks.Add(session);
                    }
                }
            }
        }

        // v2.1.0: Validates all breaks in the current bar and selects the "best" one (most extreme)
        private void ProcessBestBreaks()
        {
            double high = High[0];
            double low = Low[0];

            // --- PROCESS BEST HIGH BREAK ---
            SessionLevelInfo bestHighSession = null;
            if (_highBreaks.Count > 0)
            {
                // Prioritize: 
                // 1. Highest Price Level (most extreme)
                // 2. If tie (not possible usually unless identical), take first found
                double maxLevel = double.MinValue;
                foreach (var s in _highBreaks)
                {
                    if (s.High > maxLevel)
                    {
                        maxLevel = s.High;
                        bestHighSession = s;
                    }
                }
            }

            if (bestHighSession != null)
            {
                var session = bestHighSession;
                
                // --- EXECUTE ORIGINAL LOGIC FOR THE WINNER ---

                // If this is the FIRST time we detect a High break for this VWAP session
                if (!highHasTakenRelevant) highFirstBreakIdx = CurrentBar;

                highHasTakenRelevant = true;
                highSignalFired = false; // UNLOCK SIGNAL (New Level Hit)
                lastUnlockedHighSession = session; // FIX: Store session for TP2 Logic
                // v1.0.48: Reset SAME SIDE sequence (HIGH level break -> SHORT signals will use this VWAP)
                highAnchorSequence = 0;
                lastHighSeqResetBar = CurrentBar; // Track this bar to prevent multiple resets
                
                // v1.0.45: Reset Liquidity Grab sequence and state
                highLiqGrabSequence = 1;
                highLiqGrabLocked = false;
                highLiqGrabBarIdx = -1;

                highDetached = false; // SYNC: Reset Detachment on Break
                
                // V_LOGIC: Strategy Filters (High Break = Long?)
                
                // 1. Trade Direction Filter
                if (TradeDirection != TradeDirectionMode.ShortOnly) 
                {
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
                    }

                    LastSignalCode = code;

                    // v1.0.8: Use configurable SignalColor instead of session colors
                    Brush sigBrush = SignalColor;

                    // V_VISUAL: SIGNAL 1 - TAKE LEVEL (RESISTANCE) - v1.0.5: Synced with SessionLevels ATR-based positioning
                    double atrOffset = (atr != null && atr[0] > 0) ? atr[0] * LabelDistanceATR : TickSize * 10;

                    // v1.0.5: Position relative to candle High + offset
                    double triY = high + atrOffset;

                    // v1.0.50: Determine if this is an internal level (not day extreme)
                    highLiqGrabIsInternal = (session.High < currentDayHigh);

                    // v1.0.24: Track position for movable label
                    highLiqGrabBarIdx = CurrentBar;
                    highLiqGrabExtreme = high;
                    highLiqGrabSessionName = session.Name;
                    highLiqGrabLocked = false; // v1.0.45: New grab is unlocked (can move)

                    // v1.0.50: Create internal VWAP if this is an internal level (AND logic is enabled)
                    if (highLiqGrabIsInternal && EnableInternalLogic)
                    {
                        internalHighBarIdx = CurrentBar;
                        internalHighPrice = session.High;
                        internalHighExtreme = High[0]; // v2.0.0: Init extreme for re-anchoring
                        // Initialize with this bar's volume
                        double price = VwapMethod == VwapPriceMethod.Close ? Close[0] :
                                     VwapMethod == VwapPriceMethod.Typical ? (High[0] + Low[0] + Close[0]) / 3.0 :
                                     (High[0] + Low[0] + Close[0] + Open[0]) / 4.0;
                        double volume = Volume[0];
                        internalHighPV = price * volume;
                        internalHighVol = volume;
                        hasInternalHighVWAP = true;
                        _internalHighJustReset = true;  // v1.0.49: Skip accumulation this bar to avoid double-counting
                        
                        // v2.1.0: RESET Internal Signal 2 State for new grab
                        internalHighSignal2Fired = false;
                        lastSignaledInternalHighBar = -1;
                    }

                    // Triangle (if ShowSignal1)
                    if (ShowSignal1)
                    {
                        // v1.0.24: Use session-based tag (not CurrentBar) so we can move the label
                        Draw.TriangleDown(this, "TakeHigh_" + session.Name, true, 0, triY, sigBrush);

                        // Label (if ShowSignalText)
                        if (ShowSignalText)
                        {
                            // v1.0.49: 3 lines - add session name, HIGH/LOW, and internal marker
                            string internalMarker = highLiqGrabIsInternal ? " (i)" : "";
                            string labelCode = string.Format("Liquidity\nGrabbed {0:00}\n{1} High{2}", highLiqGrabSequence, session.Name, internalMarker);
                            SimpleFont font = new SimpleFont("Arial", LabelFontSize);
                            Draw.Text(this, "Sig1H_Txt_" + session.Name + "_" + highLiqGrabSequence, false, labelCode, 0, triY, LabelTextOffset, sigBrush, font, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                        }
                    }

                }
            }


            // --- PROCESS BEST LOW BREAK ---
            SessionLevelInfo bestLowSession = null;
            if (_lowBreaks.Count > 0)
            {
                // Prioritize: 
                // 1. Lowest Price Level (most extreme)
                double minLevel = double.MaxValue;
                foreach (var s in _lowBreaks)
                {
                    if (s.Low < minLevel)
                    {
                        minLevel = s.Low;
                        bestLowSession = s;
                    }
                }
            }

            if (bestLowSession != null)
            {
                var session = bestLowSession;
                
                // --- EXECUTE ORIGINAL LOGIC FOR THE WINNER ---

                if (!lowHasTakenRelevant) lowFirstBreakIdx = CurrentBar;

                lowHasTakenRelevant = true;
                lowSignalFired = false; // UNLOCK SIGNAL
                lastUnlockedLowSession = session; // FIX: Store session for TP2 Logic
                // v1.0.48: Reset SAME SIDE sequence (LOW level break -> LONG signals will use this VWAP)
                lowAnchorSequence = 0;
                lastLowSeqResetBar = CurrentBar; // Track this bar to prevent multiple resets
                
                // v1.0.45: Reset Liquidity Grab sequence and state
                lowLiqGrabSequence = 1;
                lowLiqGrabLocked = false;
                lowLiqGrabBarIdx = -1;
                
                lowDetached = false; // SYNC: Reset Detachment

                // V_LOGIC: Strategy Filters (Low Break = Short?)
                
                // 1. Trade Direction Filter
                if (TradeDirection != TradeDirectionMode.LongOnly) 
                {
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
                    }
                    
                    LastSignalCode = code;

                    // v1.0.8: Use configurable SignalColor instead of session colors
                    Brush sigBrush = SignalColor;

                    // V_VISUAL: SIGNAL 1 - TAKE LEVEL (SUPPORT) - v1.0.5: Synced with SessionLevels ATR-based positioning
                    double atrOffset = (atr != null && atr[0] > 0) ? atr[0] * LabelDistanceATR : TickSize * 10;

                    // v1.0.5: Position relative to candle Low + offset
                    double triY = low - atrOffset;

                    // v1.0.50: Determine if this is an internal level (not day extreme)
                    lowLiqGrabIsInternal = (session.Low > currentDayLow);

                    // v1.0.24: Track position for movable label
                    lowLiqGrabBarIdx = CurrentBar;
                    lowLiqGrabExtreme = low;
                    lowLiqGrabSessionName = session.Name;
                    lowLiqGrabLocked = false; // v1.0.45: New grab is unlocked (can move)

                    // v1.0.50: Create internal VWAP if this is an internal level (AND logic is enabled)
                    if (lowLiqGrabIsInternal && EnableInternalLogic)
                    {
                        internalLowBarIdx = CurrentBar;
                        internalLowPrice = session.Low;
                        internalLowExtreme = Low[0]; // v2.0.0: Init extreme for re-anchoring
                        // Initialize with this bar's volume
                        double price = VwapMethod == VwapPriceMethod.Close ? Close[0] :
                                     VwapMethod == VwapPriceMethod.Typical ? (High[0] + Low[0] + Close[0]) / 3.0 :
                                     (High[0] + Low[0] + Close[0] + Open[0]) / 4.0;
                        double volume = Volume[0];
                        internalLowPV = price * volume;
                        internalLowVol = volume;
                        hasInternalLowVWAP = true;
                        _internalLowJustReset = true;  // v1.0.49: Skip accumulation this bar to avoid double-counting
                        
                        // v2.1.0: RESET Internal Signal 2 State for new grab
                        internalLowSignal2Fired = false;
                        lastSignaledInternalLowBar = -1;
                    }

                    // Triangle (if ShowSignal1)
                    if (ShowSignal1)
                    {
                        // v1.0.24: Use session-based tag (not CurrentBar) so we can move the label
                        Draw.TriangleUp(this, "TakeLow_" + session.Name, true, 0, triY, sigBrush);

                        // Label (if ShowSignalText)
                        if (ShowSignalText)
                        {
                            // v1.0.49: 3 lines - add session name, HIGH/LOW, and internal marker
                            string internalMarker = lowLiqGrabIsInternal ? " (i)" : "";
                            string labelCode = string.Format("Liquidity\nGrabbed {0:00}\n{1} Low{2}", lowLiqGrabSequence, session.Name, internalMarker);
                            SimpleFont font = new SimpleFont("Arial", LabelFontSize);
                            Draw.Text(this, "Sig1L_Txt_" + session.Name + "_" + lowLiqGrabSequence, false, labelCode, 0, triY, -LabelTextOffset, sigBrush, font, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                        }
                    }

                }
            }
        }

        #endregion

        #region Time Zone Helpers
        
        private TimeSpan GetTimeByZone(string timeStr)
        {
            if (UseExchangeTime && CurrentBarDate == _lastCacheDate)
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

        // v3.0.4: Track US First Hour Opening Range
        private void TrackUSFirstHour(DateTime time)
        {
            TimeSpan usaStart = GetTimeByZone(USStartTime);
            TimeSpan usaEnd = GetTimeByZone(USEndTime);
            TimeSpan currentTime = time.TimeOfDay;

            // Check if we're inside the US session
            bool insideUS = (usaStart < usaEnd)
                ? (currentTime >= usaStart && currentTime < usaEnd)
                : (currentTime >= usaStart || currentTime < usaEnd);

            if (!insideUS)
            {
                // Outside US session — archive current first hour if complete, then reset
                if (_usFirstHourStartBarIdx >= 0 && _usFirstHourComplete)
                {
                    if (_usFirstHourDate != DateTime.MinValue && (_historicalFirstHours.Count == 0 || _historicalFirstHours[_historicalFirstHours.Count - 1].Date != _usFirstHourDate))
                    {
                        _historicalFirstHours.Add(new FirstHourRange
                        {
                            High = _usFirstHourHigh,
                            Low = _usFirstHourLow,
                            StartBarIdx = _usFirstHourStartBarIdx,
                            EndBarIdx = _usFirstHourEndBarIdx,
                            Date = _usFirstHourDate
                        });
                    }
                }
                _usFirstHourStartBarIdx = -1;
                _usFirstHourComplete = false;
                return;
            }

            // Inside US session
            TimeSpan firstHourEnd = usaStart.Add(TimeSpan.FromMinutes(USFirstHourMinutes));
            bool insideFirstHour = currentTime >= usaStart && currentTime < firstHourEnd;

            if (insideFirstHour)
            {
                if (_usFirstHourStartBarIdx < 0)
                {
                    // First bar of the first hour
                    _usFirstHourStartBarIdx = CurrentBar;
                    _usFirstHourHigh = High[0];
                    _usFirstHourLow = Low[0];
                    _usFirstHourComplete = false;
                    _usFirstHourDate = time.Date;
                }
                else
                {
                    // Update high/low
                    if (High[0] > _usFirstHourHigh) _usFirstHourHigh = High[0];
                    if (Low[0] < _usFirstHourLow) _usFirstHourLow = Low[0];
                }
                _usFirstHourEndBarIdx = CurrentBar;
            }
            else if (_usFirstHourStartBarIdx >= 0 && !_usFirstHourComplete)
            {
                // First bar AFTER the first hour — mark complete
                _usFirstHourComplete = true;
            }
        }

        #endregion
    }
}
