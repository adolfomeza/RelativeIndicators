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
            IsInternalLevel = false;
            cachedOppositeLevel = null;
            oppositeSearchDone = false;
            
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
	    public void EnsureProtection(string direction, string entrySignalName, int filledQty, 
                                     int currentVwapNumber, bool isShortSetup, string setupLevelName, 
                                     DateTime setupLevelTime, double setupAnchorPrice, double validatedTargetPrice)
	    {
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
		    
		    int neededTp1 = totalTp1Target - strategy.protectedTp1Qty;
		    if (neededTp1 < 0) neededTp1 = 0;
		    
		    int forTp1 = Math.Min(neededTp1, filledQty);
		    int forTp2 = filledQty - forTp1;
		    
		    strategy.Log(string.Format("   -> Protection Alloc: Filled={0} | ForTP1={1} (Need:{2}) | ForTP2={3}", filledQty, forTp1, neededTp1, forTp2));

		    if (forTp1 > 0)
			    SubmitProtectionOrders(direction, true, forTp1, currentVwapNumber, isShortSetup, setupLevelName, setupLevelTime, setupAnchorPrice, validatedTargetPrice);
			    
		    if (forTp2 > 0)
			    SubmitProtectionOrders(direction, false, forTp2, currentVwapNumber, isShortSetup, setupLevelName, setupLevelTime, setupAnchorPrice, validatedTargetPrice);
			    
		    // Update State
		    strategy.protectedTp1Qty += forTp1;
		    strategy.protectedTp2Qty += forTp2;
		    
		    // v1.11.14: Mark protection orders as created (This flag seems local to strategy but unused in new logic? Or generic flag?)
            // strategy.protectionOrdersCreated is not directly exposed but it was just a flag.
            // If needed, we can expose it, but let's assume it was for internal flow in EnsureProtection old.
		    
		    strategy.Log(strategy.Time[0] + " EnsureProtection COMPLETE");
	    }

        private void SubmitProtectionOrders(string direction, bool isTp1, int qty,
                                            int currentVwapNumber, bool isShortSetup, string setupLevelName, 
                                            DateTime setupLevelTime, double setupAnchorPrice, double validatedTargetPrice)
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

			    if (validatedTargetPrice > 0) 
			    {
				    targetZoneOpposite = validatedTargetPrice;
				    strategy.Log("FORCE TARGET: Using Validated Price: " + validatedTargetPrice);
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
			    
			    if (validatedTargetPrice > 0) 
			    {
				    targetZoneOpposite = validatedTargetPrice;
				    strategy.Log("FORCE TARGET: Using Validated Price: " + validatedTargetPrice);
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

		    if (isTp1) { strategy.activeTp1Price = myTpPrice; strategy.tradeOriginalTp1Price = myTpPrice; } 
		    else { strategy.activeTp2Price = myTpPrice; strategy.tradeOriginalTp2Price = myTpPrice; } 

		    // DEBUG TARGETS
		    strategy.Log(string.Format("TP CALC ({0}): Entry={1} | GlobalVWAP={2} | ZoneOpp={3} (Val={4}) | TP1={5} TP2={6} | Selected={7}",
			    direction, avgEntry, targetGlobalVWAP, targetZoneOpposite, validatedTargetPrice, tp1Price, tp2Price, myTpPrice));

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
                    // UPDATE: Si ya existe y está activa, usamos ChangeOrder para modificar cantidad/precio
                    if (currentTP.Quantity != tpQty || Math.Abs(currentTP.LimitPrice - myTpPrice) > double.Epsilon)
                    {
				        strategy.Log(string.Format("TP UPDATE ({0}): Modifying Old Qty={1}/Price={2} -> New Qty={3}/Price={4}", 
					        myTpTag, currentTP.Quantity, currentTP.LimitPrice, tpQty, myTpPrice));
                            
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
            
            if (setupLevel.IsResistance)
            {
                externalLevelAbove = FindExternalLevelAbove(setupLevel, allLevels);
                if (externalLevelAbove > 0)
                {
                    IsInternalLevel = true;
                    strategy.Log(string.Format("INTERNAL LEVEL: {0} @ {1} (External above: {2} @ {3})",
                        setupLevel.Name, setupLevel.Price, ExternalLevelAboveName, externalLevelAbove));
                }
                externalLevelBelow = FindExternalLevelBelow(setupLevel, allLevels);
            }
            else
            {
                externalLevelBelow = FindExternalLevelBelow(setupLevel, allLevels);
                if (externalLevelBelow > 0)
                {
                    IsInternalLevel = true;
                    strategy.Log(string.Format("INTERNAL LEVEL: {0} @ {1} (External below: {2} @ {3})",
                        setupLevel.Name, setupLevel.Price, ExternalLevelBelowName, externalLevelBelow));
                }
                externalLevelAbove = FindExternalLevelAbove(setupLevel, allLevels); // For TP2
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
            if (name.Contains(" Low")) oppName = name.Replace(" Low", " High");
            else if (name.Contains(" High")) oppName = name.Replace(" High", " Low");
            else return 0;
            
            if (strategy.EnableDebugLogs) strategy.Log(string.Format("{0} | SEARCH_OPPOSITE: Looking for '{1}' from SAME DAY as '{2}' (RefDate: {3:yyyy-MM-dd})", strategy.Time[0], oppName, name, refTime.Date));
            
            SessionLevel foundLvl = null;
            string setupSessionTicks = "";
            SessionLevel setupLevel = activeLevels.FirstOrDefault(l => l.Name == name);
            if (setupLevel != null && !string.IsNullOrEmpty(setupLevel.Tag))
            {
                string[] tagParts = setupLevel.Tag.Split('_');
                if (tagParts.Length >= 3) setupSessionTicks = tagParts[tagParts.Length - 1];
            }
            
            foreach(var l in activeLevels)
            {
                if (l.Name.Trim().Equals(oppName.Trim(), StringComparison.OrdinalIgnoreCase)) {
                    bool sameSession = false;
                    if (!string.IsNullOrEmpty(setupSessionTicks) && !string.IsNullOrEmpty(l.Tag))
                    {
                        string[] candidateTagParts = l.Tag.Split('_');
                        if (candidateTagParts.Length >= 3) sameSession = (candidateTagParts[candidateTagParts.Length - 1] == setupSessionTicks);
                    }
                    if (sameSession) { foundLvl = l; break; }
                }
            }
            foundLevel = foundLvl;
            return foundLvl != null ? foundLvl.Price : 0;
        }
        
        // v1.14.40: Handle TP1 Fill - Move SL to Breakeven
        public void HandleTP1Fill()
        {
            strategy.Log(strategy.Time[0] + " BE LOGIC: TP1 Filled. Moving SL to BE.");
            
            // Move single SL to breakeven
            if (strategy.stopOrder != null && strategy.entryOrder != null)
            {
                int remainingQty = Math.Abs(strategy.Position.Quantity);
                
                // Guard against Qty=0 (position already fully closed by TP1)
                if (remainingQty > 0)
                {
                    strategy.Log(strategy.Time[0] + " BE ACTION: Moving SL (" + strategy.stopOrder.Name + ") to " + strategy.entryOrder.AverageFillPrice + " Qty=" + remainingQty);
                    strategy.ChangeOrderWrapper(strategy.stopOrder, remainingQty, 0, strategy.entryOrder.AverageFillPrice);
                }
                else
                {
                    strategy.Log(strategy.Time[0] + " BE SKIP: Position already flat, cancelling orphan SL");
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
    }
}
