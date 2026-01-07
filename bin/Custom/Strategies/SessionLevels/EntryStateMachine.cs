using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;

namespace NinjaTrader.NinjaScript.Strategies
{
    // v1.14.40: Entry State Machine - Extracted from ManageEntryA_Plus
    // Handles entry logic flow: triggers, confirmation, order management
    public class EntryStateMachine
    {
        private SessionLevelsStrategy strategy;

        public EntryStateMachine(SessionLevelsStrategy strategy)
        {
            this.strategy = strategy;
        }



        /// <summary>
        /// Checks trading mode guards (Paused, LongOnly, ShortOnly)
        /// </summary>
        /// <returns>true if trading is allowed, false if should skip</returns>
        public bool CheckTradingModeGuards()
        {
            // If paused, cancel any working orders and block all entry logic
            if (strategy.currentTradingMode == TradingMode.Paused)
            {
                if (strategy.currentEntryState == EntryState.workingOrder && strategy.entryOrder != null &&
                    (strategy.entryOrder.OrderState == OrderState.Working || strategy.entryOrder.OrderState == OrderState.Accepted))
                {
                    strategy.Log(strategy.Time[0] + " PAUSED: Cancelling working entry order");
                    strategy.CancelOrderWrapper(strategy.entryOrder);
                }
                return false; // Block all entry logic when paused
            }
            
            // Cancel orders that go against direction mode
            if (strategy.currentEntryState == EntryState.workingOrder && strategy.entryOrder != null &&
                (strategy.entryOrder.OrderState == OrderState.Working || strategy.entryOrder.OrderState == OrderState.Accepted))
            {
                // LongOnly but we have a Short order pending
                if (strategy.currentTradingMode == TradingMode.LongOnly && strategy.isShortSetup)
                {
                    strategy.Log(strategy.Time[0] + " LONGONLY: Cancelling Short entry order");
                    strategy.CancelOrderWrapper(strategy.entryOrder);
                    return false;
                }
                // ShortOnly but we have a Long order pending
                if (strategy.currentTradingMode == TradingMode.ShortOnly && !strategy.isShortSetup)
                {
                    strategy.Log(strategy.Time[0] + " SHORTONLY: Cancelling Long entry order");
                    strategy.CancelOrderWrapper(strategy.entryOrder);
                    return false;
                }
            }
            
            // Check trading mode before processing new entries
            if (strategy.currentEntryState == EntryState.Idle)
            {
                // If paused, don't look for new setups
                if (strategy.currentTradingMode == TradingMode.Paused)
                {
                    return false;
                }
                
                // Check direction filter for new entries
                if (strategy.currentTradingMode == TradingMode.LongOnly && strategy.isShortSetup)
                {
                    if (strategy.lastFilterReason != "Skipped: Long Only Mode") 
                    { 
                        strategy.lastFilterReason = "Skipped: Long Only Mode"; 
                        strategy.lastFilterTime = DateTime.Now; 
                    }
                    return false; // Skip short setups
                }
                if (strategy.currentTradingMode == TradingMode.ShortOnly && !strategy.isShortSetup)
                {
                    if (strategy.lastFilterReason != "Skipped: Short Only Mode") 
                    { 
                        strategy.lastFilterReason = "Skipped: Short Only Mode"; 
                        strategy.lastFilterTime = DateTime.Now; 
                    }
                    return false; // Skip long setups
                }
            }
            
            return true; // Trading allowed
        }

        /// <summary>
        /// Handles VWAP mitigation retry logic
        /// </summary>
        /// <returns>true if retry was handled and should return early</returns>
        public bool HandleVwapMitigationRetry()
        {
            // TODO: Extract from ManageEntryA_Plus lines 2793-2850
            return false;
        }

        /// <summary>
        /// Updates anchor if price makes new High (Short) or new Low (Long)
        /// </summary>
        public void UpdateAnchorIfNeeded()
        {
            // SHORT: Re-anchor if price makes new high
            if (strategy.isShortSetup && strategy.High[0] >= strategy.setupAnchorPrice + strategy.TickSize)
            {
                strategy.setupAnchorPrice = strategy.High[0];
                
                // v1.14.56: Only calculate VWAP Adhoc for INTERNAL levels
                // For EXTERNAL levels (extremes), we use the Global VWAP
                if (strategy.isInternalLevel)
                {
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
                }
                
                strategy.Log(string.Format("RE-ANCHOR: New High @ {0} (Setup: {1})", strategy.setupAnchorPrice, strategy.setupLevelName));
            }
            
            // LONG: Re-anchor if price makes new low
            if (!strategy.isShortSetup && strategy.Low[0] <= strategy.setupAnchorPrice - strategy.TickSize)
            {
                strategy.setupAnchorPrice = strategy.Low[0];
                
                // v1.14.56: Only calculate VWAP Adhoc for INTERNAL levels
                if (strategy.isInternalLevel)
                {
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
                }
                
                strategy.Log(string.Format("RE-ANCHOR: New Low @ {0} (Setup: {1})", strategy.setupAnchorPrice, strategy.setupLevelName));
            }
        }

        /// <summary>
        /// Handles internal level invalidation when touching external levels
        /// </summary>
        /// <returns>true if invalidated and should return early</returns>
        public bool HandleInternalInvalidation()
        {
            // Only check if internal level and waiting for confirmation
            if (!strategy.isInternalLevel || strategy.currentEntryState != EntryState.WaitingForConfirmation)
                return false;
                
            bool touchedExternal = false;
            
            // SHORT internal: Check if touched external High above
            if (strategy.isShortSetup && strategy.externalLevelAbove > 0)
            {
                if (strategy.High[0] >= strategy.externalLevelAbove)
                {
                    touchedExternal = true;
                    strategy.Log(string.Format("INVALIDATED: Touched external {0} @ {1}", strategy.externalLevelAboveName, strategy.externalLevelAbove));
                }
            }
            
            // LONG internal: Check if touched external Low below
            if (!strategy.isShortSetup && strategy.externalLevelBelow > 0)
            {
                if (strategy.Low[0] <= strategy.externalLevelBelow)
                {
                    touchedExternal = true;
                    strategy.Log(string.Format("INVALIDATED: Touched external {0} @ {1}", strategy.externalLevelBelowName, strategy.externalLevelBelow));
                }
            }
            
            if (touchedExternal)
            {
                // Mark bar to prevent re-triggering
                strategy.lastInvalidationBar = strategy.CurrentBar;
                
                // Cancel entry order if exists
                if (strategy.entryOrder != null && 
                    (strategy.entryOrder.OrderState == OrderState.Working || strategy.entryOrder.OrderState == OrderState.Accepted))
                {
                    strategy.CancelOrderWrapper(strategy.entryOrder);
                }
                
                // Reset to Idle
                strategy.currentEntryState = EntryState.Idle;
                strategy.isInternalLevel = false;
                
                // AUTO-TRIGGER on external level after invalidation
                string externalName = strategy.isShortSetup ? strategy.externalLevelAboveName : strategy.externalLevelBelowName;
                double externalPrice = strategy.isShortSetup ? strategy.externalLevelAbove : strategy.externalLevelBelow;
                
                if (externalPrice > 0 && !string.IsNullOrEmpty(externalName))
                {
                    strategy.Log(string.Format("AUTO-TRIGGER: Switching to external level {0} @ {1}", externalName, externalPrice));
                    
                    // Setup new trigger on external level
                    if (strategy.isShortSetup)
                    {
                        // SHORT on external High
                        strategy.triggerTag = "TriggerShort_" + strategy.Time[0].Ticks;
                        strategy.triggerBar = strategy.CurrentBar;
                        strategy.DrawTriggerLabel(strategy.triggerTag, true, 0, strategy.High[0]);
                        
                        strategy.currentEntryState = EntryState.WaitingForConfirmation;
                        strategy.visualConfirmationDone = false;
                        strategy.isShortSetup = true;
                        strategy.setupAnchorPrice = strategy.High[0];
                        strategy.setupLevelName = externalName;
                        strategy.setupLevelTime = strategy.Time[0];
                        strategy.validatedTargetPrice = 0;
                        strategy.cachedOppositeLevel = null;
                        strategy.oppositeSearchDone = false;
                        
                        strategy.isInternalLevel = false;
                        
                        // Reset VWAP
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
                        // LONG on external Low
                        strategy.triggerTag = "TriggerLong_" + strategy.Time[0].Ticks;
                        strategy.triggerBar = strategy.CurrentBar;
                        strategy.DrawTriggerLabel(strategy.triggerTag, false, 0, strategy.Low[0]);
                        
                        strategy.currentEntryState = EntryState.WaitingForConfirmation;
                        strategy.visualConfirmationDone = false;
                        strategy.isShortSetup = false;
                        strategy.setupAnchorPrice = strategy.Low[0];
                        strategy.setupLevelName = externalName;
                        strategy.setupLevelTime = strategy.Time[0];
                        strategy.validatedTargetPrice = 0;
                        strategy.cachedOppositeLevel = null;
                        
                        strategy.isInternalLevel = false;
                        
                        // Reset VWAP
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
                }
                
                return true; // Invalidated
            }
            
            return false;
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
                // Block only if date is same AND time is still before session end (or session end is 0)
                if (lvl.StartTime.Date == strategy.Time[0].Date)
                {
                    // Only block if session hasn't finished yet
                    // Use ActualSessionEnd (TimeSpan) to check against current TimeOfDay
                    if (strategy.Time[0].TimeOfDay <= lvl.ActualSessionEnd)
                         continue;
                }

                // v1.10.25: Check if max retries exceeded
                if (lvl.EntryAttempts >= strategy.MaxRetriesPerLevel)
                    continue;
                
                // v1.10.29: Skip levels touched at startup
                if (strategy.skippedLevelsAtStartup.Contains(lvl.Name))
                    continue;

                // If level is mitigated exactly NOW
                if (lvl.IsMitigated && lvl.MitigationTime == strategy.Time[0])
                {
                    // If already waiting, check if different level
                    if (strategy.currentEntryState == EntryState.WaitingForConfirmation)
                    {
                        if (lvl.Name == strategy.setupLevelName)
                            continue;
                        else
                            strategy.Log(strategy.Time[0] + " SWITCH: New Trigger on " + lvl.Name + " overrides " + strategy.setupLevelName);
                    }
                        
                    // TRIGGER CONFIRMED
                    if (!lvl.IsResistance)
                    {
                        // Long Setup
                        strategy.triggerTag = "TriggerLong_" + strategy.Time[0].Ticks;
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
                        strategy.Log(string.Format("{0} ENTRY ATTEMPT #{1}/{2} on {3}", strategy.Time[0], lvl.EntryAttempts, strategy.MaxRetriesPerLevel, lvl.Name));
                        
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
                        strategy.triggerTag = "TriggerShort_" + strategy.Time[0].Ticks;
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
                        strategy.Log(string.Format("{0} ENTRY ATTEMPT #{1}/{2} on {3}", strategy.Time[0], lvl.EntryAttempts, strategy.MaxRetriesPerLevel, lvl.Name));
                        
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

                    break; // Only take one trigger at a time
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
                if (strategy.isShortSetup)
                {
                    strategy.setupAnchorPrice = strategy.High[0];
                    strategy.triggerTag = "RetryShort_" + strategy.Time[0].Ticks;
                    strategy.triggerBar = strategy.CurrentBar;
                    strategy.DrawTriggerLabel(strategy.triggerTag, true, 0, strategy.High[0]);
                }
                else
                {
                    strategy.setupAnchorPrice = strategy.Low[0];
                    strategy.triggerTag = "RetryLong_" + strategy.Time[0].Ticks;
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
						double tp2Target = strategy.GetOppositeLevelPrice(setupLevelName, setupLevelTime, setupAnchorPrice, true);
						if (tp2Target == 0) tp2Target = strategy.GetCurrentLowVWAP();
						strategy.validatedTargetPrice = tp2Target;

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

							// DYNAMIC SIZING (v1.8.0)
							int dynamicQuantity = strategy.CalculateDynamicQuantity(limitPrice, projectedStop);

							string entryTag = string.Format("EntryA+_Short_{0:D2}", strategy.currentVwapNumber);

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
							strategy.entryOrder = strategy.SubmitOrderUnmanaged(0, OrderAction.SellShort, OrderType.Limit, dynamicQuantity, limitPrice, 0, "", entryTag);

							if (strategy.entryOrder == null)
							{
								strategy.currentEntryState = EntryState.Idle; // Revert if submit failed
								strategy.Log("CRITICAL: Order Submit Failed. Reverting State to Idle.");
								return;
							}
							
							strategy.Log(strategy.Time[0] + " Order Submitted (Short Consolidated). Qty=" + dynamicQuantity);
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
						// DEBUG waiting
						if (strategy.CurrentBar % 10 == 0) // Limit spam
							strategy.Log(string.Format("{0} | WAITING SHORT: High[1]={1:F2} VWAP={2:F2} Req={3:F2} ValidVWAP={4} Anchor={5}", 
								strategy.Time[0], strategy.High[1], setupVWAP, (setupVWAP - TickSize), strategy.isValidVWAP(setupVWAP), setupAnchorPrice));
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
						double tp2Target = strategy.GetOppositeLevelPrice(setupLevelName, setupLevelTime, setupAnchorPrice, false);
						if (tp2Target == 0) tp2Target = strategy.GetCurrentHighVWAP();
						strategy.validatedTargetPrice = tp2Target;

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

							int dynamicQuantity = strategy.CalculateDynamicQuantity(limitPrice, projectedStop);

							string entryTag = string.Format("EntryA+_Long_{0:D2}", strategy.currentVwapNumber);

							if (!strategy.CheckChartLag())
							{
								string msg = "Skipped: Network Lag Detected";
								strategy.Log(strategy.Time[0] + " Long order BLOCKED: " + msg);
								strategy.lastFilterReason = msg; strategy.lastFilterTime = DateTime.Now;
								return;
							}

							// v1.14.61: Fix Race Condition - Set State BEFORE Order Submission
							strategy.currentEntryState = EntryState.workingOrder;
							strategy.entryOrder = strategy.SubmitOrderUnmanaged(0, OrderAction.Buy, OrderType.Limit, dynamicQuantity, limitPrice, 0, "", entryTag);
							
							if (strategy.entryOrder == null)
							{
								strategy.currentEntryState = EntryState.Idle; // Revert if submit failed
								strategy.Log("CRITICAL: Order Submit Failed. Reverting State to Idle.");
								return;
							}

							strategy.Log(strategy.Time[0] + " Order Submitted (Long Consolidated). Qty=" + dynamicQuantity);
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

                    strategy.currentEntryState = EntryState.Idle;
                    strategy.setupLevelName = "";
                    return;
                }
            }

            // 2. CHECK IF ALREADY FILLED (Sync fallback)
            bool anyFilled = (strategy.entryOrder.OrderState == OrderState.Filled || strategy.entryOrder.OrderState == OrderState.PartFilled);
            if (anyFilled)
            {
                strategy.Log(strategy.Time[0] + " SYNC: Order Filled but State was Working. Forcing InPosition.");
                strategy.currentEntryState = EntryState.PositionActive;
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
            if (strategy.entryOrder.OrderState == OrderState.Working)
            {
                double projectedStop = isShortSetup ? (setupAnchorPrice + TickSize) : (setupAnchorPrice - TickSize);
                int newQuantity = strategy.CalculateDynamicQuantity(currentVWAP, projectedStop);

                bool priceChanged = Math.Abs(strategy.entryOrder.LimitPrice - currentVWAP) >= TickSize;
                bool quantityChanged = newQuantity != strategy.entryOrder.Quantity;

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
