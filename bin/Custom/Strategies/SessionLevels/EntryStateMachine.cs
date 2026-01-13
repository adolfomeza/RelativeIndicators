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
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.SessionLevels
{
    /// <summary>
    /// Handles the core entry logic state machine:
    /// Idle -> ScanForTriggers -> WaitingForConfirmation -> OrderSubmitted -> PositionActive
    /// </summary>
    public class EntryStateMachine
    {
        private SessionLevelsStrategy strategy;
		// v1.14.80: Throttling for ChangeOrder
        private DateTime lastOrderUpdateTime = DateTime.MinValue;
        private const int UpdateIntervalMs = 250; // Max 4 updates/sec

        public EntryStateMachine(SessionLevelsStrategy strategy)
        {
            this.strategy = strategy;
        }

        // --- CORE METHODS ---

        /// <summary>
        /// Checks if trading is allowed based on current trading mode (Paused/LongOnly/ShortOnly)
        /// Returns true if trading is allowed, false otherwise
        /// </summary>
        public bool CheckTradingModeGuards()
        {
            // Check if paused
            if (strategy.currentTradingMode == TradingMode.Paused)
                return false;

            // Check LongOnly mode - block Short setups
            if (strategy.currentTradingMode == TradingMode.LongOnly && strategy.isShortSetup)
            {
                strategy.Log(strategy.Time[0] + " BLOCKED: LongOnly mode active, Short setup rejected");
                return false;
            }

            // Check ShortOnly mode - block Long setups
            if (strategy.currentTradingMode == TradingMode.ShortOnly && !strategy.isShortSetup)
            {
                strategy.Log(strategy.Time[0] + " BLOCKED: ShortOnly mode active, Long setup rejected");
                return false;
            }

            return true; // Normal mode or direction matches mode
        }

        /// <summary>
        /// Scans active levels for Short/Long triggers
        /// </summary>
        public void ScanForTriggers()
        {
            // LOOP PROTECTION: If rejected OR invalidated this bar, DO NOT scan again.
            if (strategy.CurrentBar == strategy.lastRejectionBar || strategy.CurrentBar == strategy.lastInvalidationBar) 
                return;
            
            // v1.10.28: FRESH SIGNAL ONLY - Don't trigger on historical setups
            if (strategy.State == State.Realtime && strategy.realtimeStartBar > 0 && strategy.CurrentBar <= strategy.realtimeStartBar)
                return;

            // v1.14.85: DEEP SCAN LOGIC
            // Instead of taking the first trigger (which biases towards older levels),
            // we collect ALL triggers in this bar and select the "Best" one.
            // "Best" = Most Extreme/Deepest.
            // For Supports (Longs): The LOWEST Price (Deepest dip).
            // For Resistances (Shorts): The HIGHEST Price (Highest spike).
            
            List<SessionLevel> candidates = new List<SessionLevel>();

            foreach (var lvl in strategy.activeLevels)
            {
                // FILTER: Ignorar niveles AI bloqueados
                if (!strategy.IsZoneEnabled(lvl.Name, lvl.StartTime))
                    continue;

                // BACKTEST SAFETY: Ignore Future Levels
                if (lvl.StartTime > strategy.Time[0]) continue;

                // v1.10.24: Ignore Same-Day Levels
                // v1.14.49: Fix Same-Day Logic for Globex
                // Allow trading same-day levels IF the session has already closed.
                // v1.14.95: ROBUST SESSION COMPLETION CHECK (TIMEZONE AWARE)
                // Critical: Parameter EndTimes are usually NY/Exchange based, but strategy.Time[0] is Chart/Local.
                // We MUST convert everything to the common Strategy TimeZone (NY) before comparing.
                
                if (strategy.nyTimeZone == null || strategy.chartTimeZone == null) continue;

                DateTime chartTime = strategy.Time[0];
                DateTime currentNyTime = TimeZoneInfo.ConvertTime(chartTime, strategy.chartTimeZone, strategy.nyTimeZone);
                DateTime startNyTime = TimeZoneInfo.ConvertTime(lvl.StartTime, strategy.chartTimeZone, strategy.nyTimeZone);
                
                TimeSpan levelStartTs = startNyTime.TimeOfDay;
                // lvl.ActualSessionEnd is already parsed from parameters (assumed NY/Exchange Time)
                TimeSpan levelEndTs = lvl.ActualSessionEnd; 
                
                bool isOvernightSession = levelStartTs > levelEndTs;

                if (isOvernightSession)
                {
                    // Case A: Still on Start Day (e.g. 23:00 >= 18:00) -> BLOCK
                    if (currentNyTime.Date == startNyTime.Date)
                        continue;

                    // Case B: Next Day (e.g. 01:00 < 02:00) -> BLOCK
                    // Only if we are on the immediate next day
                    if (currentNyTime.Date == startNyTime.Date.AddDays(1) && currentNyTime.TimeOfDay < levelEndTs)
                        continue;
                }
                else
                {
                    // Standard Intraday Session
                    if (currentNyTime.Date == startNyTime.Date && currentNyTime.TimeOfDay < levelEndTs)
                        continue;
                }

                // v1.10.25: Check if max retries exceeded
                if (lvl.EntryAttempts >= strategy.MaxRetriesPerLevel)
                {
                    // v1.14.80: Debug Log to explain why opportunities are "ignored"
                    if (strategy.EnableDebugLogs && strategy.IsFirstTickOfBar)
                         strategy.Log(string.Format("SCAN IGNORE: Level {0} Max Retries Reached ({1}/{2})", lvl.Name, lvl.EntryAttempts, strategy.MaxRetriesPerLevel));
                    continue;
                }
                
                // v1.10.29: Skip levels touched at startup
                if (strategy.skippedLevelsAtStartup.Contains(lvl.Name))
                    continue;

                // TRIGGER CONDITION: Mitigated exactly NOW
                if (lvl.IsMitigated && lvl.MitigationTime == strategy.Time[0])
                {
                     candidates.Add(lvl);
                }
            }

            // PROCESSING SELECTION
            if (candidates.Count > 0)
            {
                SessionLevel selectedLevel = null;

                var supportCandidates = candidates.Where(l => !l.IsResistance).ToList();
                var resistanceCandidates = candidates.Where(l => l.IsResistance).ToList();
                
                // Prioritize Deepest Level
                // Support: Lowest Price
                // Resistance: Highest Price
                
                SessionLevel bestSupport = supportCandidates.OrderBy(l => l.Price).FirstOrDefault(); 
                SessionLevel bestResistance = resistanceCandidates.OrderByDescending(l => l.Price).FirstOrDefault(); 
                
                if (bestSupport != null && bestResistance != null)
                {
                     // If both types triggered (rare/volatile), pick based on bar direction
                     if (strategy.Close[0] < strategy.Open[0]) selectedLevel = bestSupport; // Bearish -> Support
                     else selectedLevel = bestResistance; // Bullish -> Resistance
                }
                else if (bestSupport != null) selectedLevel = bestSupport;
                else if (bestResistance != null) selectedLevel = bestResistance;
                
                if (selectedLevel != null)
                {
                    ProcessSelectedTrigger(selectedLevel);
                }
            }
        }

        private void ProcessSelectedTrigger(SessionLevel lvl)
        {
            // If already waiting, check if different level (SWITCH Logic)
            if (strategy.currentEntryState == EntryState.WaitingForConfirmation)
            {
                if (lvl.Name == strategy.setupLevelName)
                    return; // Same level, ignore, keep working on it
                else
                {
                    // SWITCH: Found a "better" (deeper) level or a new level while waiting on another.
                    double currentLevelPrice = 0;
                    var currentLevel = strategy.activeLevels.FirstOrDefault(l => l.Name == strategy.setupLevelName);
                    if (currentLevel != null) currentLevelPrice = currentLevel.Price;
                    double delta = Math.Abs(currentLevelPrice - lvl.Price);
                    
                    if (strategy.EnableDebugLogs)
                        strategy.Log($"SWITCH_EVAL: Current={strategy.setupLevelName}@{currentLevelPrice} " +
                            $"New={lvl.Name}@{lvl.Price} Delta={delta:F5} TickSize={strategy.TickSize}");
                    
                    strategy.Log(strategy.Time[0] + " SWITCH: New Trigger on " + lvl.Name + " overrides " + strategy.setupLevelName);
                }
            }
                
            // TRIGGER CONFIRMED -> Initialize Setup
            if (!lvl.IsResistance)
            {
                // Long Setup
                // v1.14.80: Recycle Tags to prevent Drawing Object Accumulation Leak
                strategy.triggerTag = "TriggerLong_" + (strategy.triggerLabelIndex % 50);
                strategy.triggerLabelIndex++;
                
                strategy.triggerBar = strategy.CurrentBar;
                strategy.DrawTriggerLabel(strategy.triggerTag, false, 0, strategy.Low[0]);
                
                strategy.currentEntryState = EntryState.WaitingForConfirmation;
                strategy.visualConfirmationDone = false;
                strategy.isShortSetup = false;
                strategy.setupAnchorPrice = strategy.Low[0];
                strategy.setupLevelName = lvl.Name;
                strategy.setupLevelTime = lvl.StartTime;
                strategy.validatedTargetPrice = 0;
                strategy.cachedOppositeLevel = null;
                
                // Reset retry state
                strategy.waitingForVwapMitigation = false;
                strategy.currentVwapNumber = 1;
                strategy.vwapCandleExtreme = 0;
                
                lvl.EntryAttempts++;
                strategy.Log(string.Format("{0} ENTRY ATTEMPT #{1}/{2} on {3} (Deepest Selection)", strategy.Time[0], lvl.EntryAttempts, strategy.MaxRetriesPerLevel, lvl.Name));
				strategy.currentLevelAttempts = lvl.EntryAttempts; // v1.15.15: Store for persistent display
                
                strategy.DetectInternalLevel(lvl, strategy.activeLevels);
                
                // RESET ADHOC VWAP
                double price = strategy.Close[0];
                if (strategy.VwapMethod == VwapCalculationMode.Typical) 
                    price = (strategy.High[0] + strategy.Low[0] + strategy.Close[0]) / 3.0;
                else if (strategy.VwapMethod == VwapCalculationMode.OHLC4) 
                    price = (strategy.Open[0] + strategy.High[0] + strategy.Low[0] + strategy.Close[0]) / 4.0;

                strategy.adhocVolSum = strategy.Volume[0];
                strategy.adhocPvSum = strategy.Volume[0] * price;
                strategy.adhocLastBar = strategy.CurrentBar;
                strategy.adhocLastVol = strategy.Volume[0];
                strategy.adhocAnchorBar = strategy.CurrentBar;

                strategy.visualAdhocPrevBarVal = price;
                strategy.visualAdhocLastVal = price;
                strategy.visualAdhocLastBar = -1;
            }
            else 
            {
                // Short Setup
                // v1.14.80: Recycle Tags to prevent Drawing Object Accumulation Leak
                strategy.triggerTag = "TriggerShort_" + (strategy.triggerLabelIndex % 50);
                strategy.triggerLabelIndex++;

                strategy.triggerBar = strategy.CurrentBar;
                strategy.DrawTriggerLabel(strategy.triggerTag, true, 0, strategy.High[0]);
                
                strategy.currentEntryState = EntryState.WaitingForConfirmation;
                strategy.visualConfirmationDone = false;
                strategy.isShortSetup = true;
                strategy.setupAnchorPrice = strategy.High[0];
                strategy.setupLevelName = lvl.Name;
                strategy.setupLevelTime = lvl.StartTime;
                strategy.validatedTargetPrice = 0;
                strategy.cachedOppositeLevel = null;
                
                // Reset retry state
                strategy.waitingForVwapMitigation = false;
                strategy.currentVwapNumber = 1;
                strategy.vwapCandleExtreme = 0;
                
                lvl.EntryAttempts++;
				strategy.currentLevelAttempts = lvl.EntryAttempts; // v1.15.15: Store for persistent display
                strategy.Log(string.Format("{0} ENTRY ATTEMPT #{1}/{2} on {3} (Deepest Selection)", strategy.Time[0], lvl.EntryAttempts, strategy.MaxRetriesPerLevel, lvl.Name));
                
                strategy.DetectInternalLevel(lvl, strategy.activeLevels);
                
                // RESET ADHOC VWAP
                double price = strategy.Close[0];
                if (strategy.VwapMethod == VwapCalculationMode.Typical) 
                    price = (strategy.High[0] + strategy.Low[0] + strategy.Close[0]) / 3.0;
                else if (strategy.VwapMethod == VwapCalculationMode.OHLC4) 
                    price = (strategy.Open[0] + strategy.High[0] + strategy.Low[0] + strategy.Close[0]) / 4.0;

                strategy.adhocVolSum = strategy.Volume[0];
                strategy.adhocPvSum = strategy.Volume[0] * price;
                strategy.adhocLastBar = strategy.CurrentBar;
                strategy.adhocLastVol = strategy.Volume[0];
                strategy.adhocAnchorBar = strategy.CurrentBar;

                strategy.visualAdhocPrevBarVal = price;
                strategy.visualAdhocLastVal = price;
                strategy.visualAdhocLastBar = -1;
            }
            
            // v1.14.50: Audio Alert
            if (strategy.UseAlerts && !string.IsNullOrEmpty(strategy.AlertSoundFile))
            {
                try 
                {
                    strategy.PlaySound(strategy.AlertSoundFile);
                }
                catch (Exception ex) 
                {
                    strategy.Log("AUDIO ERROR: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// v1.14.45: Detects if we are in VWAP Mitigation Retry state
        /// Returns true if waiting for mitigation (blocks other entry logic)
        /// </summary>
        public bool HandleVwapMitigationRetry()
        {
            // Return true if we are waiting for VWAP mitigation to complete
            // This prevents other entry logic from running while we wait
            return strategy.waitingForVwapMitigation &&
                   strategy.currentEntryState == EntryState.WaitingForVwapMitigation;
        }

        /// <summary>
        /// v1.10.0: Re-anchors the setup when price makes new high/low
        /// SHORT: Re-anchor if price makes new high
        /// LONG: Re-anchor if price makes new low
        /// </summary>
        public void UpdateAnchorIfNeeded()
        {
            if (strategy.currentEntryState != EntryState.WaitingForConfirmation &&
                strategy.currentEntryState != EntryState.workingOrder)
                return;

            bool isShortSetup = strategy.isShortSetup;
            double setupAnchorPrice = strategy.setupAnchorPrice;
            double TickSize = strategy.TickSize;

            // SHORT: Re-anchor if price makes new high
            if (isShortSetup && strategy.High[0] >= setupAnchorPrice + TickSize)
            {
                strategy.setupAnchorPrice = strategy.High[0];

                // Reset VWAP from new anchor
                double price = strategy.Close[0];
                if (strategy.VwapMethod == VwapCalculationMode.Typical)
                    price = (strategy.High[0] + strategy.Low[0] + strategy.Close[0]) / 3.0;
                else if (strategy.VwapMethod == VwapCalculationMode.OHLC4)
                    price = (strategy.Open[0] + strategy.High[0] + strategy.Low[0] + strategy.Close[0]) / 4.0;

                strategy.adhocVolSum = strategy.Volume[0];
                strategy.adhocPvSum = strategy.Volume[0] * price;
                strategy.adhocLastBar = strategy.CurrentBar;
                strategy.adhocLastVol = strategy.Volume[0];
                strategy.adhocAnchorBar = strategy.CurrentBar;

                // Reset Visual
                strategy.visualAdhocPrevBarVal = price;
                strategy.visualAdhocLastVal = price;
                strategy.visualAdhocLastBar = -1;

                strategy.Log(string.Format("RE-ANCHOR: New High @ {0} (Setup: {1})", strategy.setupAnchorPrice, strategy.setupLevelName));
            }

            // LONG: Re-anchor if price makes new low
            if (!isShortSetup && strategy.Low[0] <= setupAnchorPrice - TickSize)
            {
                strategy.setupAnchorPrice = strategy.Low[0];

                // Reset VWAP from new anchor
                double price = strategy.Close[0];
                if (strategy.VwapMethod == VwapCalculationMode.Typical)
                    price = (strategy.High[0] + strategy.Low[0] + strategy.Close[0]) / 3.0;
                else if (strategy.VwapMethod == VwapCalculationMode.OHLC4)
                    price = (strategy.Open[0] + strategy.High[0] + strategy.Low[0] + strategy.Close[0]) / 4.0;

                strategy.adhocVolSum = strategy.Volume[0];
                strategy.adhocPvSum = strategy.Volume[0] * price;
                strategy.adhocLastBar = strategy.CurrentBar;
                strategy.adhocLastVol = strategy.Volume[0];
                strategy.adhocAnchorBar = strategy.CurrentBar;

                // Reset Visual
                strategy.visualAdhocPrevBarVal = price;
                strategy.visualAdhocLastVal = price;
                strategy.visualAdhocLastBar = -1;

                strategy.Log(string.Format("RE-ANCHOR: New Low @ {0} (Setup: {1})", strategy.setupAnchorPrice, strategy.setupLevelName));
            }
        }

        /// <summary>
        /// v1.10.0: Handles invalidation when internal level touches external level
        /// If touched, cancels the internal setup and auto-triggers on the external level
        /// </summary>
        public void HandleInternalInvalidation()
        {
            if (!strategy.isInternalLevel) return;
            if (strategy.currentEntryState != EntryState.WaitingForConfirmation) return;

            bool touchedExternal = false;
            bool isShortSetup = strategy.isShortSetup;

            // SHORT internal: Check if touched external High above
            if (isShortSetup && strategy.externalLevelAbove > 0)
            {
                if (strategy.High[0] >= strategy.externalLevelAbove)
                {
                    touchedExternal = true;
                    strategy.Log(string.Format("INVALIDATED: Touched external {0} @ {1}",
                        strategy.externalLevelAboveName, strategy.externalLevelAbove));
                }
            }

            // LONG internal: Check if touched external Low below
            if (!isShortSetup && strategy.externalLevelBelow > 0)
            {
                if (strategy.Low[0] <= strategy.externalLevelBelow)
                {
                    touchedExternal = true;
                    strategy.Log(string.Format("INVALIDATED: Touched external {0} @ {1}",
                        strategy.externalLevelBelowName, strategy.externalLevelBelow));
                }
            }

            if (touchedExternal)
            {
                // v1.10.1: Mark bar to prevent re-triggering (infinite loop fix)
                strategy.lastInvalidationBar = strategy.CurrentBar;

                // Cancel entry order if exists
                if (strategy.entryOrder != null &&
                    (strategy.entryOrder.OrderState == OrderState.Working ||
                     strategy.entryOrder.OrderState == OrderState.Accepted))
                {
                    strategy.CancelOrder(strategy.entryOrder);
                }

                // Reset to Idle
                strategy.currentEntryState = EntryState.Idle;
                strategy.isInternalLevel = false;

                // v1.10.2: AUTO-TRIGGER on external level after invalidation
                string externalName = isShortSetup ? strategy.externalLevelAboveName : strategy.externalLevelBelowName;
                double externalPrice = isShortSetup ? strategy.externalLevelAbove : strategy.externalLevelBelow;

                if (externalPrice > 0 && !string.IsNullOrEmpty(externalName))
                {
                    strategy.Log(string.Format("AUTO-TRIGGER: Switching to external level {0} @ {1}", externalName, externalPrice));

                    // Find the external level in activeLevels
                    var externalLevel = strategy.activeLevels.FirstOrDefault(l => l.Name == externalName);
                    if (externalLevel != null)
                    {
                        ProcessSelectedTrigger(externalLevel);
                    }
                }
            }
        }

        /// <summary>
        /// v1.14.54: Handles VWAP Retry logic when waiting for price to break the extreme
        /// after a trade closes with SL/BE
        /// </summary>
        public void HandleVwapMitigationWait()
        {
            // Only process if in the correct state
            if (!strategy.waitingForVwapMitigation) return;
            if (strategy.currentEntryState != EntryState.WaitingForVwapMitigation) return;
            if (strategy.vwapCandleExtreme == 0) return;
            
            // Check if price has broken the extreme (re-trigger condition)
            bool priceBreaksExtreme = false;
            
            if (strategy.isShortSetup)
            {
                // Short: Price must break ABOVE the anchor to re-trigger
                priceBreaksExtreme = strategy.High[0] > strategy.vwapCandleExtreme;
            }
            else
            {
                // Long: Price must break BELOW the anchor to re-trigger
                priceBreaksExtreme = strategy.Low[0] < strategy.vwapCandleExtreme;
            }
            
            if (priceBreaksExtreme)
            {
                strategy.Log(string.Format("{0} VWAP RETRY TRIGGERED: Price broke {1:F2}. Starting attempt #{2}", 
                    strategy.Time[0], strategy.vwapCandleExtreme, strategy.currentVwapNumber));
                
                // Reset to WaitingForConfirmation to re-run the entry logic
                strategy.currentEntryState = EntryState.WaitingForConfirmation;
                strategy.waitingForVwapMitigation = false;
                strategy.visualConfirmationDone = false;
                
                // Update anchor to current extreme
                // Update anchor to current extreme
                if (strategy.isShortSetup)
                {
                    strategy.setupAnchorPrice = strategy.High[0];
                    // Recycle Logic for Retries too
                    strategy.triggerTag = "RetryShort_" + (strategy.triggerLabelIndex % 50);
                    strategy.triggerLabelIndex++;
                    strategy.triggerBar = strategy.CurrentBar;
                    strategy.DrawTriggerLabel(strategy.triggerTag, true, 0, strategy.High[0]);
                }
                else
                {
                    strategy.setupAnchorPrice = strategy.Low[0];
                    // Recycle Logic for Retries too
                    strategy.triggerTag = "RetryLong_" + (strategy.triggerLabelIndex % 50);
                    strategy.triggerLabelIndex++;
                    strategy.triggerBar = strategy.CurrentBar;
                    strategy.DrawTriggerLabel(strategy.triggerTag, false, 0, strategy.Low[0]);
                }
                
                // Reset ADHOC VWAP for fresh calculation
                double price = strategy.Close[0];
                if (strategy.VwapMethod == VwapCalculationMode.Typical) 
                    price = (strategy.High[0] + strategy.Low[0] + strategy.Close[0]) / 3.0;
                else if (strategy.VwapMethod == VwapCalculationMode.OHLC4) 
                    price = (strategy.Open[0] + strategy.High[0] + strategy.Low[0] + strategy.Close[0]) / 4.0;
                
                strategy.adhocVolSum = strategy.Volume[0];
                strategy.adhocPvSum = strategy.Volume[0] * price;
                strategy.adhocLastBar = strategy.CurrentBar;
                strategy.adhocLastVol = strategy.Volume[0];
                strategy.adhocAnchorBar = strategy.CurrentBar;
            }
        }


        /// <summary>
        /// Handles confirmation logic after trigger detected
        /// </summary>
        public void HandleConfirmation()
		{
			// Verify state
			if (strategy.currentEntryState != EntryState.WaitingForConfirmation) return;

			// "Wait for a candle... close... max below vwap 1 tick"
			// Logic runs on FIRST TICK of the bar (confirmed closed bar)
			if (!strategy.IsFirstTickOfBar) return;
			// Must be past the trigger bar
			if (strategy.CurrentBar <= strategy.triggerBar) return;

			// Access shared variables
			bool isShortSetup = strategy.isShortSetup;
			double setupAnchorPrice = strategy.setupAnchorPrice;
			string setupLevelName = strategy.setupLevelName;
			DateTime setupLevelTime = strategy.setupLevelTime;
			double TickSize = strategy.TickSize;

			// Determine Local VWAP to use
			double setupVWAP = strategy.GetSetupVWAP(isShortSetup);

			if (isShortSetup)
			{
				// Short: High[1] < Bearish VWAP (setupVWAP) - 1 Tick
				// Confirmation Rule: The candle must stay BELOW the VWAP to confirm rejection.
				if (strategy.isValidVWAP(setupVWAP) && strategy.High[1] < (setupVWAP - TickSize))
				{
					// --- RISK / REWARD CHECK ---
					double projectedEntry = setupVWAP;
					
					// Log(string.Format("{0} | DEBUG_ENTRY: Calling GetOppositeLevelPrice...", strategy.Time[0]));
					
					// Padding: Stop is placed 1 tick ABOVE the anchor for breathing room.
					double projectedStop = setupAnchorPrice + TickSize; 

					// VALIDATE R/R (v1.7.28) - Continuous validation
					double risk, reward, ratio;
					bool isValidRR = strategy.ValidateRiskReward(true, projectedEntry, projectedStop, out risk, out reward, out ratio);

					// v1.14.52: Highlight confirmation candle REGARDLESS of R/R (visual confirmation of activity)
					if (strategy.HighlightConfirmationCandle && strategy.CurrentBar > 1 && !strategy.visualConfirmationDone)
					{
						strategy.BarBrushes[1] = strategy.ConfirmationCandleColor;
						strategy.CandleOutlineBrushes[1] = strategy.ConfirmationCandleColor;
						strategy.visualConfirmationDone = true;
					}

					if (isValidRR)
					{
						// CAPTURE TARGET (v1.7.16)
						// CAPTURE TARGET (v1.7.16)
						// v1.14.83: FIX - Do NOT lock fallback VWAP into validatedTargetPrice.
						// If GetOppositeLevelPrice returns 0, we use VWAP for R:R check locally,
						// but leave validatedTargetPrice as 0 so OrderProtectionManager can try again.
						double levelPrice = strategy.GetOppositeLevelPrice(setupLevelName, setupLevelTime, setupAnchorPrice, true);
						double rrTarget = levelPrice;
						
						if (rrTarget == 0) 
						{
							rrTarget = strategy.GetCurrentLowVWAP();
							// Do NOT set validatedTargetPrice here. Let it remain 0.
							strategy.validatedTargetPrice = 0; 
						}
						else
						{
							// Found a real level - lock it in
							strategy.validatedTargetPrice = levelPrice;
						}

						// EXE DEBUG & ROUNDING
						double limitPrice = strategy.Instrument.MasterInstrument.RoundToTickSize(setupVWAP);
						if (strategy.EnableDebugLogs)
						{
							try 
							{ 
								strategy.Log(string.Format("{0} | EXEC_DEBUG: Submitting Short Limit @ {1} (Raw: {2}). Bid={3} Ask={4}", 
									strategy.Time[0], limitPrice, setupVWAP, strategy.GetCurrentBid(), strategy.GetCurrentAsk())); 
							} catch {}
						}

						// Historical/Playback Checks
						bool isPlayback = (Connection.PlaybackConnection != null);
						bool canSubmitOrder = (strategy.State == State.Realtime) || (strategy.State == State.Historical && (isPlayback || strategy.AllowBacktest));

						if (canSubmitOrder)
					{
						if (strategy.entryOrder != null) 
						{
							strategy.Log("WARNING: Entry Order already exists? Overwriting.");
						}

						// v1.14.73: Entry Mode Selection
						double entryPrice;
						string entryTag;
						OrderType orderType;
						
						if (strategy.SelectedEntryMode == EntryMode.Anticipado)
						{
							// ANTICIPATED MODE: Enter on confirmation candle close
							entryPrice = strategy.Close[0];
							entryTag = string.Format("EntryAnticipado_Short_{0:D2}", strategy.currentVwapNumber);
							orderType = strategy.AnticipatedType == AnticipatedOrderType.Market ? OrderType.Market : OrderType.Limit;
							strategy.Log(string.Format("{0} | ANTICIPATED ENTRY: {1} @ {2}", strategy.Time[0], orderType, entryPrice));
						}
						else
						{
							// A+ RETRACE MODE: Wait for VWAP pullback (original behavior)
							entryPrice = limitPrice;
							entryTag = string.Format("EntryA+_Short_{0:D2}", strategy.currentVwapNumber);
							orderType = OrderType.Limit;
						}

						// DYNAMIC SIZING (v1.8.0)
						int dynamicQuantity = strategy.CalculateDynamicQuantity(entryPrice, projectedStop);

						// v1.11.17: Lag Filter - Block order if chart has lag
						if (!strategy.CheckChartLag())
						{
							string msg = "Skipped: Network Lag Detected";
							strategy.Log(strategy.Time[0] + " Short order BLOCKED: " + msg);
							strategy.lastFilterReason = msg; strategy.lastFilterTime = DateTime.Now;
							return;
						}

						// v1.14.61: Fix Race Condition - Set State BEFORE Order Submission
						strategy.currentEntryState = EntryState.workingOrder;
						
						if (orderType == OrderType.Market)
							strategy.entryOrder = strategy.SubmitOrderUnmanaged(0, OrderAction.SellShort, OrderType.Market, dynamicQuantity, 0, 0, "", entryTag);
						else
							strategy.entryOrder = strategy.SubmitOrderUnmanaged(0, OrderAction.SellShort, OrderType.Limit, dynamicQuantity, entryPrice, 0, "", entryTag);

						if (strategy.entryOrder == null)
						{
							strategy.currentEntryState = EntryState.Idle; // Revert if submit failed
							strategy.Log("CRITICAL: Order Submit Failed. Reverting State to Idle.");
							return;
						}
						
						strategy.Log(strategy.Time[0] + " Order Submitted (Short). Mode=" + strategy.SelectedEntryMode + " Type=" + orderType + " Qty=" + dynamicQuantity);
					}
						else
						{
							// Historical Skip
						}
					}
					else
					{
						string msg = string.Format("Skipped: R/R {0:F2} < 1.0", (risk > 0 ? (reward/risk) : 0));
						strategy.Log(strategy.Time[0] + string.Format(" Trade Skipped (Short). Risk: {0:F2} Reward: {1:F2} Ratio: {2:F2}", risk, reward, (risk > 0 ? (reward/risk) : 0)));
						strategy.lastFilterReason = msg; strategy.lastFilterTime = DateTime.Now;
					}
				}
				else
				{
					// Check invalidation (End of Bar)
					if (strategy.High[0] > setupAnchorPrice)
					{
						// HANDLED BY HandleInternalInvalidation/UpdateAnchorIfNeeded ALREADY
						// We don't do anything here.
					}
						else
					{
						// DEBUG waiting - v1.14.74: More frequent logging for diagnosis
						// if (strategy.CurrentBar % 2 == 0) // Every 2 bars for diagnosis
						// 	strategy.Log(string.Format("{0} | WAITING SHORT: High[1]={1:F2} VWAP={2:F2} Req={3:F2} | GlobalHigh={4:F2} GlobalLow={5:F2} | Anchor={6}", 
						// 		strategy.Time[0], strategy.High[1], setupVWAP, (setupVWAP - TickSize), 
						// 		strategy.GetCurrentHighVWAP(), strategy.GetCurrentLowVWAP(), setupAnchorPrice));
					}
				}
			}
			else
			{
				// LONG SETUP
				// Long: Low[1] > Bullish VWAP (setupVWAP) + 1 Tick
				if (strategy.isValidVWAP(setupVWAP) && strategy.Low[1] > (setupVWAP + TickSize))
				{
					// --- RISK / REWARD CHECK ---
					double projectedEntry = setupVWAP;
					double projectedStop = setupAnchorPrice - TickSize; // Padding

					// VALIDATE R/R
					double risk, reward, ratio;
					bool isValidRR = strategy.ValidateRiskReward(false, projectedEntry, projectedStop, out risk, out reward, out ratio);

					// v1.14.52: Highlight confirmation candle REGARDLESS of R/R (visual confirmation of activity)
					if (strategy.HighlightConfirmationCandle && strategy.CurrentBar > 1 && !strategy.visualConfirmationDone)
					{
						strategy.BarBrushes[1] = strategy.ConfirmationCandleColor;
						strategy.CandleOutlineBrushes[1] = strategy.ConfirmationCandleColor;
						strategy.visualConfirmationDone = true;
					}

					if (isValidRR)
					{
						// CAPTURE TARGET
						// CAPTURE TARGET
						// v1.14.83: FIX - Do NOT lock fallback VWAP into validatedTargetPrice.
						double levelPrice = strategy.GetOppositeLevelPrice(setupLevelName, setupLevelTime, setupAnchorPrice, false);
						double rrTarget = levelPrice;
						
						if (rrTarget == 0) 
						{
							rrTarget = strategy.GetCurrentHighVWAP();
							// Do NOT set validatedTargetPrice here. Let it remain 0.
							strategy.validatedTargetPrice = 0; 
						}
						else
						{
							// Found a real level - lock it in
							strategy.validatedTargetPrice = levelPrice;
						}

						// EXE DEBUG & ROUNDING
						double limitPrice = strategy.Instrument.MasterInstrument.RoundToTickSize(setupVWAP);
						if (strategy.EnableDebugLogs)
						{
							try 
							{ 
								strategy.Log(string.Format("{0} | EXEC_DEBUG: Submitting Long Limit @ {1} (Raw: {2}). Bid={3} Ask={4}", 
									strategy.Time[0], limitPrice, setupVWAP, strategy.GetCurrentBid(), strategy.GetCurrentAsk())); 
							} catch {}
						}

						bool isPlayback = (Connection.PlaybackConnection != null);
						bool canSubmitOrder = (strategy.State == State.Realtime) || (strategy.State == State.Historical && (isPlayback || strategy.AllowBacktest));

						if (canSubmitOrder)
					{
						if (strategy.entryOrder != null) 
						{
							strategy.Log("WARNING: Entry Order already exists? Overwriting.");
						}

						// v1.14.73: Entry Mode Selection
						double entryPrice;
						string entryTag;
						OrderType orderType;
						
						if (strategy.SelectedEntryMode == EntryMode.Anticipado)
						{
							// ANTICIPATED MODE: Enter on confirmation candle close
							entryPrice = strategy.Close[0];
							entryTag = string.Format("EntryAnticipado_Long_{0:D2}", strategy.currentVwapNumber);
							orderType = strategy.AnticipatedType == AnticipatedOrderType.Market ? OrderType.Market : OrderType.Limit;
							strategy.Log(string.Format("{0} | ANTICIPATED ENTRY: {1} @ {2}", strategy.Time[0], orderType, entryPrice));
						}
						else
						{
							// A+ RETRACE MODE: Wait for VWAP pullback (original behavior)
							entryPrice = limitPrice;
							entryTag = string.Format("EntryA+_Long_{0:D2}", strategy.currentVwapNumber);
							orderType = OrderType.Limit;
						}

						int dynamicQuantity = strategy.CalculateDynamicQuantity(entryPrice, projectedStop);

						if (!strategy.CheckChartLag())
						{
							string msg = "Skipped: Network Lag Detected";
							strategy.Log(strategy.Time[0] + " Long order BLOCKED: " + msg);
							strategy.lastFilterReason = msg; strategy.lastFilterTime = DateTime.Now;
							return;
						}

						// v1.14.61: Fix Race Condition - Set State BEFORE Order Submission
						strategy.currentEntryState = EntryState.workingOrder;
						
						if (orderType == OrderType.Market)
							strategy.entryOrder = strategy.SubmitOrderUnmanaged(0, OrderAction.Buy, OrderType.Market, dynamicQuantity, 0, 0, "", entryTag);
						else
							strategy.entryOrder = strategy.SubmitOrderUnmanaged(0, OrderAction.Buy, OrderType.Limit, dynamicQuantity, entryPrice, 0, "", entryTag);
						
						if (strategy.entryOrder == null)
						{
							strategy.currentEntryState = EntryState.Idle; // Revert if submit failed
							strategy.Log("CRITICAL: Order Submit Failed. Reverting State to Idle.");
							return;
						}

						strategy.Log(strategy.Time[0] + " Order Submitted (Long). Mode=" + strategy.SelectedEntryMode + " Type=" + orderType + " Qty=" + dynamicQuantity);
					}
						else
						{
                            // Skipped
						}
					}
					else
					{
						string msg = string.Format("Skipped: R/R {0:F2} < 1.0", (risk > 0 ? (reward/risk) : 0));
						strategy.Log(strategy.Time[0] + string.Format(" Trade Skipped (Long). Risk: {0:F2} Reward: {1:F2} Ratio: {2:F2}", risk, reward, (risk > 0 ? (reward/risk) : 0)));
						strategy.lastFilterReason = msg; strategy.lastFilterTime = DateTime.Now;
					}
				}
				else
				{
					// Check invalidation handled by UpdateAnchorIfNeeded
				}
			}
		}

        /// <summary>
        /// Handles active working order (trailing, R/R validation, qty adjustment)
        /// </summary>
        public void HandleWorkingOrder()
        {
            // Skip if not in working order state
            if (strategy.currentEntryState != EntryState.workingOrder) return;
            if (strategy.entryOrder == null) return;

            bool isShortSetup = strategy.isShortSetup;
            double setupAnchorPrice = strategy.setupAnchorPrice;
            double TickSize = strategy.TickSize;

            // 1. CONTINUOUS R/R VALIDATION (v1.7.28)
            if (strategy.entryOrder.OrderState == OrderState.Working)
            {
                double currentEntry = (strategy.entryOrder.LimitPrice > 0) ? strategy.entryOrder.LimitPrice : strategy.Close[0];
                double currentStop = isShortSetup ? (setupAnchorPrice + TickSize) : (setupAnchorPrice - TickSize);

                double risk, reward, ratio;
                bool isStillValid = strategy.ValidateRiskReward(isShortSetup, currentEntry, currentStop, out risk, out reward, out ratio);

                if (!isStillValid)
                {
                    strategy.Log(string.Format("{0} R/R Invalidated While Working. Risk: {1:F2} Reward: {2:F2} Ratio: {3:F2} - Cancelling Order", 
                        strategy.Time[0], risk, reward, ratio));

                    if (strategy.entryOrder != null && strategy.entryOrder.OrderState == OrderState.Working)
                        strategy.CancelOrder(strategy.entryOrder);

                    // v1.14.80: Treat R/R Invalidation as "Virtual SL" -> Wait for VWAP Mitigation
                    // Do NOT go to Idle. Wait for price to break anchor to retry.
                    strategy.currentEntryState = EntryState.WaitingForVwapMitigation;
                    strategy.waitingForVwapMitigation = true;
                    strategy.visualConfirmationDone = false;
                    strategy.vwapCandleExtreme = strategy.setupAnchorPrice; // Anchor becomes the breakout level
                    strategy.currentVwapNumber++; // Count this as a failed attempt
                    
                    strategy.Log(string.Format("{0} R/R CANCEL -> ENTRO EN ESPERA DE MITIGACION (Virtual SL). Anchor={1}", strategy.Time[0], strategy.setupAnchorPrice));
                    return;
                }
            }

            // 2. CHECK IF ALREADY FILLED (Sync fallback)
            bool anyFilled = (strategy.entryOrder.OrderState == OrderState.Filled || strategy.entryOrder.OrderState == OrderState.PartFilled);
            if (anyFilled)
            {
                // v1.14.69: DIAGNOSTIC LOG - Capture timing to debug race condition
                double msSinceFill = (DateTime.Now - strategy.entryOrder.Time).TotalMilliseconds;
                strategy.Log($"SYNC_DEBUG: execution.Name={strategy.entryOrder.Name} OrderState={strategy.entryOrder.OrderState} " +
                    $"Position.MarketPosition={strategy.Position.MarketPosition} Position.Qty={strategy.Position.Quantity} " +
                    $"timeSinceFill={msSinceFill:F0}ms");
                
                // v1.14.70: FIX Race Condition - Only transition to PositionActive if Position is actually updated
                // NinjaTrader can report OrderState=Filled before Position.MarketPosition is updated (race condition ~55ms)
                if (strategy.Position.MarketPosition != MarketPosition.Flat)
                {
                    strategy.Log(strategy.Time[0] + " SYNC: Order Filled and Position confirmed. Forcing InPosition.");
                    strategy.currentEntryState = EntryState.PositionActive;
                }
                else
                {
                    // Position not updated yet - stay in Working state, don't reset to Idle
                    // OnPositionUpdate or next OnBarUpdate will catch the actual position
                    strategy.Log($"SYNC_WAIT: OrderState=Filled but Position.MarketPosition=Flat ({msSinceFill:F0}ms ago). Waiting for Position update.");
                    // DO NOT change state - remain in Working
                }
                return;
            }

            // 3. TRAILING LOGIC (while order is working)
            double currentVWAP = strategy.GetSetupVWAP(isShortSetup);

            // 3a. Check VWAP still valid
            if (!strategy.isValidVWAP(currentVWAP))
            {
                string msg = "Skipped: Setup VWAP Invalidated";
                strategy.Log(strategy.Time[0] + " CANCEL: " + msg);
                strategy.lastFilterReason = msg; strategy.lastFilterTime = DateTime.Now;
                if (strategy.entryOrder != null) strategy.CancelOrder(strategy.entryOrder);
                return;
            }

            // 3b. Check target touch (v1.14.4)
            double targetPrice = strategy.GetOppositeLevelPrice(strategy.setupLevelName, strategy.setupLevelTime);
            if (targetPrice == 0) targetPrice = isShortSetup ? strategy.GetCurrentLowVWAP() : strategy.GetCurrentHighVWAP();

            bool targetTouched = false;
            if (isShortSetup && strategy.Low[0] <= targetPrice) targetTouched = true;
            if (!isShortSetup && strategy.High[0] >= targetPrice) targetTouched = true;

            if (targetTouched)
            {
                string msg = string.Format("Skipped: Target Touched ({0})", targetPrice);
                strategy.Log(string.Format("{0} CANCEL: {1} before Entry. Setup invalidated.", strategy.Time[0], msg));
                strategy.lastFilterReason = msg; strategy.lastFilterTime = DateTime.Now;
                if (strategy.entryOrder != null) strategy.CancelOrder(strategy.entryOrder);
                strategy.currentEntryState = EntryState.Idle;
                strategy.setupLevelName = "";
                return;
            }

            // 3c. Update order price (trailing) and quantity
            // 3c. Update order price (trailing) and quantity
            if (strategy.entryOrder.OrderState == OrderState.Working)
            {
                // v1.14.80: Throttling Check MOVED UP to prevent expensive Calculation & File I/O
                // Limit updates/calcs to 4 times per second
                bool isThrottled = (DateTime.Now - lastOrderUpdateTime).TotalMilliseconds < UpdateIntervalMs;
                if (isThrottled) return;

                double projectedStop = isShortSetup ? (setupAnchorPrice + TickSize) : (setupAnchorPrice - TickSize);
                
                // This call is expensive (File I/O + Logs) - now throttled
                int newQuantity = strategy.CalculateDynamicQuantity(currentVWAP, projectedStop);

                bool priceChanged = Math.Abs(strategy.entryOrder.LimitPrice - currentVWAP) >= TickSize;
                bool quantityChanged = newQuantity != strategy.entryOrder.Quantity;

                // Always update timestamp if we performed the check/calc
                lastOrderUpdateTime = DateTime.Now; 

                if (priceChanged || quantityChanged)
                {
                    double newLimitPrice = priceChanged ? currentVWAP : strategy.entryOrder.LimitPrice;
                    strategy.ChangeOrder(strategy.entryOrder, newQuantity, newLimitPrice, 0);
                    
                    if (quantityChanged)
                    {
                        strategy.Log(string.Format("{0} | DYNAMIC QTY ADJUST: Old={1} New={2} (Stop moved to {3:F2})",
                            strategy.Time[0], strategy.entryOrder.Quantity, newQuantity, projectedStop));
                    }
                }
            }
        }
    }
}
