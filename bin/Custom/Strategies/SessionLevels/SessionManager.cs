using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// v1.14.42: SessionManager - Manages session level detection, creation, and mitigation
    /// Extracted from SessionLevelsStrategy.CheckSession and ManageLevels
    /// </summary>
    public class SessionManager
    {
        private SessionLevelsStrategy strategy;

        public SessionManager(SessionLevelsStrategy strategy)
        {
            this.strategy = strategy;
        }

        /// <summary>
        /// Check if we're in a specific session and create/update levels accordingly
        /// </summary>
        public void CheckSession(string sessionName, TimeSpan startTs, TimeSpan endTs, Brush color, double deltaVol)
        {
            if (strategy.nyTimeZone == null || strategy.chartTimeZone == null) return;

            DateTime chartTime = strategy.Time[0];
            DateTime nyTime = TimeZoneInfo.ConvertTime(chartTime, strategy.chartTimeZone, strategy.nyTimeZone);
            TimeSpan nyTimeOfDay = nyTime.TimeOfDay;

            bool inSession = false;

            // Handle sessions that cross midnight
            if (startTs > endTs) 
            { 
                if (nyTimeOfDay >= startTs || nyTimeOfDay < endTs) inSession = true; 
            }
            else 
            { 
                if (nyTimeOfDay >= startTs && nyTimeOfDay < endTs) inSession = true; 
            }

            if (inSession)
            {
                // Determine Session Date (for unique ID)
                DateTime calculatedSessionStartNY = (startTs > endTs && nyTimeOfDay < endTs) ? nyTime.Date.AddDays(-1) : nyTime.Date;
                calculatedSessionStartNY = calculatedSessionStartNY.Add(startTs);

                // Unique IDs for High and Low
                string tagH = sessionName + "_High_" + calculatedSessionStartNY.Ticks;
                string tagL = sessionName + "_Low_" + calculatedSessionStartNY.Ticks;

                // Find or Create Levels
                var activeLevels = strategy.activeLevels;
                SessionLevel highLvl = activeLevels.FirstOrDefault(l => l.Tag == tagH);
                SessionLevel lowLvl = activeLevels.FirstOrDefault(l => l.Tag == tagL);

                // Convert Start Time to Chart Time for Visuals
                DateTime chartStartTime = TimeZoneInfo.ConvertTime(calculatedSessionStartNY, strategy.nyTimeZone, strategy.chartTimeZone);

                // FUZZY MATCHING (v1.5.4)
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
                    // New High Level
                    highLvl = new SessionLevel 
                    { 
                        Name = sessionName + " High", 
                        Price = double.MinValue, 
                        StartTime = chartStartTime, 
                        EndTime = strategy.Time[0], 
                        ActualSessionEnd = endTs, // v1.14.49
                        IsResistance = true, 
                        IsMitigated = false, 
                        Color = color, 
                        Tag = tagH,
                        VolSum = strategy.Volume[0], 
                        PvSum = strategy.Volume[0] * strategy.Close[0], 
                        JustReset = true
                    };
                    activeLevels.Add(highLvl);
                }
                else 
                {
                    highLvl.JustReset = false;
                }

                if (lowLvl == null)
                {
                    // New Low Level
                    lowLvl = new SessionLevel
                    {
                        Name = sessionName + " Low",
                        Price = double.MaxValue,
                        StartTime = chartStartTime,
                        EndTime = strategy.Time[0],
                        ActualSessionEnd = endTs, // v1.14.49
                        IsResistance = false,
                        IsMitigated = false,
                        Color = color,
                        Tag = tagL,
                        VolSum = strategy.Volume[0],
                        PvSum = strategy.Volume[0] * strategy.Close[0],
                        JustReset = true
                    };
                    activeLevels.Add(lowLvl);
                    strategy.Print("[DEBUG] Created " + sessionName + " Low tag=" + tagL + " start=" + chartStartTime);
                }
                else
                {
                    lowLvl.JustReset = false;
                }

                // Update High if new high made
                if (strategy.High[0] > highLvl.Price) 
                {
                    highLvl.Price = strategy.High[0];
                    // RE-ANCHOR VWAP
                    highLvl.VolSum = strategy.Volume[0];
                    highLvl.PvSum = strategy.Volume[0] * strategy.Close[0];
                    highLvl.JustReset = true;
                }

                // Update Low if new low made
                if (strategy.Low[0] < lowLvl.Price) 
                {
                    lowLvl.Price = strategy.Low[0];
                    // RE-ANCHOR VWAP
                    lowLvl.VolSum = strategy.Volume[0];
                    lowLvl.PvSum = strategy.Volume[0] * strategy.Close[0];
                    lowLvl.JustReset = true;
                }

                // Extend lines while in session
                highLvl.EndTime = strategy.Time[0];
                lowLvl.EndTime = strategy.Time[0];
            }
        }

        /// <summary>
        /// v1.14.64: Scan historical bars to create levels for sessions that already ended
        /// Called once at startup to retroactively create missing session levels
        /// </summary>
        public void ScanHistoricalLevels()
        {
            if (strategy.nyTimeZone == null || strategy.chartTimeZone == null) return;
            if (strategy.CurrentBar < 20) return; // Need some history

            DateTime chartTime = strategy.Time[0];
            DateTime nyTime = TimeZoneInfo.ConvertTime(chartTime, strategy.chartTimeZone, strategy.nyTimeZone);
            TimeSpan nyNow = nyTime.TimeOfDay;
            DateTime nyToday = nyTime.Date;

            // Define sessions with their parameters
            var sessions = new[]
            {
                new { Name = "Asia", Start = strategy.AsiaStartTime, End = strategy.AsiaEndTime, Color = Brushes.White },
                new { Name = "Europe", Start = strategy.EuropeStartTime, End = strategy.EuropeEndTime, Color = Brushes.Yellow },
                new { Name = "USA", Start = strategy.USAStartTime, End = "18:00:00", Color = Brushes.RoyalBlue }
            };

            foreach (var session in sessions)
            {
                TimeSpan startTs = TimeSpan.Parse(session.Start);
                TimeSpan endTs = TimeSpan.Parse(session.End);

                // Skip if session hasn't ended yet
                bool sessionEnded = false;
                if (startTs > endTs) // Crosses midnight
                {
                    // Session crosses midnight - ended if we're past end time today
                    sessionEnded = (nyNow >= endTs && nyNow < startTs);
                }
                else
                {
                    sessionEnded = (nyNow >= endTs);
                }

                if (!sessionEnded) continue;

                // Calculate session start time for today
                DateTime sessionStartNY = nyToday.Add(startTs);
                if (startTs > endTs && nyNow < startTs) sessionStartNY = nyToday.AddDays(-1).Add(startTs);

                string tagH = session.Name + "_High_" + sessionStartNY.Ticks;
                string tagL = session.Name + "_Low_" + sessionStartNY.Ticks;

                // Check if levels already exist
                var activeLevels = strategy.activeLevels;
                if (activeLevels.Any(l => l.Tag == tagH) && activeLevels.Any(l => l.Tag == tagL))
                    continue; // Already have both levels

                // Scan historical bars for this session
                double sessionHigh = double.MinValue;
                double sessionLow = double.MaxValue;
                DateTime highTime = DateTime.MinValue;
                DateTime lowTime = DateTime.MinValue;

                DateTime sessionEndNY = startTs > endTs 
                    ? sessionStartNY.Date.AddDays(1).Add(endTs) 
                    : sessionStartNY.Date.Add(endTs);

                for (int i = strategy.CurrentBar; i >= 0; i--)
                {
                    DateTime barTimeNY = TimeZoneInfo.ConvertTime(strategy.Time[strategy.CurrentBar - i], strategy.chartTimeZone, strategy.nyTimeZone);
                    
                    if (barTimeNY >= sessionStartNY && barTimeNY < sessionEndNY)
                    {
                        if (strategy.High[strategy.CurrentBar - i] > sessionHigh)
                        {
                            sessionHigh = strategy.High[strategy.CurrentBar - i];
                            highTime = strategy.Time[strategy.CurrentBar - i];
                        }
                        if (strategy.Low[strategy.CurrentBar - i] < sessionLow)
                        {
                            sessionLow = strategy.Low[strategy.CurrentBar - i];
                            lowTime = strategy.Time[strategy.CurrentBar - i];
                        }
                    }
                }

                // Create levels if we found valid data
                DateTime chartStartTime = TimeZoneInfo.ConvertTime(sessionStartNY, strategy.nyTimeZone, strategy.chartTimeZone);
                DateTime chartEndTime = TimeZoneInfo.ConvertTime(sessionEndNY, strategy.nyTimeZone, strategy.chartTimeZone);

                if (sessionHigh != double.MinValue && !activeLevels.Any(l => l.Tag == tagH))
                {
                    var highLvl = new SessionLevel
                    {
                        Name = session.Name + " High",
                        Price = sessionHigh,
                        StartTime = chartStartTime,
                        EndTime = chartEndTime,
                        ActualSessionEnd = endTs,
                        IsResistance = true,
                        IsMitigated = false,
                        Color = session.Color,
                        Tag = tagH,
                        VolSum = 0,
                        PvSum = 0,
                        JustReset = false
                    };
                    activeLevels.Add(highLvl);
                    strategy.Print("[RETROACTIVE] Created " + session.Name + " High @ " + sessionHigh + " tag=" + tagH);
                }

                if (sessionLow != double.MaxValue && !activeLevels.Any(l => l.Tag == tagL))
                {
                    var lowLvl = new SessionLevel
                    {
                        Name = session.Name + " Low",
                        Price = sessionLow,
                        StartTime = chartStartTime,
                        EndTime = chartEndTime,
                        ActualSessionEnd = endTs,
                        IsResistance = false,
                        IsMitigated = false,
                        Color = session.Color,
                        Tag = tagL,
                        VolSum = 0,
                        PvSum = 0,
                        JustReset = false
                    };
                    activeLevels.Add(lowLvl);
                    strategy.Print("[RETROACTIVE] Created " + session.Name + " Low @ " + sessionLow + " tag=" + tagL);
                }
            }
        }

        /// <summary>
        /// Manage level VWAP accumulation, mitigation detection, and drawing
        /// </summary>
        public void ManageLevels(double deltaVol)
        {
            var activeLevels = strategy.activeLevels;

            foreach (var lvl in activeLevels)
            {
                // BACKTEST SAFETY: Ignore future levels
                if (lvl.StartTime > strategy.Time[0]) continue;

                // VWAP ACCUMULATION
                if (!lvl.JustReset)
                {
                    lvl.VolSum += deltaVol;
                    double price = strategy.Close[0];
                    if (strategy.VwapMethod == VwapCalculationMode.Typical) 
                        price = (strategy.High[0] + strategy.Low[0] + strategy.Close[0]) / 3.0;
                    else if (strategy.VwapMethod == VwapCalculationMode.OHLC4) 
                        price = (strategy.Open[0] + strategy.High[0] + strategy.Low[0] + strategy.Close[0]) / 4.0;

                    lvl.PvSum += deltaVol * price;
                }

                // Calculate VWAP
                double vwap = 0;
                if (lvl.VolSum > 0) vwap = lvl.PvSum / lvl.VolSum;

                // LINE EXTENSION LOGIC
                if (!lvl.IsMitigated)
                {
                    lvl.EndTime = strategy.Time[0];
                }
                else
                {
                    // Ghost Line Extension - extend until USA close
                    DateTime mitNy = TimeZoneInfo.ConvertTime(lvl.MitigationTime, strategy.chartTimeZone, strategy.nyTimeZone);
                    TimeSpan usaEndTs = TimeSpan.Parse(strategy.USAEndTime);

                    DateTime cutoffNy;
                    if (mitNy.TimeOfDay < usaEndTs)
                        cutoffNy = mitNy.Date.Add(usaEndTs);
                    else
                        cutoffNy = mitNy.Date.AddDays(1).Add(usaEndTs);

                    DateTime currentNy = TimeZoneInfo.ConvertTime(strategy.Time[0], strategy.chartTimeZone, strategy.nyTimeZone);

                    if (currentNy < cutoffNy)
                    {
                        lvl.EndTime = strategy.Time[0];
                    }
                }

                // Check for Mitigation
                if (!lvl.IsMitigated)
                {
                    bool potentialMitigation = false;
                    if (lvl.IsResistance && strategy.High[0] >= lvl.Price) potentialMitigation = true;
                    if (!lvl.IsResistance && strategy.Low[0] <= lvl.Price) potentialMitigation = true;

                    if (potentialMitigation)
                    {
                        // Strict break check (not just touch)
                        bool strictBreak = false;
                        if (lvl.IsResistance && strategy.High[0] > lvl.Price) strictBreak = true;
                        if (!lvl.IsResistance && strategy.Low[0] < lvl.Price) strictBreak = true;

                        if (strictBreak)
                        {
                            lvl.IsMitigated = true;
                            lvl.MitigationTime = strategy.Time[0];
                        }
                    }
                }

                // Drawing Logic
                if (strategy.ShowVisuals)
                {
                    string tagA = lvl.Tag + "_A";
                    string tagB = lvl.Tag + "_B";

                    if (!lvl.IsMitigated)
                    {
                        // Active level: solid line
                        Draw.Line(strategy, tagA, false, lvl.StartTime, lvl.Price, lvl.EndTime, lvl.Price, lvl.Color, DashStyleHelper.Solid, 2);
                    }
                    else
                    {
                        // Phase A: Start -> Mitigation (solid)
                        Draw.Line(strategy, tagA, false, lvl.StartTime, lvl.Price, lvl.MitigationTime, lvl.Price, lvl.Color, DashStyleHelper.Solid, 2);

                        // Phase B (Ghost): Mitigation -> Current (gray, dashed)
                        Draw.Line(strategy, tagB, false, lvl.MitigationTime, lvl.Price, lvl.EndTime, lvl.Price, Brushes.Gray, DashStyleHelper.Dash, 1);
                    }
                }
            }
        }
    }
}
