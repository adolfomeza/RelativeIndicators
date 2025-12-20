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
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.RelativeIndicators
{
    public class RelativeVolumen : Indicator
    {
        private double sumVol;
        private double sumVolSq;
        private int count;

        private DateTime currentSessionDate;
        private int lastAlertBar = -1;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Volume indicator that compares current volume to the session's cumulative Standard Deviation.";
                Name = "Relative Volumen";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                DisplayInDataBox = true;
                DrawOnPricePanel = false;
                IsSuspendedWhileInactive = true;
                
                // Defaults
                NumStdDev = 2.0;
                SignalOnBreakout = true;
                BreakoutBullishColor = Brushes.White;
                BreakoutBearishColor = Brushes.RoyalBlue;
                LowVolumeColor = Brushes.Gray;
                
                UseSeparateRTH = false;
                RTHStartTime = "10:30";
                RTHThresholdColor = Brushes.Yellow;
                PreRTHThresholdColor = Brushes.White;

                AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Bar, "VolumePlot");
                AddPlot(new Stroke(Brushes.Orange, 2), PlotStyle.Line, "ThresholdPlot");
            }
            else if (State == State.Configure)
            {
                currentSessionDate = DateTime.MinValue;
            }
            else if (State == State.DataLoaded)
            {
                DateTime dt;
                if (DateTime.TryParse(RTHStartTime, out dt))
                    rthStartSpan = dt.TimeOfDay;
                else
                    rthStartSpan = new TimeSpan(10, 30, 0);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 0) return;

            // -------------------------------------------------------------
            // Primary Series: Relative Volume Logic (1-Minute or User Timeframe)
            // -------------------------------------------------------------
            if (BarsInProgress == 0)
            {
                bool justCrossed = false;

                // Session Reset Logic
                if (Bars.IsFirstBarOfSession)
                {
                    ResetStats();
                    inRTH = false;
                    
                    // Hide line from previous session if separating RTH
                    if (UseSeparateRTH)
                    {
                        PlotBrushes[1][0] = Brushes.Transparent;
                    }
                }
                // Optional RTH Reset Logic
                else if (UseSeparateRTH && CurrentBar > 0)
                {
                     DateTime t0 = Time[0];
                     DateTime t1 = Time[1];
                     DateTime rthToday = t0.Date.Add(rthStartSpan);
                     
                     // Crossing into RTH
                     if (t1 < rthToday && t0 >= rthToday)
                     {
                        ResetStats();
                        inRTH = true;
                        PlotBrushes[1][0] = Brushes.Transparent;
                        justCrossed = true;
                     }
                }

                double vol = Volume[0];
                
                // Accumulate statistics
                sumVol += vol;
                sumVolSq += (vol * vol);
                count++;

                double mean = 0;
                double stdDev = 0;
                double threshold = 0;

                if (count > 0)
                {
                    mean = sumVol / count;
                    double variance = (sumVolSq / count) - (mean * mean);
                    if (variance < 0) variance = 0;
                    stdDev = Math.Sqrt(variance);
                    
                    threshold = mean + (stdDev * NumStdDev);
                }

                // Plot values
                Values[0][0] = vol;           // VolumePlot
                Values[1][0] = threshold;     // ThresholdPlot
                
                // Threshold Coloring
                if (UseSeparateRTH)
                {
                    // Only override color if we didn't set it to transparent above
                    if (!Bars.IsFirstBarOfSession && !justCrossed)
                        PlotBrushes[1][0] = inRTH ? RTHThresholdColor : PreRTHThresholdColor;
                }

                // Visual and Alert logic
                if (vol > threshold && count > 1) 
                {
                    // Check Price Direction
                    bool isBullish = Close[0] >= Open[0];
                    
                    if (isBullish)
                        PlotBrushes[0][0] = BreakoutBullishColor;
                    else
                        PlotBrushes[0][0] = BreakoutBearishColor;
                    
                    if (SignalOnBreakout)
                    {
                        if (CurrentBar != lastAlertBar)
                        {
                            Alert("RelativeVolBreakout", Priority.High, "High Relative Volume Detected", NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav", 10, Brushes.Black, Brushes.Yellow);
                            lastAlertBar = CurrentBar;
                        }
                    }
                }
                else
                {
                    PlotBrushes[0][0] = LowVolumeColor;
                }
            }
        }
        
        private void ResetStats()
        {
            sumVol = 0;
            sumVolSq = 0;
            count = 0;
            currentSessionDate = Time[0].Date;
        }

        #region Properties
        [Range(0.1, double.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name="Num StdDev", Description="Number of Standard Deviations for threshold", Order=1, GroupName="Parameters")]
        public double NumStdDev
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Signal On Breakout", Description="Trigger alert when volume exceeds threshold", Order=2, GroupName="Parameters")]
        public bool SignalOnBreakout
        { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Separate RTH Calculation", Description="Reset calculations at RTH Start Time", Order=2, GroupName="RTH Options")]
        public bool UseSeparateRTH
        { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="RTH Start Time", Description="Time to switch colors (e.g. 10:30)", Order=3, GroupName="RTH Options")]
        public string RTHStartTime
        { get; set; }
        
        [XmlIgnore]
        [Display(Name="RTH Threshold Color", Description="Color of threshold line during RTH", Order=4, GroupName="RTH Options")]
        public Brush RTHThresholdColor
        { get; set; }

        [Browsable(false)]
        public string RTHThresholdColorSerializable
        {
            get { return Serialize.BrushToString(RTHThresholdColor); }
            set { RTHThresholdColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name="Pre-RTH Threshold Color", Description="Color of threshold line before RTH", Order=5, GroupName="RTH Options")]
        public Brush PreRTHThresholdColor
        { get; set; }

        [Browsable(false)]
        public string PreRTHThresholdColorSerializable
        {
            get { return Serialize.BrushToString(PreRTHThresholdColor); }
            set { PreRTHThresholdColor = Serialize.StringToBrush(value); }
        }

        private TimeSpan rthStartSpan; // Internal variable
        private bool inRTH; // Internal flag

        [XmlIgnore]
        [Display(Name="Breakout Bullish Color", Description="Color for high volume bars when price is bullish", Order=3, GroupName="Parameters")]
        public Brush BreakoutBullishColor
        { get; set; }

        [Browsable(false)]
        public string BreakoutBullishColorSerializable
        {
            get { return Serialize.BrushToString(BreakoutBullishColor); }
            set { BreakoutBullishColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name="Breakout Bearish Color", Description="Color for high volume bars when price is bearish", Order=4, GroupName="Parameters")]
        public Brush BreakoutBearishColor
        { get; set; }

        [Browsable(false)]
        public string BreakoutBearishColorSerializable
        {
            get { return Serialize.BrushToString(BreakoutBearishColor); }
            set { BreakoutBearishColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name="Low Volume Color", Description="Color for volume bars below threshold", Order=5, GroupName="Parameters")]
        public Brush LowVolumeColor
        { get; set; }

        [Browsable(false)]
        public string LowVolumeColorSerializable
        {
            get { return Serialize.BrushToString(LowVolumeColor); }
            set { LowVolumeColor = Serialize.StringToBrush(value); }
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RelativeIndicators.RelativeVolumen[] cacheRelativeVolumen;
		public RelativeIndicators.RelativeVolumen RelativeVolumen(double numStdDev, bool signalOnBreakout, bool useSeparateRTH, string rTHStartTime)
		{
			return RelativeVolumen(Input, numStdDev, signalOnBreakout, useSeparateRTH, rTHStartTime);
		}

		public RelativeIndicators.RelativeVolumen RelativeVolumen(ISeries<double> input, double numStdDev, bool signalOnBreakout, bool useSeparateRTH, string rTHStartTime)
		{
			if (cacheRelativeVolumen != null)
				for (int idx = 0; idx < cacheRelativeVolumen.Length; idx++)
					if (cacheRelativeVolumen[idx] != null && cacheRelativeVolumen[idx].NumStdDev == numStdDev && cacheRelativeVolumen[idx].SignalOnBreakout == signalOnBreakout && cacheRelativeVolumen[idx].UseSeparateRTH == useSeparateRTH && cacheRelativeVolumen[idx].RTHStartTime == rTHStartTime && cacheRelativeVolumen[idx].EqualsInput(input))
						return cacheRelativeVolumen[idx];
			return CacheIndicator<RelativeIndicators.RelativeVolumen>(new RelativeIndicators.RelativeVolumen(){ NumStdDev = numStdDev, SignalOnBreakout = signalOnBreakout, UseSeparateRTH = useSeparateRTH, RTHStartTime = rTHStartTime }, input, ref cacheRelativeVolumen);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RelativeIndicators.RelativeVolumen RelativeVolumen(double numStdDev, bool signalOnBreakout, bool useSeparateRTH, string rTHStartTime)
		{
			return indicator.RelativeVolumen(Input, numStdDev, signalOnBreakout, useSeparateRTH, rTHStartTime);
		}

		public Indicators.RelativeIndicators.RelativeVolumen RelativeVolumen(ISeries<double> input , double numStdDev, bool signalOnBreakout, bool useSeparateRTH, string rTHStartTime)
		{
			return indicator.RelativeVolumen(input, numStdDev, signalOnBreakout, useSeparateRTH, rTHStartTime);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RelativeIndicators.RelativeVolumen RelativeVolumen(double numStdDev, bool signalOnBreakout, bool useSeparateRTH, string rTHStartTime)
		{
			return indicator.RelativeVolumen(Input, numStdDev, signalOnBreakout, useSeparateRTH, rTHStartTime);
		}

		public Indicators.RelativeIndicators.RelativeVolumen RelativeVolumen(ISeries<double> input , double numStdDev, bool signalOnBreakout, bool useSeparateRTH, string rTHStartTime)
		{
			return indicator.RelativeVolumen(input, numStdDev, signalOnBreakout, useSeparateRTH, rTHStartTime);
		}
	}
}

#endregion
