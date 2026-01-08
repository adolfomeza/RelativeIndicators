using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
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
    // v1.14.39: Dedicated VWAP Calculator Module
    public class VWAPCalculator
    {
        private SessionLevelsStrategy strategy;
        
        // VWAP Objects
        public SessionVWAP EthHighVWAP { get; private set; } = new SessionVWAP();
        public SessionVWAP EthLowVWAP { get; private set; } = new SessionVWAP();
        public SessionVWAP TradeVWAP { get; private set; } = new SessionVWAP();
        
        // State State variables
        public double EthHighPrice { get; private set; } = double.MinValue;
        public double EthLowPrice { get; private set; } = double.MaxValue;
        private DateTime lastEthResetDate = DateTime.MinValue;
        public int HighAnchorBar { get; private set; } = 0;
        public int LowAnchorBar { get; private set; } = 0;
        
        // Adhoc Logic
        public double AdhocVolSum { get; set; } = 0;
        public double AdhocPvSum { get; set; } = 0;
        private double adhocLastVol = 0;
        private int adhocLastBar = -1;
        public int AdhocAnchorBar { get; set; } = 0;
        public double VisualAdhocPrevBarVal { get; set; } = 0;
        public double VisualAdhocLastVal { get; set; } = 0;
        
        // v1.14.65: Flag for Trade VWAP persistence beyond session close
        private bool isPostSessionExtension = false;
        
        public VWAPCalculator(SessionLevelsStrategy strategy)
        {
            this.strategy = strategy;
        }

        public void ManageGlobalVWAPs(double deltaVol, DateTime time, int currentBar, ISeries<double> high, ISeries<double> low, ISeries<double> close, ISeries<double> open, ISeries<double> volume, TimeZoneInfo nyTimeZone, TimeZoneInfo chartTimeZone)
        {
            if (nyTimeZone == null || chartTimeZone == null) return;
            
            // 1. Determine Current Trading Day (based on 18:00 NY start)
            DateTime currentNy = TimeZoneInfo.ConvertTime(time, chartTimeZone, nyTimeZone);
            TimeSpan cutoff = TimeSpan.FromHours(18);
            DateTime tradingDay = currentNy.TimeOfDay >= cutoff ? currentNy.Date.AddDays(1) : currentNy.Date;
            
            // 2. HARD RESET at Start of Day
            bool hardReset = false;
            if (tradingDay != lastEthResetDate)
            {
                EthHighPrice = double.MinValue;
                EthLowPrice = double.MaxValue;
                EthHighVWAP = new SessionVWAP();
                EthLowVWAP = new SessionVWAP();
                lastEthResetDate = tradingDay;
                hardReset = true;
                
                // Reset Anchor Trackers
                HighAnchorBar = currentBar;
                LowAnchorBar = currentBar;
            }
            
            // 3. Update High/Low and Anchor Logic
            bool highReset = false;
            bool lowReset = false;
            
            double price = close[0];
            if (strategy.VwapMethod == VwapCalculationMode.Typical) price = (high[0] + low[0] + close[0]) / 3.0;
            else if (strategy.VwapMethod == VwapCalculationMode.OHLC4) price = (open[0] + high[0] + low[0] + close[0]) / 4.0;
            
            // Retroactive anchor update
            if (strategy.IsFirstTickOfBar && currentBar > 0)
            {
                // Check if previous bar was the high anchor
                if (HighAnchorBar == currentBar - 1 && EthHighVWAP.VolSum > 0)
                {
                    double finalPrice = close[1];
                    if (strategy.VwapMethod == VwapCalculationMode.Typical) finalPrice = (high[1] + low[1] + close[1]) / 3.0;
                    else if (strategy.VwapMethod == VwapCalculationMode.OHLC4) finalPrice = (open[1] + high[1] + low[1] + close[1]) / 4.0;
                    
                    // Recalculate VWAP with final values
                    EthHighVWAP.Reset(volume[1], finalPrice);
                    // Update the previous bar's visual value retroactively via Strategy wrapper if needed, 
                    // but Strategy Values[] access is cleaner in main file. 
                    // Ideally we return the corrected value?
                    strategy.Values[0][1] = finalPrice;
                }
                
                // Check if previous bar was the low anchor
                if (LowAnchorBar == currentBar - 1 && EthLowVWAP.VolSum > 0)
                {
                    double finalPrice = close[1];
                    if (strategy.VwapMethod == VwapCalculationMode.Typical) finalPrice = (high[1] + low[1] + close[1]) / 3.0;
                    else if (strategy.VwapMethod == VwapCalculationMode.OHLC4) finalPrice = (open[1] + high[1] + low[1] + close[1]) / 4.0;
                    
                    EthLowVWAP.Reset(volume[1], finalPrice);
                    strategy.Values[1][1] = finalPrice;
                }
            }
            
            // Check High
            if (high[0] > EthHighPrice)
            {
                // New High found! The PREVIOUS segment is now "Old/Cut".
                if (!hardReset && currentBar > HighAnchorBar)
                {
                    int barsBack = currentBar - HighAnchorBar;
                    for (int i = 1; i < barsBack; i++)
                    {
                        strategy.PlotBrushes[0][i] = Brushes.Gray;
                    }
                }
                
                EthHighPrice = high[0];
                highReset = true;
                EthHighVWAP.Reset(volume[0], price);
                HighAnchorBar = currentBar; 
            }
            else
            {
                EthHighVWAP.Accumulate(deltaVol, price);
            }
            
            // Check Low
            if (low[0] < EthLowPrice)
            {
                if (!hardReset && currentBar > LowAnchorBar)
                {
                    int barsBack = currentBar - LowAnchorBar;
                    for (int i = 1; i < barsBack; i++)
                    {
                        strategy.PlotBrushes[1][i] = Brushes.Gray;
                    }
                }
                
                EthLowPrice = low[0];
                lowReset = true;
                EthLowVWAP.Reset(volume[0], price);
                LowAnchorBar = currentBar;
            }
            else
            {
                EthLowVWAP.Accumulate(deltaVol, price);
            }
            
            
            // v1.14.58: TradeVWAP should follow the same accumulation as Global VWAP Low/High
            // This allows it to persist overnight while staying aligned with the Global VWAP
            
            // v1.14.65: Trade VWAP Persistence Logic
            // If Hard Reset (18:00) happens AND Trade is active, we enter "Post Session Extension" mode.
            if (hardReset)
            {
                if (strategy.IsTradeVwapActive) isPostSessionExtension = true;
                else isPostSessionExtension = false;
            }
            if (!strategy.IsTradeVwapActive) isPostSessionExtension = false; // Safety reset if trade closes
            
            if (strategy.IsTradeVwapActive && deltaVol > 0)
            {
                // For Short: follow EthLowVWAP accumulation
                // For Long: follow EthHighVWAP accumulation
                // This ensures TradeVWAP stays aligned with the visible VWAP line
                TradeVWAP.Accumulate(deltaVol, price);
                
                // BUT: We need to reset TradeVWAP when the Global VWAP resets...
                // UNLESS we are in Post-Session Extension mode (active trade crossing 18:00)
                if (!isPostSessionExtension)
                {
                    if (strategy.isShortSetup && lowReset)
                    {
                        TradeVWAP.Reset(volume[0], price);
                    }
                    else if (!strategy.isShortSetup && highReset)
                    {
                        TradeVWAP.Reset(volume[0], price);
                    }
                }
            }
            
            // 4. Assign to Plots
            if (EthHighVWAP.VolSum > 0)
            {
                strategy.Values[0][0] = EthHighVWAP.CurrentValue;
                if (hardReset || highReset) strategy.PlotBrushes[0][0] = Brushes.Transparent;
            }
            else strategy.Values[0][0] = double.NaN;
            
            if (EthLowVWAP.VolSum > 0)
            {
                strategy.Values[1][0] = EthLowVWAP.CurrentValue;
                if (hardReset || lowReset) strategy.PlotBrushes[1][0] = Brushes.Transparent;
            }
            else strategy.Values[1][0] = double.NaN;
            
            // Draw Trade VWAP line manually
            if (strategy.IsTradeVwapActive && TradeVWAP.VolSum > 0 && currentBar > 0)
            {
                double tradeVwapValue = TradeVWAP.CurrentValue;
                string lineTag = "TradeVWAP_" + currentBar;
                
                // v1.14.65: Gray styling for extended session
                Brush drawBrush = isPostSessionExtension ? Brushes.Gray : Brushes.Cyan;
                int drawWidth = isPostSessionExtension ? 1 : 2;
                
                Draw.Line(strategy, lineTag, false, 1, tradeVwapValue, 0, tradeVwapValue, drawBrush, DashStyleHelper.Solid, drawWidth);
            }
        }
        
        public void UpdateAdhocVWAP(double deltaVol, int currentBar, ISeries<double> high, ISeries<double> low, ISeries<double> close, ISeries<double> open, ISeries<double> volume)
        {
            if (strategy.IsFirstTickOfBar)
            {
                adhocLastVol = 0;
                adhocLastBar = currentBar;
                
                // Retroactive update
                if (currentBar > 0 && AdhocAnchorBar == currentBar - 1 && AdhocVolSum > 0)
                {
                    double finalPrice = close[1];
                    if (strategy.VwapMethod == VwapCalculationMode.Typical) finalPrice = (high[1] + low[1] + close[1]) / 3.0;
                    else if (strategy.VwapMethod == VwapCalculationMode.OHLC4) finalPrice = (open[1] + high[1] + low[1] + close[1]) / 4.0;
                    
                    AdhocVolSum = volume[1];
                    AdhocPvSum = volume[1] * finalPrice;
                    VisualAdhocPrevBarVal = finalPrice;
                    VisualAdhocLastVal = finalPrice;
                }
            }
            
            if (deltaVol > 0)
            {
                AdhocVolSum += deltaVol;
                double price = close[0];
                if (strategy.VwapMethod == VwapCalculationMode.Typical) price = (high[0] + low[0] + close[0]) / 3.0;
                else if (strategy.VwapMethod == VwapCalculationMode.OHLC4) price = (open[0] + high[0] + low[0] + close[0]) / 4.0;
                
                AdhocPvSum += deltaVol * price;
                adhocLastVol += deltaVol; 
            }
        }
        
        public double GetSetupVWAP(bool isShort, string setupLevelName)
        {
            // 1. If we have ADHOC VOLUME tracked, use it.
            if (!string.IsNullOrEmpty(setupLevelName) && AdhocVolSum > 0)
            {
                return AdhocPvSum / AdhocVolSum;
            }
            
            // 2. Fallback to Global
            return isShort ? GetCurrentHighVWAP() : GetCurrentLowVWAP();
        }
        
        public double GetCurrentHighVWAP() { return EthHighVWAP.CurrentValue; }
        public double GetCurrentLowVWAP() { return EthLowVWAP.CurrentValue; }
        
        // v1.14.58: TradeVWAP now simply mirrors Global VWAP Low/High
        public double GetTradeVWAPCurrentValue() 
        { 
            // v1.14.66 FIX: Return the ACTUAL persistent TradeVWAP object value, 
            // instead of redirecting to the Global VWAP (which resets daily).
            // Since TradeVWAP now mirrors Global but survives reset if active, this provides the correct value.
            return TradeVWAP.CurrentValue; 
        }

        public void ResetAdhoc(double vol, double price, int bar)
        {
            AdhocVolSum = vol;
            AdhocPvSum = vol * price;
            adhocLastVol = vol;
            adhocLastBar = bar;
            AdhocAnchorBar = bar;
            VisualAdhocPrevBarVal = price;
            VisualAdhocLastVal = price;
        }

        public void ClearAdhoc()
        {
            AdhocVolSum = 0;
            AdhocPvSum = 0;
        }
        
        public void InitTradeVWAP(bool isShort)
        {
             if (isShort)
            {
                TradeVWAP.VolSum = EthLowVWAP.VolSum;
                TradeVWAP.PvSum = EthLowVWAP.PvSum;
            }
            else
            {
                TradeVWAP.VolSum = EthHighVWAP.VolSum;
                TradeVWAP.PvSum = EthHighVWAP.PvSum;
            }
        }
    }
}
