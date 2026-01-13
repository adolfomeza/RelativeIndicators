using System;
using System.Collections.Generic;
using System.Linq;

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// v1.14.88: TargetDistributor - Handles mathematical distribution of targets
    /// Logic: Hybrid Scaled Distribution (Front-Loaded R-Multiples)
    /// </summary>
    public struct PriceQtyPair
    {
        public double Price;
        public int Quantity;
        public string Label; // e.g., "1R", "2R", "ZoneEnd"

        public PriceQtyPair(double price, int qty, string label)
        {
            Price = price;
            Quantity = qty;
            Label = label;
        }
    }

    public class TargetDistributor
    {
        private SessionLevelsStrategy strategy;

        public TargetDistributor(SessionLevelsStrategy strategy)
        {
            this.strategy = strategy;
        }

        public List<PriceQtyPair> GetDistribution(
            double entryPrice, 
            double stopPrice, 
            double zoneTargetPrice, 
            int totalZoneQty, 
            bool isShort,
            string zonePrefix // "TP1" or "TP2"
        )
        {
            List<PriceQtyPair> distribution = new List<PriceQtyPair>();
            
            if (totalZoneQty <= 0) return distribution;

            // 1. Calculate Risk (R)
            double risk = Math.Abs(entryPrice - stopPrice);
            if (risk <= 0) 
            {
                // Fallback: simple target
                distribution.Add(new PriceQtyPair(zoneTargetPrice, totalZoneQty, zonePrefix + "_Final"));
                return distribution;
            }

            // 2. Identify Steps
            List<double> steps = new List<double>();
            double distanceToTarget = Math.Abs(zoneTargetPrice - entryPrice);
            
            // Calculate theoretical 'R' steps
            double theoreticalSteps = distanceToTarget / risk;
            double stepSize = risk; // Default to 1R
            int maxSteps = 20;

            // v1.14.89: DYNAMIC STEP SIZING
            // If the target is too far (e.g., 100R), increasing the R-multiple causes huge gaps if we cap at 20.
            // Instead, if steps > maxSteps, we scale the step size to cover the FULL range with 20 orders.
            if (theoreticalSteps > maxSteps)
            {
                stepSize = distanceToTarget / maxSteps;
                // Ensure stepSize is at least TickSize
                if (stepSize < strategy.TickSize) stepSize = strategy.TickSize;
            }

            // Generate Steps
            for (int i = 1; i <= maxSteps; i++)
            {
                double currentDist = stepSize * i;
                
                // Stop if we exceed or equal target (minus epsilon)
                if (currentDist >= distanceToTarget - (strategy.TickSize * 0.1)) break;

                double priceStep = isShort ? entryPrice - currentDist : entryPrice + currentDist;
                
                // Add Step
                steps.Add(priceStep);
            }
            
            // Add Final Target as the last step (Always)
            steps.Add(zoneTargetPrice);

            int totalSteps = steps.Count;
            
            // 3. Distribute Contracts
            // CASE A: Abundance (Contracts >= Steps)
            if (totalZoneQty >= totalSteps)
            {
                // Base allocation per step
                int baseQty = totalZoneQty / totalSteps;
                int remainder = totalZoneQty % totalSteps;

                // Create pairs
                for (int i = 0; i < totalSteps; i++)
                {
                    int qty = baseQty;
                    // Front-Load Remainder: Add 1 to first 'remainder' steps
                    if (i < remainder) qty++;

                    string label = (i == totalSteps - 1) ? zonePrefix + "_Final" : zonePrefix + "_" + (i + 1) + "R";
                    distribution.Add(new PriceQtyPair(steps[i], qty, label));
                }
            }
            // CASE B: Scarcity (Contracts < Steps)
            else
            {
                // Strategic Skipping
                // We have fewer contracts than steps.
                // Priority:
                // 1. Final Target (Always)
                // 2. 2 R (Break Even + Profit)
                // 3. 4 R, 6 R... (Even multiples)
                
                int contractsToAssign = totalZoneQty;
                
                // Always reserve 1 for Final Target
                distribution.Add(new PriceQtyPair(zoneTargetPrice, 1, zonePrefix + "_Final"));
                contractsToAssign--;
                
                if (contractsToAssign > 0)
                {
                    // Available slots to fill (excluding final)
                    // steps[0] is 1R, steps[1] is 2R...
                    // We want to prefer 2R (index 1), then 4R (index 3)...
                    List<int> preferredIndices = new List<int>();
                    
                    // Add even Rs (2R, 4R...)
                    for (int i = 1; i < totalSteps - 1; i += 2)
                    {
                        preferredIndices.Add(i);
                    }
                    
                    // If still extra, add odd Rs (1R, 3R...)
                    for (int i = 0; i < totalSteps - 1; i += 2)
                    {
                        preferredIndices.Add(i);
                    }
                    
                    // Sort indices to output via price order? No, simpler:
                    // Just iterate and fill orders
                    
                    // Actually, simple iteration map:
                    // Fill indices from preferred list until contracts run out
                    var selectedIndices = preferredIndices.Take(contractsToAssign).OrderBy(idx => idx).ToList();
                    
                    foreach(int idx in selectedIndices)
                    {
                        distribution.Add(new PriceQtyPair(steps[idx], 1, zonePrefix + "_" + (idx + 1) + "R"));
                    }
                }
                
                // Sort by price distance from entry to ensure correct execution order
                // (Though order submission order usually doesn't strictly matter for limits, logic helps)
                if (isShort)
                    distribution = distribution.OrderByDescending(p => p.Price).ToList();
                else
                    distribution = distribution.OrderBy(p => p.Price).ToList();
            }

            return distribution;
        }
    }
}
