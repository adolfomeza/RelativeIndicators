using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;

namespace NinjaTrader.NinjaScript.Strategies
{
    // v1.14.39: Dedicated Order Protection & Target Manager
    public class OrderProtectionManager
    {
        private SessionLevelsStrategy strategy;
        
        // State extracted from Strategy
        public bool SlOrderCreatedThisEntry { get; set; } = false;
        
        // Internal Levels State
        public bool IsInternalLevel { get; private set; } = false;
        public string ExternalLevelAboveName { get; private set; } = "";
        public string ExternalLevelBelowName { get; private set; } = "";

        // Cache for Opposite Level Search
        private SessionLevel cachedOppositeLevel = null;
        private bool oppositeSearchDone = false;
        
        public OrderProtectionManager(SessionLevelsStrategy strategy)
        {
            this.strategy = strategy;
        }

        public void ResetEntryState()
        {
            SlOrderCreatedThisEntry = false;
            strategy.Log(strategy.Time[0] + " [DEBUG] OrderProtectionManager: ResetEntryState CALLED (Clearing cached levels)");
            cachedOppositeLevel = null;
            oppositeSearchDone = false;
            IsInternalLevel = false;
            
            // v1.14.40: Clear stale order references from strategy to prevent Ghost SL bug
            // This ensures new trades don't inherit old SL/TP references
            strategy.stopOrder = null;
            strategy.tp1Order = null;
            strategy.tp2Order = null;
            strategy.Log("PROTECTION_RESET: Cleared order references for new trade");
        }

        // Helper to access ActiveLevels via Property if available, or assume strategy provides access logic
        private List<SessionLevel> GetActiveLevels()
        {
            // Assuming strategy.ActiveLevels is exposed as public property
            return strategy.activeLevels;
        }

        // =============================================================================
        // LOGIC MOVED FROM STRATEGY: EnsureProtection & SubmitProtectionOrders
        // =============================================================================

    	// REFACTORED EnsureProtection (v1.7.17) - Consolidated Split Handling
	    // v1.15.26: Modified to accept separate TP1 and TP2 prices to fix MCL bug where both TPs had same price
	    public void EnsureProtection(string direction, string entrySignalName, int filledQty,
                                     int currentVwapNumber, bool isShortSetup, string setupLevelName,
                                     DateTime setupLevelTime, double setupAnchorPrice, double validatedTp1Price, double validatedTp2Price)
	    {
		    // v1.15.14: PHANTOM POSITION CHECK - Modified to handle replay sync delays
		    // During replay, Account.Positions may not sync instantly with Position.Quantity
		    // Only block if Position.Quantity is also 0 (true phantom)
		    bool hasRealPosition = false;
		    bool positionQuantityValid = Math.Abs(strategy.Position.Quantity) > 0;

		    try
		    {
			    foreach (var pos in strategy.Account.Positions)
			    {
				    if (pos.Instrument.FullName == strategy.Instrument.FullName && pos.MarketPosition != MarketPosition.Flat)
				    {
					    hasRealPosition = true;
					    break;
				    }
			    }
		    }
		    catch (Exception ex) { strategy.Log("PHANTOM CHECK ERROR (EnsureProtection): " + ex.Message); }

		    // v1.15.14: Only block if BOTH Position.Quantity AND Account.Positions show 0
		    // If Position.Quantity > 0, trust it (especially important for replay)
		    if (!hasRealPosition && !positionQuantityValid)
		    {
			    strategy.Log(strategy.Time[0] + " PHANTOM PROTECTION BLOCKED: Strategy shows position but Account has 0 AND Position.Quantity=0. Skipping EnsureProtection.");
			    return; // Don't create SL/TP for phantom positions
		    }

		    // Log when proceeding despite Account.Positions mismatch (replay sync delay)
		    if (!hasRealPosition && positionQuantityValid)
		    {
			    strategy.Log(strategy.Time[0] + " REPLAY_SYNC_DELAY: Account.Positions=0 but Position.Quantity=" + strategy.Position.Quantity + ". Proceeding with protection (replay sync delay).");
		    }
		    
		    // v1.13.10: DIAGNOSTIC LOGS
		    strategy.Log($"DEBUG_PROTECTION: EnsureProtection CALLED - Direction={direction} FilledQty={filledQty} Position.Qty={strategy.Position.Quantity} Position.MarketPosition={strategy.Position.MarketPosition}");
		    
            // Initialization of Trade VWAP if not active
		    if (!strategy.IsTradeVwapActive)
		    {
			    if (isShortSetup)
			    {
                    if (strategy.vwapCalc != null) strategy.vwapCalc.InitTradeVWAP(true);
			    }
			    else
			    {
				    if (strategy.vwapCalc != null) strategy.vwapCalc.InitTradeVWAP(false);
			    }
			    strategy.IsTradeVwapActive = true;
			    strategy.Log(strategy.Time[0] + " TRADE VWAP: Initialized (Managed)");
		    }
		    
		    // DYNAMIC BUCKET ALLOCATION
		    int totalPositionQty = Math.Abs(strategy.Position.Quantity);
		    int totalTp1Target = (totalPositionQty + 1) / 2;
		    int totalTp2Target = totalPositionQty - totalTp1Target;

		    int neededTp1 = totalTp1Target - strategy.protectedTp1Qty;
		    if (neededTp1 < 0) neededTp1 = 0;

		    int neededTp2 = totalTp2Target - strategy.protectedTp2Qty;
		    if (neededTp2 < 0) neededTp2 = 0;

		    int forTp1 = Math.Min(neededTp1, filledQty);
		    int forTp2 = Math.Min(neededTp2, filledQty - forTp1);

		    strategy.Log(string.Format("   -> Protection Alloc: Filled={0} | ForTP1={1} (Need:{2}) | ForTP2={3} (Need:{4})", filledQty, forTp1, neededTp1, forTp2, neededTp2));

		    // v1.15.26: CRITICAL FIX - Pass separate prices for TP1 and TP2
		    // BUG: Previously both calls used same validatedTargetPrice, causing TP2 to change to TP1's price
		    // Example: MCL Jan 6 2025 4:26am - TP2 changed from 74.39 to 73.83 (TP1's price)
		    // FIX: Pass validatedTp1Price to TP1 and validatedTp2Price to TP2
            
            // v1.15.40: ROUTING based on Exit Strategy
            if (strategy.ExitStrategy == ExitStrategyType.Ladder)
            {
                // Ladder Logic
                EnsureLadderProtection(direction, entrySignalName, filledQty, currentVwapNumber, isShortSetup, setupAnchorPrice);
            }
            else
            {
                // Standard Logic (TP1/TP2)
    		    if (neededTp1 > 0)
	    		    SubmitProtectionOrders(direction, true, neededTp1, currentVwapNumber, isShortSetup, setupLevelName, setupLevelTime, setupAnchorPrice, validatedTp1Price);
    
	    	    if (neededTp2 > 0)
		    	    SubmitProtectionOrders(direction, false, neededTp2, currentVwapNumber, isShortSetup, setupLevelName, setupLevelTime, setupAnchorPrice, validatedTp2Price);
            }


        }


        public void EnsureLadderProtection(string direction, string entrySignalName, int filledQty,
                                         int currentVwapNumber, bool isShortSetup, double setupAnchorPrice)
        {
            // v1.15.40: LADDER EXIT LOGIC
            // Goal: Scale out 1 contract at 1R, 1 contract at 2R, etc.
            
            try 
            {
                int totalPositionQty = Math.Abs(strategy.Position.Quantity);
                if (totalPositionQty == 0) return;

                double avgEntry = strategy.Position.AveragePrice;
                
                // 1. Calculate Risk (R)
                // SL is at setupAnchorPrice +/- 1 tick (or 5 ticks fallback)
                double slPrice = isShortSetup ? 
                    (setupAnchorPrice + strategy.TickSize) : 
                    (setupAnchorPrice - strategy.TickSize);
                
                // Fallback SL logic matches SubmitProtectionOrders
                double fallbackDist = (strategy.StopLossTicks * strategy.TickSize);
                if (isShortSetup && slPrice <= avgEntry) slPrice = avgEntry + fallbackDist;
                if (!isShortSetup && slPrice >= avgEntry) slPrice = avgEntry - fallbackDist;
                
                // Round SL
                slPrice = strategy.Instrument.MasterInstrument.RoundToTickSize(slPrice);

                double riskAmount = Math.Abs(avgEntry - slPrice);
                // Ensure min risk to prevent tiny targets
                if (riskAmount < 5 * strategy.TickSize) riskAmount = 5 * strategy.TickSize;

                strategy.Log(string.Format("LADDER_CALC: Entry={0} SL={1} Risk(1R)={2}", avgEntry, slPrice, riskAmount));

                // 2. Submit/Update STOP LOSS (Single Order)
                // We use standard 'stopOrder' for the entire position
                if (strategy.stopOrder == null && !SlOrderCreatedThisEntry)
                {
                     string slTag = string.Format("{0}_{1:D2}", isShortSetup ? "SL_Short" : "SL_Long", currentVwapNumber);
				     OrderAction slAction = isShortSetup ? OrderAction.BuyToCover : OrderAction.Sell;
                     strategy.stopOrder = strategy.SubmitOrderUnmanagedWrapper(0, slAction, OrderType.StopMarket, totalPositionQty, 0, slPrice, "", slTag);
                     SlOrderCreatedThisEntry = true;
                     strategy.Log("LADDER_SL: Created " + slTag + " @ " + slPrice + " Qty=" + totalPositionQty);
                }
                else if (strategy.stopOrder != null && 
                        (strategy.stopOrder.OrderState == OrderState.Working || strategy.stopOrder.OrderState == OrderState.Accepted) &&
                         strategy.stopOrder.Quantity != totalPositionQty)
                {
                     strategy.ChangeOrderWrapper(strategy.stopOrder, totalPositionQty, 0, slPrice);
                     strategy.Log("LADDER_SL: Updated Qty to " + totalPositionQty);
                }

                // 3. Submit LADDER TARGETS (1 per Contract)
                // We want 1 order per unit of quantity
                // Ex: Qty=3 -> TP #1 @ 1R, TP #2 @ 2R, TP #3 @ 3R
                
                // Sync List: Remove Filled/Cancelled orders
                strategy.ladderOrders.RemoveAll(o => o == null || 
                    o.OrderState == OrderState.Filled || 
                    o.OrderState == OrderState.Cancelled || 
                    o.OrderState == OrderState.Rejected);

                int currentLadderCount = strategy.ladderOrders.Count;
                int neededLadders = totalPositionQty - currentLadderCount;

                if (neededLadders > 0)
                {
                    // Start index based on how many we already have (to continue sequence 1R, 2R...)
                    // Actually, simpler: We always want Target 1 @ 1R, Target 2 @ 2R relative to Entry
                    // But we only submit NEW ones.
                    // If we have 2 active orders, we assume they are covering the "last" contracts (ex: Target 2 and 3). 
                    // Or do we strictly bind? "Order for 1R", "Order for 2R".
                    // STRATEGY: Create missing orders for the NEXT available slots.
                    // If we have 0 orders, create for index 1, 2, 3.
                    // If we have 1 order, create for index 2, 3.
                    
                    int startStep = currentLadderCount + 1; // 1-based step
                    
                    for (int i = 0; i < neededLadders; i++)
                    {
                        int step = startStep + i; // 1, 2, 3...
                        double rewardDist = riskAmount * step;
                        double targetPrice = isShortSetup ? (avgEntry - rewardDist) : (avgEntry + rewardDist);
                        targetPrice = strategy.Instrument.MasterInstrument.RoundToTickSize(targetPrice);
                        
                        string tpTag = string.Format("LadderTP_{0}_{1}", step, currentVwapNumber);
                        OrderAction tpAction = isShortSetup ? OrderAction.BuyToCover : OrderAction.Sell;
                        
                        // Submit for 1 Qty
                        Order ladderOrd = strategy.SubmitOrderUnmanagedWrapper(0, tpAction, OrderType.Limit, 1, targetPrice, 0, "", tpTag);
                        strategy.ladderOrders.Add(ladderOrd);
                        
                        strategy.Log(string.Format("LADDER_TP: Created #{0} ({1}R) @ {2}", step, step, targetPrice));
                    }
                }
            }
            catch (Exception ex)
            {
                strategy.Log("LADDER ERROR: " + ex.Message);
            }
        }



        private void SubmitProtectionOrders(string direction, bool isTp1, int qty,
                                            int currentVwapNumber, bool isShortSetup, string setupLevelName,
                                            DateTime setupLevelTime, double setupAnchorPrice, double validatedTpPrice)
	    {
		    // Recover orphan orders logic omitted/simplified (Strategy should handle this in adoption, or we assume sync)
            // Implementation focus: Calculation and Submission
		    
		    // 2. Determine Targets (TP1 vs TP2)
		    double avgEntry = strategy.Position.AveragePrice; 
		    
		    double targetGlobalVWAP = 0;
		    double targetZoneOpposite = 0;
		    double slPrice = 0;
		    
            // Access Series via Strategy (using [0] index)
            // Note: Strategy series access might require strategy.Close[0]
		    double lastPrice = strategy.Close[0];
		    double fallbackTargetDist = (strategy.StopLossTicks * strategy.TickSize) * 2.0;

		    if (isShortSetup)
		    {
			    // SL Calculation
			    slPrice = setupAnchorPrice + strategy.TickSize;
			    if (slPrice <= lastPrice) slPrice = lastPrice + (5 * strategy.TickSize); 
			    
			    // VWAP Target
			    if (strategy.IsTradeVwapActive && strategy.vwapCalc != null)
				    targetGlobalVWAP = strategy.vwapCalc != null ? strategy.vwapCalc.GetTradeVWAPCurrentValue() : 0;
                else
				    targetGlobalVWAP = strategy.vwapCalc != null ? strategy.vwapCalc.GetCurrentLowVWAP() : 0; 
			    
			    // v1.14.57: DIAGNOSTIC LOGS
			    strategy.Log(string.Format("TP_DIAG (Short): IsTradeVwapActive={0} | GetCurrentLowVWAP={1} | targetGlobalVWAP={2} | avgEntry={3}",
			        strategy.IsTradeVwapActive, 
			        strategy.vwapCalc != null ? strategy.vwapCalc.GetCurrentLowVWAP() : -1,
			        targetGlobalVWAP, avgEntry));
			    
			    // Opposite Level Target
			    if (cachedOppositeLevel != null) targetZoneOpposite = cachedOppositeLevel.Price;
			    else 
                {
                    SessionLevel found;
                    targetZoneOpposite = GetOppositeLevelPrice(setupLevelName, setupLevelTime, GetActiveLevels(), cachedOppositeLevel, oppositeSearchDone, out found);
                    // Update cache
                    if (found != null) cachedOppositeLevel = found;
                    if (found == null) oppositeSearchDone = true;
                }

			    // v1.15.26: Use validatedTpPrice (will be TP1 or TP2 price depending on isTp1)
			    if (validatedTpPrice > 0)
			    {
				    targetZoneOpposite = validatedTpPrice;
				    strategy.Log("FORCE TARGET: Using Validated TP Price: " + validatedTpPrice);
			    }

			    if (targetZoneOpposite >= avgEntry) targetZoneOpposite = 0; 
			    if (targetGlobalVWAP >= avgEntry) targetGlobalVWAP = 0; 
			    
			    if (targetZoneOpposite <= 0) targetZoneOpposite = avgEntry - fallbackTargetDist;
			    if (targetGlobalVWAP <= 0) targetGlobalVWAP = avgEntry - fallbackTargetDist;
		    }
		    else
		    {
			    // Long Setup
			    slPrice = setupAnchorPrice - strategy.TickSize;
			    if (slPrice >= lastPrice) slPrice = lastPrice - (5 * strategy.TickSize); 
			    
			    if (strategy.IsTradeVwapActive && strategy.vwapCalc != null)
				    targetGlobalVWAP = strategy.vwapCalc != null ? strategy.vwapCalc.GetTradeVWAPCurrentValue() : 0;
			    else
				    targetGlobalVWAP = strategy.vwapCalc != null ? strategy.vwapCalc.GetCurrentHighVWAP() : 0; 

			    if (cachedOppositeLevel != null) targetZoneOpposite = cachedOppositeLevel.Price;
			    else 
                {
                    SessionLevel found;
                    targetZoneOpposite = GetOppositeLevelPrice(setupLevelName, setupLevelTime, GetActiveLevels(), cachedOppositeLevel, oppositeSearchDone, out found);
                    if (found != null) cachedOppositeLevel = found;
                    if (found == null) oppositeSearchDone = true;
                }
			    
			    // v1.15.26: Use validatedTpPrice (will be TP1 or TP2 price depending on isTp1)
			    if (validatedTpPrice > 0)
			    {
				    targetZoneOpposite = validatedTpPrice;
				    strategy.Log("FORCE TARGET: Using Validated TP Price: " + validatedTpPrice);
			    }

			    if (targetZoneOpposite <= avgEntry) targetZoneOpposite = 0; 
			    if (targetGlobalVWAP <= avgEntry) targetGlobalVWAP = 0; 

			    if (targetZoneOpposite <= 0) targetZoneOpposite = avgEntry + fallbackTargetDist;
			    if (targetGlobalVWAP <= 0) targetGlobalVWAP = avgEntry + fallbackTargetDist;
		    }
		    
		    if (targetGlobalVWAP <= 0) targetGlobalVWAP = avgEntry;
		    if (targetZoneOpposite <= 0) targetZoneOpposite = avgEntry;

		    double tp1Price = targetGlobalVWAP; 
		    double tp2Price = targetZoneOpposite; 
		    
		    // Validate TP2
		    if (isShortSetup && tp2Price >= avgEntry)
			    tp2Price = avgEntry - fallbackTargetDist;
		    if (!isShortSetup && tp2Price <= avgEntry)
			    tp2Price = avgEntry + fallbackTargetDist;
			    
		    double myTpPrice = isTp1 ? tp1Price : tp2Price;
		    string myTpTag = isTp1 ? "TP1" : "TP2";
		    
		    myTpPrice = strategy.Instrument.MasterInstrument.RoundToTickSize(myTpPrice);
		    slPrice = strategy.Instrument.MasterInstrument.RoundToTickSize(slPrice);

		    if (isTp1) 
            { 
                strategy.activeTp1Price = myTpPrice; 
                // TP1 is Dynamic (VWAP), so we WANT it to update in the panel to show current potential
                strategy.tradeOriginalTp1Price = myTpPrice; 
            } 
		    else
            {
                strategy.activeTp2Price = myTpPrice;
                // TP2 is Static (Zone), but moves to BE. We LOCK it to show the initial Target/Risk in panel.
                if (strategy.tradeOriginalTp2Price == 0) strategy.tradeOriginalTp2Price = myTpPrice;

                // v1.15.32: CRITICAL FIX - Persist TP2 price back to strategy so ManagePositionExit() uses it
                // Without this, ManagePositionExit() sees validatedTp2Price=0 and falls back to VWAP
                if (strategy.validatedTp2Price <= 0)
                {
                    strategy.validatedTp2Price = myTpPrice;
                    strategy.Log("TP2_PERSIST: Saved validatedTp2Price=" + myTpPrice + " to strategy");
                }
            } 

		    // DEBUG TARGETS
		    // v1.15.26: Show validatedTpPrice (will be TP1 or TP2 depending on which is being created)
		    strategy.Log(string.Format("TP CALC ({0}): Entry={1} | GlobalVWAP={2} | ZoneOpp={3} (ValTP={4}) | TP1={5} TP2={6} | Selected={7}",
			    direction, avgEntry, targetGlobalVWAP, targetZoneOpposite, validatedTpPrice, tp1Price, tp2Price, myTpPrice));

		    // v1.9.0: SINGLE-SL CREATION/UPDATE
		    try
		    {
			    int totalPositionQty = Math.Abs(strategy.Position.Quantity);
			    
			    // Check if SL already exists
			    Order existingSL = strategy.stopOrder;
			    Order existingTP = isTp1 ? strategy.tp1Order : strategy.tp2Order;
			    
			    bool shouldUpdateSL = (existingSL != null && (existingSL.OrderState == OrderState.Working || existingSL.OrderState == OrderState.Accepted));
			    bool shouldUpdateTP = (existingTP != null && (existingTP.OrderState == OrderState.Working || existingTP.OrderState == OrderState.Accepted));
			    
			    // STEP 1: Handle STOP LOSS (single for entire position)
			    bool slAlreadyActive = (existingSL != null && 
				    (existingSL.OrderState == OrderState.Working || 
				     existingSL.OrderState == OrderState.Accepted ||
				     existingSL.OrderState == OrderState.Submitted));
			    
			    if (existingSL != null && !slAlreadyActive)
			    {
				    strategy.Log(string.Format("SL CLEANUP: Clearing stale reference (State={0})", existingSL.OrderState));
				    strategy.stopOrder = null;
                    existingSL = null; 
			    }
			    
			    // use Local property SlOrderCreatedThisEntry
			    if (existingSL == null && !SlOrderCreatedThisEntry)
			    {
				    string slTag = string.Format("{0}_{1:D2}", direction == "Short" ? "SL_Short" : "SL_Long", currentVwapNumber);
				    OrderAction slAction = direction == "Short" ? OrderAction.BuyToCover : OrderAction.Sell;
				    
				    strategy.Log(string.Format("SL_CREATE_DEBUG (MGR): Instrument={0} Direction={1} Tag={2} Action={3} Price={4} Qty={5}",
					    strategy.Instrument.FullName, direction, slTag, slAction, slPrice, totalPositionQty));
				    
				    strategy.stopOrder = strategy.SubmitOrderUnmanagedWrapper(0, slAction, OrderType.StopMarket, totalPositionQty, 0, slPrice, "", slTag);
				    SlOrderCreatedThisEntry = true; 
				    
				    strategy.tradeRiskUSD = Math.Abs(avgEntry - slPrice) * totalPositionQty * strategy.Instrument.MasterInstrument.PointValue;
				    strategy.Log(string.Format("SL CREATED: {0} @ {1} Qty={2} Risk=${3:F2}", slTag, slPrice, totalPositionQty, strategy.tradeRiskUSD));
			    }
			    else if (SlOrderCreatedThisEntry && existingSL != null && 
				    (existingSL.OrderState == OrderState.Working || existingSL.OrderState == OrderState.Accepted) &&
				    existingSL.Quantity != totalPositionQty)
			    {
				    // Update quantity (Partial fill scenario)
				    strategy.Log(string.Format("SL UPDATE (Partial Fill): Old Qty={0} New Qty={1}", existingSL.Quantity, totalPositionQty));
				    strategy.ChangeOrderWrapper(existingSL, totalPositionQty, 0, slPrice);
			    }
			    else if (SlOrderCreatedThisEntry)
			    {
				    strategy.Log("SL SKIPPED: Already created in current entry (duplicate prevention)");
			    }
			    
			    // STEP 2: Handle TAKE PROFIT
			    int tpQty = isTp1 ? (strategy.protectedTp1Qty + qty) : (strategy.protectedTp2Qty + qty);
			    
			    Order currentTP = isTp1 ? strategy.tp1Order : strategy.tp2Order;
			    bool tpAlreadyActive = (currentTP != null && 
				    (currentTP.OrderState == OrderState.Working || 
				     currentTP.OrderState == OrderState.Accepted ||
				     currentTP.OrderState == OrderState.Submitted ||
				     currentTP.OrderState == OrderState.PartFilled));
                
                // Limpieza de referencias obsoletas (Stale Reference Cleanup)
                if (currentTP != null && !tpAlreadyActive)
                {
                     // Si la orden existe pero no está activa (ej. Cancelled/Filled de flujos anteriores que no se limpiaron),
                     // la limpiamos para permitir una creación limpia.
                     if (isTp1) strategy.tp1Order = null;
                     else strategy.tp2Order = null;
                     currentTP = null;
                }

			    if (tpAlreadyActive)
			    {
                    // v1.15.10: ALWAYS USE CHANGEORDER - It's more reliable than Cancel+Recreate
                    // The issue was not ChangeOrder itself, but OnOrderUpdate not updating references properly
                    if (currentTP.Quantity != tpQty || Math.Abs(currentTP.LimitPrice - myTpPrice) > double.Epsilon)
                    {
				        strategy.Log(string.Format("TP UPDATE ({0}): Old Qty={1}/Price={2} -> New Qty={3}/Price={4} (ID={5})",
					        myTpTag, currentTP.Quantity, currentTP.LimitPrice, tpQty, myTpPrice, currentTP.Id));

                        strategy.ChangeOrderWrapper(currentTP, tpQty, myTpPrice, 0);
                    }
			    }
			    else
			    {
                    // CREATE: Si no existe activa, creamos una nueva
				    string tpBase = direction == "Short" ?
					    (isTp1 ? "TP1_Short" : "TP2_Short") :
					    (isTp1 ? "TP1_Long" : "TP2_Long");
				    string tpTag = string.Format("{0}_{1:D2}", tpBase, currentVwapNumber);
				    OrderAction tpAction = direction == "Short" ? OrderAction.BuyToCover : OrderAction.Sell;

				    if (isTp1) {
					    strategy.tp1Order = strategy.SubmitOrderUnmanagedWrapper(0, tpAction, OrderType.Limit, tpQty, myTpPrice, 0, "", tpTag);
					    strategy.Log(string.Format("TP1 CREATED: {0} @ {1} Qty={2}", tpTag, myTpPrice, tpQty));
				    } else {
					    strategy.tp2Order = strategy.SubmitOrderUnmanagedWrapper(0, tpAction, OrderType.Limit, tpQty, myTpPrice, 0, "", tpTag);
					    strategy.Log(string.Format("TP2 CREATED: {0} @ {1} Qty={2}", tpTag, myTpPrice, tpQty));
				    }
			    }

            }
		    catch (Exception ex)
		    {
			    strategy.Log("CRITICAL ERROR Submitting Exits (Manager): " + ex.Message);
		    }
	    }
        
        // ... (Restore DetectInternalLevel and GetOppositeLevelPrice Logic) ...
        
        public void DetectInternalLevel(SessionLevel setupLevel, List<SessionLevel> allLevels)
        {
            IsInternalLevel = false;
            ExternalLevelAboveName = "";
            ExternalLevelBelowName = "";
            double externalLevelAbove = 0;
            double externalLevelBelow = 0;
            
            if (setupLevel == null || allLevels == null) return;
    
    // v1.15.39: CRITICAL FIX - Historical Levels are ALWAYS External
    // If a level is from a previous day, it cannot be "Internal" to today's session.
    // This forces the use of Global VWAP (Visible Line) which users expect for historical levels.
    if (setupLevel.StartTime.Date < strategy.Time[0].Date)
    {
        IsInternalLevel = false;
        ExternalLevelAboveName = "Historical Context"; // Fallback name
        ExternalLevelBelowName = "Historical Context";
        strategy.Log(string.Format("EXTERNAL LEVEL (HISTORICAL): {0} @ {1} (Date={2}) -> Uses Global VWAP",
            setupLevel.Name, setupLevel.Price, setupLevel.StartTime.ToShortDateString()));
        return;
    }

    // v1.14.59: Filter only TODAY's levels for internal/external classification
            DateTime today = strategy.Time[0].Date;
            var todayLevels = allLevels.Where(l => l.StartTime.Date == today).ToList();
            
            // v1.14.59: INVERTED LOGIC - If external level found → IsInternalLevel = FALSE (use Global VWAP)
            // Internal = No external protection (level is the extreme) → Use Adhoc VWAP
            // External = Has external protection → Use Global VWAP
            
            if (setupLevel.IsResistance)
            {
                externalLevelAbove = FindExternalLevelAbove(setupLevel, todayLevels);
                if (externalLevelAbove > 0)
                {
                    IsInternalLevel = false; // v1.14.59: INVERTED - Has external protection → use Global VWAP
                    strategy.Log(string.Format("EXTERNAL LEVEL: {0} @ {1} (Protected by: {2} @ {3})",
                        setupLevel.Name, setupLevel.Price, ExternalLevelAboveName, externalLevelAbove));
                }
                else
                {
                    IsInternalLevel = true; // No external protection → is the extreme → use Adhoc
                    strategy.Log(string.Format("INTERNAL LEVEL (EXTREME): {0} @ {1} (No external protection above)",
                        setupLevel.Name, setupLevel.Price));
                }
                externalLevelBelow = FindExternalLevelBelow(setupLevel, todayLevels);
            }
            else
            {
                externalLevelBelow = FindExternalLevelBelow(setupLevel, todayLevels);
                if (externalLevelBelow > 0)
                {
                    IsInternalLevel = false; // v1.14.59: INVERTED - Has external protection → use Global VWAP
                    strategy.Log(string.Format("EXTERNAL LEVEL: {0} @ {1} (Protected by: {2} @ {3})",
                        setupLevel.Name, setupLevel.Price, ExternalLevelBelowName, externalLevelBelow));
                }
                else
                {
                    IsInternalLevel = true; // No external protection → is the extreme → use Adhoc
                    strategy.Log(string.Format("INTERNAL LEVEL (EXTREME): {0} @ {1} (No external protection below)",
                        setupLevel.Name, setupLevel.Price));
                }
                externalLevelAbove = FindExternalLevelAbove(setupLevel, todayLevels); // For TP2
            }
        }

        private double FindExternalLevelAbove(SessionLevel currentLevel, List<SessionLevel> allLevels)
        {
            double highestExternal = 0;
            string highestName = "";
            foreach (var level in allLevels)
            {
                if (!level.IsResistance) continue;
                if (level.Price <= currentLevel.Price) continue;
                if (GetSessionName(currentLevel.Name) == GetSessionName(level.Name)) continue;
                if (level.Price > highestExternal) { highestExternal = level.Price; highestName = level.Name; }
            }
            if (highestExternal > 0) ExternalLevelAboveName = highestName;
            return highestExternal;
        }

        private double FindExternalLevelBelow(SessionLevel currentLevel, List<SessionLevel> allLevels)
        {
            double lowestExternal = 0;
            string lowestName = "";
            foreach (var level in allLevels)
            {
                if (level.IsResistance) continue;
                if (level.Price >= currentLevel.Price) continue;
                if (GetSessionName(currentLevel.Name) == GetSessionName(level.Name)) continue;
                if (lowestExternal == 0 || level.Price < lowestExternal) { lowestExternal = level.Price; lowestName = level.Name; }
            }
            if (lowestExternal > 0) ExternalLevelBelowName = lowestName;
            return lowestExternal;
        }

        private string GetSessionName(string levelName)
        {
            if (levelName.Contains("Asia")) return "Asia";
            if (levelName.Contains("Europe")) return "Europe";
            if (levelName.Contains("USA")) return "USA";
            return "";
        }
        
        public double GetOppositeLevelPrice(string name, DateTime refTime, List<SessionLevel> activeLevels, SessionLevel cachedOppositeLevel, bool oppositeSearchDone, out SessionLevel foundLevel)
        {
            foundLevel = null;
            // Uses Passed Cache (kept for API compatibility with Strategy or self-use if passed logic matches)
            if (cachedOppositeLevel != null) return cachedOppositeLevel.Price;
            if (oppositeSearchDone) return 0;
            if (string.IsNullOrEmpty(name)) return 0;

            string oppName = "";
            bool isSearchingHigh = false; // true if searching for High, false if searching for Low
            if (name.Contains(" Low"))
            {
                oppName = name.Replace(" Low", " High");
                isSearchingHigh = true;
            }
            else if (name.Contains(" High"))
            {
                oppName = name.Replace(" High", " Low");
                isSearchingHigh = false;
            }
            else return 0;

            // v1.15.28: Log will show CURRENT trading day (Time[0]), not setup level's session start
            if (strategy.EnableDebugLogs) strategy.Log(string.Format("{0} | SEARCH_OPPOSITE: Looking for '{1}' on CURRENT Trading Day {2:yyyy-MM-dd} (Setup: {3}, Session Start: {4:yyyy-MM-dd HH:mm})",
                strategy.Time[0], oppName, GetTradingDay(strategy.Time[0]), name, refTime));

            SessionLevel foundLvl = null;
            string setupSessionTicks = "";
            // v1.14.64 FIX: Use refTime (StartTime) to find the EXACT setup level object,
            // preventing the selection of old duplicate levels from history with the same Name but old Tags.
            SessionLevel setupLevel = activeLevels.FirstOrDefault(l => l.Name == name && l.StartTime == refTime);

            // Fallback: If not found by exact time (shouldn't happen if logic is correct), try just by name
            if (setupLevel == null)
            {
                strategy.Print("[OPP_SEARCH] WARNING: Setup level not found by Name+Time (" + name + " @ " + refTime + "). Falling back to Name only.");
                setupLevel = activeLevels.FirstOrDefault(l => l.Name == name);
            }

            if (setupLevel != null && !string.IsNullOrEmpty(setupLevel.Tag))
            {
                string[] tagParts = setupLevel.Tag.Split('_');
                if (tagParts.Length >= 3) setupSessionTicks = tagParts[tagParts.Length - 1];
            }

            // v1.15.16: First find same-day opposite level
            SessionLevel sameDayLevel = null;
            foreach(var l in activeLevels)
            {
                if (l.Name.Trim().Equals(oppName.Trim(), StringComparison.OrdinalIgnoreCase)) {
                    bool sameSession = false;
                    if (!string.IsNullOrEmpty(setupSessionTicks) && !string.IsNullOrEmpty(l.Tag))
                    {
                        string[] candidateTagParts = l.Tag.Split('_');
                        string candidateTicks = candidateTagParts.Length >= 3 ? candidateTagParts[candidateTagParts.Length - 1] : "";
                        sameSession = (candidateTicks == setupSessionTicks);
                    }
                    if (sameSession) { sameDayLevel = l; break; }
                }
            }

            // v1.15.17: Now find the MOST EXTREME opposite level from SAME TRADING DAY (maximize TP2)
            // For High: find highest price | For Low: find lowest price
            // v1.15.27: CRITICAL FIX - Compare by TRADING DAY instead of session timestamp
            // Trading Day Logic: Sessions starting after 6pm (18:00) belong to NEXT day's trading session
            // Example: Asia Low starting Jan 5 @ 7pm belongs to Jan 6 trading day
            SessionLevel mostExtremeLevel = sameDayLevel; // Start with same-day level

            // v1.15.28: FIX - Use CURRENT TIME (Time[0]) to calculate trading day, not setup level's StartTime
            // This ensures we find opposite levels from the CURRENT trading session, not from when the setup level was created
            // Example: If USA Low was created on Jan 10, but trade enters on Jan 12 at 10:54pm (Trading Day = Jan 13),
            //          we should look for opposite levels from Trading Day Jan 13 (like Asia High @ 2199.5),
            //          not from Trading Day Jan 10 (like Europe High @ 2252.8)
            DateTime currentTradingDay = GetTradingDay(strategy.Time[0]);

            foreach(var l in activeLevels)
            {
                // v1.15.28: Check if this level is from the SAME TRADING DAY as current time
                DateTime candidateTradingDay = GetTradingDay(l.StartTime);
                bool sameDay = (candidateTradingDay.Date == currentTradingDay.Date);

                if (!sameDay) continue; // Skip levels from other trading days

                // Check if this is an opposite type level (High vs Low)
                bool isOppositeType = false;
                if (isSearchingHigh && l.Name.Contains(" High")) isOppositeType = true;
                if (!isSearchingHigh && l.Name.Contains(" Low")) isOppositeType = true;

                if (isOppositeType)
                {
                    if (mostExtremeLevel == null)
                    {
                        mostExtremeLevel = l;
                    }
                    else
                    {
                        // For High: pick higher price | For Low: pick lower price
                        if (isSearchingHigh && l.Price > mostExtremeLevel.Price)
                        {
                            mostExtremeLevel = l;
                        }
                        else if (!isSearchingHigh && l.Price < mostExtremeLevel.Price)
                        {
                            mostExtremeLevel = l;
                        }
                    }
                }
            }

            // v1.15.17/v1.15.27: Log if we selected a more extreme level than same-session opposite
            if (mostExtremeLevel != null && sameDayLevel != null && mostExtremeLevel != sameDayLevel)
            {
                strategy.Log(string.Format("TP2_MAXIMIZE: Using {0} @ {1} instead of {2} @ {3} (more extreme on same trading day)",
                    mostExtremeLevel.Name, mostExtremeLevel.Price, sameDayLevel.Name, sameDayLevel.Price));
            }

            // v1.15.28: Log when opposite level is selected for TP2
            if (mostExtremeLevel != null)
            {
                strategy.Log(string.Format("TP2_SELECTED: {0} @ {1} for setup {2} on trading day {3:yyyy-MM-dd}",
                    mostExtremeLevel.Name, mostExtremeLevel.Price, setupLevel.Name, currentTradingDay));
            }

            foundLevel = mostExtremeLevel;
            return mostExtremeLevel != null ? mostExtremeLevel.Price : 0;
        }

        // v1.15.27: Helper function to calculate trading day from session start time
        // Sessions starting after 6pm (18:00) belong to NEXT day's trading session
        // Example: Asia starting Jan 5 @ 7pm (19:00) → Trading Day = Jan 6
        //          USA starting Jan 6 @ 10:30am → Trading Day = Jan 6
        private DateTime GetTradingDay(DateTime sessionStartTime)
        {
            // If session starts after 6pm (18:00), it belongs to next day's trading
            if (sessionStartTime.Hour >= 18)
            {
                return sessionStartTime.Date.AddDays(1);
            }
            else
                return sessionStartTime.Date;
            }


        		// v1.14.40: Handle TP1 Fill - Move SL to Breakeven
		public void HandleTP1Fill(int fillQty)
		{
			// v1.15.21: ALWAYS update SL quantity after TP1
			if (strategy.stopOrder != null)
			{
				// LAG PROTECTION: Position.Quantity might not have updated yet in OnExecutionUpdate
				int currentPosQty = Math.Abs(strategy.Position.Quantity);
				int slQty = strategy.stopOrder.Quantity;
				
				// Smart Quantity: If Position matches SL (meaning no change detected yet), manually subtract fill
				int targetQty = currentPosQty;
				
				if (currentPosQty == slQty)
				{
					targetQty = slQty - fillQty;
					strategy.Log(string.Format("LAG DETECTED: Position.Qty ({0}) same as SL.Qty. Manually reducing by fill ({1}) -> {2}", currentPosQty, fillQty, targetQty));
				}
				
				// v1.15.48: SAFETY GUARD (Playback/Lag Fix)
                // If Lag Detection double-counts (e.g. called twice), targetQty might drop to 0 erroneously.
                // We MUST ensure SL covers at least the remaining active TP2 orders.
                int minRequired = 0;
                if (strategy.tp2Order != null && (strategy.tp2Order.OrderState == OrderState.Working || strategy.tp2Order.OrderState == OrderState.Accepted))
                {
                    minRequired = strategy.tp2Order.Quantity;
                }
                
                if (targetQty < minRequired) 
                {
                    strategy.Log(string.Format("SL PROTECTION GUARD: TargetQty ({0}) < MinRequired ({1}). Clamping to {1}.", targetQty, minRequired));
                    targetQty = minRequired;
                }
				
				if (targetQty <= 0) targetQty = 0; // Safety

				// Guard against Qty=0
				if (targetQty > 0)
				{
					double bePrice = (strategy.entryOrder != null) ? strategy.entryOrder.AverageFillPrice : strategy.stopOrder.StopPrice; 
					// Fallback to current stop if entryOrder lost (rare)
					
					if (!strategy.EnableBreakeven)
					{
						// Breakeven disabled - Update quantity only, keep original SL price
						strategy.Log(strategy.Time[0] + " SL QTY UPDATE: TP1 filled. SL (" + strategy.stopOrder.Name + ") " + slQty + "->" + targetQty + " (BE Disabled)");
						strategy.ChangeOrderWrapper(strategy.stopOrder, targetQty, 0, strategy.stopOrder.StopPrice);
					}
					else
					{
						// Breakeven enabled
						strategy.Log(strategy.Time[0] + " BE ACTION: Moving SL (" + strategy.stopOrder.Name + ") " + slQty + "->" + targetQty + " @ " + bePrice);
						strategy.ChangeOrderWrapper(strategy.stopOrder, targetQty, 0, bePrice);
					}
				}
				else
				{
					strategy.Log(strategy.Time[0] + " BE SKIP: Target Qty is 0, cancelling orphan SL");
					if (strategy.stopOrder.OrderState == OrderState.Working || strategy.stopOrder.OrderState == OrderState.Accepted)
						strategy.CancelOrderWrapper(strategy.stopOrder);
				}
			}
		}
        
        // v1.14.40: Handle TP2 Fill - Logging only (SL already at BE from TP1)
        public void HandleTP2Fill()
        {
            strategy.Log(strategy.Time[0] + " TP2 Filled. SL already at BE (if TP1 filled first).");
        }

        // v1.14.94: Cancel All Protection Orders - Used before emergency exits to prevent race conditions
        public void CancelAllProtectionOrders()
        {
            strategy.Log(strategy.Time[0] + " CANCEL_ALL_PROTECTION: Cancelling all SL/TP orders to prevent race conditions");

            // Cancel Stop Loss
            if (strategy.stopOrder != null &&
                (strategy.stopOrder.OrderState == OrderState.Working ||
                 strategy.stopOrder.OrderState == OrderState.Accepted))
            {
                strategy.CancelOrderWrapper(strategy.stopOrder);
                strategy.Log("CANCEL_ALL: Cancelled SL order - " + strategy.stopOrder.Name);
            }

            // Cancel Take Profit 1
            if (strategy.tp1Order != null &&
                (strategy.tp1Order.OrderState == OrderState.Working ||
                 strategy.tp1Order.OrderState == OrderState.Accepted))
            {
                strategy.CancelOrderWrapper(strategy.tp1Order);
                strategy.Log("CANCEL_ALL: Cancelled TP1 order - " + strategy.tp1Order.Name);
            }

            // Cancel Take Profit 2
            if (strategy.tp2Order != null &&
                (strategy.tp2Order.OrderState == OrderState.Working ||
                 strategy.tp2Order.OrderState == OrderState.Accepted))
            {
                strategy.CancelOrderWrapper(strategy.tp2Order);
                strategy.Log("CANCEL_ALL: Cancelled TP2 order - " + strategy.tp2Order.Name);
            }

            // Cancel Ladder Orders (v1.15.40)
            if (strategy.ladderOrders != null && strategy.ladderOrders.Count > 0)
            {
                // Create copy to iterate safely
                var ordersToCancel = new List<Order>(strategy.ladderOrders);
                foreach(var ord in ordersToCancel)
                {
                     if (ord != null && (ord.OrderState == OrderState.Working || ord.OrderState == OrderState.Accepted))
                     {
                         strategy.CancelOrderWrapper(ord);
                         strategy.Log("CANCEL_ALL: Cancelled Ladder order - " + ord.Name);
                     }
                }
                strategy.ladderOrders.Clear();
            }
        }
    }
}
